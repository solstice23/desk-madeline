using System;

/// <summary>
/// Runs every check and exits non-zero if any of them failed.
/// </summary>
/// <remarks>
/// There is no test framework here on purpose. These drive the real Player at a fixed 60Hz
/// over synthetic Solids and assert vanilla's numbers, which is what AGENTS.md asks for;
/// a framework would add a dependency without adding an assertion. Run it with
///   dotnet run --project tests\DeskMadeline.Tests -c Release
/// Set SFXCHECK=1 as well to play every sound event once against an installed Celeste,
/// which needs speakers and so stays opt-in.
/// </remarks>
static class Program
{
    static int Main()
    {
        int failed = 0;
        failed += GridChecks.Run();       // whole-pixel grid: climbing, borders, landing, respawn
        failed += DreamChecks.Run();      // dream blocks: death position, display edge, being held
        failed += PushChecks.Run();      // what a window moving into her does
        failed += WaterChecks.Run();     // swimming, when the windows are water
        failed += SoundChecks.Run();      // super and hyper, at the timings people play them
        failed += DreamHyperChecks.Run(); // the dream hyper, and the crouch that decides it
        failed += TheoChecks.Run();       // how the Theo crystal breaks
        failed += EntityDreamChecks.Run();// crystal, jelly and seeker dropped inside a block
        failed += SettingsChecks.Run();   // settings defaults, and snapping back onto the displays
        failed += HairChecks.Run();      // the hair table, read from the game rather than copied
        failed += AtlasChecks.Run();      // sprites read out of an installed Celeste
        failed += SoundBankChecks.Run();
        failed += AtlasIndexTool.Run();   // opt-in: regenerate docs/celeste-atlas-index.tsv
        failed += GraphicsDumpTool.Run(); // opt-in: unpack the atlases, see tools/dump-graphics.ps1  // opt-in: every event resolves in Celeste's banks

        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine(failed == 0 ? "ALL CHECKS PASS" : $"{failed} CHECK(S) FAILED");
        return failed == 0 ? 0 : 1;
    }
}
