using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>A standable stretch of some solid's top: one node of the terrain graph.</summary>
    internal struct NavSeg
    {
        public float L, R, Y;          // the exposed run and its height
        public RectangleF Solid;       // the solid it is the top of
    }

    /// <summary>One maneuver of a route: reach the named segment by the named move.</summary>
    internal struct NavStep
    {
        public int Seg;                // destination segment index
        public byte Move;              // IdleNav.MoveWalk .. MoveLeap
        public float X;                // walk aim, face aim, dash spot, or walk-off point
        public int Dir;                // for dashes and leaps: which way the move faces
        public float Arg;              // for leaps: the height at which to leave the wall
    }

    /// <summary>
    /// Terrain navigation over whatever the desk currently is: the exposed tops of solids
    /// are places, and the edges between them are exactly her moveset -- walk and hop,
    /// grab-climb a face, up-dash to one hanging higher, drop off an end, or climb a
    /// nearby wall and leap across. Windows do not exist here; only solids do. A route is
    /// found by breadth-first search and handed to the pilot one maneuver at a time.
    /// </summary>
    internal static class IdleNav
    {
        public const byte MoveWalk = 0, MoveClimb = 1, MoveDash = 2, MoveDrop = 3, MoveLeap = 4;

        const float Headroom = 24f;     // she needs this much air above a top to stand on it
        const float StepUp = 24f;       // a running jump's reliable rise
        const float StepDown = 14f;     // a hop down that still counts as walking
        const float StepGap = 30f;      // a gap a moving jump clears
        const float GrabStart = 36f;    // a jump-and-grab truly reaches a face starting this
                                        // high: jump rise ~25 plus her collider, no folklore
        const float DashReach = 95f;    // jump plus up-dash, measured generously
        const float ChimneyMin = 12f, ChimneyMax = 55f;

        /// <summary>The exposed, standable tops of every solid on the screen.</summary>
        public static void BuildSegs(in IdleContext ctx, List<NavSeg> segs)
        {
            segs.Clear();
            float roomTop = float.MaxValue, roomBottom = float.MinValue;
            foreach (RectangleF monitor in ctx.Monitors)
            {
                roomTop = Math.Min(roomTop, monitor.Top);
                roomBottom = Math.Max(roomBottom, monitor.Bottom);
            }
            var cuts = new List<(float L, float R)>();
            foreach (Solid s in ctx.Solids)
            {
                if (s.T <= roomTop + 8f || s.T > roomBottom) continue;
                if (s.R - s.L < 10f) continue;
                // Anything hanging within a body of this top slices away the run under it.
                cuts.Clear();
                foreach (Solid o in ctx.Solids)
                {
                    if (o.T < s.T && o.B > s.T - Headroom && o.L < s.R && o.R > s.L)
                        cuts.Add((Math.Max(o.L, s.L), Math.Min(o.R, s.R)));
                }
                cuts.Sort((x, y) => x.L.CompareTo(y.L));
                float at = s.L;
                var rect = RectangleF.FromLTRB(s.L, s.T, s.R, s.B);
                foreach ((float cl, float cr) in cuts)
                {
                    if (cl - at >= 10f)
                        segs.Add(new NavSeg { L = at, R = cl, Y = s.T, Solid = rect });
                    at = Math.Max(at, cr);
                }
                if (s.R - at >= 10f)
                    segs.Add(new NavSeg { L = at, R = s.R, Y = s.T, Solid = rect });
            }
        }

        /// <summary>The segment under her feet, or -1 if she is not standing on one.</summary>
        public static int SegUnder(List<NavSeg> segs, PointF feet)
        {
            for (int i = 0; i < segs.Count; i++)
                if (Math.Abs(segs[i].Y - feet.Y) <= 4f &&
                    feet.X >= segs[i].L - 3f && feet.X <= segs[i].R + 3f) return i;
            return -1;
        }

        /// <summary>The segment holding this spot, or -1.</summary>
        public static int SegAt(List<NavSeg> segs, PointF spot)
        {
            for (int i = 0; i < segs.Count; i++)
                if (Math.Abs(segs[i].Y - spot.Y) <= 4f &&
                    spot.X >= segs[i].L - 3f && spot.X <= segs[i].R + 3f) return i;
            return -1;
        }

        /// <summary>
        /// Breadth-first over the segments, expanding every maneuver from each. Returns the
        /// maneuvers in order, or null when the terrain truly offers no way.
        /// </summary>
        public static List<NavStep> FindRoute(in IdleContext ctx, List<NavSeg> segs,
            int from, int to)
        {
            if (from < 0 || to < 0) return null;
            if (from == to) return new List<NavStep>();
            var cameFrom = new int[segs.Count];
            var cameBy = new NavStep[segs.Count];
            for (int i = 0; i < segs.Count; i++) cameFrom[i] = -1;
            var queue = new Queue<int>();
            cameFrom[from] = from;
            queue.Enqueue(from);
            int expansions = 0;
            while (queue.Count > 0 && expansions++ < 200)
            {
                int cur = queue.Dequeue();
                for (int next = 0; next < segs.Count; next++)
                {
                    if (cameFrom[next] >= 0 || next == cur) continue;
                    if (!TryEdge(ctx, segs, cur, next, out NavStep step)) continue;
                    cameFrom[next] = cur;
                    cameBy[next] = step;
                    if (next == to)
                    {
                        var steps = new List<NavStep>();
                        for (int i = to; i != from; i = cameFrom[i]) steps.Add(cameBy[i]);
                        steps.Reverse();
                        return steps;
                    }
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        /// <summary>Whether one of her moves connects segment a to segment b, and which.</summary>
        static bool TryEdge(in IdleContext ctx, List<NavSeg> segs, int ai, int bi,
            out NavStep step)
        {
            NavSeg a = segs[ai], b = segs[bi];
            step = default;
            float dy = a.Y - b.Y;       // positive: b is higher

            // Walk, hop, or a running jump across a small gap or up a small rise --
            // provided nothing tall stands in the doorway: two floors a pixel apart with
            // a wall between them are rooms, not neighbours.
            if (dy <= StepUp && dy >= -StepDown)
            {
                float gap = Math.Max(b.L - a.R, a.L - b.R);
                if (gap <= StepGap && GapClear(ctx, a, b))
                {
                    step = new NavStep
                    {
                        Seg = bi,
                        Move = MoveWalk,
                        X = Math.Clamp((b.L + b.R) / 2f, b.L + 6f, b.R - 6f),
                    };
                    return true;
                }
            }

            // Drop off an end of a onto b below.
            if (dy < -StepDown)
            {
                float offL = a.L - 12f, offR = a.R + 12f;
                if (offL > b.L + 4f && offL < b.R - 4f &&
                    CorridorClear(ctx, offL, a.Y + 2f, b.Y - 2f, b.Solid, a.Solid))
                { step = new NavStep { Seg = bi, Move = MoveDrop, X = a.L - 18f }; return true; }
                if (offR > b.L + 4f && offR < b.R - 4f &&
                    CorridorClear(ctx, offR, a.Y + 2f, b.Y - 2f, b.Solid, a.Solid))
                { step = new NavStep { Seg = bi, Move = MoveDrop, X = a.R + 18f }; return true; }
            }

            if (dy > StepUp)
            {
                RectangleF sol = b.Solid;
                // Climb a face that reaches down to her, or up-dash to one hanging higher.
                // A face is ANY solid whose top ends at b's level with the lip opening onto
                // b -- a hollow window's side border serves its top border exactly like a
                // solid block serves its own top.
                foreach (Solid f in ctx.Solids)
                {
                    if (Math.Abs(f.T - b.Y) > 4f) continue;
                    var fr = RectangleF.FromLTRB(f.L, f.T, f.R, f.B);
                    foreach (int side in BothSides)
                    {
                        float face = side > 0 ? f.L : f.R;
                        // the lip at the top of this face must open onto b
                        bool lipOnB = side > 0
                            ? b.L <= face + 2f && b.R >= face + 10f
                            : b.L <= face - 10f && b.R >= face - 2f;
                        if (!lipOnB) continue;
                        // The dash spot sits a full body off the face: rising with even a
                        // pixel of overlap into the border's corner wedges her mid-air.
                        float spot = face - side * 8f;
                        if (spot < a.L + 2f || spot > a.R - 2f) continue;
                        if (!CorridorClear(ctx, spot, b.Y - 4f, a.Y - 1f, fr, b.Solid)) continue;
                        if (f.B > a.Y - GrabStart)
                        {
                            step = new NavStep
                            { Seg = bi, Move = MoveClimb, X = face + side * 2f, Dir = side };
                            return true;
                        }
                        // A dash catch needs a face tall enough to actually catch: the
                        // two-pixel side of a top border is a lip to jump-grab, not a wall.
                        if (!ctx.WindowsReactToDash && f.B - f.T >= 20f &&
                            a.Y - f.B >= 25f && a.Y - f.B <= DashReach)
                        {
                            step = new NavStep { Seg = bi, Move = MoveDash, X = spot, Dir = side };
                            return true;
                        }
                    }
                }
                // Or climb a nearby wall and leap across onto b's face.
                foreach (Solid w in ctx.Solids)
                {
                    if (w.B < a.Y - GrabStart) continue;        // wall must start at her level
                    if (w.T > sol.Bottom - 8f) continue;        // and rise past b's underside
                    // And the leap must arrive with grip in the tank: the climb from a to
                    // the leap height has to fit what stamina affords past the jump start,
                    // or she leaps dry, cannot catch, and rides the cycle to the floor.
                    if (a.Y - 28f - (sol.Bottom - 8f) > 100f) continue;
                    RectangleF wr = RectangleF.FromLTRB(w.L, w.T, w.R, w.B);
                    float gapL = sol.Left - w.R;                // wall left of b's solid
                    if (gapL >= ChimneyMin && gapL <= ChimneyMax &&
                        w.R + 5f > a.L - 10f && w.R + 5f < a.R + 10f &&
                        CorridorClear(ctx, w.R + 6f, sol.Bottom - 12f, a.Y - 1f, sol, wr))
                    {
                        step = new NavStep
                        { Seg = bi, Move = MoveLeap, X = w.R - 2f, Dir = 1, Arg = sol.Bottom - 8f };
                        return true;
                    }
                    float gapR = w.L - sol.Right;               // wall right of b's solid
                    if (gapR >= ChimneyMin && gapR <= ChimneyMax &&
                        w.L - 5f > a.L - 10f && w.L - 5f < a.R + 10f &&
                        CorridorClear(ctx, w.L - 6f, sol.Bottom - 12f, a.Y - 1f, sol, wr))
                    {
                        step = new NavStep
                        { Seg = bi, Move = MoveLeap, X = w.L + 2f, Dir = -1, Arg = sol.Bottom - 8f };
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Nothing rising past jump height stands between two step-neighbours.</summary>
        static bool GapClear(in IdleContext ctx, NavSeg a, NavSeg b)
        {
            float zl, zr;
            if (b.L >= a.R - 5f) { zl = a.R - 2f; zr = b.L + 2f; }
            else if (a.L >= b.R - 5f) { zl = b.R - 2f; zr = a.L + 2f; }
            else return true;                               // overlapping runs share the air
            float floorY = Math.Min(a.Y, b.Y);
            foreach (Solid s in ctx.Solids)
            {
                if (s.R <= zl || s.L >= zr) continue;
                if (s.T < floorY - StepUp && s.B > floorY - 30f) return false;
            }
            return true;
        }

        static readonly int[] BothSides = { 1, -1 };

        /// <summary>
        /// A body-wide column of air: nothing solid intersects it, save the piece being
        /// climbed toward and the piece being left.
        /// </summary>
        public static bool CorridorClear(in IdleContext ctx, float x, float yTop, float yBottom,
            RectangleF ignore, RectangleF ignore2 = default)
        {
            if (yBottom <= yTop) return true;
            foreach (Solid s in ctx.Solids)
            {
                // Half a body, not a whole one: a corridor five pixels off a face must not
                // be poisoned by a piece that merely ends flush with that face.
                if (s.R < x - 4.5f || s.L > x + 4.5f) continue;
                if (s.B < yTop || s.T > yBottom) continue;
                if (InsideRect(s, ignore)) continue;
                if (ignore2.Width > 0f && InsideRect(s, ignore2)) continue;
                return false;
            }
            return true;
        }

        static bool InsideRect(Solid s, RectangleF r) =>
            s.L >= r.Left - 4f && s.R <= r.Right + 4f &&
            s.T >= r.Top - 4f && s.B <= r.Bottom + 4f;
    }
}
