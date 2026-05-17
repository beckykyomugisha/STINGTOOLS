// StingTools v4 MVP — NetworkExtractor.
//
// Converts a Revit pipe selection into a HardyCrossSolver-consumable
// (NetworkPipe, NetworkLoop) pair. Method:
//
//   1. Enumerate selected Pipe elements.
//   2. Build a node lookup keyed by (X,Y,Z) rounded to 1 mm; nodes
//      get a synthetic name N_<hash>.
//   3. For each pipe, NodeA = connector-1 origin, NodeB = connector-2
//      origin; map both to node names; populate length + diameter +
//      assumed flow (from Pipe.FlowParam when set, else 0).
//   4. Find independent cycles via DFS on the adjacency graph
//      (classic "fundamental cycles via spanning tree" algorithm).
//   5. Emit NetworkLoop per cycle with signed member list.
//
// Rough cycle detection is sufficient for Hardy Cross: the solver
// balances head-loss around each loop independently, and using any
// linearly-independent basis of cycles converges to the same
// solution.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Calc
{
    public class NetworkExtraction
    {
        public List<NetworkPipe> Pipes { get; } = new List<NetworkPipe>();
        public List<NetworkLoop> Loops { get; } = new List<NetworkLoop>();
        public Dictionary<string, ElementId> PipeIdByNetworkId { get; } = new Dictionary<string, ElementId>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class NetworkExtractor
    {
        private const double FtToM = 0.3048;
        private const double NodeSnapMm = 1.0;

        public static NetworkExtraction Extract(Document doc, IEnumerable<Pipe> pipes)
        {
            var result = new NetworkExtraction();
            if (doc == null || pipes == null) return result;

            // Node dictionary keyed by a hashed XYZ so pipes meeting at
            // the same fitting share the same node name.
            var nodes = new Dictionary<(int x, int y, int z), string>();
            int nodeSeq = 0;
            string NodeFor(XYZ p)
            {
                var key = (
                    (int)Math.Round(p.X * 304.8 / NodeSnapMm),
                    (int)Math.Round(p.Y * 304.8 / NodeSnapMm),
                    (int)Math.Round(p.Z * 304.8 / NodeSnapMm));
                if (!nodes.TryGetValue(key, out var name))
                {
                    name = $"N{nodeSeq++:D4}";
                    nodes[key] = name;
                }
                return name;
            }

            // Adjacency for cycle detection (node → list of pipe-ids).
            var adjacency = new Dictionary<string, List<(string pipeId, string otherNode)>>();
            void AddAdj(string a, string b, string pid)
            {
                if (!adjacency.TryGetValue(a, out var list))
                {
                    list = new List<(string, string)>();
                    adjacency[a] = list;
                }
                list.Add((pid, b));
            }

            int pipeSeq = 0;
            foreach (var pipe in pipes)
            {
                try
                {
                    var curve = (pipe.Location as LocationCurve)?.Curve;
                    if (curve == null) continue;
                    var a = curve.GetEndPoint(0);
                    var b = curve.GetEndPoint(1);
                    string nA = NodeFor(a);
                    string nB = NodeFor(b);
                    if (nA == nB) continue; // zero-length

                    string pid = $"P{pipeSeq++:D4}";
                    var np = new NetworkPipe
                    {
                        Id        = pid,
                        NodeA     = nA,
                        NodeB     = nB,
                        LengthM   = curve.Length * FtToM,
                        DiameterM = pipe.Diameter * FtToM,
                        FlowM3S   = ReadFlowM3S(pipe),
                    };
                    result.Pipes.Add(np);
                    result.PipeIdByNetworkId[pid] = pipe.Id;
                    AddAdj(nA, nB, pid);
                    AddAdj(nB, nA, pid);
                }
                catch (Exception ex)
                { result.Warnings.Add($"Pipe {pipe?.Id}: {ex.Message}"); }
            }

            if (result.Pipes.Count == 0) return result;

            // Fundamental-cycle basis via DFS. Walk every node;
            // maintain spanning tree; when an edge closes a cycle,
            // backtrack parents to collect the loop members.
            var visited = new HashSet<string>();
            var parent  = new Dictionary<string, (string prevNode, string pipeId)>();
            var cycles  = new List<List<(string pipeId, string from, string to)>>();

            foreach (var startNode in adjacency.Keys)
            {
                if (visited.Contains(startNode)) continue;
                var stack = new Stack<string>();
                stack.Push(startNode);
                visited.Add(startNode);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (!adjacency.TryGetValue(cur, out var neighbours)) continue;
                    foreach (var (pid, other) in neighbours)
                    {
                        // Ignore the edge we came in by.
                        if (parent.TryGetValue(cur, out var par) && par.pipeId == pid) continue;

                        if (!visited.Contains(other))
                        {
                            visited.Add(other);
                            parent[other] = (cur, pid);
                            stack.Push(other);
                        }
                        else if (parent.ContainsKey(cur) || other != startNode)
                        {
                            // Back edge → close a cycle. Walk both
                            // sides back to common ancestor.
                            var cycle = ExtractCycle(parent, cur, other, pid);
                            if (cycle.Count >= 2) cycles.Add(cycle);
                        }
                    }
                }
            }

            // De-duplicate cycles (forward/reverse direction + node rotations).
            var seen = new HashSet<string>();
            foreach (var cyc in cycles)
            {
                var loop = new NetworkLoop { Id = $"L{result.Loops.Count:D3}" };
                foreach (var (pid, from, to) in cyc)
                {
                    var pipe = result.Pipes.FirstOrDefault(p => p.Id == pid);
                    if (pipe == null) continue;
                    int sign = (pipe.NodeA == from && pipe.NodeB == to) ? +1 : -1;
                    loop.Members.Add((pid, sign));
                }
                if (loop.Members.Count < 2) continue;
                string sig = string.Join(",", loop.Members.Select(m => m.PipeId).OrderBy(s => s));
                if (seen.Contains(sig)) continue;
                seen.Add(sig);
                result.Loops.Add(loop);
            }

            return result;
        }

        private static List<(string pipeId, string from, string to)> ExtractCycle(
            Dictionary<string, (string prevNode, string pipeId)> parent,
            string a, string b, string closingPid)
        {
            // Walk both nodes back to their roots, tag visited on
            // either side; the first node seen by both walks is the
            // lowest common ancestor.
            var pathA = new List<string> { a };
            var cur = a;
            while (parent.TryGetValue(cur, out var p)) { pathA.Add(p.prevNode); cur = p.prevNode; }
            var pathB = new List<string> { b };
            cur = b;
            while (parent.TryGetValue(cur, out var p)) { pathB.Add(p.prevNode); cur = p.prevNode; }

            var setB = new HashSet<string>(pathB);
            string lca = null;
            int idxAlca = -1;
            for (int i = 0; i < pathA.Count; i++)
            {
                if (setB.Contains(pathA[i])) { lca = pathA[i]; idxAlca = i; break; }
            }
            if (lca == null) return new List<(string, string, string)>();
            int idxBlca = pathB.IndexOf(lca);

            var cycle = new List<(string pipeId, string from, string to)>();
            // A-side: walk pathA[0..idxAlca] using their parent pipe ids.
            for (int i = 0; i < idxAlca; i++)
            {
                var fromN = pathA[i];
                var toN   = pathA[i + 1];
                if (!parent.TryGetValue(fromN, out var pf)) continue;
                cycle.Add((pf.pipeId, fromN, toN));
            }
            // Closing edge: a → b via closingPid.
            cycle.Add((closingPid, a, b));
            // B-side reversed: walk pathB[idxBlca..0].
            for (int i = idxBlca; i > 0; i--)
            {
                var fromN = pathB[i];
                var toN   = pathB[i - 1];
                if (!parent.TryGetValue(toN, out var pf)) continue;
                cycle.Add((pf.pipeId, fromN, toN));
            }
            return cycle;
        }

        private static double ReadFlowM3S(Pipe pipe)
        {
            try
            {
                var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);
                if (p != null)
                {
                    // Internal unit ft³/s → m³/s.
                    return p.AsDouble() * 0.028316846592;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0.0;
        }
    }
}
