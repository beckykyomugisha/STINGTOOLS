// StingTools — Drawing Template Manager · what discipline a sheet belongs to
//
// WHY THIS FILE EXISTS
// --------------------
// An architectural GA plan was issued as SAH-PLNS-ZZ-01-DR-Z-0002. "Z" is ISO
// 19650's General / non-disciplinary role, and it was correct given its input:
// the discipline resolved to COORD, and COORD folds to Z.
//
// It resolved to COORD because DeriveSheetDiscipline decided the discipline by
// taking a CENSUS of the elements drawn in the viewports, and declaring COORD
// whenever no single discipline held 75% of them. A ground-floor plan of a house
// legitimately shows walls, doors, windows, structural columns and sanitary
// fittings — so architecture cannot reach 75%, and every GA plan in the set was
// classified as a coordination drawing. The threshold was not slightly wrong; it
// asked a question the drawing could not answer.
//
// The sheet already states its discipline twice, explicitly, in fields a person
// controls: its NUMBER ("A-001") and its TITLE ("GROUND FLOOR PLAN"). Those are
// statements of intent. The census is an inference about content, and content is
// exactly what a general arrangement mixes on purpose. So intent wins, and the
// census is the fallback for a sheet that says nothing.
//
// That ordering also makes the outcome FIXABLE: renumber or rename the sheet and
// the role follows. Under the census it was not fixable at all — an operator had
// no way to overrule a vote taken over elements.
//
// Revit-free on purpose, so StingTools.Tags.Tests can exercise it.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>Which STING discipline code a sheet belongs to, decided from the
    /// sheet's own number and title first and an element census only as a
    /// fallback.</summary>
    public static class SheetDisciplineResolver
    {
        /// <summary>The discipline a sheet NUMBER declares, or null.
        ///
        /// The leading letters before the first separator or digit: "A-001" -> A,
        /// "M-101" -> M, "FP-01" -> FP. This is the strongest evidence there is,
        /// because a sheet number is chosen deliberately and printed on the drawing.
        ///
        /// A number that has already been rewritten into a full ISO identifier
        /// ("SAH-PLNS-ZZ-01-DR-Z-0002") declares nothing — its first segment is a
        /// project code, and reading "SAH" as a discipline would be worse than
        /// reading nothing. Iso19650DocumentCode.LooksAssembled is the guard.</summary>
        public static string FromSheetNumber(string sheetNumber)
        {
            if (string.IsNullOrWhiteSpace(sheetNumber)) return null;
            string n = sheetNumber.Trim().ToUpperInvariant();

            if (Iso19650DocumentCode.LooksAssembled(n)) return null;

            var prefix = new string(n.TakeWhile(char.IsLetter).ToArray());
            if (prefix.Length == 0 || prefix.Length > 5) return null;

            // The prefix must actually be a prefix: something has to follow it, or
            // a sheet merely NAMED "PLAN" would read as discipline "PLAN".
            if (prefix.Length == n.Length) return null;

            return NumberPrefixes.TryGetValue(prefix, out string disc) ? disc : null;
        }

        /// <summary>The discipline a sheet TITLE declares, or null.
        ///
        /// Whole words only. The old version used substring matching, so "ARCH"
        /// matched ARCHIVE, "FIRE" matched FIREPLACE and "DATA" matched DATA SHEET —
        /// each of which would have mis-filed a drawing under a discipline nobody
        /// chose, with nothing reporting it.</summary>
        public static string FromTitle(string sheetName)
        {
            if (string.IsNullOrWhiteSpace(sheetName)) return null;

            var words = new HashSet<string>(
                sheetName.ToUpperInvariant()
                         .Split(Separators, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

            // Ordered, because a title can name more than one thing and the first
            // match must be predictable rather than dictionary-order.
            foreach (var rule in TitleKeywords)
                if (rule.Value.Any(words.Contains))
                    return rule.Key;

            return null;
        }

        /// <summary>The final answer.
        ///
        /// Precedence: what the sheet SAYS (number, then title), then what it
        /// CONTAINS (the census), then GEN. `census` is the per-discipline element
        /// tally; pass null or empty when there is none.</summary>
        public static string Resolve(string sheetNumber, string sheetName,
                                     IDictionary<string, int> census)
        {
            string stated = FromSheetNumber(sheetNumber) ?? FromTitle(sheetName);
            if (stated != null) return stated;

            return FromCensus(census) ?? "GEN";
        }

        /// <summary>The discipline an element tally points to, or null when it points
        /// nowhere in particular.
        ///
        /// COORD requires a genuine mix: at least two disciplines each holding a
        /// QUARTER of the elements. The old rule declared COORD whenever the leader
        /// held under three quarters, so 74% architecture and 26% spread across four
        /// trades was filed as coordination. A drawing is a coordination drawing
        /// because someone made it one, not because its leader fell a point short.</summary>
        public static string FromCensus(IDictionary<string, int> census)
        {
            if (census == null || census.Count == 0) return null;

            var sorted = census.Where(kv => kv.Value > 0)
                               .OrderByDescending(kv => kv.Value)
                               .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                               .ToList();
            if (sorted.Count == 0) return null;

            double total = sorted.Sum(kv => kv.Value);
            if (total <= 0) return null;

            int substantial = sorted.Count(kv => kv.Value / total >= 0.25);
            if (substantial >= 2 && sorted[0].Value / total < 0.60) return "COORD";

            return sorted[0].Key;
        }

        private static readonly char[] Separators =
            { ' ', '-', '_', ',', '.', '/', '(', ')', '&', ':', ';', '\t' };

        /// <summary>Sheet-number prefixes, as drawing sets actually number them.</summary>
        private static readonly Dictionary<string, string> NumberPrefixes =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "A", "A" }, { "AR", "A" }, { "ARCH", "A" },
                { "S", "S" }, { "ST", "S" }, { "STR", "S" },
                { "M", "M" }, { "MEC", "M" }, { "MECH", "M" }, { "H", "M" }, { "HVAC", "M" },
                { "E", "E" }, { "EL", "E" }, { "ELE", "E" }, { "ELEC", "E" },
                { "P", "P" }, { "PL", "P" }, { "PLM", "P" }, { "PH", "P" },
                { "C", "C" }, { "CIV", "C" },
                { "L", "L" }, { "LA", "L" },
                { "FP", "FP" }, { "FA", "FP" },
                { "LV", "LV" }, { "ICT", "LV" }, { "IT", "LV" },
                { "I", "I" }, { "ID", "I" },
                { "CO", "COORD" }, { "CD", "COORD" }, { "COORD", "COORD" },
                { "G", "GEN" }, { "GEN", "GEN" },
            };

        /// <summary>Whole words in a sheet title, in decision order.</summary>
        private static readonly List<KeyValuePair<string, string[]>> TitleKeywords =
            new List<KeyValuePair<string, string[]>>
            {
                // Explicitly multidisciplinary beats every single-discipline word,
                // because "COORDINATED SERVICES PLAN" names both.
                new KeyValuePair<string, string[]>("COORD",
                    new[] { "COORDINATION", "COORDINATED", "COMBINED", "COMPOSITE", "MULTIDISCIPLINARY" }),
                new KeyValuePair<string, string[]>("FP",
                    new[] { "SPRINKLER", "SPRINKLERS", "FIREFIGHTING", "HYDRANT", "SUPPRESSION" }),
                new KeyValuePair<string, string[]>("LV",
                    new[] { "SECURITY", "CCTV", "TELECOMS", "TELECOM", "STRUCTURED", "AUDIOVISUAL" }),
                new KeyValuePair<string, string[]>("M",
                    new[] { "MECHANICAL", "HVAC", "DUCTWORK", "VENTILATION", "REFRIGERATION" }),
                new KeyValuePair<string, string[]>("E",
                    new[] { "ELECTRICAL", "LIGHTING", "POWER", "SMALL" }),
                new KeyValuePair<string, string[]>("P",
                    new[] { "PLUMBING", "SANITARY", "DRAINAGE", "ABOVE", "BELOW" }),
                new KeyValuePair<string, string[]>("S",
                    new[] { "STRUCTURAL", "FOUNDATION", "FOUNDATIONS", "REBAR", "REINFORCEMENT", "FRAMING" }),
                new KeyValuePair<string, string[]>("C",
                    new[] { "CIVIL", "EARTHWORKS", "ROADS", "HIGHWAY", "HIGHWAYS" }),
                new KeyValuePair<string, string[]>("L",
                    new[] { "LANDSCAPE", "LANDSCAPING", "PLANTING", "SOFTWORKS" }),
                new KeyValuePair<string, string[]>("I",
                    new[] { "INTERIOR", "INTERIORS", "JOINERY", "FF&E", "FFE" }),
                new KeyValuePair<string, string[]>("A",
                    new[] { "ARCHITECTURAL", "ARCHITECTURE", "ELEVATION", "ELEVATIONS",
                            "SECTION", "SECTIONS", "FLOOR", "ROOF", "DOOR", "DOORS",
                            "WINDOW", "WINDOWS", "FINISHES", "GA" }),
            };
    }
}
