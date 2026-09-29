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
            /// <summary>Its build number, or 0 when the notes do not say -- older ones do not.</summary>
            public readonly int Number;
            /// <summary>The release's own page.</summary>
            public readonly string Page;
            /// <summary>The file itself, which is what the download button opens.</summary>
            public readonly string Download;
            public readonly string FileName;
            public readonly long Bytes;
            /// <summary>Why there is no answer, or null.</summary>
            public readonly string Error;

            Result(string commit, DateTimeOffset? made, string page, string download,
                string fileName, long bytes, string error, int number = 0)
            {
                Commit = commit; Made = made; Page = page; Number = number;
                Download = download; FileName = fileName; Bytes = bytes; Error = error;
            }

            public static Result Found(string commit, DateTimeOffset? made, string page,
                string download = "", string fileName = "", long bytes = 0, int number = 0)
                => new Result(commit, made, page, download, fileName, bytes, null, number);
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

        static readonly HttpClient Http = Client();

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

                // Only shown, never compared: the hash and date above decide what is newer.
                int number = BuildStamp.ParseNumber(Labelled(body, "number"));
                return Result.Found(commit, made, Text(release, "html_url"),
                    download, fileName, bytes, number);
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
        /// way any modal window does; call it on the UI thread.
        /// </summary>
        /// <remarks>
        /// One window rather than two. It opens saying that it is asking, and turns into the
        /// answer where it stands -- a window that appears only once a request over the network
        /// has come back leaves the click looking like it did nothing, and for as long as the
        /// server takes to answer there is nothing on screen to cancel either.
        /// </remarks>
        /// <param name="quit">
        /// How to close the pet, for the one ending where it has to: a build cannot write over
        /// itself, so the last thing an update does here is leave and let the new one start.
        /// </param>
        public static void Ask(Control ui, Action quit)
        {
            bool leaving;
            using (var window = new UpdateWindow())
            {
                window.ShowDialog(ui);
                leaving = window.Leaving;
            }
            // Only once the window is off the screen: closing the pet underneath it while it is
            // still up is not something to ask of either of them.
            if (leaving) quit();
        }

        /// <summary>
        /// The window, as one thing that changes rather than a series of them: asking, then the
        /// answer, then -- if the answer is taken up -- the fetching, and then the pet is gone
        /// and the new one is starting.
        /// </summary>
        sealed class UpdateWindow : StatusWindow
        {
            CancellationTokenSource fetching;
            bool leaving;

            public UpdateWindow() : base(Loc.T("Update.Title"))
            {
                ShowState(null, P.AccentInk, Loc.T("Update.Title"), Loc.T("Update.Checking"), null,
                    b => b.Add(Loc.T("Common.Cancel"), Close));
            }

            /// <summary>Whether this ended by handing over to a new build, which means quitting.</summary>
            public bool Leaving => leaving;

            // Asked once it is up, so the click has something to show for it straight away;
            // answered back on this thread. Gone means cancelled while the server thought.
            protected override void Started() => Task.Run(async () =>
            {
                Result result = await Newest();
                Back(() => Answer(result));
            });

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                fetching?.Cancel();
                base.OnFormClosed(e);
            }

            static string Yours => BuildStamp.Known
                ? BuildStamp.Describe(BuildStamp.Commit, BuildStamp.Made, BuildStamp.Number)
                : Loc.T("Update.Unknown");

            /// <summary>What came of asking.</summary>
            void Answer(Result result)
            {
                if (result.Error != null)
                {
                    // Nothing here can say what went wrong on GitHub's side, so the way to find
                    // out is offered instead of guessed at.
                    ShowState("", Warn, Loc.T("Update.Failed"), result.Error, null, b =>
                    {
                        b.Add(Loc.T("Update.Manually"), () => Open(ReleasePage));
                        b.Add(Loc.T("Common.Close"), Close, accent: true);
                    });
                    return;
                }

                if (!result.Newer)
                {
                    ShowState("", P.AccentInk, Loc.T("Update.Current"), null, s =>
                    {
                        var card = s.Add(new FlyoutCard(P));
                        card.Add(new FlyoutInfoRow(P, Loc.T("Update.YoursLabel"), Yours, accent: false));
                    }, b => b.Add(Loc.T("Common.Ok"), Close, accent: true));
                    return;
                }

                ShowState("", P.AccentInk, Loc.T("Update.Available"), null, s =>
                {
                    var card = s.Add(new FlyoutCard(P));
                    card.Add(new FlyoutInfoRow(P, Loc.T("Update.NewestLabel"),
                        BuildStamp.Describe(result.Short, result.Made, result.Number), accent: true));
                    card.Add(new FlyoutInfoRow(P, Loc.T("Update.YoursLabel"), Yours, accent: false));
                    s.Add(new FlyoutNote(P, result.Describe()));
                }, b =>
                {
                    // Only where there is a file to fetch and somewhere to put it.
                    bool install = result.Download.Length > 0 && SelfUpdate.Possible;
                    b.Add(Loc.T("Update.OnGitHub"), () => Open(result.Page), accent: !install);
                    if (install) b.Add(Loc.T("Update.Install"), () => Fetching(result), accent: true);
                });
            }

            /// <summary>Fetching it, and then leaving so that it can take this one's place.</summary>
            void Fetching(Result result)
            {
                FlyoutProgress bar = null;
                ShowState("", P.AccentInk, Loc.T("Update.Downloading"),
                    new SelfUpdate.Fetched(0, result.Bytes).ToString(), s =>
                    {
                        bar = s.Add(new FlyoutProgress(P));
                        s.Add(new FlyoutNote(P, result.Describe()));
                    }, b => b.Add(Loc.T("Common.Cancel"), Close));

                fetching = new CancellationTokenSource();
                CancellationToken cancel = fetching.Token;
                // Progress<T> was made here, so it comes back here to be shown.
                var progress = new Progress<SelfUpdate.Fetched>(fetched =>
                {
                    if (IsDisposed || bar.IsDisposed) return;
                    bar.SetPercent(fetched.Percent);
                    Status.SetDetail(fetched.ToString());
                });

                Task.Run(async () =>
                {
                    try
                    {
                        string unpacked = await SelfUpdate.Fetch(result.Download, progress, cancel);
                        cancel.ThrowIfCancellationRequested();
                        Back(() =>
                        {
                            if (!SelfUpdate.Handover(unpacked))
                            { Broke(Loc.T("Update.HandoverFailed"), result); return; }
                            // The script is waiting for this process to end, so end it: close
                            // the window, and Ask does the rest once it is off the screen.
                            leaving = true;
                            Close();
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Back(() => Broke(ex.Message, result)); }
                });
            }

            /// <summary>The fetch did not come off; the page it is on is still there to be had.</summary>
            void Broke(string why, Result result) =>
                ShowState("", Warn, Loc.T("Update.DownloadFailed"), why, null, b =>
                {
                    b.Add(Loc.T("Update.OnGitHub"), () => Open(result.Page));
                    b.Add(Loc.T("Common.Close"), Close, accent: true);
                });
        }

        static void Open(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
            catch (Exception ex) { PetWindow.Log("could not open " + url + ": " + ex.Message); }
        }
    }
}
