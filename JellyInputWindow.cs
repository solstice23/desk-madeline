using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>A tiny input-only HWND following one jellyfish.  The composition
    /// surface remains click-through, so no rectangle between actors captures the
    /// desktop's mouse input.</summary>
    internal sealed class JellyInputWindow : Form
    {
        readonly PetWindow ownerWindow;
        /// <summary>The shape it was last cut down to; see HitRegion.</summary>
        internal byte[] HitMask;
        internal readonly Glider Glider;

        public JellyInputWindow(PetWindow ownerWindow, Glider glider)
        {
            this.ownerWindow = ownerWindow;
            Glider = glider;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Opacity = PetWindow.HitTestOpacity;
            Size = new Size(1, 1);
            Location = new Point(-10000, -10000);

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem(
                ownerWindow.Localize("Common.Remove"), null,
                (_, __) => ownerWindow.RequestGliderRemoval(Glider)));
            ContextMenuStrip = menu;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                // CreateParams is queried by the Form base constructor before our
                // constructor body assigns ownerWindow.
                if (ownerWindow?.AlwaysOnTop ?? true) cp.ExStyle |= WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) ownerWindow.BeginGliderDrag(Glider);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if ((e.Button & MouseButtons.Left) != 0) ownerWindow.ContinueGliderDrag(Glider);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) ownerWindow.EndGliderDrag(Glider);
        }
    }
}
