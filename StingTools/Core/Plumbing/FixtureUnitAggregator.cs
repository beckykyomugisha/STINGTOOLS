// FixtureUnitAggregator — DFU / WSFU graph walk.
// Phase 178c. Reads PLM_DFU_COUNT_INT off plumbing fixtures, falls back
// to BS EN 12056-2 / IPC 2021 category defaults, then BFS-traverses the
// pipe connector graph upstream from each pipe to sum the connected
// fixture load. Output drives DrainageSizer + VentDesigner.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class DfuMapResult
    {
        public Dictionary<ElementId, double> PipeDfu { get; } = new Dictionary<ElementId, double>();
        public Dictionary<ElementId, bool>   PipeIsStack { get; } = new Dictionary<ElementId, bool>();
        public int FixturesScanned { get; set; }
        public int PipesTagged     { get; set; }
        public int PipesWritten    { get; set; }  // PLM_DFU_COUNT_INT stamped
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class FixtureUnitAggregator
    {
        // BS EN 12056-2 + IPC 2021 category defaults (DU / DFU). Used
        // when PLM_DFU_COUNT_INT is unset or zero on the fixture. Keys
        // match Revit family / type-name fragments — case-insensitive.
        private static readonly Dictionary<string, double> CategoryDefaults =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            { "WC",            2.0 },
            { "TOILET",        2.0 },
            { "WATER CLOSET", 2.0 },
            { "URINAL",        0.5 },
            { "BASIN",         0.5 },
            { "LAVATORY",      1.0 },
            { "WHB",           0.5 },
            { "BIDET",         0.5 },
            { "BATH",          0.8 },
            { "SHOWER",        0.6 },
            { "SINK",          0.8 },
            { "KITCHEN",       0.8 },
            { "DISHWASHER",    0.8 },
            { "WASHING",       0.8 },
            { "FLOOR DRAIN",   1.0 },
            { "GULLY",         1.0 },
            { "CLOTHES",       3.0 },
            { "MOP",           1.5 },
        };

        public static double GetFixtureDfu(Element fixture)
        {
            if (fixture == null) return 0;
            try
            {
                var p = fixture.LookupParameter(ParamRegistry.PLM_DFU_COUNT);
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.Integer)
                    {
                        int v = p.AsInteger();
                        if (v > 0) return v;
                    }
                    else if (p.StorageType == StorageType.Double)
                    {
                        double v = p.AsDouble();
                        if (v > 0.001) return v;
                    }
                    else if (p.StorageType == StorageType.String)
                    {
                        if (double.TryParse(p.AsString(), out var v) && v > 0) return v;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string name = "";
            try
            {
                name = ((fixture as FamilyInstance)?.Symbol?.Family?.Name ?? "") + " " +
                       (fixture.Name ?? "");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(name)) return 1.0;
            string upper = name.ToUpperInvariant();
            foreach (var kv in CategoryDefaults)
                if (upper.Contains(kv.Key)) return kv.Value;
            return 1.0;
        }

        public static double GetAccumulatedDfu(Document doc, Pipe pipe)
        {
            if (doc == null || pipe == null) return 0;

            // Direction-aware BFS for drainage: water flows downhill, so
            // "upstream" (toward fixtures) is at higher elevation than the seed
            // pipe. We seed from the downstream end Z and only follow neighbours
            // whose centroid Z is >= the seed minus a small tolerance. Without
            // this filter the BFS walks both ways from the seed and sums every
            // fixture in the connected network — every pipe ends up with the
            // same network-wide DFU total.
            double seedZFt = SeedDownstreamZ(pipe);
            const double zTolFt = 0.05; // ~15 mm slack for nearly-flat runs

            var visited = new HashSet<long>();
            var queue   = new Queue<Element>();
            visited.Add(pipe.Id.Value);
            queue.Enqueue(pipe);

            double sum = 0;
            while (queue.Count > 0)
            {
                var el = queue.Dequeue();
                try
                {
                    ConnectorManager cm = (el as MEPCurve)?.ConnectorManager
                                       ?? (el as FamilyInstance)?.MEPModel?.ConnectorManager;
                    if (cm == null) continue;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector other in c.AllRefs)
                        {
                            var owner = other.Owner;
                            if (owner == null) continue;
                            if (visited.Contains(owner.Id.Value)) continue;

                            // Drop neighbours that are clearly downstream of the
                            // seed pipe (lower Z). Fixture nodes are exempt so a
                            // bath/shower on a near-flat branch still counts.
                            double ownerZ = OwnerZ(owner);
                            var bic = (BuiltInCategory)(owner.Category?.Id?.Value ?? 0);
                            bool isFixture = bic == BuiltInCategory.OST_PlumbingFixtures
                                          || bic == BuiltInCategory.OST_MechanicalEquipment;
                            if (!isFixture && ownerZ + zTolFt < seedZFt) continue;

                            visited.Add(owner.Id.Value);

                            if (isFixture)
                            {
                                sum += GetFixtureDfu(owner);
                                continue;
                            }
                            queue.Enqueue(owner);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            return sum;
        }

        private static double SeedDownstreamZ(Pipe pipe)
        {
            try
            {
                var lc = pipe.Location as LocationCurve;
                if (lc?.Curve == null) return 0;
                var s = lc.Curve.GetEndPoint(0);
                var e = lc.Curve.GetEndPoint(1);
                return Math.Min(s.Z, e.Z);
            }
            catch { return 0; }
        }

        private static double OwnerZ(Element el)
        {
            try
            {
                if (el is MEPCurve mc && mc.Location is LocationCurve lc && lc.Curve != null)
                {
                    var s = lc.Curve.GetEndPoint(0);
                    var e = lc.Curve.GetEndPoint(1);
                    return (s.Z + e.Z) / 2.0;
                }
                if (el.Location is LocationPoint lp) return lp.Point.Z;
                var bb = el.get_BoundingBox(null);
                if (bb != null) return (bb.Min.Z + bb.Max.Z) / 2.0;
            }
            catch { }
            return 0;
        }

        public static DfuMapResult BuildDfuMap(Document doc) => BuildDfuMap(doc, writeBack: false);

        /// <summary>
        /// Walk drainage pipes, accumulate DFU upstream, and (when
        /// <paramref name="writeBack"/>=true) stamp PLM_DFU_COUNT_INT on each
        /// pipe so downstream sizers / schedules / paragraph builders don't
        /// have to re-traverse the connector graph. Caller owns the
        /// Transaction when writeBack is on. PLM_DFU_COUNT_INT already
        /// exists in MR_PARAMETERS.txt (Phase 178b).
        /// </summary>
        public static DfuMapResult BuildDfuMap(Document doc, bool writeBack)
        {
            var r = new DfuMapResult();
            if (doc == null) return r;

            var pipes = new FilteredElementCollector(doc)
                .OfClass(typeof(Pipe)).Cast<Pipe>()
                .Where(IsDrainage).ToList();

            int fixCount = 0;
            try
            {
                fixCount = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
                    .WhereElementIsNotElementType().GetElementCount();
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            r.FixturesScanned = fixCount;

            foreach (var p in pipes)
            {
                try
                {
                    double dfu = GetAccumulatedDfu(doc, p);
                    r.PipeDfu[p.Id] = dfu;
                    r.PipeIsStack[p.Id] = IsVerticalStack(p);
                    if (dfu > 0.001) r.PipesTagged++;

                    if (writeBack && dfu > 0.001)
                    {
                        try
                        {
                            var pp = p.LookupParameter("PLM_DFU_COUNT_INT");
                            if (pp != null && !pp.IsReadOnly)
                            {
                                if (pp.StorageType == StorageType.Integer)      pp.Set((int)Math.Round(dfu));
                                else if (pp.StorageType == StorageType.Double)  pp.Set(dfu);
                                else if (pp.StorageType == StorageType.String)  pp.Set($"{dfu:F1}");
                                r.PipesWritten++;
                            }
                        }
                        catch (Exception exW) { r.Warnings.Add($"DFU writeBack pipe {p.Id}: {exW.Message}"); }
                    }
                }
                catch (Exception ex2)
                {
                    r.Warnings.Add($"BuildDfuMap pipe {p.Id}: {ex2.Message}");
                }
            }
            return r;
        }

        private static bool IsDrainage(Pipe p)
        {
            try
            {
                var sys = (p.MEPSystem?.Name ?? "").ToUpperInvariant();
                if (sys.Contains("SANITARY") || sys.Contains("WASTE")
                 || sys.Contains("SOIL")     || sys.Contains("DRAIN")
                 || sys.Contains("STORM")    || sys.Contains("RAINWATER")
                 || sys.Contains("FOUL"))
                    return true;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        private static bool IsVerticalStack(Pipe p)
        {
            try
            {
                var lc = p.Location as LocationCurve;
                if (lc?.Curve == null) return false;
                var s = lc.Curve.GetEndPoint(0);
                var e = lc.Curve.GetEndPoint(1);
                double dz = Math.Abs(e.Z - s.Z);
                double total = s.DistanceTo(e);
                return total > 1e-6 && (dz / total) > 0.8;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }
    }
}
