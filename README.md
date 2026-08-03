# 玛德琳桌宠 (DeskMadeline)

Celeste 风格的桌面宠物：分层透明窗口 + 60FPS 游戏循环 + 把桌面窗口当平台，移植了原版物理（跳跃/冲刺/攀爬/蹬墙跳/Super·Hyper·Ultra）。

## 构建与运行

```
cd S:\desktoptoy\deskmadeline
dotnet build -c Release
bin\Release\net6.0-windows\DeskMadeline.exe
```

需要 .NET 6 SDK。**改代码请改这份（主副本）**；`S:\desktoptoy\tx\deskmadeline` 是同步备份。

## 分发（发给别人）

**运行环境要求**：目标机器必须安装 **.NET 6 Desktop Runtime**（Windows 桌面运行时，包含 WindowsDesktop.App）。
下载：https://dotnet.microsoft.com/download/dotnet/6.0 → 选 "Windows Desktop Runtime"。
没装会报错：`You must install or update .NET to run this application.`

**分发包内容**：拷贝 `bin\Release\net6.0-windows\` 里的这些文件：

| 文件 | 说明 |
|---|---|
| `DeskMadeline.exe` | 启动程序 |
| `DeskMadeline.dll` | 主程序集 |
| `DeskMadeline.deps.json` | 依赖清单 |
| `DeskMadeline.runtimeconfig.json` | 运行时配置 |
| `assets\` | 贴图（**必须**，缺了黑屏/崩溃） |
| `skins\` | 可选（皮肤 zip 放这里即可用，可在菜单里安装；不带则只有默认皮肤） |
| `hair_tweaks.txt` | 可选（手动调的头发数据，不带则用内置值） |

**分发前删掉**：`pet_debug.log`（每次运行自动生成的调试日志，不影响功能）、`speed_log.txt`、`keys.txt`（运行时生成，删掉会重建默认按键绑定）。

> 想要"免安装、拷走就能跑"：用 `dotnet publish -c Release -r win-x64 --self-contained true` 打包运行时（体积约 150–200MB，目标机无需装 .NET）。

## 皮肤 mod

支持 Celeste 皮肤 mod zip（`Graphics/Atlases/Gameplay/.../characters/player/*.png` 结构）。**mod 文件保持 zip 原样，不做任何改动**：托盘菜单 →「皮肤」→「安装皮肤 mod（zip）…」选 zip 即复制到 `skins\`（原样 zip），或直接把 zip 丢进 `skins\`。运行时直接读 zip 加载帧（不落盘散文件）。**动画映射以 mod 的 `Sprites.xml` 为准**：解析 `<player>`（或对应 bank，含 `copy="player"` 合并、`WakeUP/` 子目录、`frames="0-4,5*10,6-14"` 等）构建桌宠各动画的帧列表；没有 Sprites.xml 或没帧则回退默认。缺帧自动回退默认（`sweat_*` 等）。自动优先无背包目录（`player_no_backpack` / `niko`），`hair00` 缺失时仅头发回退默认。

**皮肤头发设置**：在皮肤 zip 里放一个 `skin.txt`（UTF-8，与帧同目录），或散帧目录下放 `skin.txt`：
```
# 固定头发颜色（RRGGBB，覆盖冲刺变色逻辑）
haircolor=FF2D2D
# 隐藏头发（0=显示，1=隐藏）
hair=0
```
`haircolor` 省略则用默认的冲刺变色（红/白/蓝），`hair` 省略则显示头发。改完重新切一次该皮肤生效。

- 启动时会播放一段"醒来"动画（蜷着→起身，约 1.4 秒），播完进入待机。
- 托盘菜单 →「回放醒来动画」可以随时重播。
- 托盘菜单 →「粒子特效」开关（**默认关闭**，勾上才有跑步/落地/跳跃/冲刺粒子和斩击）。
- **皮肤系统**：托盘菜单 →「皮肤」子菜单可切换皮肤（默认玛德琳 + 已装皮肤）；「安装皮肤 mod（zip）…」选择 Celeste 皮肤 mod 压缩包即可安装并自动切换。**mod 一律保持 zip 原样**：安装只是把 zip 复制到 exe 旁 `skins\`（不改动源 mod），运行时直接从 zip 内存加载帧；也兼容手工放散帧目录（`skins\<名字>\` 含 idle00.png）。当前选择存于 `skin.txt`（启动自动恢复）。支持缺帧回退（如 `wakeUp`/`sweat_*` 皮肤没有就用默认），自动优先无背包版（reimu/niko 等）。切换皮肤时自动重播一遍醒来动画。
- **汗水特效**：常驻（无开关）。攀爬向上消耗体力时头顶冒白色汗滴（`climb`），静止挂墙为 `still`，**体力 ≤20 时变 `danger` 大滴**，空中攀爬跳播一次 `jump` 喷雾；离开攀爬/爬墙跳墙后自动消失。素材取自 `characters/player/sweat/`（加了 `sweat_` 前缀防与身体帧重名）。位置想微调改 `PetWindow.cs` 的 `DrawSweat` 里的 `SweatOffsetY`（游戏像素，向下为正）。
- **疲劳红闪**：体力 <20 时身体本身会**每 0.05s 闪烁红色**（原作 `Sprite.Color = Color.Red`，攀爬动画不变，与汗水 `danger` 并存）。原作没有独立的攀爬疲劳动画，`tired00-03` 只是过场用的，已屏蔽并删除。
- 托盘菜单 →「速度计」开关：窗口顶部显示实时水平速度（H）/总速度（V）与峰值；颜色按速度分档（奔跑白→冲刺黄→超跳/Ultra 红），感受 Ultra 加速。
- 托盘菜单 →「速度日志」开关：开启期间每 0.05s 采样，速度/状态变化时往 exe 旁 `speed_log.txt` 追加一行（时间、水平速度、总速度、状态、是否下蹲、动画）；挂机不刷屏，关闭或退出时写入峰值。便于回看 Ultra 连跳的指数加速曲线。
- 托盘菜单 →「冲刺冻结帧」开关（**默认开启**）：冲刺起手有 0.05s 全停帧（原作 `Celeste.Freeze(0.05)`，期间冷却不递减，有效恢复 0.05s+0.2s）；关掉后冲刺立即移动、恢复时间为 0.2s。
- 托盘图标 = 玛德琳头像（`assets/portrait.png`，来自 Portraits/madeline/normal00.png），名字显示「玛德琳」。

## 操作

| 按键 | 作用 |
|---|---|
| 方向键 / WASD | 移动 |
| C | 跳跃（土狼时间 + 可变跳高） |
| X | 冲刺（8 方向，着地恢复） |
| Z | 攀爬（贴墙按住，消耗体力） |
| 左键拖拽 | 抓住玛德琳甩出去 |
| 右键 | 托盘菜单（缩放 2x-8x、响应键盘、置顶、重置位置） |

**技巧（原作全套）**：Super / Hyper / Ultra / Wavedash / 蹬墙跳 / 反向超冲，以及 **Cornerboost**——冲刺撞墙后 0.06s 内抓墙+蹬墙跳，越过墙顶即保留冲刺速度（保留速度机制移植自原作 `wallSpeedRetained`）。

窗口就是平台（**空心边框**）：只有窗口四周边框是实体，内部是空的——角色可以在窗口内部自由走动、站在边框上、爬侧边；被前面窗口盖住的后窗边框部分不再阻挡（前窗按 Z 序遮挡后窗）。

## 头发数据

每帧头发的锚点偏移/刘海朝向已内置于 `HairMeta.cs`，并可在 exe 旁的 `hair_tweaks.txt` 追加覆盖行（一行：`帧名 x y bangs`），**启动自动加载，不用重编译**。撤销：删掉 `hair_tweaks.txt` 里对应行。

## 实现要点

- **像素完美渲染**：1x 游戏像素缓冲（32×48）+ `NearestNeighbor + PixelOffsetMode.Half` 整数倍放大。不要用 `ScaleTransform` 直接放大（会亚像素错位）。
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

**汗水特效**（移植原作 `sweatSprite`）：单独动画器 `sweatAnimator` 播放，状态由 `Player.SweatId` 驱动——攀爬向上 `climb`、静止 `still`、体力≤20 `danger`、空中攀爬跳 `jump`（一次性喷雾）、其余 `idle`（空白）。状态切换点全部对齐原作 `Player.cs`（ClimbUpdate 3301-3342 / ClimbEnd 3164 / ClimbJump 1934 / wallBoost 5921）。渲染在 `DrawSweat`，与身体同锚定同缩放（原作 sweatSprite 底部居中对齐，非 idle 不镜像）。

**疲劳红闪**（原作 `Render:1403` + `:5904`）：`IsTired`（体力<20，wallBoost 期间 +27.5 视为不累）且 `flash` 每 0.05s 翻转 → 身体用 `Color.Red` 乘法染色闪烁，画在 `DrawBody`；攀爬动画照常（原作无独立 tired 动画）。

**抓跳与体力**：抓跳（`ClimbJump`）需要体力 > 0，体力耗尽只能普通蹬墙跳（原作 NormalUpdate 同样要求 `Stamina > 0f`）；攀爬中（ClimbUpdate）跳跃对齐原作不查体力。体力耗尽被踢出攀爬后，wallBoost 0.2s 内 `IsTired` 被 +27.5 掩盖，若攀爬入口只看 `!IsTired` 会误放行：进攀爬后 ClimbUpdate 的跳跃检查先于体力退出检查 → 还能白嫖一次抓跳；不按跳则反复进/出攀爬 → 两帧快速闪跳 + 红闪丢失。故进入攀爬统一要求原始体力 `Stamina > 0`（抓墙进入 / 蹬墙转爬 / 冲刺抓墙三处）。

## 已知问题

- ~~角色被拖/落到屏幕下方后会无限下落（地板对屏外坐标失效）~~ 已加"离开屏幕很远自动重置"兜底：离开虚拟屏幕横向 1 屏宽 / 纵向 1.5 屏高会自动重置回屏幕顶部。拖拽中不会触发。
