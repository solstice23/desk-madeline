using System;
using System.IO;
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

        try { Directory.Delete(dir, true); } catch { }

        failed += SnapChecks.Run();
        return failed;
    }
}
