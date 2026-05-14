// PipeNetworkGraph — directed pipe connectivity graph built from the
// Revit Connector API. Phase 179c.
//
// PipeNetworkBuilder.Build() walks all Pipe / PipeFitting / PipeAccessory /
// PlumbingFixture elements, links them via ConnectorManager.AllRefs, then
// does a BFS from fixture-leaf nodes toward the stack / termination. The
// resulting PipeNetwork is consumed by PumpSelector, RealTimePipeSizer, and
// the VentCreationEngine.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    // ──────────────────────────────────────────────────────────────────────
    // Data model
    // ──────────────────────────────────────────────────────────────────────

    public enum PipeNodeType
    {
        Pipe,
        Fixture,
        Junction,
        Stack,
        Equipment,
        Termination
    }

    public class PipeEdge
    {
        public PipeNode   From           { get; set; }
        public PipeNode   To             { get; set; }
        public ElementId  PipeId         { get; set; }
        public double     LengthM        { get; set; }
        public double     DnMm           { get; set; }
        public double     SlopePct       { get; set; }
        /// <summary>Hazen-Williams or Manning friction loss along this edge, kPa.</summary>
        public double     ResistanceKpa  { get; set; }
    }

    public class PipeNode
    {
        public ElementId        Id              { get; set; }
        public XYZ              Position        { get; set; }   // Revit internal feet
        public PipeNodeType     Type            { get; set; }
        public string           SystemName      { get; set; } = "";
        public double           DnMm            { get; set; }
        public double           DfuAccumulated  { get; set; }
        public double           PressureKpa     { get; set; }
        public List<PipeEdge>   Downstream      { get; } = new List<PipeEdge>();
        public List<PipeEdge>   Upstream        { get; } = new List<PipeEdge>();
    }

    public class PipeNetwork
    {
        public List<PipeNode>              Nodes     { get; } = new List<PipeNode>();
        public List<PipeEdge>              Edges     { get; } = new List<PipeEdge>();
        /// <summary>Fixture-leaf nodes with no upstream connections.</summary>
        public List<PipeNode>              RootNodes { get; } = new List<PipeNode>();
        public Dictionary<long, PipeNode>  ById      { get; } = new Dictionary<long, PipeNode>();
        public string                      SystemFilter { get; set; } = "";
    }

    // ──────────────────────────────────────────────────────────────────────
    // Builder
    // ──────────────────────────────────────────────────────────────────────

    public static class PipeNetworkBuilder
    {
        private const double FtToM  = 0.3048;
        private const double FtToMm = 304.8;

        // Water density for static pressure: ρg ≈ 9.807 kPa/m
        private const double RhoG = 9.807;

        /// <summary>
        /// Build a directed PipeNetwork for all (or one named) plumbing systems.
        /// </summary>
        public static PipeNetwork Build(Document doc, string systemNameFilter = null)
        {
            var net = new PipeNetwork { SystemFilter = systemNameFilter ?? "" };
            if (doc == null) return net;

            try
            {
                // Collect all pipe-related elements once
                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe))
                    .WhereElementIsNotElementType()
                    .Cast<Pipe>()
                    .Where(p => SystemMatches(p, systemNameFilter))
                    .ToList();

                var fittings = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeFitting)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                var accessories = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeAccessory)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                var fixtures = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                var mechEquip = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .ToList();

                // Build node lookup for all elements
                foreach (var pipe in pipes)
                    GetOrCreateNode(net, pipe, doc);

                foreach (var fi in fittings.Concat(accessories))
                    GetOrCreateNode(net, fi, doc);

                foreach (var fix in fixtures)
                {
                    var node = GetOrCreateNode(net, fix, doc);
                    node.Type = PipeNodeType.Fixture;
                }

                foreach (var eq in mechEquip)
                {
                    var node = GetOrCreateNode(net, eq, doc);
                    node.Type = PipeNodeType.Equipment;
                }

                // Walk connector adjacency to build edges
                BuildEdges(net, doc, pipes, fittings, accessories, fixtures, mechEquip);

                // Classify stacks (high vertical fraction)
                ClassifyStacks(net, doc);

                // Identify root (leaf) nodes — fixture nodes with no upstream
                foreach (var node in net.Nodes)
                {
                    if (node.Type == PipeNodeType.Fixture && node.Upstream.Count == 0)
                        net.RootNodes.Add(node);
                    else if (node.Type != PipeNodeType.Fixture && node.Upstream.Count == 0
                             && node.Downstream.Count > 0)
                        net.RootNodes.Add(node);
                }

                // Mark termination nodes — no downstream edges
                foreach (var node in net.Nodes)
                    if (node.Downstream.Count == 0 && node.Type == PipeNodeType.Pipe)
                        node.Type = PipeNodeType.Termination;
            }
            catch (Exception ex)
            {
                StingLog.Error("PipeNetworkBuilder.Build", ex);
            }

            return net;
        }

        /// <summary>
        /// BFS from leaves summing DFU into DfuAccumulated on each node.
        /// dfuMap: ElementId → DFU value (produced by FixtureUnitAggregator).
        /// </summary>
        public static void AccumulateDfu(PipeNetwork net, Dictionary<ElementId, double> dfuMap)
        {
            if (net == null || dfuMap == null) return;

            // Seed fixture nodes
            foreach (var node in net.Nodes)
            {
                if (dfuMap.TryGetValue(node.Id, out var dfu))
                    node.DfuAccumulated = dfu;
            }

            // BFS from roots downstream, propagating sums
            var visited = new HashSet<long>();
            var queue   = new Queue<PipeNode>(net.RootNodes);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (visited.Contains(node.Id.Value)) continue;
                visited.Add(node.Id.Value);

                foreach (var edge in node.Downstream)
                {
                    edge.To.DfuAccumulated += node.DfuAccumulated;
                    queue.Enqueue(edge.To);
                }
            }
        }

        /// <summary>
        /// Propagate static pressure through the network from a known inlet.
        /// inletElevFt: Revit internal feet (Z coordinate of inlet node).
        /// </summary>
        public static void AccumulatePressure(PipeNetwork net, double inletKpa, double inletElevFt)
        {
            if (net == null) return;

            // Identify inlet node (closest node to the termination / entry point)
            PipeNode inletNode = net.Nodes
                .Where(n => n.Type == PipeNodeType.Termination || n.Type == PipeNodeType.Equipment)
                .OrderBy(n => n.Position != null ? n.Position.Z : double.MaxValue)
                .FirstOrDefault();

            if (inletNode == null && net.Nodes.Count > 0)
                inletNode = net.Nodes.First();

            if (inletNode == null) return;
            inletNode.PressureKpa = inletKpa;

            // BFS from inlet toward fixtures
            var visited = new HashSet<long>();
            var queue   = new Queue<PipeNode>();
            queue.Enqueue(inletNode);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (visited.Contains(node.Id.Value)) continue;
                visited.Add(node.Id.Value);

                double nodeElevM = (node.Position?.Z ?? 0) * FtToM;
                double inletElevM = inletElevFt * FtToM;

                foreach (var edge in node.Upstream.Concat(node.Downstream))
                {
                    var neighbour = edge.From == node ? edge.To : edge.From;
                    if (visited.Contains(neighbour.Id.Value)) continue;

                    double neighbourElevM = (neighbour.Position?.Z ?? 0) * FtToM;
                    // Static head: p_neighbour = p_inlet - ρg*(ΔZ from inlet)
                    double deltaZM = neighbourElevM - inletElevM;
                    neighbour.PressureKpa = Math.Max(0, inletKpa - (RhoG * deltaZM) - edge.ResistanceKpa);
                    queue.Enqueue(neighbour);
                }
            }
        }

        /// <summary>
        /// Dijkstra's longest-resistance path from any fixture leaf to the termination node.
        /// Returns edges in order from fixture to termination.
        /// </summary>
        public static List<PipeEdge> FindCriticalPath(PipeNetwork net)
        {
            if (net == null || net.Nodes.Count == 0) return new List<PipeEdge>();

            var terminationNode = net.Nodes.FirstOrDefault(n => n.Type == PipeNodeType.Termination);
            if (terminationNode == null)
                terminationNode = net.Nodes.OrderBy(n => n.Downstream.Count).First();

            // Build adjacency: node → list of (edge, neighbour)
            double maxResistance = double.MinValue;
            List<PipeEdge> bestPath = new List<PipeEdge>();

            foreach (var root in net.RootNodes.Where(n => n.Type == PipeNodeType.Fixture))
            {
                var path = DfsPath(root, terminationNode, new HashSet<long>());
                if (path != null)
                {
                    double totalRes = path.Sum(e => e.ResistanceKpa + e.LengthM * 0.1);
                    if (totalRes > maxResistance)
                    {
                        maxResistance = totalRes;
                        bestPath = path;
                    }
                }
            }

            return bestPath;
        }

        public static List<PipeNode> FindStacks(PipeNetwork net)
        {
            if (net == null) return new List<PipeNode>();
            return net.Nodes.Where(n => n.Type == PipeNodeType.Stack).ToList();
        }

        /// <summary>
        /// Export the network as CSV text (nodes + edges sections).
        /// </summary>
        public static string ExportToCsv(PipeNetwork net)
        {
            if (net == null) return "";
            var sb = new StringBuilder();

            sb.AppendLine("NODES");
            sb.AppendLine("Id,Type,SystemName,DnMm,DfuAccumulated,PressureKpa,X_ft,Y_ft,Z_ft");
            foreach (var n in net.Nodes)
            {
                double x = n.Position?.X ?? 0;
                double y = n.Position?.Y ?? 0;
                double z = n.Position?.Z ?? 0;
                sb.AppendLine($"{n.Id.Value},{n.Type},{EscCsv(n.SystemName)},{n.DnMm:F1}," +
                              $"{n.DfuAccumulated:F2},{n.PressureKpa:F1},{x:F3},{y:F3},{z:F3}");
            }

            sb.AppendLine();
            sb.AppendLine("EDGES");
            sb.AppendLine("FromId,ToId,PipeId,LengthM,DnMm,SlopePct,ResistanceKpa");
            foreach (var e in net.Edges)
            {
                sb.AppendLine($"{e.From?.Id.Value},{e.To?.Id.Value},{e.PipeId?.Value}," +
                              $"{e.LengthM:F3},{e.DnMm:F1},{e.SlopePct:F2},{e.ResistanceKpa:F3}");
            }

            return sb.ToString();
        }

        // ──────────────────────────────────────────────────────────────────
        // Private helpers
        // ──────────────────────────────────────────────────────────────────

        private static PipeNode GetOrCreateNode(PipeNetwork net, Element el, Document doc)
        {
            long id = el.Id.Value;
            if (net.ById.TryGetValue(id, out var existing)) return existing;

            var node = new PipeNode
            {
                Id         = el.Id,
                SystemName = GetSystemName(el),
                Position   = GetCentroid(el),
                Type       = ClassifyElement(el)
            };

            // DN from pipe diameter
            if (el is Pipe pipe)
                node.DnMm = pipe.Diameter * FtToMm;

            net.Nodes.Add(node);
            net.ById[id] = node;
            return node;
        }

        private static void BuildEdges(
            PipeNetwork net, Document doc,
            IEnumerable<Pipe> pipes,
            IEnumerable<FamilyInstance> fittings,
            IEnumerable<FamilyInstance> accessories,
            IEnumerable<FamilyInstance> fixtures,
            IEnumerable<FamilyInstance> mechEquip)
        {
            var allElements = pipes.Cast<Element>()
                .Concat(fittings)
                .Concat(accessories)
                .Concat(fixtures)
                .Concat(mechEquip);

            var processedPairs = new HashSet<string>();

            foreach (var el in allElements)
            {
                try
                {
                    ConnectorManager cm = GetConnectorManager(el);
                    if (cm == null) continue;
                    if (!net.ById.TryGetValue(el.Id.Value, out var fromNode)) continue;

                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector other in c.AllRefs)
                        {
                            try
                            {
                                var neighbour = other.Owner;
                                if (neighbour == null || neighbour.Id == el.Id) continue;
                                if (!net.ById.TryGetValue(neighbour.Id.Value, out var toNode)) continue;

                                string pairKey = MakePairKey(el.Id, neighbour.Id);
                                if (!processedPairs.Add(pairKey)) continue;

                                // Determine which is upstream (fixture/high Z) vs downstream
                                // For drainage: fixture → stack → main → termination
                                var edge = BuildEdge(fromNode, toNode, el, neighbour, doc);
                                if (edge == null) continue;

                                net.Edges.Add(edge);
                                edge.From.Downstream.Add(edge);
                                edge.To.Upstream.Add(edge);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private static PipeEdge BuildEdge(PipeNode fromNode, PipeNode toNode,
            Element fromEl, Element toEl, Document doc)
        {
            try
            {
                // Determine flow direction: in drainage, higher Z = upstream (fixture end)
                // We orient edges from higher elevation to lower (fixture → drain → sewer)
                double fromZ = fromNode.Position?.Z ?? 0;
                double toZ   = toNode.Position?.Z ?? 0;

                PipeNode upstream, downstream;
                Element  upEl,     downEl;
                if (fromZ >= toZ)
                {
                    upstream = fromNode; downstream = toNode;
                    upEl     = fromEl;   downEl     = toEl;
                }
                else
                {
                    upstream = toNode;  downstream = fromNode;
                    upEl     = toEl;    downEl     = fromEl;
                }

                // Resolve pipe element for length / DN
                Pipe pipeEl = (fromEl as Pipe) ?? (toEl as Pipe);

                double lengthM = 0;
                double dnMm    = 0;
                double slopePct = 0;
                ElementId pipeId = pipeEl?.Id ?? fromEl.Id;

                if (pipeEl != null)
                {
                    var lc = pipeEl.Location as LocationCurve;
                    if (lc?.Curve != null)
                    {
                        var s = lc.Curve.GetEndPoint(0);
                        var e = lc.Curve.GetEndPoint(1);
                        lengthM = lc.Curve.Length * FtToM;
                        double horizFt = Math.Sqrt(Math.Pow(e.X - s.X, 2) + Math.Pow(e.Y - s.Y, 2));
                        double dzFt    = Math.Abs(e.Z - s.Z);
                        slopePct = horizFt > 1e-6 ? dzFt / horizFt * 100.0 : 0;
                    }
                    dnMm = pipeEl.Diameter * FtToMm;
                }

                // Estimate friction resistance: simplified Hazen-Williams
                // R = 10.67 * L * Q^1.852 / (C^1.852 * d^4.87)  — approximated as length-based
                // For network-level critical path we use L as proxy
                double resistanceKpa = EstimateFrictionKpa(lengthM, dnMm, slopePct);

                return new PipeEdge
                {
                    From          = upstream,
                    To            = downstream,
                    PipeId        = pipeId,
                    LengthM       = lengthM,
                    DnMm          = dnMm,
                    SlopePct      = slopePct,
                    ResistanceKpa = resistanceKpa
                };
            }
            catch
            {
                return null;
            }
        }

        private static double EstimateFrictionKpa(double lengthM, double dnMm, double slopePct)
        {
            if (lengthM < 0.01 || dnMm < 1) return 0;
            // Simplified: 0.1 kPa/m for 100mm, scales with (100/dn)^4.87 and length
            double dnRatio = 100.0 / dnMm;
            return lengthM * 0.1 * Math.Pow(dnRatio, 2.5);
        }

        private static void ClassifyStacks(PipeNetwork net, Document doc)
        {
            foreach (var node in net.Nodes.Where(n => n.Type == PipeNodeType.Pipe))
            {
                try
                {
                    var el = doc.GetElement(node.Id);
                    if (el is Pipe p)
                    {
                        var lc = p.Location as LocationCurve;
                        if (lc?.Curve != null)
                        {
                            var s = lc.Curve.GetEndPoint(0);
                            var e = lc.Curve.GetEndPoint(1);
                            double dz    = Math.Abs(e.Z - s.Z);
                            double total = s.DistanceTo(e);
                            if (total > 1e-6 && dz / total > 0.8)
                                node.Type = PipeNodeType.Stack;
                        }
                    }
                }
                catch { }
            }
        }

        private static List<PipeEdge> DfsPath(PipeNode current, PipeNode target, HashSet<long> visited)
        {
            if (current == null) return null;
            if (current.Id == target.Id) return new List<PipeEdge>();
            if (visited.Contains(current.Id.Value)) return null;
            visited.Add(current.Id.Value);

            foreach (var edge in current.Downstream)
            {
                var sub = DfsPath(edge.To, target, visited);
                if (sub != null)
                {
                    var path = new List<PipeEdge> { edge };
                    path.AddRange(sub);
                    return path;
                }
            }
            return null;
        }

        private static bool SystemMatches(Pipe p, string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            try
            {
                string sysName = p.MEPSystem?.Name ?? "";
                return sysName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return true; }
        }

        private static string GetSystemName(Element el)
        {
            try
            {
                if (el is Pipe p) return p.MEPSystem?.Name ?? "";
                if (el is FamilyInstance fi)
                    return fi.MEPModel?.ConnectorManager?.Connectors?.Cast<Connector>()
                           .Select(c => c.MEPSystem?.Name)
                           .FirstOrDefault(n => n != null) ?? "";
            }
            catch { }
            return "";
        }

        private static XYZ GetCentroid(Element el)
        {
            try
            {
                if (el is Pipe p)
                {
                    var lc = p.Location as LocationCurve;
                    if (lc?.Curve != null)
                        return lc.Curve.Evaluate(0.5, true);
                }
                if (el.Location is LocationPoint lp) return lp.Point;
                var bb = el.get_BoundingBox(null);
                if (bb != null) return (bb.Min + bb.Max) / 2.0;
            }
            catch { }
            return XYZ.Zero;
        }

        private static ConnectorManager GetConnectorManager(Element el)
        {
            try
            {
                if (el is MEPCurve mc) return mc.ConnectorManager;
                if (el is FamilyInstance fi) return fi.MEPModel?.ConnectorManager;
            }
            catch { }
            return null;
        }

        private static PipeNodeType ClassifyElement(Element el)
        {
            if (el is Pipe) return PipeNodeType.Pipe;
            try
            {
                var bic = (BuiltInCategory)(el.Category?.Id?.Value ?? 0);
                if (bic == BuiltInCategory.OST_PlumbingFixtures) return PipeNodeType.Fixture;
                if (bic == BuiltInCategory.OST_MechanicalEquipment) return PipeNodeType.Equipment;
                if (bic == BuiltInCategory.OST_PipeFitting) return PipeNodeType.Junction;
            }
            catch { }
            return PipeNodeType.Pipe;
        }

        private static string MakePairKey(ElementId a, ElementId b)
        {
            long lo = Math.Min(a.Value, b.Value);
            long hi = Math.Max(a.Value, b.Value);
            return $"{lo}_{hi}";
        }

        private static string EscCsv(string s)
        {
            if (s == null) return "";
            if (s.Contains(',') || s.Contains('"')) return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }
    }
}
