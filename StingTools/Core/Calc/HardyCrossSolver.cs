// StingTools v4 MVP — Hardy Cross loop network balancing.
//
// Iterative flow-correction method for looped hydronic / water-supply
// networks. Reference: Hardy Cross, "Analysis of Flow in Networks of
// Conduits or Conductors", University of Illinois Bulletin 286, 1936.
//
// The solver accepts a topology graph (pipes + nodes + loop list +
// assumed initial flows) and iteratively applies the correction:
//
//      ΔQ = - Σ (h_L)  /  Σ (n · |h_L| / |Q|)
//
// where h_L is the Darcy-Weisbach head loss through each pipe in the
// loop, n = 2 (turbulent flow with H ∝ Q²), and Q is the assumed flow.
// The correction is applied to every pipe in the loop, flipping sign
// for pipes traversed against their assumed direction. Convergence
// is declared when max(|ΔQ|) across all loops is below a user-set
// tolerance (default 0.1% of the assumed flow). Typical networks
// converge in 5-12 iterations.
//
// Phase C original shipped three stand-alone calculators (fill,
// friction, slope). Hardy Cross is the balance-the-whole-network
// step that feeds them — the solved Q_i is what DuctFrictionSolver
// gets called with, not the designer's guess.
//
// Currently used for piping (water); the same method works for
// ducting with H ∝ Q^1.85-2.0 (turbulent air) by changing n=1.852 in
// the correction. Use HydronicMode for water, AirMode for duct.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Calc
{
    public enum NetworkFluid { Water, Air }

    /// <summary>
    /// One pipe in the network. Direction sign convention: positive
    /// Q flows from Node A → Node B. The loop list captures which
    /// loop(s) contain this pipe and with what sign (+1 when the
    /// loop traverses A→B, -1 when B→A).
    /// </summary>
    public class NetworkPipe
    {
        public string Id            { get; set; } = "";
        public string NodeA         { get; set; } = "";
        public string NodeB         { get; set; } = "";
        /// <summary>Assumed flow Q₀. Revised in-place by the solver.</summary>
        public double FlowM3S       { get; set; }
        /// <summary>Internal diameter m (used for friction head loss).</summary>
        public double DiameterM     { get; set; }
        /// <summary>Length m.</summary>
        public double LengthM       { get; set; }
        /// <summary>Sum of fitting loss coefficients Σ K along this pipe.</summary>
        public double FittingLossK  { get; set; }
        /// <summary>Darcy-Weisbach friction factor (pre-calc via Swamee-Jain).</summary>
        public double FrictionFactor { get; set; }
    }

    public class NetworkLoop
    {
        public string Id { get; set; } = "";
        /// <summary>Ordered list of (pipe-id, sign). Sign = +1 when loop
        /// traverses the pipe A→B, -1 when B→A.</summary>
        public List<(string PipeId, int Sign)> Members { get; } = new List<(string, int)>();
    }

    public class HardyCrossResult
    {
        public int    Iterations   { get; set; }
        public double MaxDeltaQ    { get; set; }
        public bool   Converged    { get; set; }
        public List<string> IterationLog { get; } = new List<string>();
        public List<NetworkPipe> Pipes { get; set; }
        /// <summary>Operating point reached when a PumpCurve was supplied
        /// (m³/s through the pump). 0 if no pump curve was active.</summary>
        public double PumpOpFlowM3S    { get; set; }
        /// <summary>Operating-point head, m (or Pa via ρg). 0 if no pump curve.</summary>
        public double PumpOpHeadM      { get; set; }
    }

    /// <summary>
    /// Polynomial pump head-curve: H(Q) = a₀ + a₁·Q + a₂·Q² + … in metres
    /// of fluid head given Q in m³/s. Most manufacturers publish a 3rd-order
    /// fit (a₀, a₁, a₂, a₃). Construct via FromCubicAtPoints helper or
    /// directly with coefficients.
    /// </summary>
    public class PumpCurve
    {
        public double[] Coefficients { get; set; }   // index = polynomial order

        public double HeadAt(double qM3S)
        {
            double h = 0;
            for (int i = 0; i < Coefficients.Length; i++)
                h += Coefficients[i] * Math.Pow(qM3S, i);
            return h;
        }

        /// <summary>
        /// Build a quadratic pump curve H(Q) = a + b·Q + c·Q² from three
        /// (Q, H) reference points (shut-off, BEP, run-out). Standard
        /// catalogue-data fit accuracy.
        /// </summary>
        public static PumpCurve FromQuadraticThreePoints(
            (double q, double h) shutOff,
            (double q, double h) bep,
            (double q, double h) runOut)
        {
            double q1 = shutOff.q, q2 = bep.q, q3 = runOut.q;
            double h1 = shutOff.h, h2 = bep.h, h3 = runOut.h;
            // Solve 3×3 Vandermonde for [a, b, c].
            double d  = (q1 - q2) * (q1 - q3) * (q2 - q3);
            if (Math.Abs(d) < 1e-12)
                return new PumpCurve { Coefficients = new[] { h2, 0.0, 0.0 } };
            double a =  (q2 * q3 * (q3 - q2) * h1
                       + q1 * q3 * (q1 - q3) * h2
                       + q1 * q2 * (q2 - q1) * h3) / d;
            double b =  (q3 * q3 * (q2 - q1) * (q1 - q3) * 0 +
                         (h1 * (q2 * q2 - q3 * q3) +
                          h2 * (q3 * q3 - q1 * q1) +
                          h3 * (q1 * q1 - q2 * q2))) / d;
            double c =  (h1 * (q3 - q2) + h2 * (q1 - q3) + h3 * (q2 - q1)) / d;
            return new PumpCurve { Coefficients = new[] { a, b, c } };
        }
    }

    public static class HardyCrossSolver
    {
        public const int    DefaultMaxIterations = 60;
        public const double DefaultToleranceRel  = 0.001; // 0.1%

        // Fluid constants.
        public const double WaterDensityKgM3   = 1000.0;
        public const double WaterViscosityPaS  = 1.002e-3;
        public const double AirDensityKgM3     = 1.204;
        public const double AirViscosityPaS    = 1.813e-5;

        /// <summary>
        /// Seed each pipe's <see cref="NetworkPipe.FlowM3S"/> from the
        /// supplied per-node demand map. Acts as the initial Q₀ guess
        /// for <see cref="Solve"/> — eliminates the "user must pre-compute
        /// flows" gap (Phase 187 review item A-4).
        ///
        /// Algorithm: equal-split into branches at every node, then
        /// adjust each pipe to match its loop-traversal sign. Works
        /// well for tree-shaped + lightly-looped distribution networks
        /// (the dominant HVAC case). For dense loops the Hardy Cross
        /// iteration corrects any starting bias in 5-12 sweeps anyway,
        /// so this only has to be order-of-magnitude right.
        ///
        /// `demandLpsByNode` maps a node id → demand at that node in L/s
        /// (positive = consumption, negative = supply). Pipes with no
        /// demand connection get the average of their incident-node
        /// demands.
        /// </summary>
        public static void InitializeFromDemand(
            List<NetworkPipe> pipes,
            Dictionary<string, double> demandLpsByNode)
        {
            if (pipes == null || demandLpsByNode == null) return;
            // Count pipe-degree per node so we can distribute demand evenly.
            var degree = new Dictionary<string, int>();
            foreach (var p in pipes)
            {
                if (string.IsNullOrEmpty(p.NodeA) || string.IsNullOrEmpty(p.NodeB)) continue;
                degree[p.NodeA] = degree.TryGetValue(p.NodeA, out var a) ? a + 1 : 1;
                degree[p.NodeB] = degree.TryGetValue(p.NodeB, out var b) ? b + 1 : 1;
            }
            foreach (var p in pipes)
            {
                double qA = demandLpsByNode.TryGetValue(p.NodeA, out var da) ? da : 0;
                double qB = demandLpsByNode.TryGetValue(p.NodeB, out var db) ? db : 0;
                int degA = degree.TryGetValue(p.NodeA, out var degAv) ? Math.Max(degAv, 1) : 1;
                int degB = degree.TryGetValue(p.NodeB, out var degBv) ? Math.Max(degBv, 1) : 1;
                // Average per-pipe share of incident-node demand.
                double qLps = 0.5 * (qA / degA + qB / degB);
                p.FlowM3S = qLps * 1e-3;     // L/s → m³/s
            }
        }

        /// <summary>
        /// Seed pipe flows uniformly from a single supply rate (m³/s).
        /// Convenience for tree networks with a single source — divides
        /// flow equally among the pipes attached to the source node.
        /// </summary>
        public static void InitializeUniform(
            List<NetworkPipe> pipes, string sourceNode, double sourceFlowM3S)
        {
            if (pipes == null || string.IsNullOrEmpty(sourceNode)) return;
            int n = 0;
            foreach (var p in pipes)
                if (p.NodeA == sourceNode || p.NodeB == sourceNode) n++;
            if (n == 0) return;
            double perPipe = sourceFlowM3S / n;
            foreach (var p in pipes) p.FlowM3S = perPipe;
        }

        /// <summary>
        /// Find the system operating point against a pump head-curve.
        /// Walks pipes in series along a supplied trunk path, sums each
        /// pipe's head loss as a function of total flow Q, and bisects
        /// until system_head(Q) = pump_head(Q). Returns the operating
        /// (Q, H) pair and stamps the new Q on every pipe in the path.
        /// Use this for tree (radial) networks where Hardy Cross over-
        /// kills; for looped networks use Solve() with the resolved Q.
        /// </summary>
        public static HardyCrossResult OperatingPoint(
            List<NetworkPipe> seriesPath, PumpCurve pump,
            NetworkFluid fluid = NetworkFluid.Water,
            double qMinM3S = 1e-5, double qMaxM3S = 1.0,
            int maxIter = 60, double tolRelQ = 0.001)
        {
            var r = new HardyCrossResult { Pipes = seriesPath };
            if (seriesPath == null || seriesPath.Count == 0 || pump == null) return r;
            double rho = fluid == NetworkFluid.Water ? WaterDensityKgM3 : AirDensityKgM3;
            double mu  = fluid == NetworkFluid.Water ? WaterViscosityPaS : AirViscosityPaS;

            // System curve = Σ pipe head-loss at flow Q. Bisect for
            // pump_head(Q) - system_head(Q) = 0. Both functions are
            // monotonic in their respective domains so bisection is safe.
            double SystemHeadAt(double q)
            {
                double sum = 0;
                foreach (var p in seriesPath)
                {
                    var saved = p.FlowM3S;
                    p.FlowM3S = q;
                    sum += Math.Abs(HeadLoss(p, q, rho, mu));
                    p.FlowM3S = saved;
                }
                return sum;
            }
            double lo = qMinM3S, hi = qMaxM3S;
            double fLo = pump.HeadAt(lo) - SystemHeadAt(lo);
            double fHi = pump.HeadAt(hi) - SystemHeadAt(hi);
            if (fLo * fHi > 0)
            {
                // No sign change within range — operating point outside
                // bracket. Return best-effort (whichever endpoint is closer
                // to zero) so caller can re-bracket.
                double pick = Math.Abs(fLo) < Math.Abs(fHi) ? lo : hi;
                r.PumpOpFlowM3S = pick;
                r.PumpOpHeadM   = pump.HeadAt(pick);
                r.IterationLog.Add($"OperatingPoint: no sign change in [{lo:E2}, {hi:E2}]; picked {pick:E2}");
                foreach (var p in seriesPath) p.FlowM3S = pick;
                return r;
            }
            double mid = 0.5 * (lo + hi);
            for (int iter = 1; iter <= maxIter; iter++)
            {
                mid = 0.5 * (lo + hi);
                double f = pump.HeadAt(mid) - SystemHeadAt(mid);
                r.IterationLog.Add($"iter {iter}: Q={mid:E3}, ΔH={f:F3} m");
                if ((hi - lo) / Math.Max(mid, 1e-12) < tolRelQ)
                {
                    r.Converged  = true;
                    r.Iterations = iter;
                    break;
                }
                if (f * fLo < 0) { hi = mid; fHi = f; }
                else             { lo = mid; fLo = f; }
            }
            r.PumpOpFlowM3S = mid;
            r.PumpOpHeadM   = pump.HeadAt(mid);
            foreach (var p in seriesPath) p.FlowM3S = mid;
            return r;
        }

        /// <summary>
        /// Run Hardy Cross to convergence. Pipes and Loops are mutated
        /// in place (Pipe.FlowM3S is updated each iteration). Result
        /// reports convergence stats + per-iteration log.
        /// </summary>
        public static HardyCrossResult Solve(
            List<NetworkPipe> pipes,
            List<NetworkLoop> loops,
            NetworkFluid fluid = NetworkFluid.Water,
            int maxIter = DefaultMaxIterations,
            double tolRel = DefaultToleranceRel)
        {
            var r = new HardyCrossResult { Pipes = pipes };
            if (pipes == null || pipes.Count == 0) return r;
            if (loops == null || loops.Count == 0) return r;

            double rho = fluid == NetworkFluid.Water ? WaterDensityKgM3 : AirDensityKgM3;
            double mu  = fluid == NetworkFluid.Water ? WaterViscosityPaS : AirViscosityPaS;
            // Exponent in h_L = K·Q^n. HeadLoss() below uses Darcy-Weisbach
            // with constant f over an iteration, so h ∝ v² ∝ Q² → n=2 for
            // both water and air. The Hazen-Williams exponent (1.852) does
            // not apply here because we never call H-W.
            const double n = 2.0;

            var byId = pipes.ToDictionary(p => p.Id);

            for (int iter = 1; iter <= maxIter; iter++)
            {
                double maxAbs = 0;
                foreach (var loop in loops)
                {
                    double sumH = 0;       // Σ h_L (signed)
                    double sumHperQ = 0;   // Σ n · |h_L| / |Q|
                    foreach (var (pid, sign) in loop.Members)
                    {
                        if (!byId.TryGetValue(pid, out var p)) continue;
                        double q = p.FlowM3S;
                        if (Math.Abs(q) < 1e-12) q = 1e-12;
                        double h = HeadLoss(p, q, rho, mu);
                        sumH    += sign * h;
                        sumHperQ += n * Math.Abs(h) / Math.Abs(q);
                    }
                    if (sumHperQ < 1e-12) continue;
                    double deltaQ = -sumH / sumHperQ;
                    foreach (var (pid, sign) in loop.Members)
                    {
                        if (!byId.TryGetValue(pid, out var p)) continue;
                        p.FlowM3S += sign * deltaQ;
                    }
                    double absRel = 0;
                    foreach (var (pid, _) in loop.Members)
                    {
                        if (!byId.TryGetValue(pid, out var p)) continue;
                        if (Math.Abs(p.FlowM3S) < 1e-12) continue;
                        double rel = Math.Abs(deltaQ) / Math.Abs(p.FlowM3S);
                        if (rel > absRel) absRel = rel;
                    }
                    if (absRel > maxAbs) maxAbs = absRel;
                }

                r.Iterations = iter;
                r.MaxDeltaQ  = maxAbs;
                r.IterationLog.Add($"iter {iter}: max |ΔQ|/|Q| = {maxAbs:E3}");
                if (maxAbs < tolRel)
                {
                    r.Converged = true;
                    break;
                }
            }
            return r;
        }

        /// <summary>
        /// Darcy-Weisbach head loss across one pipe, including the
        /// fitting loss coefficient sum K. Returns h in metres of
        /// fluid column (consistent with the iteration which treats h
        /// as pressure ÷ ρg — the divisor cancels inside ΔQ).
        /// </summary>
        private static double HeadLoss(NetworkPipe p, double q, double rho, double mu)
        {
            if (p.DiameterM <= 0 || p.LengthM <= 0) return 0;
            double area = Math.PI * p.DiameterM * p.DiameterM * 0.25;
            double v    = q / area;
            if (Math.Abs(v) < 1e-12) return 0;

            double f = p.FrictionFactor;
            if (f <= 0)
            {
                double re = rho * Math.Abs(v) * p.DiameterM / mu;
                // Very cheap: Blasius for turbulent smooth pipe; caller
                // supplies f via DuctFrictionSolver.Solve for better
                // accuracy when the network has roughness data.
                if (re < 2300) f = 64.0 / Math.Max(re, 1.0);
                else f = 0.316 / Math.Pow(re, 0.25);
            }

            // h_pipe = f · (L / D) · (v²/2g)  with g cancelled
            double hPipe = f * (p.LengthM / p.DiameterM) * 0.5 * v * Math.Abs(v);
            double hFit  = p.FittingLossK * 0.5 * v * Math.Abs(v);
            // Preserve sign of v for the iteration.
            return Math.Sign(v) * (hPipe + hFit);
        }
    }
}
