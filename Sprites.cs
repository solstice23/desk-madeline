using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace DeskMadeline
{
    /// <summary>One animation: frame sequence + delay + playback mode.</summary>
    public class Anim
    {
        public string[] Frames;
        public float Delay;
        public bool Loop;      // loop
        public bool Manual;    // frames driven by code (climb)
        public string Goto;    // switch after finish (SpriteBank goto)
    }

    /// <summary>Animation player: tracks current anim and frame timing.</summary>
    public class Animator
    {
        public string CurrentId;
        public int Frame;
        public float Timer;
        public bool Finished;   // non-looping animation finished
        public float PlayTime;  // elapsed time of current animation
        public int LoopCount;   // times the current animation has fully looped

        private Dictionary<string, Anim> _anims;

        public Animator(Dictionary<string, Anim> anims) { _anims = anims; }

        public void Play(string id, bool restart = false)
        {
            if (id == null) return;
            if (CurrentId == id && !restart) return;
            if (!restart && _anims.TryGetValue(id, out var requested) &&
                requested.Goto != null && requested.Goto.Equals(CurrentId, StringComparison.OrdinalIgnoreCase))
                return;
            CurrentId = id;
            Frame = 0;
            Timer = 0;
            Finished = false;
            PlayTime = 0;
            LoopCount = 0;
        }

        public void Update(float dt)
        {
            if (CurrentId == null || !_anims.TryGetValue(CurrentId, out var a)) return;
            PlayTime += dt;
            if (a.Manual) return;
            Timer += dt;
            while (Timer >= a.Delay)
            {
                Timer -= a.Delay;
                Frame++;
                if (Frame >= a.Frames.Length)
                {
                    if (a.Loop) { Frame = 0; LoopCount++; }
                    else if (a.Goto != null && _anims.ContainsKey(a.Goto))
                    {
                        CurrentId = a.Goto;
                        a = _anims[CurrentId];
                        Frame = 0;
                        Timer = 0;
                        Finished = false;
                        LoopCount++;
                    }
                    else { Frame = a.Frames.Length - 1; Finished = true; break; }
                }
            }
        }

        public string CurrentFrameId
        {
            get
            {
                if (CurrentId == null || !_anims.TryGetValue(CurrentId, out var a)) return null;
                return a.Frames[Math.Min(Frame, a.Frames.Length - 1)];
            }
        }

        /// <summary>Hair editor: step frames forward/back within the current anim (wraps).</summary>
        public void StepFrame(int delta)
        {
            if (CurrentId == null || !_anims.TryGetValue(CurrentId, out var a) || a.Frames.Length == 0) return;
            Frame = (Frame + delta + a.Frames.Length) % a.Frames.Length;
            Finished = false;
        }
    }

    /// <summary>Sprite library: load PNGs, horizontal flip copies, tinted draw.</summary>
    public static class Sprites
    {
        private static readonly Dictionary<string, Bitmap> _tex = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> _texFlip = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static string AssetsDir;

        /// <summary>How many sprites the last load took from Celeste's atlases.</summary>
        public static int LoadedFromCeleste { get; private set; }

        /// <summary>The face the tray icon is made from, in the Portraits atlas.</summary>
        public const string PortraitId = "madeline/normal00";

        public static void LoadAll(string dir, string skinDir = null, string skinAtlasFolder = null)
        {
            AssetsDir = dir;
            foreach (var kv in _tex) kv.Value.Dispose();
            foreach (var kv in _texFlip) kv.Value.Dispose();
            _tex.Clear();
            _texFlip.Clear();

            // Celeste's own art comes from its atlases, whether those are beside the app or
            // in an install.  assets\ is laid over the top and holds only what the game has
            // no sprite for: the elytra, the cat bangs, a particle it draws as a rectangle.
            LoadFromCeleste(skinAtlasFolder);
            if (!Directory.Exists(dir))
            {
                LoadSkinDirectories(skinDir);
                return;
            }

            LoadDirectory(dir, null);
            LoadSkinDirectories(skinDir);
            string glider = Path.Combine(Path.GetDirectoryName(dir), "glider");
            if (Directory.Exists(glider)) LoadDirectory(glider, "glider/");
            string seeker = Path.Combine(Path.GetDirectoryName(dir), "seeker");
            if (Directory.Exists(seeker)) LoadDirectory(seeker, "seeker/");
            string theo = Path.Combine(Path.GetDirectoryName(dir), "theoCrystal");
            if (Directory.Exists(theo)) LoadDirectory(theo, "theoCrystal/");
        }

        /// <summary>A skin's own files, which are the user's and always live on disk.</summary>
        static void LoadSkinDirectories(string skinDir)
        {
            if (string.IsNullOrEmpty(skinDir) || !Directory.Exists(skinDir)) return;
            LoadDirectory(skinDir, null);
            // Player wake-up is the one supported animation stored below the
            // sprite root in the SMH examples. Sweat is a separate overlay;
            // load it under prefixed ids so it cannot overwrite body frames.
            string wakeUp = Directory.GetDirectories(skinDir)
                .FirstOrDefault(d => Path.GetFileName(d).Equals("wakeup", StringComparison.OrdinalIgnoreCase));
            if (wakeUp != null) LoadDirectory(wakeUp, "wakeUp");
            string sweat = Directory.GetDirectories(skinDir)
                .FirstOrDefault(d => Path.GetFileName(d).Equals("sweat", StringComparison.OrdinalIgnoreCase));
            if (sweat != null) LoadDirectory(sweat, "sweat");
            string communal = Directory.GetDirectories(skinDir)
                .FirstOrDefault(d => Path.GetFileName(d).Equals("CommunalHelper", StringComparison.OrdinalIgnoreCase));
            if (communal != null) LoadDirectory(communal, null);
        }

        /// <summary>Everything the pet draws, read straight out of Celeste's Gameplay atlas.</summary>
        /// <remarks>
        /// The folders map onto the ids the rest of the code already asks for, which are the
        /// file names assets\ used to hold. A skin built into the app -- Badeline -- is a
        /// folder in the same atlas rather than one on disk, so it is named the same way.
        /// </remarks>
        static void LoadFromCeleste(string skinAtlasFolder)
        {
            string atlases = CelesteInstall.AtlasesDirectory;
            if (atlases == null)
            {
                PetWindow.Log("sprites unavailable: no Celeste atlases beside the app or installed");
                return;
            }
            string meta = Path.Combine(atlases, "Gameplay.meta");
            if (!File.Exists(meta))
            {
                PetWindow.Log("sprites unavailable: no Gameplay atlas at " + atlases);
                return;
            }

            try
            {
                var entries = CelesteAtlas.ReadMeta(meta, out List<string> pages);
                var folders = new List<(string Folder, string Prefix)>
                {
                    ("characters/player/", ""),
                    ("objects/glider/", "glider/"),
                    ("objects/bumper/", "bumper/"),
                    ("characters/monsters/", "seeker/"),
                    ("characters/theoCrystal/", "theoCrystal/"),
                    ("pico8/", "pico8/"),
                    // Particles and the dash slash are Celeste's too, just filed elsewhere:
                    // particles/smoke0 is the id smoke0, effects/slash/00 is slash00.
                    ("particles/", ""),
                    ("effects/", ""),
                };
                if (!string.IsNullOrEmpty(skinAtlasFolder))
                    folders.Add((skinAtlasFolder.TrimEnd('/') + "/", ""));

                // Group by page so each one is decoded once: they are whole-atlas images and
                // far too big to hold on to, or to read again per sprite.
                var wanted = new Dictionary<int, List<(string Id, CelesteAtlas.Entry Entry)>>();
                foreach (var pair in entries)
                    foreach ((string folder, string prefix) in folders)
                    {
                        if (!pair.Key.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
                        string name = pair.Key.Substring(folder.Length);
                        // Celeste keeps a few of the player's animations in their own folders,
                        // and assets\ flattened those into the folder name followed by the
                        // frame: sweat/climb00 became sweatClimb00, wakeUp/00 became wakeUp00.
                        int slash = name.IndexOf('/');
                        if (slash >= 0)
                        {
                            string sub = name.Substring(0, slash), frame = name.Substring(slash + 1);
                            if (frame.Length == 0 || frame.Contains('/')) continue;
                            name = sub + char.ToUpperInvariant(frame[0]) + frame.Substring(1);
                        }
                        if (!wanted.TryGetValue(pair.Value.Page, out var list))
                            wanted[pair.Value.Page] = list = new List<(string, CelesteAtlas.Entry)>();
                        list.Add((prefix + name, pair.Value));
                        break;
                    }

                int loaded = 0;
                foreach (var page in wanted)
                {
                    string data = Path.Combine(atlases, pages[page.Key] + ".data");
                    if (!File.Exists(data)) continue;
                    using Bitmap sheet = CelesteAtlas.DecodePage(data);
                    foreach ((string id, CelesteAtlas.Entry entry) in page.Value)
                    {
                        Store(id, CelesteAtlas.Extract(sheet, entry));
                        loaded++;
                    }
                }
                // Madeline's face for the tray icon is a dialogue portrait, and those are not
                // in the gameplay atlas at all -- Celeste files them under Portraits, which is
                // the unpacked kind of atlas: an index beside a folder of separate images,
                // each one a .data of exactly the same format as a packed page.
                string face = Path.Combine(atlases, "Portraits",
                    PortraitId.Replace('/', Path.DirectorySeparatorChar) + ".data");
                if (File.Exists(face))
                {
                    Store(PortraitId, CelesteAtlas.DecodePage(face));
                    loaded++;
                }
                LoadedFromCeleste = loaded;
                PetWindow.Log($"sprites: {loaded} read from the Celeste atlases at {atlases}");
            }
            catch (Exception ex)
            {
                PetWindow.Log("sprites unavailable: " + ex.Message);
            }
        }

        /// <summary>Keep a sprite and the mirrored copy the renderer asks for by name.</summary>
        static void Store(string id, Bitmap bmp)
        {
            if (_tex.TryGetValue(id, out Bitmap old)) old.Dispose();
            if (_texFlip.TryGetValue(id, out Bitmap oldFlip)) oldFlip.Dispose();
            _tex[id] = bmp;
            var flip = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(flip))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, new Rectangle(bmp.Width, 0, -bmp.Width, bmp.Height));
            }
            _texFlip[id] = flip;
        }

        static void LoadDirectory(string dir, string idPrefix)
        {
            foreach (var file in Directory.GetFiles(dir, "*.png"))
            {
                string id = (idPrefix ?? "") + Path.GetFileNameWithoutExtension(file);
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var tmp = Image.FromStream(fs);
                var bmp = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
                }
                _tex[id] = bmp;
                var flip = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(flip))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(bmp, new Rectangle(bmp.Width, 0, -bmp.Width, bmp.Height));
                }
                _texFlip[id] = flip;
            }
        }

        public static Bitmap Get(string id, bool flipped)
        {
            if (id == null) return null;
            var dict = flipped ? _texFlip : _tex;
            if (dict.TryGetValue(id, out var b)) return b;
            // Missing-frame fallback: strip trailing digits and fall back to same-prefix 00
            string baseId = id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (dict.TryGetValue(baseId + "00", out b)) return b;
            if (dict.TryGetValue(baseId, out b)) return b;
            return null;
        }

        public static bool Has(string id) => _tex.ContainsKey(id);

        /// <summary>Build frame-id list from a sequence (prefix + two-digit index, or a single unprefixed frame).</summary>
        public static string[] Seq(string prefix, int from, int to)
        {
            var list = new List<string>();
            for (int i = from; i <= to; i++)
            {
                string id = prefix + i.ToString("00");
                if (_tex.ContainsKey(id)) list.Add(id);
            }
            return list.ToArray();
        }

        // ---------- Tinted drawing ----------
        private static readonly ImageAttributes _tintAttr = new ImageAttributes();
        private static readonly ColorMatrix _tintMatrix = new ColorMatrix();
        private static readonly ImageAttributes _silhouetteAttr = new ImageAttributes();
        // Input alpha becomes the requested solid RGB color. This matches Celeste's
        // TrailManager mask pass instead of multiplying the tint into sprite colors.
        private static readonly ColorMatrix _silhouetteMatrix = new ColorMatrix(new float[][]
        {
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 1, 1, 1, 1, 0 },
            new float[] { 0, 0, 0, 0, 1 }
        });
        private static readonly PointF[] _destPts = new PointF[3];   // cache to avoid per-frame array alloc (render is single-threaded)

        /// <summary>Multiplicative tint draw (texture should be white/gray base); alpha multiplies (1 = opaque).</summary>
        public static void DrawTinted(Graphics g, Bitmap src, Color tint, float x, float y, float w, float h, float alpha = 1f)
        {
            _tintMatrix.Matrix00 = tint.R / 255f;
            _tintMatrix.Matrix11 = tint.G / 255f;
            _tintMatrix.Matrix22 = tint.B / 255f;
            _tintMatrix.Matrix33 = alpha;
            _tintAttr.SetColorMatrix(_tintMatrix);
            _destPts[0] = new PointF(x, y);
            _destPts[1] = new PointF(x + w, y);
            _destPts[2] = new PointF(x, y + h);
            g.DrawImage(src, _destPts,
                new RectangleF(0, 0, src.Width, src.Height), GraphicsUnit.Pixel, _tintAttr);
        }

        /// <summary>Use only the source alpha as a mask and fill it with one color.</summary>
        public static void DrawSilhouette(Graphics g, Bitmap src, Color color, float x, float y, float w, float h, float alpha = 1f)
        {
            _silhouetteMatrix.Matrix30 = color.R / 255f;
            _silhouetteMatrix.Matrix31 = color.G / 255f;
            _silhouetteMatrix.Matrix32 = color.B / 255f;
            _silhouetteMatrix.Matrix33 = alpha * color.A / 255f;
            _silhouetteAttr.SetColorMatrix(_silhouetteMatrix);
            _destPts[0] = new PointF(x, y);
            _destPts[1] = new PointF(x + w, y);
            _destPts[2] = new PointF(x, y + h);
            g.DrawImage(src, _destPts,
                new RectangleF(0, 0, src.Width, src.Height), GraphicsUnit.Pixel, _silhouetteAttr);
        }
    }
}
