using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    public enum SeekerParticleKind { Attack, HitWall, Stomp, Regen }

    public readonly struct SeekerParticleEvent
    {
        public readonly SeekerParticleKind Kind;
        public readonly PointF Position;
        public readonly float Direction, RangeX, RangeY;
        public readonly int Count;
        public SeekerParticleEvent(SeekerParticleKind kind, PointF position, int count,
            float direction = 0f, float rangeX = 0f, float rangeY = 0f)
        { Kind = kind; Position = position; Count = count; Direction = direction; RangeX = rangeX; RangeY = rangeY; }
    }

    /// <summary>Port of Celeste.Seeker. Position is the Seeker actor origin/center.</summary>
    public sealed class Seeker
    {
        public const int StIdle = 0, StPatrol = 1, StSpotted = 2, StAttack = 3,
            StStunned = 4, StSkidding = 5, StRegenerate = 6, StReturned = 7;
        public static readonly Color TrailColor = Color.FromArgb(0x99, 0xE5, 0x50);

        public PointF Pos, Speed;
        public int State { get; private set; } = StIdle;
        public string FrameId => animator.CurrentFrameId;
        public int SpriteFacing => spriteFacing;
        public float ScaleX { get; private set; } = 1f;
        public float ScaleY { get; private set; } = 1f;
        float WigglerValue => (float)Math.Cos(wigglerSine) * wigglerCounter;
        public float RenderScaleX => ScaleX * (1f - .3f * WigglerValue);
        public float RenderScaleY => ScaleY * (1f - .3f * WigglerValue);
        public PointF Shake { get; private set; }
        public string ShockwaveFrameId { get; private set; }
        public bool AggroLoopActive => State == StSpotted;
        public bool BoopedLoopActive => State == StRegenerate;
        public readonly Queue<PlayerSoundEvent> SoundEvents = new Queue<PlayerSoundEvent>();
        public readonly Queue<SeekerParticleEvent> ParticleEvents = new Queue<SeekerParticleEvent>();
        public readonly List<SeekerTrail> Trails = new List<SeekerTrail>();
        public bool Removed { get; private set; }
        public bool BeingDragged { get; private set; }

        readonly Animator animator;
        readonly Random random = new Random();
        PointF counter, lastSpottedAt, lastPathTo;
        bool spotted, canSeePlayer, attackWindUp, strongSkid;
        int facing = 1, spriteFacing = 1;
        string nextSprite;
        float idleX, idleY, stateTimer, spottedLoseTimer, spottedTurnDelay;
        float attackSpeed, wigglerCounter, wigglerSine, sceneTime, previousSceneTime;
        float shakerTimer;
        int regenStage;
        float shockwaveTimer;
        readonly List<PointF> path = new List<PointF>();
        int pathIndex;
        bool lastPathFound;
        PointF roomSpawnPosition;

        public Seeker(PointF position)
        {
            Pos = roomSpawnPosition = position;
            animator = new Animator(BuildAnimations());
            animator.Play("idle", true);
        }

        static Dictionary<string, Anim> BuildAnimations()
        {
            string[] Seq(string prefix, int from, int to)
            {
                var result = new List<string>();
                for (int i = from; i <= to; i++) result.Add("seeker/" + prefix + i.ToString("00"));
                return result.ToArray();
            }
            return new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase)
            {
                ["idle"] = new Anim { Frames = Seq("predator", 0, 19), Delay = .08f, Loop = true },
                ["search"] = new Anim { Frames = Seq("predator", 0, 39), Delay = .08f, Loop = true },
                ["spot"] = new Anim { Frames = Seq("predator", 40, 43), Delay = .07f, Goto = "spotted" },
                ["spotted"] = new Anim { Frames = Seq("predator", 44, 48), Delay = .07f, Loop = true },
                ["windUp"] = new Anim { Frames = Seq("predator", 103, 107), Delay = .07f, Goto = "attacking" },
                ["attacking"] = new Anim { Frames = Seq("predator", 49, 53), Delay = .07f, Loop = true },
                ["takeHit"] = new Anim { Frames = Seq("predator", 108, 121), Delay = .07f, Goto = "stunned" },
                ["stunned"] = new Anim { Frames = Seq("predator", 122, 123), Delay = .07f, Loop = true },
                ["pulse"] = new Anim { Frames = Seq("predator", 144, 151), Delay = .04f, Loop = true },
                ["recover"] = new Anim { Frames = Seq("rebirth", 0, 10), Delay = .07f },
                ["skid"] = new Anim { Frames = Seq("predator", 129, 133), Delay = .07f, Goto = "attacking" },
                ["dazed"] = new Anim { Frames = Seq("predator", 134, 143), Delay = .07f, Loop = true },
                ["flipMouth"] = new Anim { Frames = Seq("predator", 61, 63), Delay = .05f, Goto = "spotted" },
                ["flipEyes"] = new Anim { Frames = Seq("predator", 68, 74), Delay = .05f, Goto = "search" }
            };
        }

        static float Approach(float value, float target, float amount)
            => value > target ? Math.Max(target, value - amount) : Math.Min(target, value + amount);
        static PointF Approach(PointF value, PointF target, float amount)
        {
            float dx = target.X - value.X, dy = target.Y - value.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= amount || length == 0f) return target;
            return new PointF(value.X + dx / length * amount, value.Y + dy / length * amount);
        }
        static PointF Normalize(float x, float y, float length = 1f)
        {
            float d = (float)Math.Sqrt(x * x + y * y);
            return d <= .00001f ? PointF.Empty : new PointF(x / d * length, y / d * length);
        }
        static float LengthSq(PointF p) => p.X * p.X + p.Y * p.Y;
        static float DistanceSq(PointF a, PointF b) { float x = a.X - b.X, y = a.Y - b.Y; return x * x + y * y; }
        static float Dot(PointF a, PointF b) => a.X * b.X + a.Y * b.Y;
        static float Angle(PointF p) => (float)Math.Atan2(p.Y, p.X);
        static PointF AngleTo(float angle, float length) => new PointF((float)Math.Cos(angle) * length, (float)Math.Sin(angle) * length);
        static float WrapAngle(float a) { while (a > Math.PI) a -= (float)Math.PI * 2; while (a < -Math.PI) a += (float)Math.PI * 2; return a; }
        static PointF RotateTowards(PointF speed, float target, float max)
        {
            float current = Angle(speed), delta = WrapAngle(target - current);
            return AngleTo(current + Math.Max(-max, Math.Min(max, delta)), (float)Math.Sqrt(LengthSq(speed)));
        }
        static bool Overlap(float l, float t, float r, float b, in Solid s)
            => l < s.R && r > s.L && t < s.B && b > s.T;

        bool CollidesAt(float x, float y, IList<Solid> solids)
        {
            foreach (Solid s in solids) if (Overlap(x - 3, y - 3, x + 3, y + 3, s)) return true;
            return false;
        }

        static bool SegmentHitsSolid(PointF from, PointF to, IList<Solid> solids)
        {
            foreach (Solid s in solids)
            {
                float dx = to.X - from.X, dy = to.Y - from.Y, t0 = 0f, t1 = 1f;
                if (Clip(-dx, from.X - s.L, ref t0, ref t1) && Clip(dx, s.R - from.X, ref t0, ref t1) &&
                    Clip(-dy, from.Y - s.T, ref t0, ref t1) && Clip(dy, s.B - from.Y, ref t0, ref t1)) return true;
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

        bool CanSeePlayer(Player player, IList<Solid> solids, RectangleF camera)
        {
            PointF center = player.Center;
            if (State != StSpotted && !camera.Contains(Pos) && DistanceSq(Pos, center) > 25600f) return false;
            PointF perpendicular = Normalize(-(center.Y - Pos.Y), center.X - Pos.X, 2f);
            PointF a = new PointF(Pos.X + perpendicular.X, Pos.Y + perpendicular.Y);
            PointF b = new PointF(center.X + perpendicular.X, center.Y + perpendicular.Y);
            if (SegmentHitsSolid(a, b, solids)) return false;
            a = new PointF(Pos.X - perpendicular.X, Pos.Y - perpendicular.Y);
            b = new PointF(center.X - perpendicular.X, center.Y - perpendicular.Y);
            return !SegmentHitsSolid(a, b, solids);
        }

        float GetSpeedMagnitude(float baseMagnitude, Player player)
            => DistanceSq(Pos, player.Center) > 12544f ? baseMagnitude * 3f : baseMagnitude * 1.5f;
        PointF FollowTarget => new PointF(lastSpottedAt.X, lastSpottedAt.Y - 2f);

        PointF GetPathSpeed(float magnitude)
        {
            while (pathIndex < path.Count && DistanceSq(Pos, path[pathIndex]) < 36f) pathIndex++;
            if (pathIndex >= path.Count) return PointF.Empty;
            return Normalize(path[pathIndex].X - Pos.X, path[pathIndex].Y - Pos.Y, magnitude);
        }

        void SetState(int state)
        {
            if (State == state) return;
            if (State == StSkidding) spriteFacing = facing;
            if (State == StRegenerate) SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/05_mirror_temple/seeker_revive"));
            State = state;
            stateTimer = 0f;
            switch (state)
            {
                case StIdle: break;
                case StSpotted:
                    SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/05_mirror_temple/seeker_aggro"));
                    TurnFacing(lastSpottedAt.X - Pos.X, "spot");
                    spottedLoseTimer = .6f; spottedTurnDelay = 1f;
                    break;
                case StAttack:
                    SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/05_mirror_temple/seeker_dash"));
                    attackWindUp = true; attackSpeed = -60f;
                    Speed = Normalize(FollowTarget.X - Pos.X, FollowTarget.Y - Pos.Y, -60f);
                    TurnFacing(lastSpottedAt.X - Pos.X, "windUp");
                    break;
                case StSkidding:
                    SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/05_mirror_temple/seeker_dash_turn"));
                    strongSkid = false; TurnFacing(-facing); break;
                case StRegenerate:
                    SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/general/thing_booped"));
                    animator.Play("takeHit", true); regenStage = 0; break;
            }
        }

        void TurnFacing(float dir, string gotoSprite = null)
        {
            if (dir != 0f) facing = Math.Sign(dir);
            if (spriteFacing != facing)
            {
                animator.Play(State == StSkidding ? "skid" : State == StAttack || State == StSpotted ? "flipMouth" : "flipEyes", true);
                nextSprite = gotoSprite;
            }
            else if (gotoSprite != null) animator.Play(gotoSprite);
        }
        void SnapFacing(float dir) { if (dir != 0f) spriteFacing = facing = Math.Sign(dir); }
        void StartWiggler() { wigglerCounter = 1f; wigglerSine = 0f; }

        public void BeginDrag()
        {
            BeingDragged = true;
            Speed = counter = PointF.Empty;
            ResetStateAt(Pos, null, updateRoomSpawn: false);
        }

        public void DragTo(PointF position)
        {
            if (!BeingDragged) return;
            Pos = position;
            counter = PointF.Empty;
        }

        public void EndDrag(PointF velocity)
        {
            if (!BeingDragged) return;
            BeingDragged = false;
            roomSpawnPosition = Pos;
            Speed = velocity;
        }

        public void ResetForRoomReload(PointF playerCenter)
        {
            BeingDragged = false;
            ResetStateAt(roomSpawnPosition, playerCenter, updateRoomSpawn: false);
        }

        void ResetStateAt(PointF position, PointF? playerCenter, bool updateRoomSpawn)
        {
            Pos = position;
            if (updateRoomSpawn) roomSpawnPosition = position;
            Speed = counter = PointF.Empty;
            State = StIdle;
            stateTimer = spottedLoseTimer = spottedTurnDelay = attackSpeed = 0f;
            attackWindUp = strongSkid = spotted = canSeePlayer = lastPathFound = false;
            lastSpottedAt = lastPathTo = PointF.Empty;
            path.Clear(); pathIndex = 0;
            ScaleX = ScaleY = 1f;
            wigglerCounter = shakerTimer = shockwaveTimer = 0f;
            Shake = PointF.Empty; ShockwaveFrameId = null; nextSprite = null;
            regenStage = 0;
            foreach (SeekerTrail trail in Trails) trail.Stamp?.Dispose();
            Trails.Clear();
            ParticleEvents.Clear();
            SoundEvents.Clear();
            if (playerCenter.HasValue && playerCenter.Value.X != Pos.X)
                SnapFacing(Math.Sign(playerCenter.Value.X - Pos.X));
            animator.Play("idle", true);
        }

        public void UpdateDormant(float dt)
        {
            if (BeingDragged) return;
            animator.Play("idle");
            animator.Update(dt);
        }

        public void Update(float dt, Player player, IList<Solid> solids, RectangleF worldBounds, RectangleF camera)
        {
            if (Removed) return;
            for (int i = Trails.Count - 1; i >= 0; i--)
            {
                Trails[i].Age += dt;
                if (Trails[i].Age >= .5f) { Trails[i].Stamp?.Dispose(); Trails.RemoveAt(i); }
            }
            previousSceneTime = sceneTime;
            sceneTime += dt;
            if (BeingDragged)
            {
                Speed = counter = PointF.Empty;
                animator.Play("idle");
                animator.Update(dt);
                return;
            }
            ScaleX = Approach(ScaleX, 1f, 2f * dt); ScaleY = Approach(ScaleY, 1f, 2f * dt);
            idleX += (float)Math.PI * 2f * .5f * dt; idleY += (float)Math.PI * 2f * .7f * dt;
            if (wigglerCounter > 0f) { wigglerSine += (float)Math.PI * 4f * dt; wigglerCounter = Math.Max(0f, wigglerCounter - dt / .8f); }
            if (shakerTimer > 0f)
            {
                shakerTimer -= dt;
                if (OnInterval(.05f)) Shake = new PointF(random.Next(-1, 2), random.Next(-1, 2));
                if (shakerTimer <= 0f) Shake = PointF.Empty;
            }
            else Shake = PointF.Empty;
            stateTimer += dt;

            canSeePlayer = State != StRegenerate && !player.IsDead && !player.IsRespawning && CanSeePlayer(player, solids, camera);
            if (canSeePlayer) { spotted = true; lastSpottedAt = player.Center; }
            if (lastPathTo != lastSpottedAt)
            {
                lastPathTo = lastSpottedAt; pathIndex = 0;
                lastPathFound = DesktopPathfinder.Find(path, Pos, FollowTarget, solids, worldBounds);
            }

            // PlayerCollider components are added before StateMachine in the
            // original constructor, so contacts use the pre-state-update pose.
            CheckPlayer(player);

            switch (State)
            {
                case StIdle: UpdateIdle(dt, player); break;
                case StPatrol: UpdatePatrol(dt, player); break;
                case StSpotted: UpdateSpotted(dt, player, solids); break;
                case StAttack: UpdateAttack(dt); break;
                case StStunned:
                    Speed = Approach(Speed, PointF.Empty, 150f * dt);
                    if (stateTimer >= .8f) SetState(StIdle);
                    break;
                case StSkidding:
                    if (stateTimer >= .08f) strongSkid = true;
                    Speed = Approach(Speed, PointF.Empty, (strongSkid ? 400f : 200f) * dt);
                    if (LengthSq(Speed) < 400f) SetState(canSeePlayer ? StSpotted : StIdle);
                    break;
                case StRegenerate: UpdateRegenerate(dt, player, solids); break;
                case StReturned: if (stateTimer >= .3f) SetState(StIdle); break;
            }

            string priorAnimation = animator.CurrentId;
            animator.Update(dt);
            if ((priorAnimation == "flipMouth" || priorAnimation == "flipEyes" || priorAnimation == "skid") &&
                animator.CurrentId != priorAnimation)
            {
                spriteFacing = facing;
                if (nextSprite != null) { animator.Play(nextSprite); nextSprite = null; }
            }

            if (shockwaveTimer > 0f)
            {
                shockwaveTimer += dt;
                int frame = (int)(shockwaveTimer / .05f);
                ShockwaveFrameId = frame <= 18 ? "seeker/Shockwave" + frame.ToString("00") : null;
                if (frame > 18) shockwaveTimer = 0f;
            }

            MoveH(Speed.X * dt, solids); MoveV(Speed.Y * dt, solids);
            if (Pos.X - 3 < worldBounds.Left && Speed.X < 0f) { Pos.X = worldBounds.Left + 3; CollideH(-1, solids); }
            else if (Pos.X + 3 > worldBounds.Right && Speed.X > 0f) { Pos.X = worldBounds.Right - 3; CollideH(1, solids); }
            if (Pos.Y - 3 < worldBounds.Top - 8 && Speed.Y < 0f) { Pos.Y = worldBounds.Top - 5; CollideV(); }
            else if (Pos.Y + 3 > worldBounds.Bottom && Speed.Y > 0f) { Pos.Y = worldBounds.Bottom - 3; CollideV(); }

        }

        bool OnInterval(float interval)
            => (int)(sceneTime / interval) != (int)(previousSceneTime / interval);

        void UpdateIdle(float dt, Player player)
        {
            if (canSeePlayer) { SetState(StSpotted); return; }
            PointF target = PointF.Empty;
            if (spotted && DistanceSq(Pos, FollowTarget) > 64f)
            {
                float m = GetSpeedMagnitude(50f, player);
                target = lastPathFound ? GetPathSpeed(m) : Normalize(FollowTarget.X - Pos.X, FollowTarget.Y - Pos.Y, m);
            }
            if (target.IsEmpty) target = new PointF((float)Math.Sin(idleX) * 6f, (float)Math.Sin(idleY) * 6f);
            Speed = Approach(Speed, target, 200f * dt);
            if (LengthSq(Speed) > 400f) TurnFacing(Speed.X);
            if (spriteFacing == facing) animator.Play("idle");
        }
        void UpdatePatrol(float dt, Player player) { SetState(StIdle); }

        void UpdateSpotted(float dt, Player player, IList<Solid> solids)
        {
            if (!canSeePlayer) { spottedLoseTimer -= dt; if (spottedLoseTimer < 0f) { SetState(StIdle); return; } }
            else spottedLoseTimer = .6f;
            float m = GetSpeedMagnitude(60f, player);
            PointF target = lastPathFound ? GetPathSpeed(m) : Normalize(FollowTarget.X - Pos.X, FollowTarget.Y - Pos.Y, m);
            if (DistanceSq(Pos, FollowTarget) < 2500f && Pos.Y < FollowTarget.Y)
            {
                float angle = Angle(target);
                if (Pos.Y < FollowTarget.Y - 2f) angle = AngleLerp(angle, (float)Math.PI / 2f, .5f);
                else if (Pos.Y > FollowTarget.Y + 2f) angle = AngleLerp(angle, -(float)Math.PI / 2f, .5f);
                target = AngleTo(angle, 60f);
                float offset = Math.Sign(Pos.X - lastSpottedAt.X) * 48f;
                if (Math.Abs(Pos.X - lastSpottedAt.X) < 36f && !CollidesAt(Pos.X + offset, Pos.Y, solids) && !CollidesAt(lastSpottedAt.X + offset, lastSpottedAt.Y, solids))
                    target.X = Math.Sign(Pos.X - lastSpottedAt.X) * 60f;
            }
            Speed = Approach(Speed, target, 600f * dt);
            spottedTurnDelay -= dt; if (spottedTurnDelay <= 0f) TurnFacing(Speed.X, "spotted");
            if (stateTimer >= .2f && CanAttack(solids)) SetState(StAttack);
        }
        static float AngleLerp(float a, float b, float amount) => a + WrapAngle(b - a) * amount;
        bool CanAttack(IList<Solid> solids)
        {
            if (Math.Abs(Pos.Y - lastSpottedAt.Y) > 24f || Math.Abs(Pos.X - lastSpottedAt.X) < 16f) return false;
            PointF aim = Normalize(FollowTarget.X - Pos.X, FollowTarget.Y - Pos.Y);
            if (-aim.Y > .5f || aim.Y > .5f) return false;
            return !CollidesAt(Pos.X + Math.Sign(lastSpottedAt.X - Pos.X) * 24f, Pos.Y, solids);
        }

        void UpdateAttack(float dt)
        {
            if (attackWindUp)
            {
                if (stateTimer >= .3f)
                {
                    attackWindUp = false; attackSpeed = 180f;
                    Speed = Normalize(lastSpottedAt.X - Pos.X, lastSpottedAt.Y - 2f - Pos.Y, 180f);
                    SnapFacing(Speed.X);
                }
                return;
            }
            PointF aim = Normalize(FollowTarget.X - Pos.X, FollowTarget.Y - Pos.Y);
            if (Dot(Normalize(Speed.X, Speed.Y), aim) < .4f) { SetState(StSkidding); return; }
            attackSpeed = Approach(attackSpeed, 260f, 300f * dt);
            Speed = RotateTowards(Speed, Angle(aim), .61086524f * dt);
            Speed = Normalize(Speed.X, Speed.Y, attackSpeed);
            if (OnInterval(.04f))
            {
                PointF back = Normalize(-Speed.X, -Speed.Y);
                ParticleEvents.Enqueue(new SeekerParticleEvent(SeekerParticleKind.Attack,
                    new PointF(Pos.X + back.X * 4f, Pos.Y + back.Y * 4f), 2, Angle(back), 4f, 4f));
            }
            if (OnInterval(.06f))
            {
                Trails.Add(new SeekerTrail(Pos, FrameId, spriteFacing, RenderScaleX, RenderScaleY));
            }
        }

        void UpdateRegenerate(float dt, Player player, IList<Solid> solids)
        {
            Speed.X = Approach(Speed.X, 0f, 150f * dt); Speed = Approach(Speed, PointF.Empty, 150f * dt);
            if (regenStage == 0 && stateTimer >= 1f) { regenStage = 1; shakerTimer = float.MaxValue; }
            if (regenStage == 1 && stateTimer >= 1.2f) { regenStage = 2; animator.Play("pulse", true); }
            if (regenStage == 2 && stateTimer >= 1.7f)
            {
                regenStage = 3; animator.Play("recover", true);
                shockwaveTimer = .0001f; ShockwaveFrameId = "seeker/Shockwave00";
            }
            if (regenStage == 3 && stateTimer >= 1.85f)
            {
                regenStage = 4;
                if (DistanceSq(Pos, player.Center) < 1600f && !SegmentHitsSolid(Pos, player.Center, solids)) player.ExplodeLaunch(Pos);
                for (float a = 0f; a < Math.PI * 2; a += .17453292f)
                {
                    float randomized = a + ((float)random.NextDouble() * 2f - 1f) * ((float)Math.PI / 90f);
                    float radius = random.Next(12, 18);
                    ParticleEvents.Enqueue(new SeekerParticleEvent(SeekerParticleKind.Regen,
                        new PointF(Pos.X + (float)Math.Cos(randomized) * radius, Pos.Y + (float)Math.Sin(randomized) * radius), 1, a));
                }
                shakerTimer = 0f; Shake = PointF.Empty; SetState(StReturned);
            }
        }

        void MoveH(float amount, IList<Solid> solids)
        {
            counter.X += amount; int move = (int)Math.Round(counter.X, MidpointRounding.ToEven); counter.X -= move;
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (CollidesAt(Pos.X + sign, Pos.Y, solids))
                {
                    if (State == StAttack)
                    {
                        if (!CollidesAt(Pos.X + sign, Pos.Y + 4f, solids)) { MoveVExact(4, solids); move = 0; continue; }
                        if (!CollidesAt(Pos.X + sign, Pos.Y - 4f, solids)) { MoveVExact(-4, solids); move = 0; continue; }
                    }
                    counter.X = 0f; CollideH(sign, solids); return;
                }
                Pos.X += sign; move -= sign;
            }
        }
        void MoveV(float amount, IList<Solid> solids)
        {
            counter.Y += amount; int move = (int)Math.Round(counter.Y, MidpointRounding.ToEven); counter.Y -= move;
            int sign = Math.Sign(move);
            while (move != 0)
            {
                if (CollidesAt(Pos.X, Pos.Y + sign, solids)) { counter.Y = 0f; CollideV(); return; }
                Pos.Y += sign; move -= sign;
            }
        }
        bool MoveVExact(int amount, IList<Solid> solids)
        {
            int sign = Math.Sign(amount);
            while (amount != 0) { if (CollidesAt(Pos.X, Pos.Y + sign, solids)) return false; Pos.Y += sign; amount -= sign; }
            return true;
        }
        void CollideH(int direction, IList<Solid> solids)
        {
            if ((State == StAttack || State == StSkidding) && Math.Abs(Speed.X) >= 100f)
            {
                float x = direction > 0 ? Pos.X + 3f : Pos.X - 3f;
                ParticleEvents.Enqueue(new SeekerParticleEvent(SeekerParticleKind.HitWall,
                    new PointF(x, Pos.Y), 12, direction > 0 ? (float)Math.PI : 0f, 0f, 4f));
                Speed.X = Math.Sign(Speed.X) * -100f; Speed.Y *= .4f;
                ScaleX = .6f; ScaleY = 1.4f; shakerTimer = .5f; StartWiggler();
                SoundEvents.Enqueue(new PlayerSoundEvent("event:/game/05_mirror_temple/seeker_hit_normal"));
                SetState(StStunned);
            }
            else Speed.X *= -.2f;
        }
        void CollideV() { Speed.Y *= State == StAttack ? -.6f : -.2f; }

        void CheckPlayer(Player player)
        {
            if (player.IsDead || player.IsRespawning || State == StRegenerate) return;
            player.GetHitbox(out float pl, out float pt, out float pr, out float pb);
            float attackL = Pos.X - 6f, attackT = Pos.Y - 2f, attackR = Pos.X + 6f, attackB = Pos.Y + 6f;
            if (pl < attackR && pr > attackL && pt < attackB && pb > attackT)
            {
                if (State != StStunned) player.Die(Normalize(player.Center.X - Pos.X, player.Center.Y - Pos.Y));
                else { player.PointBounce(Pos); Speed = Normalize(Pos.X - player.Center.X, Pos.Y - player.Center.Y, 100f); StartWiggler(); }
                return;
            }
            bool wideBounce = State == StAttack && (Speed.X > 0f || Speed.Y < 0f);
            float bounceL = Pos.X - (State == StAttack && Speed.X > 0f ? 10f : 6f);
            float bounceR = bounceL + (wideBounce ? 16f : 12f);
            float bounceT = Pos.Y - 8f, bounceB = Pos.Y - 2f;
            if (pl < bounceR && pr > bounceL && pt < bounceB && pb > bounceT)
            {
                player.Bounce(Pos.Y - 2f); Speed = Normalize(Pos.X - player.Center.X, Pos.Y - player.Center.Y, 200f);
                ScaleX = 1.4f; ScaleY = .6f; player.ApplyFreeze(.15f); SetState(StRegenerate);
                ParticleEvents.Enqueue(new SeekerParticleEvent(SeekerParticleKind.Stomp,
                    new PointF(Pos.X, Pos.Y - 5f), 8, -(float)Math.PI / 2f, 6f, 3f));
            }
        }
    }

    public sealed class SeekerTrail
    {
        public readonly PointF Position; public readonly string FrameId; public readonly int Facing;
        public readonly float ScaleX, ScaleY; public float Age;
        public Bitmap Stamp;
        public SeekerTrail(PointF position, string frame, int facing, float sx, float sy)
        { Position = position; FrameId = frame; Facing = facing; ScaleX = sx; ScaleY = sy; }
    }

    static class DesktopPathfinder
    {
        sealed class Node { public Point Cell, Parent; public bool HasParent; public int Cost = int.MaxValue; }
        static readonly Point[] Directions = { new Point(1, 0), new Point(0, 1), new Point(-1, 0), new Point(0, -1) };
        public static bool Find(List<PointF> result, PointF from, PointF to, IList<Solid> solids, RectangleF bounds)
        {
            int ox = (int)Math.Floor(bounds.Left / 8f), oy = (int)Math.Floor(bounds.Top / 8f);
            int w = Math.Max(1, (int)Math.Ceiling(bounds.Width / 8f)), h = Math.Max(1, (int)Math.Ceiling(bounds.Height / 8f));
            if ((long)w * h > 1500000) return false;
            var solid = new bool[w, h];
            foreach (Solid s in solids)
            {
                int l = (int)Math.Floor(s.L / 8f) - ox, r = (int)Math.Ceiling(s.R / 8f) - ox;
                int t = (int)Math.Floor(s.T / 8f) - oy, b = (int)Math.Ceiling(s.B / 8f) - oy;
                for (int x = Math.Max(0, l); x < Math.Min(w, r); x++) for (int y = Math.Max(0, t); y < Math.Min(h, b); y++) solid[x, y] = true;
            }
            Point start = new Point((int)Math.Floor(from.X / 8f) - ox, (int)Math.Floor(from.Y / 8f) - oy);
            Point end = new Point((int)Math.Floor(to.X / 8f) - ox, (int)Math.Floor(to.Y / 8f) - oy);
            if (start.X < 0 || start.Y < 0 || start.X >= w || start.Y >= h || end.X < 0 || end.Y < 0 || end.X >= w || end.Y >= h || solid[start.X, start.Y] || solid[end.X, end.Y]) return false;
            var nodes = new Dictionary<Point, Node>(); var active = new List<Node>();
            Node first = new Node { Cell = start, Cost = 0 }; nodes[start] = first; active.Add(first); Node found = null;
            while (active.Count > 0 && found == null)
            {
                active.Sort((a, b) => b.Cost.CompareTo(a.Cost)); Node current = active[active.Count - 1]; active.RemoveAt(active.Count - 1);
                foreach (Point d in Directions)
                {
                    Point p = new Point(current.Cell.X + d.X, current.Cell.Y + d.Y);
                    if (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h || solid[p.X, p.Y]) continue;
                    int add = 1;
                    foreach (Point around in Directions) { int x = p.X + around.X, y = p.Y + around.Y; if (x >= 0 && y >= 0 && x < w && y < h && solid[x, y]) { add = 7; break; } }
                    if (current.HasParent && p.X != current.Parent.X && p.Y != current.Parent.Y) add += 4;
                    if (d.Y != 0) add += (int)(current.Cost * .5f);
                    int cost = current.Cost + add;
                    if (!nodes.TryGetValue(p, out Node node)) { node = new Node { Cell = p }; nodes[p] = node; }
                    if (node.Cost <= cost) continue;
                    node.Cost = cost; node.Parent = current.Cell; node.HasParent = true; active.Add(node);
                    if (p == end) { found = node; break; }
                }
            }
            if (found == null) return false;
            result.Clear(); Node cursor = found; int guard = 0;
            while (cursor.Cell != start && guard++ < 1000)
            {
                result.Add(new PointF((cursor.Cell.X + ox + .5f) * 8f, (cursor.Cell.Y + oy + .5f) * 8f));
                cursor = nodes[cursor.Parent];
            }
            if (guard >= 1000) return false;
            result.Reverse();
            for (int i = 1; i < result.Count - 1; i++)
                if ((result[i].X == result[i - 1].X && result[i].X == result[i + 1].X) || (result[i].Y == result[i - 1].Y && result[i].Y == result[i + 1].Y)) { result.RemoveAt(i); i--; }
            return true;
        }
    }
}
