using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>
    /// Windows as FloatySpaceBlocks: they drift on a sine, sink under whatever stands on them,
    /// and take a shove from a dash. The port of Celeste's moon blocks, and the first mode in
    /// which the pet moves the user's windows rather than only reading them.
    /// </summary>
    /// <remarks>
    /// The numbers are FloatySpaceBlock.MoveToTarget's: four pixels of drift on a sine that
    /// takes 2*pi seconds to come round, twelve pixels of sink eased in and out as yLerp runs
    /// to one at a second, and eight pixels along a dash, there and back, as dashEase falls to
    /// zero at one and a half. Riding it holds the sink timer at three tenths of a second, so
    /// stepping off begins the climb back immediately.
    ///
    /// Two things are the desktop's rather than the game's. A block in Celeste has one home
    /// forever; a window's home is wherever its owner last put it, so a rectangle this did not
    /// leave there has been moved by somebody else, and that becomes the new home. Telling the
    /// two apart is what the ring of lately-asked-for places is for: a move is posted to the
    /// window's own thread, so "where is it" often answers with where it was told to be a frame
    /// or two ago, and reading that as somebody else's doing sets the block fighting itself.
    ///
    /// The other is that a window is only ever moved a whole game pixel at a time. Everything
    /// else here is measured in game pixels -- she stands on them, and the ride that carries
    /// her when a window moves can only hand her whole ones -- so a border resting a third of a
    /// pixel below her feet is a border she is not standing on. It is also what the block looks
    /// like in Celeste, which draws its four pixel bob on the same 320x180 grid as everything
    /// else. At six times over that is a step every few frames rather than sixty a second,
    /// which SetWindowPos on another process's window is glad of besides.
    /// </remarks>
    internal sealed class MoonWindows
    {
        sealed class Floating
        {
            public Win32.RECT Home;          // where its owner put it, in screen pixels
            public float Sine;               // FloatySpaceBlock.sineWave
            public float SinkTimer, YLerp;
            public float DashEase;
            public PointF DashDir;
            public int AppliedX, AppliedY;   // the offset this last asked for, in screen pixels
            public float OffsetX, OffsetY;   // and the same in game pixels, before rounding
            public readonly Point[] Told = new Point[32];   // places lately asked for
            public int ToldNext;
        }

        /// <summary>Whether this is one of the places this asked the window to be.</summary>
        static bool WasToldTo(Floating block, int x, int y)
        {
            foreach (Point told in block.Told) if (told.X == x && told.Y == y) return true;
            return false;
        }

        static void Remember(Floating block, int x, int y)
        {
            block.Told[block.ToldNext] = new Point(x, y);
            block.ToldNext = (block.ToldNext + 1) % block.Told.Length;
        }

        readonly Dictionary<IntPtr, Floating> floating = new Dictionary<IntPtr, Floating>();

        /// <summary>Whether anything is drifting; false once everything has been put back.</summary>
        public bool Active => floating.Count > 0;

        /// <summary>
        /// Advance every window one frame and move the ones whose whole-pixel offset changed.
        /// </summary>
        /// <param name="scale">Game pixels to screen pixels.</param>
        /// <param name="ridden">The windows something is standing on this frame.</param>
        public void Update(float dt, int scale, IReadOnlyList<PolledWindowInfo> windows,
            HashSet<IntPtr> ridden)
        {
            var seen = new HashSet<IntPtr>();
            foreach (PolledWindowInfo window in windows)
            {
                if (!window.IsPlatform) continue;
                seen.Add(window.Handle);
                if (!floating.TryGetValue(window.Handle, out Floating block))
                {
                    // FloatySpaceBlock starts each group at a random point of the sine, so a
                    // desktop full of them does not breathe in unison.
                    floating[window.Handle] = block = new Floating
                    {
                        Home = window.Rect,
                        Sine = (float)(random.NextDouble() * Math.PI * 2.0)
                    };
                    Remember(block, window.Rect.Left, window.Rect.Top);
                }

                // Whether the window is somewhere this put it, or somewhere its owner did.
                // The question cannot be answered by comparing against home plus the current
                // offset: a move is posted to the window's own thread and takes a moment to
                // land, so the honest answer to "where is it" is often the place asked for a
                // frame or two ago. Every place lately asked for therefore counts as this
                // one's doing, and anywhere else is the owner's -- a drag, a snap, a restore.
                bool ours = WasToldTo(block, window.Rect.Left, window.Rect.Top);
                if (!ours)
                {
                    // Rehome under the offset it is already holding, so the drift carries on
                    // from the new place instead of jumping by however far it is bobbing.
                    block.Home = new Win32.RECT
                    {
                        Left = window.Rect.Left - block.AppliedX,
                        Top = window.Rect.Top - block.AppliedY,
                        Right = window.Rect.Right - block.AppliedX,
                        Bottom = window.Rect.Bottom - block.AppliedY,
                    };
                    Remember(block, window.Rect.Left, window.Rect.Top);
                }

                block.Sine += dt;
                if (ridden.Contains(window.Handle)) block.SinkTimer = 0.3f;
                else if (block.SinkTimer > 0f) block.SinkTimer -= dt;
                block.YLerp = Approach(block.YLerp, block.SinkTimer > 0f ? 1f : 0f, dt);
                block.DashEase = Approach(block.DashEase, 0f, dt * 1.5f);

                float nudge = YoYo(QuadIn(block.DashEase)) * 8f;
                float offsetX = block.DashDir.X * nudge;
                float offsetY = 12f * SineInOut(block.YLerp)
                    + 4f * (float)Math.Sin(block.Sine)
                    + block.DashDir.Y * nudge;

                block.OffsetX = offsetX;
                block.OffsetY = offsetY;
                // Whole game pixels, not whole screen pixels; see the remarks above.
                int wantX = (int)Math.Round(offsetX, MidpointRounding.AwayFromZero) * scale;
                int wantY = (int)Math.Round(offsetY, MidpointRounding.AwayFromZero) * scale;
                if (wantX == block.AppliedX && wantY == block.AppliedY) continue;
                block.AppliedX = wantX;
                block.AppliedY = wantY;
                // Nothing is asked of a window on the frame its owner was found moving it:
                // pushing back against a drag in progress only makes it stutter.
                if (!ours) continue;
                Remember(block, block.Home.Left + wantX, block.Home.Top + wantY);
                Move(window.Handle, block.Home.Left + wantX, block.Home.Top + wantY);
            }

            // Windows that have gone away take their state with them.
            if (seen.Count == floating.Count) return;
            var gone = new List<IntPtr>();
            foreach (IntPtr handle in floating.Keys) if (!seen.Contains(handle)) gone.Add(handle);
            foreach (IntPtr handle in gone) floating.Remove(handle);
        }

        /// <summary>Where a window is being held, in game pixels, for the checks to read.</summary>
        public PointF OffsetOf(IntPtr window)
            => floating.TryGetValue(window, out Floating block)
                ? new PointF(block.OffsetX, block.OffsetY) : PointF.Empty;

        /// <summary>The same in screen pixels, as last handed to the window itself.</summary>
        public Point OffsetOfApplied(IntPtr window)
            => floating.TryGetValue(window, out Floating block)
                ? new Point(block.AppliedX, block.AppliedY) : Point.Empty;

        /// <summary>Where its owner last put it.</summary>
        public Win32.RECT HomeOf(IntPtr window)
            => floating.TryGetValue(window, out Floating block) ? block.Home : default;

        /// <summary>FloatySpaceBlock.OnDash: a dash into it shoves it eight pixels and back.</summary>
        public void Dashed(IntPtr window, PointF direction)
        {
            if (!floating.TryGetValue(window, out Floating block) || block.DashEase > 0.2f) return;
            block.DashEase = 1f;
            block.DashDir = direction;
        }

        /// <summary>Put every window back where its owner left it, and forget them.</summary>
        public void Restore()
        {
            foreach (var pair in floating)
            {
                if (pair.Value.AppliedX == 0 && pair.Value.AppliedY == 0) continue;
                Move(pair.Key, pair.Value.Home.Left, pair.Value.Home.Top);
            }
            floating.Clear();
        }

        static void Move(IntPtr window, int x, int y)
            => Win32.SetWindowPos(window, IntPtr.Zero, x, y, 0, 0,
                Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE |
                Win32.SWP_ASYNCWINDOWPOS);

        static readonly Random random = new Random();

        static float Approach(float value, float target, float amount)
            => value > target ? Math.Max(value - amount, target) : Math.Min(value + amount, target);

        // Monocle's easers, the three FloatySpaceBlock asks for.
        static float SineInOut(float t) => -((float)Math.Cos(Math.PI * t) - 1f) / 2f;
        static float QuadIn(float t) => t * t;
        static float YoYo(float t) => t <= 0.5f ? t * 2f : 1f - (t - 0.5f) * 2f;
    }

    /// <summary>What MoonWindows needs to know about a window: where it is and whether it counts.</summary>
    /// <remarks>
    /// The rectangle here is the plain window rect, not the DWM frame the rest of the pet
    /// measures platforms with. Those two differ by the invisible shadow border a window
    /// carries -- some seven pixels down each side -- and SetWindowPos speaks the former, so
    /// this must too. Reading one and writing the other made every window walk off sideways.
    /// </remarks>
    internal readonly struct PolledWindowInfo
    {
        public readonly IntPtr Handle;
        public readonly Win32.RECT Rect;
        public readonly bool IsPlatform;
        public PolledWindowInfo(IntPtr handle, Win32.RECT rect, bool isPlatform)
        { Handle = handle; Rect = rect; IsPlatform = isPlatform; }
    }
}
