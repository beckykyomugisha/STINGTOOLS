// ClashGrouper.cs — collapse raw clashes into user-manageable groups.
// rec-13: Three-pass grouping (element-pattern → repetition → spatial) so a
//         single misrouted duct hitting 20 beams surfaces as ONE group keyed on
//         the duct ElementId, not 20 spatial singletons.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Clash
{
    public static class ClashGrouper
    {
        public static List<ClashGroupRecord> Group(List<ClashRecord> clashes)
        {
            if (clashes == null || clashes.Count == 0) return new List<ClashGroupRecord>();

            var groups = new List<ClashGroupRecord>();
            int gid = 1;

            // ── Pass 1: element-pattern grouping ─────────────────────────────
            // A single element (A or B) involved in ≥ 3 clashes of the same
            // matrix pair-id becomes a group anchored on that element id. Common
            // case: one duct run clashing with every beam in a bay.
            var ungrouped = new List<ClashRecord>(clashes);
            var elementPatternGroups = FindElementPatternGroups(ungrouped);
            foreach (var pg in elementPatternGroups)
            {
                string groupId = $"GRP-{gid:D5}";
                foreach (var c in pg.Members) { c.GroupId = groupId; ungrouped.Remove(c); }
                groups.Add(new ClashGroupRecord
                {
                    Id = groupId,
                    Kind = "element",
                    Anchor = pg.AnchorDescription,
                    Size = pg.Members.Count,
                    Status = "Open",
                });
                gid++;
            }

            // ── Pass 2: repetition grouping ──────────────────────────────────
            // Remaining clashes with the same (matrix pair-id, AABB size bucket)
            // arranged at roughly-equal intervals on one axis get folded into a
            // single group. Common case: service hitting every floor in a shaft.
            var repetitionGroups = FindRepetitionGroups(ungrouped);
            foreach (var rg in repetitionGroups)
            {
                string groupId = $"GRP-{gid:D5}";
                foreach (var c in rg.Members) { c.GroupId = groupId; ungrouped.Remove(c); }
                groups.Add(new ClashGroupRecord
                {
                    Id = groupId,
                    Kind = "pattern",
                    Anchor = rg.AnchorDescription,
                    Size = rg.Members.Count,
                    Status = "Open",
                });
                gid++;
            }

            // ── Pass 3: spatial grouping (original behaviour) ────────────────
            // E5: Adaptive cell size per matrix pair. Prior code used a fixed
            //     2×2×3m grid which clusters small light fixtures awkwardly
            //     (5+ per cell) and AHU-vs-beam clashes too sparsely (1 per
            //     cell). Now: cell size = average element AABB diagonal × 1.5
            //     per pair-id, clamped to [0.5m..6m]. Coordinator-relevant
            //     groupings emerge: lights cluster at 1m, AHU/beam at 4-6m.
            var cellSizeByPair = ComputeAdaptiveCellSizes(ungrouped);
            var byCell = new Dictionary<(long, long, long, string), List<ClashRecord>>();
            foreach (var c in ungrouped)
            {
                if (!cellSizeByPair.TryGetValue(c.MatrixPairId ?? "", out var cellSize))
                    cellSize = new Vector3(2f, 2f, 3f);
                long cx = (long)(c.Centroid[0] / cellSize.X);
                long cy = (long)(c.Centroid[1] / cellSize.Y);
                long cz = (long)(c.Centroid[2] / cellSize.Z);
                var key = (cx, cy, cz, c.MatrixPairId);
                if (!byCell.TryGetValue(key, out var list))
                {
                    list = new List<ClashRecord>();
                    byCell[key] = list;
                }
                list.Add(c);
            }
            foreach (var kv in byCell)
            {
                string groupId = $"GRP-{gid:D5}";
                foreach (var c in kv.Value) c.GroupId = groupId;
                groups.Add(new ClashGroupRecord
                {
                    Id = groupId,
                    Kind = "spatial",
                    Anchor = kv.Key.Item4,
                    Size = kv.Value.Count,
                    Status = "Open"
                });
                gid++;
            }
            return groups;
        }

        private sealed class PatternGroup
        {
            public string AnchorDescription;
            public List<ClashRecord> Members = new List<ClashRecord>();
        }

        /// <summary>
        /// rec-13: Element-pattern pass. Groups by (matrix pair-id, element A id OR
        /// element B id) where the same element appears ≥ 3 times. The element
        /// appearing most often is the anchor ("DUCT:STR_BEAM clashes for Duct 12345").
        /// </summary>
        private static List<PatternGroup> FindElementPatternGroups(List<ClashRecord> clashes)
        {
            // D9: Single-pass dictionary keyed on (pair, side, elementId).
            //     Prior code built two parallel dictionaries (byPairAndA,
            //     byPairAndB) then concatenated them — twice the dictionary
            //     work, twice the bucket allocations. One dict, one pass.
            var byPairSideEid = new Dictionary<(string Pair, string Side, int Eid), List<ClashRecord>>();
            foreach (var c in clashes)
            {
                string pair = c.MatrixPairId ?? "";
                if (c.ElementA != null)
                {
                    var key = (pair, "A", c.ElementA.ElementId);
                    if (!byPairSideEid.TryGetValue(key, out var lst))
                        byPairSideEid[key] = lst = new List<ClashRecord>();
                    lst.Add(c);
                }
                if (c.ElementB != null)
                {
                    var key = (pair, "B", c.ElementB.ElementId);
                    if (!byPairSideEid.TryGetValue(key, out var lst))
                        byPairSideEid[key] = lst = new List<ClashRecord>();
                    lst.Add(c);
                }
            }

            var result = new List<PatternGroup>();
            var claimed = new HashSet<string>();   // track identity of claimed records
            // G9: Order candidates by size DESCENDING, then by key ASCENDING, so
            // the output is deterministic regardless of Dictionary enumeration
            // order (which is non-deterministic across runtime versions and
            // insertion histories).
            var candidates = byPairSideEid
                .Select(kv => (Key: kv.Key, Members: kv.Value))
                .OrderByDescending(x => x.Members.Count)
                .ThenBy(x => x.Key.Side, System.StringComparer.Ordinal)
                .ThenBy(x => x.Key.Pair, System.StringComparer.Ordinal)
                .ThenBy(x => x.Key.Eid);

            foreach (var entry in candidates)
            {
                if (entry.Members.Count < 3) continue;   // early reject: even pre-claim is too small
                var members = entry.Members.Where(c => !claimed.Contains(c.Identity ?? "")).ToList();
                if (members.Count < 3) continue;    // G9: post-claim count gate

                var anchorMember = members
                    .OrderBy(m => m.Identity ?? "", System.StringComparer.Ordinal)
                    .First();
                var cat = entry.Key.Side == "A" ? anchorMember.ElementA?.Category : anchorMember.ElementB?.Category;
                result.Add(new PatternGroup
                {
                    AnchorDescription = $"{entry.Key.Pair} via {entry.Key.Side}={cat}:{entry.Key.Eid}",
                    Members = members,
                });
                foreach (var m in members) claimed.Add(m.Identity ?? "");
            }
            return result;
        }

        /// <summary>
        /// rec-13: Repetition pass. Groups remaining clashes with identical
        /// matrix pair-id and approximately equal AABB volumes that fall at
        /// roughly equal intervals on at least one axis (X, Y, or Z).
        ///
        /// A5: All three axes are now evaluated; the axis with the smallest
        /// mean-deviation wins. Prior code only checked Z, breaking horizontal
        /// risers clashing with wall framing in X or Y into spatial singletons.
        /// Common cases now caught:
        ///   - vertical riser hitting every floor (Z-stack)
        ///   - horizontal main run hitting every joist (Y or X)
        /// </summary>
        private static List<PatternGroup> FindRepetitionGroups(List<ClashRecord> clashes)
        {
            var result = new List<PatternGroup>();
            if (clashes.Count < 3) return result;

            // Bucket by (matrix pair, AABB volume rounded to nearest log bin).
            var byPairAndVolBucket = clashes.GroupBy(c =>
                (c.MatrixPairId ?? "", VolumeBucket(c.VolumeMm3)));

            foreach (var bucket in byPairAndVolBucket)
            {
                if (bucket.Count() < 3) continue;
                var members = bucket.ToList();

                // A5: Try all three axes; pick the smallest mean-deviation.
                // E6: Tolerance is now relative to the spacing — `0.1 * mean`
                //     so 1ft-spaced lights tolerate 0.1ft jitter, 10ft beam
                //     centres tolerate 1ft. Floor of 0.05ft (~15mm) for
                //     extremely tight packs.
                int bestAxis = -1;
                float bestDev = float.MaxValue;
                List<ClashRecord> bestSorted = null;
                for (int axis = 0; axis < 3; axis++)
                {
                    var sorted = members.OrderBy(c => c.Centroid[axis]).ToList();
                    if (!TryComputeMeanDev(sorted, axis, relativeTolerance: 0.1f, minToleranceFt: 0.05f, out float dev)) continue;
                    if (dev < bestDev) { bestDev = dev; bestAxis = axis; bestSorted = sorted; }
                }
                if (bestAxis < 0 || bestSorted == null) continue;

                string axisLabel = bestAxis == 0 ? "X-row" : bestAxis == 1 ? "Y-row" : "Z-stack";
                result.Add(new PatternGroup
                {
                    AnchorDescription = $"{bucket.Key.Item1} repetition ({axisLabel}, vol≈{bucket.Key.Item2})",
                    Members = bestSorted,
                });
            }
            return result;
        }

        /// <summary>
        /// A5 / E6: Returns the mean deviation when the points are equally
        /// spaced (within tolerance), else false. Tolerance is now relative
        /// to the spacing — works for both tight light grids and wide beam
        /// centres without per-pair tuning.
        /// </summary>
        private static bool TryComputeMeanDev(List<ClashRecord> sorted, int axis,
            float relativeTolerance, float minToleranceFt, out float meanDev)
        {
            meanDev = float.MaxValue;
            if (sorted == null || sorted.Count < 3) return false;
            var deltas = new List<float>();
            for (int i = 1; i < sorted.Count; i++)
            {
                deltas.Add(sorted[i].Centroid[axis] - sorted[i - 1].Centroid[axis]);
            }
            float mean = deltas.Average();
            if (mean < 0.1f) return false;     // too-close centroids aren't a pattern
            float tolerance = System.Math.Max(minToleranceFt, mean * relativeTolerance);
            float devSum = 0f;
            foreach (var d in deltas)
            {
                float diff = System.Math.Abs(d - mean);
                if (diff > tolerance) return false;
                devSum += diff;
            }
            meanDev = devSum / deltas.Count;
            return true;
        }

        private static int VolumeBucket(float volMm3)
        {
            // Log2 bucket: clashes within a ~2× volume band cluster together.
            if (volMm3 <= 1f) return 0;
            return (int)System.Math.Round(System.Math.Log2(volMm3));
        }

        /// <summary>
        /// E5: Average AABB diagonal × 1.5 per matrix pair-id, clamped to
        /// [0.5m..6m]. Pairs with no AABB data fall back to the original
        /// 2×2×3m grid. Returns feet-units cell size to match Centroid units.
        /// </summary>
        private static Dictionary<string, Vector3> ComputeAdaptiveCellSizes(List<ClashRecord> clashes)
        {
            var byPair = new Dictionary<string, (double sumDiag, int n)>();
            foreach (var c in clashes)
            {
                if (c.AabbMin == null || c.AabbMax == null) continue;
                if (c.AabbMin.Length < 3 || c.AabbMax.Length < 3) continue;
                double dx = c.AabbMax[0] - c.AabbMin[0];
                double dy = c.AabbMax[1] - c.AabbMin[1];
                double dz = c.AabbMax[2] - c.AabbMin[2];
                double diag = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                string pid = c.MatrixPairId ?? "";
                if (!byPair.TryGetValue(pid, out var v)) v = (0, 0);
                byPair[pid] = (v.sumDiag + diag, v.n + 1);
            }
            var result = new Dictionary<string, Vector3>(byPair.Count);
            // Clamp range converted from m to ft (1m ≈ 3.281 ft).
            const float minFt = 0.5f / 0.3048f;   // 0.5m floor
            const float maxFt = 6.0f / 0.3048f;   // 6m  ceiling
            foreach (var kv in byPair)
            {
                if (kv.Value.n == 0) continue;
                float avgDiag = (float)(kv.Value.sumDiag / kv.Value.n);
                float cell = System.Math.Min(maxFt, System.Math.Max(minFt, avgDiag * 1.5f));
                // Z slightly larger than XY (typical building geometry has
                // taller storeys than rooms are deep).
                result[kv.Key] = new Vector3(cell, cell, cell * 1.5f);
            }
            return result;
        }
    }
}
