using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScheduleIControlCenter
{
    internal sealed class IntroSplashForm : Form
    {
        private enum IntroPhase
        {
            FadeIn,
            WaitingForMain,
            Hold,
            FadeOut,
            Complete
        }

        private const int FadeInMilliseconds = 720;
        private const int FadeOutMilliseconds = 680;
        private readonly Timer animationTimer = new Timer { Interval = 15 };
        private readonly SplashCanvas canvas;
        private IntroPhase phase = IntroPhase.FadeIn;
        private DateTime phaseStartedUtc;
        private int holdMilliseconds = 900;

        public event EventHandler FullyVisible;
        public event EventHandler IntroCompleted;

        public IntroSplashForm(Rectangle controlCenterBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = controlCenterBounds;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(3, 7, 20);
            Opacity = 0d;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;

            canvas = new SplashCanvas(Environment.UserName)
            {
                Dock = DockStyle.Fill
            };
            Controls.Add(canvas);
            animationTimer.Tick += AnimationTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            phase = IntroPhase.FadeIn;
            phaseStartedUtc = DateTime.UtcNow;
            animationTimer.Start();
            canvas.Invalidate();
        }

        public void RevealMainAfterHold(int milliseconds)
        {
            holdMilliseconds = Math.Max(350, milliseconds);
            phase = IntroPhase.Hold;
            phaseStartedUtc = DateTime.UtcNow;
            animationTimer.Start();
            BringToFront();
        }

        private void AnimationTick(object sender, EventArgs e)
        {
            double elapsed = (DateTime.UtcNow - phaseStartedUtc).TotalMilliseconds;
            if (phase == IntroPhase.FadeIn)
            {
                double progress = Clamp01(elapsed / FadeInMilliseconds);
                Opacity = 1d - Math.Pow(1d - progress, 3d);
                if (progress >= 1d)
                {
                    Opacity = 1d;
                    phase = IntroPhase.WaitingForMain;
                    animationTimer.Stop();
                    EventHandler handler = FullyVisible;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
                return;
            }

            if (phase == IntroPhase.Hold)
            {
                if (elapsed >= holdMilliseconds)
                {
                    phase = IntroPhase.FadeOut;
                    phaseStartedUtc = DateTime.UtcNow;
                }
                return;
            }

            if (phase == IntroPhase.FadeOut)
            {
                double progress = Clamp01(elapsed / FadeOutMilliseconds);
                Opacity = Math.Pow(1d - progress, 2d);
                if (progress >= 1d)
                {
                    Opacity = 0d;
                    phase = IntroPhase.Complete;
                    animationTimer.Stop();
                    EventHandler handler = IntroCompleted;
                    if (handler != null)
                        handler(this, EventArgs.Empty);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animationTimer.Stop();
                animationTimer.Dispose();
                canvas.Dispose();
            }
            base.Dispose(disposing);
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0d, Math.Min(1d, value));
        }
    }

    internal sealed class SplashCanvas : Control
    {
        private const string CubeResource = "ScheduleIControlCenter.IntelDatabaseLogo.png";
        private const string WelcomeFontResource = "ScheduleIControlCenter.WelcomeFont.ttf";
        private readonly string greeting;
        private readonly Bitmap cube;
        private readonly PrivateFontCollection privateFonts = new PrivateFontCollection();
        private readonly byte[] privateFontBytes;
        private GCHandle privateFontHandle;
        private FontFamily welcomeFamily;

        public SplashCanvas(string desktopUserName)
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.FromArgb(3, 7, 20);
            greeting = "Welcome, " + (string.IsNullOrWhiteSpace(desktopUserName) ? "User" : desktopUserName.Trim());
            cube = LoadEmbeddedBitmap(CubeResource);
            privateFontBytes = LoadEmbeddedBytes(WelcomeFontResource);
            if (privateFontBytes != null && privateFontBytes.Length > 0)
            {
                privateFontHandle = GCHandle.Alloc(privateFontBytes, GCHandleType.Pinned);
                privateFonts.AddMemoryFont(privateFontHandle.AddrOfPinnedObject(), privateFontBytes.Length);
                if (privateFonts.Families.Length > 0)
                    welcomeFamily = privateFonts.Families[0];
            }
            if (welcomeFamily == null)
                welcomeFamily = new FontFamily("Segoe UI");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighQuality;

            Rectangle bounds = ClientRectangle;
            using (LinearGradientBrush background = new LinearGradientBrush(
                bounds,
                Color.FromArgb(5, 13, 34),
                Color.FromArgb(2, 5, 16),
                LinearGradientMode.Vertical))
            {
                graphics.FillRectangle(background, bounds);
            }

            DrawAmbientGrid(graphics, bounds);

            float scale = Math.Max(0.72f, Math.Min(1.18f, Math.Min(bounds.Width / 1450f, bounds.Height / 850f)));
            float centerX = bounds.Left + bounds.Width / 2f;
            float cubeSize = 190f * scale;
            float cubeCenterY = bounds.Top + bounds.Height * 0.39f;

            DrawRadialGlow(graphics, centerX, cubeCenterY + cubeSize * 0.12f, 420f * scale, 250f * scale);

            RectangleF cubeBounds = new RectangleF(
                centerX - cubeSize / 2f,
                cubeCenterY - cubeSize / 2f,
                cubeSize,
                cubeSize);
            graphics.DrawImage(cube, cubeBounds);

            float titleTop = cubeBounds.Bottom + 26f * scale;
            DrawBrandText(graphics, "Intelligence Database", centerX, titleTop, 30f * scale);

            float welcomeTop = titleTop + 72f * scale;
            DrawWelcomeText(graphics, greeting, centerX, welcomeTop, 31f * scale);

            using (Pen accent = new Pen(Color.FromArgb(90, 40, 144, 255), Math.Max(1f, 1.4f * scale)))
                graphics.DrawLine(accent, centerX - 118f * scale, welcomeTop + 54f * scale, centerX + 118f * scale, welcomeTop + 54f * scale);
        }

        private static void DrawAmbientGrid(Graphics graphics, Rectangle bounds)
        {
            const int spacing = 52;
            using (Pen grid = new Pen(Color.FromArgb(9, 86, 161, 255), 1f))
            {
                for (int x = bounds.Left; x < bounds.Right; x += spacing)
                    graphics.DrawLine(grid, x, bounds.Top, x, bounds.Bottom);
                for (int y = bounds.Top; y < bounds.Bottom; y += spacing)
                    graphics.DrawLine(grid, bounds.Left, y, bounds.Right, y);
            }

            using (LinearGradientBrush vignette = new LinearGradientBrush(
                bounds,
                Color.FromArgb(10, 3, 7, 20),
                Color.FromArgb(205, 2, 5, 16),
                LinearGradientMode.Vertical))
            {
                Blend blend = new Blend
                {
                    Positions = new[] { 0f, 0.48f, 1f },
                    Factors = new[] { 0.85f, 0.08f, 1f }
                };
                vignette.Blend = blend;
                graphics.FillRectangle(vignette, bounds);
            }
        }

        private static void DrawRadialGlow(Graphics graphics, float centerX, float centerY, float width, float height)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(centerX - width / 2f, centerY - height / 2f, width, height);
                using (PathGradientBrush glow = new PathGradientBrush(path))
                {
                    glow.CenterPoint = new PointF(centerX, centerY);
                    glow.CenterColor = Color.FromArgb(105, 32, 134, 255);
                    glow.SurroundColors = new[] { Color.FromArgb(0, 4, 10, 28) };
                    graphics.FillPath(glow, path);
                }
            }
        }

        private static void DrawBrandText(Graphics graphics, string text, float centerX, float top, float emSize)
        {
            // Match the public site's header wordmark with one clean native
            // Segoe UI 700 pass. Atmosphere belongs behind the letters; drawing
            // the glyphs repeatedly made their edges look doubled and muddy.
            using (Font font = new Font("Segoe UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                Size measured = TextRenderer.MeasureText(graphics, text, font, new Size(int.MaxValue, int.MaxValue), flags);
                Rectangle textBounds = new Rectangle(
                    (int)Math.Round(centerX - measured.Width / 2f),
                    (int)Math.Round(top),
                    measured.Width,
                    measured.Height);

                RectangleF hazeBounds = new RectangleF(
                    textBounds.Left - 42f,
                    textBounds.Top - 18f,
                    textBounds.Width + 84f,
                    textBounds.Height + 36f);
                using (GraphicsPath hazePath = new GraphicsPath())
                {
                    hazePath.AddEllipse(hazeBounds);
                    using (PathGradientBrush haze = new PathGradientBrush(hazePath))
                    {
                        haze.CenterColor = Color.FromArgb(25, 30, 103, 206);
                        haze.SurroundColors = new[] { Color.FromArgb(0, 30, 103, 206) };
                        graphics.FillPath(haze, hazePath);
                    }
                }

                TextRenderer.DrawText(graphics, text, font, textBounds, Color.FromArgb(30, 103, 206), flags);
            }
        }

        private void DrawWelcomeText(Graphics graphics, string text, float centerX, float top, float emSize)
        {
            using (GraphicsPath path = CenteredTextPath(text, welcomeFamily, FontStyle.Regular, emSize, centerX, top))
            {
                using (Pen glow = new Pen(Color.FromArgb(32, 69, 146, 255), Math.Max(3f, emSize * 0.18f)) { LineJoin = LineJoin.Round })
                    graphics.DrawPath(glow, path);
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(218, 229, 249)))
                    graphics.FillPath(fill, path);
            }
        }

        private static GraphicsPath CenteredTextPath(string text, FontFamily family, FontStyle style, float emSize, float centerX, float top)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddString(text, family, (int)style, emSize, new PointF(0f, top), StringFormat.GenericTypographic);
            RectangleF measured = path.GetBounds();
            using (Matrix translate = new Matrix())
            {
                translate.Translate(centerX - measured.Left - measured.Width / 2f, 0f);
                path.Transform(translate);
            }
            return path;
        }

        private static Bitmap LoadEmbeddedBitmap(string resourceName)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new InvalidOperationException("Missing splash image resource: " + resourceName);
                    using (Bitmap source = new Bitmap(stream))
                        return new Bitmap(source);
                }
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "splash.image-resource", DiagnosticCategory.Resource, DiagnosticSeverity.Fatal, "The splash image could not be loaded.", resourceName);
                throw;
            }
        }

        private static byte[] LoadEmbeddedBytes(string resourceName)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new InvalidOperationException("Missing splash font resource: " + resourceName);
                    byte[] bytes = new byte[stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                            throw new EndOfStreamException("Could not read the embedded splash font.");
                        offset += read;
                    }
                    return bytes;
                }
            }
            catch (Exception ex)
            {
                DiagnosticsService.Record(ex, "splash.font-resource", DiagnosticCategory.Resource, DiagnosticSeverity.Fatal, "The splash font could not be loaded.", resourceName);
                throw;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (cube != null)
                    cube.Dispose();
                privateFonts.Dispose();
                if (privateFontHandle.IsAllocated)
                    privateFontHandle.Free();
            }
            base.Dispose(disposing);
        }
    }
}
