using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Xml.Linq;

namespace DeskMadeline
{
    /// <summary>
    /// Monocle's <c>Chooser&lt;string&gt;</c>: a weighted random pick, as an animation's goto is.
    /// </summary>
    public sealed class Chooser
    {
        public readonly List<(string Value, float Weight)> Choices = new List<(string, float)>();
        public float TotalWeight { get; private set; }

        public Chooser() { }
        public Chooser(string first, float weight) { Add(first, weight); }

        public Chooser Add(string choice, float weight)
        {
            weight = Math.Max(weight, 0f);
            Choices.Add((choice, weight));
            TotalWeight += weight;
            return this;
        }

        /// <summary>Chooser.Choose, drawing from Calc.Random.</summary>
        public string Choose(Random random)
        {
            if (TotalWeight <= 0f) return null;
            if (Choices.Count == 1) return Choices[0].Value;
            double roll = random.NextDouble() * TotalWeight;
            float sum = 0f;
            for (int i = 0; i < Choices.Count - 1; i++)
            {
                sum += Choices[i].Weight;
                if (roll < sum) return Choices[i].Value;
            }
            return Choices[Choices.Count - 1].Value;
        }

        /// <summary>Chooser.FromString: "choice" alone, or "choice0:weight,choice1:weight,...".</summary>
        public static Chooser FromString(string data)
        {
            var chooser = new Chooser();
            string[] parts = data.Split(',');
            if (parts.Length == 1 && parts[0].IndexOf(':') == -1)
            {
                chooser.Add(parts[0], 1f);
                return chooser;
            }
            foreach (string part in parts)
            {
                if (part.IndexOf(':') == -1) { chooser.Add(part, 1f); continue; }
                string[] pair = part.Split(':');
                chooser.Add(pair[0].Trim(), Convert.ToSingle(pair[1].Trim(), CultureInfo.InvariantCulture));
            }
            return chooser;
        }
    }

    /// <summary>The pieces of Monocle's Calc that sprites use.</summary>
    public static class MonoCalc
    {
        /// <summary>Calc.ReadCSVIntWithTricks: "0-4,5*10,6-14", ranges either way round.</summary>
        public static int[] ReadCSVIntWithTricks(string csv)
        {
            if (csv == "") return new int[0];
            var list = new List<int>();
            foreach (string text in csv.Split(','))
            {
                if (text.IndexOf('-') != -1)
                {
                    string[] range = text.Split('-');
                    int from = Convert.ToInt32(range[0]), to = Convert.ToInt32(range[1]);
                    for (int i = from; i != to; i += Math.Sign(to - from)) list.Add(i);
                    list.Add(to);
                }
                else if (text.IndexOf('*') != -1)
                {
                    string[] times = text.Split('*');
                    int item = Convert.ToInt32(times[0]), count = Convert.ToInt32(times[1]);
                    for (int i = 0; i < count; i++) list.Add(item);
                }
                else list.Add(Convert.ToInt32(text));
            }
            return list.ToArray();
        }

        /// <summary>Calc.Chance: NextFloat() &lt; chance.</summary>
        public static bool Chance(this Random random, float chance) => (float)random.NextDouble() < chance;

        /// <summary>Calc.NextFloat(max).</summary>
        public static float NextFloat(this Random random, float max) => (float)random.NextDouble() * max;
    }

    /// <summary>
    /// Where a sprite's frames come from: Monocle's Atlas, answering by atlas path
    /// ("characters/player/idle00").
    /// </summary>
    public interface IFrameAtlas
    {
        bool Has(string path);
    }

    public static class AtlasLookup
    {
        /// <summary>
        /// Atlas.GetAtlasSubtextureFromAtlasAt: index 0 may be the bare key; any index is the key
        /// followed by the number, zero-padded to anything up to six more digits.
        /// </summary>
        public static string SubtextureAt(IFrameAtlas atlas, string key, int index)
        {
            if (index == 0 && atlas.Has(key)) return key;
            string text = index.ToString(CultureInfo.InvariantCulture);
            int length = text.Length;
            while (text.Length < length + 6)
            {
                if (atlas.Has(key + text)) return key + text;
                text = "0" + text;
            }
            return null;
        }

        /// <summary>Atlas.orig_GetAtlasSubtextures: index 0, 1, 2... until the first gap.</summary>
        public static List<string> Subtextures(IFrameAtlas atlas, string key)
        {
            var list = new List<string>();
            for (int i = 0; ; i++)
            {
                string found = SubtextureAt(atlas, key, i);
                if (found == null) break;
                list.Add(found);
            }
            return list;
        }

        public static bool HasSubtextures(IFrameAtlas atlas, string key) => SubtextureAt(atlas, key, 0) != null;
    }

    /// <summary>Sprite.Animation: frames are atlas paths.</summary>
    public sealed class SpriteAnimation
    {
        public float Delay;
        public string[] Frames;
        public Chooser Goto;
    }

    /// <summary>
    /// Monocle's Sprite, as far as animation goes: which frame is showing, and how Play, goto and
    /// the frame callbacks advance it. Frames are atlas paths rather than textures.
    /// </summary>
    public class GameSprite
    {
        public float Rate = 1f;
        public Action<string> OnFinish;
        public Action<string> OnLoop;
        public Action<string> OnFrameChange;
        public Action<string> OnLastFrame;
        public Action<string, string> OnChange;

        /// <summary>Image.Origin, and Sprite.Justify, which re-derives it from each frame's size.</summary>
        public PointF Origin;
        public PointF? Justify;

        /// <summary>The origin to draw a frame of this size around: Justify's when there is one.</summary>
        public PointF OriginFor(int width, int height)
            => Justify is PointF j ? new PointF(width * j.X, height * j.Y) : Origin;

        /// <summary>Calc.Random, which the goto choosers and randomised frames draw from.</summary>
        public Random Random = new Random();

        public Dictionary<string, SpriteAnimation> Animations { get; private set; } =
            new Dictionary<string, SpriteAnimation>(StringComparer.OrdinalIgnoreCase);

        SpriteAnimation currentAnimation;
        float animationTimer;

        /// <summary>The frame showing: Image.Texture, as its atlas path.</summary>
        public string Texture { get; private set; }
        public bool Animating { get; private set; }
        public string CurrentAnimationID { get; set; } = "";
        public string LastAnimationID { get; set; }
        public int CurrentAnimationFrame { get; private set; }
        public int CurrentAnimationTotalFrames => currentAnimation?.Frames.Length ?? 0;

        public bool Has(string id) => id != null && Animations.ContainsKey(id);

        public string GetFrame(string animation, int frame) => Animations[animation].Frames[frame];

        public void ClearAnimations() => Animations.Clear();

        /// <summary>Sprite.Update, one call per game frame.</summary>
        public void Update(float deltaTime)
        {
            if (!Animating) return;
            animationTimer += deltaTime * Rate;
            if (!(Math.Abs(animationTimer) >= currentAnimation.Delay)) return;
            CurrentAnimationFrame += Math.Sign(animationTimer);
            animationTimer -= Math.Sign(animationTimer) * currentAnimation.Delay;
            if (CurrentAnimationFrame < 0 || CurrentAnimationFrame >= currentAnimation.Frames.Length)
            {
                string before = CurrentAnimationID;
                OnLastFrame?.Invoke(CurrentAnimationID);
                if (before != CurrentAnimationID) return;
                if (currentAnimation.Goto != null)
                {
                    CurrentAnimationID = currentAnimation.Goto.Choose(Random);
                    OnChange?.Invoke(LastAnimationID, CurrentAnimationID);
                    LastAnimationID = CurrentAnimationID;
                    currentAnimation = Animations[LastAnimationID];
                    CurrentAnimationFrame = CurrentAnimationFrame < 0 ? currentAnimation.Frames.Length - 1 : 0;
                    SetFrame(currentAnimation.Frames[CurrentAnimationFrame]);
                    OnLoop?.Invoke(CurrentAnimationID);
                }
                else
                {
                    CurrentAnimationFrame = CurrentAnimationFrame < 0 ? 0 : currentAnimation.Frames.Length - 1;
                    Animating = false;
                    string finished = CurrentAnimationID;
                    CurrentAnimationID = "";
                    currentAnimation = null;
                    animationTimer = 0f;
                    OnFinish?.Invoke(finished);
                }
            }
            else SetFrame(currentAnimation.Frames[CurrentAnimationFrame]);
        }

        void SetFrame(string texture)
        {
            if (texture == Texture) return;
            Texture = texture;
            OnFrameChange?.Invoke(CurrentAnimationID);
        }

        public void SetAnimationFrame(int frame)
        {
            animationTimer = 0f;
            CurrentAnimationFrame = frame % currentAnimation.Frames.Length;
            SetFrame(currentAnimation.Frames[CurrentAnimationFrame]);
        }

        /// <summary>Sprite.Play. Asking for an animation the sprite does not have throws, as it does.</summary>
        public virtual void Play(string id, bool restart = false, bool randomizeFrame = false)
        {
            if (CurrentAnimationID == id && !restart) return;
            OnChange?.Invoke(LastAnimationID, id);
            LastAnimationID = CurrentAnimationID = id;
            currentAnimation = Animations[id];
            Animating = currentAnimation.Delay > 0f;
            if (randomizeFrame)
            {
                animationTimer = Random.NextFloat(currentAnimation.Delay);
                CurrentAnimationFrame = Random.Next(currentAnimation.Frames.Length);
            }
            else
            {
                animationTimer = 0f;
                CurrentAnimationFrame = 0;
            }
            SetFrame(currentAnimation.Frames[CurrentAnimationFrame]);
        }

        /// <summary>
        /// Sprite.CloneInto, as SpriteBank.CreateOn uses it: the template's animations and the
        /// state it was left in -- every bank entry has already played its start animation --
        /// onto this sprite, whose callbacks stay its own.
        /// </summary>
        public void LoadFrom(GameSprite template)
        {
            Texture = template.Texture;
            Origin = template.Origin;
            Justify = template.Justify;
            Animations = new Dictionary<string, SpriteAnimation>(template.Animations, StringComparer.OrdinalIgnoreCase);
            currentAnimation = template.currentAnimation;
            animationTimer = template.animationTimer;
            Animating = template.Animating;
            CurrentAnimationID = template.CurrentAnimationID;
            LastAnimationID = template.LastAnimationID;
            CurrentAnimationFrame = template.CurrentAnimationFrame;
        }
    }

    /// <summary>One XML element a SpriteData was built from: SpriteDataSource.</summary>
    public sealed class SpriteDataSource
    {
        public XElement Xml;
        public string Path;
        public string OverridePath;
    }

    /// <summary>
    /// One entry of a sprite bank, built the way Monocle's SpriteData.Add builds it.
    /// </summary>
    public sealed class SpriteData
    {
        public readonly string Name;
        public readonly List<SpriteDataSource> Sources = new List<SpriteDataSource>();
        public readonly GameSprite Sprite = new GameSprite();
        readonly IFrameAtlas atlas;

        public SpriteData(string name, IFrameAtlas atlas) { Name = name; this.atlas = atlas; }

        /// <summary>
        /// SpriteData.Add: every Anim and Loop of the element. With an override path -- an entry
        /// that copies another -- an animation takes its frames from the override path when every
        /// one of them is there, and from the copied entry's own path otherwise.
        /// </summary>
        public void Add(XElement xml, string overridePath = null)
        {
            var source = new SpriteDataSource { Xml = xml, Path = (string)xml.Attribute("path"), OverridePath = overridePath };
            string prefix = "Sprite '" + xml.Name.LocalName + "': ";
            if (xml.Attribute("path") == null && string.IsNullOrEmpty(overridePath))
                throw new Exception(prefix + "'path' is missing!");
            var ids = new HashSet<string>();
            foreach (XElement e in xml.Elements("Anim")) CheckAnim(e, prefix, ids);
            foreach (XElement e in xml.Elements("Loop")) CheckAnim(e, prefix, ids);
            string start = (string)xml.Attribute("start");
            if (start != null && !ids.Contains(start))
                throw new Exception(prefix + "starting animation '" + start + "' is missing!");

            string path = (string)xml.Attribute("path") ?? "";
            float defaultDelay = AttrFloat(xml, "delay", 0f);
            foreach (XElement anim in xml.Elements("Anim"))
            {
                Chooser into = anim.Attribute("goto") == null ? null : Chooser.FromString((string)anim.Attribute("goto"));
                string animPath = (string)anim.Attribute("path") ?? "";
                int[] frames = MonoCalc.ReadCSVIntWithTricks((string)anim.Attribute("frames") ?? "");
                animPath = string.IsNullOrEmpty(overridePath) || !HasFrames(overridePath + animPath, frames)
                    ? path + animPath : overridePath + animPath;
                AddAnimation((string)anim.Attribute("id"), animPath, AttrFloat(anim, "delay", defaultDelay), into, frames);
            }
            foreach (XElement loop in xml.Elements("Loop"))
            {
                string id = (string)loop.Attribute("id");
                string loopPath = (string)loop.Attribute("path") ?? "";
                int[] frames = MonoCalc.ReadCSVIntWithTricks((string)loop.Attribute("frames") ?? "");
                loopPath = string.IsNullOrEmpty(overridePath) || !HasFrames(overridePath + loopPath, frames)
                    ? path + loopPath : overridePath + loopPath;
                AddAnimation(id, loopPath, AttrFloat(loop, "delay", defaultDelay), new Chooser(id, 1f), frames);
            }
            // SpriteData.Add's Center, Justify and Origin children. Center is CenterOrigin with a
            // Justify of a half each way, which every frame change then re-derives from.
            XElement origin = xml.Element("Origin"), justify = xml.Element("Justify");
            if (xml.Element("Center") != null) Sprite.Justify = new PointF(0.5f, 0.5f);
            else if (justify != null) Sprite.Justify = Position(justify);
            else if (origin != null) Sprite.Origin = Position(origin);
            if (start != null) Sprite.Play(start);
            Sources.Add(source);
        }

        static void CheckAnim(XElement e, string prefix, HashSet<string> ids)
        {
            string id = (string)e.Attribute("id");
            if (id == null) throw new Exception(prefix + "'id' is missing on " + e.Name.LocalName + "!");
            if (!ids.Add(id)) throw new Exception(prefix + "multiple animations with id '" + id + "'!");
        }

        /// <summary>SpriteData.orig_HasFrames.</summary>
        bool HasFrames(string path, int[] frames)
        {
            if (frames == null || frames.Length == 0) return AtlasLookup.SubtextureAt(atlas, path, 0) != null;
            foreach (int frame in frames)
                if (AtlasLookup.SubtextureAt(atlas, path, frame) == null) return false;
            return true;
        }

        /// <summary>Sprite.Add / AddLoop, through Sprite.GetFrames.</summary>
        void AddAnimation(string id, string framePath, float delay, Chooser into, int[] frames)
        {
            string[] textures;
            if (frames.Length == 0) textures = AtlasLookup.Subtextures(atlas, framePath).ToArray();
            else
            {
                textures = new string[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                    textures[i] = AtlasLookup.SubtextureAt(atlas, framePath, frames[i])
                        ?? throw new Exception("Can't find sprite " + framePath + " with index " + frames[i]);
            }
            // GetFrames reads the first frame's size straight away, so an empty set throws there.
            if (textures.Length == 0) throw new Exception("Can't find sprite " + framePath);
            Sprite.Animations[id] = new SpriteAnimation { Delay = delay, Frames = textures, Goto = into };
        }

        static float AttrFloat(XElement xml, string name, float fallback)
        {
            string value = (string)xml.Attribute(name);
            return value == null ? fallback : Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        /// <summary>Calc.Position: an element's x and y attributes.</summary>
        static PointF Position(XElement xml) => new PointF(AttrFloat(xml, "x", 0f), AttrFloat(xml, "y", 0f));
    }

    /// <summary>
    /// Monocle's SpriteBank over Everest's merged Sprites.xml: a mod's element replaces the
    /// game's of the same name and anything new is added; then each element becomes a
    /// SpriteData, copying the one it names in copy="..." first.
    /// </summary>
    public sealed class SpriteBank
    {
        public readonly Dictionary<string, SpriteData> SpriteData =
            new Dictionary<string, SpriteData>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Entries that failed to build, and why.</summary>
        public readonly List<string> Failures = new List<string>();

        public bool Has(string id) => id != null && SpriteData.ContainsKey(id);

        public static SpriteBank Load(IFrameAtlas atlas, XDocument game, IEnumerable<XDocument> mods)
        {
            var root = new XElement(game.Root);
            foreach (XDocument mod in mods)
                foreach (XElement element in mod.Root.Elements())
                {
                    XElement existing = root.Element(element.Name);
                    if (existing != null) existing.ReplaceWith(new XElement(element));
                    else root.Add(new XElement(element));
                }

            var bank = new SpriteBank();
            var seen = new Dictionary<string, XElement>();
            foreach (XElement element in root.Elements())
            {
                string name = element.Name.LocalName;
                seen[name] = element;
                var data = new SpriteData(name, atlas);
                try
                {
                    string copy = (string)element.Attribute("copy");
                    if (copy != null) data.Add(seen[copy], (string)element.Attribute("path"));
                    data.Add(element);
                    bank.SpriteData[name] = data;
                }
                catch (Exception ex)
                {
                    bank.Failures.Add(name + ": " + ex.Message);
                }
            }
            return bank;
        }

        /// <summary>SpriteBank.CreateOn.</summary>
        public void CreateOn(GameSprite sprite, string id)
        {
            if (!SpriteData.TryGetValue(id, out SpriteData data))
                throw new Exception("Missing animation name in SpriteData: '" + id + "'!");
            sprite.LoadFrom(data.Sprite);
        }
    }
}
