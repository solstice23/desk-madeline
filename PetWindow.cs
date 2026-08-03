using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// 桌宠主窗口：分层透明窗口 + 60FPS 游戏循环 + 窗口平台轮询 + 托盘菜单。
    /// 所有桌面坐标为物理像素（进程 PerMonitorV2）；物理模拟在游戏像素空间进行（1 游戏像素 = S 物理像素）。
    /// </summary>
    public class PetWindow : Form
    {
        // ===== 可调参数 =====
        public int GameScale = 6;               // 整数倍放大（原作 1080p 为 6x）
        public bool InputEnabled = true;
        public bool InputWhenUnfocused;
        public bool AlwaysOnTop = true;
        public static PetWindow Instance;

        // Keep a fixed, wide 1x render footprint. At 768 game pixels the complete
        // one-second trail remains available even through fast ultras; GPU scaling
        // makes this much cheaper than the old full-size GDI canvas.
        const int CanvasW = 1024, CanvasH = 160;
        const float AnchorX = 512, AnchorY = 80; // 脚底锚点（画布内）
        const double FixedDt = 1.0 / 60.0;
        static readonly IntPtr FloorId = new IntPtr(-991);
        const int WindowBorderPx = 8;           // 窗口空心边框厚度（物理像素）

        readonly Player player = new Player();
        readonly KeyBindings bindings;
        readonly PetSettings settings;
        internal readonly SkinManager skinManager;
        readonly Animator animator;
        readonly Animator sweatAnimator;
        readonly Dictionary<string, Anim> anims;
        readonly NotifyIcon tray;
        ContextMenuStrip trayMenu;

        Thread loopThread;
        volatile bool running;
        int pendingScale = -1;
        volatile string pendingSkinId;
        int pendingGliderSpawns;
        bool introWakeUp = true;   // 启动时先播"醒来"动画（wakeUp 00-14），播完切 idle

        // 渲染
        Bitmap small;           // 1x 游戏像素缓冲（CanvasW × CanvasH），整数坐标绘制后整数倍放大
        readonly TrailStamp[] trailStamps = new TrailStamp[16];
        D3DPresenter presenter;
        CompositionHost compositionHost;
        readonly Rectangle virtualDesktop;
        int renderFrameCount;

        // 平台
        readonly Dictionary<IntPtr, Win32.RECT> lastRects = new Dictionary<IntPtr, Win32.RECT>();
        int pollCounter;

        // 输入状态
        bool prevJump, prevDash, prevCrouchDash;

        // 拖拽
        volatile bool dragging;
        Point dragGrabOffset;      // 物理像素：抓取点相对脚底
        PointF cursorVel;          // 物理像素/秒
        Point lastCursor;
        IntPtr trayIconHandle;     // 托盘图标的 HICON（需显式 DestroyIcon）

        // 粒子 / 特效
        readonly ParticleSystem particles = new ParticleSystem();
        readonly List<WaveRing> waveRings = new List<WaveRing>();
        readonly Random effectRng = new Random();
        PType dust, dashBlue, dashRed;
        bool ParticlesEnabled = true;    // 粒子特效开关（默认开，托盘菜单可关闭）
        float skidDustTimer;
        string observedParticleAnimId;
        int observedParticleAnimFrame = -1;
        int observedLaunchCount;
        int observedRingDashSequenceCount;
        int observedWallJumpEffectCount;
        int observedJumpEffectCount;
        int observedLandingEffectCount;
        int observedSweatAnimSequenceCount;
        bool speedRingLaunchActive;
        float speedRingLaunchTimer;
        float nextSpeedRingTime;
        float dashParticleTimer;
        float tiredFlashTimer;
        bool tiredFlash;
        readonly List<DashTrail> dashTrails = new List<DashTrail>();
        SlashVisual slash;
        int observedDashSequenceCount;
        bool dashVisualPending;
        float dashVisualTimer = -1f;
        int dashTrailStage;
        bool english = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "zh";
        const int CatTailCount = 8;
        readonly PointF[] catTailNodes = new PointF[CatTailCount];
        readonly Color[] customHairColors = new Color[3];
        readonly Queue<int> speedometerSamples = new Queue<int>(10);
        bool catTailStarted;
        bool catTailEnabled, catBangsEnabled, customHairColorsEnabled;
        int speedometerMode;
        bool hitboxesEnabled;
        readonly Bitmap[] picoDigits = new Bitmap[10];
        readonly List<Glider> gliders = new List<Glider>();

        struct WaveRing
        {
            public float X, Y, Angle, Progress;
        }

        sealed class DashTrail
        {
            public float X, Y, ScaleX, ScaleY, Age;
            public int Facing, HairCount;
            public string FrameId, BangsId;
            public Color Tint, HairColor;
            public PointF[] HairNodes;
            public PointF[] CatTailNodes;
            public Bitmap Mask;
        }

        struct SlashVisual
        {
            public bool Active;
            public float X, Y, Angle, Age;
        }

        public PetWindow()
        {
            Instance = this;
            settings = PetSettings.Load(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "settings.txt"));
            virtualDesktop = GetVirtualDesktopBounds();
            GameScale = settings.Scale;
            InputEnabled = settings.InputEnabled;
            InputWhenUnfocused = settings.InputWhenUnfocused;
            AlwaysOnTop = settings.AlwaysOnTop;
            ParticlesEnabled = settings.ParticlesEnabled;
            catTailEnabled = settings.CatTailEnabled;
            catBangsEnabled = settings.CatBangsEnabled;
            customHairColorsEnabled = settings.CustomHairColorsEnabled;
            customHairColors[0] = Rgb(settings.HairColor0);
            customHairColors[1] = Rgb(settings.HairColor1);
            customHairColors[2] = Rgb(settings.HairColor2);
            speedometerMode = settings.SpeedometerMode;
            hitboxesEnabled = settings.HitboxesEnabled;
            player.SetFreezeFramesEnabled(settings.FreezeFramesEnabled);
            player.InfiniteStamina = settings.InfiniteStamina;
            player.SetDashMode(settings.DashMode);
            player.Gliders = gliders;
            if (settings.Language == "en") english = true;
            else if (settings.Language == "zh") english = false;
            bindings = new KeyBindings(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "keybindings.txt"));
            // 日志防无限增长：超过 5MB 时清空重写（保留最近一次运行记录）
            try
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet_debug.log");
                if (new System.IO.FileInfo(logPath).Length > 5 * 1024 * 1024)
                    System.IO.File.WriteAllText(logPath, "");
            }
            catch { }
            // ---- 窗口样式：无边框、不在任务栏、DirectComposition 透明 ----
            FormBorderStyle = FormBorderStyle.None;
            Text = "Desk Madeline";
            // 分层画布的尺寸由 GameScale 明确控制，不允许 WinForms 在 WM_DPICHANGED
            // 时再按字体 DPI 缩放一次。
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // 注意：不要设置 BackColor/TransparencyKey（色键分层与 ULW 冲突）
            Size = new Size(24 * GameScale, 33 * GameScale);
            Location = new Point(-10000, -10000);
            BackColor = Color.Black;
            Opacity = 0.01; // nonzero alpha keeps the small body hit target interactive

            // ---- 贴图与动画 ----
            skinManager = new SkinManager(AppDomain.CurrentDomain.BaseDirectory);
            var initialSkin = skinManager.Find(settings.Skin);
            skinManager.Activate(initialSkin);
            Sprites.LoadAll(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "player"),
                initialSkin?.PlayerDirectory);
            try
            {
                using var fontSource = new Bitmap(System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "assets", "pico8font.png"));
                for (int digit = 0; digit < picoDigits.Length; digit++)
                {
                    int sourceX = digit < 4 ? 104 + digit * 4 : (digit - 4) * 4;
                    int sourceY = digit < 4 ? 0 : 6;
                    picoDigits[digit] = fontSource.Clone(
                        new Rectangle(sourceX, sourceY, 3, 5), PixelFormat.Format32bppPArgb);
                }
            }
            catch { }
            HairMeta.LoadOverrides(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hair_tweaks.txt"));
            dust = new PType
            {
                Tex = new[] { "smoke0", "smoke1", "smoke2", "smoke3" },
                Color = Color.White,
                GravY = 4f,
                LifeMin = 0.3f, LifeMax = 0.5f,
                // ParticleType.Size scales the 8x8 smoke texture by 0.7 ± 0.2.
                Size = 5.6f, SizeRange = 1.6f,
                SpeedMin = 5f, SpeedMax = 15f,
                ScaleOut = true
            };
            dashBlue = new PType
            {
                Tex = new[] { "dashParticle" },
                Color = Color.FromArgb(0x44, 0xB7, 0xFF),
                Color2 = Color.FromArgb(0x75, 0xC9, 0xFF),
                BlinkColor = true,
                GravY = 8f,
                LifeMin = 1f, LifeMax = 1.8f,
                Size = 1f,
                SpeedMin = 10f, SpeedMax = 20f,
                LateFade = true
            };
            dashRed = new PType
            {
                Tex = new[] { "dashParticle" },
                Color = Color.FromArgb(0xAC, 0x32, 0x32),
                Color2 = Color.FromArgb(0xE0, 0x59, 0x59),
                BlinkColor = true,
                GravY = 8f,
                LifeMin = 1f, LifeMax = 1.8f,
                Size = 1f,
                SpeedMin = 10f, SpeedMax = 20f,
                LateFade = true
            };
            anims = BuildAnims();
            animator = new Animator(anims);
            sweatAnimator = new Animator(BuildSweatAnims());
            sweatAnimator.Play("idle");
            animator.Play("wakeUp");   // 启动先播醒来动画

            // ---- 出生点：主屏工作区底部中央 ----
            var wa = Screen.PrimaryScreen.WorkingArea;
            player.Pos = new PointF((wa.Left + wa.Right) / 2f / GameScale, wa.Bottom / GameScale - 2);

            // ---- 托盘 ----
            trayMenu = BuildMenu();
            tray = new NotifyIcon
            {
                Text = T("Madeline", "玛德琳"),
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
                // 保持工具窗口（不出现在 Alt+Tab/任务栏），但必须允许激活：点击玛德琳后
                // 她取得键盘焦点，移动键便不会同时输入到其他程序。
                cp.ExStyle |= Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW;
                if (AlwaysOnTop) cp.ExStyle |= WS_EX_TOPMOST;
                return cp;
            }
        }

        static Rectangle GetVirtualDesktopBounds()
        {
            int left = int.MaxValue, top = int.MaxValue;
            int right = int.MinValue, bottom = int.MinValue;
            foreach (var screen in Screen.AllScreens)
            {
                left = Math.Min(left, screen.Bounds.Left);
                top = Math.Min(top, screen.Bounds.Top);
                right = Math.Max(right, screen.Bounds.Right);
                bottom = Math.Max(bottom, screen.Bounds.Bottom);
            }
            return left == int.MaxValue
                ? new Rectangle(0, 0, 1920, 1080)
                : Rectangle.FromLTRB(left, top, right, bottom);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            compositionHost = new CompositionHost(virtualDesktop, AlwaysOnTop);
            compositionHost.Show();
            presenter = new D3DPresenter(compositionHost.Handle, CanvasW, CanvasH, GameScale, virtualDesktop);
            Win32.SetWindowPos(Handle, AlwaysOnTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            PollSolids();
            player.Hair.Reset(new PointF(player.Pos.X, player.Pos.Y - 9), player.Facing);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 窗口真正显示后再启动游戏循环（DirectComposition target 要求 HWND 已就绪）
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

            Add("idle", Sprites.Seq("idle", 0, 8), 0.1f, true);
            var wakeUp = new List<string>(Sprites.Seq("wakeUp", 0, 4));
            for (int i = 0; i < 10 && Sprites.Has("wakeUp05"); i++) wakeUp.Add("wakeUp05");
            wakeUp.AddRange(Sprites.Seq("wakeUp", 6, 14));
            Add("wakeUp", wakeUp.ToArray(), 0.1f, false); // Sprites.xml: 0-4, 5*10, 6-14
            Add("idleA", Sprites.Seq("idleA", 0, 30), 0.12f, false);
            Add("idleB", Sprites.Seq("idleB", 0, 30), 0.16f, false);
            Add("idleC", Sprites.Seq("idleC", 0, 30), 0.05f, false);
            foreach (string fidget in new[] { "idleA", "idleB", "idleC" })
                if (d.TryGetValue(fidget, out var fidgetAnim)) fidgetAnim.Goto = "idle";
            Add("runSlow", Sprites.Seq("runSlow", 0, 11), 0.07f, false);
            if (d.TryGetValue("runSlow", out var runSlowAnim)) runSlowAnim.Goto = "runFast";
            Add("runFast", Sprites.Seq("runFast", 0, 11), 0.05f, true);
            Add("idle_carry", Sprites.Seq("idle_carry", 0, 8), 0.1f, true);
            Add("runSlow_carry", Sprites.Seq("run_carry", 0, 11), 0.07f, true);
            Add("jumpSlow_carry", Sprites.Seq("jump_carry", 0, 1), 0.1f, true);
            Add("fallSlow_carry", Sprites.Seq("jump_carry", 2, 3), 0.1f, false);
            Add("pickUp", Sprites.Seq("pickup", 0, 4), 0.06f, false);
            var stumble = new List<string> { "runStumble10", "runStumble11" };
            stumble.AddRange(Sprites.Seq("runStumble", 0, 11));
            Add("runStumble", stumble.ToArray(), 0.05f, false);
            if (d.TryGetValue("runStumble", out var stumbleAnim)) stumbleAnim.Goto = "runFast";
            // Vanilla Sprites.xml splits each jump sheet in half: 00/01 loop while
            // rising, then 02/03 play once and hold while falling.  The separate
            // fall00-07 sheet belongs to the scripted "fall" state, not fast-fall.
            Add("jumpSlow", Sprites.Seq("jumpSlow", 0, 1), 0.10f, true);
            Add("jumpFast", Sprites.Seq("jumpFast", 0, 1), 0.10f, true);
            Add("fallSlow", Sprites.Seq("jumpSlow", 2, 3), 0.10f, false);
            Add("fallFast", Sprites.Seq("jumpFast", 2, 3), 0.10f, false);
            Add("dash", Sprites.Seq("dash", 0, 3), 0.09f, true);
            Add("climb", Sprites.Seq("climb", 0, 5), 0.04f, true);
            Add("wallslide", new[] { "climb00" }, 1f, true);
            Add("climbLookBack", new[] { "climb08" }, 1f, true);
            Add("climbLookBackStart", new[] { "climb06", "climb07", "climb08" }, 0.08f, false);
            if (d.TryGetValue("climbLookBackStart", out var lookBackStart)) lookBackStart.Goto = "climbLookBack";
            Add("dangling", Sprites.Seq("dangling", 0, 9), 0.11f, true);
            Add("duck", new[] { "duck" }, 1f, true);
            Add("lookUp", Sprites.Seq("lookUp", 2, 7), 0.1f, false);
            Add("tired", Sprites.Seq("tired", 0, 3), 0.18f, true);
            Add("edge", Sprites.Seq("edge", 0, 13), 0.25f, true);
            Add("edgeBack", Sprites.Seq("edge_back", 0, 13), 0.25f, true);
            Add("push", Sprites.Seq("push", 0, 15), 0.1f, true);
            Add("flip", Sprites.Seq("flip", 0, 7), 0.04f, false);
            if (d.TryGetValue("flip", out var flipAnim)) flipAnim.Goto = "runFast";
            Add("skid", new[] { "flip08" }, 1f, true);
            return d;
        }

        static Dictionary<string, Anim> BuildSweatAnims()
        {
            var d = new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase);
            void Add(string id, string[] frames, float delay, bool loop)
            { if (frames.Length > 0) d[id] = new Anim { Frames = frames, Delay = delay, Loop = loop }; }
            Add("idle", new[] { "sweatIdle00" }, 1f, true);
            Add("still", Sprites.Seq("sweatStill", 0, 5), 0.1f, true);
            Add("climbLoop", Sprites.Seq("sweatClimb", 2, 7), 0.1f, true);
            d["climb"] = new Anim { Frames = Sprites.Seq("sweatClimb", 0, 1), Delay = 0.1f, Goto = "climbLoop" };
            Add("danger", Sprites.Seq("sweatDanger", 0, 5), 0.05f, true);
            d["jump"] = new Anim { Frames = Sprites.Seq("sweatJump", 0, 3), Delay = 0.1f, Goto = "idle" };
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
            if (pendingSkinId != null)
            {
                string id = pendingSkinId;
                pendingSkinId = null;
                var skin = skinManager.Find(id);
                Sprites.LoadAll(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "player"),
                    skin?.PlayerDirectory);
                skinManager.Activate(skin);
                settings.Skin = skin?.Id ?? SkinManager.DefaultId;
                settings.Save();
                Log("skin -> " + (skin?.DisplayName ?? "default"));
            }

            int spawnCount = Interlocked.Exchange(ref pendingGliderSpawns, 0);
            for (int i = 0; i < spawnCount; i++)
            {
                // Spawn just above and in front of Madeline, as a normal unheld
                // Glider actor rather than placing it directly in her pickup box.
                gliders.Add(new Glider(new PointF(
                    player.Pos.X + player.Facing * (18f + i * 5f), player.Pos.Y - 16f)));
            }

            // 应用待定的缩放变更
            if (pendingScale > 0)
            {
                GameScale = pendingScale;
                pendingScale = -1;
                presenter.Resize(GameScale);
                pollCounter = 999; // 立即重取平台（单位变了）
            }

            // 平台轮询（每 0.25s）
            if (++pollCounter >= 15)
            {
                pollCounter = 0;
                PollSolids();
            }

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

            bool frozenAtStart = player.FreezeFramesEnabled && player.IsHitStopped;
            if (!frozenAtStart)
            {
                // Hair/Sprite/Sweat components update before StateMachine in Player.
                animator.Update(dt);
                sweatAnimator.Update(dt);
                player.AnimFinished = animator.Finished ||
                    !string.Equals(player.AnimId, animator.CurrentId, StringComparison.OrdinalIgnoreCase);
                player.AnimLoopCount = animator.LoopCount;
                player.CurrentFrameId = animator.CurrentFrameId;
                if (ParticlesEnabled) EmitAnimationParticles();

                tiredFlashTimer += dt;
                while (tiredFlashTimer >= 0.05f)
                {
                    tiredFlashTimer -= 0.05f;
                    tiredFlash = !tiredFlash;
                }
            }

            // 物理
            int wasState = player.State;
            player.Update(dt, input);

            // A frame that began frozen only advances the raw freeze countdown.
            if (frozenAtStart)
            {
                UpdateDashCoreVisuals(0f); // observe the dash, but do not spawn/age FX
                return;
            }

            // UpdateSprite selects animations after component advancement. A newly
            // selected animation stays on frame zero until the next game frame.
            animator.Play(player.AnimId);
            bool restartSweat = player.SweatAnimSequenceCount != observedSweatAnimSequenceCount;
            observedSweatAnimSequenceCount = player.SweatAnimSequenceCount;
            sweatAnimator.Play(player.SweatAnimId, restartSweat);
            player.AnimFinished = animator.Finished ||
                !string.Equals(player.AnimId, animator.CurrentId, StringComparison.OrdinalIgnoreCase);
            player.AnimLoopCount = animator.LoopCount;
            player.CurrentFrameId = animator.CurrentFrameId;
            if (ParticlesEnabled) EmitAnimationParticles();

            float hairX = 0f, hairY = 0f;
            if (HairMeta.TryGet(animator.CurrentFrameId, out var hairMeta))
            {
                hairX = hairMeta.Offset.X;
                hairY = hairMeta.Offset.Y;
            }
            player.UpdateHairOnly(dt, hairX, hairY);
            UpdateCatTail(dt);

            // DashBegin starts freeze mid-update, after sprite selection and hair
            // anchoring, but before DashCoroutine creates slash/trail effects.
            if (player.FreezeFramesEnabled && player.IsHitStopped)
            {
                UpdateDashCoreVisuals(0f);
                return;
            }

            foreach (Glider glider in gliders)
                glider.Update(dt, input, player.Solids, player.MinX, player.MaxX);

            UpdateWavedashWaves(dt);

            // 离开屏幕很远自动重置（防"无限下落"/被甩出虚拟屏幕）
            if (!introWakeUp) CheckAutoReset();

            // 粒子（走路/落地/跳跃/冲刺）+ 冲刺斩击计时
            if (ParticlesEnabled)
            {
                EmitPlayerParticles(dt, wasState);
                particles.Update(dt);
            }
            else
            {
                particles.Clear();
                observedJumpEffectCount = player.JumpEffectCount;
                observedWallJumpEffectCount = player.WallJumpEffectCount;
                observedLandingEffectCount = player.LandingEffectCount;
            }

            UpdateDashCoreVisuals(dt);
        }

        static Color Rgb(int value) => Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255);
        static int RgbValue(Color value) => (value.R << 16) | (value.G << 8) | value.B;

        internal Color ResolveHairColor(int dashes, Color fallback)
        {
            if (customHairColorsEnabled) return customHairColors[Math.Max(0, Math.Min(2, dashes))];
            return skinManager.ResolveHairColor(dashes, fallback);
        }

        static PointF Approach(PointF value, PointF target, float maxMove)
        {
            float dx = target.X - value.X, dy = target.Y - value.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= maxMove || length == 0f) return target;
            return new PointF(value.X + dx / length * maxMove, value.Y + dy / length * maxMove);
        }

        void UpdateCatTail(float dt)
        {
            if (!catTailEnabled) { catTailStarted = false; return; }
            float hx = 0f, hy = 0f;
            if (HairMeta.TryGet(player.CurrentFrameId, out var hm)) { hx = hm.Offset.X; hy = hm.Offset.Y; }
            var root = new PointF(player.Pos.X + hx * player.Facing,
                player.Pos.Y - 2f * player.SpriteScaleY + hy);
            if (!catTailStarted)
            {
                for (int i = 0; i < CatTailCount; i++) catTailNodes[i] = root;
                catTailStarted = true;
            }
            catTailNodes[0] = root;
            float amount = 1.5f;
            var step = new PointF(0f, -1f);
            var target = new PointF(root.X - player.Facing * amount * 2f + step.X, root.Y + step.Y);
            PointF previous = root;
            for (int i = 1; i < CatTailCount; i++)
            {
                if (i == CatTailCount / 2)
                {
                    float sine = (float)Math.Sin(player.Hair.Wave);
                    step = new PointF(0f, -1f + 0.5f * sine);
                    amount = 1.5f + 0.5f * sine;
                }
                float speed = (1f - (float)i / CatTailCount * 0.5f) * 32f;
                catTailNodes[i] = Approach(catTailNodes[i], target, speed * dt);
                float dx = catTailNodes[i].X - previous.X, dy = catTailNodes[i].Y - previous.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance > 2f)
                    catTailNodes[i] = new PointF(previous.X + dx / distance * 2f, previous.Y + dy / distance * 2f);
                target = new PointF(catTailNodes[i].X - player.Facing * amount + step.X,
                    catTailNodes[i].Y + step.Y);
                previous = catTailNodes[i];
            }
        }

        void UpdateDashCoreVisuals(float dt)
        {
            // Existing effects update before Player/DashCoroutine can add new ones,
            // so a freshly-created slash or snapshot renders once at age zero.
            if (slash.Active)
            {
                slash.Age += dt;
                slash.X += (float)Math.Cos(slash.Angle) * 8f * dt;
                slash.Y += (float)Math.Sin(slash.Angle) * 8f * dt;
                if (slash.Age >= 0.4f) slash.Active = false;
            }
            for (int i = dashTrails.Count - 1; i >= 0; i--)
            {
                dashTrails[i].Age += dt;
                if (dashTrails[i].Age >= 1f)
                {
                    dashTrails[i].Mask?.Dispose();
                    dashTrails.RemoveAt(i);
                }
            }

            if (player.DashSequenceCount != observedDashSequenceCount)
            {
                observedDashSequenceCount = player.DashSequenceCount;
                dashVisualPending = true;
                dashVisualTimer = -1f;
                dashTrailStage = 0;
            }

            // Celeste.Freeze(0.05) occurs before DashCoroutine creates SlashFx and the first trail.
            bool spawnedThisFrame = false;
            if (dashVisualPending && !player.IsFrozen)
            {
                dashVisualPending = false;
                dashVisualTimer = 0f;
                slash = new SlashVisual
                {
                    Active = true,
                    X = player.Pos.X,
                    Y = player.Pos.Y - 5.5f,
                    Angle = (float)Math.Atan2(player.DashDir.Y, player.DashDir.X)
                };
                CaptureDashTrail();
                dashTrailStage = 1;
                spawnedThisFrame = true;
            }

            if (dashVisualTimer >= 0f && !spawnedThisFrame)
            {
                float previous = dashVisualTimer;
                dashVisualTimer += dt;
                // Normal DashCoroutine: immediate snapshot, DashUpdate snapshot at 0.08,
                // and final coroutine snapshot at 0.15 seconds.
                if (dashTrailStage == 1 && previous < 0.08f && dashVisualTimer >= 0.08f)
                {
                    CaptureDashTrail();
                    dashTrailStage = 2;
                }
                if (dashTrailStage == 2 && previous < 0.15f && dashVisualTimer >= 0.15f)
                {
                    CaptureDashTrail();
                    dashTrailStage = 3;
                    dashVisualTimer = -1f;
                }
            }

        }

        void CaptureDashTrail()
        {
            int count = player.Hair.ActiveCount;
            var nodes = new PointF[count];
            for (int i = 0; i < count; i++) nodes[i] = player.Hair.Nodes[i];
            string frameId = animator.CurrentFrameId ?? (player.Ducking ? "duck" : "dash00");
            string bangsId = "bangs00";
            if (HairMeta.TryGet(frameId, out var hm) && hm.Bangs >= 0 && hm.Bangs < HairMeta.BangsFrames.Length)
                bangsId = HairMeta.BangsFrames[hm.Bangs];
            if (catBangsEnabled) bangsId = "catbangs" + bangsId.Substring(bangsId.Length - 2);
            PointF[] tailNodes = null;
            if (catTailEnabled)
            {
                tailNodes = new PointF[CatTailCount];
                Array.Copy(catTailNodes, tailNodes, CatTailCount);
            }
            var trail = new DashTrail
            {
                X = player.Pos.X,
                Y = player.Pos.Y,
                ScaleX = player.SpriteScaleX,
                ScaleY = player.SpriteScaleY,
                Facing = player.Facing,
                FrameId = frameId,
                BangsId = bangsId,
                HairCount = count,
                HairNodes = nodes,
                CatTailNodes = tailNodes,
                HairColor = player.HairColor,
                // Player.GetTrailColor(wasDashB), resolved through the active skin's
                // corresponding one-dash / no-dash palette.
                Tint = player.LastDashWasTwo
                    ? ResolveHairColor(1, Player.NormalHairColor)
                    : ResolveHairColor(0, Player.UsedHairColor)
            };
            trail.Mask = BakeDashTrailMask(trail);
            dashTrails.Add(trail);
        }

        Bitmap BakeDashTrailMask(DashTrail trail)
        {
            // Celeste's TrailManager renders hair + sprite once into an off-screen
            // buffer, converts its resulting alpha to a white mask, then only moves and
            // fades that immutable texture. Rebuilding individual pieces every frame
            // changes raster coverage and causes direction-aligned shimmer.
            const int sizePx = 64;
            const float center = sizePx / 2f;
            var mask = new Bitmap(sizePx, sizePx, PixelFormat.Format32bppPArgb);
            var g = Graphics.FromImage(mask);
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.None;
            g.CompositingQuality = CompositingQuality.HighSpeed;

            bool flip = trail.Facing < 0;
            var blob = Sprites.Get("hair00", false);
            var bangs = Sprites.Get(trail.BangsId, flip);
            if (blob != null && trail.CatTailNodes != null)
            {
                for (int i = 0; i < trail.CatTailNodes.Length; i++)
                {
                    float x = SnapPx(trail.CatTailNodes[i].X - trail.X + center);
                    float y = SnapPx(trail.CatTailNodes[i].Y - trail.Y + center);
                    const float tailSize = 3f;
                    DrawTintedSafe(g, blob, Color.Black, x - tailSize / 2f - 1, y - tailSize / 2f, tailSize, tailSize);
                    DrawTintedSafe(g, blob, Color.Black, x - tailSize / 2f + 1, y - tailSize / 2f, tailSize, tailSize);
                    DrawTintedSafe(g, blob, Color.Black, x - tailSize / 2f, y - tailSize / 2f - 1, tailSize, tailSize);
                    DrawTintedSafe(g, blob, Color.Black, x - tailSize / 2f, y - tailSize / 2f + 1, tailSize, tailSize);
                }
                // Color is a second pass so adjacent nodes do not repaint one
                // another with outline black and turn the tail into a dark chain.
                for (int i = 0; i < trail.CatTailNodes.Length; i++)
                {
                    float x = SnapPx(trail.CatTailNodes[i].X - trail.X + center);
                    float y = SnapPx(trail.CatTailNodes[i].Y - trail.Y + center);
                    DrawTintedSafe(g, blob, trail.HairColor, x - 1.5f, y - 1.5f, 3f, 3f);
                }
            }
            if (blob != null && bangs != null)
            {
                // Hair.Render includes its four-direction black outline. The max-blend
                // mask pass includes that outline in the final silhouette as well.
                for (int i = 0; i < trail.HairCount; i++)
                {
                    float scale = HairSegmentScale(i, trail.HairCount);
                    float pieceW = SnapEven(10f * scale * Math.Abs(trail.ScaleX));
                    float pieceH = SnapEven(10f * scale);
                    var tex = i == 0 ? bangs : blob;
                    float x = SnapPx(trail.HairNodes[i].X - trail.X + center) - pieceW / 2f;
                    float y = SnapPx(trail.HairNodes[i].Y - trail.Y + center) - pieceH / 2f;
                    DrawTintedSafe(g, tex, Color.Black, x - 1, y, pieceW, pieceH);
                    DrawTintedSafe(g, tex, Color.Black, x + 1, y, pieceW, pieceH);
                    DrawTintedSafe(g, tex, Color.Black, x, y - 1, pieceW, pieceH);
                    DrawTintedSafe(g, tex, Color.Black, x, y + 1, pieceW, pieceH);
                }
                for (int i = trail.HairCount - 1; i >= 0; i--)
                {
                    float scale = HairSegmentScale(i, trail.HairCount);
                    float pieceW = SnapEven(10f * scale * Math.Abs(trail.ScaleX));
                    float pieceH = SnapEven(10f * scale);
                    var tex = i == 0 ? bangs : blob;
                    DrawTintedSafe(g, tex, trail.HairColor,
                        SnapPx(trail.HairNodes[i].X - trail.X + center) - pieceW / 2f,
                        SnapPx(trail.HairNodes[i].Y - trail.Y + center) - pieceH / 2f,
                        pieceW, pieceH);
                }
            }

            var body = Sprites.Get(trail.FrameId, flip);
            if (body != null)
            {
                g.DrawImage(body,
                    SnapPx(center - 16f * trail.ScaleX),
                    SnapPx(center - 32f * trail.ScaleY),
                    SnapPx(32f * trail.ScaleX), SnapPx(32f * trail.ScaleY));
            }
            g.Dispose();
            // Store the final tinted stamp. Direct2D can now draw this immutable 64x64
            // bitmap directly at its world coordinate with only an opacity change.
            var tinted = new Bitmap(sizePx, sizePx, PixelFormat.Format32bppPArgb);
            using (var tintGraphics = Graphics.FromImage(tinted))
            {
                tintGraphics.Clear(Color.Transparent);
                tintGraphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                tintGraphics.PixelOffsetMode = PixelOffsetMode.Half;
                tintGraphics.SmoothingMode = SmoothingMode.None;
                Sprites.DrawSilhouette(tintGraphics, mask, trail.Tint, 0, 0, sizePx, sizePx);
            }
            mask.Dispose();
            return tinted;
        }

        // Celeste Player.Update + SpeedRing：Super/Hyper/Wavedash 起跳后，在速度保持
        // 140+ 的前 0.5 秒内每 0.15 秒生成一个环；每个环以 3/s 展开并以 10px/s 前移。
        void UpdateWavedashWaves(float dt)
        {
            // Existing SpeedRing entities update before Player can add another one;
            // newly emitted rings therefore render once at lerp=0 and origin position.
            for (int i = waveRings.Count - 1; i >= 0; i--)
            {
                var ring = waveRings[i];
                ring.Progress += 3f * dt;
                ring.X += (float)Math.Cos(ring.Angle) * 10f * dt;
                ring.Y += (float)Math.Sin(ring.Angle) * 10f * dt;
                if (ring.Progress >= 1f) waveRings.RemoveAt(i);
                else waveRings[i] = ring;
            }

            // DashBegin explicitly sets vanilla's `launched` flag false. This is
            // particularly important for multi-ultras: each new diagonal dash ends
            // the preceding jump's speed-ring sequence.
            if (player.DashSequenceCount != observedRingDashSequenceCount)
            {
                observedRingDashSequenceCount = player.DashSequenceCount;
                speedRingLaunchActive = false;
                speedRingLaunchTimer = 0f;
                nextSpeedRingTime = 0.15f;
            }
            bool launchStartedThisFrame = false;
            if (player.LaunchCount != observedLaunchCount)
            {
                observedLaunchCount = player.LaunchCount;
                // SuperJump only sets vanilla's `launched` flag. If a launch is
                // already active it does not reset launchedTimer, which prevents
                // chained hypers/ultras from restarting and overproducing rings.
                if (!speedRingLaunchActive)
                {
                    speedRingLaunchActive = true;
                    speedRingLaunchTimer = 0f;
                    nextSpeedRingTime = 0.15f;
                    launchStartedThisFrame = true;
                }
            }

            // Player checks `launched` before its StateMachine update. A SuperJump
            // created during that update therefore starts aging on the next frame.
            if (speedRingLaunchActive && !launchStartedThisFrame)
            {
                float speedSq = player.Speed.X * player.Speed.X + player.Speed.Y * player.Speed.Y;
                if (speedSq < 19600f)
                {
                    speedRingLaunchActive = false;
                    speedRingLaunchTimer = 0f;
                }
                else
                {
                    speedRingLaunchTimer += dt;
                    if (speedRingLaunchTimer >= 0.5f)
                    {
                        speedRingLaunchActive = false;
                        speedRingLaunchTimer = 0f;
                    }
                    else
                    {
                        while (speedRingLaunchTimer >= nextSpeedRingTime)
                        {
                            waveRings.Add(new WaveRing
                            {
                                X = player.Pos.X,
                                Y = player.Pos.Y - 5.5f,
                                Angle = (float)Math.Atan2(player.Speed.Y, player.Speed.X)
                            });
                            nextSpeedRingTime += 0.15f;
                        }
                    }
                }
            }

        }

        // 粒子发射（参数移植自 Celeste Player.cs 的 Dust.Burst 调用）
        void EmitPlayerParticles(float dt, int wasState)
        {
            float up = (float)-Math.PI / 2f;

            if (player.WallJumpEffectCount != observedWallJumpEffectCount)
            {
                observedWallJumpEffectCount = player.WallJumpEffectCount;
                int dir = player.LastWallJumpDirection;
                float angle = dir < 0 ? (float)(Math.PI * -3.0 / 4.0) : (float)(-Math.PI / 4.0);
                EmitDustBurst(player.Pos.X - dir * 2f, player.Pos.Y - 5.5f, angle, 4);
            }

            if (player.JumpEffectCount != observedJumpEffectCount)
            {
                observedJumpEffectCount = player.JumpEffectCount;
                EmitDustBurst(player.Pos.X, player.Pos.Y, up, 4);
            }

            if (player.LandingEffectCount != observedLandingEffectCount)
            {
                observedLandingEffectCount = player.LandingEffectCount;
                EmitDustBurst(player.Pos.X, player.Pos.Y, up, 8);
            }

            if (player.WallSlideDustActive)
            {
                int dir = player.WallSlideDirection;
                EmitDustBurst(player.Pos.X + dir * 5f, player.Pos.Y - 1.5f, up, 1);
            }

            // Dash-attacking against the held direction emits skid dust every
            // 0.02s from orig_UpdateSprite. Ordinary running has no dust burst.
            if (player.IsDashAttacking && player.AnimId == "skid")
            {
                skidDustTimer += dt;
                while (skidDustTimer >= 0.02f)
                {
                    skidDustTimer -= 0.02f;
                    EmitDustBurst(player.Pos.X, player.Pos.Y, up, 1);
                }
            }
            else skidDustTimer = 0f;

            // 冲刺
            if (player.State == Player.StDash)
            {
                float dashAngle = (float)Math.Atan2(player.DashDir.Y, player.DashDir.X);
                if (wasState != Player.StDash)
                {
                    dashParticleTimer = 0f;
                }
                dashParticleTimer += dt;
                // 原作 DashUpdate：运动中每 0.02 秒发射一个 P_DashA/P_DashB。
                while (dashParticleTimer >= 0.02f &&
                       (player.Speed.X != 0f || player.Speed.Y != 0f))
                {
                    dashParticleTimer -= 0.02f;
                    float px = player.Pos.X + (float)(effectRng.NextDouble() * 4.0 - 2.0);
                    float py = player.Pos.Y - 5.5f + (float)(effectRng.NextDouble() * 4.0 - 2.0);
                    particles.Emit(player.LastDashWasTwo ? dashRed : dashBlue,
                        px, py, dashAngle, (float)Math.PI / 3f, 1);
                }
            }
            else dashParticleTimer = 0f;
        }

        void EmitAnimationParticles()
        {
            if (animator.CurrentId == observedParticleAnimId && animator.Frame == observedParticleAnimFrame)
                return;
            observedParticleAnimId = animator.CurrentId;
            observedParticleAnimFrame = animator.Frame;
            // Player.OnFrameChange: pushing emits foreground dust on frames 8 and 15.
            if (animator.CurrentId == "push" && (animator.Frame == 8 || animator.Frame == 15))
            {
                float dx = -player.Facing;
                float angle = (float)Math.Atan2(-0.5f, dx);
                EmitDustBurst(player.Pos.X - player.Facing * 5f, player.Pos.Y - 1f,
                    angle, 1, 0f);
            }
        }

        void EmitDustBurst(float x, float y, float direction, int count, float positionRange = 4f)
        {
            float perpendicular = direction - (float)Math.PI / 2f;
            float rangeX = Math.Abs((float)Math.Cos(perpendicular) * positionRange);
            float rangeY = Math.Abs((float)Math.Sin(perpendicular) * positionRange);
            for (int i = 0; i < count; i++)
            {
                float px = x + ((float)effectRng.NextDouble() * 2f - 1f) * rangeX;
                float py = y + ((float)effectRng.NextDouble() * 2f - 1f) * rangeY;
                particles.Emit(dust, px, py, direction, 0.5f, 1);
            }
        }

        PetInput SampleInput()
        {
            var input = new PetInput();
            // GetAsyncKeyState is global. By default it is gated to this pet's
            // focus; the explicit menu opt-in permits reading it while unfocused.
            // No hook is installed and keys are never swallowed from other apps.
            if (!InputEnabled || dragging || (!InputWhenUnfocused && Win32.GetForegroundWindow() != Handle))
            {
                prevJump = prevDash = prevCrouchDash = false;
                return input;
            }

            bool left = bindings.IsDown(PetAction.Left);
            bool right = bindings.IsDown(PetAction.Right);
            bool up = bindings.IsDown(PetAction.Up);
            bool down = bindings.IsDown(PetAction.Down);
            bool jump = bindings.IsDown(PetAction.Jump);
            bool dash = bindings.IsDown(PetAction.Dash);
            bool grab = bindings.IsDown(PetAction.Grab);
            bool crouchDash = bindings.IsDown(PetAction.CrouchDash);

            input.MoveX = (right ? 1 : 0) - (left ? 1 : 0);
            input.MoveY = (down ? 1 : 0) - (up ? 1 : 0);
            input.JumpHeld = jump;
            input.GrabHeld = grab;

            if (jump && !prevJump) player.BufferJump();
            // Crouch Dash wins if both actions are pressed on the same frame, as it explicitly
            // requests the crouched dash path used for demos/hypers in Celeste.
            if (crouchDash && !prevCrouchDash) player.BufferDash(crouchDash: true);
            else if (dash && !prevDash) player.BufferDash();
            prevJump = jump;
            prevDash = dash;
            prevCrouchDash = crouchDash;

            input.JumpPressed = player.HasJumpBuffer;
            input.DashPressed = player.HasDashBuffer;
            return input;
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
            // Treat the exposed perimeter of the monitor union as solid.  Each edge
            // extends outward, then other monitor rectangles are subtracted from it:
            // a shared seam stays open while offset/non-overlapping portions are walls.
            int virtualLeft = int.MaxValue, virtualRight = int.MinValue;
            var screenRects = new List<Win32.RECT>();
            foreach (var screen in Screen.AllScreens)
            {
                var bounds = screen.Bounds;
                screenRects.Add(new Win32.RECT
                {
                    Left = bounds.Left, Top = bounds.Top,
                    Right = bounds.Right, Bottom = bounds.Bottom
                });
                virtualLeft = Math.Min(virtualLeft, bounds.Left);
                virtualRight = Math.Max(virtualRight, bounds.Right);
            }
            int edgeDepth = Math.Max(64, (int)Math.Ceiling(400f * s));
            foreach (var r in screenRects)
            {
                var outsideEdges = new[]
                {
                    new Win32.RECT { Left = r.Left, Top = r.Top - edgeDepth, Right = r.Right, Bottom = r.Top },
                    new Win32.RECT { Left = r.Left, Top = r.Bottom, Right = r.Right, Bottom = r.Bottom + edgeDepth },
                    new Win32.RECT { Left = r.Left - edgeDepth, Top = r.Top, Right = r.Left, Bottom = r.Bottom },
                    new Win32.RECT { Left = r.Right, Top = r.Top, Right = r.Right + edgeDepth, Bottom = r.Bottom }
                };
                foreach (var edge in outsideEdges)
                    foreach (var p in SubtractRects(edge, screenRects))
                        solids.Add(new Solid
                        {
                            Id = FloorId,
                            L = p.Left / s, T = p.Top / s,
                            R = p.Right / s, B = p.Bottom / s
                        });
            }

            // Screen 返回的边界在 PerMonitorV2 下与 DWM 一样都是物理像素。
            // 从实际显示器并集计算左右极值，避免 SystemInformation 的 DPI 虚拟化。
            if (virtualLeft != int.MaxValue)
            {
                player.MinX = virtualLeft / s;
                player.MaxX = virtualRight / s;
            }

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
            if (small == null)
                small = new Bitmap(CanvasW, CanvasH, PixelFormat.Format32bppPArgb);

            // Calculate and quantize the camera once. Previously drawing and window
            // presentation recomputed it independently, then rounded in different
            // coordinate spaces; moving snapshots visibly oscillated by a pixel.
            float camX = ComputeCameraX();
            float camY = player.Pos.Y - AnchorY;

            // 1x 游戏像素缓冲：整数坐标直接落像素，杜绝亚像素偏移；之后整数倍最近邻放大
            using (var g = Graphics.FromImage(small))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;
                g.CompositingQuality = CompositingQuality.HighSpeed;

                float bodyAnchorX = player.Pos.X - camX;
                float bodyAnchorY = player.Pos.Y - camY;

                DrawWavedashWaves(g, camX, camY);
                DrawGliders(g, camX, camY);

                // 头发画在身体后面（先画头发，再画身体覆盖）。
                // wakeUp 帧自带完整头发（蜷着睡觉），不再叠加模拟头发
                if (animator.CurrentId != "wakeUp")
                {
                    DrawCatTail(g, camX, camY);
                    DrawHair(g, camX, camY);
                }
                DrawBody(g, bodyAnchorX, bodyAnchorY);
                DrawSweat(g, bodyAnchorX, bodyAnchorY);

                if (ParticlesEnabled) particles.Draw(g, camX, camY);
                // SlashFx 与 TrailManager 是核心冲刺表现，不受粒子开关控制。
                DrawSlash(g, camX, camY);
                DrawSpeedometer(g, camX, camY);
                DrawHitboxes(g, camX, camY);
            }

            int left = (int)Math.Round(camX * s);
            int top = (int)Math.Round(camY * s);
            int trailCount = Math.Min(dashTrails.Count, trailStamps.Length);
            for (int i = 0; i < trailCount; i++)
            {
                var trail = dashTrails[i];
                float remain = 1f - trail.Age;
                float opacity = 0.75f * remain * remain * remain;
                trailStamps[i] = new TrailStamp(trail.Mask, trail.X, trail.Y, opacity);
            }
            presenter.Present(small, left, top, trailStamps, trailCount);

            // Only this tiny invisible input HWND moves. Rendering belongs to the fixed
            // click-through composition host, so window movement cannot shake pixels.
            int inputLeft = (int)Math.Round(player.Pos.X * s) - 12 * s;
            int inputTop = (int)Math.Round(player.Pos.Y * s) - 30 * s;
            Win32.SetWindowPos(Handle, IntPtr.Zero, inputLeft, inputTop,
                24 * s, 33 * s, Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
            // 每 5 秒记录一次位置 + 速度 + 状态
            if ((++renderFrameCount % 300) == 0)
                PetWindow.Log("frame " + renderFrameCount + " pos=" + player.Pos.X.ToString("F1") + "," + player.Pos.Y.ToString("F1") +
                    " sp=" + player.Speed.X.ToString("F0") + "," + player.Speed.Y.ToString("F0") +
                    " st=" + player.State + " duck=" + (player.Ducking ? 1 : 0) + " anim=" + player.AnimId);
        }

        float ComputeCameraX()
        {
            // The camera never chases effects. The fixed wide footprint contains them,
            // so afterimages cannot make the player/window oscillate at high speed.
            return player.Pos.X - AnchorX;
        }

        // 原作 SlashFx：4 帧 × 0.1s，出生于玩家 Center，以冲刺方向 8px/s 移动。
        void DrawSlash(Graphics g, float camX, float camY)
        {
            if (!slash.Active) return;
            int frame = Math.Min(3, (int)(slash.Age / 0.1f));
            var tex = Sprites.Get("slash0" + frame, false);
            if (tex == null) return;
            var state = g.Save();
            g.TranslateTransform(SnapPx(slash.X - camX), SnapPx(slash.Y - camY));
            // SlashFx deliberately leaves the source orientation unchanged for exactly PI.
            if (Math.Abs(slash.Angle - (float)Math.PI) > 0.01f)
                g.RotateTransform(slash.Angle * 180f / (float)Math.PI);
            g.DrawImage(tex, -12, -4, 24, 8);
            g.Restore(state);
        }

        void DrawGliders(Graphics g, float camX, float camY)
        {
            foreach (Glider glider in gliders)
            {
                Bitmap frame = Sprites.Get(glider.FrameId, false);
                if (frame == null) continue;
                var state = g.Save();
                g.TranslateTransform(SnapPx(glider.Pos.X - camX), SnapPx(glider.Pos.Y - camY));
                g.RotateTransform(glider.Rotation * 180f / (float)Math.PI);
                float w = 48f * glider.ScaleX, h = 48f * glider.ScaleY;
                float x = -w / 2f, y = -h / 2f;
                // Glider.Render calls DrawSimpleOutline before drawing the sprite.
                Sprites.DrawSilhouette(g, frame, Color.Black, x - 1f, y, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x + 1f, y, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x, y - 1f, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x, y + 1f, w, h);
                g.DrawImage(frame, x, y, w, h);
                g.Restore(state);
            }
        }

        void DrawBody(Graphics g, float anchorX, float anchorY)
        {
            // 身体（挤压拉伸锚定脚底中心），矩形吸附到整数游戏像素
            bool flip = player.Facing < 0;
            var frame = Sprites.Get(animator.CurrentFrameId, flip);
            if (frame != null)
            {
                float sx = player.SpriteScaleX, sy = player.SpriteScaleY;
                float x = SnapPx(anchorX - 16 * sx), y = SnapPx(anchorY - 32 * sy);
                float w = SnapPx(32 * sx), h = SnapPx(32 * sy);
                // 原作低体力表现：每 0.05 秒红/白闪烁身体。
                if (player.IsLowStamina && tiredFlash)
                    Sprites.DrawTinted(g, frame, Color.Red, x, y, w, h);
                else
                    g.DrawImage(frame, x, y, w, h);
            }
        }

        void DrawSweat(Graphics g, float anchorX, float anchorY)
        {
            if (introWakeUp) return;
            var sweat = Sprites.Get(sweatAnimator.CurrentFrameId, player.Facing < 0);
            if (sweat != null)
            {
                float sx = player.SpriteScaleX, sy = player.SpriteScaleY;
                float x = SnapPx(anchorX - 16 * sx), y = SnapPx(anchorY - 32 * sy);
                g.DrawImage(sweat, x, y, SnapPx(32 * sx), SnapPx(32 * sy));
            }
        }

        void DrawWavedashWaves(Graphics g, float camX, float camY)
        {
            foreach (var ring in waveRings)
            {
                float alpha = 0.6f * (1f - ring.Progress);
                int a = Math.Max(0, Math.Min(255, (int)Math.Round(alpha * 255f)));
                using var pen = new Pen(Color.FromArgb(a, Color.White), 1f);
                var points = new PointF[16];
                float maxRadius = 4f + (14f - 4f) * ring.Progress;
                float nx = (float)Math.Cos(ring.Angle), ny = (float)Math.Sin(ring.Angle);
                for (int i = 0; i < points.Length; i++)
                {
                    float radians = i * (float)Math.PI * 2f / points.Length;
                    float vx = (float)Math.Cos(radians), vy = (float)Math.Sin(radians);
                    float along = Math.Abs(vx * nx + vy * ny);
                    float radius = maxRadius * (1f - 0.5f * along);
                    points[i] = new PointF(
                        SnapPx(ring.X - camX + vx * radius),
                        SnapPx(ring.Y - camY + vy * radius));
                }
                g.DrawPolygon(pen, points);
            }
        }

        void DrawHair(Graphics g, float camX, float camY)
        {
            var hair = player.Hair;
            Color color = player.HairColor;
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
            if (catBangsEnabled) bangsId = "catbangs" + bangsId.Substring(bangsId.Length - 2);
            var bangs = Sprites.Get(bangsId, flip);
            if (blob == null || bangs == null) return;

            // 画布坐标（像素完美：原版渲染时 Nodes[0].Floor()，这里把每个节点吸附到整数游戏像素，
            // ×整数倍放大 = 整数物理像素，避免亚像素模糊）
            int hairCount = hair.ActiveCount;
            Span<PointF> pt = stackalloc PointF[PlayerHairSim.MaxCount];
            for (int i = 0; i < hairCount; i++)
                pt[i] = new PointF(
                    SnapPx(hair.Nodes[i].X - camX),
                    SnapPx(hair.Nodes[i].Y - camY));

            // 黑色描边（原作：±1px 四方向）
            for (int i = 0; i < hairCount; i++)
            {
                float sc = HairSegmentScale(i, hairCount);
                var tex = i == 0 ? bangs : blob;
                float w = SnapEven(10 * sc * Math.Abs(player.SpriteScaleX));
                float h = SnapEven(10 * sc);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2 - 1, pt[i].Y - h / 2, w, h);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2 + 1, pt[i].Y - h / 2, w, h);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2, pt[i].Y - h / 2 - 1, w, h);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2, pt[i].Y - h / 2 + 1, w, h);
            }
            // 本体（后画前面，刘海最后）
            for (int i = hairCount - 1; i >= 0; i--)
            {
                float sc = HairSegmentScale(i, hairCount);
                var tex = i == 0 ? bangs : blob;
                float w = SnapEven(10 * sc * Math.Abs(player.SpriteScaleX));
                float h = SnapEven(10 * sc);
                DrawTintedSafe(g, tex, color, pt[i].X - w / 2, pt[i].Y - h / 2, w, h);
            }
        }

        void DrawSpeedometer(Graphics g, float camX, float camY)
        {
            if (speedometerMode == 0 || picoDigits[0] == null || introWakeUp) return;
            double speed = speedometerMode switch
            {
                1 => Math.Abs(Math.Round(player.Speed.X)),
                2 => Math.Abs(Math.Round(player.Speed.Y)),
                _ => Math.Round(Math.Sqrt(player.Speed.X * player.Speed.X + player.Speed.Y * player.Speed.Y))
            };
            speedometerSamples.Enqueue((int)speed);
            while (speedometerSamples.Count > 10) speedometerSamples.Dequeue();
            string text = speedometerSamples.Max().ToString(CultureInfo.InvariantCulture);
            int totalWidth = text.Length * 4 - 1;
            int startX = (int)Math.Round(player.Pos.X - camX) - totalWidth / 2;
            int top = (int)Math.Round(player.Pos.Y - camY - 24f);
            for (int i = 0; i < text.Length; i++)
                DrawPicoDigit(g, text[i] - '0', startX + i * 4, top);
        }

        void DrawPicoDigit(Graphics g, int digit, int x, int y)
        {
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
                if (ox != 0 || oy != 0)
                    Sprites.DrawTinted(g, picoDigits[digit], Color.Black, x + ox, y + oy, 3, 5);
            g.DrawImage(picoDigits[digit], x, y, 3, 5);
        }

        void DrawHitboxes(Graphics g, float camX, float camY)
        {
            if (!hitboxesEnabled) return;
            using var solidBrush = new SolidBrush(Color.Red);
            foreach (var solid in player.Solids)
                DrawSolidHitbox(g, solid.L - camX, solid.T - camY,
                    solid.R - solid.L, solid.B - solid.T, solidBrush);
            float playerHeight = player.Ducking ? 6f : 11f;
            using var playerBrush = new SolidBrush(Color.Lime);
            DrawHollowRect(g, player.Pos.X - 4f - camX, player.Pos.Y - playerHeight - camY,
                8f, playerHeight, playerBrush);
        }

        static void DrawSolidHitbox(Graphics g, float x, float y, float width, float height, Brush brush)
        {
            int left = (int)Math.Round(x), top = (int)Math.Round(y);
            int w = Math.Max(1, (int)Math.Round(width));
            int h = Math.Max(1, (int)Math.Round(height));
            // A physical 8px window border is often only two game pixels thick.
            // Drawing both sides then looks like a solid 2px band instead of the
            // player's single-pixel debug stroke.
            if (h <= 2 && w > h) { g.FillRectangle(brush, left, top, w, 1); return; }
            if (w <= 2 && h > w) { g.FillRectangle(brush, left, top, 1, h); return; }
            DrawHollowRect(g, x, y, width, height, brush);
        }

        static void DrawHollowRect(Graphics g, float x, float y, float width, float height, Brush brush)
        {
            int left = (int)Math.Round(x), top = (int)Math.Round(y);
            int w = Math.Max(1, (int)Math.Round(width));
            int h = Math.Max(1, (int)Math.Round(height));
            g.FillRectangle(brush, left, top, w, 1);
            g.FillRectangle(brush, left, top + h - 1, w, 1);
            g.FillRectangle(brush, left, top, 1, h);
            g.FillRectangle(brush, left + w - 1, top, 1, h);
        }

        void DrawCatTail(Graphics g, float camX, float camY)
        {
            if (!catTailEnabled || !catTailStarted) return;
            var texture = Sprites.Get("hair00", false);
            if (texture == null) return;
            for (int i = 0; i < CatTailCount; i++)
            {
                float x = SnapPx(catTailNodes[i].X - camX);
                float y = SnapPx(catTailNodes[i].Y - camY);
                // Use the colored node's exact geometry for the four-direction
                // outline, just like PlayerHair.Render; only the ±1px offsets differ.
                const float tailSize = 3f;
                DrawTintedSafe(g, texture, Color.Black, x - tailSize / 2f - 1, y - tailSize / 2f, tailSize, tailSize);
                DrawTintedSafe(g, texture, Color.Black, x - tailSize / 2f + 1, y - tailSize / 2f, tailSize, tailSize);
                DrawTintedSafe(g, texture, Color.Black, x - tailSize / 2f, y - tailSize / 2f - 1, tailSize, tailSize);
                DrawTintedSafe(g, texture, Color.Black, x - tailSize / 2f, y - tailSize / 2f + 1, tailSize, tailSize);
            }
            for (int i = 0; i < CatTailCount; i++)
            {
                float x = SnapPx(catTailNodes[i].X - camX);
                float y = SnapPx(catTailNodes[i].Y - camY);
                DrawTintedSafe(g, texture, player.HairColor, x - 1.5f, y - 1.5f, 3f, 3f);
            }
        }

        static float HairSegmentScale(int i, int count)
            => 0.25f + (1f - (float)i / count) * 0.75f;

        // 像素完美：浮点游戏像素吸附到整数（偶数取整保证 w/2 也为整数，矩形边缘不落亚像素）
        static float SnapPx(float v) => (float)Math.Round(v);
        static float SnapEven(float v) => (float)(Math.Round(v / 2f) * 2f);

        static void DrawTintedSafe(Graphics g, Bitmap tex, Color c, float x, float y, float w, float h, float alpha = 1f)
            => Sprites.DrawTinted(g, tex, c, x, y, w, h, alpha);

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
                    // 正常情况下移除 WS_EX_NOACTIVATE 后鼠标按下已经会激活窗口；显式
                    // Activate 也覆盖某些分层窗口/窗口管理工具的特殊激活行为。
                    Activate();
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
        string T(string en, string zh) => english ? en : zh;

        void SaveSettings()
        {
            settings.Scale = pendingScale > 0 ? pendingScale : GameScale;
            settings.InputEnabled = InputEnabled;
            settings.InputWhenUnfocused = InputWhenUnfocused;
            settings.AlwaysOnTop = AlwaysOnTop;
            settings.ParticlesEnabled = ParticlesEnabled;
            settings.FreezeFramesEnabled = player.FreezeFramesEnabled;
            settings.InfiniteStamina = player.InfiniteStamina;
            settings.DashMode = player.DashMode;
            settings.Language = english ? "en" : "zh";
            settings.Skin = skinManager.Active?.Id ?? SkinManager.DefaultId;
            settings.CatTailEnabled = catTailEnabled;
            settings.CatBangsEnabled = catBangsEnabled;
            settings.CustomHairColorsEnabled = customHairColorsEnabled;
            settings.HairColor0 = RgbValue(customHairColors[0]);
            settings.HairColor1 = RgbValue(customHairColors[1]);
            settings.HairColor2 = RgbValue(customHairColors[2]);
            settings.SpeedometerMode = speedometerMode;
            settings.HitboxesEnabled = hitboxesEnabled;
            settings.Save();
        }

        void ChangeLanguage(bool useEnglish)
        {
            if (english == useEnglish) return;
            english = useEnglish;
            SaveSettings();
            // 菜单点击事件结束后重建，避免在 WinForms 正在派发事件时释放当前菜单。
            BeginInvoke(new Action(() =>
            {
                var old = trayMenu;
                trayMenu = BuildMenu();
                tray.ContextMenuStrip = trayMenu;
                tray.Text = T("Madeline", "玛德琳");
                old?.Dispose();
            }));
        }

        string ActionName(PetAction action)
        {
            return action switch
            {
                PetAction.Left => T("Left", "左"),
                PetAction.Right => T("Right", "右"),
                PetAction.Up => T("Up", "上"),
                PetAction.Down => T("Down", "下"),
                PetAction.Jump => T("Jump", "跳跃"),
                PetAction.Dash => T("Dash", "冲刺"),
                PetAction.Grab => T("Grab", "抓取"),
                PetAction.CrouchDash => T("Crouch Dash", "蹲冲"),
                _ => action.ToString()
            };
        }

        string KeyName(int virtualKey)
            => virtualKey == 0 ? T("Unbound", "未绑定") : ((Keys)virtualKey).ToString();

        void RefreshBindingItems(ToolStripMenuItem actionItem, PetAction action)
        {
            int[] values = bindings.Get(action);
            for (int i = 0; i < 3; i++)
                actionItem.DropDownItems[i].Text = (i + 1) + ": " + KeyName(values[i]);
        }

        ToolStripMenuItem BuildBindingsMenu()
        {
            var root = new ToolStripMenuItem(T("Key bindings", "按键绑定"));
            foreach (PetAction action in KeyBindings.Actions)
            {
                var actionItem = new ToolStripMenuItem(ActionName(action));
                int[] values = bindings.Get(action);
                for (int i = 0; i < 3; i++)
                {
                    int slot = i;
                    var slotItem = new ToolStripMenuItem((i + 1) + ": " + KeyName(values[i]));
                    slotItem.DropDownItems.Add(new ToolStripMenuItem(T("Change…", "更改…"), null, (_, __) =>
                    {
                        using var capture = new KeyCaptureDialog(
                            T("Bind " + ActionName(action), "绑定" + ActionName(action)),
                            T("Press a key. Backspace/Delete clears this slot; Esc cancels.",
                              "请按一个键。Backspace/Delete 清除此栏；Esc 取消。"));
                        if (capture.ShowDialog(this) == DialogResult.OK)
                        {
                            bindings.Set(action, slot, capture.CapturedKey);
                            RefreshBindingItems(actionItem, action);
                        }
                    }));
                    slotItem.DropDownItems.Add(new ToolStripMenuItem(T("Unbind", "解除绑定"), null, (_, __) =>
                    {
                        bindings.Set(action, slot, 0);
                        RefreshBindingItems(actionItem, action);
                    }));
                    actionItem.DropDownItems.Add(slotItem);
                }
                root.DropDownItems.Add(actionItem);
            }
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add(new ToolStripMenuItem(T("Reset defaults", "恢复默认"), null, (_, __) =>
            {
                bindings.ResetDefaults();
                // Rebuild so every open slot label reflects the reset values.
                BeginInvoke(new Action(() =>
                {
                    var old = trayMenu;
                    trayMenu = BuildMenu();
                    tray.ContextMenuStrip = trayMenu;
                    old?.Dispose();
                }));
            }));
            return root;
        }

        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var languageItem = new ToolStripMenuItem(T("Language", "语言"));
            languageItem.DropDownItems.Add(new ToolStripMenuItem("English", null, (_, __) => ChangeLanguage(true))
                { Checked = english });
            languageItem.DropDownItems.Add(new ToolStripMenuItem("中文", null, (_, __) => ChangeLanguage(false))
                { Checked = !english });
            menu.Items.Add(languageItem);

            var skinItem = new ToolStripMenuItem(T("Skin", "皮肤"));
            void AddSkinChoice(string id, string label)
            {
                var choice = new ToolStripMenuItem(label)
                {
                    Checked = (pendingSkinId ?? skinManager.Active?.Id ?? SkinManager.DefaultId)
                        .Equals(id, StringComparison.OrdinalIgnoreCase),
                    Tag = id
                };
                choice.Click += (_, __) =>
                {
                    pendingSkinId = id;
                    foreach (ToolStripItem raw in skinItem.DropDownItems)
                        if (raw is ToolStripMenuItem item && item.Tag is string)
                            item.Checked = ((string)item.Tag).Equals(id, StringComparison.OrdinalIgnoreCase);
                };
                skinItem.DropDownItems.Add(choice);
            }
            AddSkinChoice(SkinManager.DefaultId, T("Default Madeline", "默认玛德琳"));
            foreach (var skin in skinManager.Skins) AddSkinChoice(skin.Id, skin.DisplayName);
            skinItem.DropDownItems.Add(new ToolStripSeparator());
            skinItem.DropDownItems.Add(new ToolStripMenuItem(T("Refresh skins", "刷新皮肤"), null, (_, __) =>
            {
                // Re-scan archives and reload the selected skin as well.  Reloading
                // matters when an existing zip was replaced, not just when a new one
                // was added.  Rebuild after the click unwinds so WinForms is not asked
                // to dispose the menu currently dispatching this event.
                string activeId = skinManager.Active?.Id ?? SkinManager.DefaultId;
                skinManager.Discover();
                pendingSkinId = skinManager.Find(activeId)?.Id ?? SkinManager.DefaultId;
                Log("skins refreshed: " + skinManager.Skins.Count);
                BeginInvoke(new Action(() =>
                {
                    var old = trayMenu;
                    trayMenu = BuildMenu();
                    tray.ContextMenuStrip = trayMenu;
                    old?.Dispose();
                }));
            }));
            skinItem.DropDownItems.Add(new ToolStripMenuItem(T("Open skins folder", "打开皮肤文件夹"), null, (_, __) =>
            {
                string skinsDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skins");
                try
                {
                    System.IO.Directory.CreateDirectory(skinsDirectory);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = skinsDirectory,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, T("Could not open skins folder", "无法打开皮肤文件夹"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }));
            menu.Items.Add(skinItem);

            var cosmeticsItem = new ToolStripMenuItem(T("Cosmetics", "装饰"));
            var catTailItem = new ToolStripMenuItem(T("Cat tail", "猫尾")) { Checked = catTailEnabled };
            catTailItem.Click += (_, __) =>
            {
                catTailEnabled = !catTailEnabled;
                catTailItem.Checked = catTailEnabled;
                catTailStarted = false;
                SaveSettings();
            };
            cosmeticsItem.DropDownItems.Add(catTailItem);
            var catBangsItem = new ToolStripMenuItem(T("Cat bangs", "猫耳刘海")) { Checked = catBangsEnabled };
            catBangsItem.Click += (_, __) =>
            {
                catBangsEnabled = !catBangsEnabled;
                catBangsItem.Checked = catBangsEnabled;
                SaveSettings();
            };
            cosmeticsItem.DropDownItems.Add(catBangsItem);
            menu.Items.Add(cosmeticsItem);

            var hairColorsItem = new ToolStripMenuItem(T("Hair colors", "头发颜色"));
            var hairColorsEnabledItem = new ToolStripMenuItem(T("Use custom colors", "使用自定义颜色"))
                { Checked = customHairColorsEnabled };
            hairColorsEnabledItem.Click += (_, __) =>
            {
                customHairColorsEnabled = !customHairColorsEnabled;
                hairColorsEnabledItem.Checked = customHairColorsEnabled;
                SaveSettings();
            };
            hairColorsItem.DropDownItems.Add(hairColorsEnabledItem);
            hairColorsItem.DropDownItems.Add(new ToolStripSeparator());
            string[] colorNames = { T("No dashes", "无冲刺"), T("One dash", "一次冲刺"), T("Two dashes", "两次冲刺") };
            var colorItems = new ToolStripMenuItem[3];
            void RefreshColorLabels()
            {
                for (int i = 0; i < colorItems.Length; i++)
                    colorItems[i].Text = colorNames[i] + ": #" + RgbValue(customHairColors[i]).ToString("X6");
            }
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                colorItems[i] = new ToolStripMenuItem();
                colorItems[i].Click += (_, __) =>
                {
                    using var dialog = new ColorDialog
                    {
                        Color = customHairColors[index],
                        FullOpen = true,
                        AnyColor = true
                    };
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        customHairColors[index] = dialog.Color;
                        RefreshColorLabels();
                        SaveSettings();
                    }
                };
                hairColorsItem.DropDownItems.Add(colorItems[i]);
            }
            RefreshColorLabels();
            hairColorsItem.DropDownItems.Add(new ToolStripSeparator());
            hairColorsItem.DropDownItems.Add(new ToolStripMenuItem(T("Reset Celeste colors", "恢复原版颜色"), null, (_, __) =>
            {
                customHairColors[0] = Player.UsedHairColor;
                customHairColors[1] = Player.NormalHairColor;
                customHairColors[2] = Player.TwoDashesHairColor;
                RefreshColorLabels();
                SaveSettings();
            }));
            menu.Items.Add(hairColorsItem);

            var scaleItem = new ToolStripMenuItem(T("Scale (nearest-neighbor)", "缩放（等比放大）"));
            foreach (var v in new[] { 2, 3, 4, 5, 6, 8 })
            {
                var item = new ToolStripMenuItem(v + "x") { Tag = v, Checked = v == GameScale };
                item.Click += (_, __) =>
                {
                    pendingScale = v;
                    SaveSettings();
                    foreach (ToolStripMenuItem s in scaleItem.DropDownItems) s.Checked = (int)s.Tag == v;
                };
                scaleItem.DropDownItems.Add(item);
            }
            menu.Items.Add(scaleItem);

            ToolStripMenuItem inputItem = null;
            inputItem = new ToolStripMenuItem(T("Keyboard controls", "键盘控制"), null, (_, __) =>
            { InputEnabled = !InputEnabled; inputItem.Checked = InputEnabled; SaveSettings(); })
            { Checked = InputEnabled };
            menu.Items.Add(inputItem);
            ToolStripMenuItem unfocusedInputItem = null;
            unfocusedInputItem = new ToolStripMenuItem(T("Respond while unfocused", "失焦时也响应输入"), null, (_, __) =>
            {
                InputWhenUnfocused = !InputWhenUnfocused;
                unfocusedInputItem.Checked = InputWhenUnfocused;
                SaveSettings();
            }) { Checked = InputWhenUnfocused };
            menu.Items.Add(unfocusedInputItem);
            menu.Items.Add(BuildBindingsMenu());

            ToolStripMenuItem topItem = null;
            topItem = new ToolStripMenuItem(T("Always on top", "总是置顶"), null, (_, __) =>
            {
                AlwaysOnTop = !AlwaysOnTop;
                topItem.Checked = AlwaysOnTop;
                SaveSettings();
                Win32.SetWindowPos(Handle, AlwaysOnTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                    0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
                if (compositionHost != null)
                    Win32.SetWindowPos(compositionHost.Handle, AlwaysOnTop ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST,
                        0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            })
            { Checked = AlwaysOnTop };
            menu.Items.Add(topItem);

            var particleItem = new ToolStripMenuItem(T("Particle effects", "粒子特效"), null, (sender, __) =>
            {
                ParticlesEnabled = !ParticlesEnabled;
                ((ToolStripMenuItem)sender).Checked = ParticlesEnabled;
                SaveSettings();
            })
            { Checked = ParticlesEnabled };
            menu.Items.Add(particleItem);

            var freezeItem = new ToolStripMenuItem(T("Freeze frames", "冻结帧"), null, (sender, __) =>
            {
                player.SetFreezeFramesEnabled(!player.FreezeFramesEnabled);
                ((ToolStripMenuItem)sender).Checked = player.FreezeFramesEnabled;
                SaveSettings();
            })
            { Checked = player.FreezeFramesEnabled };
            menu.Items.Add(freezeItem);

            var overlaysItem = new ToolStripMenuItem(T("Extra overlays", "额外叠加层"));
            var speedometerItem = new ToolStripMenuItem(T("Speedometer", "速度计"));
            foreach (var option in new[]
            {
                new KeyValuePair<int, string>(0, T("Off", "关闭")),
                new KeyValuePair<int, string>(1, T("Horizontal", "水平")),
                new KeyValuePair<int, string>(2, T("Vertical", "垂直")),
                new KeyValuePair<int, string>(3, T("Both", "合速度"))
            })
            {
                int mode = option.Key;
                var choice = new ToolStripMenuItem(option.Value)
                {
                    Checked = speedometerMode == mode,
                    Tag = mode
                };
                choice.Click += (_, __) =>
                {
                    speedometerMode = mode;
                    SaveSettings();
                    foreach (ToolStripMenuItem item in speedometerItem.DropDownItems)
                        item.Checked = (int)item.Tag == mode;
                };
                speedometerItem.DropDownItems.Add(choice);
            }
            overlaysItem.DropDownItems.Add(speedometerItem);
            var hitboxesItem = new ToolStripMenuItem(T("Hitboxes", "碰撞箱")) { Checked = hitboxesEnabled };
            hitboxesItem.Click += (_, __) =>
            {
                hitboxesEnabled = !hitboxesEnabled;
                hitboxesItem.Checked = hitboxesEnabled;
                SaveSettings();
            };
            overlaysItem.DropDownItems.Add(hitboxesItem);
            menu.Items.Add(overlaysItem);

            var staminaItem = new ToolStripMenuItem(T("Infinite stamina", "无限体力"), null, (sender, __) =>
            {
                player.InfiniteStamina = !player.InfiniteStamina;
                ((ToolStripMenuItem)sender).Checked = player.InfiniteStamina;
                SaveSettings();
            }) { Checked = player.InfiniteStamina };
            menu.Items.Add(staminaItem);

            var dashItem = new ToolStripMenuItem(T("Dash count", "冲刺次数"));
            foreach (var option in new[]
            {
                new KeyValuePair<int, string>(0, "0"),
                new KeyValuePair<int, string>(1, "1"),
                new KeyValuePair<int, string>(2, "2"),
                new KeyValuePair<int, string>(-1, "∞")
            })
            {
                int mode = option.Key;
                var choice = new ToolStripMenuItem(option.Value)
                {
                    Checked = player.DashMode == mode,
                    Tag = mode
                };
                choice.Click += (_, __) =>
                {
                    player.SetDashMode(mode);
                    SaveSettings();
                    foreach (ToolStripMenuItem item in dashItem.DropDownItems)
                        item.Checked = (int)item.Tag == mode;
                };
                dashItem.DropDownItems.Add(choice);
            }
            menu.Items.Add(dashItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(T("Replay wake-up animation", "回放醒来动画"), null, (_, __) =>
            {
                introWakeUp = true;
                animator.Play("wakeUp", true);
            }));
            menu.Items.Add(new ToolStripMenuItem(T("Reset position", "重置位置"), null, (_, __) => ResetPosition()));
            menu.Items.Add(new ToolStripMenuItem(T("Spawn jellyfish", "生成水母"), null, (_, __) =>
                Interlocked.Increment(ref pendingGliderSpawns)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(T("Controls", "操作说明"), null, (_, __) =>
                MessageBox.Show(
                    T(
                        "Click Madeline first to focus her, or enable Respond while unfocused. Keys can be changed under Key bindings (three slots per action). Crouch Dash is separate and unbound by default.\n\nMove: Arrow keys / A D\nJump: C (coyote time + variable height)\nDash: X (8 directions; refills on landing)\nClimb / carry: Hold Grab against a wall or near a jellyfish\n\nTech:\n· Super: press Jump during a grounded dash\n· Hyper: down-diagonal grounded dash, then Jump\n· Wavedash/Ultra: down-diagonal air dash, then Jump on landing\n· Cornerboost: Grab + wall-jump within 0.06s after hitting a wall\n· Left-drag Madeline to throw her\n\nWindows are hollow platforms: stand on borders or climb their sides.",
                        "先点击玛德琳取得键盘焦点，或启用“失焦时也响应输入”。可在“按键绑定”中修改按键（每项三栏）。蹲冲为独立按键，默认未绑定。\n\n移动：方向键 / AD\n跳跃：C（土狼时间+可变跳高）\n冲刺：X（8方向，着地恢复）\n攀爬/携带：对准墙或靠近水母时按住抓取\n\n技巧：\n· Super：地面冲刺中按跳跃\n· Hyper：地面斜下冲后按跳跃\n· Wavedash/Ultra：空中斜下冲，落地时按跳跃\n· Cornerboost：冲刺撞墙后 0.06s 内抓墙+蹬墙跳\n· 左键拖着玛德琳甩出去\n\n窗口是空心平台：可站边框、爬侧边。"),
                    T("Desk Madeline", "玛德琳桌宠"), MessageBoxButtons.OK, MessageBoxIcon.Information)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(T("Exit", "退出"), null, (_, __) => ExitApp()));
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
            running = false;
            tray.Visible = false;
            // Close after the ToolStrip click unwinds. Application.Exit enumerates all
            // open forms; disposing the composition host during that enumeration caused
            // "Collection was modified" on .NET 8.
            BeginInvoke(new Action(Close));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            running = false;
            SaveSettings();
            // 等游戏循环线程结束当前帧再释放资源，避免渲染线程还在使用 GPU 对象
            if (loopThread != null && loopThread != Thread.CurrentThread)
                loopThread.Join(1500);
            tray.Visible = false;
            tray.Dispose();
            // 释放托盘图标 HICON 与 Direct3D / DirectComposition 资源
            if (trayIconHandle != IntPtr.Zero) { Win32.DestroyIcon(trayIconHandle); trayIconHandle = IntPtr.Zero; }
            presenter?.Dispose();
            foreach (var trail in dashTrails) trail.Mask?.Dispose();
            dashTrails.Clear();
            foreach (var digit in picoDigits) digit?.Dispose();
            compositionHost?.Close();
            compositionHost?.Dispose();
            base.OnFormClosing(e);
        }
    }

    /// <summary>
    /// Stationary, click-through virtual-desktop host for the DirectComposition tree.
    /// Input is handled by PetWindow's small invisible body-sized HWND instead.
    /// </summary>
    sealed class CompositionHost : Form
    {
        readonly bool topmost;

        public CompositionHost(Rectangle bounds, bool topmost)
        {
            this.topmost = topmost;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            Text = "Desk Madeline Renderer";
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOPMOST = 0x00000008;
                var cp = base.CreateParams;
                cp.ExStyle |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TRANSPARENT |
                              Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED;
                if (topmost) cp.ExStyle |= WS_EX_TOPMOST;
                return cp;
            }
        }
    }

    /// <summary>提升定时器精度（winmm）。</summary>
    static class TimePeriod
    {
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint uMilliseconds);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint uMilliseconds);
        public static void Begin(uint ms) => timeBeginPeriod(ms);
        public static void End(uint ms) => timeEndPeriod(ms);
    }

    /// <summary>Small modal key-capture window used by the tray binding editor.</summary>
    sealed class KeyCaptureDialog : Form
    {
        public int CapturedKey { get; private set; }

        public KeyCaptureDialog(string title, string instructions)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            KeyPreview = true;
            ClientSize = new Size(430, 100);
            Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = instructions,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = SystemFonts.MessageBoxFont,
                Padding = new Padding(16)
            });
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
            else
            {
                CapturedKey = (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
                    ? 0
                    : (int)e.KeyCode;
                DialogResult = DialogResult.OK;
            }
            Close();
        }
    }
}
