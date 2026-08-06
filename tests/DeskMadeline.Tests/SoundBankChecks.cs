using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DeskMadeline;

/// <summary>Does every event the code names resolve in the banks a build carries?</summary>
/// <remarks>
/// SoundEffects.Play logs "SFX event failed" and swallows the error, so an event that is not
/// there is silent rather than obvious -- indistinguishable from a move that forgot to play
/// one. Opt in with SFXCHECK=1, since this really does play each event, at 1% volume, and
/// needs Celeste installed.
///
/// The list is read out of the source rather than written here. A hand-kept one silently went
/// stale: it had no jellyfish in it, so it said every event resolved without dlc_sfx.bank and
/// nearly cost a bundled build the jellyfish's sounds, that being a Farewell mechanic whose
/// events live under event:/new_content/.
/// </remarks>
static class SoundBankChecks
{
    /// <summary>Every event: string literal in the app's own sources.</summary>
    static List<string> EventsNamedInSource()
    {
        var events = new SortedSet<string>(StringComparer.Ordinal);
        string repo = "D:\\dev\\deskmadeline";
        foreach (string file in Directory.GetFiles(repo, "*.cs"))
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"(event:/[^\"]+)\""))
                events.Add(match.Groups[1].Value);
        return new List<string>(events);
    }

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
        Console.WriteLine("SOUND EVENTS against the banks");
        Console.WriteLine(new string('=', 74));

        List<string> events = EventsNamedInSource();
        Console.WriteLine($"  {events.Count} events named in the sources");
        if (events.Count < 20)
        {
            Console.WriteLine("  suspiciously few -- the sources were not found");
            return 1;
        }

        string log = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet_debug.log");
        if (File.Exists(log)) File.Delete(log);

        using var sfx = new SoundEffects(() => true, 2, 1);
        if (!sfx.Available)
        {
            Console.WriteLine("  no Celeste install found -- nothing to check against");
            return 0;
        }

        int failed = 0;
        foreach (string path in events)
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
