// ClashGrouper.cs — collapse raw clashes into user-manageable groups.
// rec-13: Three-pass grouping (element-pattern → repetition → spatial) so a
//         single misrouted duct hitting 20 beams surfaces as ONE group keyed on
//         the duct ElementId, not 20 spatial singletons.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

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
            // Anything still ungrouped falls through to 2 m × 2 m × 3 m cells
            // keyed on matrix pair-id. Preserves the Stage 4 contract for the
            // common one-off clash case.
            var cellSize = new Vector3(2f, 2f, 3f);
            var byCell = new Dictionary<(long, long, long, string), List<ClashRecord>>();
            foreach (var c in ungrouped)
            {
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
            var byPairAndA = new Dictionary<(string, int), List<ClashRecord>>();
            var byPairAndB = new Dictionary<(string, int), List<ClashRecord>>();
            foreach (var c in clashes)
            {
                string pair = c.MatrixPairId ?? "";
                if (c.ElementA != null)
                {
                    var key = (pair, c.ElementA.ElementId);
                    if (!byPairAndA.TryGetValue(key, out var lst)) byPairAndA[key] = lst = new List<ClashRecord>();
                    lst.Add(c);
                }
                if (c.ElementB != null)
                {
                    var key = (pair, c.ElementB.ElementId);
                    if (!byPairAndB.TryGetValue(key, out var lst)) byPairAndB[key] = lst = new List<ClashRecord>();
                    lst.Add(c);
                }
            }

            var result = new List<PatternGroup>();
            var claimed = new HashSet<string>();   // track identity of claimed records
            // Prefer the larger bucket when a clash could join either A-anchor or B-anchor group.
            var candidates = byPairAndA.Select(kv => (kv, side: "A"))
                .Concat(byPairAndB.Select(kv => (kv, side: "B")))
                .OrderByDescending(x => x.kv.Value.Count);

            foreach (var (kv, side) in candidates)
            {
                if (kv.Value.Count < 3) continue;
                var members = kv.Value.Where(c => !claimed.Contains(c.Identity ?? "")).ToList();
                if (members.Count < 3) continue;

                var cat = side == "A" ? members.First().ElementA?.Category : members.First().ElementB?.Category;
                result.Add(new PatternGroup
                {
                    AnchorDescription = $"{kv.Key.Item1} via {side}={cat}:{kv.Key.Item2}",
                    Members = members,
                });
                foreach (var m in members) claimed.Add(m.Identity ?? "");
            }
            return result;
        }

        /// <summary>
        /// rec-13: Repetition pass. Groups remaining clashes with identical
        /// matrix pair-id and approximately equal AABB volumes that fall at
        /// roughly equal intervals on at least one axis. Common case: a riser
        /// hitting every floor slab in a stack — 5 clashes instead of 5
        /// individual spatial singletons.
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
                var list = bucket.OrderBy(c => c.Centroid[2]).ToList();   // sort by Z (stack axis)
                if (IsEquallySpaced(list, axis: 2, tolerance: 0.5f))
                {
                    result.Add(new PatternGroup
                    {
                        AnchorDescription = $"{bucket.Key.Item1} repetition (Z-stack, vol≈{bucket.Key.Item2})",
                        Members = list,
                    });
                }
            }
            return result;
        }

        private static int VolumeBucket(float volMm3)
        {
            // Log2 bucket: clashes within a ~2× volume band cluster together.
            if (volMm3 <= 1f) return 0;
            return (int)System.Math.Round(System.Math.Log2(volMm3));
        }

        private static bool IsEquallySpaced(List<ClashRecord> sorted, int axis, float tolerance)
        {
            if (sorted.Count < 3) return false;
            var deltas = new List<float>();
            for (int i = 1; i < sorted.Count; i++)
            {
                deltas.Add(sorted[i].Centroid[axis] - sorted[i - 1].Centroid[axis]);
            }
            float mean = deltas.Average();
            if (mean < 0.1f) return false;   // too-close centroids aren't a pattern
            foreach (var d in deltas)
            {
                if (System.Math.Abs(d - mean) > tolerance) return false;
            }
            return true;
        }
    }
}
