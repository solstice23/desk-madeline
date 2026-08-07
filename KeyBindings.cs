using System;
using System.Collections.Generic;
using System.IO;

namespace DeskMadeline
{
    internal enum PetAction
    {
        Left,
        Right,
        Up,
        Down,
        Jump,
        Dash,
        Grab,
        CrouchDash,
        DeployElytra
    }

    /// <summary>Celeste-style keyboard bindings: exactly three keyboard slots per gameplay action.</summary>
    internal sealed class KeyBindings
    {
        readonly object sync = new object();
        readonly Dictionary<PetAction, int[]> keys = new Dictionary<PetAction, int[]>();
        // Per key, not per action: see Poll.
        readonly Dictionary<PetAction, bool[]> down = new Dictionary<PetAction, bool[]>();
        readonly Dictionary<PetAction, bool[]> wasDown = new Dictionary<PetAction, bool[]>();
        readonly string path;

        public static readonly PetAction[] Actions =
        {
            PetAction.Left, PetAction.Right, PetAction.Up, PetAction.Down,
            PetAction.Jump, PetAction.Dash, PetAction.Grab, PetAction.CrouchDash,
            PetAction.DeployElytra
        };

        public KeyBindings(string path)
        {
            this.path = path;
            foreach (PetAction action in Actions)
            {
                down[action] = new bool[3];
                wasDown[action] = new bool[3];
            }
            ResetDefaults(save: false);
            Load();
        }

        /// <summary>
        /// One reading of the keyboard for the frame, as MInput.Update takes one: every bound
        /// key, kept beside what it was the frame before.
        /// </summary>
        /// <remarks>
        /// Each key is remembered separately rather than as one "is anything bound to this
        /// down", because Binding.Pressed asks every key for its own edge -- hold one of the
        /// keys bound to jump, press another, and Celeste jumps. Reading the whole binding as a
        /// single button loses that: the button was already down, so nothing happened.
        ///
        /// It runs on every frame, including the ones where the pet is not listening, so that
        /// a key held down while typing somewhere else is not a fresh press the moment the pet
        /// is focused again.
        /// </remarks>
        public void Poll()
        {
            lock (sync)
            {
                foreach (PetAction action in Actions)
                {
                    int[] binding = keys[action];
                    bool[] now = down[action], before = wasDown[action];
                    for (int i = 0; i < binding.Length; i++)
                    {
                        before[i] = now[i];
                        now[i] = binding[i] != 0 && ReadKey(binding[i]);
                    }
                }
            }
        }

        /// <summary>Binding.Check: any of them down.</summary>
        public bool IsDown(PetAction action)
        {
            lock (sync)
            {
                bool[] now = down[action];
                for (int i = 0; i < now.Length; i++) if (now[i]) return true;
                return false;
            }
        }

        /// <summary>Binding.Pressed: any of them going down, whether or not a sibling is held.</summary>
        public bool Pressed(PetAction action)
        {
            lock (sync)
            {
                bool[] now = down[action], before = wasDown[action];
                for (int i = 0; i < now.Length; i++) if (now[i] && !before[i]) return true;
                return false;
            }
        }

        /// <summary>Where the keyboard is read from; the checks hand it one they can drive.</summary>
        internal Func<int, bool> ReadKey = Win32.KeyDown;

        public int[] Get(PetAction action)
        {
            lock (sync) return (int[])keys[action].Clone();
        }

        public void Set(PetAction action, int slot, int virtualKey)
        {
            if (slot < 0 || slot >= 3) return;
            lock (sync)
            {
                var binding = keys[action];
                // A key appears at most once within an action, matching Celeste's Binding.Add toggle.
                if (virtualKey != 0)
                    for (int i = 0; i < binding.Length; i++)
                        if (i != slot && binding[i] == virtualKey) binding[i] = 0;
                binding[slot] = virtualKey;
                SaveLocked();
            }
        }

        public void ResetDefaults(bool save = true)
        {
            lock (sync)
            {
                // Celeste's keyboard defaults: arrows to move, C/X, and Z/V/Left Shift to grab.
                // Crouch Dash is unbound by default in Celeste.
                keys[PetAction.Left] = new[] { 0x25, 0, 0 };
                keys[PetAction.Right] = new[] { 0x27, 0, 0 };
                keys[PetAction.Up] = new[] { 0x26, 0, 0 };
                keys[PetAction.Down] = new[] { 0x28, 0, 0 };
                keys[PetAction.Jump] = new[] { 0x43, 0, 0 };
                keys[PetAction.Dash] = new[] { 0x58, 0, 0 };
                keys[PetAction.Grab] = new[] { 0x5A, 0x56, 0xA0 };
                keys[PetAction.CrouchDash] = new[] { 0, 0, 0 };
                // CommunalHelper's default keyboard binding is W.
                keys[PetAction.DeployElytra] = new[] { 0x57, 0, 0 };
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
                        var binding = new int[3];
                        for (int i = 0; i < binding.Length && i < values.Length; i++)
                            int.TryParse(values[i], out binding[i]);
                        keys[action] = binding;
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
                    "# DeskMadeline virtual-key bindings; three slots per action (0 = unbound)."
                };
                foreach (var action in Actions)
                    lines.Add(action + "=" + string.Join(",", keys[action]));
                File.WriteAllLines(path, lines);
            }
            catch { }
        }
    }
}
