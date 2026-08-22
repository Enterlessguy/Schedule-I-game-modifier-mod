using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppScheduleOne.Product;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScheduleIControlBridge
{
    [HarmonyPatch(typeof(ProductManager), nameof(ProductManager.CalculateProductValue), new Type[] { typeof(ProductDefinition), typeof(float) })]
    internal static class ProductValuePatch
    {
        private static void Postfix(ProductDefinition product, ref float __result)
        {
            if (product == null || MarketValueScaling.SuppressPatch || !MarketValueScaling.EligibilityActive)
                return;

            float factor = MarketValueScaling.GetFactor(product.ID);
            if (Math.Abs(factor - 1f) <= 0.0001f)
                return;

            float scaled = __result * factor;
            if (!float.IsNaN(scaled) && !float.IsInfinity(scaled) && scaled >= 0f && scaled <= MarketValueScaling.MaxMarketValue)
                __result = scaled;
        }
    }

    internal static class MarketValueScaling
    {
        public const float MinFactor = 0.1f;
        public const float MaxFactor = 1000000f;
        public const float MaxMarketValue = SellPriceLimitManager.PracticalMoneyMaximum;
        private const int ConfigVersion = 1;
        private const long MaxConfigBytes = 64 * 1024;
        private const string ConfigFileName = "ScheduleIControlBridge.market-values.json";

        [ThreadStatic]
        private static bool suppressPatch;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, float> ActiveFactors = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private static JObject root;
        private static string configPath;
        private static string activeSaveScope = string.Empty;
        private static Action<string> warn;
        private static Action<string> audit;
        private static long configRevision = 1;
        private static IntPtr activeManagerPointer = IntPtr.Zero;

        public static bool SuppressPatch { get { return suppressPatch; } }
        public static bool PatchActive { get; private set; }
        public static bool EligibilityActive { get; private set; }
        public static bool PersistenceReady { get; private set; }
        public static long ConfigRevision { get { return configRevision; } }
        public static string ActiveSaveScope { get { return activeSaveScope; } }
        public static int ActiveOverrideCount { get { lock (Sync) return ActiveFactors.Count; } }
        public static string ConfigPath { get { return configPath ?? string.Empty; } }

        public static void Initialize(Action<string> warningSink, Action<string> auditSink)
        {
            warn = warningSink;
            audit = auditSink;
            configPath = Path.Combine(MelonEnvironment.UserDataDirectory, ConfigFileName);
            root = CreateEmptyRoot();

            try
            {
                Directory.CreateDirectory(MelonEnvironment.UserDataDirectory);
                if (File.Exists(configPath))
                {
                    FileInfo info = new FileInfo(configPath);
                    if (info.Length > MaxConfigBytes)
                        throw new InvalidDataException("Market-value configuration exceeds 64 KiB.");

                    JsonLoadSettings settings = new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        CommentHandling = CommentHandling.Ignore,
                        LineInfoHandling = LineInfoHandling.Ignore
                    };
                    using (StreamReader reader = new StreamReader(configPath, new UTF8Encoding(false, true)))
                    using (JsonTextReader json = new JsonTextReader(reader) { DateParseHandling = DateParseHandling.None })
                    {
                        root = JObject.Load(json, settings);
                        if (json.Read())
                            throw new InvalidDataException("Market-value configuration has trailing content.");
                    }
                    ValidateRoot(root);
                }

                PersistenceReady = true;
            }
            catch (Exception ex)
            {
                // Rejected or unreadable configuration is treated as a clean
                // start: an empty root with persistence ready. The invalid file
                // is left untouched on disk and is replaced atomically on the
                // next successful apply. This also recovers automatically when
                // an older build-scoped profile is rejected after a game update.
                root = CreateEmptyRoot();
                PersistenceReady = true;
                if (warn != null)
                    warn("Ignored invalid market-value configuration and started clean: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void SetPatchActive(bool value)
        {
            PatchActive = value;
        }

        public static void SetEligibility(bool value)
        {
            EligibilityActive = value;
        }

        public static bool IsActiveFor(string savePath, ProductManager manager)
        {
            if (!EligibilityActive || manager == null)
                return false;
            string scope = ComputeSaveScope(savePath);
            return scope.Length > 0
                && string.Equals(activeSaveScope, scope, StringComparison.Ordinal)
                && activeManagerPointer == manager.Pointer;
        }

        public static string ComputeSaveScope(string savePath)
        {
            string normalized = (savePath ?? string.Empty).Trim().Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
            if (normalized.Length == 0)
                return string.Empty;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                StringBuilder result = new StringBuilder(32);
                for (int i = 0; i < 16; i++)
                    result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        public static bool EnsureSave(string savePath, ProductManager manager, out string error)
        {
            error = null;
            if (!PatchActive || !PersistenceReady)
            {
                error = "Market-value persistence or patching is not ready.";
                return false;
            }
            if (!EligibilityActive)
            {
                error = "Fair-market overrides are not eligible in the current build, save, authority, or multiplayer state.";
                return false;
            }
            if (manager == null || manager.AllProducts == null)
            {
                error = "ProductManager is not ready for market-value synchronization.";
                return false;
            }

            string scope = ComputeSaveScope(savePath);
            if (scope.Length == 0)
            {
                error = "The loaded save could not be assigned a safe market-value scope.";
                return false;
            }

            lock (Sync)
            {
                if (string.Equals(activeSaveScope, scope, StringComparison.Ordinal) && activeManagerPointer == manager.Pointer)
                    return true;

                try
                {
                    ActiveFactors.Clear();
                    RefreshProducts(manager);
                    LoadFactorsForScope(scope, ActiveFactors);
                    activeSaveScope = scope;
                    activeManagerPointer = manager.Pointer;
                    RefreshProducts(manager);
                    configRevision++;
                    if (audit != null)
                        audit(string.Format(CultureInfo.InvariantCulture, "op=product.market.activate saveScope={0} overrides={1} configRevision={2}", scope, ActiveFactors.Count, configRevision));
                    return true;
                }
                catch (Exception ex)
                {
                    ActiveFactors.Clear();
                    activeSaveScope = string.Empty;
                    activeManagerPointer = IntPtr.Zero;
                    error = "Failed to activate market-value configuration: " + ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        public static float GetFactor(string productId)
        {
            if (!EligibilityActive || string.IsNullOrEmpty(productId))
                return 1f;
            lock (Sync)
            {
                float factor;
                return ActiveFactors.TryGetValue(productId, out factor) ? factor : 1f;
            }
        }

        public static Dictionary<string, float> SnapshotFactors()
        {
            lock (Sync)
                return new Dictionary<string, float>(ActiveFactors, StringComparer.OrdinalIgnoreCase);
        }

        public static float CalculateVanilla(ProductDefinition product)
        {
            if (product == null)
                return 0f;
            bool previous = suppressPatch;
            suppressPatch = true;
            try
            {
                return ProductManager.CalculateProductValue(product, product.BasePrice);
            }
            finally
            {
                suppressPatch = previous;
            }
        }

        public static bool TrySetFactors(
            string savePath,
            Dictionary<string, float> factors,
            ProductManager manager,
            out string error)
        {
            error = null;
            if (!EligibilityActive || !EnsureSave(savePath, manager, out error))
                return false;
            if (factors == null || factors.Count > 64)
            {
                error = "A save may contain at most 64 explicit market-value overrides.";
                return false;
            }

            Dictionary<string, float> normalizedFactors = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, float> pair in factors)
            {
                if (!PipeServer.IsSafeIdentifier(pair.Key, 64)
                    || float.IsNaN(pair.Value)
                    || float.IsInfinity(pair.Value)
                    || pair.Value < MinFactor
                    || pair.Value > MaxFactor)
                {
                    error = "Market-value override keys or factors were invalid.";
                    return false;
                }
                if (Math.Abs(pair.Value - 1f) > 0.0001f)
                    normalizedFactors[pair.Key] = pair.Value;
            }

            lock (Sync)
            {
                JObject oldRoot = (JObject)root.DeepClone();
                Dictionary<string, float> oldFactors = new Dictionary<string, float>(ActiveFactors, StringComparer.OrdinalIgnoreCase);
                try
                {
                    WriteFactorsToRoot(activeSaveScope, normalizedFactors);
                    SaveRootAtomically(root);
                    ActiveFactors.Clear();
                    foreach (KeyValuePair<string, float> pair in normalizedFactors)
                        ActiveFactors[pair.Key] = pair.Value;
                    RefreshProducts(manager);
                    configRevision++;
                    return true;
                }
                catch (Exception ex)
                {
                    root = oldRoot;
                    ActiveFactors.Clear();
                    foreach (KeyValuePair<string, float> pair in oldFactors)
                        ActiveFactors[pair.Key] = pair.Value;
                    try
                    {
                        SaveRootAtomically(root);
                        RefreshProducts(manager);
                    }
                    catch (Exception rollbackEx)
                    {
                        error = "Market-value apply failed and rollback was incomplete: " + rollbackEx.Message;
                        return false;
                    }
                    error = "Market-value apply failed and was rolled back: " + ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        public static bool RefreshActive(ProductManager manager, out string error)
        {
            error = null;
            if (!EligibilityActive || manager == null || manager.AllProducts == null || activeManagerPointer != manager.Pointer)
            {
                error = "The active fair-market product graph is unavailable.";
                return false;
            }
            lock (Sync)
            {
                try
                {
                    RefreshProducts(manager);
                    return true;
                }
                catch (Exception ex)
                {
                    error = "Failed to refresh active fair-market values: " + ex.Message;
                    return false;
                }
            }
        }

        public static void RestoreCurrent(ProductManager manager)
        {
            if (manager == null || manager.AllProducts == null)
                return;
            lock (Sync)
            {
                ActiveFactors.Clear();
                activeSaveScope = string.Empty;
                activeManagerPointer = IntPtr.Zero;
                RefreshProducts(manager);
            }
        }

        public static void Deactivate(ProductManager manager)
        {
            lock (Sync)
            {
                EligibilityActive = false;
                ActiveFactors.Clear();
                activeSaveScope = string.Empty;
                activeManagerPointer = IntPtr.Zero;
                if (manager != null && manager.AllProducts != null)
                    RefreshProducts(manager);
            }
        }

        public static void ClearManagedState()
        {
            lock (Sync)
            {
                EligibilityActive = false;
                ActiveFactors.Clear();
                activeSaveScope = string.Empty;
                activeManagerPointer = IntPtr.Zero;
            }
        }

        private static void RefreshProducts(ProductManager manager)
        {
            var all = manager.AllProducts;
            for (int i = 0; i < all.Count && i < 512; i++)
            {
                ProductDefinition product = all[i];
                if (product == null || !PipeServer.IsSafeIdentifier(product.ID, 64))
                    continue;
                float value = ProductManager.CalculateProductValue(product, product.BasePrice);
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > MaxMarketValue)
                    throw new InvalidDataException("Calculated market value was invalid for " + product.ID + ".");
                product.MarketValue = value;
            }
        }

        private static JObject CreateEmptyRoot()
        {
            return new JObject
            {
                ["version"] = ConfigVersion,
                ["gameVersion"] = GameOperations.ExpectedGameVersion,
                ["gameBuild"] = GameOperations.ExpectedGameBuild,
                ["saves"] = new JObject()
            };
        }

        private static void ValidateRoot(JObject candidate)
        {
            if (candidate == null
                || candidate.Value<int?>("version") != ConfigVersion
                || !string.Equals(candidate.Value<string>("gameVersion"), GameOperations.ExpectedGameVersion, StringComparison.Ordinal)
                || !string.Equals(candidate.Value<string>("gameBuild"), GameOperations.ExpectedGameBuild, StringComparison.Ordinal))
                throw new InvalidDataException("Market-value configuration version/build does not match this bridge.");

            JObject saves = candidate["saves"] as JObject;
            if (saves == null || saves.Count > 64)
                throw new InvalidDataException("Market-value save scopes are missing or exceed the 64-scope limit.");
            foreach (JProperty save in saves.Properties())
            {
                if (!PipeServer.IsSafeIdentifier(save.Name, 64))
                    throw new InvalidDataException("Market-value save scope key is invalid.");
                JObject scope = save.Value as JObject;
                JObject factors = scope == null ? null : scope["factors"] as JObject;
                if (factors == null || factors.Count > 64)
                    throw new InvalidDataException("Market-value factor collection is invalid.");
                HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JProperty factor in factors.Properties())
                {
                    float? value = factor.Value.Value<float?>();
                    if (!PipeServer.IsSafeIdentifier(factor.Name, 64)
                        || !seenIds.Add(factor.Name)
                        || (factor.Value.Type != JTokenType.Integer && factor.Value.Type != JTokenType.Float)
                        || !value.HasValue
                        || float.IsNaN(value.Value)
                        || float.IsInfinity(value.Value)
                        || value.Value < MinFactor
                        || value.Value > MaxFactor)
                        throw new InvalidDataException("Market-value factor entry is invalid.");
                }
            }
        }

        private static void LoadFactorsForScope(string scope, Dictionary<string, float> destination)
        {
            JObject saves = root["saves"] as JObject;
            JObject save = saves == null ? null : saves[scope] as JObject;
            JObject factors = save == null ? null : save["factors"] as JObject;
            if (factors == null)
                return;
            foreach (JProperty pair in factors.Properties())
                destination[pair.Name] = pair.Value.Value<float>();
        }

        private static void WriteFactorsToRoot(string scope, Dictionary<string, float> factors)
        {
            JObject values = new JObject();
            foreach (KeyValuePair<string, float> pair in factors)
                values[pair.Key] = pair.Value;
            JObject saves = (JObject)root["saves"];
            if (saves[scope] == null && saves.Count >= 64)
                throw new InvalidDataException("Market-value configuration already contains the maximum 64 save scopes.");
            saves[scope] = new JObject
            {
                ["factors"] = values,
                ["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        private static void SaveRootAtomically(JObject value)
        {
            string serialized = value.ToString(Formatting.Indented);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(serialized);
            if (bytes.LongLength > MaxConfigBytes)
                throw new InvalidDataException("Market-value configuration would exceed 64 KiB.");

            string temp = configPath + ".tmp";
            string backup = configPath + ".bak";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(configPath))
                File.Replace(temp, configPath, backup, true);
            else
                File.Move(temp, configPath);
        }
    }
}
