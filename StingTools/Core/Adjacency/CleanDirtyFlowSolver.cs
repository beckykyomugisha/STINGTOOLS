// Healthcare Pack H-10 — Clean-dirty flow analysis.
// BFS over RoomGraph from each "DECON-D" / "MORT" room — flags any path
// that re-enters a clean care zone (OR, ICU, WARD-INPT, HSDU-P) before
// hitting a designated buffer (DECON-C, ANTERM, HSDU-W).

using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Adjacency
{
    public class FlowFinding
    {
        public int FromRoomId;
        public int ToRoomId;
        public string FromClass;
        public string ToClass;
        public string Severity;
        public string Code;
        public string Message;
    }

    public static class CleanDirtyFlowSolver
    {
        private static readonly HashSet<string> DirtySources = new(System.StringComparer.OrdinalIgnoreCase)
            { "DECON-D", "MORT", "POST", "HSDU-W" };
        private static readonly HashSet<string> CleanZones = new(System.StringComparer.OrdinalIgnoreCase)
            { "OR-CONV", "OR-HYBRID", "OR-ULTRA", "ICU", "WARD-INPT", "HSDU-P", "PH-CSP-797", "NICU", "PE-PROT" };
        private static readonly HashSet<string> Buffers = new(System.StringComparer.OrdinalIgnoreCase)
            { "DECON-C", "ANTERM", "HSDU-W" };

        public static List<FlowFinding> Audit(Document doc)
        {
            var findings = new List<FlowFinding>();
            if (doc == null) return findings;
            var g = RoomGraphBuilder.Build(doc);

            string ClassOf(int rid) => g.Rooms.TryGetValue(rid, out var r) ? GetParam(r, "CLN_ROOM_CLASS_TXT") : "";

            foreach (var (rid, room) in g.Rooms)
            {
                var rc = ClassOf(rid);
                if (string.IsNullOrEmpty(rc) || !DirtySources.Contains(rc)) continue;

                // BFS up to depth 3.
                var queue = new Queue<(int id, int depth, bool bufferSeen)>();
                queue.Enqueue((rid, 0, false));
                var seen = new HashSet<int> { rid };
                while (queue.Count > 0)
                {
                    var (id, depth, buffered) = queue.Dequeue();
                    if (depth >= 3) continue;
                    foreach (var n in g.Neighbours(id))
                    {
                        if (!seen.Add(n)) continue;
                        var nrc = ClassOf(n);
                        var newBuffered = buffered || Buffers.Contains(nrc);
                        if (CleanZones.Contains(nrc) && !newBuffered)
                        {
                            findings.Add(new FlowFinding
                            {
                                FromRoomId = rid, ToRoomId = n,
                                FromClass = rc, ToClass = nrc,
                                Severity = "ERROR",
                                Code = "FLOW.CLEAN_DIRTY_CROSS",
                                Message = $"Clean zone {nrc} ({g.Rooms[n].Name}) reachable from dirty room {rc} ({room.Name}) without buffer at depth {depth+1}"
                            });
                        }
                        queue.Enqueue((n, depth + 1, newBuffered));
                    }
                }
            }
            return findings;
        }

        private static string GetParam(Element el, string name)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null || !p.HasValue) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                return p.AsValueString() ?? "";
            } catch { return ""; }
        }
    }
}
