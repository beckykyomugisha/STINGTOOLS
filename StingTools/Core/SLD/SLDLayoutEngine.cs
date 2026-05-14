// StingTools — SLD layout engine (Phase 175)
//
// Pure layout — turns a hierarchy of SLDNodes into XYZ symbol positions
// plus busbar / branch line geometry. All coordinates output in Revit
// internal feet.
//
// SLD-04: symbol size and spacing are read from SymbolStandardRegistry
//         rather than hard-coded constants.
// SLD-05: column wrapping — when accumulated Y exceeds MaxColumnHeightMm,
//         a new column starts so deep trees don't overflow the sheet.
// SLD-06: busbar width computed from actual child symbol positions rather
//         than a formula that ignores the true column extent.
//
// Alignment fix: busbar Y = pos.Y - symHalf (parent bottom edge / output
//   connector). Branch lines now terminate at childPos.Y + symHalf (top
//   of child symbol / input connector) so lines do not pierce symbol bodies.

using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core.Symbols;

namespace StingTools.Core.SLD
{
    public sealed class SLDLayout
    {
        public Dictionary<ElementId, XYZ> SymbolPositions { get; set; }
            = new Dictionary<ElementId, XYZ>();
        public List<(XYZ from, XYZ to)> BusbarSegments { get; set; }
            = new List<(XYZ, XYZ)>();
        public List<(XYZ from, XYZ to)> BranchLines { get; set; }
            = new List<(XYZ, XYZ)>();
        // Carry pole count alongside each branch line so the annotation placer
        // can draw tick marks without re-walking the node tree.
        public List<(XYZ from, XYZ to, int poles)> BranchLinesWithPoles { get; set; }
            = new List<(XYZ, XYZ, int)>();
        public XYZ ViewOrigin { get; set; } = XYZ.Zero;
        public double TotalWidth { get; set; }
        public double TotalHeight { get; set; }
    }

    public static class SLDLayoutEngine
    {
        private const double MmPerFoot        = 304.8;
        // SLD-04 fallback values used when the standard has no settings.
        private const double DefaultSymbolSizeMm  = 8.0;
        private const double DefaultSpacingRatio  = 0.625; // spacingMm = sizeMm × ratio
        private const double DefaultLevelOffMm    = 40.0;
        // SLD-05: maximum column height before wrapping to a new column.
        private const double MaxColumnHeightMm    = 500.0;
        // Width allocated per wrapped column (must exceed levelOffMm).
        private const double ColumnWidthMm        = 50.0;
        private static double Mm(double mm) => mm / MmPerFoot;

        public static SLDLayout CalculateLayout(SLDNode root, string standardId)
        {
            var layout = new SLDLayout();
            if (root == null) return layout;

            // SLD-04: read symbol size from the active standard
            var std = SymbolStandardRegistry.GetStandard(standardId);
            double symSizeMm    = std?.SymbolSizeMm > 0 ? std.SymbolSizeMm : DefaultSymbolSizeMm;
            double spacingMm    = symSizeMm * DefaultSpacingRatio;
            double symHalf      = Mm(symSizeMm / 2.0); // half-height of symbol in feet
            double levelDx      = Mm(DefaultLevelOffMm);
            double dy           = Mm(symSizeMm + spacingMm);
            double maxColHeight = Mm(MaxColumnHeightMm);
            double colWidth     = Mm(ColumnWidthMm);

            // Per-level Y cursor and column index for wrapping (SLD-05).
            var yByLevel   = new Dictionary<int, double>();
            var colByLevel = new Dictionary<int, int>();

            void Place(SLDNode node)
            {
                if (!yByLevel.TryGetValue(node.HierarchyLevel, out var curY)) curY = 0;
                if (!colByLevel.TryGetValue(node.HierarchyLevel, out var col)) col = 0;

                // SLD-05: wrap to a new column when height limit is exceeded
                if (curY >= maxColHeight) { col++; curY = 0; colByLevel[node.HierarchyLevel] = col; }

                double x = node.HierarchyLevel * levelDx + col * colWidth;
                var pos = new XYZ(x, -curY, 0);
                layout.SymbolPositions[node.ElementId] = pos;
                yByLevel[node.HierarchyLevel] = curY + dy;

                if (node.IsPanel && node.Children.Count > 0)
                {
                    // Busbar hangs at the bottom edge of the parent symbol — power exits downward.
                    // symHalf = half the symbol height so busbar aligns exactly with the
                    // parent's output connector rather than an arbitrary offset.
                    double busY = pos.Y - symHalf;

                    // SLD-06: place children first so their actual positions
                    // are available when computing busbar extent.
                    foreach (var child in node.Children) Place(child);

                    double minChildX = double.MaxValue;
                    double maxChildX = double.MinValue;
                    foreach (var child in node.Children)
                    {
                        if (layout.SymbolPositions.TryGetValue(child.ElementId, out var cp))
                        {
                            if (cp.X < minChildX) minChildX = cp.X;
                            if (cp.X > maxChildX) maxChildX = cp.X;
                        }
                    }
                    if (minChildX == double.MaxValue) { minChildX = pos.X; maxChildX = pos.X; }

                    // Busbar spans from (just left of leftmost child) to (just right of rightmost)
                    var busFrom = new XYZ(System.Math.Min(pos.X, minChildX) - Mm(2), busY, 0);
                    var busTo   = new XYZ(System.Math.Max(pos.X, maxChildX) + Mm(2), busY, 0);
                    layout.BusbarSegments.Add((busFrom, busTo));

                    foreach (var child in node.Children)
                    {
                        if (layout.SymbolPositions.TryGetValue(child.ElementId, out var childPos))
                        {
                            // Branch from busbar down to the TOP of the child symbol — not the
                            // centre — so the line terminates at the input connector and does not
                            // pierce through the symbol body.
                            var childTop = new XYZ(childPos.X, childPos.Y + symHalf, 0);
                            layout.BranchLines.Add((new XYZ(childPos.X, busY, 0), childTop));
                            layout.BranchLinesWithPoles.Add(
                                (new XYZ(childPos.X, busY, 0), childTop, child.Poles));
                        }
                    }
                }
                else if (!node.IsPanel)
                {
                    // Leaf node — no busbar, children are already placed inline above
                }
            }

            Place(root);

            double maxX = 0, maxY = 0;
            foreach (var p in layout.SymbolPositions.Values)
            {
                if (p.X > maxX) maxX = p.X;
                if (-p.Y > maxY) maxY = -p.Y;
            }
            layout.TotalWidth  = maxX + levelDx;
            layout.TotalHeight = maxY + dy;
            return layout;
        }
    }
}
