// GasPipeSizer — low-pressure gas installation pipe sizing on a tree.
// Revit-free; RevitFlowTreeBuilder supplies the tree, STING_GAS_DESIGN.json
// supplies gas properties, pipe series and pressure-drop budgets.
//
// Flow per appliance:   Q (m³/h) = heat input (kW, gross) × 3.6 / CV (MJ/m³)
// Pressure loss:        Pole's formula, as used in BS 6891 / IGEM/UP/2
//                         Q = 0.0071 · √(h · d⁵ / (s · L))
//                       rearranged  h = s · L · (Q / 0.0071)² / d⁵
//                       h mbar, d internal bore mm, L equivalent length m,
//                       s relative density (air = 1). Valid for low-pressure
//                       (≤ 75 mbar) installation pipework.
//
// Sizing (Size): every node carries the sum of the appliance flows below it
// (no diversity). The drop budget from the source to the furthest appliance
// is spread per metre of equivalent length; each pipe takes the smallest bore
// in the series whose drop per metre fits. Every source→appliance path is
// then checked, and while one exceeds the budget the pipe with the largest
// drop on it is stepped up a size. Check() evaluates the bores as modelled.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Mep.Networks;

namespace StingTools.Core.Gas
{
    public sealed class GasProperties
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        /// <summary>Relative density, air = 1.</summary>
        public double RelativeDensity { get; set; }
        /// <summary>Gross calorific value, MJ/m³.</summary>
        public double CalorificValueMJm3 { get; set; }
        /// <summary>Allowed drop from the source (meter outlet / regulator) to any appliance, mbar.</summary>
        public double MaxDropMbar { get; set; }
        public string Source { get; set; } = "";
    }

    public sealed class GasPipeSize
    {
        public string Label { get; set; } = "";
        public double OuterDiameterMm { get; set; }
        /// <summary>Revit nominal diameter for this size (copper: OD; steel: DN), mm.</summary>
        public double NominalMm { get; set; }
        public double BoreMm { get; set; }
    }

    public sealed class GasNodeResult
    {
        public FlowNode Node { get; set; }
        public double FlowM3h { get; set; }
        public double BoreMm { get; set; }
        public string SizeLabel { get; set; } = "";
        public double DropMbar { get; set; }
        public double VelocityMs { get; set; }
        /// <summary>Bore as modelled before sizing, mm (0 when unknown).</summary>
        public double ModelledBoreMm { get; set; }
        public bool Changed => Node.Kind == FlowNodeKind.Pipe && ModelledBoreMm > 0 && Math.Abs(ModelledBoreMm - BoreMm) > 0.5;
    }

    public sealed class GasSizingResult
    {
        public bool Ok { get; set; }
        public double TotalLoadKw { get; set; }
        public double TotalFlowM3h { get; set; }
        public double WorstPathDropMbar { get; set; }
        public FlowNode WorstAppliance { get; set; }
        public Dictionary<FlowNode, GasNodeResult> Nodes { get; } = new Dictionary<FlowNode, GasNodeResult>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class GasPipeSizer
    {
        public const double PoleConstant = 0.0071;

        public static double FlowM3h(double loadKw, GasProperties gas)
            => gas.CalorificValueMJm3 > 0 ? loadKw * 3.6 / gas.CalorificValueMJm3 : 0;

        /// <summary>Pole's formula pressure drop, mbar.</summary>
        public static double DropMbar(double qM3h, double lengthM, double boreMm, double relativeDensity)
        {
            if (qM3h <= 0 || lengthM <= 0) return 0;
            if (boreMm <= 0) throw new ArgumentOutOfRangeException(nameof(boreMm));
            double r = qM3h / PoleConstant;
            return relativeDensity * lengthM * r * r / Math.Pow(boreMm, 5);
        }

        /// <summary>Pole's formula flow capacity, m³/h.</summary>
        public static double CapacityM3h(double dropMbar, double lengthM, double boreMm, double relativeDensity)
            => PoleConstant * Math.Sqrt(dropMbar * Math.Pow(boreMm, 5) / (relativeDensity * lengthM));

        public static double VelocityMs(double qM3h, double boreMm)
            => boreMm > 0 ? qM3h / 3600.0 / (Math.PI * Math.Pow(boreMm / 1000.0, 2) / 4.0) : 0;

        /// <summary>Evaluate the bores already on the tree.</summary>
        public static GasSizingResult Check(FlowNode root, GasProperties gas)
            => Run(root, gas, null);

        /// <summary>Choose bores from <paramref name="series"/> and evaluate.</summary>
        public static GasSizingResult Size(FlowNode root, GasProperties gas, IList<GasPipeSize> series)
        {
            if (series == null || series.Count == 0) throw new ArgumentException("Pipe series is empty.", nameof(series));
            return Run(root, gas, series.OrderBy(s => s.BoreMm).ToList());
        }

        private static GasSizingResult Run(FlowNode root, GasProperties gas, List<GasPipeSize> series)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (gas == null) throw new ArgumentNullException(nameof(gas));
            var r = new GasSizingResult();
            if (gas.RelativeDensity <= 0 || gas.CalorificValueMJm3 <= 0 || gas.MaxDropMbar <= 0)
            {
                r.Warnings.Add($"Gas '{gas.Id}' is missing relative density, calorific value or drop budget.");
                return r;
            }

            var appliances = root.Descendants().Where(n => n.IsTerminal).ToList();
            if (appliances.Count == 0) { r.Warnings.Add("No appliances on the tree."); return r; }
            var unrated = appliances.Where(a => a.LoadKw <= 0).ToList();
            if (unrated.Count > 0)
                r.Warnings.Add($"{unrated.Count} appliance(s) have no heat input and carry no flow: " +
                               string.Join(", ", unrated.Take(5).Select(a => a.Label)));

            var all = new[] { root }.Concat(root.Descendants()).ToList();
            var flow = new Dictionary<FlowNode, double>();
            double FlowOf(FlowNode n)
            {
                if (flow.TryGetValue(n, out var f)) return f;
                f = (n.IsTerminal ? FlowM3h(n.LoadKw, gas) : 0) + n.Children.Sum(FlowOf);
                flow[n] = f;
                return f;
            }
            foreach (var n in all) FlowOf(n);
            r.TotalLoadKw = appliances.Sum(a => Math.Max(0, a.LoadKw));
            r.TotalFlowM3h = FlowOf(root);

            var modelled = all.ToDictionary(n => n, n => n.DiameterMm);
            var chosen = new Dictionary<FlowNode, GasPipeSize>();

            if (series != null)
            {
                // Budget per metre along the longest (equivalent-length) path.
                double longest = appliances.Max(a => a.PathFromRoot().Sum(n => HydraulicLength(n, 0)));
                double perM = longest > 0 ? gas.MaxDropMbar / longest : gas.MaxDropMbar;
                foreach (var n in all.Where(n => n.Kind == FlowNodeKind.Pipe))
                {
                    double q = flow[n];
                    var pick = series.FirstOrDefault(s => DropMbar(q, 1.0, s.BoreMm, gas.RelativeDensity) <= perM)
                               ?? series.Last();
                    chosen[n] = pick;
                    n.DiameterMm = pick.BoreMm;
                }
                ReinheritFittings(root);

                // Step up the worst pipe on any path over budget.
                for (int guard = 0; guard < 500; guard++)
                {
                    var over = appliances
                        .Select(a => (a, drop: PathDrop(a, flow, gas)))
                        .Where(t => t.drop > gas.MaxDropMbar + 1e-9)
                        .OrderByDescending(t => t.drop).FirstOrDefault();
                    if (over.a == null) break;
                    var worst = over.a.PathFromRoot()
                        .Where(n => n.Kind == FlowNodeKind.Pipe && chosen.ContainsKey(n) && chosen[n] != series.Last())
                        .OrderByDescending(n => NodeDrop(n, flow, gas))
                        .FirstOrDefault();
                    if (worst == null)
                    {
                        r.Warnings.Add($"{over.a.Label}: path drop {over.drop:F2} mbar exceeds {gas.MaxDropMbar:F2} mbar " +
                                       "even at the largest bore in the series.");
                        break;
                    }
                    var next = series[series.IndexOf(chosen[worst]) + 1];
                    chosen[worst] = next;
                    worst.DiameterMm = next.BoreMm;
                    ReinheritFittings(root);
                }
            }
            else
            {
                FlowTreeUtil.ResolveInheritedBores(root);
                var noBore = all.Where(n => n.Kind == FlowNodeKind.Pipe && n.DiameterMm <= 0).ToList();
                if (noBore.Count > 0) { r.Warnings.Add($"{noBore.Count} pipe(s) have no bore."); return r; }
            }

            foreach (var n in all)
            {
                r.Nodes[n] = new GasNodeResult
                {
                    Node = n,
                    FlowM3h = flow[n],
                    BoreMm = n.DiameterMm,
                    SizeLabel = chosen.TryGetValue(n, out var s) ? s.Label : "",
                    DropMbar = NodeDrop(n, flow, gas),
                    VelocityMs = VelocityMs(flow[n], n.DiameterMm),
                    ModelledBoreMm = modelled[n]
                };
            }
            var worstPath = appliances.Select(a => (a, drop: PathDrop(a, flow, gas))).OrderByDescending(t => t.drop).First();
            r.WorstPathDropMbar = worstPath.drop;
            r.WorstAppliance = worstPath.a;
            if (worstPath.drop > gas.MaxDropMbar + 1e-9 && series == null)
                r.Warnings.Add($"{worstPath.a.Label}: {worstPath.drop:F2} mbar from the source exceeds the {gas.MaxDropMbar:F2} mbar allowance.");
            r.Ok = worstPath.drop <= gas.MaxDropMbar + 1e-9;
            return r;
        }

        /// <summary>Length the drop is computed over: pipe length + fitting equivalent length.</summary>
        private static double HydraulicLength(FlowNode n, double boreMm)
        {
            double eq = n.EquivLengthDiameters > 0 && (boreMm > 0 || n.DiameterMm > 0)
                ? n.EquivLengthDiameters * (boreMm > 0 ? boreMm : n.DiameterMm) / 1000.0
                : n.EquivLengthM;
            return n.LengthM + eq;
        }

        private static double NodeDrop(FlowNode n, Dictionary<FlowNode, double> flow, GasProperties gas)
        {
            if (n.DiameterMm <= 0) return 0;
            return DropMbar(flow[n], HydraulicLength(n, n.DiameterMm), n.DiameterMm, gas.RelativeDensity);
        }

        private static double PathDrop(FlowNode appliance, Dictionary<FlowNode, double> flow, GasProperties gas)
            => appliance.PathFromRoot().Sum(n => NodeDrop(n, flow, gas));

        private static void ReinheritFittings(FlowNode root)
        {
            foreach (var n in root.Descendants().Where(n => n.Kind == FlowNodeKind.Fitting || n.Kind == FlowNodeKind.Accessory))
            {
                double d = 0;
                for (var p = n.Parent; p != null && d <= 0; p = p.Parent)
                    if (p.Kind == FlowNodeKind.Pipe && p.DiameterMm > 0) d = p.DiameterMm;
                if (d > 0) n.DiameterMm = d;
            }
        }
    }
}
