using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DeskMadeline
{
    /// <summary>
    /// Replacing this copy with the one the build server has: fetch it, unpack it, and hand the
    /// swap to something that is not itself being overwritten.
    /// </summary>
    /// <remarks>
    /// A running program on Windows cannot be written over -- its own exe and every assembly it
    /// has loaded are held open for as long as it lives. So the pet does everything that can be
    /// done while it is alive, which is the downloading and the unpacking, and then leaves a
    /// short script behind: wait for this process to go, copy the new build over the old one,
    /// start it again. The pet quits, the script does the two seconds of work nobody can do from
    /// inside, and the new copy comes up in the same folder.
    ///
    /// What the zip holds is what the build workflow gathered, which is the build and nothing
    /// else: settings.txt and the log are left out of it there, so an update never writes over
    /// what the user chose. And if the copy fails -- an install somewhere that needs an
    /// administrator, a file held by something else -- the script starts the old build back up
    /// rather than leaving the desktop empty, and says why in the log beside it.
    /// </remarks>
    internal static class SelfUpdate
    {
        /// <summary>Where the new build is put together, well away from the one in use.</summary>
        static string Work => Path.Combine(Path.GetTempPath(), "DeskMadeline-update");

        /// <summary>Whether an update could be carried out at all from where this is running.</summary>
        public static bool Possible => !string.IsNullOrEmpty(Environment.ProcessPath);

        /// <summary>How much of it has come down, for the bar and the line under it.</summary>
        internal readonly struct Fetched
        {
            public readonly long Done, Total;
            public Fetched(long done, long total) { Done = done; Total = total; }

            public int Percent => Total > 0 ? (int)(Done * 100L / Total) : 0;

            /// <summary>
            /// In kilobytes, which for a few megabytes over a home line is the number that
            /// actually moves, and the percentage the bar is showing. A server that will not say
            /// how big it is gets the one number, since neither of the others can be worked out.
            /// </summary>
            public override string ToString()
            {
                string done = Kb(Done);
                return Total > 0
                    ? string.Format(Loc.T("Update.Progress"), done, Kb(Total), Percent) : done;
            }

            static string Kb(long bytes) => (bytes / 1024L).ToString("N0",
                System.Globalization.CultureInfo.CurrentCulture) + " KB";
        }

        /// <summary>
        /// Fetch the build and unpack it, saying how far along it is, and answer with the folder
        /// the new build is in.
        /// </summary>
        public static async Task<string> Fetch(string url, IProgress<Fetched> progress,
            CancellationToken cancel)
        {
            Directory.CreateDirectory(Work);
            string zip = Path.Combine(Work, "build.zip");
            string unpacked = Path.Combine(Work, "build");
            if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true);

            using (HttpResponseMessage response = await UpdateCheck.Http.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0L;
                using Stream from = await response.Content.ReadAsStreamAsync(cancel);
                using FileStream to = File.Create(zip);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await from.ReadAsync(buffer, cancel)) > 0)
                {
                    await to.WriteAsync(buffer.AsMemory(0, read), cancel);
                    done += read;
                    progress.Report(new Fetched(done, total));
                }
                progress.Report(new Fetched(done, total > 0 ? total : done));
            }

            ZipFile.ExtractToDirectory(zip, unpacked);
            File.Delete(zip);
            // Whatever was in there, it was not a build of this.
            if (!File.Exists(Path.Combine(unpacked, "DeskMadeline.exe")))
                throw new InvalidOperationException(Loc.T("Update.NotABuild"));
            return unpacked;
        }

        /// <summary>
        /// Leave the swap to a script and let it wait for this process to end. Returns false if
        /// the script could not even be started, in which case nothing has been touched.
        /// </summary>
        public static bool Handover(string unpacked)
        {
            try
            {
                string script = Path.Combine(Work, "update.ps1");
                File.WriteAllText(script, Swap, new UTF8Encoding(false));
                string here = AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "
                        + Quote(script) + " " + Environment.ProcessId + " " + Quote(unpacked)
                        + " " + Quote(here) + " " + Quote(Environment.ProcessPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch (Exception ex)
            {
                PetWindow.Log("update handover failed: " + ex.Message);
                return false;
            }
        }

        static string Quote(string path) => "\"" + path + "\"";

        /// <summary>
        /// The script. Written out rather than kept beside the exe, since the exe's own folder
        /// is the one being overwritten.
        /// </summary>
        const string Swap = @"param([int]$Pet, [string]$From, [string]$To, [string]$Exe)
# Wait for the pet to let go of its own files. Ten seconds is far longer than it takes; if it
# is somehow still there after that, start it again rather than copying over a running program.
for ($i = 0; $i -lt 100; $i++) {
  if (-not (Get-Process -Id $Pet -ErrorAction SilentlyContinue)) { break }
  Start-Sleep -Milliseconds 100
}
if (Get-Process -Id $Pet -ErrorAction SilentlyContinue) {
  ""the pet is still running after ten seconds; nothing was replaced"" |
    Out-File (Join-Path $env:TEMP 'DeskMadeline-update.log')
  exit 1
}
# Windows can hold a file open for a moment after the process that had it is gone.
Start-Sleep -Milliseconds 400
try {
  Copy-Item -Path (Join-Path $From '*') -Destination $To -Recurse -Force -ErrorAction Stop
} catch {
  # Better an old pet than none: it is started again below either way, and this says why it is
  # still the old one -- most likely a folder that needs an administrator to write to.
  $_ | Out-File (Join-Path $env:TEMP 'DeskMadeline-update.log')
}
Start-Process -FilePath $Exe -WorkingDirectory $To
Remove-Item -LiteralPath $From -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
";
    }
}
