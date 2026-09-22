// Bs8666Label - composes the reinforcement annotation a steel fixer reads.
//
// Revit-free so it can be tested without Revit; TagConfig.Tag7 supplies the
// parameter values and pastes the result into TAG7 section A.
//
// WHY
//
// BS 8666 / BS 1192 detailing annotates bars as
//
//     21H16-03-150
//     |  |   |   |
//     |  |   |   +-- spacing, mm centres
//     |  |   +------ bar mark
//     |  +---------- type letter and size, e.g. H16
//     +------------- number of bars
//
// STING's reinforcement tags carried ASS_TAG_1_TXT - the ISO 19650 asset code -
// as their only always-visible label. That identifies the asset and tells a
// steel fixer nothing. The bar mark existed as STR_BAR_MARK_TXT and shipped at
// Tier 7; the count did not exist at all until 2026-09-21.
//
// This composes the readable sentence form rather than the terse code, because
// TAG7 section A is narrative prose ("... sized at 16 mm ..."), not a label
// grid. The terse form belongs in the bending schedule, which carries all
// sixteen BS 8666 columns.
//
// EVERY PART IS OPTIONAL. A model part-way through detailing has a mark and no
// count, or a count and no spacing. Each piece is emitted only when present, so
// a partial answer reads as a partial answer instead of "21H16--" - which would
// look like data and be a lie about what is modelled.

using System;
using System.Collections.Generic;
using System.Text;

namespace StingTools.Core
{
    /// <summary>Plain and marked-up forms of one reinforcement annotation.</summary>
    public class Bs8666Text
    {
        /// <summary>Prose with no markup, for ASS_TAG_7*_TXT.</summary>
        public string Plain { get; set; } = "";
        /// <summary>The same text with the TAG7 label/value markers.</summary>
        public string Marked { get; set; } = "";
        /// <summary>False when nothing was supplied, so the caller appends nothing.</summary>
        public bool HasContent => Plain.Length > 0;
    }

    /// <summary>Builds the BS 8666 reinforcement annotation from parameter values.</summary>
    public static class Bs8666Label
    {
        private const string L0 = "«L»", L1 = "«/L»";
        private const string V0 = "«V»", V1 = "«/V»";

        /// <summary>
        /// Composes " reinforced as bar mark 03, 21 no. H16 at 150 mm centres",
        /// omitting any part that was not supplied. Leading space included so the
        /// caller can append directly, matching the other section-A fragments.
        /// </summary>
        public static Bs8666Text Compose(string barMark, string totalNo, string typeAndSize, string spacingMm)
        {
            var result = new Bs8666Text();

            barMark      = Clean(barMark);
            totalNo      = Clean(totalNo);
            typeAndSize  = Clean(typeAndSize);
            spacingMm    = Clean(spacingMm);

            if (barMark.Length == 0 && totalNo.Length == 0 &&
                typeAndSize.Length == 0 && spacingMm.Length == 0)
                return result;

            var plain = new StringBuilder();
            var marked = new StringBuilder();

            void Part(string label, string value, string trailing = "")
            {
                if (value.Length == 0) return;
                if (plain.Length > 0) { plain.Append(", "); marked.Append(", "); }
                if (label.Length > 0)
                {
                    plain.Append(label).Append(' ');
                    marked.Append(L0).Append(label).Append(L1).Append(' ');
                }
                plain.Append(value).Append(trailing);
                marked.Append(V0).Append(value).Append(V1).Append(trailing);
            }

            Part("bar mark", barMark);

            // Count and size read as one phrase - "21 no. H16" - so they are
            // joined rather than emitted as two comma-separated parts.
            if (totalNo.Length > 0 || typeAndSize.Length > 0)
            {
                if (plain.Length > 0) { plain.Append(", "); marked.Append(", "); }
                var qty = new StringBuilder();
                if (totalNo.Length > 0) qty.Append(totalNo).Append(" no.");
                if (typeAndSize.Length > 0)
                {
                    if (qty.Length > 0) qty.Append(' ');
                    qty.Append(typeAndSize);
                }
                plain.Append(qty);
                marked.Append(V0).Append(qty).Append(V1);
            }

            if (spacingMm.Length > 0)
            {
                if (plain.Length > 0) { plain.Append(' '); marked.Append(' '); }
                plain.Append("at ").Append(spacingMm).Append(" mm centres");
                marked.Append(L0).Append("at").Append(L1).Append(' ')
                      .Append(V0).Append(spacingMm).Append(V1).Append(' ')
                      .Append(L0).Append("mm centres").Append(L1);
            }

            result.Plain  = " reinforced as " + plain;
            result.Marked = " " + L0 + "reinforced as" + L1 + " " + marked;
            return result;
        }

        /// <summary>
        /// Type letter plus bar size, e.g. "H16". Falls back to whichever half
        /// exists - a size with no grade is still worth printing.
        /// </summary>
        public static string TypeAndSize(string typeLetter, string sizeMm)
        {
            typeLetter = Clean(typeLetter);
            sizeMm     = Clean(sizeMm);
            if (typeLetter.Length == 0 && sizeMm.Length == 0) return "";
            if (sizeMm.Length == 0) return typeLetter;
            if (typeLetter.Length == 0) return sizeMm + " mm";

            // "H" + "16" -> "H16"; a grade written "B500B" still concatenates
            // readably, and anything already containing the size is left alone.
            return typeLetter.IndexOf(sizeMm, StringComparison.Ordinal) >= 0
                ? typeLetter
                : typeLetter + sizeMm;
        }

        private static string Clean(string s)
            => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();
    }
}
