using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeskMadeline
{
    /// <summary>
    /// Fetching the 64-bit FMOD a plain copy of Celeste has not got, out of Everest's release.
    /// </summary>
    /// <remarks>
    /// The game as it is sold is the 32-bit XNA build, whose FMOD a 64-bit pet cannot load, and
    /// nothing else on such a machine has a 64-bit one either -- see <see cref="FmodRuntime"/>.
    /// The two libraries that would fix that are the ones Everest installs, and Everest hangs
    /// them off its own GitHub release inside main.zip, a 71MB archive of which they are 1.2MB.
    ///
    /// So the archive is not downloaded. A zip's index is at its end, GitHub's release storage
    /// serves ranges, and an entry is a run of bytes at an offset the index gives: read the last
    /// pages for the index, read the two entries, inflate them, and that is the whole of it --
    /// about a megabyte and a quarter over three requests apiece. What comes out is checked
    /// against the CRC the index carries and then against being a 64-bit library at all before
    /// anything is written, since a wrong file here is the difference between no sound and the
    /// pet not starting.
    ///
    /// Nothing of FMOD's is redistributed by this project or kept in this repository; this is
    /// the user's machine fetching, when asked to, the same file their copy of Celeste would
    /// have had if it had been modded. It lands beside the pet, never inside the game's folder.
    /// </remarks>
    internal static class FmodDownload
    {
        const string LatestRelease =
            "https://api.github.com/repos/EverestAPI/Everest/releases/latest";
        /// <summary>Where to send someone when this cannot do it for them.</summary>
        public const string EverestPage = "https://everestapi.github.io/";

        /// <summary>The asset the libraries are in, and where they sit inside it.</summary>
        const string Archive = "main.zip";
        const string Inside = "everest-lib/lib64-win-x64/";

        /// <summary>What is wanted out of it: the core, then the studio library beside it.</summary>
        /// <remarks>
        /// Both names for the studio library, because both are in use -- newer lib sets ship
        /// FMOD's own fmodstudio64.dll and older ones renamed it. Whichever the archive has is
        /// the one taken, and it is written under the name it had.
        /// </remarks>
        static readonly string[] Core = { "fmod64.dll" };
        static readonly string[] Studio = { "fmodstudio64.dll", "fmodstudio.dll" };

        static readonly HttpClient Http = Client();

        static HttpClient Client()
        {
            // No overall timeout, as SelfUpdate has none: a slow line is not a failure. The
            // cancellation token is what ends this, and the dialog holds it.
            var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            client.DefaultRequestHeaders.Add("User-Agent", "DeskMadeline");
            return client;
        }

        /// <summary>Where the libraries go: the folder the pet's own FmodRuntime looks in.</summary>
        public static string Destination => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "lib64-win-x64");

        /// <summary>
        /// Whether this is worth offering: there is no runtime that can be loaded, and the pet
        /// is where it can write one. A bundled build that already carries FMOD is not offered
        /// anything, nor is one whose only trouble is that the banks are missing.
        /// </summary>
        public static bool Wanted
            => FmodRuntime.Locate(AppDomain.CurrentDomain.BaseDirectory,
                   CelesteInstall.Directory)?.Usable != true;

        /// <summary>
        /// Fetch the two libraries and put them in place. Throws on anything that stops that,
        /// having written nothing.
        /// </summary>
        public static async Task Fetch(IProgress<SelfUpdate.Fetched> progress,
            CancellationToken cancel)
        {
            string url = await ArchiveUrl(cancel);
            List<ZipEntry> index = await Index(url, cancel);
            ZipEntry core = Pick(index, Core), studio = Pick(index, Studio);
            long total = core.Compressed + studio.Compressed;

            // Both are read before either is written: half a runtime in place is worse than
            // none, since the pet would then find it and fail on the missing other half.
            var files = new List<KeyValuePair<string, byte[]>>();
            long done = 0;
            foreach (ZipEntry entry in new[] { core, studio })
            {
                byte[] bytes = await Read(url, entry, read =>
                    progress.Report(new SelfUpdate.Fetched(done + read, total)), cancel);
                done += entry.Compressed;
                progress.Report(new SelfUpdate.Fetched(done, total));
                if (!FmodRuntime.Is64BitImage(bytes))
                    throw new InvalidOperationException(entry.Name + " is not a 64-bit library");
                files.Add(new KeyValuePair<string, byte[]>(
                    Path.GetFileName(entry.Name.Replace('/', Path.DirectorySeparatorChar)), bytes));
            }

            Directory.CreateDirectory(Destination);
            foreach (var file in files)
            {
                // Beside the destination and then moved onto it: a half-written DLL is a file
                // the loader will happily try, and this way it is never one.
                string destination = Path.Combine(Destination, file.Key);
                string temporary = destination + ".part";
                File.WriteAllBytes(temporary, file.Value);
                File.Move(temporary, destination, true);
                PetWindow.Log("FMOD: wrote " + destination + " (" + file.Value.Length + " bytes)");
            }
        }

        /// <summary>Everest's newest release, and the archive hanging off it.</summary>
        static async Task<string> ArchiveUrl(CancellationToken cancel)
        {
            using HttpResponseMessage response = await Http.GetAsync(LatestRelease, cancel);
            response.EnsureSuccessStatusCode();
            using JsonDocument json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancel));
            if (json.RootElement.TryGetProperty("assets", out JsonElement assets) &&
                assets.ValueKind == JsonValueKind.Array)
                foreach (JsonElement asset in assets.EnumerateArray())
                    if (asset.TryGetProperty("name", out JsonElement name) &&
                        string.Equals(name.GetString(), Archive, StringComparison.OrdinalIgnoreCase) &&
                        asset.TryGetProperty("browser_download_url", out JsonElement url))
                        return url.GetString();
            throw new InvalidOperationException("Everest's release has no " + Archive);
        }

        /// <summary>One file in the archive, as its index describes it.</summary>
        readonly struct ZipEntry
        {
            public readonly string Name;
            public readonly ushort Method;
            public readonly long Compressed, Uncompressed, Header;
            public readonly uint Crc;

            public ZipEntry(string name, ushort method, long compressed, long uncompressed,
                long header, uint crc)
            {
                Name = name; Method = method; Compressed = compressed;
                Uncompressed = uncompressed; Header = header; Crc = crc;
            }
        }

        /// <summary>
        /// The archive's index, read from its end. Two requests at most: the last pages, which
        /// hold the end record and usually the whole index, and the index itself when it is
        /// bigger than that.
        /// </summary>
        static async Task<List<ZipEntry>> Index(string url, CancellationToken cancel)
        {
            long length = await Length(url, cancel);
            long tailAt = Math.Max(0, length - 128 * 1024);
            byte[] tail = await Range(url, tailAt, length - 1, null, cancel);

            int end = LastIndexOf(tail, 0x06054b50);
            if (end < 0) throw new InvalidOperationException(Archive + " has no zip index");
            int entries = BitConverter.ToUInt16(tail, end + 10);
            long size = BitConverter.ToUInt32(tail, end + 12);
            long at = BitConverter.ToUInt32(tail, end + 16);
            // Zip64, which this does not read. Everest's archive is far from needing it, and
            // guessing at an index that is not there would be worse than saying so.
            if (at == 0xFFFFFFFFL || size == 0xFFFFFFFFL || entries == 0xFFFF)
                throw new InvalidOperationException(Archive + " is a zip64 archive");

            byte[] directory = at >= tailAt
                ? Slice(tail, (int)(at - tailAt), (int)size)
                : await Range(url, at, at + size - 1, null, cancel);

            var index = new List<ZipEntry>(entries);
            int position = 0;
            for (int i = 0; i < entries && position + 46 <= directory.Length; i++)
            {
                if (BitConverter.ToUInt32(directory, position) != 0x02014b50) break;
                ushort method = BitConverter.ToUInt16(directory, position + 10);
                uint crc = BitConverter.ToUInt32(directory, position + 16);
                long compressed = BitConverter.ToUInt32(directory, position + 20);
                long uncompressed = BitConverter.ToUInt32(directory, position + 24);
                int nameLength = BitConverter.ToUInt16(directory, position + 28);
                int extraLength = BitConverter.ToUInt16(directory, position + 30);
                int commentLength = BitConverter.ToUInt16(directory, position + 32);
                long header = BitConverter.ToUInt32(directory, position + 42);
                string name = System.Text.Encoding.UTF8.GetString(
                    directory, position + 46, nameLength);
                index.Add(new ZipEntry(name, method, compressed, uncompressed, header, crc));
                position += 46 + nameLength + extraLength + commentLength;
            }
            return index;
        }

        /// <summary>The entry under the first of those names that the archive has.</summary>
        static ZipEntry Pick(List<ZipEntry> index, string[] names)
        {
            foreach (string name in names)
                foreach (ZipEntry entry in index)
                    if (entry.Name.EndsWith(Inside + name, StringComparison.OrdinalIgnoreCase))
                        return entry;
            throw new InvalidOperationException(
                Archive + " has no " + Inside + names[0] + "; Everest has moved it");
        }

        /// <summary>
        /// One entry, fetched and inflated, checked against the length and the CRC the index
        /// gave for it.
        /// </summary>
        static async Task<byte[]> Read(string url, ZipEntry entry, Action<long> progress,
            CancellationToken cancel)
        {
            // The local header repeats the name and carries an extra field of its own length,
            // so where the data starts is only knowable from the file itself.
            byte[] header = await Range(url, entry.Header, entry.Header + 29, null, cancel);
            if (BitConverter.ToUInt32(header, 0) != 0x04034b50)
                throw new InvalidOperationException(entry.Name + " is not where the index says");
            long at = entry.Header + 30 +
                BitConverter.ToUInt16(header, 26) + BitConverter.ToUInt16(header, 28);
            byte[] compressed = await Range(
                url, at, at + entry.Compressed - 1, progress, cancel);

            byte[] bytes;
            if (entry.Method == 0) bytes = compressed;
            else if (entry.Method == 8)
            {
                using var from = new DeflateStream(
                    new MemoryStream(compressed), CompressionMode.Decompress);
                using var to = new MemoryStream((int)entry.Uncompressed);
                await from.CopyToAsync(to, cancel);
                bytes = to.ToArray();
            }
            else throw new InvalidOperationException(
                entry.Name + " is compressed in a way this cannot read (" + entry.Method + ")");

            if (bytes.Length != entry.Uncompressed)
                throw new InvalidOperationException(entry.Name + " came out the wrong length");
            if (Crc32(bytes) != entry.Crc)
                throw new InvalidOperationException(entry.Name + " came out damaged");
            return bytes;
        }

        static async Task<long> Length(string url, CancellationToken cancel)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using HttpResponseMessage response = await Http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            long? length = response.Content.Headers.ContentLength;
            if (!length.HasValue || length.Value <= 0)
                throw new InvalidOperationException("the server will not say how big " +
                    Archive + " is, so it cannot be read a piece at a time");
            return length.Value;
        }

        /// <summary>
        /// A run of bytes out of the archive. A server that ignores the range and sends the
        /// whole file back is refused rather than indulged -- that is 71MB nobody asked for.
        /// </summary>
        static async Task<byte[]> Range(string url, long from, long to, Action<long> progress,
            CancellationToken cancel)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(from, to);
            using HttpResponseMessage response = await Http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
                throw new InvalidOperationException(
                    "the download does not serve ranges, so " + Archive + " cannot be read in part");

            int wanted = checked((int)(to - from + 1));
            var bytes = new byte[wanted];
            using Stream stream = await response.Content.ReadAsStreamAsync(cancel);
            int done = 0;
            while (done < wanted)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(done, wanted - done), cancel);
                if (read <= 0) throw new InvalidOperationException("the download stopped short");
                done += read;
                progress?.Invoke(done);
            }
            return bytes;
        }

        static byte[] Slice(byte[] bytes, int at, int length)
        {
            var slice = new byte[length];
            Buffer.BlockCopy(bytes, at, slice, 0, length);
            return slice;
        }

        /// <summary>The last place a four-byte signature appears, or -1.</summary>
        static int LastIndexOf(byte[] bytes, uint signature)
        {
            for (int i = bytes.Length - 4; i >= 0; i--)
                if (BitConverter.ToUInt32(bytes, i) == signature) return i;
            return -1;
        }

        /// <summary>
        /// Offer it, fetch it if it is taken up, and say how it went -- one dialog that turns
        /// into its next state, as the update's does. Modal; call it on the UI thread.
        /// </summary>
        /// <param name="restart">
        /// How to start the pet again, for the end where it worked: the runtime is read once,
        /// at startup, so a new one only really takes over at the next.
        /// </param>
        public static void Ask(Control ui, Action restart) => new Conversation(ui, restart).Run();

        sealed class Conversation
        {
            readonly Control ui;
            readonly Action restart;
            TaskDialogPage showing;
            CancellationTokenSource fetching;
            bool leaving;

            public Conversation(Control ui, Action restart)
            { this.ui = ui; this.restart = restart; }

            public void Run()
            {
                showing = Offer();
                TaskDialog.ShowDialog(showing);
                fetching?.Cancel();
                if (leaving) restart();
            }

            void Turn(TaskDialogPage next)
            {
                if (showing == null) return;
                showing.Navigate(next);
                showing = next;
            }

            TaskDialogPage Blank(string heading, TaskDialogIcon icon) => new TaskDialogPage
            {
                Caption = Loc.T("App.Title"),
                Heading = heading,
                Icon = icon,
                AllowCancel = true,
                SizeToContent = true
            };

            /// <summary>
            /// What is missing, and the three ways out of it: install Everest, which gives the
            /// game the libraries and much else besides; take the two libraries alone; or have
            /// her stay quiet, which costs nothing and can be changed whenever.
            /// </summary>
            /// <remarks>
            /// Three buttons rather than a yes and a no, because declining is a real answer
            /// here and not a failure to decide -- everything except sound works without any of
            /// this. So the last button says what it does rather than "cancel", and the text
            /// says where the offer will be waiting.
            /// </remarks>
            TaskDialogPage Offer()
            {
                TaskDialogPage page = Blank(Loc.T("Sfx.GetTitle"), TaskDialogIcon.Information);
                page.Text = Loc.T("Sfx.GetWhy");
                page.Footnote = new TaskDialogFootnote(Loc.T("Sfx.GetFrom"));
                var everest = new TaskDialogButton(Loc.T("Sfx.InstallEverest"));
                everest.Click += (_, _) => Open(EverestPage);
                var get = new TaskDialogButton(Loc.T("Update.Download"))
                { AllowCloseDialog = false };
                get.Click += (_, _) => Turn(Fetching());
                page.Buttons.Add(everest);
                page.Buttons.Add(get);
                page.Buttons.Add(new TaskDialogButton(Loc.T("Sfx.StaySilent")));
                page.DefaultButton = get;
                return page;
            }

            TaskDialogPage Fetching()
            {
                var bar = new TaskDialogProgressBar(TaskDialogProgressBarState.Normal);
                TaskDialogPage page = Blank(Loc.T("Sfx.Getting"), TaskDialogIcon.Information);
                page.ProgressBar = bar;
                page.Text = new SelfUpdate.Fetched(0, 0).ToString();
                page.Buttons.Add(new TaskDialogButton(Loc.T("Common.Cancel")));

                fetching = new CancellationTokenSource();
                CancellationToken cancel = fetching.Token;
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
                        await Fetch(progress, cancel);
                        cancel.ThrowIfCancellationRequested();
                        Back(() => Turn(Done()));
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        PetWindow.Log("FMOD download failed: " + ex.Message);
                        Back(() => Turn(Broke(ex.Message)));
                    }
                });
                page.Destroyed += (_, _) => { if (showing == page) showing = null; };
                return page;
            }

            /// <summary>It is in place, and the pet has to come up again to read it.</summary>
            TaskDialogPage Done()
            {
                TaskDialogPage page = Blank(Loc.T("Sfx.GetDone"), TaskDialogIcon.None);
                page.Text = Loc.T("Sfx.RestartToApply");
                page.Footnote = new TaskDialogFootnote(Destination);
                var now = new TaskDialogButton(Loc.T("Sfx.RestartNow"));
                now.Click += (_, _) => leaving = true;
                var later = new TaskDialogButton(Loc.T("Update.Later"));
                page.Buttons.Add(later);
                page.Buttons.Add(now);
                page.DefaultButton = now;
                return page;
            }

            /// <summary>It did not come off; Everest's own page is the way round it.</summary>
            TaskDialogPage Broke(string why)
            {
                TaskDialogPage page = Blank(Loc.T("Sfx.GetFailed"), TaskDialogIcon.Warning);
                page.Text = why;
                var everest = new TaskDialogButton(Loc.T("Sfx.OnEverest"));
                everest.Click += (_, _) => Open(EverestPage);
                var close = new TaskDialogButton(Loc.T("Common.Close"));
                page.Buttons.Add(everest);
                page.Buttons.Add(close);
                page.DefaultButton = close;
                return page;
            }

            void Back(Action what)
            {
                if (!ui.IsHandleCreated || ui.IsDisposed) return;
                try
                {
                    ui.BeginInvoke(new Action(() =>
                    {
                        try { what(); }
                        catch (Exception ex) { PetWindow.Log("FMOD download: " + ex.Message); }
                    }));
                }
                catch (InvalidOperationException) { }   // closing underneath us
            }
        }

        static void Open(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex) { PetWindow.Log("could not open " + url + ": " + ex.Message); }
        }

        static uint[] crcTable;

        /// <summary>Zip's own checksum, which is what the index records for each entry.</summary>
        static uint Crc32(byte[] bytes)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint i = 0; i < 256; i++)
                {
                    uint value = i;
                    for (int bit = 0; bit < 8; bit++)
                        value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                    table[i] = value;
                }
                crcTable = table;
            }
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
