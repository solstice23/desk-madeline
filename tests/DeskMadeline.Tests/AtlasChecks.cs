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

        string dump = Path.Combine("D:\\dev\\deskmadeline", "celeste_graphics_dump",
            "Graphics", "Atlases", "Gameplay");
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

        // End to end: with no assets folder to read, the loader must fall back to the atlas
        // and produce the very ids the animations ask for.
        Sprites.LoadAll(Path.Combine(Path.GetTempPath(), "deskmadeline-no-assets"),
            null, "characters/player_badeline");
        Check("with no assets folder, sprites come from Celeste", Sprites.LoadedFromCeleste);
        foreach (string id in new[]
        {
            "idle00", "runFast00", "climb00", "jumpFast00", "dash00", "hair00", "bangs00",
            "dangling00", "deadside00", "wakeUp00", "sweatIdle00",
            "glider/idle0", "seeker/Shockwave00", "theoCrystal/idle00", "pico8/font",
            "smoke0", "zappysmoke00", "slash00", Sprites.PortraitId,
        })
            Check($"id \"{id}\" resolves", Sprites.Get(id, false) != null);

        return failed;
    }
}
