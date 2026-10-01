using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DeskMadeline;

/// <summary>
/// The sprite bank read out of the game's own Sprites.xml, the way Monocle's SpriteBank reads
/// it, and Monocle's Sprite playing what it read.
/// </summary>
/// <remarks>
/// Needs an installed Celeste for Sprites.xml and the atlas; reports what it could not find
/// rather than failing. The Sprite checks below the bank need neither.
/// </remarks>
static class SpriteBankChecks
{
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    /// <summary>A three-frame atlas, enough to drive a Sprite by hand.</summary>
    sealed class TinyAtlas : IFrameAtlas
    {
        readonly HashSet<string> paths;
        public TinyAtlas(params string[] paths) { this.paths = new HashSet<string>(paths); }
        public bool Has(string path) => path != null && paths.Contains(path);
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("SPRITE BANK: Monocle's SpriteData and Sprite");
        Console.WriteLine(new string('=', 74));

        // Calc.ReadCSVIntWithTricks and Atlas numbering, which every animation goes through.
        Check("frames \"0-4,5*3,7-6\" read as Monocle reads them",
            string.Join(",", MonoCalc.ReadCSVIntWithTricks("0-4,5*3,7-6")) == "0,1,2,3,4,5,5,5,7,6");
        var tiny = new TinyAtlas("a/duck", "a/run00", "a/run01", "a/run02", "a/run04", "a/idleA00");
        Check("index 0 is the bare name when there is one", AtlasLookup.SubtextureAt(tiny, "a/duck", 0) == "a/duck");
        Check("a set stops at its first gap, and idle does not take idleA",
            string.Join(",", AtlasLookup.Subtextures(tiny, "a/run")) == "a/run00,a/run01,a/run02" &&
            AtlasLookup.Subtextures(tiny, "a/idle").Count == 0);

        CheckSprite(tiny);
        CheckGameBank();
        return failed;
    }

    /// <summary>Sprite.Update and Play, against Monocle's order of events.</summary>
    static void CheckSprite(TinyAtlas tiny)
    {
        var xml = XElement.Parse(
            "<s path=\"a/\" start=\"run\">" +
            "<Loop id=\"run\" path=\"run\" delay=\"0.1\"/>" +
            "<Anim id=\"once\" path=\"run\" delay=\"0.1\" frames=\"0,1\"/>" +
            "<Anim id=\"into\" path=\"run\" delay=\"0.1\" frames=\"0\" goto=\"run\"/>" +
            "</s>");
        var data = new SpriteData("s", tiny);
        data.Add(xml);
        var sprite = new GameSprite();
        sprite.LoadFrom(data.Sprite);
        Check("a sprite starts where its entry was left: on its start animation",
            sprite.CurrentAnimationID == "run" && sprite.Texture == "a/run00");

        var events = new List<string>();
        sprite.OnFrameChange = a => events.Add("frame " + sprite.CurrentAnimationFrame);
        sprite.OnLastFrame = a => events.Add("last " + a);
        sprite.OnLoop = a => events.Add("loop " + a);
        sprite.OnFinish = a => events.Add("finish " + a);

        // One frame per Update, however long the step: Monocle tests the timer once.
        sprite.Update(0.35f);
        Check("an update advances one frame however long it was", sprite.CurrentAnimationFrame == 1);
        sprite.Update(0.1f); sprite.Update(0.1f);
        Check("a loop's last frame comes round through OnLastFrame then OnLoop",
            string.Join("|", events) == "frame 1|frame 2|last run|frame 0|loop run");

        events.Clear();
        sprite.Play("once");
        sprite.Update(0.1f); sprite.Update(0.1f);
        // No "frame 0": it is the texture already showing, and SetFrame only reports a change.
        Check("an animation without a goto finishes, clearing the current id",
            string.Join("|", events) == "frame 1|last once|finish once" &&
            sprite.CurrentAnimationID == "" && sprite.LastAnimationID == "once" && !sprite.Animating);

        events.Clear();
        sprite.Play("into");
        sprite.Update(0.1f);
        Check("a goto moves on to its target at frame 0",
            sprite.CurrentAnimationID == "run" && sprite.CurrentAnimationFrame == 0);
        sprite.Play("run");
        Check("playing what is already playing does nothing", sprite.CurrentAnimationFrame == 0);
    }

    static void CheckGameBank()
    {
        Console.WriteLine();
        Console.WriteLine("  The game's Sprites.xml over its atlas");
        string xmlPath = CelesteInstall.GraphicsFile("Sprites.xml");
        if (xmlPath == null || CelesteInstall.AtlasesDirectory == null)
        {
            Console.WriteLine("  no Celeste install found -- skipped");
            return;
        }
        Sprites.LoadAll(Path.Combine(Path.GetTempPath(), "deskmadeline-no-assets"));
        var bank = SpriteBank.Load(Sprites.Atlas, XDocument.Load(xmlPath), Array.Empty<XDocument>());
        foreach (string id in new[] { "player", "player_no_backpack", "player_badeline", "player_sweat" })
            Check($"{id} builds", bank.Has(id));

        var player = bank.SpriteData["player"].Sprite;
        var idle = player.Animations["idle"];
        Check("idle: nine frames at 0.1s, going back to idle",
            idle.Frames.Length == 9 && idle.Delay == 0.1f && idle.Goto?.Choices.Single().Value == "idle");
        Check("sleep: 0-10, then 10 five more times, then 11-23",
            player.Animations["sleep"].Frames.Length == 29 &&
            player.Animations["sleep"].Frames[15] == "characters/player/sleep10");
        Check("duck is the bare frame", player.Animations["duck"].Frames.Single() == "characters/player/duck");
        Check("wakeUp is the folder's frames", player.Animations["wakeUp"].Frames[0] == "characters/player/wakeUp/00");
        Check("Badeline's idle is hers",
            bank.SpriteData["player_badeline"].Sprite.Animations["idle"].Frames[0] == "characters/player_badeline/idle00");
        // Only the player's folders are loaded by path, so the bank's other characters cannot
        // find their frames here; none of them is the pet's.
        Check("every entry that failed is one the pet does not use",
            bank.Failures.All(f => !f.StartsWith("player", StringComparison.OrdinalIgnoreCase)));
    }
}
