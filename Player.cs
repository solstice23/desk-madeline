using System;
using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>Platform (window rect / floor), units: game pixels.</summary>
    public struct Solid
    {
        public IntPtr Id;
        public float L, T, R, B;
        public bool Dream;
        /// <summary>Fills the area beyond the monitors; nothing here can be seen.</summary>
        public bool OffScreen;
    }

    /// <summary>Per-frame input snapshot.</summary>
    public struct PetInput
    {
        public int MoveX;      // -1/0/1
        public int MoveY;      // -1 up / 0 / 1 down
        // Celeste reads three separate virtual axes off the same bindings, each with its own
        // controller deadzone: MoveX/MoveY, GliderMoveY (jellyfish fall), and Aim (dash
        // direction). On a keyboard all of them equal MoveX/MoveY.
        public int AimX;       // Input.Aim.X
        public int AimY;       // Input.Aim.Y
        public int GliderMoveY;
        // Input.Feather: the move bindings again, at the aim deadzone rather than the move
        // one. Swimming steers by it, so on a controller it answers where MoveX/MoveY are
        // still silent -- and on a keyboard it is the same eight directions as everything else.
        public int FeatherX, FeatherY;
        public bool JumpHeld;
        public bool GrabHeld;
        public bool JumpPressed;  // already input-buffered (valid this frame)
        public bool DashPressed;
        public bool ElytraHeld;
    }

    /// <summary>
    /// The answers a solid can give to being dashed into -- vanilla's DashCollisionResults.
    /// Bounce is vanilla's too, but nothing on the desktop returns it, so it stays unported
    /// rather than half-wired.
    /// </summary>
    public enum DashCollisionResults { NormalCollision, NormalOverride, Rebound, Ignore }

    public readonly struct PlayerSoundEvent
    {
        public readonly string Path, Parameter;
        public readonly float Value;
        public PlayerSoundEvent(string path, string parameter = null, float value = 0f)
        { Path = path; Parameter = parameter; Value = value; }
    }

    /// <summary>
    /// Madeline: physics and state machine ported from Celeste Player.cs (Normal/Climb/Dash).
    /// Coordinate unit = game pixel (1:1 with vanilla); rendering upscales by S.
    /// </summary>
    public class Player
    {
        // ===== Vanilla constants (Player.cs) =====
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
        const float SuperDashTime = 0.3f;          // Super Dashing: the dash runs twice as long
        const float SuperDashAttackBonus = 0.15f;  // and its dash-attack window with it
        const float DashCurveRate = 4.1887903f;    // 240 deg/s, the rate the dash turns at
        const float DashCooldown = 0.2f;
        const float DashRefillCooldown = 0.1f;
        const float DashAttackTime = 0.3f;
        const float ClimbMaxStamina = 110f;
        const float WallSpeedRetentionTime = 0.06f;  // wall-speed retention window (vanilla wallSpeedRetentionTimer, ~4 frames)
        const float ClimbJumpCost = 27.5f;
        const float ClimbUpSpeed = -45f;
        const float ClimbDownSpeed = 80f;
        const float ClimbAccel = 900f;         // climb move acceleration (vanilla Approach)
        const float ClimbStillCost = 10f;      // stamina drain per second while holding still on a wall
        const float ClimbSlipSpeed = 30f;      // slide speed when the grabbed lip slips away
        const float ClimbHopY = -120f;         // hop-up speed when climbing over a ledge
        const float ClimbHopX = 100f;          // horizontal push when climbing over a ledge
        const int DashingUpwardCornerCorrection = 5; // upward-dash ceiling corner-correction distance
        const int DashVFloorSnapDist = 3;      // airborne horizontal dash snaps down onto a floor this close

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

        // Hair colors (Player.cs)
        public static readonly Color NormalHairColor = Color.FromArgb(0xAC, 0x32, 0x32);
        public static readonly Color UsedHairColor = Color.FromArgb(0x44, 0xB7, 0xFF);
        public static readonly Color FlashHairColor = Color.White;
        public static readonly Color TwoDashesHairColor = Color.FromArgb(0xFF, 0x6D, 0xEF);

        // ===== State =====
        public PointF Pos;          // foot-center (game pixels)
        public PointF Speed;
        public int Facing = 1;      // -1 left / 1 right
        public bool Ducking;
        public int State;
        public string StateName => State >= 0 && State < StateNames.Length
            ? StateNames[State] : "Custom(" + State + ")";
        public int Dashes = 1;
        // 0/1/2 = max dash count, -1 = infinite (visuals treated like vanilla two-dash).
        public int DashMode = 1;
        public float Stamina = ClimbMaxStamina;
        public bool InfiniteStamina;
        public bool Invincible;
        /// <summary>Assists.SuperDashing: the variant menu's "Super Dashing".</summary>
        public bool SuperDashing;
        public bool FreezeFramesEnabled = true;
        public bool RespawnReversalEnabled;
        public bool ElytraEnabled;
        public bool onGround;
        public IntPtr GroundId;
        public PointF DashDir;
        /// <summary>
        /// Solid.OnDashCollide, asked with the collision's own one-axis direction while she is
        /// dash-attacking, and answered by whoever owns the solid -- a floaty block takes a
        /// shove, a kevin block charges and throws her back. Every solid here is a window, so
        /// the shell owns the one callback rather than each solid carrying its own.
        /// </summary>
        public Func<IntPtr, PointF, DashCollisionResults> OnDashCollide;
        public IList<IPetHoldable> Holdables;
        public IPetHoldable Holding { get; private set; }
        public bool IsHoldingGlider => Holding is Glider;

        // Presentation
        public float SpriteScaleX = 1f, SpriteScaleY = 1f;
        public string AnimId = "idle";
        public string CurrentFrameId;   // synced by the window each frame (hair anchor follows current frame)
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
        {
            if (Ghost) return;      // a rehearsal has no voice
            SoundEvents.Enqueue(new PlayerSoundEvent(path, parameter, value));
        }

        /// <summary>True on rehearsal copies: no voice, no hair, no reach into the world.</summary>
        internal bool Ghost;

        /// <summary>
        /// A rehearsal copy for the idle planner: the same physics down to every private
        /// timer, but mute, hairless, holding nothing, and unable to touch anything real --
        /// a simulated dash must never move an actual window.
        /// </summary>
        internal Player CloneForSim()
        {
            var ghost = (Player)MemberwiseClone();
            ghost.Ghost = true;
            ghost.OnDashCollide = null;
            ghost.Holdables = null;
            ghost.FreezeFramesEnabled = false;
            return ghost;
        }

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

        /// <param name="snapUp">
        /// Whether coming at it from almost straight above throws her straight up. A puffer
        /// asks for that; a bumper does not, and takes her off the way she arrived.
        /// </param>
        /// <param name="sidesOnly">Flatten any launch with a sideways part into a level one.</param>
        /// <summary>
        /// Player.Rebound: thrown back off something a dash could not move, which on the
        /// desktop is a kevin block's face as it activates. Vanilla also clears `launched`,
        /// AutoJumpTimer and lowFrictionStopTimer; those belong to the speed-ring, feather and
        /// icy-floor systems, none of which this port has.
        /// </summary>
        public void Rebound(int direction = 0)
        {
            Speed.X = direction * 120f;
            Speed.Y = -120f;
            varJumpSpeed = Speed.Y;
            varJumpTimer = .15f;
            autoJump = true;
            dashAttackTimer = 0f;
            gliderBoostTimer = 0f;
            wallSlideTimer = WallSlideTime;
            wallBoostTimer = 0f;
            forceMoveXTimer = 0f;
            State = StNormal;
        }

        public PointF ExplodeLaunch(PointF from, bool snapUp = true, bool sidesOnly = false)
        {
            ApplyFreeze(.1f);
            PointF vector = SafeNormalize(Center.X - from.X, Center.Y - from.Y, 0f, -1f);
            float dotUp = vector.Y;
            if (snapUp && dotUp <= -.7f) { vector.X = 0f; vector.Y = -1f; }
            else if (dotUp <= .65f && dotUp >= -.55f) { vector.Y = 0f; vector.X = Math.Sign(vector.X); }
            if (sidesOnly && vector.X != 0f) { vector.Y = 0f; vector.X = Math.Sign(vector.X); }
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
            // Player.orig_Die returns null without changing player state when the
            // Assist Mode Invincible flag is active.
            if (Invincible) return;
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
            if (Invincible) return;
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
                // Whole pixels: a Celeste Actor never holds a sub-pixel position, and both the
                // collision checks below and the climb checks she will face after respawning
                // assume the whole-pixel grid.  A polar offset lands between pixels.
                PointF candidate = new PointF(
                    (float)Math.Round(Math.Max(MinX + 4f, Math.Min(MaxX - 4f,
                        origin.X + (float)Math.Cos(angle) * radius))),
                    (float)Math.Round(origin.Y + (float)Math.Sin(angle) * radius));
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

        // Calc.Angle / AngleToVector / AngleDiff / AngleApproach / RotateTowards, which
        // the curving dash of Super Dashing goes through.
        static float Angle(PointF p) => (float)Math.Atan2(p.Y, p.X);
        static PointF AngleToVector(float radians, float length)
            => new PointF((float)Math.Cos(radians) * length, (float)Math.Sin(radians) * length);
        static float AngleDiff(float a, float b)
        {
            float d = b - a;
            while (d > Math.PI) d -= (float)Math.PI * 2f;
            while (d <= -Math.PI) d += (float)Math.PI * 2f;
            return d;
        }
        static float AngleApproach(float value, float target, float maxMove)
        {
            float diff = AngleDiff(value, target);
            if (Math.Abs(diff) < maxMove) return target;
            return value + Math.Max(-maxMove, Math.Min(maxMove, diff));
        }
        static PointF RotateTowards(PointF vec, float targetRadians, float maxMoveRadians)
            => AngleToVector(AngleApproach(Angle(vec), targetRadians, maxMoveRadians),
                             (float)Math.Sqrt(vec.X * vec.X + vec.Y * vec.Y));

        /// <summary>Player.CorrectDashPrecision: a hair off an axis counts as on it.</summary>
        static PointF CorrectDashPrecision(PointF dir)
        {
            if (dir.X != 0f && Math.Abs(dir.X) < 0.001f)
                return new PointF(0f, Math.Sign(dir.Y));
            if (dir.Y != 0f && Math.Abs(dir.Y) < 0.001f)
                return new PointF(Math.Sign(dir.X), 0f);
            return dir;
        }

        // Timers
        float jumpGraceTimer;
        float varJumpTimer;
        float varJumpSpeed;
        float dashCooldownTimer;
        float explodeLaunchBoostTimer, explodeLaunchBoostSpeed;
        float dashRefillCooldownTimer;
        float dashAttackTimer;
        float hairFlashTimer;     // hair flash-white timer (0.12s on dash refill)
        float wallSlideTimer = WallSlideTime;
        int wallSlideDir;
        float forceMoveXTimer;
        int forceMoveX;
        float climbNoMoveTimer;
        int wallBoostDir;         // climb-jump boost direction (vanilla wallBoostDir)
        float wallBoostTimer;     // climb-jump boost timer (vanilla wallBoostTimer)
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
        // Player.lastAim / Input.LastAim: the 8-way snapped aim, refreshed every frame.
        PointF lastAim = new PointF(1, 0);

        /// <summary>Input.GetAimVector: a zero aim falls back to Facing, otherwise 8-way snapped.</summary>
        PointF AimVector(int aimX, int aimY)
        {
            if (aimX == 0 && aimY == 0) return new PointF(Facing, 0);
            float len = (float)Math.Sqrt(aimX * aimX + aimY * aimY);
            return new PointF(aimX / len, aimY / len);
        }
        bool dashAimPending;
        // Player.canCurveDash: a dash may be steered until it hits something. Only
        // Super Dashing reads it, but it is cleared wherever vanilla clears it.
        bool canCurveDash;
        bool calledDashEvents;  // vanilla Player.calledDashEvents: dash sfx fires once per dash
        bool autoJump;          // auto-jump hold after dash ends (vanilla AutoJump: half-gravity / var-jump treated as jump held)
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
        PointF carryOffset = new PointF(0f, -12f);
        float dreamDashCanEndTimer;
        float dreamDashAnimTimer, dreamDashOutTimer;
        // Player.dreamJump. Vanilla's "dream jump" window is nothing but the ordinary
        // jumpGraceTimer that DreamDashEnd re-grants on a horizontal exit; this flag only
        // selects the dream-block jump sfx and never gates a jump.
        bool dreamJump;
        float throwAnimTimer;
        float gliderBoostTimer;
        PointF gliderBoostDir;
        PointF dreamDashEntryPos;
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

        // Retained speed (cornerboost): store horizontal speed on wall hit; restore if wall stops blocking within the window
        float wallSpeedRetained;
        float wallSpeedRetentionTimer;
        int moveX;
        int hopWaitX;
        float hopWaitXSpeed;

        // External data
        public List<Solid> Solids = new List<Solid>();
        /// <summary>Water she can swim in. Not solids; see WaterAt.</summary>
        public List<Solid> Waters = new List<Solid>();
        bool wasInWater;
        public float MinX = -10000, MaxX = 10000;   // screen bounds (game pixels)
        public bool BeingDragged;

        PointF counter;  // sub-pixel movement accumulator (Actor.movementCounter)
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

        // ===== Collision =====
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

        /// <summary>Raw overlap test (no inside/outside semantics).</summary>
        bool CollideAt(float x, float y)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var s in Solids)
                if (Overlap(l, t, r, b, s)) return true;
            return false;
        }

        /// <summary>
        /// Water at the hitbox placed at (x, y). Water is not solid and is kept apart from
        /// the solids for that reason: vanilla's Water is a plain Entity that everything moves
        /// through, and a window filled with it must not become a floor.
        /// </summary>
        bool WaterAt(float x, float y)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var water in Waters)
                if (Overlap(l, t, r, b, water)) return true;
            return false;
        }

        // Player.SwimCheck and its neighbours, each the collider lifted by so many pixels:
        // deep enough to swim in, deep enough to be under, shallow enough to jump or rise out.
        bool SwimCheck() => WaterAt(Pos.X, Pos.Y - 8f) && WaterAt(Pos.X, Pos.Y);
        bool SwimUnderwaterCheck() => WaterAt(Pos.X, Pos.Y - 9f);
        bool SwimJumpCheck() => !WaterAt(Pos.X, Pos.Y - 14f);
        bool SwimRiseCheck() => !WaterAt(Pos.X, Pos.Y - 18f);

        bool TryGetWaterAt(float x, float y, out Solid pool)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var water in Waters)
                if (Overlap(l, t, r, b, water)) { pool = water; return true; }
            pool = default;
            return false;
        }

        /// <summary>
        /// Water.Update's half of it: a splash on the way in and on the way out, deeper-
        /// sounding when she was above the middle of the pool and it is not shallow, and the
        /// dash variants when she crosses the surface mid-dash.
        /// </summary>
        /// <remarks>
        /// Vanilla hangs this off a WaterInteraction component the Water entity polls; here
        /// the water is a list of rectangles and nothing polls, so she watches the crossing
        /// herself. What is heard is the same, which is the part that is the port.
        /// </remarks>
        void UpdateWaterSounds()
        {
            bool inWater = TryGetWaterAt(Pos.X, Pos.Y, out Solid pool);
            if (inWater != wasInWater)
            {
                float centreY = Pos.Y - HitH / 2f;
                // Water._IsShallow: under 16 pixels of it and a splash is a shallow one.
                bool deep = inWater || wasInWater
                    ? centreY < (pool.T + pool.B) / 2f && (pool.B - pool.T) >= 16f
                    : false;
                if (wasInWater)
                    PlaySound(DashAttacking
                        ? "event:/char/madeline/water_dash_out"
                        : "event:/char/madeline/water_out", "deep", deep ? 1f : 0f);
                else
                    PlaySound(DashAttacking && deep
                        ? "event:/char/madeline/water_dash_in"
                        : "event:/char/madeline/water_in", "deep", deep ? 1f : 0f);
            }
            wasInWater = inWater;
        }

        /// <summary>Whether she is dragging along the surface: Player's swimSurfaceLoopSfx.</summary>
        public bool SwimSurfaceMoving =>
            Speed.X != 0f && ((State == StSwim && !SwimUnderwaterCheck()) ||
                              (State == StNormal && WaterAt(Pos.X, Pos.Y)));

        /// <summary>Player._IsOverWater: her hitbox, two pixels taller, touching water.</summary>
        bool IsOverWater()
        {
            HitboxAt(Pos.X, Pos.Y, out float l, out float t, out float r, out float b);
            foreach (var water in Waters)
                if (Overlap(l, t, r, b + 2f, water)) return true;
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

        /// <summary>The area past the monitors, if the hitbox at (x,y) has reached it.</summary>
        bool TryGetOffScreenAt(float x, float y, out Solid edge)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var solid in Solids)
                if (solid.OffScreen && Overlap(l, t, r, b, solid))
                {
                    edge = solid;
                    return true;
                }
            edge = default;
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

        /// <summary>Grounded check: must be on a platform top surface (not inside the platform).</summary>
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

        // Vanilla: upward dash extends the check distance from 3 to 5
        bool WallJumpCheck(int dir) => CollideAt(Pos.X + dir * (DashAttacking && DashDir.X == 0f && DashDir.Y == -1f ? 5 : 3), Pos.Y);
        bool ClimbCheck(int dir, int yAdd = 0) => CollideAt(Pos.X + dir * 2, Pos.Y + yAdd);
        bool DashAttacking => dashAttackTimer > 0f;  // dash-attack window (0.3s after dash ends)
        // upward-dash wall check: |X|<=0.2 and Y<=-0.75 → SuperWallJump
        bool SuperWallJumpAngleCheck => Math.Abs(DashDir.X) <= 0.2f && DashDir.Y <= -0.75f;

        // ===== Movement (ported Actor.MoveH/MoveV: sub-pixel accumulate + integer steps) =====
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
                bool blocked = false, held = false;
                IntPtr hit = IntPtr.Zero;
                foreach (var s in Solids)
                {
                    if (!Overlap(l, t, r, b, s)) continue;
                    bool inside = Overlap(l0, t0, r0, b0, s);
                    // A window border only collides from the outside: one opening around her
                    // would otherwise swallow her, with no way back out.  A DreamBlock holds
                    // her as it does in Celeste, where Actor.MoveHExact tests the destination
                    // and nothing else -- being inside one is meant to be a trap, and a dash
                    // is always the way out of it.
                    if (s.Dream || !inside) { hit = s.Id; blocked = true; held = inside; break; }
                }
                if (blocked)
                {
                    counter.X = 0;
                    if (!notifyCollision) return;
                    // Player.OnCollideH's first line, before any of its branches. It sits
                    // out here because the port hoists the dash-collide question out of
                    // OnCollideH, and vanilla clears the flag ahead of that too.
                    canCurveDash = false;
                    // Held, not hit.  Nothing struck anything, so there is no impact to report:
                    // reporting one every frame she is in there would repeat its sound and its
                    // squash, and she is meant to simply sit in the block.  A dash reports as
                    // usual, because that is where the dream dash out of it begins.
                    // Player.OnCollideH's first question, before anything of its own: was
                    // this a dash into something that answers dashes, along the dash. The
                    // solid's owner answers through OnDashCollide, and the answer decides
                    // whether the ordinary collision still happens. NormalOverride means
                    // "answered, but collide as usual", and skips the red-dash exemption the
                    // way vanilla's does; StRedDash is unreachable here today, and the guard
                    // is ported with the rest so that it stays true if that changes.
                    if (DashAttacking && Math.Sign(DashDir.X) == sign && hit != IntPtr.Zero &&
                        OnDashCollide != null)
                    {
                        DashCollisionResults result = OnDashCollide(hit, new PointF(sign, 0f));
                        if (result == DashCollisionResults.NormalOverride)
                            result = DashCollisionResults.NormalCollision;
                        else if (State == StRedDash) result = DashCollisionResults.Ignore;
                        if (result == DashCollisionResults.Rebound)
                        { Rebound(-Math.Sign(Speed.X)); return; }
                        if (result == DashCollisionResults.Ignore) return;
                    }
                    if (held && !DashAttacking) Speed.X = 0f;
                    else OnCollideH(sign);
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
                bool blocked = false, held = false;
                IntPtr hit = IntPtr.Zero;
                foreach (var s in Solids)
                {
                    if (!Overlap(l, t, r, b, s)) continue;
                    bool inside = Overlap(l0, t0, r0, b0, s);
                    if (s.Dream || !inside) { hit = s.Id; blocked = true; held = inside; break; }
                }
                if (blocked)
                {
                    counter.Y = 0;
                    if (!notifyCollision) return;
                    canCurveDash = false;   // Player.OnCollideV's first line; see MoveHExact.
                    // The vertical half has no NormalOverride mapping in vanilla; a vertical
                    // rebound is straight up, with no sideways part.
                    if (DashAttacking && Math.Sign(DashDir.Y) == sign && hit != IntPtr.Zero &&
                        OnDashCollide != null)
                    {
                        DashCollisionResults result = OnDashCollide(hit, new PointF(0f, sign));
                        if (State == StRedDash) result = DashCollisionResults.Ignore;
                        if (result == DashCollisionResults.Rebound) { Rebound(); return; }
                        if (result == DashCollisionResults.Ignore) return;
                    }
                    // Held, not hit: see MoveHExact.  Without this she lands on the block she
                    // is sitting in every single frame, footstep and all.
                    if (held && !DashAttacking) Speed.Y = 0f;
                    else OnCollideV(sign);
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
            State = StDreamDash;
            dreamDashCanEndTimer = 0.1f;
            dreamDashAnimTimer = 0.16f;
            dreamJump = false;   // DreamDashBegin
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

            // Dash horizontal wall hit: vertical corner correction ±1..4 (prefer snapping down to ground)
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
                // Air-dash landing: horizontal corner correction ±1..4, slide onto platform edges (dash state only; vanilla)
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
                // Down-diagonal dash landing → wavedash: convert to crouched ground dash (vanilla is state-agnostic; same conditions)
                // Press jump on the landing frame → hyper 325 launch; core of air down-diagonal dash + land-jump
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
                // Ceiling hit: upward corner correction (extends to 5px on upward dash)
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
                // Vanilla clears the dash-attack window on both vertical collisions, not
                // just landings: bonking a ceiling ends the dash attack, so it cannot
                // still feed a super wall jump afterwards.
                dashAttackTimer = 0f;
                gliderBoostTimer = 0f;
                // Vanilla: ceiling hit cancels variable jump (prevents keeping low-gravity arc after headbonk)
                if (varJumpTimer < 0.15f) varJumpTimer = 0;
                return false;
            }
        }

        // ===== Input buffering =====
        public void BufferJump()
        {
            // Celeste.Input builds Jump as VirtualButton(binding, pad, bufferTime: 0.08f, ...),
            // the same buffer Dash and CrouchDash use - not the 0.1s jump grace time.
            jumpBufferTimer = 0.08f;
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

        /// <summary>Reset to a position: clear speed/state/dashes/stamina/timers; reset hair.</summary>
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
            canCurveDash = false;
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
            lastAim = new PointF(Facing, 0);
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
            carryOffset = new PointF(0f, -12f);
            dreamDashCanEndTimer = 0f;
            dreamDashAnimTimer = dreamDashOutTimer = 0f;
            dreamJump = false;
            throwAnimTimer = 0f;
            gliderBoostTimer = 0f;
            gliderBoostDir = PointF.Empty;
            dreamDashEntryPos = pos;
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
            if (!Ghost) Hair.Reset(new PointF(Pos.X, Pos.Y - 9), Facing);
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
            if (!Ghost) Hair.MoveBy(x, y);
            if (Holding != null)
                Holding.Carry(new PointF(Holding.Pos.X + x, Holding.Pos.Y + y));
        }

        bool CanDash => (dashBufferTimer > 0 || crouchDashBufferTimer > 0) &&
                        dashCooldownTimer <= 0 && Dashes > 0 && !BeingDragged;
        bool IsTired => IsLowStamina;

        public void RefillDash()
        {
            Dashes = DashCapacity;
            hairFlashTimer = 0.12f;  // Vanilla: flash white 0.12s then snap back to red
            HairColor = FlashHairColor;
        }

        // ===== Main update =====
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
                    if (!Ghost) Hair.Reset(new PointF(Pos.X, Pos.Y - 9f), Facing);
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
            if (hairFlashTimer > 0) hairFlashTimer -= dt;  // hair flash-white timer
            if (varJumpTimer > 0) varJumpTimer -= dt;
            if (sweatJumpTimer > 0f)
            {
                sweatJumpTimer -= dt;
                if (sweatJumpTimer <= 0f) SweatAnimId = "idle";
            }
            if (minHoldTimer > 0f) minHoldTimer -= dt;
            if (dreamDashAnimTimer > 0f) dreamDashAnimTimer -= dt;
            if (dreamDashOutTimer > 0f) dreamDashOutTimer -= dt;
            if (throwAnimTimer > 0f) throwAnimTimer -= dt;

            // Vanilla only looks for ground while Speed.Y >= 0; rising never counts as
            // grounded, so it cannot refresh coyote time, stamina or the dash refill.
            // CheckGround still runs unconditionally to keep GroundId (the moving-window
            // ride link, which has no vanilla counterpart) tracking the platform below.
            bool touchingGround = CheckGround();
            onGround = !BeingDragged && touchingGround && Speed.Y >= 0f;

            if (onGround) highestAirY = Pos.Y;
            else highestAirY = Math.Min(highestAirY, Pos.Y);
            if (landingStumbleTimer > 0f) landingStumbleTimer -= dt;
            if (playFootstepOnLand > 0f) playFootstepOnLand -= dt;

            // Player.orig_Update refills in water before it refills on the ground, and asks
            // nothing else of it: swimming hands the dash back every frame she is in it.
            if (State == StSwim && dashRefillReady && Dashes < DashCapacity) RefillDash();

            if (onGround)
            {
                dreamJump = false;
                Stamina = ClimbMaxStamina;
                wallSlideTimer = WallSlideTime;  // Vanilla: reset wall-slide timer on landing
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

            // Player.orig_Update: `lastAim = Input.GetAimVector(Facing)`, refreshed every
            // frame right after Facing and before the state machine. DashCoroutine reads
            // it when it resumes, so this runs on the frame that decides a dash direction.
            // Update.freeze returns before this point, matching vanilla, where a frozen
            // Engine skips Player.Update entirely and leaves lastAim on its old value.
            lastAim = AimVector(input.AimX, input.AimY);

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
                        carryOffset = new PointF(
                            inv * inv * pickupCurveBegin.X + 2f * inv * eased * pickupCurveControl.X,
                            inv * inv * pickupCurveBegin.Y + 2f * inv * eased * pickupCurveControl.Y + eased * eased * -12f);
                    }
                    if (pickupTimer <= 0f)
                    {
                        Speed = pickupStoredSpeed;
                        Speed.Y = Math.Min(Speed.Y, 0f);
                        varJumpTimer = pickupStoredVarJump;
                        if (Holding != null)
                        {
                            if (Holding.SlowFall && gliderBoostTimer > 0f && gliderBoostDir.Y < 0f)
                            {
                                gliderBoostTimer = 0f;
                                Speed.Y = Math.Min(Speed.Y, -DashSpeed * Math.Abs(gliderBoostDir.Y));
                            }
                            else if (Holding.SlowFall && Speed.Y < 0f)
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

                int stateBeforeUpdate = State;
                int dashCountBeforeUpdate = DashSequenceCount;
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
                    case StSwim: SwimUpdate(dt, input); break;
                    case StDash: DashUpdate(dt, input); break;
                    case StDreamDash: DreamDashUpdate(dt, input); break;
                    case StElytra: ElytraUpdate(dt, input); break;
                    case StLaunch: LaunchUpdate(dt, input); break;
                }

                // Monocle's StateMachine runs the state update before the state coroutine.
                // DashCoroutine's first step, `yield return null`, is therefore consumed on
                // the frame the dash began (the frame NormalUpdate ran), and the aim lands on
                // the next frame that actually dispatches DashUpdate - which runs first, with
                // DashDir still zero, so jumping on it supers in Facing exactly as vanilla.
                // If DashUpdate already changed state, vanilla's StateMachine has replaced
                // the coroutine and the aim is never applied.
                // A Super Dashing re-dash inside DashUpdate replaces the coroutine during
                // this very frame, and a fresh coroutine spends this frame on its
                // `yield return null` -- so the aim it reads is the next frame's, not this
                // one's, exactly as when the dash began from another state.
                if (dashAimPending && stateBeforeUpdate == StDash &&
                    DashSequenceCount == dashCountBeforeUpdate)
                {
                    dashAimPending = false;
                    if (State == StDash) ApplyDashAim();
                }

                // Player.DashEnd, which the StateMachine runs on the way out of the dash.  The
                // aim above is genuinely lost when the dash is cut short, but the dash's own
                // effects are not: leaving on the first frame, as a super or hyper does when
                // jump and dash are pressed together, still sounds the dash.
                if (stateBeforeUpdate == StDash && State != StDash) CallDashEvents();

                // Player.orig_Update: an airborne horizontal dash snaps down to a floor
                // up to DashVFloorSnapDist away. This is what lets a dash aimed slightly
                // above the ground land grounded, which is the setup for supers/hypers
                // off a near-ground dash. (DashCorrectCheck only guards LedgeBlockers,
                // which the desktop pet has none of, so only the solid check remains.)
                if (!onGround && DashAttacking && DashDir.Y == 0f && CollideAt(Pos.X, Pos.Y + DashVFloorSnapDist))
                    MoveVExact(DashVFloorSnapDist);

                // Vanilla releases the duck hitbox while falling once standing
                // space is available (except during climb).
                if (Speed.Y > 0f && CanUnDuck && !onGround && jumpGraceTimer <= 0f && State != StClimb)
                    Ducking = false;
                }


                // Sitting inside a DreamBlock she cannot move at all, so keep her still
                // instead of letting gravity build a speed the collision quietly throws away
                // again: that leaves the fall speed flickering between frames, and with it the
                // animation.  A dash is exempt, being the way out of the block.
                if (State != StDreamDash && !DashAttacking && DreamAt(Pos.X, Pos.Y))
                {
                    Speed = PointF.Empty;
                    counter = PointF.Empty;
                }

                // Player.orig_Update tests the current state separately before
                // each axis. Exiting DreamDash therefore gets normal movement in
                // the exit frame, while entering it on H suppresses the V move.
                if (State != StDreamDash && !IsDead)
                    MoveH(Speed.X * dt);
                if (State != StDreamDash && !IsDead)
                    MoveV(Speed.Y * dt);

                // Vanilla skips level-bound enforcement during DreamDash. The
                // desktop perimeter is represented by real non-dream solids, so
                // allowing the naive move to reach them produces the proper death.
                if (State != StDreamDash)
                {
                    if (Pos.X < MinX + 4) { Pos.X = MinX + 4; if (Speed.X < 0) Speed.X = 0; }
                    if (Pos.X > MaxX - 4) { Pos.X = MaxX - 4; if (Speed.X > 0) Speed.X = 0; }
                }

                // Player.UpdateSprite's tail, which is where vanilla moves between swimming
                // and not: floating up out of the surface stops at it, and walking or
                // climbing into water starts the swim. Climbing in from above her own middle
                // is pushed up first, so she surfaces rather than sinking through the lip.
                if (State == StSwim)
                {
                    if (Speed.Y < 0f && Speed.Y >= SwimMaxRise && IsOverWater())
                        while (!SwimCheck())
                        {
                            Speed.Y = 0f;
                            if (CollideAt(Pos.X, Pos.Y + 1f)) break;
                            MoveVExact(1);
                        }
                }
                else if (State == StNormal && SwimCheck()) { State = StSwim; SwimBegin(); }
                else if (State == StClimb && SwimCheck())
                {
                    if (TryGetWaterAt(Pos.X, Pos.Y, out Solid pool) &&
                        Pos.Y - HitH / 2f < (pool.T + pool.B) / 2f)
                    {
                        while (SwimCheck() && !CollideAt(Pos.X, Pos.Y - 1f)) MoveVExact(-1);
                        if (SwimCheck()) { State = StSwim; SwimBegin(); }
                    }
                    else { State = StSwim; SwimBegin(); }
                }
                UpdateWaterSounds();
                }
            }
            else
            {
                Speed = new PointF(0, 0);
                wallSlideDir = 0;
            }

            // Expression recovery (vanilla 1.75/s)
            SpriteScaleX = Approach(SpriteScaleX, 1f, 1.75f * dt);
            SpriteScaleY = Approach(SpriteScaleY, 1f, 1.75f * dt);

            // Hair color: vanilla flashes white 0.12s then snaps to red; when Dash=0 lerps to blue at 6/s
            if (hairFlashTimer > 0)
            {
                HairColor = FlashHairColor;  // stay white while flashing
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
                // With no dash, lerp toward blue (6/s)
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

        /// <summary>
        /// Celeste.Player.UpdateCarry, called after the current sprite frame is
        /// selected so PlayerSprite.CarryYOffset belongs to that same frame.
        /// </summary>
        internal void UpdateCarryPosition(float spriteCarryYOffset)
        {
            if (Holding != null)
                Holding.Carry(new PointF(Pos.X + carryOffset.X,
                    Pos.Y + carryOffset.Y + spriteCarryYOffset * SpriteScaleY));
        }

        /// <summary>Hair-editor only: freeze physics/anim and run hair sim with given hx/hy (live preview).</summary>
        public void UpdateHairOnly(float dt, float hx, float hy)
        {
            float anchorY = -9f * SpriteScaleY;
            Hair.AfterUpdate(dt, new PointF(Pos.X + hx * Facing, Pos.Y + anchorY + hy), Facing, Dashes > 1);
        }

        void LaunchUpdate(float dt, PetInput input)
        {
            if (CanDash)
            {
                // Vanilla's LaunchUpdate returns StartDash(), which spends the dash and the
                // press that asked for it. Beginning one without doing that gave her the dash
                // back for free every time something launched her, and a dash held down came
                // out twice: once free here, once properly out of Normal a few frames later.
                bool crouchDash = ConsumeDashRequest();
                State = BeginDash(input, crouchDash);
                return;
            }
            if (Holding == null && input.GrabHeld && !IsTired && !Ducking && TryPickupHoldable()) return;
            Speed.Y = Approach(Speed.Y, 160f, (Speed.Y < 0f ? 450f : 225f) * dt);
            Speed.X = Approach(Speed.X, 0f, 200f * dt);
            if (Math.Sqrt(Speed.X * Speed.X + Speed.Y * Speed.Y) < 220f) State = StNormal;
        }

        // ===== Normal state =====
        void NormalUpdate(float dt, PetInput input)
        {
            if (Holding == null && input.GrabHeld && !IsTired && !Ducking && TryPickupHoldable())
            {
                Ducking = false;
                return;
            }

            // Grab wall to enter climb
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
            // Celeste.Player.NormalUpdate places its CanDash check inside the
            // Holding == null branch.  Keep the gate here rather than in the
            // CanDash property: a held Theo/glider blocks the normal-state dash,
            // while the input buffer remains available for a later frame after
            // the holdable is released.
            if (Holding == null && CanDash)
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

            // Duck / stand
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

            // Horizontal movement
            if (Ducking && onGround)
            {
                Speed.X = Approach(Speed.X, 0, DuckFriction * dt);
            }
            else
            {
                float mult = onGround ? 1f : AirMult;
                float maxRun = Holding != null && Holding.SlowRun ? 70f :
                    Holding != null && Holding.SlowFall && !onGround ? 108.00001f : MaxRun;
                if (Holding != null && Holding.SlowFall && !onGround) mult *= 0.5f;
                if (Math.Abs(Speed.X) > maxRun && Sign(Speed.X) == moveX)
                    Speed.X = Approach(Speed.X, maxRun * moveX, RunReduce * mult * dt);
                else
                    Speed.X = Approach(Speed.X, maxRun * moveX, RunAccel * mult * dt);
            }

            // Max fall speed. Vanilla drops the glider's fall control while a wall jump
            // is still forcing move-X, so the jelly does not float during that window.
            if (Holding != null && Holding.SlowFall && forceMoveXTimer <= 0f)
            {
                // vanilla reads Input.GliderMoveY here, not Input.MoveY
                float gliderTarget = input.GliderMoveY > 0 ? 120f : input.GliderMoveY < 0 ? 24f : 40f;
                maxFall = Approach(maxFall, gliderTarget, FastMaxAccel * dt);
            }
            else
            {
                maxFall = (input.MoveY == 1 && Speed.Y >= MaxFall)
                    ? Approach(maxFall, FastMaxFall, FastMaxAccel * dt)
                    : Approach(maxFall, MaxFall, FastMaxAccel * dt);
            }
            // Fast-fall stretch (vanilla: Speed.Y > 200 → lerp toward 0.5x1.5)
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
                    // Grab while wall-sliding → auto enter climb (vanilla ClimbTrigger)
                    if (input.GrabHeld && !IsTired) { EnterClimb(); State = StClimb; return; }
                    target = 160f + (20f - 160f) * (wallSlideTimer / WallSlideTime);
                }
                float gravMult = (Math.Abs(Speed.Y) < HalfGravThreshold && (input.JumpHeld || autoJump)) ? 0.5f : 1f;
                if (Holding != null && Holding.SlowFall && forceMoveXTimer <= 0f) gravMult *= 0.5f;
                Speed.Y = Approach(Speed.Y, target, Gravity * gravMult * dt);
            }
            else wallSlideDir = 0;

            // Variable jump (vanilla: AutoJump counts as jump held so post-dash jumps keep the arc)
            if (varJumpTimer > 0)
            {
                if (input.JumpHeld || autoJump) Speed.Y = Math.Min(Speed.Y, varJumpSpeed);
                else varJumpTimer = 0;
            }

            // Jump
            if (input.JumpPressed)
            {
                if (jumpGraceTimer > 0) Jump(input);
                else if (CanUnDuck && WallJumpCheck(1))
                {
                    if (Holding == null && Facing == 1 && input.GrabHeld && Stamina > 0) ClimbJump(input);
                    else if (DashAttacking && SuperWallJumpAngleCheck) SuperWallJump(-1);  // upward-dash wall hit
                    else WallJump(-1, input);
                }
                else if (CanUnDuck && WallJumpCheck(-1))
                {
                    if (Holding == null && Facing == -1 && input.GrabHeld && Stamina > 0) ClimbJump(input);
                    else if (DashAttacking && SuperWallJumpAngleCheck) SuperWallJump(1);  // upward-dash wall hit
                    else WallJump(1, input);
                }
            }

        }

        bool TryPickupHoldable()
        {
            if (Holdables == null) return false;
            IPetHoldable nearest = null;
            float nearestSq = float.MaxValue;
            foreach (IPetHoldable holdable in Holdables)
            {
                if (!holdable.CanPickup(this)) continue;
                float dx = holdable.Pos.X - Pos.X, dy = holdable.Pos.Y - Pos.Y;
                float distanceSq = dx * dx + dy * dy;
                if (distanceSq < nearestSq) { nearest = holdable; nearestSq = distanceSq; }
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
            // PickupCoroutine assigns carryOffset = begin before its first yield.
            // This keeps the actor at the curve start on the pickup frame instead
            // of flashing at CarryOffsetTarget for one rendered frame.
            carryOffset = pickupCurveBegin;
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
            IPetHoldable held = Holding;
            held.Release(PointF.Empty, Solids);
            Holding = null;
            if (held is Glider)
                PlaySound("event:/new_content/char/madeline/glider_drop");
        }

        public void ReleaseHoldableForDrag(IPetHoldable holdable)
        {
            if (Holding != holdable) return;
            Holding.Release(PointF.Empty, Solids);
            Holding = null;
            if (holdable is Glider)
                PlaySound("event:/new_content/char/madeline/glider_drop");
            minHoldTimer = 0f;
            if (State == StPickup) EnterNormal();
        }

        public void ForgetHoldable(IPetHoldable holdable)
        {
            if (Holding != holdable) return;
            Holding = null;
            minHoldTimer = 0f;
            if (State == StPickup) EnterNormal();
        }

        public void SwatHoldable(int direction)
        {
            if (Holding == null) return;
            Holding.Release(new PointF(.8f * direction, -.25f), Solids);
            Holding = null;
        }

        float maxFall = MaxFall;

        void Jump(PetInput input, bool particles = true)
        {
            ConsumeJump();
            autoJump = false;
            jumpGraceTimer = 0;
            varJumpTimer = VarJumpTime;
            wallSlideTimer = WallSlideTime;
            wallBoostTimer = 0f;
            dashAttackTimer = 0f;  // Vanilla: jump clears the dash-attack window
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
            varJumpTimer = VarJumpTime;
            wallSlideTimer = WallSlideTime;
            wallBoostTimer = 0f;
            dashAttackTimer = 0f;  // Vanilla: super jump clears the dash-attack window
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
            dashAttackTimer = 0f;  // Vanilla: wall jump clears the dash-attack window
            gliderBoostTimer = 0f;
            Speed.X = WallJumpHSpeed * dir;
            Speed.Y = JumpSpeed;
            varJumpSpeed = Speed.Y;
            // Player.orig_WallJump only forces the long steering lock for a
            // SlowFall holdable (the jelly). Theo uses the normal neutral-wall-
            // jump branch, so moveX==0 leaves steering immediately available.
            if (Holding != null && Holding.SlowFall) { forceMoveX = dir; forceMoveXTimer = 0.26f; }
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
            // Upward dash into wall → super wall jump (170h, -160v, varTimer 0.25)
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
            // Vanilla SuperWallJump does not set forceMove (can turn immediately)
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
            dreamJump = false;   // vanilla ClimbJump clears it before jumping
            Jump(input, particles: false);
            // Vanilla wallBoost: undirected climb-jump does not push off immediately; hold away-from-wall within 0.2s → 130 accel + restore stamina
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

        // ===== Being pushed by something that moves =====
        // Solid.MoveHExact does two things to the actors around it: it carries the ones riding
        // it, and it pushes the ones it is moving into, handing each a squish callback. The
        // first of those is PetWindow's ride-along. This is the second.

        /// <summary>
        /// A solid has moved into her and she must give way. Steps a pixel at a time like
        /// Actor.MoveHExact, and squishes her against whatever stops her, as vanilla does by
        /// passing SquishCallback to the same call.
        /// </summary>
        /// <returns>False if the push ended in a crush.</returns>
        public bool PushH(int amount, Solid pusher)
        {
            int sign = Math.Sign(amount);
            for (int i = 0; i < Math.Abs(amount); i++)
            {
                if (!StepBlocked(sign, 0)) { Pos.X += sign; continue; }
                return OnSquish(pusher);
            }
            return true;
        }

        /// <summary>
        /// Solid.MoveHExact's first half: the window underfoot moves, and she goes with it.
        /// The original desktop mechanic, and the older of the two things a moving window does
        /// to her -- this one has been here since before anything was pushed by anything.
        /// </summary>
        /// <remarks>
        /// Only whole game pixels reach her position; the fraction is carried the way
        /// Actor.movementCounter carries one, so a slow drag is not rounded away to nothing.
        /// The fraction is also why the push must still look at the window she is riding: what
        /// is held back here leaves her a sliver inside its border, and inside is where a solid
        /// stops holding her up.
        /// </remarks>
        /// <summary>
        /// The solid carrying her, by Player.IsRiding: the one underfoot, or -- while she is
        /// climbing -- the one she has hold of.
        /// </summary>
        /// <remarks>
        /// Celeste asks this of each actor as a solid moves and carries whoever says yes; here
        /// it is asked the other way round, because a window that has moved wants to know who
        /// came with it. The climbing case is the one that reads oddly, and is vanilla's: a
        /// CollideCheck at Position + UnitX * Facing. So a window dragged by its title bar takes
        /// her along while she hangs off its side, and a floaty block she is holding on to sinks
        /// under her exactly as one she is standing on does.
        /// </remarks>
        public IntPtr RidingId
        {
            get
            {
                if (State != StClimb) return GroundId;
                // More than one solid can be carrying her, and only one link can be followed:
                // take the one her hand is on, which is the one the probe reaches into without
                // her already being inside it. Anything she is standing in answers the probe
                // too, and is not what she is holding.
                foreach (var s in Solids)
                    if (IsRiding(s) && !OverlapsHitbox(s)) return s.Id;
                return GroundId;
            }
        }

        /// <summary>
        /// Player.IsRiding: whether this solid moving would take her with it. Every solid asks
        /// for itself, and more than one can say yes -- a wall she is holding while her feet are
        /// still on the floor is two.
        /// </summary>
        public bool IsRiding(Solid solid)
        {
            if (IsDead || IsRespawning) return false;
            // Vanilla: dream dashing rides whatever it is inside; climbing rides what is under
            // her hand, a pixel in the way she faces; anything else rides what is a pixel below.
            if (State == StDreamDash)
            {
                HitboxAt(Pos.X, Pos.Y, out float dl, out float dt, out float dr, out float db);
                return Overlap(dl, dt, dr, db, solid);
            }
            float x = State == StClimb ? Pos.X + Facing : Pos.X;
            float y = State == StClimb ? Pos.Y : Pos.Y + 1f;
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            return Overlap(l, t, r, b, solid);
        }

        public void RideAlong(float dx, float dy)
        {
            if (RidingId != rideAlongId)
            {
                rideAlongId = RidingId;
                rideRemainder = PointF.Empty;
            }
            rideRemainder = new PointF(rideRemainder.X + dx, rideRemainder.Y + dy);
            int rideX = (int)Math.Round(rideRemainder.X, MidpointRounding.ToEven);
            int rideY = (int)Math.Round(rideRemainder.Y, MidpointRounding.ToEven);
            rideRemainder = new PointF(rideRemainder.X - rideX, rideRemainder.Y - rideY);
            if (rideX != 0 || rideY != 0) Pos = new PointF(Pos.X + rideX, Pos.Y + rideY);
        }

        /// <summary>She is riding nothing; the next ride starts its fraction afresh.</summary>
        public void EndRide() => rideAlongId = IntPtr.Zero;

        IntPtr rideAlongId;
        PointF rideRemainder;

        /// <summary>Whether she is inside anything where she stands.</summary>
        public bool CollidesHere => CollideAt(Pos.X, Pos.Y);

        /// <summary>Whether she would be inside anything standing there.</summary>
        public bool CollidesAt(float x, float y) => CollideAt(x, y);

        /// <summary>
        /// A solid that has just moved by (dx, dy) and now covers her. Decides whether that is
        /// a push at all, and if it is, shoves her no further than the solid itself travelled.
        /// </summary>
        /// <remarks>
        /// Celeste can shove an actor until it is clear of the solid, because an actor is never
        /// inside one to begin with: the most it is ever overlapping by is the distance the
        /// solid moved this frame. Neither holds here. A window can arrive around her, and it
        /// can arrive from the far side of the screen between two polls, and clearing it from
        /// the middle would mean throwing her the width of the window.
        ///
        /// So one limit: a solid never moves her further than it moved itself. A window
        /// sweeping into her keeps her flush against it, one frame at a time; a window that
        /// lands partly on her carries her the rest of the way over the frames that follow;
        /// and one she is standing inside drags her along as it goes, which is what a solid
        /// sweeping through a space does to what is in it.
        ///
        /// An earlier version asked instead whether she was inside the solid before it moved,
        /// and left her alone if she was. That reads well and is wrong: a push that does not
        /// quite clear her leaves her inside, so every frame after the first was skipped and
        /// the window walked through her.
        /// </remarks>
        /// <returns>False if the push ended in a crush.</returns>
        public bool SweptInto(Solid solid, float dx, float dy)
        {
            if (!OverlapsHitbox(solid)) return true;

            if (dx != 0f)
            {
                int want = ClearanceX(solid, Math.Sign(dx));
                int room = (int)Math.Ceiling(Math.Abs(dx));
                if (!PushH(Math.Sign(want) * Math.Min(Math.Abs(want), room), solid)) return false;
            }
            if (dy != 0f)
            {
                int want = ClearanceY(solid, Math.Sign(dy));
                int room = (int)Math.Ceiling(Math.Abs(dy));
                if (!PushV(Math.Sign(want) * Math.Min(Math.Abs(want), room), solid)) return false;
            }
            return true;
        }

        /// <summary>
        /// Whether her hitbox reaches a box -- a PlayerCollider carrying a Hitbox of its own,
        /// which is how a puffer is touched rather than by its own collider.
        /// </summary>
        public bool OverlapsBox(float left, float top, float right, float bottom)
        {
            HitboxAt(Pos.X, Pos.Y, out float l, out float t, out float r, out float b);
            return l < right && r > left && t < bottom && b > top;
        }

        /// <summary>
        /// Whether her hitbox reaches a circle -- what Monocle's Collide.RectToCircle answers
        /// for a Circle collider against hers, which is what a bumper carries.
        /// </summary>
        /// <remarks>
        /// Written as the nearest point on the box rather than as Monocle writes it, which is a
        /// point-inside test and then the circle against each edge in turn. They are the same
        /// question: the nearest point is inside when she is on it, and on the edge otherwise.
        /// </remarks>
        public bool OverlapsCircle(PointF centre, float radius)
        {
            HitboxAt(Pos.X, Pos.Y, out float l, out float t, out float r, out float b);
            float nearestX = Math.Clamp(centre.X, l, r), nearestY = Math.Clamp(centre.Y, t, b);
            float dx = centre.X - nearestX, dy = centre.Y - nearestY;
            return dx * dx + dy * dy <= radius * radius;
        }

        /// <summary>Whether her hitbox is inside this solid.</summary>
        public bool OverlapsHitbox(Solid solid)
        {
            HitboxAt(Pos.X, Pos.Y, out float l, out float t, out float r, out float b);
            return Overlap(l, t, r, b, solid);
        }

        /// <summary>How far along X she must go to be clear of it, pushed the way it moved.</summary>
        public int ClearanceX(Solid solid, int direction)
        {
            HitboxAt(Pos.X, Pos.Y, out float l, out float t, out float r, out float b);
            return direction > 0 ? (int)Math.Ceiling(solid.R - l) : -(int)Math.Ceiling(r - solid.L);
        }

        /// <summary>The same for Y.</summary>
        public int ClearanceY(Solid solid, int direction)
        {
            HitboxAt(Pos.X, Pos.Y, out float l, out float t, out float r, out float b);
            return direction > 0 ? (int)Math.Ceiling(solid.B - t) : -(int)Math.Ceiling(b - solid.T);
        }

        /// <summary>
        /// Set her down in the nearest free spot, without crushing her: what a window that
        /// jumped rather than moved gets, there being no shove to speak of. The search widens
        /// well past the squish wiggle, since a teleport can leave her deep inside something.
        /// </summary>
        public void DisplaceOutOfSolids()
        {
            if (!CollideAt(Pos.X, Pos.Y)) return;
            // Nearest first, and nearest means nearest: the squish wiggle walks its offsets in
            // a fixed order that would take a twenty pixel drop over a one pixel sidestep, and
            // at this range that is the difference between being nudged aside and being thrown.
            for (int radius = 1; radius <= DisplaceReach; radius++)
                for (int x = -radius; x <= radius; x++)
                    for (int y = -radius; y <= radius; y++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(y)) != radius) continue;
                        if (CollideAt(Pos.X + x, Pos.Y + y)) continue;
                        Pos = new PointF(Pos.X + x, Pos.Y + y);
                        return;
                    }
            // Nowhere within reach: leave her where she is rather than kill her for it. She is
            // inside something and can walk out, which is what she could always do.
        }

        /// <summary>
        /// How far a displacement may carry her. A window border is a few pixels thick, so
        /// stepping out of one is a small move; the rest of the room is for the odd wide frame.
        /// Beyond this she is left alone, because moving her further than she can see herself
        /// move is worse than leaving her standing in a window.
        /// </summary>
        const int DisplaceReach = 12;

        /// <summary>The same, vertically.</summary>
        public bool PushV(int amount, Solid pusher)
        {
            int sign = Math.Sign(amount);
            for (int i = 0; i < Math.Abs(amount); i++)
            {
                if (!StepBlocked(0, sign)) { Pos.Y += sign; continue; }
                return OnSquish(pusher);
            }
            return true;
        }

        /// <summary>
        /// Whether one pixel that way is into something. A solid she is already inside does not
        /// count, which is the rule MoveHExact uses: being pushed out of the thing pushing her
        /// is the whole point, and only a second solid can stop it.
        /// </summary>
        bool StepBlocked(int dx, int dy)
        {
            HitboxAt(Pos.X, Pos.Y, out float l0, out float t0, out float r0, out float b0);
            HitboxAt(Pos.X + dx, Pos.Y + dy, out float l, out float t, out float r, out float b);
            foreach (var s in Solids)
            {
                if (!Overlap(l, t, r, b, s)) continue;
                if (!Overlap(l0, t0, r0, b0, s)) return true;
            }
            return false;
        }

        /// <summary>
        /// Player.OnSquish: duck out of it, wiggle out of it, or die. Ducking is tried first
        /// and only when she is not already ducking or climbing; then every offset within
        /// three pixels across and five up or down; and only then is she crushed.
        /// </summary>
        /// <returns>False if she was crushed.</returns>
        bool OnSquish(Solid pusher)
        {
            bool ducked = false;
            if (!Ducking && State != StClimb)
            {
                ducked = true;
                Ducking = true;
                if (!CollideAt(Pos.X, Pos.Y)) return true;
            }
            if (TrySquishWiggle())
            {
                if (ducked && CanUnDuck) Ducking = false;
                return true;
            }
            if (ducked) Ducking = false;
            // A Celeste room reload puts her back at the map's spawn; a desktop has none, so
            // every death here has to say where she comes back. Without this a crush reuses
            // whatever position was last reset to, which is wherever she happened to start.
            PointF centre = new PointF((pusher.L + pusher.R) / 2f, (pusher.T + pusher.B) / 2f);
            PointF away = new PointF(Pos.X - centre.X, Pos.Y - 5f - centre.Y);
            if (away.IsEmpty) away = new PointF(0f, -1f);
            deathRespawnPos = FindNearbySafeRespawn(centre, away);
            Die(PointF.Empty);
            return false;
        }

        /// <summary>Actor.TrySquishWiggle: the first free spot within three across, five down.</summary>
        bool TrySquishWiggle(int wiggleX = 3, int wiggleY = 5)
        {
            for (int x = 0; x <= wiggleX; x++)
                for (int y = 0; y <= wiggleY; y++)
                {
                    if (x == 0 && y == 0) continue;
                    for (int sx = 1; sx >= -1; sx -= 2)
                        for (int sy = 1; sy >= -1; sy -= 2)
                            if (!CollideAt(Pos.X + x * sx, Pos.Y + y * sy))
                            {
                                Pos = new PointF(Pos.X + x * sx, Pos.Y + y * sy);
                                return true;
                            }
                }
            return false;
        }

        // ===== Swim state =====
        // Player.SwimBegin/SwimUpdate. Vanilla's numbers, in the order it applies them:
        // rise 60/s while neutral, 80/s under steering (60 while fully under), approached at
        // 600/s, or 400/s when already going faster than 80 the same way -- so a dash carries
        // into the water and is dragged down rather than cut off.
        const float SwimYSpeedMult = 0.5f;
        const float SwimMaxRise = -60f;
        const float SwimVDeccel = 600f;
        const float SwimMax = 80f;
        const float SwimUnderwaterMax = 60f;
        const float SwimAccel = 600f;
        const float SwimReduce = 400f;
        const float SwimDashSpeedMult = 0.75f;

        void SwimBegin()
        {
            if (Speed.Y > 0f) Speed.Y *= SwimYSpeedMult;
            Stamina = ClimbMaxStamina;
        }

        void SwimUpdate(float dt, PetInput input)
        {
            if (!SwimCheck()) { State = StNormal; return; }
            if (CanUnDuck) Ducking = false;
            if (CanDash)
            {
                bool crouchDash = ConsumeDashRequest();
                State = BeginDash(input, crouchDash);
                return;
            }

            // Nothing here lets go of what she is carrying, and that is the game: vanilla's
            // one Throw() lives in NormalUpdate, so a hold cannot be released while swimming
            // there either. Verified in Celeste itself, not only in its source. Mods that put
            // something to carry in water inherit the same trap; adding a release here would
            // be inventing the mechanic they are missing.
            bool underwater = SwimUnderwaterCheck();
            // Out of the water onto a wall, which is how a pool with a lip is left. Vanilla
            // reads MoveVExact's collision to know there is room above; here that is the same
            // question asked before moving.
            if (!underwater && Speed.Y >= 0f && input.GrabHeld && !IsTired && CanUnDuck &&
                Math.Sign(Speed.X) != -Facing && ClimbCheck(Facing) &&
                !CollideAt(Pos.X, Pos.Y - 1f))
            {
                MoveVExact(-1);
                Ducking = false;
                EnterClimb();
                State = StClimb;
                return;
            }

            // Input.Feather.Value.SafeNormalize(): the eight directions as a unit vector.
            float aimX = input.FeatherX, aimY = input.FeatherY;
            float length = (float)Math.Sqrt(aimX * aimX + aimY * aimY);
            if (length > 0f) { aimX /= length; aimY /= length; }

            float max = underwater ? SwimUnderwaterMax : SwimMax;
            Speed.X = Approach(Speed.X, max * aimX,
                (Math.Abs(Speed.X) > SwimMax && Math.Sign(Speed.X) == Math.Sign(aimX)
                    ? SwimReduce : SwimAccel) * dt);
            if (aimY == 0f && SwimRiseCheck())
            {
                // She floats up to the surface on her own, and stops rising once out of it.
                Speed.Y = Approach(Speed.Y, SwimMaxRise, SwimVDeccel * dt);
            }
            else if (aimY >= 0f || underwater)
            {
                Speed.Y = Approach(Speed.Y, SwimMax * aimY,
                    (Math.Abs(Speed.Y) > SwimMax && Math.Sign(Speed.Y) == Math.Sign(aimY)
                        ? SwimReduce : SwimAccel) * dt);
            }

            if (!underwater && input.MoveX != 0 && CollideAt(Pos.X + input.MoveX, Pos.Y) &&
                !CollideAt(Pos.X + input.MoveX, Pos.Y - 3f))
                ClimbHop();

            if (input.JumpPressed && SwimJumpCheck())
            {
                Jump(input);
                State = StNormal;
            }
        }

        // ===== Climb state =====
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
                    if (onGround)
                    {
                        if (Speed.Y > 0f) Speed.Y = 0f;  // vanilla stops the slide immediately on landing
                        ty = 0;
                    }
                }
                else slipping = true;
            }
            else slipping = true;
            // Vanilla samples lastClimbMove before the slip override, so slipping still
            // counts as "holding still" and keeps draining the 10/s still cost.
            lastClimbMove = Sign(ty);
            if (slipping && SlipCheck()) ty = ClimbSlipSpeed;
            // Vanilla: climb speed uses Approach (accel ClimbAccel=900), not an instant set
            Speed.Y = Approach(Speed.Y, ty, ClimbAccel * dt);

            if (input.MoveY != 1 && Speed.Y > 0f && !CollideAt(Pos.X + Facing, Pos.Y + 1f))
                Speed.Y = 0f;

            if (!InfiniteStamina && climbNoMoveTimer <= 0f)
            {
                if (lastClimbMove < 0) Stamina -= 45.4545f * dt;
                else if (lastClimbMove == 0) Stamina -= ClimbStillCost * dt; // Vanilla: holding still on a wall drains 10 stamina/s
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
            forceMoveX = 0;                // vanilla forceMoveX = 0
            forceMoveXTimer = 0.2f;        // ignore move-X input for 0.2s
            fastJump = false;
            PlaySound("event:/char/madeline/climb_ledge");
        }

        void MoveVExactLocal(int n) => MoveVExact(n);

        // ===== Dash state =====
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
            calledDashEvents = false;   // DashBegin
            DashSequenceCount++;
            dashStartedOnGround = onGround;
            dashCooldownTimer = DashCooldown;
            dashRefillCooldownTimer = DashRefillCooldown;
            dashAttackTimer = DashAttackTime;
            if (SuperDashing) dashAttackTimer += SuperDashAttackBonus;
            gliderBoostTimer = 0.55f;
            wallSlideTimer = WallSlideTime;
            freezeTimer = FreezeFramesEnabled ? 0.05f : 0f; // vanilla Freeze(0.05)
            beforeDashSpeed = Speed;

            if (!onGround && Ducking && CanUnDuck) Ducking = false;
            else if (!Ducking && (crouchDash || input.MoveY == 1)) Ducking = true;

            // The direction is deliberately NOT captured here. Vanilla refreshes lastAim
            // every frame and DashCoroutine reads it when it resumes, which is after the
            // 0.05s freeze - so the aim held ~4 frames after the press is the one that
            // counts. That leniency is what makes a hand demo dash possible: press down
            // with dash to duck here, then swap to a horizontal aim before it is sampled.
            Speed = new PointF(0, 0);
            DashDir = new PointF(0, 0);
            dashAimPending = true;
            canCurveDash = true;

            // DashCoroutine's wait. Vanilla picks it when the coroutine resumes rather
            // than here, which is the same instant in every way that shows: DashUpdate
            // never drains it before the aim has landed.
            dashTime = SuperDashing ? SuperDashTime : DashTime;
            return StDash;
        }

        void ApplyDashAim()
        {
            // DashCoroutine: `Vector2 value = lastAim;` on the frame the coroutine resumes.
            PointF dir = lastAim;

            PointF speed = new PointF(dir.X * DashSpeed, dir.Y * DashSpeed);
            if (Sign(beforeDashSpeed.X) == Sign(speed.X) && Math.Abs(beforeDashSpeed.X) > Math.Abs(speed.X))
                speed.X = beforeDashSpeed.X;

            DashDir = dir;
            gliderBoostDir = dir;
            Speed = speed;
            // DashCoroutine: water takes a quarter off a dash begun in it, and answers with
            // its own sound over the dash's.
            if (WaterAt(Pos.X, Pos.Y))
            {
                Speed = new PointF(Speed.X * SwimDashSpeedMult, Speed.Y * SwimDashSpeedMult);
                PlaySound("event:/char/madeline/water_dash_gen");
            }

            // Grounded down-diagonal dash → crouch dash (1.2x, vanilla). DashCoroutine
            // tests onGround as of this frame, not the state captured at DashBegin.
            if (onGround &&
                DashDir.X != 0 && DashDir.Y > 0 && Speed.Y > 0 &&
                !DreamAt(Pos.X, Pos.Y + 1f))
            {
                DashDir = new PointF(Sign(DashDir.X), 0);
                Speed = new PointF(Speed.X * 1.2f, 0);
                Ducking = true;
            }
            if (DashDir.X != 0) Facing = Sign(DashDir.X);
            CallDashEvents();
        }

        /// <summary>Player.CallDashEvents: the dash's own effects, fired once per dash.</summary>
        /// <remarks>
        /// DashCoroutine only reaches these on the dash's second frame, so a super or hyper
        /// taken on the first one -- jump and dash pressed together, or a jump right on the
        /// dash's heels -- left the dash with no sound at all.  Vanilla covers that by calling
        /// this from DashEnd as well, the flag making whichever comes second a no-op.  The aim
        /// may not have been sampled yet when it arrives that way, and a dash with no direction
        /// yet takes its side from Facing.
        /// </remarks>
        void CallDashEvents()
        {
            if (calledDashEvents) return;
            calledDashEvents = true;
            bool rightSound = DashDir.X == 0f && DashDir.Y == 0f
                ? Facing > 0
                : DashDir.Y < 0f || (DashDir.Y == 0f && DashDir.X > 0f);
            PlaySound(LastDashWasTwo
                ? (rightSound ? "event:/char/madeline/dash_pink_right" : "event:/char/madeline/dash_pink_left")
                : (rightSound ? "event:/char/madeline/dash_red_right" : "event:/char/madeline/dash_red_left"));
        }

        void DashUpdate(float dt, PetInput input)
        {
            // Super Dashing steers the dash toward the aim at 240 deg/s, until the two are
            // within about eight degrees of each other, and gives up once she has hit
            // something. On the frame before the aim lands Speed is still zero, so this
            // sits out the first frame of every dash on its own.
            if (SuperDashing && canCurveDash && (input.AimX != 0 || input.AimY != 0) &&
                (Speed.X != 0f || Speed.Y != 0f))
            {
                PointF aim = CorrectDashPrecision(lastAim);
                PointF heading = SafeNormalize(Speed.X, Speed.Y);
                float towards = aim.X * heading.X + aim.Y * heading.Y;
                if (towards >= -0.1f && towards < 0.99f)
                {
                    Speed = RotateTowards(Speed, Angle(aim), DashCurveRate * dt);
                    DashDir = CorrectDashPrecision(SafeNormalize(Speed.X, Speed.Y));
                }
            }

            // And a dash spent mid-dash restarts it rather than waiting for Normal.
            // StateMachine.ForceState(2) on the state it is already in runs DashEnd and
            // then DashBegin, and replaces the coroutine, which begins by giving up a
            // frame -- so the new aim lands one frame later, exactly as an ordinary
            // dash's does. dashCooldownTimer keeps this out of the dash's own first
            // frames, so the DashEnd here has always sounded already.
            if (SuperDashing && CanDash)
            {
                bool crouchDash = ConsumeDashRequest();  // Player.StartDash
                CallDashEvents();                        // Player.DashEnd
                State = BeginDash(input, crouchDash);    // Player.DashBegin
                return;
            }

            if (Holding == null && (DashDir.X != 0f || DashDir.Y != 0f) &&
                input.GrabHeld && !IsTired && CanUnDuck && TryPickupHoldable())
                return;

            // Jump cancels dash → Super / Hyper / Ultra / wall jump (vanilla DashUpdate prioritizes jump over everything)
            if (input.JumpPressed)
            {
                // Jump during ground/horizontal dash → super jump: super=260; crouch dash=hyper=325; landing-frame=ultra
                if (Math.Abs(DashDir.Y) < 0.1f && jumpGraceTimer > 0 && CanUnDuck)
                {
                    SuperJump();
                    EnterNormal();
                    return;
                }
                // Upward dash into wall → SuperWallJump (170h, -160v), else normal wall jump
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
            // Vanilla's dash length is a coroutine wait, and Monocle's Coroutine tests the
            // wait before decrementing it, so the dash ends on the frame the timer is no
            // longer positive rather than the frame it reaches zero. Draining it here (after
            // the jump checks, and never on the pending-aim frame, whose wait has not been
            // set yet) reproduces vanilla's super/hyper input window frame for frame.
            if (dashAimPending) return;
            if (dashTime > 0f) { dashTime -= dt; return; }

            if (DashDir.Y <= 0)
                Speed = new PointF(DashDir.X * EndDashSpeed, DashDir.Y * EndDashSpeed);
            if (Speed.Y < 0) Speed.Y *= EndDashUpMult;
            autoJump = true; // Vanilla DashCoroutine ends with AutoJump=true: keep half-gravity / variable jump
            EnterNormal();
        }

        // Player.DreamDashUpdate: DreamDash movement itself remains collisionless;
        // after the minimum 0.1s, leaving every dream block returns to Normal and
        // restores the resources the original DreamDashEnd restores.
        void DreamDashUpdate(float dt, PetInput input)
        {
            // Vanilla calls NaiveMove before testing the new overlap.  Keeping this
            // order is frame-critical for dream jumps, double jumps and dream tech.
            PointF beforeMove = Pos;
            PointF beforeMoveCounter = counter;
            MoveH(Speed.X * dt);
            MoveV(Speed.Y * dt);
            dreamDashCanEndTimer -= dt;
            // Desktop rule, deliberately not vanilla: a window can hang past the edge of the
            // display, and dream dashing on inside it would carry her where the user can
            // neither see nor follow.  The area beyond the display is already solid, so
            // refusing to treat it as somewhere the dash may continue is the whole rule: she
            // falls through to the ordinary "dashed into a solid" path below and meets the
            // display boundary exactly as she would any other wall, assist-mode bounce
            // included.
            if (!TryGetOffScreenAt(Pos.X, Pos.Y, out _))
            {
                if (DreamAt(Pos.X, Pos.Y)) return;
                if (dreamDashCanEndTimer > 0f) return;
            }

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
                    if (Invincible)
                    {
                        // Player.DreamDashUpdate has a dedicated Assist Mode path:
                        // undo NaiveMove, reverse velocity, and remain in DreamDash.
                        Pos = beforeMove;
                        counter = beforeMoveCounter;
                        Speed = new PointF(-Speed.X, -Speed.Y);
                        PlaySound("event:/game/general/assist_dreamblockbounce");
                        return;
                    }
                    Pos = original;
                    DieFromDreamDash();
                    return;
                }
            }

            bool enterClimb = false;
            if (input.JumpPressed && Math.Abs(DashDir.X) > 0.01f)
            {
                dreamJump = true;   // vanilla sets it before this jump, for the sfx
                Jump(input);
            }
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
            // DreamDashEnd: a horizontal exit re-grants the ordinary coyote window even when
            // the exit-frame jump above already consumed it. That, and nothing else, is what
            // makes the vanilla dream double jump possible.
            if (Math.Abs(DashDir.X) > 0.01f)
            {
                jumpGraceTimer = JumpGraceTime;
                dreamJump = true;
            }
            else jumpGraceTimer = 0f;
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

        void DieFromDreamDash()
        {
            if (Invincible || IsDead) return;
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

        // ===== Animation selection (ported orig_UpdateSprite) =====
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
            else if (State == StSwim)
            {
                // Player.orig_UpdateSprite. The swim animation reads MoveY, not the feather
                // she steers with, so a controller half-pushed up still swims level.
                id = input.MoveY > 0 ? "swimDown" : input.MoveY < 0 ? "swimUp" : "swimIdle";
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
                if (Holding == null && moveX != 0 && CollideAt(Pos.X + moveX, Pos.Y))
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

            // Do not override until flip finishes
            if (AnimId == "flip" && !AnimFinished && id != "flip") { UpdateIdleFidget(dt, input, false); return; }
            // Do not override until idle fidget finishes
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
    /// Hair simulation (ported from PlayerHair.AfterUpdate).
    /// Hand-tune hair: edit the constants below and rebuild (dotnet build):
    ///   Count          hair segment count (more = longer)
    ///   HangDown       per-segment downward offset (px)
    ///   BackLean       per-segment behind offset (px; walk trail strength)
    ///   ApproachSpeed  hair follow speed (px/s): lower = floatier trail; higher = sticks to head
    ///   MaxSegment     max spacing between adjacent segments (px): higher = longer hair
    ///   WaveSpeed      idle sway speed
    /// Hair root anchor height is anchorY in Player.Update (currently -9 x squash scale);
    /// Per-frame anchor tweaks (hx/hy/bangs facing) live in HairMeta.cs.
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

            // Player.UpdateHair: with two dashes hair becomes 5 nodes and uses separate strong-wind sine params.
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
