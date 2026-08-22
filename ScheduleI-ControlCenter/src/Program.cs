using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace ScheduleIControlCenter
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            DiagnosticsService.InitializeGlobalHandlers();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                string layoutDumpPath = null;
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "--layout-dump", StringComparison.OrdinalIgnoreCase))
                        layoutDumpPath = args[i + 1];
                }
                if (!string.IsNullOrEmpty(layoutDumpPath))
                {
                    GameEnvironment environment = GameEnvironment.Detect();
                    Application.Run(new MainForm(environment, layoutDumpPath));
                }
                else
                {
                    Application.Run(new StartupApplicationContext());
                }
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "startup.main", DiagnosticCategory.Startup, DiagnosticSeverity.Fatal, "The Control Center could not start.", null);
                MessageBox.Show(ex.ToString(), "Schedule I Control Center - Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

    }

    internal sealed class StartupApplicationContext : ApplicationContext
    {
        private readonly IntroSplashForm splash;
        private readonly Rectangle controlCenterBounds;
        private MainForm controlCenter;

        public StartupApplicationContext()
        {
            controlCenterBounds = CalculateInitialControlCenterBounds();
            splash = new IntroSplashForm(controlCenterBounds);
            splash.FullyVisible += SplashFullyVisible;
            splash.IntroCompleted += SplashIntroCompleted;
            splash.Show();
        }

        private void SplashFullyVisible(object sender, EventArgs e)
        {
            try
            {
                GameEnvironment environment = GameEnvironment.Detect();
                controlCenter = new MainForm(environment, null);
                controlCenter.StartPosition = FormStartPosition.Manual;
                controlCenter.Bounds = controlCenterBounds;
                controlCenter.FormClosed += ControlCenterClosed;
                MainForm = controlCenter;
                controlCenter.Show();
                splash.BringToFront();
                splash.Activate();
                splash.RevealMainAfterHold(900);
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "startup.main-form", DiagnosticCategory.Startup, DiagnosticSeverity.Fatal, "The Control Center could not initialize its main window.", null);
                splash.Close();
                MessageBox.Show(ex.ToString(), "Schedule I Control Center - Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
            }
        }

        private void SplashIntroCompleted(object sender, EventArgs e)
        {
            splash.Close();
            if (controlCenter != null && !controlCenter.IsDisposed)
            {
                controlCenter.BringToFront();
                controlCenter.Activate();
            }
        }

        private void ControlCenterClosed(object sender, FormClosedEventArgs e)
        {
            ExitThread();
        }

        private static Rectangle CalculateInitialControlCenterBounds()
        {
            Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int width = Math.Max(1280, Math.Min(1450, workingArea.Width));
            int height = Math.Max(780, Math.Min(850, workingArea.Height));
            int left = workingArea.Left + (workingArea.Width - width) / 2;
            int top = workingArea.Top + (workingArea.Height - height) / 2;
            return new Rectangle(left, top, width, height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (splash != null)
                    splash.Dispose();
                if (controlCenter != null)
                    controlCenter.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
