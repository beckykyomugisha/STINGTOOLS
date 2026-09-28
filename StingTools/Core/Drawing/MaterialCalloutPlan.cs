using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>A face that could carry a material callout, in view-plane coordinates.</summary>
    public sealed class MaterialCalloutCandidate
    {
        /// <summary>The material's ElementId value (paint material when painted).</summary>
        public long Material;
        public double U, V;       // position on the view plane (feet)
        public double Area;       // face area (any unit, the same for all)
        public bool Painted;
    }

    /// <summary>
    /// Which faces get a material callout, and where a build-up's callouts stack.
    ///
    /// An elevation with a callout on every wall panel is unreadable: the same
    /// "BRK-001 Facing brick" twenty times. One callout per material within a
    /// paper distance is what a person draws. Callouts already on the view count
    /// as placed, so a re-run adds nothing where a material is already called out.
    /// </summary>
    public static class MaterialCalloutPlan
    {
        /// <summary>Default spacing between two callouts of the same material, on paper.</summary>
        public const double DefaultSpacingPaperMm = 80.0;

        /// <summary>Model distance (feet) that is <paramref name="paperMm"/> on a sheet at 1:<paramref name="scale"/>.</summary>
        public static double SpacingFeet(double paperMm, int scale)
            => Math.Max(1, scale) * paperMm / 304.8;

        /// <summary>
        /// Indices of <paramref name="candidates"/> to tag. Largest faces first (painted
        /// before unpainted); a candidate is dropped when a kept or existing callout of the
        /// same material lies within <paramref name="spacing"/>.
        /// </summary>
        public static List<int> Thin(IList<MaterialCalloutCandidate> candidates, double spacing,
            IEnumerable<MaterialCalloutCandidate> existing = null)
        {
            var kept = new List<int>();
            if (candidates == null || candidates.Count == 0) return kept;
            var placed = new List<MaterialCalloutCandidate>(existing ?? Enumerable.Empty<MaterialCalloutCandidate>());
            double s2 = spacing * spacing;
            var order = Enumerable.Range(0, candidates.Count)
                .Where(i => candidates[i] != null)
                .OrderByDescending(i => candidates[i].Painted)
                .ThenByDescending(i => candidates[i].Area)
                .ThenBy(i => i);
            foreach (int i in order)
            {
                var c = candidates[i];
                bool near = placed.Any(p => p.Material == c.Material
                                         && (p.U - c.U) * (p.U - c.U) + (p.V - c.V) * (p.V - c.V) < s2);
                if (near) continue;
                kept.Add(i);
                placed.Add(c);
            }
            kept.Sort();
            return kept;
        }

        /// <summary>
        /// Head positions for a build-up callout: one per layer, stacked in a column to the
        /// right of the element (<paramref name="columnU"/>), the first at <paramref name="topV"/>
        /// and each next <paramref name="pitch"/> lower. Layers are ordered by their position
        /// across the element (U, then V), so the column reads outside-in the way the layers
        /// sit. Returns positions in the order of <paramref name="layerCentres"/>.
        /// </summary>
        public static List<(double U, double V)> StackHeads(IList<(double U, double V)> layerCentres,
            double columnU, double topV, double pitch)
        {
            var result = new List<(double, double)>(new (double, double)[layerCentres?.Count ?? 0]);
            if (layerCentres == null) return result;
            var order = Enumerable.Range(0, layerCentres.Count)
                .OrderBy(i => layerCentres[i].U).ThenByDescending(i => layerCentres[i].V).ToList();
            for (int k = 0; k < order.Count; k++)
                result[order[k]] = (columnU, topV - k * pitch);
            return result;
        }
    }
}
