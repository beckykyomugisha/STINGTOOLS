// GasNetworkCheck — pressure drop in a looped (or branched) low-pressure gas
// installation, solved exactly on the network. Revit-free.
//
// Appliances draw their full flow (Q = kW × 3.6 / CV). With the source at
// 0 mbar, PipeNetworkSolver finds every node's pressure; each appliance's
// drop is minus its node pressure. Link law is Pole's formula,
// h = s·L·Q²/(0.0071²·d⁵) (mbar, m³/h, mm, m). Sizing a looped installation
// is not attempted: GasPipeSizer sizes trees; loops are checked here.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Mep.Networks;

namespace StingTools.Core.Gas
{
    public sealed class GasNetworkAppliance
    {
        public NetNode Node { get; set; }
        public double FlowM3h { get; set; }
        public double DropMbar { get; set; }
    }

    public sealed class GasNetworkLink
    {
        public NetLink Link { get; set; }
        public double FlowM3h { get; set; }
        public double DropMbar { get; set; }
        public double VelocityMs { get; set; }
    }

    public sealed class GasNetworkResult
    {
        public bool Ok { get; set; }
        public bool Converged { get; set; }
        public int Loops { get; set; }
        public double TotalLoadKw { get; set; }
        public double TotalFlowM3h { get; set; }
        public double WorstDropMbar { get; set; }
        public NetNode WorstAppliance { get; set; }
        public List<GasNetworkAppliance> Appliances { get; } = new List<GasNetworkAppliance>();
        public List<GasNetworkLink> Links { get; } = new List<GasNetworkLink>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class GasNetworkCheck
    {
        public static double Resistance(NetLink l, double relativeDensity)
        {
            if (l.BoreMm <= 0 || l.TotalLengthM <= 0) return 0;
            return relativeDensity * l.TotalLengthM / (GasPipeSizer.PoleConstant * GasPipeSizer.PoleConstant * Math.Pow(l.BoreMm, 5));
        }

        public static GasNetworkResult Check(PipeNetwork net, GasProperties gas)
        {
            var r = new GasNetworkResult();
            if (net?.Source == null) { r.Warnings.Add("Network has no source."); return r; }
            if (gas == null || gas.RelativeDensity <= 0 || gas.CalorificValueMJm3 <= 0 || gas.MaxDropMbar <= 0)
            { r.Warnings.Add("Gas properties are incomplete."); return r; }
            var apps = net.Terminals.ToList();
            if (apps.Count == 0) { r.Warnings.Add("No appliances in the network."); return r; }
            var unrated = apps.Where(a => a.LoadKw <= 0).ToList();
            if (unrated.Count > 0)
                r.Warnings.Add($"{unrated.Count} appliance(s) have no heat input and carry no flow: " + string.Join(", ", unrated.Take(5).Select(a => a.Label)));
            var noBore = net.Links.Where(l => l.Kind == NetLinkKind.Pipe && l.BoreMm <= 0).ToList();
            if (noBore.Count > 0) { r.Warnings.Add($"{noBore.Count} pipe(s) have no bore."); return r; }

            r.Loops = net.LoopCount;
            var solver = new PipeNetworkSolver(net, l => Resistance(l, gas.RelativeDensity), 2.0, 0.0,
                n => GasPipeSizer.FlowM3h(Math.Max(0, n.LoadKw), gas), _ => 0);
            var sol = solver.Solve(0.0);
            r.Warnings.AddRange(sol.Warnings);
            r.Converged = sol.Converged;

            foreach (var a in apps)
                r.Appliances.Add(new GasNetworkAppliance { Node = a, FlowM3h = sol.Demand[a.Id], DropMbar = -sol.Pressure[a.Id] });
            foreach (var kv in sol.Flow)
            {
                double q = Math.Abs(kv.Value);
                r.Links.Add(new GasNetworkLink
                {
                    Link = kv.Key, FlowM3h = q, DropMbar = Resistance(kv.Key, gas.RelativeDensity) * q * q,
                    VelocityMs = GasPipeSizer.VelocityMs(q, kv.Key.BoreMm)
                });
            }
            r.TotalLoadKw = apps.Sum(a => Math.Max(0, a.LoadKw));
            r.TotalFlowM3h = r.Appliances.Sum(a => a.FlowM3h);
            var worst = r.Appliances.OrderByDescending(a => a.DropMbar).First();
            r.WorstDropMbar = worst.DropMbar;
            r.WorstAppliance = worst.Node;
            r.Ok = sol.Converged && worst.DropMbar <= gas.MaxDropMbar + 1e-9;
            if (worst.DropMbar > gas.MaxDropMbar + 1e-9)
                r.Warnings.Add($"{worst.Node.Label}: {worst.DropMbar:F2} mbar from the source exceeds the {gas.MaxDropMbar:F2} mbar allowance.");
            return r;
        }
    }
}
