using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// The right-click menu: a panel that opens where it was asked for and stays until it is sent
    /// away. A row of tabs across the top, one short page under it, and a strip along the bottom
    /// for About and the way out.
    /// Pages hold only what is done in a click -- a toggle, a choice, an action; anything that
    /// takes more than that opens a window of its own.
    /// </summary>
    /// <remarks>
    /// Closes on Esc, or when a window of another program takes the foreground. Every window of
    /// this one -- Madeline, her entities, the dialogs a page opens -- keeps it open, so a
    /// setting can be changed and tried on her straight away. Foreground is polled rather than
    /// taken from Deactivate, which does not come again once she has been clicked.
    ///
    /// Its height follows the page on show; a page taller than the screen allows scrolls, and
    /// remembers where it was scrolled to. It is not topmost, so Madeline stays in front of it.
    /// </remarks>
    internal sealed class PetFlyout : Form, IFlyoutHost
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hwnd);
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2, DWMWA_BORDER_COLOR = 34;
        const int CS_DROPSHADOW = 0x20000, WS_EX_TOOLWINDOW = 0x80;

        public readonly FlyoutPalette P = FlyoutPalette.Current();
        readonly Point anchor;
        readonly Rectangle? previous;
        readonly FlyoutTabs tabs;
        readonly Panel scroller, top, body;
        readonly FlyoutFooter footer;
        readonly List<FlyoutStack> pages = new List<FlyoutStack>();
        readonly List<Point> scrolls = new List<Point>();
        readonly Timer watch = new Timer { Interval = 100 };
        readonly uint processId = (uint)Environment.ProcessId;
        bool seenForeground, placed, fromBottom;
        // The edge that sits on the anchor when it opened upward: that one stays put.
        int anchoredBottom;

        public event Action<int> TabChanged;

        int Em => Font.Height;
        public int RowWidth => Em * 20;

        /// <param name="anchor">Where the menu was asked for.</param>
        /// <param name="previous">The bounds of the flyout this one replaces, to open exactly there.</param>
        public PetFlyout(Point anchor, Rectangle? previous = null, bool previousFromBottom = false)
        {
            this.anchor = anchor;
            this.previous = previous;
            fromBottom = previousFromBottom;

            Text = Loc.T("App.Title");
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            // Not topmost: Madeline and her entities are, and they belong in front of it.
            TopMost = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            BackColor = P.Back;

            tabs = new FlyoutTabs(P, Font) { Dock = DockStyle.Top };
            tabs.Height = tabs.GetPreferredSize(Size.Empty).Height;
            tabs.Changed += ShowPage;
            top = new Panel { Dock = DockStyle.Top, Height = Em * 3 / 4, BackColor = P.Back };
            scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = P.Back };
            // The tabs and pages sit inside a margin; the footer runs edge to edge under them.
            body = new Panel { Dock = DockStyle.Fill, BackColor = P.Back, Padding = new Padding(Em / 2) };
            body.Controls.Add(scroller);
            body.Controls.Add(top);
            body.Controls.Add(tabs);
            footer = new FlyoutFooter(P) { Dock = DockStyle.Bottom, Font = Font };
            footer.Height = footer.GetPreferredSize(Size.Empty).Height;

            Controls.Add(body);
            Controls.Add(footer);

            watch.Tick += (_, _) => Watch();
        }

        /// <summary>An item in the strip along the bottom, under every page.</summary>
        public void AddFooterItem(string glyph, string text, Action pressed, bool right = false)
            => footer.Add(glyph, text, pressed, right);

        /// <summary>Whether it opened upward from its anchor.</summary>
        public bool FromBottom => fromBottom;

        public int Tab => tabs.Selected;

        /// <summary>A new tab, and the page to fill for it.</summary>
        public FlyoutStack AddPage(string title, string glyph, string full = null)
        {
            tabs.Add(title, glyph, full);
            var page = new FlyoutStack(P) { Width = RowWidth, Font = Font };
            pages.Add(page);
            scrolls.Add(Point.Empty);
            return page;
        }

        /// <summary>
        /// A row that folds <paramref name="body"/> out beneath it, open or shut as it was last
        /// left: <paramref name="open"/> holds the keys of the open ones and is kept up to date.
        /// </summary>
        public FlyoutStack Expander(FlyoutStack page, string key, string text, ISet<string> open, Action changed)
        {
            var body = new FlyoutStack(P) { Width = RowWidth };
            var row = page.Add(new FlyoutExpander(P, text, body, open.Contains(key)));
            page.Add(body);
            row.Toggled += () =>
            {
                if (row.Expanded) open.Add(key); else open.Remove(key);
                changed();
            };
            return body;
        }

        public void SelectPage(int index)
        {
            tabs.Selected = index;
            if (IsHandleCreated) ShowPage(tabs.Selected);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_TOOLWINDOW;   // out of Alt+Tab, like a menu
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int border = P.Border.R | P.Border.G << 8 | P.Border.B << 16;
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ShowPage(tabs.Selected);
            watch.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetForegroundWindow(Handle);
            Activate();
            tabs.Focus();
        }

        void ShowPage(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            if (scroller.Controls.Count > 0 && scroller.Controls[0] is FlyoutStack shown)
            {
                int was = pages.IndexOf(shown);
                if (was >= 0) scrolls[was] = new Point(-scroller.AutoScrollPosition.X, -scroller.AutoScrollPosition.Y);
            }
            scroller.SuspendLayout();
            scroller.Controls.Clear();
            scroller.Controls.Add(pages[index]);
            scroller.ResumeLayout();
            Refit();
            scroller.AutoScrollPosition = scrolls[index];
            TabChanged?.Invoke(index);
        }

        /// <summary>
        /// Size to the page on show, up to the screen, and sit where a right-click menu would:
        /// the corner nearest the pointer on the pointer, flipped left or up where the menu
        /// would not fit. That corner stays put while pages of different heights come and go.
        /// </summary>
        public void Refit()
        {
            foreach (FlyoutStack page in pages)
            {
                page.Width = RowWidth;
                page.Relayout();
            }
            Rectangle work = Screen.FromPoint(previous?.Location ?? anchor).WorkingArea;
            int chrome = body.Padding.Vertical + tabs.Height + top.Height + footer.Height;
            int current = pages.Count > 0 && tabs.Selected < pages.Count ? pages[tabs.Selected].Height : 0;
            int height = Math.Min(current + chrome, work.Height - Em);
            bool scrolls = current + chrome > height;
            int width = RowWidth + body.Padding.Horizontal + (scrolls ? SystemInformation.VerticalScrollBarWidth : 0);
            ClientSize = new Size(width, height);

            int x = Left;
            if (!placed)
            {
                placed = true;
                if (previous is Rectangle was)
                {
                    x = was.X;
                    anchoredBottom = was.Bottom;
                    if (!fromBottom) Top = was.Y;
                }
                else
                {
                    x = anchor.X + Width <= work.Right ? anchor.X : anchor.X - Width;
                    fromBottom = anchor.Y + Height > work.Bottom;
                    anchoredBottom = anchor.Y;
                    if (!fromBottom) Top = anchor.Y;
                }
            }
            int y = fromBottom ? anchoredBottom - Height : Top;
            x = Math.Max(work.Left, Math.Min(x, work.Right - Width));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - Height));
            Location = new Point(x, y);
        }

        void Watch()
        {
            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            GetWindowThreadProcessId(fg, out uint owner);
            if (owner == processId) { seenForeground = true; return; }
            // Until it has been in front once, a foreground that is still the taskbar's is
            // the click that opened it, not a sign of anything.
            if (seenForeground) Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) watch.Dispose();
            base.Dispose(disposing);
        }
    }
}
