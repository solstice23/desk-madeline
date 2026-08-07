using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>
    /// What a moving solid does to everything that is not Madeline: carries it if it is riding,
    /// pushes it if it is in the way, and squishes it against whatever is behind.
    /// </summary>
    /// <remarks>
    /// Solid.MoveHExact makes no distinction -- it walks every Actor in the scene, and the
    /// player is one of them. Player has its own copy because its squish is the elaborate one
    /// (a duck, then three by five, then death); everything else in Celeste shares the plain
    /// Actor version, three by three and then gone, so they share this.
    ///
    /// The two limits are the ones Player.SweptInto carries and for the same reasons: a solid
    /// never moves anything further than it moved itself, and what cannot be pushed clear is
    /// left inside rather than thrown out the far side.
    ///
    /// Boxes are given as a half width and a top and bottom offset from the position, because
    /// they are not all hung the same way: the crystal and the jellyfish stand on their
    /// position as she does, and the seeker is centred on its own. Measuring one as though it
    /// were the other looks in the wrong place entirely, and nothing is ever crushed.
    /// </remarks>
    internal static class ActorSweep
    {
        /// <summary>Whether a box at (x, y) is inside any solid but the one named.</summary>
        static bool Blocked(IList<Solid> solids, float x, float y, float halfWidth,
            float top, float bottom, IntPtr ignore)
        {
            float l = x - halfWidth, r = x + halfWidth, t = y + top, b = y + bottom;
            foreach (Solid s in solids)
            {
                if (s.Id == ignore) continue;
                if (l < s.R && r > s.L && t < s.B && b > s.T) return true;
            }
            return false;
        }

        static bool Inside(Solid s, float x, float y, float halfWidth, float top, float bottom)
            => x - halfWidth < s.R && x + halfWidth > s.L && y + top < s.B && y + bottom > s.T;

        /// <summary>Actor.TrySquishWiggle: the first free spot within three pixels either way.</summary>
        static bool Wiggle(IList<Solid> solids, ref PointF pos, float halfWidth,
            float top, float bottom)
        {
            for (int x = 0; x <= 3; x++)
                for (int y = 0; y <= 3; y++)
                {
                    if (x == 0 && y == 0) continue;
                    for (int sx = 1; sx >= -1; sx -= 2)
                        for (int sy = 1; sy >= -1; sy -= 2)
                        {
                            float tryX = pos.X + x * sx, tryY = pos.Y + y * sy;
                            if (Blocked(solids, tryX, tryY, halfWidth, top, bottom, IntPtr.Zero))
                                continue;
                            pos = new PointF(tryX, tryY);
                            return true;
                        }
                }
            return false;
        }

        /// <summary>
        /// A solid that has moved by (dx, dy) and now covers this box. Pushes it clear, no
        /// further than the solid itself travelled.
        /// </summary>
        /// <returns>False if it was squished, which is where each entity decides its own end.</returns>
        public static bool Push(IList<Solid> solids, ref PointF pos, float halfWidth,
            float top, float bottom, Solid mover, float dx, float dy)
        {
            if (!Inside(mover, pos.X, pos.Y, halfWidth, top, bottom)) return true;

            if (dx != 0f)
            {
                int sign = Math.Sign(dx);
                int want = sign > 0
                    ? (int)Math.Ceiling(mover.R - (pos.X - halfWidth))
                    : -(int)Math.Ceiling(pos.X + halfWidth - mover.L);
                int steps = Math.Min(Math.Abs(want), (int)Math.Ceiling(Math.Abs(dx)));
                for (int i = 0; i < steps; i++)
                {
                    if (Blocked(solids, pos.X + sign, pos.Y, halfWidth, top, bottom, mover.Id))
                        return Wiggle(solids, ref pos, halfWidth, top, bottom);
                    pos = new PointF(pos.X + sign, pos.Y);
                }
            }
            if (dy != 0f)
            {
                int sign = Math.Sign(dy);
                int want = sign > 0
                    ? (int)Math.Ceiling(mover.B - (pos.Y + top))
                    : -(int)Math.Ceiling(pos.Y + bottom - mover.T);
                int steps = Math.Min(Math.Abs(want), (int)Math.Ceiling(Math.Abs(dy)));
                for (int i = 0; i < steps; i++)
                {
                    if (Blocked(solids, pos.X, pos.Y + sign, halfWidth, top, bottom, mover.Id))
                        return Wiggle(solids, ref pos, halfWidth, top, bottom);
                    pos = new PointF(pos.X, pos.Y + sign);
                }
            }
            return true;
        }

        /// <summary>Whether this box is standing on top of that solid, within a pixel.</summary>
        public static bool RidingOn(Solid s, PointF pos, float halfWidth, float bottom)
            => pos.X - halfWidth < s.R && pos.X + halfWidth > s.L &&
               pos.Y + bottom >= s.T - 1f && pos.Y + bottom <= s.T + 1f;
    }
}
