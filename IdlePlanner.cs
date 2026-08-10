using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>
    /// Turns one nav step into a plan of named moves, by audition: candidate plans are
    /// laid out in style order -- with an occasional flourish promoted to the front --
    /// and each is rehearsed on a ghost until one actually works. What she performs is
    /// therefore always both idiomatic (the vocabulary is the plan) and possible (the
    /// rehearsal is the same physics). Plans containing a dash are never offered where
    /// a dash could move a window, and never through a pufferfish.
    /// </summary>
    internal static class IdlePlanner
    {
        /// <summary>
        /// Plan the given step from the player's current state. Returns null when no
        /// candidate survives rehearsal -- which is an answer, not a failure to answer.
        /// </summary>
        public static List<Move> PlanStep(in IdleContext ctx, List<NavSeg> segs,
            NavStep step, Random rng)
        {
            Player p = ctx.Player;
            if (step.Seg >= segs.Count) return null;
            NavSeg seg = segs[step.Seg];
            bool dashOk = !ctx.WindowsReactToDash && !NearAPuffer(ctx, p.Pos, seg);
            bool flair = dashOk && rng.NextDouble() < .45;

            // Arriving on an adjacent run at the same height counts: window chrome
            // splits one top into segments a pixel apart, and a lip-pop lands where it
            // lands -- the next step replans from there and crosses the divider.
            bool Accept(Player g) =>
                g.onGround && g.Pos.Y <= seg.Y + 6f && g.Pos.Y >= seg.Y - 10f &&
                g.Pos.X >= seg.L - 45f && g.Pos.X <= seg.R + 45f;

            var candidates = new List<List<Move>>();
            switch (step.Move)
            {
                case IdleNav.MoveWalk:
                {
                    float span = Math.Abs(step.X - p.Pos.X);
                    int dir = step.X > p.Pos.X ? 1 : -1;
                    if (flair && span > 260f)
                        // Ultra terrain: a long clear run from a standing start. The
                        // rehearsal is the corridor check -- a ghost that bonks anything
                        // simply fails the audition.
                        candidates.Add(new List<Move>
                        {
                            IdleMoves.Of(MoveKind.Ultra, dir: dir),
                            IdleMoves.Of(MoveKind.WalkTo, x: step.X),
                        });
                    if (flair && span > 180f)
                    {
                        // The long flat stretch is what the tech is for.
                        MoveKind t = rng.Next(3) switch
                        { 0 => MoveKind.Super, 1 => MoveKind.Hyper, _ => MoveKind.Wavedash };
                        candidates.Add(new List<Move>
                        {
                            IdleMoves.Of(t, dir: dir, at: 6),
                            IdleMoves.Of(MoveKind.WalkTo, x: step.X),
                        });
                    }
                    candidates.Add(new List<Move> { IdleMoves.Of(MoveKind.WalkTo, x: step.X) });
                    // A step of fifteen to twenty-four pixels is past the walking hop:
                    // the running-jump variant covers it, and the roll picks whichever
                    // rehearses.
                    candidates.Add(new List<Move>
                    {
                        IdleMoves.Of(MoveKind.RunningJump, dir: dir, hold: 12),
                        IdleMoves.Of(MoveKind.WalkTo, x: step.X),
                    });
                    break;
                }

                case IdleNav.MoveDrop:
                {
                    int dir = step.X > p.Pos.X ? 1 : -1;
                    candidates.Add(new List<Move> { IdleMoves.Of(MoveKind.DropOff, dir: dir) });
                    break;
                }

                case IdleNav.MoveClimb:
                {
                    // step.X is a touch inside the face; recover the face and the approach.
                    int side = step.Dir;                    // face approached from this side
                    float face = step.X - side * 2f;
                    float height = p.Pos.Y - seg.Y;
                    int climbFrames = (int)(height / 45f * 60f) + 30;
                    var walk = IdleMoves.Of(MoveKind.WalkTo, x: face - side * 12f);
                    var jumpGrab = IdleMoves.Of(MoveKind.RunningJump, dir: side, hold: 10, grab: true);

                    // A chimney beside the face makes the flashier and faster ascent.
                    bool chimney = WallBeside(ctx, face, -side, seg.Y, p.Pos.Y);
                    if (chimney && height > 60f)
                        candidates.Add(ChimneyPlan(walk, jumpGrab, side, height, seg.Y));
                    if (flair && height > 50f && height < 150f)
                        candidates.Add(new List<Move>
                        {
                            IdleMoves.Of(MoveKind.WalkTo, x: face - side * 6f),
                            IdleMoves.Of(MoveKind.Wallbounce, dir: side, at: 15),
                            IdleMoves.Of(MoveKind.ClimbOverLip, dir: side),
                        });
                    if (height <= 120f)
                        candidates.Add(new List<Move>
                        {
                            walk, jumpGrab,
                            IdleMoves.Of(MoveKind.ClimbUp, dir: side, hold: climbFrames),
                            IdleMoves.Of(MoveKind.ClimbOverLip, dir: side),
                        });
                    // And the humble version that works under an overhang: walk to the
                    // face and grab it from standing, no jump arc to get cut.
                    if (height <= 120f)
                        candidates.Add(new List<Move>
                        {
                            IdleMoves.Of(MoveKind.WalkTo, x: face - side * 5f),
                            IdleMoves.Of(MoveKind.ClimbUp, dir: side, hold: climbFrames + 20),
                            IdleMoves.Of(MoveKind.ClimbOverLip, dir: side),
                        });
                    // The wall ladder covers any height the tank cannot: climb while it
                    // lasts, then the free wall-jump cadence, ending by jumping the lip.
                    candidates.Add(new List<Move> { walk, jumpGrab,
                        IdleMoves.Of(MoveKind.WallLadder, dir: side, x: seg.Y - 6f) });
                    break;
                }

                case IdleNav.MoveDash:
                {
                    if (!dashOk) return null;
                    int side = step.Dir;
                    float face = step.X + side * 8f;
                    // The same reach, several hands: dash timings and the diagonal are
                    // separate auditions, and the roll at the end picks among survivors.
                    foreach (int at in new[] { 14, 12, 17 })
                        candidates.Add(new List<Move>
                        {
                            IdleMoves.Of(MoveKind.WalkTo, x: step.X),
                            IdleMoves.Of(MoveKind.Settle),
                            IdleMoves.Of(MoveKind.UpDashGrab, dir: side, at: at),
                            IdleMoves.Of(MoveKind.WallLadder, dir: side, x: seg.Y - 6f),
                        });
                    candidates.Add(new List<Move>
                    {
                        IdleMoves.Of(MoveKind.WalkTo, x: step.X - side * 14f),
                        IdleMoves.Of(MoveKind.Settle),
                        IdleMoves.Of(MoveKind.DiagDashGrab, dir: side, at: 12),
                        IdleMoves.Of(MoveKind.WallLadder, dir: side, x: seg.Y - 6f),
                    });
                    // The wallbounce is not mere flair here: it reaches thirty pixels
                    // past the up-dash, and half the desk''s hanging faces live in that
                    // band. Flair only decides whether it auditions first or second.
                    var bounce = new List<Move>
                    {
                        IdleMoves.Of(MoveKind.WalkTo, x: face - side * 6f),
                        IdleMoves.Of(MoveKind.Wallbounce, dir: side, at: 15),
                        IdleMoves.Of(MoveKind.WallLadder, dir: side, x: seg.Y - 6f),
                    };
                    if (flair) candidates.Insert(0, bounce);
                    else candidates.Add(bounce);
                    break;
                }

                case IdleNav.MoveLeap:
                {
                    // step.X is inside the assist wall; the chimney is on the leap side.
                    int leap = step.Dir;
                    float wallFace = step.X + leap * 2f;
        
                    candidates.Add(new List<Move>
                    {
                        IdleMoves.Of(MoveKind.WalkTo, x: wallFace + leap * 10f),
                        IdleMoves.Of(MoveKind.RunningJump, dir: -leap, hold: 10, grab: true),
                        IdleMoves.Of(MoveKind.WallLadder, dir: -leap, x: step.Arg),
                        IdleMoves.Of(MoveKind.ChimneyKick, dir: leap),
                        IdleMoves.Of(MoveKind.WallLadder, dir: leap, x: seg.Y - 6f),
                    });
                    break;
                }
            }

            // Audition everything; roll among the survivors. First-survivor order made
            // her reliable but repetitive -- the same wall got the same ascent forever.
            var survivors = new List<List<Move>>();
            foreach (List<Move> plan in candidates)
                if (IdleMoves.Rehearse(ctx.Player, plan, Accept, 2600, out _, out _, out _))
                    survivors.Add(plan);
            if (survivors.Count == 0) return null;
            return survivors[rng.Next(survivors.Count)];
        }

        /// <summary>Alternating kicks up a chimney, ending over the target's lip.</summary>
        static List<Move> ChimneyPlan(Move walk, Move jumpGrab, int side, float height,
            float topY)
        {
            var plan = new List<Move> { walk, jumpGrab,
                IdleMoves.Of(MoveKind.ClimbUp, dir: side, hold: 40) };
            int kicks = Math.Max(2, (int)(height / 45f) + 2);
            int on = side;
            for (int i = 0; i < kicks; i++)
            {
                plan.Add(IdleMoves.Of(MoveKind.ChimneyKick, dir: -on));
                on = -on;
            }
            // Whichever wall she ends on, the ladder finishes the height and jumps the
            // lip; the rehearsal keeps only versions that top out on the target.
            plan.Add(IdleMoves.Of(MoveKind.WallLadder, dir: on, x: topY - 6f));
            return plan;
        }

        /// <summary>A second wall within kick reach of the face, spanning the climb.</summary>
        static bool WallBeside(in IdleContext ctx, float face, int dir, float topY, float bottomY)
        {
            float l = Math.Min(face + dir * 12f, face + dir * 50f);
            float r = Math.Max(face + dir * 12f, face + dir * 50f);
            foreach (Solid s in ctx.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.T > topY + 20f || s.B < bottomY - 20f) continue;
                return true;
            }
            return false;
        }

        static bool NearAPuffer(in IdleContext ctx, PointF from, NavSeg to)
        {
            foreach (Puffer puffer in ctx.Puffers)
            {
                if (puffer.Removed) continue;
                float midX = (from.X + Math.Clamp((to.L + to.R) / 2f, to.L, to.R)) / 2f;
                if (Math.Abs(puffer.Pos.X - midX) < 120f &&
                    puffer.Pos.Y > Math.Min(from.Y, to.Y) - 60f &&
                    puffer.Pos.Y < Math.Max(from.Y, to.Y) + 60f) return true;
            }
            return false;
        }
    }
}
