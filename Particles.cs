using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>Particle behavior definition (parameters ported from Celeste ParticleType).</summary>
    public class PType
    {
        public string[] Tex;        // texture id set (pick one at random on emit)
        public Color Color = Color.White;
        public Color Color2 = Color.White;
        public bool BlinkColor;
        public bool ChooseColor;
        public float GravY;         // gravity (px/s^2, positive = down)
        public float Friction;      // friction (px/s^2, slows down)
        public float LifeMin = 0.3f, LifeMax = 0.5f;
        public float Size = 5f;     // size (game pixels)
        public float SizeRange;
        public float SpeedMin = 5f, SpeedMax = 15f;
        public float SpeedMultiplier = 1f;
        public bool ScaleOut;       // shrink on death
        public bool FadeOut = true; // fade out on death
        public bool LateFade;       // vanilla Late: fade only in the last 25% of lifetime
    }

    /// <summary>Single particle (holds its texture reference).</summary>
    public struct Particle
    {
        public float X, Y, VX, VY;
        public float Life, MaxLife;
        public float Size;
        public Bitmap Tex;
        public Color Color;
        public Color Color2;
        public bool BlinkColor;
        public float GravY;
        public float Friction;
        public float SpeedMultiplier;
        public bool FadeOut;
        public bool ScaleOut;
        public bool LateFade;
    }

    /// <summary>
    /// Lightweight particle system: emit / update / draw. Coordinates = game pixels (world space);
    /// drawn relative to the camera and snapped to integer pixels (integer upscale = pixel-perfect).
    /// </summary>
    public class ParticleSystem
    {
        readonly Random rng = new Random();
        readonly List<Particle> parts = new List<Particle>();

        /// <summary>Emit count particles at (x,y) with direction dir (radians) ± dirRange.</summary>
        public void Emit(PType t, float x, float y, float dir, float dirRange, int count)
            => Emit(t, x, y, dir, dirRange, count, 0f, 0f);

        public void Emit(PType t, float x, float y, float dir, float dirRange, int count,
            float positionRangeX, float positionRangeY)
        {
            if (t == null || t.Tex == null || t.Tex.Length == 0) return;
            for (int i = 0; i < count; i++)
            {
                var b = Sprites.Get(t.Tex[rng.Next(t.Tex.Length)], false);
                if (b == null) continue;
                float a = dir + (float)(rng.NextDouble() * 2 - 1) * dirRange;
                float sp = t.SpeedMin + (float)rng.NextDouble() * (t.SpeedMax - t.SpeedMin);
                float life = t.LifeMin + (float)rng.NextDouble() * (t.LifeMax - t.LifeMin);
                float size = t.Size + (float)(rng.NextDouble() * 2 - 1) * t.SizeRange;
                if (size < 1) size = 1;
                parts.Add(new Particle
                {
                    X = x + (float)(rng.NextDouble() * 2 - 1) * positionRangeX,
                    Y = y + (float)(rng.NextDouble() * 2 - 1) * positionRangeY,
                    VX = (float)Math.Cos(a) * sp,
                    VY = (float)Math.Sin(a) * sp,
                    Life = life, MaxLife = life,
                    Size = size,
                    Tex = b,
                    Color = t.ChooseColor && rng.Next(2) != 0 ? t.Color2 : t.Color,
                    Color2 = t.Color2,
                    BlinkColor = t.BlinkColor,
                    GravY = t.GravY,
                    Friction = t.Friction,
                    SpeedMultiplier = t.SpeedMultiplier,
                    FadeOut = t.FadeOut,
                    ScaleOut = t.ScaleOut,
                    LateFade = t.LateFade
                });
            }
        }

        public void Update(float dt)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var p = parts[i];
                p.Life -= dt;
                if (p.Life <= 0) { parts.RemoveAt(i); continue; }
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                p.VY += p.GravY * dt;
                if (p.VX > 0) p.VX = Math.Max(0, p.VX - p.Friction * dt);
                else p.VX = Math.Min(0, p.VX + p.Friction * dt);
                if (p.SpeedMultiplier != 1f)
                {
                    float multiplier = (float)Math.Pow(p.SpeedMultiplier, dt);
                    p.VX *= multiplier;
                    p.VY *= multiplier;
                }
                parts[i] = p;
            }
        }

        public int Count => parts.Count;
        public void Clear() => parts.Clear();

        internal void AppendPointStamps(TrailStamp[] stamps, ref int count,
            Dictionary<int, Bitmap> colorBitmaps)
        {
            for (int i = 0; i < parts.Count && count < stamps.Length; i++)
            {
                Particle p = parts[i];
                float k = p.Life / p.MaxLife;
                float alpha = p.FadeOut ? Math.Min(1f, k * (p.LateFade ? 4f : 1f)) : 1f;
                Color color = p.BlinkColor
                    ? ((((int)(p.Life / .1f) & 1) != 0) ? p.Color : p.Color2)
                    : p.Color;
                int key = color.ToArgb();
                if (!colorBitmaps.TryGetValue(key, out Bitmap bitmap))
                {
                    bitmap = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    bitmap.SetPixel(0, 0, color);
                    colorBitmaps[key] = bitmap;
                }
                stamps[count++] = new TrailStamp(bitmap, (int)p.X, (int)p.Y, alpha);
            }
        }

        /// <summary>Draw into the 1x canvas (camX/camY = world→canvas offset).</summary>
        public void Draw(Graphics g, float camX, float camY)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Tex == null) continue;

                float k = p.Life / p.MaxLife;          // 1→0
                float alpha = p.FadeOut ? Math.Min(1f, k * (p.LateFade ? 4f : 1f)) : 1f;
                // Particle.ScaleOut uses Ease.CubeOut(remainingLife), reaching zero
                // rather than retaining a 30% minimum size.
                float size = p.ScaleOut ? p.Size * (1f - (float)Math.Pow(1f - k, 3f)) : p.Size;
                if (size < 1) size = 1;

                int sx = (int)Math.Round(p.X - camX - size / 2f);
                int sy = (int)Math.Round(p.Y - camY - size / 2f);
                int s = (int)Math.Round(size);
                if (s < 1) s = 1;

                // Calc.BetweenInterval(Life, .1): odd intervals use StartColor,
                // even intervals use Color2.
                Color color = p.BlinkColor
                    ? ((((int)(p.Life / 0.1f) & 1) != 0) ? p.Color : p.Color2)
                    : p.Color;
                Sprites.DrawTinted(g, p.Tex, color, sx, sy, s, s, alpha);
            }
        }
    }
}
