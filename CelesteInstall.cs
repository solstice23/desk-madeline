using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DeskMadeline
{
    /// <summary>Where Celeste is installed, if it is.</summary>
    /// <remarks>
    /// Its own files are what the pet borrows rather than ships: FMOD banks for sound, and
    /// atlases for artwork. Set CELESTE_PATH to point at an install this cannot work out.
    /// </remarks>
    internal static class CelesteInstall
    {
        static bool searched;
        static string directory;

        /// <summary>The install directory, or null when there is none. Found once and kept.</summary>
        public static string Directory
        {
            get
            {
                if (!searched) { directory = Find(); searched = true; }
                return directory;
            }
        }

        /// <summary>
        /// Where the atlases are: beside the app when a build carries them, otherwise inside
        /// the install. The same arrangement SoundEffects uses for the FMOD banks.
        /// </summary>
        public static string AtlasesDirectory
        {
            get
            {
                string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "Content", "Graphics", "Atlases");
                if (File.Exists(Path.Combine(bundled, "Gameplay.meta"))) return bundled;
                return Directory == null ? null
                    : Path.Combine(Directory, "Content", "Graphics", "Atlases");
            }
        }

        static string Find()
        {
            var candidates = new List<string>();
            string explicitPath = Environment.GetEnvironmentVariable("CELESTE_PATH");
            if (!string.IsNullOrWhiteSpace(explicitPath)) candidates.Add(explicitPath);
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", "Celeste"));

            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                string steam = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(steam))
                {
                    candidates.Add(Path.Combine(steam, "steamapps", "common", "Celeste"));
                    string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraries))
                    {
                        foreach (Match match in Regex.Matches(File.ReadAllText(libraries),
                            "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\""))
                        {
                            string root = match.Groups[1].Value.Replace("\\\\", "\\");
                            candidates.Add(Path.Combine(root, "steamapps", "common", "Celeste"));
                        }
                    }
                }
            }
            catch { }

            foreach (DriveInfo drive in DriveInfo.GetDrives())
                if (drive.IsReady)
                    candidates.Add(Path.Combine(drive.RootDirectory.FullName,
                        "SteamLibrary", "steamapps", "common", "Celeste"));

            foreach (string path in candidates)
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "Celeste.exe")))
                    return Path.GetFullPath(path);
            return null;
        }
    }
}
