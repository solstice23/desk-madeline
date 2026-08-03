// 自动生成：刘海锚点偏移(相对帧中心x=16,基准y=27) + 刘海朝向帧索引(0左/1中/2右)
namespace DeskMadeline
{
    public static class HairMeta
    {
        public struct Meta { public System.Drawing.PointF Offset; public int Bangs;
            public Meta(System.Drawing.PointF o, int b) { Offset = o; Bangs = b; } }
        public static readonly string[] BangsFrames = { "bangs00", "bangs01", "bangs02" };
        public static readonly System.Collections.Generic.Dictionary<string, Meta> Offsets = new System.Collections.Generic.Dictionary<string, Meta>
        {
            { "climb00", new Meta(new System.Drawing.PointF(-0.9f, -0.9f), 2) },
            { "climb01", new Meta(new System.Drawing.PointF(-1.1f, -1.2f), 2) },
            { "climb02", new Meta(new System.Drawing.PointF(-1.2f, -1.6f), 2) },
            { "climb03", new Meta(new System.Drawing.PointF(-0.9f, -1.0f), 2) },
            { "climb04", new Meta(new System.Drawing.PointF(-1.4f, -1.0f), 2) },
            { "climb05", new Meta(new System.Drawing.PointF(-1.1f, -1.0f), 2) },
            { "climb06", new Meta(new System.Drawing.PointF(-0.9f, -0.9f), 2) },
            { "climb07", new Meta(new System.Drawing.PointF(-1.1f, -1.0f), 2) },
            { "climb08", new Meta(new System.Drawing.PointF(-0.9f, -0.9f), 2) },
            { "climb09", new Meta(new System.Drawing.PointF(-0.1f, -0.9f), 2) },
            { "climb10", new Meta(new System.Drawing.PointF(0.2f, -1.4f), 2) },
            { "climb11", new Meta(new System.Drawing.PointF(0.2f, -1.4f), 2) },
            { "climb12", new Meta(new System.Drawing.PointF(-0.9f, -0.9f), 2) },
            { "climb13", new Meta(new System.Drawing.PointF(-1.5f, -0.9f), 2) },
            { "climb14", new Meta(new System.Drawing.PointF(-1.5f, -0.9f), 2) },
            { "dangling00", new Meta(new System.Drawing.PointF(-0.9f, -0.6f), 2) },
            { "dangling01", new Meta(new System.Drawing.PointF(-1.0f, -0.6f), 2) },
            { "dangling02", new Meta(new System.Drawing.PointF(-1.1f, -0.6f), 2) },
            { "dangling03", new Meta(new System.Drawing.PointF(-1.3f, -0.6f), 2) },
            { "dangling04", new Meta(new System.Drawing.PointF(-2.2f, -0.8f), 2) },
            { "dangling05", new Meta(new System.Drawing.PointF(-1.3f, -0.8f), 2) },
            { "dangling06", new Meta(new System.Drawing.PointF(-1.3f, -0.8f), 2) },
            { "dangling07", new Meta(new System.Drawing.PointF(-1.4f, -0.8f), 2) },
            { "dangling08", new Meta(new System.Drawing.PointF(-1.5f, -0.8f), 2) },
            { "dangling09", new Meta(new System.Drawing.PointF(-1.5f, -0.8f), 2) },
            { "dash00", new Meta(new System.Drawing.PointF(-1.6f, 0.4f), 2) },
            { "dash01", new Meta(new System.Drawing.PointF(-1.8f, 0.4f), 2) },
            { "dash02", new Meta(new System.Drawing.PointF(-2.2f, 0.4f), 2) },
            { "dash03", new Meta(new System.Drawing.PointF(-2.1f, 0.8f), 2) },
            { "duck", new Meta(new System.Drawing.PointF(-1.9f, 1.4f), 0) },
            { "edge00", new Meta(new System.Drawing.PointF(-3.2f, -0.5f), 1) },
            { "edge01", new Meta(new System.Drawing.PointF(-3.2f, -0.5f), 1) },
            { "edge02", new Meta(new System.Drawing.PointF(-3.2f, -0.5f), 1) },
            { "edge03", new Meta(new System.Drawing.PointF(-3.0f, 0.1f), 1) },
            { "edge04", new Meta(new System.Drawing.PointF(-3.3f, -0.6f), 1) },
            { "edge05", new Meta(new System.Drawing.PointF(-3.2f, -0.4f), 1) },
            { "edge06", new Meta(new System.Drawing.PointF(-3.1f, -0.2f), 1) },
            { "edge07", new Meta(new System.Drawing.PointF(-3.0f, -0.5f), 2) },
            { "edge08", new Meta(new System.Drawing.PointF(-3.2f, -0.3f), 2) },
            { "edge09", new Meta(new System.Drawing.PointF(-2.9f, -0.2f), 2) },
            { "fall00", new Meta(new System.Drawing.PointF(-1.8f, -1.3f), 2) },
            { "fall01", new Meta(new System.Drawing.PointF(-1.8f, -1.3f), 2) },
            { "fall02", new Meta(new System.Drawing.PointF(-1.6f, -1.2f), 1) },
            { "fall03", new Meta(new System.Drawing.PointF(-1.5f, -1.0f), 1) },
            { "fall04", new Meta(new System.Drawing.PointF(-1.6f, -1.2f), 1) },
            { "fall05", new Meta(new System.Drawing.PointF(-1.5f, -1.0f), 1) },
            { "fall06", new Meta(new System.Drawing.PointF(-1.6f, -1.2f), 1) },
            { "fall07", new Meta(new System.Drawing.PointF(-1.5f, -1.0f), 1) },
            { "flip00", new Meta(new System.Drawing.PointF(1.9f, 0.1f), 0) },
            { "flip01", new Meta(new System.Drawing.PointF(1.6f, 0.3f), 0) },
            { "flip02", new Meta(new System.Drawing.PointF(1.0f, 0.5f), 1) },
            { "flip03", new Meta(new System.Drawing.PointF(0.7f, 0.5f), 1) },
            { "flip04", new Meta(new System.Drawing.PointF(0.4f, 1.2f), 2) },
            { "flip05", new Meta(new System.Drawing.PointF(-0.6f, 0.9f), 2) },
            { "flip06", new Meta(new System.Drawing.PointF(0.0f, 0.5f), 2) },
            { "flip07", new Meta(new System.Drawing.PointF(-1.5f, -0.0f), 2) },
            { "flip08", new Meta(new System.Drawing.PointF(0.9f, 1.3f), 2) },
            { "idle00", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "idle01", new Meta(new System.Drawing.PointF(-2.2f, -0.5f), 1) },
            { "idle02", new Meta(new System.Drawing.PointF(-1.9f, -0.2f), 1) },
            { "idle03", new Meta(new System.Drawing.PointF(-2.2f, 0.6f), 1) },
            { "idle04", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "idle05", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "idle06", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "idle07", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "idle08", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "idleA00", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "idleA01", new Meta(new System.Drawing.PointF(-2.2f, -0.5f), 1) },
            { "idleA02", new Meta(new System.Drawing.PointF(-1.9f, -0.2f), 0) },
            { "idleA03", new Meta(new System.Drawing.PointF(-2.3f, -0.1f), 0) },
            { "idleA04", new Meta(new System.Drawing.PointF(-2.6f, -0.2f), 0) },
            { "idleA05", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 0) },
            { "idleA06", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 0) },
            { "idleA07", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 0) },
            { "idleA08", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 0) },
            { "idleA09", new Meta(new System.Drawing.PointF(-2.5f, 0.6f), 1) },
            { "idleA10", new Meta(new System.Drawing.PointF(-2.5f, 0.4f), 1) },
            { "idleA11", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "idleB00", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "idleB01", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB02", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB03", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB04", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB05", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB06", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB07", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB08", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB09", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 0) },
            { "idleB10", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 0) },
            { "idleB11", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 0) },
            { "idleB12", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 0) },
            { "idleB13", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB14", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB15", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB16", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "idleB17", new Meta(new System.Drawing.PointF(-2.2f, 0.7f), 1) },
            { "idleB18", new Meta(new System.Drawing.PointF(-2.2f, 0.7f), 1) },
            { "idleB19", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "idleB20", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "idleB21", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "idleB22", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "idleB23", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "idleC00", new Meta(new System.Drawing.PointF(-2.2f, -0.5f), 1) },
            { "idleC01", new Meta(new System.Drawing.PointF(-1.9f, -0.2f), 0) },
            { "idleC02", new Meta(new System.Drawing.PointF(-2.3f, -0.1f), 0) },
            { "idleC03", new Meta(new System.Drawing.PointF(-2.3f, -0.1f), 0) },
            { "idleC04", new Meta(new System.Drawing.PointF(-2.3f, -0.1f), 0) },
            { "idleC05", new Meta(new System.Drawing.PointF(-2.3f, -0.1f), 0) },
            { "idleC06", new Meta(new System.Drawing.PointF(-1.9f, 0.7f), 2) },
            { "idleC07", new Meta(new System.Drawing.PointF(-1.3f, 0.7f), 2) },
            { "idleC08", new Meta(new System.Drawing.PointF(-2.5f, 0.4f), 1) },
            { "idleC09", new Meta(new System.Drawing.PointF(-2.5f, 0.6f), 1) },
            { "idleC10", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "idleC11", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "jumpFast00", new Meta(new System.Drawing.PointF(-1.2f, -1.5f), 1) },
            { "jumpFast01", new Meta(new System.Drawing.PointF(-1.7f, -1.3f), 2) },
            { "jumpFast02", new Meta(new System.Drawing.PointF(-1.5f, -1.2f), 2) },
            { "jumpFast03", new Meta(new System.Drawing.PointF(-2.1f, 0.5f), 1) },
            { "jumpSlow00", new Meta(new System.Drawing.PointF(-1.5f, -1.5f), 2) },
            { "jumpSlow01", new Meta(new System.Drawing.PointF(-1.8f, -1.4f), 2) },
            { "jumpSlow02", new Meta(new System.Drawing.PointF(-1.8f, -1.3f), 2) },
            { "jumpSlow03", new Meta(new System.Drawing.PointF(-2.3f, -0.2f), 1) },
            // Exact PlayerSprite metadata for the holdable-specific sheets.
            // These cannot fall back to the visually tuned non-carry sheets:
            // vanilla uses bangs00 and the integer HairOffset values below.
            { "idle_carry00", new Meta(new System.Drawing.PointF(0f, -2f), 0) },
            { "idle_carry01", new Meta(new System.Drawing.PointF(0f, -2f), 0) },
            { "idle_carry02", new Meta(new System.Drawing.PointF(0f, -2f), 0) },
            { "idle_carry03", new Meta(new System.Drawing.PointF(0f, -2f), 0) },
            { "idle_carry04", new Meta(new System.Drawing.PointF(0f, -1f), 0) },
            { "idle_carry05", new Meta(new System.Drawing.PointF(0f, -1f), 0) },
            { "idle_carry06", new Meta(new System.Drawing.PointF(0f, -1f), 0) },
            { "idle_carry07", new Meta(new System.Drawing.PointF(0f, -1f), 0) },
            { "idle_carry08", new Meta(new System.Drawing.PointF(0f, -1f), 0) },
            { "jump_carry00", new Meta(new System.Drawing.PointF(1f, -3f), 0) },
            { "jump_carry01", new Meta(new System.Drawing.PointF(1f, -3f), 0) },
            { "jump_carry02", new Meta(new System.Drawing.PointF(1f, -2f), 0) },
            { "jump_carry03", new Meta(new System.Drawing.PointF(0f, -2f), 0) },
            { "run_carry00", new Meta(new System.Drawing.PointF(1f, -2f), 0) },
            { "run_carry01", new Meta(new System.Drawing.PointF(1f, -1f), 0) },
            { "run_carry02", new Meta(new System.Drawing.PointF(1f, -1f), 0) },
            { "run_carry03", new Meta(new System.Drawing.PointF(1f, -1f), 0) },
            { "run_carry04", new Meta(new System.Drawing.PointF(1f, -3f), 0) },
            { "run_carry05", new Meta(new System.Drawing.PointF(1f, -2f), 0) },
            { "run_carry06", new Meta(new System.Drawing.PointF(1f, -1f), 0) },
            { "run_carry07", new Meta(new System.Drawing.PointF(1f, -1f), 0) },
            { "run_carry08", new Meta(new System.Drawing.PointF(1f, -1f), 0) },
            { "run_carry09", new Meta(new System.Drawing.PointF(1f, -1f), 0) },
            { "run_carry10", new Meta(new System.Drawing.PointF(1f, -3f), 0) },
            { "run_carry11", new Meta(new System.Drawing.PointF(1f, -2f), 0) },
            { "pickup00", new Meta(new System.Drawing.PointF(2f, 0f), 0) },
            { "pickup01", new Meta(new System.Drawing.PointF(1f, -1f), 0) },
            { "pickup02", new Meta(new System.Drawing.PointF(0f, -2f), 0) },
            { "pickup03", new Meta(new System.Drawing.PointF(0f, -2f), 0) },
            { "pickup04", new Meta(new System.Drawing.PointF(0f, -2f), 0) },
            { "throw00", new Meta(new System.Drawing.PointF(0f, -3f), 0) },
            { "throw01", new Meta(new System.Drawing.PointF(2f, -2f), 0) },
            { "throw02", new Meta(new System.Drawing.PointF(2f, -2f), 0) },
            { "throw03", new Meta(new System.Drawing.PointF(1f, -2f), 0) },
            { "lookUp00", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "lookUp01", new Meta(new System.Drawing.PointF(-2.2f, -0.5f), 1) },
            { "lookUp02", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 1) },
            { "lookUp03", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "lookUp04", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "lookUp05", new Meta(new System.Drawing.PointF(-2.4f, 0.4f), 1) },
            { "lookUp06", new Meta(new System.Drawing.PointF(-2.0f, -0.3f), 1) },
            { "lookUp07", new Meta(new System.Drawing.PointF(-2.0f, -0.0f), 1) },
            { "runFast00", new Meta(new System.Drawing.PointF(-2.5f, -0.8f), 2) },
            { "runFast01", new Meta(new System.Drawing.PointF(-2.6f, 0.2f), 2) },
            { "runFast02", new Meta(new System.Drawing.PointF(-2.8f, 0.1f), 2) },
            { "runFast03", new Meta(new System.Drawing.PointF(-2.8f, 0.2f), 2) },
            { "runFast04", new Meta(new System.Drawing.PointF(-2.3f, -1.5f), 2) },
            { "runFast05", new Meta(new System.Drawing.PointF(-2.6f, -0.8f), 2) },
            { "runFast06", new Meta(new System.Drawing.PointF(-2.6f, -0.8f), 2) },
            { "runFast07", new Meta(new System.Drawing.PointF(-2.3f, 0.1f), 2) },
            { "runFast08", new Meta(new System.Drawing.PointF(-2.5f, 0.0f), 2) },
            { "runFast09", new Meta(new System.Drawing.PointF(-2.7f, 0.2f), 1) },
            { "runFast10", new Meta(new System.Drawing.PointF(-2.5f, -1.9f), 1) },
            { "runFast11", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 2) },
            // runStumble uses the same X/bangs sequence as runFast; its first
            // crouched recovery frames shift HairOffset downward in Sprites.xml.
            { "runStumble00", new Meta(new System.Drawing.PointF(-2.5f, 2.2f), 2) },
            { "runStumble01", new Meta(new System.Drawing.PointF(-2.6f, 3.2f), 2) },
            { "runStumble02", new Meta(new System.Drawing.PointF(-2.8f, 3.1f), 2) },
            { "runStumble03", new Meta(new System.Drawing.PointF(-2.8f, 1.2f), 2) },
            { "runStumble04", new Meta(new System.Drawing.PointF(-2.3f, -0.5f), 2) },
            { "runStumble05", new Meta(new System.Drawing.PointF(-2.6f, -0.8f), 2) },
            { "runStumble06", new Meta(new System.Drawing.PointF(-2.6f, -0.8f), 2) },
            { "runStumble07", new Meta(new System.Drawing.PointF(-2.3f, 0.1f), 2) },
            { "runStumble08", new Meta(new System.Drawing.PointF(-2.5f, 0.0f), 2) },
            { "runStumble09", new Meta(new System.Drawing.PointF(-2.7f, 0.2f), 1) },
            { "runStumble10", new Meta(new System.Drawing.PointF(-2.5f, -1.9f), 1) },
            { "runStumble11", new Meta(new System.Drawing.PointF(-2.2f, -0.4f), 2) },
            { "runSlow00", new Meta(new System.Drawing.PointF(-2.6f, -0.7f), 2) },
            { "runSlow01", new Meta(new System.Drawing.PointF(-2.6f, 0.2f), 2) },
            { "runSlow02", new Meta(new System.Drawing.PointF(-2.8f, 0.1f), 2) },
            { "runSlow03", new Meta(new System.Drawing.PointF(-2.6f, 0.3f), 2) },
            { "runSlow04", new Meta(new System.Drawing.PointF(-2.3f, -1.5f), 2) },
            { "runSlow05", new Meta(new System.Drawing.PointF(-2.6f, -0.8f), 2) },
            { "runSlow06", new Meta(new System.Drawing.PointF(-2.6f, -0.8f), 2) },
            { "runSlow07", new Meta(new System.Drawing.PointF(-2.3f, 0.1f), 2) },
            { "runSlow08", new Meta(new System.Drawing.PointF(-2.5f, 0.0f), 2) },
            { "runSlow09", new Meta(new System.Drawing.PointF(-2.5f, 0.2f), 1) },
            { "runSlow10", new Meta(new System.Drawing.PointF(-2.5f, -1.9f), 1) },
            { "runSlow11", new Meta(new System.Drawing.PointF(-2.7f, -0.7f), 2) },
            { "tired00", new Meta(new System.Drawing.PointF(-2.4f, 1.2f), 1) },
            { "tired01", new Meta(new System.Drawing.PointF(-2.7f, 1.5f), 1) },
            { "tired02", new Meta(new System.Drawing.PointF(-2.4f, 1.2f), 1) },
            { "tired03", new Meta(new System.Drawing.PointF(-2.7f, 0.8f), 1) },
        };

        // ===== 运行时覆盖（hair_tweaks.txt，手调头发用，免重编译）=====
        // 每行格式：帧名 x y bangs  例如： idle00 -2.5 0.2 1
        static readonly System.Collections.Generic.Dictionary<string, Meta> Overrides =
            new System.Collections.Generic.Dictionary<string, Meta>(System.StringComparer.OrdinalIgnoreCase);

        public static void LoadOverrides(string path)
        {
            Overrides.Clear();
            try
            {
                if (!System.IO.File.Exists(path)) { PetWindow.Log("hair_tweaks: none"); return; }
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    var p = line.Split(new[] { ' ', '\t', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 4) continue;
                    if (float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                        float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
                        int.TryParse(p[3], out var b))
                        Overrides[p[0]] = new Meta(new System.Drawing.PointF(x, y), b);
                }
                PetWindow.Log("hair_tweaks: loaded " + Overrides.Count + " from " + path);
            }
            catch (System.Exception ex) { PetWindow.Log("hair_tweaks: load error " + ex.Message); }
        }

        /// <summary>取某帧头发元数据：优先运行时覆盖，其次默认表。</summary>
        public static bool TryGet(string frameId, out Meta meta)
        {
            if (frameId != null && Overrides.TryGetValue(frameId, out meta)) return true;
            if (frameId != null && Offsets.TryGetValue(frameId, out meta)) return true;
            // Carry sheets use the same head poses as their non-carry counterparts.
            // This fallback keeps hair anchored for base and partially implemented
            // skins without requiring duplicate metadata for every carry frame.
            if (frameId != null)
            {
                string fallback = null;
                if (frameId.StartsWith("idle_carry")) fallback = "idle" + frameId.Substring("idle_carry".Length);
                else if (frameId.StartsWith("run_carry")) fallback = "runFast" + frameId.Substring("run_carry".Length);
                else if (frameId.StartsWith("jump_carry")) fallback = "jumpSlow" + frameId.Substring("jump_carry".Length);
                if (fallback != null && Offsets.TryGetValue(fallback, out meta)) return true;
            }
            meta = default; return false;
        }

        public static void SaveOverride(string path, string frameId, float x, float y, int bangs)
        {
            Overrides[frameId] = new Meta(new System.Drawing.PointF(x, y), bangs);
            try
            {
                var lines = new System.Collections.Generic.List<string>();
                foreach (var kv in Overrides)
                    lines.Add(kv.Key + " " +
                        kv.Value.Offset.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " +
                        kv.Value.Offset.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " " +
                        kv.Value.Bangs);
                System.IO.File.WriteAllLines(path, lines);
            }
            catch { }
        }
    }
}
