using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
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
        public readonly Dictionary<string, int[]> CarryOffsets =
            new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
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
                    if (Path.GetFileName(modDirectory).StartsWith(".")) continue;
                    RegisterPackages(modDirectory, Path.GetFileName(modDirectory), seen);
                }
                foreach (string archive in Directory.GetFiles(root, "*.zip"))
                {
                    try
                    {
                        string extracted = ExtractArchive(root, archive);
                        RegisterPackages(extracted, "zip-" + Path.GetFileNameWithoutExtension(archive), seen);
                    }
                    catch (Exception ex)
                    {
                        PetWindow.Log("skin zip ignored " + archive + ": " + ex.Message);
                    }
                }
            }
            Skins.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
        }

        void RegisterPackages(string container, string packageKey, HashSet<string> seen)
        {
            foreach (string modDirectory in FindPackageRoots(container))
            {
                try
                {
                    string config = Path.Combine(modDirectory, "SkinModHelperConfig.yaml");
                    SkinDefinition skin = File.Exists(config)
                        ? Parse(modDirectory, config, packageKey)
                        : ParseDirectReplacement(modDirectory, packageKey);
                    if (skin != null && Directory.Exists(skin.PlayerDirectory) && seen.Add(skin.Id))
                        Skins.Add(skin);
                }
                catch (Exception ex)
                {
                    PetWindow.Log("skin ignored " + modDirectory + ": " + ex.Message);
                }
            }
        }

        static IEnumerable<string> FindPackageRoots(string container)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string ownConfig = Path.Combine(container, "SkinModHelperConfig.yaml");
            string ownPlayer = Path.Combine(container, "Graphics", "Atlases", "Gameplay", "characters", "player");
            if (File.Exists(ownConfig) || IsPlayerDirectory(ownPlayer)) roots.Add(container);

            foreach (string config in Directory.GetFiles(container, "SkinModHelperConfig.yaml", SearchOption.AllDirectories))
                roots.Add(Path.GetDirectoryName(config));
            foreach (string player in Directory.GetDirectories(container, "player", SearchOption.AllDirectories))
            {
                string normalized = player.Replace('\\', '/');
                if (!normalized.EndsWith("/Graphics/Atlases/Gameplay/characters/player", StringComparison.OrdinalIgnoreCase)) continue;
                string modRoot = Path.GetFullPath(Path.Combine(player, "..", "..", "..", "..", ".."));
                if (IsPlayerDirectory(player)) roots.Add(modRoot);
            }
            return roots;
        }

        static bool IsPlayerDirectory(string directory)
            => Directory.Exists(directory) && File.Exists(Path.Combine(directory, "idle00.png"));

        static string ExtractArchive(string root, string archivePath)
        {
            var info = new FileInfo(archivePath);
            string safeName = new string(Path.GetFileNameWithoutExtension(archivePath)
                .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
            string cache = Path.Combine(root, ".desk-madeline-cache",
                safeName + "_" + info.Length + "_" + info.LastWriteTimeUtc.Ticks);
            string marker = Path.Combine(cache, ".complete");
            if (File.Exists(marker)) return cache;

            Directory.CreateDirectory(cache);
            string cachePrefix = Path.GetFullPath(cache).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            long extractedBytes = 0;
            int extractedFiles = 0;
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/').TrimStart('/');
                    bool needed = name.Equals("SkinModHelperConfig.yaml", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("/SkinModHelperConfig.yaml", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("everest.yaml", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("/everest.yaml", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Graphics/", StringComparison.OrdinalIgnoreCase) ||
                        name.IndexOf("/Graphics/", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!needed || name.EndsWith("/")) continue;
                    if (++extractedFiles > 12000) throw new InvalidDataException("archive contains too many skin files");
                    extractedBytes += entry.Length;
                    if (entry.Length > 64L * 1024 * 1024 || extractedBytes > 256L * 1024 * 1024)
                        throw new InvalidDataException("archive skin data is too large");

                    string destination = Path.GetFullPath(Path.Combine(cache, name.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("archive contains an unsafe path");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, true);
                }
            }
            File.WriteAllText(marker, info.Name);
            return cache;
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

        public bool TryGetCarryOffsets(string animation, out int[] offsets)
        {
            offsets = null;
            return Active != null && Active.CarryOffsets.TryGetValue(animation, out offsets);
        }

        SkinDefinition Parse(string modDirectory, string configPath, string packageKey)
        {
            string[] lines = File.ReadAllLines(configPath);
            string legacyId = Value(lines.FirstOrDefault(l => Key(l) == "SkinId"));
            if (!string.IsNullOrEmpty(legacyId))
                return ParseLegacy(modDirectory, legacyId, lines, packageKey);

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
            var result = new SkinDefinition
            {
                Id = packageKey + ":" + skinName,
                DisplayName = FriendlyName(skinName),
                PlayerDirectory = CombineAtlasPath(modDirectory, atlasPath),
                SpriteXml = xml
            };
            LoadHairColors(Path.Combine(result.PlayerDirectory, "skinConfig", "HairConfig.yaml"), result);
            LoadCarryOffsets(sprite, result);
            return result;
        }

        SkinDefinition ParseLegacy(string modDirectory, string skinId, string[] lines, string packageKey)
        {
            string graphicsPath = skinId.Replace('_', Path.DirectorySeparatorChar);
            string xml = Path.Combine(modDirectory, "Graphics", graphicsPath, "Sprites.xml");
            XElement sprite = FindSprite(xml, "player");
            if (sprite == null) return null;
            string atlasPath = NormalizePath((string)sprite.Attribute("path"));
            var result = new SkinDefinition
            {
                Id = packageKey + ":" + skinId,
                DisplayName = FriendlyName(skinId.Split('_').Last()),
                PlayerDirectory = CombineAtlasPath(modDirectory, atlasPath),
                SpriteXml = xml
            };

            LoadHairColors(lines, result);
            LoadCarryOffsets(sprite, result);
            return result;
        }

        static void LoadCarryOffsets(XElement sprite, SkinDefinition skin)
        {
            XElement metadata = sprite.Elements().FirstOrDefault(e =>
                e.Name.LocalName.Equals("Metadata", StringComparison.OrdinalIgnoreCase));
            if (metadata == null) return;
            foreach (XElement frames in metadata.Descendants().Where(e =>
                e.Name.LocalName.Equals("Frames", StringComparison.OrdinalIgnoreCase)))
            {
                string path = (string)frames.Attribute("path");
                string carry = (string)frames.Attribute("carry");
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(carry)) continue;
                var values = new List<int>();
                foreach (string value in carry.Split(','))
                    if (int.TryParse(value.Trim(), out int parsed)) values.Add(parsed);
                if (values.Count > 0) skin.CarryOffsets[path.Trim()] = values.ToArray();
            }
        }

        static SkinDefinition ParseDirectReplacement(string modDirectory, string packageKey)
        {
            string playerDirectory = Path.Combine(modDirectory, "Graphics", "Atlases", "Gameplay", "characters", "player");
            if (!IsPlayerDirectory(playerDirectory)) return null;
            string displayName = Path.GetFileName(modDirectory);
            string manifest = Path.Combine(modDirectory, "everest.yaml");
            if (File.Exists(manifest))
            {
                string nameLine = File.ReadLines(manifest).FirstOrDefault(l => Key(l) == "Name");
                string configured = Value(nameLine);
                if (!string.IsNullOrWhiteSpace(configured)) displayName = configured;
            }
            var result = new SkinDefinition
            {
                Id = packageKey + ":direct",
                DisplayName = FriendlyName(displayName),
                PlayerDirectory = playerDirectory
            };
            LoadHairColors(Path.Combine(playerDirectory, "skinConfig", "HairConfig.yaml"), result);
            // Mikuline predates per-skin HairConfig and relies on a separately configured
            // LiquidMod. DeskMadeline has no global Everest mod settings to import, so
            // use the palette authored into Mikuline's sprites as this one compatibility
            // exception. Match the manifest package name, never the ZIP filename.
            if (result.HairColors.Count == 0 && displayName.Equals("Mikuline", StringComparison.OrdinalIgnoreCase))
            {
                Color mikuGreen = Color.FromArgb(0x00, 0xD8, 0xC1);
                result.HairColors[0] = mikuGreen;
                result.HairColors[1] = mikuGreen;
                result.HairColors[2] = mikuGreen;
            }
            return result;
        }

        static void LoadHairColors(string path, SkinDefinition skin)
        {
            if (File.Exists(path)) LoadHairColors(File.ReadAllLines(path), skin);
        }

        static void LoadHairColors(IEnumerable<string> lines, SkinDefinition skin)
        {
            int currentDashes = int.MinValue;
            foreach (string raw in lines)
            {
                string key = Key(raw);
                string value = Value(raw);
                if (key == "Dashes" && int.TryParse(value, out int dashes)) currentDashes = dashes;
                else if (key == "Color" && currentDashes != int.MinValue && TryColor(value, out Color color))
                {
                    skin.HairColors[currentDashes] = color;
                    currentDashes = int.MinValue;
                }
            }
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
