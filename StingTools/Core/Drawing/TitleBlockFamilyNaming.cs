// StingTools — Drawing Template Manager · T-6
//
// Revit-free half of TitleBlockResolver: (declared name, paper, orientation,
// BIM mode, the concrete family catalogue) -> concrete family + warnings.
//
// What it replaced, and why each was wrong:
//   * any STING_TB_SHEET*PRESENT* name returned "STING_TB_PRESENT_A1_v1.0"
//     whatever the paper — an A3 presentation profile got an A1 sheet;
//   * a blank paperSize silently became "A1";
//   * A2 / A4 returned null, so the dangling logical name went back to the
//     producer, which then fell back to "first title block in the project" —
//     although STING_TITLE_BLOCKS.json declares the A2 families.
// The supported set is now read from the catalogue, not hard-coded, and
// every guess is either refused or reported.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public sealed class TitleBlockResolution
    {
        /// <summary>Concrete family name, or null when it could not be
        /// resolved (see <see cref="Warnings"/>).</summary>
        public string Family { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public bool IsResolved => !string.IsNullOrEmpty(Family);
    }

    public static class TitleBlockFamilyNaming
    {
        public static readonly string[] IsoSizes = { "A0", "A1", "A2", "A3", "A4" };

        /// <param name="declared">DrawingType.titleBlockFamily (may be logical or blank).</param>
        /// <param name="paper">DrawingType.paperSize.</param>
        /// <param name="orientation">"Landscape" / "Portrait".</param>
        /// <param name="mode">"BIM" / "NONBIM".</param>
        /// <param name="concreteFamilies">Non-abstract ids from STING_TITLE_BLOCKS.json.
        /// Empty = catalogue unavailable; derivation still runs but is flagged.</param>
        /// <param name="isLoaded">Optional: is a family of this name loaded in the project.</param>
        public static TitleBlockResolution Resolve(
            string declared, string paper, string orientation, string mode,
            ICollection<string> concreteFamilies, Func<string, bool> isLoaded = null)
        {
            var r = new TitleBlockResolution();
            declared = (declared ?? "").Trim();
            var catalogue = concreteFamilies ?? Array.Empty<string>();
            bool haveCatalogue = catalogue.Count > 0;
            bool Exists(string n) => !haveCatalogue
                || catalogue.Contains(n)
                || catalogue.Any(c => string.Equals(c, n, StringComparison.OrdinalIgnoreCase));

            if (declared.Length > 0
                && ((haveCatalogue && Exists(declared)) || (isLoaded?.Invoke(declared) ?? false)))
            {
                r.Family = declared;
                return r;
            }

            if (declared.StartsWith("STING_TB_ASSEMBLY_", StringComparison.OrdinalIgnoreCase))
            {
                var fam = EnsureVersionSuffix(declared);
                if (!Exists(fam))
                {
                    r.Warnings.Add($"Title block '{declared}' maps to '{fam}', which STING_TITLE_BLOCKS.json does not declare.");
                    return r;
                }
                r.Family = fam;
                if (!haveCatalogue) r.Warnings.Add(CatalogueMissing(fam));
                return r;
            }

            bool isSheetName = declared.StartsWith("STING_TB_SHEET", StringComparison.OrdinalIgnoreCase);
            if (!isSheetName && declared.Length > 0)
            {
                // Healthcare "STING - …" / legacy real families: not ours to rewrite.
                r.Family = declared;
                return r;
            }

            bool presentation = declared.IndexOf("PRESENT", StringComparison.OrdinalIgnoreCase) >= 0;
            string label = declared.Length > 0 ? $"'{declared}'" : "blank title-block family";

            string nameSize  = ExtractSizeCode(declared);
            string paperRaw  = (paper ?? "").Trim();
            string paperSize = ExtractSizeCode(paperRaw);
            if (paperRaw.Length > 0 && paperSize == null)
                r.Warnings.Add($"paperSize '{paperRaw}' is not an ISO A-size ({string.Join("/", IsoSizes)}).");
            if (nameSize != null && paperSize != null && nameSize != paperSize)
                r.Warnings.Add($"{label} names {nameSize} but paperSize is {paperSize}; using {nameSize} from the family name.");

            string size = nameSize ?? paperSize;
            if (size == null)
            {
                r.Warnings.Add(paperRaw.Length == 0
                    ? $"{label} needs a paper size and paperSize is blank — no title block chosen (was silently A1)."
                    : $"{label}: cannot choose a title block for paper '{paperRaw}'.");
                return r;
            }

            bool portrait = string.Equals((orientation ?? "").Trim(), "Portrait", StringComparison.OrdinalIgnoreCase);
            string m = string.Equals((mode ?? "").Trim(), "NONBIM", StringComparison.OrdinalIgnoreCase) ? "NONBIM" : "BIM";
            string sheetFam = $"STING_TB_{size}{(portrait ? "_PORT" : "")}_{m}_v2.0";

            if (presentation)
            {
                string presFam = $"STING_TB_PRESENT_{size}_v1.0";
                if (Exists(presFam))
                {
                    r.Family = presFam;
                    if (portrait)
                        r.Warnings.Add($"{label}: presentation family '{presFam}' has no portrait variant; orientation 'Portrait' is not honoured.");
                }
                else if (Exists(sheetFam))
                {
                    r.Family = sheetFam;
                    r.Warnings.Add($"{label}: no presentation title block exists at {size}; using working sheet '{sheetFam}' so the paper size is kept.");
                }
                else
                {
                    r.Warnings.Add($"{label}: neither a presentation nor a working title block exists at {size} ({(portrait ? "portrait" : "landscape")}).");
                    return r;
                }
                if (!haveCatalogue) r.Warnings.Add(CatalogueMissing(r.Family));
                return r;
            }

            if (!Exists(sheetFam))
            {
                var supported = IsoSizes.Where(s => Exists($"STING_TB_{s}{(portrait ? "_PORT" : "")}_{m}_v2.0"));
                r.Warnings.Add($"{label}: no STING title block for {size} {(portrait ? "portrait" : "landscape")} " +
                               $"({sheetFam} is not in STING_TITLE_BLOCKS.json). Supported: {string.Join(", ", supported)}.");
                return r;
            }
            r.Family = sheetFam;
            if (!haveCatalogue) r.Warnings.Add(CatalogueMissing(sheetFam));
            return r;
        }

        private static string CatalogueMissing(string fam) =>
            $"STING_TITLE_BLOCKS.json could not be read; '{fam}' was derived by naming convention and not verified.";

        /// <summary>"STING_TB_SHEET_A1" / "A3" / "ISO A2" → "A1" / "A3" / "A2"; null when none.</summary>
        public static string ExtractSizeCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            foreach (var code in IsoSizes)
            {
                var idx = s.IndexOf(code, StringComparison.OrdinalIgnoreCase);
                while (idx >= 0)
                {
                    bool leftOk  = idx == 0 || !char.IsLetterOrDigit(s[idx - 1]);
                    bool rightOk = idx + code.Length == s.Length || !char.IsLetterOrDigit(s[idx + code.Length]);
                    if (leftOk && rightOk) return code;
                    idx = s.IndexOf(code, idx + 1, StringComparison.OrdinalIgnoreCase);
                }
            }
            return null;
        }

        /// <summary>Fab logical names gain the _v1.0 suffix unless versioned.</summary>
        public static string EnsureVersionSuffix(string name)
        {
            if (Regex.IsMatch(name ?? "", @"_v\d+\.\d+$", RegexOptions.IgnoreCase)) return name;
            return name + "_v1.0";
        }
    }
}
