// RevitPipeNetworkBuilder — turns connected pipework into a PipeNetwork graph.
//
// Unlike RevitFlowTreeBuilder this keeps every connection, so a gridded
// sprinkler system or a gas ring main is described as it is built.
//
//   * Every connection between two elements is a junction, placed at the
//     connector (its elevation is the connector's).
//   * Pipes and two-port fittings / valves become links between their two
//     junctions. A pipe with a free end gets its own free-end junction.
//   * Multi-port fittings (tees, crosses), terminals and the source become
//     nodes: their connection junctions are merged into one. A multi-port
//     fitting's equivalent length is shared among the links it joins.
//   * Dead ends (closed branches, un-selected heads' arm-overs) are pruned.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Mep.Networks
{
    public sealed class PipeNetworkBuildResult
    {
        public PipeNetwork Network { get; } = new PipeNetwork();
        public List<long> UnreachedTerminals { get; } = new List<long>();
        public int PrunedNodes { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class RevitPipeNetworkBuilder
    {
        private const double FtToM = 0.3048;

        public static PipeNetworkBuildResult Build(Document doc, Element source, FlowTreeBuildOptions opts,
            Action<Element, NetNode> readTerminal)
        {
            var r = new PipeNetworkBuildResult();
            if (doc == null || source == null || opts == null) { r.Warnings.Add("No source element."); return r; }

            // 1. Walk every connection reachable from the source.
            var visited = new HashSet<long> { source.Id.Value };
            var edges = new HashSet<(long, long)>();
            var queue = new Queue<Element>();
            queue.Enqueue(source);
            while (queue.Count > 0 && visited.Count < opts.MaxElements)
            {
                var el = queue.Dequeue();
                if (opts.TerminalIds.Contains(el.Id.Value) && el.Id != source.Id) continue;
                foreach (var nb in RevitFlowTreeBuilder.Neighbours(el))
                {
                    long id = nb.Id.Value;
                    if (!visited.Contains(id))
                    {
                        if (!RevitFlowTreeBuilder.Traversable(nb) && !opts.TerminalIds.Contains(id))
                        {
                            if (opts.TerminalPredicate == null || !opts.TerminalPredicate(nb)) continue;
                            opts.TerminalIds.Add(id);
                        }
                        visited.Add(id);
                        queue.Enqueue(nb);
                    }
                    long a = Math.Min(el.Id.Value, id), b = Math.Max(el.Id.Value, id);
                    edges.Add((a, b));
                }
            }
            if (visited.Count >= opts.MaxElements)
                r.Warnings.Add($"Walk stopped at {opts.MaxElements} elements; the network may be incomplete.");
            foreach (long t in opts.TerminalIds) if (!visited.Contains(t)) r.UnreachedTerminals.Add(t);
            if (r.UnreachedTerminals.Count > 0)
                r.Warnings.Add($"{r.UnreachedTerminals.Count} terminal(s) are not connected to the source and were left out.");

            var incident = visited.ToDictionary(v => v, v => new List<(long, long)>());
            foreach (var e in edges)
            {
                if (incident.ContainsKey(e.Item1)) incident[e.Item1].Add(e);
                if (incident.ContainsKey(e.Item2)) incident[e.Item2].Add(e);
            }

            // 2. Junctions, merged for node elements.
            string J((long, long) e) => $"j:{e.Item1}|{e.Item2}";
            var parent = new Dictionary<string, string>();
            string Find(string x)
            {
                if (!parent.ContainsKey(x)) parent[x] = x;
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }
            void Union(string a, string b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

            bool IsLinkElement(Element el, int connections)
            {
                if (el.Id.Value == source.Id.Value && !(el is Pipe)) return false;
                if (opts.TerminalIds.Contains(el.Id.Value) && el.Id != source.Id) return false;
                if (el is Pipe || el is FlexPipe) return connections <= 2;
                var bic = (BuiltInCategory)(el.Category?.Id.Value ?? 0);
                if (bic == BuiltInCategory.OST_PipeFitting || bic == BuiltInCategory.OST_PipeAccessory) return connections == 2;
                return false;
            }

            var elements = visited.ToDictionary(v => v, v => doc.GetElement(new ElementId(v)));
            var nodeOwner = new Dictionary<string, long>();   // merged-node root → element that owns it
            foreach (var kv in elements)
            {
                var el = kv.Value;
                var inc = incident[kv.Key];
                bool isSourcePipe = kv.Key == source.Id.Value && el is Pipe;
                if (isSourcePipe && inc.Count == 2)
                    r.Warnings.Add("The source pipe is connected at both ends; it is treated as a supply point in the middle of the network.");
                if (IsLinkElement(el, inc.Count) && !(isSourcePipe && inc.Count == 2)) continue;
                string own = $"e:{kv.Key}";
                Find(own);
                foreach (var e in inc) Union(J(e), own);
                nodeOwner[own] = kv.Key;
            }

            // 3. Nodes.
            var net = r.Network;
            var nodeOf = new Dictionary<string, NetNode>();
            NetNode NodeFor(string junction, double z)
            {
                string root = Find(junction);
                if (nodeOf.TryGetValue(root, out var n)) return n;
                // A node can stand for several elements screwed straight into one
                // another (a head in a tee, two heads on a cross): they share one
                // pressure, so terminals' K-factors and loads add (orifices in
                // parallel). The source wins over anything merged into it.
                var owners = nodeOwner.Where(kv => Find(kv.Key) == root).Select(kv => kv.Value).Distinct().ToList();
                n = new NetNode { Id = root, ElevationM = z, Kind = NetNodeKind.Junction, Label = root };
                if (owners.Count > 0)
                {
                    long primary = owners.Contains(source.Id.Value) ? source.Id.Value
                                 : owners.FirstOrDefault(o => opts.TerminalIds.Contains(o));
                    if (primary == 0) primary = owners[0];
                    var el = elements[primary];
                    n.ElementId = string.Join(";", owners.Select(o => o.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    n.Label = string.Join(" + ", owners.Select(o => RevitFlowTreeBuilder.Describe(elements[o])));
                    n.ElevationM = PointZ(el) ?? z;
                    var terms = owners.Where(o => opts.TerminalIds.Contains(o) && o != source.Id.Value).ToList();
                    if (owners.Contains(source.Id.Value))
                    {
                        n.Kind = NetNodeKind.Source;
                        if (terms.Count > 0) r.Warnings.Add($"{terms.Count} terminal(s) connect straight onto the source and are left out.");
                    }
                    else if (terms.Count > 0)
                    {
                        n.Kind = NetNodeKind.Terminal;
                        foreach (var t in terms)
                        {
                            var tmp = new NetNode();
                            readTerminal?.Invoke(elements[t], tmp);
                            n.KFactor += tmp.KFactor;
                            n.LoadKw += tmp.LoadKw;
                            if (tmp.KFactor <= 0 && tmp.LoadKw <= 0) n.Label += " (no data)";
                        }
                        if (terms.Count > 1) r.Warnings.Add($"{terms.Count} terminals share one connection point and are solved together: {n.Label}.");
                    }
                }
                nodeOf[root] = n;
                net.AddNode(n);
                return n;
            }

            // 4. Links.
            var multiPortShare = new Dictionary<long, (string part, int ports)>();
            foreach (var kv in elements)
            {
                var el = kv.Value;
                var inc = incident[kv.Key];
                var bic = (BuiltInCategory)(el.Category?.Id.Value ?? 0);
                if (bic == BuiltInCategory.OST_PipeFitting && !IsLinkElement(el, inc.Count) && inc.Count >= 3)
                {
                    string part = ((el as FamilyInstance)?.MEPModel as Autodesk.Revit.DB.Mechanical.MechanicalFitting)?.PartType.ToString() ?? "";
                    multiPortShare[kv.Key] = (part, inc.Count);
                }
            }

            foreach (var kv in elements)
            {
                var el = kv.Value;
                var inc = incident[kv.Key];
                bool isSourcePipe = kv.Key == source.Id.Value && el is Pipe;
                if (!IsLinkElement(el, inc.Count) && !isSourcePipe) continue;
                if (isSourcePipe && inc.Count == 2) continue;          // became a node above

                string ja, jb;
                double za, zb;
                if (inc.Count == 2)
                {
                    ja = J(inc[0]); jb = J(inc[1]);
                    za = ConnectionZ(el, inc[0], kv.Key) ?? 0; zb = ConnectionZ(el, inc[1], kv.Key) ?? 0;
                }
                else
                {
                    // A pipe with a free end (or none).
                    ja = inc.Count == 1 ? J(inc[0]) : $"free0:{kv.Key}";
                    jb = $"free:{kv.Key}";
                    za = inc.Count == 1 ? ConnectionZ(el, inc[0], kv.Key) ?? 0 : 0;
                    zb = FreeEndZ(el, za) ?? za;
                    if (isSourcePipe) { Union(jb, $"e:{kv.Key}"); nodeOwner[$"e:{kv.Key}"] = kv.Key; }
                }
                var na = NodeFor(ja, za);
                var nb = NodeFor(jb, zb);

                var link = new NetLink
                {
                    Id = kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ElementId = kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Label = RevitFlowTreeBuilder.Describe(el),
                    A = na.Id, B = nb.Id
                };
                if (el is Pipe pipe)
                {
                    link.Kind = NetLinkKind.Pipe;
                    link.LengthM = (pipe.Location as LocationCurve)?.Curve?.Length * FtToM ?? 0;
                    link.BoreMm = RevitFlowTreeBuilder.BoreMm(pipe);
                    link.HazenWilliamsC = opts.HazenWilliamsC?.Invoke(pipe) ?? 0;
                }
                else
                {
                    var bic = (BuiltInCategory)(el.Category?.Id.Value ?? 0);
                    link.Kind = bic == BuiltInCategory.OST_PipeAccessory ? NetLinkKind.Accessory : NetLinkKind.Fitting;
                    string part = link.Kind == NetLinkKind.Accessory ? "Valve"
                        : ((el as FamilyInstance)?.MEPModel as Autodesk.Revit.DB.Mechanical.MechanicalFitting)?.PartType.ToString() ?? "";
                    // Bore from the adjacent pipes.
                    link.BoreMm = inc.Select(e => e.Item1 == kv.Key ? e.Item2 : e.Item1)
                        .Select(o => elements.TryGetValue(o, out var oe) ? oe as Pipe : null)
                        .Where(p => p != null).Select(RevitFlowTreeBuilder.BoreMm).DefaultIfEmpty(0).Max();
                    link.EquivLengthM = EquivM(opts, part, link.BoreMm);
                }
                // Share of any multi-port fitting at either end.
                foreach (var e in inc)
                {
                    long other = e.Item1 == kv.Key ? e.Item2 : e.Item1;
                    if (multiPortShare.TryGetValue(other, out var share) && share.ports > 0 && link.BoreMm > 0)
                        link.EquivLengthM += EquivM(opts, share.part, link.BoreMm) / share.ports;
                }
                if (link.A == link.B) continue;                          // both ends merged into one node
                net.Connect(link);
            }

            // Node elements with no links (a lone terminal on the source) still need to exist.
            foreach (var own in nodeOwner.Keys.ToList()) NodeFor(own, 0);

            r.PrunedNodes = net.PruneDeadEnds();
            if (net.Source == null) r.Warnings.Add("The source element did not become a network node — select a valve, meter, pump or a pipe end.");
            return r;
        }

        private static double EquivM(FlowTreeBuildOptions opts, string part, double boreMm)
            => opts.EquivalentLengthM != null ? opts.EquivalentLengthM(part, boreMm)
                                              : (opts.EquivalentBores?.Invoke(part) ?? 0) * boreMm / 1000.0;

        private static double? PointZ(Element el)
        {
            if (el?.Location is LocationPoint lp) return lp.Point.Z * FtToM;
            var bb = el?.get_BoundingBox(null);
            return bb != null ? (bb.Min.Z + bb.Max.Z) / 2 * FtToM : (double?)null;
        }

        /// <summary>Elevation of the connector on <paramref name="el"/> that joins the other element of edge <paramref name="e"/>.</summary>
        private static double? ConnectionZ(Element el, (long, long) e, long self)
        {
            long other = e.Item1 == self ? e.Item2 : e.Item1;
            try
            {
                ConnectorSet cs = (el as MEPCurve)?.ConnectorManager?.Connectors
                               ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager?.Connectors;
                if (cs == null) return null;
                foreach (Connector c in cs)
                {
                    if (!c.IsConnected) continue;
                    foreach (Connector rf in c.AllRefs)
                        if (rf.Owner?.Id.Value == other) return c.Origin.Z * FtToM;
                }
            }
            catch (Exception ex) { StingLog.Warn($"RevitPipeNetworkBuilder.ConnectionZ {el?.Id}: {ex.Message}"); }
            return null;
        }

        private static double? FreeEndZ(Element el, double connectedZ)
        {
            try
            {
                var cs = (el as MEPCurve)?.ConnectorManager?.Connectors;
                if (cs == null) return null;
                foreach (Connector c in cs)
                    if (!c.IsConnected && c.ConnectorType == ConnectorType.End) return c.Origin.Z * FtToM;
            }
            catch (Exception ex) { StingLog.Warn($"RevitPipeNetworkBuilder.FreeEndZ {el?.Id}: {ex.Message}"); }
            return null;
        }
    }
}
