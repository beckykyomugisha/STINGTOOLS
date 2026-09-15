// StingTools — Drawing Template Manager · the ISO 19650 document identifier
//
// WHY THIS FILE EXISTS
// --------------------
// SHT_TAG_1_TXT was assembled inline as
//
//     {project}-{originator}-{level}-{form}-{disc}-{sheet.SheetNumber}-{rev}
//
// which produced, on a real job:
//
//     PROJECTN-ORGANI-L01-LG-COORD-A-L1-001-1
//
// Five things are wrong with that, and each one shows on the paper:
//
//   1. NO VOLUME/SYSTEM FIELD. ISO 19650 is
//      Project-Originator-Volume-Level-Type-Role-Number. Dropping Volume is why
//      the export pattern emitted "SAH--ZZ" with an empty segment.
//   2. TYPE AND ROLE TRANSPOSED — it emits level-type-role as level-form-disc
//      but in the wrong slots relative to the standard.
//   3. "L01" IS NOT A LEVEL CODE. ISO uses 00, 01, B1, ZZ, XX. "L01" is STING's
//      internal form leaking onto an issued drawing.
//   4. "COORD" IS NOT A ROLE. Roles are single letters (A, S, M, E, P, ... Z).
//   5. THE NUMBER SEGMENT WAS THE WHOLE SHEET NUMBER, hyphens and all, so the
//      identifier had a VARIABLE number of segments and could not be parsed back.
//      It is also what let the code eat its own output once the sheet number
//      became the code.
//
// And the revision was appended. ISO keeps revision as metadata ALONGSIDE the
// identifier, not inside it: a document's identity does not change when it is
// revised, which is the entire point of having an identifier.
//
// Everything here is Revit-free so it is unit-tested. The Revit half reads
// parameters and hands strings over.

using System;
using System.Linq;
using System.Text;

namespace StingTools.Core.Drawing
{
    public static class Iso19650DocumentCode
    {
        public const string Separator = "-";

        /// <summary>Used where a field is genuinely not applicable — ISO's own
        /// convention, and readable as "deliberately none" rather than "forgotten".</summary>
        public const string NotApplicable = "ZZ";

        /// <summary>Used where a field applies but is unknown.</summary>
        public const string Unknown = "XX";

        /// <summary>Assemble the identifier:
        /// <c>Project-Originator-Volume-Level-Type-Role-Number</c>.
        ///
        /// Seven fields, always, in this order. A fixed field count is what makes the
        /// result parseable — the old form varied because the Number segment carried
        /// the sheet number's own hyphens.
        ///
        /// NO REVISION. It is metadata beside the identifier, not part of it.</summary>
        public static string Assemble(string project, string originator, string volume,
                                      string level, string type, string role, string number)
        {
            return string.Join(Separator, new[]
            {
                Field(project,    "PRJ"),
                Field(originator, "XXX"),
                NormaliseVolume(volume),
                NormaliseLevel(level),
                NormaliseType(type),
                NormaliseRole(role),
                NormaliseNumber(number),
            });
        }

        /// <summary>Does this string already look like an assembled identifier?
        ///
        /// Used to refuse re-assembling one that has been written back into the sheet
        /// number: seven hyphen-separated fields where the last is four digits is a
        /// shape the old free-form sheet numbers never had.</summary>
        public static bool LooksAssembled(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Trim().Split(new[] { '-' }, StringSplitOptions.None);
            if (parts.Length != 7) return false;
            return parts[6].Length == 4 && parts[6].All(char.IsDigit);
        }

        /// <summary>Volume / system. Blank becomes ZZ — "no volume on this job" is a
        /// real answer and far better than an empty segment, which is what produced
        /// "SAH--ZZ" in a live filename.</summary>
        public static string NormaliseVolume(string raw)
        {
            string v = Clean(raw);
            return v.Length == 0 ? NotApplicable : v;
        }

        /// <summary>Level. ISO uses 00 / 01 / B1 / M1 / ZZ / XX.
        ///
        /// STING carries L01, L02, GF, B1 internally, and those leaked onto drawings.
        /// "L01" -> "01", "GF" -> "00", "ROOF"/"RF" -> "RF", blank -> ZZ.</summary>
        public static string NormaliseLevel(string raw)
        {
            string v = Clean(raw);
            if (v.Length == 0) return NotApplicable;
            if (v == "GF" || v == "GRD" || v == "GROUND") return "00";
            if (v == "ROOF") return "RF";

            // L01 / LVL01 / L1 -> 01
            var digits = new string(v.Where(char.IsDigit).ToArray());
            if (v.StartsWith("L", StringComparison.Ordinal) && digits.Length > 0)
                return digits.PadLeft(2, '0');

            // B1 / B2 basements keep their form.
            if (v.StartsWith("B", StringComparison.Ordinal) && digits.Length > 0)
                return "B" + digits;

            if (digits.Length > 0 && digits.Length == v.Length)
                return digits.PadLeft(2, '0');

            return v.Length <= 2 ? v : v.Substring(0, 2);
        }

        /// <summary>Document type — DR, M3, SH, LG, SP … Blank becomes DR, because a
        /// sheet being tagged is a drawing unless something says otherwise.</summary>
        public static string NormaliseType(string raw)
        {
            string v = Clean(raw);
            return v.Length == 0 ? "DR" : v;
        }

        /// <summary>Role — ONE letter, per ISO 19650 / BS 1192.
        ///
        /// STING's discipline codes are richer than the role alphabet (COORD, GEN, MG,
        /// RP, FP, LV have no single-letter equivalent), so they fold to the nearest
        /// role, and anything multidisciplinary or unrecognised becomes Z (General).
        /// Z is a real ISO role, not a placeholder — which matters, because a reader
        /// can act on "general" and cannot act on a code that is not in the standard.</summary>
        public static string NormaliseRole(string raw)
        {
            string v = Clean(raw);
            if (v.Length == 0) return "Z";

            // A project may say which ISO letter ITS discipline code means -- "FS"
            // for fire safety, say -- in STING_SHEET_DISCIPLINES.json. What it may
            // NOT do is invent a letter: SheetDisciplineConfig rejects any target
            // outside ISO 19650's alphabet and names it, so an invalid one never
            // reaches this method. The table below stays in code because those
            // letters belong to the standard, not to a project.
            var aliases = SheetDisciplineConfig.RoleLetters;
            if (aliases != null && aliases.TryGetValue(v, out string configured))
                return configured;

            switch (v)
            {
                case "A": case "ARCH": case "ARCHITECTURAL":      return "A";
                case "S": case "STR": case "STRUCT":              return "S";
                case "M": case "MECH": case "HVAC": case "H":     return "M";
                case "E": case "ELEC": case "ELE": case "LV":     return "E";
                case "P": case "PLUMB": case "PLM": case "PH":    return "P";
                case "C": case "CIVIL":                          return "C";
                case "L": case "LAND": case "LANDSCAPE":          return "L";
                case "Q": case "QS":                             return "Q";
                case "K": case "CLIENT":                         return "K";
                case "W": case "CONTRACTOR":                     return "W";
                case "I": case "INT": case "INTERIOR":            return "I";
                case "D": case "DRAIN": case "DRAINAGE":          return "D";
                case "F": case "FM": case "FACILITIES":           return "F";
                case "G": case "GIS": case "SURVEY":              return "G";
                case "T": case "PLANNING":                       return "T";
                case "B": case "SURVEYOR":                       return "B";
                case "X": case "SUBCONTRACTOR":                  return "X";
                case "Y":                                        return "Y";

                // No single-letter role exists for these. Z = General is the honest
                // answer; inventing "CO" or "FP" would put a non-standard code on an
                // issued drawing, which is the thing this class exists to stop.
                case "COORD": case "GEN": case "MULTI": case "ZZ": return "Z";
                case "MG":                                        return "M";   // medical gas -> mechanical

                // Y = Specialist Designer, which is what these are.
                //
                // Fire protection folded to S (Structural) and radiation protection
                // to E (Electrical). Both were wrong in the way that matters: the
                // role segment names the PROFESSION accountable for the container, so
                // a sprinkler layout filed under S attributes it to the structural
                // engineer. It is a plausible-looking letter that misroutes a
                // drawing, which is worse than an obviously missing one.
                case "RP": case "FP": case "FIRE": case "SPECIALIST": return "Y";
            }

            // A single letter that is already a role passes through.
            if (v.Length == 1 && v[0] >= 'A' && v[0] <= 'Z') return v;
            return "Z";
        }

        /// <summary>Number — four digits, always.
        ///
        /// Takes the TRAILING digit run, so "A-L1-001" yields 0001 and "7" yields
        /// 0007. A fixed width is what keeps the identifier parseable and sortable; a
        /// variable one is how the old form ended up with a different number of
        /// segments per sheet.</summary>
        public static string NormaliseNumber(string raw)
        {
            // The trailing run is taken from the RAW string, BEFORE separators are
            // stripped. Cleaning first merges the digit groups: "A-L1-001" becomes
            // "AL1001", whose trailing run is "1001" — so sheet 1 would be numbered
            // 1001. Caught by the test built from that exact sheet.
            string v = (raw ?? "").Trim().ToUpperInvariant();
            if (v.Length == 0) return "0000";

            int end = v.Length;
            while (end > 0 && !char.IsDigit(v[end - 1])) end--;
            int start = end;
            while (start > 0 && char.IsDigit(v[start - 1])) start--;

            string digits = end > start ? v.Substring(start, end - start) : "";
            if (digits.Length == 0) return "0000";

            digits = digits.TrimStart('0');
            if (digits.Length == 0) digits = "0";
            return digits.Length >= 4 ? digits : digits.PadLeft(4, '0');
        }

        private static string Field(string raw, string fallback)
        {
            string v = Clean(raw);
            return v.Length == 0 ? fallback : v;
        }

        /// <summary>Upper-case, and strip anything that is not A-Z or 0-9. Separators
        /// inside a FIELD are what made the old identifier unparseable, so a field can
        /// never contain one.</summary>
        private static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim().ToUpperInvariant())
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
            return sb.ToString();
        }
    }
}
