using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeskMadeline
{
    /// <summary>Per-user Windows sign-in registration; no elevation required.</summary>
    internal static class StartupRegistration
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "DeskMadeline";

        internal static string Command => "\"" + Application.ExecutablePath + "\"";

        public static bool IsEnabled()
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            string registered = key?.GetValue(ValueName) as string;
            // A registration for an old install location is not active for this
            // copy. Enabling the option replaces it with the current absolute path.
            return string.Equals(registered?.Trim(), Command,
                StringComparison.OrdinalIgnoreCase);
        }

        public static void SetEnabled(bool enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
            if (enabled)
                key.SetValue(ValueName, Command, RegistryValueKind.String);
            else
                key.DeleteValue(ValueName, false);
        }
    }
}
