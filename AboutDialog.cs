using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>Who made this, and what it is made of.</summary>
    /// <remarks>
    /// The git history says where the project came from, but nobody running a desktop pet reads
    /// a git history. This is the same thing where it can be seen: the original author first,
    /// then this continuation of it, then the game none of it would exist without.
    ///
    /// Every size here is measured rather than chosen. The window holds a sentence of English
    /// or a shorter one of Chinese, in whatever font and at whatever scaling the desktop uses,
    /// and a number picked while looking at one of those is wrong for the others.
    /// </remarks>
    internal sealed class AboutDialog : Form
    {
        const string OriginalAuthorUrl = "https://space.bilibili.com/3493085122136852";
        const string AuthorUrl = "https://github.com/solstice23/";
        const string ProjectUrl = "https://github.com/solstice23/desk-madeline";
        const string CelesteUrl = "https://www.celestegame.com/";

        const int Gutter = 20;   // not Margin: Form has one of those already

        public AboutDialog()
        {
            Text = Loc.T("Menu.About");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            BackColor = SystemColors.Window;
            Font = SystemFonts.MessageBoxFont;

            var portrait = new PictureBox
            {
                Location = new Point(Gutter, Gutter),
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = Portrait()
            };
            Controls.Add(portrait);

            var title = Add(new Label
            {
                Text = Loc.T("App.Title"),
                Font = new Font(Font.FontFamily, Font.Size + 4f, FontStyle.Bold),
                Location = new Point(portrait.Right + 16, Gutter + 2),
                AutoSize = true
            });
            var version = Add(new Label
            {
                Text = Version(),
                ForeColor = SystemColors.GrayText,
                Location = new Point(title.Left, title.Bottom + 4),
                AutoSize = true
            });

            int y = Math.Max(portrait.Bottom, version.Bottom) + 20;
            y = AddLink(ref y, Loc.T("About.OriginalBy"), "Eisyiah", OriginalAuthorUrl);
            y = AddLink(ref y, Loc.T("About.ContinuedBy"), "solstice23", AuthorUrl);
            y += 8;
            y = AddLink(ref y, Loc.T("About.FanProject"), "Celeste", CelesteUrl);

            // Made before the width is worked out, since the pair of them along the bottom is
            // one of the things the window has to be wide enough for.
            var source = new Button
            {
                Text = Loc.T("About.Source"),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(88, 28)
            };
            source.Click += (_, _) => Open(ProjectUrl);
            Controls.Add(source);

            var ok = new Button
            {
                Text = Loc.T("Common.Ok"),
                DialogResult = DialogResult.OK,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(88, 28)
            };
            Controls.Add(ok);

            // As wide as the widest line already is, as wide as the buttons need, and never so
            // narrow that the paragraph below turns into a column.
            int content = 0;
            foreach (Control control in Controls)
            {
                if (control == ok || control == source) continue;   // not placed yet
                content = Math.Max(content, control.Right);
            }
            int width = Math.Max(content - Gutter, TextRenderer.MeasureText(
                new string('m', 46), Font).Width);
            width = Math.Max(width, source.Width + 8 + ok.Width);

            var notice = Add(new Label
            {
                Text = Loc.T("About.ThirdParty"),
                ForeColor = SystemColors.GrayText,
                Location = new Point(Gutter, y + 6),
                Size = new Size(width, TextRenderer.MeasureText(Loc.T("About.ThirdParty"),
                    Font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 2)
            });

            ClientSize = new Size(width + Gutter * 2, notice.Bottom + 14 + ok.Height + Gutter - 8);
            ok.Location = new Point(ClientSize.Width - Gutter - ok.Width, notice.Bottom + 14);
            source.Location = new Point(ok.Left - 8 - source.Width, ok.Top);
            AcceptButton = ok;
            CancelButton = ok;
        }

        T Add<T>(T control) where T : Control
        {
            Controls.Add(control);
            return control;
        }

        /// <summary>A line of text with one word of it linked.</summary>
        int AddLink(ref int y, string text, string linkText, string url)
        {
            var label = Add(new LinkLabel
            {
                Text = string.Format(text, linkText),
                Location = new Point(Gutter, y),
                AutoSize = true,
                LinkBehavior = LinkBehavior.HoverUnderline,
                ForeColor = SystemColors.ControlText
            });
            int at = label.Text.IndexOf(linkText, StringComparison.Ordinal);
            label.Links.Clear();
            if (at >= 0) label.Links.Add(at, linkText.Length, url);
            label.LinkClicked += (_, e) => Open(e.Link.LinkData as string);
            return label.Bottom + 6;
        }

        static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { PetWindow.Log("could not open " + url + ": " + ex.Message); }
        }

        /// <summary>Her portrait, the same one the tray icon is made of.</summary>
        static Image Portrait()
        {
            string file = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "assets", "portrait.png");
            try
            {
                if (System.IO.File.Exists(file)) return new Bitmap(file);
                Bitmap fromAtlas = Sprites.Get(Sprites.PortraitId, false);
                return fromAtlas == null ? null : new Bitmap(fromAtlas);
            }
            catch { return null; }
        }

        /// <summary>
        /// The commit this was built from, and when it was made -- which answers "what am I
        /// running" in a way a version number nobody bumps does not.
        /// </summary>
        /// <remarks>
        /// A build from a tree with no git to ask has neither, and falls back to the assembly's
        /// version; see BuildStamp.
        /// </remarks>
        static string Version()
        {
            if (BuildStamp.Known) return BuildStamp.Describe(BuildStamp.Commit, BuildStamp.Made);

            var assembly = Assembly.GetExecutingAssembly();
            string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "";
            int build = version.IndexOf('+');          // the commit the SDK appends, if any
            return build > 0 ? version.Substring(0, build) : version;
        }
    }
}
