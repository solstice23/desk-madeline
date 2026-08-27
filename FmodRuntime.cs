using System;
using System.Diagnostics;
using System.IO;

namespace DeskMadeline
{
    /// <summary>The FMOD runtime a folder holds: which two files it is, and what they are.</summary>
    /// <remarks>
    /// Celeste does not keep FMOD in one place, and a folder is whichever of these it happens
    /// to be:
    ///
    ///   fmod.dll, fmodstudio.dll                     beside Celeste.exe -- the plain game,
    ///                                                which is the 32-bit XNA build
    ///   lib64-win-x64\fmod64.dll, fmodstudio.dll     Everest's 64-bit FNA build
    ///   lib64-win-x64\fmod64.dll, fmodstudio64.dll   the same, from a newer lib set
    ///
    /// The last two are one file under two names -- identical bytes, and fmodstudio64.dll is
    /// the name FMOD itself ships it under. Older Everest lib sets renamed it so that FNA's
    /// DllImport("fmodstudio") would find it; newer ones leave the name alone and resolve it
    /// in code. Either way the studio library imports fmod64.dll, so the core is loaded first,
    /// by full path, and the implicit load then finds the module already there.
    ///
    /// The pet is a 64-bit process, so only the last two can be loaded at all: a copy of
    /// Celeste that Everest has not converted has no FMOD this can use, and saying that
    /// plainly -- rather than reporting a file as missing that no plain install ever had --
    /// is half of what this class is for.
    /// </remarks>
    internal sealed class FmodRuntime
    {
        /// <summary>What Celeste 1.4 carries, and what to assume when a file will not say.</summary>
        public const uint DefaultVersion = 0x00011014;   // FMOD 1.10.14, digit for digit

        /// <summary>Where each build keeps the pair: subfolder, core, studio.</summary>
        /// <remarks>
        /// The two in the middle are neither build's layout: they are the pair dropped straight
        /// into a folder, which is what someone whose Celeste has no 64-bit FMOD can do about
        /// it, the folders looked in including the one the pet itself is in.
        /// </remarks>
        static readonly string[][] Layouts =
        {
            new[] { "lib64-win-x64", "fmod64.dll", "fmodstudio64.dll" },
            new[] { "lib64-win-x64", "fmod64.dll", "fmodstudio.dll" },
            new[] { "",              "fmod64.dll", "fmodstudio64.dll" },
            new[] { "",              "fmod64.dll", "fmodstudio.dll" },
            new[] { "",              "fmod.dll",   "fmodstudio.dll" },
        };

        FmodRuntime(string core, string studio)
        {
            Core = core;
            Studio = studio;
            switch (Machine(core))
            {
                case 0x8664: Bits = 64; break;
                case 0x014c: Bits = 32; break;
            }
            Version = DefaultVersion;
            VersionText = "1.10.14";
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(studio);
                if (info.FileMajorPart > 0)
                {
                    Version = Encode(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
                    VersionText = info.FileMajorPart + "." + info.FileMinorPart + "." + info.FileBuildPart;
                }
            }
            catch { }
        }

        /// <summary>fmod64.dll, or fmod.dll. Loaded first: the studio library imports it.</summary>
        public string Core { get; }
        /// <summary>fmodstudio64.dll, or fmodstudio.dll. Every entry point used comes from here.</summary>
        public string Studio { get; }
        /// <summary>64 or 32, as the PE header says; 0 when the file is not one.</summary>
        public int Bits { get; }
        /// <summary>The version FMOD_Studio_System_Create is to be given.</summary>
        /// <remarks>
        /// Read from the library rather than assumed: create fails outright on a mismatch, and
        /// a version taken from the file is right for whichever 1.10.x an install carries.
        /// </remarks>
        public uint Version { get; }
        public string VersionText { get; }

        /// <summary>Whether this process can load it at all. Bitness has to match exactly.</summary>
        public bool Usable => Bits == (Environment.Is64BitProcess ? 64 : 32);

        /// <summary>What was found, for the log: name, version, bitness and where it is.</summary>
        public string Describe()
            => Path.GetFileName(Studio) + " " + VersionText + ", " + Bits + "-bit, in " +
               Path.GetDirectoryName(Studio);

        /// <summary>
        /// The runtime in the first of these folders that has one, preferring one this process
        /// can load over one it cannot -- a folder that holds only the 32-bit pair is still
        /// worth returning, since it is the answer to why there is no sound.
        /// </summary>
        public static FmodRuntime Locate(params string[] folders)
        {
            FmodRuntime unusable = null;
            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                foreach (string[] layout in Layouts)
                {
                    string dir = layout[0].Length == 0 ? folder : Path.Combine(folder, layout[0]);
                    string core = Path.Combine(dir, layout[1]);
                    string studio = Path.Combine(dir, layout[2]);
                    if (!File.Exists(core) || !File.Exists(studio)) continue;
                    var runtime = new FmodRuntime(core, studio);
                    if (runtime.Usable) return runtime;
                    if (unusable == null) unusable = runtime;
                }
            }
            return unusable;
        }

        /// <summary>The architecture in a PE header, or 0 for anything that has none.</summary>
        static int Machine(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);
                if (stream.Length < 0x40) return 0;
                stream.Position = 0x3c;
                int header = reader.ReadInt32();
                if (header <= 0 || header + 6 > stream.Length) return 0;
                stream.Position = header;
                if (reader.ReadUInt32() != 0x00004550) return 0;   // "PE\0\0"
                return reader.ReadUInt16();
            }
            catch { return 0; }
        }

        /// <summary>FMOD writes a version as its own decimal digits in hex: 1.10.14 is 0x00011014.</summary>
        static uint Encode(int major, int minor, int patch)
            => (uint)((Digits(major) << 16) | (Digits(minor) << 8) | Digits(patch));

        static int Digits(int value)
            => value < 0 || value > 99 ? 0 : ((value / 10) << 4) | (value % 10);
    }
}
