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
    /// Desktop pet main window: layered transparent window + 60 FPS game loop + window-platform polling + tray menu.
    /// All desktop coordinates are physical pixels (process is PerMonitorV2); physics runs in game-pixel space (1 game px = S physical px).
    /// </summary>
    public class PetWindow : Form
    {
        // ===== Tunable parameters =====
        public int GameScale = 6;               // integer nearest-neighbor scale (vanilla 1080p is 6x)
        public bool InputEnabled = true;
        public bool PadInputEnabled = true;
        public bool InputWhenUnfocused;
        public bool AlwaysOnTop = true;
        public static PetWindow Instance;

        // Keep a symmetric world-space envelope around the player. Particles stay
        // where they were emitted while the camera follows Madeline; the former
        // 160px-tall strip clipped dash/Elytra particles after fast vertical moves.
        // 1024px retains the same one-second ultra envelope on both axes.
        const int CanvasW = 1024, CanvasH = 1024;
        const float AnchorX = 512, AnchorY = 512; // foot anchor (inside the canvas)
        const double FixedDt = 1.0 / 60.0;
        static readonly IntPtr FloorId = new IntPtr(-991);
        const int WindowBorderPx = 8;           // hollow window-border thickness (physical pixels)
        const float EdgeWrapMargin = 12f;       // let the sprite clear the display before wrapping

        readonly Player player = new Player();
        readonly KeyBindings bindings;
        readonly PadBindings padBindings;
        readonly PetSettings settings;
        readonly SoundEffects soundEffects;
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
        int pendingTheoSpawns;
        int pendingSeekerSpawns;
        int pendingRemoveAllEntities;
        readonly Queue<Glider> pendingGliderRemovals = new Queue<Glider>();
        readonly Queue<TheoCrystal> pendingTheoRemovals = new Queue<TheoCrystal>();
        readonly Queue<Seeker> pendingSeekerRemovals = new Queue<Seeker>();
        bool introWakeUp = true;   // On startup play the wake-up animation (wakeUp 00-14), then switch to idle

        // Rendering
        Bitmap small;           // 1x game-pixel buffer (CanvasW x CanvasH); draw at integer coords then integer upscale
        readonly TrailStamp[] trailStamps = new TrailStamp[1024];
        D3DPresenter presenter;
        CompositionHost compositionHost;
        readonly Rectangle virtualDesktop;
        int renderFrameCount;

        // Platforms
        readonly Dictionary<IntPtr, Win32.RECT> lastRects = new Dictionary<IntPtr, Win32.RECT>();
        int pollCounter;
        IntPtr rideAlongId;             // window the ride-along remainder belongs to
        PointF rideAlongRemainder;      // sub-pixel part of a moving window's offset

        // Input state
        bool prevJump, prevDash, prevCrouchDash;

        // Dragging
        volatile bool dragging;
        Point dragGrabOffset;      // physical pixels: grab point relative to feet
        PointF cursorVel;          // physical pixels / second
        Point lastCursor;
        IntPtr trayIconHandle;     // tray icon HICON (must DestroyIcon explicitly)
        bool restartAfterExit;     // start a fresh copy once this one has let go of everything

        // Particles / effects
        readonly ParticleSystem particles = new ParticleSystem();
        readonly ParticleSystem seekerParticles = new ParticleSystem();
        readonly Dictionary<int, Bitmap> seekerParticleBitmaps = new Dictionary<int, Bitmap>();
        Bitmap gliderDebugStamp, theoDebugStamp;
        readonly Bitmap[] seekerDebugStamps = new Bitmap[3];
        readonly List<WaveRing> waveRings = new List<WaveRing>();
        readonly Random effectRng = new Random();
        PType dust, dashBlue, dashRed, dashBadeline, elytraDeploy;
        PType seekerAttack, seekerHitWall, seekerStomp, seekerRegen, theoImpact;
        bool ParticlesEnabled = true;    // particle effects toggle (on by default; tray menu can disable)
        float skidDustTimer;
        string observedParticleAnimId;
        int observedParticleAnimFrame = -1;
        string observedSoundAnimId;
        int observedSoundAnimFrame = -1;
        bool soundDucking;
        int observedLaunchCount;
        int observedRingDashSequenceCount;
        int observedWallJumpEffectCount;
        int observedJumpEffectCount;
        int observedLandingEffectCount;
        int observedElytraDeployCount;
        int observedExplodeLaunchCount;
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
        float dreamTrailTimer;
        const int CatTailCount = 8;
        readonly PointF[] catTailNodes = new PointF[CatTailCount];
        readonly Color[] customHairColors = new Color[3];
        readonly Queue<int> speedometerSamples = new Queue<int>(10);
        bool catTailStarted;
        bool catTailEnabled, catBangsEnabled, customHairColorsEnabled;
        int speedometerMode;
        bool hitboxesEnabled;
        bool dreamBlockMode;
        volatile bool ignoreMaximizedWindows;   // read by the poll on the game-loop thread
        int edgeWrapMode;
        readonly List<RectangleF> monitorGameBounds = new List<RectangleF>();
        readonly Bitmap[] picoDigits = new Bitmap[10];
        readonly List<Glider> gliders = new List<Glider>();
        readonly List<IPetHoldable> holdables = new List<IPetHoldable>();
        readonly object gliderWindowLock = new object();
        readonly Dictionary<Glider, JellyInputWindow> gliderWindows = new Dictionary<Glider, JellyInputWindow>();
        readonly Dictionary<Glider, GliderStampCache> gliderStampCache = new Dictionary<Glider, GliderStampCache>();
        readonly List<TheoCrystal> theos = new List<TheoCrystal>();
        readonly object theoWindowLock = new object();
        readonly Dictionary<TheoCrystal, TheoInputWindow> theoWindows = new Dictionary<TheoCrystal, TheoInputWindow>();
        readonly List<Seeker> seekers = new List<Seeker>();
        readonly object seekerWindowLock = new object();
        readonly Dictionary<Seeker, SeekerInputWindow> seekerWindows = new Dictionary<Seeker, SeekerInputWindow>();
        readonly Dictionary<Seeker, GliderStampCache> seekerStampCache = new Dictionary<Seeker, GliderStampCache>();
        Glider draggedGlider;
        PointF gliderDragOffset, gliderCursorVelocity;
        Point lastGliderCursor;
        TheoCrystal draggedTheo;
        PointF theoDragOffset, theoCursorVelocity;
        Point lastTheoCursor;
        Seeker draggedSeeker;
        PointF seekerDragOffset, seekerCursorVelocity;
        Point lastSeekerCursor;
        bool seekerRespawnDormant, observedPlayerRespawning;

        sealed class GliderStampCache
        {
            public string FrameId;
            public int Rotation, ScaleX, ScaleY;
            public Bitmap Bitmap;
        }

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
            PadInputEnabled = settings.PadInputEnabled;
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
            dreamBlockMode = settings.DreamBlockMode;
            ignoreMaximizedWindows = settings.IgnoreMaximizedWindows;
            edgeWrapMode = settings.EdgeWrapMode;
            player.ElytraEnabled = settings.ElytraEnabled;
            player.SetFreezeFramesEnabled(settings.FreezeFramesEnabled);
            player.RespawnReversalEnabled = settings.RespawnReversalEnabled;
            player.InfiniteStamina = settings.InfiniteStamina;
            player.Invincible = settings.Invincible;
            player.SetDashMode(settings.DashMode);
            player.NormalSurfaceSoundIndex = settings.SurfaceSoundIndex;
            player.Holdables = holdables;
            Loc.SetLanguage(Loc.DetectDefault(settings.Language));
            // Both the sounds below and the sprites further down are read out of Celeste, so
            // where it is has to be settled before either of them looks.
            ResolveCelesteInstall();
            soundEffects = new SoundEffects(
                () => IsPetInputWindow(Win32.GetForegroundWindow()),
                settings.SfxMode, settings.SfxVolume);
            bindings = new KeyBindings(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "keybindings.txt"));
            padBindings = new PadBindings(System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "padbindings.txt"));
            // Cap log growth: rewrite when over 5MB (keeps the latest run)
            try
            {
                var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pet_debug.log");
                if (new System.IO.FileInfo(logPath).Length > 5 * 1024 * 1024)
                    System.IO.File.WriteAllText(logPath, "");
            }
            catch { }
            // ---- Window style: borderless, no taskbar entry, DirectComposition transparency ----
            FormBorderStyle = FormBorderStyle.None;
            Text = "Desk Madeline";
            // Layered-canvas size is controlled explicitly by GameScale; do not let WinForms
            // re-scale it again on WM_DPICHANGED via font DPI.
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // Note: do not set BackColor/TransparencyKey (color-key layering conflicts with ULW)
            Size = new Size(24 * GameScale, 33 * GameScale);
            Location = new Point(-10000, -10000);
            BackColor = Color.Black;
            Opacity = 0.01; // nonzero alpha keeps the small body hit target interactive

            // ---- Sprites and animations ----
            skinManager = new SkinManager(AppDomain.CurrentDomain.BaseDirectory);
            var initialSkin = skinManager.Find(settings.Skin);
            skinManager.Activate(initialSkin);
            Sprites.LoadAll(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "player"),
                initialSkin?.PlayerDirectory, initialSkin?.PlayerAtlasFolder);
            try
            {
                // Shipped beside the app, or read from the same atlas the sprites came from.
                string fontFile = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "assets", "pico8font.png");
                using var fontSource = System.IO.File.Exists(fontFile)
                    ? new Bitmap(fontFile)
                    : Sprites.Get("pico8/font", false) is Bitmap atlasFont
                        ? new Bitmap(atlasFont)
                        : throw new System.IO.FileNotFoundException(fontFile);
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
            // ParticleTypes.cs: Player.P_DashBadB, used by
            // PlayerSpriteMode.MadelineAsBadeline for a two-dash dash.
            dashBadeline = new PType
            {
                Tex = new[] { "dashParticle" },
                Color = Color.FromArgb(0x9B, 0x3F, 0xB5),
                Color2 = Color.FromArgb(0xCC, 0x8E, 0xE2),
                BlinkColor = true,
                GravY = 8f,
                LifeMin = 1f, LifeMax = 1.8f,
                Size = 1f,
                SpeedMin = 10f, SpeedMax = 20f,
                LateFade = true
            };
            elytraDeploy = new PType
            {
                Tex = new[] { "smoke0", "smoke1", "smoke2", "smoke3" },
                Color = Color.White,
                Color2 = Color.LightGray,
                ChooseColor = true,
                GravY = 1f,
                LifeMin = 1f, LifeMax = 3f,
                // The source texture is 8px and the mod uses scale 1 +/- 0.4.
                Size = 8f, SizeRange = 3.2f,
                SpeedMin = 10f, SpeedMax = 60f,
                ScaleOut = true,
                FadeOut = false
            };
            seekerAttack = new PType
            {
                Tex = new[] { "dashParticle" }, Color = Color.FromArgb(0x99, 0xE5, 0x50),
                Color2 = Color.FromArgb(0xDD, 0xFF, 0xBC), BlinkColor = true,
                LifeMin = .6f, LifeMax = 1.2f, Size = 1f,
                SpeedMin = 20f, SpeedMax = 40f, SpeedMultiplier = .4f, LateFade = true
            };
            seekerStomp = seekerAttack;
            seekerHitWall = new PType
            {
                Tex = new[] { "dashParticle" }, Color = Color.FromArgb(0x99, 0xE5, 0x50),
                Color2 = Color.FromArgb(0xDD, 0xFF, 0xBC), BlinkColor = true,
                LifeMin = .6f, LifeMax = 1.2f, Size = 1f,
                SpeedMin = 30f, SpeedMax = 60f, SpeedMultiplier = .4f, LateFade = true
            };
            seekerRegen = new PType
            {
                Tex = new[] { "dashParticle" }, Color = Color.FromArgb(0xCB, 0xDB, 0xFC),
                Color2 = Color.FromArgb(0x57, 0x5F, 0xD9), BlinkColor = true,
                LifeMin = .4f, LifeMax = 1.2f, Size = 1f,
                SpeedMin = 20f, SpeedMax = 100f, SpeedMultiplier = .4f, LateFade = true
            };
            // ParticleTypes.cs: TheoCrystal.P_Impact.
            theoImpact = new PType
            {
                Tex = new[] { "dashParticle" }, Color = Color.FromArgb(0xCB, 0xDB, 0xFC),
                LifeMin = .3f, LifeMax = .8f, Size = 1f,
                SpeedMin = 10f, SpeedMax = 20f, SpeedMultiplier = .1f, LateFade = true
            };
            anims = BuildAnims();
            animator = new Animator(anims);
            sweatAnimator = new Animator(BuildSweatAnims());
            sweatAnimator.Play("idle");
            animator.Play("wakeUp");   // Play wake-up animation on startup

            // ---- Spawn point: bottom-center of primary working area ----
            var wa = Screen.PrimaryScreen.WorkingArea;
            player.Pos = new PointF(ToGamePixels((wa.Left + wa.Right) / 2), ToGamePixels(wa.Bottom) - 2);

            // ---- Tray ----
            trayMenu = BuildMenu();
            tray = new NotifyIcon
            {
                Text = Loc.T("App.Name"),
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
                // Keep tool-window style (hidden from Alt+Tab/taskbar) but allow activation: after clicking Madeline
                // she takes keyboard focus so movement keys are not also typed into other apps.
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
            // Start the game loop only after the window is really shown (DirectComposition target needs a ready HWND)
            running = true;
            loopThread = new Thread(GameLoop) { IsBackground = true, Name = "PetLoop" };
            loopThread.Start();
        }

        // ================= Animation definitions =================
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
            Add("throw", Sprites.Seq("throw", 0, 3), 0.06f, false);
            Add("dreamDashIn", Sprites.Seq("dreamDash", 0, 3), 0.04f, false);
            if (d.TryGetValue("dreamDashIn", out var dreamIn)) dreamIn.Goto = "dreamDashLoop";
            Add("dreamDashLoop", Sprites.Seq("dreamDash", 4, 16), 0.03f, true);
            Add("dreamDashOut", Sprites.Seq("dreamDash", 17, 20), 0.04f, false);
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
            Add("elytra", Sprites.Seq("fly", 0, 8), 10f, true, manual: true);
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

        // ================= Game loop =================
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
                TimePeriod.End(1);   // Restore timer resolution even on exceptions
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
            soundEffects.Update();
            if (pendingSkinId != null)
            {
                string id = pendingSkinId;
                pendingSkinId = null;
                var skin = skinManager.Find(id);
                Sprites.LoadAll(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "player"),
                    skin?.PlayerDirectory, skin?.PlayerAtlasFolder);
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
                var glider = new Glider(new PointF(
                    player.Pos.X + player.Facing * (18f + i * 5f), player.Pos.Y - 16f));
                gliders.Add(glider);
                holdables.Add(glider);
            }
            if (spawnCount > 0 && IsHandleCreated)
                BeginInvoke(new Action(EnsureGliderWindows));

            int seekerSpawnCount = Interlocked.Exchange(ref pendingSeekerSpawns, 0);
            for (int i = 0; i < seekerSpawnCount; i++)
            {
                PointF desired = new PointF(
                    player.Pos.X + player.Facing * 48f, player.Pos.Y - 24f);
                PointF? spawn = FindFreeSeekerSpawn(desired, player.Facing);
                if (spawn.HasValue) seekers.Add(new Seeker(spawn.Value));
            }
            if (seekerSpawnCount > 0 && IsHandleCreated)
                BeginInvoke(new Action(EnsureSeekerWindows));

            int theoSpawnCount = Interlocked.Exchange(ref pendingTheoSpawns, 0);
            for (int i = 0; i < theoSpawnCount; i++)
            {
                var theo = new TheoCrystal(new PointF(
                    player.Pos.X + player.Facing * (20f + i * 8f), player.Pos.Y - 12f));
                theos.Add(theo);
                holdables.Add(theo);
            }
            if (theoSpawnCount > 0 && IsHandleCreated)
                BeginInvoke(new Action(EnsureTheoWindows));

            int removeFlags = Interlocked.Exchange(ref pendingRemoveAllEntities, 0);
            if ((removeFlags & 1) != 0)
                lock (pendingGliderRemovals)
                    foreach (Glider glider in gliders) pendingGliderRemovals.Enqueue(glider);
            if ((removeFlags & 2) != 0)
                lock (pendingSeekerRemovals)
                    foreach (Seeker seeker in seekers) pendingSeekerRemovals.Enqueue(seeker);
            if ((removeFlags & 4) != 0)
                lock (pendingTheoRemovals)
                    foreach (TheoCrystal theo in theos) pendingTheoRemovals.Enqueue(theo);

            lock (pendingGliderRemovals)
            {
                while (pendingGliderRemovals.Count > 0)
                {
                    Glider glider = pendingGliderRemovals.Dequeue();
                    if (!gliders.Remove(glider)) continue;
                    holdables.Remove(glider);
                    player.ForgetHoldable(glider);
                    if (draggedGlider == glider) draggedGlider = null;
                    soundEffects.StopLoop(glider);
                    if (gliderStampCache.TryGetValue(glider, out GliderStampCache cache))
                    {
                        cache.Bitmap?.Dispose();
                        gliderStampCache.Remove(glider);
                    }
                    if (IsHandleCreated) BeginInvoke(new Action(() => CloseGliderWindow(glider)));
                }
            }

            lock (pendingTheoRemovals)
            {
                while (pendingTheoRemovals.Count > 0)
                {
                    TheoCrystal theo = pendingTheoRemovals.Dequeue();
                    if (!theos.Remove(theo)) continue;
                    holdables.Remove(theo);
                    player.ForgetHoldable(theo);
                    if (draggedTheo == theo) draggedTheo = null;
                    soundEffects.StopLoop(theo);
                    if (IsHandleCreated) BeginInvoke(new Action(() => CloseTheoWindow(theo)));
                }
            }

            lock (pendingSeekerRemovals)
            {
                while (pendingSeekerRemovals.Count > 0)
                {
                    Seeker seeker = pendingSeekerRemovals.Dequeue();
                    if (!seekers.Remove(seeker)) continue;
                    if (draggedSeeker == seeker) draggedSeeker = null;
                    soundEffects.StopLoop(seeker);
                    if (seekerStampCache.TryGetValue(seeker, out GliderStampCache cache))
                    {
                        cache.Bitmap?.Dispose();
                        seekerStampCache.Remove(seeker);
                    }
                    foreach (SeekerTrail trail in seeker.Trails) trail.Stamp?.Dispose();
                    if (IsHandleCreated) BeginInvoke(new Action(() => CloseSeekerWindow(seeker)));
                }
            }

            // Apply pending scale change
            if (pendingScale > 0)
            {
                GameScale = pendingScale;
                pendingScale = -1;
                presenter.Resize(GameScale);
                pollCounter = 999; // Immediately re-poll platforms (units changed)
            }

            // Platform poll (every 0.25s)
            if (++pollCounter >= 15)
            {
                pollCounter = 0;
                PollSolids();
            }

            if (introWakeUp)
            {
                // Startup wake-up: freeze physics, play only wakeUp + hair sim; switch to idle when done.
                float hx = 0f, hy = 0f;
                if (HairMeta.TryGet(animator.CurrentFrameId, out var wm)) { hx = wm.Offset.X; hy = wm.Offset.Y; }
                player.UpdateHairOnly(dt, hx, hy);
                animator.Update(dt);
                EmitAnimationSounds();
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

            // Input
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
                EmitAnimationSounds();
                if (ParticlesEnabled) EmitAnimationParticles();

                tiredFlashTimer += dt;
                while (tiredFlashTimer >= 0.05f)
                {
                    tiredFlashTimer -= 0.05f;
                    tiredFlash = !tiredFlash;
                }
            }

            // Physics
            int wasState = player.State;
            bool wasDeadOrRespawning = player.IsDead || player.IsRespawning;
            PointF beforeUpdatePosition = player.Pos;
            player.Update(dt, input);
            UpdateSoundEffects(wasState);
            if (!wasDeadOrRespawning) ApplyEdgeWrap(beforeUpdatePosition);

            bool playerRespawningNow = player.IsRespawning;
            if (playerRespawningNow && !observedPlayerRespawning)
            {
                // Level.Reload recreates room entities in Celeste. Menu-spawned
                // desktop entities have no map loader, so reset them explicitly.
                seekerRespawnDormant = true;
                draggedSeeker = null;
                foreach (Seeker seeker in seekers)
                    seeker.ResetForRoomReload(player.Center);
            }
            observedPlayerRespawning = playerRespawningNow;
            bool anyInput = input.MoveX != 0 || input.MoveY != 0 || input.JumpHeld ||
                input.GrabHeld || input.DashPressed || input.ElytraHeld || dragging;
            if (seekerRespawnDormant && !playerRespawningNow && anyInput)
                seekerRespawnDormant = false;

            // A frame that began frozen only advances the raw freeze countdown.
            if (frozenAtStart)
            {
                UpdateDashCoreVisuals(0f); // observe the dash, but do not spawn/age FX
                return;
            }

            // UpdateSprite selects animations after component advancement. A newly
            // selected animation stays on frame zero until the next game frame.
            animator.Play(player.AnimId);
            if (player.State == Player.StElytra) animator.Frame = player.ElytraAnimationFrame;
            EmitAnimationSounds();
            bool restartSweat = player.SweatAnimSequenceCount != observedSweatAnimSequenceCount;
            observedSweatAnimSequenceCount = player.SweatAnimSequenceCount;
            sweatAnimator.Play(player.SweatAnimId, restartSweat);
            player.AnimFinished = animator.Finished ||
                !string.Equals(player.AnimId, animator.CurrentId, StringComparison.OrdinalIgnoreCase);
            player.AnimLoopCount = animator.LoopCount;
            player.CurrentFrameId = animator.CurrentFrameId;
            // Player.orig_Update orders UpdateSprite before UpdateCarry. The
            // animator is hosted here, so apply the held actor's curve only after
            // the matching frame (and its CarryYOffset metadata) is available.
            player.UpdateCarryPosition(ResolveCarryYOffset(player.CurrentFrameId));
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

            var seekerWorldBounds = new RectangleF(
                virtualDesktop.Left / (float)GameScale,
                virtualDesktop.Top / (float)GameScale,
                virtualDesktop.Width / (float)GameScale,
                virtualDesktop.Height / (float)GameScale);

            foreach (Glider glider in gliders)
            {
                glider.Update(dt, input, player.Solids, player.MinX, player.MaxX);
                while (glider.SoundEvents.Count > 0)
                {
                    PlayerSoundEvent sound = glider.SoundEvents.Dequeue();
                    soundEffects.Play(sound.Path, sound.Parameter, sound.Value);
                }
                if (glider.MovementSoundActive)
                {
                    soundEffects.StartLoop(glider,
                        "event:/new_content/game/10_farewell/glider_movement");
                    soundEffects.SetLoopParameter(glider, "glider_speed", glider.MovementSoundSpeed);
                }
                else soundEffects.StopLoop(glider);
            }

            foreach (TheoCrystal theo in theos)
            {
                theo.Update(dt, player, player.Solids, seekerWorldBounds);
                while (theo.SoundEvents.Count > 0)
                {
                    PlayerSoundEvent sound = theo.SoundEvents.Dequeue();
                    soundEffects.Play(sound.Path, sound.Parameter, sound.Value);
                }
                while (theo.ImpactEvents.Count > 0)
                {
                    TheoImpactEvent effect = theo.ImpactEvents.Dequeue();
                    if (ParticlesEnabled)
                        seekerParticles.Emit(theoImpact, effect.Position.X, effect.Position.Y,
                            effect.Direction, .87266465f, 12, effect.RangeX, effect.RangeY);
                }
                if (theo.Removed)
                    lock (pendingTheoRemovals) pendingTheoRemovals.Enqueue(theo);
            }

            var seekerCamera = new RectangleF(player.Pos.X - 160f, player.Pos.Y - 90f, 320f, 180f);
            foreach (Seeker seeker in seekers)
            {
                if (seekerRespawnDormant) seeker.UpdateDormant(dt, player.Solids, seekerWorldBounds);
                else seeker.Update(dt, player, player.Solids, seekerWorldBounds, seekerCamera, theos);
                while (seeker.SoundEvents.Count > 0)
                {
                    PlayerSoundEvent sound = seeker.SoundEvents.Dequeue();
                    soundEffects.Play(sound.Path, sound.Parameter, sound.Value);
                }
                if (seeker.BoopedLoopActive)
                    soundEffects.StartLoop(seeker, "event:/game/05_mirror_temple/seeker_booped");
                else soundEffects.StopLoop(seeker);
                while (seeker.ParticleEvents.Count > 0)
                {
                    SeekerParticleEvent effect = seeker.ParticleEvents.Dequeue();
                    if (!ParticlesEnabled) continue;
                    PType type = effect.Kind == SeekerParticleKind.HitWall ? seekerHitWall :
                        effect.Kind == SeekerParticleKind.Stomp ? seekerStomp :
                        effect.Kind == SeekerParticleKind.Regen ? seekerRegen : seekerAttack;
                    // ParticleType.DirectionRange is the full arc; ParticleSystem.Emit
                    // accepts a +/- half-range.
                    float directionRange = effect.Kind == SeekerParticleKind.Regen
                        ? (float)Math.PI / 6f : .87266465f;
                    seekerParticles.Emit(type, effect.Position.X, effect.Position.Y, effect.Direction,
                        directionRange, effect.Count, effect.RangeX, effect.RangeY);
                }
            }

            UpdateWavedashWaves(dt);

            // Back onto a display first; only a position no display can account for resets.
            SnapIntoView();
            SnapEntitiesIntoView();
            // Auto-reset when far off-screen (prevents infinite fall / being thrown off the virtual desktop)
            if (!introWakeUp && !player.IsDead) CheckAutoReset();

            // Particles (run/land/jump/dash) + dash slash timing
            if (ParticlesEnabled)
            {
                EmitPlayerParticles(dt, wasState);
                particles.Update(dt);
                seekerParticles.Update(dt);
            }
            else
            {
                particles.Clear();
                seekerParticles.Clear();
                observedJumpEffectCount = player.JumpEffectCount;
                observedWallJumpEffectCount = player.WallJumpEffectCount;
                observedLandingEffectCount = player.LandingEffectCount;
            }

            UpdateDashCoreVisuals(dt);
        }

        static Color Rgb(int value) => Color.FromArgb((value >> 16) & 255, (value >> 8) & 255, value & 255);
        static int RgbValue(Color value) => (value.R << 16) | (value.G << 8) | value.B;

        PointF? FindFreeSeekerSpawn(PointF desired, int facing)
        {
            bool Free(PointF candidate)
            {
                var bounds = new RectangleF(
                    virtualDesktop.Left / (float)GameScale + 3f,
                    virtualDesktop.Top / (float)GameScale - 5f,
                    virtualDesktop.Width / (float)GameScale - 6f,
                    virtualDesktop.Height / (float)GameScale + 2f);
                if (!bounds.Contains(candidate)) return false;
                foreach (Solid solid in player.Solids)
                    if (candidate.X - 3f < solid.R && candidate.X + 3f > solid.L &&
                        candidate.Y - 3f < solid.B && candidate.Y + 3f > solid.T)
                        return false;
                foreach (Seeker seeker in seekers)
                    if (!seeker.Removed && Math.Abs(candidate.X - seeker.Pos.X) < 6f &&
                        Math.Abs(candidate.Y - seeker.Pos.Y) < 6f)
                        return false;
                return true;
            }

            if (Free(desired)) return desired;
            int direction = facing == 0 ? 1 : facing;
            // The physics hitbox is 6x6. Eight-pixel steps leave a small visible
            // gap and keep repeated menu spawns close to the requested position.
            for (int ring = 1; ring <= 12; ring++)
            {
                float d = ring * 8f;
                PointF[] candidates =
                {
                    new PointF(desired.X + direction * d, desired.Y),
                    new PointF(desired.X - direction * d, desired.Y),
                    new PointF(desired.X, desired.Y - d),
                    new PointF(desired.X, desired.Y + d),
                    new PointF(desired.X + direction * d, desired.Y - d),
                    new PointF(desired.X - direction * d, desired.Y - d),
                    new PointF(desired.X + direction * d, desired.Y + d),
                    new PointF(desired.X - direction * d, desired.Y + d)
                };
                foreach (PointF candidate in candidates)
                    if (Free(candidate)) return candidate;
            }
            // Do not create an overlapping actor if the compact spawn area is full.
            return null;
        }

        void UpdateSoundEffects(int wasState)
        {
            if (wasState == Player.StDreamDash && player.State != Player.StDreamDash)
                soundEffects.StopLoop();
            while (player.SoundEvents.Count > 0)
            {
                PlayerSoundEvent sound = player.SoundEvents.Dequeue();
                soundEffects.Play(sound.Path, sound.Parameter, sound.Value);
                if (sound.Path == "event:/char/madeline/dreamblock_enter")
                    soundEffects.StartLoop("event:/char/madeline/dreamblock_travel");
            }
            if (player.Ducking != soundDucking)
            {
                // orig_Update sounds the duck wherever it happens, but standing up only counts
                // on the ground: unducking into a hyper off a ledge is not someone standing up.
                if (!player.IsDead && !player.IsRespawning && (player.Ducking || player.onGround))
                    soundEffects.Play(player.Ducking
                        ? "event:/char/madeline/duck"
                        : "event:/char/madeline/stand");
                soundDucking = player.Ducking;
            }
            // orig_Update tests the sprite selected on the preceding frame before
            // UpdateSprite chooses the next one later in the same player update.
            bool wallSliding = animator.CurrentId == "wallslide" && player.Speed.Y > 0f;
            if (wallSliding)
            {
                soundEffects.StartLoop("wallslide", "event:/char/madeline/wallslide");
                soundEffects.SetLoopParameter("wallslide", "surface_index",
                    player.WallSurfaceSoundIndex(player.Facing));
            }
            else soundEffects.StopLoop("wallslide");
        }

        void EmitAnimationSounds()
        {
            if (animator.CurrentId == observedSoundAnimId && animator.Frame == observedSoundAnimFrame)
                return;
            observedSoundAnimId = animator.CurrentId;
            observedSoundAnimFrame = animator.Frame;
            string id = animator.CurrentId;
            int frame = animator.Frame;
            bool footstep =
                ((id == "runSlow_carry" || id == "runFast" || id == "runSlow") && (frame == 0 || frame == 6)) ||
                (id == "runStumble" && frame == 6) || (id == "flip" && frame == 4) ||
                (id == "push" && (frame == 8 || frame == 15));
            if (footstep)
                soundEffects.Play("event:/char/madeline/footstep", "surface_index",
                    player.GroundSurfaceSoundIndex);
            else if (id == "climb" && frame == 5)
                soundEffects.Play("event:/char/madeline/handhold", "surface_index",
                    player.WallSurfaceSoundIndex(player.Facing));
            else if (introWakeUp && id == "wakeUp" && frame == 19)
                soundEffects.Play("event:/char/madeline/campfire_stand");
        }

        internal Color ResolveHairColor(int dashes, Color fallback)
        {
            if (customHairColorsEnabled) return customHairColors[Math.Max(0, Math.Min(2, dashes))];
            return skinManager.ResolveHairColor(dashes, fallback);
        }

        internal float ResolveCarryYOffset(string frameId)
        {
            if (string.IsNullOrEmpty(frameId)) return 0f;
            int frame = 0;
            int split = frameId.Length;
            while (split > 0 && char.IsDigit(frameId[split - 1])) split--;
            if (split < frameId.Length) int.TryParse(frameId.Substring(split), out frame);
            string id = frameId.Substring(0, split);
            int[] offsets = null;
            if (skinManager.TryGetCarryOffsets(id, out int[] skinOffsets))
                offsets = skinOffsets;
            else if (id.Equals("idle_carry", StringComparison.OrdinalIgnoreCase))
                offsets = new[] { -1, -1, -1, 0, 0, 0, 0, 0, -1 };
            else if (id.Equals("run_carry", StringComparison.OrdinalIgnoreCase))
                offsets = new[] { -1, 0, 0, 0, -3, -2, -1, 0, 0, 0, -3, -1 };
            else if (id.Equals("jump_carry", StringComparison.OrdinalIgnoreCase))
                offsets = new[] { -3, -3, -1, -1 };
            return offsets != null && frame >= 0 && frame < offsets.Length ? offsets[frame] : 0f;
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

            if (player.ExplodeLaunchSequenceCount != observedExplodeLaunchCount)
            {
                observedExplodeLaunchCount = player.ExplodeLaunchSequenceCount;
                slash = new SlashVisual
                {
                    Active = true,
                    X = player.Center.X,
                    Y = player.Center.Y,
                    Angle = player.ExplodeLaunchAngle
                };
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

            if (player.State == Player.StDreamDash)
            {
                dreamTrailTimer -= dt;
                if (dreamTrailTimer <= 0f)
                {
                    CaptureDashTrail();
                    dreamTrailTimer += 0.1f;
                }
            }
            else
                dreamTrailTimer = 0f;

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
                float rootX = SnapPx(trail.HairNodes[0].X - trail.X + center);
                float rootY = SnapPx(trail.HairNodes[0].Y - trail.Y + center);
                for (int i = 0; i < trail.HairCount; i++)
                {
                    float scale = HairSegmentScale(i, trail.HairCount);
                    float pieceW = 10f * scale * Math.Abs(trail.ScaleX);
                    float pieceH = 10f * scale;
                    var tex = i == 0 ? bangs : blob;
                    float x = rootX + trail.HairNodes[i].X - trail.HairNodes[0].X - pieceW / 2f;
                    float y = rootY + trail.HairNodes[i].Y - trail.HairNodes[0].Y - pieceH / 2f;
                    DrawTintedSafe(g, tex, Color.Black, x - 1, y, pieceW, pieceH);
                    DrawTintedSafe(g, tex, Color.Black, x + 1, y, pieceW, pieceH);
                    DrawTintedSafe(g, tex, Color.Black, x, y - 1, pieceW, pieceH);
                    DrawTintedSafe(g, tex, Color.Black, x, y + 1, pieceW, pieceH);
                }
                for (int i = trail.HairCount - 1; i >= 0; i--)
                {
                    float scale = HairSegmentScale(i, trail.HairCount);
                    float pieceW = 10f * scale * Math.Abs(trail.ScaleX);
                    float pieceH = 10f * scale;
                    var tex = i == 0 ? bangs : blob;
                    DrawTintedSafe(g, tex, trail.HairColor,
                        rootX + trail.HairNodes[i].X - trail.HairNodes[0].X - pieceW / 2f,
                        rootY + trail.HairNodes[i].Y - trail.HairNodes[0].Y - pieceH / 2f,
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

        // Celeste Player.Update + SpeedRing: after Super/Hyper/Wavedash jump, while speed stays
        // at 140+ for the first 0.5s spawn a ring every 0.15s; each ring expands at 3/s and advances at 10px/s.
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

        // Particle emission (parameters ported from Celeste Player.cs Dust.Burst calls)
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

            if (player.ElytraDeploySequenceCount != observedElytraDeployCount)
            {
                observedElytraDeployCount = player.ElytraDeploySequenceCount;
                for (int i = 0; i < 10; i++)
                {
                    float deviation = (float)(effectRng.NextDouble() * 2.0 - 1.0);
                    particles.Emit(elytraDeploy, player.Pos.X, player.Pos.Y - 5.5f,
                        player.ElytraDeployParticleAngle + deviation * 0.1f,
                        (float)Math.PI / 4f, 1);
                }
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

            // Dash
            if (player.State == Player.StDash)
            {
                float dashAngle = (float)Math.Atan2(player.DashDir.Y, player.DashDir.X);
                if (wasState != Player.StDash)
                {
                    dashParticleTimer = 0f;
                }
                dashParticleTimer += dt;
                // Vanilla DashUpdate: while moving, emit one P_DashA/P_DashB every 0.02s.
                while (dashParticleTimer >= 0.02f &&
                       (player.Speed.X != 0f || player.Speed.Y != 0f))
                {
                    dashParticleTimer -= 0.02f;
                    float px = player.Pos.X + (float)(effectRng.NextDouble() * 4.0 - 2.0);
                    float py = player.Pos.Y - 5.5f + (float)(effectRng.NextDouble() * 4.0 - 2.0);
                    PType dashType = !player.LastDashWasTwo ? dashBlue :
                        skinManager.IsBadeline ? dashBadeline : dashRed;
                    particles.Emit(dashType,
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
            // GetAsyncKeyState and XInput are both global. By default they are gated to
            // this pet's focus; the explicit menu opt-in permits reading them while
            // unfocused. No hook is installed and keys are never swallowed from other apps.
            bool blocked = dragging || draggedGlider != null || draggedTheo != null || draggedSeeker != null ||
                (!InputWhenUnfocused && !IsPetInputWindow(Win32.GetForegroundWindow()));
            bool useKeys = InputEnabled && !blocked;
            bool usePad = PadInputEnabled && !blocked;
            if (!useKeys && !usePad)
            {
                prevJump = prevDash = prevCrouchDash = false;
                return input;
            }

            PadState pad = usePad ? XInputPad.Poll() : default;
            // Keyboard bindings are digital, so the threshold only ever affects the controller;
            // it reproduces Celeste's per-virtual-input deadzones.
            bool Held(PetAction action, float threshold)
                => (useKeys && bindings.IsDown(action)) || (usePad && padBindings.IsDown(pad, action, threshold));

            bool left = Held(PetAction.Left, PadBindings.MoveXThreshold);
            bool right = Held(PetAction.Right, PadBindings.MoveXThreshold);
            bool up = Held(PetAction.Up, PadBindings.MoveYThreshold);
            bool down = Held(PetAction.Down, PadBindings.MoveYThreshold);
            bool jump = Held(PetAction.Jump, PadBindings.ButtonThreshold);
            bool dash = Held(PetAction.Dash, PadBindings.ButtonThreshold);
            bool grab = Held(PetAction.Grab, PadBindings.ButtonThreshold);
            bool crouchDash = Held(PetAction.CrouchDash, PadBindings.ButtonThreshold);
            bool elytra = Held(PetAction.DeployElytra, PadBindings.ButtonThreshold);

            input.MoveX = (right ? 1 : 0) - (left ? 1 : 0);
            input.MoveY = (down ? 1 : 0) - (up ? 1 : 0);
            input.AimX = (Held(PetAction.Right, PadBindings.AimThreshold) ? 1 : 0)
                - (Held(PetAction.Left, PadBindings.AimThreshold) ? 1 : 0);
            input.AimY = (Held(PetAction.Down, PadBindings.AimThreshold) ? 1 : 0)
                - (Held(PetAction.Up, PadBindings.AimThreshold) ? 1 : 0);
            input.GliderMoveY = (Held(PetAction.Down, PadBindings.GliderMoveYThreshold) ? 1 : 0)
                - (Held(PetAction.Up, PadBindings.GliderMoveYThreshold) ? 1 : 0);
            input.JumpHeld = jump;
            input.GrabHeld = grab;
            input.ElytraHeld = elytra;

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

        bool IsPetInputWindow(IntPtr hwnd)
        {
            if (hwnd == Handle) return true;
            lock (gliderWindowLock)
                if (gliderWindows.Values.Any(window => window.IsHandleCreated && window.Handle == hwnd)) return true;
            lock (theoWindowLock)
                if (theoWindows.Values.Any(window => window.IsHandleCreated && window.Handle == hwnd)) return true;
            lock (seekerWindowLock)
                return seekerWindows.Values.Any(window => window.IsHandleCreated && window.Handle == hwnd);
        }

        void EnsureTheoWindows()
        {
            lock (theoWindowLock)
            {
                foreach (TheoCrystal theo in theos)
                {
                    if (theoWindows.ContainsKey(theo)) continue;
                    var window = new TheoInputWindow(this, theo);
                    theoWindows[theo] = window;
                    window.Show();
                }
            }
        }

        internal void RequestTheoRemoval(TheoCrystal theo)
        {
            lock (pendingTheoRemovals) pendingTheoRemovals.Enqueue(theo);
            BeginInvoke(new Action(() => CloseTheoWindow(theo)));
        }

        void CloseTheoWindow(TheoCrystal theo)
        {
            lock (theoWindowLock)
            {
                if (!theoWindows.TryGetValue(theo, out TheoInputWindow window)) return;
                theoWindows.Remove(theo); window.Close(); window.Dispose();
            }
        }

        internal void BeginTheoDrag(TheoCrystal theo)
        {
            Point cursor = Cursor.Position;
            theo.BeginDrag(player);
            draggedTheo = theo;
            theoDragOffset = new PointF(cursor.X / (float)GameScale - theo.Pos.X,
                cursor.Y / (float)GameScale - theo.Pos.Y);
            lastTheoCursor = cursor;
            theoCursorVelocity = PointF.Empty;
        }

        internal void ContinueTheoDrag(TheoCrystal theo)
        {
            if (draggedTheo != theo) return;
            Point cursor = Cursor.Position;
            const float dt = 1f / 60f;
            theoCursorVelocity = new PointF(
                theoCursorVelocity.X * .7f + (cursor.X - lastTheoCursor.X) / GameScale / dt * .3f,
                theoCursorVelocity.Y * .7f + (cursor.Y - lastTheoCursor.Y) / GameScale / dt * .3f);
            lastTheoCursor = cursor;
            theo.DragTo(new PointF(cursor.X / (float)GameScale - theoDragOffset.X,
                cursor.Y / (float)GameScale - theoDragOffset.Y));
        }

        internal void EndTheoDrag(TheoCrystal theo)
        {
            if (draggedTheo != theo) return;
            float vx = theoCursorVelocity.X * .6f, vy = theoCursorVelocity.Y * .6f;
            float length = (float)Math.Sqrt(vx * vx + vy * vy);
            if (length > 400f) { vx *= 400f / length; vy *= 400f / length; }
            if (length < 30f) vx = vy = 0f;
            theo.EndDrag(new PointF(vx, vy));
            draggedTheo = null;
        }

        void EnsureSeekerWindows()
        {
            lock (seekerWindowLock)
            {
                foreach (Seeker seeker in seekers)
                {
                    if (seekerWindows.ContainsKey(seeker)) continue;
                    var window = new SeekerInputWindow(this, seeker);
                    seekerWindows[seeker] = window;
                    window.Show();
                }
            }
        }

        internal void RequestSeekerRemoval(Seeker seeker)
        {
            lock (pendingSeekerRemovals) pendingSeekerRemovals.Enqueue(seeker);
            BeginInvoke(new Action(() => CloseSeekerWindow(seeker)));
        }

        void CloseSeekerWindow(Seeker seeker)
        {
            lock (seekerWindowLock)
            {
                if (!seekerWindows.TryGetValue(seeker, out SeekerInputWindow window)) return;
                seekerWindows.Remove(seeker);
                window.Close();
                window.Dispose();
            }
        }

        internal void BeginSeekerDrag(Seeker seeker)
        {
            Point cursor = Cursor.Position;
            seeker.BeginDrag();
            draggedSeeker = seeker;
            seekerDragOffset = new PointF(cursor.X / (float)GameScale - seeker.Pos.X,
                cursor.Y / (float)GameScale - seeker.Pos.Y);
            lastSeekerCursor = cursor;
            seekerCursorVelocity = PointF.Empty;
        }

        internal void ContinueSeekerDrag(Seeker seeker)
        {
            if (draggedSeeker != seeker) return;
            Point cursor = Cursor.Position;
            const float dt = 1f / 60f;
            seekerCursorVelocity = new PointF(
                seekerCursorVelocity.X * .7f + (cursor.X - lastSeekerCursor.X) / GameScale / dt * .3f,
                seekerCursorVelocity.Y * .7f + (cursor.Y - lastSeekerCursor.Y) / GameScale / dt * .3f);
            lastSeekerCursor = cursor;
            seeker.DragTo(new PointF(cursor.X / (float)GameScale - seekerDragOffset.X,
                cursor.Y / (float)GameScale - seekerDragOffset.Y));
        }

        internal void EndSeekerDrag(Seeker seeker)
        {
            if (draggedSeeker != seeker) return;
            float vx = seekerCursorVelocity.X * .6f, vy = seekerCursorVelocity.Y * .6f;
            float length = (float)Math.Sqrt(vx * vx + vy * vy);
            if (length > 400f) { vx *= 400f / length; vy *= 400f / length; }
            if (length < 30f) vx = vy = 0f;
            seeker.EndDrag(new PointF(vx, vy));
            draggedSeeker = null;
        }

        void EnsureGliderWindows()
        {
            lock (gliderWindowLock)
            {
                foreach (Glider glider in gliders)
                {
                    if (gliderWindows.ContainsKey(glider)) continue;
                    var window = new JellyInputWindow(this, glider);
                    gliderWindows[glider] = window;
                    window.Show();
                }
            }
        }

        internal string Localize(string key) => Loc.T(key);

        internal void RequestGliderRemoval(Glider glider)
        {
            lock (pendingGliderRemovals)
                pendingGliderRemovals.Enqueue(glider);

            // Let the ToolStrip click finish before disposing its owner window.
            BeginInvoke(new Action(() => CloseGliderWindow(glider)));
        }

        void CloseGliderWindow(Glider glider)
        {
            lock (gliderWindowLock)
            {
                if (!gliderWindows.TryGetValue(glider, out JellyInputWindow window)) return;
                gliderWindows.Remove(glider);
                window.Close();
                window.Dispose();
            }
        }

        internal void BeginGliderDrag(Glider glider)
        {
            Point cursor = Cursor.Position;
            glider.BeginDrag(player);
            draggedGlider = glider;
            gliderDragOffset = new PointF(cursor.X / (float)GameScale - glider.Pos.X,
                cursor.Y / (float)GameScale - glider.Pos.Y);
            lastGliderCursor = cursor;
            gliderCursorVelocity = PointF.Empty;
        }

        internal void ContinueGliderDrag(Glider glider)
        {
            if (draggedGlider != glider) return;
            Point cursor = Cursor.Position;
            const float dt = 1f / 60f;
            gliderCursorVelocity = new PointF(
                gliderCursorVelocity.X * 0.7f + (cursor.X - lastGliderCursor.X) / GameScale / dt * 0.3f,
                gliderCursorVelocity.Y * 0.7f + (cursor.Y - lastGliderCursor.Y) / GameScale / dt * 0.3f);
            lastGliderCursor = cursor;
            glider.DragTo(new PointF(cursor.X / (float)GameScale - gliderDragOffset.X,
                cursor.Y / (float)GameScale - gliderDragOffset.Y));
        }

        internal void EndGliderDrag(Glider glider)
        {
            if (draggedGlider != glider) return;
            float vx = gliderCursorVelocity.X * 0.6f, vy = gliderCursorVelocity.Y * 0.6f;
            float length = (float)Math.Sqrt(vx * vx + vy * vy);
            if (length > 400f) { vx *= 400f / length; vy *= 400f / length; }
            if (length < 30f) vx = vy = 0f;
            glider.EndDrag(new PointF(vx, vy));
            draggedGlider = null;
        }

        // ================= Platforms (windows are platforms; hollow borders) =================
        /// <summary>Physical pixels to game pixels, snapped to the whole-pixel grid.</summary>
        /// <remarks>
        /// Celeste's collision is defined on whole pixels: Actor.MoveH/MoveV step one pixel at
        /// a time and every Solid is an integer rect, so a player who has come to rest against
        /// a wall has her collider edge exactly on the wall edge, and the port inherits checks
        /// that rely on it.  Player.SlipCheck probes the single point at the collider edge
        /// while the climb wall check is a box overlap reaching one pixel further; between the
        /// two lies a sub-pixel band where she counts as on the wall but the slip probe misses
        /// it, and climbing becomes a permanent 30px/s slip.  Window rectangles are physical
        /// pixels and rarely divide evenly by GameScale, so every coordinate handed to the
        /// physics is snapped here instead.
        ///
        /// Rounds halves up rather than through Math.Round, whose default tie-break is to the
        /// nearest even integer: that maps 15.5 and 16.5 both onto 16, so at 8x the two edges
        /// of an 8px window border would land on the same grid line and the border would
        /// disappear.  Adding 0.5 before flooring keeps whole-pixel offsets whole for every
        /// coordinate, negative ones on secondary monitors included.
        /// </remarks>
        float ToGamePixels(int physical) => (float)Math.Floor(physical / (double)GameScale + 0.5);

        /// <summary>Physical-pixel rect to a grid-aligned Solid; false when it rounds away to nothing.</summary>
        bool TryToSolid(IntPtr id, Win32.RECT r, bool dream, out Solid solid)
        {
            float l = ToGamePixels(r.Left), t = ToGamePixels(r.Top);
            float right = ToGamePixels(r.Right), b = ToGamePixels(r.Bottom);
            // A remnant narrower than a game pixel has no grid representation.  Keeping it
            // would leave a zero-width wall that still blocks movement but that SlipCheck's
            // point probe can never hit.  WindowEdges keeps real borders at least a pixel
            // thick, so only slivers left behind by occlusion are dropped here.
            if (right <= l || b <= t) { solid = default; return false; }
            solid = new Solid { Id = id, L = l, T = t, R = right, B = b, Dream = dream };
            return true;
        }

        /// <summary>A window the poll kept: its frame, and whether it may be stood on.</summary>
        readonly struct PolledWindow
        {
            public readonly IntPtr Handle;
            public readonly Win32.RECT Rect;
            /// <summary>False for a window that hides what is behind it but is not a platform.</summary>
            public readonly bool IsPlatform;

            public PolledWindow(IntPtr handle, Win32.RECT rect, bool isPlatform)
            {
                Handle = handle;
                Rect = rect;
                IsPlatform = isPlatform;
            }
        }

        void PollSolids()
        {
            float s = GameScale;
            var cur = new Dictionary<IntPtr, Win32.RECT>();
            IntPtr self = Handle;

            // EnumWindows enumerates top-to-bottom Z-order (front→back); keep order for front-window occlusion of rear windows
            var zorder = new List<PolledWindow>();

            Win32.EnumWindows((hwnd, _) =>
            {
                if (hwnd == self) return true;
                if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd)) return true;
                // Windows cloaked by DWM (UWP background, off virtual desktop)
                if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
                int ex = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
                if ((ex & Win32.WS_EX_TOOLWINDOW) != 0) return true;
                // Click-through transparent windows (other pets / overlays) are not platforms
                if ((ex & Win32.WS_EX_LAYERED) != 0 && (ex & Win32.WS_EX_TRANSPARENT) != 0) return true;
                string cls = Win32.GetClassNameString(hwnd);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Xaml_WindowedPopupClass") return true;
                if (dreamBlockMode && (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")) return true;
                if (!Win32.TryGetWindowRect(hwnd, out var r)) return true;
                if (r.Width < 24 || r.Height < 18) return true;
                // An ignored window is still in front of whatever it covers, so it is kept as
                // an occluder: dropping it outright would leave the borders of every window
                // underneath standing as walls in a place the user sees nothing at all.
                bool isPlatform = !(ignoreMaximizedWindows && IsMaximizedOrFullscreen(hwnd, r));
                // Only platforms are tracked between polls; the ride-along follows the window
                // underfoot, and nothing can be standing on one of these.
                if (isPlatform) cur[hwnd] = r;
                zorder.Add(new PolledWindow(hwnd, r, isPlatform));
                return true;
            }, IntPtr.Zero);

            // Build platforms (game units): keep only hollow window borders; front (higher Z) windows
            // subtract their full rect from rear borders so covered segments no longer collide.
            var solids = new List<Solid>(zorder.Count * 4 + 1);
            var occluders = new List<Win32.RECT>(zorder.Count);
            // Windows in front that hide what is behind them without being solid themselves.
            var hidersOnly = new List<Win32.RECT>();
            foreach (var window in zorder)
            {
                var r = window.Rect;
                if (window.IsPlatform)
                {
                    if (dreamBlockMode)
                    {
                        // Dream blocks union with each other: overlapping windows are one
                        // shape, and cutting them apart only invents edges that belong to no
                        // window.  What does come out is the ignored windows -- maximized and
                        // fullscreen -- and whatever they cover, which is solid nobody can see.
                        foreach (var p in SubtractRects(r, hidersOnly))
                            if (TryToSolid(window.Handle, p, true, out Solid dreamSolid)) solids.Add(dreamSolid);
                    }
                    else
                    {
                        foreach (var edge in WindowEdges(r))
                        {
                            var pieces = SubtractRects(edge, occluders);
                            foreach (var p in pieces)
                                if (TryToSolid(window.Handle, p, false, out Solid piece)) solids.Add(piece);
                        }
                    }
                }
                occluders.Add(r);   // This window as a whole occludes windows behind it
                if (!window.IsPlatform) hidersOnly.Add(r);
            }
            // Treat the exposed perimeter of the monitor union as solid.  Each edge
            // extends outward, then other monitor rectangles are subtracted from it:
            // a shared seam stays open while offset/non-overlapping portions are walls.
            int virtualLeft = int.MaxValue, virtualRight = int.MinValue;
            var screenRects = new List<Win32.RECT>();
            monitorGameBounds.Clear();
            foreach (var screen in Screen.AllScreens)
            {
                var bounds = screen.Bounds;
                screenRects.Add(new Win32.RECT
                {
                    Left = bounds.Left, Top = bounds.Top,
                    Right = bounds.Right, Bottom = bounds.Bottom
                });
                monitorGameBounds.Add(RectangleF.FromLTRB(
                    ToGamePixels(bounds.Left), ToGamePixels(bounds.Top),
                    ToGamePixels(bounds.Right), ToGamePixels(bounds.Bottom)));
                virtualLeft = Math.Min(virtualLeft, bounds.Left);
                virtualRight = Math.Max(virtualRight, bounds.Right);
            }
            int edgeDepth = Math.Max(64, (int)Math.Ceiling(400f * s));
            foreach (var r in screenRects)
            {
                var outsideEdges = new[]
                {
                    new KeyValuePair<bool, Win32.RECT>(false,
                        new Win32.RECT { Left = r.Left, Top = r.Top - edgeDepth, Right = r.Right, Bottom = r.Top }),
                    new KeyValuePair<bool, Win32.RECT>(false,
                        new Win32.RECT { Left = r.Left, Top = r.Bottom, Right = r.Right, Bottom = r.Bottom + edgeDepth }),
                    new KeyValuePair<bool, Win32.RECT>(true,
                        new Win32.RECT { Left = r.Left - edgeDepth, Top = r.Top, Right = r.Left, Bottom = r.Bottom }),
                    new KeyValuePair<bool, Win32.RECT>(true,
                        new Win32.RECT { Left = r.Right, Top = r.Top, Right = r.Right + edgeDepth, Bottom = r.Bottom })
                };
                foreach (var edge in outsideEdges)
                {
                    if (edge.Key ? (edgeWrapMode & 1) != 0 : (edgeWrapMode & 2) != 0) continue;
                    foreach (var p in SubtractRects(edge.Value, screenRects))
                        if (TryToSolid(FloorId, p, false, out Solid piece))
                        {
                            piece.OffScreen = true;
                            solids.Add(piece);
                        }
                }
            }

            // Screen bounds under PerMonitorV2 are physical pixels, same as DWM.
            // Compute left/right extremes from the real monitor union to avoid SystemInformation DPI virtualization.
            if (virtualLeft != int.MaxValue)
            {
                if ((edgeWrapMode & 1) != 0)
                {
                    player.MinX = -100000f;
                    player.MaxX = 100000f;
                }
                else
                {
                    player.MinX = ToGamePixels(virtualLeft);
                    player.MaxX = ToGamePixels(virtualRight);
                }
            }

            // Ride-along: follow the window underfoot when it moves.  Only whole game pixels
            // reach Pos; the sub-pixel part is carried in a remainder the way
            // Actor.movementCounter carries one, so a slow window drag is not rounded away.
            if (player.GroundId != IntPtr.Zero && player.GroundId != FloorId &&
                lastRects.TryGetValue(player.GroundId, out var oldR) &&
                cur.TryGetValue(player.GroundId, out var newR))
            {
                if (player.GroundId != rideAlongId)
                {
                    rideAlongId = player.GroundId;
                    rideAlongRemainder = PointF.Empty;
                }
                rideAlongRemainder = new PointF(
                    rideAlongRemainder.X + (newR.Left - oldR.Left) / s,
                    rideAlongRemainder.Y + (newR.Top - oldR.Top) / s);
                int rideX = (int)Math.Round(rideAlongRemainder.X, MidpointRounding.ToEven);
                int rideY = (int)Math.Round(rideAlongRemainder.Y, MidpointRounding.ToEven);
                rideAlongRemainder = new PointF(rideAlongRemainder.X - rideX, rideAlongRemainder.Y - rideY);
                if (rideX != 0 || rideY != 0)
                    player.Pos = new PointF(player.Pos.X + rideX, player.Pos.Y + rideY);
            }
            else rideAlongId = IntPtr.Zero;

            lastRects.Clear();
            foreach (var kv in cur) lastRects[kv.Key] = kv.Value;
            player.Solids = solids;
        }

        void ApplyEdgeWrap(PointF previous)
        {
            if (edgeWrapMode == 0 || player.IsDead || player.IsRespawning ||
                player.BeingDragged || monitorGameBounds.Count == 0) return;

            RectangleF source = RectangleF.Empty;
            foreach (RectangleF monitor in monitorGameBounds)
                if (previous.X >= monitor.Left && previous.X <= monitor.Right &&
                    previous.Y >= monitor.Top && previous.Y <= monitor.Bottom)
                {
                    source = monitor;
                    break;
                }
            if (source.IsEmpty)
            {
                float nearestDistance = float.MaxValue;
                foreach (RectangleF monitor in monitorGameBounds)
                {
                    float nearestX = Math.Max(monitor.Left, Math.Min(monitor.Right, previous.X));
                    float nearestY = Math.Max(monitor.Top, Math.Min(monitor.Bottom, previous.Y));
                    float dx = previous.X - nearestX, dy = previous.Y - nearestY;
                    float distance = dx * dx + dy * dy;
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        source = monitor;
                    }
                }
                float reach = EdgeWrapMargin + 8f;
                if (nearestDistance > reach * reach) return;
            }

            bool OnAnyMonitor(float x, float y)
            {
                foreach (RectangleF monitor in monitorGameBounds)
                    if (x >= monitor.Left && x < monitor.Right &&
                        y >= monitor.Top && y < monitor.Bottom) return true;
                return false;
            }

            float offsetX = 0f, offsetY = 0f;
            if ((edgeWrapMode & 1) != 0)
            {
                float sampleY = Math.Max(source.Top, Math.Min(source.Bottom - 0.01f,
                    player.Pos.Y - player.CurrentHitHeight * 0.5f));
                if (player.Speed.X < 0f && player.Pos.X <= source.Left - EdgeWrapMargin &&
                    !OnAnyMonitor(source.Left - 0.01f, sampleY))
                {
                    float overshoot = source.Left - EdgeWrapMargin - player.Pos.X;
                    offsetX = source.Right + EdgeWrapMargin - overshoot - player.Pos.X;
                }
                else if (player.Speed.X > 0f && player.Pos.X >= source.Right + EdgeWrapMargin &&
                    !OnAnyMonitor(source.Right + 0.01f, sampleY))
                {
                    float overshoot = player.Pos.X - source.Right - EdgeWrapMargin;
                    offsetX = source.Left - EdgeWrapMargin + overshoot - player.Pos.X;
                }
            }
            if ((edgeWrapMode & 2) != 0)
            {
                float sampleX = Math.Max(source.Left, Math.Min(source.Right - 0.01f, player.Pos.X));
                if (player.Speed.Y < 0f && player.Pos.Y <= source.Top - EdgeWrapMargin &&
                    !OnAnyMonitor(sampleX, source.Top - 0.01f))
                {
                    float overshoot = source.Top - EdgeWrapMargin - player.Pos.Y;
                    offsetY = source.Bottom + EdgeWrapMargin - overshoot - player.Pos.Y;
                }
                else if (player.Speed.Y > 0f && player.Pos.Y >= source.Bottom + EdgeWrapMargin &&
                    !OnAnyMonitor(sampleX, source.Bottom + 0.01f))
                {
                    float overshoot = player.Pos.Y - source.Bottom - EdgeWrapMargin;
                    offsetY = source.Top - EdgeWrapMargin + overshoot - player.Pos.Y;
                }
            }
            player.WrapBy(offsetX, offsetY);
        }

        /// <summary>Maximized, or covering a whole monitor the way borderless fullscreen does.</summary>
        /// <remarks>
        /// A window this size offers nothing to stand on but its own outline around the screen,
        /// and it is usually what the user is actually working in or watching, so treating it
        /// as a platform is mostly in the way.
        /// </remarks>
        static bool IsMaximizedOrFullscreen(IntPtr hwnd, in Win32.RECT r)
        {
            // Maximized frames stop at the working area, so only IsZoomed catches those;
            // fullscreen ones are not zoomed but cover their monitor exactly.
            if (Win32.IsZoomed(hwnd)) return true;
            Rectangle monitor = Screen.FromHandle(hwnd).Bounds;
            return r.Left <= monitor.Left && r.Top <= monitor.Top &&
                   r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
        }

        /// <summary>Four hollow window edges (physical-pixel coords), thickness WindowBorderPx.</summary>
        IEnumerable<Win32.RECT> WindowEdges(Win32.RECT r)
        {
            // Never thinner than one game pixel: a border that rounded away to nothing on the
            // grid would stop being a platform at all.
            int b = Math.Max(WindowBorderPx, GameScale);
            yield return new Win32.RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Top + b };                 // top
            yield return new Win32.RECT { Left = r.Left, Top = r.Bottom - b, Right = r.Right, Bottom = r.Bottom };          // bottom
            yield return new Win32.RECT { Left = r.Left, Top = r.Top + b, Right = r.Left + b, Bottom = r.Bottom - b };      // left
            yield return new Win32.RECT { Left = r.Right - b, Top = r.Top + b, Right = r.Right, Bottom = r.Bottom - b };    // right
        }

        /// <summary>Subtract occluder rects from rectangle a; return non-overlapping remainders.</summary>
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

        // ================= Rendering =================
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

            // 1x game-pixel buffer: integer coords land on pixels (no subpixel drift); then integer nearest-neighbor upscale
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

                if (player.IsPreDeath)
                {
                    DrawPreDeath(g, camX, camY);
                }
                else if (player.IsDead)
                {
                    DrawDeathEffect(g, camX, camY, player.DeathPosition,
                        player.DeathColor, player.DeathPercent);
                }
                else if (player.IsRespawning)
                {
                    DrawDeathEffect(g, camX, camY, player.RespawnEffectPosition,
                        player.RespawnColor, player.RespawnPercent);
                }
                else
                {
                    // Draw hair behind the body (hair first, body on top).
                    // wakeUp frames already include full hair (curled sleep pose); do not overlay simulated hair
                    if (animator.CurrentId != "wakeUp")
                    {
                        DrawCatTail(g, camX, camY);
                        DrawHair(g, camX, camY);
                    }
                    DrawBody(g, bodyAnchorX, bodyAnchorY);
                    DrawSweat(g, bodyAnchorX, bodyAnchorY);
                    // Glider.Depth is -5 in vanilla, in front of Player.Depth 0.
                    // Drawing held gliders in this layer also avoids rebuilding a
                    // separately uploaded rotated stamp on every carry frame.
                    DrawGliders(g, camX, camY, heldOnly: true);
                    DrawTheos(g, camX, camY, heldOnly: true);
                }

                if (ParticlesEnabled) particles.Draw(g, camX, camY);
                // SlashFx and TrailManager are core dash visuals; not gated by the particles toggle.
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
            // TheoCrystal.Depth=100, behind Player.Depth=0 while unheld.
            foreach (TheoCrystal theo in theos)
            {
                // IsDying: TheoCrystal.Die hides the sprite, leaving only the burst.
                if (theo.IsHeld || theo.Removed || theo.IsDying || trailCount >= trailStamps.Length) continue;
                // TheoCrystal.orig_ctor sets sprite.Scale.X = -1.
                Bitmap stamp = Sprites.Get(theo.FrameId, true);
                if (stamp != null)
                    // Sprites.xml: theo_crystal Origin=(32,42), ten pixels below
                    // the 64x64 frame center used by TrailStamp.
                    trailStamps[trailCount++] = new TrailStamp(stamp, theo.Pos.X, theo.Pos.Y - 10f, 1f);
            }
            int foregroundStart = trailCount;
            foreach (Glider glider in gliders)
            {
                if (glider.IsHeld) continue;
                if (trailCount >= trailStamps.Length) break;
                Bitmap stamp = GetGliderStamp(glider);
                if (stamp != null)
                    trailStamps[trailCount++] = new TrailStamp(stamp, glider.Pos.X, glider.Pos.Y, 1f);
            }
            foreach (Seeker seeker in seekers)
            {
                foreach (SeekerTrail trail in seeker.Trails)
                {
                    if (trailCount >= trailStamps.Length) break;
                    trail.Stamp ??= CreateSeekerStamp(trail.FrameId, trail.Facing,
                        trail.ScaleX, trail.ScaleY, true);
                    float remain = 1f - trail.Age / .5f;
                    trailStamps[trailCount++] = new TrailStamp(trail.Stamp,
                        trail.Position.X, trail.Position.Y, .75f * remain * remain * remain);
                }
                if (trailCount >= trailStamps.Length) break;
                if (seeker.ShockwaveFrameId != null && trailCount < trailStamps.Length)
                {
                    Bitmap shockwave = Sprites.Get(seeker.ShockwaveFrameId, false);
                    if (shockwave != null)
                        trailStamps[trailCount++] = new TrailStamp(shockwave, seeker.Pos.X, seeker.Pos.Y, 1f);
                }
                Bitmap seekerStamp = GetSeekerStamp(seeker);
                if (seekerStamp != null && trailCount < trailStamps.Length)
                    trailStamps[trailCount++] = new TrailStamp(seekerStamp,
                        seeker.Pos.X + seeker.Shake.X, seeker.Pos.Y + seeker.Shake.Y, 1f);
            }
            seekerParticles.AppendPointStamps(trailStamps, ref trailCount, seekerParticleBitmaps);
            if (hitboxesEnabled) AppendActorDebugStamps(ref trailCount);
            presenter.Present(small, left, top, trailStamps, trailCount, foregroundStart);

            lock (gliderWindowLock)
            {
                foreach (var pair in gliderWindows)
                {
                    if (!pair.Value.IsHandleCreated) continue;
                    int jellyLeft = (int)Math.Round((pair.Key.Pos.X - 10f) * s);
                    int jellyTop = (int)Math.Round((pair.Key.Pos.Y - 16f) * s);
                    Win32.SetWindowPos(pair.Value.Handle, IntPtr.Zero, jellyLeft, jellyTop,
                        20 * s, 22 * s, Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
                }
            }
            lock (seekerWindowLock)
            {
                foreach (var pair in seekerWindows)
                {
                    if (!pair.Value.IsHandleCreated) continue;
                    int seekerLeft = (int)Math.Round((pair.Key.Pos.X - 16f) * s);
                    int seekerTop = (int)Math.Round((pair.Key.Pos.Y - 16f) * s);
                    Win32.SetWindowPos(pair.Value.Handle, IntPtr.Zero, seekerLeft, seekerTop,
                        32 * s, 32 * s, Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
                }
            }
            lock (theoWindowLock)
            {
                foreach (var pair in theoWindows)
                {
                    if (!pair.Value.IsHandleCreated) continue;
                    int theoLeft = (int)Math.Round((pair.Key.Pos.X - 8f) * s);
                    int theoTop = (int)Math.Round((pair.Key.Pos.Y - 16f) * s);
                    Win32.SetWindowPos(pair.Value.Handle, IntPtr.Zero, theoLeft, theoTop,
                        16 * s, 22 * s, Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
                }
            }

            // Only this tiny invisible input HWND moves. Rendering belongs to the fixed
            // click-through composition host, so window movement cannot shake pixels.
            int inputLeft = (int)Math.Round(player.Pos.X * s) - 12 * s;
            int inputTop = (int)Math.Round(player.Pos.Y * s) - 30 * s;
            Win32.SetWindowPos(Handle, IntPtr.Zero, inputLeft, inputTop,
                24 * s, 33 * s, Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
            // Log position + speed + state every 5 seconds
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

        // Vanilla SlashFx: 4 frames x 0.1s, spawned at player Center, moves 8px/s along dash direction.
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

        void DrawGliders(Graphics g, float camX, float camY, bool heldOnly = false)
        {
            foreach (Glider glider in gliders)
            {
                if (heldOnly && !glider.IsHeld) continue;
                Bitmap frame = Sprites.Get(glider.FrameId, false);
                if (frame == null) continue;
                var state = g.Save();
                g.TranslateTransform(SnapPx(glider.Pos.X - camX), SnapPx(glider.Pos.Y - camY));
                g.RotateTransform(glider.Rotation * 180f / (float)Math.PI);
                float w = 48f * glider.ScaleX, h = 48f * glider.ScaleY;
                // Vanilla Sprites.xml: <Justify x="0.5" y="0.58"/>.
                // Rotation and scale are around that origin, not the frame center.
                float x = -24f * glider.ScaleX;
                float y = -27.84f * glider.ScaleY;
                // Glider.Render calls DrawSimpleOutline before drawing the sprite.
                Sprites.DrawSilhouette(g, frame, Color.Black, x - 1f, y, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x + 1f, y, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x, y - 1f, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x, y + 1f, w, h);
                g.DrawImage(frame, x, y, w, h);
                g.Restore(state);
            }
        }

        void DrawTheos(Graphics g, float camX, float camY, bool heldOnly = false)
        {
            foreach (TheoCrystal theo in theos)
            {
                if (theo.Removed) continue;
                if (theo.IsDying)
                {
                    // TheoCrystal.Die hides the sprite and leaves a DeathEffect in forest
                    // green -- the burst Madeline dies in, which DrawDeathEffect already
                    // draws.  A loose crystal is otherwise presented as its own stamp, but
                    // the burst belongs on the canvas with every other effect, so it is drawn
                    // here whether he was being carried or not.
                    DrawDeathEffect(g, camX, camY, theo.DeathPosition, Color.ForestGreen, theo.DeathPercent);
                    continue;
                }
                if (heldOnly && !theo.IsHeld) continue;
                Bitmap frame = Sprites.Get(theo.FrameId, true);
                if (frame == null) continue;
                // Sprites.xml: <Origin x="32" y="42"/>.
                g.DrawImage(frame, SnapPx(theo.Pos.X - camX) - 32f,
                    SnapPx(theo.Pos.Y - camY) - 42f, 64f, 64f);
            }
        }

        void DrawPreDeath(Graphics g, float camX, float camY)
        {
            float dx = player.DeathBodyPosition.X - player.Pos.X;
            float dy = player.DeathBodyPosition.Y - player.Pos.Y;
            DrawHair(g, camX - dx, camY - dy,
                player.DeathBodyFrameId != null && player.DeathBodyFrameId.EndsWith("00")
                    ? Color.White : player.DeathColor);
            Bitmap frame = Sprites.Get(player.DeathBodyFrameId, player.Facing < 0);
            if (frame == null) return;
            var state = g.Save();
            g.TranslateTransform(SnapPx(player.DeathBodyPosition.X - camX),
                SnapPx(player.DeathBodyPosition.Y - camY));
            g.RotateTransform(player.DeathBodyRotation * 180f / (float)Math.PI);
            g.ScaleTransform(player.DeathBodyScale, player.DeathBodyScale);
            g.DrawImage(frame, -16f, -32f, 32f, 32f);
            g.Restore(state);
        }

        Bitmap GetGliderStamp(Glider glider)
        {
            Bitmap frame = Sprites.Get(glider.FrameId, false);
            if (frame == null) return null;
            int rotation = (int)Math.Round(glider.Rotation * 180f / Math.PI);
            int scaleX = (int)Math.Round(glider.ScaleX * 100f);
            int scaleY = (int)Math.Round(glider.ScaleY * 100f);
            if (gliderStampCache.TryGetValue(glider, out var cached) &&
                cached.FrameId == glider.FrameId && cached.Rotation == rotation &&
                cached.ScaleX == scaleX && cached.ScaleY == scaleY)
                return cached.Bitmap;

            var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;
                var state = g.Save();
                g.TranslateTransform(32f, 32f);
                g.RotateTransform(rotation);
                float w = 48f * glider.ScaleX, h = 48f * glider.ScaleY;
                float x = -24f * glider.ScaleX;
                float y = -27.84f * glider.ScaleY;
                Sprites.DrawSilhouette(g, frame, Color.Black, x - 1f, y, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x + 1f, y, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x, y - 1f, w, h);
                Sprites.DrawSilhouette(g, frame, Color.Black, x, y + 1f, w, h);
                g.DrawImage(frame, x, y, w, h);
                g.Restore(state);
            }
            if (cached == null)
            {
                cached = new GliderStampCache();
                gliderStampCache[glider] = cached;
            }
            // The presenter has already uploaded the previous frame on the prior
            // Present. It no longer needs the CPU bitmap after this replacement.
            cached.Bitmap?.Dispose();
            cached.FrameId = glider.FrameId;
            cached.Rotation = rotation;
            cached.ScaleX = scaleX;
            cached.ScaleY = scaleY;
            cached.Bitmap = bitmap;
            return bitmap;
        }

        Bitmap GetSeekerStamp(Seeker seeker)
        {
            string frameId = seeker.FrameId;
            int sx = (int)Math.Round(seeker.RenderScaleX * 100f);
            int sy = (int)Math.Round(seeker.RenderScaleY * 100f);
            int facing = seeker.SpriteFacing;
            if (seekerStampCache.TryGetValue(seeker, out GliderStampCache cached) &&
                cached.FrameId == frameId && cached.ScaleX == sx && cached.ScaleY == sy &&
                cached.Rotation == facing) return cached.Bitmap;
            Bitmap bitmap = CreateSeekerStamp(frameId, facing, seeker.RenderScaleX, seeker.RenderScaleY, false);
            cached ??= new GliderStampCache();
            cached.Bitmap?.Dispose();
            cached.FrameId = frameId; cached.ScaleX = sx; cached.ScaleY = sy;
            cached.Rotation = facing; cached.Bitmap = bitmap;
            seekerStampCache[seeker] = cached;
            return bitmap;
        }

        static Bitmap CreateSeekerStamp(string frameId, int facing, float scaleX, float scaleY, bool trail)
        {
            Bitmap frame = Sprites.Get(frameId, facing < 0);
            if (frame == null) return null;
            var bitmap = new Bitmap(128, 128, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;
                float w = frame.Width * scaleX, h = frame.Height * scaleY;
                float x = 64f - w / 2f, y = 64f - h / 2f;
                if (trail) Sprites.DrawSilhouette(g, frame, Seeker.TrailColor, x, y, w, h);
                else g.DrawImage(frame, x, y, w, h);
            }
            return bitmap;
        }

        void DrawBody(Graphics g, float anchorX, float anchorY)
        {
            // Body (squash/stretch anchored at foot center); rect snapped to integer game pixels
            bool flip = player.Facing < 0;
            var frame = Sprites.Get(animator.CurrentFrameId, flip);
            if (frame != null)
            {
                float sx = player.SpriteScaleX, sy = player.SpriteScaleY;
                float x = SnapPx(anchorX - 16 * sx), y = SnapPx(anchorY - 32 * sy);
                float w = SnapPx(32 * sx), h = SnapPx(32 * sy);
                // Vanilla low-stamina look: flash body red/white every 0.05s.
                if (player.IsLowStamina && tiredFlash)
                    Sprites.DrawTinted(g, frame, Color.Red, x, y, w, h);
                else
                    g.DrawImage(frame, x, y, w, h);
            }
        }

        void DrawDeathEffect(Graphics g, float camX, float camY,
            PointF effectPosition, Color effectColor, float effectPercent)
        {
            Bitmap texture = Sprites.Get("hair00", false);
            if (texture == null) return;
            float ease = effectPercent;
            float cubeOut = 1f - (float)Math.Pow(1f - ease, 3f);
            float radius = cubeOut * 24f;
            float scale = ease < 0.5f
                ? 0.5f + ease
                : 1f - (float)Math.Pow((ease - 0.5f) * 2f, 3f);
            Color color = ((int)Math.Floor(ease * 10f) & 1) == 0
                ? effectColor : Color.White;
            float centerX = effectPosition.X - camX;
            float centerY = effectPosition.Y - camY;
            for (int i = 0; i < 8; i++)
            {
                float angle = ((float)i / 8f + ease * 0.25f) * (float)Math.PI * 2f;
                float x = SnapPx(centerX + (float)Math.Cos(angle) * radius);
                float y = SnapPx(centerY + (float)Math.Sin(angle) * radius);
                float w = SnapEven(10f * scale), h = SnapEven(10f * scale);
                Sprites.DrawTinted(g, texture, Color.Black, x - w / 2f - 1f, y - h / 2f, w, h);
                Sprites.DrawTinted(g, texture, Color.Black, x - w / 2f + 1f, y - h / 2f, w, h);
                Sprites.DrawTinted(g, texture, Color.Black, x - w / 2f, y - h / 2f - 1f, w, h);
                Sprites.DrawTinted(g, texture, Color.Black, x - w / 2f, y - h / 2f + 1f, w, h);
            }
            for (int i = 0; i < 8; i++)
            {
                float angle = ((float)i / 8f + ease * 0.25f) * (float)Math.PI * 2f;
                float x = SnapPx(centerX + (float)Math.Cos(angle) * radius);
                float y = SnapPx(centerY + (float)Math.Sin(angle) * radius);
                float w = SnapEven(10f * scale), h = SnapEven(10f * scale);
                Sprites.DrawTinted(g, texture, color, x - w / 2f, y - h / 2f, w, h);
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

        void DrawHair(Graphics g, float camX, float camY, Color? colorOverride = null)
        {
            var hair = player.Hair;
            Color color = colorOverride ?? player.HairColor;
            bool flip = player.Facing < 0;
            var blob = Sprites.Get("hair00", false);
            // Bangs frame: pick from current anim frame facing meta (0 look-left / 1 center / 2 look-right); hair editor uses live values
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

            // Canvas coords (pixel-perfect: vanilla floors Nodes[0]; here each node snaps to integer game pixels,
            // integer upscale = integer physical pixels, avoiding subpixel blur)
            int hairCount = hair.ActiveCount;
            Span<PointF> pt = stackalloc PointF[PlayerHairSim.MaxCount];
            float rootScreenX = SnapPx(hair.Nodes[0].X - camX);
            float rootScreenY = SnapPx(hair.Nodes[0].Y - camY);
            for (int i = 0; i < hairCount; i++)
                pt[i] = new PointF(
                    rootScreenX + hair.Nodes[i].X - hair.Nodes[0].X,
                    rootScreenY + hair.Nodes[i].Y - hair.Nodes[0].Y);

            // Black outline (vanilla: ±1px in four directions)
            for (int i = 0; i < hairCount; i++)
            {
                float sc = HairSegmentScale(i, hairCount);
                var tex = i == 0 ? bangs : blob;
                float w = 10f * sc * Math.Abs(player.SpriteScaleX);
                float h = 10f * sc;
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2 - 1, pt[i].Y - h / 2, w, h);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2 + 1, pt[i].Y - h / 2, w, h);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2, pt[i].Y - h / 2 - 1, w, h);
                DrawTintedSafe(g, tex, Color.Black, pt[i].X - w / 2, pt[i].Y - h / 2 + 1, w, h);
            }
            // Body fill (back to front; bangs last)
            for (int i = hairCount - 1; i >= 0; i--)
            {
                float sc = HairSegmentScale(i, hairCount);
                var tex = i == 0 ? bangs : blob;
                float w = 10f * sc * Math.Abs(player.SpriteScaleX);
                float h = 10f * sc;
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
            List<Solid> allSolids = player.Solids;
            foreach (var solid in allSolids)
                DrawSolidHitbox(g, allSolids, solid, camX, camY, solidBrush);
            float playerHeight = player.Ducking ? 6f : 11f;
            using var playerBrush = new SolidBrush(Color.Lime);
            DrawHollowRect(g, player.Pos.X - 4f - camX, player.Pos.Y - playerHeight - camY,
                8f, playerHeight, playerBrush);
            DrawSeekerPaths(g, camX, camY);
        }

        void DrawSeekerPaths(Graphics g, float camX, float camY)
        {
            using var pathPen = new Pen(Color.Red, 1f);
            using var pathBrush = new SolidBrush(Color.Red);
            using var endpointBrush = new SolidBrush(Color.Green);
            foreach (Seeker seeker in seekers)
            {
                if (!seeker.DebugPathAttempted) continue;
                IReadOnlyList<PointF> path = seeker.DebugPath;
                if (seeker.DebugPathFound && path.Count > 0)
                {
                    PointF start = path[0];
                    for (int i = 1; i < path.Count; i++)
                    {
                        PointF next = path[i];
                        g.DrawLine(pathPen, SnapPx(start.X - camX), SnapPx(start.Y - camY),
                            SnapPx(next.X - camX), SnapPx(next.Y - camY));
                        g.FillRectangle(pathBrush, SnapPx(start.X - camX) - 2f,
                            SnapPx(start.Y - camY) - 2f, 4f, 4f);
                        start = next;
                    }
                    g.FillRectangle(pathBrush, SnapPx(start.X - camX) - 2f,
                        SnapPx(start.Y - camY) - 2f, 4f, 4f);
                }
                PointF from = PathfinderCellCenter(seeker.DebugPathStart);
                PointF to = PathfinderCellCenter(seeker.DebugPathEnd);
                g.FillRectangle(endpointBrush, SnapPx(from.X - camX) - 2f,
                    SnapPx(from.Y - camY) - 2f, 4f, 4f);
                g.FillRectangle(endpointBrush, SnapPx(to.X - camX) - 2f,
                    SnapPx(to.Y - camY) - 2f, 4f, 4f);
            }
        }

        static PointF PathfinderCellCenter(PointF point)
            => new PointF((float)Math.Floor(point.X / 8f) * 8f + 4f,
                (float)Math.Floor(point.Y / 8f) * 8f + 4f);

        void AppendActorDebugStamps(ref int trailCount)
        {
            gliderDebugStamp ??= CreateHoldableDebugStamp(glider: true);
            theoDebugStamp ??= CreateHoldableDebugStamp(glider: false);
            foreach (Glider glider in gliders)
            {
                if (trailCount >= trailStamps.Length) return;
                trailStamps[trailCount++] = new TrailStamp(gliderDebugStamp,
                    glider.Pos.X, glider.Pos.Y, 1f);
            }
            foreach (TheoCrystal theo in theos)
            {
                if (theo.Removed || trailCount >= trailStamps.Length) continue;
                trailStamps[trailCount++] = new TrailStamp(theoDebugStamp,
                    theo.Pos.X, theo.Pos.Y, 1f);
            }
            foreach (Seeker seeker in seekers)
            {
                if (trailCount >= trailStamps.Length) return;
                int variant = seeker.State == Seeker.StAttack && seeker.Speed.X > 0f ? 1 :
                    seeker.State == Seeker.StAttack && seeker.Speed.Y < 0f ? 2 : 0;
                seekerDebugStamps[variant] ??= CreateSeekerDebugStamp(variant);
                trailStamps[trailCount++] = new TrailStamp(seekerDebugStamps[variant],
                    seeker.Pos.X, seeker.Pos.Y, 1f);
            }
        }

        static Bitmap CreateHoldableDebugStamp(bool glider)
        {
            var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bitmap);
            using var physical = new SolidBrush(Color.Red);
            using var pickup = new SolidBrush(Color.Pink);
            DrawHollowRect(g, 28f, 22f, 8f, 10f, physical);
            DrawHollowRect(g, glider ? 22f : 24f, 16f,
                glider ? 20f : 16f, 22f, pickup);
            return bitmap;
        }

        static Bitmap CreateSeekerDebugStamp(int variant)
        {
            var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bitmap);
            using var attack = new SolidBrush(Color.Red);
            using var bounce = new SolidBrush(Color.Aqua);
            DrawHollowRect(g, 26f, 30f, 12f, 8f, attack);
            float x = variant == 1 ? 22f : 26f;
            float width = variant == 0 ? 12f : 16f;
            DrawHollowRect(g, x, 24f, width, 6f, bounce);
            return bitmap;
        }

        /// <summary>Outline a solid, minus the stretches another solid is pressed against.</summary>
        /// <remarks>
        /// Occlusion cuts a covered platform into pieces, and outlining each piece whole draws
        /// a line along every join between them.  A join is inside the shape rather than an
        /// edge of it, and it reads as the border of one window running on across the window
        /// behind it.  Border mode never showed this because its pieces are thin enough to be
        /// drawn as a single stroke, which is the case kept below.
        /// </remarks>
        static void DrawSolidHitbox(Graphics g, List<Solid> all, Solid s, float camX, float camY, Brush brush)
        {
            float width = s.R - s.L, height = s.B - s.T;
            if ((height <= 2 && width > height) || (width <= 2 && height > width))
            {
                DrawSolidHitbox(g, s.L - camX, s.T - camY, width, height, brush);
                return;
            }

            var covered = new List<PointF>();

            void Edge(bool horizontal, bool nearSide)
            {
                float at = horizontal ? (nearSide ? s.T : s.B) : (nearSide ? s.L : s.R);
                float from = horizontal ? s.L : s.T, to = horizontal ? s.R : s.B;
                covered.Clear();
                foreach (Solid q in all)
                {
                    // Does q fill the space immediately outside this edge?  A solid exactly
                    // level with it on the far side does; the solid itself never can.
                    bool presses = horizontal
                        ? (nearSide ? q.T < at && q.B >= at : q.B > at && q.T <= at)
                        : (nearSide ? q.L < at && q.R >= at : q.R > at && q.L <= at);
                    bool alongside = horizontal
                        ? q.L < s.R && q.R > s.L
                        : q.T < s.B && q.B > s.T;
                    if (presses && alongside)
                        covered.Add(horizontal
                            ? new PointF(Math.Max(q.L, from), Math.Min(q.R, to))
                            : new PointF(Math.Max(q.T, from), Math.Min(q.B, to)));
                }
                foreach (PointF span in ExposedSpans(from, to, covered))
                {
                    int length = Math.Max(1, (int)Math.Round(span.Y - span.X));
                    if (horizontal)
                        g.FillRectangle(brush, (int)Math.Round(span.X - camX),
                            (int)Math.Round(at - camY), length, 1);
                    else
                        g.FillRectangle(brush, (int)Math.Round(at - camX),
                            (int)Math.Round(span.X - camY), 1, length);
                }
            }

            Edge(horizontal: true, nearSide: true);
            Edge(horizontal: true, nearSide: false);
            Edge(horizontal: false, nearSide: true);
            Edge(horizontal: false, nearSide: false);
        }

        /// <summary>The parts of [from,to] left over once the covered spans are removed.</summary>
        static List<PointF> ExposedSpans(float from, float to, List<PointF> covered)
        {
            var spans = new List<PointF> { new PointF(from, to) };
            foreach (PointF cover in covered)
            {
                var next = new List<PointF>(spans.Count + 1);
                foreach (PointF span in spans)
                {
                    if (cover.Y <= span.X || cover.X >= span.Y) { next.Add(span); continue; }
                    if (span.X < cover.X) next.Add(new PointF(span.X, cover.X));
                    if (span.Y > cover.Y) next.Add(new PointF(cover.Y, span.Y));
                }
                spans = next;
                if (spans.Count == 0) break;
            }
            return spans;
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

        // Pixel-perfect: snap float game pixels to integers (even rounding keeps w/2 integer so edges stay on-pixel)
        static float SnapPx(float v) => (float)Math.Round(v);
        static float SnapEven(float v) => (float)(Math.Round(v / 2f) * 2f);

        static void DrawTintedSafe(Graphics g, Bitmap tex, Color c, float x, float y, float w, float h, float alpha = 1f)
            => Sprites.DrawTinted(g, tex, c, x, y, w, h, alpha);

        // ================= Mouse drag =================
        protected override void WndProc(ref Message m)
        {
            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_MOUSEMOVE = 0x0200;
            const int WM_LBUTTONUP = 0x0202;
            const int WM_RBUTTONUP = 0x0205;

            switch (m.Msg)
            {
                case WM_LBUTTONDOWN:
                    // Normally removing WS_EX_NOACTIVATE already activates on mouse-down; explicit
                    // Activate also covers odd activation behavior from some layered windows / window managers.
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
                        // Whole game pixels only: the physics grid the climb checks rely on.
                        player.Pos = new PointF(
                            ToGamePixels(cur.X + dragGrabOffset.X),
                            ToGamePixels(cur.Y + dragGrabOffset.Y));
                    }
                    break;
                case WM_LBUTTONUP:
                    if (dragging)
                    {
                        dragging = false;
                        player.BeingDragged = false;
                        // Throw: inherit mouse velocity (convert to game units, clamp)
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

        // ================= Tray =================

        void SaveSettings()
        {
            settings.Scale = pendingScale > 0 ? pendingScale : GameScale;
            settings.InputEnabled = InputEnabled;
            settings.PadInputEnabled = PadInputEnabled;
            settings.InputWhenUnfocused = InputWhenUnfocused;
            settings.AlwaysOnTop = AlwaysOnTop;
            settings.ParticlesEnabled = ParticlesEnabled;
            settings.FreezeFramesEnabled = player.FreezeFramesEnabled;
            settings.InfiniteStamina = player.InfiniteStamina;
            settings.Invincible = player.Invincible;
            settings.DashMode = player.DashMode;
            settings.Language = Loc.CurrentCode;
            settings.Skin = skinManager.Active?.Id ?? SkinManager.DefaultId;
            settings.CatTailEnabled = catTailEnabled;
            settings.CatBangsEnabled = catBangsEnabled;
            settings.CustomHairColorsEnabled = customHairColorsEnabled;
            settings.HairColor0 = RgbValue(customHairColors[0]);
            settings.HairColor1 = RgbValue(customHairColors[1]);
            settings.HairColor2 = RgbValue(customHairColors[2]);
            settings.SpeedometerMode = speedometerMode;
            settings.HitboxesEnabled = hitboxesEnabled;
            settings.DreamBlockMode = dreamBlockMode;
            settings.IgnoreMaximizedWindows = ignoreMaximizedWindows;
            settings.RespawnReversalEnabled = player.RespawnReversalEnabled;
            settings.EdgeWrapMode = edgeWrapMode;
            settings.ElytraEnabled = player.ElytraEnabled;
            settings.SfxMode = soundEffects.Mode;
            settings.SfxVolume = soundEffects.Volume;
            settings.SurfaceSoundIndex = player.NormalSurfaceSoundIndex;
            settings.Save();
        }

        /// <summary>Settle which Celeste the artwork and sound come from, before they are read.</summary>
        /// <remarks>
        /// It is a setting rather than only a search so that a copy in an unusual place, or one
        /// of several, can be named once and kept. The first run fills it in with whatever
        /// looking around turns up, which is the whole answer for an ordinary Steam install;
        /// only when that finds nothing is the user asked, and a bundled build carries the
        /// game's files beside it and so is never asked at all.
        /// </remarks>
        void ResolveCelesteInstall()
        {
            CelesteInstall.Chosen = settings.CelestePath;
            string found = CelesteInstall.Directory;   // the setting first, then the usual places
            if (found == null && !CelesteInstall.HasBundledContent)
            {
                if (MessageBox.Show(Loc.T("Celeste.NotFound"), Loc.T("App.Title"),
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
                    found = AskForCelesteFolder();
                if (found == null)
                {
                    Log("no Celeste install: running without her sprites or sounds");
                    MessageBox.Show(Loc.T("Celeste.Without"), Loc.T("App.Title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            if (found == null || found.Equals(settings.CelestePath, StringComparison.OrdinalIgnoreCase))
                return;
            settings.CelestePath = found;
            CelesteInstall.Chosen = found;
            settings.Save();
            Log("Celeste install: " + found);
        }

        /// <summary>Ask for the folder Celeste.exe is in, until it is one or the user gives up.</summary>
        string AskForCelesteFolder()
        {
            while (true)
            {
                using var dialog = new FolderBrowserDialog
                {
                    Description = Loc.T("Celeste.PickFolder"),
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false,
                    SelectedPath = CelesteInstall.Directory ?? settings.CelestePath ?? ""
                };
                if (dialog.ShowDialog() != DialogResult.OK) return null;
                if (CelesteInstall.IsInstall(dialog.SelectedPath))
                    return System.IO.Path.GetFullPath(dialog.SelectedPath);
                if (MessageBox.Show(Loc.T("Celeste.NoExeThere"), Loc.T("App.Title"),
                        MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry)
                    return null;
            }
        }

        void ChangeLanguage(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            if (Loc.CurrentCode.Equals(code, StringComparison.OrdinalIgnoreCase)) return;
            Loc.SetLanguage(code);
            SaveSettings();
            // Rebuild after the menu click unwinds so we do not dispose the menu WinForms is still dispatching.
            BeginInvoke(new Action(() =>
            {
                var old = trayMenu;
                trayMenu = BuildMenu();
                tray.ContextMenuStrip = trayMenu;
                tray.Text = Loc.T("App.Name");
                old?.Dispose();
            }));
        }

        string ActionName(PetAction action)
        {
            return action switch
            {
                PetAction.Left => Loc.T("Action.Left"),
                PetAction.Right => Loc.T("Action.Right"),
                PetAction.Up => Loc.T("Action.Up"),
                PetAction.Down => Loc.T("Action.Down"),
                PetAction.Jump => Loc.T("Action.Jump"),
                PetAction.Dash => Loc.T("Action.Dash"),
                PetAction.Grab => Loc.T("Action.Grab"),
                PetAction.CrouchDash => Loc.T("Action.CrouchDash"),
                PetAction.DeployElytra => Loc.T("Action.DeployElytra"),
                _ => action.ToString()
            };
        }

        string KeyName(int virtualKey)
            => virtualKey == 0 ? Loc.T("Keys.Unbound") : ((Keys)virtualKey).ToString();

        void RefreshBindingItems(ToolStripMenuItem actionItem, PetAction action)
        {
            int[] values = bindings.Get(action);
            for (int i = 0; i < 3; i++)
                actionItem.DropDownItems[i].Text = (i + 1) + ": " + KeyName(values[i]);
        }

        ToolStripMenuItem BuildBindingsMenu()
        {
            var root = new ToolStripMenuItem(Loc.T("Keys.Root"));
            foreach (PetAction action in KeyBindings.Actions)
            {
                var actionItem = new ToolStripMenuItem(ActionName(action));
                int[] values = bindings.Get(action);
                for (int i = 0; i < 3; i++)
                {
                    int slot = i;
                    var slotItem = new ToolStripMenuItem((i + 1) + ": " + KeyName(values[i]));
                    slotItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Keys.Change"), null, (_, __) =>
                    {
                        using var capture = new KeyCaptureDialog(
                            Loc.Format("Keys.BindTitle", ActionName(action)),
                            Loc.T("Keys.CaptureHint"));
                        if (capture.ShowDialog(this) == DialogResult.OK)
                        {
                            bindings.Set(action, slot, capture.CapturedKey);
                            RefreshBindingItems(actionItem, action);
                        }
                    }));
                    slotItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Keys.Unbind"), null, (_, __) =>
                    {
                        bindings.Set(action, slot, 0);
                        RefreshBindingItems(actionItem, action);
                    }));
                    actionItem.DropDownItems.Add(slotItem);
                }
                root.DropDownItems.Add(actionItem);
            }
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Keys.ResetDefaults"), null, (_, __) =>
            {
                bindings.ResetDefaults();
                // Rebuild so every open slot label reflects the reset values.
                RebuildTrayMenu();
            }));
            return root;
        }

        static string PadButtonName(PadButton button)
        {
            return button switch
            {
                PadButton.None => Loc.T("Keys.Unbound"),
                PadButton.A => Loc.T("Pad.A"),
                PadButton.B => Loc.T("Pad.B"),
                PadButton.X => Loc.T("Pad.X"),
                PadButton.Y => Loc.T("Pad.Y"),
                PadButton.LeftShoulder => Loc.T("Pad.LeftShoulder"),
                PadButton.RightShoulder => Loc.T("Pad.RightShoulder"),
                PadButton.LeftTrigger => Loc.T("Pad.LeftTrigger"),
                PadButton.RightTrigger => Loc.T("Pad.RightTrigger"),
                PadButton.LeftStick => Loc.T("Pad.LeftStick"),
                PadButton.RightStick => Loc.T("Pad.RightStick"),
                PadButton.Start => Loc.T("Pad.Start"),
                PadButton.Back => Loc.T("Pad.Back"),
                PadButton.DPadUp => Loc.T("Pad.DPadUp"),
                PadButton.DPadDown => Loc.T("Pad.DPadDown"),
                PadButton.DPadLeft => Loc.T("Pad.DPadLeft"),
                PadButton.DPadRight => Loc.T("Pad.DPadRight"),
                PadButton.LeftThumbstickUp => Loc.T("Pad.LeftStickUp"),
                PadButton.LeftThumbstickDown => Loc.T("Pad.LeftStickDown"),
                PadButton.LeftThumbstickLeft => Loc.T("Pad.LeftStickLeft"),
                PadButton.LeftThumbstickRight => Loc.T("Pad.LeftStickRight"),
                PadButton.RightThumbstickUp => Loc.T("Pad.RightStickUp"),
                PadButton.RightThumbstickDown => Loc.T("Pad.RightStickDown"),
                PadButton.RightThumbstickLeft => Loc.T("Pad.RightStickLeft"),
                PadButton.RightThumbstickRight => Loc.T("Pad.RightStickRight"),
                _ => button.ToString()
            };
        }

        void RefreshPadBindingItems(ToolStripMenuItem actionItem, PetAction action)
        {
            PadButton[] values = padBindings.Get(action);
            for (int i = 0; i < 3; i++)
                actionItem.DropDownItems[i].Text = (i + 1) + ": " + PadButtonName(values[i]);
        }

        ToolStripMenuItem BuildPadBindingsMenu()
        {
            var root = new ToolStripMenuItem(Loc.T("Pad.Root"));
            foreach (PetAction action in KeyBindings.Actions)
            {
                var actionItem = new ToolStripMenuItem(ActionName(action));
                PadButton[] values = padBindings.Get(action);
                for (int i = 0; i < 3; i++)
                {
                    int slot = i;
                    var slotItem = new ToolStripMenuItem((i + 1) + ": " + PadButtonName(values[i]));
                    slotItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Keys.Change"), null, (_, __) =>
                    {
                        using var capture = new PadCaptureDialog(
                            Loc.Format("Keys.BindTitle", ActionName(action)),
                            Loc.T("Pad.CaptureHint"));
                        if (capture.ShowDialog(this) == DialogResult.OK)
                        {
                            padBindings.Set(action, slot, capture.CapturedButton);
                            RefreshPadBindingItems(actionItem, action);
                        }
                    }));
                    slotItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Keys.Unbind"), null, (_, __) =>
                    {
                        padBindings.Set(action, slot, PadButton.None);
                        RefreshPadBindingItems(actionItem, action);
                    }));
                    actionItem.DropDownItems.Add(slotItem);
                }
                root.DropDownItems.Add(actionItem);
            }
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Keys.ResetDefaults"), null, (_, __) =>
            {
                padBindings.ResetDefaults();
                // Rebuild so every open slot label reflects the reset values.
                RebuildTrayMenu();
            }));
            return root;
        }

        /// <summary>Rebuild after the menu click unwinds so WinForms is not mid-dispatch on the old strip.</summary>
        void RebuildTrayMenu()
        {
            BeginInvoke(new Action(() =>
            {
                var old = trayMenu;
                trayMenu = BuildMenu();
                tray.ContextMenuStrip = trayMenu;
                old?.Dispose();
            }));
        }

        ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();

            var languageItem = new ToolStripMenuItem(Loc.T("Menu.Language"));
            foreach (LanguageInfo lang in Loc.Languages)
            {
                string code = lang.Code;
                var choice = new ToolStripMenuItem(lang.NativeName)
                {
                    Checked = Loc.CurrentCode.Equals(code, StringComparison.OrdinalIgnoreCase),
                    Tag = code
                };
                choice.Click += (_, __) => ChangeLanguage(code);
                languageItem.DropDownItems.Add(choice);
            }
            menu.Items.Add(languageItem);

            var skinItem = new ToolStripMenuItem(Loc.T("Menu.Skin"));
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
            AddSkinChoice(SkinManager.DefaultId, Loc.T("Skin.Default"));
            foreach (var skin in skinManager.Skins) AddSkinChoice(skin.Id, skin.DisplayName);
            skinItem.DropDownItems.Add(new ToolStripSeparator());
            skinItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Skin.Refresh"), null, (_, __) =>
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
            skinItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Skin.OpenFolder"), null, (_, __) =>
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
                    MessageBox.Show(ex.Message, Loc.T("Skin.OpenFolderFailed"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }));
            menu.Items.Add(skinItem);

            var cosmeticsItem = new ToolStripMenuItem(Loc.T("Menu.Cosmetics"));
            var catTailItem = new ToolStripMenuItem(Loc.T("Cosmetics.CatTail")) { Checked = catTailEnabled };
            catTailItem.Click += (_, __) =>
            {
                catTailEnabled = !catTailEnabled;
                catTailItem.Checked = catTailEnabled;
                catTailStarted = false;
                SaveSettings();
            };
            cosmeticsItem.DropDownItems.Add(catTailItem);
            var catBangsItem = new ToolStripMenuItem(Loc.T("Cosmetics.CatBangs")) { Checked = catBangsEnabled };
            catBangsItem.Click += (_, __) =>
            {
                catBangsEnabled = !catBangsEnabled;
                catBangsItem.Checked = catBangsEnabled;
                SaveSettings();
            };
            cosmeticsItem.DropDownItems.Add(catBangsItem);
            menu.Items.Add(cosmeticsItem);

            var hairColorsItem = new ToolStripMenuItem(Loc.T("Menu.HairColors"));
            var hairColorsEnabledItem = new ToolStripMenuItem(Loc.T("Hair.UseCustom"))
                { Checked = customHairColorsEnabled };
            hairColorsEnabledItem.Click += (_, __) =>
            {
                customHairColorsEnabled = !customHairColorsEnabled;
                hairColorsEnabledItem.Checked = customHairColorsEnabled;
                SaveSettings();
            };
            hairColorsItem.DropDownItems.Add(hairColorsEnabledItem);
            hairColorsItem.DropDownItems.Add(new ToolStripSeparator());
            string[] colorNames = { Loc.T("Hair.NoDashes"), Loc.T("Hair.OneDash"), Loc.T("Hair.TwoDashes") };
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
            hairColorsItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Hair.ResetCeleste"), null, (_, __) =>
            {
                customHairColors[0] = Player.UsedHairColor;
                customHairColors[1] = Player.NormalHairColor;
                customHairColors[2] = Player.TwoDashesHairColor;
                RefreshColorLabels();
                SaveSettings();
            }));
            menu.Items.Add(hairColorsItem);

            var scaleItem = new ToolStripMenuItem(Loc.T("Menu.Scale"));
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
            inputItem = new ToolStripMenuItem(Loc.T("Menu.KeyboardControls"), null, (_, __) =>
            { InputEnabled = !InputEnabled; inputItem.Checked = InputEnabled; SaveSettings(); })
            { Checked = InputEnabled };
            menu.Items.Add(inputItem);
            ToolStripMenuItem padInputItem = null;
            padInputItem = new ToolStripMenuItem(Loc.T("Menu.ControllerControls"), null, (_, __) =>
            { PadInputEnabled = !PadInputEnabled; padInputItem.Checked = PadInputEnabled; SaveSettings(); })
            { Checked = PadInputEnabled };
            menu.Items.Add(padInputItem);
            ToolStripMenuItem unfocusedInputItem = null;
            unfocusedInputItem = new ToolStripMenuItem(Loc.T("Menu.RespondUnfocused"), null, (_, __) =>
            {
                InputWhenUnfocused = !InputWhenUnfocused;
                unfocusedInputItem.Checked = InputWhenUnfocused;
                SaveSettings();
            }) { Checked = InputWhenUnfocused };
            menu.Items.Add(unfocusedInputItem);
            menu.Items.Add(BuildBindingsMenu());
            menu.Items.Add(BuildPadBindingsMenu());

            ToolStripMenuItem topItem = null;
            topItem = new ToolStripMenuItem(Loc.T("Menu.AlwaysOnTop"), null, (_, __) =>
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

            var startupItem = new ToolStripMenuItem(Loc.T("Menu.LaunchAtSignIn"))
            {
                Checked = StartupRegistration.IsEnabled()
            };
            startupItem.Click += (_, __) =>
            {
                try
                {
                    StartupRegistration.SetEnabled(!startupItem.Checked);
                    startupItem.Checked = StartupRegistration.IsEnabled();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message,
                        Loc.T("Startup.ChangeFailed"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            menu.Items.Add(startupItem);

            var sfxItem = new ToolStripMenuItem(Loc.T("Menu.SoundEffects"));
            foreach (var option in new[]
            {
                new KeyValuePair<int, string>(0, Loc.T("Common.Off")),
                new KeyValuePair<int, string>(1, Loc.T("Sfx.OnlyWhenFocused")),
                new KeyValuePair<int, string>(2, Loc.T("Common.On"))
            })
            {
                int mode = option.Key;
                var choice = new ToolStripMenuItem(option.Value)
                {
                    Checked = soundEffects.Mode == mode,
                    Tag = mode
                };
                choice.Click += (_, __) =>
                {
                    soundEffects.Mode = mode;
                    SaveSettings();
                    foreach (ToolStripItem raw in sfxItem.DropDownItems)
                        if (raw is ToolStripMenuItem item && item.Tag is int)
                            item.Checked = (int)item.Tag == mode;
                };
                sfxItem.DropDownItems.Add(choice);
            }
            sfxItem.DropDownItems.Add(new ToolStripSeparator());
            var volumeItem = new ToolStripMenuItem();
            void RefreshVolumeLabel() => volumeItem.Text =
                Loc.T("Sfx.Volume") + ": " + soundEffects.Volume + "%";
            for (int volume = 0; volume <= 100; volume += 10)
            {
                int value = volume;
                var choice = new ToolStripMenuItem(value + "%")
                {
                    Checked = soundEffects.Volume == value,
                    Tag = "volume"
                };
                choice.Click += (_, __) =>
                {
                    soundEffects.Volume = value;
                    RefreshVolumeLabel();
                    SaveSettings();
                    foreach (ToolStripMenuItem item in volumeItem.DropDownItems)
                        item.Checked = item.Text == value + "%";
                };
                volumeItem.DropDownItems.Add(choice);
            }
            RefreshVolumeLabel();
            sfxItem.DropDownItems.Add(volumeItem);
            sfxItem.DropDownItems.Add(new ToolStripSeparator());
            var surfaceItem = new ToolStripMenuItem(Loc.T("Sfx.SurfaceMaterial"));
            foreach (var option in new[]
            {
                new KeyValuePair<int, string>(1, Loc.T("Surface.Asphalt")),
                new KeyValuePair<int, string>(2, Loc.T("Surface.Car")),
                new KeyValuePair<int, string>(3, Loc.T("Surface.Dirt")),
                new KeyValuePair<int, string>(4, Loc.T("Surface.Snow")),
                new KeyValuePair<int, string>(5, Loc.T("Surface.Wood")),
                new KeyValuePair<int, string>(6, Loc.T("Surface.StoneBridge")),
                new KeyValuePair<int, string>(7, Loc.T("Surface.Girder")),
                new KeyValuePair<int, string>(8, Loc.T("Surface.BrickDefault")),
                new KeyValuePair<int, string>(9, Loc.T("Surface.ZipMover")),
                new KeyValuePair<int, string>(11, Loc.T("Surface.InactiveDreamBlock")),
                new KeyValuePair<int, string>(12, Loc.T("Surface.ActiveDreamBlock")),
                new KeyValuePair<int, string>(13, Loc.T("Surface.ResortWood")),
                new KeyValuePair<int, string>(14, Loc.T("Surface.ResortRoof")),
                new KeyValuePair<int, string>(15, Loc.T("Surface.ResortSinkingPlatform")),
                new KeyValuePair<int, string>(16, Loc.T("Surface.ResortBasementTile")),
                new KeyValuePair<int, string>(17, Loc.T("Surface.ResortLinens")),
                new KeyValuePair<int, string>(18, Loc.T("Surface.ResortBoxes")),
                new KeyValuePair<int, string>(19, Loc.T("Surface.ResortBooks")),
                new KeyValuePair<int, string>(20, Loc.T("Surface.ClutterDoor")),
                new KeyValuePair<int, string>(21, Loc.T("Surface.ClutterSwitch")),
                new KeyValuePair<int, string>(22, Loc.T("Surface.ResortElevator")),
                new KeyValuePair<int, string>(23, Loc.T("Surface.CliffsideSnow")),
                new KeyValuePair<int, string>(25, Loc.T("Surface.CliffsideGrass")),
                new KeyValuePair<int, string>(27, Loc.T("Surface.CliffsideWhiteBlock")),
                new KeyValuePair<int, string>(28, Loc.T("Surface.Gondola")),
                new KeyValuePair<int, string>(32, Loc.T("Surface.AuroraGlass")),
                new KeyValuePair<int, string>(33, Loc.T("Surface.Grass")),
                new KeyValuePair<int, string>(35, Loc.T("Surface.CassetteBlock")),
                new KeyValuePair<int, string>(36, Loc.T("Surface.CoreIce")),
                new KeyValuePair<int, string>(37, Loc.T("Surface.CoreMoltenRock")),
                new KeyValuePair<int, string>(40, Loc.T("Surface.Glitch")),
                new KeyValuePair<int, string>(42, Loc.T("Surface.MoonCafe")),
                new KeyValuePair<int, string>(43, Loc.T("Surface.DreamClouds")),
                new KeyValuePair<int, string>(44, Loc.T("Surface.Moon"))
            })
            {
                int index = option.Key;
                var choice = new ToolStripMenuItem(index + " — " + option.Value)
                {
                    Checked = player.NormalSurfaceSoundIndex == index,
                    Tag = index
                };
                choice.Click += (_, __) =>
                {
                    player.NormalSurfaceSoundIndex = index;
                    SaveSettings();
                    foreach (ToolStripMenuItem item in surfaceItem.DropDownItems)
                        item.Checked = (int)item.Tag == index;
                };
                surfaceItem.DropDownItems.Add(choice);
            }
            sfxItem.DropDownItems.Add(surfaceItem);
            menu.Items.Add(sfxItem);

            var particleItem = new ToolStripMenuItem(Loc.T("Menu.ParticleEffects"), null, (sender, __) =>
            {
                ParticlesEnabled = !ParticlesEnabled;
                ((ToolStripMenuItem)sender).Checked = ParticlesEnabled;
                SaveSettings();
            })
            { Checked = ParticlesEnabled };
            menu.Items.Add(particleItem);

            var freezeItem = new ToolStripMenuItem(Loc.T("Menu.FreezeFrames"), null, (sender, __) =>
            {
                player.SetFreezeFramesEnabled(!player.FreezeFramesEnabled);
                ((ToolStripMenuItem)sender).Checked = player.FreezeFramesEnabled;
                SaveSettings();
            })
            { Checked = player.FreezeFramesEnabled };
            menu.Items.Add(freezeItem);

            var respawnReversalItem = new ToolStripMenuItem(
                Loc.T("Menu.RespawnReversal"), null, (sender, __) =>
            {
                player.RespawnReversalEnabled = !player.RespawnReversalEnabled;
                ((ToolStripMenuItem)sender).Checked = player.RespawnReversalEnabled;
                SaveSettings();
            }) { Checked = player.RespawnReversalEnabled };
            menu.Items.Add(respawnReversalItem);

            var ignoreMaximizedItem = new ToolStripMenuItem(
                Loc.T("Menu.IgnoreMaximizedWindows"), null, (sender, __) =>
            {
                ignoreMaximizedWindows = !ignoreMaximizedWindows;
                ((ToolStripMenuItem)sender).Checked = ignoreMaximizedWindows;
                pollCounter = 999;
                SaveSettings();
            }) { Checked = ignoreMaximizedWindows };
            menu.Items.Add(ignoreMaximizedItem);

            var dreamItem = new ToolStripMenuItem(Loc.T("Menu.DreamBlockWindows"), null, (sender, __) =>
            {
                dreamBlockMode = !dreamBlockMode;
                ((ToolStripMenuItem)sender).Checked = dreamBlockMode;
                pollCounter = 999;
                SaveSettings();
            }) { Checked = dreamBlockMode };
            menu.Items.Add(dreamItem);

            var edgeWrapItem = new ToolStripMenuItem(
                Loc.T("Menu.EdgeWrap"));
            foreach (var option in new[]
            {
                new KeyValuePair<int, string>(0, Loc.T("Common.Off")),
                new KeyValuePair<int, string>(1, Loc.T("Common.Horizontal")),
                new KeyValuePair<int, string>(2, Loc.T("Common.Vertical")),
                new KeyValuePair<int, string>(3, Loc.T("EdgeWrap.Both"))
            })
            {
                int mode = option.Key;
                var choice = new ToolStripMenuItem(option.Value)
                {
                    Checked = edgeWrapMode == mode,
                    Tag = mode
                };
                choice.Click += (_, __) =>
                {
                    edgeWrapMode = mode;
                    pollCounter = 999;
                    SaveSettings();
                    foreach (ToolStripMenuItem item in edgeWrapItem.DropDownItems)
                        item.Checked = (int)item.Tag == mode;
                };
                edgeWrapItem.DropDownItems.Add(choice);
            }
            menu.Items.Add(edgeWrapItem);

            var elytraItem = new ToolStripMenuItem(
                Loc.T("Menu.Elytra"), null, (sender, __) =>
            {
                player.ElytraEnabled = !player.ElytraEnabled;
                ((ToolStripMenuItem)sender).Checked = player.ElytraEnabled;
                SaveSettings();
            }) { Checked = player.ElytraEnabled };
            menu.Items.Add(elytraItem);

            var overlaysItem = new ToolStripMenuItem(Loc.T("Menu.ExtraOverlays"));
            var speedometerItem = new ToolStripMenuItem(Loc.T("Menu.Speedometer"));
            foreach (var option in new[]
            {
                new KeyValuePair<int, string>(0, Loc.T("Common.Off")),
                new KeyValuePair<int, string>(1, Loc.T("Common.Horizontal")),
                new KeyValuePair<int, string>(2, Loc.T("Common.Vertical")),
                new KeyValuePair<int, string>(3, Loc.T("Speedometer.Both"))
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
            var hitboxesItem = new ToolStripMenuItem(Loc.T("Menu.Hitboxes")) { Checked = hitboxesEnabled };
            hitboxesItem.Click += (_, __) =>
            {
                hitboxesEnabled = !hitboxesEnabled;
                hitboxesItem.Checked = hitboxesEnabled;
                SaveSettings();
            };
            overlaysItem.DropDownItems.Add(hitboxesItem);
            menu.Items.Add(overlaysItem);

            var staminaItem = new ToolStripMenuItem(Loc.T("Menu.InfiniteStamina"), null, (sender, __) =>
            {
                player.InfiniteStamina = !player.InfiniteStamina;
                ((ToolStripMenuItem)sender).Checked = player.InfiniteStamina;
                SaveSettings();
            }) { Checked = player.InfiniteStamina };
            menu.Items.Add(staminaItem);

            var invincibleItem = new ToolStripMenuItem(Loc.T("Menu.Invincible"), null, (sender, __) =>
            {
                player.Invincible = !player.Invincible;
                ((ToolStripMenuItem)sender).Checked = player.Invincible;
                SaveSettings();
            }) { Checked = player.Invincible };
            menu.Items.Add(invincibleItem);

            var dashItem = new ToolStripMenuItem(Loc.T("Menu.DashCount"));
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
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Menu.ReplayWakeUp"), null, (_, __) =>
            {
                introWakeUp = true;
                animator.Play("wakeUp", true);
            }));
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Menu.ResetPosition"), null, (_, __) => ResetPosition()));
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Menu.SpawnJellyfish"), null, (_, __) =>
                Interlocked.Increment(ref pendingGliderSpawns)));
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Menu.SpawnSeeker"), null, (_, __) =>
                Interlocked.Increment(ref pendingSeekerSpawns)));
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Menu.SpawnTheo"), null, (_, __) =>
                Interlocked.Increment(ref pendingTheoSpawns)));
            var removeEntitiesItem = new ToolStripMenuItem(Loc.T("Menu.RemoveEntities"));
            removeEntitiesItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Menu.RemoveAllJellyfish"), null,
                (_, __) => Interlocked.Or(ref pendingRemoveAllEntities, 1)));
            removeEntitiesItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Menu.RemoveAllSeekers"), null,
                (_, __) => Interlocked.Or(ref pendingRemoveAllEntities, 2)));
            removeEntitiesItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Menu.RemoveAllTheo"), null,
                (_, __) => Interlocked.Or(ref pendingRemoveAllEntities, 4)));
            removeEntitiesItem.DropDownItems.Add(new ToolStripSeparator());
            removeEntitiesItem.DropDownItems.Add(new ToolStripMenuItem(Loc.T("Menu.RemoveEverything"), null,
                (_, __) => Interlocked.Or(ref pendingRemoveAllEntities, 7)));
            menu.Items.Add(removeEntitiesItem);
            menu.Items.Add(new ToolStripSeparator());

            var celesteFolderItem = new ToolStripMenuItem(Loc.T("Menu.CelesteFolder"))
            {
                ToolTipText = CelesteInstall.Directory ?? Loc.T("Celeste.NoneFound")
            };
            celesteFolderItem.Click += (_, __) =>
            {
                string folder = AskForCelesteFolder();
                if (folder == null ||
                    folder.Equals(CelesteInstall.Directory, StringComparison.OrdinalIgnoreCase)) return;
                settings.CelestePath = folder;
                CelesteInstall.Chosen = folder;
                SaveSettings();
                Log("Celeste install: " + folder);
                // Her sprites and the sound banks are both read once, at startup, so a new
                // folder only really takes over at the next one.
                if (MessageBox.Show(Loc.T("Celeste.RestartToApply"), Loc.T("App.Title"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    restartAfterExit = true;
                    ExitApp();
                }
            };
            menu.Items.Add(celesteFolderItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Menu.Controls"), null, (_, __) =>
                MessageBox.Show(
                    Loc.T("Help.ControlsBody"),
                    Loc.T("App.Title"), MessageBoxButtons.OK, MessageBoxIcon.Information)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(Loc.T("Common.Exit"), null, (_, __) => ExitApp()));
            return menu;
        }

        Icon BuildTrayIcon()
        {
            // Build tray icon from Madeline portrait (not pixel art; smooth downscale).
            // Shipped beside the app, or her dialogue portrait out of Celeste's own atlas.
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "portrait.png");
            try
            {
                using (var src = System.IO.File.Exists(path)
                    ? new Bitmap(path)
                    : new Bitmap(Sprites.Get(Sprites.PortraitId, false)))
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
                    trayIconHandle = hIcon;   // Keep handle; DestroyIcon on exit
                    bmp.Dispose();
                    return Icon.FromHandle(hIcon);
                }
            }
            catch { return SystemIcons.Application; }
        }

        /// <summary>Put her back on the displays if she ended up off them.</summary>
        /// <remarks>
        /// The perimeter of the monitors is solid, so she can neither walk nor fall off it, but
        /// a drag sets her position outright and can leave her anywhere -- past the edge she
        /// would simply keep falling out of sight.  An axis set to wrap is left alone, leaving
        /// the screen being the whole point of it.
        /// </remarks>
        void SnapIntoView()
        {
            if (dragging || introWakeUp || player.IsDead || player.IsRespawning) return;
            PointF onView = ClampIntoDisplays(
                player.Pos, 4f, player.CurrentHitHeight, 0f, monitorGameBounds, edgeWrapMode);
            // WrapBy carries her hair and anything she is holding along with her.
            if (onView != player.Pos)
                player.WrapBy(onView.X - player.Pos.X, onView.Y - player.Pos.Y);
        }

        /// <summary>The same for everything else loose on the desktop.</summary>
        /// <remarks>
        /// They can all be dragged, so they can all be dropped past an edge, and none of them
        /// has anything that would bring them back: the crystal would break, and the jelly and
        /// the seeker would drift off wherever they were let go.  Anything being carried or
        /// held by the cursor is left alone, its position belonging to whoever has it.
        /// </remarks>
        void SnapEntitiesIntoView()
        {
            if (introWakeUp || monitorGameBounds.Count == 0) return;

            foreach (TheoCrystal theo in theos)
            {
                if (theo.Removed || theo.IsHeld || theo.BeingDragged || theo.IsDying) continue;
                PointF onView = ClampIntoDisplays(theo.Pos, TheoCrystal.HalfWidth,
                    TheoCrystal.ColliderHeight, 0f, monitorGameBounds, edgeWrapMode);
                if (onView != theo.Pos) theo.SnapIntoView(onView);
            }
            foreach (Glider glider in gliders)
            {
                if (glider.IsHeld || glider.BeingDragged) continue;
                PointF onView = ClampIntoDisplays(glider.Pos, Glider.HalfWidth,
                    Glider.ColliderHeight, 0f, monitorGameBounds, edgeWrapMode);
                if (onView != glider.Pos) glider.SnapIntoView(onView);
            }
            foreach (Seeker seeker in seekers)
            {
                if (seeker.Removed || seeker.BeingDragged) continue;
                // The seeker's 6x6 collider is centred on its position, not hung below it.
                PointF onView = ClampIntoDisplays(seeker.Pos, Seeker.HalfSize, Seeker.HalfSize,
                    Seeker.HalfSize, monitorGameBounds, edgeWrapMode);
                if (onView != seeker.Pos) seeker.SnapIntoView(onView);
            }
        }

        /// <summary>Where it belongs if it has ended up off the displays; unchanged if not.</summary>
        /// <remarks>
        /// The collider is given as its reach from the position rather than assumed, since the
        /// crystal and the jelly hang below theirs like she does while the seeker is centred
        /// on its own.
        /// </remarks>
        internal static PointF ClampIntoDisplays(PointF pos, float halfWidth, float above,
            float below, List<RectangleF> displays, int edgeWrapMode)
        {
            if (displays.Count == 0 || edgeWrapMode == 3) return pos;

            // Wholly on one display is the answer almost every frame, and worth having before
            // the general test below, which allocates to handle straddling a seam.
            foreach (RectangleF display in displays)
                if (pos.X - halfWidth >= display.Left && pos.X + halfWidth <= display.Right &&
                    pos.Y - above >= display.Top && pos.Y + below <= display.Bottom)
                    return pos;

            var box = new Win32.RECT
            {
                Left = (int)Math.Floor(pos.X - halfWidth),
                Top = (int)Math.Floor(pos.Y - above),
                Right = (int)Math.Ceiling(pos.X + halfWidth),
                Bottom = (int)Math.Ceiling(pos.Y + below),
            };
            var rects = new List<Win32.RECT>(displays.Count);
            foreach (RectangleF display in displays)
                rects.Add(new Win32.RECT
                {
                    Left = (int)display.Left, Top = (int)display.Top,
                    Right = (int)display.Right, Bottom = (int)display.Bottom,
                });
            // Nothing left of her once the displays are taken away means she is wholly on them,
            // which is also how straddling the seam between two of them comes out fine.
            if (SubtractRects(box, rects).Count == 0) return pos;

            RectangleF nearest = displays[0];
            float nearestDistance = float.MaxValue;
            foreach (RectangleF display in displays)
            {
                float dx = pos.X - Math.Max(display.Left, Math.Min(display.Right, pos.X));
                float dy = pos.Y - Math.Max(display.Top, Math.Min(display.Bottom, pos.Y));
                float distance = dx * dx + dy * dy;
                if (distance < nearestDistance) { nearestDistance = distance; nearest = display; }
            }

            float x = pos.X, y = pos.Y;
            if ((edgeWrapMode & 1) == 0)
                x = Math.Max(nearest.Left + halfWidth, Math.Min(nearest.Right - halfWidth, x));
            if ((edgeWrapMode & 2) == 0)
                y = Math.Max(nearest.Top + above, Math.Min(nearest.Bottom - below, y));
            return new PointF(x, y);
        }

        // Auto-reset when far off-screen (prevents infinite fall / being thrown off the virtual desktop)
        void CheckAutoReset()
        {
            if (dragging || introWakeUp) return;
            var vs = SystemInformation.VirtualScreen;
            float gx = player.Pos.X * GameScale;
            float gy = player.Pos.Y * GameScale;
            // Threshold: more than 1 screen width horizontally or 1.5 screen heights vertically counts as "far away"
            bool far = gx < vs.Left - vs.Width || gx > vs.Right + vs.Width ||
                       gy < vs.Top - vs.Height * 1.5f || gy > vs.Bottom + vs.Height * 1.5f;
            if (far) ResetPosition();
        }

        // Reset: appear at top-center of the current screen, then free-fall
        void ResetPosition()
        {
            // Clamp into the virtual screen first (prevents wrong monitor pick when coords overflow during infinite fall)
            var vs = SystemInformation.VirtualScreen;
            float px = Math.Max(vs.Left, Math.Min(player.Pos.X * GameScale, vs.Right - 1f));
            float py = Math.Max(vs.Top, Math.Min(player.Pos.Y * GameScale, vs.Bottom - 1f));
            var sc = Screen.FromPoint(new Point((int)px, (int)py));
            var wa = sc.WorkingArea;
            player.ResetTo(new PointF(ToGamePixels((wa.Left + wa.Right) / 2), ToGamePixels(wa.Top) + 5));
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
            // Wait for the game-loop thread to finish the current frame before releasing resources so the render thread is not still using GPU objects
            if (loopThread != null && loopThread != Thread.CurrentThread)
                loopThread.Join(1500);
            soundEffects.Dispose();
            tray.Visible = false;
            tray.Dispose();
            // Release tray-icon HICON and Direct3D / DirectComposition resources
            if (trayIconHandle != IntPtr.Zero) { Win32.DestroyIcon(trayIconHandle); trayIconHandle = IntPtr.Zero; }
            presenter?.Dispose();
            foreach (var trail in dashTrails) trail.Mask?.Dispose();
            dashTrails.Clear();
            lock (gliderWindowLock)
            {
                foreach (var window in gliderWindows.Values) window.Dispose();
                gliderWindows.Clear();
            }
            lock (theoWindowLock)
            {
                foreach (var window in theoWindows.Values) window.Dispose();
                theoWindows.Clear();
            }
            foreach (var cached in gliderStampCache.Values) cached.Bitmap?.Dispose();
            gliderStampCache.Clear();
            lock (seekerWindowLock)
            {
                foreach (var window in seekerWindows.Values) window.Dispose();
                seekerWindows.Clear();
            }
            foreach (var cached in seekerStampCache.Values) cached.Bitmap?.Dispose();
            seekerStampCache.Clear();
            foreach (Seeker seeker in seekers)
                foreach (SeekerTrail trail in seeker.Trails) trail.Stamp?.Dispose();
            foreach (Bitmap bitmap in seekerParticleBitmaps.Values) bitmap.Dispose();
            seekerParticleBitmaps.Clear();
            gliderDebugStamp?.Dispose();
            theoDebugStamp?.Dispose();
            foreach (Bitmap bitmap in seekerDebugStamps) bitmap?.Dispose();
            foreach (var digit in picoDigits) digit?.Dispose();
            compositionHost?.Close();
            compositionHost?.Dispose();
            // Started here rather than at the click so the new copy finds settings.txt written,
            // the tray icon gone and the sound device free, instead of racing this one for them.
            if (restartAfterExit)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath,
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex) { Log("restart failed: " + ex.Message); }
            }
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

    /// <summary>Raise timer resolution (winmm).</summary>
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

    /// <summary>Modal controller-button capture window used by the tray binding editor.</summary>
    sealed class PadCaptureDialog : Form
    {
        // Capture-only: a bind must be deliberate, so a stick or trigger has to travel
        // well past the gameplay thresholds before it counts as a press.
        const float CaptureThreshold = 0.5f;

        static readonly PadButton[] Candidates = (PadButton[])Enum.GetValues(typeof(PadButton));

        readonly System.Windows.Forms.Timer poll;
        readonly HashSet<PadButton> heldOnOpen = new HashSet<PadButton>();
        readonly Label hint;
        readonly string instructionText;
        bool sampledOpenState;
        bool showingDisconnected;

        public PadButton CapturedButton { get; private set; }

        public PadCaptureDialog(string title, string instructions)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            KeyPreview = true;
            ClientSize = new Size(430, 120);
            instructionText = instructions;
            hint = new Label
            {
                Dock = DockStyle.Fill,
                Text = instructions,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = SystemFonts.MessageBoxFont,
                Padding = new Padding(16)
            };
            Controls.Add(hint);
            poll = new System.Windows.Forms.Timer { Interval = 16 };
            poll.Tick += (_, __) => Sample();
            poll.Start();
        }

        void Sample()
        {
            PadState state = XInputPad.Poll();
            if (!state.Connected)
            {
                // Otherwise an unplugged controller just looks like a dialog that ignores input.
                if (!showingDisconnected)
                {
                    showingDisconnected = true;
                    hint.Text = Loc.T("Pad.NoController") + "\n\n" + instructionText;
                }
                return;
            }
            if (showingDisconnected)
            {
                showingDisconnected = false;
                hint.Text = instructionText;
            }
            // Buttons already held when the dialog opened (a trigger still down from the
            // menu click, a resting stick) only arm once they have been released.
            if (!sampledOpenState)
            {
                sampledOpenState = true;
                foreach (PadButton button in Candidates)
                    if (button != PadButton.None && state.Check(button, CaptureThreshold))
                        heldOnOpen.Add(button);
                return;
            }
            foreach (PadButton button in Candidates)
            {
                if (button == PadButton.None) continue;
                if (!state.Check(button, CaptureThreshold)) { heldOnOpen.Remove(button); continue; }
                if (heldOnOpen.Contains(button)) continue;
                CapturedButton = button;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                CapturedButton = PadButton.None;
                DialogResult = DialogResult.OK;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
            else return;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            poll.Stop();
            poll.Dispose();
            base.OnFormClosed(e);
        }
    }
}
