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
    /// There are no releases to check: every push to master is built by the build workflow and
    /// what comes out is an artifact of that run. So the question "is there a newer one" is
    /// asked of the workflow's runs -- the newest that succeeded on master -- and answered by
    /// comparing its commit against the one stamped into this build. Hash first, since two
    /// builds of the same commit are the same build whatever their times say; then the commit
    /// dates, so that a local build made after the server's newest is not told to update.
    ///
    /// Asked only when the user asks. Nothing here runs on a timer, and nothing is downloaded:
    /// the newer build is offered as a link to the page it is on, which is as far as this goes
    /// towards replacing itself.
    /// </remarks>
    internal static class UpdateCheck
    {
        const string Runs = "https://api.github.com/repos/solstice23/desk-madeline/actions/"
            + "workflows/build.yml/runs?branch=master&status=success&per_page=1";

        internal readonly struct Result
        {
            /// <summary>The build server's newest commit, full length, or empty.</summary>
            public readonly string Commit;
            public readonly DateTimeOffset? Made;
            /// <summary>That run's page, where its artifact is.</summary>
            public readonly string Page;
            /// <summary>Why there is no answer, or null.</summary>
            public readonly string Error;

            Result(string commit, DateTimeOffset? made, string page, string error)
            { Commit = commit; Made = made; Page = page; Error = error; }

            public static Result Found(string commit, DateTimeOffset? made, string page)
                => new Result(commit, made, page, null);
            public static Result Failed(string why) => new Result("", null, "", why);

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

        /// <summary>The newest build of master that came out whole. Never throws.</summary>
        public static async Task<Result> Newest()
        {
            try
            {
                using HttpResponseMessage response = await http.GetAsync(Runs);
                if (!response.IsSuccessStatusCode)
                    return Result.Failed((int)response.StatusCode + " " + response.ReasonPhrase);

                using JsonDocument json = JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());
                if (!json.RootElement.TryGetProperty("workflow_runs", out JsonElement runs) ||
                    runs.ValueKind != JsonValueKind.Array || runs.GetArrayLength() == 0)
                    return Result.Failed(Loc.T("Update.NoBuilds"));

                JsonElement run = runs[0];
                string commit = Text(run, "head_sha");
                string page = Text(run, "html_url");
                // head_commit is the commit itself, so its timestamp is the one to compare with
                // the stamp in this build; the run's own times are when the server got to it.
                DateTimeOffset? made = run.TryGetProperty("head_commit", out JsonElement head)
                    && head.ValueKind == JsonValueKind.Object
                    ? BuildStamp.Parse(Text(head, "timestamp")) : null;
                if (commit.Length == 0) return Result.Failed(Loc.T("Update.NoBuilds"));
                return Result.Found(commit, made, page);
            }
            catch (Exception ex) { return Result.Failed(ex.Message); }
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
            page.Footnote = new TaskDialogFootnote(Loc.T("Update.Artifact"));

            download = new TaskDialogButton(Loc.T("Update.Download")) { Tag = result.Page };
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
