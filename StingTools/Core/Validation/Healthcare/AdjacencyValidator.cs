using StingTools.Core.Validation;
using StingTools.Core.Adjacency;
using System;
using Autodesk.Revit.DB;
using StingTools.Standards.HBN;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    /// <summary>HBN-derived adjacency targets — flags when mandatory adjacencies
    /// are violated or forbidden adjacencies appear. Targets are loaded live from
    /// HEALTHCARE_ADJACENCY_HBN.csv (HC-DEF-08) and rooms are matched against them
    /// by BOTH canonical room-class code and RoomClassCodes department, so a room
    /// tagged e.g. IMG-CT satisfies a rule keyed on the IMAGING department.
    /// Proximity is measured by a door/room-graph BFS ("N doors apart", HC-DEF-04)
    /// via RoomGraphBuilder, falling back to a labelled room-centroid distance where
    /// no usable door graph exists.</summary>
    public class AdjacencyValidator : HealthcareValidatorBase
    {
        public override string Name => "AdjacencyValidator";
        private const string Tag = "AdjacencyValidator";

        // Centroid-fallback thresholds (m), used only when no usable door graph
        // exists for a room. Wrapping command sets these from HcOptions.AdjacencyDepth.
        public double MaxMandatoryDistanceM { get; set; } = 30.0;
        public double MinForbiddenDistanceM { get; set; } = 30.0;

        // Door-graph BFS hop cap (HC-DEF-04). Bounds the NearestHops search; the
        // wrapping command sets it from HcOptions.AdjacencyDepth.
        public int BfsDepth { get; set; } = 3;

        public override List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            // Group clinical rooms under BOTH their canonical room-class code and
            // their RoomClassCodes department, so adjacency keys expressed either
            // way (IMAGING department vs HSDU-P canonical code) both resolve.
            var table = RoomClassCodes.Get(doc);
            var byKey = new Dictionary<string, List<Element>>(System.StringComparer.OrdinalIgnoreCase);
            void AddKey(string key, Element r)
            {
                if (string.IsNullOrEmpty(key)) return;
                if (!byKey.TryGetValue(key, out var list)) { list = new List<Element>(); byKey[key] = list; }
                if (!list.Contains(r)) list.Add(r);
            }
            foreach (var r in GetClinicalRoomsCached(doc))
            {
                var canon = GetRoomClassCached(r);          // canonical code
                if (string.IsNullOrEmpty(canon)) continue;
                AddKey(canon, r);
                AddKey(table.DepartmentOf(canon), r);       // department (deduped inside AddKey)
            }
            if (byKey.Count < 2) return res;

            // HC-DEF-04 — prefer a real door/room-graph BFS ("N doors apart") over the
            // centroid heuristic, which false-flags corridor-connected rooms. The graph
            // is built once; where it has no door edges (or a room is isolated) we fall
            // back to the labelled centroid distance so the check still runs.
            var graph = RoomGraphBuilder.Build(doc);
            bool graphUsable = graph.Adj.Values.Any(s => s.Count > 0);

            foreach (var t in AdjacencyTargetsRegistry.Get(doc))
            {
                var a = t.A; var b = t.B;
                var target = t.Target;
                if (!byKey.TryGetValue(a, out var aRooms)) continue;
                if (!byKey.TryGetValue(b, out var bRooms)) continue;
                var bIds = new HashSet<long>(bRooms.Select(r => r.Id.Value));

                foreach (var ar in aRooms)
                {
                    long arId = ar.Id.Value;
                    bool useBfs = graphUsable && graph.Adj.TryGetValue(arId, out var nb) && nb.Count > 0;

                    if (useBfs)
                    {
                        int hops = NearestHops(graph, arId, bIds, BfsDepth + 1);
                        // hops is the door-count to the nearest DISTINCT b-room; int.MaxValue
                        // when none is reachable within the cap.
                        if (target == 2 && hops > 1)
                            res.Add(new ValidationResult(ar.Id, ValidationSeverity.Warning,
                                "ADJ.MANDATORY_FAR",
                                $"{a} (room {ar.Name}) is {HopText(hops)} from nearest {b} — HBN expects direct adjacency",
                                Tag));
                        else if (target == 1 && hops > 3)
                            res.Add(new ValidationResult(ar.Id, ValidationSeverity.Info,
                                "ADJ.PREFERRED_FAR",
                                $"{a} (room {ar.Name}) is {HopText(hops)} from nearest {b} — HBN prefers within 3 doors",
                                Tag));
                        else if (target == 0 && hops <= 2)
                            res.Add(new ValidationResult(ar.Id, ValidationSeverity.Warning,
                                "ADJ.FORBIDDEN_NEAR",
                                $"{a} (room {ar.Name}) is only {HopText(hops)} from {b} — HBN forbids close adjacency",
                                Tag));
                    }
                    else
                    {
                        // Centroid fallback (labelled) — no usable door graph for this room.
                        var ap = (ar.Location as LocationPoint)?.Point;
                        if (ap == null) continue;
                        double minDist = double.MaxValue;
                        foreach (var br in bRooms)
                        {
                            if (ReferenceEquals(ar, br)) continue;   // a room can sit in both buckets
                            var bp = (br.Location as LocationPoint)?.Point;
                            if (bp == null) continue;
                            var d = (bp - ap).GetLength() * 0.3048; // ft → m
                            if (d < minDist) minDist = d;
                        }
                        if (minDist == double.MaxValue) continue;

                        if (target == 2 && minDist > MaxMandatoryDistanceM)
                            res.Add(new ValidationResult(ar.Id, ValidationSeverity.Warning,
                                "ADJ.MANDATORY_FAR",
                                $"{a} (room {ar.Name}) is {minDist:F0} m from nearest {b} (centroid — no door graph) — HBN expects direct adjacency",
                                Tag));
                        else if (target == 0 && minDist < MinForbiddenDistanceM)
                            res.Add(new ValidationResult(ar.Id, ValidationSeverity.Warning,
                                "ADJ.FORBIDDEN_NEAR",
                                $"{a} (room {ar.Name}) is only {minDist:F0} m from {b} (centroid — no door graph) — HBN forbids close adjacency",
                                Tag));
                    }
                }
            }
            return res;
        }

        // Multi-target BFS over the door graph: fewest doors from <c>start</c> to any
        // room in <c>targets</c> (excluding start itself), capped at <c>maxDepth</c>.
        // Returns int.MaxValue when no target is reachable within the cap.
        private static int NearestHops(RoomGraph g, long start, HashSet<long> targets, int maxDepth)
        {
            var seen = new HashSet<long> { start };
            var queue = new Queue<(long id, int depth)>();
            queue.Enqueue((start, 0));
            while (queue.Count > 0)
            {
                var (id, depth) = queue.Dequeue();
                if (depth >= maxDepth) continue;
                foreach (var n in g.Neighbours(id))
                {
                    if (!seen.Add(n)) continue;
                    if (targets.Contains(n)) return depth + 1;
                    queue.Enqueue((n, depth + 1));
                }
            }
            return int.MaxValue;
        }

        private static string HopText(int hops) =>
            hops == int.MaxValue ? "> graph range" : $"{hops} door{(hops == 1 ? "" : "s")}";
    }
}
