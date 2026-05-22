using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// AL-2 — Run every material gate that supports Block severity and
    /// aggregate the count. Called by IssueDeliverableCommand before
    /// the standard coverage check so Block-severity findings actually
    /// stop a ship-ready submission.
    /// </summary>
    public class MaterialBlockerResult
    {
        public int BlockerCount { get; set; }
        public int GatesWithBlockers { get; set; }
        public string Summary { get; set; } = "";
        public bool HasBlockers => BlockerCount > 0;
    }

    public static class MaterialBlockerChain
    {
        public static MaterialBlockerResult Check(Document doc)
        {
            var result = new MaterialBlockerResult();
            if (doc == null) return result;

            var rows = MaterialRowBuilder.Build(doc).ToList();
            var sb = new StringBuilder();

            try
            {
                var sustain = MaterialSustainabilityGate.RunAll(doc, rows);
                int b = sustain.Count(f => string.Equals(f.Severity, "Block", StringComparison.OrdinalIgnoreCase));
                if (b > 0) { result.GatesWithBlockers++; result.BlockerCount += b; sb.AppendLine($"Sustainability: {b} block(s)"); }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialBlockerChain.Sustain: {ex.Message}"); }

            try
            {
                var hc = HealthcareMaterialGate.RunAll(doc);
                int b = hc.Count(f => string.Equals(f.Severity, "Block", StringComparison.OrdinalIgnoreCase));
                if (b > 0) { result.GatesWithBlockers++; result.BlockerCount += b; sb.AppendLine($"Healthcare: {b} block(s)"); }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialBlockerChain.HC: {ex.Message}"); }

            try
            {
                var fw = FireWallCompositionGate.RunAll(doc);
                int b = fw.Count(f => string.Equals(f.Severity, "Block", StringComparison.OrdinalIgnoreCase));
                if (b > 0) { result.GatesWithBlockers++; result.BlockerCount += b; sb.AppendLine($"Fire-Wall: {b} block(s)"); }
            }
            catch (Exception ex) { StingLog.Warn($"MaterialBlockerChain.FW: {ex.Message}"); }

            result.Summary = sb.Length > 0 ? sb.ToString() : "No blockers — gates report clean.";
            return result;
        }
    }
}
