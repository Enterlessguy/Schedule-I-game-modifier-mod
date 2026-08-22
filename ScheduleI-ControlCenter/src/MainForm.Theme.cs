using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ScheduleIControlCenter
{
    internal sealed partial class MainForm
    {
        // Intelligence Database design system. These opaque WinForms colors are
        // the stable desktop equivalents of the site's translucent navy glass.
        private static readonly Color AppBackground = Color.FromArgb(5, 10, 25);
        private static readonly Color AppBackgroundDeep = Color.FromArgb(3, 6, 18);
        private static readonly Color Surface = Color.FromArgb(14, 20, 37);
        private static readonly Color SurfaceStrong = Color.FromArgb(18, 27, 49);
        private static readonly Color InputSurface = Color.FromArgb(8, 14, 30);
        private static readonly Color HeaderColor = Color.FromArgb(7, 13, 27);
        private static readonly Color Ink = Color.FromArgb(248, 250, 255);
        private static readonly Color Muted = Color.FromArgb(170, 179, 207);
        private static readonly Color Faint = Color.FromArgb(112, 124, 157);
        private static readonly Color Border = Color.FromArgb(39, 48, 72);
        private static readonly Color BorderStrong = Color.FromArgb(58, 82, 128);
        private static readonly Color RowAlt = Color.FromArgb(10, 17, 33);
        private static readonly Color Selection = Color.FromArgb(23, 54, 94);
        private static readonly Color Primary = Color.FromArgb(77, 166, 255);
        private static readonly Color PrimaryStrong = Color.FromArgb(30, 103, 206);
        private static readonly Color PrimaryHover = Color.FromArgb(105, 181, 255);
        private static readonly Color PrimarySoft = Color.FromArgb(24, 49, 82);
        private static readonly Color Success = Color.FromArgb(73, 209, 139);
        private static readonly Color Warning = Color.FromArgb(245, 185, 66);
        private static readonly Color Danger = Color.FromArgb(255, 101, 119);
        private static readonly Font UiFont = new Font("Segoe UI", 9.5F);
        private static readonly Font UiFontSemibold = new Font("Segoe UI Semibold", 9.5F);
        private static readonly Font TabFont = new Font("Segoe UI Semibold", 9.5F);
        private static readonly Font HeadingFont = new Font("Segoe UI Semibold", 18F);
        private static readonly Font CardTitleFont = new Font("Segoe UI Semibold", 11F);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string subAppName, string subIdList);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int enabled = 1;
                // 20 is the current immersive-dark-mode attribute; 19 covers
                // the older Windows 10 implementation. Corner preference 2
                // requests the standard modern rounded frame where supported.
                if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
                int rounded = 2;
                DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
            }
            catch
            {
                // The custom client theme remains complete on older Windows.
            }
        }

        private static void ApplyNativeDarkMode(Control root)
        {
            if (root == null)
                return;
            try
            {
                if (root is TextBoxBase || root is ListBox || root is ComboBox || root is DataGridView)
                    SetWindowTheme(root.Handle, "DarkMode_Explorer", null);
            }
            catch
            {
            }
            foreach (Control child in root.Controls)
                ApplyNativeDarkMode(child);
        }

        private static void ApplyButtonStyle(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Cursor = Cursors.Hand;
            button.Height = 38;
            button.Font = UiFontSemibold;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.UseVisualStyleBackColor = false;
            button.TabStop = true;
            if (primary)
            {
                button.BackColor = Primary;
                button.ForeColor = Ink;
                button.FlatAppearance.BorderColor = Primary;
                button.FlatAppearance.MouseOverBackColor = PrimaryHover;
                button.FlatAppearance.MouseDownBackColor = PrimaryStrong;
            }
            else
            {
                button.BackColor = SurfaceStrong;
                button.ForeColor = Ink;
                button.FlatAppearance.BorderColor = BorderStrong;
                button.FlatAppearance.MouseOverBackColor = PrimarySoft;
                button.FlatAppearance.MouseDownBackColor = Selection;
            }
            IntelButton intelButton = button as IntelButton;
            if (intelButton != null)
            {
                intelButton.IsPrimary = primary;
                intelButton.Invalidate();
            }
            else
            {
                EventHandler round = delegate { SetRoundedRegion(button, 9); };
                button.Resize -= round;
                button.Resize += round;
                SetRoundedRegion(button, 9);
            }
        }

        private static Panel MakeCard(string title, Control body)
        {
            Panel card = new IntelCardPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                Padding = new Padding(18, 12, 18, 14),
                Margin = new Padding(0)
            };

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Surface };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label heading = new Label
            {
                Text = title,
                Font = CardTitleFont,
                ForeColor = Ink,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(body, 0, 1);
            card.Controls.Add(layout);
            return card;
        }

        private static void ApplyGridTheme(DataGridView grid)
        {
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.GridColor = Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceStrong;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.Font = UiFontSemibold;
            grid.ColumnHeadersHeight = 42;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.DefaultCellStyle.Font = UiFont;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Selection;
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlt;
            grid.RowTemplate.Height = 38;
            grid.DefaultCellStyle.Padding = new Padding(9, 3, 9, 3);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 4, 10, 4);
            grid.CellPainting -= GridCellPainting;
            grid.CellPainting += GridCellPainting;
        }

        private static void GridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1)
                return;
            DataGridView grid = sender as DataGridView;
            if (grid == null)
                return;
            using (SolidBrush background = new SolidBrush(SurfaceStrong))
                e.Graphics.FillRectangle(background, e.CellBounds);
            using (Pen bottom = new Pen(BorderStrong))
                e.Graphics.DrawLine(bottom, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            if (e.ColumnIndex > 0)
                using (Pen separator = new Pen(Border))
                    e.Graphics.DrawLine(separator, e.CellBounds.Left, e.CellBounds.Top + 10, e.CellBounds.Left, e.CellBounds.Bottom - 10);
            string text = Convert.ToString(e.FormattedValue);
            Rectangle textBounds = new Rectangle(e.CellBounds.X + 11, e.CellBounds.Y, Math.Max(1, e.CellBounds.Width - 18), e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, text, UiFontSemibold, textBounds, Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.Handled = true;
        }

        private static void ConfigureNavigationTabs(TabControl tabs)
        {
            // Top-level navigation is driven by the purpose-grouped sidebar.
            // A one-pixel tab header keeps the dependable TabControl page host
            // without exposing the legacy strip.
            tabs.Appearance = TabAppearance.FlatButtons;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(0, 1);
            tabs.Padding = Point.Empty;
            tabs.BackColor = AppBackground;
            tabs.Font = TabFont;
        }

        private static void ConfigureSectionTabs(TabControl tabs)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.Appearance = TabAppearance.Normal;
            tabs.SizeMode = TabSizeMode.Fixed;
            tabs.ItemSize = new Size(176, 40);
            tabs.Padding = new Point(12, 5);
            tabs.Font = TabFont;
            tabs.BackColor = AppBackground;
            tabs.DrawItem += SectionTabsDrawItem;
        }

        private static void SectionTabsDrawItem(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            Rectangle bounds = tabs.GetTabRect(e.Index);
            bool selected = e.Index == tabs.SelectedIndex;
            using (SolidBrush background = new SolidBrush(selected ? SurfaceStrong : AppBackground))
                e.Graphics.FillRectangle(background, bounds);
            if (selected)
                using (SolidBrush accent = new SolidBrush(Primary))
                    e.Graphics.FillRectangle(accent, bounds.Left + 14, bounds.Bottom - 3, Math.Max(6, bounds.Width - 28), 3);
            TextRenderer.DrawText(
                e.Graphics,
                tabs.TabPages[e.Index].Text,
                TabFont,
                bounds,
                selected ? Ink : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static void ConfigureHeaderStatus(Label label, string text)
        {
            label.Text = text;
            label.AutoSize = false;
            label.Width = 110;
            label.Height = 28;
            label.ForeColor = Muted;
            label.Font = new Font("Segoe UI Semibold", 7.5F);
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Margin = new Padding(5, 4, 0, 4);
        }

        private static void SetRoundedRegion(Control control, int radius)
        {
            if (control == null || control.Width <= 1 || control.Height <= 1)
                return;
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius))
            {
                Region old = control.Region;
                control.Region = new Region(path);
                if (old != null)
                    old.Dispose();
            }
        }

        private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            Rectangle arc = new Rectangle(rectangle.X, rectangle.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rectangle.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class IntelButton : Button
        {
            private bool hovered;
            private bool pressed;

            public bool IsPrimary { get; set; }

            public IntelButton()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
            }

            protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color canvas = Parent == null ? AppBackground : Parent.BackColor;
                e.Graphics.Clear(canvas);
                Rectangle bounds = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
                Color top;
                Color bottom;
                Color textColor;
                if (!Enabled)
                {
                    top = Color.FromArgb(18, 25, 43);
                    bottom = Color.FromArgb(13, 19, 34);
                    textColor = Color.FromArgb(93, 105, 136);
                }
                else if (IsPrimary)
                {
                    top = pressed ? PrimaryStrong : hovered ? Color.FromArgb(111, 190, 255) : Color.FromArgb(82, 171, 255);
                    bottom = pressed ? Color.FromArgb(23, 82, 177) : hovered ? Color.FromArgb(47, 126, 232) : Color.FromArgb(35, 105, 211);
                    textColor = Ink;
                }
                else
                {
                    Color idle = BackColor.IsEmpty ? SurfaceStrong : BackColor;
                    Color hover = FlatAppearance.MouseOverBackColor.IsEmpty ? Color.FromArgb(26, 42, 70) : FlatAppearance.MouseOverBackColor;
                    Color down = FlatAppearance.MouseDownBackColor.IsEmpty ? Selection : FlatAppearance.MouseDownBackColor;
                    top = pressed ? down : hovered ? hover : idle;
                    bottom = Darken(top, pressed ? 0.72F : 0.82F);
                    textColor = Enabled ? ForeColor : Faint;
                }

                using (GraphicsPath path = RoundedPath(bounds, 10))
                using (LinearGradientBrush fill = new LinearGradientBrush(bounds, top, bottom, LinearGradientMode.Vertical))
                {
                    e.Graphics.FillPath(fill, path);
                    if (IsPrimary || FlatAppearance.BorderSize > 0)
                    {
                        Color borderColor = IsPrimary ? Color.FromArgb(100, PrimaryHover) : hovered ? BorderStrong : FlatAppearance.BorderColor;
                        using (Pen border = new Pen(borderColor.IsEmpty ? Border : borderColor))
                            e.Graphics.DrawPath(border, path);
                    }
                    if (Focused && ShowFocusCues)
                    {
                        Rectangle focusBounds = Rectangle.Inflate(bounds, -3, -3);
                        using (GraphicsPath focus = RoundedPath(focusBounds, 7))
                        using (Pen focusPen = new Pen(Color.FromArgb(150, PrimaryHover)))
                            e.Graphics.DrawPath(focusPen, focus);
                    }
                }

                TextRenderer.DrawText(e.Graphics, Text, Font, bounds, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (UseMnemonic ? TextFormatFlags.NoPadding : TextFormatFlags.NoPrefix));
            }

            private static Color Darken(Color color, float factor)
            {
                return Color.FromArgb(color.A,
                    Math.Max(0, Math.Min(255, (int)(color.R * factor))),
                    Math.Max(0, Math.Min(255, (int)(color.G * factor))),
                    Math.Max(0, Math.Min(255, (int)(color.B * factor))));
            }
        }

        private sealed class IntelComboBox : ComboBox
        {
            private const int WmPaint = 0x000F;
            private bool hovered;

            public IntelComboBox()
            {
                DrawMode = DrawMode.OwnerDrawFixed;
                ItemHeight = 28;
                FlatStyle = FlatStyle.Flat;
                BackColor = InputSurface;
                ForeColor = Ink;
            }

            protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }

            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                if (e.Index < 0)
                    return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush fill = new SolidBrush(selected ? Selection : InputSurface))
                    e.Graphics.FillRectangle(fill, e.Bounds);
                string text = Convert.ToString(Items[e.Index]);
                Rectangle textBounds = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(1, e.Bounds.Width - 18), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, text, Font, textBounds, selected ? Ink : Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg != WmPaint || DropDownStyle != ComboBoxStyle.DropDownList || !IsHandleCreated)
                    return;
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                    using (SolidBrush background = new SolidBrush(InputSurface))
                        graphics.FillRectangle(background, bounds);
                    using (Pen border = new Pen(hovered || Focused ? BorderStrong : Border))
                        graphics.DrawRectangle(border, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                    string text = SelectedIndex >= 0 ? Convert.ToString(SelectedItem) : Text;
                    Rectangle textBounds = new Rectangle(10, 0, Math.Max(1, Width - 40), Height);
                    TextRenderer.DrawText(graphics, text, Font, textBounds, Enabled ? Ink : Faint,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    int centerX = Width - 17;
                    int centerY = Height / 2;
                    using (Pen chevron = new Pen(Enabled ? Muted : Faint, 1.6F))
                    {
                        graphics.DrawLine(chevron, centerX - 4, centerY - 2, centerX, centerY + 2);
                        graphics.DrawLine(chevron, centerX, centerY + 2, centerX + 4, centerY - 2);
                    }
                }
            }
        }

        private sealed class IntelNumericUpDown : NumericUpDown
        {
            private const int WmPaint = 0x000F;
            private const int StepperWidth = 34;
            private readonly IntelStepperOverlay stepper;

            public IntelNumericUpDown()
            {
                AutoSize = false;
                BorderStyle = BorderStyle.None;
                BackColor = InputSurface;
                ForeColor = Ink;
                MinimumSize = new Size(96, 38);
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
                stepper = new IntelStepperOverlay(this);
                Controls.Add(stepper);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                RestyleChildren();
            }

            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                RestyleChildren();
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                RestyleChildren();
                Invalidate(true);
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                RestyleChildren();
            }

            private void RestyleChildren()
            {
                // UpDownBase performs layout from its own constructor, before this
                // subclass has initialized the themed overlay.
                if (stepper == null)
                    return;

                foreach (Control child in Controls)
                {
                    if (ReferenceEquals(child, stepper))
                        continue;
                    child.BackColor = Enabled ? InputSurface : SurfaceStrong;
                    child.ForeColor = Enabled ? Ink : Faint;
                    if (child is TextBox)
                    {
                        child.Visible = true;
                        TextBox textBox = (TextBox)child;
                        textBox.BorderStyle = BorderStyle.None;
                        textBox.Multiline = false;
                        textBox.TextAlign = HorizontalAlignment.Left;
                        textBox.Font = Font;
                        int editHeight = Math.Min(textBox.PreferredHeight, Math.Max(1, Height - 8));
                        int editTop = Math.Max(1, (Height - editHeight) / 2);
                        textBox.SetBounds(12, editTop, Math.Max(1, Width - StepperWidth - 18), editHeight);
                    }
                    else
                    {
                        // UpDownBase owns a native buttons child which may repaint itself after the
                        // parent. Hide it, then cover its complete bounds with our topmost overlay.
                        child.Visible = false;
                    }
                }
                stepper.Enabled = Enabled;
                stepper.SetBounds(Math.Max(0, Width - StepperWidth - 1), 1, StepperWidth, Math.Max(1, Height - 2));
                stepper.Visible = true;
                stepper.BringToFront();
                stepper.Invalidate();
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg != WmPaint || !IsHandleCreated)
                    return;
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                    using (SolidBrush surface = new SolidBrush(Enabled ? InputSurface : SurfaceStrong))
                        graphics.FillRectangle(surface, bounds);
                    using (Pen border = new Pen(Focused ? Primary : Border))
                        graphics.DrawRectangle(border, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                }
            }

            private sealed class IntelStepperOverlay : Control
            {
                private readonly IntelNumericUpDown owner;
                private int hotHalf;
                private int pressedHalf;

                public IntelStepperOverlay(IntelNumericUpDown owner)
                {
                    this.owner = owner;
                    TabStop = false;
                    Cursor = Cursors.Hand;
                    SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                        ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                }

                protected override void OnMouseMove(MouseEventArgs e)
                {
                    int next = e.Y < Height / 2 ? 1 : 2;
                    if (hotHalf != next)
                    {
                        hotHalf = next;
                        Invalidate();
                    }
                    base.OnMouseMove(e);
                }

                protected override void OnMouseLeave(EventArgs e)
                {
                    hotHalf = 0;
                    pressedHalf = 0;
                    Invalidate();
                    base.OnMouseLeave(e);
                }

                protected override void OnMouseDown(MouseEventArgs e)
                {
                    if (Enabled && e.Button == MouseButtons.Left)
                    {
                        pressedHalf = e.Y < Height / 2 ? 1 : 2;
                        owner.Focus();
                        if (pressedHalf == 1)
                            owner.UpButton();
                        else
                            owner.DownButton();
                        Invalidate();
                    }
                    base.OnMouseDown(e);
                }

                protected override void OnMouseUp(MouseEventArgs e)
                {
                    pressedHalf = 0;
                    Invalidate();
                    base.OnMouseUp(e);
                }

                protected override void OnPaint(PaintEventArgs e)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    Color baseFill = Enabled ? Color.FromArgb(18, 31, 55) : Color.FromArgb(12, 18, 32);
                    using (SolidBrush fill = new SolidBrush(baseFill))
                        e.Graphics.FillRectangle(fill, ClientRectangle);

                    PaintHalf(e.Graphics, new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height / 2 - 1)), 1);
                    PaintHalf(e.Graphics, new Rectangle(1, Height / 2, Math.Max(1, Width - 2), Math.Max(1, Height - Height / 2 - 1)), 2);

                    using (Pen divider = new Pen(owner.Focused ? BorderStrong : Border))
                    {
                        e.Graphics.DrawLine(divider, 0, 2, 0, Math.Max(2, Height - 3));
                        e.Graphics.DrawLine(divider, 5, Height / 2, Math.Max(5, Width - 5), Height / 2);
                    }

                    Color arrowColor = Enabled ? (hotHalf == 0 ? Muted : PrimaryHover) : Faint;
                    int centerX = Width / 2;
                    int upperY = Height / 4;
                    int lowerY = Height * 3 / 4;
                    using (Pen arrow = new Pen(arrowColor, 1.7F))
                    {
                        arrow.StartCap = LineCap.Round;
                        arrow.EndCap = LineCap.Round;
                        e.Graphics.DrawLine(arrow, centerX - 4, upperY + 2, centerX, upperY - 2);
                        e.Graphics.DrawLine(arrow, centerX, upperY - 2, centerX + 4, upperY + 2);
                        e.Graphics.DrawLine(arrow, centerX - 4, lowerY - 2, centerX, lowerY + 2);
                        e.Graphics.DrawLine(arrow, centerX, lowerY + 2, centerX + 4, lowerY - 2);
                    }
                }

                private void PaintHalf(Graphics graphics, Rectangle bounds, int half)
                {
                    if (!Enabled || (hotHalf != half && pressedHalf != half))
                        return;
                    Color tint = pressedHalf == half ? Color.FromArgb(39, 76, 128) : Color.FromArgb(28, 54, 91);
                    using (SolidBrush fill = new SolidBrush(tint))
                        graphics.FillRectangle(fill, bounds);
                }
            }
        }

        private sealed class IntelTextBox : TextBox
        {
            private const int WmPaint = 0x000F;

            public IntelTextBox()
            {
                BorderStyle = BorderStyle.None;
                BackColor = InputSurface;
                ForeColor = Ink;
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg != WmPaint || !IsHandleCreated)
                    return;
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                using (Pen border = new Pen(Focused ? BorderStrong : Border))
                    graphics.DrawRectangle(border, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            }
        }

        private sealed class IntelListBox : ListBox
        {
            public IntelListBox()
            {
                DrawMode = DrawMode.OwnerDrawFixed;
                BorderStyle = BorderStyle.None;
                ItemHeight = 31;
                IntegralHeight = false;
                BackColor = InputSurface;
                ForeColor = Ink;
            }

            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                if (e.Index < 0)
                    return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush canvas = new SolidBrush(InputSurface))
                    e.Graphics.FillRectangle(canvas, e.Bounds);
                Rectangle itemBounds = Rectangle.Inflate(e.Bounds, -3, -2);
                if (selected)
                {
                    using (GraphicsPath path = RoundedPath(itemBounds, 7))
                    using (SolidBrush selection = new SolidBrush(PrimarySoft))
                        e.Graphics.FillPath(selection, path);
                    using (SolidBrush accent = new SolidBrush(Primary))
                        e.Graphics.FillRectangle(accent, itemBounds.X, itemBounds.Y + 5, 3, Math.Max(3, itemBounds.Height - 10));
                }
                Rectangle textBounds = new Rectangle(itemBounds.X + 12, itemBounds.Y, Math.Max(1, itemBounds.Width - 18), itemBounds.Height);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(Items[e.Index]), Font, textBounds, selected ? PrimaryHover : Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class IntelDataGridView : DataGridView
        {
            public IntelDataGridView()
            {
                DoubleBuffered = true;
            }
        }

        private sealed class IntelCheckBox : CheckBox
        {
            private bool hovered;

            public IntelCheckBox()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                FlatStyle = FlatStyle.Flat;
            }

            protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color canvas = Parent == null ? Surface : Parent.BackColor;
                e.Graphics.Clear(canvas);
                Rectangle box = new Rectangle(2, Math.Max(2, (Height - 18) / 2), 17, 17);
                using (GraphicsPath path = RoundedPath(box, 5))
                using (SolidBrush fill = new SolidBrush(Checked ? PrimaryStrong : InputSurface))
                using (Pen border = new Pen(hovered || Focused ? Primary : BorderStrong))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
                if (Checked)
                {
                    using (Pen check = new Pen(Ink, 2F))
                    {
                        check.StartCap = LineCap.Round;
                        check.EndCap = LineCap.Round;
                        e.Graphics.DrawLines(check, new[] { new Point(6, box.Y + 9), new Point(10, box.Y + 13), new Point(16, box.Y + 5) });
                    }
                }
                Rectangle textBounds = new Rectangle(27, 0, Math.Max(1, Width - 29), Height);
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : Faint,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class IntelTrackBar : Control
        {
            private int minimum;
            private int maximum = 10;
            private int currentValue;
            private bool dragging;

            public int Minimum
            {
                get { return minimum; }
                set { minimum = value; if (maximum < minimum) maximum = minimum; Value = currentValue; Invalidate(); }
            }

            public int Maximum
            {
                get { return maximum; }
                set { maximum = Math.Max(minimum, value); Value = currentValue; Invalidate(); }
            }

            public int Value
            {
                get { return currentValue; }
                set
                {
                    int bounded = Math.Max(minimum, Math.Min(maximum, value));
                    if (currentValue == bounded) return;
                    currentValue = bounded;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }

            public int TickFrequency { get; set; } = 1;
            public int LargeChange { get; set; } = 1;
            public int SmallChange { get; set; } = 1;
            public event EventHandler ValueChanged;

            public IntelTrackBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                BackColor = Surface;
                TabStop = true;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (Enabled && e.Button == MouseButtons.Left)
                {
                    Focus();
                    dragging = true;
                    Capture = true;
                    SetValueFromX(e.X);
                }
                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (dragging) SetValueFromX(e.X);
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                dragging = false;
                Capture = false;
                base.OnMouseUp(e);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { Value -= Math.Max(1, SmallChange); e.Handled = true; }
                else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { Value += Math.Max(1, SmallChange); e.Handled = true; }
                else if (e.KeyCode == Keys.PageDown) { Value -= Math.Max(1, LargeChange); e.Handled = true; }
                else if (e.KeyCode == Keys.PageUp) { Value += Math.Max(1, LargeChange); e.Handled = true; }
                else if (e.KeyCode == Keys.Home) { Value = Minimum; e.Handled = true; }
                else if (e.KeyCode == Keys.End) { Value = Maximum; e.Handled = true; }
                base.OnKeyDown(e);
            }

            private void SetValueFromX(int x)
            {
                int left = 10;
                int right = Math.Max(left + 1, Width - 12);
                float ratio = Math.Max(0F, Math.Min(1F, (x - left) / (float)(right - left)));
                Value = Minimum + (int)Math.Round((Maximum - Minimum) * ratio);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Surface);
                int left = 10;
                int right = Math.Max(left + 1, Width - 12);
                int trackY = Math.Max(16, Height / 2 - 8);
                Rectangle track = new Rectangle(left, trackY, right - left, 5);
                using (GraphicsPath path = RoundedPath(track, 3))
                using (SolidBrush rail = new SolidBrush(BorderStrong))
                    e.Graphics.FillPath(rail, path);
                float ratio = Maximum == Minimum ? 0F : (Value - Minimum) / (float)(Maximum - Minimum);
                int thumbX = left + (int)Math.Round((right - left) * ratio);
                Rectangle fillBounds = new Rectangle(left, trackY, Math.Max(3, thumbX - left), 5);
                using (GraphicsPath fillPath = RoundedPath(fillBounds, 3))
                using (LinearGradientBrush fill = new LinearGradientBrush(fillBounds, PrimaryHover, PrimaryStrong, LinearGradientMode.Horizontal))
                    e.Graphics.FillPath(fill, fillPath);
                for (int tick = Minimum; tick <= Maximum; tick += Math.Max(1, TickFrequency))
                {
                    float tickRatio = Maximum == Minimum ? 0F : (tick - Minimum) / (float)(Maximum - Minimum);
                    int tickX = left + (int)Math.Round((right - left) * tickRatio);
                    using (SolidBrush dot = new SolidBrush(tick <= Value ? Primary : Faint))
                        e.Graphics.FillEllipse(dot, tickX - 1, Math.Min(Height - 5, trackY + 16), 3, 3);
                }
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(54, Primary)))
                    e.Graphics.FillEllipse(glow, thumbX - 10, trackY - 8, 20, 20);
                using (LinearGradientBrush thumb = new LinearGradientBrush(new Rectangle(thumbX - 7, trackY - 6, 14, 17), PrimaryHover, PrimaryStrong, LinearGradientMode.Vertical))
                    e.Graphics.FillEllipse(thumb, thumbX - 7, trackY - 6, 14, 17);
                using (Pen edge = new Pen(Color.FromArgb(180, PrimaryHover)))
                    e.Graphics.DrawEllipse(edge, thumbX - 7, trackY - 6, 14, 17);
            }
        }

        private sealed class IntelGroupBox : GroupBox
        {
            public IntelGroupBox()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = AppBackground;
                ForeColor = Ink;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle surface = new Rectangle(1, 8, Math.Max(1, Width - 3), Math.Max(1, Height - 10));
                using (GraphicsPath path = RoundedPath(surface, 14))
                using (SolidBrush fill = new SolidBrush(Surface))
                using (Pen border = new Pen(Border))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
                TextRenderer.DrawText(e.Graphics, Text, CardTitleFont, new Rectangle(18, 11, Math.Max(1, Width - 36), 24), Ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private sealed class IntelCardPanel : Panel
        {
            public IntelCardPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = Surface;
                Resize += delegate { SetRoundedRegion(this, 14); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 14))
                using (Pen pen = new Pen(Border))
                    e.Graphics.DrawPath(pen, path);
            }
        }

        private sealed class IntelNetworkPanel : Panel
        {
            public IntelNetworkPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = SurfaceStrong;
                Resize += delegate { SetRoundedRegion(this, 18); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                using (LinearGradientBrush fill = new LinearGradientBrush(bounds, SurfaceStrong, Surface, 90F))
                using (GraphicsPath path = RoundedPath(bounds, 18))
                {
                    e.Graphics.FillPath(fill, path);
                    using (Pen edge = new Pen(BorderStrong))
                        e.Graphics.DrawPath(edge, path);
                }
            }
        }

        private sealed class IntelStatusChip : Label
        {
            public IntelStatusChip()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color tint = ForeColor == Success ? Color.FromArgb(23, 62, 58)
                    : ForeColor == Warning ? Color.FromArgb(64, 47, 25)
                    : ForeColor == Danger ? Color.FromArgb(63, 29, 43)
                    : SurfaceStrong;
                using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), 12))
                using (SolidBrush fill = new SolidBrush(tint))
                using (Pen border = new Pen(Color.FromArgb(90, ForeColor)))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        private sealed class BorderlessTabControl : TabControl
        {
            private const int TcmAdjustRect = 0x1328;

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == TcmAdjustRect && !DesignMode)
                {
                    message.Result = (IntPtr)1;
                    return;
                }
                base.WndProc(ref message);
            }
        }

        private sealed class IntelCubeMark : Control
        {
            private static readonly Image WebsiteCube = LoadWebsiteCube();

            public IntelCubeMark()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                MinimumSize = new Size(34, 34);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (WebsiteCube != null)
                {
                    e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    int side = Math.Min(Width, Height);
                    Rectangle target = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
                    e.Graphics.DrawImage(WebsiteCube, target);
                    return;
                }
                using (SolidBrush fallback = new SolidBrush(Primary))
                    e.Graphics.FillRectangle(fallback, ClientRectangle);
            }

            private static Image LoadWebsiteCube()
            {
                try
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScheduleIControlCenter.IntelDatabaseLogo.png"))
                    {
                        if (stream == null)
                            return null;
                        using (Image image = Image.FromStream(stream))
                            return new Bitmap(image);
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        private sealed class IntelGlowLabel : Control
        {
            public IntelGlowLabel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                ForeColor = Primary;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                StringFormat format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                RectangleF bounds = new RectangleF(1, 0, Math.Max(1, Width - 2), Height);
                using (SolidBrush glowWide = new SolidBrush(Color.FromArgb(24, 18, 103, 255)))
                using (SolidBrush glowNear = new SolidBrush(Color.FromArgb(48, 52, 146, 255)))
                {
                    foreach (Point offset in new[] { new Point(-2, 0), new Point(2, 0), new Point(0, -2), new Point(0, 2) })
                        e.Graphics.DrawString(Text, Font, glowWide, new RectangleF(bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height), format);
                    foreach (Point offset in new[] { new Point(-1, 0), new Point(1, 0), new Point(0, -1), new Point(0, 1) })
                        e.Graphics.DrawString(Text, Font, glowNear, new RectangleF(bounds.X + offset.X, bounds.Y + offset.Y, bounds.Width, bounds.Height), format);
                }
                using (LinearGradientBrush text = new LinearGradientBrush(bounds, Color.FromArgb(91, 174, 255), Color.FromArgb(24, 105, 231), LinearGradientMode.Horizontal))
                    e.Graphics.DrawString(Text, Font, text, bounds, format);
                format.Dispose();
            }
        }

        private void DumpLayout(string path)
        {
            TryMarkDump(Path.Combine(Path.GetTempPath(), "layout-dump-start.txt"), "start");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Schedule I Control Center layout dump");
            sb.AppendLine("Form bounds: " + Bounds + " display: " + DisplayRectangle);
            sb.AppendLine("Dpi: " + DeviceDpi);
            for (int i = 0; i < navigation.TabPages.Count; i++)
            {
                navigation.SelectedIndex = i;
                Application.DoEvents();
                TabPage page = navigation.TabPages[i];
                sb.AppendLine("=== Tab " + i + ": " + page.Text + " ===");
                sb.AppendLine("TabPage bounds: " + page.Bounds + " display: " + page.DisplayRectangle);
                DumpControl(page, sb, 1);
            }
            sb.AppendLine("=== Header ===");
            DumpControl(Controls[0], sb, 1);
            sb.AppendLine("=== END ===");
            string content = sb.ToString();
            string tempPath = Path.Combine(Path.GetTempPath(), "layout-dump.txt");
            File.WriteAllText(tempPath, content, new UTF8Encoding(false));
            TryMarkDump(Path.Combine(Path.GetTempPath(), "layout-dump-done.txt"), "done");
        }

        private static void TryMarkDump(string path, string label)
        {
            try
            {
                File.WriteAllText(path, label + " " + DateTime.Now.ToString("HH:mm:ss.fff"), new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static void DumpControl(Control control, StringBuilder sb, int depth)
        {
            string indent = new string(' ', depth * 2);
            string text = (control.Text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 48)
                text = text.Substring(0, 48);
            sb.AppendLine(string.Format(
                "{0}{1} '{2}' Bounds={3} Dock={4} Vis={5} Auto={6}",
                indent,
                control.GetType().Name,
                text,
                control.Bounds,
                control.Dock,
                control.Visible,
                control.AutoSize));
            DataGridView grid = control as DataGridView;
            if (grid != null)
            {
                string widths = string.Join(",", grid.Columns.Cast<DataGridViewColumn>().Select(column => column.Width.ToString()));
                int totalColumnWidth = grid.Columns.Cast<DataGridViewColumn>().Sum(column => column.Width);
                int clientWidth = grid.ClientSize.Width;
                sb.AppendLine(string.Format(
                    "{0}  GRID rows={1} cols={2} widths=[{3}] clientW={4} colsOverflow={5}",
                    indent,
                    grid.Rows.Count,
                    grid.Columns.Count,
                    widths,
                    clientWidth,
                    totalColumnWidth > clientWidth));
            }
            foreach (Control child in control.Controls)
                DumpControl(child, sb, depth + 1);
        }
    }
}
