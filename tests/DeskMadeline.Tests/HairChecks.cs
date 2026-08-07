using System;
using System.Collections.Generic;
using System.Reflection;
using DeskMadeline;

// Where her hair sits, per frame.
//
// The numbers come from the game's own Content\Graphics\Sprites.xml now, rather than from a
// copy of it typed into HairMeta. This checks the reading of it, and -- more usefully --
// holds the copy that is still in the file up against the original, so that a difference
// between them is something that gets reported rather than something that waits to be found.
// The elytra is not Celeste's and cannot be there; it is expected to be missing.
static class HairChecks
{
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static Dictionary<string, HairMeta.Meta> Table(string name)
        => (Dictionary<string, HairMeta.Meta>)typeof(HairMeta)
            .GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null);

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("HAIR: the frame table, read from Sprites.xml");
        Console.WriteLine(new string('=', 74));

        string sprites = CelesteInstall.GraphicsFile("Sprites.xml");
        if (sprites == null)
        {
            Console.WriteLine("  no Sprites.xml -- nothing to read");
            return 0;
        }
        HairMeta.LoadVanilla(sprites);
        var vanilla = Table("Vanilla");
        Check($"the player's frames are read ({vanilla.Count} of them)", vanilla.Count > 200);

        // Three shapes of entry: a plain offset, one carrying a bangs frame after a colon,
        // and a sheet of a single frame, which is filed under its bare name.
        Check("a plain offset (swim06 is 0,-3)",
            vanilla.TryGetValue("swim06", out var swim) &&
            swim.Offset.X == 0f && swim.Offset.Y == -3f && swim.Bangs == 0);
        Check("an offset with bangs (climb08 is 2,-2 with bangs 2)",
            vanilla.TryGetValue("climb08", out var climb) &&
            climb.Offset.X == 2f && climb.Offset.Y == -2f && climb.Bangs == 2);
        Check("a one-frame sheet, under its bare name (duck)",
            vanilla.ContainsKey("duck"));

        // The swim frames are the ones that were missing when swimming was ported.
        int swimFrames = 0;
        for (int i = 0; i < 18; i++) if (vanilla.ContainsKey("swim" + i.ToString("00"))) swimFrames++;
        Check($"every swim frame has an entry ({swimFrames} of 18)", swimFrames == 18);

        Console.WriteLine();
        Console.WriteLine("  Every frame resolves, from one layer or another");
        int unanswered = 0;
        foreach (string frame in vanilla.Keys)
            if (!HairMeta.TryGet(frame, out _)) unanswered++;
        Check($"nothing the game has a frame for is left without hair ({unanswered} missing)",
            unanswered == 0);
        Check("swimming among them, which is what went unnoticed before",
            HairMeta.TryGet("swim00", out _) && HairMeta.TryGet("swim12", out _));

        // Not a failure: the entries here are the port's own, tuned finer than the game's
        // whole pixels. Printed so that the two can be told apart at a glance, and so that
        // anything that ought to have been left to the game shows up as a line here.
        Console.WriteLine();
        Console.WriteLine("  What this repository still answers for itself");
        var own = Table("Offsets");
        var tuned = new List<string>();
        var onlyOurs = new List<string>();
        foreach (var pair in own)
        {
            if (!vanilla.TryGetValue(pair.Key, out var theirs)) { onlyOurs.Add(pair.Key); continue; }
            if (Math.Abs(pair.Value.Offset.X - theirs.Offset.X) > 0.001f ||
                Math.Abs(pair.Value.Offset.Y - theirs.Offset.Y) > 0.001f ||
                pair.Value.Bangs != theirs.Bangs)
                tuned.Add(pair.Key);
        }
        Console.WriteLine($"    {own.Count} entries: {tuned.Count} tuned away from the game's," +
            $" {own.Count - tuned.Count - onlyOurs.Count} the same as it," +
            $" {onlyOurs.Count} it has no answer for");
        Console.WriteLine($"      only ours: {string.Join(", ", onlyOurs)}");
        Check("the frames the game cannot supply are the elytra's and the longer climb sheet",
            onlyOurs.TrueForAll(id => id.StartsWith("fly") || id.StartsWith("climb")));

        return failed;
    }
}
