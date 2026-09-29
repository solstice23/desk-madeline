using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// Keyboard and controller bindings: every action against its three slots. A window of its
    /// own rather than part of the menu, because binding is a task -- pick a slot, press a key,
    /// again for the next -- and not a click.
    /// </summary>
    /// <remarks>
    /// Single: asking for it again brings back the one already open. A slot opens the capture
    /// dialog, which also clears it on Backspace or Delete. Changes save as they are made.
    /// </remarks>
    internal sealed class BindingsWindow : Form, IFlyoutHost
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        static BindingsWindow open;
        static int device;

        readonly KeyBindings keys;
        readonly PadBindings pad;
        readonly FlyoutPalette p = FlyoutPalette.Current();
        readonly FlyoutStack stack;
        readonly ToolTip tip = new ToolTip();
        readonly List<(PetAction Action, int Slot, FlyoutButton Button)> slots = new List<(PetAction, int, FlyoutButton)>();

        int Em => Font.Height;

        public static void Show(KeyBindings keys, PadBindings pad)
        {
            if (open != null && !open.IsDisposed)
            {
                if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
                open.Activate();
                return;
            }
            open = new BindingsWindow(keys, pad);
            open.FormClosed += (_, _) => open = null;
            open.Show();
        }

        BindingsWindow(KeyBindings keys, PadBindings pad)
        {
            this.keys = keys;
            this.pad = pad;
            Motion.Refresh();
            Text = Loc.T("Settings.Bindings");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            BackColor = p.Back;
            Padding = new Padding(Em);
            if (PetWindow.Instance?.AppIcon is Icon icon) Icon = icon;

            stack = new FlyoutStack(p) { Width = Em * 20, Location = new Point(Em, Em), Font = Font };
            Controls.Add(stack);
            Build();
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
            Refit();
            CenterToScreen();
        }

        public void Refit()
        {
            stack.Relayout();
            ClientSize = new Size(stack.Width + Em * 2, stack.Height + Em * 2);
        }

        void Build()
        {
            stack.Add(new FlyoutSegmented(p, null,
                new[] { Loc.T("Settings.Keyboard"), Loc.T("Settings.Controller") },
                () => device, i => { device = i; Refresh(); }));

            int labelWidth = 0;
            foreach (PetAction action in KeyBindings.Actions)
                labelWidth = Math.Max(labelWidth, TextRenderer.MeasureText(ActionName(action), Font).Width);
            labelWidth += Em * 2 / 3;

            foreach (PetAction action in KeyBindings.Actions)
            {
                var row = stack.Add(new BindingRow(p, ActionName(action), labelWidth));
                for (int i = 0; i < 3; i++)
                {
                    int slot = i;
                    var button = new FlyoutButton(p, "", tip);
                    button.Pressed += () => { if (CaptureSlot(action, slot)) Refresh(); };
                    row.Controls.Add(button);
                    slots.Add((action, slot, button));
                }
            }

            var reset = new FlyoutButtons(p);
            reset.Add(Loc.T("Settings.ResetBindings"), () =>
            {
                if (device == 0) keys.ResetDefaults(); else pad.ResetDefaults();
                Refresh();
            });
            stack.Add(reset);
            stack.Add(new FlyoutNote(p, Loc.T("Settings.BindingsNote")));
            Refresh();
        }

        // An empty slot is a dash, so the ones that do something stand out; the tooltip still
        // says so in words, and gives a name too long for the slot in full.
        new void Refresh()
        {
            foreach (var (action, slot, button) in slots)
            {
                bool bound = device == 0 ? keys.Get(action)[slot] != 0 : pad.Get(action)[slot] != PadButton.None;
                string name = device == 0 ? KeyName(keys.Get(action)[slot]) : PadButtonName(pad.Get(action)[slot]);
                button.Show(bound ? name : "—", !bound, name);
            }
        }

        bool CaptureSlot(PetAction action, int slot)
        {
            string title = Loc.Format("Keys.BindTitle", ActionName(action));
            if (device == 0)
            {
                using var dialog = new KeyCaptureDialog(title, Loc.T("Keys.CaptureHint"));
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                keys.Set(action, slot, dialog.CapturedKey);
            }
            else
            {
                using var dialog = new PadCaptureDialog(title, Loc.T("Pad.CaptureHint"));
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                pad.Set(action, slot, dialog.CapturedButton);
            }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>An action's name and its three slot buttons.</summary>
        sealed class BindingRow : Control
        {
            readonly FlyoutPalette p;
            readonly string label;
            readonly int labelWidth;

            public BindingRow(FlyoutPalette palette, string label, int labelWidth)
            {
                p = palette;
                this.label = label;
                this.labelWidth = labelWidth;
                BackColor = palette.Back;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            int Em => Font.Height;

            public override Size GetPreferredSize(Size proposed) => new Size(Width, Em * 3 / 2 + Em / 4);

            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                int gap = Em / 4, n = Controls.Count, x = labelWidth + Em / 3;
                int w = (Width - x - gap * (n - 1) - Em / 3) / Math.Max(1, n);
                foreach (Control c in Controls)
                {
                    c.Bounds = new Rectangle(x, Em / 8, w, Height - Em / 4);
                    x += w + gap;
                }
            }

            protected override void OnPaint(PaintEventArgs e)
                => TextRenderer.DrawText(e.Graphics, label, Font, new Rectangle(Em / 3, 0, labelWidth, Height), p.Text,
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);
        }

        static string ActionName(PetAction action) => action switch
        {
            PetAction.Left => Loc.T("Action.Left"),
            PetAction.Right => Loc.T("Action.Right"),
            PetAction.Up => Loc.T("Action.Up"),
            PetAction.Down => Loc.T("Action.Down"),
            PetAction.Jump => Loc.T("Action.Jump"),
            PetAction.Dash => Loc.T("Action.Dash"),
            PetAction.Grab => Loc.T("Action.Grab"),
            PetAction.CrouchDash => Loc.T("Action.CrouchDash"),
            PetAction.DeployElytra => Loc.T("Action.DeployElytra"),
            _ => action.ToString()
        };

        static string KeyName(int virtualKey) => virtualKey switch
        {
            0 => Loc.T("Keys.Unbound"),
            // Keys' own names for the modifiers are long for a slot and say "Menu" for Alt.
            (int)Keys.LShiftKey => "LShift",
            (int)Keys.RShiftKey => "RShift",
            (int)Keys.LControlKey => "LCtrl",
            (int)Keys.RControlKey => "RCtrl",
            (int)Keys.LMenu => "LAlt",
            (int)Keys.RMenu => "RAlt",
            (int)Keys.Return => "Enter",
            _ => ((Keys)virtualKey).ToString(),
        };

        static string PadButtonName(PadButton button) => button switch
        {
            PadButton.None => Loc.T("Keys.Unbound"),
            PadButton.A => Loc.T("Pad.A"),
            PadButton.B => Loc.T("Pad.B"),
            PadButton.X => Loc.T("Pad.X"),
            PadButton.Y => Loc.T("Pad.Y"),
            PadButton.LeftShoulder => Loc.T("Pad.LeftShoulder"),
            PadButton.RightShoulder => Loc.T("Pad.RightShoulder"),
            PadButton.LeftTrigger => Loc.T("Pad.LeftTrigger"),
            PadButton.RightTrigger => Loc.T("Pad.RightTrigger"),
            PadButton.LeftStick => Loc.T("Pad.LeftStick"),
            PadButton.RightStick => Loc.T("Pad.RightStick"),
            PadButton.Start => Loc.T("Pad.Start"),
            PadButton.Back => Loc.T("Pad.Back"),
            PadButton.DPadUp => Loc.T("Pad.DPadUp"),
            PadButton.DPadDown => Loc.T("Pad.DPadDown"),
            PadButton.DPadLeft => Loc.T("Pad.DPadLeft"),
            PadButton.DPadRight => Loc.T("Pad.DPadRight"),
            PadButton.LeftThumbstickUp => Loc.T("Pad.LeftStickUp"),
            PadButton.LeftThumbstickDown => Loc.T("Pad.LeftStickDown"),
            PadButton.LeftThumbstickLeft => Loc.T("Pad.LeftStickLeft"),
            PadButton.LeftThumbstickRight => Loc.T("Pad.LeftStickRight"),
            PadButton.RightThumbstickUp => Loc.T("Pad.RightStickUp"),
            PadButton.RightThumbstickDown => Loc.T("Pad.RightStickDown"),
            PadButton.RightThumbstickLeft => Loc.T("Pad.RightStickLeft"),
            PadButton.RightThumbstickRight => Loc.T("Pad.RightStickRight"),
            _ => button.ToString()
        };
    }
}
