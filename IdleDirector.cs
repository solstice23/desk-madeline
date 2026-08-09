using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>What the director can see of the world, handed to it once a frame.</summary>
    internal struct IdleContext
    {
        public Player Player;
        public IList<Solid> Solids;
        public IReadOnlyList<RectangleF> Monitors;           // game px
        public PointF Cursor;                                 // game px
        public bool ForegroundFullscreen;
        public bool SeekersDormant;
        public IReadOnlyList<Glider> Gliders;
        public IReadOnlyList<Seeker> Seekers;
        public IReadOnlyList<Puffer> Puffers;
        public IReadOnlyList<KeyValuePair<IntPtr, RectangleF>> Windows;   // platform, game px
    }

    /// <summary>
    /// Idle autonomy: when nobody has touched the keys for a while, she does things on her
    /// own -- wanders, climbs a window to sit on it, watches the cursor, carries a jellyfish
    /// somewhere, and mostly naps.
    /// </summary>
    /// <remarks>
    /// This is desktop policy, not ported gameplay, and it stays entirely on this side of the
    /// fence: the director's only output is the same PetInput the keyboard produces, consumed
    /// by the untouched ported physics. She walks because MoveX is held, climbs because grab
    /// and up are held, and stumbles exactly where a player would, which is what keeps the
    /// autonomy honest -- there is no way for it to move her that a player does not have.
    ///
    /// Three layers. The arbiter engages after a quiet spell and yields the frame any real
    /// input arrives. The brain picks activities by scored utility -- energy, novelty, what
    /// the world offers -- with rests between them, because a pet that mostly sits still and
    /// occasionally does one deliberate thing reads as alive, where constant performance reads
    /// as a screensaver. The pilot turns "go there" into inputs reactively: walk, hop what is
    /// low, grab and climb what is tall, and give up gracefully through a watchdog when no
    /// progress is being made, which looks like a decision rather than a bug.
    ///
    /// Deliberate refusals: she never dashes on her own -- in kevin mode a dash flings the
    /// user's windows about, and nothing she does idly should move anyone's work -- and her
    /// autonomous input never counts as the "real" input that wakes dormant seekers, or she
    /// would stroll back into the one that just killed her. Fullscreen in the foreground means
    /// the user is watching something, and she sleeps through it.
    /// </remarks>
    internal sealed class IdleDirector
    {
        public enum Activity { Rest, Nap, Wander, ClimbWindow, WatchCursor, Inspect, CarryJelly }

        /// <summary>How long the keys must be quiet before she takes over. A field for the checks.</summary>
        internal float EngageAfter = 5f;
        const float DeathSulk = 120f;

        public bool Engaged { get; private set; }
        /// <summary>True while she is asleep; the shell plays the campfire lie-down for it.</summary>
        public bool Napping { get; private set; }
        public Activity Current { get; private set; } = Activity.Rest;

        bool wakeRequested;

        /// <summary>The shell asks once a frame; a true answer starts the wake-up animation.</summary>
        public bool ConsumeWakeRequest()
        {
            if (!wakeRequested) return false;
            wakeRequested = false;
            return true;
        }

        /// <summary>A real key, pad or drag: hers again, this very frame.</summary>
        public void NoteRealInput()
        {
            quiet = 0f;
            if (!Engaged) return;
            if (Napping) { Napping = false; wakeRequested = true; }
            Engaged = false;
        }

        enum Phase { Telegraph, Go, Do }

        float quiet, clock, sulk;
        float energy = .6f;
        float activityTime, activityBudget;
        Phase phase;
        float phaseTime;
        PointF target;
        RectangleF targetRect;
        Glider targetJelly;
        int carryStage;
        float bestDist, stall;
        readonly Dictionary<Activity, float> lastPicked = new Dictionary<Activity, float>();
        readonly Dictionary<Activity, float> shunnedUntil = new Dictionary<Activity, float>();
        readonly HashSet<IntPtr> knownWindows = new HashSet<IntPtr>();
        bool windowsSeeded;
        RectangleF freshWindow;
        float freshAge = 999f;
        PointF cursorPrev;
        float cursorStillFor, cursorNearFor;
        int faceTapFrames, faceTapDir;
        float faceTapCooldown;
        int jumpHoldFrames;
        float legHopX = float.NaN;

        /// <summary>One leg in four gets a skip at some point along it.</summary>
        void RollLegHop(in IdleContext ctx)
        {
            legHopX = rng.NextDouble() < .25
                ? ctx.Player.Pos.X + (target.X - ctx.Player.Pos.X) * (.3f + (float)rng.NextDouble() * .4f)
                : float.NaN;
        }
        bool wasDead;
        readonly Random rng;

        public IdleDirector(Random rng) { this.rng = rng; }

        /// <summary>When true, every Drive leaves its state in DebugText for the debug pane.</summary>
        public bool DebugEnabled;
        public string DebugText { get; private set; } = "";

        public PetInput Drive(float dt, in IdleContext ctx)
        {
            PetInput result = DriveCore(dt, ctx);
            if (DebugEnabled) DebugText = ComposeDebug(ctx);
            return result;
        }

        string ComposeDebug(in IdleContext ctx)
        {
            string doing = !Engaged
                ? (sulk > 0f ? $"sulking for {sulk:F0}s more"
                             : $"waiting -- quiet {quiet:F1}s of {EngageAfter:F0}s")
                : $"{Current}  ({(Current == Activity.CarryJelly ? "stage " + carryStage : phase.ToString().ToLowerInvariant())})"
                  + $"  {activityTime:F1}s / {activityBudget:F0}s";
            return doing + "\n"
                + $"energy {energy:F2}   napping {(Napping ? "yes" : "no")}   stall {stall:F1}s\n"
                + $"target {target.X:F0},{target.Y:F0}"
                + $"   her {ctx.Player.Pos.X:F0},{ctx.Player.Pos.Y:F0}"
                + $"   stamina {ctx.Player.Stamina:F0}\n"
                + $"windows {ctx.Windows.Count}   climb pick {(climbCandidate.Width > 0f ? "yes" : "no")}"
                + $"   fresh window {(freshAge < 30f ? freshAge.ToString("F0") + "s ago" : "no")}\n"
                + $"cursor {ctx.Cursor.X:F0},{ctx.Cursor.Y:F0}"
                + $"   still {cursorStillFor:F1}s   near her {cursorNearFor:F1}s\n"
                + $"fullscreen fg {(ctx.ForegroundFullscreen ? "yes" : "no")}"
                + $"   seekers dormant {(ctx.SeekersDormant ? "yes" : "no")}";
        }

        PetInput DriveCore(float dt, in IdleContext ctx)
        {
            var input = new PetInput();
            clock += dt;
            WatchWindows(ctx);
            WatchCursorMotion(dt, ctx);

            // Death ends the outing, quietly -- no wake-up from a corpse -- and earns a sulk
            // before she is willing to go out again.
            bool dead = ctx.Player.IsDead || ctx.Player.IsRespawning;
            if (dead)
            {
                if (!wasDead) { sulk = DeathSulk; Napping = false; Engaged = false; }
                wasDead = true;
                return input;
            }
            wasDead = false;
            if (sulk > 0f) { sulk -= dt; return input; }

            quiet += dt;
            if (!Engaged)
            {
                if (quiet < EngageAfter) return input;
                Engaged = true;
                // The nap clock restarts with the outing: fresh out of the player's hands she
                // does things, and sleeps when she has earned it. Without this, "time since
                // the last nap" began at forever and the very first roll chose sleep.
                lastPicked[Activity.Nap] = clock;
                // Settle first; the first real activity comes when the rest runs out.
                Begin(Activity.Rest, ctx);
            }

            // Something fullscreen in the foreground is something being watched. Sleep
            // through the whole of it.
            if (ctx.ForegroundFullscreen && Current != Activity.Nap)
                Begin(Activity.Nap, ctx);

            // A seeker that can still hunt is nothing to be near. Everything else waits.
            Seeker threat = NearestThreat(ctx, out float threatDistance);
            if (threat != null && threatDistance < 90f)
            {
                if (Napping) { Napping = false; wakeRequested = true; }   // startled awake
                if (Current != Activity.Rest) Begin(Activity.Rest, ctx);
                input.MoveX = ctx.Player.Pos.X >= threat.Pos.X ? 1 : -1;
                Drain(dt, moving: true);
                return input;
            }

            activityTime += dt;
            phaseTime += dt;
            bool done = activityTime >= activityBudget && !(Current == Activity.Nap && ctx.ForegroundFullscreen);
            if (done) Finish(ctx);

            switch (Current)
            {
                case Activity.Rest:
                    FaceCursorIfNear(ref input, ctx, dt);
                    Drain(dt, moving: false, resting: true);
                    break;

                case Activity.Nap:
                    // Nowhere to lie down mid-air; wait for the ground, then sleep.
                    if (ctx.Player.onGround && ctx.Player.State == Player.StNormal) Napping = true;
                    if (Napping) energy = Math.Min(1f, energy + dt / 50f);
                    break;

                case Activity.WatchCursor:
                    FaceCursor(ref input, ctx);
                    if (cursorStillFor > 3f) Finish(ctx);
                    Drain(dt, moving: false, resting: true);
                    break;

                case Activity.Wander:
                case Activity.ClimbWindow:
                case Activity.Inspect:
                    RunOuting(ref input, dt, ctx);
                    break;

                case Activity.CarryJelly:
                    RunCarry(ref input, dt, ctx);
                    break;
            }
            return input;
        }

        // ===== the brain =====

        void Begin(Activity next, in IdleContext ctx)
        {
            Current = next;
            lastPicked[next] = clock;
            activityTime = 0f;
            phase = Phase.Telegraph;
            phaseTime = 0f;
            bestDist = float.MaxValue;
            stall = 0f;
            carryStage = 0;
            Napping = false;
            PetWindow.Log("idle: " + next);

            switch (next)
            {
                case Activity.Rest:
                    activityBudget = 2f + (float)rng.NextDouble() * 5f;
                    break;
                case Activity.Nap:
                    activityBudget = 45f + (float)rng.NextDouble() * 105f;
                    break;
                case Activity.WatchCursor:
                    activityBudget = 5f + (float)rng.NextDouble() * 5f;
                    break;
                case Activity.Wander:
                    activityBudget = 20f;
                    target = WanderPoint(ctx);
                    RollLegHop(ctx);
                    break;
                case Activity.ClimbWindow:
                case Activity.Inspect:
                    activityBudget = 30f;
                    targetRect = next == Activity.Inspect ? freshWindow : climbCandidate;
                    target = new PointF(targetRect.Left + targetRect.Width / 2f, targetRect.Top);
                    break;
                case Activity.CarryJelly:
                    activityBudget = 35f;
                    target = targetJelly == null ? ctx.Player.Pos : targetJelly.Pos;
                    break;
            }
        }

        void Finish(in IdleContext ctx)
        {
            if (Current == Activity.Nap && Napping)
            {
                // Waking on her own gets the stretch too.
                Napping = false;
                wakeRequested = true;
            }
            if (Current != Activity.Rest) { Begin(Activity.Rest, ctx); return; }
            Begin(Pick(ctx), ctx);
        }

        /// <summary>The watchdog's verdict: this was not working; do not try it again soon.</summary>
        void Abandon(in IdleContext ctx)
        {
            shunnedUntil[Current] = clock + 60f;
            Begin(Activity.Rest, ctx);
        }

        Activity Pick(in IdleContext ctx)
        {
            float Since(Activity a) => clock - (lastPicked.TryGetValue(a, out float at) ? at : -600f);
            float Novelty(Activity a) => Math.Clamp(Since(a) / 240f, 0f, 1f) * .8f;
            bool Shunned(Activity a) => shunnedUntil.TryGetValue(a, out float until) && clock < until;

            var scores = new List<(Activity What, float Score)>
            {
                (Activity.Rest, .3f),
                (Activity.Wander, .4f + energy * .6f + Novelty(Activity.Wander)),
                (Activity.WatchCursor, cursorNearFor > 0f && cursorStillFor < 1f ? 1.2f : .05f),
            };
            // Sleep is earned: tired, or a long while awake. Offering it fresh out of a nap
            // made her doze her whole day away.
            if (energy < .5f || Since(Activity.Nap) > 300f)
                scores.Add((Activity.Nap, (1f - energy) * 2f + Since(Activity.Nap) / 600f));
            climbCandidate = FindClimbable(ctx);
            if (climbCandidate.Width > 0f)
                scores.Add((Activity.ClimbWindow, .5f + energy * .7f + Novelty(Activity.ClimbWindow)));
            if (freshAge < 30f)
                scores.Add((Activity.Inspect, 2.5f));
            targetJelly = PickJelly(ctx);
            if (targetJelly != null)
                scores.Add((Activity.CarryJelly, .6f + Novelty(Activity.CarryJelly)));

            float total = 0f;
            for (int i = scores.Count - 1; i >= 0; i--)
            {
                if (Shunned(scores[i].What) || scores[i].Score <= 0f) scores.RemoveAt(i);
                else total += scores[i].Score;
            }
            if (total <= 0f) return Activity.Rest;
            float roll = (float)rng.NextDouble() * total;
            foreach ((Activity what, float score) in scores)
            {
                roll -= score;
                if (roll <= 0f) return what;
            }
            return Activity.Rest;
        }

        // ===== outings: wander, climb, inspect =====

        void RunOuting(ref PetInput input, float dt, in IdleContext ctx)
        {
            if (phase == Phase.Telegraph)
            {
                // Look where you are going before you go: the half second that makes it read
                // as a decision rather than a lottery.
                FaceToward(ref input, ctx, target.X);
                if (phaseTime >= .4f + (float)rng.NextDouble() * .4f) { phase = Phase.Go; phaseTime = 0f; }
                return;
            }
            if (phase == Phase.Do)
            {
                if (Current == Activity.Wander)
                {
                    // A stroll is legs, not a trek: pause a moment where she stopped, look
                    // about, then off again, until the outing's time is spent. One leg and a
                    // long stand read as barely moving at all.
                    FaceCursorIfNear(ref input, ctx, dt);
                    Drain(dt, moving: false, resting: true);
                    if (phaseTime > 1f + (float)rng.NextDouble() * 2f)
                    {
                        if (activityTime > activityBudget - 4f) { Finish(ctx); return; }
                        target = WanderPoint(ctx);
                        RollLegHop(ctx);
                        phase = Phase.Go;
                        phaseTime = 0f;
                        bestDist = float.MaxValue;
                        stall = 0f;
                    }
                    return;
                }
                // Sitting on what she climbed; the idle fidgets do the rest.
                FaceCursorIfNear(ref input, ctx, dt);
                Drain(dt, moving: false, resting: true);
                if (phaseTime > 8f + (float)rng.NextDouble() * 14f) Finish(ctx);
                return;
            }

            bool wantTop = Current != Activity.Wander;
            if (Arrived(ctx, wantTop)) { phase = Phase.Do; phaseTime = 0f; return; }
            if (Current == Activity.Wander)
            {
                // A wall too tall to bother with ends the leg where she stands -- she pauses,
                // then strolls somewhere else, which is what choosing not to looks like.
                int dir = target.X > ctx.Player.Pos.X ? 1 : -1;
                if (WallAhead(ctx, ctx.Player, dir, out float top) &&
                    ctx.Player.Pos.Y - top > 60f)
                { phase = Phase.Do; phaseTime = 0f; return; }
                // Now and then a leg has a skip in it, for no reason a pet needs.
                if (!float.IsNaN(legHopX) && Math.Abs(ctx.Player.Pos.X - legHopX) < 4f &&
                    ctx.Player.onGround && jumpHoldFrames == 0)
                { ctx.Player.BufferJump(); jumpHoldFrames = 8; legHopX = float.NaN; }
            }
            // An outing that targets a top climbs whatever it takes; a stroll still climbs
            // what is modest, because a window in the way is half the fun of a desk.
            Walk(ref input, dt, ctx, target.X, climbUpTo: wantTop ? float.MaxValue : 60f);
            Drain(dt, moving: true);
            Watchdog(dt, ctx, wantTop);
        }

        bool Arrived(in IdleContext ctx, bool wantTop)
        {
            Player p = ctx.Player;
            if (!wantTop) return Math.Abs(p.Pos.X - target.X) <= 3f && p.onGround;
            return p.onGround && p.Pos.X > targetRect.Left + 2f && p.Pos.X < targetRect.Right - 2f &&
                Math.Abs(p.Pos.Y - targetRect.Top) <= 2f;
        }

        // ===== the jellyfish errand =====

        void RunCarry(ref PetInput input, float dt, in IdleContext ctx)
        {
            Player p = ctx.Player;
            if (targetJelly == null || targetJelly.BeingDragged) { Abandon(ctx); return; }

            switch (carryStage)
            {
                case 0:     // to the jellyfish
                    if (phase == Phase.Telegraph)
                    {
                        FaceToward(ref input, ctx, targetJelly.Pos.X);
                        if (phaseTime >= .5f) { phase = Phase.Go; phaseTime = 0f; }
                        return;
                    }
                    if (Math.Abs(p.Pos.X - targetJelly.Pos.X) <= 6f &&
                        Math.Abs(p.Pos.Y - targetJelly.Pos.Y) <= 14f)
                    { carryStage = 1; stall = 0f; bestDist = float.MaxValue; return; }
                    Walk(ref input, dt, ctx, targetJelly.Pos.X, climbUpTo: 0f);
                    Drain(dt, moving: true);
                    Watchdog(dt, ctx, false, targetJelly.Pos.X);
                    break;

                case 1:     // hands on
                    input.GrabHeld = true;
                    FaceToward(ref input, ctx, targetJelly.Pos.X);
                    if (p.IsHoldingGlider)
                    {
                        carryStage = 2;
                        target = WanderPoint(ctx);
                        bestDist = float.MaxValue;
                        stall = 0f;
                    }
                    else { stall += dt; if (stall > 2f) Abandon(ctx); }
                    break;

                case 2:     // the walk, jellyfish overhead -- stepping off a ledge floats
                    input.GrabHeld = true;
                    if (Math.Abs(p.Pos.X - target.X) <= 4f && p.onGround)
                    { carryStage = 3; stall = 0f; return; }
                    Walk(ref input, dt, ctx, target.X, climbUpTo: 0f);
                    Drain(dt, moving: true);
                    Watchdog(dt, ctx, false);
                    break;

                case 3:     // put it down gently: down and let go is the soft drop
                    input.MoveY = 1;
                    input.GliderMoveY = 1;
                    if (!p.IsHoldingGlider) { carryStage = 4; stall = 0f; }
                    else { stall += dt; if (stall > 2f) Abandon(ctx); }
                    break;

                case 4:     // step back and admire it
                    if (Math.Abs(p.Pos.X - target.X) < 12f)
                        input.MoveX = p.Facing >= 0 ? -1 : 1;
                    else
                    {
                        FaceToward(ref input, ctx, targetJelly.Pos.X);
                        if (phaseTime > 3f) Finish(ctx);
                    }
                    Drain(dt, moving: input.MoveX != 0);
                    break;
            }
        }

        Glider PickJelly(in IdleContext ctx)
        {
            foreach (Glider glider in ctx.Gliders)
            {
                if (glider.IsHeld || glider.BeingDragged) continue;
                if (Math.Abs(glider.Pos.Y - ctx.Player.Pos.Y) > 60f) continue;
                if (NearAPuffer(ctx, glider.Pos)) continue;
                return glider;
            }
            return null;
        }

        // ===== the pilot =====

        /// <summary>
        /// One frame of getting there: walk at it, hop what is low, jump what a jump truly
        /// reaches, and grab-climb walls up to climbUpTo. She gives up through the watchdog,
        /// not here. Her jump tops out near twenty-eight pixels (JumpSpeed -105, VarJumpTime
        /// 0.2, gravity 900 in the reference), which is what draws the lines below.
        /// </summary>
        void Walk(ref PetInput input, float dt, in IdleContext ctx, float targetX, float climbUpTo)
        {
            Player p = ctx.Player;
            int dir = targetX > p.Pos.X + 2f ? 1 : targetX < p.Pos.X - 2f ? -1 : 0;

            if (p.State == Player.StClimb)
            {
                // Mid-climb: keep hold, keep going up; the climb-hop at the ledge is hers.
                // Letting go before the tank is dry is what a player does too.
                if (p.Stamina < 25f) { jumpHoldFrames = 0; return; }
                input.GrabHeld = true;
                input.MoveY = -1;
                input.MoveX = p.Facing;
                return;
            }

            input.MoveX = dir;
            if (dir == 0) return;

            // A jump-height wall is taken at a run: leave the ground early enough to carry
            // the walking speed over the lip, the way a player times it. Jumping only once
            // pinned against the wall rises with no way sideways and falls straight back.
            if (p.onGround && jumpHoldFrames == 0 && Math.Abs(p.Speed.X) > 60f &&
                WallWithin(ctx, p, dir, 10f, 34f, out float farTop))
            {
                float farRise = p.Pos.Y - farTop;
                if (farRise > 14f && farRise <= 24f) { p.BufferJump(); jumpHoldFrames = 12; }
            }

            if (WallAhead(ctx, p, dir, out float wallTop))
            {
                float rise = p.Pos.Y - wallTop;      // how far above her feet it stands
                if (rise <= 14f)
                {
                    // A step: hop it. The buffer is the same one the keyboard fills.
                    if (p.onGround && jumpHoldFrames == 0) { p.BufferJump(); jumpHoldFrames = 8; }
                }
                else if (rise <= 24f)
                {
                    // Jump height: a full jump lands her on top, which is how a player
                    // crosses a low window in the way.
                    if (p.onGround && jumpHoldFrames == 0) { p.BufferJump(); jumpHoldFrames = 12; }
                }
                else if (rise <= climbUpTo || climbUpTo == float.MaxValue)
                {
                    // Tall: jump first and grab the wall at the top of the arc -- the jump is
                    // free height the stamina never pays for -- then climb.
                    if (p.onGround && jumpHoldFrames == 0) { p.BufferJump(); jumpHoldFrames = 10; }
                    input.GrabHeld = true;
                    input.MoveY = -1;
                }
            }
            if (jumpHoldFrames > 0) { jumpHoldFrames--; input.JumpHeld = true; }
            input.JumpPressed = p.HasJumpBuffer;
        }

        static bool WallAhead(in IdleContext ctx, Player p, int dir, out float top)
            => WallWithin(ctx, p, dir, 5f, 8f, out top);

        static bool WallWithin(in IdleContext ctx, Player p, int dir, float from, float to,
            out float top)
        {
            // A strip at her knees and chest, some way ahead; the floor underfoot does not count.
            float l = Math.Min(p.Pos.X + dir * from, p.Pos.X + dir * to);
            float r = Math.Max(p.Pos.X + dir * from, p.Pos.X + dir * to);
            top = float.MaxValue;
            bool found = false;
            foreach (Solid s in ctx.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.B <= p.Pos.Y - 10f || s.T >= p.Pos.Y - 1f) continue;
                found = true;
                top = Math.Min(top, s.T);
            }
            return found;
        }

        void Watchdog(float dt, in IdleContext ctx, bool wantTop, float? overrideX = null)
        {
            float dx = Math.Abs((overrideX ?? target.X) - ctx.Player.Pos.X);
            float dy = wantTop ? Math.Abs(targetRect.Top - ctx.Player.Pos.Y) : 0f;
            float dist = dx + dy;
            if (dist < bestDist - 1f) { bestDist = dist; stall = 0f; }
            else stall += dt;
            if (stall > 3f) Abandon(ctx);
        }

        // ===== small perceptions =====

        void WatchWindows(in IdleContext ctx)
        {
            if (!windowsSeeded)
            {
                foreach (var window in ctx.Windows) knownWindows.Add(window.Key);
                windowsSeeded = true;
                return;
            }
            freshAge += 1f / 60f;
            seenScratch.Clear();
            foreach (var window in ctx.Windows)
            {
                seenScratch.Add(window.Key);
                if (knownWindows.Add(window.Key))
                {
                    // Something new on the desk. New things are interesting.
                    freshWindow = window.Value;
                    freshAge = 0f;
                }
            }
            knownWindows.RemoveWhere(handle => !seenScratch.Contains(handle));
        }

        readonly HashSet<IntPtr> seenScratch = new HashSet<IntPtr>();

        void WatchCursorMotion(float dt, in IdleContext ctx)
        {
            float moved = Math.Abs(ctx.Cursor.X - cursorPrev.X) + Math.Abs(ctx.Cursor.Y - cursorPrev.Y);
            cursorPrev = ctx.Cursor;
            cursorStillFor = moved < 2f ? cursorStillFor + dt : 0f;
            float near = Math.Abs(ctx.Cursor.X - ctx.Player.Pos.X) +
                Math.Abs(ctx.Cursor.Y - ctx.Player.Pos.Y);
            cursorNearFor = near < 90f && moved >= 2f ? cursorNearFor + dt : 0f;
            if (faceTapCooldown > 0f) faceTapCooldown -= dt;
        }

        void FaceCursorIfNear(ref PetInput input, in IdleContext ctx, float dt)
        {
            if (Math.Abs(ctx.Cursor.X - ctx.Player.Pos.X) < 90f &&
                Math.Abs(ctx.Cursor.Y - ctx.Player.Pos.Y) < 60f)
                FaceCursor(ref input, ctx);
        }

        void FaceCursor(ref PetInput input, in IdleContext ctx)
            => FaceToward(ref input, ctx, ctx.Cursor.X);

        /// <summary>
        /// Face something without going there: a two-frame tap of the stick, which shifts her
        /// weight the way a person turns on the spot.
        /// </summary>
        void FaceToward(ref PetInput input, in IdleContext ctx, float x)
        {
            if (faceTapFrames > 0)
            {
                faceTapFrames--;
                input.MoveX = faceTapDir;
                return;
            }
            int wanted = x > ctx.Player.Pos.X + 4f ? 1 : x < ctx.Player.Pos.X - 4f ? -1 : 0;
            if (wanted == 0 || wanted == ctx.Player.Facing || faceTapCooldown > 0f) return;
            faceTapDir = wanted;
            faceTapFrames = 2;
            faceTapCooldown = .6f;
        }

        // ===== places =====

        PointF WanderPoint(in IdleContext ctx)
        {
            RectangleF room = RoomAround(ctx);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float x = room.Left + 20f + (float)rng.NextDouble() * Math.Max(1f, room.Width - 40f);
                // A leg worth walking: somewhere that is not two steps from here.
                if (Math.Abs(x - ctx.Player.Pos.X) < 60f) continue;
                var spot = new PointF(x, ctx.Player.Pos.Y);
                if (!NearAPuffer(ctx, spot)) return spot;
            }
            return ctx.Player.Pos;
        }

        RectangleF climbCandidate;

        /// <summary>
        /// A window she can actually get on top of from where she stands: its wall must come
        /// down to her ground -- a floating one's wall hangs out of reach -- and its top must
        /// be within what her stamina can afford, or the watchdog spends three seconds
        /// discovering what this could have known for free.
        /// </summary>
        RectangleF FindClimbable(in IdleContext ctx)
        {
            if (ctx.Windows.Count == 0) return RectangleF.Empty;
            RectangleF room = RoomAround(ctx);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var window = ctx.Windows[rng.Next(ctx.Windows.Count)];
                RectangleF rect = window.Value;
                if (!room.IntersectsWith(rect)) continue;
                if (rect.Width < 24f) continue;
                if (rect.Bottom < ctx.Player.Pos.Y - 8f) continue;      // wall out of reach
                if (ctx.Player.Pos.Y - rect.Top > 130f) continue;       // jump start + the tank
                if (ctx.Player.Pos.Y - rect.Top < 16f) continue;        // hardly a climb
                if (NearAPuffer(ctx, new PointF(rect.Left + rect.Width / 2f, rect.Top))) continue;
                return rect;
            }
            return RectangleF.Empty;
        }

        RectangleF RoomAround(in IdleContext ctx)
        {
            foreach (RectangleF monitor in ctx.Monitors)
                if (ctx.Player.Pos.X >= monitor.Left && ctx.Player.Pos.X <= monitor.Right)
                    return monitor;
            return ctx.Monitors.Count > 0
                ? ctx.Monitors[0]
                : new RectangleF(ctx.Player.Pos.X - 100f, ctx.Player.Pos.Y - 100f, 200f, 200f);
        }

        Seeker NearestThreat(in IdleContext ctx, out float distance)
        {
            distance = float.MaxValue;
            if (ctx.SeekersDormant) return null;
            Seeker nearest = null;
            foreach (Seeker seeker in ctx.Seekers)
            {
                if (seeker.Removed) continue;
                float d = Math.Abs(seeker.Pos.X - ctx.Player.Pos.X) +
                    Math.Abs(seeker.Pos.Y - ctx.Player.Pos.Y);
                if (d < distance) { distance = d; nearest = seeker; }
            }
            return nearest;
        }

        bool NearAPuffer(in IdleContext ctx, PointF spot)
        {
            foreach (Puffer puffer in ctx.Puffers)
            {
                if (puffer.Removed) continue;
                float d = Math.Abs(puffer.Pos.X - spot.X) + Math.Abs(puffer.Pos.Y - spot.Y);
                if (d < 48f) return true;
            }
            return false;
        }

        void Drain(float dt, bool moving, bool resting = false)
        {
            if (moving) energy = Math.Max(0f, energy - dt / 120f);
            else if (resting) energy = Math.Min(1f, energy + dt / 240f);
        }

        // ===== the checks' handles =====

        internal void ForceEngageForCheck()
        {
            quiet = EngageAfter;
            Engaged = true;
            Begin(Activity.Rest, default);
            activityBudget = 0f;
        }

        internal void ForceActivityForCheck(Activity what, PointF at, RectangleF rect = default,
            Glider jelly = null)
        {
            Engaged = true;
            Current = what;
            lastPicked[what] = clock;
            targetJelly = jelly;
            target = at;
            if (rect.Width > 0f)
            {
                targetRect = rect;
                target = new PointF(rect.Left + rect.Width / 2f, rect.Top);
            }
            activityTime = 0f;
            activityBudget = 999f;
            phase = Phase.Go;
            phaseTime = 0f;
            bestDist = float.MaxValue;
            stall = 0f;
            carryStage = 0;
            Napping = false;
        }
    }
}
