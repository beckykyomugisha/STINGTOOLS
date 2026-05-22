// StingTools — Octave-band sound-power container.
//
// Eight standard octave centres: 63, 125, 250, 500, 1000, 2000, 4000,
// 8000 Hz. Used for fan source spectra, attenuation tables, and the
// final NC-rating regression.

using System;
using System.Linq;

namespace StingTools.Core.Acoustic
{
    public struct OctaveBand
    {
        public double Hz63;
        public double Hz125;
        public double Hz250;
        public double Hz500;
        public double Hz1000;
        public double Hz2000;
        public double Hz4000;
        public double Hz8000;

        public static readonly double[] CentreFrequencies =
            new double[] { 63, 125, 250, 500, 1000, 2000, 4000, 8000 };

        public double this[int i]
        {
            get => i switch
            {
                0 => Hz63, 1 => Hz125, 2 => Hz250, 3 => Hz500,
                4 => Hz1000, 5 => Hz2000, 6 => Hz4000, 7 => Hz8000,
                _ => 0
            };
            set
            {
                switch (i)
                {
                    case 0: Hz63 = value; break;
                    case 1: Hz125 = value; break;
                    case 2: Hz250 = value; break;
                    case 3: Hz500 = value; break;
                    case 4: Hz1000 = value; break;
                    case 5: Hz2000 = value; break;
                    case 6: Hz4000 = value; break;
                    case 7: Hz8000 = value; break;
                }
            }
        }

        public double[] AsArray() => new[]
        {
            Hz63, Hz125, Hz250, Hz500, Hz1000, Hz2000, Hz4000, Hz8000
        };

        public static OctaveBand FromArray(double[] a)
        {
            var b = new OctaveBand();
            for (int i = 0; i < 8 && i < (a?.Length ?? 0); i++) b[i] = a[i];
            return b;
        }

        /// <summary>Energy-summed addition of two spectra (Lw or Lp).</summary>
        public static OctaveBand AddLevels(OctaveBand a, OctaveBand b)
        {
            var r = new OctaveBand();
            for (int i = 0; i < 8; i++)
            {
                double la = a[i], lb = b[i];
                r[i] = 10 * Math.Log10(Math.Pow(10, la / 10) + Math.Pow(10, lb / 10));
            }
            return r;
        }

        /// <summary>Subtract attenuation (positive dB values) from a level spectrum.</summary>
        public static OctaveBand operator -(OctaveBand level, OctaveBand attenuation)
        {
            var r = new OctaveBand();
            for (int i = 0; i < 8; i++) r[i] = level[i] - attenuation[i];
            return r;
        }

        /// <summary>Add two spectra arithmetically (use for attenuation tally).</summary>
        public static OctaveBand operator +(OctaveBand a, OctaveBand b)
        {
            var r = new OctaveBand();
            for (int i = 0; i < 8; i++) r[i] = a[i] + b[i];
            return r;
        }
    }

    /// <summary>
    /// ASHRAE NC (Noise Criteria) curve evaluator.
    /// NC rating = the lowest curve number that is not exceeded by the
    /// Lp spectrum at any octave from 63 Hz to 8000 Hz.
    /// </summary>
    public static class NcCurves
    {
        // Tabulated NC curves (NC-15 through NC-65 in 5-step increments).
        // Columns are octave centres 63 → 8k Hz.
        public static readonly System.Collections.Generic.Dictionary<int, double[]> Curves = new()
        {
            { 15, new[]{ 47.0, 36.0, 29.0, 22.0, 17.0, 14.0, 12.0, 11.0 } },
            { 20, new[]{ 51.0, 40.0, 33.0, 26.0, 22.0, 19.0, 17.0, 16.0 } },
            { 25, new[]{ 54.0, 44.0, 37.0, 31.0, 27.0, 24.0, 22.0, 21.0 } },
            { 30, new[]{ 57.0, 48.0, 41.0, 35.0, 31.0, 29.0, 28.0, 27.0 } },
            { 35, new[]{ 60.0, 52.0, 45.0, 40.0, 36.0, 34.0, 33.0, 32.0 } },
            { 40, new[]{ 64.0, 56.0, 50.0, 45.0, 41.0, 39.0, 38.0, 37.0 } },
            { 45, new[]{ 67.0, 60.0, 54.0, 49.0, 46.0, 44.0, 43.0, 42.0 } },
            { 50, new[]{ 71.0, 64.0, 58.0, 54.0, 51.0, 49.0, 48.0, 47.0 } },
            { 55, new[]{ 74.0, 67.0, 62.0, 58.0, 56.0, 54.0, 53.0, 52.0 } },
            { 60, new[]{ 77.0, 71.0, 67.0, 63.0, 61.0, 59.0, 58.0, 57.0 } },
            { 65, new[]{ 80.0, 75.0, 71.0, 68.0, 66.0, 64.0, 63.0, 62.0 } }
        };

        /// <summary>
        /// Return the NC rating of an Lp spectrum (dB re 20 μPa) —
        /// the lowest NC curve that is not exceeded at any octave.
        /// </summary>
        public static int Rate(OctaveBand lp)
        {
            foreach (var (nc, curve) in Curves.OrderBy(k => k.Key))
            {
                bool fits = true;
                for (int i = 0; i < 8; i++)
                    if (lp[i] > curve[i]) { fits = false; break; }
                if (fits) return nc;
            }
            return 65; // exceeds highest tabulated — report as "≥65"
        }
    }
}
