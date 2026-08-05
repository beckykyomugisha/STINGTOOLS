using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.BOQ
{
    /// <summary>
    /// BOQ-9 — Carbon pivot by phase / level.
    ///
    /// Aggregates BOQ line-item carbon by (PhaseCreated, Level) so a
    /// sustainability report can break "tCO₂e per storey, per phase"
    /// in one click. Pure read over a built BOQDocument + the host
    /// document for the phase + level lookups.
    /// </summary>
    public class CarbonPivotRow
    {
        public string Phase { get; set; } = "";
        public string Level { get; set; } = "";
        public int ElementCount { get; set; }
        public double TotalCarbonKg { get; set; }
        public double TotalCostUGX { get; set; }
    }

    public class CarbonPivotResult
    {
        public List<CarbonPivotRow> Rows { get; } = new List<CarbonPivotRow>();
        public double GrandTotalCarbonKg => Rows.Sum(r => r.TotalCarbonKg);
        public double GrandTotalCostUGX => Rows.Sum(r => r.TotalCostUGX);
    }

    public static class CarbonPivotByPhaseLevel
    {
        public static CarbonPivotResult Build(Document doc, BOQDocument boq)
        {
            var result = new CarbonPivotResult();
            if (doc == null || boq == null) return result;
            var acc = new Dictionary<(string phase, string lvl), CarbonPivotRow>();

            foreach (var item in boq.AllItems)
            {
                string phase = item.RevitElementId >= 0
                    ? ReadPhaseName(doc, item.RevitElementId) ?? "(no phase)"
                    : "(no phase)";
                string lvl = string.IsNullOrEmpty(item.Level) ? "(no level)" : item.Level;
                var key = (phase, lvl);
                if (!acc.TryGetValue(key, out var r))
                {
                    r = new CarbonPivotRow { Phase = phase, Level = lvl };
                    acc[key] = r;
                }
                r.ElementCount++;
                r.TotalCarbonKg += item.EmbodiedCarbonKg;
                r.TotalCostUGX  += item.TotalUGX;
            }
            result.Rows.AddRange(acc.Values
                .OrderBy(r => r.Phase).ThenBy(r => r.Level));
            return result;
        }

        public static string WriteCsv(Document doc, CarbonPivotResult result)
        {
            string outDir = Core.OutputLocationHelper.GetOutputDirectory(doc);
            string path = Path.Combine(outDir,
                $"STING_carbon_by_phase_level_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Phase,Level,ElementCount,TotalCarbonKg,TotalCostUGX");
            foreach (var r in result.Rows)
                sb.AppendLine($"\"{r.Phase}\",\"{r.Level}\",{r.ElementCount},{r.TotalCarbonKg:F1},{r.TotalCostUGX:F0}");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static string ReadPhaseName(Document doc, long elementId)
        {
            try
            {
                var el = doc.GetElement(new ElementId(elementId));
                if (el == null) return null;
                var pp = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                if (pp == null || pp.StorageType != StorageType.ElementId) return null;
                var pid = pp.AsElementId();
                if (pid == null || pid.Value <= 0) return null;
                return doc.GetElement(pid)?.Name;
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CarbonPivot.Phase", $"ReadPhaseName: {ex.Message}"); return null; }
        }
    }
}
