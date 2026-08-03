using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// 桌宠主窗口：分层透明窗口 + 60FPS 游戏循环 + 窗口平台轮询 + 托盘菜单。
    /// 进程 Per-Monitor V2 DPI aware：所有坐标均为物理像素（Screen/Cursor/GetWindowRect/ULW 统一）；
    /// 物理模拟在游戏像素空间进行（1 游戏像素 = S 物理像素）。
    /// </summary>
    public class PetWindow : Form
    {
        // ===== 可调参数 =====
        public int GameScale = 6;               // 整数倍放大（原作 1080p 为 6x）
        public bool InputEnabled = true;
        public bool AlwaysOnTop = true;
        public static PetWindow Instance;

        const int CanvasW = 32, CanvasH = 48;   // 画布（游戏像素）
        const float AnchorX = 16, AnchorY = 44; // 脚底锚点（画布内）
        const double FixedDt = 1.0 / 60.0;
        static readonly IntPtr FloorId = new IntPtr(-991);
        const int WindowBorderPx = 8;           // 窗口空心边框厚度（物理像素）

        readonly Player player = new Player();
        readonly Animator animator;
        readonly Dictionary<string, Anim> anims;
        readonly Animator sweatAnimator;
        readonly Dictionary<string, Anim> sweatAnims;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu;

        Thread loopThread;
        volatile bool running;
        int pendingScale = -1;
        string pendingSkin;          // 待应用皮肤名（null=无；""=默认）。菜单线程设置，游戏循环线程应用，避免渲染竞争
        string activeSkin = "";      // 当前皮肤名
        bool introWakeUp = true;   // 启动时先播"醒来"动画（wakeUp 00-14），播完切 idle

        // 头发调试器（托盘菜单开关 → 启用后按 F1 进入编辑）
        public bool HairDebug;
        public bool HairEdit;
        List<(string Anim, int Idx, string Frame)> editFrames;
        int editIdx;
        string editFrameId;
        float editHx, editHy;
        int editBangs;
        float editStepTimer;
        bool editPrevEsc, editPrevBangs, editPrevSave, editPrevF1;
        readonly string hairTweaksPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hair_tweaks.txt");
        readonly string keysPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keys.txt");

        // 渲染
        Bitmap canvas;          // 物理像素画布（CanvasW*s × CanvasH*s）
        Bitmap small;           // 1x 游戏像素缓冲（CanvasW × CanvasH），整数坐标绘制后整数倍放大
        LayeredPresenter presenter;
        int renderFrameCount;

        // 平台
        readonly Dictionary<IntPtr, Win32.RECT> lastRects = new Dictionary<IntPtr, Win32.RECT>();
        int pollCounter;

        // 输入状态
        bool prevJump, prevDash;
        int lastMoveX, lastMoveY;        // TakeNewer：同轴两键同按时保持后按方向
        bool turnedMoveX, turnedMoveY;

        // 拖拽
        volatile bool dragging;
        Point dragGrabOffset;      // 物理像素：抓取点相对脚底
        PointF cursorVel;          // 物理像素/秒
        Point lastCursor;
        IntPtr trayIconHandle;     // 托盘图标的 HICON（需显式 DestroyIcon）

        // 粒子 / 特效
        readonly ParticleSystem particles = new ParticleSystem();
        PType dust, sparky;
        bool ParticlesEnabled = false;   // 粒子特效开关（默认关，托盘菜单可开）
        float slashTimer;          // 冲刺斩击剩余时长（>0 显示）
        int slashDir = 1;          // 斩击朝向
        float runDustTimer;        // 跑步扬尘间隔

        // 速度计 / 速度日志（托盘菜单开关）
        bool SpeedMeter;                   // 速度计 HUD 显示
        bool SpeedLog;                     // 速度日志写盘
        float speedLogTimer;               // 日志采样计时（每 0.05s 采样一次）
        float lastLogH, lastLogT;          // 上次写入的水平/总速度（变化才写，避免挂机刷屏）
        int lastLogState = -1;
        bool lastLogDuck;
        float peakHSpeed, peakTSpeed;      // 峰值：水平 / 总速度
        readonly string speedLogPath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "speed_log.txt");

        public PetWindow()
        {
            Instance = this;
            // 日志防无限增长：超过 5MB 时清空重写（保留最近一次运行记录）
            try
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet_debug.log");
                if (new System.IO.FileInfo(logPath).Length > 5 * 1024 * 1024)
                    System.IO.File.WriteAllText(logPath, "");
            }
            catch { }
            // ---- 窗口样式：无边框、不在任务栏、分层透明 ----
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // PMv2 下禁止 WinForms 按 DPI 自动缩放窗体（尺寸由 ULW 全权控制，防止跨 DPI 显示器时被改大小）
            AutoScaleMode = AutoScaleMode.None;
            // 注意：不要设置 BackColor/TransparencyKey（色键分层与 ULW 冲突）
            Size = new Size(CanvasW * GameScale, CanvasH * GameScale);
            Location = new Point(-10000, -10000);

            // ---- 贴图与动画 ----
            Sprites.LoadAll(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "player"));
            HairMeta.LoadOverrides(hairTweaksPath);
            KeyBinds.Load(keysPath);
            dust = new PType
            {
                Tex = new[] { "smoke0", "smoke1", "smoke2", "smoke3" },
                Color = Color.FromArgb(238, 238, 242),
                GravY = 4f,
                LifeMin = 0.3f, LifeMax = 0.5f,
                Size = 5f, SizeRange = 1f,
                SpeedMin = 6f, SpeedMax = 16f,
                ScaleOut = true
            };
            sparky = new PType
            {
                Tex = new[] { "zappysmoke00", "zappysmoke01", "zappysmoke02", "zappysmoke03" },
                Color = Color.FromArgb(150, 215, 255),
                GravY = 8f,
                LifeMin = 0.2f, LifeMax = 0.35f,
                Size = 5f, SizeRange = 1f,
                SpeedMin = 20f, SpeedMax = 45f,
                ScaleOut = true
            };
            anims = BuildAnims();
            animator = new Animator(anims);
            animator.Play("wakeUp");   // 启动先播醒来动画
            sweatAnims = BuildSweatAnims();
            sweatAnimator = new Animator(sweatAnims);
            sweatAnimator.Play("idle");

            // 皮肤：启动时恢复上次选择（游戏循环尚未启动，直接加载无竞争）。动画集按 mod Sprites.xml 切换。
            string savedSkin = Skins.LoadActive();
            if (savedSkin != "" && Skins.TryGetSkinSource(savedSkin, out var szip, out var sdir))
            {
                if (szip != null)
                {
                    Sprites.LoadSkinZip(szip, savedSkin);
                    var sa = Skins.BuildSkinAnims(szip, anims);
                    animator.SetAnims(sa.Count > 0 ? sa : anims);
                }
                else
                {
                    Sprites.LoadSkin(sdir);
                    animator.SetAnims(anims);
                }
                activeSkin = savedSkin;
                Skins.LoadOptions(savedSkin);
            }
            BuildEditFrames();

            // ---- 出生点：主屏工作区底部中央 ----
            var wa = Screen.PrimaryScreen.WorkingArea;
            player.Pos = new PointF((wa.Left + wa.Right) / 2f / GameScale, wa.Bottom / GameScale - 2);

            // ---- 托盘 ----
            trayMenu = BuildMenu();
            tray = new NotifyIcon
            {
                Text = "玛德琳",
                Icon = BuildTrayIcon(),
                ContextMenuStrip = trayMenu,
                Visible = true
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOPMOST = 0x00000008;
                var cp = base.CreateParams;
                cp.ExStyle |= Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW |
                              Win32.WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            presenter = new LayeredPresenter(Handle, CanvasW * GameScale, CanvasH * GameScale);
            PollSolids();
            player.Hair.Reset(new PointF(player.Pos.X, player.Pos.Y - 9), player.Facing);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 窗口真正显示后再启动游戏循环（ULW 要求窗口已就绪）
            running = true;
            loopThread = new Thread(GameLoop) { IsBackground = true, Name = "PetLoop" };
            loopThread.Start();
        }

        // ================= 动画定义 =================
        static Dictionary<string, Anim> BuildAnims()
        {
            var d = new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase);
            void Add(string id, string[] frames, float delay, bool loop, bool manual = false)
            { if (frames.Length > 0) d[id] = new Anim { Frames = frames, Delay = delay, Loop = loop, Manual = manual }; }

            Add("idle", Sprites.Seq("idle", 0, 8), 0.09f, true);
            Add("wakeUp", Sprites.Seq("wakeUp", 0, 14), 0.09f, false);   // 启动醒来动画（蜷→起身）
            Add("idleA", Sprites.Seq("idleA", 0, 30), 0.09f, false);
            Add("idleB", Sprites.Seq("idleB", 0, 30), 0.09f, false);
            Add("idleC", Sprites.Seq("idleC", 0, 30), 0.09f, false);
            Add("runSlow", Sprites.Seq("runSlow", 0, 11), 0.08f, true);
            Add("runFast", Sprites.Seq("runFast", 0, 11), 0.06f, true);
            Add("jumpSlow", Sprites.Seq("jumpSlow", 0, 3), 0.08f, false);
            Add("jumpFast", Sprites.Seq("jumpFast", 0, 3), 0.08f, false);
            Add("fallSlow", Sprites.Seq("fall", 0, 3), 0.10f, true);
            Add("fallFast", Sprites.Seq("fall", 4, 7), 0.08f, true);
            Add("dash", Sprites.Seq("dash", 0, 3), 0.05f, true);
            Add("climb", ClimbFrames(), 0.1f, true, manual: true);
            Add("climbTurn", new[] { "climb07", "climb08" }, 0.12f, false);
            Add("wallslide", new[] { "climb02" }, 1f, true);
            Add("dangling", Sprites.Seq("dangling", 0, 9), 0.1f, true);
            Add("duck", new[] { "duck" }, 1f, true);
            Add("lookUp", Sprites.Seq("lookUp", 0, 7), 0.1f, false);
            Add("edge", Sprites.Seq("edge", 0, 13), 0.08f, true);
            Add("flip", Sprites.Seq("flip", 0, 8), 0.06f, false);
            return d;
        }

        /// <summary>攀爬循环帧：只含 climb00-06；climb07/08 为扭头帧（走 climbTurn），climb09-14 为废案屏蔽。</summary>
        static string[] ClimbFrames()
        {
            var list = new List<string>();
            for (int i = 0; i <= 6; i++)
            {
                var id = "climb" + i.ToString("00");
                if (Sprites.Has(id)) list.Add(id);
            }
            return list.ToArray();
        }

        /// <summary>汗水动画（原作 player_sweat SpriteBank）：跳/爬/危险/静止/空闲。
        /// 素材已加 sweat_ 前缀放进 assets/player，避免与身体帧（idle00/climb00 等）重名冲突。</summary>
        static Dictionary<string, Anim> BuildSweatAnims()
        {
            var d = new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase);
            void Add(string id, string[] frames, float delay, bool loop)
            { if (frames.Length > 0) d[id] = new Anim { Frames = frames, Delay = delay, Loop = loop }; }

            Add("jump", Sprites.Seq("sweat_jump", 0, 3), 0.05f, false);   // 攀爬跳喷雾（一次性，播完停在末帧近乎透明）
            Add("climb", Sprites.Seq("sweat_climb", 0, 7), 0.07f, true);
            Add("danger", Sprites.Seq("sweat_danger", 0, 5), 0.08f, true);
            Add("still", Sprites.Seq("sweat_still", 0, 5), 0.08f, true);
            Add("idle", new[] { "sweat_idle00" }, 0.1f, true);            // 空白帧，不出汗
            return d;
        }

        // ================= 游戏循环 =================
        void GameLoop()
        {
            Log("loop start");
            TimePeriod.Begin(1);
            try
            {
                var sw = Stopwatch.StartNew();
                double last = sw.Elapsed.TotalSeconds;
                double acc = 0;

                while (running)
                {
                    double now = sw.Elapsed.TotalSeconds;
                    acc += now - last;
                    last = now;
                    if (acc > 0.1) acc = 0.1;

                    bool stepped = false;
                    while (acc >= FixedDt)
                    {
                        acc -= FixedDt;
                        Tick((float)FixedDt);
                        stepped = true;
                    }
                    if (stepped) Render();

                    double sleepMs = (FixedDt - acc) * 1000 - 1;
                    if (sleepMs > 1) Thread.Sleep((int)sleepMs);
                    else Thread.SpinWait(2000);
                }
            }
            catch (Exception ex)
            {
                Log("LOOP CRASH: " + ex);
            }
            finally
            {
                TimePeriod.End(1);   // 异常也恢复定时器精度
            }
        }

        static readonly object logLock = new object();
        public static void Log(string msg)
        {
            try
            {
                lock (logLock)
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet_debug.log"),
                        DateTime.Now.ToString("HH:mm:ss.fff ") + msg + "\n");
            }
            catch { }
        }

        void Tick(float dt)
        {
            // 应用待定的缩放变更
            if (pendingScale > 0)
            {
                GameScale = pendingScale;
                pendingScale = -1;
                canvas?.Dispose();
                canvas = null;
                presenter.Resize(CanvasW * GameScale, CanvasH * GameScale);
                pollCounter = 999; // 立即重取平台（单位变了）
            }

            // 应用待定的皮肤切换（游戏循环线程内加载，避免与渲染竞争）
            if (pendingSkin != null)
            {
                ApplySkin(pendingSkin);
                pendingSkin = null;
            }

            // 平台轮询（每 0.25s）
            if (++pollCounter >= 15)
            {
                pollCounter = 0;
                PollSolids();
            }

            // 头发调试器：冻结物理，逐帧预览头发
            if (HairEdit) { TickHairEdit(dt); return; }

            // 已启用调试时，F1 进入编辑
            bool f1 = Win32.KeyDown(0x70);
            if (f1 && !editPrevF1)
            {
                editPrevF1 = true;
                if (HairDebug) { EnterHairEdit(); return; }
            }
            else if (!f1) editPrevF1 = false;

            if (introWakeUp)
            {
                // 启动醒来动画：冻结物理，只播 wakeUp + 头发模拟；播完切 idle。
                float hx = 0f, hy = 0f;
                if (HairMeta.TryGet(animator.CurrentFrameId, out var wm)) { hx = wm.Offset.X; hy = wm.Offset.Y; }
                player.UpdateHairOnly(dt, hx, hy);
                animator.Update(dt);
                player.AnimFinished = animator.Finished;
                player.AnimLoopCount = animator.LoopCount;
                player.CurrentFrameId = animator.CurrentFrameId;
                if (animator.Finished)
                {
                    introWakeUp = false;
                    animator.Play("idle", true);
                    Log("wake up done -> idle");
                }
                return;
            }

            // 输入
            var input = SampleInput();

            // 物理
            bool wasOnGround = player.onGround;
            int wasState = player.State;
            player.Update(dt, input);

            // 离开屏幕很远自动重置（防"无限下落"/被甩出虚拟屏幕）
            if (!introWakeUp) CheckAutoReset();

            // 速度计/日志：更新峰值 + 按 0.05s 采样写日志（h=水平带方向，t=总速度）
            float tv = (float)Math.Sqrt(player.Speed.X * player.Speed.X + player.Speed.Y * player.Speed.Y);
            if (Math.Abs(player.Speed.X) > peakHSpeed) peakHSpeed = Math.Abs(player.Speed.X);
            if (tv > peakTSpeed) peakTSpeed = tv;
            if (SpeedLog)
            {
                speedLogTimer += dt;
                if (speedLogTimer >= 0.05f)
                {
                    speedLogTimer -= 0.05f;
                    // 只在速度/状态/下蹲任一变化时落盘，挂机不刷屏
                    bool stChg = player.State != lastLogState || player.Ducking != lastLogDuck;
                    if (stChg || player.Speed.X != lastLogH || Math.Abs(tv - lastLogT) > 0.5f)
                    {
                        lastLogH = player.Speed.X;
                        lastLogT = tv;
                        lastLogState = player.State;
                        lastLogDuck = player.Ducking;
                        WriteSpeedLog(player.Speed.X, tv);
                    }
                }
            }

            // 粒子（走路/落地/跳跃/冲刺）+ 冲刺斩击计时
            if (ParticlesEnabled)
            {
                EmitPlayerParticles(dt, wasOnGround, wasState);
                particles.Update(dt);
                if (slashTimer > 0) slashTimer -= dt;
            }
            else
            {
                particles.Clear();
                slashTimer = 0;
            }

            // 动画
            animator.Play(player.AnimId);
            if (player.AnimId == "climb") animator.Frame = player.ClimbFrame;
            animator.Update(dt);
            player.AnimFinished = animator.Finished;
            player.AnimLoopCount = animator.LoopCount;
            player.CurrentFrameId = animator.CurrentFrameId; // 下一帧起头发锚点跟随当前帧

            // 汗水动画（原作 sweatSprite）：跳跃喷雾需强制重播，其余随 SweatId 切换
            if (player.SweatRestart) { sweatAnimator.Play(player.SweatId, true); player.SweatRestart = false; }
            else sweatAnimator.Play(player.SweatId);
            sweatAnimator.Update(dt);
            // 攀爬跳喷雾一次性播完 → 自动回 idle（原作 jump 末帧仍残留小汗滴，放大后明显，避免卡帧不消失）
            if (player.SweatId == "jump" && sweatAnimator.Finished) player.SweatId = "idle";
        }

        // ================= 头发调试器 =================
        void EnterHairEdit()
        {
            if (editFrames == null || editFrames.Count == 0) return;
            string f = animator.CurrentFrameId;
            int idx = f == null ? -1 : editFrames.FindIndex(e => string.Equals(e.Frame, f, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;
            editIdx = idx;
            var e = editFrames[editIdx];
            animator.Play(e.Anim, true);
            animator.Frame = e.Idx;
            editFrameId = e.Frame;
            LoadEditValues();
            HairEdit = true;
            try
            {
                BeginInvoke(new Action(() => MessageBox.Show(
                    "头发调试器（当前帧 " + editFrameId + "）\n" +
                    "[ / ]  切换帧\n" +
                    "← / →  头发锚点左右（0.1）\n" +
                    "↑ / ↓  头发锚点上下（0.1）\n" +
                    "C      刘海朝向 0/1/2\n" +
                    "F5     保存当前帧到 hair_tweaks.txt\n" +
                    "F1 / Esc  退出（自动保存）",
                    "玛德琳", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
            catch { }
        }

        void ExitHairEdit()
        {
            if (!HairEdit) return;
            SaveEditFrame();
            HairEdit = false;
        }

        void BuildEditFrames()
        {
            editFrames = new List<(string Anim, int Idx, string Frame)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in anims)
                for (int i = 0; i < kv.Value.Frames.Length; i++)
                {
                    var f = kv.Value.Frames[i];
                    if (seen.Add(f)) editFrames.Add((kv.Key, i, f));
                }
        }

        void StepEditFrame(int dir)
        {
            if (editFrames == null || editFrames.Count == 0) return;
            editIdx = (editIdx + dir + editFrames.Count) % editFrames.Count;
            var e = editFrames[editIdx];
            animator.Play(e.Anim, true);
            animator.Frame = e.Idx;
            editFrameId = e.Frame;
            LoadEditValues();
            PetWindow.Log("hair edit frame " + editFrameId + " x=" + editHx.ToString("0.###") + " y=" + editHy.ToString("0.###") + " b=" + editBangs);
        }

        void LoadEditValues()
        {
            if (HairMeta.TryGet(editFrameId, out var m))
            { editHx = m.Offset.X; editHy = m.Offset.Y; editBangs = m.Bangs; }
            else { editHx = 0f; editHy = 0f; editBangs = 1; }
        }

        void SaveEditFrame()
        {
            if (string.IsNullOrEmpty(editFrameId)) return;
            HairMeta.SaveOverride(hairTweaksPath, editFrameId, editHx, editHy, editBangs);
            PetWindow.Log("hair saved " + editFrameId + " " + editHx.ToString("0.###") + " " + editHy.ToString("0.###") + " " + editBangs);
        }

        void TickHairEdit(float dt)
        {
            bool f1 = Win32.KeyDown(0x70);
            if (f1 && !editPrevF1) { editPrevF1 = true; ExitHairEdit(); return; }
            editPrevF1 = f1;

            if (Win32.KeyDown(0x1B))
            {
                if (!editPrevEsc) { editPrevEsc = true; ExitHairEdit(); }
                return;
            }
            editPrevEsc = Win32.KeyDown(0x1B);

            // 帧步进（按住每 0.15s 一步）
            editStepTimer -= dt;
            int dir = 0;
            if (Win32.KeyDown(0xDB)) dir -= 1;   // [
            if (Win32.KeyDown(0xDD)) dir += 1;   // ]
            if (dir != 0)
            {
                if (editStepTimer <= 0) { editStepTimer = 0.15f; StepEditFrame(dir); }
            }
            else editStepTimer = 0;

            // 锚点偏移（0.1/帧）
            const float step = 0.1f;
            if (Win32.KeyDown(0x25)) editHx -= step;   // ←
            if (Win32.KeyDown(0x27)) editHx += step;   // →
            if (Win32.KeyDown(0x26)) editHy -= step;   // ↑
            if (Win32.KeyDown(0x28)) editHy += step;   // ↓

            bool cDown = Win32.KeyDown(0x43);          // C 刘海朝向
            if (cDown && !editPrevBangs) { editPrevBangs = true; editBangs = (editBangs + 1) % 3; }
            if (!cDown) editPrevBangs = false;

            bool f5Down = Win32.KeyDown(0x74);         // F5 保存
            if (f5Down && !editPrevSave) { editPrevSave = true; SaveEditFrame(); }
            if (!f5Down) editPrevSave = false;

            // 实时写入覆盖表：头发锚点与刘海渲染都立即生效
            if (editFrameId != null)
            {
                HairMeta.SetOverride(editFrameId, editHx, editHy, editBangs);
                player.CurrentFrameId = editFrameId;
            }
            player.UpdateHairOnly(dt, editHx, editHy);
        }

        // 粒子发射（参数移植自 Celeste Player.cs 的 Dust.Burst 调用）
        void EmitPlayerParticles(float dt, bool wasOnGround, int wasState)
        {
            float up = (float)-Math.PI / 2f;

            // 落地尘
            if (player.onGround && !wasOnGround)
                particles.Emit(dust, player.Pos.X, player.Pos.Y, up, 0.6f, 4);

            // 跳跃 puff
            if (!player.onGround && wasOnGround && player.Speed.Y < 0)
                particles.Emit(dust, player.Pos.X, player.Pos.Y, up, 0.6f, 6);

            // 跑步扬尘
            if (player.onGround && Math.Abs(player.Speed.X) > 30)
            {
                runDustTimer += dt;
                if (runDustTimer > 0.12f)
                {
                    runDustTimer = 0;
                    particles.Emit(dust, player.Pos.X + player.Facing * 2, player.Pos.Y, up, 0.5f, 1);
                }
            }
            else runDustTimer = 0;

            // 冲刺
            if (player.State == Player.StDash)
            {
                float dashAngle = (float)Math.Atan2(-player.DashDir.Y, -player.DashDir.X);
                if (wasState != Player.StDash)
                {
                    // 冲刺开始：爆发 + 斩击特效
                    particles.Emit(sparky, player.Pos.X, player.Pos.Y, dashAngle, 0.6f, 8);
                    slashTimer = 0.15f;
                    slashDir = player.DashDir.X < 0 ? -1 : 1;
                }
                else
                {
                    // 冲刺中：持续拖尾
                    particles.Emit(sparky, player.Pos.X, player.Pos.Y, dashAngle, 0.8f, 1);
                }
            }
        }

        PetInput SampleInput()
        {
            var input = new PetInput();
            if (!InputEnabled || dragging || KeyBinds.DialogOpen) { prevJump = prevDash = false; return input; }

            bool left = KeyBinds.Pressed("Left");
            bool right = KeyBinds.Pressed("Right");
            bool up = KeyBinds.Pressed("Up");
            bool down = KeyBinds.Pressed("Down");
            bool jump = KeyBinds.Pressed("Jump");
            bool dash = KeyBinds.Pressed("Dash");
            bool grab = KeyBinds.Pressed("Grab");

            // 原作 OverlapBehaviors.TakeNewer：左/右同按 → 保持后按方向（先按住左再按右 → 向右走），不取消
            input.MoveX = NewerAxis(right, left, ref lastMoveX, ref turnedMoveX);
            input.MoveY = NewerAxis(down, up, ref lastMoveY, ref turnedMoveY);
            input.JumpHeld = jump;
            input.GrabHeld = grab;

            if (jump && !prevJump) player.BufferJump();
            if (dash && !prevDash) player.BufferDash();
            prevJump = jump;
            prevDash = dash;

            input.JumpPressed = player.HasJumpBuffer;
            input.DashPressed = player.HasDashBuffer;
            return input;
        }

        // 原作 VirtualIntegerAxis / VirtualJoystick 的 OverlapBehaviors.TakeNewer：
        // 正/负两键同按时，第一帧把方向翻转一次（保持"后按"的方向），之后不再翻转；单键时复位标记。
        static int NewerAxis(bool pos, bool neg, ref int last, ref bool turned)
        {
            if (pos && neg)
            {
                if (!turned) { last = -last; turned = true; }
                return last;
            }
            if (pos) { turned = false; return last = 1; }
            if (neg) { turned = false; return last = -1; }
            turned = false;
            return last = 0;
        }

        // ================= 平台（窗口即平台，空心边框）=================
        void PollSolids()
        {
            float s = GameScale;
            var cur = new Dictionary<IntPtr, Win32.RECT>();
            IntPtr self = Handle;

            // EnumWindows 按 Z 序从上到下枚举（前→后）；记下顺序用于「前窗遮挡后窗」
            var zorder = new List<KeyValuePair<IntPtr, Win32.RECT>>();

            Win32.EnumWindows((hwnd, _) =>
            {
                if (hwnd == self) return true;
                if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd)) return true;
                // 被 DWM 隐藏的窗口（UWP 后台、虚拟桌面外）
                if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
                int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                if ((ex & Win32.WS_EX_TOOLWINDOW) != 0) return true;
                // 透明点击穿透的窗口（其他桌宠/悬浮层）不做平台
                if ((ex & Win32.WS_EX_LAYERED) != 0 && (ex & Win32.WS_EX_TRANSPARENT) != 0) return true;
                string cls = Win32.GetClassNameString(hwnd);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Xaml_WindowedPopupClass") return true;
                if (!Win32.TryGetWindowRect(hwnd, out var r)) return true;
                if (r.Width < 24 || r.Height < 18) return true;
                cur[hwnd] = r;
                zorder.Add(new KeyValuePair<IntPtr, Win32.RECT>(hwnd, r));
                return true;
            }, IntPtr.Zero);

            // 组装平台（游戏单位）：窗口只保留「空心边框」；前面（Z 序在上）的窗口
            // 以整块矩形裁掉后窗边框被盖住的部分，被盖处不再阻挡。
            var solids = new List<Solid>(zorder.Count * 4 + 1);
            var occluders = new List<Win32.RECT>(zorder.Count);
            foreach (var kv in zorder)
            {
                var r = kv.Value;
                foreach (var edge in WindowEdges(r))
                {
                    var pieces = SubtractRects(edge, occluders);
                    foreach (var p in pieces)
                        solids.Add(new Solid { Id = kv.Key, L = p.Left / s, T = p.Top / s, R = p.Right / s, B = p.Bottom / s });
                }
                occluders.Add(r);   // 本窗口整体遮挡它后面的窗口
            }
            // 宠物所在屏幕的底部作为地板
            var screen = Screen.FromPoint(new Point((int)(player.Pos.X * s), (int)(player.Pos.Y * s)));
            var vb = screen.Bounds;
            solids.Add(new Solid { Id = FloorId, L = vb.Left / s - 400, T = vb.Bottom / s, R = vb.Right / s + 400, B = vb.Bottom / s + 400 });

            // 虚拟屏幕左右边界
            var vs = SystemInformation.VirtualScreen;
            player.MinX = vs.Left / s;
            player.MaxX = vs.Right / s;

            // 搭乘：所站窗口移动时跟随
            if (player.GroundId != IntPtr.Zero && player.GroundId != FloorId &&
                lastRects.TryGetValue(player.GroundId, out var oldR) &&
                cur.TryGetValue(player.GroundId, out var newR))
            {
                player.Pos = new PointF(
                    player.Pos.X + (newR.Left - oldR.Left) / s,
                    player.Pos.Y + (newR.Top - oldR.Top) / s);
            }

            lastRects.Clear();
            foreach (var kv in cur) lastRects[kv.Key] = kv.Value;
            player.Solids = solids;
        }

        /// <summary>窗口四条空心边框（物理像素坐标），厚度 WindowBorderPx。</summary>
        static IEnumerable<Win32.RECT> WindowEdges(Win32.RECT r)
        {
            int b = WindowBorderPx;
            yield return new Win32.RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Top + b };                 // 上
            yield return new Win32.RECT { Left = r.Left, Top = r.Bottom - b, Right = r.Right, Bottom = r.Bottom };          // 下
            yield return new Win32.RECT { Left = r.Left, Top = r.Top + b, Right = r.Left + b, Bottom = r.Bottom - b };      // 左
            yield return new Win32.RECT { Left = r.Right - b, Top = r.Top + b, Right = r.Right, Bottom = r.Bottom - b };    // 右
        }

        /// <summary>矩形 a 减去一组遮挡矩形，返回互不重叠的剩余子矩形。</summary>
        static List<Win32.RECT> SubtractRects(Win32.RECT a, List<Win32.RECT> occluders)
        {
            var cur = new List<Win32.RECT> { a };
            foreach (var o in occluders)
            {
                var next = new List<Win32.RECT>(cur.Count + 4);
                foreach (var rc in cur)
                {
                    if (rc.Right <= o.Left || rc.Left >= o.Right || rc.Bottom <= o.Top || rc.Top >= o.Bottom)
                    {
                        next.Add(rc);
                        continue;
                    }
                    if (rc.Top < o.Top) next.Add(new Win32.RECT { Left = rc.Left, Top = rc.Top, Right = rc.Right, Bottom = o.Top });
                    if (rc.Bottom > o.Bottom) next.Add(new Win32.RECT { Left = rc.Left, Top = o.Bottom, Right = rc.Right, Bottom = rc.Bottom });
                    int ot = Math.Max(rc.Top, o.Top), ob = Math.Min(rc.Bottom, o.Bottom);
                    if (ot < ob)
                    {
                        if (rc.Left < o.Left) next.Add(new Win32.RECT { Left = rc.Left, Top = ot, Right = o.Left, Bottom = ob });
                        if (rc.Right > o.Right) next.Add(new Win32.RECT { Left = o.Right, Top = ot, Right = rc.Right, Bottom = ob });
                    }
                }
                cur = next;
                if (cur.Count == 0) return cur;
            }
            return cur;
        }

        // ================= 渲染 =================
        void Render()
        {
            int s = GameScale;
            if (canvas == null)
            {
                canvas = new Bitmap(CanvasW * s, CanvasH * s, PixelFormat.Format32bppPArgb);
                Log("canvas created " + canvas.Width + "x" + canvas.Height);
            }
            if (small == null)
                small = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppPArgb);

            // 1x 游戏像素缓冲：整数坐标直接落像素，杜绝亚像素偏移；之后整数倍最近邻放大
            using (var g = Graphics.FromImage(small))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;
                g.CompositingQuality = CompositingQuality.HighSpeed;

                float camX = player.Pos.X - AnchorX;   // 世界→画布
                float camY = player.Pos.Y - AnchorY;

                // 头发画在身体后面（先画头发，再画身体覆盖）。
                // wakeUp 帧自带完整头发（蜷着睡觉），不再叠加模拟头发
                if (animator.CurrentId != "wakeUp")
                    DrawHair(g, camX, camY);
                DrawBody(g);
                DrawSweat(g);

                // 粒子 + 冲刺斩击（画在最上层，开关控制）
                if (ParticlesEnabled)
                {
                    particles.Draw(g, camX, camY);
                    DrawSlash(g, camX, camY);
                }
            }

            // 整数倍放大：NearestNeighbor + Half（实测产生干净 s×s 方块）
            using (var gc = Graphics.FromImage(canvas))
            {
                gc.Clear(Color.Transparent);
                gc.InterpolationMode = InterpolationMode.NearestNeighbor;
                gc.PixelOffsetMode = PixelOffsetMode.Half;
                gc.DrawImage(small, 0, 0, CanvasW * s, CanvasH * s);
                // 速度计（物理像素绘制在窗口顶部，开关控制）
                if (SpeedMeter) DrawSpeedMeter(gc);
            }

            int left = (int)Math.Round((player.Pos.X - AnchorX) * s);
            int top = (int)Math.Round((player.Pos.Y - AnchorY) * s);
            presenter.Present(canvas, left, top);
            // 每帧置顶到 topmost 链最前（应对其他 topmost 窗口的子窗口覆盖）
            Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            // 每 5 秒记录一次位置 + 速度 + 状态
            if ((++renderFrameCount % 300) == 0)
                PetWindow.Log("frame " + renderFrameCount + " pos=" + player.Pos.X.ToString("F1") + "," + player.Pos.Y.ToString("F1") +
                    " sp=" + player.Speed.X.ToString("F0") + "," + player.Speed.Y.ToString("F0") +
                    " st=" + player.State + " duck=" + (player.Ducking ? 1 : 0) + " anim=" + player.AnimId);
        }

        // 冲刺斩击特效（slash00-03，4 帧，随冲刺朝向翻转）
        void DrawSlash(Graphics g, float camX, float camY)
        {
            if (slashTimer <= 0) return;
            int frame = (int)((1f - slashTimer / 0.15f) * 4f);
            if (frame > 3) frame = 3;
            var tex = Sprites.Get("slash0" + frame, slashDir < 0);
            if (tex == null) return;
            int w = 24, h = 8;
            float cx = player.Pos.X + player.DashDir.X * 6 - camX;
            float cy = player.Pos.Y - 9 - camY;
            float alpha = Math.Min(1f, slashTimer / 0.15f * 2f);
            Sprites.DrawTinted(g, tex, Color.White,
                (int)Math.Round(cx - w / 2f), (int)Math.Round(cy - h / 2f), w, h, alpha);
        }

        // ================= 速度计 / 速度日志 =================
        void SetSpeedLog(bool on)
        {
            speedLogTimer = 0f;
            try
            {
                if (on)
                    System.IO.File.AppendAllText(speedLogPath,
                        "\n=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " 速度日志开始 ===\n");
                else
                    System.IO.File.AppendAllText(speedLogPath,
                        "=== 结束 峰值 h=" + peakHSpeed.ToString("F0") + " v=" + peakTSpeed.ToString("F0") + " ===\n");
            }
            catch { }
            Log(on ? "speed log start" : "speed log stop");
        }

        void WriteSpeedLog(float h, float tv)
        {
            try
            {
                System.IO.File.AppendAllText(speedLogPath, string.Format(
                    "{0:HH:mm:ss.fff} h={1,6:F1} t={2,6:F1} st={3} duck={4} {5}\n",
                    DateTime.Now, h, tv, player.State, player.Ducking ? 1 : 0, player.AnimId));
            }
            catch { }
        }

        // 速度计：画在窗口顶部小面板。H=水平速度（带方向）、V=总速度，第二行峰值。
        // 颜色按速度档位变化：灰<奔跑<冲刺<超跳/ultra（红），ultra 加速时一眼可见。
        void DrawSpeedMeter(Graphics gc)
        {
            float h = player.Speed.X;
            float tv = (float)Math.Sqrt(h * h + player.Speed.Y * player.Speed.Y);
            int fs = Math.Max(10, GameScale * 2);
            using var font = new Font("Consolas", fs, FontStyle.Bold, GraphicsUnit.Pixel);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            Size m1 = TextRenderer.MeasureText("H 000  V 000", font, new Size(9999, 9999), flags);
            Size m2 = TextRenderer.MeasureText("pk 000 / 000", font, new Size(9999, 9999), flags);
            int lineH = m1.Height;
            int w = Math.Max(m1.Width, m2.Width) + 12;
            int hh = lineH * 2 + 8;
            int x = 4, y = 4;
            using (var bg = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                gc.FillRectangle(bg, x, y, w, hh);
            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1f))
                gc.DrawRectangle(pen, x, y, w - 1, hh - 1);
            TextRenderer.DrawText(gc, string.Format("H {0:F0}  V {1:F0}", h, tv), font,
                new Point(x + 6, y + 3), SpeedColor(Math.Abs(h)), flags);
            TextRenderer.DrawText(gc, string.Format("pk {0:F0} / {1:F0}", peakHSpeed, peakTSpeed), font,
                new Point(x + 6, y + 3 + lineH), Color.FromArgb(175, 255, 255, 255), flags);
        }

        static Color SpeedColor(float v)
        {
            if (v > 325f) return Color.FromArgb(255, 85, 85);     // 红：超跳 / ultra 以上
            if (v > 240f) return Color.FromArgb(255, 205, 60);    // 黄：冲刺速度
            if (v > 90f) return Color.White;                      // 白：奔跑
            return Color.FromArgb(170, 170, 170);                 // 灰：闲逛
        }

        void DrawBody(Graphics g)
        {
            // 身体（挤压拉伸锚定脚底中心），矩形吸附到整数游戏像素
            bool flip = player.Facing < 0;
            var frame = Sprites.Get(animator.CurrentFrameId, flip);
            if (frame != null)
            {
                float sx = player.SpriteScaleX, sy = player.SpriteScaleY;
                float x = SnapPx(AnchorX - 16 * sx), y = SnapPx(AnchorY - 32 * sy);
                float w = SnapPx(32 * sx), h = SnapPx(32 * sy);
                // 原作 Render：IsTired && flash → Sprite.Color = Color.Red（乘法染色成红色剪影），攀爬动画不变
                if (player.IsTired && player.TiredFlash)
                    Sprites.DrawTinted(g, frame, Color.Red, x, y, w, h);
                else
                    g.DrawImage(frame, x, y, w, h);
            }
        }

        /// <summary>
        /// 汗水（原作 sweatSprite，白色水滴贴图直接绘制，与身体同锚定/缩放）。
        /// 攀爬时 Facing 朝向墙：墙在左（Facing<0）时水平镜像汗滴，墙在右保持原样。
        /// SweatOffsetY 可微调水滴相对头部的垂直位置（游戏像素，向下为正）。
        /// </summary>
        const float SweatOffsetY = 0f;
        void DrawSweat(Graphics g)
        {
            if (player.SweatId == "idle") return;
            bool flip = player.Facing < 0;   // 攀爬时 Facing 朝向墙：墙在左 → 镜像汗滴
            var frame = Sprites.Get(sweatAnimator.CurrentFrameId, flip);
            if (frame == null) return;
            float sx = player.SpriteScaleX, sy = player.SpriteScaleY;
            g.DrawImage(frame,
                SnapPx(AnchorX - 16 * sx), SnapPx(AnchorY - 32 * sy + SweatOffsetY),
                SnapPx(32 * sx), SnapPx(32 * sy));
        }

        void DrawHair(Graphics g, float camX, float camY)
        {
            // 皮肤配置：隐藏头发 / 固定头发颜色
            if (Skins.HideHair) return;
            var hair = player.Hair;
            Color color = Skins.HairColorOverride ?? player.HairColor;
            bool flip = player.Facing < 0;
            var blob = Sprites.Get("hair00", false);
            // 刘海帧：按当前动画帧的朝向元数据选择（0左看/1居中/2右看）；编辑模式下用实时值预览
            string bangsId = "bangs00";
            int bangsIdx = -1;
            if (HairMeta.TryGet(player.CurrentFrameId, out var hm) &&
                hm.Bangs >= 0 && hm.Bangs < HairMeta.BangsFrames.Length)
                bangsIdx = hm.Bangs;
            if (bangsIdx >= 0 && bangsIdx < HairMeta.BangsFrames.Length)
                bangsId = HairMeta.BangsFrames[bangsIdx];
            var bangs = Sprites.Get(bangsId, flip);
            if (blob == null || bangs == null) return;

            // 画布坐标（像素完美：原版渲染时 Nodes[0].Floor()，这里把每个节点吸附到整数游戏像素，
            // ×整数倍放大 = 整数物理像素，避免亚像素模糊）
            Span<PointF> pt = stackalloc PointF[PlayerHairSim.Count];
            for (int i = 0; i < PlayerHairSim.Count; i++)
                pt[i] = new PointF(
                    SnapPx(hair.Nodes[i].X - camX),
                    SnapPx(hair.Nodes[i].Y - camY));

            // 黑色描边（原作：±1px 四方向）
            for (int i = 0; i < PlayerHairSim.Count; i++)
            {
                float sc = HairSegmentScale(i);
                var tex = i == 0 ? bangs : blob;
                float w = SnapEven(10 * sc);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2 - 1, pt[i].Y - w / 2, w, w);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2 + 1, pt[i].Y - w / 2, w, w);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2, pt[i].Y - w / 2 - 1, w, w);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2, pt[i].Y - w / 2 + 1, w, w);
            }
            // 本体（后画前面，刘海最后）
            for (int i = PlayerHairSim.Count - 1; i >= 0; i--)
            {
                float sc = HairSegmentScale(i);
                var tex = i == 0 ? bangs : blob;
                float w = SnapEven(10 * sc);
                DrawTintedSafe(g, tex, color, pt[i].X - w / 2, pt[i].Y - w / 2, w, w);
            }
        }

        static float HairSegmentScale(int i)
            => 0.25f + (1f - (float)i / PlayerHairSim.Count) * 0.75f;

        // 像素完美：浮点游戏像素吸附到整数（偶数取整保证 w/2 也为整数，矩形边缘不落亚像素）
        static float SnapPx(float v) => (float)Math.Round(v);
        static float SnapEven(float v) => (float)(Math.Round(v / 2f) * 2f);

        static void DrawTintedSafe(Graphics g, Bitmap tex, Color c, float x, float y, float w, float h)
            => Sprites.DrawTinted(g, tex, c, x, y, w, h);

        // ================= 鼠标拖拽 =================
        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_MOUSEMOVE = 0x0200;
            const int WM_LBUTTONUP = 0x0202;
            const int WM_RBUTTONUP = 0x0205;

            switch (m.Msg)
            {
                case WM_LBUTTONDOWN:
                    dragging = true;
                    player.BeingDragged = true;
                    var feet = new Point((int)(player.Pos.X * GameScale), (int)(player.Pos.Y * GameScale));
                    dragGrabOffset = new Point(feet.X - Cursor.Position.X, feet.Y - Cursor.Position.Y);
                    lastCursor = Cursor.Position;
                    cursorVel = new PointF(0, 0);
                    break;
                case WM_MOUSEMOVE:
                    if (dragging)
                    {
                        var cur = Cursor.Position;
                        float dt = 1f / 60f;
                        cursorVel = new PointF(
                            cursorVel.X * 0.7f + (cur.X - lastCursor.X) / dt * 0.3f,
                            cursorVel.Y * 0.7f + (cur.Y - lastCursor.Y) / dt * 0.3f);
                        lastCursor = cur;
                        player.Pos = new PointF(
                            (cur.X + dragGrabOffset.X) / (float)GameScale,
                            (cur.Y + dragGrabOffset.Y) / (float)GameScale);
                    }
                    break;
                case WM_LBUTTONUP:
                    if (dragging)
                    {
                        dragging = false;
                        player.BeingDragged = false;
                        // 投掷：继承鼠标速度（转游戏单位，限速）
                        float vx = cursorVel.X / GameScale * 0.6f;
                        float vy = cursorVel.Y / GameScale * 0.6f;
                        float len = (float)Math.Sqrt(vx * vx + vy * vy);
                        if (len > 400) { vx = vx / len * 400; vy = vy / len * 400; }
                        if (len > 30) player.Speed = new PointF(vx, vy);
                        else player.Speed = new PointF(0, 0);
                    }
                    break;
                case WM_RBUTTONUP:
                    trayMenu.Show(Cursor.Position);
                    break;
            }
            base.WndProc(ref m);
        }

        // ================= 托盘 =================
        /// <summary>应用皮肤（游戏循环线程调用）。name="" 恢复默认。</summary>
        void ApplySkin(string name)
        {
            if (name == "")
            {
                Sprites.LoadSkin(null);
                animator.SetAnims(anims);
                activeSkin = "";
            }
            else if (Skins.TryGetSkinSource(name, out var zipPath, out var dir))
            {
                if (zipPath != null)
                {
                    Sprites.LoadSkinZip(zipPath, name);
                    var sa = Skins.BuildSkinAnims(zipPath, anims);
                    animator.SetAnims(sa.Count > 0 ? sa : anims);
                }
                else
                {
                    Sprites.LoadSkin(dir);
                    animator.SetAnims(anims);
                }
                activeSkin = name;
            }
            Skins.LoadOptions(activeSkin);
            Skins.SaveActive(activeSkin);
            PetWindow.Log("skin -> " + (activeSkin == "" ? "default" : activeSkin) +
                (Skins.HideHair ? " hair=off" : "") +
                (Skins.HairColorOverride != null ? " hair=" + Skins.HairColorOverride.Value.ToArgb().ToString("X6") : ""));

            // 切换皮肤 → 重播一遍醒来动画（同「回放醒来动画」）
            introWakeUp = true;
            animator.Play("wakeUp", true);
            player.SweatId = "idle";
            sweatAnimator.Play("idle", true);
        }

        /// <summary>刷新「皮肤」子菜单：默认 + 已安装皮肤（勾选当前）。</summary>
        void RebuildSkinMenu(ToolStripMenuItem skinMenu)
        {
            skinMenu.DropDownItems.Clear();
            var def = new ToolStripMenuItem("默认（玛德琳）", null, (_, __) => pendingSkin = "");
            def.Checked = activeSkin == "";
            skinMenu.DropDownItems.Add(def);
            foreach (var s in Skins.ListInstalled())
            {
                var item = new ToolStripMenuItem(s, null, (_, __) => pendingSkin = s);
                item.Checked = string.Equals(s, activeSkin, StringComparison.OrdinalIgnoreCase);
                skinMenu.DropDownItems.Add(item);
            }
        }

        /// <summary>选择 mod zip 安装皮肤，安装后自动切换过去。</summary>
        void InstallSkinMod()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 Celeste 皮肤 mod（.zip）";
                dlg.Filter = "Celeste 皮肤 mod (*.zip)|*.zip|所有文件 (*.*)|*.*";
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    string name = Skins.InstallZip(dlg.FileName);
                    pendingSkin = name;   // 装完直接切过去
                    PetWindow.Log("skin installed: " + name);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("安装失败：" + ex.Message, "玛德琳", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    PetWindow.Log("skin install failed: " + ex);
                }
            }
        }

        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();            var scaleItem = new ToolStripMenuItem("缩放（等比放大）");
            foreach (var v in new[] { 2, 3, 4, 5, 6, 8 })
            {
                var item = new ToolStripMenuItem(v + "x") { Tag = v, Checked = v == GameScale };
                item.Click += (_, __) =>
                {
                    pendingScale = v;
                    foreach (ToolStripMenuItem s in scaleItem.DropDownItems) s.Checked = (int)s.Tag == v;
                };
                scaleItem.DropDownItems.Add(item);
            }
            menu.Items.Add(scaleItem);

            var inputItem = new ToolStripMenuItem("响应键盘", null, (_, __) =>
            { InputEnabled = !InputEnabled; ((ToolStripMenuItem)menu.Items[1]).Checked = InputEnabled; })
            { Checked = true };
            menu.Items.Add(inputItem);

            var topItem = new ToolStripMenuItem("总是置顶", null, (_, __) =>
            {
                AlwaysOnTop = !AlwaysOnTop;
                ((ToolStripMenuItem)menu.Items[2]).Checked = AlwaysOnTop;
                Win32.SetWindowPos(Handle, AlwaysOnTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                    0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            })
            { Checked = true };
            menu.Items.Add(topItem);

            var particleItem = new ToolStripMenuItem("粒子特效", null, (sender, __) =>
            {
                ParticlesEnabled = !ParticlesEnabled;
                ((ToolStripMenuItem)sender).Checked = ParticlesEnabled;
            })
            { Checked = false };
            menu.Items.Add(particleItem);

            var speedMeterItem = new ToolStripMenuItem("速度计", null, (sender, __) =>
            {
                SpeedMeter = !SpeedMeter;
                if (SpeedMeter) peakHSpeed = peakTSpeed = 0f;   // 重新开启时清零峰值
                ((ToolStripMenuItem)sender).Checked = SpeedMeter;
            })
            { Checked = false };
            menu.Items.Add(speedMeterItem);

            var speedLogItem = new ToolStripMenuItem("速度日志", null, (sender, __) =>
            {
                SpeedLog = !SpeedLog;
                SetSpeedLog(SpeedLog);
                ((ToolStripMenuItem)sender).Checked = SpeedLog;
            })
            { Checked = false };
            menu.Items.Add(speedLogItem);

            var freezeItem = new ToolStripMenuItem("冲刺冻结帧", null, (sender, __) =>
            {
                player.FreezeFrameEnabled = !player.FreezeFrameEnabled;
                ((ToolStripMenuItem)sender).Checked = player.FreezeFrameEnabled;
            })
            { Checked = true };
            menu.Items.Add(freezeItem);

            // ---- 皮肤 ----
            var skinMenu = new ToolStripMenuItem("皮肤");
            menu.Items.Add(skinMenu);
            var installItem = new ToolStripMenuItem("安装皮肤 mod（zip）…", null, (_, __) => InstallSkinMod());
            menu.Items.Add(installItem);
            menu.Opening += (_, __) => RebuildSkinMenu(skinMenu);   // 每次打开时刷新已装皮肤

            var hairEditItem = new ToolStripMenuItem("头发调试器（F1 进入）", null, (sender, __) =>
            {
                HairDebug = !HairDebug;
                if (!HairDebug && HairEdit) ExitHairEdit();
                ((ToolStripMenuItem)sender).Checked = HairDebug;
            })
            { Checked = false };
            menu.Items.Add(hairEditItem);

            menu.Items.Add(new ToolStripMenuItem("按键设置", null, (_, __) =>
            {
                KeyBinds.DialogOpen = true;
                try
                {
                    using var dlg = new KeyBindDialog();
                    if (dlg.ShowDialog() == DialogResult.OK) KeyBinds.Save(keysPath);
                    else KeyBinds.Load(keysPath);   // 取消 → 还原上次保存的绑定
                }
                finally { KeyBinds.DialogOpen = false; }
            }));

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("回放醒来动画", null, (_, __) =>
            {
                introWakeUp = true;
                animator.Play("wakeUp", true);
                player.SweatId = "idle";          // 睡觉期间不出汗
                sweatAnimator.Play("idle", true);
            }));
            menu.Items.Add(new ToolStripMenuItem("重置位置", null, (_, __) => ResetPosition()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("操作说明", null, (_, __) =>
                MessageBox.Show(
                    "移动：方向键 / AD\n跳跃：C（土狼时间+可变跳高）\n冲刺：X（8方向，着地恢复）\n攀爬：Z（对准墙按住，消耗体力）\n\n技巧（原作全套）：\n· Super：地面冲刺中按 C → 水平 260 超级跳\n· Hyper：地面斜下冲（↓+X）转蹲冲后按 C → 325 超跳\n· Ultra：空中斜下冲落地瞬间按 C → 高速弹起\n· Cornerboost：冲刺撞墙后 0.06s 内抓墙+蹬墙跳，越过墙顶保留冲刺速度\n· 蹬墙跳：贴墙跳 C；冲刺中抓墙 Z 转攀爬\n· 左键拖着玛德琳甩出去\n\n窗口是空心边框：内部可自由穿行，\n可站边框、爬侧边",
                    "玛德琳桌宠", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出", null, (_, __) => ExitApp()));
            return menu;
        }

        Icon BuildTrayIcon()
        {
            // 用玛德琳头像做托盘图标（头像非像素风，平滑缩小）
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "portrait.png");
            try
            {
                using (var src = new Bitmap(path))
                {
                    var bmp = new Bitmap(32, 32, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.DrawImage(src, 0, 0, 32, 32);
                    }
                    IntPtr hIcon = bmp.GetHicon();
                    trayIconHandle = hIcon;   // 记录句柄，退出时 DestroyIcon
                    bmp.Dispose();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch { return SystemIcons.Application; }
        }

        // 离开屏幕很远自动重置（防"无限下落" / 被拖拽甩出虚拟屏幕）
        void CheckAutoReset()
        {
            if (dragging || introWakeUp) return;
            var vs = SystemInformation.VirtualScreen;
            float gx = player.Pos.X * GameScale;
            float gy = player.Pos.Y * GameScale;
            // 阈值：横向超出 1 个屏宽、纵向超出 1.5 个屏高，视为"离开很远"
            bool far = gx < vs.Left - vs.Width || gx > vs.Right + vs.Width ||
                       gy < vs.Top - vs.Height * 1.5f || gy > vs.Bottom + vs.Height * 1.5f;
            if (far) ResetPosition();
        }

        // 重置：从当前所在屏幕顶部中央出现，然后自由下落
        void ResetPosition()
        {
            // 先钳制到虚拟屏幕范围内（防"无限下落"时坐标溢出选错显示器）
            var vs = SystemInformation.VirtualScreen;
            float px = Math.Max(vs.Left, Math.Min(player.Pos.X * GameScale, vs.Right - 1f));
            float py = Math.Max(vs.Top, Math.Min(player.Pos.Y * GameScale, vs.Bottom - 1f));
            var sc = Screen.FromPoint(new Point((int)px, (int)py));
            var wa = sc.WorkingArea;
            player.ResetTo(new PointF((wa.Left + wa.Right) / 2f / GameScale, wa.Top / GameScale + 5));
            dragging = false;
            if (introWakeUp)
            {
                introWakeUp = false;
                animator.Play(player.AnimId, true);
            }
            PetWindow.Log("reset pos to " + player.Pos.X.ToString("F1") + "," + player.Pos.Y.ToString("F1"));
        }

        void ExitApp()
        {
            if (SpeedLog) SetSpeedLog(false);   // 退出时补写"结束 + 峰值"标记
            running = false;
            tray.Visible = false;
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            running = false;
            // 等游戏循环线程结束当前帧再释放资源，避免渲染线程还在用 DIB/DC 时被删除
            if (loopThread != null && loopThread != Thread.CurrentThread)
                loopThread.Join(1500);
            tray.Visible = false;
            tray.Dispose();
            // 释放托盘图标 HICON 与分层窗口 GDI 资源（DIB/DC）
            if (trayIconHandle != IntPtr.Zero) { Win32.DestroyIcon(trayIconHandle); trayIconHandle = IntPtr.Zero; }
            presenter?.Dispose();
            base.OnFormClosing(e);
        }
    }

    /// <summary>把 32bpp 预乘位图通过 UpdateLayeredWindow 呈现到分层窗口。</summary>
    class LayeredPresenter : IDisposable
    {
        readonly IntPtr hwnd;
        int w, h;
        IntPtr memDc, dib, oldObj, bits;
        byte[] managedBuf;

        public LayeredPresenter(IntPtr hwnd, int w, int h)
        {
            this.hwnd = hwnd;
            Resize(w, h);
        }

        public void Resize(int w, int h)
        {
            FreeGdi();
            this.w = w; this.h = h;
            IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
            memDc = Win32.CreateCompatibleDC(screenDc);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);

            var bmi = new Win32.BITMAPINFO();
            bmi.bmiHeader.biSize = 40;
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB（32bpp 时顶字节作为 alpha，ULW 会读取）
            dib = Win32.CreateDIBSection(memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero)
                PetWindow.Log("CreateDIBSection failed: dib=" + dib + " bits=" + bits + " err=" + Marshal.GetLastWin32Error());
            oldObj = Win32.SelectObject(memDc, dib);
            managedBuf = new byte[w * h * 4];
        }

        public void Present(Bitmap bmp, int left, int top)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(data.Scan0, managedBuf, 0, managedBuf.Length); }
            finally { bmp.UnlockBits(data); }
            Marshal.Copy(managedBuf, 0, bits, managedBuf.Length);

            var ptDst = new Win32.POINT { X = left, Y = top };
            var sz = new Win32.SIZE { cx = w, cy = h };
            var ptSrc = new Win32.POINT { X = 0, Y = 0 };
            var blend = new Win32.BLENDFUNCTION
            {
                BlendOp = Win32.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Win32.AC_SRC_ALPHA
            };
            IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
            bool ok = Win32.UpdateLayeredWindow(hwnd, screenDc, ref ptDst, ref sz, memDc, ref ptSrc, 0, ref blend, Win32.ULW_ALPHA);
            if (!ok)
            {
                if (!loggedFail)
                {
                    loggedFail = true;
                    int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                    Win32.GetWindowRect(hwnd, out var r);
                    PetWindow.Log("ULW failed, err=" + Marshal.GetLastWin32Error() +
                        " exstyle=" + ex + " rect=" + r.Left + "," + r.Top + "," + r.Width + "x" + r.Height +
                        " psize=" + w + "x" + h + " dib=" + dib + " memDc=" + memDc);
                }
            }
            else if (!loggedOk)
            {
                loggedOk = true;
                PetWindow.Log("ULW ok at " + left + "," + top + " size " + w + "x" + h);
            }
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }

        bool loggedFail;
        bool loggedOk;

        void FreeGdi()
        {
            if (memDc != IntPtr.Zero)
            {
                if (oldObj != IntPtr.Zero) Win32.SelectObject(memDc, oldObj);
                if (dib != IntPtr.Zero) Win32.DeleteObject(dib);
                Win32.DeleteDC(memDc);
                memDc = dib = oldObj = IntPtr.Zero;
            }
        }

        public void Dispose() => FreeGdi();
    }

    /// <summary>提升定时器精度（winmm）。</summary>
    static class TimePeriod
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint uMilliseconds);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint uMilliseconds);
        public static void Begin(uint ms) => timeBeginPeriod(ms);
        public static void End(uint ms) => timeEndPeriod(ms);
    }
}
