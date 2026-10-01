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
        /// True when a pipe from (0,0,0) to (dx,dy,dz) is more than 80 % vertical — a
        /// stack <em>candidate</em>, not yet a stack (see <see cref="StackRuns"/>).
        /// </summary>
        public static bool IsMostlyVertical(double dx, double dy, double dz)
        {
            double total = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return total > 1e-6 && Math.Abs(dz) / total > 0.8;
        }

        /// <summary>
        /// The stacks among mostly-vertical pipes; each returned list is one stack's ids.
        /// DTW-111: every vertical pipe used to be a stack — each WC tail, trap drop and
        /// vertical offset was drawn as a DN100 STACK, and because stacks block the walk
        /// that counts a branch's fixtures, a fixture behind a vertical tail was never
        /// counted. Now segments at one plan position whose heights meet (within
        /// <see cref="StackRule.JoinGapFt"/>, a fitting) form a run, and a run is a stack
        /// only when it is at least <see cref="StackRule.MinRunFt"/> tall and — when the
        /// document has levels — passes through at least one level.
        /// </summary>
        public static List<List<long>> StackRuns(
            IReadOnlyList<(long Id, double X, double Y, double ZMin, double ZMax)> verticals,
            IReadOnlyList<double> sortedLevelElevations,
            StackRule rule = null)
        {
            rule = rule ?? new StackRule();
            var runs = new List<List<long>>();
            if (verticals == null || verticals.Count == 0) return runs;

            var byId = new Dictionary<long, (double ZMin, double ZMax)>();
            foreach (var v in verticals)
                byId[v.Id] = (Math.Min(v.ZMin, v.ZMax), Math.Max(v.ZMin, v.ZMax));

            var planGroups = ClusterByPlanPosition(
                verticals.Select(v => (v.Id, v.X, v.Y)).ToList(), rule.PlanToleranceFt);

            foreach (var g in planGroups)
            {
                // Split the plan group into vertically contiguous runs.
                var ordered = g.OrderBy(id => byId[id].ZMin).ToList();
                var current = new List<long>();
                double runMin = 0, runMax = 0;
                foreach (var id in ordered)
                {
                    var (a, b) = byId[id];
                    if (current.Count > 0 && a > runMax + rule.JoinGapFt)
                    {
                        if (IsStackRun(runMin, runMax, sortedLevelElevations, rule)) runs.Add(current);
                        current = new List<long>();
                    }
                    if (current.Count == 0) { runMin = a; runMax = b; }
                    else { runMin = Math.Min(runMin, a); runMax = Math.Max(runMax, b); }
                    current.Add(id);
                }
                if (current.Count > 0 && IsStackRun(runMin, runMax, sortedLevelElevations, rule)) runs.Add(current);
            }
            return runs;
        }

        private static bool IsStackRun(double zMin, double zMax, IReadOnlyList<double> levels, StackRule rule)
        {
            if (zMax - zMin < rule.MinRunFt) return false;
            if (levels == null || levels.Count == 0) return true;
            foreach (var e in levels)
                if (e > zMin + rule.LevelToleranceFt && e < zMax - rule.LevelToleranceFt) return true;
            return false;
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
        /// True when a source of this rank is a guess: only a water meter, a tank or a pump
        /// set is known to feed the network. Other connected equipment (rank 3) or no
        /// source at all means the layout and any pressure from it are indicative (DTW-128).
        /// </summary>
        public static bool SupplySourceIsAssumed(int rank) => rank > 2;

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

    /// <summary>What makes a run of vertical pipe a stack (all lengths in feet).</summary>
    public sealed class StackRule
    {
        /// <summary>Segments within this plan distance are one vertical line.</summary>
        public double PlanToleranceFt { get; set; } = 150 / SchematicLayoutMath.MmPerFoot;
        /// <summary>A vertical gap up to this (a coupling, a tee, a cleanout) still joins two segments.</summary>
        public double JoinGapFt { get; set; } = 500 / SchematicLayoutMath.MmPerFoot;
        /// <summary>Shortest run that is a stack: about one storey.</summary>
        public double MinRunFt { get; set; } = 2000 / SchematicLayoutMath.MmPerFoot;
        /// <summary>A level within this distance of a run's end is not crossed by it.</summary>
        public double LevelToleranceFt { get; set; } = 50 / SchematicLayoutMath.MmPerFoot;
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

    /// <summary>A run of pass-through pipe and fittings between two nodes worth drawing.</summary>
    public sealed class ChainSegment
    {
        public long From { get; set; }
        public long To { get; set; }
        /// <summary>The nodes passed through, in order from <see cref="From"/>.</summary>
        public List<long> Through { get; } = new List<long>();
    }

    /// <summary>Outcome of fitting a schematic to its sheet slot.</summary>
    public sealed class SchematicFitResult
    {
        public int Scale { get; set; }
        /// <summary>False when no scale fits the slot (the drawing is larger than the slot).</summary>
        public bool Fits { get; set; }
        /// <summary>False when the slot size could not be read; the scale is then the minimum.</summary>
        public bool SlotKnown { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double SlotWidthMm { get; set; }
        public double SlotHeightMm { get; set; }

        /// <summary>One line for the report, or null when it fits.</summary>
        public string Problem()
        {
            if (!SlotKnown) return $"The sheet slot size could not be read; drawn at 1:{Scale} without a fit check.";
            if (Fits) return null;
            return $"The schematic ({WidthMm:F0} × {HeightMm:F0} mm at 1:{Scale}) is larger than its sheet slot "
                 + $"({SlotWidthMm:F0} × {SlotHeightMm:F0} mm) at any scale that keeps its labels readable — "
                 + "it overflows the sheet. Draw it per system (Named system…) to split it.";
        }
    }

    /// <summary>
    /// DTW-120: layout rules that keep a plumbing schematic on its sheet. The drainage
    /// riser used true elevation at a pinned 1:50, so more than 7–8 storeys or ~19
    /// stacks overflowed the A1 slot; the supply schematic gave every element its own
    /// 20 mm column. Floors are now storey rows at a fixed pitch, columns a fixed pitch,
    /// runs of pass-through pipe collapse to one line, and the scale is the smallest
    /// that fits the drawing type's slot.
    /// </summary>
    public static class SchematicFit
    {
        public static readonly int[] StandardScales = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 };

        /// <summary>
        /// Position of elevation <paramref name="z"/> in storeys: level i is row i, a
        /// height between two levels interpolates between their rows, and beyond the
        /// first / last level the adjacent storey's height carries on. With fewer than two
        /// distinct levels <paramref name="fallbackStoreyFt"/> stands in for the storey
        /// height (a display spacing only — no level is named from it).
        /// </summary>
        public static double StoreyRow(IReadOnlyList<double> sortedElevations, double z, double fallbackStoreyFt)
        {
            if (fallbackStoreyFt <= 0) fallbackStoreyFt = 1;
            var e = new List<double>();
            if (sortedElevations != null)
                foreach (var v in sortedElevations)
                    if (!double.IsNaN(v) && (e.Count == 0 || v - e[e.Count - 1] > 1e-3)) e.Add(v);
            if (e.Count == 0) return z / fallbackStoreyFt;
            if (e.Count == 1) return (z - e[0]) / fallbackStoreyFt;
            if (z <= e[0]) return (z - e[0]) / (e[1] - e[0]);
            int last = e.Count - 1;
            if (z >= e[last]) return last + (z - e[last]) / (e[last] - e[last - 1]);
            for (int i = 0; i < last; i++)
                if (z <= e[i + 1]) return i + (z - e[i]) / (e[i + 1] - e[i]);
            return last;
        }

        /// <summary>
        /// The smallest standard scale, not below <paramref name="minScale"/>, at which the
        /// paper extent fits the slot. When none fits, the scale that comes closest (the
        /// smallest scale at the least overflow) with <c>Fits = false</c>. A slot of
        /// unknown size (≤ 0) gives <paramref name="minScale"/> with <c>SlotKnown = false</c>.
        /// </summary>
        public static SchematicFitResult ChooseScale(Func<int, (double W, double H)> paperExtentMm,
            double slotWidthMm, double slotHeightMm, int minScale, int maxScale = 1000)
        {
            if (minScale <= 0) minScale = 1;
            var candidates = new List<int> { minScale };
            candidates.AddRange(StandardScales.Where(s => s > minScale && s <= Math.Max(minScale, maxScale)));

            if (paperExtentMm == null || slotWidthMm <= 0 || slotHeightMm <= 0)
            {
                var ext0 = paperExtentMm?.Invoke(minScale) ?? (0, 0);
                return new SchematicFitResult { Scale = minScale, Fits = true, SlotKnown = false,
                    WidthMm = ext0.W, HeightMm = ext0.H, SlotWidthMm = slotWidthMm, SlotHeightMm = slotHeightMm };
            }

            SchematicFitResult best = null;
            double bestRatio = double.MaxValue;
            foreach (int s in candidates)
            {
                var ext = paperExtentMm(s);
                var r = new SchematicFitResult { Scale = s, SlotKnown = true, WidthMm = ext.W, HeightMm = ext.H,
                    SlotWidthMm = slotWidthMm, SlotHeightMm = slotHeightMm };
                if (ext.W <= slotWidthMm && ext.H <= slotHeightMm) { r.Fits = true; return r; }
                double ratio = Math.Max(ext.W / slotWidthMm, ext.H / slotHeightMm);
                if (ratio < bestRatio - 1e-9) { bestRatio = ratio; best = r; }
            }
            return best;
        }

        /// <summary>
        /// Printed width of <paramref name="text"/> (its longest line), paper mm, for a
        /// text type of <paramref name="heightMm"/> and width factor. An estimate: 0.7 ×
        /// height per character covers capitals and digits in the usual sans fonts.
        /// </summary>
        public static double EstimateTextWidthMm(string text, double heightMm, double widthFactor = 1.0)
        {
            if (string.IsNullOrEmpty(text) || heightMm <= 0) return 0;
            if (widthFactor <= 0) widthFactor = 1.0;
            int longest = text.Replace("\r", "").Split('\n').Max(l => l.Length);
            return longest * heightMm * 0.7 * widthFactor;
        }

        /// <summary>Printed height of <paramref name="text"/>, paper mm (lines × 1.5 × height).</summary>
        public static double EstimateTextHeightMm(string text, double heightMm)
        {
            if (string.IsNullOrEmpty(text) || heightMm <= 0) return 0;
            int lines = text.Replace("\r", "").Split('\n').Length;
            return lines * heightMm * 1.5;
        }

        /// <summary>
        /// The network reachable from <paramref name="start"/>, with every run of
        /// pass-through nodes (those <paramref name="keep"/> rejects) collapsed into one
        /// segment between kept nodes. <paramref name="start"/> is always kept. Each
        /// segment is reported once.
        /// </summary>
        public static List<ChainSegment> CollapseChains(long start,
            Func<long, IEnumerable<long>> neighbours, Func<long, bool> keep, int limit = 200000)
        {
            var segs = new List<ChainSegment>();
            if (neighbours == null) return segs;
            bool Keep(long id) => id == start || keep == null || keep(id);
            IEnumerable<long> Nbrs(long id) => (neighbours(id) ?? Enumerable.Empty<long>()).Distinct().Where(n => n != id);

            var done = new HashSet<(long, long)>();   // (kept node, first step) already walked
            var queued = new HashSet<long> { start };
            var queue = new Queue<long>();
            queue.Enqueue(start);
            int steps = 0;
            while (queue.Count > 0 && steps < limit)
            {
                long u = queue.Dequeue();
                foreach (var v in Nbrs(u))
                {
                    if (!done.Add((u, v))) continue;
                    var seg = new ChainSegment { From = u };
                    long prev = u, cur = v;
                    var inWalk = new HashSet<long> { u };
                    while (!Keep(cur) && steps++ < limit)
                    {
                        if (!inWalk.Add(cur)) break;          // a loop of pass-through nodes
                        seg.Through.Add(cur);
                        long p = prev;
                        var next = Nbrs(cur).Where(n => n != p).ToList();
                        if (next.Count == 0) break;           // dead end (should be kept; guard)
                        prev = cur;
                        cur = next[0];
                    }
                    if (seg.Through.Count > 0 && seg.Through[seg.Through.Count - 1] == cur) seg.Through.RemoveAt(seg.Through.Count - 1);
                    seg.To = cur;
                    // The same run walked from its other end is not reported again.
                    done.Add((cur, seg.Through.Count > 0 ? seg.Through[seg.Through.Count - 1] : u));
                    if (seg.To != seg.From) segs.Add(seg);
                    if (Keep(cur) && queued.Add(cur)) queue.Enqueue(cur);
                }
            }
            return segs;
        }

        /// <summary>
        /// (row, column) of every node the segments reach from <paramref name="start"/>:
        /// row from <paramref name="rowOf"/>, column the parent's (a single child) or fanned
        /// out around it; no two nodes share a cell.
        /// </summary>
        public static Dictionary<long, (int Row, int Col)> LayoutTree(long start,
            IEnumerable<ChainSegment> segments, Func<long, int> rowOf)
        {
            var adj = new Dictionary<long, List<long>>();
            void Link(long a, long b)
            {
                if (!adj.TryGetValue(a, out var l)) adj[a] = l = new List<long>();
                if (!l.Contains(b)) l.Add(b);
            }
            foreach (var s in segments ?? Enumerable.Empty<ChainSegment>()) { Link(s.From, s.To); Link(s.To, s.From); }

            var cells = new Dictionary<long, (int Row, int Col)>();
            var grid = new SchematicCellGrid();
            var queue = new Queue<(long Id, int PrefCol)>();
            queue.Enqueue((start, 0));
            while (queue.Count > 0)
            {
                var (id, pref) = queue.Dequeue();
                if (cells.ContainsKey(id)) continue;
                int row = rowOf != null ? rowOf(id) : 0;
                int col = grid.Claim(row, pref);
                cells[id] = (row, col);
                var kids = adj.TryGetValue(id, out var l) ? l.Where(k => !cells.ContainsKey(k)).ToList() : new List<long>();
                for (int i = 0; i < kids.Count; i++)
                    queue.Enqueue((kids[i], kids.Count == 1 ? col : col + i - kids.Count / 2));
            }
            return cells;
        }
    }
}
