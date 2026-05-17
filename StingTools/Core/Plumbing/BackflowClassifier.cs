// BackflowClassifier — BS EN 1717 fluid category + device selection.
// CrossConnectionChecker walks the connector graph between potable
// supply terminals and non-potable sources, flagging any direct
// connection that lacks a Cat-3+ separation device.
// Phase 178c.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public enum FluidCategory
    {
        Category1 = 1,  // wholesome potable
        Category2 = 2,  // slightly contaminated
        Category3 = 3,  // significantly contaminated (chemicals)
        Category4 = 4,  // seriously contaminated (toxic / microbiological)
        Category5 = 5,  // very seriously contaminated (pathogenic)
    }

    public class BackflowRisk
    {
        public ElementId ElementId        { get; set; }
        public FluidCategory Category     { get; set; } = FluidCategory.Category1;
        public string RecommendedDevice   { get; set; } = "";
        public string StandardReference   { get; set; } = "BS EN 1717 Table 2";
        public string Notes               { get; set; } = "";
        public string SystemName          { get; set; } = "";
    }

    public class CrossConnectionFinding
    {
        public ElementId PotableElementId    { get; set; }
        public ElementId NonPotableElementId { get; set; }
        public FluidCategory NonPotableCategory { get; set; }
        public string Severity { get; set; } = "ERROR";
        public string Notes    { get; set; } = "";
    }

    public static class BackflowClassifier
    {
        private static readonly Dictionary<FluidCategory, string> DeviceMap =
            new Dictionary<FluidCategory, string>
        {
            { FluidCategory.Category1, "" },
            { FluidCategory.Category2, "CHK-SC" },
            { FluidCategory.Category3, "CHK-DC" },
            { FluidCategory.Category4, "VLV-RPZ" },
            { FluidCategory.Category5, "GAP-AA" },
        };

        public static BackflowRisk ClassifyElement(Document doc, Element el)
        {
            var r = new BackflowRisk { ElementId = el?.Id ?? ElementId.InvalidElementId };
            if (el == null) return r;

            try
            {
                var p = el.LookupParameter(ParamRegistry.PLM_FLUID_CAT);
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                {
                    var s = p.AsString();
                    if (int.TryParse((s ?? "").Trim(), out var n) && n >= 1 && n <= 5)
                    {
                        r.Category = (FluidCategory)n;
                        r.RecommendedDevice = DeviceMap[r.Category];
                        return r;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            string sysName = "";
            try
            {
                if (el is MEPCurve mc) sysName = mc.MEPSystem?.Name ?? "";
                else if (el is FamilyInstance fi)
                    sysName = fi.MEPModel?.ConnectorManager?.Connectors?
                              .Cast<Connector>().FirstOrDefault()?.MEPSystem?.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            r.SystemName = sysName;
            string upper = (sysName ?? "").ToUpperInvariant();

            if (upper.Contains("RO") || upper.Contains("DIALYSIS") || upper.Contains("LAB") || upper.Contains("MEDGAS"))
                r.Category = FluidCategory.Category4;
            else if (upper.Contains("POOL") || upper.Contains("SPA") || upper.Contains("IRRIG") || upper.Contains("GARDEN")
                  || upper.Contains("RWH") || upper.Contains("RAINWATER HARVEST") || upper.Contains("GREYWATER"))
                r.Category = FluidCategory.Category3;
            else if (upper.Contains("DHW") || upper.Contains("HWS") || upper.Contains("LTHW") || upper.Contains("HEATING"))
                r.Category = FluidCategory.Category2;
            else if (upper.Contains("FOUL") || upper.Contains("SAN") || upper.Contains("WASTE") || upper.Contains("SEWAGE"))
                r.Category = FluidCategory.Category5;
            else
                r.Category = FluidCategory.Category1;

            r.RecommendedDevice = DeviceMap[r.Category];
            return r;
        }

        public static List<BackflowRisk> ClassifyAll(Document doc)
        {
            var list = new List<BackflowRisk>();
            if (doc == null) return list;
            try
            {
                var pipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>();
                foreach (var p in pipes) list.Add(ClassifyElement(doc, p));
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return list;
        }
    }

    public static class CrossConnectionChecker
    {
        public static List<CrossConnectionFinding> Scan(Document doc)
        {
            var findings = new List<CrossConnectionFinding>();
            if (doc == null) return findings;

            var allPipes = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
            var classification = new Dictionary<long, FluidCategory>();
            foreach (var p in allPipes)
            {
                var r = BackflowClassifier.ClassifyElement(doc, p);
                classification[p.Id.Value] = r.Category;
            }

            foreach (var p in allPipes)
            {
                if (!classification.TryGetValue(p.Id.Value, out var cat)) continue;
                if (cat != FluidCategory.Category1) continue;
                try
                {
                    foreach (Connector c in p.ConnectorManager.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector other in c.AllRefs)
                        {
                            var owner = other.Owner;
                            if (owner == null || owner.Id == p.Id) continue;
                            FluidCategory otherCat = FluidCategory.Category1;
                            if (owner is Pipe op && classification.TryGetValue(op.Id.Value, out var oc))
                                otherCat = oc;
                            else
                                otherCat = BackflowClassifier.ClassifyElement(doc, owner).Category;
                            if (otherCat >= FluidCategory.Category3 && !HasBackflowDeviceBetween(p, owner))
                            {
                                findings.Add(new CrossConnectionFinding
                                {
                                    PotableElementId    = p.Id,
                                    NonPotableElementId = owner.Id,
                                    NonPotableCategory  = otherCat,
                                    Severity            = otherCat == FluidCategory.Category5 ? "CRITICAL" : "ERROR",
                                    Notes               = $"Direct connection from Cat-1 potable to Cat-{(int)otherCat}: " +
                                                          $"{(otherCat == FluidCategory.Category5 ? "AIR GAP MANDATORY" : DeviceFor(otherCat) + " required")}"
                                });
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            return findings;
        }

        private static bool HasBackflowDeviceBetween(Element a, Element b)
        {
            try
            {
                foreach (var el in new[] { a, b })
                {
                    var p = el.LookupParameter(ParamRegistry.PLM_BF_TYPE);
                    if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    {
                        var s = (p.AsString() ?? "").ToUpperInvariant();
                        if (s.Contains("RPZ") || s.Contains("DC") || s.Contains("GAP")) return true;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        private static string DeviceFor(FluidCategory cat) =>
            cat == FluidCategory.Category2 ? "Single check valve (SCV)" :
            cat == FluidCategory.Category3 ? "Double check valve (DCV)" :
            cat == FluidCategory.Category4 ? "RPZ valve" :
                                              "Air gap (Type AA/AB)";
    }
}
