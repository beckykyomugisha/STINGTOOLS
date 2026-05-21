// StingTools v4 MVP — ASHRAE/SMACNA duct friction solver.
//
// Implements the Darcy-Weisbach equation with the Swamee-Jain explicit
// approximation to the Colebrook correlation — the standard method for
// sizing and validating low-to-medium-velocity HVAC ductwork.
//
//     ΔP_straight = f · (L / Dh) · ( ρ · v² / 2 )
//     f (Swamee-Jain) ≈ 0.25 / [ log10( ε / (3.7 · Dh) + 5.74 / Re^0.9 ) ]²
//     Re = (ρ · v · Dh) / μ
//     ΔP_fitting  = Σ C · ( ρ · v² / 2 )
//
// Constants (air @ 20 °C, sea level):
//     ρ = 1.204  kg/m³
//     μ = 1.813e-5 Pa·s
//     Default roughness for galvanised sheet steel ε = 9.0e-5 m
//
// SMACNA fitting loss coefficients (C factors) from the SMACNA HVAC
// Systems Duct Design, 4th ed., Appendix A. A curated subset is
// included here; more can be loaded at runtime via a CSV sidecar.
//
// Output: FrictionResult with component breakdown and a round-trip
// summary ("9.8 Pa across 12.0 m straight + 3 elbows").

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Calc
{
    public enum DuctShape { Round, Rectangular, Flat }

    public class DuctFittingLoss
    {
        public string Name  { get; set; } = "";
        public double C     { get; set; }
        public int    Count { get; set; } = 1;
        public double LossPa { get; set; }
    }

    public class DuctFrictionResult
    {
        public double VelocityMs       { get; set; }
        public double ReynoldsNumber   { get; set; }
        public double FrictionFactor   { get; set; }
        public double HydraulicDiameterM { get; set; }
        public double StraightDropPa   { get; set; }
        public double FittingDropPa    { get; set; }
        public double TotalDropPa      { get; set; }
        public List<DuctFittingLoss> Fittings { get; } = new List<DuctFittingLoss>();
        public string Regime           { get; set; } = "";
        public bool   IsLaminar        { get; set; }
    }

    public static class DuctFrictionSolver
    {
        public const double AirDensityKgM3     = 1.204;
        public const double AirViscosityPaS    = 1.813e-5;
        public const double GalvRoughnessM     = 9.0e-5;
        public const double AluminumRoughnessM = 3.0e-5;
        // ASHRAE Handbook 2021 Ch.21 fully-extended flex: ε ≈ 3 mm. Installed
        // flex with slight sag runs ~1.5 mm; projects can pass the lower
        // value to Solve() for a less-conservative friction estimate.
        public const double FlexRoughnessM     = 3.0e-3;

        /// <summary>
        /// Compute the pressure drop across a straight duct section
        /// plus an optional set of fittings.
        /// </summary>
        public static DuctFrictionResult Solve(
            DuctShape shape,
            double sideAMm,
            double sideBMm,
            double lengthM,
            double flowM3S,
            IEnumerable<DuctFittingLoss> fittings,
            double roughnessM = GalvRoughnessM)
        {
            var r = new DuctFrictionResult();
            if (lengthM <= 0 || flowM3S <= 0 || sideAMm <= 0) return r;

            double dhM;
            double areaM2;
            if (shape == DuctShape.Round)
            {
                dhM    = sideAMm / 1000.0;
                areaM2 = Math.PI * dhM * dhM * 0.25;
            }
            else
            {
                // Hydraulic diameter for rectangular: Dh = 2ab/(a+b)
                double a = sideAMm / 1000.0;
                double b = (sideBMm > 0 ? sideBMm : sideAMm) / 1000.0;
                dhM    = 2.0 * a * b / (a + b);
                areaM2 = a * b;
            }
            r.HydraulicDiameterM = dhM;

            double v = flowM3S / areaM2;
            r.VelocityMs = v;

            double re = (AirDensityKgM3 * v * dhM) / AirViscosityPaS;
            r.ReynoldsNumber = re;

            double f;
            if (re < 2300)
            {
                f = 64.0 / Math.Max(re, 1.0);
                r.IsLaminar = true;
                r.Regime = "Laminar (Re<2300)";
            }
            else
            {
                // Swamee-Jain: explicit Colebrook approximation, valid
                // for 5e3 < Re < 1e8 and ε/Dh < 1e-2.
                double relRough = roughnessM / dhM;
                double term     = relRough / 3.7 + 5.74 / Math.Pow(re, 0.9);
                f = 0.25 / Math.Pow(Math.Log10(term), 2.0);
                r.IsLaminar = false;
                r.Regime = "Turbulent (Swamee-Jain)";
            }
            r.FrictionFactor = f;

            double dynPa = 0.5 * AirDensityKgM3 * v * v;
            r.StraightDropPa = f * (lengthM / dhM) * dynPa;

            if (fittings != null)
            {
                foreach (var fit in fittings)
                {
                    if (fit == null) continue;
                    fit.LossPa = fit.Count * fit.C * dynPa;
                    r.Fittings.Add(fit);
                    r.FittingDropPa += fit.LossPa;
                }
            }

            r.TotalDropPa = r.StraightDropPa + r.FittingDropPa;
            return r;
        }

        /// <summary>
        /// Curated SMACNA fitting-loss coefficients. Reference: SMACNA
        /// HVAC Systems Duct Design, 4th ed., Appendix A.
        ///
        /// This is the corporate-baseline fallback. Projects override
        /// individual entries via STING_MEP_SIZING_RULES.json
        /// (duct.fittingLossCoefficients) — see <see cref="LookupC"/>.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, double> SmacnaCoefficients
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            // Elbows
            { "ELBOW_90_SMOOTH",       0.11 },
            { "ELBOW_90_MITRED_VANED", 0.27 },
            { "ELBOW_90_MITRED_PLAIN", 1.20 },
            { "ELBOW_45_SMOOTH",       0.08 },
            { "ELBOW_60_SMOOTH",       0.10 },
            // Tees / branches (straight-through vs branch)
            { "TEE_STRAIGHT_45",       0.15 },
            { "TEE_STRAIGHT_90",       0.50 },
            { "TEE_BRANCH_45",         0.60 },
            { "TEE_BRANCH_90",         1.00 },
            // Transitions
            { "CONTRACTION_45",        0.20 },
            { "EXPANSION_45",          0.25 },
            { "EXPANSION_ABRUPT",      0.50 },
            // Dampers / terminals
            { "DAMPER_OPEN",           0.04 },
            { "DAMPER_HALF",           6.00 },
            { "DIFFUSER",              1.40 },
            { "EGGCRATE",              0.80 },
            // Flex connection penalty
            { "FLEX_PER_METRE",        1.00 }, // multiplied by flex length m
        };

        /// <summary>
        /// Resolve a fitting-loss coefficient C by name, preferring a
        /// project-override entry (from MepSizingRegistry) over the
        /// baked-in SMACNA baseline. Returns 0 for unknown fitting types
        /// — callers should treat 0 as "no loss assigned" and skip.
        /// </summary>
        public static double LookupC(string fittingName,
            IReadOnlyDictionary<string, double> projectOverrides = null)
        {
            if (string.IsNullOrEmpty(fittingName)) return 0;
            if (projectOverrides != null &&
                projectOverrides.TryGetValue(fittingName, out double pv) && pv > 0)
                return pv;
            return SmacnaCoefficients.TryGetValue(fittingName, out double cv) ? cv : 0;
        }

        /// <summary>
        /// Apply the CIBSE Guide B3 velocity limits to a friction result
        /// and return a list of violations. Limits: low-velocity
        /// commercial ≤ 7.6 m/s mains, ≤ 5.0 m/s branches, ≤ 3.0 m/s
        /// terminals.
        /// </summary>
        public static List<string> CibseB3VelocityCheck(DuctFrictionResult r, string runTypeHint = "branch")
        {
            var issues = new List<string>();
            if (r == null) return issues;
            double limit = runTypeHint?.ToLowerInvariant() switch
            {
                "main"     => 7.6,
                "branch"   => 5.0,
                "terminal" => 3.0,
                _          => 5.0,
            };
            if (r.VelocityMs > limit)
                issues.Add(
                    $"Velocity {r.VelocityMs:F1} m/s exceeds CIBSE B3 " +
                    $"{runTypeHint} limit {limit:F1} m/s (Dh={r.HydraulicDiameterM*1000.0:F0} mm)");
            if (r.VelocityMs > 15.0)
                issues.Add($"Velocity {r.VelocityMs:F1} m/s > 15 m/s — regenerative noise likely.");
            return issues;
        }
    }
}
