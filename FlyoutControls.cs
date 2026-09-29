using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskMadeline
{
    /// <summary>
    /// The flyout's colours, light or dark as Windows' app theme is. The accent is Celeste's
    /// blue -- Madeline's hair once her dash is spent -- rather than the Windows accent.
    /// </summary>
    internal sealed class FlyoutPalette
    {
        public Color Back, Text, Secondary, Border, Control, ControlBorder, Hover, Footer;
        /// <summary>Fills: a switch that is on, the chosen segment, the slider, the marks.</summary>
        public Color Accent;
        /// <summary>Text and knobs drawn on the accent: dark, since the blue is a light one.</summary>
        public Color OnAccent;
        /// <summary>The accent as thin lines on the background -- an icon -- deep enough to read.</summary>
        public Color AccentInk;

        public static FlyoutPalette Current()
        {
            bool dark = false;
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                dark = key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch { }
            Color accent = Player.UsedHairColor;
            return dark
                ? new FlyoutPalette
                {
                    Back = Color.FromArgb(44, 44, 44), Text = Color.FromArgb(255, 255, 255),
                    Secondary = Color.FromArgb(197, 197, 197), Border = Color.FromArgb(70, 70, 70),
                    Control = Color.FromArgb(58, 58, 58), ControlBorder = Color.FromArgb(82, 82, 82),
                    Hover = Color.FromArgb(62, 62, 62), Footer = Color.FromArgb(32, 32, 32),
                    Accent = accent, OnAccent = Color.FromArgb(16, 16, 16), AccentInk = accent,
                }
                : new FlyoutPalette
                {
                    Back = Color.FromArgb(249, 249, 249), Text = Color.FromArgb(27, 27, 27),
                    Secondary = Color.FromArgb(96, 96, 96), Border = Color.FromArgb(222, 222, 222),
                    Control = Color.FromArgb(255, 255, 255), ControlBorder = Color.FromArgb(214, 214, 214),
                    Hover = Color.FromArgb(240, 240, 240), Footer = Color.FromArgb(238, 238, 238),
                    Accent = accent, OnAccent = Color.FromArgb(16, 16, 16),
                    // The same blue, darkened until a thin icon holds up on near-white.
                    AccentInk = Color.FromArgb(0x1C, 0x86, 0xCF),
                };
        }
    }

    /// <summary>A window that stacks flyout rows and resizes when one of them changes height.</summary>
    internal interface IFlyoutHost
    {
        void Refit();
    }

    /// <summary>Children one under another at its own width; hidden ones take no room.</summary>
    internal sealed class FlyoutStack : Panel
    {
        public FlyoutStack(FlyoutPalette palette) { BackColor = palette.Back; Margin = Padding.Empty; }

        public void Relayout()
        {
            int y = 0;
            foreach (Control child in Controls)
            {
                if (!child.Visible) continue;
                child.Width = Width;
                if (child is FlyoutStack inner) inner.Relayout();
                else child.Height = child.GetPreferredSize(Size.Empty).Height;
                child.Location = new Point(0, y);
                y += child.Height;
            }
            Height = y;
        }

        public T Add<T>(T control) where T : Control
        {
            control.Width = Width;
            Controls.Add(control);
            return control;
        }

        public override Size GetPreferredSize(Size proposed) => new Size(Width, Height);
    }

    /// <summary>What every flyout row shares: the palette, a width, font-relative metrics, focus.</summary>
    internal abstract class FlyoutRow : Control
    {
        protected readonly FlyoutPalette P;
        protected bool Hot;

        protected FlyoutRow(FlyoutPalette palette)
        {
            P = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = palette.Back;
            ForeColor = palette.Text;
            Margin = Padding.Empty;
        }

        protected int Em => Font.Height;

        /// <summary>Tell the window holding this row that its height has changed.</summary>
        protected void Refit() => (FindForm() as IFlyoutHost)?.Refit();

        protected const TextFormatFlags Line =
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hot = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected void DrawFocus(Graphics g, Rectangle r)
        {
            if (!Focused || !ShowFocusCues) return;
            using var pen = new Pen(P.Text) { DashStyle = DashStyle.Dot };
            g.SmoothingMode = SmoothingMode.None;
            g.DrawRectangle(pen, Rectangle.Inflate(r, -1, -1));
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Max(1f, Math.Min(radius * 2f, Math.Min(r.Width, r.Height)));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>A small bold group title.</summary>
    internal sealed class FlyoutHeader : FlyoutRow
    {
        readonly bool first;

        public FlyoutHeader(FlyoutPalette palette, string text, bool first = false) : base(palette)
        {
            Text = text;
            this.first = first;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        int Above => first ? 0 : Em * 3 / 4;

        public override Size GetPreferredSize(Size proposed) => new Size(Width, Above + Em * 3 / 2);

        protected override void OnPaint(PaintEventArgs e)
        {
            using var bold = new Font(Font, FontStyle.Bold);
            var r = new Rectangle(Em / 3, Above, Width - Em / 3, Height - Above);
            TextRenderer.DrawText(e.Graphics, Text, bold, r, P.Text, Line);
        }
    }

    /// <summary>Grey explanatory text, wrapped to the row width.</summary>
    internal sealed class FlyoutNote : FlyoutRow
    {
        public FlyoutNote(FlyoutPalette palette, string text) : base(palette)
        {
            Text = text;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        const TextFormatFlags Wrap = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;

        public override Size GetPreferredSize(Size proposed)
            => new Size(Width, TextRenderer.MeasureText(Text, Font, new Size(Width - Em * 2 / 3, 0), Wrap).Height + Em / 3);

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Height = GetPreferredSize(Size.Empty).Height;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
            => TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(Em / 3, 0, Width - Em * 2 / 3, Height), P.Secondary, Wrap);
    }

    /// <summary>A label on the left and an on/off switch on the right; the whole row toggles.</summary>
    internal sealed class FlyoutSwitch : FlyoutRow
    {
        readonly Func<bool> get;
        readonly Action<bool> set;

        public FlyoutSwitch(FlyoutPalette palette, string text, Func<bool> get, Action<bool> set) : base(palette)
        {
            Text = text;
            this.get = get;
            this.set = set;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 2);

        void Flip() { set(!get()); Invalidate(); }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Flip(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Flip(); e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Hot) using (var hover = new SolidBrush(P.Hover))
                using (GraphicsPath path = Rounded(ClientRectangle, Em / 4f)) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillPath(hover, path); }

            int trackW = Em * 2, trackH = Em;
            var track = new RectangleF(Width - trackW - Em / 3f, (Height - trackH) / 2f, trackW, trackH);
            var label = new Rectangle(Em / 3, 0, (int)track.X - Em / 2, Height);
            TextRenderer.DrawText(g, Text, Font, label, P.Text, Line | TextFormatFlags.EndEllipsis);

            bool on = get();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Rounded(track, trackH / 2f))
            {
                if (on) using (var fill = new SolidBrush(P.Accent)) g.FillPath(fill, path);
                else using (var pen = new Pen(P.Secondary, Math.Max(1f, Em / 20f))) g.DrawPath(pen, path);
            }
            float knob = on ? trackH * .6f : trackH * .5f;
            float kx = on ? track.Right - trackH / 2f - knob / 2f : track.X + trackH / 2f - knob / 2f;
            using (var brush = new SolidBrush(on ? P.OnAccent : P.Secondary))
                g.FillEllipse(brush, kx, track.Y + (trackH - knob) / 2f, knob, knob);
            DrawFocus(g, ClientRectangle);
        }
    }

    /// <summary>
    /// One choice of several, as a framed group of joined segments: beside its label when both
    /// fit, under it when they do not, and in a grid of equal cells when even a line of its own
    /// is too short for them.
    /// </summary>
    internal sealed class FlyoutSegmented : FlyoutRow
    {
        readonly string[] options;
        readonly Func<int> get;
        readonly Action<int> set;
        readonly bool labelled;
        int hotSegment = -1;

        public FlyoutSegmented(FlyoutPalette palette, string label, string[] options,
            Func<int> get, Action<int> set) : base(palette)
        {
            Text = label ?? "";
            labelled = !string.IsNullOrEmpty(label);
            this.options = options;
            this.get = get;
            this.set = set;
        }

        int SegmentWidth()
        {
            int widest = 0;
            foreach (string o in options) widest = Math.Max(widest, TextRenderer.MeasureText(o, Font).Width);
            return widest + Em;
        }

        int CellH => Em * 3 / 2;
        int Available => Width - Em * 2 / 3;
        int LabelWidth => labelled ? TextRenderer.MeasureText(Text, Font).Width + Em : 0;

        // On one line each segment is as wide as its own text, so a long option does not
        // stretch the short ones beside it.
        int NaturalWidth(int i) => TextRenderer.MeasureText(options[i], Font).Width + Em;
        int LineWidth()
        {
            int w = 0;
            for (int i = 0; i < options.Length; i++) w += NaturalWidth(i);
            return w;
        }

        bool Inline => labelled ? LabelWidth + LineWidth() + Em / 3 <= Width : LineWidth() <= Available;
        bool OneLine => LineWidth() <= Available;

        // A grid of equal cells, as balanced as it can be: 2 + 2 rather than 3 + 1.
        int Columns
        {
            get
            {
                if (OneLine) return options.Length;
                int most = Math.Max(1, Math.Min(options.Length, Available / Math.Max(1, SegmentWidth())));
                int rows = (options.Length + most - 1) / most;
                return (options.Length + rows - 1) / rows;
            }
        }

        int Rows => (options.Length + Columns - 1) / Columns;

        Rectangle Track()
        {
            if (Inline)
            {
                int w = LineWidth();
                return labelled
                    ? new Rectangle(Width - w - Em / 3, (Height - CellH) / 2, w, CellH)
                    : new Rectangle(Em / 3, (Height - CellH) / 2, w, CellH);
            }
            int top = labelled ? Em * 2 : Em / 4;
            return new Rectangle(Em / 3, top, OneLine ? LineWidth() : Available, CellH * Rows);
        }

        RectangleF Cell(Rectangle t, int i)
        {
            if (OneLine)
            {
                float x = t.X;
                for (int j = 0; j < i; j++) x += NaturalWidth(j);
                return new RectangleF(x, t.Y, NaturalWidth(i), CellH);
            }
            int cols = Columns;
            float w = t.Width / (float)cols;
            return new RectangleF(t.X + i % cols * w, t.Y + i / cols * CellH, w, CellH);
        }

        public override Size GetPreferredSize(Size proposed)
            => new Size(Width, Inline ? Em * 2 + Em / 3 : (labelled ? Em * 2 : Em / 4) + CellH * Rows + Em / 2);

        int SegmentAt(Point p)
        {
            Rectangle t = Track();
            if (!t.Contains(p)) return -1;
            for (int i = 0; i < options.Length; i++)
                if (Cell(t, i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int now = SegmentAt(e.Location);
            if (now != hotSegment) { hotSegment = now; Cursor = now >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hotSegment = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int i = SegmentAt(e.Location);
            if (i >= 0 && i != get()) { set(i); Invalidate(); }
        }

        protected override bool IsInputKey(Keys key) => key is Keys.Left or Keys.Right || base.IsInputKey(key);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0;
            if (step == 0) return;
            int next = Math.Max(0, Math.Min(options.Length - 1, get() + step));
            if (next != get()) { set(next); Invalidate(); }
            e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle t = Track();
            if (labelled)
            {
                var label = Inline ? new Rectangle(Em / 3, 0, t.X - Em / 3, Height) : new Rectangle(Em / 3, 0, Width, Em * 2);
                TextRenderer.DrawText(g, Text, Font, label, P.Text, Line | TextFormatFlags.EndEllipsis);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = Em / 4f;
            using (GraphicsPath frame = Rounded(new RectangleF(t.X + .5f, t.Y + .5f, t.Width - 1f, t.Height - 1f), radius))
            {
                using (var fill = new SolidBrush(P.Control)) g.FillPath(fill, frame);
                using (var pen = new Pen(P.ControlBorder)) g.DrawPath(pen, frame);
            }
            int selected = get(), cols = Columns;
            for (int i = 0; i < options.Length; i++)
            {
                RectangleF seg = Cell(t, i);
                if (i == selected || i == hotSegment)
                {
                    using GraphicsPath pill = Rounded(RectangleF.Inflate(seg, -Em / 8f, -Em / 8f), radius * .75f);
                    using var fill = new SolidBrush(i == selected ? P.Accent : P.Hover);
                    g.FillPath(fill, pill);
                }
                if (i % cols > 0 && i != selected && i - 1 != selected)
                    using (var pen = new Pen(P.ControlBorder))
                        g.DrawLine(pen, seg.X, seg.Y + seg.Height / 4f, seg.X, seg.Bottom - seg.Height / 4f);
                TextRenderer.DrawText(g, options[i], Font, Rectangle.Round(RectangleF.Inflate(seg, -Em / 6f, 0)),
                    i == selected ? P.OnAccent : P.Text, Line | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
            if (selected >= 0 && selected < options.Length) DrawFocus(g, Rectangle.Round(Cell(t, selected)));
        }
    }

    /// <summary>A row that folds content out beneath itself, with a chevron that says which way.</summary>
    internal sealed class FlyoutExpander : FlyoutRow
    {
        readonly Control body;

        public FlyoutExpander(FlyoutPalette palette, string text, Control body, bool expanded) : base(palette)
        {
            Text = text;
            this.body = body;
            body.Visible = expanded;
            Cursor = Cursors.Hand;
        }

        public bool Expanded => body.Visible;
        public event Action Toggled;

        public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 2);

        void Flip() { body.Visible = !body.Visible; Invalidate(); Toggled?.Invoke(); Refit(); }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Flip(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Flip(); e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Hot) using (var hover = new SolidBrush(P.Hover))
                using (GraphicsPath path = Rounded(ClientRectangle, Em / 4f)) g.FillPath(hover, path);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Em / 3, 0, Width - Em * 2, Height), P.Text, Line);
            // A chevron: right when folded, down when open.
            float cx = Width - Em, cy = Height / 2f, s = Em / 5f;
            PointF[] chevron = Expanded
                ? new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) }
                : new[] { new PointF(cx - s / 2, cy - s), new PointF(cx + s / 2, cy), new PointF(cx - s / 2, cy + s) };
            using (var pen = new Pen(P.Secondary, Math.Max(1.2f, Em / 16f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, chevron);
            DrawFocus(g, ClientRectangle);
        }
    }

    /// <summary>A small flat button: a binding slot, or a plain command like Reset.</summary>
    internal sealed class FlyoutButton : FlyoutRow
    {
        public event Action Pressed;
        readonly ToolTip tip;
        string toolTip;

        /// <summary>Drawn in the secondary colour: an empty slot.</summary>
        public bool Muted;

        /// <summary>Set text, muted state and tooltip together.</summary>
        public void Show(string text, bool muted, string toolTipText)
        {
            Muted = muted;
            toolTip = toolTipText;
            if (Text == text) { tip?.SetToolTip(this, toolTip); Invalidate(); }
            else Text = text;
        }

        public FlyoutButton(FlyoutPalette palette, string text, ToolTip tip = null) : base(palette)
        {
            this.tip = tip;
            Text = text;
            Cursor = Cursors.Hand;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            tip?.SetToolTip(this, toolTip ?? Text);
            Invalidate();
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Pressed?.Invoke(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Pressed?.Invoke(); e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(.5f, .5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = Rounded(r, Em / 5f))
            {
                using (var fill = new SolidBrush(Hot ? P.Hover : P.Control)) g.FillPath(fill, path);
                using (var pen = new Pen(P.ControlBorder)) g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.Inflate(ClientRectangle, -Em / 4, 0), Muted ? P.Secondary : P.Text,
                Line | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            DrawFocus(g, ClientRectangle);
        }
    }
    /// <summary>
    /// A row of tabs, each an icon over a short label. The icons come from Windows' own icon
    /// font; where there is none the labels stand alone.
    /// </summary>
    internal sealed class FlyoutTabs : FlyoutRow
    {
        readonly List<(string Text, string Glyph, string Full)> tabs = new List<(string, string, string)>();
        readonly ToolTip tip = new ToolTip();
        readonly Font iconFont, labelFont;
        int selected, hotTab = -1;

        public event Action<int> Changed;

        public FlyoutTabs(FlyoutPalette palette, Font font) : base(palette)
        {
            Font = font;
            labelFont = new Font(font.FontFamily, font.Size * .85f);
            iconFont = IconFont(font.Size * 1.3f);
        }

        /// <param name="full">The whole name, shown on hover, where the label is a short form of it.</param>
        public void Add(string text, string glyph, string full = null) => tabs.Add((text, glyph, full ?? text));

        /// <summary>
        /// Draw an icon-font glyph centred on its own outline. Text drawing centres the font's
        /// line, and icon glyphs do not sit in the middle of it, so they came out a little high.
        /// </summary>
        public static void DrawGlyph(Graphics g, string glyph, Font font, RectangleF area, Color color)
        {
            using var path = new GraphicsPath();
            float emSize = font.SizeInPoints * g.DpiY / 72f;
            path.AddString(glyph, font.FontFamily, (int)font.Style, emSize, PointF.Empty, StringFormat.GenericTypographic);
            RectangleF box = path.GetBounds();
            if (box.Width <= 0 || box.Height <= 0) return;
            // Whole pixels, so the strokes land on the grid rather than blur across two.
            float dx = (float)Math.Round(area.X + (area.Width - box.Width) / 2f - box.X);
            float dy = (float)Math.Round(area.Y + (area.Height - box.Height) / 2f - box.Y);
            using var move = new Matrix();
            move.Translate(dx, dy);
            path.Transform(move);
            SmoothingMode was = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color)) g.FillPath(brush, path);
            g.SmoothingMode = was;
        }

        /// <summary>Windows' own icon font at a size, or null where there is none.</summary>
        public static Font IconFont(float size)
        {
            foreach (string family in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                using var probe = new Font(family, size);
                if (probe.Name == family) return new Font(family, size);
            }
            return null;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Selected
        {
            get => selected;
            set { selected = Math.Max(0, Math.Min(tabs.Count - 1, value)); Invalidate(); }
        }

        public override Size GetPreferredSize(Size proposed) => new Size(Width, iconFont != null ? Em * 3 : Em * 2);

        // Equal cells: one even row reads better than cells sized to their labels, which made
        // the long English ones sprawl. A label too long for its cell is shortened in the
        // strings instead, with the whole name on hover.
        RectangleF[] Cells()
        {
            var cells = new RectangleF[tabs.Count];
            float w = Width / (float)Math.Max(1, tabs.Count);
            for (int i = 0; i < tabs.Count; i++) cells[i] = new RectangleF(i * w, 0, w, Height);
            return cells;
        }

        int TabAt(Point p)
        {
            if (p.Y < 0 || p.Y >= Height) return -1;
            RectangleF[] cells = Cells();
            for (int i = 0; i < cells.Length; i++) if (p.X >= cells[i].X && p.X < cells[i].Right) return i;
            return -1;
        }

        void Select(int i)
        {
            if (i < 0 || i == selected) return;
            selected = i;
            Invalidate();
            Changed?.Invoke(i);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int now = TabAt(e.Location);
            if (now != hotTab)
            {
                hotTab = now;
                tip.SetToolTip(this, now >= 0 ? tabs[now].Full : null);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { hotTab = -1; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Select(TabAt(e.Location)); }
        protected override bool IsInputKey(Keys key) => key is Keys.Left or Keys.Right || base.IsInputKey(key);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) { Select(Math.Max(0, selected - 1)); e.Handled = true; }
            if (e.KeyCode == Keys.Right) { Select(Math.Min(tabs.Count - 1, selected + 1)); e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF[] cells = Cells();
            for (int i = 0; i < tabs.Count; i++)
            {
                RectangleF r = cells[i];
                bool on = i == selected;
                if (i == hotTab || on)
                    using (GraphicsPath pill = Rounded(RectangleF.Inflate(r, -Em / 8f, -Em / 8f), Em / 4f))
                    using (var fill = new SolidBrush(on ? P.Control : P.Hover))
                        g.FillPath(fill, pill);
                Color ink = on ? P.Text : P.Secondary;
                if (iconFont != null)
                {
                    var icon = new RectangleF(r.X, r.Y + Em / 5f, r.Width, Em * 1.5f);
                    DrawGlyph(g, tabs[i].Glyph, iconFont, icon, on ? P.AccentInk : ink);
                    var label = new Rectangle((int)r.X, (int)(Em * 1.65f), (int)r.Width, (int)(Em * 1.1f));
                    TextRenderer.DrawText(g, tabs[i].Text, labelFont, label, ink,
                        Line | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                }
                else
                    TextRenderer.DrawText(g, tabs[i].Text, labelFont, Rectangle.Round(r), ink,
                        Line | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                if (on)
                {
                    float w = Math.Min(r.Width * .4f, Em * 1.2f), h = Math.Max(3f, Em / 8f);
                    using GraphicsPath bar = Rounded(new RectangleF(r.X + (r.Width - w) / 2f, Height - h - Em / 8f, w, h), h / 2f);
                    using var accent = new SolidBrush(P.Accent);
                    g.FillPath(accent, bar);
                }
            }
            if (selected >= 0 && selected < tabs.Count)
                DrawFocus(g, Rectangle.Round(cells[selected]));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { iconFont?.Dispose(); labelFont.Dispose(); tip.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// One choice from a list too long for a segmented row: the row shows the current choice,
    /// and a click opens the list in place under it. Picking closes it again.
    /// </summary>
    internal sealed class FlyoutPicker : FlyoutRow
    {
        readonly string[] options;
        readonly Func<int> get;
        readonly Action<int> set;
        readonly OptionList list;

        public FlyoutPicker(FlyoutPalette palette, string label, string[] options, Func<int> get, Action<int> set)
            : base(palette)
        {
            Text = label;
            this.options = options;
            this.get = get;
            this.set = set;
            Cursor = Cursors.Hand;
            list = new OptionList(palette, this) { Visible = false };
        }

        /// <summary>The list, which goes into the stack straight after the row.</summary>
        public Control List => list;

        public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 2);

        void Flip()
        {
            list.Visible = !list.Visible;
            if (list.Visible) list.ScrollToSelected();
            Invalidate();
            Refit();
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Flip(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Flip(); e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Hot)
                using (var hover = new SolidBrush(P.Hover))
                using (GraphicsPath path = Rounded(ClientRectangle, Em / 4f)) g.FillPath(hover, path);
            int labelWidth = TextRenderer.MeasureText(Text, Font).Width;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Em / 3, 0, labelWidth + Em / 3, Height), P.Text, Line);
            int i = get();
            string value = i >= 0 && i < options.Length ? options[i] : "";
            var valueRect = new Rectangle(labelWidth + Em, 0, Width - labelWidth - Em * 2 - Em / 2, Height);
            TextRenderer.DrawText(g, value, Font, valueRect, P.Secondary,
                Line | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
            float cx = Width - Em, cy = Height / 2f, s = Em / 5f;
            PointF[] chevron = list.Visible
                ? new[] { new PointF(cx - s, cy + s / 2), new PointF(cx, cy - s / 2), new PointF(cx + s, cy + s / 2) }
                : new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) };
            using (var pen = new Pen(P.Secondary, Math.Max(1.2f, Em / 16f))
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, chevron);
            DrawFocus(g, ClientRectangle);
        }

        /// <summary>The open list: at most eight rows tall, scrolled with the wheel past that.</summary>
        sealed class OptionList : FlyoutRow
        {
            const int MaxRows = 8;
            readonly FlyoutPicker owner;
            int scroll, hotRow = -1;

            public OptionList(FlyoutPalette palette, FlyoutPicker owner) : base(palette)
            {
                this.owner = owner;
                Cursor = Cursors.Hand;
                TabStop = false;
            }

            int RowH => Em * 7 / 4;
            int Shown => Math.Min(MaxRows, owner.options.Length);

            public override Size GetPreferredSize(Size proposed) => new Size(Width, RowH * Shown + Em / 3);

            public void ScrollToSelected()
                => scroll = Math.Max(0, Math.Min(owner.options.Length - Shown, owner.get() - Shown / 2));

            int RowAt(Point p)
            {
                if (p.Y < 0) return -1;
                int r = scroll + p.Y / RowH;
                return r < owner.options.Length && p.Y / RowH < Shown ? r : -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int now = RowAt(e.Location);
                if (now != hotRow) { hotRow = now; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { hotRow = -1; base.OnMouseLeave(e); }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int next = Math.Max(0, Math.Min(owner.options.Length - Shown, scroll - Math.Sign(e.Delta) * 2));
                if (next != scroll) { scroll = next; hotRow = RowAt(e.Location); Invalidate(); }
                if (e is HandledMouseEventArgs handled) handled.Handled = true;
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                int i = RowAt(e.Location);
                if (i < 0) return;
                owner.set(i);
                owner.Flip();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var frame = new RectangleF(Em / 3f + .5f, .5f, Width - Em * 2 / 3f - 1f, Height - Em / 3f - 1f);
                using (GraphicsPath path = Rounded(frame, Em / 4f))
                {
                    using (var fill = new SolidBrush(P.Control)) g.FillPath(fill, path);
                    using (var pen = new Pen(P.ControlBorder)) g.DrawPath(pen, path);
                }
                int current = owner.get();
                for (int row = 0; row < Shown; row++)
                {
                    int i = scroll + row;
                    var r = new Rectangle((int)frame.X + Em / 6, row * RowH + Em / 8, (int)frame.Width - Em / 3, RowH - Em / 4);
                    if (i == hotRow)
                        using (GraphicsPath pill = Rounded(r, Em / 5f))
                        using (var fill = new SolidBrush(P.Hover)) g.FillPath(fill, pill);
                    if (i == current)
                    {
                        var bar = new RectangleF(r.X + Em / 8f, r.Y + r.Height / 4f, Math.Max(3f, Em / 8f), r.Height / 2f);
                        using GraphicsPath path = Rounded(bar, bar.Width / 2f);
                        using var accent = new SolidBrush(P.Accent);
                        g.FillPath(accent, path);
                    }
                    TextRenderer.DrawText(g, owner.options[i], Font,
                        new Rectangle(r.X + Em * 2 / 3, r.Y, r.Width - Em, r.Height), P.Text, Line | TextFormatFlags.EndEllipsis);
                }
                if (owner.options.Length > Shown)
                {
                    // A thin thumb along the right edge: where in the list this is.
                    float track = Height - Em / 3f - Em / 2f;
                    float thumb = Math.Max(Em, track * Shown / owner.options.Length);
                    float y = Em / 4f + (track - thumb) * scroll / (owner.options.Length - Shown);
                    using GraphicsPath path = Rounded(new RectangleF(frame.Right - Em / 3f, y, Em / 8f, thumb), Em / 16f);
                    using var brush = new SolidBrush(P.Secondary);
                    g.FillPath(brush, path);
                }
            }
        }
    }

    /// <summary>A label, the value, and a track to set it with one click or a drag.</summary>
    internal sealed class FlyoutSlider : FlyoutRow
    {
        readonly int min, max, step;
        readonly Func<int> get;
        readonly Action<int> set;
        readonly Func<int, string> format;
        bool dragging;

        public FlyoutSlider(FlyoutPalette palette, string label, int min, int max, int step,
            Func<int> get, Action<int> set, Func<int, string> format) : base(palette)
        {
            Text = label;
            this.min = min; this.max = max; this.step = step;
            this.get = get; this.set = set; this.format = format;
        }

        public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 2);

        RectangleF Track()
        {
            float left = Width * .42f, right = Width - Em * 3.2f;
            return new RectangleF(left, Height / 2f - Em / 10f, right - left, Math.Max(3f, Em / 5f));
        }

        void SetFrom(int x)
        {
            RectangleF t = Track();
            float f = Math.Max(0f, Math.Min(1f, (x - t.X) / t.Width));
            int v = min + (int)Math.Round(f * (max - min) / step) * step;
            if (v != get()) { set(v); Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.X < Track().X - Em / 2f) return;
            dragging = true;
            SetFrom(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFrom(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; }
        protected override bool IsInputKey(Keys key) => key is Keys.Left or Keys.Right || base.IsInputKey(key);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int delta = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
            if (delta == 0) return;
            int v = Math.Max(min, Math.Min(max, get() + delta));
            if (v != get()) { set(v); Invalidate(); }
            e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            RectangleF t = Track();
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Em / 3, 0, (int)t.X - Em / 2, Height), P.Text,
                Line | TextFormatFlags.EndEllipsis);
            int v = get();
            TextRenderer.DrawText(g, format(v), Font,
                new Rectangle((int)t.Right + Em / 2, 0, Width - (int)t.Right - Em / 2 - Em / 3, Height),
                P.Secondary, Line | TextFormatFlags.Right);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float f = (v - min) / (float)Math.Max(1, max - min);
            using (GraphicsPath path = Rounded(t, t.Height / 2f))
            using (var brush = new SolidBrush(P.ControlBorder)) g.FillPath(brush, path);
            using (GraphicsPath path = Rounded(new RectangleF(t.X, t.Y, Math.Max(t.Height, t.Width * f), t.Height), t.Height / 2f))
            using (var brush = new SolidBrush(P.Accent)) g.FillPath(brush, path);
            float knob = Em * .9f, kx = t.X + t.Width * f - knob / 2f, ky = t.Y + t.Height / 2f - knob / 2f;
            using (var brush = new SolidBrush(P.Control)) g.FillEllipse(brush, kx, ky, knob, knob);
            using (var pen = new Pen(P.ControlBorder)) g.DrawEllipse(pen, kx, ky, knob, knob);
            using (var brush = new SolidBrush(P.Accent))
                g.FillEllipse(brush, kx + knob * .25f, ky + knob * .25f, knob * .5f, knob * .5f);
            DrawFocus(g, ClientRectangle);
        }
    }

    /// <summary>Buttons laid out in equal columns, as many to a line as fit.</summary>
    internal sealed class FlyoutButtons : Control
    {
        readonly List<FlyoutButton> buttons = new List<FlyoutButton>();
        readonly FlyoutPalette palette;

        public FlyoutButtons(FlyoutPalette palette)
        {
            this.palette = palette;
            BackColor = palette.Back;
            Margin = Padding.Empty;
        }

        int Em => Font.Height;

        public FlyoutButton Add(string text, Action pressed)
        {
            var button = new FlyoutButton(palette, text);
            button.Pressed += pressed;
            buttons.Add(button);
            Controls.Add(button);
            return button;
        }

        int Columns()
        {
            int widest = 0;
            foreach (FlyoutButton b in buttons)
                widest = Math.Max(widest, TextRenderer.MeasureText(b.Text, Font).Width + Em * 3 / 2);
            int usable = Width - Em * 2 / 3;
            return Math.Max(1, Math.Min(buttons.Count, (usable + Em / 4) / Math.Max(1, widest + Em / 4)));
        }

        int ButtonH => Em * 3 / 2 + Em / 6;

        public override Size GetPreferredSize(Size proposed)
        {
            int cols = Columns();
            int rows = (buttons.Count + cols - 1) / cols;
            return new Size(Width, rows * (ButtonH + Em / 4) + Em / 4);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int cols = Columns(), gap = Em / 4, usable = Width - Em * 2 / 3;
            int w = (usable - gap * (cols - 1)) / cols;
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].Bounds = new Rectangle(Em / 3 + i % cols * (w + gap), Em / 8 + i / cols * (ButtonH + gap), w, ButtonH);
        }
    }

    /// <summary>A label and a swatch of the colour it names; a click asks for another.</summary>
    internal sealed class FlyoutSwatch : FlyoutRow
    {
        readonly Func<Color> get;
        readonly Action pick;

        public FlyoutSwatch(FlyoutPalette palette, string label, Func<Color> get, Action pick) : base(palette)
        {
            Text = label;
            this.get = get;
            this.pick = pick;
            Cursor = Cursors.Hand;
        }

        public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 2);

        protected override void OnClick(EventArgs e) { base.OnClick(e); pick(); Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { pick(); Invalidate(); e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Hot)
                using (var hover = new SolidBrush(P.Hover))
                using (GraphicsPath path = Rounded(ClientRectangle, Em / 4f)) g.FillPath(hover, path);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Em / 3, 0, Width / 2, Height), P.Text, Line);
            Color c = get();
            string hex = "#" + (c.ToArgb() & 0xFFFFFF).ToString("X6");
            int hexW = TextRenderer.MeasureText(hex, Font).Width;
            var swatch = new RectangleF(Width - Em / 3f - Em * 2f, Height / 2f - Em * .55f, Em * 2f, Em * 1.1f);
            TextRenderer.DrawText(g, hex, Font, new Rectangle((int)swatch.X - hexW - Em / 3, 0, hexW, Height), P.Secondary, Line);
            using (GraphicsPath path = Rounded(swatch, Em / 5f))
            {
                using (var fill = new SolidBrush(Color.FromArgb(255, c))) g.FillPath(fill, path);
                using (var pen = new Pen(P.ControlBorder)) g.DrawPath(pen, path);
            }
            DrawFocus(g, ClientRectangle);
        }
    }

    /// <summary>
    /// The strip along the bottom of the flyout: a shade apart from the pages, edge to edge, with
    /// small flat items -- an icon and a word -- at its left and right ends.
    /// </summary>
    internal sealed class FlyoutFooter : FlyoutRow
    {
        readonly List<(string Glyph, string Text, Action Pressed, bool Right)> items =
            new List<(string, string, Action, bool)>();
        Font iconFont;
        int hot = -1;

        public FlyoutFooter(FlyoutPalette palette) : base(palette)
        {
            BackColor = palette.Footer;
            TabStop = false;
        }

        public void Add(string glyph, string text, Action pressed, bool right)
        {
            items.Add((glyph, text, pressed, right));
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 2 + Em / 2);

        bool iconFontLooked;

        // Made on first use rather than when the font is set, which happens before the strip
        // has a parent and does not always announce itself.
        Font Icons
        {
            get
            {
                if (!iconFontLooked) { iconFontLooked = true; iconFont = FlyoutTabs.IconFont(Font.Size); }
                return iconFont;
            }
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            iconFont?.Dispose();
            iconFont = null;
            iconFontLooked = false;
        }

        List<Rectangle> Layout_()
        {
            var bounds = new List<Rectangle>();
            int left = Em / 2, right = Width - Em / 2, h = Em * 3 / 2, y = (Height - h) / 2;
            foreach (var item in items)
            {
                int w = TextRenderer.MeasureText(item.Text, Font).Width + (Icons != null ? Em * 2 + Em / 3 : Em);
                if (item.Right) { right -= w; bounds.Add(new Rectangle(right, y, w, h)); right -= Em / 4; }
                else { bounds.Add(new Rectangle(left, y, w, h)); left += w + Em / 4; }
            }
            return bounds;
        }

        int ItemAt(Point p)
        {
            List<Rectangle> bounds = Layout_();
            for (int i = 0; i < bounds.Count; i++) if (bounds[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int now = ItemAt(e.Location);
            if (now != hot) { hot = now; Cursor = now >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = ItemAt(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left) items[i].Pressed();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var line = new Pen(P.Border)) g.DrawLine(line, 0, 0, Width, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            List<Rectangle> bounds = Layout_();
            for (int i = 0; i < items.Count; i++)
            {
                Rectangle r = bounds[i];
                if (i == hot)
                    using (GraphicsPath pill = Rounded(r, Em / 4f))
                    using (var fill = new SolidBrush(P.Hover)) g.FillPath(fill, pill);
                int x = r.X + Em / 2;
                if (Icons != null)
                {
                    FlyoutTabs.DrawGlyph(g, items[i].Glyph, Icons, new RectangleF(x, r.Y, Em, r.Height), P.Secondary);
                    x += Em + Em / 3;
                }
                TextRenderer.DrawText(g, items[i].Text, Font, new Rectangle(x, r.Y, r.Right - x, r.Height), P.Text, Line);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) iconFont?.Dispose();
            base.Dispose(disposing);
        }
    }
}
