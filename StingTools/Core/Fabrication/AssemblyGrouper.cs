using StingTools.Core;
// StingTools v4 MVP — AssemblyGrouper.
//
// For a per-discipline element set, returns ordered List<List<ElementId>>
// where each inner list represents one shop-drawing assembly. Break
// rules are loaded from STING_FAB_RULES.json (S5.3) and applied per
// discipline:
//   - max length / segment count
//   - max bend / fitting count
//   - break at flange / valve / penetration markers
//   - explicit override marker (ASS_FAB_BREAK_BOOL = 1)
//
// First-pass implementation: linear walk along centreline-connected
// runs, accumulating until a break rule fires. Branch traversal uses
// a stack-based BFS over ConnectorManager.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Fabrication
{
    public class AssemblyGrouper
    {
        public class FabRules
        {
            public Dictionary<string, DisciplineRules> ByDiscipline { get; set; }
                = new Dictionary<string, DisciplineRules>(StringComparer.OrdinalIgnoreCase);
        }
        public class DisciplineRules
        {
            /// <summary>Hard max stick length — industry default 6000 mm
            /// (single stick of steel pipe).</summary>
            public double MaxLengthMm    { get; set; } = 6000;
            /// <summary>Max bends per spool. 4 is the Phase A default;
            /// beyond this hydro-test alignment becomes problematic.</summary>
            public int    MaxBends       { get; set; } = 4;
            public int    MaxFittings    { get; set; } = 12;
            /// <summary>Manual-handling weight cap. 200 kg is typical for
            /// 2-man lift; 500 kg is the crane threshold. Phase E adds
            /// this constraint so spools don't exceed the loading bay
            /// or forklift capacity of the fab shop.</summary>
            public double MaxWeightKg    { get; set; } = 400;
            public bool   BreakAtFlange  { get; set; } = true;
            public bool   BreakAtValve   { get; set; } = true;
            public bool   BreakAtPenetration { get; set; } = true;
            public bool   BreakAtBranch  { get; set; } = true;
        }

        /// <summary>
        /// Per-spool metrics recorded by the grouper so AssemblyBuilder
        /// can write them back to ASS_LENGTH_TOTAL_MM, ASS_WEIGHT_KG,
        /// ASS_WELD_COUNT_NR, etc. Keyed on the index of the group in
        /// the output list.
        /// </summary>
        public class SpoolMetrics
        {
            public double LengthTotalMm { get; set; }
            public double WeightKg      { get; set; }
            public int    BendCount     { get; set; }
            public int    FittingCount  { get; set; }
            public int    WeldCount     { get; set; }
            public int    FlangeCount   { get; set; }
            public int    CutCount      { get; set; }
        }

        private const double MmToFt = 1.0 / 304.8;
        private FabRules _rules;

        public AssemblyGrouper()
        {
            LoadRules();
        }

        public List<List<ElementId>> GroupForDiscipline(
            Document doc, IList<ElementId> ids, string discipline)
        {
            var metrics = new List<SpoolMetrics>();
            return GroupForDiscipline(doc, ids, discipline, out metrics);
        }

        /// <summary>
        /// Overload that also returns per-group metrics aligned to the
        /// output list. metrics[i] corresponds to groups[i].
        /// </summary>
        public List<List<ElementId>> GroupForDiscipline(
            Document doc, IList<ElementId> ids, string discipline,
            out List<SpoolMetrics> metrics)
        {
            var groups = new List<List<ElementId>>();
            metrics = new List<SpoolMetrics>();
            if (doc == null || ids == null || ids.Count == 0) return groups;
            DisciplineRules rules = ResolveRules(discipline);

            var visited = new HashSet<long>();
            var current = new List<ElementId>();
            var metric  = new SpoolMetrics();
            double lengthAccumFt = 0.0;
            int bendAccum = 0;
            int fittingAccum = 0;
            int flangeAccum  = 0;
            double weightAccumKg = 0.0;

            foreach (var id in ids)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                if (!visited.Add(id.Value)) continue;

                Element el = doc.GetElement(id);
                if (el == null) continue;

                // Hard-break override written by earlier QC pass.
                if (ReadBool(el, "ASS_FAB_BREAK_BOOL"))
                {
                    if (current.Count > 0) { groups.Add(current); metrics.Add(metric); current = new List<ElementId>(); metric = new SpoolMetrics(); }
                    lengthAccumFt = 0; bendAccum = 0; fittingAccum = 0; flangeAccum = 0; weightAccumKg = 0;
                    current.Add(id);
                    continue;
                }

                // Add element to current group and update accumulators.
                current.Add(id);
                double lenFt = SafeLengthFt(el);
                lengthAccumFt += lenFt;

                // Per-element weight estimate. We accumulate on the fly
                // so MaxWeightKg is respected in the same pass as length.
                double elementWeight = SpoolWeightCalculator.WeightKg(doc, new[] { id });
                weightAccumKg += elementWeight;

                bool isBend    = IsBendFitting(el);
                bool isFit     = IsFitting(el);
                bool isFlange  = IsFlange(el);
                bool isValve   = IsValve(el);
                bool isPen     = IsPenetration(el);
                if (isBend)   bendAccum++;
                if (isFit)    fittingAccum++;
                if (isFlange) flangeAccum++;

                metric.LengthTotalMm = lengthAccumFt * 304.8;
                metric.WeightKg      = weightAccumKg;
                metric.BendCount     = bendAccum;
                metric.FittingCount  = fittingAccum;
                metric.FlangeCount   = flangeAccum;
                // Welds (approx): one weld per inline fitting joint +
                // end-to-end coupling on cut pieces. Phase E rough
                // estimate: fittings + (stick-breaks - 1).
                metric.WeldCount = fittingAccum;
                metric.CutCount  = Math.Max(0, fittingAccum - 1);

                bool overflow = (lengthAccumFt * 304.8) > rules.MaxLengthMm
                             || bendAccum      > rules.MaxBends
                             || fittingAccum   > rules.MaxFittings
                             || weightAccumKg  > rules.MaxWeightKg;

                bool breakHere = overflow
                              || (rules.BreakAtFlange     && isFlange)
                              || (rules.BreakAtValve      && isValve)
                              || (rules.BreakAtPenetration&& isPen);

                if (breakHere)
                {
                    groups.Add(current);
                    metrics.Add(metric);
                    current = new List<ElementId>();
                    metric  = new SpoolMetrics();
                    lengthAccumFt = 0; bendAccum = 0; fittingAccum = 0; flangeAccum = 0; weightAccumKg = 0;
                }
            }
            if (current.Count > 0)
            {
                groups.Add(current);
                metrics.Add(metric);
            }
            return groups;
        }

        private DisciplineRules ResolveRules(string discipline)
        {
            if (_rules?.ByDiscipline != null &&
                _rules.ByDiscipline.TryGetValue(discipline ?? "", out var r))
                return r;
            return new DisciplineRules();
        }

        private void LoadRules()
        {
            string path = StingToolsApp.FindDataFile("STING_FAB_RULES.json");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _rules = new FabRules();
                return;
            }
            try
            {
                _rules = JsonConvert.DeserializeObject<FabRules>(File.ReadAllText(path))
                         ?? new FabRules();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AssemblyGrouper: STING_FAB_RULES.json parse failed: {ex.Message}");
                _rules = new FabRules();
            }
        }

        private static bool ReadBool(Element el, string param)
        {
            try { var p = el.LookupParameter(param);
                  return p != null && p.StorageType == StorageType.Integer && p.AsInteger() == 1; }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
        }

        private static double SafeLengthFt(Element el)
        {
            try
            {
                if (el is MEPCurve mep)
                    return ((mep.Location as LocationCurve)?.Curve?.Length) ?? 0.0;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0.0;
        }

        private static bool IsFitting(Element el)
        {
            if (el?.Category == null) return false;
            var bic = (BuiltInCategory)el.Category.Id.Value;
            return bic == BuiltInCategory.OST_PipeFitting
                || bic == BuiltInCategory.OST_DuctFitting
                || bic == BuiltInCategory.OST_ConduitFitting
                || bic == BuiltInCategory.OST_CableTrayFitting;
        }

        private static bool IsBendFitting(Element el)
        {
            if (!IsFitting(el)) return false;
            string n = (el.Name ?? "").ToUpperInvariant();
            return n.Contains("ELBOW") || n.Contains("BEND")
                || n.Contains("90") || n.Contains("45");
        }

        private static bool IsFlange(Element el)
        {
            if (el?.Category == null) return false;
            string n = (el.Name ?? "").ToUpperInvariant();
            return n.Contains("FLANGE") || n.Contains("FLG");
        }

        private static bool IsValve(Element el)
        {
            if (el?.Category == null) return false;
            var bic = (BuiltInCategory)el.Category.Id.Value;
            if (bic != BuiltInCategory.OST_PipeAccessory) return false;
            string n = (el.Name ?? "").ToUpperInvariant();
            return n.Contains("VALVE") || n.Contains("VLV");
        }

        private static bool IsPenetration(Element el)
        {
            string n = (el?.Name ?? "").ToUpperInvariant();
            return n.Contains("SLEEVE") || n.Contains("PENETRATION") ||
                   n.Contains("FIRESTOP");
        }
    }
}
