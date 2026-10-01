using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace DeskMadeline
{
    /// <summary>
    /// PlayerSprite.FrameMetadata: per texture, whether hair is drawn over it, where it hangs from,
    /// which bangs it wears, and how far a held object rides -- built the way
    /// PlayerSprite.CreateFramesMetadata builds it, from each player sprite's &lt;Metadata&gt;.
    /// </summary>
    /// <remarks>
    /// Keyed by the texture's atlas path, as the game keys it, so a skin's frames answer from the
    /// skin's own metadata and Madeline's from the game's, and the two can never be mixed. A
    /// texture with no entry wears no hair, hangs it from (0,0), shows bangs 0 and carries at 0
    /// -- PlayerSprite's getters, each falling back the same way.
    /// </remarks>
    public static class HairMeta
    {
        public struct Meta
        {
            public PointF Offset;
            public int Bangs;
            public bool HasHair;
            public int CarryYOffset;
        }

        /// <summary>PlayerHair.bangs: GFX.Game.GetAtlasSubtextures("characters/player/bangs").</summary>
        public static readonly string[] BangsFrames = { "bangs00", "bangs01", "bangs02" };

        static readonly Dictionary<string, Meta> FrameMetadata =
            new Dictionary<string, Meta>(StringComparer.OrdinalIgnoreCase);

        public static int Count => FrameMetadata.Count;

        /// <summary>PlayerSprite.ClearFramesMetadata.</summary>
        public static void Clear() => FrameMetadata.Clear();

        /// <summary>
        /// PlayerSprite.CreateFramesMetadata: every &lt;Frames&gt; of every source the entry was
        /// built from, under the source's override path when it was copied, its own otherwise.
        /// </summary>
        public static void CreateFramesMetadata(SpriteBank bank, string sprite, IFrameAtlas atlas)
        {
            if (bank == null || !bank.SpriteData.TryGetValue(sprite, out SpriteData data)) return;
            foreach (SpriteDataSource source in data.Sources)
            {
                XElement metadata = source.Xml.Element("Metadata");
                string path = source.Path;
                if (metadata == null) continue;
                if (!string.IsNullOrEmpty(source.OverridePath)) path = source.OverridePath;
                foreach (XElement frames in metadata.Descendants("Frames"))
                    AddFrames(path + ((string)frames.Attribute("path") ?? ""),
                        (string)frames.Attribute("hair") ?? "", (string)frames.Attribute("carry") ?? "",
                        atlas, overwrite: true);
            }
        }

        /// <summary>
        /// One &lt;Frames&gt;: frame i is the path followed by i in two digits, or, for a sheet of
        /// one frame, the bare path. hair is "x,y" with an optional ":bangs", "x" or nothing for
        /// none; carry is a plain number.
        /// </summary>
        static void AddFrames(string path, string hair, string carry, IFrameAtlas atlas, bool overwrite)
        {
            string[] hairs = hair.Split('|');
            string[] carries = carry.Split(',');
            for (int i = 0; i < Math.Max(hairs.Length, carries.Length); i++)
            {
                string key = path + (i < 10 ? "0" : "") + i;
                if (i == 0 && !atlas.Has(key)) key = path;
                if (!overwrite && FrameMetadata.ContainsKey(key)) continue;
                var meta = new Meta();
                if (i < hairs.Length)
                {
                    if (hairs[i].Equals("x", StringComparison.OrdinalIgnoreCase) || hairs[i].Length <= 0)
                        meta.HasHair = false;
                    else
                    {
                        string[] withFrame = hairs[i].Split(':');
                        string[] offset = withFrame[0].Split(',');
                        meta.HasHair = true;
                        meta.Offset = new PointF(Convert.ToInt32(offset[0], CultureInfo.InvariantCulture),
                            Convert.ToInt32(offset[1], CultureInfo.InvariantCulture));
                        meta.Bangs = withFrame.Length >= 2 ? Convert.ToInt32(withFrame[1], CultureInfo.InvariantCulture) : 0;
                    }
                }
                if (i < carries.Length && carries[i].Length > 0)
                    meta.CarryYOffset = int.Parse(carries[i], CultureInfo.InvariantCulture);
                FrameMetadata[key] = meta;
            }
        }

        /// <summary>
        /// CommunalHelper's CustomPlayerFrameMetadata.xml, which its hook on CreateFramesMetadata
        /// adds after the game's: the elytra sheet under the sprite's own path when it has one,
        /// player_no_backpack's otherwise, and only where no entry is there already.
        /// </summary>
        public static void AddCommunalHelperMetadata(SpriteBank bank, string sprite, IFrameAtlas atlas)
        {
            if (bank == null || !bank.SpriteData.TryGetValue(sprite, out SpriteData data) || data.Sources.Count == 0) return;
            SpriteDataSource first = data.Sources[0];
            string path = !string.IsNullOrEmpty(first.OverridePath) ? first.OverridePath : first.Path;
            if (!AtlasLookup.HasSubtextures(atlas, path + "CommunalHelper/fly"))
                path = "characters/player_no_backpack/";
            AddFrames(path + "CommunalHelper/fly", "4,0|4,0|4,-1|4,-1|3,-1|3,-1|2,-1|2,-1|2,-1", "", atlas, overwrite: false);
        }

        /// <summary>
        /// GFX.LoadData's metadata pass over the player sprites, each through CommunalHelper's two
        /// hooks on CreateFramesMetadata: its elytra animation before, its elytra metadata after.
        /// </summary>
        public static void LoadPlayerSprites(SpriteBank bank, IFrameAtlas atlas)
        {
            Clear();
            foreach (string id in new[] { "player", "player_no_backpack", "badeline", "player_badeline", "player_playback" })
            {
                AddCommunalHelperElytra(bank, id, atlas);
                CreateFramesMetadata(bank, id, atlas);
                AddCommunalHelperMetadata(bank, id, atlas);
            }
        }

        /// <summary>
        /// CommunalHelper's Elytra hook on CreateFramesMetadata: a player sprite with no
        /// anim_player_elytra_fly of its own is given one, from its own CommunalHelper/fly frames
        /// or player_no_backpack's, at a delay of ten seconds -- the state sets its frame.
        /// </summary>
        public static void AddCommunalHelperElytra(SpriteBank bank, string id, IFrameAtlas atlas)
        {
            if (bank == null || !bank.SpriteData.TryGetValue(id, out SpriteData data)) return;
            if (data.Sprite.Has(Player.ElytraAnimation) || data.Sources.Count == 0) return;
            SpriteDataSource first = data.Sources[0];
            string path = !string.IsNullOrEmpty(first.OverridePath) ? first.OverridePath : first.Path;
            if (!AtlasLookup.HasSubtextures(atlas, path + "CommunalHelper/fly"))
                path = "characters/player_no_backpack/";
            var frames = AtlasLookup.Subtextures(atlas, path + "CommunalHelper/fly");
            if (frames.Count > 0)
                data.Sprite.Animations[Player.ElytraAnimation] = new SpriteAnimation { Frames = frames.ToArray(), Delay = 10f };
        }

        public static bool TryGet(string texture, out Meta meta)
        {
            if (texture != null && FrameMetadata.TryGetValue(texture, out meta)) return true;
            meta = default;
            return false;
        }

        /// <summary>PlayerSprite.HasHair.</summary>
        public static bool HasHair(string texture) => TryGet(texture, out Meta meta) && meta.HasHair;

        /// <summary>PlayerSprite.CarryYOffset, before the sprite's Y scale.</summary>
        public static int CarryYOffset(string texture) => TryGet(texture, out Meta meta) ? meta.CarryYOffset : 0;
    }
}
