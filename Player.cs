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
        const int ClimbFrameCount = 7;          // 攀爬动画帧数（climb00-06；climb07/08 扭头帧走 climbTurn，09-14 废案）
        const float WallSpeedRetentionTime = 0.06f;  // 撞墙保留速度时限（原作 wallSpeedRetentionTimer，约 4 帧）
        const float ClimbJumpCost = 27.5f;
        const float ClimbUpSpeed = -45f;
        const float ClimbDownSpeed = 80f;
        const float ClimbAccel = 900f;         // 攀爬移动加速度（原作 Approach）
        const float ClimbStillCost = 10f;      // 静止爬墙每秒体力消耗
        const float ClimbHopY = -120f;         // 爬过墙顶弹起速度
        const float ClimbHopX = 100f;          // 爬过墙顶水平推力
        const int DashingUpwardCornerCorrection = 5; // 上冲天花板角修正距离

        public const int StNormal = 0;
        public const int StClimb = 1;
        public const int StDash = 2;

        // 头发颜色（Player.cs）
        public static readonly Color NormalHairColor = Color.FromArgb(0xAC, 0x32, 0x32);
        public static readonly Color UsedHairColor = Color.FromArgb(0x44, 0xB7, 0xFF);
        public static readonly Color FlashHairColor = Color.White;

        // ===== 状态 =====
        public PointF Pos;          // 脚底中心（游戏像素）
        public PointF Speed;
        public int Facing = 1;      // -1 左 / 1 右
        public bool Ducking;
        public int State;
        public int Dashes = 1;
        public bool FreezeFrameEnabled = true;   // 冲刺起手冻结帧开关（原作 Celeste.Freeze(0.05)）
        public float Stamina = ClimbMaxStamina;
        public bool onGround;
        public IntPtr GroundId;
        public PointF DashDir;

        // 表现
        public float SpriteScaleX = 1f, SpriteScaleY = 1f;
        public string AnimId = "idle";
        public int ClimbFrame;
        public string CurrentFrameId;   // 由窗口每帧同步（头发锚点跟随当前帧）
        public Color HairColor = NormalHairColor;
        public readonly PlayerHairSim Hair = new PlayerHairSim();

        // 汗水（原作 sweatSprite）：窗口用 sweatAnimator 播放；SweatRestart=true 表示该帧需强制重播（原作 Play(restart:true)）
        public string SweatId = "idle";
        public bool SweatRestart;

        // 计时器
        float jumpGraceTimer;
        float varJumpTimer;
        float varJumpSpeed;
        float dashCooldownTimer;
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
        bool dashStartedOnGround;
        PointF beforeDashSpeed;
        bool autoJump;          // 冲刺结束后的自动跳保持（原作 AutoJump：半重力/可变跳高视为按住跳）
        int lastClimbMove;
        bool fastJump;
        float climbAnimAccum;
        float idleTimer;
        string fidgetId;
        float impactSpeed;
        float tiredFlashTimer;     // 疲劳红闪计时（原作 flash 每 0.05s 翻转）
        public bool TiredFlash;    // 原作 Render：IsTired && flash → Sprite.Color = Red

        // 保留速度（cornerboost）：撞墙瞬间保存水平速度，时限内墙不再阻挡则返还
        float wallSpeedRetained;
        float wallSpeedRetentionTimer;

        // 外部数据
        public List<Solid> Solids = new List<Solid>();
        public float MinX = -10000, MaxX = 10000;   // 屏幕边界（游戏像素）
        public bool BeingDragged;

        PointF counter;  // 亚像素位移累积（Actor.movementCounter）
        readonly Random rng = new Random();

        static float Approach(float val, float target, float maxMove)
            => val > target ? Math.Max(val - maxMove, target) : Math.Min(val + maxMove, target);
        static int Sign(float v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        // ===== 碰撞 =====
        float HitH => Ducking ? 6f : 11f;

        void HitboxAt(float x, float y, out float l, out float t, out float r, out float b)
        { l = x - 4; r = x + 4; t = y - HitH; b = y; }

        void HitboxAt(float x, float y, float h, out float l, out float t, out float r, out float b)
        { l = x - 4; r = x + 4; t = y - h; b = y; }

        static bool Overlap(float l, float t, float r, float b, in Solid s)
            => l < s.R && r > s.L && t < s.B && b > s.T;

        /// <summary>原始重叠检测（不做内外语义）。</summary>
        bool CollideAt(float x, float y)
        {
            HitboxAt(x, y, out float l, out float t, out float r, out float b);
            foreach (var s in Solids)
                if (Overlap(l, t, r, b, s)) return true;
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

        bool CanUnDuck => !CollideAt(Pos.X, Pos.Y, 11f);

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

        void MoveHExact(int n)
        {
            int sign = Math.Sign(n);
            while (n != 0)
            {
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
                    // 撞墙保留速度（cornerboost）：撞墙瞬间保存水平速度，时限内墙不再阻挡则返还
                    if (wallSpeedRetentionTimer <= 0f)
                    {
                        wallSpeedRetained = Speed.X;
                        wallSpeedRetentionTimer = WallSpeedRetentionTime;
                    }
                    if (!OnCollideH(sign)) return;
                    continue; // 角修正成功，继续
                }
                Pos.X += sign;
                n -= sign;
            }
        }

        void MoveVExact(int n)
        {
            int sign = Math.Sign(n);
            while (n != 0)
            {
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
                    if (!OnCollideV(sign)) return;
                    continue;
                }
                Pos.Y += sign;
                n -= sign;
            }
        }

        bool OnCollideH(int sign)
        {
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
                            Pos.Y += i * d;
                            Pos.X += sign;
                            return true;
                        }
                    }
                }
            }
            Speed.X = 0;
            return false;
        }

        bool OnCollideV(int sign)
        {
            if (sign > 0)
            {
                impactSpeed = Speed.Y;
                // 空中冲刺落地：水平角修正 ±1..4，滑上平台边缘（仅冲刺状态，原作同款）
                if (State == StDash && !dashStartedOnGround)
                {
                    if (Speed.X <= 0.01f)
                        for (int n = -1; n >= -DashCornerCorrection; n--)
                            if (!CheckGroundAt(Pos.X + n, Pos.Y)) { Pos.X += n; Pos.Y += 1; return true; }
                    if (Speed.X >= -0.01f)
                        for (int n = 1; n <= DashCornerCorrection; n++)
                            if (!CheckGroundAt(Pos.X + n, Pos.Y)) { Pos.X += n; Pos.Y += 1; return true; }
                }
                // 斜下冲刺落地 → wavedash / 凌波微步：转为蹲姿地面冲刺（原作状态无关，条件同款）
                // 落地瞬间按 C → hyper 325 弹射；这就是「空中斜下冲 + 落地跳」的核心
                if (DashDir.X != 0 && DashDir.Y > 0 && Speed.Y > 0)
                {
                    DashDir = new PointF(Sign(DashDir.X), 0);
                    Speed.Y = 0;
                    Speed.X *= 1.2f;
                    Ducking = true;
                }
                Speed.Y = 0;
                return false;
            }
            else
            {
                // 撞天花板：向上角修正（上冲时扩展为 5px）
                int upCorner = (State == StDash && DashDir.Y < 0f) ? DashingUpwardCornerCorrection : UpwardCornerCorrection;
                for (int d = -1; d <= 1; d += 2)
                    for (int i = 1; i <= upCorner; i++)
                        if (!CollideAt(Pos.X + i * d, Pos.Y - 1))
                        { Pos.X += i * d; return true; }
                Speed.Y = 0;
                // 原作：撞天花板取消可变跳高（防止撞头后仍保持低重力弧线）
                if (varJumpTimer < 0.15f) varJumpTimer = 0;
                return false;
            }
        }

        // ===== 输入缓冲 =====
        public void BufferJump() => jumpBufferTimer = 0.08f; // 原作 VirtualButton 缓冲 0.08s
        public void BufferDash() => dashBufferTimer = 0.08f;
        public bool HasJumpBuffer => jumpBufferTimer > 0;
        public bool HasDashBuffer => dashBufferTimer > 0;
        void ConsumeJump() => jumpBufferTimer = 0;
        void ConsumeDash() => dashBufferTimer = 0;

        /// <summary>重置到指定位置：清空速度/状态/冲刺/体力/计时器，头发复位。</summary>
        public void ResetTo(PointF pos)
        {
            Pos = pos;
            Speed = new PointF(0, 0);
            State = StNormal;
            Dashes = 1;
            Stamina = ClimbMaxStamina;
            Ducking = false;
            onGround = false;
            GroundId = IntPtr.Zero;
            BeingDragged = false;
            jumpBufferTimer = 0;
            dashBufferTimer = 0;
            dashCooldownTimer = 0;
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
            freezeTimer = 0;
            hairFlashTimer = 0;
            wallSpeedRetained = 0;
            wallSpeedRetentionTimer = 0;
            counter.X = counter.Y = 0;
            SweatId = "idle";
            SweatRestart = false;
            tiredFlashTimer = 0;
            TiredFlash = false;
            Hair.Reset(new PointF(Pos.X, Pos.Y - 9), Facing);
        }

        bool CanDash => dashBufferTimer > 0 && dashCooldownTimer <= 0 && Dashes > 0 && !BeingDragged;
        // 原作：wallBoost 期间体力 +27.5 视为不那么累（IsTired => CheckStamina < 20f）
        float CheckStamina => wallBoostTimer > 0f ? Stamina + ClimbJumpCost : Stamina;
        public bool IsTired => CheckStamina < 20f;

        public void RefillDash()
        {
            Dashes = 1;
            hairFlashTimer = 0.12f;  // 原作：闪白 0.12s 后直接切回红色
            HairColor = FlashHairColor;
        }

        // ===== 主更新 =====
        public void Update(float dt, PetInput input)
        {
            // 计时器
            if (jumpBufferTimer > 0) jumpBufferTimer -= dt;
            if (dashBufferTimer > 0) dashBufferTimer -= dt;
            if (dashAttackTimer > 0) dashAttackTimer -= dt;
            if (hairFlashTimer > 0) hairFlashTimer -= dt;  // 头发闪白计时
            // wallSlideTimer 只在正在滑墙时递减（原作行为）——挪到 NormalUpdate 滑墙段
            if (forceMoveXTimer > 0) forceMoveXTimer -= dt;
            if (varJumpTimer > 0) varJumpTimer -= dt;

            // 疲劳红闪翻转（原作 Scene.OnInterval(0.05f) → flash = !flash）
            tiredFlashTimer -= dt;
            if (tiredFlashTimer <= 0) { tiredFlashTimer = 0.05f; TiredFlash = !TiredFlash; }

            if (freezeTimer > 0) { freezeTimer -= dt; return; }

            bool wasOnGround = onGround;
            impactSpeed = 0;
            onGround = !BeingDragged && CheckGround();

            // 冲刺恢复冷却（原作：Freeze 帧全游戏暂停 → 冷却不递减，有效恢复 0.05s+0.2s；归零当帧走 else，补冲推迟一帧）
            if (dashCooldownTimer > 0) dashCooldownTimer -= dt;
            if (dashRefillCooldownTimer > 0) dashRefillCooldownTimer -= dt;
            else if (onGround && Dashes < 1) RefillDash();

            if (onGround)
            {
                Stamina = ClimbMaxStamina;
                wallSlideTimer = WallSlideTime;  // 原作：着地即重置滑墙时间
                jumpGraceTimer = JumpGraceTime;
                if (!wasOnGround)
                {
                    // 落地压缩（原作连续公式：Lerp(1→1.6, Lerp(1→0.4), Speed.Y/240)）
                    float amount = Math.Min(impactSpeed / 240f, 1f);
                    SpriteScaleX = 1f + 0.6f * amount;  // Lerp(1, 1.6, amount)
                    SpriteScaleY = 1f - 0.6f * amount;  // Lerp(1, 0.4, amount)
                }
            }
            else if (jumpGraceTimer > 0) jumpGraceTimer -= dt;

            if (!BeingDragged)
            {
                bool wasClimb = State == StClimb;
                switch (State)
                {
                    case StNormal: NormalUpdate(dt, input); break;
                    case StClimb: ClimbUpdate(dt, input); break;
                    case StDash: DashUpdate(dt, input); break;
                }

                // 原作 ClimbEnd：离开攀爬 → 汗水回 idle（刚攀爬跳出的 "jump" 保留）
                if (wasClimb && State != StClimb && SweatId != "jump") SweatId = "idle";

                // 保留速度返还（cornerboost）：撞墙后时限内，若前进方向不再被墙阻挡，恢复撞墙时的水平速度
                if (wallSpeedRetentionTimer > 0f)
                {
                    int rs = Math.Sign(wallSpeedRetained);
                    if (Math.Sign(Speed.X) == -rs)
                        wallSpeedRetentionTimer = 0f;                 // 反向移动取消保留
                    else if (!CollideAt(Pos.X + rs, Pos.Y))
                    {
                        Speed.X = wallSpeedRetained;                  // 墙不再阻挡 → 返还
                        wallSpeedRetentionTimer = 0f;
                    }
                    else
                        wallSpeedRetentionTimer -= dt;                // 仍被挡 → 倒计时
                }

                MoveH(Speed.X * dt);
                MoveV(Speed.Y * dt);

                // 屏幕左右边界
                if (Pos.X < MinX + 4) { Pos.X = MinX + 4; if (Speed.X < 0) Speed.X = 0; }
                if (Pos.X > MaxX - 4) { Pos.X = MaxX - 4; if (Speed.X > 0) Speed.X = 0; }
            }
            else
            {
                Speed = new PointF(0, 0);
                wallSlideDir = 0;
                SweatId = "idle";   // 拖拽悬空不出汗
            }

            // 表情恢复（原作 1.75/s）
            SpriteScaleX = Approach(SpriteScaleX, 1f, 1.75f * dt);
            SpriteScaleY = Approach(SpriteScaleY, 1f, 1.75f * dt);

            // 头发颜色：原作 0.12s 闪白后 → 瞬间切红；Dash=0 时 → 6/s 渐变到蓝
            if (hairFlashTimer > 0)
            {
                HairColor = FlashHairColor;  // 闪白期间保持白色
            }
            else if (Dashes > 0)
            {
                HairColor = NormalHairColor;  // 闪白结束 → 直接切红（不作渐变）
            }
            else
            {
                // 没 Dash 时渐变到蓝色（6/s）
                Color target = UsedHairColor;
                float k = Math.Min(1f, 6f * dt);
                HairColor = Color.FromArgb(
                    (int)(HairColor.R + (target.R - HairColor.R) * k),
                    (int)(HairColor.G + (target.G - HairColor.G) * k),
                    (int)(HairColor.B + (target.B - HairColor.B) * k));
            }

            UpdateSprite(dt, input);

            // 头发模拟：发根锚点遵循原版 PlayerHair.AfterUpdate 公式
            //   Nodes[0] = RenderPosition + (0, -9 * Scale.Y) + HairOffset × (Facing, 1)
            // 基准恒为脚底上方 -9×ScaleY；每帧 HairOffset 含姿态差异（HairMeta，可被 hair_tweaks.txt 覆盖）
            float anchorY = -9f * SpriteScaleY;
            float hx = 0f, hy = 0f;
            if (HairMeta.TryGet(CurrentFrameId, out var hm))
            {
                hx = hm.Offset.X; hy = hm.Offset.Y;
            }
            Hair.AfterUpdate(dt, new PointF(Pos.X + hx * Facing, Pos.Y + anchorY + hy), Facing);
        }

        /// <summary>头发编辑器专用：物理/动画冻结，只按给定 hx/hy 跑头发模拟（实时预览）。</summary>
        public void UpdateHairOnly(float dt, float hx, float hy)
        {
            float anchorY = -9f * SpriteScaleY;
            Hair.AfterUpdate(dt, new PointF(Pos.X + hx * Facing, Pos.Y + anchorY + hy), Facing);
        }

        // ===== 普通状态 =====
        void NormalUpdate(float dt, PetInput input)
        {
            // 抓墙进入攀爬
            if (input.GrabHeld && !IsTired && Stamina > 0 && !Ducking && Speed.Y >= 0 && Sign(Speed.X) != -Facing)
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
                            Pos.Y -= i;
                            EnterClimb();
                            State = StClimb;
                            return;
                        }
                    }
                }
            }
            if (CanDash) { State = StartDash(input); return; }

            // 蹲下 / 起身
            if (Ducking)
            {
                if (onGround && input.MoveY != 1 && CanUnDuck)
                {
                    Ducking = false;
                    SpriteScaleX = 0.8f; SpriteScaleY = 1.2f;
                }
                // 蹲姿起跳/落下：上升保持蹲姿（缩小碰撞盒钻缝），开始下落且头顶有空位时解除（原作 UpdateSprites）
                else if (!onGround && Speed.Y > 0f && CanUnDuck && jumpGraceTimer <= 0f)
                {
                    Ducking = false;
                }
            }
            else if (onGround && input.MoveY == 1 && Speed.Y >= 0)
            {
                Ducking = true;
                SpriteScaleX = 1.4f; SpriteScaleY = 0.6f;
            }

            // 水平移动
            int mx = forceMoveXTimer > 0 ? forceMoveX : input.MoveX;
            if (forceMoveXTimer > 0 && forceMoveX != 0) Facing = forceMoveX;  // 原作：强制移动期间朝向跟随移动方向
            if (Ducking && onGround)
            {
                Speed.X = Approach(Speed.X, 0, DuckFriction * dt);
            }
            else
            {
                float mult = onGround ? 1f : AirMult;
                if (Math.Abs(Speed.X) > MaxRun && Sign(Speed.X) == mx)
                    Speed.X = Approach(Speed.X, MaxRun * mx, RunReduce * mult * dt);
                else
                    Speed.X = Approach(Speed.X, MaxRun * mx, RunAccel * mult * dt);
            }
            if (State == StNormal && mx != 0 && wallSlideDir == 0)
            {
                // 蹲下时也能左右转向（原作同款），转向瞬间带挤压动画
                if (Facing != mx && Ducking) { SpriteScaleX = 0.8f; SpriteScaleY = 1.2f; }
                Facing = mx;
            }

            // 最大下落速度
            maxFall = (input.MoveY == 1 && Speed.Y >= MaxFall)
                ? Approach(maxFall, FastMaxFall, FastMaxAccel * dt)
                : Approach(maxFall, MaxFall, FastMaxAccel * dt);
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
                if ((mx == Facing || (mx == 0 && input.GrabHeld)) && input.MoveY != 1 &&
                    Speed.Y >= 0 && wallSlideTimer > 0 && CollideAt(Pos.X + Facing, Pos.Y))
                {
                    wallSlideDir = Facing;
                    // 滑墙中按抓取 → 自动进入攀爬（原作 ClimbTrigger）
                    if (input.GrabHeld && !IsTired && Stamina > 0) { EnterClimb(); State = StClimb; return; }
                    target = 160f + (20f - 160f) * (wallSlideTimer / WallSlideTime);
                }
                if (wallSlideDir != 0) wallSlideTimer -= dt;  // 只在滑墙时递减（原作行为）
                float gravMult = (Math.Abs(Speed.Y) < HalfGravThreshold && (input.JumpHeld || autoJump)) ? 0.5f : 1f;
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
                if (jumpGraceTimer > 0) Jump(input);
                else if (CanUnDuck && WallJumpCheck(1))
                {
                    if (Facing == 1 && input.GrabHeld && Stamina > 0) ClimbJump(input);
                    else if (DashAttacking && SuperWallJumpAngleCheck) SuperWallJump(-1);  // 上冲撞墙
                    else WallJump(-1, input);
                }
                else if (CanUnDuck && WallJumpCheck(-1))
                {
                    if (Facing == -1 && input.GrabHeld && Stamina > 0) ClimbJump(input);
                    else if (DashAttacking && SuperWallJumpAngleCheck) SuperWallJump(1);  // 上冲撞墙
                    else WallJump(1, input);
                }
            }

            // 攀爬跳助推（原作 wallBoost）：无方向攀爬跳后 0.2s 内按住离墙方向 → 130 加速 + 体力返还
            if (wallBoostTimer > 0f)
            {
                wallBoostTimer -= dt;
                if (input.MoveX == wallBoostDir)
                {
                    Speed.X = WallJumpHSpeed * input.MoveX;  // 130 * 离墙方向
                    Stamina += ClimbJumpCost;                // 返还 27.5
                    wallBoostTimer = 0f;
                    SweatId = "idle";                        // 原作 wallBoost 成功 → 汗水回 idle
                }
            }
        }

        float maxFall = MaxFall;

        void Jump(PetInput input)
        {
            ConsumeJump();
            autoJump = false;
            jumpGraceTimer = 0;
            varJumpTimer = VarJumpTime;
            wallSlideTimer = WallSlideTime;
            dashAttackTimer = 0f;  // 原作：跳跃清除冲刺攻击窗口
            Speed.X += JumpHBoost * input.MoveX;
            Speed.Y = JumpSpeed;
            varJumpSpeed = Speed.Y;
            SpriteScaleX = 0.6f; SpriteScaleY = 1.4f;
            // 原作：起跳不清蹲姿（蹲姿跳跃：上升保持小碰撞盒，下落时才解除，见 NormalUpdate）
        }

        void SuperJump(PetInput input = default)
        {
            ConsumeJump();
            autoJump = false;
            jumpGraceTimer = 0;
            varJumpTimer = VarJumpTime;
            wallSlideTimer = WallSlideTime;
            dashAttackTimer = 0f;  // 原作：super 跳清除冲刺攻击窗口
            // 反向技巧：冲刺中按住反方向 + 跳 → super 朝反方向飞出去
            int dir = Facing;
            if (input.MoveX != 0)
                dir = input.MoveX;
            Speed.X = SuperJumpH * dir;
            Speed.Y = JumpSpeed;
            if (Ducking)
            {
                Ducking = false;
                Speed.X *= DuckSuperJumpXMult;
                Speed.Y *= DuckSuperJumpYMult;
            }
            varJumpSpeed = Speed.Y;
            Facing = dir;
            SpriteScaleX = 0.6f; SpriteScaleY = 1.4f;
        }

        void WallJump(int dir, PetInput input)
        {
            ConsumeJump();
            autoJump = false;
            Ducking = false;
            jumpGraceTimer = 0;
            varJumpTimer = VarJumpTime;
            wallSlideTimer = WallSlideTime;
            dashAttackTimer = 0f;  // 原作：蹬墙跳清除冲刺攻击窗口
            Speed.X = WallJumpHSpeed * dir;
            Speed.Y = JumpSpeed;
            varJumpSpeed = Speed.Y;
            // 原作：只有按住方向键时才强制移动（无输入时蹬墙跳不强制方向偏移）
            if (input.MoveX != 0) { forceMoveX = dir; forceMoveXTimer = WallJumpForceTime; }
            Facing = dir;
            SpriteScaleX = 0.6f; SpriteScaleY = 1.4f;
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
            wallSlideTimer = WallSlideTime;
            Speed.X = SuperWallJumpH * dir;
            Speed.Y = SuperWallJumpSpeed;
            varJumpSpeed = Speed.Y;
            // 原作 SuperWallJump 不设 forceMove（可立即转向）
            Facing = dir;
            SpriteScaleX = 0.6f; SpriteScaleY = 1.4f;
        }

        void ClimbJump(PetInput input)
        {
            if (!onGround)
            {
                Stamina -= ClimbJumpCost;
                // 原作：空中攀爬跳 → 汗水 jump 喷雾（重播）
                SweatId = "jump"; SweatRestart = true;
            }
            Jump(input);
            // 原作 wallBoost：无方向攀爬跳不立刻推离；0.2s 内按住离墙方向 → 130 加速 + 返还体力
            if (input.MoveX == 0)
            {
                wallBoostDir = -Facing;
                wallBoostTimer = 0.2f;
            }
        }

        void EnterClimb()
        {
            autoJump = false;
            Speed.X = 0;
            Speed.Y *= 0.2f;
            wallSlideTimer = WallSlideTime;
            climbNoMoveTimer = 0.1f;
            lastClimbMove = 0;
            for (int i = 0; i < 2; i++)
            {
                if (CollideAt(Pos.X + Facing, Pos.Y)) break;
                Pos.X += Facing;
            }
        }

        // ===== 攀爬状态 =====
        void ClimbUpdate(float dt, PetInput input)
        {
            climbNoMoveTimer -= dt;
            if (onGround) Stamina = ClimbMaxStamina;

            if (input.JumpPressed)
            {
                if (input.MoveX == -Facing) WallJump(-Facing, input);
                else ClimbJump(input);
                State = StNormal;
                return;
            }
            if (CanDash) { State = StartDash(input); return; }
            if (!input.GrabHeld) { State = StNormal; return; }
            if (!CollideAt(Pos.X + Facing, Pos.Y))
            {
                if (Speed.Y < 0) ClimbHop();
                State = StNormal;
                return;
            }

            float ty = 0;
            if (climbNoMoveTimer <= 0)
            {
                if (input.MoveY == -1)
                {
                    ty = ClimbUpSpeed;
                    if (CollideAt(Pos.X, Pos.Y - 1)) { ty = 0; }
                }
                else if (input.MoveY == 1)
                {
                    ty = ClimbDownSpeed;
                    if (onGround) ty = 0;
                }
            }
            // 原作：攀爬速度用 Approach（加速度 ClimbAccel=900），不是瞬间切换
            Speed.Y = Approach(Speed.Y, ty, ClimbAccel * dt);
            lastClimbMove = Sign(ty);

            // 原作：体力消耗 + 汗水动画（仅抓墙动作期 climbNoMoveTimer<=0 才消耗）
            if (climbNoMoveTimer <= 0)
            {
                if (lastClimbMove < 0)
                {
                    Stamina -= 45.4545f * dt;
                    SweatId = Stamina <= 20f ? "danger" : "climb";
                }
                else
                {
                    if (lastClimbMove == 0) Stamina -= ClimbStillCost * dt;  // 原作：静止爬墙每秒消耗 10
                    SweatId = Stamina <= 20f ? "danger" : (!onGround ? "still" : "idle");
                }
            }
            else
            {
                SweatId = Stamina <= 20f ? "danger" : "idle";
            }
            if (Stamina <= 0)
            {
                Stamina = 0;
                State = StNormal;
            }

            // 攀爬动画由位移驱动（每爬 2px 进一帧）
            climbAnimAccum += Math.Abs(Speed.Y * dt);
            while (climbAnimAccum >= 2)
            {
                climbAnimAccum -= 2;
                ClimbFrame = (ClimbFrame + 1) % ClimbFrameCount;
            }
        }

        void ClimbHop()
        {
            // 原作：爬过墙顶弹起（-120 垂直 + 朝崖边方向 100 水平推力）
            Speed.Y = Math.Min(Speed.Y, ClimbHopY);
            Speed.X = ClimbHopX * Facing;  // 朝 Facing（崖边）方向推出
            forceMoveX = 0;                // 原作 forceMoveX = 0
            forceMoveXTimer = 0.2f;        // 0.2s 内不受方向键影响
        }

        void MoveVExactLocal(int n) => MoveVExact(n);

        // ===== 冲刺状态 =====
        int StartDash(PetInput input)
        {
            ConsumeDash();
            autoJump = false;
            Dashes--;
            dashStartedOnGround = onGround;
            dashCooldownTimer = DashCooldown;
            dashRefillCooldownTimer = DashRefillCooldown;
            dashAttackTimer = DashAttackTime;
            wallSlideTimer = WallSlideTime;
            if (FreezeFrameEnabled) freezeTimer = 0.05f; // 原作 Freeze(0.05)；菜单「冲刺冻结帧」可关掉起手停顿
            beforeDashSpeed = Speed;

            // 8 向瞄准，无输入默认朝前
            float ax = input.MoveX, ay = input.MoveY;
            PointF dir;
            if (ax == 0 && ay == 0) dir = new PointF(Facing, 0);
            else
            {
                float len = (float)Math.Sqrt(ax * ax + ay * ay);
                dir = new PointF(ax / len, ay / len);
            }

            if (!onGround && Ducking && CanUnDuck) Ducking = false;
            else if (!Ducking && onGround && input.MoveY == 1) Ducking = true;

            PointF speed = new PointF(dir.X * DashSpeed, dir.Y * DashSpeed);
            if (Sign(beforeDashSpeed.X) == Sign(speed.X) && Math.Abs(beforeDashSpeed.X) > Math.Abs(speed.X))
                speed.X = beforeDashSpeed.X;

            DashDir = dir;
            Speed = speed;

            // 地面斜下冲 → 蹲冲（1.2x，原作同款）
            if (onGround && DashDir.X != 0 && DashDir.Y > 0 && Speed.Y > 0)
            {
                DashDir = new PointF(Sign(DashDir.X), 0);
                Speed = new PointF(Speed.X * 1.2f, 0);
                Ducking = true;
            }
            if (DashDir.X != 0) Facing = Sign(DashDir.X);
            dashTime = DashTime;
            return StDash;
        }

        void DashUpdate(float dt, PetInput input)
        {
            dashTime -= dt;

            // 跳跃打断冲刺 → Super / Hyper / Ultra / 蹬墙跳（原作 DashUpdate 中 jump 优先于一切）
            if (input.JumpPressed)
            {
                // 地面/水平冲刺中跳 → 超级跳：super=260；蹲冲=hyper=325；落地瞬间=ultra
                if (Math.Abs(DashDir.Y) < 0.1f && jumpGraceTimer > 0 && CanUnDuck)
                {
                    SuperJump(input);
                    State = StNormal;
                    return;
                }
                // 上冲撞墙 → SuperWallJump（170h, -160v），否则普通蹬墙跳
                if (SuperWallJumpAngleCheck)
                {
                    if (CanUnDuck && WallJumpCheck(1)) { SuperWallJump(-1); State = StNormal; return; }
                    if (CanUnDuck && WallJumpCheck(-1)) { SuperWallJump(1); State = StNormal; return; }
                }
                else
                {
                    if (CanUnDuck && WallJumpCheck(1)) { WallJump(-1, input); State = StNormal; return; }
                    if (CanUnDuck && WallJumpCheck(-1)) { WallJump(1, input); State = StNormal; return; }
                }
            }
            // 冲刺中抓墙 → 攀爬（jump 之后检查，避免抢走超跳）
            if (input.GrabHeld && DashDir.X != 0 && !IsTired && Stamina > 0 &&
                CollideAt(Pos.X + Sign(DashDir.X), Pos.Y))
            {
                State = StClimb;
                Speed = new PointF(0, 0);
                EnterClimb();
                return;
            }
            if (dashTime <= 0)
            {
                if (DashDir.Y <= 0)
                    Speed = new PointF(DashDir.X * EndDashSpeed, DashDir.Y * EndDashSpeed);
                if (Speed.Y < 0) Speed.Y *= EndDashUpMult;
                autoJump = true; // 原作 DashCoroutine 结尾 AutoJump=true：维持半重力/可变跳高
                State = StNormal;
            }
        }

        // ===== 动画选择（移植 orig_UpdateSprite）=====
        void UpdateSprite(float dt, PetInput input)
        {
            string id;
            if (BeingDragged)
            {
                id = "dangling";
            }
            else if (State == StDash || dashAttackTimer > 0)
            {
                id = Ducking ? "duck" : "dash";
            }
            else if (State == StClimb)
            {
                if (lastClimbMove < 0) id = "climb";
                else if (lastClimbMove > 0) id = "wallslide";
                else if (input.MoveX == -Facing && Speed.Y >= 0) id = "climbTurn";
                else if (!CollideAt(Pos.X + Facing, Pos.Y + 6)) id = "dangling";
                else id = "wallslide";
                // 原作无独立 tired 动画：疲劳用 Sprite.Color=Red 红闪（见 DrawBody），攀爬动画不变
            }
            else if (Ducking && State == StNormal)
            {
                id = "duck";
            }
            else if (onGround)
            {
                fastJump = false;
                if (Math.Abs(Speed.X) <= 25 && input.MoveX == 0)
                {
                    bool noGroundAhead1 = !CollideAt(Pos.X + Facing, Pos.Y + 2);
                    bool noGroundAhead4 = !CollideAt(Pos.X + Facing * 4, Pos.Y + 2);
                    if (noGroundAhead1 && noGroundAhead4) id = "edge";
                    else if (input.MoveY == -1) id = "lookUp";
                    else id = "idle";
                }
                else if (Sign(Speed.X) == -input.MoveX && input.MoveX != 0 && Math.Abs(Speed.X) > 30)
                {
                    id = "flip";
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
                if (fastJump || Math.Abs(Speed.X) > 90) { fastJump = true; id = "jumpFast"; }
                else id = "jumpSlow";
            }
            else
            {
                if (fastJump || Speed.Y >= MaxFall) { fastJump = true; id = "fallFast"; }
                else id = "fallSlow";
            }

            // flip 播完前不覆盖
            if (AnimId == "flip" && !AnimFinished && id != "flip") { UpdateIdleFidget(dt, input, false); return; }
            // 待机小动作播完前不覆盖
            if (fidgetId != null)
            {
                if (AnimFinished) { fidgetId = null; idleTimer = 0; }
                else return;
            }
            if (id == "idle" && AnimId == "idle" && UpdateIdleFidget(dt, input, true)) return;
            if (id != "idle") idleTimer = 0;
            AnimId = id;
        }

        public bool AnimFinished;

        bool UpdateIdleFidget(float dt, PetInput input, bool allow)
        {
            idleTimer += dt;
            if (allow && idleTimer > 4f && AnimLoopCount > 0 && rng.NextDouble() < 0.4)
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
        public const int Count = 4;
        const float HangDown = 2f;
        const float BackLean = 0.5f;
        const float ApproachSpeed = 64f;
        const float MaxSegment = 3f;
        const float WaveSpeed = 4f;
        public readonly PointF[] Nodes = new PointF[Count];
        float wave;
        bool started;

        static PointF Approach(PointF val, PointF target, float maxMove)
        {
            float dx = target.X - val.X, dy = target.Y - val.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist <= maxMove || dist == 0) return target;
            return new PointF(val.X + dx / dist * maxMove, val.Y + dy / dist * maxMove);
        }

        public void AfterUpdate(float dt, PointF anchor, int facing)
        {
            wave += dt * WaveSpeed;
            if (!started)
            {
                for (int i = 0; i < Count; i++) Nodes[i] = new PointF(anchor.X - facing * 3, anchor.Y + 2);
                started = true;
            }
            Nodes[0] = anchor;
            var target = new PointF(Nodes[0].X - facing * BackLean * 2f, Nodes[0].Y + HangDown);
            var prev = Nodes[0];
            for (int i = 1; i < Count; i++)
            {
                float approach = (1f - (float)i / Count * 0.5f) * ApproachSpeed;
                Nodes[i] = Approach(Nodes[i], target, approach * dt);
                float dx = Nodes[i].X - prev.X, dy = Nodes[i].Y - prev.Y;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dist > MaxSegment)
                    Nodes[i] = new PointF(prev.X + dx / dist * MaxSegment, prev.Y + dy / dist * MaxSegment);
                target = new PointF(Nodes[i].X - facing * BackLean, Nodes[i].Y + HangDown);
                prev = Nodes[i];
            }
        }

        public void Reset(PointF anchor, int facing)
        {
            started = false;
            AfterUpdate(0, anchor, facing);
        }
    }
}
