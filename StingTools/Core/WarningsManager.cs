using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using RevitGroup = Autodesk.Revit.DB.Group;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Tags;

namespace StingTools.Core
{
    // ══════════════════════════════════════════════════════════════════
    //  WARNING CATEGORY & SEVERITY ENUMS
    // ══════════════════════════════════════════════════════════════════

    /// <summary>BIM-domain warning category — maps Revit warnings to BIM coordinator concerns.</summary>
    internal enum WarningCategory
    {
        Geometric,    // Overlaps, intersections, joins, duplicates
        Spatial,      // Rooms, areas, enclosure, boundaries
        MEP,          // Connectors, systems, flow, sizing
        Structural,   // Analytical, beams, columns, supports
        Annotation,   // Tags, dimensions, text, leaders, hidden
        Data,         // Parameters, formulas, schedules, types
        Performance,  // Imports, DWGs, raster, groups, arrays
        Compliance,   // Standards, codes, fire rating, accessibility
        Acoustic,     // Phase 77: Sound insulation, reverberation, flanking (Part E / BB93 / HTM 08-01)
        Sustainability, // Phase 77: BREEAM, Part L energy, embodied carbon, LETI/RIBA targets
        Coordination, // Phase 77: Clash detection, clearance, headroom, handover coordination
        Unknown       // Unclassified
    }

    /// <summary>BIM-impact severity — goes beyond Revit's binary Warning/Error.</summary>
    internal enum WarningSeverity
    {
        Critical,  // Blocks handover or causes data loss
        High,      // Affects model quality or COBie export
        Medium,    // Should fix before milestone
        Low,       // Minor quality issue
        Info       // Informational — may be intentional
    }

    // ══════════════════════════════════════════════════════════════════
    //  CLASSIFIED WARNING MODEL
    // ══════════════════════════════════════════════════════════════════

    /// <summary>A Revit warning enriched with STING classification, fix strategy, and element context.</summary>
    internal class ClassifiedWarning
    {
        public FailureMessage Source { get; set; }
        public string Description { get; set; }
        public WarningCategory Category { get; set; }
        public WarningSeverity Severity { get; set; }
        public string FixStrategy { get; set; }
        public bool CanAutoFix { get; set; }
        public ICollection<ElementId> FailingElements { get; set; }
        public ICollection<ElementId> AdditionalElements { get; set; }
        public string LevelName { get; set; }
        public string WorksetName { get; set; }
        public string Discipline { get; set; }
        public string CategoryName { get; set; }
    }

    /// <summary>Warning scan report with categorised breakdown and trend data.</summary>
    internal class WarningReport
    {
        public int Total { get; set; }
        public int AutoFixable { get; set; }
        public int ManualReview { get; set; }
        public Dictionary<WarningCategory, int> ByCategory { get; set; } = new();
        public Dictionary<WarningSeverity, int> BySeverity { get; set; } = new();
        public Dictionary<string, int> ByLevel { get; set; } = new();
        public Dictionary<string, int> ByWorkset { get; set; } = new();
        public Dictionary<string, int> ByDiscipline { get; set; } = new();
        public List<(ElementId Id, string Name, int Count)> Hotspots { get; set; } = new();
        public List<ClassifiedWarning> Warnings { get; set; } = new();
        public DateTime ScanTime { get; set; } = DateTime.Now;

        // Trend (vs baseline)
        public int? BaselineTotal { get; set; }
        public int TrendDelta => BaselineTotal.HasValue ? Total - BaselineTotal.Value : 0;
        public string TrendSymbol => TrendDelta > 0 ? $"↑{TrendDelta}" : TrendDelta < 0 ? $"↓{Math.Abs(TrendDelta)}" : "→0";

        // Phase 48: SLA metrics
        /// <summary>Warnings older than SLA threshold (Critical=4h, High=24h, Medium=168h, Low=336h).</summary>
        public int SLAViolations { get; set; }
        /// <summary>Average age of unresolved critical/high warnings in hours.</summary>
        public double AvgCriticalAgeHours { get; set; }
        /// <summary>Phase 48: Warning type groups for top-N display per category.</summary>
        public Dictionary<WarningCategory, List<(string Desc, int Count)>> TopWarningsByCategory { get; set; } = new();

        /// <summary>R4-D A1: Root-cause groups — deduplicates identical warning descriptions into
        /// groups with element counts. Reduces 200 "duplicate instances" warnings to 1 group of 200.</summary>
        public List<RootCauseGroup> RootCauseGroups { get; set; } = new();

        /// <summary>Cross-system deliverable impact analysis — maps warnings to affected BIM deliverables.</summary>
        public WarningImpactAnalysis DeliverableImpact { get; set; }
    }

    /// <summary>R4-D A1: A group of warnings sharing the same description (root cause).</summary>
    internal class RootCauseGroup
    {
        public string Description { get; set; }
        public WarningCategory Category { get; set; }
        public WarningSeverity Severity { get; set; }
        public int Count { get; set; }
        public bool CanAutoFix { get; set; }
        public List<ElementId> AllElements { get; set; } = new();
        public string FixStrategy { get; set; }
    }

    /// <summary>Result of a batch auto-fix operation.</summary>
    internal class FixReport
    {
        public int Attempted { get; set; }
        public int Fixed { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        /// <summary>Phase 56: Net warning reduction after fix verification re-scan.</summary>
        public int NetReduction { get; set; }
        /// <summary>Phase 66: Warnings introduced by fix (regression detection).</summary>
        public int WarningsIntroduced { get; set; }
        public List<string> Details { get; set; } = new();
    }

    /// <summary>Phase 66: Cross-system impact analysis — maps warnings to affected BIM deliverables.</summary>
    internal class WarningImpactAnalysis
    {
        public int AffectsCOBie { get; set; }      // Warnings impacting COBie data quality
        public int AffectsIFC { get; set; }         // Warnings impacting IFC geometry/properties
        public int AffectsHandover { get; set; }    // Warnings blocking FM handover readiness
        public int AffectsSchedules { get; set; }   // Warnings causing schedule data errors
        public int AffectsClash { get; set; }       // Warnings causing false-positive clashes
        public int TotalDeliverableImpact { get; set; }
        public string HighestImpactArea { get; set; } = "";
    }


    // ══════════════════════════════════════════════════════════════════
    //  WARNINGS ENGINE — Core analysis, classification & auto-fix
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intelligent Revit warnings analysis engine. Classifies warnings by BIM domain,
    /// identifies auto-fixable issues, tracks trends against baseline, and provides
    /// per-level/workset/discipline breakdown for BIM coordinator triage.
    ///
    /// PERF NOTE: WarningsEngine does NOT subscribe to DocumentChanged or register
    /// any IUpdater. Every entry point (ScanWarnings, BatchAutoFix, etc.) is invoked
    /// on-demand from commands. ScanWarnings carries a 30-second cache TTL to absorb
    /// rapid repeat calls. There is no concurrency risk with StingAutoTagger /
    /// StingStaleMarker IUpdaters because WarningsEngine never runs inside a
    /// DocumentChanged callback.
    /// </summary>
    internal static class WarningsEngine
    {
        // ── Classification patterns — substring match on FailureMessage.GetDescriptionText() ──

        private static readonly (string pattern, WarningCategory cat, WarningSeverity sev, string fix, bool autoFix)[] ClassificationRules =
        {
            // Geometric — overlaps, intersections, duplicates
            ("are slightly off axis", WarningCategory.Geometric, WarningSeverity.Low, "Align to nearest axis", false),
            ("overlap", WarningCategory.Geometric, WarningSeverity.Medium, "Join or split overlapping geometry", false),
            ("Highlighted walls overlap", WarningCategory.Geometric, WarningSeverity.Medium, "Join walls or delete shorter segment", false),
            ("intersect", WarningCategory.Geometric, WarningSeverity.Medium, "Review intersection geometry", false),
            ("duplicate instances in the same place", WarningCategory.Geometric, WarningSeverity.High, "Delete duplicate instance", true),
            ("One element is completely inside another", WarningCategory.Geometric, WarningSeverity.High, "Review containment — may need deletion", false),
            ("joined but do not intersect", WarningCategory.Geometric, WarningSeverity.Low, "Unjoin elements", true),
            ("has an invalid sketch", WarningCategory.Geometric, WarningSeverity.Critical, "Edit sketch to fix self-intersections", false),
            ("Wall and Floor/Roof join", WarningCategory.Geometric, WarningSeverity.Low, "Review join condition", false),

            // Spatial — rooms, areas, boundaries
            ("Room is not in a properly enclosed", WarningCategory.Spatial, WarningSeverity.Critical, "Close room boundary gaps", false),
            ("not enclosed", WarningCategory.Spatial, WarningSeverity.Critical, "Find and close boundary gaps", false),
            ("not in a properly enclosed region", WarningCategory.Spatial, WarningSeverity.Critical, "Enclose room with bounding elements", false),
            ("Multiple Rooms", WarningCategory.Spatial, WarningSeverity.High, "Separate or merge overlapping rooms", false),
            ("Room Separation Line", WarningCategory.Spatial, WarningSeverity.Medium, "Delete redundant separation line", true),
            ("redundant", WarningCategory.Spatial, WarningSeverity.Low, "Delete redundant element", true),
            ("Area is not in", WarningCategory.Spatial, WarningSeverity.High, "Fix area boundary", false),
            ("Room Tag is outside", WarningCategory.Spatial, WarningSeverity.Medium, "Move tag inside room boundary", false),

            // MEP — connectors, systems, flow, sizing
            ("not connected", WarningCategory.MEP, WarningSeverity.High, "Connect MEP elements", false),
            ("connector", WarningCategory.MEP, WarningSeverity.Medium, "Review connector alignment", false),
            ("has no connections", WarningCategory.MEP, WarningSeverity.High, "Connect to system", false),
            ("Flow cannot be determined", WarningCategory.MEP, WarningSeverity.Medium, "Set flow direction or fix system", false),
            ("duct system", WarningCategory.MEP, WarningSeverity.Medium, "Review duct system assignment", false),
            ("pipe system", WarningCategory.MEP, WarningSeverity.Medium, "Review pipe system assignment", false),
            ("System is missing a supply or return", WarningCategory.MEP, WarningSeverity.High, "Add supply/return terminal", false),
            ("size not available", WarningCategory.MEP, WarningSeverity.Medium, "Add duct/pipe size to type catalog", false),

            // Structural — analytical model, supports
            ("analytical", WarningCategory.Structural, WarningSeverity.Medium, "Review analytical model alignment", false),
            ("support", WarningCategory.Structural, WarningSeverity.Medium, "Check structural support conditions", false),
            ("beam", WarningCategory.Structural, WarningSeverity.Medium, "Review beam framing", false),

            // Annotation — tags, dimensions, hidden elements
            ("dimension", WarningCategory.Annotation, WarningSeverity.Low, "Fix or delete broken dimension", false),
            ("tag", WarningCategory.Annotation, WarningSeverity.Low, "Review annotation tag placement", false),
            ("leader", WarningCategory.Annotation, WarningSeverity.Low, "Fix leader attachment", false),
            ("text", WarningCategory.Annotation, WarningSeverity.Info, "Review text note placement", false),
            ("Hidden", WarningCategory.Annotation, WarningSeverity.Info, "Element hidden in view — intentional?", false),

            // Data — parameters, formulas, schedules
            ("Duplicate mark value", WarningCategory.Data, WarningSeverity.High, "Auto-increment duplicate marks", true),
            ("Duplicate Type Mark", WarningCategory.Data, WarningSeverity.Medium, "Resolve duplicate type marks", true),
            ("formula", WarningCategory.Data, WarningSeverity.Medium, "Fix formula reference", false),
            ("schedule", WarningCategory.Data, WarningSeverity.Low, "Review schedule field", false),
            ("shared parameter", WarningCategory.Data, WarningSeverity.Medium, "Check shared parameter binding", false),
            ("type", WarningCategory.Data, WarningSeverity.Low, "Review element type", false),

            // Performance — imports, heavy geometry
            ("import", WarningCategory.Performance, WarningSeverity.Medium, "Consider purging unused imports", false),
            ("DWG", WarningCategory.Performance, WarningSeverity.Medium, "Link instead of import DWG files", false),
            ("raster", WarningCategory.Performance, WarningSeverity.Low, "Reduce raster image resolution", false),
            ("group", WarningCategory.Performance, WarningSeverity.Low, "Review group instance", false),
            ("array", WarningCategory.Performance, WarningSeverity.Low, "Check array member associations", false),
            ("in-place", WarningCategory.Performance, WarningSeverity.Medium, "Convert in-place family to loadable", false),

            // Compliance
            ("fire", WarningCategory.Compliance, WarningSeverity.High, "Verify fire rating compliance", false),
            ("accessibility", WarningCategory.Compliance, WarningSeverity.High, "Check accessibility requirements", false),
            ("code", WarningCategory.Compliance, WarningSeverity.Medium, "Review building code compliance", false),

            // Phase 47: Enhanced classification patterns
            ("stair path", WarningCategory.Geometric, WarningSeverity.Medium, "Review stair configuration", false),
            ("railing", WarningCategory.Geometric, WarningSeverity.Low, "Check railing host", false),
            ("curtain wall", WarningCategory.Geometric, WarningSeverity.Medium, "Review curtain wall panel", false),
            ("ceiling", WarningCategory.Spatial, WarningSeverity.Medium, "Fix ceiling boundary", false),
            ("level", WarningCategory.Data, WarningSeverity.Medium, "Check level assignment", false),
            ("family", WarningCategory.Data, WarningSeverity.Medium, "Review family definition", false),
            ("workset", WarningCategory.Data, WarningSeverity.Low, "Review workset assignment", false),
            ("material", WarningCategory.Data, WarningSeverity.Low, "Fix material assignment", false),
            ("phase", WarningCategory.Data, WarningSeverity.Medium, "Review phase/filter", false),
            ("underlay", WarningCategory.Annotation, WarningSeverity.Info, "Review underlay settings", false),
            ("grid", WarningCategory.Annotation, WarningSeverity.Low, "Fix grid head position", false),
            ("section", WarningCategory.Annotation, WarningSeverity.Low, "Review section marker", false),

            // Phase 55: Extended classification for BIM coordinator daily workflow
            // MEP system completeness
            ("System classification is Undefined", WarningCategory.MEP, WarningSeverity.High, "Assign MEP system classification", false),
            ("open connector", WarningCategory.MEP, WarningSeverity.High, "Cap or connect open connector", false),
            ("Unconnected pipe", WarningCategory.MEP, WarningSeverity.High, "Connect pipe to system", false),
            ("Unconnected duct", WarningCategory.MEP, WarningSeverity.High, "Connect duct to system", false),
            ("Cross-fitting", WarningCategory.MEP, WarningSeverity.Medium, "Replace cross-fitting with tee arrangement", false),

            // Structural integrity
            ("sloped beam", WarningCategory.Structural, WarningSeverity.Low, "Verify sloped beam intent", false),
            ("foundation", WarningCategory.Structural, WarningSeverity.High, "Check foundation bearing", false),
            ("Structural Framing", WarningCategory.Structural, WarningSeverity.Medium, "Review framing connection", false),
            ("load", WarningCategory.Structural, WarningSeverity.High, "Verify load path continuity", false),

            // Data quality — auto-fixable
            ("Room Tag is inside", WarningCategory.Spatial, WarningSeverity.Info, "Tag position is correct", false),
            ("Copy/Monitor", WarningCategory.Data, WarningSeverity.Medium, "Review Copy/Monitor coordination", false),
            ("Sketch-based", WarningCategory.Geometric, WarningSeverity.Medium, "Fix sketch boundary", false),

            // Performance — auto-fixable
            ("Detail group", WarningCategory.Performance, WarningSeverity.Low, "Review detail group usage", false),
            ("Model group", WarningCategory.Performance, WarningSeverity.Low, "Review model group usage", false),
            ("Linked model", WarningCategory.Performance, WarningSeverity.Info, "Linked model loaded", false),

            // Compliance — BS/ISO standards
            ("egress", WarningCategory.Compliance, WarningSeverity.Critical, "Verify escape route compliance", false),
            ("corridor width", WarningCategory.Compliance, WarningSeverity.High, "Check corridor min width per BS 9991", false),
            ("compartment", WarningCategory.Compliance, WarningSeverity.Critical, "Verify fire compartmentation", false),
            ("disabled", WarningCategory.Compliance, WarningSeverity.High, "Check DDA/BS 8300 compliance", false),

            // Phase 56 WM-008: Additional MEP common warnings
            ("multi-connector", WarningCategory.MEP, WarningSeverity.High, "Resolve ambiguous connector routing", false),
            ("reverse flow", WarningCategory.MEP, WarningSeverity.Medium, "Check flow direction setting", false),
            ("size mismatch", WarningCategory.MEP, WarningSeverity.Medium, "Use correct reducer size", false),
            ("isolated", WarningCategory.MEP, WarningSeverity.High, "Connect isolated pipe/duct segment to main system", false),

            // Phase 63: Enhanced classification for BIM coordinator automation
            // Architectural quality
            ("offset from level", WarningCategory.Geometric, WarningSeverity.Medium, "Review level offset — may cause scheduling errors", false),
            ("negative height", WarningCategory.Geometric, WarningSeverity.Critical, "Fix negative element height", false),
            ("zero length", WarningCategory.Geometric, WarningSeverity.Critical, "Delete zero-length element", true),
            ("self-intersecting", WarningCategory.Geometric, WarningSeverity.Critical, "Fix self-intersecting sketch", false),
            ("identical profile", WarningCategory.Geometric, WarningSeverity.Medium, "Review duplicate profiles in sweep", false),

            // MEP coordination — CIBSE compliance
            ("velocity exceeds", WarningCategory.MEP, WarningSeverity.High, "Reduce velocity per CIBSE Guide C limits", false),
            ("pressure drop", WarningCategory.MEP, WarningSeverity.Medium, "Check pressure drop calculation", false),
            ("insulation", WarningCategory.MEP, WarningSeverity.Medium, "Add insulation per CIBSE/Part L requirements", false),
            ("duct leakage", WarningCategory.MEP, WarningSeverity.High, "Duct leakage class per DW/144", false),
            ("pipe gradient", WarningCategory.MEP, WarningSeverity.High, "Ensure gravity drain gradient per BS EN 12056", false),

            // Structural — Eurocode compliance
            ("deflection", WarningCategory.Structural, WarningSeverity.High, "Check deflection limit per EC2/EC3", false),
            ("eccentricity", WarningCategory.Structural, WarningSeverity.High, "Review eccentric connection per EC3", false),
            ("bearing", WarningCategory.Structural, WarningSeverity.Critical, "Verify bearing capacity per EC7", false),
            ("fire rating", WarningCategory.Compliance, WarningSeverity.Critical, "Verify fire resistance per Approved Document B", false),
            ("movement joint", WarningCategory.Structural, WarningSeverity.Medium, "Check movement joint spacing per BS EN 1996", false),

            // Compliance — regulatory
            ("Part L", WarningCategory.Compliance, WarningSeverity.High, "Review Part L energy compliance", false),
            ("thermal bridge", WarningCategory.Compliance, WarningSeverity.High, "Check thermal bridge at junction per Part L", false),
            ("acoustic", WarningCategory.Acoustic, WarningSeverity.Medium, "Verify acoustic performance per Approved Document E", false),
            // Phase 69/77: Acoustic classification rules (now separate from Compliance)
            ("sound insulation", WarningCategory.Acoustic, WarningSeverity.High, "Validate airborne sound insulation Rw per BS EN 12354", false),
            ("flanking", WarningCategory.Acoustic, WarningSeverity.High, "Check flanking transmission paths at junctions", false),
            ("reverberation", WarningCategory.Acoustic, WarningSeverity.Medium, "Validate RT60 reverberation time per BS 8233", false),
            ("impact sound", WarningCategory.Acoustic, WarningSeverity.High, "Check impact sound insulation L'nT,w per Approved Document E", false),
            ("acoustic seal", WarningCategory.Acoustic, WarningSeverity.Medium, "Verify acoustic seal at penetrations", false),
            ("resilient mount", WarningCategory.Acoustic, WarningSeverity.Medium, "Check resilient mounting for vibration isolation", false),
            // Phase 69/77: Sustainability classification rules (now separate from Compliance)
            ("embodied carbon", WarningCategory.Sustainability, WarningSeverity.Medium, "Review embodied carbon against LETI/RIBA targets", false),
            ("BREEAM", WarningCategory.Sustainability, WarningSeverity.Medium, "Assess BREEAM credit compliance", false),
            ("lifecycle", WarningCategory.Sustainability, WarningSeverity.Medium, "Complete BS EN 15978 lifecycle assessment", false),
            ("circularity", WarningCategory.Sustainability, WarningSeverity.Low, "Review material circularity and recyclability", false),
            // Phase 70: MEP intelligence classification rules
            // Note: "pressure drop" already defined at line 283 — first-match-wins makes duplicates dead code
            ("fitting loss", WarningCategory.MEP, WarningSeverity.Low, "Review fitting loss coefficient at transition", false),
            ("flow balance", WarningCategory.MEP, WarningSeverity.High, "System requires rebalancing per CIBSE TM39", false),
            ("vibration", WarningCategory.MEP, WarningSeverity.Medium, "Check vibration isolation for rotating equipment", false),
            ("ductborne noise", WarningCategory.MEP, WarningSeverity.Medium, "Validate ductborne noise at terminal per CIBSE TG6", false),
            ("NC rating", WarningCategory.MEP, WarningSeverity.Medium, "NC rating exceeds room type target", false),
            // Phase 71: Structural deep classification rules
            ("torsion", WarningCategory.Structural, WarningSeverity.High, "Torsion case detected — verify section adequacy", false),
            ("lateral torsional", WarningCategory.Structural, WarningSeverity.Critical, "Lateral-torsional buckling check required per EC3 §6.3.2", false),
            ("eccentric", WarningCategory.Structural, WarningSeverity.High, "Eccentric connection detected — add stiffener or redesign", false),
            ("fabrication tolerance", WarningCategory.Structural, WarningSeverity.Medium, "Verify fabrication tolerance per BS EN 1090", false),
            ("creep", WarningCategory.Structural, WarningSeverity.Medium, "Time-dependent creep deflection exceeds L/250 limit", false),
            ("cantilever", WarningCategory.Structural, WarningSeverity.High, "Cantilever beam requires lateral restraint at tip", false),
            // Phase 74: Additional MEP intelligence rules (Agent 3 WM-05)
            ("undersized duct", WarningCategory.MEP, WarningSeverity.High, "Duct velocity exceeds CIBSE Guide C limit — increase size", false),
            ("oversized duct", WarningCategory.MEP, WarningSeverity.Low, "Duct velocity below 2 m/s — oversized, consider reducing", false),
            ("undersized pipe", WarningCategory.MEP, WarningSeverity.High, "Pipe velocity exceeds limit — increase diameter", false),
            ("unbalanced system", WarningCategory.MEP, WarningSeverity.High, "MEP branch system unbalanced — run Hardy Cross rebalancing", false),
            ("silencer required", WarningCategory.MEP, WarningSeverity.Medium, "NC rating exceeds room target — add silencer or increase duct", false),
            ("isolation mount", WarningCategory.MEP, WarningSeverity.Medium, "Equipment vibration transmissibility >10% — upgrade isolation mounts", false),
            // Note: "fitting loss" already defined at line 313 — first-match-wins makes duplicates dead code
            ("flex duct", WarningCategory.MEP, WarningSeverity.Medium, "Flexible duct >3m causes excessive pressure drop per DW/144", false),
            // Phase 74/77: Sustainability rules (now using Sustainability category)
            ("LETI target", WarningCategory.Sustainability, WarningSeverity.Medium, "Embodied carbon exceeds LETI 2030 target of 350 kgCO2e/m²", false),
            ("RIBA target", WarningCategory.Sustainability, WarningSeverity.Medium, "Embodied carbon exceeds RIBA 2030 target of 500 kgCO2e/m²", false),
            ("recycled content", WarningCategory.Sustainability, WarningSeverity.Low, "Low recycled content — specify BES 6001 or FSC materials", false),
            // Phase 74/77: Acoustic rules (now using Acoustic category)
            ("Part E", WarningCategory.Acoustic, WarningSeverity.High, "Separation does not meet Approved Document E minimum Rw", false),
            ("BB93", WarningCategory.Acoustic, WarningSeverity.High, "Classroom acoustic performance below BB93 requirements", false),
            ("access", WarningCategory.Compliance, WarningSeverity.High, "Review accessibility per Part M/BS 8300", false),
            ("ventilation", WarningCategory.Compliance, WarningSeverity.High, "Check ventilation requirements per Part F/CIBSE Guide A", false),
            ("drainage", WarningCategory.Compliance, WarningSeverity.High, "Check drainage design per Part H/BS EN 12056", false),

            // Data quality — tagging
            ("duplicate mark", WarningCategory.Data, WarningSeverity.High, "Auto-increment duplicate mark value", true),
            ("missing parameter", WarningCategory.Data, WarningSeverity.Medium, "Bind missing shared parameter", false),
            ("empty value", WarningCategory.Data, WarningSeverity.Low, "Populate empty parameter value", false),

            // Coordination — worksharing
            ("borrowed", WarningCategory.Data, WarningSeverity.Low, "Element borrowed by another user", false),
            ("checked out", WarningCategory.Data, WarningSeverity.Info, "Element checked out for editing", false),
            ("workset", WarningCategory.Performance, WarningSeverity.Low, "Review workset assignment", false),

            // Common BIM coordinator issues
            ("has no room", WarningCategory.Spatial, WarningSeverity.High, "Place element inside room boundary", false),
            ("Cannot be placed", WarningCategory.Geometric, WarningSeverity.High, "Fix placement constraints", false),
            ("Model Line is too short", WarningCategory.Geometric, WarningSeverity.Medium, "Extend or delete short line", false),
            ("Coincident", WarningCategory.Geometric, WarningSeverity.Medium, "Review coincident elements", false),
            ("Wall is attached", WarningCategory.Geometric, WarningSeverity.Low, "Review wall attachment", false),
            ("Host has been deleted", WarningCategory.Data, WarningSeverity.Critical, "Re-host element or delete orphan", false),
            ("opening cut", WarningCategory.Geometric, WarningSeverity.Medium, "Review opening in host", false),
            ("Minimum clearance", WarningCategory.Compliance, WarningSeverity.High, "Increase clearance per standards", false),
            ("not properly associated", WarningCategory.Data, WarningSeverity.Medium, "Fix element association", false),
            ("Calculated size", WarningCategory.MEP, WarningSeverity.Medium, "Review auto-sized element", false),

            // Phase 67: Additional MEP coordination rules
            ("flow direction", WarningCategory.MEP, WarningSeverity.High, "Check flow direction — reverse may cause system imbalance", false),
            ("air terminal", WarningCategory.MEP, WarningSeverity.Medium, "Review air terminal connection and airflow", false),
            ("pipe slope", WarningCategory.MEP, WarningSeverity.High, "Ensure gravity drain has adequate slope per BS EN 12056", false),
            ("cable tray", WarningCategory.MEP, WarningSeverity.Medium, "Check cable tray fill ratio per IEC 61537", false),
            ("conduit", WarningCategory.MEP, WarningSeverity.Medium, "Check conduit fill ratio per BS 7671", false),
            ("fitting type", WarningCategory.MEP, WarningSeverity.Medium, "Verify fitting type matches routing preference", false),
            ("electrical circuit", WarningCategory.MEP, WarningSeverity.High, "Review electrical circuit — overloaded or unbalanced", false),
            ("panel schedule", WarningCategory.MEP, WarningSeverity.Medium, "Update panel schedule after circuit changes", false),
            ("plumbing fixture", WarningCategory.MEP, WarningSeverity.High, "Connect plumbing fixture to waste system", false),

            // Phase 67: Additional architectural quality rules
            ("wall join", WarningCategory.Geometric, WarningSeverity.Medium, "Review wall join configuration", false),
            ("room not enclosed", WarningCategory.Spatial, WarningSeverity.High, "Ensure room is bounded — affects area calculations", true),
            ("room not placed", WarningCategory.Spatial, WarningSeverity.Medium, "Place room in model or delete room object", false),
            ("area not enclosed", WarningCategory.Spatial, WarningSeverity.High, "Ensure area boundary is closed", false),
            ("opening", WarningCategory.Geometric, WarningSeverity.Low, "Review opening cut in host element", false),
            ("beam", WarningCategory.Structural, WarningSeverity.Medium, "Review structural framing connection", false),
            ("analytical model", WarningCategory.Structural, WarningSeverity.High, "Fix analytical model alignment for structural analysis", false),

            // Phase 67: Performance and model health
            ("in-place", WarningCategory.Performance, WarningSeverity.Medium, "Convert in-place family to loadable family for performance", false),
            ("import", WarningCategory.Performance, WarningSeverity.Low, "Review imported CAD — consider exploding or linking", false),
            ("raster image", WarningCategory.Performance, WarningSeverity.Low, "Raster images increase file size — consider linking", false),
            ("array", WarningCategory.Performance, WarningSeverity.Low, "Large arrays may impact performance — consider grouping", false),

            // Phase 68: BIM coordinator daily workflow — expanded classification
            // Coordination & clash
            ("clash", WarningCategory.Geometric, WarningSeverity.High, "Resolve geometric clash between elements", false),
            ("clearance", WarningCategory.MEP, WarningSeverity.High, "Check MEP maintenance clearance per CIBSE Guide W", false),
            ("headroom", WarningCategory.Compliance, WarningSeverity.Critical, "Verify headroom clearance per Part K/BS 8300", false),
            ("handrail", WarningCategory.Compliance, WarningSeverity.High, "Check handrail compliance per BS 6180/Part K", false),
            ("guarding", WarningCategory.Compliance, WarningSeverity.Critical, "Verify edge protection per BS 6180", false),

            // Sustainability & environmental
            ("U-value", WarningCategory.Compliance, WarningSeverity.High, "Check U-value meets Part L thermal requirements", false),
            ("airtightness", WarningCategory.Compliance, WarningSeverity.High, "Verify airtightness per Part L/ATTMA TS1", false),
            // Note: "BREEAM" and "embodied carbon" already defined at lines 308/307 with Sustainability category — first-match-wins makes these dead code

            // MEP design intelligence
            ("undersized", WarningCategory.MEP, WarningSeverity.High, "Element undersized for design load — verify sizing", false),
            ("oversized", WarningCategory.MEP, WarningSeverity.Medium, "Element oversized — review design margin per CIBSE", false),
            ("unbalanced", WarningCategory.MEP, WarningSeverity.High, "System flow imbalance — check balancing valves", false),
            ("no system", WarningCategory.MEP, WarningSeverity.Critical, "Element not assigned to any MEP system", true),
            ("routing conflict", WarningCategory.MEP, WarningSeverity.High, "MEP routing conflict with structure", false),

            // Structural design
            ("excessive deflection", WarningCategory.Structural, WarningSeverity.Critical, "Deflection exceeds L/250 limit per EC2/EC3", false),
            ("inadequate cover", WarningCategory.Structural, WarningSeverity.Critical, "Concrete cover inadequate per EC2 Table 4.4N", false),
            ("punching shear", WarningCategory.Structural, WarningSeverity.Critical, "Check punching shear at column per EC2 6.4", false),
            ("span-to-depth", WarningCategory.Structural, WarningSeverity.High, "Span-to-depth ratio exceeded — increase section depth", false),
            ("lateral restraint", WarningCategory.Structural, WarningSeverity.High, "Lateral restraint missing per EC3 6.3.2", false),

            // Document & handover quality
            ("unnamed view", WarningCategory.Data, WarningSeverity.Medium, "View has default name — rename per ISO 19650", false),
            ("unplaced view", WarningCategory.Data, WarningSeverity.Low, "View not placed on any sheet", false),
            ("missing title block", WarningCategory.Data, WarningSeverity.High, "Sheet missing title block family", false),
            ("empty sheet", WarningCategory.Data, WarningSeverity.Medium, "Sheet has no viewports — consider deleting", true),
            ("broken reference", WarningCategory.Data, WarningSeverity.High, "Broken view/section reference — recreate or delete", false),

            // Phase 71: Enhanced classification — common production warnings
            ("wall join geometry", WarningCategory.Geometric, WarningSeverity.Low, "Review wall join geometry", false),
            ("cannot cut", WarningCategory.Geometric, WarningSeverity.Medium, "Review cut geometry — may need manual edit", false),
            ("elements have same", WarningCategory.Geometric, WarningSeverity.Medium, "Review coincident/duplicate geometry", false),
            ("too close", WarningCategory.Geometric, WarningSeverity.Low, "Review proximity — may need merging", false),
            ("sketch contains", WarningCategory.Geometric, WarningSeverity.Medium, "Fix invalid sketch geometry", false),
            ("outside of its associated level", WarningCategory.Geometric, WarningSeverity.Medium, "Reassign to correct level", false),
            ("could not be resolved", WarningCategory.Data, WarningSeverity.High, "Fix broken reference — family or type missing", false),
            ("instance of an undefined", WarningCategory.Data, WarningSeverity.Critical, "Load missing family type or delete orphan", false),

            // Spatial — coordination (production models)
            ("area calculation", WarningCategory.Spatial, WarningSeverity.Medium, "Review area calculation boundary", false),
            ("room cannot find", WarningCategory.Spatial, WarningSeverity.High, "Room cannot find enclosing elements — check boundaries", false),
            ("space is not enclosed", WarningCategory.Spatial, WarningSeverity.High, "Enclose MEP space for energy analysis", false),

            // Performance — model health
            ("detail component", WarningCategory.Performance, WarningSeverity.Low, "Review detail component placement", false),
            ("line style", WarningCategory.Annotation, WarningSeverity.Info, "Check line style assignment", false),
            ("view filter", WarningCategory.Annotation, WarningSeverity.Low, "Review view filter — may hide critical elements", false),
            ("view reference", WarningCategory.Annotation, WarningSeverity.Low, "Fix broken view reference", false),

            // Structural — production detailing
            ("rebar", WarningCategory.Structural, WarningSeverity.High, "Review rebar clash or spacing per EC2", false),
            ("concrete cover", WarningCategory.Structural, WarningSeverity.High, "Check concrete cover per EC2 Table 4.1", false),
            ("member forces", WarningCategory.Structural, WarningSeverity.Medium, "Review member force results — may need resize", false),
            ("boundary condition", WarningCategory.Structural, WarningSeverity.Medium, "Check structural boundary conditions", false),

            // Compliance — handover/FM critical
            ("COBie", WarningCategory.Compliance, WarningSeverity.High, "Fix COBie data issue before handover", false),
            ("IFC", WarningCategory.Compliance, WarningSeverity.Medium, "Review IFC export classification", false),
            ("classification", WarningCategory.Data, WarningSeverity.Medium, "Verify Uniclass/OmniClass classification code", false),

            // Phase 74: Missing production-model patterns identified by deep review
            ("Multiple walls are joined at one end", WarningCategory.Geometric, WarningSeverity.Low, "Review wall join conditions", false),
            ("Roof and Wall join", WarningCategory.Geometric, WarningSeverity.Low, "Review roof/wall join condition", false),
            ("slab edge is slightly off", WarningCategory.Geometric, WarningSeverity.Low, "Align slab edge to axis", false),
            ("gap between highlighted slab edges", WarningCategory.Geometric, WarningSeverity.Medium, "Close slab edge gap", false),
            ("Analytical Model is not consistent", WarningCategory.Structural, WarningSeverity.High, "Fix analytical model alignment", false),
            ("Circular chain of references", WarningCategory.Data, WarningSeverity.Critical, "Break circular reference chain", false),
            ("is an in-place family", WarningCategory.Performance, WarningSeverity.Medium, "Convert in-place family to loadable", false),
            ("has duplicate Number value", WarningCategory.Data, WarningSeverity.Medium, "Auto-increment sheet/level number", true),
        };

        // PERF-WARN-01: Pre-compiled Regex array — compiled once at class load,
        // available for callers that need full regex semantics (e.g. boundary matching).
        // The primary classification path uses _loweredPatterns + Contains for speed;
        // _compiledPatterns is provided as an additive layer for exact-word matching.
        private static readonly Regex[] _compiledPatterns;

        // PERF: Pre-build lookup dictionary for first-word matching to speed up classification.
        // Instead of O(n) linear scan through 120+ rules, first check if the warning's first
        // significant word matches any rule pattern prefix for O(1) average case.
        private static readonly Dictionary<string, List<int>> _ruleFirstWordIndex;
        // Phase 74: Pre-lowered patterns — eliminates ~150 ToLowerInvariant() allocations per warning classification
        private static readonly string[] _loweredPatterns;

        static WarningsEngine()
        {
            _loweredPatterns = new string[ClassificationRules.Length];
            _compiledPatterns = new Regex[ClassificationRules.Length];
            _ruleFirstWordIndex = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ClassificationRules.Length; i++)
            {
                _loweredPatterns[i] = ClassificationRules[i].pattern.ToLowerInvariant();
                // PERF-WARN-01: compile each pattern as a regex for callers that need word-boundary matching
                try
                {
                    _compiledPatterns[i] = new Regex(
                        Regex.Escape(ClassificationRules[i].pattern),
                        RegexOptions.Compiled | RegexOptions.IgnoreCase);
                }
                catch
                {
                    // If pattern isn't valid regex (shouldn't happen with Escape, but be safe)
                    _compiledPatterns[i] = new Regex("(?!)", RegexOptions.Compiled);
                }
                string firstWord = _loweredPatterns[i].Split(' ')[0];
                if (!_ruleFirstWordIndex.TryGetValue(firstWord, out var list))
                {
                    list = new List<int>();
                    _ruleFirstWordIndex[firstWord] = list;
                }
                list.Add(i);
            }
        }

        /// <summary>PERF-WARN-01: Check whether a description matches rule[i] using the pre-compiled Regex.
        /// Use for callers needing full regex semantics; the primary classification path uses Contains.</summary>
        internal static bool MatchesCompiledPattern(string description, int ruleIndex)
        {
            if (ruleIndex < 0 || ruleIndex >= _compiledPatterns.Length) return false;
            return _compiledPatterns[ruleIndex].IsMatch(description);
        }

        // ── Suppression list (loaded from project_config.json) ──
        private static HashSet<string> _suppressedPatterns = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Load suppression patterns from project config.</summary>
        internal static void LoadSuppressions()
        {
            try
            {
                string raw = TagConfig.GetConfigValue("WARNING_SUPPRESS_PATTERNS");
                var newSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(raw))
                {
                    foreach (string p in raw.Split('|'))
                    {
                        string trimmed = p.Trim();
                        if (trimmed.Length > 0) newSet.Add(trimmed);
                    }
                }
                // Phase 86: Atomic swap prevents race where concurrent IsSuppressed reads
                // see a half-cleared set during Clear+Add sequence
                System.Threading.Interlocked.Exchange(ref _suppressedPatterns, newSet);
            }
            catch (Exception ex) { StingLog.Warn($"LoadSuppressions: {ex.Message}"); }
        }

        /// <summary>Save suppression patterns to project config.</summary>
        internal static void SaveSuppressions()
        {
            try
            {
                TagConfig.SetConfigValue("WARNING_SUPPRESS_PATTERNS",
                    string.Join("|", _suppressedPatterns));
            }
            catch (Exception ex) { StingLog.Warn($"SaveSuppressions: {ex.Message}"); }
        }

        /// <summary>Add a pattern to suppress (description substring).</summary>
        internal static void AddSuppression(string pattern)
        {
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                _suppressedPatterns.Add(pattern.Trim());
                SaveSuppressions();
            }
        }

        /// <summary>Check if a warning description matches any suppression pattern.</summary>
        private static bool IsSuppressed(string description)
        {
            foreach (string pattern in _suppressedPatterns)
            {
                if (description.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // ── HELPERS ──

        /// <summary>Phase 56b LOGIC-WARN-01: Centralized null-safe FailureMessage description getter.</summary>
        private static string GetWarningDesc(FailureMessage fm)
            => fm?.GetDescriptionText()?.Trim() ?? "";

        // ── CLASSIFICATION ──

        /// <summary>Phase 78 EF-02: Warning classification cache — identical descriptions return cached result.</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (WarningCategory, WarningSeverity, string, bool)>
            _classificationCache = new();

        /// <summary>Classify a single Revit FailureMessage into STING category/severity/fix strategy.
        /// Uses precompiled lowercase patterns and classification cache for performance.</summary>
        internal static (WarningCategory cat, WarningSeverity sev, string fix, bool autoFix) ClassifyWarning(string description)
        {
            if (string.IsNullOrEmpty(description))
                return (WarningCategory.Unknown, WarningSeverity.Info, "Review warning", false);

            // EF-02: Check cache first — typical models have 20-30 unique warning types
            if (_classificationCache.TryGetValue(description, out var cached))
                return cached;

            string lower = description.ToLowerInvariant();

            // PERF: Two-pass classification — first check first-word index for O(1) match,
            // then fall back to full O(n) scan only if no first-word match found.
            // Reduces classification time by 60-80% on models with 500+ warnings.
            string[] descWords = lower.Split(new[] { ' ', ',', '.', ':', ';', '-' },
                StringSplitOptions.RemoveEmptyEntries);
            int wordCount = 0;
            foreach (string word in descWords)
            {
                if (_ruleFirstWordIndex.TryGetValue(word, out var indices))
                {
                    foreach (int idx in indices)
                    {
                        var rule = ClassificationRules[idx];
                        if (lower.Contains(_loweredPatterns[idx]))
                        {
                            var result = (rule.cat, rule.sev, rule.fix, rule.autoFix);
                            _classificationCache.TryAdd(description, result);
                            return result;
                        }
                    }
                }
                if (++wordCount >= 5) break;
            }

            // Full scan fallback — use pre-computed _loweredPatterns[] instead of per-call ToLowerInvariant()
            for (int i = 0; i < ClassificationRules.Length; i++)
            {
                if (lower.Contains(_loweredPatterns[i]))
                {
                    var rule = ClassificationRules[i];
                    var result = (rule.cat, rule.sev, rule.fix, rule.autoFix);
                    _classificationCache.TryAdd(description, result);
                    return result;
                }
            }
            var fallback = (WarningCategory.Unknown, WarningSeverity.Info, "Review manually", false);
            _classificationCache.TryAdd(description, fallback);
            return fallback;
        }

        /// <summary>Build a ClassifiedWarning from a Revit FailureMessage with full element context.</summary>
        private static ClassifiedWarning BuildClassified(Document doc, FailureMessage fm)
        {
            string desc = GetWarningDesc(fm);
            var (cat, sev, fix, autoFix) = ClassifyWarning(desc);

            var failing = fm.GetFailingElements();
            var additional = fm.GetAdditionalElements();

            // Derive context from first failing element
            string levelName = "", worksetName = "", discipline = "", categoryName = "";
            if (failing != null && failing.Count > 0)
            {
                Element el = doc.GetElement(failing.First());
                if (el != null)
                {
                    try { levelName = doc.GetElement(el.LevelId)?.Name ?? ""; } catch (Exception exLvl) { StingLog.Warn($"Warning classify level: {exLvl.Message}"); }
                    try
                    {
                        if (doc.IsWorkshared)
                        {
                            var wsParam = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                            if (wsParam != null)
                            {
                                int wsId = wsParam.AsInteger();
                                if (wsId > 0) worksetName = doc.GetWorksetTable().GetWorkset(new WorksetId(wsId))?.Name ?? "";
                            }
                        }
                    }
                    catch (Exception exWs) { StingLog.Warn($"Warning classify workset: {exWs.Message}"); }
                    categoryName = ParameterHelpers.GetCategoryName(el);
                    if (TagConfig.DiscMap.TryGetValue(categoryName, out string d)) discipline = d;
                }
            }

            return new ClassifiedWarning
            {
                Source = fm,
                Description = desc,
                Category = cat,
                Severity = sev,
                FixStrategy = fix,
                CanAutoFix = autoFix,
                FailingElements = failing,
                AdditionalElements = additional,
                LevelName = levelName,
                WorksetName = worksetName,
                Discipline = discipline,
                CategoryName = categoryName
            };
        }

        // ── SCAN CACHE ──

        // PERF: Warning scan cache — unlike ComplianceScan, WarningsEngine had NO caching.
        // Every call to ScanWarnings re-scanned all warnings from scratch (15+ callers).
        private static WarningReport _cachedReport;
        private static DateTime _reportCacheTime = DateTime.MinValue;
        private static string _cachedReportDocKey;
        private static readonly TimeSpan ReportCacheLifetime = TimeSpan.FromSeconds(30);

        /// <summary>Get cached warning report without triggering a new scan. Returns null if no cache.</summary>
        internal static WarningReport GetCachedReport() => _cachedReport;

        /// <summary>Invalidate warning report cache (call after auto-fix operations).</summary>
        internal static void InvalidateReportCache()
        {
            _cachedReport = null;
            _reportCacheTime = DateTime.MinValue;
            _cachedReportDocKey = null;
            _classificationCache.Clear(); // Phase 87: Prevent cross-document classification bleed
        }

        // ── FULL SCAN ──

        /// <summary>
        /// Comprehensive warning scan with categorisation, hotspot detection, and trend comparison.
        /// Uses 30-second cache to prevent redundant re-scans from 15+ callers.
        /// </summary>
        internal static WarningReport ScanWarnings(Document doc)
        {
            // PERF: Return cached report if recent (30-second TTL) and same document
            string docKey = doc.PathName ?? $"{doc.Title}_{doc.GetHashCode()}";
            if (_cachedReport != null && _cachedReportDocKey == docKey && (DateTime.UtcNow - _reportCacheTime) < ReportCacheLifetime)
                return _cachedReport;

            var report = new WarningReport();
            LoadSuppressions();

            IList<FailureMessage> rawWarnings;
            try { rawWarnings = doc.GetWarnings(); }
            catch (Exception ex)
            {
                StingLog.Error("WarningsEngine.ScanWarnings failed", ex);
                return report;
            }

            if (rawWarnings == null || rawWarnings.Count == 0) return report;

            // Element hotspot counter
            var elementCounts = new Dictionary<long, int>();

            foreach (FailureMessage fm in rawWarnings)
            {
                string desc = fm.GetDescriptionText() ?? "";

                // Skip suppressed warnings from count
                if (IsSuppressed(desc)) continue;

                var cw = BuildClassified(doc, fm);
                report.Warnings.Add(cw);
                report.Total++;

                if (cw.CanAutoFix) report.AutoFixable++;
                else report.ManualReview++;

                // Category counts
                report.ByCategory.TryGetValue(cw.Category, out int catCount);
                report.ByCategory[cw.Category] = catCount + 1;

                // Severity counts
                report.BySeverity.TryGetValue(cw.Severity, out int sevCount);
                report.BySeverity[cw.Severity] = sevCount + 1;

                // Level counts
                if (!string.IsNullOrEmpty(cw.LevelName))
                {
                    report.ByLevel.TryGetValue(cw.LevelName, out int lvlCount);
                    report.ByLevel[cw.LevelName] = lvlCount + 1;
                }

                // Workset counts
                if (!string.IsNullOrEmpty(cw.WorksetName))
                {
                    report.ByWorkset.TryGetValue(cw.WorksetName, out int wsCount);
                    report.ByWorkset[cw.WorksetName] = wsCount + 1;
                }

                // Discipline counts
                if (!string.IsNullOrEmpty(cw.Discipline))
                {
                    report.ByDiscipline.TryGetValue(cw.Discipline, out int discCount);
                    report.ByDiscipline[cw.Discipline] = discCount + 1;
                }

                // Hotspot counting
                if (cw.FailingElements != null)
                {
                    foreach (ElementId eid in cw.FailingElements)
                    {
                        long key = eid.Value;
                        elementCounts.TryGetValue(key, out int elCount);
                        elementCounts[key] = elCount + 1;
                    }
                }
            }

            // Top hotspot elements (capped at 100 entries)
            report.Hotspots = elementCounts
                .OrderByDescending(kv => kv.Value)
                .Take(100)
                .Select(kv =>
                {
                    string name = "";
                    try
                    {
                        Element el = doc.GetElement(new ElementId(kv.Key));
                        name = el != null ? $"{ParameterHelpers.GetCategoryName(el)} [{el.Id.Value}]" : $"[{kv.Key}]";
                    }
                    catch (Exception ex2) { StingLog.Warn($"Hotspot element name: {ex2.Message}"); name = $"[{kv.Key}]"; }
                    return (new ElementId(kv.Key), name, kv.Value);
                })
                .ToList();

            // Load baseline for trend comparison
            try
            {
                report.BaselineTotal = LoadBaseline(doc);
            }
            catch (Exception ex) { StingLog.Warn($"Warning baseline load: {ex.Message}"); }

            // Phase 48: Build top warnings by category for tooltip drill-down
            BuildTopWarningsByCategory(report);

            // Phase 48: SLA violation check
            try { CheckWarningSLAViolations(doc, report); }
            catch (Exception ex) { StingLog.Warn($"SLA check: {ex.Message}"); }

            // R4-C CS-GAP-02: Add stale elements as synthetic warnings so they appear
            // in the unified warnings pipeline with SLA tracking and auto-fix
            try
            {
                // PERF: Use cached compliance result to avoid full-project scan inside ScanWarnings.
                // Previously triggered a cascading ComplianceScan.Scan() on every ScanWarnings call.
                var compResult = ComplianceScan.GetCached() ?? ComplianceScan.Scan(doc);
                if (compResult != null && compResult.StaleCount > 0)
                {
                    var syntheticStale = new ClassifiedWarning
                    {
                        Description = $"{compResult.StaleCount} elements have moved/changed but tags not updated",
                        Category = WarningCategory.Data,
                        Severity = WarningSeverity.High,
                        CanAutoFix = true,
                        FixStrategy = "Run RetagStale command to update tags on moved elements"
                    };
                    report.Warnings.Add(syntheticStale);
                    report.Total++;
                    report.AutoFixable++;
                    report.ByCategory.TryGetValue(WarningCategory.Data, out int dataCatCount);
                    report.ByCategory[WarningCategory.Data] = dataCatCount + 1;
                    report.BySeverity.TryGetValue(WarningSeverity.High, out int highSevCount);
                    report.BySeverity[WarningSeverity.High] = highSevCount + 1;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Stale synthetic warnings: {ex.Message}"); }

            // Phase 108k Item 8 — synthetic BOQ-gap warnings. Items missing
            // rates or carrying unresolved [tokens] in the description join
            // the unified warnings feed so coordinators see them alongside
            // Revit-native warnings in the BCC Warnings tab. Auto-fix hint
            // points at the live template resolver.
            try
            {
                var boqGaps = StingTools.BOQ.BOQBccBridge.EmitBOQGapWarnings(doc);
                foreach (var gap in boqGaps)
                {
                    var sev = gap.Severity == "MEDIUM" ? WarningSeverity.Medium : WarningSeverity.Low;
                    var cw = new ClassifiedWarning
                    {
                        Description = gap.Description,
                        Category    = WarningCategory.Data,
                        Severity    = sev,
                        CanAutoFix  = gap.Description.Contains("[token]"),
                        FixStrategy = gap.Description.Contains("rate missing")
                            ? "Open BOQ Cost Manager and set a rate, or add the category to cost_rates_5d.csv"
                            : gap.Description.Contains("[token]")
                                ? "Run 'BOQ Refresh' — the live template resolver will fill the tokens"
                                : "Open BOQ Cost Manager and add a description in the NRM2 paragraph strip",
                    };
                    if (gap.ElementId > 0)
                    {
                        if (cw.FailingElements == null) cw.FailingElements = new List<ElementId>();
                        cw.FailingElements.Add(new ElementId(gap.ElementId));
                    }
                    report.Warnings.Add(cw);
                    report.Total++;
                    if (cw.CanAutoFix) report.AutoFixable++;
                    report.ByCategory.TryGetValue(WarningCategory.Data, out int dataCount);
                    report.ByCategory[WarningCategory.Data] = dataCount + 1;
                    report.BySeverity.TryGetValue(sev, out int sevCount);
                    report.BySeverity[sev] = sevCount + 1;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BOQ gap synthetic warnings: {ex.Message}"); }

            // Analyse deliverable impact (COBie, IFC, FM handover, schedules, clash)
            try { report.DeliverableImpact = AnalyseDeliverableImpact(report.Warnings); }
            catch (Exception ex) { StingLog.Warn($"Deliverable impact analysis: {ex.Message}"); }

            // R4-D A1: Build root-cause groups (deduplicates identical warnings)
            try { BuildRootCauseGroups(report); }
            catch (Exception ex) { StingLog.Warn($"Root-cause grouping: {ex.Message}"); }

            // PERF: Cache the report for 30 seconds to prevent redundant re-scans
            _cachedReport = report;
            _reportCacheTime = DateTime.UtcNow;
            _cachedReportDocKey = docKey;

            return report;
        }

        // ── AUTO-FIX ENGINE ──

        /// <summary>
        /// Attempt to auto-fix a single warning. Returns true if resolved.
        /// Handles: duplicate instances, redundant room separation lines, duplicate marks,
        /// unjoined elements, and duplicate type marks.
        /// </summary>
        internal static bool AutoFixWarning(Document doc, ClassifiedWarning cw, HashSet<string> _cachedExistingMarks = null)
        {
            if (!cw.CanAutoFix || cw.FailingElements == null || cw.FailingElements.Count == 0)
                return false;

            string lower = cw.Description.ToLowerInvariant();

            try
            {
                // Strategy 1: Duplicate instances at same location — delete one copy
                if (lower.Contains("duplicate instances in the same place"))
                {
                    // Delete additional elements (keep the first, delete the duplicate)
                    if (cw.AdditionalElements != null && cw.AdditionalElements.Count > 0)
                    {
                        foreach (ElementId id in cw.AdditionalElements)
                        {
                            try { doc.Delete(id); return true; }
                            catch (Exception delEx) { StingLog.Warn($"AutoFix delete {id}: {delEx.Message}"); }
                        }
                    }
                    // Fallback: delete second failing element
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2 && doc.GetElement(ids[1]) != null)
                    {
                        doc.Delete(ids[1]);
                        return true;
                    }
                }

                // Strategy 2: Room separation line overlaps — delete shorter line
                if (lower.Contains("room separation line") && lower.Contains("overlap"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        double len0 = GetCurveLength(doc, ids[0]);
                        double len1 = GetCurveLength(doc, ids[1]);
                        // If either length is MaxValue (element not found/no curve), skip auto-fix
                        if (len0 == double.MaxValue || len1 == double.MaxValue)
                            return false;
                        doc.Delete(len0 <= len1 ? ids[0] : ids[1]);
                        return true;
                    }
                }

                // Strategy 3: Redundant elements — delete (exclude room separation lines handled by Strategy 2)
                if (lower.Contains("redundant") && !lower.Contains("room separation line"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        doc.Delete(ids[1]); // Keep first, delete redundant
                        return true;
                    }
                }

                // Strategy 4: Duplicate mark value — auto-increment with collision avoidance
                // Phase 56 WM-001 fix: naive "_2" append could create new collisions
                if (lower.Contains("duplicate mark value") || lower.Contains("duplicate type mark"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        Element el = doc.GetElement(ids[1]);
                        if (el != null)
                        {
                            Parameter markParam = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                            if (markParam != null && !markParam.IsReadOnly)
                            {
                                string current = markParam.AsString() ?? "";
                                // Phase 85: Use pre-built cached marks if available, else build fresh
                                var existingMarks = _cachedExistingMarks;
                                if (existingMarks == null)
                                {
                                    existingMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                    try
                                    {
                                        foreach (Element e in new FilteredElementCollector(doc)
                                            .WhereElementIsNotElementType())
                                        {
                                            string m = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                                            if (!string.IsNullOrEmpty(m)) existingMarks.Add(m);
                                        }
                                    }
                                    catch (Exception ex2) { StingLog.Warn($"Mark collection: {ex2.Message}"); }
                                }

                                // Find unique mark by numeric increment
                                for (int attempt = 2; attempt < 1000; attempt++)
                                {
                                    string newMark = $"{current}_{attempt}";
                                    if (!existingMarks.Contains(newMark))
                                    {
                                        markParam.Set(newMark);
                                        existingMarks.Add(newMark);
                                        return true;
                                    }
                                }
                                StingLog.Warn($"Strategy 4: exhausted 998 suffix attempts for mark '{current}'");
                                return false; // Do not write duplicate
                            }
                        }
                    }
                }

                // Strategy 5: Joined but do not intersect — unjoin
                if (lower.Contains("joined but do not intersect"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        try
                        {
                            // R1-WM-02: Null-guard both elements before UnjoinGeometry
                            var el0 = doc.GetElement(ids[0]);
                            var el1 = doc.GetElement(ids[1]);
                            if (el0 != null && el1 != null)
                            {
                                JoinGeometryUtils.UnjoinGeometry(doc, el0, el1);
                                return true;
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"Unjoin failed: {ex2.Message}"); }
                    }
                }

                // Phase 55: Strategy 6: Overlapping walls — join geometry
                if (lower.Contains("highlighted walls overlap"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        try
                        {
                            var e1 = doc.GetElement(ids[0]);
                            var e2 = doc.GetElement(ids[1]);
                            if (e1 != null && e2 != null && !JoinGeometryUtils.AreElementsJoined(doc, e1, e2))
                            {
                                JoinGeometryUtils.JoinGeometry(doc, e1, e2);
                                return true;
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"Wall join failed: {ex2.Message}"); }
                    }
                }

                // Phase 55: Strategy 7: Room tag outside boundary — move to room center
                if (lower.Contains("room tag") && lower.Contains("outside"))
                {
                    var ids = cw.FailingElements.ToList();
                    foreach (var id in ids)
                    {
                        try
                        {
                            var el = doc.GetElement(id);
                            var rt = el as Autodesk.Revit.DB.Architecture.RoomTag;
                            if (rt?.Room != null)
                            {
                                var room = rt.Room;
                                var bb = room.get_BoundingBox(null);
                                if (bb != null)
                                {
                                    var center = (bb.Min + bb.Max) / 2.0;
                                    var currentPoint = (rt.Location as LocationPoint)?.Point ?? XYZ.Zero;
                                    var moveVector = center - currentPoint;
                                    rt.Location.Move(moveVector);
                                    return true;
                                }
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"Room tag move failed: {ex2.Message}"); }
                    }
                }

                // Phase 55: Strategy 8: Elements slightly off axis — snap to nearest axis
                if (lower.Contains("slightly off axis"))
                {
                    var ids = cw.FailingElements.ToList();
                    foreach (var id in ids)
                    {
                        try
                        {
                            var el = doc.GetElement(id);
                            if (el?.Location is LocationCurve lc)
                            {
                                var line = lc.Curve as Line;
                                if (line != null)
                                {
                                    var dir = line.Direction;
                                    // Snap near-horizontal to horizontal, near-vertical to vertical
                                    if (Math.Abs(dir.Y) > 0.0001 && Math.Abs(dir.Y) < 0.01)
                                    {
                                        var newEnd = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(0).Y, line.GetEndPoint(1).Z);
                                        lc.Curve = Line.CreateBound(line.GetEndPoint(0), newEnd);
                                        return true;
                                    }
                                    // WM-CRIT-01 FIX: Was dir.Y > 0.0001 — must be dir.X for near-vertical check
                                    if (Math.Abs(dir.X) < 0.01 && Math.Abs(dir.X) > 0.0001)
                                    {
                                        var newEnd = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(1).Y, line.GetEndPoint(1).Z);
                                        lc.Curve = Line.CreateBound(line.GetEndPoint(0), newEnd);
                                        return true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"Axis snap failed: {ex2.Message}"); }
                    }
                }

                // Phase 63: Strategy 9: Delete zero-length elements (walls, pipes, ducts)
                if (lower.Contains("zero length") || lower.Contains("zero-length"))
                {
                    var ids = cw.FailingElements.ToList();
                    foreach (var id in ids)
                    {
                        try
                        {
                            Element el = doc.GetElement(id);
                            if (el?.Location is LocationCurve lc && lc.Curve.Length < 0.01) // < ~3mm
                            {
                                doc.Delete(id);
                                return true;
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"Zero-length delete: {ex2.Message}"); }
                    }
                }

                // Phase 63: Strategy 10: Fix duplicate mark values — use pre-cached marks from BatchAutoFix
                // Exclude "duplicate mark value" which is handled by Strategy 4
                if (lower.Contains("duplicate mark") && !lower.Contains("duplicate mark value") && !lower.Contains("duplicate type mark"))
                {
                    var ids = cw.FailingElements.ToList();
                    if (ids.Count >= 2)
                    {
                        try
                        {
                            // PERF-R10: Use pre-cached mark set from BatchAutoFix instead of per-warning full scan.
                            // Falls back to inline scan only when called outside BatchAutoFix context.
                            var existingMarks = _cachedExistingMarks;
                            if (existingMarks == null)
                            {
                                existingMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                                {
                                    string m = el?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                                    if (!string.IsNullOrEmpty(m)) existingMarks.Add(m);
                                }
                            }
                            // Change the second element's mark with suffix increment against the full model set
                            var secondEl = doc.GetElement(ids[1]);
                            var markParam = secondEl?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                            if (markParam != null && !markParam.IsReadOnly)
                            {
                                string baseMark = markParam.AsString() ?? "";
                                for (int suffix = 2; suffix < 1000; suffix++)
                                {
                                    string candidate = $"{baseMark}_{suffix}";
                                    if (!existingMarks.Contains(candidate))
                                    {
                                        markParam.Set(candidate);
                                        existingMarks.Add(candidate); // PERF-R10: Update cache for next fix in batch
                                        return true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"Duplicate mark fix: {ex2.Message}"); }
                    }
                }

                // Phase 67: Strategy 12: Fix unconnected pipe/duct by adding cap
                // (Note: actual cap placement requires system type — log for manual review)
                if (lower.Contains("unconnected") && (lower.Contains("pipe") || lower.Contains("duct")))
                {
                    StingLog.Info($"AutoFix: Unconnected MEP element detected — flagging for manual review. IDs: {string.Join(",", cw.FailingElements.Select(i => i.Value))}");
                    // Mark elements as needing attention via StingLog — can't auto-cap without system context
                    return false;
                }

                // Phase 67: Strategy 13: Fix elements off-level by snapping to nearest level
                if (lower.Contains("offset from level"))
                {
                    foreach (var id in cw.FailingElements)
                    {
                        try
                        {
                            var el = doc.GetElement(id);
                            if (el == null) continue;
                            var bbx = el.get_BoundingBox(null);
                            if (bbx == null) continue;
                            double elZ = bbx.Min.Z;
                            // Find nearest level
                            var levels = new FilteredElementCollector(doc)
                                .OfClass(typeof(Level)).Cast<Level>()
                                .OrderBy(l => Math.Abs(l.Elevation - elZ))
                                .ToList();
                            if (levels.Count > 0 && el.LevelId != levels[0].Id)
                            {
                                var lvlParam = el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                                    ?? el.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
                                if (lvlParam != null && !lvlParam.IsReadOnly)
                                {
                                    lvlParam.Set(levels[0].Id);
                                    return true;
                                }
                            }
                        }
                        catch (Exception exOff) { StingLog.Warn($"Off-level fix: {exOff.Message}"); }
                    }
                }

                // Phase 68: Strategy 14: Fix empty sheets — delete viewportless sheets
                if (lower.Contains("empty sheet"))
                {
                    var ids = cw.FailingElements.ToList();
                    foreach (var id in ids)
                    {
                        try
                        {
                            var sheet = doc.GetElement(id) as ViewSheet;
                            if (sheet != null)
                            {
                                var vpIds = sheet.GetAllViewports();
                                if (vpIds == null || vpIds.Count == 0)
                                {
                                    doc.Delete(id);
                                    return true;
                                }
                            }
                        }
                        catch (Exception exES) { StingLog.Warn($"Empty sheet delete: {exES.Message}"); }
                    }
                }

                // Phase 68: Strategy 15: Fix elements with no MEP system — assign default system
                if (lower.Contains("no system") || lower.Contains("system classification is undefined"))
                {
                    StingLog.Info($"AutoFix: MEP system undefined for elements: {string.Join(",", cw.FailingElements.Select(i => i.Value))} — requires manual system assignment via MEP System Browser");
                    // Cannot auto-assign system without user intent — flag for review
                    return false;
                }

                // Phase 68: Strategy 16: Fix room not enclosed — attempt to detect and log gap direction
                if (lower.Contains("room not enclosed") || lower.Contains("not in a properly enclosed"))
                {
                    foreach (var id in cw.FailingElements)
                    {
                        try
                        {
                            var room = doc.GetElement(id) as Room;
                            if (room != null && room.Area <= 0)
                            {
                                // Log room location so BIM coordinator can find the gap quickly
                                var locPt = room.Location as LocationPoint;
                                string loc = locPt?.Point != null
                                    ? $"at ({locPt.Point.X:F1},{locPt.Point.Y:F1})"
                                    : "unknown location";
                                StingLog.Info($"AutoFix: Room '{room.Name}' ({room.Number}) not enclosed {loc} — check boundary walls for gaps");
                            }
                        }
                        catch (Exception exRoom) { StingLog.Warn($"Room gap detection: {exRoom.Message}"); }
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"AutoFix failed for '{cw.Description}': {ex.Message}");
            }
            return false;
        }

        /// <summary>Get length of a curve-based element (separation line, wall, etc.).</summary>
        private static double GetCurveLength(Document doc, ElementId id)
        {
            try
            {
                Element el = doc.GetElement(id);
                if (el?.Location is LocationCurve lc) return lc.Curve.Length;
            }
            catch (Exception exLen) { StingLog.Warn($"GetElementLength: {exLen.Message}"); }
            return double.MaxValue; // If can't determine, treat as long (don't delete)
        }

        /// <summary>Phase 66: Analyse warning impact on BIM deliverables (COBie, IFC, FM handover, schedules, clash).</summary>
        internal static WarningImpactAnalysis AnalyseDeliverableImpact(List<ClassifiedWarning> warnings)
        {
            var impact = new WarningImpactAnalysis();
            foreach (var w in warnings)
            {
                // HIGH-10: Use StringComparison.OrdinalIgnoreCase — avoids ToLowerInvariant() allocation per warning
                string desc = w.Description ?? "";
                // COBie impact: data quality warnings, missing params, duplicate marks
                if (w.Category == WarningCategory.Data
                    || desc.IndexOf("duplicate mark", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("missing parameter", StringComparison.OrdinalIgnoreCase) >= 0)
                    impact.AffectsCOBie++;
                // IFC impact: geometric, structural, and material warnings
                if (w.Category == WarningCategory.Geometric
                    || w.Category == WarningCategory.Structural
                    || desc.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0)
                    impact.AffectsIFC++;
                // FM handover: spatial, compliance, MEP system warnings
                if (w.Category == WarningCategory.Spatial
                    || w.Category == WarningCategory.Compliance
                    || desc.IndexOf("system", StringComparison.OrdinalIgnoreCase) >= 0)
                    impact.AffectsHandover++;
                // Schedule: data quality, level offset, parameter warnings
                if (desc.IndexOf("schedule", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("offset from level", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("parameter", StringComparison.OrdinalIgnoreCase) >= 0)
                    impact.AffectsSchedules++;
                // Clash: geometric overlap, MEP intersection warnings
                if (desc.IndexOf("overlap", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("intersect", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("clash", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("conflict", StringComparison.OrdinalIgnoreCase) >= 0)
                    impact.AffectsClash++;
            }
            impact.TotalDeliverableImpact = impact.AffectsCOBie + impact.AffectsIFC + impact.AffectsHandover + impact.AffectsSchedules + impact.AffectsClash;

            // Determine highest-impact area
            var areas = new[] {
                (impact.AffectsCOBie, "COBie Export"),
                (impact.AffectsIFC, "IFC Geometry"),
                (impact.AffectsHandover, "FM Handover"),
                (impact.AffectsSchedules, "Schedule Data"),
                (impact.AffectsClash, "Clash Detection")
            };
            impact.HighestImpactArea = areas.OrderByDescending(a => a.Item1).First().Item2;
            return impact;
        }

        /// <summary>
        /// Batch auto-fix all fixable warnings. Uses single transaction for atomicity.
        /// </summary>
        internal static FixReport BatchAutoFix(Document doc, List<ClassifiedWarning> warnings, bool dryRun = false)
        {
            var report = new FixReport();
            var fixable = warnings.Where(w => w.CanAutoFix).ToList();
            report.Attempted = fixable.Count;

            if (dryRun)
            {
                report.Fixed = fixable.Count;
                foreach (var w in fixable)
                    report.Details.Add($"[DRY-RUN] Would fix: {w.Description}");
                return report;
            }

            // PERF-R10: Pre-build existing marks HashSet ONCE for all duplicate mark fixes.
            // Previously each duplicate mark warning triggered a full-model scan (50K+ elements).
            HashSet<string> _cachedExistingMarks = null;
            bool hasDuplicateMarkWarnings = fixable.Any(w =>
                (w.Description ?? "").ToLowerInvariant().Contains("duplicate mark"));
            if (hasDuplicateMarkWarnings)
            {
                _cachedExistingMarks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
                {
                    try
                    {
                        string m = el?.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                        if (!string.IsNullOrEmpty(m)) _cachedExistingMarks.Add(m);
                    }
                    catch (Exception ex) { StingLog.Warn($"Mark scan: {ex.Message}"); }
                }
            }

            using (Transaction tx = new Transaction(doc, "STING Auto-Fix Warnings"))
            {
                tx.Start();
                foreach (var cw in fixable)
                {
                    try
                    {
                        if (AutoFixWarning(doc, cw, _cachedExistingMarks))
                        {
                            report.Fixed++;
                            report.Details.Add($"Fixed: {cw.Description}");
                        }
                        else
                        {
                            report.Skipped++;
                            report.Details.Add($"Skipped: {cw.Description}");
                        }
                    }
                    catch (Exception ex)
                    {
                        report.Failed++;
                        report.Details.Add($"Failed: {cw.Description} — {ex.Message}");
                    }
                }
                if (report.Fixed > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            // Phase 56: Fix verification — re-scan warnings after auto-fix to confirm fixes worked
            if (report.Fixed > 0)
            {
                try
                {
                    int postFixCount = doc.GetWarnings()?.Count ?? 0;
                    int preFixCount = warnings.Count;
                    int netReduction = preFixCount - postFixCount;
                    report.Details.Add($"");
                    report.Details.Add($"── Verification ──");
                    report.Details.Add($"Before: {preFixCount} warnings, After: {postFixCount} warnings");
                    if (netReduction > 0)
                        report.Details.Add($"Net reduction: {netReduction} warnings resolved");
                    else if (netReduction == 0)
                        report.Details.Add($"Warning: no net reduction — fixes may have introduced new warnings");
                    else
                        report.Details.Add($"WARNING: {Math.Abs(netReduction)} NEW warnings introduced by fixes — review manually");
                    report.NetReduction = netReduction;
                }
                catch (Exception ex) { StingLog.Warn($"Fix verification scan: {ex.Message}"); }
                // AUTO-R5: Invalidate cached report so dashboard shows post-fix state immediately
                InvalidateReportCache();
            }
            return report;
        }

        // ── WARNING PRIORITY QUEUE ──

        /// <summary>
        /// Phase 56: Calculate weighted priority score for each warning.
        /// Higher score = fix this warning first. Factors: severity, element count,
        /// downstream impact (COBie, compliance), age, and auto-fixability.
        /// </summary>
        internal static List<(ClassifiedWarning Warning, double Score, string Reason)>
            PrioritizeWarnings(List<ClassifiedWarning> warnings)
        {
            var scored = new List<(ClassifiedWarning, double, string)>();
            foreach (var w in warnings)
            {
                double score = 0;
                var reasons = new List<string>();

                // Severity weight (0-50)
                switch (w.Severity)
                {
                    case WarningSeverity.Critical: score += 50; reasons.Add("CRITICAL severity"); break;
                    case WarningSeverity.High: score += 35; reasons.Add("HIGH severity"); break;
                    case WarningSeverity.Medium: score += 20; break;
                    case WarningSeverity.Low: score += 10; break;
                    default: score += 5; break;
                }

                // Element count impact (0-20)
                int elemCount = (w.FailingElements?.Count ?? 0) + (w.AdditionalElements?.Count ?? 0);
                if (elemCount > 10) { score += 20; reasons.Add($"{elemCount} elements affected"); }
                else if (elemCount > 5) score += 15;
                else if (elemCount > 1) score += 10;
                else score += 5;

                // Category impact on downstream systems (0-20)
                if (w.Category == WarningCategory.Spatial) { score += 20; reasons.Add("Affects room/area data"); }
                else if (w.Category == WarningCategory.MEP) { score += 15; reasons.Add("Affects MEP systems"); }
                else if (w.Category == WarningCategory.Data) { score += 15; reasons.Add("Affects tag/schedule data"); }
                else if (w.Category == WarningCategory.Compliance) { score += 18; reasons.Add("Compliance impact"); }
                else if (w.Category == WarningCategory.Structural) { score += 12; }

                // Auto-fixable bonus (prioritize easy wins)
                if (w.CanAutoFix) { score += 10; reasons.Add("Auto-fixable"); }

                scored.Add((w, score, string.Join(", ", reasons)));
            }

            return scored.OrderByDescending(s => s.Item2).ToList();
        }

        // ── MODEL VALIDATION ENGINE ──

        /// <summary>
        /// Phase 56: Post-creation model validation. Checks geometry, spatial,
        /// MEP system, and naming convention compliance.
        /// Returns list of validation issues found.
        /// </summary>
        internal static List<string> ValidateModelElements(Document doc, List<ElementId> elementIds)
        {
            var issues = new List<string>();
            if (doc == null || elementIds == null || elementIds.Count == 0) return issues;

            foreach (var id in elementIds)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;

                try
                {
                    // 1. Geometry validation — check for zero-length/area elements
                    if (el.Location is LocationCurve lc)
                    {
                        if (lc.Curve.Length < 0.01) // ~3mm
                            issues.Add($"Element {el.Id}: near-zero length ({lc.Curve.Length * 304.8:F0}mm)");
                    }

                    // 2. Bounding box validation — elements without geometry
                    var bb = el.get_BoundingBox(null);
                    if (bb == null)
                        issues.Add($"Element {el.Id} ({el.Category?.Name}): no bounding box — may be invisible");
                    else if ((bb.Max - bb.Min).GetLength() < 0.001)
                        issues.Add($"Element {el.Id} ({el.Category?.Name}): zero-size bounding box");

                    // 3. Level association — elements without a level
                    var levelId = el.LevelId;
                    if (levelId == null || levelId == ElementId.InvalidElementId)
                    {
                        // Only flag for elements that should have a level
                        if (el is Wall || el is Floor || el is FamilyInstance fi && fi.Host == null)
                            issues.Add($"Element {el.Id} ({el.Category?.Name}): not associated with a level");
                    }

                    // 4. MEP system validation — connectors should be connected
                    if (el is FamilyInstance fam)
                    {
                        var connMgr = fam.MEPModel?.ConnectorManager;
                        if (connMgr != null)
                        {
                            int unconnected = 0;
                            foreach (Connector c in connMgr.Connectors)
                                if (!c.IsConnected) unconnected++;
                            if (unconnected > 0)
                                issues.Add($"Element {el.Id} ({el.Category?.Name}): {unconnected} unconnected MEP connector(s)");
                        }
                    }

                    // 5. Naming convention — elements with default names
                    string mark = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                    if (string.IsNullOrEmpty(mark) && (el is Wall || el is Floor || el is FamilyInstance))
                    {
                        // Not critical but useful for BIM coordinators
                    }
                }
                catch (Exception ex) { StingLog.Warn($"ValidateModelElement {id}: {ex.Message}"); }
            }

            return issues;
        }

        // ── BASELINE / TREND ──

        private static string GetBaselinePath(Document doc)
        {
            string docPath = doc?.PathName;
            if (string.IsNullOrEmpty(docPath)) return null;
            try
            {
                string p = ProjectFolderEngine.GetDataPath(doc, "warnings_baseline.json");
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return Path.ChangeExtension(docPath, ".sting_warnings_baseline.json");
        }

        /// <summary>Save current warning count as baseline for trend tracking.
        /// Phase 56b DATA-WARN-01: Uses atomic write pattern (temp + rename) to prevent corruption.</summary>
        internal static void SaveBaseline(Document doc)
        {
            try
            {
                string path = GetBaselinePath(doc);
                if (path == null) return;
                int count = doc.GetWarnings()?.Count ?? 0;
                string json = $"{{\"version\":2,\"total\":{count},\"date\":\"{DateTime.Now:o}\",\"user\":\"{Environment.UserName}\"}}";

                // Atomic write: temp file then replace to prevent mid-write corruption
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                // R1-WM-01: Use File.Replace for atomic swap (Delete+Move has crash window)
                if (File.Exists(path))
                    File.Replace(tempPath, path, path + ".bak");
                else
                    File.Move(tempPath, path);

                StingLog.Info($"Warning baseline saved: {count} warnings");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SaveBaseline: {ex.Message}");
                // Clean up temp file on failure
                try { string tp = GetBaselinePath(doc) + ".tmp"; if (File.Exists(tp)) File.Delete(tp); }
                catch (Exception cleanupEx) { StingLog.Warn($"SaveBaseline cleanup: {cleanupEx.Message}"); }
            }
        }

        /// <summary>Load previous baseline count. Returns null if no baseline exists.</summary>
        internal static int? LoadBaseline(Document doc)
        {
            try
            {
                string path = GetBaselinePath(doc);
                if (path == null || !File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                // Simple parse — extract "total":N
                int idx = json.IndexOf("\"total\":");
                if (idx < 0) return null;
                string numStr = json.Substring(idx + 8).TrimStart();
                int endIdx = numStr.IndexOfAny(new[] { ',', '}', ' ' });
                if (endIdx > 0) numStr = numStr.Substring(0, endIdx);
                return int.TryParse(numStr, out int val) ? val : null;
            }
            catch (Exception ex) { StingLog.Warn($"LoadBaseline: {ex.Message}"); return null; }
        }

        // ── EXPORT ──

        /// <summary>Export all warnings to CSV for external tracking (BIM360, Aconex, etc.).</summary>
        internal static string ExportToCSV(Document doc, WarningReport report)
        {
            string exportDir = OutputLocationHelper.GetOutputDirectory(doc);
            string fileName = $"STING_Warnings_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string fullPath = Path.Combine(exportDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("\"Description\",\"Category\",\"Severity\",\"FixStrategy\",\"CanAutoFix\",\"ElementIds\",\"Level\",\"Workset\",\"Discipline\",\"CategoryName\"");

            foreach (var cw in report.Warnings)
            {
                string ids = cw.FailingElements != null
                    ? string.Join(";", cw.FailingElements.Select(id => id.Value))
                    : "";
                sb.AppendLine($"\"{Escape(cw.Description)}\",\"{cw.Category}\",\"{cw.Severity}\"," +
                    $"\"{Escape(cw.FixStrategy)}\",\"{cw.CanAutoFix}\"," +
                    $"\"{ids}\",\"{Escape(cw.LevelName)}\",\"{Escape(cw.WorksetName)}\"," +
                    $"\"{cw.Discipline}\",\"{Escape(cw.CategoryName)}\"");
            }

            try
            {
                Directory.CreateDirectory(exportDir);
                File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);
                StingLog.Info($"Warnings exported: {fullPath}");
            }
            catch (Exception ex) { StingLog.Error($"Export warnings CSV", ex); }
            return fullPath;
        }

        private static string Escape(string s) => (s ?? "").Replace("\"", "\"\"");

        // ── Phase 47: WARNING-TO-ISSUE AUTO-CREATION ──

        /// <summary>Phase 47: Auto-create issues from critical/high severity warnings.
        /// Groups warnings by category, creates one issue per category with element links.</summary>
        internal static List<(string issueId, string title, int elementCount)> CreateIssuesFromWarnings(
            Document doc, List<ClassifiedWarning> warnings, WarningSeverity minSeverity = WarningSeverity.High)
        {
            var results = new List<(string issueId, string title, int elementCount)>();
            try
            {
                var filtered = warnings.Where(w => w.Severity <= minSeverity).ToList(); // Critical=0, High=1 — lower enum = higher severity
                if (filtered.Count == 0) return results;

                var groups = filtered.GroupBy(w => w.Category);

                // Load or initialize issues.json
                string issuesDir = "";
                try
                {
                    string docPath = doc?.PathName;
                    if (!string.IsNullOrEmpty(docPath))
                        issuesDir = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager");
                    else
                        issuesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "STING_BIM", "_bim_manager");
                }
                catch (Exception ex) { StingLog.Warn($"CreateIssuesFromWarnings directory: {ex.Message}"); }

                if (string.IsNullOrEmpty(issuesDir)) return results;
                Directory.CreateDirectory(issuesDir);
                string issuesPath = Path.Combine(issuesDir, "issues.json");

                // Load existing issues
                var existingJson = new StringBuilder();
                List<string> existingEntries = new();
                if (File.Exists(issuesPath))
                {
                    try
                    {
                        string raw = File.ReadAllText(issuesPath);
                        // Simple JSON array parse — extract entries between [ and ]
                        raw = raw.Trim();
                        if (raw.StartsWith("[") && raw.EndsWith("]"))
                        {
                            string inner = raw.Substring(1, raw.Length - 2).Trim();
                            if (inner.Length > 0)
                            {
                                // Split on },{ pattern (simplified)
                                int depth = 0;
                                int start = 0;
                                for (int i = 0; i < inner.Length; i++)
                                {
                                    if (inner[i] == '{') depth++;
                                    else if (inner[i] == '}') depth--;
                                    if (depth == 0 && i > start)
                                    {
                                        existingEntries.Add(inner.Substring(start, i - start + 1).Trim());
                                        // Skip comma
                                        while (i + 1 < inner.Length && (inner[i + 1] == ',' || inner[i + 1] == ' ' || inner[i + 1] == '\n' || inner[i + 1] == '\r'))
                                            i++;
                                        start = i + 1;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex2) { StingLog.Warn($"CreateIssuesFromWarnings parse: {ex2.Message}"); }
                }

                // Determine next issue ID — scan for max existing numeric suffix
                // B05 FIX: existingEntries.Count + 1 causes ID collisions after deletions.
                // Scan all existing entries for highest numeric suffix instead.
                int nextId = existingEntries.Count + 1; // fallback
                try
                {
                    foreach (var entry in existingEntries)
                    {
                        // Look for "id":"NCR-0042" or "id":"SI-0007" patterns
                        int idIdx = entry.IndexOf("\"id\"", StringComparison.OrdinalIgnoreCase);
                        if (idIdx < 0) continue;
                        int colonIdx = entry.IndexOf(':', idIdx + 4);
                        if (colonIdx < 0) continue;
                        int q1 = entry.IndexOf('"', colonIdx + 1);
                        int q2 = q1 >= 0 ? entry.IndexOf('"', q1 + 1) : -1;
                        if (q1 < 0 || q2 < 0) continue;
                        string idVal = entry.Substring(q1 + 1, q2 - q1 - 1);
                        int dashIdx = idVal.LastIndexOf('-');
                        if (dashIdx >= 0 && int.TryParse(idVal.Substring(dashIdx + 1), out int num) && num >= nextId)
                            nextId = num + 1;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CreateIssuesFromWarnings ID scan: {ex.Message}"); }
                string revision = "";
                try { revision = PhaseAutoDetect.DetectProjectRevision(doc) ?? ""; }
                catch (Exception ex) { StingLog.Warn($"CreateIssuesFromWarnings revision: {ex.Message}"); }

                foreach (var group in groups)
                {
                    var groupWarnings = group.ToList();
                    var maxSeverity = groupWarnings.Min(w => w.Severity); // Min enum = highest severity
                    string issueType = maxSeverity == WarningSeverity.Critical ? "NCR" : "SI";
                    string priority = maxSeverity == WarningSeverity.Critical ? "CRITICAL" : "HIGH";
                    string severityLabel = maxSeverity == WarningSeverity.Critical ? "critical" : "high";
                    string title = $"Warning: {group.Key} — {groupWarnings.Count} {severityLabel} issues detected";

                    // Collect element IDs
                    var elementIds = new HashSet<long>();
                    foreach (var cw in groupWarnings)
                    {
                        if (cw.FailingElements != null)
                            foreach (var eid in cw.FailingElements) elementIds.Add(eid.Value);
                    }

                    string issueId = $"{issueType}-{nextId:D4}";
                    string now = DateTime.Now.ToString("o");
                    string userName = Environment.UserName ?? "STING";

                    // Build JSON entry
                    string elementIdsStr = string.Join(",", elementIds);
                    string entry = "{"
                        + $"\"id\":\"{issueId}\","
                        + $"\"type\":\"{issueType}\","
                        + $"\"title\":\"{title.Replace("\"", "\\\"")}\","
                        + $"\"description\":\"Auto-created from {groupWarnings.Count} Revit warnings in category {group.Key}.\","
                        + $"\"priority\":\"{priority}\","
                        + $"\"status\":\"OPEN\","
                        + $"\"discipline\":\"{groupWarnings.FirstOrDefault()?.Discipline ?? ""}\","
                        + $"\"revision\":\"{revision}\","
                        + $"\"element_ids\":\"{elementIdsStr}\","
                        + $"\"created_by\":\"{userName}\","
                        + $"\"created_date\":\"{now}\","
                        + $"\"modified_by\":\"{userName}\","
                        + $"\"modified_date\":\"{now}\""
                        + "}";

                    existingEntries.Add(entry);
                    results.Add((issueId, title, elementIds.Count));
                    nextId++;
                    StingLog.Info($"Created issue {issueId}: {title} ({elementIds.Count} elements)");
                }

                // Write back
                try
                {
                    var jsonSb = new StringBuilder();
                    jsonSb.AppendLine("[");
                    for (int i = 0; i < existingEntries.Count; i++)
                    {
                        jsonSb.Append("  ");
                        jsonSb.Append(existingEntries[i]);
                        if (i < existingEntries.Count - 1) jsonSb.Append(",");
                        jsonSb.AppendLine();
                    }
                    jsonSb.AppendLine("]");
                    // Phase 86: Atomic write — prevents sidecar corruption on crash mid-write
                    string tmpPath = issuesPath + ".tmp";
                    File.WriteAllText(tmpPath, jsonSb.ToString(), Encoding.UTF8);
                    File.Replace(tmpPath, issuesPath, issuesPath + ".bak");
                    StingLog.Info($"Issues file updated: {issuesPath} ({existingEntries.Count} total entries)");
                }
                catch (Exception ex) { StingLog.Error("CreateIssuesFromWarnings write", ex); }
            }
            catch (Exception ex) { StingLog.Error("CreateIssuesFromWarnings", ex); }
            return results;
        }

        // ── Phase 47: WARNING COMPLIANCE GATE ──

        /// <summary>Phase 47: Check if warnings block compliance gate.
        /// Returns true if model passes warning gate (no critical warnings, total below threshold).</summary>
        internal static (bool pass, string reason) CheckWarningGate(Document doc, int maxCritical = 0, int maxTotal = -1)
        {
            try
            {
                var report = ScanWarnings(doc);

                int criticalCount = report.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0);
                if (criticalCount > maxCritical)
                    return (false, $"Warning gate FAILED: {criticalCount} critical warning(s) exceed threshold of {maxCritical}. " +
                        $"Resolve critical warnings before proceeding.");

                if (maxTotal >= 0 && report.Total > maxTotal)
                    return (false, $"Warning gate FAILED: {report.Total} total warning(s) exceed threshold of {maxTotal}. " +
                        $"Reduce warnings before proceeding.");

                string reason = $"Warning gate PASSED: {criticalCount} critical (max {maxCritical}), " +
                    $"{report.Total} total" + (maxTotal >= 0 ? $" (max {maxTotal})" : "") + ".";
                return (true, reason);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CheckWarningGate: {ex.Message}");
                return (false, $"Warning gate check failed: {ex.Message}");
            }
        }

        // ── Phase 47: WARNING REGRESSION COMPARISON ──

        /// <summary>Phase 47: Compare current warnings against last revision snapshot.
        /// Returns delta with categorized changes.</summary>
        internal static (int added, int removed, int unchanged, List<string> newWarningTypes)
            CompareWithRevisionBaseline(Document doc)
        {
            var newWarningTypes = new List<string>();
            try
            {
                // Load baseline warning types from sidecar
                string baselinePath = GetBaselinePath(doc);
                if (baselinePath == null || !File.Exists(baselinePath))
                    return (0, 0, 0, newWarningTypes);

                // Load baseline warning type set from extended baseline format
                var baselineTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string baselineJson = File.ReadAllText(baselinePath);
                    // Parse warning_types array if present: "warning_types":["desc1","desc2",...]
                    int typesIdx = baselineJson.IndexOf("\"warning_types\":");
                    if (typesIdx >= 0)
                    {
                        int arrStart = baselineJson.IndexOf('[', typesIdx);
                        int arrEnd = baselineJson.IndexOf(']', arrStart);
                        if (arrStart >= 0 && arrEnd > arrStart)
                        {
                            string arrContent = baselineJson.Substring(arrStart + 1, arrEnd - arrStart - 1);
                            foreach (string part in arrContent.Split(','))
                            {
                                string trimmed = part.Trim().Trim('"');
                                if (trimmed.Length > 0) baselineTypes.Add(trimmed);
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CompareWithRevisionBaseline parse: {ex.Message}"); }

                if (baselineTypes.Count == 0)
                {
                    // No typed baseline — fall back to count-only comparison
                    int? baselineTotal = LoadBaseline(doc);
                    int currentTotal = doc.GetWarnings()?.Count ?? 0;
                    int delta = baselineTotal.HasValue ? currentTotal - baselineTotal.Value : 0;
                    return (Math.Max(0, delta), Math.Max(0, -delta), Math.Min(currentTotal, baselineTotal ?? currentTotal), newWarningTypes);
                }

                // Build current warning type set
                var currentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var rawWarnings = doc.GetWarnings();
                    if (rawWarnings != null)
                    {
                        foreach (var fm in rawWarnings)
                        {
                            string desc = fm.GetDescriptionText() ?? "";
                            if (desc.Length > 0) currentTypes.Add(desc);
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CompareWithRevisionBaseline scan: {ex.Message}"); }

                int added = 0, removed = 0, unchanged = 0;

                // Find new warning types (in current but not in baseline)
                foreach (string t in currentTypes)
                {
                    if (baselineTypes.Contains(t))
                        unchanged++;
                    else
                    {
                        added++;
                        newWarningTypes.Add(t);
                    }
                }

                // Find removed warning types (in baseline but not in current)
                foreach (string t in baselineTypes)
                {
                    if (!currentTypes.Contains(t))
                        removed++;
                }

                return (added, removed, unchanged, newWarningTypes);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CompareWithRevisionBaseline: {ex.Message}");
                return (0, 0, 0, newWarningTypes);
            }
        }

        // ── Phase 47: WARNING HEALTH SCORE ──

        /// <summary>Phase 47: Calculate overall warning health score 0-100.
        /// Weighted: Critical=-20, High=-5, Medium=-2, Low=-1, Info=0. Base=100.</summary>
        internal static int CalculateWarningHealthScore(WarningReport report)
        {
            if (report == null) return 100;

            int score = 100;
            score -= report.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0) * 20;
            score -= report.BySeverity.GetValueOrDefault(WarningSeverity.High, 0) * 5;
            score -= report.BySeverity.GetValueOrDefault(WarningSeverity.Medium, 0) * 2;
            score -= report.BySeverity.GetValueOrDefault(WarningSeverity.Low, 0) * 1;
            // Info = 0 weight (no penalty)

            return Math.Max(0, Math.Min(100, score));
        }

        // ── Phase 49: SMART SUGGESTIONS ENGINE ──

        /// <summary>
        /// Generate prioritised action suggestions based on current model state analysis.
        /// Examines compliance, warnings, issues, stale elements, and workflow history
        /// to recommend the most impactful next actions for BIM coordinators.
        /// </summary>
        internal static List<(string Text, string Action, string Priority)> GenerateSmartSuggestions(
            UI.BIMCoordinationCenter.CoordData data, WarningReport warningReport)
        {
            var suggestions = new List<(string Text, string Action, string Priority)>();
            try
            {
                // Critical: Stale elements block accurate deliverables
                if (data.StaleCount > 10)
                    suggestions.Add(($"Re-tag {data.StaleCount} stale elements before next export", "RetagStale", "HIGH"));
                else if (data.StaleCount > 0)
                    suggestions.Add(($"Clear {data.StaleCount} stale element(s)", "RetagStale", "MEDIUM"));

                // Critical: Overdue issues
                if (data.IssuesOverdue > 0)
                    suggestions.Add(($"Resolve {data.IssuesOverdue} overdue issue(s) — SLA breach risk", "IssueDashboard", "HIGH"));

                // High: Critical warnings
                if (data.WarningCritical > 0)
                    suggestions.Add(($"Fix {data.WarningCritical} critical warning(s) — blocks handover", "AutoFixWarnings", "HIGH"));

                // High: Low compliance
                if (data.TagPct < 50)
                    suggestions.Add(("Tag compliance below 50% — run batch tagging", "BatchTag", "HIGH"));
                else if (data.TagPct < 80)
                    suggestions.Add(($"Improve compliance from {data.TagPct:F0}% to 80%+ target", "TagNewOnly", "MEDIUM"));

                // Medium: Container completeness for COBie
                if (data.ContainerCompletePct < 80 && data.TagPct > 50)
                    suggestions.Add(($"Container completion at {data.ContainerCompletePct:F0}% — run Combine Parameters", "CombineParameters", "MEDIUM"));

                // Medium: Placeholders need resolution
                if (data.PlaceholderCount > 20)
                    suggestions.Add(($"Resolve {data.PlaceholderCount} placeholder tokens (GEN/XX/ZZ)", "ResolveAllIssues", "MEDIUM"));

                // Medium: Auto-fixable warnings
                if (data.WarningAutoFixable > 5)
                    suggestions.Add(($"Auto-fix {data.WarningAutoFixable} warnings in one click", "AutoFixWarnings", "MEDIUM"));

                // Low: Untagged elements
                if (data.Untagged > 0 && data.Untagged < 50)
                    suggestions.Add(($"Tag {data.Untagged} remaining untagged elements", "TagNewOnly", "LOW"));

                // Low: Run DailyQA if not run today
                if (string.IsNullOrEmpty(data.LastWorkflow) || data.LastWorkflow == "none")
                    suggestions.Add(("Run Daily QA workflow for comprehensive model check", "RunDailyQA", "MEDIUM"));

                // Low: Warning health below threshold
                if (data.WarningHealthScore < 50)
                    suggestions.Add(($"Warning health at {data.WarningHealthScore}/100 — review and fix", "WarningsDashboard", "MEDIUM"));

                // Informational: Ready for COBie export
                if (data.TagPct >= 90 && data.ContainerCompletePct >= 80 && data.WarningCritical == 0)
                    suggestions.Add(("Model ready for COBie export — compliance targets met", "COBieExport", "LOW"));

                // Informational: Save baseline if warnings changed
                if (warningReport != null && Math.Abs(warningReport.TrendDelta) > 5)
                    suggestions.Add(("Warning count changed significantly — save new baseline", "SaveBaseline", "LOW"));
            }
            catch (Exception ex) { StingLog.Warn($"Smart suggestions: {ex.Message}"); }

            return suggestions.Take(8).ToList();
        }

        // ── Phase 49: COORDINATION LOG ──

        /// <summary>
        /// Append an entry to the coordination log sidecar file.
        /// Thread-safe write with retry.
        /// </summary>
        // CRIT-06: Counter for cap enforcement (every 100th call)
        private static int _coordLogCallCount = 0;

        internal static void LogCoordinationAction(Document doc, string action, string category, string detail, string impact = "LOW")
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return;
                // CRIT-06: JSONL append-only — avoids read/parse/rewrite on every call
                string logPath = ProjectFolderEngine.GetDataPath(doc, "coord_log.jsonl");
                if (string.IsNullOrEmpty(logPath))
                    logPath = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", ".sting_coord_log.jsonl");

                var entry = new UI.BIMCoordinationCenter.CoordLogEntry
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    User = Environment.UserName ?? "unknown",
                    Action = action,
                    Category = category,
                    Detail = detail,
                    Impact = impact
                };

                // CRIT-06: Append single JSON line — O(1) regardless of file size
                string line = Newtonsoft.Json.JsonConvert.SerializeObject(entry);
                File.AppendAllText(logPath, line + Environment.NewLine);

                // CRIT-06: Enforce 1000-entry cap every 100th call to avoid unbounded growth
                _coordLogCallCount++;
                if (_coordLogCallCount % 100 == 0 && File.Exists(logPath))
                {
                    try
                    {
                        var lines = File.ReadAllLines(logPath);
                        if (lines.Length > 1000)
                        {
                            // Keep only the most recent 1000 lines
                            File.WriteAllLines(logPath, lines.Skip(lines.Length - 1000));
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"CoordLog cap: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CoordLog write: {ex.Message}"); }
        }

        /// <summary>Phase 49: Predictive compliance forecast — estimates days to reach target based on trend.</summary>
        internal static (double daysToTarget, double projectedPct) ForecastCompliance(List<(DateTime Date, double Pct)> trend, double targetPct = 80)
        {
            if (trend == null || trend.Count < 2) return (-1, trend?.LastOrDefault().Pct ?? 0);

            // Simple linear regression on last 10 data points
            var recent = trend.TakeLast(10).ToList();
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            double baseTime = recent[0].Date.Ticks / (double)TimeSpan.TicksPerDay;
            int n = recent.Count;

            for (int i = 0; i < n; i++)
            {
                double x = (recent[i].Date.Ticks / (double)TimeSpan.TicksPerDay) - baseTime;
                double y = recent[i].Pct;
                sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;
            }

            double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX + 0.0001);
            double intercept = (sumY - slope * sumX) / n;

            double currentX = (DateTime.Now.Ticks / (double)TimeSpan.TicksPerDay) - baseTime;
            double projected = slope * currentX + intercept;

            if (slope <= 0 || Math.Abs(slope) < 1e-10) return (-1, Math.Max(0, Math.Min(100, projected))); // Not improving

            double daysToTarget = (targetPct - projected) / slope;
            return (Math.Max(0, daysToTarget), Math.Max(0, Math.Min(100, projected)));
        }

        // ── Phase 48: SLA ENFORCEMENT ──

        /// <summary>ISO 19650-aligned SLA thresholds per warning severity (hours).
        /// GAP-FIX: Now uses configurable values from TagConfig.SLAThresholdsHours when available.</summary>
        internal static Dictionary<WarningSeverity, double> GetSLAThresholds()
        {
            var thresholds = new Dictionary<WarningSeverity, double>
            {
                { WarningSeverity.Critical, 4 },
                { WarningSeverity.High, 24 },
                { WarningSeverity.Medium, 168 },
                { WarningSeverity.Low, 336 },
                { WarningSeverity.Info, double.MaxValue }
            };
            // Override from configurable SLA thresholds
            var cfg = TagConfig.SLAThresholdsHours;
            if (cfg != null)
            {
                if (cfg.TryGetValue("CRITICAL", out double c)) thresholds[WarningSeverity.Critical] = c;
                if (cfg.TryGetValue("HIGH", out double h)) thresholds[WarningSeverity.High] = h;
                if (cfg.TryGetValue("MEDIUM", out double m)) thresholds[WarningSeverity.Medium] = m;
                if (cfg.TryGetValue("LOW", out double l)) thresholds[WarningSeverity.Low] = l;
            }
            return thresholds;
        }

        // HIGH-08: SLAThresholdsHours is a mutable view — LoadSLAThresholds() populates it;
        // callers that only need a snapshot should use GetSLAThresholds() instead.
        // Initialised from GetSLAThresholds() defaults to avoid duplication.
        /// Configurable via WARNING_SLA_*_HOURS keys in project_config.json.</summary>
        internal static Dictionary<WarningSeverity, double> SLAThresholdsHours = new()
        {
            { WarningSeverity.Critical, 4 },     // 4 hours — overridden by LoadSLAThresholds()
            { WarningSeverity.High, 24 },
            { WarningSeverity.Medium, 168 },
            { WarningSeverity.Low, 336 },
            { WarningSeverity.Info, double.MaxValue }
        };

        /// <summary>Load SLA thresholds from project_config.json with current defaults as fallbacks.</summary>
        internal static void LoadSLAThresholds()
        {
            try
            {
                double critical = TagConfig.GetConfigDouble("WARNING_SLA_CRITICAL_HOURS", 4);
                double high = TagConfig.GetConfigDouble("WARNING_SLA_HIGH_HOURS", 24);
                double medium = TagConfig.GetConfigDouble("WARNING_SLA_MEDIUM_HOURS", 168);
                double low = TagConfig.GetConfigDouble("WARNING_SLA_LOW_HOURS", 336);

                SLAThresholdsHours = new Dictionary<WarningSeverity, double>
                {
                    { WarningSeverity.Critical, critical },
                    { WarningSeverity.High, high },
                    { WarningSeverity.Medium, medium },
                    { WarningSeverity.Low, low },
                    { WarningSeverity.Info, double.MaxValue }
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadSLAThresholds: {ex.Message} — using defaults");
            }
        }

        /// <summary>Phase 48: Check for SLA violations against warning baseline timestamps.
        /// Returns count of warnings exceeding their severity-specific SLA.</summary>
        internal static int CheckWarningSLAViolations(Document doc, WarningReport report)
        {
            int violations = 0;
            try
            {
                // Load baseline timestamp to calculate warning age
                string baselinePath = GetBaselinePath(doc);
                DateTime baselineTime = DateTime.Now.AddHours(-48); // Default: assume 48h old if no baseline
                if (baselinePath != null && File.Exists(baselinePath))
                {
                    try
                    {
                        string json = File.ReadAllText(baselinePath);
                        int dateIdx = json.IndexOf("\"date\":\"");
                        if (dateIdx >= 0)
                        {
                            int s = dateIdx + 8;
                            int e = json.IndexOf('"', s);
                            if (e > s && DateTime.TryParse(json.Substring(s, e - s), out DateTime dt))
                                baselineTime = dt;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"SLA baseline parse: {ex.Message}"); }
                }

                double hoursOld = (DateTime.Now - baselineTime).TotalHours;
                // GAP-FIX: Use configurable SLA thresholds
                var slaLimits = GetSLAThresholds();
                foreach (var sev in new[] { WarningSeverity.Critical, WarningSeverity.High, WarningSeverity.Medium, WarningSeverity.Low })
                {
                    if (report.BySeverity.TryGetValue(sev, out int count) && count > 0)
                    {
                        if (slaLimits.TryGetValue(sev, out double limit) && hoursOld > limit)
                            violations += count;
                    }
                }
                report.SLAViolations = violations;
            }
            catch (Exception ex) { StingLog.Warn($"CheckWarningSLAViolations: {ex.Message}"); }
            return violations;
        }

        /// <summary>Phase 48: Save extended baseline with warning type tracking for regression analysis.</summary>
        internal static void SaveExtendedBaseline(Document doc)
        {
            try
            {
                string path = GetBaselinePath(doc);
                if (path == null) return;

                var warnings = doc.GetWarnings();
                int count = warnings?.Count ?? 0;

                // A2: Load existing first-seen timestamps so we don't overwrite them
                var firstSeen = LoadFirstSeenTimestamps(path);
                string now = DateTime.Now.ToString("o");

                // Build warning type array with per-type first-seen timestamps
                var typeEntries = new List<string>();
                if (warnings != null)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var fm in warnings)
                    {
                        string desc = fm.GetDescriptionText();
                        if (string.IsNullOrEmpty(desc) || !seen.Add(desc)) continue;

                        // Preserve existing first-seen date, or stamp new
                        string firstSeenDate = firstSeen.TryGetValue(desc, out string fsDate) ? fsDate : now;
                        string escaped = desc.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        typeEntries.Add($"{{\"desc\":\"{escaped}\",\"first_seen\":\"{firstSeenDate}\",\"count\":1}}");
                    }
                }

                var sb = new StringBuilder();
                sb.Append("{");
                sb.Append($"\"version\":3,");
                sb.Append($"\"total\":{count},");
                sb.Append($"\"date\":\"{now}\",");
                sb.Append($"\"user\":\"{Environment.UserName ?? "unknown"}\",");
                sb.Append("\"warning_types\":[");
                sb.Append(string.Join(",", typeEntries));
                sb.Append("]");
                sb.Append("}");

                // R2-FIX: Atomic write using File.Replace (no crash window between Delete and Move)
                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, sb.ToString(), Encoding.UTF8);
                try { File.Replace(tempPath, path, path + ".bak"); }
                catch { if (File.Exists(tempPath)) { File.Copy(tempPath, path, true); try { File.Delete(tempPath); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); } } }

                StingLog.Info($"Extended warning baseline saved: {count} warnings, {typeEntries.Count} types with first-seen timestamps");
            }
            catch (Exception ex) { StingLog.Warn($"SaveExtendedBaseline: {ex.Message}"); }
        }

        /// <summary>A2: Load per-warning first-seen timestamps from existing baseline sidecar.</summary>
        private static Dictionary<string, string> LoadFirstSeenTimestamps(string baselinePath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(baselinePath)) return result;
                string json = File.ReadAllText(baselinePath);
                var obj = JObject.Parse(json);
                var types = obj["warning_types"] as JArray;
                if (types == null) return result;
                foreach (var item in types)
                {
                    if (item is JObject jObj)
                    {
                        string desc = jObj["desc"]?.ToString();
                        string fs = jObj["first_seen"]?.ToString();
                        if (!string.IsNullOrEmpty(desc) && !string.IsNullOrEmpty(fs))
                            result[desc] = fs;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadFirstSeenTimestamps: {ex.Message}"); }
            return result;
        }

        /// <summary>A2: Check per-warning SLA violations using first-seen timestamps.
        /// Returns list of warnings exceeding their SLA threshold based on individual age.</summary>
        internal static List<(string Description, double AgeHours, double SLAHours, WarningSeverity Severity)>
            CheckPerWarningSLAViolations(Document doc, WarningReport report)
        {
            var violations = new List<(string, double, double, WarningSeverity)>();
            try
            {
                string path = GetBaselinePath(doc);
                if (path == null || !File.Exists(path)) return violations;

                var firstSeen = LoadFirstSeenTimestamps(path);
                if (firstSeen.Count == 0) return violations;

                var now = DateTime.Now;
                // HIGH-07: Use configurable SLA thresholds instead of hardcoded values
                var slaThresholds = GetSLAThresholds();
                foreach (var group in report.RootCauseGroups)
                {
                    if (!firstSeen.TryGetValue(group.Description, out string fsStr)) continue;
                    if (!DateTime.TryParse(fsStr, out DateTime fsDate)) continue;

                    double ageHours = (now - fsDate).TotalHours;
                    double slaHours = slaThresholds.TryGetValue(group.Severity, out double sv) ? sv : 336;

                    if (ageHours > slaHours)
                        violations.Add((group.Description, ageHours, slaHours, group.Severity));
                }
            }
            catch (Exception ex) { StingLog.Warn($"CheckPerWarningSLAViolations: {ex.Message}"); }
            return violations;
        }

        /// <summary>Phase 48: Build top-N warnings per category for drill-down tooltips.</summary>
        internal static void BuildTopWarningsByCategory(WarningReport report)
        {
            if (report?.Warnings == null) return;
            report.TopWarningsByCategory.Clear();

            var groups = report.Warnings.GroupBy(w => w.Category);
            foreach (var group in groups)
            {
                var topDescs = group
                    .GroupBy(w => w.Description)
                    .OrderByDescending(g => g.Count())
                    .Take(3)
                    .Select(g => (g.Key.Length > 80 ? g.Key.Substring(0, 77) + "..." : g.Key, g.Count()))
                    .ToList();
                report.TopWarningsByCategory[group.Key] = topDescs;
            }
        }

        /// <summary>R4-D A1: Build root-cause groups — deduplicates warnings by description.
        /// Reduces 200 "duplicate instances" warnings into 1 group with count=200.
        /// Sorted by count descending so highest-impact groups appear first.</summary>
        internal static void BuildRootCauseGroups(WarningReport report)
        {
            if (report?.Warnings == null || report.Warnings.Count == 0) return;
            report.RootCauseGroups.Clear();

            var grouped = report.Warnings
                .GroupBy(w => w.Description ?? "", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count());

            foreach (var g in grouped)
            {
                var first = g.First();
                var allElements = new List<ElementId>();
                foreach (var w in g)
                {
                    if (w.FailingElements != null)
                        allElements.AddRange(w.FailingElements);
                }

                report.RootCauseGroups.Add(new RootCauseGroup
                {
                    Description = first.Description ?? "",
                    Category = first.Category,
                    Severity = first.Severity,
                    Count = g.Count(),
                    CanAutoFix = first.CanAutoFix,
                    FixStrategy = first.FixStrategy,
                    AllElements = allElements.Distinct().ToList()
                });
            }
        }

        // ── Phase 68: BIM Coordinator Action Plan Generator ──────────

        /// <summary>
        /// Generates a prioritized action plan for BIM coordinators based on current
        /// model state: warnings, compliance, issues, stale elements. Returns actions
        /// sorted by impact score (highest first). Each action includes command tag
        /// for one-click execution.
        /// </summary>
        internal static List<(string Action, string CommandTag, string Priority, int ImpactScore, string Rationale)>
            GenerateActionPlan(Document doc, WarningReport warnings, ComplianceScan.ComplianceResult compliance)
        {
            var actions = new List<(string Action, string CommandTag, string Priority, int ImpactScore, string Rationale)>();
            if (doc == null) return actions;

            try
            {
                // 1. Critical warnings first — blocks handover
                int critical = warnings?.BySeverity.GetValueOrDefault(WarningSeverity.Critical) ?? 0;
                if (critical > 0)
                    actions.Add(($"Fix {critical} critical warning(s)", "AutoFixWarnings", "CRITICAL", 100,
                        "Critical warnings block ISO 19650 handover and may cause data loss"));

                // 2. Stale elements — tag accuracy
                int stale = compliance?.StaleCount ?? 0;
                if (stale > 0)
                    actions.Add(($"Re-tag {stale} stale element(s)", "RetagStale", "HIGH", 90,
                        "Stale elements have moved/changed — tags no longer match spatial context"));

                // 3. Low compliance — tag coverage
                double tagPct = compliance?.CompliancePercent ?? 0;
                if (tagPct < 80)
                    actions.Add(($"Improve tag compliance from {tagPct:F0}% to 80%+", "BatchTag", "HIGH", 85,
                        "ISO 19650 requires asset identification for information exchange"));
                else if (tagPct < 95)
                    actions.Add(($"Raise tag compliance from {tagPct:F0}% to 95%+", "TagNewOnly", "MEDIUM", 60,
                        "Near-complete tagging enables reliable COBie export"));

                // 4. Container gaps — COBie readiness
                double containerPct = compliance?.ContainerCompletePct ?? 100;
                if (containerPct < 90)
                    actions.Add(($"Fill container gaps (currently {containerPct:F0}%)", "CombineParameters", "HIGH", 80,
                        "Discipline containers must be populated for COBie/schedule accuracy"));

                // 5. Placeholder tokens — data quality
                int placeholders = compliance?.PlaceholderCount ?? 0;
                if (placeholders > 0)
                    actions.Add(($"Resolve {placeholders} placeholder token(s) (GEN/XX/ZZ)", "ResolveAllIssues", "MEDIUM", 70,
                        "Placeholder codes indicate incomplete spatial/system classification"));

                // 6. High warnings — model quality
                int high = warnings?.BySeverity.GetValueOrDefault(WarningSeverity.High) ?? 0;
                if (high > 10)
                    actions.Add(($"Address {high} high-severity warning(s)", "WarningsDashboard", "MEDIUM", 55,
                        "High-severity warnings affect COBie export and model quality"));

                // 7. Auto-fixable warnings — quick wins
                int autoFixable = warnings?.AutoFixable ?? 0;
                if (autoFixable > 5)
                    actions.Add(($"Auto-fix {autoFixable} warning(s) in one click", "AutoFixWarnings", "LOW", 40,
                        "Auto-fixable warnings can be resolved without manual intervention"));

                // 8. Validate tags for ISO compliance
                if (tagPct >= 80)
                    actions.Add(("Run ISO 19650 tag validation", "ValidateTags", "MEDIUM", 50,
                        "Cross-validates DISC↔SYS, SYS↔FUNC, FUNC↔PROD per CIBSE/Uniclass"));

                // 9. Template audit — view consistency
                actions.Add(("Audit view templates for compliance", "TemplateAudit", "LOW", 30,
                    "Ensures all views use STING templates with correct VG settings"));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GenerateActionPlan: {ex.Message}");
            }

            // Sort by impact score descending
            actions.Sort((a, b) => b.ImpactScore.CompareTo(a.ImpactScore));
            return actions;
        }

        /// <summary>
        /// Phase 68: Calculates model readiness score for a specific BIM deliverable.
        /// Returns 0-100 score with breakdown per criterion.
        /// </summary>
        internal static (int Score, List<(string Check, bool Passed, string Detail)> Checks)
            CalculateDeliverableReadiness(Document doc, string deliverable, WarningReport warnings, ComplianceScan.ComplianceResult compliance)
        {
            var checks = new List<(string Check, bool Passed, string Detail)>();
            if (doc == null) return (0, checks);

            try
            {
                double tagPct = compliance?.CompliancePercent ?? 0;
                double containerPct = compliance?.ContainerCompletePct ?? 100;
                int stale = compliance?.StaleCount ?? 0;
                int critical = warnings?.BySeverity.GetValueOrDefault(WarningSeverity.Critical) ?? 0;
                int total = warnings?.Total ?? 0;

                switch (deliverable?.ToUpperInvariant())
                {
                    case "COBIE":
                        checks.Add(("Tag compliance ≥ 90%", tagPct >= 90, $"{tagPct:F0}%"));
                        checks.Add(("Container completion ≥ 95%", containerPct >= 95, $"{containerPct:F0}%"));
                        checks.Add(("No stale elements", stale == 0, stale > 0 ? $"{stale} stale" : "OK"));
                        checks.Add(("No critical warnings", critical == 0, critical > 0 ? $"{critical} critical" : "OK"));
                        checks.Add(("No placeholder tokens", (compliance?.PlaceholderCount ?? 0) == 0,
                            (compliance?.PlaceholderCount ?? 0) > 0 ? $"{compliance.PlaceholderCount} placeholders" : "OK"));
                        break;
                    case "IFC":
                        checks.Add(("Tag compliance ≥ 70%", tagPct >= 70, $"{tagPct:F0}%"));
                        checks.Add(("No critical geometric warnings", critical == 0, critical > 0 ? $"{critical}" : "OK"));
                        int geomWarnings = warnings?.ByCategory.GetValueOrDefault(WarningCategory.Geometric) ?? 0;
                        checks.Add(("Geometric warnings < 20", geomWarnings < 20, $"{geomWarnings} geometric"));
                        break;
                    case "PDF":
                    case "DRAWINGS":
                        checks.Add(("No empty sheets",
                            !(warnings?.Warnings?.Any(w => w.Description?.Contains("empty sheet") == true) ?? false), "checked"));
                        checks.Add(("Sheet naming compliant", true, "run SheetNamingCheck"));
                        int annotationWarnings = warnings?.ByCategory.GetValueOrDefault(WarningCategory.Annotation) ?? 0;
                        checks.Add(("Annotation warnings < 10", annotationWarnings < 10, $"{annotationWarnings}"));
                        break;
                    case "FM":
                    case "HANDOVER":
                        checks.Add(("Tag compliance ≥ 95%", tagPct >= 95, $"{tagPct:F0}%"));
                        checks.Add(("Container completion ≥ 98%", containerPct >= 98, $"{containerPct:F0}%"));
                        checks.Add(("No stale elements", stale == 0, stale > 0 ? $"{stale}" : "OK"));
                        checks.Add(("No critical warnings", critical == 0, critical > 0 ? $"{critical}" : "OK"));
                        // HIGH-09: compute once, use twice — avoid redundant call to CalculateWarningHealthScore
                        int healthScore = CalculateWarningHealthScore(warnings);
                        checks.Add(("Warning health ≥ 80", healthScore >= 80, $"{healthScore}"));
                        int spatialWarnings = warnings?.ByCategory.GetValueOrDefault(WarningCategory.Spatial) ?? 0;
                        checks.Add(("No spatial warnings", spatialWarnings == 0, $"{spatialWarnings}"));
                        break;
                    default:
                        checks.Add(("Tag compliance ≥ 80%", tagPct >= 80, $"{tagPct:F0}%"));
                        checks.Add(("No critical warnings", critical == 0, critical > 0 ? $"{critical}" : "OK"));
                        break;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CalculateDeliverableReadiness: {ex.Message}");
            }

            int passed = checks.Count(c => c.Passed);
            int score = checks.Count > 0 ? (int)(100.0 * passed / checks.Count) : 0;
            return (score, checks);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  TRANSACTION-LEVEL WARNING HANDLER (IFailuresPreprocessor)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intercepts warnings during STING transactions. Three modes:
    /// Silent — dismiss all warnings (for batch operations).
    /// Selective — auto-resolve known fixable, keep unknown (default).
    /// Strict — fail transaction on any warning (for compliance-gated operations).
    /// </summary>
    internal class StingWarningHandler : IFailuresPreprocessor
    {
        internal enum HandlerMode { Silent, Selective, Strict }

        private readonly HandlerMode _mode;
        private readonly List<string> _encountered = new();

        public StingWarningHandler(HandlerMode mode = HandlerMode.Selective)
        {
            _mode = mode;
        }

        /// <summary>Warnings encountered during this transaction.</summary>
        public IReadOnlyList<string> EncounteredWarnings => _encountered;
        public int WarningCount => _encountered.Count;

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            if (failures == null || failures.Count == 0)
                return FailureProcessingResult.Continue;

            foreach (FailureMessageAccessor fma in failures)
            {
                FailureSeverity severity = fma.GetSeverity();
                string desc = fma.GetDescriptionText() ?? "";
                _encountered.Add(desc);

                // DocumentCorruption — always rollback
                if (severity == FailureSeverity.DocumentCorruption)
                    return FailureProcessingResult.ProceedWithRollBack;

                if (severity == FailureSeverity.Error)
                {
                    // Errors cannot be dismissed — try resolution via BuiltInFailures IDs first
                    if (fma.HasResolutions())
                    {
                        fma.SetCurrentResolutionType(FailureResolutionType.Default);
                        failuresAccessor.ResolveFailure(fma);
                    }
                    continue;
                }

                // Warning handling based on mode
                switch (_mode)
                {
                    case HandlerMode.Silent:
                        failuresAccessor.DeleteWarning(fma);
                        break;

                    case HandlerMode.Selective:
                        var (_, _, _, autoFix) = WarningsEngine.ClassifyWarning(desc);
                        if (autoFix && fma.HasResolutions())
                        {
                            fma.SetCurrentResolutionType(FailureResolutionType.Default);
                            failuresAccessor.ResolveFailure(fma);
                        }
                        else
                        {
                            failuresAccessor.DeleteWarning(fma);
                        }
                        break;

                    case HandlerMode.Strict:
                        // Don't dismiss — let transaction fail
                        return FailureProcessingResult.ProceedWithRollBack;
                }
            }
            return FailureProcessingResult.ProceedWithCommit;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Phase 55: AUTO-ISSUE CREATION (continuation of WarningsEngine)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Extended warnings engine: auto-issue creation from critical warnings.</summary>
    internal static class WarningsEngineExt
    {
        /// <summary>
        /// Auto-create issues from CRITICAL/HIGH severity warnings.
        /// Bridges the gap between Revit warnings (alerts) and STING issues (work orders).
        /// </summary>
        internal static int AutoCreateIssuesFromWarnings(Document doc, WarningReport report,
            WarningSeverity minSeverity = WarningSeverity.Critical)
        {
            if (doc == null || report == null || report.Warnings.Count == 0) return 0;

            int created = 0;
            try
            {
                // Load existing issues to check for duplicates
                string issuesPath = Path.Combine(
                    Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                    "_bim_manager", "issues.json");

                var existingIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int maxExistingId = 0;
                if (File.Exists(issuesPath))
                {
                    try
                    {
                        string json = File.ReadAllText(issuesPath);
                        var arr = Newtonsoft.Json.Linq.JArray.Parse(json);
                        foreach (var item in arr)
                        {
                            string desc = item["description"]?.ToString() ?? "";
                            existingIssues.Add(desc);
                            // Scan for max numeric ID suffix to prevent collision after deletions
                            string idStr = item["id"]?.ToString() ?? "";
                            int dashIdx = idStr.LastIndexOf('-');
                            if (dashIdx >= 0 && int.TryParse(idStr.Substring(dashIdx + 1), out int num) && num > maxExistingId)
                                maxExistingId = num;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Load issues for dedup: {ex.Message}"); }
                }

                // Filter warnings by minimum severity
                var targetWarnings = report.Warnings.Where(w =>
                    w.Severity <= minSeverity && // Critical=0, High=1, so <= works
                    !string.IsNullOrEmpty(w.Description));

                // Group by description to avoid creating 50 issues for same warning type
                var grouped = targetWarnings
                    .GroupBy(w => w.Description.Length > 80 ? w.Description.Substring(0, 80) : w.Description)
                    .Take(20); // Cap at 20 issue types

                var newIssues = new List<object>();
                int nextId = maxExistingId + 1;

                foreach (var group in grouped)
                {
                    string desc = $"[AUTO] {group.Key}";
                    if (existingIssues.Contains(desc)) continue; // Already tracked

                    var first = group.First();
                    string issueType = first.Severity == WarningSeverity.Critical ? "NCR" : "SI";
                    string priority = first.Severity == WarningSeverity.Critical ? "CRITICAL" : "HIGH";

                    var elementIds = group.SelectMany(w => w.FailingElements ?? Enumerable.Empty<ElementId>())
                        .Select(id => id.Value.ToString()).Distinct().Take(10).ToList();

                    newIssues.Add(new
                    {
                        id = $"{issueType}-{nextId:D4}",
                        title = desc,
                        description = $"{group.Count()} warning(s): {group.Key}",
                        type = issueType,
                        priority = priority,
                        status = "OPEN",
                        discipline = first.Discipline ?? "GEN",
                        assignee = "BIM Manager",
                        created_date = DateTime.Now.ToString("o"),
                        created_by = "STING Auto",
                        auto_created = true,
                        warning_category = first.Category.ToString(),
                        affected_elements = elementIds,
                        element_count = group.Sum(w => (w.FailingElements?.Count ?? 0))
                    });
                    nextId++;
                    created++;
                }

                if (newIssues.Count > 0)
                {
                    // Append to issues.json
                    string dir = Path.GetDirectoryName(issuesPath) ?? "";
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    Newtonsoft.Json.Linq.JArray arr;
                    if (File.Exists(issuesPath))
                    {
                        try { arr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(issuesPath)); }
                        catch (Exception ex) { StingLog.Warn($"ParseJArray: {ex.Message}"); arr = new Newtonsoft.Json.Linq.JArray(); }
                    }
                    else arr = new Newtonsoft.Json.Linq.JArray();

                    foreach (var issue in newIssues)
                        arr.Add(Newtonsoft.Json.Linq.JObject.FromObject(issue));

                    // Phase 87: Atomic write to prevent corruption on crash
                    string tmpPath = issuesPath + ".tmp";
                    File.WriteAllText(tmpPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    if (File.Exists(issuesPath))
                        File.Replace(tmpPath, issuesPath, issuesPath + ".bak");
                    else
                        File.Move(tmpPath, issuesPath);
                    StingLog.Info($"AutoCreateIssuesFromWarnings: created {created} issues from {minSeverity}+ warnings");
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoCreateIssuesFromWarnings: {ex.Message}"); }
            return created;
        }
        // ── TAG-STALE-WARN-01: Auto-raise issues from stale elements ──────

        /// <summary>
        /// Checks for stale elements and auto-creates a HIGH-priority SI issue
        /// when stale count exceeds zero. Deduplicates against existing open issues.
        /// </summary>
        internal static int AutoRaiseStaleIssues(Document doc)
        {
            if (doc == null) return 0;
            int created = 0;
            try
            {
                var cached = ComplianceScan.GetCached() ?? ComplianceScan.Scan(doc);
                if (cached == null || cached.StaleCount <= 0) return 0;

                string bimDir = BIMManager.GapFixEngine.GetBimDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return 0;
                string issuesPath = System.IO.Path.Combine(bimDir, "issues.json");

                Newtonsoft.Json.Linq.JArray arr;
                if (File.Exists(issuesPath))
                {
                    string raw = File.ReadAllText(issuesPath);
                    arr = string.IsNullOrWhiteSpace(raw)
                        ? new Newtonsoft.Json.Linq.JArray()
                        : Newtonsoft.Json.Linq.JArray.Parse(raw);
                }
                else
                {
                    arr = new Newtonsoft.Json.Linq.JArray();
                }

                // Deduplicate: skip if any OPEN issue already covers stale elements
                foreach (var item in arr)
                {
                    string status = item["status"]?.ToString() ?? "";
                    string title = item["title"]?.ToString() ?? "";
                    if (status == "OPEN" && title.Contains("stale", StringComparison.OrdinalIgnoreCase))
                        return 0;
                }

                // Find next ID
                int maxNum = 0;
                foreach (var item in arr)
                {
                    string id = item["id"]?.ToString() ?? "";
                    int dashIdx = id.LastIndexOf('-');
                    if (dashIdx >= 0 && int.TryParse(id.Substring(dashIdx + 1), out int num))
                        maxNum = Math.Max(maxNum, num);
                }
                string nextId = $"SI-{(maxNum + 1).ToString("D4")}";

                string rev = "";
                try { rev = PhaseAutoDetect.DetectProjectRevision(doc); }
                catch (Exception ex) { StingLog.Warn($"AutoRaiseStaleIssues rev detect: {ex.Message}"); }

                var issue = new Newtonsoft.Json.Linq.JObject
                {
                    ["id"] = nextId,
                    ["type"] = "SI",
                    ["title"] = $"{cached.StaleCount} stale elements require re-tagging",
                    ["description"] = $"Model contains {cached.StaleCount} elements whose tags are stale (geometry/level/spatial data changed since last tag). Run Retag Stale to resolve.",
                    ["priority"] = "HIGH",
                    ["status"] = "OPEN",
                    ["discipline"] = "",
                    ["revision"] = rev,
                    ["element_ids"] = "",
                    ["created_by"] = Environment.UserName,
                    ["created_date"] = DateTime.UtcNow.ToString("o"),
                    ["modified_by"] = Environment.UserName,
                    ["modified_date"] = DateTime.UtcNow.ToString("o"),
                    ["auto_created"] = true,
                    ["source"] = "TAG-STALE-WARN-01"
                };
                arr.Add(issue);
                created = 1;

                // Atomic write
                if (!Directory.Exists(bimDir))
                    Directory.CreateDirectory(bimDir);
                string tmpPath = issuesPath + ".tmp";
                File.WriteAllText(tmpPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                if (File.Exists(issuesPath))
                    File.Replace(tmpPath, issuesPath, issuesPath + ".bak");
                else
                    File.Move(tmpPath, issuesPath);

                StingLog.Info($"AutoRaiseStaleIssues: created {nextId} for {cached.StaleCount} stale elements");
            }
            catch (Exception ex) { StingLog.Warn($"AutoRaiseStaleIssues: {ex.Message}"); }
            return created;
        }
    } // end WarningsEngineExt

    // ══════════════════════════════════════════════════════════════════
    //  COMMANDS (8 IExternalCommand classes)
        // ══════════════════════════════════════════════════════════════
        // Phase 55: AUTO-ISSUE CREATION FROM CRITICAL WARNINGS
        // Cross-system automation: warning → issue pipeline
        // ══════════════════════════════════════════════════════════════

    /// <summary>Extended warnings engine: auto-issue creation from critical warnings.</summary>
    internal static class WarningsEngineExt
    {
        /// <summary>
        /// Auto-create issues from CRITICAL/HIGH severity warnings.
        /// Bridges the gap between Revit warnings (alerts) and STING issues (work orders).
        /// </summary>
        internal static int AutoCreateIssuesFromWarnings(Document doc, WarningReport report,
            WarningSeverity minSeverity = WarningSeverity.Critical)
        {
            if (doc == null || report == null || report.Warnings.Count == 0) return 0;

            int created = 0;
            try
            {
                // Load existing issues to check for duplicates
                string issuesPath = Path.Combine(
                    Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                    "_bim_manager", "issues.json");

                var existingIssues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(issuesPath))
                {
                    try
                    {
                        string json = File.ReadAllText(issuesPath);
                        var arr = Newtonsoft.Json.Linq.JArray.Parse(json);
                        foreach (var item in arr)
                        {
                            string desc = item["description"]?.ToString() ?? "";
                            existingIssues.Add(desc);
                            // Scan for max numeric ID suffix to prevent collision after deletions
                            string idStr = item["id"]?.ToString() ?? "";
                            int dashIdx = idStr.LastIndexOf('-');
                            if (dashIdx >= 0 && int.TryParse(idStr.Substring(dashIdx + 1), out int num) && num > maxExistingId)
                                maxExistingId = num;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Load issues for dedup: {ex.Message}"); }
                }

                // Filter warnings by minimum severity
                var targetWarnings = report.Warnings.Where(w =>
                    w.Severity <= minSeverity && // Critical=0, High=1, so <= works
                    !string.IsNullOrEmpty(w.Description));

                // Group by description to avoid creating 50 issues for same warning type
                var grouped = targetWarnings
                    .GroupBy(w => w.Description.Length > 80 ? w.Description.Substring(0, 80) : w.Description)
                    .Take(20); // Cap at 20 issue types

                var newIssues = new List<object>();
                int nextId = maxExistingId + 1;

                foreach (var group in grouped)
                {
                    string desc = $"[AUTO] {group.Key}";
                    if (existingIssues.Contains(desc)) continue; // Already tracked

                    var first = group.First();
                    string issueType = first.Severity == WarningSeverity.Critical ? "NCR" : "SI";
                    string priority = first.Severity == WarningSeverity.Critical ? "CRITICAL" : "HIGH";

                    var elementIds = group.SelectMany(w => w.FailingElements ?? Enumerable.Empty<ElementId>())
                        .Select(id => id.Value.ToString()).Distinct().Take(10).ToList();

                    newIssues.Add(new
                    {
                        id = $"{issueType}-{nextId:D4}",
                        title = desc,
                        description = $"{group.Count()} warning(s): {group.Key}",
                        type = issueType,
                        priority = priority,
                        status = "OPEN",
                        discipline = first.Discipline ?? "GEN",
                        assignee = "BIM Manager",
                        created_date = DateTime.Now.ToString("o"),
                        created_by = "STING Auto",
                        auto_created = true,
                        warning_category = first.Category.ToString(),
                        affected_elements = elementIds,
                        element_count = group.Sum(w => (w.FailingElements?.Count ?? 0))
                    });
                    nextId++;
                    created++;
                }

                if (newIssues.Count > 0)
                {
                    // Append to issues.json
                    string dir = Path.GetDirectoryName(issuesPath) ?? "";
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    Newtonsoft.Json.Linq.JArray arr;
                    if (File.Exists(issuesPath))
                    {
                        try { arr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(issuesPath)); }
                        catch (Exception ex) { StingLog.Warn($"ParseJArray: {ex.Message}"); arr = new Newtonsoft.Json.Linq.JArray(); }
                    }
                    else arr = new Newtonsoft.Json.Linq.JArray();

                    foreach (var issue in newIssues)
                        arr.Add(Newtonsoft.Json.Linq.JObject.FromObject(issue));

                    // Phase 87: Atomic write to prevent corruption on crash
                    string tmpPath = issuesPath + ".tmp";
                    File.WriteAllText(tmpPath, arr.ToString(Newtonsoft.Json.Formatting.Indented));
                    if (File.Exists(issuesPath))
                        File.Replace(tmpPath, issuesPath, issuesPath + ".bak");
                    else
                        File.Move(tmpPath, issuesPath);
                    StingLog.Info($"AutoCreateIssuesFromWarnings: created {created} issues from {minSeverity}+ warnings");
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoCreateIssuesFromWarnings: {ex.Message}"); }
            return created;
        }
    } // end WarningsEngineExt

    // ══════════════════════════════════════════════════════════════════
    //  COMMANDS (8 IExternalCommand classes)
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Comprehensive warnings dashboard showing categorised breakdown, severity distribution,
    /// trend vs baseline, hotspot elements, and per-level/discipline/workset analysis.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                int healthScore = WarningsEngine.CalculateWarningHealthScore(report);

                // Build rich WPF result panel
                var builder = UI.StingResultPanel.Create("STING Warnings Dashboard")
                    .SetSubtitle($"{report.Total} warnings {report.TrendSymbol}  |  Health: {healthScore}/100");

                // ── Summary section ──
                int critical = report.BySeverity.TryGetValue(WarningSeverity.Critical, out int c) ? c : 0;
                int high = report.BySeverity.TryGetValue(WarningSeverity.High, out int h) ? h : 0;
                int medium = report.BySeverity.TryGetValue(WarningSeverity.Medium, out int m) ? m : 0;
                int low = report.BySeverity.TryGetValue(WarningSeverity.Low, out int l) ? l : 0;
                int info = report.BySeverity.TryGetValue(WarningSeverity.Info, out int inf) ? inf : 0;

                builder.AddSection("Summary")
                    .Metric("Total Warnings", report.Total.ToString())
                    .Metric("Health Score", $"{healthScore} / 100",
                        healthScore >= 80 ? "GOOD" : healthScore >= 50 ? "NEEDS ATTENTION" : "POOR")
                    .RAGBar(healthScore);

                if (critical > 0)
                    builder.MetricError("Critical", critical.ToString(), "Requires immediate attention");
                else
                    builder.MetricHighlight("Critical", "0", "No critical warnings");
                if (high > 0) builder.MetricWarn("High", high.ToString());
                if (medium > 0) builder.Metric("Medium", medium.ToString());
                if (low > 0) builder.Metric("Low", low.ToString());
                if (info > 0) builder.Metric("Info", info.ToString());

                builder.Separator()
                    .MetricHighlight("Auto-fixable", report.AutoFixable.ToString(), "Can be fixed automatically")
                    .Metric("Manual review", report.ManualReview.ToString());

                // ── Baseline trend ──
                if (report.BaselineTotal.HasValue)
                {
                    builder.AddSection("Trend vs Baseline")
                        .Metric("Baseline", report.BaselineTotal.Value.ToString())
                        .Metric("Current", report.Total.ToString())
                        .Metric("Delta", report.TrendSymbol,
                            report.Total < report.BaselineTotal ? "Improving" :
                            report.Total > report.BaselineTotal ? "Worsening" : "Stable");
                }

                // ── By Category table ──
                if (report.ByCategory.Count > 0)
                {
                    var catRows = report.ByCategory.OrderByDescending(x => x.Value)
                        .Select(kv => new[] { kv.Key.ToString(), kv.Value.ToString(),
                            $"{(double)kv.Value / Math.Max(report.Total, 1) * 100:F0}%" })
                        .ToList();
                    builder.AddSection("By Category")
                        .Table(new[] { "Category", "Count", "%" }, catRows);
                }

                // ── By Severity table ──
                {
                    var sevRows = new List<string[]>();
                    foreach (WarningSeverity sev in Enum.GetValues(typeof(WarningSeverity)))
                        if (report.BySeverity.TryGetValue(sev, out int cnt) && cnt > 0)
                            sevRows.Add(new[] { sev.ToString(), cnt.ToString(),
                                $"{(double)cnt / Math.Max(report.Total, 1) * 100:F0}%" });
                    if (sevRows.Count > 0)
                        builder.AddSection("By Severity")
                            .Table(new[] { "Severity", "Count", "%" }, sevRows);
                }

                // ── By Discipline table ──
                if (report.ByDiscipline.Count > 0)
                {
                    var discRows = report.ByDiscipline.OrderByDescending(x => x.Value)
                        .Select(kv => new[] { kv.Key, kv.Value.ToString() }).ToList();
                    builder.AddSection("By Discipline")
                        .Table(new[] { "Discipline", "Count" }, discRows);
                }

                // ── By Level table (top 10) ──
                if (report.ByLevel.Count > 0)
                {
                    var lvlRows = report.ByLevel.OrderByDescending(x => x.Value).Take(10)
                        .Select(kv => new[] { kv.Key, kv.Value.ToString() }).ToList();
                    builder.AddSection("By Level (top 10)")
                        .Table(new[] { "Level", "Count" }, lvlRows);
                }

                // ── Hotspot elements (top 10) ──
                if (report.Hotspots.Count > 0)
                {
                    var hotRows = report.Hotspots.Take(10)
                        .Select(hs => new[] { hs.Item2, hs.Item3.ToString() }).ToList();
                    builder.AddSection("Hotspot Elements (most warnings)")
                        .Table(new[] { "Element", "Warnings" }, hotRows);
                }

                // ── SLA violations ──
                if (report.SLAViolations > 0)
                {
                    builder.AddSection("SLA Violations")
                        .MetricError("Overdue", report.SLAViolations.ToString(), "Warnings exceeding SLA thresholds")
                        .Metric("Avg Critical Age", $"{report.AvgCriticalAgeHours:F1} hours");
                }

                // Build plain text for clipboard/fallback
                var sb = new StringBuilder();
                sb.AppendLine($"STING Warnings Dashboard — {report.Total} total {report.TrendSymbol}");
                sb.AppendLine($"Health Score: {healthScore}/100");
                sb.AppendLine($"Critical: {critical}  High: {high}  Medium: {medium}  Low: {low}");
                sb.AppendLine($"Auto-fixable: {report.AutoFixable}  Manual: {report.ManualReview}");
                if (report.ByCategory.Count > 0)
                {
                    sb.AppendLine("\nBy Category:");
                    foreach (var kv in report.ByCategory.OrderByDescending(x => x.Value))
                        sb.AppendLine($"  {kv.Key}: {kv.Value}");
                }
                if (report.Hotspots.Count > 0)
                {
                    sb.AppendLine("\nHotspot Elements:");
                    foreach (var (id, name, count) in report.Hotspots.Take(10))
                        sb.AppendLine($"  {name}: {count} warnings");
                }
                builder.SetRawText(sb.ToString());

                builder.Show();

                StingLog.Info($"WarningsDashboard: {report.Total} total, {report.AutoFixable} fixable, health={healthScore}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("WarningsDashboard failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Scan and auto-fix all fixable warnings in a single transaction.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsAutoFixCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                if (report.AutoFixable == 0)
                {
                    TaskDialog.Show("STING Warnings", "No auto-fixable warnings found.");
                    return Result.Succeeded;
                }

                // Preview
                var dlg = new TaskDialog("STING Auto-Fix Warnings");
                dlg.MainInstruction = $"{report.AutoFixable} auto-fixable warnings found";
                dlg.MainContent = "Fix strategies:\n" +
                    string.Join("\n", report.Warnings
                        .Where(w => w.CanAutoFix)
                        .GroupBy(w => w.FixStrategy)
                        .Select(g => $"  • {g.Key}: {g.Count()}"));
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fix all auto-fixable warnings");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Dry-run preview (no changes)");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel");
                var result = dlg.Show();

                if (result == TaskDialogResult.CommandLink3) return Result.Cancelled;
                bool dryRun = result == TaskDialogResult.CommandLink2;

                var fixReport = WarningsEngine.BatchAutoFix(doc, report.Warnings, dryRun);

                var sb = new StringBuilder();
                sb.AppendLine(dryRun ? "DRY-RUN RESULTS:" : "AUTO-FIX RESULTS:");
                sb.AppendLine($"  Fixed:   {fixReport.Fixed}");
                sb.AppendLine($"  Skipped: {fixReport.Skipped}");
                sb.AppendLine($"  Failed:  {fixReport.Failed}");
                if (fixReport.Details.Count > 0)
                {
                    sb.AppendLine("\nDetails (first 20):");
                    foreach (string d in fixReport.Details.Take(20))
                        sb.AppendLine($"  {d}");
                }
                TaskDialog.Show("STING Auto-Fix", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("WarningsAutoFix failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Export all warnings to CSV for external tracking.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                string path = WarningsEngine.ExportToCSV(doc, report);
                TaskDialog.Show("STING Warnings Export",
                    $"Exported {report.Total} warnings to:\n{path}");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Save current warning count as baseline for trend tracking.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsBaselineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                int? prev = WarningsEngine.LoadBaseline(doc);
                int current = doc.GetWarnings()?.Count ?? 0;
                WarningsEngine.SaveBaseline(doc);

                string msg = prev.HasValue
                    ? $"Baseline updated: {prev.Value} → {current} ({(current > prev.Value ? "↑" : current < prev.Value ? "↓" : "→")}{Math.Abs(current - prev.Value)})"
                    : $"Baseline saved: {current} warnings";
                TaskDialog.Show("STING Warning Baseline", msg);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Select elements associated with a specific warning type.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsSelectElementsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                if (report.Total == 0) { TaskDialog.Show("STING", "No warnings found."); return Result.Succeeded; }

                // Group by description for picker
                var groups = report.Warnings
                    .GroupBy(w => w.Description)
                    .OrderByDescending(g => g.Count())
                    .Take(30)
                    .Select(g => new StingTools.Select.StingListPicker.ListItem
                    {
                        Label = $"({g.Count()}) {g.Key}",
                        Detail = $"{g.First().Category} | {g.First().Severity}",
                        Tag = g.Key
                    })
                    .ToList();

                var picked = StingTools.Select.StingListPicker.Show("Select Warning Type",
                    "Pick a warning type to select its elements:", groups, allowMultiSelect: false);
                if (picked == null || picked.Count == 0) return Result.Cancelled;

                string selectedDesc = picked[0].Tag as string;
                var matchingWarnings = report.Warnings.Where(w => w.Description == selectedDesc);
                var allIds = new HashSet<ElementId>();
                foreach (var cw in matchingWarnings)
                {
                    if (cw.FailingElements != null)
                        foreach (var id in cw.FailingElements) allIds.Add(id);
                    if (cw.AdditionalElements != null)
                        foreach (var id in cw.AdditionalElements) allIds.Add(id);
                }

                if (allIds.Count > 0)
                {
                    uidoc.Selection.SetElementIds(allIds.ToList());
                    TaskDialog.Show("STING", $"Selected {allIds.Count} elements with warning:\n{selectedDesc}");
                }
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Add warning patterns to the suppression list (hidden from dashboard).</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsSuppressCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                var groups = report.Warnings
                    .GroupBy(w => w.Description)
                    .OrderByDescending(g => g.Count())
                    .Take(30)
                    .Select(g => new StingTools.Select.StingListPicker.ListItem
                    {
                        Label = $"({g.Count()}) {g.Key}",
                        Detail = $"{g.First().Category} | Suppress this warning type",
                        Tag = g.Key
                    })
                    .ToList();

                var picked = StingTools.Select.StingListPicker.Show("Suppress Warning Types",
                    "Select warnings to suppress from dashboard:", groups, allowMultiSelect: true);
                if (picked == null || picked.Count == 0) return Result.Cancelled;

                int count = 0;
                foreach (var item in picked)
                {
                    string desc = item.Tag as string;
                    if (!string.IsNullOrEmpty(desc))
                    {
                        // Use first 50 chars as pattern to avoid overly specific matching
                        string pattern = desc.Length > 50 ? desc.Substring(0, 50) : desc;
                        WarningsEngine.AddSuppression(pattern);
                        count++;
                    }
                }
                TaskDialog.Show("STING", $"Suppressed {count} warning pattern(s).\nThey will be hidden from future dashboard scans.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Compare warnings to ISO 19650 / BS 7671 / CIBSE compliance requirements.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsComplianceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var report = WarningsEngine.ScanWarnings(doc);
                var sb = new StringBuilder();
                sb.AppendLine("STING Warnings Compliance Report");
                sb.AppendLine(new string('═', 45));

                // ISO 19650 compliance: spatial and data warnings
                int spatial = report.ByCategory.GetValueOrDefault(WarningCategory.Spatial);
                int data = report.ByCategory.GetValueOrDefault(WarningCategory.Data);
                int mep = report.ByCategory.GetValueOrDefault(WarningCategory.MEP);
                int compliance = report.ByCategory.GetValueOrDefault(WarningCategory.Compliance);
                int critical = report.BySeverity.GetValueOrDefault(WarningSeverity.Critical);

                sb.AppendLine($"\n■ ISO 19650 — Information Management:");
                sb.AppendLine($"  Spatial integrity:     {(spatial == 0 ? "PASS ✓" : $"FAIL ✗ ({spatial} issues)")}");
                sb.AppendLine($"  Data consistency:      {(data == 0 ? "PASS ✓" : $"FAIL ✗ ({data} issues)")}");
                sb.AppendLine($"  Critical warnings:     {(critical == 0 ? "PASS ✓" : $"FAIL ✗ ({critical} critical)")}");

                sb.AppendLine($"\n■ Building Services (CIBSE / BS 7671):");
                sb.AppendLine($"  MEP connectivity:      {(mep == 0 ? "PASS ✓" : $"REVIEW ({mep} issues)")}");

                sb.AppendLine($"\n■ Compliance Standards:");
                sb.AppendLine($"  Fire/accessibility:    {(compliance == 0 ? "PASS ✓" : $"REVIEW ({compliance} issues)")}");

                // Overall verdict
                bool pass = critical == 0 && spatial <= 2 && data <= 5;
                sb.AppendLine($"\n■ OVERALL: {(pass ? "COMPLIANT ✓" : "NON-COMPLIANT ✗ — resolve critical/spatial issues")}");

                TaskDialog.Show("STING Compliance Report", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    /// <summary>Monitor warning count before/after a command — detect warning regression.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class WarningsMonitorCommand : IExternalCommand
    {
        private static int? _preCommandCount;

        /// <summary>Call before a major command to snapshot warning count.</summary>
        internal static void SnapshotBefore(Document doc)
        {
            try { _preCommandCount = doc?.GetWarnings()?.Count; }
            catch (Exception ex) { StingLog.Warn($"WarningSnapshot: {ex.Message}"); _preCommandCount = null; }
        }

        /// <summary>Call after a command — shows alert if warnings increased.</summary>
        internal static void CheckAfter(Document doc, string commandName)
        {
            try
            {
                if (!_preCommandCount.HasValue || doc == null) return;
                int after = doc.GetWarnings()?.Count ?? 0;
                int delta = after - _preCommandCount.Value;
                if (delta > 0)
                {
                    StingLog.Warn($"WarningsMonitor: {commandName} introduced {delta} new warning(s) ({_preCommandCount.Value} → {after})");
                }
                _preCommandCount = null;
            }
            catch (Exception ex) { StingLog.Warn($"WarningsMonitor.CheckAfter: {ex.Message}"); }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
                if (doc == null) return Result.Failed;
                int count = doc.GetWarnings()?.Count ?? 0;
                TaskDialog.Show("STING Warning Monitor",
                    $"Current warnings: {count}\n" +
                    $"Last pre-command snapshot: {(_preCommandCount.HasValue ? _preCommandCount.Value.ToString() : "none")}\n\n" +
                    "The monitor automatically tracks warning count before/after major STING commands.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Phase 47: BIM COORDINATION CENTER — Unified dashboard command
    // ══════════════════════════════════════════════════════════════════

    // ══════════════════════════════════════════════════════════════════
    //  Phase 63: MODEL HEALTH SCORING ENGINE
    //  Comprehensive model quality assessment with weighted scoring
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Phase 63: Comprehensive model health score (0-100) with per-category breakdown.
    /// Combines warnings, compliance, data quality, performance, and standards into single metric.</summary>
    internal static class ModelHealthScorer
    {
        public class HealthScore
        {
            public double Overall { get; set; }
            public double Warnings { get; set; }       // 0-25: from WarningsEngine health
            public double Compliance { get; set; }      // 0-25: from ComplianceScan
            public double DataQuality { get; set; }     // 0-25: containers, TAG7, STATUS
            public double Performance { get; set; }     // 0-25: model size, groups, links
            public string RAG => Overall >= 80 ? "GREEN" : Overall >= 50 ? "AMBER" : "RED";
            public List<string> Recommendations { get; set; } = new List<string>();
        }

        /// <summary>Calculate comprehensive model health score.</summary>
        internal static HealthScore Calculate(Document doc)
        {
            var score = new HealthScore();

            // 1. Warnings score (25 points)
            try
            {
                var wr = WarningsEngine.ScanWarnings(doc);
                double warnHealth = WarningsEngine.CalculateWarningHealthScore(wr);
                score.Warnings = Math.Max(0, warnHealth / 4.0); // Scale 0-100 to 0-25
                int wrCritical = wr.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0);
                if (wrCritical > 0)
                    score.Recommendations.Add($"Fix {wrCritical} critical warnings immediately");
                if (wr.AutoFixable > 5)
                    score.Recommendations.Add($"Auto-fix {wr.AutoFixable} warnings to improve score quickly");
            }
            catch (Exception ex) { StingLog.Warn($"HealthScore warnings: {ex.Message}"); }

            // 2. Compliance score (25 points)
            try
            {
                ComplianceScan.InvalidateCache();
                var comp = ComplianceScan.Scan(doc);
                score.Compliance = comp.CompliancePercent / 4.0;
                if (comp.Untagged > 50)
                    score.Recommendations.Add($"Tag {comp.Untagged} untagged elements (Batch Tag)");
                if (comp.StaleCount > 0)
                    score.Recommendations.Add($"Retag {comp.StaleCount} stale elements (Retag Stale)");
            }
            catch (Exception ex) { StingLog.Warn($"HealthScore compliance: {ex.Message}"); }

            // 3. Data quality score (25 points)
            try
            {
                int totalTagged = 0, withContainers = 0, withTag7 = 0, withStatus = 0;
                var elems = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                    .ToList();

                foreach (var el in elems.Take(5000)) // Sample for performance
                {
                    string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (string.IsNullOrEmpty(tag1)) continue;
                    totalTagged++;

                    string tag2 = ParameterHelpers.GetString(el, "ASS_TAG_2_TXT");
                    if (!string.IsNullOrEmpty(tag2)) withContainers++;

                    string tag7 = ParameterHelpers.GetString(el, "ASS_TAG_7_TXT");
                    if (!string.IsNullOrEmpty(tag7)) withTag7++;

                    string status = ParameterHelpers.GetString(el, "ASS_STATUS_TXT");
                    if (!string.IsNullOrEmpty(status)) withStatus++;
                }

                if (totalTagged > 0)
                {
                    double containerPct = withContainers * 100.0 / totalTagged;
                    double tag7Pct = withTag7 * 100.0 / totalTagged;
                    double statusPct = withStatus * 100.0 / totalTagged;
                    score.DataQuality = (containerPct * 0.4 + tag7Pct * 0.3 + statusPct * 0.3) / 4.0;

                    if (containerPct < 80)
                        score.Recommendations.Add("Run Combine Parameters to populate discipline containers");
                    if (statusPct < 50)
                        score.Recommendations.Add("Set STATUS tokens (NEW/EXISTING/DEMOLISHED)");
                }
            }
            catch (Exception ex) { StingLog.Warn($"HealthScore data quality: {ex.Message}"); }

            // 4. Performance score (25 points)
            try
            {
                double perfScore = 25.0;
                int elementCount = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                // Penalty for very large models
                if (elementCount > 500000) perfScore -= 5;
                else if (elementCount > 200000) perfScore -= 2;

                // Penalty for excessive model groups
                int groups = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Group))
                    .GetElementCount();
                if (groups > 100) perfScore -= 3;

                // Penalty for unresolved links
                int links = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .GetElementCount();
                if (links > 20) perfScore -= 2;

                score.Performance = Math.Max(0, perfScore);
                if (elementCount > 500000)
                    score.Recommendations.Add("Consider splitting model — over 500K elements");
            }
            catch (Exception ex) { StingLog.Warn($"HealthScore performance: {ex.Message}"); }

            score.Overall = score.Warnings + score.Compliance + score.DataQuality + score.Performance;
            return score;
        }
    }

    /// <summary>Phase 63: Comprehensive model health report with scoring.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ModelHealthScoreCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var score = ModelHealthScorer.Calculate(ctx.Doc);

            var report = new StringBuilder();
            report.AppendLine($"MODEL HEALTH SCORE: {score.Overall:F0}/100 ({score.RAG})\n");
            report.AppendLine($"  Warnings:    {score.Warnings:F0}/25");
            report.AppendLine($"  Compliance:  {score.Compliance:F0}/25");
            report.AppendLine($"  Data Quality: {score.DataQuality:F0}/25");
            report.AppendLine($"  Performance: {score.Performance:F0}/25\n");

            if (score.Recommendations.Count > 0)
            {
                report.AppendLine("RECOMMENDATIONS:");
                foreach (string r in score.Recommendations)
                    report.AppendLine($"  → {r}");
            }

            TaskDialog.Show("STING Model Health", report.ToString());
            return Result.Succeeded;
        }
    }

    /// <summary>Phase 47: Open unified BIM Coordination Center with all dashboards merged.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    /// <summary>ExternalEvent handler for BCC modeless action dispatch.</summary>
    internal class BCCActionEventHandler : IExternalEventHandler
    {
        private volatile string _pendingAction = null;
        internal void Post(string action) => _pendingAction = action;

        public void Execute(UIApplication app)
        {
            string action = System.Threading.Interlocked.Exchange(ref _pendingAction, null);
            if (!string.IsNullOrEmpty(action))
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc != null)
                    BIMCoordinationCenterCommand.ProcessAction(action, doc, app);
            }
        }
        public string GetName() => "BCCAction";
    }

    public class BIMCoordinationCenterCommand : IExternalCommand
    {
        private static BCCActionEventHandler _bccHandler;
        private static ExternalEvent _bccEvent;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = ParameterHelpers.GetApp(commandData);
                var uidoc = uiApp?.ActiveUIDocument;
                Document doc = uidoc?.Document;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                // Phase 76: Singleton — if BCC is already open, just activate it
                if (UI.BIMCoordinationCenter.CurrentInstance != null)
                {
                    UI.BIMCoordinationCenter.CurrentInstance.Dispatcher.Invoke(() =>
                    {
                        UI.BIMCoordinationCenter.CurrentInstance.Activate();
                        UI.BIMCoordinationCenter.CurrentInstance.Focus();
                    });
                    return Result.Succeeded;
                }

                // Create ExternalEvent for modeless dispatch (once per Revit session)
                if (_bccHandler == null)
                {
                    _bccHandler = new BCCActionEventHandler();
                    _bccEvent   = ExternalEvent.Create(_bccHandler);
                }

                // Wire ActionDispatcher so BCC button clicks go through ExternalEvent
                UI.BIMCoordinationCenter.ActionDispatcher = action =>
                {
                    _bccHandler.Post(action);
                    _bccEvent.Raise();
                };

                var coordData = BuildCoordData(doc);
                if (coordData == null) { message = "Could not build coordination data."; return Result.Failed; }

                UI.BIMCoordinationCenter.Show(coordData);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BIMCoordinationCenter failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>Build CoordData for the unified WPF dialog. Can be called from StingCommandHandler loop.</summary>
        // ══════════════════════════════════════════════════════════════
        //  GAP-COORD-01: SMART ACTION INFERENCE ENGINE
        //  Analyses model state and suggests next actions for coordinator
        // ══════════════════════════════════════════════════════════════

        /// <summary>GAP-COORD-01: Infers next best actions for BIM coordinator based on
        /// current model state (compliance, warnings, stale, issues).</summary>
        internal static List<(string Action, string Reason, string CommandTag, string Priority)>
            InferNextActions(Document doc, ComplianceScan.ComplianceResult comp, WarningReport warnings, int openIssues)
        {
            var actions = new List<(string, string, string, string)>();

            // Stale elements → retag first
            if (comp.StaleCount > 0)
                actions.Add(("Retag Stale Elements", $"{comp.StaleCount} elements moved since last tag", "RetagStale", "HIGH"));

            // Critical warnings → fix immediately
            int warnCritical = warnings?.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0) ?? 0;
            if (warnCritical > 0)
                actions.Add(("Fix Critical Warnings", $"{warnCritical} critical warnings need attention", "WarningsAutoFix", "CRITICAL"));

            // Low compliance → batch tag
            if (comp.CompliancePercent < 80 && comp.Untagged > 10)
                actions.Add(("Batch Tag Untagged", $"{comp.Untagged} untagged elements ({comp.CompliancePercent:F0}%)", "BatchTag", "HIGH"));

            // Auto-fixable warnings → quick win
            if ((warnings?.AutoFixable ?? 0) > 5)
                actions.Add(("Auto-Fix Warnings", $"{warnings.AutoFixable} warnings can be auto-fixed", "WarningsAutoFix", "MEDIUM"));

            // Placeholders → resolve
            if (comp.PlaceholderCount > 20)
                actions.Add(("Resolve Placeholders", $"{comp.PlaceholderCount} elements with GEN/XX/ZZ placeholders", "ResolveAllIssues", "MEDIUM"));

            // High compliance → export ready
            if (comp.CompliancePercent >= 90 && warnCritical == 0)
                actions.Add(("Generate Weekly Report", "Model ready for coordination report", "WeeklyReport", "LOW"));

            // Open issues → review
            if (openIssues > 5)
                actions.Add(("Review Open Issues", $"{openIssues} open issues require action", "IssueDashboard", "MEDIUM"));

            // Containers incomplete → combine
            if (comp.ContainerCompletePct < 80 && comp.TaggedComplete > 0)
                actions.Add(("Run Combine Parameters", $"Container completion at {comp.ContainerCompletePct:F0}%", "CombineParameters", "HIGH"));

            return actions.OrderByDescending(a => a.Item4 == "CRITICAL" ? 4 :
                a.Item4 == "HIGH" ? 3 : a.Item4 == "MEDIUM" ? 2 : 1).Take(5).ToList();
        }

        // ══════════════════════════════════════════════════════════════
        //  GAP-COORD-02: DOCUMENT-ISSUE CROSS-LINKING
        // ══════════════════════════════════════════════════════════════

        /// <summary>GAP-COORD-02: Links documents to issues for ISO 19650 traceability.</summary>
        internal static class DocumentIssueLinkEngine
        {
            private static string GetLinksPath(Document doc)
            {
                string docPath = doc?.PathName;
                if (string.IsNullOrEmpty(docPath)) return null;
                string dir = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "issue_doc_links.json");
            }

            /// <summary>Link a document to an issue.</summary>
            internal static void LinkDocumentToIssue(Document doc, string issueId, string documentId, string linkType = "RESPONSE")
            {
                try
                {
                    string path = GetLinksPath(doc);
                    if (path == null) return;

                    JArray links = File.Exists(path) ? JArray.Parse(File.ReadAllText(path)) : new JArray();
                    links.Add(new JObject
                    {
                        ["issue_id"] = issueId,
                        ["document_id"] = documentId,
                        ["link_type"] = linkType,
                        ["created_date"] = DateTime.Now.ToString("o"),
                        ["created_by"] = Environment.UserName
                    });
                    File.WriteAllText(path, links.ToString(Newtonsoft.Json.Formatting.Indented));
                    StingLog.Info($"GAP-COORD-02: Linked {documentId} → {issueId} ({linkType})");
                }
                catch (Exception ex) { StingLog.Warn($"DocumentIssueLink: {ex.Message}"); }
            }

            /// <summary>Get all documents linked to an issue.</summary>
            internal static List<string> GetDocumentsForIssue(Document doc, string issueId)
            {
                try
                {
                    string path = GetLinksPath(doc);
                    if (path == null || !File.Exists(path)) return new List<string>();
                    var links = JArray.Parse(File.ReadAllText(path));
                    return links.Where(l => l["issue_id"]?.ToString() == issueId)
                        .Select(l => l["document_id"]?.ToString()).Where(d => d != null).ToList();
                }
                catch (Exception ex) { StingLog.Warn($"LoadSuppression: {ex.Message}"); return new List<string>(); }
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  GAP-COORD-03: CDE STATE VALIDATION BEFORE HANDOVER
        // ══════════════════════════════════════════════════════════════

        /// <summary>GAP-COORD-03: Pre-handover CDE state validation.
        /// Checks all documents are in correct state before deliverable export.</summary>
        internal static (bool Pass, List<string> Issues) ValidateCDEHandoverReadiness(Document doc)
        {
            var issues = new List<string>();
            try
            {
                string docDir = Path.GetDirectoryName(doc.PathName) ?? "";
                string docsPath = Path.Combine(docDir, "_bim_manager", "documents.json");
                string issuesPath = Path.Combine(docDir, "_bim_manager", "issues.json");

                // Check documents state
                if (File.Exists(docsPath))
                {
                    var docs = JArray.Parse(File.ReadAllText(docsPath));
                    int wipCount = 0, sharedCount = 0;
                    foreach (JObject d in docs)
                    {
                        string cde = d["cde_status"]?.ToString() ?? "WIP";
                        if (cde == "WIP") wipCount++;
                        else if (cde == "SHARED") sharedCount++;
                    }
                    if (wipCount > 0) issues.Add($"{wipCount} documents still in WIP status (must be SHARED before handover)");
                }

                // Check open critical issues
                if (File.Exists(issuesPath))
                {
                    var allIssues = JArray.Parse(File.ReadAllText(issuesPath));
                    int critOpen = allIssues.Count(i =>
                        i["status"]?.ToString() != "CLOSED" &&
                        i["priority"]?.ToString() == "CRITICAL");
                    if (critOpen > 0) issues.Add($"{critOpen} CRITICAL issues still open");
                }

                // Check compliance threshold
                var comp = ComplianceScan.Scan(doc);
                double minCompliance = TagConfig.GetConfigDouble("CDE_PUBLISHED_MIN_COMPLIANCE", 90);
                if (comp.CompliancePercent < minCompliance)
                    issues.Add($"Tag compliance {comp.CompliancePercent:F0}% below {minCompliance:F0}% threshold");

                if (comp.StaleCount > 0)
                    issues.Add($"{comp.StaleCount} stale elements need retagging");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ValidateCDEHandoverReadiness: {ex.Message}");
                issues.Add($"Validation error: {ex.Message}");
            }

            return (issues.Count == 0, issues);
        }

        internal static UI.BIMCoordinationCenter.CoordData BuildCoordData(Document doc)
        {
            try
            {
                // PERF-CRIT: Use cached compliance result if fresh (30s TTL).
                // Previously called InvalidateCache() + Scan() on EVERY dialog open/refresh,
                // causing a full-model element scan (2-5s) each time. In the keep-dialog-open
                // loop, this ran after every button click (5 clicks = 5 full scans = 10-25s).
                var compliance = ComplianceScan.Scan(doc);

                // PERF: Use cached warning report (30s TTL added in Phase 71b)
                var warningReport = WarningsEngine.ScanWarnings(doc);
                int healthScore = WarningsEngine.CalculateWarningHealthScore(warningReport);

                // 3. Load issues from issues.json
                int openIssues = 0, criticalIssues = 0;
                try
                {
                    string docPath = doc.PathName;
                    if (!string.IsNullOrEmpty(docPath))
                    {
                        string issuesPath = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager", "issues.json");
                        if (File.Exists(issuesPath))
                        {
                            string raw = File.ReadAllText(issuesPath);
                            // Count OPEN issues
                            int idx = 0;
                            while ((idx = raw.IndexOf("\"status\":\"OPEN\"", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                            { openIssues++; idx++; }
                            idx = 0;
                            while ((idx = raw.IndexOf("\"priority\":\"CRITICAL\"", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                            { criticalIssues++; idx++; }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter issues load: {ex.Message}"); }

                // 4. Load workflow history — PERF: read file ONCE for both summary and rows
                int workflowRuns = 0;
                string lastWorkflow = "none";
                var workflowHistoryRows = new List<UI.BIMCoordinationCenter.WorkflowRunRow>();
                try
                {
                    string docPath = doc.PathName;
                    if (!string.IsNullOrEmpty(docPath))
                    {
                        string logPath = Path.Combine(Path.GetDirectoryName(docPath), "STING_WORKFLOW_LOG.json");
                        if (File.Exists(logPath))
                        {
                            // Single file read for both summary and detailed rows
                            string[] logLines = File.ReadAllLines(logPath);
                            workflowRuns = logLines.Length;

                            // Extract last workflow name from last line
                            if (logLines.Length > 0)
                            {
                                string lastLine = logLines[logLines.Length - 1];
                                int presetIdx = lastLine.IndexOf("\"preset\":\"");
                                if (presetIdx >= 0)
                                {
                                    int valStart = presetIdx + 10;
                                    int valEnd = lastLine.IndexOf('"', valStart);
                                    if (valEnd > valStart) lastWorkflow = lastLine.Substring(valStart, valEnd - valStart);
                                }
                            }

                            // Parse last 20 rows for DataGrid
                            foreach (string line in logLines.TakeLast(20).Reverse())
                            {
                                try
                                {
                                    var rec = Newtonsoft.Json.Linq.JObject.Parse(line);
                                    workflowHistoryRows.Add(new UI.BIMCoordinationCenter.WorkflowRunRow
                                    {
                                        Timestamp = (rec.Value<string>("timestamp") ?? "").Length > 16
                                            ? rec.Value<string>("timestamp").Substring(0, 16) : rec.Value<string>("timestamp") ?? "",
                                        Preset = rec.Value<string>("preset") ?? "",
                                        Steps = rec.Value<int?>("totalSteps") ?? 0,
                                        Passed = rec.Value<int?>("passedSteps") ?? 0,
                                        Failed = rec.Value<int?>("failedSteps") ?? 0,
                                        Skipped = rec.Value<int?>("skippedSteps") ?? 0,
                                        Duration = $"{rec.Value<double?>("durationMs") ?? 0 / 1000.0:F1}s",
                                        CompBefore = rec.Value<double?>("complianceBefore") ?? 0,
                                        CompAfter = rec.Value<double?>("complianceAfter") ?? 0,
                                        User = rec.Value<string>("user") ?? ""
                                    });
                                }
                                catch (Exception exRec) { StingLog.Warn($"Workflow history record parse: {exRec.Message}"); }
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter workflow load: {ex.Message}"); }

                // 5. Warning regression delta
                var (warnAdded, warnRemoved, warnUnchanged, newTypes) = WarningsEngine.CompareWithRevisionBaseline(doc);

                // 6. Warning gate check
                var (gatePass, gateReason) = WarningsEngine.CheckWarningGate(doc);

                // Build model health checks
                var healthChecks = new List<(string, int, int, string)>();
                var recommendations = new List<string>();
                int modelHealthScore = healthScore; // Use warning-based health as starting point
                string modelHealthRating = modelHealthScore >= 80 ? "GOOD" : modelHealthScore >= 50 ? "FAIR" : "POOR";

                // Assemble CoordData for unified WPF dialog
                string ragStatus = compliance != null ? compliance.RAGStatus : "UNKNOWN";
                double tagPct = compliance?.CompliancePercent ?? 0;
                double strictPct = compliance?.StrictPercent ?? 0;
                int staleCount = compliance?.StaleCount ?? 0;

                // Derive model health score from multiple signals
                int mhWarningScore = Math.Max(0, 10 - warningReport.Total / 10);
                int mhTagScore = (int)(tagPct / 10);
                int mhStaleScore = staleCount == 0 ? 10 : Math.Max(0, 10 - staleCount / 5);
                modelHealthScore = Math.Min(100, (mhWarningScore + mhTagScore + mhStaleScore) * 100 / 30);
                modelHealthRating = modelHealthScore >= 80 ? "GOOD" : modelHealthScore >= 50 ? "FAIR" : "POOR";

                healthChecks.Add(("Warnings", mhWarningScore, 10, $"{warningReport.Total} warnings in model"));
                healthChecks.Add(("Tag Completeness", mhTagScore, 10, $"{tagPct:F0}% complete"));
                healthChecks.Add(("Stale Elements", mhStaleScore, 10, staleCount == 0 ? "No stale elements" : $"{staleCount} stale elements"));

                if (warningReport.Total > 10) recommendations.Add("Resolve Revit warnings (currently " + warningReport.Total + ")");
                if (tagPct < 80) recommendations.Add("Run 'Batch Tag' or 'Tag & Combine' to improve tag coverage");
                if (staleCount > 0) recommendations.Add($"Re-tag {staleCount} stale elements");

                // Load revision data
                int revisionCount = 0, revisionClouds = 0;
                int cloudsUnresolved = 0;
                int revisionsThisWeek = 0;
                var cloudsBySheetDict = new Dictionary<string, int>();
                var cloudsByDisciplineDict = new Dictionary<string, int>();
                try
                {
                    var revisions = new FilteredElementCollector(doc).OfClass(typeof(Revision)).ToElements();
                    revisionCount = revisions.Count;

                    // Count revisions created this week
                    var weekAgo = DateTime.Now.AddDays(-7);
                    foreach (var rev in revisions.Cast<Revision>())
                    {
                        try
                        {
                            string dateStr = rev.RevisionDate;
                            if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out DateTime revDate))
                            {
                                if (revDate >= weekAgo) revisionsThisWeek++;
                            }
                        }
                        catch { /* date parse failure is non-critical */ }
                    }

                    // Pre-collect all revision clouds once and group by revision ID
                    var allClouds = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RevisionClouds)
                        .WhereElementIsNotElementType().ToElements();
                    var cloudsByRevId = new Dictionary<ElementId, int>();

                    // Build set of issued revision IDs for unresolved count
                    var issuedRevIds = new HashSet<ElementId>();
                    foreach (var rev in revisions.Cast<Revision>())
                    {
                        if (!rev.Issued) issuedRevIds.Add(rev.Id);
                    }

                    foreach (var c in allClouds)
                    {
                        var revId = c.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION)?.AsElementId();
                        if (revId != null && revId != ElementId.InvalidElementId)
                        {
                            cloudsByRevId.TryGetValue(revId, out int cnt);
                            cloudsByRevId[revId] = cnt + 1;

                            // Count clouds on non-issued (draft) revisions as unresolved
                            if (issuedRevIds.Contains(revId))
                                cloudsUnresolved++;
                        }
                        else
                        {
                            // Cloud with no valid revision is unresolved
                            cloudsUnresolved++;
                        }

                        // Clouds by sheet
                        try
                        {
                            var ownerViewId = c.OwnerViewId;
                            if (ownerViewId != null && ownerViewId != ElementId.InvalidElementId)
                            {
                                var ownerView = doc.GetElement(ownerViewId);
                                string sheetKey = null;
                                if (ownerView is ViewSheet vs)
                                {
                                    sheetKey = $"{vs.SheetNumber} - {vs.Name}";
                                }
                                else if (ownerView is View v)
                                {
                                    // Find sheet hosting this view
                                    var titleParam = v.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NUMBER);
                                    if (titleParam != null)
                                    {
                                        string sheetNum = titleParam.AsString();
                                        if (!string.IsNullOrEmpty(sheetNum))
                                            sheetKey = sheetNum;
                                    }
                                }
                                if (!string.IsNullOrEmpty(sheetKey))
                                {
                                    cloudsBySheetDict.TryGetValue(sheetKey, out int sCnt);
                                    cloudsBySheetDict[sheetKey] = sCnt + 1;
                                }
                            }
                        }
                        catch (Exception shEx) { StingLog.Warn($"Cloud sheet lookup: {shEx.Message}"); }

                        // Clouds by discipline (derive from revision description or cloud's view discipline)
                        try
                        {
                            string disc = "General";
                            var ownerView2 = c.OwnerViewId != null && c.OwnerViewId != ElementId.InvalidElementId
                                ? doc.GetElement(c.OwnerViewId) as View : null;
                            if (ownerView2 != null)
                            {
                                var viewDisc = ownerView2.get_Parameter(BuiltInParameter.VIEW_DISCIPLINE);
                                if (viewDisc != null)
                                {
                                    int discVal = viewDisc.AsInteger();
                                    disc = discVal switch
                                    {
                                        1 => "Architectural",
                                        2 => "Structural",
                                        4 => "Mechanical",
                                        16 => "Electrical",
                                        32 => "Plumbing",
                                        4095 => "Coordination",
                                        _ => "General"
                                    };
                                }
                            }
                            cloudsByDisciplineDict.TryGetValue(disc, out int dCnt);
                            cloudsByDisciplineDict[disc] = dCnt + 1;
                        }
                        catch (Exception dEx) { StingLog.Warn($"Cloud discipline lookup: {dEx.Message}"); }
                    }

                    foreach (var rev in revisions.Cast<Revision>())
                    {
                        cloudsByRevId.TryGetValue(rev.Id, out int clouds);
                        revisionClouds += clouds;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter revision load: {ex.Message}"); }

                // Platform sync info
                string lastSyncTime = "";
                int syncChanges = 0;
                try
                {
                    string docPath2 = doc.PathName;
                    if (!string.IsNullOrEmpty(docPath2))
                    {
                        string syncPath = Path.Combine(Path.GetDirectoryName(docPath2), "_bim_manager", "platform_sync.json");
                        if (File.Exists(syncPath))
                        {
                            string syncRaw = File.ReadAllText(syncPath);
                            int tsIdx = syncRaw.IndexOf("\"last_sync\":\"");
                            if (tsIdx >= 0) { int s = tsIdx + 13; int e = syncRaw.IndexOf('"', s); if (e > s) lastSyncTime = syncRaw.Substring(s, e - s); }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter sync load: {ex.Message}"); }

                // Phase 48: Load overdue issue count and issue rows for DataGrid
                int issuesOverdue = 0;
                int issuesTotal2 = 0;
                var issueRows = new List<UI.BIMCoordinationCenter.IssueRow>();
                try
                {
                    string docPath3 = doc.PathName;
                    if (!string.IsNullOrEmpty(docPath3))
                    {
                        string issuesPath2 = Path.Combine(Path.GetDirectoryName(docPath3), "_bim_manager", "issues.json");
                        if (File.Exists(issuesPath2))
                        {
                            var arr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(issuesPath2));
                            issuesTotal2 = arr.Count;
                            foreach (var item in arr)
                            {
                                string st = item.Value<string>("status") ?? "";
                                bool overdue = false;
                                string created = item.Value<string>("created_date")
                                    ?? item.Value<string>("createdAt")
                                    ?? item.Value<string>("date_raised") ?? "";
                                string daysOpen = "";
                                if (DateTime.TryParse(created, out DateTime cDate))
                                {
                                    int d = (int)(DateTime.Now - cDate).TotalDays;
                                    daysOpen = d < 1 ? "<1d" : d < 7 ? $"{d}d" : d < 30 ? $"{d/7}w" : $"{d/30}mo";
                                    string pri = item.Value<string>("priority") ?? "";
                                    double slaHours = pri == "CRITICAL" ? 4 : pri == "HIGH" ? 24 : pri == "MEDIUM" ? 168 : 336;
                                    if (st == "OPEN" && (DateTime.Now - cDate).TotalHours > slaHours) { overdue = true; issuesOverdue++; }
                                }
                                // Multi-assignee: read "assignees" JSON array, fallback to single "assignee"
                                var assigneeList = new List<string>();
                                var assigneesArr = item["assignees"] as Newtonsoft.Json.Linq.JArray;
                                if (assigneesArr != null && assigneesArr.Count > 0)
                                {
                                    foreach (var a in assigneesArr)
                                    {
                                        string av = a.Value<string>();
                                        if (!string.IsNullOrWhiteSpace(av)) assigneeList.Add(av.Trim());
                                    }
                                }
                                string singleAssignee = item.Value<string>("assignee")
                                    ?? item.Value<string>("assigned_to")
                                    ?? item.Value<string>("created_by") ?? "";
                                if (assigneeList.Count == 0 && !string.IsNullOrWhiteSpace(singleAssignee))
                                    assigneeList.Add(singleAssignee.Trim());

                                // Element count from element_ids array
                                int elemCount = 0;
                                var elemArr = item["element_ids"] as Newtonsoft.Json.Linq.JArray;
                                if (elemArr != null) elemCount = elemArr.Count;

                                // Location: prefer explicit field, fall back to lat/lng when available.
                                string location = item.Value<string>("location") ?? "";
                                if (string.IsNullOrEmpty(location))
                                {
                                    double? lat = item.Value<double?>("latitude");
                                    double? lng = item.Value<double?>("longitude");
                                    if (lat.HasValue && lng.HasValue)
                                        location = $"{lat:F5},{lng:F5}";
                                }

                                issueRows.Add(new UI.BIMCoordinationCenter.IssueRow
                                {
                                    Id = item.Value<string>("id") ?? item.Value<string>("issue_id") ?? "",
                                    Title = item.Value<string>("title") ?? "",
                                    Type = item.Value<string>("type") ?? "",
                                    Priority = item.Value<string>("priority") ?? "",
                                    Status = st,
                                    Assignee = singleAssignee,
                                    AssigneeList = assigneeList,
                                    Assignees = string.Join(", ", assigneeList),
                                    Discipline = item.Value<string>("discipline") ?? "",
                                    Revision = item.Value<string>("revision") ?? "",
                                    ElementCount = elemCount,
                                    Created = created.Length > 10 ? created.Substring(0, 10) : created,
                                    IsOverdue = overdue,
                                    DaysOpen = daysOpen,
                                    RaisedBy = item.Value<string>("raised_by")
                                               ?? item.Value<string>("created_by")
                                               ?? item.Value<string>("createdBy") ?? "",
                                    Location = location
                                });
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter issue rows: {ex.Message}"); }

                // Phase 48: Load revision rows for DataGrid
                var revisionRows = new List<UI.BIMCoordinationCenter.RevisionRow>();
                try
                {
                    var revisions2 = new FilteredElementCollector(doc).OfClass(typeof(Revision)).Cast<Revision>().ToList();
                    // Pre-collect all clouds once; group by revision ID to avoid per-revision collectors
                    var allClouds2 = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RevisionClouds)
                        .WhereElementIsNotElementType().ToElements();
                    var cloudsByRevId2 = new Dictionary<ElementId, int>();
                    foreach (var c2 in allClouds2)
                    {
                        try
                        {
                            var rid2 = c2.get_Parameter(BuiltInParameter.REVISION_CLOUD_REVISION)?.AsElementId();
                            if (rid2 != null && rid2 != ElementId.InvalidElementId)
                            {
                                cloudsByRevId2.TryGetValue(rid2, out int cnt2);
                                cloudsByRevId2[rid2] = cnt2 + 1;
                            }
                        }
                        catch (Exception ex2) { StingLog.Warn($"Revision cloud count failed: {ex2.Message}"); }
                    }
                    foreach (var rev in revisions2)
                    {
                        cloudsByRevId2.TryGetValue(rev.Id, out int clouds2);
                        revisionRows.Add(new UI.BIMCoordinationCenter.RevisionRow
                        {
                            Id = rev.Id.Value.ToString(),
                            Name = rev.Name ?? "",
                            Date = rev.RevisionDate ?? "",
                            Description = rev.Description ?? "",
                            Clouds = clouds2,
                            Status = rev.Issued ? "ISSUED" : "DRAFT"
                        });
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BIMCoordCenter revision rows: {ex.Message}"); }

                var coordData = new UI.BIMCoordinationCenter.CoordData
                {
                    ProjectName = doc.Title ?? "Untitled",
                    FilePath = doc.PathName ?? "",
                    TagPct = tagPct,
                    StrictPct = strictPct,
                    RAGStatus = ragStatus,
                    TotalElements = compliance?.TotalElements ?? 0,
                    TaggedComplete = compliance?.TaggedComplete ?? 0,
                    Untagged = compliance?.Untagged ?? 0,
                    StaleCount = staleCount,
                    SheetsTagged = compliance?.SheetsTagged ?? 0,
                    SheetsTotal = compliance?.TotalSheets ?? 0,
                    ByDisc = compliance?.ByDisc ?? new Dictionary<string, DiscComplianceData>(),
                    EmptyTokenCounts = compliance?.EmptyTokenCounts?.ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<string, int>(),
                    ContainerCompletePct = compliance?.ContainerCompletePct ?? 0,
                    ByPhase = compliance?.ByPhase?.ToDictionary(
                        x => x.Key,
                        x => (x.Value.Total, x.Value.Tagged, x.Value.CompliancePct)) ?? new Dictionary<string, (int, int, double)>(),
                    PlaceholderCount = compliance?.PlaceholderCount ?? 0,
                    WarningTotal = warningReport.Total,
                    WarningCritical = warningReport.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0),
                    WarningHigh = warningReport.BySeverity.GetValueOrDefault(WarningSeverity.High, 0),
                    WarningAutoFixable = warningReport.AutoFixable,
                    WarningHealthScore = healthScore,
                    WarningTrend = warningReport.TrendSymbol,
                    WarningGatePass = gatePass,
                    WarningGateReason = gateReason,
                    WarningAdded = warnAdded,
                    WarningRemoved = warnRemoved,
                    WarningByCategory = warningReport.ByCategory,
                    WarningBySeverity = warningReport.BySeverity,
                    WarningByLevel = warningReport.ByLevel,
                    WarningByDiscipline = warningReport.ByDiscipline,
                    WarningHotspots = warningReport.Hotspots.Select(h => (h.Name, h.Count)).ToList(),
                    WarningSLAViolations = warningReport.SLAViolations,
                    WarningTopByCategory = warningReport.TopWarningsByCategory
                        .ToDictionary(x => x.Key, x => x.Value.Select(v => (v.Desc, v.Count)).ToList()),
                    // Phase 87: Extended warning data for Ideate-level Warnings tab
                    WarningManualReview = warningReport.ManualReview,
                    WarningAvgCriticalAgeHours = warningReport.AvgCriticalAgeHours,
                    WarningByWorkset = warningReport.ByWorkset ?? new Dictionary<string, int>(),
                    WarningRootCauseGroups = (warningReport.RootCauseGroups ?? new List<RootCauseGroup>())
                        .Select(g => (g.Description, g.Category, g.Severity, g.Count, g.CanAutoFix, g.FixStrategy ?? ""))
                        .ToList(),
                    WarningImpactCOBie = warningReport.DeliverableImpact?.AffectsCOBie ?? 0,
                    WarningImpactIFC = warningReport.DeliverableImpact?.AffectsIFC ?? 0,
                    WarningImpactHandover = warningReport.DeliverableImpact?.AffectsHandover ?? 0,
                    WarningImpactSchedules = warningReport.DeliverableImpact?.AffectsSchedules ?? 0,
                    WarningImpactClash = warningReport.DeliverableImpact?.AffectsClash ?? 0,
                    WarningHighestImpactArea = warningReport.DeliverableImpact?.HighestImpactArea ?? "",
                    IssuesOpen = openIssues,
                    IssuesCritical = criticalIssues,
                    IssuesOverdue = issuesOverdue,
                    IssuesTotal = issuesTotal2,
                    Issues = issueRows,
                    RevisionCount = revisionCount,
                    RevisionClouds = revisionClouds,
                    CloudsUnresolved = cloudsUnresolved,
                    RevisionsThisWeek = revisionsThisWeek,
                    CloudsBySheet = cloudsBySheetDict,
                    CloudsByDiscipline = cloudsByDisciplineDict,
                    Revisions = revisionRows,
                    LastSyncTime = lastSyncTime,
                    SyncChanges = syncChanges,
                    WorkflowRuns = workflowRuns,
                    LastWorkflow = lastWorkflow,
                    WorkflowHistory = workflowHistoryRows,
                    ModelHealthScore = modelHealthScore,
                    ModelHealthRating = modelHealthRating,
                    HealthChecks = healthChecks,
                    Recommendations = recommendations
                };

                // Phase 49: Generate smart suggestions based on model state analysis
                coordData.SmartSuggestions = WarningsEngine.GenerateSmartSuggestions(coordData, warningReport);

                // Phase 49: Load compliance trend from workflow log
                try
                {
                    string logPath = Path.Combine(Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                        "STING_WORKFLOW_LOG.json");
                    if (File.Exists(logPath))
                    {
                        var lines = File.ReadAllLines(logPath);
                        foreach (string line in lines.TakeLast(20))
                        {
                            try
                            {
                                var rec = Newtonsoft.Json.Linq.JObject.Parse(line);
                                string ts = rec.Value<string>("timestamp") ?? "";
                                double after = rec.Value<double?>("complianceAfter") ?? 0;
                                if (DateTime.TryParse(ts, out DateTime dt) && after > 0)
                                    coordData.ComplianceTrend.Add((dt, after));
                            }
                            catch (Exception exTrend) { StingLog.Warn($"Compliance trend record: {exTrend.Message}"); }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Compliance trend load: {ex.Message}"); }

                // Phase 49: Load coordination log from sidecar
                try
                {
                    string coordLogPath = ProjectFolderEngine.GetDataPath(doc, "coord_log.json");
                    if (string.IsNullOrEmpty(coordLogPath) || !File.Exists(coordLogPath))
                    {
                        coordLogPath = Path.Combine(Path.GetDirectoryName(doc.PathName ?? "") ?? "",
                            ".sting_coord_log.json");
                    }
                    if (File.Exists(coordLogPath))
                    {
                        var logEntries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<UI.BIMCoordinationCenter.CoordLogEntry>>(
                            File.ReadAllText(coordLogPath));
                        if (logEntries != null)
                            coordData.CoordLog = logEntries.OrderByDescending(e => e.Timestamp).Take(200).ToList();
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Coord log load: {ex.Message}"); }

                // Phase 49: Cross-system correlation
                try
                {
                    // Count stale elements that also have warnings
                    int staleWithWarnings = 0;
                    var warningElementIds = new HashSet<long>();
                    foreach (var cw in warningReport.Warnings)
                    {
                        if (cw.FailingElements != null)
                            foreach (var eid in cw.FailingElements)
                                warningElementIds.Add(eid.Value);
                    }
                    if (staleCount > 0 && warningElementIds.Count > 0)
                    {
                        // Approximate: count overlap between stale and warning elements
                        staleWithWarnings = Math.Min(staleCount / 5, warningElementIds.Count / 10); // Heuristic
                    }
                    coordData.StaleLinkedToWarnings = staleWithWarnings;
                    coordData.WarningsLinkedToIssues = Math.Min(openIssues, warningReport.BySeverity.GetValueOrDefault(WarningSeverity.Critical, 0));
                    coordData.UnresolvedDependencies = (compliance?.ByDisc?.Count > 1)
                        ? compliance.ByDisc.Count(d => d.Value.CompliancePct < 50) : 0;
                }
                catch (Exception ex) { StingLog.Warn($"Cross-system correlation: {ex.Message}"); }

                // Phase 101: populate CoordData.Warnings with real WarningRow
                // data so the Warnings tab Browse / Select tree shows live
                // warnings with real element IDs. Previously the tree was
                // hardcoded to a sample string catalogue — double-clicking a
                // row produced no selection because the element IDs were
                // placeholders. Now the double-click dispatches
                // ZoomToWarning_<desc> which resolves real IDs against
                // doc.GetWarnings() and uses these rows for the element list.
                try
                {
                    coordData.Warnings = new List<UI.BIMCoordinationCenter.WarningRow>();
                    int rowIdx = 0;
                    foreach (var cw in warningReport.Warnings.Take(500))
                    {
                        var ids = cw.FailingElements?.Select(id => id.Value).ToList()
                                  ?? new List<long>();
                        coordData.Warnings.Add(new UI.BIMCoordinationCenter.WarningRow
                        {
                            Id          = $"W{rowIdx++:D4}",
                            Description = cw.Description ?? "(unknown warning)",
                            Category    = cw.Category.ToString(),
                            Severity    = cw.Severity.ToString(),
                            ElementCount= ids.Count,
                            AutoFixable = cw.CanAutoFix,
                            FixStrategy = cw.FixStrategy ?? "",
                            ElementIds  = ids
                        });
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Warnings list populate: {ex.Message}"); }

                // SLA violation detail
                try
                {
                    int slaCritical = 0, slaHigh = 0;
                    double critAgeTotal = 0; int critCount = 0;
                    foreach (var issue in issueRows)
                    {
                        if (!issue.IsOverdue) continue;
                        if (issue.Priority == "CRITICAL") { slaCritical++; }
                        else if (issue.Priority == "HIGH") { slaHigh++; }
                        if (issue.Priority == "CRITICAL" && DateTime.TryParse(issue.Created, out DateTime cd))
                        { critAgeTotal += (DateTime.Now - cd).TotalHours; critCount++; }
                    }
                    coordData.SLACriticalViolations = slaCritical;
                    coordData.SLAHighViolations = slaHigh;
                    coordData.AvgCriticalAgeHours = critCount > 0 ? critAgeTotal / critCount : 0;
                }
                catch (Exception ex) { StingLog.Warn($"SLA violations: {ex.Message}"); }

                // Compliance forecast from trend data
                try
                {
                    if (coordData.ComplianceTrend.Count >= 3)
                    {
                        var recent = coordData.ComplianceTrend.TakeLast(5).ToList();
                        double avgDelta = 0;
                        for (int i = 1; i < recent.Count; i++)
                            avgDelta += recent[i].Pct - recent[i - 1].Pct;
                        avgDelta /= Math.Max(1, recent.Count - 1);
                        double forecast = Math.Min(100, Math.Max(0, tagPct + avgDelta * 3));
                        coordData.ForecastedCompliancePct = forecast;
                        coordData.ForecastLabel = avgDelta > 0.5
                            ? $"Trending up — projected {forecast:F0}% in 3 workflow cycles (avg +{avgDelta:F1}% per cycle)"
                            : avgDelta < -0.5
                                ? $"Trending down — projected {forecast:F0}% in 3 cycles (avg {avgDelta:F1}% per cycle). Action required."
                                : $"Stable at {tagPct:F0}% — no significant trend detected";
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Compliance forecast: {ex.Message}"); }

                // Current user info
                coordData.CurrentUserName = Environment.UserName;
                coordData.FilePath = doc.PathName ?? "";

                // Phase 76: Restore permissions from project_config.json "permissions" key
                try
                {
                    if (!string.IsNullOrEmpty(doc.PathName))
                    {
                        string cfgPath = Path.Combine(Path.GetDirectoryName(doc.PathName), "project_config.json");
                        if (File.Exists(cfgPath))
                        {
                            var cfgObj = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(cfgPath));
                            var perms = cfgObj["permissions"];
                            if (perms != null)
                            {
                                var rolesArr = perms["roles"] as Newtonsoft.Json.Linq.JArray;
                                if (rolesArr != null && rolesArr.Count > 0)
                                    coordData.Roles = rolesArr.ToObject<List<UI.BIMCoordinationCenter.RoleDefinition>>();
                                var foldersArr = perms["folders"] as Newtonsoft.Json.Linq.JArray;
                                if (foldersArr != null && foldersArr.Count > 0)
                                    coordData.FolderPermissions = foldersArr.ToObject<List<UI.BIMCoordinationCenter.FolderPermission>>();
                            }
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BuildCoordData: permissions restore failed: {ex.Message}"); }

                // Phase 165 (TPL-FOLLOW-03) — populate the user's workflow queue.
                // Driven by the workflow instance store under _BIM_COORD/workflows/.
                // Failures are logged and the empty list flows through so the
                // Workflows tab shows the "inbox zero" hint instead of crashing.
                try
                {
                    string userKey = Environment.UserName ?? "";
                    var instances = Planscape.Docs.Workflow.WorkflowEngine.GetMyQueue(doc, userKey);
                    if (instances != null && instances.Count > 0)
                    {
                        var registry = Planscape.Docs.Workflow.WorkflowRegistry.Load(doc);
                        foreach (var inst in instances)
                        {
                            var def = registry?.Get(inst.WorkflowId);
                            string sla = "GREEN";
                            string dueLocal = "";
                            if (!string.IsNullOrEmpty(inst.SlaDeadline) &&
                                DateTime.TryParse(inst.SlaDeadline, out var deadline))
                            {
                                dueLocal = deadline.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                                var hoursLeft = (deadline - DateTime.UtcNow).TotalHours;
                                if (hoursLeft < 0) sla = "RED";
                                else if (hoursLeft < 8) sla = "AMBER";
                            }
                            coordData.MyQueue.Add(new UI.BIMCoordinationCenter.MyQueueRow
                            {
                                DocId = inst.DocId ?? "",
                                Subject = "",
                                Step = inst.State ?? "",
                                Workflow = def?.Name ?? inst.WorkflowId ?? "",
                                DueLocal = dueLocal,
                                SlaStatus = sla,
                            });
                        }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"BuildCoordData: My Queue load failed: {ex.Message}"); }

                StingLog.Info($"BIMCoordCenter built: health={healthScore}, warnings={warningReport.Total}, compliance={tagPct:F1}%");
                return coordData;
            }
            catch (Exception ex)
            {
                StingLog.Error("BuildCoordData failed", ex);
                return null;
            }
        }

        /// <summary>Process an action returned from the BIM Coordination Center dialog.</summary>
        internal static void ProcessAction(string action, Document doc, UIApplication app)
        {
            if (string.IsNullOrEmpty(action)) return;

            try
            {
                // Handle revision zoom/select actions
                if (action.StartsWith("ViewRevision_") || action.StartsWith("ZoomToRevision_") || action.StartsWith("SelectRevision_"))
                {
                    string revIdStr;
                    if (action.StartsWith("ViewRevision_")) revIdStr = action.Substring("ViewRevision_".Length);
                    else if (action.StartsWith("ZoomToRevision_")) revIdStr = action.Substring("ZoomToRevision_".Length);
                    else revIdStr = action.Substring("SelectRevision_".Length);
                    bool zoom3D = action.StartsWith("ZoomToRevision_");
                    try
                    {
                        var revClouds = new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_RevisionClouds)
                            .WhereElementIsNotElementType()
                            .ToList();
                        var matchingIds = new List<ElementId>();
                        foreach (var rc in revClouds)
                        {
                            if (rc is RevisionCloud cloud)
                            {
                                var revElem = doc.GetElement(cloud.RevisionId);
                                if (revElem != null && (revElem.Id.Value.ToString() == revIdStr ||
                                    (revElem is Revision r && r.SequenceNumber.ToString() == revIdStr)))
                                    matchingIds.Add(rc.Id);
                            }
                        }
                        if (matchingIds.Count > 0)
                        {
                            if (zoom3D)
                            {
                                ZoomToElementIn3D(doc, app, string.Join(",", matchingIds.Select(id => id.Value)));
                            }
                            else
                            {
                                var uidoc = app?.ActiveUIDocument;
                                if (uidoc != null)
                                {
                                    uidoc.Selection.SetElementIds(matchingIds);
                                    try { uidoc.ShowElements(matchingIds); } catch (Exception) { /* view may not support ShowElements */ }
                                }
                            }
                        }
                        else
                            TaskDialog.Show("STING", $"No revision clouds found for revision {revIdStr}.");
                    }
                    catch (Exception ex) { StingLog.Warn($"Revision action: {ex.Message}"); }
                    return;
                }

                // Handle zoom-to-element actions (3D section box)
                if (action.StartsWith("ZoomToElement_"))
                {
                    string idStr = action.Substring("ZoomToElement_".Length);
                    ZoomToElementIn3D(doc, app, idStr);
                    return;
                }

                // Handle zoom-to-warning actions (3D section box around warning elements)
                if (action.StartsWith("ZoomToWarning_"))
                {
                    string warningKey = action.Substring("ZoomToWarning_".Length);
                    ZoomToWarningIn3D(doc, app, warningKey);
                    return;
                }

                // Handle zoom-to-issue actions (3D section box around issue-linked elements)
                if (action.StartsWith("ZoomToIssue_") || action.StartsWith("SelectIssue_"))
                {
                    string issueId = action.Contains("ZoomToIssue_")
                        ? action.Substring("ZoomToIssue_".Length)
                        : action.Substring("SelectIssue_".Length);
                    bool zoom = action.StartsWith("ZoomToIssue_");
                    try
                    {
                        string docPath = doc.PathName;
                        if (!string.IsNullOrEmpty(docPath))
                        {
                            string issuesPath = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager", "issues.json");
                            if (File.Exists(issuesPath))
                            {
                                var arr = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(issuesPath));
                                var issue = arr.FirstOrDefault(i => (i.Value<string>("id") ?? "") == issueId);
                                if (issue != null)
                                {
                                    var elemIds = issue["element_ids"] as Newtonsoft.Json.Linq.JArray;
                                    if (elemIds != null && elemIds.Count > 0)
                                    {
                                        string csv = string.Join(",", elemIds.Select(e => e.ToString()));
                                        if (zoom) ZoomToElementIn3D(doc, app, csv);
                                        else
                                        {
                                            var ids = csv.Split(',').Where(s => long.TryParse(s, out _))
                                                .Select(s => new ElementId(long.Parse(s))).ToList();
                                            app?.ActiveUIDocument?.Selection.SetElementIds(ids);
                                        }
                                        return;
                                    }
                                }
                            }
                        }
                        TaskDialog.Show("STING", $"No linked elements found for issue {issueId}.");
                    }
                    catch (Exception ex) { StingLog.Warn($"ZoomToIssue: {ex.Message}"); }
                    return;
                }

                // Handle select-warning actions
                if (action.StartsWith("SelectWarning_"))
                {
                    string warningKey = action.Substring("SelectWarning_".Length);
                    SelectWarningElements(doc, app, warningKey);
                    return;
                }

                // Handle inline warning/issue actions
                switch (action)
                {
                    case "AutoFixWarnings":
                        var warningReport = WarningsEngine.ScanWarnings(doc);
                        var fixReport = WarningsEngine.BatchAutoFix(doc, warningReport.Warnings);
                        TaskDialog.Show("STING Auto-Fix", $"Fixed: {fixReport.Fixed}\nSkipped: {fixReport.Skipped}\nFailed: {fixReport.Failed}");
                        return;
                    case "CreateIssuesFromWarnings":
                        var wr2 = WarningsEngine.ScanWarnings(doc);
                        var created = WarningsEngine.CreateIssuesFromWarnings(doc, wr2.Warnings);
                        TaskDialog.Show("STING", created.Count > 0
                            ? $"Created {created.Count} issue(s):\n" + string.Join("\n", created.Select(c => $"  {c.issueId}: {c.title}"))
                            : "No critical/high warnings found.");
                        return;
                    case "ExportWarnings":
                        var wr3 = WarningsEngine.ScanWarnings(doc);
                        string csvPath = WarningsEngine.ExportToCSV(doc, wr3);
                        TaskDialog.Show("STING Export", $"Exported to:\n{csvPath}");
                        return;
                    case "SaveBaseline":
                        WarningsEngine.SaveBaseline(doc);
                        TaskDialog.Show("STING", "Warning baseline saved.");
                        return;
                    case "SaveExtendedBaseline":
                        WarningsEngine.SaveExtendedBaseline(doc);
                        TaskDialog.Show("STING", "Extended warning baseline saved.");
                        return;

                    // ── Meeting Manager actions — call DocumentManagementDialog methods directly ──
                    case "NewMeeting":
                        UI.DocumentManagementDialog.CreateMeeting(doc);
                        return;
                    case "AddActionItem":
                        UI.DocumentManagementDialog.AddActionItem(doc);
                        return;
                    case "AutoAgenda":
                        UI.DocumentManagementDialog.GenerateAutoAgenda(doc);
                        return;
                    case "LogMinutes":
                        UI.DocumentManagementDialog.LogMeetingMinutes(doc);
                        return;
                    case "MeetingTemplates":
                        UI.DocumentManagementDialog.ShowMeetingTemplates(doc);
                        return;
                    case "MeetingHistory":
                        UI.DocumentManagementDialog.ShowMeetingHistory(doc);
                        return;
                    case "OpenActions":
                        UI.DocumentManagementDialog.ShowOpenActions(doc);
                        return;
                    case "ExportMinutes":
                        UI.DocumentManagementDialog.ExportMeetingMinutes(doc);
                        return;
                    case "SendReminder":
                        UI.DocumentManagementDialog.SendMeetingReminder(doc);
                        return;

                    // ── Permission actions — handle inline ──
                    case "EditUserRole":
                        EditUserRoleInline(doc);
                        return;
                    case "SavePermissions":
                        SavePermissionsInline(doc);
                        return;

                    // Phase 96: Project Members tab actions. Previously these only worked via
                    // the StingCommandHandler path — when BCC dispatched them through its own
                    // ExternalEvent they fell through DispatchCoordAction and the user saw
                    // "Action 'SaveProjectMembers' is not handled." Route directly to the BCC
                    // WPF instance (same target StingCommandHandler uses) so both paths reach
                    // HandleProjectMembersAction.
                    case "SaveProjectMembers":
                    case "AddTeamMember":
                    case "EditTeamMember":
                    case "EditMember":
                    case "RemoveTeamMember":
                    case "RemoveMember":
                    case "AddRole":
                    case "EditRole":
                    case "DeleteRole":
                    case "ImportTeamCSV":
                    {
                        var bcc = UI.BIMCoordinationCenter.CurrentInstance;
                        if (bcc != null)
                        {
                            // HandleProjectMembersAction expects the canonical action name.
                            // Normalise EditTeamMember→EditMember / RemoveTeamMember→RemoveMember
                            // so the switch inside HandleProjectMembersAction only needs one case.
                            string normalised = action;
                            if (action == "EditTeamMember") normalised = "EditMember";
                            else if (action == "RemoveTeamMember") normalised = "RemoveMember";
                            bcc.HandleProjectMembersAction(normalised);
                        }
                        else
                        {
                            StingLog.Warn($"BCC action '{action}' dispatched but CurrentInstance is null.");
                        }
                        return;
                    }
                    case "TakeSnapshot":
                        TakeModelSnapshot(doc);
                        return;

                    // Phase 99: inline handlers for the Raise Issue form buttons.
                    // The StingCommandHandler versions just flag ExtraParams so the
                    // issue creator knows extra data is attached — we do the same
                    // here so they work from the BCC ExternalEvent path too.
                    case "CaptureIssueSnapshot":
                    {
                        StingLog.Info("View snapshot captured for issue (from BCC)");
                        UI.StingCommandHandler.SetExtraParam("IssueSnapshot", "captured");
                        return;
                    }
                    case "AttachIssueLocation":
                    {
                        var uidoc = app?.ActiveUIDocument;
                        string viewName = uidoc?.ActiveView?.Name ?? "Unknown";
                        UI.StingCommandHandler.SetExtraParam("IssueLocation", $"View: {viewName}");
                        StingLog.Info($"Issue location attached from BCC: {viewName}");
                        return;
                    }

                    // Phase 102: Planscape hub share/notification actions — lightweight
                    // clipboard/TaskDialog operations that don't need full IExternalCommand
                    // classes. Previously only wired in StingCommandHandler, which isn't
                    // on the BCC ExternalEvent path. Now handled here so "Copy Dashboard
                    // Link", "Email Report", "Teams Message", "WhatsApp Update", "Generate
                    // QR Link" and "Export HTML Dashboard" fire from the Planscape inline
                    // panel without producing "Action X is not handled" errors.
                    case "PlanscapeCopyLink":
                    {
                        string projectName = doc?.Title ?? "BIMProject";
                        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
                        string link = $"planscape://dashboard/{projectName}/{timestamp}";
                        try { System.Windows.Clipboard.SetText(link); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        TaskDialog.Show("STING — Planscape",
                            $"Dashboard link copied to clipboard:\n{link}\n\nShare with your team or embed in a QR code.");
                        return;
                    }
                    case "PlanscapeEmail":
                    {
                        string projectName = doc?.Title ?? "BIMProject";
                        string body =
                            $"Subject: {projectName} — BIM Coordination Update\n\n" +
                            $"Date: {DateTime.Today:dd MMM yyyy}\n\n" +
                            "Please review the latest coordination status in Planscape:\n" +
                            "  - Model health and warnings dashboard\n" +
                            "  - Open issues and action items\n" +
                            "  - Deliverables and revisions\n\n" +
                            "Generated by BIM Coordination Center (STINGTOOLS BCC).\n" +
                            "For the full dashboard, request the HTML export from your BIM Manager.";
                        try { System.Windows.Clipboard.SetText(body); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        TaskDialog.Show("STING — Email Report",
                            "Email draft copied to clipboard.\n\n" +
                            "Paste into your email client (Outlook, Gmail, etc.). Attach the HTML " +
                            "dashboard export for the full report.\n\n" +
                            "Tip: configure SMTP in project_config.json to enable one-click sending.");
                        return;
                    }
                    case "PlanscapeTeams":
                    {
                        string projectName = doc?.Title ?? "BIM Project";
                        string msg =
                            $"\ud83d\udcca **{projectName} — BIM Coordination Update**\n" +
                            $"\ud83d\uddd3 {DateTime.Today:dd MMM yyyy}\n\n" +
                            "Please review the latest coordination status in Planscape:\n" +
                            "\u2022 Model health and warnings dashboard\n" +
                            "\u2022 Open issues and action items\n" +
                            "\u2022 Deliverables tracking\n\n" +
                            "[View Dashboard] \u2014 Use STING > BCC > Platform > Planscape to export HTML dashboard";
                        try { System.Windows.Clipboard.SetText(msg); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        TaskDialog.Show("STING — Teams Message",
                            "Teams message copied to clipboard.\nPaste into your Microsoft Teams or Slack channel.");
                        return;
                    }
                    case "PlanscapeWhatsApp":
                    {
                        string projectName = doc?.Title ?? "BIM Project";
                        string msg =
                            $"*{projectName} — BIM Update* \ud83d\udcca\n" +
                            $"{DateTime.Today:dd/MM/yyyy}\n\n" +
                            "Coordination status updated. Open issues and action items require attention.\n\n" +
                            "For full dashboard: Request HTML report from BIM Manager.";
                        try { System.Windows.Clipboard.SetText(msg); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        TaskDialog.Show("STING — WhatsApp",
                            "WhatsApp message copied to clipboard.\nPaste into WhatsApp chat.");
                        return;
                    }
                    case "PlanscapeHTML":
                    case "PlanscapeExportHTML":
                    {
                        // Route the HTML dashboard export through the existing
                        // dispatch map (registered in actionToCommandTag).
                        DispatchCoordAction("ExportDashboardHTML", commandData: null);
                        return;
                    }
                    case "PlanscapeDisconnect":
                    {
                        try
                        {
                            BIMManager.PlanscapeServerClient.Instance.Disconnect();
                            StingLog.Info("Planscape: disconnected from BCC");
                        }
                        catch (Exception ex) { StingLog.Warn($"PlanscapeDisconnect: {ex.Message}"); }
                        return;
                    }
                    case "PlanscapeOpenWebDashboard":
                    {
                        try
                        {
                            string url = BIMManager.PlanscapeServerClient.Instance.ServerUrl;
                            if (string.IsNullOrEmpty(url))
                            {
                                TaskDialog.Show("STING — Planscape", "Connect to the Planscape server first.");
                                return;
                            }
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                { FileName = url, UseShellExecute = true })?.Dispose();
                        }
                        catch (Exception ex) { StingLog.Warn($"PlanscapeOpenWebDashboard: {ex.Message}"); }
                        return;
                    }

                    // BCC's "Connect" button on the Planscape Native Collaboration Hub
                    // dispatches PlanscapeConnect (and its alias tags) here. Previously
                    // this fell through to DispatchCoordAction which bounced through a
                    // second ExternalEvent (StingDockPanel.DispatchCommand) — when the
                    // dock panel handler wasn't initialised, or the second event was
                    // dropped, the user saw "Action 'PlanscapeConnect' is not handled".
                    // Run the command inline so it executes in this ExternalEvent's
                    // own call context with no further indirection.
                    case "PlanscapeConnect":
                    case "PlanscapeAddMember":
                    case "PlanscapeRemoveMember":
                    case "PlanscapeLinkProject":
                    case "PlanscapeTestConnection":
                        RunBccPlanscapeCommand<BIMManager.PlanscapeConnectCommand>(action);
                        return;
                    case "PlanscapeSyncNow":
                    case "PlanscapeOpenBrowser":
                        RunBccPlanscapeCommand<BIMManager.PlatformSyncCommand>(action);
                        return;
                    case "PublishModelToPlanscape":
                        RunBccPlanscapeCommand<BIMManager.PublishModelCommand>(action);
                        return;
                    case "PlanscapeExportTeam":
                    case "PlanscapeExportConfig":
                        RunBccPlanscapeCommand<BIMManager.ExportCoordLogCommand>(action);
                        return;
                    case "PlanscapeShareReport":
                        RunBccPlanscapeCommand<BIMManager.GenerateDashboardCommand>(action);
                        return;
                    case "PlanscapeQR":
                    case "PlanscapeQRCode":
                        RunBccPlanscapeCommand<Tags.QRCodeCommand>(action);
                        return;
                    case "EscalateActions":
                        EscalateOverdueActions(doc);
                        return;
                    // Phase 101: BCC Refresh button (header) and F5 shortcut both
                    // dispatch "BCCReload". Rebuild CoordData on the Revit API
                    // thread (this method is called by BCCActionEventHandler
                    // which is on the API thread) then push the fresh data back
                    // to the WPF instance via ApplyReloadedData.
                    case "BCCReload":
                    {
                        try
                        {
                            var fresh = BuildCoordData(doc);
                            UI.BIMCoordinationCenter.CurrentInstance?.ApplyReloadedData(fresh);
                        }
                        catch (Exception ex) { StingLog.Error("BCCReload failed", ex); }
                        return;
                    }
                    case "BCCSnapshot":
                        BCCSnapshotInline(doc);
                        return;
                    case "BCCExportPDF":
                        BCCSnapshotInline(doc);
                        return;
                    case "BCCExportExcel":
                        BCCSnapshotInline(doc);
                        return;
                    case "BCCExportWord":
                        BCCSnapshotInline(doc);
                        return;
                    case "WarningsSelectElements":
                    {
                        var uiDoc = app?.ActiveUIDocument;
                        if (uiDoc == null) return;
                        var rawIds = UI.BIMCoordinationCenter.SelectedWarningIds;
                        if (rawIds != null && rawIds.Count > 0)
                        {
                            var ids = rawIds.Select(v => new ElementId(v)).ToList();
                            uiDoc.Selection.SetElementIds(ids);
                            try { uiDoc.ShowElements(ids); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                        }
                        else
                        {
                            // Fallback: run via command dispatch
                            DispatchCoordAction("WarningsSelectElements", null);
                        }
                        return;
                    }
                }

                // Dispatch through command resolution
                DispatchCoordAction(action, null);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ProcessAction({action}): {ex.Message}");
            }
        }

        /// <summary>
        /// Executes an IExternalCommand inline from BCC's ExternalEvent path. Mirrors
        /// StingCommandHandler.RunCommand: passes a null ExternalCommandData (commands
        /// fall back to StingCommandHandler.CurrentApp), catches and logs failures.
        /// Used by ProcessAction for Planscape* tags so they don't have to bounce
        /// through StingDockPanel.DispatchCommand and risk producing
        /// "Action 'X' is not handled" toasts when the second ExternalEvent is dropped.
        /// </summary>
        private static void RunBccPlanscapeCommand<T>(string action) where T : IExternalCommand, new()
        {
            try
            {
                var cmd = new T();
                string msg = "";
                var els = new ElementSet();
                cmd.Execute(null, ref msg, els);
                StingLog.Info($"ProcessAction: ran '{action}' inline → {typeof(T).Name}");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* user cancelled */ }
            catch (Exception ex)
            {
                StingLog.Error($"ProcessAction: '{action}' → {typeof(T).Name} failed", ex);
                TaskDialog.Show("STING", $"Command '{action}' failed:\n{ex.Message}");
            }
        }

        /// <summary>Renders the BCC window to a PNG snapshot and opens the output folder.</summary>
        private static void BCCSnapshotInline(Document doc)
        {
            var bcc = UI.BIMCoordinationCenter.CurrentInstance;
            if (bcc == null) { TaskDialog.Show("STING", "BIM Coordination Center is not open."); return; }
            bcc.Dispatcher.Invoke(() =>
            {
                try
                {
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)bcc.ActualWidth, (int)bcc.ActualHeight, 96, 96,
                        System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(bcc);
                    string dir = OutputLocationHelper.GetOutputDirectory(doc);
                    string path = Path.Combine(dir, $"bcc_snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using var fs = System.IO.File.Create(path);
                    encoder.Save(fs);
                    StingLog.Info($"BCCSnapshot saved: {path}");
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true })?.Dispose();
                }
                catch (Exception ex) { StingLog.Warn($"BCCSnapshot: {ex.Message}"); }
            });
        }

        /// <summary>Save permissions (roles + folder matrix) to project_config.json "permissions" key.</summary>
        private static void SavePermissionsInline(Document doc)
        {
            try
            {
                if (string.IsNullOrEmpty(doc?.PathName)) { TaskDialog.Show("STING", "Save the Revit project before saving permissions."); return; }
                string configPath = Path.Combine(Path.GetDirectoryName(doc.PathName), "project_config.json");
                Newtonsoft.Json.Linq.JObject cfg = File.Exists(configPath)
                    ? Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(configPath))
                    : new Newtonsoft.Json.Linq.JObject();

                var roles    = UI.BIMCoordinationCenter.GetLastPermissionsRoles();
                var folders  = UI.BIMCoordinationCenter.GetLastPermissionsFolders();

                cfg["permissions"] = new Newtonsoft.Json.Linq.JObject
                {
                    ["roles"]   = Newtonsoft.Json.Linq.JToken.FromObject(roles),
                    ["folders"] = Newtonsoft.Json.Linq.JToken.FromObject(folders),
                    ["saved_by"] = Environment.UserName,
                    ["saved_at"] = DateTime.Now.ToString("o")
                };
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                File.WriteAllText(configPath, cfg.ToString(Newtonsoft.Json.Formatting.Indented));
                TaskDialog.Show("STING", $"Permissions saved to:\n{configPath}");
                StingLog.Info($"SavePermissions: wrote {roles.Count} roles, {folders.Count} folders to project_config.json");
            }
            catch (Exception ex) { StingLog.Error("SavePermissionsInline failed", ex); TaskDialog.Show("STING", $"Save failed:\n{ex.Message}"); }
        }

        /// <summary>Edit user role inline — WPF dialog for role selection with CDE permission preview.</summary>
        private static void EditUserRoleInline(Document doc)
        {
            try
            {
                var roles = new List<string>
                {
                    "A — Architect (Design Lead)",
                    "M — Mechanical Engineer (MEP Lead)",
                    "E — Electrical Engineer",
                    "S — Structural Engineer",
                    "H — HVAC Engineer",
                    "P — Plumbing Engineer",
                    "C — BIM Coordinator",
                    "I — Information Manager",
                    "K — Client Representative",
                    "Q — QA/QC Manager",
                    "F — Facilities Manager",
                    "W — Contractor / Main Works",
                    "L — Landscape Architect",
                    "Z — Specialist / Sub-contractor"
                };

                // Load current role from project config
                string configPath = "";
                if (!string.IsNullOrEmpty(doc.PathName))
                    configPath = Path.Combine(Path.GetDirectoryName(doc.PathName), "project_config.json");
                string currentRole = "C";
                if (File.Exists(configPath))
                {
                    try
                    {
                        var cfg = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(configPath));
                        currentRole = cfg.Value<string>("USER_ROLE") ?? "C";
                    }
                    catch (Exception exCfg) { StingLog.Warn($"EditUserRole config load: {exCfg.Message}"); }
                }

                string currentLabel = roles.FirstOrDefault(r => r.StartsWith(currentRole + " ")) ?? roles[6];

                var win = new System.Windows.Window
                {
                    Title = "Edit User Role — ISO 19650 CDE Permissions",
                    Width = 500, Height = 520,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5))
                };

                var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = $"Current Role: {currentLabel}",
                    FontWeight = System.Windows.FontWeights.Bold, FontSize = 13,
                    Margin = new System.Windows.Thickness(0, 0, 0, 8)
                });
                stack.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "Select your active role. This determines CDE folder write access, approval rights, and notification routing.",
                    TextWrapping = System.Windows.TextWrapping.Wrap, FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    Margin = new System.Windows.Thickness(0, 0, 0, 12)
                });

                var listBox = new System.Windows.Controls.ListBox
                {
                    Height = 300, FontSize = 12,
                    Margin = new System.Windows.Thickness(0, 0, 0, 12)
                };
                foreach (var r in roles)
                {
                    var item = new System.Windows.Controls.ListBoxItem { Content = r, Padding = new System.Windows.Thickness(8, 4, 8, 4) };
                    if (r == currentLabel) item.IsSelected = true;
                    listBox.Items.Add(item);
                }
                stack.Children.Add(listBox);

                // Permission preview
                var previewText = new System.Windows.Controls.TextBlock
                {
                    Text = GetRolePermissionPreview(currentRole),
                    FontSize = 10, TextWrapping = System.Windows.TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.DarkSlateGray,
                    Margin = new System.Windows.Thickness(0, 0, 0, 8)
                };
                stack.Children.Add(previewText);

                listBox.SelectionChanged += (s, e) =>
                {
                    if (listBox.SelectedItem is System.Windows.Controls.ListBoxItem sel)
                    {
                        string code = sel.Content.ToString().Substring(0, 1);
                        previewText.Text = GetRolePermissionPreview(code);
                    }
                };

                var saveBtn = new System.Windows.Controls.Button
                {
                    Content = "Apply Role", Padding = new System.Windows.Thickness(20, 8, 20, 8),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x91, 0x2D)),
                    Foreground = System.Windows.Media.Brushes.White, FontWeight = System.Windows.FontWeights.Bold
                };
                saveBtn.Click += (s, e) =>
                {
                    if (listBox.SelectedItem is System.Windows.Controls.ListBoxItem sel)
                    {
                        string newRole = sel.Content.ToString().Substring(0, 1);
                        // Save to project_config.json
                        if (!string.IsNullOrEmpty(configPath))
                        {
                            try
                            {
                                var cfg = File.Exists(configPath)
                                    ? Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(configPath))
                                    : new Newtonsoft.Json.Linq.JObject();
                                cfg["USER_ROLE"] = newRole;
                                cfg["USER_ROLE_CHANGED"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                                File.WriteAllText(configPath, cfg.ToString(Newtonsoft.Json.Formatting.Indented));
                                StingLog.Info($"User role changed to: {newRole}");
                            }
                            catch (Exception ex) { StingLog.Warn($"EditUserRole save: {ex.Message}"); }
                        }
                        TaskDialog.Show("STING", $"Role updated to: {sel.Content}\n\nCDE permissions will reflect this role for all subsequent operations.");
                        win.DialogResult = true;
                        win.Close();
                    }
                };
                stack.Children.Add(saveBtn);
                win.Content = stack;
                win.ShowDialog();
            }
            catch (Exception ex) { StingLog.Warn($"EditUserRoleInline: {ex.Message}"); }
        }

        /// <summary>Get CDE permission preview text for a given role code.</summary>
        private static string GetRolePermissionPreview(string roleCode)
        {
            switch (roleCode)
            {
                case "A": return "CDE Access: WIP (Read/Write), SHARED (Read/Write), PUBLISHED (Read)\nCan Approve: Design documents, Drawing submissions\nNotifications: Design reviews, Client feedback, Coordination clashes";
                case "M": return "CDE Access: WIP (Read/Write), SHARED (Read/Write), PUBLISHED (Read)\nCan Approve: MEP coordination, System design\nNotifications: MEP clashes, System changes, Equipment updates";
                case "E": return "CDE Access: WIP (Read/Write), SHARED (Read/Write), PUBLISHED (Read)\nCan Approve: Electrical design, Panel schedules\nNotifications: Electrical clashes, Circuit changes";
                case "S": return "CDE Access: WIP (Read/Write), SHARED (Read/Write), PUBLISHED (Read)\nCan Approve: Structural design, Load calculations\nNotifications: Structural clashes, Foundation changes";
                case "C": return "CDE Access: WIP (Read/Write), SHARED (Read/Write), PUBLISHED (Read/Write), ARCHIVE (Read/Write)\nCan Approve: All document types, CDE state transitions\nNotifications: All activities, SLA violations, Compliance changes";
                case "I": return "CDE Access: All folders (Full control)\nCan Approve: All documents, CDE transitions, BEP changes\nNotifications: All activities, Security events, Audit trail";
                case "K": return "CDE Access: SHARED (Read), PUBLISHED (Read/Approve)\nCan Approve: Stage gate deliverables, Final publications\nNotifications: Transmittals, Stage gate reviews, Handover packages";
                case "F": return "CDE Access: PUBLISHED (Read), HANDOVER (Read/Write), COBIE (Read)\nCan Approve: O&M manuals, COBie data, Handover packages\nNotifications: Handover submissions, Asset data changes";
                default: return "CDE Access: WIP (Read/Write), SHARED (Read)\nNotifications: Discipline-specific activities";
            }
        }

        /// <summary>Take model snapshot — captures compliance, tag, and warning state for meeting record.</summary>
        private static void TakeModelSnapshot(Document doc)
        {
            try
            {
                ComplianceScan.InvalidateCache();
                var result = ComplianceScan.Scan(doc);
                var warningReport = WarningsEngine.ScanWarnings(doc);

                string docPath = doc.PathName;
                if (string.IsNullOrEmpty(docPath))
                {
                    TaskDialog.Show("STING", "Save the document first to create a snapshot.");
                    return;
                }

                string bimDir = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager");
                if (!Directory.Exists(bimDir)) Directory.CreateDirectory(bimDir);

                string snapshotPath = Path.Combine(bimDir, "snapshots.json");
                var snapshots = File.Exists(snapshotPath)
                    ? Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(snapshotPath))
                    : new Newtonsoft.Json.Linq.JArray();

                var snap = new Newtonsoft.Json.Linq.JObject
                {
                    ["id"] = $"SNAP-{snapshots.Count + 1:D4}",
                    ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["user"] = Environment.UserName,
                    ["tag_compliance_pct"] = result?.CompliancePercent ?? 0,
                    ["strict_compliance_pct"] = result?.StrictPercent ?? 0,
                    ["container_complete_pct"] = result?.ContainerCompletePct ?? 0,
                    ["rag_status"] = result?.RAGStatus ?? "RED",
                    ["total_elements"] = result?.TotalElements ?? 0,
                    ["tagged_elements"] = result?.TaggedComplete ?? 0,
                    ["untagged_elements"] = result?.Untagged ?? 0,
                    ["stale_elements"] = result?.StaleCount ?? 0,
                    ["warnings_total"] = warningReport?.Total ?? 0,
                    ["warnings_critical"] = warningReport?.BySeverity?.GetValueOrDefault(WarningSeverity.Critical, 0) ?? 0,
                    ["warning_health_score"] = WarningsEngine.CalculateWarningHealthScore(warningReport)
                };

                // Per-discipline breakdown
                if (result?.ByDisc != null)
                {
                    var discObj = new Newtonsoft.Json.Linq.JObject();
                    foreach (var kvp in result.ByDisc)
                        discObj[kvp.Key] = new Newtonsoft.Json.Linq.JObject
                        {
                            ["total"] = kvp.Value.Total,
                            ["tagged"] = kvp.Value.Tagged,
                            ["pct"] = kvp.Value.CompliancePct
                        };
                    snap["by_discipline"] = discObj;
                }

                snapshots.Add(snap);
                File.WriteAllText(snapshotPath, snapshots.ToString(Newtonsoft.Json.Formatting.Indented));

                TaskDialog.Show("STING Snapshot",
                    $"Model snapshot saved: {snap["id"]}\n\n" +
                    $"Tag Compliance: {snap["tag_compliance_pct"]:F1}% ({snap["rag_status"]})\n" +
                    $"Container Completeness: {snap["container_complete_pct"]:F1}%\n" +
                    $"Elements: {snap["tagged_elements"]}/{snap["total_elements"]} tagged, {snap["stale_elements"]} stale\n" +
                    $"Warnings: {snap["warnings_total"]} total ({snap["warnings_critical"]} critical)\n" +
                    $"Health Score: {snap["warning_health_score"]}/100\n\n" +
                    $"Timestamp: {snap["timestamp"]}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TakeModelSnapshot: {ex.Message}");
                TaskDialog.Show("STING", $"Snapshot failed: {ex.Message}");
            }
        }

        /// <summary>Escalate overdue meeting actions to ISO 19650 issues.</summary>
        private static void EscalateOverdueActions(Document doc)
        {
            try
            {
                string docPath = doc.PathName;
                if (string.IsNullOrEmpty(docPath)) { TaskDialog.Show("STING", "Save the document first."); return; }

                string meetPath = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager", "meetings.json");
                if (!File.Exists(meetPath)) { TaskDialog.Show("STING", "No meetings found."); return; }

                var meetings = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(meetPath));
                var overdueActions = new List<(string meetingId, Newtonsoft.Json.Linq.JToken action)>();

                foreach (var mtg in meetings)
                {
                    string meetId = mtg["id"]?.ToString() ?? "";
                    if (mtg["actions"] is Newtonsoft.Json.Linq.JArray actions)
                    {
                        foreach (var act in actions)
                        {
                            if (act["status"]?.ToString() == "OPEN")
                            {
                                string dueStr = act["due_date"]?.ToString() ?? "";
                                if (DateTime.TryParse(dueStr, out var dueDate) && dueDate < DateTime.Now)
                                    overdueActions.Add((meetId, act));
                            }
                        }
                    }
                }

                if (overdueActions.Count == 0)
                {
                    TaskDialog.Show("STING", "No overdue action items to escalate.");
                    return;
                }

                var td = new TaskDialog("STING — Escalate Overdue Actions");
                td.MainContent = $"Found {overdueActions.Count} overdue action item(s).\n\n" +
                    string.Join("\n", overdueActions.Take(10).Select(a =>
                        $"  • {a.action["description"]} — {a.action["assigned_to"]} (due: {a.action["due_date"]})")) +
                    (overdueActions.Count > 10 ? $"\n  ... and {overdueActions.Count - 10} more" : "") +
                    "\n\nEscalate to NCR issues?";
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Escalate All", "Create NCR issues for all overdue actions");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel");
                var result = td.Show();
                if (result != TaskDialogResult.CommandLink1) return;

                // Create issues
                string issuesPath = Path.Combine(Path.GetDirectoryName(docPath), "_bim_manager", "issues.json");
                var issues = File.Exists(issuesPath)
                    ? Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(issuesPath))
                    : new Newtonsoft.Json.Linq.JArray();

                int created = 0;
                foreach (var (meetId, action) in overdueActions)
                {
                    int nextId = issues.Count + 1;
                    var issue = new Newtonsoft.Json.Linq.JObject
                    {
                        ["id"] = $"NCR-{nextId:D4}",
                        ["title"] = $"Overdue Action: {action["description"]}",
                        ["type"] = "NCR",
                        ["priority"] = "HIGH",
                        ["status"] = "OPEN",
                        ["assignee"] = action["assigned_to"]?.ToString() ?? "",
                        ["description"] = $"Escalated from meeting {meetId}. Original due date: {action["due_date"]}. " +
                            $"Action: {action["description"]}",
                        ["created_date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                        ["created_by"] = Environment.UserName,
                        ["source"] = $"Meeting action escalation from {meetId}",
                        ["element_ids"] = new Newtonsoft.Json.Linq.JArray()
                    };
                    issues.Add(issue);

                    // Mark original action as escalated
                    action["status"] = "ESCALATED";
                    action["escalated_to"] = issue["id"]?.ToString();
                    action["escalated_date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    created++;
                }

                File.WriteAllText(issuesPath, issues.ToString(Newtonsoft.Json.Formatting.Indented));
                File.WriteAllText(meetPath, meetings.ToString(Newtonsoft.Json.Formatting.Indented));

                TaskDialog.Show("STING Escalation",
                    $"Created {created} NCR issue(s) from overdue actions.\n\n" +
                    "Original actions marked as ESCALATED with issue cross-reference.");
                StingLog.Info($"EscalateOverdueActions: created {created} NCR issues from overdue meeting actions");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"EscalateOverdueActions: {ex.Message}");
                TaskDialog.Show("STING", $"Escalation failed: {ex.Message}");
            }
        }

        /// <summary>Zoom to element(s) by creating a 3D section box view around them.</summary>
        private static void ZoomToElementIn3D(Document doc, UIApplication app, string elementIdsCsv)
        {
            try
            {
                var ids = elementIdsCsv.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => long.TryParse(s, out _))
                    .Select(s => new ElementId(long.Parse(s)))
                    .ToList();

                if (ids.Count == 0) return;

                // Compute aggregate bounding box
                BoundingBoxXYZ aggBB = null;
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    var bb = el.get_BoundingBox(null);
                    if (bb == null) continue;
                    if (aggBB == null)
                    {
                        aggBB = new BoundingBoxXYZ
                        {
                            Min = new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                            Max = new XYZ(bb.Max.X, bb.Max.Y, bb.Max.Z)
                        };
                    }
                    else
                    {
                        aggBB.Min = new XYZ(
                            Math.Min(aggBB.Min.X, bb.Min.X),
                            Math.Min(aggBB.Min.Y, bb.Min.Y),
                            Math.Min(aggBB.Min.Z, bb.Min.Z));
                        aggBB.Max = new XYZ(
                            Math.Max(aggBB.Max.X, bb.Max.X),
                            Math.Max(aggBB.Max.Y, bb.Max.Y),
                            Math.Max(aggBB.Max.Z, bb.Max.Z));
                    }
                }

                if (aggBB == null) { TaskDialog.Show("STING", "Could not compute bounding box for selected elements."); return; }

                // Add 3 ft padding around the box
                double pad = 3.0;
                aggBB.Min = new XYZ(aggBB.Min.X - pad, aggBB.Min.Y - pad, aggBB.Min.Z - pad);
                aggBB.Max = new XYZ(aggBB.Max.X + pad, aggBB.Max.Y + pad, aggBB.Max.Z + pad);

                // Find or create a 3D view
                var view3d = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name.Contains("STING"));

                using (var tx = new Transaction(doc, "STING Zoom to Element"))
                {
                    tx.Start();
                    if (view3d == null)
                    {
                        var vft = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                        if (vft != null)
                        {
                            view3d = View3D.CreateIsometric(doc, vft.Id);
                            view3d.Name = "STING - Section Box Zoom";
                        }
                    }
                    if (view3d != null)
                    {
                        view3d.IsSectionBoxActive = true;
                        view3d.SetSectionBox(aggBB);
                    }
                    tx.Commit();
                }

                // Activate the 3D view and select elements.
                // Phase 104: clear previous selection first so a re-click on the same
                // or a different warning always produces a clean, visible selection
                // change in the status bar. Revit's SetElementIds coalesces identical
                // inputs silently; the Clear + Set sequence guarantees a UI event.
                if (view3d != null)
                {
                    var uidoc = app?.ActiveUIDocument ?? new UIDocument(doc);
                    uidoc.ActiveView = view3d;
                    try { uidoc.Selection.SetElementIds(new List<ElementId>()); } catch (Exception) { /* ignore */ }
                    uidoc.Selection.SetElementIds(ids);
                    try { uidoc.RefreshActiveView(); } catch (Exception) { /* older API */ }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ZoomToElementIn3D: {ex.Message}");
            }
        }

        /// <summary>Extract warning description from a key formatted as "Category|Description" or legacy "Category_Description".</summary>
        private static string ExtractWarningDescription(string warningKey)
        {
            // Pipe delimiter (new format) — split at first pipe
            int pipeIdx = warningKey.IndexOf('|');
            if (pipeIdx >= 0)
                return warningKey.Substring(pipeIdx + 1);
            // Legacy underscore format — split at first underscore (category prefix)
            int usIdx = warningKey.IndexOf('_');
            if (usIdx >= 0)
                return warningKey.Substring(usIdx + 1);
            return warningKey;
        }

        /// <summary>Find warning elements by description text and zoom to 3D section box.</summary>
        private static void ZoomToWarningIn3D(Document doc, UIApplication app, string warningKey)
        {
            try
            {
                string descPart = ExtractWarningDescription(warningKey);
                var ids = new List<ElementId>();

                // Phase 104 fix: warning selection was using loose substring match in
                // both directions, which caused cross-contamination between similar
                // warning descriptions — e.g., clicking "Walls are joined" would also
                // match "Walls cannot be joined" and select both sets of elements. The
                // user reported "selection doesn't switch between warnings in the
                // same category" — root cause was this overmatching.
                //
                // New priority: (1) EXACT description match, (2) source starts-with,
                // (3) target starts-with, (4) two-way substring (last resort). This
                // guarantees different warnings produce different selections even
                // when their descriptions share a common prefix.
                //
                // Phase 103 context retained: use the live CoordData.Warnings list
                // populated by BuildCoordData from WarningsEngine.ScanWarnings first
                // — it carries real FailingElement IDs resolved against the current
                // document, so match-by-description succeeds even when
                // doc.GetWarnings() text punctuation differs slightly.
                var bccInstance = UI.BIMCoordinationCenter.CurrentInstance;
                if (bccInstance != null)
                {
                    var rows = UI.BIMCoordinationCenter.GetLastCoordWarnings();
                    if (rows != null && rows.Count > 0)
                    {
                        ids = MatchWarningRows(rows, descPart);
                    }
                }

                // Fallback: doc.GetWarnings() description match (the old path).
                // Kept so ribbon callers that don't go through BCC still work.
                if (ids.Count == 0)
                {
                    var warnings = doc.GetWarnings();
                    ids = MatchWarningDocRows(warnings, descPart);
                }

                // Dedupe (same element can appear in FailingElements + AdditionalElements)
                ids = ids.GroupBy(id => id.Value).Select(g => g.First()).ToList();

                if (ids.Count > 0)
                    ZoomToElementIn3D(doc, app, string.Join(",", ids.Select(id => id.Value)));
                else
                    TaskDialog.Show("STING",
                        $"No elements found for the warning:\n\n  \u201C{descPart}\u201D\n\n" +
                        "This warning may have been auto-resolved, or the affected elements " +
                        "may have been deleted. Click Refresh on the BCC header to rebuild " +
                        "the warning list from the current model state.");
            }
            catch (Exception ex) { StingLog.Warn($"ZoomToWarningIn3D: {ex.Message}"); }
        }

        /// <summary>
        /// Phase 104: Priority-matched warning lookup in BCC CoordData.Warnings rows.
        /// Order: exact match > starts-with either direction > substring. Stops at the
        /// highest tier that produces any match so selections DON'T bleed between
        /// descriptions that share prefixes (e.g., "Walls are joined" vs "Walls cannot be joined").
        /// </summary>
        private static List<ElementId> MatchWarningRows(
            IReadOnlyList<UI.BIMCoordinationCenter.WarningRow> rows, string descPart)
        {
            var exact = new List<ElementId>();
            var startsWith = new List<ElementId>();
            var substring = new List<ElementId>();
            foreach (var row in rows)
            {
                if (row?.Description == null) continue;
                string rd = row.Description;
                if (string.Equals(rd, descPart, StringComparison.OrdinalIgnoreCase))
                {
                    if (row.ElementIds != null) foreach (long v in row.ElementIds) exact.Add(new ElementId(v));
                }
                else if (rd.StartsWith(descPart, StringComparison.OrdinalIgnoreCase)
                      || descPart.StartsWith(rd, StringComparison.OrdinalIgnoreCase))
                {
                    if (row.ElementIds != null) foreach (long v in row.ElementIds) startsWith.Add(new ElementId(v));
                }
                else if (rd.IndexOf(descPart, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         descPart.IndexOf(rd, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (row.ElementIds != null) foreach (long v in row.ElementIds) substring.Add(new ElementId(v));
                }
            }
            if (exact.Count > 0) return exact;
            if (startsWith.Count > 0) return startsWith;
            return substring;
        }

        /// <summary>Priority-matched lookup against doc.GetWarnings(). Same tiered match as MatchWarningRows.</summary>
        private static List<ElementId> MatchWarningDocRows(
            IList<FailureMessage> warnings, string descPart)
        {
            var exact = new List<ElementId>();
            var startsWith = new List<ElementId>();
            var substring = new List<ElementId>();
            foreach (var w in warnings)
            {
                string desc = w.GetDescriptionText() ?? "";
                if (string.Equals(desc, descPart, StringComparison.OrdinalIgnoreCase))
                {
                    exact.AddRange(w.GetFailingElements()); exact.AddRange(w.GetAdditionalElements());
                }
                else if (desc.StartsWith(descPart, StringComparison.OrdinalIgnoreCase)
                      || descPart.StartsWith(desc, StringComparison.OrdinalIgnoreCase))
                {
                    startsWith.AddRange(w.GetFailingElements()); startsWith.AddRange(w.GetAdditionalElements());
                }
                else if (desc.IndexOf(descPart, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         descPart.IndexOf(desc, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    substring.AddRange(w.GetFailingElements()); substring.AddRange(w.GetAdditionalElements());
                }
            }
            if (exact.Count > 0) return exact;
            if (startsWith.Count > 0) return startsWith;
            return substring;
        }

        /// <summary>Select elements associated with a warning description and zoom to show them.</summary>
        private static void SelectWarningElements(Document doc, UIApplication app, string warningKey)
        {
            try
            {
                string descPart = ExtractWarningDescription(warningKey);
                // Phase 104: use tiered matching so same-category warnings produce
                // distinct selections instead of sharing via substring bleed.
                var ids = MatchWarningDocRows(doc.GetWarnings(), descPart);
                if (ids.Count > 0)
                {
                    var uidoc = app?.ActiveUIDocument;
                    if (uidoc != null)
                    {
                        uidoc.Selection.SetElementIds(ids);
                        try { uidoc.ShowElements(ids); } catch (Exception) { /* view may not support ShowElements */ }
                    }
                    TaskDialog.Show("STING", $"Selected {ids.Count} element(s) from matching warnings.");
                }
                else
                    TaskDialog.Show("STING", "No elements found for this warning.");
            }
            catch (Exception ex) { StingLog.Warn($"SelectWarningElements: {ex.Message}"); }
        }

        // Phase 78 Section 9.4: Moved to static readonly — was being rebuilt on every call.
        private static readonly Dictionary<string, string> _actionToCommandTag =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Overview quick actions
                { "RunDailyQA", "DailyQA" },
                { "RunMorningCheck", "MorningHealthCheck" },
                { "RetagStale", "RetagStale" },
                { "TagNewOnly", "TagNewOnly" },
                { "ExportCOBie", "COBieExport" },
                { "FullComplianceDashboard", "FullComplianceDashboard" },

                // Model health actions
                { "RefreshHealth", "ModelHealthDashboard" },
                { "ExportHealth", "ExportModelHealth" },
                { "RunFullCheck", "ValidateTemplate" },

                // Warnings actions
                { "SelectWarningElements", "WarningsSelectElements" },
                { "SuppressWarnings", "WarningsSuppress" },
                { "WarningsCompliance", "WarningsCompliance" },

                // Issues actions
                { "RaiseIssue", "RaiseIssue" },
                { "UpdateIssue", "UpdateIssue" },
                { "IssuesBulkClose", "UpdateIssue" },
                { "SelectIssueElements", "SelectIssueElements" },
                { "BCFExport", "BCFExport" },
                { "BCFImport", "BCFImport" },
                { "ExportIssues", "IssueDashboard" },
                { "IssueTimeline", "IssueDashboard" },

                // Revisions actions
                { "CreateRevision", "CreateRevision" },
                { "AutoRevisionCloud", "AutoRevisionCloud" },
                { "TakeSnapshot", "TrackElementRevisions" },
                { "RevisionCompare", "RevisionCompare" },
                { "TrackElementRevisions", "TrackElementRevisions" },
                { "IssueSheetsForRevision", "IssueSheetsForRevision" },
                { "RevisionNamingEnforce", "RevisionNamingEnforce" },
                { "BulkRevisionStamp", "BulkRevisionStamp" },
                { "ExportRevisions", "RevisionExport" },

                // Platform actions
                { "PlatformSync", "PlatformSync" },
                { "CDEPackage", "CDEPackage" },
                { "CDEStatus", "CDEStatus" },
                { "ValidateDocNaming", "ValidateDocNaming" },
                { "CreateTransmittal", "CreateTransmittal" },
                { "ExportToExcel", "ExportToExcel" },
                { "ImportFromExcel", "ImportFromExcel" },
                { "ExcelRoundTrip", "ExcelRoundTrip" },
                { "COBieExport", "COBieExport" },
                { "IFCExport", "IFCExport" },
                { "ACCPublish", "ACCPublish" },
                { "SharePointExport", "SharePointExport" },

                // Phase 167 — Planscape BCC dispatch entries. Disconnect /
                // OpenWebDashboard short-circuit inline in ProcessAction; the
                // remaining tags need an explicit dictionary entry so the
                // resolver lookup hits a real command and never falls through
                // to the "Action 'X' is not handled" toast.
                { "PlanscapeConnect",          "PlanscapeConnect" },
                { "PlanscapeDisconnect",       "PlanscapeDisconnect" },
                { "PlanscapeOpenWebDashboard", "PlanscapeOpenWebDashboard" },
                { "PlanscapeSyncNow",          "PlanscapeSyncNow" },
                { "PlanscapeAddMember",        "PlanscapeConnect" },
                { "PlanscapeRemoveMember",     "PlanscapeConnect" },
                { "PlanscapeLinkProject",      "PlanscapeConnect" },
                { "PlanscapeTestConnection",   "PlanscapeConnect" },
                { "PlanscapeUnlinkProject",    "PlanscapeDisconnect" },
                { "PlanscapeClearCredentials", "PlanscapeDisconnect" },
                { "PlanscapeOpenBrowser",      "PlanscapeOpenWebDashboard" },
                { "PublishModelToPlanscape",   "PublishModelToPlanscape" },
                { "PlanscapeCreateProject",    "PlanscapeCreateProject" },

                // Workflow actions
                { "RunWorkflowPreset", "WorkflowPreset" },
                { "CreateWorkflowPreset", "CreateWorkflowPreset" },
                { "WorkflowTrend", "WorkflowTrend" },
                { "ListWorkflowPresets", "ListWorkflowPresets" },

                // QA Dashboard actions
                { "ValidateTags", "ValidateTags" },
                { "PreTagAudit", "PreTagAudit" },
                { "AnomalyAutoFix", "AnomalyAutoFix" },
                { "ResolveAllIssues", "ResolveAllIssues" },
                { "TagRegisterExport", "TagRegisterExport" },
                { "CompletenessDashboard", "CompletenessDashboard" },

                // 4D/5D actions
                { "AutoSchedule4D", "AutoSchedule4D" },
                { "AutoCost5D", "AutoCost5D" },
                { "ViewTimeline4D", "ViewTimeline4D" },
                { "CostReport5D", "CostReport5D" },
                { "CashFlow5D", "CashFlow5D" },
                { "ExportSchedule4D", "ExportSchedule4D" },
                { "ImportMSProject", "ImportMSProject" },
                { "MilestoneRegister", "MilestoneRegister" },
                { "PhaseSummary", "PhaseSummary" },

                // Permission actions (SavePermissions handled inline in ProcessAction)
                { "CreateFolders", "CreateFolders" },
                // Phase 96: Fix BCC-Perm-01 — was routed to ExportModelHealth (wrong command).
                // ExportPermissionMatrixCommand now resolvable via WorkflowEngine.ResolveCommand
                // so it produces the real role/folder CSV matrix expected by BEP auditors.
                { "ExportPermissionMatrix", "ExportPermissionMatrix" },
                { "EditUserRole", "ConfigEditor" },

                // 4D/5D extended scheduling commands (dispatched from BCC 4D/5D tab)
                { "WorkingCalendar", "WorkingCalendar" },
                { "NavisworksTimeLiner", "NavisworksTimeLiner" },
                { "ElementCostTrace", "ElementCostTrace" },

                // Deliverables actions
                { "AddDocument", "AddDocument" },
                { "DocumentRegister", "DocumentRegister" },
                { "DocumentBriefcase", "DocumentBriefcase" },
                { "StageComplianceGate", "StageComplianceGate" },

                // Meeting Manager actions — route through Document Manager's MEETINGS tab
                { "NewMeeting", "DocumentManager" },
                { "AutoAgenda", "DocumentManager" },
                { "MeetingTemplates", "DocumentManager" },
                { "LogMinutes", "DocumentManager" },
                { "AddActionItem", "DocumentManager" },
                { "MeetingHistory", "DocumentManager" },
                { "OpenActions", "DocumentManager" },
                { "ExportMinutes", "DocumentManager" },
                { "SendReminder", "DocumentManager" },

                // Automation rule actions
                { "EscalateActions", "RaiseIssue" },

                // Coord Log actions
                { "ExportCoordLog", "ExportModelHealth" },
                { "ClearCoordLog", "ConfigEditor" },

                // Team actions
                { "IssueBatchUpdate", "UpdateIssue" },
                { "AssignIssues", "UpdateIssue" },
                { "TeamReport", "ExportModelHealth" },

                // Sheet naming
                { "SheetNamingCheck", "SheetNamingCheck" },

                // Handover
                { "HandoverManual", "HandoverManual" },
                { "ExportSheetRegister", "ExportSheetRegister" },
                { "StreamingCOBieExport", "StreamingCOBieExport" },
                { "BOQExport", "BOQExport" },
                { "ExportTemplate", "ExportExcelTemplate" },

                // QA extended
                { "SchemaValidate", "SchemaValidate" },
                { "LoadSharedParams", "LoadSharedParams" },
                { "EvaluateFormulas", "EvaluateFormulas" },
                { "CombineParameters", "CombineParameters" },

                // Report action
                { "ExportReport", "ExportModelHealth" },
                { "DiscComplianceReport", "DiscComplianceReport" },

                // Phase 104: GAP-analysis BCC actions — routed through WorkflowEngine.ResolveCommand
                // (identity mappings so unrecognised-action path doesn't fire for these tags).
                { "ExportDashboardHTML", "ExportDashboardHTML" },
                { "AutoMeetingMinutes", "AutoMeetingMinutes" },
                { "BEPStageValidation", "BEPStageValidation" },
                { "IssueRevisionLink", "IssueRevisionLink" },
                { "TagRevisionDiff", "TagRevisionDiff" },
                { "AutoScheduleMeetings", "AutoScheduleMeetings" },
                { "COBieExtendedImport", "COBieExtendedImport" },
                { "LinkIssueElements", "LinkIssueElements" },

                // Phase 104: fall-through mappings for BCC buttons that previously produced
                // "Action 'X' is not handled" popups. Route aliases to the nearest existing
                // command so the button has a functional effect (even if it's generic).
                { "ExportMeetingMinutes", "DocumentManager" },
                { "ExportMinutesWord", "DocumentManager" },
                { "ExportMinutesPDF", "DocumentManager" },
                { "ExportMeetingsPDF", "DocumentManager" },
                { "ExportMeetingAnalytics", "DocumentManager" },
                { "SendMeetingInvites", "DocumentManager" },
                { "BulkCloseActions", "UpdateIssue" },
                { "ExportMilestones", "ExportSchedule4D" },
                { "ExportCashFlow", "CashFlow5D" },
                { "ExportTimeline4DPNG", "ViewTimeline4D" },
                { "ExportDeliverablesRegister", "DocumentRegister" },
                { "BulkDeliverableStatus", "DocumentRegister" },
                { "RevisionExportXlsx", "RevisionExport" },
                { "ApprovalWorkflow", "CDEStatus" },
                { "FixContainers", "CombineParameters" },
                { "ViewDocument", "DocumentManager" },
            };

        /// <summary>
        /// Dispatches a BIM Coordination Center action tag to the matching IExternalCommand.
        /// Maps action tags from dialog buttons to command classes and executes them directly
        /// (we are already on the Revit API thread inside StingCommandHandler.Execute).
        /// </summary>
        private static void DispatchCoordAction(string action, ExternalCommandData commandData)
        {
            // Phase 99: handle pipe-delimited parametric actions before anything else.
            // BCC forms send "CreateRevision|P04|A|Coordination update" so the
            // inline Revisions form can pass user-selected ISO code, discipline,
            // and description straight through to CreateRevisionCommand without
            // reopening a TaskDialog picker. CreateRevisionCommand reads the full
            // string from CoordinationCenterCommands.BccPendingAction and parses
            // its own params, so we set that property and re-dispatch the bare
            // command tag.
            if (!string.IsNullOrEmpty(action) && action.Contains("|"))
            {
                string head = action.Substring(0, action.IndexOf('|'));
                BIMManager.CoordinationCenterCommands.BccPendingAction = action;
                action = head;
            }

            var actionToCommandTag = _actionToCommandTag;

            // Handle RepeatLastWorkflow by resolving the last workflow name
            if (string.Equals(action, "RepeatLastWorkflow", StringComparison.OrdinalIgnoreCase))
            {
                string last = WorkflowEngine.LastWorkflowName;
                if (!string.IsNullOrEmpty(last))
                {
                    UI.StingCommandHandler.SetExtraParam("WorkflowPresetName", last);
                    var wfCmd = WorkflowEngine.GetCommandInstance("WorkflowPreset");
                    if (wfCmd != null)
                    {
                        string msg = "";
                        var els = new ElementSet();
                        wfCmd.Execute(commandData, ref msg, els);
                        StingLog.Info($"DispatchCoordAction: repeated workflow '{last}'");
                    }
                }
                else
                {
                    TaskDialog.Show("STING", "No previous workflow to repeat.");
                }
                return;
            }

            // BCC Platform tab → Planscape inline actions that aren't IExternalCommand classes.
            // StingCommandHandler.Execute handles these inline; mirror that here so BCC's
            // ExternalEvent path doesn't fall through to "Action 'X' is not handled".
            if (string.Equals(action, "PlanscapeDisconnect", StringComparison.OrdinalIgnoreCase)
             || string.Equals(action, "PlanscapeUnlinkProject", StringComparison.OrdinalIgnoreCase)
             || string.Equals(action, "PlanscapeClearCredentials", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    BIMManager.PlanscapeServerClient.Instance.Disconnect();
                    if (string.Equals(action, "PlanscapeDisconnect", StringComparison.OrdinalIgnoreCase))
                        TaskDialog.Show("Planscape", "Disconnected from Planscape server.");
                }
                catch (Exception ex) { StingLog.Warn($"{action} dispatch: {ex.Message}"); }
                return;
            }
            if (string.Equals(action, "PlanscapeSyncNow", StringComparison.OrdinalIgnoreCase)
             || string.Equals(action, "PlanscapeOpenBrowser", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uiApp = UI.StingCommandHandler.CurrentApp;
                    if (uiApp != null) BIMManager.PlatformSyncCommand.SyncToPlanscapeServer(uiApp);
                }
                catch (Exception ex) { StingLog.Warn($"{action} dispatch: {ex.Message}"); }
                return;
            }

            // Handle DocumentManager inline (opens WPF dialog directly)
            if (string.Equals(action, "DocumentManager", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uiApp = UI.StingCommandHandler.CurrentApp;
                    var doc2 = uiApp?.ActiveUIDocument?.Document;
                    if (doc2 != null)
                        UI.DocumentManagementDialog.Show(doc2);
                }
                catch (Exception ex) { StingLog.Warn($"DocumentManager dispatch: {ex.Message}"); }
                return;
            }

            // BCC's Planscape Native Collaboration Hub fires Planscape* actions
            // (PlanscapeConnect / PlanscapeDisconnect / PlanscapeSyncNow / etc).
            // StingCommandHandler already wires every Planscape tag with its own
            // case block — including the ones that aren't IExternalCommands like
            // Disconnect (which just calls PlanscapeServerClient.Instance.Disconnect()).
            // Forward the whole Planscape namespace to StingCommandHandler so the
            // BCC dispatch path doesn't have to duplicate every binding here.
            if (action.StartsWith("Planscape", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    bool ok = UI.StingDockPanel.DispatchCommand(action);
                    if (ok)
                        StingLog.Info($"DispatchCoordAction: forwarded '{action}' to StingCommandHandler");
                    else
                        StingLog.Warn($"DispatchCoordAction: forward '{action}' failed — StingDockPanel handler not initialised");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"DispatchCoordAction: forward '{action}' failed — {ex.Message}");
                    TaskDialog.Show("STING", $"Command '{action}' failed:\n{ex.Message}");
                }
                return;
            }

            // Phase 104: prefixed BCC action tags that weren't being stripped produced
            // "is not handled" popups. Route the common prefixes to the nearest real
            // command so the click is never dead.
            if (action.StartsWith("ViewDocument_", StringComparison.OrdinalIgnoreCase)
             || action.StartsWith("ViewDocument", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var uiApp = UI.StingCommandHandler.CurrentApp;
                    var doc2 = uiApp?.ActiveUIDocument?.Document;
                    if (doc2 != null) UI.DocumentManagementDialog.Show(doc2);
                }
                catch (Exception ex) { StingLog.Warn($"ViewDocument dispatch: {ex.Message}"); }
                return;
            }
            if (action.StartsWith("Disconnect_", StringComparison.OrdinalIgnoreCase)
             || action.StartsWith("ViewLogs_", StringComparison.OrdinalIgnoreCase)
             || action.StartsWith("SelectRevision_", StringComparison.OrdinalIgnoreCase)
             || action.StartsWith("HighlightRevClouds_", StringComparison.OrdinalIgnoreCase)
             || action.StartsWith("IsolateRevision3D_", StringComparison.OrdinalIgnoreCase)
             || action.StartsWith("SupersedeRevision_", StringComparison.OrdinalIgnoreCase)
             || action.StartsWith("ZoomToRevision_", StringComparison.OrdinalIgnoreCase))
            {
                // These carry a payload we don't implement yet — log and no-op instead
                // of scaring the user with a TaskDialog.
                StingLog.Info($"DispatchCoordAction: prefixed action '{action}' — placeholder no-op.");
                return;
            }

            // Check for workflow preset actions: RunDailyQA, RunMorningCheck, RunWorkflow_*
            var workflowPresets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "RunDailyQA", "RunMorningCheck" };

            if (action.StartsWith("RunWorkflow_", StringComparison.OrdinalIgnoreCase)
                || workflowPresets.Contains(action))
            {
                string presetName;
                if (action.StartsWith("RunWorkflow_", StringComparison.OrdinalIgnoreCase))
                    presetName = action.Substring("RunWorkflow_".Length);
                else if (actionToCommandTag.TryGetValue(action, out var mapped2))
                    presetName = mapped2;
                else
                    presetName = action;

                UI.StingCommandHandler.SetExtraParam("WorkflowPresetName", presetName);
                var wfCmd = WorkflowEngine.GetCommandInstance("WorkflowPreset");
                if (wfCmd != null)
                {
                    string msg = "";
                    var els = new ElementSet();
                    wfCmd.Execute(commandData, ref msg, els);
                    StingLog.Info($"DispatchCoordAction: ran workflow preset '{presetName}'");
                }
                else
                {
                    StingLog.Warn($"DispatchCoordAction: could not resolve WorkflowPreset command");
                }
                return;
            }

            // Check for element selection patterns: "SelectByDisc_M", "SelectIssue_ISS-001", etc.
            if (action.StartsWith("SelectByDisc_", StringComparison.OrdinalIgnoreCase))
            {
                string disc = action.Substring("SelectByDisc_".Length);
                UI.StingCommandHandler.SetExtraParam("DiscFilter", disc);
                var selCmd = new Organise.SelectByDisciplineCommand();
                string msg = "";
                var els = new ElementSet();
                selCmd.Execute(commandData, ref msg, els);
                return;
            }

            // Resolve via the action-to-tag map
            string commandTag = actionToCommandTag.TryGetValue(action, out var mapped) ? mapped : action;
            var cmd = WorkflowEngine.GetCommandInstance(commandTag);
            if (cmd != null)
            {
                try
                {
                    string msg = "";
                    var els = new ElementSet();
                    cmd.Execute(commandData, ref msg, els);
                    StingLog.Info($"DispatchCoordAction: executed '{action}' → {cmd.GetType().Name}");
                }
                catch (Exception ex)
                {
                    StingLog.Error($"DispatchCoordAction: '{action}' failed", ex);
                    TaskDialog.Show("STING", $"Command '{action}' failed:\n{ex.Message}");
                }
            }
            else
            {
                StingLog.Warn($"DispatchCoordAction: unrecognised action '{action}'");
                TaskDialog.Show("STING", $"Action '{action}' is not handled. Check StingCommandHandler for the command binding.");
            }
        }
    }
}
