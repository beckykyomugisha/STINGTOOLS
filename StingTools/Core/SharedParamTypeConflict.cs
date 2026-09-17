// ============================================================================
// SharedParamTypeConflict.cs — why a family refuses to load into a project.
//
// WHY THIS EXISTS
//
// On 2026-09-17 "Propagate Universal" ran for 17 minutes and reported
// "succeeded=0, failed=1". The whole of what it said about the reason was:
//
//     Error: LoadFamily back into project failed
//
// Document.LoadFamily returns false with no reason attached, so the command
// had nothing better to write down. The actual cause was already known from a
// different route — twelve shared parameters the family offers as Text that the
// project holds, under the SAME GUIDs, as Number / Currency / Length / Yes-No.
// Revit keys a shared parameter on its GUID, and refuses the load rather than
// silently pick one of the two types.
//
// A false return and a two-word message are indistinguishable from any other
// load failure, so the operator is left to re-derive the twelve names from
// Revit's modal dialog by hand, once per attempt.
//
// WHAT THIS DOES
//
// Compares what a family OFFERS against what a project HOLDS and names every
// parameter that will block the load, before the load is attempted. Both sides
// are passed in as plain records, so the comparison is Revit-free and provable
// without a host — the collectors that read a FamilyManager and a Document are
// the callers' job.
//
// Two kinds block a load, and they are opposites:
//
//   TypeMismatch   same GUID, different data type   (the observed twelve)
//   NameCollision  same name, different GUID        (two files minted their own)
//
// WHAT IT DELIBERATELY DOES NOT DO
//
// It never reports a conflict from missing information. A parameter whose data
// type could not be read comes back as Unknown and is skipped, because an
// invented conflict here would abort a propagation that would have worked. A
// parameter the project does not hold at all is not a conflict either — loading
// the family is exactly how the project acquires it.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core
{
    /// <summary>One shared parameter as one side declares it.</summary>
    public class SharedParamFacts
    {
        /// <summary>Parameter name as this side spells it.</summary>
        public string Name { get; set; }

        /// <summary>The GUID Revit identifies the parameter by. <see cref="Guid.Empty"/> when unknown.</summary>
        public Guid Guid { get; set; }

        /// <summary>
        /// The data type, as a <c>ForgeTypeId.TypeId</c> string
        /// (e.g. <c>autodesk.spec.aec:length-2.0.0</c>) or a plain
        /// Revit shared-parameter-file word (e.g. <c>LENGTH</c>). Null or
        /// empty means "could not be read", and is never reported as a conflict.
        /// </summary>
        public string DataType { get; set; }
    }

    /// <summary>What kind of clash Revit will refuse the load over.</summary>
    public enum SharedParamConflictKind
    {
        /// <summary>Same GUID on both sides, different data type.</summary>
        TypeMismatch,
        /// <summary>Same name on both sides, different GUID.</summary>
        NameCollision
    }

    /// <summary>One parameter that will block a family load, and both sides of it.</summary>
    public class SharedParamTypeConflict
    {
        public SharedParamConflictKind Kind { get; set; }
        public string FamilyName { get; set; }
        public string ProjectName { get; set; }
        public Guid FamilyGuid { get; set; }
        public Guid ProjectGuid { get; set; }
        public string FamilyDataType { get; set; }
        public string ProjectDataType { get; set; }

        /// <summary>One line naming the parameter and both sides of the disagreement.</summary>
        public string Describe()
        {
            if (Kind == SharedParamConflictKind.NameCollision)
                return $"{FamilyName} — same name, different GUID " +
                       $"(family {Short(FamilyGuid)}, project {Short(ProjectGuid)})";

            return $"{FamilyName} — family offers " +
                   $"{SharedParamConflictDetector.TypeLabel(FamilyDataType)}, project holds " +
                   $"{SharedParamConflictDetector.TypeLabel(ProjectDataType)} " +
                   $"(GUID {Short(FamilyGuid)})";
        }

        private static string Short(Guid g)
        {
            string s = g.ToString();
            return s.Length >= 8 ? s.Substring(0, 8) : s;
        }
    }

    /// <summary>
    /// Finds the shared parameters that will make <c>Document.LoadFamily</c>
    /// return false. Revit-free on purpose: every defect in this area has been a
    /// wrong comparison, not a failed read.
    /// </summary>
    public static class SharedParamConflictDetector
    {
        /// <summary>
        /// Every parameter that will block the load. Empty means the shared
        /// parameters are compatible — which is not a promise the load will
        /// succeed, only that it will not fail for this reason.
        /// </summary>
        public static List<SharedParamTypeConflict> Detect(
            IEnumerable<SharedParamFacts> familySide,
            IEnumerable<SharedParamFacts> projectSide)
        {
            var conflicts = new List<SharedParamTypeConflict>();
            if (familySide == null || projectSide == null) return conflicts;

            var project = projectSide.Where(p => p != null).ToList();

            // Last writer wins on a duplicated key either side. A document cannot
            // hold two SharedParameterElements with one GUID, so a duplicate here
            // means the caller's collector double-counted; picking one is closer to
            // the truth than throwing away the whole check.
            var byGuid = new Dictionary<Guid, SharedParamFacts>();
            foreach (var p in project.Where(p => p.Guid != Guid.Empty))
                byGuid[p.Guid] = p;

            var byName = new Dictionary<string, SharedParamFacts>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in project.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
                byName[p.Name.Trim()] = p;

            foreach (var fam in familySide.Where(f => f != null))
            {
                SharedParamFacts proj = null;

                if (fam.Guid != Guid.Empty && byGuid.TryGetValue(fam.Guid, out proj))
                {
                    // Unreadable on either side: skip. An invented conflict aborts a
                    // propagation that would have worked, which is worse than a load
                    // failure we at least see.
                    if (IsUnknown(fam.DataType) || IsUnknown(proj.DataType)) continue;

                    if (!SameType(fam.DataType, proj.DataType))
                    {
                        conflicts.Add(new SharedParamTypeConflict
                        {
                            Kind = SharedParamConflictKind.TypeMismatch,
                            FamilyName = fam.Name,
                            ProjectName = proj.Name,
                            FamilyGuid = fam.Guid,
                            ProjectGuid = proj.Guid,
                            FamilyDataType = fam.DataType,
                            ProjectDataType = proj.DataType
                        });
                    }
                    continue; // matched by GUID; a name check would double-report it
                }

                if (!string.IsNullOrWhiteSpace(fam.Name) &&
                    byName.TryGetValue(fam.Name.Trim(), out proj) &&
                    fam.Guid != Guid.Empty && proj.Guid != Guid.Empty &&
                    fam.Guid != proj.Guid)
                {
                    conflicts.Add(new SharedParamTypeConflict
                    {
                        Kind = SharedParamConflictKind.NameCollision,
                        FamilyName = fam.Name,
                        ProjectName = proj.Name,
                        FamilyGuid = fam.Guid,
                        ProjectGuid = proj.Guid,
                        FamilyDataType = fam.DataType,
                        ProjectDataType = proj.DataType
                    });
                }
            }

            // Stable, human order: the kind that is usually a data-file defect first,
            // then alphabetical, so two runs of the same fault read the same.
            return conflicts
                .OrderBy(c => c.Kind)
                .ThenBy(c => c.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// The conflicts as a message fit for a TaskDialog or a report cell.
        /// Returns null for an empty list, so a caller can use it as the whole
        /// "is there anything to say" test.
        /// </summary>
        public static string Describe(IList<SharedParamTypeConflict> conflicts, int maxLines = 12)
        {
            if (conflicts == null || conflicts.Count == 0) return null;
            if (maxLines < 1) maxLines = 1;

            var sb = new StringBuilder();
            sb.Append(conflicts.Count == 1
                ? "1 shared parameter blocks the load:"
                : $"{conflicts.Count} shared parameters block the load:");

            foreach (var c in conflicts.Take(maxLines))
                sb.Append("\n  • ").Append(c.Describe());

            int hidden = conflicts.Count - maxLines;
            if (hidden > 0) sb.Append($"\n  • … and {hidden} more");

            return sb.ToString();
        }

        /// <summary>
        /// A data type as a human would name it: <c>autodesk.spec.aec:length-2.0.0</c>
        /// reads as <c>length</c>, and a shared-parameter-file word passes through.
        /// Display only — comparison uses <see cref="SameType"/>.
        /// </summary>
        public static string TypeLabel(string dataType)
        {
            if (string.IsNullOrWhiteSpace(dataType)) return "(unknown)";
            string s = dataType.Trim();

            int colon = s.LastIndexOf(':');
            if (colon >= 0 && colon < s.Length - 1) s = s.Substring(colon + 1);

            // Strip the trailing schema version ("-2.0.0"), which is noise to a
            // reader and identical on both sides of every real conflict.
            int dash = s.LastIndexOf('-');
            if (dash > 0 && s.Substring(dash + 1).All(ch => char.IsDigit(ch) || ch == '.'))
                s = s.Substring(0, dash);

            // "spec.string" → "string": the redundant prefix some specs carry.
            if (s.StartsWith("spec.", StringComparison.OrdinalIgnoreCase) && s.Length > 5)
                s = s.Substring(5);

            return s.Length == 0 ? "(unknown)" : s;
        }

        /// <summary>
        /// Whether two data types are the same.
        ///
        /// The two sides are written in two vocabularies and both turn up here: a
        /// shared-parameter FILE says <c>YESNO</c>, a live parameter's
        /// <c>ForgeTypeId</c> says <c>autodesk.spec:spec.bool.yesNo-2.0.0</c>. A
        /// string compare of those two reports a conflict that does not exist, and
        /// an invented conflict aborts a propagation that would have worked. So
        /// both are reduced to one canonical token first.
        ///
        /// It equates spellings of the same spec and nothing else —
        /// <c>number</c> and <c>integer</c> stay different, as Revit treats them.
        /// </summary>
        public static bool SameType(string a, string b)
        {
            if (IsUnknown(a) || IsUnknown(b)) return true; // nothing to disagree about
            if (string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(Canon(a), Canon(b), StringComparison.Ordinal);
        }

        // Shared-parameter-file word ↔ ForgeTypeId spec, for the specs STING
        // actually ships. Keyed on the stripped form of BOTH vocabularies, so a
        // new entry only ever needs adding on one side.
        private static readonly Dictionary<string, string> TypeAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "text",        "text" },
            { "string",      "text" },
            { "stringtext",  "text" },
            { "multilinetext", "multilinetext" },
            { "stringmultiline", "multilinetext" },
            { "url",         "url" },
            { "stringurl",   "url" },
            { "yesno",       "yesno" },
            { "boolyesno",   "yesno" },
            { "integer",     "integer" },
            { "intinteger",  "integer" },
            { "number",      "number" },
            { "currency",    "currency" },
            { "length",      "length" },
            { "area",        "area" },
            { "volume",      "volume" },
            { "angle",       "angle" },
        };

        /// <summary>
        /// One token per spec, whichever vocabulary named it. Unrecognised specs
        /// reduce to their stripped label rather than to a shared "unknown"
        /// bucket — two genuinely different specs must not become equal by both
        /// being unfamiliar.
        /// </summary>
        private static string Canon(string dataType)
        {
            string label = TypeLabel(dataType);
            var stripped = new string(label.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return TypeAliases.TryGetValue(stripped, out string canon) ? canon : stripped;
        }

        private static bool IsUnknown(string dataType) => string.IsNullOrWhiteSpace(dataType);
    }
}
