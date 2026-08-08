using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeskMadeline
{
    /// <summary>Supported UI language metadata.</summary>
    internal sealed class LanguageInfo
    {
        public string Code { get; }
        public string NativeName { get; }
        public LanguageInfo(string code, string nativeName)
        {
            Code = code;
            NativeName = nativeName;
        }
    }

    /// <summary>
    /// Key-based UI localization. English is the fallback; other languages are
    /// in-code tables below. To add a language: append a <see cref="LanguageInfo"/>,
    /// add a dictionary entry in <c>translations</c>, and fill every key.
    /// </summary>
    internal static class Loc
    {
        public const string DefaultCode = "en";

        static readonly LanguageInfo[] languages =
        {
            new LanguageInfo("en", "English"),
            new LanguageInfo("zh", "中文"),
        };

        // English fallback table (also the canonical key list).
        static readonly Dictionary<string, string> english = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.Name"] = "Madeline",
            ["App.Title"] = "Desk Madeline",
            ["Common.Remove"] = "Remove",
            ["Common.Off"] = "Off",
            ["Common.On"] = "On",
            ["Common.Horizontal"] = "Horizontal",
            ["Common.Vertical"] = "Vertical",
            ["Common.Exit"] = "Exit",
            ["Common.Ok"] = "OK",
            ["Menu.About"] = "About",
            ["Menu.CheckUpdate"] = "Check for Updates...",
            ["Update.Title"] = "Check for Updates",
            ["Update.Checking"] = "Asking GitHub which build is newest...",
            ["Update.Available"] = "There is a newer build.",
            ["Update.Current"] = "This is the newest build.",
            ["Update.Failed"] = "Could not ask GitHub which build is newest.",
            ["Update.NoBuilds"] = "No build has been published yet.",
            ["Update.Yours"] = "This build: {0}",
            ["Update.Newest"] = "Newest build: {0}",
            ["Update.Unknown"] = "not built from a checkout",
            ["Update.OnThePage"] = "It is on the release page on GitHub.",
            ["Update.Download"] = "Download",
            ["Update.Later"] = "Later",
            ["About.OriginalBy"] = "Originally created by {0}",
            ["About.ContinuedBy"] = "Extended by {0}",
            ["About.FanProject"] = "A fan project, not affiliated with {0} or its developers.",
            ["About.ThirdParty"] = "Her artwork and sounds are read from your own copy of the game. See THIRD_PARTY_NOTICES.md for the mods this borrows from.",
            ["About.Source"] = "GitHub",
            ["Action.Left"] = "Left",
            ["Action.Right"] = "Right",
            ["Action.Up"] = "Up",
            ["Action.Down"] = "Down",
            ["Action.Jump"] = "Jump",
            ["Action.Dash"] = "Dash",
            ["Action.Grab"] = "Grab",
            ["Action.CrouchDash"] = "Crouch Dash",
            ["Action.DeployElytra"] = "Deploy Elytra",
            ["Keys.Root"] = "Key bindings",
            ["Keys.Unbound"] = "Unbound",
            ["Keys.Change"] = "Change…",
            ["Keys.Unbind"] = "Unbind",
            ["Keys.ResetDefaults"] = "Reset defaults",
            ["Keys.BindTitle"] = "Bind {0}",
            ["Keys.CaptureHint"] = "Press a key. Backspace/Delete clears this slot; Esc cancels.",
            ["Pad.Root"] = "Controller bindings",
            ["Pad.CaptureHint"] = "Press a controller button. Backspace/Delete clears this slot; Esc cancels.",
            ["Pad.NoController"] = "No controller detected.",
            ["Pad.A"] = "A",
            ["Pad.B"] = "B",
            ["Pad.X"] = "X",
            ["Pad.Y"] = "Y",
            ["Pad.LeftShoulder"] = "LB (left bumper)",
            ["Pad.RightShoulder"] = "RB (right bumper)",
            ["Pad.LeftTrigger"] = "LT (left trigger)",
            ["Pad.RightTrigger"] = "RT (right trigger)",
            ["Pad.LeftStick"] = "Left stick click",
            ["Pad.RightStick"] = "Right stick click",
            ["Pad.Start"] = "Start",
            ["Pad.Back"] = "Back",
            ["Pad.DPadUp"] = "D-Pad up",
            ["Pad.DPadDown"] = "D-Pad down",
            ["Pad.DPadLeft"] = "D-Pad left",
            ["Pad.DPadRight"] = "D-Pad right",
            ["Pad.LeftStickUp"] = "Left stick up",
            ["Pad.LeftStickDown"] = "Left stick down",
            ["Pad.LeftStickLeft"] = "Left stick left",
            ["Pad.LeftStickRight"] = "Left stick right",
            ["Pad.RightStickUp"] = "Right stick up",
            ["Pad.RightStickDown"] = "Right stick down",
            ["Pad.RightStickLeft"] = "Right stick left",
            ["Pad.RightStickRight"] = "Right stick right",
            ["Section.Madeline"] = "Madeline",
            ["Section.Input"] = "Input and movement",
            ["Section.Appearance"] = "Look and sound",
            ["Section.Desktop"] = "Desktop interaction",
            ["Menu.Language"] = "Language",
            ["Menu.Skin"] = "Skin",
            ["Skin.Default"] = "Default Madeline",
            ["Skin.Refresh"] = "Refresh skins",
            ["Skin.OpenFolder"] = "Open skins folder",
            ["Skin.OpenFolderFailed"] = "Could not open skins folder",
            ["Menu.Cosmetics"] = "Cosmetics",
            ["Cosmetics.CatTail"] = "Cat tail",
            ["Cosmetics.CatBangs"] = "Cat bangs",
            ["Menu.HairColors"] = "Hair colors",
            ["Hair.UseCustom"] = "Use custom colors",
            ["Hair.NoDashes"] = "No dashes",
            ["Hair.OneDash"] = "One dash",
            ["Hair.TwoDashes"] = "Two dashes",
            ["Hair.ResetCeleste"] = "Reset Celeste colors",
            ["Menu.Scale"] = "Scale (nearest-neighbor)",
            ["Menu.KeyboardControls"] = "Keyboard controls",
            ["Menu.ControllerControls"] = "Controller controls (XInput)",
            ["Menu.RespondUnfocused"] = "Respond while unfocused",
            ["Menu.AlwaysOnTop"] = "Always on top",
            ["Menu.LaunchAtSignIn"] = "Launch at sign-in",
            ["Startup.ChangeFailed"] = "Could not change sign-in startup",
            ["Menu.SoundEffects"] = "Sound effects",
            ["Sfx.OnlyWhenFocused"] = "Only when focused",
            ["Sfx.Volume"] = "Volume",
            ["Sfx.SurfaceMaterial"] = "Surface material",
            ["Surface.Asphalt"] = "Asphalt",
            ["Surface.Car"] = "Car",
            ["Surface.Dirt"] = "Dirt",
            ["Surface.Snow"] = "Snow",
            ["Surface.Wood"] = "Wood",
            ["Surface.StoneBridge"] = "Stone bridge",
            ["Surface.Girder"] = "Girder",
            ["Surface.BrickDefault"] = "Brick (Default)",
            ["Surface.ZipMover"] = "Zip mover",
            ["Surface.InactiveDreamBlock"] = "Inactive Dream Block",
            ["Surface.ActiveDreamBlock"] = "Active Dream Block",
            ["Surface.ResortWood"] = "Resort wood",
            ["Surface.ResortRoof"] = "Resort roof",
            ["Surface.ResortSinkingPlatform"] = "Resort sinking platform",
            ["Surface.ResortBasementTile"] = "Resort basement tile",
            ["Surface.ResortLinens"] = "Resort linens",
            ["Surface.ResortBoxes"] = "Resort boxes",
            ["Surface.ResortBooks"] = "Resort books",
            ["Surface.ClutterDoor"] = "Clutter door",
            ["Surface.ClutterSwitch"] = "Clutter switch",
            ["Surface.ResortElevator"] = "Resort elevator",
            ["Surface.CliffsideSnow"] = "Cliffside snow",
            ["Surface.CliffsideGrass"] = "Cliffside grass",
            ["Surface.CliffsideWhiteBlock"] = "Cliffside white block",
            ["Surface.Gondola"] = "Gondola",
            ["Surface.AuroraGlass"] = "Aurora glass",
            ["Surface.Grass"] = "Grass",
            ["Surface.CassetteBlock"] = "Cassette block",
            ["Surface.CoreIce"] = "Core ice",
            ["Surface.CoreMoltenRock"] = "Core molten rock",
            ["Surface.Glitch"] = "Glitch",
            ["Surface.MoonCafe"] = "Moon cafe",
            ["Surface.DreamClouds"] = "Dream clouds",
            ["Surface.Moon"] = "Moon",
            ["Menu.ParticleEffects"] = "Particle effects",
            ["Menu.FreezeFrames"] = "Freeze frames",
            ["Menu.RespawnReversal"] = "Respawn reversal animation",
            ["Menu.IgnoreMaximizedWindows"] = "Ignore maximized and fullscreen windows",
            ["Menu.WindowsAre"] = "Windows are",
            ["Windows.Solid"] = "Solid",
            ["Windows.DreamBlocks"] = "Dream Blocks",
            ["Windows.Water"] = "Water",
            ["Windows.MoonBlocks"] = "Moon Blocks",
            ["Menu.EdgeWrap"] = "Infinite screen edges (Experimental)",
            ["EdgeWrap.Both"] = "Both",
            ["Menu.Elytra"] = "Elytra mode (CommunalHelper)",
            ["Menu.ExtraOverlays"] = "Extra overlays",
            ["Menu.Speedometer"] = "Speedometer",
            ["Speedometer.Both"] = "Both",
            ["Menu.Hitboxes"] = "Hitboxes",
            ["Menu.InfiniteStamina"] = "Infinite stamina",
            ["Menu.Invincible"] = "Invincible",
            ["Menu.DashCount"] = "Dash count",
            ["Menu.ReplayWakeUp"] = "Replay wake-up animation",
            ["Menu.ResetPosition"] = "Reset position",
            ["Menu.SpawnJellyfish"] = "Spawn jellyfish",
            ["Menu.SpawnSeeker"] = "Spawn Seeker",
            ["Menu.SpawnTheo"] = "Spawn Theo crystal",
            ["Menu.RemoveEntities"] = "Remove spawned entities",
            ["Menu.RemoveAllJellyfish"] = "Remove all jellyfish",
            ["Menu.RemoveAllSeekers"] = "Remove all Seekers",
            ["Menu.RemoveAllTheo"] = "Remove all Theo crystals",
            ["Menu.RemoveEverything"] = "Remove everything",
            ["Menu.CelesteFolder"] = "Celeste folder…",
            ["Celeste.NoneFound"] = "No Celeste installation found",
            ["Celeste.Why"] = "Madeline's sprites and sounds are read from an installed copy of Celeste.",
            ["Celeste.NotFound"] = "No installation could be found.\n\nChoose the folder that holds Celeste.exe.",
            ["Celeste.None"] = "none",
            ["Celeste.InUse"] = "In use: {0}",
            ["Celeste.Detected"] = "Detected: {0}",
            ["Celeste.PickFolder"] = "Choose the folder that holds Celeste.exe",
            ["Celeste.NoExeThere"] = "There is no Celeste.exe in that folder.",
            ["Menu.CelesteDetect"] = "Detect Celeste",
            ["Menu.CelesteChoose"] = "Choose folder…",
            ["Celeste.Incomplete"] = "The Celeste at {0} is missing files her sprites and sounds are read from:",
            ["Celeste.AndMore"] = "…and {0} more",
            ["Celeste.ChooseAnother"] = "Choose a different folder?",
            ["Celeste.UseAnyway"] = "Use it anyway?",
            ["Celeste.FoundAt"] = "Found Celeste at:\n\n{0}",
            ["Celeste.Without"] = "Running without Celeste's files: Madeline has no sprites and no sounds. Point at the game from the tray menu, under Celeste folder.",
            ["Celeste.RestartToApply"] = "Restart Desk Madeline now to read her sprites and sounds from the new folder?",
            ["Menu.Controls"] = "How to play",
            ["Help.ControlsBody"] = "Click Madeline first to focus her, or enable Respond while unfocused. Keys can be changed under Key bindings (three slots per action). An XInput controller works the same way under Controller bindings, with Celeste's default layout. Crouch Dash is separate and unbound by default.\n\nMove: Arrow keys\nJump: C (coyote time + variable height)\nDash: X (8 directions; refills on landing)\nClimb / carry: Hold Grab against a wall, jellyfish, or Theo crystal\n\nTech:\n· Super: press Jump during a grounded dash\n· Hyper: down-diagonal grounded dash, then Jump\n· Wavedash/Ultra: down-diagonal air dash, then Jump on landing\n· Cornerboost: Grab + wall-jump within 0.06s after hitting a wall\n· Left-drag Madeline to throw her\n\nWindows are hollow platforms: stand on borders or climb their sides.",
        };

        // Non-English catalogs. Key set should match <see cref="english"/>.
        static readonly Dictionary<string, Dictionary<string, string>> translations =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["App.Name"] = "玛德琳",
                ["App.Title"] = "玛德琳桌宠",
                ["Common.Remove"] = "移除",
                ["Common.Off"] = "关闭",
                ["Common.On"] = "开启",
                ["Common.Horizontal"] = "水平",
                ["Common.Vertical"] = "垂直",
                ["Common.Exit"] = "退出",
                ["Common.Ok"] = "确定",
                ["Menu.About"] = "关于",
                ["Menu.CheckUpdate"] = "检查更新...",
                ["Update.Title"] = "检查更新",
                ["Update.Checking"] = "正在向 GitHub 查询最新构建...",
                ["Update.Available"] = "有更新的构建。",
                ["Update.Current"] = "已是最新构建。",
                ["Update.Failed"] = "无法向 GitHub 查询最新构建。",
                ["Update.NoBuilds"] = "尚未发布任何构建。",
                ["Update.Yours"] = "当前构建：{0}",
                ["Update.Newest"] = "最新构建：{0}",
                ["Update.Unknown"] = "并非由代码仓库构建",
                ["Update.OnThePage"] = "在 GitHub 的发布页面上。",
                ["Update.Download"] = "下载",
                ["Update.Later"] = "稍后",
                ["About.OriginalBy"] = "原作者：{0}",
                ["About.ContinuedBy"] = "扩展：{0}",
                ["About.FanProject"] = "同人项目，与 {0} 及其开发者无关。",
                ["About.ThirdParty"] = "贴图与音效均读取自你自己的游戏副本。本项目借鉴的模组见 THIRD_PARTY_NOTICES.md。",
                ["About.Source"] = "GitHub",
                ["Action.Left"] = "左",
                ["Action.Right"] = "右",
                ["Action.Up"] = "上",
                ["Action.Down"] = "下",
                ["Action.Jump"] = "跳跃",
                ["Action.Dash"] = "冲刺",
                ["Action.Grab"] = "抓取",
                ["Action.CrouchDash"] = "蹲冲",
                ["Action.DeployElytra"] = "展开鞘翅",
                ["Keys.Root"] = "按键绑定",
                ["Keys.Unbound"] = "未绑定",
                ["Keys.Change"] = "更改…",
                ["Keys.Unbind"] = "解除绑定",
                ["Keys.ResetDefaults"] = "恢复默认",
                ["Keys.BindTitle"] = "绑定{0}",
                ["Keys.CaptureHint"] = "请按一个键。Backspace/Delete 清除此栏；Esc 取消。",
                ["Pad.Root"] = "手柄绑定",
                ["Pad.CaptureHint"] = "请按一个手柄按键。Backspace/Delete 清除此栏；Esc 取消。",
                ["Pad.NoController"] = "未检测到手柄。",
                ["Pad.A"] = "A",
                ["Pad.B"] = "B",
                ["Pad.X"] = "X",
                ["Pad.Y"] = "Y",
                ["Pad.LeftShoulder"] = "LB（左肩键）",
                ["Pad.RightShoulder"] = "RB（右肩键）",
                ["Pad.LeftTrigger"] = "LT（左扳机）",
                ["Pad.RightTrigger"] = "RT（右扳机）",
                ["Pad.LeftStick"] = "按下左摇杆",
                ["Pad.RightStick"] = "按下右摇杆",
                ["Pad.Start"] = "Start",
                ["Pad.Back"] = "Back",
                ["Pad.DPadUp"] = "十字键上",
                ["Pad.DPadDown"] = "十字键下",
                ["Pad.DPadLeft"] = "十字键左",
                ["Pad.DPadRight"] = "十字键右",
                ["Pad.LeftStickUp"] = "左摇杆上",
                ["Pad.LeftStickDown"] = "左摇杆下",
                ["Pad.LeftStickLeft"] = "左摇杆左",
                ["Pad.LeftStickRight"] = "左摇杆右",
                ["Pad.RightStickUp"] = "右摇杆上",
                ["Pad.RightStickDown"] = "右摇杆下",
                ["Pad.RightStickLeft"] = "右摇杆左",
                ["Pad.RightStickRight"] = "右摇杆右",
                ["Section.Madeline"] = "玛德琳",
                ["Section.Input"] = "输入与移动",
                ["Section.Appearance"] = "外观与声音",
                ["Section.Desktop"] = "桌面交互",
                ["Menu.Language"] = "语言",
                ["Menu.Skin"] = "皮肤",
                ["Skin.Default"] = "默认玛德琳",
                ["Skin.Refresh"] = "刷新皮肤",
                ["Skin.OpenFolder"] = "打开皮肤文件夹",
                ["Skin.OpenFolderFailed"] = "无法打开皮肤文件夹",
                ["Menu.Cosmetics"] = "装饰",
                ["Cosmetics.CatTail"] = "猫尾",
                ["Cosmetics.CatBangs"] = "猫耳刘海",
                ["Menu.HairColors"] = "头发颜色",
                ["Hair.UseCustom"] = "使用自定义颜色",
                ["Hair.NoDashes"] = "无冲刺",
                ["Hair.OneDash"] = "一次冲刺",
                ["Hair.TwoDashes"] = "两次冲刺",
                ["Hair.ResetCeleste"] = "恢复原版颜色",
                ["Menu.Scale"] = "缩放（等比放大）",
                ["Menu.KeyboardControls"] = "键盘控制",
                ["Menu.ControllerControls"] = "手柄控制（XInput）",
                ["Menu.RespondUnfocused"] = "失焦时也响应输入",
                ["Menu.AlwaysOnTop"] = "总是置顶",
                ["Menu.LaunchAtSignIn"] = "登录时启动",
                ["Startup.ChangeFailed"] = "无法更改登录启动设置",
                ["Menu.SoundEffects"] = "音效",
                ["Sfx.OnlyWhenFocused"] = "仅聚焦时",
                ["Sfx.Volume"] = "音量",
                ["Sfx.SurfaceMaterial"] = "表面材质",
                ["Surface.Asphalt"] = "沥青",
                ["Surface.Car"] = "汽车",
                ["Surface.Dirt"] = "泥土",
                ["Surface.Snow"] = "雪地",
                ["Surface.Wood"] = "木材",
                ["Surface.StoneBridge"] = "石桥",
                ["Surface.Girder"] = "钢梁",
                ["Surface.BrickDefault"] = "砖块（默认）",
                ["Surface.ZipMover"] = "轨道方块",
                ["Surface.InactiveDreamBlock"] = "未激活梦境方块",
                ["Surface.ActiveDreamBlock"] = "激活梦境方块",
                ["Surface.ResortWood"] = "度假村木材",
                ["Surface.ResortRoof"] = "度假村屋顶",
                ["Surface.ResortSinkingPlatform"] = "度假村下沉平台",
                ["Surface.ResortBasementTile"] = "度假村地下室",
                ["Surface.ResortLinens"] = "度假村布料",
                ["Surface.ResortBoxes"] = "度假村纸箱",
                ["Surface.ResortBooks"] = "度假村书本",
                ["Surface.ClutterDoor"] = "杂物门",
                ["Surface.ClutterSwitch"] = "杂物开关",
                ["Surface.ResortElevator"] = "度假村电梯",
                ["Surface.CliffsideSnow"] = "山脊雪地",
                ["Surface.CliffsideGrass"] = "山脊草地",
                ["Surface.CliffsideWhiteBlock"] = "山脊白块",
                ["Surface.Gondola"] = "缆车",
                ["Surface.AuroraGlass"] = "极光玻璃",
                ["Surface.Grass"] = "草地",
                ["Surface.CassetteBlock"] = "磁带方块",
                ["Surface.CoreIce"] = "核心冰面",
                ["Surface.CoreMoltenRock"] = "核心熔岩",
                ["Surface.Glitch"] = "故障方块",
                ["Surface.MoonCafe"] = "月球咖啡馆",
                ["Surface.DreamClouds"] = "梦境云层",
                ["Surface.Moon"] = "月球",
                ["Menu.ParticleEffects"] = "粒子特效",
                ["Menu.FreezeFrames"] = "冻结帧",
                ["Menu.RespawnReversal"] = "重生逆向动画",
                ["Menu.IgnoreMaximizedWindows"] = "忽略最大化和全屏窗口",
                ["Menu.WindowsAre"] = "窗口是",
                ["Windows.Solid"] = "实体平台",
                ["Windows.DreamBlocks"] = "梦境方块",
                ["Windows.Water"] = "水",
                ["Windows.MoonBlocks"] = "月亮块",
                ["Menu.EdgeWrap"] = "无限屏幕边缘（实验性）",
                ["EdgeWrap.Both"] = "水平和垂直",
                ["Menu.Elytra"] = "鞘翅模式（CommunalHelper）",
                ["Menu.ExtraOverlays"] = "额外叠加层",
                ["Menu.Speedometer"] = "速度计",
                ["Speedometer.Both"] = "合速度",
                ["Menu.Hitboxes"] = "碰撞箱",
                ["Menu.InfiniteStamina"] = "无限体力",
                ["Menu.Invincible"] = "无敌模式",
                ["Menu.DashCount"] = "冲刺次数",
                ["Menu.ReplayWakeUp"] = "回放醒来动画",
                ["Menu.ResetPosition"] = "重置位置",
                ["Menu.SpawnJellyfish"] = "生成水母",
                ["Menu.SpawnSeeker"] = "生成 Seeker",
                ["Menu.SpawnTheo"] = "生成 Theo 水晶",
                ["Menu.RemoveEntities"] = "移除生成的实体",
                ["Menu.RemoveAllJellyfish"] = "移除所有水母",
                ["Menu.RemoveAllSeekers"] = "移除所有 Seeker",
                ["Menu.RemoveAllTheo"] = "移除所有 Theo 水晶",
                ["Menu.RemoveEverything"] = "全部移除",
                ["Menu.CelesteFolder"] = "Celeste 文件夹…",
                ["Celeste.NoneFound"] = "未找到 Celeste 安装目录",
                ["Celeste.Why"] = "玛德琳的贴图与音效读取自已安装的 Celeste。",
                ["Celeste.NotFound"] = "未能找到安装目录。\n\n请选择包含 Celeste.exe 的文件夹。",
                ["Celeste.None"] = "无",
                ["Celeste.InUse"] = "正在使用：{0}",
                ["Celeste.Detected"] = "检测到：{0}",
                ["Celeste.PickFolder"] = "请选择包含 Celeste.exe 的文件夹",
                ["Celeste.NoExeThere"] = "该文件夹中没有 Celeste.exe。",
                ["Menu.CelesteDetect"] = "自动检测 Celeste",
                ["Menu.CelesteChoose"] = "选择文件夹…",
                ["Celeste.Incomplete"] = "{0} 中的 Celeste 缺少读取贴图与音效所需的文件：",
                ["Celeste.AndMore"] = "……还有 {0} 个",
                ["Celeste.ChooseAnother"] = "要改选其他文件夹吗？",
                ["Celeste.UseAnyway"] = "仍要使用该文件夹吗？",
                ["Celeste.FoundAt"] = "已找到 Celeste：\n\n{0}",
                ["Celeste.Without"] = "未使用 Celeste 的文件运行：玛德琳没有贴图也没有音效。可稍后在托盘菜单的“Celeste 文件夹”中指定游戏目录。",
                ["Celeste.RestartToApply"] = "现在重启玛德琳桌宠，以从新的文件夹读取贴图与音效吗？",
                ["Menu.Controls"] = "操作说明",
                ["Help.ControlsBody"] = "先点击玛德琳取得键盘焦点，或启用“失焦时也响应输入”。可在“按键绑定”中修改按键（每项三栏）。XInput 手柄同理，可在“手柄绑定”中修改，默认为原版布局。蹲冲为独立按键，默认未绑定。\n\n移动：方向键\n跳跃：C（土狼时间+可变跳高）\n冲刺：X（8方向，着地恢复）\n攀爬/携带：对准墙、靠近水母或 Theo 水晶时按住抓取\n\n技巧：\n· Super：地面冲刺中按跳跃\n· Hyper：地面斜下冲后按跳跃\n· Wavedash/Ultra：空中斜下冲，落地时按跳跃\n· Cornerboost：冲刺撞墙后 0.06s 内抓墙+蹬墙跳\n· 左键拖着玛德琳甩出去\n\n窗口是空心平台：可站边框、爬侧边。",
            },
        };

        static string currentCode = DefaultCode;

        public static string CurrentCode => currentCode;

        public static IReadOnlyList<LanguageInfo> Languages => languages;

        public static bool IsKnown(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;
            foreach (var lang in languages)
                if (lang.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Pick initial language from settings or UI culture (zh* → zh, else en).</summary>
        public static string DetectDefault(string settingsCode = null)
        {
            if (IsKnown(settingsCode)) return Normalize(settingsCode);
            string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (IsKnown(ui)) return Normalize(ui);
            return DefaultCode;
        }

        public static void SetLanguage(string code)
        {
            currentCode = IsKnown(code) ? Normalize(code) : DefaultCode;
        }

        /// <summary>Translate a catalog key. Missing keys fall back to English, then the key itself.</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            if (TryGet(currentCode, key, out string value)) return value;
            if (english.TryGetValue(key, out value)) return value;
            return key;
        }

        /// <summary>Translate and format with <see cref="string.Format(IFormatProvider, string, object[])"/>.</summary>
        public static string Format(string key, params object[] args)
        {
            string template = T(key);
            if (args == null || args.Length == 0) return template;
            try { return string.Format(CultureInfo.CurrentCulture, template, args); }
            catch (FormatException) { return template; }
        }

        static bool TryGet(string code, string key, out string value)
        {
            value = null;
            if (code.Equals(DefaultCode, StringComparison.OrdinalIgnoreCase))
                return english.TryGetValue(key, out value);
            return translations.TryGetValue(code, out var table) && table.TryGetValue(key, out value);
        }

        static string Normalize(string code)
        {
            foreach (var lang in languages)
                if (lang.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) return lang.Code;
            return code.Trim().ToLowerInvariant();
        }
    }
}
