using System;
using DeskMadeline;

// Is the build hanging off the rolling release one this copy does not have?
//
// The comparison and the reading of the release notes, not the request: what comes back from
// GitHub is made up here so that the awkward answers can be asked for. The awkward one is a
// build made from work that has not been pushed -- its commit is not the server's, and it is
// not behind it either.
static class UpdateChecks
{
    static int failed;

    static void Check(string what, bool ok)
    {
        Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what}");
        if (!ok) failed++;
    }

    const string Mine = "5599ab7";
    const string Theirs = "c0ffee1234567890abcdef1234567890abcdef12";
    static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-08-08T12:00:00+00:00");

    static UpdateCheck.Result Server(string commit, DateTimeOffset? made)
        => UpdateCheck.Result.Found(commit, made, "https://example.invalid/runs/1");

    /// <summary>Notes shaped like the ones the build workflow writes.</summary>
    static string Notes(string commit, string committed) => string.Join("\r\n", new[]
    {
        "The build of master as it stands. Unzip it anywhere and run DeskMadeline.exe.",
        "",
        "commit: " + commit,
        "committed: " + committed,
        "",
    });

    public static int Run()
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 74));
        Console.WriteLine("UPDATES: whether the build server has one this does not");
        Console.WriteLine(new string('=', 74));

        Console.WriteLine("  Against a build that says which commit it came from");
        Check("the same commit is not an update",
            !Server(Mine + "0123456789abcdef0123456789abcdef0", Noon)
                .NewerThan(Mine, Noon.AddDays(-1)));
        Check("a different commit, made later, is",
            Server(Theirs, Noon).NewerThan(Mine, Noon.AddMinutes(-1)));
        // The one that matters here rather than in the game: building from unpushed work.
        Check("a different commit, made earlier, is not -- this one is ahead of the server",
            !Server(Theirs, Noon.AddHours(-2)).NewerThan(Mine, Noon));
        Check("nor is one made at the very same moment",
            !Server(Theirs, Noon).NewerThan(Mine, Noon));

        Console.WriteLine();
        Console.WriteLine("  When one side or the other cannot say");
        Check("a build with no commit in it is offered the server's",
            Server(Theirs, Noon).NewerThan("", null));
        Check("and with no dates to compare, a different commit is taken as newer",
            Server(Theirs, null).NewerThan(Mine, Noon));

        Console.WriteLine();
        Console.WriteLine("  When there is no answer");
        Check("nothing came back, so nothing is offered",
            !UpdateCheck.Result.Failed("no route to host").NewerThan(Mine, Noon));
        Check("and the failure is kept to be shown",
            UpdateCheck.Result.Failed("no route to host").Error == "no route to host");
        Check("a run with no commit on it is not an update",
            !Server("", Noon).NewerThan(Mine, Noon.AddYears(-1)));

        // What the dialog puts on its two lines.
        Console.WriteLine();
        Console.WriteLine("  How a build is written down");
        Check("seven of the hash, as everywhere else", Server(Theirs, Noon).Short == "c0ffee1");
        Check("a short hash is left as it is", Server("abc", Noon).Short == "abc");
        Check("and with no date it is the hash alone",
            BuildStamp.Describe("c0ffee1", null) == "c0ffee1");
        Check("with one, both", BuildStamp.Describe("c0ffee1", Noon).StartsWith("c0ffee1  ·  "));

        // The notes the build workflow writes, read back. Two labelled lines in among prose
        // that anybody might edit, so what matters is finding them and not the rest.
        Console.WriteLine();
        Console.WriteLine("  Reading which build a release is");
        string notes = Notes(Theirs, "2026-08-08T12:00:00+00:00");
        Check("the commit comes back out", Labelled(notes, "commit") == Theirs);
        Check("and the date, as the moment it says",
            BuildStamp.Parse(Labelled(notes, "committed")) == Noon);
        Check("a line that is not there is not invented", Labelled(notes, "author") == "");
        Check("and neither is one from notes somebody emptied", Labelled("", "commit") == "");

        Console.WriteLine();
        Console.WriteLine("  What the offer says it is handing over");
        Check("the file and its size",
            UpdateCheck.Result.Found(Theirs, Noon, "p", "u", "DeskMadeline-c0ffee1.zip",
                3_500_000).Describe() == "DeskMadeline-c0ffee1.zip  ·  3.3 MB");
        Check("the file alone when its size is unknown",
            UpdateCheck.Result.Found(Theirs, Noon, "p", "u", "DeskMadeline-c0ffee1.zip")
                .Describe() == "DeskMadeline-c0ffee1.zip");
        Check("and where to find it when the release has no file on it",
            UpdateCheck.Result.Found(Theirs, Noon, "p").Describe() == Loc.T("Update.OnThePage"));

        Console.WriteLine();
        Console.WriteLine("  What the download says as it comes down");
        Check("kilobytes, out of how many, and the percentage between them",
            new SelfUpdate.Fetched(524_288, 3_670_016).ToString() == "512 KB of 3,584 KB  ·  14%");
        Check("nothing yet is nothing of the whole",
            new SelfUpdate.Fetched(0, 3_670_016).ToString() == "0 KB of 3,584 KB  ·  0%");
        Check("and all of it is all of it",
            new SelfUpdate.Fetched(3_670_016, 3_670_016).ToString() == "3,584 KB of 3,584 KB  ·  100%");
        // A server that will not say how big the file is leaves nothing to be a fraction of.
        Check("a length nobody gave is left out rather than guessed",
            new SelfUpdate.Fetched(524_288, 0).ToString() == "512 KB");
        Check("and the bar has nowhere to be", new SelfUpdate.Fetched(524_288, 0).Percent == 0);

        return failed;
    }

    /// <summary>UpdateCheck's own reader, reached the way the checks reach any private part.</summary>
    static string Labelled(string body, string name)
        => (string)typeof(UpdateCheck).GetMethod("Labelled",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Invoke(null, new object[] { body, name });
}
