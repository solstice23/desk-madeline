# 玛德琳桌宠 (DeskMadeline)

Celeste 风格的桌面宠物：Direct3D 11 / DirectComposition 透明渲染 + 60FPS 游戏循环 + 把桌面窗口当平台，移植了原版物理（跳跃/冲刺/攀爬/蹬墙跳/Super·Hyper·Ultra）。

## 构建与运行

```
cd D:\dev\deskmadeline
dotnet build -c Release
bin\Release\net8.0-windows\DeskMadeline.exe
```

需要 .NET 8 SDK。

## 分发（发给别人）

**运行环境要求**：目标机器必须安装 **.NET 8 Desktop Runtime**（Windows 桌面运行时，包含 WindowsDesktop.App）。
下载：https://dotnet.microsoft.com/download/dotnet/8.0 → 选 "Windows Desktop Runtime"。
没装会报错：`You must install or update .NET to run this application.`

**分发包内容**：拷贝 `bin\Release\net8.0-windows\` 里的这些文件：

| 文件 | 说明 |
|---|---|
| `DeskMadeline.exe` | 启动程序 |
| `DeskMadeline.dll` | 主程序集 |
| `DeskMadeline.deps.json` | 依赖清单 |
| `DeskMadeline.runtimeconfig.json` | 运行时配置 |
| `assets\` | 贴图（**必须**，缺了黑屏/崩溃） |
| `hair_tweaks.txt` | 可选（手动调的头发数据，不带则用内置值） |

**分发前删掉**：`pet_debug.log`（每次运行自动生成的调试日志，不影响功能）。

> 想要"免安装、拷走就能跑"：用 `dotnet publish -c Release -r win-x64 --self-contained true` 打包运行时（体积约 150–200MB，目标机无需装 .NET）。

- 启动时会播放一段"醒来"动画（蜷着→起身，约 1.4 秒），播完进入待机。
- 托盘菜单 →「回放醒来动画」可以随时重播。
- 托盘菜单 →「粒子特效」开关（**默认关闭**，勾上才有跑步/落地/跳跃/冲刺粒子和斩击）。
- 托盘图标 = 玛德琳头像（`assets/portrait.png`，来自 Portraits/madeline/normal00.png），名字显示「玛德琳」。

## 操作

| 按键 | 作用 |
|---|---|
| 方向键 / WASD | 移动 |
| C | 跳跃（土狼时间 + 可变跳高） |
| X | 冲刺（8 方向，着地恢复） |
| Z / V / 左 Shift | 攀爬；靠近水母时拾取，松开投掷 |
| 自定义（默认未绑定） | 蹲冲（独立 8 方向瞄准） |
| 左键拖拽 | 抓住玛德琳甩出去 |
| 右键 | 托盘菜单（缩放 2x-8x、响应键盘、置顶、重置位置） |

右键菜单的「生成水母」会在玛德琳前上方放置一只 Farewell 水母。携带时具有原版慢落／方向控制，按下抓取键捡起，松开抓取键投掷，按住下再松开则放下。「失焦时也响应输入」可选择让桌宠在其他程序位于前台时继续读取绑定按键；该选项默认关闭且不会吞掉其他程序的输入。

**技巧（原作全套）**：Super / Hyper / Ultra / Wavedash / 蹬墙跳 / 反向超冲，以及 **Cornerboost**——冲刺撞墙后 0.06s 内抓墙+蹬墙跳，越过墙顶即保留冲刺速度（保留速度机制移植自原作 `wallSpeedRetained`）。

窗口就是平台（**空心边框**）：只有窗口四周边框是实体，内部是空的——角色可以在窗口内部自由走动、站在边框上、爬侧边；被前面窗口盖住的后窗边框部分不再阻挡（前窗按 Z 序遮挡后窗）。

## 头发数据

每帧头发的锚点偏移/刘海朝向已内置于 `HairMeta.cs`，并可在 exe 旁的 `hair_tweaks.txt` 追加覆盖行（一行：`帧名 x y bangs`），**启动自动加载，不用重编译**。撤销：删掉 `hair_tweaks.txt` 里对应行。

## Skins / 皮肤

The right-click menu's **Skin** submenu supports complete Skin Mod Helper and Skin Mod Helper Plus packages, plus classic direct `characters/player` replacements. Put either the original mod `.zip` or an unpacked mod directory under `skins` beside `DeskMadeline.exe`, then choose **Skin → Refresh skins**. ZIPs are read into a validated local cache; an enclosing folder inside the archive is fine. The selected skin is remembered in `settings.txt`; **Default Madeline** switches back to the built-in art.

右键菜单的「皮肤」子菜单支持完整的 Skin Mod Helper / Skin Mod Helper Plus 皮肤包，也支持传统的 `characters/player` 直接替换包。把原始 mod `.zip` 或解压后的 mod 文件夹放进 exe 同目录的 `skins` 文件夹，然后选择「皮肤 → 刷新皮肤」。ZIP 会解压到经过路径和体积检查的本地缓存，压缩包内多套一层文件夹也没问题。选择会保存进 `settings.txt`；选「默认玛德琳」可恢复内置贴图。

The loader follows Skin Mod Helper's source behavior: legacy `SkinId` paths, Plus `Player_List` + `Character_ID`, sprite paths from `Graphics/Sprites.xml`, vanilla fallback for omitted frames/hair/bangs, and legacy per-dash hair colors. The checked-in `example_skins` packages are discovered automatically by local development builds but are not copied into release output.

Code-only cosmetics are separate from selectable skins. The **Cosmetics** submenu provides independent Cateline-style **Cat tail** and **Cat bangs** toggles, so either feature can be combined with any sprite skin. **Hair colors** provides a universal, persisted no-dash / one-dash / two-dash palette override. Mikuline is recognized by its `everest.yaml` package name and automatically uses its turquoise palette because the original package delegates those colors to LiquidMod instead of shipping a HairConfig. Everest DLLs such as Foxeline are not executed by the desktop pet.

仅由代码实现的装饰与可选择皮肤相互独立。「装饰」子菜单提供 Cateline 风格的「猫尾」和「猫耳刘海」开关，两项均可与任意精灵皮肤组合使用。「头发颜色」可全局覆盖并保存无冲刺／一次冲刺／两次冲刺的颜色。Mikuline 会通过 `everest.yaml` 中的包名自动识别并使用其青绿色；原包把颜色交给 LiquidMod，因此自身没有 HairConfig。桌宠不会执行 Foxeline 等 Everest DLL。

The **Extra overlays** submenu includes an Extended Variant Mode-style speedometer with Horizontal, Vertical, and Both (vector magnitude) modes. It uses the original PICO-8 digits and displays the peak value from the latest 10 rendered frames. The **Hitboxes** toggle outlines Madeline's current standing/ducking collider in lime and every active window or screen-edge solid in red. Both settings are remembered. Particle effects are enabled by default. Use **Skin → Open skins folder** to open (or create) the directory where skin ZIPs are installed.

「额外叠加层」子菜单包含 Extended Variant Mode 风格的速度计，可选择水平、垂直或合速度（速度向量长度）。它使用原版 PICO-8 数字，并显示最近 10 个渲染帧中的峰值。「碰撞箱」会用亮绿色描出玛德琳当前站立／下蹲碰撞箱，并用红色描出所有有效窗口与屏幕边缘实体。两项设置都会保存，粒子特效默认开启。可通过「皮肤 → 打开皮肤文件夹」打开（或创建）用于放置皮肤 ZIP 的目录。

The **Sound effects** submenu offers Off (the default), Only when focused, and On modes, plus volume adjustment in 10% steps. Mode and volume are remembered. Focused-only audio follows the same pet-window focus boundary as keyboard input and never plays while another application is active.

「音效」子菜单提供关闭（默认）、仅聚焦时和开启三种模式，并可按 10% 调节音量。模式和音量都会保存；“仅聚焦时”沿用键盘输入的桌宠窗口焦点判断，在其他程序处于活动状态时不会播放。

## 实现要点

- **GPU 合成**：1x 游戏像素缓冲上传至 Direct2D/Direct3D 11，并按绝对物理桌面坐标画进固定的虚拟桌面交换链；宿主 HWND 和 DirectComposition visual 都不随角色移动。
- **输入隔离**：渲染宿主完全点击穿透，只有跟随角色身体的小型透明输入 HWND 接收聚焦、拖拽和右键。
- **世界特效层**：冲刺残影使用独立 GPU 图层和物理像素世界坐标，避免高速移动时裁切或水平锯齿抖动。
- **头发锚点**：`node0 = feet + (HairOffset.X×Facing, -9×ScaleY + HairOffset.Y)`，恒 -9。
- **头发层位**：画在身体后面（先画头发再画身体）。
- **头发模拟参数**：`Player.cs` 底部 `PlayerHairSim` 类里的命名常量。
- **每帧头发元数据**：`HairMeta.cs`（原版正确值），可被 `hair_tweaks.txt` 覆盖。

## 粒子 / 特效

轻量粒子系统（`Particles.cs`），参数移植自 Celeste：
- **跑步扬尘**：跑动时脚下冒小烟（`smoke0-3`）
- **落地烟尘**：着地一簇尘
- **跳跃 puff**：起跳小气流
- **冲刺粒子**：冲刺时拖蓝白尾迹（`zappysmoke00-03`）
- **斩击特效**：冲刺开始时的白色弧线（`slash00-03`）

粒子画进 1x 画布，吸附整数像素，保持像素完美。调整效果：改 `Particles.cs` 里 `PType` 的参数（大小/速度/寿命/颜色/数量）或 `EmitPlayerParticles` 里的发射点。

## 已知问题

- ~~角色被拖/落到屏幕下方后会无限下落（地板对屏外坐标失效）~~ 已加"离开屏幕很远自动重置"兜底：离开虚拟屏幕横向 1 屏宽 / 纵向 1.5 屏高会自动重置回屏幕顶部。拖拽中不会触发。
