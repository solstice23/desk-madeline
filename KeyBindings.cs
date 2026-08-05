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
            ResetDefaults(save: false);
            Load();
        }

        public bool IsDown(PetAction action)
        {
            lock (sync)
            {
                var binding = keys[action];
                for (int i = 0; i < binding.Length; i++)
                    if (binding[i] != 0 && Win32.KeyDown(binding[i])) return true;
                return false;
            }
        }

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
