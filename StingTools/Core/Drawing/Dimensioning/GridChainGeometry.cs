// StingTools — grid chain geometry (DTW-86). Revit-free.
//
// A grid chain dimensions the spacing of one set of PARALLEL grids, so it
// needs (a) the grids grouped by direction, (b) each grid's position measured
// ACROSS the set — along the set's normal — and (c) a dimension line running
// along that normal, clear of every grid end.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.Drawing.Dimensioning
{
    /// <summary>A straight grid in plan: its two end points (feet), plus the caller's index.</summary>
    public readonly struct GridSeg
    {
        public GridSeg(int index, double x0, double y0, double x1, double y1)
        { Index = index; X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public int Index { get; }
        public double X0 { get; }
        public double Y0 { get; }
        public double X1 { get; }
        public double Y1 { get; }
    }

    /// <summary>One chain: a set of parallel grids and the line that dimensions them.</summary>
    public sealed class GridChainPlan
    {
        /// <summary>"east-west", "north-south", or "angle-&lt;deg&gt;" — also the provenance key part.</summary>
        public string Label { get; set; }
        public double DirX { get; set; }
        public double DirY { get; set; }
        public double NormX { get; set; }
        public double NormY { get; set; }
        /// <summary>Indices of the grids the chain references, in position order, coincident ones dropped.</summary>
        public List<int> Members { get; } = new List<int>();
        /// <summary>Position of each member along the normal (feet), parallel to <see cref="Members"/>.</summary>
        public List<double> Positions { get; } = new List<double>();
        /// <summary>Grids left out because another grid already sits at their position.</summary>
        public int CoincidentDropped { get; set; }
        public bool Placeable { get; set; }
        /// <summary>Why not placeable, or null.</summary>
        public string Reason { get; set; }
        public double LineX0 { get; set; }
        public double LineY0 { get; set; }
        public double LineX1 { get; set; }
        public double LineY1 { get; set; }
    }

    public static class GridChainGeometry
    {
        public const double DefaultAngleTolDeg = 0.5;
        public const double DefaultCoincidentTolFt = 1.0 / 304.8;   // 1 mm

        /// <summary>
        /// Group <paramref name="grids"/> by direction and plan one chain per group.
        /// Position = dot(grid point, set normal); the line runs along the normal,
        /// <paramref name="marginFt"/> beyond the far end of every grid and
        /// <paramref name="marginFt"/> past the outermost grid on each side.
        /// </summary>
        public static List<GridChainPlan> Plan(IReadOnlyList<GridSeg> grids, double marginFt,
            double angleTolDeg = DefaultAngleTolDeg, double coincidentTolFt = DefaultCoincidentTolFt)
        {
            var plans = new List<GridChainPlan>();
            if (grids == null || grids.Count == 0) return plans;

            double sinTol = Math.Sin(angleTolDeg * Math.PI / 180.0);
            var groups = new List<(double Dx, double Dy, List<GridSeg> Members)>();
            foreach (var g in grids)
            {
                if (!TryDirection(g, out double dx, out double dy)) continue;
                int found = -1;
                for (int i = 0; i < groups.Count; i++)
                    if (Math.Abs(dx * groups[i].Dy - dy * groups[i].Dx) <= sinTol) { found = i; break; }
                if (found < 0) groups.Add((dx, dy, new List<GridSeg> { g }));
                else groups[found].Members.Add(g);
            }

            foreach (var grp in groups)
            {
                double dx = grp.Dx, dy = grp.Dy;
                double nx = -dy, ny = dx;   // in-plane normal: the axis the set is spaced along
                var plan = new GridChainPlan
                {
                    Label = LabelFor(dx, dy, sinTol),
                    DirX = dx, DirY = dy, NormX = nx, NormY = ny,
                };

                // The line sits beyond the far end of EVERY grid (all directions), so a
                // chain never crosses another set's bubbles.
                double along = double.MinValue;
                foreach (var g in grids)
                {
                    along = Math.Max(along, g.X0 * dx + g.Y0 * dy);
                    along = Math.Max(along, g.X1 * dx + g.Y1 * dy);
                }
                along += marginFt;

                var ordered = grp.Members
                    .Select(g => (g.Index, Pos: g.X0 * nx + g.Y0 * ny))
                    .OrderBy(t => t.Pos)
                    .ToList();
                foreach (var t in ordered)
                {
                    if (plan.Positions.Count > 0 && t.Pos - plan.Positions[plan.Positions.Count - 1] <= coincidentTolFt)
                    { plan.CoincidentDropped++; continue; }
                    plan.Members.Add(t.Index);
                    plan.Positions.Add(t.Pos);
                }

                if (ordered.Count < 2) plan.Reason = "only one grid in this direction";
                else if (plan.Members.Count < 2) plan.Reason = "grids are coincident";
                else
                {
                    double lo = plan.Positions[0] - marginFt, hi = plan.Positions[plan.Positions.Count - 1] + marginFt;
                    plan.LineX0 = nx * lo + dx * along; plan.LineY0 = ny * lo + dy * along;
                    plan.LineX1 = nx * hi + dx * along; plan.LineY1 = ny * hi + dy * along;
                    plan.Placeable = true;
                }
                plans.Add(plan);
            }
            return plans;
        }

        /// <summary>
        /// Unit direction, made canonical so a grid drawn either way joins the same
        /// set and the chain lands on the same side: the dominant component positive
        /// (east for a predominantly east-west grid, north otherwise).
        /// </summary>
        private static bool TryDirection(GridSeg g, out double dx, out double dy)
        {
            dx = g.X1 - g.X0; dy = g.Y1 - g.Y0;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return false;
            dx /= len; dy /= len;
            bool flip = Math.Abs(dx) >= Math.Abs(dy) ? dx < 0 : dy < 0;
            if (flip) { dx = -dx; dy = -dy; }
            return true;
        }

        private static string LabelFor(double dx, double dy, double sinTol)
        {
            if (Math.Abs(dy) <= sinTol) return "east-west";
            if (Math.Abs(dx) <= sinTol) return "north-south";
            double deg = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            return "angle-" + deg.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
