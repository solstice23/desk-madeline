using System;
using System.Collections.Generic;
using System.IO;

namespace DeskMadeline
{
    /// <summary>Celeste-style controller bindings: exactly three controller slots per gameplay action.</summary>
    internal sealed class PadBindings
    {
        // Celeste's per-virtual-input thresholds (Celeste.Input.Initialize):
        // MoveX 0.3, MoveY 0.7, GliderMoveY 0.3, Aim 0.25, and 0.2 for every VirtualButton.
        public const float MoveXThreshold = 0.3f;
        public const float MoveYThreshold = 0.7f;
        public const float GliderMoveYThreshold = 0.3f;
        public const float AimThreshold = 0.25f;
        public const float ButtonThreshold = 0.2f;

        readonly object sync = new object();
        readonly Dictionary<PetAction, PadButton[]> buttons = new Dictionary<PetAction, PadButton[]>();
        readonly string path;

        public PadBindings(string path)
        {
            this.path = path;
            ResetDefaults(save: false);
            Load();
        }

        public bool IsDown(PadState state, PetAction action, float threshold)
        {
            if (!state.Connected) return false;
            lock (sync)
            {
                var binding = buttons[action];
                for (int i = 0; i < binding.Length; i++)
                    if (binding[i] != PadButton.None && state.Check(binding[i], threshold)) return true;
                return false;
            }
        }

        /// <summary>
        /// Binding.Pressed for the pad half: any one bound button going down counts, even while
        /// another bound to the same action is already held. The two readings are handed in
        /// rather than remembered here, since the poll already keeps them.
        /// </summary>
        public bool Pressed(PadState state, PadState before, PetAction action, float threshold)
        {
            if (!state.Connected) return false;
            lock (sync)
            {
                var binding = buttons[action];
                for (int i = 0; i < binding.Length; i++)
                    if (binding[i] != PadButton.None && state.Check(binding[i], threshold) &&
                        !before.Check(binding[i], threshold)) return true;
                return false;
            }
        }

        public PadButton[] Get(PetAction action)
        {
            lock (sync) return (PadButton[])buttons[action].Clone();
        }

        public void Set(PetAction action, int slot, PadButton button)
        {
            if (slot < 0 || slot >= 3) return;
            lock (sync)
            {
                var binding = buttons[action];
                // A button appears at most once within an action, matching Celeste's Binding.Add toggle.
                if (button != PadButton.None)
                    for (int i = 0; i < binding.Length; i++)
                        if (i != slot && binding[i] == button) binding[i] = PadButton.None;
                binding[slot] = button;
                SaveLocked();
            }
        }

        public void ResetDefaults(bool save = true)
        {
            lock (sync)
            {
                // Celeste Settings.SetDefaultButtonControls.
                buttons[PetAction.Left] = new[] { PadButton.LeftThumbstickLeft, PadButton.DPadLeft, PadButton.None };
                buttons[PetAction.Right] = new[] { PadButton.LeftThumbstickRight, PadButton.DPadRight, PadButton.None };
                buttons[PetAction.Up] = new[] { PadButton.LeftThumbstickUp, PadButton.DPadUp, PadButton.None };
                buttons[PetAction.Down] = new[] { PadButton.LeftThumbstickDown, PadButton.DPadDown, PadButton.None };
                buttons[PetAction.Jump] = new[] { PadButton.A, PadButton.Y, PadButton.None };
                buttons[PetAction.Dash] = new[] { PadButton.X, PadButton.B, PadButton.None };
                // Celeste also defaults Grab to RightShoulder; the fourth binding does not fit
                // this pet's three slots, so the shoulder pair is truncated to LeftShoulder.
                buttons[PetAction.Grab] = new[] { PadButton.LeftTrigger, PadButton.RightTrigger, PadButton.LeftShoulder };
                // Crouch Dash (DemoDash) has no controller default in Celeste.
                buttons[PetAction.CrouchDash] = new[] { PadButton.None, PadButton.None, PadButton.None };
                // CommunalHelper's upstream controller default for Deploy Elytra is not available in
                // the reference material here, so the slot is left unbound instead of guessed.
                buttons[PetAction.DeployElytra] = new[] { PadButton.None, PadButton.None, PadButton.None };
                if (save) SaveLocked();
            }
        }

        void Load()
        {
            try
            {
                if (!File.Exists(path)) return;
                lock (sync)
                {
                    foreach (string raw in File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        int equals = line.IndexOf('=');
                        if (equals <= 0 || !Enum.TryParse(line.Substring(0, equals), true, out PetAction action)) continue;
                        string[] values = line.Substring(equals + 1).Split(',');
                        var binding = new PadButton[3];
                        for (int i = 0; i < binding.Length && i < values.Length; i++)
                            if (!Enum.TryParse(values[i].Trim(), true, out binding[i]) ||
                                !Enum.IsDefined(typeof(PadButton), binding[i]))
                                binding[i] = PadButton.None;
                        buttons[action] = binding;
                    }
                }
            }
            catch { }
        }

        void SaveLocked()
        {
            try
            {
                var lines = new List<string>
                {
                    "# DeskMadeline controller bindings; three slots per action (None = unbound)."
                };
                foreach (var action in KeyBindings.Actions)
                    lines.Add(action + "=" + string.Join(",", buttons[action]));
                File.WriteAllLines(path, lines);
            }
            catch { }
        }
    }
}
