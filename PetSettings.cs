using System;
using System.Collections.Generic;
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
        public bool InfiniteStamina;
        public int DashMode = 1;
        public string Language;

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
                ReadBool(values, "InfiniteStamina", ref result.InfiniteStamina);
                if (values.TryGetValue("DashMode", out string dash) && int.TryParse(dash, out int dashValue))
                    result.DashMode = dashValue < 0 ? -1 : Math.Max(0, Math.Min(2, dashValue));
                if (values.TryGetValue("Language", out string language) &&
                    (language == "en" || language == "zh")) result.Language = language;
            }
            catch { }
            return result;
        }

        static void ReadBool(Dictionary<string, string> values, string key, ref bool target)
        {
            if (values.TryGetValue(key, out string text) && bool.TryParse(text, out bool value)) target = value;
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
                    "InfiniteStamina=" + InfiniteStamina,
                    "DashMode=" + DashMode,
                    "Language=" + (Language ?? "")
                });
            }
            catch { }
        }
    }
}
