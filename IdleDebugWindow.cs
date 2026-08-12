using System;
using System.Collections.Generic;
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
        readonly ComboBox windowPicker;
        List<KeyValuePair<IntPtr, string>> choices = new List<KeyValuePair<IntPtr, string>>();
        int refreshTick;

        /// <summary>Raised when the user closes it, so the menu item can uncheck itself.</summary>
        public Action Hidden;

        public IdleDebugWindow()
        {
            Text = Loc.T("Menu.AutonomyDebug");
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(520, 420);
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

            // Overrides: force an activity, or send her up a chosen window, right now.
            // Buttons only queue a request; the game loop consumes it on its own thread.
            var controls = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(4)
            };
            windowPicker = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            controls.Controls.Add(windowPicker);
            var climbButton = new Button { Text = "Climb it", AutoSize = true };
            climbButton.Click += (_, __) =>
            {
                int i = windowPicker.SelectedIndex;
                if (i >= 0 && i < choices.Count)
                    PetWindow.Instance?.RequestIdleOverride(
                        (int)IdleDirector.Activity.ClimbWindow, choices[i].Key);
            };
            controls.Controls.Add(climbButton);
            foreach (IdleDirector.Activity a in new[]
            {
                IdleDirector.Activity.Wander, IdleDirector.Activity.Inspect,
                IdleDirector.Activity.PlayWithWall, IdleDirector.Activity.Nap,
                IdleDirector.Activity.Rest,
            })
            {
                var button = new Button { Text = a.ToString(), AutoSize = true };
                IdleDirector.Activity forced = a;
                button.Click += (_, __) =>
                    PetWindow.Instance?.RequestIdleOverride((int)forced, IntPtr.Zero);
                controls.Controls.Add(button);
            }
            Controls.Add(controls);
            label.BringToFront();

            // The game loop writes the text into a field; this only ever reads it, so the
            // loop never blocks on the UI and the UI never touches the director.
            timer = new Timer { Interval = 100 };
            timer.Tick += (_, __) =>
            {
                string text = PetWindow.Instance?.IdleDebugText ?? "";
                if (label.Text != text) label.Text = text;
                if (++refreshTick % 20 != 0) return;
                var now = PetWindow.Instance?.IdleDebugWindowChoices();
                if (now == null) return;
                bool same = now.Count == choices.Count;
                for (int i = 0; same && i < now.Count; i++)
                    if (now[i].Key != choices[i].Key || now[i].Value != choices[i].Value)
                        same = false;
                if (same) return;
                choices = now;
                int keep = windowPicker.SelectedIndex;
                windowPicker.Items.Clear();
                foreach (var choice in choices)
                    windowPicker.Items.Add(choice.Value.Length == 0 ? "(untitled)" : choice.Value);
                if (keep >= 0 && keep < windowPicker.Items.Count)
                    windowPicker.SelectedIndex = keep;
                else if (windowPicker.Items.Count > 0)
                    windowPicker.SelectedIndex = 0;
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
