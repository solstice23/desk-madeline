using System;
using System.IO;
using DeskMadeline;

/// <summary>Which FMOD a folder holds, in each of the layouts Celeste is shipped in.</summary>
/// <remarks>
/// The pet used to look for exactly one pair of names in exactly one folder, so a Celeste that
/// keeps its FMOD anywhere else was silent and called incomplete besides. The layouts are
/// listed in FmodRuntime; these build one folder per layout out of stub DLLs -- a PE header is
/// all Locate reads of them -- and ask what it makes of each.
/// </remarks>
static class FmodChecks
{
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    /// <summary>A file just enough of a PE for the architecture to be read out of it.</summary>
    static void Stub(string path, int machine)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var bytes = new byte[0x50];
        bytes[0] = (byte)'M'; bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x40).CopyTo(bytes, 0x3c);          // where the PE header is
        BitConverter.GetBytes(0x00004550).CopyTo(bytes, 0x40);    // "PE\0\0"
        BitConverter.GetBytes((ushort)machine).CopyTo(bytes, 0x44);
        File.WriteAllBytes(path, bytes);
    }

    const int X64 = 0x8664, X86 = 0x014c;

    static string Layout(string root, string name, string subfolder, string core, string studio,
        int machine)
    {
        string folder = Path.Combine(root, name);
        string dir = subfolder.Length == 0 ? folder : Path.Combine(folder, subfolder);
        Stub(Path.Combine(dir, core), machine);
        Stub(Path.Combine(dir, studio), machine);
        return folder;
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("FMOD RUNTIME as each build of Celeste keeps it");
        Console.WriteLine(new string('=', 74));

        string root = Path.Combine(Path.GetTempPath(), "deskmadeline-fmod-check");
        try { Directory.Delete(root, true); } catch { }
        Directory.CreateDirectory(root);

        string everest = Layout(root, "everest", "lib64-win-x64",
            "fmod64.dll", "fmodstudio.dll", X64);
        string newer = Layout(root, "everest-newer", "lib64-win-x64",
            "fmod64.dll", "fmodstudio64.dll", X64);
        string plain = Layout(root, "plain", "", "fmod.dll", "fmodstudio.dll", X86);
        string nothing = Path.Combine(root, "nothing");
        Directory.CreateDirectory(nothing);

        Console.WriteLine();
        Console.WriteLine("  Where it is, and under which name");
        var found = FmodRuntime.Locate(everest);
        Check(@"Everest's build: lib64-win-x64\fmodstudio.dll",
            found != null && Path.GetFileName(found.Studio) == "fmodstudio.dll" && found.Bits == 64);
        found = FmodRuntime.Locate(newer);
        Check(@"a newer lib set, which does not rename it: lib64-win-x64\fmodstudio64.dll",
            found != null && Path.GetFileName(found.Studio) == "fmodstudio64.dll" && found.Bits == 64);
        Check("and its core is fmod64.dll, which the studio library imports by that name",
            found != null && Path.GetFileName(found.Core) == "fmod64.dll");
        string dropped = Layout(root, "dropped-in", "", "fmod64.dll", "fmodstudio64.dll", X64);
        Check("the pair dropped straight into a folder, which is the way out for a copy of " +
              "Celeste that has no 64-bit FMOD of its own",
            FmodRuntime.Locate(dropped)?.Bits == 64);
        found = FmodRuntime.Locate(plain);
        Check("the plain game: fmod.dll beside Celeste.exe, 32-bit",
            found != null && found.Bits == 32);
        Check("which a 64-bit pet cannot load, and is told so rather than left to guess",
            found != null && !found.Usable && Environment.Is64BitProcess);
        Check("a folder with neither has none", FmodRuntime.Locate(nothing) == null);
        Check("and neither has no folder at all", FmodRuntime.Locate((string)null) == null);

        Console.WriteLine();
        Console.WriteLine("  Which of several folders is taken");
        Check("one that can be loaded beats one that cannot, whichever comes first",
            FmodRuntime.Locate(plain, everest)?.Bits == 64);
        Check("and when none can be, the one that is there is still returned, to be explained",
            FmodRuntime.Locate(plain, nothing)?.Bits == 32);

        Console.WriteLine();
        Console.WriteLine("  What an install is judged on");
        string install = Path.Combine(root, "install");
        foreach (string file in new[]
        {
            @"Celeste.exe", @"Content\Graphics\Atlases\Gameplay.meta",
            @"Content\Graphics\Atlases\Gameplay0.data",
            @"Content\FMOD\Desktop\Master Bank.bank", @"Content\FMOD\Desktop\Master Bank.strings.bank",
            @"Content\FMOD\Desktop\sfx.bank", @"Content\FMOD\Desktop\dlc_sfx.bank",
        })
        {
            string full = Path.Combine(install, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, "");
        }
        Check("a copy of Celeste with no 64-bit FMOD is complete all the same, since no plain " +
              "one has ever had it and offering to go and find a better copy is a dead end",
            CelesteInstall.IsComplete(install));
        Check("its banks are found", CelesteInstall.BanksDirectory(install) != null);
        Check("but it is not a folder to take audio from whole",
            !CelesteInstall.HasAudio(install));
        File.Delete(Path.Combine(install, @"Content\FMOD\Desktop\Master Bank.bank"));
        Check("and losing a bank is missing a file, which is said outright",
            !CelesteInstall.IsComplete(install) && CelesteInstall.BanksDirectory(install) == null);

        Console.WriteLine();
        Console.WriteLine("  The runtime an installed Celeste actually has");
        string real = CelesteInstall.Directory;
        var installed = real == null ? null : FmodRuntime.Locate(real);
        if (installed == null)
            Console.WriteLine("    ..    no Celeste installed, or one with no FMOD at all");
        else
        {
            Console.WriteLine("    ..    " + installed.Describe());
            // The version is read out of the library rather than assumed, and the assumption is
            // still what every known build carries: if those two ever disagree, one is wrong.
            Check("its version is FMOD 1.x, which these bindings are",
                installed.Version >> 16 == 1);
            Check("and 1.10.14 reads back as 0x00011014, FMOD's own way of writing it",
                installed.VersionText != "1.10.14" ||
                installed.Version == FmodRuntime.DefaultVersion);
        }

        try { Directory.Delete(root, true); } catch { }
        return failed;
    }
}
