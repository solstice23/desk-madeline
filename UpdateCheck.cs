using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// Whether the build server has a newer build than this one, and where to get it.
    /// </summary>
    /// <remarks>
    /// Every push to master is built and hung off one rolling release, tagged nightly, which is
    /// what this asks about. Which commit that release was built from is written in its notes by
    /// the build workflow -- the run that made it knows, and nothing else on the release says --
    /// and the answer is that commit compared against the one stamped into this build. Hash
    /// first, since two builds of the same commit are the same build whatever their times say;
    /// then the commit dates, so that a build made here after the server's newest is not told to
    /// update to something older than itself.
    ///
    /// Asked only when the user asks. Nothing runs on a timer, and nothing is downloaded here:
    /// the newer build is offered as a link, which is as far as this goes towards replacing
    /// itself. The link is to the file, since a release asset needs no GitHub account.
    /// </remarks>
    internal static class UpdateCheck
    {
        const string Release =
            "https://api.github.com/repos/solstice23/desk-madeline/releases/tags/nightly";

        internal readonly struct Result
        {
            /// <summary>The commit that release was built from, full length, or empty.</summary>
            public readonly string Commit;
            public readonly DateTimeOffset? Made;
            /// <summary>The release's own page.</summary>
            public readonly string Page;
            /// <summary>The file itself, which is what the download button opens.</summary>
            public readonly string Download;
            public readonly string FileName;
            public readonly long Bytes;
            /// <summary>Why there is no answer, or null.</summary>
            public readonly string Error;

            Result(string commit, DateTimeOffset? made, string page, string download,
                string fileName, long bytes, string error)
            {
                Commit = commit; Made = made; Page = page;
                Download = download; FileName = fileName; Bytes = bytes; Error = error;
            }

            public static Result Found(string commit, DateTimeOffset? made, string page,
                string download = "", string fileName = "", long bytes = 0)
                => new Result(commit, made, page, download, fileName, bytes, null);
            public static Result Failed(string why)
                => new Result("", null, "", "", "", 0, why);

            /// <summary>The file and how big it is, for the line under the offer.</summary>
            public string Describe()
                => FileName.Length == 0 ? Loc.T("Update.OnThePage")
                    : Bytes <= 0 ? FileName
                    : FileName + "  ·  " + (Bytes / 1048576.0).ToString("0.0",
                        System.Globalization.CultureInfo.CurrentCulture) + " MB";

            /// <summary>Its hash as it is written everywhere else: the first seven of it.</summary>
            public string Short => Commit.Length >= 7 ? Commit.Substring(0, 7) : Commit;

            /// <summary>Whether that is a build this one is not.</summary>
            public bool Newer => NewerThan(BuildStamp.Commit, BuildStamp.Made);

            /// <summary>The same question against any build, so that it can be asked in a check.</summary>
            public bool NewerThan(string commit, DateTimeOffset? made)
            {
                if (Error != null || Commit.Length == 0) return false;
                // Nothing to compare against: offer it and let the user decide.
                if (string.IsNullOrEmpty(commit)) return true;
                if (Commit.StartsWith(commit, StringComparison.OrdinalIgnoreCase)) return false;
                // A different commit is not necessarily a later one -- a build made here from
                // work that has not been pushed is ahead of the server, not behind it.
                if (Made.HasValue && made.HasValue) return Made.Value > made.Value;
                return true;
            }
        }

        static readonly HttpClient http = Client();

        static HttpClient Client()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            // GitHub turns away a request that does not say who is asking.
            client.DefaultRequestHeaders.Add("User-Agent", "DeskMadeline");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            return client;
        }

        /// <summary>The build hanging off the rolling release. Never throws.</summary>
        public static async Task<Result> Newest()
        {
            try
            {
                using HttpResponseMessage response = await http.GetAsync(Release);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return Result.Failed(Loc.T("Update.NoBuilds"));
                if (!response.IsSuccessStatusCode)
                    return Result.Failed((int)response.StatusCode + " " + response.ReasonPhrase);

                using JsonDocument json = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());
                JsonElement release = json.RootElement;
                string body = Text(release, "body");
                string commit = Labelled(body, "commit");
                DateTimeOffset? made = BuildStamp.Parse(Labelled(body, "committed"));
                // Whoever edits those notes by hand can lose the commit; the release still has a
                // date of its own, and it is better to offer a build with the wrong date on it
                // than to say there is nothing there.
                if (!made.HasValue) made = BuildStamp.Parse(Text(release, "published_at"));

                string download = "", fileName = "";
                long bytes = 0;
                if (release.TryGetProperty("assets", out JsonElement assets) &&
                    assets.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement asset in assets.EnumerateArray())
                    {
                        string name = Text(asset, "name");
                        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        fileName = name;
                        download = Text(asset, "browser_download_url");
                        bytes = asset.TryGetProperty("size", out JsonElement size) &&
                            size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0;
                        break;
                    }

                return Result.Found(commit, made, Text(release, "html_url"),
                    download, fileName, bytes);
            }
            catch (Exception ex) { return Result.Failed(ex.Message); }
        }

        /// <summary>One of the "name: value" lines the build workflow writes into the notes.</summary>
        static string Labelled(string body, string name)
        {
            foreach (string line in body.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) continue;
                return trimmed.Substring(name.Length + 1).Trim();
            }
            return "";
        }

        static string Text(JsonElement element, string name)
            => element.TryGetProperty(name, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        /// <summary>
        /// Ask, with the asking on screen, and then say what came of it. Blocks the caller the
        /// way any modal dialog does; call it on the UI thread.
        /// </summary>
        /// <remarks>
        /// One dialog rather than two. It opens saying that it is asking, and turns into the
        /// answer where it stands -- a window that appears only once a request over the network
        /// has come back leaves the click looking like it did nothing, and for as long as the
        /// server takes to answer there is nothing on screen to cancel either.
        /// </remarks>
        public static void Ask(Control ui)
        {
            TaskDialogButton download = null;
            bool closed = false;

            var waiting = new TaskDialogPage
            {
                Caption = Loc.T("Update.Title"),
                Heading = Loc.T("Update.Checking"),
                Icon = TaskDialogIcon.Information,
                ProgressBar = new TaskDialogProgressBar(TaskDialogProgressBarState.Marquee),
                AllowCancel = true,
                Buttons = { TaskDialogButton.Cancel },
                SizeToContent = true
            };

            // Started once the dialog is up, so that there is always something on screen to
            // cancel, and answered back on this thread -- the modal loop pumps the post.
            waiting.Created += (_, _) => Task.Run(async () =>
            {
                Result result = await Newest();
                if (!ui.IsHandleCreated || ui.IsDisposed) return;
                ui.BeginInvoke(new Action(() =>
                {
                    if (closed) return;      // cancelled while the server was thinking
                    waiting.Navigate(Answer(result, out download));
                }));
            });
            // Navigating raises this on the page being left, which is after the only read of it.
            waiting.Destroyed += (_, _) => closed = true;

            TaskDialogButton chosen = TaskDialog.ShowDialog(waiting);
            if (chosen != null && chosen == download) Open(download.Tag as string);
        }

        /// <summary>The page the waiting one turns into.</summary>
        static TaskDialogPage Answer(Result result, out TaskDialogButton download)
        {
            download = null;
            var page = new TaskDialogPage
            {
                Caption = Loc.T("Update.Title"),
                AllowCancel = true,
                SizeToContent = true
            };

            if (result.Error != null)
            {
                page.Icon = TaskDialogIcon.Warning;
                page.Heading = Loc.T("Update.Failed");
                page.Text = result.Error;
                page.Buttons.Add(TaskDialogButton.OK);
                return page;
            }

            string yours = BuildStamp.Known
                ? BuildStamp.Describe(BuildStamp.Commit, BuildStamp.Made)
                : Loc.T("Update.Unknown");
            string newest = BuildStamp.Describe(result.Short, result.Made);

            if (!result.Newer)
            {
                page.Icon = TaskDialogIcon.Information;
                page.Heading = Loc.T("Update.Current");
                page.Text = string.Format(Loc.T("Update.Yours"), yours);
                page.Buttons.Add(TaskDialogButton.OK);
                return page;
            }

            page.Icon = TaskDialogIcon.ShieldSuccessGreenBar;
            page.Heading = Loc.T("Update.Available");
            page.Text = string.Format(Loc.T("Update.Newest"), newest) + Environment.NewLine
                + string.Format(Loc.T("Update.Yours"), yours);
            page.Footnote = new TaskDialogFootnote(result.Describe());

            // The file itself where there is one: a release asset is handed to anybody, so
            // there is no reason to send the user to a page to find the same link.
            download = new TaskDialogButton(Loc.T("Update.Download"))
            { Tag = result.Download.Length > 0 ? result.Download : result.Page };
            page.Buttons.Add(download);
            page.Buttons.Add(new TaskDialogButton(Loc.T("Update.Later")));
            page.DefaultButton = download;
            return page;
        }

        static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { PetWindow.Log("could not open " + url + ": " + ex.Message); }
        }
    }
}
