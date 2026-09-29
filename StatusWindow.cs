using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// The app's own windows other than the flyout, all one shape: a status line -- an icon in
    /// a tinted circle, a title, a line of detail -- then whatever the state needs under it, then
    /// the buttons, with the one Enter presses in the accent.
    /// </summary>
    /// <remarks>
    /// A window moves from state to state in place rather than giving way to another: each state
    /// rebuilds what is under the status line, eases the window to its new height, and fades
    /// the new content in from one snapshot, so the fade is an image draw a frame. The top edge
    /// stays put while the height moves. The first state is set before the window is shown and
    /// simply appears, with the window fading in around it.
    ///
    /// Drawn in the flyout's palette, with a dark title bar in the dark theme, and sized from
    /// the font, since it holds English or Chinese at whatever scaling the desktop uses.
    /// </remarks>
    internal class StatusWindow : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        protected readonly FlyoutPalette P = FlyoutPalette.Current();
        protected readonly FlyoutStatus Status;
        protected readonly FlyoutStack Body;
        protected readonly FlyoutButtonRow Buttons;
        readonly FlyoutStack stack;
        readonly Reveal reveal;
        readonly Tween presence, grow, fade;
        int growFrom, growTo;

        protected int Em => Font.Height;

        /// <summary>Amber, for something that did not work but can be worked round.</summary>
        protected static Color Warn => Color.FromArgb(0xE8, 0xA3, 0x3D);
        /// <summary>Red, for something that failed outright.</summary>
        protected static Color Danger => Color.FromArgb(0xE8, 0x5A, 0x5A);

        public StatusWindow(string caption, int widthEm = 22)
        {
            Motion.Refresh();
            Text = caption;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            BackColor = P.Back;
            if (PetWindow.Instance?.AppIcon is Icon icon) Icon = icon;

            stack = new FlyoutStack(P) { Width = Em * widthEm, Font = Font, Location = new Point(Em, Em) };
            Status = stack.Add(new FlyoutStatus(P));
            Body = stack.Add(new FlyoutStack(P));
            Buttons = stack.Add(new FlyoutButtonRow(P));
            Controls.Add(stack);
            reveal = new Reveal(P.Back) { Visible = false };
            Controls.Add(reveal);
            reveal.BringToFront();

            presence = new Tween(this, 0f, Ease.OutCubic)
            {
                Stepped = () =>
                {
                    double v = Math.Max(0.0, Math.Min(1.0, presence.Value));
                    Opacity = v >= 0.999 ? 1.0 : v;
                },
            };
            grow = new Tween(this, 1f, Ease.OutQuint)
            {
                Stepped = () => ClientSize = new Size(ClientSize.Width,
                    (int)Math.Round(growFrom + (growTo - growFrom) * grow.Value)),
            };
            fade = new Tween(this, 1f, Ease.OutCubic)
            {
                Stepped = () =>
                {
                    reveal.Veil = 1f - fade.Value;
                    if (fade.Value >= 1f) reveal.Stop();
                },
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = P.Back.GetBrightness() < .5f ? 1 : 0;
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
            Started();
        }

        /// <summary>Once the window is on screen: where a state that has work to do begins it.</summary>
        protected virtual void Started() { }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && Buttons.Default is FlyoutButton main)
            { main.PerformClick(); e.Handled = true; }
        }

        /// <summary>
        /// Move on to a state: the status line, what goes under it, and the buttons -- the one
        /// marked accent is what Enter presses. <paramref name="glyph"/> null is the spinner.
        /// </summary>
        protected void ShowState(string glyph, Color tint, string title, string detail,
            Action<FlyoutStack> fill, Action<FlyoutButtonRow> actions)
        {
            Status.Set(glyph, tint, title, detail);
            while (Body.Controls.Count > 0) Body.Controls[0].Dispose();
            fill?.Invoke(Body);
            Buttons.Clear();
            actions?.Invoke(Buttons);
            stack.Relayout();
            if (!IsHandleCreated || !Visible) return;

            int target = stack.Height + Em * 2;
            if (Motion.Enabled)
            {
                reveal.Start(stack);
                fade.Snap(0f);
                fade.To(1f, 220f);
                growFrom = ClientSize.Height;
                growTo = target;
                grow.Snap(0f);
                grow.To(1f, 260f);
            }
            else ClientSize = new Size(ClientSize.Width, target);
        }

        /// <summary>
        /// Ease to the height the content now needs, for a state whose text has changed length
        /// without the window moving on -- a controller unplugged mid-capture, say.
        /// </summary>
        protected void Resettle()
        {
            stack.Relayout();
            if (!IsHandleCreated || !Visible) return;
            int target = stack.Height + Em * 2;
            if (target == ClientSize.Height) return;
            if (!Motion.Enabled) { ClientSize = new Size(ClientSize.Width, target); return; }
            growFrom = ClientSize.Height;
            growTo = target;
            grow.Snap(0f);
            grow.To(1f, 220f);
        }

        /// <summary>Onto this window's thread, if it is still there to go to.</summary>
        protected void Back(Action what)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    try { what(); }
                    catch (Exception ex) { PetWindow.Log(Text + ": " + ex.Message); }
                }));
            }
            catch (InvalidOperationException) { }   // closing underneath us
        }

        /// <summary>
        /// Laid over the content as a state changes: a snapshot of the new content under a veil
        /// of the background that lifts.
        /// </summary>
        sealed class Reveal : Control
        {
            Bitmap shot;
            float veil;

            public Reveal(Color back)
            {
                BackColor = back;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint | ControlStyles.Opaque, true);
            }

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public float Veil { get => veil; set { veil = value; Invalidate(); } }

            public void Start(Control over)
            {
                Stop();
                shot = new Bitmap(Math.Max(1, over.Width), Math.Max(1, over.Height), PixelFormat.Format32bppPArgb);
                over.DrawToBitmap(shot, new Rectangle(0, 0, shot.Width, shot.Height));
                Bounds = over.Bounds;
                veil = 1f;
                Visible = true;
                BringToFront();
            }

            public void Stop()
            {
                Visible = false;
                shot?.Dispose();
                shot = null;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (var back = new SolidBrush(BackColor)) e.Graphics.FillRectangle(back, ClientRectangle);
                if (shot != null) e.Graphics.DrawImageUnscaled(shot, 0, 0);
                int alpha = (int)(255 * Math.Max(0f, Math.Min(1f, veil)));
                if (alpha > 0)
                    using (var fill = new SolidBrush(Color.FromArgb(alpha, BackColor)))
                        e.Graphics.FillRectangle(fill, ClientRectangle);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) Stop();
                base.Dispose(disposing);
            }
        }
    }

    /// <summary>
    /// A question or a notice, in the app's own style rather than a Windows message box: a
    /// title, the detail, and buttons named for what they do rather than Yes and No.
    /// </summary>
    internal static class FlyoutDialog
    {
        public enum Kind { Info, Question, Warning, Error }

        /// <summary>
        /// Ask, and wait for the answer: the index of the button pressed, or -1 for closing the
        /// window. The last button is the accent one Enter presses.
        /// </summary>
        /// <param name="list">Lines shown under the detail -- missing files, say -- or null.</param>
        public static int Ask(IWin32Window owner, Kind kind, string title, string detail,
            string[] buttons, string list = null)
        {
            using var window = new MessageWindow(kind, title, detail, buttons, list);
            if (owner != null) window.ShowDialog(owner); else window.ShowDialog();
            return window.Answer;
        }

        /// <summary>Say something that needs only acknowledging.</summary>
        public static void Tell(IWin32Window owner, Kind kind, string title, string detail)
            => Ask(owner, kind, title, detail, new[] { Loc.T("Common.Ok") });

        sealed class MessageWindow : StatusWindow
        {
            public int Answer { get; private set; } = -1;

            public MessageWindow(Kind kind, string title, string detail, string[] buttons, string list)
                : base(Loc.T("App.Title"))
            {
                (string glyph, Color tint) = kind switch
                {
                    Kind.Warning => ("", Warn),
                    Kind.Error => ("", Danger),
                    Kind.Question => ("", P.AccentInk),
                    _ => ("", P.AccentInk),
                };
                ShowState(glyph, tint, title, detail,
                    list == null ? null : s =>
                    {
                        var card = s.Add(new FlyoutCard(P));
                        card.Add(new FlyoutListRow(P, list));
                    },
                    row =>
                    {
                        for (int i = 0; i < buttons.Length; i++)
                        {
                            int index = i;
                            row.Add(buttons[i], () => { Answer = index; Close(); }, accent: i == buttons.Length - 1);
                        }
                    });
            }
        }
    }
}
