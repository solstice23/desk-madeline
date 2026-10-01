using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;

namespace DeskMadeline
{
    // ============================================================================================
    // A port of SkinModHelper Plus (AAA1459/SkinModHelper) as far as it reaches the player the
    // pet draws: the sprite a skin dresses her in, the hook on Sprite.Play and the animations it
    // adds, HairConfig and CharacterConfig, colour grading, and the colours of her trail, dash
    // and death. What has no desktop counterpart -- cutscenes, portraits, CelesteNet, triggers,
    // EntityTweaks, the menus -- is left out, and says so where it would have gone.
    // ============================================================================================

    /// <summary>SkinModHelperConfig.yaml: one entry of the list.</summary>
    public sealed class SmhSkinConfig
    {
        public string SkinName { get; set; }
        public bool Player_List { get; set; }
        public bool Silhouette_List { get; set; }
        public bool General_List { get; set; } = true;
        public string Character_ID { get; set; }
        string otherSpritePath, otherSpriteExPath;
        public string OtherSprite_Path { get => otherSpritePath; set => otherSpritePath = TrimPath(value); }
        public string OtherSprite_ExPath { get => otherSpriteExPath; set => otherSpriteExPath = TrimPath(value); }
        public string SkinDialogKey { get; set; }
        public string hashSeed { get; set; }
        public string Mod { get; set; }

        static string TrimPath(string value)
        {
            if (value == null) return null;
            value = value.Replace("\\", "/");
            return value.EndsWith("/") ? value.Remove(value.Length - 1) : value;
        }

        /// <summary>SkinsSystem.getHash, plus one: the PlayerSpriteMode the skin is worn as.</summary>
        public static int Hash(string seed)
        {
            int hashValue;
            unchecked
            {
                int num = 352654597;
                int num2 = num;
                for (int i = 0; i < seed.Length; i += 2)
                {
                    num = ((num << 5) + num) ^ seed[i];
                    if (i == seed.Length - 1) break;
                    num2 = ((num2 << 5) + num2) ^ seed[i + 1];
                }
                hashValue = num + (num2 * 1566083941);
            }
            if (hashValue < 0) hashValue += (1 << 31);
            return hashValue;
        }
    }

    /// <summary>The old, one-skin SkinModHelperConfig.yaml: SkinId and its hair colours.</summary>
    public sealed class SmhOldConfig
    {
        public string SkinId { get; set; }
        public string SkinDialogKey { get; set; }
        public List<HairColor> HairColors { get; set; }
        public sealed class HairColor
        {
            public int Dashes { get; set; }
            public string Color { get; set; }
        }
    }

    /// <summary>skinConfig/HairConfig.yaml, deserialised with the same setters it has.</summary>
    public sealed class SmhHairConfigFile
    {
        public string OutlineColor { get; set; }
        public bool HairFlash { get; set; } = true;
        public int? HairFloatingDashCount { get; set; }
        public SmhHair.FlipModes HairFlipMode { get; set; } = SmhHair.FlipModes.None;

        [YamlMember(Alias = "BangsOrigin")]
        public string _BangsOrigin { get => null; set => BangsOrigin = Smh.StringToVector2(value); }
        [YamlMember(Alias = "HairOrigin")]
        public string _HairOrigin { get => null; set => HairOrigin = Smh.StringToVector2(value); }
        [YamlIgnore] public PointF BangsOrigin = new PointF(5f, 5f);
        [YamlIgnore] public PointF HairOrigin = new PointF(5f, 5f);

        public List<AttrWithDashes> HairAttrWithDashes
        {
            get => null;
            set
            {
                foreach (var attr in value)
                {
                    if (Attrs.TryGetValue(attr.Dashes, out var before))
                    {
                        attr.Color ??= before.Color;
                        attr.Length ??= before.Length;
                    }
                    Attrs[attr.Dashes] = attr;
                }
            }
        }
        [YamlIgnore] public readonly Dictionary<int, AttrWithDashes> Attrs = new Dictionary<int, AttrWithDashes>();

        public sealed class AttrWithDashes
        {
            public AttrWithDashes() { }
            public AttrWithDashes(int dashes) { Dashes = dashes; }
            public int Dashes { get; set; }
            public string Color { get; set; }
            public int? Length { get; set; }
            public string Scale { get; set; }
            public List<SegmentAttr> SegmentAttrs { get; set; } = new List<SegmentAttr>();
            public List<SegmentAttr> SegmentsColors { get => SegmentAttrs; set => SegmentAttrs = value; }

            public sealed class SegmentAttr
            {
                [YamlMember(Alias = "Segment")]
                public string _Segment
                {
                    get => Segment.ToString(CultureInfo.InvariantCulture);
                    set
                    {
                        if (int.TryParse(value, out int i)) Segment = i;
                        else if (Enum.TryParse(value, true, out SmhHair.Special special)) Segment = (int)special;
                    }
                }
                [YamlIgnore] public int Segment { get; private set; }
                public float? Scale { get; set; }
                public string Color { get; set; }
                public float? Speed { get; set; }
            }
        }

        // Backward compatibility, as HairConfig keeps it.
        public List<AttrWithDashes> HairColors { get => HairAttrWithDashes; set => HairAttrWithDashes = value; }
        public string iHairColors
        {
            get => null;
            set
            {
                string[] colors = value.Split('|').Select(c => c.Trim()).ToArray();
                for (int i = 0; i < colors.Length; i++)
                {
                    if (colors[i] == "x") continue;
                    if (!Attrs.ContainsKey(i)) Attrs[i] = new AttrWithDashes(i);
                    Attrs[i].Color = colors[i];
                }
            }
        }
        public List<HairLength> HairLengths
        {
            get => null;
            set
            {
                foreach (var item in value)
                {
                    if (!Attrs.ContainsKey(item.Dashes)) Attrs[item.Dashes] = new AttrWithDashes(item.Dashes);
                    Attrs[item.Dashes].Length = item.Length;
                }
            }
        }
        public sealed class HairLength
        {
            public int Dashes { get; set; }
            public int Length { get; set; }
        }
        public string BangsOffset { get => null; set { var v = Smh.StringToVector2(value); BangsOrigin = new PointF(BangsOrigin.X + v.X, BangsOrigin.Y + v.Y); } }
        public string HairOffset { get => null; set { var v = Smh.StringToVector2(value); HairOrigin = new PointF(HairOrigin.X + v.X, HairOrigin.Y + v.Y); } }
    }

    /// <summary>skinConfig/CharacterConfig.yaml, the parts that reach the player on a desktop.</summary>
    public sealed class SmhCharacterConfigFile
    {
        public bool? BadelineMode { get; set; }
        public bool? SilhouetteMode { get; set; }
        public bool TintMaskWithHair { get; set; }
        public int MaskMode { get; set; }
        public bool ColorGradingSuchAsPlayer { get; set; }
        public bool ColorGradingAfterColored { get; set; }
        public bool LowStaminaFlashHair { get; set; }
        public string LowStaminaFlashColor { get; set; }
        public bool HoldableFacingFlipable { get; set; }
        public string TrailsColor { get; set; }
        public string DeathParticleColor { get; set; }
        public float? IdleAnimationChance { get; set; }

        [YamlIgnore] public Chooser IdleColdOptions;
        [YamlIgnore] public Chooser IdleWarmOptions;
        [YamlIgnore] public readonly Dictionary<(string, string), string> WarpAnimationsPlay = new Dictionary<(string, string), string>();

        [YamlMember(Alias = "IdleColdOptions")]
        public List<string> _IdleColdOptions { get => null; set => IdleColdOptions = Options(value); }
        [YamlMember(Alias = "IdleWarmOptions")]
        public List<string> _IdleWarmOptions { get => null; set => IdleWarmOptions = Options(value); }
        [YamlMember(Alias = "WarpAnimationsPlay")]
        public List<string> _WarpAnimationsPlay
        {
            get => null;
            set
            {
                foreach (string option in value)
                {
                    string[] parts = option.Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length < 2) continue;
                    string when = parts[0], then = parts[parts.Length - 1];
                    if (string.IsNullOrEmpty(then)) then = "_";
                    for (int i = parts.Length - 2; i > 0; i--) WarpAnimationsPlay[(when, parts[i])] = then;
                }
            }
        }

        public List<SmhParticleModifier> ParticleModify { get; set; }

        static Chooser Options(List<string> value)
        {
            var chooser = new Chooser();
            foreach (string option in value)
            {
                string[] parts = option.Split(new[] { ',' }, 2).Select(p => p.Trim()).ToArray();
                float.TryParse(parts.Length == 2 ? parts[1] : "3", NumberStyles.Float, CultureInfo.InvariantCulture, out float weight);
                chooser.Add(parts[0].StartsWith("idle") ? parts[0] : "idle" + parts[0], Math.Max(0, weight));
            }
            return chooser;
        }
    }

    /// <summary>CharacterConfig.particleModifier: the fields a modified ParticleType takes.</summary>
    public sealed class SmhParticleModifier
    {
        public string TargetFullName { get; set; }
        public bool IsStatic { get; set; } = true;
        public string Source { get; set; }
        public List<string> SourceChooser { get; set; }
        public string Color { get; set; }
        public string Color2 { get; set; }
        public string ColorMode { get; set; }
        public string FadeMode { get; set; }
        public float? SpeedMin { get; set; }
        public float? SpeedMax { get; set; }
        public float? SpeedMultiplier { get; set; }
        public string Acceleration { get; set; }
        public float? Friction { get; set; }
        public float? Direction { get; set; }
        public float? DirectionRange { get; set; }
        public float? LifeMin { get; set; }
        public float? LifeMax { get; set; }
        public float? Size { get; set; }
        public float? SizeRange { get; set; }
        public float? SpinMin { get; set; }
        public float? SpinMax { get; set; }
        public bool? SpinFlippedChance { get; set; }
        public string RotationMode { get; set; }
        public bool? ScaleOut { get; set; }
        public bool? UseActualDeltaTime { get; set; }
    }

    /// <summary>SkinsSystem's helpers, and the colour arithmetic of XNA's Color.</summary>
    public static class Smh
    {
        public const string PlayerCipher = "_+";
        public const int MaxHairLength = 99;

        public static bool RGB_IsMatch(string s) => s != null && s.All(Uri.IsHexDigit) && s.Length == 6;
        public static bool RGBA_IsMatch(string s) => s != null && s.All(Uri.IsHexDigit) && (s.Length == 6 || s.Length == 8);

        static int Hex(char c) => Convert.ToInt32(c.ToString(), 16);

        /// <summary>Calc.HexToColor.</summary>
        public static Color HexToColor(string hex)
        {
            int n = hex.Length >= 1 && hex[0] == '#' ? 1 : 0;
            if (hex.Length - n >= 6)
                return Color.FromArgb(Hex(hex[n]) * 16 + Hex(hex[n + 1]), Hex(hex[n + 2]) * 16 + Hex(hex[n + 3]),
                    Hex(hex[n + 4]) * 16 + Hex(hex[n + 5]));
            return Color.White;
        }

        /// <summary>Calc.HexToColorWithAlpha: RRGGBB, RRGGBBAA, or AA alone over white.</summary>
        public static Color HexToColorWithAlpha(string hex)
        {
            int n = hex.Length >= 1 && hex[0] == '#' ? 1 : 0;
            switch (hex.Length - n)
            {
                case 2: return Color.FromArgb(Hex(hex[n]) * 16 + Hex(hex[n + 1]), 255, 255, 255);
                case 6: return HexToColor(hex);
                case 8:
                    return Color.FromArgb(Hex(hex[n + 6]) * 16 + Hex(hex[n + 7]), Hex(hex[n]) * 16 + Hex(hex[n + 1]),
                        Hex(hex[n + 2]) * 16 + Hex(hex[n + 3]), Hex(hex[n + 4]) * 16 + Hex(hex[n + 5]));
                default: return Color.White;
            }
        }

        public static PointF StringToVector2(string value)
        {
            string[] parts = value?.Split(new[] { ',' }, 2).Select(p => p.Trim()).ToArray();
            if (parts != null && parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                return new PointF(x, y);
            return PointF.Empty;
        }

        /// <summary>XNA Color * float: every channel, alpha included, scaled and clamped.</summary>
        public static Color Mul(Color c, float f) => Color.FromArgb(Clamp(c.A * f), Clamp(c.R * f), Clamp(c.G * f), Clamp(c.B * f));
        static int Clamp(float v) => v < 0 ? 0 : v > 255 ? 255 : (int)v;

        public static float GetAlpha(Color c) => c.A == 0 ? 0f : c.A / 255f;

        /// <summary>
        /// SkinsSystem.ColorBlend: multiply by a colour at its own full brightness (as the first
        /// colour's opacity), or by a brightness when given a float.
        /// </summary>
        public static Color ColorBlend(Color c1, object obj)
        {
            if (obj is Color c2 && c2.A != 0)
            {
                c2 = Mul(Mul(c2, 255f / c2.A), GetAlpha(c1));
                return Color.FromArgb(c1.A, c1.R * c2.R / 255, c1.G * c2.G / 255, c1.B * c2.B / 255);
            }
            if (obj is float f)
            {
                if (f > 1f) return Lerp(c1, Color.White, f);
                return Lerp(Color.Black, c1, f);
            }
            return c1;
        }

        /// <summary>XNA Color.Lerp: each channel lerped, then truncated; the amount clamped.</summary>
        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static readonly IDeserializer yaml = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

        /// <summary>Everest's YamlHelper: unknown keys are ignored.</summary>
        public static T Deserialize<T>(string text) => yaml.Deserialize<T>(text);
    }

    /// <summary>
    /// SkinModHelperShader.fx on the CPU: DoCG's lookup into a 256x16 colour grade and MixHair's
    /// tint of a mask colour, in the three techniques the hooks choose between. Textures are
    /// premultiplied, as the game's are, and every result is rounded to the eight bits a render
    /// target would hold.
    /// </summary>
    public static class SmhShader
    {
        struct Key : IEquatable<Key>
        {
            public Bitmap Source, Grade;
            public bool AfterColored;
            public int Tint, Mix, MaskMode;
            public bool Equals(Key o) => ReferenceEquals(Source, o.Source) && ReferenceEquals(Grade, o.Grade) &&
                AfterColored == o.AfterColored && Tint == o.Tint && Mix == o.Mix && MaskMode == o.MaskMode;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(Source, Grade, AfterColored, Tint, Mix, MaskMode);
        }

        static readonly Dictionary<Key, Bitmap> cache = new Dictionary<Key, Bitmap>();

        public static void Clear()
        {
            foreach (Bitmap b in cache.Values) b.Dispose();
            cache.Clear();
        }

        /// <summary>
        /// The texture as the shader leaves it before the sprite's colour is multiplied in -- or,
        /// for ColorGradeAftColored, with that colour already in, to be drawn untinted.
        /// </summary>
        /// <param name="mixHair">MixHair's haircolor when TintMaskWithHair is on, null when it is off.</param>
        public static Bitmap Apply(Bitmap source, Bitmap grade, bool afterColored, Color tint, Color? mixHair, int maskMode)
        {
            if (source == null || (grade == null && mixHair == null)) return source;
            var key = new Key
            {
                Source = source, Grade = grade, AfterColored = afterColored && grade != null,
                Tint = afterColored && grade != null ? tint.ToArgb() : 0, Mix = mixHair?.ToArgb() ?? 0,
                MaskMode = mixHair == null ? -1 : maskMode,
            };
            if (cache.TryGetValue(key, out Bitmap done)) return done;

            int w = source.Width, h = source.Height;
            var result = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var src = Read(source);
            var lut = grade == null ? null : Read(grade);
            int gw = grade?.Width ?? 0, gh = grade?.Height ?? 0;
            var output = new byte[w * h * 4];
            float tr = tint.R / 255f, tg = tint.G / 255f, tb = tint.B / 255f, ta = tint.A / 255f;
            float hr = 0, hg = 0, hb = 0, ha = 0;
            if (mixHair is Color hc) { hr = hc.R / 255f; hg = hc.G / 255f; hb = hc.B / 255f; ha = hc.A / 255f; }

            for (int i = 0; i < w * h; i++)
            {
                // Bitmap bytes are B, G, R, A.
                float b = src[i * 4] / 255f, g = src[i * 4 + 1] / 255f, r = src[i * 4 + 2] / 255f, a = src[i * 4 + 3] / 255f;
                if (grade == null)
                    MixHair(ref r, ref g, ref b, ref a);
                else if (!key.AfterColored)
                {
                    DoCG(ref r, ref g, ref b, ref a);
                    MixHair(ref r, ref g, ref b, ref a);
                }
                else
                {
                    MixHair(ref r, ref g, ref b, ref a);
                    r *= tr; g *= tg; b *= tb; a *= ta;
                    float un = 1f / Math.Max(ta, 1f / 256f);
                    r *= un; g *= un; b *= un; a *= un;
                    DoCG(ref r, ref g, ref b, ref a);
                    r *= ta; g *= ta; b *= ta; a *= ta;
                }
                output[i * 4] = Byte(b); output[i * 4 + 1] = Byte(g); output[i * 4 + 2] = Byte(r); output[i * 4 + 3] = Byte(a);
            }
            Write(result, output);
            cache[key] = result;
            return result;

            void DoCG(ref float r, ref float g, ref float b, ref float a)
            {
                int x = (int)(r * 15f + 0.5f), z = (int)(b * 15f + 0.5f), y = (int)(g * 15f + 0.5f);
                // float2((x + z*16 + .5)/256, (y + .5)/16), point-sampled with wrap.
                int u = ((int)((x + z * 16 + 0.5f) / 256f * gw) % gw + gw) % gw;
                int v = ((int)((y + 0.5f) / 16f * gh) % gh + gh) % gh;
                int at = (v * gw + u) * 4;
                float pa = a;
                b = lut[at] / 255f * pa; g = lut[at + 1] / 255f * pa; r = lut[at + 2] / 255f * pa; a = lut[at + 3] / 255f * pa;
            }

            void MixHair(ref float r, ref float g, ref float b, ref float a)
            {
                if (mixHair == null) return;
                if (maskMode > 2)
                {
                    if (r == b && r == g) { r *= hr; g *= hg; b *= hb; a *= ha; }
                    return;
                }
                float f = r + g + b;
                float channel = maskMode == 0 ? r : maskMode == 1 ? g : b;
                if (f == channel) { r = f * hr; g = f * hg; b = f * hb; a *= ha; }
            }
        }

        static byte Byte(float v) => (byte)Math.Max(0, Math.Min(255, (int)(v * 255f + 0.5f)));

        static byte[] Read(Bitmap bitmap)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var bytes = new byte[bitmap.Width * bitmap.Height * 4];
            for (int y = 0; y < bitmap.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
            bitmap.UnlockBits(data);
            return bytes;
        }

        static void Write(Bitmap bitmap, byte[] bytes)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            for (int y = 0; y < bitmap.Height; y++)
                System.Runtime.InteropServices.Marshal.Copy(bytes, y * bitmap.Width * 4, data.Scan0 + y * data.Stride, bitmap.Width * 4);
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>HairConfig, at run time: what the hair of a skinned player looks like.</summary>
    public sealed class SmhHair
    {
        public enum Special { General = 100, Trail = 101, DashPtcl = 102, Outline = -101, Flash = -102 }
        public enum FlipModes { None, SyncBangs, FacingBangs, FacingPrevHair }
        public const int FeatherIndex = -1;
        public static readonly Color EmptyS = Color.FromArgb(0, 255, 255, 255);

        public readonly SmhHairConfigFile File;
        public List<string> NewBangs, NewHairs;
        public Dictionary<(int, int), Color> ActualHairColors;
        public Dictionary<int, int> ActualHairLengths;
        public Dictionary<(int, int), PointF> ActualHairScales;
        int attrDashesLimit = 2;
        public bool HairFlashing;
        public bool HasZeroDashFlash;
        public int? LastDashes;
        /// <summary>A Color or a float: CharacterConfig's low-stamina flash, laid over the hair.</summary>
        public object HairColorGrading;
        public int LastHairCount;
        public Color Border = Color.Black;

        public static readonly Color ZeroDashesColor = Smh.HexToColor("44B7FF");
        public static readonly Color OneDashesColor = Smh.HexToColor("AC3232");
        public static readonly Color TwoDashesColor = Smh.HexToColor("FF6DEF");

        public SmhHair(SmhHairConfigFile file) { File = file ?? new SmhHairConfigFile(); }

        public PointF BangsOrigin => File.BangsOrigin;
        public PointF HairOrigin => File.HairOrigin;

        void InitHairColor()
        {
            ActualHairColors ??= new Dictionary<(int, int), Color>
            {
                [(0, (int)Special.General)] = ZeroDashesColor,
                [(1, (int)Special.General)] = OneDashesColor,
                [(2, (int)Special.General)] = TwoDashesColor,
            };
        }

        /// <summary>HairConfig.InitAttrsWithDashes.</summary>
        public void InitAttrsWithDashes(bool hasColorGrading)
        {
            if (!File.HairFlash || hasColorGrading) InitHairColor();
            foreach (var attr in File.Attrs.Values)
            {
                bool valid = false;
                bool empty;
                if ((empty = attr.Color == "orig") || Smh.RGB_IsMatch(attr.Color))
                {
                    InitHairColor();
                    ActualHairColors[(attr.Dashes, (int)Special.General)] = empty ? EmptyS : Smh.HexToColor(attr.Color);
                    foreach (var seg in attr.SegmentAttrs)
                        if (seg.Segment != (int)Special.General && ((empty = seg.Color == "orig") || Smh.RGB_IsMatch(seg.Color)))
                            ActualHairColors[(attr.Dashes, seg.Segment)] = empty ? EmptyS : Smh.HexToColor(seg.Color);
                    valid = true;
                }
                if (attr.Length != null)
                {
                    (ActualHairLengths ??= new Dictionary<int, int>())[attr.Dashes] = Math.Max(1, Math.Min(Smh.MaxHairLength, attr.Length.Value));
                    valid = true;
                }
                if (attr.Scale != null)
                {
                    string[] arr = attr.Scale.Split(new[] { ',' }, 2).Select(p => p.Trim()).ToArray();
                    if (float.TryParse(arr[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
                    {
                        ActualHairScales ??= new Dictionary<(int, int), PointF>();
                        if (arr.Length < 2 || !float.TryParse(arr[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale2))
                            scale2 = scale;
                        ActualHairScales[(attr.Dashes, (int)Special.General)] = new PointF(scale, scale2);
                        foreach (var seg in attr.SegmentAttrs)
                            if (seg.Scale is float f) ActualHairScales[(attr.Dashes, seg.Segment)] = new PointF(f, f);
                        valid = true;
                    }
                }
                if (valid && attr.Dashes > attrDashesLimit) attrDashesLimit = attr.Dashes;
            }
            HasZeroDashFlash = ActualHairColors?.ContainsKey((0, (int)Special.Flash)) ?? false;
        }

        /// <summary>HairConfig.Old_BuildHairColors, for a skin of the old config.</summary>
        public void OldBuildHairColors(List<SmhOldConfig.HairColor> colors)
        {
            ActualHairColors = new Dictionary<(int, int), Color>
            {
                [(0, (int)Special.General)] = ZeroDashesColor,
                [(1, (int)Special.General)] = OneDashesColor,
                [(2, (int)Special.General)] = TwoDashesColor,
            };
            if (colors == null) return;
            foreach (var hc in colors)
                if (hc.Dashes >= 0 && Smh.RGB_IsMatch(hc.Color))
                {
                    ActualHairColors[(hc.Dashes, (int)Special.General)] = Smh.HexToColor(hc.Color);
                    if (attrDashesLimit < hc.Dashes) attrDashesLimit = hc.Dashes;
                }
        }

        /// <summary>HairConfig.Safe_GetHairColor(dashes): the general colour, falling back a dash at a time.</summary>
        public bool SafeGetHairColor(int dashes, out Color color)
        {
            color = default;
            if (ActualHairColors == null) return false;
            dashes = Math.Min(attrDashesLimit, dashes);
            while (true)
            {
                if (ActualHairColors.TryGetValue((dashes, (int)Special.General), out color)) return color != EmptyS;
                if (dashes <= 0) return false;
                dashes--;
            }
        }

        /// <summary>HairConfig.Safe_GetHairColor(index, dashes): a segment's colour over the general one.</summary>
        public bool SafeGetHairColor(int index, int dashes, int hairCount, out Color color)
        {
            color = default;
            if (ActualHairColors == null) return false;
            dashes = Math.Min(attrDashesLimit, dashes);
            while (true)
            {
                if (ActualHairColors.TryGetValue((dashes, (int)Special.General), out color))
                {
                    if ((index < (int)Special.General && ActualHairColors.TryGetValue((dashes, index - hairCount), out Color c2)) ||
                        ActualHairColors.TryGetValue((dashes, index), out c2))
                        color = c2;
                    return color != EmptyS;
                }
                if (dashes <= 2) return false;
                dashes--;
            }
        }

        /// <summary>HairConfig.GetHairColorWithSpecified.</summary>
        public bool GetHairColorWithSpecified(int index, int dashes, out Color color)
        {
            color = default;
            if (ActualHairColors == null) return false;
            dashes = Math.Min(attrDashesLimit, dashes);
            while (true)
            {
                if (ActualHairColors.TryGetValue((dashes, index), out color)) return color != EmptyS;
                if (dashes <= 2) return false;
                dashes--;
            }
        }

        /// <summary>HairConfig.GetHairLength.</summary>
        public int? GetHairLength(int? dashCount)
        {
            if (dashCount == null || ActualHairLengths == null) return null;
            int dashes = Math.Min(attrDashesLimit, dashCount.Value);
            while (true)
            {
                if (ActualHairLengths.TryGetValue(dashes, out int length)) return length;
                if (dashes <= 2) return null;
                dashes--;
            }
        }

        /// <summary>HairConfig.GetHairScale: interpolated root to end, by segment.</summary>
        public bool GetHairScale(int index, int dashes, int hairCount, float spriteScaleX, out PointF scale)
        {
            scale = PointF.Empty;
            if (ActualHairScales == null || index == 0) return false;
            dashes = Math.Min(attrDashesLimit, dashes);
            while (true)
            {
                if (ActualHairScales.TryGetValue((dashes, (int)Special.General), out scale))
                {
                    if ((index < (int)Special.General && ActualHairScales.TryGetValue((dashes, index - hairCount), out PointF v)) ||
                        ActualHairScales.TryGetValue((dashes, index), out v))
                        scale = v;
                    float num = scale.Y + (1f - (float)index / hairCount) * (scale.X - scale.Y);
                    scale = new PointF(num * Math.Abs(spriteScaleX), num);
                    return true;
                }
                if (dashes <= 2) return false;
                dashes--;
            }
        }

        /// <summary>HairConfig.GetHairScaleWithSpecified.</summary>
        public bool GetHairScaleWithSpecified(int index, int dashes, out PointF scale)
        {
            scale = PointF.Empty;
            if (ActualHairScales == null || index == 0) return false;
            dashes = Math.Min(attrDashesLimit, dashes);
            while (true)
            {
                if (ActualHairScales.TryGetValue((dashes, index), out scale))
                {
                    scale = new PointF(scale.X, scale.X);
                    return true;
                }
                if (dashes <= 2) return false;
                dashes--;
            }
        }

        /// <summary>HairConfig.FlipHair.</summary>
        public PointF FlipHair(PointF scale, int index, int facing, IReadOnlyList<PointF> nodes)
        {
            if (index <= 0) return scale;
            switch (File.HairFlipMode)
            {
                case FlipModes.SyncBangs: scale.X *= facing; break;
                case FlipModes.FacingBangs:
                    scale.X *= nodes[index].X - nodes[0].X < 0f ? 1 : -1; break;
                case FlipModes.FacingPrevHair:
                    scale.X *= nodes[index].X - nodes[index - 1].X < 0f ? 1 : -1; break;
            }
            return scale;
        }
    }

    /// <summary>
    /// A skin of SkinModHelper's worn by the player: the sprite it dresses her in, and its hooks
    /// on her animation, hair, colours and effects.
    /// </summary>
    public sealed class SmhSkin
    {
        /// <summary>The bank entry she wears: Character_ID, or player plus the old skin's name.</summary>
        public readonly string SpriteName;
        public readonly string SkinName;
        /// <summary>PlayerSpriteMode the skin is worn as: its hash value plus one.</summary>
        public readonly int Mode;
        public readonly bool OldConfig;
        public readonly SmhHair Hair;
        public readonly SmhCharacterConfigFile Character;
        /// <summary>SpriteDataCache: the entry's source paths, override path before path, in order.</summary>
        readonly List<string> sourcePaths;
        readonly string gameplayDirectory;
        readonly SpriteBank bank;
        readonly IFrameAtlas atlas;
        /// <summary>SkinsSystem.VanillaCharacterTextures: every frame of the game's own player sprites.</summary>
        readonly HashSet<string> vanillaCharacterTextures;

        public SmhSkin(SpriteBank bank, IFrameAtlas atlas, string spriteName, string skinName, int mode,
            string gameplayDirectory, SmhOldConfig oldConfig, string oldExPath)
        {
            this.bank = bank;
            this.atlas = atlas;
            SpriteName = spriteName;
            SkinName = skinName;
            Mode = mode;
            this.gameplayDirectory = gameplayDirectory;
            OldConfig = oldConfig != null;

            sourcePaths = new List<string>();
            if (bank.SpriteData.TryGetValue(spriteName, out SpriteData data))
                foreach (SpriteDataSource source in data.Sources)
                {
                    sourcePaths.Add(source.OverridePath);
                    sourcePaths.Add(source.Path);
                }

            vanillaCharacterTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in new[] { "player", "badeline", "player_badeline", "player_playback", "player_no_backpack" })
                if (bank.SpriteData.TryGetValue(id, out SpriteData vanilla))
                    foreach (SpriteAnimation anim in vanilla.Sprite.Animations.Values)
                        foreach (string frame in anim.Frames) vanillaCharacterTextures.Add(frame);

            // HairConfig.For.
            if (OldConfig)
            {
                Hair = new SmhHair(new SmhHairConfigFile { HairFlash = false });
                string hairPath = oldExPath + "/characters/player/";
                if (AtlasLookup.HasSubtextures(atlas, hairPath + "bangs")) Hair.NewBangs = AtlasLookup.Subtextures(atlas, hairPath + "bangs");
                if (AtlasLookup.HasSubtextures(atlas, hairPath + "hair")) Hair.NewHairs = AtlasLookup.Subtextures(atlas, hairPath + "hair");
                Hair.OldBuildHairColors(oldConfig.HairColors);
            }
            else
            {
                Hair = new SmhHair(Load<SmhHairConfigFile>("skinConfig/HairConfig"));
                if (TexturesOnSprite("bangs", out var bangs) && bangs[0] != "characters/player/bangs00") Hair.NewBangs = bangs;
                if (TexturesOnSprite("hair", out var hairs) && hairs[0] != "characters/player/hair00") Hair.NewHairs = hairs;
                Hair.InitAttrsWithDashes(HasDirectory(RootPath("idle") + "ColorGrading"));
            }
            Hair.Border = Smh.RGBA_IsMatch(Hair.File.OutlineColor) ? Smh.HexToColorWithAlpha(Hair.File.OutlineColor) : Color.Black;

            // CharacterConfig.BindCharacterConfig, then ModeInitialize and RefreshConflict.
            Character = Load<SmhCharacterConfigFile>("skinConfig/CharacterConfig") ?? new SmhCharacterConfigFile();
            Character.BadelineMode ??= Mode == Player.ModeBadeline || Mode == Player.ModeMadelineAsBadeline;
            Character.SilhouetteMode ??= Mode == Player.ModePlayback;
            if (Character.TintMaskWithHair) Character.SilhouetteMode = false;
            else if (Character.SilhouetteMode == true) Character.LowStaminaFlashHair = true;
        }

        // ---------------------------------------------------------------- where its files are

        /// <summary>getAnimationRootPath(sprite): the first source's override path, else its path.</summary>
        public string RootPath() => sourcePaths.Count > 1 ? sourcePaths[0] ?? sourcePaths[1] ?? "" : "";

        /// <summary>getAnimationRootPath(sprite, id): the folder of the animation's first frame.</summary>
        public string RootPath(string animation)
        {
            if (bank.SpriteData.TryGetValue(SpriteName, out SpriteData data) &&
                data.Sprite.Animations.TryGetValue(animation, out SpriteAnimation anim) && anim.Frames.Length > 0)
            {
                string frame = anim.Frames[0];
                int slash = frame.LastIndexOf('/');
                return slash >= 0 ? frame.Substring(0, slash + 1) : "";
            }
            return RootPath();
        }

        string File(string subpath, string name)
        {
            if (gameplayDirectory == null) return null;
            foreach (string ext in new[] { ".yaml", ".yml" })
            {
                string full = Path.Combine(gameplayDirectory, (subpath + name).Replace('/', Path.DirectorySeparatorChar) + ext);
                if (System.IO.File.Exists(full)) return full;
            }
            return null;
        }

        /// <summary>GetAssetOnSprite: the first source path the file is found under.</summary>
        T Load<T>(string name) where T : class
        {
            foreach (string subpath in sourcePaths.Skip(0))
            {
                if (string.IsNullOrEmpty(subpath)) continue;
                string file = File(subpath, name);
                if (file == null) continue;
                try { return Smh.Deserialize<T>(System.IO.File.ReadAllText(file)); }
                catch (Exception ex) { PetWindow.Log("skin config unreadable " + file + ": " + ex.Message); return null; }
            }
            return null;
        }

        bool HasDirectory(string path)
            => gameplayDirectory != null && Directory.Exists(Path.Combine(gameplayDirectory, path.Replace('/', Path.DirectorySeparatorChar)));

        /// <summary>GetTexturesOnSprite.</summary>
        public bool TexturesOnSprite(string name, out List<string> textures)
        {
            textures = null;
            foreach (string path in sourcePaths)
                if (!string.IsNullOrEmpty(path) && AtlasLookup.HasSubtextures(atlas, path + name))
                {
                    textures = AtlasLookup.Subtextures(atlas, path + name);
                    return true;
                }
            return false;
        }

        /// <summary>GetTextureOnSprite.</summary>
        public bool TextureOnSprite(string name, out string texture)
        {
            texture = null;
            foreach (string path in sourcePaths)
                if (!string.IsNullOrEmpty(path) && atlas.Has(path + name))
                {
                    texture = path + name;
                    return true;
                }
            return false;
        }

        // ---------------------------------------------------------------- the player's lambdas

        public float? IdleAnimationChance => Character.IdleAnimationChance;
        public Chooser IdleColdOptions => Character.IdleColdOptions;

        /// <summary>actualBackpack: a skin whose name does not end in _NB has its backpack.</summary>
        bool ActualBackpack => !(SkinName?.EndsWith("_NB") ?? false);

        /// <summary>_patchSpriteMode_NB_V.</summary>
        public int PatchModeIdleOptions(int mode)
            => ActualBackpack || Character.IdleWarmOptions != null ? Player.ModeMadeline : mode;

        /// <summary>_patchSpriteMode_NB.</summary>
        public int PatchModeNoBackpack(int mode) => ActualBackpack ? Player.ModeMadeline : Player.ModeMadelineNoBackpack;

        /// <summary>_patchSpriteMode_Bad, in UpdateHair, DashUpdate and GetTrailColor.</summary>
        public int PatchModeBadeline(int mode)
            => Character.BadelineMode is bool b ? (b ? Player.ModeMadelineAsBadeline : Player.ModeMadeline) : mode;

        // ---------------------------------------------------------------- Sprite.Play

        /// <summary>
        /// SomePatches.PlayerSpritePlayHook: the animations SkinModHelper adds where a skin has
        /// them, the ones it holds on to, and the vanilla animation lent to a skin that lacks one.
        /// The animation prefix of its triggers is never set on a desktop, so it is left out.
        /// </summary>
        public void Play(Player player, string id, bool restart, bool randomizeFrame)
        {
            GameSprite self = player.Sprite;
            string origID = id;
            bool swimCheck = player.SwimCheckForSkin;

            switch (id)
            {
                case "walk":
                    if (player.Holding != null) id = "runSlow_carry";
                    break;
                case "dash":
                    id = DashDirAnim(player, swimCheck && self.Has("swimDash") ? "swimDash" : "dash", id);
                    break;
                case "duck":
                    if (player.IsDashAttacking)
                    {
                        if (player.DashStartedOnGround && player.DashDir.Y >= 0f && player.DashDir.X != 0f &&
                            (player.DashDir.X > 0f ? 1 : -1) == player.Facing && self.Has("dashSlide"))
                            id = "dashSlide";
                        else if (swimCheck && self.Has("swimDashCrouch")) id = DashDirAnim(player, "swimDashCrouch", id);
                        else id = DashDirAnim(player, "dashCrouch", id);
                    }
                    break;
                case "idle":
                case "edge":
                case "edgeBack":
                    if (player.State == Player.StDash && player.DashDir.Y > 0f && player.DashDir.X == 0f && self.Has("dashGrounded"))
                        id = "dashGrounded";
                    break;
                case "swimUp":
                case "swimDown":
                    if (player.WallSpeedRetentionTimer <= 0f) goto case "swimIdle";
                    break;
                case "swimIdle":
                    if ((player.Speed.X != 0 || player.MoveXForSkin != 0) && Math.Abs(player.Speed.X) >= Math.Abs(player.Speed.Y) && self.Has("swimSide"))
                        id = "swimSide";
                    break;
                case "dreamDashIn":
                case "dreamDashOut":
                    id = DashDirAnim(player, id, id);
                    break;
            }
            // "Universal code... if you are theo smuggle enthusiast..."
            if (player.Holding != null && !id.EndsWith("_carry") && self.Has(id + "_carry")) id += "_carry";

            if (!restart && self.LastAnimationID != null)
            {
                if (origID == "dreamDashOut")
                {
                    if (self.CurrentAnimationID.Contains(id)) origID = id;
                    if (self.LastAnimationID.Contains(id))
                    {
                        self.LastAnimationID = origID;
                        return;
                    }
                }
                else
                {
                    if (origID != id || id == "duck" || id == "lookUp")
                    {
                        if (id == self.LastAnimationID) return;
                        if (self.Animations.TryGetValue(id, out SpriteAnimation animation) && animation.Goto != null)
                            foreach (var choice in animation.Goto.Choices)
                                if (self.LastAnimationID == choice.Value) return;
                    }
                    if (Character.WarpAnimationsPlay.TryGetValue((self.LastAnimationID, id), out string warp))
                    {
                        if (warp == "_") return;
                        id = warp;
                    }
                    string last = self.LastAnimationID;
                    bool JumpCrazy() => last.Contains("jumpCrazy") && (!player.onGround || !player.OnGroundForSkin);
                    bool JumpHyper() => (last.Contains("jumpHyper") || last.Contains("jumpSuper")) &&
                        (!player.WasOnGround || player.Speed.Y < 0f) &&
                        (Math.Abs(player.Speed.X) > 110f || (player.WallSpeedRetentionTimer > 0f && Math.Abs(player.WallSpeedRetained) > 110f));
                    bool WallBounce() => !player.onGround && last.Contains("wallBounce");
                    switch (origID)
                    {
                        case "runStumble": return;
                        case "jumpFast": if (JumpCrazy() || JumpHyper() || WallBounce()) return; break;
                        case "fallSlow": if (JumpCrazy() || WallBounce()) return; break;
                        case "runFast":
                        case "runWind": if (JumpCrazy() || JumpHyper()) return; break;
                        case "jumpSlow":
                        case "fallFast": if (JumpHyper() || WallBounce()) return; break;
                        case "idle": if (JumpHyper()) return; break;
                        case "duck": if (!player.StartedDashing && JumpHyper()) return; break;
                    }
                }
            }

            // Final: an animation the skin does not have is lent from the game's player sprite.
            if (!self.Has(id))
            {
                PetWindow.Log($"'{SpriteName}' missing animation: {id}");
                SpriteAnimation lent = null;
                GameSprite from = null;
                if (bank.SpriteData.TryGetValue("player", out SpriteData p) && p.Sprite.Animations.TryGetValue(id, out lent)) from = p.Sprite;
                else if (bank.SpriteData.TryGetValue("player_no_backpack", out SpriteData nb) && nb.Sprite.Animations.TryGetValue(id, out lent)) from = nb.Sprite;
                if (from == null) return;
                self.Animations[id] = lent;
                PatchSprite(from, self);
                if (bank.SpriteData.TryGetValue(SpriteName, out SpriteData own)) PatchSprite(from, own.Sprite);
            }
            self.Play(id, restart, randomizeFrame);
        }

        /// <summary>dashDirAnim: the base animation, or its _Up/_Down/_Side/_SideUp/_SideDown.</summary>
        static string DashDirAnim(Player player, string baseID, string id)
        {
            GameSprite self = player.Sprite;
            if (self.Has(baseID)) id = baseID;
            if (Math.Abs(player.DashDir.X) < 0.3f)
            {
                if (player.DashDir.Y <= -0.5f) { if (self.Has(baseID + "_Up")) id = baseID + "_Up"; }
                else if (player.DashDir.Y >= 0.5f && self.Has(baseID + "_Down")) id = baseID + "_Down";
            }
            else
            {
                if (self.Has(baseID + "_Side")) id = baseID + "_Side";
                if (player.DashDir.Y <= -0.5f) { if (self.Has(baseID + "_SideUp")) id = baseID + "_SideUp"; }
                else if (player.DashDir.Y >= 0.5f && self.Has(baseID + "_SideDown")) id = baseID + "_SideDown";
            }
            return id;
        }

        /// <summary>SkinsSystem.PatchSprite: the animations of one sprite the other lacks.</summary>
        public static void PatchSprite(GameSprite from, GameSprite to)
        {
            foreach (var pair in from.Animations.ToList())
                if (!to.Animations.ContainsKey(pair.Key)) to.Animations[pair.Key] = pair.Value;
        }

        /// <summary>PlayerSuperJumpHook: a hyper or super of the skin's own, where it has one.</summary>
        public void AfterSuperJump(Player player, bool hyper)
        {
            if (player.Sprite.CurrentAnimationID.Contains("dreamDashOut")) return;
            if (!TryPlay(player, hyper ? "jumpHyper" : "jumpSuper")) TryPlay(player, "jumpCrazy");
        }

        /// <summary>PlayerSuperWallJumpHook: a wall bounce of the skin's own, where it has one.</summary>
        public void AfterSuperWallJump(Player player)
        {
            if (player.Sprite.CurrentAnimationID.Contains("dreamDashOut")) return;
            if (!TryPlay(player, "wallBounce")) TryPlay(player, "jumpCrazy");
        }

        bool TryPlay(Player player, string id)
        {
            if (!player.Sprite.Has(id)) return false;
            player.PlaySprite(id);
            return true;
        }

        // ---------------------------------------------------------------- the hair

        /// <summary>PlayerHairGetHairTextureHook, over PlayerHair.GetHairTexture.</summary>
        public string GetHairTexture(int index, int hairFrame, int hairCount, string currentTexture, string vanillaBangs, string vanillaHair)
        {
            if (Hair.NewBangs != null && currentTexture != null && vanillaCharacterTextures.Contains(currentTexture))
                return index == 0 ? vanillaBangs : vanillaHair;
            if (index == 0)
            {
                if (Hair.NewBangs != null) return Hair.NewBangs.Count > hairFrame ? Hair.NewBangs[hairFrame] : Hair.NewBangs[0];
            }
            else if (Hair.NewHairs != null)
            {
                string hair = Hair.NewHairs.Count > hairFrame ? Hair.NewHairs[hairFrame] : Hair.NewHairs[0];
                string name = hair + "_" + (index - hairCount);
                if (atlas.Has(name) || atlas.Has(name = hair + "_" + index)) return name;
                return hair;
            }
            return index == 0 ? vanillaBangs : vanillaHair;
        }

        // ---------------------------------------------------------------- colour grading

        /// <summary>CharacterConfig.ColorGrade_Path for this frame, as the hair's render hook chose it.</summary>
        public string CurrentGrade;

        /// <summary>PlayerHairRenderHook_ColorGrade's choice, made every frame the player is drawn.</summary>
        public void ChooseColorGrade(int dashCount, bool flashing) => CurrentGrade = ColorGradeFor(dashCount, flashing);

        /// <summary>
        /// Player.Render's red, through _pLowStaminaFlash: the skin's flash colour, a silhouette's
        /// darkened hair, and the hair graded the same way when LowStaminaFlashHair is on.
        /// </summary>
        public Color LowStaminaFlash(Color hairColor)
        {
            Color color = Color.Red;
            object backup = Color.Red;
            if (Smh.RGB_IsMatch(Character.LowStaminaFlashColor))
            {
                backup = color = Smh.HexToColor(Character.LowStaminaFlashColor);
                if (Character.SilhouetteMode == true) color = Smh.ColorBlend(hairColor, color);
            }
            else if (Character.SilhouetteMode == true)
            {
                backup = 0.4f;
                color = Smh.ColorBlend(hairColor, 0.4f);
            }
            if (Character.LowStaminaFlashHair) Hair.HairColorGrading = backup ?? color;
            return color;
        }

        /// <summary>The ColorGrading folder SkinModHelper looks in: beside the idle frames.</summary>
        public string ColorGradeDirectory => RootPath("idle") + "ColorGrading/";

        /// <summary>getCGLimit: the highest dashN in the folder, at least two; zero without the folder.</summary>
        public int ColorGradeLimit()
        {
            if (gameplayDirectory == null) return 0;
            string dir = Path.Combine(gameplayDirectory, ColorGradeDirectory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) return 0;
            int max = 2;
            foreach (string file in Directory.EnumerateFiles(dir, "dash*.png"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(file).Substring(4), out int i) && i > max) max = i;
            return max;
        }

        /// <summary>
        /// PlayerHairRenderHook_ColorGrade's choice of grade: dashN for her dash count -- the highest
        /// the folder has at or below it, from two down -- or flashN, or flash, while the hair flashes.
        /// </summary>
        public string ColorGradeFor(int? dashCount, bool flashing)
        {
            int maxNum = ColorGradeLimit();
            if (maxNum == 0) return null;
            string dir = ColorGradeDirectory;
            if (dashCount is int dashes)
            {
                dashes = Math.Max(0, Math.Min(maxNum, dashes));
                while (dashes > 2 && !atlas.Has(dir + "dash" + dashes)) dashes--;
                string path = dir + "dash" + dashes;
                if (flashing)
                {
                    if (atlas.Has(dir + "flash" + dashes)) path = dir + "flash" + dashes;
                    else if (atlas.Has(dir + "flash")) path = dir + "flash";
                }
                return path;
            }
            if (flashing && atlas.Has(dir + "flash")) return dir + "flash";
            return null;
        }
    }
}
