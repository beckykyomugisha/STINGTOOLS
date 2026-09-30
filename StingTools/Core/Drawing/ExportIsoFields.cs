// ExportIsoFields.cs — the ONE answer to "what suitability and revision does this exported
// sheet carry?", shared by the export file name, the document register row and the ACC upload.
//
// Why this exists. Before it, the Export Centre built the file name with a fallback of "S2"
// for a sheet with no suitability and "P01" for a sheet with no revision — invented values
// (91 of the 93 corporate drawing types also ship "S2"/"P01" as IsoNaming defaults, which is
// the same invention dressed as configuration). The register was then written by a different
// chain that fell back to S0, so one file said S2 in its name and S0 in the register, and an
// ACC upload of that file would have stamped a status nobody set.
//
// The rule now: a sheet's suitability is the code the sheet carries (STING_SUITABILITY_TXT,
// PRJ_DWG_SUITABILITY_COD_TXT or the STATUS cell — ExportCenterEngine.SheetSuitabilityCode);
// its revision is the Revit current revision, else the title-block revision parameter
// (ExportCenterEngine.GetCurrentRevision). Nothing else. When either is absent the value is
// an explicit NOT-SET MARKER, never a plausible code:
//
//   * Suitability -> "XX". The codebase's existing placeholder for an empty POSITIONAL ISO
//     field (the Drawing Types engine prints XX for one — SheetNumberEngine); suitability is
//     positional in the ISO 19650-2 name. XX is not an ISO 19650 suitability code, so
//     Iso19650Suitability.CdeStateFor("XX") is null and nothing can route by it.
//   * Revision    -> "NOREV". Deliberately not "XX": a revision is read by people as a
//     P01/C01-style label, and "NOREV" cannot be mistaken for one. It is also unambiguous
//     inside a file name, where "XX" can legitimately be the ISO "unknown level" code.
//
// A file carrying either marker is flagged on its export row and its register row
// (iso_unset), reported in the run's warnings, and REFUSED by the ACC upload paths.
//
// Revit-free and log-free: linked into StingTools.Acc.Tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>The resolved suitability / revision of one exported sheet.</summary>
    public sealed class ExportIsoFieldValues
    {
        /// <summary>The suitability code, or <see cref="ExportIsoFields.NotSetSuitability"/>.</summary>
        public string Suitability { get; set; } = ExportIsoFields.NotSetSuitability;
        /// <summary>The revision label, or <see cref="ExportIsoFields.NotSetRevision"/>.</summary>
        public string Revision { get; set; } = ExportIsoFields.NotSetRevision;
        public bool SuitabilitySet { get; set; }
        public bool RevisionSet { get; set; }
        /// <summary>CDE state the suitability puts the file in; null when it is not set.</summary>
        public string CdeState => SuitabilitySet ? Iso19650Suitability.CdeStateFor(Suitability) : null;
        /// <summary>True when both fields carry real values.</summary>
        public bool Complete => SuitabilitySet && RevisionSet;

        /// <summary>The field names that are NOT set ("suitability", "revision"); empty when complete.
        /// This is the list written to the register row's iso_unset.</summary>
        public List<string> Unset
        {
            get
            {
                var l = new List<string>();
                if (!SuitabilitySet) l.Add(ExportIsoFields.FieldSuitability);
                if (!RevisionSet) l.Add(ExportIsoFields.FieldRevision);
                return l;
            }
        }
    }

    public static class ExportIsoFields
    {
        /// <summary>Printed in place of a suitability the sheet does not carry.</summary>
        public const string NotSetSuitability = "XX";
        /// <summary>Printed in place of a revision the sheet does not carry.</summary>
        public const string NotSetRevision = "NOREV";

        public const string FieldSuitability = "suitability";
        public const string FieldRevision = "revision";

        /// <summary>
        /// Resolve from what the sheet itself carries. <paramref name="sheetSuitability"/> may
        /// be a raw cell ("S4 - FOR APPROVAL"); only a recognised ISO 19650 code counts as set.
        /// <paramref name="sheetRevision"/> is taken as given when non-blank — a revision label
        /// has no closed vocabulary — unless it IS one of the markers (a marker read back from
        /// an earlier run is still "not set").
        /// </summary>
        public static ExportIsoFieldValues Resolve(string sheetSuitability, string sheetRevision)
        {
            var v = new ExportIsoFieldValues();
            string code = Iso19650Suitability.ExtractCode(sheetSuitability ?? string.Empty);
            if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(Iso19650Suitability.CdeStateFor(code)))
            {
                v.Suitability = code;
                v.SuitabilitySet = true;
            }
            string rev = (sheetRevision ?? string.Empty).Trim();
            if (rev.Length > 0 && !IsNotSetMarker(rev))
            {
                v.Revision = rev;
                v.RevisionSet = true;
            }
            return v;
        }

        /// <summary>True for a blank value or either marker.</summary>
        public static bool IsNotSetMarker(string value)
        {
            string s = (value ?? string.Empty).Trim();
            return s.Length == 0
                || s.Equals(NotSetSuitability, StringComparison.OrdinalIgnoreCase)
                || s.Equals(NotSetRevision, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Does this file name carry a not-set marker? True when any name token is
        /// <see cref="NotSetRevision"/>, or when the last two tokens are the suitability and
        /// revision positions of the ISO 19650-2 name ("…-{Suitability}-{Revision}", the
        /// Export Centre default template) and the suitability token is
        /// <see cref="NotSetSuitability"/>. "XX" elsewhere in the name (the ISO unknown-level
        /// code) does not count. Names from a custom template that omit both fields are not
        /// detectable here — the register row's iso_unset flag covers those.
        /// </summary>
        public static bool NameCarriesNotSetMarker(string fileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrPath)) return false;
            string stem;
            try { stem = Path.GetFileNameWithoutExtension(fileNameOrPath.Trim()); }
            catch (ArgumentException) { stem = fileNameOrPath; }
            var tokens = stem.Split(new[] { '-', '_', ' ', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(t => t.Equals(NotSetRevision, StringComparison.OrdinalIgnoreCase))) return true;
            return tokens.Length >= 3 &&
                   tokens[tokens.Length - 2].Equals(NotSetSuitability, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>One line naming what is missing, for a warning or a refusal.</summary>
        public static string DescribeUnset(IEnumerable<string> unset)
        {
            var l = (unset ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (l.Count == 0) return string.Empty;
            return "no " + string.Join(" and no ", l);
        }
    }
}
