using System;
using System.IO;
using DeskMadeline;

/// <summary>Do the events the port asks for resolve in the installed Celeste banks?</summary>
/// <remarks>
/// SoundEffects.Play logs "SFX event failed" and swallows the error, so an event that is not
/// there is silent rather than obvious -- indistinguishable from a move that forgot to play
/// one. Opt in with SFXCHECK=1, since this really does play each event, at 1% volume, and
/// needs Celeste installed.
/// </remarks>
static class SoundBankChecks
{
    static readonly string[] Events =
    {
        "event:/char/madeline/jump",
        "event:/char/madeline/jump_super",        // super
        "event:/char/madeline/jump_superslide",   // hyper
        "event:/char/madeline/jump_superwall",
        "event:/char/madeline/jump_wall_left",
        "event:/char/madeline/jump_wall_right",
        "event:/char/madeline/jump_climb_left",
        "event:/char/madeline/jump_climb_right",
        "event:/char/madeline/jump_dreamblock",
        "event:/char/madeline/dash_red_left",
        "event:/char/madeline/dash_red_right",
        "event:/char/madeline/dash_pink_left",
        "event:/char/madeline/dash_pink_right",
        "event:/char/madeline/landing",
        "event:/char/madeline/footstep",
        "event:/char/madeline/duck",
        "event:/char/madeline/stand",
        "event:/char/madeline/grab",
        "event:/char/madeline/grab_letgo",
        "event:/char/madeline/handhold",
        "event:/char/madeline/wallslide",
        "event:/char/madeline/death",
        "event:/char/madeline/predeath",
        "event:/char/madeline/revive",
        "event:/char/madeline/dreamblock_enter",
        "event:/char/madeline/dreamblock_travel",
        "event:/char/madeline/dreamblock_exit",
        "event:/char/madeline/climb_ledge",
        "event:/char/madeline/campfire_stand",
        "event:/game/general/assist_dreamblockbounce",
    };

    public static int Run()
    {
        if (Environment.GetEnvironmentVariable("SFXCHECK") != "1")
        {
            Console.WriteLine();
            Console.WriteLine("  (sound bank check skipped; set SFXCHECK=1 to play every event once)");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("SOUND EVENTS against the installed Celeste banks");
        Console.WriteLine(new string('=', 74));

        string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet_debug.log");
        if (File.Exists(log)) File.Delete(log);

        using var sfx = new SoundEffects(() => true, 2, 1);
        if (!sfx.Available)
        {
            Console.WriteLine("  no Celeste install found -- nothing to check against");
            return 0;
        }

        int failed = 0;
        foreach (string path in Events)
        {
            long before = Failures(log);
            sfx.Play(path);
            sfx.Update();
            bool ok = Failures(log) == before;
            if (!ok) failed++;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {path}");
        }
        return failed;
    }

    static long Failures(string log)
    {
        if (!File.Exists(log)) return 0;
        long count = 0;
        foreach (string line in File.ReadAllLines(log))
            if (line.Contains("SFX event failed")) count++;
        return count;
    }
}
