using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StingTools.Commands.Electrical.Coordination
{
    /// <summary>Tripping characteristic family of a protective device.</summary>
    public enum DeviceCurve
    {
        /// <summary>Nothing known — no band can be drawn.</summary>
        Unknown,
        /// <summary>IEC 60898-1 type B: instantaneous 3–5 × In.</summary>
        B,
        /// <summary>IEC 60898-1 type C: instantaneous 5–10 × In.</summary>
        C,
        /// <summary>IEC 60898-1 type D: instantaneous 10–20 × In.</summary>
        D,
        /// <summary>Moulded-case breaker — settings are manufacturer-specific; no generic band.</summary>
        Mccb,
        /// <summary>Air circuit breaker — settings are manufacturer-specific; no generic band.</summary>
        Acb
    }

    /// <summary>
    /// A protective device as a min-time / max-time BAND, built only from the
    /// IEC 60898-1 type-test points (Table 7). Nothing between the test points is
    /// invented: the band edges are step functions justified by the fact that a
    /// thermal-magnetic characteristic is monotonically inverse-time.
    ///
    ///   a) 1.13·In  no trip within 1 h (2 h for In &gt; 63 A)          → min-time edge
    ///   b) 1.45·In  trips within 1 h (2 h for In &gt; 63 A)           → max-time edge
    ///   c) 2.55·In  trips in 1 s &lt; t &lt; 60 s (120 s for In &gt; 32 A)  → both edges
    ///   d) lower instantaneous (B 3, C 5, D 10 × In): no trip before 0.1 s → min-time edge
    ///   e) upper instantaneous (B 5, C 10, D 20 × In): trips before 0.1 s  → max-time edge
    ///
    /// MCCB / ACB have adjustable, manufacturer-specific characteristics and get NO band.
    /// </summary>
    public sealed class DeviceBand
    {
        public string Label   { get; set; } = "";
        public double RatingA { get; set; }
        public DeviceCurve Curve { get; set; }

        /// <summary>True only for IEC 60898-1 B / C / D devices with a known rating.</summary>
        public bool HasBand =>
            RatingA > 0 && (Curve == DeviceCurve.B || Curve == DeviceCurve.C || Curve == DeviceCurve.D);

        /// <summary>Lower instantaneous multiple (test d): B 3, C 5, D 10.</summary>
        public double LowerInstMultiple => Curve switch
        {
            DeviceCurve.B => 3, DeviceCurve.C => 5, DeviceCurve.D => 10, _ => double.NaN
        };

        /// <summary>Upper instantaneous multiple (test e): B 5, C 10, D 20.</summary>
        public double UpperInstMultiple => Curve switch
        {
            DeviceCurve.B => 5, DeviceCurve.C => 10, DeviceCurve.D => 20, _ => double.NaN
        };

        public double LowerInstA => LowerInstMultiple * RatingA;
        public double UpperInstA => UpperInstMultiple * RatingA;

        /// <summary>Conventional time for tests a / b: 1 h (2 h for In &gt; 63 A).</summary>
        public double ConventionalTimeS => RatingA > 63 ? 7200 : 3600;

        /// <summary>Upper time limit of test c at 2.55·In: 60 s (120 s for In &gt; 32 A).</summary>
        public double TestCMaxS => RatingA > 32 ? 120 : 60;

        /// <summary>
        /// Earliest time (s) the device could trip at <paramref name="currentA"/>
        /// (lower edge of the band). 0 = may trip instantaneously.
        /// NaN when the device has no band.
        /// </summary>
        public double MinTripTimeS(double currentA)
        {
            if (!HasBand) return double.NaN;
            double m = Multiple(currentA);
            if (m < 1.13) return ConventionalTimeS;      // test a — and monotonic below it
            if (m < 2.55) return 1.0;                    // test c lower limit, monotonic
            if (m < LowerInstMultiple) return 0.1;       // test d, monotonic
            return 0.0;                                  // instantaneous release may operate
        }

        /// <summary>
        /// Latest time (s) by which the device is guaranteed to have tripped at
        /// <paramref name="currentA"/> (upper edge of the band).
        /// +∞ = no trip is guaranteed. NaN when the device has no band.
        /// </summary>
        public double MaxClearTimeS(double currentA)
        {
            if (!HasBand) return double.NaN;
            double m = Multiple(currentA);
            if (m < 1.45) return double.PositiveInfinity; // below conventional tripping current
            if (m < 2.55) return ConventionalTimeS;      // test b, monotonic
            if (m < UpperInstMultiple) return TestCMaxS;  // test c upper limit, monotonic
            return 0.1;                                  // test e
        }

        /// <summary>
        /// I / In, rounded to 1e-9 so a breakpoint computed as 1.45 × In (which floating
        /// point can land at 1.4499999…) falls on the side the standard puts it.
        /// </summary>
        private double Multiple(double currentA) => Math.Round(currentA / RatingA, 9);

        public override string ToString() =>
            HasBand ? $"{Curve}{RatingA:0.#}" : $"{Label} ({Curve})";
    }

    /// <summary>Outcome of a band-based selectivity check for one upstream/downstream pair.</summary>
    public enum SelectivityVerdict
    {
        /// <summary>Upstream minimum-trip edge lies above downstream maximum-clear edge over the whole range.</summary>
        Selective,
        /// <summary>The bands overlap somewhere in the range — generic data cannot show selectivity.</summary>
        NotAssured,
        /// <summary>Upstream is guaranteed to trip before downstream can at some current.</summary>
        NotSelective,
        /// <summary>At least one device has no generic band (MCCB / ACB / unknown).</summary>
        NoCurveData
    }

    public sealed class SelectivityResult
    {
        public SelectivityVerdict Verdict { get; set; }
        /// <summary>Lowest current (A) at which the verdict's condition first applies; 0 when none.</summary>
        public double CriticalCurrentA { get; set; }
        public double ProspectiveFaultKa { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// Pure, Revit-free IEC 60898-1 band model and selectivity check. See
    /// <see cref="DeviceBand"/> for the only data it uses.
    /// </summary>
    public static class IecMcbBands
    {
        /// <summary>Wording that must accompany every result derived from these bands.</summary>
        public const string Basis =
            "generic IEC 60898 bands — confirm with manufacturer selectivity tables";

        private static readonly Regex RatingRx =
            new Regex(@"(?<![\d.])(\d+(?:\.\d+)?)\s*A(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex LetterBeforeRx =
            new Regex(@"(?<![A-Za-z])([BCD])\s*-?\s*(\d+(?:\.\d+)?)(?![\d.])", RegexOptions.CultureInvariant);
        private static readonly Regex LetterAfterRx =
            new Regex(@"(?:\bMCB[-_ ]?|\bTYPE\s*|\bCURVE\s*|\d\s*A\s+)([BCD])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Builds a band from a device label (e.g. "C16", "16A C", "B32", "250A 65kA MCCB")
        /// and an optional type hint (e.g. "MCB-C", "MCCB", "ACB"). Never invents a curve
        /// letter: an MCB whose letter cannot be read gets <see cref="DeviceCurve.Unknown"/>.
        /// </summary>
        public static DeviceBand Parse(string label, string typeHint = null)
        {
            var band = new DeviceBand { Label = label ?? "" };
            string text = (label ?? "") + " " + (typeHint ?? "");
            string upper = text.ToUpperInvariant();

            // Rating: "16A" / "16 A" (never "kA"), else "C16" form.
            var mr = RatingRx.Match(text);
            var ml = LetterBeforeRx.Match(label ?? "");
            if (mr.Success) band.RatingA = double.Parse(mr.Groups[1].Value, CultureInfo.InvariantCulture);
            else if (ml.Success) band.RatingA = double.Parse(ml.Groups[2].Value, CultureInfo.InvariantCulture);

            if (upper.Contains("MCCB")) { band.Curve = DeviceCurve.Mccb; return band; }
            if (upper.Contains("ACB"))  { band.Curve = DeviceCurve.Acb;  return band; }

            string letter = null;
            if (ml.Success) letter = ml.Groups[1].Value;
            else
            {
                var ma = LetterAfterRx.Match(text);
                if (ma.Success) letter = ma.Groups[1].Value.ToUpperInvariant();
            }
            band.Curve = letter switch
            {
                "B" => DeviceCurve.B,
                "C" => DeviceCurve.C,
                "D" => DeviceCurve.D,
                _   => DeviceCurve.Unknown
            };
            return band;
        }

        /// <summary>
        /// Band selectivity check. The upstream minimum-trip edge must lie at or above the
        /// downstream maximum-clear edge at every current from the downstream conventional
        /// tripping current (1.45·In) up to the prospective fault current. Band edges come
        /// from strict (&lt;) and non-strict (≥) test limits, so touching edges do not overlap.
        /// </summary>
        /// <param name="upstream">Upstream device band.</param>
        /// <param name="downstream">Downstream device band.</param>
        /// <param name="prospectiveFaultKa">Prospective fault current at the downstream device, kA.</param>
        public static SelectivityResult Check(DeviceBand upstream, DeviceBand downstream, double prospectiveFaultKa)
        {
            var res = new SelectivityResult { ProspectiveFaultKa = prospectiveFaultKa };
            if (upstream == null || downstream == null || !upstream.HasBand || !downstream.HasBand)
            {
                string which = (upstream == null || !upstream.HasBand) ? upstream?.ToString() ?? "upstream" : downstream?.ToString() ?? "downstream";
                res.Verdict = SelectivityVerdict.NoCurveData;
                res.Reason = $"no curve data for {which} — manufacturer time-current and selectivity data required";
                return res;
            }
            if (!(prospectiveFaultKa > 0))
            {
                res.Verdict = SelectivityVerdict.NotAssured;
                res.Reason = "prospective fault current unknown — selectivity cannot be assessed";
                return res;
            }

            double iLow = 1.45 * downstream.RatingA;
            double iHigh = prospectiveFaultKa * 1000.0;
            var samples = SampleCurrents(upstream, downstream, iLow, iHigh);

            double firstOverlap = 0;
            foreach (double i in samples)
            {
                double upMin = upstream.MinTripTimeS(i);
                double upMax = upstream.MaxClearTimeS(i);
                double dnMin = downstream.MinTripTimeS(i);
                double dnMax = downstream.MaxClearTimeS(i);

                if (upMax < dnMin)
                {
                    res.Verdict = SelectivityVerdict.NotSelective;
                    res.CriticalCurrentA = i;
                    res.Reason = $"at {i:0} A upstream {upstream} is guaranteed to trip (≤ {FormatT(upMax)}) " +
                                 $"before downstream {downstream} can (≥ {FormatT(dnMin)})";
                    return res;
                }
                if (upMin < dnMax && firstOverlap == 0) firstOverlap = i;
            }

            if (firstOverlap > 0)
            {
                res.Verdict = SelectivityVerdict.NotAssured;
                res.CriticalCurrentA = firstOverlap;
                res.Reason = firstOverlap >= upstream.LowerInstA
                    ? $"prospective fault {prospectiveFaultKa:0.###} kA reaches the upstream {upstream} instantaneous lower band " +
                      $"({upstream.LowerInstA:0} A) — selectivity NOT assured; manufacturer selectivity table required"
                    : $"bands overlap from {firstOverlap:0} A (upstream {upstream} may trip before downstream {downstream} clears) — " +
                      "selectivity NOT assured; manufacturer selectivity table required";
                return res;
            }

            res.Verdict = SelectivityVerdict.Selective;
            res.Reason = $"bands separate up to {prospectiveFaultKa:0.###} kA";
            return res;
        }

        /// <summary>
        /// Log-spaced samples between the limits plus every band breakpoint of both
        /// devices, taken just below and just above, so a step edge is never skipped.
        /// </summary>
        public static List<double> SampleCurrents(DeviceBand up, DeviceBand dn, double iLow, double iHigh)
        {
            var pts = new List<double>();
            if (!(iHigh > 0)) return pts;
            if (iLow > iHigh) { pts.Add(iHigh); return pts; }
            const int n = 400;
            double l0 = Math.Log10(iLow), l1 = Math.Log10(iHigh);
            for (int k = 0; k <= n; k++) pts.Add(Math.Pow(10, l0 + (l1 - l0) * k / n));
            foreach (var b in new[] { up, dn })
            {
                foreach (double m in new[] { 1.13, 1.45, 2.55, b.LowerInstMultiple, b.UpperInstMultiple })
                {
                    double bp = m * b.RatingA;
                    foreach (double f in new[] { 0.999999, 1.0, 1.000001 })
                    {
                        double x = bp * f;
                        if (x >= iLow && x <= iHigh) pts.Add(x);
                    }
                }
            }
            return pts.Distinct().OrderBy(x => x).ToList();
        }

        private static string FormatT(double s) =>
            double.IsPositiveInfinity(s) ? "never" : s < 1 ? $"{s * 1000:0} ms" : $"{s:0.#} s";
    }
}
