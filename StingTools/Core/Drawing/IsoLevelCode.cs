// StingTools — Drawing Template Manager · the ISO 19650 level code for a storey
//
// WHY THIS FILE EXISTS
// --------------------
// A ground-floor plan was issued with level segment "01". ISO 19650 numbers the
// ground storey 00, upper storeys 01 / 02 / …, and basements B1 / B2 — so "01"
// on a ground floor names the storey above the one drawn.
//
// It came from the level being called "L1". The old rule took any trailing digits
// off the name and prefixed L: "L1" -> "L01" -> "01". That is defensible in
// isolation and wrong here, because whether "Level 1" means the ground storey or
// the first storey ABOVE it is a project convention, not a fact about the string.
// Most Revit templates ship "Level 1" AT ground; UK practice often names it GF or
// L00. A name-only rule has to guess, and it guessed the same way for everyone.
//
// The building itself already answers the question: the ground storey is the one
// at datum. So the code is derived from ELEVATION, which is a measured fact —
// nearest to zero is 00, each storey above counts up, each below counts down into
// B1, B2. A project that disagrees can still say so by name, and one with no
// elevations to work from falls back to reading the name as before.
//
// Revit-free so it can be tested: the caller passes names and elevations.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>One storey, as the resolver needs to see it.</summary>
    public sealed class StoreyDatum
    {
        public string Name { get; set; }
        /// <summary>Elevation in millimetres, relative to the project's own datum.</summary>
        public double ElevationMm { get; set; }
        /// <summary>DTW-116 — Revit's "Building Story" flag (LEVEL_IS_BUILDING_STORY):
        /// false for a datum level such as "T.O. Steel" that is not a storey; null when the
        /// caller does not know, in which case the level counts as a storey.</summary>
        public bool? IsBuildingStorey { get; set; }
    }

    public static class IsoLevelCode
    {
        /// <summary>No level applies.</summary>
        public const string NotApplicable = "XX";
        /// <summary>Applies to more than one level.</summary>
        public const string Multiple = "ZZ";

        /// <summary>ISO 19650 level codes for a whole building, keyed by level name.
        ///
        /// Built once from every level in the model rather than per sheet, because
        /// the code for a storey depends on where it sits in the STACK — you cannot
        /// tell whether a level is 01 or 02 by looking at it alone, which is why the
        /// name-only rule could never have been right.</summary>
        public static Dictionary<string, string> BuildMap(IEnumerable<StoreyDatum> storeys)
            => BuildMap(storeys, DefaultCoincidentToleranceMm);

        /// <summary>DTW-116 — levels closer than this share a storey: per-building level
        /// sets at the same height, and datum levels such as "Level 1 SSL" a screed below
        /// FFL. 50 mm.</summary>
        public const double DefaultCoincidentToleranceMm = 50.0;

        /// <summary>
        /// The map, with levels within <paramref name="coincidentToleranceMm"/> of each other
        /// grouped into one storey (one code, one place in the stack), and levels whose
        /// <see cref="StoreyDatum.IsBuildingStorey"/> is false left out of the count — they
        /// take the code of the storey they coincide with or sit in.
        ///
        /// Numbering levels by list position gave coincident levels different codes and
        /// shifted every storey above them (DTW-116).
        /// </summary>
        public static Dictionary<string, string> BuildMap(IEnumerable<StoreyDatum> storeys, double coincidentToleranceMm)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (storeys == null) return map;
            double tol = Math.Max(0, coincidentToleranceMm);

            var ordered = storeys
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Name))
                .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(s => s.ElevationMm)
                .ToList();
            if (ordered.Count == 0) return map;

            // The stack is the building storeys. A level known NOT to be one (Building
            // Story off) does not take a number; unknown counts. If nothing is flagged a
            // storey, every level counts — a model with the flag cleared everywhere still
            // needs codes.
            var stack = ordered.Where(s => s.IsBuildingStorey != false).ToList();
            if (stack.Count == 0) stack = ordered;

            // Group coincident storeys: a new storey starts more than tol above the first
            // level of the current one.
            var groups = new List<List<StoreyDatum>>();
            foreach (var s in stack)
            {
                if (groups.Count == 0 || s.ElevationMm - groups[groups.Count - 1][0].ElevationMm > tol)
                    groups.Add(new List<StoreyDatum>());
                groups[groups.Count - 1].Add(s);
            }

            // The ground storey: nearest to datum, and on a tie the LOWER one — a
            // building with levels at -150 and +150 has its ground floor at the
            // slab, not the one above it.
            int groundIndex = 0;
            double best = double.MaxValue, bestElev = double.MaxValue;
            for (int i = 0; i < groups.Count; i++)
            {
                var rep = groups[i].OrderBy(s => Math.Abs(s.ElevationMm)).ThenBy(s => s.ElevationMm).First();
                double d = Math.Abs(rep.ElevationMm);
                if (d < best || (d == best && rep.ElevationMm < bestElev))
                { best = d; bestElev = rep.ElevationMm; groundIndex = i; }
            }

            var groupCodes = new string[groups.Count];
            for (int i = 0; i < groups.Count; i++)
            {
                // A name in the group that states a code speaks for the group: "GF" and a
                // coincident "Level 1 SSL" are both the ground storey.
                string stated = groups[i].Select(s => FromName(s.Name)).FirstOrDefault(c => c != null);
                int offset = i - groundIndex;
                groupCodes[i] = stated
                    ?? (offset == 0 ? "00" : offset > 0 ? offset.ToString("00") : "B" + (-offset));
                foreach (var s in groups[i])
                    // A name that states a special code outranks the stack. ROOF is a
                    // roof wherever it sits, and a project that has named a level GF has
                    // already answered the question this class exists to answer.
                    map[s.Name] = FromName(s.Name) ?? groupCodes[i];
            }

            // Levels outside the stack take the storey they coincide with, else the one
            // they sit in (at or below), else the lowest.
            foreach (var s in ordered)
            {
                if (map.ContainsKey(s.Name)) continue;
                string stated = FromName(s.Name);
                if (stated != null) { map[s.Name] = stated; continue; }
                int pick = -1;
                for (int i = 0; i < groups.Count && pick < 0; i++)
                    if (groups[i].Any(g => Math.Abs(g.ElevationMm - s.ElevationMm) <= tol)) pick = i;
                if (pick < 0)
                    for (int i = 0; i < groups.Count; i++)
                        if (groups[i][0].ElevationMm <= s.ElevationMm + tol) pick = i;
                map[s.Name] = groupCodes[pick < 0 ? 0 : pick];
            }

            return map;
        }

        /// <summary>The code a level NAME states outright, or null when it only
        /// implies a position in the stack.
        ///
        /// "L1" deliberately returns null: it is exactly the ambiguous case, and
        /// answering it here would put the guess back.</summary>
        public static string FromName(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName)) return null;
            string n = levelName.Trim().ToUpperInvariant();
            var words = new HashSet<string>(
                n.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

            // "GROUND" has to be the whole of what the name says. Merely CONTAINING
            // it is not a statement: "Lower Ground" is a storey BELOW the ground one
            // and would have been filed as 00, which is the same over-eager matching
            // that made ARCHIVE match ARCH. A qualified name is ambiguous, so it
            // says nothing and the stack decides.
            if (n == "GF" || n == "G" || n == "L00" || n == "00") return "00";
            if (words.Contains("GROUND") && words.All(w => GroundWords.Contains(w))) return "00";

            if (n == "RF" || words.Contains("ROOF")) return "RF";
            if (words.Contains("MEZZANINE") || n == "MEZZ" || n == "MZ") return "M1";

            if (words.Contains("BASEMENT") || n.StartsWith("B", StringComparison.Ordinal))
            {
                string digits = new string(n.Where(char.IsDigit).ToArray());
                // "B" alone, or "BASEMENT", is the first basement. "BUILDING A" is
                // not a basement at all, which is why a bare B only counts when
                // nothing else follows it.
                if (words.Contains("BASEMENT")) return "B" + (digits.Length > 0 ? digits.TrimStart('0') : "1");
                if (n.Length <= 3 && digits.Length > 0) return "B" + digits.TrimStart('0');
                if (n == "B") return "B1";
            }

            // An already-ISO two-digit code passes through: a project that has done
            // this properly must not have it re-derived underneath them.
            if (n.Length == 2 && n.All(char.IsDigit)) return n;

            return null;
        }

        /// <summary>The fallback for a model with no elevations to reason about:
        /// read the name the way the old rule did. "L1" -> "01", "LEVEL 2" -> "02".
        /// Still a guess, and now only reached when there is nothing better.</summary>
        public static string FromNameOnly(string levelName)
        {
            string stated = FromName(levelName);
            if (stated != null) return stated;
            if (string.IsNullOrWhiteSpace(levelName)) return NotApplicable;

            string digits = new string(levelName.Where(char.IsDigit).ToArray());
            if (digits.Length == 0) return NotApplicable;
            digits = digits.TrimStart('0');
            if (digits.Length == 0) return "00";
            return int.TryParse(digits, out int v) && v >= 0 && v <= 99
                ? v.ToString("00")
                : NotApplicable;
        }

        private static readonly char[] NameSeparators = { ' ', '-', '_', '.', '/' };

        /// <summary>Words a plain ground-storey name may consist of. Anything else in
        /// the name -- LOWER, UPPER, MEZZANINE -- qualifies it into a different
        /// storey, and a qualified name is not an answer.</summary>
        private static readonly HashSet<string> GroundWords =
            new HashSet<string>(StringComparer.Ordinal) { "GROUND", "FLOOR", "LEVEL", "FL", "LVL" };
    }
}
