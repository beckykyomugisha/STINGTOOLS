// Healthcare Pack H-7 — Medical Gas diversified-flow solver.
// Aggregates per-TU design flow into ZVB / branch / plant loads
// applying NFPA 99 §5.1.13 diversity factors.

using StingTools.Standards.NFPA99;
using System.Collections.Generic;
using System.Linq;

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
                        sum += LookupDouble(tu);
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

        private static double LookupDouble(MgasNode tu) => 0.0; // Phase H-7 stub — extends to read MGS_DESIGN_FLOW_LPM_NR per TU
    }
}
