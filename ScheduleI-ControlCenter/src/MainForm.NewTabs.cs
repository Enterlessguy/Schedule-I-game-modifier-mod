using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScheduleIControlCenter
{
    internal sealed partial class MainForm
    {
        private const int LaunderDefaultLimit = 2000;
        private const int LaunderMinimumLimit = 1;
        private const int LaunderMaximumLimit = 16777215;

        private readonly List<LaunderBusinessRow> launderRows = new List<LaunderBusinessRow>();
        private readonly List<EffectPriceRow> effectPriceRows = new List<EffectPriceRow>();
        private readonly List<EffectParamRow> effectParamRows = new List<EffectParamRow>();

        private readonly DataGridView launderGrid = new IntelDataGridView();
        private readonly DataGridView effectsGrid = new IntelDataGridView();
        private readonly DataGridView effectsParamGrid = new IntelDataGridView();
        private readonly ComboBox launderModeSelector = new IntelComboBox();
        private readonly NumericUpDown launderLimitInput = new IntelNumericUpDown();
        private readonly Button launderRefreshButton = new IntelButton();
        private readonly Button launderPreviewButton = new IntelButton();
        private readonly Button launderApplyButton = new IntelButton();
        private readonly Label launderSummary = new Label();

        private readonly ComboBox effectsModeSelector = new IntelComboBox();
        private readonly NumericUpDown effectsScaleInput = new IntelNumericUpDown();
        private readonly Button effectsRefreshButton = new IntelButton();
        private readonly Button effectsPreviewButton = new IntelButton();
        private readonly Button effectsApplyButton = new IntelButton();
        private readonly Label effectsSummary = new Label();

        private bool launderBusy;
        private bool effectsBusy;
        private string launderPreviewId = string.Empty;
        private long launderPreviewRevision;
        private long launderPreviewConfigRevision;
        private string effectsPreviewId = string.Empty;
        private long effectsPreviewRevision;
        private long effectsPreviewConfigRevision;

        private readonly IntelTrackBar inventoryModeSlider = new IntelTrackBar();
        private readonly Label inventoryModeValue = new Label();
        private readonly NumericUpDown playerSpeedInput = new IntelNumericUpDown();
        private readonly Label playerSpeedPreviewValue = new Label();
        private readonly Button playerLeftSwapHotkeyButton = new IntelButton();
        private readonly Label playerLeftSwapHotkeyValue = new Label();
        private readonly Button playerRightSwapHotkeyButton = new IntelButton();
        private readonly Label playerRightSwapHotkeyValue = new Label();
        private readonly Button playerInventoryRefreshButton = new IntelButton();
        private readonly Button playerInventoryPreviewButton = new IntelButton();
        private readonly Button playerInventoryApplyButton = new IntelButton();
        private readonly Button playerSpeedRefreshButton = new IntelButton();
        private readonly Button playerSpeedPreviewButton = new IntelButton();
        private readonly Button playerSpeedApplyButton = new IntelButton();
        private readonly Label playerSummary = new Label();
        private bool playerBusy;
        private string playerPreviewId = string.Empty;
        private long playerPreviewRevision;
        private long playerPreviewConfigRevision;
        private string playerLeftSwapHotkey = "LeftArrow";
        private string playerRightSwapHotkey = "RightArrow";
        private int playerSwapHotkeyCaptureDirection;

        private TabPage BuildPlayerPage()
        {
            TabPage page = NewPage("Player");
            TableLayoutPanel layout = PageLayout(3);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 352));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            page.Controls.Add(layout);

            GroupBox intro = NewGroup("Inventory and movement");
            intro.Controls.Add(new Label
            {
                Text = "Use the native eight-slot hotbar as a stable surface and switch save-scoped virtual pages.\r\nPages 1-3 provide 8/16/24 slots; mode 4 allocates on demand up to an 8-page (64-slot) cap.\r\nMovement speed remains independent and both settings require the reviewed solo-host workflow.",
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 4, 12, 4)
            });
            layout.Controls.Add(intro, 0, 0);

            GroupBox settings = NewGroup("Player settings");
            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8), BackColor = Surface };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

            TableLayoutPanel inventoryRow = MakePlayerControlRow();
            Label capacityLabel = FieldLabel("Capacity");
            capacityLabel.Dock = DockStyle.Fill;
            inventoryRow.Controls.Add(capacityLabel, 0, 0);
            inventoryModeSlider.Minimum = 1;
            inventoryModeSlider.Maximum = 4;
            inventoryModeSlider.Value = 1;
            inventoryModeSlider.TickFrequency = 1;
            inventoryModeSlider.LargeChange = 1;
            inventoryModeSlider.SmallChange = 1;
            inventoryModeSlider.Dock = DockStyle.Fill;
            inventoryModeSlider.Margin = new Padding(5, 4, 5, 4);
            inventoryModeSlider.ValueChanged += delegate
            {
                inventoryModeValue.Text = "Current: " + InventoryModeLabel(inventoryModeSlider.Value) + "  •  Upcoming: " + InventoryModeLabel(inventoryModeSlider.Value);
                InvalidatePlayerPreview();
            };
            inventoryRow.Controls.Add(inventoryModeSlider, 1, 0);
            inventoryModeValue.AutoSize = false;
            inventoryModeValue.Dock = DockStyle.Fill;
            inventoryModeValue.Text = "Current: " + InventoryModeLabel(inventoryModeSlider.Value) + "  •  Upcoming: " + InventoryModeLabel(inventoryModeSlider.Value);
            inventoryModeValue.TextAlign = ContentAlignment.MiddleLeft;
            inventoryModeValue.ForeColor = Ink;
            inventoryRow.Controls.Add(inventoryModeValue, 2, 0);
            AddPlayerButtons(inventoryRow, playerInventoryRefreshButton, playerInventoryPreviewButton, playerInventoryApplyButton);
            body.Controls.Add(inventoryRow, 0, 0);

            body.Controls.Add(BuildPlayerHotkeyRow("Previous page", playerLeftSwapHotkeyButton, playerLeftSwapHotkeyValue, -1), 0, 1);
            body.Controls.Add(BuildPlayerHotkeyRow("Next page", playerRightSwapHotkeyButton, playerRightSwapHotkeyValue, 1), 0, 2);

            TableLayoutPanel speedRow = MakePlayerControlRow();
            Label speedLabel = FieldLabel("Speed multiplier");
            speedLabel.Dock = DockStyle.Fill;
            speedRow.Controls.Add(speedLabel, 0, 0);
            playerSpeedInput.DecimalPlaces = 2;
            playerSpeedInput.Minimum = 0.10m;
            playerSpeedInput.Maximum = 10m;
            playerSpeedInput.Increment = 0.10m;
            playerSpeedInput.Value = 1m;
            playerSpeedInput.ValueChanged += delegate
            {
                playerSpeedPreviewValue.Text = string.Format(CultureInfo.CurrentCulture, "Current: {0:0.00}x  •  Upcoming: {0:0.00}x", playerSpeedInput.Value);
                InvalidatePlayerPreview();
            };
            Panel speedInputCell = MakeCenteredPlayerControl(playerSpeedInput, 36, 4);
            speedRow.Controls.Add(speedInputCell, 1, 0);
            playerSpeedPreviewValue.AutoSize = false;
            playerSpeedPreviewValue.Dock = DockStyle.Fill;
            playerSpeedPreviewValue.Text = string.Format(CultureInfo.CurrentCulture, "Current: {0:0.00}x  •  Upcoming: {0:0.00}x", playerSpeedInput.Value);
            playerSpeedPreviewValue.TextAlign = ContentAlignment.MiddleLeft;
            playerSpeedPreviewValue.ForeColor = Muted;
            speedRow.Controls.Add(playerSpeedPreviewValue, 2, 0);
            AddPlayerButtons(speedRow, playerSpeedRefreshButton, playerSpeedPreviewButton, playerSpeedApplyButton);
            body.Controls.Add(speedRow, 0, 3);
            settings.Controls.Add(body);
            layout.Controls.Add(settings, 0, 1);

            GroupBox current = NewGroup("Current configuration");
            playerSummary.Dock = DockStyle.Fill;
            playerSummary.Padding = new Padding(18, 16, 18, 16);
            playerSummary.ForeColor = Muted;
            playerSummary.Text = "Refresh player settings to read the current inventory and speed values.";
            current.Controls.Add(playerSummary);
            layout.Controls.Add(current, 0, 2);
            SetPlayerBusy(false, null);
            return page;
        }

        private static TableLayoutPanel MakePlayerControlRow()
        {
            TableLayoutPanel row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, BackColor = Surface, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return row;
        }

        private static Panel MakeCenteredPlayerControl(Control control, int height, int verticalOffset = 0)
        {
            Panel cell = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0) };
            control.Dock = DockStyle.None;
            control.Anchor = AnchorStyles.None;
            control.Margin = new Padding(0);
            control.Height = height;
            cell.Controls.Add(control);
            EventHandler center = delegate
            {
                int controlHeight = Math.Min(height, Math.Max(1, cell.ClientSize.Height));
                int top = Math.Max(0, (cell.ClientSize.Height - controlHeight) / 2 + verticalOffset);
                if (top + controlHeight > cell.ClientSize.Height)
                    top = Math.Max(0, cell.ClientSize.Height - controlHeight);
                control.Bounds = new Rectangle(0, top, Math.Max(1, cell.ClientSize.Width), controlHeight);
            };
            cell.Resize += center;
            center(cell, EventArgs.Empty);
            return cell;
        }

        private static Panel MakePlayerButtonCell(Button button)
        {
            Panel cell = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0) };
            button.Dock = DockStyle.None;
            button.Anchor = AnchorStyles.None;
            button.Margin = new Padding(0);
            button.Height = 38;
            cell.Controls.Add(button);
            EventHandler center = delegate
            {
                int buttonHeight = Math.Min(38, Math.Max(1, cell.ClientSize.Height));
                button.Bounds = new Rectangle(4, Math.Max(0, (cell.ClientSize.Height - buttonHeight) / 2), Math.Max(1, cell.ClientSize.Width - 8), buttonHeight);
            };
            cell.Resize += center;
            center(cell, EventArgs.Empty);
            return cell;
        }

        private void AddPlayerButtons(TableLayoutPanel panel, Button refresh, Button preview, Button apply)
        {
            refresh.Text = "Refresh";
            StyleButton(refresh, false);
            refresh.Click += async delegate { await RefreshPlayerSettingsAsync(); };
            panel.Controls.Add(MakePlayerButtonCell(refresh), 3, 0);

            preview.Text = "Preview";
            StyleButton(preview, true);
            preview.Enabled = false;
            preview.Click += async delegate { await PreviewPlayerSettingsAsync(); };
            panel.Controls.Add(MakePlayerButtonCell(preview), 4, 0);

            apply.Text = "Apply";
            StyleButton(apply, true);
            apply.Enabled = false;
            apply.Click += async delegate { await ApplyPlayerPreviewAsync(); };
            panel.Controls.Add(MakePlayerButtonCell(apply), 5, 0);
        }

        private TableLayoutPanel BuildPlayerHotkeyRow(string labelText, Button button, Label valueLabel, int direction)
        {
            TableLayoutPanel row = MakePlayerControlRow();
            Label label = FieldLabel(labelText);
            label.Dock = DockStyle.Fill;
            row.Controls.Add(label, 0, 0);
            button.Text = HotkeyDisplayName(direction < 0 ? playerLeftSwapHotkey : playerRightSwapHotkey);
            StyleButton(button, false);
            button.Click += delegate { BeginPlayerSwapHotkeyCapture(direction); };
            button.PreviewKeyDown += delegate(object sender, PreviewKeyDownEventArgs e) { e.IsInputKey = true; };
            button.KeyDown += delegate(object sender, KeyEventArgs e) { PlayerSwapHotkeyButton_KeyDown(e, direction); };
            row.Controls.Add(MakeCenteredPlayerControl(button, 38), 1, 0);
            valueLabel.AutoSize = false;
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.Text = "Click the button, then press a key. Works during storage and phone use.";
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            valueLabel.ForeColor = Muted;
            row.Controls.Add(valueLabel, 2, 0);
            return row;
        }

        private void BeginPlayerSwapHotkeyCapture(int direction)
        {
            if (playerBusy)
                return;
            playerSwapHotkeyCaptureDirection = direction;
            Button button = direction < 0 ? playerLeftSwapHotkeyButton : playerRightSwapHotkeyButton;
            Label valueLabel = direction < 0 ? playerLeftSwapHotkeyValue : playerRightSwapHotkeyValue;
            button.Text = "Press a key…";
            valueLabel.Text = "Press Escape to cancel. Modifier-only keys are intentionally excluded.";
            button.Focus();
        }

        private void PlayerSwapHotkeyButton_KeyDown(KeyEventArgs e, int direction)
        {
            if (playerSwapHotkeyCaptureDirection != direction)
                return;
            Button button = direction < 0 ? playerLeftSwapHotkeyButton : playerRightSwapHotkeyButton;
            Label valueLabel = direction < 0 ? playerLeftSwapHotkeyValue : playerRightSwapHotkeyValue;
            string current = direction < 0 ? playerLeftSwapHotkey : playerRightSwapHotkey;
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape)
            {
                playerSwapHotkeyCaptureDirection = 0;
                button.Text = HotkeyDisplayName(current);
                valueLabel.Text = "Hotkey unchanged. Works during storage and phone use.";
                return;
            }
            if (!TryMapSwapHotkey(e.KeyCode, out string mapped))
            {
                button.Text = "Try another key…";
                valueLabel.Text = "That key cannot be used by the game input system.";
                return;
            }
            playerSwapHotkeyCaptureDirection = 0;
            if (direction < 0)
                playerLeftSwapHotkey = mapped;
            else
                playerRightSwapHotkey = mapped;
            button.Text = HotkeyDisplayName(mapped);
            valueLabel.Text = string.Equals(playerLeftSwapHotkey, playerRightSwapHotkey, StringComparison.OrdinalIgnoreCase)
                ? "Choose a different key for each direction."
                : "Upcoming: " + HotkeyDisplayName(mapped) + ". Preview and Apply to persist it.";
            InvalidatePlayerPreview();
        }

        private static bool TryMapSwapHotkey(Keys key, out string mapped)
        {
            mapped = null;
            if (key >= Keys.A && key <= Keys.Z)
                mapped = key.ToString();
            else if (key >= Keys.D0 && key <= Keys.D9)
                mapped = "Digit" + ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture);
            else if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                mapped = "Numpad" + ((int)key - (int)Keys.NumPad0).ToString(CultureInfo.InvariantCulture);
            else if (key >= Keys.F1 && key <= Keys.F24)
                mapped = key.ToString();
            else
            {
                switch (key)
                {
                    case Keys.Space: mapped = "Space"; break;
                    case Keys.Enter: mapped = "Enter"; break;
                    case Keys.Tab: mapped = "Tab"; break;
                    case Keys.Back: mapped = "Backspace"; break;
                    case Keys.Left: mapped = "LeftArrow"; break;
                    case Keys.Right: mapped = "RightArrow"; break;
                    case Keys.Up: mapped = "UpArrow"; break;
                    case Keys.Down: mapped = "DownArrow"; break;
                    case Keys.PageUp: mapped = "PageUp"; break;
                    case Keys.PageDown: mapped = "PageDown"; break;
                    case Keys.Home: mapped = "Home"; break;
                    case Keys.End: mapped = "End"; break;
                    case Keys.Insert: mapped = "Insert"; break;
                    case Keys.Delete: mapped = "Delete"; break;
                    case Keys.CapsLock: mapped = "CapsLock"; break;
                    case Keys.NumLock: mapped = "NumLock"; break;
                    case Keys.PrintScreen: mapped = "PrintScreen"; break;
                    case Keys.Scroll: mapped = "ScrollLock"; break;
                    case Keys.Pause: mapped = "Pause"; break;
                    case Keys.Apps: mapped = "ContextMenu"; break;
                    case Keys.Decimal: mapped = "NumpadPeriod"; break;
                    case Keys.Divide: mapped = "NumpadDivide"; break;
                    case Keys.Multiply: mapped = "NumpadMultiply"; break;
                    case Keys.Add: mapped = "NumpadPlus"; break;
                    case Keys.Subtract: mapped = "NumpadMinus"; break;
                    case Keys.Oemtilde: mapped = "Backquote"; break;
                    case Keys.OemQuotes: mapped = "Quote"; break;
                    case Keys.OemSemicolon: mapped = "Semicolon"; break;
                    case Keys.Oemcomma: mapped = "Comma"; break;
                    case Keys.OemPeriod: mapped = "Period"; break;
                    case Keys.OemQuestion: mapped = "Slash"; break;
                    case Keys.OemPipe: mapped = "Backslash"; break;
                    case Keys.OemOpenBrackets: mapped = "LeftBracket"; break;
                    case Keys.OemCloseBrackets: mapped = "RightBracket"; break;
                    case Keys.OemMinus: mapped = "Minus"; break;
                    case Keys.Oemplus: mapped = "Equals"; break;
                }
            }
            return !string.IsNullOrEmpty(mapped);
        }

        private static string HotkeyDisplayName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "Right Arrow";
            if (value.EndsWith("Arrow", StringComparison.Ordinal))
                return value.Substring(0, value.Length - 5) + " Arrow";
            if (value.StartsWith("Digit", StringComparison.Ordinal))
                return value.Substring(5);
            if (value.StartsWith("Numpad", StringComparison.Ordinal))
                return "Numpad " + value.Substring(6);
            return value;
        }

        private async Task RefreshPlayerSettingsAsync()
        {
            InvalidatePlayerPreview();
            SetPlayerBusy(true, "Reading current inventory and player speed...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("player.settings.get", new Dictionary<string, object>(), true);
                ShowResult(result);
                if (!result.Success)
                    return;
                DisplayPlayerSettings(result.Data, false);
                currentBridgeRevision = result.Revision;
            }
            finally
            {
                SetPlayerBusy(false, null);
            }
        }

        private async Task PreviewPlayerSettingsAsync()
        {
            InvalidatePlayerPreview();
            if (string.Equals(playerLeftSwapHotkey, playerRightSwapHotkey, StringComparison.OrdinalIgnoreCase))
            {
                playerSummary.Text = "Previous page and Next page must use different hotkeys.";
                return;
            }
            SetPlayerBusy(true, "Building a revision-checked player settings preview...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("player.settings.preview", new Dictionary<string, object>
                {
                    { "inventoryMode", inventoryModeSlider.Value },
                    { "speedMultiplier", playerSpeedInput.Value },
                    { "leftSwapHotkey", playerLeftSwapHotkey },
                    { "rightSwapHotkey", playerRightSwapHotkey }
                }, true);
                ShowResult(result);
                if (!result.Success)
                    return;
                playerPreviewId = JsonUtil.GetString(result.Data, "previewId", string.Empty);
                playerPreviewRevision = JsonUtil.GetLong(result.Data, "expectedRevision", result.Revision);
                playerPreviewConfigRevision = JsonUtil.GetLong(result.Data, "expectedConfigRevision", 0);
                DisplayPlayerSettings(result.Data, true);
            }
            finally
            {
                SetPlayerBusy(false, null);
            }
        }

        private async Task ApplyPlayerPreviewAsync()
        {
            if (playerPreviewId.Length == 0)
                return;
            if (!Confirm("Apply the virtual inventory-page, left/right swap-hotkey, and player-speed preview? Page 0 remains the vanilla save surface."))
                return;
            SetPlayerBusy(true, "Applying and persisting player settings...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("player.settings.applyPreview", new Dictionary<string, object>
                {
                    { "previewId", playerPreviewId },
                    { "expectedRevision", playerPreviewRevision },
                    { "expectedConfigRevision", playerPreviewConfigRevision }
                }, false);
                ShowResult(result);
                InvalidatePlayerPreview();
                if (result.Success)
                {
                    DisplayPlayerSettings(result.Data, false);
                }
            }
            finally
            {
                SetPlayerBusy(false, null);
            }
        }

        private void DisplayPlayerSettings(Dictionary<string, object> data, bool preview)
        {
            if (data == null)
                return;
            int mode = JsonUtil.GetInt(data, preview ? "newInventoryMode" : "configuredInventoryMode", inventoryModeSlider.Value);
            if (mode < inventoryModeSlider.Minimum || mode > inventoryModeSlider.Maximum)
                mode = 1;
            float speed = GetFloat(data, preview ? "newSpeedMultiplier" : "configuredSpeedMultiplier", 1f);
            string leftSwapHotkey = JsonUtil.GetString(data, preview ? "newLeftSwapHotkey" : "leftSwapHotkey", playerLeftSwapHotkey);
            string rightSwapHotkey = JsonUtil.GetString(data, preview ? "newRightSwapHotkey" : "rightSwapHotkey", playerRightSwapHotkey);
            decimal boundedSpeed = Math.Max(playerSpeedInput.Minimum, Math.Min(playerSpeedInput.Maximum, (decimal)speed));
            if (!preview)
            {
                inventoryModeSlider.Value = mode;
                playerSpeedInput.Value = boundedSpeed;
                playerLeftSwapHotkey = leftSwapHotkey;
                playerRightSwapHotkey = rightSwapHotkey;
                playerLeftSwapHotkeyButton.Text = HotkeyDisplayName(leftSwapHotkey);
                playerRightSwapHotkeyButton.Text = HotkeyDisplayName(rightSwapHotkey);
                inventoryModeValue.Text = "Current: " + InventoryModeLabel(mode) + "  •  Upcoming: " + InventoryModeLabel(mode);
            }
            int slotCount = JsonUtil.GetInt(data, preview ? "newInventorySlotCount" : "inventorySlotCount", 0);
            int baseSlots = JsonUtil.GetInt(data, "baseInventorySlots", 8);
            int pageCount = JsonUtil.GetInt(data, preview ? "newConfiguredPageCount" : "inventoryPageCount", 1);
            int nativeSlots = JsonUtil.GetInt(data, preview ? "newNativeHotbarSlots" : "nativeHotbarSlots", 0);
            int currentPage = preview ? 0 : JsonUtil.GetInt(data, "currentPage", JsonUtil.GetInt(data, "inventoryPage", 0));
            int allocatedPages = JsonUtil.GetInt(data, preview ? "newAllocatedPageCount" : "allocatedPageCount", pageCount);
            bool inventoryReady = JsonUtil.GetBool(data, preview ? "newInventoryReady" : "inventoryReady", false);
            bool sidecarLoaded = preview ? false : JsonUtil.GetBool(data, "sidecarLoaded", JsonUtil.GetBool(data, "inventorySidecarLoaded", false));
            string inventoryError = preview ? string.Empty : JsonUtil.GetString(data, "lastInventoryError", string.Empty);
            string saveScope = preview ? string.Empty : JsonUtil.GetString(data, "saveScope", JsonUtil.GetString(data, "inventorySaveScope", string.Empty));
            string pagePlural = pageCount == 1 ? string.Empty : "s";
            string prefix = preview ? "Preview upcoming" : "Current";
            string readiness = preview ? "runtime readiness unchanged until apply" : (inventoryReady ? "ready" : "not ready (vanilla inventory untouched)");
            string sidecar = preview ? "runtime sidecar status unchanged" : (sidecarLoaded ? "sidecar loaded" : "sidecar not loaded");
            string error = preview || string.IsNullOrEmpty(inventoryError) ? string.Empty : " Error: " + inventoryError;
            string page = preview ? "unchanged until apply" : (currentPage + 1).ToString(CultureInfo.CurrentCulture);
            string scope = string.IsNullOrEmpty(saveScope) ? string.Empty : " Save scope " + saveScope + ".";
            string capacity = mode == 4
                ? string.Format(CultureInfo.CurrentCulture, "up to {0} slots", slotCount)
                : string.Format(CultureInfo.CurrentCulture, "{0} slots", slotCount);
            playerSummary.Text = string.Format(CultureInfo.CurrentCulture, "{0}: inventory {1} ({2}, {3} configured page{4}; native surface {5}, page {6}, allocated {7}; {8}, {9}). Previous {10}; next {11}. Speed {12:0.00}x.{13} {14}{15}", prefix, InventoryModeLabel(mode), capacity, pageCount, pagePlural, nativeSlots, page, allocatedPages, readiness, sidecar, HotkeyDisplayName(leftSwapHotkey), HotkeyDisplayName(rightSwapHotkey), speed, error, preview ? "Apply to activate." : "", scope);
            string upcomingCapacity = mode == 4
                ? string.Format(CultureInfo.CurrentCulture, "{0} (up to {1} slots; {2} page{3})", InventoryModeLabel(mode), slotCount, allocatedPages, allocatedPages == 1 ? string.Empty : "s")
                : string.Format(CultureInfo.CurrentCulture, "{0} ({1} slots)", InventoryModeLabel(mode), slotCount);
            inventoryModeValue.Text = string.Format(CultureInfo.CurrentCulture, "Current: {0}  •  Upcoming: {1}", InventoryModeLabel(mode), upcomingCapacity);
            playerSpeedPreviewValue.Text = string.Format(CultureInfo.CurrentCulture, "Current: {0:0.00}x  •  Upcoming: {1:0.00}x", speed, speed);
            playerLeftSwapHotkeyValue.Text = (preview ? "Upcoming: " : "Current: ") + HotkeyDisplayName(leftSwapHotkey) + ". Previous page; works during storage and phone use.";
            playerRightSwapHotkeyValue.Text = (preview ? "Upcoming: " : "Current: ") + HotkeyDisplayName(rightSwapHotkey) + ". Next page; works during storage and phone use.";
        }

        private void SetPlayerBusy(bool busy, string message)
        {
            playerBusy = busy;
            bool ready = !busy && bridgeConnected && soloHost;
            playerInventoryRefreshButton.Enabled = ready;
            playerSpeedRefreshButton.Enabled = ready;
            playerLeftSwapHotkeyButton.Enabled = !busy;
            playerRightSwapHotkeyButton.Enabled = !busy;
            playerInventoryPreviewButton.Enabled = ready;
            playerSpeedPreviewButton.Enabled = ready;
            playerInventoryApplyButton.Enabled = ready && playerPreviewId.Length > 0;
            playerSpeedApplyButton.Enabled = ready && playerPreviewId.Length > 0;
            if (!string.IsNullOrEmpty(message))
                playerSummary.Text = message;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void InvalidatePlayerPreview()
        {
            playerPreviewId = string.Empty;
            playerPreviewRevision = 0;
            playerPreviewConfigRevision = 0;
            playerInventoryApplyButton.Enabled = false;
            playerSpeedApplyButton.Enabled = false;
        }

        private static string InventoryModeLabel(int mode)
        {
            switch (mode)
            {
                case 2: return "2x";
                case 3: return "3x";
                case 4: return "on-demand pages (8-page cap)";
                default: return "1x";
            }
        }

        private TabPage BuildLaunderingPage()
        {
            TabPage page = NewPage("Laundering");
            TableLayoutPanel layout = PageLayout(3);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            page.Controls.Add(layout);

            GroupBox workflow = NewGroup("Daily laundering limit per business");
            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8) };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(new Label
            {
                Text = "Set one daily laundering ceiling for every owned business, or edit individual businesses in the table.",
                Dock = DockStyle.Fill,
                ForeColor = Muted
            }, 0, 0);

            FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            controls.Controls.Add(FieldLabel("Plan"));
            launderModeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            launderModeSelector.Items.AddRange(new object[] { "One limit for all owned businesses", "Edit each business" });
            launderModeSelector.SelectedIndex = 0;
            launderModeSelector.Width = 285;
            launderModeSelector.SelectedIndexChanged += delegate
            {
                launderLimitInput.Enabled = launderModeSelector.SelectedIndex == 0;
                InvalidateLaunderPreview();
                launderSummary.Text = launderModeSelector.SelectedIndex == 0
                    ? "Choose a daily limit and preview before applying."
                    : "Double-click the Planned Limit column for the business you want to change.";
            };
            controls.Controls.Add(launderModeSelector);
            controls.Controls.Add(FieldLabel("Limit $"));
            launderLimitInput.DecimalPlaces = 0;
            launderLimitInput.ThousandsSeparator = true;
            launderLimitInput.Minimum = LaunderMinimumLimit;
            launderLimitInput.Maximum = LaunderMaximumLimit;
            launderLimitInput.Increment = 500;
            launderLimitInput.Value = LaunderDefaultLimit;
            launderLimitInput.Width = 110;
            launderLimitInput.ValueChanged += delegate { InvalidateLaunderPreview(); };
            controls.Controls.Add(launderLimitInput);
            launderRefreshButton.Text = "Refresh";
            StyleButton(launderRefreshButton, false);
            launderRefreshButton.Width = 125;
            launderRefreshButton.Click += async delegate { await RefreshLaunderLimitsAsync(); };
            controls.Controls.Add(launderRefreshButton);
            launderPreviewButton.Text = "Preview";
            StyleButton(launderPreviewButton, true);
            launderPreviewButton.Width = 125;
            launderPreviewButton.Enabled = false;
            launderPreviewButton.Click += async delegate { await PreviewLaunderLimitsAsync(); };
            controls.Controls.Add(launderPreviewButton);
            launderApplyButton.Text = "Apply";
            StyleButton(launderApplyButton, true);
            launderApplyButton.Width = 115;
            launderApplyButton.Enabled = false;
            launderApplyButton.Click += async delegate { await ApplyLaunderPreviewAsync(); };
            controls.Controls.Add(launderApplyButton);
            body.Controls.Add(controls, 0, 1);
            workflow.Controls.Add(body);
            layout.Controls.Add(workflow, 0, 0);

            ConfigureLaunderGrid();
            layout.Controls.Add(launderGrid, 0, 1);
            launderSummary.Dock = DockStyle.Fill;
            launderSummary.Padding = new Padding(12, 10, 12, 0);
            launderSummary.ForeColor = Muted;
            launderSummary.Text = "Refresh limits to read the native $2,000 daily laundering ceiling for each owned business.";
            layout.Controls.Add(launderSummary, 0, 2);
            return page;
        }

        private TabPage BuildEffectsPage()
        {
            TabPage page = NewPage("Effects");
            TableLayoutPanel layout = PageLayout(4);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            page.Controls.Add(layout);

            GroupBox workflow = NewGroup("Drug effect pricing and physical intensity");
            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8) };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.Controls.Add(new Label
            {
                Text = "Review product-effect pricing and exposed physical intensity parameters, then preview all changes before applying.",
                Dock = DockStyle.Fill,
                ForeColor = Muted
            }, 0, 0);

            FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            controls.Controls.Add(FieldLabel("Plan"));
            effectsModeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            effectsModeSelector.Items.AddRange(new object[] { "Edit individually", "Scale all price changes" });
            effectsModeSelector.SelectedIndex = 0;
            effectsModeSelector.Width = 200;
            effectsModeSelector.SelectedIndexChanged += delegate
            {
                effectsScaleInput.Enabled = effectsModeSelector.SelectedIndex == 1;
                InvalidateEffectsPreview();
                effectsSummary.Text = effectsModeSelector.SelectedIndex == 0
                    ? "Double-click a price or intensity cell to edit it, then preview."
                    : "Enter a multiplier for the current price-change values, then preview.";
            };
            controls.Controls.Add(effectsModeSelector);
            effectsScaleInput.DecimalPlaces = 2;
            effectsScaleInput.Minimum = 0.01m;
            effectsScaleInput.Maximum = PracticalMultiplierMaximum;
            effectsScaleInput.Increment = 0.25m;
            effectsScaleInput.Value = 1m;
            effectsScaleInput.Width = 110;
            effectsScaleInput.Enabled = false;
            effectsScaleInput.ValueChanged += delegate { InvalidateEffectsPreview(); };
            controls.Controls.Add(effectsScaleInput);
            effectsRefreshButton.Text = "Refresh";
            StyleButton(effectsRefreshButton, false);
            effectsRefreshButton.Width = 130;
            effectsRefreshButton.Click += async delegate { await RefreshEffectsAsync(); };
            controls.Controls.Add(effectsRefreshButton);
            effectsPreviewButton.Text = "Preview";
            StyleButton(effectsPreviewButton, true);
            effectsPreviewButton.Width = 135;
            effectsPreviewButton.Enabled = false;
            effectsPreviewButton.Click += async delegate { await PreviewEffectsAsync(); };
            controls.Controls.Add(effectsPreviewButton);
            effectsApplyButton.Text = "Apply";
            StyleButton(effectsApplyButton, true);
            effectsApplyButton.Width = 125;
            effectsApplyButton.Enabled = false;
            effectsApplyButton.Click += async delegate { await ApplyEffectPreviewAsync(); };
            controls.Controls.Add(effectsApplyButton);
            body.Controls.Add(controls, 0, 1);
            workflow.Controls.Add(body);
            layout.Controls.Add(workflow, 0, 0);

            ConfigureEffectsGrid();
            layout.Controls.Add(effectsGrid, 0, 1);
            ConfigureEffectsParamGrid();
            layout.Controls.Add(effectsParamGrid, 0, 2);
            effectsSummary.Dock = DockStyle.Fill;
            effectsSummary.Padding = new Padding(12, 10, 12, 0);
            effectsSummary.ForeColor = Muted;
            effectsSummary.Text = "Refresh effects to read the loaded drug effects, their price increases, and any adjustable intensity values.";
            layout.Controls.Add(effectsSummary, 0, 3);
            return page;
        }

        private static void StyleGrid(DataGridView grid)
        {
            ApplyGridTheme(grid);
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }

        private void ConfigureLaunderGrid()
        {
            StyleGrid(launderGrid);
            launderGrid.Columns.Add("Business", "Business");
            launderGrid.Columns.Add("CurrentLimit", "Current Limit");
            launderGrid.Columns.Add("CurrentTotal", "Current Total");
            launderGrid.Columns.Add("Capacity", "Capacity");
            launderGrid.Columns.Add("PlannedLimit", "Planned Limit");
            launderGrid.Columns.Add("Status", "Status");
            launderGrid.Columns["Business"].ReadOnly = true;
            launderGrid.Columns["CurrentLimit"].ReadOnly = true;
            launderGrid.Columns["CurrentTotal"].ReadOnly = true;
            launderGrid.Columns["Capacity"].ReadOnly = true;
            launderGrid.Columns["Status"].ReadOnly = true;
            launderGrid.CellDoubleClick += LaunderGridCellDoubleClick;
            launderGrid.CellEndEdit += LaunderGridCellEndEdit;
        }

        private void LaunderGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || launderModeSelector.SelectedIndex != 1 || launderBusy)
                return;
            if (!string.Equals(launderGrid.Columns[e.ColumnIndex].Name, "PlannedLimit", StringComparison.Ordinal))
                return;
            LaunderBusinessRow model = launderGrid.Rows[e.RowIndex].Tag as LaunderBusinessRow;
            if (model == null)
                return;
            InvalidateLaunderPreview();
            if (!EnterCellEdit(launderGrid, launderGrid.Rows[e.RowIndex].Cells[e.ColumnIndex]))
                launderSummary.Text = "Finish or cancel the current cell edit first, then double-click the planned limit again.";
        }

        private void LaunderGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !string.Equals(launderGrid.Columns[e.ColumnIndex].Name, "PlannedLimit", StringComparison.Ordinal))
                return;
            LaunderBusinessRow model = launderGrid.Rows[e.RowIndex].Tag as LaunderBusinessRow;
            if (model == null)
                return;
            int value;
            if (!int.TryParse(Convert.ToString(launderGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value), NumberStyles.Number, CultureInfo.CurrentCulture, out value)
                || value < LaunderMinimumLimit || value > LaunderMaximumLimit)
            {
                launderSummary.Text = string.Format(CultureInfo.CurrentCulture, "The daily limit must be a whole number from {0:N0} to {1:N0}.", LaunderMinimumLimit, LaunderMaximumLimit);
                launderGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = model.PlannedLimit;
                return;
            }
            model.PlannedLimit = value;
            launderSummary.Text = "Manual limits changed. Preview before applying.";
            InvalidateLaunderPreview();
        }

        private void ConfigureEffectsGrid()
        {
            StyleGrid(effectsGrid);
            effectsGrid.Columns.Add("Effect", "Effect");
            effectsGrid.Columns.Add("Tier", "Tier");
            effectsGrid.Columns.Add("ValueChange", "Price Change $");
            effectsGrid.Columns.Add("ValueMultiplier", "Multiplier x");
            effectsGrid.Columns.Add("AddBaseValueMultiple", "Base Multiple x");
            effectsGrid.Columns["Effect"].ReadOnly = true;
            effectsGrid.Columns["Tier"].ReadOnly = true;
            effectsGrid.CellDoubleClick += EffectsGridCellDoubleClick;
            effectsGrid.CellEndEdit += EffectsGridCellEndEdit;
        }

        private void EffectsGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || effectsModeSelector.SelectedIndex != 0 || effectsBusy)
                return;
            string column = effectsGrid.Columns[e.ColumnIndex].Name;
            if (column != "ValueChange" && column != "ValueMultiplier" && column != "AddBaseValueMultiple")
                return;
            EffectPriceRow model = effectsGrid.Rows[e.RowIndex].Tag as EffectPriceRow;
            if (model == null)
                return;
            InvalidateEffectsPreview();
            if (!EnterCellEdit(effectsGrid, effectsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex]))
                effectsSummary.Text = "Finish or cancel the current cell edit first, then double-click the value again.";
        }

        private void EffectsGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            EffectPriceRow model = effectsGrid.Rows[e.RowIndex].Tag as EffectPriceRow;
            if (model == null)
                return;
            string column = effectsGrid.Columns[e.ColumnIndex].Name;
            decimal value;
            if (!decimal.TryParse(Convert.ToString(effectsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value), NumberStyles.Number, CultureInfo.CurrentCulture, out value))
            {
                effectsSummary.Text = "Enter a numeric value for the selected effect price field.";
                EffectsRefreshCell(model, column);
                return;
            }
            if (column == "ValueChange")
            {
                int rounded = (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);
                if (rounded < -16777215 || rounded > 16777215)
                {
                    effectsSummary.Text = "Price change must be between -16,777,215 and 16,777,215.";
                    EffectsRefreshCell(model, column);
                    return;
                }
                model.PlannedValueChange = rounded;
            }
            else if (column == "ValueMultiplier")
            {
                float multiplier = (float)value;
                if (multiplier < 0f || multiplier > 1000000f)
                {
                    effectsSummary.Text = "Multiplier must be between 0 and 1,000,000.";
                    EffectsRefreshCell(model, column);
                    return;
                }
                model.PlannedValueMultiplier = multiplier;
            }
            else if (column == "AddBaseValueMultiple")
            {
                float multiple = (float)value;
                if (multiple < -1000000f || multiple > 1000000f)
                {
                    effectsSummary.Text = "Base multiple must be between -1,000,000 and 1,000,000.";
                    EffectsRefreshCell(model, column);
                    return;
                }
                model.PlannedAddBaseValueMultiple = multiple;
            }
            effectsSummary.Text = "Manual effect values changed. Preview before applying.";
            InvalidateEffectsPreview();
        }

        private void EffectsRefreshCell(EffectPriceRow model, string column)
        {
            EffectPriceRow row = effectPriceRows.Find(r => r.EffectId == model.EffectId);
            if (row == null)
                return;
            foreach (DataGridViewRow gridRow in effectsGrid.Rows)
            {
                EffectPriceRow tag = gridRow.Tag as EffectPriceRow;
                if (tag != null && tag.EffectId == model.EffectId)
                {
                    if (column == "ValueChange") gridRow.Cells[column].Value = row.ValueChange;
                    else if (column == "ValueMultiplier") gridRow.Cells[column].Value = row.ValueMultiplier;
                    else if (column == "AddBaseValueMultiple") gridRow.Cells[column].Value = row.AddBaseValueMultiple;
                }
            }
        }

        private void ConfigureEffectsParamGrid()
        {
            StyleGrid(effectsParamGrid);
            effectsParamGrid.Columns.Add("Effect", "Effect");
            effectsParamGrid.Columns.Add("Parameter", "Parameter");
            effectsParamGrid.Columns.Add("Value", "Value");
            effectsParamGrid.Columns.Add("Range", "Allowed Range");
            effectsParamGrid.Columns.Add("Hint", "What it changes");
            effectsParamGrid.Columns["Effect"].ReadOnly = true;
            effectsParamGrid.Columns["Parameter"].ReadOnly = true;
            effectsParamGrid.Columns["Range"].ReadOnly = true;
            effectsParamGrid.Columns["Hint"].ReadOnly = true;
            effectsParamGrid.CellDoubleClick += EffectsParamGridCellDoubleClick;
            effectsParamGrid.CellEndEdit += EffectsParamGridCellEndEdit;
        }

        private void EffectsParamGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || effectsBusy)
                return;
            if (!string.Equals(effectsParamGrid.Columns[e.ColumnIndex].Name, "Value", StringComparison.Ordinal))
                return;
            EffectParamRow model = effectsParamGrid.Rows[e.RowIndex].Tag as EffectParamRow;
            if (model == null || model.ReadOnly)
                return;
            InvalidateEffectsPreview();
            if (!EnterCellEdit(effectsParamGrid, effectsParamGrid.Rows[e.RowIndex].Cells[e.ColumnIndex]))
                effectsSummary.Text = "Finish or cancel the current cell edit first, then double-click the value again.";
        }

        private void EffectsParamGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !string.Equals(effectsParamGrid.Columns[e.ColumnIndex].Name, "Value", StringComparison.Ordinal))
                return;
            EffectParamRow model = effectsParamGrid.Rows[e.RowIndex].Tag as EffectParamRow;
            if (model == null)
                return;
            float value;
            if (!float.TryParse(Convert.ToString(effectsParamGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value), NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || value < model.Min || value > model.Max)
            {
                effectsSummary.Text = string.Format(CultureInfo.CurrentCulture, "{0} must be between {1:0.###} and {2:0.###}.", model.DisplayName, model.Min, model.Max);
                effectsParamGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = model.Value;
                return;
            }
            model.PlannedValue = value;
            effectsSummary.Text = "Manual intensity values changed. Preview before applying.";
            InvalidateEffectsPreview();
        }

        private async Task RefreshLaunderLimitsAsync()
        {
            SetLaunderBusy(true, "Reading live laundering limits...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("business.launder.list", new Dictionary<string, object>(), true);
                ShowResult(result);
                if (!result.Success)
                    return;
                launderRows.Clear();
                object businesses;
                if (result.Data != null && result.Data.TryGetValue("businesses", out businesses))
                {
                    foreach (object item in JsonUtil.AsItems(businesses))
                    {
                        Dictionary<string, object> row = JsonUtil.AsObject(item);
                        if (row == null)
                            continue;
                        string code = JsonUtil.GetString(row, "businessCode", string.Empty);
                        int limit = (int)JsonUtil.GetLong(row, "customDailyLimit", LaunderDefaultLimit);
                        launderRows.Add(new LaunderBusinessRow
                        {
                            BusinessCode = code,
                            DisplayName = BusinessDisplayName(code),
                            CustomLimit = limit,
                            CurrentTotal = GetFloat(row, "currentLaunderTotal", 0f),
                            Capacity = GetFloat(row, "launderCapacity", 0f),
                            Overridden = JsonUtil.GetBool(row, "isOverridden", false),
                            PlannedLimit = limit
                        });
                    }
                }
                DisplayLaunderRows();
                launderSummary.Text = string.Format(CultureInfo.CurrentCulture, "{0} owned business{1} loaded. The native daily ceiling is $2,000 per business.", launderRows.Count, launderRows.Count == 1 ? string.Empty : "es");
            }
            finally
            {
                SetLaunderBusy(false, null);
            }
        }

        private void DisplayLaunderRows()
        {
            launderGrid.Rows.Clear();
            foreach (LaunderBusinessRow model in launderRows)
            {
                int index = launderGrid.Rows.Add(
                    model.DisplayName,
                    model.CustomLimit,
                    model.CurrentTotal.ToString("0.##", CultureInfo.CurrentCulture),
                    model.Capacity.ToString("0.##", CultureInfo.CurrentCulture),
                    model.PlannedLimit,
                    model.Overridden ? "Custom" : "Native");
                launderGrid.Rows[index].Tag = model;
            }
        }

        private void DisplayLaunderPreview(Dictionary<string, object> data)
        {
            object businesses;
            if (data == null || !data.TryGetValue("businesses", out businesses))
                return;
            int changed = 0;
            foreach (object item in JsonUtil.AsItems(businesses))
            {
                Dictionary<string, object> preview = JsonUtil.AsObject(item);
                if (preview == null)
                    continue;
                string code = JsonUtil.GetString(preview, "businessCode", string.Empty);
                int oldLimit = JsonUtil.GetInt(preview, "oldLimit", 0);
                int newLimit = JsonUtil.GetInt(preview, "newLimit", oldLimit);
                if (oldLimit != newLimit)
                    changed++;
                for (int i = 0; i < launderGrid.Rows.Count; i++)
                {
                    LaunderBusinessRow model = launderGrid.Rows[i].Tag as LaunderBusinessRow;
                    if (model == null || !string.Equals(model.BusinessCode, code, StringComparison.OrdinalIgnoreCase))
                        continue;
                    model.PlannedLimit = newLimit;
                    launderGrid.Rows[i].Cells["CurrentLimit"].Value = oldLimit;
                    launderGrid.Rows[i].Cells["PlannedLimit"].Value = newLimit;
                    launderGrid.Rows[i].Cells["Status"].Value = oldLimit == newLimit ? "No change" : "Ready to apply";
                    break;
                }
            }
            launderSummary.Text = string.Format(CultureInfo.CurrentCulture, "Preview ready: {0} business limit change{1} will be applied. Planned values show the upcoming limit; Apply persists them.", changed, changed == 1 ? string.Empty : "s");
        }

        private async Task PreviewLaunderLimitsAsync()
        {
            InvalidateLaunderPreview();
            SetLaunderBusy(true, "Building a revision-checked laundering preview...");
            try
            {
                List<Dictionary<string, object>> targets = new List<Dictionary<string, object>>();
                foreach (LaunderBusinessRow row in launderRows)
                {
                    int limit = launderModeSelector.SelectedIndex == 0 ? (int)launderLimitInput.Value : row.PlannedLimit;
                    if (limit < LaunderMinimumLimit || limit > LaunderMaximumLimit)
                    {
                        launderSummary.Text = string.Format(CultureInfo.CurrentCulture, "The daily limit must be from {0:N0} to {1:N0}.", LaunderMinimumLimit, LaunderMaximumLimit);
                        return;
                    }
                    targets.Add(new Dictionary<string, object> { { "businessCode", row.BusinessCode }, { "limit", limit } });
                }
                if (targets.Count == 0)
                {
                    launderSummary.Text = "Refresh laundering limits before previewing.";
                    return;
                }

                OperationResult result = await bridge.InvokeAsync("business.launder.preview", new Dictionary<string, object> { { "targets", targets } }, true);
                ShowResult(result);
                if (!result.Success)
                    return;
                launderPreviewId = JsonUtil.GetString(result.Data, "previewId", string.Empty);
                launderPreviewRevision = JsonUtil.GetLong(result.Data, "expectedRevision", result.Revision);
                launderPreviewConfigRevision = JsonUtil.GetLong(result.Data, "expectedConfigRevision", 0);
                launderApplyButton.Enabled = soloHost && launderPreviewId.Length > 0;
                DisplayLaunderPreview(result.Data);
                int count = JsonUtil.GetInt(result.Data, "count", 0);
                if (count == 0)
                    launderSummary.Text = "Preview ready; no business limit changes were detected.";
            }
            finally
            {
                SetLaunderBusy(false, null);
            }
        }

        private async Task ApplyLaunderPreviewAsync()
        {
            if (launderPreviewId.Length == 0)
                return;
            if (!Confirm("Apply these business laundering limits to the loaded save profile? The bridge re-applies the ceiling every game day and keeps the old profile as a rollback copy."))
                return;
            SetLaunderBusy(true, "Applying and persisting laundering limits...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("business.launder.applyPreview", new Dictionary<string, object>
                {
                    { "previewId", launderPreviewId },
                    { "expectedRevision", launderPreviewRevision },
                    { "expectedConfigRevision", launderPreviewConfigRevision }
                }, false);
                ShowResult(result);
                InvalidateLaunderPreview();
                if (result.Success)
                {
                    await RefreshLaunderLimitsAsync();
                }
            }
            finally
            {
                SetLaunderBusy(false, null);
            }
        }

        private async Task RefreshEffectsAsync()
        {
            SetEffectsBusy(true, "Reading loaded drug effects and intensity values...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("effects.list", new Dictionary<string, object>(), true);
                ShowResult(result);
                if (!result.Success)
                    return;
                effectPriceRows.Clear();
                effectParamRows.Clear();
                object effects;
                if (result.Data != null && result.Data.TryGetValue("effects", out effects))
                {
                    foreach (object item in JsonUtil.AsItems(effects))
                    {
                        Dictionary<string, object> row = JsonUtil.AsObject(item);
                        if (row == null)
                            continue;
                        string id = JsonUtil.GetString(row, "effectId", string.Empty);
                        string name = JsonUtil.GetString(row, "name", string.Empty);
                        EffectPriceRow price = new EffectPriceRow
                        {
                            EffectId = id,
                            Name = name,
                            Tier = JsonUtil.GetInt(row, "tier", 0),
                            ValueChange = (int)JsonUtil.GetLong(row, "valueChange", 0),
                            ValueMultiplier = GetFloat(row, "valueMultiplier", 1f),
                            AddBaseValueMultiple = GetFloat(row, "addBaseValueMultiple", 0f)
                        };
                        price.PlannedValueChange = price.ValueChange;
                        price.PlannedValueMultiplier = price.ValueMultiplier;
                        price.PlannedAddBaseValueMultiple = price.AddBaseValueMultiple;
                        effectPriceRows.Add(price);

                        object parameters;
                        if (row.TryGetValue("parameters", out parameters))
                        {
                            foreach (object paramItem in JsonUtil.AsItems(parameters))
                            {
                                Dictionary<string, object> paramRow = JsonUtil.AsObject(paramItem);
                                if (paramRow == null)
                                    continue;
                                EffectParamRow param = new EffectParamRow
                                {
                                    EffectId = id,
                                    EffectName = name,
                                    ParamName = JsonUtil.GetString(paramRow, "name", string.Empty),
                                    DisplayName = JsonUtil.GetString(paramRow, "displayName", string.Empty),
                                    Value = GetFloat(paramRow, "value", 0f),
                                    Min = GetFloat(paramRow, "min", 0f),
                                    Max = GetFloat(paramRow, "max", 1f),
                                    Hint = JsonUtil.GetString(paramRow, "hint", string.Empty),
                                    ReadOnly = JsonUtil.GetBool(paramRow, "readOnly", false)
                                };
                                param.PlannedValue = param.Value;
                                effectParamRows.Add(param);
                            }
                        }
                    }
                }
                DisplayEffectsRows();
                int count = JsonUtil.GetInt(result.Data, "count", effectPriceRows.Count);
                effectsSummary.Text = string.Format(CultureInfo.CurrentCulture, "{0} effect{1} loaded; {2} adjustable intensity parameter{3}.", count, count == 1 ? string.Empty : "s", effectParamRows.Count, effectParamRows.Count == 1 ? string.Empty : "s");
            }
            finally
            {
                SetEffectsBusy(false, null);
            }
        }

        private void DisplayEffectsRows()
        {
            effectsGrid.Rows.Clear();
            foreach (EffectPriceRow model in effectPriceRows)
            {
                int index = effectsGrid.Rows.Add(
                    FriendlyEffectName(model),
                    model.Tier,
                    model.PlannedValueChange,
                    model.PlannedValueMultiplier,
                    model.PlannedAddBaseValueMultiple);
                effectsGrid.Rows[index].Tag = model;
            }
            effectsParamGrid.Rows.Clear();
            foreach (EffectParamRow model in effectParamRows)
            {
                int index = effectsParamGrid.Rows.Add(
                    FriendlyEffectName(model),
                    model.DisplayName,
                    model.PlannedValue,
                    string.Format(CultureInfo.CurrentCulture, "{0:0.###} - {1:0.###}", model.Min, model.Max),
                    model.Hint);
                if (model.ReadOnly)
                {
                    effectsParamGrid.Rows[index].Cells["Value"].ReadOnly = true;
                    effectsParamGrid.Rows[index].Cells["Value"].Style.ForeColor = Muted;
                }
                effectsParamGrid.Rows[index].Tag = model;
            }
        }

        private void DisplayEffectsPreview(Dictionary<string, object> data)
        {
            object effects;
            if (data == null || !data.TryGetValue("effects", out effects))
                return;
            int changed = 0;
            foreach (object item in JsonUtil.AsItems(effects))
            {
                Dictionary<string, object> preview = JsonUtil.AsObject(item);
                if (preview == null)
                    continue;
                string effectId = JsonUtil.GetString(preview, "effectId", string.Empty);
                EffectPriceRow price = null;
                foreach (EffectPriceRow candidate in effectPriceRows)
                    if (string.Equals(candidate.EffectId, effectId, StringComparison.OrdinalIgnoreCase))
                    {
                        price = candidate;
                        break;
                    }
                if (price != null)
                {
                    price.PlannedValueChange = (int)JsonUtil.GetLong(preview, "newValueChange", price.ValueChange);
                    price.PlannedValueMultiplier = GetFloat(preview, "newValueMultiplier", price.ValueMultiplier);
                    price.PlannedAddBaseValueMultiple = GetFloat(preview, "newAddBaseValueMultiple", price.AddBaseValueMultiple);
                    if (price.PlannedValueChange != price.ValueChange
                        || Math.Abs(price.PlannedValueMultiplier - price.ValueMultiplier) > 0.0001f
                        || Math.Abs(price.PlannedAddBaseValueMultiple - price.AddBaseValueMultiple) > 0.0001f)
                        changed++;
                }
                object parameters;
                if (preview.TryGetValue("newParameters", out parameters))
                {
                    foreach (object parameterItem in JsonUtil.AsItems(parameters))
                    {
                        Dictionary<string, object> parameter = JsonUtil.AsObject(parameterItem);
                        if (parameter == null)
                            continue;
                        string name = JsonUtil.GetString(parameter, "name", string.Empty);
                        foreach (EffectParamRow candidate in effectParamRows)
                            if (string.Equals(candidate.EffectId, effectId, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(candidate.ParamName, name, StringComparison.OrdinalIgnoreCase))
                            {
                                candidate.PlannedValue = GetFloat(parameter, "value", candidate.Value);
                                if (Math.Abs(candidate.PlannedValue.Value - candidate.Value) > 0.0001f)
                                    changed++;
                                break;
                            }
                    }
                }
            }
            DisplayEffectsRows();
            effectsSummary.Text = string.Format(CultureInfo.CurrentCulture, "Preview ready: {0} upcoming effect value change{1} are shown in the grids. Apply to persist.", changed, changed == 1 ? string.Empty : "s");
        }

        private async Task PreviewEffectsAsync()
        {
            InvalidateEffectsPreview();
            SetEffectsBusy(true, "Building a revision-checked effects preview...");
            try
            {
                List<Dictionary<string, object>> targets;
                string validationError;
                if (!TryBuildEffectTargets(out targets, out validationError))
                {
                    effectsSummary.Text = validationError;
                    return;
                }
                OperationResult result = await bridge.InvokeAsync("effects.preview", new Dictionary<string, object> { { "targets", targets } }, true);
                ShowResult(result);
                if (!result.Success)
                    return;
                effectsPreviewId = JsonUtil.GetString(result.Data, "previewId", string.Empty);
                effectsPreviewRevision = JsonUtil.GetLong(result.Data, "expectedRevision", result.Revision);
                effectsPreviewConfigRevision = JsonUtil.GetLong(result.Data, "expectedConfigRevision", 0);
                effectsApplyButton.Enabled = soloHost && effectsPreviewId.Length > 0;
                DisplayEffectsPreview(result.Data);
                int count = JsonUtil.GetInt(result.Data, "count", 0);
                if (count == 0)
                    effectsSummary.Text = "Preview ready; no effect values were changed.";
            }
            finally
            {
                SetEffectsBusy(false, null);
            }
        }

        private bool TryBuildEffectTargets(out List<Dictionary<string, object>> targets, out string error)
        {
            targets = new List<Dictionary<string, object>>();
            error = null;
            Dictionary<string, List<Dictionary<string, object>>> parametersByEffect = new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);

            foreach (EffectParamRow param in effectParamRows)
            {
                if (!param.PlannedValue.HasValue || Math.Abs(param.PlannedValue.Value - param.Value) <= 0.0001f)
                    continue;
                List<Dictionary<string, object>> list;
                if (!parametersByEffect.TryGetValue(param.EffectId, out list))
                {
                    list = new List<Dictionary<string, object>>();
                    parametersByEffect[param.EffectId] = list;
                }
                list.Add(new Dictionary<string, object> { { "name", param.ParamName }, { "value", param.PlannedValue.Value } });
            }

            foreach (EffectPriceRow row in effectPriceRows)
            {
                bool priceChanged = row.PlannedValueChange != row.ValueChange
                    || Math.Abs(row.PlannedValueMultiplier - row.ValueMultiplier) > 0.0001f
                    || Math.Abs(row.PlannedAddBaseValueMultiple - row.AddBaseValueMultiple) > 0.0001f;
                bool hasParams = parametersByEffect.ContainsKey(row.EffectId);
                if (!priceChanged && !hasParams)
                    continue;
                Dictionary<string, object> target = new Dictionary<string, object> { { "effectId", row.EffectId } };
                if (row.PlannedValueChange != row.ValueChange)
                    target["valueChange"] = row.PlannedValueChange;
                if (Math.Abs(row.PlannedValueMultiplier - row.ValueMultiplier) > 0.0001f)
                    target["valueMultiplier"] = row.PlannedValueMultiplier;
                if (Math.Abs(row.PlannedAddBaseValueMultiple - row.AddBaseValueMultiple) > 0.0001f)
                    target["addBaseValueMultiple"] = row.PlannedAddBaseValueMultiple;
                List<Dictionary<string, object>> list;
                if (parametersByEffect.TryGetValue(row.EffectId, out list) && list.Count > 0)
                    target["parameters"] = list;
                targets.Add(target);
            }

            if (effectsModeSelector.SelectedIndex == 1)
            {
                decimal scale = effectsScaleInput.Value;
                foreach (EffectPriceRow row in effectPriceRows)
                {
                    decimal raw = Math.Round(row.ValueChange * scale, 0, MidpointRounding.AwayFromZero);
                    int scaled = (int)Math.Max(-PracticalMoneyMaximum, Math.Min(PracticalMoneyMaximum, raw));
                    if (scaled == row.ValueChange)
                        continue;
                    Dictionary<string, object> target = new Dictionary<string, object> { { "effectId", row.EffectId }, { "valueChange", scaled } };
                    targets.Add(target);
                }
            }

            if (targets.Count == 0)
            {
                error = "No effect values have changed. Edit a value or choose a scale before previewing.";
                return false;
            }
            return true;
        }

        private async Task ApplyEffectPreviewAsync()
        {
            if (effectsPreviewId.Length == 0)
                return;
            if (!Confirm("Apply these effect price and intensity changes to the loaded save profile? The bridge writes them to the loaded effect assets and re-applies them on future sessions."))
                return;
            SetEffectsBusy(true, "Applying and persisting effect changes...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("effects.applyPreview", new Dictionary<string, object>
                {
                    { "previewId", effectsPreviewId },
                    { "expectedRevision", effectsPreviewRevision },
                    { "expectedConfigRevision", effectsPreviewConfigRevision }
                }, false);
                ShowResult(result);
                InvalidateEffectsPreview();
                if (result.Success)
                {
                    await RefreshEffectsAsync();
                }
            }
            finally
            {
                SetEffectsBusy(false, null);
            }
        }

        private void SetLaunderBusy(bool busy, string message)
        {
            launderBusy = busy;
            launderRefreshButton.Enabled = !busy && bridgeConnected && soloHost;
            launderPreviewButton.Enabled = !busy && bridgeConnected && soloHost && launderRows.Count > 0;
            launderApplyButton.Enabled = !busy && launderPreviewId.Length > 0 && soloHost;
            if (!string.IsNullOrEmpty(message))
                launderSummary.Text = message;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void SetEffectsBusy(bool busy, string message)
        {
            effectsBusy = busy;
            effectsRefreshButton.Enabled = !busy && bridgeConnected && soloHost;
            effectsPreviewButton.Enabled = !busy && bridgeConnected && soloHost && effectPriceRows.Count > 0;
            effectsApplyButton.Enabled = !busy && effectsPreviewId.Length > 0 && soloHost;
            if (!string.IsNullOrEmpty(message))
                effectsSummary.Text = message;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void InvalidateLaunderPreview()
        {
            launderPreviewId = string.Empty;
            launderPreviewRevision = 0;
            launderPreviewConfigRevision = 0;
            launderApplyButton.Enabled = false;
        }

        private void InvalidateEffectsPreview()
        {
            effectsPreviewId = string.Empty;
            effectsPreviewRevision = 0;
            effectsPreviewConfigRevision = 0;
            effectsApplyButton.Enabled = false;
        }

        private static float GetFloat(Dictionary<string, object> obj, string key, float fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
                return fallback;
            try
            {
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static string BusinessDisplayName(string code)
        {
            switch ((code ?? string.Empty).ToLowerInvariant())
            {
                case "laundromat": return "Laundromat";
                case "carwash": return "Car Wash";
                case "postoffice": return "Post Office";
                case "tacoticklers": return "Taco Ticklers";
                default: return string.IsNullOrEmpty(code) ? "Unknown business" : code;
            }
        }

        private static string FriendlyEffectName(EffectPriceRow model)
        {
            return string.IsNullOrEmpty(model.Name) ? model.EffectId : model.Name;
        }

        private static string FriendlyEffectName(EffectParamRow model)
        {
            return string.IsNullOrEmpty(model.EffectName) ? model.EffectId : model.EffectName;
        }

        private sealed class LaunderBusinessRow
        {
            public string BusinessCode;
            public string DisplayName;
            public int CustomLimit;
            public float CurrentTotal;
            public float Capacity;
            public bool Overridden;
            public int PlannedLimit;
        }

        private sealed class EffectPriceRow
        {
            public string EffectId;
            public string Name;
            public int Tier;
            public int ValueChange;
            public float ValueMultiplier;
            public float AddBaseValueMultiple;
            public int PlannedValueChange;
            public float PlannedValueMultiplier;
            public float PlannedAddBaseValueMultiple;
        }

        private sealed class EffectParamRow
        {
            public string EffectId;
            public string EffectName;
            public string ParamName;
            public string DisplayName;
            public float Value;
            public float Min;
            public float Max;
            public string Hint;
            public bool ReadOnly;
            public float? PlannedValue;
        }
    }
}
