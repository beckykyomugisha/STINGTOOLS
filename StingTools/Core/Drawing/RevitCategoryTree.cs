// StingTools — Drawing Template Manager · Phase 137
//
// RevitCategoryTree is the single source of truth for the Revit
// model categories (and their subcategories) that the VG editor,
// the annotation runner, and the validator iterate over. Pure data —
// no Revit API calls and no logic. Subcategory entries inherit
// HasCutLines from the parent unless explicitly overridden.

using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public sealed class RevitCategory
    {
        public string Bic { get; set; }
        public string DisplayName { get; set; }
        public bool HasCutLines { get; set; }
        public bool HasHalftone { get; set; }
        public bool HasDetailLevel { get; set; }
        public bool IsTaggable { get; set; }
        public List<RevitSubCategory> SubCategories { get; set; } = new List<RevitSubCategory>();
    }

    public sealed class RevitSubCategory
    {
        public string DisplayName { get; set; }
        public string BicPath { get; set; }
        public bool HasCutLines { get; set; }
    }

    public static class RevitCategoryTree
    {
        public static IReadOnlyList<RevitCategory> All { get; }

        public static IEnumerable<RevitCategory> TaggableCategories =>
            All.Where(c => c.IsTaggable);

        public static IEnumerable<RevitCategory> CategoriesWithCut =>
            All.Where(c => c.HasCutLines);

        public static RevitCategory FindByBic(string bic)
        {
            if (string.IsNullOrEmpty(bic)) return null;
            return All.FirstOrDefault(c =>
                string.Equals(c.Bic, bic, System.StringComparison.OrdinalIgnoreCase));
        }

        public static RevitCategory FindByDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return All.FirstOrDefault(c =>
                string.Equals(c.DisplayName, name, System.StringComparison.OrdinalIgnoreCase));
        }

        static RevitCategoryTree()
        {
            var list = new List<RevitCategory>();
            Populate(list);
            All = list;
        }

        // Helper to build subcategory lists with parent's HasCutLines.
        private static List<RevitSubCategory> Subs(string parentBic, bool parentCut, params string[] names)
        {
            var result = new List<RevitSubCategory>(names.Length);
            foreach (var n in names)
                result.Add(new RevitSubCategory { DisplayName = n, BicPath = parentBic + "/" + n, HasCutLines = parentCut });
            return result;
        }

        private static void Populate(List<RevitCategory> list)
        {
            // Areas
            list.Add(new RevitCategory { Bic = "OST_Areas", DisplayName = "Areas",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_Areas", false, "Connection Zones", "Interior Fill", "Maintenance Zones", "Reference Lines", "Zones") });

            // Audio Visual Devices
            list.Add(new RevitCategory { Bic = "OST_AudioVisualDevices", DisplayName = "Audio Visual Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true });

            // Cable Tray Fittings
            list.Add(new RevitCategory { Bic = "OST_CableTrayFitting", DisplayName = "Cable Tray Fittings",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_CableTrayFitting", false, "Center Line", "Drop", "Rise") });

            // Cable Trays
            list.Add(new RevitCategory { Bic = "OST_CableTray", DisplayName = "Cable Trays",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_CableTray", false, "Center Line", "Clearance Zones", "Drop", "Maintenance Zones", "Rise") });

            // Casework
            list.Add(new RevitCategory { Bic = "OST_Casework", DisplayName = "Casework",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Casework", true, "Hidden Lines") });

            // Ceilings
            list.Add(new RevitCategory { Bic = "OST_Ceilings", DisplayName = "Ceilings",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Ceilings", true, "Common Edges", "Common Lines", "Hidden Lines", "Interior Fill", "Pattern Fill", "Surface Pattern") });

            // Columns (Architectural)
            list.Add(new RevitCategory { Bic = "OST_Columns", DisplayName = "Columns",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Columns", true, "Clearance Zones", "Connection Zones", "Hidden Lines", "Maintenance Zones") });

            // Communication Devices
            list.Add(new RevitCategory { Bic = "OST_CommunicationDevices", DisplayName = "Communication Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_CommunicationDevices", false, "Center Line", "Clearance Zones", "Drop", "Maintenance Zones", "Rise") });

            // Conduit Fittings
            list.Add(new RevitCategory { Bic = "OST_ConduitFitting", DisplayName = "Conduit Fittings",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_ConduitFitting", false, "Center Line", "Drop", "Rise") });

            // Conduits
            list.Add(new RevitCategory { Bic = "OST_Conduit", DisplayName = "Conduits",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_Conduit", false, "Center Line", "Clearance Zones", "Drop", "Maintenance Zones", "Rise") });

            // Curtain Panels
            list.Add(new RevitCategory { Bic = "OST_CurtainWallPanels", DisplayName = "Curtain Panels",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_CurtainWallPanels", true, "Hidden Lines") });

            // Curtain Systems
            list.Add(new RevitCategory { Bic = "OST_CurtainSystems", DisplayName = "Curtain Systems",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_CurtainSystems", true, "Hidden Lines") });

            // Curtain Wall Mullions
            list.Add(new RevitCategory { Bic = "OST_CurtainWallMullions", DisplayName = "Curtain Wall Mullions",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_CurtainWallMullions", true, "Hidden Lines") });

            // Data Devices
            list.Add(new RevitCategory { Bic = "OST_DataDevices", DisplayName = "Data Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_DataDevices", false, "Center Line", "Clearance Zones", "Drop", "Maintenance Zones", "Rise") });

            // Detail Items
            list.Add(new RevitCategory { Bic = "OST_DetailComponents", DisplayName = "Detail Items",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_DetailComponents", false, "Hidden Lines", "Light Source") });

            // Doors
            list.Add(new RevitCategory { Bic = "OST_Doors", DisplayName = "Doors",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Doors", true, "Elevation Swing", "Frame/Mullion", "Glass", "Hidden Lines", "Moulding", "Opening", "Plan Swing", "Sill/Head", "Trim") });

            // Duct Accessories
            list.Add(new RevitCategory { Bic = "OST_DuctAccessory", DisplayName = "Duct Accessories",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_DuctAccessory", false, "Center Line", "Clearance Zones", "Maintenance Zones") });

            // Duct Fittings
            list.Add(new RevitCategory { Bic = "OST_DuctFitting", DisplayName = "Duct Fittings",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_DuctFitting", false, "Center Line", "Clearance Zones", "Insulation", "Lining", "Maintenance Zones") });

            // Duct Insulations
            list.Add(new RevitCategory { Bic = "OST_DuctInsulations", DisplayName = "Duct Insulations",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            // Duct Linings
            list.Add(new RevitCategory { Bic = "OST_DuctLinings", DisplayName = "Duct Linings",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            // Duct Placeholders
            list.Add(new RevitCategory { Bic = "OST_PlaceHolderDucts", DisplayName = "Duct Placeholders",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            // Ducts
            list.Add(new RevitCategory { Bic = "OST_DuctCurves", DisplayName = "Ducts",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_DuctCurves", false, "Center Line", "Clearance Zones", "Flex", "Hidden Lines", "Insulation", "Lining", "Maintenance Zones", "Rise") });

            // Electrical Equipment
            list.Add(new RevitCategory { Bic = "OST_ElectricalEquipment", DisplayName = "Electrical Equipment",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_ElectricalEquipment", true, "Center Line", "Clearance Zones", "Hidden Lines", "Maintenance Zones") });

            // Electrical Fixtures
            list.Add(new RevitCategory { Bic = "OST_ElectricalFixtures", DisplayName = "Electrical Fixtures",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_ElectricalFixtures", false, "Center Line", "Clearance Zones", "Hidden Lines", "Maintenance Zones") });

            // Entourage
            list.Add(new RevitCategory { Bic = "OST_Entourage", DisplayName = "Entourage",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_Entourage", false, "Hidden Lines") });

            // Fire Alarm Devices
            list.Add(new RevitCategory { Bic = "OST_FireAlarmDevices", DisplayName = "Fire Alarm Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_FireAlarmDevices", false, "Center Line", "Clearance Zones", "Drop", "Maintenance Zones", "Rise") });

            // Fire Protection
            list.Add(new RevitCategory { Bic = "OST_FireProtection", DisplayName = "Fire Protection",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true });

            // Flex Ducts
            list.Add(new RevitCategory { Bic = "OST_FlexDuctCurves", DisplayName = "Flex Ducts",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_FlexDuctCurves", false, "Center Line", "Clearance Zones", "Maintenance Zones") });

            // Flex Pipes
            list.Add(new RevitCategory { Bic = "OST_FlexPipeCurves", DisplayName = "Flex Pipes",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_FlexPipeCurves", false, "Center Line", "Clearance Zones", "Insulation", "Maintenance Zones") });

            // Floors
            list.Add(new RevitCategory { Bic = "OST_Floors", DisplayName = "Floors",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Floors", true, "Common Edges", "Hidden Lines", "Interior Fill", "Pattern Fill", "Slab Edges", "Surface Pattern") });

            // Food Service Equipment
            list.Add(new RevitCategory { Bic = "OST_FoodServiceEquipment", DisplayName = "Food Service Equipment",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_FoodServiceEquipment", true, "Hidden Lines") });

            // Furniture
            list.Add(new RevitCategory { Bic = "OST_Furniture", DisplayName = "Furniture",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Furniture", true, "Hidden Lines", "Line randomness", "Lines direcida", "Overhead Lines", "Rise line") });

            // Furniture Systems
            list.Add(new RevitCategory { Bic = "OST_FurnitureSystems", DisplayName = "Furniture Systems",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_FurnitureSystems", true, "Hidden Lines", "Side Edges") });

            // Generic Models
            list.Add(new RevitCategory { Bic = "OST_GenericModel", DisplayName = "Generic Models",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_GenericModel", true, "Automatic Flush", "Hidden Lines") });

            // Grids
            list.Add(new RevitCategory { Bic = "OST_Grids", DisplayName = "Grids",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true });

            // HVAC Zones
            list.Add(new RevitCategory { Bic = "OST_HVAC_Zones", DisplayName = "HVAC Zones",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_HVAC_Zones", false, "Boundaries", "Interior Fill", "Reference Lines") });

            // Lighting Devices
            list.Add(new RevitCategory { Bic = "OST_LightingDevices", DisplayName = "Lighting Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_LightingDevices", false, "Center Line", "Clearance Zones", "Connection (Other)", "Maintenance Zones", "Rise line") });

            // Lighting Fixtures
            list.Add(new RevitCategory { Bic = "OST_LightingFixtures", DisplayName = "Lighting Fixtures",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_LightingFixtures", false, "Center Line", "Clearance Zones", "Hidden Lines", "Light Source", "Maintenance Zones") });

            // Mass
            list.Add(new RevitCategory { Bic = "OST_Mass", DisplayName = "Mass",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Mass", true, "<Hidden Lines>", "Curtain Panel", "Face", "Form", "Glazing", "Mass Exterior Wall", "Mass Floor", "Mass Glazing", "Mass Opening", "Mass Roof", "Mass Shade", "Mass Skylight", "Mass Zone", "Pattern Fill", "Pattern Lines") });

            // Mechanical Control Devices
            list.Add(new RevitCategory { Bic = "OST_MechanicalControlDevices", DisplayName = "Mechanical Control Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_MechanicalControlDevices", false, "<Hidden Lines>") });

            // Mechanical Equipment
            list.Add(new RevitCategory { Bic = "OST_MechanicalEquipment", DisplayName = "Mechanical Equipment",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_MechanicalEquipment", true, "Center Line", "Clearance Zones", "Connection (Other)", "Hidden Lines", "Maintenance Zones") });

            // MEP Fabrication Ductwork
            list.Add(new RevitCategory { Bic = "OST_FabricationDuctwork", DisplayName = "MEP Fabrication Ductwork",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_FabricationDuctwork", false, "Center Line", "Insulation", "Lining", "Symbology") });

            // MEP Fabrication Hangers
            list.Add(new RevitCategory { Bic = "OST_FabricationContainment", DisplayName = "MEP Fabrication Hangers",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_FabricationContainment", false, "Symbology") });

            // MEP Fabrication Pipework
            list.Add(new RevitCategory { Bic = "OST_FabricationPipework", DisplayName = "MEP Fabrication Pipework",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_FabricationPipework", false, "Center Line", "Insulation", "Symbology") });

            // Nurse Call Devices
            list.Add(new RevitCategory { Bic = "OST_NurseCallDevices", DisplayName = "Nurse Call Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_NurseCallDevices", false, "Center Line", "Drop", "Maintenance Zones", "Rise") });

            // Parking
            list.Add(new RevitCategory { Bic = "OST_Parking", DisplayName = "Parking",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_Parking", false, "Hidden Lines") });

            // Parts
            list.Add(new RevitCategory { Bic = "OST_Parts", DisplayName = "Parts",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Parts", true, "Hidden Lines") });

            // Pipe Accessories
            list.Add(new RevitCategory { Bic = "OST_PipeAccessory", DisplayName = "Pipe Accessories",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_PipeAccessory", false, "Accessories", "Center Line", "Clearance Zones", "Maintenance Zones") });

            // Pipe Fittings
            list.Add(new RevitCategory { Bic = "OST_PipeFitting", DisplayName = "Pipe Fittings",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_PipeFitting", false, "Center Line", "Clearance Zones", "Insulation", "Maintenance Zones") });

            // Pipe Insulations
            list.Add(new RevitCategory { Bic = "OST_PipeInsulations", DisplayName = "Pipe Insulations",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            // Pipe Placeholders
            list.Add(new RevitCategory { Bic = "OST_PlaceHolderPipes", DisplayName = "Pipe Placeholders",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            // Pipes
            list.Add(new RevitCategory { Bic = "OST_PipeCurves", DisplayName = "Pipes",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_PipeCurves", false, "Center Line", "Clearance Zones", "Insulation", "Maintenance Zones", "pipecenter", "pipediameter") });

            // Plumbing Equipment
            list.Add(new RevitCategory { Bic = "OST_PlumbingEquipment", DisplayName = "Plumbing Equipment",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_PlumbingEquipment", true, "<Hidden Lines>") });

            // Plumbing Fixtures
            list.Add(new RevitCategory { Bic = "OST_PlumbingFixtures", DisplayName = "Plumbing Fixtures",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_PlumbingFixtures", true, "Center Line", "Couplings/Flanges", "Flush Valve", "Maintenance Zones", "Nozzle", "Pipe", "Plumbing Accessory", "Plumbing System", "Sanitary Fixtures", "Urinol", "Valve Handle", "Washer") });

            // Railings
            list.Add(new RevitCategory { Bic = "OST_StairsRailing", DisplayName = "Railings",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_StairsRailing", true, "<Above> Marshals", "<Above> Railings Cut Line", "<Above> Top Rails", "Balusters", "Guards", "Rails", "Supports") });

            // Ramps
            list.Add(new RevitCategory { Bic = "OST_Ramps", DisplayName = "Ramps",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Ramps", true, "<Hidden Lines>") });

            // Reference Planes
            list.Add(new RevitCategory { Bic = "OST_CLines", DisplayName = "Reference Planes",
                HasCutLines = false, HasHalftone = false, HasDetailLevel = false, IsTaggable = false });

            // Roads
            list.Add(new RevitCategory { Bic = "OST_Roads", DisplayName = "Roads",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_Roads", false, "<Hidden Lines>", "Reference Line") });

            // Roofs
            list.Add(new RevitCategory { Bic = "OST_Roofs", DisplayName = "Roofs",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Roofs", true, "Common Edges", "Fascia", "Hidden Lines", "Interior Edges", "Interior Fill", "Pattern Fill") });

            // Rooms
            list.Add(new RevitCategory { Bic = "OST_Rooms", DisplayName = "Rooms",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_Rooms", false, "<Overhead>", "Color Fill", "Reference Lines", "Room Separation", "Space Separation", "Thin Lines") });

            // Security Devices
            list.Add(new RevitCategory { Bic = "OST_SecurityDevices", DisplayName = "Security Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_SecurityDevices", false, "Center Line", "Connection (Other)", "Maintenance Zones") });

            // Shaft Openings
            list.Add(new RevitCategory { Bic = "OST_ShaftOpening", DisplayName = "Shaft Openings",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            // Signage
            list.Add(new RevitCategory { Bic = "OST_Signage", DisplayName = "Signage",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Signage", true, "<Hidden Lines>") });

            // Site
            list.Add(new RevitCategory { Bic = "OST_Site", DisplayName = "Site",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_Site", false, "Interior Gauge", "Pads", "Subregion", "Path") });

            // Specialty Equipment
            list.Add(new RevitCategory { Bic = "OST_SpecialityEquipment", DisplayName = "Specialty Equipment",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_SpecialityEquipment", true, "<Hidden Lines>", "Solar Panel") });

            // Sprinklers
            list.Add(new RevitCategory { Bic = "OST_Sprinklers", DisplayName = "Sprinklers",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_Sprinklers", false, "<Hidden Lines>") });

            // Stairs
            list.Add(new RevitCategory { Bic = "OST_Stairs", DisplayName = "Stairs",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Stairs", true, "<Above> Cut Marks", "<Above> Outlines", "<Above> Riser Lines", "<Above> Supports", "<Hidden Lines>", "Cut Marks", "Cut Lines", "Supports", "Treads/Risers", "Stringers/Carriage") });

            // Structural Area Reinforcement
            list.Add(new RevitCategory { Bic = "OST_AreaRein", DisplayName = "Structural Area Reinforcement",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_AreaRein", false, "Boundary", "Lines") });

            // Structural Beam Systems
            list.Add(new RevitCategory { Bic = "OST_StructuralFramingSystem", DisplayName = "Structural Beam Systems",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_StructuralFramingSystem", true, "<Hidden Lines>") });

            // Structural Columns
            list.Add(new RevitCategory { Bic = "OST_StructuralColumns", DisplayName = "Structural Columns",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_StructuralColumns", true, "<Hidden Lines>", "Hidden Lines", "Location Lines", "Stick Symbols") });

            // Structural Connections
            list.Add(new RevitCategory { Bic = "OST_StructuralConnections", DisplayName = "Structural Connections",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false,
                SubCategories = Subs("OST_StructuralConnections", true, "<Hidden Lines>") });

            // Structural Fabric Areas
            list.Add(new RevitCategory { Bic = "OST_FabricAreas", DisplayName = "Structural Fabric Areas",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_FabricAreas", false, "Boundary") });

            // Structural Fabric Reinforcement
            list.Add(new RevitCategory { Bic = "OST_FabricReinforcement", DisplayName = "Structural Fabric Reinforcement",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_FabricReinforcement", false, "Boundary", "Fabric Wire") });

            // Structural Foundations
            list.Add(new RevitCategory { Bic = "OST_StructuralFoundation", DisplayName = "Structural Foundations",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_StructuralFoundation", true, "<Hidden Lines>") });

            // Structural Framing
            list.Add(new RevitCategory { Bic = "OST_StructuralFraming", DisplayName = "Structural Framing",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_StructuralFraming", true, "<Hidden Lines>", "Chord", "Girder", "Hidden Faces", "Horizontal Bracing", "Joist", "Kicker Bracing", "Location Lines", "Other", "Purlin", "Stick Symbols", "Vertical Bracing", "Web") });

            // Structural Path Reinforcement
            list.Add(new RevitCategory { Bic = "OST_PathRein", DisplayName = "Structural Path Reinforcement",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_PathRein", false, "Boundary") });

            // Structural Rebar
            list.Add(new RevitCategory { Bic = "OST_Rebar", DisplayName = "Structural Rebar",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_Rebar", false, "<Hidden Lines>", "Splice Location Lines", "Anchors", "Belts", "Holes", "Modifiers", "Others", "Plates", "Profiles", "Reference", "Shear Studs", "Symbol", "Welds") });

            // Structural Rebar Couplers
            list.Add(new RevitCategory { Bic = "OST_RebarCouplers", DisplayName = "Structural Rebar Couplers",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            // Structural Stiffeners
            list.Add(new RevitCategory { Bic = "OST_StructuralStiffeners", DisplayName = "Structural Stiffeners",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false,
                SubCategories = Subs("OST_StructuralStiffeners", true, "<Hidden Lines>") });

            // Structural Trusses
            list.Add(new RevitCategory { Bic = "OST_StructuralTruss", DisplayName = "Structural Trusses",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_StructuralTruss", true, "Stick Symbols") });

            // Telephone Devices
            list.Add(new RevitCategory { Bic = "OST_TelephoneDevices", DisplayName = "Telephone Devices",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true });

            // Temporary Structures
            list.Add(new RevitCategory { Bic = "OST_TemporaryStructure", DisplayName = "Temporary Structures",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_TemporaryStructure", false, "<Hidden Lines>") });

            // Topography
            list.Add(new RevitCategory { Bic = "OST_Topography", DisplayName = "Topography",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_Topography", false, "<Hidden Lines>", "Boundary Point", "Interior Point", "Primary Contours", "Secondary Contours", "Triangulation Edges") });

            // Topsoil
            list.Add(new RevitCategory { Bic = "OST_Toposolid", DisplayName = "Topsoil",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_Toposolid", false, "<Hidden Lines>", "Common Edges", "Folding Lines", "Primary Contours", "Secondary Contours", "Split Lines") });

            // Vertical Circulation
            list.Add(new RevitCategory { Bic = "OST_VerticalCirculation", DisplayName = "Vertical Circulation",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_VerticalCirculation", true, "<Hidden Lines>") });

            // Walls
            list.Add(new RevitCategory { Bic = "OST_Walls", DisplayName = "Walls",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Walls", true, "Common Edges", "<Hidden Lines>", "Wall Sweep - Cornice") });

            // Windows
            list.Add(new RevitCategory { Bic = "OST_Windows", DisplayName = "Windows",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_Windows", true, "Elevation Swing", "Frame/Mullion", "Glass", "<Hidden Lines>", "Moulding", "Opening", "Plan Swing", "Sill/Head", "Trim") });

            // Wires
            list.Add(new RevitCategory { Bic = "OST_Wire", DisplayName = "Wires",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_Wire", false, "Home Run Arrows", "Wire Tick Marks") });

            // ── Phase 137 expansion — categories visible in Revit's VG dialog
            //    that weren't in the original catalogue. Live read from
            //    doc.Settings.Categories at runtime still picks up the rest. ──

            list.Add(new RevitCategory { Bic = "OST_DuctTerminal", DisplayName = "Air Terminals",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true,
                SubCategories = Subs("OST_DuctTerminal", true, "Clearance Zones", "Connection Zones", "Maintenance Zones") });

            list.Add(new RevitCategory { Bic = "OST_Lines", DisplayName = "Lines",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_Lines", false, "<Area Boundary>", "<Beyond>", "<Centerline>", "<Demolished>", "<Fabric Envelope>", "<Fabric Sheets>", "<Hidden>", "<Insulation Batting Lines>", "<Lines>", "<Medium Lines>", "<Overhead>", "<Path of Travel Lines>", "<Room Separation>", "<Space Separation>", "<Thin Lines>", "<Wide Lines>", "Lines Modelo Rejilla Conti...", "MEP Hidden") });

            list.Add(new RevitCategory { Bic = "OST_MEPSpaces", DisplayName = "Spaces",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_MEPSpaces", false, "Color Fill", "Interior", "Reference") });

            list.Add(new RevitCategory { Bic = "OST_Planting", DisplayName = "Planting",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true,
                SubCategories = Subs("OST_Planting", false, "<Hidden Lines>") });

            list.Add(new RevitCategory { Bic = "OST_MechanicalEquipmentSet", DisplayName = "Mechanical Equipment Set",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = true });

            list.Add(new RevitCategory { Bic = "OST_PlumbingFixtures", DisplayName = "Plumbing Fixtures (extra subcats)",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = true });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionPlates", DisplayName = "Structural Connection Plates",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionAnchors", DisplayName = "Structural Connection Anchors",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionBolts", DisplayName = "Structural Connection Bolts",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionShearStuds", DisplayName = "Structural Connection Shear Studs",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionHoles", DisplayName = "Structural Connection Holes",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionWelds", DisplayName = "Structural Connection Welds",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionProfiles", DisplayName = "Structural Connection Profiles",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionModifiers", DisplayName = "Structural Connection Modifiers",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_StructConnectionOthers", DisplayName = "Structural Connection Others",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_RasterImages", DisplayName = "Raster Images",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_RoomTags", DisplayName = "Room Tags",
                HasCutLines = false, HasHalftone = false, HasDetailLevel = false, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_SitePropertyLines", DisplayName = "Property Lines",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false,
                SubCategories = Subs("OST_SitePropertyLines", false, "Stripe", "Survey Point") });

            list.Add(new RevitCategory { Bic = "OST_ProjectBasePoint", DisplayName = "Project Base Point",
                HasCutLines = false, HasHalftone = false, HasDetailLevel = false, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_SharedBasePoint", DisplayName = "Survey Point",
                HasCutLines = false, HasHalftone = false, HasDetailLevel = false, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_Hardscape", DisplayName = "Hardscape",
                HasCutLines = true, HasHalftone = true, HasDetailLevel = true, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_Roads_Beds", DisplayName = "Road Beds",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_TopographyContours", DisplayName = "Topography Contours",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_AnalyticalNodes", DisplayName = "Analytical Nodes",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_AnalyticalMember", DisplayName = "Analytical Members",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });

            list.Add(new RevitCategory { Bic = "OST_AnalyticalPanel", DisplayName = "Analytical Panels",
                HasCutLines = false, HasHalftone = true, HasDetailLevel = false, IsTaggable = false });
        }
    }
}
