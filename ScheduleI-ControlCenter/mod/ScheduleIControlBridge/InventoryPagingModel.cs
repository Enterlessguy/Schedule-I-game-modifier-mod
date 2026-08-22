using System;
using System.Collections.Generic;

namespace ScheduleIControlBridge
{
    // Pure paging rules shared by the bridge's regression tests. The live bridge
    // deliberately keeps the native game surface at exactly NativePageWidth.
    internal static class InventoryPagingModel
    {
        public const int NativePageWidth = 8;
        public const int Mode4PageCap = 8;
        public const string DefaultSwapHotkey = "RightArrow";

        private static readonly HashSet<string> SupportedSwapHotkeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Space", "Enter", "Tab", "Backquote", "Quote", "Semicolon", "Comma", "Period", "Slash", "Backslash",
            "LeftBracket", "RightBracket", "Minus", "Equals",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "Digit0", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9",
            "LeftArrow", "RightArrow", "UpArrow", "DownArrow", "Backspace", "PageDown", "PageUp", "Home", "End", "Insert", "Delete",
            "CapsLock", "NumLock", "PrintScreen", "ScrollLock", "Pause", "ContextMenu",
            "NumpadEnter", "NumpadDivide", "NumpadMultiply", "NumpadPlus", "NumpadMinus", "NumpadPeriod", "NumpadEquals",
            "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24"
        };

        public static bool TryNormalizeSwapHotkey(string value, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;
            string candidate = value.Trim();
            foreach (string supported in SupportedSwapHotkeys)
            {
                if (!string.Equals(candidate, supported, StringComparison.OrdinalIgnoreCase))
                    continue;
                normalized = supported;
                return true;
            }
            return false;
        }

        public static int ConfiguredPageCountForMode(int mode)
        {
            if (mode <= 1)
                return 1;
            if (mode == 2 || mode == 3)
                return mode;
            return Mode4PageCap;
        }

        public static int AllocatedPageCountForMode(int mode, int allocatedPages)
        {
            int minimum = mode == 4 ? 1 : ConfiguredPageCountForMode(mode);
            return Math.Max(minimum, Math.Min(Mode4PageCap, Math.Max(1, allocatedPages)));
        }

        public static int PageCountForMode(int mode, int allocatedPages = 1)
        {
            if (mode <= 1)
                return 1;
            if (mode == 2 || mode == 3)
                return mode;
            return Math.Max(1, Math.Min(Mode4PageCap, allocatedPages));
        }

        public static int CapacityForMode(int mode, int allocatedPages = 1)
        {
            return NativePageWidth * PageCountForMode(mode, allocatedPages);
        }

        public static int ClampPage(int page, int pageCount)
        {
            int last = Math.Max(0, pageCount - 1);
            return Math.Max(0, Math.Min(last, page));
        }

        public static bool TryGetBoundedTarget(int currentPage, int pageCount, int delta, out int targetPage)
        {
            targetPage = ClampPage(currentPage, pageCount);
            if (delta == 0)
                return false;

            int candidate = targetPage + Math.Sign(delta);
            int bounded = ClampPage(candidate, pageCount);
            if (bounded == targetPage)
                return false;

            targetPage = bounded;
            return true;
        }

        public static bool IsDowngradeSafe(int newPageCount, IList<bool> occupiedPages, out string error)
        {
            // A mode change changes the visible page window, not the saved bank.
            // Higher pages remain in the bounded sidecar and can become visible
            // again when the user selects a larger mode.
            error = null;
            return true;
        }

        public static bool ShouldRunVanillaSave(bool pageZeroRestored, bool pagingActive)
        {
            return !pagingActive || pageZeroRestored;
        }

        public static float SpeedForEligibility(bool eligible, bool inventoryReady, float configuredSpeed)
        {
            return eligible ? configuredSpeed : 1f;
        }

        public static bool TryCommitAfterPersistence(Func<bool> persist, Action successAudit, ref long revision, out string error)
        {
            error = null;
            long oldRevision = revision;
            try
            {
                if (persist == null || !persist())
                {
                    error = "Required persistence did not succeed.";
                    return false;
                }
                revision = oldRevision + 1;
                if (successAudit != null)
                    successAudit();
                return true;
            }
            catch (Exception ex)
            {
                revision = oldRevision;
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public static bool ValidateSidecarMetadata(int version, string gameVersion, string gameBuild, string saveScope, int allocatedPages, int serializedPageCount, string expectedGameVersion, string expectedGameBuild, string expectedSaveScope, out string error)
        {
            error = null;
            if (version != 1
                || !string.Equals(gameVersion, expectedGameVersion, StringComparison.Ordinal)
                || !string.Equals(gameBuild, expectedGameBuild, StringComparison.Ordinal)
                || !string.Equals(saveScope, expectedSaveScope, StringComparison.Ordinal))
            {
                error = "Inventory page sidecar version, build, or save scope does not match.";
                return false;
            }
            if (allocatedPages < 1 || allocatedPages > Mode4PageCap
                || serializedPageCount < 0 || serializedPageCount > Mode4PageCap - 1
                || serializedPageCount != allocatedPages - 1)
            {
                error = "Inventory page sidecar page count is outside the safe cap.";
                return false;
            }
            return true;
        }
    }

    // Small transaction harness. It models the live swap contract without any
    // Unity/IL2CPP types: snapshot current surface, apply target, and restore the
    // snapshot if the target operation reports failure or throws.
    internal sealed class InventoryPagingTransaction<T>
    {
        private readonly List<T[]> pages;
        private T[] surface;

        public InventoryPagingTransaction(IEnumerable<T[]> initialPages, int currentPage = 0)
        {
            pages = new List<T[]>();
            foreach (T[] page in initialPages)
                pages.Add(Clone(page));
            if (pages.Count == 0)
                pages.Add(new T[InventoryPagingModel.NativePageWidth]);

            CurrentPage = InventoryPagingModel.ClampPage(currentPage, pages.Count);
            surface = Clone(pages[CurrentPage]);
        }

        public int CurrentPage { get; private set; }
        public T[] Surface { get { return Clone(surface); } }
        public int PageCount { get { return pages.Count; } }

        public T[] SnapshotCurrentPage()
        {
            pages[CurrentPage] = Clone(surface);
            return Clone(surface);
        }

        public void MutateSurface(Func<T[], bool> mutation)
        {
            T[] candidate = Clone(surface);
            if (mutation == null || !mutation(candidate))
                throw new InvalidOperationException("Surface mutation failed.");
            surface = candidate;
        }

        public bool TrySwitch(int delta, Func<T[], bool> apply)
        {
            if (apply == null)
                throw new ArgumentNullException("apply");

            int targetPage;
            if (!InventoryPagingModel.TryGetBoundedTarget(CurrentPage, pages.Count, delta, out targetPage))
                return false;

            return TrySwitchToPage(targetPage, apply);
        }

        public bool TrySwitchToPage(int targetPage, Func<T[], bool> apply)
        {
            return TrySwitchToPage(targetPage, apply, null);
        }

        public bool TrySwitchToPage(int targetPage, Func<T[], bool> apply, Func<bool> persist)
        {
            if (apply == null)
                throw new ArgumentNullException("apply");
            if (targetPage < 0 || targetPage >= pages.Count || targetPage == CurrentPage)
                return false;

            T[] oldSurface = Clone(surface);
            pages[CurrentPage] = Clone(oldSurface);
            T[] target = Clone(pages[targetPage]);
            try
            {
                if (!apply(Clone(target)))
                {
                    apply(Clone(oldSurface));
                    return false;
                }
                if (persist != null && !persist())
                {
                    apply(Clone(oldSurface));
                    return false;
                }
            }
            catch
            {
                try { apply(Clone(oldSurface)); }
                catch { }
                return false;
            }

            surface = target;
            CurrentPage = targetPage;
            return true;
        }

        private static T[] Clone(T[] source)
        {
            if (source == null)
                return new T[InventoryPagingModel.NativePageWidth];
            T[] copy = new T[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }

    // Selection lifecycle seam used by regression tests. A successful page
    // switch must end unselected; selection restoration is rollback-only.
    internal static class InventoryPagingLifecycleModel
    {
        public static bool TrySwitch<T>(
            InventoryPagingTransaction<T> transaction,
            int targetPage,
            int previousSelectedIndex,
            Action deselectOldSurface,
            Func<T[], bool> applyTarget,
            Func<bool> verifyUnselected,
            Action<int> restoreSelection,
            IList<string> order,
            out int resultingSelectedIndex)
        {
            if (transaction == null)
                throw new ArgumentNullException("transaction");
            if (deselectOldSurface == null)
                throw new ArgumentNullException("deselectOldSurface");
            if (applyTarget == null)
                throw new ArgumentNullException("applyTarget");
            if (verifyUnselected == null)
                throw new ArgumentNullException("verifyUnselected");
            if (restoreSelection == null)
                throw new ArgumentNullException("restoreSelection");

            if (order != null)
                order.Add("snapshot");
            deselectOldSurface();
            if (order != null)
                order.Add("deselect_old");

            bool switched = transaction.TrySwitchToPage(targetPage, target =>
            {
                if (order != null)
                    order.Add("target_mutation");
                return applyTarget(target);
            });
            if (!switched)
            {
                restoreSelection(previousSelectedIndex);
                if (order != null)
                    order.Add("rollback_selection_restore");
                resultingSelectedIndex = previousSelectedIndex;
                return false;
            }

            bool verified = verifyUnselected();
            if (order != null)
                order.Add("postcondition");
            if (!verified)
            {
                restoreSelection(previousSelectedIndex);
                if (order != null)
                    order.Add("rollback_selection_restore");
                resultingSelectedIndex = previousSelectedIndex;
                return false;
            }

            resultingSelectedIndex = -1;
            if (order != null)
                order.Add("success_unselected");
            return true;
        }
    }
}
