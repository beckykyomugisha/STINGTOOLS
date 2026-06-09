using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Creates and loads STING tag families (.rfa) for every taggable category
    /// declared in the v5 tag configuration CSVs. The exact count is
    /// <see cref="TagFamilyConfig.TotalFamilyCount"/> at runtime (121 standard +
    /// 8 tie-in point + 3 discipline sheet + 4 structural variant +
    /// <see cref="TagFamilyConfig.MepVariantFamilies"/>.Length MEP variant +
    /// <see cref="TagFamilyConfig.HealthcareVariantFamilies"/>.Length healthcare
    /// variant). Each tag family is created from the appropriate Revit .rft
    /// annotation template and configured with STING shared parameters
    /// (ASS_TAG_1_TXT, etc.).
    ///
    /// Workflow:
    ///   1. Locate Revit annotation tag templates (.rft) on disk
    ///   2. For each taggable category, create a family from the template
    ///   3. Add STING shared parameters via FamilyManager
    ///   4. Save .rfa to TagFamilies/ output directory
    ///   5. Load into the current project
    ///   6. Report results with per-category status
    ///
    /// Post-creation step: User opens each family in Family Editor and sets the
    /// Label to display ASS_TAG_1_TXT (cannot be done programmatically — Revit API limitation).
    /// </summary>
    internal static class TagFamilyConfig
    {
        /// <summary>Naming prefix for all STING tag families.</summary>
        public const string FamilyPrefix = "STING";

        /// <summary>
        /// Maps BuiltInCategory to the expected Revit .rft annotation template filename.
        /// Revit ships these templates in the Family Templates/Annotations/ directory.
        /// If the specific template is not found, falls back to Generic Tag.rft.
        /// </summary>
        public static readonly Dictionary<BuiltInCategory, string> CategoryTemplateMap =
            new Dictionary<BuiltInCategory, string>
        {
            // ── MEP — Mechanical / HVAC ────────────────────────────────────
            { BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment Tag.rft" },
            { BuiltInCategory.OST_MechanicalEquipmentSet, "Mechanical Equipment Tag.rft" },
            { BuiltInCategory.OST_MechanicalControlDevices, "Mechanical Equipment Tag.rft" },
            { BuiltInCategory.OST_DuctCurves, "Duct Tag.rft" },
            { BuiltInCategory.OST_DuctFitting, "Duct Fitting Tag.rft" },
            { BuiltInCategory.OST_DuctAccessory, "Duct Accessory Tag.rft" },
            { BuiltInCategory.OST_DuctTerminal, "Air Terminal Tag.rft" },
            { BuiltInCategory.OST_DuctInsulations, "Duct Tag.rft" },
            { BuiltInCategory.OST_DuctLinings, "Duct Tag.rft" },
            { BuiltInCategory.OST_FlexDuctCurves, "Flex Duct Tag.rft" },

            // ── MEP — Plumbing / Piping ────────────────────────────────────
            { BuiltInCategory.OST_PipeCurves, "Pipe Tag.rft" },
            { BuiltInCategory.OST_PipeFitting, "Pipe Fitting Tag.rft" },
            { BuiltInCategory.OST_PipeAccessory, "Pipe Accessory Tag.rft" },
            { BuiltInCategory.OST_PipeInsulations, "Pipe Tag.rft" },
            { BuiltInCategory.OST_FlexPipeCurves, "Flex Pipe Tag.rft" },
            { BuiltInCategory.OST_PlumbingEquipment, "Plumbing Fixture Tag.rft" },
            { BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixture Tag.rft" },

            // ── MEP — Fire Protection ──────────────────────────────────────
            { BuiltInCategory.OST_Sprinklers, "Sprinkler Tag.rft" },
            { BuiltInCategory.OST_FireAlarmDevices, "Fire Alarm Device Tag.rft" },
            { BuiltInCategory.OST_FireProtection, "Generic Tag.rft" },

            // ── MEP — Electrical ───────────────────────────────────────────
            { BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment Tag.rft" },
            { BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixture Tag.rft" },
            { BuiltInCategory.OST_LightingFixtures, "Lighting Fixture Tag.rft" },
            { BuiltInCategory.OST_LightingDevices, "Lighting Device Tag.rft" },
            { BuiltInCategory.OST_Conduit, "Conduit Tag.rft" },
            { BuiltInCategory.OST_ConduitFitting, "Conduit Fitting Tag.rft" },
            { BuiltInCategory.OST_CableTray, "Cable Tray Tag.rft" },
            { BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fitting Tag.rft" },
            { BuiltInCategory.OST_ElectricalCircuit, "Electrical Equipment Tag.rft" },

            // ── MEP — Low Voltage / Communications ─────────────────────────
            { BuiltInCategory.OST_CommunicationDevices, "Communication Device Tag.rft" },
            { BuiltInCategory.OST_DataDevices, "Data Device Tag.rft" },
            { BuiltInCategory.OST_NurseCallDevices, "Nurse Call Device Tag.rft" },
            { BuiltInCategory.OST_SecurityDevices, "Security Device Tag.rft" },
            { BuiltInCategory.OST_TelephoneDevices, "Telephone Device Tag.rft" },
            { BuiltInCategory.OST_AudioVisualDevices, "Communication Device Tag.rft" },

            // ── MEP — MEP Fabrication ──────────────────────────────────────
            { BuiltInCategory.OST_FabricationContainment, "Cable Tray Tag.rft" },
            { BuiltInCategory.OST_FabricationDuctwork, "Duct Tag.rft" },
            { BuiltInCategory.OST_FabricationDuctworkStiffeners, "Duct Tag.rft" },
            { BuiltInCategory.OST_FabricationHangers, "Generic Tag.rft" },
            { BuiltInCategory.OST_FabricationPipework, "Pipe Tag.rft" },
            { BuiltInCategory.OST_MEPSpaces, "Room Tag.rft" },
            { BuiltInCategory.OST_HVAC_Zones, "Room Tag.rft" },

            // ── Architecture — Enclosure ───────────────────────────────────
            { BuiltInCategory.OST_Doors, "Door Tag.rft" },
            { BuiltInCategory.OST_Windows, "Window Tag.rft" },
            { BuiltInCategory.OST_Walls, "Wall Tag.rft" },
            { BuiltInCategory.OST_Floors, "Floor Tag.rft" },
            { BuiltInCategory.OST_Ceilings, "Ceiling Tag.rft" },
            { BuiltInCategory.OST_Roofs, "Roof Tag.rft" },
            { BuiltInCategory.OST_Rooms, "Room Tag.rft" },
            { BuiltInCategory.OST_Areas, "Area Tag.rft" },
            { BuiltInCategory.OST_CurtainWallPanels, "Curtain Panel Tag.rft" },
            { BuiltInCategory.OST_CurtainWallMullions, "Curtain Wall Mullion Tag.rft" },
            { BuiltInCategory.OST_Cornices, "Wall Tag.rft" },
            { BuiltInCategory.OST_EdgeSlab, "Floor Tag.rft" },
            { BuiltInCategory.OST_RoofSoffit, "Roof Tag.rft" },
            { BuiltInCategory.OST_Fascia, "Roof Tag.rft" },
            { BuiltInCategory.OST_Gutter, "Roof Tag.rft" },
            { BuiltInCategory.OST_Mass, "Generic Tag.rft" },

            // ── Architecture — Interior / Furnishings ──────────────────────
            { BuiltInCategory.OST_Furniture, "Furniture Tag.rft" },
            { BuiltInCategory.OST_FurnitureSystems, "Furniture System Tag.rft" },
            { BuiltInCategory.OST_Casework, "Casework Tag.rft" },
            { BuiltInCategory.OST_FoodServiceEquipment, "Generic Tag.rft" },
            { BuiltInCategory.OST_MedicalEquipment, "Generic Tag.rft" },
            { BuiltInCategory.OST_Signage, "Generic Tag.rft" },
            { BuiltInCategory.OST_Entourage, "Generic Tag.rft" },

            // ── Architecture — Circulation ─────────────────────────────────
            { BuiltInCategory.OST_Stairs, "Stair Tag.rft" },
            { BuiltInCategory.OST_StairsRuns, "Stair Tag.rft" },
            { BuiltInCategory.OST_StairsLandings, "Stair Tag.rft" },
            { BuiltInCategory.OST_StairsSupports, "Stair Tag.rft" },
            { BuiltInCategory.OST_Ramps, "Ramp Tag.rft" },
            { BuiltInCategory.OST_Railings, "Generic Tag.rft" },
            { BuiltInCategory.OST_RailingTopRail, "Generic Tag.rft" },
            { BuiltInCategory.OST_RailingHandRail, "Generic Tag.rft" },
            { BuiltInCategory.OST_VerticalCirculation, "Generic Tag.rft" },

            // ── Structure ──────────────────────────────────────────────────
            { BuiltInCategory.OST_StructuralColumns, "Structural Column Tag.rft" },
            { BuiltInCategory.OST_StructuralFraming, "Structural Framing Tag.rft" },
            { BuiltInCategory.OST_StructuralFoundation, "Structural Foundation Tag.rft" },
            { BuiltInCategory.OST_Columns, "Column Tag.rft" },
            { BuiltInCategory.OST_StructuralTruss, "Structural Framing Tag.rft" },
            { BuiltInCategory.OST_StructuralStiffener, "Structural Framing Tag.rft" },
            { BuiltInCategory.OST_StructConnections, "Generic Tag.rft" },
            { BuiltInCategory.OST_StructuralFramingSystem, "Structural Framing Tag.rft" },
            { BuiltInCategory.OST_Rebar, "Structural Framing Tag.rft" },
            { BuiltInCategory.OST_Coupler, "Generic Tag.rft" },
            { BuiltInCategory.OST_FabricReinforcement, "Generic Tag.rft" },
            { BuiltInCategory.OST_AreaRein, "Generic Tag.rft" },
            { BuiltInCategory.OST_PathRein, "Generic Tag.rft" },

            // ── Generic / Specialty / Site ──────────────────────────────────
            { BuiltInCategory.OST_GenericModel, "Generic Model Tag.rft" },
            { BuiltInCategory.OST_SpecialityEquipment, "Specialty Equipment Tag.rft" },
            { BuiltInCategory.OST_Parking, "Parking Tag.rft" },
            { BuiltInCategory.OST_Site, "Site Tag.rft" },
            { BuiltInCategory.OST_Planting, "Generic Tag.rft" },
            { BuiltInCategory.OST_Hardscape, "Generic Tag.rft" },
            { BuiltInCategory.OST_Roads, "Generic Tag.rft" },
            { BuiltInCategory.OST_BuildingPad, "Generic Tag.rft" },
            { BuiltInCategory.OST_Toposolid, "Generic Tag.rft" },
            { BuiltInCategory.OST_Parts, "Generic Tag.rft" },
            { BuiltInCategory.OST_Assemblies, "Generic Tag.rft" },
            { BuiltInCategory.OST_DetailComponents, "Generic Tag.rft" },
            { BuiltInCategory.OST_ProfileFamilies, "Generic Tag.rft" },
            { BuiltInCategory.OST_Materials, "Material Tag.rft" },

            // ── Loads ──────────────────────────────────────────────────────
            { BuiltInCategory.OST_PointLoads, "Generic Tag.rft" },
            { BuiltInCategory.OST_LineLoads, "Generic Tag.rft" },
            { BuiltInCategory.OST_AreaLoads, "Generic Tag.rft" },
            { BuiltInCategory.OST_InternalPointLoads, "Generic Tag.rft" },
            { BuiltInCategory.OST_InternalLineLoads, "Generic Tag.rft" },
            { BuiltInCategory.OST_InternalAreaLoads, "Generic Tag.rft" },

            // ── Analytical ─────────────────────────────────────────────────
            { BuiltInCategory.OST_AnalyticalMember, "Generic Tag.rft" },
            { BuiltInCategory.OST_AnalyticalNodes, "Generic Tag.rft" },
            { BuiltInCategory.OST_AnalyticalPanel, "Generic Tag.rft" },
            { BuiltInCategory.OST_AnalyticalOpening, "Generic Tag.rft" },
            { BuiltInCategory.OST_RigidLinksAnalytical, "Generic Tag.rft" },

            // ── Miscellaneous ──────────────────────────────────────────────
            { BuiltInCategory.OST_IOSModelGroups, "Generic Tag.rft" },
            { BuiltInCategory.OST_RvtLinks, "Generic Tag.rft" },
            { BuiltInCategory.OST_SiteProperty, "Generic Tag.rft" },

            // ── Additional (align with LABEL_DEFINITIONS v5.5) ───────────────
            { BuiltInCategory.OST_StructConnectionBolts, "Generic Tag.rft" },
            { BuiltInCategory.OST_SitePropertyLineSegment, "Generic Tag.rft" },
            { BuiltInCategory.OST_ToposolidLink, "Generic Tag.rft" },
            { BuiltInCategory.OST_StructConnectionWelds, "Generic Tag.rft" },
            { BuiltInCategory.OST_Wire, "Wire Tag.rft" },

            // ── Sheets (base category for all discipline sheet tags) ─────────
            { BuiltInCategory.OST_Sheets, "Generic Tag.rft" },

        };

        /// <summary>
        /// Human-readable category name for family naming and reporting.
        /// Uses the Revit display name where possible.
        /// </summary>
        public static readonly Dictionary<BuiltInCategory, string> CategoryDisplayName =
            new Dictionary<BuiltInCategory, string>
        {
            // MEP — Mechanical / HVAC
            { BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment" },
            { BuiltInCategory.OST_MechanicalEquipmentSet, "Mechanical Equipment Sets" },
            { BuiltInCategory.OST_MechanicalControlDevices, "Mechanical Control Devices" },
            { BuiltInCategory.OST_DuctCurves, "Ducts" },
            { BuiltInCategory.OST_DuctFitting, "Duct Fittings" },
            { BuiltInCategory.OST_DuctAccessory, "Duct Accessories" },
            { BuiltInCategory.OST_DuctTerminal, "Air Terminals" },
            { BuiltInCategory.OST_DuctInsulations, "Duct Insulation" },
            { BuiltInCategory.OST_DuctLinings, "Duct Lining" },
            { BuiltInCategory.OST_FlexDuctCurves, "Flex Ducts" },
            // MEP — Plumbing / Piping
            { BuiltInCategory.OST_PipeCurves, "Pipes" },
            { BuiltInCategory.OST_PipeFitting, "Pipe Fittings" },
            { BuiltInCategory.OST_PipeAccessory, "Pipe Accessories" },
            { BuiltInCategory.OST_PipeInsulations, "Pipe Insulation" },
            { BuiltInCategory.OST_FlexPipeCurves, "Flex Pipes" },
            { BuiltInCategory.OST_PlumbingEquipment, "Plumbing Equipment" },
            { BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures" },
            // MEP — Fire Protection
            { BuiltInCategory.OST_Sprinklers, "Sprinklers" },
            { BuiltInCategory.OST_FireAlarmDevices, "Fire Alarm Devices" },
            { BuiltInCategory.OST_FireProtection, "Fire Protection" },
            // MEP — Electrical
            { BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment" },
            { BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixtures" },
            { BuiltInCategory.OST_LightingFixtures, "Lighting Fixtures" },
            { BuiltInCategory.OST_LightingDevices, "Lighting Devices" },
            { BuiltInCategory.OST_Conduit, "Conduits" },
            { BuiltInCategory.OST_ConduitFitting, "Conduit Fittings" },
            { BuiltInCategory.OST_CableTray, "Cable Trays" },
            { BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fittings" },
            { BuiltInCategory.OST_ElectricalCircuit, "Electrical Connectors" },
            // MEP — Low Voltage / Communications
            { BuiltInCategory.OST_CommunicationDevices, "Communication Devices" },
            { BuiltInCategory.OST_DataDevices, "Data Devices" },
            { BuiltInCategory.OST_NurseCallDevices, "Nurse Call Devices" },
            { BuiltInCategory.OST_SecurityDevices, "Security Devices" },
            { BuiltInCategory.OST_TelephoneDevices, "Telephone Devices" },
            { BuiltInCategory.OST_AudioVisualDevices, "Audio Visual Devices" },
            // MEP — MEP Fabrication
            { BuiltInCategory.OST_FabricationContainment, "MEP Fabrication Containment" },
            { BuiltInCategory.OST_FabricationDuctwork, "MEP Fabrication Ductwork" },
            { BuiltInCategory.OST_FabricationDuctworkStiffeners, "MEP Fabrication Ductwork Stiffeners" },
            { BuiltInCategory.OST_FabricationHangers, "MEP Fabrication Hangers" },
            { BuiltInCategory.OST_FabricationPipework, "MEP Fabrication Pipework" },
            { BuiltInCategory.OST_MEPSpaces, "Spaces" },
            { BuiltInCategory.OST_HVAC_Zones, "Zones" },
            // Architecture — Enclosure
            { BuiltInCategory.OST_Doors, "Doors" },
            { BuiltInCategory.OST_Windows, "Windows" },
            { BuiltInCategory.OST_Walls, "Walls" },
            { BuiltInCategory.OST_Floors, "Floors" },
            { BuiltInCategory.OST_Ceilings, "Ceilings" },
            { BuiltInCategory.OST_Roofs, "Roofs" },
            { BuiltInCategory.OST_Rooms, "Rooms" },
            { BuiltInCategory.OST_Areas, "Areas" },
            { BuiltInCategory.OST_CurtainWallPanels, "Curtain Panel" },
            { BuiltInCategory.OST_CurtainWallMullions, "Curtain Wall Mullion" },
            { BuiltInCategory.OST_Cornices, "Wall Sweeps" },
            { BuiltInCategory.OST_EdgeSlab, "Slab Edges" },
            { BuiltInCategory.OST_RoofSoffit, "Roof Soffits" },
            { BuiltInCategory.OST_Fascia, "Fascia" },
            { BuiltInCategory.OST_Gutter, "Gutter" },
            { BuiltInCategory.OST_Mass, "Mass" },
            // Architecture — Interior / Furnishings
            { BuiltInCategory.OST_Furniture, "Furniture" },
            { BuiltInCategory.OST_FurnitureSystems, "Furniture Systems" },
            { BuiltInCategory.OST_Casework, "Casework" },
            { BuiltInCategory.OST_FoodServiceEquipment, "Food Service Equipment" },
            { BuiltInCategory.OST_MedicalEquipment, "Medical Equipment" },
            { BuiltInCategory.OST_Signage, "Signage" },
            { BuiltInCategory.OST_Entourage, "Entourage" },
            // Architecture — Circulation
            { BuiltInCategory.OST_Stairs, "Stairs" },
            { BuiltInCategory.OST_StairsRuns, "Stair Runs" },
            { BuiltInCategory.OST_StairsLandings, "Stair Landings" },
            { BuiltInCategory.OST_StairsSupports, "Stair Supports" },
            { BuiltInCategory.OST_Ramps, "Ramps" },
            { BuiltInCategory.OST_Railings, "Railing" },
            { BuiltInCategory.OST_RailingTopRail, "Top Rails" },
            { BuiltInCategory.OST_RailingHandRail, "Handrails" },
            { BuiltInCategory.OST_VerticalCirculation, "Vertical Circulation" },
            // Structure
            { BuiltInCategory.OST_StructuralColumns, "Structural Columns" },
            { BuiltInCategory.OST_StructuralFraming, "Structural Framing" },
            { BuiltInCategory.OST_StructuralFoundation, "Structural Foundations" },
            { BuiltInCategory.OST_Columns, "Columns" },
            { BuiltInCategory.OST_StructuralTruss, "Structural Trusses" },
            { BuiltInCategory.OST_StructuralStiffener, "Structural Stiffeners" },
            { BuiltInCategory.OST_StructConnections, "Structural Connection" },
            { BuiltInCategory.OST_StructuralFramingSystem, "Structural Beam Systems" },
            { BuiltInCategory.OST_Rebar, "Structural Rebar" },
            { BuiltInCategory.OST_Coupler, "Structural Rebar Couplers" },
            { BuiltInCategory.OST_FabricReinforcement, "Structural Fabric Reinforcement" },
            { BuiltInCategory.OST_AreaRein, "Structural Area Reinforcement" },
            { BuiltInCategory.OST_PathRein, "Structural Path Reinforcement" },
            // Generic / Specialty / Site
            { BuiltInCategory.OST_GenericModel, "Generic Models" },
            { BuiltInCategory.OST_SpecialityEquipment, "Specialty Equipment" },
            { BuiltInCategory.OST_Parking, "Parking" },
            { BuiltInCategory.OST_Site, "Site" },
            { BuiltInCategory.OST_Planting, "Planting" },
            { BuiltInCategory.OST_Hardscape, "Hardscape" },
            { BuiltInCategory.OST_Roads, "Roads" },
            { BuiltInCategory.OST_BuildingPad, "Pads" },
            { BuiltInCategory.OST_Toposolid, "Toposolid" },
            { BuiltInCategory.OST_Parts, "Parts" },
            { BuiltInCategory.OST_Assemblies, "Assemblies" },
            { BuiltInCategory.OST_DetailComponents, "Detail Items" },
            { BuiltInCategory.OST_ProfileFamilies, "Profiles" },
            { BuiltInCategory.OST_Materials, "Materials" },
            // Loads
            { BuiltInCategory.OST_PointLoads, "Point Loads" },
            { BuiltInCategory.OST_LineLoads, "Line Loads" },
            { BuiltInCategory.OST_AreaLoads, "Area Loads" },
            { BuiltInCategory.OST_InternalPointLoads, "Internal Point Loads" },
            { BuiltInCategory.OST_InternalLineLoads, "Internal Line Loads" },
            { BuiltInCategory.OST_InternalAreaLoads, "Internal Area Loads" },
            // Analytical
            { BuiltInCategory.OST_AnalyticalMember, "Analytical Members" },
            { BuiltInCategory.OST_AnalyticalNodes, "Analytical Nodes" },
            { BuiltInCategory.OST_AnalyticalPanel, "Analytical Panels" },
            { BuiltInCategory.OST_AnalyticalOpening, "Analytical Openings" },
            { BuiltInCategory.OST_RigidLinksAnalytical, "Analytical Links" },
            // Miscellaneous
            { BuiltInCategory.OST_IOSModelGroups, "Model Groups" },
            { BuiltInCategory.OST_RvtLinks, "RVT Links" },
            { BuiltInCategory.OST_SiteProperty, "Property Lines" },

            // Additional (align with LABEL_DEFINITIONS v5.5)
            { BuiltInCategory.OST_StructConnectionBolts, "Bolt" },
            { BuiltInCategory.OST_SitePropertyLineSegment, "Property Line Segments" },
            { BuiltInCategory.OST_ToposolidLink, "Toposolid Links" },
            { BuiltInCategory.OST_StructConnectionWelds, "Weld" },
            { BuiltInCategory.OST_Wire, "Wire" },

            // Sheets (base category)
            { BuiltInCategory.OST_Sheets, "Sheet Document" },

        };

        /// <summary>
        /// Tie-in point tag families (ISO 19650-3 interface management).
        /// These create ADDITIONAL tag families for existing BuiltInCategories,
        /// so they cannot go in the CategoryTemplateMap dictionary (duplicate keys).
        /// Each tuple: (BuiltInCategory, templateName, displayName, familySuffix)
        /// </summary>
        public static readonly (BuiltInCategory bic, string template, string display, string suffix)[] TieInPointFamilies =
        {
            (BuiltInCategory.OST_PipeCurves,     "Pipe Tag.rft",                "Tie-In Point (Pipe)",            "Tie-In Point Tag (Pipe — Plumbing & Hydraulic)"),
            (BuiltInCategory.OST_DuctCurves,     "Duct Tag.rft",                "Tie-In Point (Duct)",            "Tie-In Point Tag (Duct — HVAC)"),
            (BuiltInCategory.OST_Conduit,        "Conduit Tag.rft",             "Tie-In Point (Conduit)",         "Tie-In Point Tag (Conduit — Electrical LV/ELV)"),
            (BuiltInCategory.OST_CableTray,      "Cable Tray Tag.rft",          "Tie-In Point (Cable Tray)",      "Tie-In Point Tag (Cable Tray — Electrical)"),
            (BuiltInCategory.OST_Sprinklers,     "Sprinkler Tag.rft",           "Tie-In Point (Fire Protection)", "Tie-In Point Tag (Fire Protection — Sprinkler / Suppression)"),
            (BuiltInCategory.OST_GenericModel,    "Generic Tag.rft",             "Tie-In Point (Gas)",             "Tie-In Point Tag (Gas — Medical / Industrial / Natural Gas)"),
            // Pipe system-specific tie-in variants (from MEP CSV #49, #50)
            (BuiltInCategory.OST_PipeCurves,     "Pipe Tag.rft",                "Tie-In Point (Fire Protection Pipe)", "Tie-In FP Pipe"),
            (BuiltInCategory.OST_PipeCurves,     "Pipe Tag.rft",                "Tie-In Point (Gas Pipe)",             "Tie-In Gas Pipe"),
        };

        /// <summary>
        /// Discipline-specific sheet tag families.
        /// Sheets (OST_Sheets) is already in CategoryTemplateMap for the base "Sheet Document Tag".
        /// These create ADDITIONAL sheet tag families per discipline (ARCH, MEP, STR).
        /// </summary>
        public static readonly (BuiltInCategory bic, string template, string display, string suffix)[] DisciplineSheetFamilies =
        {
            (BuiltInCategory.OST_Sheets, "Generic Tag.rft", "Sheets — Architectural discipline",        "Architectural Sheet"),
            (BuiltInCategory.OST_Sheets, "Generic Tag.rft", "Sheets — MEP disciplines (M/E/P/FP/LV)",  "MEP Sheet"),
            (BuiltInCategory.OST_Sheets, "Generic Tag.rft", "Sheets — Structural discipline",           "Structural Sheet"),
        };

        /// <summary>
        /// Structural discipline variant tag families.
        /// These are ADDITIONAL tag families for categories already covered in the base map,
        /// but with structural-specific naming/configuration (from STR CSV).
        /// </summary>
        public static readonly (BuiltInCategory bic, string template, string display, string suffix)[] StructuralVariantFamilies =
        {
            (BuiltInCategory.OST_Floors,            "Generic Tag.rft",           "Floors (Structural)",                  "Structural Slab"),
            (BuiltInCategory.OST_Walls,             "Generic Tag.rft",           "Walls (Structural/Load-bearing)",      "Structural Wall"),
            (BuiltInCategory.OST_StructuralFraming, "Structural Framing Tag.rft","Structural Framing (Bracing)",         "Brace / Truss"),
            (BuiltInCategory.OST_Columns,           "Generic Tag.rft",           "Columns (Architectural)",              "Architectural Column"),
        };

        /// <summary>
        /// MEP category variant tag families.
        /// These create ADDITIONAL tag families for categories already in CategoryTemplateMap
        /// but with MEP-specific naming (e.g., MEP Sleeve uses OST_GenericModel).
        /// </summary>
        public static readonly (BuiltInCategory bic, string template, string display, string suffix)[] MepVariantFamilies =
        {
            (BuiltInCategory.OST_GenericModel,        "Generic Model Tag.rft",         "MEP Sleeve (Fire-rated penetration)", "MEP Sleeve"),
            // ── Lightning Protection System (BS EN 62305) — MEP CSV families #54..#59
            (BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment Tag.rft",  "LPS Air Terminal (BS EN 62305-3 §5.2)",   "LPS Air Terminal"),
            (BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment Tag.rft",  "LPS Down Conductor (BS EN 62305-3 §5.3)", "LPS Down Conductor"),
            (BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment Tag.rft",  "LPS Earth Electrode (BS EN 62305-3 §5.4)","LPS Earth Electrode"),
            (BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment Tag.rft",  "LPS Bond / Spark Gap (BS EN 62305-3)",    "LPS Bond"),
            (BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment Tag.rft",  "LPS SPD (BS EN 62305-4)",                 "LPS SPD"),
            (BuiltInCategory.OST_ElectricalEquipment, "Electrical Equipment Tag.rft",  "LPS Test Clamp / Inspection Point",       "LPS Test Clamp"),
            // ── LPS reuse variants (cross-discipline) — GEN CSV #34, STR CSV #22, ARCH CSV #36
            (BuiltInCategory.OST_GenericModel,         "Generic Model Tag.rft",         "LPS Generic Component (cross-disc reuse)",          "LPS Generic Component"),
            (BuiltInCategory.OST_StructuralFoundation, "Structural Foundation Tag.rft", "LPS Foundation Earth (Structural Reuse)",           "LPS Foundation Earth (Structural Reuse)"),
            (BuiltInCategory.OST_Roofs,                "Roof Tag.rft",                  "LPS Natural Air Termination (Architectural Reuse)", "LPS Natural Air Termination (Architectural Reuse)"),
        };

        /// <summary>
        /// Healthcare tag family variants (HTM / HBN / NHS pack).
        /// Sourced from <c>STING_TAG_CONFIG_v5_0_HEALTH.csv</c> (58 declarations,
        /// 56 unique family names). Each tuple binds one .rfa file to one
        /// BuiltInCategory so multi-category families like Anti-Ligature get a
        /// distinct file per binding (Revit tag families are single-category).
        /// </summary>
        public static readonly (BuiltInCategory bic, string template, string display, string suffix)[] HealthcareVariantFamilies =
        {
            // Rooms (4)
            (BuiltInCategory.OST_Rooms,                "Room Tag.rft",                 "Clinical Room (HBN)",                                  "Clinical Room"),
            (BuiltInCategory.OST_Rooms,                "Room Tag.rft",                 "Pressure Regime (HTM 03-01)",                          "Pressure Regime"),
            (BuiltInCategory.OST_Rooms,                "Room Tag.rft",                 "Infection Class (HBN 04-01)",                          "Infection Class"),
            (BuiltInCategory.OST_Rooms,                "Room Tag.rft",                 "Bariatric (NHS England)",                              "Bariatric"),
            // Generic Models (6)
            (BuiltInCategory.OST_GenericModel,         "Generic Model Tag.rft",        "MRI Zone (MHRA DB2007(03))",                           "MRI Zone"),
            (BuiltInCategory.OST_GenericModel,         "Generic Model Tag.rft",        "5-Gauss Marker (MHRA)",                                "5-Gauss Marker"),
            (BuiltInCategory.OST_GenericModel,         "Generic Model Tag.rft",        "Hoist Track (BS EN 10535)",                            "Hoist Track"),
            (BuiltInCategory.OST_GenericModel,         "Generic Model Tag.rft",        "AGV Dock",                                             "AGV Dock"),
            (BuiltInCategory.OST_GenericModel,         "Generic Model Tag.rft",        "Controlled Area Sign (IRR 17)",                        "Controlled Area Sign"),
            (BuiltInCategory.OST_GenericModel,         "Generic Model Tag.rft",        "Dosimetry Post (IRR 17)",                              "Dosimetry Post"),
            // Doors (2) — Anti-Ligature disambiguated by host category
            (BuiltInCategory.OST_Doors,                "Door Tag.rft",                 "Anti-Ligature Door (MaxiCare guidance)",               "Anti-Ligature (Door)"),
            (BuiltInCategory.OST_Doors,                "Door Tag.rft",                 "X-ray Door (NCRP 147)",                                "X-ray Door"),
            // Windows (1)
            (BuiltInCategory.OST_Windows,              "Window Tag.rft",               "X-ray Window (NCRP 147)",                              "X-ray Window"),
            // Walls (3)
            (BuiltInCategory.OST_Walls,                "Wall Tag.rft",                 "X-ray Barrier (NCRP 147)",                             "X-ray Barrier"),
            (BuiltInCategory.OST_Walls,                "Wall Tag.rft",                 "MRI Faraday Cage (MHRA)",                              "MRI Faraday Cage"),
            (BuiltInCategory.OST_Walls,                "Wall Tag.rft",                 "Linac Maze (NCRP 151)",                                "Linac Maze"),
            // Lighting Fixtures (3) — Anti-Ligature disambiguated
            (BuiltInCategory.OST_LightingFixtures,     "Lighting Fixture Tag.rft",     "Anti-Ligature Lighting (NHS England)",                 "Anti-Ligature (Lighting Fixture)"),
            (BuiltInCategory.OST_LightingFixtures,     "Lighting Fixture Tag.rft",     "Examination Light (BS EN 60601-2-41)",                 "Examination Light"),
            (BuiltInCategory.OST_LightingFixtures,     "Lighting Fixture Tag.rft",     "Operating Light (BS EN 60601-2-41)",                   "Operating Light"),
            // Mechanical Equipment (5)
            (BuiltInCategory.OST_MechanicalEquipment,  "Mechanical Equipment Tag.rft", "VIE (HTM 02-01)",                                      "VIE"),
            (BuiltInCategory.OST_MechanicalEquipment,  "Mechanical Equipment Tag.rft", "Manifold (HTM 02-01)",                                 "Manifold"),
            (BuiltInCategory.OST_MechanicalEquipment,  "Mechanical Equipment Tag.rft", "Medical Air Plant (HTM 02-01)",                        "Medical Air Plant"),
            (BuiltInCategory.OST_MechanicalEquipment,  "Mechanical Equipment Tag.rft", "Medical Vacuum Plant (HTM 02-01)",                     "Medical Vacuum Plant"),
            (BuiltInCategory.OST_MechanicalEquipment,  "Mechanical Equipment Tag.rft", "AGS Plant (HTM 02-01)",                                "AGS Plant"),
            // Pipes (1)
            (BuiltInCategory.OST_PipeCurves,           "Pipe Tag.rft",                 "Medical Gas Pipeline (HTM 02-01)",                     "Medical Gas Pipeline"),
            // Pipe Accessories (1)
            (BuiltInCategory.OST_PipeAccessory,        "Pipe Accessory Tag.rft",       "Zone Valve Box (HTM 02-01)",                           "Zone Valve Box"),
            // Plumbing Fixtures (7) — Anti-Ligature disambiguated
            (BuiltInCategory.OST_PlumbingFixtures,     "Plumbing Fixture Tag.rft",     "Anti-Ligature Plumbing (NHS England)",                 "Anti-Ligature (Plumbing Fixture)"),
            (BuiltInCategory.OST_PlumbingFixtures,     "Plumbing Fixture Tag.rft",     "Medical Gas Terminal Unit (BS 5682)",                  "Medical Gas Terminal Unit"),
            (BuiltInCategory.OST_PlumbingFixtures,     "Plumbing Fixture Tag.rft",     "Area Alarm Panel (HTM 02-01)",                         "Area Alarm Panel"),
            (BuiltInCategory.OST_PlumbingFixtures,     "Plumbing Fixture Tag.rft",     "Master Alarm Panel (HTM 02-01)",                       "Master Alarm Panel"),
            (BuiltInCategory.OST_PlumbingFixtures,     "Plumbing Fixture Tag.rft",     "Scrub Trough (HBN 26)",                                "Scrub Trough"),
            (BuiltInCategory.OST_PlumbingFixtures,     "Plumbing Fixture Tag.rft",     "Bedpan Washer (HBN 09-03)",                            "Bedpan Washer"),
            (BuiltInCategory.OST_PlumbingFixtures,     "Plumbing Fixture Tag.rft",     "Birth Pool (HBN 09-02)",                               "Birth Pool"),
            // Specialty Equipment (10) — 8 healthcare variants + 2 base label variants
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "Bedhead Trunking (HBN 04-01)",                         "Bedhead Trunking"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "Pendant (HBN 26)",                                     "Pendant"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "Crash Cart (NHS resus)",                               "Crash Cart"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "AED Cabinet (BS EN 60601)",                            "AED Cabinet"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "Hand-Rub Dispenser (NICE NG139)",                      "Hand-Rub Dispenser"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "PTS Station (HTM 2024)",                               "PTS Station"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "RTLS Reader",                                          "RTLS Reader"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "Surgical Robot Bay (HBN 26)",                          "Surgical Robot Bay"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "Specialty Equipment — Asset management focus",         "Specialty Equipment Tag Asset"),
            (BuiltInCategory.OST_SpecialityEquipment,  "Specialty Equipment Tag.rft",  "Specialty Equipment — Lightweight general purpose",    "Specialty Equipment Tag General"),
            // Medical Equipment (16)
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Imaging Modality (IPEM 91)",                           "Imaging Modality"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Dialysis Station (HBN 07-01)",                         "Dialysis Station"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Incubator (HBN 09-02)",                                "Incubator"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Mortuary Fridge (HBN 20)",                             "Mortuary Fridge"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Autoclave (HTM 01-01)",                                "Autoclave"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Washer Disinfector (HTM 01-01)",                       "Washer Disinfector"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Endoscope Reprocessor (HTM 01-06)",                    "Endoscope Reprocessor"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Cytotoxic Hood (USP 800)",                             "Cytotoxic Hood"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Pharmacy Isolator (USP 797)",                          "Pharmacy Isolator"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Biosafety Cabinet (BS EN 12469)",                      "Biosafety Cabinet"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Lab Fume Hood (BS EN 14175)",                          "Lab Fume Hood"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "HBO Chamber (NFPA 99 §14)",                            "HBO Chamber"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "IVF Workstation (HFEA)",                               "IVF Workstation"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Patient Monitor (BS EN 60601-2-49)",                   "Patient Monitor"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Warming Cot (HBN 09-02)",                              "Warming Cot"),
            (BuiltInCategory.OST_MedicalEquipment,     "Generic Tag.rft",              "Medical Refrigerator (HTM 07-07)",                     "Medical Refrigerator"),
            // Nurse Call Devices (1)
            (BuiltInCategory.OST_NurseCallDevices,     "Nurse Call Device Tag.rft",    "Nurse Call (HTM 08-03)",                               "Nurse Call"),
        };

        /// <summary>
        /// Explicit creator-suffix → CSV family-name aliases for variant families
        /// whose CSV declaration uses a structurally different name than the
        /// short suffix the creator carries. Consumed by
        /// <see cref="GetTieInFamilyName"/> at name-generation time so tier-plan
        /// lookups against the CSV-derived <c>plansByFamily</c> dictionary hit.
        /// </summary>
        public static readonly Dictionary<string, string> VariantSuffixToCsvName =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Tie-in points — MEP CSV #46..#51 use the verbose "Tie-In Point Tag (…)" form
            { "Tie-In Pipe",            "STING - Tie-In Point Tag (Pipe — Plumbing & Hydraulic)" },
            { "Tie-In Duct",            "STING - Tie-In Point Tag (Duct — HVAC)" },
            { "Tie-In Conduit",         "STING - Tie-In Point Tag (Conduit — Electrical LV/ELV)" },
            { "Tie-In Cable Tray",      "STING - Tie-In Point Tag (Cable Tray — Electrical)" },
            { "Tie-In Fire Protection", "STING - Tie-In Point Tag (Fire Protection — Sprinkler / Suppression)" },
            { "Tie-In Gas",             "STING - Tie-In Point Tag (Gas — Medical / Industrial / Natural Gas)" },
            // STR CSV: "Brace / Truss" (with slashes + spaces) vs creator's flat "Brace Truss"
            { "Brace Truss",            "STING - Brace / Truss Tag" },
            // NOTE: Anti-Ligature is intentionally NOT mapped here. The HEALTH CSV
            // declares it once but binds 3 BICs (Doors / Lighting Fixtures /
            // Plumbing Fixtures); each .rfa must carry its own category binding so
            // the creator emits disambiguated names like
            // "STING - Anti-Ligature (Door) Tag". Plan lookup via
            // CsvFamilyNameCandidates strips the parenthetical at resolve time.
        };

        /// <summary>
        /// Per-BuiltInCategory override for the family-name segment used by
        /// <see cref="GetFamilyName"/>. CSVs ship singular family names
        /// ("STING - Door Tag", "STING - Wall Tag") while Revit's category
        /// display name is plural ("Doors", "Walls"). This map pins the
        /// creator's output to the CSV-aligned form so tier-plan lookups hit.
        /// Categories absent from this map fall back to
        /// <see cref="CategoryDisplayName"/> (typically the plural form).
        /// </summary>
        public static readonly Dictionary<BuiltInCategory, string> CategoryCsvFamilyKey =
            new Dictionary<BuiltInCategory, string>
        {
            // Enclosure
            { BuiltInCategory.OST_Doors, "Door" },
            { BuiltInCategory.OST_Windows, "Window" },
            { BuiltInCategory.OST_Walls, "Wall" },
            { BuiltInCategory.OST_Floors, "Floor" },
            { BuiltInCategory.OST_Ceilings, "Ceiling" },
            { BuiltInCategory.OST_Roofs, "Roof" },
            { BuiltInCategory.OST_Rooms, "Room" },
            { BuiltInCategory.OST_CurtainWallPanels, "Curtain Panel" },
            { BuiltInCategory.OST_CurtainWallMullions, "Curtain Wall Mullion" },
            // Circulation
            { BuiltInCategory.OST_Stairs, "Stair" },
            { BuiltInCategory.OST_Ramps, "Ramp" },
            { BuiltInCategory.OST_Railings, "Railing" },
            // Structure
            { BuiltInCategory.OST_StructuralColumns, "Structural Column" },
            { BuiltInCategory.OST_StructuralFoundation, "Structural Foundation" },
            { BuiltInCategory.OST_StructConnections, "Structural Connection" },
            // MEP — Mechanical / Plumbing / Electrical / Fire
            { BuiltInCategory.OST_DuctCurves, "Duct" },
            { BuiltInCategory.OST_DuctFitting, "Duct Fitting" },
            { BuiltInCategory.OST_DuctAccessory, "Duct Accessory" },
            { BuiltInCategory.OST_DuctTerminal, "Air Terminal" },
            { BuiltInCategory.OST_FlexDuctCurves, "Flex Duct" },
            { BuiltInCategory.OST_PipeCurves, "Pipe" },
            { BuiltInCategory.OST_PipeFitting, "Pipe Fitting" },
            { BuiltInCategory.OST_PipeAccessory, "Pipe Accessory" },
            { BuiltInCategory.OST_FlexPipeCurves, "Flex Pipe" },
            { BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixture" },
            { BuiltInCategory.OST_Sprinklers, "Sprinkler" },
            { BuiltInCategory.OST_FireAlarmDevices, "Fire Alarm Device" },
            { BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixture" },
            { BuiltInCategory.OST_LightingFixtures, "Lighting Fixture" },
            { BuiltInCategory.OST_LightingDevices, "Lighting Device" },
            { BuiltInCategory.OST_Conduit, "Conduit" },
            { BuiltInCategory.OST_ConduitFitting, "Conduit Fitting" },
            { BuiltInCategory.OST_CableTray, "Cable Tray" },
            { BuiltInCategory.OST_CableTrayFitting, "Cable Tray Fitting" },
            // Comms / LV
            { BuiltInCategory.OST_CommunicationDevices, "Communication Device" },
            { BuiltInCategory.OST_DataDevices, "Data Device" },
            { BuiltInCategory.OST_NurseCallDevices, "Nurse Call Device" },
            { BuiltInCategory.OST_SecurityDevices, "Security Device" },
            // Generic / Sheets
            { BuiltInCategory.OST_GenericModel, "Generic Model" },
            { BuiltInCategory.OST_Sheets, "Sheet Document" },
        };

        /// <summary>Total tag family count including standard categories + all variant arrays.</summary>
        public static int TotalFamilyCount =>
            CategoryTemplateMap.Count +
            TieInPointFamilies.Length +
            DisciplineSheetFamilies.Length +
            StructuralVariantFamilies.Length +
            MepVariantFamilies.Length +
            HealthcareVariantFamilies.Length;

        /// <summary>
        /// Asserts that every key in <c>LABEL_DEFINITIONS.json</c> <c>category_labels</c>
        /// is covered by a family this class produces, and vice versa. Returns
        /// (missingFromCreator, extraInCreator) lists. Both empty == aligned.
        /// Phase 187 drift-check method; called at startup by StingToolsApp.OnStartup.
        /// </summary>
        public static (List<string> missingFromCreator, List<string> extraInCreator) AuditAgainstLabelDefinitions(string dataDir)
        {
            var labelPath = System.IO.Path.Combine(dataDir, "LABEL_DEFINITIONS.json");
            if (!System.IO.File.Exists(labelPath))
                return (new List<string>(), new List<string>());

            var creatorNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var kv in CategoryTemplateMap)
                creatorNames.Add(GetFamilyName(kv.Key).Replace($"{FamilyPrefix} - ", "").Replace(" Tag", ""));
            foreach (var v in TieInPointFamilies)          creatorNames.Add(v.suffix);
            foreach (var v in DisciplineSheetFamilies)     creatorNames.Add(v.suffix);
            foreach (var v in StructuralVariantFamilies)   creatorNames.Add(v.suffix);
            foreach (var v in MepVariantFamilies)          creatorNames.Add(v.suffix);
            foreach (var v in HealthcareVariantFamilies)   creatorNames.Add(v.suffix);

            HashSet<string> labelKeys;
            try
            {
                var json = System.IO.File.ReadAllText(labelPath);
                var doc = Newtonsoft.Json.Linq.JObject.Parse(json);
                var cl = doc["category_labels"] as Newtonsoft.Json.Linq.JObject;
                labelKeys = new HashSet<string>(
                    cl != null ? cl.Properties().Select(p => p.Name) : System.Linq.Enumerable.Empty<string>(),
                    System.StringComparer.OrdinalIgnoreCase);
            }
            catch (System.Exception ex)
            {
                StingLog.Warn($"AuditAgainstLabelDefinitions: failed to parse {labelPath}: {ex.Message}");
                return (new List<string>(), new List<string>());
            }

            var missing = labelKeys.Where(k => !creatorNames.Contains(k)).OrderBy(s => s).ToList();
            var extra   = creatorNames.Where(k => !labelKeys.Contains(k)).OrderBy(s => s).ToList();
            return (missing, extra);
        }

        /// <summary>
        /// Generate the variant family name for a suffix. When the suffix
        /// appears in <see cref="VariantSuffixToCsvName"/>, the CSV-aligned
        /// full name wins so tier-plan lookups by family name hit. Otherwise
        /// falls back to the generic "{prefix} - {suffix} Tag" form.
        /// </summary>
        public static string GetTieInFamilyName(string suffix)
        {
            if (!string.IsNullOrEmpty(suffix)
                && VariantSuffixToCsvName.TryGetValue(suffix, out string csvName)
                && !string.IsNullOrEmpty(csvName))
            {
                return csvName;
            }
            return $"{FamilyPrefix} - {suffix} Tag";
        }

        /// <summary>Generate variant family filename from suffix.</summary>
        public static string GetTieInFamilyFileName(string suffix) => GetTieInFamilyName(suffix) + ".rfa";

        /// <summary>
        /// Resolve a creator-side family name to the CSV-side family name used
        /// in <c>plansByFamily</c>. Yields candidates in priority order:
        ///   1. Exact match → as-is (no alias needed).
        ///   2. Strip parenthetical disambiguator before " Tag":
        ///      "STING - Anti-Ligature (Door) Tag" → "STING - Anti-Ligature Tag".
        ///      Used by healthcare variants where one CSV name binds multiple
        ///      BICs and the creator emits per-BIC files.
        ///   3. Plural → singular fallback: "STING - Doors Tag" → "STING - Door Tag".
        ///      The CSV ships singular forms while Revit's category display is
        ///      plural, so this rule rescues every basic category whose
        ///      <see cref="CategoryCsvFamilyKey"/> override happens to be missing.
        /// </summary>
        public static IEnumerable<string> CsvFamilyNameCandidates(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) yield break;
            yield return familyName;

            const string suffix = " Tag";
            if (!familyName.EndsWith(suffix, StringComparison.Ordinal)) yield break;
            string stem = familyName.Substring(0, familyName.Length - suffix.Length);

            // Strip trailing "(...)" disambiguator: "Anti-Ligature (Door)" → "Anti-Ligature"
            if (stem.EndsWith(")", StringComparison.Ordinal))
            {
                int openParen = stem.LastIndexOf('(');
                if (openParen > 0)
                {
                    string trimmed = stem.Substring(0, openParen).TrimEnd();
                    if (!string.IsNullOrEmpty(trimmed))
                        yield return trimmed + suffix;
                }
            }

            // Plural → singular: drop final 's' before " Tag"
            if (stem.Length > 0 && stem[stem.Length - 1] == 's')
            {
                yield return stem.Substring(0, stem.Length - 1) + suffix;
            }
        }

        /// <summary>
        /// Alias-aware <c>plansByFamily</c> lookup: tries the exact name first,
        /// then plural→singular fallback. Returns the matching plan or null.
        /// </summary>
        public static TierPlan TryGetTierPlan(Dictionary<string, TierPlan> plansByFamily, string familyName)
        {
            if (plansByFamily == null || string.IsNullOrEmpty(familyName)) return null;
            foreach (var candidate in CsvFamilyNameCandidates(familyName))
            {
                if (plansByFamily.TryGetValue(candidate, out TierPlan plan) && plan != null)
                    return plan;
            }
            return null;
        }

        /// <summary>Alias-aware <c>plansByFamily</c> ContainsKey check.</summary>
        public static bool ContainsPlanForFamily(Dictionary<string, TierPlan> plansByFamily, string familyName)
        {
            if (plansByFamily == null || string.IsNullOrEmpty(familyName)) return false;
            foreach (var candidate in CsvFamilyNameCandidates(familyName))
            {
                if (plansByFamily.ContainsKey(candidate)) return true;
            }
            return false;
        }

        /// <summary>
        /// STING shared parameters to add to each tag family.
        /// These are the primary tag container parameters that the Label should display.
        /// </summary>
        public static readonly string[] TagParams = new[]
        {
            ParamRegistry.TAG1,  // Primary 8-segment tag (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ)
            ParamRegistry.TAG2,  // System tag short (SYS-FUNC-PROD-SEQ)
            ParamRegistry.TAG3,  // System tag extended
            ParamRegistry.TAG4,  // Short label (PROD-SEQ)
            ParamRegistry.TAG5,  // Description tag (PROD + family name)
            ParamRegistry.TAG6,  // Status tag (multi-line)
            ParamRegistry.TAG7,  // Rich narrative (full marked-up text)
            ParamRegistry.TAG7A, // Section A: Identity Header (Bold)
            ParamRegistry.TAG7B, // Section B: System & Function (Italic)
            ParamRegistry.TAG7C, // Section C: Spatial Context
            ParamRegistry.TAG7D, // Section D: Lifecycle & Status
            ParamRegistry.TAG7E, // Section E: Technical Specs (Bold)
            ParamRegistry.TAG7F, // Section F: Classification (Italic)
        };

        /// <summary>
        /// Visibility control parameters — added to every tag family so that
        /// calculated values in Edit Label can gate tier 1..10 and warning visibility.
        /// These are Type parameters (Yes/No) set by SetPresentationModeCommand and
        /// used by MigrateTagFamiliesCommand / TagStyleEngine.FindTypeVariant.
        /// Tiers 4..10 are required so the slider and SetParagraphDepth are not orphaned.
        /// </summary>
        public static readonly string[] VisibilityParams = new[]
        {
            ParamRegistry.PARA_STATE_1, ParamRegistry.PARA_STATE_2, ParamRegistry.PARA_STATE_3,
            ParamRegistry.PARA_STATE_4, ParamRegistry.PARA_STATE_5, ParamRegistry.PARA_STATE_6,
            ParamRegistry.PARA_STATE_7, ParamRegistry.PARA_STATE_8, ParamRegistry.PARA_STATE_9,
            ParamRegistry.PARA_STATE_10,
            ParamRegistry.WARN_VISIBLE,
        };

        /// <summary>
        /// Resolve the PerFamilyTierMap plan and return the subset of VisibilityParams that
        /// should be injected into this family. For every tier OMITted by the plan the
        /// corresponding TAG_PARA_STATE_N_BOOL is dropped so the Revit family does not carry
        /// an orphan visibility toggle (T1..T3 + WARN_VISIBLE are always kept).
        /// Falls back to the full VisibilityParams list when both familyName and category
        /// resolve to DefaultPlan (all Keep) — a no-op in that case.
        /// </summary>
        public static IEnumerable<string> VisibilityParamsFor(string familyName, string categoryDisplay)
        {
            var plan = PerFamilyTierMap.Resolve(familyName, categoryDisplay);
            foreach (string p in VisibilityParams)
            {
                if (p == ParamRegistry.PARA_STATE_4  && plan.T4  == TierState.Omit) continue;
                if (p == ParamRegistry.PARA_STATE_5  && plan.T5  == TierState.Omit) continue;
                if (p == ParamRegistry.PARA_STATE_6  && plan.T6  == TierState.Omit) continue;
                if (p == ParamRegistry.PARA_STATE_7  && plan.T7  == TierState.Omit) continue;
                if (p == ParamRegistry.PARA_STATE_8  && plan.T8  == TierState.Omit) continue;
                if (p == ParamRegistry.PARA_STATE_9  && plan.T9  == TierState.Omit) continue;
                if (p == ParamRegistry.PARA_STATE_10 && plan.T10 == TierState.Omit) continue;
                yield return p;
            }
        }

        /// <summary>
        /// Style/appearance parameters — all 128 TAG_{size}{style}_{colour}_BOOL variants plus
        /// box colour/visibility/style, leader colour, scale-tier-auto, and depth-tier cache.
        /// Added to every tag family by Create/Migrate so the Tag Style Engine can switch
        /// visible label rows and box/leader overrides per type.
        /// </summary>
        public static string[] StyleParams
        {
            get
            {
                var list = new List<string>();
                list.AddRange(ParamRegistry.AllTagStyleParams); // 128 variants
                list.Add(ParamRegistry.TAG_BOX_COLOR_R);
                list.Add(ParamRegistry.TAG_BOX_COLOR_G);
                list.Add(ParamRegistry.TAG_BOX_COLOR_B);
                list.Add(ParamRegistry.TAG_BOX_VISIBLE);
                list.Add(ParamRegistry.TAG_BOX_STYLE);
                list.Add(ParamRegistry.TAG_LEADER_COLOR_R);
                list.Add(ParamRegistry.TAG_LEADER_COLOR_G);
                list.Add(ParamRegistry.TAG_LEADER_COLOR_B);
                list.Add(ParamRegistry.TAG_SCALE_TIER_AUTO);
                list.Add(ParamRegistry.TAG_DEPTH_TIER);
                return list.ToArray();
            }
        }

        /// <summary>
        /// Get all parameters that should be added to a tag family for a specific
        /// category. Includes TagParams + VisibilityParams + category-specific
        /// paragraph container and tier 2/3 display parameters from LABEL_DEFINITIONS.json.
        /// When <paramref name="familyName"/> is supplied, the visibility params are
        /// filtered through PerFamilyTierMap so OMITted tiers do not carry an orphan
        /// TAG_PARA_STATE_N_BOOL.
        /// </summary>
        public static List<string> GetAllFamilyParams(string categoryDisplayName, string familyName = null)
        {
            var result = new List<string>();

            // Always add universal tag params
            result.AddRange(TagParams);

            // Always add visibility control params (PARA_STATE_1..10 + WARN_VISIBLE)
            // — filtered per TierPlan when family is known, otherwise full list.
            result.AddRange(VisibilityParamsFor(familyName, categoryDisplayName));

            // Always add style/appearance params (128 TAG_{size}{style}_{colour}_BOOL +
            // box colour/visible/style + leader colour + scale-tier-auto + depth-tier cache)
            foreach (string sp in StyleParams)
                if (!result.Contains(sp)) result.Add(sp);

            // Add description param
            result.Add("ASS_DESCRIPTION_TXT");

            // Add category-specific params from label definitions
            var labelParams = LabelDefinitionHelper.GetCategoryParams(categoryDisplayName);
            foreach (string p in labelParams)
            {
                if (!result.Contains(p))
                    result.Add(p);
            }

            return result;
        }

        /// <summary>
        /// Generate the STING family name for a category. Prefers the
        /// CSV-aligned singular form from <see cref="CategoryCsvFamilyKey"/>
        /// when one is defined so the family name matches what the v5 tag
        /// configuration CSVs declare ("STING - Door Tag", not
        /// "STING - Doors Tag"). Falls back to <see cref="CategoryDisplayName"/>
        /// then to the raw enum stem.
        /// </summary>
        public static string GetFamilyName(BuiltInCategory bic)
        {
            string catName;
            if (CategoryCsvFamilyKey.TryGetValue(bic, out string csvKey) && !string.IsNullOrEmpty(csvKey))
                catName = csvKey;
            else if (CategoryDisplayName.TryGetValue(bic, out string displayName))
                catName = displayName;
            else
                catName = bic.ToString().Replace("OST_", "");
            return $"{FamilyPrefix} - {catName} Tag";
        }

        /// <summary>Generate the .rfa filename for a category.</summary>
        public static string GetFamilyFileName(BuiltInCategory bic)
        {
            return GetFamilyName(bic) + ".rfa";
        }

        /// <summary>
        /// Locate the Revit annotation template directory.
        /// Searches common installation paths for the .rft files.
        /// </summary>
        public static string FindTemplateDirectory(Autodesk.Revit.ApplicationServices.Application app)
        {
            // Primary: Revit's reported family template path
            string basePath = app.FamilyTemplatePath;
            if (!string.IsNullOrEmpty(basePath))
            {
                // PRIORITY 1: Directories containing *Tag.rft files (annotation tag templates).
                // These are the specific templates needed for tag family creation.
                // Order matters: deeper annotation-specific paths come first.
                string[] tagDirs = new[]
                {
                    "English\\Annotations",             // Revit 2025+ typical (tag templates here)
                    "English_I\\Annotations",           // Imperial variant
                    "Metric\\Annotations",              // Metric
                    "English-Imperial\\Annotations",    // Nested Imperial
                    "Annotations",                      // Direct Annotations/ subfolder
                };

                foreach (string sub in tagDirs)
                {
                    string candidate = Path.Combine(basePath, sub);
                    if (Directory.Exists(candidate) &&
                        Directory.GetFiles(candidate, "*Tag.rft").Length > 0)
                        return candidate;
                }

                // PRIORITY 2: Recursive search for any directory containing *Tag.rft
                try
                {
                    foreach (string dir in Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories))
                    {
                        if (Directory.GetFiles(dir, "*Tag.rft").Length > 0)
                            return dir;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"permission issues: {ex.Message}"); }

                // PRIORITY 3: Directories containing any .rft (may have Generic Annotation.rft
                // which can be used as fallback for all categories).
                string[] generalDirs = new[]
                {
                    "English\\Annotations",
                    "English",
                    "English_I",
                    "Metric",
                    "Annotations",
                };

                foreach (string sub in generalDirs)
                {
                    string candidate = Path.Combine(basePath, sub);
                    if (Directory.Exists(candidate) &&
                        Directory.GetFiles(candidate, "*.rft").Length > 0)
                        return candidate;
                }

                // Try the base path itself
                if (Directory.GetFiles(basePath, "*.rft").Length > 0)
                    return basePath;
            }

            // Fallback: search common Revit installation paths
            string[] revitPaths = new[]
            {
                @"C:\ProgramData\Autodesk\RVT 2027\Family Templates",
                @"C:\ProgramData\Autodesk\RVT 2026\Family Templates",
                @"C:\ProgramData\Autodesk\RVT 2025\Family Templates",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Autodesk", "RVT 2027", "Family Templates"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Autodesk", "RVT 2026", "Family Templates"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Autodesk", "RVT 2025", "Family Templates"),
            };

            foreach (string rp in revitPaths)
            {
                if (!Directory.Exists(rp)) continue;
                try
                {
                    // First: look for directories with tag templates specifically
                    foreach (string dir in Directory.GetDirectories(rp, "*", SearchOption.AllDirectories))
                    {
                        if (Directory.GetFiles(dir, "*Tag.rft").Length > 0)
                            return dir;
                    }
                    // Then: any directory with .rft files
                    foreach (string dir in Directory.GetDirectories(rp, "*", SearchOption.AllDirectories))
                    {
                        if (Directory.GetFiles(dir, "*.rft").Length > 0)
                            return dir;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"permission issues: {ex.Message}"); }
            }

            return null;
        }

        /// <summary>
        /// Find the .rft template file for a given category.
        /// Tries the specific category template first, then falls back to Generic Tag.rft.
        /// Also handles locale variations (e.g., "Metric Generic Tag.rft").
        /// </summary>
        public static string FindTemplate(string templateDir, BuiltInCategory bic)
        {
            if (string.IsNullOrEmpty(templateDir)) return null;

            // Build list of directories to search (templateDir + parent + sibling Annotations/)
            var searchDirs = new List<string> { templateDir };
            string parentDir = Path.GetDirectoryName(templateDir);
            if (!string.IsNullOrEmpty(parentDir))
            {
                searchDirs.Add(parentDir);
                // Check sibling directories (e.g., if we're in English/, also check English/Annotations/)
                string annotSub = Path.Combine(templateDir, "Annotations");
                if (Directory.Exists(annotSub))
                    searchDirs.Insert(0, annotSub); // Higher priority
                // Check parent's Annotations/ subfolder
                annotSub = Path.Combine(parentDir, "Annotations");
                if (Directory.Exists(annotSub) && !searchDirs.Contains(annotSub))
                    searchDirs.Add(annotSub);
            }

            // Try specific category template in all search directories
            if (CategoryTemplateMap.TryGetValue(bic, out string templateName))
            {
                foreach (string dir in searchDirs)
                {
                    string specific = Path.Combine(dir, templateName);
                    if (File.Exists(specific)) return specific;

                    // Try with "Metric " prefix
                    string metric = Path.Combine(dir, "Metric " + templateName);
                    if (File.Exists(metric)) return metric;

                    // Try without spaces (some locales)
                    string noSpace = Path.Combine(dir, templateName.Replace(" ", ""));
                    if (File.Exists(noSpace)) return noSpace;
                }
            }

            // Fallback chain: Generic Tag → Multi-Category Tag → Generic Annotation
            string[] fallbacks = new[]
            {
                "Generic Tag.rft",
                "Metric Generic Tag.rft",
                "Multi-Category Tag.rft",
                "Metric Multi-Category Tag.rft",
                "Generic Annotation.rft",
                "Metric Generic Annotation.rft",
            };

            foreach (string fb in fallbacks)
            {
                foreach (string dir in searchDirs)
                {
                    string path = Path.Combine(dir, fb);
                    if (File.Exists(path)) return path;
                }
            }

            // Last resort: pick any .rft file in the template directory tree
            foreach (string dir in searchDirs)
            {
                string[] rftFiles = Directory.GetFiles(dir, "*Tag.rft");
                if (rftFiles.Length > 0) return rftFiles[0];
            }
            foreach (string dir in searchDirs)
            {
                string[] rftFiles = Directory.GetFiles(dir, "*.rft");
                if (rftFiles.Length > 0) return rftFiles[0];
            }

            return null;
        }

        /// <summary>
        /// Search for a pre-configured seed family (.rfa) with labels already bound.
        /// Seed families are the gold standard — they have Label → ASS_TAG_1_TXT
        /// already configured, so they work immediately without manual Family Editor steps.
        ///
        /// Search order:
        ///   1. Data/TagFamilies/Seeds/  (distributed seed files)
        ///   2. Data/TagFamilies/        (user-configured files from previous Configure Labels run)
        /// Seed files are identified by having a "_seed" suffix or being in the Seeds/ subdirectory.
        /// </summary>
        public static string FindSeedFamily(BuiltInCategory bic)
        {
            string baseName = GetFamilyFileName(bic);
            string nameNoExt = GetFamilyName(bic);
            string dataPath = StingToolsApp.DataPath;
            if (string.IsNullOrEmpty(dataPath)) return null;

            // Check Seeds/ subdirectory first (distributed with the plugin)
            string seedDir = Path.Combine(dataPath, "TagFamilies", "Seeds");
            if (Directory.Exists(seedDir))
            {
                string seedPath = Path.Combine(seedDir, baseName);
                if (File.Exists(seedPath)) return seedPath;

                // Also check for _seed suffix variant
                string seedSuffix = Path.Combine(seedDir, nameNoExt + "_seed.rfa");
                if (File.Exists(seedSuffix)) return seedSuffix;
            }

            return null;
        }

        /// <summary>
        /// Get the output directory for tag families.
        /// Creates a TagFamilies/ subdirectory alongside the plugin data.
        /// </summary>
        public static string GetOutputDirectory()
        {
            string baseDir = StingToolsApp.DataPath;
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.GetDirectoryName(StingToolsApp.AssemblyPath) ?? "";

            string tagFamilyDir = Path.Combine(baseDir, "TagFamilies");
            if (!Directory.Exists(tagFamilyDir))
                Directory.CreateDirectory(tagFamilyDir);

            return tagFamilyDir;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Create Tag Families — create all tag families from templates
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates STING tag families (.rfa) for every taggable category declared
    /// in the v5 tag configuration CSVs (count is
    /// <see cref="TagFamilyConfig.TotalFamilyCount"/>: 121 base + 8 tie-in
    /// point + 3 discipline sheet + 4 structural variant + N MEP variant +
    /// N healthcare variant).
    /// Each family is created from the appropriate Revit annotation template,
    /// configured with STING shared parameters, saved, and loaded into the project.
    ///
    /// The command:
    ///   1. Locates Revit's .rft annotation templates on disk
    ///   2. Skips categories that already have a STING tag loaded
    ///   3. Creates new family documents from templates
    ///   4. Adds ASS_TAG_1_TXT through ASS_TAG_6_TXT shared parameters
    ///   5. Saves .rfa files to Data/TagFamilies/
    ///   6. Loads families into the current project
    ///
    /// Post-creation: Open each family in Family Editor, add a Label pointing
    /// to ASS_TAG_1_TXT to complete the tag family configuration.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp = ParameterHelpers.GetApp(commandData);
            UIDocument uidoc = uiApp.ActiveUIDocument;
            Document doc = uidoc.Document;
            var app = uiApp.Application;

            // ── Dual-wire authoring: load every built-in mode's TierPlans so
            //    each family gets stamped with both Handover and Design &
            //    Construction T4-T10 rows in a single pass. Switching between
            //    the two patterns at runtime is then a project-level BOOL flip
            //    (HANDOVER_MODE_HANDOVER_BOOL / HANDOVER_MODE_DC_BOOL) instead
            //    of a family re-author. Modes whose CSVs are missing on disk
            //    are silently skipped — families keep whatever rows are live.
            Dictionary<string, Dictionary<string, TierPlan>> plansByMode =
                TagConfigPlanResolver.LoadAllPerMode(doc);
            Dictionary<string, TierPlan> plansByFamily = TagConfigPlanResolver.LoadAll(doc);
            bool preserveHandEdits = TagConfigPlanResolver.ReadPreserveHandEdits(doc);
            string activeMode = HandoverModeHelper.GetActiveMode(doc);

            // ── Pre-check: Auto-fix any numeric label params to TEXT ──
            var typeMismatches = LabelParamTypeValidator.ValidateSourceFile();
            if (typeMismatches.Count > 0)
            {
                int autoFixed = LabelParamTypeValidator.AutoFixSourceFile();
                if (autoFixed > 0)
                    StingLog.Info($"Auto-fixed {autoFixed} label params to TEXT before family creation");
            }

            // ── Step 1: Locate annotation templates ──
            string templateDir = TagFamilyConfig.FindTemplateDirectory(app);
            if (string.IsNullOrEmpty(templateDir))
            {
                TaskDialog.Show("Create Tag Families",
                    "Cannot find Revit annotation tag templates (.rft).\n\n" +
                    "Ensure Revit is installed with Family Templates.\n" +
                    $"Searched: {app.FamilyTemplatePath ?? "(null)"}");
                return Result.Failed;
            }

            // Log template directory and available .rft files for diagnostics
            string[] availableRft = Directory.GetFiles(templateDir, "*.rft");
            int tagRftCount = availableRft.Count(f => f.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0);
            StingLog.Info($"Tag family templates directory: {templateDir} " +
                $"({availableRft.Length} .rft files, {tagRftCount} tag templates)");

            // ── Step 2: Locate shared parameter file ──
            string sharedParamFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(sharedParamFile))
            {
                TaskDialog.Show("Create Tag Families",
                    "Cannot find MR_PARAMETERS.txt shared parameter file.\n" +
                    "Run 'Check Data' to verify data files are present.");
                return Result.Failed;
            }

            // ── Step 3: Check which STING tag families are already loaded ──
            var loadedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Family fam in new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>())
            {
                if (fam.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase))
                    loadedFamilies.Add(fam.Name);
            }

            // ── Step 4: Determine categories to process ──
            var categories = TagFamilyConfig.CategoryTemplateMap.Keys.ToList();
            int total = TagFamilyConfig.TotalFamilyCount;
            int alreadyLoaded = 0;
            int created = 0;
            int loaded = 0;
            int failed = 0;
            int templateMissing = 0;
            var report = new StringBuilder();
            var failures = new List<string>();

            // Pre-check: how many already loaded?
            foreach (var bic in categories)
            {
                string famName = TagFamilyConfig.GetFamilyName(bic);
                if (loadedFamilies.Contains(famName))
                    alreadyLoaded++;
            }
            // Also count tie-in point families
            foreach (var tiein in TagFamilyConfig.TieInPointFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(tiein.suffix);
                if (loadedFamilies.Contains(famName))
                    alreadyLoaded++;
            }
            // Also count discipline sheet families
            foreach (var ds in TagFamilyConfig.DisciplineSheetFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(ds.suffix);
                if (loadedFamilies.Contains(famName))
                    alreadyLoaded++;
            }
            // Also count structural variant families
            foreach (var sv in TagFamilyConfig.StructuralVariantFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(sv.suffix);
                if (loadedFamilies.Contains(famName))
                    alreadyLoaded++;
            }
            // Also count MEP variant families
            foreach (var mv in TagFamilyConfig.MepVariantFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(mv.suffix);
                if (loadedFamilies.Contains(famName))
                    alreadyLoaded++;
            }
            // Also count healthcare variant families
            foreach (var hv in TagFamilyConfig.HealthcareVariantFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(hv.suffix);
                if (loadedFamilies.Contains(famName))
                    alreadyLoaded++;
            }

            // Confirmation dialog
            int toCreate = total - alreadyLoaded;
            if (toCreate == 0)
            {
                TaskDialog.Show("Create Tag Families",
                    $"All {total} STING tag families are already loaded in this project.\n" +
                    "No new families to create.");
                return Result.Succeeded;
            }

            // Count how many .rfa files already exist on disk (built by a
            // previous run but not loaded into this project). These can be
            // skipped on incremental runs so we only build the genuinely new
            // families (e.g. when adding the 6 LPS variants).
            string outputDirEarly = TagFamilyConfig.GetOutputDirectory();
            int onDisk = 0;
            foreach (var bic in categories)
            {
                if (File.Exists(Path.Combine(outputDirEarly, TagFamilyConfig.GetFamilyFileName(bic))))
                    onDisk++;
            }
            foreach (var tiein in TagFamilyConfig.TieInPointFamilies)
            {
                if (File.Exists(Path.Combine(outputDirEarly, TagFamilyConfig.GetTieInFamilyFileName(tiein.suffix))))
                    onDisk++;
            }
            foreach (var ds in TagFamilyConfig.DisciplineSheetFamilies)
            {
                if (File.Exists(Path.Combine(outputDirEarly, TagFamilyConfig.GetTieInFamilyFileName(ds.suffix))))
                    onDisk++;
            }
            foreach (var sv in TagFamilyConfig.StructuralVariantFamilies)
            {
                if (File.Exists(Path.Combine(outputDirEarly, TagFamilyConfig.GetTieInFamilyFileName(sv.suffix))))
                    onDisk++;
            }
            foreach (var mv in TagFamilyConfig.MepVariantFamilies)
            {
                if (File.Exists(Path.Combine(outputDirEarly, TagFamilyConfig.GetTieInFamilyFileName(mv.suffix))))
                    onDisk++;
            }
            foreach (var hv in TagFamilyConfig.HealthcareVariantFamilies)
            {
                if (File.Exists(Path.Combine(outputDirEarly, TagFamilyConfig.GetTieInFamilyFileName(hv.suffix))))
                    onDisk++;
            }

            TaskDialog confirm = new TaskDialog("Create Tag Families");
            confirm.MainInstruction = $"Create {toCreate} STING tag families?";
            int familiesWithPlan = 0;
            foreach (var bic in categories)
            {
                string fn = TagFamilyConfig.GetFamilyName(bic);
                if (TagFamilyConfig.ContainsPlanForFamily(plansByFamily, fn)) familiesWithPlan++;
            }
            // Coverage warning: <50% of base categories have CSV plans usually
            // indicates a naming-convention drift between creator and CSVs
            // (e.g. plural/singular, suffix format). Surface it loudly so
            // silent default-tier authoring doesn't ship as-if normal.
            int coveragePct = categories.Count == 0 ? 100 : (int)Math.Round(familiesWithPlan * 100.0 / categories.Count);
            string coverageBanner = coveragePct < 50
                ? $"⚠ WARNING: only {coveragePct}% of base categories matched a CSV plan.\n" +
                  "  Most families will be authored with DEFAULT visibility params\n" +
                  "  instead of per-family T4-T10 rows. Likely cause: CSV family\n" +
                  "  name drift (plural/singular, suffix format). Check CategoryCsvFamilyKey\n" +
                  "  and VariantSuffixToCsvName in TagFamilyConfig.cs.\n\n"
                : string.Empty;
            confirm.MainContent =
                coverageBanner +
                $"Total taggable categories: {total}\n" +
                $"Already loaded in project: {alreadyLoaded}\n" +
                $"Already built on disk: {onDisk}\n" +
                $"To create: {toCreate}\n\n" +
                $"Mode: {activeMode}  •  Preserve hand-edits: {(preserveHandEdits ? "on" : "off")}\n" +
                $"Families with a CSV plan: {familiesWithPlan} (of {categories.Count} primary categories, {coveragePct}%)\n\n" +
                $"Templates: {templateDir}\n" +
                $"Tag .rft files found: {tagRftCount} of {availableRft.Length} total\n" +
                $"Output: {TagFamilyConfig.GetOutputDirectory()}\n\n" +
                "Each family will be created from a Revit annotation template,\n" +
                "loaded with STING shared parameters, and — when a plan is\n" +
                "available — have T4..T10 visibility formulas re-authored.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            var choice = confirm.Show();
            if (choice == TaskDialogResult.Cancel)
                return Result.Cancelled;
            bool skipExistingOnDisk = false; // default: recreate all families

            string outputDir = outputDirEarly;
            report.AppendLine($"STING Tag Family Creation Report");
            report.AppendLine(new string('=', 50));
            report.AppendLine($"Template directory: {templateDir}");
            report.AppendLine($"Output directory: {outputDir}");
            report.AppendLine();

            // ── Step 5: Create each tag family ──
            foreach (var bic in categories)
            {
                string famName = TagFamilyConfig.GetFamilyName(bic);
                string catDisplay = TagFamilyConfig.CategoryDisplayName.TryGetValue(bic, out string dn)
                    ? dn : bic.ToString();

                // Skip if already loaded
                if (loadedFamilies.Contains(famName))
                {
                    report.AppendLine($"  [SKIP] {catDisplay} — already loaded");
                    continue;
                }

                // Find template
                string templatePath = TagFamilyConfig.FindTemplate(templateDir, bic);
                if (string.IsNullOrEmpty(templatePath))
                {
                    report.AppendLine($"  [MISS] {catDisplay} — no template found");
                    templateMissing++;
                    failures.Add($"{catDisplay}: template not found");
                    continue;
                }

                string outputPath = Path.Combine(outputDir, TagFamilyConfig.GetFamilyFileName(bic));

                try
                {
                    // Strategy 3: Check for pre-configured seed files (with labels already bound)
                    // These are .rfa files manually configured via the Family Editor and
                    // placed in Data/TagFamilies/ for distribution. They take priority
                    // because they have labels already pointing to ASS_TAG_1_TXT.
                    string seedPath = TagFamilyConfig.FindSeedFamily(bic);
                    if (!string.IsNullOrEmpty(seedPath))
                    {
                        if (LoadFamilyIntoProject(doc, seedPath, famName))
                        {
                            report.AppendLine($"  [SEED] {catDisplay} — loaded pre-configured seed family");
                            loaded++;
                        }
                        else
                        {
                            report.AppendLine($"  [FAIL] {catDisplay} — seed load failed");
                            failed++;
                            failures.Add($"{catDisplay}: seed family load failed");
                        }
                        continue;
                    }

                    // Check if .rfa already exists on disk (from previous run)
                    // Verify it has parameters — if empty, delete and recreate
                    if (File.Exists(outputPath))
                    {
                        if (skipExistingOnDisk)
                        {
                            report.AppendLine($"  [SKIP] {catDisplay} — .rfa exists on disk, skipped (incremental run)");
                            continue;
                        }
                        bool hasParams = VerifyFamilyHasParams(app, outputPath);
                        if (hasParams)
                        {
                            if (LoadFamilyIntoProject(doc, outputPath, famName))
                            {
                                report.AppendLine($"  [LOAD] {catDisplay} — loaded from existing .rfa");
                                loaded++;
                            }
                            else
                            {
                                report.AppendLine($"  [FAIL] {catDisplay} — load failed");
                                failed++;
                                failures.Add($"{catDisplay}: family load failed");
                            }
                            continue;
                        }
                        else
                        {
                            // Empty family from previous failed run — delete and recreate
                            StingLog.Info($"Deleting empty .rfa for {catDisplay} — will recreate with params");
                            try { File.Delete(outputPath); }
                            catch (Exception delEx)
                            {
                                StingLog.Warn($"Cannot delete {outputPath}: {delEx.Message}");
                            }
                        }
                    }

                    // Create family from template (Strategy 1 + 2 fallback)
                    Document famDoc = app.NewFamilyDocument(templatePath);
                    if (famDoc == null)
                    {
                        report.AppendLine($"  [FAIL] {catDisplay} — NewFamilyDocument returned null");
                        failed++;
                        failures.Add($"{catDisplay}: cannot create from template");
                        continue;
                    }

                    // Add shared parameters: tag containers + visibility (filtered by TierPlan) + category-specific
                    var allParams = TagFamilyConfig.GetAllFamilyParams(catDisplay, famName);
                    bool paramsAdded = AddSharedParameters(famDoc, sharedParamFile, app, allParams);

                    // If no params were added, log detailed diagnostics
                    if (!paramsAdded)
                    {
                        StingLog.Warn($"No params added to {catDisplay}. " +
                            $"Param list count: {allParams.Count}, " +
                            $"SharedParamFile: {sharedParamFile}, " +
                            $"File exists: {File.Exists(sharedParamFile)}");
                    }

                    // Attempt to rebind the existing Label to ASS_TAG_1_TXT
                    bool labelBound = TryRebindLabel(famDoc);

                    // Wave-1 commit 3: if a TierPlan for this family is known,
                    // bind T4..T10 shared params + apply visibility formulas
                    // before saving. No-op when plansByFamily does not contain
                    // the family (e.g. a category not yet listed in the CSVs).
                    AuthorFromPlanIfAvailable(famDoc, famName, plansByMode, plansByFamily,
                        app, sharedParamFile, preserveHandEdits, report);

                    // Save the family document
                    SaveAsOptions saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(outputPath, saveOpts);
                    famDoc.Close(false);

                    created++;
                    string paramStatus = paramsAdded
                        ? (labelBound ? "with params + label" : "with params")
                        : "no params (manual add needed)";

                    // Load into project
                    if (LoadFamilyIntoProject(doc, outputPath, famName))
                    {
                        report.AppendLine($"  [OK]   {catDisplay} — created and loaded ({paramStatus})");
                        loaded++;
                    }
                    else
                    {
                        report.AppendLine($"  [PART] {catDisplay} — created but load failed ({paramStatus})");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{catDisplay}: {ex.Message}");
                    report.AppendLine($"  [FAIL] {catDisplay} — {ex.Message}");
                    StingLog.Error($"Tag family creation failed for {catDisplay}", ex);
                }
            }

            // ── Step 5b: Create tie-in point tag families ──
            report.AppendLine();
            report.AppendLine("── Tie-In Point Families ──");
            foreach (var tiein in TagFamilyConfig.TieInPointFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(tiein.suffix);
                string fileName = TagFamilyConfig.GetTieInFamilyFileName(tiein.suffix);

                if (loadedFamilies.Contains(famName))
                {
                    report.AppendLine($"  [SKIP] {tiein.display} — already loaded");
                    continue;
                }

                // Check for existing .rfa on disk
                string existingRfa = Path.Combine(outputDir, fileName);
                if (File.Exists(existingRfa))
                {
                    if (skipExistingOnDisk)
                    {
                        report.AppendLine($"  [SKIP] {tiein.display} — .rfa exists on disk, skipped (incremental run)");
                        continue;
                    }
                    try
                    {
                        using (Transaction t = new Transaction(doc, "STING Load Tie-In Tag"))
                        {
                            t.Start();
                            doc.LoadFamily(existingRfa);
                            t.Commit();
                        }
                        loaded++;
                        report.AppendLine($"  [LOAD] {tiein.display} — loaded from existing .rfa");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"  [FAIL] {tiein.display} — load failed: {ex.Message}");
                    }
                }

                // Find template and create family
                string tpl = null;
                foreach (string dir in new[] { templateDir })
                {
                    string specific = Path.Combine(dir, tiein.template);
                    if (File.Exists(specific)) { tpl = specific; break; }
                    string metric = Path.Combine(dir, "Metric " + tiein.template);
                    if (File.Exists(metric)) { tpl = metric; break; }
                }
                if (string.IsNullOrEmpty(tpl))
                {
                    // Fallback to Generic Tag.rft
                    string generic = Path.Combine(templateDir, "Generic Tag.rft");
                    if (File.Exists(generic)) tpl = generic;
                    else
                    {
                        string metricGeneric = Path.Combine(templateDir, "Metric Generic Tag.rft");
                        if (File.Exists(metricGeneric)) tpl = metricGeneric;
                    }
                }

                if (string.IsNullOrEmpty(tpl))
                {
                    templateMissing++;
                    report.AppendLine($"  [MISS] {tiein.display} — no template found");
                    continue;
                }

                try
                {
                    Document famDoc = app.NewFamilyDocument(tpl);
                    if (famDoc == null)
                    {
                        failed++;
                        report.AppendLine($"  [FAIL] {tiein.display} — NewFamilyDocument returned null");
                        continue;
                    }

                    // Add shared parameters using resilient helper (isolates OpenSharedParameterFile errors)
                    var tieInParams = TagFamilyConfig.TagParams
                        .Concat(TagFamilyConfig.VisibilityParamsFor(famName, tiein.display))
                        .Append("ASS_DESCRIPTION_TXT").ToList();
                    bool paramsAdded = AddSharedParameters(famDoc, sharedParamFile, app, tieInParams);

                    AuthorFromPlanIfAvailable(famDoc, famName, plansByMode, plansByFamily,
                        app, sharedParamFile, preserveHandEdits, report);

                    // Save and load — always proceeds even if params failed
                    string savePath = Path.Combine(outputDir, fileName);
                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(savePath, saveOpts);
                    famDoc.Close(false);
                    created++;

                    using (Transaction t = new Transaction(doc, "STING Load Tie-In Tag"))
                    {
                        t.Start();
                        doc.LoadFamily(savePath);
                        t.Commit();
                    }
                    loaded++;
                    string paramStatus = paramsAdded ? "with params" : "no params";
                    report.AppendLine($"  [OK]   {tiein.display} — created and loaded ({paramStatus})");
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{tiein.display}: {ex.Message}");
                    report.AppendLine($"  [FAIL] {tiein.display} — {ex.Message}");
                    StingLog.Error($"Tie-in tag family creation failed for {tiein.display}", ex);
                }
            }

            // ── Step 5c: Create discipline sheet tag families ──
            report.AppendLine();
            report.AppendLine("── Discipline Sheet Families ──");
            foreach (var ds in TagFamilyConfig.DisciplineSheetFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(ds.suffix);
                string fileName = TagFamilyConfig.GetTieInFamilyFileName(ds.suffix);

                if (loadedFamilies.Contains(famName))
                {
                    report.AppendLine($"  [SKIP] {ds.display} — already loaded");
                    continue;
                }

                // Check for existing .rfa on disk
                string existingRfa = Path.Combine(outputDir, fileName);
                if (File.Exists(existingRfa))
                {
                    if (skipExistingOnDisk)
                    {
                        report.AppendLine($"  [SKIP] {ds.display} — .rfa exists on disk, skipped (incremental run)");
                        continue;
                    }
                    try
                    {
                        using (Transaction t = new Transaction(doc, "STING Load Sheet Tag"))
                        {
                            t.Start();
                            doc.LoadFamily(existingRfa);
                            t.Commit();
                        }
                        loaded++;
                        report.AppendLine($"  [LOAD] {ds.display} — loaded from existing .rfa");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"  [FAIL] {ds.display} — load failed: {ex.Message}");
                    }
                }

                // Find template and create family
                string tpl = null;
                foreach (string dir in new[] { templateDir })
                {
                    string specific = Path.Combine(dir, ds.template);
                    if (File.Exists(specific)) { tpl = specific; break; }
                    string metric = Path.Combine(dir, "Metric " + ds.template);
                    if (File.Exists(metric)) { tpl = metric; break; }
                }
                if (string.IsNullOrEmpty(tpl))
                {
                    string generic = Path.Combine(templateDir, "Generic Tag.rft");
                    if (File.Exists(generic)) tpl = generic;
                    else
                    {
                        string metricGeneric = Path.Combine(templateDir, "Metric Generic Tag.rft");
                        if (File.Exists(metricGeneric)) tpl = metricGeneric;
                    }
                }

                if (string.IsNullOrEmpty(tpl))
                {
                    templateMissing++;
                    report.AppendLine($"  [MISS] {ds.display} — no template found");
                    continue;
                }

                try
                {
                    Document famDoc = app.NewFamilyDocument(tpl);
                    if (famDoc == null)
                    {
                        failed++;
                        report.AppendLine($"  [FAIL] {ds.display} — NewFamilyDocument returned null");
                        continue;
                    }

                    // Add shared parameters using resilient helper (isolates OpenSharedParameterFile errors)
                    var dsParams = TagFamilyConfig.TagParams
                        .Concat(TagFamilyConfig.VisibilityParamsFor(famName, ds.display))
                        .Append("ASS_DESCRIPTION_TXT").ToList();
                    bool paramsAdded = AddSharedParameters(famDoc, sharedParamFile, app, dsParams);

                    AuthorFromPlanIfAvailable(famDoc, famName, plansByMode, plansByFamily,
                        app, sharedParamFile, preserveHandEdits, report);

                    string savePath = Path.Combine(outputDir, fileName);
                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(savePath, saveOpts);
                    famDoc.Close(false);
                    created++;

                    using (Transaction t = new Transaction(doc, "STING Load Sheet Tag"))
                    {
                        t.Start();
                        doc.LoadFamily(savePath);
                        t.Commit();
                    }
                    loaded++;
                    string paramStatus = paramsAdded ? "with params" : "no params";
                    report.AppendLine($"  [OK]   {ds.display} — created and loaded ({paramStatus})");
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{ds.display}: {ex.Message}");
                    report.AppendLine($"  [FAIL] {ds.display} — {ex.Message}");
                    StingLog.Error($"Sheet tag family creation failed for {ds.display}", ex);
                }
            }

            // ── Step 5d: Create structural variant tag families ──
            report.AppendLine();
            report.AppendLine("── Structural Variant Families ──");
            foreach (var sv in TagFamilyConfig.StructuralVariantFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(sv.suffix);
                string fileName = TagFamilyConfig.GetTieInFamilyFileName(sv.suffix);

                if (loadedFamilies.Contains(famName))
                {
                    report.AppendLine($"  [SKIP] {sv.display} — already loaded");
                    continue;
                }

                // Check for existing .rfa on disk
                string existingRfa = Path.Combine(outputDir, fileName);
                if (File.Exists(existingRfa))
                {
                    if (skipExistingOnDisk)
                    {
                        report.AppendLine($"  [SKIP] {sv.display} — .rfa exists on disk, skipped (incremental run)");
                        continue;
                    }
                    try
                    {
                        using (Transaction t = new Transaction(doc, "STING Load Struct Variant Tag"))
                        {
                            t.Start();
                            doc.LoadFamily(existingRfa);
                            t.Commit();
                        }
                        loaded++;
                        report.AppendLine($"  [LOAD] {sv.display} — loaded from existing .rfa");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"  [FAIL] {sv.display} — load failed: {ex.Message}");
                    }
                }

                // Find template and create family
                string tpl = null;
                foreach (string dir in new[] { templateDir })
                {
                    string specific = Path.Combine(dir, sv.template);
                    if (File.Exists(specific)) { tpl = specific; break; }
                    string metric = Path.Combine(dir, "Metric " + sv.template);
                    if (File.Exists(metric)) { tpl = metric; break; }
                }
                if (string.IsNullOrEmpty(tpl))
                {
                    string generic = Path.Combine(templateDir, "Generic Tag.rft");
                    if (File.Exists(generic)) tpl = generic;
                    else
                    {
                        string metricGeneric = Path.Combine(templateDir, "Metric Generic Tag.rft");
                        if (File.Exists(metricGeneric)) tpl = metricGeneric;
                    }
                }

                if (string.IsNullOrEmpty(tpl))
                {
                    templateMissing++;
                    report.AppendLine($"  [MISS] {sv.display} — no template found");
                    continue;
                }

                try
                {
                    Document famDoc = app.NewFamilyDocument(tpl);
                    if (famDoc == null)
                    {
                        failed++;
                        report.AppendLine($"  [FAIL] {sv.display} — NewFamilyDocument returned null");
                        continue;
                    }

                    // Add shared parameters using resilient helper (isolates OpenSharedParameterFile errors)
                    var svParams = TagFamilyConfig.TagParams
                        .Concat(TagFamilyConfig.VisibilityParamsFor(famName, sv.display))
                        .Append("ASS_DESCRIPTION_TXT").ToList();
                    bool paramsAdded = AddSharedParameters(famDoc, sharedParamFile, app, svParams);

                    AuthorFromPlanIfAvailable(famDoc, famName, plansByMode, plansByFamily,
                        app, sharedParamFile, preserveHandEdits, report);

                    string savePath = Path.Combine(outputDir, fileName);
                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(savePath, saveOpts);
                    famDoc.Close(false);
                    created++;

                    using (Transaction t = new Transaction(doc, "STING Load Struct Variant Tag"))
                    {
                        t.Start();
                        doc.LoadFamily(savePath);
                        t.Commit();
                    }
                    loaded++;
                    string paramStatus = paramsAdded ? "with params" : "no params";
                    report.AppendLine($"  [OK]   {sv.display} — created and loaded ({paramStatus})");
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{sv.display}: {ex.Message}");
                    report.AppendLine($"  [FAIL] {sv.display} — {ex.Message}");
                    StingLog.Error($"Structural variant tag family creation failed for {sv.display}", ex);
                }
            }

            // ── Step 5e: Create MEP variant tag families ──
            report.AppendLine();
            report.AppendLine("── MEP Variant Families ──");
            foreach (var mv in TagFamilyConfig.MepVariantFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(mv.suffix);
                string fileName = TagFamilyConfig.GetTieInFamilyFileName(mv.suffix);

                if (loadedFamilies.Contains(famName))
                {
                    report.AppendLine($"  [SKIP] {mv.display} — already loaded");
                    continue;
                }

                // Check for existing .rfa on disk
                string existingRfa = Path.Combine(outputDir, fileName);
                if (File.Exists(existingRfa))
                {
                    if (skipExistingOnDisk)
                    {
                        report.AppendLine($"  [SKIP] {mv.display} — .rfa exists on disk, skipped (incremental run)");
                        continue;
                    }
                    try
                    {
                        using (Transaction t = new Transaction(doc, "STING Load MEP Variant Tag"))
                        {
                            t.Start();
                            doc.LoadFamily(existingRfa);
                            t.Commit();
                        }
                        loaded++;
                        report.AppendLine($"  [LOAD] {mv.display} — loaded from existing .rfa");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"  [FAIL] {mv.display} — load failed: {ex.Message}");
                    }
                }

                // Find template and create family
                string tpl = null;
                foreach (string dir in new[] { templateDir })
                {
                    string specific = Path.Combine(dir, mv.template);
                    if (File.Exists(specific)) { tpl = specific; break; }
                    string metric = Path.Combine(dir, "Metric " + mv.template);
                    if (File.Exists(metric)) { tpl = metric; break; }
                }
                if (string.IsNullOrEmpty(tpl))
                {
                    string generic = Path.Combine(templateDir, "Generic Tag.rft");
                    if (File.Exists(generic)) tpl = generic;
                    else
                    {
                        string metricGeneric = Path.Combine(templateDir, "Metric Generic Tag.rft");
                        if (File.Exists(metricGeneric)) tpl = metricGeneric;
                    }
                }

                if (string.IsNullOrEmpty(tpl))
                {
                    templateMissing++;
                    report.AppendLine($"  [MISS] {mv.display} — no template found");
                    continue;
                }

                try
                {
                    Document famDoc = app.NewFamilyDocument(tpl);
                    if (famDoc == null)
                    {
                        failed++;
                        report.AppendLine($"  [FAIL] {mv.display} — NewFamilyDocument returned null");
                        continue;
                    }

                    // Add shared parameters using resilient helper (isolates OpenSharedParameterFile errors)
                    var mvParams = TagFamilyConfig.TagParams
                        .Concat(TagFamilyConfig.VisibilityParamsFor(famName, mv.display))
                        .Append("ASS_DESCRIPTION_TXT").ToList();
                    bool paramsAdded = AddSharedParameters(famDoc, sharedParamFile, app, mvParams);

                    AuthorFromPlanIfAvailable(famDoc, famName, plansByMode, plansByFamily,
                        app, sharedParamFile, preserveHandEdits, report);

                    string savePath = Path.Combine(outputDir, fileName);
                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(savePath, saveOpts);
                    famDoc.Close(false);
                    created++;

                    using (Transaction t = new Transaction(doc, "STING Load MEP Variant Tag"))
                    {
                        t.Start();
                        doc.LoadFamily(savePath);
                        t.Commit();
                    }
                    loaded++;
                    string paramStatus = paramsAdded ? "with params" : "no params";
                    report.AppendLine($"  [OK]   {mv.display} — created and loaded ({paramStatus})");
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{mv.display}: {ex.Message}");
                    report.AppendLine($"  [FAIL] {mv.display} — {ex.Message}");
                    StingLog.Error($"MEP variant tag family creation failed for {mv.display}", ex);
                }
            }

            // ── Step 5f: Create healthcare variant tag families ──
            report.AppendLine();
            report.AppendLine("── Healthcare Variant Families ──");
            foreach (var hv in TagFamilyConfig.HealthcareVariantFamilies)
            {
                string famName = TagFamilyConfig.GetTieInFamilyName(hv.suffix);
                string fileName = TagFamilyConfig.GetTieInFamilyFileName(hv.suffix);

                if (loadedFamilies.Contains(famName))
                {
                    report.AppendLine($"  [SKIP] {hv.display} — already loaded");
                    continue;
                }

                // Check for existing .rfa on disk
                string existingRfa = Path.Combine(outputDir, fileName);
                if (File.Exists(existingRfa))
                {
                    if (skipExistingOnDisk)
                    {
                        report.AppendLine($"  [SKIP] {hv.display} — .rfa exists on disk, skipped (incremental run)");
                        continue;
                    }
                    try
                    {
                        using (Transaction t = new Transaction(doc, "STING Load Healthcare Tag"))
                        {
                            t.Start();
                            doc.LoadFamily(existingRfa);
                            t.Commit();
                        }
                        loaded++;
                        report.AppendLine($"  [LOAD] {hv.display} — loaded from existing .rfa");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"  [FAIL] {hv.display} — load failed: {ex.Message}");
                    }
                }

                // Find template and create family
                string tpl = null;
                foreach (string dir in new[] { templateDir })
                {
                    string specific = Path.Combine(dir, hv.template);
                    if (File.Exists(specific)) { tpl = specific; break; }
                    string metric = Path.Combine(dir, "Metric " + hv.template);
                    if (File.Exists(metric)) { tpl = metric; break; }
                }
                if (string.IsNullOrEmpty(tpl))
                {
                    string generic = Path.Combine(templateDir, "Generic Tag.rft");
                    if (File.Exists(generic)) tpl = generic;
                    else
                    {
                        string metricGeneric = Path.Combine(templateDir, "Metric Generic Tag.rft");
                        if (File.Exists(metricGeneric)) tpl = metricGeneric;
                    }
                }

                if (string.IsNullOrEmpty(tpl))
                {
                    templateMissing++;
                    report.AppendLine($"  [MISS] {hv.display} — no template found");
                    continue;
                }

                try
                {
                    Document famDoc = app.NewFamilyDocument(tpl);
                    if (famDoc == null)
                    {
                        failed++;
                        report.AppendLine($"  [FAIL] {hv.display} — NewFamilyDocument returned null");
                        continue;
                    }

                    // Add shared parameters using resilient helper (isolates OpenSharedParameterFile errors)
                    var hvParams = TagFamilyConfig.TagParams
                        .Concat(TagFamilyConfig.VisibilityParamsFor(famName, hv.display))
                        .Append("ASS_DESCRIPTION_TXT").ToList();
                    bool paramsAdded = AddSharedParameters(famDoc, sharedParamFile, app, hvParams);

                    AuthorFromPlanIfAvailable(famDoc, famName, plansByMode, plansByFamily,
                        app, sharedParamFile, preserveHandEdits, report);

                    string savePath = Path.Combine(outputDir, fileName);
                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(savePath, saveOpts);
                    famDoc.Close(false);
                    created++;

                    using (Transaction t = new Transaction(doc, "STING Load Healthcare Tag"))
                    {
                        t.Start();
                        doc.LoadFamily(savePath);
                        t.Commit();
                    }
                    loaded++;
                    string paramStatus = paramsAdded ? "with params" : "no params";
                    report.AppendLine($"  [OK]   {hv.display} — created and loaded ({paramStatus})");
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{hv.display}: {ex.Message}");
                    report.AppendLine($"  [FAIL] {hv.display} — {ex.Message}");
                    StingLog.Error($"Healthcare variant tag family creation failed for {hv.display}", ex);
                }
            }

            // ── Step 6: Report ──
            report.AppendLine();
            report.AppendLine(new string('-', 50));
            report.AppendLine($"Created:  {created}");
            report.AppendLine($"Loaded:   {loaded}");
            report.AppendLine($"Skipped:  {alreadyLoaded} (already loaded)");
            report.AppendLine($"Missing:  {templateMissing} (no template)");
            report.AppendLine($"Failed:   {failed}");

            if (created > 0)
            {
                report.AppendLine();
                report.AppendLine("NEXT STEP:");
                report.AppendLine("Run 'Configure Labels' to open each family in the");
                report.AppendLine("Family Editor and set the Label to ASS_TAG_1_TXT.");
                report.AppendLine("The wizard will guide you step by step.");
                report.AppendLine();
                report.AppendLine("TIP: After configuring, copy finished .rfa files to");
                report.AppendLine("Data/TagFamilies/Seeds/ to skip this step next time.");
            }

            TaskDialog td = new TaskDialog("Create Tag Families");
            td.MainInstruction = $"Created {created}, loaded {loaded} tag families";
            td.MainContent = report.ToString();
            if (failures.Count > 0)
            {
                td.ExpandedContent = "Failures:\n" + string.Join("\n", failures);
            }
            td.Show();

            StingLog.Info($"CreateTagFamilies: created={created}, loaded={loaded}, " +
                $"skipped={alreadyLoaded}, missing={templateMissing}, failed={failed}");

            return Result.Succeeded;
        }

        /// <summary>
        /// Per-family author hook. When <paramref name="plansByMode"/> has at
        /// least one mode that lists this family, stamps BOTH pattern row sets
        /// into the family via <see cref="FamilyLabelAuthor.AuthorLabelsMulti"/>
        /// so switching between Handover and Design & Construction at runtime
        /// is a selector-BOOL flip. Falls back to the single-plan path via
        /// <paramref name="plansByFamily"/> when the per-mode dict is empty
        /// (e.g. only the active-mode CSVs are on disk). No-op when no plan
        /// mentions the family.
        /// </summary>
        private void AuthorFromPlanIfAvailable(Document famDoc, string famName,
            Dictionary<string, Dictionary<string, TierPlan>> plansByMode,
            Dictionary<string, TierPlan> plansByFamily,
            Autodesk.Revit.ApplicationServices.Application app,
            string sharedParamFile, bool preserveHandEdits,
            StringBuilder report)
        {
            if (famDoc == null || string.IsNullOrEmpty(famName)) return;

            var modePlans = new List<FamilyLabelAuthor.ModePlan>();
            if (plansByMode != null)
            {
                foreach (var kv in plansByMode)
                {
                    if (kv.Value == null) continue;
                    TierPlan plan = TagFamilyConfig.TryGetTierPlan(kv.Value, famName);
                    if (plan == null) continue;
                    modePlans.Add(new FamilyLabelAuthor.ModePlan
                    {
                        Mode = kv.Key,
                        GateParam = HandoverModeHelper.GetSelectorBool(kv.Key),
                        Plan = plan,
                    });
                }
            }

            if (modePlans.Count == 0)
            {
                TierPlan plan = TagFamilyConfig.TryGetTierPlan(plansByFamily, famName);
                if (plan == null) return;
                modePlans.Add(new FamilyLabelAuthor.ModePlan
                {
                    Mode = "", GateParam = null, Plan = plan,
                });
            }

            try
            {
                var opts = new FamilyLabelAuthor.Options
                {
                    App = app,
                    SharedParamFile = sharedParamFile,
                    PreserveHandEdits = preserveHandEdits,
                    FamilyName = famName,
                };
                var r = FamilyLabelAuthor.AuthorLabelsMulti(famDoc, modePlans, opts);
                string modeTag = modePlans.Count > 1
                    ? $"modes=[{string.Join(",", modePlans.ConvertAll(m => m.Mode))}]"
                    : "";
                report.AppendLine($"         author → bound={r.ParamsBound} " +
                    $"formulas={r.FormulasApplied} skipped={r.FormulasSkipped} " +
                    $"preserved={r.TiersPreserved} label-rebound={r.LabelRebound} {modeTag}".TrimEnd());
                foreach (var w in r.Warnings) StingLog.Warn($"{famName}: {w}");
            }
            catch (Exception ex)
            {
                report.AppendLine($"         author → FAILED: {ex.Message}");
                StingLog.Error($"AuthorFromPlanIfAvailable({famName})", ex);
            }
        }

        /// <summary>
        /// Add STING shared parameters to a family document using FamilyManager.
        /// Opens the shared parameter file, finds each tag parameter by GUID,
        /// and adds it to the family as an instance parameter.
        /// </summary>
        private bool AddSharedParameters(Document famDoc,
            string sharedParamFile,
            Autodesk.Revit.ApplicationServices.Application app,
            List<string> paramNames = null)
        {
            // Always restore the shared parameter file, even on crash.
            // Failing to restore leaves Revit pointing at a wrong file
            // (e.g. ARCH_001_Casework_Tag_PARAMETERS.txt) for all future commands.
            string originalFile = app.SharedParametersFilename;
            try
            {
                app.SharedParametersFilename = sharedParamFile;

                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    StingLog.Warn($"Cannot open shared parameter file: {sharedParamFile}");
                    StingLog.Warn($"File exists: {File.Exists(sharedParamFile)}, " +
                        $"App.SharedParametersFilename: {app.SharedParametersFilename}");
                    return false;
                }

                // Log group count for diagnostics
                int groupCount = 0;
                int defCount = 0;
                foreach (DefinitionGroup grp in defFile.Groups)
                {
                    groupCount++;
                    foreach (Definition d in grp.Definitions)
                        defCount++;
                }
                StingLog.Info($"Shared parameter file opened: {groupCount} groups, {defCount} definitions");

                FamilyManager famMan = famDoc.FamilyManager;
                int added = 0;

                // Use provided param list or fallback to basic TagParams
                var paramsToAdd = paramNames ?? new List<string>(TagFamilyConfig.TagParams);

                using (Transaction tx = new Transaction(famDoc, "STING Add Tag Params"))
                {
                    tx.Start();

                    foreach (string paramName in paramsToAdd)
                    {
                        // Find the definition in the shared parameter file
                        ExternalDefinition extDef = FindSharedDefinition(defFile, paramName);
                        if (extDef == null)
                        {
                            StingLog.Warn($"Shared parameter '{paramName}' not found in file");
                            continue;
                        }

                        // Check if already added
                        bool exists = false;
                        foreach (FamilyParameter fp in famMan.Parameters)
                        {
                            if (fp.Definition.Name == paramName)
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (exists) continue;

                        try
                        {
                            famMan.AddParameter(
                                extDef,
                                GroupTypeId.General,
                                true); // isInstance = true (tags display instance values)
                            added++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Cannot add param '{paramName}' to family: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                StingLog.Info($"Added {added} shared parameters to tag family");
                return added > 0;
            }
            catch (Exception ex)
            {
                StingLog.Error("AddSharedParameters failed", ex);
                return false;
            }
            finally
            {
                // ALWAYS restore — prevents leaving Revit pointed at wrong SP file
                try
                {
                    if (!string.IsNullOrEmpty(originalFile))
                        app.SharedParametersFilename = originalFile;
                }
                catch (Exception ex2) { StingLog.Warn($"best effort: {ex2.Message}"); }
            }
        }

        /// <summary>
        /// Attempt to find the existing Label/TextNote in the tag family template
        /// and rebind it to ASS_TAG_1_TXT. This exploits the fact that .rft templates
        /// come with a pre-existing Label element pointing to a built-in parameter.
        ///
        /// Approach: Find all TextNote elements in the family document, locate the
        /// FamilyParameter for ASS_TAG_1_TXT, and attempt to associate them via
        /// the Dimension.FamilyLabel API (the only programmatic Label mechanism).
        ///
        /// Returns true if the label was successfully rebound, false if the API
        /// does not support this operation (expected for most Revit versions).
        /// </summary>
        private bool TryRebindLabel(Document famDoc)
        {
            try
            {
                FamilyManager famMan = famDoc.FamilyManager;

                // Find the ASS_TAG_1_TXT parameter we just added
                FamilyParameter tagParam = null;
                foreach (FamilyParameter fp in famMan.Parameters)
                {
                    if (fp.Definition.Name == ParamRegistry.TAG1)
                    {
                        tagParam = fp;
                        break;
                    }
                }
                if (tagParam == null) return false;

                // Find existing dimensions in the family that may have labels.
                // In tag .rft templates, the label text is typically implemented as
                // a Dimension with a FamilyLabel property.
                var dims = new FilteredElementCollector(famDoc)
                    .OfClass(typeof(Dimension))
                    .Cast<Dimension>()
                    .ToList();

                using (Transaction tx = new Transaction(famDoc, "STING Rebind Label"))
                {
                    tx.Start();

                    foreach (Dimension dim in dims)
                    {
                        try
                        {
                            // Attempt to set the FamilyLabel to our tag parameter.
                            // This works for dimension labels in families but may not
                            // work for annotation text labels (which is the Revit limitation).
                            if (dim.FamilyLabel != null || dim.FamilyLabel == null)
                            {
                                dim.FamilyLabel = tagParam;
                                StingLog.Info("Successfully rebound dimension label to ASS_TAG_1_TXT");
                                tx.Commit();
                                return true;
                            }
                        }
                        catch
                        {
                            // Expected: most dimensions in tag families don't support
                            // FamilyLabel assignment. Continue to next.
                        }
                    }

                    // Also try: find TextNote elements and check if they have
                    // any association mechanism (varies by Revit version)
                    var textNotes = new FilteredElementCollector(famDoc)
                        .OfClass(typeof(TextNote))
                        .Cast<TextNote>()
                        .ToList();

                    foreach (TextNote tn in textNotes)
                    {
                        try
                        {
                            // In some Revit versions, tag templates use TextNote with
                            // a special BuiltInParameter for label association.
                            // Try to set the text to indicate which parameter to display.
                            Parameter labelParam = tn.get_Parameter(
                                BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                            if (labelParam != null && !labelParam.IsReadOnly)
                            {
                                labelParam.Set(ParamRegistry.TAG1);
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Not supported — expected: {ex.Message}"); }
                    }

                    tx.Commit();
                }

                // If we get here, no programmatic rebind worked.
                // The label still shows the default parameter.
                return false;
            }
            catch (Exception ex)
            {
                StingLog.Info($"Label rebind attempt (expected to fail): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Verify that a .rfa file on disk contains STING shared parameters.
        /// Opens the family document, checks FamilyManager.Parameters count,
        /// and closes without saving. Returns false if the family is empty
        /// (from a previous failed creation run).
        /// </summary>
        private bool VerifyFamilyHasParams(
            Autodesk.Revit.ApplicationServices.Application app,
            string rfaPath)
        {
            try
            {
                Document famDoc = app.OpenDocumentFile(rfaPath);
                if (famDoc == null) return false;

                int paramCount = 0;
                FamilyManager famMan = famDoc.FamilyManager;
                foreach (FamilyParameter fp in famMan.Parameters)
                {
                    // Count STING params (ASS_, TAG_, HVC_, etc.) — not built-in
                    string name = fp.Definition?.Name ?? "";
                    if (name.Contains("_"))
                        paramCount++;
                }

                famDoc.Close(false);

                if (paramCount < 5)
                {
                    StingLog.Info($"VerifyFamilyHasParams: {rfaPath} has only {paramCount} " +
                        "STING params — treating as empty, will recreate");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VerifyFamilyHasParams failed for {rfaPath}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Search all groups in the shared parameter file for a definition by name.</summary>
        private ExternalDefinition FindSharedDefinition(DefinitionFile defFile, string paramName)
        {
            foreach (DefinitionGroup group in defFile.Groups)
            {
                foreach (Definition def in group.Definitions)
                {
                    if (def.Name == paramName && def is ExternalDefinition extDef)
                        return extDef;
                }
            }
            return null;
        }

        /// <summary>
        /// Load a family .rfa file into the project document.
        /// Uses the overwrite option to update existing families.
        /// </summary>
        private bool LoadFamilyIntoProject(Document doc, string familyPath, string expectedName)
        {
            try
            {
                using (Transaction tx = new Transaction(doc, $"STING Load Tag Family"))
                {
                    tx.Start();
                    bool loaded = doc.LoadFamily(familyPath, new TagFamilyLoadOptions(), out Family family);
                    tx.Commit();

                    if (loaded && family != null)
                    {
                        StingLog.Info($"Loaded tag family: {family.Name}");
                        return true;
                    }
                    else if (family != null)
                    {
                        StingLog.Info($"Tag family already loaded: {expectedName}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error($"Failed to load tag family '{expectedName}'", ex);
            }
            return false;
        }
    }

    /// <summary>
    /// Family load options that allow overwriting existing families with updated versions.
    /// </summary>
    internal class TagFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true; // Always load/overwrite
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
            out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Load Tag Families — load pre-existing .rfa files from disk
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads STING tag family .rfa files from the Data/TagFamilies/ directory
    /// into the current project. Use this after tag families have been created
    /// and their Labels configured in the Family Editor.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string tagFamilyDir = TagFamilyConfig.GetOutputDirectory();
            if (!Directory.Exists(tagFamilyDir))
            {
                TaskDialog.Show("Load Tag Families",
                    "Tag families directory not found.\n" +
                    "Run 'Create Tag Families' first to generate the .rfa files.");
                return Result.Failed;
            }

            string[] rfaFiles = Directory.GetFiles(tagFamilyDir, "STING - *.rfa");
            if (rfaFiles.Length == 0)
            {
                TaskDialog.Show("Load Tag Families",
                    $"No STING tag family .rfa files found in:\n{tagFamilyDir}\n\n" +
                    "Run 'Create Tag Families' first.");
                return Result.Failed;
            }

            // Check which are already loaded
            var loadedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Family fam in new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>())
            {
                loadedFamilies.Add(fam.Name);
            }

            int loaded = 0;
            int skipped = 0;
            int failed = 0;
            var report = new StringBuilder();

            // CRASH FIX: Single transaction for all families instead of one per .rfa file.
            // Rapid-fire tx.Commit() calls trigger Revit's deferred regeneration
            // which causes native segfaults (same root cause as ENH-003).
            using (Transaction tx = new Transaction(doc, "STING Load Tag Families"))
            {
                tx.Start();
                foreach (string rfaPath in rfaFiles.OrderBy(f => f))
                {
                    string famName = Path.GetFileNameWithoutExtension(rfaPath);

                    if (loadedFamilies.Contains(famName))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        bool success = doc.LoadFamily(rfaPath, new TagFamilyLoadOptions(), out Family fam);
                        if (success)
                        {
                            loaded++;
                            report.AppendLine($"  [OK] {famName}");
                        }
                        else
                        {
                            failed++;
                            report.AppendLine($"  [FAIL] {famName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        report.AppendLine($"  [FAIL] {famName} — {ex.Message}");
                        StingLog.Error($"Load tag family failed: {famName}", ex);
                    }
                }
                tx.Commit();
            }

            TaskDialog td = new TaskDialog("Load Tag Families");
            td.MainInstruction = $"Loaded {loaded} tag families";
            td.MainContent =
                $"Found: {rfaFiles.Length} .rfa files\n" +
                $"Loaded: {loaded}\n" +
                $"Skipped: {skipped} (already loaded)\n" +
                $"Failed: {failed}\n\n" +
                (report.Length > 0 ? report.ToString() : "");
            td.Show();

            StingLog.Info($"LoadTagFamilies: loaded={loaded}, skipped={skipped}, failed={failed}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Configure Tag Labels — guided wizard to set Labels in tag families
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Guided wizard that opens each STING tag family in the Family Editor
    /// for Label configuration. For each family, provides the EXACT Edit Label
    /// specification from LABEL_DEFINITIONS.json — parameters, prefixes, suffixes,
    /// calculated value formulas, and break settings.
    ///
    /// The user's ONLY manual step is:
    ///   1. Click the Label text element in the family
    ///   2. Click 'Edit Label'
    ///   3. Add the parameters listed (all text/formulas are provided)
    ///   4. Load into Project and Close
    ///
    /// Workflow: Run after CreateTagFamiliesCommand to complete tag family setup.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConfigureTagLabelsCommand : IExternalCommand
    {
        /// <summary>Parameters whose presence on the family's first FamilySymbol indicates the
        /// family has already been through ConfigureTagLabels / CreateTagFamilies — used to
        /// decide whether to skip it in merge-only mode.</summary>
        private static readonly string[] ConfiguredMarkerParams = new[]
        {
            ParamRegistry.TAG1,           // ASS_TAG_1_TXT — the primary 8-segment display target
            ParamRegistry.PARA_STATE_1,   // TAG_PARA_STATE_1_BOOL — tier visibility gate
            ParamRegistry.WARN_VISIBLE,   // TAG_WARN_VISIBLE_BOOL
        };

        /// <summary>True iff a loaded family carries the full STING marker-parameter set on
        /// its first FamilySymbol. Cheap — does NOT open the family in the Family Editor,
        /// just inspects one symbol via <see cref="Element.LookupParameter"/>.</summary>
        private static bool IsFamilyConfigured(Document doc, Family fam)
        {
            try
            {
                var symbolIds = fam.GetFamilySymbolIds();
                if (symbolIds == null || symbolIds.Count == 0) return false;
                var firstSym = doc.GetElement(symbolIds.First()) as FamilySymbol;
                if (firstSym == null) return false;
                foreach (string markerName in ConfiguredMarkerParams)
                {
                    if (firstSym.LookupParameter(markerName) == null) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"IsFamilyConfigured '{fam?.Name}': {ex.Message}");
                return false;
            }
        }

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp = ParameterHelpers.GetApp(commandData);
            Document doc = uiApp.ActiveUIDocument.Document;

            // Find all loaded STING tag families
            var stingFamilies = new List<Family>();
            foreach (Family fam in new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>())
            {
                if (fam.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase)
                    && fam.Name.Contains("Tag"))
                {
                    stingFamilies.Add(fam);
                }
            }

            if (stingFamilies.Count == 0)
            {
                TaskDialog.Show("STING Tools - Configure Tag Labels",
                    "No STING tag families loaded in this project.\n\n" +
                    "Run 'Create Tag Families' first to generate and load them.");
                return Result.Failed;
            }

            // Sort alphabetically for consistent order
            stingFamilies = stingFamilies.OrderBy(f => f.Name).ToList();

            // Merge-only pre-scan: split into already-configured vs. needs-attention by checking
            // marker params on the first FamilySymbol. Default behaviour is to skip the
            // already-configured bucket so re-runs only prompt for families that actually need
            // work. Force rewrite re-iterates everything and is an explicit opt-in.
            var alreadyConfigured = new List<Family>();
            var needsAttention = new List<Family>();
            foreach (var fam in stingFamilies)
            {
                if (IsFamilyConfigured(doc, fam)) alreadyConfigured.Add(fam);
                else                               needsAttention.Add(fam);
            }

            bool forceRewrite = false;
            if (alreadyConfigured.Count > 0 && needsAttention.Count > 0)
            {
                var modeDlg = new TaskDialog("STING — Configure Tag Labels");
                modeDlg.MainInstruction =
                    $"{alreadyConfigured.Count} of {stingFamilies.Count} tag families already have " +
                    "the STING parameter set — how should those be handled?";
                modeDlg.MainContent =
                    "Merge-only (recommended): skip the already-configured families and only " +
                    $"prompt for the {needsAttention.Count} that still need attention.\n\n" +
                    "Force rewrite: re-prompt for every family including ones already configured — " +
                    "use this when the tier spec itself changed and every family must be re-checked.";
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Merge-only — skip already-configured",
                    $"Prompt only for the {needsAttention.Count} family(ies) missing STING markers.");
                modeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Force rewrite — re-prompt every family",
                    $"Re-prompt for all {stingFamilies.Count} families.");
                modeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
                var res = modeDlg.Show();
                if (res == TaskDialogResult.Cancel) return Result.Cancelled;
                forceRewrite = (res == TaskDialogResult.CommandLink2);
            }
            else if (alreadyConfigured.Count == stingFamilies.Count)
            {
                // Every family appears configured — ask once whether the user wants to force.
                var allOk = new TaskDialog("STING — Configure Tag Labels");
                allOk.MainInstruction = "All loaded tag families already carry the STING markers.";
                allOk.MainContent =
                    "Nothing to merge — every family already has TAG1 / TAG_PARA_STATE_1 / " +
                    "TAG_WARN_VISIBLE on its first symbol.\n\n" +
                    "Use Force rewrite only if the tier spec itself has changed and every " +
                    "family must be re-verified.";
                allOk.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Close — nothing to do");
                allOk.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Force rewrite — re-prompt every family");
                allOk.CommonButtons = TaskDialogCommonButtons.Cancel;
                var res = allOk.Show();
                if (res != TaskDialogResult.CommandLink2) return Result.Succeeded;
                forceRewrite = true;
            }

            // Re-slice the working set per the chosen mode.
            stingFamilies = forceRewrite ? stingFamilies : needsAttention;
            if (stingFamilies.Count == 0)
            {
                TaskDialog.Show("STING — Configure Tag Labels",
                    "Nothing to do — every loaded tag family already carries the STING markers.");
                return Result.Succeeded;
            }

            // Load label definitions for exact Edit Label instructions
            var catLabels = LabelDefinitionHelper.LoadCategoryLabels();
            var paramText = LabelDefinitionHelper.LoadParameterText();
            bool hasLabelDefs = catLabels.Count > 0;

            // Validate label param types BEFORE configuration
            var typeMismatches = LabelParamTypeValidator.ValidateSourceFile();
            if (typeMismatches.Count > 0)
            {
                int autoFixed = LabelParamTypeValidator.AutoFixSourceFile();
                if (autoFixed > 0)
                {
                    StingLog.Info($"Auto-fixed {autoFixed} label params to TEXT in MR_PARAMETERS.txt");
                    TaskDialog.Show("STING Tools - Parameter Fix",
                        $"Fixed {autoFixed} parameters from numeric to TEXT type in MR_PARAMETERS.txt.\n\n" +
                        "IMPORTANT: Existing tag families (.rfa) still have the old parameter types.\n" +
                        "You must DELETE and RE-CREATE tag families for the fix to take effect:\n\n" +
                        "  1. Run 'Create Tag Families' (will overwrite existing .rfa files)\n" +
                        "  2. Then run 'Configure Tag Labels' again\n\n" +
                        "Parameters fixed:\n" +
                        string.Join(", ", typeMismatches.Take(10).Select(m => m.name)) +
                        (typeMismatches.Count > 10 ? $"\n... and {typeMismatches.Count - 10} more" : ""));
                }
            }

            // Introduction dialog
            TaskDialog intro = new TaskDialog("Configure Tag Labels");
            intro.MainInstruction = $"Configure Labels for {stingFamilies.Count} STING tag families";
            intro.MainContent =
                "This wizard opens each tag family in the Family Editor and\n" +
                "provides the EXACT Edit Label configuration.\n\n" +
                "For each family:\n" +
                "  1. Click the Label text element (shows 'Type Mark')\n" +
                "  2. Click 'Edit Label' in Properties panel\n" +
                "  3. Remove the default parameter\n" +
                "  4. Add the parameters shown (exact text provided)\n" +
                "  5. Set Prefix/Suffix/Spaces/Break as listed\n" +
                "  6. Load into Project and Close\n\n" +
                (hasLabelDefs
                    ? $"Label definitions loaded: {catLabels.Count} categories from LABEL_DEFINITIONS.json\n"
                    : "WARNING: LABEL_DEFINITIONS.json not found — basic instructions only.\n") +
                "All parameters have already been added to the families.";
            intro.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (intro.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            int configured = 0;
            int skipped = 0;
            int remaining = stingFamilies.Count;

            foreach (Family fam in stingFamilies)
            {
                remaining--;

                // Extract category name from family name: "STING - Walls Tag" → "Walls"
                string catName = ExtractCategoryName(fam.Name);

                // Build the Edit Label specification for this category
                string labelSpec = BuildLabelSpec(catName, catLabels, paramText);

                // Show instructions for this family
                TaskDialog step = new TaskDialog("Configure Tag Label");
                step.MainInstruction = $"[{configured + skipped + 1}/{stingFamilies.Count}] {fam.Name}";
                step.MainContent =
                    $"({remaining} remaining after this)\n\n" +
                    "Click 'Open' to edit this family, or 'Skip' to move on.";
                step.ExpandedContent = labelSpec;
                step.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Open in Family Editor",
                    "Opens this tag family — the Edit Label spec will show after opening");
                step.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Skip this family",
                    "Move to the next tag family");
                step.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Stop — done for now",
                    $"Exit wizard ({configured} configured so far)");
                step.CommonButtons = TaskDialogCommonButtons.Cancel;

                TaskDialogResult stepResult = step.Show();

                if (stepResult == TaskDialogResult.CommandLink3 ||
                    stepResult == TaskDialogResult.Cancel)
                {
                    break;
                }

                if (stepResult == TaskDialogResult.CommandLink2)
                {
                    skipped++;
                    continue;
                }

                // Open the family in the Family Editor
                try
                {
                    Document famDoc = doc.EditFamily(fam);
                    if (famDoc != null)
                    {
                        // Family is now open in the editor.
                        // Show the FULL Edit Label spec as the reminder
                        TaskDialog reminder = new TaskDialog("Edit Label Specification");
                        reminder.MainInstruction = $"Now editing: {fam.Name}";
                        reminder.MainContent =
                            "1. Click the Label text element in the family view\n" +
                            "2. In Properties panel → click 'Edit Label'\n" +
                            "3. Remove the existing parameter (select → Remove)\n" +
                            "4. Add parameters below in order (use Add →)\n" +
                            "5. Set Prefix, Suffix, Spaces, Break as listed\n" +
                            "6. Check 'Wrap between parameters only'\n" +
                            "7. Click OK → Load into Project and Close";
                        reminder.ExpandedContent = labelSpec;
                        reminder.CommonButtons = TaskDialogCommonButtons.Ok;
                        reminder.Show();

                        configured++;
                    }
                    else
                    {
                        StingLog.Warn($"EditFamily returned null for {fam.Name}");
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Error($"Failed to open family: {fam.Name}", ex);
                    TaskDialog.Show("Error",
                        $"Could not open {fam.Name}:\n{ex.Message}");
                    skipped++;
                }
            }

            // Final summary
            TaskDialog summary = new TaskDialog("Configure Tag Labels");
            summary.MainInstruction = $"Label configuration complete";
            summary.MainContent =
                $"Families opened for editing: {configured}\n" +
                $"Skipped: {skipped}\n" +
                $"Total STING tag families: {stingFamilies.Count}\n\n" +
                (configured < stingFamilies.Count
                    ? "Run this command again to configure remaining families."
                    : "All tag families have been opened for configuration.\n\n" +
                      "TIP: Copy finished .rfa files from Data/TagFamilies/ to\n" +
                      "Data/TagFamilies/Seeds/ so they auto-load next time.");
            summary.Show();

            StingLog.Info($"ConfigureTagLabels: configured={configured}, skipped={skipped}");
            return Result.Succeeded;
        }

        /// <summary>
        /// Extract category display name from STING family name.
        /// "STING - Mechanical Equipment Tag" → "Mechanical Equipment"
        /// "STING - Walls Tag" → "Walls"
        /// </summary>
        private string ExtractCategoryName(string familyName)
        {
            string name = familyName;
            if (name.StartsWith("STING - ", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(8);
            if (name.EndsWith(" Tag", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Trim();
        }

        /// <summary>
        /// Build the exact Edit Label specification for a category from LABEL_DEFINITIONS.json.
        /// Produces a formatted table showing Parameter | Prefix | Suffix | Spaces | Break
        /// for each tier, with calculated value formulas for tier 2/3 gating.
        /// </summary>
        private string BuildLabelSpec(string catName,
            Dictionary<string, JObject> catLabels,
            Dictionary<string, JObject> paramText)
        {
            var sb = new StringBuilder();

            // Try to find label definition for this category
            JObject catDef = null;
            if (catLabels.TryGetValue(catName, out catDef))
            {
                sb.AppendLine($"EDIT LABEL SPECIFICATION: {catName}");
                sb.AppendLine(new string('─', 60));
                sb.AppendLine();

                // Tier 1 — Always visible
                sb.AppendLine("── TIER 1 (Always Visible) ──");
                sb.AppendLine("Add these parameters directly:");
                sb.AppendLine();
                FormatTierTable(sb, catDef["tier_1"] as JArray, paramText);

                // Tier 2 — Gated by TAG_PARA_STATE_2_BOOL
                var tier2 = catDef["tier_2"] as JArray;
                if (tier2 != null && tier2.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("── TIER 2 (Standard+) ──");
                    sb.AppendLine("Use CALCULATED VALUES (Type: Text) for each parameter:");
                    sb.AppendLine("  Formula: if(TAG_PARA_STATE_2_BOOL, <param>, \"\")");
                    sb.AppendLine("  NOTE: All parameters MUST be TEXT type in MR_PARAMETERS.txt.");
                    sb.AppendLine("  Numeric params (NUMBER/LENGTH/AREA) cause 'Inconsistent Units'.");
                    sb.AppendLine();
                    FormatTierTable(sb, tier2, paramText);
                }

                // Tier 3 — Gated by TAG_PARA_STATE_3_BOOL
                var tier3 = catDef["tier_3"] as JArray;
                if (tier3 != null && tier3.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("── TIER 3 (Comprehensive) ──");
                    sb.AppendLine("Use CALCULATED VALUES (Type: Text) for each parameter:");
                    sb.AppendLine("  Formula: if(TAG_PARA_STATE_3_BOOL, <param>, \"\")");
                    sb.AppendLine("  NOTE: All parameters MUST be TEXT type in MR_PARAMETERS.txt.");
                    sb.AppendLine();
                    FormatTierTable(sb, tier3, paramText);
                }

                // Tiers 4..10 — shared commissioning / cost / carbon / fabrication /
                // clash / as-built / compliance rows (v5.3 schema). Gated by matching
                // TAG_PARA_STATE_N_BOOL type parameter so SetParagraphDepthCommand can
                // progressively widen visible tiers.
                string[] extLabels = new[]
                {
                    "TIER 4 (Commissioning & handover)",
                    "TIER 5 (Cost & procurement)",
                    "TIER 6 (Carbon & sustainability — BS EN 15978)",
                    "TIER 7 (Fabrication & QC — ISO 6412)",
                    "TIER 8 (Clash & coordination)",
                    "TIER 9 (As-built & health)",
                    "TIER 10 (Compliance / audit trail)",
                };
                for (int i = 0; i < 7; i++)
                {
                    int t = i + 4;
                    var arr = catDef[$"tier_{t}"] as JArray;
                    if (arr == null || arr.Count == 0) continue;
                    sb.AppendLine();
                    sb.AppendLine($"── {extLabels[i]} ──");
                    sb.AppendLine("Use CALCULATED VALUES (Type: Text) for each parameter:");
                    sb.AppendLine($"  Formula: if(TAG_PARA_STATE_{t}_BOOL, <param>, \"\")");
                    sb.AppendLine();
                    FormatTierTable(sb, arr, paramText);
                }

                // Paragraph container
                string paraCont = catDef["paragraph_container"]?.ToString();
                if (!string.IsNullOrEmpty(paraCont) && paraCont != "null")
                {
                    sb.AppendLine();
                    sb.AppendLine($"── PARAGRAPH CONTAINER ──");
                    sb.AppendLine($"  Add: {paraCont}");
                    sb.AppendLine($"  Formula: if(TAG_PARA_STATE_3_BOOL, {paraCont}, \"\")");
                    sb.AppendLine($"  Break: YES (new line)");
                }

                // TAG7 — Rich Descriptive Narrative (6 sub-sections A-F)
                sb.AppendLine();
                sb.AppendLine("── TAG7 NARRATIVE (Rich Description — Auto-Generated) ──");
                sb.AppendLine("TAG7 is auto-populated by Tag & Combine / Full Auto-Populate.");
                sb.AppendLine("Add these TAG7 sub-section parameters to the label for");
                sb.AppendLine("multi-line rich display (each gated by visibility booleans):");
                sb.AppendLine();
                sb.AppendLine("  Parameter                    | Content           | Style    | Brk");
                sb.AppendLine("  " + new string('─', 72));
                sb.AppendLine("  ASS_TAG_7A_TXT               | Identity Header   | Bold     | YES");
                sb.AppendLine("  ASS_TAG_7B_TXT               | System & Function | Italic   | YES");
                sb.AppendLine("  ASS_TAG_7C_TXT               | Spatial Context   | Normal   | YES");
                sb.AppendLine("  ASS_TAG_7D_TXT               | Lifecycle/Status  | Normal   | YES");
                sb.AppendLine("  ASS_TAG_7E_TXT               | Technical Specs   | Bold     | YES");
                sb.AppendLine("  ASS_TAG_7F_TXT               | Classification    | Italic   | YES");
                sb.AppendLine();
                sb.AppendLine("  For all TAG7 rows, use Calculated Values:");
                sb.AppendLine("    if(TAG_PARA_STATE_3_BOOL, ASS_TAG_7x_TXT, \"\")");
                sb.AppendLine("  This makes TAG7 visible only in Full Specification mode.");

                // Paragraph template (if defined)
                string paraTpl = catDef["paragraph_template"]?.ToString();
                if (!string.IsNullOrEmpty(paraTpl))
                {
                    sb.AppendLine();
                    sb.AppendLine("  Paragraph template (auto-generated text):");
                    // Wrap at ~70 chars for readability
                    string wrapped = paraTpl.Length > 140
                        ? "  " + paraTpl.Substring(0, 140) + "..."
                        : "  " + paraTpl;
                    sb.AppendLine(wrapped);
                }

                sb.AppendLine();
                sb.AppendLine("Settings: Check 'Wrap between parameters only'");
            }
            else
            {
                // No label definition — provide basic instructions
                sb.AppendLine($"EDIT LABEL: {catName}");
                sb.AppendLine(new string('─', 60));
                sb.AppendLine();
                sb.AppendLine("No detailed spec in LABEL_DEFINITIONS.json.");
                sb.AppendLine("Use this standard configuration:");
                sb.AppendLine();
                sb.AppendLine("── TIER 1 (Always Visible) ──");
                sb.AppendLine("  ASS_TAG_1_TXT                | (no prefix/suffix) | Break=YES");
                sb.AppendLine("  ASS_DESCRIPTION_TXT          | (no prefix/suffix) | Break=YES");
                sb.AppendLine();
                sb.AppendLine("── TAG7 (Full Specification mode only) ──");
                sb.AppendLine("  Use Calculated Values: if(TAG_PARA_STATE_3_BOOL, <param>, \"\")");
                sb.AppendLine("  ASS_TAG_7A_TXT  (Identity)   | Break=YES");
                sb.AppendLine("  ASS_TAG_7B_TXT  (System)     | Break=YES");
                sb.AppendLine("  ASS_TAG_7C_TXT  (Spatial)    | Break=YES");
                sb.AppendLine("  ASS_TAG_7D_TXT  (Lifecycle)  | Break=YES");
                sb.AppendLine("  ASS_TAG_7E_TXT  (Technical)  | Break=YES");
                sb.AppendLine("  ASS_TAG_7F_TXT  (Class.)     | Break=YES");
                sb.AppendLine();
                sb.AppendLine("Settings: Check 'Wrap between parameters only'");
            }

            return sb.ToString();
        }

        /// <summary>Format a tier's parameters as a table for the Edit Label dialog.</summary>
        private void FormatTierTable(StringBuilder sb,
            JArray tierParams,
            Dictionary<string, JObject> paramText)
        {
            if (tierParams == null || tierParams.Count == 0)
            {
                sb.AppendLine("  (none)");
                return;
            }

            // Header includes Style/Color/Size columns when any row in this tier carries them
            // (v5.3 schema — tier_4..tier_10 ship with style/color/size; tier_1..3 inherit defaults).
            bool anyStyle = false;
            foreach (JObject e in tierParams)
            {
                if (e["style"] != null || e["color"] != null || e["size"] != null)
                { anyStyle = true; break; }
            }

            if (anyStyle)
            {
                sb.AppendLine("  Parameter                    | Prefix         | Suffix         | Spc | Brk | Style  | Color  | Size");
                sb.AppendLine("  " + new string('─', 108));
            }
            else
            {
                sb.AppendLine("  Parameter                    | Prefix         | Suffix         | Spc | Brk");
                sb.AppendLine("  " + new string('─', 80));
            }

            foreach (JObject entry in tierParams)
            {
                string param = entry["param"]?.ToString() ?? "";
                int spaces = entry["spaces"]?.Value<int>() ?? 0;
                bool brk = entry["break"]?.Value<bool>() ?? false;

                // Get prefix/suffix: override from tier entry, else from global paramText
                string prefix = entry["prefix_override"]?.ToString();
                string suffix = entry["suffix_override"]?.ToString();

                if (prefix == null && paramText.TryGetValue(param, out var ptDef))
                    prefix = ptDef["prefix"]?.ToString() ?? "";
                if (suffix == null && paramText.TryGetValue(param, out var ptDef2))
                    suffix = ptDef2["suffix"]?.ToString() ?? "";

                prefix = prefix ?? "";
                suffix = suffix ?? "";

                // Truncate long param names for display
                string paramShort = param.Length > 28 ? param.Substring(0, 25) + "..." : param;
                string prefixDisp = prefix.Length > 0 ? $"\"{prefix}\"" : "";
                string suffixDisp = suffix.Length > 0 ? $"\"{suffix}\"" : "";

                if (anyStyle)
                {
                    string style = entry["style"]?.ToString() ?? "";
                    string color = entry["color"]?.ToString() ?? "";
                    string size = entry["size"]?.ToString() ?? "";
                    sb.AppendLine($"  {paramShort,-30} | {prefixDisp,-14} | {suffixDisp,-14} | {spaces,-3} | {(brk ? "YES" : ""),-3} | {style,-6} | {color,-6} | {size}");
                }
                else
                {
                    sb.AppendLine($"  {paramShort,-30} | {prefixDisp,-14} | {suffixDisp,-14} | {spaces,-3} | {(brk ? "YES" : "")}");
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Audit Tag Families — check which categories have tag families loaded
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Audits tag family coverage: reports which of the 50 taggable categories
    /// have STING tag families loaded and which are missing.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AuditTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Collect all loaded families
            var loadedFamilies = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase);
            foreach (Family fam in new FilteredElementCollector(doc)
                .OfClass(typeof(Family)).Cast<Family>())
            {
                loadedFamilies[fam.Name] = fam;
            }

            // Also collect all annotation family symbols for the FindTagType approach
            var annotationTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                    fs.Category.CategoryType == CategoryType.Annotation)
                .ToList();

            var report = new StringBuilder();
            report.AppendLine("STING Tag Family Audit");
            report.AppendLine(new string('=', 50));

            int stingLoaded = 0;
            int otherTag = 0;
            int missing = 0;

            foreach (var bic in TagFamilyConfig.CategoryTemplateMap.Keys)
            {
                string famName = TagFamilyConfig.GetFamilyName(bic);
                string catDisplay = TagFamilyConfig.CategoryDisplayName.TryGetValue(bic, out string dn)
                    ? dn : bic.ToString();

                if (loadedFamilies.ContainsKey(famName))
                {
                    report.AppendLine($"  [STING] {catDisplay}");
                    stingLoaded++;
                }
                else
                {
                    // Check if ANY tag type exists for this category
                    Category cat = doc.Settings.Categories.get_Item(bic);
                    FamilySymbol anyTag = (cat != null)
                        ? TagPlacementEngine.FindTagType(doc, cat)
                        : null;

                    if (anyTag != null)
                    {
                        report.AppendLine($"  [OTHER] {catDisplay} — using '{anyTag.Family.Name}'");
                        otherTag++;
                    }
                    else
                    {
                        report.AppendLine($"  [NONE]  {catDisplay} — NO tag family loaded");
                        missing++;
                    }
                }
            }

            report.AppendLine();
            report.AppendLine(new string('-', 50));
            report.AppendLine($"STING tags: {stingLoaded}");
            report.AppendLine($"Other tags: {otherTag}");
            report.AppendLine($"Missing:    {missing}");
            report.AppendLine($"Coverage:   {(stingLoaded + otherTag) * 100 / TagFamilyConfig.CategoryTemplateMap.Count}%");

            // Check for .rfa files on disk
            string outputDir = TagFamilyConfig.GetOutputDirectory();
            int onDisk = Directory.Exists(outputDir)
                ? Directory.GetFiles(outputDir, "STING - *.rfa").Length
                : 0;
            if (onDisk > 0 && stingLoaded < onDisk)
            {
                report.AppendLine();
                report.AppendLine($"NOTE: {onDisk} .rfa files exist on disk but only {stingLoaded} loaded.");
                report.AppendLine("Run 'Load Tag Families' to load them.");
            }

            // Check label param types
            var typeMismatches = LabelParamTypeValidator.ValidateSourceFile();
            if (typeMismatches.Count > 0)
            {
                report.AppendLine();
                report.AppendLine($"⚠ WARNING: {typeMismatches.Count} label params have non-TEXT types");
                report.AppendLine("These cause 'Inconsistent Units' in tag family formulas.");
                report.AppendLine("Run 'Configure Tag Labels' to auto-fix, then re-create families.");
                foreach (var (pname, ptype) in typeMismatches.Take(20))
                    report.AppendLine($"  {pname}: {ptype} (should be TEXT)");
                if (typeMismatches.Count > 20)
                    report.AppendLine($"  ... and {typeMismatches.Count - 20} more");
            }

            // Check bound param types in project
            var boundMismatches = LabelParamTypeValidator.ValidateBoundParams(doc);
            if (boundMismatches.Count > 0)
            {
                report.AppendLine();
                report.AppendLine($"⚠ PROJECT: {boundMismatches.Count} label params bound as non-Text");
                report.AppendLine("These need re-binding: delete old params, reload MR_PARAMETERS.txt,");
                report.AppendLine("then re-create tag families.");
                foreach (var (pname, stype) in boundMismatches.Take(20))
                    report.AppendLine($"  {pname}: {stype}");
            }

            // ── Style-pack coverage (review fix for leader-and-style #2) ──
            // Tag families should expose all 128 TAG_{SIZE}{STYLE}_{COLOR}_BOOL
            // params. If injection partly failed, ApplyTagStyle silently
            // updates 0 types and the user sees no error — this audit makes
            // the partial state visible. Counted on type-elements that have
            // at least ONE style param (i.e. STING tag types).
            var stylePack = ParamRegistry.AllTagStyleParams;
            int stingTypesScanned = 0;
            int stingTypesFullPack = 0;
            int stingTypesPartialPack = 0;
            int stingTypesNoPack = 0;
            int worstMissing = 0;
            string worstTypeName = "";
            foreach (Element typeEl in new FilteredElementCollector(doc).WhereElementIsElementType())
            {
                if (typeEl?.Category == null) continue;
                if (typeEl.Category.CategoryType != CategoryType.Annotation) continue;
                int present = 0;
                foreach (var name in stylePack)
                    if (typeEl.LookupParameter(name) != null) present++;
                if (present == 0) continue;
                stingTypesScanned++;
                int typeMissing = stylePack.Length - present;
                if (typeMissing == 0) stingTypesFullPack++;
                else
                {
                    stingTypesPartialPack++;
                    if (typeMissing > worstMissing)
                    {
                        worstMissing = typeMissing;
                        worstTypeName = $"{typeEl.Name} ({((typeEl as ElementType)?.FamilyName) ?? "?"})";
                    }
                }
                if (present < 8) stingTypesNoPack++;
            }
            if (stingTypesScanned > 0)
            {
                report.AppendLine();
                report.AppendLine("── Style-pack coverage (128 TAG_*_BOOL params) ──");
                report.AppendLine($"  Annotation types with style params: {stingTypesScanned}");
                report.AppendLine($"  Full pack (all 128):                {stingTypesFullPack}");
                report.AppendLine($"  Partial pack (silent style failure): {stingTypesPartialPack}");
                if (stingTypesPartialPack > 0)
                {
                    report.AppendLine($"  Worst: {worstTypeName} — missing {worstMissing} of {stylePack.Length} params");
                    report.AppendLine("  → ApplyTagStyle will silently no-op for missing slots.");
                    report.AppendLine("  → Re-run 'Create Tag Families' or 'Family Parameter Processor'.");
                }
            }

            // ── Paragraph BOOL storage mix (review fix for leader-and-style #5) ──
            // Mixed YESNO + TEXT bindings cause SetParagraphDepth to update
            // half the project; surface the count so a migration can be run.
            var paraStates = ParamRegistry.AllParaStates;
            int paraStringTypes = 0, paraIntegerTypes = 0, paraMixed = 0;
            foreach (Element typeEl in new FilteredElementCollector(doc).WhereElementIsElementType())
            {
                bool sawString = false, sawInt = false;
                foreach (var pn in paraStates)
                {
                    Parameter p = typeEl.LookupParameter(pn);
                    if (p == null) continue;
                    if (p.StorageType == StorageType.String) sawString = true;
                    else if (p.StorageType == StorageType.Integer) sawInt = true;
                }
                if (sawString && sawInt) paraMixed++;
                else if (sawString) paraStringTypes++;
                else if (sawInt) paraIntegerTypes++;
            }
            if (paraStringTypes + paraIntegerTypes + paraMixed > 0)
            {
                report.AppendLine();
                report.AppendLine("── Paragraph BOOL storage (TAG_PARA_STATE_*_BOOL) ──");
                report.AppendLine($"  TEXT-storage (v5.3+):  {paraStringTypes}");
                report.AppendLine($"  YESNO-storage (legacy): {paraIntegerTypes}");
                report.AppendLine($"  Mixed within one type:  {paraMixed}");
                if (paraIntegerTypes > 0 || paraMixed > 0)
                {
                    report.AppendLine("  ⚠ Mixed bindings make SetParagraphDepth half-silent.");
                    report.AppendLine("    Calculated-Value `if(BOOL, …)` only resolves on TEXT params.");
                    report.AppendLine("    Re-load MR_PARAMETERS.txt v5.3+ then re-bind from project.");
                }
            }

            StingLog.Info($"TagFamilyAudit stylePack: scanned={stingTypesScanned}, " +
                $"full={stingTypesFullPack}, partial={stingTypesPartialPack}; " +
                $"paraBOOL: text={paraStringTypes}, yesno={paraIntegerTypes}, mixed={paraMixed}");

            TaskDialog td = new TaskDialog("Tag Family Audit");
            td.MainInstruction = $"Tag coverage: {stingLoaded + otherTag}/{TagFamilyConfig.CategoryTemplateMap.Count} categories";
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"TagFamilyAudit: sting={stingLoaded}, other={otherTag}, missing={missing}");
            return Result.Succeeded;
        }
    }

    /// <summary>
    /// Validates that all shared parameters used in tag family label tiers (tier_2, tier_3, warnings)
    /// are TEXT type. Numeric parameters (NUMBER, LENGTH, AREA, etc.) cause Revit "Inconsistent Units"
    /// errors when used in calculated value formulas like if(BOOL, PARAM, "").
    ///
    /// This validation checks:
    /// 1. MR_PARAMETERS.txt definitions (source file)
    /// 2. Bound parameters in the current project (runtime)
    /// 3. Reports any mismatches with fix instructions
    /// </summary>
    internal static class LabelParamTypeValidator
    {
        /// <summary>
        /// Validate that all label tier parameters are TEXT type in MR_PARAMETERS.txt.
        /// Returns list of (paramName, currentType) for non-TEXT params.
        /// </summary>
        public static List<(string name, string type)> ValidateSourceFile()
        {
            var mismatches = new List<(string name, string type)>();

            // Load label definitions
            var catLabels = LabelDefinitionHelper.LoadCategoryLabels();
            if (catLabels.Count == 0) return mismatches;

            // Collect all params in tier_2/tier_3/warnings
            var labelParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var catData in catLabels.Values)
            {
                foreach (string tierName in new[] { "tier_2", "tier_3", "warnings" })
                {
                    var tier = catData[tierName] as JArray;
                    if (tier == null) continue;
                    foreach (JObject row in tier)
                    {
                        string param = row["parameter"]?.ToString() ?? row["param"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(param))
                            labelParams.Add(param);
                    }
                }
            }

            // Read MR_PARAMETERS.txt types
            string mrFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(mrFile)) return mismatches;

            try
            {
                foreach (string line in File.ReadLines(mrFile))
                {
                    if (!line.StartsWith("PARAM")) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 4) continue;

                    string pname = parts[2];
                    string ptype = parts[3];

                    if (!labelParams.Contains(pname)) continue;

                    // Per LABEL_DEFINITIONS.json (v5.3+): every parameter referenced by a
                    // label/calculated-value template — including _BOOL ones — must be TEXT,
                    // because Revit label formulas cannot use YESNO parameters as the
                    // condition of if(...). STING writers detect storage and write
                    // 'Yes'/'No' for TEXT vs 1/0 for legacy INTEGER families.
                    // Pure-flag _BOOL params (NOT referenced by label tiers) are governed
                    // by MR_PARAMETERS.txt directly and stay YESNO — they never reach this
                    // validator because labelParams only contains label-tier references.
                    if (!string.Equals(ptype, "TEXT", StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches.Add((pname, ptype));
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LabelParamTypeValidator: {ex.Message}");
            }

            return mismatches;
        }

        /// <summary>
        /// Validate bound parameters in a Revit project/family document.
        /// Returns list of (paramName, storageType) for params bound as non-String.
        /// </summary>
        public static List<(string name, string storageType)> ValidateBoundParams(Document doc)
        {
            var mismatches = new List<(string name, string storageType)>();

            var catLabels = LabelDefinitionHelper.LoadCategoryLabels();
            if (catLabels.Count == 0) return mismatches;

            var labelParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var catData in catLabels.Values)
            {
                foreach (string tierName in new[] { "tier_2", "tier_3", "warnings" })
                {
                    var tier = catData[tierName] as JArray;
                    if (tier == null) continue;
                    foreach (JObject row in tier)
                    {
                        string param = row["parameter"]?.ToString() ?? row["param"]?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(param))
                            labelParams.Add(param);
                    }
                }
            }

            // Check a sample element for each problematic param
            // Use BindingMap to check parameter definitions
            var bindingMap = doc.ParameterBindings;
            var iter = bindingMap.ForwardIterator();
            while (iter.MoveNext())
            {
                var def = iter.Key as InternalDefinition;
                if (def == null) continue;
                if (!labelParams.Contains(def.Name)) continue;

                // Label-tier params (including _BOOL) must be TEXT — see ValidateSourceFile
                // and the LABEL_DEFINITIONS.json _comment for the rationale.
                try
                {
                    var spec = def.GetDataType();
                    if (spec != null && spec != SpecTypeId.String.Text)
                    {
                        mismatches.Add((def.Name, spec.TypeId));
                    }
                }
                catch (Exception ex) { StingLog.Warn($"older Revit API: {ex.Message}"); }
            }

            return mismatches;
        }

        /// <summary>
        /// Auto-fix MR_PARAMETERS.txt by changing all label tier numeric params to TEXT.
        /// Returns number of params fixed.
        /// </summary>
        public static int AutoFixSourceFile()
        {
            var mismatches = ValidateSourceFile();
            if (mismatches.Count == 0) return 0;

            string mrFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(mrFile)) return 0;

            var namesToFix = new HashSet<string>(mismatches.Select(m => m.name),
                StringComparer.OrdinalIgnoreCase);

            try
            {
                var lines = File.ReadAllLines(mrFile);
                int fixed_ = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].StartsWith("PARAM")) continue;
                    string[] parts = lines[i].Split('\t');
                    if (parts.Length < 4) continue;
                    if (namesToFix.Contains(parts[2]))
                    {
                        // Label-tier params (including _BOOL ones used in if(...) formulas)
                        // must be TEXT. See ValidateSourceFile rationale.
                        parts[3] = "TEXT";
                        lines[i] = string.Join("\t", parts);
                        fixed_++;
                    }
                }

                File.WriteAllLines(mrFile, lines);
                StingLog.Info($"LabelParamTypeValidator: Fixed {fixed_} label-tier params to TEXT in MR_PARAMETERS.txt");
                return fixed_;
            }
            catch (Exception ex)
            {
                StingLog.Error("LabelParamTypeValidator.AutoFix failed", ex);
                return 0;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Augment Tag Families — full parameter sync for existing .rfa files
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Brings every STING tag family .rfa on disk up to the complete
    /// STING parameter contract without disturbing any already-configured labels.
    ///
    /// What it injects (all additive — existing params are never removed or moved):
    ///   1. Tag containers   — TAG1-TAG7, TAG7A-TAG7F (13 params)
    ///   2. Visibility gates — TAG_PARA_STATE_1..10_BOOL + TAG_WARN_VISIBLE_BOOL
    ///   3. Tag style matrix — 128 TAG_{size}{style}_{colour}_BOOL variants
    ///   4. Appearance params — box colour/visible/style, leader colour,
    ///                          TAG_SCALE_TIER_AUTO_BOOL, TAG_DEPTH_TIER_INT
    ///   5. Description      — ASS_DESCRIPTION_TXT
    ///   6. Category-specific label params from LABEL_DEFINITIONS.json
    ///   7. T4-T10 label formula rows — from STING_TAG_CONFIG_v5_0_*.csv
    ///
    /// Safety guarantees:
    ///   • FamilyManager.AddParameter is additive-only: already-bound params are skipped.
    ///   • FamilyLabelAuthor.AuthorLabelsMulti only touches T4..T10 rows from the CSV tier plans.
    ///   • T1-T3 label elements are completely outside its scope.
    ///   • When PreserveHandEdits=true, formula writes are skipped on hand-positioned families
    ///     (param bindings are still added — only the formula overwrite is deferred).
    ///   • Families with no CSV tier plan still receive the full base + style + visibility params.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AugmentTagFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp = ParameterHelpers.GetApp(commandData);
            Document doc = uiApp.ActiveUIDocument?.Document;
            var app = uiApp.Application;

            // ── Step 1: Shared param file ──────────────────────────────
            string sharedParamFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(sharedParamFile))
            {
                TaskDialog.Show("Augment Tag Families",
                    "Cannot find MR_PARAMETERS.txt.\nRun 'Check Data' to verify data files.");
                return Result.Failed;
            }

            // ── Step 2: Load tier plans from CSVs ─────────────────────
            Dictionary<string, Dictionary<string, TierPlan>> plansByMode;
            Dictionary<string, TierPlan> plansByFamily;
            bool preserveHandEdits;
            try
            {
                plansByMode = TagConfigPlanResolver.LoadAllPerMode(doc);
                plansByFamily = TagConfigPlanResolver.LoadAll(doc);
                preserveHandEdits = TagConfigPlanResolver.ReadPreserveHandEdits(doc);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Augment Tag Families",
                    $"Failed to load tier plans from CSVs:\n{ex.Message}");
                return Result.Failed;
            }

            // ── Step 3: Locate .rfa output folder ─────────────────────
            string outputDir = TagFamilyConfig.GetOutputDirectory();
            if (!Directory.Exists(outputDir))
            {
                TaskDialog.Show("Augment Tag Families",
                    $"Tag family output directory does not exist:\n{outputDir}\n\n" +
                    "Run 'Create Tag Families' first to generate the .rfa files.");
                return Result.Failed;
            }

            string[] rfaFiles = Directory.GetFiles(outputDir, "STING - *.rfa");
            if (rfaFiles.Length == 0)
            {
                TaskDialog.Show("Augment Tag Families",
                    $"No STING tag family .rfa files found in:\n{outputDir}\n\n" +
                    "Run 'Create Tag Families' first to generate the .rfa files.");
                return Result.Failed;
            }

            // ── Step 4: Confirm with user ──────────────────────────────
            var confirm = TaskDialog.Show("Augment Tag Families",
                $"Found {rfaFiles.Length} STING .rfa files in:\n{outputDir}\n\n" +
                "This will inject ALL missing parameters (additive — nothing removed):\n" +
                "  • Tag containers (TAG1-TAG7, TAG7A-TAG7F)\n" +
                "  • Visibility gates (TAG_PARA_STATE_1..10_BOOL, TAG_WARN_VISIBLE_BOOL)\n" +
                "  • Tag style matrix (128 TAG_{size}{style}_{colour}_BOOL variants)\n" +
                "  • Appearance params (box colour/style, leader colour, scale/depth cache)\n" +
                "  • Category-specific label params (LABEL_DEFINITIONS.json)\n" +
                "  • T4-T10 label formulas (STING_TAG_CONFIG CSV tier definitions)\n\n" +
                "T1-T3 label rows you have already configured are NOT touched.\n" +
                "Families with no CSV tier plan still receive base + style + visibility params.\n\n" +
                $"Preserve hand-edited label positions: {(preserveHandEdits ? "YES" : "NO")}\n\n" +
                "Continue?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
            if (confirm == TaskDialogResult.No) return Result.Cancelled;

            // ── Step 5: Process each .rfa ──────────────────────────────
            var report = new StringBuilder();
            int success = 0, noplan = 0, failed = 0;

            foreach (string rfaPath in rfaFiles)
            {
                string famName = Path.GetFileNameWithoutExtension(rfaPath);
                Document famDoc = null;
                try
                {
                    famDoc = app.OpenDocumentFile(rfaPath);
                    if (famDoc == null)
                    {
                        report.AppendLine($"  [FAIL] {famName} — OpenDocumentFile returned null");
                        failed++;
                        continue;
                    }

                    // ── 5a: Detect the family's Revit category ─────────
                    string categoryDisplay = DetectCategoryName(famDoc);

                    // ── 5b: Build the FULL parameter list for this family
                    //   Includes: TagParams + VisibilityParams + StyleParams
                    //             + ASS_DESCRIPTION_TXT + category label params
                    List<string> allParams = TagFamilyConfig.GetAllFamilyParams(
                        categoryDisplay, famName);

                    // ── 5c: Inject ALL missing shared parameters ────────
                    int injected = InjectMissingParams(famDoc, allParams, sharedParamFile, app);

                    // ── 5d: Apply T4-T10 label formulas from CSVs ──────
                    var modePlans = BuildModePlans(plansByMode, plansByFamily, famName);
                    int formulasApplied = 0, formulasSkipped = 0,
                        tiersPreserved = 0, labelRebound = 0;
                    string planTag = "(no CSV tier plan — base/style params only)";

                    if (modePlans.Count > 0)
                    {
                        var opts = new FamilyLabelAuthor.Options
                        {
                            App = app,
                            SharedParamFile = sharedParamFile,
                            PreserveHandEdits = preserveHandEdits,
                            FamilyName = famName,
                        };
                        FamilyLabelAuthor.Result r =
                            FamilyLabelAuthor.AuthorLabelsMulti(famDoc, modePlans, opts);
                        formulasApplied = r.FormulasApplied;
                        formulasSkipped = r.FormulasSkipped;
                        tiersPreserved  = r.TiersPreserved;
                        labelRebound    = r.LabelRebound;
                        planTag = modePlans.Count > 1
                            ? $"modes=[{string.Join(",", modePlans.ConvertAll(m => m.Mode))}]"
                            : "single-mode";
                        foreach (var w in r.Warnings)
                            StingLog.Warn($"AugmentTagFamilies {famName}: {w}");
                    }
                    else
                    {
                        noplan++;
                    }

                    // ── 5e: Save in-place ───────────────────────────────
                    var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(rfaPath, saveOpts);

                    report.AppendLine($"  [OK]   {famName}");
                    report.AppendLine(
                        $"           cat=\"{categoryDisplay}\" " +
                        $"injected={injected} {planTag}");
                    report.AppendLine(
                        $"           formulas={formulasApplied} skipped={formulasSkipped} " +
                        $"preserved={tiersPreserved} label-rebound={labelRebound}");
                    success++;
                }
                catch (Exception ex)
                {
                    report.AppendLine($"  [FAIL] {famName} — {ex.Message}");
                    StingLog.Error($"AugmentTagFamilies: {famName}", ex);
                    failed++;
                }
                finally
                {
                    try { famDoc?.Close(false); } catch { }
                }
            }

            // ── Step 6: Optionally reload augmented families into project ─
            if (success > 0 && doc != null)
            {
                var reload = TaskDialog.Show("Augment Tag Families — Reload?",
                    $"Augmented {success} families successfully.\n\n" +
                    "Reload all augmented .rfa files into the current project?\n" +
                    "(Only families that are already loaded will be updated.)\n\n" +
                    "overwriteParameterValues = false — existing placed-tag instance values are preserved.",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);

                if (reload == TaskDialogResult.Yes)
                {
                    int reloaded = 0, reloadFailed = 0;
                    foreach (string rfaPath in rfaFiles)
                    {
                        string famName = Path.GetFileNameWithoutExtension(rfaPath);
                        try
                        {
                            // Only reload families that are already in the project
                            bool found = false;
                            foreach (Family f in new FilteredElementCollector(doc)
                                .OfClass(typeof(Family)).Cast<Family>())
                            {
                                if (string.Equals(f.Name, famName,
                                    StringComparison.OrdinalIgnoreCase))
                                { found = true; break; }
                            }
                            if (!found) continue;

                            using (Transaction tx = new Transaction(doc,
                                $"STING Reload {famName}"))
                            {
                                tx.Start();
                                doc.LoadFamily(rfaPath, new OverwriteLoadOptions(), out Family _);
                                tx.Commit();
                            }
                            reloaded++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn(
                                $"AugmentTagFamilies reload {famName}: {ex.Message}");
                            reloadFailed++;
                        }
                    }
                    report.AppendLine();
                    report.AppendLine(
                        $"Reloaded {reloaded} into project ({reloadFailed} failed).");
                }
            }

            // ── Step 7: Summary ────────────────────────────────────────
            string summary =
                $"Processed {rfaFiles.Length}:  OK={success}  " +
                $"No-CSV-plan={noplan}  Failed={failed}\n" +
                $"Source: {outputDir}\n\n" +
                report.ToString();

            TaskDialog.Show("Augment Tag Families — Complete", summary);
            return Result.Succeeded;
        }

        // ─────────────────────────────────────────────────────────────
        //  Static helpers
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Detect the Revit category display name from an open family document,
        /// e.g. "Ducts", "Air Terminals", "Mechanical Equipment", "Lighting Fixtures".
        /// Falls back to empty string on any failure so GetAllFamilyParams still works.
        /// </summary>
        private static string DetectCategoryName(Document famDoc)
        {
            try
            {
                var famCatId = famDoc.OwnerFamily?.FamilyCategoryId;
                if (famCatId == null || famCatId == ElementId.InvalidElementId)
                    return "";
                var cat = Category.GetCategory(famDoc, famCatId);
                return cat?.Name ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// Open MR_PARAMETERS.txt and add every parameter in <paramref name="paramNames"/>
        /// that is not already bound in the family. Additive-only: existing params are left
        /// exactly as-is. Returns the count of newly added parameters.
        /// </summary>
        private static int InjectMissingParams(
            Document famDoc,
            IEnumerable<string> paramNames,
            string sharedParamFile,
            Autodesk.Revit.ApplicationServices.Application app)
        {
            string originalFile = app.SharedParametersFilename;
            try
            {
                app.SharedParametersFilename = sharedParamFile;
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    StingLog.Warn(
                        "AugmentTagFamilies.InjectMissingParams: cannot open shared param file");
                    return 0;
                }

                // Build O(1) lookup of already-present param names
                var present = new HashSet<string>(StringComparer.Ordinal);
                FamilyManager famMan = famDoc.FamilyManager;
                foreach (FamilyParameter fp in famMan.Parameters)
                    present.Add(fp.Definition.Name);

                // Collect definitions to add (skip already-present ones)
                var toAdd = new List<ExternalDefinition>();
                foreach (string pName in paramNames)
                {
                    if (present.Contains(pName)) continue;
                    ExternalDefinition extDef = FindExternalDefinition(defFile, pName);
                    if (extDef == null)
                    {
                        StingLog.Warn(
                            $"AugmentTagFamilies: '{pName}' not found in MR_PARAMETERS.txt");
                        continue;
                    }
                    toAdd.Add(extDef);
                }

                if (toAdd.Count == 0) return 0;

                int added = 0;
                using (Transaction tx = new Transaction(famDoc, "STING Inject Missing Params"))
                {
                    tx.Start();
                    foreach (ExternalDefinition extDef in toAdd)
                    {
                        try
                        {
                            famMan.AddParameter(
                                extDef,
                                GroupTypeId.General,
                                true /* isInstance = true: tags display per-instance values */);
                            added++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn(
                                $"AugmentTagFamilies: cannot add '{extDef.Name}': {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                return added;
            }
            catch (Exception ex)
            {
                StingLog.Error("AugmentTagFamilies.InjectMissingParams failed", ex);
                return 0;
            }
            finally
            {
                // ALWAYS restore — prevents leaving Revit pointed at the wrong SP file
                try { app.SharedParametersFilename = originalFile; } catch { }
            }
        }

        /// <summary>Search every group in the definition file for a matching parameter name.</summary>
        private static ExternalDefinition FindExternalDefinition(
            DefinitionFile defFile, string paramName)
        {
            foreach (DefinitionGroup grp in defFile.Groups)
                foreach (Definition def in grp.Definitions)
                    if (def.Name == paramName && def is ExternalDefinition ext)
                        return ext;
            return null;
        }

        /// <summary>
        /// Build the list of mode plans for a given family name by consulting both the
        /// per-mode and the flat family tier-plan maps (same logic as
        /// <see cref="CreateTagFamiliesCommand.AuthorFromPlanIfAvailable"/>).
        /// Returns an empty list when the family has no CSV tier plan entry — in that
        /// case the caller still injects base + style + visibility params, just skips formulas.
        /// </summary>
        private static List<FamilyLabelAuthor.ModePlan> BuildModePlans(
            Dictionary<string, Dictionary<string, TierPlan>> plansByMode,
            Dictionary<string, TierPlan> plansByFamily,
            string famName)
        {
            var modePlans = new List<FamilyLabelAuthor.ModePlan>();

            if (plansByMode != null)
            {
                foreach (var kv in plansByMode)
                {
                    if (kv.Value == null) continue;
                    TierPlan plan = TagFamilyConfig.TryGetTierPlan(kv.Value, famName);
                    if (plan == null) continue;
                    modePlans.Add(new FamilyLabelAuthor.ModePlan
                    {
                        Mode      = kv.Key,
                        GateParam = HandoverModeHelper.GetSelectorBool(kv.Key),
                        Plan      = plan,
                    });
                }
            }

            if (modePlans.Count == 0 && plansByFamily != null)
            {
                TierPlan plan = TagFamilyConfig.TryGetTierPlan(plansByFamily, famName);
                if (plan != null)
                {
                    modePlans.Add(new FamilyLabelAuthor.ModePlan
                    {
                        Mode = "", GateParam = null, Plan = plan,
                    });
                }
            }

            return modePlans;
        }

        /// <summary>
        /// IFamilyLoadOptions that always accepts the incoming family without
        /// overwriting instance parameter values on already-placed tags.
        /// </summary>
        private sealed class OverwriteLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false; // preserve placed-tag instance values
                return true;
            }
            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}
