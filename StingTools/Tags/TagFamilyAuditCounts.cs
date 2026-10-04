// Revit-free counting for the Tag Family Audit.
//
// The audit used to print "N .rfa files exist on disk but only M loaded", where N
// counted files in one folder and M counted CATEGORIES covered — so a project that
// held every family of a 210-family library still read "only 121 loaded". Here the
// library is compared to the project family by family, across every library root.
//
// It also used to call YESNO storage of TAG_PARA_STATE_*_BOOL "legacy" and warn about
// it, while MR_PARAMETERS.txt declares those parameters YESNO. The storage is now
// judged against the type the shared parameter file declares.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Tags
{
    public static class TagFamilyAuditCounts
    {
        public sealed class LibraryGap
        {
            /// <summary>Distinct families in the library, legacy spellings folded into their canonical name.</summary>
            public int LibraryCount { get; set; }
            /// <summary>Library families present in the project.</summary>
            public int LoadedCount { get; set; }
            /// <summary>Library families absent from the project, sorted.</summary>
            public List<string> NotLoaded { get; } = new List<string>();
        }

        /// <summary>
        /// Which library families the project does not hold. A project family under a
        /// legacy name counts as its canonical family (Load renames it), and a library
        /// that still ships a legacy file beside the canonical one counts it once.
        /// </summary>
        public static LibraryGap Compare(IEnumerable<string> libraryFamilyNames, IEnumerable<string> projectFamilyNames)
        {
            var gap = new LibraryGap();
            var library = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in libraryFamilyNames ?? Enumerable.Empty<string>())
            {
                string canonical = TagFamilyNameAliases.Canonicalise(n);
                string key = TagFamilyNameAliases.MatchKey(canonical);
                if (key.Length > 0 && !library.ContainsKey(key)) library[key] = canonical.Trim();
            }

            var project = new HashSet<string>(
                (projectFamilyNames ?? Enumerable.Empty<string>())
                    .Select(n => TagFamilyNameAliases.MatchKey(TagFamilyNameAliases.Canonicalise(n)))
                    .Where(k => k.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            gap.LibraryCount = library.Count;
            foreach (var kv in library)
            {
                if (project.Contains(kv.Key)) gap.LoadedCount++;
                else gap.NotLoaded.Add(kv.Value);
            }
            gap.NotLoaded.Sort(StringComparer.OrdinalIgnoreCase);
            return gap;
        }

        /// <summary>
        /// The Revit storage a shared-parameter data type is held in, by the name of
        /// Revit's StorageType member: "Integer" for YESNO / INTEGER, "String" for TEXT.
        /// Null when the type is missing or not one of those.
        /// </summary>
        public static string StorageForDeclaredType(string declaredType)
        {
            switch ((declaredType ?? "").Trim().ToUpperInvariant())
            {
                case "YESNO":
                case "INTEGER": return "Integer";
                case "TEXT": return "String";
                default: return null;
            }
        }

        /// <summary>
        /// The data type a parameter is declared with in a shared parameter file's text
        /// (PARAM rows: PARAM, GUID, NAME, DATATYPE, ...). Null when the file does not
        /// declare it.
        /// </summary>
        public static string DeclaredType(IEnumerable<string> sharedParamFileLines, string paramName)
        {
            if (sharedParamFileLines == null || string.IsNullOrWhiteSpace(paramName)) return null;
            foreach (var line in sharedParamFileLines)
            {
                if (line == null || !line.StartsWith("PARAM\t", StringComparison.Ordinal)) continue;
                var parts = line.Split('\t');
                if (parts.Length > 3 && string.Equals(parts[2], paramName, StringComparison.Ordinal))
                    return parts[3].Trim();
            }
            return null;
        }
    }
}
