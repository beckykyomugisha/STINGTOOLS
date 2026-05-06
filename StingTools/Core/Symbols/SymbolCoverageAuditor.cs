// StingTools — coverage auditor (Phase 175)
//
// Reports how many MEP elements in a project (or single view) carry a
// STING symbol overlay vs. how many are still uncovered, broken down by
// category and level.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Symbols
{
    public sealed class CoverageReport
    {
        public int TotalMEPElements { get; set; }
        public int CoveredElements { get; set; }
        public int UncoveredElements { get; set; }
        public double CoveragePercent => TotalMEPElements == 0 ? 0
            : 100.0 * CoveredElements / TotalMEPElements;
        public Dictionary<string, int> UncoveredByCategory { get; set; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> UncoveredByLevel { get; set; }
            = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<ElementId> UncoveredIds { get; set; } = new List<ElementId>();
    }

    public static class SymbolCoverageAuditor
    {
        private static readonly BuiltInCategory[] MepCategories = new[]
        {
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_DuctTerminal,
        };

        public static CoverageReport AuditCoverage(Document doc, View view = null)
        {
            var report = new CoverageReport();
            if (doc == null) return report;

            try
            {
                var filter = new ElementMulticategoryFilter(MepCategories);
                FilteredElementCollector col = view != null
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);
                var elements = col
                    .WhereElementIsNotElementType()
                    .WherePasses(filter)
                    .ToList();
                report.TotalMEPElements = elements.Count;

                // Build covered set from existing symbol tags.
                var coveredHostIds = new HashSet<int>();
                FilteredElementCollector tagCol = view != null
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);
                foreach (IndependentTag tag in tagCol
                    .OfClass(typeof(IndependentTag))
                    .Cast<IndependentTag>())
                {
                    var p = tag.LookupParameter("STING_HOST_ELEMENT_ID");
                    if (p == null || string.IsNullOrEmpty(p.AsString())) continue;
                    if (long.TryParse(p.AsString(), out var raw))
                        coveredHostIds.Add((int)raw);
                }

                foreach (var el in elements)
                {
                    bool covered = coveredHostIds.Contains(el.Id.IntegerValue);
                    if (covered) { report.CoveredElements++; continue; }

                    report.UncoveredElements++;
                    report.UncoveredIds.Add(el.Id);

                    string cat = el.Category?.Name ?? "<unknown>";
                    report.UncoveredByCategory[cat] =
                        report.UncoveredByCategory.TryGetValue(cat, out var c) ? c + 1 : 1;

                    string lvl = ResolveLevelName(doc, el);
                    report.UncoveredByLevel[lvl] =
                        report.UncoveredByLevel.TryGetValue(lvl, out var lc) ? lc + 1 : 1;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"AuditCoverage: {ex.Message}");
            }
            return report;
        }

        public static string GenerateCoverageReport(Document doc)
        {
            var r = AuditCoverage(doc);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("STING Symbol Coverage");
            sb.AppendLine($"  Total MEP elements: {r.TotalMEPElements}");
            sb.AppendLine($"  Covered          : {r.CoveredElements}");
            sb.AppendLine($"  Uncovered        : {r.UncoveredElements}");
            sb.AppendLine($"  Coverage         : {r.CoveragePercent:F1}%");
            if (r.UncoveredByCategory.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Uncovered by category:");
                foreach (var kv in r.UncoveredByCategory.OrderByDescending(kv => kv.Value).Take(10))
                    sb.AppendLine($"  {kv.Key,-30} {kv.Value,5}");
            }
            return sb.ToString();
        }

        private static string ResolveLevelName(Document doc, Element el)
        {
            try
            {
                var levelId = el.LevelId;
                if (levelId != null && levelId != ElementId.InvalidElementId)
                    return (doc.GetElement(levelId) as Level)?.Name ?? "<no level>";
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveLevelName: {ex.Message}"); }
            return "<no level>";
        }
    }
}
