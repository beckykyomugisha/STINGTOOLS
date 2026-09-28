// Psychrometrics — moist-air state properties and cooling-coil process.
//
// Revit-free. Formulation from ASHRAE Handbook — Fundamentals (SI), Ch. 1:
//   * Saturation pressure: Hyland-Wexler over ice (−100…0 °C, eq. 5) and
//     over liquid water (0…200 °C, eq. 6), p_ws in Pa, T in K.
//   * Humidity ratio W = 0.621945 · p_w / (p − p_w)             (eq. 20)
//   * Enthalpy h = 1.006·t + W·(2501 + 1.86·t)   kJ/kg dry air   (eq. 30)
//   * Specific volume v = 0.287042·(t + 273.15)·(1 + 1.607858·W) / p
//                                                 m³/kg dry air, p in kPa (eq. 26)
//   * Wet bulb from the psychrometric equation (eq. 33 above freezing,
//     eq. 35 below), solved by bisection
//   * Standard-atmosphere pressure p = 101.325·(1 − 2.25577e-5·Z)^5.2559 kPa (eq. 3)
//
// The coil model is the usual straight-line process: the leaving state lies
// on the line from the entering state to the apparatus dew point (ADP) on
// the saturation curve, and the bypass factor is how far along that line the
// leaving state stops short. Mixing is by dry-air mass.

using System;

namespace StingTools.Core.Hvac
{
    /// <summary>One moist-air state. Build with <see cref="Psychrometrics"/>.</summary>
    public sealed class AirState
    {
        public double DryBulbC        { get; internal set; }
        public double HumidityRatio   { get; internal set; }   // kg water / kg dry air
        public double PressureKPa     { get; internal set; }
        public double RelativeHumidity{ get; internal set; }   // 0..1
        public double EnthalpyKJkg    { get; internal set; }   // per kg dry air
        public double SpecificVolume  { get; internal set; }   // m³ / kg dry air
        public double DewPointC       { get; internal set; }
        public double WetBulbC        { get; internal set; }

        public override string ToString() =>
            $"{DryBulbC:F1} °C db / {WetBulbC:F1} °C wb / {RelativeHumidity * 100:F0} % RH / " +
            $"W {HumidityRatio * 1000:F2} g/kg / h {EnthalpyKJkg:F1} kJ/kg";
    }

    public sealed class CoilProcessResult
    {
        public AirState Entering        { get; set; }
        public AirState Leaving         { get; set; }
        /// <summary>Apparatus dew point (°C); NaN when the process line does not reach saturation (sensible heating/cooling only).</summary>
        public double   ApparatusDewPointC { get; set; } = double.NaN;
        /// <summary>Bypass factor 0..1; NaN when there is no ADP.</summary>
        public double   BypassFactor    { get; set; } = double.NaN;
        public double   DryAirMassKgS   { get; set; }
        public double   TotalKw         { get; set; }
        public double   SensibleKw      { get; set; }
        public double   LatentKw        { get; set; }
        /// <summary>Sensible heat ratio; NaN when total is ~0.</summary>
        public double   Shr             { get; set; } = double.NaN;
        /// <summary>Condensate removed, L/h (kg/h of water ≈ L/h).</summary>
        public double   CondensateLh    { get; set; }
        /// <summary>True when the requested leaving state is supersaturated (RH &gt; 100 %) and was clamped.</summary>
        public bool     LeavingClamped  { get; set; }
    }

    public static class Psychrometrics
    {
        public const double StandardPressureKPa = 101.325;
        private const double Rw = 0.621945;          // ratio of molar masses, water/dry air

        // ── Pressure ────────────────────────────────────────────────────

        /// <summary>Standard-atmosphere barometric pressure at elevation, kPa.</summary>
        public static double PressureAtElevation(double elevationM)
            => 101.325 * Math.Pow(1.0 - 2.25577e-5 * elevationM, 5.2559);

        // ── Saturation ──────────────────────────────────────────────────

        /// <summary>Saturation vapour pressure, Pa (Hyland-Wexler; ice below 0 °C).</summary>
        public static double SaturationPressurePa(double tC)
        {
            double T = tC + 273.15;
            if (tC < 0)
            {
                const double C1 = -5.6745359e3, C2 = 6.3925247, C3 = -9.6778430e-3,
                             C4 = 6.2215701e-7, C5 = 2.0747825e-9, C6 = -9.4840240e-13, C7 = 4.1635019;
                return Math.Exp(C1 / T + C2 + C3 * T + C4 * T * T + C5 * T * T * T
                                + C6 * T * T * T * T + C7 * Math.Log(T));
            }
            const double C8 = -5.8002206e3, C9 = 1.3914993, C10 = -4.8640239e-2,
                         C11 = 4.1764768e-5, C12 = -1.4452093e-8, C13 = 6.5459673;
            return Math.Exp(C8 / T + C9 + C10 * T + C11 * T * T + C12 * T * T * T + C13 * Math.Log(T));
        }

        /// <summary>Humidity ratio at saturation, kg/kg.</summary>
        public static double SaturationHumidityRatio(double tC, double pKPa = StandardPressureKPa)
        {
            double pws = SaturationPressurePa(tC) / 1000.0;
            return Rw * pws / Math.Max(pKPa - pws, 1e-9);
        }

        // ── Basic relations ─────────────────────────────────────────────

        public static double HumidityRatioFromVapourPressure(double pwKPa, double pKPa)
            => Rw * pwKPa / Math.Max(pKPa - pwKPa, 1e-9);

        public static double VapourPressureFromHumidityRatio(double w, double pKPa)
            => pKPa * w / (Rw + w);

        public static double Enthalpy(double tC, double w) => 1.006 * tC + w * (2501.0 + 1.86 * tC);

        /// <summary>Dry-bulb from enthalpy and humidity ratio (inverse of <see cref="Enthalpy"/>).</summary>
        public static double DryBulbFromEnthalpy(double hKJkg, double w) => (hKJkg - 2501.0 * w) / (1.006 + 1.86 * w);

        public static double SpecificVolume(double tC, double w, double pKPa)
            => 0.287042 * (tC + 273.15) * (1.0 + 1.607858 * w) / pKPa;

        /// <summary>Dew point, °C: the temperature whose saturation pressure equals the vapour pressure.</summary>
        public static double DewPoint(double w, double pKPa = StandardPressureKPa)
        {
            double pw = VapourPressureFromHumidityRatio(w, pKPa) * 1000.0;
            if (pw <= 0) return double.NaN;
            return Bisect(t => SaturationPressurePa(t) - pw, -100.0, 200.0);
        }

        /// <summary>
        /// Humidity ratio implied by a dry-bulb / wet-bulb pair (psychrometric
        /// equation, ASHRAE eq. 33 above freezing, eq. 35 below).
        /// </summary>
        public static double HumidityRatioFromWetBulb(double tC, double twbC, double pKPa = StandardPressureKPa)
        {
            double wsStar = SaturationHumidityRatio(twbC, pKPa);
            if (twbC >= 0)
                return ((2501.0 - 2.326 * twbC) * wsStar - 1.006 * (tC - twbC))
                       / (2501.0 + 1.86 * tC - 4.186 * twbC);
            return ((2830.0 - 0.24 * twbC) * wsStar - 1.006 * (tC - twbC))
                   / (2830.0 + 1.86 * tC - 2.1 * twbC);
        }

        /// <summary>Thermodynamic wet bulb, °C, solved by bisection on the psychrometric equation.</summary>
        public static double WetBulb(double tC, double w, double pKPa = StandardPressureKPa)
        {
            double td = DewPoint(w, pKPa);
            double lo = double.IsNaN(td) ? -100.0 : td;
            return Bisect(twb => HumidityRatioFromWetBulb(tC, twb, pKPa) - w, lo, tC);
        }

        // ── State constructors ──────────────────────────────────────────

        public static AirState FromDryBulbHumidityRatio(double tC, double w, double pKPa = StandardPressureKPa)
        {
            if (w < 0) throw new ArgumentOutOfRangeException(nameof(w), "Humidity ratio cannot be negative.");
            double pw = VapourPressureFromHumidityRatio(w, pKPa);
            double pws = SaturationPressurePa(tC) / 1000.0;
            return new AirState
            {
                DryBulbC         = tC,
                HumidityRatio    = w,
                PressureKPa      = pKPa,
                RelativeHumidity = pws > 0 ? pw / pws : double.NaN,
                EnthalpyKJkg     = Enthalpy(tC, w),
                SpecificVolume   = SpecificVolume(tC, w, pKPa),
                DewPointC        = w > 0 ? DewPoint(w, pKPa) : double.NaN,
                WetBulbC         = WetBulb(tC, w, pKPa)
            };
        }

        /// <summary>State from dry bulb and relative humidity (0..1).</summary>
        public static AirState FromDryBulbRh(double tC, double rh, double pKPa = StandardPressureKPa)
        {
            if (rh < 0 || rh > 1.0 + 1e-9) throw new ArgumentOutOfRangeException(nameof(rh), "RH must be 0..1.");
            double pw = rh * SaturationPressurePa(tC) / 1000.0;
            return FromDryBulbHumidityRatio(tC, HumidityRatioFromVapourPressure(pw, pKPa), pKPa);
        }

        /// <summary>State from dry bulb and wet bulb (the form climate design data is given in).</summary>
        public static AirState FromDryBulbWetBulb(double tC, double twbC, double pKPa = StandardPressureKPa)
        {
            if (twbC > tC + 1e-9) throw new ArgumentOutOfRangeException(nameof(twbC), "Wet bulb cannot exceed dry bulb.");
            return FromDryBulbHumidityRatio(tC, Math.Max(0, HumidityRatioFromWetBulb(tC, twbC, pKPa)), pKPa);
        }

        // ── Processes ───────────────────────────────────────────────────

        /// <summary>
        /// Adiabatic mixing by dry-air mass. <paramref name="fractionA"/> is
        /// the dry-air mass fraction of stream A (e.g. outdoor-air fraction).
        /// </summary>
        public static AirState Mix(AirState a, AirState b, double fractionA)
        {
            if (fractionA < 0 || fractionA > 1) throw new ArgumentOutOfRangeException(nameof(fractionA));
            double w = fractionA * a.HumidityRatio + (1 - fractionA) * b.HumidityRatio;
            double h = fractionA * a.EnthalpyKJkg + (1 - fractionA) * b.EnthalpyKJkg;
            double p = fractionA * a.PressureKPa + (1 - fractionA) * b.PressureKPa;
            return FromDryBulbHumidityRatio(DryBulbFromEnthalpy(h, w), w, p);
        }

        /// <summary>
        /// Cooling-coil process from an entering state to a leaving state given
        /// by dry bulb and RH, for a volume flow measured at the entering state.
        /// </summary>
        public static CoilProcessResult CoolingCoil(AirState entering, double leavingDbC, double leavingRh,
            double volumeFlowM3s)
        {
            double p = entering.PressureKPa;
            bool clamped = leavingRh > 1.0;
            var leaving = FromDryBulbRh(leavingDbC, Math.Min(leavingRh, 1.0), p);
            // A coil cannot add moisture: a leaving W above the entering W means
            // the requested state is a sensible-only process at entering W.
            if (leaving.HumidityRatio > entering.HumidityRatio)
                leaving = FromDryBulbHumidityRatio(leavingDbC, entering.HumidityRatio, p);

            double m = volumeFlowM3s / entering.SpecificVolume;                 // kg dry air / s
            double total = m * (entering.EnthalpyKJkg - leaving.EnthalpyKJkg);  // kW
            // Sensible at the leaving humidity ratio: the latent part is the
            // enthalpy of the water removed, the rest is sensible.
            double hAtEnteringTLeavingW = Enthalpy(entering.DryBulbC, leaving.HumidityRatio);
            double sensible = m * (hAtEnteringTLeavingW - leaving.EnthalpyKJkg);
            double latent = total - sensible;

            var r = new CoilProcessResult
            {
                Entering       = entering,
                Leaving        = leaving,
                DryAirMassKgS  = m,
                TotalKw        = total,
                SensibleKw     = sensible,
                LatentKw       = latent,
                Shr            = Math.Abs(total) > 1e-9 ? sensible / total : double.NaN,
                CondensateLh   = Math.Max(0, m * (entering.HumidityRatio - leaving.HumidityRatio)) * 3600.0,
                LeavingClamped = clamped
            };

            double adp = ApparatusDewPoint(entering, leaving);
            if (!double.IsNaN(adp))
            {
                r.ApparatusDewPointC = adp;
                double span = entering.DryBulbC - adp;
                r.BypassFactor = Math.Abs(span) > 1e-9 ? (leaving.DryBulbC - adp) / span : double.NaN;
            }
            return r;
        }

        /// <summary>
        /// Where the straight process line entering → leaving, extended past
        /// the leaving state, meets the saturation curve. NaN for a line that
        /// never reaches saturation (no dehumidification).
        /// </summary>
        public static double ApparatusDewPoint(AirState entering, AirState leaving)
        {
            double dt = leaving.DryBulbC - entering.DryBulbC;
            double dw = leaving.HumidityRatio - entering.HumidityRatio;
            if (dt >= 0 || dw >= -1e-9) return double.NaN;
            double p = entering.PressureKPa;
            // s = 1 at the leaving state; f(s) > 0 while the point is unsaturated.
            double F(double s)
            {
                double t = entering.DryBulbC + s * dt;
                double w = entering.HumidityRatio + s * dw;
                return SaturationHumidityRatio(t, p) - w;
            }
            if (F(1.0) <= 0) return leaving.DryBulbC;               // leaving already saturated
            double hi = 1.0;
            for (int i = 0; i < 60 && F(hi) > 0; i++)
            {
                hi *= 1.5;
                if (entering.HumidityRatio + hi * dw <= 0) return double.NaN;
            }
            if (F(hi) > 0) return double.NaN;
            double s0 = Bisect(F, 1.0, hi);
            return entering.DryBulbC + s0 * dt;
        }

        // ── Numerics ────────────────────────────────────────────────────

        private static double Bisect(Func<double, double> f, double lo, double hi)
        {
            double flo = f(lo), fhi = f(hi);
            if (Math.Sign(flo) == Math.Sign(fhi))
                return Math.Abs(flo) < Math.Abs(fhi) ? lo : hi;
            for (int i = 0; i < 200; i++)
            {
                double mid = 0.5 * (lo + hi);
                double fm = f(mid);
                if (Math.Abs(fm) < 1e-12 || hi - lo < 1e-9) return mid;
                if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; }
                else hi = mid;
            }
            return 0.5 * (lo + hi);
        }
    }
}
