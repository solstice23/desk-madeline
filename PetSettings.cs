using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DeskMadeline
{
    /// <summary>Small human-readable persistent settings file stored beside the executable.</summary>
    internal sealed class PetSettings
    {
        public int Scale = 6;
        public bool InputEnabled = true;
        public bool AlwaysOnTop = true;
        public bool ParticlesEnabled;
        public bool FreezeFramesEnabled = true;
        public bool InfiniteStamina;
        public int DashMode = 1;
        public string Language;
        public string Skin = "default";
        public bool CatTailEnabled;
        public bool CatBangsEnabled;
        public bool CustomHairColorsEnabled;
        public int HairColor0 = 0x44B7FF;
        public int HairColor1 = 0xAC3232;
        public int HairColor2 = 0xFF6DEF;

        readonly string path;

        PetSettings(string path) { this.path = path; }

        public static PetSettings Load(string path)
        {
            var result = new PetSettings(path);
            try
            {
                if (!File.Exists(path)) return result;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int equals = line.IndexOf('=');
                    if (equals > 0) values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
                }
                if (values.TryGetValue("Scale", out string scale) && int.TryParse(scale, out int scaleValue))
                    result.Scale = Math.Max(2, Math.Min(8, scaleValue));
                ReadBool(values, "InputEnabled", ref result.InputEnabled);
                ReadBool(values, "AlwaysOnTop", ref result.AlwaysOnTop);
                ReadBool(values, "ParticlesEnabled", ref result.ParticlesEnabled);
                ReadBool(values, "FreezeFramesEnabled", ref result.FreezeFramesEnabled);
                ReadBool(values, "InfiniteStamina", ref result.InfiniteStamina);
                if (values.TryGetValue("DashMode", out string dash) && int.TryParse(dash, out int dashValue))
                    result.DashMode = dashValue < 0 ? -1 : Math.Max(0, Math.Min(2, dashValue));
                if (values.TryGetValue("Language", out string language) &&
                    (language == "en" || language == "zh")) result.Language = language;
                if (values.TryGetValue("Skin", out string skin) && skin.Length > 0) result.Skin = skin;
                ReadBool(values, "CatTailEnabled", ref result.CatTailEnabled);
                ReadBool(values, "CatBangsEnabled", ref result.CatBangsEnabled);
                ReadBool(values, "CustomHairColorsEnabled", ref result.CustomHairColorsEnabled);
                ReadColor(values, "HairColor0", ref result.HairColor0);
                ReadColor(values, "HairColor1", ref result.HairColor1);
                ReadColor(values, "HairColor2", ref result.HairColor2);
            }
            catch { }
            return result;
        }

        static void ReadBool(Dictionary<string, string> values, string key, ref bool target)
        {
            if (values.TryGetValue(key, out string text) && bool.TryParse(text, out bool value)) target = value;
        }

        static void ReadColor(Dictionary<string, string> values, string key, ref int target)
        {
            if (!values.TryGetValue(key, out string text)) return;
            text = text.Trim().TrimStart('#');
            if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out int value)) target = value;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(path, new[]
                {
                    "# DeskMadeline settings",
                    "Scale=" + Scale,
                    "InputEnabled=" + InputEnabled,
                    "AlwaysOnTop=" + AlwaysOnTop,
                    "ParticlesEnabled=" + ParticlesEnabled,
                    "FreezeFramesEnabled=" + FreezeFramesEnabled,
                    "InfiniteStamina=" + InfiniteStamina,
                    "DashMode=" + DashMode,
                    "Language=" + (Language ?? ""),
                    "Skin=" + (Skin ?? "default"),
                    "CatTailEnabled=" + CatTailEnabled,
                    "CatBangsEnabled=" + CatBangsEnabled,
                    "CustomHairColorsEnabled=" + CustomHairColorsEnabled,
                    "HairColor0=#" + HairColor0.ToString("X6", CultureInfo.InvariantCulture),
                    "HairColor1=#" + HairColor1.ToString("X6", CultureInfo.InvariantCulture),
                    "HairColor2=#" + HairColor2.ToString("X6", CultureInfo.InvariantCulture)
                });
            }
            catch { }
        }
    }
}
