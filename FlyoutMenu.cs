using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// A small right-click menu in the flyout's style, for what is too little for the flyout:
    /// removing one of her toys, or the tray's two ways out when there is no Madeline to show.
    /// Rows of an icon and a word, grey notes, and hairlines between groups.
    /// </summary>
    /// <remarks>
    /// Opens with its corner on the pointer like any context menu, flipping where it would not
    /// fit, and goes as soon as it loses the foreground or Esc is pressed. Topmost, since the
    /// toys it is opened on are.
    /// </remarks>
    internal sealed class FlyoutMenu : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUNDSMALL = 3, DWMWA_BORDER_COLOR = 34;
        const int CS_DROPSHADOW = 0x20000, WS_EX_TOOLWINDOW = 0x80;

        readonly FlyoutPalette p = FlyoutPalette.Current();
        readonly FlyoutStack stack;
        readonly Tween presence;
        bool closing;

        int Em => Font.Height;

        public FlyoutMenu()
        {
            Motion.Refresh();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            BackColor = p.Back;
            Padding = new Padding(Em / 4);
            stack = new FlyoutStack(p) { Font = Font, Location = new Point(Em / 4, Em / 4) };
            Controls.Add(stack);
            presence = new Tween(this, 0f, Ease.OutCubic)
            {
                Stepped = () =>
                {
                    double v = Math.Max(0.0, Math.Min(1.0, presence.Value));
                    Opacity = v >= 0.999 ? 1.0 : v;
                },
            };
        }

        /// <summary>A row: an icon, a word, and what choosing it does. The menu closes first.</summary>
        public FlyoutMenu Item(string glyph, string text, Action chosen)
        {
            var row = new Row(p, glyph, text);
            row.Chosen += () => { Close(); chosen(); };
            stack.Controls.Add(row);
            return this;
        }

        /// <summary>Grey text; <paramref name="path"/> for a folder, kept to one line.</summary>
        public FlyoutMenu Note(string text, bool path = false)
        {
            stack.Controls.Add(new FlyoutNote(p, text) { Path = path });
            return this;
        }

        public FlyoutMenu Separator()
        {
            stack.Controls.Add(new Line(p));
            return this;
        }

        /// <summary>Open with its corner on <paramref name="at"/>, as a context menu does.</summary>
        public void ShowAt(Point at)
        {
            // Shown see-through first and measured after: until the window is up, WinForms
            // reports every row in it as hidden, and a menu measured then has no rows at all.
            Opacity = 0;
            Location = at;
            Show();
            int widest = Em * 9;
            // As wide as the widest row, and as the notes would like up to the limit, so a note
            // does not squeeze into a column beside short rows.
            foreach (Control c in stack.Controls)
                if (c is Row row) widest = Math.Max(widest, row.NaturalWidth);
                else if (c is FlyoutNote note) widest = Math.Max(widest, Math.Min(note.NaturalWidth, Em * 24));
            stack.Width = Math.Min(widest, Em * 24);
            stack.Relayout();
            ClientSize = new Size(stack.Width + Em / 2, stack.Height + Em / 2);
            Rectangle work = Screen.FromPoint(at).WorkingArea;
            int x = at.X + Width <= work.Right ? at.X : at.X - Width;
            int y = at.Y + Height <= work.Bottom ? at.Y : at.Y - Height;
            Location = new Point(Math.Max(work.Left, Math.Min(x, work.Right - Width)),
                Math.Max(work.Top, Math.Min(y, work.Bottom - Height)));
            SetForegroundWindow(Handle);
            Activate();
            if (Motion.Enabled) presence.To(1f, 120f);
            else presence.Snap(1f);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = DWMWCP_ROUNDSMALL;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int border = p.Border.R | p.Border.G << 8 | p.Border.B << 16;
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!closing) BeginInvoke(new Action(Close));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            closing = true;
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            BeginInvoke(new Action(Dispose));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        }

        /// <summary>One choice: an icon and a word on a row that lights under the pointer.</summary>
        sealed class Row : FlyoutRow
        {
            readonly string glyph;
            Font icons;
            public event Action Chosen;

            public Row(FlyoutPalette palette, string glyph, string text) : base(palette)
            {
                this.glyph = glyph;
                Text = text;
                Cursor = Cursors.Hand;
            }

            public int NaturalWidth => TextRenderer.MeasureText(Text, Font).Width + Em * 3;

            public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 7 / 4);

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Chosen?.Invoke();
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Chosen?.Invoke(); e.Handled = true; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                if (HoverT.Value > 0f)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using GraphicsPath path = Rounded(ClientRectangle, Em / 5f);
                    using var fill = new SolidBrush(Ease.Blend(BackColor, P.Hover, HoverT.Value));
                    g.FillPath(fill, path);
                }
                icons ??= FlyoutTabs.IconFont(Font.Size);
                if (icons != null && !string.IsNullOrEmpty(glyph))
                    FlyoutTabs.DrawGlyph(g, glyph, icons, new RectangleF(Em / 3f, 0, Em, Height), P.Secondary);
                TextRenderer.DrawText(g, Text, Font, new Rectangle(Em * 5 / 3, 0, Width - Em * 2, Height), P.Text, Line);
                DrawFocus(g, ClientRectangle);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) icons?.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>A hairline between groups of rows.</summary>
        sealed class Line : Control
        {
            readonly FlyoutPalette p;

            public Line(FlyoutPalette palette)
            {
                p = palette;
                BackColor = palette.Back;
            }

            public override Size GetPreferredSize(Size proposed) => new Size(Width, Font.Height / 2);

            protected override void OnPaint(PaintEventArgs e)
            {
                using var pen = new Pen(p.Border);
                e.Graphics.DrawLine(pen, Font.Height / 3, Height / 2, Width - Font.Height / 3, Height / 2);
            }
        }
    }
}
