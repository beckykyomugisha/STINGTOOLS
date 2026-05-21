// StingTools — Refrigerant pipe sizing solver.
//
// Sizes VRF/VRV refrigerant lines (suction / discharge / liquid) by:
//   1. Sweeping the standard copper-ACR bore list (3/8", 1/2", 5/8"...)
//      from smallest to largest.
//   2. Computing velocity v = m_dot / (ρ·A) at the candidate size.
//   3. Rejecting sizes that fall below the oil-return minimum velocity
//      (vertical risers more restrictive than horizontal runs).
//   4. Rejecting sizes whose pressure drop over the equivalent length
//      exceeds the vendor's max-line-ΔP budget (ASHRAE 15 + Daikin
//      typical 30 kPa for gas legs, 50 kPa for liquid).
//   5. Picking the smallest size that passes both tests.
//
// Pressure drop uses Darcy-Weisbach with a Blasius f for smooth copper:
//     ΔP = f · (L_eq / D) · ½ ρ v²,   f = 0.316 / Re^0.25 (turbulent).
// Two-phase effects on suction (small amount of flash from line losses)
// are accounted for by a 10 % multiplier — for full Lockhart-Martinelli
// you'd need a state engine.
//
// Output: chosen diameter, velocity, ΔP, lift/length compliance.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Refrigerant
{
    public class RefrigerantSizingInput
    {
        public string RefrigerantId   { get; set; } = "R410A";
        public RefrigerantLeg Leg     { get; set; } = RefrigerantLeg.Suction;
        /// <summary>Total cooling capacity carried by this line, kW.</summary>
        public double CapacityKw      { get; set; }
        /// <summary>Total equivalent length L_eq = L_actual + Σ fitting equiv. m.</summary>
        public double EquivLengthM    { get; set; }
        /// <summary>Net vertical lift (outdoor above indoor, m). Negative = drop.</summary>
        public double LiftM           { get; set; }
        /// <summary>True if the line includes a vertical riser carrying gas
        /// upward — applies the higher oil-return min velocity.</summary>
        public bool HasVerticalRiser  { get; set; }
        /// <summary>ΔP budget per leg, kPa. Default 30 kPa gas / 50 kPa liquid.</summary>
        public double MaxPressureDropKpa { get; set; } = 30;
    }

    public class RefrigerantSizingResult
    {
        public bool   Ok                  { get; set; }
        public double SelectedBoreMm      { get; set; }
        public double VelocityMs          { get; set; }
        public double MassFlowKgS         { get; set; }
        public double ReynoldsNumber      { get; set; }
        public double FrictionFactor      { get; set; }
        public double PressureDropKpa     { get; set; }
        public double LiftPenaltyKpa      { get; set; }
        public string Refrigerant         { get; set; }
        public string Leg                 { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<(double DiaMm, double VelMs, double DpKpa, string Reason)> Trace { get; }
            = new List<(double, double, double, string)>();
    }

    public static class RefrigerantPipeSolver
    {
        /// <summary>
        /// Copper ACR (Air Conditioning + Refrigeration) outside-diameter
        /// list, mm. Internal diameters approximated as OD − 1.5 mm wall.
        /// EU SI sizes shown; US imperial maps onto the same nominal set.
        /// </summary>
        public static readonly double[] CopperAcrOdMm =
            new double[] { 6.35, 9.52, 12.7, 15.88, 19.05, 22.22, 25.4, 28.58, 31.75, 38.1, 41.27, 44.45, 53.98 };

        public const double CopperWallMm = 1.0;        // typical L-grade ACR copper
        public const double GravityMs2   = 9.81;

        public static RefrigerantSizingResult Size(RefrigerantSizingInput input)
        {
            var r = new RefrigerantSizingResult
            {
                Refrigerant = input.RefrigerantId,
                Leg = input.Leg.ToString()
            };
            if (input.CapacityKw <= 0)
            {
                r.Warnings.Add("Capacity is zero — nothing to size.");
                return r;
            }

            var fluid = RefrigerantProperties.Get(input.RefrigerantId);
            var (rho, mu) = RefrigerantProperties.Pair(fluid, input.Leg);

            // Mass flow from capacity: m_dot = Q / hfg
            // (For the LIQUID leg the same m_dot flows, just at a different ρ.)
            double mdot = input.CapacityKw / Math.Max(fluid.HfgKJperKg, 50);   // kg/s
            r.MassFlowKgS = mdot;

            double minVel = input.HasVerticalRiser
                ? fluid.MinVerticalVelMs
                : fluid.MinHorizontalVelMs;
            // Liquid lines have no oil-return constraint; cap by max velocity.
            if (input.Leg == RefrigerantLeg.Liquid) minVel = 0.5;
            double maxVel = fluid.MaxVelocityMs;

            // Lift penalty applies only to LIQUID columns (static head).
            // For gas it's negligible — included as 0.
            double liftKpa = input.Leg == RefrigerantLeg.Liquid && input.LiftM > 0
                ? rho * GravityMs2 * input.LiftM / 1000.0
                : 0;
            r.LiftPenaltyKpa = liftKpa;
            double dpBudgetKpa = Math.Max(input.MaxPressureDropKpa - liftKpa, 1.0);

            double maxDpBudgetPa = dpBudgetKpa * 1000;

            foreach (double odMm in CopperAcrOdMm)
            {
                double idMm = odMm - 2 * CopperWallMm;
                if (idMm < 2) continue;
                double d = idMm * 1e-3;
                double area = Math.PI * d * d * 0.25;
                double v = mdot / (rho * area);

                if (v > maxVel)
                {
                    r.Trace.Add((odMm, v, 0, $"v={v:F1} > vmax={maxVel:F1}"));
                    continue;
                }
                if (v < minVel)
                {
                    r.Trace.Add((odMm, v, 0, $"v={v:F1} < vmin={minVel:F1} (oil return)"));
                    continue;
                }

                double re = rho * v * d / mu;
                double f = re < 2300
                    ? 64.0 / Math.Max(re, 1.0)
                    : 0.316 / Math.Pow(re, 0.25);   // Blasius smooth-pipe
                double dpPa = f * (input.EquivLengthM / d) * 0.5 * rho * v * v;
                // Suction-side two-phase multiplier (rough).
                if (input.Leg == RefrigerantLeg.Suction) dpPa *= 1.10;
                double dpKpa = dpPa / 1000.0;

                if (dpPa > maxDpBudgetPa)
                {
                    r.Trace.Add((odMm, v, dpKpa, $"ΔP={dpKpa:F1} > {dpBudgetKpa:F1} kPa"));
                    continue;
                }

                // First size that passes — accept.
                r.Ok = true;
                r.SelectedBoreMm = odMm;
                r.VelocityMs = v;
                r.ReynoldsNumber = re;
                r.FrictionFactor = f;
                r.PressureDropKpa = dpKpa;
                r.Trace.Add((odMm, v, dpKpa, "OK"));

                // Length / lift compliance against vendor envelope.
                if (input.EquivLengthM > fluid.MaxEquivLengthM)
                    r.Warnings.Add($"Equivalent length {input.EquivLengthM:F0} m exceeds vendor max {fluid.MaxEquivLengthM:F0} m for {fluid.Id}.");
                if (input.LiftM > fluid.MaxLiftAboveIndoorM)
                    r.Warnings.Add($"Lift {input.LiftM:F0} m exceeds vendor max {fluid.MaxLiftAboveIndoorM:F0} m above indoor unit.");
                if (input.LiftM < -fluid.MaxLiftBelowIndoorM)
                    r.Warnings.Add($"Drop {-input.LiftM:F0} m exceeds vendor max {fluid.MaxLiftBelowIndoorM:F0} m below indoor unit.");
                return r;
            }

            // Exhausted the size list — report failure with the closest candidate.
            r.Warnings.Add("No copper ACR size satisfies both velocity and pressure constraints.");
            return r;
        }
    }
}
