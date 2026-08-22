using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScheduleIControlCenter
{
    internal sealed partial class MainForm : Form
    {
        private const decimal PracticalMoneyMaximum = 16777215m;
        private const decimal PracticalMultiplierMaximum = 1000000m;
        private readonly GameEnvironment environment;
        private readonly SaveService saves;
        private readonly BridgeClient bridge;
        private readonly Timer statusTimer = new Timer();
        private readonly Dictionary<string, string> helpTopics = new Dictionary<string, string>();

        private readonly ComboBox saveSelector = new IntelComboBox();
        private readonly Label gameStatus = new IntelStatusChip();
        private readonly Label bridgeStatus = new IntelStatusChip();
        private readonly Label saveStatus = new Label();
        private readonly Label authorityStatus = new IntelStatusChip();
        private readonly Label workspaceTitle = new Label();
        private readonly Label workspaceKicker = new Label();
        private readonly List<Button> sidebarButtons = new List<Button>();
        private readonly Label overviewHeadline = new Label();
        private readonly Label overviewDetail = new Label();
        private readonly Panel statusAccent = new Panel();
        private readonly TextBox homeOperationLog = new IntelTextBox();
        private readonly TabControl navigation = new BorderlessTabControl();

        private readonly ComboBox marketDrugSelector = new IntelComboBox();
        private readonly ComboBox marketModeSelector = new IntelComboBox();
        private readonly NumericUpDown marketFactorInput = new IntelNumericUpDown();
        private readonly DataGridView marketGrid = new IntelDataGridView();
        private readonly Label marketSummary = new Label();
        private readonly Button marketApplyButton = new IntelButton();
        private readonly Button marketPreviewButton = new IntelButton();

        private readonly CheckBox dealLimitOverrideToggle = new IntelCheckBox();
        private readonly NumericUpDown dealLimitInput = new IntelNumericUpDown();
        private readonly Label dealLimitState = new Label();
        private readonly Button dealLimitRefreshButton = new IntelButton();
        private readonly Button dealLimitPreviewButton = new IntelButton();
        private readonly Button dealLimitApplyButton = new IntelButton();
        private readonly ComboBox sellDrugSelector = new IntelComboBox();
        private readonly ComboBox sellModeSelector = new IntelComboBox();
        private readonly NumericUpDown sellFactorInput = new IntelNumericUpDown();
        private readonly DataGridView sellGrid = new IntelDataGridView();
        private readonly Label sellSummary = new Label();
        private readonly Button sellRefreshButton = new IntelButton();
        private readonly Button sellPreviewButton = new IntelButton();
        private readonly Button sellApplyButton = new IntelButton();
        private readonly Dictionary<string, SellPriceProductRow> sellRows = new Dictionary<string, SellPriceProductRow>(StringComparer.OrdinalIgnoreCase);

        private readonly ComboBox customerScopeSelector = new IntelComboBox();
        private readonly ComboBox customerModeSelector = new IntelComboBox();
        private readonly NumericUpDown customerFactorInput = new IntelNumericUpDown();
        private readonly DataGridView customerGrid = new IntelDataGridView();
        private readonly Label customerSummary = new Label();
        private readonly Button customerRefreshButton = new IntelButton();
        private readonly Button customerPreviewButton = new IntelButton();
        private readonly Button customerApplyButton = new IntelButton();
        private readonly Dictionary<string, CustomerAllowanceRow> customerRows = new Dictionary<string, CustomerAllowanceRow>(StringComparer.OrdinalIgnoreCase);

        private readonly ComboBox propertySelector = new IntelComboBox();
        private readonly Button ownLiveButton = new IntelButton();
        private readonly Button ownOfflineButton = new IntelButton();
        private readonly Label propertyGuidance = new Label();

        private readonly Button consoleButton = new IntelButton();
        private readonly Label toolsModeNotice = new Label();

        private readonly TextBox commandInput = new IntelTextBox();
        private readonly TextBox commandOutput = new IntelTextBox();
        private readonly TextBox operationLog = new IntelTextBox();
        private readonly ComboBox legacyDrugSelector = new IntelComboBox();
        private readonly NumericUpDown legacyFactorInput = new IntelNumericUpDown();
        private readonly Button legacyApplyButton = new IntelButton();

        private readonly TextBox helpSearch = new IntelTextBox();
        private readonly ListBox helpTopicList = new IntelListBox();
        private readonly RichTextBox helpText = new RichTextBox();
        private readonly ListBox diagnosticIncidentList = new IntelListBox();
        private readonly RichTextBox diagnosticDetail = new RichTextBox();
        private readonly Label diagnosticHealth = new Label();
        private readonly Button diagnosticRefreshButton = new IntelButton();
        private readonly Button diagnosticCopyButton = new IntelButton();
        private readonly Button diagnosticExportButton = new IntelButton();
        private readonly DiagnosticsService diagnostics = DiagnosticsService.Current;
        private readonly List<DiagnosticIncident> diagnosticDisplayedIncidents = new List<DiagnosticIncident>();

        private bool statusPollBusy;
        private bool marketBusy;
        private bool dealLimitBusy;
        private bool sellBusy;
        private bool customerBusy;
        private bool bridgeConnected;
        private bool soloHost;
        private bool compatibilityPromptShown;
        private bool dealLimitCapability;
        private bool dealLimitStatusKnown;
        private long currentBridgeRevision;
        private string loadedSavePath = string.Empty;
        private string marketPreviewId = string.Empty;
        private long marketPreviewRevision;
        private long marketPreviewConfigRevision;
        private string dealLimitPreviewId = string.Empty;
        private long dealLimitPreviewRevision;
        private long dealLimitPreviewConfigRevision;
        private string sellPreviewId = string.Empty;
        private long sellPreviewRevision;
        private decimal activeUnitPriceMin = 1m;
        private decimal activeUnitPriceMax = PracticalMoneyMaximum;
        private string customerPreviewId = string.Empty;
        private long customerPreviewRevision;
        private long customerPreviewConfigRevision;
        private readonly List<string> sessionOperationHistory = new List<string>();
        private int sessionOperationCount;
        private const int MaxSessionOperationHistory = 150;

        public MainForm(GameEnvironment environment, string layoutDumpPath = null)
        {
            this.environment = environment;
            saves = new SaveService(environment);
            bridge = new BridgeClient();
            updateService = new UpdateService();

            Text = "Schedule I Control Center - " + ReleaseInfo.Label;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1280, 780);
            Size = new Size(1450, 850);
            BackColor = AppBackground;
            Font = UiFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            BuildHelpTopics();
            BuildUi();
            RefreshSaves();
            RefreshRuntimeLabels(false, null);
            RefreshDiagnostics();

            statusTimer.Interval = 3000;
            statusTimer.Tick += async delegate { await RefreshRuntimeStatusAsync(); };
            if (!string.IsNullOrEmpty(layoutDumpPath))
            {
                Shown += delegate
                {
                    try
                    {
                        DumpLayout(layoutDumpPath);
                    }
                    finally
                    {
                        Application.Exit();
                    }
                };
            }
            Shown += async delegate
            {
                ApplyNativeDarkMode(this);
                statusTimer.Start();
                ShowCompletedUpdateIfNeeded();
                await RefreshRuntimeStatusAsync();
                await CheckForUpdatesAsync(true);
            };
            FormClosed += delegate { statusTimer.Stop(); };
        }

        private SaveDescriptor SelectedSave { get { return saveSelector.SelectedItem as SaveDescriptor; } }

        private void BuildUi()
        {
            navigation.Dock = DockStyle.Fill;
            ConfigureNavigationTabs(navigation);
            navigation.TabPages.Add(BuildHomePage());

            TabPage products = BuildProductsPage();
            products.Text = "Fair-value sync";
            TabPage prices = BuildPricesLimitsPage();
            prices.Text = "Sell prices & deal limits";
            TabPage customers = BuildCustomersPage();
            customers.Text = "Customer allowances";
            navigation.TabPages.Add(BuildGroupedWorkspacePage(
                "Market intelligence",
                "Coordinate value, pricing, deal ceilings and customer affordability from one reviewed workflow.",
                products, prices, customers));

            TabPage player = BuildPlayerPage();
            player.Text = "Player & inventory";
            navigation.TabPages.Add(player);

            TabPage laundering = BuildLaunderingPage();
            laundering.Text = "Laundering";
            TabPage properties = BuildPropertiesPage();
            properties.Text = "Properties";
            navigation.TabPages.Add(BuildGroupedWorkspacePage(
                "Business operations",
                "Manage owned infrastructure and its operating limits without mixing it into market controls.",
                laundering, properties));

            TabPage effects = BuildEffectsPage();
            effects.Text = "Drug effects";
            navigation.TabPages.Add(effects);

            TabPage savesPage = BuildSaveToolsPage();
            savesPage.Text = "Save & safety";
            navigation.TabPages.Add(savesPage);

            TabPage updates = BuildUpdatesPage();
            updates.Text = "Updates";
            navigation.TabPages.Add(updates);

            TabPage help = BuildHelpPage();
            help.Text = "Help center";
            navigation.TabPages.Add(help);

            TabPage diagnostics = BuildAdvancedPage();
            diagnostics.Text = "Diagnostics";
            navigation.TabPages.Add(diagnostics);

            navigation.SelectedIndexChanged += delegate { UpdateWorkspaceSelection(); };

            Panel workspace = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground };
            Control header = BuildHeader();
            workspace.Controls.Add(navigation);
            workspace.Controls.Add(header);
            navigation.BringToFront();

            Panel shell = new Panel { Dock = DockStyle.Fill, BackColor = AppBackgroundDeep };
            shell.Controls.Add(workspace);
            shell.Controls.Add(BuildSidebar());
            Controls.Add(shell);

            NormalizeInputHeights(navigation);
            NormalizeInputHeights(header);
            UpdateWorkspaceSelection();
        }

        private TabPage BuildGroupedWorkspacePage(string title, string description, params TabPage[] sections)
        {
            TabPage page = NewPage(title);
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = AppBackground,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Panel intro = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground, Padding = new Padding(12, 12, 12, 8) };
            Label heading = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 35,
                Font = HeadingFont,
                ForeColor = Ink,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Label detail = new Label
            {
                Text = description,
                Dock = DockStyle.Fill,
                Font = UiFont,
                ForeColor = Muted,
                TextAlign = ContentAlignment.TopLeft,
                AutoEllipsis = false
            };
            intro.Controls.Add(detail);
            intro.Controls.Add(heading);
            layout.Controls.Add(intro, 0, 0);

            FlowLayoutPanel sectionBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Surface,
                Padding = new Padding(8, 4, 8, 3),
                Margin = new Padding(0)
            };
            Panel sectionHost = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground, Margin = new Padding(0) };
            List<Button> sectionButtons = new List<Button>();
            List<Panel> sectionSurfaces = new List<Panel>();
            for (int i = 0; i < sections.Length; i++)
            {
                int targetIndex = i;
                TabPage section = sections[i];
                string sectionTitle = section.Text;
                Panel sectionSurface = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground, Visible = i == 0 };
                while (section.Controls.Count > 0)
                {
                    Control content = section.Controls[0];
                    section.Controls.RemoveAt(0);
                    sectionSurface.Controls.Add(content);
                }
                sectionSurfaces.Add(sectionSurface);
                sectionHost.Controls.Add(sectionSurface);
                section.Dispose();

                Button sectionButton = new IntelButton
                {
                    Text = sectionTitle,
                    UseMnemonic = false,
                    Width = Math.Max(150, Math.Min(230, TextRenderer.MeasureText(sectionTitle, TabFont).Width + 36)),
                    Height = 34,
                    Margin = new Padding(2, 1, 4, 1),
                    FlatStyle = FlatStyle.Flat,
                    Font = TabFont,
                    Cursor = Cursors.Hand,
                    UseVisualStyleBackColor = false
                };
                sectionButton.FlatAppearance.BorderSize = 0;
                sectionButton.Resize += delegate { SetRoundedRegion(sectionButton, 9); };
                sectionButton.Click += delegate
                {
                    for (int j = 0; j < sectionSurfaces.Count; j++)
                    {
                        sectionSurfaces[j].Visible = j == targetIndex;
                        sectionButtons[j].BackColor = j == targetIndex ? PrimarySoft : Surface;
                        sectionButtons[j].ForeColor = j == targetIndex ? PrimaryHover : Muted;
                    }
                    sectionSurfaces[targetIndex].BringToFront();
                };
                sectionButtons.Add(sectionButton);
                sectionBar.Controls.Add(sectionButton);
            }
            if (sectionButtons.Count > 0)
            {
                sectionButtons[0].BackColor = PrimarySoft;
                sectionButtons[0].ForeColor = PrimaryHover;
                for (int i = 1; i < sectionButtons.Count; i++)
                {
                    sectionButtons[i].BackColor = Surface;
                    sectionButtons[i].ForeColor = Muted;
                }
            }
            layout.Controls.Add(sectionBar, 0, 1);
            layout.Controls.Add(sectionHost, 0, 2);
            page.Controls.Add(layout);
            return page;
        }

        private Control BuildSidebar()
        {
            Panel sidebar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 248,
                BackColor = AppBackgroundDeep,
                Padding = new Padding(12, 12, 12, 14)
            };

            Panel brand = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = AppBackgroundDeep };
            IntelCubeMark cube = new IntelCubeMark { Location = new Point(5, 7), Size = new Size(58, 58) };
            Label brandName = new Label
            {
                Text = "SCHEDULE I",
                Location = new Point(72, 12),
                Size = new Size(138, 24),
                Font = new Font("Segoe UI Semibold", 13F),
                ForeColor = Ink,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Label brandDetail = new Label
            {
                Text = "CONTROL CENTER",
                Location = new Point(72, 37),
                Size = new Size(138, 20),
                Font = new Font("Segoe UI Semibold", 8F),
                ForeColor = Primary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            IntelGlowLabel database = new IntelGlowLabel
            {
                Text = "Intelligence Database",
                Location = new Point(8, 70),
                Size = new Size(204, 24),
                Font = new Font("Segoe UI Semibold", 9.5F),
                ForeColor = Primary
            };
            brand.Controls.Add(cube);
            brand.Controls.Add(brandName);
            brand.Controls.Add(brandDetail);
            brand.Controls.Add(database);

            FlowLayoutPanel nav = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = false,
                BackColor = AppBackgroundDeep,
                Padding = new Padding(0, 8, 0, 0)
            };
            nav.Controls.Add(SidebarSectionLabel("WORKSPACE"));
            nav.Controls.Add(CreateSidebarButton("Overview", 0));
            nav.Controls.Add(SidebarSectionLabel("CONTROL"));
            nav.Controls.Add(CreateSidebarButton("Market intelligence", 1));
            nav.Controls.Add(CreateSidebarButton("Player & inventory", 2));
            nav.Controls.Add(CreateSidebarButton("Business operations", 3));
            nav.Controls.Add(CreateSidebarButton("Drug effects", 4));
            nav.Controls.Add(SidebarSectionLabel("SYSTEM"));
            nav.Controls.Add(CreateSidebarButton("Save & safety", 5));
            nav.Controls.Add(CreateSidebarButton("Updates", 6));
            nav.Controls.Add(CreateSidebarButton("Help center", 7));
            nav.Controls.Add(SidebarSectionLabel("POWER TOOLS"));
            nav.Controls.Add(CreateSidebarButton("Diagnostics", 8));

            Label build = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Text = ReleaseInfo.Label.ToUpperInvariant(),
                Font = new Font("Segoe UI Semibold", 8F),
                ForeColor = Faint,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };

            sidebar.Controls.Add(nav);
            sidebar.Controls.Add(build);
            sidebar.Controls.Add(brand);
            nav.BringToFront();
            return sidebar;
        }

        private static Label SidebarSectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                Width = 204,
                Height = 30,
                Margin = new Padding(8, 10, 0, 1),
                Font = new Font("Segoe UI Semibold", 7.5F),
                ForeColor = Faint,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private Button CreateSidebarButton(string text, int index)
        {
            Button button = new IntelButton
            {
                Text = text,
                UseMnemonic = false,
                Width = 204,
                Height = 42,
                Margin = new Padding(2, 2, 0, 2),
                Padding = new Padding(14, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                Font = UiFontSemibold,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Tag = index
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { navigation.SelectedIndex = index; };
            button.Resize += delegate { SetRoundedRegion(button, 10); };
            sidebarButtons.Add(button);
            return button;
        }

        private void UpdateWorkspaceSelection()
        {
            string[] titles = { "Overview", "Market intelligence", "Player & inventory", "Business operations", "Drug effects", "Save & safety", "Updates", "Help center", "Diagnostics" };
            string[] kickers = { "SYSTEM OVERVIEW", "LIVE ECONOMY", "PLAYER CONTROL", "OWNED INFRASTRUCTURE", "PRODUCT BEHAVIOR", "OFFLINE SAFETY", "RELEASE SYNC", "KNOWLEDGE BASE", "POWER TOOLS" };
            int selected = Math.Max(0, Math.Min(navigation.SelectedIndex, titles.Length - 1));
            workspaceTitle.Text = titles[selected];
            workspaceKicker.Text = kickers[selected];
            workspaceKicker.Visible = selected != 0;
            TableLayoutPanel titleStack = workspaceKicker.Parent as TableLayoutPanel;
            if (titleStack != null && titleStack.RowStyles.Count > 0)
                titleStack.RowStyles[0].Height = selected == 0 ? 0 : 17;
            for (int i = 0; i < sidebarButtons.Count; i++)
            {
                bool active = Convert.ToInt32(sidebarButtons[i].Tag, CultureInfo.InvariantCulture) == selected;
                sidebarButtons[i].BackColor = active ? PrimarySoft : AppBackgroundDeep;
                sidebarButtons[i].ForeColor = active ? PrimaryHover : Muted;
                sidebarButtons[i].FlatAppearance.MouseOverBackColor = active ? Selection : SurfaceStrong;
                sidebarButtons[i].FlatAppearance.MouseDownBackColor = Selection;
            }
        }

        private static void NormalizeInputHeights(Control root)
        {
            if (root == null)
                return;
            foreach (Control child in root.Controls)
            {
                if (child is ComboBox)
                {
                    ((ComboBox)child).IntegralHeight = false;
                    child.Height = 34;
                    child.Font = UiFont;
                    child.BackColor = InputSurface;
                    child.ForeColor = Ink;
                    ((ComboBox)child).FlatStyle = FlatStyle.Flat;
                }
                else if (child is NumericUpDown)
                {
                    child.Height = Math.Max(38, child.Height);
                    child.MinimumSize = new Size(Math.Max(96, child.MinimumSize.Width), 38);
                    child.Font = UiFont;
                    child.BackColor = InputSurface;
                    child.ForeColor = Ink;
                    ((NumericUpDown)child).BorderStyle = BorderStyle.None;
                }
                else if (child is CheckBox)
                {
                    child.Height = 34;
                    child.Font = UiFont;
                    child.BackColor = Color.Transparent;
                    child.ForeColor = Ink;
                }
                else if (child is TextBoxBase)
                {
                    child.BackColor = InputSurface;
                    child.ForeColor = Ink;
                    ((TextBoxBase)child).BorderStyle = BorderStyle.None;
                }
                else if (child is ListBox)
                {
                    child.BackColor = InputSurface;
                    child.ForeColor = Ink;
                    ((ListBox)child).BorderStyle = BorderStyle.None;
                }
                else if (child is TrackBar)
                {
                    child.BackColor = Surface;
                    child.ForeColor = Primary;
                }
                else if (child is DataGridView)
                {
                    ApplyGridTheme((DataGridView)child);
                }
                else if (child is Label)
                {
                    child.BackColor = Color.Transparent;
                    if (child.ForeColor == SystemColors.ControlText || child.ForeColor == Color.Black)
                        child.ForeColor = Ink;
                }
                else if (child is TableLayoutPanel || child is FlowLayoutPanel)
                {
                    child.BackColor = child.Parent is GroupBox || HasGroupParent(child) ? Surface : AppBackground;
                }
                else if (child is SplitContainer)
                {
                    SplitContainer split = (SplitContainer)child;
                    split.BackColor = Border;
                    split.Panel1.BackColor = AppBackground;
                    split.Panel2.BackColor = AppBackground;
                }
                NormalizeInputHeights(child);
            }
            TabControl tabs = root as TabControl;
            if (tabs != null)
            {
                foreach (TabPage page in tabs.TabPages)
                    NormalizeInputHeights(page);
            }
        }

        private static bool HasGroupParent(Control control)
        {
            Control current = control == null ? null : control.Parent;
            while (current != null)
            {
                if (current is GroupBox)
                    return true;
                if (current is TabPage)
                    return false;
                current = current.Parent;
            }
            return false;
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 112,
                BackColor = HeaderColor,
                Padding = new Padding(24, 10, 24, 10),
                ColumnCount = 2,
                RowCount = 2
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            header.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Border))
                    e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            TableLayoutPanel titleStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            titleStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
            titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            workspaceKicker.Dock = DockStyle.Fill;
            workspaceKicker.Font = new Font("Segoe UI Semibold", 7.5F);
            workspaceKicker.ForeColor = Primary;
            workspaceKicker.TextAlign = ContentAlignment.BottomLeft;
            workspaceTitle.Dock = DockStyle.Fill;
            workspaceTitle.Font = new Font("Segoe UI Semibold", 16F);
            workspaceTitle.ForeColor = Ink;
            workspaceTitle.TextAlign = ContentAlignment.TopLeft;
            workspaceTitle.AutoEllipsis = true;
            titleStack.Controls.Add(workspaceKicker, 0, 0);
            titleStack.Controls.Add(workspaceTitle, 0, 1);
            header.Controls.Add(titleStack, 0, 0);

            FlowLayoutPanel statusStrip = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0, 2, 0, 0),
                BackColor = HeaderColor
            };
            ConfigureHeaderStatus(gameStatus, "GAME CHECK");
            ConfigureHeaderStatus(bridgeStatus, "BRIDGE CHECK");
            ConfigureHeaderStatus(authorityStatus, "AUTH CHECK");
            statusStrip.Controls.Add(authorityStatus);
            statusStrip.Controls.Add(bridgeStatus);
            statusStrip.Controls.Add(gameStatus);
            header.Controls.Add(statusStrip, 1, 0);

            TableLayoutPanel saveRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 2, 8, 0) };
            saveRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            saveRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            saveRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            Label saveLabel = new Label { Text = "ACTIVE SAVE", ForeColor = Faint, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 7.5F) };
            saveRow.Controls.Add(saveLabel, 0, 0);
            saveSelector.Dock = DockStyle.Fill;
            saveSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            saveSelector.FlatStyle = FlatStyle.Flat;
            saveSelector.BackColor = InputSurface;
            saveSelector.ForeColor = Ink;
            saveSelector.SelectedIndexChanged += delegate { RefreshSelectedSaveUi(); };
            saveRow.Controls.Add(saveSelector, 1, 0);
            Button refresh = MakeButton("Refresh", false);
            refresh.Dock = DockStyle.Fill;
            refresh.Margin = new Padding(10, 0, 0, 0);
            refresh.Click += delegate { RefreshSaves(); };
            saveRow.Controls.Add(refresh, 2, 0);
            header.Controls.Add(saveRow, 0, 1);

            saveStatus.Dock = DockStyle.Fill;
            saveStatus.ForeColor = Muted;
            saveStatus.Font = UiFont;
            saveStatus.TextAlign = ContentAlignment.MiddleRight;
            saveStatus.AutoEllipsis = true;
            header.Controls.Add(saveStatus, 1, 1);
            return header;
        }

        private TabPage BuildHomePage()
        {
            TabPage page = NewPage("Overview");
            TableLayoutPanel layout = PageLayout(3);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 202));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 244));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);

            IntelNetworkPanel status = new IntelNetworkPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(26, 22, 26, 18) };
            TableLayoutPanel statusBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
            statusBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            statusBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            overviewHeadline.Dock = DockStyle.Fill;
            overviewHeadline.Font = new Font("Segoe UI Semibold", 19F);
            overviewHeadline.ForeColor = Ink;
            overviewHeadline.TextAlign = ContentAlignment.MiddleLeft;
            overviewHeadline.AutoEllipsis = true;
            overviewDetail.Dock = DockStyle.Fill;
            overviewDetail.ForeColor = Muted;
            overviewDetail.AutoEllipsis = true;
            overviewDetail.Padding = new Padding(0, 2, 0, 0);
            overviewDetail.MaximumSize = new Size(650, 0);
            statusBody.Controls.Add(overviewHeadline, 0, 0);
            statusBody.Controls.Add(overviewDetail, 0, 1);
            status.Controls.Add(statusBody);
            statusAccent.Dock = DockStyle.Left;
            statusAccent.Width = 4;
            statusAccent.BackColor = Warning;
            status.Controls.Add(statusAccent);
            layout.Controls.Add(status, 0, 0);

            GroupBox next = NewGroup("Recommended workspaces");
            TableLayoutPanel quick = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(12, 12, 12, 12), BackColor = Surface };
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            quick.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            quick.Controls.Add(MakeDashboardActionCard("Market intelligence", "Coordinate selling prices, fair value and customer affordability.", "Open market", 1, true), 0, 0);
            quick.Controls.Add(MakeDashboardActionCard("Player control", "Review inventory capacity and movement settings together.", "Open player", 2, true), 1, 0);
            quick.Controls.Add(MakeDashboardActionCard("Save protection", "Back up and validate the selected save before offline work.", "Open saves", 5, true), 2, 0);
            next.Controls.Add(quick);
            layout.Controls.Add(next, 0, 1);

            GroupBox recent = NewGroup("Command logs");
            ConfigureOperationLog(homeOperationLog);
            recent.Controls.Add(homeOperationLog);
            layout.Controls.Add(recent, 0, 2);
            return page;
        }

        private Panel MakeDashboardActionCard(string title, string description, string actionText, int pageIndex, bool primary)
        {
            IntelCardPanel card = new IntelCardPanel { Dock = DockStyle.Fill, Margin = new Padding(6), Padding = new Padding(16, 12, 16, 12) };
            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Surface };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            Label heading = new Label { Text = title, Dock = DockStyle.Fill, Font = CardTitleFont, ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft };
            Label detail = new Label { Text = description, Dock = DockStyle.Fill, Font = UiFont, ForeColor = Muted, AutoEllipsis = false, Padding = new Padding(0, 3, 0, 3) };
            Button action = MakeButton(actionText, primary);
            action.Dock = DockStyle.Fill;
            action.Margin = new Padding(0, 4, 0, 0);
            action.Click += delegate { navigation.SelectedIndex = pageIndex; };
            body.Controls.Add(heading, 0, 0);
            body.Controls.Add(detail, 0, 1);
            body.Controls.Add(action, 0, 2);
            card.Controls.Add(body);
            return card;
        }

        private TabPage BuildProductsPage()
        {
            TabPage page = NewPage("Products");
            TableLayoutPanel layout = PageLayout(3);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            page.Controls.Add(CreateScrollablePage(layout, 700));

            GroupBox workflow = NewGroup("Product Fair Value");
            TableLayoutPanel workflowBody = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8) };
            workflowBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            workflowBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label explanation = new Label
            {
                Text = "Keep customer value aligned with the current selling price, or review a custom fair-value plan before applying.",
                Dock = DockStyle.Fill,
                ForeColor = Muted
            };
            workflowBody.Controls.Add(explanation, 0, 0);
            // Keep this action row as one measured strip.  The previous wrapping
            // layout pushed Apply below the other actions at the minimum window
            // width, making it look detached from Preview.
            FlowLayoutPanel controls = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(0, 0, 0, 1)
            };
            controls.Controls.Add(FieldLabel("Products"));
            ConfigureDrugSelector(marketDrugSelector);
            marketDrugSelector.Width = 112;
            marketDrugSelector.SelectedIndexChanged += delegate
            {
                InvalidateMarketPreview();
                marketGrid.Rows.Clear();
                marketSummary.Text = "Product scope changed. Refresh values before creating a new preview.";
            };
            controls.Controls.Add(marketDrugSelector);
            controls.Controls.Add(FieldLabel("Plan"));
            marketModeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            marketModeSelector.Items.AddRange(new object[] { "Match selling price (recommended)", "Use absolute multiplier", "Edit individual fair values" });
            marketModeSelector.SelectedIndex = 0;
            marketModeSelector.Width = 238;
            marketModeSelector.SelectedIndexChanged += delegate
            {
                marketFactorInput.Enabled = marketModeSelector.SelectedIndex == 1;
                InvalidateMarketPreview();
                if (marketModeSelector.SelectedIndex == 2)
                    PrepareMarketManualTargets();
                else
                    ClearMarketPlannedDisplay();
                UpdateMarketEditMode();
            };
            controls.Controls.Add(marketModeSelector);
            marketFactorInput.DecimalPlaces = 2;
            marketFactorInput.Increment = 0.25m;
            marketFactorInput.Minimum = 0.10m;
            marketFactorInput.Maximum = PracticalMultiplierMaximum;
            marketFactorInput.Value = 2m;
            marketFactorInput.Width = 98;
            marketFactorInput.Enabled = false;
            marketFactorInput.ValueChanged += delegate { InvalidateMarketPreview(); };
            controls.Controls.Add(marketFactorInput);
            Button refresh = MakeButton("Refresh", false);
            refresh.Width = 105;
            refresh.Click += async delegate { await RefreshMarketValuesAsync(); };
            controls.Controls.Add(refresh);
            marketPreviewButton.Text = "Preview";
            StyleButton(marketPreviewButton, true);
            marketPreviewButton.Width = 105;
            marketPreviewButton.Click += async delegate { await PreviewMarketValuesAsync(); };
            controls.Controls.Add(marketPreviewButton);
            marketApplyButton.Text = "Apply";
            StyleButton(marketApplyButton, true);
            marketApplyButton.Width = 105;
            marketApplyButton.Enabled = false;
            marketApplyButton.Click += async delegate { await ApplyMarketPreviewAsync(); };
            controls.Controls.Add(marketApplyButton);
            workflowBody.Controls.Add(controls, 0, 1);
            workflow.Controls.Add(workflowBody);
            layout.Controls.Add(workflow, 0, 0);

            ConfigureMarketGrid();
            UpdateMarketEditMode();
            layout.Controls.Add(marketGrid, 0, 1);
            marketSummary.Dock = DockStyle.Fill;
            marketSummary.Padding = new Padding(12, 10, 12, 0);
            marketSummary.ForeColor = Muted;
            marketSummary.Text = "Refresh live values to compare selling price with customer fair-market value.";
            layout.Controls.Add(marketSummary, 0, 2);
            return page;
        }

        private TabPage BuildPricesLimitsPage()
        {
            TabPage page = NewPage("Prices & Limits");
            TableLayoutPanel layout = PageLayout(4);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            page.Controls.Add(CreateScrollablePage(layout, 870));

            GroupBox dealLimit = NewGroup("Maximum total for a deal");
            TableLayoutPanel dealBody = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8) };
            dealBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            dealBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            dealBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            dealBody.Controls.Add(new Label
            {
                Text = "The vanilla $9,999 ceiling applies to a total counteroffer or handover. Set a custom whole-dollar maximum up to the exact technical limit of $16,777,215.",
                Dock = DockStyle.Fill,
                ForeColor = Muted
            }, 0, 0);
            dealLimitState.Dock = DockStyle.Fill;
            dealLimitState.ForeColor = Muted;
            dealLimitState.Text = "Refresh limits to read the active deal ceiling and the separate unit-price bounds.";
            dealBody.Controls.Add(dealLimitState, 0, 1);

            FlowLayoutPanel dealControls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            dealLimitOverrideToggle.Text = "Use custom deal maximum";
            dealLimitOverrideToggle.AutoSize = true;
            dealLimitOverrideToggle.Padding = new Padding(0, 7, 4, 0);
            dealLimitOverrideToggle.CheckedChanged += delegate
            {
                dealLimitInput.Enabled = dealLimitOverrideToggle.Checked;
                InvalidateDealLimitPreview();
                dealLimitState.Text = dealLimitOverrideToggle.Checked
                    ? "Custom maximum selected. Preview before applying."
                    : "Game-default $9,999 selected. Preview before restoring.";
            };
            dealControls.Controls.Add(dealLimitOverrideToggle);
            dealControls.Controls.Add(FieldLabel("Maximum $"));
            dealLimitInput.DecimalPlaces = 0;
            dealLimitInput.ThousandsSeparator = true;
            dealLimitInput.Minimum = 9999m;
            dealLimitInput.Maximum = PracticalMoneyMaximum;
            dealLimitInput.Increment = 1000m;
            dealLimitInput.Value = 9999m;
            dealLimitInput.Width = 110;
            dealLimitInput.Enabled = false;
            dealLimitInput.ValueChanged += delegate { InvalidateDealLimitPreview(); };
            dealControls.Controls.Add(dealLimitInput);
            dealLimitRefreshButton.Text = "Refresh";
            StyleButton(dealLimitRefreshButton, false);
            dealLimitRefreshButton.Width = 115;
            dealLimitRefreshButton.Click += async delegate { await RefreshDealLimitAsync(); };
            dealControls.Controls.Add(dealLimitRefreshButton);
            dealLimitPreviewButton.Text = "Preview";
            StyleButton(dealLimitPreviewButton, true);
            dealLimitPreviewButton.Width = 115;
            dealLimitPreviewButton.Click += async delegate { await PreviewDealLimitAsync(); };
            dealControls.Controls.Add(dealLimitPreviewButton);
            dealLimitApplyButton.Text = "Apply";
            StyleButton(dealLimitApplyButton, true);
            dealLimitApplyButton.Width = 110;
            dealLimitApplyButton.Enabled = false;
            dealLimitApplyButton.Click += async delegate { await ApplyDealLimitPreviewAsync(); };
            dealControls.Controls.Add(dealLimitApplyButton);
            dealBody.Controls.Add(dealControls, 0, 2);
            dealLimit.Controls.Add(dealBody);
            layout.Controls.Add(dealLimit, 0, 0);

            GroupBox unitPrices = NewGroup("Product unit sell prices");
            TableLayoutPanel priceBody = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8) };
            priceBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            priceBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            priceBody.Controls.Add(new Label
            {
                Text = "The bridge removes the vanilla $999 unit-price cap for an eligible solo host. Whole-dollar prices can be set up to $16,777,215; sync fair value afterward.",
                Dock = DockStyle.Fill,
                ForeColor = Warning
            }, 0, 0);
            FlowLayoutPanel priceControls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            priceControls.Controls.Add(FieldLabel("Products"));
            ConfigureDrugSelector(sellDrugSelector);
            sellDrugSelector.Width = 120;
            sellDrugSelector.SelectedIndexChanged += delegate
            {
                InvalidateSellPreview();
                sellGrid.Rows.Clear();
                sellRows.Clear();
                sellSummary.Text = "Product scope changed. Refresh unit prices before previewing.";
            };
            priceControls.Controls.Add(sellDrugSelector);
            priceControls.Controls.Add(FieldLabel("Plan"));
            sellModeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            sellModeSelector.Items.AddRange(new object[] { "Scale current prices", "Edit individual prices" });
            sellModeSelector.SelectedIndex = 0;
            sellModeSelector.Width = 175;
            sellModeSelector.SelectedIndexChanged += delegate
            {
                sellFactorInput.Enabled = sellModeSelector.SelectedIndex == 0;
                InvalidateSellPreview();
                if (sellModeSelector.SelectedIndex == 1)
                    PrepareSellManualTargets();
                else
                    ClearSellPlannedDisplay();
                UpdateSellEditMode();
            };
            priceControls.Controls.Add(sellModeSelector);
            sellFactorInput.DecimalPlaces = 2;
            sellFactorInput.Minimum = 0.01m;
            sellFactorInput.Maximum = PracticalMultiplierMaximum;
            sellFactorInput.Increment = 0.25m;
            sellFactorInput.Value = 2m;
            sellFactorInput.Width = 110;
            sellFactorInput.ValueChanged += delegate { InvalidateSellPreview(); };
            priceControls.Controls.Add(sellFactorInput);
            sellRefreshButton.Text = "Refresh";
            StyleButton(sellRefreshButton, false);
            sellRefreshButton.Width = 115;
            sellRefreshButton.Click += async delegate { await RefreshSellPricesAsync(); };
            priceControls.Controls.Add(sellRefreshButton);
            sellPreviewButton.Text = "Preview";
            StyleButton(sellPreviewButton, true);
            sellPreviewButton.Width = 120;
            sellPreviewButton.Click += async delegate { await PreviewSellPricesAsync(); };
            priceControls.Controls.Add(sellPreviewButton);
            sellApplyButton.Text = "Apply";
            StyleButton(sellApplyButton, true);
            sellApplyButton.Width = 135;
            sellApplyButton.Enabled = false;
            sellApplyButton.Click += async delegate { await ApplySellPricePreviewAsync(); };
            priceControls.Controls.Add(sellApplyButton);
            priceBody.Controls.Add(priceControls, 0, 1);
            unitPrices.Controls.Add(priceBody);
            layout.Controls.Add(unitPrices, 0, 1);

            ConfigureSellGrid();
            UpdateSellEditMode();
            layout.Controls.Add(sellGrid, 0, 2);
            sellSummary.Dock = DockStyle.Fill;
            sellSummary.Padding = new Padding(12, 10, 12, 0);
            sellSummary.ForeColor = Muted;
            sellSummary.Text = "Refresh limits and prices. Deal totals and product unit prices are deliberately controlled separately.";
            layout.Controls.Add(sellSummary, 0, 3);
            return page;
        }

        private TabPage BuildCustomersPage()
        {
            TabPage page = NewPage("Customers");
            TableLayoutPanel layout = PageLayout(3);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            page.Controls.Add(layout);

            GroupBox workflow = NewGroup("Customer weekly allowances");
            TableLayoutPanel workflowBody = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8) };
            workflowBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            workflowBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label explanation = new Label
            {
                Text = "Scale customer budgets or edit planned values individually. Native preferences and relationship rules still apply.",
                Dock = DockStyle.Fill,
                ForeColor = Muted
            };
            workflowBody.Controls.Add(explanation, 0, 0);

            FlowLayoutPanel controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            controls.Controls.Add(FieldLabel("Scope"));
            customerScopeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            customerScopeSelector.Items.AddRange(new object[] { "Unlocked customers", "All customers" });
            customerScopeSelector.SelectedIndex = 0;
            customerScopeSelector.Width = 145;
            customerScopeSelector.SelectedIndexChanged += delegate
            {
                InvalidateCustomerPreview();
                customerGrid.Rows.Clear();
                customerRows.Clear();
                customerSummary.Text = "Customer scope changed. Refresh allowances before creating a new preview.";
            };
            controls.Controls.Add(customerScopeSelector);

            controls.Controls.Add(FieldLabel("Plan"));
            customerModeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
            customerModeSelector.Items.AddRange(new object[] { "Scale original allowances", "Edit individual allowances" });
            customerModeSelector.SelectedIndex = 0;
            customerModeSelector.Width = 205;
            customerModeSelector.SelectedIndexChanged += delegate
            {
                customerFactorInput.Enabled = customerModeSelector.SelectedIndex == 0;
                InvalidateCustomerPreview();
                if (customerModeSelector.SelectedIndex == 1)
                    PrepareCustomerManualTargets();
                else
                    ClearCustomerPlannedDisplay();
                UpdateCustomerEditMode();
            };
            controls.Controls.Add(customerModeSelector);

            customerFactorInput.DecimalPlaces = 2;
            customerFactorInput.Increment = 0.25m;
            customerFactorInput.Minimum = 0.10m;
            customerFactorInput.Maximum = PracticalMultiplierMaximum;
            customerFactorInput.Value = 2m;
            customerFactorInput.Width = 110;
            customerFactorInput.ValueChanged += delegate { InvalidateCustomerPreview(); };
            controls.Controls.Add(customerFactorInput);

            customerRefreshButton.Text = "Refresh allowances";
            StyleButton(customerRefreshButton, false);
            customerRefreshButton.Width = 140;
            customerRefreshButton.Click += async delegate { await RefreshCustomerAllowancesAsync(); };
            controls.Controls.Add(customerRefreshButton);

            customerPreviewButton.Text = "Preview plan";
            StyleButton(customerPreviewButton, true);
            customerPreviewButton.Width = 115;
            customerPreviewButton.Click += async delegate { await PreviewCustomerAllowancesAsync(); };
            controls.Controls.Add(customerPreviewButton);

            customerApplyButton.Text = "Apply";
            StyleButton(customerApplyButton, true);
            customerApplyButton.Width = 135;
            customerApplyButton.Enabled = false;
            customerApplyButton.Click += async delegate { await ApplyCustomerAllowancePreviewAsync(); };
            controls.Controls.Add(customerApplyButton);

            workflowBody.Controls.Add(controls, 0, 1);
            workflow.Controls.Add(workflowBody);
            layout.Controls.Add(workflow, 0, 0);

            ConfigureCustomerGrid();
            UpdateCustomerEditMode();
            layout.Controls.Add(customerGrid, 0, 1);
            customerSummary.Dock = DockStyle.Fill;
            customerSummary.Padding = new Padding(12, 10, 12, 0);
            customerSummary.ForeColor = Muted;
            customerSummary.Text = "Refresh live customer allowances. In manual mode, double-click only the planned minimum or maximum columns.";
            layout.Controls.Add(customerSummary, 0, 2);
            return page;
        }

        private TabPage BuildPropertiesPage()
        {
            TabPage page = NewPage("Properties");
            TableLayoutPanel layout = PageLayout(2);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);

            GroupBox acquire = NewGroup("Acquire a property or business");
            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(16, 12, 16, 12) };
            propertyGuidance.Text = "Acquiring only is supported. The live vanilla route is recommended because it triggers the game's normal ownership logic. Removing ownership remains intentionally unavailable.";
            propertyGuidance.Dock = DockStyle.Fill;
            propertyGuidance.ForeColor = Muted;
            body.Controls.Add(propertyGuidance, 0, 0);
            propertySelector.DropDownStyle = ComboBoxStyle.DropDownList;
            propertySelector.Dock = DockStyle.Top;
            propertySelector.SelectedIndexChanged += delegate { RefreshPropertyActions(); };
            body.Controls.Add(propertySelector, 0, 1);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            ownLiveButton.Text = "Buy (live)";
            StyleButton(ownLiveButton, true);
            ownLiveButton.Width = 210;
            ownLiveButton.Click += async delegate { await AcquirePropertyLiveAsync(); };
            ownOfflineButton.Text = "Buy (offline)";
            StyleButton(ownOfflineButton, false);
            ownOfflineButton.Width = 200;
            ownOfflineButton.Click += delegate { AcquirePropertyOffline(); };
            actions.Controls.Add(ownLiveButton);
            actions.Controls.Add(ownOfflineButton);
            body.Controls.Add(actions, 0, 2);
            Label note = new Label { Text = "Live requires a loaded solo-host save. Offline requires the game to be closed and reloads the save afterward.", Dock = DockStyle.Fill, ForeColor = Muted };
            body.Controls.Add(note, 0, 3);
            acquire.Controls.Add(body);
            layout.Controls.Add(acquire, 0, 0);

            GroupBox safety = NewGroup("Why un-own is blocked");
            Label text = new Label
            {
                Text = "Properties connect to employees, storage, quests, and business state. Clearing ownership can strand those references. The Control Center only follows the safe, forward ownership path.",
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                ForeColor = Muted
            };
            safety.Controls.Add(text);
            layout.Controls.Add(safety, 0, 1);
            return page;
        }

        private TabPage BuildSaveToolsPage()
        {
            TabPage page = NewPage("Save Tools");
            TableLayoutPanel layout = PageLayout(2);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);

            toolsModeNotice.Dock = DockStyle.Fill;
            toolsModeNotice.Padding = new Padding(16);
            toolsModeNotice.Font = new Font("Segoe UI Semibold", 10F);
            layout.Controls.Add(toolsModeNotice, 0, 0);

            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 14, 6, 6), FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true };
            tools.Controls.Add(MakeToolCard("Backup selected save", "Creates a complete timestamped copy of the selected slot.", "Back up", true, delegate { RunWithSelectedSave(s => saves.CreateBackup(s, "manual")); }));
            tools.Controls.Add(MakeToolCard("Validate JSON", "Parses every JSON file and reports any invalid save data.", "Check files", false, delegate { RunWithSelectedSave(saves.ValidateSave); }));
            consoleButton.Text = "Enable console";
            StyleButton(consoleButton, false);
            consoleButton.Width = 145;
            consoleButton.Click += delegate
            {
                if (Confirm("Enable the built-in console for the selected save? Reload is required."))
                    RunWithSelectedSave(saves.EnableConsoleOffline);
            };
            tools.Controls.Add(MakeToolCard("In-game console", "Enables Game.json console support. This is an offline save change.", consoleButton));
            layout.Controls.Add(tools, 0, 1);
            return page;
        }

        private TabPage BuildHelpPage()
        {
            TabPage page = NewPage("Help");
            TableLayoutPanel helpLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = AppBackground,
                Padding = new Padding(8)
            };
            helpLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            helpLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            helpLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(helpLayout);

            IntelCardPanel topicsCard = new IntelCardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), Padding = new Padding(12) };
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, BackColor = Surface };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.Controls.Add(new Label { Text = "Help topics", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 12F), ForeColor = Ink }, 0, 0);
            helpSearch.Dock = DockStyle.Fill;
            helpSearch.Margin = new Padding(0, 4, 0, 6);
            helpSearch.TextChanged += delegate { FilterHelpTopics(); };
            left.Controls.Add(helpSearch, 0, 1);
            helpTopicList.Dock = DockStyle.Fill;
            helpTopicList.SelectedIndexChanged += delegate { ShowSelectedHelpTopic(); };
            left.Controls.Add(helpTopicList, 0, 2);
            topicsCard.Controls.Add(left);
            helpLayout.Controls.Add(topicsCard, 0, 0);

            helpText.Dock = DockStyle.Fill;
            helpText.ReadOnly = true;
            helpText.BorderStyle = BorderStyle.None;
            helpText.BackColor = InputSurface;
            helpText.ForeColor = Ink;
            helpText.Font = new Font("Segoe UI", 10F);
            helpText.DetectUrls = false;
            IntelCardPanel articleCard = new IntelCardPanel { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0), Padding = new Padding(18) };
            articleCard.Controls.Add(helpText);
            helpLayout.Controls.Add(articleCard, 1, 0);
            FilterHelpTopics();
            return page;
        }

        private TabPage BuildAdvancedPage()
        {
            TabPage page = NewPage("Advanced");
            TableLayoutPanel layout = PageLayout(4);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 178));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
            // This editor needs a stable measured row: group caption + card
            // padding + 44 px warning + 38 px actions and their margins.
            // A percentage row collapsed below that total and clipped the
            // controls through the bottom border into Diagnostics.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            page.Controls.Add(layout);

            GroupBox command = NewGroup("Command console");
            TableLayoutPanel commandBody = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2, Padding = new Padding(12, 8, 12, 8) };
            commandBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            commandBody.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            commandBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            commandBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            commandBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            commandBody.Controls.Add(new Label { Text = "Use the Help tab for syntax and examples. GUI workflows are recommended for normal use.", Dock = DockStyle.Fill, ForeColor = Muted }, 0, 0);
            commandBody.SetColumnSpan(commandBody.GetControlFromPosition(0, 0), 2);
            commandInput.Dock = DockStyle.Fill;
            commandInput.Font = new Font("Consolas", 10F);
            commandInput.Text = "bridge status";
            commandInput.KeyDown += async delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ExecuteCommandAsync(); } };
            commandBody.Controls.Add(commandInput, 0, 1);
            Button run = MakeButton("Run", true);
            run.Dock = DockStyle.Fill;
            run.Margin = new Padding(8, 0, 0, 0);
            run.Click += async delegate { await ExecuteCommandAsync(); };
            commandBody.Controls.Add(run, 1, 1);
            Label commandHint = new Label { Text = "Examples: bridge status | bridge market Shrooms | bridge prices Shrooms | validate", Dock = DockStyle.Fill, ForeColor = Muted };
            commandBody.Controls.Add(commandHint, 0, 2);
            commandBody.SetColumnSpan(commandHint, 2);
            command.Controls.Add(commandBody);
            layout.Controls.Add(command, 0, 0);

            SplitContainer outputSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 600 };
            commandOutput.Multiline = true;
            commandOutput.ReadOnly = true;
            commandOutput.ScrollBars = ScrollBars.Both;
            commandOutput.Font = new Font("Consolas", 9F);
            commandOutput.Dock = DockStyle.Fill;
            operationLog.Multiline = true;
            operationLog.ReadOnly = true;
            operationLog.ScrollBars = ScrollBars.Vertical;
            operationLog.Font = new Font("Consolas", 9F);
            operationLog.Dock = DockStyle.Fill;
            outputSplit.Panel1.Controls.Add(WrapWithCaption("Command output", commandOutput));
            ConfigureOperationLog(operationLog);
            outputSplit.Panel2.Controls.Add(WrapWithCaption("Command logs", operationLog));
            layout.Controls.Add(outputSplit, 0, 1);

            GroupBox legacy = NewGroup("Advanced offline sell-price editor");
            TableLayoutPanel legacyBody = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 8), ColumnCount = 1, RowCount = 2 };
            legacyBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            legacyBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            Label warning = new Label
            {
                Text = "Offline unit prices support whole-dollar values up to $16,777,215. After a sell-only edit, use Market intelligence > Fair-value sync.",
                ForeColor = Danger,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            legacyBody.Controls.Add(warning, 0, 0);
            FlowLayoutPanel legacyControls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Surface, Padding = new Padding(0, 3, 0, 2) };
            ConfigureDrugSelector(legacyDrugSelector);
            legacyDrugSelector.Width = 120;
            legacyControls.Controls.Add(legacyDrugSelector);
            legacyFactorInput.DecimalPlaces = 2;
            legacyFactorInput.Minimum = 0.01m;
            legacyFactorInput.Maximum = PracticalMultiplierMaximum;
            legacyFactorInput.Value = 2m;
            legacyFactorInput.Width = 110;
            legacyControls.Controls.Add(legacyFactorInput);
            Button legacyPreview = MakeButton("Preview", false);
            legacyPreview.Width = 135;
            legacyPreview.Click += delegate { PreviewLegacyPrices(); };
            legacyControls.Controls.Add(legacyPreview);
            legacyApplyButton.Text = "Apply";
            StyleButton(legacyApplyButton, false);
            legacyApplyButton.Width = 125;
            legacyApplyButton.Click += delegate { ApplyLegacyPrices(); };
            legacyControls.Controls.Add(legacyApplyButton);
            legacyBody.Controls.Add(legacyControls, 0, 1);
            legacy.Controls.Add(legacyBody);
            layout.Controls.Add(legacy, 0, 2);

            GroupBox diagnosticGroup = NewGroup("Diagnostics and troubleshooting");
            TableLayoutPanel diagnosticLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 10, 8), ColumnCount = 2, RowCount = 2 };
            diagnosticLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            diagnosticLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            diagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            diagnosticLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel diagnosticActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Surface };
            diagnosticHealth.Text = "Health snapshot is not available yet.";
            diagnosticHealth.AutoEllipsis = true;
            diagnosticHealth.Width = 500;
            diagnosticHealth.ForeColor = Muted;
            diagnosticActions.Controls.Add(diagnosticHealth);
            diagnosticRefreshButton.Text = "Refresh health";
            StyleButton(diagnosticRefreshButton, false);
            diagnosticRefreshButton.Click += delegate { RefreshDiagnostics(); };
            diagnosticActions.Controls.Add(diagnosticRefreshButton);
            diagnosticCopyButton.Text = "Copy safe report";
            StyleButton(diagnosticCopyButton, false);
            diagnosticCopyButton.Click += delegate { CopyDiagnosticReport(); };
            diagnosticActions.Controls.Add(diagnosticCopyButton);
            diagnosticExportButton.Text = "Export report";
            StyleButton(diagnosticExportButton, false);
            diagnosticExportButton.Click += delegate { ExportDiagnosticReport(); };
            diagnosticActions.Controls.Add(diagnosticExportButton);
            diagnosticLayout.Controls.Add(diagnosticActions, 0, 0);
            diagnosticLayout.SetColumnSpan(diagnosticActions, 2);
            diagnosticIncidentList.Dock = DockStyle.Fill;
            diagnosticIncidentList.IntegralHeight = false;
            diagnosticIncidentList.SelectedIndexChanged += delegate { ShowSelectedDiagnostic(); };
            diagnosticLayout.Controls.Add(diagnosticIncidentList, 0, 1);
            diagnosticDetail.Dock = DockStyle.Fill;
            diagnosticDetail.ReadOnly = true;
            diagnosticDetail.BorderStyle = BorderStyle.None;
            diagnosticDetail.BackColor = InputSurface;
            diagnosticDetail.ForeColor = Ink;
            diagnosticDetail.Font = new Font("Segoe UI", 9F);
            diagnosticDetail.ScrollBars = RichTextBoxScrollBars.Vertical;
            diagnosticLayout.Controls.Add(diagnosticDetail, 1, 1);
            diagnosticGroup.Controls.Add(diagnosticLayout);
            layout.Controls.Add(diagnosticGroup, 0, 3);
            return page;
        }

        private async Task RefreshRuntimeStatusAsync()
        {
            if (statusPollBusy)
                return;
            statusPollBusy = true;
            try
            {
                bool running = environment.IsGameRunning();
                OperationResult status = null;
                if (running)
                    status = await bridge.InvokeAsync("system.status", new Dictionary<string, object>(), true);
                RefreshRuntimeLabels(running, status);
                RefreshDiagnostics(status);
                await OfferCompatibilityModeAsync(running, status);
            }
            finally
            {
                statusPollBusy = false;
            }
        }

        private void RefreshDiagnostics(OperationResult status = null)
        {
            try
            {
                bool running = false;
                try { running = environment.IsGameRunning(); } catch { }
                bool bridgeReady = status != null ? status.Success : bridgeConnected;
                bool saveReady = SelectedSave != null;
                string bridgeSummary = status == null ? (bridgeReady ? "connected" : "not connected") : (status.Success ? "ready" : (status.Message ?? "unavailable"));
                diagnostics.SetHealth(DiagnosticHealthSnapshot.Capture(environment, bridgeReady, saveReady, bridgeSummary));
                DiagnosticHealthSnapshot health = diagnostics.HealthSnapshot;
                diagnosticHealth.Text = string.Format("Game {0} • Bridge {1} • Save {2} • {3} incidents", running ? "running" : "stopped", bridgeReady ? "ready" : "offline", saveReady ? "selected" : "not selected", diagnostics.GetIncidents().Count);
                diagnosticDisplayedIncidents.Clear();
                diagnosticDisplayedIncidents.AddRange(diagnostics.GetIncidents());
                diagnosticIncidentList.BeginUpdate();
                diagnosticIncidentList.Items.Clear();
                foreach (DiagnosticIncident incident in diagnosticDisplayedIncidents)
                    diagnosticIncidentList.Items.Add(string.Format("#{0} {1} [{2}] {3}", incident.Sequence, incident.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"), incident.Severity, incident.UserMessage));
                diagnosticIncidentList.EndUpdate();
                if (diagnosticIncidentList.Items.Count > 0) diagnosticIncidentList.SelectedIndex = 0;
                else diagnosticDetail.Text = health == null ? "No incidents have been captured." : health.ToReportText() + "\r\nNo incidents have been captured.";
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "diagnostics.refresh", DiagnosticCategory.Ui, DiagnosticSeverity.Warning, "The diagnostics panel could not refresh completely.", null);
            }
        }

        private void ShowSelectedDiagnostic()
        {
            int index = diagnosticIncidentList.SelectedIndex;
            if (index < 0 || index >= diagnosticDisplayedIncidents.Count) return;
            DiagnosticIncident incident = diagnosticDisplayedIncidents[index];
            DiagnosticCatalogEntry article = DiagnosticCatalog.Match(incident);
            StringBuilder text = new StringBuilder();
            text.AppendLine(incident.Summary);
            text.AppendLine("Incident: " + incident.CorrelationId + "  |  " + incident.TimestampUtc.ToLocalTime().ToString("G"));
            text.AppendLine();
            text.AppendLine("Why this may have happened");
            text.AppendLine(incident.Reasoning);
            text.AppendLine();
            text.AppendLine("Evidence");
            text.AppendLine(string.IsNullOrEmpty(incident.Evidence) ? "No additional evidence was supplied." : incident.Evidence);
            if (!string.IsNullOrEmpty(incident.TechnicalDetails)) { text.AppendLine(); text.AppendLine("Technical details"); text.AppendLine(incident.TechnicalDetails); }
            text.AppendLine();
            text.AppendLine("Next actions");
            text.AppendLine(incident.NextActions);
            if (article != null)
            {
                text.AppendLine();
                text.AppendLine("Troubleshooting guide: " + article.Title);
                text.AppendLine(article.Symptoms);
                text.AppendLine("Inspect: " + article.Evidence);
            }
            diagnosticDetail.Text = text.ToString();
            diagnosticDetail.SelectionStart = 0;
            diagnosticDetail.SelectionLength = 0;
        }

        private void CopyDiagnosticReport()
        {
            try { Clipboard.SetText(diagnostics.CreateSafeReport()); }
            catch (Exception ex) { DiagnosticsService.Record(ex, "diagnostics.copy", DiagnosticCategory.Ui, DiagnosticSeverity.Warning, "The safe diagnostic report could not be copied to the clipboard.", null); RefreshDiagnostics(); }
        }

        private void ExportDiagnosticReport()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt|All files (*.*)|*.*", FileName = "ScheduleI-ControlCenter-diagnostics.txt", Title = "Export safe diagnostic report" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string error;
                if (!diagnostics.ExportSafeReport(dialog.FileName, out error)) MessageBox.Show(this, "Could not export the report: " + error, "Diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task OfferCompatibilityModeAsync(bool running, OperationResult status)
        {
            if (!running || status == null || !status.Success)
            {
                compatibilityPromptShown = false;
                return;
            }

            bool reviewedBuild = JsonUtil.GetBool(status.Data, "knownBuild", false);
            bool compatibilityEnabled = JsonUtil.GetBool(status.Data, "compatibilityModeEnabled", false);
            bool diagnosticsPassed = JsonUtil.GetBool(status.Data, "compatibilityDiagnosticsPassed", false);
            bool compatibilityAvailable = JsonUtil.GetBool(status.Data, "compatibilityModeAvailable", false);
            if (reviewedBuild || compatibilityEnabled || !compatibilityAvailable || !diagnosticsPassed || compatibilityPromptShown)
                return;

            compatibilityPromptShown = true;
            string gameVersion = JsonUtil.GetString(status.Data, "gameVersion", "unknown");
            string expectedVersion = JsonUtil.GetString(status.Data, "expectedGameBuild", "the reviewed build");
            DialogResult choice = MessageBox.Show(
                this,
                "Schedule I " + gameVersion + " is not the bridge's reviewed game version (expected build " + expectedVersion + ").\r\n\r\n"
                    + "The bridge's compatibility diagnostics passed, but compatibility mode is not guaranteed for an unreviewed update. Continue only if you accept that risk.\r\n\r\n"
                    + "Yes enables the live bridge patches for this session. No keeps them disabled.",
                "Unreviewed game build",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes)
                return;

            OperationResult enabled = await bridge.InvokeAsync(
                "system.compatibility.enable",
                new Dictionary<string, object> { { "confirm", true } },
                false);
            ShowResult(enabled);
            if (!enabled.Success)
                return;

            OperationResult refreshed = await bridge.InvokeAsync("system.status", new Dictionary<string, object>(), true);
            RefreshRuntimeLabels(true, refreshed);
        }

        private void RefreshRuntimeLabels(bool running, OperationResult status)
        {
            gameStatus.Text = running ? "GAME RUNNING" : "GAME STOPPED";
            gameStatus.ForeColor = running ? Success : Warning;
            bridgeConnected = status != null && status.Success;
            bridgeStatus.Text = bridgeConnected ? "BRIDGE LIVE" : "BRIDGE DOWN";
            bridgeStatus.ForeColor = bridgeConnected ? Success : Faint;

            soloHost = false;
            dealLimitCapability = false;
            dealLimitStatusKnown = false;
            currentBridgeRevision = 0;
            loadedSavePath = string.Empty;
            bool saveReady = false;
            bool reviewedBuild = false;
            bool compatibilityModeEnabled = false;
            bool operationalBuild = false;
            if (bridgeConnected)
            {
                reviewedBuild = JsonUtil.GetBool(status.Data, "knownBuild", false);
                compatibilityModeEnabled = JsonUtil.GetBool(status.Data, "compatibilityModeEnabled", false);
                operationalBuild = reviewedBuild || compatibilityModeEnabled;
                soloHost = operationalBuild && JsonUtil.GetBool(status.Data, "isSoloHost", false);
                saveReady = JsonUtil.GetBool(status.Data, "saveReady", false);
                loadedSavePath = JsonUtil.GetString(status.Data, "savePath", string.Empty);
                currentBridgeRevision = status.Revision;
                object capabilities;
                if (status.Data != null && status.Data.TryGetValue("capabilities", out capabilities))
                {
                    foreach (object capability in JsonUtil.AsItems(capabilities))
                    {
                        if (string.Equals(Convert.ToString(capability, CultureInfo.InvariantCulture), "sale.dealLimit.get", StringComparison.Ordinal))
                        {
                            dealLimitCapability = true;
                            break;
                        }
                    }
                }
                // v0.4+ bridges expose these fields on every status response, even when
                // the deal-limit patch is temporarily unavailable. That metadata proves
                // the protocol knows the feature; the capability list proves it is ready.
                dealLimitStatusKnown = HasAnyStatusField(status.Data,
                    "sellPriceLimitPatchActive",
                    "sellPriceLimitPersistenceReady",
                    "configuredMaxDealTotal",
                    "reviewedDefaultMaxDealTotal");
            }
            authorityStatus.Text = soloHost ? "SOLO HOST" : "NO AUTHORITY";
            authorityStatus.ForeColor = soloHost ? Success : Faint;

            SaveDescriptor selected = SelectedSave;
            saveStatus.Text = selected == null ? "No save selected" : string.Format("{0}  •  console {1}", selected.SlotName, selected.ConsoleEnabled ? "enabled" : "disabled");
            if (!running)
            {
                overviewHeadline.Text = "Not Ready";
                overviewDetail.Text = "Open Diagnostics to review live-control readiness.";
                statusAccent.BackColor = Warning;
            }
            else if (!bridgeConnected)
            {
                overviewHeadline.Text = "Not Ready";
                overviewDetail.Text = "Open Diagnostics to review the unavailable live bridge.";
                statusAccent.BackColor = Danger;
            }
            else if (!operationalBuild)
            {
                overviewHeadline.Text = "Not Ready";
                overviewDetail.Text = "Open Diagnostics to review the game-build compatibility gate.";
                statusAccent.BackColor = Warning;
            }
            else if (!saveReady || !soloHost)
            {
                overviewHeadline.Text = "Not Ready";
                overviewDetail.Text = "Open Diagnostics to review the save and authority gates.";
                statusAccent.BackColor = Warning;
            }
            else
            {
                overviewHeadline.Text = "Ready";
                overviewDetail.Text = "Live-control readiness checks passed.";
                statusAccent.BackColor = Success;
            }

            bool gameStopped = !running;
            consoleButton.Enabled = gameStopped;
            ownOfflineButton.Enabled = gameStopped && SelectedSave != null;
            legacyApplyButton.Enabled = gameStopped && SelectedSave != null;
            toolsModeNotice.Text = gameStopped
                ? "OFFLINE MODE AVAILABLE - save-writing tools can run and create backups."
                : "LIVE MODE - offline save writes are blocked while Schedule I is running.";
            toolsModeNotice.ForeColor = gameStopped ? Success : Warning;
            RefreshPropertyActions();
            marketPreviewButton.Enabled = !marketBusy && bridgeConnected && soloHost;
            dealLimitRefreshButton.Enabled = !dealLimitBusy && bridgeConnected && soloHost && dealLimitCapability;
            dealLimitPreviewButton.Enabled = !dealLimitBusy && bridgeConnected && soloHost && dealLimitCapability;
            dealLimitApplyButton.Enabled = !dealLimitBusy && soloHost && dealLimitCapability && dealLimitPreviewId.Length > 0;
            sellRefreshButton.Enabled = !sellBusy && bridgeConnected && soloHost;
            sellPreviewButton.Enabled = !sellBusy && bridgeConnected && soloHost;
            sellApplyButton.Enabled = !sellBusy && soloHost && sellPreviewId.Length > 0;
            customerRefreshButton.Enabled = !customerBusy && bridgeConnected && soloHost;
            customerPreviewButton.Enabled = !customerBusy && bridgeConnected && soloHost;
            customerApplyButton.Enabled = !customerBusy && soloHost && customerPreviewId.Length > 0;
            SetPlayerBusy(playerBusy, null);
            if (!dealLimitBusy)
            {
                if (!bridgeConnected)
                    dealLimitState.Text = "Deal-limit status is unavailable while the bridge is offline.";
                else if (!soloHost)
                    dealLimitState.Text = "Deal-limit controls become available for a loaded solo-host save.";
                else if (!dealLimitStatusKnown)
                    dealLimitState.Text = "The running bridge does not advertise the Prices & Limits capability. Install the current bridge and restart Schedule I.";
                else if (!dealLimitCapability)
                    dealLimitState.Text = "Deal-limit controls are currently unavailable; open Diagnostics to review bridge readiness.";
            }
            if (!soloHost)
            {
                InvalidateMarketPreview();
                InvalidateDealLimitPreview();
                InvalidateSellPreview();
                InvalidateCustomerPreview();
                InvalidatePlayerPreview();
            }
        }

        private async Task RefreshMarketValuesAsync()
        {
            SetMarketBusy(true, "Reading live selling prices and fair-market values...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("product.market.list", MarketFilterArguments(), true);
                ShowResult(result);
                if (!result.Success)
                    return;
                DisplayMarketList(result.Data);
                currentBridgeRevision = result.Revision;
            }
            finally
            {
                SetMarketBusy(false, null);
            }
        }

        private async Task PreviewMarketValuesAsync()
        {
            InvalidateMarketPreview();
            SetMarketBusy(true, "Building a revision-checked fair-market preview...");
            try
            {
                Dictionary<string, object> args;
                if (marketModeSelector.SelectedIndex == 0)
                {
                    args = MarketFilterArguments();
                    args["mode"] = "matchSellPrice";
                }
                else if (marketModeSelector.SelectedIndex == 1)
                {
                    args = MarketFilterArguments();
                    args["mode"] = "absoluteFactor";
                    args["factor"] = marketFactorInput.Value;
                }
                else
                {
                    List<Dictionary<string, object>> targets;
                    string validationError;
                    if (!TryBuildMarketExplicitTargets(out targets, out validationError))
                    {
                        ShowResult(OperationResult.Fail(validationError));
                        marketSummary.Text = validationError;
                        return;
                    }
                    args = new Dictionary<string, object>
                    {
                        { "mode", "explicitValues" },
                        { "targets", targets }
                    };
                }

                OperationResult result = await bridge.InvokeAsync("product.market.previewSync", args, true);
                ShowResult(result);
                if (!result.Success)
                    return;
                marketPreviewId = JsonUtil.GetString(result.Data, "previewId", string.Empty);
                marketPreviewRevision = JsonUtil.GetLong(result.Data, "expectedRevision", result.Revision);
                marketPreviewConfigRevision = JsonUtil.GetLong(result.Data, "expectedConfigRevision", 0);
                DisplayMarketPreview(result.Data);
                marketApplyButton.Enabled = soloHost && marketPreviewId.Length > 0;
            }
            finally
            {
                SetMarketBusy(false, null);
            }
        }

        private async Task ApplyMarketPreviewAsync()
        {
            if (marketPreviewId.Length == 0)
                return;
            if (!Confirm("Apply this fair-market preview to the loaded save profile? The bridge will recheck every value, verify customer valuation, and persist the exact product overrides."))
                return;

            SetMarketBusy(true, "Applying, persisting, and verifying fair-market values...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("product.market.applyPreview", new Dictionary<string, object>
                {
                    { "previewId", marketPreviewId },
                    { "expectedRevision", marketPreviewRevision },
                    { "expectedConfigRevision", marketPreviewConfigRevision }
                }, false);
                ShowResult(result);
                InvalidateMarketPreview();
                if (result.Success)
                {
                    await RefreshMarketValuesAsync();
                }
            }
            finally
            {
                SetMarketBusy(false, null);
            }
        }

        private void DisplayMarketList(Dictionary<string, object> data)
        {
            marketGrid.Rows.Clear();
            int aligned = 0;
            object products;
            if (data == null || !data.TryGetValue("products", out products))
                return;
            foreach (object item in JsonUtil.AsItems(products))
            {
                Dictionary<string, object> row = JsonUtil.AsObject(item);
                if (row == null) continue;
                MarketProductRow model = new MarketProductRow
                {
                    ProductId = JsonUtil.GetString(row, "productId", string.Empty),
                    Name = JsonUtil.GetString(row, "name", string.Empty),
                    DrugType = JsonUtil.GetString(row, "drugType", string.Empty),
                    SellPrice = JsonUtil.GetDecimal(row, "sellPrice", 0),
                    VanillaMarketValue = JsonUtil.GetDecimal(row, "vanillaMarketValue", 0),
                    EffectiveMarketValue = JsonUtil.GetDecimal(row, "effectiveMarketValue", 0),
                    Factor = JsonUtil.GetDecimal(row, "factor", 1),
                    ValueProposition = JsonUtil.GetDecimal(row, "valueProposition", 0),
                    Aligned = JsonUtil.GetBool(row, "alignedWithSellPrice", false)
                };
                model.PlannedMarketValue = model.EffectiveMarketValue;
                if (model.Aligned) aligned++;
                int index = marketGrid.Rows.Add(
                    FriendlyProductName(model),
                    model.SellPrice,
                    model.EffectiveMarketValue,
                    marketModeSelector.SelectedIndex == 2 ? (object)model.PlannedMarketValue : string.Empty,
                    model.ValueProposition.ToString("0.00"),
                    model.Aligned ? "Aligned" : "Needs sync");
                marketGrid.Rows[index].Tag = model;
            }
            UpdateMarketEditMode();
            int count = JsonUtil.GetInt(data, "count", marketGrid.Rows.Count);
            marketSummary.Text = string.Format("{0} products loaded. {1} aligned; {2} need fair-market synchronization. Customer value of 1.00 restores the original price/value balance.", count, aligned, Math.Max(0, count - aligned));
        }

        private void DisplayMarketPreview(Dictionary<string, object> data)
        {
            marketGrid.Rows.Clear();
            object changes;
            if (data == null || !data.TryGetValue("changes", out changes))
                return;
            int changed = 0;
            foreach (object item in JsonUtil.AsItems(changes))
            {
                Dictionary<string, object> row = JsonUtil.AsObject(item);
                if (row == null) continue;
                decimal current = JsonUtil.GetDecimal(row, "expectedCurrentMarketValue", 0);
                decimal planned = JsonUtil.GetDecimal(row, "newMarketValue", 0);
                bool differs = Math.Abs(current - planned) > 0.001m;
                if (differs) changed++;
                MarketProductRow model = new MarketProductRow
                {
                    ProductId = JsonUtil.GetString(row, "productId", string.Empty),
                    Name = JsonUtil.GetString(row, "name", string.Empty),
                    DrugType = JsonUtil.GetString(row, "drugType", string.Empty),
                    SellPrice = JsonUtil.GetDecimal(row, "expectedSellPrice", 0),
                    VanillaMarketValue = JsonUtil.GetDecimal(row, "expectedVanillaMarketValue", 0),
                    EffectiveMarketValue = current,
                    PlannedMarketValue = planned,
                    Factor = JsonUtil.GetDecimal(row, "newFactor", 1),
                    ValueProposition = JsonUtil.GetDecimal(row, "plannedValueProposition", 0),
                    Aligned = !differs
                };
                int index = marketGrid.Rows.Add(
                    FriendlyProductName(model),
                    model.SellPrice,
                    model.EffectiveMarketValue,
                    model.PlannedMarketValue,
                    model.ValueProposition.ToString("0.00"),
                    differs ? "Ready to sync" : "Already aligned");
                marketGrid.Rows[index].Tag = model;
            }
            UpdateMarketEditMode();
            marketSummary.Text = string.Format("Preview ready for {0} products; {1} fair-market values will change. Preview expires in 60 seconds.", marketGrid.Rows.Count, changed);
        }

        private Dictionary<string, object> MarketFilterArguments()
        {
            return new Dictionary<string, object> { { "drugType", Convert.ToString(marketDrugSelector.SelectedItem, CultureInfo.InvariantCulture) } };
        }

        private async Task RefreshDealLimitAsync()
        {
            InvalidateDealLimitPreview();
            SetDealLimitBusy(true, "Reading the live deal and unit-price limits...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("sale.dealLimit.get", new Dictionary<string, object>(), true);
                ShowResult(result);
                if (result.Success)
                {
                    DisplayDealLimitStatus(result.Data);
                    currentBridgeRevision = result.Revision;
                }
            }
            finally
            {
                SetDealLimitBusy(false, null);
            }
        }

        private async Task PreviewDealLimitAsync()
        {
            InvalidateDealLimitPreview();
            SetDealLimitBusy(true, "Building a revision-checked maximum-deal preview...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("sale.dealLimit.preview", new Dictionary<string, object>
                {
                    { "enabled", dealLimitOverrideToggle.Checked },
                    { "maxDealTotal", decimal.ToInt32(dealLimitInput.Value) }
                }, true);
                ShowResult(result);
                if (!result.Success)
                    return;
                dealLimitPreviewId = JsonUtil.GetString(result.Data, "previewId", string.Empty);
                dealLimitPreviewRevision = JsonUtil.GetLong(result.Data, "expectedRevision", result.Revision);
                dealLimitPreviewConfigRevision = JsonUtil.GetLong(result.Data, "expectedConfigRevision", 0);
                int next = JsonUtil.GetInt(result.Data, "newMaxDealTotal", 9999);
                bool enabled = JsonUtil.GetBool(result.Data, "newOverrideEnabled", false);
                dealLimitState.Text = enabled
                    ? string.Format(CultureInfo.CurrentCulture, "Preview ready: raise both deal-entry paths to ${0:N0}. This does not change existing product prices.", next)
                    : "Preview ready: restore the native $9,999 deal maximum.";
                dealLimitApplyButton.Enabled = soloHost && dealLimitPreviewId.Length > 0;
            }
            finally
            {
                SetDealLimitBusy(false, null);
            }
        }

        private async Task ApplyDealLimitPreviewAsync()
        {
            if (dealLimitPreviewId.Length == 0)
                return;
            string question = dealLimitOverrideToggle.Checked
                ? string.Format(CultureInfo.CurrentCulture, "Apply and persist a ${0:N0} maximum total for counteroffers and handovers? Product unit-price and customer-affordability limits remain separate.", dealLimitInput.Value)
                : "Restore and persist the native $9,999 maximum total for counteroffers and handovers?";
            if (!Confirm(question))
                return;

            SetDealLimitBusy(true, "Applying, persisting, and verifying the deal-total maximum...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("sale.dealLimit.applyPreview", new Dictionary<string, object>
                {
                    { "previewId", dealLimitPreviewId },
                    { "expectedRevision", dealLimitPreviewRevision },
                    { "expectedConfigRevision", dealLimitPreviewConfigRevision }
                }, false);
                ShowResult(result);
                InvalidateDealLimitPreview();
                if (result.Success)
                {
                    DisplayDealLimitStatus(result.Data);
                }
            }
            finally
            {
                SetDealLimitBusy(false, null);
            }
        }

        private void DisplayDealLimitStatus(Dictionary<string, object> data)
        {
            bool enabled = JsonUtil.GetBool(data, "overrideEnabled", false);
            int configured = JsonUtil.GetInt(data, "configuredMaxDealTotal", 9999);
            decimal effective = JsonUtil.GetDecimal(data, "effectiveMaxDealTotal", enabled ? configured : 9999);
            activeUnitPriceMin = JsonUtil.GetDecimal(data, "unitPriceMin", 1);
            activeUnitPriceMax = JsonUtil.GetDecimal(data, "unitPriceMax", 999);
            dealLimitOverrideToggle.Checked = enabled;
            decimal bounded = Math.Max(dealLimitInput.Minimum, Math.Min(dealLimitInput.Maximum, configured));
            dealLimitInput.Value = bounded;
            dealLimitInput.Enabled = enabled;
            dealLimitState.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Effective deal maximum: ${0:N0} ({1}). Product unit-price range: ${2:N0}-${3:N0}. Patch: {4}; persistence: {5}.",
                effective,
                enabled ? "custom" : "game default",
                activeUnitPriceMin,
                activeUnitPriceMax,
                JsonUtil.GetBool(data, "patchActive", false) ? "ready" : "unavailable",
                JsonUtil.GetBool(data, "persistenceReady", false) ? "ready" : "unavailable");
        }

        private async Task RefreshSellPricesAsync()
        {
            InvalidateSellPreview();
            SetSellBusy(true, "Reading live product unit prices...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("product.price.list", SellFilterArguments(), true);
                ShowResult(result);
                if (!result.Success)
                    return;
                DisplaySellPriceList(result.Data);
                currentBridgeRevision = result.Revision;
            }
            finally
            {
                SetSellBusy(false, null);
            }
        }

        private async Task PreviewSellPricesAsync()
        {
            InvalidateSellPreview();
            SetSellBusy(true, "Building a revision-checked unit-price preview...");
            try
            {
                Dictionary<string, object> args;
                if (sellModeSelector.SelectedIndex == 0)
                {
                    args = SellFilterArguments();
                    args["mode"] = "currentFactor";
                    args["factor"] = sellFactorInput.Value;
                }
                else
                {
                    List<Dictionary<string, object>> targets;
                    string validationError;
                    if (!TryBuildSellExplicitTargets(out targets, out validationError))
                    {
                        ShowResult(OperationResult.Fail(validationError));
                        sellSummary.Text = validationError;
                        return;
                    }
                    args = new Dictionary<string, object>
                    {
                        { "mode", "explicitValues" },
                        { "targets", targets }
                    };
                }

                OperationResult result = await bridge.InvokeAsync("product.price.previewScale", args, true);
                ShowResult(result);
                if (!result.Success)
                    return;
                sellPreviewId = JsonUtil.GetString(result.Data, "previewId", string.Empty);
                sellPreviewRevision = JsonUtil.GetLong(result.Data, "expectedRevision", result.Revision);
                DisplaySellPricePreview(result.Data);
                sellApplyButton.Enabled = soloHost && sellPreviewId.Length > 0;
            }
            finally
            {
                SetSellBusy(false, null);
            }
        }

        private async Task ApplySellPricePreviewAsync()
        {
            if (sellPreviewId.Length == 0)
                return;
            if (!Confirm("Apply these product UNIT prices through the game's live price path? The Control Center will verify readback and start an in-game save. Fair-market values are not changed; use Products afterward to keep customer value aligned."))
                return;

            Dictionary<string, decimal> expected = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in sellGrid.Rows)
            {
                SellPriceProductRow model = row.Tag as SellPriceProductRow;
                if (model != null && model.ProductId.Length > 0)
                    expected[model.ProductId] = model.PlannedPrice;
            }

            SetSellBusy(true, "Applying and verifying product unit prices...");
            try
            {
                OperationResult apply = await bridge.InvokeAsync("product.price.applyPreview", new Dictionary<string, object>
                {
                    { "previewId", sellPreviewId },
                    { "expectedRevision", sellPreviewRevision }
                }, false);
                ShowResult(apply);
                InvalidateSellPreview();
                if (!apply.Success)
                    return;

                await Task.Delay(500);
                OperationResult readback = await bridge.InvokeAsync("product.price.list", SellFilterArguments(), true);
                ShowResult(readback);
                if (!readback.Success)
                {
                    sellSummary.Text = "Prices were submitted, but readback failed. Refresh before making another change.";
                    return;
                }
                DisplaySellPriceList(readback.Data);
                bool verified = true;
                foreach (KeyValuePair<string, decimal> pair in expected)
                {
                    SellPriceProductRow actual;
                    if (!sellRows.TryGetValue(pair.Key, out actual) || Math.Abs(actual.CurrentPrice - pair.Value) > 0.001m)
                    {
                        verified = false;
                        break;
                    }
                }
                if (!verified)
                {
                    sellSummary.Text = "Price submission completed, but readback did not match the preview. Refresh and inspect before retrying.";
                    return;
                }

                OperationResult save = await bridge.InvokeAsync("game.save", new Dictionary<string, object>(), false);
                ShowResult(save);
                sellSummary.Text = save.Success
                    ? "Unit prices were applied, read back, and an in-game save was started. Open Products to align fair-market values."
                    : "Unit prices were applied and read back, but the in-game save did not start; save manually before exiting.";
            }
            finally
            {
                SetSellBusy(false, null);
            }
        }

        private void DisplaySellPriceList(Dictionary<string, object> data)
        {
            sellGrid.Rows.Clear();
            sellRows.Clear();
            activeUnitPriceMin = JsonUtil.GetDecimal(data, "minPrice", activeUnitPriceMin);
            activeUnitPriceMax = JsonUtil.GetDecimal(data, "maxPrice", activeUnitPriceMax);
            object products;
            if (data == null || !data.TryGetValue("products", out products))
                return;

            int aligned = 0;
            foreach (object item in JsonUtil.AsItems(products))
            {
                Dictionary<string, object> row = JsonUtil.AsObject(item);
                if (row == null) continue;
                SellPriceProductRow model = new SellPriceProductRow
                {
                    ProductId = JsonUtil.GetString(row, "productId", string.Empty),
                    Name = JsonUtil.GetString(row, "name", string.Empty),
                    DrugType = JsonUtil.GetString(row, "drugType", string.Empty),
                    CurrentPrice = JsonUtil.GetDecimal(row, "price", 0),
                    FairMarketValue = JsonUtil.GetDecimal(row, "fairMarketValue", 0),
                    ValueProposition = JsonUtil.GetDecimal(row, "valueProposition", 0),
                    Aligned = JsonUtil.GetBool(row, "alignedWithFairMarket", false)
                };
                model.PlannedPrice = model.CurrentPrice;
                if (model.Aligned) aligned++;
                if (model.ProductId.Length > 0)
                    sellRows[model.ProductId] = model;
                int index = sellGrid.Rows.Add(
                    FriendlySellProductName(model),
                    model.CurrentPrice,
                    sellModeSelector.SelectedIndex == 1 ? (object)model.PlannedPrice : string.Empty,
                    model.FairMarketValue,
                    model.ValueProposition.ToString("0.00"),
                    model.Aligned ? "Price/value aligned" : "Fair-value sync advised");
                sellGrid.Rows[index].Tag = model;
            }
            UpdateSellEditMode();
            sellSummary.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} products loaded; unit-price range is ${1:N0}-${2:N0}; {3} currently align with fair value. Manual prices must be whole dollars.",
                sellGrid.Rows.Count,
                activeUnitPriceMin,
                activeUnitPriceMax,
                aligned);
        }

        private void DisplaySellPricePreview(Dictionary<string, object> data)
        {
            Dictionary<string, SellPriceProductRow> prior = new Dictionary<string, SellPriceProductRow>(sellRows, StringComparer.OrdinalIgnoreCase);
            sellGrid.Rows.Clear();
            sellRows.Clear();
            activeUnitPriceMin = JsonUtil.GetDecimal(data, "minPrice", activeUnitPriceMin);
            activeUnitPriceMax = JsonUtil.GetDecimal(data, "maxPrice", activeUnitPriceMax);
            object changes;
            if (data == null || !data.TryGetValue("changes", out changes))
                return;

            int changed = 0;
            int clamped = 0;
            foreach (object item in JsonUtil.AsItems(changes))
            {
                Dictionary<string, object> row = JsonUtil.AsObject(item);
                if (row == null) continue;
                string id = JsonUtil.GetString(row, "productId", string.Empty);
                SellPriceProductRow existing;
                if (!prior.TryGetValue(id, out existing))
                    existing = new SellPriceProductRow { ProductId = id, Name = JsonUtil.GetString(row, "name", id) };
                decimal current = JsonUtil.GetDecimal(row, "expectedOldPrice", existing.CurrentPrice);
                decimal planned = JsonUtil.GetDecimal(row, "newPrice", current);
                if (Math.Abs(current - planned) > 0.001m) changed++;
                if (planned == activeUnitPriceMax && sellModeSelector.SelectedIndex == 0 && sellFactorInput.Value > 1m) clamped++;
                SellPriceProductRow model = new SellPriceProductRow
                {
                    ProductId = id,
                    Name = JsonUtil.GetString(row, "name", existing.Name),
                    DrugType = JsonUtil.GetString(row, "drugType", existing.DrugType),
                    CurrentPrice = current,
                    PlannedPrice = planned,
                    FairMarketValue = existing.FairMarketValue,
                    ValueProposition = existing.ValueProposition,
                    Aligned = existing.Aligned
                };
                if (id.Length > 0)
                    sellRows[id] = model;
                int index = sellGrid.Rows.Add(
                    FriendlySellProductName(model),
                    model.CurrentPrice,
                    model.PlannedPrice,
                    model.FairMarketValue,
                    model.ValueProposition.ToString("0.00"),
                    planned == current ? "No change" : (planned == activeUnitPriceMax ? "Ready (at unit max)" : "Ready to apply"));
                sellGrid.Rows[index].Tag = model;
            }
            UpdateSellEditMode();
            sellSummary.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Preview ready for {0} products; {1} unit prices will change{2}. Preview expires in 60 seconds.",
                sellGrid.Rows.Count,
                changed,
                clamped > 0 ? string.Format(CultureInfo.CurrentCulture, "; {0} reached the ${1:N0} exact technical maximum", clamped, activeUnitPriceMax) : string.Empty);
        }

        private Dictionary<string, object> SellFilterArguments()
        {
            return new Dictionary<string, object> { { "drugType", Convert.ToString(sellDrugSelector.SelectedItem, CultureInfo.InvariantCulture) } };
        }

        private bool TryBuildSellExplicitTargets(out List<Dictionary<string, object>> targets, out string error)
        {
            targets = new List<Dictionary<string, object>>();
            error = null;
            sellGrid.EndEdit();
            foreach (DataGridViewRow row in sellGrid.Rows)
            {
                SellPriceProductRow model = row.Tag as SellPriceProductRow;
                if (model == null || string.IsNullOrEmpty(model.ProductId))
                {
                    error = "A product row is missing its stable ID. Refresh prices before previewing.";
                    return false;
                }
                decimal target;
                if (!TryParseGridDecimal(row.Cells["SellPlanned"].Value, out target)
                    || !ValidateSellTarget(target, out error))
                    return false;
                model.PlannedPrice = target;
                targets.Add(new Dictionary<string, object>
                {
                    { "productId", model.ProductId },
                    { "price", target }
                });
            }
            if (targets.Count == 0)
            {
                error = "Refresh product prices before creating a manual preview.";
                return false;
            }
            return true;
        }

        private bool ValidateSellTarget(decimal target, out string error)
        {
            error = null;
            if (target != decimal.Truncate(target))
            {
                error = "Product unit prices must be whole dollars.";
                return false;
            }
            if (target < activeUnitPriceMin || target > activeUnitPriceMax)
            {
                error = string.Format(CultureInfo.CurrentCulture, "Product unit price must stay between ${0:N0} and the ${1:N0} exact technical maximum.", activeUnitPriceMin, activeUnitPriceMax);
                return false;
            }
            return true;
        }

        private bool CustomerIncludeLocked
        {
            get { return customerScopeSelector.SelectedIndex == 1; }
        }

        private async Task RefreshCustomerAllowancesAsync()
        {
            InvalidateCustomerPreview();
            SetCustomerBusy(true, "Reading live customer allowances...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("customer.allowance.list", new Dictionary<string, object>
                {
                    { "includeLocked", CustomerIncludeLocked }
                }, true);
                ShowResult(result);
                if (!result.Success)
                    return;
                DisplayCustomerAllowanceList(result.Data);
                currentBridgeRevision = result.Revision;
            }
            finally
            {
                SetCustomerBusy(false, null);
            }
        }

        private async Task PreviewCustomerAllowancesAsync()
        {
            InvalidateCustomerPreview();
            SetCustomerBusy(true, "Building a revision-checked customer allowance preview...");
            try
            {
                Dictionary<string, object> args;
                if (customerModeSelector.SelectedIndex == 0)
                {
                    args = new Dictionary<string, object>
                    {
                        { "includeLocked", CustomerIncludeLocked },
                        { "mode", "originalFactor" },
                        { "factor", customerFactorInput.Value }
                    };
                }
                else
                {
                    List<Dictionary<string, object>> targets;
                    string validationError;
                    if (!TryBuildCustomerExplicitTargets(out targets, out validationError))
                    {
                        ShowResult(OperationResult.Fail(validationError));
                        customerSummary.Text = validationError;
                        return;
                    }
                    args = new Dictionary<string, object>
                    {
                        { "mode", "explicitValues" },
                        { "targets", targets }
                    };
                }

                OperationResult result = await bridge.InvokeAsync("customer.allowance.preview", args, true);
                ShowResult(result);
                if (!result.Success)
                    return;
                customerPreviewId = JsonUtil.GetString(result.Data, "previewId", string.Empty);
                customerPreviewRevision = JsonUtil.GetLong(result.Data, "expectedRevision", result.Revision);
                customerPreviewConfigRevision = JsonUtil.GetLong(result.Data, "expectedConfigRevision", 0);
                DisplayCustomerAllowancePreview(result.Data);
                customerApplyButton.Enabled = soloHost && customerPreviewId.Length > 0;
            }
            finally
            {
                SetCustomerBusy(false, null);
            }
        }

        private async Task ApplyCustomerAllowancePreviewAsync()
        {
            if (customerPreviewId.Length == 0)
                return;
            if (!Confirm("Apply this customer allowance preview to the loaded save profile? The bridge will recheck every customer, persist the exact allowances, and verify the live readback."))
                return;

            SetCustomerBusy(true, "Applying, persisting, and verifying customer allowances...");
            try
            {
                OperationResult result = await bridge.InvokeAsync("customer.allowance.applyPreview", new Dictionary<string, object>
                {
                    { "previewId", customerPreviewId },
                    { "expectedRevision", customerPreviewRevision },
                    { "expectedConfigRevision", customerPreviewConfigRevision }
                }, false);
                ShowResult(result);
                InvalidateCustomerPreview();
                if (result.Success)
                {
                    await RefreshCustomerAllowancesAsync();
                }
            }
            finally
            {
                SetCustomerBusy(false, null);
            }
        }

        private void DisplayCustomerAllowanceList(Dictionary<string, object> data)
        {
            customerGrid.Rows.Clear();
            customerRows.Clear();
            object customers;
            if (data == null || !data.TryGetValue("customers", out customers))
                return;

            int unlocked = 0;
            int overridden = 0;
            foreach (object item in JsonUtil.AsItems(customers))
            {
                Dictionary<string, object> row = JsonUtil.AsObject(item);
                if (row == null) continue;
                CustomerAllowanceRow model = new CustomerAllowanceRow
                {
                    CustomerId = JsonUtil.GetString(row, "customerId", string.Empty),
                    Name = JsonUtil.GetString(row, "name", string.Empty),
                    Unlocked = JsonUtil.GetBool(row, "unlocked", false),
                    OriginalMinWeeklySpend = JsonUtil.GetDecimal(row, "originalMinWeeklySpend", 0),
                    OriginalMaxWeeklySpend = JsonUtil.GetDecimal(row, "originalMaxWeeklySpend", 0),
                    CurrentMinWeeklySpend = JsonUtil.GetDecimal(row, "currentMinWeeklySpend", 0),
                    CurrentMaxWeeklySpend = JsonUtil.GetDecimal(row, "currentMaxWeeklySpend", 0),
                    AdjustedWeeklySpend = JsonUtil.GetDecimal(row, "adjustedWeeklySpend", 0),
                    OrdersPerWeek = JsonUtil.GetDecimal(row, "ordersPerWeek", 0),
                    AllowancePerOrder = JsonUtil.GetDecimal(row, "allowancePerOrder", 0),
                    HardOfferLimit = JsonUtil.GetDecimal(row, "hardOfferLimit", 0),
                    Overridden = JsonUtil.GetBool(row, "overridden", false)
                };
                model.PlannedMinWeeklySpend = model.CurrentMinWeeklySpend;
                model.PlannedMaxWeeklySpend = model.CurrentMaxWeeklySpend;
                if (model.Unlocked) unlocked++;
                if (model.Overridden) overridden++;
                if (model.CustomerId.Length > 0)
                    customerRows[model.CustomerId] = model;
                AddCustomerGridRow(model, customerModeSelector.SelectedIndex == 1, model.Overridden ? "Custom allowance" : "Original allowance");
            }
            UpdateCustomerEditMode();
            customerSummary.Text = string.Format("{0} customers loaded; {1} unlocked; {2} use custom allowances. Double-click planned minimum or maximum in manual mode.", customerGrid.Rows.Count, unlocked, overridden);
        }

        private void DisplayCustomerAllowancePreview(Dictionary<string, object> data)
        {
            Dictionary<string, CustomerAllowanceRow> prior = new Dictionary<string, CustomerAllowanceRow>(customerRows, StringComparer.OrdinalIgnoreCase);
            customerGrid.Rows.Clear();
            customerRows.Clear();
            object changes;
            if (data == null || !data.TryGetValue("changes", out changes))
                return;

            int changed = 0;
            foreach (object item in JsonUtil.AsItems(changes))
            {
                Dictionary<string, object> row = JsonUtil.AsObject(item);
                if (row == null) continue;
                string id = JsonUtil.GetString(row, "customerId", string.Empty);
                CustomerAllowanceRow existing;
                if (!prior.TryGetValue(id, out existing))
                    existing = new CustomerAllowanceRow { CustomerId = id, Name = id };
                CustomerAllowanceRow model = new CustomerAllowanceRow
                {
                    CustomerId = id,
                    Name = JsonUtil.GetString(row, "name", existing.Name),
                    Unlocked = JsonUtil.GetBool(row, "unlocked", existing.Unlocked),
                    OriginalMinWeeklySpend = JsonUtil.GetDecimal(row, "originalMinWeeklySpend", existing.OriginalMinWeeklySpend),
                    OriginalMaxWeeklySpend = JsonUtil.GetDecimal(row, "originalMaxWeeklySpend", existing.OriginalMaxWeeklySpend),
                    CurrentMinWeeklySpend = JsonUtil.GetDecimal(row, "expectedCurrentMinWeeklySpend", existing.CurrentMinWeeklySpend),
                    CurrentMaxWeeklySpend = JsonUtil.GetDecimal(row, "expectedCurrentMaxWeeklySpend", existing.CurrentMaxWeeklySpend),
                    PlannedMinWeeklySpend = JsonUtil.GetDecimal(row, "newMinWeeklySpend", existing.CurrentMinWeeklySpend),
                    PlannedMaxWeeklySpend = JsonUtil.GetDecimal(row, "newMaxWeeklySpend", existing.CurrentMaxWeeklySpend),
                    AdjustedWeeklySpend = JsonUtil.GetDecimal(row, "plannedAdjustedWeeklySpend", existing.AdjustedWeeklySpend),
                    OrdersPerWeek = JsonUtil.GetDecimal(row, "ordersPerWeek", existing.OrdersPerWeek),
                    AllowancePerOrder = JsonUtil.GetDecimal(row, "plannedAllowancePerOrder", existing.AllowancePerOrder),
                    HardOfferLimit = JsonUtil.GetDecimal(row, "plannedHardOfferLimit", existing.HardOfferLimit)
                };
                bool differs = Math.Abs(model.CurrentMinWeeklySpend - model.PlannedMinWeeklySpend) > 0.001m
                    || Math.Abs(model.CurrentMaxWeeklySpend - model.PlannedMaxWeeklySpend) > 0.001m;
                model.Overridden = differs || existing.Overridden;
                if (differs) changed++;
                if (id.Length > 0)
                    customerRows[id] = model;
                AddCustomerGridRow(model, true, differs ? "Ready to update" : "No change");
            }
            UpdateCustomerEditMode();
            customerSummary.Text = string.Format("Preview ready for {0} customers; {1} allowance ranges will change. Preview expires in 60 seconds.", customerGrid.Rows.Count, changed);
        }

        private void AddCustomerGridRow(CustomerAllowanceRow model, bool showPlanned, string status)
        {
            int index = customerGrid.Rows.Add(
                FriendlyCustomerName(model),
                model.Unlocked ? "Unlocked" : "Locked",
                model.OriginalMinWeeklySpend,
                model.OriginalMaxWeeklySpend,
                model.CurrentMinWeeklySpend,
                model.CurrentMaxWeeklySpend,
                showPlanned ? (object)model.PlannedMinWeeklySpend : string.Empty,
                showPlanned ? (object)model.PlannedMaxWeeklySpend : string.Empty,
                model.AdjustedWeeklySpend,
                model.OrdersPerWeek,
                model.AllowancePerOrder,
                model.HardOfferLimit,
                status);
            customerGrid.Rows[index].Tag = model;
        }

        private async Task AcquirePropertyLiveAsync()
        {
            PropertyState property = propertySelector.SelectedItem as PropertyState;
            if (property == null) return;
            if (property.IsOwned) { ShowResult(OperationResult.Ok("The selected property is already owned.")); return; }

            OperationResult status = await bridge.InvokeAsync("system.status", new Dictionary<string, object>(), true);
            ShowResult(status);
            if (!status.Success || !JsonUtil.GetBool(status.Data, "isSoloHost", false) || !JsonUtil.GetBool(status.Data, "saveReady", false))
            {
                ShowResult(OperationResult.Fail("A loaded solo-host save is required for live ownership."));
                return;
            }
            OperationResult dryRun = await bridge.InvokeAsync("property.own", new Dictionary<string, object> { { "propertyCode", property.Code } }, true);
            ShowResult(dryRun);
            if (!dryRun.Success || !Confirm("Acquire '" + property.Code + "' through the live vanilla ownership path?"))
                return;
            OperationResult apply = await bridge.InvokeAsync("property.own", new Dictionary<string, object>
            {
                { "propertyCode", property.Code },
                { "expectedRevision", status.Revision }
            }, false);
            ShowResult(apply);
            if (apply.Success)
            {
                OperationResult save = await bridge.InvokeAsync("game.save", new Dictionary<string, object>(), false);
                ShowResult(save);
                RefreshSaves();
            }
        }

        private void AcquirePropertyOffline()
        {
            PropertyState property = propertySelector.SelectedItem as PropertyState;
            if (property == null || property.IsOwned) return;
            if (Confirm("Acquire '" + property.Code + "' directly in the selected save? A complete backup is created first and reload is required."))
                RunWithSelectedSave(s => saves.OwnPropertyOffline(s, property.Code));
        }

        private async Task ExecuteCommandAsync()
        {
            string command = commandInput.Text.Trim();
            if (command.Length == 0) return;
            commandOutput.AppendText("> " + command + Environment.NewLine);
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            SaveDescriptor save = SelectedSave;
            OperationResult result;
            try
            {
                if (EqualsPart(parts, 0, "status"))
                    result = OperationResult.Ok(string.Format("Game={0}; Save={1}; Console={2}", environment.IsGameRunning() ? "running" : "stopped", save == null ? "none" : save.Key, save != null && save.ConsoleEnabled ? "enabled" : "disabled"));
                else if (EqualsPart(parts, 0, "backup"))
                    result = RequireSave(save, s => saves.CreateBackup(s, "command"));
                else if (EqualsPart(parts, 0, "validate"))
                    result = RequireSave(save, saves.ValidateSave);
                else if (parts.Length >= 2 && EqualsPart(parts, 0, "bridge"))
                    result = await ExecuteBridgeCommandAsync(parts);
                else
                    result = OperationResult.Fail("Unknown command. Open Help > Command reference.");
            }
            catch (Exception ex)
            {
                result = OperationResult.Fail("Command failed: " + ex.Message);
            }
            commandOutput.AppendText(result.Message + Environment.NewLine);
            if (!string.IsNullOrEmpty(result.RawResponse))
                commandOutput.AppendText(JsonUtil.PrettyPrint(result.RawResponse) + Environment.NewLine);
            ShowResult(result);
        }

        private async Task<OperationResult> ExecuteBridgeCommandAsync(string[] parts)
        {
            if (parts.Length == 2 && EqualsPart(parts, 1, "status"))
                return await bridge.InvokeAsync("system.status", new Dictionary<string, object>(), true);
            if (parts.Length >= 3 && EqualsPart(parts, 1, "prices"))
                return await bridge.InvokeAsync("product.price.list", new Dictionary<string, object> { { "drugType", parts[2] } }, true);
            if (parts.Length >= 3 && EqualsPart(parts, 1, "market"))
                return await bridge.InvokeAsync("product.market.list", new Dictionary<string, object> { { "drugType", parts[2] } }, true);
            if (parts.Length >= 3 && EqualsPart(parts, 1, "market-preview"))
            {
                Dictionary<string, object> args = new Dictionary<string, object> { { "drugType", parts[2] }, { "mode", "matchSellPrice" } };
                if (parts.Length >= 4)
                {
                    decimal factor;
                    if (!decimal.TryParse(parts[3], NumberStyles.Number, CultureInfo.InvariantCulture, out factor))
                        return OperationResult.Fail("Invalid factor: " + parts[3]);
                    args["mode"] = "absoluteFactor";
                    args["factor"] = factor;
                }
                return await bridge.InvokeAsync("product.market.previewSync", args, true);
            }
            if (parts.Length == 2 && EqualsPart(parts, 1, "save"))
            {
                if (!Confirm("Start an in-game save through the live bridge?"))
                    return OperationResult.Fail("Live save canceled.");
                return await bridge.InvokeAsync("game.save", new Dictionary<string, object>(), false);
            }
            return OperationResult.Fail("Bridge commands: bridge status | bridge prices <drug> | bridge market <drug> | bridge market-preview <drug> [factor] | bridge save");
        }

        private void PreviewLegacyPrices()
        {
            SaveDescriptor save = SelectedSave;
            if (save == null) { ShowResult(OperationResult.Fail("Select a save first.")); return; }
            OperationResult result = saves.PreviewPriceFactor(save, Convert.ToString(legacyDrugSelector.SelectedItem), legacyFactorInput.Value, false);
            ShowResult(result);
            commandOutput.AppendText(string.Join(Environment.NewLine, result.PriceChanges.Select(p => string.Format("{0}: baseline={1}, current={2}, planned={3}", p.ProductId, p.BaselinePrice, p.CurrentPrice, p.NewPrice))) + Environment.NewLine);
        }

        private void ApplyLegacyPrices()
        {
            SaveDescriptor save = SelectedSave;
            if (save == null) { ShowResult(OperationResult.Fail("Select a save first.")); return; }
            if (!Confirm("Apply SELL PRICE ONLY? This can make customers reject the new price until Products > Match selling price is completed."))
                return;
            ShowResult(saves.ApplyPriceFactorOffline(save, Convert.ToString(legacyDrugSelector.SelectedItem), legacyFactorInput.Value));
            RefreshSaves();
        }

        private void RefreshSaves()
        {
            string selectedKey = SelectedSave == null ? null : SelectedSave.Key;
            List<SaveDescriptor> discovered = saves.DiscoverSaves();
            saveSelector.BeginUpdate();
            saveSelector.Items.Clear();
            foreach (SaveDescriptor save in discovered) saveSelector.Items.Add(save);
            saveSelector.EndUpdate();
            int index = 0;
            if (!string.IsNullOrEmpty(selectedKey))
                for (int i = 0; i < saveSelector.Items.Count; i++)
                    if (((SaveDescriptor)saveSelector.Items[i]).Key == selectedKey) index = i;
            if (saveSelector.Items.Count > 0) saveSelector.SelectedIndex = index;
            else ShowResult(OperationResult.Fail("No Schedule I saves were discovered at " + environment.SaveRoot));
        }

        private void RefreshSelectedSaveUi()
        {
            propertySelector.Items.Clear();
            SaveDescriptor save = SelectedSave;
            if (save != null)
            {
                foreach (PropertyState property in saves.GetProperties(save)) propertySelector.Items.Add(property);
                if (propertySelector.Items.Count > 0) propertySelector.SelectedIndex = 0;
            }
            saveStatus.Text = save == null ? "No save selected" : string.Format("{0}  •  console {1}", save.SlotName, save.ConsoleEnabled ? "enabled" : "disabled");
            InvalidateMarketPreview();
            InvalidateCustomerPreview();
            InvalidatePlayerPreview();
            RefreshPropertyActions();
        }

        private void RefreshPropertyActions()
        {
            PropertyState property = propertySelector.SelectedItem as PropertyState;
            bool available = property != null && !property.IsOwned;
            ownLiveButton.Enabled = available && bridgeConnected && soloHost;
            ownOfflineButton.Enabled = available && !environment.IsGameRunning();
            if (property == null)
                propertyGuidance.Text = "Select a save and property first.";
            else if (property.IsOwned)
                propertyGuidance.Text = property.Code + " is already owned. No change is needed.";
            else
                propertyGuidance.Text = property.Code + " is available. Live acquisition is recommended; un-own remains unavailable.";
        }

        private void RunWithSelectedSave(Func<SaveDescriptor, OperationResult> operation)
        {
            SaveDescriptor save = SelectedSave;
            if (save == null) { ShowResult(OperationResult.Fail("Select a save first.")); return; }
            ShowResult(operation(save));
            RefreshSaves();
        }

        private static OperationResult RequireSave(SaveDescriptor save, Func<SaveDescriptor, OperationResult> operation)
        {
            return save == null ? OperationResult.Fail("No save is selected.") : operation(save);
        }

        private void ShowResult(OperationResult result)
        {
            if (result == null)
                return;
            if (!result.Success)
            {
                DiagnosticCategory category = InferDiagnosticCategory(result);
                if (result.Error != null)
                    diagnostics.RecordIncident(result.Error, "ui.operation", category, DiagnosticSeverity.Error, result.Message, "Code=" + (result.Code ?? "none"));
                else
                    DiagnosticsService.RecordFailure(result, "ui.operation", category, "Code=" + (result.Code ?? "none") + "; selectedSave=" + (SelectedSave == null ? "none" : DiagnosticsService.SafePath(SelectedSave.FolderPath)));
            }
            sessionOperationCount++;
            string line = string.Format(CultureInfo.InvariantCulture, "#{0:000}  {1}  [{2}] {3}{4}{5}",
                sessionOperationCount,
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                result.Success ? "OK" : "ERROR",
                result.Message,
                string.IsNullOrEmpty(result.AppliedMode) ? string.Empty : " | mode=" + result.AppliedMode,
                string.IsNullOrEmpty(result.BackupPath) ? string.Empty : " | backup=" + result.BackupPath);
            sessionOperationHistory.Insert(0, line);
            if (sessionOperationHistory.Count > MaxSessionOperationHistory)
                sessionOperationHistory.RemoveAt(sessionOperationHistory.Count - 1);
            UpdateOperationLogViews();
        }

        private static DiagnosticCategory InferDiagnosticCategory(OperationResult result)
        {
            string code = ((result == null ? string.Empty : result.Code) ?? string.Empty).ToLowerInvariant();
            string message = ((result == null ? string.Empty : result.Message) ?? string.Empty).ToLowerInvariant();
            if (code.Contains("bridge") || message.Contains("bridge")) return DiagnosticCategory.Bridge;
            if (code.Contains("json") || message.Contains("json")) return DiagnosticCategory.Save;
            if (code.Contains("protocol") || message.Contains("protocol")) return DiagnosticCategory.Protocol;
            if (message.Contains("backup")) return DiagnosticCategory.Backup;
            if (message.Contains("permission") || message.Contains("file") || message.Contains("directory")) return DiagnosticCategory.Filesystem;
            if (message.Contains("preview") || message.Contains("revision")) return DiagnosticCategory.Validation;
            return DiagnosticCategory.Validation;
        }

        private void ConfigureOperationLog(TextBox log)
        {
            if (log == null)
                return;
            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.WordWrap = false;
            log.Font = new Font("Consolas", 9F);
            log.Dock = DockStyle.Fill;
            log.Margin = new Padding(0);
            log.Padding = new Padding(12, 10, 12, 10);
            log.BackColor = InputSurface;
            log.ForeColor = Muted;
            UpdateOperationLogViews();
        }

        private void UpdateOperationLogViews()
        {
            string text = sessionOperationHistory.Count == 0
                ? "No commands have run this session."
                : string.Join(Environment.NewLine, sessionOperationHistory.ToArray());
            if (homeOperationLog != null && !homeOperationLog.IsDisposed)
            {
                homeOperationLog.Text = text;
                homeOperationLog.SelectionStart = 0;
                homeOperationLog.SelectionLength = 0;
            }
            if (operationLog != null && !operationLog.IsDisposed)
            {
                operationLog.Text = text;
                operationLog.SelectionStart = 0;
                operationLog.SelectionLength = 0;
            }
        }

        private void SetMarketBusy(bool busy, string message)
        {
            marketBusy = busy;
            marketPreviewButton.Enabled = !busy && bridgeConnected && soloHost;
            marketApplyButton.Enabled = !busy && marketPreviewId.Length > 0 && soloHost;
            if (!string.IsNullOrEmpty(message)) marketSummary.Text = message;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void SetDealLimitBusy(bool busy, string message)
        {
            dealLimitBusy = busy;
            dealLimitRefreshButton.Enabled = !busy && bridgeConnected && soloHost && dealLimitCapability;
            dealLimitPreviewButton.Enabled = !busy && bridgeConnected && soloHost && dealLimitCapability;
            dealLimitApplyButton.Enabled = !busy && dealLimitPreviewId.Length > 0 && soloHost && dealLimitCapability;
            if (!string.IsNullOrEmpty(message)) dealLimitState.Text = message;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void SetSellBusy(bool busy, string message)
        {
            sellBusy = busy;
            sellRefreshButton.Enabled = !busy && bridgeConnected && soloHost;
            sellPreviewButton.Enabled = !busy && bridgeConnected && soloHost;
            sellApplyButton.Enabled = !busy && sellPreviewId.Length > 0 && soloHost;
            if (!string.IsNullOrEmpty(message)) sellSummary.Text = message;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void SetCustomerBusy(bool busy, string message)
        {
            customerBusy = busy;
            customerRefreshButton.Enabled = !busy && bridgeConnected && soloHost;
            customerPreviewButton.Enabled = !busy && bridgeConnected && soloHost;
            customerApplyButton.Enabled = !busy && customerPreviewId.Length > 0 && soloHost;
            if (!string.IsNullOrEmpty(message)) customerSummary.Text = message;
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void InvalidateMarketPreview()
        {
            marketPreviewId = string.Empty;
            marketPreviewRevision = 0;
            marketPreviewConfigRevision = 0;
            marketApplyButton.Enabled = false;
        }

        private void InvalidateDealLimitPreview()
        {
            dealLimitPreviewId = string.Empty;
            dealLimitPreviewRevision = 0;
            dealLimitPreviewConfigRevision = 0;
            dealLimitApplyButton.Enabled = false;
        }

        private void InvalidateSellPreview()
        {
            sellPreviewId = string.Empty;
            sellPreviewRevision = 0;
            sellApplyButton.Enabled = false;
        }

        private void InvalidateCustomerPreview()
        {
            customerPreviewId = string.Empty;
            customerPreviewRevision = 0;
            customerPreviewConfigRevision = 0;
            customerApplyButton.Enabled = false;
        }

        private void UpdateSellEditMode()
        {
            if (!sellGrid.Columns.Contains("SellPlanned"))
                return;
            sellGrid.Columns["SellPlanned"].ReadOnly = sellModeSelector.SelectedIndex != 1;
        }

        private void PrepareSellManualTargets()
        {
            foreach (DataGridViewRow row in sellGrid.Rows)
            {
                SellPriceProductRow model = row.Tag as SellPriceProductRow;
                if (model == null) continue;
                model.PlannedPrice = model.CurrentPrice;
                row.Cells["SellPlanned"].Value = model.PlannedPrice;
                row.Cells["SellStatus"].Value = "Double-click planned price to edit";
            }
            if (sellGrid.Rows.Count > 0)
                sellSummary.Text = "Manual mode: double-click a planned unit price, then preview and apply the complete plan.";
        }

        private void ClearSellPlannedDisplay()
        {
            foreach (DataGridViewRow row in sellGrid.Rows)
            {
                SellPriceProductRow model = row.Tag as SellPriceProductRow;
                row.Cells["SellPlanned"].Value = string.Empty;
                if (model != null)
                    row.Cells["SellStatus"].Value = model.Aligned ? "Price/value aligned" : "Fair-value sync advised";
            }
        }

        private void SellGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || sellModeSelector.SelectedIndex != 1 || sellBusy)
                return;
            if (!string.Equals(sellGrid.Columns[e.ColumnIndex].Name, "SellPlanned", StringComparison.Ordinal))
                return;
            if (!(sellGrid.Rows[e.RowIndex].Tag is SellPriceProductRow))
                return;
            InvalidateSellPreview();
            if (!EnterCellEdit(sellGrid, sellGrid.Rows[e.RowIndex].Cells[e.ColumnIndex]))
                sellSummary.Text = "Finish or cancel the current cell edit first, then double-click the planned value again.";
        }

        private void SellGridCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || sellModeSelector.SelectedIndex != 1
                || !string.Equals(sellGrid.Columns[e.ColumnIndex].Name, "SellPlanned", StringComparison.Ordinal))
                return;
            decimal target;
            string error = null;
            if (!TryParseGridDecimal(e.FormattedValue, out target) || !ValidateSellTarget(target, out error))
            {
                e.Cancel = true;
                sellGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error ?? "Enter a whole-dollar unit price.";
                sellSummary.Text = sellGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText;
            }
        }

        private void SellGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !string.Equals(sellGrid.Columns[e.ColumnIndex].Name, "SellPlanned", StringComparison.Ordinal))
                return;
            DataGridViewCell cell = sellGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            cell.ErrorText = string.Empty;
            SellPriceProductRow model = sellGrid.Rows[e.RowIndex].Tag as SellPriceProductRow;
            decimal target;
            if (model == null || !TryParseGridDecimal(cell.Value, out target))
                return;
            model.PlannedPrice = target;
            sellGrid.Rows[e.RowIndex].Cells["SellStatus"].Value = Math.Abs(target - model.CurrentPrice) > 0.001m
                ? "Manual change - preview required"
                : "No manual change";
            InvalidateSellPreview();
            sellSummary.Text = "Manual unit prices changed. Preview the plan before applying it.";
        }

        private void UpdateMarketEditMode()
        {
            if (!marketGrid.Columns.Contains("Planned"))
                return;
            marketGrid.Columns["Planned"].ReadOnly = marketModeSelector.SelectedIndex != 2;
        }

        private void PrepareMarketManualTargets()
        {
            foreach (DataGridViewRow row in marketGrid.Rows)
            {
                MarketProductRow model = row.Tag as MarketProductRow;
                if (model == null) continue;
                model.PlannedMarketValue = model.EffectiveMarketValue;
                row.Cells["Planned"].Value = model.PlannedMarketValue;
                row.Cells["Status"].Value = "Double-click planned value to edit";
            }
            if (marketGrid.Rows.Count > 0)
                marketSummary.Text = "Manual mode: double-click a planned fair value, then preview and apply the complete plan.";
        }

        private void ClearMarketPlannedDisplay()
        {
            foreach (DataGridViewRow row in marketGrid.Rows)
            {
                MarketProductRow model = row.Tag as MarketProductRow;
                row.Cells["Planned"].Value = string.Empty;
                if (model != null)
                    row.Cells["Status"].Value = model.Aligned ? "Aligned" : "Needs sync";
            }
        }

        private void MarketGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || marketModeSelector.SelectedIndex != 2 || marketBusy)
                return;
            if (!string.Equals(marketGrid.Columns[e.ColumnIndex].Name, "Planned", StringComparison.Ordinal))
                return;
            MarketProductRow model = marketGrid.Rows[e.RowIndex].Tag as MarketProductRow;
            if (model == null)
                return;
            InvalidateMarketPreview();
            if (!EnterCellEdit(marketGrid, marketGrid.Rows[e.RowIndex].Cells[e.ColumnIndex]))
                marketSummary.Text = "Finish or cancel the current cell edit first, then double-click the planned value again.";
        }

        private void MarketGridCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || marketModeSelector.SelectedIndex != 2
                || !string.Equals(marketGrid.Columns[e.ColumnIndex].Name, "Planned", StringComparison.Ordinal))
                return;
            MarketProductRow model = marketGrid.Rows[e.RowIndex].Tag as MarketProductRow;
            decimal target;
            string error;
            if (model == null)
            {
                e.Cancel = true;
                marketGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "This product row is no longer valid. Refresh values.";
                marketSummary.Text = marketGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText;
                return;
            }
            if (!TryParseGridDecimal(e.FormattedValue, out target))
            {
                e.Cancel = true;
                marketGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = "Enter a numeric fair value.";
                marketSummary.Text = marketGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText;
                return;
            }
            if (!ValidateMarketTarget(model, target, out error))
            {
                e.Cancel = true;
                marketGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText = error;
                marketSummary.Text = marketGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText;
            }
        }

        private void MarketGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || !string.Equals(marketGrid.Columns[e.ColumnIndex].Name, "Planned", StringComparison.Ordinal))
                return;
            DataGridViewCell cell = marketGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
            cell.ErrorText = string.Empty;
            MarketProductRow model = marketGrid.Rows[e.RowIndex].Tag as MarketProductRow;
            decimal target;
            if (model == null || !TryParseGridDecimal(cell.Value, out target))
                return;
            model.PlannedMarketValue = target;
            marketGrid.Rows[e.RowIndex].Cells["Status"].Value = Math.Abs(target - model.EffectiveMarketValue) > 0.001m
                ? "Manual change - preview required"
                : "No manual change";
            InvalidateMarketPreview();
            marketSummary.Text = "Manual values changed. Preview the plan before applying it.";
        }

        private bool TryBuildMarketExplicitTargets(out List<Dictionary<string, object>> targets, out string error)
        {
            targets = new List<Dictionary<string, object>>();
            error = null;
            marketGrid.EndEdit();
            foreach (DataGridViewRow row in marketGrid.Rows)
            {
                MarketProductRow model = row.Tag as MarketProductRow;
                if (model == null || string.IsNullOrEmpty(model.ProductId))
                {
                    error = "A product row is missing its stable ID. Refresh values before previewing.";
                    return false;
                }
                decimal target;
                if (!TryParseGridDecimal(row.Cells["Planned"].Value, out target)
                    || !ValidateMarketTarget(model, target, out error))
                    return false;
                model.PlannedMarketValue = target;
                targets.Add(new Dictionary<string, object>
                {
                    { "productId", model.ProductId },
                    { "marketValue", target }
                });
            }
            if (targets.Count == 0)
            {
                error = "Refresh product values before creating a manual preview.";
                return false;
            }
            return true;
        }

        private static bool ValidateMarketTarget(MarketProductRow model, decimal target, out string error)
        {
            error = null;
            if (target < 0m || target > PracticalMoneyMaximum)
            {
                error = "Fair value must be between 0 and 16,777,215.";
                return false;
            }
            if (model.VanillaMarketValue <= 0m)
            {
                if (target != 0m)
                {
                    error = "A product with zero original fair value can only use a target of 0.";
                    return false;
                }
                return true;
            }
            decimal factor = target / model.VanillaMarketValue;
            if (factor < 0.1m || factor > 10m)
            {
                error = string.Format(CultureInfo.CurrentCulture, "This target is {0:0.###}x the original value; the allowed range is 0.1x to 10x.", factor);
                return false;
            }
            return true;
        }

        private void UpdateCustomerEditMode()
        {
            if (!customerGrid.Columns.Contains("PlannedMin"))
                return;
            bool manual = customerModeSelector.SelectedIndex == 1;
            customerGrid.Columns["PlannedMin"].ReadOnly = !manual;
            customerGrid.Columns["PlannedMax"].ReadOnly = !manual;
        }

        private void PrepareCustomerManualTargets()
        {
            foreach (DataGridViewRow row in customerGrid.Rows)
            {
                CustomerAllowanceRow model = row.Tag as CustomerAllowanceRow;
                if (model == null) continue;
                model.PlannedMinWeeklySpend = model.CurrentMinWeeklySpend;
                model.PlannedMaxWeeklySpend = model.CurrentMaxWeeklySpend;
                row.Cells["PlannedMin"].Value = model.PlannedMinWeeklySpend;
                row.Cells["PlannedMax"].Value = model.PlannedMaxWeeklySpend;
                row.Cells["CustomerStatus"].Value = "Double-click a planned value to edit";
            }
            if (customerGrid.Rows.Count > 0)
                customerSummary.Text = "Manual mode: double-click planned minimum or maximum, then preview and apply the complete plan.";
        }

        private void ClearCustomerPlannedDisplay()
        {
            foreach (DataGridViewRow row in customerGrid.Rows)
            {
                CustomerAllowanceRow model = row.Tag as CustomerAllowanceRow;
                row.Cells["PlannedMin"].Value = string.Empty;
                row.Cells["PlannedMax"].Value = string.Empty;
                if (model != null)
                    row.Cells["CustomerStatus"].Value = model.Overridden ? "Custom allowance" : "Original allowance";
            }
        }

        private void CustomerGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || customerModeSelector.SelectedIndex != 1 || customerBusy)
                return;
            string column = customerGrid.Columns[e.ColumnIndex].Name;
            if (!string.Equals(column, "PlannedMin", StringComparison.Ordinal) && !string.Equals(column, "PlannedMax", StringComparison.Ordinal))
                return;
            CustomerAllowanceRow model = customerGrid.Rows[e.RowIndex].Tag as CustomerAllowanceRow;
            if (model == null)
                return;
            InvalidateCustomerPreview();
            if (!EnterCellEdit(customerGrid, customerGrid.Rows[e.RowIndex].Cells[e.ColumnIndex]))
                customerSummary.Text = "Finish or cancel the current cell edit first, then double-click the planned value again.";
        }

        // WinForms throws InvalidOperationException when CurrentCell is changed
        // while another cell edit is still pending ("cannot commit or quit a
        // cell value change"). Commit or cancel the pending edit first, and
        // fall back quietly if the grid still refuses to move.
        private static bool EnterCellEdit(DataGridView grid, DataGridViewCell cell)
        {
            try
            {
                if (grid.IsCurrentCellInEditMode)
                {
                    bool committed = grid.EndEdit(DataGridViewDataErrorContexts.Commit);
                    if (!committed)
                        grid.CancelEdit();
                }
                grid.CurrentCell = cell;
                grid.BeginEdit(true);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void CustomerGridCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || customerModeSelector.SelectedIndex != 1)
                return;
            string column = customerGrid.Columns[e.ColumnIndex].Name;
            if (!string.Equals(column, "PlannedMin", StringComparison.Ordinal) && !string.Equals(column, "PlannedMax", StringComparison.Ordinal))
                return;

            DataGridViewRow gridRow = customerGrid.Rows[e.RowIndex];
            CustomerAllowanceRow model = gridRow.Tag as CustomerAllowanceRow;
            decimal proposed;
            decimal min;
            decimal max;
            string error;
            if (model == null || !TryParseGridDecimal(e.FormattedValue, out proposed))
            {
                e.Cancel = true;
                gridRow.Cells[e.ColumnIndex].ErrorText = model == null ? "This customer row is no longer valid. Refresh allowances." : "Enter a numeric weekly allowance.";
                customerSummary.Text = gridRow.Cells[e.ColumnIndex].ErrorText;
                return;
            }

            min = string.Equals(column, "PlannedMin", StringComparison.Ordinal) ? proposed : ReadCustomerPlannedValue(gridRow, "PlannedMin", model.PlannedMinWeeklySpend);
            max = string.Equals(column, "PlannedMax", StringComparison.Ordinal) ? proposed : ReadCustomerPlannedValue(gridRow, "PlannedMax", model.PlannedMaxWeeklySpend);
            if (!ValidateCustomerAllowanceRange(min, max, out error))
            {
                e.Cancel = true;
                gridRow.Cells[e.ColumnIndex].ErrorText = error;
                customerSummary.Text = error;
            }
        }

        private void CustomerGridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            string column = customerGrid.Columns[e.ColumnIndex].Name;
            if (!string.Equals(column, "PlannedMin", StringComparison.Ordinal) && !string.Equals(column, "PlannedMax", StringComparison.Ordinal))
                return;
            DataGridViewRow row = customerGrid.Rows[e.RowIndex];
            row.Cells[e.ColumnIndex].ErrorText = string.Empty;
            CustomerAllowanceRow model = row.Tag as CustomerAllowanceRow;
            if (model == null)
                return;
            model.PlannedMinWeeklySpend = ReadCustomerPlannedValue(row, "PlannedMin", model.PlannedMinWeeklySpend);
            model.PlannedMaxWeeklySpend = ReadCustomerPlannedValue(row, "PlannedMax", model.PlannedMaxWeeklySpend);
            bool differs = Math.Abs(model.PlannedMinWeeklySpend - model.CurrentMinWeeklySpend) > 0.001m
                || Math.Abs(model.PlannedMaxWeeklySpend - model.CurrentMaxWeeklySpend) > 0.001m;
            row.Cells["CustomerStatus"].Value = differs ? "Manual change - preview required" : "No manual change";
            InvalidateCustomerPreview();
            customerSummary.Text = "Manual allowances changed. Preview the plan before applying it.";
        }

        private bool TryBuildCustomerExplicitTargets(out List<Dictionary<string, object>> targets, out string error)
        {
            targets = new List<Dictionary<string, object>>();
            error = null;
            customerGrid.EndEdit();
            foreach (DataGridViewRow row in customerGrid.Rows)
            {
                CustomerAllowanceRow model = row.Tag as CustomerAllowanceRow;
                if (model == null || string.IsNullOrEmpty(model.CustomerId))
                {
                    error = "A customer row is missing its stable ID. Refresh allowances before previewing.";
                    return false;
                }
                decimal min = ReadCustomerPlannedValue(row, "PlannedMin", model.PlannedMinWeeklySpend);
                decimal max = ReadCustomerPlannedValue(row, "PlannedMax", model.PlannedMaxWeeklySpend);
                if (!ValidateCustomerAllowanceRange(min, max, out error))
                    return false;
                model.PlannedMinWeeklySpend = min;
                model.PlannedMaxWeeklySpend = max;
                targets.Add(new Dictionary<string, object>
                {
                    { "customerId", model.CustomerId },
                    { "minWeeklySpend", min },
                    { "maxWeeklySpend", max }
                });
            }
            if (targets.Count == 0)
            {
                error = "Refresh customer allowances before creating a manual preview.";
                return false;
            }
            return true;
        }

        private static decimal ReadCustomerPlannedValue(DataGridViewRow row, string column, decimal fallback)
        {
            decimal value;
            return TryParseGridDecimal(row.Cells[column].Value, out value) ? value : fallback;
        }

        private static bool ValidateCustomerAllowanceRange(decimal min, decimal max, out string error)
        {
            error = null;
            if (min < 0m || min > PracticalMoneyMaximum || max < 0m || max > PracticalMoneyMaximum)
            {
                error = "Weekly allowance values must be between 0 and 16,777,215.";
                return false;
            }
            if (min > max)
            {
                error = "Minimum weekly allowance cannot be greater than maximum weekly allowance.";
                return false;
            }
            return true;
        }

        private static bool TryParseGridDecimal(object value, out decimal result)
        {
            string text = Convert.ToString(value, CultureInfo.CurrentCulture);
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
                || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        }

        private void BuildHelpTopics()
        {
            helpTopics["Quick start"] = "QUICK START\n\n1. Start Schedule I and load the save you want to control.\n2. Confirm the top bar says BRIDGE LIVE and SOLO HOST.\n3. Open Market intelligence > Sell prices & deal limits for deal ceilings or unit prices.\n4. Use Fair-value sync after any unit-price change.\n5. Use Customer allowances when affordability per order is still the limiting factor.\n6. In a manual plan, double-click only a planned-value cell; current values remain read-only.\n\nEvery live change still requires a short-lived, revision-checked preview and an explicit apply step.";
            helpTopics["Market prices and deal limits"] = "SELL PRICES AND DEAL LIMITS\n\nSchedule I has several independent money controls. The vanilla maximum TOTAL entered in a counteroffer or handover is $9,999. This workspace can raise both paths to any whole-dollar value through $16,777,215, or restore $9,999. The setting is bridge-wide and build-specific, persists across restarts, and activates only for an eligible solo host.\n\nProduct UNIT prices normally stop at $999. On the reviewed solo-host build, the bridge raises the runtime bound and routes price commits around that validation, allowing whole-dollar prices through $16,777,215. This is the largest whole-dollar value exactly represented by the game's single-precision price fields, so it is a technical numeric boundary rather than a gameplay cap. Changing a unit price does not automatically change fair-market value; open Fair-value sync afterward.";
            helpTopics["Fair-value sync"] = "FAIR-VALUE SYNC\n\nSelling price is the amount you ask. Fair-market value is what customers think the product is worth. Raising only the selling price lowers the value proposition and makes purchases or counteroffers less likely.\n\nMatch selling price is the recommended plan. Use absolute multiplier applies one factor against each product's original value. Edit individual fair values lets you double-click a Planned fair value cell and enter an exact target. Manual targets can range from 0 to 16,777,215 and multipliers from 0.1x to 1,000,000x, subject to the final exact-value boundary.\n\nEdits are drafts only: Preview sync freezes and validates them, and Apply and verify performs the mutation. Customer preferences, relationship, addiction, affordability, and randomness can still affect a sale.";
            helpTopics["Customer allowances"] = "CUSTOMER ALLOWANCES\n\nA customer's minimum and maximum weekly spend help determine the order budget available across their expected orders. Allowance per order and hard offer limit make the practical ceiling easier to see.\n\nScale original allowances is idempotent: the factor is always applied to the reviewed original range, not stacked on the current override. Edit individual allowances lets you double-click Planned min/week or Planned max/week and enter exact values from 0 to 16,777,215; minimum cannot exceed maximum.\n\nUnlocked scope changes active customers only. All customers can also prepare locked customers for later. Higher allowance improves affordability but does not bypass product preference, relationship, addiction, quantity, or other native deal rules.";
            helpTopics["Live and offline modes"] = "LIVE AND OFFLINE MODES\n\nLive controls use the same-user named-pipe bridge and the game's own runtime objects. They require the exact known build, a loaded save, and host/server authority.\n\nOffline controls edit save JSON. They require Schedule I to be closed, create a complete slot backup, validate JSON, and replace only the targeted file. Offline product prices support exact whole-dollar values from $1 to $16,777,215.\n\nFair-market values, customer allowances, and the total-deal maximum have no suitable native save field, so they are live plus bridge-sidecar only.";
            helpTopics["Property ownership"] = "PROPERTY OWNERSHIP\n\nAcquire live uses the game's vanilla Property.SetOwned path, verifies IsOwned, then starts an in-game save. Acquire offline is available only with the game closed and creates a backup.\n\nUn-owning is unavailable because it can strand employees, quests, storage, and business state.";
            helpTopics["Save protection"] = "SAVE PROTECTION\n\nBackup creates a full timestamped copy of the selected slot. Validate parses every JSON file. Console enable changes only Game.json and requires reload.\n\nOffline writes are blocked while the game is running. Live mutations are build-gated, revision-checked, allowlisted, and bounded. The bridge accepts no arbitrary paths, code, reflection, or generic console strings.";
            helpTopics["Multiplayer limits"] = "MULTIPLAYER LIMITS\n\nFair-market values and customer allowance overrides are restricted to the reviewed solo-host workflow. If a remote player joins or authority is lost, these controls become unavailable.\n\nOther live operations keep their own authority requirements.";
            helpTopics["Command reference"] = "COMMAND REFERENCE\n\nstatus\n  Local game/save summary.\n\nbackup\n  Full backup of the selected save.\n\nvalidate\n  Parse all selected-save JSON files.\n\nbridge status\n  Full bridge readiness and capability response.\n\nbridge prices <drug>\n  List live selling prices. Example: bridge prices Shrooms\n\nbridge market <drug>\n  List sell price, vanilla market, effective market, factor, and customer value.\n\nbridge market-preview <drug> [factor]\n  Without factor, preview Match selling price. With factor, preview an absolute vanilla multiplier.\n\nbridge save\n  Start the game's native save.\n\nNormal users should use the guided pages; preview IDs and revisions are intentionally handled internally.";
            helpTopics["Troubleshooting"] = "TROUBLESHOOTING\n\nBRIDGE OFFLINE\nConfirm Schedule I is running with MelonLoader and the installed bridge. Restart the game if the mod was just upgraded.\n\nAUTHORITY NOT READY\nLoad a save as host and disconnect remote players for product and customer controls.\n\nPREVIEW EXPIRED OR REVISION CONFLICT\nRefresh and preview again. Previews expire after 60 seconds and any intervening mutation invalidates them.\n\nMANUAL CELL WILL NOT EDIT\nChoose the manual plan first, then double-click a Planned value column. Current and original columns intentionally remain read-only.\n\nCUSTOMERS STILL REJECT\nCheck Products for adequate customer value, then Customers for allowance per order and hard offer limit. Preferences, relationship, addiction, quantity, and randomness can still cause rejection; these controls do not force a sale.";
            foreach (DiagnosticCatalogEntry article in DiagnosticCatalog.Entries)
                helpTopics["Troubleshooting: " + article.Title] = article.Title.ToUpperInvariant() + "\n\nSYMPTOMS\n" + article.Symptoms + "\n\nLIKELY CAUSE / REASONING\n" + article.Reasoning + "\n\nEVIDENCE TO INSPECT\n" + article.Evidence + "\n\nNEXT STEPS\n" + article.NextSteps;
            helpTopics["Security and rollback"] = "SECURITY AND ROLLBACK\n\nThe bridge is local-only, one-client, same-user, and uses bounded versioned JSON. Market and customer overrides are save-scoped. The total-deal maximum is build-specific and stored in UserData\\ScheduleIControlBridge.sell-price-limit.json. Manual cell edits remain local drafts until previewed and applied.\n\nDisable only the bridge by closing the game and removing or renaming Mods\\ScheduleIControlBridge.dll. On the next launch without the mod, the native $9,999 total-deal ceiling returns and bridge-owned fair-market/allowance overrides no longer apply; saved unit prices remain changed. Restore a save backup only when you intentionally want to reverse saved unit prices or ownership.";
        }

        private void FilterHelpTopics()
        {
            string search = helpSearch.Text.Trim();
            string selected = Convert.ToString(helpTopicList.SelectedItem);
            helpTopicList.BeginUpdate();
            helpTopicList.Items.Clear();
            foreach (KeyValuePair<string, string> topic in helpTopics)
                if (search.Length == 0 || topic.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 || topic.Value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    helpTopicList.Items.Add(topic.Key);
            helpTopicList.EndUpdate();
            if (helpTopicList.Items.Count > 0)
            {
                int index = selected.Length == 0 ? 0 : helpTopicList.Items.IndexOf(selected);
                helpTopicList.SelectedIndex = index >= 0 ? index : 0;
            }
            else
                helpText.Text = "No help topics matched your search.";
        }

        private void ShowSelectedHelpTopic()
        {
            string key = Convert.ToString(helpTopicList.SelectedItem);
            string text;
            helpText.Text = helpTopics.TryGetValue(key, out text) ? text : string.Empty;
        }

        private void ConfigureMarketGrid()
        {
            ApplyGridTheme(marketGrid);
            marketGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
            marketGrid.MultiSelect = false;
            marketGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            marketGrid.Columns.Add("Product", "Product");
            marketGrid.Columns.Add("SellPrice", "Sell price");
            marketGrid.Columns.Add("FairMarket", "Fair value");
            marketGrid.Columns.Add("Planned", "Planned");
            marketGrid.Columns.Add("CustomerValue", "Customer value");
            marketGrid.Columns.Add("Status", "Status");
            foreach (DataGridViewColumn column in marketGrid.Columns)
                column.ReadOnly = true;
            marketGrid.Columns["Planned"].DefaultCellStyle.Format = "0.##";
            marketGrid.Columns["SellPrice"].DefaultCellStyle.Format = "0.##";
            marketGrid.Columns["FairMarket"].DefaultCellStyle.Format = "0.##";
            marketGrid.Columns[0].FillWeight = 150;
            marketGrid.Columns[5].FillWeight = 115;
            marketGrid.CellDoubleClick += MarketGridCellDoubleClick;
            marketGrid.CellValidating += MarketGridCellValidating;
            marketGrid.CellEndEdit += MarketGridCellEndEdit;
            marketGrid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.Cancel = true; };
        }

        private void ConfigureSellGrid()
        {
            ApplyGridTheme(sellGrid);
            sellGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
            sellGrid.MultiSelect = false;
            sellGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            sellGrid.Columns.Add("SellProduct", "Product");
            sellGrid.Columns.Add("SellCurrent", "Current price");
            sellGrid.Columns.Add("SellPlanned", "Planned price");
            sellGrid.Columns.Add("SellFairValue", "Fair value");
            // A unit-price preview does not mutate fair-market value, so this remains the
            // current value proposition until the change is applied and the list refreshes.
            sellGrid.Columns.Add("SellCustomerValue", "Customer value");
            sellGrid.Columns.Add("SellStatus", "Status");
            foreach (DataGridViewColumn column in sellGrid.Columns)
                column.ReadOnly = true;
            foreach (string name in new[] { "SellCurrent", "SellPlanned", "SellFairValue" })
                sellGrid.Columns[name].DefaultCellStyle.Format = "0.##";
            sellGrid.Columns["SellProduct"].FillWeight = 150;
            sellGrid.Columns["SellStatus"].FillWeight = 130;
            sellGrid.CellDoubleClick += SellGridCellDoubleClick;
            sellGrid.CellValidating += SellGridCellValidating;
            sellGrid.CellEndEdit += SellGridCellEndEdit;
            sellGrid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.Cancel = true; };
        }

        private void ConfigureCustomerGrid()
        {
            ApplyGridTheme(customerGrid);
            customerGrid.EditMode = DataGridViewEditMode.EditProgrammatically;
            customerGrid.MultiSelect = false;
            customerGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            customerGrid.Columns.Add("Customer", "Customer");
            customerGrid.Columns.Add("Access", "Access");
            customerGrid.Columns.Add("OriginalMin", "Base min");
            customerGrid.Columns.Add("OriginalMax", "Base max");
            customerGrid.Columns.Add("CurrentMin", "Current min");
            customerGrid.Columns.Add("CurrentMax", "Current max");
            customerGrid.Columns.Add("PlannedMin", "Planned min");
            customerGrid.Columns.Add("PlannedMax", "Planned max");
            customerGrid.Columns.Add("Adjusted", "Weekly spend");
            customerGrid.Columns.Add("Orders", "Orders");
            customerGrid.Columns.Add("PerOrder", "Per order");
            customerGrid.Columns.Add("OfferLimit", "Max offer");
            customerGrid.Columns.Add("CustomerStatus", "Status");
            foreach (DataGridViewColumn column in customerGrid.Columns)
            {
                column.ReadOnly = true;
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            customerGrid.Columns["Customer"].Width = 210;
            customerGrid.Columns["Access"].Width = 78;
            customerGrid.Columns["CustomerStatus"].Width = 175;
            foreach (string name in new[] { "OriginalMin", "OriginalMax", "CurrentMin", "CurrentMax", "PlannedMin", "PlannedMax", "Adjusted", "Orders", "PerOrder", "OfferLimit" })
            {
                customerGrid.Columns[name].Width = 108;
                customerGrid.Columns[name].DefaultCellStyle.Format = "0.##";
            }
            customerGrid.CellDoubleClick += CustomerGridCellDoubleClick;
            customerGrid.CellValidating += CustomerGridCellValidating;
            customerGrid.CellEndEdit += CustomerGridCellEndEdit;
            customerGrid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.Cancel = true; };
        }

        private static string FriendlyProductName(MarketProductRow row)
        {
            string name = row == null ? string.Empty : row.Name;
            string id = row == null ? string.Empty : row.ProductId;
            return string.IsNullOrEmpty(name) ? id : name + " (" + id + ")";
        }

        private static string FriendlySellProductName(SellPriceProductRow row)
        {
            string name = row == null ? string.Empty : row.Name;
            string id = row == null ? string.Empty : row.ProductId;
            return string.IsNullOrEmpty(name) ? id : name + " (" + id + ")";
        }

        private static string FriendlyCustomerName(CustomerAllowanceRow row)
        {
            string name = row == null ? string.Empty : row.Name;
            string id = row == null ? string.Empty : row.CustomerId;
            return string.IsNullOrEmpty(name) || string.Equals(name, id, StringComparison.OrdinalIgnoreCase) ? id : name + " (" + id + ")";
        }

        private static void ConfigureDrugSelector(ComboBox box)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.Clear();
            box.Items.AddRange(new object[] { "Shrooms", "Cocaine", "Meth", "Weed", "All" });
            box.SelectedIndex = 0;
        }

        private static TabPage NewPage(string text)
        {
            return new TabPage(text) { BackColor = AppBackground, ForeColor = Ink, Padding = new Padding(14) };
        }

        private static TableLayoutPanel PageLayout(int rows)
        {
            return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows, BackColor = AppBackground };
        }

        private static Control CreateScrollablePage(Control content, int minimumHeight)
        {
            Panel viewport = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AppBackground,
                AutoScroll = true,
                Padding = new Padding(0)
            };

            // Docking the content to the top and giving it a real minimum height
            // lets WinForms expose a native page scrollbar when the form is short,
            // while preserving the DataGridView's own row/column scrolling.
            content.Dock = DockStyle.Top;
            content.MinimumSize = new Size(0, minimumHeight);
            content.Height = minimumHeight;
            viewport.Controls.Add(content);
            return viewport;
        }

        private static GroupBox NewGroup(string text)
        {
            return new IntelGroupBox { Text = text, Dock = DockStyle.Fill, Font = CardTitleFont, BackColor = AppBackground, ForeColor = Ink, Padding = new Padding(16, 18, 16, 14), Margin = new Padding(0, 0, 0, 12) };
        }

        private static Label FieldLabel(string text)
        {
            int width = Math.Max(72, Math.Min(138, TextRenderer.MeasureText(text, UiFont).Width + 12));
            return new Label { Text = text, AutoSize = false, Width = width, Height = 34, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 0, 6, 0), ForeColor = Muted, Font = UiFont };
        }

        private static Button MakeButton(string text, bool primary)
        {
            Button button = new IntelButton { Text = text, Height = 38, Width = 145, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(5, 3, 5, 3) };
            StyleButton(button, primary);
            return button;
        }

        private static void StyleButton(Button button, bool primary)
        {
            ApplyButtonStyle(button, primary);
        }

        private static Panel MakeToolCard(string title, string description, string buttonText, bool primary, Action action)
        {
            Button button = MakeButton(buttonText, primary);
            button.Click += delegate { action(); };
            return MakeToolCard(title, description, button);
        }

        private static Panel MakeToolCard(string title, string description, Button button)
        {
            Panel card = new IntelCardPanel { Width = 320, Height = 170, BackColor = Surface, Padding = new Padding(16), Margin = new Padding(8) };
            Label heading = new Label { Text = title, Font = CardTitleFont, Dock = DockStyle.Top, Height = 30, ForeColor = Ink };
            Label body = new Label { Text = description, Dock = DockStyle.Top, Height = 70, ForeColor = Muted, Font = UiFont };
            button.Dock = DockStyle.Bottom;
            card.Controls.Add(button);
            card.Controls.Add(body);
            card.Controls.Add(heading);
            return card;
        }

        private static Control WrapWithCaption(string caption, Control content)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            Label label = new Label { Text = caption, Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI Semibold", 9F) };
            panel.Controls.Add(content);
            panel.Controls.Add(label);
            content.BringToFront();
            return panel;
        }

        private static bool EqualsPart(string[] parts, int index, string value)
        {
            return index < parts.Length && string.Equals(parts[index], value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAnyStatusField(Dictionary<string, object> data, params string[] names)
        {
            if (data == null || names == null)
                return false;
            for (int i = 0; i < names.Length; i++)
            {
                if (data.ContainsKey(names[i]))
                    return true;
            }
            return false;
        }

        private bool Confirm(string message)
        {
            return MessageBox.Show(this, message, "Schedule I Control Center", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
    }
}
