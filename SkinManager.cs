using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DeskMadeline
{
    /// <summary>
    /// Discovers Skin Mod Helper and Skin Mod Helper Plus packages.  The path
    /// rules mirror SMH: Character_ID selects an element in Graphics/Sprites.xml,
    /// while legacy SkinId values map underscores to slashes.
    /// </summary>
    internal sealed class SkinDefinition
    {
        public string Id;
        public string DisplayName;
        public string PlayerDirectory;
        public string SpriteXml;
        public readonly Dictionary<int, Color> HairColors = new Dictionary<int, Color>();
    }

    internal sealed class SkinManager
    {
        public const string DefaultId = "default";
        public readonly List<SkinDefinition> Skins = new List<SkinDefinition>();
        public SkinDefinition Active { get; private set; }

        readonly string baseDirectory;

        public SkinManager(string baseDirectory)
        {
            this.baseDirectory = baseDirectory;
            Discover();
        }

        public void Discover()
        {
            Skins.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in CandidateRoots())
            {
                if (!Directory.Exists(root)) continue;
                foreach (string modDirectory in Directory.GetDirectories(root))
                {
                    string config = Path.Combine(modDirectory, "SkinModHelperConfig.yaml");
                    if (!File.Exists(config)) continue;
                    try
                    {
                        var skin = Parse(modDirectory, config);
                        if (skin != null && Directory.Exists(skin.PlayerDirectory) && seen.Add(skin.Id))
                            Skins.Add(skin);
                    }
                    catch (Exception ex)
                    {
                        PetWindow.Log("skin ignored " + modDirectory + ": " + ex.Message);
                    }
                }
            }
            Skins.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        }

        IEnumerable<string> CandidateRoots()
        {
            yield return Path.Combine(baseDirectory, "skins");
            yield return Path.Combine(baseDirectory, "example_skins");

            // Development builds live at bin/<configuration>/<tfm>.  This keeps
            // the checked-in examples usable without adding ~10 MB to every build.
            string projectExamples = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "example_skins"));
            yield return projectExamples;
        }

        public SkinDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Equals(DefaultId, StringComparison.OrdinalIgnoreCase)) return null;
            return Skins.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public void Activate(SkinDefinition skin) => Active = skin;

        public Color ResolveHairColor(int dashes, Color fallback)
        {
            return Active != null && Active.HairColors.TryGetValue(dashes, out Color color) ? color : fallback;
        }

        SkinDefinition Parse(string modDirectory, string configPath)
        {
            string[] lines = File.ReadAllLines(configPath);
            string legacyId = Value(lines.FirstOrDefault(l => Key(l) == "SkinId"));
            if (!string.IsNullOrEmpty(legacyId))
                return ParseLegacy(modDirectory, legacyId, lines);

            var blocks = ParseBlocks(lines);
            Dictionary<string, string> selected = blocks.FirstOrDefault(b =>
                b.TryGetValue("Player_List", out string value) && value.Equals("true", StringComparison.OrdinalIgnoreCase));
            if (selected == null || !selected.TryGetValue("Character_ID", out string characterId) || string.IsNullOrWhiteSpace(characterId))
                return null;

            string xml = Path.Combine(modDirectory, "Graphics", "Sprites.xml");
            XElement sprite = FindSprite(xml, characterId);
            if (sprite == null) return null;
            string atlasPath = NormalizePath((string)sprite.Attribute("path"));
            if (string.IsNullOrEmpty(atlasPath)) return null;

            string skinName = selected.TryGetValue("SkinName", out string configuredName) ? configuredName : characterId;
            return new SkinDefinition
            {
                Id = Path.GetFileName(modDirectory) + ":" + skinName,
                DisplayName = FriendlyName(skinName),
                PlayerDirectory = CombineAtlasPath(modDirectory, atlasPath),
                SpriteXml = xml
            };
        }

        SkinDefinition ParseLegacy(string modDirectory, string skinId, string[] lines)
        {
            string graphicsPath = skinId.Replace('_', Path.DirectorySeparatorChar);
            string xml = Path.Combine(modDirectory, "Graphics", graphicsPath, "Sprites.xml");
            XElement sprite = FindSprite(xml, "player");
            if (sprite == null) return null;
            string atlasPath = NormalizePath((string)sprite.Attribute("path"));
            var result = new SkinDefinition
            {
                Id = Path.GetFileName(modDirectory) + ":" + skinId,
                DisplayName = FriendlyName(skinId.Split('_').Last()),
                PlayerDirectory = CombineAtlasPath(modDirectory, atlasPath),
                SpriteXml = xml
            };

            int currentDashes = int.MinValue;
            foreach (string raw in lines)
            {
                string key = Key(raw);
                string value = Value(raw);
                if (key == "Dashes" && int.TryParse(value, out int dashes)) currentDashes = dashes;
                else if (key == "Color" && currentDashes != int.MinValue && TryColor(value, out Color color))
                {
                    result.HairColors[currentDashes] = color;
                    currentDashes = int.MinValue;
                }
            }
            return result;
        }

        static List<Dictionary<string, string>> ParseBlocks(string[] lines)
        {
            var result = new List<Dictionary<string, string>>();
            Dictionary<string, string> current = null;
            foreach (string raw in lines)
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith("- SkinName:", StringComparison.OrdinalIgnoreCase))
                {
                    current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    result.Add(current);
                }
                if (current == null || trimmed.Length == 0 || trimmed.StartsWith("#")) continue;
                string key = Key(trimmed);
                if (!string.IsNullOrEmpty(key)) current[key] = Value(trimmed);
            }
            return result;
        }

        static XElement FindSprite(string xmlPath, string elementName)
        {
            if (!File.Exists(xmlPath)) return null;
            XDocument document = XDocument.Load(xmlPath, LoadOptions.None);
            return document.Root?.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase));
        }

        static string CombineAtlasPath(string modDirectory, string atlasPath)
        {
            if (string.IsNullOrEmpty(atlasPath)) return null;
            string result = Path.Combine(modDirectory, "Graphics", "Atlases", "Gameplay");
            foreach (string part in atlasPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
                result = Path.Combine(result, part);
            return result;
        }

        static string NormalizePath(string value) => value?.Trim().Replace('\\', '/').Trim('/');

        static string Key(string line)
        {
            if (line == null) return null;
            string trimmed = line.Trim().TrimStart('-').TrimStart();
            int colon = trimmed.IndexOf(':');
            return colon > 0 ? trimmed.Substring(0, colon).Trim() : null;
        }

        static string Value(string line)
        {
            if (line == null) return null;
            int colon = line.IndexOf(':');
            if (colon < 0) return null;
            string value = line.Substring(colon + 1).Trim();
            int comment = value.IndexOf(" #", StringComparison.Ordinal);
            if (comment >= 0) value = value.Substring(0, comment).Trim();
            return value.Trim('"', '\'');
        }

        static string FriendlyName(string value) => (value ?? "Skin").Replace('_', ' ').Trim();

        static bool TryColor(string value, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string hex = value.Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int rgb)) return false;
            color = Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            return true;
        }
    }
}
