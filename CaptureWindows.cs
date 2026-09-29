using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// Waits for a key for one binding slot. Backspace or Delete clears the slot and Esc leaves
    /// it as it was; so do the two buttons, for whoever reaches for the mouse.
    /// </summary>
    /// <remarks>
    /// Every key is taken before the window's own handling sees it -- arrows, Tab and Enter
    /// included -- since those move focus between the buttons otherwise, and the arrows are
    /// Celeste's own default movement keys.
    /// </remarks>
    sealed class KeyCaptureDialog : StatusWindow
    {
        public int CapturedKey { get; private set; }

        public KeyCaptureDialog(string title, string instructions) : base(title, 20)
        {
            ShowState("", P.AccentInk, title, instructions, null, b =>
            {
                b.Add(Loc.T("Keys.Clear"), () => { CapturedKey = 0; DialogResult = DialogResult.OK; });
                b.Add(Loc.T("Common.Cancel"), () => DialogResult = DialogResult.Cancel, accent: true);
            });
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (msg.Msg != WM_KEYDOWN && msg.Msg != WM_SYSKEYDOWN) return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) DialogResult = DialogResult.Cancel;
            else
            {
                CapturedKey = key == Keys.Back || key == Keys.Delete ? 0 : (int)key;
                DialogResult = DialogResult.OK;
            }
            return true;
        }
    }

    /// <summary>
    /// Waits for a controller button for one binding slot, polling the pad; Backspace or Delete
    /// on the keyboard clears the slot and Esc leaves it, as do the two buttons.
    /// </summary>
    sealed class PadCaptureDialog : StatusWindow
    {
        // Capture-only: a bind must be deliberate, so a stick or trigger has to travel
        // well past the gameplay thresholds before it counts as a press.
        const float CaptureThreshold = 0.5f;

        static readonly PadButton[] Candidates = (PadButton[])Enum.GetValues(typeof(PadButton));

        readonly Timer poll;
        readonly HashSet<PadButton> heldOnOpen = new HashSet<PadButton>();
        readonly string instructionText;
        bool sampledOpenState;
        bool showingDisconnected;

        public PadButton CapturedButton { get; private set; }

        public PadCaptureDialog(string title, string instructions) : base(title, 20)
        {
            instructionText = instructions;
            ShowState("", P.AccentInk, title, instructions, null, b =>
            {
                b.Add(Loc.T("Keys.Clear"), () => { CapturedButton = PadButton.None; DialogResult = DialogResult.OK; });
                b.Add(Loc.T("Common.Cancel"), () => DialogResult = DialogResult.Cancel, accent: true);
            });
            poll = new Timer { Interval = 16 };
            poll.Tick += (_, __) => Sample();
            poll.Start();
        }

        void Sample()
        {
            PadState state = XInputPad.Poll();
            if (!state.Connected)
            {
                // Otherwise an unplugged controller just looks like a window that ignores input.
                if (!showingDisconnected)
                {
                    showingDisconnected = true;
                    Status.Set("", Warn, Text, Loc.T("Pad.NoController") + "\n" + instructionText);
                    Resettle();
                }
                return;
            }
            if (showingDisconnected)
            {
                showingDisconnected = false;
                Status.Set("", P.AccentInk, Text, instructionText);
                Resettle();
            }
            // Buttons already held when the window opened (a trigger still down from the
            // menu click, a resting stick) only arm once they have been released.
            if (!sampledOpenState)
            {
                sampledOpenState = true;
                foreach (PadButton button in Candidates)
                    if (button != PadButton.None && state.Check(button, CaptureThreshold))
                        heldOnOpen.Add(button);
                return;
            }
            foreach (PadButton button in Candidates)
            {
                if (button == PadButton.None) continue;
                if (!state.Check(button, CaptureThreshold)) { heldOnOpen.Remove(button); continue; }
                if (heldOnOpen.Contains(button)) continue;
                CapturedButton = button;
                DialogResult = DialogResult.OK;
                return;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                CapturedButton = PadButton.None;
                DialogResult = DialogResult.OK;
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);   // Esc closes, Enter presses Cancel
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            poll.Stop();
            poll.Dispose();
            base.OnFormClosed(e);
        }
    }
}
