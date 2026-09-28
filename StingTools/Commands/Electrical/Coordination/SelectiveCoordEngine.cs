using System;
using System.Collections.Generic;
using System.Globalization;
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
        /// <summary>Verdict for the pair: NotAssured, NotSelective or NoCurveData.</summary>
        public SelectivityVerdict Verdict { get; set; }
        /// <summary>Retained for compatibility. Always false: ZSI needs manufacturer data
        /// for MCCB / ACB, which this engine does not have.</summary>
        public bool IsZsiMitigated { get; set; }
    }

    /// <summary>One assessed upstream / downstream pair.</summary>
    public sealed class CoordPairResult
    {
        public SLDNode Upstream   { get; set; }
        public SLDNode Downstream { get; set; }
        public DeviceBand UpstreamBand   { get; set; }
        public DeviceBand DownstreamBand { get; set; }
        public SelectivityResult Result  { get; set; }
        /// <summary>Where the prospective fault current came from (node value or assumption).</summary>
        public string FaultSource { get; set; } = "";
    }

    /// <summary>
    /// Selective-coordination checker. Walks the SLD hierarchy and, for every
    /// parent/child pair of protective devices, runs the IEC 60898-1 band check in
    /// <see cref="IecMcbBands.Check"/> up to the prospective fault current.
    ///
    /// A pair is only ever reported Selective when the generic bands PROVE it. MCCB /
    /// ACB (no generic band) → NoCurveData. Fault currents reaching the upstream
    /// instantaneous band → NotAssured ("manufacturer selectivity table required").
    /// Every result carries <see cref="IecMcbBands.Basis"/>.
    ///
    /// Replaces the previous check, which compared single synthetic linear ramps
    /// (ROADMAP ELEC-4) and so could "pass" pairs no real data supported.
    /// </summary>
    public static class SelectiveCoordEngine
    {
        /// <summary>Assess every parent/child pair whose ratings are known.</summary>
        public static List<CoordPairResult> Evaluate(SLDNode root, TccDatabase tcc)
        {
            var results = new List<CoordPairResult>();
            if (root == null || tcc == null) return results;
            Walk(root, null, tcc, results);
            return results;
        }

        /// <summary>
        /// Every pair that is NOT proven selective (NotAssured, NotSelective, NoCurveData).
        /// </summary>
        public static List<CoordViolation> Check(SLDNode root, TccDatabase tcc)
            => ToViolations(Evaluate(root, tcc));

        public static List<CoordViolation> ToViolations(IEnumerable<CoordPairResult> pairs)
            => (pairs ?? Enumerable.Empty<CoordPairResult>())
                .Where(p => p.Result.Verdict != SelectivityVerdict.Selective)
                .Select(p => new CoordViolation
                {
                    UpstreamDevice   = p.Upstream?.Label ?? "(unnamed)",
                    DownstreamDevice = p.Downstream?.Label ?? "(unnamed)",
                    FaultKa          = Math.Round(p.Result.ProspectiveFaultKa, 3),
                    Verdict          = p.Result.Verdict,
                    Reason           = $"{p.Result.Verdict}: {p.Result.Reason} [{p.FaultSource}] — {IecMcbBands.Basis}"
                })
                .ToList();

        private static void Walk(SLDNode node, SLDNode parent, TccDatabase tcc, List<CoordPairResult> results)
        {
            if (node == null) return;
            if (parent != null && !string.IsNullOrWhiteSpace(parent.Rating) && !string.IsNullOrWhiteSpace(node.Rating))
            {
                var up = tcc.ResolveBand(parent.Rating);
                var dn = tcc.ResolveBand(node.Rating);
                double psc = ProspectiveFaultKa(node, parent, tcc, out string src);
                results.Add(new CoordPairResult
                {
                    Upstream = parent, Downstream = node,
                    UpstreamBand = up, DownstreamBand = dn,
                    Result = IecMcbBands.Check(up, dn, psc),
                    FaultSource = src
                });
            }
            foreach (var child in node.Children ?? Enumerable.Empty<SLDNode>())
                Walk(child, node, tcc, results);
        }

        /// <summary>
        /// Prospective fault at the downstream device: the node's own stamped fault level,
        /// else its parent's (a higher, conservative value), else the downstream device's
        /// rated breaking capacity from the database (the highest current it can be asked
        /// to see). 0 when none is known.
        /// </summary>
        private static double ProspectiveFaultKa(SLDNode node, SLDNode parent, TccDatabase tcc, out string source)
        {
            double v = ParseKa(node.FaultKa);
            if (v > 0) { source = "fault level at downstream node"; return v; }
            v = ParseKa(parent?.FaultKa);
            if (v > 0) { source = "fault level at upstream node (conservative)"; return v; }
            var e = tcc.Resolve(node.Rating?.Trim());
            if (e != null && e.MaxFaultKa > 0) { source = "no fault level — downstream breaking capacity assumed"; return e.MaxFaultKa; }
            source = "no fault level";
            return 0;
        }

        private static double ParseKa(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            string digits = new string(s.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }
}
