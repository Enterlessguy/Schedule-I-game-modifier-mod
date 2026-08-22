using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScheduleIControlCenter
{
    internal sealed partial class MainForm
    {
        private readonly UpdateService updateService;
        private readonly Label updateState = new Label();
        private readonly Label updateDetail = new Label();
        private readonly Label updateChecked = new Label();
        private readonly RichTextBox updateNotes = new RichTextBox();
        private readonly Button updateActionButton = new IntelButton();
        private readonly Button updateReleaseButton = new IntelButton();
        private readonly Panel updateProgressTrack = new Panel();
        private readonly Panel updateProgressFill = new Panel();
        private UpdateRelease availableUpdate;
        private bool updateBusy;

        private TabPage BuildUpdatesPage()
        {
            TabPage page = NewPage("Updates");
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = AppBackground,
                Padding = new Padding(0, 2, 0, 0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 218));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Panel intro = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground, Padding = new Padding(12, 8, 12, 8) };
            Label title = new Label { Text = "Application updates", Dock = DockStyle.Top, Height = 36, Font = HeadingFont, ForeColor = Ink };
            Label description = new Label
            {
                Text = "Keep the complete Control Center package synchronized with stable Intelligence Database releases on GitHub.",
                Dock = DockStyle.Fill,
                Font = UiFont,
                ForeColor = Muted
            };
            intro.Controls.Add(description);
            intro.Controls.Add(title);
            layout.Controls.Add(intro, 0, 0);

            GroupBox statusGroup = NewGroup("Release status");
            TableLayoutPanel status = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                Padding = new Padding(16, 12, 16, 12),
                BackColor = Surface
            };
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            IntelCubeMark cube = new IntelCubeMark { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 18, 0) };
            status.Controls.Add(cube, 0, 0);
            status.SetRowSpan(cube, 2);
            updateState.Text = "Checking release status";
            updateState.Dock = DockStyle.Fill;
            updateState.Font = new Font("Segoe UI Semibold", 15F);
            updateState.ForeColor = Ink;
            updateState.TextAlign = ContentAlignment.BottomLeft;
            status.Controls.Add(updateState, 1, 0);
            updateDetail.Text = "Installed: " + ReleaseInfo.Label + " (" + ReleaseInfo.SemanticVersion + ")";
            updateDetail.Dock = DockStyle.Fill;
            updateDetail.Font = UiFont;
            updateDetail.ForeColor = Muted;
            updateDetail.TextAlign = ContentAlignment.TopLeft;
            status.Controls.Add(updateDetail, 1, 1);

            updateActionButton.Text = "Check for updates";
            updateActionButton.Dock = DockStyle.Fill;
            updateActionButton.Margin = new Padding(8, 8, 0, 8);
            StyleButton(updateActionButton, true);
            updateActionButton.Click += async delegate { await HandleUpdateActionAsync(); };
            status.Controls.Add(updateActionButton, 2, 0);
            updateReleaseButton.Text = "View release on GitHub";
            updateReleaseButton.Dock = DockStyle.Fill;
            updateReleaseButton.Margin = new Padding(8, 4, 0, 8);
            StyleButton(updateReleaseButton, false);
            updateReleaseButton.Enabled = false;
            updateReleaseButton.Click += delegate { OpenCurrentReleasePage(); };
            status.Controls.Add(updateReleaseButton, 2, 1);

            updateChecked.Text = "No release information has been retrieved yet.";
            updateChecked.Dock = DockStyle.Fill;
            updateChecked.ForeColor = Faint;
            updateChecked.Font = new Font("Segoe UI", 8.5F);
            updateChecked.TextAlign = ContentAlignment.MiddleLeft;
            status.Controls.Add(updateChecked, 0, 2);
            status.SetColumnSpan(updateChecked, 2);
            updateProgressTrack.Dock = DockStyle.Fill;
            updateProgressTrack.Margin = new Padding(8, 9, 0, 9);
            updateProgressTrack.BackColor = Border;
            updateProgressTrack.Visible = false;
            updateProgressFill.BackColor = Primary;
            updateProgressFill.Dock = DockStyle.Left;
            updateProgressFill.Width = 0;
            updateProgressTrack.Controls.Add(updateProgressFill);
            status.Controls.Add(updateProgressTrack, 2, 2);
            statusGroup.Controls.Add(status);
            layout.Controls.Add(statusGroup, 0, 1);

            GroupBox notesGroup = NewGroup("Latest release notes");
            updateNotes.Dock = DockStyle.Fill;
            updateNotes.ReadOnly = true;
            updateNotes.BorderStyle = BorderStyle.None;
            updateNotes.BackColor = Surface;
            updateNotes.ForeColor = Muted;
            updateNotes.Font = new Font("Segoe UI", 10F);
            updateNotes.Text = "Connect to the internet and check GitHub to retrieve the latest stable release information. Last-known release details remain available while offline.";
            updateNotes.Padding = new Padding(12);
            notesGroup.Controls.Add(updateNotes);
            layout.Controls.Add(notesGroup, 0, 2);

            page.Controls.Add(layout);
            return page;
        }

        private async Task HandleUpdateActionAsync()
        {
            if (updateBusy) return;
            if (availableUpdate == null)
            {
                await CheckForUpdatesAsync(false);
                return;
            }
            await DownloadAndInstallUpdateAsync();
        }

        private async Task CheckForUpdatesAsync(bool automatic)
        {
            if (updateBusy || IsDisposed) return;
            updateBusy = true;
            SetUpdateProgress(0, false);
            updateState.Text = "Checking GitHub";
            updateDetail.Text = "Retrieving stable release metadata over HTTPS…";
            updateActionButton.Enabled = false;
            try
            {
                UpdateCheckResult result = await updateService.CheckForUpdatesAsync();
                ApplyUpdateResult(result, false);
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "update.check", DiagnosticCategory.Update, DiagnosticSeverity.Warning,
                    "The latest release could not be checked.", "Cached release information was used when available.");
                UpdateCheckResult cached = updateService.LoadCachedResult();
                if (cached != null)
                {
                    ApplyUpdateResult(cached, true);
                    updateState.Text = cached.UpdateAvailable ? "Update last seen — offline" : "Offline — using cached status";
                    updateDetail.Text = "GitHub is unreachable. " + cached.Message;
                }
                else
                {
                    availableUpdate = null;
                    updateState.Text = "Unable to reach GitHub";
                    updateState.ForeColor = Warning;
                    updateDetail.Text = "No cached release information is available. Check your connection and try again.";
                    updateNotes.Text = "Update checks need an internet connection. The application itself remains usable while offline.";
                    updateActionButton.Text = "Try again";
                    updateReleaseButton.Enabled = false;
                    updateChecked.Text = "Release check failed at " + DateTime.Now.ToString("g", CultureInfo.CurrentCulture) + ".";
                }
                if (!automatic)
                    System.Media.SystemSounds.Exclamation.Play();
            }
            finally
            {
                updateBusy = false;
                if (!IsDisposed) updateActionButton.Enabled = true;
            }
        }

        private void ApplyUpdateResult(UpdateCheckResult result, bool cached)
        {
            availableUpdate = result != null && result.UpdateAvailable ? result.Release : null;
            UpdateRelease release = result == null ? null : result.Release;
            updateState.ForeColor = availableUpdate == null ? Success : PrimaryHover;
            updateState.Text = availableUpdate == null ? "You’re up to date" : "Control Center " + availableUpdate.VersionText + " is ready";
            updateDetail.Text = availableUpdate == null
                ? "Installed: " + ReleaseInfo.Label + " (" + ReleaseInfo.SemanticVersion + ")"
                : "Installed " + ReleaseInfo.SemanticVersion + "  •  Available " + availableUpdate.VersionText + "  •  Complete package update";
            updateActionButton.Text = availableUpdate == null ? "Check again" : "Download and install";
            updateReleaseButton.Enabled = release != null && release.ReleasePageUri != null;
            updateNotes.Text = release == null || string.IsNullOrWhiteSpace(release.ReleaseNotes)
                ? "No release notes were supplied for the latest stable release."
                : release.ReleaseNotes.Trim();
            DateTime checkedUtc = result == null ? DateTime.MinValue : result.CheckedUtc;
            updateChecked.Text = checkedUtc == DateTime.MinValue
                ? (cached ? "Showing last-known release metadata." : "Release metadata retrieved.")
                : (cached ? "Last successful check: " : "Checked: ") + checkedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    + (cached ? "  •  cached for offline viewing" : "  •  GitHub Releases");
        }

        private async Task DownloadAndInstallUpdateAsync()
        {
            UpdateRelease release = availableUpdate;
            if (release == null) return;
            if (environment.IsGameRunning())
            {
                MessageBox.Show(this, "Close Schedule I before installing the update. The download was not started.",
                    "Game must be closed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult choice = MessageBox.Show(this,
                "Install Control Center " + release.VersionText + "?\n\nThe complete verified release package will be synchronized into the Schedule I folder. Existing managed files are backed up before replacement.",
                "Install update", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (choice != DialogResult.Yes) return;

            updateBusy = true;
            updateActionButton.Enabled = false;
            updateReleaseButton.Enabled = false;
            updateState.Text = "Downloading update";
            updateDetail.Text = "Verifying size and SHA-256 integrity before installation…";
            SetUpdateProgress(0, true);
            try
            {
                string archive = await updateService.DownloadAsync(release, progress =>
                {
                    try
                    {
                        if (IsDisposed || !IsHandleCreated) return;
                        BeginInvoke((Action)(() => SetUpdateProgress(progress, true)));
                    }
                    catch (InvalidOperationException) { }
                });
                updateState.Text = "Starting verified installer";
                updateDetail.Text = "The Control Center will close, apply the complete package transactionally, and reopen.";
                string error;
                if (!UpdateService.StartInstaller(release, archive, environment.GameRoot, out error))
                    throw new InvalidOperationException(error ?? "Windows did not start the update installer.");
                statusTimer.Stop();
                BeginInvoke((Action)Application.Exit);
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "update.install", DiagnosticCategory.Update, DiagnosticSeverity.Error,
                    "The application update was not installed.", "The existing installation remains active.");
                updateState.Text = "Update was not installed";
                updateState.ForeColor = Danger;
                updateDetail.Text = ex.Message;
                updateActionButton.Text = "Try again";
                updateActionButton.Enabled = true;
                updateReleaseButton.Enabled = release.ReleasePageUri != null;
                SetUpdateProgress(0, false);
                updateBusy = false;
            }
        }

        private void SetUpdateProgress(int progress, bool visible)
        {
            if (updateProgressTrack.IsDisposed) return;
            updateProgressTrack.Visible = visible;
            int bounded = Math.Max(0, Math.Min(100, progress));
            updateProgressFill.Width = Math.Max(0, updateProgressTrack.ClientSize.Width * bounded / 100);
            updateProgressFill.Height = updateProgressTrack.ClientSize.Height;
        }

        private void OpenCurrentReleasePage()
        {
            UpdateRelease release = availableUpdate ?? (updateService.LoadCachedResult() ?? new UpdateCheckResult()).Release;
            if (release == null || release.ReleasePageUri == null) return;
            try { Process.Start(new ProcessStartInfo(release.ReleasePageUri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "update.open-release", DiagnosticCategory.Update, DiagnosticSeverity.Warning,
                    "The release page could not be opened.", release.ReleasePageUri.AbsoluteUri);
            }
        }

        private void ShowCompletedUpdateIfNeeded()
        {
            try
            {
                string path = Path.Combine(environment.GameRoot, "ScheduleI-ControlCenter", "Updates", "last-update.json");
                if (!File.Exists(path)) return;
                Dictionary<string, object> data = JsonUtil.ReadObject(path);
                if (JsonUtil.GetBool(data, "acknowledged", false)) return;
                if (!JsonUtil.GetBool(data, "success", false))
                {
                    string failure = JsonUtil.GetString(data, "message", "The update installer did not complete.");
                    DiagnosticsService.Record(new InvalidOperationException(failure), "update.previous-install",
                        DiagnosticCategory.Update, DiagnosticSeverity.Error,
                        "The previous application update was not installed.", "The existing installation was retained or restored.");
                    MessageBox.Show(this, failure + "\n\nThe existing Control Center installation remains available. Open Updates to try again or Diagnostics for details.",
                        "Update not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    data["acknowledged"] = true;
                    JsonUtil.WriteObjectAtomic(path, data);
                    return;
                }
                string version = JsonUtil.GetString(data, "version", ReleaseInfo.SemanticVersion);
                string notes = JsonUtil.GetString(data, "releaseNotes", string.Empty);
                using (UpdateWelcomeForm welcome = new UpdateWelcomeForm(version, BriefReleaseNotes(notes)))
                    welcome.ShowDialog(this);
                data["acknowledged"] = true;
                JsonUtil.WriteObjectAtomic(path, data);
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "update.welcome", DiagnosticCategory.Update, DiagnosticSeverity.Warning,
                    "The post-update summary could not be displayed.", null);
            }
        }

        private static string BriefReleaseNotes(string notes)
        {
            string text = (notes ?? string.Empty).Replace("\r", string.Empty);
            StringBuilder result = new StringBuilder();
            foreach (string raw in text.Split('\n'))
            {
                string line = Regex.Replace(raw.Trim(), "^[#>*`\\-\\s]+", string.Empty).Trim();
                if (line.Length == 0) continue;
                if (result.Length > 0) result.AppendLine();
                result.Append("• ").Append(line);
                if (result.Length >= 1400) break;
            }
            if (result.Length == 0) result.Append("This release includes the latest Control Center improvements and fixes.");
            return result.Length <= 1500 ? result.ToString() : result.ToString(0, 1500) + "…";
        }

        private sealed class UpdateWelcomeForm : Form
        {
            public UpdateWelcomeForm(string version, string notes)
            {
                Text = "Control Center updated";
                Size = new Size(760, 550);
                MinimumSize = new Size(680, 500);
                StartPosition = FormStartPosition.CenterParent;
                BackColor = AppBackground;
                ForeColor = Ink;
                Font = UiFont;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;

                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(42, 28, 42, 28), BackColor = AppBackground };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
                IntelCubeMark cube = new IntelCubeMark { Dock = DockStyle.Fill };
                layout.Controls.Add(cube, 0, 0);
                Label brand = new Label { Text = "Intelligence Database", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 20F), ForeColor = PrimaryStrong, TextAlign = ContentAlignment.MiddleCenter };
                layout.Controls.Add(brand, 0, 1);
                Label heading = new Label { Text = "Control Center " + version + " is ready", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 13F), ForeColor = Ink, TextAlign = ContentAlignment.MiddleCenter };
                layout.Controls.Add(heading, 0, 2);
                RichTextBox changelog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Surface, ForeColor = Muted, Font = new Font("Segoe UI", 10F), Text = notes, ScrollBars = RichTextBoxScrollBars.Vertical };
                layout.Controls.Add(changelog, 0, 3);
                Button close = new IntelButton { Text = "Continue", Dock = DockStyle.Right, Width = 160, DialogResult = DialogResult.OK };
                StyleButton(close, true);
                Panel actions = new Panel { Dock = DockStyle.Fill, BackColor = AppBackground, Padding = new Padding(0, 8, 0, 0) };
                actions.Controls.Add(close);
                layout.Controls.Add(actions, 0, 4);
                Controls.Add(layout);
                AcceptButton = close;
                ApplyNativeDarkMode(this);
            }
        }
    }
}
