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

        /// <summary>Everything the pet reads out of an install, and so everything one needs.</summary>
        /// <remarks>
        /// Celeste.exe alone says only that a folder is Celeste's. A half-copied, half-deleted
        /// or half-updated one has it and little else, and taking that for an install is how
        /// the pet ends up with no sprites or no sound and nothing said about why. Her portrait
        /// is not here: assets\portrait.png ships beside the app and is what the tray icon uses.
        ///
        /// Neither is the FMOD runtime, which used to be, as lib64-win-x64\fmod64.dll and
        /// fmodstudio.dll. Only an install Everest has converted to its 64-bit FNA build has
        /// those, so listing them called every plain copy of Celeste broken and offered to go
        /// looking for a better one, of which there is none. What a folder can do about sound
        /// is <see cref="FmodRuntime"/>'s question, and it is asked where the answer can be
        /// acted on: the log, and the tray menu beside the sound settings.
        /// </remarks>
        static readonly string[] Required =
        {
            "Celeste.exe",
            "Content/Graphics/Atlases/Gameplay.meta",
            "Content/Graphics/Atlases/Gameplay0.data",
            "Content/FMOD/Desktop/Master Bank.bank",
            "Content/FMOD/Desktop/Master Bank.strings.bank",
            "Content/FMOD/Desktop/sfx.bank",
            "Content/FMOD/Desktop/dlc_sfx.bank",   // the jellyfish is a Farewell mechanic
        };

        /// <summary>What an install is missing of those, in the game's own layout.</summary>
        public static List<string> MissingFrom(string path)
        {
            var missing = new List<string>();
            foreach (string file in Required)
                if (string.IsNullOrWhiteSpace(path) ||
                    !File.Exists(Path.Combine(path, file.Replace('/', Path.DirectorySeparatorChar))))
                    missing.Add(file.Replace('/', Path.DirectorySeparatorChar));
            return missing;
        }

        /// <summary>Whether an install has everything the pet reads.</summary>
        public static bool IsComplete(string path) => MissingFrom(path).Count == 0;

        /// <summary>Whether a build carries the game's artwork and so needs no install for it.</summary>
        public static bool HasBundledContent => File.Exists(Path.Combine(BundledAtlases, "Gameplay.meta"));

        /// <summary>The same for the FMOD runtime and banks, which travel separately.</summary>
        public static bool HasBundledAudio => HasAudio(AppDomain.CurrentDomain.BaseDirectory);

        /// <summary>Whether a folder holds the FMOD runtime and banks, bundled or installed.</summary>
        public static bool HasAudio(string directory)
            => FmodRuntime.Locate(directory) != null && BanksDirectory(directory) != null;

        /// <summary>
        /// The folder holding the sound banks, beside the app or inside an install, or null
        /// when it holds none. The banks themselves are the same in every copy of the game.
        /// </summary>
        public static string BanksDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return null;
            string banks = Path.Combine(directory, "Content", "FMOD", "Desktop");
            return File.Exists(Path.Combine(banks, "Master Bank.bank")) ? banks : null;
        }

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
        /// A file of the game's under Content\Graphics, beside the app or in the install --
        /// Sprites.xml, which says where her hair sits on every frame of every animation.
        /// </summary>
        public static string GraphicsFile(string name)
        {
            string beside = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Content", "Graphics", name);
            if (File.Exists(beside)) return beside;
            if (Directory == null) return null;
            string installed = Path.Combine(Directory, "Content", "Graphics", name);
            return File.Exists(installed) ? installed : null;
        }

        /// <summary>
        /// The install someone has named: CELESTE_PATH, or the folder chosen in settings.
        /// </summary>
        /// <remarks>
        /// celeste-path.txt is not among these. It belongs to a development tree -- the build
        /// reads it, and so does tools\dump-reference.ps1 -- and having the app read it too made
        /// three places to look for one answer, two of them invisible to whoever is running the
        /// pet.
        /// </remarks>
        static string Configured()
        {
            string environment = Environment.GetEnvironmentVariable("CELESTE_PATH");
            if (!string.IsNullOrWhiteSpace(environment)) return environment.Trim();
            return string.IsNullOrWhiteSpace(chosen) ? null : chosen;
        }

        /// <summary>
        /// DESKMADELINE_NO_CELESTE=1 makes looking find nothing, so what a machine without the
        /// game sees -- the explanation, the folder picker, the setting being written -- can be
        /// walked through on one that has it. A folder actually chosen still counts, since the
        /// point of choosing one is that it then works.
        /// </summary>
        static bool PretendMissing
            => Environment.GetEnvironmentVariable("DESKMADELINE_NO_CELESTE") == "1";

        static string Find() => Search(false);

        /// <summary>
        /// Where looking alone says the game is, with no regard for anything named. Nothing is
        /// remembered, so this is what the tray menu shows and offers next to what is in use.
        /// </summary>
        public static string Detected() => Search(true);

        static string Search(bool ignoreNamed)
        {
            // A folder someone named wins outright, whole or not: it was an answer to this
            // question, and quietly using a different copy of the game instead of saying what
            // is wrong with the named one would be no answer at all.
            string explicitPath = ignoreNamed ? null : Configured();
            if (IsInstall(explicitPath)) return Path.GetFullPath(explicitPath);
            if (PretendMissing) return null;

            var candidates = new List<string>();
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

            // A whole install first, wherever it is; a broken one only if there is no other,
            // and then it is still worth returning, so that what is wrong with it can be said
            // rather than reporting no Celeste at all with one plainly sitting there.
            foreach (string path in candidates)
                if (IsComplete(path)) return Path.GetFullPath(path);
            foreach (string path in candidates)
                if (IsInstall(path)) return Path.GetFullPath(path);
            return null;
        }
    }
}
