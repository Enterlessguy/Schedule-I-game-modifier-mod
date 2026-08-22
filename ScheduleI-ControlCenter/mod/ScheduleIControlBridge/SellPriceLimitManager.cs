using System;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Handover;
using Il2CppScheduleOne.UI.Phone;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScheduleIControlBridge
{
    // Product prices are transmitted as IEEE-754 singles. 16,777,215 is a
    // conservative whole-dollar ceiling immediately below the point where
    // consecutive integers cease to be exactly representable.
    [HarmonyPatch(typeof(ProductManager), nameof(ProductManager.SendPrice), new Type[] { typeof(string), typeof(float) })]
    internal static class ProductSendPricePatch
    {
        private static bool Prefix(ProductManager __instance, string __0, float __1)
        {
            if (!SellPriceLimitManager.ShouldOverrideUnitPrices)
                return true;
            SellPriceLimitManager.ApplyUncappedUnitPrice(__instance, __0, __1);
            return false;
        }
    }

    // Schedule I 0.4.6f13 retains the counteroffer's shared AmountSelector path
    // entry points and the HandoverScreenPriceSelector component with a shared
    // AmountSelector. The deal-total limit is therefore enforced at the final
    // commit points: CounterofferInterface.Send and HandoverScreen.PriceChanged
    // / DonePressed, where the selected amount is clamped before native code
    // uses it.
    [HarmonyPatch(typeof(CounterofferInterface), nameof(CounterofferInterface.Send))]
    internal static class CounterofferSendPatch
    {
        private static bool Prefix(CounterofferInterface __instance)
        {
            if (!SellPriceLimitManager.ShouldOverride || __instance == null)
                return true;
            SellPriceLimitManager.ClampPriceSelector(__instance.PriceSelector);
            return true;
        }
    }

    [HarmonyPatch(typeof(HandoverScreen), nameof(HandoverScreen.PriceChanged))]
    internal static class HandoverPriceChangedPatch
    {
        private static bool Prefix(HandoverScreen __instance, ref float __0)
        {
            if (!SellPriceLimitManager.ShouldOverride || __instance == null)
                return true;
            __0 = SellPriceLimitManager.ClampDealTotal(__0);
            SellPriceLimitManager.ClampPriceSelector(__instance.PriceSelector);
            return true;
        }
    }

    [HarmonyPatch(typeof(HandoverScreen), nameof(HandoverScreen.DonePressed))]
    internal static class HandoverDonePressedPatch
    {
        private static bool Prefix(HandoverScreen __instance)
        {
            if (!SellPriceLimitManager.ShouldOverride || __instance == null)
                return true;
            SellPriceLimitManager.ClampPriceSelector(__instance.PriceSelector);
            return true;
        }
    }

    internal static class SellPriceLimitManager
    {
        public const int ReviewedDefaultUnitPriceMin = 1;
        public const int ReviewedDefaultUnitPriceMax = 999;
        public const int ReviewedDefaultDealTotalMax = 9999;
        public const int PracticalMoneyMaximum = 16777215;
        public const int HardMaximumDealTotal = PracticalMoneyMaximum;
        private const int ConfigVersion = 1;
        private const long MaxConfigBytes = 8 * 1024;
        private const string ConfigFileName = "ScheduleIControlBridge.sell-price-limit.json";

        private static JObject root;
        private static string configPath;
        private static Action<string> warn;
        private static Action<string> audit;
        private static bool dealTotalOverrideEnabled;
        private static int configuredDealTotalMax = ReviewedDefaultDealTotalMax;
        private static long configRevision = 1;

        public static bool PersistenceReady { get; private set; }
        public static bool PatchActive { get; private set; }
        public static bool EligibilityActive { get; private set; }
        public static bool DealTotalOverrideEnabled { get { return dealTotalOverrideEnabled; } }
        public static int ConfiguredDealTotalMax { get { return configuredDealTotalMax; } }
        public static long ConfigRevision { get { return configRevision; } }
        public static string ConfigPath { get { return configPath ?? string.Empty; } }
        public static int CurrentUnitPriceMin { get { return ReviewedDefaultUnitPriceMin; } }
        public static int CurrentUnitPriceMax { get { return ShouldOverrideUnitPrices ? PracticalMoneyMaximum : ReviewedDefaultUnitPriceMax; } }
        // The native IL2CPP methods inline their 9,999 constants, so changing the generated
        // static-field wrappers cannot change those clamp sites.  More importantly, touching
        // those wrappers after a save becomes ready has proven unsafe on the reviewed build.
        // The four Harmony replacements are therefore the sole enforcement path.
        public static float CurrentCounterofferMax { get { return EffectiveMaxDealTotal; } }
        public static float CurrentHandoverMax { get { return EffectiveMaxDealTotal; } }
        public static bool ShouldOverride { get { return PatchActive && EligibilityActive && dealTotalOverrideEnabled; } }
        public static bool ShouldOverrideUnitPrices { get { return PatchActive && EligibilityActive; } }
        public static float EffectiveMaxDealTotal { get { return ShouldOverride ? configuredDealTotalMax : ReviewedDefaultDealTotalMax; } }

        public static bool OverrideApplied
        {
            get
            {
                return ShouldOverride;
            }
        }

        public static void Initialize(Action<string> warningSink, Action<string> auditSink)
        {
            warn = warningSink;
            audit = auditSink;
            configPath = Path.Combine(MelonEnvironment.UserDataDirectory, ConfigFileName);
            root = CreateRoot(false, ReviewedDefaultDealTotalMax);
            dealTotalOverrideEnabled = false;
            configuredDealTotalMax = ReviewedDefaultDealTotalMax;

            try
            {
                Directory.CreateDirectory(MelonEnvironment.UserDataDirectory);
                if (File.Exists(configPath))
                {
                    FileInfo info = new FileInfo(configPath);
                    if (info.Length > MaxConfigBytes)
                        throw new InvalidDataException("Sell-price-limit configuration exceeds 8 KiB.");
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
                            throw new InvalidDataException("Sell-price-limit configuration has trailing content.");
                    }
                    ValidateRoot(root);
                    dealTotalOverrideEnabled = root.Value<bool>("overrideEnabled");
                    configuredDealTotalMax = root.Value<int>("maxDealTotal");
                }
                PersistenceReady = true;
            }
            catch (Exception ex)
            {
                root = CreateRoot(false, ReviewedDefaultDealTotalMax);
                dealTotalOverrideEnabled = false;
                configuredDealTotalMax = ReviewedDefaultDealTotalMax;
                PersistenceReady = false;
                if (warn != null)
                    warn("Sell-price-limit persistence is disabled: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void SetPatchActive(bool value)
        {
            PatchActive = value;
        }

        public static void SetEligibility(bool value)
        {
            if (EligibilityActive == value)
                return;
            EligibilityActive = value;
            if (!value)
                RestoreReviewedDefault();
        }

        public static bool EnsureApplied(out string error)
        {
            error = null;
            if (!EligibilityActive)
            {
                error = "The deal-total maximum is not eligible in the current build, save, authority, or multiplayer state.";
                return false;
            }
            if (!PersistenceReady || !PatchActive)
            {
                error = "Deal-total-limit persistence or patching is unavailable.";
                return false;
            }
            // No native field write is required. The patched counteroffer, handover,
            // and product-price methods enforce the active policy at their call sites.
            return true;
        }

        public static void ApplyUncappedUnitPrice(ProductManager manager, string productId, float price)
        {
            if (manager == null || string.IsNullOrWhiteSpace(productId)
                || float.IsNaN(price) || float.IsInfinity(price)
                || price < 1f || price > PracticalMoneyMaximum)
            {
                if (warn != null)
                    warn("Rejected an invalid uncapped unit-price submission.");
                return;
            }

            try
            {
                // The bridge is solo-host gated. Calling the host-side SetPrice RPC
                // directly preserves the normal product dictionary/save update while
                // bypassing SendPrice's reviewed $999 validation.
                manager.SetPrice(null, productId, price);
            }
            catch (Exception ex)
            {
                if (warn != null)
                    warn("Uncapped unit-price submission failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static bool TrySetOverride(bool enabled, int maxDealTotal, out string error)
        {
            error = null;
            if (!EligibilityActive || !PersistenceReady || !PatchActive)
            {
                error = "Deal-total-limit control is not ready.";
                return false;
            }
            int normalized = enabled ? maxDealTotal : ReviewedDefaultDealTotalMax;
            if (normalized < ReviewedDefaultDealTotalMax || normalized > HardMaximumDealTotal)
            {
                error = string.Format(CultureInfo.InvariantCulture, "Maximum deal total must be a whole number between {0} and {1}.", ReviewedDefaultDealTotalMax, HardMaximumDealTotal);
                return false;
            }

            JObject priorRoot = root == null ? null : (JObject)root.DeepClone();
            bool priorEnabled = dealTotalOverrideEnabled;
            int priorConfigured = configuredDealTotalMax;
            JObject candidate = CreateRoot(enabled, normalized);
            try
            {
                SaveRootAtomically(candidate);
                root = candidate;
                dealTotalOverrideEnabled = enabled;
                configuredDealTotalMax = normalized;
                string applyError;
                if (!EnsureApplied(out applyError))
                    throw new InvalidOperationException(applyError);
                configRevision++;
                if (audit != null)
                    audit(string.Format(CultureInfo.InvariantCulture, "op=sale.dealLimit.apply enabled={0} maxDealTotal={1} configRevision={2}", enabled, normalized, configRevision));
                return true;
            }
            catch (Exception ex)
            {
                root = priorRoot ?? CreateRoot(priorEnabled, priorConfigured);
                dealTotalOverrideEnabled = priorEnabled;
                configuredDealTotalMax = priorConfigured;
                try
                {
                    SaveRootAtomically(root);
                    EnsureApplied(out error);
                }
                catch (Exception rollbackEx)
                {
                    error = "Deal-total-limit apply failed and rollback was incomplete: " + rollbackEx.Message;
                    return false;
                }
                error = "Deal-total-limit apply failed and was rolled back: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public static float ClampDealTotal(float value)
        {
            if (float.IsNaN(value) || float.IsNegativeInfinity(value))
                return 1f;
            if (float.IsPositiveInfinity(value))
                return EffectiveMaxDealTotal;
            return Math.Max(1f, Math.Min(EffectiveMaxDealTotal, value));
        }

        public static bool TryParsePrice(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static string FormatPrice(float value)
        {
            return value.ToString("0.##", CultureInfo.CurrentCulture);
        }

        public static void ClampPriceSelector(AmountSelector selector)
        {
            if (selector == null)
                return;
            float max = EffectiveMaxDealTotal;
            if (selector.MaxValue < max)
                selector.MaxValue = max;
            float clamped = ClampDealTotal(selector.SelectedAmount);
            if (clamped != selector.SelectedAmount)
                selector.SetAmount(clamped);
        }

        public static void RestoreReviewedDefault()
        {
            // Disabling ShouldOverride makes every prefix defer to the untouched native
            // implementation, whose reviewed limit remains 9,999.
        }

        public static void ClearManagedState()
        {
            EligibilityActive = false;
        }

        private static JObject CreateRoot(bool enabled, int maxDealTotal)
        {
            return new JObject
            {
                ["version"] = ConfigVersion,
                ["gameVersion"] = GameOperations.ExpectedGameVersion,
                ["gameBuild"] = GameOperations.ExpectedGameBuild,
                ["overrideEnabled"] = enabled,
                ["maxDealTotal"] = maxDealTotal,
                ["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        private static void ValidateRoot(JObject candidate)
        {
            if (candidate == null
                || candidate.Value<int?>("version") != ConfigVersion
                || !string.Equals(candidate.Value<string>("gameVersion"), GameOperations.ExpectedGameVersion, StringComparison.Ordinal)
                || !string.Equals(candidate.Value<string>("gameBuild"), GameOperations.ExpectedGameBuild, StringComparison.Ordinal))
                throw new InvalidDataException("Sell-price-limit configuration version/build does not match this bridge.");
            JToken enabledToken = candidate["overrideEnabled"];
            JToken maxToken = candidate["maxDealTotal"];
            int? max = maxToken == null ? null : maxToken.Value<int?>();
            if (enabledToken == null || enabledToken.Type != JTokenType.Boolean
                || maxToken == null || maxToken.Type != JTokenType.Integer
                || !max.HasValue
                || max.Value < ReviewedDefaultDealTotalMax
                || max.Value > HardMaximumDealTotal)
                throw new InvalidDataException("Sell-price-limit configuration values are invalid.");
        }

        private static void SaveRootAtomically(JObject value)
        {
            string serialized = value.ToString(Formatting.Indented);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(serialized);
            if (bytes.LongLength > MaxConfigBytes)
                throw new InvalidDataException("Sell-price-limit configuration would exceed 8 KiB.");
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
