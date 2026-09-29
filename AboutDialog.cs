using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>Who made this, and what it is made of.</summary>
    /// <remarks>
    /// The git history says where the project came from, but nobody running a desktop pet reads
    /// a git history. This is the same thing where it can be seen: the original author first,
    /// then this continuation of it, then the game none of it would exist without.
    ///
    /// Drawn in the flyout's palette and controls, so the two read as one app: her portrait and
    /// the build over a card of credits, each credit a whole row to click rather than one word
    /// of a sentence. Every size is measured from the font rather than chosen, since the window
    /// holds English or Chinese at whatever scaling the desktop uses.
    /// </remarks>
    internal sealed class AboutDialog : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        const string OriginalAuthorUrl = "https://space.bilibili.com/3493085122136852";
        const string AuthorUrl = "https://github.com/solstice23/";
        const string ProjectUrl = "https://github.com/solstice23/desk-madeline";
        const string CelesteUrl = "https://www.celestegame.com/";

        readonly FlyoutPalette p = FlyoutPalette.Current();
        readonly FlyoutStack stack;
        readonly Tween presence;

        int Em => Font.Height;

        public AboutDialog()
        {
            Motion.Refresh();
            Text = Loc.T("Menu.About");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            BackColor = p.Back;
            if (PetWindow.Instance?.AppIcon is Icon icon) Icon = icon;

            stack = new FlyoutStack(p) { Width = Em * 21, Font = Font, Location = new Point(Em, Em) };
            Controls.Add(stack);

            stack.Add(new Hero(p, Portrait(), Loc.T("App.Title"), Version()));
            var credits = stack.Add(new FlyoutCard(p));
            credits.Add(new LinkRow(p, Loc.T("About.CreatedBy"), "Eisyiah", () => Open(OriginalAuthorUrl)));
            credits.Add(new LinkRow(p, Loc.T("About.MaintainedBy"), "solstice23", () => Open(AuthorUrl)));
            stack.Add(new LinkNote(p, Loc.T("About.FanProject"), "Celeste", () => Open(CelesteUrl)));
            stack.Add(new FlyoutNote(p, Loc.T("About.ThirdParty")));
            var buttons = stack.Add(new FlyoutButtonRow(p));
            buttons.Add(Loc.T("About.Source"), () => Open(ProjectUrl), accent: true);
            buttons.Add(Loc.T("Common.Close"), Close);

            presence = new Tween(this, 0f, Ease.OutCubic)
            {
                Stepped = () =>
                {
                    double v = Math.Max(0.0, Math.Min(1.0, presence.Value));
                    Opacity = v >= 0.999 ? 1.0 : v;
                },
            };
            KeyPreview = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = p.Back.GetBrightness() < .5f ? 1 : 0;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            stack.Relayout();
            ClientSize = new Size(stack.Width + Em * 2, stack.Height + Em * 2);
            CenterToScreen();
            if (Motion.Enabled) Opacity = 0;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            presence.To(1f, 200f);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) { Close(); e.Handled = true; }
        }

        static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { PetWindow.Log("could not open " + url + ": " + ex.Message); }
        }

        /// <summary>Her portrait, the same one the tray icon is made of.</summary>
        static Image Portrait()
        {
            string file = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "assets", "portrait.png");
            try
            {
                if (System.IO.File.Exists(file)) return new Bitmap(file);
                Bitmap fromAtlas = Sprites.Get(Sprites.PortraitId, false);
                return fromAtlas == null ? null : new Bitmap(fromAtlas);
            }
            catch { return null; }
        }

        /// <summary>
        /// The commit this was built from, and when it was made -- which answers "what am I
        /// running" in a way a version number nobody bumps does not.
        /// </summary>
        /// <remarks>
        /// A build from a tree with no git to ask has neither, and falls back to the assembly's
        /// version; see BuildStamp.
        /// </remarks>
        static string Version()
        {
            if (BuildStamp.Known) return BuildStamp.Title();

            var assembly = Assembly.GetExecutingAssembly();
            string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "";
            int build = version.IndexOf('+');          // the commit the SDK appends, if any
            return build > 0 ? version.Substring(0, build) : version;
        }

        /// <summary>Her portrait in a rounded frame, the name under it, the build under that.</summary>
        sealed class Hero : Control
        {
            readonly FlyoutPalette p;
            readonly Image portrait;
            readonly string title, version;

            public Hero(FlyoutPalette palette, Image portrait, string title, string version)
            {
                p = palette;
                this.portrait = portrait;
                this.title = title;
                this.version = version;
                BackColor = palette.Back;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            int Em => Font.Height;
            int Side => Em * 4;

            public override Size GetPreferredSize(Size proposed) => new Size(Width, Side + Em * 4);

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var frame = new RectangleF((Width - Side) / 2f, Em / 4f, Side, Side);
                using (GraphicsPath path = FlyoutRow.Rounded(frame, Side * .22f))
                {
                    if (portrait != null)
                    {
                        // Scaled once, smoothly, then laid in with a brush so the rounded edge
                        // is antialiased: a clip region would leave it jagged.
                        using var scaled = new Bitmap(Side, Side, PixelFormat.Format32bppPArgb);
                        using (var sg = Graphics.FromImage(scaled))
                        {
                            sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            sg.DrawImage(portrait, new Rectangle(0, 0, Side, Side));
                        }
                        using var brush = new TextureBrush(scaled, WrapMode.Clamp);
                        brush.TranslateTransform(frame.X, frame.Y);
                        g.FillPath(brush, path);
                    }
                    else using (var fill = new SolidBrush(p.Control)) g.FillPath(fill, path);
                    using var pen = new Pen(p.ControlBorder);
                    g.DrawPath(pen, path);
                }
                using var big = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold);
                int y = (int)frame.Bottom + Em / 2;
                TextRenderer.DrawText(g, title, big, new Rectangle(0, y, Width, big.Height), p.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, version, Font, new Rectangle(0, y + big.Height + Em / 6, Width, Em), p.Secondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) portrait?.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>A credit: the role on the left, the name on the right, the whole row a link.</summary>
        sealed class LinkRow : FlyoutRow
        {
            readonly string role, name;
            readonly Action open;
            readonly Font icons;

            public LinkRow(FlyoutPalette palette, string role, string name, Action open) : base(palette)
            {
                this.role = role;
                this.name = name;
                this.open = open;
                Text = role + " " + name;
                BackColor = palette.Control;
                Cursor = Cursors.Hand;
                icons = FlyoutTabs.IconFont(SystemFonts.MessageBoxFont.Size * .9f);
            }

            public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 2 + Em / 4);

            protected override void OnClick(EventArgs e) { base.OnClick(e); open(); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { open(); e.Handled = true; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                if (HoverT.Value > 0f)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using GraphicsPath path = Rounded(Rectangle.Inflate(ClientRectangle, -Em / 6, -Em / 8), Em / 4f);
                    using var fill = new SolidBrush(Ease.Blend(P.Control, P.Hover, HoverT.Value));
                    g.FillPath(fill, path);
                }
                TextRenderer.DrawText(g, role, Font, new Rectangle(Em / 2, 0, Width / 2, Height), P.Secondary, Line);
                // An arrow out of a box: this goes to a web page.
                int glyphW = icons != null ? Em + Em / 4 : 0;
                if (icons != null)
                    FlyoutTabs.DrawGlyph(g, "", icons, new RectangleF(Width - Em / 2 - Em, 0, Em, Height), P.AccentInk);
                TextRenderer.DrawText(g, name, Font, new Rectangle(Width / 2, 0, Width / 2 - Em / 2 - glyphW, Height),
                    P.AccentInk, Line | TextFormatFlags.Right);
                DrawFocus(g, ClientRectangle);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) icons?.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// A grey note with one word of it a link, as the old window had it. On one line the word
        /// is drawn in the accent and alone is clickable; a sentence too long for one line wraps,
        /// and the whole note becomes the link.
        /// </summary>
        sealed class LinkNote : FlyoutRow
        {
            readonly string format, word;
            readonly Action open;
            const TextFormatFlags Flat = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            const TextFormatFlags Wrap = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

            public LinkNote(FlyoutPalette palette, string format, string word, Action open) : base(palette)
            {
                this.format = format;
                this.word = word;
                this.open = open;
                Text = string.Format(format, word);
                TabStop = false;
            }

            protected override bool FadesOnHover => false;

            (string Before, string After) Parts()
            {
                int at = format.IndexOf("{0}", StringComparison.Ordinal);
                return at < 0 ? (format, "") : (format.Substring(0, at), format.Substring(at + 3));
            }

            int Usable => Width - Em * 2 / 3;

            // The left padding Windows gives text drawn the ordinary way, so this line starts
            // where the plain notes under it do.
            int Lead => (TextRenderer.MeasureText("x", Font).Width - TextRenderer.MeasureText("x", Font, Size.Empty, Flat).Width) / 2;
            bool OneLine => TextRenderer.MeasureText(Text, Font, Size.Empty, Flat).Width <= Usable;

            Rectangle WordBounds()
            {
                var (before, _) = Parts();
                int x = Em / 3 + Lead + TextRenderer.MeasureText(before, Font, Size.Empty, Flat).Width;
                int w = TextRenderer.MeasureText(word, Font, Size.Empty, Flat).Width;
                return new Rectangle(x, 0, w, Em);
            }

            public override Size GetPreferredSize(Size proposed) => new Size(Width,
                (OneLine ? Em : TextRenderer.MeasureText(Text, Font, new Size(Usable, 0), Wrap).Height) + Em / 3);

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                Cursor = !OneLine || WordBounds().Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == MouseButtons.Left && (!OneLine || WordBounds().Contains(e.Location))) open();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                if (!OneLine)
                {
                    TextRenderer.DrawText(g, Text, Font, new Rectangle(Em / 3, 0, Usable, Height), P.Secondary, Wrap);
                    return;
                }
                var (before, after) = Parts();
                Rectangle link = WordBounds();
                TextRenderer.DrawText(g, before, Font, new Point(Em / 3 + Lead, 0), P.Secondary, Flat);
                TextRenderer.DrawText(g, word, Font, link.Location, P.AccentInk, Flat);
                TextRenderer.DrawText(g, after, Font, new Point(link.Right, 0), P.Secondary, Flat);
            }
        }
    }
}
