using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Il2CppScheduleOne.Effects;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ScheduleIControlBridge
{
    internal static class EffectsIntensityManager
    {
        private const int ConfigVersion = 1;
        private const long MaxConfigBytes = 64 * 1024;
        private const string ConfigFileName = "ScheduleIControlBridge.effect-profile.json";

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, EffectPreviewGroup> EffectPreviews = new Dictionary<string, EffectPreviewGroup>(StringComparer.OrdinalIgnoreCase);

        private static JObject root;
        private static string configPath;
        private static string activeSaveScope = string.Empty;
        private static Action<string> warn;
        private static Action<string> audit;
        private static long configRevision = 1;
        private static bool typeResolved;
        private static Il2CppSystem.Type effectType;

        public static bool PersistenceReady { get; private set; }
        public static bool PatchActive { get; private set; }
        public static bool EligibilityActive { get; private set; }
        public static long ConfigRevision { get { return configRevision; } }
        public static string ActiveSaveScope { get { return activeSaveScope; } }
        public static int ActiveOverrideCount { get { lock (Sync) return CountProfileEffects(); } }

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
                        throw new InvalidDataException("Effect profile exceeds 64 KiB.");

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
                            throw new InvalidDataException("Effect profile has trailing content.");
                    }
                    ValidateRoot(root);
                }
                PersistenceReady = true;
            }
            catch (Exception ex)
            {
                root = CreateEmptyRoot();
                PersistenceReady = true;
                if (warn != null)
                    warn("Ignored invalid effect profile and started clean: " + ex.GetType().Name + ": " + ex.Message);
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
            if (!PersistenceReady)
            {
                error = "Effect profile persistence is not ready.";
                return false;
            }
            if (!EligibilityActive)
            {
                error = "Effect overrides are not eligible in the current build, save, authority, or multiplayer state.";
                return false;
            }
            string scope = MarketValueScaling.ComputeSaveScope(savePath);
            if (scope.Length == 0)
            {
                error = "The loaded save could not be assigned a safe effect scope.";
                return false;
            }

            lock (Sync)
            {
                if (string.Equals(activeSaveScope, scope, StringComparison.Ordinal))
                    return true;
                activeSaveScope = scope;
                configRevision++;
                ApplyProfileToLoadedEffects();
                if (audit != null)
                    audit(string.Format(CultureInfo.InvariantCulture, "op=effects.activate saveScope={0} configRevision={1}", scope, configRevision));
                return true;
            }
        }

        public static bool TryApply(string savePath, List<EffectTarget> targets, out string error)
        {
            error = null;
            if (!EnsureSave(savePath, out error))
                return false;
            if (targets == null || targets.Count == 0 || targets.Count > 64)
            {
                error = "An effect profile may contain at most 64 effect entries.";
                return false;
            }

            Dictionary<string, EffectTarget> normalized = new Dictionary<string, EffectTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (EffectTarget target in targets)
            {
                string targetError = null;
                bool targetValid = PipeServer.IsSafeIdentifier(target.EffectId, 64) && IsValidTarget(target, out targetError);
                if (!targetValid)
                {
                    error = targetError ?? "An effect target was invalid.";
                    return false;
                }
                normalized[target.EffectId] = target;
            }

            lock (Sync)
            {
                JObject priorRoot = root == null ? null : (JObject)root.DeepClone();
                JObject candidate = (JObject)root.DeepClone();
                JObject saves = candidate["saves"] as JObject ?? new JObject();
                JObject scopeNode = saves[activeSaveScope] as JObject ?? new JObject();
                JObject effects = scopeNode["effects"] as JObject ?? new JObject();
                foreach (KeyValuePair<string, EffectTarget> pair in normalized)
                    effects[pair.Key] = TargetToJson(pair.Value);
                scopeNode["effects"] = effects;
                saves[activeSaveScope] = scopeNode;
                candidate["saves"] = saves;
                candidate["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

                try
                {
                    SaveRootAtomically(candidate);
                    root = candidate;
                    configRevision++;
                    foreach (EffectTarget target in normalized.Values)
                        ApplyTargetToInstance(target);
                    if (audit != null)
                        audit(string.Format(CultureInfo.InvariantCulture, "op=effects.apply saveScope={0} count={1} configRevision={2}", activeSaveScope, normalized.Count, configRevision));
                    return true;
                }
                catch (Exception ex)
                {
                    root = priorRoot ?? CreateEmptyRoot();
                    try { SaveRootAtomically(root); } catch { }
                    error = "Effect apply failed and was rolled back: " + ex.GetType().Name + ": " + ex.Message;
                    return false;
                }
            }
        }

        public static List<LiveEffect> ListEffects(out string error)
        {
            error = null;
            List<Effect> effects = FindLoadedEffects(out string findError);
            if (effects == null)
            {
                error = findError;
                return null;
            }
            List<LiveEffect> result = new List<LiveEffect>();
            foreach (Effect effect in effects)
            {
                if (effect == null)
                    continue;
                LiveEffect live = new LiveEffect
                {
                    EffectId = effect.ID ?? string.Empty,
                    Name = effect.Name ?? string.Empty,
                    TypeName = effect.GetIl2CppType().FullName,
                    Tier = effect.Tier,
                    ValueChange = effect.ValueChange,
                    ValueMultiplier = effect.ValueMultiplier,
                    AddBaseValueMultiple = effect.AddBaseValueMultiple,
                    Parameters = ReadIntensityParameters(effect)
                };
                if (live.EffectId.Length == 0)
                    live.EffectId = effect.GetIl2CppType().FullName;
                result.Add(live);
            }
            result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            ApplyProfileToLoadedEffects();
            return result;
        }

        public static EffectPreviewGroup CreateGroupPreview(List<EffectTarget> targets, out string error)
        {
            error = null;
            if (targets == null || targets.Count == 0 || targets.Count > 64)
            {
                error = "Effect previews require 1-64 effect targets.";
                return null;
            }
            EffectPreviewGroup group = new EffectPreviewGroup { Id = Guid.NewGuid().ToString("N") };
            foreach (EffectTarget target in targets)
            {
                if (!IsValidTarget(target, out error))
                    return null;
                Effect effect = FindEffect(target.EffectId);
                if (effect == null)
                {
                    error = "No loaded effect matched the requested effect id: " + target.EffectId;
                    return null;
                }
                group.Previews.Add(new EffectPreview
                {
                    EffectId = target.EffectId,
                    Target = target,
                    OldValueChange = effect.ValueChange,
                    OldValueMultiplier = effect.ValueMultiplier,
                    OldAddBaseValueMultiple = effect.AddBaseValueMultiple,
                    OldTier = effect.Tier,
                    OldParameters = ReadIntensityParameters(effect)
                });
            }
            lock (Sync)
            {
                EffectPreviews[group.Id] = group;
                if (EffectPreviews.Count > 32)
                {
                    string oldest = null;
                    DateTime oldestTime = DateTime.MaxValue;
                    foreach (KeyValuePair<string, EffectPreviewGroup> pair in EffectPreviews)
                    {
                        if (pair.Value.CreatedUtc < oldestTime)
                        {
                            oldestTime = pair.Value.CreatedUtc;
                            oldest = pair.Key;
                        }
                    }
                    if (oldest != null)
                        EffectPreviews.Remove(oldest);
                }
            }
            return group;
        }

        public static EffectPreviewGroup TakePreview(string previewId, out string error)
        {
            error = null;
            if (!PipeServer.IsSafeIdentifier(previewId, 64))
            {
                error = "previewId is invalid.";
                return null;
            }
            lock (Sync)
            {
                EffectPreviewGroup preview;
                if (!EffectPreviews.TryGetValue(previewId, out preview))
                {
                    error = "Effect preview was not found or has expired.";
                    return null;
                }
                EffectPreviews.Remove(previewId);
                return preview;
            }
        }

        public static bool ApplyGroupPreview(EffectPreviewGroup group, string savePath, out string error)
        {
            error = null;
            if (group == null || group.Previews == null || group.Previews.Count == 0)
            {
                error = "The effect preview was empty.";
                return false;
            }
            List<EffectTarget> targets = new List<EffectTarget>();
            foreach (EffectPreview preview in group.Previews)
                targets.Add(preview.Target);
            return TryApply(savePath, targets, out error);
        }

        public static void ClearManagedState()
        {
            EligibilityActive = false;
        }

        private static int CountProfileEffects()
        {
            int count = 0;
            if (activeSaveScope.Length > 0 && root != null && root["saves"] is JObject saves && saves[activeSaveScope] is JObject scopeNode && scopeNode["effects"] is JObject effects)
                count = effects.Count;
            return count;
        }

        private static bool IsValidTarget(EffectTarget target, out string error)
        {
            error = null;
            if (target.ValueChange.HasValue && (target.ValueChange.Value < -SellPriceLimitManager.PracticalMoneyMaximum || target.ValueChange.Value > SellPriceLimitManager.PracticalMoneyMaximum))
            {
                error = "valueChange must be between -16777215 and 16777215.";
                return false;
            }
            if (target.ValueMultiplier.HasValue && (float.IsNaN(target.ValueMultiplier.Value) || float.IsInfinity(target.ValueMultiplier.Value) || target.ValueMultiplier.Value < 0f || target.ValueMultiplier.Value > 1000000f))
            {
                error = "valueMultiplier must be between 0 and 1,000,000.";
                return false;
            }
            if (target.AddBaseValueMultiple.HasValue && (float.IsNaN(target.AddBaseValueMultiple.Value) || float.IsInfinity(target.AddBaseValueMultiple.Value) || target.AddBaseValueMultiple.Value < -1000000f || target.AddBaseValueMultiple.Value > 1000000f))
            {
                error = "addBaseValueMultiple must be between -1,000,000 and 1,000,000.";
                return false;
            }
            if (target.Tier.HasValue && (target.Tier.Value < 0 || target.Tier.Value > 5))
            {
                error = "tier must be between 0 and 5.";
                return false;
            }
            if (target.Parameters != null)
            {
                foreach (EffectParameter parameter in target.Parameters)
                {
                    if (!PipeServer.IsSafeIdentifier(parameter.Name, 48)
                        || float.IsNaN(parameter.Value)
                        || float.IsInfinity(parameter.Value)
                        || parameter.Value < -100000f
                        || parameter.Value > 100000f)
                    {
                        error = "An effect parameter name or value was invalid.";
                        return false;
                    }
                }
            }
            return true;
        }

        private static JObject TargetToJson(EffectTarget target)
        {
            JObject result = new JObject();
            if (target.ValueChange.HasValue) result["valueChange"] = target.ValueChange.Value;
            if (target.ValueMultiplier.HasValue) result["valueMultiplier"] = target.ValueMultiplier.Value;
            if (target.AddBaseValueMultiple.HasValue) result["addBaseValueMultiple"] = target.AddBaseValueMultiple.Value;
            if (target.Tier.HasValue) result["tier"] = target.Tier.Value;
            if (target.Parameters != null && target.Parameters.Count > 0)
            {
                JArray parameters = new JArray();
                foreach (EffectParameter parameter in target.Parameters)
                {
                    parameters.Add(new JObject { ["name"] = parameter.Name, ["value"] = parameter.Value });
                }
                result["parameters"] = parameters;
            }
            return result;
        }

        private static Effect FindEffect(string effectId)
        {
            List<Effect> effects = FindLoadedEffects(out _);
            if (effects == null)
                return null;
            foreach (Effect effect in effects)
            {
                if (effect == null)
                    continue;
                string id = effect.ID ?? string.Empty;
                if (id.Length == 0)
                    id = effect.GetIl2CppType().FullName;
                if (string.Equals(id, effectId, StringComparison.OrdinalIgnoreCase))
                    return effect;
            }
            return null;
        }

        private static void ApplyTargetToInstance(EffectTarget target)
        {
            Effect effect = FindEffect(target.EffectId);
            if (effect == null)
                return;
            if (target.ValueChange.HasValue) effect.ValueChange = target.ValueChange.Value;
            if (target.ValueMultiplier.HasValue) effect.ValueMultiplier = target.ValueMultiplier.Value;
            if (target.AddBaseValueMultiple.HasValue) effect.AddBaseValueMultiple = target.AddBaseValueMultiple.Value;
            if (target.Tier.HasValue) effect.Tier = target.Tier.Value;
            if (target.Parameters != null)
            {
                foreach (EffectParameter parameter in target.Parameters)
                    ApplyIntensityParameter(effect, parameter.Name, parameter.Value);
            }
        }

        private static void ApplyProfileToLoadedEffects()
        {
            if (activeSaveScope.Length == 0 || root == null || !(root["saves"] is JObject saves) || !(saves[activeSaveScope] is JObject scopeNode) || !(scopeNode["effects"] is JObject effects))
                return;
            foreach (KeyValuePair<string, JToken> pair in effects)
            {
                EffectTarget target = JsonToTarget(pair.Key, pair.Value as JObject);
                string targetError;
                if (target != null
                    && PipeServer.IsSafeIdentifier(target.EffectId, 64)
                    && IsValidTarget(target, out targetError))
                {
                    ApplyTargetToInstance(target);
                }
            }
        }

        private static EffectTarget JsonToTarget(string id, JObject node)
        {
            if (node == null)
                return null;
            EffectTarget target = new EffectTarget { EffectId = id };
            if (node["valueChange"] != null && node["valueChange"].Type == JTokenType.Integer) target.ValueChange = node.Value<int>("valueChange");
            if (node["valueMultiplier"] != null && node["valueMultiplier"].Type == JTokenType.Float) target.ValueMultiplier = node.Value<float>("valueMultiplier");
            if (node["addBaseValueMultiple"] != null && node["addBaseValueMultiple"].Type == JTokenType.Float) target.AddBaseValueMultiple = node.Value<float>("addBaseValueMultiple");
            if (node["tier"] != null && node["tier"].Type == JTokenType.Integer) target.Tier = node.Value<int>("tier");
            if (node["parameters"] is JArray parameters)
            {
                target.Parameters = new List<EffectParameter>();
                foreach (JToken token in parameters)
                {
                    JObject item = token as JObject;
                    if (item == null) continue;
                    target.Parameters.Add(new EffectParameter
                    {
                        Name = item.Value<string>("name") ?? string.Empty,
                        Value = item.Value<float>("value")
                    });
                }
            }
            return target;
        }

        private static List<Effect> FindLoadedEffects(out string error)
        {
            error = null;
            try
            {
                Il2CppSystem.Type type = ResolveEffectType();
                if (type == null)
                {
                    error = "The game's Effect type could not be resolved.";
                    return null;
                }
                Il2CppReferenceArray<UnityEngine.Object> objects = UnityEngine.Object.FindObjectsOfTypeAll(type);
                List<Effect> result = new List<Effect>();
                if (objects != null)
                {
                    for (int i = 0; i < objects.Length; i++)
                    {
                        UnityEngine.Object obj = objects[i];
                        if (obj == null)
                            continue;
                        try
                        {
                            Effect effect = obj.Cast<Effect>();
                            if (effect != null)
                                result.Add(effect);
                        }
                        catch
                        {
                        }
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                error = "Failed to enumerate effect assets: " + ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        private static Il2CppSystem.Type ResolveEffectType()
        {
            if (typeResolved)
                return effectType;
            try
            {
                effectType = Il2CppSystem.Type.GetType("ScheduleOne.Effects.Effect, Assembly-CSharp");
            }
            catch
            {
                effectType = null;
            }
            typeResolved = true;
            return effectType;
        }

        private static List<EffectParameter> ReadIntensityParameters(Effect effect)
        {
            List<EffectParameter> parameters = new List<EffectParameter>();
            string id = (effect.ID ?? string.Empty).ToLowerInvariant();
            if (id == "shrinking")
            {
                parameters.Add(new EffectParameter { Name = "Scale", DisplayName = "Body Height Scale", Value = Shrinking.Scale, Min = 0.2f, Max = 3f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
                parameters.Add(new EffectParameter { Name = "LerpTime", DisplayName = "Height Transition Time", Value = Shrinking.LerpTime, Min = 0f, Max = 10f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
            }
            if (id == "brighteyed")
            {
                BrightEyed bright = effect.TryCast<BrightEyed>();
                if (bright != null)
                {
                    parameters.Add(new EffectParameter { Name = "Emission", DisplayName = "Eye Glow Emission", Value = bright.Emission, Min = 0f, Max = 10f, Hint = "How strongly the eyes glow." });
                    parameters.Add(new EffectParameter { Name = "LightIntensity", DisplayName = "Eye Light Intensity", Value = bright.LightIntensity, Min = 0f, Max = 10f, Hint = "Light cast by the glowing eyes." });
                }
            }
            if (id == "seizure")
            {
                parameters.Add(new EffectParameter { Name = "CAMERA_JITTER_INTENSITY", DisplayName = "Camera Jitter", Value = Seizure.CAMERA_JITTER_INTENSITY, Min = 0f, Max = 10f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
                parameters.Add(new EffectParameter { Name = "DURATION_NPC", DisplayName = "NPC Duration", Value = Seizure.DURATION_NPC, Min = 0f, Max = 60f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
                parameters.Add(new EffectParameter { Name = "DURATION_PLAYER", DisplayName = "Player Duration", Value = Seizure.DURATION_PLAYER, Min = 0f, Max = 60f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
            }
            if (id == "lethal")
            {
                parameters.Add(new EffectParameter { Name = "HEALTH_DRAIN_PLAYER", DisplayName = "Player Health Drain", Value = Lethal.HEALTH_DRAIN_PLAYER, Min = 0f, Max = 100f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
                parameters.Add(new EffectParameter { Name = "HEALTH_DRAIN_NPC", DisplayName = "NPC Health Drain", Value = Lethal.HEALTH_DRAIN_NPC, Min = 0f, Max = 100f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
            }
            if (id == "antigravity")
            {
                parameters.Add(new EffectParameter { Name = "GravityMultiplier", DisplayName = "Gravity Multiplier", Value = AntiGravity.GravityMultiplier, Min = -2f, Max = 2f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
            }
            if (id == "athletic")
            {
                AddSpeedParameters(parameters, Athletic.SPEED_MULTIPLIER, Athletic.NPC_SPEED_MULTIPLIER, Athletic.WorkSpeedMultiplier, true);
            }
            if (id == "energizing")
            {
                AddSpeedParameters(parameters, Energizing.SPEED_MULTIPLIER, null, Energizing.WorkSpeedMultiplier, true);
            }
            if (id == "focused")
            {
                parameters.Add(new EffectParameter { Name = "WorkSpeedMultiplier", DisplayName = "Work Speed Multiplier", Value = Focused.WorkSpeedMultiplier, Min = 0.2f, Max = 5f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
            }
            if (id == "sedating")
            {
                parameters.Add(new EffectParameter { Name = "WorkSpeedMultiplier", DisplayName = "Work Speed Multiplier", Value = Sedating.WorkSpeedMultiplier, Min = 0f, Max = 2f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
            }
            if (id == "sneaky")
            {
                parameters.Add(new EffectParameter { Name = "SPEED_MULTIPLIER", DisplayName = "Move Speed Multiplier", Value = Sneaky.SPEED_MULTIPLIER, Min = 0.2f, Max = 5f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
                parameters.Add(new EffectParameter { Name = "FOOTSTEP_VOL_MULTIPLIER", DisplayName = "Footstep Volume Multiplier", Value = Sneaky.FOOTSTEP_VOL_MULTIPLIER, Min = 0f, Max = 2f, Hint = "Compiled into the game; cannot be changed.", ReadOnly = true });
            }
            return parameters;
        }

        private static void AddSpeedParameters(List<EffectParameter> parameters, float speedMultiplier, float? npcSpeedMultiplier, float workSpeedMultiplier, bool readOnly)
        {
            string hint = readOnly ? "Compiled into the game; cannot be changed." : "Movement speed while active.";
            parameters.Add(new EffectParameter { Name = "SPEED_MULTIPLIER", DisplayName = "Move Speed Multiplier", Value = speedMultiplier, Min = 0.2f, Max = 5f, Hint = hint, ReadOnly = readOnly });
            if (npcSpeedMultiplier.HasValue)
                parameters.Add(new EffectParameter { Name = "NPC_SPEED_MULTIPLIER", DisplayName = "NPC Move Speed Multiplier", Value = npcSpeedMultiplier.Value, Min = 0.2f, Max = 5f, Hint = hint, ReadOnly = readOnly });
            parameters.Add(new EffectParameter { Name = "WorkSpeedMultiplier", DisplayName = "Work Speed Multiplier", Value = workSpeedMultiplier, Min = 0.2f, Max = 5f, Hint = hint, ReadOnly = readOnly });
        }

        private static void ApplyIntensityParameter(Effect effect, string name, float value)
        {
            BrightEyed bright = effect.TryCast<BrightEyed>();
            if (bright != null)
            {
                if (name == "Emission") bright.Emission = value;
                else if (name == "LightIntensity") bright.LightIntensity = value;
            }
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
                throw new InvalidDataException("Effect profile version/build does not match this bridge.");
        }

        private static void SaveRootAtomically(JObject value)
        {
            string serialized = value.ToString(Formatting.Indented);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(serialized);
            if (bytes.LongLength > MaxConfigBytes)
                throw new InvalidDataException("Effect profile would exceed 64 KiB.");
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

    internal sealed class EffectTarget
    {
        public string EffectId;
        public int? ValueChange;
        public float? ValueMultiplier;
        public float? AddBaseValueMultiple;
        public int? Tier;
        public List<EffectParameter> Parameters;
    }

    internal sealed class EffectParameter
    {
        public string Name;
        public string DisplayName;
        public float Value;
        public float Min;
        public float Max;
        public string Hint;
        public bool ReadOnly;

        public JObject ToJson()
        {
            return new JObject
            {
                ["name"] = Name,
                ["displayName"] = DisplayName ?? Name,
                ["value"] = Value,
                ["min"] = Min,
                ["max"] = Max,
                ["hint"] = Hint ?? string.Empty,
                ["readOnly"] = ReadOnly
            };
        }
    }

    internal sealed class LiveEffect
    {
        public string EffectId;
        public string Name;
        public string TypeName;
        public int Tier;
        public int ValueChange;
        public float ValueMultiplier;
        public float AddBaseValueMultiple;
        public List<EffectParameter> Parameters;

        public JObject ToJson()
        {
            JArray parameters = new JArray();
            if (Parameters != null)
            {
                foreach (EffectParameter parameter in Parameters)
                    parameters.Add(parameter.ToJson());
            }
            return new JObject
            {
                ["effectId"] = EffectId,
                ["name"] = Name,
                ["typeName"] = TypeName ?? string.Empty,
                ["tier"] = Tier,
                ["valueChange"] = ValueChange,
                ["valueMultiplier"] = ValueMultiplier,
                ["addBaseValueMultiple"] = AddBaseValueMultiple,
                ["parameters"] = parameters
            };
        }
    }

    internal sealed class EffectPreview
    {
        public string EffectId;
        public EffectTarget Target;
        public int OldValueChange;
        public float OldValueMultiplier;
        public float OldAddBaseValueMultiple;
        public int OldTier;
        public List<EffectParameter> OldParameters;

        public JObject ToJson()
        {
            JArray oldParameters = new JArray();
            JArray newParameters = new JArray();
            if (OldParameters != null)
            {
                foreach (EffectParameter parameter in OldParameters)
                {
                    oldParameters.Add(parameter.ToJson());
                    JObject planned = parameter.ToJson();
                    if (Target != null && Target.Parameters != null)
                    {
                        foreach (EffectParameter targetParameter in Target.Parameters)
                        {
                            if (string.Equals(targetParameter.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                planned["value"] = targetParameter.Value;
                                break;
                            }
                        }
                    }
                    newParameters.Add(planned);
                }
            }
            return new JObject
            {
                ["effectId"] = EffectId,
                ["oldValueChange"] = OldValueChange,
                ["oldValueMultiplier"] = OldValueMultiplier,
                ["oldAddBaseValueMultiple"] = OldAddBaseValueMultiple,
                ["oldTier"] = OldTier,
                ["oldParameters"] = oldParameters,
                ["newValueChange"] = Target != null && Target.ValueChange.HasValue ? Target.ValueChange.Value : OldValueChange,
                ["newValueMultiplier"] = Target != null && Target.ValueMultiplier.HasValue ? Target.ValueMultiplier.Value : OldValueMultiplier,
                ["newAddBaseValueMultiple"] = Target != null && Target.AddBaseValueMultiple.HasValue ? Target.AddBaseValueMultiple.Value : OldAddBaseValueMultiple,
                ["newTier"] = Target != null && Target.Tier.HasValue ? Target.Tier.Value : OldTier,
                ["newParameters"] = newParameters
            };
        }
    }

    internal sealed class EffectPreviewGroup
    {
        public readonly DateTime CreatedUtc = DateTime.UtcNow;
        public string Id;
        public readonly List<EffectPreview> Previews = new List<EffectPreview>();
    }
}
