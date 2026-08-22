using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ScheduleIControlBridge
{
    internal static class PlayerRuntimeSettings
    {
        private const int ConfigVersion = 2;
        private const int InventoryModeSingle = 1;
        private const int InventoryModeDouble = 2;
        private const int InventoryModeTriple = 3;
        private const int InventoryModeOnDemand = 4;
        private const float MinimumSpeed = 0.1f;
        private const float MaximumSpeed = 10f;
        private const float PageSwapCooldownSeconds = 0.8f;
        private const float SwapNoticeLifetimeSeconds = 1f;
        private const float SwapNoticeAlpha = 0.2f;
        private const long MaxConfigBytes = 16 * 1024;
        private const long MaxSidecarBytes = 256 * 1024;
        private const string ConfigFileName = "ScheduleIControlBridge.player-runtime.json";
        private const string SidecarPrefix = "ScheduleIControlBridge.inventory-pages.";
        private const string SidecarSuffix = ".json";

        private static readonly object Sync = new object();
        private static readonly Dictionary<int, string> extraPageJson = new Dictionary<int, string>();
        private static Action<string> warn;
        private static Action<string> audit;
        private static string configPath;
        private static JObject root;
        private static string sidecarPath;
        private static string activeSaveScope = string.Empty;
        private static string pageZeroJson;
        private static string lastInventoryError = string.Empty;
        private static int configuredInventoryMode = InventoryModeSingle;
        private static float configuredSpeedMultiplier = 1f;
        private static int currentPage;
        private static int allocatedPageCount = 1;
        private static PlayerInventory activeInventory;
        private static bool reentrancyGuard;
        private static bool sidecarLoaded;
        private static bool sidecarRejected;
        private static string sidecarRejectedScope = string.Empty;
        private static string sidecarRejectedPath = string.Empty;
        private static bool saveUnsafe;
        private static bool speedWasApplied;
        private static float lastAppliedSpeed = 1f;
        private static string lastObservedInventoryJson;
        private static DateTime nextInventoryDirtyCheckUtc;
        private static DateTime nextInventoryFlushRetryUtc;
        private static int inventoryFlushRetryDelayMilliseconds = 2000;
        private static bool inventoryPersistenceFailureActive;
        private static bool inventorySurfaceDirty;
        private static string lastPagingInputGate = string.Empty;
        private static string lastPagingInputComponents = string.Empty;
        private static string lastCanonicalInputState = string.Empty;
        private static bool canonicalKeyboardBindingsApplied;
        private static bool pagingInputSampleKnown;
        private static bool lastPagingLeft;
        private static bool lastPagingRight;
        private static float nextPageSwapAllowedTime;
        private static float lastSuccessfulPageSwapTime = -1f;
        private static GameObject swapNoticeObject;
        private static Text swapNoticeText;
        private static float swapNoticeExpiresTime;
        private static bool swapNoticeSetupFailureLogged;
        private static int deferredUnequipFramesRemaining;
        private static bool deferredUnequipRecoveryAttempted;
        private static int deferredUnequipPage = -1;

        public static bool PersistenceReady { get; private set; }
        public static bool PatchActive { get; private set; }
        public static bool EligibilityActive { get; private set; }
        public static bool InventoryReady { get; private set; }
        public static long ConfigRevision { get; private set; } = 1;
        public static int ConfiguredInventoryMode { get { return configuredInventoryMode; } }
        public static float ConfiguredSpeedMultiplier { get { return configuredSpeedMultiplier; } }
        public static int NativeHotbarSlots { get { return InventoryReady ? InventoryPagingModel.NativePageWidth : 0; } }
        public static int CurrentPage { get { return currentPage; } }
        public static int AllocatedPageCount { get { return Math.Max(1, allocatedPageCount); } }
        public static int ConfiguredPageCount { get { return InventoryPagingModel.ConfiguredPageCountForMode(configuredInventoryMode); } }
        public static string ActiveSaveScope { get { return activeSaveScope ?? string.Empty; } }
        public static bool SidecarLoaded { get { return sidecarLoaded; } }
        public static string LastInventoryError { get { return lastInventoryError ?? string.Empty; } }

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
                        throw new InvalidDataException("Player runtime settings exceed 16 KiB.");
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
                            throw new InvalidDataException("Player runtime settings have trailing content.");
                    }
                    ValidateRoot(root);
                    configuredInventoryMode = ClampInventoryMode(root.Value<int?>("inventoryMode") ?? InventoryModeSingle);
                    configuredSpeedMultiplier = ClampSpeed(root.Value<float?>("speedMultiplier") ?? 1f);
                }
                PersistenceReady = true;
            }
            catch (Exception ex)
            {
                root = CreateEmptyRoot();
                configuredInventoryMode = InventoryModeSingle;
                configuredSpeedMultiplier = 1f;
                PersistenceReady = true;
                Warn("Ignored invalid player runtime settings and started clean: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void SetPatchActive(bool value)
        {
            PatchActive = value;
            if (!value)
            {
                InventoryReady = false;
                DestroySwapNotice();
            }
        }

        public static void SetEligibility(bool value)
        {
            if (EligibilityActive != value)
            {
                if (!value)
                {
                    bool restored = RestorePageZeroBeforeDisable();
                    FlushSidecar();
                    InventoryReady = false;
                    if (restored)
                    {
                        activeInventory = null;
                        ResetPages();
                    }
                }
                speedWasApplied = false;
            }
            EligibilityActive = value;
        }

        public static void Tick(bool eligible)
        {
            if (!PatchActive || !PersistenceReady)
                return;
            if (!eligible)
            {
                InventoryReady = false;
                speedWasApplied = false;
            }
        }

        public static void TickDeferredUnequipVerification()
        {
            if (deferredUnequipFramesRemaining <= 0)
                return;
            if (!PatchActive || !PersistenceReady || !EligibilityActive || !InventoryReady || activeInventory == null)
            {
                deferredUnequipFramesRemaining = 0;
                deferredUnequipRecoveryAttempted = false;
                deferredUnequipPage = -1;
                return;
            }

            int phase = 3 - deferredUnequipFramesRemaining;
            deferredUnequipFramesRemaining--;
            bool verified = VerifyUnequippedSurface(out string state);
            Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.page_unequip_deferred page={0} frame={1} phase={2} verified={3} {4}", deferredUnequipPage, Time.frameCount, phase, verified, state));

            if (!verified && !deferredUnequipRecoveryAttempted)
            {
                deferredUnequipRecoveryAttempted = true;
                int deselected = DeselectSelectedHotbarSlots();
                ReconcileUnequippedPlayerState();
                RefreshNativeSlotUIs();
                bool recovered = VerifyUnequippedSurface(out string recoveredState);
                Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.page_unequip_deferred_recovery page={0} frame={1} phase={2} deselected={3} verified={4} {5}", deferredUnequipPage, Time.frameCount, phase, deselected, recovered, recoveredState));
                verified = recovered;
            }

            if (deferredUnequipFramesRemaining == 0)
            {
                bool finalVerified = VerifyUnequippedSurface(out string finalState);
                Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.page_unequip_deferred_final page={0} frame={1} verified={2} recoveryAttempted={3} {4}", deferredUnequipPage, Time.frameCount, finalVerified, deferredUnequipRecoveryAttempted, finalState));
                deferredUnequipRecoveryAttempted = false;
                deferredUnequipPage = -1;
            }
        }

        public static void ApplySpeedDuringNativeMovement()
        {
            if (!PatchActive || !PersistenceReady)
                return;
            try
            {
                float target = InventoryPagingModel.SpeedForEligibility(EligibilityActive, InventoryReady, configuredSpeedMultiplier);
                if (!speedWasApplied || Math.Abs(lastAppliedSpeed - target) > 0.0001f)
                {
                    PlayerMovement.StaticMoveSpeedMultiplier = target;
                    lastAppliedSpeed = target;
                    speedWasApplied = true;
                    Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.speed_applied speedMultiplier={0:0.###}", target));
                }
            }
            catch (Exception ex)
            {
                Warn("Player movement speed lifecycle sync failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void OnMovementLifecycle(PlayerMovement movement)
        {
            if (!PatchActive || !PersistenceReady || !EligibilityActive || movement == null)
                return;
            TryActivateInventory();
        }

        public static void HandlePagingInput()
        {
            if (!PatchActive || !PersistenceReady || !EligibilityActive || !InventoryReady || saveUnsafe)
                return;
            try
            {
                AuditPagingInputComponents();
                if (!Application.isFocused)
                {
                    AuditPagingInputGate("game_not_focused");
                    return;
                }

                string captureReason;
                if (IsTypingOrCapturingInput(out captureReason))
                {
                    AuditPagingInputGate("capture_" + captureReason);
                    return;
                }

                if (GameInput.Instance == null
                    || GameInput.Instance.GetAction(GameInput.ButtonCode.InventoryLeft) == null
                    || GameInput.Instance.GetAction(GameInput.ButtonCode.InventoryRight) == null)
                {
                    AuditPagingInputGate("canonical_action_unavailable");
                    return;
                }

                bool left = GameInput.GetButtonDown(GameInput.ButtonCode.InventoryLeft);
                bool right = GameInput.GetButtonDown(GameInput.ButtonCode.InventoryRight);
                AuditPagingInputGate("ready");
                if (!pagingInputSampleKnown || left != lastPagingLeft || right != lastPagingRight)
                {
                    pagingInputSampleKnown = true;
                    lastPagingLeft = left;
                    lastPagingRight = right;
                    Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.paging_input_sample left={0} right={1}", left, right));
                }
                if (left == right)
                    return;
                Audit(string.Format(CultureInfo.InvariantCulture,
                    "op=player.settings.page_input_ignored reason=canonical_callback_path delta={0}", left ? -1 : 1));
            }
            catch (Exception ex)
            {
                Warn("Could not read the canonical player inventory paging actions: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void HandleCanonicalPagingCallback(int delta)
        {
            if (!PatchActive || !PersistenceReady || !EligibilityActive || !InventoryReady || saveUnsafe || delta == 0)
                return;
            try
            {
                if (!Application.isFocused)
                {
                    AuditPagingInputGate("game_not_focused");
                    return;
                }
                string captureReason;
                if (IsTypingOrCapturingInput(out captureReason))
                {
                    AuditPagingInputGate("capture_" + captureReason);
                    return;
                }
                AuditPagingInputGate("canonical_callback_ready");
                Audit(string.Format(CultureInfo.InvariantCulture,
                    "op=player.settings.canonical_input_callback delta={0}", delta));
                float now = Time.unscaledTime;
                if (now < nextPageSwapAllowedTime)
                {
                    AuditPagingInputGate("page_swap_cooldown");
                    return;
                }

                if (!TryMovePage(delta))
                    return;

                float intervalMilliseconds = lastSuccessfulPageSwapTime < 0f
                    ? -1f
                    : (now - lastSuccessfulPageSwapTime) * 1000f;
                lastSuccessfulPageSwapTime = now;
                nextPageSwapAllowedTime = now + PageSwapCooldownSeconds;
                ShowSwapNotice();
                Audit(string.Format(CultureInfo.InvariantCulture,
                    "op=player.settings.page_swap_guard cooldownMs={0:0} intervalMs={1:0.0}",
                    PageSwapCooldownSeconds * 1000f, intervalMilliseconds));
            }
            catch (Exception ex)
            {
                Warn("Could not handle the canonical player inventory paging callback: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // When virtual paging is active, the canonical callback is owned by
        // the bridge. Letting GameInput's original handler continue would
        // also advance the native equipped-slot selection, producing a
        // second action after the page swap. Fixed mode 1 stays vanilla.
        public static bool ShouldSuppressNativeInventoryNavigation()
        {
            return PatchActive && PersistenceReady && EligibilityActive && InventoryReady
                && !saveUnsafe && PageCount() > 1;
        }

        private static void EnsureCanonicalKeyboardPagingBindings()
        {
            if (canonicalKeyboardBindingsApplied || GameInput.Instance == null)
                return;
            InputAction left = GameInput.Instance.GetAction(GameInput.ButtonCode.InventoryLeft);
            InputAction right = GameInput.Instance.GetAction(GameInput.ButtonCode.InventoryRight);
            if (left == null || right == null)
                return;
            bool leftAdded = EnsureCanonicalKeyboardBinding(left, "<Keyboard>/leftArrow");
            bool rightAdded = EnsureCanonicalKeyboardBinding(right, "<Keyboard>/rightArrow");
            canonicalKeyboardBindingsApplied = true;
            Audit(string.Format(CultureInfo.InvariantCulture,
                "op=player.settings.canonical_input_binding_sync leftAdded={0} rightAdded={1} leftBindings={2} rightBindings={3}",
                leftAdded, rightAdded, DescribeBindings(left), DescribeBindings(right)));
        }

        private static bool EnsureCanonicalKeyboardBinding(InputAction action, string path)
        {
            for (int i = 0; i < action.bindings.Count; i++)
                if (string.Equals(action.bindings[i].effectivePath, path, StringComparison.OrdinalIgnoreCase))
                    return false;
            bool wasEnabled = action.enabled;
            if (wasEnabled)
                action.Disable();
            try
            {
                InputActionSetupExtensions.AddBinding(action, path, null, null, null);
            }
            finally
            {
                if (wasEnabled)
                    action.Enable();
            }
            return true;
        }

        public static void AuditBridgeInputBoundary(string phase)
        {
            if (!PatchActive || !PersistenceReady || !EligibilityActive || !InventoryReady || saveUnsafe)
                return;
            AuditCanonicalInputState(phase, false);
        }

        public static void AuditCanonicalInputCallback(string direction)
        {
            if (!PatchActive || !PersistenceReady || !EligibilityActive || !InventoryReady || saveUnsafe)
                return;
            AuditCanonicalInputState("callback_" + direction, true);
        }

        private static void AuditCanonicalInputState(string phase, bool always)
        {
            try
            {
                InputAction left = GameInput.Instance == null ? null : GameInput.Instance.GetAction(GameInput.ButtonCode.InventoryLeft);
                InputAction right = GameInput.Instance == null ? null : GameInput.Instance.GetAction(GameInput.ButtonCode.InventoryRight);
                string state = string.Format(CultureInfo.InvariantCulture,
                    "left={0} right={1} leftState={2} rightState={3} leftPhase={4} rightPhase={5} leftTriggered={6} rightTriggered={7} leftPressedFrame={8} rightPressedFrame={9} leftBindings={10} rightBindings={11} leftControls={12} rightControls={13}",
                    GameInput.Instance != null && GameInput.GetButtonDown(GameInput.ButtonCode.InventoryLeft),
                    GameInput.Instance != null && GameInput.GetButtonDown(GameInput.ButtonCode.InventoryRight),
                    DescribeAction(left), DescribeAction(right),
                    left == null ? "none" : left.phase.ToString(),
                    right == null ? "none" : right.phase.ToString(),
                    left != null && left.triggered,
                    right != null && right.triggered,
                    left != null && left.WasPressedThisFrame(),
                    right != null && right.WasPressedThisFrame(),
                    DescribeBindings(left), DescribeBindings(right),
                    DescribeControls(left), DescribeControls(right));
                if (!always && string.Equals(lastCanonicalInputState, state, StringComparison.Ordinal))
                    return;
                lastCanonicalInputState = state;
                Audit("op=player.settings.canonical_input_state phase=" + phase + " " + state);
            }
            catch (Exception ex)
            {
                Audit("op=player.settings.canonical_input_state phase=" + phase + " error=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private static string DescribeAction(InputAction action)
        {
            if (action == null)
                return "missing";
            return string.Format(CultureInfo.InvariantCulture, "name={0},enabled={1},type={2}",
                action.name ?? "", action.enabled, action.type);
        }

        private static string DescribeBindings(InputAction action)
        {
            if (action == null)
                return "missing";
            var values = new List<string>();
            for (int i = 0; i < action.bindings.Count && i < 16; i++)
                values.Add(action.bindings[i].effectivePath ?? "");
            return string.Join("|", values.ToArray());
        }

        private static string DescribeControls(InputAction action)
        {
            if (action == null)
                return "missing";
            var values = new List<string>();
            for (int i = 0; i < action.controls.Count && i < 16; i++)
                values.Add(action.controls[i] == null ? "" : action.controls[i].path ?? "");
            return string.Join("|", values.ToArray());
        }

        // ItemSlot mutation events are not stable across the reviewed IL2CPP
        // build, so use a bounded dirty comparison instead. This performs no
        // disk I/O on every frame and only watches the eight native hotbar slots
        // while an extra virtual page is visible.
        public static void TickInventoryPersistence()
        {
            if (!PatchActive || !PersistenceReady || !EligibilityActive || !InventoryReady
                || activeInventory == null || currentPage == 0 || reentrancyGuard || saveUnsafe)
                return;
            if (DateTime.UtcNow < nextInventoryDirtyCheckUtc)
                return;
            nextInventoryDirtyCheckUtc = DateTime.UtcNow.AddMilliseconds(250);
            try
            {
                string current = CaptureInventoryJson(activeInventory);
                if (!string.Equals(current, lastObservedInventoryJson, StringComparison.Ordinal))
                {
                    lastObservedInventoryJson = current;
                    inventorySurfaceDirty = true;
                    Audit(string.Format(CultureInfo.InvariantCulture,
                        "op=player.settings.inventory_page_dirty page={0} jsonLength={1}",
                        currentPage, current == null ? 0 : current.Length));
                }
                if (inventorySurfaceDirty && DateTime.UtcNow >= nextInventoryFlushRetryUtc)
                {
                    if (FlushSidecar())
                    {
                        inventorySurfaceDirty = false;
                        nextInventoryFlushRetryUtc = DateTime.MinValue;
                        inventoryFlushRetryDelayMilliseconds = 2000;
                        if (inventoryPersistenceFailureActive)
                        {
                            inventoryPersistenceFailureActive = false;
                            Audit("op=player.settings.inventory_persistence_recovered");
                        }
                    }
                    else
                    {
                        nextInventoryFlushRetryUtc = DateTime.UtcNow.AddMilliseconds(inventoryFlushRetryDelayMilliseconds);
                        inventoryFlushRetryDelayMilliseconds = Math.Min(30000, inventoryFlushRetryDelayMilliseconds * 2);
                        if (!inventoryPersistenceFailureActive)
                        {
                            inventoryPersistenceFailureActive = true;
                            Warn(lastInventoryError);
                            Audit("op=player.settings.inventory_persistence_failed retryBackoffMs=" + inventoryFlushRetryDelayMilliseconds.ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lastInventoryError = "Inventory mutation check failed: " + ex.GetType().Name + ": " + ex.Message;
                Warn(lastInventoryError);
            }
        }

        public static PlayerSettingsSnapshot ReadSnapshot()
        {
            int pages = PageCount();
            return new PlayerSettingsSnapshot
            {
                ConfiguredInventoryMode = configuredInventoryMode,
                ConfiguredSpeedMultiplier = configuredSpeedMultiplier,
                BaseInventorySlots = InventoryPagingModel.NativePageWidth,
                InventorySlotCount = InventoryPagingModel.NativePageWidth * pages,
                InventoryPage = Math.Min(currentPage, Math.Max(0, pages - 1)),
                InventoryPageCount = pages,
                InventoryReady = InventoryReady,
                NativeHotbarSlots = NativeHotbarSlots,
                CurrentPage = currentPage,
                AllocatedPageCount = AllocatedPageCount,
                ConfiguredPageCount = InventoryPagingModel.ConfiguredPageCountForMode(configuredInventoryMode),
                SaveScope = activeSaveScope,
                SidecarLoaded = sidecarLoaded,
                LastInventoryError = lastInventoryError,
                PlayerSpeedMultiplier = configuredSpeedMultiplier
            };
        }

        public static bool ApplySettings(int inventoryMode, float speedMultiplier, out string error)
        {
            error = null;
            if (!PersistenceReady || !PatchActive)
            {
                error = "Player runtime settings are unavailable on this game build.";
                return false;
            }
            if (!IsValidInventoryMode(inventoryMode) || !IsValidSpeed(speedMultiplier))
            {
                error = "Inventory mode or player speed is outside the allowed range.";
                return false;
            }

            int oldMode = configuredInventoryMode;
            float oldSpeed = configuredSpeedMultiplier;
            long oldConfigRevision = ConfigRevision;
            int oldPage = currentPage;
            int oldAllocated = allocatedPageCount;
            string oldSurfaceJson = InventoryReady && activeInventory != null ? CaptureInventoryJson(activeInventory) : null;
            JObject oldRoot = (JObject)root.DeepClone();
            int newPageCount = inventoryMode == InventoryModeOnDemand
                ? Math.Max(1, Math.Min(InventoryPagingModel.Mode4PageCap, allocatedPageCount))
                : inventoryMode;

            if (InventoryReady)
            {
                SnapshotCurrentPage();
                if (oldPage >= newPageCount && !TrySwitchToPage(0))
                {
                    error = "The inventory could not return to native page 0 safely; no setting was changed.";
                    return false;
                }
            }

            bool configSaved = false;
            try
            {
                JObject candidate = (JObject)root.DeepClone();
                candidate["inventoryMode"] = inventoryMode;
                candidate["speedMultiplier"] = speedMultiplier;
                candidate["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                SaveRootAtomically(candidate);
                configSaved = true;
                root = candidate;
                configuredInventoryMode = inventoryMode;
                configuredSpeedMultiplier = speedMultiplier;
                // Retain the bounded saved bank across mode changes. The mode
                // controls how many pages are addressable now; it never deletes
                // higher-page ItemSets that the user may expose again later.
                allocatedPageCount = InventoryPagingModel.AllocatedPageCountForMode(inventoryMode, oldAllocated);
                long committedRevision = ConfigRevision;
                string commitError;
                if (!InventoryPagingModel.TryCommitAfterPersistence(
                    FlushSidecar,
                    () => Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.apply inventoryMode={0} speedMultiplier={1:0.###} nativeWidth={2} configRevision={3}", configuredInventoryMode, configuredSpeedMultiplier, InventoryPagingModel.NativePageWidth, committedRevision)),
                    ref committedRevision,
                    out commitError))
                    throw new InvalidOperationException(string.IsNullOrEmpty(lastInventoryError) ? commitError : lastInventoryError);
                ConfigRevision = committedRevision;
                speedWasApplied = false;
                return true;
            }
            catch (Exception ex)
            {
                configuredInventoryMode = oldMode;
                configuredSpeedMultiplier = oldSpeed;
                ConfigRevision = oldConfigRevision;
                currentPage = oldPage;
                allocatedPageCount = oldAllocated;
                if (InventoryReady && activeInventory != null && oldSurfaceJson != null)
                {
                    try
                    {
                        reentrancyGuard = true;
                        ApplyInventoryJson(oldSurfaceJson);
                        RefreshNativeSlotUIs();
                    }
                    catch (Exception rollbackSurfaceEx)
                    {
                        saveUnsafe = true;
                        lastInventoryError = "Player setting rollback could not restore the old eight-slot surface: " + rollbackSurfaceEx.GetType().Name + ": " + rollbackSurfaceEx.Message;
                    }
                    finally { reentrancyGuard = false; }
                }
                if (configSaved)
                {
                    try { SaveRootAtomically(oldRoot); root = oldRoot; }
                    catch (Exception rollbackConfigEx) { Warn("Player setting configuration rollback failed: " + rollbackConfigEx.Message); }
                }
                error = "Player runtime settings were rolled back: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public static void RestoreLiveOverrides()
        {
            bool restored = false;
            try
            {
                restored = RestorePageZeroBeforeDisable();
                FlushSidecar();
            }
            catch (Exception ex) { Warn("Could not flush inventory pages during bridge shutdown: " + ex.Message); }
            speedWasApplied = false;
            InventoryReady = false;
            if (restored)
                currentPage = 0;
        }

        public static void ClearManagedState()
        {
            RestorePageZeroBeforeDisable();
            FlushSidecar();
            EligibilityActive = false;
            InventoryReady = false;
        }

        public static bool TryMovePage(int delta)
        {
            if (!InventoryReady || activeInventory == null || delta == 0)
                return false;

            int oldAllocated = allocatedPageCount;
            int pages = PageCount();
            int target;
            if (!InventoryPagingModel.TryGetBoundedTarget(currentPage, pages, delta, out target))
            {
                if (delta > 0 && configuredInventoryMode == InventoryModeOnDemand && pages < InventoryPagingModel.Mode4PageCap)
                {
                    allocatedPageCount = pages + 1;
                    target = pages;
                }
                else
                {
                    Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.page_input_ignored reason=bound page={0} pageCount={1} delta={2}", currentPage, pages, delta));
                    return false;
                }
            }
            bool switched = TrySwitchToPage(target);
            if (!switched)
                allocatedPageCount = oldAllocated;
            return switched;
        }

        public static void OnInventoryUiReady(PlayerInventory inventory)
        {
            if (!PatchActive || !EligibilityActive || inventory == null)
                return;
            activeInventory = inventory;
            TryActivateInventory();
        }

        public static void OnPlayerInventoryLoaded(Player player)
        {
            if (player == null || Player.Local == null || player != Player.Local)
                return;
            saveUnsafe = false;
            Audit("op=player.settings.inventory_recovery saveUnsafe=false reason=local_vanilla_load_complete");
            InventoryReady = false;
            activeInventory = null;
            ResetPages();
            TryActivateInventory();
        }

        public static bool BeginVanillaInventorySave(Player player, out InventorySaveState state)
        {
            state = new InventorySaveState();
            if (saveUnsafe)
            {
                if (!InventoryPagingModel.ShouldRunVanillaSave(false, true))
                    throw new InvalidOperationException("Vanilla inventory serialization was blocked: native page 0 is not safely restored.");
            }
            if (!InventoryReady || activeInventory == null || player == null || player != Player.Local)
                return true;

            try
            {
                SnapshotCurrentPage();
                state.Active = currentPage != 0;
                state.PreviousPage = currentPage;
                if (state.Active && !TrySwitchToPage(0))
                {
                    lastInventoryError = "Vanilla save was blocked because page 0 could not be restored safely.";
                    saveUnsafe = true;
                    Warn(lastInventoryError);
                    throw new InvalidOperationException(lastInventoryError);
                }
                if (!FlushSidecar())
                    throw new InvalidOperationException(string.IsNullOrEmpty(lastInventoryError) ? "Inventory page sidecar could not be persisted." : lastInventoryError);
                return true;
            }
            catch (Exception ex)
            {
                lastInventoryError = "Vanilla save preparation failed: " + ex.GetType().Name + ": " + ex.Message;
                saveUnsafe = true;
                Warn(lastInventoryError);
                throw new InvalidOperationException(lastInventoryError, ex);
            }
        }

        public static void EndVanillaInventorySave(InventorySaveState state)
        {
            if (state == null || state.Consumed)
                return;
            state.Consumed = true;
            if (!state.Active || !InventoryReady)
                return;
            try
            {
                if (!TrySwitchToPage(state.PreviousPage))
                    Warn("Vanilla save completed, but the previous inventory page could not be restored safely.");
            }
            catch (Exception ex)
            {
                Warn("Could not restore the visible inventory page after vanilla save: " + ex.Message);
            }
        }

        public static bool BeforeGameSave(out InventorySaveState state)
        {
            state = new InventorySaveState();
            if (saveUnsafe)
                return InventoryPagingModel.ShouldRunVanillaSave(false, true);
            if (!InventoryReady || activeInventory == null)
                return true;
            try
            {
                SnapshotCurrentPage();
                state.Active = currentPage != 0;
                state.PreviousPage = currentPage;
                if (state.Active && !TrySwitchToPage(0))
                {
                    saveUnsafe = true;
                    lastInventoryError = "Game save was blocked because page 0 could not be restored safely.";
                    Warn(lastInventoryError);
                    return false;
                }
                if (!FlushSidecar())
                    throw new InvalidOperationException(string.IsNullOrEmpty(lastInventoryError) ? "Inventory page sidecar could not be persisted." : lastInventoryError);
                return true;
            }
            catch (Exception ex)
            {
                saveUnsafe = true;
                lastInventoryError = "Game save preparation failed: " + ex.GetType().Name + ": " + ex.Message;
                Warn(lastInventoryError);
                return false;
            }
        }

        private static bool RestorePageZeroBeforeDisable()
        {
            if (!InventoryReady || activeInventory == null || currentPage == 0)
                return true;
            try
            {
                SnapshotCurrentPage();
                if (!TrySwitchToPage(0))
                {
                    saveUnsafe = true;
                    lastInventoryError = "Paging deactivation was blocked because native page 0 could not be restored.";
                    Warn(lastInventoryError);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                saveUnsafe = true;
                lastInventoryError = "Paging deactivation failed while restoring page 0: " + ex.GetType().Name + ": " + ex.Message;
                Warn(lastInventoryError);
                return false;
            }
        }

        private static void TryActivateInventory()
        {
            if (!PatchActive || !PersistenceReady || !EligibilityActive || reentrancyGuard || saveUnsafe)
                return;

            try
            {
                Player player = Player.Local;
                if (player == null || player.gameObject == null)
                {
                    SetInventoryFailure("Local player is unavailable.");
                    return;
                }

                PlayerInventory inventory = activeInventory;
                if (inventory == null)
                    inventory = player.gameObject.GetComponent<PlayerInventory>();
                if (inventory == null)
                    inventory = player.gameObject.GetComponentInChildren<PlayerInventory>(true);
                if (inventory == null)
                {
                    SetInventoryFailure("Local PlayerInventory is unavailable.");
                    return;
                }

                if (inventory.hotbarSlots == null || inventory.hotbarSlots.Count != InventoryPagingModel.NativePageWidth)
                {
                    SetInventoryFailure("Native hotbar invariant failed: expected exactly 8 hotbar slots.");
                    return;
                }
                if (inventory.SlotUIs == null || inventory.SlotUIs.Count < InventoryPagingModel.NativePageWidth)
                {
                    SetInventoryFailure("Native hotbar invariant failed: fewer than 8 corresponding slot UIs.");
                    return;
                }
                string savePath;
                if (!TryGetSavePath(out savePath))
                {
                    SetInventoryFailure("Active save scope is unavailable.");
                    return;
                }

                string scope = ComputeInventoryScope(savePath, player);
                if (!InventoryReady || !string.Equals(scope, activeSaveScope, StringComparison.Ordinal) || activeInventory != inventory)
                {
                    activeInventory = inventory;
                    activeSaveScope = scope;
                    sidecarPath = Path.Combine(MelonEnvironment.UserDataDirectory, SidecarPrefix + scope + SidecarSuffix);
                    currentPage = 0;
                    allocatedPageCount = 1;
                    pageZeroJson = CaptureInventoryJson(inventory);
                    extraPageJson.Clear();
                    if (sidecarRejected && string.Equals(sidecarRejectedScope, activeSaveScope, StringComparison.Ordinal)
                        && string.Equals(sidecarRejectedPath, sidecarPath, StringComparison.Ordinal))
                    {
                        SetInventoryFailure(lastInventoryError);
                        return;
                    }
                    if (!LoadSidecar())
                    {
                        SetInventoryFailure(lastInventoryError);
                        return;
                    }
                    // Fixed modes own their complete configured page surface from
                    // activation onward; mode 4 remains on-demand and grows only
                    // when the user advances past its current allocation.
                    allocatedPageCount = InventoryPagingModel.AllocatedPageCountForMode(configuredInventoryMode, allocatedPageCount);
                    lastObservedInventoryJson = pageZeroJson;
                    inventorySurfaceDirty = false;
                    nextInventoryDirtyCheckUtc = DateTime.UtcNow.AddMilliseconds(250);
                    nextInventoryFlushRetryUtc = DateTime.MinValue;
                    inventoryFlushRetryDelayMilliseconds = 2000;
                    inventoryPersistenceFailureActive = false;
                    InventoryReady = true;
                    lastInventoryError = string.Empty;
                    Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.inventory_ready inventoryReady=true nativeHotbarSlots={0} slotUiCount={1} currentPage={2} allocatedPageCount={3} saveScope={4} sidecarLoaded={5}", InventoryPagingModel.NativePageWidth, inventory.SlotUIs.Count, currentPage, allocatedPageCount, activeSaveScope, sidecarLoaded));
                }
                EnsureCanonicalKeyboardPagingBindings();
                EnsureSwapNotice();
            }
            catch (Exception ex)
            {
                SetInventoryFailure("Inventory activation failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool TrySwitchToPage(int targetPage)
        {
            if (!InventoryReady || activeInventory == null || targetPage < 0 || targetPage >= PageCount())
                return false;
            if (targetPage == currentPage)
                return true;
            if (reentrancyGuard)
                return false;

            reentrancyGuard = true;
            string oldJson = CaptureInventoryJson(activeInventory);
            int oldPage = currentPage;
            int oldAllocated = allocatedPageCount;
            int selectedIndex = activeInventory.EquippedSlotIndex;
            string oldPageZeroJson = pageZeroJson;
            Dictionary<int, string> oldExtraPageJson = new Dictionary<int, string>(extraPageJson);
            try
            {
                SnapshotCurrentPage(oldJson);
                int deselectedBeforeMutation = DeselectSelectedHotbarSlots();
                ReconcileUnequippedPlayerState();
                bool immediateOldSurfaceClear = VerifyUnequippedSurface(out string oldSurfaceState);
                Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.page_unequip_immediate page={0} frame={1} phase=before_mutation deselected={2} verified={3} {4}", oldPage, Time.frameCount, deselectedBeforeMutation, immediateOldSurfaceClear, oldSurfaceState));
                string targetJson = GetPageJson(targetPage);
                ApplyInventoryJson(targetJson);
                currentPage = targetPage;
                int deselectedAfterMutation = DeselectSelectedHotbarSlots();
                ReconcileUnequippedPlayerState();
                RefreshNativeSlotUIs();
                bool immediateTargetClear = VerifyUnequippedSurface(out string targetSurfaceState);
                if (!FlushSidecar())
                    throw new InvalidOperationException(string.IsNullOrEmpty(lastInventoryError) ? "Inventory page sidecar could not be persisted." : lastInventoryError);
                deferredUnequipFramesRemaining = 2;
                deferredUnequipRecoveryAttempted = false;
                deferredUnequipPage = currentPage;
                Audit(string.Format(CultureInfo.InvariantCulture, "op=player.settings.page_changed page={0} pageCount={1} nativeHotbarSlots={2} deselectedBeforeMutation={3} deselectedAfterMutation={4} immediateUnequipped={5} {6}", currentPage, PageCount(), InventoryPagingModel.NativePageWidth, deselectedBeforeMutation, deselectedAfterMutation, immediateTargetClear, targetSurfaceState));
                return true;
            }
            catch (Exception ex)
            {
                try
                {
                    pageZeroJson = oldPageZeroJson;
                    extraPageJson.Clear();
                    foreach (KeyValuePair<int, string> pair in oldExtraPageJson)
                        extraPageJson[pair.Key] = pair.Value;
                    allocatedPageCount = oldAllocated;
                    ApplyInventoryJson(oldJson);
                    currentPage = oldPage;
                    RestoreSelection(selectedIndex);
                }
                catch (Exception rollbackEx)
                {
                    lastInventoryError = "Inventory page rollback failed: " + rollbackEx.GetType().Name + ": " + rollbackEx.Message;
                }
                if (string.IsNullOrEmpty(lastInventoryError))
                    lastInventoryError = "Inventory page swap failed: " + ex.GetType().Name + ": " + ex.Message;
                Warn(lastInventoryError);
                return false;
            }
            finally
            {
                reentrancyGuard = false;
            }
        }

        private static void SnapshotCurrentPage()
        {
            if (InventoryReady && activeInventory != null)
                SnapshotCurrentPage(CaptureInventoryJson(activeInventory));
        }

        private static void SnapshotCurrentPage(string json)
        {
            if (currentPage == 0)
                pageZeroJson = json;
            else
                extraPageJson[currentPage] = json;
        }

        private static string GetPageJson(int page)
        {
            if (page == 0)
                return pageZeroJson;
            string json;
            return extraPageJson.TryGetValue(page, out json) ? json : null;
        }

        private static string CaptureInventoryJson(PlayerInventory inventory)
        {
            if (inventory == null || inventory.hotbarSlots == null || inventory.hotbarSlots.Count != InventoryPagingModel.NativePageWidth)
                throw new InvalidDataException("The native hotbar surface is not exactly 8 slots.");
            Il2CppReferenceArray<ItemSlot> slots = new Il2CppReferenceArray<ItemSlot>(InventoryPagingModel.NativePageWidth);
            for (int i = 0; i < InventoryPagingModel.NativePageWidth; i++)
                slots[i] = inventory.hotbarSlots[i];
            return new ItemSet(slots).GetJSON();
        }

        private static void ApplyInventoryJson(string json)
        {
            if (activeInventory == null || activeInventory.hotbarSlots == null || activeInventory.hotbarSlots.Count != InventoryPagingModel.NativePageWidth)
                throw new InvalidDataException("The native hotbar surface is not exactly 8 slots.");

            if (string.IsNullOrEmpty(json))
            {
                for (int i = 0; i < InventoryPagingModel.NativePageWidth; i++)
                {
                    activeInventory.hotbarSlots[i].SetStoredItem(null, false);
                    activeInventory.hotbarSlots[i].SetPlayerFilter(null, false);
                }
                return;
            }

            DeserializedItemSet parsed;
            if (!ItemSet.TryDeserialize(json, out parsed) || parsed == null || parsed.Items == null || parsed.Items.Length != InventoryPagingModel.NativePageWidth)
                throw new InvalidDataException("An inventory page sidecar entry could not be deserialized as eight native slots.");

            for (int i = 0; i < InventoryPagingModel.NativePageWidth; i++)
            {
                activeInventory.hotbarSlots[i].SetStoredItem(parsed.GetItemAt(i), false);
                activeInventory.hotbarSlots[i].SetPlayerFilter(parsed.GetSlotFilterAt(i), false);
            }
        }

        private static void RefreshNativeSlotUIs()
        {
            if (activeInventory == null || activeInventory.SlotUIs == null)
                return;
            for (int i = 0; i < InventoryPagingModel.NativePageWidth && i < activeInventory.SlotUIs.Count; i++)
            {
                ItemSlotUI ui = activeInventory.SlotUIs[i];
                if (ui != null)
                    ui.UpdateUI();
            }
        }

        private static int DeselectSelectedHotbarSlots()
        {
            if (activeInventory == null || activeInventory.hotbarSlots == null || activeInventory.hotbarSlots.Count != InventoryPagingModel.NativePageWidth)
                return 0;

            int deselected = 0;
            for (int i = 0; i < InventoryPagingModel.NativePageWidth; i++)
            {
                HotbarSlot slot = activeInventory.hotbarSlots[i];
                if (slot == null)
                    continue;
                if (slot.IsSelected || slot._equippable != null || slot._equippedItem != null)
                {
                    slot.Deselect();
                    deselected++;
                }
            }
            return deselected;
        }

        private static void ReconcileUnequippedPlayerState()
        {
            PlayerInventory inventory = activeInventory;
            if (inventory != null)
            {
                if (inventory.Equippable != null)
                    inventory.SetEquippable(null);
                inventory.EquippedSlotIndex = -1;
                inventory.PriorEquippedSlotIndex = -1;
                inventory.PreviousEquippedSlotIndex = -1;
                inventory.EquippedSlotChanged();
            }

            Player player = Player.Local;
            if (player != null)
            {
                player.SetEquippedSlotIndex(-1);
                player.UnequipAll();
            }
        }

        private static bool VerifyUnequippedSurface(out string state)
        {
            int selectedCount = 0;
            int staleEquippableCount = 0;
            int staleEquippedItemCount = 0;
            bool nativeSurfaceValid = activeInventory != null
                && activeInventory.hotbarSlots != null
                && activeInventory.hotbarSlots.Count == InventoryPagingModel.NativePageWidth;
            if (nativeSurfaceValid)
            {
                for (int i = 0; i < InventoryPagingModel.NativePageWidth; i++)
                {
                    HotbarSlot slot = activeInventory.hotbarSlots[i];
                    if (slot == null)
                    {
                        nativeSurfaceValid = false;
                        continue;
                    }
                    if (slot.IsSelected)
                        selectedCount++;
                    if (slot._equippable != null)
                        staleEquippableCount++;
                    if (slot._equippedItem != null)
                        staleEquippedItemCount++;
                }
            }

            Player player = Player.Local;
            int networkedEquippedCount = 0;
            if (player != null && player._networkedEquipper != null && player._networkedEquipper._allEquippedItems != null)
            {
                for (int i = 0; i < player._networkedEquipper._allEquippedItems.Count; i++)
                {
                    var handler = player._networkedEquipper._allEquippedItems[i];
                    if (handler != null && handler.IsEquipped)
                        networkedEquippedCount++;
                }
            }

            int inventoryIndex = activeInventory == null ? -2 : activeInventory.EquippedSlotIndex;
            int priorIndex = activeInventory == null ? -2 : activeInventory.PriorEquippedSlotIndex;
            int previousIndex = activeInventory == null ? -2 : activeInventory.PreviousEquippedSlotIndex;
            bool inventoryEquippable = activeInventory != null && activeInventory.Equippable != null;
            bool inventoryItem = activeInventory != null && activeInventory.EquippedItem != null;
            bool anythingEquipped = activeInventory != null && activeInventory.isAnythingEquipped;
            int playerIndex = player == null ? -2 : player.EquippedItemSlotIndex;
            bool playerItem = player != null && player.GetEquippedItem() != null;
            bool verified = nativeSurfaceValid
                && selectedCount == 0
                && staleEquippableCount == 0
                && staleEquippedItemCount == 0
                && inventoryIndex == -1
                && priorIndex == -1
                && previousIndex == -1
                && !inventoryEquippable
                && !inventoryItem
                && !anythingEquipped
                && playerIndex == -1
                && !playerItem
                && networkedEquippedCount == 0;
            state = string.Format(CultureInfo.InvariantCulture, "nativeSurfaceValid={0} selectedCount={1} staleEquippableCount={2} staleEquippedItemCount={3} inventoryIndex={4} priorIndex={5} previousIndex={6} inventoryEquippable={7} inventoryItem={8} anythingEquipped={9} playerIndex={10} playerItem={11} networkedEquippedCount={12}", nativeSurfaceValid, selectedCount, staleEquippableCount, staleEquippedItemCount, inventoryIndex, priorIndex, previousIndex, inventoryEquippable, inventoryItem, anythingEquipped, playerIndex, playerItem, networkedEquippedCount);
            return verified;
        }

        private static void RestoreSelection(int selectedIndex)
        {
            if (activeInventory == null || selectedIndex < 0 || selectedIndex >= InventoryPagingModel.NativePageWidth)
                return;
            HotbarSlot slot = activeInventory.hotbarSlots[selectedIndex];
            if (slot == null || slot.ItemInstance == null)
                return;
            activeInventory.Equip(slot);
            Player player = Player.Local;
            if (player != null)
                player.SetEquippedSlotIndex(selectedIndex);
        }

        private static int PageCount()
        {
            return InventoryPagingModel.PageCountForMode(configuredInventoryMode, allocatedPageCount);
        }

        private static bool LoadSidecar()
        {
            sidecarLoaded = false;
            if (sidecarRejected && string.Equals(sidecarRejectedScope, activeSaveScope, StringComparison.Ordinal)
                && string.Equals(sidecarRejectedPath, sidecarPath, StringComparison.Ordinal))
                return false;
            sidecarRejected = false;
            if (string.IsNullOrEmpty(sidecarPath) || !File.Exists(sidecarPath))
                return true;
            try
            {
                FileInfo info = new FileInfo(sidecarPath);
                if (info.Length > MaxSidecarBytes)
                    throw new InvalidDataException("Inventory page sidecar exceeds 256 KiB.");
                JsonLoadSettings settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Ignore
                };
                JObject value;
                using (StreamReader reader = new StreamReader(sidecarPath, new UTF8Encoding(false, true)))
                using (JsonTextReader json = new JsonTextReader(reader) { DateParseHandling = DateParseHandling.None })
                {
                    value = JObject.Load(json, settings);
                    if (json.Read())
                        throw new InvalidDataException("Inventory page sidecar has trailing content.");
                }
                int persistedPageCount = value.Value<int?>("allocatedPageCount") ?? 1;
                JArray pages = value["pages"] as JArray;
                string metadataError = null;
                if (pages == null || !InventoryPagingModel.ValidateSidecarMetadata(
                    value.Value<int?>("version") ?? 0,
                    value.Value<string>("gameVersion"),
                    value.Value<string>("gameBuild"),
                    value.Value<string>("saveScope"),
                    persistedPageCount,
                    pages == null ? -1 : pages.Count,
                    GameOperations.ExpectedGameVersion,
                    GameOperations.ExpectedGameBuild,
                    activeSaveScope,
                    out metadataError))
                {
                    throw new InvalidDataException(pages == null ? "Inventory page sidecar page array is invalid." : metadataError);
                }
                if (pages.Count > InventoryPagingModel.Mode4PageCap)
                    throw new InvalidDataException("Inventory page sidecar page array is invalid.");
                allocatedPageCount = persistedPageCount;
                for (int i = 0; i < pages.Count; i++)
                {
                    string pageJson = pages[i].Type == JTokenType.Null ? null : pages[i].Value<string>();
                    if (!string.IsNullOrEmpty(pageJson))
                    {
                        // Parse before accepting so corrupt entries fail closed.
                        DeserializedItemSet parsed;
                        if (!ItemSet.TryDeserialize(pageJson, out parsed) || parsed == null || parsed.Items == null || parsed.Items.Length != InventoryPagingModel.NativePageWidth)
                            throw new InvalidDataException("Inventory page entry is not a valid eight-slot ItemSet.");
                        extraPageJson[i + 1] = pageJson;
                    }
                }
                sidecarLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                extraPageJson.Clear();
                allocatedPageCount = 1;
                sidecarLoaded = false;
                sidecarRejected = true;
                sidecarRejectedScope = activeSaveScope;
                sidecarRejectedPath = sidecarPath;
                lastInventoryError = "Ignored inventory page sidecar without changing vanilla inventory: " + ex.GetType().Name + ": " + ex.Message;
                Warn(lastInventoryError);
                return false;
            }
        }

        private static bool FlushSidecar()
        {
            if (!InventoryReady)
                return true;
            if (sidecarRejected || saveUnsafe || string.IsNullOrEmpty(activeSaveScope) || string.IsNullOrEmpty(sidecarPath) || activeInventory == null)
            {
                if (string.IsNullOrEmpty(lastInventoryError))
                    lastInventoryError = "Inventory page sidecar is unavailable; paging changes cannot be persisted.";
                return false;
            }
            try
            {
                string currentJson = CaptureInventoryJson(activeInventory);
                SnapshotCurrentPage(currentJson);
                JArray pages = new JArray();
                for (int page = 1; page < Math.Max(1, allocatedPageCount); page++)
                {
                    string json;
                    pages.Add(extraPageJson.TryGetValue(page, out json) ? (JToken)json : JValue.CreateNull());
                }
                JObject value = new JObject
                {
                    ["version"] = 1,
                    ["gameVersion"] = GameOperations.ExpectedGameVersion,
                    ["gameBuild"] = GameOperations.ExpectedGameBuild,
                    ["saveScope"] = activeSaveScope,
                    ["allocatedPageCount"] = allocatedPageCount,
                    ["pages"] = pages,
                    ["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                };
                SaveSidecarAtomically(value);
                sidecarLoaded = true;
                lastObservedInventoryJson = currentJson;
                inventorySurfaceDirty = false;
                Audit(string.Format(CultureInfo.InvariantCulture,
                    "op=player.settings.inventory_pages_persisted visiblePage={0} allocatedPageCount={1}",
                    currentPage, allocatedPageCount));
                if (lastInventoryError.StartsWith("Inventory page sidecar flush failed:", StringComparison.Ordinal))
                    lastInventoryError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                lastInventoryError = "Inventory page sidecar flush failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static void SaveSidecarAtomically(JObject value)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(value.ToString(Formatting.Indented));
            if (bytes.LongLength > MaxSidecarBytes)
                throw new InvalidDataException("Inventory page sidecar would exceed 256 KiB.");
            string temp = sidecarPath + ".tmp";
            string backup = sidecarPath + ".bak";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            try
            {
                if (File.Exists(sidecarPath))
                    File.Replace(temp, sidecarPath, backup, true);
                else
                    File.Move(temp, sidecarPath);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        private static bool TryGetSavePath(out string savePath)
        {
            savePath = string.Empty;
            if (!LoadManager.InstanceExists || LoadManager.Instance == null)
                return false;
            LoadManager load = LoadManager.Instance;
            if (load.ActiveSaveInfo != null && !string.IsNullOrEmpty(load.ActiveSaveInfo.SavePath))
                savePath = load.ActiveSaveInfo.SavePath;
            if (string.IsNullOrEmpty(savePath))
                savePath = load.LoadedGameFolderPath;
            return !string.IsNullOrEmpty(savePath);
        }

        private static string ComputeInventoryScope(string savePath, Player player)
        {
            string identity = string.Empty;
            try
            {
                identity = player == null ? string.Empty : (player.PlayerCode ?? string.Empty);
                if (identity.Length == 0 && player != null)
                    identity = player.PlayerName ?? string.Empty;
            }
            catch { }
            string seed = MarketValueScaling.ComputeSaveScope(savePath) + "|" + identity;
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(seed))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool IsTypingOrCapturingInput(out string reason)
        {
            reason = "unknown";
            try
            {
                if (activeInventory == null)
                {
                    reason = "inventory_unavailable";
                    return true;
                }
                if (!activeInventory.HotbarEnabled || !activeInventory.EquippingEnabled)
                {
                    reason = "hotbar_disabled";
                    return true;
                }
                if (activeInventory.AttachedScreen != null)
                {
                    reason = "incompatible_screen";
                    return true;
                }
                EventSystem eventSystem = EventSystem.current;
                if (eventSystem == null)
                {
                    reason = "event_system_unavailable";
                    return true;
                }
                GameObject selected = eventSystem.currentSelectedGameObject;
                if (selected == null)
                    return false;
                if (selected.name.IndexOf("input", StringComparison.OrdinalIgnoreCase) >= 0
                    || selected.name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reason = "typing_selected_object";
                    return true;
                }
                Component[] components = selected.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                    if (components[i] != null && components[i].GetType().Name.IndexOf("InputField", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        reason = "typing_input_field";
                        return true;
                    }
                return false;
            }
            catch
            {
                reason = "guard_exception";
                return true;
            }
        }

        private static void AuditPagingInputGate(string reason)
        {
            if (string.Equals(lastPagingInputGate, reason, StringComparison.Ordinal))
                return;
            lastPagingInputGate = reason;
            Audit("op=player.settings.paging_input_gate reason=" + reason);
        }

        private static void AuditPagingInputComponents()
        {
            try
            {
                bool hotbarEnabled = activeInventory != null && activeInventory.HotbarEnabled;
                bool equippingEnabled = activeInventory != null && activeInventory.EquippingEnabled;
                bool attachedScreen = activeInventory != null && activeInventory.AttachedScreen != null;
                EventSystem eventSystem = EventSystem.current;
                GameObject selected = eventSystem == null ? null : eventSystem.currentSelectedGameObject;
                bool leftAction = GameInput.Instance != null && GameInput.Instance.GetAction(GameInput.ButtonCode.InventoryLeft) != null;
                bool rightAction = GameInput.Instance != null && GameInput.Instance.GetAction(GameInput.ButtonCode.InventoryRight) != null;
                string selectedName = selected == null ? "none" : selected.name + "/" + selected.GetType().Name;
                string components = string.Format(CultureInfo.InvariantCulture,
                    "focused={0} hotbarEnabled={1} equippingEnabled={2} attachedScreen={3} eventSystem={4} selected={5} leftAction={6} rightAction={7}",
                    Application.isFocused, hotbarEnabled, equippingEnabled, attachedScreen, eventSystem != null, selectedName, leftAction, rightAction);
                if (string.Equals(lastPagingInputComponents, components, StringComparison.Ordinal))
                    return;
                lastPagingInputComponents = components;
                Audit("op=player.settings.paging_input_components " + components);
            }
            catch (Exception ex)
            {
                Audit("op=player.settings.paging_input_components error=" + ex.GetType().Name);
            }
        }

        private static void EnsureSwapNotice()
        {
            if (swapNoticeText != null && swapNoticeObject != null)
                return;
            if (!InventoryReady || activeInventory == null || activeInventory.SlotUIs == null
                || activeInventory.SlotUIs.Count < InventoryPagingModel.NativePageWidth)
                return;

            ItemSlotUI anchor = activeInventory.SlotUIs[InventoryPagingModel.NativePageWidth - 1];
            if (anchor == null || anchor.transform == null)
                return;

            try
            {
                GameObject notice = new GameObject("ScheduleIControlBridge_SwappingNotice");
                notice.AddComponent<RectTransform>();
                notice.AddComponent<CanvasRenderer>();
                notice.AddComponent<Text>();
                notice.transform.SetParent(anchor.transform, false);
                RectTransform rect = notice.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(1f, 0.5f);
                rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(-18f, -40f);
                rect.sizeDelta = new Vector2(90f, 24f);

                Text label = notice.GetComponent<Text>();
                label.text = "Swapping.";
                label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                label.fontSize = 14;
                label.alignment = TextAnchor.MiddleLeft;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.raycastTarget = false;
                label.color = new Color(1f, 1f, 1f, 0f);
                label.enabled = false;
                swapNoticeObject = notice;
                swapNoticeText = label;
                swapNoticeSetupFailureLogged = false;
            }
            catch (Exception ex)
            {
                if (!swapNoticeSetupFailureLogged)
                {
                    swapNoticeSetupFailureLogged = true;
                    Warn("Could not create the non-native swapping notice: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void ShowSwapNotice()
        {
            EnsureSwapNotice();
            if (swapNoticeText == null)
                return;
            swapNoticeExpiresTime = Time.unscaledTime + SwapNoticeLifetimeSeconds;
            swapNoticeText.enabled = true;
            swapNoticeText.color = new Color(1f, 1f, 1f, SwapNoticeAlpha);
        }

        public static void TickSwapNotice()
        {
            if (swapNoticeText == null)
                return;
            float remaining = swapNoticeExpiresTime - Time.unscaledTime;
            if (remaining <= 0f)
            {
                swapNoticeText.enabled = false;
                return;
            }
            float alpha = SwapNoticeAlpha * Mathf.Clamp01(remaining / SwapNoticeLifetimeSeconds);
            swapNoticeText.color = new Color(1f, 1f, 1f, alpha);
        }

        private static void DestroySwapNotice()
        {
            if (swapNoticeObject != null)
                UnityEngine.Object.Destroy(swapNoticeObject);
            swapNoticeObject = null;
            swapNoticeText = null;
            swapNoticeExpiresTime = 0f;
            swapNoticeSetupFailureLogged = false;
        }

        private static void ResetPages()
        {
            DestroySwapNotice();
            lock (Sync)
            {
                currentPage = 0;
                allocatedPageCount = 1;
                pageZeroJson = null;
                extraPageJson.Clear();
                sidecarLoaded = false;
                sidecarRejected = false;
                sidecarRejectedScope = string.Empty;
                sidecarRejectedPath = string.Empty;
                activeSaveScope = string.Empty;
                sidecarPath = null;
                lastInventoryError = string.Empty;
                lastObservedInventoryJson = null;
                inventorySurfaceDirty = false;
                nextInventoryDirtyCheckUtc = DateTime.MinValue;
                nextInventoryFlushRetryUtc = DateTime.MinValue;
                inventoryFlushRetryDelayMilliseconds = 2000;
                inventoryPersistenceFailureActive = false;
                lastPagingInputGate = string.Empty;
                lastPagingInputComponents = string.Empty;
                lastCanonicalInputState = string.Empty;
                canonicalKeyboardBindingsApplied = false;
                pagingInputSampleKnown = false;
                lastPagingLeft = false;
                lastPagingRight = false;
                nextPageSwapAllowedTime = 0f;
                lastSuccessfulPageSwapTime = -1f;
            }
        }

        private static void SetInventoryFailure(string message)
        {
            string previous = lastInventoryError;
            InventoryReady = false;
            lastInventoryError = message ?? "Inventory unavailable.";
            if (!string.Equals(previous, lastInventoryError, StringComparison.Ordinal))
                Audit("op=player.settings.inventory_ready inventoryReady=false error=" + lastInventoryError);
        }

        private static bool IsValidInventoryMode(int value) { return value >= InventoryModeSingle && value <= InventoryModeOnDemand; }
        private static int ClampInventoryMode(int value) { return Math.Max(InventoryModeSingle, Math.Min(InventoryModeOnDemand, value)); }
        private static bool IsValidSpeed(float value) { return !float.IsNaN(value) && !float.IsInfinity(value) && value >= MinimumSpeed && value <= MaximumSpeed; }
        private static float ClampSpeed(float value) { return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Math.Max(MinimumSpeed, Math.Min(MaximumSpeed, value)); }

        private static JObject CreateEmptyRoot()
        {
            return new JObject
            {
                ["version"] = ConfigVersion,
                ["gameVersion"] = GameOperations.ExpectedGameVersion,
                ["gameBuild"] = GameOperations.ExpectedGameBuild,
                ["inventoryMode"] = InventoryModeSingle,
                ["speedMultiplier"] = 1f,
                ["updatedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        private static void ValidateRoot(JObject candidate)
        {
            if (candidate == null
                || candidate.Value<int?>("version") != ConfigVersion
                || !string.Equals(candidate.Value<string>("gameVersion"), GameOperations.ExpectedGameVersion, StringComparison.Ordinal)
                || !string.Equals(candidate.Value<string>("gameBuild"), GameOperations.ExpectedGameBuild, StringComparison.Ordinal))
                throw new InvalidDataException("Player runtime settings version/build does not match this bridge.");
        }

        private static void SaveRootAtomically(JObject value)
        {
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(value.ToString(Formatting.Indented));
            if (bytes.LongLength > MaxConfigBytes)
                throw new InvalidDataException("Player runtime settings would exceed 16 KiB.");
            string temp = configPath + ".tmp";
            string backup = configPath + ".bak";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            try
            {
                if (File.Exists(configPath))
                    File.Replace(temp, configPath, backup, true);
                else
                    File.Move(temp, configPath);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        private static void Warn(string message) { if (warn != null) warn(message); }
        private static void Audit(string message) { if (audit != null) audit(message); }
    }

    internal sealed class InventorySaveState
    {
        public bool Active;
        public int PreviousPage;
        public bool Consumed;
    }

    internal sealed class PlayerSettingsSnapshot
    {
        public int ConfiguredInventoryMode;
        public float ConfiguredSpeedMultiplier;
        public int BaseInventorySlots;
        public int InventorySlotCount;
        public int InventoryPage;
        public int InventoryPageCount;
        public bool InventoryReady;
        public int NativeHotbarSlots;
        public int CurrentPage;
        public int AllocatedPageCount;
        public int ConfiguredPageCount;
        public string SaveScope;
        public bool SidecarLoaded;
        public string LastInventoryError;
        public float PlayerSpeedMultiplier;

        public JObject ToJson()
        {
            return new JObject
            {
                ["inventoryMode"] = ConfiguredInventoryMode,
                ["configuredInventoryMode"] = ConfiguredInventoryMode,
                ["speedMultiplier"] = PlayerSpeedMultiplier,
                ["configuredSpeedMultiplier"] = ConfiguredSpeedMultiplier,
                ["baseInventorySlots"] = InventoryPagingModel.NativePageWidth,
                ["nativeHotbarSlots"] = NativeHotbarSlots,
                ["inventoryReady"] = InventoryReady,
                ["inventorySlotCount"] = InventorySlotCount,
                ["inventoryPage"] = InventoryPage,
                ["currentPage"] = CurrentPage,
                ["inventoryPageCount"] = InventoryPageCount,
                ["configuredPageCount"] = ConfiguredPageCount,
                ["allocatedPageCount"] = AllocatedPageCount,
                ["saveScope"] = SaveScope ?? string.Empty,
                ["sidecarLoaded"] = SidecarLoaded,
                ["lastInventoryError"] = LastInventoryError ?? string.Empty
            };
        }
    }

    [HarmonyPatch(typeof(PlayerInventory), "SetupInventoryUI")]
    internal static class PlayerInventorySetupPatch
    {
        private static void Postfix(PlayerInventory __instance) { PlayerRuntimeSettings.OnInventoryUiReady(__instance); }
    }

    [HarmonyPatch(typeof(GameInput), "Update")]
    internal static class GameInputUpdateDiagnosticsPatch
    {
        private static void Prefix() { PlayerRuntimeSettings.AuditBridgeInputBoundary("game_input_update_prefix"); }
        private static void Postfix() { PlayerRuntimeSettings.AuditBridgeInputBoundary("game_input_update_postfix"); }
    }

    [HarmonyPatch(typeof(GameInput), "LateUpdate")]
    internal static class GameInputLateUpdateDiagnosticsPatch
    {
        private static void Prefix() { PlayerRuntimeSettings.AuditBridgeInputBoundary("game_input_late_update_prefix"); }
        private static void Postfix() { PlayerRuntimeSettings.AuditBridgeInputBoundary("game_input_late_update_postfix"); }
    }

    [HarmonyPatch(typeof(GameInput), "OnInventoryLeft")]
    internal static class GameInputInventoryLeftDiagnosticsPatch
    {
        private static bool Prefix()
        {
            PlayerRuntimeSettings.AuditCanonicalInputCallback("left_prefix");
            PlayerRuntimeSettings.HandleCanonicalPagingCallback(-1);
            return !PlayerRuntimeSettings.ShouldSuppressNativeInventoryNavigation();
        }
        private static void Postfix() { PlayerRuntimeSettings.AuditCanonicalInputCallback("left_postfix"); }
    }

    [HarmonyPatch(typeof(GameInput), "OnInventoryRight")]
    internal static class GameInputInventoryRightDiagnosticsPatch
    {
        private static bool Prefix()
        {
            PlayerRuntimeSettings.AuditCanonicalInputCallback("right_prefix");
            PlayerRuntimeSettings.HandleCanonicalPagingCallback(1);
            return !PlayerRuntimeSettings.ShouldSuppressNativeInventoryNavigation();
        }
        private static void Postfix() { PlayerRuntimeSettings.AuditCanonicalInputCallback("right_postfix"); }
    }

    [HarmonyPatch(typeof(PlayerMovement), "Move")]
    internal static class PlayerMovementMovePatch
    {
        private static void Prefix(PlayerMovement __instance)
        {
            PlayerRuntimeSettings.OnMovementLifecycle(__instance);
            PlayerRuntimeSettings.ApplySpeedDuringNativeMovement();
        }
    }

    [HarmonyPatch(typeof(Player), "LoadInventory", new Type[] { typeof(string) })]
    internal static class PlayerLoadInventoryPatch
    {
        private static void Postfix(Player __instance) { PlayerRuntimeSettings.OnPlayerInventoryLoaded(__instance); }
    }

    [HarmonyPatch(typeof(Player), "GetInventoryString")]
    internal static class PlayerInventorySavePatch
    {
        private static bool Prefix(Player __instance, ref InventorySaveState __state)
        {
            return PlayerRuntimeSettings.BeginVanillaInventorySave(__instance, out __state);
        }

        private static void Postfix(InventorySaveState __state) { PlayerRuntimeSettings.EndVanillaInventorySave(__state); }
        private static Exception Finalizer(Exception __exception, InventorySaveState __state)
        {
            PlayerRuntimeSettings.EndVanillaInventorySave(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SaveManager), "Save", new Type[] { })]
    internal static class SaveManagerInventoryPatch
    {
        private static bool Prefix(ref InventorySaveState __state) { return PlayerRuntimeSettings.BeforeGameSave(out __state); }
        private static void Postfix(InventorySaveState __state) { PlayerRuntimeSettings.EndVanillaInventorySave(__state); }
        private static Exception Finalizer(Exception __exception, InventorySaveState __state)
        {
            PlayerRuntimeSettings.EndVanillaInventorySave(__state);
            return __exception;
        }
    }

}
