using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
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
    ///
    /// It fades in sliding out of the pointer's corner and fades out when sent away; switching
    /// tabs eases its height to the new page while the page fades through. Every motion is
    /// done the cheap way: the window's own alpha, which the compositor applies, and for the
    /// fade between pages two snapshots taken once rather than every control painting itself
    /// translucent each frame.
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
        const int CS_DROPSHADOW = 0x20000, WS_EX_TOOLWINDOW = 0x80, WS_EX_COMPOSITED = 0x02000000;
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("user32.dll")]
        static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

        public readonly FlyoutPalette P = FlyoutPalette.Current();
        readonly Point anchor;
        readonly Rectangle? previous;
        readonly FlyoutTabs tabs;
        readonly Panel scroller, top, body, frame;
        // Opened upward, the shape the window is cut to: the part of it in use.
        Rectangle shown;
        // The flyout's own height, which opened upward is not the window's: the window then
        // reaches from the top of the screen down, and the flyout is the part at its bottom.
        int visible;
        readonly FlyoutFooter footer;
        readonly FadeCover cover;
        readonly List<FlyoutStack> pages = new List<FlyoutStack>();
        readonly List<Point> scrolls = new List<Point>();
        readonly Timer watch = new Timer { Interval = 100 };
        readonly uint processId = (uint)Environment.ProcessId;
        bool seenForeground, placed, fromBottom, dismissing;
        // The edge that sits on the anchor when it opened upward: that one stays put.
        int anchoredBottom, anchoredX, anchoredTop;

        // Opening and closing: 0 gone, 1 fully there.
        readonly Tween presence;
        // Switching tabs: the height eases from one page's to the other's while the page fades
        // through at an even pace, so the old page's fade-out keeps its share of the time.
        readonly Tween switching, fade;
        int switchFrom, switchTo;

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

            Motion.Refresh();
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
            cover = new FadeCover(P.Back) { Visible = false };
            body.Controls.Add(cover);
            footer = new FlyoutFooter(P) { Dock = DockStyle.Bottom, Font = Font };
            footer.Height = footer.GetPreferredSize(Size.Empty).Height;

            // Everything the flyout shows is in one panel: the whole window when it opens
            // downward, the part of a taller window in use when it opens upward -- see Place.
            frame = new Panel { Dock = DockStyle.Fill, BackColor = P.Back };
            frame.Controls.Add(body);
            frame.Controls.Add(footer);
            Controls.Add(frame);
            Orient(fromBottom);

            presence = new Tween(this, 0f, Ease.OutCubic) { Stepped = Appear };
            switching = new Tween(this, 1f, Ease.OutQuint) { Stepped = Switching };
            fade = new Tween(this, 1f, Ease.Linear) { Stepped = () => cover.Progress = fade.Value };
            watch.Tick += (_, _) => Watch();
        }

        /// <summary>
        /// Which way up the chrome goes. Opening downward, the tabs are at the top, by the
        /// pointer, and the strip at the bottom; opening upward -- the bottom edge on the pointer
        /// and the flyout growing up from it -- the two swap, so the tabs are still the part
        /// nearest the pointer and still the edge that stays put while pages change height.
        /// </summary>
        void Orient(bool tabsAtBottom)
        {
            DockStyle near = tabsAtBottom ? DockStyle.Bottom : DockStyle.Top;
            if (tabs.Dock == near) return;
            SuspendLayout();
            tabs.Dock = near;
            top.Dock = near;
            footer.Dock = tabsAtBottom ? DockStyle.Top : DockStyle.Bottom;
            footer.LineAtBottom = tabsAtBottom;
            footer.Invalidate();
            ResumeLayout();
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

        /// <summary>Rows that are there only while another control says so.</summary>
        /// <remarks>
        /// They fold in and out the way an expander's body does, with the same tween and
        /// timing, only driven by <paramref name="show"/> -- which the controlling row calls --
        /// rather than a row of their own.
        /// </remarks>
        public FlyoutStack Revealed(FlyoutStack page, bool shown, out Action<bool> show)
        {
            var body = new FlyoutStack(P) { Width = RowWidth, Visible = shown };
            page.Add(body);
            Tween reveal = null;
            reveal = new Tween(body, shown ? 1f : 0f, Ease.OutQuint)
            {
                Stepped = () =>
                {
                    if (reveal.Value <= 0f && reveal.Target <= 0f) body.Visible = false;
                    Refit();
                },
            };
            page.Reveal(body, reveal);
            show = on =>
            {
                if (on) body.Visible = true;
                reveal.To(on ? 1f : 0f, on ? 280f : 220f);
            };
            return body;
        }

        /// <summary>Show a tab, as a click on it would with <paramref name="animate"/>, or at once.</summary>
        public void SelectPage(int index, bool animate = false)
        {
            tabs.Selected = index;
            if (IsHandleCreated) ShowPage(tabs.Selected, animate);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.ExStyle |= WS_EX_TOOLWINDOW;   // out of Alt+Tab, like a menu
                // All of its controls painted together off screen and shown in one go, so the
                // panel and its contents moving inside the window, as pages change height, are
                // never seen half redrawn.
                cp.ExStyle |= WS_EX_COMPOSITED;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FrameStyle(cut: fromBottom);
        }

        /// <summary>
        /// Windows' own rounded corners and border -- or, for a window cut to a shape, none:
        /// Windows draws them around the whole window, and opened upward most of the window is
        /// the empty reach above the flyout, so they would outline that instead.
        /// </summary>
        void FrameStyle(bool cut)
        {
            const int DWMWCP_DONOTROUND = 1;
            const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
            int round = cut ? DWMWCP_DONOTROUND : DWMWCP_ROUND;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int border = cut ? unchecked((int)DWMWA_COLOR_NONE) : P.Border.R | P.Border.G << 8 | P.Border.B << 16;
            DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ShowPage(tabs.Selected, animate: false);
            // Invisible before the first frame is shown, so the fade-in starts from nothing
            // rather than from a flash of the finished flyout.
            if (Motion.Enabled && previous == null) Opacity = 0;
            else presence.Snap(1f);
            watch.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetForegroundWindow(Handle);
            Activate();
            tabs.Focus();
            if (presence.Value < 1f) presence.To(1f, 220f);
        }

        /// <summary>Fade out and then close; what Esc and clicking elsewhere do.</summary>
        public void Dismiss()
        {
            if (dismissing || IsDisposed) return;
            dismissing = true;
            watch.Stop();
            if (!Motion.Enabled || presence.Value <= 0f) { Close(); return; }
            presence.To(0f, 160f);
        }

        // Opening: from the pointer's corner, a few pixels short of its place and see-through,
        // into place and solid. Closing runs the same backwards, and closes at the end.
        void Appear()
        {
            float v = presence.Value;
            double opacity = Math.Max(0.0, Math.Min(1.0, v));
            // Exactly 1 lets the window stop being layered, which is how it spends its life.
            if (Math.Abs(Opacity - opacity) > 0.001) Opacity = opacity >= 0.999 ? 1.0 : opacity;
            Place(visible);
            if (dismissing && v <= 0f) Close();
        }

        void ShowPage(int index) => ShowPage(index, animate: true);

        void ShowPage(int index, bool animate)
        {
            if (index < 0 || index >= pages.Count) return;
            FlyoutStack old = scroller.Controls.Count > 0 ? scroller.Controls[0] as FlyoutStack : null;
            if (old == pages[index]) { Refit(); return; }
            if (old != null)
            {
                int was = pages.IndexOf(old);
                if (was >= 0) scrolls[was] = new Point(-scroller.AutoScrollPosition.X, -scroller.AutoScrollPosition.Y);
            }
            animate &= Motion.Enabled && old != null && placed && Visible;

            // The page going away, as it looks now, before anything changes under it.
            Bitmap from = animate ? Snapshot(scroller) : null;

            scroller.SuspendLayout();
            scroller.Controls.Clear();
            scroller.Controls.Add(pages[index]);
            scroller.ResumeLayout();
            pages[index].Width = RowWidth;
            pages[index].Relayout();
            UpdateScrolling();
            if (scroller.AutoScroll) scroller.AutoScrollPosition = scrolls[index];
            TabChanged?.Invoke(index);

            if (!animate)
            {
                Refit();
                return;
            }
            // The page arriving, drawn whole at its scroll position, while the window still
            // has the old one's height; the cover shows the two in turn while the window moves.
            Bitmap to = Snapshot(pages[index], scroller.AutoScroll ? scrolls[index].Y : 0);
            switchFrom = visible;
            switchTo = MeasureHeight();
            cover.Start(from, to);
            cover.Bounds = scroller.Bounds;
            cover.Visible = true;
            cover.BringToFront();
            switching.Snap(0f);
            fade.Snap(0f);
            switching.To(1f, 280f);
            fade.To(1f, 280f);
        }

        void Switching()
        {
            float t = switching.Value;
            int height = (int)Math.Round(switchFrom + (switchTo - switchFrom) * t);
            Place(height);
            cover.Bounds = scroller.Bounds;
            if (t >= 1f)
            {
                cover.Stop();
                Refit();
            }
        }

        static Bitmap Snapshot(Control c, int scrollY = 0)
        {
            var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height), PixelFormat.Format32bppPArgb);
            c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (scrollY <= 0) return bmp;
            // A page scrolled down shows from its scroll offset, not from its top.
            var shifted = new Bitmap(bmp.Width, Math.Max(1, bmp.Height - scrollY), PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(shifted)) g.DrawImageUnscaled(bmp, 0, -scrollY);
            bmp.Dispose();
            return shifted;
        }

        int Chrome => body.Padding.Vertical + tabs.Height + top.Height + footer.Height + frame.Padding.Vertical;

        Rectangle Work => Screen.FromPoint(previous?.Location ?? anchor).WorkingArea;

        int MeasureHeight()
        {
            int current = pages.Count > 0 && tabs.Selected < pages.Count ? pages[tabs.Selected].Height : 0;
            return Math.Min(current + Chrome, Work.Height - Em);
        }

        /// <summary>
        /// Size to the page on show, up to the screen, and sit where a right-click menu would:
        /// the corner nearest the pointer on the pointer, flipped left or up where the menu
        /// would not fit. That corner stays put while pages of different heights come and go.
        /// </summary>
        public void Refit()
        {
            // Held until the window has its new size: otherwise, for a moment each frame of a
            // fold-out, the page is taller than the window and a scrollbar flashes up.
            scroller.SuspendLayout();
            if (tabs.Selected >= 0 && tabs.Selected < pages.Count)
            {
                pages[tabs.Selected].Width = RowWidth;
                pages[tabs.Selected].Relayout();
            }
            UpdateScrolling();
            int height = MeasureHeight();
            // Mid-switch, the switch owns the height: aim it at the new one instead.
            if (switching.Value < 1f) switchTo = height;
            else Place(height);
            scroller.ResumeLayout();
        }

        /// <summary>
        /// Scroll only a page the screen cannot hold. The window grows to fit every other page,
        /// so a scrollbar anywhere else is the window and the page disagreeing for a frame.
        /// </summary>
        void UpdateScrolling()
        {
            int current = pages.Count > 0 && tabs.Selected < pages.Count ? pages[tabs.Selected].Height : 0;
            bool needed = current + Chrome > Work.Height - Em;
            if (scroller.AutoScroll != needed) scroller.AutoScroll = needed;
        }

        /// <remarks>
        /// Opening upward, the window itself never changes size: Windows does not show a
        /// window's new size and its repaint in the same frame, and with the top edge moving
        /// every frame of a tab switch that showed as the old picture, top-aligned, for a frame --
        /// the tabs jumping and what was behind showing under them. So the window reaches from
        /// the top of the screen down to the pointer from the start, the flyout is a panel at its
        /// bottom, and the window is cut to that panel's rounded shape. Only the panel and the
        /// shape move as pages change height, and outside the shape clicks go to whatever is
        /// below. Windows draws no rounded corners or border of its own on a window with a shape,
        /// so the panel draws a hairline border instead.
        /// </remarks>
        void Place(int height)
        {
            // Against the most the screen allows, not the height of the moment: mid-switch the
            // page is taller than the window, and that is not a reason for a scrollbar.
            int current = pages.Count > 0 && tabs.Selected < pages.Count ? pages[tabs.Selected].Height : 0;
            bool scrolls = current + Chrome > Work.Height - Em;
            int width = RowWidth + body.Padding.Horizontal + frame.Padding.Horizontal +
                (scrolls ? SystemInformation.VerticalScrollBarWidth : 0);
            Rectangle work = Work;
            if (!placed)
            {
                placed = true;
                ClientSize = new Size(width, height);
                if (previous is Rectangle was)
                {
                    anchoredX = was.X;
                    anchoredBottom = was.Bottom;
                    anchoredTop = was.Y;
                }
                else
                {
                    anchoredX = anchor.X + Width <= work.Right ? anchor.X : anchor.X - Width;
                    fromBottom = anchor.Y + Height > work.Bottom;
                    Orient(fromBottom);
                    anchoredBottom = Math.Min(anchor.Y, work.Bottom);
                    anchoredTop = anchor.Y;
                }
                if (fromBottom)
                {
                    if (IsHandleCreated) FrameStyle(cut: true);
                    frame.Dock = DockStyle.None;
                    frame.Padding = new Padding(1);
                    frame.BackColor = P.Border;
                }
            }
            visible = height;
            int x = Math.Max(work.Left, Math.Min(anchoredX, work.Right - width));
            // Opening and closing: short of its place by up to half a line, on the side the
            // pointer is, so it seems to come out of the click.
            int slide = (int)Math.Round((1f - presence.Value) * Em / 2f);
            if (!fromBottom)
            {
                var size = SizeFromClientSize(new Size(width, height));
                int y = Math.Max(work.Top, Math.Min(anchoredTop, work.Bottom - size.Height)) - slide;
                Bounds = new Rectangle(x, y, size.Width, size.Height);
                return;
            }

            int reach = Math.Max(height, anchoredBottom - work.Top);
            var whole = new Rectangle(x, anchoredBottom - reach + slide, width, reach);
            if (Bounds != whole) Bounds = whole;
            frame.SetBounds(0, reach - height, width, height);
            var cut = new Rectangle(0, reach - height, width, height);
            if (cut != shown && IsHandleCreated)
            {
                shown = cut;
                int round = Em / 2;
                // The window owns the region once it is set, and frees the one it replaces.
                SetWindowRgn(Handle, CreateRoundRectRgn(cut.Left, cut.Top, cut.Right + 1, cut.Bottom + 1, round, round), true);
            }
        }

        void Watch()
        {
            IntPtr fg = Win32.GetForegroundWindow();
            if (fg == IntPtr.Zero) return;
            GetWindowThreadProcessId(fg, out uint owner);
            if (owner == processId) { seenForeground = true; return; }
            // Until it has been in front once, a foreground that is still the taskbar's is
            // the click that opened it, not a sign of anything.
            if (seenForeground) Dismiss();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Dismiss(); e.Handled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { watch.Dispose(); cover.Stop(); }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Laid over the page while tabs switch: the old page fading into the background, then
        /// the new one out of it. Two plain image draws and a translucent fill a frame, however
        /// many controls either page has.
        /// </summary>
        sealed class FadeCover : Control
        {
            Bitmap from, to;
            float progress;

            public FadeCover(Color back)
            {
                BackColor = back;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint | ControlStyles.Opaque, true);
            }

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public float Progress
            {
                get => progress;
                set { progress = value; Invalidate(); }
            }

            public void Start(Bitmap from, Bitmap to)
            {
                Stop();
                this.from = from;
                this.to = to;
                progress = 0f;
            }

            public void Stop()
            {
                Visible = false;
                from?.Dispose();
                to?.Dispose();
                from = to = null;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                // Fade through the background: the old page out over the first third, the new
                // one in over the rest, so the two are never seen on top of each other.
                const float turn = .35f;
                bool second = progress >= turn;
                Bitmap shown = second ? to : from;
                float veil = second ? 1f - (progress - turn) / (1f - turn) : progress / turn;
                using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
                if (shown != null) g.DrawImageUnscaled(shown, 0, 0);
                int alpha = (int)(255 * Math.Max(0f, Math.Min(1f, veil)));
                if (alpha > 0)
                    using (var fill = new SolidBrush(Color.FromArgb(alpha, BackColor)))
                        g.FillRectangle(fill, ClientRectangle);
            }
        }
    }
}
