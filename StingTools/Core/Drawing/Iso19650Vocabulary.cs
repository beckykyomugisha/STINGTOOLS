using System;
// StingTools — ISO 19650-2 / BS 1192 controlled vocabularies
//
// Single source of truth for every dropdown that should be a
// closed list rather than free text. The values here come from
// BS EN ISO 19650-2:2018 and the UK BIM Framework guidance —
// project / originator / volume / level / type / role codes
// plus suitability / authorisation states and RIBA stages.
//
// The DrawingTypeEditorDialog (and any future editor) reads these
// arrays to populate ComboBox.ItemsSource so the user cannot
// typo their way into an invalid project deliverable.

using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    public static class Iso19650Vocabulary
    {
        // ── Discipline / Role codes (BS EN ISO 19650-2 §A.5 + UK BIM Framework) ──
        // Single-letter codes used in the file-name "role" segment.
        public static readonly string[] DisciplineCodes =
        {
            "*",  // wildcard for routing rules
            "A",  // Architect
            "B",  // Building Surveyor
            "C",  // Civil Engineer
            "D",  // Drainage / Highways Engineer
            "E",  // Electrical Engineer
            "F",  // Facilities Manager
            "G",  // GIS / Land Surveyor
            "H",  // Heating & Ventilation Engineer
            "I",  // Interior Designer
            "K",  // Client
            "L",  // Landscape Architect
            "M",  // Mechanical Engineer
            "P",  // Public Health (Plumbing) Engineer
            "Q",  // Quantity Surveyor
            "S",  // Structural Engineer
            "T",  // Town & Country Planner
            "W",  // Contractor (Worker)
            "X",  // Subcontractor / Specialist
            "Y",  // Specialist Designer
            "Z",  // General / multi-disciplinary
        };

        public static readonly Dictionary<string, string> DisciplineLabels = new Dictionary<string, string>
        {
            { "*", "Any / wildcard" },
            { "A", "A — Architect" },
            { "B", "B — Building Surveyor" },
            { "C", "C — Civil Engineer" },
            { "D", "D — Drainage / Highways" },
            { "E", "E — Electrical Engineer" },
            { "F", "F — Facilities Manager" },
            { "G", "G — GIS / Land Surveyor" },
            { "H", "H — Heating & Ventilation" },
            { "I", "I — Interior Designer" },
            { "K", "K — Client" },
            { "L", "L — Landscape Architect" },
            { "M", "M — Mechanical Engineer" },
            { "P", "P — Public Health / Plumbing" },
            { "Q", "Q — Quantity Surveyor" },
            { "S", "S — Structural Engineer" },
            { "T", "T — Town & Country Planner" },
            { "W", "W — Contractor" },
            { "X", "X — Subcontractor / Specialist" },
            { "Y", "Y — Specialist Designer" },
            { "Z", "Z — General / multi-disciplinary" },
        };

        // ── Information-Container Type codes (ISO 19650 §A.6 — "Type" field) ──
        public static readonly string[] DocTypes =
        {
            "AF", // Animation File
            "AR", // Asset Register
            "BQ", // Bill of Quantities
            "BR", // Brief
            "CA", // Calculations
            "CO", // Correspondence
            "CM", // Construction Method (Method Statement)
            "CP", // Cost Plan
            "CR", // Clash Report
            "DB", // Database
            "DR", // Drawing
            "FN", // File Note
            "HS", // Health & Safety File
            "IE", // Image
            "M2", // Model File - 2D
            "M3", // Model File - 3D
            "MI", // Minutes / Action
            "MR", // Manufacture Information
            "MS", // Method Statement
            "PP", // Presentation
            "PR", // Programme
            "RD", // Room Data Sheet
            "RI", // RFI
            "RP", // Report
            "SA", // Schedule of Accommodation
            "SC", // Schedule
            "SH", // Safety Hazard
            "SN", // Snagging List
            "SP", // Specification
            "SU", // Survey
            "VS", // Visualisation
        };

        // ── Suitability / CDE state (BS EN ISO 19650-1 §A — "Suitability code") ──
        // WIP / Shared / Published / Archive container states.
        public static readonly string[] SuitabilityCodes =
        {
            // Work in Progress
            "S0",  // Initial WIP / draft
            // Shared (for purpose)
            "S1",  // Suitable for coordination
            "S2",  // Suitable for information
            "S3",  // Suitable for review and comment
            "S4",  // Suitable for stage approval
            "S6",  // Suitable for PIM authorization
            "S7",  // Suitable for AIM authorization
            // Published (authorisation)
            "A1",  // Authorized and accepted
            "A2",  // Authorized for construction
            "A3",  // Authorized for manufacture / installation
            "A4",  // Authorized for installation
            "A5",  // Authorized for archive
            "B1",  // Partial sign-off, with comments
            "B2",  // Partial sign-off, with major comments
            "B3",  // Partial sign-off, ongoing review
            "B4",  // Partial sign-off, manufacture
            "B5",  // Partial sign-off, installation
            "B6",  // Partial sign-off, archive
            // Archive
            "AR",  // Archive
        };

        public static readonly Dictionary<string, string> SuitabilityLabels = new Dictionary<string, string>
        {
            { "S0", "S0 — Initial WIP / draft" },
            { "S1", "S1 — Suitable for coordination" },
            { "S2", "S2 — Suitable for information" },
            { "S3", "S3 — Suitable for review & comment" },
            { "S4", "S4 — Suitable for stage approval" },
            { "S6", "S6 — Suitable for PIM authorization" },
            { "S7", "S7 — Suitable for AIM authorization" },
            { "A1", "A1 — Authorized and accepted" },
            { "A2", "A2 — Authorized for construction" },
            { "A3", "A3 — Authorized for manufacture" },
            { "A4", "A4 — Authorized for installation" },
            { "A5", "A5 — Authorized for archive" },
            { "B1", "B1 — Partial sign-off, w/ comments" },
            { "B2", "B2 — Partial sign-off, w/ major comments" },
            { "B3", "B3 — Partial sign-off, ongoing" },
            { "B4", "B4 — Partial sign-off, manufacture" },
            { "B5", "B5 — Partial sign-off, installation" },
            { "B6", "B6 — Partial sign-off, archive" },
            { "AR", "AR — Archive" },
        };

        // ── Revision codes (UK convention layered on top of ISO 19650) ──
        public static readonly string[] RevisionPrefixes =
        {
            "P",  // Preliminary (pre-construction)
            "C",  // Construction (post-tender)
            "T",  // Tender
            "I",  // Information
            "R",  // Revision
            "A",  // As-built
        };

        // ── RIBA Plan of Work 2020 stages ──
        public static readonly string[] RibaStages =
        {
            "*",
            "0",  // Strategic Definition
            "1",  // Preparation and Briefing
            "2",  // Concept Design
            "3",  // Spatial Coordination
            "4",  // Technical Design
            "5",  // Manufacturing and Construction
            "6",  // Handover
            "7",  // Use
        };

        public static readonly Dictionary<string, string> RibaStageLabels = new Dictionary<string, string>
        {
            { "*", "Any stage / wildcard" },
            { "0", "0 — Strategic Definition" },
            { "1", "1 — Preparation and Briefing" },
            { "2", "2 — Concept Design" },
            { "3", "3 — Spatial Coordination" },
            { "4", "4 — Technical Design" },
            { "5", "5 — Manufacturing and Construction" },
            { "6", "6 — Handover" },
            { "7", "7 — Use" },
        };

        // ── Volume / System codes (ISO 19650 §A — "Volume/System" field) ──
        public static readonly string[] VolumeCodes =
        {
            "ZZ",  // Multiple / general
            "XX",  // No location / whole site
            "01", "02", "03", "04", "05", "06", "07", "08", "09",
            "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
        };

        // ── Level codes (ISO 19650 §A — "Levels and Locations" field) ──
        public static readonly string[] LevelCodes =
        {
            "ZZ",  // Multiple
            "XX",  // No level
            "B2", "B1",
            "00", "01", "02", "03", "04", "05", "06", "07", "08", "09",
            "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
            "RF",  // Roof
            "MZ",  // Mezzanine
            "PH",  // Penthouse
        };

        // ── Drawing Purpose (STING extension on top of ISO Type=DR) ──
        public static readonly string[] DrawingPurposes =
        {
            "Plan",
            "RCP",
            "Section",
            "Elevation",
            "Detail",
            "Schedule",
            "Spool",
            "Coordination",
            "Legend",
            "ThreeD",
            "Cover",
            "Startup",
            "Render",
            "Submission",
            "Clarification",
            "ClientReview",
            "DesignReview",
        };

        // ── Paper sizes (ISO 216) ──
        public static readonly string[] PaperSizes = { "A0", "A1", "A2", "A3", "A4" };

        public static readonly Dictionary<string, string> PaperSizeLabels = new Dictionary<string, string>
        {
            { "A0", "A0 — 841 × 1189 mm" },
            { "A1", "A1 — 594 × 841 mm" },
            { "A2", "A2 — 420 × 594 mm" },
            { "A3", "A3 — 297 × 420 mm" },
            { "A4", "A4 — 210 × 297 mm" },
        };

        public static readonly string[] Orientations = { "Landscape", "Portrait" };

        // ── Detail levels (Revit native) ──
        public static readonly string[] DetailLevels = { "Coarse", "Medium", "Fine" };

        // ── Crop strategies ──
        public static readonly string[] CropKinds =
            { "ScopeBox", "ScopeBoxOrBbox", "TightBbox", "RoomBoundary", "None" };

        // ── Section / elevation marker bubble styles (Revit native) ──
        public static readonly string[] BubbleStyles = { "Filled", "Open", "Dash" };

        // ── Dimension strategies (STING annotation rule pack) ──
        public static readonly string[] DimensionStrategies = { "Linear", "Ordinate", "Chain" };

        // ── Colour schemes (STING ColorHelper + style packs) ──
        public static readonly string[] ColorSchemes =
            { "Monochrome", "Discipline", "Pastel", "RAG", "Spectral", "Warm", "Cool", "High Contrast" };

        // ── Authority codes (Uganda + UK common authorities) ──
        public static readonly string[] AuthorityCodes =
        {
            "",      // none / non-submission
            "KCCA",  // Kampala Capital City Authority
            "ERA",   // Electricity Regulatory Authority
            "NEMA",  // National Environment Management Authority
            "BC",    // UK Building Control
            "PA",    // UK Planning Authority
            "EA",    // Environment Agency (UK)
        };

        // ── Common ISO 19650 sheet-number patterns ──
        // {Project}-{Originator}-{Volume}-{Level}-{Type}-{Role}-{Number}
        // Concrete templates for common deliverables.
        public static readonly string[] SheetNumberPatterns =
        {
            "{disc}-{lvl}-{seq:D3}",                              // STING short form
            "{disc}-{sys}-{lvl}-{seq:D3}",                        // STING with system
            "{spool}-{disc}-{seq:D3}",                            // Fabrication
            "{disc}-{lvl}-{mark}-{seq:D2}",                       // Section / detail
            "ISO19650:{prj}-{orig}-{vol}-{lvl}-DR-{role}-{seq:D4}", // Full BS 1192 / ISO 19650-2
        };

        public static readonly string[] SheetNamePatterns =
        {
            "{discipline} Plan — {lvl}",
            "{discipline} {purpose} — {lvl}",
            "Section {mark} — {lvl}",
            "Spool {spool} — {discipline}",
            "Detail {mark}",
            "Drawing Register",
            "Cover Page — {prj}",
            "Issue Status — {datadrop}",
        };

        // ── Tag families (STING category → tag-family map) ──
        public static readonly string[] CommonTagFamilies =
        {
            "STING_TAG_ROOM",
            "STING_TAG_DOOR",
            "STING_TAG_WINDOW",
            "STING_TAG_WALL",
            "STING_TAG_FLOOR",
            "STING_TAG_CEILING",
            "STING_TAG_HVAC",
            "STING_TAG_PIPE",
            "STING_TAG_DUCT",
            "STING_TAG_ELECTRICAL",
            "STING_TAG_LIGHTING",
            "STING_TAG_PLUMBING",
            "STING_TAG_FIRE",
            "STING_TAG_GENERIC",
        };
    }
}
