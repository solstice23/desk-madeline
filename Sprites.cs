using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;

namespace DeskMadeline
{
    /// <summary>一段动画：帧序列 + 延迟 + 播放方式。</summary>
    public class Anim
    {
        public string[] Frames;
        public float Delay;
        public bool Loop;      // 循环
        public bool Manual;    // 帧由代码驱动（攀爬）
    }

    /// <summary>动画播放器：跟踪当前动画、帧计时。</summary>
    public class Animator
    {
        public string CurrentId;
        public int Frame;
        public float Timer;
        public bool Finished;   // 非循环动画播完
        public float PlayTime;  // 当前动画已播放时长
        public int LoopCount;   // 当前动画循环完成的次数

        private Dictionary<string, Anim> _anims;

        public Animator(Dictionary<string, Anim> anims) { _anims = anims; }

        /// <summary>切换动画集（皮肤切换时用 mod Sprites.xml 构建的动画集替换）。</summary>
        public void SetAnims(Dictionary<string, Anim> anims) { _anims = anims; }

        public void Play(string id, bool restart = false)
        {
            if (id == null) return;
            if (CurrentId == id && !restart) return;
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

        /// <summary>头发编辑器：在当前动画内前后步进帧（循环）。</summary>
        public void StepFrame(int delta)
        {
            if (CurrentId == null || !_anims.TryGetValue(CurrentId, out var a) || a.Frames.Length == 0) return;
            Frame = (Frame + delta + a.Frames.Length) % a.Frames.Length;
            Finished = false;
        }
    }

    /// <summary>贴图库：加载 PNG、水平翻转副本、染色绘制。支持皮肤覆盖层（皮肤帧优先，缺帧回退默认）。</summary>
    public static class Sprites
    {
        private static readonly Dictionary<string, Bitmap> _tex = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> _texFlip = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> _skin = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> _skinFlip = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static string AssetsDir;
        public static string ActiveSkin;   // 当前皮肤名（null/空 = 默认）

        public static void LoadAll(string dir)
        {
            AssetsDir = dir;
            foreach (var kv in _tex) kv.Value.Dispose();
            foreach (var kv in _texFlip) kv.Value.Dispose();
            _tex.Clear();
            _texFlip.Clear();
            foreach (var file in Directory.GetFiles(dir, "*.png"))
                LoadTexture(file, _tex, _texFlip);
        }

        /// <summary>加载皮肤帧目录（覆盖默认）。dir 为 null 时恢复默认皮肤。</summary>
        public static void LoadSkin(string dir)
        {
            ClearSkin();
            ActiveSkin = dir == null ? null : Path.GetFileName(dir);
            if (dir == null || !Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.png"))
                LoadTexture(file, _skin, _skinFlip);
            LoadWakeUpDir(dir);
        }

        /// <summary>直接从皮肤 mod zip 加载帧（不落盘、不改动 zip）。frameDir 由 Skins.FindFrameDir 定位。</summary>
        public static void LoadSkinZip(string zipPath, string name)
        {
            ClearSkin();
            ActiveSkin = name;
            if (zipPath == null || !File.Exists(zipPath)) return;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                string frameDir = Skins.FindFrameDir(zip);
                if (frameDir == null) return;
                string prefix = frameDir + "/";
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    string id = Path.GetFileNameWithoutExtension(e.FullName);
                    using (var s = e.Open())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        ms.Position = 0;
                        LoadTexture(ms, id, _skin, _skinFlip);
                    }
                }
                LoadWakeUpZip(zip, frameDir);
            }
        }

        // ---- 醒来动画：mod 里放在 frameDir/WakeUP/00.png…（数字命名），映射成 wakeUp00… 供 wakeUp 动画用 ----
        static void LoadWakeUpZip(ZipArchive zip, string frameDir)
        {
            string prefix = frameDir + "/";
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                int i = e.FullName.LastIndexOf('/');
                if (i < 0) continue;
                string folder = e.FullName.Substring(0, i);
                string leaf = folder.Substring(folder.LastIndexOf('/') + 1);
                if (!leaf.Equals("wakeup", StringComparison.OrdinalIgnoreCase)) continue;
                if (!folder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string fn = Path.GetFileNameWithoutExtension(e.FullName);
                if (!int.TryParse(fn, out _)) continue;   // 只要数字帧 00..NN
                string id = "wakeUp" + fn.PadLeft(2, '0');
                using (var s = e.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    ms.Position = 0;
                    LoadTexture(ms, id, _skin, _skinFlip);
                }
            }
        }

        static void LoadWakeUpDir(string dir)
        {
            foreach (var d in Directory.GetDirectories(dir))
                if (Path.GetFileName(d).Equals("wakeup", StringComparison.OrdinalIgnoreCase))
                    foreach (var f in Directory.GetFiles(d, "*.png"))
                    {
                        string fn = Path.GetFileNameWithoutExtension(f);
                        if (!int.TryParse(fn, out _)) continue;
                        LoadTexture(f, "wakeUp" + fn.PadLeft(2, '0'), _skin, _skinFlip);
                    }
        }

        static void ClearSkin()
        {
            foreach (var kv in _skin) kv.Value.Dispose();
            foreach (var kv in _skinFlip) kv.Value.Dispose();
            _skin.Clear();
            _skinFlip.Clear();
        }

        static void LoadTexture(string file, Dictionary<string, Bitmap> tex, Dictionary<string, Bitmap> texFlip)
            => LoadTexture(file, Path.GetFileNameWithoutExtension(file), tex, texFlip);

        static void LoadTexture(string file, string id, Dictionary<string, Bitmap> tex, Dictionary<string, Bitmap> texFlip)
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            LoadTexture(fs, id, tex, texFlip);
        }

        static void LoadTexture(Stream fs, string id, Dictionary<string, Bitmap> tex, Dictionary<string, Bitmap> texFlip)
        {
            using var tmp = Image.FromStream(fs);
            var bmp = new Bitmap(tmp.Width, tmp.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(tmp, 0, 0, tmp.Width, tmp.Height);
            }
            tex[id] = bmp;
            var flip = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(flip))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, new Rectangle(bmp.Width, 0, -bmp.Width, bmp.Height));
            }
            texFlip[id] = flip;
        }

        /// <summary>取帧：皮肤优先，缺帧回退默认（含尾部数字回退）。</summary>
        public static Bitmap Get(string id, bool flipped)
        {
            if (id == null) return null;
            if (TryGet(id, flipped, _skin, _skinFlip, out var b)) return b;
            if (TryGet(id, flipped, _tex, _texFlip, out b)) return b;
            return null;
        }

        static bool TryGet(string id, bool flipped, Dictionary<string, Bitmap> tex, Dictionary<string, Bitmap> texFlip, out Bitmap b)
        {
            var dict = flipped ? texFlip : tex;
            if (dict.TryGetValue(id, out b)) return true;
            // 兼容缺帧：去掉尾部数字回退到同前缀 00
            string baseId = id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (dict.TryGetValue(baseId + "00", out b)) return true;
            if (dict.TryGetValue(baseId, out b)) return true;
            b = null;
            return false;
        }

        public static bool Has(string id) => _tex.ContainsKey(id) || _skin.ContainsKey(id);

        /// <summary>按帧序列生成帧 id 列表（前缀 + 两位序号，或单个无前缀帧）。</summary>
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

        // ---------- 染色绘制 ----------
        private static readonly ImageAttributes _tintAttr = new ImageAttributes();
        private static readonly ColorMatrix _tintMatrix = new ColorMatrix();
        private static readonly PointF[] _destPts = new PointF[3];   // 缓存，避免每帧分配数组（渲染为单线程）

        /// <summary>以乘法染色绘制（贴图应为白色/灰色基底），alpha 可乘（1=不透明）。</summary>
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
    }
}
