using System.Drawing;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>Input-only window following a Seeker; rendering stays on DirectComposition.</summary>
    internal sealed class SeekerInputWindow : Form
    {
        readonly PetWindow ownerWindow;
        internal readonly Seeker Seeker;

        public SeekerInputWindow(PetWindow ownerWindow, Seeker seeker)
        {
            this.ownerWindow = ownerWindow;
            Seeker = seeker;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Opacity = .01;
            Size = new Size(1, 1);
            Location = new Point(-10000, -10000);
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem(ownerWindow.Localize("Remove", "移除"), null,
                (_, __) => ownerWindow.RequestSeekerRemoval(Seeker)));
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
    }
}
