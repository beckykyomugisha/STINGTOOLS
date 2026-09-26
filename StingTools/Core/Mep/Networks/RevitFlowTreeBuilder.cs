// RevitFlowTreeBuilder — turns a piped network in the model into a FlowTree.
//
// Breadth-first walk over physical connectors from a source element (a pipe,
// valve, meter or pump) until every requested terminal is reached. The walk
// gives each element one parent, so the result is a spanning tree: a looped
// or gridded network is reported (its extra connections are ignored), not
// silently treated as a tree. Only elements on a path from the source to a
// terminal are kept.
//
// Callers decide what a terminal is and read its demand (K-factor, heat
// input) through delegates, and supply the equivalent-length table for
// fittings and valves — so sprinkler and gas share one walker.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Mep.Networks
{
    public sealed class FlowTreeBuildOptions
    {
        /// <summary>Terminal elements to reach (sprinkler heads, appliances).</summary>
        public HashSet<long> TerminalIds { get; } = new HashSet<long>();
        /// <summary>
        /// Optional: any connected element that is not pipework and matches
        /// this becomes a terminal as the walk reaches it (e.g. every gas
        /// appliance on the installation). Leave null to use TerminalIds only.
        /// </summary>
        public Func<Element, bool> TerminalPredicate { get; set; }
        /// <summary>Fills KFactor / LoadKw on a terminal node.</summary>
        public Action<Element, FlowNode> ReadTerminal { get; set; }
        /// <summary>Equivalent length in bores for a fitting part type or "Valve".</summary>
        public Func<string, double> EquivalentBores { get; set; } = _ => 0;
        /// <summary>Hazen-Williams C for a pipe (0 = solver default).</summary>
        public Func<Pipe, double> HazenWilliamsC { get; set; } = _ => 0;
        public int MaxElements { get; set; } = 20000;
    }

    public sealed class FlowTreeBuildResult
    {
        public FlowNode Root { get; set; }
        public Dictionary<FlowNode, ElementId> ElementIdByNode { get; } = new Dictionary<FlowNode, ElementId>();
        public List<long> UnreachedTerminals { get; } = new List<long>();
        public int LoopConnections { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class RevitFlowTreeBuilder
    {
        private const double FtToM = 0.3048;

        public static FlowTreeBuildResult Build(Document doc, Element source, FlowTreeBuildOptions opts)
        {
            var result = new FlowTreeBuildResult();
            if (doc == null || source == null || opts == null) { result.Warnings.Add("No source element."); return result; }

            var parent = new Dictionary<long, long>();
            var visited = new HashSet<long> { source.Id.Value };
            var queue = new Queue<Element>();
            queue.Enqueue(source);
            int loops = 0;

            while (queue.Count > 0 && visited.Count < opts.MaxElements)
            {
                var el = queue.Dequeue();
                bool isTerminal = opts.TerminalIds.Contains(el.Id.Value);
                if (isTerminal && el.Id != source.Id) continue;          // terminals are leaves
                foreach (var nb in Neighbours(el))
                {
                    long id = nb.Id.Value;
                    if (visited.Contains(id))
                    {
                        if (!parent.TryGetValue(el.Id.Value, out var p) || p != id) loops++;
                        continue;
                    }
                    if (!Traversable(nb) && !opts.TerminalIds.Contains(id))
                    {
                        if (opts.TerminalPredicate == null || !opts.TerminalPredicate(nb)) continue;
                        opts.TerminalIds.Add(id);
                    }
                    visited.Add(id);
                    parent[id] = el.Id.Value;
                    queue.Enqueue(nb);
                }
            }
            if (visited.Count >= opts.MaxElements)
                result.Warnings.Add($"Walk stopped at {opts.MaxElements} elements; the network may be incomplete.");
            // Each undirected loop edge is seen from both ends.
            result.LoopConnections = loops / 2;
            if (result.LoopConnections > 0)
                result.Warnings.Add($"{result.LoopConnections} loop connection(s) found. The calculation treats the " +
                                    "network as a tree and ignores them; a looped or gridded system needs a looped solver.");

            // Keep only elements on a source → terminal path.
            var needed = new HashSet<long> { source.Id.Value };
            foreach (long t in opts.TerminalIds)
            {
                if (!visited.Contains(t)) { result.UnreachedTerminals.Add(t); continue; }
                for (long cur = t; needed.Add(cur) && parent.TryGetValue(cur, out var p); cur = p) { }
            }
            if (result.UnreachedTerminals.Count > 0)
                result.Warnings.Add($"{result.UnreachedTerminals.Count} terminal(s) are not connected to the source and were left out.");

            var nodes = new Dictionary<long, FlowNode>();
            FlowNode NodeFor(long id)
            {
                if (nodes.TryGetValue(id, out var n)) return n;
                var el = doc.GetElement(new ElementId(id));
                n = MakeNode(el, id == source.Id.Value, opts);
                nodes[id] = n;
                result.ElementIdByNode[n] = el.Id;
                return n;
            }
            result.Root = NodeFor(source.Id.Value);
            // Parents before children: order by BFS depth.
            int Depth(long id) { int d = 0; for (long c = id; parent.TryGetValue(c, out var p); c = p) d++; return d; }
            foreach (long id in needed.Where(i => i != source.Id.Value).OrderBy(Depth))
                NodeFor(parent[id]).Add(NodeFor(id));

            SetElevations(doc, result, source);
            return result;
        }

        private static bool Traversable(Element e)
        {
            if (e is Pipe || e is FlexPipe) return true;
            var bic = (BuiltInCategory)(e.Category?.Id.Value ?? 0);
            return bic == BuiltInCategory.OST_PipeFitting || bic == BuiltInCategory.OST_PipeAccessory;
        }

        private static IEnumerable<Element> Neighbours(Element e)
        {
            ConnectorSet cs = (e as MEPCurve)?.ConnectorManager?.Connectors
                           ?? (e as FamilyInstance)?.MEPModel?.ConnectorManager?.Connectors;
            if (cs == null) yield break;
            foreach (Connector c in cs)
            {
                if (c.Domain != Domain.DomainPiping || !c.IsConnected) continue;
                foreach (Connector r in c.AllRefs)
                {
                    if (r.ConnectorType == ConnectorType.Logical) continue;
                    var owner = r.Owner;
                    if (owner == null || owner.Id == e.Id || owner is MEPSystem) continue;
                    yield return owner;
                }
            }
        }

        private static FlowNode MakeNode(Element el, bool isSource, FlowTreeBuildOptions opts)
        {
            var n = new FlowNode { Id = el.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), Label = Describe(el) };
            if (isSource) { n.Kind = FlowNodeKind.Source; }
            if (opts.TerminalIds.Contains(el.Id.Value) && !isSource)
            {
                n.Kind = FlowNodeKind.Terminal;
                opts.ReadTerminal?.Invoke(el, n);
                return n;
            }
            if (el is Pipe pipe)
            {
                if (!isSource) n.Kind = FlowNodeKind.Pipe;
                n.LengthM = (pipe.Location as LocationCurve)?.Curve?.Length * FtToM ?? 0;
                n.DiameterMm = BoreMm(pipe);
                n.HazenWilliamsC = opts.HazenWilliamsC?.Invoke(pipe) ?? 0;
                if (isSource) { n.Kind = FlowNodeKind.Pipe; }   // a source pipe still carries flow and loss
                return n;
            }
            var bic = (BuiltInCategory)(el.Category?.Id.Value ?? 0);
            if (bic == BuiltInCategory.OST_PipeFitting)
            {
                if (!isSource) n.Kind = FlowNodeKind.Fitting;
                string part = ((el as FamilyInstance)?.MEPModel as Autodesk.Revit.DB.Mechanical.MechanicalFitting)?.PartType.ToString() ?? "";
                n.EquivLengthDiameters = opts.EquivalentBores?.Invoke(part) ?? 0;
            }
            else if (bic == BuiltInCategory.OST_PipeAccessory)
            {
                if (!isSource) n.Kind = FlowNodeKind.Accessory;
                n.EquivLengthDiameters = opts.EquivalentBores?.Invoke("Valve") ?? 0;
            }
            else if (!isSource) n.Kind = FlowNodeKind.Junction;
            return n;
        }

        /// <summary>Internal bore, mm: the inner-diameter parameter when set, else the pipe diameter.</summary>
        public static double BoreMm(Pipe pipe)
        {
            try
            {
                double inner = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_INNER_DIAM_PARAM)?.AsDouble() ?? 0;
                return (inner > 0 ? inner : pipe.Diameter) * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"RevitFlowTreeBuilder.BoreMm {pipe?.Id}: {ex.Message}"); return 0; }
        }

        /// <summary>
        /// Each node's elevation is its downstream point: for a pipe, the end
        /// away from its parent; otherwise its location (or connector centroid).
        /// </summary>
        private static void SetElevations(Document doc, FlowTreeBuildResult r, Element source)
        {
            foreach (var kv in r.ElementIdByNode)
            {
                var node = kv.Key;
                var el = doc.GetElement(kv.Value);
                try
                {
                    XYZ p = null;
                    if (el is Pipe pipe && pipe.Location is LocationCurve lc)
                    {
                        var a = lc.Curve.GetEndPoint(0);
                        var b = lc.Curve.GetEndPoint(1);
                        XYZ parentPt = node.Parent != null ? PointOf(doc, r.ElementIdByNode[node.Parent]) : null;
                        p = parentPt == null ? b : (a.DistanceTo(parentPt) <= b.DistanceTo(parentPt) ? b : a);
                    }
                    else p = PointOf(doc, kv.Value);
                    node.ElevationM = (p?.Z ?? 0) * FtToM;
                }
                catch (Exception ex) { StingLog.Warn($"RevitFlowTreeBuilder elevation {kv.Value}: {ex.Message}"); }
            }
        }

        private static XYZ PointOf(Document doc, ElementId id)
        {
            var el = doc.GetElement(id);
            if (el?.Location is LocationPoint lp) return lp.Point;
            if (el?.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
            var bb = el?.get_BoundingBox(null);
            return bb != null ? (bb.Min + bb.Max) / 2 : null;
        }

        private static string Describe(Element el)
        {
            string name = el.Name ?? "";
            string cat = el.Category?.Name ?? "";
            return $"{cat} {el.Id.Value}{(string.IsNullOrEmpty(name) ? "" : " " + name)}".Trim();
        }
    }
}
