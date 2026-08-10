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
        public bool WindowsAreKevin;                          // a dash would fling windows
        public bool WindowsReactToDash;                       // kevin or moon: a dash moves them
        public bool EdgesClimbable;                           // no horizontal wrap: edge walls exist
        public float EdgeLeft, EdgeRight;                     // game px, virtual desktop extremes
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
    /// Deliberate refusals: she never dashes in kevin mode -- there a dash flings the user's
    /// windows about, and nothing she does idly should move anyone's work -- and elsewhere
    /// only along ground she has checked clear of solids and pufferfish, so a dash can never
    /// arrive at anything. Her autonomous input never counts as the "real" input that wakes
    /// dormant seekers, or she would stroll back into the one that just killed her.
    /// Fullscreen in the foreground means the user is watching something, and she sleeps
    /// through it.
    /// </remarks>
    internal sealed class IdleDirector
    {
        public enum Activity
        { Rest, Nap, Wander, ClimbWindow, WatchCursor, Inspect, CarryJelly, HangOnEdge }

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
        float watchdogAim = float.PositiveInfinity;
        float bestClimbY;
        bool crossingBudgeted;
        int walledLegs;
        bool trappedLeg;
        bool legElevated;
        int dashAimFrames;
        int leapCatchFrames;
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
        int neutralFrames;
        int wallSide;
        enum ClimbIntent { Up, Down, Hang, NeutralHop, LeapAcross }
        ClimbIntent climbIntent;
        float intentT;
        float climbStartY;
        bool prevClimbing;
        bool pendingLeap;
        float hangFor;
        int hangEdgeDir;
        float legHopX = float.NaN;
        float legDashX = float.NaN;
        bool legLedgeJump;
        int sitStage;
        float sitT, sitPause;
        bool sitHang, forceSitHang;
        int lipDir;

        internal void ForceSitHangForCheck() => forceSitHang = true;

        /// <summary>
        /// One leg in four gets a skip at some point along it, and a long one may get a dash
        /// -- never in kevin mode, where a dash is what throws windows.
        /// </summary>
        void RollLegSpice(in IdleContext ctx)
        {
            float span = target.X - ctx.Player.Pos.X;
            legHopX = rng.NextDouble() < .25
                ? ctx.Player.Pos.X + span * (.3f + (float)rng.NextDouble() * .4f)
                : float.NaN;
            legDashX = !ctx.WindowsAreKevin && Math.Abs(span) > 140f && rng.NextDouble() < .35
                ? ctx.Player.Pos.X + span * (.2f + (float)rng.NextDouble() * .3f)
                : float.NaN;
            legLedgeJump = rng.NextDouble() < .7;
        }

        internal void ForceLegDashForCheck(float x) => legDashX = x;



        /// <summary>
        /// Whether a dash from here would arrive at nothing: no solid and no pufferfish
        /// anywhere along the corridor a ground dash covers, with margin.
        /// </summary>
        static bool DashPathClear(in IdleContext ctx, int dir)
        {
            Player p = ctx.Player;
            float l = Math.Min(p.Pos.X + dir * 4f, p.Pos.X + dir * 150f);
            float r = Math.Max(p.Pos.X + dir * 4f, p.Pos.X + dir * 150f);
            foreach (Solid s in ctx.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.B <= p.Pos.Y - 10f || s.T >= p.Pos.Y - 1f) continue;
                return false;
            }
            foreach (Puffer puffer in ctx.Puffers)
            {
                if (puffer.Removed) continue;
                if (puffer.Pos.X > l - 16f && puffer.Pos.X < r + 16f &&
                    Math.Abs(puffer.Pos.Y - p.Pos.Y) < 32f) return false;
            }
            return true;
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
                + $"   seekers dormant {(ctx.SeekersDormant ? "yes" : "no")}\n"
                + $"kevin {(ctx.WindowsAreKevin ? "yes" : "no")}"
                + $"   edges {(ctx.EdgesClimbable ? "climbable" : "wrapping")}";
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
                    // Give the watching a couple of seconds of life before deciding the
                    // cursor has gone quiet -- beginning and instantly finishing a dozen
                    // times a minute is a tic, not attention.
                    if (activityTime > 2f && cursorStillFor > 3f) Finish(ctx);
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

                case Activity.HangOnEdge:
                    RunHang(ref input, dt, ctx);
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
            watchdogAim = float.PositiveInfinity;
            bestClimbY = float.MaxValue;
            crossingBudgeted = false;
            stall = 0f;
            driftTime = 0f;
            driftAnchor = ctx.Player != null ? ctx.Player.Pos : default;
            driftStrikes = 0;
            stepPeakY = ctx.Player != null ? ctx.Player.Pos.Y : 0f;
            stepFalls = 0;
            carryStage = 0;
            hangFor = 0f;
            wallSide = 0;
            legHopX = float.NaN;
            legDashX = float.NaN;
            legLedgeJump = false;
            walledLegs = 0;
            trappedLeg = false;
            legElevated = false;
            pendingLeap = false;
            route = null;
            routeAt = 0;
            routeNullFor = 0f;
            dashAimFrames = 0;
            leapCatchFrames = 0;
            Napping = false;
            PetWindow.Log("idle: " + next);

            switch (next)
            {
                case Activity.Rest:
                    activityBudget = 1.5f + (float)rng.NextDouble() * 3f;
                    break;
                case Activity.Nap:
                    activityBudget = 45f + (float)rng.NextDouble() * 105f;
                    break;
                case Activity.WatchCursor:
                    activityBudget = 5f + (float)rng.NextDouble() * 5f;
                    break;
                case Activity.Wander:
                    activityBudget = 25f;
                    NewWanderLeg(ctx);
                    break;
                case Activity.ClimbWindow:
                case Activity.Inspect:
                    targetRect = next == Activity.Inspect ? freshWindow : climbCandidate;
                    target = new PointF(targetRect.Left + targetRect.Width / 2f, targetRect.Top);
                    // A taller wall is a longer outing; the watchdog still ends a stalled one.
                    activityBudget = 30f +
                        Math.Max(0f, ctx.Player.Pos.Y - targetRect.Top) * .3f;
                    break;
                case Activity.CarryJelly:
                    activityBudget = 35f;
                    target = targetJelly == null ? ctx.Player.Pos : targetJelly.Pos;
                    break;
                case Activity.HangOnEdge:
                {
                    activityBudget = 25f;
                    if (hangEdgeDir == 0) { Begin(Activity.Rest, ctx); return; }
                    // Aim a touch inside the wall so the walk keeps pressing at it, and pick
                    // a hanging point forty to eighty pixels up the side of the screen.
                    float edgeX = hangEdgeDir > 0 ? ctx.EdgeRight : ctx.EdgeLeft;
                    target = new PointF(edgeX + hangEdgeDir * 2f, ctx.Player.Pos.Y);
                    // Never into the ceiling: hanging began from wherever she stood, and
                    // begun from a window top that aimed her five pixels under the screen's
                    // lid, where the jump cycle flickers against it.
                    float hangY = Math.Max(RoomAround(ctx).Top + 48f,
                        ctx.Player.Pos.Y - 40f - (float)rng.NextDouble() * 40f);
                    targetRect = new RectangleF(edgeX - 2f, hangY, 4f, 4f);
                    break;
                }
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
            if (Current != Activity.Rest)
            {
                PetWindow.Log("idle: finished " + Current);
                Begin(Activity.Rest, ctx);
                return;
            }
            Begin(Pick(ctx), ctx);
        }

        /// <summary>The watchdog's verdict: this was not working; do not try it again soon.</summary>
        void Abandon(in IdleContext ctx)
        {
            PetWindow.Log($"idle: gave up on {Current} at"
                + $" {ctx.Player.Pos.X:F0},{ctx.Player.Pos.Y:F0}");
            // Remember where the idea failed, so the next pick goes somewhere else
            // instead of returning to the same cursed lip every twenty seconds.
            if (Current == Activity.ClimbWindow || Current == Activity.Inspect ||
                Current == Activity.HangOnEdge || legElevated)
                NoteFailedSpot(target);
            // Say what was standing there, so a stuck spot can be diagnosed from the diary.
            int said = 0;
            foreach (Solid s in ctx.Solids)
            {
                if (s.R < ctx.Player.Pos.X - 80f || s.L > ctx.Player.Pos.X + 80f ||
                    s.B < ctx.Player.Pos.Y - 80f || s.T > ctx.Player.Pos.Y + 80f) continue;
                PetWindow.Log($"idle:   nearby solid {s.L:F0},{s.T:F0}..{s.R:F0},{s.B:F0}");
                if (++said >= 6) break;
            }
            // One failed leg does not condemn wandering itself -- the next leg goes
            // somewhere else anyway. The targeted outings keep the long shun: their
            // target was the thing that failed.
            shunnedUntil[Current] = clock + (Current == Activity.Wander ? 15f : 60f);
            Begin(Activity.Rest, ctx);
        }

        Activity Pick(in IdleContext ctx)
        {
            float Since(Activity a) => clock - (lastPicked.TryGetValue(a, out float at) ? at : -600f);
            float Novelty(Activity a) => Math.Clamp(Since(a) / 240f, 0f, 1f) * .8f;
            bool Shunned(Activity a) => shunnedUntil.TryGetValue(a, out float until) && clock < until;

            // Rest is deliberately absent: Finish already lays a rest between any two
            // activities, and offering it here as well chained them into long stillness.
            var scores = new List<(Activity What, float Score)>
            {
                (Activity.Wander, .4f + energy * .6f + Novelty(Activity.Wander)),
                (Activity.WatchCursor,
                    cursorNearFor > .5f && cursorStillFor < .5f &&
                    Since(Activity.WatchCursor) > 20f ? 1.2f : 0f),
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
            if (ctx.EdgesClimbable)
            {
                RectangleF room = RoomAround(ctx);
                bool leftHere = Math.Abs(room.Left - ctx.EdgeLeft) < 2f;
                bool rightHere = Math.Abs(room.Right - ctx.EdgeRight) < 2f;
                hangEdgeDir = leftHere && rightHere ? (rng.Next(2) == 0 ? -1 : 1)
                    : leftHere ? -1 : rightHere ? 1 : 0;
                // Once a minute at most: with no climbable window about it was the only
                // vertical thing on offer, and she hung off the screen four times a minute.
                if (hangEdgeDir != 0 && Since(Activity.HangOnEdge) > 60f)
                    scores.Add((Activity.HangOnEdge, .35f + energy * .5f + Novelty(Activity.HangOnEdge)));
            }
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
                        NewWanderLeg(ctx);
                        phase = Phase.Go;
                        phaseTime = 0f;
                    }
                    return;
                }
                // On top of what she climbed: not a statue. Settle, stroll to the lip,
                // peer over it ducked, sometimes swing below and hang off the edge, climb
                // back over, then lounge -- what a player parked on a ledge does.
                RunTopSit(ref input, dt, ctx);
                return;
            }

            // The last line of defence against every shape of loop: a Go phase that has
            // produced no real displacement for two four-second windows in a row is going
            // nowhere, whichever sub-system is busy pretending otherwise -- micro-legs
            // re-picking in a pocket reset the per-leg watchdogs, but they cannot fake
            // net movement.
            driftTime += dt;
            if (driftTime >= 4f)
            {
                driftTime = 0f;
                float moved = Math.Abs(ctx.Player.Pos.X - driftAnchor.X)
                    + Math.Abs(ctx.Player.Pos.Y - driftAnchor.Y);
                driftAnchor = ctx.Player.Pos;
                if (moved >= 24f) driftStrikes = 0;
                else if (++driftStrikes >= 2)
                {
                    NoteFailedSpot(ctx.Player.Pos);
                    NoteFailedSpot(target);
                    Abandon(ctx);
                    return;
                }
            }
            bool wantTop = Current != Activity.Wander || legElevated;
            if (Arrived(ctx, wantTop))
            {
                phase = Phase.Do;
                phaseTime = 0f;
                walledLegs = 0;
                trappedLeg = false;
                if (wantTop && Current != Activity.Wander) BeginTopSit(ctx);
                return;
            }
            bool crossing = false;
            if (Current == Activity.Wander && legElevated && !crossingBudgeted)
            {
                // An elevated leg is a longer one; the extension is granted once.
                crossingBudgeted = true;
                activityBudget = Math.Max(activityBudget, activityTime + 30f);
            }
            if (Current == Activity.Wander && !legElevated)
            {
                int dir = target.X > ctx.Player.Pos.X ? 1 : -1;
                // A leg that leaves this monitor has the seam wall in its way -- the gap
                // between displays is built as a solid column -- and that one she scales,
                // walks the top of, and drops off the far side of. It is a longer leg, so
                // it is given the time it needs.
                RectangleF room = RoomAround(ctx);
                crossing = target.X < room.Left || target.X > room.Right;
                // Two legs in a row ending at walls means the ground here is a hole, not a
                // floor with scenery: the next leg climbs out the way a player would.
                crossing |= trappedLeg;
                // Once per leg -- extending it every frame let a failed crossing pin her
                // against the seam for as long as the seam cared to keep her.
                if (crossing && !crossingBudgeted)
                {
                    crossingBudgeted = true;
                    activityBudget = Math.Max(activityBudget, activityTime + 30f);
                }
                // Any other wall too tall to bother with ends the leg where she stands --
                // she pauses, then strolls somewhere else, which is what choosing not to
                // looks like.
                if (!crossing && WallAhead(ctx, ctx.Player, dir, out float top) &&
                    ctx.Player.Pos.Y - top > 60f)
                {
                    walledLegs++;
                    // Half of the time, a little hop against it first -- sizing it up --
                    // and then the leg ends here.
                    if (rng.Next(2) == 0 && ctx.Player.onGround)
                    {
                        ctx.Player.BufferJump();
                        input.JumpPressed = true;
                        input.JumpHeld = true;
                    }
                    phase = Phase.Do; phaseTime = 0f; return;
                }
                // Now and then a leg has a skip in it, for no reason a pet needs.
                if (!float.IsNaN(legHopX) && Math.Abs(ctx.Player.Pos.X - legHopX) < 4f &&
                    ctx.Player.onGround && jumpHoldFrames == 0)
                {
                    ctx.Player.BufferJump();
                    jumpHoldFrames = 8;
                    // Sometimes one skip asks for another a little further on.
                    legHopX = rng.NextDouble() < .4
                        ? ctx.Player.Pos.X + dir * (30f + (float)rng.NextDouble() * 60f)
                        : float.NaN;
                }
                // And a long one may have a dash, on ground checked clear first.
                if (!float.IsNaN(legDashX) && !ctx.WindowsAreKevin && ctx.Player.onGround &&
                    Math.Abs(ctx.Player.Speed.X) > 60f &&
                    Math.Abs(ctx.Player.Pos.X - legDashX) < 6f)
                {
                    legDashX = float.NaN;
                    if (DashPathClear(ctx, dir)) ctx.Player.BufferDash();
                }
            }
            if (wantTop)
            {
                if (route == null && ctx.Player.onGround)
                {
                    // Ask the terrain, not the window: the graph routes to any standable
                    // spot or says plainly that there is no way.
                    IdleNav.BuildSegs(ctx, navSegs);
                    int from = IdleNav.SegUnder(navSegs, ctx.Player.Pos);
                    int to = IdleNav.SegAt(navSegs, target);
                    route = IdleNav.FindRoute(ctx, navSegs, from, to);
                    routeAt = 0;
                    if (route == null)
                    {
                        // No way there. A stroll simply goes somewhere else; a committed
                        // outing waits half a second -- the live world jitters between
                        // polls -- and then gives the idea up.
                        if (Current == Activity.Wander)
                        {
                            legElevated = false;
                            target = WanderPoint(ctx);
                            return;
                        }
                        routeNullFor += dt;
                        if (routeNullFor > .5f) Abandon(ctx);
                        return;
                    }
                    routeNullFor = 0f;
                }
                if (route != null && routeAt < route.Count)
                {
                    RunRouteStep(ref input, dt, ctx);
                    return;
                }
            }
            float aimX = target.X;
            // An outing that targets a top climbs whatever it takes; a stroll still climbs
            // what is modest -- a window in the way is half the fun of a desk -- and a
            // crossing leg climbs like an outing, because the seam demands it.
            Walk(ref input, dt, ctx, aimX,
                climbUpTo: wantTop || crossing ? float.MaxValue : 60f,
                scaleWithJumps: wantTop || crossing);
            input.DashPressed = ctx.Player.HasDashBuffer;
            Drain(dt, moving: true);
            // The via-edge route walks past the target's x on the way to the edge, which
            // poisons a straight-line best-distance; while she is on the wall or in the
            // air of that plan, progress is measured as new height instead.
            Watchdog(dt, ctx, wantTop, aimX, busyScaling:
                crossing && (ctx.Player.State == Player.StClimb || !ctx.Player.onGround));
        }

        void BeginTopSit(in IdleContext ctx)
        {
            sitStage = 0;
            sitT = 0f;
            sitPause = 1.5f + (float)rng.NextDouble() * 2f;
            sitHang = forceSitHang || rng.NextDouble() < .6;
            forceSitHang = false;
            lipDir = ctx.Player.Pos.X > targetRect.Left + targetRect.Width / 2f ? 1 : -1;
        }

        void RunTopSit(ref PetInput input, float dt, in IdleContext ctx)
        {
            Player p = ctx.Player;
            sitT += dt;
            // Off the window entirely: the visit is over, no drama.
            if (p.Pos.Y > targetRect.Top + 50f) { Finish(ctx); return; }
            switch (sitStage)
            {
                case 0:     // settle first
                    FaceCursorIfNear(ref input, ctx, dt);
                    Drain(dt, moving: false, resting: true);
                    if (sitT > sitPause) { sitStage = 1; sitT = 0f; }
                    break;

                case 1:     // stroll to the lip
                {
                    float lipX = lipDir > 0 ? targetRect.Right - 5f : targetRect.Left + 5f;
                    if (Math.Abs(p.Pos.X - lipX) <= 2f && p.onGround) { sitStage = 2; sitT = 0f; break; }
                    input.MoveX = lipX > p.Pos.X ? 1 : -1;
                    if (sitT > 4f) { sitStage = 6; sitT = 0f; }
                    break;
                }

                case 2:     // peer over it, ducked
                    input.MoveY = 1;
                    if (sitT > 1.5f) { sitStage = sitHang ? 3 : 6; sitT = 0f; }
                    break;

                case 3:     // swing below: step off outward, catch the face on the way back
                    if (p.onGround) input.MoveX = lipDir;
                    else { input.MoveX = -lipDir; input.GrabHeld = true; }
                    if (p.State == Player.StClimb) { sitStage = 4; sitT = 0f; }
                    else if (sitT > 2f) { sitStage = 6; sitT = 0f; }
                    break;

                case 4:     // hanging off the edge of the window
                    input.GrabHeld = true;
                    input.MoveX = p.Facing;
                    if (sitT > 1f + (float)rng.NextDouble() * 1.5f || p.Stamina < 30f)
                    { sitStage = 5; sitT = 0f; }
                    break;

                case 5:     // back up and over the lip
                    input.GrabHeld = true;
                    input.MoveY = -1;
                    input.MoveX = p.Facing;
                    if (p.onGround && Math.Abs(p.Pos.Y - targetRect.Top) <= 2f)
                    { sitStage = 6; sitT = 0f; }
                    else if (sitT > 3f) Finish(ctx);
                    break;

                default:    // lounging; the outing ends from here
                    FaceCursorIfNear(ref input, ctx, dt);
                    Drain(dt, moving: false, resting: true);
                    if (phaseTime > 8f + (float)rng.NextDouble() * 14f) Finish(ctx);
                    break;
            }
        }

        bool Arrived(in IdleContext ctx, bool wantTop)
        {
            Player p = ctx.Player;
            if (!wantTop) return Math.Abs(p.Pos.X - target.X) <= 3f && p.onGround;
            // Live window rects jitter a few pixels between polls; standing on the top,
            // or a hair above it, is standing on it -- demanding the stored pixel had her
            // working a wall she was already on top of.
            return p.onGround && p.Pos.X > targetRect.Left + 2f && p.Pos.X < targetRect.Right - 2f &&
                p.Pos.Y <= targetRect.Top + 6f && p.Pos.Y >= targetRect.Top - 10f;
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

        /// <summary>
        /// One frame of the current route maneuver. Advances to the next step the moment
        /// she stands on this step's segment, however she got there -- the executor is
        /// reactive, so a fall into a chimney or a detour over a wall self-corrects.
        /// </summary>
        void RunRouteStep(ref PetInput input, float dt, in IdleContext ctx)
        {
            Player p = ctx.Player;
            NavStep step = route[routeAt];
            if (step.Seg >= navSegs.Count) { Abandon(ctx); return; }
            NavSeg seg = navSegs[step.Seg];
            if (p.onGround && p.Pos.Y <= seg.Y + 6f && p.Pos.Y >= seg.Y - 10f &&
                p.Pos.X >= seg.L - 3f && p.Pos.X <= seg.R + 3f)
            {
                routeAt++;
                wallSide = 0;
                stepPeakY = p.Pos.Y;
                stepFalls = 0;
                bestDist = float.MaxValue;
                bestClimbY = float.MaxValue;
                stall = 0f;
                return;
            }
            // Falling eighty pixels back from a step's high point, twice, means the
            // maneuver does not work here -- whatever the incremental watchdog thinks of
            // the pixel-scale records each retry sets on the way up.
            if (p.Pos.Y < stepPeakY) stepPeakY = p.Pos.Y;
            if (p.onGround && p.Pos.Y - stepPeakY > 80f)
            {
                stepPeakY = p.Pos.Y;
                if (++stepFalls >= 2)
                {
                    NoteFailedSpot(target);
                    Abandon(ctx);
                    return;
                }
            }
            float aim;
            bool scaling = false;
            switch (step.Move)
            {
                case IdleNav.MoveClimb:
                    aim = step.X;
                    scaling = true;
                    Walk(ref input, dt, ctx, aim, climbUpTo: float.MaxValue, scaleWithJumps: true);
                    break;
                case IdleNav.MoveDash:
                    aim = step.X;
                    scaling = true;
                    if (p.State == Player.StClimb ||
                        (wallSide != 0 && !p.onGround && p.Dashes == 0 &&
                         p.State != Player.StDash))
                    {
                        // Caught the face at least once: from here the ordinary climb
                        // machinery -- grab, tank, neutral-jump ladder -- owns it. The
                        // press-in below is only for the very first catch, or its
                        // ungated grab chatters against the tank rule forever.
                        Walk(ref input, dt, ctx, aim, climbUpTo: float.MaxValue,
                            scaleWithJumps: true);
                        break;
                    }
                    climbViaX = step.X;
                    climbViaDir = step.Dir;
                    RunDashUp(ref input, dt, ctx);
                    break;
                case IdleNav.MoveLeap:
                {
                    bool nearWall = Math.Abs(p.Pos.X - step.X) < 14f;
                    // Below the leap height the wall is the errand; only once she is up
                    // there -- flying or hanging past it -- is the segment the steer.
                    aim = nearWall ? step.X
                        : p.Pos.Y <= step.Arg ? Math.Clamp(p.Pos.X, seg.L + 6f, seg.R - 6f)
                        : step.X;
                    scaling = true;
                    if (nearWall && p.onGround && p.Pos.Y <= step.Arg)
                    {
                        // Standing on the assist wall itself, above the leap height: step
                        // off toward the target and take the face waiting in the chimney.
                        aim = step.X + step.Dir * 25f;
                    }
                    else if (nearWall && p.Pos.Y <= step.Arg)
                    {
                        // Off the face that looks at the target only; from the far face
                        // the same leap flies the wrong way, so there she climbs on and
                        // comes over the top instead.
                        if (p.State == Player.StClimb && p.Facing == -step.Dir)
                        { climbIntent = ClimbIntent.LeapAcross; intentT = .4f; }
                        else if (!p.onGround && wallSide == -step.Dir)
                            pendingLeap = true;
                    }
                    Walk(ref input, dt, ctx, aim, climbUpTo: float.MaxValue, scaleWithJumps: true);
                    break;
                }
                default:        // walk, hop, jump a gap, or walk off an end
                    aim = step.X;
                    legLedgeJump = true;
                    Walk(ref input, dt, ctx, aim, climbUpTo: 60f);
                    break;
            }
            input.DashPressed |= p.HasDashBuffer;
            Drain(dt, moving: true);
            Watchdog(dt, ctx, false, aim, busyScaling:
                scaling && (p.State == Player.StClimb || !p.onGround), overrideY: seg.Y);
        }

        /// <summary>
        /// The jump-and-up-dash that reaches a window hanging above the floor: walk under
        /// its wall, jump, dash straight up beside the face, and put the grab out. Only
        /// offered where a dash cannot move the window.
        /// </summary>
        void RunDashUp(ref PetInput input, float dt, in IdleContext ctx)
        {
            Player p = ctx.Player;
            if (p.onGround && (Math.Abs(p.Pos.X - climbViaX) > 3f || Math.Abs(p.Speed.X) > 30f))
            {
                // Settle at the spot first: jumping with leftover run speed drifts her
                // under the window during the rise, and the dash leaves from the wrong place.
                Walk(ref input, dt, ctx, climbViaX, climbUpTo: 0f);
                return;
            }
            if (p.onGround)
            {
                if (jumpHoldFrames == 0) { p.BufferJump(); jumpHoldFrames = 12; }
            }
            else if (dashAimFrames == 0 && p.State != Player.StDash &&
                !ctx.WindowsReactToDash && p.Speed.Y > -25f && p.Dashes > 0)
            {
                // The top of the jump, where the dash buys the most height.
                p.BufferDash();
                dashAimFrames = 8;
            }
            if (dashAimFrames > 0 || p.State == Player.StDash)
            {
                // Straight up, held across every frame the dash could read its aim from,
                // and no sideways drift until it is done. The dash aims from AimX/AimY --
                // the port's Input.GetAimVector -- not from MoveX/MoveY.
                if (dashAimFrames > 0) dashAimFrames--;
                input.MoveY = -1;
                input.AimY = -1;
                input.AimX = 0;
            }
            else if (!p.onGround && p.Speed.Y > -10f && p.Dashes == 0)
            {
                // Dash spent and past its rise: press into the face with the grab out.
                // A fresh catch is a fresh climb: the intent rerolls, or a stale one
                // chatters grab-and-release on the spot forever.
                prevClimbing = false;
                input.MoveX = climbViaDir;
                input.GrabHeld = true;
            }
            if (jumpHoldFrames > 0) { jumpHoldFrames--; input.JumpHeld = true; }
            input.JumpPressed = p.HasJumpBuffer;
            input.DashPressed = p.HasDashBuffer;
        }

        /// <summary>
        /// Up the side of the screen a little way, hold on, look at the desk from there,
        /// and drop off. The edge walls are real solids the shell builds, so this is the
        /// same grab and climb as any wall.
        /// </summary>
        void RunHang(ref PetInput input, float dt, in IdleContext ctx)
        {
            Player p = ctx.Player;
            if (phase == Phase.Telegraph)
            {
                FaceToward(ref input, ctx, target.X);
                if (phaseTime >= .4f + (float)rng.NextDouble() * .4f) { phase = Phase.Go; phaseTime = 0f; }
                return;
            }
            if (phase == Phase.Do)
            {
                // Let go was the exit; a beat on the ground, then done.
                Drain(dt, moving: false, resting: true);
                if (p.onGround && phaseTime > 1.5f) Finish(ctx);
                return;
            }
            if (p.State == Player.StClimb && p.Pos.Y <= targetRect.Top)
            {
                // High enough: hold still and hang, until it has been a moment or the
                // arms have had enough.
                hangFor += dt;
                input.GrabHeld = true;
                input.MoveX = p.Facing;
                if (hangFor > 1.5f + (float)rng.NextDouble() * 2f || p.Stamina < 12f)
                { phase = Phase.Do; phaseTime = 0f; }
                return;
            }
            Walk(ref input, dt, ctx, target.X, climbUpTo: float.MaxValue);
            Drain(dt, moving: true);
            Watchdog(dt, ctx, wantTop: true);
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
        /// reaches, and grab-climb walls up to climbUpTo. Her jump tops out near twenty-eight
        /// pixels (JumpSpeed -105, VarJumpTime 0.2, gravity 900 in the reference), which is
        /// what draws the lines below. With scaleWithJumps she also climbs past the tank:
        /// wall jumps cost no stamina, so at a nearly-dry tank she lets go and rides the
        /// neutral-jump ladder -- jump, drift back to the wall, jump again -- the way players
        /// scale walls taller than their stamina. Jumping straight out of the climb would be
        /// a ClimbJump and cost 27.5, which is why the grab is released first.
        /// </summary>
        void Walk(ref PetInput input, float dt, in IdleContext ctx, float targetX, float climbUpTo,
            bool scaleWithJumps = false)
        {
            Player p = ctx.Player;
            int dir = targetX > p.Pos.X + 2f ? 1 : targetX < p.Pos.X - 2f ? -1 : 0;

            if (p.State == Player.StClimb)
            {
                // On a wall she is a player with options, and she picks one: keep going up,
                // slide down a little, just hang, let go into the neutral-jump ladder, or
                // leap across to an adjacent wall. The roll leans toward progress, so the
                // outing still arrives; the rest is what being on a wall is for.
                if (!prevClimbing) { climbStartY = p.Pos.Y; intentT = 0f; }
                prevClimbing = true;
                wallSide = p.Facing;
                intentT -= dt;
                if (intentT <= 0f) RollClimbIntent(ctx, p, scaleWithJumps);
                if (p.Stamina < 30f && scaleWithJumps)
                {
                    // The tank decides for her: let go, and the ladder below is free.
                    neutralFrames = 0;
                    return;
                }
                if (p.Stamina < 25f) { jumpHoldFrames = 0; return; }
                switch (climbIntent)
                {
                    case ClimbIntent.NeutralHop:
                        if (!scaleWithJumps) goto default;
                        neutralFrames = 0;
                        return;                     // release; the airborne branch jumps
                    case ClimbIntent.LeapAcross:
                        if (!scaleWithJumps) goto default;
                        pendingLeap = true;
                        return;                     // release now, leap next frame
                    case ClimbIntent.Hang:
                        input.GrabHeld = true;
                        input.MoveX = p.Facing;
                        return;
                    case ClimbIntent.Down:
                        // Not all the way back down: a few pixels of second thoughts.
                        if (p.Pos.Y >= climbStartY - 6f)
                        { climbIntent = ClimbIntent.Up; goto default; }
                        input.GrabHeld = true;
                        input.MoveY = 1;
                        input.MoveX = p.Facing;
                        return;
                    default:
                        input.GrabHeld = true;
                        input.MoveY = -1;
                        input.MoveX = p.Facing;
                        return;
                }
            }
            prevClimbing = false;

            if (scaleWithJumps && !p.onGround && wallSide != 0)
            {
                if (pendingLeap)
                {
                    // The leap across: a wall jump with the away direction held throws her
                    // hard at the adjacent wall, and from here she is steered into it. The
                    // catch that follows may land on a nearly dry tank, so the grab gate
                    // opens for a moment.
                    pendingLeap = false;
                    wallSide = -wallSide;
                    p.BufferJump();
                    jumpHoldFrames = 10;
                    neutralFrames = 0;
                    leapCatchFrames = 40;
                }
                // Between wall jumps. The jump itself is taken neutral so it stays a plain
                // wall jump; then steer back into the wall, regrabbing if the tank allows,
                // and jump again the moment the wall is in reach and the rise has stopped.
                // Unless a second wall stands within a jump on the other side: then this is
                // a chimney, and the way up it is back and forth between the two.
                else if (jumpHoldFrames == 0 && neutralFrames == 0 && p.Speed.Y >= -10f &&
                    WallWithin(ctx, p, wallSide, 0f, 5f, out _) &&
                    HeadroomAbove(ctx, p, 30f))
                {
                    p.BufferJump();
                    jumpHoldFrames = 12;
                    // Hop to the far wall only if it keeps rising above her: a wall that
                    // ends at her height is a lip to go over, not a rally partner -- taking
                    // it anyway bounced her in the pocket beside her own destination.
                    if (WallWithin(ctx, p, -wallSide, 6f, 45f, out _) &&
                        WallContinuesAbove(ctx, p, -wallSide))
                    {
                        wallSide = -wallSide;
                        neutralFrames = 0;
                    }
                    else neutralFrames = 2;
                }
                if (leapCatchFrames > 0) leapCatchFrames--;
                if (neutralFrames > 0) { neutralFrames--; input.MoveX = 0; }
                else
                {
                    input.MoveX = wallSide;
                    // Below 35 the regrab is refused -- a climb at a nearly dry tank lets
                    // go the same frame it catches, and the chatter turns buffered ladder
                    // jumps into 27.5-stamina climb jumps. The exception is the moment
                    // after a leap, when the catch is the whole point.
                    input.GrabHeld = p.Stamina > 35f ||
                        (leapCatchFrames > 0 && p.Stamina > 5f);
                }
                if (jumpHoldFrames > 0) { jumpHoldFrames--; input.JumpHeld = true; }
                input.JumpPressed = p.HasJumpBuffer;
                return;
            }
            if (p.onGround) wallSide = 0;

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

            // A player never walks off a ledge they could jump off.
            if (legLedgeJump && p.onGround && jumpHoldFrames == 0 &&
                Math.Abs(p.Speed.X) > 60f && LedgeAhead(ctx, p, dir))
            { p.BufferJump(); jumpHoldFrames = 10; }

            if (WallAhead(ctx, p, dir, out float wallTop))
            {
                float rise = p.Pos.Y - wallTop;      // how far above her feet it stands
                if (rise <= 14f)
                {
                    // A step: hop it -- barely, for a seam a pixel or two tall.
                    if (p.onGround && jumpHoldFrames == 0)
                    { p.BufferJump(); jumpHoldFrames = rise <= 4f ? 2 : 8; }
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
                    if (scaleWithJumps) wallSide = dir;
                }
            }
            if (jumpHoldFrames > 0) { jumpHoldFrames--; input.JumpHeld = true; }
            input.JumpPressed = p.HasJumpBuffer;
        }

        /// <summary>
        /// What to do with the wall she is holding. Progress dominates; the leap is only
        /// offered when there really is a wall within reach on the other side.
        /// </summary>
        void RollClimbIntent(in IdleContext ctx, Player p, bool scaling)
        {
            double roll = rng.NextDouble();
            if (route != null && routeAt < route.Count)
            {
                // Mid-route the wall is an errand, not a pastime: no hanging about, no
                // sliding back, and above all no leaping off it on a whim.
                climbIntent = roll < .6 ? ClimbIntent.Up : ClimbIntent.NeutralHop;
                intentT = 1f + (float)rng.NextDouble();
                return;
            }
            if (!scaling)
            {
                climbIntent = roll < .8 ? ClimbIntent.Up
                    : roll < .95 ? ClimbIntent.Hang : ClimbIntent.Down;
            }
            else if (roll < .10 && WallWithin(ctx, p, -p.Facing, 6f, 55f, out _))
                climbIntent = ClimbIntent.LeapAcross;
            else if (roll < .50) climbIntent = ClimbIntent.Up;
            else if (roll < .75) climbIntent = ClimbIntent.NeutralHop;
            else if (roll < .88) climbIntent = ClimbIntent.Hang;
            else climbIntent = ClimbIntent.Down;
            intentT = climbIntent switch
            {
                ClimbIntent.Up => 1f + (float)rng.NextDouble() * 1.5f,
                ClimbIntent.Down => .4f + (float)rng.NextDouble() * .5f,
                ClimbIntent.Hang => .5f + (float)rng.NextDouble() * .7f,
                _ => .3f,
            };
        }

        static bool WallAhead(in IdleContext ctx, Player p, int dir, out float top)
            => WallWithin(ctx, p, dir, 5f, 8f, out top);

        /// <summary>Nothing solid within reach above her head: a jump has somewhere to go.</summary>
        static bool HeadroomAbove(in IdleContext ctx, Player p, float need)
        {
            foreach (Solid s in ctx.Solids)
            {
                // Her own head column only: the wall she is climbing stands flush at
                // four pixels off centre and must not read as a ceiling.
                if (s.R < p.Pos.X - 3.5f || s.L > p.Pos.X + 3.5f) continue;
                if (s.B > p.Pos.Y - 11f - need && s.T < p.Pos.Y - 11f) return false;
            }
            return true;
        }

        /// <summary>The wall on that side still stands thirty to forty pixels above her.</summary>
        static bool WallContinuesAbove(in IdleContext ctx, Player p, int dir)
        {
            float l = Math.Min(p.Pos.X + dir * 6f, p.Pos.X + dir * 45f);
            float r = Math.Max(p.Pos.X + dir * 6f, p.Pos.X + dir * 45f);
            foreach (Solid s in ctx.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.T <= p.Pos.Y - 40f && s.B >= p.Pos.Y - 30f) return true;
            }
            return false;
        }

        /// <summary>The floor stops just ahead: nothing to stand on within a stride.</summary>
        static bool LedgeAhead(in IdleContext ctx, Player p, int dir)
        {
            float l = Math.Min(p.Pos.X + dir * 6f, p.Pos.X + dir * 18f);
            float r = Math.Max(p.Pos.X + dir * 6f, p.Pos.X + dir * 18f);
            foreach (Solid s in ctx.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.T >= p.Pos.Y - 2f && s.T <= p.Pos.Y + 12f) return false;
            }
            return true;
        }

        static bool WallWithin(in IdleContext ctx, Player p, int dir, float from, float to,
            out float top)
        {
            // A strip at her knees and chest, some way ahead. The floor underfoot must not
            // count, but a monitor seam one pixel taller than her floor must -- she stalled
            // on exactly that -- so the cut is a quarter pixel above her feet, not a whole one.
            float l = Math.Min(p.Pos.X + dir * from, p.Pos.X + dir * to);
            float r = Math.Max(p.Pos.X + dir * from, p.Pos.X + dir * to);
            top = float.MaxValue;
            bool found = false;
            foreach (Solid s in ctx.Solids)
            {
                if (s.R <= l || s.L >= r) continue;
                if (s.B <= p.Pos.Y - 10f || s.T >= p.Pos.Y - .25f) continue;
                found = true;
                top = Math.Min(top, s.T);
            }
            return found;
        }

        void Watchdog(float dt, in IdleContext ctx, bool wantTop, float? overrideX = null,
            bool busyScaling = false, float? overrideY = null)
        {
            // Progress is measured toward the plan's current waypoint, and the yardstick
            // starts over when the waypoint changes: a route that legitimately walks away
            // from the final target -- out from under a window to its dash spot -- must not
            // be judged against a best set on a different leg of the trip.
            float aim = overrideX ?? target.X;
            if (Math.Abs(aim - watchdogAim) > 1.5f)
            {
                watchdogAim = aim;
                bestDist = float.MaxValue;
            }
            float dx = Math.Abs(aim - ctx.Player.Pos.X);
            float dy = wantTop ? Math.Abs(targetRect.Top - ctx.Player.Pos.Y)
                : overrideY.HasValue ? Math.Abs(overrideY.Value - ctx.Player.Pos.Y) : 0f;
            float dist = dx + dy;
            if (dist < bestDist - 1f) { bestDist = dist; stall = 0f; }
            // Scaling a seam is honest vertical work the horizontal distance cannot see --
            // but only while it keeps setting a new high-water mark. A ladder bouncing in a
            // pocket under an overhang gains nothing, and that is a stall like any other.
            else if (busyScaling && ctx.Player.Pos.Y < bestClimbY - 4f)
            { bestClimbY = ctx.Player.Pos.Y; stall = 0f; }
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

        /// <summary>
        /// A new stroll leg: a spot sampled from the terrain itself. A flat one is walked;
        /// an elevated one is routed with the same plans a window climb uses, because on
        /// this desk they are the same problem.
        /// </summary>
        void NewWanderLeg(in IdleContext ctx)
        {
            target = ExplorePoint(ctx, out RectangleF surface, out legElevated);
            if (legElevated) targetRect = surface;
            route = null;
            routeAt = 0;
            RollLegSpice(ctx);
            trappedLeg = walledLegs >= 2;
            bestDist = float.MaxValue;
            bestClimbY = float.MaxValue;
            crossingBudgeted = false;
            stall = 0f;
        }

        // ===== places =====

        internal PointF ProbeExploreForCheck(in IdleContext ctx, out RectangleF route,
            out bool elevated) => ExplorePoint(ctx, out route, out elevated);

        /// <summary>
        /// Somewhere to go, sampled from the terrain itself: the exposed top of any solid
        /// is a place. Window tops weigh more, a window that just appeared much more, and
        /// higher ground appeals in proportion to her energy -- but the floor is terrain
        /// too, so plain strolls remain in the lottery.
        /// </summary>
        PointF ExplorePoint(in IdleContext ctx, out RectangleF route, out bool elevated)
        {
            route = RectangleF.Empty;
            elevated = false;
            if (ctx.Monitors.Count > 1 && rng.NextDouble() < .25)
            {
                // A stroll to some other monitor stays on the ground; the seams and steps
                // on the way are the pilot's problem.
                RectangleF other = ctx.Monitors[rng.Next(ctx.Monitors.Count)];
                float ox = other.Left + 20f + (float)rng.NextDouble() * Math.Max(1f, other.Width - 40f);
                return new PointF(ox, ctx.Player.Pos.Y);
            }
            RectangleF room = RoomAround(ctx);
            float totalWeight = 0f;
            PointF pick = default;
            RectangleF pickRoute = default;
            bool pickUp = false, any = false;
            foreach (Solid s in ctx.Solids)
            {
                // On the screen, and not its very top edge -- standing above the monitor
                // is standing nowhere.
                if (s.T <= room.Top + 8f || s.T > room.Bottom) continue;
                float lo = Math.Max(s.L + 4f, room.Left + 10f);
                float hi = Math.Min(s.R - 4f, room.Right - 10f);
                if (hi - lo < 8f) continue;
                float x = lo + (float)rng.NextDouble() * (hi - lo);
                var spot = new PointF(x, s.T);
                if (NearAPuffer(ctx, spot)) continue;
                if (RecentlyFailed(spot)) continue;
                if (!Headroom(ctx, s, x)) continue;
                float weight = 1f;
                RectangleF routeRect = RectangleF.FromLTRB(s.L, s.T, s.R, s.B);
                foreach (var win in ctx.Windows)
                {
                    RectangleF wr = win.Value;
                    if (Math.Abs(s.T - wr.Top) < 3f && x > wr.Left && x < wr.Right)
                    {
                        // The spot is a window's top: route by the whole window, whose
                        // side walls are where the plans start.
                        routeRect = wr;
                        weight += 1.5f;
                        if (freshAge < 30f && Math.Abs(wr.Top - freshWindow.Top) < 3f &&
                            Math.Abs(wr.Left - freshWindow.Left) < 3f) weight += 3f;
                        break;
                    }
                }
                bool up = s.T < ctx.Player.Pos.Y - 16f;
                if (up) weight += energy;
                totalWeight += weight;
                if (rng.NextDouble() * totalWeight < weight)
                { pick = spot; pickRoute = routeRect; pickUp = up; any = true; }
            }
            if (!any) return WanderPoint(ctx);
            route = pickRoute;
            elevated = pickUp;
            return pick;
        }

        /// <summary>Nothing hangs low enough over this spot to keep her from standing there.</summary>
        static bool Headroom(in IdleContext ctx, Solid on, float x)
        {
            foreach (Solid s in ctx.Solids)
            {
                if (s.L > x + 5f || s.R < x - 5f) continue;
                if (s.B > on.T - 24f && s.B <= on.T && s.T < on.T - 1f) return false;
            }
            return true;
        }

        PointF WanderPoint(in IdleContext ctx)
        {
            RectangleF room = RoomAround(ctx);
            // The desk is all of it: about a third of the time, aim at some monitor rather
            // than this one, and walk the seam like any other floor.
            if (ctx.Monitors.Count > 1 && rng.NextDouble() < .3)
                room = ctx.Monitors[rng.Next(ctx.Monitors.Count)];
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
        readonly List<NavSeg> navSegs = new List<NavSeg>();
        List<NavStep> route;
        int routeAt;
        float routeNullFor;
        float driftTime;
        PointF driftAnchor;
        int driftStrikes;
        float stepPeakY;
        int stepFalls;
        readonly List<(PointF At, float Until)> failedSpots = new List<(PointF, float)>();

        void NoteFailedSpot(PointF at)
        {
            failedSpots.RemoveAll(f => f.Until < clock);
            if (failedSpots.Count >= 16) failedSpots.RemoveAt(0);
            failedSpots.Add((at, clock + 120f));
        }

        internal void NoteFailedSpotForCheck(PointF at) => NoteFailedSpot(at);

        /// <summary>A spot near one that recently defeated her.</summary>
        bool RecentlyFailed(PointF spot)
        {
            foreach ((PointF at, float until) in failedSpots)
                if (until > clock && Math.Abs(spot.X - at.X) < 60f &&
                    Math.Abs(spot.Y - at.Y) < 60f) return true;
            return false;
        }
        // Pilot scratch for the up-dash maneuver: where to leave the ground, and which
        // side the face is on.
        float climbViaX;
        int climbViaDir;

        // (the reach planning that lived here is IdleNav now: routes over the terrain
        // graph, found by search rather than per-window cases)

        /// <summary>
        /// A window she can actually get on top of from where she stands: its wall must come
        /// down to her ground, since a floating one's wall hangs out of reach. Height is no
        /// bar -- past the tank she rides the neutral-jump ladder.
        /// </summary>
        internal RectangleF ProbeClimbForCheck(in IdleContext ctx) => FindClimbable(ctx);

        RectangleF FindClimbable(in IdleContext ctx)
        {
            if (ctx.Windows.Count == 0) return RectangleF.Empty;
            IdleNav.BuildSegs(ctx, navSegs);
            int from = IdleNav.SegUnder(navSegs, ctx.Player.Pos);
            if (from < 0) return RectangleF.Empty;
            RectangleF room = RoomAround(ctx);
            int noTop = 0, noRoute = 0;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var window = ctx.Windows[rng.Next(ctx.Windows.Count)];
                RectangleF rect = window.Value;
                if (!room.IntersectsWith(rect)) continue;
                if (rect.Width < 24f) continue;
                if (ctx.Player.Pos.Y - rect.Top < 16f) continue;        // hardly a climb
                if (NearAPuffer(ctx, new PointF(rect.Left + rect.Width / 2f, rect.Top))) continue;
                if (RecentlyFailed(new PointF(rect.Left + rect.Width / 2f, rect.Top))) continue;
                int to = -1;
                for (int i = 0; i < navSegs.Count && to < 0; i++)
                    if (Math.Abs(navSegs[i].Y - rect.Top) <= 4f &&
                        navSegs[i].R > rect.Left && navSegs[i].L < rect.Right) to = i;
                if (to < 0) { noTop++; continue; }
                if (IdleNav.FindRoute(ctx, navSegs, from, to) == null) { noRoute++; continue; }
                return rect;
            }
            // The scan came up dry: say why, so the diary answers what a shrug cannot.
            if (noTop + noRoute > 0)
                PetWindow.Log($"idle: climb scan empty: no standable top {noTop},"
                    + $" no route {noRoute}");
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
            bestClimbY = float.MaxValue;
            crossingBudgeted = false;
            stall = 0f;
            carryStage = 0;
            hangFor = 0f;
            wallSide = 0;
            legHopX = float.NaN;
            legDashX = float.NaN;
            legLedgeJump = true;
            walledLegs = 0;
            trappedLeg = false;
            route = null;
            routeAt = 0;
            legElevated = what == Activity.Wander && rect.Width > 0f;
            pendingLeap = false;
            dashAimFrames = 0;
            leapCatchFrames = 0;
            sitStage = 0;
            sitT = 0f;
            sitPause = 1.5f;
            Napping = false;
        }
    }
}
