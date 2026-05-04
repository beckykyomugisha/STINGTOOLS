// StingTools — Circuit walker.
//
// Given an ElectricalSystem (Revit circuit), enumerates the conduit
// and cable-tray segments that the circuit's cabling routes through.
// Approach: start from the panel + each load element on the circuit,
// expand outward through connected conduits / fittings / cable trays
// up to a depth bound, and intersect the BFS frontiers reachable from
// both endpoints. Any segment in that intersection is on the run.
//
// Falls back to "every conduit whose nearest panel == this panel"
// when topology can't be reconstructed (unconnected conduits etc.).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Electrical
{
    public class CircuitRouteResult
    {
        public List<Element> Conduits   { get; } = new List<Element>();
        public List<Element> CableTrays { get; } = new List<Element>();
        public double        TotalLengthMm { get; set; }
        public List<string>  Warnings   { get; } = new List<string>();

        public IEnumerable<Element> AllSegments => Conduits.Concat(CableTrays);
    }

    public static class CircuitWalker
    {
        private const double FtToMm = 304.8;

        public static CircuitRouteResult Walk(Document doc, ElectricalSystem sys)
        {
            var result = new CircuitRouteResult();
            if (doc == null || sys == null) return result;

            var endpoints = new List<Element>();
            try
            {
                var panel = sys.BaseEquipment;
                if (panel != null) endpoints.Add(panel);
                if (sys.Elements != null)
                    foreach (Element e in sys.Elements)
                        if (e != null) endpoints.Add(e);
            }
            catch (Exception ex) { result.Warnings.Add("collect endpoints: " + ex.Message); }

            if (endpoints.Count == 0)
            {
                result.Warnings.Add("Circuit has no base equipment or load elements.");
                return result;
            }

            var visitedPerEndpoint = new List<HashSet<long>>();
            foreach (var ep in endpoints)
            {
                var reachable = BfsFromElement(ep, doc, maxDepth: 12);
                visitedPerEndpoint.Add(reachable);
            }

            var unionOfEndpoints = new HashSet<long>();
            foreach (var s in visitedPerEndpoint) unionOfEndpoints.UnionWith(s);

            // A conduit/tray segment "on the run" is one reachable from at
            // least two distinct endpoints on the circuit, OR (for radial
            // single-load circuits) any segment between the panel and any
            // load. Use a simpler heuristic: any segment reachable from
            // >= 2 endpoints is part of the route trunk.
            var counts = new Dictionary<long, int>();
            foreach (var s in visitedPerEndpoint)
                foreach (var id in s)
                    counts[id] = counts.TryGetValue(id, out int c) ? c + 1 : 1;

            foreach (var kv in counts)
            {
                if (kv.Value < 2) continue;
                var el = doc.GetElement(new ElementId(kv.Key));
                if (el == null) continue;
                var bic = el.Category?.Id?.Value ?? 0;
                if (bic == (long)BuiltInCategory.OST_Conduit) result.Conduits.Add(el);
                else if (bic == (long)BuiltInCategory.OST_CableTray) result.CableTrays.Add(el);
            }

            // Single-endpoint fallback: include every conduit reachable from
            // the panel within depth 8, when the intersection set was empty.
            if (result.Conduits.Count == 0 && result.CableTrays.Count == 0
                && visitedPerEndpoint.Count > 0)
            {
                foreach (var id in visitedPerEndpoint[0])
                {
                    var el = doc.GetElement(new ElementId(id));
                    if (el == null) continue;
                    var bic = el.Category?.Id?.Value ?? 0;
                    if (bic == (long)BuiltInCategory.OST_Conduit) result.Conduits.Add(el);
                    else if (bic == (long)BuiltInCategory.OST_CableTray) result.CableTrays.Add(el);
                }
            }

            double totalFt = 0.0;
            foreach (var seg in result.AllSegments)
            {
                try
                {
                    var lc = seg.Location as LocationCurve;
                    if (lc?.Curve != null) totalFt += lc.Curve.Length;
                }
                catch { }
            }
            result.TotalLengthMm = totalFt * FtToMm;

            return result;
        }

        private static HashSet<long> BfsFromElement(Element seed, Document doc, int maxDepth)
        {
            var visited = new HashSet<long>();
            if (seed == null) return visited;
            visited.Add(seed.Id.Value);

            var frontier = new List<Element> { seed };
            for (int d = 0; d < maxDepth && frontier.Count > 0; d++)
            {
                var next = new List<Element>();
                foreach (var el in frontier)
                {
                    var connectors = ExtractEndConnectors(el);
                    if (connectors == null) continue;
                    foreach (var c in connectors)
                    {
                        ConnectorSet refs;
                        try { refs = c.AllRefs; } catch { continue; }
                        if (refs == null) continue;
                        foreach (Connector other in refs)
                        {
                            var owner = other?.Owner;
                            if (owner == null) continue;
                            long oid = owner.Id.Value;
                            if (visited.Contains(oid)) continue;
                            var bic = owner.Category?.Id?.Value ?? 0;
                            if (bic != (long)BuiltInCategory.OST_Conduit
                             && bic != (long)BuiltInCategory.OST_ConduitFitting
                             && bic != (long)BuiltInCategory.OST_CableTray
                             && bic != (long)BuiltInCategory.OST_CableTrayFitting
                             && bic != (long)BuiltInCategory.OST_ElectricalEquipment
                             && bic != (long)BuiltInCategory.OST_ElectricalFixtures
                             && bic != (long)BuiltInCategory.OST_LightingFixtures
                             && bic != (long)BuiltInCategory.OST_LightingDevices)
                                continue;
                            visited.Add(oid);
                            next.Add(owner);
                        }
                    }
                }
                frontier = next;
            }
            return visited;
        }

        private static IEnumerable<Connector> ExtractEndConnectors(Element el)
        {
            ConnectorManager cm = null;
            try
            {
                if (el is MEPCurve mc) cm = mc.ConnectorManager;
                else if (el is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
            }
            catch { return null; }
            if (cm == null) return null;

            var result = new List<Connector>();
            try
            {
                foreach (Connector c in cm.Connectors)
                    if (c.ConnectorType == ConnectorType.End) result.Add(c);
            }
            catch { }
            return result;
        }
    }

    public class WireQuantityRow
    {
        public string ProfileId    { get; set; } = "";
        public string ProfileName  { get; set; } = "";
        public int    Cores        { get; set; }
        public double CsaMm2       { get; set; }
        public double TotalMetres  { get; set; }
        public double TotalKg      { get; set; }
        public int    SegmentCount { get; set; }
    }

    public class WireQuantityReport
    {
        public DateTime Generated  { get; set; } = DateTime.UtcNow;
        public string   ScopeName  { get; set; } = "";
        public List<WireQuantityRow> Rows { get; } = new List<WireQuantityRow>();
        public double TotalMetres => Rows.Sum(r => r.TotalMetres);
        public double TotalKg     => Rows.Sum(r => r.TotalKg);
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class WireQuantityCalculator
    {
        private const double FtToMm = 304.8;

        public static WireQuantityReport Compute(Document doc, View scopeView = null)
        {
            var report = new WireQuantityReport();
            if (doc == null) return report;
            report.ScopeName = scopeView != null ? $"View: {scopeView.Name}" : "Project";

            var systems = new FilteredElementCollector(doc)
                .OfClass(typeof(ElectricalSystem))
                .WhereElementIsNotElementType()
                .Cast<ElectricalSystem>()
                .ToList();

            var map = CircuitWireMap.Load(doc);
            var rowsByProfile = new Dictionary<string, WireQuantityRow>(StringComparer.OrdinalIgnoreCase);

            foreach (var sys in systems)
            {
                string circuitId = sys.Name ?? sys.Id.Value.ToString();
                string profileId = map.GetProfileId(circuitId);
                WireProfile profile = !string.IsNullOrEmpty(profileId)
                    ? WireProfileRegistry.Get(doc, profileId)
                    : null;
                if (profile == null) profile = WireProfileRegistry.FallbackForCircuit(sys);

                var route = CircuitWalker.Walk(doc, sys);
                double lengthMm = 0;
                int    segCount = 0;
                foreach (var seg in route.AllSegments)
                {
                    if (scopeView != null && !IsInView(seg, scopeView)) continue;
                    try
                    {
                        var lc = seg.Location as LocationCurve;
                        if (lc?.Curve != null)
                        {
                            lengthMm += lc.Curve.Length * FtToMm;
                            segCount++;
                        }
                    }
                    catch { }
                }
                if (lengthMm <= 0) continue;

                if (!rowsByProfile.TryGetValue(profile.Id, out var row))
                {
                    row = new WireQuantityRow
                    {
                        ProfileId   = profile.Id,
                        ProfileName = profile.Name,
                        Cores       = profile.Cores,
                        CsaMm2      = profile.CsaMm2,
                    };
                    rowsByProfile[profile.Id] = row;
                }
                row.TotalMetres  += lengthMm / 1000.0;
                row.TotalKg      += (lengthMm / 1000.0) * (profile.WeightKgPerKm / 1000.0) * 1000.0 / 1000.0;
                row.SegmentCount += segCount;
            }

            // Recompute weights cleanly: kg = metres × (weight_kg_per_km / 1000).
            foreach (var row in rowsByProfile.Values)
            {
                var p = WireProfileRegistry.Get(doc, row.ProfileId);
                double weightPerM = (p?.WeightKgPerKm ?? 0) / 1000.0;
                row.TotalKg = row.TotalMetres * weightPerM;
            }

            report.Rows.AddRange(rowsByProfile.Values
                .OrderByDescending(r => r.TotalMetres));
            return report;
        }

        public static string WriteCsv(WireQuantityReport report, string outputDir)
        {
            try
            {
                Directory.CreateDirectory(outputDir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(outputDir, $"wire_quantity_{stamp}.csv");
                using (var w = new StreamWriter(path, false, System.Text.Encoding.UTF8))
                {
                    w.WriteLine("ProfileId,ProfileName,Cores,CsaMm2,SegmentCount,TotalMetres,TotalKg");
                    foreach (var r in report.Rows)
                    {
                        w.WriteLine(string.Join(",",
                            CsvEscape(r.ProfileId),
                            CsvEscape(r.ProfileName),
                            r.Cores.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            r.CsaMm2.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            r.SegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            r.TotalMetres.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            r.TotalKg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
                    }
                }
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn("WireQuantityCalculator.WriteCsv: " + ex.Message);
                return null;
            }
        }

        private static bool IsInView(Element el, View view)
        {
            try
            {
                var col = new FilteredElementCollector(el.Document, view.Id).OfClass(el.GetType());
                return col.Cast<Element>().Any(e => e.Id == el.Id);
            }
            catch { return true; }
        }

        private static string CsvEscape(string v)
        {
            if (v == null) return "";
            if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}

