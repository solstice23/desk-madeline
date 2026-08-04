using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>平台（窗口矩形 / 地板），单位：游戏像素。</summary>
    public struct Solid
    {
        public IntPtr Id;
        public float L, T, R, B;
        public bool Dream;
    }

    /// <summary>每帧输入快照。</summary>
    public struct PetInput
    {
        public int MoveX;      // -1/0/1
        public int MoveY;      // -1 上 / 0 / 1 下
        public bool JumpHeld;
        public bool GrabHeld;
        public bool JumpPressed;  // 已做输入缓冲（本帧有效）
        public bool DashPressed;
        public bool ElytraHeld;
    }

    public readonly struct PlayerSoundEvent
    {
        public readonly string Path, Parameter;
        public readonly float Value;
        public PlayerSoundEvent(string path, string parameter = null, float value = 0f)
        { Path = path; Parameter = parameter; Value = value; }
    }

    /// <summary>
    /// 玛德琳：从 Celeste Player.cs 移植的物理与状态机（Normal/Climb/Dash）。
    /// 坐标单位 = 游戏像素（1:1 对应原作），渲染时整体放大 S 倍。
    /// </summary>
    public class Player
    {
        // ===== 原作常量（Player.cs）=====
        const float MaxFall = 160f;
        const float Gravity = 900f;
        const float HalfGravThreshold = 40f;
        const float FastMaxFall = 240f;
        const float FastMaxAccel = 300f;
        const float MaxRun = 90f;
        const float RunAccel = 1000f;
        const float RunReduce = 400f;
        const float AirMult = 0.65f;
        const float DuckFriction = 500f;
        const float JumpGraceTime = 0.1f;
        const float JumpSpeed = -105f;
        const float JumpHBoost = 40f;
        const float VarJumpTime = 0.2f;
        const int UpwardCornerCorrection = 4;
        const int DashCornerCorrection = 4;
        const float WallJumpForceTime = 0.16f;
        const float WallJumpHSpeed = 130f;
        const float WallSlideTime = 1.2f;
        const float SuperWallJumpH = 170f;
        const float SuperWallJumpSpeed = -160f;
        const float SuperWallJumpVarTime = 0.25f;
        const float SuperWallJumpForceTime = 0.2f;
        const float SuperJumpH = 260f;
        const float DuckSuperJumpXMult = 1.25f;
        const float DuckSuperJumpYMult = 0.5f;
        const float DashSpeed = 240f;
        const float EndDashSpeed = 160f;
        const float EndDashUpMult = 0.75f;
        const float DashTime = 0.15f;
        const float DashCooldown = 0.2f;
        const float DashRefillCooldown = 0.1f;
        const float DashAttackTime = 0.3f;
        const float ClimbMaxStamina = 110f;
        const float WallSpeedRetentionTime = 0.06f;  // 撞墙保留速度时限（原作 wallSpeedRetentionTimer，约 4 帧）
        const float ClimbJumpCost = 27.5f;
        const float ClimbUpSpeed = -45f;
        const float ClimbDownSpeed = 80f;
        const float ClimbAccel = 900f;         // 攀爬移动加速度（原作 Approach）
        const float ClimbStillCost = 10f;      // 静止爬墙每秒体力消耗
        const float ClimbHopY = -120f;         // 爬过墙顶弹起速度
        const float ClimbHopX = 100f;          // 爬过墙顶水平推力
        const int DashingUpwardCornerCorrection = 5; // 上冲天花板角修正距离

        // Keep Celeste.Player's complete state-number contract.  Several of these
        // need level actors the desktop cannot currently create, but retaining the
        // original indices means new ports can be added without renumbering or
        // folding unrelated behavior into Normal.
        public const int StNormal = 0, StClimb = 1, StDash = 2, StSwim = 3,
            StBoost = 4, StRedDash = 5, StHitSquash = 6, StLaunch = 7,
            StPickup = 8, StDreamDash = 9, StSummitLaunch = 10, StDummy = 11,
            StIntroWalk = 12, StIntroJump = 13, StIntroRespawn = 14,
            StIntroWakeUp = 15, StBirdDashTutorial = 16, StFrozen = 17,
            StReflectionFall = 18, StStarFly = 19, StTempleFall = 20,
            StCassetteFly = 21, StAttract = 22, StIntroMoonJump = 23,
            StFlingBird = 24, StIntroThinkForABit = 25, StElytra = 26;

        public static readonly string[] StateNames =
        {
            "Normal", "Climb", "Dash", "Swim", "Boost", "RedDash",
            "HitSquash", "Launch", "Pickup", "DreamDash", "SummitLaunch",
            "Dummy", "IntroWalk", "IntroJump", "IntroRespawn", "IntroWakeUp",
            "BirdDashTutorial", "Frozen", "ReflectionFall", "StarFly",
            "TempleFall", "CassetteFly", "Attract", "IntroMoonJump",
            "FlingBird", "IntroThinkForABit", "Elytra"
        };

        // 头发颜色（Player.cs）
        public static readonly Color NormalHairColor = Color.FromArgb(0xAC, 0x32, 0x32);
        public static readonly Color UsedHairColor = Color.FromArgb(0x44, 0xB7, 0xFF);
        public static readonly Color FlashHairColor = Color.White;
        public static readonly Color TwoDashesHairColor = Color.FromArgb(0xFF, 0x6D, 0xEF);

        // ===== 状态 =====
        public PointF Pos;          // 脚底中心（游戏像素）
        public PointF Speed;
        public int Facing = 1;      // -1 左 / 1 右
        public bool Ducking;
        public int State;
        public string StateName => State >= 0 && State < StateNames.Length
            ? StateNames[State] : "Custom(" + State + ")";
        public int Dashes = 1;
        // 0/1/2 = 最大冲刺数，-1 = 无限（按原作双冲刺外观处理）。
        public int DashMode = 1;
        public float Stamina = ClimbMaxStamina;
        public bool InfiniteStamina;
        public bool FreezeFramesEnabled = true;
        public bool RespawnReversalEnabled;
        public bool ElytraEnabled;
        public bool onGround;
        public IntPtr GroundId;
        public PointF DashDir;
        public IList<Glider> Gliders;
        public Glider Holding { get; private set; }
        public bool IsHoldingGlider => Holding != null;

        // 表现
        public float SpriteScaleX = 1f, SpriteScaleY = 1f;
        public string AnimId = "idle";
        public string CurrentFrameId;   // 由窗口每帧同步（头发锚点跟随当前帧）
        public string SweatAnimId { get; private set; } = "idle";
        public int SweatAnimSequenceCount { get; private set; }
        public Color HairColor = NormalHairColor;
        public readonly PlayerHairSim Hair = new PlayerHairSim();
        public int WavedashCount { get; private set; }
        public int LaunchCount { get; private set; }
        public int DashSequenceCount { get; private set; }
        public int WallJumpEffectCount { get; private set; }
        public int LastWallJumpDirection { get; private set; }
        public int JumpEffectCount { get; private set; }
        public int LandingEffectCount { get; private set; }
        public bool WallSlideDustActive => (wallSlideDir != 0 && wallSlideTimer / WallSlideTime > 0.65f) ||
                                           (State == StClimb && lastClimbMove > 0);
        public int WallSlideDirection => State == StClimb ? Facing : wallSlideDir;
        public bool LastDashWasTwo { get; private set; }
        public bool IsLowStamina => !InfiniteStamina &&
            Stamina + (wallBoostTimer > 0f ? ClimbJumpCost : 0f) < 20f;
        public int DashCapacity => DashMode < 0 ? 2 : DashMode;
        public bool InfiniteDash => DashMode < 0;
        public bool IsHitStopped => freezeTimer > 0f;
        public bool IsFrozen => freezeTimer > 0f || dashAimPending;
        public bool IsDashAttacking => DashAttacking;
        public bool IsDead { get; private set; }
        public float CurrentHitHeight => HitH;
        public float DeathPercent { get; private set; }
        public PointF DeathPosition { get; private set; }
        public Color DeathColor { get; private set; }
        public bool IsPreDeath { get; private set; }
        public PointF DeathBodyPosition { get; private set; }
        public float DeathBodyScale { get; private set; } = 1f;
        public float DeathBodyRotation { get; private set; }
        public string DeathBodyFrameId { get; private set; }
        public int DeathSequenceCount { get; private set; }
        public bool IsRespawning => State == StIntroRespawn;
        public PointF Center => new PointF(Pos.X, Pos.Y - HitH / 2f);
        public int ElytraAnimationFrame { get; private set; } = 6;
        public int ElytraDeploySequenceCount { get; private set; }
        public int ExplodeLaunchSequenceCount { get; private set; }
        public float ExplodeLaunchAngle { get; private set; }
        public float ElytraDeployParticleAngle { get; private set; }
        public float RespawnPercent { get; private set; }
        public PointF RespawnEffectPosition { get; private set; }
        public Color RespawnColor { get; private set; }
        public readonly Queue<PlayerSoundEvent> SoundEvents = new Queue<PlayerSoundEvent>();
        public int NormalSurfaceSoundIndex = 8;        // Platform.cs default (brick)
        public const int DreamSurfaceSoundIndex = 12; // active DreamBlock

        public int GroundSurfaceSoundIndex => SurfaceSoundIndexAt(Pos.X, Pos.Y + 1f);
        public int WallSurfaceSoundIndex(int direction)
            => SurfaceSoundIndexAt(Pos.X + direction * 3f, Pos.Y);

        int SurfaceSoundIndexAt(float x, float y)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (Solid solid in Solids)
                if (Overlap(l, t, r, b, solid))
                    return solid.Dream ? DreamSurfaceSoundIndex : NormalSurfaceSoundIndex;
            return NormalSurfaceSoundIndex;
        }

        void PlaySound(string path, string parameter = null, float value = 0f)
            => SoundEvents.Enqueue(new PlayerSoundEvent(path, parameter, value));

        public void SetFreezeFramesEnabled(bool enabled)
        {
            FreezeFramesEnabled = enabled;
            if (!enabled) freezeTimer = 0f;
        }

        public void SetDashMode(int mode)
        {
            DashMode = mode < 0 ? -1 : Math.Max(0, Math.Min(2, mode));
            Dashes = DashCapacity;
            hairFlashTimer = 0.12f;
        }

        public void GetHitbox(out float left, out float top, out float right, out float bottom)
            => HitboxAt(Pos.X, Pos.Y, out left, out top, out right, out bottom);

        public void ApplyFreeze(float duration)
        {
            if (FreezeFramesEnabled) freezeTimer = Math.Max(freezeTimer, duration);
        }

        public void Bounce(float fromY)
        {
            MoveVExact((int)(fromY - Pos.Y), false);
            RefillDash();
            Stamina = ClimbMaxStamina;
            State = StNormal;
            jumpGraceTimer = 0f;
            varJumpTimer = .2f;
            autoJump = true;
            dashAttackTimer = 0f;
            gliderBoostTimer = 0f;
            wallSlideTimer = WallSlideTime;
            wallBoostTimer = 0f;
            varJumpSpeed = Speed.Y = -140f;
            SpriteScaleX = .6f;
            SpriteScaleY = 1.4f;
        }

        public void PointBounce(PointF from)
        {
            if (State == StDash) State = StNormal;
            RefillDash();
            Stamina = ClimbMaxStamina;
            PointF vector = SafeNormalize(Center.X - from.X, Center.Y - from.Y);
            if (vector.Y > -.2f && vector.Y <= .4f) vector.Y = -.2f;
            Speed = new PointF(vector.X * 220f * 1.5f, vector.Y * 220f);
            if (Math.Abs(Speed.X) < 100f)
                Speed.X = Speed.X == 0f ? -Facing * 100f : Math.Sign(Speed.X) * 100f;
        }

        public PointF ExplodeLaunch(PointF from)
        {
            ApplyFreeze(.1f);
            PointF vector = SafeNormalize(Center.X - from.X, Center.Y - from.Y, 0f, -1f);
            float dotUp = vector.Y;
            if (dotUp <= -.7f) { vector.X = 0f; vector.Y = -1f; }
            else if (dotUp <= .65f && dotUp >= -.55f) { vector.Y = 0f; vector.X = Math.Sign(vector.X); }
            Speed = new PointF(280f * vector.X, 280f * vector.Y);
            if (Speed.Y <= 50f) { Speed.Y = Math.Min(-150f, Speed.Y); autoJump = true; }
            if (Speed.X != 0f)
            {
                explodeLaunchBoostSpeed = Speed.X * 1.2f;
                if (moveX == Math.Sign(Speed.X)) { Speed.X = explodeLaunchBoostSpeed; explodeLaunchBoostTimer = 0f; }
                else explodeLaunchBoostTimer = .01f;
            }
            RefillDash();
            Stamina = ClimbMaxStamina;
            dashCooldownTimer = .2f;
            bool beginsLaunchState = State != StLaunch;
            State = StLaunch;
            // LaunchBegin sets vanilla's `launched` flag, which emits SpeedRing
            // every 0.15s while speed stays >= 140, for at most 0.5s.
            if (beginsLaunchState) LaunchCount++;
            ExplodeLaunchAngle = (float)Math.Atan2(Speed.Y, Speed.X);
            ExplodeLaunchSequenceCount++;
            return vector;
        }

        public void Die(PointF direction)
        {
            if (IsDead || IsRespawning) return;
            if (Holding != null) DropGlider();
            IsDead = true;
            DeathSequenceCount++;
            DeathColor = HairColor;
            DeathPosition = new PointF(Pos.X, Pos.Y - 5f);
            DeathPercent = 0f;
            deathTimer = 0f;
            Speed = PointF.Empty;
            counter = PointF.Empty;
            dashAimPending = false;
            IsPreDeath = !direction.IsEmpty;
            deathDirection = direction;
            deathBodyStart = DeathBodyPosition = Pos;
            deathPreTimer = 0f;
            DeathBodyScale = IsPreDeath ? 1.5f : 1f;
            DeathBodyRotation = 0f;
            if (IsPreDeath)
            {
                if (Math.Abs(direction.X) > Math.Abs(direction.Y))
                {
                    DeathBodyFrameId = "deadside00";
                    Facing = -Math.Sign(direction.X);
                }
                else
                {
                    float target = Facing > 0 ? (float)Math.PI : 0f;
                    float angle = (float)Math.Atan2(direction.Y, direction.X);
                    float delta = target - angle;
                    while (delta > Math.PI) delta -= (float)Math.PI * 2f;
                    while (delta < -Math.PI) delta += (float)Math.PI * 2f;
                    angle += Math.Max(-.5f, Math.Min(.5f, delta));
                    deathDirection = new PointF((float)Math.Cos(angle), (float)Math.Sin(angle));
                    DeathBodyFrameId = deathDirection.Y < 0f ? "deadup00" : "deaddown00";
                }
                PlaySound("event:/char/madeline/predeath");
            }
            else PlaySound("event:/char/madeline/death");
            freezeTimer = IsPreDeath && FreezeFramesEnabled ? .05f : 0f;
            State = StFrozen;
        }

        public void DieFromSeeker(PointF seekerPosition)
        {
            PointF direction = SafeNormalize(Center.X - seekerPosition.X,
                Center.Y - seekerPosition.Y, -Facing, 0f);
            deathRespawnPos = FindNearbySafeRespawn(seekerPosition, direction);
            Die(direction);
        }

        PointF FindNearbySafeRespawn(PointF threat, PointF away)
        {
            PointF origin = Pos;
            PointF? bestGrounded = null, bestAir = null;
            float bestGroundedScore = float.MaxValue, bestAirScore = float.MaxValue;
            float baseAngle = (float)Math.Atan2(away.Y, away.X);
            // A Celeste room reload chooses a map spawn. Desktop rooms have no map
            // spawns, so choose the closest valid 8x11 placement around the death,
            // preferring the direction away from the killing Seeker and safe ground.
            float[] angleOffsets =
            {
                0f, -(float)Math.PI / 8f, (float)Math.PI / 8f,
                -(float)Math.PI / 4f, (float)Math.PI / 4f,
                -(float)Math.PI * 3f / 8f, (float)Math.PI * 3f / 8f,
                -(float)Math.PI / 2f, (float)Math.PI / 2f,
                (float)Math.PI
            };
            int[] radii = { 40, 48, 56, 64, 80, 96, 112, 128 };
            foreach (int radius in radii)
            foreach (float offset in angleOffsets)
            {
                float angle = baseAngle + offset;
                PointF candidate = new PointF(
                    Math.Max(MinX + 4f, Math.Min(MaxX - 4f,
                        origin.X + (float)Math.Cos(angle) * radius)),
                    origin.Y + (float)Math.Sin(angle) * radius);
                if (CollideAt(candidate.X, candidate.Y, 11f)) continue;
                PointF candidateCenter = new PointF(candidate.X, candidate.Y - 5.5f);
                float tx = candidateCenter.X - threat.X, ty = candidateCenter.Y - threat.Y;
                if (tx * tx + ty * ty < 40f * 40f) continue;
                float ox = candidate.X - origin.X, oy = candidate.Y - origin.Y;
                float score = ox * ox + oy * oy + Math.Abs(offset) * 64f;
                if (CheckGroundAt(candidate.X, candidate.Y))
                {
                    if (score < bestGroundedScore) { bestGroundedScore = score; bestGrounded = candidate; }
                }
                else if (score < bestAirScore) { bestAirScore = score; bestAir = candidate; }
            }
            return bestGrounded ?? bestAir ?? deathRespawnPos;
        }

        static PointF SafeNormalize(float x, float y, float fallbackX = 0f, float fallbackY = 0f)
        {
            float length = (float)Math.Sqrt(x * x + y * y);
            return length > .00001f ? new PointF(x / length, y / length) : new PointF(fallbackX, fallbackY);
        }

        // 计时器
        float jumpGraceTimer;
        float varJumpTimer;
        float varJumpSpeed;
        float dashCooldownTimer;
        float explodeLaunchBoostTimer, explodeLaunchBoostSpeed;
        float dashRefillCooldownTimer;
        float dashAttackTimer;
        float hairFlashTimer;     // 头发闪白计时（Dash 恢复时 0.12s）
        float wallSlideTimer = WallSlideTime;
        int wallSlideDir;
        float forceMoveXTimer;
        int forceMoveX;
        float climbNoMoveTimer;
        int wallBoostDir;         // 攀爬跳助推方向（原作 wallBoostDir）
        float wallBoostTimer;     // 攀爬跳助推计时（原作 wallBoostTimer）
        float dashTime;
        float freezeTimer;
        float jumpBufferTimer;
        float dashBufferTimer;
        float crouchDashBufferTimer;
        bool jumpBufferFresh;
        bool dashBufferFresh;
        bool crouchDashBufferFresh;
        bool dashStartedOnGround;
        PointF beforeDashSpeed;
        PointF pendingDashDir;
        bool dashAimPending;
        bool autoJump;          // 冲刺结束后的自动跳保持（原作 AutoJump：半重力/可变跳高视为按住跳）
        int lastClimbMove;
        bool fastJump;
        float idleTimer;
        string fidgetId;
        int observedIdleLoopCount;
        float highestAirY;
        float landingStumbleTimer;
        float playFootstepOnLand;
        float sweatJumpTimer;
        float minHoldTimer;
        float pickupTimer;
        PointF pickupStoredSpeed;
        float pickupStoredVarJump;
        PointF pickupCurveBegin, pickupCurveControl;
        float dreamDashCanEndTimer;
        float dreamDashAnimTimer, dreamDashOutTimer;
        float dreamTechGraceTimer;
        float throwAnimTimer;
        float gliderBoostTimer;
        PointF gliderBoostDir;
        PointF dreamDashEntryPos;
        Solid dreamDashBlock;
        bool hasDreamDashBlock;
        PointF deathRespawnPos;
        PointF deathDirection, deathBodyStart;
        float deathPreTimer;
        float deathTimer;
        float respawnTimer;
        bool respawnTravels;
        PointF respawnEffectStart;
        PointF respawnTarget;
        float elytraAngle;
        float elytraSpeed;
        int elytraFacing;
        float elytraStableTimer;
        float elytraCooldown;
        bool normalDashRequested;
        bool normalDashWasCrouch;

        const float ElytraStableAngle = 0.2f;
        const float ElytraAngleRange = 2f;
        const float ElytraMinSpeed = 64f;
        const float ElytraMaxSpeed = 320f;
        const float ElytraAccel = 90f;
        const float ElytraDecel = 165f;
        const float ElytraFastDecel = 220f;
        const float ElytraAngleChangeFactor = 480f;
        const float ElytraCooldownTime = 7f / 60f;

        // 保留速度（cornerboost）：撞墙瞬间保存水平速度，时限内墙不再阻挡则返还
        float wallSpeedRetained;
        float wallSpeedRetentionTimer;
        int moveX;
        int hopWaitX;
        float hopWaitXSpeed;

        // 外部数据
        public List<Solid> Solids = new List<Solid>();
        public float MinX = -10000, MaxX = 10000;   // 屏幕边界（游戏像素）
        public bool BeingDragged;

        PointF counter;  // 亚像素位移累积（Actor.movementCounter）
        readonly Random rng = new Random();

        static float Approach(float val, float target, float maxMove)
            => val > target ? Math.Max(val - maxMove, target) : Math.Min(val + maxMove, target);
        static int Sign(float v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        static void AdvanceBuffer(ref float timer, ref bool fresh, float dt)
        {
            if (timer <= 0f) { fresh = false; return; }
            if (fresh) fresh = false;
            else timer -= dt;
        }

        // ===== 碰撞 =====
        float HitH => Ducking ? 6f : 11f;

        void HitboxAt(float x, float y, out float l, out float t, out float r, out float b)
        { l = x - 4; r = x + 4; t = y - HitH; b = y; }

        void HitboxAt(float x, float y, float h, out float l, out float t, out float r, out float b)
        { l = x - 4; r = x + 4; t = y - h; b = y; }

        static bool Overlap(float l, float t, float r, float b, in Solid s)
            => l < s.R && r > s.L && t < s.B && b > s.T;

        bool CollidePoint(float x, float y)
        {
            foreach (var s in Solids)
                if (x >= s.L && x < s.R && y >= s.T && y < s.B) return true;
            return false;
        }

        /// <summary>原始重叠检测（不做内外语义）。</summary>
        bool CollideAt(float x, float y)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var s in Solids)
                if (Overlap(l, t, r, b, s)) return true;
            return false;
        }

        bool DreamAt(float x, float y)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var s in Solids)
                if (s.Dream && Overlap(l, t, r, b, s)) return true;
            return false;
        }

        bool TryGetDreamAt(float x, float y, out Solid dream)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var solid in Solids)
                if (solid.Dream && Overlap(l, t, r, b, solid))
                {
                    dream = solid;
                    return true;
                }
            dream = default;
            return false;
        }

        bool NonDreamAt(float x, float y)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var s in Solids)
                if (!s.Dream && Overlap(l, t, r, b, s)) return true;
            return false;
        }

        bool CollideAt(float x, float y, float h)
        {
            HitboxAt(x, y, h, out float l, out float t, out float r, out float b);
            foreach (var s in Solids)
                if (Overlap(l, t, r, b, s)) return true;
            return false;
        }

        /// <summary>站立检测：必须在某个平台的顶面上（不能在平台内部）。</summary>
        bool CheckGroundAt(float x, float y)
        {
            HitboxAt(x, y + 1, out float l, out float t, out float r, out float b);
            HitboxAt(x, y, out float l0, out float t0, out float r0, out float b0);
            foreach (var s in Solids)
                if (Overlap(l, t, r, b, s) && !Overlap(l0, t0, r0, b0, s)) return true;
            return false;
        }

        bool CheckGround()
        {
            HitboxAt(Pos.X, Pos.Y + 1, out float l, out float t, out float r, out float b);
            HitboxAt(Pos.X, Pos.Y, out float l0, out float t0, out float r0, out float b0);
            foreach (var s in Solids)
                if (Overlap(l, t, r, b, s) && !Overlap(l0, t0, r0, b0, s))
                { GroundId = s.Id; return true; }
            GroundId = IntPtr.Zero;
            return false;
        }

        bool CanUnDuck => CanUnDuckAt(Pos.X, Pos.Y);
        bool CanUnDuckAt(float x, float y) => !CollideAt(x, y, 11f);

        // 原作：上冲时检测距离从 3 扩展到 5
        bool WallJumpCheck(int dir) => CollideAt(Pos.X + dir * (DashAttacking && DashDir.X == 0f && DashDir.Y == -1f ? 5 : 3), Pos.Y);
        bool ClimbCheck(int dir, int yAdd = 0) => CollideAt(Pos.X + dir * 2, Pos.Y + yAdd);
        bool DashAttacking => dashAttackTimer > 0f;  // 冲刺攻击窗口（冲刺结束后 0.3s 内）
        // 上冲撞墙判定：|X|≤0.2 且 Y≤-0.75 → SuperWallJump
        bool SuperWallJumpAngleCheck => Math.Abs(DashDir.X) <= 0.2f && DashDir.Y <= -0.75f;

        // ===== 移动（移植 Actor.MoveH/MoveV：亚像素累积 + 整数步进）=====
        public void MoveH(float dx)
        {
            counter.X += dx;
            int n = (int)Math.Round(counter.X, MidpointRounding.ToEven);
            if (n == 0) return;
            counter.X -= n;
            MoveHExact(n);
        }

        public void MoveV(float dy)
        {
            counter.Y += dy;
            int n = (int)Math.Round(counter.Y, MidpointRounding.ToEven);
            if (n == 0) return;
            counter.Y -= n;
            MoveVExact(n);
        }

        void MoveHExact(int n, bool notifyCollision = true)
        {
            int sign = Math.Sign(n);
            while (n != 0)
            {
                if (State == StDreamDash)
                {
                    Pos.X += sign;
                    n -= sign;
                    continue;
                }
                HitboxAt(Pos.X, Pos.Y, out float l0, out float t0, out float r0, out float b0);
                HitboxAt(Pos.X + sign, Pos.Y, out float l, out float t, out float r, out float b);
                bool blocked = false;
                foreach (var s in Solids)
                {
                    // 「从外侧撞上才算墙」：已在内部的平台不阻挡（防止被窗口吞掉）
                    if (Overlap(l, t, r, b, s) && !Overlap(l0, t0, r0, b0, s)) { blocked = true; break; }
                }
                if (blocked)
                {
                    counter.X = 0;
                    if (notifyCollision) OnCollideH(sign);
                    return; // Actor.MoveH discards the blocked movement remainder.
                }
                Pos.X += sign;
                n -= sign;
            }
        }

        void MoveVExact(int n, bool notifyCollision = true)
        {
            int sign = Math.Sign(n);
            while (n != 0)
            {
                if (State == StDreamDash)
                {
                    Pos.Y += sign;
                    n -= sign;
                    continue;
                }
                HitboxAt(Pos.X, Pos.Y, out float l0, out float t0, out float r0, out float b0);
                HitboxAt(Pos.X, Pos.Y + sign, out float l, out float t, out float r, out float b);
                bool blocked = false;
                foreach (var s in Solids)
                {
                    if (Overlap(l, t, r, b, s) && !Overlap(l0, t0, r0, b0, s)) { blocked = true; break; }
                }
                if (blocked)
                {
                    counter.Y = 0;
                    if (notifyCollision) OnCollideV(sign);
                    return; // Actor.MoveV discards the blocked movement remainder.
                }
                Pos.Y += sign;
                n -= sign;
            }
        }

        bool TryEnterDreamDash(float x, float y, int axisX, int axisY)
        {
            // DreamDashCheck is keyed to DashAttacking, not StDash.  Pickup can
            // return to Normal while the 0.3s attack window is still active; that
            // is the vanilla interaction that makes jelly smuggling possible.
            if (!DashAttacking || !DreamAt(x, y)) return false;
            if ((axisX != 0 && Sign(DashDir.X) != axisX) ||
                (axisY != 0 && Sign(DashDir.Y) != axisY)) return false;

            // DreamDashCheck excludes the DreamBlock itself when checking for a
            // second Solid and performs a perpendicular 1..4px corner correction.
            if (NonDreamAt(x, y))
            {
                float perpendicularX = Math.Abs(axisY);
                float perpendicularY = Math.Abs(axisX);
                float otherSpeed = axisX != 0 ? Speed.Y : Speed.X;
                bool corrected = false;
                if (otherSpeed <= 0f)
                    for (int i = -1; i >= -4; i--)
                    {
                        float cx = x + perpendicularX * i, cy = y + perpendicularY * i;
                        if (!NonDreamAt(cx, cy))
                        {
                            Pos = new PointF(Pos.X + perpendicularX * i, Pos.Y + perpendicularY * i);
                            corrected = true;
                            break;
                        }
                    }
                if (!corrected && otherSpeed >= 0f)
                    for (int i = 1; i <= 4; i++)
                    {
                        float cx = x + perpendicularX * i, cy = y + perpendicularY * i;
                        if (!NonDreamAt(cx, cy))
                        {
                            Pos = new PointF(Pos.X + perpendicularX * i, Pos.Y + perpendicularY * i);
                            corrected = true;
                            break;
                        }
                    }
                if (!corrected) return false;
            }

            dreamDashEntryPos = Pos;
            hasDreamDashBlock = TryGetDreamAt(x, y, out dreamDashBlock);
            State = StDreamDash;
            dreamDashCanEndTimer = 0.1f;
            dreamDashAnimTimer = 0.16f;
            dreamTechGraceTimer = 0f;
            Speed = new PointF(DashDir.X * DashSpeed, DashDir.Y * DashSpeed);
            Stamina = ClimbMaxStamina;
            dashAttackTimer = 0f;
            gliderBoostTimer = 0f;
            PlaySound("event:/char/madeline/dreamblock_enter");
            return true;
        }

        bool TryGetDreamExitWall(int direction, out Solid wall)
        {
            wall = default;
            if (direction == 0) return false;
            HitboxAt(Pos.X, Pos.Y, out _, out float top, out _, out float bottom);
            float bestDistance = float.MaxValue;
            bool found = false;
            foreach (Solid solid in Solids)
            {
                if (!solid.Dream || top >= solid.B || bottom <= solid.T) continue;
                float distance = direction > 0
                    ? Pos.X - 4f - solid.R
                    : solid.L - (Pos.X + 4f);
                // NaiveMove can overshoot the face by at most one 240px/s frame.
                if (distance < -0.01f || distance > 8f || distance >= bestDistance) continue;
                bestDistance = distance;
                wall = solid;
                found = true;
            }
            return found;
        }

        bool OnCollideH(int sign)
        {
            // CommunalHelper consumes Elytra collision callbacks without changing
            // velocity; GlideUpdate chooses the next state on the following tick.
            if (State == StElytra) return false;

            // Vanilla turns a grounded horizontal dash into a duck when the
            // crouched hitbox fits one pixel ahead.  The collision still stops
            // movement for this frame, but does not kill dash speed/attack.
            if (State == StDash && onGround && !CollideAt(Pos.X + sign, Pos.Y, 6f))
            {
                Ducking = true;
                return false;
            }

            // 冲刺水平撞墙：垂直角修正 ±1..4（优先向下贴地）
            if (State == StDash && Speed.Y == 0f && Speed.X != 0f)
            {
                for (int i = 1; i <= DashCornerCorrection; i++)
                {
                    for (int d = 1; d >= -1; d -= 2)
                    {
                        float nx = Pos.X + sign, ny = Pos.Y + i * d;
                        if (!CollideAt(nx, ny) && CollideAt(nx, ny - d))
                        {
                            MoveVExact(i * d, false);
                            MoveHExact(sign, false);
                            return true;
                        }
                    }
                }
            }
            if (TryEnterDreamDash(Pos.X + sign, Pos.Y, sign, 0)) return true;
            if (wallSpeedRetentionTimer <= 0f)
            {
                wallSpeedRetained = Speed.X;
                wallSpeedRetentionTimer = WallSpeedRetentionTime;
            }
            Speed.X = 0;
            dashAttackTimer = 0f;
            gliderBoostTimer = 0f;
            return false;
        }

        bool OnCollideV(int sign)
        {
            if (State == StElytra) return false;

            if (sign > 0)
            {
                // 空中冲刺落地：水平角修正 ±1..4，滑上平台边缘（仅冲刺状态，原作同款）
                if (State == StDash && !dashStartedOnGround)
                {
                    if (Speed.X <= 0.01f)
                        for (int n = -1; n >= -DashCornerCorrection; n--)
                            if (!CheckGroundAt(Pos.X + n, Pos.Y)) { MoveHExact(n, false); MoveVExact(1, false); return true; }
                    if (Speed.X >= -0.01f)
                        for (int n = 1; n <= DashCornerCorrection; n++)
                            if (!CheckGroundAt(Pos.X + n, Pos.Y)) { MoveHExact(n, false); MoveVExact(1, false); return true; }
                }
                if (TryEnterDreamDash(Pos.X, Pos.Y + sign, 0, sign)) return true;
                // 斜下冲刺落地 → wavedash / 凌波微步：转为蹲姿地面冲刺（原作状态无关，条件同款）
                // 落地瞬间按 C → hyper 325 弹射；这就是「空中斜下冲 + 落地跳」的核心
                if (DashDir.X != 0 && DashDir.Y > 0 && Speed.Y > 0)
                {
                    WavedashCount++;
                    DashDir = new PointF(Sign(DashDir.X), 0);
                    Speed.Y = 0;
                    Speed.X *= 1.2f;
                    Ducking = true;
                }
                if (State != StClimb)
                {
                    float amount = Math.Min(Speed.Y / FastMaxFall, 1f);
                    SpriteScaleX = 1f + 0.6f * amount;
                    SpriteScaleY = 1f - 0.6f * amount;
                    PlaySound(playFootstepOnLand > 0f
                        ? "event:/char/madeline/footstep"
                        : "event:/char/madeline/landing", "surface_index",
                        GroundSurfaceSoundIndex);
                    if (Speed.Y >= 80f) LandingEffectCount++;
                    playFootstepOnLand = 0f;
                    if (highestAirY < Pos.Y - 50f && Speed.Y >= MaxFall && Math.Abs(Speed.X) >= MaxRun)
                        landingStumbleTimer = 0.7f;
                }
                // Vanilla's vertical collision callback clears DashAttacking after
                // processing the landing (unless corner correction returned early).
                dashAttackTimer = 0f;
                gliderBoostTimer = 0f;
                Speed.Y = 0;
                return false;
            }
            else
            {
                // 撞天花板：向上角修正（上冲时扩展为 5px）
                int upCorner = DashAttacking && Math.Abs(Speed.X) < 0.01f
                    ? DashingUpwardCornerCorrection : UpwardCornerCorrection;
                if (Speed.X <= 0.01f)
                    for (int i = 1; i <= upCorner; i++)
                        if (!CollideAt(Pos.X - i, Pos.Y - 1))
                        { Pos.X -= i; Pos.Y -= 1; return true; }
                if (Speed.X >= -0.01f)
                    for (int i = 1; i <= upCorner; i++)
                        if (!CollideAt(Pos.X + i, Pos.Y - 1))
                        { Pos.X += i; Pos.Y -= 1; return true; }
                if (TryEnterDreamDash(Pos.X, Pos.Y + sign, 0, sign)) return true;
                Speed.Y = 0;
                gliderBoostTimer = 0f;
                // 原作：撞天花板取消可变跳高（防止撞头后仍保持低重力弧线）
                if (varJumpTimer < 0.15f) varJumpTimer = 0;
                return false;
            }
        }

        // ===== 输入缓冲 =====
        public void BufferJump()
        {
            jumpBufferTimer = 0.1f;
            jumpBufferFresh = true;
        }
        public void BufferDash(bool crouchDash = false)
        {
            if (crouchDash)
            {
                crouchDashBufferTimer = 0.08f;
                crouchDashBufferFresh = true;
            }
            else
            {
                dashBufferTimer = 0.08f;
                dashBufferFresh = true;
            }
        }
        public bool HasJumpBuffer => jumpBufferTimer > 0;
        public bool HasDashBuffer => dashBufferTimer > 0 || crouchDashBufferTimer > 0;
        void ConsumeJump() { jumpBufferTimer = 0; jumpBufferFresh = false; }
        void ConsumeDash()
        {
            dashBufferTimer = crouchDashBufferTimer = 0;
            dashBufferFresh = crouchDashBufferFresh = false;
        }

        /// <summary>重置到指定位置：清空速度/状态/冲刺/体力/计时器，头发复位。</summary>
        public void ResetTo(PointF pos)
        {
            if (Holding != null)
            {
                Holding.Release(PointF.Empty, Solids);
                Holding = null;
            }
            Pos = pos;
            deathRespawnPos = pos;
            Speed = new PointF(0, 0);
            SpriteScaleX = SpriteScaleY = 1f;
            State = StNormal;
            Dashes = DashCapacity;
            Stamina = ClimbMaxStamina;
            Ducking = false;
            onGround = false;
            GroundId = IntPtr.Zero;
            BeingDragged = false;
            jumpBufferTimer = 0;
            dashBufferTimer = 0;
            crouchDashBufferTimer = 0;
            jumpBufferFresh = dashBufferFresh = crouchDashBufferFresh = false;
            dashCooldownTimer = 0;
            explodeLaunchBoostTimer = explodeLaunchBoostSpeed = 0f;
            dashRefillCooldownTimer = 0;
            dashAttackTimer = 0;
            dashTime = 0;
            jumpGraceTimer = 0;
            varJumpTimer = 0;
            varJumpSpeed = 0;
            wallSlideTimer = WallSlideTime;
            wallSlideDir = 0;
            forceMoveXTimer = 0;
            forceMoveX = 0;
            climbNoMoveTimer = 0;
            wallBoostDir = 0;
            wallBoostTimer = 0;
            moveX = 0;
            hopWaitX = 0;
            hopWaitXSpeed = 0f;
            freezeTimer = 0;
            dashAimPending = false;
            pendingDashDir = new PointF(0, 0);
            hairFlashTimer = 0;
            wallSpeedRetained = 0;
            wallSpeedRetentionTimer = 0;
            maxFall = MaxFall;
            fastJump = false;
            idleTimer = 0f;
            fidgetId = null;
            observedIdleLoopCount = 0;
            highestAirY = pos.Y;
            landingStumbleTimer = 0f;
            playFootstepOnLand = 0f;
            SweatAnimId = "idle";
            sweatJumpTimer = 0f;
            minHoldTimer = 0f;
            pickupTimer = 0f;
            pickupStoredSpeed = PointF.Empty;
            pickupStoredVarJump = 0f;
            pickupCurveBegin = pickupCurveControl = PointF.Empty;
            dreamDashCanEndTimer = 0f;
            dreamDashAnimTimer = dreamDashOutTimer = 0f;
            dreamTechGraceTimer = 0f;
            throwAnimTimer = 0f;
            gliderBoostTimer = 0f;
            gliderBoostDir = PointF.Empty;
            dreamDashEntryPos = pos;
            hasDreamDashBlock = false;
            deathTimer = 0f;
            respawnTimer = 0f;
            IsDead = false;
            IsPreDeath = false;
            DeathBodyFrameId = null;
            DeathBodyScale = 1f;
            DeathBodyRotation = 0f;
            DeathPercent = 0f;
            DeathPosition = pos;
            RespawnPercent = 0f;
            RespawnEffectPosition = pos;
            counter.X = counter.Y = 0;
            Hair.Reset(new PointF(Pos.X, Pos.Y - 9), Facing);
        }

        void BeginRespawn()
        {
            PointF effectStart = DeathPosition;
            PointF target = deathRespawnPos;
            ResetTo(target);
            respawnTravels = RespawnReversalEnabled;
            State = StIntroRespawn;
            respawnTimer = 0f;
            respawnEffectStart = effectStart;
            respawnTarget = target;
            if (respawnTravels)
            {
                Pos = new PointF(effectStart.X, effectStart.Y + 5f);
                RespawnEffectPosition = effectStart;
            }
            else
                RespawnEffectPosition = new PointF(target.X, target.Y - 5f);
            RespawnPercent = 1f;
            RespawnColor = DashCapacity > 1
                ? (PetWindow.Instance?.ResolveHairColor(2, TwoDashesHairColor) ?? TwoDashesHairColor)
                : (PetWindow.Instance?.ResolveHairColor(1, NormalHairColor) ?? NormalHairColor);
            HairColor = RespawnColor;
            PlaySound("event:/char/madeline/revive");
        }

        public void WrapBy(float x, float y)
        {
            if (x == 0f && y == 0f) return;
            Pos = new PointF(Pos.X + x, Pos.Y + y);
            Hair.MoveBy(x, y);
            if (Holding != null)
                Holding.Carry(new PointF(Holding.Pos.X + x, Holding.Pos.Y + y));
        }

        bool CanDash => (dashBufferTimer > 0 || crouchDashBufferTimer > 0) &&
                        dashCooldownTimer <= 0 && Dashes > 0 && !BeingDragged;
        bool IsTired => IsLowStamina;

        public void RefillDash()
        {
            Dashes = DashCapacity;
            hairFlashTimer = 0.12f;  // 原作：闪白 0.12s 后直接切回红色
            HairColor = FlashHairColor;
        }

        // ===== 主更新 =====
        public void Update(float dt, PetInput input)
        {
            if (IsDead)
            {
                if (IsPreDeath)
                {
                    if (freezeTimer > 0f) { freezeTimer -= dt; return; }
                    deathPreTimer += dt;
                    float progress = Math.Min(1f, deathPreTimer / .5f);
                    float eased = 1f - (float)Math.Pow(1f - progress, 3f);
                    DeathBodyPosition = new PointF(
                        deathBodyStart.X + deathDirection.X * 24f * eased,
                        deathBodyStart.Y + deathDirection.Y * 24f * eased);
                    DeathBodyScale = 1.5f - eased * .5f;
                    DeathBodyRotation = (float)Math.Floor(eased * 4f) * (float)Math.PI * 2f;
                    DeathBodyFrameId = DeathBodyFrameId.TrimEnd('0', '1') +
                        (((int)(deathPreTimer / .1f) & 1) == 0 ? "00" : "01");
                    if (deathPreTimer >= .375f)
                    {
                        IsPreDeath = false;
                        DeathPosition = new PointF(DeathBodyPosition.X, DeathBodyPosition.Y - 5f);
                        deathTimer = 0f;
                        PlaySound("event:/char/madeline/death");
                    }
                    return;
                }
                deathTimer += dt;
                DeathPercent = Math.Min(1f, deathTimer / 0.834f);
                // PlayerDeadBody begins the room reload after 65% of DeathEffect's
                // 0.834s duration; the remaining effect is covered by the wipe.
                if (deathTimer >= 0.834f * 0.65f) BeginRespawn();
                return;
            }
            if (State == StIntroRespawn)
            {
                // Player.IntroRespawn contracts a reversed DeathEffect at the
                // newly loaded player's position for 0.6 seconds.
                respawnTimer += dt;
                float progress = Math.Min(1f, respawnTimer / 0.6f);
                if (respawnTravels)
                {
                    RespawnEffectPosition = new PointF(
                        respawnEffectStart.X + (respawnTarget.X - respawnEffectStart.X) * progress,
                        respawnEffectStart.Y + (respawnTarget.Y - 5f - respawnEffectStart.Y) * progress);
                    Pos = new PointF(RespawnEffectPosition.X, RespawnEffectPosition.Y + 5f);
                }
                RespawnPercent = 1f - progress;
                if (progress >= 1f)
                {
                    Pos = respawnTarget;
                    State = StNormal;
                    SpriteScaleX = 1.5f;
                    SpriteScaleY = 0.5f;
                    RespawnPercent = 0f;
                    Hair.Reset(new PointF(Pos.X, Pos.Y - 9f), Facing);
                }
                return;
            }
            if (InfiniteStamina) Stamina = ClimbMaxStamina;

            // Celeste.Freeze halts Player.Update. Only advance the raw freeze here;
            // gameplay timers and the dash aim remain locked until it ends.
            if (freezeTimer > 0)
            {
                freezeTimer -= dt;
                // Input still updates during Celeste.Freeze with DeltaTime == 0.
                // A press made during the freeze is no longer "new" when the
                // first non-frozen frame advances its buffer timer.
                jumpBufferFresh = dashBufferFresh = crouchDashBufferFresh = false;
                return;
            }

            // DashBegin clears movement during the freeze; DashCoroutine applies the
            // direction that was sampled on the press frame once gameplay resumes.
            if (dashAimPending)
            {
                dashAimPending = false;
                ApplyDashAim();
            }

            idleTimer += dt;
            if (Speed.X != 0f || Speed.Y != 0f) idleTimer = 0f;

            // Per-frame timers.  The wall-slide flag describes the previous
            // frame until this point, exactly like Player.Update in vanilla.
            if (wallSlideDir != 0)
            {
                wallSlideTimer = Math.Max(0f, wallSlideTimer - dt);
                wallSlideDir = 0;
            }
            AdvanceBuffer(ref jumpBufferTimer, ref jumpBufferFresh, dt);
            AdvanceBuffer(ref dashBufferTimer, ref dashBufferFresh, dt);
            AdvanceBuffer(ref crouchDashBufferTimer, ref crouchDashBufferFresh, dt);
            if (dashCooldownTimer > 0) dashCooldownTimer -= dt;
            if (explodeLaunchBoostTimer > 0f)
            {
                if (input.MoveX == Math.Sign(explodeLaunchBoostSpeed))
                {
                    Speed.X = explodeLaunchBoostSpeed;
                    explodeLaunchBoostTimer = 0f;
                }
                else explodeLaunchBoostTimer -= dt;
            }
            bool dashRefillReady = dashRefillCooldownTimer <= 0f;
            if (!dashRefillReady) dashRefillCooldownTimer -= dt;
            if (InfiniteDash && dashRefillReady && Dashes < DashCapacity)
                RefillDash();
            if (dashAttackTimer > 0) dashAttackTimer -= dt;
            if (gliderBoostTimer > 0f) gliderBoostTimer -= dt;
            if (hairFlashTimer > 0) hairFlashTimer -= dt;  // 头发闪白计时
            if (varJumpTimer > 0) varJumpTimer -= dt;
            if (sweatJumpTimer > 0f)
            {
                sweatJumpTimer -= dt;
                if (sweatJumpTimer <= 0f) SweatAnimId = "idle";
            }
            if (minHoldTimer > 0f) minHoldTimer -= dt;
            if (dreamDashAnimTimer > 0f) dreamDashAnimTimer -= dt;
            if (dreamDashOutTimer > 0f) dreamDashOutTimer -= dt;
            if (dreamTechGraceTimer > 0f) dreamTechGraceTimer -= dt;
            if (throwAnimTimer > 0f) throwAnimTimer -= dt;

            onGround = !BeingDragged && CheckGround();

            if (onGround) highestAirY = Pos.Y;
            else highestAirY = Math.Min(highestAirY, Pos.Y);
            if (landingStumbleTimer > 0f) landingStumbleTimer -= dt;
            if (playFootstepOnLand > 0f) playFootstepOnLand -= dt;

            if (onGround)
            {
                dreamTechGraceTimer = 0f;
                Stamina = ClimbMaxStamina;
                wallSlideTimer = WallSlideTime;  // 原作：着地即重置滑墙时间
                if (State != StClimb) autoJump = false;
                if (dashRefillReady && Dashes < DashCapacity) RefillDash();
                jumpGraceTimer = JumpGraceTime;
            }
            else if (jumpGraceTimer > 0) jumpGraceTimer -= dt;

            // Vanilla tests wallBoost against the prior sampled moveX, then
            // samples this frame's forced/raw horizontal input for the state machine.
            if (wallBoostTimer > 0f)
            {
                wallBoostTimer -= dt;
                if (moveX == wallBoostDir)
                {
                    Speed.X = WallJumpHSpeed * moveX;
                    if (!InfiniteStamina) Stamina += ClimbJumpCost;
                    wallBoostTimer = 0f;
                    SweatAnimId = "idle";
                }
            }
            if (forceMoveXTimer > 0f)
            {
                forceMoveXTimer -= dt;
                moveX = forceMoveX;
            }
            else
            {
                moveX = input.MoveX;
            }

            // Vanilla updates Facing in both Normal (0) and Dash (2); only Climb
            // (1) is excluded among the states implemented here.  Updating it
            // during Dash is what makes reverse supers, hypers and wavedashes
            // possible: SuperJump launches in Facing, not in DashDir.
            if (!BeingDragged && moveX != 0 && (State == StNormal || State == StDash))
            {
                if (Facing != moveX && Ducking)
                {
                    SpriteScaleX = 0.8f;
                    SpriteScaleY = 1.2f;
                }
                Facing = moveX;
            }

            if (!BeingDragged)
            {
                bool pickupCompletedThisFrame = false;
                if (State == StPickup)
                {
                    pickupTimer -= dt;
                    if (Holding != null)
                    {
                        float progress = Math.Max(0f, Math.Min(1f, 1f - pickupTimer / 0.16f));
                        float eased = progress < 0.5f
                            ? 4f * progress * progress * progress
                            : 1f - (float)Math.Pow(-2f * progress + 2f, 3) / 2f;
                        float inv = 1f - eased;
                        PointF offset = new PointF(
                            inv * inv * pickupCurveBegin.X + 2f * inv * eased * pickupCurveControl.X,
                            inv * inv * pickupCurveBegin.Y + 2f * inv * eased * pickupCurveControl.Y + eased * eased * -12f);
                        Holding.Carry(new PointF(Pos.X + offset.X, Pos.Y + offset.Y));
                    }
                    if (pickupTimer <= 0f)
                    {
                        Speed = pickupStoredSpeed;
                        Speed.Y = Math.Min(Speed.Y, 0f);
                        varJumpTimer = pickupStoredVarJump;
                        if (Holding != null)
                        {
                            if (gliderBoostTimer > 0f && gliderBoostDir.Y < 0f)
                            {
                                gliderBoostTimer = 0f;
                                Speed.Y = Math.Min(Speed.Y, -DashSpeed * Math.Abs(gliderBoostDir.Y));
                            }
                            else if (Speed.Y < 0f)
                                Speed.Y = Math.Min(Speed.Y, JumpSpeed);
                        }
                        EnterNormal();
                        pickupCompletedThisFrame = true;
                    }
                }
                if (State != StPickup)
                {
                // StateMachine's pickup coroutine completes during base.Update in
                // vanilla. Actor movement then runs later in that same frame, but
                // NormalUpdate does not. That one frame is what preserves the last
                // dash-attack leniency needed for dream smuggling.
                if (!pickupCompletedThisFrame)
                {
                // Celeste updates retained wall speed before its StateMachine
                // component.  This ordering matters for corner boosts: the
                // restored dash speed must be visible to ClimbJump/Jump.
                if (wallSpeedRetentionTimer > 0f)
                {
                    int rs = Math.Sign(wallSpeedRetained);
                    if (Math.Sign(Speed.X) == -rs)
                        wallSpeedRetentionTimer = 0f;
                    else if (!CollideAt(Pos.X + rs, Pos.Y))
                    {
                        Speed.X = wallSpeedRetained;
                        wallSpeedRetentionTimer = 0f;
                    }
                    else
                        wallSpeedRetentionTimer -= dt;
                }

                if (hopWaitX != 0)
                {
                    if (Math.Sign(Speed.X) == -hopWaitX || Speed.Y > 0f)
                        hopWaitX = 0;
                    else if (!CollideAt(Pos.X + hopWaitX, Pos.Y))
                    {
                        Speed.X = hopWaitXSpeed;
                        hopWaitX = 0;
                    }
                }

                switch (State)
                {
                    case StNormal:
                        normalDashRequested = false;
                        elytraCooldown = Approach(elytraCooldown, 0f, dt);
                        NormalUpdate(dt, input);
                        TryDeployElytra(input);
                        // StateMachine applies NormalUpdate's returned state only
                        // after the CommunalHelper hook has had a chance to replace
                        // it with Elytra. Do not run DashBegin when Elytra wins.
                        if (normalDashRequested && State != StElytra)
                            State = BeginDash(input, normalDashWasCrouch);
                        break;
                    case StClimb: ClimbUpdate(dt, input); break;
                    case StDash: DashUpdate(dt, input); break;
                    case StDreamDash: DreamDashUpdate(dt, input); break;
                    case StElytra: ElytraUpdate(dt, input); break;
                    case StLaunch: LaunchUpdate(dt, input); break;
                }

                // Vanilla releases the duck hitbox while falling once standing
                // space is available (except during climb).
                if (Speed.Y > 0f && CanUnDuck && !onGround && jumpGraceTimer <= 0f && State != StClimb)
                    Ducking = false;
                }

                // Player.orig_Update tests the current state separately before
                // each axis. Exiting DreamDash therefore gets normal movement in
                // the exit frame, while entering it on H suppresses the V move.
                if (State != StDreamDash && !IsDead)
                    MoveH(Speed.X * dt);
                if (State != StDreamDash && !IsDead)
                    MoveV(Speed.Y * dt);

                if (Holding != null)
                    Holding.Carry(new PointF(Pos.X, Pos.Y - 12f +
                        (PetWindow.Instance?.ResolveCarryYOffset(CurrentFrameId) ?? 0f)));

                // Vanilla skips level-bound enforcement during DreamDash. The
                // desktop perimeter is represented by real non-dream solids, so
                // allowing the naive move to reach them produces the proper death.
                if (State != StDreamDash)
                {
                    if (Pos.X < MinX + 4) { Pos.X = MinX + 4; if (Speed.X < 0) Speed.X = 0; }
                    if (Pos.X > MaxX - 4) { Pos.X = MaxX - 4; if (Speed.X > 0) Speed.X = 0; }
                }
                }
            }
            else
            {
                Speed = new PointF(0, 0);
                wallSlideDir = 0;
            }

            // 表情恢复（原作 1.75/s）
            SpriteScaleX = Approach(SpriteScaleX, 1f, 1.75f * dt);
            SpriteScaleY = Approach(SpriteScaleY, 1f, 1.75f * dt);

            // 头发颜色：原作 0.12s 闪白后 → 瞬间切红；Dash=0 时 → 6/s 渐变到蓝
            if (hairFlashTimer > 0)
            {
                HairColor = FlashHairColor;  // 闪白期间保持白色
            }
            else if (Dashes >= 2)
            {
                HairColor = PetWindow.Instance?.ResolveHairColor(2, TwoDashesHairColor) ?? TwoDashesHairColor;
            }
            else if (Dashes > 0 || DashCapacity == 0)
            {
                HairColor = PetWindow.Instance?.ResolveHairColor(1, NormalHairColor) ?? NormalHairColor;
            }
            else
            {
                // 没 Dash 时渐变到蓝色（6/s）
                Color target = PetWindow.Instance?.ResolveHairColor(0, UsedHairColor) ?? UsedHairColor;
                float k = Math.Min(1f, 6f * dt);
                HairColor = Color.FromArgb(
                    (int)(HairColor.R + (target.R - HairColor.R) * k),
                    (int)(HairColor.G + (target.G - HairColor.G) * k),
                    (int)(HairColor.B + (target.B - HairColor.B) * k));
            }

            UpdateSprite(dt, input);

            // PetWindow runs PlayerHair.AfterUpdate after it applies the animation
            // selected above, matching Player.UpdateSprite -> UpdateHair ordering.
        }

        /// <summary>头发编辑器专用：物理/动画冻结，只按给定 hx/hy 跑头发模拟（实时预览）。</summary>
        public void UpdateHairOnly(float dt, float hx, float hy)
        {
            float anchorY = -9f * SpriteScaleY;
            Hair.AfterUpdate(dt, new PointF(Pos.X + hx * Facing, Pos.Y + anchorY + hy), Facing, Dashes > 1);
        }

        void LaunchUpdate(float dt, PetInput input)
        {
            if (CanDash)
            {
                State = BeginDash(input, crouchDashBufferTimer > 0f);
                return;
            }
            if (Holding == null && input.GrabHeld && !IsTired && !Ducking && TryPickupGlider()) return;
            Speed.Y = Approach(Speed.Y, 160f, (Speed.Y < 0f ? 450f : 225f) * dt);
            Speed.X = Approach(Speed.X, 0f, 200f * dt);
            if (Math.Sqrt(Speed.X * Speed.X + Speed.Y * Speed.Y) < 220f) State = StNormal;
        }

        // ===== 普通状态 =====
        void NormalUpdate(float dt, PetInput input)
        {
            if (Holding == null && input.GrabHeld && !IsTired && !Ducking && TryPickupGlider())
            {
                Ducking = false;
                return;
            }

            // 抓墙进入攀爬
            if (Holding == null && input.GrabHeld && !IsTired && !Ducking &&
                Speed.Y >= 0 && Sign(Speed.X) != -Facing)
            {
                if (ClimbCheck(Facing))
                {
                    EnterClimb();
                    State = StClimb;
                    return;
                }
                if (input.MoveY < 1)
                {
                    for (int i = 1; i <= 2; i++)
                    {
                        if (!CollideAt(Pos.X, Pos.Y - i) && ClimbCheck(Facing, -i))
                        {
                            MoveVExact(-i, false);
                            EnterClimb();
                            State = StClimb;
                            return;
                        }
                    }
                }
            }
            if (CanDash)
            {
                normalDashWasCrouch = ConsumeDashRequest();
                normalDashRequested = true;
                return;
            }

            if (Holding != null && !input.GrabHeld && minHoldTimer <= 0f)
            {
                if (input.MoveY == 1) DropGlider();
                else ThrowGlider();
            }

            // 蹲下 / 起身
            if (Holding != null)
            {
                if (!Ducking && onGround && input.MoveY == 1 && Speed.Y >= 0f)
                {
                    DropGlider();
                    Ducking = true;
                    SpriteScaleX = 1.4f; SpriteScaleY = 0.6f;
                }
                else if (onGround && Ducking && CanUnDuck)
                    Ducking = false;
            }
            else if (Ducking)
            {
                if (onGround && input.MoveY != 1 && CanUnDuck)
                {
                    Ducking = false;
                    SpriteScaleX = 0.8f; SpriteScaleY = 1.2f;
                }
                else if (onGround && input.MoveY != 1 && Speed.X == 0f)
                {
                    // DuckCorrectCheck / DuckCorrectSlide: ease sideways out of
                    // a low ceiling when standing space exists nearby.
                    for (int i = 4; i > 0; i--)
                    {
                        if (CanUnDuckAt(Pos.X + i, Pos.Y))
                        {
                            MoveH(50f * dt);
                            break;
                        }
                        if (CanUnDuckAt(Pos.X - i, Pos.Y))
                        {
                            MoveH(-50f * dt);
                            break;
                        }
                    }
                }
            }
            else if (onGround && input.MoveY == 1 && Speed.Y >= 0)
            {
                Ducking = true;
                SpriteScaleX = 1.4f; SpriteScaleY = 0.6f;
            }

            // 水平移动
            if (Ducking && onGround)
            {
                Speed.X = Approach(Speed.X, 0, DuckFriction * dt);
            }
            else
            {
                float mult = onGround ? 1f : AirMult;
                float maxRun = Holding != null && !onGround ? 108.00001f : MaxRun;
                if (Holding != null && !onGround) mult *= 0.5f;
                if (Math.Abs(Speed.X) > maxRun && Sign(Speed.X) == moveX)
                    Speed.X = Approach(Speed.X, maxRun * moveX, RunReduce * mult * dt);
                else
                    Speed.X = Approach(Speed.X, maxRun * moveX, RunAccel * mult * dt);
            }

            // 最大下落速度
            if (Holding != null)
            {
                float gliderTarget = input.MoveY > 0 ? 120f : input.MoveY < 0 ? 24f : 40f;
                maxFall = Approach(maxFall, gliderTarget, FastMaxAccel * dt);
            }
            else
            {
                maxFall = (input.MoveY == 1 && Speed.Y >= MaxFall)
                    ? Approach(maxFall, FastMaxFall, FastMaxAccel * dt)
                    : Approach(maxFall, MaxFall, FastMaxAccel * dt);
            }
            // 快速下落拉伸（原作：Speed.Y > 200 → 渐变到 0.5x1.5）
            if (input.MoveY == 1 && Speed.Y >= MaxFall)
            {
                float stretchThreshold = MaxFall + (FastMaxFall - MaxFall) * 0.5f;  // 200
                if (Speed.Y >= stretchThreshold)
                {
                    float stretchAmount = Math.Min(1f, (Speed.Y - stretchThreshold) / (FastMaxFall - stretchThreshold));
                    SpriteScaleX = 1f - 0.5f * stretchAmount;  // Lerp(1, 0.5)
                    SpriteScaleY = 1f + 0.5f * stretchAmount;  // Lerp(1, 1.5)
                }
            }

            if (!onGround)
            {
                float target = maxFall;
                wallSlideDir = 0;
                if (Holding == null && (moveX == Facing || (moveX == 0 && input.GrabHeld)) && input.MoveY != 1 &&
                    Speed.Y >= 0 && wallSlideTimer > 0 && CanUnDuck && CollideAt(Pos.X + Facing, Pos.Y))
                {
                    Ducking = false;
                    wallSlideDir = Facing;
                    // The player is pinned against the wall.  Do not leave the one-frame
                    // air-acceleration velocity in Speed.X while the subpixel counter waits
                    // to round to a whole collision pixel (10.83 px/s at 60 Hz).
                    Speed.X = 0f;
                    counter.X = 0f;
                    // 滑墙中按抓取 → 自动进入攀爬（原作 ClimbTrigger）
                    if (input.GrabHeld && !IsTired) { EnterClimb(); State = StClimb; return; }
                    target = 160f + (20f - 160f) * (wallSlideTimer / WallSlideTime);
                }
                float gravMult = (Math.Abs(Speed.Y) < HalfGravThreshold && (input.JumpHeld || autoJump)) ? 0.5f : 1f;
                if (Holding != null) gravMult *= 0.5f;
                Speed.Y = Approach(Speed.Y, target, Gravity * gravMult * dt);
            }
            else wallSlideDir = 0;

            // 可变跳高（原作：AutoJump 视为按住跳，保证 dash 结束立刻跳的弧线）
            if (varJumpTimer > 0)
            {
                if (input.JumpHeld || autoJump) Speed.Y = Math.Min(Speed.Y, varJumpSpeed);
                else varJumpTimer = 0;
            }

            // 跳跃
            if (input.JumpPressed)
            {
                if (jumpGraceTimer > 0 || dreamTechGraceTimer > 0) Jump(input);
                else if (CanUnDuck && WallJumpCheck(1))
                {
                    if (Holding == null && Facing == 1 && input.GrabHeld && Stamina > 0) ClimbJump(input);
                    else if (DashAttacking && SuperWallJumpAngleCheck) SuperWallJump(-1);  // 上冲撞墙
                    else WallJump(-1, input);
                }
                else if (CanUnDuck && WallJumpCheck(-1))
                {
                    if (Holding == null && Facing == -1 && input.GrabHeld && Stamina > 0) ClimbJump(input);
                    else if (DashAttacking && SuperWallJumpAngleCheck) SuperWallJump(1);  // 上冲撞墙
                    else WallJump(1, input);
                }
            }

        }

        bool TryPickupGlider()
        {
            if (Gliders == null) return false;
            Glider nearest = null;
            float nearestSq = float.MaxValue;
            foreach (Glider glider in Gliders)
            {
                if (!glider.CanPickup(this)) continue;
                float dx = glider.Pos.X - Pos.X, dy = glider.Pos.Y - Pos.Y;
                float distanceSq = dx * dx + dy * dy;
                if (distanceSq < nearestSq) { nearest = glider; nearestSq = distanceSq; }
            }
            if (nearest == null || !nearest.Pickup(this)) return false;
            Holding = nearest;
            minHoldTimer = 0.35f;
            pickupStoredSpeed = Speed;
            pickupStoredVarJump = varJumpTimer;
            Speed = PointF.Empty;
            pickupTimer = 0.16f;
            pickupCurveBegin = new PointF(nearest.Pos.X - Pos.X, nearest.Pos.Y - Pos.Y);
            pickupCurveControl = new PointF(
                pickupCurveBegin.X + Sign(pickupCurveBegin.X) * 2f, -14f);
            State = StPickup;
            PlaySound("event:/char/madeline/crystaltheo_lift");
            return true;
        }

        void ThrowGlider()
        {
            if (Holding == null) return;
            Holding.Release(new PointF(Facing, 0f), Solids);
            Holding = null;
            Speed.X -= 80f * Facing;
            throwAnimTimer = 0.24f;
            PlaySound("event:/char/madeline/crystaltheo_throw");
        }

        void DropGlider()
        {
            if (Holding == null) return;
            Holding.Release(PointF.Empty, Solids);
            Holding = null;
            PlaySound("event:/new_content/char/madeline/glider_drop");
        }

        public void ReleaseGliderForDrag()
        {
            if (Holding == null) return;
            Holding.Release(PointF.Empty, Solids);
            Holding = null;
            PlaySound("event:/new_content/char/madeline/glider_drop");
            minHoldTimer = 0f;
            if (State == StPickup) EnterNormal();
        }

        public void ForgetGlider(Glider glider)
        {
            if (Holding != glider) return;
            Holding = null;
            minHoldTimer = 0f;
            if (State == StPickup) EnterNormal();
        }

        float maxFall = MaxFall;

        void Jump(PetInput input, bool particles = true)
        {
            bool dreamJump = dreamTechGraceTimer > 0f;
            ConsumeJump();
            autoJump = false;
            jumpGraceTimer = 0;
            dreamTechGraceTimer = 0f;
            varJumpTimer = VarJumpTime;
            wallSlideTimer = WallSlideTime;
            wallBoostTimer = 0f;
            dashAttackTimer = 0f;  // 原作：跳跃清除冲刺攻击窗口
            gliderBoostTimer = 0f;
            Speed.X += JumpHBoost * moveX;
            Speed.Y = JumpSpeed;
            varJumpSpeed = Speed.Y;
            SpriteScaleX = 0.6f; SpriteScaleY = 1.4f;
            if (particles) JumpEffectCount++;
            PlaySound(dreamJump
                ? "event:/char/madeline/jump_dreamblock"
                : "event:/char/madeline/jump");
        }

        void SuperJump()
        {
            bool wasDucking = Ducking;
            ConsumeJump();
            autoJump = false;
            jumpGraceTimer = 0;
            dreamTechGraceTimer = 0f;
            varJumpTimer = VarJumpTime;
            wallSlideTimer = WallSlideTime;
            wallBoostTimer = 0f;
            dashAttackTimer = 0f;  // 原作：super 跳清除冲刺攻击窗口
            // Vanilla SuperJump launches in Facing rather than DashDir. Facing can
            // change during the dash, which is what enables reverse dash tech.
            int dir = Facing;
            Speed.X = SuperJumpH * dir;
            Speed.Y = JumpSpeed;
            if (Ducking)
            {
                Ducking = false;
                Speed.X *= DuckSuperJumpXMult;
                Speed.Y *= DuckSuperJumpYMult;
            }
            varJumpSpeed = Speed.Y;
            gliderBoostTimer = 0.55f;
            gliderBoostDir = wasDucking
                ? new PointF(0.8314696f * Facing, -0.5555702f)
                : new PointF(0.7071068f * Facing, -0.7071068f);
            Facing = dir;
            LaunchCount++;
            JumpEffectCount++;
            SpriteScaleX = 0.6f; SpriteScaleY = 1.4f;
            PlaySound("event:/char/madeline/jump");
            PlaySound(wasDucking
                ? "event:/char/madeline/jump_superslide"
                : "event:/char/madeline/jump_super");
        }

        void WallJump(int dir, PetInput input)
        {
            ConsumeJump();
            autoJump = false;
            Ducking = false;
            jumpGraceTimer = 0;
            varJumpTimer = VarJumpTime;
            wallSlideTimer = WallSlideTime;
            wallBoostTimer = 0f;
            dashAttackTimer = 0f;  // 原作：蹬墙跳清除冲刺攻击窗口
            gliderBoostTimer = 0f;
            Speed.X = WallJumpHSpeed * dir;
            Speed.Y = JumpSpeed;
            varJumpSpeed = Speed.Y;
            // A held glider always gets the longer 0.26s wall-jump steering lock.
            // Without one, vanilla only applies the normal 0.16s lock when a
            // horizontal direction was held on the jump frame.
            if (Holding != null) { forceMoveX = dir; forceMoveXTimer = 0.26f; }
            else if (moveX != 0) { forceMoveX = dir; forceMoveXTimer = WallJumpForceTime; }
            Facing = dir;
            LastWallJumpDirection = dir;
            WallJumpEffectCount++;
            SpriteScaleX = 0.6f; SpriteScaleY = 1.4f;
            PlaySound("event:/char/madeline/landing", "surface_index",
                WallSurfaceSoundIndex(-dir));
            PlaySound(dir < 0
                ? "event:/char/madeline/jump_wall_right"
                : "event:/char/madeline/jump_wall_left");
        }

        void SuperWallJump(int dir)
        {
            // 冲刺上冲撞墙 → 超级蹬墙跳（170h, -160v, varTimer 0.25）
            ConsumeJump();
            Ducking = false;
            autoJump = false;
            jumpGraceTimer = 0;
            varJumpTimer = SuperWallJumpVarTime;
            dashAttackTimer = 0f;
            gliderBoostTimer = 0.55f;
            gliderBoostDir = new PointF(0f, -1f);
            wallSlideTimer = WallSlideTime;
            wallBoostTimer = 0f;
            Speed.X = SuperWallJumpH * dir;
            Speed.Y = SuperWallJumpSpeed;
            varJumpSpeed = Speed.Y;
            // 原作 SuperWallJump 不设 forceMove（可立即转向）
            Facing = dir;
            LaunchCount++;
            LastWallJumpDirection = dir;
            WallJumpEffectCount++;
            SpriteScaleX = 0.6f; SpriteScaleY = 1.4f;
            PlaySound(dir < 0
                ? "event:/char/madeline/jump_wall_right"
                : "event:/char/madeline/jump_wall_left");
            PlaySound("event:/char/madeline/jump_superwall");
        }

        void ClimbJump(PetInput input)
        {
            if (!onGround)
            {
                if (!InfiniteStamina) Stamina -= ClimbJumpCost;
                SweatAnimId = "jump";
                sweatJumpTimer = 0.4f;
                SweatAnimSequenceCount++;
            }
            Jump(input, particles: false);
            // 原作 wallBoost：无方向攀爬跳不立刻推离；0.2s 内按住离墙方向 → 130 加速 + 返还体力
            if (moveX == 0)
            {
                wallBoostDir = -Facing;
                wallBoostTimer = 0.2f;
            }
            LastWallJumpDirection = -Facing;
            WallJumpEffectCount++;
            PlaySound(Facing > 0
                ? "event:/char/madeline/jump_climb_right"
                : "event:/char/madeline/jump_climb_left");
        }

        void EnterClimb()
        {
            autoJump = false;
            wallSpeedRetained = 0f;
            wallSpeedRetentionTimer = 0f;
            wallBoostTimer = 0f;
            hopWaitX = 0;
            SweatAnimId = "idle";
            sweatJumpTimer = 0f;
            Speed.X = 0;
            Speed.Y *= 0.2f;
            Ducking = false;
            wallSlideTimer = WallSlideTime;
            climbNoMoveTimer = 0.1f;
            lastClimbMove = 0;
            for (int i = 0; i < 2; i++)
            {
                if (CollideAt(Pos.X + Facing, Pos.Y)) break;
                Pos.X += Facing;
            }
            PlaySound("event:/char/madeline/grab", "surface_index",
                WallSurfaceSoundIndex(Facing));
        }

        void EnterNormal()
        {
            State = StNormal;
            maxFall = MaxFall; // NormalBegin
        }

        void TryDeployElytra(PetInput input)
        {
            if (!ElytraEnabled || !input.ElytraHeld || onGround || elytraCooldown > 0f ||
                Dashes <= 0 || BeingDragged || IsDead)
                return;

            Dashes = Math.Max(0, Dashes - 1);
            State = StElytra;
            SpriteScaleX = 1.4f;
            SpriteScaleY = 0.6f;
            elytraFacing = Facing;
            PointF relative = elytraFacing == 1 ? Speed : new PointF(-Speed.X, Speed.Y);
            elytraAngle = (float)Math.Atan2(relative.Y, relative.X);
            elytraSpeed = (float)Math.Sqrt(relative.X * relative.X + relative.Y * relative.Y);
            elytraStableTimer = 0f;
            UpdateElytraAnimationFrame(elytraAngle);
            float deployAngle = ClampElytraDeployAngle(elytraAngle);
            ElytraDeployParticleAngle = elytraFacing == 1
                ? deployAngle : (float)Math.PI - deployAngle;
            ElytraDeploySequenceCount++;
        }

        static float ClampElytraDeployAngle(float angle)
            => Math.Max(ElytraStableAngle - ElytraAngleRange / 2f,
                Math.Min(ElytraStableAngle, angle));

        void EndElytra() => elytraCooldown = ElytraCooldownTime;

        void ElytraUpdate(float dt, PetInput input)
        {
            // This order is significant and matches CommunalHelper.GlideUpdate.
            if (onGround)
            {
                EndElytra();
                EnterNormal();
                return;
            }
            if (ClimbCheck(elytraFacing))
            {
                EndElytra();
                Facing = elytraFacing;
                EnterClimb();
                State = StClimb;
                return;
            }
            if (CanDash)
            {
                EndElytra();
                State = StartDash(input);
                return;
            }
            if (!ElytraEnabled || !input.ElytraHeld)
            {
                EndElytra();
                EnterNormal();
                return;
            }

            float oldAngle = elytraAngle;
            float oldSpeed = Math.Max(elytraSpeed, ElytraMinSpeed);
            float maxAngleChange = dt * ElytraAngleChangeFactor / oldSpeed;
            float newAngle;
            if (elytraStableTimer > 0f)
                newAngle = oldAngle;
            else if (oldSpeed == ElytraMinSpeed && input.MoveY < 0)
                newAngle = Approach(oldAngle, ElytraStableAngle, maxAngleChange);
            else
                newAngle = Approach(oldAngle,
                    ElytraStableAngle + ElytraAngleRange / 2f * input.MoveY,
                    maxAngleChange);
            newAngle = Math.Max(ElytraStableAngle - ElytraAngleRange / 2f,
                Math.Min(ElytraStableAngle + ElytraAngleRange / 2f, newAngle));
            elytraStableTimer = Approach(elytraStableTimer, 0f, dt);

            float newSpeed = oldSpeed;
            float inputAmount = Math.Abs(input.MoveY);
            if (elytraStableTimer <= 0f)
            {
                if (newAngle < ElytraStableAngle)
                    newSpeed = Approach(oldSpeed, ElytraMinSpeed,
                        dt * (oldSpeed > ElytraMaxSpeed ? ElytraFastDecel : ElytraDecel) * inputAmount);
                else if (newAngle > ElytraStableAngle && oldSpeed < ElytraMaxSpeed)
                    newSpeed = Approach(oldSpeed, ElytraMaxSpeed, dt * ElytraAccel * inputAmount);
            }

            elytraAngle = newAngle;
            elytraSpeed = newSpeed;
            Facing = elytraFacing;
            Speed = new PointF((float)Math.Cos(newAngle) * newSpeed * elytraFacing,
                (float)Math.Sin(newAngle) * newSpeed);

            UpdateElytraAnimationFrame(newAngle);
        }

        void UpdateElytraAnimationFrame(float angle)
        {
            const int frameCount = 9;
            const int stableFrame = 6;
            float t = (angle - ElytraStableAngle) / (ElytraAngleRange / 2f);
            int frame = stableFrame;
            if (t < 0f) frame -= (int)(t * (frameCount - stableFrame - 1));
            else frame -= (int)(t * stableFrame);
            ElytraAnimationFrame = Math.Max(0, Math.Min(frameCount - 1, frame));
        }

        bool SlipCheck(float addY = 0f)
        {
            // Player.SlipCheck probes the upper edge of the wall beside the
            // standing hitbox.  The asymmetric X coordinates mirror Celeste's
            // right-exclusive collider bounds.
            float x = Facing > 0 ? Pos.X + 4f : Pos.X - 5f;
            float y = Pos.Y - HitH + 4f + addY;
            if (!CollidePoint(x, y))
                return !CollidePoint(x, y - 4f + addY);
            return false;
        }

        // ===== 攀爬状态 =====
        void ClimbUpdate(float dt, PetInput input)
        {
            climbNoMoveTimer -= dt;
            if (onGround) Stamina = ClimbMaxStamina;

            if (input.JumpPressed)
            {
                if (moveX == -Facing) WallJump(-Facing, input);
                else ClimbJump(input);
                EnterNormal();
                return;
            }
            if (CanDash) { SweatAnimId = "idle"; State = StartDash(input); return; }
            if (!input.GrabHeld)
            {
                SweatAnimId = "idle";
                PlaySound("event:/char/madeline/grab_letgo");
                EnterNormal();
                return;
            }
            if (!CollideAt(Pos.X + Facing, Pos.Y))
            {
                if (Speed.Y < 0) ClimbHop();
                SweatAnimId = "idle";
                EnterNormal();
                return;
            }

            float ty = 0;
            bool slipping = false;
            if (climbNoMoveTimer <= 0)
            {
                if (input.MoveY == -1)
                {
                    ty = ClimbUpSpeed;
                    // Vanilla only combines SlipCheck(-1) with
                    // ClimbHopBlockedCheck (carried strawberry seeds). The desktop
                    // pet has no followers, so an exposed lip must proceed to the
                    // normal SlipCheck below and ledge-hop instead of getting stuck.
                    if (CollideAt(Pos.X, Pos.Y - 1))
                    {
                        if (Speed.Y < 0f) Speed.Y = 0f;
                        ty = 0f;
                        slipping = true;
                    }
                    else if (SlipCheck())
                    {
                        ClimbHop();
                        EnterNormal();
                        return;
                    }
                }
                else if (input.MoveY == 1)
                {
                    ty = ClimbDownSpeed;
                    if (onGround) ty = 0;
                }
                else slipping = true;
            }
            else slipping = true;
            if (slipping && SlipCheck()) ty = 30f;
            // 原作：攀爬速度用 Approach（加速度 ClimbAccel=900），不是瞬间切换
            Speed.Y = Approach(Speed.Y, ty, ClimbAccel * dt);
            lastClimbMove = Sign(ty);

            if (input.MoveY != 1 && Speed.Y > 0f && !CollideAt(Pos.X + Facing, Pos.Y + 1f))
                Speed.Y = 0f;

            if (!InfiniteStamina && climbNoMoveTimer <= 0f)
            {
                if (lastClimbMove < 0) Stamina -= 45.4545f * dt;
                else if (lastClimbMove == 0) Stamina -= ClimbStillCost * dt; // 原作：静止爬墙每秒消耗 10
            }
            if (Stamina <= 0)
            {
                Stamina = 0;
                SweatAnimId = "idle";
                EnterNormal();
            }
            else if (climbNoMoveTimer > 0f)
                SweatAnimId = !InfiniteStamina && Stamina <= 20f ? "danger" : "idle";
            else if (!InfiniteStamina && Stamina <= 20f)
                SweatAnimId = "danger";
            else if (lastClimbMove < 0)
                SweatAnimId = "climb";
            else if (!onGround)
                SweatAnimId = "still";
            else
                SweatAnimId = "idle";

        }

        void ClimbHop()
        {
            // Static walls retain the horizontal hop until the rising player has
            // actually cleared the ledge; this is vanilla's hopWaitX behavior.
            Speed.Y = Math.Min(Speed.Y, ClimbHopY);
            hopWaitX = Facing;
            hopWaitXSpeed = ClimbHopX * Facing;
            playFootstepOnLand = 0.5f;
            forceMoveX = 0;                // 原作 forceMoveX = 0
            forceMoveXTimer = 0.2f;        // 0.2s 内不受方向键影响
            fastJump = false;
            PlaySound("event:/char/madeline/climb_ledge");
        }

        void MoveVExactLocal(int n) => MoveVExact(n);

        // ===== 冲刺状态 =====
        bool ConsumeDashRequest()
        {
            bool crouchDash = crouchDashBufferTimer > 0f;
            ConsumeDash();
            LastDashWasTwo = Dashes == 2;
            Dashes = Math.Max(0, Dashes - 1);
            hairFlashTimer = 0.12f;
            HairColor = FlashHairColor;
            return crouchDash;
        }

        int StartDash(PetInput input)
            => BeginDash(input, ConsumeDashRequest());

        int BeginDash(PetInput input, bool crouchDash)
        {
            autoJump = false;
            // NormalEnd/ClimbEnd clear this in Celeste before entering Dash.
            wallSpeedRetained = 0f;
            wallSpeedRetentionTimer = 0f;
            wallBoostTimer = 0f;
            hopWaitX = 0;
            SweatAnimId = "idle";
            sweatJumpTimer = 0f;
            DashSequenceCount++;
            dashStartedOnGround = onGround;
            dashCooldownTimer = DashCooldown;
            dashRefillCooldownTimer = DashRefillCooldown;
            dashAttackTimer = DashAttackTime;
            gliderBoostTimer = 0.55f;
            wallSlideTimer = WallSlideTime;
            freezeTimer = FreezeFramesEnabled ? 0.05f : 0f; // 原作 Freeze(0.05)
            beforeDashSpeed = Speed;

            if (!onGround && Ducking && CanUnDuck) Ducking = false;
            else if (!Ducking && (crouchDash || input.MoveY == 1)) Ducking = true;

            // Lock lastAim on the dash-press frame. Releasing or changing a direction
            // during the 0.05s freeze must not curve a normal dash into another vector.
            float ax = input.MoveX, ay = input.MoveY;
            if (ax == 0 && ay == 0) pendingDashDir = new PointF(Facing, 0);
            else
            {
                float len = (float)Math.Sqrt(ax * ax + ay * ay);
                pendingDashDir = new PointF(ax / len, ay / len);
            }
            Speed = new PointF(0, 0);
            DashDir = new PointF(0, 0);
            dashAimPending = true;

            dashTime = DashTime;
            return StDash;
        }

        void ApplyDashAim()
        {
            PointF dir = pendingDashDir;

            PointF speed = new PointF(dir.X * DashSpeed, dir.Y * DashSpeed);
            if (Sign(beforeDashSpeed.X) == Sign(speed.X) && Math.Abs(beforeDashSpeed.X) > Math.Abs(speed.X))
                speed.X = beforeDashSpeed.X;

            DashDir = dir;
            gliderBoostDir = dir;
            Speed = speed;

            // 地面斜下冲 → 蹲冲（1.2x，原作同款）
            if (dashStartedOnGround &&
                DashDir.X != 0 && DashDir.Y > 0 && Speed.Y > 0 &&
                !DreamAt(Pos.X, Pos.Y + 1f))
            {
                DashDir = new PointF(Sign(DashDir.X), 0);
                Speed = new PointF(Speed.X * 1.2f, 0);
                Ducking = true;
            }
            if (DashDir.X != 0) Facing = Sign(DashDir.X);
            bool rightSound = DashDir.Y < 0f || (DashDir.Y == 0f && DashDir.X > 0f);
            PlaySound(LastDashWasTwo
                ? (rightSound ? "event:/char/madeline/dash_pink_right" : "event:/char/madeline/dash_pink_left")
                : (rightSound ? "event:/char/madeline/dash_red_right" : "event:/char/madeline/dash_red_left"));
        }

        void DashUpdate(float dt, PetInput input)
        {
            dashTime -= dt;

            if (Holding == null && (DashDir.X != 0f || DashDir.Y != 0f) &&
                input.GrabHeld && !IsTired && CanUnDuck && TryPickupGlider())
                return;

            // A down-diagonal dash buffered across a horizontal DreamBlock exit
            // remains diagonal. If jump is then pressed during the dream coyote
            // window, it cancels into the corresponding hyper at that moment;
            // do not flatten the dash early when no jump was requested.
            if (input.JumpPressed && dreamTechGraceTimer > 0f &&
                DashDir.X != 0f && DashDir.Y > 0f)
            {
                DashDir = new PointF(Sign(DashDir.X), 0f);
                Speed.Y = 0f;
                Ducking = true;
            }

            // 跳跃打断冲刺 → Super / Hyper / Ultra / 蹬墙跳（原作 DashUpdate 中 jump 优先于一切）
            if (input.JumpPressed)
            {
                // 地面/水平冲刺中跳 → 超级跳：super=260；蹲冲=hyper=325；落地瞬间=ultra
                if (Math.Abs(DashDir.Y) < 0.1f &&
                    (jumpGraceTimer > 0 || dreamTechGraceTimer > 0) && CanUnDuck)
                {
                    SuperJump();
                    EnterNormal();
                    return;
                }
                // 上冲撞墙 → SuperWallJump（170h, -160v），否则普通蹬墙跳
                if (SuperWallJumpAngleCheck)
                {
                    if (CanUnDuck && WallJumpCheck(1)) { SuperWallJump(-1); EnterNormal(); return; }
                    if (CanUnDuck && WallJumpCheck(-1)) { SuperWallJump(1); EnterNormal(); return; }
                }
                else
                {
                    if (CanUnDuck && WallJumpCheck(1))
                    {
                        if (Holding == null && Facing == 1 && input.GrabHeld && Stamina > 0) ClimbJump(input);
                        else WallJump(-1, input);
                        EnterNormal();
                        return;
                    }
                    if (CanUnDuck && WallJumpCheck(-1))
                    {
                        if (Holding == null && Facing == -1 && input.GrabHeld && Stamina > 0) ClimbJump(input);
                        else WallJump(1, input);
                        EnterNormal();
                        return;
                    }
                }
            }
            if (dashTime <= 0)
            {
                if (DashDir.Y <= 0)
                    Speed = new PointF(DashDir.X * EndDashSpeed, DashDir.Y * EndDashSpeed);
                if (Speed.Y < 0) Speed.Y *= EndDashUpMult;
                autoJump = true; // 原作 DashCoroutine 结尾 AutoJump=true：维持半重力/可变跳高
                EnterNormal();
            }
        }

        // Player.DreamDashUpdate: DreamDash movement itself remains collisionless;
        // after the minimum 0.1s, leaving every dream block returns to Normal and
        // restores the resources the original DreamDashEnd restores.
        void DreamDashUpdate(float dt, PetInput input)
        {
            // Vanilla calls NaiveMove before testing the new overlap.  Keeping this
            // order is frame-critical for dream jumps, double jumps and dream tech.
            MoveH(Speed.X * dt);
            MoveV(Speed.Y * dt);
            dreamDashCanEndTimer -= dt;
            if (TryGetDreamAt(Pos.X, Pos.Y, out Solid currentDream))
            {
                dreamDashBlock = currentDream;
                hasDreamDashBlock = true;
                return;
            }
            if (dreamDashCanEndTimer > 0f) return;

            if (NonDreamAt(Pos.X, Pos.Y))
            {
                PointF original = Pos;
                bool corrected = false;
                for (int x = 1; x <= 5 && !corrected; x++)
                for (int sx = -1; sx <= 1 && !corrected; sx += 2)
                for (int y = 1; y <= 5 && !corrected; y++)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    PointF candidate = new PointF(original.X + x * sx, original.Y + y * sy);
                    // DreamDashedIntoSolid checks every Solid, including the
                    // DreamBlock we just left.  A wiggle back into it is not a
                    // valid escape from the wall.
                    if (!CollideAt(candidate.X, candidate.Y)) { Pos = candidate; corrected = true; break; }
                }
                if (!corrected)
                {
                    Pos = original;
                    SnapDreamDeathToExitFace();
                    DieFromDreamDash();
                    return;
                }
            }

            bool enterClimb = false;
            if (input.JumpPressed && Math.Abs(DashDir.X) > 0.01f)
                Jump(input);
            else
            {
                autoJump = true;
                // Dream Grab. Vanilla's generic 5px correction can leave this
                // desktop representation visibly embedded in a window because a
                // 240px/s naive step may overshoot its face. Snap the 8px collider
                // flush to the exact DreamBlock boundary instead.
                if (DashDir.Y >= 0f || Math.Abs(DashDir.X) > 0.01f)
                {
                    int exitDirection = Sign(DashDir.X);
                    // DreamDashUpdate does not apply NormalUpdate's Holding == null
                    // climb-entry guard. This exception is the vanilla Dream
                    // Smuggle Grab and is what makes the later jelly regrab viable.
                    if (input.GrabHeld && moveX == -exitDirection &&
                        TryGetDreamExitWall(exitDirection, out Solid wall))
                    {
                        Facing = moveX;
                        Pos.X = exitDirection > 0 ? wall.R + 4f : wall.L - 4f;
                        enterClimb = true;
                    }
                    else if (input.GrabHeld && exitDirection == 0)
                    {
                        bool wallLeft = ClimbCheck(-1);
                        bool wallRight = ClimbCheck(1);
                        if ((moveX == 1 && wallRight) || (moveX == -1 && wallLeft))
                        {
                            Facing = moveX;
                            enterClimb = true;
                        }
                    }
                }
            }
            jumpGraceTimer = Math.Abs(DashDir.X) > 0.01f ? JumpGraceTime : 0f;
            dreamTechGraceTimer = Math.Abs(DashDir.X) > 0.01f ? JumpGraceTime : 0f;
            RefillDash();
            Stamina = ClimbMaxStamina;
            dreamDashOutTimer = 0.16f;
            freezeTimer = FreezeFramesEnabled ? 0.05f : 0f;
            if (enterClimb)
            {
                EnterClimb();
                State = StClimb;
            }
            else
                EnterNormal();
            PlaySound("event:/char/madeline/dreamblock_exit");
        }

        void SnapDreamDeathToExitFace()
        {
            if (!hasDreamDashBlock) return;
            // A desktop monitor edge cannot scroll into view like a Celeste room.
            // Keep the death body's center on the last DreamBlock face instead of
            // leaving it one full player collider beyond the physical display.
            if (DashDir.X > 0f) Pos.X = dreamDashBlock.R - 4f;
            else if (DashDir.X < 0f) Pos.X = dreamDashBlock.L + 4f;
            if (DashDir.Y > 0f) Pos.Y = dreamDashBlock.B;
            else if (DashDir.Y < 0f) Pos.Y = dreamDashBlock.T + HitH;
        }

        void DieFromDreamDash()
        {
            if (IsDead) return;
            if (Holding != null) DropGlider();
            IsDead = true;
            DeathSequenceCount++;
            PlaySound("event:/char/madeline/death");
            DeathColor = HairColor;
            DeathPosition = new PointF(Pos.X, Pos.Y - 5f);
            DeathPercent = 0f;
            deathTimer = 0f;
            deathRespawnPos = dreamDashEntryPos;
            Speed = PointF.Empty;
            counter = PointF.Empty;
            dashAimPending = false;
            freezeTimer = 0f;
            State = StFrozen;
        }

        // ===== 动画选择（移植 orig_UpdateSprite）=====
        void UpdateSprite(float dt, PetInput input)
        {
            if (landingStumbleTimer > 0f && Speed.Y != 0f) landingStumbleTimer = 0f;
            string id;
            if (BeingDragged)
            {
                id = "dangling";
            }
            else if (State == StPickup)
            {
                id = "pickUp";
            }
            else if (State == StDreamDash)
            {
                id = dreamDashAnimTimer > 0f ? "dreamDashIn" : "dreamDashLoop";
            }
            else if (State == StElytra)
            {
                id = "elytra";
            }
            else if (dreamDashOutTimer > 0f)
            {
                id = "dreamDashOut";
            }
            else if (throwAnimTimer > 0f)
            {
                id = "throw";
            }
            else if (!onGround && landingStumbleTimer > 0f)
            {
                id = "runStumble";
            }
            else if (dashAttackTimer > 0)
            {
                if (onGround && DashDir.Y == 0f && !Ducking && Speed.X != 0f &&
                    moveX == -Sign(Speed.X)) id = "skid";
                else id = Ducking ? "duck" : "dash";
            }
            else if (State == StClimb)
            {
                if (lastClimbMove < 0) id = "climb";
                else if (lastClimbMove > 0) id = "wallslide";
                else if (!CollideAt(Pos.X + Facing, Pos.Y + 6)) id = "dangling";
                else if (input.MoveX == -Facing)
                    id = AnimId == "climbLookBackStart" || AnimId == "climbLookBack"
                        ? AnimId : "climbLookBackStart";
                else id = "wallslide";
            }
            else if (Ducking && State == StNormal)
            {
                id = "duck";
            }
            else if (onGround)
            {
                fastJump = false;
                if (moveX != 0 && CollideAt(Pos.X + moveX, Pos.Y))
                {
                    id = "push";
                }
                else if (Math.Abs(Speed.X) <= 25 && moveX == 0)
                {
                    if (Holding != null)
                    {
                        id = "idle_carry";
                    }
                    else
                    {
                        bool noGroundAhead1 = !CollideAt(Pos.X + Facing, Pos.Y + 2);
                        bool noGroundAhead4 = !CollideAt(Pos.X + Facing * 4, Pos.Y + 2);
                        bool noGroundBehind1 = !CollideAt(Pos.X - Facing, Pos.Y + 2);
                        bool noGroundBehind4 = !CollideAt(Pos.X - Facing * 4, Pos.Y + 2);
                        if (noGroundAhead1 && noGroundAhead4) id = "edge";
                        else if (noGroundBehind1 && noGroundBehind4) id = "edgeBack";
                        else if (input.MoveY == -1) id = "lookUp";
                        else id = "idle";
                    }
                }
                else if (Holding != null)
                {
                    id = "runSlow_carry";
                }
                else if (Sign(Speed.X) == -moveX && moveX != 0)
                {
                    id = Math.Abs(Speed.X) > MaxRun ? "skid" : "flip";
                }
                else if (landingStumbleTimer > 0f)
                {
                    id = "runStumble";
                }
                else
                {
                    id = Math.Abs(Speed.X) < 45 ? "runSlow" : "runFast";
                }
            }
            else if (wallSlideDir != 0)
            {
                id = "wallslide";
            }
            else if (Speed.Y < 0)
            {
                if (Holding != null) id = "jumpSlow_carry";
                else if (fastJump || Math.Abs(Speed.X) > 90) { fastJump = true; id = "jumpFast"; }
                else id = "jumpSlow";
            }
            else
            {
                if (Holding != null) id = "fallSlow_carry";
                else if (fastJump || Speed.Y >= MaxFall) { fastJump = true; id = "fallFast"; }
                else id = "fallSlow";
            }

            // flip 播完前不覆盖
            if (AnimId == "flip" && !AnimFinished && id != "flip") { UpdateIdleFidget(dt, input, false); return; }
            // 待机小动作播完前不覆盖
            if (fidgetId != null)
            {
                if (AnimFinished) { fidgetId = null; observedIdleLoopCount = 0; }
                else return;
            }
            if (id == "idle" && AnimId == "idle" && UpdateIdleFidget(dt, input, true)) return;
            if (id != "idle") observedIdleLoopCount = 0;
            AnimId = id;
        }

        public bool AnimFinished;

        bool UpdateIdleFidget(float dt, PetInput input, bool allow)
        {
            bool completedIdleLoop = AnimLoopCount > observedIdleLoopCount;
            observedIdleLoopCount = AnimLoopCount;
            if (allow && idleTimer > 3f && completedIdleLoop && rng.NextDouble() < 0.2)
            {
                string[] pool = { "idleA", "idleB", "idleC" };
                string pick = pool[rng.Next(pool.Length)];
                if (Sprites.Has(pick + "00"))
                {
                    fidgetId = pick;
                    AnimId = pick;
                    return true;
                }
            }
            return false;
        }

        public int AnimLoopCount;
    }

    /// <summary>
    /// 头发模拟（移植 PlayerHair.AfterUpdate）。
    /// 手动调头发：改下面的常量，重新编译即可（dotnet build）：
    ///   Count          发丝段数（越多越长）
    ///   HangDown       每段向下垂的偏移（px）
    ///   BackLean       每段朝背后的偏移（px，走路拖尾强度）
    ///   ApproachSpeed  发丝跟随速度（px/s）：越小越飘、拖尾越明显；越大越贴头
    ///   MaxSegment     相邻段最大间距（px）：越大发丝越长
    ///   WaveSpeed      静止时的摆动速度
    /// 头发根锚点高度在 Player.Update 里的 anchorY（当前 -9×挤压倍率）；
    /// 每帧的锚点微调（hx/hy/刘海朝向）在 HairMeta.cs 里。
    /// </summary>
    public class PlayerHairSim
    {
        public const int MaxCount = 5;
        public int ActiveCount { get; private set; } = 4;
        const float MaxSegment = 3f;
        const float WaveSpeed = 4f;
        public readonly PointF[] Nodes = new PointF[MaxCount];
        float wave, time;
        public float Wave => wave;
        bool started;

        static PointF Approach(PointF val, PointF target, float maxMove)
        {
            float dx = target.X - val.X, dy = target.Y - val.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist <= maxMove || dist == 0) return target;
            return new PointF(val.X + dx / dist * maxMove, val.Y + dy / dist * maxMove);
        }

        public void AfterUpdate(float dt, PointF anchor, int facing, bool twoDashes)
        {
            wave += dt * WaveSpeed;
            time += dt;
            int count = twoDashes ? 5 : 4;
            if (!started)
            {
                for (int i = 0; i < MaxCount; i++) Nodes[i] = new PointF(anchor.X - facing * 3, anchor.Y + 2);
                started = true;
            }
            else if (count > ActiveCount)
            {
                for (int i = ActiveCount; i < count; i++) Nodes[i] = Nodes[i - 1];
            }
            ActiveCount = count;

            // Player.UpdateHair：双冲刺时头发变为 5 节，并使用独立的强风式正弦参数。
            float stepX = twoDashes ? (float)Math.Sin(time * 2f) * 0.7f - facing * 3f : 0f;
            float stepY = twoDashes ? (float)Math.Sin(time) : 2f;
            float backLean = twoDashes ? 0f : 0.5f;
            float approachSpeed = twoDashes ? 90f : 64f;
            float stepYSine = twoDashes ? 1f : 0f;

            Nodes[0] = anchor;
            var target = new PointF(
                Nodes[0].X - facing * backLean * 2f + stepX,
                Nodes[0].Y + (float)Math.Sin(wave) * stepYSine + stepY);
            var prev = Nodes[0];
            for (int i = 1; i < count; i++)
            {
                float approach = (1f - (float)i / count * 0.5f) * approachSpeed;
                Nodes[i] = Approach(Nodes[i], target, approach * dt);
                float dx = Nodes[i].X - prev.X, dy = Nodes[i].Y - prev.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist > MaxSegment)
                    Nodes[i] = new PointF(prev.X + dx / dist * MaxSegment, prev.Y + dy / dist * MaxSegment);
                target = new PointF(
                    Nodes[i].X - facing * backLean + stepX,
                    Nodes[i].Y + (float)Math.Sin(wave + i * 0.8f) * stepYSine + stepY);
                prev = Nodes[i];
            }
        }

        public void Reset(PointF anchor, int facing)
        {
            started = false;
            AfterUpdate(0, anchor, facing, false);
        }

        public void MoveBy(float x, float y)
        {
            for (int i = 0; i < ActiveCount; i++)
                Nodes[i] = new PointF(Nodes[i].X + x, Nodes[i].Y + y);
        }
    }
}
