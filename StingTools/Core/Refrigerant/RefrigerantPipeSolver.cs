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
// Suction ΔP is multiplied by SuctionDpMultiplier (default 1.10) as an
// allowance for entrained oil and liquid; it is an input, reported in the
// trace, not a hidden constant. A Lockhart-Martinelli two-phase calculation
// needs vapour quality along the line, which this solver does not model.
//
// Liquid-line static head follows the direction the liquid actually flows.
// Cooling: ODU → IDU, so an ODU above the IDU gains head. Heating (heat
// pump): IDU → ODU, the reverse. A reversible system is sized for the worse
// of the two, i.e. |lift| is always a debit — the default, because VRF/VRV
// systems are almost always heat pumps.
//
// Output: chosen diameter, velocity, ΔP, lift/length compliance.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Refrigerant
{
    /// <summary>Which way the liquid line runs in service. Decides the sign of its static head.</summary>
    public enum RefrigerantOperatingMode
    {
        /// <summary>Heat pump: liquid flows both ways; size for the uphill case.</summary>
        Reversible = 0,
        /// <summary>Liquid flows outdoor unit → indoor unit.</summary>
        CoolingOnly = 1,
        /// <summary>Liquid flows indoor unit → outdoor unit.</summary>
        HeatingOnly = 2
    }

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
        /// <summary>
        /// Subcooling reserve at the condenser outlet (K). Used by the
        /// LIQUID-leg flash-gas check: line ΔP drops saturation pressure,
        /// which is allowable only while sub-cooling exceeds the equivalent
        /// temperature drop. Default 5 K — typical TXV-fed system. Set 8 K
        /// for EEV systems with deliberate sub-cooling control.
        /// </summary>
        public double SubcoolingReserveK { get; set; } = 5.0;

        /// <summary>Liquid-flow direction in service; decides whether lift debits or credits the liquid ΔP budget.</summary>
        public RefrigerantOperatingMode Mode { get; set; } = RefrigerantOperatingMode.Reversible;

        /// <summary>
        /// Allowance on suction-line friction for entrained oil and liquid
        /// (1.10 = +10 %). Values below 1 are treated as 1.
        /// </summary>
        public double SuctionDpMultiplier { get; set; } = 1.10;

        /// <summary>
        /// Optional vendor series id (e.g. "Daikin-VRV-5", "Mitsubishi-CityMulti-R2").
        /// When set, the solver consults <see cref="RefrigerantVendorRegistry"/>
        /// for vendor-published max-length tables. Stricter checks layered on
        /// top of the generic Darcy ΔP-budget pass; failures surface as warnings.
        /// </summary>
        public string VendorSeriesId { get; set; }
        /// <summary>Actual (physical) one-way length of the run, m. Used for
        /// vendor-table compliance when an Equivalent-Length value is supplied
        /// via <see cref="EquivLengthM"/>.</summary>
        public double ActualOnewayLengthM { get; set; }
        /// <summary>Total summed pipe length across all branches (m).
        /// Vendor tables cap this independently of the one-way length.</summary>
        public double TotalSystemLengthM { get; set; }
        /// <summary>Optional document for resolving the vendor registry. May
        /// be null — caller can also pre-pass a <see cref="VendorLimits"/>.</summary>
        public Autodesk.Revit.DB.Document Document { get; set; }
        /// <summary>Direct vendor limits override — bypasses registry lookup
        /// when supplied.</summary>
        public VendorSeriesLimits VendorLimits { get; set; }
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
        /// <summary>Equivalent saturation-temperature drop from line ΔP (K).
        /// LIQUID legs only; compared against <see cref="RefrigerantSizingInput.SubcoolingReserveK"/>.</summary>
        public double SatTempDropK        { get; set; }
        /// <summary>Vendor series id consulted for length-table compliance, when supplied.</summary>
        public string VendorSeriesId      { get; set; }
        /// <summary>Vendor series human-readable label.</summary>
        public string VendorSeriesLabel   { get; set; }
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

        /// <summary>
        /// Static-head change on the liquid line, kPa: positive = a debit
        /// (liquid rises along its flow), negative = a credit (it falls).
        /// <paramref name="liftM"/> is outdoor-unit height above the indoor unit.
        /// </summary>
        public static double LiquidStaticHeadKpa(double rhoKgM3, double liftM, RefrigerantOperatingMode mode)
        {
            double perM = rhoKgM3 * GravityMs2 / 1000.0;
            switch (mode)
            {
                case RefrigerantOperatingMode.CoolingOnly: return -perM * liftM;   // ODU → IDU
                case RefrigerantOperatingMode.HeatingOnly: return  perM * liftM;   // IDU → ODU
                default:                                    return  perM * Math.Abs(liftM);
            }
        }

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

            // Liquid-column static head — applies to LIQUID legs only.
            // For gas (suction / discharge) the density is too low for the
            // static term to matter (<0.1 kPa per metre at typical conditions).
            //
            // Sign: positive LiftM means outdoor unit is ABOVE indoor.
            //   * For a liquid line flowing UP that path, gravity opposes
            //     flow → static head DEBITS the available ΔP budget.
            //   * For a liquid line flowing DOWN that path (negative lift),
            //     gravity ASSISTS → static head CREDITS the budget. The
            //     recovered head can be substantial; ignoring it makes
            //     sizing of evap-above-condenser systems unnecessarily
            //     conservative.
            //
            // We sign-track the lift and let the budget go either way.
            double liftKpa = input.Leg == RefrigerantLeg.Liquid
                ? LiquidStaticHeadKpa(rho, input.LiftM, input.Mode)
                : 0;
            r.LiftPenaltyKpa = liftKpa;
            if (input.Leg == RefrigerantLeg.Liquid && Math.Abs(input.LiftM) > 0)
                r.Trace.Add((0, 0, liftKpa,
                    $"static head {(liftKpa >= 0 ? "debit" : "credit")} {Math.Abs(liftKpa):F1} kPa " +
                    $"(lift {input.LiftM:+0.0;-0.0} m, {input.Mode})"));
            double suctionMult = Math.Max(1.0, input.SuctionDpMultiplier);
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
                // Suction-side allowance for entrained oil / liquid (input).
                if (input.Leg == RefrigerantLeg.Suction) dpPa *= suctionMult;
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

                // Liquid-leg flash-gas check. Line ΔP drops the saturation
                // pressure; the corresponding T_sat drop is dpKpa × (dT/dP)_sat.
                // If that exceeds the subcooling reserve, vapour forms before
                // the TXV → erratic capacity. Issue as a warning so the user
                // either oversizes the liquid line or specifies more subcooling.
                if (input.Leg == RefrigerantLeg.Liquid && fluid.DtDpKperKpa > 0)
                {
                    r.SatTempDropK = dpKpa * fluid.DtDpKperKpa;
                    if (r.SatTempDropK > input.SubcoolingReserveK)
                    {
                        r.Warnings.Add(
                            $"Flash-gas risk: line ΔP {dpKpa:F1} kPa drops T_sat by " +
                            $"{r.SatTempDropK:F1} K, exceeding the {input.SubcoolingReserveK:F1} K " +
                            $"subcooling reserve. Oversize the liquid line or specify more " +
                            $"sub-cooling at the condenser outlet.");
                    }
                }

                // Length / lift compliance against the generic fluid envelope.
                if (input.EquivLengthM > fluid.MaxEquivLengthM)
                    r.Warnings.Add($"Equivalent length {input.EquivLengthM:F0} m exceeds generic max {fluid.MaxEquivLengthM:F0} m for {fluid.Id}.");
                if (input.LiftM > fluid.MaxLiftAboveIndoorM)
                    r.Warnings.Add($"Lift {input.LiftM:F0} m exceeds generic max {fluid.MaxLiftAboveIndoorM:F0} m above indoor unit.");
                if (input.LiftM < -fluid.MaxLiftBelowIndoorM)
                    r.Warnings.Add($"Drop {-input.LiftM:F0} m exceeds generic max {fluid.MaxLiftBelowIndoorM:F0} m below indoor unit.");

                // Vendor-specific length-table compliance (Daikin REYQ-T,
                // Mitsubishi City Multi, Toshiba SHRMe, etc). Stricter than
                // the generic envelope when present.
                var vendor = input.VendorLimits
                    ?? (input.VendorSeriesId != null && input.Document != null
                        ? RefrigerantVendorRegistry.Get(input.Document).Get(input.VendorSeriesId)
                        : null);
                if (vendor != null)
                {
                    r.VendorSeriesId    = vendor.Id;
                    r.VendorSeriesLabel = vendor.Label;
                    if (vendor.EquivalentOnewayMaxM > 0 && input.EquivLengthM > vendor.EquivalentOnewayMaxM)
                        r.Warnings.Add(
                            $"Vendor {vendor.Label}: equivalent one-way length " +
                            $"{input.EquivLengthM:F0} m exceeds max {vendor.EquivalentOnewayMaxM:F0} m.");
                    if (vendor.ActualOnewayMaxM > 0 && input.ActualOnewayLengthM > vendor.ActualOnewayMaxM)
                        r.Warnings.Add(
                            $"Vendor {vendor.Label}: actual one-way length " +
                            $"{input.ActualOnewayLengthM:F0} m exceeds max {vendor.ActualOnewayMaxM:F0} m.");
                    if (vendor.TotalPipeLengthM > 0 && input.TotalSystemLengthM > vendor.TotalPipeLengthM)
                        r.Warnings.Add(
                            $"Vendor {vendor.Label}: total system pipe length " +
                            $"{input.TotalSystemLengthM:F0} m exceeds max {vendor.TotalPipeLengthM:F0} m.");
                    if (vendor.VerticalHighLowOduIduM > 0 &&
                        Math.Abs(input.LiftM) > vendor.VerticalHighLowOduIduM)
                        r.Warnings.Add(
                            $"Vendor {vendor.Label}: |lift| {Math.Abs(input.LiftM):F0} m " +
                            $"exceeds ODU↔IDU vertical max {vendor.VerticalHighLowOduIduM:F0} m.");
                }
                return r;
            }

            // Exhausted the size list — report failure with the closest candidate.
            r.Warnings.Add("No copper ACR size satisfies both velocity and pressure constraints.");
            return r;
        }
    }
}
