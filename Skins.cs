using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace DeskMadeline
{
    /// <summary>
    /// 皮肤管理：皮肤一律保持 zip 原样，安装在 skins\ 下存 zip（不改动原 mod 文件），
    /// 运行时由 Sprites.LoadSkinZip 直接读 zip 加载帧。也兼容手工放散帧目录。
    /// </summary>
    public static class Skins
    {
        public static string SkinsDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skins");

        // ---- 当前皮肤的头发展现设置（每次切换皮肤时 LoadOptions 解析）----
        public static bool HideHair;             // 隐藏头发（真 → DrawHair 不画）
        public static Color? HairColorOverride;  // 固定头发颜色（覆盖冲刺变色；null=用默认变色逻辑）

        /// <summary>列出已安装皮肤（skins\ 下的 *.zip 与含 idle00.png 的散帧目录）。返回皮肤名。</summary>
        public static List<string> ListInstalled()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(SkinsDir))
                {
                    foreach (var f in Directory.GetFiles(SkinsDir, "*.zip"))
                        list.Add(Path.GetFileNameWithoutExtension(f));
                    foreach (var d in Directory.GetDirectories(SkinsDir))
                        if (File.Exists(Path.Combine(d, "idle00.png")))
                            list.Add(Path.GetFileName(d));
                }
            }
            catch { }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        /// <summary>按皮肤名解析来源。返回 true 且 zip 非空 = zip 皮肤；dir 非空 = 散帧皮肤。</summary>
        public static bool TryGetSkinSource(string name, out string zipPath, out string dir)
        {
            zipPath = Path.Combine(SkinsDir, name + ".zip");
            if (File.Exists(zipPath)) { dir = null; return true; }
            dir = Path.Combine(SkinsDir, name);
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "idle00.png"))) { zipPath = null; return true; }
            zipPath = null; dir = null;
            return false;
        }

        /// <summary>安装：校验是有效皮肤后把 zip 原样复制到 skins\（不改动源 mod）。返回皮肤名。</summary>
        public static string InstallZip(string zipPath)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
                if (FindFrameDir(zip) == null)
                    throw new InvalidDataException("zip 里没找到皮肤帧目录（需含 idle00.png 与 bangs00/hair00.png 的 characters/player 类目录）");
            Directory.CreateDirectory(SkinsDir);
            string baseName = CleanName(Path.GetFileNameWithoutExtension(zipPath));
            string dest = Path.Combine(SkinsDir, baseName + ".zip");
            int n = 2;
            while (File.Exists(dest)) dest = Path.Combine(SkinsDir, baseName + "_" + n++ + ".zip");
            File.Copy(zipPath, dest, overwrite: false);
            return Path.GetFileNameWithoutExtension(dest);
        }

        /// <summary>在 zip 里定位皮肤帧目录：含 idle00.png + (bangs00|hair00).png，优先无背包版。找不到返回 null。</summary>
        public static string FindFrameDir(ZipArchive zip)
        {
            var folders = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                int i = e.FullName.LastIndexOf('/');
                string folder = i < 0 ? "" : e.FullName.Substring(0, i);
                string fn = e.FullName.Substring(i + 1);
                if (!folders.TryGetValue(folder, out var set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); folders[folder] = set; }
                set.Add(fn);
            }
            var candidates = new List<string>();
            foreach (var kv in folders)
                if (kv.Value.Contains("idle00.png") &&
                    (kv.Value.Contains("bangs00.png") || kv.Value.Contains("hair00.png")))
                    candidates.Add(kv.Key);
            if (candidates.Count == 0) return null;
            // 桌宠无背包 → 优先选无背包版（reimu 的 player_no_backpack / niko 的 niko）
            candidates.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            return candidates[0];
        }

        /// <summary>目录优先级：无背包优先（0 最好，越大越靠后）。</summary>
        static int Rank(string folder)
        {
            if (folder.IndexOf("no_backpack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                folder.IndexOf("nobackpack", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
            if (folder.IndexOf("_backpack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                folder.IndexOf("playback", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            return 1;
        }

        /// <summary>由 zip 文件名生成可读皮肤名：去 GameBanana 的 __xxxx 后缀与开头编号。</summary>
        static string CleanName(string raw)
        {
            string s = raw == null ? "" : raw.Trim();
            int i = s.IndexOf("__", StringComparison.Ordinal);
            if (i > 0) s = s.Substring(0, i);
            s = s.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            s = s.TrimStart('_', ' ', '-', '.');
            if (s.Length == 0) s = "skin";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        // ---- 当前皮肤持久化（exe 旁 skin.txt）----
        public static string ActiveFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skin.txt");

        public static string LoadActive()
        {
            try { if (File.Exists(ActiveFile)) return File.ReadAllText(ActiveFile).Trim(); } catch { }
            return "";
        }

        public static void SaveActive(string name)
        {
            try { File.WriteAllText(ActiveFile, name ?? ""); } catch { }
        }

        /// <summary>
        /// 解析皮肤里的 skin.txt 头发展现设置（name 为空/默认皮肤时清空）：
        ///   haircolor=RRGGBB   固定头发颜色（覆盖冲刺变色）
        ///   hair=0              隐藏头发
        /// zip 皮肤从 zip 内的 skin.txt 读取，散帧皮肤读目录下的 skin.txt。
        /// </summary>
        public static void LoadOptions(string name)
        {
            HideHair = false;
            HairColorOverride = null;
            if (string.IsNullOrEmpty(name)) return;
            if (TryGetSkinSource(name, out var zipPath, out var dir))
            {
                if (zipPath != null) ParseOptions(ReadZipLines(zipPath, "skin.txt"));
                else if (dir != null)
                {
                    try { ParseOptions(File.ReadAllLines(Path.Combine(dir, "skin.txt"))); } catch { }
                }
            }
        }

        static string[] ReadZipLines(string zipPath, string entryName)
        {
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var e in zip.Entries)
                    if (string.Equals(Path.GetFileName(e.FullName), entryName, StringComparison.OrdinalIgnoreCase))
                        using (var sr = new StreamReader(e.Open()))
                            return sr.ReadToEnd().Split('\n');
            }
            catch { }
            return null;
        }

        static void ParseOptions(string[] lines)
        {
            if (lines == null) return;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (key == "hair")
                {
                    HideHair = val == "0" || val.Equals("off", StringComparison.OrdinalIgnoreCase);
                }
                else if (key == "haircolor")
                {
                    string c = val.TrimStart('#');
                    if (c.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) c = c.Substring(2);
                    if (c.Length == 6 && int.TryParse(c, NumberStyles.HexNumber, null, out int rgb))
                        HairColorOverride = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                }
            }
        }

        // ================= Sprites.xml 解析 → 动画映射 =================
        // 一条动画定义：path（相对帧目录）+ 可选 frames 索引 + delay + 是否循环
        class AnimDef
        {
            public string Path;
            public List<int> Frames;   // null = 全部帧
            public float Delay;
            public bool Loop;
        }

        /// <summary>
        /// 从 mod 的 Sprites.xml 构建桌宠动画集：按默认动画的 id 逐条取 mod 对应动画的帧（缺的/没帧的回退默认定义）。
        /// 只替换帧列表；延迟/循环/手动沿用桌宠默认（保持节奏与攀爬手动帧）。返回空表则用默认动画集。
        /// </summary>
        public static Dictionary<string, Anim> BuildSkinAnims(string zipPath, Dictionary<string, Anim> defaultAnims)
        {
            var result = new Dictionary<string, Anim>(StringComparer.OrdinalIgnoreCase);
            if (zipPath == null || !File.Exists(zipPath) || defaultAnims == null) return result;
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                string frameDir = FindFrameDir(zip);
                if (frameDir == null) return result;
                var bank = FindBank(zip, frameDir);
                if (bank == null) return result;
                foreach (var kv in defaultAnims)
                {
                    string modId = ToyToModAnim(kv.Key);
                    if (modId == null || !bank.TryGetValue(modId, out var def)) { result[kv.Key] = kv.Value; continue; }
                    var frames = ExpandFrames(zip, frameDir, def);
                    if (frames.Count == 0) { result[kv.Key] = kv.Value; continue; }
                    result[kv.Key] = new Anim { Frames = frames.ToArray(), Delay = kv.Value.Delay, Loop = kv.Value.Loop, Manual = kv.Value.Manual };
                }
            }
            catch { }
            return result;
        }

        /// <summary>桌宠动画 id → mod SpriteBank 动画 id（个别命名差异）。</summary>
        static string ToyToModAnim(string toyId)
        {
            switch (toyId)
            {
                case "idle": case "wakeUp": case "idleA": case "idleB": case "idleC":
                case "runSlow": case "runFast": case "jumpSlow": case "jumpFast":
                case "fallSlow": case "fallFast": case "dash": case "wallslide":
                case "dangling": case "duck": case "lookUp": case "edge": case "flip":
                    return toyId;
                case "climb": return "climbup";
                case "climbTurn": return "climbLookBackStart";
                default: return null;
            }
        }

        /// <summary>在 zip 里找匹配帧目录的 SpriteBank（扫描所有 Sprites.xml）。返回 mod 动画 id → 定义。</summary>
        static Dictionary<string, AnimDef> FindBank(ZipArchive zip, string frameDir)
        {
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.EndsWith("Sprites.xml", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var s = e.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    ms.Position = 0;
                    var doc = new XmlDocument();
                    doc.Load(ms);
                    var d = ParseBankDoc(doc, frameDir);
                    if (d != null) return d;
                }
                catch { }
            }
            return null;
        }

        static Dictionary<string, AnimDef> ParseBankDoc(XmlDocument doc, string frameDir)
        {
            XmlElement root = doc.DocumentElement;
            if (root == null) return null;
            string want = frameDir.TrimEnd('/') + "/";
            XmlElement match = null;
            foreach (XmlNode child in root.ChildNodes)
            {
                if (child is not XmlElement el) continue;
                string p = el.GetAttribute("path");
                if (p.Length > 0 && (p.TrimEnd('/') + "/").Equals(want, StringComparison.OrdinalIgnoreCase)) { match = el; break; }
            }
            if (match == null) return null;
            var defs = new Dictionary<string, AnimDef>(StringComparer.OrdinalIgnoreCase);
            string copyName = match.GetAttribute("copy");
            if (copyName.Length > 0)   // copy="player"：先并入被复制的 bank
                foreach (XmlNode child in root.ChildNodes)
                    if (child is XmlElement el && el.Name.Equals(copyName, StringComparison.OrdinalIgnoreCase))
                        ParseAnims(el, defs);
            ParseAnims(match, defs);
            return defs;
        }

        static void ParseAnims(XmlElement bank, Dictionary<string, AnimDef> defs)
        {
            foreach (XmlNode child in bank.ChildNodes)
            {
                if (child is not XmlElement el) continue;
                if (el.Name != "Anim" && el.Name != "Loop") continue;
                string id = el.GetAttribute("id");
                string path = el.GetAttribute("path");
                if (id.Length == 0 || path.Length == 0) continue;
                var def = new AnimDef { Path = path, Loop = el.Name == "Loop" };
                string fs = el.GetAttribute("frames");
                if (fs.Length > 0)
                {
                    var idx = ParseFrames(fs);
                    if (idx.Count > 0) def.Frames = idx;
                }
                if (float.TryParse(el.GetAttribute("delay"), NumberStyles.Float, CultureInfo.InvariantCulture, out float delay))
                    def.Delay = delay;
                defs[id] = def;
            }
        }

        /// <summary>frames 属性展开：逗号分隔，"N" 单帧、"N-M" 范围、"N*C" 重复 C 次。</summary>
        static List<int> ParseFrames(string s)
        {
            var list = new List<int>();
            foreach (var tokRaw in s.Split(','))
            {
                string tok = tokRaw.Trim();
                if (tok.Length == 0) continue;
                int star = tok.IndexOf('*');
                if (star > 0)
                {
                    if (int.TryParse(tok.Substring(0, star), out int n) && int.TryParse(tok.Substring(star + 1), out int c))
                        for (int i = 0; i < c; i++) list.Add(n);
                    continue;
                }
                int dash = tok.IndexOf('-');
                if (dash > 0 && int.TryParse(tok.Substring(0, dash), out int a) && int.TryParse(tok.Substring(dash + 1), out int b))
                {
                    for (int n = a; n <= b; n++) list.Add(n);
                    continue;
                }
                if (int.TryParse(tok, out int single)) list.Add(single);
            }
            return list;
        }

        static string LastSegment(string path)
        {
            path = path.TrimEnd('/');
            int i = path.LastIndexOf('/');
            return i < 0 ? path : path.Substring(i + 1);
        }

        /// <summary>按 AnimDef 展开帧 id 列表：显式 frames 直接映射；无 frames 则枚举 mod 里对应路径的实际帧数。</summary>
        static List<string> ExpandFrames(ZipArchive zip, string frameDir, AnimDef def)
        {
            var list = new List<string>();
            string seg = LastSegment(def.Path);
            string prefix = frameDir + "/";
            if (def.Frames != null)
            {
                foreach (int n in def.Frames) list.Add(seg + n.ToString("00"));
                return list;
            }
            if (def.Path.EndsWith("/"))
            {
                // 子目录：frameDir/path/NN.png
                string subPrefix = prefix + def.Path.TrimEnd('/') + "/";
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.StartsWith(subPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(Path.GetFileNameWithoutExtension(e.FullName), out int n)) list.Add(seg + n.ToString("00"));
                }
            }
            else
            {
                // 平铺：frameDir/segNN.png（seg 后必须正好两位数字，避免误收 idleA/idleB/idle_carry）
                foreach (var e in zip.Entries)
                {
                    if (!e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    string baseName = e.FullName.Substring(prefix.Length);
                    if (baseName.IndexOf('/') >= 0) continue;
                    string fn = Path.GetFileNameWithoutExtension(baseName);
                    if (!fn.StartsWith(seg, StringComparison.OrdinalIgnoreCase)) continue;
                    string rest = fn.Substring(seg.Length);
                    if (rest.Length == 2 && int.TryParse(rest, out int n)) list.Add(fn);
                }
                list.Sort((a, b) =>
                {
                    int na = int.Parse(a.Substring(a.Length - 2));
                    int nb = int.Parse(b.Substring(b.Length - 2));
                    return na.CompareTo(nb);
                });
            }
            return list;
        }
    }
}
