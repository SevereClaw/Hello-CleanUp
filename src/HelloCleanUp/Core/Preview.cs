using System.Collections.Generic;
using System.Text;

namespace HelloCleanUp.Core
{
    /// <summary>The "what will change" text shown before applying — in the window and in /dry-run.</summary>
    public static class Preview
    {
        /// <summary>Changes grouped by tweak; empty string when everything selected is already in place.</summary>
        public static string ForTweaks(IEnumerable<Tweak> tweaks, out int changeCount)
        {
            changeCount = 0;
            var sb = new StringBuilder();
            foreach (var t in tweaks)
            {
                var lines = t.PlannedChanges();
                if (lines.Count == 0) continue;
                changeCount += lines.Count;
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine(t.Title + "  [" + t.Id + "]");
                foreach (var l in lines) sb.AppendLine("    " + l);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
