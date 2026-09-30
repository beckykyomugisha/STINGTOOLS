// StingTools — the Revit-free rules behind the drainage and supply schematics.
//
// WHY THIS FILE EXISTS
//
// Both schematic generators used to invent content when the model did not
// supply it: a quarter of all nodes promoted to "stacks" when none was found,
// a dashed "DN{stack/2} VENT" on every stack whether or not a vent pipe
// existed, up to four "indicative" branches on a stack with no fixtures, a
// 1.25 % slope for a branch with no slope, and floor labels computed as
// Z / 3000 mm. They also sized glyphs and text offsets in model millimetres on
// a 1:50 view (so a "60 mm" glyph printed at 1.2 mm), and the supply layout let
// two nodes of different parents land on the same cell.
//
// The decisions that replace those guesses live here, Revit-free, so they are
// unit-tested (StingTools.Tags.Tests / SchematicLayoutMathTests). The Revit-
// bound generators feed them ids, elevations and classifications read from
// the model — never assumed ones.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Plumbing
{
    /// <summary>Unit, level and filter rules shared by the plumbing schematics.</summary>
    public static class SchematicLayoutMath
    {
        public const double MmPerFoot = 304.8;

        /// <summary>
        /// Model length (feet) that prints at <paramref name="paperMm"/> on a view of
        /// scale 1:<paramref name="viewScale"/>. Drafting-view geometry is in model
        /// units, so anything meant to have a paper size (glyphs, text offsets) must
        /// be multiplied by the scale, or it prints 50 times too small at 1:50.
        /// </summary>
        public static double PaperMmToModelFt(double paperMm, int viewScale)
            => paperMm * Math.Max(1, viewScale) / MmPerFoot;

        /// <summary>
        /// Index of the value closest to <paramref name="target"/>; -1 when there is
        /// none. NaN / infinite values are never chosen. Ties go to the first.
        /// </summary>
        public static int ClosestIndex(IReadOnlyList<double> values, double target)
        {
            if (values == null) return -1;
            int best = -1;
            double bestDiff = double.MaxValue;
            for (int i = 0; i < values.Count; i++)
            {
                double v = values[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                double d = Math.Abs(v - target);
                if (d < bestDiff) { bestDiff = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Exact (trimmed, case-insensitive) system-name match; an empty filter
        /// matches everything. The picker lists whole PipingSystem names, so the
        /// filter must match the whole name — a substring test let "Sanitary 1"
        /// also pick up "Sanitary 10".
        /// </summary>
        public static bool SystemNameMatches(string systemName, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return string.Equals((systemName ?? "").Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Index of the highest level whose elevation is at or below <paramref name="z"/>
        /// (within <paramref name="tolerance"/>), in <paramref name="sortedElevations"/>
        /// (ascending). -1 when <paramref name="z"/> is below every level — the caller
        /// then has no level to name, and must not make one up.
        /// </summary>
        public static int IndexAtOrBelow(IReadOnlyList<double> sortedElevations, double z, double tolerance)
        {
            if (sortedElevations == null) return -1;
            int found = -1;
            for (int i = 0; i < sortedElevations.Count; i++)
            {
                if (sortedElevations[i] <= z + tolerance) found = i;
                else break;
            }
            return found;
        }

        /// <summary>
        /// Indices of the levels a vertical run from <paramref name="zMin"/> to
        /// <paramref name="zMax"/> passes: the level at or below its foot, then every
        /// level up to its head. Empty when no level exists or the run lies wholly
        /// below the lowest one.
        /// </summary>
        public static List<int> LevelsSpanning(IReadOnlyList<double> sortedElevations,
            double zMin, double zMax, double tolerance)
        {
            var result = new List<int>();
            if (sortedElevations == null || sortedElevations.Count == 0) return result;
            if (zMax < zMin) { var t = zMin; zMin = zMax; zMax = t; }
            int start = IndexAtOrBelow(sortedElevations, zMin, tolerance);
            if (start < 0)
            {
                // Foot below every level: start at the first level the run reaches, if any.
                start = 0;
                if (sortedElevations[0] > zMax + tolerance) return result;
            }
            for (int i = start; i < sortedElevations.Count; i++)
            {
                if (sortedElevations[i] > zMax + tolerance) break;
                result.Add(i);
            }
            return result;
        }

        /// <summary>
        /// Groups stack segments that share a plan position (within
        /// <paramref name="toleranceFt"/>) — one physical stack is modelled as a
        /// vertical pipe per storey, and each segment used to be drawn as its own
        /// stack. Order of the first member is kept.
        /// </summary>
        public static List<List<long>> ClusterByPlanPosition(
            IReadOnlyList<(long Id, double X, double Y)> segments, double toleranceFt)
        {
            var groups = new List<List<long>>();
            var anchors = new List<(double X, double Y)>();
            if (segments == null) return groups;
            foreach (var s in segments)
            {
                int hit = -1;
                for (int g = 0; g < anchors.Count; g++)
                {
                    double dx = anchors[g].X - s.X, dy = anchors[g].Y - s.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) <= toleranceFt) { hit = g; break; }
                }
                if (hit < 0)
                {
                    anchors.Add((s.X, s.Y));
                    groups.Add(new List<long> { s.Id });
                }
                else groups[hit].Add(s.Id);
            }
            return groups;
        }

        /// <summary>
        /// Rank of a candidate supply source (lower is better); int.MaxValue = not a
        /// source. A water meter is the incoming main; a tank or a pump set feeds the
        /// network; other equipment is a weaker guess. Anything else means the
        /// schematic has no modelled source and must say so.
        /// </summary>
        public static int SupplySourceRank(string glyphCode, bool isEquipment)
        {
            switch ((glyphCode ?? "").ToUpperInvariant())
            {
                case "MTR": return 0;
                case "TK":  return 1;
                case "PMP": return 2;
            }
            return isEquipment ? 3 : int.MaxValue;
        }

        /// <summary>
        /// Pressure label text, or null when none may be shown. A pressure is only
        /// printed when the inlet pressure was configured by the user; when the
        /// source node itself was not found in the model it is marked indicative.
        /// </summary>
        public static string PressureLabel(double kpa, bool inletPressureConfigured, bool sourceAssumed)
        {
            if (!inletPressureConfigured || kpa <= 0 || double.IsNaN(kpa)) return null;
            return sourceAssumed ? $"{kpa:F0} kPa (indicative)" : $"{kpa:F0} kPa";
        }

        /// <summary>Slope label: the value when known, "slope ?" when not — never a default.</summary>
        public static string SlopeLabel(double? slopePct)
            => slopePct.HasValue && !double.IsNaN(slopePct.Value) ? "× " + slopePct.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%" : "slope ?";
    }

    /// <summary>Kind of a graph node, as far as the vent / branch rules care.</summary>
    public enum SchematicPipeKind
    {
        DrainPipe,
        VentPipe,
        Fitting,
        Fixture,
        Other
    }

    /// <summary>Result of looking for a vent on a stack.</summary>
    public sealed class VentHit
    {
        public List<long> VentIds { get; } = new List<long>();
        public double DnMm { get; set; }
    }

    /// <summary>Graph rules: which vent belongs to a stack, what a branch serves.</summary>
    public static class SchematicGraphRules
    {
        /// <summary>
        /// The vent pipes connected to a stack: Vent-classified pipes reachable from any
        /// stack segment directly or through at most <paramref name="maxFittingHops"/>
        /// fittings (a tee / coupling at the stack head). Drain pipes, fixtures and other
        /// elements stop the walk — a vent off a branch belongs to that branch, not the
        /// stack. Null when no vent pipe connects; the DN is the largest real one.
        /// </summary>
        public static VentHit FindStackVent(IEnumerable<long> stackIds,
            Func<long, IEnumerable<long>> neighbours,
            Func<long, SchematicPipeKind> kind,
            Func<long, double> dnMm,
            int maxFittingHops = 2)
        {
            if (stackIds == null || neighbours == null || kind == null) return null;
            var stackSet = new HashSet<long>(stackIds);
            var seen = new HashSet<long>(stackSet);
            var frontier = new Queue<(long Id, int Hops)>();
            foreach (var s in stackSet) frontier.Enqueue((s, 0));

            var hit = new VentHit();
            while (frontier.Count > 0)
            {
                var (id, hops) = frontier.Dequeue();
                foreach (var n in neighbours(id) ?? Enumerable.Empty<long>())
                {
                    if (!seen.Add(n)) continue;
                    switch (kind(n))
                    {
                        case SchematicPipeKind.VentPipe:
                            hit.VentIds.Add(n);
                            double dn = dnMm?.Invoke(n) ?? 0;
                            if (dn > hit.DnMm) hit.DnMm = dn;
                            break;
                        case SchematicPipeKind.Fitting:
                            if (hops < maxFittingHops) frontier.Enqueue((n, hops + 1));
                            break;
                    }
                }
            }
            return hit.VentIds.Count > 0 ? hit : null;
        }

        /// <summary>
        /// Number of fixtures reachable from <paramref name="start"/> without passing
        /// through <paramref name="blocked"/> (the stack and its junctions) or a vent.
        /// Bounded by <paramref name="limit"/> visited nodes.
        /// </summary>
        public static int CountFixtures(long start, ISet<long> blocked,
            Func<long, IEnumerable<long>> neighbours,
            Func<long, SchematicPipeKind> kind,
            int limit = 2000)
        {
            if (neighbours == null || kind == null) return 0;
            var seen = new HashSet<long> { start };
            if (blocked != null) seen.UnionWith(blocked);
            var q = new Queue<long>();
            q.Enqueue(start);
            int count = 0, visited = 0;
            while (q.Count > 0 && visited < limit)
            {
                long id = q.Dequeue();
                visited++;
                var k = kind(id);
                if (k == SchematicPipeKind.Fixture) { count++; continue; }
                if (k == SchematicPipeKind.VentPipe) continue;
                foreach (var n in neighbours(id) ?? Enumerable.Empty<long>())
                    if (seen.Add(n)) q.Enqueue(n);
            }
            return count;
        }
    }

    /// <summary>
    /// Occupied-cell tracker for a grid layout. Two nodes of different parents used to
    /// be given the same (row, column) and drawn on top of each other; a claim now
    /// takes the nearest free column (preferred, +1, -1, +2, -2 …).
    /// </summary>
    public sealed class SchematicCellGrid
    {
        private readonly HashSet<(int Row, int Col)> _taken = new HashSet<(int, int)>();

        public bool IsOccupied(int row, int col) => _taken.Contains((row, col));

        public int Claim(int row, int preferredCol)
        {
            for (int step = 0; ; step++)
            {
                int right = preferredCol + step;
                if (_taken.Add((row, right))) return right;
                if (step == 0) continue;
                int left = preferredCol - step;
                if (_taken.Add((row, left))) return left;
            }
        }
    }
}
