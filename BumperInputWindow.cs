using System.Drawing;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>Input-only window following a Bumper; rendering stays on DirectComposition.</summary>
    internal sealed class BumperInputWindow : Form
    {
        readonly PetWindow ownerWindow;
        /// <summary>The shape it was last cut down to; see HitRegion.</summary>
        internal byte[] HitMask;
        internal readonly Bumper Bumper;

        public BumperInputWindow(PetWindow ownerWindow, Bumper bumper)
        {
            this.ownerWindow = ownerWindow;
            Bumper = bumper;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Opacity = PetWindow.HitTestOpacity;
            Size = new Size(1, 1);
            Location = new Point(-10000, -10000);
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem(ownerWindow.Localize("Common.Remove"), null,
                (_, __) => ownerWindow.RequestBumperRemoval(Bumper)));
            ContextMenuStrip = menu;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                if (ownerWindow?.AlwaysOnTop ?? true) cp.ExStyle |= WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) ownerWindow.BeginBumperDrag(Bumper);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if ((e.Button & MouseButtons.Left) != 0) ownerWindow.ContinueBumperDrag(Bumper);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) ownerWindow.EndBumperDrag(Bumper);
        }
    }
}
