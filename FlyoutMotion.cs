using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>Easing curves: fast out of the gate, soft into place.</summary>
    internal static class Ease
    {
        /// <summary>Even pace: for a cross-fade whose halves need their own share of the time.</summary>
        public static float Linear(float t) => t;

        /// <summary>For colour: a gentle settle.</summary>
        public static float OutCubic(float t) { float u = 1f - t; return 1f - u * u * u; }

        /// <summary>For movement: most of the way almost at once, then an unhurried last stretch.</summary>
        public static float OutQuint(float t) { float u = 1f - t; return 1f - u * u * u * u * u; }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static RectangleF Lerp(RectangleF a, RectangleF b, float t) => new RectangleF(
            a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
            a.Width + (b.Width - a.Width) * t, a.Height + (b.Height - a.Height) * t);
    }

    /// <summary>
    /// One animated number belonging to a control: set a target, read <see cref="Value"/> while
    /// painting. It repaints its control each frame while it moves, and nothing else.
    /// </summary>
    internal sealed class Tween
    {
        readonly Control owner;
        readonly Func<float, float> ease;
        float from, to;
        long start;
        float duration;

        public Tween(Control owner, float value, Func<float, float> ease)
        {
            this.owner = owner;
            this.ease = ease;
            from = to = Value = value;
        }

        public float Value { get; private set; }
        public float Target => to;

        /// <summary>
        /// Called after every change of <see cref="Value"/>, for a tween that moves more than
        /// its own control's paint -- a window's size, the rows under a fold-out.
        /// </summary>
        public Action Stepped;

        /// <summary>Head for <paramref name="target"/> over <paramref name="ms"/> milliseconds, from wherever it is now.</summary>
        public void To(float target, float ms)
        {
            if (target == to && (Value == to || Motion.IsRunning(this))) return;
            from = Value;
            to = target;
            if (ms <= 0f || !Motion.Enabled || from == to)
            {
                Value = to;
                Motion.Remove(this);
                owner.Invalidate();
                Stepped?.Invoke();
                return;
            }
            start = Motion.Now;
            duration = ms;
            Motion.Add(this);
        }

        /// <summary>Jump there, no animation.</summary>
        public void Snap(float target) => To(target, 0f);

        /// <summary>Advance to the clock; false once it has arrived.</summary>
        internal bool Step(long now)
        {
            if (owner.IsDisposed) return false;
            float t = Math.Min(1f, (now - start) / (float)Stopwatch.Frequency * 1000f / duration);
            Value = from + (to - from) * ease(t);
            owner.Invalidate();
            Stepped?.Invoke();
            return t < 1f;
        }
    }

    /// <summary>
    /// The one clock every tween runs on. It ticks only while something is moving and stops the
    /// moment the last tween arrives, so a flyout at rest costs nothing. Progress is measured
    /// against real time, so a late frame shortens nothing and stretches nothing: the tween is
    /// just further along when it is drawn.
    /// </summary>
    internal static class Motion
    {
        [DllImport("user32.dll")]
        static extern bool SystemParametersInfo(int action, int param, out bool value, int winIni);
        const int SPI_GETCLIENTAREAANIMATION = 0x1042;

        static readonly List<Tween> active = new List<Tween>();
        static Timer timer;
        static readonly Stopwatch clock = Stopwatch.StartNew();

        public static long Now => clock.ElapsedTicks;

        /// <summary>
        /// Windows' own "Animation effects" setting: off, every tween jumps to its end. Read
        /// when a flyout opens, since it can change while the app runs.
        /// </summary>
        public static bool Enabled { get; private set; } = true;

        public static void Refresh()
        {
            try { Enabled = !SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out bool on, 0) || on; }
            catch { Enabled = true; }
        }

        public static bool IsRunning(Tween tween) => active.Contains(tween);

        public static void Add(Tween tween)
        {
            if (!active.Contains(tween)) active.Add(tween);
            if (timer == null)
            {
                timer = new Timer { Interval = 10 };
                timer.Tick += (_, _) => Tick();
            }
            if (!timer.Enabled) timer.Start();
        }

        public static void Remove(Tween tween) => active.Remove(tween);

        static void Tick()
        {
            long now = clock.ElapsedTicks;
            for (int i = active.Count - 1; i >= 0; i--)
                if (!active[i].Step(now)) active.RemoveAt(i);
            if (active.Count == 0) timer.Stop();
        }
    }
}
