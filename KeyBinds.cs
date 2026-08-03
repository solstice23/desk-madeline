using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// 可自定义按键：每个动作可绑定多个键（任一按下即触发）。
    /// 存到 exe 旁的 keys.txt（每行：动作=虚拟键码1,虚拟键码2,...）。
    /// </summary>
    public static class KeyBinds
    {
        public static readonly Dictionary<string, List<int>> Binds =
            new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        public static string ConfigPath;
        public static bool DialogOpen;   // 绑定对话框打开期间屏蔽游戏输入

        public static readonly string[] Actions = { "Left", "Right", "Up", "Down", "Jump", "Dash", "Grab" };
        public static readonly string[] ActionNames = { "向左", "向右", "向上", "向下", "跳跃", "冲刺", "攀爬" };

        public static void LoadDefaults()
        {
            Binds.Clear();
            Binds["Left"]  = new List<int> { 0x25, 0x41 };   // ← / A
            Binds["Right"] = new List<int> { 0x27, 0x44 };   // → / D
            Binds["Up"]    = new List<int> { 0x26, 0x57 };   // ↑ / W
            Binds["Down"]  = new List<int> { 0x28, 0x53 };   // ↓ / S
            Binds["Jump"]  = new List<int> { 0x43, 0x20 };   // C / 空格
            Binds["Dash"]  = new List<int> { 0x58 };         // X
            Binds["Grab"]  = new List<int> { 0x5A };         // Z
        }

        public static void Load(string path)
        {
            ConfigPath = path;
            LoadDefaults();
            try
            {
                if (!System.IO.File.Exists(path)) return;
                foreach (var line in System.IO.File.ReadAllLines(path))
                {
                    var p = line.Split(new[] { '=', ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 2) continue;
                    if (!Binds.ContainsKey(p[0])) continue;
                    var list = new List<int>();
                    for (int i = 1; i < p.Length; i++)
                        if (int.TryParse(p[i], out int vk) && vk > 0) list.Add(vk);
                    if (list.Count > 0) Binds[p[0]] = list;
                }
            }
            catch { }
        }

        public static void Save(string path)
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in Binds)
                    lines.Add(kv.Key + "=" + string.Join(",", kv.Value));
                System.IO.File.WriteAllLines(path, lines);
            }
            catch { }
        }

        /// <summary>任一所绑定的键被按下即视为触发。</summary>
        public static bool Pressed(string action)
        {
            if (Binds.TryGetValue(action, out var list))
                foreach (int vk in list)
                    if (Win32.KeyDown(vk)) return true;
            return false;
        }

        public static string StringFor(string action)
        {
            if (!Binds.TryGetValue(action, out var list) || list.Count == 0) return "（未绑定）";
            return string.Join(" / ", list.ConvertAll(Name));
        }

        /// <summary>虚拟键码 → 可读名称。</summary>
        public static string Name(int vk)
        {
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // 0-9
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // A-Z
            if (vk >= 0x60 && vk <= 0x69) return "小键盘" + (vk - 0x60);
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x70 + 1);
            switch (vk)
            {
                case 0x20: return "空格";
                case 0x1B: return "Esc";
                case 0x0D: return "回车";
                case 0x09: return "Tab";
                case 0x08: return "退格";
                case 0x25: return "←";
                case 0x26: return "↑";
                case 0x27: return "→";
                case 0x28: return "↓";
                case 0x21: return "PgUp";
                case 0x22: return "PgDn";
                case 0x23: return "End";
                case 0x24: return "Home";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
                case 0xA0: return "左Shift";
                case 0xA1: return "右Shift";
                case 0xA2: return "左Ctrl";
                case 0xA3: return "右Ctrl";
                case 0xA4: return "左Alt";
                case 0xA5: return "右Alt";
                default: return ((Keys)vk).ToString();
            }
        }
    }

    /// <summary>按键绑定设置对话框：为每个动作添加/清除多个键。</summary>
    public class KeyBindDialog : Form
    {
        readonly Dictionary<string, Label> valueLabels = new Dictionary<string, Label>();
        string capturingAction;
        Button capturingButton;

        public KeyBindDialog()
        {
            Text = "按键设置";
            ClientSize = new Size(400, KeyBinds.Actions.Length * 30 + 66);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            int y = 12;
            for (int i = 0; i < KeyBinds.Actions.Length; i++)
            {
                string a = KeyBinds.Actions[i];
                Controls.Add(new Label { Text = KeyBinds.ActionNames[i], Location = new Point(14, y + 4), AutoSize = true });
                var val = new Label { Location = new Point(88, y + 4), AutoSize = true, Text = KeyBinds.StringFor(a) };
                valueLabels[a] = val;
                Controls.Add(val);
                var add = new Button { Text = "添加", Location = new Point(262, y), Size = new Size(54, 24), Tag = a };
                add.Click += (s, _) => StartCapture(a, (Button)s, val);
                Controls.Add(add);
                var clear = new Button { Text = "清除", Location = new Point(322, y), Size = new Size(54, 24) };
                clear.Click += (_, __) => { KeyBinds.Binds[a].Clear(); RefreshAll(); };
                Controls.Add(clear);
                y += 30;
            }

            y += 6;
            var def = new Button { Text = "恢复默认", Location = new Point(14, y), Size = new Size(92, 26) };
            def.Click += (_, __) => { KeyBinds.LoadDefaults(); RefreshAll(); };
            Controls.Add(def);
            var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(250, y), Size = new Size(60, 26) };
            Controls.Add(ok);
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(316, y), Size = new Size(60, 26) };
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        void StartCapture(string action, Button btn, Label val)
        {
            capturingAction = action;
            capturingButton = btn;
            btn.Text = "按任意键…";
            val.Text = "等待按键…";
        }

        void EndCapture()
        {
            capturingAction = null;
            if (capturingButton != null) { capturingButton.Text = "添加"; capturingButton = null; }
            RefreshAll();
        }

        void AddKey(int vk)
        {
            var list = KeyBinds.Binds[capturingAction];
            if (!list.Contains(vk)) list.Add(vk);
            EndCapture();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (capturingAction != null)
            {
                Keys code = keyData & Keys.KeyCode;
                if (code == Keys.Escape) { EndCapture(); return true; }   // Esc 取消本次捕获
                if (code == Keys.ShiftKey || code == Keys.ControlKey || code == Keys.Menu ||
                    code == Keys.LWin || code == Keys.RWin) return true;  // 忽略修饰键
                AddKey((int)code);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        void RefreshAll()
        {
            foreach (var a in KeyBinds.Actions)
                if (valueLabels.TryGetValue(a, out var l)) l.Text = KeyBinds.StringFor(a);
        }
    }
}
