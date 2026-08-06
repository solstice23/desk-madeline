using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>
    /// Farewell jellyfish (Celeste's Glider): an 8x10 holdable actor with the
    /// original free-flight gravity, wall bounce, throw force and slow-fall rules.
    /// Coordinates use the same bottom-center convention as Player.
    /// </summary>
    public sealed class Glider : IPetHoldable
    {
        const float Width = 8f;
        const float Height = 10f;
        /// <summary>Collider reach from Pos, for the desktop shell's bounds handling.</summary>
        public const float HalfWidth = Width / 2f, ColliderHeight = Height;

        public PointF Pos;
        public PointF Speed;
        PointF IPetHoldable.Pos => Pos;
        PointF IPetHoldable.Speed => Speed;
        public Player Holder { get; private set; }
        public bool IsHeld => Holder != null;
        public bool SlowRun => false;
        public bool SlowFall => true;
        public bool BeingDragged { get; private set; }
        public string FrameId { get; private set; } = "glider/idle0";
        public float Rotation { get; private set; }
        public float ScaleX { get; private set; } = 1f;
        public float ScaleY { get; private set; } = 1f;
        public readonly Queue<PlayerSoundEvent> SoundEvents = new Queue<PlayerSoundEvent>();
        public bool MovementSoundActive { get; private set; }
        public float MovementSoundSpeed { get; private set; }

        float animTimer;
        int animFrame;
        string anim = "idle";
        float noGravityTimer;
        float highFrictionTimer;
        float cannotHoldTimer;
        PointF counter;
        IList<Solid> lastSolids;

        public Glider(PointF position) => Pos = position;

        static float Approach(float value, float target, float amount)
            => value > target ? Math.Max(target, value - amount) : Math.Min(target, value + amount);

        static bool Overlap(float l, float t, float r, float b, in Solid solid)
            => l < solid.R && r > solid.L && t < solid.B && b > solid.T;

        static void Bounds(float x, float y, out float l, out float t, out float r, out float b)
        {
            l = x - Width / 2f;
            r = x + Width / 2f;
            t = y - Height;
            b = y;
        }

        bool CollidesAt(float x, float y, IList<Solid> solids)
        {
            Bounds(x, y, out float l, out float t, out float r, out float b);
            foreach (Solid solid in solids)
                if (Overlap(l, t, r, b, solid)) return true;
            return false;
        }

        bool BlocksMove(float x, float y, IList<Solid> solids)
        {
            Bounds(Pos.X, Pos.Y, out float l0, out float t0, out float r0, out float b0);
            Bounds(x, y, out float l, out float t, out float r, out float b);
            foreach (Solid solid in solids)
            {
                if (!Overlap(l, t, r, b, solid)) continue;
                // See TheoCrystal.BlocksMove: window borders collide only from the outside so
                // one opening around her cannot swallow her, but a DreamBlock is an ordinary
                // Solid to every actor except a dashing player, and holds her where she lies.
                if (solid.Dream || !Overlap(l0, t0, r0, b0, solid)) return true;
            }
            return false;
        }

        public bool CanPickup(Player player)
        {
            if (IsHeld || BeingDragged || cannotHoldTimer > 0f) return false;
            // Holdable.PickupCollider is 20x22 at (-10,-16).
            float l = Pos.X - 10f, r = Pos.X + 10f, t = Pos.Y - 16f, b = Pos.Y + 6f;
            float ph = player.Ducking ? 6f : 11f;
            return player.Pos.X - 4f < r && player.Pos.X + 4f > l &&
                   player.Pos.Y - ph < b && player.Pos.Y > t;
        }

        public bool Pickup(Player player)
        {
            if (!CanPickup(player)) return false;
            Holder = player;
            Speed = PointF.Empty;
            highFrictionTimer = 0.5f;
            SetAnimation("held", true);
            return true;
        }

        public void Carry(PointF position)
        {
            Pos = position;
            counter = PointF.Empty;
        }

        public void Release(PointF force, IList<Solid> solids = null)
        {
            solids ??= lastSolids;
            // Holdable.Release first moves an overlapping throwable out of Solid.
            // This is essential after dream smuggling: without it the jelly remains
            // embedded in the DreamBlock and cannot meet the regrab collider.
            if (solids != null && CollidesAt(Pos.X, Pos.Y, solids))
            {
                if (force.X != 0f)
                {
                    int direction = Math.Sign(force.X);
                    for (int distance = 1; distance <= 10; distance++)
                    {
                        if (CollidesAt(Pos.X + direction * distance, Pos.Y, solids)) continue;
                        Pos.X += direction * distance;
                        break;
                    }
                }
                while (CollidesAt(Pos.X, Pos.Y, solids)) Pos.Y += 1f;
            }
            Holder = null;
            noGravityTimer = 0.1f;
            cannotHoldTimer = 0.3f;
            force.Y *= 0.5f;
            if (force.X != 0f && force.Y == 0f) force.Y = -0.4f;
            Speed = new PointF(force.X * 100f, force.Y * 100f);
            SetAnimation("idle", true);
        }

        public void BeginDrag(Player player)
        {
            if (Holder == player) player.ReleaseHoldableForDrag(this);
            BeingDragged = true;
            Holder = null;
            Speed = PointF.Empty;
            counter = PointF.Empty;
            SetAnimation("idle", true);
        }

        public void DragTo(PointF position)
        {
            if (!BeingDragged) return;
            Pos = position;
            counter = PointF.Empty;
        }

        /// <summary>Desktop: put her back on the displays after a drag left her off them.</summary>
        public void SnapIntoView(PointF position)
        {
            Pos = position;
            counter = PointF.Empty;
        }

        public void EndDrag(PointF velocity)
        {
            if (!BeingDragged) return;
            BeingDragged = false;
            cannotHoldTimer = 0.15f;
            noGravityTimer = 0.1f;
            Speed = velocity;
        }

        public void Update(float dt, PetInput input, IList<Solid> solids, float minX, float maxX)
        {
            lastSolids = solids;
            if (cannotHoldTimer > 0f) cannotHoldTimer -= dt;

            if (BeingDragged)
            {
                MovementSoundActive = false;
                Rotation = Approach(Rotation, 0f, (float)Math.PI * dt);
                ScaleX = Approach(ScaleX, 1f, 2f * dt);
                ScaleY = Approach(ScaleY, 1f, 2f * dt);
                SetAnimation("idle");
                UpdateAnimation(dt);
                return;
            }

            if (IsHeld)
            {
                Player player = Holder;
                float maxAngle = player.onGround ? 0.6981317f : 1.0471976f;
                float targetRotation = Math.Max(-maxAngle, Math.Min(maxAngle,
                    -player.Speed.X / 300f * maxAngle));
                Rotation = Approach(Rotation, targetRotation, (float)Math.PI * dt);

                bool open = !player.onGround && player.Speed.Y > 20f;
                bool opening = open && anim != "fall" && anim != "fallLoop";
                if (opening) SetAnimation("fall");
                else if (!open) SetAnimation("held");
                if (open)
                {
                    if (!MovementSoundActive)
                        SoundEvents.Enqueue(new PlayerSoundEvent(
                            "event:/new_content/game/10_farewell/glider_engage"));
                    MovementSoundActive = true;
                    float sx = player.Speed.X * 0.5f;
                    float sy = player.Speed.Y < 0f ? player.Speed.Y * 2f : player.Speed.Y;
                    MovementSoundSpeed = (float)Math.Sqrt(sx * sx + sy * sy) / 120f * 0.7f;
                }
                else MovementSoundActive = false;
                // Glider.PlayOpen sets this exact squash before easing back to one.
                // Besides matching the animation, keeping it as one continuous
                // fall -> fallLoop state avoids repeatedly restarting differently
                // colored expansion frames while the holder is falling.
                if (opening) { ScaleX = 1.5f; ScaleY = 0.6f; }
                float targetX = 1f, targetY = 1f;
                // vanilla reads Input.GliderMoveY here, not Input.MoveY
                if (open && input.GliderMoveY > 0) { targetX = 0.7f; targetY = 1.4f; }
                else if (open && input.GliderMoveY < 0) { targetX = 1.2f; targetY = 0.8f; }
                ScaleX = Approach(ScaleX, targetX, 2f * dt);
                ScaleY = Approach(ScaleY, targetY, 2f * dt);
            }
            else
            {
                MovementSoundActive = false;
                if (highFrictionTimer > 0f) highFrictionTimer -= dt;
                Rotation = Approach(Rotation, 0f, (float)Math.PI * dt);
                ScaleX = Approach(ScaleX, 1f, 2f * dt);
                ScaleY = Approach(ScaleY, 1f, 2f * dt);
                SetAnimation("idle");

                bool onGround = !CollidesAt(Pos.X, Pos.Y, solids) && CollidesAt(Pos.X, Pos.Y + 1f, solids);
                if (onGround)
                {
                    // Vanilla nudges a resting glider away from an unsupported
                    // ledge instead of letting it balance forever on one corner.
                    float target = !CollidesAt(Pos.X + 3f, Pos.Y + 1f, solids) ? 20f
                        : CollidesAt(Pos.X - 3f, Pos.Y + 1f, solids) ? 0f : -20f;
                    Speed.X = Approach(Speed.X, target, 800f * dt);
                }
                else
                {
                    float friction = Speed.Y < 0f ? 40f : highFrictionTimer > 0f ? 10f : 40f;
                    Speed.X = Approach(Speed.X, 0f, friction * dt);
                    if (noGravityTimer <= 0f)
                    {
                        float gravity = Speed.Y >= -30f ? 100f : 200f;
                        Speed.Y = Approach(Speed.Y, 30f, gravity * dt);
                    }
                    else noGravityTimer -= dt;
                }

                MoveH(Speed.X * dt, solids);
                MoveV(Speed.Y * dt, solids);
                float half = Width / 2f;
                if (Pos.X < minX + half)
                {
                    Pos.X = minX + half;
                    OnCollideH();
                }
                else if (Pos.X > maxX - half)
                {
                    Pos.X = maxX - half;
                    OnCollideH();
                }
            }

            UpdateAnimation(dt);
        }

        void MoveH(float amount, IList<Solid> solids)
        {
            counter.X += amount;
            int move = (int)Math.Round(counter.X, MidpointRounding.ToEven);
            if (move == 0) return;
            counter.X -= move;
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (BlocksMove(Pos.X + sign, Pos.Y, solids))
                {
                    counter.X = 0f;
                    OnCollideH();
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
                if (BlocksMove(Pos.X, Pos.Y + sign, solids))
                {
                    counter.Y = 0f;
                    OnCollideV();
                    return;
                }
                Pos.Y += sign;
                move -= sign;
            }
        }

        void OnCollideH()
        {
            SoundEvents.Enqueue(new PlayerSoundEvent(Speed.X < 0f
                ? "event:/new_content/game/10_farewell/glider_wallbounce_left"
                : "event:/new_content/game/10_farewell/glider_wallbounce_right"));
            Speed.X *= -1f;
            ScaleX = 0.8f;
            ScaleY = 1.2f;
        }

        void OnCollideV()
        {
            if (Math.Abs(Speed.Y) > 8f)
            {
                ScaleX = 1.2f;
                ScaleY = 0.8f;
                SoundEvents.Enqueue(new PlayerSoundEvent(
                    "event:/new_content/game/10_farewell/glider_land"));
            }
            Speed.Y = Speed.Y < 0f ? Speed.Y * -0.5f : 0f;
        }

        void SetAnimation(string id, bool restart = false)
        {
            if (!restart && anim == id) return;
            anim = id;
            animFrame = 0;
            animTimer = 0f;
        }

        void UpdateAnimation(float dt)
        {
            animTimer += dt;
            if (anim == "held")
            {
                FrameId = "glider/held0";
                return;
            }
            if (anim == "fall")
            {
                if (animTimer >= 0.06f)
                {
                    animTimer -= 0.06f;
                    animFrame++;
                }
                if (animFrame < 3)
                    FrameId = "glider/fall" + animFrame;
                else
                {
                    anim = "fallLoop";
                    animFrame = 0;
                    FrameId = "glider/fallLoop0";
                }
                return;
            }
            if (anim == "fallLoop")
            {
                if (animTimer >= 0.06f)
                {
                    animTimer -= 0.06f;
                    animFrame = (animFrame + 1) % 2;
                }
                FrameId = "glider/fallLoop" + animFrame;
                return;
            }
            if (animTimer >= 0.1f)
            {
                animTimer -= 0.1f;
                animFrame = (animFrame + 1) % 10;
            }
            FrameId = "glider/idle" + animFrame;
        }
    }
}
