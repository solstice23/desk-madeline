using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DeskMadeline;

/// <summary>
/// Sprites read out of an installed Celeste must come out the same as the ones the build
/// currently ships, since those were extracted from the very same atlas.
/// </summary>
/// <remarks>
/// Needs both a Celeste install and celeste_graphics_dump, which is not in the repository, so
/// it reports what it could not find rather than failing.
/// </remarks>
static class AtlasChecks
{
    static int failed;

    /// <summary>
    /// Where the unpacked art is. DUMPROOT points this at a freshly written one, which is how
    /// tools\dump-graphics.ps1 gets checked against the atlas it came from.
    /// </summary>
    static string DumpRoot
    {
        get
        {
            string root = Environment.GetEnvironmentVariable("DUMPROOT");
            return string.IsNullOrWhiteSpace(root)
                ? Path.Combine("D:\\dev\\deskmadeline", "celeste_graphics_dump")
                : root;
        }
    }

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    /// <summary>Per-pixel comparison, straight through the premultiplied bytes.</summary>
    static (bool Same, int Differing, string Detail) Compare(Bitmap a, Bitmap b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return (false, -1, $"{a.Width}x{a.Height} against {b.Width}x{b.Height}");
        int differing = 0;
        string first = null;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                Color pa = a.GetPixel(x, y), pb = b.GetPixel(x, y);
                if (pa == pb) continue;
                differing++;
                first ??= $"at {x},{y}: {pa} against {pb}";
            }
        return (differing == 0, differing, first ?? "identical");
    }

    static Bitmap LoadPng(string path)
    {
        using var stream = File.OpenRead(path);
        using var raw = Image.FromStream(stream);
        var copy = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(copy);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
        return copy;
    }

    /// <summary>
    /// The dump is the game's atlases unpacked, one png per sprite, and its layout says which
    /// is which: Graphics/Atlases/&lt;atlas&gt;/&lt;path&gt;.png is sprite &lt;path&gt; of &lt;atlas&gt;. Checking
    /// that both ways round is what makes the correspondence a mapping rather than a habit,
    /// and would catch a game update adding art the dump has never seen.
    /// </summary>
    static void CheckDumpIsOneToOne(string celeste)
    {
        string atlasRoot = Path.Combine(DumpRoot, "Graphics", "Atlases");
        string installed = Path.Combine(celeste, "Content", "Graphics", "Atlases");
        if (!Directory.Exists(atlasRoot)) return;

        var dump = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(atlasRoot, "*.png", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(atlasRoot, file).Replace('\\', '/');
            int slash = rel.IndexOf('/');
            if (slash < 0) continue;
            string atlas = rel.Substring(0, slash);
            if (!dump.TryGetValue(atlas, out var set))
                dump[atlas] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(rel.Substring(slash + 1, rel.Length - slash - 5));   // drop ".png"
        }

        int matched = 0, dumpOnly = 0, gameOnly = 0, atlases = 0;
        foreach (string meta in Directory.GetFiles(installed, "*.meta"))
        {
            string atlas = Path.GetFileNameWithoutExtension(meta);
            Dictionary<string, CelesteAtlas.Entry> entries;
            try { entries = CelesteAtlas.ReadMeta(meta, out _); }
            catch { continue; }
            atlases++;
            dump.TryGetValue(atlas, out HashSet<string> files);
            files ??= new HashSet<string>();
            foreach (string path in entries.Keys)
                if (files.Contains(path)) matched++; else gameOnly++;
            foreach (string path in files)
                if (!entries.ContainsKey(path)) dumpOnly++;
            dump.Remove(atlas);
        }
        foreach (var leftover in dump) dumpOnly += leftover.Value.Count;

        Console.WriteLine($"      {atlases} atlases: {matched} matched, {dumpOnly} only in the dump, " +
                          $"{gameOnly} only in the game");
        Check("every dumped png is a sprite of the game", dumpOnly == 0);
        Check("every sprite of the game is a dumped png", gameOnly == 0);
    }

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("CELESTE ATLAS");
        Console.WriteLine(new string('=', 74));

        string celeste = CelesteInstall.Directory;
        if (celeste == null)
        {
            Console.WriteLine("  no Celeste install found -- nothing to read");
            return 0;
        }
        string meta = Path.Combine(celeste, "Content", "Graphics", "Atlases", "Gameplay.meta");
        if (!File.Exists(meta))
        {
            Console.WriteLine($"  no Gameplay.meta at {meta}");
            return 0;
        }

        var entries = CelesteAtlas.ReadMeta(meta, out List<string> pages);
        Console.WriteLine($"  {entries.Count} sprites across {pages.Count} page(s): {string.Join(", ", pages)}");
        Check("the atlas index reads", entries.Count > 1000 && pages.Count >= 1);
        Check("a known sprite is indexed", entries.ContainsKey("characters/player/idle00"));

        string dump = Path.Combine(DumpRoot, "Graphics", "Atlases", "Gameplay");
        if (!Directory.Exists(dump))
        {
            Console.WriteLine("  no celeste_graphics_dump to compare against -- index only");
            return failed;
        }

        // A spread: a big player frame, a trimmed one, the crystal, a jelly and a seeker.
        string[] samples =
        {
            "characters/player/idle00",
            "characters/player/climb00",
            "characters/player/hair00",
            "characters/theoCrystal/idle00",
            "objects/glider/idle0",
            "characters/monsters/Shockwave00",
        };

        Bitmap page = null;
        int lastPage = -1;
        foreach (string path in samples)
        {
            if (!entries.TryGetValue(path, out CelesteAtlas.Entry entry))
            {
                Check($"{path} is in the atlas", false);
                continue;
            }
            if (entry.Page != lastPage)
            {
                page?.Dispose();
                page = CelesteAtlas.DecodePage(Path.Combine(celeste, "Content", "Graphics",
                    "Atlases", pages[entry.Page] + ".data"));
                lastPage = entry.Page;
            }
            Console.WriteLine($"      {path}: at {entry.X},{entry.Y} size {entry.Width}x{entry.Height} " +
                              $"offset {entry.OffsetX},{entry.OffsetY} frame {entry.FrameWidth}x{entry.FrameHeight}");
            string pngPath = Path.Combine(dump, path.Replace('/', Path.DirectorySeparatorChar) + ".png");
            if (!File.Exists(pngPath)) { Check($"{path} has a dumped png", false); continue; }

            using Bitmap extracted = CelesteAtlas.Extract(page, entry);
            using Bitmap expected = LoadPng(pngPath);
            var (same, differing, detail) = Compare(extracted, expected);
            Check($"{path} matches the dump ({detail})", same);
            if (!same && differing > 0)
                Console.WriteLine($"          {differing} of {extracted.Width * extracted.Height} pixels differ");
        }
        page?.Dispose();

        CheckDumpIsOneToOne(celeste);

        // End to end: with no assets folder to read, the loader must fall back to the atlas
        // and produce the very ids the animations ask for.
        Sprites.LoadAll(Path.Combine(Path.GetTempPath(), "deskmadeline-no-assets"),
            null, "characters/player_badeline");
        Check($"the atlases supply the sprites ({Sprites.LoadedFromCeleste} of them)", Sprites.LoadedFromCeleste > 500);
        foreach (string id in new[]
        {
            "idle00", "runFast00", "climb00", "jumpFast00", "dash00", "hair00", "bangs00",
            "dangling00", "deadside00", "wakeUp00", "sweatIdle00",
            "glider/idle0", "seeker/Shockwave00", "theoCrystal/idle00", "pico8/font",
            "smoke0", "zappysmoke00", "slash00", Sprites.PortraitId,
        })
            Check($"id \"{id}\" resolves", Sprites.Get(id, false) != null);

        // The hair painted into the poses that carry no hair of their own, lifted out so it
        // can be tinted with the rest of her. Counted against the frame it came from: every
        // pixel of the game's hair red, and no others.
        Console.WriteLine();
        Console.WriteLine("  Painted hair, lifted out of the frames that wear no hair");
        // Badeline is still the loaded skin here, and hers is painted in her own colours:
        // nothing of the game's red to lift, so nothing is tinted and her art is left alone.
        Check("a skin that painted its own is untouched (badeline asleep)",
            Sprites.BakedHairMask("sleep00", false) == null);
        Sprites.LoadAll(Path.Combine(Path.GetTempPath(), "deskmadeline-no-assets"), null, null);
        foreach (string id in new[] { "sleep00", "wakeUp00" })
        {
            Bitmap frame = Sprites.Get(id, false), mask = Sprites.BakedHairMask(id, false);
            int red = Painted(frame, 0xAC, 0x32, 0x32), shade = Painted(frame, 0x5A, 0x1A, 0x1A);
            int lit = mask == null ? 0 : Lit(mask);
            Check($"{id} has its hair painted in and lifted ({red} + {shade} px)",
                mask != null && red > 0 && lit == red + shade);
        }
        Bitmap awake = Sprites.Get("idle00", false);
        Check("a frame that wears its own hair has none painted in, and no mask",
            awake != null && Painted(awake, 0xAC, 0x32, 0x32) == 0 &&
            Sprites.BakedHairMask("idle00", false) == null);

        return failed;
    }

    /// <summary>How many pixels of one colour a frame has.</summary>
    static int Painted(Bitmap frame, int r, int g, int b)
    {
        if (frame == null) return 0;
        int n = 0;
        for (int y = 0; y < frame.Height; y++)
            for (int x = 0; x < frame.Width; x++)
            {
                Color px = frame.GetPixel(x, y);
                if (px.A > 0 && px.R == r && px.G == g && px.B == b) n++;
            }
        return n;
    }

    /// <summary>How many pixels of a mask will be painted with the hair colour.</summary>
    static int Lit(Bitmap mask)
    {
        int n = 0;
        for (int y = 0; y < mask.Height; y++)
            for (int x = 0; x < mask.Width; x++)
                if (mask.GetPixel(x, y).A > 0) n++;
        return n;
    }
}
