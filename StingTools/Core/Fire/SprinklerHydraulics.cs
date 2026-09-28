// SprinklerHydraulics — tree (branch) hydraulic calculation for a sprinkler
// design area. Revit-free; RevitFlowTreeBuilder supplies the tree.
//
// Method (the hand-calculation method used for tree systems):
//   1. Each design-area head must deliver at least q_min = density × area
//      per head, and run at no less than the minimum head pressure.
//      Head flow q = K·√p, so p_head = max(p_min, (q_min / K)²).
//   2. Work from the heads back towards the source. Through each pipe,
//      fitting or valve add the Hazen-Williams friction loss
//          p = 6.05×10⁵ · Q^1.85 · L / (C^1.85 · d^4.87)   bar
//      (Q L/min, d mm, L m — the BS EN 12845 form; NFPA 13's
//      4.52·Q^1.85/(C^1.85·d^4.87) psi/ft is the same equation in US units)
//      and the static head 0.0981 bar per metre of rise.
//   3. Where branches meet, the branch needing the highest pressure governs;
//      every other branch is treated as an equivalent K (Q/√p) and its flow
//      raised to the governing pressure: Q' = Q·√(p_gov / p).
//   4. The result is the flow and pressure required at the source, which the
//      water supply (town main, tank + pump) must meet.
//
// This is a design check, not a certificate: the hazard data, minimum
// pressures and velocity limits come from STING_SPRINKLER_DESIGN.json and
// are to be confirmed against the edition of BS EN 12845 / NFPA 13 in force.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Mep.Networks;

namespace StingTools.Core.Fire
{
    public sealed class SprinklerDesignCriteria
    {
        public string HazardId          { get; set; } = "";
        /// <summary>Design density, mm/min (= L/min per m²).</summary>
        public double DensityMmMin      { get; set; }
        /// <summary>Area of operation, m².</summary>
        public double DesignAreaM2      { get; set; }
        /// <summary>Minimum pressure at any operating head, bar.</summary>
        public double MinHeadPressureBar{ get; set; }
        /// <summary>Area assigned to each head, m². 0 = design area ÷ number of heads.</summary>
        public double AreaPerHeadM2     { get; set; }
        /// <summary>Default Hazen-Williams C for pipes that carry none.</summary>
        public double DefaultC          { get; set; } = 120;
        /// <summary>Velocity above which a pipe is flagged, m/s.</summary>
        public double MaxVelocityMs     { get; set; } = 10;
        /// <summary>Velocity above which a valve or flow switch is flagged, m/s.</summary>
        public double MaxValveVelocityMs{ get; set; } = 6;
    }

    public sealed class SprinklerNodeResult
    {
        public FlowNode Node          { get; set; }
        public double   FlowLpm       { get; set; }
        /// <summary>Pressure at the node's upstream (inlet) side, bar.</summary>
        public double   InletPressureBar  { get; set; }
        public double   FrictionBar   { get; set; }
        public double   VelocityMs    { get; set; }
        public bool     OverVelocity  { get; set; }
    }

    public sealed class SprinklerHydraulicResult
    {
        public bool   Ok                 { get; set; }
        public double SourceFlowLpm      { get; set; }
        public double SourcePressureBar  { get; set; }
        public int    HeadCount          { get; set; }
        public double MinHeadFlowLpm     { get; set; }
        public double AreaPerHeadM2      { get; set; }
        /// <summary>Head with the highest required pressure after balancing (the hydraulically most remote).</summary>
        public FlowNode MostRemoteHead   { get; set; }
        public Dictionary<FlowNode, SprinklerNodeResult> Nodes { get; } = new Dictionary<FlowNode, SprinklerNodeResult>();
        public List<string> Warnings     { get; } = new List<string>();

        public IEnumerable<SprinklerNodeResult> Heads => Nodes.Values.Where(n => n.Node.IsTerminal);
    }

    public static class SprinklerHydraulics
    {
        /// <summary>Static head of water, bar per metre.</summary>
        public const double StaticBarPerM = 0.0981;

        /// <summary>Hazen-Williams friction loss, bar, for Q L/min through L m of bore d mm.</summary>
        public static double FrictionLossBar(double qLpm, double lengthM, double dMm, double c)
        {
            if (qLpm <= 0 || lengthM <= 0) return 0;
            if (dMm <= 0 || c <= 0) throw new ArgumentOutOfRangeException(nameof(dMm), "Bore and C must be positive.");
            return 6.05e5 * Math.Pow(qLpm, 1.85) * lengthM / (Math.Pow(c, 1.85) * Math.Pow(dMm, 4.87));
        }

        public static double VelocityMs(double qLpm, double dMm)
        {
            if (dMm <= 0) return 0;
            double area = Math.PI * Math.Pow(dMm / 1000.0, 2) / 4.0;
            return qLpm / 60000.0 / area;
        }

        public static SprinklerHydraulicResult Solve(FlowNode root, SprinklerDesignCriteria criteria)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            var result = new SprinklerHydraulicResult();

            FlowTreeUtil.ResolveInheritedBores(root);
            var heads = root.Descendants().Where(n => n.IsTerminal).ToList();
            result.HeadCount = heads.Count;
            if (heads.Count == 0) { result.Warnings.Add("No sprinkler heads in the tree."); return result; }
            if (criteria.DensityMmMin <= 0) { result.Warnings.Add("Design density is not set."); return result; }

            double areaPerHead = criteria.AreaPerHeadM2 > 0
                ? criteria.AreaPerHeadM2
                : (criteria.DesignAreaM2 > 0 ? criteria.DesignAreaM2 / heads.Count : 0);
            if (areaPerHead <= 0) { result.Warnings.Add("Neither area per head nor design area is set."); return result; }
            result.AreaPerHeadM2 = areaPerHead;
            result.MinHeadFlowLpm = criteria.DensityMmMin * areaPerHead;
            if (criteria.DesignAreaM2 > 0 && heads.Count * areaPerHead + 1e-6 < criteria.DesignAreaM2)
                result.Warnings.Add($"{heads.Count} heads × {areaPerHead:F1} m² covers {heads.Count * areaPerHead:F0} m², " +
                                    $"less than the {criteria.DesignAreaM2:F0} m² area of operation — select every head in the design area.");

            var missingK = heads.Where(h => h.KFactor <= 0).ToList();
            if (missingK.Count > 0)
            {
                result.Warnings.Add($"{missingK.Count} head(s) have no K-factor: " +
                                    string.Join(", ", missingK.Take(5).Select(h => h.Label)));
                return result;
            }
            var noBore = root.Descendants().Where(n => n.Kind == FlowNodeKind.Pipe && n.DiameterMm <= 0).ToList();
            if (noBore.Count > 0)
            {
                result.Warnings.Add($"{noBore.Count} pipe(s) have no bore — cannot calculate friction.");
                return result;
            }

            var solver = new Solver(criteria, result.MinHeadFlowLpm);
            var (q, p) = solver.Demand(root);
            result.SourceFlowLpm = q;
            result.SourcePressureBar = p;
            solver.Distribute(root, p, result);

            // Most remote = the head left with the least margin over what it needs.
            result.MostRemoteHead = result.Heads
                .OrderBy(h => h.InletPressureBar - solver.HeadPressure(h.Node))
                .Select(h => h.Node).FirstOrDefault();

            foreach (var n in result.Nodes.Values.Where(v => v.OverVelocity))
                result.Warnings.Add($"{n.Node.Label}: {n.VelocityMs:F1} m/s exceeds " +
                                    $"{(n.Node.Kind == FlowNodeKind.Accessory ? criteria.MaxValveVelocityMs : criteria.MaxVelocityMs):F0} m/s.");
            result.Ok = true;
            return result;
        }

        private sealed class Solver
        {
            private readonly SprinklerDesignCriteria _c;
            private readonly double _qMin;
            private readonly Dictionary<FlowNode, (double q, double p)> _memo = new Dictionary<FlowNode, (double, double)>();

            public Solver(SprinklerDesignCriteria c, double qMinLpm) { _c = c; _qMin = qMinLpm; }

            public double HeadPressure(FlowNode head)
                => Math.Max(_c.MinHeadPressureBar, Math.Pow(_qMin / head.KFactor, 2));

            private double Loss(FlowNode node, double q)
            {
                double L = node.TotalLengthM;
                if (L <= 0 || node.DiameterMm <= 0) return 0;
                double cf = node.HazenWilliamsC > 0 ? node.HazenWilliamsC : (_c.DefaultC > 0 ? _c.DefaultC : 120);
                return FrictionLossBar(q, L, node.DiameterMm, cf);
            }

            /// <summary>Pressure each child needs at this node's outlet, with the child's design flow.</summary>
            private List<(FlowNode ch, double q, double p)> Branches(FlowNode node)
            {
                var list = new List<(FlowNode, double, double)>();
                foreach (var ch in node.Children)
                {
                    var (qc, pc) = Demand(ch);
                    if (qc <= 0) continue;
                    list.Add((ch, qc, pc + StaticBarPerM * (ch.ElevationM - node.ElevationM)));
                }
                return list;
            }

            /// <summary>(Flow, inlet pressure) the node needs to meet the design at every head below it.</summary>
            public (double q, double p) Demand(FlowNode node)
            {
                if (_memo.TryGetValue(node, out var hit)) return hit;
                (double q, double p) res;
                if (node.IsTerminal)
                {
                    double ph = HeadPressure(node);
                    res = (node.KFactor * Math.Sqrt(ph), ph);
                }
                else
                {
                    var br = Branches(node);
                    if (br.Count == 0) res = (0, 0);
                    else
                    {
                        double pGov = br.Max(b => b.p);
                        // Non-governing branches act as an equivalent K = Q/√p.
                        double q = br.Sum(b => b.p > 1e-12 ? b.q * Math.Sqrt(pGov / b.p) : b.q);
                        res = (q, pGov + Loss(node, q));
                    }
                }
                _memo[node] = res;
                return res;
            }

            /// <summary>
            /// Walk down from the source at the solved pressure. A subtree fed
            /// above its own demand pressure behaves as its equivalent K, so its
            /// flow scales by √(p_available / p_demand).
            /// </summary>
            public void Distribute(FlowNode node, double inletP, SprinklerHydraulicResult r)
            {
                var (qDesign, pDesign) = Demand(node);
                double k = pDesign > 1e-12 ? Math.Sqrt(Math.Max(inletP, 0) / pDesign) : 1.0;
                double q = qDesign * k;
                if (node.IsTerminal)
                {
                    q = node.KFactor * Math.Sqrt(Math.Max(inletP, 0));
                    r.Nodes[node] = new SprinklerNodeResult { Node = node, FlowLpm = q, InletPressureBar = inletP };
                    return;
                }
                double hf = Loss(node, q);
                double v = VelocityMs(q, node.DiameterMm);
                bool over = node.Kind == FlowNodeKind.Accessory ? v > _c.MaxValveVelocityMs
                          : node.Kind == FlowNodeKind.Pipe && v > _c.MaxVelocityMs;
                r.Nodes[node] = new SprinklerNodeResult
                {
                    Node = node, FlowLpm = q, InletPressureBar = inletP, FrictionBar = hf,
                    VelocityMs = v, OverVelocity = over
                };
                double pOut = inletP - hf;
                foreach (var ch in node.Children)
                    Distribute(ch, pOut - StaticBarPerM * (ch.ElevationM - node.ElevationM), r);
            }
        }
    }
}
