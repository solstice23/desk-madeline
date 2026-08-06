using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DeskMadeline;

/// <summary>
/// Writes docs/celeste-atlas-index.tsv: every sprite in every atlas of an installed Celeste,
/// used by the pet or not.
/// </summary>
/// <remarks>
/// Run with ATLASINDEX=1 and a Celeste install present. The point is that the index is in the
/// repository, so a future change can look up what art exists, what its untrimmed frame
/// measures and how much of it is blank, without an install, without the dump, and without
/// unpacking anything.
/// </remarks>
static class AtlasIndexTool
{
    public static int Run()
    {
        if (Environment.GetEnvironmentVariable("ATLASINDEX") != "1") return 0;

        string celeste = CelesteInstall.Directory;
        if (celeste == null) { Console.WriteLine("  no Celeste install to index"); return 0; }
        string atlases = Path.Combine(celeste, "Content", "Graphics", "Atlases");
        if (!Directory.Exists(atlases)) { Console.WriteLine($"  no atlases at {atlases}"); return 0; }

        var output = new StringBuilder();
        output.AppendLine("# Every sprite in Celeste's atlases, generated -- see docs/celeste-assets.md.");
        output.AppendLine("# The dump mirrors this: celeste_graphics_dump/Graphics/Atlases/<atlas>/<path>.png");
        output.AppendLine("# frame is the untrimmed size the game draws; trim is the part actually stored.");
        output.AppendLine("atlas\tpath\tframeW\tframeH\ttrimX\ttrimY\ttrimW\ttrimH");

        int total = 0, atlasCount = 0;
        foreach (string meta in Directory.GetFiles(atlases, "*.meta").OrderBy(p => p))
        {
            string name = Path.GetFileNameWithoutExtension(meta);
            Dictionary<string, CelesteAtlas.Entry> entries;
            try { entries = CelesteAtlas.ReadMeta(meta, out _); }
            catch (Exception ex) { Console.WriteLine($"  {name}: unreadable ({ex.Message})"); continue; }

            atlasCount++;
            foreach (var pair in entries.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                CelesteAtlas.Entry e = pair.Value;
                output.Append(name).Append('\t').Append(pair.Key).Append('\t')
                      .Append(e.FrameWidth).Append('\t').Append(e.FrameHeight).Append('\t')
                      .Append(e.OffsetX).Append('\t').Append(e.OffsetY).Append('\t')
                      .Append(e.Width).Append('\t').Append(e.Height).Append('\n');
                total++;
            }
        }

        string path = Path.Combine("D:\\dev\\deskmadeline", "docs", "celeste-atlas-index.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, output.ToString());
        Console.WriteLine($"  indexed {total} sprites across {atlasCount} atlases -> {path}");
        Console.WriteLine($"  {new FileInfo(path).Length / 1024} KB");
        return 0;
    }
}
