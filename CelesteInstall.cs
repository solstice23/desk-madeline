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
        /// <summary>An install written down rather than discovered. Also read by the csproj.</summary>
        public const string ConfigFileName = "celeste-path.txt";

        static bool searched;
        static string directory;
        static string chosen;

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
        /// The install the user picked, which is remembered in settings.txt and wins over
        /// anything found by looking. Setting it forgets whatever was worked out before.
        /// </summary>
        public static string Chosen
        {
            get => chosen;
            set
            {
                chosen = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                searched = false;
            }
        }

        /// <summary>Whether a folder is an install rather than merely a folder.</summary>
        public static bool IsInstall(string path)
            => !string.IsNullOrWhiteSpace(path) &&
               File.Exists(Path.Combine(path, "Celeste.exe"));

        /// <summary>Whether a build carries the game's own content and so needs no install.</summary>
        public static bool HasBundledContent => File.Exists(Path.Combine(BundledAtlases, "Gameplay.meta"));

        static string BundledAtlases => Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "Content", "Graphics", "Atlases");

        /// <summary>
        /// Where the atlases are: beside the app when a build carries them, otherwise inside
        /// the install. The same arrangement SoundEffects uses for the FMOD banks.
        /// </summary>
        public static string AtlasesDirectory
        {
            get
            {
                if (HasBundledContent) return BundledAtlases;
                return Directory == null ? null
                    : Path.Combine(Directory, "Content", "Graphics", "Atlases");
            }
        }

        /// <summary>
        /// The install someone has named: CELESTE_PATH, or a celeste-path.txt holding the
        /// path on one line, beside the app or in any folder above it.
        /// </summary>
        /// <remarks>
        /// The file is what saves naming an install over and over, on the command line and in
        /// the environment, and it is looked for above the app as well so that one written at
        /// the root of a checkout serves everything built out of it. It is not copied
        /// anywhere: a path from one machine means nothing on another.
        /// </remarks>
        static string Configured()
        {
            string environment = Environment.GetEnvironmentVariable("CELESTE_PATH");
            if (!string.IsNullOrWhiteSpace(environment)) return environment.Trim();
            if (!string.IsNullOrWhiteSpace(chosen)) return chosen;

            var folder = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int up = 0; up < 8 && folder != null; up++, folder = folder.Parent)
            {
                string file = Path.Combine(folder.FullName, ConfigFileName);
                if (!File.Exists(file)) continue;
                try
                {
                    foreach (string line in File.ReadAllLines(file))
                    {
                        string text = line.Trim();
                        if (text.Length > 0 && !text.StartsWith("#")) return text;
                    }
                }
                catch { }
            }
            return null;
        }

        static string Find()
        {
            var candidates = new List<string>();
            string explicitPath = Configured();
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
