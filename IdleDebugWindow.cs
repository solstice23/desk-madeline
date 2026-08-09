using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// A little pane of her thoughts: what the idle director is doing, wanting, and measuring,
    /// refreshed a few times a second. A debug tool, so it is not persisted and not localized
    /// beyond its title.
    /// </summary>
    internal sealed class IdleDebugWindow : Form
    {
        readonly Label label;
        readonly Timer timer;

        /// <summary>Raised when the user closes it, so the menu item can uncheck itself.</summary>
        public Action Hidden;

        public IdleDebugWindow()
        {
            Text = Loc.T("Menu.AutonomyDebug");
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(340, 170);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 12, area.Top + 12);
            label = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                Padding = new Padding(8),
                Text = ""
            };
            Controls.Add(label);
            // The game loop writes the text into a field; this only ever reads it, so the
            // loop never blocks on the UI and the UI never touches the director.
            timer = new Timer { Interval = 100 };
            timer.Tick += (_, __) =>
            {
                string text = PetWindow.Instance?.IdleDebugText ?? "";
                if (label.Text != text) label.Text = text;
            };
            timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                Hidden?.Invoke();
            }
            base.OnFormClosing(e);
        }
    }
}
