using System;
// DWG-CURVE-01: Curved wall support from DWG arc segments.
//
// The Phase 142 wall pipeline rejects arc segments — only straight lines convert.
// This detector consumes a list of arc primitives (centre, radius, start/end angles)
// extracted by the existing GeometryObject walker and emits CurvedWall records
// that the Revit wall creator can pass to Wall.Create(Document, Curve, ...) using
// an Arc primitive instead of a Line.
//
// Pair detection mirrors the straight-wall logic: two arcs that share the same
// centre and have radii ~equal to wall_thickness/2 either side of a mean radius
// are treated as the two faces of one curved wall; mean radius becomes the
// centreline.
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Cad
{
    public sealed record DetectedArc(
        double CentreX, double CentreY,
        double Radius,
        double StartAngleRad, double EndAngleRad);

    public sealed record CurvedWall(
        double CentreX, double CentreY,
        double CentrelineRadius,
        double StartAngleRad, double EndAngleRad,
        double ThicknessMm);

    public static class CurvedWallDetector
    {
        public static List<CurvedWall> Pair(
            IReadOnlyList<DetectedArc> arcs,
            double centreToleranceMm = 50.0,
            double angleToleranceRad = 0.05,
            double maxWallThicknessMm = 600.0)
        {
            var result = new List<CurvedWall>();
            var consumed = new bool[arcs.Count];

            for (int i = 0; i < arcs.Count; i++)
            {
                if (consumed[i]) continue;
                var a = arcs[i];
                int bestJ = -1;
                double bestDr = double.MaxValue;

                for (int j = i + 1; j < arcs.Count; j++)
                {
                    if (consumed[j]) continue;
                    var b = arcs[j];
                    if (System.Math.Abs(a.CentreX - b.CentreX) > centreToleranceMm) continue;
                    if (System.Math.Abs(a.CentreY - b.CentreY) > centreToleranceMm) continue;
                    if (System.Math.Abs(a.StartAngleRad - b.StartAngleRad) > angleToleranceRad) continue;
                    if (System.Math.Abs(a.EndAngleRad - b.EndAngleRad) > angleToleranceRad) continue;
                    double dr = System.Math.Abs(a.Radius - b.Radius);
                    if (dr <= maxWallThicknessMm && dr < bestDr)
                    {
                        bestDr = dr;
                        bestJ = j;
                    }
                }

                if (bestJ >= 0)
                {
                    var b = arcs[bestJ];
                    consumed[i] = consumed[bestJ] = true;
                    double meanR = 0.5 * (a.Radius + b.Radius);
                    double thicknessMm = System.Math.Abs(a.Radius - b.Radius);
                    double cx = 0.5 * (a.CentreX + b.CentreX);
                    double cy = 0.5 * (a.CentreY + b.CentreY);
                    result.Add(new CurvedWall(cx, cy, meanR,
                        a.StartAngleRad, a.EndAngleRad, thicknessMm));
                }
                else
                {
                    // Singleton arc — treat as centreline with default 200 mm thickness.
                    consumed[i] = true;
                    result.Add(new CurvedWall(a.CentreX, a.CentreY, a.Radius,
                        a.StartAngleRad, a.EndAngleRad, 200.0));
                }
            }
            return result;
        }

        public static IEnumerable<(double X, double Y)> Sample(CurvedWall wall, int steps = 32)
        {
            for (int k = 0; k <= steps; k++)
            {
                double t = (double)k / steps;
                double ang = wall.StartAngleRad + t * (wall.EndAngleRad - wall.StartAngleRad);
                yield return (
                    wall.CentreX + wall.CentrelineRadius * System.Math.Cos(ang),
                    wall.CentreY + wall.CentrelineRadius * System.Math.Sin(ang));
            }
        }
    }
}
