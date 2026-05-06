// StingTools — SLD layout engine (Phase 175)
//
// Pure layout — turns a hierarchy of SLDNodes into XYZ symbol positions
// plus busbar / branch line geometry. All coordinates output in Revit
// internal feet.

using System.Collections.Generic;
using Autodesk.Revit.DB;

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
        public XYZ ViewOrigin { get; set; } = XYZ.Zero;
        public double TotalWidth { get; set; }
        public double TotalHeight { get; set; }
    }

    public static class SLDLayoutEngine
    {
        private const double MmPerFoot = 304.8;
        private const double SymbolHeightMm  = 8.0;
        private const double SymbolSpacingMm = 5.0;
        private const double BusbarOffsetMm  = 4.0;
        private const double LevelOffsetMm   = 40.0;
        private static double Mm(double mm) => mm / MmPerFoot;

        public static SLDLayout CalculateLayout(SLDNode root, string standardId)
        {
            var layout = new SLDLayout();
            if (root == null) return layout;

            double dy = Mm(SymbolHeightMm + SymbolSpacingMm);
            double busOff = Mm(BusbarOffsetMm);
            double levelDx = Mm(LevelOffsetMm);

            // Per-level Y cursor.
            var yByLevel = new Dictionary<int, double>();

            void Place(SLDNode node)
            {
                if (!yByLevel.TryGetValue(node.HierarchyLevel, out var curY)) curY = 0;
                double x = node.HierarchyLevel * levelDx;
                var pos = new XYZ(x, -curY, 0);
                layout.SymbolPositions[node.ElementId] = pos;
                yByLevel[node.HierarchyLevel] = curY + dy;

                if (node.IsPanel && node.Children.Count > 0)
                {
                    double busY = pos.Y - busOff;
                    var busFrom = new XYZ(pos.X - Mm(10), busY, 0);
                    var busTo   = new XYZ(pos.X + Mm(10) + node.Children.Count * Mm(SymbolSpacingMm), busY, 0);
                    layout.BusbarSegments.Add((busFrom, busTo));

                    foreach (var child in node.Children)
                    {
                        Place(child);
                        if (layout.SymbolPositions.TryGetValue(child.ElementId, out var childPos))
                        {
                            layout.BranchLines.Add((new XYZ(childPos.X, busY, 0), childPos));
                        }
                    }
                }
            }

            Place(root);

            // View bounds (rough): max X + 2 levelDx, max accumulated Y.
            double maxX = 0, maxY = 0;
            foreach (var p in layout.SymbolPositions.Values)
            {
                if (p.X > maxX) maxX = p.X;
                if (-p.Y > maxY) maxY = -p.Y;
            }
            layout.TotalWidth = maxX + levelDx;
            layout.TotalHeight = maxY + dy;
            return layout;
        }
    }
}
