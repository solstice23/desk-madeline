using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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
    /// Asked only when the user asks: nothing here runs on a timer, and nothing is fetched
    /// without being asked for. What is offered is the newer build itself -- SelfUpdate does the
    /// fetching and the swap -- and beside it the release's page, for anyone who would rather
    /// see what they are getting first.
    /// </remarks>
    internal static class UpdateCheck
    {
        const string Release =
            "https://api.github.com/repos/solstice23/desk-madeline/releases/tags/nightly";
        /// <summary>Where to send somebody when this cannot answer for itself.</summary>
        const string ReleasePage =
            "https://github.com/solstice23/desk-madeline/releases/tag/nightly";

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

        /// <summary>Shared with SelfUpdate, which fetches from the same host.</summary>
        internal static readonly HttpClient Http = Client();

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
                using HttpResponseMessage response = await Http.GetAsync(Release);
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
        /// <param name="quit">
        /// How to close the pet, for the one ending where it has to: a build cannot write over
        /// itself, so the last thing an update does here is leave and let the new one start.
        /// </param>
        public static void Ask(Control ui, Action quit) => new Conversation(ui, quit).Run();

        /// <summary>
        /// The dialog, as one thing that changes rather than a series of them: asking, then the
        /// answer, then -- if the answer is taken up -- the fetching, and then the pet is gone
        /// and the new one is starting.
        /// </summary>
        sealed class Conversation
        {
            readonly Control ui;
            readonly Action quit;
            TaskDialogPage showing;
            CancellationTokenSource fetching;
            bool leaving;

            public Conversation(Control ui, Action quit) { this.ui = ui; this.quit = quit; }

            public void Run()
            {
                showing = Waiting();
                TaskDialog.ShowDialog(showing);
                fetching?.Cancel();
                // Only once the dialog is off the screen: closing the window underneath it
                // while it is still up is not something to ask of either of them.
                if (leaving) quit();
            }

            /// <summary>Move the dialog on to its next state, if it is still there to move.</summary>
            void Turn(TaskDialogPage next)
            {
                if (showing == null) return;
                showing.Navigate(next);
                showing = next;
            }

            TaskDialogPage Blank(string heading, TaskDialogIcon icon) => new TaskDialogPage
            {
                Caption = Loc.T("Update.Title"),
                Heading = heading,
                Icon = icon,
                AllowCancel = true,
                SizeToContent = true
            };

            /// <summary>Asking. Up before the request, so the click has something to show for it.</summary>
            TaskDialogPage Waiting()
            {
                TaskDialogPage page = Blank(Loc.T("Update.Checking"), TaskDialogIcon.Information);
                page.ProgressBar = new TaskDialogProgressBar(TaskDialogProgressBarState.Marquee);
                page.Buttons.Add(new TaskDialogButton(Loc.T("Common.Cancel")));
                // Started once it is up, and answered back on this thread; the modal loop pumps
                // the post. Gone means cancelled while the server was still thinking.
                page.Created += (_, _) => Task.Run(async () =>
                {
                    Result result = await Newest();
                    Back(() => Turn(Answer(result)));
                });
                page.Destroyed += (_, _) => { if (showing == page) showing = null; };
                return page;
            }

            /// <summary>What came of asking.</summary>
            TaskDialogPage Answer(Result result)
            {
                if (result.Error != null)
                {
                    TaskDialogPage failed = Blank(Loc.T("Update.Failed"), TaskDialogIcon.Warning);
                    failed.Text = result.Error;
                    // Nothing here can say what went wrong on GitHub's side, so the way to find
                    // out is offered instead of guessed at.
                    var byHand = new TaskDialogButton(Loc.T("Update.Manually"));
                    byHand.Click += (_, _) => Open(ReleasePage);
                    var close = new TaskDialogButton(Loc.T("Common.Close"));
                    failed.Buttons.Add(byHand);
                    failed.Buttons.Add(close);
                    failed.DefaultButton = close;
                    return failed;
                }

                string yours = BuildStamp.Known
                    ? BuildStamp.Describe(BuildStamp.Commit, BuildStamp.Made)
                    : Loc.T("Update.Unknown");

                if (!result.Newer)
                {
                    TaskDialogPage current = Blank(Loc.T("Update.Current"),
                        TaskDialogIcon.Information);
                    current.Text = string.Format(Loc.T("Update.Yours"), yours);
                    current.Buttons.Add(new TaskDialogButton(Loc.T("Common.Ok")));
                    return current;
                }

                // Plain, not one of the shields: those paint the whole head of the dialog in a
                // colour, and a desktop pet having a new build is not a security matter.
                TaskDialogPage there = Blank(Loc.T("Update.Available"),
                    TaskDialogIcon.Information);
                there.Text = string.Format(Loc.T("Update.Newest"),
                        BuildStamp.Describe(result.Short, result.Made))
                    + Environment.NewLine + string.Format(Loc.T("Update.Yours"), yours);
                there.Footnote = new TaskDialogFootnote(result.Describe());

                var page = new TaskDialogButton(Loc.T("Update.OnGitHub"));
                page.Click += (_, _) => Open(result.Page);
                there.Buttons.Add(page);

                // Only where there is a file to fetch and somewhere to put it.
                if (result.Download.Length > 0 && SelfUpdate.Possible)
                {
                    // It does not close the dialog: the dialog is where the fetching is shown.
                    var install = new TaskDialogButton(Loc.T("Update.Install"))
                    { AllowCloseDialog = false };
                    install.Click += (_, _) => Turn(Fetching(result));
                    there.Buttons.Add(install);
                    there.DefaultButton = install;
                }
                else there.DefaultButton = page;
                return there;
            }

            /// <summary>Fetching it, and then leaving so that it can take this one's place.</summary>
            TaskDialogPage Fetching(Result result)
            {
                var bar = new TaskDialogProgressBar(TaskDialogProgressBarState.Normal);
                TaskDialogPage page = Blank(Loc.T("Update.Downloading"),
                    TaskDialogIcon.Information);
                page.ProgressBar = bar;
                page.Text = new SelfUpdate.Fetched(0, result.Bytes).ToString();
                page.Footnote = new TaskDialogFootnote(result.Describe());
                // Held on to: this is the one the dialog is closed by when the fetch is done,
                // and a button can only be clicked from code while it is bound to a page.
                var stop = new TaskDialogButton(Loc.T("Common.Cancel"));
                page.Buttons.Add(stop);

                fetching = new CancellationTokenSource();
                CancellationToken cancel = fetching.Token;
                // Progress<T> was made here, so it comes back here to be shown.
                var progress = new Progress<SelfUpdate.Fetched>(fetched =>
                {
                    if (showing != page) return;
                    bar.Value = Math.Clamp(fetched.Percent, 0, 100);
                    page.Text = fetched.ToString();
                });

                page.Created += (_, _) => Task.Run(async () =>
                {
                    try
                    {
                        string unpacked = await SelfUpdate.Fetch(result.Download, progress, cancel);
                        cancel.ThrowIfCancellationRequested();
                        Back(() =>
                        {
                            if (!SelfUpdate.Handover(unpacked))
                            { Turn(Broke(Loc.T("Update.HandoverFailed"), result)); return; }
                            // The script is waiting for this process to end, so end it: close
                            // the dialog, and Run does the rest once it is off the screen.
                            leaving = true;
                            stop.PerformClick();
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Back(() => Turn(Broke(ex.Message, result))); }
                });
                page.Destroyed += (_, _) => { if (showing == page) showing = null; };
                return page;
            }

            /// <summary>The fetch did not come off; the page it is on is still there to be had.</summary>
            TaskDialogPage Broke(string why, Result result)
            {
                TaskDialogPage page = Blank(Loc.T("Update.DownloadFailed"), TaskDialogIcon.Warning);
                page.Text = why;
                var byHand = new TaskDialogButton(Loc.T("Update.OnGitHub"));
                byHand.Click += (_, _) => Open(result.Page);
                var close = new TaskDialogButton(Loc.T("Common.Close"));
                page.Buttons.Add(byHand);
                page.Buttons.Add(close);
                page.DefaultButton = close;
                return page;
            }

            /// <summary>Onto the thread the dialog lives on, if there is still one to go to.</summary>
            void Back(Action what)
            {
                if (!ui.IsHandleCreated || ui.IsDisposed) return;
                try
                {
                    ui.BeginInvoke(new Action(() =>
                    {
                        try { what(); }
                        catch (Exception ex) { PetWindow.Log("update: " + ex.Message); }
                    }));
                }
                catch (InvalidOperationException) { }   // closing underneath us
            }
        }

        static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { PetWindow.Log("could not open " + url + ": " + ex.Message); }
        }
    }
}
