using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.SLD;

namespace StingTools.Commands.Electrical.Coordination
{
    public class CoordViolation
    {
        public string UpstreamDevice   { get; set; } = "";
        public string DownstreamDevice { get; set; } = "";
        public double FaultKa          { get; set; }
        public string Reason           { get; set; } = "";
        /// <summary>
        /// True when Zone-Selective Interlocking is active and both devices are
        /// electronic-trip (MCCB / ACB) types. The violation is still recorded
        /// but should be treated as informational rather than a hard fail.
        /// </summary>
        public bool IsZsiMitigated { get; set; }
    }

    /// <summary>
    /// Selective-coordination checker. Walks the SLD hierarchy and asserts
    /// that, at every fault level the downstream device might see, the
    /// upstream device clears SLOWER by at least the coordination margin
    /// factor. ALL violations at every sample point are recorded (not just
    /// the first per pair). Log-log interpolation is used when TCC curve
    /// data is available; otherwise the linear-ramp fallback is used.
    /// </summary>
    public static class SelectiveCoordEngine
    {
        /// <summary>
        /// Check selective coordination across the entire SLD tree.
        /// </summary>
        /// <param name="root">Root node of the single-line diagram.</param>
        /// <param name="tcc">TCC database to resolve device curves.</param>
        /// <param name="maxFaultKaFallback">
        ///     Upper fault-level bound used when neither device specifies a
        ///     rated maximum (kA). Default 10 kA.
        /// </param>
        /// <param name="sampleCount">
        ///     Number of evenly-spaced fault-level samples across the range.
        ///     Default 20.
        /// </param>
        /// <param name="coordMarginFactor">
        ///     The upstream device must clear at least
        ///     <c>upMs * coordMarginFactor</c> milliseconds above the downstream
        ///     device. Values &lt; 1.0 are clamped to 1.0. Default 1.1 (10 %
        ///     margin).
        /// </param>
        /// <param name="zsiEnabled">
        ///     When true, violations where both devices are electronic-trip
        ///     types (MCCB / ACB) are flagged with a ZSI note and
        ///     <see cref="CoordViolation.IsZsiMitigated"/> = true instead of
        ///     being recorded as hard failures.
        /// </param>
        public static List<CoordViolation> Check(
            SLDNode root,
            TccDatabase tcc,
            double maxFaultKaFallback = 10.0,
            int    sampleCount        = 20,
            double coordMarginFactor  = 1.1,
            bool   zsiEnabled         = false)
        {
            var violations = new List<CoordViolation>();
            if (root == null || tcc == null) return violations;

            // Clamp margin to at least 1.0 (upstream must be at least as slow)
            double margin = Math.Max(1.0, coordMarginFactor);

            CheckNode(root, null, tcc, maxFaultKaFallback, sampleCount, margin, zsiEnabled, violations);
            return violations;
        }

        // ── Electronic-trip device types that are eligible for ZSI ──────────
        private static readonly HashSet<string> ZsiEligibleTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MCCB", "ACB" };

        private static bool IsZsiEligible(TccEntry entry)
            => entry != null && ZsiEligibleTypes.Contains(entry.Type ?? "");

        private static void CheckNode(
            SLDNode node,
            SLDNode parent,
            TccDatabase tcc,
            double maxFaultKaFallback,
            int    sampleCount,
            double margin,
            bool   zsiEnabled,
            List<CoordViolation> violations)
        {
            if (parent != null && node != null)
            {
                var upDev   = tcc.Resolve(parent.Rating);
                var downDev = tcc.Resolve(node.Rating);

                if (upDev != null && downDev != null)
                {
                    // Resolve log-log curves (null when not in database)
                    TccCurve upCurve   = tcc.ResolveCurve(parent.Rating);
                    TccCurve downCurve = tcc.ResolveCurve(node.Rating);

                    // Whether ZSI could mitigate a violation for this pair
                    bool pairZsiEligible = zsiEnabled
                        && IsZsiEligible(upDev)
                        && IsZsiEligible(downDev);

                    double maxFaultKa = Math.Min(upDev.MaxFaultKa, downDev.MaxFaultKa);
                    if (maxFaultKa <= 0) maxFaultKa = maxFaultKaFallback;

                    double step = maxFaultKa / Math.Max(1, sampleCount);

                    for (double f = step; f <= maxFaultKa + step * 0.001; f += step)
                    {
                        // Clamp to the declared maximum so floating-point drift
                        // does not produce a sample that exceeds the range.
                        double fSample = Math.Min(f, maxFaultKa);

                        double upMs   = upDev.ClearingTimeMs(fSample, upCurve);
                        double downMs = downDev.ClearingTimeMs(fSample, downCurve);

                        // Violation: upstream does NOT clear at least (margin × downstream)
                        // i.e. upMs * margin <= downMs  →  upstream trips as fast as (or
                        // faster than) downstream within the required margin.
                        if (upMs > 0 && upMs * margin <= downMs)
                        {
                            bool zsiMitigated = pairZsiEligible;

                            string reason = zsiMitigated
                                ? $"Upstream {upMs:0.#}ms × {margin:0.##} = {upMs * margin:0.#}ms ≤ downstream {downMs:0.#}ms at {fSample:0.00} kA " +
                                  "(ZSI active — upstream instantaneous may be suppressed)"
                                : $"Upstream {upMs:0.#}ms × {margin:0.##} = {upMs * margin:0.#}ms ≤ downstream {downMs:0.#}ms at {fSample:0.00} kA";

                            violations.Add(new CoordViolation
                            {
                                UpstreamDevice   = parent.Label ?? "(unnamed)",
                                DownstreamDevice = node.Label   ?? "(unnamed)",
                                FaultKa          = Math.Round(fSample, 3),
                                Reason           = reason,
                                IsZsiMitigated   = zsiMitigated
                            });
                            // No break — record ALL violations across the full sample range.
                        }
                    }
                }
            }

            foreach (var child in node?.Children ?? Enumerable.Empty<SLDNode>())
                CheckNode(child, node, tcc, maxFaultKaFallback, sampleCount, margin, zsiEnabled, violations);
        }
    }
}
