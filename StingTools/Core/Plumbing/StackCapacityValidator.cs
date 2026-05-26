// StackCapacityValidator — BS EN 12056-2 §6.5 Table 11 stack-DU
// capacity check + induced-siphonage early warning when a stack
// exceeds 70 % of its rated capacity. Phase 178c.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;
using StingTools.Standards.BSEN12056;

namespace StingTools.Core.Plumbing
{
    public class StackCapacityFinding
    {
        public ElementId StackPipeId { get; set; }
        public int DnMm              { get; set; }
        public double Dfu            { get; set; }
        public double CapacityDu     { get; set; }
        public double UtilisationPct { get; set; }
        public string Severity       { get; set; } = "INFO";
        public string Notes          { get; set; } = "";
    }

    public class StackCapacityReport
    {
        public int StacksScanned     { get; set; }
        public int StacksFlagged     { get; set; }
        public int StacksOverCapacity{ get; set; }
        public int StacksStamped     { get; set; }   // PLM_STACK_CAP_DU + PLM_STACK_UTIL_PCT
        public List<StackCapacityFinding> Findings { get; } = new List<StackCapacityFinding>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class StackCapacityValidator
    {
        public const double InducedSiphonageThresholdPct = 70.0;

        public static StackCapacityReport Validate(Document doc, DfuMapResult dfuMap)
            => Validate(doc, dfuMap, writeBack: false);

        /// <summary>
        /// Walk vertical stacks and report DFU utilisation vs BS EN 12056-2
        /// Table 11. When writeBack=true also stamps PLM_STACK_CAP_DU and
        /// PLM_STACK_UTIL_PCT (Phase 187) on each stack pipe so schedules
        /// and downstream relief-vent auto-sizers can read the result
        /// without re-running the validator. Caller owns the Transaction.
        /// </summary>
        public static StackCapacityReport Validate(Document doc, DfuMapResult dfuMap, bool writeBack)
        {
            var r = new StackCapacityReport();
            if (doc == null || dfuMap == null) return r;

            foreach (var kv in dfuMap.PipeDfu)
            {
                if (!dfuMap.PipeIsStack.TryGetValue(kv.Key, out var isStack) || !isStack) continue;
                Pipe p = null;
                try { p = doc.GetElement(kv.Key) as Pipe; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (p == null) continue;

                int dn = (int)Math.Round(p.Diameter * 0.3048 * 1000.0);
                double cap = BSen12056Standards.GetStackCapacityDu(dn);
                double pct = cap > 0 ? (kv.Value / cap) * 100.0 : 0;
                r.StacksScanned++;

                if (writeBack)
                {
                    try
                    {
                        if (StingTools.Core.ParameterHelpers.SetString(p, "PLM_STACK_CAP_DU",
                                $"{cap:F1}", overwrite: true)) r.StacksStamped++;
                        StingTools.Core.ParameterHelpers.SetString(p, "PLM_STACK_UTIL_PCT",
                            $"{pct:F1}", overwrite: true);
                    }
                    catch (Exception exW) { r.Warnings.Add($"Stack stamp {p.Id}: {exW.Message}"); }
                }

                var f = new StackCapacityFinding
                {
                    StackPipeId    = p.Id,
                    DnMm           = dn,
                    Dfu            = kv.Value,
                    CapacityDu     = cap,
                    UtilisationPct = pct
                };
                if (pct > 100)
                {
                    f.Severity = "ERROR";
                    f.Notes = $"Stack OVER capacity ({pct:F0} %) — upsize to next DN per BS EN 12056-2 Table 11.";
                    r.StacksOverCapacity++;
                    r.StacksFlagged++;
                }
                else if (pct >= InducedSiphonageThresholdPct)
                {
                    f.Severity = "WARN";
                    f.Notes = $"Stack >{InducedSiphonageThresholdPct:F0} % capacity — induced-siphonage risk; verify trap seals or add relief vent.";
                    r.StacksFlagged++;
                }
                else
                {
                    f.Severity = "INFO";
                }
                r.Findings.Add(f);
            }
            return r;
        }
    }
}
