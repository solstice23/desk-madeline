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
        public bool PadInputEnabled = true;
        public bool InputWhenUnfocused;
        public bool AlwaysOnTop = true;
        public bool ParticlesEnabled = true;
        public bool FreezeFramesEnabled = true;
        public bool InfiniteStamina;
        public bool Invincible;
        public int DashMode = 1;
        public string Language;
        public string Skin = "default";
        public bool CatTailEnabled;
        public bool CatBangsEnabled;
        public bool CustomHairColorsEnabled;
        public int HairColor0 = 0x44B7FF;
        public int HairColor1 = 0xAC3232;
        public int HairColor2 = 0xFF6DEF;
        public int SpeedometerMode;
        public bool HitboxesEnabled;
        public bool DreamBlockMode;
        public bool IgnoreMaximizedWindows = true;
        public bool RespawnReversalEnabled = true;
        public int EdgeWrapMode;
        public bool ElytraEnabled;
        public int SfxMode = 1;          // 0 off, 1 only when focused, 2 always
        public int SfxVolume = 100;
        public int SurfaceSoundIndex = 8;
        /// <summary>Where Celeste is, once it has been found or chosen. Empty means look again.</summary>
        public string CelestePath = "";

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
                ReadBool(values, "PadInputEnabled", ref result.PadInputEnabled);
                ReadBool(values, "InputWhenUnfocused", ref result.InputWhenUnfocused);
                ReadBool(values, "AlwaysOnTop", ref result.AlwaysOnTop);
                ReadBool(values, "ParticlesEnabled", ref result.ParticlesEnabled);
                ReadBool(values, "FreezeFramesEnabled", ref result.FreezeFramesEnabled);
                ReadBool(values, "InfiniteStamina", ref result.InfiniteStamina);
                ReadBool(values, "Invincible", ref result.Invincible);
                if (values.TryGetValue("DashMode", out string dash) && int.TryParse(dash, out int dashValue))
                    result.DashMode = dashValue < 0 ? -1 : Math.Max(0, Math.Min(2, dashValue));
                if (values.TryGetValue("Language", out string language) && language.Length > 0)
                    result.Language = language.Trim();
                if (values.TryGetValue("Skin", out string skin) && skin.Length > 0) result.Skin = skin;
                ReadBool(values, "CatTailEnabled", ref result.CatTailEnabled);
                ReadBool(values, "CatBangsEnabled", ref result.CatBangsEnabled);
                ReadBool(values, "CustomHairColorsEnabled", ref result.CustomHairColorsEnabled);
                ReadColor(values, "HairColor0", ref result.HairColor0);
                ReadColor(values, "HairColor1", ref result.HairColor1);
                ReadColor(values, "HairColor2", ref result.HairColor2);
                if (values.TryGetValue("SpeedometerMode", out string speedometer) &&
                    int.TryParse(speedometer, out int speedometerValue))
                    result.SpeedometerMode = Math.Max(0, Math.Min(3, speedometerValue));
                ReadBool(values, "HitboxesEnabled", ref result.HitboxesEnabled);
                ReadBool(values, "DreamBlockMode", ref result.DreamBlockMode);
                ReadBool(values, "IgnoreMaximizedWindows", ref result.IgnoreMaximizedWindows);
                ReadBool(values, "RespawnReversalEnabled", ref result.RespawnReversalEnabled);
                if (values.TryGetValue("EdgeWrapMode", out string edgeWrap) &&
                    int.TryParse(edgeWrap, out int edgeWrapValue))
                    result.EdgeWrapMode = Math.Max(0, Math.Min(3, edgeWrapValue));
                ReadBool(values, "ElytraEnabled", ref result.ElytraEnabled);
                if (values.TryGetValue("SfxMode", out string sfxMode) &&
                    int.TryParse(sfxMode, out int sfxModeValue))
                    result.SfxMode = Math.Max(0, Math.Min(2, sfxModeValue));
                if (values.TryGetValue("SfxVolume", out string sfxVolume) &&
                    int.TryParse(sfxVolume, out int sfxVolumeValue))
                    result.SfxVolume = Math.Max(0, Math.Min(100, sfxVolumeValue));
                if (values.TryGetValue("SurfaceSoundIndex", out string surfaceSound) &&
                    int.TryParse(surfaceSound, out int surfaceSoundValue))
                    result.SurfaceSoundIndex = Math.Max(0, Math.Min(44, surfaceSoundValue));
                if (values.TryGetValue("CelestePath", out string celestePath))
                    result.CelestePath = celestePath.Trim();
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
                    "PadInputEnabled=" + PadInputEnabled,
                    "InputWhenUnfocused=" + InputWhenUnfocused,
                    "AlwaysOnTop=" + AlwaysOnTop,
                    "ParticlesEnabled=" + ParticlesEnabled,
                    "FreezeFramesEnabled=" + FreezeFramesEnabled,
                    "InfiniteStamina=" + InfiniteStamina,
                    "Invincible=" + Invincible,
                    "DashMode=" + DashMode,
                    "Language=" + (Language ?? ""),
                    "Skin=" + (Skin ?? "default"),
                    "CatTailEnabled=" + CatTailEnabled,
                    "CatBangsEnabled=" + CatBangsEnabled,
                    "CustomHairColorsEnabled=" + CustomHairColorsEnabled,
                    "HairColor0=#" + HairColor0.ToString("X6", CultureInfo.InvariantCulture),
                    "HairColor1=#" + HairColor1.ToString("X6", CultureInfo.InvariantCulture),
                    "HairColor2=#" + HairColor2.ToString("X6", CultureInfo.InvariantCulture),
                    "SpeedometerMode=" + SpeedometerMode,
                    "HitboxesEnabled=" + HitboxesEnabled,
                    "DreamBlockMode=" + DreamBlockMode,
                    "IgnoreMaximizedWindows=" + IgnoreMaximizedWindows,
                    "RespawnReversalEnabled=" + RespawnReversalEnabled,
                    "EdgeWrapMode=" + EdgeWrapMode,
                    "ElytraEnabled=" + ElytraEnabled,
                    "SfxMode=" + SfxMode,
                    "SfxVolume=" + SfxVolume,
                    "SurfaceSoundIndex=" + SurfaceSoundIndex,
                    "CelestePath=" + (CelestePath ?? "")
                });
            }
            catch { }
        }
    }
}
