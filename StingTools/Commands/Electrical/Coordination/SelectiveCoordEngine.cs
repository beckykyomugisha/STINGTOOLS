using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.SLD;

namespace StingTools.Commands.Electrical.Coordination
{
    public class CoordViolation
    {
        public string UpstreamDevice { get; set; } = "";
        public string DownstreamDevice { get; set; } = "";
        public double FaultKa { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// Selective-coordination checker. Walks the SLD hierarchy and asserts
    /// that, at every fault level the downstream device might see, the
    /// upstream device clears at LEAST as slowly. A violation is recorded
    /// when the upstream clearing time falls at or below the downstream
    /// clearing time anywhere in the sample range.
    /// </summary>
    public static class SelectiveCoordEngine
    {
        public static List<CoordViolation> Check(SLDNode root, TccDatabase tcc,
            double maxFaultKaFallback = 10.0, int sampleCount = 10)
        {
            var violations = new List<CoordViolation>();
            if (root == null || tcc == null) return violations;
            CheckNode(root, null, tcc, maxFaultKaFallback, sampleCount, violations);
            return violations;
        }

        private static void CheckNode(SLDNode node, SLDNode parent, TccDatabase tcc,
            double maxFaultKaFallback, int sampleCount, List<CoordViolation> violations)
        {
            if (parent != null && node != null)
            {
                var upDev = tcc.Resolve(parent.Rating);
                var downDev = tcc.Resolve(node.Rating);
                if (upDev != null && downDev != null)
                {
                    // Gap 14: resolve full curves for log-log interpolation
                    var upCurve   = tcc.ResolveCurve(parent.Rating);
                    var downCurve = tcc.ResolveCurve(node.Rating);

                    // Use the more constraining (lower) device rating as the upper sample
                    // bound — checking beyond a device's rated fault current is meaningless.
                    // Only fall back to maxFaultKaFallback when device ratings are absent.
                    double deviceLimit = Math.Min(upDev.MaxFaultKa, downDev.MaxFaultKa);
                    double maxFaultKa  = deviceLimit > 0 ? deviceLimit : maxFaultKaFallback;

                    double step = maxFaultKa / Math.Max(1, sampleCount);
                    for (double f = step; f <= maxFaultKa; f += step)
                    {
                        double upMs = upDev.ClearingTimeMs(f, upCurve);
                        double downMs = downDev.ClearingTimeMs(f, downCurve);
                        if (upMs > 0 && upMs <= downMs)
                        {
                            violations.Add(new CoordViolation
                            {
                                UpstreamDevice = parent.Label ?? "(unnamed)",
                                DownstreamDevice = node.Label ?? "(unnamed)",
                                FaultKa = Math.Round(f, 2),
                                Reason = $"Upstream {upMs:0}ms ≤ downstream {downMs:0}ms at {f:0.00} kA"
                            });
                            break;  // record first violation per pair
                        }
                    }
                }
            }
            foreach (var child in node?.Children ?? Enumerable.Empty<SLDNode>())
                CheckNode(child, node, tcc, maxFaultKaFallback, sampleCount, violations);
        }
    }
}
