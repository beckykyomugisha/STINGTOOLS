// DWG-MULTI-01: Multi-layer wall detection.
//
// The Phase 142 detector finds parallel-pair walls (two adjacent lines forming a single
// solid wall) but does not recognise dual-leaf encoding (exterior + interior leaf,
// cavity walls, drylined block, double-skin SFS). This detector consumes the parallel
// pair output and groups successive pairs that share a centreline corridor < 600 mm
// apart into a single composite "multi-layer" wall description.
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Cad
{
    public sealed record DetectedLeaf(double CentreX1, double CentreY1, double CentreX2, double CentreY2, double ThicknessMm);

    public sealed record WallAssembly(
        List<DetectedLeaf> Leaves,
        double TotalThicknessMm,
        string TypeHint);

    public static class MultiLayerWallDetector
    {
        public static List<WallAssembly> Group(
            IReadOnlyList<DetectedLeaf> singleLeaves,
            double maxCorridorMm = 600.0,
            double parallelAngleTolDeg = 5.0)
        {
            var result = new List<WallAssembly>();
            var consumed = new bool[singleLeaves.Count];

            for (int i = 0; i < singleLeaves.Count; i++)
            {
                if (consumed[i]) continue;
                var anchor = singleLeaves[i];
                var group = new List<DetectedLeaf> { anchor };
                consumed[i] = true;
                for (int j = i + 1; j < singleLeaves.Count; j++)
                {
                    if (consumed[j]) continue;
                    if (!AreParallel(anchor, singleLeaves[j], parallelAngleTolDeg)) continue;
                    var gapMm = PerpendicularGapMm(anchor, singleLeaves[j]);
                    if (gapMm <= maxCorridorMm)
                    {
                        group.Add(singleLeaves[j]);
                        consumed[j] = true;
                    }
                }

                double total = group.Sum(l => l.ThicknessMm);
                if (group.Count > 1)
                {
                    var ordered = group.OrderBy(l => l.CentreX1 + l.CentreY1).ToList();
                    for (int k = 0; k < ordered.Count - 1; k++)
                        total += System.Math.Max(0,
                            PerpendicularGapMm(ordered[k], ordered[k + 1])
                            - 0.5 * (ordered[k].ThicknessMm + ordered[k + 1].ThicknessMm));
                    result.Add(new WallAssembly(ordered, total, BuildHint(ordered)));
                }
                else
                {
                    result.Add(new WallAssembly(group, total, $"Solid-{(int)anchor.ThicknessMm}"));
                }
            }
            return result;
        }

        private static bool AreParallel(DetectedLeaf a, DetectedLeaf b, double angleTolDeg)
        {
            double angA = System.Math.Atan2(a.CentreY2 - a.CentreY1, a.CentreX2 - a.CentreX1);
            double angB = System.Math.Atan2(b.CentreY2 - b.CentreY1, b.CentreX2 - b.CentreX1);
            double diff = System.Math.Abs((angA - angB) * 180.0 / System.Math.PI) % 180.0;
            return diff < angleTolDeg || diff > 180.0 - angleTolDeg;
        }

        private static double PerpendicularGapMm(DetectedLeaf a, DetectedLeaf b)
        {
            double midAx = 0.5 * (a.CentreX1 + a.CentreX2);
            double midAy = 0.5 * (a.CentreY1 + a.CentreY2);
            double midBx = 0.5 * (b.CentreX1 + b.CentreX2);
            double midBy = 0.5 * (b.CentreY1 + b.CentreY2);
            double dx = midAx - midBx;
            double dy = midAy - midBy;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        private static string BuildHint(List<DetectedLeaf> leaves)
        {
            var parts = new List<string>();
            for (int i = 0; i < leaves.Count; i++)
            {
                parts.Add(((int)leaves[i].ThicknessMm).ToString());
                if (i < leaves.Count - 1)
                {
                    var gap = (int)System.Math.Max(0,
                        PerpendicularGapMm(leaves[i], leaves[i + 1])
                        - 0.5 * (leaves[i].ThicknessMm + leaves[i + 1].ThicknessMm));
                    if (gap > 0) parts.Add(gap.ToString());
                }
            }
            return leaves.Count switch
            {
                2 => $"Cavity-{string.Join("-", parts)}",
                3 => $"Triple-{string.Join("-", parts)}",
                _ => $"Composite-{string.Join("-", parts)}",
            };
        }
    }
}
