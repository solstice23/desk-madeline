using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

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

    /// <summary>贴图库：加载 PNG、水平翻转副本、染色绘制。</summary>
    public static class Sprites
    {
        private static readonly Dictionary<string, Bitmap> _tex = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Bitmap> _texFlip = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        public static string AssetsDir;

        public static void LoadAll(string dir)
        {
            AssetsDir = dir;
            foreach (var kv in _tex) kv.Value.Dispose();
            foreach (var kv in _texFlip) kv.Value.Dispose();
            _tex.Clear();
            _texFlip.Clear();
            foreach (var file in Directory.GetFiles(dir, "*.png"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
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
            // 兼容缺帧：去掉尾部数字回退到同前缀 00
            string baseId = id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (dict.TryGetValue(baseId + "00", out b)) return b;
            if (dict.TryGetValue(baseId, out b)) return b;
            return null;
        }

        public static bool Has(string id) => _tex.ContainsKey(id);

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
