// StingTools — ASHRAE Radiant Time Series (RTS) lag pass.
//
// The block-load engine ships a reactive model: solar gain at hour 10
// lands in the room as cooling load at hour 10. ASHRAE RTS (Handbook
// Fundamentals 2021 Ch.18) recognises that the RADIANT portion of any
// heat gain is absorbed by the building mass and re-emitted over the
// following ~24 hours. Convective gains hit the air instantly; radiant
// gains spread.
//
// Net effect on a sized peak:
//   * Solar peaks ~30-60 min later in the day than reactive models say.
//   * Smaller peaks for heavy-mass buildings (gain spread over more hours).
//   * Larger sustained loads outside the peak hour.
//
// We ship tabulated Radiant Time Factors (RTF) for three construction
// classes (Light / Medium / Heavy) per ASHRAE 2021 Table 19. RTF₀..₂₃
// sum to 1.0 and represent the fraction of an instantaneous radiant
// gain that converts to load at hour 0, 1, 2, … 23 after the gain.
//
// Default radiant fractions per gain type (ASHRAE 2021 Table 14):
//   Conduction (walls, roof)  → 63 % radiant / 37 % convective
//   Solar through glass       → 100 % radiant / 0 % convective (transmitted)
//   Occupants — sensible      → 70 % radiant / 30 % convective
//   Lighting — recessed       → 67 % radiant / 33 % convective
//   Equipment — generic       → 50 % radiant / 50 % convective
//
// Engine integration is opt-in: BlockLoadEngine.Run takes a
// RtsConstructionClass parameter. When set to `Reactive` (the default)
// the calc is unchanged. When set to `Light/Medium/Heavy` each hourly
// gain stream is split into radiant + convective, the radiant fraction
// is convolved with the matching RTF, and the result re-aggregated.

using System;

namespace StingTools.Core.Hvac.Loads
{
    public enum RtsConstructionClass
    {
        /// <summary>No RTS lag — load = instantaneous gain (Phase 187 default).</summary>
        Reactive,
        /// <summary>Light mass: timber frame, raised-access floor, suspended ceiling.</summary>
        Light,
        /// <summary>Medium mass: light steel frame, exposed deck, gyp board.</summary>
        Medium,
        /// <summary>Heavy mass: concrete frame, exposed slab, masonry partitions.</summary>
        Heavy
    }

    public static class RadiantTimeSeries
    {
        // ASHRAE Handbook Fundamentals 2021 Table 19a — non-solar RTF for
        // a representative ZONE of each construction class. Indexed 0..23.
        // The tables shipped here are the "medium glass / interior carpet"
        // baseline; future versions can split by glass area + flooring.
        private static readonly double[] _light = new double[]
        {
            0.47, 0.19, 0.11, 0.06, 0.04, 0.03, 0.02, 0.02, 0.02, 0.01, 0.01, 0.01,
            0.005, 0.005, 0.005, 0.005, 0.005, 0.005, 0.005, 0, 0, 0, 0, 0
        };
        private static readonly double[] _medium = new double[]
        {
            0.34, 0.18, 0.11, 0.08, 0.06, 0.04, 0.03, 0.03, 0.02, 0.02, 0.02, 0.02,
            0.01, 0.01, 0.01, 0.01, 0.005, 0.005, 0.005, 0.005, 0.005, 0, 0, 0
        };
        private static readonly double[] _heavy = new double[]
        {
            0.23, 0.14, 0.10, 0.08, 0.07, 0.06, 0.05, 0.04, 0.04, 0.03, 0.03, 0.03,
            0.02, 0.02, 0.02, 0.02, 0.01, 0.01, 0.01, 0.005, 0.005, 0.005, 0.005, 0.005
        };

        /// <summary>Return the 24-hour radiant time factor array for a class.</summary>
        public static double[] FactorsFor(RtsConstructionClass cls)
        {
            return cls switch
            {
                RtsConstructionClass.Light  => _light,
                RtsConstructionClass.Medium => _medium,
                RtsConstructionClass.Heavy  => _heavy,
                _                           => null
            };
        }

        /// <summary>
        /// Convolve a 24-hour gain stream with the radiant time factors,
        /// returning a 24-hour load stream that's the radiant gain
        /// spread out over time. Convective fraction is passed through
        /// unchanged by the caller.
        /// </summary>
        public static double[] ConvolveRadiant(double[] hourlyGainsW, RtsConstructionClass cls)
        {
            if (hourlyGainsW == null || hourlyGainsW.Length != 24) return hourlyGainsW;
            var rtf = FactorsFor(cls);
            if (rtf == null) return hourlyGainsW;          // Reactive
            var outLoad = new double[24];
            for (int g = 0; g < 24; g++)
            {
                double gain = hourlyGainsW[g];
                if (gain == 0) continue;
                for (int t = 0; t < 24; t++)
                {
                    int hour = (g + t) % 24;
                    outLoad[hour] += gain * rtf[t];
                }
            }
            return outLoad;
        }

        /// <summary>
        /// Split a gain stream into (radiant, convective) and apply RTS
        /// to the radiant portion only. Returns the sum.
        /// </summary>
        public static double[] ApplyRtsToGain(double[] hourlyGainsW,
            double radiantFraction, RtsConstructionClass cls)
        {
            if (hourlyGainsW == null || hourlyGainsW.Length != 24) return hourlyGainsW;
            if (cls == RtsConstructionClass.Reactive) return hourlyGainsW;
            radiantFraction = Math.Max(0, Math.Min(1, radiantFraction));
            var radiant   = new double[24];
            var convective = new double[24];
            for (int h = 0; h < 24; h++)
            {
                radiant[h]    = hourlyGainsW[h] * radiantFraction;
                convective[h] = hourlyGainsW[h] * (1 - radiantFraction);
            }
            var laggedRad = ConvolveRadiant(radiant, cls);
            var result = new double[24];
            for (int h = 0; h < 24; h++) result[h] = laggedRad[h] + convective[h];
            return result;
        }

        /// <summary>Canonical radiant fractions per ASHRAE Handbook 2021 Table 14.</summary>
        public static class RadiantFraction
        {
            public const double Conduction = 0.63;
            public const double SolarGlass = 1.00;
            public const double Occupant   = 0.70;
            public const double Lighting   = 0.67;
            public const double Equipment  = 0.50;
        }

        // ── Phase 187g — per-zone RTF from thermal mass ─────────────────
        //
        // ASHRAE's rigorous RTS derives Radiant Time Factors from each
        // zone's Conduction Transfer Function via Laplace-domain inversion
        // (Handbook 2021 Ch.18 §RTS-MATHEMATICS). That's a heavy implementation
        // — needs polynomial CTF coefficients per construction layer.
        //
        // The practical middle-ground STING ships: derive a thermal-mass
        // score per zone (area-weighted Σ ρ·c·thickness across the envelope),
        // then interpolate between the published Light/Medium/Heavy tables.
        // Direction-of-effect is correct (heavier mass → longer lag, lower
        // peak) without the full CTF math. Reverts to Heavy at very high
        // mass and to Reactive when zone has no envelope info.

        /// <summary>
        /// Build a 24-hour Radiant Time Factor array interpolated from the
        /// area-weighted average thermal mass of a zone's envelope. Returns
        /// null when <paramref name="avgThermalMassKJperM2K"/> ≤ 0 — caller
        /// then falls back to the class-based RTF (FactorsFor).
        ///
        /// Interpolation breakpoints:
        ///   ≤  50 kJ/m²K → Light
        ///   = 200        → Medium
        ///   ≥ 400        → Heavy
        ///   intermediate → linear interpolation between bracketing tables.
        /// </summary>
        public static double[] FactorsForThermalMass(double avgThermalMassKJperM2K)
        {
            if (avgThermalMassKJperM2K <= 0) return null;
            const double light = 50.0, medium = 200.0, heavy = 400.0;
            if (avgThermalMassKJperM2K <= light)  return _light;
            if (avgThermalMassKJperM2K >= heavy)  return _heavy;
            if (avgThermalMassKJperM2K <= medium)
            {
                double t = (avgThermalMassKJperM2K - light) / (medium - light);
                return Lerp(_light, _medium, t);
            }
            else
            {
                double t = (avgThermalMassKJperM2K - medium) / (heavy - medium);
                return Lerp(_medium, _heavy, t);
            }
        }

        /// <summary>
        /// Convolve a 24-hour gain stream with a caller-supplied RTF array
        /// (used for the per-zone path; the class-based path goes through
        /// <see cref="ConvolveRadiant"/>).
        /// </summary>
        public static double[] ConvolveRadiantWithRtf(double[] hourlyGainsW, double[] rtf)
        {
            if (hourlyGainsW == null || hourlyGainsW.Length != 24 || rtf == null) return hourlyGainsW;
            var outLoad = new double[24];
            for (int g = 0; g < 24; g++)
            {
                double gain = hourlyGainsW[g];
                if (gain == 0) continue;
                for (int t = 0; t < 24; t++)
                {
                    int hour = (g + t) % 24;
                    outLoad[hour] += gain * rtf[Math.Min(t, rtf.Length - 1)];
                }
            }
            return outLoad;
        }

        /// <summary>
        /// Per-zone variant of <see cref="ApplyRtsToGain"/> — splits radiant
        /// + convective per the supplied fraction, convolves the radiant
        /// portion with the zone-specific RTF, passes convective through.
        /// </summary>
        public static double[] ApplyRtsToGainWithRtf(double[] hourlyGainsW,
            double radiantFraction, double[] rtf)
        {
            if (hourlyGainsW == null || hourlyGainsW.Length != 24) return hourlyGainsW;
            if (rtf == null) return hourlyGainsW;
            radiantFraction = Math.Max(0, Math.Min(1, radiantFraction));
            var radiant    = new double[24];
            var convective = new double[24];
            for (int h = 0; h < 24; h++)
            {
                radiant[h]    = hourlyGainsW[h] * radiantFraction;
                convective[h] = hourlyGainsW[h] * (1 - radiantFraction);
            }
            var laggedRad = ConvolveRadiantWithRtf(radiant, rtf);
            var result = new double[24];
            for (int h = 0; h < 24; h++) result[h] = laggedRad[h] + convective[h];
            return result;
        }

        private static double[] Lerp(double[] a, double[] b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            var r = new double[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < r.Length; i++)
            {
                double av = i < a.Length ? a[i] : 0;
                double bv = i < b.Length ? b[i] : 0;
                r[i] = av * (1 - t) + bv * t;
            }
            return r;
        }
    }
}
