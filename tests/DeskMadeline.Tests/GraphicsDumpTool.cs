using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DeskMadeline;

/// <summary>
/// Unpacks an installed Celeste's atlases into celeste_graphics_dump: one png per sprite,
/// laid out as Graphics/Atlases/&lt;atlas&gt;/&lt;path&gt;.png.
/// </summary>
/// <remarks>
/// The dump is not in the repository -- it is a few hundred megabytes of the game's art --
/// so anyone wanting to look at a sprite has to make their own. That is what this is for.
/// Run it through tools\dump-graphics.ps1, or with GRAPHICSDUMP=1.
/// </remarks>
static class GraphicsDumpTool
{
    public static int Run()
    {
        if (Environment.GetEnvironmentVariable("GRAPHICSDUMP") != "1") return 0;

        string atlases = CelesteInstall.AtlasesDirectory;
        if (atlases == null || !Directory.Exists(atlases))
        {
            Console.WriteLine("  no Celeste atlases found -- set CELESTE_PATH");
            return 1;
        }
        string outRoot = Environment.GetEnvironmentVariable("GRAPHICSDUMP_OUT");
        if (string.IsNullOrWhiteSpace(outRoot))
            outRoot = Path.Combine("D:\\dev\\deskmadeline", "celeste_graphics_dump");
        outRoot = Path.Combine(outRoot, "Graphics", "Atlases");

        Console.WriteLine();
        Console.WriteLine($"  unpacking {atlases}");
        Console.WriteLine($"         to {outRoot}");

        int written = 0, skipped = 0;
        foreach (string meta in Directory.GetFiles(atlases, "*.meta"))
        {
            string atlas = Path.GetFileNameWithoutExtension(meta);
            Dictionary<string, CelesteAtlas.Entry> entries;
            List<string> pages;
            try { entries = CelesteAtlas.ReadMeta(meta, out pages); }
            catch (Exception ex) { Console.WriteLine($"    {atlas}: {ex.Message}"); continue; }

            // Group by page so each is decoded once; they are whole-atlas images.
            var byPage = new Dictionary<int, List<KeyValuePair<string, CelesteAtlas.Entry>>>();
            foreach (var pair in entries)
            {
                if (!byPage.TryGetValue(pair.Value.Page, out var list))
                    byPage[pair.Value.Page] = list = new List<KeyValuePair<string, CelesteAtlas.Entry>>();
                list.Add(pair);
            }

            int here = 0;
            foreach (var page in byPage)
            {
                // A packed atlas has its pages beside the meta; an unpacked one, such as
                // Portraits, keeps each sprite as its own .data in a folder of that name.
                string packed = Path.Combine(atlases, pages[page.Key] + ".data");
                Bitmap sheet = null;
                try
                {
                    if (File.Exists(packed)) sheet = CelesteAtlas.DecodePage(packed);
                    foreach (var pair in page.Value)
                    {
                        string target = Path.Combine(outRoot, atlas,
                            pair.Key.Replace('/', Path.DirectorySeparatorChar) + ".png");
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        Bitmap image;
                        if (sheet != null) image = CelesteAtlas.Extract(sheet, pair.Value);
                        else
                        {
                            string loose = Path.Combine(atlases, atlas,
                                pair.Key.Replace('/', Path.DirectorySeparatorChar) + ".data");
                            if (!File.Exists(loose)) { skipped++; continue; }
                            image = CelesteAtlas.DecodePage(loose);
                        }
                        using (image) image.Save(target, ImageFormat.Png);
                        written++; here++;
                    }
                }
                finally { sheet?.Dispose(); }
            }
            Console.WriteLine($"    {atlas,-16} {here,5} sprites");
        }
        Console.WriteLine($"  {written} written" + (skipped > 0 ? $", {skipped} with no image found" : ""));
        return 0;
    }
}
