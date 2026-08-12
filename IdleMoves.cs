using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>The idiom she moves in: each kind is one named Celeste move.</summary>
    internal enum MoveKind
    {
        WalkTo,         // walk to an x, hopping what a walk hops
        RunningJump,    // jump at a run, optionally with the grab out for the wall at the end
        ClimbUp,        // grab and go up for a while
        ClimbOverLip,   // grab and go up until she pops over onto the top
        NeutralHop,     // release, jump, drift back, regrab -- the stamina-free ladder rung
        ChimneyKick,    // wall jump away and take the far wall
        UpDashGrab,     // jump, dash straight up at the apex, press in and catch
        Wallbounce,     // up-dash beside a wall, jump during the dash: the super wall jump
        Super,          // grounded dash, jump inside the window: long flat flight
        Hyper,          // crouch-dash, jump inside the window: longer, lower, faster
        Wavedash,       // hop, diagonal-down dash, jump the instant of landing
        DropOff,        // walk off an edge and fall to whatever is below
        Settle,         // stand still until the speed dies
        WallLadder,     // climb while the tank lasts, then wall-jump cadence to a height:
                        // the ladder ends by jumping over the lip, which costs nothing
        DiagDashGrab,   // jump, dash up-diagonally into a face, grab: cuts the corner
                        // onto walls the vertical dash stands beside but cannot enter
        Ultra,          // the chain: hop, down-diagonal air dash, land during the dash
                        // for the 1.2x boost, jump on contact, again -- accelerating
                        // bounding leaps down a long clear floor
        DashAcross,     // off a wall and across a gap too wide to kick: wall-jump away,
                        // a horizontal dash carries the rest, grab out for the far face
    }

    /// <summary>One move with its parameters. A plan is a list of these.</summary>
    internal struct Move
    {
        public MoveKind Kind;
        public int Dir;         // horizontal intent; for wall moves, which side the wall is
        public float X;         // the x it aims at, where the kind uses one
        public int Hold;        // jump-hold or climb frames
        public int At;          // the frame offset of the move's second action (a dash, a jump)
        public bool Grab;       // for RunningJump: grab out after the jump
    }

    /// <summary>Per-move playback state; one per move, for rehearsal and performance alike.</summary>
    internal struct MoveRun
    {
        public int F;
        public int MarkF;       // the frame the first one-shot fired on
        public bool Acted;      // the move's first one-shot fired
        public bool Acted2;     // the move's second one-shot fired
        public int Side;        // wall ladder: the wall she is working now, once a
                                // kick has crossed her to the facing one (0: m.Dir)
    }

    /// <summary>
    /// The move library: turns a Move into per-frame PetInput against a live or ghost
    /// player, exactly the same way for both -- a rehearsal is the performance, early.
    /// </summary>
    internal static class IdleMoves
    {
        public const float Dt = 1f / 60f;

        /// <summary>The one way a Move is made; the planner's vocabulary in one line each.</summary>
        public static Move Of(MoveKind kind, int dir = 0, float x = 0f, int hold = 0,
            int at = 0, bool grab = false)
            => new Move { Kind = kind, Dir = dir, X = x, Hold = hold, At = at, Grab = grab };

        /// <summary>Produce this frame's input for the move, advancing its run state.</summary>
        public static PetInput Frame(Player p, in Move m, ref MoveRun run)
        {
            var input = new PetInput();
            int f = run.F++;
            switch (m.Kind)
            {
                case MoveKind.WalkTo:
                {
                    int dir = m.X > p.Pos.X + 2f ? 1 : m.X < p.Pos.X - 2f ? -1 : 0;
                    input.MoveX = dir;
                    // A walk hops what a walk can hop: a seam, a step, a lip.
                    if (dir != 0 && p.onGround && StepAhead(p, dir, out float rise) && rise <= 14f)
                        p.BufferJump();
                    // Pressing with intent and not moving is being blocked, and standing
                    // against a wall doing nothing is the one thing she must never do.
                    if (dir != 0 && p.onGround && Math.Abs(p.Speed.X) < 5f) run.MarkF++;
                    else run.MarkF = 0;
                    break;
                }

                case MoveKind.RunningJump:
                    input.MoveX = m.Dir;
                    if (!run.Acted && p.onGround &&
                        (Math.Abs(p.Speed.X) >= 60f || f >= 24))
                    { p.BufferJump(); run.Acted = true; run.MarkF = f; }
                    if (run.Acted && f <= run.MarkF + m.Hold) input.JumpHeld = true;
                    if (run.Acted && m.Grab && !p.onGround) input.GrabHeld = true;
                    break;

                case MoveKind.ClimbUp:
                case MoveKind.ClimbOverLip:
                    input.GrabHeld = true;
                    input.MoveY = -1;
                    input.MoveX = m.Dir;
                    break;

                case MoveKind.NeutralHop:
                    if (f == 1) p.BufferJump();
                    if (f >= 1 && f <= 1 + 8) input.JumpHeld = true;
                    if (f >= 3)
                    {
                        input.MoveX = m.Dir;
                        input.GrabHeld = p.Stamina > 10f;
                    }
                    break;

                case MoveKind.ChimneyKick:
                    if (f == 1) p.BufferJump();
                    if (f >= 1 && f <= 1 + 8) input.JumpHeld = true;
                    if (f >= 1)
                    {
                        input.MoveX = m.Dir;                    // away from this wall
                        if (f >= 4) input.GrabHeld = true;      // hands out for the far one
                    }
                    break;

                case MoveKind.UpDashGrab:
                    if (f == 0) { p.BufferJump(); }
                    if (f <= 12) input.JumpHeld = true;
                    if (!run.Acted2 && f >= m.At && p.Dashes > 0)
                    { p.BufferDash(); run.Acted2 = true; }
                    if (run.Acted2 && f < m.At + 8)
                    {
                        input.MoveY = -1;                       // aim the dash straight up
                        input.AimY = -1;
                        input.AimX = 0;
                    }
                    else if (run.Acted2)
                    {
                        input.MoveX = m.Dir;                    // press into the face
                        input.GrabHeld = true;
                    }
                    break;

                case MoveKind.DiagDashGrab:
                    if (f == 0) p.BufferJump();
                    if (f <= 8) input.JumpHeld = true;
                    if (!run.Acted2 && f >= m.At && p.Dashes > 0)
                    { p.BufferDash(); run.Acted2 = true; }
                    if (run.Acted2 && f < m.At + 8)
                    {
                        input.AimX = m.Dir;
                        input.AimY = -1;
                        input.MoveY = -1;
                        input.MoveX = m.Dir;
                    }
                    else if (run.Acted2)
                    {
                        input.MoveX = m.Dir;
                        input.GrabHeld = true;
                    }
                    break;

                case MoveKind.Wallbounce:
                    if (f == 0) p.BufferDash();
                    if (f < 8)
                    {
                        input.MoveY = -1;
                        input.AimY = -1;
                        input.AimX = 0;
                    }
                    // Pressed at the wall through the rise: the super wall jump wants her
                    // flush against it when the jump lands inside the dash.
                    if (f >= 2) input.MoveX = m.Dir;
                    if (!run.Acted && f >= m.At)
                    { p.BufferJump(); run.Acted = true; }
                    if (run.Acted && f <= m.At + 14) input.JumpHeld = true;
                    if (f > m.At + 14) input.GrabHeld = p.Stamina > 10f;
                    break;

                case MoveKind.Super:
                case MoveKind.Hyper:
                    if (f == 0) p.BufferDash(m.Kind == MoveKind.Hyper);
                    if (f < 4) { input.AimX = m.Dir; input.MoveX = m.Dir; }
                    if (!run.Acted && f >= m.At)
                    { p.BufferJump(); run.Acted = true; }
                    if (run.Acted && f <= m.At + 14) input.JumpHeld = true;
                    input.MoveX = m.Dir;
                    break;

                case MoveKind.Wavedash:
                    if (f == 0) p.BufferJump();
                    if (f <= 3) input.JumpHeld = true;
                    if (!run.Acted && f >= 6 && p.Dashes > 0)
                    { p.BufferDash(); run.Acted = true; }
                    if (run.Acted && !run.Acted2 && f < 14)
                    {
                        input.AimX = m.Dir;
                        input.AimY = 1;                          // down-diagonal
                        input.MoveY = 1;
                    }
                    if (run.Acted && !run.Acted2 && f > 8 && p.onGround)
                    { p.BufferJump(); run.Acted2 = true; }
                    if (run.Acted2) input.JumpHeld = f < 40;
                    input.MoveX = m.Dir;
                    break;

                case MoveKind.DropOff:
                    input.MoveX = m.Dir;
                    if (!p.onGround) run.Acted = true;
                    break;

                case MoveKind.DashAcross:
                {
                    if (f == 0) { p.BufferJump(); run.Acted = true; }
                    input.MoveX = m.Dir;
                    if (f <= 6) input.JumpHeld = true;
                    if (run.Acted && !run.Acted2 && f >= m.At && p.Dashes > 0)
                    { p.BufferDash(); run.Acted2 = true; }
                    if (run.Acted2 && f < m.At + 8) input.AimX = m.Dir;
                    if (run.Acted2 && f >= m.At + 4)
                        input.GrabHeld = p.Stamina > 30f;
                    break;
                }

                case MoveKind.Ultra:
                {
                    if (f == 0) run.Side = p.WavedashCount;
                    // The single-shot variant (grab): one boosted landing, then brake
                    // and stay -- the drop flourish, not the cross-country chain.
                    if (m.Grab && p.WavedashCount > run.Side) break;
                    input.MoveX = m.Dir;
                    bool dashing = p.HasDashBuffer || p.State == Player.StDash;
                    if (dashing)
                    {
                        // Down-diagonal, held through the dash: landing mid-dash is
                        // what earns the boost.
                        input.AimX = m.Dir;
                        input.AimY = 1;
                        input.MoveY = 1;
                    }
                    if (p.onGround && f - run.MarkF > 5)
                    { p.BufferJump(); run.MarkF = f; run.Acted = true; }
                    if (run.Acted && f - run.MarkF <= 4) input.JumpHeld = true;
                    if (!p.onGround && p.Dashes > 0 && p.Speed.Y > -30f && run.Acted)
                        p.BufferDash();
                    break;
                }

                case MoveKind.WallLadder:
                    if (p.State == Player.StClimb)
                    {
                        run.Acted = true;                       // has held the wall at least once
                        run.Acted2 = false;
                        if (p.Stamina >= 30f)
                        {
                            input.GrabHeld = true;
                            input.MoveY = -1;
                            input.MoveX = run.Side == 0 ? m.Dir : run.Side;
                        }
                        // else release: the airborne cadence below is free
                    }
                    else if (!p.onGround)
                    {
                        if (run.Acted2)
                        {
                            // The hanging catch: a face whose bottom stops above her
                            // standing head. Rise straight -- pressing in during the
                            // rise drifts her under the face or bonks its underside --
                            // and press in at the apex, where the grab engages.
                            input.MoveX = p.Speed.Y >= -20f ? m.Dir : 0;
                            input.GrabHeld = true;
                            if (f - run.MarkF <= 12) input.JumpHeld = true;
                        }
                        else
                        {
                            int lad = run.Side == 0 ? m.Dir : run.Side;
                            if (p.Speed.Y >= -10f && f - run.MarkF > 4 && NearWall(p, lad, 5f))
                            {
                                p.BufferJump(); run.MarkF = f;
                                // The kick: cross to the facing wall instead of
                                // regaining this one -- always when it crowds the
                                // neutral's arc, by the plan's style when merely near.
                                if (NearWall(p, -lad, 14f) ||
                                    (m.Grab && NearWall(p, -lad, 44f)))
                                { run.Side = -lad; lad = -lad; }
                            }
                            input.MoveX = f - run.MarkF < 2 ? 0 : lad;
                            input.GrabHeld = p.Stamina > 35f;
                            if (f - run.MarkF <= 12) input.JumpHeld = true;
                        }
                    }
                    else if (!NearWall(p, m.Dir, 10f) && HangingFaceAhead(p, m.Dir))
                    {
                        // Grounded under a hanging face: the sideways self-start would
                        // walk her beneath it and out the other side. Stand still and
                        // hop vertically instead; the airborne half does the catching.
                        input.MoveX = 0;
                        if (f - run.MarkF > 14)
                        { p.BufferJump(); run.MarkF = f; run.Acted2 = true; }
                        if (run.Acted2 && f - run.MarkF <= 10)
                        {
                            input.JumpHeld = true;
                            input.GrabHeld = true;
                        }
                    }
                    else
                    {
                        // Grounded beside the wall: the ladder starts itself -- jump with
                        // the grab out, and retry each cycle until a catch lands.
                        input.MoveX = m.Dir;
                        run.Acted2 = false;
                        run.Side = 0;
                        if (f - run.MarkF > 10 && NearWall(p, m.Dir, 10f))
                        { p.BufferJump(); run.MarkF = f; }
                        if (f - run.MarkF <= 10)
                        {
                            input.JumpHeld = true;
                            input.GrabHeld = true;
                        }
                    }
                    break;

                case MoveKind.Settle:
                    break;
            }
            input.JumpPressed = p.HasJumpBuffer;
            input.DashPressed = p.HasDashBuffer;
            return input;
        }

        /// <summary>Whether the move has finished on this player, well or badly.</summary>
        public static bool Done(Player p, in Move m, in MoveRun run)
        {
            int f = run.F;
            switch (m.Kind)
            {
                case MoveKind.WalkTo:
                    return (Math.Abs(p.Pos.X - m.X) <= 2f && p.onGround &&
                        Math.Abs(p.Speed.X) < 40f) || run.MarkF > 45 || f >= 900;
                case MoveKind.RunningJump:
                    return (run.Acted && f > 10 && (p.onGround || p.State == Player.StClimb))
                        || f >= 90;
                case MoveKind.ClimbUp:
                    return f >= m.Hold || p.onGround ||
                        (f > 4 && p.State != Player.StClimb) || f >= 240;
                case MoveKind.ClimbOverLip:
                    return p.onGround || (f > 6 && p.State != Player.StClimb) || f >= 60;
                case MoveKind.NeutralHop:
                    return (f > 6 && p.State == Player.StClimb) || (f > 6 && p.onGround)
                        || f >= 45;
                case MoveKind.ChimneyKick:
                    return (f > 8 && p.State == Player.StClimb) || (f > 8 && p.onGround)
                        || f >= 50;
                case MoveKind.UpDashGrab:
                case MoveKind.DiagDashGrab:
                    return p.State == Player.StClimb || (f > m.At + 16 && p.onGround)
                        || f >= 100;
                case MoveKind.Wallbounce:
                    return (f > m.At + 6 && p.State == Player.StClimb) ||
                        (f > m.At + 16 && p.onGround) || f >= 110;
                case MoveKind.Super:
                case MoveKind.Hyper:
                    return (f > 24 && p.onGround) || f >= 140;
                case MoveKind.Wavedash:
                    return (run.Acted2 && f > 30 && p.onGround) || f >= 80;
                case MoveKind.DashAcross:
                    return (run.Acted2 && p.State == Player.StClimb && f > m.At) ||
                        (f > 20 && p.onGround) || f >= 90;
                case MoveKind.Ultra:
                    // The chain ends when the speed does, or at the two-second cap.
                    return f >= 120 ||
                        (run.Acted && f > 40 && p.onGround && Math.Abs(p.Speed.X) < 60f);
                case MoveKind.DropOff:
                    return (run.Acted && p.onGround) || f >= 150;
                case MoveKind.Settle:
                    return (p.onGround && Math.Abs(p.Speed.X) < 15f) || f >= 40;
                case MoveKind.WallLadder:
                {
                    // Done gripped at the asked height, or airborne there when the wall
                    // demonstrably continues above -- a mid-wall stop before a kick -- or
                    // landed: near a lip the cadence keeps jumping until a jump carries
                    // her over and she stands on the top. High-but-airborne at a lip is
                    // never done; that is a fall about to happen. And grounded with no
                    // wall in reach is not a ladder at all: fail fast, do not wander off.
                    // A chimney kick may have crossed her to the facing wall; the ladder
                    // she is on now is the one that gets judged.
                    int lad = run.Side == 0 ? m.Dir : run.Side;
                    return (p.Pos.Y <= m.X &&
                            (p.State == Player.StClimb ||
                             (!p.onGround && WallSpansAbove(p, lad, m.X)))) ||
                        (run.Acted && f > 20 && p.onGround) ||
                        (f > 60 && p.onGround && !NearWall(p, lad, 12f)) || f >= 900;
                }
            }
            return true;
        }

        /// <summary>
        /// Rehearse a plan on a ghost of the live player: the same physics, none of the
        /// world. Returns whether the ghost ends in the accepted state, and where.
        /// </summary>
        public static bool Rehearse(Player live, IReadOnlyList<Move> plan,
            Func<Player, bool> accept, int frameBudget,
            out PointF end, out float peakY, out int frames)
        {
            Player ghost = live.CloneForSim();
            frames = 0;
            peakY = ghost.Pos.Y;
            foreach (Move m in plan)
            {
                var run = new MoveRun();
                while (frames < frameBudget)
                {
                    PetInput input = Frame(ghost, m, ref run);
                    ghost.Update(Dt, input);
                    frames++;
                    peakY = Math.Min(peakY, ghost.Pos.Y);
                    if (Done(ghost, m, run)) break;
                }
                if (frames >= frameBudget) break;
            }
            // Let the dust settle before judging, the way an audience would.
            for (int i = 0; i < 24 && frames < frameBudget; i++)
            {
                if (accept(ghost)) break;
                ghost.Update(Dt, new PetInput());
                frames++;
                peakY = Math.Min(peakY, ghost.Pos.Y);
            }
            end = ghost.Pos;
            return accept(ghost);
        }

        /// <summary>The wall on that side continues well above the stop height.</summary>
        static bool WallSpansAbove(Player p, int dir, float stopY)
        {
            float l = Math.Min(p.Pos.X, p.Pos.X + dir * 8f);
            float r = Math.Max(p.Pos.X, p.Pos.X + dir * 8f);
            foreach (Solid s in p.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.T <= stopY - 25f && s.B >= stopY) return true;
            }
            return false;
        }

        /// <summary>
        /// A face on that side whose bottom hangs above her standing head yet within a
        /// vertical hop's grab reach: the walls of windows that float, or whose lower
        /// reaches another window covers.
        /// </summary>
        static bool HangingFaceAhead(Player p, int dir)
        {
            float edge = p.Pos.X + dir * 4f;
            foreach (Solid s in p.Solids)
            {
                float face = dir > 0 ? s.L : s.R;
                if (Math.Abs(face - edge) > 8f) continue;
                if (s.B < p.Pos.Y - 34f || s.B > p.Pos.Y - 11f) continue;
                if (s.T > s.B - 20f) continue;      // a sliver is not a wall to climb
                return true;
            }
            return false;
        }

        /// <summary>A wall within reach on that side at her body height.</summary>
        static bool NearWall(Player p, int dir, float within)
        {
            float l = Math.Min(p.Pos.X, p.Pos.X + dir * within);
            float r = Math.Max(p.Pos.X, p.Pos.X + dir * within);
            foreach (Solid s in p.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.B <= p.Pos.Y - 10f || s.T >= p.Pos.Y - .25f) continue;
                return true;
            }
            return false;
        }

        /// <summary>A rise just ahead of her feet: what the walking hop exists for.</summary>
        static bool StepAhead(Player p, int dir, out float rise)
        {
            float l = Math.Min(p.Pos.X + dir * 5f, p.Pos.X + dir * 8f);
            float r = Math.Max(p.Pos.X + dir * 5f, p.Pos.X + dir * 8f);
            rise = 0f;
            foreach (Solid s in p.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.B <= p.Pos.Y - 10f || s.T >= p.Pos.Y - .25f) continue;
                rise = Math.Max(rise, p.Pos.Y - s.T);
            }
            return rise > 0f;
        }
    }
}
