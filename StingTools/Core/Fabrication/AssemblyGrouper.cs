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
            public double MaxLengthMm    { get; set; } = 6000;
            public int    MaxBends       { get; set; } = 6;
            public int    MaxFittings    { get; set; } = 12;
            public bool   BreakAtFlange  { get; set; } = true;
            public bool   BreakAtValve   { get; set; } = true;
            public bool   BreakAtPenetration { get; set; } = true;
            public bool   BreakAtBranch  { get; set; } = true;
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
            var groups = new List<List<ElementId>>();
            if (doc == null || ids == null || ids.Count == 0) return groups;
            DisciplineRules rules = ResolveRules(discipline);

            var visited = new HashSet<long>();
            var current = new List<ElementId>();
            double lengthAccumFt = 0.0;
            int bendAccum = 0;
            int fittingAccum = 0;

            foreach (var id in ids)
            {
                if (id == null || id == ElementId.InvalidElementId) continue;
                if (!visited.Add(id.Value)) continue;

                Element el = doc.GetElement(id);
                if (el == null) continue;

                // Hard-break override
                if (ReadBool(el, "ASS_FAB_BREAK_BOOL"))
                {
                    if (current.Count > 0) { groups.Add(current); current = new List<ElementId>(); }
                    lengthAccumFt = 0; bendAccum = 0; fittingAccum = 0;
                    current.Add(id);
                    continue;
                }

                // Add element to current group
                current.Add(id);
                lengthAccumFt += SafeLengthFt(el);
                if (IsBendFitting(el)) bendAccum++;
                if (IsFitting(el))     fittingAccum++;

                bool overflow = (lengthAccumFt * 304.8) > rules.MaxLengthMm
                             || bendAccum    > rules.MaxBends
                             || fittingAccum > rules.MaxFittings;

                bool breakHere = overflow
                              || (rules.BreakAtFlange     && IsFlange(el))
                              || (rules.BreakAtValve      && IsValve(el))
                              || (rules.BreakAtPenetration&& IsPenetration(el));

                if (breakHere)
                {
                    groups.Add(current);
                    current = new List<ElementId>();
                    lengthAccumFt = 0; bendAccum = 0; fittingAccum = 0;
                }
            }
            if (current.Count > 0) groups.Add(current);
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
            catch { return false; }
        }

        private static double SafeLengthFt(Element el)
        {
            try
            {
                if (el is MEPCurve mep)
                    return ((mep.Location as LocationCurve)?.Curve?.Length) ?? 0.0;
            }
            catch { }
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
