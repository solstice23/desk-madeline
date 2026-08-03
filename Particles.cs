using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>粒子行为定义（参数移植自 Celeste ParticleType）。</summary>
    public class PType
    {
        public string[] Tex;        // 纹理 id 组（发射时随机选一个）
        public Color Color = Color.White;
        public float GravY;         // 重力（px/s²，正=向下）
        public float Friction;      // 摩擦（px/s²，减速）
        public float LifeMin = 0.3f, LifeMax = 0.5f;
        public float Size = 5f;     // 尺寸（游戏像素）
        public float SizeRange;
        public float SpeedMin = 5f, SpeedMax = 15f;
        public bool ScaleOut;       // 消亡时缩小
        public bool FadeOut = true; // 消亡时淡出
    }

    /// <summary>单个粒子（持有所用贴图引用）。</summary>
    public struct Particle
    {
        public float X, Y, VX, VY;
        public float Life, MaxLife;
        public float Size;
        public Bitmap Tex;
        public Color Color;
        public float GravY;
        public float Friction;
        public bool FadeOut;
        public bool ScaleOut;
    }

    /// <summary>
    /// 轻量粒子系统：发射/更新/绘制。坐标=游戏像素（世界空间），
    /// 绘制时相对相机平移并吸附到整数像素（×整数倍放大=像素完美）。
    /// </summary>
    public class ParticleSystem
    {
        readonly Random rng = new Random();
        readonly List<Particle> parts = new List<Particle>();

        /// <summary>在 (x,y) 以方向 dir（弧度）±dirRange 发射 count 个粒子。</summary>
        public void Emit(PType t, float x, float y, float dir, float dirRange, int count)
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
                    X = x, Y = y,
                    VX = (float)Math.Cos(a) * sp,
                    VY = (float)Math.Sin(a) * sp,
                    Life = life, MaxLife = life,
                    Size = size,
                    Tex = b,
                    Color = t.Color,
                    GravY = t.GravY,
                    Friction = t.Friction,
                    FadeOut = t.FadeOut,
                    ScaleOut = t.ScaleOut
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
                p.VY += p.GravY * dt;
                if (p.VX > 0) p.VX = Math.Max(0, p.VX - p.Friction * dt);
                else p.VX = Math.Min(0, p.VX + p.Friction * dt);
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                parts[i] = p;
            }
        }

        public int Count => parts.Count;
        public void Clear() => parts.Clear();

        /// <summary>绘制进 1x 画布（camX/camY = 世界→画布偏移）。</summary>
        public void Draw(Graphics g, float camX, float camY)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p.Tex == null) continue;

                float k = p.Life / p.MaxLife;          // 1→0
                float alpha = p.FadeOut ? Math.Min(1f, k * 2f) : 1f;
                float size = p.ScaleOut ? p.Size * (0.3f + 0.7f * k) : p.Size;
                if (size < 1) size = 1;

                int sx = (int)Math.Round(p.X - camX - size / 2f);
                int sy = (int)Math.Round(p.Y - camY - size / 2f);
                int s = (int)Math.Round(size);
                if (s < 1) s = 1;

                Sprites.DrawTinted(g, p.Tex, p.Color, sx, sy, s, s, alpha);
            }
        }
    }
}
