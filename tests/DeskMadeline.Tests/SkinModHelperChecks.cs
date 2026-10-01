using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DeskMadeline;

/// <summary>
/// SkinModHelper Plus, as the pet ports it: a skin's sprite built from its own Sprites.xml, its
/// HairConfig, the hook on Sprite.Play, colour grading, and the shader's arithmetic.
/// </summary>
/// <remarks>
/// The skin checks use the example skins in the repository, and Tendo Alice when an extracted
/// copy is beside the app; each reports what it could not find rather than failing.
/// </remarks>
static class SkinModHelperChecks
{
    const float Dt = 1f / 60f;
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("SKINMODHELPER: skin sprites, HairConfig, the Play hook, colour grading");
        Console.WriteLine(new string('=', 74));

        CheckShader();
        CheckHash();

        string xml = CelesteInstall.GraphicsFile("Sprites.xml");
        if (xml == null || CelesteInstall.AtlasesDirectory == null)
        {
            Console.WriteLine("  no Celeste install found -- skin checks skipped");
            return failed;
        }
        string repo = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", ".."));
        CheckRalsei(repo, xml);
        CheckAlice(repo, xml);
        return failed;
    }

    /// <summary>DoCG on a lookup texture whose every texel names its own coordinates.</summary>
    static void CheckShader()
    {
        using var lut = new Bitmap(256, 16, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 256; x++)
                lut.SetPixel(x, y, Color.FromArgb(255, x, y * 16, 7));
        using var src = new Bitmap(2, 1, PixelFormat.Format32bppPArgb);
        // r 1.0 -> x 15, g 0.5 -> y (int)(7.5+0.5) = 8, b 0.2 -> z (int)(3+0.5) = 3.
        src.SetPixel(0, 0, Color.FromArgb(255, 255, 128, 51));
        src.SetPixel(1, 0, Color.FromArgb(0, 0, 0, 0));
        Bitmap graded = SmhShader.Apply(src, lut, false, Color.White, null, 0);
        Color p = graded.GetPixel(0, 0);
        Check($"DoCG looks up x + 16z, y, from the colour rounded to sixteenths ({p.R},{p.G})",
            p.R == 15 + 16 * 3 && p.G == 8 * 16 && p.A == 255);
        Check("and a transparent pixel stays transparent", graded.GetPixel(1, 0).A == 0);
    }

    /// <summary>SkinsSystem.getHash, against values worked by hand from its definition.</summary>
    static void CheckHash()
    {
        int h = SmhSkinConfig.Hash("ab");
        int n1 = unchecked(((352654597 << 5) + 352654597) ^ 'a');
        int n2 = unchecked(((352654597 << 5) + 352654597) ^ 'b');
        int expected = unchecked(n1 + n2 * 1566083941);
        if (expected < 0) expected += 1 << 31;
        Check("a skin's mode is getHash of its name, plus one in the registration", h == expected);
    }

    static (SpriteBank bank, SkinManager manager, SkinDefinition skin) Load(string root, string xml, Func<SkinDefinition, bool> pick)
    {
        var manager = new SkinManager(root);
        SkinDefinition skin = manager.Skins.FirstOrDefault(pick);
        if (skin == null) return (null, manager, null);
        Sprites.LoadAll(Path.Combine(Path.GetTempPath(), "deskmadeline-no-assets"), skin.GameplayDirectory);
        var mods = new List<XDocument>();
        string modXml = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(skin.GameplayDirectory)), "Sprites.xml");
        if (File.Exists(modXml)) mods.Add(XDocument.Load(modXml));
        var bank = SpriteBank.Load(Sprites.Atlas, XDocument.Load(xml), mods);
        HairMeta.LoadPlayerSprites(bank, Sprites.Atlas);
        HairMeta.CreateFramesMetadata(bank, skin.CharacterId, Sprites.Atlas);
        return (bank, manager, skin);
    }

    static void CheckRalsei(string repo, string xml)
    {
        Console.WriteLine();
        Console.WriteLine("  Ralsei (example_skins): a new-config skin with colour grading");
        var (bank, _, skin) = Load(repo, xml, s => s.CharacterId == "ralsei");
        if (skin == null) { Console.WriteLine("    no ralsei among the skins -- skipped"); return; }
        Check("registered as a SkinModHelper skin worn as its Character_ID",
            skin.Kind == SkinKind.SkinModHelper && bank.Has("ralsei"));
        var smh = new SmhSkin(bank, Sprites.Atlas, "ralsei", skin.SkinName, skin.Mode, skin.GameplayDirectory, null, null);
        var idle = bank.SpriteData["ralsei"].Sprite.Animations["idle"];
        Check("its idle is its own frames", idle.Frames.All(f => f.StartsWith("characters/ralsei/")));
        Check("its ColorGrading folder is found beside the idle frames", smh.ColorGradeLimit() >= 2);
        string grade1 = smh.ColorGradeFor(1, false), grade2 = smh.ColorGradeFor(2, false);
        Check($"the grade follows the dash count ({grade1}, {grade2})",
            grade1 == smh.ColorGradeDirectory + "dash1" && grade2 == smh.ColorGradeDirectory + "dash2");
    }

    static void CheckAlice(string repo, string xml)
    {
        Console.WriteLine();
        Console.WriteLine("  Tendo Alice (an installed zip): the skin that flickered");
        string bin = Path.Combine(repo, "bin", "Release", "net10.0-windows");
        if (!Directory.Exists(Path.Combine(bin, "skins")))
        {
            Console.WriteLine("    no installed skins -- skipped");
            return;
        }
        var (bank, _, skin) = Load(bin, xml, s => s.CharacterId == "TendouAlice_China");
        if (skin == null) { Console.WriteLine("    Tendo Alice is not installed -- skipped"); return; }

        var anims = bank.SpriteData["TendouAlice_China"].Sprite.Animations;
        Check($"her idle is her eight frames, not Madeline's nine ({anims["idle"].Frames.Length})",
            anims["idle"].Frames.Length == 8 && anims["idle"].Frames.All(f => f.StartsWith("characters/TendouAlice/China/")));
        Check("no animation she defines borrows a frame of Madeline's",
            anims.Values.SelectMany(a => a.Frames).All(f => !f.StartsWith("characters/player/")));
        // Her duck is a Loop with no frame list: every duck frame she has, from duck00.
        Check($"her duck is her own animated sheet from duck00 ({anims["duck"].Frames.Length} frames)",
            anims["duck"].Frames[0] == "characters/TendouAlice/China/duck00" &&
            anims["duck"].Frames.All(f => f.StartsWith("characters/TendouAlice/China/duck")));

        var smh = new SmhSkin(bank, Sprites.Atlas, "TendouAlice_China", skin.SkinName, skin.Mode, skin.GameplayDirectory, null, null);
        Check("her HairConfig: length 4 at every count, no flash, never floating",
            smh.Hair.GetHairLength(1) == 4 && smh.Hair.GetHairLength(2) == 4 && !smh.Hair.File.HairFlash &&
            smh.Hair.File.HairFloatingDashCount == -1);
        Check("one dash: a blue tip over grey segments",
            smh.Hair.SafeGetHairColor(1, out Color general) && general.ToArgb() == Color.FromArgb(0x00, 0xBD, 0xFF).ToArgb() &&
            smh.Hair.SafeGetHairColor(1, 1, 4, out Color segment) && segment.ToArgb() == Color.FromArgb(0x44, 0x4D, 0x61).ToArgb());
        Check("her own bangs and hair textures", smh.Hair.NewBangs?.Count == 3 && smh.Hair.NewHairs != null);

        // Worn: the Play hook lends what she lacks, whole, and nothing more.
        var p = new Player
        {
            Solids = new List<Solid> { new Solid { Id = new IntPtr(1), L = -2000f, T = 200f, R = 2000f, B = 260f } },
            MinX = -100000f, MaxX = 100000f, FreezeFramesEnabled = false, Pos = new PointF(100f, 200f), Facing = 1,
        };
        p.ResetSprite(bank, "TendouAlice_China", skin.Mode, smh);
        var input = new PetInput();
        var seen = new HashSet<string>();
        for (int i = 0; i < 60 * 60; i++)
        {
            p.Sprite.Update(Dt);
            p.Update(Dt, input);
            if (p.CurrentFrameId != null) seen.Add(p.CurrentFrameId);
        }
        Check($"a minute standing shows only her frames ({seen.Count} of them)",
            seen.Count > 0 && seen.All(f => f.StartsWith("characters/TendouAlice/China/")));
        // GetTrailColor's hook: no Trail colour of hers, so the general colour for the dashes
        // the dash began with -- one dash spent of one, so none left: her zero-dash colour.
        p.Dashes = 1;
        p.BufferDash(false);
        input.AimX = 1; input.MoveX = 1;
        for (int i = 0; i < 3; i++) { input.DashPressed = p.HasDashBuffer; p.Sprite.Update(Dt); p.Update(Dt, input); }
        Color trail = p.TrailColor(true);
        Check($"her dash trail is her colour for the dashes it began with ({trail.R:X2}{trail.G:X2}{trail.B:X2})",
            p.StartedDashingCount == 0 && trail.ToArgb() == Color.FromArgb(0x0F, 0xEE, 0x70).ToArgb());
        Check("her death_particle stands in for the hair blob in her death and respawn",
            smh.TextureOnSprite("death_particle", out string particle) && particle == "characters/TendouAlice/China/death_particle");

        bool lacked = !p.Sprite.Has("launch");
        p.PlaySprite("launch");
        Check("an animation she lacks is lent whole from the game's player",
            !lacked || (p.Sprite.Has("launch") && p.Sprite.Animations["launch"].Frames[0].StartsWith("characters/player/")));
    }
}
