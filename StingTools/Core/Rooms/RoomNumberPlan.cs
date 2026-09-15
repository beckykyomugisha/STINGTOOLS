using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace StingTools.Core.Rooms
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Room renumbering — the pure half.
    //
    //  Deliberately Revit-free (no Autodesk.Revit.* using) so the ordering and
    //  the collision detection can be unit-tested without a Revit host, the way
    //  VisibilityRuleMatcher.PlanCore is. The Revit-facing command hands this
    //  plain coordinates and reads back plain strings; Plan writes nothing.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>How the plan walks a floor plate to decide numbering order.</summary>
    public enum RoomNumberOrder
    {
        /// <summary>Left-to-right on one band, right-to-left on the next. Follows how a
        /// person walks a corridor, so consecutive numbers are physically adjacent.</summary>
        Serpentine = 0,
        /// <summary>Left-to-right on every band. Consecutive numbers jump back across
        /// the plate at each band change.</summary>
        RowMajor = 1,
        /// <summary>Top-to-bottom on every column band. For plates that read vertically.</summary>
        ColumnMajor = 2,
    }

    /// <summary>
    /// A named numbering scheme. Corporate baseline ships in
    /// Data/STING_ROOM_NUMBERING.json; a project overrides by id at
    /// &lt;project&gt;/_BIM_COORD/room_numbering.json.
    /// </summary>
    public class RoomNumberingScheme
    {
        public string Id { get; set; } = "default";
        public string Name { get; set; } = "Level + sequence";

        /// <summary>Token pattern. Supported: {lvl} {dept} {seq} {seq:Dn}.
        /// An unrecognised {token} is emitted verbatim AND reported as a warning —
        /// never silently dropped, which would collapse distinct rooms onto one number.</summary>
        public string Pattern { get; set; } = "{lvl}{seq:D2}";

        public RoomNumberOrder Order { get; set; } = RoomNumberOrder.Serpentine;

        /// <summary>Row separation, in millimetres. A new row starts wherever the vertical
        /// gap between one room centre and the next exceeds this; rooms closer than it are
        /// one row and sorted by X. Too small and every room becomes its own row; too large
        /// and a whole floor collapses into one. It is a GAP, not a grid — see
        /// <see cref="RoomNumberPlanner.ClusterByGap"/> for why that distinction matters.</summary>
        public double RowBandMm { get; set; } = 3000.0;

        public int StartAt { get; set; } = 1;
        public int Step { get; set; } = 1;

        /// <summary>Restart the sequence for each level. Almost always true — it is
        /// what makes {lvl} meaningful.</summary>
        public bool RestartPerLevel { get; set; } = true;

        /// <summary>Restart the sequence for each department as well as each level.</summary>
        public bool RestartPerDepartment { get; set; } = false;

        /// <summary>Leave rooms that already carry a number untouched, and treat their
        /// numbers as reserved. The safe default: renumbering an issued drawing set
        /// silently invalidates every reference to it.</summary>
        public bool PreserveExistingNumbers { get; set; } = true;
    }

    /// <summary>One room, as the planner needs to see it. Coordinates are plan
    /// millimetres in project coordinates; the caller converts from Revit feet.</summary>
    public class RoomSeed
    {
        public string Key { get; set; }            // opaque element id, echoed back
        public double XMm { get; set; }
        public double YMm { get; set; }
        public double LevelElevationMm { get; set; }
        public string LevelCode { get; set; } = "";
        public string Department { get; set; } = "";
        public string Name { get; set; } = "";
        public string ExistingNumber { get; set; } = "";
    }

    /// <summary>What the planner decided for one room.</summary>
    public class RoomNumberAssignment
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string OldNumber { get; set; }
        public string NewNumber { get; set; }
        public bool Changed
        {
            get { return !string.Equals(OldNumber ?? "", NewNumber ?? "", StringComparison.Ordinal); }
        }
    }

    /// <summary>The whole plan. Nothing here has touched the model.</summary>
    public class RoomNumberPlanResult
    {
        public List<RoomNumberAssignment> Assignments { get; } = new List<RoomNumberAssignment>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Blockers { get; } = new List<string>();

        /// <summary>Rooms held back because PreserveExistingNumbers was on.</summary>
        public int PreservedCount { get; set; }

        public int ChangedCount { get { return Assignments.Count(a => a.Changed); } }
        public bool CanApply { get { return Blockers.Count == 0; } }
    }

    public static class RoomNumberPlanner
    {
        /// <summary>
        /// Decide a number for every seed. Pure: no I/O, no Revit, no writes.
        /// A collision against a preserved number is a BLOCKER, not a warning —
        /// applying a plan that duplicates a room number produces a model that
        /// looks fine and schedules wrong.
        /// </summary>
        public static RoomNumberPlanResult Plan(
            IEnumerable<RoomSeed> seeds, RoomNumberingScheme scheme)
        {
            var result = new RoomNumberPlanResult();
            if (scheme == null) { result.Blockers.Add("No numbering scheme supplied."); return result; }

            var all = (seeds ?? Enumerable.Empty<RoomSeed>()).Where(s => s != null).ToList();
            if (all.Count == 0)
            {
                // An empty scope is not a success. Say so, the way LodTally does.
                result.Warnings.Add("No rooms in scope — nothing to renumber.");
                return result;
            }

            if (scheme.Step == 0)
            {
                result.Blockers.Add("Scheme Step is 0: every room would receive the same number.");
                return result;
            }
            if (scheme.RowBandMm <= 0)
            {
                result.Blockers.Add(
                    "Scheme RowBandMm is " + scheme.RowBandMm.ToString(CultureInfo.InvariantCulture) +
                    ". A band height must be positive — at zero every room lands in its own row " +
                    "and the walk order is meaningless.");
                return result;
            }
            ValidatePattern(scheme.Pattern, result);
            if (!result.CanApply) return result;

            // Numbers we must not mint, because a room outside the renumbering set
            // already owns them.
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var toNumber = new List<RoomSeed>();
            foreach (var s in all)
            {
                bool hasNumber = !string.IsNullOrWhiteSpace(s.ExistingNumber);
                if (scheme.PreserveExistingNumbers && hasNumber)
                {
                    reserved.Add(s.ExistingNumber.Trim());
                    result.PreservedCount++;
                    result.Assignments.Add(new RoomNumberAssignment
                    {
                        Key = s.Key,
                        Name = s.Name,
                        OldNumber = s.ExistingNumber,
                        NewNumber = s.ExistingNumber,
                    });
                }
                else toNumber.Add(s);
            }

            foreach (var group in GroupSeeds(toNumber, scheme))
            {
                int seq = scheme.StartAt;
                foreach (var seed in WalkOrder(group.Value, scheme))
                {
                    string number = Render(scheme.Pattern, seed, seq);

                    // Step past anything a preserved room already holds rather than
                    // minting a duplicate. Bounded so a pathological scheme cannot spin.
                    int guard = 0;
                    while (reserved.Contains(number) && guard++ < 10000)
                    {
                        seq += scheme.Step;
                        number = Render(scheme.Pattern, seed, seq);
                    }
                    if (guard >= 10000)
                    {
                        result.Blockers.Add(
                            "Could not find a free number for '" + seed.Name + "' after 10,000 " +
                            "attempts. The pattern probably omits {seq}, so every room renders identically.");
                        return result;
                    }

                    reserved.Add(number);
                    result.Assignments.Add(new RoomNumberAssignment
                    {
                        Key = seed.Key,
                        Name = seed.Name,
                        OldNumber = seed.ExistingNumber ?? "",
                        NewNumber = number,
                    });
                    seq += scheme.Step;
                }
            }

            // Belt and braces: prove the plan is internally unique before offering it.
            var dupes = result.Assignments
                .GroupBy(a => a.NewNumber, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();
            foreach (var d in dupes)
                result.Blockers.Add(
                    "Number '" + d.Key + "' would be assigned to " + d.Count() + " rooms: " +
                    string.Join(", ", d.Select(x => x.Name).Take(5)));

            return result;
        }

        private static void ValidatePattern(string pattern, RoomNumberPlanResult result)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                result.Blockers.Add("Scheme Pattern is empty.");
                return;
            }
            if (pattern.IndexOf("{seq", StringComparison.OrdinalIgnoreCase) < 0)
                result.Blockers.Add(
                    "Scheme Pattern '" + pattern + "' contains no {seq} token, so every room " +
                    "in a group would render the same number.");

            foreach (var token in EnumerateTokens(pattern))
            {
                string bare = token.Split(':')[0].ToLowerInvariant();
                if (bare != "lvl" && bare != "dept" && bare != "seq")
                    result.Warnings.Add(
                        "Pattern token '{" + token + "}' is not recognised and is emitted " +
                        "verbatim. Supported tokens: {lvl} {dept} {seq} {seq:Dn}.");
            }
        }

        private static IEnumerable<string> EnumerateTokens(string pattern)
        {
            int i = 0;
            while (i < pattern.Length)
            {
                int open = pattern.IndexOf('{', i);
                if (open < 0) yield break;
                int close = pattern.IndexOf('}', open);
                if (close < 0) yield break;
                yield return pattern.Substring(open + 1, close - open - 1);
                i = close + 1;
            }
        }

        /// <summary>Group seeds into independent sequences, ordered bottom-up by level.</summary>
        private static IEnumerable<KeyValuePair<string, List<RoomSeed>>> GroupSeeds(
            List<RoomSeed> seeds, RoomNumberingScheme scheme)
        {
            return seeds
                .GroupBy(s => new
                {
                    Lvl = scheme.RestartPerLevel ? (s.LevelCode ?? "") : "",
                    Elev = scheme.RestartPerLevel ? s.LevelElevationMm : 0.0,
                    Dept = scheme.RestartPerDepartment ? (s.Department ?? "") : "",
                })
                .OrderBy(g => g.Key.Elev)
                .ThenBy(g => g.Key.Lvl, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Dept, StringComparer.Ordinal)
                .Select(g => new KeyValuePair<string, List<RoomSeed>>(
                    g.Key.Lvl + "|" + g.Key.Dept, g.ToList()));
        }

        /// <summary>Order one group the way someone would walk it.</summary>
        internal static List<RoomSeed> WalkOrder(List<RoomSeed> group, RoomNumberingScheme scheme)
        {
            double band = scheme.RowBandMm;

            if (scheme.Order == RoomNumberOrder.ColumnMajor)
            {
                // Vertical bands, each read top-to-bottom, bands left-to-right.
                return group
                    .OrderBy(s => (int)Math.Floor(s.XMm / band))
                    .ThenByDescending(s => s.YMm)
                    .ThenBy(s => s.Key, StringComparer.Ordinal)
                    .ToList();
            }

            // Horizontal rows, read from the TOP of the plan down, the way a plan is read.
            var banded = ClusterByGap(group, band);

            var ordered = new List<RoomSeed>(group.Count);
            for (int i = 0; i < banded.Count; i++)
            {
                bool reverse = scheme.Order == RoomNumberOrder.Serpentine && (i % 2 == 1);
                var row = reverse
                    ? banded[i].OrderByDescending(s => s.XMm).ThenBy(s => s.Key, StringComparer.Ordinal)
                    : banded[i].OrderBy(s => s.XMm).ThenBy(s => s.Key, StringComparer.Ordinal);
                ordered.AddRange(row);
            }
            return ordered;
        }

        /// <summary>
        /// Group seeds into rows, top of plan first, starting a new row wherever the
        /// vertical gap to the room above exceeds <paramref name="band"/>.
        ///
        /// Deliberately NOT a fixed grid of floor(y / band). A fixed grid is anchored to
        /// the project origin, so two rooms 1.2 m apart land in different rows whenever
        /// they happen to straddle a multiple of the band height — and which rooms those
        /// are changes if anyone moves the project base point. Clustering on the gap is
        /// origin-independent and matches what the setting says it does.
        ///
        /// Single-linkage, so a chain of rooms each within the band of the next forms one
        /// row even if its ends are further apart than the band. That is the desired
        /// reading for a corridor; a plate with no clear rows should use RowMajor.
        /// </summary>
        internal static List<List<RoomSeed>> ClusterByGap(List<RoomSeed> group, double band)
        {
            var rows = new List<List<RoomSeed>>();
            if (group == null || group.Count == 0) return rows;

            var byY = group
                .OrderByDescending(s => s.YMm)
                .ThenBy(s => s.Key, StringComparer.Ordinal)
                .ToList();

            var current = new List<RoomSeed> { byY[0] };
            double previousY = byY[0].YMm;

            for (int i = 1; i < byY.Count; i++)
            {
                if (previousY - byY[i].YMm > band)
                {
                    rows.Add(current);
                    current = new List<RoomSeed>();
                }
                current.Add(byY[i]);
                previousY = byY[i].YMm;
            }
            rows.Add(current);
            return rows;
        }

        /// <summary>Substitute the pattern tokens for one room at one sequence value.</summary>
        internal static string Render(string pattern, RoomSeed seed, int seq)
        {
            var sb = new StringBuilder(pattern.Length + 8);
            int i = 0;
            while (i < pattern.Length)
            {
                char c = pattern[i];
                if (c != '{') { sb.Append(c); i++; continue; }

                int close = pattern.IndexOf('}', i);
                if (close < 0) { sb.Append(pattern, i, pattern.Length - i); break; }

                string token = pattern.Substring(i + 1, close - i - 1);
                string bare = token.Split(':')[0].ToLowerInvariant();
                string fmt = token.Contains(":") ? token.Substring(token.IndexOf(':') + 1) : null;

                switch (bare)
                {
                    case "lvl": sb.Append(seed.LevelCode ?? ""); break;
                    case "dept": sb.Append(Sanitise(seed.Department)); break;
                    case "seq":
                        sb.Append(string.IsNullOrEmpty(fmt)
                            ? seq.ToString(CultureInfo.InvariantCulture)
                            : SafeFormat(seq, fmt));
                        break;
                    default:
                        // Unknown token is emitted verbatim — ValidatePattern already warned.
                        sb.Append('{').Append(token).Append('}');
                        break;
                }
                i = close + 1;
            }
            return sb.ToString();
        }

        private static string SafeFormat(int value, string fmt)
        {
            try { return value.ToString(fmt, CultureInfo.InvariantCulture); }
            catch (FormatException) { return value.ToString(CultureInfo.InvariantCulture); }
        }

        /// <summary>Strip what Revit will not accept in a room number.</summary>
        private static string Sanitise(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }
    }
}
