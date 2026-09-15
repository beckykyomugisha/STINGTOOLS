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
using System.Text;

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

            return SheetDisciplineConfig.NumberPrefixes.TryGetValue(prefix, out string disc)
                ? disc : null;
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
            // match must be predictable rather than dictionary-order. That is why the
            // configured form is a LIST and not a map.
            foreach (var rule in SheetDisciplineConfig.TitleKeywords)
                if (rule.Words != null && rule.Words.Any(words.Contains))
                    return rule.Discipline;

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

        /// <summary>The TITLE_BLOCK.csv column a discipline belongs to.
        ///
        /// Two vocabularies exist and both are load-bearing: this resolver speaks the
        /// ISO-ish short codes that go into SHT_DISC and the identifier's role segment
        /// (A, S, M, E, P), and TITLE_BLOCK.csv has columns named ARCH, STR, MEP, ELE,
        /// PLM. They have to be translated somewhere, and "somewhere" was nowhere:
        /// TitleBlockEngine.ResolveDiscipline tested SHT_DISC against the column names,
        /// found "A" was not "ARCH", and silently fell through to reading the FIRST
        /// CHARACTER of the sheet number instead. On a sheet numbered
        /// "SAH-PLNS-ZZ-01-DR-A-0001" that first character is S, so an architectural
        /// drawing was filled from the STRUCTURAL column of the CSV.
        ///
        /// `available` is the CSV's actual header, so a project that adds a column gets
        /// it used; pass null to accept any name.</summary>
        public static string ToCsvColumn(string disc, ICollection<string> available = null)
        {
            if (string.IsNullOrWhiteSpace(disc)) return "GEN";
            string d = disc.Trim().ToUpperInvariant();

            bool Have(string col) => available == null || available.Count == 0
                || available.Contains(col, StringComparer.OrdinalIgnoreCase);

            // A column named exactly as the code wins -- that is how a project extends
            // the CSV with its own discipline and has it honoured without a code change.
            if (Have(d) && (available != null && available.Count > 0)) return d;

            if (SheetDisciplineConfig.CsvColumns.TryGetValue(d, out string mapped) && Have(mapped))
                return mapped;

            // No column for this discipline. GEN is the honest answer: it means "the
            // project-wide defaults", which is true. Folding Civil into Structural or
            // Interiors into Architectural would put one trade's defaults on another's
            // drawings, and nothing would report it.
            return Have("GEN") ? "GEN" : d;
        }

        /// <summary>Build a sheet number from a pattern.
        ///
        /// Tokens: {disc} {lvl} {proj} {orig} and {seq} / {seq:D2} / {seq:D3} /
        /// {seq:D4}. An unknown token is left alone rather than silently deleted --
        /// a typo that vanishes produces a number nobody can explain, and one that
        /// survives is obvious the moment the preview is read.
        ///
        /// A token that resolves to nothing takes its adjacent separator with it, so
        /// a project with no level code gets "A-001" and not "A--001". That exact
        /// double dash reached a live PDF filename once already.</summary>
        public static string FormatNumber(string pattern, string disc, string level,
                                          string projectCode, string originator, int seq)
        {
            if (string.IsNullOrWhiteSpace(pattern)) pattern = "{disc}-{seq:D3}";

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "disc", disc }, { "lvl", level },
                { "proj", projectCode }, { "orig", originator },
            };

            var sb = new StringBuilder();
            var pendingSep = new StringBuilder();
            bool wroteSomething = false;

            int i = 0;
            while (i < pattern.Length)
            {
                if (pattern[i] != '{')
                {
                    pendingSep.Append(pattern[i]);
                    i++;
                    continue;
                }

                int close = pattern.IndexOf('}', i);
                if (close < 0) { pendingSep.Append(pattern.Substring(i)); break; }

                string token = pattern.Substring(i + 1, close - i - 1);
                i = close + 1;

                string name = token, fmt = null;
                int colon = token.IndexOf(':');
                if (colon >= 0) { name = token.Substring(0, colon); fmt = token.Substring(colon + 1); }

                string resolved;
                if (string.Equals(name, "seq", StringComparison.OrdinalIgnoreCase))
                {
                    int pad = 3;
                    if (!string.IsNullOrEmpty(fmt) && fmt.Length >= 2
                        && (fmt[0] == 'D' || fmt[0] == 'd')
                        && int.TryParse(fmt.Substring(1), out int p2) && p2 > 0 && p2 <= 8)
                        pad = p2;
                    resolved = seq.ToString(new string('0', pad));
                }
                else if (values.TryGetValue(name, out string v))
                {
                    resolved = (v ?? "").Trim();
                }
                else
                {
                    // Unknown token: keep it visible.
                    resolved = "{" + token + "}";
                }

                if (resolved.Length == 0)
                {
                    pendingSep.Clear();   // drop the separator that led to nothing
                    continue;
                }

                if (wroteSomething) sb.Append(pendingSep);
                pendingSep.Clear();
                sb.Append(resolved);
                wroteSomething = true;
            }

            sb.Append(pendingSep);
            return sb.ToString().Trim();
        }


        private static readonly char[] Separators =
            { ' ', '-', '_', ',', '.', '/', '(', ')', '&', ':', ';', '\t' };

        /// <summary>Sheet-number prefixes, as drawing sets actually number them.</summary>

    }
}
