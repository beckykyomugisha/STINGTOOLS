using System;
// StingTools — SLD layout engine (Phase 175 + Phase 179 enhancements)
//
// Pure layout — turns a hierarchy of SLDNodes into XYZ symbol positions
// plus busbar / branch line geometry. All coordinates output in Revit
// internal feet.
//
// Phase 179: SLDLayoutOptions and SLDAnnotationOptions make every
// formerly-hardcoded constant configurable; multi-root support via
// SLDLayout.Offset; UI sliders in StingElectricalPanel are now wired.

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.SLD
{
    /// <summary>
    /// Layout geometry constants, all editable at generation time.
    /// </summary>
    public sealed class SLDLayoutOptions
    {
        public double SymbolHeightMm  { get; set; } = 8.0;
        public double SymbolSpacingMm { get; set; } = 5.0;
        public double BusbarOffsetMm  { get; set; } = 4.0;
        public double LevelOffsetMm   { get; set; } = 40.0;
        /// <summary>Horizontal gap between separate distribution hierarchies (multi-root).</summary>
        public double RootGapMm       { get; set; } = 80.0;

        public static SLDLayoutOptions Default => new SLDLayoutOptions();
    }

    /// <summary>
    /// Controls which engineering data columns appear on SLD annotations.
    /// Values here override what the standard's AnnotationRules declare.
    /// </summary>
    public sealed class SLDAnnotationOptions
    {
        public bool ShowRatings  { get; set; } = true;
        public bool ShowLoads    { get; set; } = true;
        public bool ShowVdPct    { get; set; }
        public bool ShowFaultKa  { get; set; }
        public bool ShowCsaMm2   { get; set; }

        public static SLDAnnotationOptions Default => new SLDAnnotationOptions();
    }

    public sealed class SLDLayout
    {
        public Dictionary<ElementId, XYZ> SymbolPositions { get; set; }
            = new Dictionary<ElementId, XYZ>();
        public List<(XYZ from, XYZ to)> BusbarSegments { get; set; }
            = new List<(XYZ, XYZ)>();
        public List<(XYZ from, XYZ to)> BranchLines { get; set; }
            = new List<(XYZ, XYZ)>();
        // Carries pole count per branch so SLDAnnotationPlacer can draw tick marks.
        public List<(XYZ from, XYZ to, int poles)> BranchLinesWithPoles { get; set; }
            = new List<(XYZ, XYZ, int)>();
        public XYZ ViewOrigin { get; set; } = XYZ.Zero;
        public double TotalWidth  { get; set; }
        public double TotalHeight { get; set; }

        /// <summary>
        /// Returns a new layout with all positions and segment endpoints
        /// shifted by (dx, dy, 0). Used to place multiple root hierarchies
        /// side-by-side in a single SLD view without coordinate collisions.
        /// </summary>
        public SLDLayout Offset(double dx, double dy)
        {
            var out_ = new SLDLayout
            {
                TotalWidth  = TotalWidth,
                TotalHeight = TotalHeight,
                ViewOrigin  = new XYZ(ViewOrigin.X + dx, ViewOrigin.Y + dy, 0),
            };
            foreach (var kv in SymbolPositions)
                out_.SymbolPositions[kv.Key] = new XYZ(kv.Value.X + dx, kv.Value.Y + dy, 0);
            foreach (var s in BusbarSegments)
                out_.BusbarSegments.Add((new XYZ(s.from.X + dx, s.from.Y + dy, 0),
                                         new XYZ(s.to.X   + dx, s.to.Y   + dy, 0)));
            foreach (var s in BranchLines)
                out_.BranchLines.Add((new XYZ(s.from.X + dx, s.from.Y + dy, 0),
                                      new XYZ(s.to.X   + dx, s.to.Y   + dy, 0)));
            return out_;
        }
    }

    public static class SLDLayoutEngine
    {
        private const double MmPerFoot = 304.8;
        private static double Mm(double mm) => mm / MmPerFoot;

        public static SLDLayout CalculateLayout(SLDNode root, string standardId,
            SLDLayoutOptions opts = null)
        {
            opts = opts ?? SLDLayoutOptions.Default;
            var layout = new SLDLayout();
            if (root == null) return layout;

            double dy      = Mm(opts.SymbolHeightMm + opts.SymbolSpacingMm);
            double symHalf = Mm(opts.SymbolHeightMm / 2.0); // half-height for connector alignment
            double busOff  = Mm(opts.BusbarOffsetMm);       // vertical offset from panel symbol to busbar
            double levelDx = Mm(opts.LevelOffsetMm);

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
                    double busY = pos.Y - symHalf; // busOff replaced with symHalf (symbol half-height)
                    var busFrom = new XYZ(pos.X - Mm(10), busY, 0);
                    var busTo   = new XYZ(pos.X + Mm(10) + node.Children.Count * Mm(opts.SymbolSpacingMm), busY, 0);
                    layout.BusbarSegments.Add((busFrom, busTo));

                    foreach (var child in node.Children)
                    {
                        Place(child);
                        if (layout.SymbolPositions.TryGetValue(child.ElementId, out var childPos))
                            layout.BranchLines.Add((new XYZ(childPos.X, busY, 0), childPos));
                    }
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
