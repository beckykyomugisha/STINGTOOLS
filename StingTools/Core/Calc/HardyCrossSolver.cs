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
