// SharedParamSubset - a Revit shared-parameter file holding only the named
// parameters, cut from MR_PARAMETERS.txt.
//
// WHY
//
// A tag label can only use a shared parameter that is added through Edit Label ->
// Add Parameter -> Select, one at a time, from Revit's shared-parameter browser.
// Against MR_PARAMETERS.txt that browser lists every group of ~3,000 parameters
// and resets to the first group on every reopen. Pointed at a file holding only
// the ten or so parameters one family needs, it shows a short list and the add
// cycle becomes mechanical - the method that made the universal master buildable
// (2026-07-04).
//
// The GUIDs, types and groups are copied, never rewritten, so a parameter added
// from the subset is the same parameter the project binds.
//
// Revit-free and unit-tested (StingTools.Tags.Tests).

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Tags
{
    public static class SharedParamSubset
    {
        /// <summary>The lines of a shared-parameter file with only these parameters.</summary>
        public sealed class Result
        {
            /// <summary>File lines, in source order. Write with LF endings, UTF-8, no BOM.</summary>
            public List<string> Lines { get; } = new List<string>();
            /// <summary>Requested names that the source file does not define.</summary>
            public List<string> Missing { get; } = new List<string>();
        }

        /// <summary>
        /// Cut <paramref name="names"/> out of <paramref name="source"/> (the lines of
        /// MR_PARAMETERS.txt). Keeps the header and META lines, the GROUP lines of the
        /// groups those parameters use, and their PARAM lines, all in source order.
        /// </summary>
        public static Result Build(IEnumerable<string> source, IEnumerable<string> names)
        {
            var result = new Result();
            var wanted = new List<string>((names ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct());
            var wantedSet = new HashSet<string>(wanted, StringComparer.Ordinal);

            var lines = (source ?? Enumerable.Empty<string>()).Select(l => (l ?? "").TrimEnd('\r')).ToList();

            // Pass 1: which parameters exist, and which groups they sit in.
            var found = new HashSet<string>(StringComparer.Ordinal);
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (string l in lines)
            {
                if (!l.StartsWith("PARAM\t", StringComparison.Ordinal)) continue;
                var f = l.Split('\t');
                // PARAM | GUID | NAME | DATATYPE | DATACATEGORY | GROUP | ...
                if (f.Length > 5 && wantedSet.Contains(f[2]))
                {
                    found.Add(f[2]);
                    groups.Add(f[5]);
                }
            }

            // Pass 2: copy what is needed, in order. Comment lines are kept only
            // above the first section header - MR_PARAMETERS.txt carries a version
            // history further down that has no place in a ten-parameter file.
            bool inBody = false;
            foreach (string l in lines)
            {
                if (l.StartsWith("*", StringComparison.Ordinal)) inBody = true;
                if (inBody && l.StartsWith("#", StringComparison.Ordinal)) continue;

                if (l.StartsWith("GROUP\t", StringComparison.Ordinal))
                {
                    var f = l.Split('\t');
                    if (f.Length > 1 && groups.Contains(f[1])) result.Lines.Add(l);
                }
                else if (l.StartsWith("PARAM\t", StringComparison.Ordinal))
                {
                    var f = l.Split('\t');
                    if (f.Length > 2 && wantedSet.Contains(f[2])) result.Lines.Add(l);
                }
                else if (l.StartsWith("#", StringComparison.Ordinal)
                         || l.StartsWith("*", StringComparison.Ordinal)
                         || l.StartsWith("META\t", StringComparison.Ordinal))
                {
                    result.Lines.Add(l);
                }
            }

            result.Missing.AddRange(wanted.Where(n => !found.Contains(n)));
            return result;
        }
    }
}
