// StingTools — a command's result, as one line for a workflow report
//
// Inside a workflow preset a command must not stop the run with a modal result dialog
// (WorkflowEngine.IsRunningPreset); nobody is there to close it, and a preset that pops
// five dialogs is not unattended. What the dialog would have said goes to the STING log
// in full, and a short form goes back as the step's message, which the workflow report
// prints under the step. This makes the short form: the title, then the first lines of
// the body that say something, joined, whitespace collapsed, capped.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core
{
    public static class StepMessage
    {
        public const int DefaultMaxLines = 6;
        public const int DefaultMaxChars = 400;

        private static readonly Regex _ws = new Regex(@"\s+", RegexOptions.Compiled);
        // Rule lines and bare decoration ("════", "────", "---") carry nothing.
        private static readonly Regex _decoration = new Regex(@"^[\s═─━=\-_*·•.]*$", RegexOptions.Compiled);

        /// <summary>
        /// "Title: line; line; line …" from a dialog body. Blank and decorative lines are
        /// skipped, bullets trimmed; at most <paramref name="maxLines"/> lines and
        /// <paramref name="maxChars"/> characters (an ellipsis marks a cut).
        /// </summary>
        public static string Summarise(string title, string body,
            int maxLines = DefaultMaxLines, int maxChars = DefaultMaxChars)
        {
            var lines = new List<string>();
            foreach (var raw in (body ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (_decoration.IsMatch(raw)) continue;
                var l = _ws.Replace(raw, " ").Trim().TrimStart('•', '·', '*', '-', '✓', '✗', '⚠').Trim();
                if (l.Length == 0) continue;
                lines.Add(l);
            }
            bool more = lines.Count > maxLines;
            var head = string.Join("; ", lines.Take(Math.Max(0, maxLines)));
            var t = (title ?? "").Trim();
            string s = t.Length == 0 ? head : head.Length == 0 ? t : t + ": " + head;
            if (more) s += " …";
            if (maxChars > 1 && s.Length > maxChars) s = s.Substring(0, maxChars - 1).TrimEnd() + "…";
            return s;
        }
    }
}
