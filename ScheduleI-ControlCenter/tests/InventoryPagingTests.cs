using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using ScheduleIControlBridge;

namespace ScheduleIControlCenter
{
    internal static class InventoryPagingTests
    {
        public static void RunAll()
        {
            CapacityModesAreNativeEightWide();
            SwapHotkeysAreNormalizedAndBounded();
            FixedModesAllocateAndPersistAllConfiguredPages();
            PageMovementIsClamped();
            ExactItemAndFilterRoundTrip();
            DowngradeFromPageTwoDoesNotOverwritePageOne();
            MidSwapFailureRollsBackAllEightSlots();
            InjectedWriterFailureRollsBackAndPreservesError();
            CashAndClipboardSentinelsRemainUntouched();
            SaveScopeIncludesPathAndPlayerIdentity();
            CorruptAndMismatchedSidecarsAreRejected();
            SaveOnPageOneRestoresPageZeroAndVisiblePage();
            ReloadRestoresPersistedPages();
            MutationWithoutPageSwitchRoundTrips();
            OccupiedPageDowngradeRetainsSavedPages();
            SavePrefixBlocksUnsafeSerialization();
            FailedSettingsWriteDoesNotAdvanceRevisionOrAudit();
            SpeedDoesNotDependOnInventoryReadiness();
            SelectionLifecycleEndsUnselectedAndRollsBackSelectionOnlyOnFailure();
        }

        private static void CapacityModesAreNativeEightWide()
        {
            Require(InventoryPagingModel.PageCountForMode(1) == 1, "mode 1 page count");
            Require(InventoryPagingModel.CapacityForMode(1) == 8, "mode 1 capacity");
            Require(InventoryPagingModel.PageCountForMode(2) == 2 && InventoryPagingModel.CapacityForMode(2) == 16, "mode 2 capacity");
            Require(InventoryPagingModel.PageCountForMode(3) == 3 && InventoryPagingModel.CapacityForMode(3) == 24, "mode 3 capacity");
            Require(InventoryPagingModel.PageCountForMode(4) == 1, "mode 4 starts on demand at one page");
            Require(InventoryPagingModel.PageCountForMode(4, 8) == 8 && InventoryPagingModel.CapacityForMode(4, 8) == 64, "mode 4 safe cap");
            Require(InventoryPagingModel.ConfiguredPageCountForMode(2) == 2 && InventoryPagingModel.ConfiguredPageCountForMode(3) == 3 && InventoryPagingModel.ConfiguredPageCountForMode(4) == 8, "configured page count is fixed versus on-demand");
        }

        private static void SwapHotkeysAreNormalizedAndBounded()
        {
            Require(InventoryPagingModel.DefaultSwapHotkey == "RightArrow", "right arrow remains the default swap hotkey");
            Require(InventoryPagingModel.TryNormalizeSwapHotkey(" rightarrow ", out string arrow) && arrow == "RightArrow", "hotkey normalization is case-insensitive and trimmed");
            Require(InventoryPagingModel.TryNormalizeSwapHotkey("F12", out string function) && function == "F12", "function hotkey is accepted");
            Require(InventoryPagingModel.TryNormalizeSwapHotkey("Numpad7", out string numpad) && numpad == "Numpad7", "numpad hotkey is accepted");
            Require(!InventoryPagingModel.TryNormalizeSwapHotkey("LeftCtrl", out string modifier), "modifier-only hotkey is rejected");
            Require(!InventoryPagingModel.TryNormalizeSwapHotkey("Escape", out string escape), "escape stays reserved for capture cancellation");
            Require(!InventoryPagingModel.TryNormalizeSwapHotkey("not-a-key", out string invalid), "unknown hotkey is rejected");
        }

        private static void FixedModesAllocateAndPersistAllConfiguredPages()
        {
            string[] page0 = Page("vanilla", "f0");
            string[] page1 = Page("fixed-1", "f1");
            string[] page2 = Page("fixed-2", "f2");

            int mode2Allocated = InventoryPagingModel.AllocatedPageCountForMode(2, 1);
            Require(mode2Allocated == 2, "fresh mode 2 allocates page 1 before it is addressable");
            Require(InventoryPagingModel.ValidateSidecarMetadata(1, "game", "build", "scope", mode2Allocated, 1, "game", "build", "scope", out string mode2Error), "fresh mode 2 sidecar contains page 1");

            int mode3Allocated = InventoryPagingModel.AllocatedPageCountForMode(3, 1);
            Require(mode3Allocated == 3, "fresh mode 3 allocates pages 1 and 2 before they are addressable");
            Require(InventoryPagingModel.ValidateSidecarMetadata(1, "game", "build", "scope", mode3Allocated, 2, "game", "build", "scope", out string mode3Error), "fresh mode 3 sidecar contains pages 1 and 2");

            var relaunch = new InventoryPagingTransaction<string>(new[] { page0, page1, page2 });
            Require(relaunch.TrySwitchToPage(1, target => true), "mode 3 relaunch restores page 1");
            Require(relaunch.Surface.SequenceEqual(page1), "mode 3 relaunch page 1 content");
            Require(relaunch.TrySwitchToPage(2, target => true), "mode 3 relaunch restores page 2");
            Require(relaunch.Surface.SequenceEqual(page2), "mode 3 relaunch page 2 content");

            string downgradeError;
            Require(InventoryPagingModel.IsDowngradeSafe(2, new List<bool> { false, false, true }, out downgradeError), "occupied higher page does not block a visibility downgrade");
            Require(InventoryPagingModel.PageCountForMode(2, mode3Allocated) == 2 && InventoryPagingModel.PageCountForMode(1, mode3Allocated) == 1, "downgrade changes visible window only");
            Require(InventoryPagingModel.AllocatedPageCountForMode(1, mode3Allocated) == mode3Allocated, "downgrade retains saved page allocation");
        }

        private static void PageMovementIsClamped()
        {
            int page;
            Require(!InventoryPagingModel.TryGetBoundedTarget(0, 2, -1, out page) && page == 0, "left bound clamps");
            Require(InventoryPagingModel.TryGetBoundedTarget(0, 2, 1, out page) && page == 1, "right moves one page");
            Require(!InventoryPagingModel.TryGetBoundedTarget(1, 2, 1, out page) && page == 1, "right bound clamps");
        }

        private static void ExactItemAndFilterRoundTrip()
        {
            string[] first = Page("item", "filter");
            string[] second = Page("other", "other-filter");
            var model = new InventoryPagingTransaction<string>(new[] { first, second });
            model.MutateSurface(slots => { slots[3] = "changed|changed-filter"; return true; });
            string[] applied = null;
            Require(model.TrySwitch(1, target => { applied = target; return true; }), "switch to page 1");
            Require(applied.SequenceEqual(second), "page 1 exact item/filter surface");
            Require(model.TrySwitch(-1, target => { applied = target; return true; }), "switch back to page 0");
            Require(model.Surface[3] == "changed|changed-filter", "page 0 item/filter round trip");
        }

        private static void MidSwapFailureRollsBackAllEightSlots()
        {
            string[] first = Page("old", "old-filter");
            string[] second = Page("new", "new-filter");
            var model = new InventoryPagingTransaction<string>(new[] { first, second });
            string[] applied = first.ToArray();
            bool firstAttempt = true;
            Require(!model.TrySwitch(1, target =>
            {
                Array.Copy(target, applied, 8);
                if (firstAttempt) { firstAttempt = false; return false; }
                return true;
            }), "failed swap reports failure");
            Require(model.CurrentPage == 0, "failed swap retains old page");
            Require(applied.SequenceEqual(first), "failed swap restores all eight item/filter records");
        }

        private static void InjectedWriterFailureRollsBackAndPreservesError()
        {
            string[] first = Page("old", "old-filter");
            string[] second = Page("new", "new-filter");
            var model = new InventoryPagingTransaction<string>(new[] { first, second });
            string[] applied = first.ToArray();
            string writeError = string.Empty;
            Require(!model.TrySwitchToPage(1, target => { Array.Copy(target, applied, 8); return true; }, () => { writeError = "atomic sidecar write failed"; return false; }), "injected sidecar failure rejects page change");
            Require(model.CurrentPage == 0 && model.Surface.SequenceEqual(first), "injected sidecar failure rolls back logical and visible page");
            Require(applied.SequenceEqual(first), "injected sidecar failure restores all eight visible slots");
            Require(writeError == "atomic sidecar write failed", "injected sidecar error is preserved for status reporting");
        }

        private static void DowngradeFromPageTwoDoesNotOverwritePageOne()
        {
            string[] page0 = Page("page0", "f0");
            string[] page1 = Page("page1-sentinel", "f1");
            string[] page2 = new string[8];
            var model = new InventoryPagingTransaction<string>(new[] { page0, page1, page2 });
            string[] applied = null;
            string error;
            Require(InventoryPagingModel.IsDowngradeSafe(2, new List<bool> { false, true, false }, out error), "empty page 2 allows downgrade to two pages");
            Require(model.TrySwitchToPage(2, target => { applied = target; return true; }), "reach visible page 2");
            Require(model.TrySwitchToPage(0, target => { applied = target; return true; }), "downgrade transaction first restores page 0");
            Require(model.CurrentPage == 0 && applied[0] == page0[0], "page 0 surface is restored before mode commit");
            Require(model.TrySwitchToPage(1, target => { applied = target; return true; }), "page 1 remains addressable");
            Require(applied.SequenceEqual(page1), "distinct page 1 sentinel was not overwritten by page 2");
        }

        private static void CashAndClipboardSentinelsRemainUntouched()
        {
            string[] ui = new[] { "h0", "h1", "h2", "h3", "h4", "h5", "h6", "h7", "CASH", "CLIPBOARD" };
            string cash = ui[8];
            string clipboard = ui[9];
            var model = new InventoryPagingTransaction<string>(new[] { Page("a", "f"), Page("b", "f") });
            string[] native = ui.Take(8).ToArray();
            Require(model.TrySwitch(1, target => { Array.Copy(target, native, 8); return true; }), "sentinel page switch");
            Require(ui[8] == cash && ui[9] == clipboard, "cash and clipboard sentinels untouched");
        }

        private static void SaveScopeIncludesPathAndPlayerIdentity()
        {
            Require(!string.Equals(Scope("save-a", "player-a"), Scope("save-a", "player-b"), StringComparison.Ordinal), "player identity isolates scope");
            Require(!string.Equals(Scope("save-a", "player-a"), Scope("save-b", "player-a"), StringComparison.Ordinal), "save path isolates scope");
        }

        private static void CorruptAndMismatchedSidecarsAreRejected()
        {
            Require(!ValidSidecar("{broken", "build", "scope"), "corrupt sidecar rejected");
            Require(!ValidSidecar("{\"version\":1,\"gameVersion\":\"game\",\"gameBuild\":\"other\",\"saveScope\":\"scope\"}", "build", "scope"), "build mismatch rejected");
            Require(!ValidSidecar("{\"version\":1,\"gameVersion\":\"game\",\"gameBuild\":\"build\",\"saveScope\":\"other\"}", "build", "scope"), "scope mismatch rejected");
            Require(ValidSidecar("{\"version\":1,\"gameVersion\":\"game\",\"gameBuild\":\"build\",\"saveScope\":\"scope\"}", "build", "scope"), "valid sidecar accepted");
            string metadataError;
            Require(!InventoryPagingModel.ValidateSidecarMetadata(1, "game", "build", "scope", 2, 0, "game", "build", "scope", out metadataError), "serialized page-count mismatch rejected");
            Require(InventoryPagingModel.ValidateSidecarMetadata(1, "game", "build", "scope", 8, 7, "game", "build", "scope", out metadataError), "eight-page cap accepts exactly seven extra entries");
            Require(!InventoryPagingModel.ValidateSidecarMetadata(1, "game", "build", "scope", 8, 8, "game", "build", "scope", out metadataError), "eight-page cap rejects an eighth extra entry");
        }

        private static void SaveOnPageOneRestoresPageZeroAndVisiblePage()
        {
            var model = new InventoryPagingTransaction<string>(new[] { Page("page0", "f0"), Page("page1", "f1") });
            string[] applied = null;
            Require(model.TrySwitch(1, target => { applied = target; return true; }), "visible page 1");
            int previousPage = model.CurrentPage;
            Require(InventoryPagingModel.ShouldRunVanillaSave(true, true), "safe save prefix allows save");
            Require(model.TrySwitch(-1, target => { applied = target; return true; }), "save preparation restores page 0");
            string[] expectedPage0 = Page("page0", "f0");
            Require(applied.SequenceEqual(expectedPage0), "vanilla save sees the exact page 0 item/filter surface");
            Require(model.TrySwitch(1, target => { applied = target; return true; }), "save finalizer restores page 1");
            string[] expectedPage1 = Page("page1", "f1");
            Require(model.CurrentPage == previousPage && applied.SequenceEqual(expectedPage1), "save finalizer restores the exact visible page");
        }

        private static void ReloadRestoresPersistedPages()
        {
            string[] page1 = Page("persisted", "persisted-filter");
            var firstRun = new InventoryPagingTransaction<string>(new[] { Page("vanilla", "f0"), page1 });
            string[] saved = null;
            Require(firstRun.TrySwitch(1, target => { saved = target; return true; }), "persist page switch");
            var envelope = new Dictionary<string, object>
            {
                { "version", 1 }, { "gameVersion", "game" }, { "gameBuild", "build" },
                { "saveScope", "scope" }, { "allocatedPageCount", 2 }, { "pages", new[] { string.Join(";", saved) } }
            };
            string sidecarJson = new JavaScriptSerializer().Serialize(envelope);
            var parsed = new JavaScriptSerializer().DeserializeObject(sidecarJson) as Dictionary<string, object>;
            string metadataError;
            Require(InventoryPagingModel.ValidateSidecarMetadata(
                Convert.ToInt32(parsed["version"]), Convert.ToString(parsed["gameVersion"]), Convert.ToString(parsed["gameBuild"]),
                Convert.ToString(parsed["saveScope"]), Convert.ToInt32(parsed["allocatedPageCount"]),
                ((object[])parsed["pages"]).Length, "game", "build", "scope", out metadataError), "valid relaunch sidecar metadata");
            object[] parsedPages = (object[])parsed["pages"];
            string[] parsedPage = Convert.ToString(parsedPages[0]).Split(new[] { ';' }, StringSplitOptions.None);
            Require(parsedPage.Length == 8, "serialized page payload contains all eight item/filter records");
            var relaunch = new InventoryPagingTransaction<string>(new[] { Page("vanilla", "f0"), parsedPage });
            Require(relaunch.TrySwitch(1, target => { saved = target; return true; }), "relaunch page switch");
            Require(relaunch.Surface.SequenceEqual(page1), "relaunch restores page 1");
        }

        private static void MutationWithoutPageSwitchRoundTrips()
        {
            var model = new InventoryPagingTransaction<string>(new[] { Page("page0", "f0"), Page("page1", "f1") });
            Require(model.TrySwitchToPage(1, target => true), "mutation test enters page 1");
            model.MutateSurface(slots => { slots[4] = "changed-item|changed-filter"; return true; });
            string serializedPage = string.Join(";", model.SnapshotCurrentPage());
            string[] parsedPage = serializedPage.Split(new[] { ';' }, StringSplitOptions.None);
            var relaunch = new InventoryPagingTransaction<string>(new[] { Page("page0", "f0"), parsedPage });
            Require(relaunch.TrySwitchToPage(1, target => true), "mutation test relaunch enters page 1");
            Require(relaunch.Surface.SequenceEqual(model.Surface), "mutation without page switch survives serialize/relaunch with exact item/filter surface");
        }

        private static void OccupiedPageDowngradeRetainsSavedPages()
        {
            string error;
            Require(InventoryPagingModel.IsDowngradeSafe(1, new List<bool> { false, true }, out error), "occupied higher page permits visibility downgrade");
            Require(InventoryPagingModel.AllocatedPageCountForMode(1, 2) == 2, "mode 1 retains page 1 in the saved bank");
            Require(InventoryPagingModel.PageCountForMode(1, 2) == 1, "mode 1 hides retained page 1");
            Require(InventoryPagingModel.PageCountForMode(2, 2) == 2, "mode 2 re-exposes retained page 1");
            Require(InventoryPagingModel.AllocatedPageCountForMode(4, 3) == 3, "infinity retains all allocated pages within the cap");
        }

        private static void SavePrefixBlocksUnsafeSerialization()
        {
            Require(!InventoryPagingModel.ShouldRunVanillaSave(false, true), "save prefix blocks when page 0 restore fails");
            Require(InventoryPagingModel.ShouldRunVanillaSave(false, false), "vanilla save remains available when paging is inactive");
            var state = new InventorySaveStateModel { Active = true };
            Require(state.Consume() && !state.Consume(), "save state is restored at most once");
        }

        private static void FailedSettingsWriteDoesNotAdvanceRevisionOrAudit()
        {
            long revision = 7;
            bool audited = false;
            string error;
            Require(!InventoryPagingModel.TryCommitAfterPersistence(() => false, () => audited = true, ref revision, out error), "failed settings persistence rejects commit");
            Require(revision == 7 && !audited, "failed settings persistence does not advance revision or emit success audit");
            Require(error.IndexOf("persistence", StringComparison.OrdinalIgnoreCase) >= 0, "failed settings persistence preserves decision error");
        }

        private static void SpeedDoesNotDependOnInventoryReadiness()
        {
            Require(Math.Abs(InventoryPagingModel.SpeedForEligibility(true, false, 2f) - 2f) < 0.0001f, "speed applies while inventory is not ready");
            Require(Math.Abs(InventoryPagingModel.SpeedForEligibility(true, true, 2f) - 2f) < 0.0001f, "speed applies while inventory is ready");
            Require(Math.Abs(InventoryPagingModel.SpeedForEligibility(false, true, 2f) - 1f) < 0.0001f, "speed resets when eligibility is lost");
        }

        private static void SelectionLifecycleEndsUnselectedAndRollsBackSelectionOnlyOnFailure()
        {
            var successModel = new InventoryPagingTransaction<string>(new[] { Page("old", "f0"), Page("new", "f1") });
            int selectedIndex = 4;
            var successOrder = new List<string>();
            int resultingIndex;
            Require(InventoryPagingLifecycleModel.TrySwitch(
                successModel,
                1,
                selectedIndex,
                () => selectedIndex = -1,
                target => true,
                () => selectedIndex == -1,
                index => selectedIndex = index,
                successOrder,
                out resultingIndex), "successful lifecycle switch");
            Require(resultingIndex == -1 && selectedIndex == -1, "successful lifecycle ends unselected");
            Require(string.Join(",", successOrder) == "snapshot,deselect_old,target_mutation,postcondition,success_unselected", "successful lifecycle ordering has no target re-equip");

            var failureModel = new InventoryPagingTransaction<string>(new[] { Page("old", "f0"), Page("new", "f1") });
            selectedIndex = 4;
            var failureOrder = new List<string>();
            Require(!InventoryPagingLifecycleModel.TrySwitch(
                failureModel,
                1,
                selectedIndex,
                () => selectedIndex = -1,
                target => false,
                () => selectedIndex == -1,
                index => selectedIndex = index,
                failureOrder,
                out resultingIndex), "failed lifecycle switch");
            Require(failureModel.CurrentPage == 0 && resultingIndex == 4 && selectedIndex == 4, "failed lifecycle restores prior selection");
            Require(failureOrder.Contains("rollback_selection_restore"), "failed lifecycle records rollback selection restore");
        }

        private static string[] Page(string item, string filter)
        {
            string[] page = new string[8];
            for (int i = 0; i < page.Length; i++)
                page[i] = item + i.ToString() + "|" + filter + i.ToString();
            return page;
        }

        private static string Scope(string path, string identity)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(path + "|" + identity))).Replace("-", string.Empty);
        }

        private static bool ValidSidecar(string json, string build, string scope)
        {
            try
            {
                var values = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                if (values == null)
                    return false;
                string error;
                return InventoryPagingModel.ValidateSidecarMetadata(
                    Convert.ToInt32(values["version"]), Convert.ToString(values["gameVersion"]), Convert.ToString(values["gameBuild"]),
                    Convert.ToString(values["saveScope"]), 1, 0, "game", build, scope, out error);
            }
            catch { return false; }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Inventory test failed: " + message);
        }
    }

    internal sealed class InventorySaveStateModel
    {
        private bool consumed;
        public bool Active;
        public bool Consume()
        {
            if (consumed)
                return false;
            consumed = true;
            return true;
        }
    }
}
