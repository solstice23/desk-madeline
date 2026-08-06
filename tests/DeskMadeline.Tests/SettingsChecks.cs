using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DeskMadeline;

// Settings defaults: what a fresh install gets, and -- more importantly -- that changing a
// default does not overwrite a preference somebody already saved.
static class SettingsChecks
{
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    public static int Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "deskmadeline-defaults-check");
        Directory.CreateDirectory(dir);

        Console.WriteLine("  Fresh install (no settings.txt)");
        string missing = Path.Combine(dir, "does-not-exist.txt");
        if (File.Exists(missing)) File.Delete(missing);
        var fresh = PetSettings.Load(missing);
        Check("sound effects default to only-when-focused (SfxMode 1)", fresh.SfxMode == 1);
        Check("respawn reversal animation defaults to on", fresh.RespawnReversalEnabled);
        Check("ignore maximized/fullscreen windows defaults to on", fresh.IgnoreMaximizedWindows);

        Console.WriteLine();
        Console.WriteLine("  Existing settings.txt keeps what the user chose");
        string saved = Path.Combine(dir, "settings.txt");
        File.WriteAllLines(saved, new[]
        {
            "# DeskMadeline settings",
            "SfxMode=0",
            "RespawnReversalEnabled=False",
            "IgnoreMaximizedWindows=False",
            "Scale=8",
        });
        var existing = PetSettings.Load(saved);
        Check("sound stays off for someone who turned it off", existing.SfxMode == 0);
        Check("respawn reversal stays off for someone who turned it off", !existing.RespawnReversalEnabled);
        Check("ignore maximized stays off for someone who turned it off", !existing.IgnoreMaximizedWindows);
        Check("unrelated settings still load (Scale 8)", existing.Scale == 8);

        Console.WriteLine();
        Console.WriteLine("  Partially written settings.txt falls back to the new defaults");
        string partial = Path.Combine(dir, "partial.txt");
        File.WriteAllLines(partial, new[] { "Scale=4" });
        var mixed = PetSettings.Load(partial);
        Check("missing SfxMode key uses the new default", mixed.SfxMode == 1);
        Check("missing RespawnReversalEnabled key uses the new default", mixed.RespawnReversalEnabled);
        Check("missing IgnoreMaximizedWindows key uses the new default", mixed.IgnoreMaximizedWindows);

        Console.WriteLine();
        Console.WriteLine("  Celeste folder");
        string celeste = Path.Combine(dir, "celeste.txt");
        var pointed = PetSettings.Load(celeste);
        Check("no folder is remembered until one is found or chosen", pointed.CelestePath == "");
        pointed.CelestePath = @"C:\Games\Celeste";
        pointed.Save();
        Check("the chosen folder survives a restart",
            PetSettings.Load(celeste).CelestePath == @"C:\Games\Celeste");

        string wasChosen = CelesteInstall.Chosen;
        try
        {
            CelesteInstall.Chosen = null;
            string real = CelesteInstall.Directory;
            Check("a folder without Celeste.exe is not an install", !CelesteInstall.IsInstall(dir));
            CelesteInstall.Chosen = dir;
            Check("a remembered folder that no longer holds the game is not used",
                CelesteInstall.Directory != dir);
            if (real != null)
            {
                CelesteInstall.Chosen = real;
                Check("the remembered folder is the one used", CelesteInstall.Directory == real);
                Check("it is an install", CelesteInstall.IsInstall(real));
            }
            else Console.WriteLine("    ..    no Celeste installed; the rest is untestable here");
        }
        finally { CelesteInstall.Chosen = wasChosen; }

        try { Directory.Delete(dir, true); } catch { }

        Console.WriteLine();
        Console.WriteLine("  Localization");
        // Every key must exist in every language: a missing one shows up as an English word
        // in the middle of a translated menu, and only when that menu is opened.
        var english = (Dictionary<string, string>)typeof(Loc)
            .GetField("english", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var translations = (Dictionary<string, Dictionary<string, string>>)typeof(Loc)
            .GetField("translations", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        foreach (var language in translations)
        {
            var missingKeys = new List<string>();
            foreach (string key in english.Keys)
                if (!language.Value.ContainsKey(key)) missingKeys.Add(key);
            var extra = new List<string>();
            foreach (string key in language.Value.Keys)
                if (!english.ContainsKey(key)) extra.Add(key);
            Check($"{language.Key} has every English key ({english.Count})" +
                (missingKeys.Count == 0 ? "" : ", missing " + string.Join(", ", missingKeys)),
                missingKeys.Count == 0);
            Check($"{language.Key} has no key English lacks" +
                (extra.Count == 0 ? "" : ", extra " + string.Join(", ", extra)), extra.Count == 0);
        }

        failed += SnapChecks.Run();
        return failed;
    }
}
