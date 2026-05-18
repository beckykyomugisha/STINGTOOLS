using System;
// HC-24: AGV / pneumatic-tube path optimiser.
//
// Upgrades the placeholder adjacency-BFS noted in HEALTHCARE_PACK_DESIGN §11 H-10 to
// a proper weighted-shortest-path solver. Models the building as a graph where:
//   * Nodes      = rooms / corridors / vertical-transport endpoints
//   * Edges      = travelable adjacencies, weighted by metres × speed-class
//   * Forbidden  = clinical-rule deny list (cannot route pneumatic tubes through
//                  sterile core, cannot route AGVs through patient-bedroom corridors
//                  during sleep hours)
//
// Returns the lowest-cost path plus a structured violation list so callers can
// surface clinical-rule conflicts (e.g. "route crosses sterile core at Lab 04 →
// flagged per HBN 15-01 §4.12").
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Routing
{
    public sealed record AgvNode(string Id, string RoomKind, double X, double Y, double Z);

    public sealed record AgvEdge(string FromId, string ToId, double LengthM, string Kind);
    // Kind: "corridor" | "lift" | "tube" | "ramp"

    public sealed record AgvConstraint(
        string DeniedRoomKind,
        string TransportKind,
        string Source);

    public sealed record AgvPathResult(
        IReadOnlyList<string> NodeIds,
        double TotalCostM,
        IReadOnlyList<string> Violations);

    public static class AgvPathOptimiser
    {
        public static AgvPathResult FindPath(
            IReadOnlyList<AgvNode> nodes,
            IReadOnlyList<AgvEdge> edges,
            IReadOnlyList<AgvConstraint> constraints,
            string startId,
            string goalId,
            string transportKind)
        {
            var nodeById = nodes.ToDictionary(n => n.Id);
            var adj = new Dictionary<string, List<AgvEdge>>();
            foreach (var n in nodes) adj[n.Id] = new List<AgvEdge>();
            foreach (var e in edges)
            {
                if (adj.ContainsKey(e.FromId)) adj[e.FromId].Add(e);
                if (adj.ContainsKey(e.ToId))
                    adj[e.ToId].Add(new AgvEdge(e.ToId, e.FromId, e.LengthM, e.Kind));
            }

            var deniedRoomKinds = constraints
                .Where(c => string.Equals(c.TransportKind, transportKind, System.StringComparison.OrdinalIgnoreCase))
                .Select(c => c.DeniedRoomKind)
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

            // Dijkstra
            var dist = nodes.ToDictionary(n => n.Id, _ => double.PositiveInfinity);
            var prev = new Dictionary<string, string>();
            dist[startId] = 0.0;
            var open = new SortedSet<(double Cost, string Id)>(Comparer<(double, string)>.Create((a, b) =>
            {
                int c = a.Item1.CompareTo(b.Item1);
                return c != 0 ? c : string.CompareOrdinal(a.Item2, b.Item2);
            }));
            open.Add((0.0, startId));

            while (open.Count > 0)
            {
                var cur = open.Min; open.Remove(cur);
                if (cur.Id == goalId) break;
                foreach (var e in adj[cur.Id])
                {
                    if (!nodeById.TryGetValue(e.ToId, out var nb)) continue;
                    var nxt = cur.Cost + e.LengthM;
                    if (nxt < dist[nb.Id])
                    {
                        open.Remove((dist[nb.Id], nb.Id));
                        dist[nb.Id] = nxt;
                        prev[nb.Id] = cur.Id;
                        open.Add((nxt, nb.Id));
                    }
                }
            }

            if (double.IsPositiveInfinity(dist[goalId]))
                return new AgvPathResult(System.Array.Empty<string>(), double.PositiveInfinity,
                    new[] { $"No path from {startId} to {goalId} for {transportKind}." });

            var path = new List<string>();
            for (var n = goalId; n != null; )
            {
                path.Add(n);
                if (!prev.TryGetValue(n, out var p)) break;
                n = p;
            }
            path.Reverse();

            var violations = new List<string>();
            foreach (var id in path)
            {
                if (!nodeById.TryGetValue(id, out var nd)) continue;
                if (deniedRoomKinds.Contains(nd.RoomKind))
                    violations.Add($"Path crosses denied room kind '{nd.RoomKind}' at node {id} (transport={transportKind}).");
            }

            return new AgvPathResult(path, dist[goalId], violations);
        }
    }
}
