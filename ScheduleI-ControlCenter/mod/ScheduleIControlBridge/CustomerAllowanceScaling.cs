using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScheduleIControlBridge
{
    [HarmonyPatch(typeof(CustomerData), nameof(CustomerData.GetAdjustedWeeklySpend), new Type[] { typeof(float) })]
    internal static class CustomerWeeklySpendPatch
    {
        private static void Postfix(CustomerData __instance, float normalizedRelationship, ref float __result)
        {
            CustomerAllowanceScaling.Apply(__instance, normalizedRelationship, ref __result);
        }
    }

    internal sealed class AllowanceRange
    {
        public float MinWeeklySpend;
        public float MaxWeeklySpend;

        public AllowanceRange Clone()
        {
            return new AllowanceRange
            {
                MinWeeklySpend = MinWeeklySpend,
                MaxWeeklySpend = MaxWeeklySpend
            };
        }
    }

    internal sealed class LiveCustomerAllowance
    {
        public Customer Customer;
        public CustomerData Data;
        public string Id;
        public string Name;
        public bool Unlocked;
        public float OriginalMinWeeklySpend;
        public float OriginalMaxWeeklySpend;
        public float CurrentMinWeeklySpend;
        public float CurrentMaxWeeklySpend;
    }

    internal static class CustomerAllowanceScaling
    {
        public const float MinWeeklySpend = 0f;
        public const float MaxWeeklySpend = SellPriceLimitManager.PracticalMoneyMaximum;
        public const int MaxCustomersPerSave = 128;
        public const int MaxManualTargetsPerRequest = 96;
        public const float HardOfferLimitMultiplier = 3f;

        private const int ConfigVersion = 1;
        private const long MaxConfigBytes = 128 * 1024;
        private const string ConfigFileName = "ScheduleIControlBridge.customer-allowances.json";
        private const float Tolerance = 0.001f;

        [ThreadStatic]
        private static bool suppressPatch;

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, AllowanceRange> ActiveRanges =
            new Dictionary<string, AllowanceRange>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<IntPtr, AllowanceRange> ActivePointerRanges =
            new Dictionary<IntPtr, AllowanceRange>();
        private static readonly Dictionary<string, LiveCustomerAllowance> CustomerGraph =
            new Dictionary<string, LiveCustomerAllowance>(StringComparer.OrdinalIgnoreCase);

        private static JObject root;
        private static string configPath;
        private static string activeSaveScope = string.Empty;
        private static string activeGraphSignature = string.Empty;
        private static Action<string> warn;
        private static Action<string> audit;
        private static long configRevision = 1;

        public static bool PatchActive { get; private set; }
        public static bool EligibilityActive { get; private set; }
        public static bool PersistenceReady { get; private set; }
        public static long ConfigRevision { get { return configRevision; } }
        public static string ActiveSaveScope { get { return activeSaveScope; } }
        public static string ConfigPath { get { return configPath ?? string.Empty; } }
        public static int ActiveOverrideCount { get { lock (Sync) return ActivePointerRanges.Count; } }

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
                        throw new InvalidDataException("Customer-allowance configuration exceeds 128 KiB.");

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
                            throw new InvalidDataException("Customer-allowance configuration has trailing content.");
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
                    warn("Ignored invalid customer-allowance configuration and started clean: "
                        + ex.GetType().Name + ": " + ex.Message);
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

        public static bool IsActiveFor(string savePath)
        {
            if (!PatchActive || !PersistenceReady || !EligibilityActive)
                return false;
            string scope = MarketValueScaling.ComputeSaveScope(savePath);
            if (scope.Length == 0 || !string.Equals(activeSaveScope, scope, StringComparison.Ordinal))
                return false;
            try
            {
                return string.Equals(activeGraphSignature, ComputeGraphSignature(), StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        public static bool EnsureSave(string savePath, out string error)
        {
            error = null;
            if (!PatchActive || !PersistenceReady)
            {
                error = "Customer-allowance persistence or patching is not ready.";
                return false;
            }
            if (!EligibilityActive)
            {
                error = "Customer allowances are not eligible in the current build, save, authority, or multiplayer state.";
                return false;
            }

            string scope = MarketValueScaling.ComputeSaveScope(savePath);
            if (scope.Length == 0)
            {
                error = "The loaded save could not be assigned a safe customer-allowance scope.";
                return false;
            }

            lock (Sync)
            {
                try
                {
                    string signature = ComputeGraphSignature();
                    if (string.Equals(activeSaveScope, scope, StringComparison.Ordinal)
                        && string.Equals(activeGraphSignature, signature, StringComparison.Ordinal))
                        return true;

                    bool scopeChanged = !string.Equals(activeSaveScope, scope, StringComparison.Ordinal);
                    CustomerGraph.Clear();
                    ActivePointerRanges.Clear();
                    if (scopeChanged)
                    {
                        ActiveRanges.Clear();
                        LoadRangesForScope(scope, ActiveRanges);
                    }

                    CaptureCustomerGraph(CustomerGraph);
                    RebuildPointerMap();
                    activeSaveScope = scope;
                    activeGraphSignature = signature;
                    configRevision++;
                    if (audit != null)
                        audit(string.Format(CultureInfo.InvariantCulture,
                            "op=customer.allowance.activate saveScope={0} customers={1} overrides={2} configRevision={3}",
                            scope, CustomerGraph.Count, ActivePointerRanges.Count, configRevision));
                    return true;
                }
                catch (Exception ex)
                {
                    CustomerGraph.Clear();
                    ActivePointerRanges.Clear();
                    activeSaveScope = string.Empty;
                    activeGraphSignature = string.Empty;
                    error = "Failed to activate customer-allowance configuration: "
                        + ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        public static List<LiveCustomerAllowance> SnapshotCustomers(bool includeLocked, out string error)
        {
            lock (Sync)
            {
                error = null;
                List<LiveCustomerAllowance> result = new List<LiveCustomerAllowance>();
                foreach (LiveCustomerAllowance row in CustomerGraph.Values)
                {
                    if (!includeLocked && !row.Unlocked)
                        continue;
                    AllowanceRange current;
                    if (!ActiveRanges.TryGetValue(row.Id, out current))
                    {
                        current = new AllowanceRange
                        {
                            MinWeeklySpend = row.OriginalMinWeeklySpend,
                            MaxWeeklySpend = row.OriginalMaxWeeklySpend
                        };
                    }
                    result.Add(new LiveCustomerAllowance
                    {
                        Customer = row.Customer,
                        Data = row.Data,
                        Id = row.Id,
                        Name = row.Name,
                        Unlocked = row.Unlocked,
                        OriginalMinWeeklySpend = row.OriginalMinWeeklySpend,
                        OriginalMaxWeeklySpend = row.OriginalMaxWeeklySpend,
                        CurrentMinWeeklySpend = current.MinWeeklySpend,
                        CurrentMaxWeeklySpend = current.MaxWeeklySpend
                    });
                }
                result.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
                return result;
            }
        }

        public static Dictionary<string, AllowanceRange> SnapshotRanges()
        {
            lock (Sync)
            {
                Dictionary<string, AllowanceRange> result =
                    new Dictionary<string, AllowanceRange>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, AllowanceRange> pair in ActiveRanges)
                    result[pair.Key] = pair.Value.Clone();
                return result;
            }
        }

        public static bool TrySetRanges(
            string savePath,
            Dictionary<string, AllowanceRange> ranges,
            out string error)
        {
            error = null;
            if (!EligibilityActive || !EnsureSave(savePath, out error))
                return false;
            if (ranges == null || ranges.Count > MaxCustomersPerSave)
            {
                error = "A save may contain at most 128 explicit customer-allowance overrides.";
                return false;
            }

            Dictionary<string, AllowanceRange> normalized =
                new Dictionary<string, AllowanceRange>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> seenRangeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, AllowanceRange> pair in ranges)
            {
                if (!PipeServer.IsSafeIdentifier(pair.Key, 64)
                    || !IsValidRange(pair.Value))
                {
                    error = "Customer-allowance override keys or values were invalid.";
                    return false;
                }

                if (!seenRangeIds.Add(pair.Key))
                {
                    error = "Customer-allowance override ids must be unique ignoring case.";
                    return false;
                }

                LiveCustomerAllowance customer;
                if (!CustomerGraph.TryGetValue(pair.Key, out customer))
                {
                    error = "No live customer matched allowance id " + pair.Key + ".";
                    return false;
                }
                if (!CanRepresentRange(customer, pair.Value))
                {
                    error = "The requested range cannot be represented safely for customer " + pair.Key + ".";
                    return false;
                }
                if (NearlyEqual(pair.Value.MinWeeklySpend, customer.OriginalMinWeeklySpend)
                    && NearlyEqual(pair.Value.MaxWeeklySpend, customer.OriginalMaxWeeklySpend))
                    continue;
                normalized[pair.Key] = pair.Value.Clone();
            }

            lock (Sync)
            {
                JObject oldRoot = (JObject)root.DeepClone();
                Dictionary<string, AllowanceRange> oldRanges = SnapshotRanges();
                try
                {
                    WriteRangesToRoot(activeSaveScope, normalized);
                    SaveRootAtomically(root);
                    ActiveRanges.Clear();
                    foreach (KeyValuePair<string, AllowanceRange> pair in normalized)
                        ActiveRanges[pair.Key] = pair.Value.Clone();
                    RebuildPointerMap();
                    configRevision++;
                    return true;
                }
                catch (Exception ex)
                {
                    root = oldRoot;
                    ActiveRanges.Clear();
                    foreach (KeyValuePair<string, AllowanceRange> pair in oldRanges)
                        ActiveRanges[pair.Key] = pair.Value.Clone();
                    try
                    {
                        SaveRootAtomically(root);
                        RebuildPointerMap();
                    }
                    catch (Exception rollbackEx)
                    {
                        error = "Customer-allowance apply failed and rollback was incomplete: " + rollbackEx.Message;
                        return false;
                    }
                    error = "Customer-allowance apply failed and was rolled back: "
                        + ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        public static float CalculateVanillaAdjusted(CustomerData data, float normalizedRelationship)
        {
            if (data == null)
                return 0f;
            bool previous = suppressPatch;
            suppressPatch = true;
            try
            {
                return data.GetAdjustedWeeklySpend(normalizedRelationship);
            }
            finally
            {
                suppressPatch = previous;
            }
        }

        public static float CalculateForRange(
            CustomerData data,
            float normalizedRelationship,
            float minWeekly,
            float maxWeekly)
        {
            if (data == null)
                return 0f;
            float relationship = Clamp01(normalizedRelationship);
            float original = Lerp(data.MinWeeklySpend, data.MaxWeeklySpend, relationship);
            float target = Lerp(minWeekly, maxWeekly, relationship);
            float vanillaAdjusted = CalculateVanillaAdjusted(data, relationship);
            if (Math.Abs(original) <= Tolerance)
                return Math.Abs(target) <= Tolerance ? vanillaAdjusted : float.NaN;
            return vanillaAdjusted * (target / original);
        }

        public static void Apply(CustomerData data, float normalizedRelationship, ref float result)
        {
            if (data == null || suppressPatch || !PatchActive || !EligibilityActive)
                return;

            AllowanceRange range;
            lock (Sync)
            {
                if (!ActivePointerRanges.TryGetValue(data.Pointer, out range))
                    return;
                range = range.Clone();
            }

            float relationship = Clamp01(normalizedRelationship);
            float original = Lerp(data.MinWeeklySpend, data.MaxWeeklySpend, relationship);
            float target = Lerp(range.MinWeeklySpend, range.MaxWeeklySpend, relationship);
            if (Math.Abs(original) <= Tolerance)
                return;
            float scaled = result * (target / original);
            if (IsFinite(scaled) && scaled >= MinWeeklySpend && scaled <= MaxWeeklySpend * 100f)
                result = scaled;
        }

        public static void Deactivate()
        {
            lock (Sync)
            {
                EligibilityActive = false;
                ActivePointerRanges.Clear();
                ActiveRanges.Clear();
                CustomerGraph.Clear();
                activeSaveScope = string.Empty;
                activeGraphSignature = string.Empty;
            }
        }

        public static void ClearManagedState()
        {
            Deactivate();
        }

        private static string ComputeGraphSignature()
        {
            var unlocked = Customer.UnlockedCustomers;
            var locked = Customer.LockedCustomers;
            if (unlocked == null || locked == null)
                throw new InvalidDataException("Customer lists are not ready.");

            unchecked
            {
                ulong signature = 1469598103934665603UL;
                MixSignature(ref signature, unlocked.Pointer.ToInt64());
                MixSignature(ref signature, unlocked.Count);
                AppendCustomerSignature(unlocked, ref signature);
                MixSignature(ref signature, -1);
                MixSignature(ref signature, locked.Pointer.ToInt64());
                MixSignature(ref signature, locked.Count);
                AppendCustomerSignature(locked, ref signature);
                return signature.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        private static void AppendCustomerSignature(
            Il2CppSystem.Collections.Generic.List<Customer> source,
            ref ulong signature)
        {
            for (int i = 0; i < source.Count; i++)
            {
                Customer customer = source[i];
                if (customer == null || customer.NPC == null || customer.CustomerData == null)
                    throw new InvalidDataException("Customer graph is not fully ready.");
                string id = (customer.NPC.ID ?? string.Empty).Trim();
                if (!PipeServer.IsSafeIdentifier(id, 64))
                    throw new InvalidDataException("A live customer has an invalid stable NPC id.");
                MixSignature(ref signature, i);
                MixSignature(ref signature, customer.Pointer.ToInt64());
                MixSignature(ref signature, customer.NPC.Pointer.ToInt64());
                MixSignature(ref signature, customer.CustomerData.Pointer.ToInt64());
                for (int character = 0; character < id.Length; character++)
                    MixSignature(ref signature, id[character]);
                MixSignature(ref signature, 0);
            }
        }

        private static void MixSignature(ref ulong signature, long value)
        {
            unchecked
            {
                ulong bits = (ulong)value;
                for (int i = 0; i < 8; i++)
                {
                    signature ^= (byte)(bits & 0xff);
                    signature *= 1099511628211UL;
                    bits >>= 8;
                }
            }
        }

        private static void CaptureCustomerGraph(Dictionary<string, LiveCustomerAllowance> destination)
        {
            var unlocked = Customer.UnlockedCustomers;
            var locked = Customer.LockedCustomers;
            if (unlocked == null || locked == null)
                throw new InvalidDataException("Customer lists are not ready.");

            Dictionary<IntPtr, string> dataOwners = new Dictionary<IntPtr, string>();
            AddCustomers(unlocked, true, destination, dataOwners);
            AddCustomers(locked, false, destination, dataOwners);
            if (destination.Count == 0)
                throw new InvalidDataException("No live customers were found.");
            if (destination.Count > MaxCustomersPerSave)
                throw new InvalidDataException("Live customer count exceeds the 128-customer safety limit.");
        }

        private static void AddCustomers(
            Il2CppSystem.Collections.Generic.List<Customer> source,
            bool unlocked,
            Dictionary<string, LiveCustomerAllowance> destination,
            Dictionary<IntPtr, string> dataOwners)
        {
            for (int i = 0; i < source.Count; i++)
            {
                Customer customer = source[i];
                if (customer == null || customer.NPC == null || customer.CustomerData == null)
                    throw new InvalidDataException("Customer graph is not fully ready.");
                string id = (customer.NPC.ID ?? string.Empty).Trim();
                if (!PipeServer.IsSafeIdentifier(id, 64))
                    throw new InvalidDataException("A live customer has an invalid stable NPC id.");
                if (destination.ContainsKey(id))
                    throw new InvalidDataException("Duplicate live customer id: " + id + ".");

                CustomerData data = customer.CustomerData;
                IntPtr pointer = data.Pointer;
                string existingOwner;
                if (pointer == IntPtr.Zero)
                    throw new InvalidDataException("Customer data pointer was unavailable for " + id + ".");
                if (dataOwners.TryGetValue(pointer, out existingOwner))
                    throw new InvalidDataException("Customers " + existingOwner + " and " + id + " share one allowance data object.");

                float min = data.MinWeeklySpend;
                float max = data.MaxWeeklySpend;
                if (!IsValidRange(new AllowanceRange { MinWeeklySpend = min, MaxWeeklySpend = max }))
                    throw new InvalidDataException("Customer " + id + " has an invalid original allowance range.");

                string name = customer.NPC.FullName ?? id;
                if (name.Length > 128)
                    name = name.Substring(0, 128);
                dataOwners[pointer] = id;
                destination[id] = new LiveCustomerAllowance
                {
                    Customer = customer,
                    Data = data,
                    Id = id,
                    Name = name,
                    Unlocked = unlocked,
                    OriginalMinWeeklySpend = min,
                    OriginalMaxWeeklySpend = max,
                    CurrentMinWeeklySpend = min,
                    CurrentMaxWeeklySpend = max
                };
            }
        }

        private static void RebuildPointerMap()
        {
            ActivePointerRanges.Clear();
            foreach (KeyValuePair<string, AllowanceRange> pair in ActiveRanges)
            {
                LiveCustomerAllowance customer;
                if (!CustomerGraph.TryGetValue(pair.Key, out customer))
                    throw new InvalidDataException("Stored customer allowance does not match a live customer: " + pair.Key + ".");
                if (!CanRepresentRange(customer, pair.Value))
                    throw new InvalidDataException("Stored customer allowance cannot be represented safely: " + pair.Key + ".");
                if (NearlyEqual(pair.Value.MinWeeklySpend, customer.OriginalMinWeeklySpend)
                    && NearlyEqual(pair.Value.MaxWeeklySpend, customer.OriginalMaxWeeklySpend))
                    continue;
                ActivePointerRanges[customer.Data.Pointer] = pair.Value.Clone();
            }
        }

        private static bool CanRepresentRange(LiveCustomerAllowance customer, AllowanceRange range)
        {
            if (customer == null || range == null)
                return false;
            if (Math.Abs(customer.OriginalMinWeeklySpend) <= Tolerance
                && Math.Abs(range.MinWeeklySpend) > Tolerance)
                return false;
            if (Math.Abs(customer.OriginalMaxWeeklySpend) <= Tolerance
                && Math.Abs(range.MaxWeeklySpend) > Tolerance)
                return false;
            return true;
        }

        private static bool IsValidRange(AllowanceRange range)
        {
            return range != null
                && IsFinite(range.MinWeeklySpend)
                && IsFinite(range.MaxWeeklySpend)
                && range.MinWeeklySpend >= MinWeeklySpend
                && range.MaxWeeklySpend >= range.MinWeeklySpend
                && range.MaxWeeklySpend <= MaxWeeklySpend;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= Tolerance;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float Lerp(float min, float max, float amount)
        {
            return min + ((max - min) * amount);
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
                throw new InvalidDataException("Customer-allowance configuration version/build does not match this bridge.");

            JObject saves = candidate["saves"] as JObject;
            if (saves == null || saves.Count > 64)
                throw new InvalidDataException("Customer-allowance save scopes are missing or exceed the 64-scope limit.");
            foreach (JProperty save in saves.Properties())
            {
                if (!PipeServer.IsSafeIdentifier(save.Name, 64))
                    throw new InvalidDataException("Customer-allowance save scope key is invalid.");
                JObject scope = save.Value as JObject;
                JObject customers = scope == null ? null : scope["customers"] as JObject;
                if (customers == null || customers.Count > MaxCustomersPerSave)
                    throw new InvalidDataException("Customer-allowance collection is invalid.");

                HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JProperty customer in customers.Properties())
                {
                    JObject values = customer.Value as JObject;
                    JToken minToken = values == null ? null : values["minWeeklySpend"];
                    JToken maxToken = values == null ? null : values["maxWeeklySpend"];
                    float? min = minToken == null ? null : minToken.Value<float?>();
                    float? max = maxToken == null ? null : maxToken.Value<float?>();
                    if (!PipeServer.IsSafeIdentifier(customer.Name, 64)
                        || !seenIds.Add(customer.Name)
                        || values == null
                        || minToken == null
                        || maxToken == null
                        || (minToken.Type != JTokenType.Integer && minToken.Type != JTokenType.Float)
                        || (maxToken.Type != JTokenType.Integer && maxToken.Type != JTokenType.Float)
                        || !min.HasValue
                        || !max.HasValue
                        || !IsValidRange(new AllowanceRange { MinWeeklySpend = min.Value, MaxWeeklySpend = max.Value }))
                        throw new InvalidDataException("Customer-allowance entry is invalid.");
                }
            }
        }

        private static void LoadRangesForScope(string scope, Dictionary<string, AllowanceRange> destination)
        {
            JObject saves = root["saves"] as JObject;
            JObject save = saves == null ? null : saves[scope] as JObject;
            JObject customers = save == null ? null : save["customers"] as JObject;
            if (customers == null)
                return;
            foreach (JProperty pair in customers.Properties())
            {
                JObject values = (JObject)pair.Value;
                destination[pair.Name] = new AllowanceRange
                {
                    MinWeeklySpend = values.Value<float>("minWeeklySpend"),
                    MaxWeeklySpend = values.Value<float>("maxWeeklySpend")
                };
            }
        }

        private static void WriteRangesToRoot(string scope, Dictionary<string, AllowanceRange> ranges)
        {
            JObject values = new JObject();
            foreach (KeyValuePair<string, AllowanceRange> pair in ranges)
            {
                values[pair.Key] = new JObject
                {
                    ["minWeeklySpend"] = pair.Value.MinWeeklySpend,
                    ["maxWeeklySpend"] = pair.Value.MaxWeeklySpend
                };
            }
            JObject saves = (JObject)root["saves"];
            if (saves[scope] == null && saves.Count >= 64)
                throw new InvalidDataException("Customer-allowance configuration already contains the maximum 64 save scopes.");
            saves[scope] = new JObject
            {
                ["customers"] = values,
                ["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        private static void SaveRootAtomically(JObject value)
        {
            string serialized = value.ToString(Formatting.Indented);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(serialized);
            if (bytes.LongLength > MaxConfigBytes)
                throw new InvalidDataException("Customer-allowance configuration would exceed 128 KiB.");

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
