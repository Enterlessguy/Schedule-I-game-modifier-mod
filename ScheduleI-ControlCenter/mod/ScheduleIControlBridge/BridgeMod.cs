using System;
using System.Collections.Concurrent;
using System.Threading;
using HarmonyLib;
using MelonLoader;
using ScheduleIControlCenter;
using UnityEngine;

namespace ScheduleIControlBridge
{
    public sealed class BridgeMod : MelonMod
    {
        private readonly ConcurrentQueue<BridgeRequest> requests = new ConcurrentQueue<BridgeRequest>();
        private PipeServer pipeServer;
        private GameOperations operations;
        private CompatibilityDiagnosticsResult compatibilityDiagnostics;
        private bool compatibilityModeEnabled;
        private bool playerPatchesAllowed;
        private bool playerPatchAttempted;
        private int stopped;
        private bool backgroundExecutionEnabled;

        public override void OnInitializeMelon()
        {
            BuildFingerprint fingerprint = BuildFingerprint.Read();
            MarketValueScaling.Initialize(Warn, Audit);
            CustomerAllowanceScaling.Initialize(Warn, Audit);
            SellPriceLimitManager.Initialize(Warn, Audit);
            BusinessLaunderScaling.Initialize(Warn, Audit);
            EffectsIntensityManager.Initialize(Warn, Audit);
            PlayerRuntimeSettings.Initialize(Warn, Audit);
            compatibilityDiagnostics = CompatibilityDiagnostics.Run();
            bool exactBuild = IsReviewedBuild(fingerprint);
            if (exactBuild)
            {
                playerPatchesAllowed = true;
                InstallPatchFamilies(false);
            }
            else
            {
                DisablePatchFamilies();
                LoggerInstance.Warning("Game build is not in the reviewed fingerprint; live patches are disabled until the Control Center explicitly enables compatibility mode.");
                LoggerInstance.Warning("Compatibility diagnostics: {0} {1}", compatibilityDiagnostics.Passed ? "passed." : "failed.", compatibilityDiagnostics.Summary);
            }
            EffectsIntensityManager.SetPatchActive(true);
            operations = new GameOperations(
                fingerprint,
                Audit,
                Warn,
                () => compatibilityModeEnabled,
                EnableCompatibilityMode,
                compatibilityDiagnostics);
            pipeServer = new PipeServer(requests.Enqueue, Warn);
            pipeServer.Start();

            LoggerInstance.Msg("Bridge v{0} listening on same-user pipe {1}.", GameOperations.ModVersion, PipeServer.PipeName);
            LoggerInstance.Msg("Control Center release: {0}.", ReleaseInfo.Version);
            LoggerInstance.Msg("Build fingerprint: {0}.", fingerprint.FilesMatch ? "recognized" : "UNKNOWN - mutations disabled");
            if (!string.IsNullOrEmpty(fingerprint.Error))
                LoggerInstance.Warning("Build fingerprint error: {0}", fingerprint.Error);
        }

        private static bool IsReviewedBuild(BuildFingerprint fingerprint)
        {
            return fingerprint.FilesMatch
                && string.Equals(Application.version, GameOperations.ExpectedGameVersion, StringComparison.Ordinal);
        }

        private void DisablePatchFamilies()
        {
            MarketValueScaling.SetPatchActive(false);
            CustomerAllowanceScaling.SetPatchActive(false);
            SellPriceLimitManager.SetPatchActive(false);
            BusinessLaunderScaling.SetPatchActive(false);
            PlayerRuntimeSettings.SetPatchActive(false);
        }

        private bool InstallPatchFamilies(bool compatibilityMode)
        {
            bool allSucceeded = true;
            try
            {
                HarmonyInstance.PatchAll(typeof(ProductValuePatch));
                MarketValueScaling.SetPatchActive(true);
                LoggerInstance.Msg("Fair-market value patch enabled with save-scoped explicit product factors.");
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                MarketValueScaling.SetPatchActive(false);
                LoggerInstance.Error("Fair-market value patch failed; market mutations are disabled.");
                LoggerInstance.Error(ex.ToString());
            }

            try
            {
                HarmonyInstance.PatchAll(typeof(CustomerWeeklySpendPatch));
                CustomerAllowanceScaling.SetPatchActive(true);
                LoggerInstance.Msg("Customer weekly-spend patch enabled with save-scoped explicit allowance ranges.");
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                CustomerAllowanceScaling.SetPatchActive(false);
                LoggerInstance.Error("Customer weekly-spend patch failed; allowance mutations are disabled.");
                LoggerInstance.Error(ex.ToString());
            }

            try
            {
                HarmonyInstance.PatchAll(typeof(ProductSendPricePatch));
                HarmonyInstance.PatchAll(typeof(CounterofferSendPatch));
                HarmonyInstance.PatchAll(typeof(HandoverPriceChangedPatch));
                HarmonyInstance.PatchAll(typeof(HandoverDonePressedPatch));
                SellPriceLimitManager.SetPatchActive(true);
                LoggerInstance.Msg("Uncapped unit-price and deal-total patches enabled for the reviewed solo-host workflow.");
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                SellPriceLimitManager.SetPatchActive(false);
                LoggerInstance.Error("Economy-cap patching failed; uncapped unit prices and custom deal maximums are disabled.");
                LoggerInstance.Error(ex.ToString());
            }

            try
            {
                HarmonyInstance.PatchAll(typeof(BusinessMinsPassPatch));
                HarmonyInstance.PatchAll(typeof(BusinessStartLaunderingPatch));
                HarmonyInstance.PatchAll(typeof(LaunderingInterfaceInitializePatch));
                HarmonyInstance.PatchAll(typeof(LaunderingInterfaceOpenPatch));
                HarmonyInstance.PatchAll(typeof(LaunderingInterfaceRefreshPatch));
                BusinessLaunderScaling.SetPatchActive(true);
                LoggerInstance.Msg("Launder-limit patches enabled for owned businesses.");
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                BusinessLaunderScaling.SetPatchActive(false);
                LoggerInstance.Error("Launder-limit patching failed; custom laundering limits are disabled.");
                LoggerInstance.Error(ex.ToString());
            }

            if (!allSucceeded && compatibilityMode)
            {
                try { HarmonyInstance.UnpatchSelf(); }
                catch { }
                DisablePatchFamilies();
            }
            return allSucceeded;
        }

        private bool EnableCompatibilityMode()
        {
            if (compatibilityModeEnabled)
                return true;
            if (compatibilityDiagnostics == null || !compatibilityDiagnostics.Passed)
                return false;
            if (!InstallPatchFamilies(true))
                return false;

            compatibilityModeEnabled = true;
            playerPatchesAllowed = true;
            playerPatchAttempted = false;
            LoggerInstance.Warning("Compatibility mode enabled by explicit Control Center confirmation for game version {0}; this build is not reviewed.", Application.version ?? "unknown");
            return true;
        }

        public override void OnUpdate()
        {
            if (!backgroundExecutionEnabled)
            {
                Application.runInBackground = true;
                backgroundExecutionEnabled = true;
                LoggerInstance.Msg("Unity background execution enabled for reliable Control Center requests while unfocused.");
            }

            if (operations == null)
                return;

            operations.Tick();

            PlayerRuntimeSettings.TickInventoryPersistence();
            PlayerRuntimeSettings.TickSwapNotice();
            PlayerRuntimeSettings.AuditBridgeInputBoundary("bridge_on_update_before");
            PlayerRuntimeSettings.HandlePagingInput();
            PlayerRuntimeSettings.TickDeferredUnequipVerification();
            PlayerRuntimeSettings.AuditBridgeInputBoundary("bridge_on_update_after");

            if (playerPatchesAllowed
                && !playerPatchAttempted
                && PlayerRuntimeSettings.EligibilityActive)
            {
                playerPatchAttempted = true;
                if (TryInstallPlayerPatches(out string playerPatchError))
                    LoggerInstance.Msg("Player paging, native-save bridge, and movement-speed patches enabled after runtime initialization.");
                else
                {
                    playerPatchesAllowed = false;
                    LoggerInstance.Error("Player runtime patching failed; inventory and speed controls are disabled: {0}", playerPatchError);
                }
            }

            int handled = 0;
            BridgeRequest request;
            while (handled < 8 && requests.TryDequeue(out request))
            {
                if (!request.TryBeginExecution())
                    continue;

                handled++;
                try
                {
                    request.Complete(operations.Handle(request));
                }
                catch (Exception ex)
                {
                    LoggerInstance.Error("Bridge request {0}/{1} failed.", request.Id, request.Operation);
                    LoggerInstance.Error(ex.ToString());
                    request.Complete(ProtocolJson.Response(
                        request.Id,
                        false,
                        "internal_error",
                        "The bridge request failed. See the MelonLoader log.",
                        operations.Revision,
                        null));
                }
            }
        }

        public override void OnApplicationQuit()
        {
            StopBridge(false);
        }

        public override void OnDeinitializeMelon()
        {
            StopBridge(true);
        }

        private void StopBridge(bool restoreLiveValues)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0)
                return;

            if (pipeServer != null)
                pipeServer.Stop();

            if (restoreLiveValues && operations != null)
                operations.RestoreLiveOverrides();
            else
            {
                MarketValueScaling.ClearManagedState();
                CustomerAllowanceScaling.ClearManagedState();
                SellPriceLimitManager.ClearManagedState();
                PlayerRuntimeSettings.ClearManagedState();
            }

            if (restoreLiveValues)
            {
                try { HarmonyInstance.UnpatchSelf(); }
                catch { }
                MarketValueScaling.SetPatchActive(false);
                CustomerAllowanceScaling.SetPatchActive(false);
                SellPriceLimitManager.SetPatchActive(false);
                PlayerRuntimeSettings.SetPatchActive(false);
            }

            BridgeRequest pending;
            long revision = operations == null ? 0 : operations.Revision;
            while (requests.TryDequeue(out pending))
            {
                pending.CompleteWithoutExecution(ProtocolJson.Response(
                    pending.Id,
                    false,
                    "bridge_stopping",
                    "The bridge is stopping.",
                    revision,
                    null));
            }
        }

        private void Audit(string message)
        {
            LoggerInstance.Msg("AUDIT {0}", message);
        }

        private bool TryInstallPlayerPatches(out string error)
        {
            error = null;
            try
            {
                LoggerInstance.Msg("Player patch stage: SetupInventoryUI begin.");
                HarmonyInstance.PatchAll(typeof(PlayerInventorySetupPatch));
                LoggerInstance.Msg("Player patch stage: SetupInventoryUI complete.");
                LoggerInstance.Msg("Player patch stage: LoadInventory/GetInventoryString/Save begin.");
                HarmonyInstance.PatchAll(typeof(PlayerLoadInventoryPatch));
                HarmonyInstance.PatchAll(typeof(PlayerInventorySavePatch));
                HarmonyInstance.PatchAll(typeof(SaveManagerInventoryPatch));
                LoggerInstance.Msg("Player patch stage: LoadInventory/GetInventoryString/Save complete.");
                LoggerInstance.Msg("Player patch stage: PlayerMovement.Move begin.");
                HarmonyInstance.PatchAll(typeof(PlayerMovementMovePatch));
                LoggerInstance.Msg("Player patch stage: PlayerMovement.Move complete.");
                HarmonyInstance.PatchAll(typeof(GameInputUpdateDiagnosticsPatch));
                HarmonyInstance.PatchAll(typeof(GameInputLateUpdateDiagnosticsPatch));
                HarmonyInstance.PatchAll(typeof(GameInputInventoryLeftDiagnosticsPatch));
                HarmonyInstance.PatchAll(typeof(GameInputInventoryRightDiagnosticsPatch));
                PlayerRuntimeSettings.SetPatchActive(true);
                LoggerInstance.Msg("Player patch stage: runtime active.");
                return true;
            }
            catch (Exception ex)
            {
                PlayerRuntimeSettings.SetPatchActive(false);
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private void Warn(string message)
        {
            LoggerInstance.Warning(message);
        }
    }
}
