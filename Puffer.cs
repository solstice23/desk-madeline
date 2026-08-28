using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>
    /// Port of Celeste.Puffer, the pufferfish of Farewell: bounce off the top of it and it is
    /// knocked away; come at it from below or beside and it swells up and goes off, throwing
    /// whatever is near it sideways.
    /// </summary>
    /// <remarks>
    /// Unlike the bumper, this one does answer to something other than her. Puffer.Explode
    /// looks for a player and for a Theo crystal inside its forty-pixel blast and launches
    /// both, so a crystal left beside one is thrown by it here too. The jellyfish and the
    /// seeker it has nothing to say to, which is again the game: the rest of what Explode
    /// reaches for is cracked blocks, touch switches and floating debris, none of which the
    /// desktop has.
    ///
    /// Its three states are vanilla's. Idle wanders on a sine about wherever it was left; Hit
    /// is being knocked away, sliding off solids and slowing to a stop; Gone is the two and a
    /// half seconds after it bursts, the last half of which it spends curving back to where it
    /// started. Being dragged is the desktop's, and moves where it starts from with it -- so a
    /// puffer put somewhere comes back to that place rather than to where it was spawned.
    ///
    /// What is left out of the class, and why: HitSpring, GotoHitSpeed and the PufferCollider
    /// it answers are the spring family, and nothing on a desktop is a spring; Added sets a
    /// draw depth for maps outside the vanilla level set; and Explode's screen shake and its
    /// three displacement bursts want a Level to ripple. Everything else in the reference is
    /// here, including the parts it is easy to read past: it is never carried by anything it
    /// floats over, a solid that cannot push it clear sets it off, and a boop drops it back
    /// to the middle of its own wander before it is thrown.
    /// </remarks>
    public sealed class Puffer
    {
        const float RespawnTime = 2.5f, RespawnMoveTime = .5f;
        const float BounceSpeed = 200f, ExplodeRadius = 40f, DetectRadius = 32f;
        const float StunnedAccel = 320f, AlertedRadius = 60f, CantExplodeTime = .5f;
        /// <summary>Collider = Hitbox(12, 10, -6, -5): six either side, five above and below.</summary>
        public const float HalfWidth = 6f, HalfHeight = 5f;
        /// <summary>PlayerCollider = Hitbox(14, 12, -7, -7): a touch wider, and higher up.</summary>
        const float TouchHalfWidth = 7f, TouchTop = -7f, TouchBottom = 5f;

        public enum States { Idle, Hit, Gone }

        public PointF Pos;
        public States State { get; private set; } = States.Idle;
        public bool Removed { get; private set; }
        public bool BeingDragged { get; private set; }
        public string FrameId => animator.CurrentFrameId;
        /// <summary>Which way round it is drawn; a puffer faces the way it was made facing.</summary>
        public int Facing { get; private set; } = 1;
        /// <summary>Sprite.Scale, from the squash of a bounce and the swell of an alert.</summary>
        public PointF Scale => new PointF(
            scale.X * (1f + inflateWiggler * .4f), scale.Y * (1f + inflateWiggler * .4f));
        /// <summary>Sprite.Rotation, in radians: the wobble after something lands on it.</summary>
        public float Rotation => bounceWiggler * 20f * (float)(Math.PI / 180.0);
        /// <summary>Set on the frame it bursts, for the shell to throw the particles.</summary>
        public int Explosions { get; private set; }
        public readonly Queue<PlayerSoundEvent> SoundEvents = new Queue<PlayerSoundEvent>();

        PointF anchor, start, lastSine, lastSpeed, hitSpeed, counter;
        PointF scale = new PointF(1f, 1f);
        float sineCounter, goneTimer, cannotHitTimer, cantExplodeTimer, alertTimer;
        float playerAliveFade, eyeSpin, timeActive;
        PointF lastPlayerPos;
        float bounceWiggler, bounceWigglerTime, inflateWiggler, inflateWigglerTime;
        PointF returnControl;
        readonly Animator animator;

        static readonly Random random = new Random();

        public Puffer(PointF at, bool faceRight = true)
            : this(at, faceRight, (float)(random.NextDouble() * Math.PI * 4.0)) { }

        public Puffer(PointF at, bool faceRight, float sinePhase)
        {
            Facing = faceRight ? 1 : -1;
            sineCounter = sinePhase;
            anchor = at;
            Pos = new PointF(at.X + Sine * 3f, at.Y + SineOverTwo * 2f);
            start = lastSine = lastSpeed = Pos;
            animator = new Animator(BuildAnimations());
            animator.Play("idle", true);
        }

        float Sine => (float)Math.Sin(sineCounter);
        float SineOverTwo => (float)Math.Sin(sineCounter / 2f);

        /// <summary>Its own Sprites.xml entry: everything at 0.08 but the recovery.</summary>
        static Dictionary<string, Anim> BuildAnimations()
        {
            string[] Seq(string name, int count)
            {
                var frames = new string[count];
                for (int i = 0; i < count; i++) frames[i] = "puffer/" + name + i.ToString("00");
                return frames;
            }
            return new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new Anim { Frames = Seq("idle", 12), Delay = .08f, Loop = true },
                ["alert"] = new Anim { Frames = Seq("alert", 2), Delay = .08f, Goto = "alerted" },
                ["alerted"] = new Anim { Frames = Seq("alerted", 6), Delay = .08f, Loop = true },
                ["explode"] = new Anim { Frames = Seq("explode", 10), Delay = .08f, Goto = "hidden" },
                // The same two frames as the alert, the other way round.
                ["unalert"] = new Anim
                { Frames = new[] { "puffer/alert01", "puffer/alert00" }, Delay = .08f, Goto = "idle" },
                ["hidden"] = new Anim { Frames = Seq("hidden", 4), Delay = .08f, Loop = true },
                ["recover"] = new Anim { Frames = Seq("recover", 6), Delay = .05f, Goto = "idle" },
            };
        }

        public void Update(float dt, Player player, IList<Solid> solids, IList<TheoCrystal> theos,
            float worldBottom = float.PositiveInfinity)
        {
            Explosions = 0;
            timeActive += dt;
            eyeSpin = Approach(eyeSpin, 0f, dt * 1.5f);
            if (player != null && !player.IsDead)
            {
                playerAliveFade = Approach(playerAliveFade, 1f, dt);
                lastPlayerPos = player.Center;
            }
            else playerAliveFade = Approach(playerAliveFade, 0f, dt);
            if (cannotHitTimer > 0f) cannotHitTimer -= dt;
            // Held while it is gone: the half second it cannot go off for is meant to be
            // spent after it re-forms, not while it is away.
            if (State != States.Gone && cantExplodeTimer > 0f) cantExplodeTimer -= dt;
            if (alertTimer > 0f) alertTimer -= dt;
            Wigglers(dt);
            // Wiggler-driven squash settles back to round, as Puffer.Update's does.
            scale = new PointF(Approach(scale.X, 1f, dt), Approach(scale.Y, 1f, dt));
            sineCounter += (float)(Math.PI * 2.0) * .5f * dt;
            animator.Update(dt);

            if (BeingDragged) { lastSine = lastSpeed = anchor = start = Pos; return; }

            switch (State)
            {
                case States.Idle:
                    // Anything that moved it -- a drag, a shove -- moves what it wanders about.
                    if (Pos.X != lastSine.X || Pos.Y != lastSine.Y)
                        anchor = new PointF(anchor.X + Pos.X - lastSine.X, anchor.Y + Pos.Y - lastSine.Y);
                    // Against the exact position, counter and all, as Monocle's MoveToX
                    // measures against ExactPosition. Measuring against Pos alone feeds the
                    // fractional remainder back into the accumulator every frame, and the
                    // moment the sine's target sits near a half pixel that half-real error
                    // fires a step, flips sign, and fires the opposite one -- the whole fish
                    // vibrating a pixel at frame rate, which reads as flicker and puts the
                    // tail on and off the same column twice a step.
                    MoveH(anchor.X + Sine * 3f - (Pos.X + counter.X), solids);
                    MoveV(anchor.Y + SineOverTwo * 2f - (Pos.Y + counter.Y), solids);
                    lastSine = Pos;
                    if (ProximityExplodeCheck(player, solids)) { Explode(player, solids, theos); break; }
                    if (AlertedCheck(player)) Alert(false, true);
                    else if (animator.CurrentId == "alerted" && alertTimer <= 0f)
                    {
                        SoundEvents.Enqueue(new PlayerSoundEvent("event:/new_content/game/10_farewell/puffer_shrink"));
                        animator.Play("unalert");
                    }
                    Touch(player, solids, theos);
                    break;

                case States.Hit:
                    lastSpeed = Pos;
                    MoveH(hitSpeed.X * dt, solids);
                    MoveV(hitSpeed.Y * dt, solids);
                    anchor = Pos;
                    hitSpeed.X = Approach(hitSpeed.X, 0f, 150f * dt);
                    hitSpeed = TowardsZero(hitSpeed, StunnedAccel * dt);
                    if (ProximityExplodeCheck(player, solids)) { Explode(player, solids, theos); break; }
                    // Knocked out of the world. Vanilla measures five pixels past the bottom
                    // of the room; here that is the bottom of the monitors, which is the only
                    // edge a boop can drive it through -- and it is the one thing loose on the
                    // desktop that is not caught and put back, because it catches itself: it
                    // hides where it fell and swims home from where it started.
                    if (Pos.Y - HalfHeight >= worldBottom + 5f)
                    {
                        animator.Play("hidden", true);
                        GotoGone();
                        break;
                    }
                    Touch(player, solids, theos);
                    if (hitSpeed.X == 0f && hitSpeed.Y == 0f)
                    {
                        counter = PointF.Empty;
                        GotoIdle();
                    }
                    break;

                case States.Gone:
                    float was = goneTimer;
                    goneTimer -= dt;
                    if (goneTimer <= RespawnMoveTime)
                    {
                        if (was > RespawnMoveTime && Distance(start, Pos) > 8f)
                            SoundEvents.Enqueue(new PlayerSoundEvent("event:/new_content/game/10_farewell/puffer_return"));
                        float t = CubeInOut(Math.Clamp(1f - goneTimer / RespawnMoveTime, 0f, 1f));
                        Pos = Curve(t);
                    }
                    if (goneTimer <= 0f) GotoIdle();
                    break;
            }
        }

        /// <summary>Puffer.OnPlayer: landed on from above is a bounce, anything else is a burst.</summary>
        void Touch(Player player, IList<Solid> solids, IList<TheoCrystal> theos)
        {
            if (player == null || player.IsDead || player.IsRespawning) return;
            if (State == States.Gone || cantExplodeTimer > 0f) return;
            if (!player.OverlapsBox(Pos.X - TouchHalfWidth, Pos.Y + TouchTop,
                Pos.X + TouchHalfWidth, Pos.Y + TouchBottom)) return;

            // The tenth of a second is refreshed by the touch and not by what came of it, so
            // standing on one holds it open: she has to come off it before it can answer her
            // again. Running the clock down under her foot would let it boop her twice.
            if (cannotHitTimer <= 0f)
            {
                // Vanilla measures her feet against where it was before the sine moved it this
                // frame, so that its own wander cannot decide which of the two happens.
                if (player.Pos.Y > lastSpeed.Y + 3f) { Explode(player, solids, theos); }
                else
                {
                    player.Bounce(Pos.Y - HalfHeight);
                    GotoHit();
                    // Player.OnPlayer's tail: it stops wandering where it stands. Back to the
                    // middle of its bob, the sine begun again, and that spot is home for the
                    // wander that follows -- without which it slides sideways as it is booped.
                    MoveToAnchorX();
                    sineCounter = 0f;
                    anchor = lastSine = Pos;
                }
            }
            cannotHitTimer = .1f;
        }

        /// <summary>MoveToX(anchorPosition.X): back to the middle of its own wander.</summary>
        void MoveToAnchorX()
        {
            counter.X = 0f;
            Pos.X = (float)Math.Round(anchor.X, MidpointRounding.ToEven);
        }

        bool ProximityExplodeCheck(Player player, IList<Solid> solids)
        {
            if (cantExplodeTimer > 0f || player == null || player.IsDead || player.IsRespawning)
                return false;
            if (Distance(Pos, player.Center) >= DetectRadius) return false;
            // Only from below: her middle has to be at or under its own, near enough.
            if (player.Center.Y < Pos.Y + HalfHeight - 4f) return false;
            return !Blocked(Pos, player.Center, solids);
        }

        bool AlertedCheck(Player player)
            => player != null && !player.IsDead && Distance(Pos, player.Center) < AlertedRadius;

        void Alert(bool restart, bool sound)
        {
            if (animator.CurrentId == "idle")
            {
                if (sound) SoundEvents.Enqueue(new PlayerSoundEvent("event:/new_content/game/10_farewell/puffer_expand"));
                animator.Play("alert");
                inflateWigglerTime = .6f;
            }
            else if (restart && sound)
                SoundEvents.Enqueue(new PlayerSoundEvent("event:/new_content/game/10_farewell/puffer_expand"));
            alertTimer = 2f;
        }

        /// <summary>
        /// Puffer.Explode: everything inside forty pixels that it has anything to say to, which
        /// is her and a crystal, is thrown away from it. Line of sight both times -- a puffer
        /// behind a wall does not reach through it.
        /// </summary>
        void Explode(Player player, IList<Solid> solids, IList<TheoCrystal> theos)
        {
            SoundEvents.Enqueue(new PlayerSoundEvent("event:/new_content/game/10_farewell/puffer_splode"));
            animator.Play("explode", true);
            if (player != null && !player.IsDead && !player.IsRespawning &&
                Distance(Pos, player.Center) < ExplodeRadius && !Blocked(Pos, player.Center, solids))
                // sidesOnly: a puffer throws her along the ground rather than over itself.
                player.ExplodeLaunch(Pos, snapUp: false, sidesOnly: true);
            if (theos != null)
                foreach (TheoCrystal theo in theos)
                {
                    if (theo.Removed || theo.IsHeld) continue;
                    PointF centre = new PointF(theo.Pos.X, theo.Pos.Y - TheoCrystal.ColliderHeight / 2f);
                    if (Distance(Pos, centre) >= ExplodeRadius || Blocked(Pos, centre, solids)) continue;
                    theo.ExplodeLaunch(Pos);
                    break;      // CollideFirst: one crystal, the first it finds
                }
            Explosions++;
            GotoGone();
        }

        void GotoIdle()
        {
            if (State == States.Gone)
            {
                Pos = start;
                cantExplodeTimer = CantExplodeTime;
                animator.Play("recover", true);
                SoundEvents.Enqueue(new PlayerSoundEvent("event:/new_content/game/10_farewell/puffer_reform"));
            }
            lastSine = lastSpeed = anchor = Pos;
            hitSpeed = PointF.Empty;
            sineCounter = 0f;
            State = States.Idle;
        }

        void GotoHit()
        {
            scale = new PointF(1.2f, .8f);
            hitSpeed = new PointF(0f, BounceSpeed);
            State = States.Hit;
            bounceWigglerTime = .6f;
            eyeSpin = 1f;
            Alert(true, false);
            SoundEvents.Enqueue(new PlayerSoundEvent("event:/new_content/game/10_farewell/puffer_boop"));
        }

        /// <summary>
        /// Gone, and the curve it comes back along: bowed out to one side so that it swims back
        /// rather than sliding.
        /// </summary>
        void GotoGone()
        {
            returnControl = new PointF(Pos.X + (start.X - Pos.X) * .5f, Pos.Y + (start.Y - Pos.Y) * .5f);
            float dx = start.X - Pos.X, dy = start.Y - Pos.Y;
            if (dx * dx + dy * dy > 100f)
            {
                if (Math.Abs(dy) > Math.Abs(dx))
                    returnControl.X += Pos.X > start.X ? -24f : 24f;
                else
                    returnControl.Y += Pos.Y > start.Y ? -24f : 24f;
            }
            goneFrom = Pos;
            goneTimer = RespawnTime;
            State = States.Gone;
        }

        /// <summary>SimpleCurve.GetPoint: the quadratic through begin, control and end.</summary>
        PointF Curve(float t)
        {
            float a = 1f - t;
            return new PointF(
                a * a * goneFrom.X + 2f * a * t * returnControl.X + t * t * start.X,
                a * a * goneFrom.Y + 2f * a * t * returnControl.Y + t * t * start.Y);
        }

        PointF goneFrom;

        // ===== movement, as Actor.MoveH/MoveV with the puffer's own collisions =====
        void MoveH(float amount, IList<Solid> solids)
        {
            counter.X += amount;
            int move = (int)Math.Round(counter.X, MidpointRounding.ToEven);
            if (move == 0) return;
            counter.X -= move;
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (Inside(Pos.X + sign, Pos.Y, solids))
                {
                    counter.X = 0f;
                    hitSpeed.X *= -.8f;      // OnCollideH
                    return;
                }
                Pos.X += sign;
                move -= sign;
            }
        }

        void MoveV(float amount, IList<Solid> solids)
        {
            counter.Y += amount;
            int move = (int)Math.Round(counter.Y, MidpointRounding.ToEven);
            if (move == 0) return;
            counter.Y -= move;
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (Inside(Pos.X, Pos.Y + sign, solids))
                {
                    counter.Y = 0f;
                    if (sign > 0)
                    {
                        // OnCollideV: it tries to slip off the edge of what it landed on before
                        // it settles for bouncing, which is what keeps one from sitting on a
                        // corner it could roll off.
                        for (int side = -1; side <= 1; side += 2)
                            for (int step = 1; step <= 2; step++)
                            {
                                float x = Pos.X + step * side;
                                if (Inside(x, Pos.Y, solids) || Inside(x, Pos.Y + 1f, solids)) continue;
                                Pos.X = x;
                                return;
                            }
                        hitSpeed.Y *= -.2f;
                    }
                    return;
                }
                Pos.Y += sign;
                move -= sign;
            }
        }

        bool Inside(float x, float y, IList<Solid> solids)
        {
            if (solids == null) return false;
            float l = x - HalfWidth, r = x + HalfWidth, t = y - HalfHeight, b = y + HalfHeight;
            foreach (Solid s in solids)
                if (l < s.R && r > s.L && t < s.B && b > s.T) return true;
            return false;
        }

        // ===== dragging, which is the desktop's =====
        // ===== Puffer.Render, which is a good deal more than the sprite =====

        /// <summary>
        /// Whether the sprite is drawn with the black outline around it. Everything but the
        /// three states where it is not really there gets one: the smoke of an explosion after
        /// its first two frames, and the last of a recovery, are the fish and so are outlined.
        /// </summary>
        public bool Outlined
        {
            get
            {
                string id = animator.CurrentId;
                if (id != "hidden" && id != "explode" && id != "recover") return true;
                if (id == "explode") return animator.Frame <= 1;
                return id == "recover" && animator.Frame >= 4;
            }
        }

        /// <summary>How strongly the arc it watches her along is showing, from nothing to one.</summary>
        public float AggroFade => State == States.Gone ? 0f
            : playerAliveFade * ClampedMap(Distance(Pos, lastPlayerPos), 128f, 96f);

        /// <summary>
        /// The twenty-eight marks it draws in an arc over itself, brightest where it is looking.
        /// </summary>
        /// <remarks>
        /// Straight out of Render, jitter and all: the arc runs from just below level on one
        /// side to just past level on the other, shifted by the wobble of a bounce and drifting
        /// slowly on a sine; each mark fades in as its angle comes within a right angle of the
        /// way she is, and the two on the ends are always lines. Nothing here is decoration
        /// this port invented -- it is what tells the player which way a puffer is watching.
        /// </remarks>
        /// <returns>How many of the arrays were filled; up to twenty-eight.</returns>
        public int AggroArc(PointF[] at, PointF[] inward, float[] alpha)
        {
            float fade = AggroFade;
            if (fade <= 0f) return 0;
            // She above it is reflected below, so that the arc still bends towards her.
            PointF towards = lastPlayerPos;
            bool mirrored = false;
            if (towards.Y < Pos.Y)
            {
                towards = new PointF(towards.X + towards.X - Pos.X,
                    Pos.Y - (towards.Y - Pos.Y) * .5f);
                mirrored = true;
            }
            float at_her = (float)Math.Atan2(towards.Y - Pos.Y, towards.X - Pos.X);
            int count = 0;
            for (int i = 0; i < 28 && count < at.Length; i++)
            {
                float drift = (float)Math.Sin(timeActive * .5f) * .02f;
                float angle = Map(i / 28f + drift, -(float)(Math.PI / 30.0), 3.2463126f)
                    + bounceWiggler * 20f * (float)(Math.PI / 180.0);
                var along = new PointF((float)Math.Cos(angle), (float)Math.Sin(angle));
                var mark = new PointF(Pos.X + along.X * 32f, Pos.Y + along.Y * 32f);
                float t = ClampedMap(AbsAngleDiff(angle, at_her), (float)(Math.PI / 2.0), .17453292f);
                t = CubeOut(t) * .8f * fade;
                if (t <= 0f) continue;

                if (i == 0 || i == 27)
                {
                    at[count] = mark;
                    inward[count] = new PointF(along.X * 10f, along.Y * 10f);
                    alpha[count++] = t;
                    continue;
                }
                float wobble = (float)Math.Sin(timeActive * 2f + i * .6f);
                if (i % 2 == 0) wobble = -wobble;
                mark = new PointF(mark.X + along.X * wobble, mark.Y + along.Y * wobble);
                at[count] = mark;
                inward[count] = !mirrored && AbsAngleDiff(angle, at_her) <= .17453292f
                    ? new PointF(along.X * 3f, along.Y * 3f) : PointF.Empty;
                alpha[count++] = t;
            }
            return count;
        }

        /// <summary>The one black pixel of an eye, which it only has while it is puffed up.</summary>
        public bool HasEye => animator.CurrentId == "alerted";

        /// <summary>Where that pixel goes: looking at her, and spinning if it was just booped.</summary>
        public PointF Eye
        {
            get
            {
                PointF sprite = Scale;
                var from = new PointF(Pos.X + 3f * sprite.X * Facing,
                    Pos.Y + (Facing < 0 ? -5f : -4f) * sprite.Y);
                var to = new PointF(lastPlayerPos.X, lastPlayerPos.Y - 4f);
                float angle = (float)Math.Atan2(to.Y - from.Y, to.X - from.X)
                    + eyeSpin * (float)(Math.PI * 2.0) * 2f;
                float dx = (float)Math.Cos(angle), dy = (float)Math.Sin(angle);
                // Vanilla rounds the step and adds it, rather than rounding where it lands:
                // from a fractional position the two differ by a pixel, and the eye is one.
                return new PointF(from.X + (float)Math.Round(dx),
                    from.Y + (float)Math.Round(Map(dy, -1f, 2f, -1f, 1f)));
            }
        }

        static float Map(float value, float min, float max) => min + (max - min) * value;

        /// <summary>Calc.Map from one range to another; the ends may be the wrong way round.</summary>
        static float Map(float value, float outMin, float outMax, float inMin, float inMax)
            => outMin + (outMax - outMin) * ((value - inMin) / (inMax - inMin));

        static float ClampedMap(float value, float from, float to)
            => Math.Clamp((value - from) / (to - from), 0f, 1f);

        static float AbsAngleDiff(float a, float b)
        {
            float diff = (a - b) % (float)(Math.PI * 2.0);
            if (diff < 0f) diff += (float)(Math.PI * 2.0);
            if (diff > Math.PI) diff = (float)(Math.PI * 2.0) - diff;
            return diff;
        }

        static float CubeOut(float t) { float f = t - 1f; return 1f + f * f * f; }

        public void BeginDrag()
        {
            BeingDragged = true;
            counter = PointF.Empty;
            if (State == States.Gone) { State = States.Idle; animator.Play("idle", true); }
            hitSpeed = PointF.Empty;
        }

        public void DragTo(PointF to) => Pos = to;

        public void EndDrag()
        {
            BeingDragged = false;
            lastSine = lastSpeed = anchor = start = Pos;
            State = States.Idle;
        }

        public void Remove() => Removed = true;

        /// <summary>
        /// Puffer.OnSquish: a solid that cannot push it clear sets it off where it stands and
        /// it is gone -- which, being a puffer, is two and a half seconds and a swim home.
        /// </summary>
        /// <remarks>
        /// Vanilla wiggles three pixels for somewhere to go first, and ActorSweep has already
        /// done that by the time this is called. It is spared nothing: the crystal has the
        /// invincibility assist behind it and the jellyfish is spared here for being somebody's
        /// pet, but a puffer goes off and comes back on its own, so there is nothing to spare
        /// it from.
        /// </remarks>
        public void Squish(Player player, IList<Solid> solids, IList<TheoCrystal> theos)
        {
            if (State == States.Gone) return;
            Explode(player, solids, theos);   // which goes on to GotoGone, as vanilla's does
        }

        void Wigglers(float dt)
        {
            // Wiggler: a decaying cosine over its duration, at the frequency it was made with.
            if (bounceWigglerTime > 0f)
            {
                bounceWigglerTime = Math.Max(0f, bounceWigglerTime - dt);
                float t = bounceWigglerTime / .6f;
                bounceWiggler = (float)Math.Cos(bounceWigglerTime * 2.5f * Math.PI * 2.0) * t;
            }
            else bounceWiggler = 0f;
            if (inflateWigglerTime > 0f)
            {
                inflateWigglerTime = Math.Max(0f, inflateWigglerTime - dt);
                float t = inflateWigglerTime / .6f;
                inflateWiggler = (float)Math.Cos(inflateWigglerTime * 2f * Math.PI * 2.0) * t;
            }
            else inflateWiggler = 0f;
        }

        /// <summary>Calc.Approach for a vector: shorten it, rather than each part of it.</summary>
        static PointF TowardsZero(PointF value, float amount)
        {
            float length = (float)Math.Sqrt(value.X * value.X + value.Y * value.Y);
            if (length <= amount) return PointF.Empty;
            float keep = (length - amount) / length;
            return new PointF(value.X * keep, value.Y * keep);
        }

        static float Approach(float value, float target, float amount)
            => value > target ? Math.Max(target, value - amount) : Math.Min(target, value + amount);

        static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        static float CubeInOut(float t)
            => t < .5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3f) / 2f;

        /// <summary>Whether a wall stands between the two, so that a burst cannot reach through.</summary>
        static bool Blocked(PointF from, PointF to, IList<Solid> solids)
        {
            if (solids == null) return false;
            foreach (Solid s in solids)
            {
                float dx = to.X - from.X, dy = to.Y - from.Y, t0 = 0f, t1 = 1f;
                if (Clip(-dx, from.X - s.L, ref t0, ref t1) && Clip(dx, s.R - from.X, ref t0, ref t1) &&
                    Clip(-dy, from.Y - s.T, ref t0, ref t1) && Clip(dy, s.B - from.Y, ref t0, ref t1))
                    return true;
            }
            return false;
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
            else { if (r < t0) return false; if (r < t1) t1 = r; }
            return true;
        }
    }
}
