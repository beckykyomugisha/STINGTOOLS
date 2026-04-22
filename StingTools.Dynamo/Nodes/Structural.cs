// STING Tools — Structural automation nodes.
// Wraps the Str* command suite (80+ dispatch tags) — 30 highest-value
// operations across creation / auto-sizing / analysis / design /
// detailing / CAD-to-structural / Excel import / carbon.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>
    /// Structural engineering automation — creation, auto-sizing,
    /// analysis, design, detailing, CAD-to-structural, Excel import,
    /// and embodied-carbon assessment.
    /// </summary>
    public static class Structural
    {
        // ── Creation ───────────────────────────────────────────
        /// <summary>Pad foundation under selected columns (EC7).</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreatePadFooting() => StingDispatcher.Dispatch("StrCreatePadFooting");

        /// <summary>Strip footing under a line of walls/columns.</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreateStripFooting() => StingDispatcher.Dispatch("StrCreateStripFooting");

        /// <summary>Structural slab with rebar zones.</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreateStructuralSlab() => StingDispatcher.Dispatch("StrCreateStructuralSlab");

        /// <summary>Structural wall (reinforced concrete or masonry).</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreateStructuralWall() => StingDispatcher.Dispatch("StrCreateStructuralWall");

        /// <summary>Beam system filling a bay between columns.</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreateBeamSystem() => StingDispatcher.Dispatch("StrCreateBeamSystem");

        /// <summary>Lateral bracing (X, chevron, K, knee).</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreateBracing() => StingDispatcher.Dispatch("StrCreateBracing");

        /// <summary>Roof truss (Pratt / Howe / Warren / Fink).</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreateTruss() => StingDispatcher.Dispatch("StrCreateTruss");

        /// <summary>Full bay frame — columns + beams + slab.</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreateFullBayFrame() => StingDispatcher.Dispatch("StrCreateFullBayFrame");

        /// <summary>Multi-storey grid frame across a rectangular footprint.</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool CreateGridFrame() => StingDispatcher.Dispatch("StrCreateGridFrame");

        /// <summary>Auto-foundations under every column.</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool AutoFoundations() => StingDispatcher.Dispatch("StrAutoFoundations");

        /// <summary>Slab edge beams around every floor.</summary>
        [NodeCategory("STING Tools.Structural.Create")]
        public static bool SlabEdgeBeams() => StingDispatcher.Dispatch("StrSlabEdgeBeams");

        // ── Auto-size ──────────────────────────────────────────
        /// <summary>Size every beam + column from loads (EC2 / EC3).</summary>
        [NodeCategory("STING Tools.Structural.AutoSize")]
        public static bool AutoSizeAll() => StingDispatcher.Dispatch("StrAutoSizeAll");

        /// <summary>Grid-spacing optimizer (4m-12m, cost minimising).</summary>
        [NodeCategory("STING Tools.Structural.AutoSize")]
        public static bool GridOptimize() => StingDispatcher.Dispatch("StrGridOptimize");

        /// <summary>EC2 auto-rebar for selected beams/columns.</summary>
        [NodeCategory("STING Tools.Structural.AutoSize")]
        public static bool AutoRebar() => StingDispatcher.Dispatch("StrAutoRebar");

        // ── Analysis ──────────────────────────────────────────
        /// <summary>Column load takedown through the stack.</summary>
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool ColumnLoadTakedown() => StingDispatcher.Dispatch("StrColumnLoadTakedown");

        /// <summary>2D frame analysis on selected members.</summary>
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool FrameAnalysis() => StingDispatcher.Dispatch("StrFrameAnalysis");

        /// <summary>EC2 serviceability deflection check.</summary>
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool DeflectionCheck() => StingDispatcher.Dispatch("StrDeflectionCheck");

        /// <summary>Punching shear check around slab columns.</summary>
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool PunchingShearCheck() => StingDispatcher.Dispatch("StrPunchingShearCheck");

        /// <summary>Wind load per selected code (ASCE 7 / EC 1 / BS 6399).</summary>
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool WindLoad() => StingDispatcher.Dispatch("StrWindLoad");

        /// <summary>Fire resistance per EC2/3 heat transfer model.</summary>
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool FireResistance() => StingDispatcher.Dispatch("StrFireResistance");

        /// <summary>Seismic spectral analysis (ASCE 7 / EC 8).</summary>
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool SeismicAnalysis() => StingDispatcher.Dispatch("StrSeismicAnalysis");

        /// <summary>Progressive collapse (GSA alternate path).</summary>
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool ProgressiveCollapse() => StingDispatcher.Dispatch("StrProgressiveCollapse");

        // ── Design intelligence ───────────────────────────────
        /// <summary>Connection design per EC3 / SCI P358 bolt groups.</summary>
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool ConnectionDesign() => StingDispatcher.Dispatch("StrConnectionDesign");

        /// <summary>Code compliance gate across EC2 / EC3 / EC7.</summary>
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool CodeCompliance() => StingDispatcher.Dispatch("StrCodeCompliance");

        /// <summary>Bar bending schedule per BS 8666.</summary>
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool BarBending() => StingDispatcher.Dispatch("StrBarBending");

        /// <summary>Retaining wall design (stability + bearing + toe kickout).</summary>
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool RetainingWall() => StingDispatcher.Dispatch("StrRetainingWall");

        // ── CAD ingestion ──────────────────────────────────────
        /// <summary>DWG → structural wizard (7-page, full config).</summary>
        [NodeCategory("STING Tools.Structural.CAD")]
        public static bool CADWizard() => StingDispatcher.Dispatch("StructuralDWGWizard");

        /// <summary>Quick one-click DWG → structural.</summary>
        [NodeCategory("STING Tools.Structural.CAD")]
        public static bool QuickDWG() => StingDispatcher.Dispatch("QuickStructuralDWG");

        // ── Excel import ──────────────────────────────────────
        /// <summary>Full Excel → structural import (6 sheet formats).</summary>
        [NodeCategory("STING Tools.Structural.Excel")]
        public static bool ExcelImport() => StingDispatcher.Dispatch("StrExcelImport");

        /// <summary>Export BBS + schedule back to Excel.</summary>
        [NodeCategory("STING Tools.Structural.Excel")]
        public static bool ExcelExport() => StingDispatcher.Dispatch("StrExcelExportSchedule");

        // ── Carbon & sustainability ───────────────────────────
        /// <summary>Embodied carbon assessment (ICE v3 + RICS WLC).</summary>
        [NodeCategory("STING Tools.Structural.Carbon")]
        public static bool CarbonAssessment() => StingDispatcher.Dispatch("StrCarbonAssessment");

        /// <summary>Optimize grid for minimum embodied carbon.</summary>
        [NodeCategory("STING Tools.Structural.Carbon")]
        public static bool CarbonOptimize() => StingDispatcher.Dispatch("StrCarbonOptimize");
    }
}
