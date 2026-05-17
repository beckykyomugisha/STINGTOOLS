// StingTools v4 MVP — Phase J cable router (tray / conduit graph).
//
// Builds a graph over OST_CableTray + OST_Conduit + their fittings,
// then Dijkstra-routes a cable from source to destination.
// ConnectorManager.Connectors provides the adjacency: any two MEP
// curves sharing a connector (or both connected to the same fitting)
// are graph-neighbours.
//
// Output: ordered tray/conduit ElementId list + total path length.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Core;

namespace StingTools.Core.Electrical
{
    public class CableRoute
    {
        public List<long>   TrayIds      { get; } = new List<long>();
        public double       LengthM      { get; set; }
        public bool         Success      { get; set; }
        public string       FailureReason{ get; set; } = "";
        public List<string> Notes        { get; } = new List<string>();
    }

    public static class CableRouter
    {
        private const double FtToM = 0.3048;

        /// <summary>
        /// Route a cable from sourceEquipment to destEquipment through
        /// the project's cable-tray + conduit network. Selects the
        /// nearest tray connector on both ends, then Dijkstras through
        /// the connector graph. Returns an empty route when either
        /// endpoint has no tray within pickRadiusMm.
        /// </summary>
        public static CableRoute Route(
            Document doc,
            Element sourceEquipment,
            Element destEquipment,
            double pickRadiusMm = 2000.0)
        {
            var result = new CableRoute();
            if (doc == null || sourceEquipment == null || destEquipment == null)
            {
                result.FailureReason = "null inputs";
                return result;
            }

            var srcPt = LocationOf(sourceEquipment);
            var dstPt = LocationOf(destEquipment);
            if (srcPt == null || dstPt == null)
            {
                result.FailureReason = "equipment has no LocationPoint";
                return result;
            }

            var graph = BuildTrayGraph(doc);
            if (graph.Count == 0)
            {
                result.FailureReason = "no cable tray / conduit found in document";
                return result;
            }

            long srcTray = NearestTrayId(graph, srcPt, pickRadiusMm * 0.003281);
            long dstTray = NearestTrayId(graph, dstPt, pickRadiusMm * 0.003281);
            if (srcTray == 0 || dstTray == 0)
            {
                result.FailureReason =
                    $"No tray within {pickRadiusMm:F0} mm of endpoint (src={srcTray}, dst={dstTray})";
                return result;
            }
            if (srcTray == dstTray)
            {
                result.Success = true;
                result.TrayIds.Add(srcTray);
                var c = graph[srcTray];
                result.LengthM = c.LengthM;
                return result;
            }

            var path = Dijkstra(graph, srcTray, dstTray);
            if (path.Count == 0)
            {
                result.FailureReason = "no graph path from source tray to destination tray";
                return result;
            }
            result.Success = true;
            double len = 0;
            foreach (var id in path)
            {
                result.TrayIds.Add(id);
                if (graph.TryGetValue(id, out var v)) len += v.LengthM;
            }
            result.LengthM = len;
            return result;
        }

        // ---- graph build -------------------------------------------------------

        private class TrayNode
        {
            public long   Id        { get; set; }
            public double LengthM   { get; set; }
            public XYZ    Midpoint  { get; set; }
            public List<long> Neighbours { get; } = new List<long>();
        }

        private static Dictionary<long, TrayNode> BuildTrayGraph(Document doc)
        {
            var nodes = new Dictionary<long, TrayNode>();
            // Collect tray + conduit curves.
            var cats = new[]
            {
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_ConduitFitting,
            };
            foreach (var cat in cats)
            {
                try
                {
                    var col = new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType();
                    foreach (var el in col)
                    {
                        var n = new TrayNode { Id = el.Id.Value };
                        var lc = el.Location as LocationCurve;
                        if (lc?.Curve != null)
                        {
                            n.LengthM  = lc.Curve.Length * FtToM;
                            try { n.Midpoint = lc.Curve.Evaluate(0.5, true); } catch { }
                        }
                        else if (el.Location is LocationPoint lp)
                        {
                            n.Midpoint = lp.Point;
                        }
                        nodes[n.Id] = n;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CableRouter BuildTrayGraph {cat}: {ex.Message}"); }
            }

            // Adjacency via ConnectorManager.Connectors.AllRefs.
            foreach (var kv in nodes)
            {
                try
                {
                    var el = doc.GetElement(new ElementId(kv.Key));
                    ConnectorManager cm = null;
                    if (el is MEPCurve mc) cm = mc.ConnectorManager;
                    else if (el is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
                    if (cm == null) continue;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (c == null) continue;
                        ConnectorSet refs;
                        try { refs = c.AllRefs; } catch { continue; }
                        if (refs == null) continue;
                        foreach (Connector other in refs)
                        {
                            try
                            {
                                var ownerId = other.Owner?.Id.Value ?? 0;
                                if (ownerId == 0 || ownerId == kv.Key) continue;
                                if (!nodes.ContainsKey(ownerId)) continue;
                                if (!kv.Value.Neighbours.Contains(ownerId))
                                    kv.Value.Neighbours.Add(ownerId);
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CableRouter adjacency {kv.Key}: {ex.Message}"); }
            }
            return nodes;
        }

        // ---- Dijkstra --------------------------------------------------------

        private static List<long> Dijkstra(Dictionary<long, TrayNode> graph, long src, long dst)
        {
            var dist = new Dictionary<long, double>();
            var prev = new Dictionary<long, long>();
            var queue = new PriorityQueue<long, double>();
            foreach (var k in graph.Keys) dist[k] = double.PositiveInfinity;
            dist[src] = 0;
            queue.Enqueue(src, 0);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur == dst) break;
                if (!graph.TryGetValue(cur, out var curNode)) continue;
                foreach (var nb in curNode.Neighbours)
                {
                    if (!graph.TryGetValue(nb, out var nbNode)) continue;
                    double alt = dist[cur] + nbNode.LengthM;
                    if (alt < dist[nb])
                    {
                        dist[nb] = alt;
                        prev[nb] = cur;
                        queue.Enqueue(nb, alt);
                    }
                }
            }

            if (!prev.ContainsKey(dst) && src != dst) return new List<long>();
            var path = new List<long> { dst };
            var c = dst;
            while (prev.ContainsKey(c)) { c = prev[c]; path.Add(c); }
            path.Reverse();
            return path;
        }

        // ---- helpers ---------------------------------------------------------

        private static long NearestTrayId(Dictionary<long, TrayNode> graph, XYZ pt, double radiusFt)
        {
            long best = 0;
            double bestDist = double.MaxValue;
            foreach (var kv in graph)
            {
                if (kv.Value.Midpoint == null) continue;
                double d = kv.Value.Midpoint.DistanceTo(pt);
                if (d <= radiusFt && d < bestDist)
                {
                    bestDist = d;
                    best = kv.Key;
                }
            }
            return best;
        }

        private static XYZ LocationOf(Element el)
        {
            try
            {
                if (el.Location is LocationPoint lp) return lp.Point;
                if (el.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
                var bb = el.get_BoundingBox(null);
                if (bb != null) return 0.5 * (bb.Min + bb.Max);
            }
            catch { }
            return null;
        }
    }
}
