// Healthcare Pack H-7 — Medical Gas diversified-flow solver.
// Aggregates per-TU design flow into ZVB / branch / plant loads
// applying NFPA 99 §5.1.13 diversity factors.

using System;
using Autodesk.Revit.DB;
using StingTools.Standards.NFPA99;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core;

namespace StingTools.Core.MedGas
{
    public class GasZoneLoad
    {
        public string GasCode;
        public string ZoneRef;
        public int TerminalCount;
        public double SumDesignFlowLpm;
        public double DiversifiedFlowLpm;
    }

    public static class MgasFlowSolver
    {
        public static List<GasZoneLoad> Solve(MgasNetwork net)
        {
            var loads = new List<GasZoneLoad>();
            if (net == null) return loads;
            var doc = net.SourceDoc;

            foreach (var (gas, nodes) in net.Nodes)
            {
                var diversity = NFPA99Standards.GetDiversity(gas) ?? 1.0;
                var byZone = nodes.Where(n => n.Role == "TU")
                                  .GroupBy(n => n.Tag ?? "(unzoned)");
                foreach (var grp in byZone)
                {
                    double sum = 0; int count = 0;
                    foreach (var tu in grp)
                    {
                        sum += LookupTuFlow(doc, tu);
                        count++;
                    }
                    loads.Add(new GasZoneLoad
                    {
                        GasCode = gas, ZoneRef = grp.Key,
                        TerminalCount = count,
                        SumDesignFlowLpm = sum,
                        DiversifiedFlowLpm = sum * diversity
                    });
                }
            }
            return loads;
        }

        // Read MGS_DESIGN_FLOW_LPM_NR off the terminal-unit family instance.
        // Falls back to a per-gas heuristic (HTM 02-01 Annex A indicative
        // per-terminal flow) when the parameter is missing or zero.
        private static double LookupTuFlow(Document doc, MgasNode tu)
        {
            if (doc == null || tu?.Id == null) return DefaultTuFlow(tu);
            try
            {
                var el = doc.GetElement(tu.Id);
                var p = el?.LookupParameter("MGS_DESIGN_FLOW_LPM_NR");
                if (p != null && p.HasValue)
                {
                    double v = p.StorageType == StorageType.Double  ? p.AsDouble()
                              : p.StorageType == StorageType.Integer ? p.AsInteger()
                              : (double.TryParse(p.AsString(), out var s) ? s : 0);
                    if (v > 0) return v;
                }
            } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return DefaultTuFlow(tu);
        }

        // HTM 02-01 Annex A indicative per-TU sustained flow (l/min, free air)
        // when MGS_DESIGN_FLOW_LPM_NR is not authored on the family.
        private static double DefaultTuFlow(MgasNode tu) => tu?.GasCode switch
        {
            "O2"   => 10.0,    // continuous oxygen ward bed-space
            "MA4"  => 80.0,    // medical air patient
            "MA7"  => 350.0,   // surgical air drill burst → averaged
            "N2O"  => 6.0,
            "N2"   => 100.0,
            "CO2"  => 6.0,
            "HE"   => 6.0,
            "VAC"  => 40.0,    // vacuum FAD per BS EN ISO 7396-1
            "AGS"  => 75.0,    // AGS standard
            "DENT" => 50.0,
            _      => 0.0
        };
    }
}
