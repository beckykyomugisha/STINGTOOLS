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

        /// <summary>Per-material veto channel consulted by the PBR pipeline
        /// before applying a pack. Returns <c>(allow:true, reason:null)</c>
        /// when nothing objects; <c>(allow:false, reason:"…")</c> when a gate
        /// blocks. Reasons surface in the inspector toast so the user knows
        /// what to fix. Each gate is wrapped in its own try/catch so a buggy
        /// gate can't block every PBR apply project-wide.</summary>
        public static (bool allow, string reason) CheckPbrApply(Document doc, Material mat, string packId)
        {
            if (doc == null || mat == null) return (true, null);

            // Frozen materials — PBR changes appearance which a "Frozen"
            // lifecycle state intentionally forbids.
            try
            {
                var row = MaterialRowBuilder.BuildOne(doc, mat);
                if (MaterialLifecycle.IsFrozen(row))
                    return (false, $"'{mat.Name}' is at lifecycle state Frozen — unfreeze or duplicate before applying PBR.");
            }
            catch (Exception ex) { StingLog.WarnRateLimited("PbrVetoLifecycle", $"PBR lifecycle veto: {ex.Message}"); }

            // Healthcare clinical / radiation-shielding materials carry
            // certification metadata — silently swapping textures on them
            // would invalidate the project's compliance trail.
            try
            {
                var hcFindings = HealthcareMaterialGate.RunAll(doc);
                if (hcFindings != null)
                {
                    foreach (var f in hcFindings)
                    {
                        if (!string.Equals(f.Severity, "Block", StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(f.MaterialName, mat.Name, StringComparison.OrdinalIgnoreCase))
                            return (false, $"Healthcare gate blocks '{mat.Name}': {f.Message}");
                    }
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("PbrVetoHC", $"PBR healthcare veto: {ex.Message}"); }

            return (true, null);
        }
    }
}
