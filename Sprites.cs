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
        // Built on demand from the frames above; a null value means asked and there is none.
        private static readonly Dictionary<string, Bitmap> _hairMask = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> _hairMaskFlip = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static string AssetsDir;

        /// <summary>How many sprites the last load took from Celeste's atlases.</summary>
        public static int LoadedFromCeleste { get; private set; }

        /// <summary>The face the tray icon is made from, in the Portraits atlas.</summary>
        public const string PortraitId = "madeline/normal00";

        /// <param name="skinGameplayDirectory">
        /// The worn skin's Graphics/Atlases/Gameplay: laid over the game's atlas by path, which is
        /// all a skin's files are -- textures at the paths its sprites and configs name.
        /// </param>
        public static void LoadAll(string dir, string skinGameplayDirectory = null)
        {
            AssetsDir = dir;
            foreach (var kv in _tex) kv.Value.Dispose();
            foreach (var kv in _texFlip) kv.Value.Dispose();
            _tex.Clear();
            _texFlip.Clear();
            foreach (var kv in _atlas) kv.Value.Dispose();
            foreach (var kv in _atlasFlip) kv.Value.Dispose();
            _atlas.Clear();
            _atlasFlip.Clear();
            SmhShader.Clear();
            // Derived from those, so they go with them; a skin brings its own frames and its
            // own answer to whether any hair is painted into them.
            foreach (var kv in _hairMask) kv.Value?.Dispose();
            foreach (var kv in _hairMaskFlip) kv.Value?.Dispose();
            _hairMask.Clear();
            _hairMaskFlip.Clear();

            // Celeste's own art comes from its atlases, whether those are beside the app or
            // in an install.  assets\ is laid over the top and holds only what the game has
            // no sprite for: the elytra, the cat bangs, a particle it draws as a rectangle.
            LoadFromCeleste();
            LoadAtlasOverlay(skinGameplayDirectory);
            if (!Directory.Exists(dir)) return;

            LoadDirectory(dir, null);
            // CommunalHelper's elytra frames ship in assets\ as fly00-08; in the game they sit at
            // characters/player_no_backpack/CommunalHelper/fly, which is where its hook looks.
            for (int i = 0; ; i++)
            {
                string flyFile = Path.Combine(dir, "fly" + i.ToString("00") + ".png");
                if (!File.Exists(flyFile)) break;
                string flyPath = "characters/player_no_backpack/CommunalHelper/fly" + i.ToString("00");
                if (!_atlas.ContainsKey(flyPath)) StorePath(flyPath, ReadPng(flyFile));
            }
            string glider = Path.Combine(Path.GetDirectoryName(dir), "glider");
            if (Directory.Exists(glider)) LoadDirectory(glider, "glider/");
            string seeker = Path.Combine(Path.GetDirectoryName(dir), "seeker");
            if (Directory.Exists(seeker)) LoadDirectory(seeker, "seeker/");
            string theo = Path.Combine(Path.GetDirectoryName(dir), "theoCrystal");
            if (Directory.Exists(theo)) LoadDirectory(theo, "theoCrystal/");
        }

        /// <summary>Everything the pet draws, read straight out of Celeste's Gameplay atlas.</summary>
        /// <remarks>
        /// The folders map onto the ids the rest of the code already asks for, which are the
        /// file names assets\ used to hold. A skin built into the app -- Badeline -- is a
        /// folder in the same atlas rather than one on disk, so it is named the same way.
        /// </remarks>
        static void LoadFromCeleste()
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
                    ("objects/puffer/", "puffer/"),
                    ("characters/monsters/", "seeker/"),
                    ("characters/theoCrystal/", "theoCrystal/"),
                    ("pico8/", "pico8/"),
                    // Particles and the dash slash are Celeste's too, just filed elsewhere:
                    // particles/smoke0 is the id smoke0, effects/slash/00 is slash00.
                    ("particles/", ""),
                    ("effects/", ""),
                    // One sprite rather than a folder: the rest of util/ is whole-screen
                    // textures. The empty remainder makes its id the prefix alone.
                    ("util/glove", "glove"),
                };

                // Which entry each id comes from. A skin's folder names the same frames as
                // characters/player/ -- player_badeline/idle00 is idle00 just as player/idle00
                // is -- and the skin's has to win. Deciding that here, by the folder's place in
                // the list, rather than by whichever was stored last: that was whichever came
                // later in the atlas's index, which differed from frame to frame, so an idle
                // cycle came out half Badeline and half Madeline.
                var chosen = new Dictionary<string, (int Folder, CelesteAtlas.Entry Entry)>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in entries)
                    for (int f = 0; f < folders.Count; f++)
                    {
                        (string folder, string prefix) = folders[f];
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
                        string id = prefix + name;
                        if (!chosen.TryGetValue(id, out var already) || already.Folder < f)
                            chosen[id] = (f, pair.Value);
                        break;
                    }

                // Group by page so each one is decoded once: they are whole-atlas images and
                // far too big to hold on to, or to read again per sprite.
                var wanted = new Dictionary<int, List<(string Id, CelesteAtlas.Entry Entry, bool ByPath)>>();
                void Want(string id, CelesteAtlas.Entry entry, bool byPath)
                {
                    if (!wanted.TryGetValue(entry.Page, out var list))
                        wanted[entry.Page] = list = new List<(string, CelesteAtlas.Entry, bool)>();
                    list.Add((id, entry, byPath));
                }
                foreach (var pick in chosen) Want(pick.Key, pick.Value.Entry, false);
                // The player's sprite bank entries by their own atlas paths, the way the game
                // keeps them: Sprites.xml names frames by path, and a skin or a copy="player"
                // entry resolves its animations against exactly these.
                foreach (var pair in entries)
                    foreach (string folder in PlayerAtlasFolders)
                        if (pair.Key.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                        {
                            Want(pair.Key, pair.Value, true);
                            break;
                        }

                int loaded = 0;
                foreach (var page in wanted)
                {
                    string data = Path.Combine(atlases, pages[page.Key] + ".data");
                    if (!File.Exists(data)) continue;
                    using Bitmap sheet = CelesteAtlas.DecodePage(data);
                    foreach ((string id, CelesteAtlas.Entry entry, bool byPath) in page.Value)
                    {
                        if (byPath) StorePath(id, CelesteAtlas.Extract(sheet, entry));
                        else
                        {
                            Store(id, CelesteAtlas.Extract(sheet, entry));
                            loaded++;
                        }
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

        /// <summary>The game's folders the player's sprite bank entries draw their frames from.</summary>
        static readonly string[] PlayerAtlasFolders =
        {
            "characters/player/", "characters/player_badeline/",
            "characters/player_no_backpack/", "characters/player_playback/",
        };

        // Textures by atlas path, as Monocle's Atlas holds them, with the mirrored copies the
        // renderer asks for. Separate from the ids above: an id is a name the pet gave a
        // sprite, a path is where the game -- or a mod laid over it -- keeps one.
        private static readonly Dictionary<string, Bitmap> _atlas = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> _atlasFlip = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        sealed class PathAtlas : IFrameAtlas
        {
            public bool Has(string path) => path != null && _atlas.ContainsKey(path);
        }

        /// <summary>The Gameplay atlas by path, for sprite banks to find their frames in.</summary>
        public static readonly IFrameAtlas Atlas = new PathAtlas();

        static void StorePath(string path, Bitmap bmp)
        {
            if (_atlas.TryGetValue(path, out Bitmap old)) old.Dispose();
            if (_atlasFlip.TryGetValue(path, out Bitmap oldFlip)) oldFlip.Dispose();
            _atlas[path] = bmp;
            _atlasFlip[path] = Mirror(bmp);
        }

        /// <summary>
        /// A mod's Graphics/Atlases/Gameplay, laid over the game's the way Everest lays every
        /// mod's: each file is the texture at its own path, replacing the game's if it has one.
        /// </summary>
        /// <remarks>
        /// Desktop adaptation: only the skin package in use is laid over. Everest applies every
        /// installed mod at once; here the packages are a list to pick one from, and one the user
        /// has not picked should not change what the pet looks like.
        /// </remarks>
        public static void LoadAtlasOverlay(string gameplayDirectory)
        {
            if (string.IsNullOrEmpty(gameplayDirectory) || !Directory.Exists(gameplayDirectory)) return;
            foreach (string file in Directory.EnumerateFiles(gameplayDirectory, "*.png", SearchOption.AllDirectories))
            {
                string path = Path.GetRelativePath(gameplayDirectory, file).Replace('\\', '/');
                path = path.Substring(0, path.Length - 4);
                try { StorePath(path, ReadPng(file)); }
                catch (Exception ex) { PetWindow.Log("skin texture unreadable " + file + ": " + ex.Message); }
            }
        }

        static Bitmap ReadPng(string file)
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var tmp = Image.FromStream(fs);
            var bmp = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
            }
            return bmp;
        }

        static Bitmap Mirror(Bitmap bmp)
        {
            var flip = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(flip))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, new Rectangle(bmp.Width, 0, -bmp.Width, bmp.Height));
            }
            return flip;
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
            // An atlas path is exact: a sprite bank resolved it, and it either is there or not.
            if ((flipped ? _atlasFlip : _atlas).TryGetValue(id, out var b)) return b;
            var dict = flipped ? _texFlip : _tex;
            if (dict.TryGetValue(id, out b)) return b;
            // Missing-frame fallback: strip trailing digits and fall back to same-prefix 00
            string baseId = id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (dict.TryGetValue(baseId + "00", out b)) return b;
            if (dict.TryGetValue(baseId, out b)) return b;
            return null;
        }

        public static bool Has(string id) => _tex.ContainsKey(id);

        /// <summary>
        /// The hair the artist drew into a frame, as a mask to be tinted, or null for a frame
        /// that has none.
        /// </summary>
        /// <remarks>
        /// Most poses carry no hair at all -- PlayerHair draws it over them -- but the ones it
        /// is left off, the sleeping sheet above all, have it painted into the sprite in her
        /// ordinary red. In the game that is always the right red, because nobody lies down at
        /// a campfire holding two dashes; on a desktop she naps whenever she is left alone, and
        /// with two dashes the pose came out red-haired while every other frame of her was
        /// pink. So the painted hair is lifted into a mask -- white where the red is, and the
        /// bangs sprite's own grey where its shade is -- and drawing that tinted paints it
        /// whatever colour her hair is now, the shade included, since DrawTinted multiplies.
        ///
        /// Only the game's own red is lifted. A skin that paints its sleeping hair some other
        /// colour has already answered the question for itself and is left alone.
        /// </remarks>
        public static Bitmap BakedHairMask(string id, bool flipped)
        {
            if (id == null) return null;
            var cache = flipped ? _hairMaskFlip : _hairMask;
            if (cache.TryGetValue(id, out Bitmap cached)) return cached;
            Bitmap mask = BuildHairMask(Get(id, flipped));
            cache[id] = mask;   // null included: a frame with no painted hair is asked once
            return mask;
        }

        // Madeline's hair as the frames paint it, and under it the shade the bangs sprite
        // carries as a grey. 0x5A/0xAC and 134/255 are the same ratio to within a unit; the
        // grey is used so a sleeping head and an awake one are shaded alike.
        static readonly Color PaintedHair = Color.FromArgb(0xAC, 0x32, 0x32);
        static readonly Color PaintedHairShade = Color.FromArgb(0x5A, 0x1A, 0x1A);
        const int BangsShade = 134;

        static Bitmap BuildHairMask(Bitmap frame)
        {
            if (frame == null) return null;
            Bitmap mask = null;
            for (int y = 0; y < frame.Height; y++)
                for (int x = 0; x < frame.Width; x++)
                {
                    Color px = frame.GetPixel(x, y);
                    if (px.A == 0) continue;
                    int shade;
                    if (px.R == PaintedHair.R && px.G == PaintedHair.G && px.B == PaintedHair.B)
                        shade = 255;
                    else if (px.R == PaintedHairShade.R && px.G == PaintedHairShade.G &&
                             px.B == PaintedHairShade.B)
                        shade = BangsShade;
                    else continue;
                    mask ??= new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
                    mask.SetPixel(x, y, Color.FromArgb(px.A, shade, shade, shade));
                }
            return mask;
        }

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
