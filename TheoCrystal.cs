using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    public readonly struct TheoImpactEvent
    {
        public readonly PointF Position;
        public readonly float Direction, RangeX, RangeY;
        public TheoImpactEvent(PointF position, float direction, float rangeX, float rangeY)
        { Position = position; Direction = direction; RangeX = rangeX; RangeY = rangeY; }
    }

    /// <summary>Port of Celeste.TheoCrystal's holdable and actor physics.</summary>
    public sealed class TheoCrystal : IPetHoldable
    {
        const float Width = 8f, Height = 10f;
        /// <summary>Collider reach from Pos, for the desktop shell's bounds handling.</summary>
        public const float HalfWidth = Width / 2f, ColliderHeight = Height;
        public PointF Pos { get; private set; }
        public PointF Speed { get; private set; }
        public Player Holder { get; private set; }
        public bool IsHeld => Holder != null;
        public bool BeingDragged { get; private set; }
        public bool SlowRun => true;
        public bool SlowFall => false;
        public bool Removed { get; private set; }
        public string FrameId => "theoCrystal/idle00";
        /// <summary>0 while whole; runs to 1 over DeathEffect.Duration once he breaks.</summary>
        public float DeathPercent { get; private set; }
        public bool IsDying => DeathPercent > 0f;
        /// <summary>Where the death burst is centred: Collider.Center, as TheoCrystal.Die passes it.</summary>
        public PointF DeathPosition => new PointF(Pos.X, Pos.Y - Height / 2f);
        public readonly Queue<PlayerSoundEvent> SoundEvents = new Queue<PlayerSoundEvent>();
        public readonly Queue<TheoImpactEvent> ImpactEvents = new Queue<TheoImpactEvent>();

        const float DeathEffectDuration = 0.834f;   // DeathEffect.Duration

        float noGravityTimer, holdGravityTimer, cannotHoldTimer, hardVerticalHitSoundCooldown, swatTimer;
        float deathTimer;
        bool dead;
        PointF counter;
        IList<Solid> lastSolids;
        Seeker hitSeeker;

        public TheoCrystal(PointF position) => Pos = position;

        static float Approach(float value, float target, float amount)
            => value > target ? Math.Max(target, value - amount) : Math.Min(target, value + amount);
        static bool Overlap(float l, float t, float r, float b, in Solid solid)
            => l < solid.R && r > solid.L && t < solid.B && b > solid.T;
        static PointF Normalize(float x, float y, float length)
        {
            float d = (float)Math.Sqrt(x * x + y * y);
            return d <= .00001f ? PointF.Empty : new PointF(x / d * length, y / d * length);
        }
        static void Bounds(float x, float y, out float l, out float t, out float r, out float b)
        { l = x - 4f; r = x + 4f; t = y - 10f; b = y; }

        bool CollidesAt(float x, float y, IList<Solid> solids)
        {
            Bounds(x, y, out float l, out float t, out float r, out float b);
            foreach (Solid solid in solids) if (Overlap(l, t, r, b, solid)) return true;
            return false;
        }
        /// <summary>
        /// Whether the move is blocked, and whether by something he is already sitting in.
        /// </summary>
        /// <remarks>
        /// A window border only collides from the outside, so one opening around him does not
        /// swallow him.  A DreamBlock is a plain Solid to everything but a dashing player --
        /// Celeste has no exemption for a crystal inside one, and DreamBlock.BlockedCheck
        /// treats him as an actor it is blocked by -- so it holds him where he lies, the way
        /// it holds her.  A drag is the way out.  Being held is not an impact, and reporting
        /// one would sound the crystal against the block on every frame he spends in it.
        /// </remarks>
        bool BlocksMove(float x, float y, IList<Solid> solids, out bool held)
        {
            Bounds(Pos.X, Pos.Y, out float l0, out float t0, out float r0, out float b0);
            Bounds(x, y, out float l, out float t, out float r, out float b);
            held = false;
            foreach (Solid solid in solids)
            {
                if (!Overlap(l, t, r, b, solid)) continue;
                bool inside = Overlap(l0, t0, r0, b0, solid);
                if (solid.Dream || !inside) { held = inside; return true; }
            }
            return false;
        }

        public bool CanPickup(Player player)
        {
            if (Removed || IsHeld || BeingDragged || cannotHoldTimer > 0f) return false;
            // TheoCrystal: Holdable.PickupCollider = 16x22 at (-8,-16).
            float ph = player.Ducking ? 6f : 11f;
            return player.Pos.X - 4f < Pos.X + 8f && player.Pos.X + 4f > Pos.X - 8f &&
                   player.Pos.Y - ph < Pos.Y + 6f && player.Pos.Y > Pos.Y - 16f;
        }

        public bool Pickup(Player player)
        {
            if (!CanPickup(player)) return false;
            Holder = player;
            Speed = PointF.Empty;
            return true;
        }

        public void Carry(PointF position) { Pos = position; counter = PointF.Empty; }

        public void Release(PointF force, IList<Solid> solids = null)
        {
            solids ??= lastSolids;
            if (solids != null && CollidesAt(Pos.X, Pos.Y, solids))
            {
                if (force.X != 0f)
                {
                    int direction = Math.Sign(force.X);
                    for (int distance = 1; distance <= 10; distance++)
                        if (!CollidesAt(Pos.X + direction * distance, Pos.Y, solids))
                        { Pos = new PointF(Pos.X + direction * distance, Pos.Y); break; }
                }
                while (CollidesAt(Pos.X, Pos.Y, solids)) Pos = new PointF(Pos.X, Pos.Y + 1f);
            }
            Holder = null;
            holdGravityTimer = .1f;
            cannotHoldTimer = .1f;
            if (force.X != 0f && force.Y == 0f) force.Y = -.4f;
            Speed = new PointF(force.X * 200f, force.Y * 200f);
            if (!Speed.IsEmpty) noGravityTimer = .1f;
        }

        public void BeginDrag(Player player)
        {
            if (Holder == player) player.ReleaseHoldableForDrag(this);
            BeingDragged = true; Holder = null; Speed = counter = PointF.Empty;
        }
        public void DragTo(PointF position) { if (BeingDragged) { Pos = position; counter = PointF.Empty; } }
        /// <summary>Desktop: put him back on the displays after a drag left him off them.</summary>
        public void SnapIntoView(PointF position) { Pos = position; counter = PointF.Empty; }
        public void EndDrag(PointF velocity)
        {
            if (!BeingDragged) return;
            BeingDragged = false; cannotHoldTimer = .1f; holdGravityTimer = .1f;
            Speed = velocity;
            if (!Speed.IsEmpty) noGravityTimer = .1f;
        }

        public void Update(float dt, Player player, IList<Solid> solids, RectangleF worldBounds)
        {
            lastSolids = solids;
            if (cannotHoldTimer > 0f) cannotHoldTimer -= dt;
            if (holdGravityTimer > 0f) holdGravityTimer -= dt;
            if (hardVerticalHitSoundCooldown > 0f) hardVerticalHitSoundCooldown -= dt;
            if (swatTimer > 0f) swatTimer -= dt;
            if (hitSeeker != null && swatTimer <= 0f && !OverlapsSeeker(hitSeeker)) hitSeeker = null;
            if (dead)
            {
                // TheoCrystal.Die hides the sprite and leaves a DeathEffect to play out where
                // he broke.  He is gone once it has, there being no room to reload here.
                deathTimer += dt;
                DeathPercent = Math.Min(1f, deathTimer / DeathEffectDuration);
                if (DeathPercent >= 1f) Removed = true;
                return;
            }
            if (Removed || BeingDragged || IsHeld) return;

            bool onGround = !CollidesAt(Pos.X, Pos.Y, solids) && CollidesAt(Pos.X, Pos.Y + 1f, solids);
            if (onGround)
            {
                float target = !CollidesAt(Pos.X + 3f, Pos.Y + 1f, solids) ? 20f :
                    CollidesAt(Pos.X - 3f, Pos.Y + 1f, solids) ? 0f : -20f;
                Speed = new PointF(Approach(Speed.X, target, 800f * dt), Speed.Y);
            }
            else if (holdGravityTimer <= 0f)
            {
                float gravity = Math.Abs(Speed.Y) <= 30f ? 400f : 800f;
                float friction = Speed.Y < 0f ? 175f : 350f;
                Speed = new PointF(Approach(Speed.X, 0f, friction * dt), Speed.Y);
                if (noGravityTimer > 0f) noGravityTimer -= dt;
                else Speed = new PointF(Speed.X, Approach(Speed.Y, 200f, gravity * dt));
            }
            MoveH(Speed.X * dt, solids);
            MoveV(Speed.Y * dt, solids);

            if (Pos.X - 4f < worldBounds.Left)
            { Pos = new PointF(worldBounds.Left + 4f, Pos.Y); Speed = new PointF(Speed.X * -.4f, Speed.Y); }
            else if (Pos.X + 4f > worldBounds.Right)
            { Pos = new PointF(worldBounds.Right - 4f, Pos.Y); Speed = new PointF(Speed.X * -.4f, Speed.Y); }
            if (Pos.Y - 10f < worldBounds.Top - 4f)
            { Pos = new PointF(Pos.X, worldBounds.Top + 14f); Speed = new PointF(Speed.X, 0f); }
            else if (Pos.Y - 10f > worldBounds.Bottom)
            {
                if (player.Invincible)
                {
                    Pos = new PointF(Pos.X, worldBounds.Bottom);
                    Speed = new PointF(Speed.X, -300f);
                    SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/general/assist_screenbottom"));
                }
                else Die(player);
            }
        }

        /// <summary>TheoCrystal.Die: he takes the player with him and breaks where he lies.</summary>
        void Die(Player player)
        {
            if (dead) return;
            dead = true;
            deathTimer = 0f;
            Speed = PointF.Empty;
            player.Die(new PointF(-player.Facing, 0f));
            SoundEvents.Enqueue(new PlayerSoundEvent("event:/char/madeline/death"));
        }

        public bool DangerousTo(Seeker seeker) => !IsHeld && !BeingDragged &&
            (Speed.X != 0f || Speed.Y != 0f) && hitSeeker != seeker;
        public void HitBySeeker(Seeker seeker)
        {
            if (!IsHeld) Speed = Normalize(Pos.X - seeker.Pos.X, Pos.Y - 5f - seeker.Pos.Y, 120f);
            SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/05_mirror_temple/crystaltheo_hit_side"));
        }
        public bool SwatBy(Seeker seeker, int direction)
        {
            if (!IsHeld || hitSeeker != null) return false;
            swatTimer = .1f; hitSeeker = seeker;
            Holder.SwatHoldable(direction);
            return true;
        }

        bool OverlapsSeeker(Seeker seeker)
            => Pos.X - 4f < seeker.Pos.X + 6f && Pos.X + 4f > seeker.Pos.X - 6f &&
               Pos.Y - 10f < seeker.Pos.Y + 6f && Pos.Y > seeker.Pos.Y - 2f;

        void MoveH(float amount, IList<Solid> solids)
        {
            counter.X += amount; int move = (int)Math.Round(counter.X, MidpointRounding.ToEven); counter.X -= move;
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (BlocksMove(Pos.X + sign, Pos.Y, solids, out bool held))
                {
                    counter.X = 0f;
                    if (held) Speed = new PointF(0f, Speed.Y);   // held, not hit
                    else OnCollideH(sign);
                    return;
                }
                Pos = new PointF(Pos.X + sign, Pos.Y); move -= sign;
            }
        }
        void MoveV(float amount, IList<Solid> solids)
        {
            counter.Y += amount; int move = (int)Math.Round(counter.Y, MidpointRounding.ToEven); counter.Y -= move;
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (BlocksMove(Pos.X, Pos.Y + sign, solids, out bool held))
                {
                    counter.Y = 0f;
                    if (held) Speed = new PointF(Speed.X, 0f);   // held, not hit
                    else OnCollideV(sign);
                    return;
                }
                Pos = new PointF(Pos.X, Pos.Y + sign); move -= sign;
            }
        }
        void OnCollideH(int direction)
        {
            SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/05_mirror_temple/crystaltheo_hit_side"));
            if (Math.Abs(Speed.X) > 100f)
                ImpactEvents.Enqueue(new TheoImpactEvent(new PointF(Pos.X + direction * 4f, Pos.Y - 4f),
                    direction > 0 ? (float)Math.PI : 0f, 0f, 6f));
            Speed = new PointF(Speed.X * -.4f, Speed.Y);
        }
        void OnCollideV(int direction)
        {
            if (Speed.Y > 0f)
            {
                float parameter = hardVerticalHitSoundCooldown <= 0f ? Math.Min(1f, Speed.Y / 200f) : 0f;
                SoundEvents.Enqueue(new PlayerSoundEvent(
                    "event:/game/05_mirror_temple/crystaltheo_hit_ground", "crystal_velocity", parameter));
                if (hardVerticalHitSoundCooldown <= 0f) hardVerticalHitSoundCooldown = .5f;
            }
            if (Speed.Y > 160f)
                ImpactEvents.Enqueue(new TheoImpactEvent(new PointF(Pos.X, Pos.Y),
                    -(float)Math.PI / 2f, 6f, 0f));
            Speed = new PointF(Speed.X, Speed.Y > 140f ? Speed.Y * -.6f : 0f);
        }
    }
}
