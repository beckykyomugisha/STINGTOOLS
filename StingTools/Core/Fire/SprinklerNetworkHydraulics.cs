// SprinklerNetworkHydraulics — sprinkler design-area calculation on a looped
// or gridded network (and on trees, exactly). Revit-free.
//
// Every operating head is an orifice q = K·√p. For a given source pressure
// PipeNetworkSolver finds every node pressure and pipe flow; the required
// source pressure is the lowest one at which every operating head reaches
// its requirement max(p_min, (q_min/K)²), found by bisection (head pressures
// rise monotonically with source pressure). This is the method hydraulic
// calculation software uses for grids; SprinklerHydraulics' equivalent-K
// junction balancing is the hand method for trees.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Mep.Networks;

namespace StingTools.Core.Fire
{
    public sealed class SprinklerNetworkHead
    {
        public NetNode Node { get; set; }
        public double PressureBar { get; set; }
        public double FlowLpm { get; set; }
        public double RequiredBar { get; set; }
    }

    public sealed class SprinklerNetworkLink
    {
        public NetLink Link { get; set; }
        public double FlowLpm { get; set; }
        public double VelocityMs { get; set; }
        public double FrictionBar { get; set; }
        public bool OverVelocity { get; set; }
    }

    public sealed class SprinklerNetworkResult
    {
        public bool Ok { get; set; }
        public double SourceFlowLpm { get; set; }
        public double SourcePressureBar { get; set; }
        public double MinHeadFlowLpm { get; set; }
        public double AreaPerHeadM2 { get; set; }
        public int Loops { get; set; }
        public SprinklerNetworkHead MostRemoteHead { get; set; }
        public List<SprinklerNetworkHead> Heads { get; } = new List<SprinklerNetworkHead>();
        public List<SprinklerNetworkLink> Links { get; } = new List<SprinklerNetworkLink>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class SprinklerNetworkHydraulics
    {
        public static double Resistance(NetLink l, double defaultC)
        {
            if (l.BoreMm <= 0 || l.TotalLengthM <= 0) return 0;
            double c = l.HazenWilliamsC > 0 ? l.HazenWilliamsC : defaultC;
            return 6.05e5 * l.TotalLengthM / (Math.Pow(c, 1.85) * Math.Pow(l.BoreMm, 4.87));
        }

        public static SprinklerNetworkResult Solve(PipeNetwork net, SprinklerDesignCriteria c)
        {
            var r = new SprinklerNetworkResult();
            if (net?.Source == null) { r.Warnings.Add("Network has no source."); return r; }
            var heads = net.Terminals.ToList();
            if (heads.Count == 0) { r.Warnings.Add("No sprinkler heads in the network."); return r; }
            if (c.DensityMmMin <= 0) { r.Warnings.Add("Design density is not set."); return r; }
            var noK = heads.Where(h => h.KFactor <= 0).ToList();
            if (noK.Count > 0)
            {
                r.Warnings.Add($"{noK.Count} head(s) have no K-factor: " + string.Join(", ", noK.Take(5).Select(h => h.Label)));
                return r;
            }
            var noBore = net.Links.Where(l => l.Kind == NetLinkKind.Pipe && l.BoreMm <= 0).ToList();
            if (noBore.Count > 0) { r.Warnings.Add($"{noBore.Count} pipe(s) have no bore — cannot calculate friction."); return r; }

            double areaPerHead = c.AreaPerHeadM2 > 0 ? c.AreaPerHeadM2 : (c.DesignAreaM2 > 0 ? c.DesignAreaM2 / heads.Count : 0);
            if (areaPerHead <= 0) { r.Warnings.Add("Neither area per head nor design area is set."); return r; }
            r.AreaPerHeadM2 = areaPerHead;
            r.MinHeadFlowLpm = c.DensityMmMin * areaPerHead;
            r.Loops = net.LoopCount;
            if (c.DesignAreaM2 > 0 && heads.Count * areaPerHead + 1e-6 < c.DesignAreaM2)
                r.Warnings.Add($"{heads.Count} heads × {areaPerHead:F1} m² covers {heads.Count * areaPerHead:F0} m², " +
                               $"less than the {c.DesignAreaM2:F0} m² area of operation — select every head in the design area.");

            double Req(NetNode h) => Math.Max(c.MinHeadPressureBar, Math.Pow(r.MinHeadFlowLpm / h.KFactor, 2));
            var solver = new PipeNetworkSolver(net, l => Resistance(l, c.DefaultC), 1.85,
                SprinklerHydraulics.StaticBarPerM, _ => 0, n => n.KFactor);

            NetSolveResult last = null;
            double Margin(double ps)
            {
                last = solver.Solve(ps, last?.Pressure);
                return heads.Min(h => last.Pressure[h.Id] - Req(h));
            }

            // Bracket: grow the source pressure until every head is satisfied.
            double lo = 0, hi = Math.Max(1.0, heads.Max(Req));
            bool bracketed = false;
            for (int guard = 0; guard < 20; guard++)
            {
                if (Margin(hi) >= 0) { bracketed = true; break; }
                lo = hi; hi *= 2;
            }
            if (!bracketed) { r.Warnings.Add($"No source pressure up to {hi:F0} bar satisfies every head."); return r; }
            for (int i = 0; i < 60 && hi - lo > 1e-6; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (Margin(mid) >= 0) hi = mid; else lo = mid;
            }
            var sol = solver.Solve(hi, last?.Pressure);
            r.Warnings.AddRange(sol.Warnings);
            r.SourcePressureBar = hi;

            foreach (var h in heads)
                r.Heads.Add(new SprinklerNetworkHead
                {
                    Node = h, PressureBar = sol.Pressure[h.Id], FlowLpm = sol.Demand[h.Id], RequiredBar = Req(h)
                });
            r.SourceFlowLpm = r.Heads.Sum(h => h.FlowLpm);
            r.MostRemoteHead = r.Heads.OrderBy(h => h.PressureBar - h.RequiredBar).First();

            foreach (var kv in sol.Flow)
            {
                var l = kv.Key;
                double q = Math.Abs(kv.Value);
                double v = SprinklerHydraulics.VelocityMs(q, l.BoreMm);
                double hf = Resistance(l, c.DefaultC) * Math.Pow(q, 1.85);
                bool over = l.Kind == NetLinkKind.Accessory ? v > c.MaxValveVelocityMs : l.Kind == NetLinkKind.Pipe && v > c.MaxVelocityMs;
                r.Links.Add(new SprinklerNetworkLink { Link = l, FlowLpm = q, VelocityMs = v, FrictionBar = hf, OverVelocity = over });
                if (over) r.Warnings.Add($"{l.Label}: {v:F1} m/s exceeds {(l.Kind == NetLinkKind.Accessory ? c.MaxValveVelocityMs : c.MaxVelocityMs):F0} m/s.");
            }
            r.Ok = sol.Converged;
            return r;
        }
    }
}
