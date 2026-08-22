using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScheduleIControlBridge
{
    [HarmonyPatch(typeof(Business), nameof(Business.MinsPass))]
    internal static class BusinessMinsPassPatch
    {
        private static void Postfix(Business __instance)
        {
            BusinessLaunderScaling.ApplyCapacity(__instance);
        }
    }

    [HarmonyPatch(typeof(Business), nameof(Business.StartLaunderingOperation))]
    internal static class BusinessStartLaunderingPatch
    {
        private static void Prefix(Business __instance)
        {
            BusinessLaunderScaling.ApplyCapacity(__instance);
        }
    }

    [HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.Initialize))]
    internal static class LaunderingInterfaceInitializePatch
    {
        private static void Postfix(LaunderingInterface __instance)
        {
            BusinessLaunderScaling.ApplyInterfaceCapacity(__instance);
        }
    }

    [HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.Open))]
    internal static class LaunderingInterfaceOpenPatch
    {
        private static void Postfix(LaunderingInterface __instance)
        {
            BusinessLaunderScaling.ApplyInterfaceCapacity(__instance);
        }
    }

    [HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.RefreshLaunderButton))]
    internal static class LaunderingInterfaceRefreshPatch
    {
        private static void Postfix(LaunderingInterface __instance)
        {
            BusinessLaunderScaling.ApplyInterfaceCapacity(__instance);
        }
    }

    internal static class BusinessLaunderScaling
    {
        public const int DefaultDailyLimit = 2000;
        public const int MinimumDailyLimit = 1;
        public const int HardMaximumDailyLimit = SellPriceLimitManager.PracticalMoneyMaximum;
        private const int ConfigVersion = 1;
        private const long MaxConfigBytes = 8 * 1024;
        private const string ConfigFileName = "ScheduleIControlBridge.launder-limits.json";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, int> ActiveLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, LaunderPreviewGroup> LaunderPreviews = new Dictionary<string, LaunderPreviewGroup>(StringComparer.OrdinalIgnoreCase);

        private static JObject root;
        private static string configPath;
        private static string activeSaveScope = string.Empty;
        private static Action<string> warn;
        private static Action<string> audit;
        private static long configRevision = 1;

        public static bool PersistenceReady { get; private set; }
        public static bool PatchActive { get; private set; }
        public static bool EligibilityActive { get; private set; }
        public static long ConfigRevision { get { return configRevision; } }
        public static string ActiveSaveScope { get { return activeSaveScope; } }
        public static int ActiveOverrideCount { get { lock (Sync) return ActiveLimits.Count; } }
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
                        throw new InvalidDataException("Launder-limit configuration exceeds 8 KiB.");

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
                            throw new InvalidDataException("Launder-limit configuration has trailing content.");
                    }
                    ValidateRoot(root);
                }
                PersistenceReady = true;
            }
            catch (Exception ex)
            {
                // Rejected or unreadable configuration is treated as a clean
                // start (same recovery as market/allowance profiles).
                root = CreateEmptyRoot();
                PersistenceReady = true;
                if (warn != null)
                    warn("Ignored invalid launder-limit configuration and started clean: " + ex.GetType().Name + ": " + ex.Message);
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

        public static bool EnsureSave(string savePath, out string error)
        {
            error = null;
            if (!PatchActive || !PersistenceReady)
            {
                error = "Launder-limit persistence or patching is not ready.";
                return false;
            }
            if (!EligibilityActive)
            {
                error = "Launder-limit overrides are not eligible in the current build, save, authority, or multiplayer state.";
                return false;
            }
            string scope = MarketValueScaling.ComputeSaveScope(savePath);
            if (scope.Length == 0)
            {
                error = "The loaded save could not be assigned a safe launder-limit scope.";
                return false;
            }

            lock (Sync)
            {
                if (string.Equals(activeSaveScope, scope, StringComparison.Ordinal))
                    return true;
                try
                {
                    ActiveLimits.Clear();
                    JObject scopeNode = root["saves"] == null ? null : root["saves"][scope] as JObject;
                    if (scopeNode != null && scopeNode["limits"] is JObject limits)
                    {
                        foreach (KeyValuePair<string, JToken> pair in limits)
                        {
                            int value = pair.Value.Value<int>();
                            if (IsValidLimit(value))
                                ActiveLimits[pair.Key] = value;
                        }
                    }
                    activeSaveScope = scope;
                    configRevision++;
                    if (audit != null)
                        audit(string.Format(CultureInfo.InvariantCulture, "op=business.launder.activate saveScope={0} overrides={1} configRevision={2}", scope, ActiveLimits.Count, configRevision));
                    return true;
                }
                catch (Exception ex)
                {
                    ActiveLimits.Clear();
                    activeSaveScope = string.Empty;
                    error = "Failed to activate launder-limit configuration: " + ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        public static bool TrySetLimits(string savePath, Dictionary<string, int> limits, out string error)
        {
            error = null;
            if (!EnsureSave(savePath, out error))
                return false;
            if (limits == null || limits.Count == 0 || limits.Count > 64)
            {
                error = "A save may contain at most 64 explicit launder-limit overrides.";
                return false;
            }

            Dictionary<string, int> normalized = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> pair in limits)
            {
                // 0 is the documented "remove override / restore native default"
                // sentinel; positive values must stay inside the valid range.
                if (!PipeServer.IsSafeIdentifier(pair.Key, 48)
                    || (pair.Value != 0 && !IsValidLimit(pair.Value)))
                {
                    error = "Launder-limit keys or values were invalid; limits must be 0 (restore native) or a whole number from 1 to 16777215.";
                    return false;
                }
                normalized[pair.Key] = pair.Value;
            }

            lock (Sync)
            {
                JObject priorRoot = root == null ? null : (JObject)root.DeepClone();
                JObject candidate = (JObject)root.DeepClone();
                JObject saves = candidate["saves"] as JObject ?? new JObject();
                JObject scopeNode = saves[activeSaveScope] as JObject ?? new JObject();
                JObject limitsNode = scopeNode["limits"] as JObject ?? new JObject();
                foreach (KeyValuePair<string, int> pair in normalized)
                {
                    if (pair.Value == 0)
                        limitsNode.Remove(pair.Key);
                    else
                        limitsNode[pair.Key] = pair.Value;
                }
                scopeNode["limits"] = limitsNode;
                saves[activeSaveScope] = scopeNode;
                candidate["saves"] = saves;
                candidate["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

                try
                {
                    SaveRootAtomically(candidate);
                    root = candidate;
                    ActiveLimits.Clear();
                    foreach (KeyValuePair<string, int> pair in normalized)
                    {
                        if (pair.Value != 0)
                            ActiveLimits[pair.Key] = pair.Value;
                    }
                    configRevision++;
                    ApplyAllCapacities();
                    if (audit != null)
                        audit(string.Format(CultureInfo.InvariantCulture, "op=business.launder.apply saveScope={0} count={1} configRevision={2}", activeSaveScope, normalized.Count, configRevision));
                    return true;
                }
                catch (Exception ex)
                {
                    root = priorRoot ?? CreateEmptyRoot();
                    try { SaveRootAtomically(root); } catch { }
                    error = "Launder-limit apply failed and was rolled back: " + ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        public static int GetLimit(string businessCode)
        {
            lock (Sync)
            {
                int value;
                return !string.IsNullOrEmpty(businessCode) && ActiveLimits.TryGetValue(businessCode, out value) ? value : DefaultDailyLimit;
            }
        }

        public static bool IsOverridden(string businessCode)
        {
            lock (Sync)
                return !string.IsNullOrEmpty(businessCode) && ActiveLimits.ContainsKey(businessCode);
        }

        public static float RemainingFor(Business business)
        {
            if (business == null || string.IsNullOrEmpty(business.propertyCode))
                return 0f;
            int limit = GetLimit(business.propertyCode);
            if (!IsOverridden(business.propertyCode))
                return 0f;
            float remaining = limit - business.currentLaunderTotal;
            return remaining < 0f ? 0f : remaining;
        }

        public static void ApplyCapacity(Business business)
        {
            if (!PatchActive || !EligibilityActive || business == null)
                return;
            try
            {
                if (!business.IsOwned || !IsOverridden(business.propertyCode))
                    return;
                business.LaunderCapacity = RemainingFor(business);
            }
            catch
            {
                // Capacity application is best-effort; the next game minute retries.
            }
        }

        public static void ApplyInterfaceCapacity(LaunderingInterface ui)
        {
            if (!PatchActive || !EligibilityActive || ui == null)
                return;
            try
            {
                Business business = ui.Business;
                if (business == null)
                    return;
                ApplyCapacity(business);
            }
            catch
            {
            }
        }

        public static void ApplyAllCapacities()
        {
            try
            {
                foreach (Business business in Business.OwnedBusinesses)
                    ApplyCapacity(business);
            }
            catch
            {
            }
        }

        public static void ClearManagedState()
        {
            EligibilityActive = false;
        }

        public static LaunderPreviewGroup CreateGroupPreview(Dictionary<string, int> targets, out string error)
        {
            error = null;
            if (targets == null || targets.Count == 0 || targets.Count > 64)
            {
                error = "Launder-limit previews require 1-64 business targets.";
                return null;
            }
            LaunderPreviewGroup group = new LaunderPreviewGroup { Id = Guid.NewGuid().ToString("N") };
            foreach (KeyValuePair<string, int> pair in targets)
            {
                string businessCode = pair.Key;
                int limit = pair.Value;
                if (!PipeServer.IsSafeIdentifier(businessCode, 48) || (limit != 0 && !IsValidLimit(limit)))
                {
                    error = "Launder-limit targets need a safe business code and a limit of 0 (restore native) or a whole number from 1 to 16777215.";
                    return null;
                }
                Business business = FindBusiness(businessCode);
                if (business == null)
                {
                    error = "No owned business matched the requested launder-limit code: " + businessCode;
                    return null;
                }
                group.Previews.Add(new LaunderPreview
                {
                    BusinessCode = businessCode,
                    OldLimit = GetLimit(businessCode),
                    NewLimit = limit,
                    CurrentTotal = business.currentLaunderTotal,
                    CapacityAfter = Math.Max(0f, limit - business.currentLaunderTotal)
                });
            }
            lock (Sync)
            {
                LaunderPreviews[group.Id] = group;
                if (LaunderPreviews.Count > 32)
                {
                    string oldest = null;
                    DateTime oldestTime = DateTime.MaxValue;
                    foreach (KeyValuePair<string, LaunderPreviewGroup> pair in LaunderPreviews)
                    {
                        if (pair.Value.CreatedUtc < oldestTime)
                        {
                            oldestTime = pair.Value.CreatedUtc;
                            oldest = pair.Key;
                        }
                    }
                    if (oldest != null)
                        LaunderPreviews.Remove(oldest);
                }
            }
            return group;
        }

        public static LaunderPreviewGroup TakePreview(string previewId, out string error)
        {
            error = null;
            if (!PipeServer.IsSafeIdentifier(previewId, 64))
            {
                error = "previewId is invalid.";
                return null;
            }
            lock (Sync)
            {
                LaunderPreviewGroup preview;
                if (!LaunderPreviews.TryGetValue(previewId, out preview))
                {
                    error = "Launder-limit preview was not found or has expired.";
                    return null;
                }
                LaunderPreviews.Remove(previewId);
                return preview;
            }
        }

        public static bool ApplyGroupPreview(LaunderPreviewGroup group, string savePath, out string error)
        {
            error = null;
            if (group == null || group.Previews == null || group.Previews.Count == 0)
            {
                error = "The launder-limit preview was empty.";
                return false;
            }
            Dictionary<string, int> targets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (LaunderPreview preview in group.Previews)
                targets[preview.BusinessCode] = preview.NewLimit;
            return TrySetLimits(savePath, targets, out error);
        }

        public static Dictionary<string, int> SnapshotLimits()
        {
            lock (Sync)
                return new Dictionary<string, int>(ActiveLimits, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsValidLimit(int value)
        {
            return value >= MinimumDailyLimit && value <= HardMaximumDailyLimit;
        }

        private static Business FindBusiness(string businessCode)
        {
            try
            {
                foreach (Business business in Business.OwnedBusinesses)
                {
                    if (business != null && string.Equals(business.propertyCode, businessCode, StringComparison.OrdinalIgnoreCase))
                        return business;
                }
            }
            catch
            {
            }
            return null;
        }

        private static JObject CreateEmptyRoot()
        {
            return new JObject
            {
                ["version"] = ConfigVersion,
                ["gameVersion"] = GameOperations.ExpectedGameVersion,
                ["gameBuild"] = GameOperations.ExpectedGameBuild,
                ["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["saves"] = new JObject()
            };
        }

        private static void ValidateRoot(JObject candidate)
        {
            if (candidate == null
                || candidate.Value<int?>("version") != ConfigVersion
                || !string.Equals(candidate.Value<string>("gameVersion"), GameOperations.ExpectedGameVersion, StringComparison.Ordinal)
                || !string.Equals(candidate.Value<string>("gameBuild"), GameOperations.ExpectedGameBuild, StringComparison.Ordinal))
                throw new InvalidDataException("Launder-limit configuration version/build does not match this bridge.");
        }

        private static void SaveRootAtomically(JObject value)
        {
            string serialized = value.ToString(Formatting.Indented);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(serialized);
            if (bytes.LongLength > MaxConfigBytes)
                throw new InvalidDataException("Launder-limit configuration would exceed 8 KiB.");
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

    internal sealed class LaunderPreview
    {
        public string BusinessCode;
        public int OldLimit;
        public int NewLimit;
        public float CurrentTotal;
        public float CapacityAfter;

        public JObject ToJson()
        {
            return new JObject
            {
                ["businessCode"] = BusinessCode,
                ["oldLimit"] = OldLimit,
                ["newLimit"] = NewLimit,
                ["currentTotal"] = CurrentTotal,
                ["capacityAfter"] = CapacityAfter
            };
        }
    }

    internal sealed class LaunderPreviewGroup
    {
        public readonly DateTime CreatedUtc = DateTime.UtcNow;
        public string Id;
        public readonly List<LaunderPreview> Previews = new List<LaunderPreview>();
    }
}
