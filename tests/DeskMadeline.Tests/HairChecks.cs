using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DeskMadeline;

// Where her hair sits, per frame: PlayerSprite.FrameMetadata, built from the game's own
// Sprites.xml the way CreateFramesMetadata builds it, keyed by texture, with CommunalHelper's
// elytra added the way its hooks add it. Nothing here is a copy of the game's numbers; the
// checks hold the reading up against values read off Sprites.xml by eye.
static class HairChecks
{
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    static bool Is(string texture, float x, float y, int bangs)
        => HairMeta.TryGet(texture, out var m) && m.HasHair && m.Offset.X == x && m.Offset.Y == y && m.Bangs == bangs;

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("HAIR: PlayerSprite.FrameMetadata, from the game's Sprites.xml");
        Console.WriteLine(new string('=', 74));

        string xml = CelesteInstall.GraphicsFile("Sprites.xml");
        if (xml == null || CelesteInstall.AtlasesDirectory == null)
        {
            Console.WriteLine("  no Celeste install found -- nothing to read");
            return 0;
        }
        Sprites.LoadAll(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "assets", "player"));
        var bank = SpriteBank.Load(Sprites.Atlas, XDocument.Load(xml), Array.Empty<XDocument>());
        HairMeta.LoadPlayerSprites(bank, Sprites.Atlas);
        Check($"the player sprites' frames are read ({HairMeta.Count} of them)", HairMeta.Count > 1000);

        // Three shapes of entry: a plain offset, one with a bangs frame after a colon, and a
        // sheet of a single frame, filed under its bare path.
        Check("a plain offset (swim06 is 0,-3)", Is("characters/player/swim06", 0f, -3f, 0));
        Check("an offset with bangs (climb08 is 2,-2 with bangs 2)", Is("characters/player/climb08", 2f, -2f, 2));
        Check("a one-frame sheet, under its bare path (duck)", HairMeta.HasHair("characters/player/duck"));
        Check("every swim frame has hair",
            Enumerable.Range(0, 18).All(i => HairMeta.HasHair("characters/player/swim" + i.ToString("00"))));
        // lookUp's sheet turns her head on frame 4: from 0,-2 facing to -1,-2 with bangs 1.
        Check("lookUp turns on frame 4 and nowhere else",
            Is("characters/player/lookUp03", 0f, -2f, 0) && Is("characters/player/lookUp04", -1f, -2f, 1) &&
            Is("characters/player/lookUp07", -1f, -2f, 1));

        // A copy="player" entry's frames answer under its own path: CreateFramesMetadata walks
        // the copied source with the override path.
        Check("Badeline's frames have the same metadata under her own path",
            Is("characters/player_badeline/climb08", 2f, -2f, 2));

        Console.WriteLine();
        Console.WriteLine("  Which frames wear hair at all (PlayerSprite.HasHair)");
        Check("the sleeping sheet does not, hair=\"\" being the game's way of saying so",
            !HairMeta.HasHair("characters/player/sleep00") && !HairMeta.HasHair("characters/player/sleep11"));
        Check("nor the wakeUp sheet, which the table never mentions",
            !HairMeta.HasHair("characters/player/wakeUp/00") && !HairMeta.HasHair("characters/player/wakeUp/07"));
        Check("while idle does", HairMeta.HasHair("characters/player/idle00"));

        Console.WriteLine();
        Console.WriteLine("  Carrying (PlayerSprite.CarryYOffset)");
        // The curves this port used to keep by hand, now read from carry="..." instead.
        int[] idleCarry = { -1, -1, -1, 0, 0, 0, 0, 0, -1 };
        int[] runCarry = { -1, 0, 0, 0, -3, -2, -1, 0, 0, 0, -3, -1 };
        int[] jumpCarry = { -3, -3, -1, -1 };
        bool Curve(string sheet, int[] curve) => Enumerable.Range(0, curve.Length)
            .All(i => HairMeta.CarryYOffset("characters/player/" + sheet + i.ToString("00")) == curve[i]);
        Check("idle_carry, run_carry and jump_carry ride as they did by hand",
            Curve("idle_carry", idleCarry) && Curve("run_carry", runCarry) && Curve("jump_carry", jumpCarry));

        Console.WriteLine();
        Console.WriteLine("  CommunalHelper's elytra");
        Check("its metadata lands on player_no_backpack's fly sheet",
            Is("characters/player_no_backpack/CommunalHelper/fly00", 4f, 0f, 0) &&
            Is("characters/player_no_backpack/CommunalHelper/fly08", 2f, -1f, 0));
        Check("and every player sprite has its nine-frame glide",
            new[] { "player", "player_badeline" }.All(id =>
                bank.SpriteData[id].Sprite.Animations.TryGetValue(Player.ElytraAnimation, out var a) && a.Frames.Length == 9));

        return failed;
    }
}
