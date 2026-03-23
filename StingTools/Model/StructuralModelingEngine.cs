// ============================================================================
// StructuralModelingEngine.cs — Advanced Structural Modeling Automation Engine
// Provides algorithmic structural element creation from CAD data with:
//   - Foundation detection (strip/pad/pile/raft) from geometry analysis
//   - Structural wall inference from thickness + layer patterns
//   - Slab/deck creation from closed loop detection with opening subtraction
//   - Beam system generation with tributary area load distribution
//   - Column grid optimization with structural bay analysis
//   - Bracing pattern generation (X, V, K, chevron)
//   - Truss generation from span/depth/type parameters
//   - Rebar zone inference from member geometry
//   - Load path analysis and member connectivity graph
//   - CAD structural layer classification with 40+ pattern rules
//
// Architecture:
//   StructuralModelingEngine (orchestrator)
//     ├── StructuralLayerClassifier  — 40+ DWG layer patterns for structural detection
//     ├── FoundationAnalyzer         — geometry → foundation type inference
//     ├── BeamSystemGenerator        — tributary area + grid-based beam placement
//     ├── BracingPatternEngine       — lateral system generation
//     ├── TrussGenerator             — parametric truss geometry
//     ├── SlabAnalyzer               — opening detection + edge beam inference
//     ├── LoadPathAnalyzer           — connectivity graph + load tracing
//     └── StructuralGridOptimizer    — bay analysis + column placement
//
// All dimensions in millimeters externally, converted to Revit feet internally.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;

namespace StingTools.Model
{
    #region Structural Data Types

    /// <summary>Structural element classification from CAD analysis.</summary>
    public enum StructuralElementType
    {
        Column, Beam, Brace, Wall, Slab, Footing, PadFooting, PileFoundation,
        RaftFoundation, StripFooting, RetainingWall, ShearWall, CoreWall,
        Truss, BeamSystem, Purlin, Lintel, TransferBeam, GroundBeam,
        PlinthBeam, TieBeam, Staircase, Ramp, Void, Opening
    }

    /// <summary>Bracing pattern types for lateral stability systems.</summary>
    public enum BracingPattern
    {
        XBrace, VBrace, InvertedV, KBrace, SingleDiagonal,
        Chevron, ZigZag, Portal
    }

    /// <summary>Truss type for parametric generation.</summary>
    public enum TrussType
    {
        Pratt, Warren, Howe, Fan, Fink, KTruss, Vierendeel, BowString
    }

    /// <summary>Foundation type inference result.</summary>
    public enum FoundationType
    {
        Isolated, Strip, Raft, Pile, Combined, Stepped, Grillage
    }

    /// <summary>Result from structural CAD analysis — detected structural member.</summary>
    public class DetectedStructuralMember
    {
        public StructuralElementType ElementType { get; set; }
        public XYZ StartPoint { get; set; }
        public XYZ EndPoint { get; set; }
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double ThicknessMm { get; set; }
        public double LengthMm { get; set; }
        public string LayerName { get; set; }
        public List<XYZ> BoundaryPoints { get; set; } = new();
        public double Rotation { get; set; }
        public double Confidence { get; set; } = 1.0;
        public string InferredSize { get; set; }

        public double LengthFt => (EndPoint != null && StartPoint != null)
            ? StartPoint.DistanceTo(EndPoint) : LengthMm * Units.MmToFeet;
    }

    /// <summary>Structural bay detected from column grid analysis.</summary>
    public class StructuralBay
    {
        public XYZ Corner1 { get; set; }
        public XYZ Corner2 { get; set; }
        public XYZ Corner3 { get; set; }
        public XYZ Corner4 { get; set; }
        public double SpanXFt { get; set; }
        public double SpanYFt { get; set; }
        public double AreaSqFt { get; set; }
        public List<ElementId> ColumnIds { get; set; } = new();
        public bool NeedsSecondaryBeams { get; set; }
        public int RecommendedSecondaryCount { get; set; }
    }

    /// <summary>Load path node in structural connectivity graph.</summary>
    public class LoadPathNode
    {
        public ElementId ElementId { get; set; }
        public XYZ Location { get; set; }
        public StructuralElementType NodeType { get; set; }
        public List<LoadPathNode> ConnectedTo { get; set; } = new();
        public double TributaryAreaSqM { get; set; }
        public double EstimatedLoadKN { get; set; }
    }

    /// <summary>Full result from structural modeling operation.</summary>
    public class StructuralModelResult
    {
        public bool Success { get; set; }
        public string Summary { get; set; }
        public List<ElementId> CreatedIds { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public int ColumnsCreated { get; set; }
        public int BeamsCreated { get; set; }
        public int BracesCreated { get; set; }
        public int SlabsCreated { get; set; }
        public int FootingsCreated { get; set; }
        public int WallsCreated { get; set; }
        public int TrussesCreated { get; set; }
        public TimeSpan Duration { get; set; }

        public int TotalCreated => ColumnsCreated + BeamsCreated + BracesCreated +
            SlabsCreated + FootingsCreated + WallsCreated + TrussesCreated;

        public static StructuralModelResult Fail(string msg) =>
            new() { Success = false, Summary = msg };
    }

    #endregion


    #region Structural Layer Classifier

    /// <summary>
    /// Classifies DWG layer names into structural element types using 40+ pattern rules.
    /// Supports AIA, BS 1192, ISO 13567, NCS, and common CAD standards.
    /// Priority-ordered: most specific patterns first to avoid false positives.
    /// </summary>
    internal static class StructuralLayerClassifier
    {
        private static readonly (string Pattern, StructuralElementType Type, double Confidence)[] Rules =
        {
            // ── Foundations (highest priority) ──
            ("pile", StructuralElementType.PileFoundation, 0.95),
            ("piling", StructuralElementType.PileFoundation, 0.95),
            ("raft", StructuralElementType.RaftFoundation, 0.95),
            ("raft_found", StructuralElementType.RaftFoundation, 0.98),
            ("strip_found", StructuralElementType.StripFooting, 0.95),
            ("strip-fdn", StructuralElementType.StripFooting, 0.95),
            ("pad_found", StructuralElementType.PadFooting, 0.95),
            ("pad-fdn", StructuralElementType.PadFooting, 0.95),
            ("footing", StructuralElementType.Footing, 0.90),
            ("ftg", StructuralElementType.Footing, 0.85),
            ("fdn", StructuralElementType.Footing, 0.85),
            ("found", StructuralElementType.Footing, 0.80),
            ("fundament", StructuralElementType.Footing, 0.85), // German

            // ── Structural walls ──
            ("shear_wall", StructuralElementType.ShearWall, 0.95),
            ("shearwall", StructuralElementType.ShearWall, 0.95),
            ("core_wall", StructuralElementType.CoreWall, 0.95),
            ("corewall", StructuralElementType.CoreWall, 0.95),
            ("retaining", StructuralElementType.RetainingWall, 0.92),
            ("ret_wall", StructuralElementType.RetainingWall, 0.92),
            ("rc_wall", StructuralElementType.ShearWall, 0.90),
            ("conc_wall", StructuralElementType.ShearWall, 0.88),
            ("str_wall", StructuralElementType.Wall, 0.90),
            ("s-wall", StructuralElementType.Wall, 0.85),

            // ── Columns ──
            ("str_col", StructuralElementType.Column, 0.95),
            ("s-col", StructuralElementType.Column, 0.90),
            ("rc_col", StructuralElementType.Column, 0.92),
            ("steel_col", StructuralElementType.Column, 0.92),
            ("stl_col", StructuralElementType.Column, 0.90),
            ("column", StructuralElementType.Column, 0.85),
            ("col-", StructuralElementType.Column, 0.80),
            ("stutze", StructuralElementType.Column, 0.85), // German
            ("pilier", StructuralElementType.Column, 0.85), // French

            // ── Beams ──
            ("transfer", StructuralElementType.TransferBeam, 0.90),
            ("ground_beam", StructuralElementType.GroundBeam, 0.92),
            ("grnd_beam", StructuralElementType.GroundBeam, 0.90),
            ("plinth", StructuralElementType.PlinthBeam, 0.90),
            ("tie_beam", StructuralElementType.TieBeam, 0.90),
            ("lintel", StructuralElementType.Lintel, 0.92),
            ("purlin", StructuralElementType.Purlin, 0.90),
            ("str_beam", StructuralElementType.Beam, 0.92),
            ("s-beam", StructuralElementType.Beam, 0.88),
            ("rc_beam", StructuralElementType.Beam, 0.90),
            ("steel_beam", StructuralElementType.Beam, 0.90),
            ("stl_beam", StructuralElementType.Beam, 0.90),
            ("beam", StructuralElementType.Beam, 0.82),
            ("trager", StructuralElementType.Beam, 0.82), // German
            ("poutre", StructuralElementType.Beam, 0.82), // French

            // ── Bracing ──
            ("brace", StructuralElementType.Brace, 0.90),
            ("bracing", StructuralElementType.Brace, 0.90),
            ("x-brace", StructuralElementType.Brace, 0.95),
            ("diag", StructuralElementType.Brace, 0.75),

            // ── Trusses ──
            ("truss", StructuralElementType.Truss, 0.92),
            ("lattice", StructuralElementType.Truss, 0.85),

            // ── Slabs ──
            ("str_slab", StructuralElementType.Slab, 0.92),
            ("rc_slab", StructuralElementType.Slab, 0.90),
            ("slab", StructuralElementType.Slab, 0.82),
            ("deck", StructuralElementType.Slab, 0.78),
            ("soffit", StructuralElementType.Slab, 0.75),
            ("dalle", StructuralElementType.Slab, 0.82), // French

            // ── Openings / Voids ──
            ("opening", StructuralElementType.Opening, 0.85),
            ("void", StructuralElementType.Void, 0.80),
            ("penetration", StructuralElementType.Opening, 0.78),
            ("hole", StructuralElementType.Opening, 0.72),

            // ── Stairs ──
            ("stair", StructuralElementType.Staircase, 0.85),
            ("ramp", StructuralElementType.Ramp, 0.85),
        };

        /// <summary>
        /// Classifies a DWG layer name into a structural element type.
        /// Returns null if no structural pattern matches.
        /// </summary>
        public static (StructuralElementType Type, double Confidence)? Classify(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return null;
            var lower = layerName.ToLowerInvariant();

            foreach (var (pattern, type, confidence) in Rules)
            {
                if (lower.Contains(pattern))
                    return (type, confidence);
            }
            return null;
        }

        /// <summary>
        /// Returns true if the layer name indicates any structural content.
        /// </summary>
        public static bool IsStructuralLayer(string layerName)
        {
            if (string.IsNullOrEmpty(layerName)) return false;
            var lower = layerName.ToLowerInvariant();
            return lower.Contains("str") || lower.Contains("struct") ||
                   lower.Contains("found") || lower.Contains("fdn") ||
                   lower.Contains("ftg") || lower.Contains("beam") ||
                   lower.Contains("col") || lower.Contains("slab") ||
                   lower.Contains("brace") || lower.Contains("truss") ||
                   lower.Contains("pile") || lower.Contains("raft") ||
                   lower.Contains("shear") || lower.Contains("core") ||
                   lower.Contains("retaining") || lower.Contains("lintel");
        }
    }

    #endregion


    #region Foundation Analyzer

    /// <summary>
    /// Analyzes geometry to infer foundation type and dimensions.
    /// Uses aspect ratio, area, proximity to columns, and geometric patterns.
    /// </summary>
    internal static class FoundationAnalyzer
    {
        // Typical foundation dimension ranges (mm)
        private const double MinPadSizeMm = 600;
        private const double MaxPadSizeMm = 4000;
        private const double MinStripWidthMm = 300;
        private const double MaxStripWidthMm = 1200;
        private const double MinRaftAreaSqM = 20;

        /// <summary>
        /// Infers foundation type from a closed boundary polygon.
        /// Algorithm:
        ///   1. Compute bounding box dimensions and area
        ///   2. Aspect ratio near 1:1 + small area → Pad footing
        ///   3. Aspect ratio > 3:1 + narrow width → Strip footing
        ///   4. Large area covering multiple bays → Raft foundation
        ///   5. Circular/small → Pile cap
        /// </summary>
        public static (FoundationType Type, double Confidence) InferType(
            List<XYZ> boundary, List<XYZ> columnPositions = null)
        {
            if (boundary == null || boundary.Count < 3)
                return (FoundationType.Isolated, 0.3);

            // Compute bounding box
            double minX = boundary.Min(p => p.X), maxX = boundary.Max(p => p.X);
            double minY = boundary.Min(p => p.Y), maxY = boundary.Max(p => p.Y);
            double widthFt = maxX - minX;
            double depthFt = maxY - minY;
            double widthMm = widthFt * Units.FeetToMm;
            double depthMm = depthFt * Units.FeetToMm;
            double areaSqM = widthFt * depthFt * Units.SqFtToSqM;

            double aspectRatio = Math.Max(widthMm, depthMm) /
                Math.Max(1, Math.Min(widthMm, depthMm));
            double shorterDim = Math.Min(widthMm, depthMm);
            double longerDim = Math.Max(widthMm, depthMm);

            // Count columns within or near this boundary
            int columnsInside = 0;
            if (columnPositions != null)
            {
                foreach (var col in columnPositions)
                {
                    if (col.X >= minX - 0.5 && col.X <= maxX + 0.5 &&
                        col.Y >= minY - 0.5 && col.Y <= maxY + 0.5)
                        columnsInside++;
                }
            }

            // Large area with multiple columns → Raft
            if (areaSqM >= MinRaftAreaSqM && columnsInside >= 4)
                return (FoundationType.Raft, 0.90);

            // Large area → Raft
            if (areaSqM >= MinRaftAreaSqM * 2)
                return (FoundationType.Raft, 0.85);

            // Near-square, small-medium → Pad (isolated)
            if (aspectRatio < 1.8 &&
                shorterDim >= MinPadSizeMm && longerDim <= MaxPadSizeMm)
                return (FoundationType.Isolated, 0.88);

            // Multiple columns on elongated shape → Combined
            if (columnsInside >= 2 && aspectRatio >= 1.5 && aspectRatio < 4.0)
                return (FoundationType.Combined, 0.85);

            // Long and narrow → Strip
            if (aspectRatio >= 3.0 &&
                shorterDim >= MinStripWidthMm && shorterDim <= MaxStripWidthMm)
                return (FoundationType.Strip, 0.87);

            // Moderate elongation → Strip or Combined
            if (aspectRatio >= 2.0)
                return (FoundationType.Strip, 0.72);

            // Small square → Pile cap or isolated pad
            if (longerDim < MinPadSizeMm)
                return (FoundationType.Pile, 0.65);

            // Default
            return (FoundationType.Isolated, 0.60);
        }

        /// <summary>
        /// Calculates recommended pad footing size based on column load and soil capacity.
        /// Simplified Meyerhof bearing capacity with safety factor.
        /// </summary>
        /// <param name="columnLoadKN">Axial load on column (kN)</param>
        /// <param name="soilCapacityKPa">Allowable bearing capacity (kPa), default 150 for medium clay</param>
        /// <param name="safetyFactor">Global safety factor, default 2.5 per EC7</param>
        /// <returns>Square footing side length in mm</returns>
        public static double CalculatePadSize(double columnLoadKN,
            double soilCapacityKPa = 150, double safetyFactor = 2.5)
        {
            if (columnLoadKN <= 0) return MinPadSizeMm;
            if (soilCapacityKPa <= 0) soilCapacityKPa = 100;
            if (safetyFactor <= 0) safetyFactor = 2.5;
            double requiredAreaSqM = (columnLoadKN * safetyFactor) / soilCapacityKPa;
            double sideLengthM = Math.Sqrt(requiredAreaSqM);
            // Round up to nearest 50mm
            double sideMm = Math.Ceiling(sideLengthM * 1000.0 / 50.0) * 50.0;
            return Math.Max(sideMm, MinPadSizeMm);
        }

        /// <summary>
        /// Calculates recommended strip footing width based on wall load.
        /// </summary>
        /// <param name="wallLoadKNPerM">Load per meter run of wall (kN/m)</param>
        /// <param name="soilCapacityKPa">Allowable bearing capacity (kPa)</param>
        /// <param name="safetyFactor">Safety factor per EC7</param>
        /// <returns>Strip footing width in mm</returns>
        public static double CalculateStripWidth(double wallLoadKNPerM,
            double soilCapacityKPa = 100, double safetyFactor = 2.5)
        {
            if (wallLoadKNPerM <= 0) return MinStripWidthMm;
            if (soilCapacityKPa <= 0) soilCapacityKPa = 100;
            if (safetyFactor <= 0) safetyFactor = 2.5;
            double requiredWidthM = (wallLoadKNPerM * safetyFactor) / soilCapacityKPa;
            double widthMm = Math.Ceiling(requiredWidthM * 1000.0 / 50.0) * 50.0;
            return Math.Max(widthMm, MinStripWidthMm);
        }
    }

    #endregion


    #region Beam System Generator

    /// <summary>
    /// Generates beam systems from structural bay analysis.
    /// Algorithms:
    ///   - Tributary area calculation for load distribution
    ///   - Optimal secondary beam spacing from span/depth ratios
    ///   - Automatic beam size selection from span tables
    ///   - Edge beam inference from slab boundaries
    /// </summary>
    internal static class BeamSystemGenerator
    {
        // Typical span/depth ratios (L/d) for preliminary sizing
        private const double SpanDepthSimplySupported = 20.0;
        private const double SpanDepthContinuous = 26.0;
        private const double SpanDepthCantilever = 8.0;

        // Maximum secondary beam spacing (mm) before deflection governs
        private const double MaxSecondarySpacingMm = 3000;
        private const double MinSecondarySpacingMm = 1000;

        /// <summary>
        /// Analyzes a structural bay and determines optimal beam layout.
        /// Uses span direction, aspect ratio, and load conditions.
        /// </summary>
        public static BeamSystemLayout AnalyzeBay(StructuralBay bay,
            double liveLoadKPa = 2.5, double deadLoadKPa = 4.0)
        {
            var layout = new BeamSystemLayout();

            double spanXMm = bay.SpanXFt * Units.FeetToMm;
            double spanYMm = bay.SpanYFt * Units.FeetToMm;

            // Primary beams span the shorter direction for efficiency
            bool primaryAlongX = spanXMm <= spanYMm;
            layout.PrimarySpanMm = primaryAlongX ? spanXMm : spanYMm;
            layout.SecondarySpanMm = primaryAlongX ? spanYMm : spanXMm;
            layout.PrimaryAlongX = primaryAlongX;

            // Preliminary beam depth from span/depth ratio
            layout.PrimaryDepthMm = Math.Ceiling(layout.PrimarySpanMm /
                SpanDepthSimplySupported / 25.0) * 25.0;
            layout.SecondaryDepthMm = Math.Ceiling(layout.SecondarySpanMm /
                SpanDepthSimplySupported / 25.0) * 25.0;

            // Beam width typically 0.4-0.6 × depth for RC, or standard steel sections
            layout.PrimaryWidthMm = Math.Max(200,
                Math.Ceiling(layout.PrimaryDepthMm * 0.5 / 25.0) * 25.0);
            layout.SecondaryWidthMm = Math.Max(150,
                Math.Ceiling(layout.SecondaryDepthMm * 0.5 / 25.0) * 25.0);

            // Calculate optimal secondary beam count
            double totalLoad = (liveLoadKPa + deadLoadKPa);
            double tributaryWidth = layout.SecondarySpanMm;
            if (tributaryWidth > MaxSecondarySpacingMm)
            {
                int count = (int)Math.Ceiling(tributaryWidth / MaxSecondarySpacingMm) - 1;
                layout.SecondaryBeamCount = Math.Max(1, count);
                layout.SecondarySpacingMm = tributaryWidth / (layout.SecondaryBeamCount + 1);
            }
            else
            {
                layout.SecondaryBeamCount = 0;
                layout.SecondarySpacingMm = tributaryWidth;
            }

            // Tributary area for load calculations
            layout.TributaryAreaSqM = (spanXMm / 1000.0) * (spanYMm / 1000.0);
            layout.TotalLoadKN = layout.TributaryAreaSqM * totalLoad;

            return layout;
        }

        /// <summary>
        /// Selects a standard steel beam size from span and load.
        /// Simplified selection using common UK/EU sections.
        /// </summary>
        public static string SuggestSteelSection(double spanMm, double loadKNPerM)
        {
            // Approximate moment = wL²/8
            double spanM = spanMm / 1000.0;
            double momentKNm = loadKNPerM * spanM * spanM / 8.0;

            // Required section modulus (Wpl) ≈ M / fy, with fy=355 N/mm² for S355
            double wplRequired = momentKNm * 1e6 / 355.0; // mm³

            // Select from common UB sections (depth × mass)
            if (wplRequired < 100e3) return "UB 203x133x25";
            if (wplRequired < 200e3) return "UB 254x146x31";
            if (wplRequired < 350e3) return "UB 305x165x40";
            if (wplRequired < 500e3) return "UB 356x171x45";
            if (wplRequired < 750e3) return "UB 406x178x54";
            if (wplRequired < 1100e3) return "UB 457x191x67";
            if (wplRequired < 1500e3) return "UB 533x210x82";
            if (wplRequired < 2200e3) return "UB 610x229x101";
            if (wplRequired < 3200e3) return "UB 686x254x125";
            return "UB 762x267x147";
        }
    }

    /// <summary>Layout result for a structural bay beam system.</summary>
    public class BeamSystemLayout
    {
        public double PrimarySpanMm { get; set; }
        public double SecondarySpanMm { get; set; }
        public double PrimaryDepthMm { get; set; }
        public double PrimaryWidthMm { get; set; }
        public double SecondaryDepthMm { get; set; }
        public double SecondaryWidthMm { get; set; }
        public int SecondaryBeamCount { get; set; }
        public double SecondarySpacingMm { get; set; }
        public bool PrimaryAlongX { get; set; }
        public double TributaryAreaSqM { get; set; }
        public double TotalLoadKN { get; set; }
    }

    #endregion


    #region Bracing Pattern Engine

    /// <summary>
    /// Generates lateral bracing patterns between columns/nodes.
    /// Supports X, V, inverted-V (chevron), K, single diagonal, zigzag, and portal frame patterns.
    /// </summary>
    internal static class BracingPatternEngine
    {
        /// <summary>
        /// Generates bracing member geometry between two columns across N storeys.
        /// Returns list of (start, end) line segments for brace members.
        /// </summary>
        public static List<(XYZ Start, XYZ End)> GenerateBraces(
            XYZ colA, XYZ colB,
            double storeyHeightFt,
            int storeyCount,
            BracingPattern pattern)
        {
            var braces = new List<(XYZ Start, XYZ End)>();
            double bayWidth = colA.DistanceTo(new XYZ(colB.X, colB.Y, colA.Z));

            for (int i = 0; i < storeyCount; i++)
            {
                double baseZ = colA.Z + i * storeyHeightFt;
                double topZ = baseZ + storeyHeightFt;

                var bl = new XYZ(colA.X, colA.Y, baseZ);  // bottom-left
                var br = new XYZ(colB.X, colB.Y, baseZ);  // bottom-right
                var tl = new XYZ(colA.X, colA.Y, topZ);   // top-left
                var tr = new XYZ(colB.X, colB.Y, topZ);   // top-right

                // Midpoints
                var bm = (bl + br) * 0.5;
                var tm = (tl + tr) * 0.5;
                var ml = (bl + tl) * 0.5;
                var mr = (br + tr) * 0.5;

                switch (pattern)
                {
                    case BracingPattern.XBrace:
                        braces.Add((bl, tr));
                        braces.Add((br, tl));
                        break;

                    case BracingPattern.SingleDiagonal:
                        // Alternate direction per storey for visual balance
                        if (i % 2 == 0) braces.Add((bl, tr));
                        else braces.Add((br, tl));
                        break;

                    case BracingPattern.VBrace:
                        braces.Add((bl, tm));
                        braces.Add((br, tm));
                        break;

                    case BracingPattern.InvertedV:
                    case BracingPattern.Chevron:
                        braces.Add((bm, tl));
                        braces.Add((bm, tr));
                        break;

                    case BracingPattern.KBrace:
                        braces.Add((bl, mr));
                        braces.Add((br, mr));
                        break;

                    case BracingPattern.ZigZag:
                        if (i % 2 == 0)
                        {
                            braces.Add((bl, tr));
                        }
                        else
                        {
                            braces.Add((tl, br));
                        }
                        break;

                    case BracingPattern.Portal:
                        // Portal frame: add haunches at beam-column junctions
                        double haunchLen = bayWidth * 0.15;
                        double haunchDrop = storeyHeightFt * 0.15;
                        var hl = new XYZ(tl.X + (tr.X - tl.X) * 0.15, tl.Y + (tr.Y - tl.Y) * 0.15, topZ - haunchDrop);
                        var hr = new XYZ(tr.X - (tr.X - tl.X) * 0.15, tr.Y - (tr.Y - tl.Y) * 0.15, topZ - haunchDrop);
                        braces.Add((tl, hl));
                        braces.Add((tr, hr));
                        break;
                }
            }

            return braces;
        }

        /// <summary>
        /// Recommends bracing pattern based on building height and bay dimensions.
        /// </summary>
        public static BracingPattern RecommendPattern(
            double buildingHeightM, double bayWidthM, bool isSteel)
        {
            double slenderness = buildingHeightM / bayWidthM;

            if (slenderness > 6) return BracingPattern.XBrace;       // Tall/narrow → maximum stiffness
            if (slenderness > 4) return BracingPattern.Chevron;       // Medium → good stiffness, allows openings
            if (isSteel && slenderness > 2) return BracingPattern.VBrace;
            if (!isSteel) return BracingPattern.Portal;               // RC → portal frame is typical
            return BracingPattern.SingleDiagonal;                     // Low-rise steel
        }
    }

    #endregion

    #region Truss Generator

    /// <summary>
    /// Generates parametric truss geometry from span, depth, type, and panel count.
    /// Outputs member lines for top chord, bottom chord, verticals, and diagonals.
    /// </summary>
    internal static class TrussGenerator
    {
        /// <summary>
        /// Generates truss member geometry.
        /// </summary>
        /// <param name="startPt">Left support point</param>
        /// <param name="endPt">Right support point</param>
        /// <param name="depthFt">Truss depth (bottom to top chord)</param>
        /// <param name="type">Truss configuration</param>
        /// <param name="panelCount">Number of panels (even number recommended)</param>
        /// <returns>Lists of member lines: top chord, bottom chord, web members</returns>
        public static TrussGeometry Generate(
            XYZ startPt, XYZ endPt,
            double depthFt, TrussType type, int panelCount = 8)
        {
            var result = new TrussGeometry();
            panelCount = Math.Max(4, panelCount);
            if (panelCount % 2 != 0) panelCount++; // Ensure even

            var dir = (endPt - startPt).Normalize();
            double spanFt = startPt.DistanceTo(endPt);
            double panelWidth = spanFt / panelCount;
            var perpUp = XYZ.BasisZ;

            // Generate node positions
            var bottomNodes = new List<XYZ>();
            var topNodes = new List<XYZ>();

            for (int i = 0; i <= panelCount; i++)
            {
                double t = (double)i / panelCount;
                var bottomPt = startPt + dir * (spanFt * t);
                bottomNodes.Add(bottomPt);

                double topOffset = depthFt;
                switch (type)
                {
                    case TrussType.BowString:
                        // Parabolic top chord
                        topOffset = depthFt * (1.0 - 4.0 * (t - 0.5) * (t - 0.5));
                        topOffset = Math.Max(topOffset, depthFt * 0.2);
                        break;
                    case TrussType.Fink:
                        // Triangular (pitched) top chord
                        topOffset = depthFt * (1.0 - 2.0 * Math.Abs(t - 0.5));
                        topOffset = Math.Max(topOffset, 0.1);
                        break;
                }

                var topPt = bottomPt + perpUp * topOffset;
                topNodes.Add(topPt);
            }

            // Top chord segments
            for (int i = 0; i < panelCount; i++)
                result.TopChord.Add((topNodes[i], topNodes[i + 1]));

            // Bottom chord segments
            for (int i = 0; i < panelCount; i++)
                result.BottomChord.Add((bottomNodes[i], bottomNodes[i + 1]));

            // Web members depend on truss type
            switch (type)
            {
                case TrussType.Pratt:
                    // Verticals at each panel point + diagonals sloping toward center
                    for (int i = 0; i <= panelCount; i++)
                        result.Verticals.Add((bottomNodes[i], topNodes[i]));
                    for (int i = 0; i < panelCount; i++)
                    {
                        if (i < panelCount / 2)
                            result.Diagonals.Add((bottomNodes[i + 1], topNodes[i]));
                        else
                            result.Diagonals.Add((bottomNodes[i], topNodes[i + 1]));
                    }
                    break;

                case TrussType.Warren:
                    // No verticals, alternating diagonals
                    for (int i = 0; i < panelCount; i++)
                    {
                        if (i % 2 == 0)
                            result.Diagonals.Add((bottomNodes[i], topNodes[i + 1]));
                        else
                            result.Diagonals.Add((topNodes[i], bottomNodes[i + 1]));
                    }
                    break;

                case TrussType.Howe:
                    // Verticals + diagonals sloping away from center
                    for (int i = 0; i <= panelCount; i++)
                        result.Verticals.Add((bottomNodes[i], topNodes[i]));
                    for (int i = 0; i < panelCount; i++)
                    {
                        if (i < panelCount / 2)
                            result.Diagonals.Add((bottomNodes[i], topNodes[i + 1]));
                        else
                            result.Diagonals.Add((bottomNodes[i + 1], topNodes[i]));
                    }
                    break;

                case TrussType.Fan:
                    // All diagonals radiate from support points
                    for (int i = 1; i <= panelCount / 2; i++)
                        result.Diagonals.Add((bottomNodes[0], topNodes[i]));
                    for (int i = panelCount / 2; i < panelCount; i++)
                        result.Diagonals.Add((bottomNodes[panelCount], topNodes[i]));
                    // Verticals at intermediate points
                    for (int i = 1; i < panelCount; i++)
                        result.Verticals.Add((bottomNodes[i], topNodes[i]));
                    break;

                case TrussType.KTruss:
                    // K-pattern: verticals split by diagonal pairs
                    for (int i = 0; i <= panelCount; i++)
                        result.Verticals.Add((bottomNodes[i], topNodes[i]));
                    for (int i = 0; i < panelCount; i++)
                    {
                        var midLeft = (bottomNodes[i] + topNodes[i]) * 0.5;
                        var midRight = (bottomNodes[i + 1] + topNodes[i + 1]) * 0.5;
                        result.Diagonals.Add((midLeft, topNodes[i + 1]));
                        result.Diagonals.Add((midLeft, bottomNodes[i + 1]));
                    }
                    break;

                case TrussType.Vierendeel:
                    // Rigid frame truss — verticals only, no diagonals (moment connections)
                    for (int i = 0; i <= panelCount; i++)
                        result.Verticals.Add((bottomNodes[i], topNodes[i]));
                    break;

                default: // Pratt-like fallback
                    for (int i = 0; i <= panelCount; i++)
                        result.Verticals.Add((bottomNodes[i], topNodes[i]));
                    for (int i = 0; i < panelCount; i++)
                        result.Diagonals.Add((bottomNodes[i], topNodes[i + 1]));
                    break;
            }

            result.SpanFt = spanFt;
            result.DepthFt = depthFt;
            result.PanelCount = panelCount;
            result.TotalMemberCount = result.TopChord.Count + result.BottomChord.Count +
                result.Verticals.Count + result.Diagonals.Count;

            return result;
        }

        /// <summary>
        /// Recommends truss depth from span using L/d ratios.
        /// </summary>
        public static double RecommendDepthMm(double spanMm, TrussType type)
        {
            double ratio = type switch
            {
                TrussType.Pratt => 10.0,
                TrussType.Warren => 12.0,
                TrussType.Howe => 10.0,
                TrussType.Vierendeel => 6.0, // Needs more depth (no diagonals)
                TrussType.BowString => 8.0,
                TrussType.Fink => 5.0,       // Pitched roof, shallow
                _ => 10.0,
            };
            double depthMm = Math.Ceiling(spanMm / ratio / 50.0) * 50.0;
            return Math.Max(depthMm, 300);
        }
    }

    /// <summary>Geometry output from truss generation.</summary>
    public class TrussGeometry
    {
        public List<(XYZ Start, XYZ End)> TopChord { get; set; } = new();
        public List<(XYZ Start, XYZ End)> BottomChord { get; set; } = new();
        public List<(XYZ Start, XYZ End)> Verticals { get; set; } = new();
        public List<(XYZ Start, XYZ End)> Diagonals { get; set; } = new();
        public double SpanFt { get; set; }
        public double DepthFt { get; set; }
        public int PanelCount { get; set; }
        public int TotalMemberCount { get; set; }
    }

    #endregion


    #region Slab Analyzer

    /// <summary>
    /// Analyzes slab boundaries for openings, edge beams, and reinforcement zones.
    /// Detects step changes, cantilevers, and irregular shapes.
    /// </summary>
    internal static class SlabAnalyzer
    {
        /// <summary>
        /// Detects openings within a slab boundary by finding inner loops
        /// that are fully contained within the outer boundary.
        /// Uses ray-casting point-in-polygon test.
        /// </summary>
        public static List<CurveLoop> DetectOpenings(
            CurveLoop outerBoundary, List<CurveLoop> candidateLoops)
        {
            var openings = new List<CurveLoop>();
            if (candidateLoops == null) return openings;

            // Get outer boundary bounding box for quick rejection
            var outerPts = GetPointsFromLoop(outerBoundary);
            double outerMinX = outerPts.Min(p => p.X);
            double outerMaxX = outerPts.Max(p => p.X);
            double outerMinY = outerPts.Min(p => p.Y);
            double outerMaxY = outerPts.Max(p => p.Y);

            foreach (var loop in candidateLoops)
            {
                var pts = GetPointsFromLoop(loop);
                if (pts.Count < 3) continue;

                // Quick bounding box containment check
                bool allInside = true;
                foreach (var pt in pts)
                {
                    if (pt.X < outerMinX || pt.X > outerMaxX ||
                        pt.Y < outerMinY || pt.Y > outerMaxY)
                    {
                        allInside = false;
                        break;
                    }
                }
                if (!allInside) continue;

                // Ray-casting test for centroid
                var centroid = new XYZ(
                    pts.Average(p => p.X),
                    pts.Average(p => p.Y),
                    pts.Average(p => p.Z));
                if (PointInPolygon(centroid, outerPts))
                    openings.Add(loop);
            }

            return openings;
        }

        /// <summary>
        /// Identifies edges of a slab boundary that need edge beams
        /// (edges not supported by walls or other slabs).
        /// </summary>
        public static List<(XYZ Start, XYZ End)> FindUnsupportedEdges(
            CurveLoop boundary, List<XYZ> wallEndpoints, double toleranceFt = 1.0)
        {
            var unsupported = new List<(XYZ Start, XYZ End)>();

            foreach (var curve in boundary)
            {
                var start = curve.GetEndPoint(0);
                var end = curve.GetEndPoint(1);

                // Check if this edge is near any wall
                bool hasSupport = false;
                if (wallEndpoints != null)
                {
                    foreach (var wp in wallEndpoints)
                    {
                        // Check if wall endpoint is near this edge (within tolerance)
                        double distToLine = DistancePointToLine(wp, start, end);
                        if (distToLine < toleranceFt)
                        {
                            hasSupport = true;
                            break;
                        }
                    }
                }

                if (!hasSupport)
                    unsupported.Add((start, end));
            }

            return unsupported;
        }

        /// <summary>
        /// Calculates reinforcement zones based on support conditions.
        /// Returns list of zones with position type (top/bottom) and extent.
        /// </summary>
        public static List<RebarZone> InferRebarZones(
            CurveLoop boundary, List<XYZ> supportPositions)
        {
            var zones = new List<RebarZone>();
            var pts = GetPointsFromLoop(boundary);
            if (pts.Count < 3 || supportPositions == null) return zones;

            var centroid = new XYZ(
                pts.Average(p => p.X),
                pts.Average(p => p.Y), 0);

            // Mid-span zone: bottom reinforcement (sagging moment)
            zones.Add(new RebarZone
            {
                ZoneType = "Bottom - Midspan",
                CenterPoint = centroid,
                ExtentFt = pts.Max(p => p.DistanceTo(centroid)) * 0.6,
                Position = RebarPosition.Bottom,
            });

            // Support zones: top reinforcement (hogging moment)
            foreach (var support in supportPositions)
            {
                zones.Add(new RebarZone
                {
                    ZoneType = "Top - Support",
                    CenterPoint = support,
                    ExtentFt = 2.0, // ~600mm each side of support
                    Position = RebarPosition.Top,
                });
            }

            return zones;
        }

        // ── Geometry Helpers ──

        internal static List<XYZ> GetPointsFromLoop(CurveLoop loop)
        {
            var pts = new List<XYZ>();
            foreach (var curve in loop)
                pts.Add(curve.GetEndPoint(0));
            return pts;
        }

        internal static bool PointInPolygon(XYZ point, List<XYZ> polygon)
        {
            // Ray-casting algorithm (2D, ignores Z)
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                    point.X < (polygon[j].X - polygon[i].X) *
                    (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) +
                    polygon[i].X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        internal static double DistancePointToLine(XYZ point, XYZ lineStart, XYZ lineEnd)
        {
            var lineDir = lineEnd - lineStart;
            double lineLenSq = lineDir.GetLength();
            if (lineLenSq < 1e-9) return point.DistanceTo(lineStart);
            lineLenSq *= lineLenSq;

            double t = Math.Max(0, Math.Min(1,
                (point - lineStart).DotProduct(lineDir) / lineLenSq));
            var projection = lineStart + lineDir * t;
            return point.DistanceTo(projection);
        }
    }

    /// <summary>Reinforcement zone with position and extent.</summary>
    public class RebarZone
    {
        public string ZoneType { get; set; }
        public XYZ CenterPoint { get; set; }
        public double ExtentFt { get; set; }
        public RebarPosition Position { get; set; }
    }

    /// <summary>Reinforcement position in slab.</summary>
    public enum RebarPosition { Top, Bottom }

    #endregion


    #region Load Path Analyzer

    /// <summary>
    /// Builds structural connectivity graph and traces load paths
    /// from roof → beams → columns → foundations.
    /// Uses spatial proximity and geometric intersection for connection detection.
    /// </summary>
    internal static class LoadPathAnalyzer
    {
        private const double ConnectionToleranceFt = 0.5; // ~150mm

        /// <summary>
        /// Builds a load path graph from structural elements in the model.
        /// Connects elements by spatial proximity at endpoints.
        /// </summary>
        public static List<LoadPathNode> BuildConnectivityGraph(Document doc)
        {
            var nodes = new List<LoadPathNode>();

            // Collect all structural elements
            var columns = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            var beams = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            var foundations = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .WhereElementIsNotElementType()
                .ToList();

            // Create nodes for columns
            foreach (var col in columns)
            {
                var loc = col.Location as LocationPoint;
                if (loc == null) continue;
                nodes.Add(new LoadPathNode
                {
                    ElementId = col.Id,
                    Location = loc.Point,
                    NodeType = StructuralElementType.Column,
                });
            }

            // Create nodes for beams at BOTH endpoints (not midpoint) for accurate connectivity
            foreach (var beam in beams)
            {
                var loc = beam.Location as LocationCurve;
                if (loc?.Curve == null) continue;
                var startPt = loc.Curve.GetEndPoint(0);
                var endPt = loc.Curve.GetEndPoint(1);
                nodes.Add(new LoadPathNode
                {
                    ElementId = beam.Id,
                    Location = startPt,
                    NodeType = StructuralElementType.Beam,
                });
                nodes.Add(new LoadPathNode
                {
                    ElementId = beam.Id,
                    Location = endPt,
                    NodeType = StructuralElementType.Beam,
                });
            }

            // Create nodes for foundations
            foreach (var fdn in foundations)
            {
                var bb = fdn.get_BoundingBox(null);
                if (bb == null) continue;
                var center = (bb.Min + bb.Max) * 0.5;
                nodes.Add(new LoadPathNode
                {
                    ElementId = fdn.Id,
                    Location = center,
                    NodeType = StructuralElementType.Footing,
                });
            }

            // Build connections by proximity
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    double dist = nodes[i].Location.DistanceTo(nodes[j].Location);
                    // Different tolerance for different connection types
                    double tol = ConnectionToleranceFt;
                    // Beam-to-column connections can be at beam endpoints
                    if ((nodes[i].NodeType == StructuralElementType.Beam ||
                         nodes[j].NodeType == StructuralElementType.Beam))
                    {
                        // Check beam endpoints too
                        tol = ConnectionToleranceFt * 3; // ~450mm for beam endpoint tolerance
                    }

                    if (dist < tol)
                    {
                        nodes[i].ConnectedTo.Add(nodes[j]);
                        nodes[j].ConnectedTo.Add(nodes[i]);
                    }
                }
            }

            return nodes;
        }

        /// <summary>
        /// Traces load path from a starting node down to foundation.
        /// Returns ordered path: element → beam → column → foundation.
        /// Uses BFS to find shortest path to any foundation node.
        /// </summary>
        public static List<LoadPathNode> TraceToFoundation(
            LoadPathNode startNode, List<LoadPathNode> allNodes)
        {
            var visited = new HashSet<ElementId>();
            var queue = new Queue<List<LoadPathNode>>();
            queue.Enqueue(new List<LoadPathNode> { startNode });

            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                var current = path.Last();

                if (visited.Contains(current.ElementId)) continue;
                visited.Add(current.ElementId);

                // Found foundation → return path
                if (current.NodeType == StructuralElementType.Footing ||
                    current.NodeType == StructuralElementType.PadFooting ||
                    current.NodeType == StructuralElementType.StripFooting ||
                    current.NodeType == StructuralElementType.RaftFoundation)
                    return path;

                // Prioritize downward connections (lower Z)
                var sorted = current.ConnectedTo
                    .Where(n => !visited.Contains(n.ElementId))
                    .OrderBy(n => n.Location.Z)
                    .ToList();

                foreach (var next in sorted)
                {
                    var newPath = new List<LoadPathNode>(path) { next };
                    queue.Enqueue(newPath);
                }
            }

            return new List<LoadPathNode>(); // No path found
        }

        /// <summary>
        /// Calculates tributary area for each column using Voronoi-like nearest-column assignment.
        /// Simplified grid-based approach: divides floor area into cells, assigns each to nearest column.
        /// </summary>
        public static Dictionary<ElementId, double> CalculateTributaryAreas(
            List<LoadPathNode> columnNodes, double floorWidthFt, double floorDepthFt,
            double originXFt = 0, double originYFt = 0)
        {
            var areas = new Dictionary<ElementId, double>();
            if (columnNodes.Count == 0) return areas;

            foreach (var col in columnNodes)
                areas[col.ElementId] = 0;

            // Grid-based Voronoi approximation
            const int gridRes = 100;
            double cellWidth = floorWidthFt / gridRes;
            double cellDepth = floorDepthFt / gridRes;
            double cellArea = cellWidth * cellDepth;

            for (int ix = 0; ix < gridRes; ix++)
            {
                double x = originXFt + (ix + 0.5) * cellWidth;
                for (int iy = 0; iy < gridRes; iy++)
                {
                    double y = originYFt + (iy + 0.5) * cellDepth;
                    var pt = new XYZ(x, y, 0);

                    // Find nearest column (2D distance)
                    LoadPathNode nearest = null;
                    double minDist = double.MaxValue;
                    foreach (var col in columnNodes)
                    {
                        double d = Math.Sqrt(
                            Math.Pow(col.Location.X - x, 2) +
                            Math.Pow(col.Location.Y - y, 2));
                        if (d < minDist) { minDist = d; nearest = col; }
                    }

                    if (nearest != null)
                        areas[nearest.ElementId] += cellArea;
                }
            }

            return areas;
        }
    }

    #endregion


    #region Structural Grid Optimizer

    /// <summary>
    /// Optimizes structural column grid layouts and detects bays.
    /// Algorithms:
    ///   - DBSCAN clustering for column position regularization
    ///   - Grid line inference from column alignments
    ///   - Bay detection from grid intersections
    ///   - Optimal grid spacing from cost/material efficiency curves
    /// </summary>
    internal static class StructuralGridOptimizer
    {
        /// <summary>
        /// Detects structural bays from column positions using grid intersection analysis.
        /// Algorithm:
        ///   1. Project columns onto X and Y axes
        ///   2. Cluster projections to find grid lines (DBSCAN-like)
        ///   3. Form rectangular bays from grid intersections
        ///   4. Verify each bay has columns at all 4 corners
        /// </summary>
        public static List<StructuralBay> DetectBays(List<XYZ> columnPositions,
            double clusterToleranceFt = 1.0)
        {
            var bays = new List<StructuralBay>();
            if (columnPositions == null || columnPositions.Count < 4) return bays;

            // Step 1: Find grid lines by clustering X and Y coordinates
            var xCoords = columnPositions.Select(p => p.X).ToList();
            var yCoords = columnPositions.Select(p => p.Y).ToList();

            var xGridLines = ClusterCoordinates(xCoords, clusterToleranceFt);
            var yGridLines = ClusterCoordinates(yCoords, clusterToleranceFt);

            if (xGridLines.Count < 2 || yGridLines.Count < 2) return bays;

            xGridLines.Sort();
            yGridLines.Sort();

            // Step 2: Form bays from adjacent grid line pairs
            for (int ix = 0; ix < xGridLines.Count - 1; ix++)
            {
                for (int iy = 0; iy < yGridLines.Count - 1; iy++)
                {
                    double x1 = xGridLines[ix];
                    double x2 = xGridLines[ix + 1];
                    double y1 = yGridLines[iy];
                    double y2 = yGridLines[iy + 1];

                    // Verify columns exist at all 4 corners
                    var corners = new[]
                    {
                        new XYZ(x1, y1, 0), new XYZ(x2, y1, 0),
                        new XYZ(x2, y2, 0), new XYZ(x1, y2, 0)
                    };

                    bool allCornersHaveColumns = true;
                    foreach (var corner in corners)
                    {
                        bool found = columnPositions.Any(p =>
                            Math.Abs(p.X - corner.X) < clusterToleranceFt &&
                            Math.Abs(p.Y - corner.Y) < clusterToleranceFt);
                        if (!found) { allCornersHaveColumns = false; break; }
                    }

                    if (!allCornersHaveColumns) continue;

                    double spanX = x2 - x1;
                    double spanY = y2 - y1;

                    var bay = new StructuralBay
                    {
                        Corner1 = corners[0],
                        Corner2 = corners[1],
                        Corner3 = corners[2],
                        Corner4 = corners[3],
                        SpanXFt = spanX,
                        SpanYFt = spanY,
                        AreaSqFt = spanX * spanY,
                    };

                    // Determine if secondary beams are needed
                    double maxSpanMm = Math.Max(spanX, spanY) * Units.FeetToMm;
                    bay.NeedsSecondaryBeams = maxSpanMm > 6000; // > 6m span
                    if (bay.NeedsSecondaryBeams)
                    {
                        bay.RecommendedSecondaryCount = (int)Math.Ceiling(maxSpanMm / 3000.0) - 1;
                    }

                    bays.Add(bay);
                }
            }

            return bays;
        }

        /// <summary>
        /// Clusters a list of 1D coordinates to find grid line positions.
        /// Uses a simple sweep-line clustering (DBSCAN-inspired for 1D).
        /// </summary>
        internal static List<double> ClusterCoordinates(
            List<double> coords, double tolerance)
        {
            if (coords.Count == 0) return new List<double>();

            var sorted = coords.OrderBy(c => c).ToList();
            var clusters = new List<List<double>> { new List<double> { sorted[0] } };

            for (int i = 1; i < sorted.Count; i++)
            {
                double lastCenter = clusters.Last().Average();
                if (Math.Abs(sorted[i] - lastCenter) <= tolerance)
                    clusters.Last().Add(sorted[i]);
                else
                    clusters.Add(new List<double> { sorted[i] });
            }

            // Return cluster centers (only clusters with 2+ columns are grid lines)
            return clusters
                .Where(c => c.Count >= 1) // Allow single columns for edge cases
                .Select(c => c.Average())
                .ToList();
        }

        /// <summary>
        /// Suggests optimal grid spacing based on building use and material.
        /// Returns recommended X and Y spacing in mm.
        /// </summary>
        public static (double SpacingXMm, double SpacingYMm) RecommendGridSpacing(
            string buildingUse, bool isSteel)
        {
            // Typical optimal spans per building type
            return buildingUse?.ToLowerInvariant() switch
            {
                "office" => isSteel ? (9000, 9000) : (7500, 7500),
                "residential" => isSteel ? (6000, 8000) : (5000, 6000),
                "warehouse" => isSteel ? (12000, 18000) : (8000, 12000),
                "retail" => isSteel ? (8000, 12000) : (7000, 9000),
                "hospital" => isSteel ? (7500, 7500) : (6000, 7500),
                "school" => isSteel ? (7500, 9000) : (6000, 7500),
                "car_park" => isSteel ? (7500, 16000) : (7500, 10000),
                "industrial" => isSteel ? (12000, 24000) : (8000, 15000),
                _ => isSteel ? (8000, 10000) : (6000, 7500),
            };
        }
    }

    #endregion


    // ════════════════════════════════════════════════════════════════════
    // STRUCTURAL MODELING ENGINE — Main Orchestrator
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Advanced structural modeling automation engine.
    /// Creates structural Revit elements with intelligent type resolution,
    /// geometric analysis, and CAD-to-structural conversion.
    ///
    /// Capabilities:
    ///   - Pad/strip/raft foundation creation from geometry or load analysis
    ///   - Structural wall creation (shear walls, core walls, retaining walls)
    ///   - Structural slab creation with opening detection
    ///   - Beam system generation with tributary area analysis
    ///   - Bracing system generation (X, V, K, chevron patterns)
    ///   - Parametric truss generation (Pratt, Warren, Howe, Fan, Vierendeel)
    ///   - Column grid optimization and bay detection
    ///   - Load path analysis and connectivity graph
    ///   - Full structural frame from CAD (columns + beams + bracing + foundations)
    /// </summary>
    public class StructuralModelingEngine
    {
        private readonly Document _doc;
        private readonly ModelEngine _modelEngine;
        private readonly ModelFamilyResolver _resolver;

        public StructuralModelingEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _modelEngine = new ModelEngine(doc);
            _resolver = new ModelFamilyResolver(doc);
        }

        // ── Pad Footing ──────────────────────────────────────────────────

        /// <summary>
        /// Creates an isolated (pad) footing under a column or at a point.
        /// Sizes from load calculation or explicit dimensions.
        /// </summary>
        public StructuralModelResult CreatePadFooting(
            double xMm, double yMm,
            double widthMm = 1200, double depthMm = 1200, double thicknessMm = 400,
            string typeName = null, string levelName = null,
            double columnLoadKN = 0, double soilCapacityKPa = 150)
        {
            try
            {
                // Auto-size from load if provided
                if (columnLoadKN > 0)
                {
                    double calcSize = FoundationAnalyzer.CalculatePadSize(
                        columnLoadKN, soilCapacityKPa);
                    widthMm = Math.Max(widthMm, calcSize);
                    depthMm = widthMm; // Square footing
                }

                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveFamilySymbol(
                    BuiltInCategory.OST_StructuralFoundation, typeName);
                if (!typeResult.Success)
                    return StructuralModelResult.Fail(typeResult.Message);

                var symbol = _doc.GetElement(typeResult.TypeId) as FamilySymbol;
                if (symbol == null)
                    return StructuralModelResult.Fail("No foundation family found.");
                _resolver.EnsureActive(symbol);

                var pt = new XYZ(Units.Mm(xMm), Units.Mm(yMm), level.Elevation);

                FamilyInstance footing = null;
                using (var tx = new Transaction(_doc, "STING STRUCT: Create Pad Footing"))
                {
                    tx.Start();
                    footing = _doc.Create.NewFamilyInstance(
                        pt, symbol, level, StructuralType.Footing);

                    // Try to set dimensions
                    TrySetParam(footing, BuiltInParameter.STRUCTURAL_FOUNDATION_LENGTH, Units.Mm(widthMm));
                    TrySetParam(footing, BuiltInParameter.STRUCTURAL_FOUNDATION_WIDTH, Units.Mm(depthMm));

                    ModelWorksetAssigner.Assign(_doc, footing);
                    tx.Commit();
                }

                var result = new StructuralModelResult
                {
                    Success = true,
                    FootingsCreated = 1,
                    Summary = $"Created {widthMm}×{depthMm}×{thicknessMm}mm pad footing " +
                        $"({typeResult.TypeName}) on {level.Name}",
                };
                result.CreatedIds.Add(footing.Id);
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreatePadFooting", ex);
                return StructuralModelResult.Fail($"Pad footing failed: {ex.Message}");
            }
        }

        // ── Strip Footing ────────────────────────────────────────────────

        /// <summary>
        /// Creates a strip (continuous) footing along a line.
        /// </summary>
        public StructuralModelResult CreateStripFooting(
            double startXMm, double startYMm,
            double endXMm, double endYMm,
            double widthMm = 600, double depthMm = 300,
            string typeName = null, string levelName = null,
            double wallLoadKNPerM = 0, double soilCapacityKPa = 100)
        {
            try
            {
                if (wallLoadKNPerM > 0)
                {
                    widthMm = Math.Max(widthMm,
                        FoundationAnalyzer.CalculateStripWidth(wallLoadKNPerM, soilCapacityKPa));
                }

                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                // Create as a structural wall below grade
                var typeResult = _resolver.ResolveWallType(typeName, widthMm);
                if (!typeResult.Success)
                    return StructuralModelResult.Fail(typeResult.Message);

                var startPt = new XYZ(Units.Mm(startXMm), Units.Mm(startYMm), level.Elevation);
                var endPt = new XYZ(Units.Mm(endXMm), Units.Mm(endYMm), level.Elevation);

                if (startPt.DistanceTo(endPt) < 0.01)
                    return StructuralModelResult.Fail("Start and end points are too close.");

                var line = Line.CreateBound(startPt, endPt);

                Wall strip = null;
                using (var tx = new Transaction(_doc, "STING STRUCT: Create Strip Footing"))
                {
                    tx.Start();
                    strip = Wall.Create(_doc, line, typeResult.TypeId,
                        level.Id, Units.Mm(depthMm), -Units.Mm(depthMm), false, true);
                    ModelWorksetAssigner.Assign(_doc, strip);
                    tx.Commit();
                }

                double lengthMm = startPt.DistanceTo(endPt) * Units.FeetToMm;
                var result = new StructuralModelResult
                {
                    Success = true,
                    FootingsCreated = 1,
                    Summary = $"Created {lengthMm / 1000:F1}m strip footing " +
                        $"({widthMm}mm wide × {depthMm}mm deep) on {level.Name}",
                };
                result.CreatedIds.Add(strip.Id);
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreateStripFooting", ex);
                return StructuralModelResult.Fail($"Strip footing failed: {ex.Message}");
            }
        }

        // ── Structural Slab ──────────────────────────────────────────────

        /// <summary>
        /// Creates a structural floor slab with optional openings.
        /// Analyzes boundary for edge beam needs and reinforcement zones.
        /// </summary>
        public StructuralModelResult CreateStructuralSlab(
            double widthMm, double depthMm,
            double thicknessMm = 200,
            string typeName = null, string levelName = null,
            double originXMm = 0, double originYMm = 0,
            List<(double X, double Y, double W, double H)> openingsMm = null)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveFloorType(typeName);
                if (!typeResult.Success)
                    return StructuralModelResult.Fail(typeResult.Message);

                var ox = Units.Mm(originXMm);
                var oy = Units.Mm(originYMm);
                var w = Units.Mm(widthMm);
                var d = Units.Mm(depthMm);

                // Create outer boundary
                var boundary = new CurveLoop();
                boundary.Append(Line.CreateBound(new XYZ(ox, oy, 0), new XYZ(ox + w, oy, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox + w, oy, 0), new XYZ(ox + w, oy + d, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox + w, oy + d, 0), new XYZ(ox, oy + d, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox, oy + d, 0), new XYZ(ox, oy, 0)));

                var loops = new List<CurveLoop> { boundary };

                // Add openings as inner loops (reversed winding)
                if (openingsMm != null)
                {
                    foreach (var (oX, oY, oW, oH) in openingsMm)
                    {
                        var opOx = Units.Mm(oX);
                        var opOy = Units.Mm(oY);
                        var opW = Units.Mm(oW);
                        var opH = Units.Mm(oH);

                        var opening = new CurveLoop();
                        // Reversed winding for opening
                        opening.Append(Line.CreateBound(new XYZ(opOx, opOy, 0), new XYZ(opOx, opOy + opH, 0)));
                        opening.Append(Line.CreateBound(new XYZ(opOx, opOy + opH, 0), new XYZ(opOx + opW, opOy + opH, 0)));
                        opening.Append(Line.CreateBound(new XYZ(opOx + opW, opOy + opH, 0), new XYZ(opOx + opW, opOy, 0)));
                        opening.Append(Line.CreateBound(new XYZ(opOx + opW, opOy, 0), new XYZ(opOx, opOy, 0)));
                        loops.Add(opening);
                    }
                }

                Floor slab = null;
                var fh = new ModelFailureHandler();
                using (var tx = new Transaction(_doc, "STING STRUCT: Create Structural Slab"))
                {
                    var opts = tx.GetFailureHandlingOptions();
                    opts.SetFailuresPreprocessor(fh);
                    tx.SetFailureHandlingOptions(opts);
                    tx.Start();

                    slab = Floor.Create(_doc, loops, typeResult.TypeId, level.Id);

                    // Mark as structural
                    var structParam = slab.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                    if (structParam != null && !structParam.IsReadOnly)
                        structParam.Set(1);

                    ModelWorksetAssigner.Assign(_doc, slab);
                    tx.Commit();
                }

                double areaSqM = (widthMm / 1000.0) * (depthMm / 1000.0);
                int openingCount = openingsMm?.Count ?? 0;
                var result = new StructuralModelResult
                {
                    Success = true,
                    SlabsCreated = 1,
                    Summary = $"Created {areaSqM:F1}m² structural slab ({thicknessMm}mm, " +
                        $"{typeResult.TypeName}){(openingCount > 0 ? $" with {openingCount} openings" : "")} on {level.Name}",
                    Warnings = fh.CapturedWarnings,
                };
                result.CreatedIds.Add(slab.Id);
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreateStructuralSlab", ex);
                return StructuralModelResult.Fail($"Structural slab failed: {ex.Message}");
            }
        }


        // ── Beam System ──────────────────────────────────────────────────

        /// <summary>
        /// Creates a beam system for a structural bay with primary and secondary beams.
        /// Auto-calculates beam sizes from span/depth ratios.
        /// </summary>
        public StructuralModelResult CreateBeamSystem(
            double bayXMm, double bayYMm,
            double originXMm = 0, double originYMm = 0,
            string beamTypeName = null, string levelName = null,
            double heightMm = 3000,
            double liveLoadKPa = 2.5, double deadLoadKPa = 4.0)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveFamilySymbol(
                    BuiltInCategory.OST_StructuralFraming, beamTypeName);
                if (!typeResult.Success)
                    return StructuralModelResult.Fail(typeResult.Message);

                var symbol = _doc.GetElement(typeResult.TypeId) as FamilySymbol;
                if (symbol == null)
                    return StructuralModelResult.Fail("No beam family found.");
                _resolver.EnsureActive(symbol);

                // Analyze bay layout — convert mm to feet for internal use
                var bay = new StructuralBay
                {
                    SpanXFt = bayXMm * Units.MmToFeet,
                    SpanYFt = bayYMm * Units.MmToFeet,
                };
                var layout = BeamSystemGenerator.AnalyzeBay(bay, liveLoadKPa, deadLoadKPa);

                double z = Units.Mm(heightMm);
                var ox = Units.Mm(originXMm);
                var oy = Units.Mm(originYMm);
                var w = Units.Mm(bayXMm);
                var d = Units.Mm(bayYMm);

                var createdIds = new List<ElementId>();
                var fh = new ModelFailureHandler();

                using (var tx = new Transaction(_doc, "STING STRUCT: Create Beam System"))
                {
                    var opts = tx.GetFailureHandlingOptions();
                    opts.SetFailuresPreprocessor(fh);
                    tx.SetFailureHandlingOptions(opts);
                    tx.Start();

                    // Edge beams (4 perimeter beams)
                    var edgeLines = new[]
                    {
                        Line.CreateBound(new XYZ(ox, oy, z), new XYZ(ox + w, oy, z)),
                        Line.CreateBound(new XYZ(ox + w, oy, z), new XYZ(ox + w, oy + d, z)),
                        Line.CreateBound(new XYZ(ox + w, oy + d, z), new XYZ(ox, oy + d, z)),
                        Line.CreateBound(new XYZ(ox, oy + d, z), new XYZ(ox, oy, z)),
                    };

                    foreach (var eLine in edgeLines)
                    {
                        try
                        {
                            var beam = _doc.Create.NewFamilyInstance(
                                eLine, symbol, level, StructuralType.Beam);
                            ModelWorksetAssigner.Assign(_doc, beam);
                            createdIds.Add(beam.Id);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Edge beam skipped: {ex.Message}");
                        }
                    }

                    // Secondary beams (if span requires them)
                    if (layout.SecondaryBeamCount > 0)
                    {
                        double spacing = layout.PrimaryAlongX
                            ? d / (layout.SecondaryBeamCount + 1)
                            : w / (layout.SecondaryBeamCount + 1);

                        for (int i = 1; i <= layout.SecondaryBeamCount; i++)
                        {
                            Line secLine;
                            if (layout.PrimaryAlongX)
                            {
                                double yPos = oy + spacing * i;
                                secLine = Line.CreateBound(
                                    new XYZ(ox, yPos, z), new XYZ(ox + w, yPos, z));
                            }
                            else
                            {
                                double xPos = ox + spacing * i;
                                secLine = Line.CreateBound(
                                    new XYZ(xPos, oy, z), new XYZ(xPos, oy + d, z));
                            }

                            try
                            {
                                var beam = _doc.Create.NewFamilyInstance(
                                    secLine, symbol, level, StructuralType.Beam);
                                ModelWorksetAssigner.Assign(_doc, beam);
                                createdIds.Add(beam.Id);
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"Secondary beam skipped: {ex.Message}");
                            }
                        }
                    }

                    tx.Commit();
                }

                string suggestion = BeamSystemGenerator.SuggestSteelSection(
                    Math.Max(bayXMm, bayYMm), (liveLoadKPa + deadLoadKPa) * bayXMm / 1000.0);

                var result = new StructuralModelResult
                {
                    Success = true,
                    BeamsCreated = createdIds.Count,
                    Summary = $"Created beam system: {createdIds.Count} beams " +
                        $"(4 edge + {layout.SecondaryBeamCount} secondary) for " +
                        $"{bayXMm / 1000:F1}m × {bayYMm / 1000:F1}m bay. " +
                        $"Suggested steel: {suggestion}. Load: {layout.TotalLoadKN:F0}kN",
                    Warnings = fh.CapturedWarnings,
                    CreatedIds = createdIds,
                };
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreateBeamSystem", ex);
                return StructuralModelResult.Fail($"Beam system failed: {ex.Message}");
            }
        }

        // ── Bracing ──────────────────────────────────────────────────────

        /// <summary>
        /// Creates a bracing system between two column positions across storeys.
        /// </summary>
        public StructuralModelResult CreateBracing(
            double col1XMm, double col1YMm,
            double col2XMm, double col2YMm,
            int storeyCount = 1,
            double storeyHeightMm = 3600,
            BracingPattern pattern = BracingPattern.XBrace,
            string braceTypeName = null, string levelName = null)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveFamilySymbol(
                    BuiltInCategory.OST_StructuralFraming, braceTypeName);
                if (!typeResult.Success)
                    return StructuralModelResult.Fail(typeResult.Message);

                var symbol = _doc.GetElement(typeResult.TypeId) as FamilySymbol;
                if (symbol == null)
                    return StructuralModelResult.Fail("No framing family found.");
                _resolver.EnsureActive(symbol);

                var colA = new XYZ(Units.Mm(col1XMm), Units.Mm(col1YMm), level.Elevation);
                var colB = new XYZ(Units.Mm(col2XMm), Units.Mm(col2YMm), level.Elevation);
                double storeyHtFt = Units.Mm(storeyHeightMm);

                var braceLines = BracingPatternEngine.GenerateBraces(
                    colA, colB, storeyHtFt, storeyCount, pattern);

                var createdIds = new List<ElementId>();
                var fh = new ModelFailureHandler();

                using (var tx = new Transaction(_doc, "STING STRUCT: Create Bracing"))
                {
                    var opts = tx.GetFailureHandlingOptions();
                    opts.SetFailuresPreprocessor(fh);
                    tx.SetFailureHandlingOptions(opts);
                    tx.Start();

                    foreach (var (start, end) in braceLines)
                    {
                        if (start.DistanceTo(end) < 0.01) continue;
                        try
                        {
                            var line = Line.CreateBound(start, end);
                            var brace = _doc.Create.NewFamilyInstance(
                                line, symbol, level, StructuralType.Brace);
                            ModelWorksetAssigner.Assign(_doc, brace);
                            createdIds.Add(brace.Id);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Brace member skipped: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                var result = new StructuralModelResult
                {
                    Success = true,
                    BracesCreated = createdIds.Count,
                    Summary = $"Created {pattern} bracing: {createdIds.Count} members " +
                        $"across {storeyCount} storey(s) on {level.Name}",
                    Warnings = fh.CapturedWarnings,
                    CreatedIds = createdIds,
                };
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreateBracing", ex);
                return StructuralModelResult.Fail($"Bracing failed: {ex.Message}");
            }
        }


        // ── Truss ────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a parametric truss from span, depth, and type.
        /// Generates all members (top chord, bottom chord, verticals, diagonals)
        /// as individual structural framing elements.
        /// </summary>
        public StructuralModelResult CreateTruss(
            double startXMm, double startYMm, double startZMm,
            double endXMm, double endYMm, double endZMm,
            TrussType type = TrussType.Pratt,
            double depthMm = 0, int panelCount = 8,
            string memberTypeName = null, string levelName = null)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveFamilySymbol(
                    BuiltInCategory.OST_StructuralFraming, memberTypeName);
                if (!typeResult.Success)
                    return StructuralModelResult.Fail(typeResult.Message);

                var symbol = _doc.GetElement(typeResult.TypeId) as FamilySymbol;
                if (symbol == null)
                    return StructuralModelResult.Fail("No framing family found.");
                _resolver.EnsureActive(symbol);

                var startPt = new XYZ(Units.Mm(startXMm), Units.Mm(startYMm), Units.Mm(startZMm));
                var endPt = new XYZ(Units.Mm(endXMm), Units.Mm(endYMm), Units.Mm(endZMm));

                double spanMm = startPt.DistanceTo(endPt) * Units.FeetToMm;
                if (depthMm <= 0)
                    depthMm = TrussGenerator.RecommendDepthMm(spanMm, type);

                var trussGeom = TrussGenerator.Generate(
                    startPt, endPt, Units.Mm(depthMm), type, panelCount);

                var createdIds = new List<ElementId>();
                var fh = new ModelFailureHandler();

                using (var tx = new Transaction(_doc, "STING STRUCT: Create Truss"))
                {
                    var opts = tx.GetFailureHandlingOptions();
                    opts.SetFailuresPreprocessor(fh);
                    tx.SetFailureHandlingOptions(opts);
                    tx.Start();

                    // Create all truss members
                    var allMembers = new List<(XYZ Start, XYZ End)>();
                    allMembers.AddRange(trussGeom.TopChord);
                    allMembers.AddRange(trussGeom.BottomChord);
                    allMembers.AddRange(trussGeom.Verticals);
                    allMembers.AddRange(trussGeom.Diagonals);

                    foreach (var (mStart, mEnd) in allMembers)
                    {
                        if (mStart.DistanceTo(mEnd) < 0.01) continue;
                        try
                        {
                            var line = Line.CreateBound(mStart, mEnd);
                            var member = _doc.Create.NewFamilyInstance(
                                line, symbol, level, StructuralType.Brace);
                            ModelWorksetAssigner.Assign(_doc, member);
                            createdIds.Add(member.Id);
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Truss member skipped: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                var result = new StructuralModelResult
                {
                    Success = true,
                    TrussesCreated = 1,
                    BeamsCreated = createdIds.Count,
                    Summary = $"Created {type} truss: {createdIds.Count} members " +
                        $"({trussGeom.TopChord.Count} top + {trussGeom.BottomChord.Count} bottom + " +
                        $"{trussGeom.Verticals.Count} verticals + {trussGeom.Diagonals.Count} diagonals). " +
                        $"Span: {spanMm / 1000:F1}m, Depth: {depthMm}mm, Panels: {panelCount}",
                    Warnings = fh.CapturedWarnings,
                    CreatedIds = createdIds,
                };
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreateTruss", ex);
                return StructuralModelResult.Fail($"Truss failed: {ex.Message}");
            }
        }

        // ── Structural Wall ──────────────────────────────────────────────

        /// <summary>
        /// Creates a structural (shear/core/retaining) wall.
        /// </summary>
        public StructuralModelResult CreateStructuralWall(
            double startXMm, double startYMm,
            double endXMm, double endYMm,
            double heightMm = 3600, double thicknessMm = 200,
            string wallTypeName = null, string levelName = null,
            StructuralElementType wallType = StructuralElementType.ShearWall)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveWallType(wallTypeName, thicknessMm);
                if (!typeResult.Success)
                    return StructuralModelResult.Fail(typeResult.Message);

                var startPt = new XYZ(Units.Mm(startXMm), Units.Mm(startYMm), 0);
                var endPt = new XYZ(Units.Mm(endXMm), Units.Mm(endYMm), 0);

                if (startPt.DistanceTo(endPt) < 0.01)
                    return StructuralModelResult.Fail("Wall start and end points are too close.");

                var line = Line.CreateBound(startPt, endPt);

                Wall wall = null;
                using (var tx = new Transaction(_doc, "STING STRUCT: Create Structural Wall"))
                {
                    tx.Start();
                    wall = Wall.Create(_doc, line, typeResult.TypeId,
                        level.Id, Units.Mm(heightMm), 0, false, true); // isStructural = true

                    // Set structural usage parameter
                    var usageParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_USAGE_PARAM);
                    if (usageParam != null && !usageParam.IsReadOnly)
                    {
                        int usage = wallType switch
                        {
                            StructuralElementType.ShearWall => 2,     // Shear
                            StructuralElementType.RetainingWall => 1, // Bearing
                            StructuralElementType.CoreWall => 3,      // Combined
                            _ => 2,
                        };
                        try { usageParam.Set(usage); }
                        catch (Exception ex) { StingLog.Warn($"Wall usage set: {ex.Message}"); }
                    }

                    ModelWorksetAssigner.Assign(_doc, wall);
                    tx.Commit();
                }

                double lengthMm = startPt.DistanceTo(endPt) * Units.FeetToMm;
                var result = new StructuralModelResult
                {
                    Success = true,
                    WallsCreated = 1,
                    Summary = $"Created {wallType} wall: {lengthMm / 1000:F1}m × {heightMm / 1000:F1}m × " +
                        $"{thicknessMm}mm ({typeResult.TypeName}) on {level.Name}",
                };
                result.CreatedIds.Add(wall.Id);
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreateStructuralWall", ex);
                return StructuralModelResult.Fail($"Structural wall failed: {ex.Message}");
            }
        }

        // ── Full Structural Frame from Bay ───────────────────────────────

        /// <summary>
        /// Creates a complete structural frame for a single bay:
        /// 4 columns + edge beams + secondary beams + optional bracing + slab.
        /// One-click complete bay generation.
        /// </summary>
        public StructuralModelResult CreateFullBayFrame(
            double bayXMm, double bayYMm,
            double storeyHeightMm = 3600,
            int storeyCount = 1,
            double originXMm = 0, double originYMm = 0,
            bool addBracing = false,
            BracingPattern bracingPattern = BracingPattern.XBrace,
            bool addSlab = true,
            string columnTypeName = null,
            string beamTypeName = null,
            string levelName = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var totalResult = new StructuralModelResult { Success = true };

            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                // Step 1: Place 4 corner columns for each storey
                var columnPositions = new[]
                {
                    (originXMm, originYMm),
                    (originXMm + bayXMm, originYMm),
                    (originXMm + bayXMm, originYMm + bayYMm),
                    (originXMm, originYMm + bayYMm),
                };

                foreach (var (cx, cy) in columnPositions)
                {
                    var colResult = _modelEngine.PlaceColumn(cx, cy, columnTypeName, levelName);
                    if (colResult.Success)
                    {
                        totalResult.ColumnsCreated++;
                        totalResult.CreatedIds.AddRange(colResult.CreatedElementIds);
                        if (colResult.CreatedElementId != null &&
                            colResult.CreatedElementId != ElementId.InvalidElementId)
                            totalResult.CreatedIds.Add(colResult.CreatedElementId);
                    }
                    else
                    {
                        totalResult.Warnings.Add($"Column at ({cx},{cy}): {colResult.Error ?? colResult.Message}");
                    }
                }

                // Step 2: Create beam system at each storey level
                for (int s = 0; s < storeyCount; s++)
                {
                    double beamHeight = storeyHeightMm * (s + 1);
                    var beamResult = CreateBeamSystem(bayXMm, bayYMm,
                        originXMm, originYMm, beamTypeName, levelName, beamHeight);

                    if (beamResult.Success)
                    {
                        totalResult.BeamsCreated += beamResult.BeamsCreated;
                        totalResult.CreatedIds.AddRange(beamResult.CreatedIds);
                    }
                    else
                    {
                        totalResult.Warnings.Add($"Beam system storey {s + 1}: {beamResult.Summary}");
                    }
                }

                // Step 3: Optional bracing on one face
                if (addBracing)
                {
                    var braceResult = CreateBracing(
                        originXMm, originYMm,
                        originXMm + bayXMm, originYMm,
                        storeyCount, storeyHeightMm, bracingPattern,
                        levelName: levelName);

                    if (braceResult.Success)
                    {
                        totalResult.BracesCreated += braceResult.BracesCreated;
                        totalResult.CreatedIds.AddRange(braceResult.CreatedIds);
                    }
                }

                // Step 4: Optional slab at each level
                if (addSlab)
                {
                    for (int s = 0; s < storeyCount; s++)
                    {
                        var slabResult = CreateStructuralSlab(bayXMm, bayYMm,
                            levelName: levelName,
                            originXMm: originXMm, originYMm: originYMm);

                        if (slabResult.Success)
                        {
                            totalResult.SlabsCreated += slabResult.SlabsCreated;
                            totalResult.CreatedIds.AddRange(slabResult.CreatedIds);
                        }
                    }
                }

                sw.Stop();
                totalResult.Duration = sw.Elapsed;
                totalResult.Summary = $"Created full bay frame: " +
                    $"{totalResult.ColumnsCreated} columns, {totalResult.BeamsCreated} beams" +
                    $"{(totalResult.BracesCreated > 0 ? $", {totalResult.BracesCreated} braces" : "")}" +
                    $"{(totalResult.SlabsCreated > 0 ? $", {totalResult.SlabsCreated} slabs" : "")} " +
                    $"for {bayXMm / 1000:F1}m × {bayYMm / 1000:F1}m × " +
                    $"{storeyCount * storeyHeightMm / 1000:F1}m frame in {sw.Elapsed.TotalSeconds:F1}s";

                return totalResult;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreateFullBayFrame", ex);
                return StructuralModelResult.Fail($"Full bay frame failed: {ex.Message}");
            }
        }


        // ── Multi-Bay Grid Frame ─────────────────────────────────────────

        /// <summary>
        /// Creates a complete structural grid frame with multiple bays.
        /// Generates columns at all grid intersections, beams along grid lines,
        /// and optional bracing on perimeter bays.
        /// </summary>
        public StructuralModelResult CreateGridFrame(
            int baysX, int baysY,
            double baySpacingXMm, double baySpacingYMm,
            double storeyHeightMm = 3600, int storeyCount = 1,
            double originXMm = 0, double originYMm = 0,
            bool perimeterBracing = false,
            BracingPattern bracingPattern = BracingPattern.Chevron,
            string columnTypeName = null, string beamTypeName = null,
            string levelName = null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var totalResult = new StructuralModelResult { Success = true };

            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return StructuralModelResult.Fail("No levels found.");

                int totalCols = (baysX + 1) * (baysY + 1);
                StingLog.Info($"StructuralModelingEngine: Creating {baysX}×{baysY} grid frame " +
                    $"({totalCols} columns, {storeyCount} storeys)");

                // Step 1: Place columns at all grid intersections
                var colResult = _modelEngine.PlaceColumnGrid(
                    baysY + 1, baysX + 1, baySpacingXMm, baySpacingYMm,
                    columnTypeName, levelName, originXMm, originYMm);

                if (colResult.Success)
                {
                    totalResult.ColumnsCreated = colResult.CreatedIds.Count;
                    totalResult.CreatedIds.AddRange(colResult.CreatedIds);
                }
                else
                {
                    totalResult.Warnings.Add($"Column grid: {colResult.Message}");
                }

                // Step 2: Beams along each grid line for each storey
                var beamTypeResult = _resolver.ResolveFamilySymbol(
                    BuiltInCategory.OST_StructuralFraming, beamTypeName);
                if (beamTypeResult.Success)
                {
                    var symbol = _doc.GetElement(beamTypeResult.TypeId) as FamilySymbol;
                    if (symbol != null)
                    {
                        _resolver.EnsureActive(symbol);
                        var fh = new ModelFailureHandler();

                        for (int s = 1; s <= storeyCount; s++)
                        {
                            double z = Units.Mm(storeyHeightMm * s) + level.Elevation;

                            using (var tx = new Transaction(_doc, $"STING STRUCT: Beams Storey {s}"))
                            {
                                var opts = tx.GetFailureHandlingOptions();
                                opts.SetFailuresPreprocessor(fh);
                                tx.SetFailureHandlingOptions(opts);
                                tx.Start();

                                // X-direction beams (along each Y grid line)
                                for (int iy = 0; iy <= baysY; iy++)
                                {
                                    for (int ix = 0; ix < baysX; ix++)
                                    {
                                        double x1 = Units.Mm(originXMm + ix * baySpacingXMm);
                                        double x2 = Units.Mm(originXMm + (ix + 1) * baySpacingXMm);
                                        double y = Units.Mm(originYMm + iy * baySpacingYMm);

                                        try
                                        {
                                            var line = Line.CreateBound(
                                                new XYZ(x1, y, z), new XYZ(x2, y, z));
                                            var beam = _doc.Create.NewFamilyInstance(
                                                line, symbol, level, StructuralType.Beam);
                                            ModelWorksetAssigner.Assign(_doc, beam);
                                            totalResult.CreatedIds.Add(beam.Id);
                                            totalResult.BeamsCreated++;
                                        }
                                        catch (Exception ex)
                                        {
                                            StingLog.Warn($"X-beam ({ix},{iy}) skipped: {ex.Message}");
                                        }
                                    }
                                }

                                // Y-direction beams (along each X grid line)
                                for (int ix = 0; ix <= baysX; ix++)
                                {
                                    for (int iy = 0; iy < baysY; iy++)
                                    {
                                        double x = Units.Mm(originXMm + ix * baySpacingXMm);
                                        double y1 = Units.Mm(originYMm + iy * baySpacingYMm);
                                        double y2 = Units.Mm(originYMm + (iy + 1) * baySpacingYMm);

                                        try
                                        {
                                            var line = Line.CreateBound(
                                                new XYZ(x, y1, z), new XYZ(x, y2, z));
                                            var beam = _doc.Create.NewFamilyInstance(
                                                line, symbol, level, StructuralType.Beam);
                                            ModelWorksetAssigner.Assign(_doc, beam);
                                            totalResult.CreatedIds.Add(beam.Id);
                                            totalResult.BeamsCreated++;
                                        }
                                        catch (Exception ex)
                                        {
                                            StingLog.Warn($"Y-beam ({ix},{iy}) skipped: {ex.Message}");
                                        }
                                    }
                                }

                                tx.Commit();
                            }
                        }

                        totalResult.Warnings.AddRange(fh.CapturedWarnings);
                    }
                }

                // Step 3: Perimeter bracing (one brace frame per perimeter bay)
                if (perimeterBracing)
                {
                    // X-direction perimeter (front and back faces)
                    for (int ix = 0; ix < baysX; ix++)
                    {
                        foreach (int iy in new[] { 0, baysY })
                        {
                            double cx1 = originXMm + ix * baySpacingXMm;
                            double cx2 = originXMm + (ix + 1) * baySpacingXMm;
                            double cy = originYMm + iy * baySpacingYMm;

                            var braceResult = CreateBracing(cx1, cy, cx2, cy,
                                storeyCount, storeyHeightMm, bracingPattern, levelName: levelName);
                            if (braceResult.Success)
                            {
                                totalResult.BracesCreated += braceResult.BracesCreated;
                                totalResult.CreatedIds.AddRange(braceResult.CreatedIds);
                            }
                        }
                    }

                    // Y-direction perimeter (left and right faces)
                    for (int iy = 0; iy < baysY; iy++)
                    {
                        foreach (int ix in new[] { 0, baysX })
                        {
                            double cx = originXMm + ix * baySpacingXMm;
                            double cy1 = originYMm + iy * baySpacingYMm;
                            double cy2 = originYMm + (iy + 1) * baySpacingYMm;

                            var braceResult = CreateBracing(cx, cy1, cx, cy2,
                                storeyCount, storeyHeightMm, bracingPattern, levelName: levelName);
                            if (braceResult.Success)
                            {
                                totalResult.BracesCreated += braceResult.BracesCreated;
                                totalResult.CreatedIds.AddRange(braceResult.CreatedIds);
                            }
                        }
                    }
                }

                sw.Stop();
                totalResult.Duration = sw.Elapsed;
                totalResult.Summary = $"Created {baysX}×{baysY} structural grid frame ({storeyCount} storeys): " +
                    $"{totalResult.ColumnsCreated} columns, {totalResult.BeamsCreated} beams" +
                    $"{(totalResult.BracesCreated > 0 ? $", {totalResult.BracesCreated} braces" : "")} " +
                    $"in {sw.Elapsed.TotalSeconds:F1}s. " +
                    $"Bay: {baySpacingXMm / 1000:F1}m × {baySpacingYMm / 1000:F1}m × {storeyHeightMm / 1000:F1}m";

                return totalResult;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.CreateGridFrame", ex);
                return StructuralModelResult.Fail($"Grid frame failed: {ex.Message}");
            }
        }

        // ── Load Path Analysis ───────────────────────────────────────────

        /// <summary>
        /// Performs load path analysis on existing structural model.
        /// Builds connectivity graph and calculates tributary areas.
        /// </summary>
        public StructuralModelResult AnalyzeLoadPaths()
        {
            try
            {
                var nodes = LoadPathAnalyzer.BuildConnectivityGraph(_doc);
                var columnNodes = nodes.Where(n => n.NodeType == StructuralElementType.Column).ToList();
                var beamNodes = nodes.Where(n => n.NodeType == StructuralElementType.Beam).ToList();
                var fdnNodes = nodes.Where(n =>
                    n.NodeType == StructuralElementType.Footing ||
                    n.NodeType == StructuralElementType.PadFooting ||
                    n.NodeType == StructuralElementType.StripFooting).ToList();

                // Count connections
                int connectedColumns = columnNodes.Count(n => n.ConnectedTo.Count > 0);
                int disconnectedColumns = columnNodes.Count(n => n.ConnectedTo.Count == 0);

                // Check for columns without foundation
                int columnsWithoutFdn = 0;
                foreach (var col in columnNodes)
                {
                    var path = LoadPathAnalyzer.TraceToFoundation(col, nodes);
                    if (path.Count == 0) columnsWithoutFdn++;
                }

                var result = new StructuralModelResult
                {
                    Success = true,
                    Summary = $"Load path analysis: {nodes.Count} nodes " +
                        $"({columnNodes.Count} columns, {beamNodes.Count} beams, {fdnNodes.Count} foundations). " +
                        $"Connected: {connectedColumns}/{columnNodes.Count} columns. " +
                        $"{(columnsWithoutFdn > 0 ? $"WARNING: {columnsWithoutFdn} columns without load path to foundation!" : "All columns have foundation paths.")}",
                };

                if (columnsWithoutFdn > 0)
                    result.Warnings.Add($"{columnsWithoutFdn} columns have no load path to any foundation element.");
                if (disconnectedColumns > 0)
                    result.Warnings.Add($"{disconnectedColumns} columns are completely disconnected from any beam.");

                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("StructuralModelingEngine.AnalyzeLoadPaths", ex);
                return StructuralModelResult.Fail($"Load path analysis failed: {ex.Message}");
            }
        }

        // ── Bay Detection ────────────────────────────────────────────────

        /// <summary>
        /// Detects structural bays from existing columns in the model.
        /// </summary>
        public (List<StructuralBay> Bays, string Summary) DetectExistingBays()
        {
            var columns = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType()
                .ToList();

            var positions = new List<XYZ>();
            foreach (var col in columns)
            {
                var loc = col.Location as LocationPoint;
                if (loc != null) positions.Add(loc.Point);
            }

            var bays = StructuralGridOptimizer.DetectBays(positions);

            string summary = $"Detected {bays.Count} structural bays from {columns.Count} columns.";
            if (bays.Count > 0)
            {
                var avgSpanX = bays.Average(b => b.SpanXFt) * Units.FeetToMm;
                var avgSpanY = bays.Average(b => b.SpanYFt) * Units.FeetToMm;
                summary += $" Avg bay: {avgSpanX / 1000:F1}m × {avgSpanY / 1000:F1}m.";

                int needingSecondary = bays.Count(b => b.NeedsSecondaryBeams);
                if (needingSecondary > 0)
                    summary += $" {needingSecondary} bays need secondary beams (span > 6m).";
            }

            return (bays, summary);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private void TrySetParam(Element el, BuiltInParameter bip, double value)
        {
            try
            {
                var p = el.get_Parameter(bip);
                if (p != null && !p.IsReadOnly) p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"Param set: {ex.Message}"); }
        }

        private void AttachFailureHandler(Transaction tx, ModelFailureHandler fh)
        {
            var opts = tx.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(fh);
            tx.SetFailureHandlingOptions(opts);
        }
    }
}
