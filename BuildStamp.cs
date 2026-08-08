using System;
using System.Globalization;
using System.Reflection;

namespace DeskMadeline
{
    /// <summary>
    /// Which commit this build came from, and when that commit was made.
    /// </summary>
    /// <remarks>
    /// Put here by the StampCommit target in the csproj, which asks git at build time. A build
    /// from a tree with no git to ask -- a source zip, someone else's copy -- has neither, and
    /// everything that reads this has to cope with not knowing.
    /// </remarks>
    internal static class BuildStamp
    {
        /// <summary>The short hash, or empty when this did not come from a checkout.</summary>
        public static readonly string Commit = Metadata("CommitHash");

        /// <summary>When that commit was made, in the zone of whoever made it.</summary>
        public static readonly DateTimeOffset? Made = Parse(Metadata("CommitDate"));

        public static bool Known => Commit.Length > 0;

        /// <summary>
        /// A moment as the reader would write it: their zone, their date format. The zone a
        /// commit was made in belongs to somebody else and is not the interesting part of it.
        /// </summary>
        public static string Local(DateTimeOffset at)
            => at.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

        /// <summary>Hash and date together, as the About window shows them.</summary>
        public static string Describe(string commit, DateTimeOffset? made)
        {
            if (commit.Length == 0) return "";
            return made.HasValue ? commit + "  ·  " + Local(made.Value) : commit;
        }

        public static DateTimeOffset? Parse(string text)
            => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out DateTimeOffset at) ? at : (DateTimeOffset?)null;

        static string Metadata(string key)
        {
            foreach (AssemblyMetadataAttribute attribute in
                typeof(BuildStamp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                if (attribute.Key == key) return attribute.Value ?? "";
            return "";
        }
    }
}
