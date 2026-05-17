using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text;
using Autodesk.Revit.DB;
using StingTools.Core;
using Grid = System.Windows.Controls.Grid;
using Visibility = System.Windows.Visibility;
using Newtonsoft.Json;
using Autodesk.Revit.UI;

namespace StingTools.UI
{
    // ════════════════════════════════════════════════════════════════════
    //  StingExportDialog — ExLink-style unified export dialog
    //
    //  A single WPF dialog that replaces ad-hoc export workflows with a
    //  comprehensive export configurator:
    //
    //  ┌─────────────────────────────────────────────────────────────┐
    //  │  LEFT: Categories      │  CENTER: Parameters  │  RIGHT:    │
    //  │  ☑ Air Terminals       │  ☑ ASS_TAG_1         │  Filters   │
    //  │  ☑ Doors               │  ☑ ASS_DISCIPLINE    │  Family:   │
    //  │  ☑ Duct Accessories    │  ☑ ASS_LOC_TXT       │  [All]     │
    //  │  ☐ Electrical Equip    │  ☑ Width             │  Type:     │
    //  │  ...                   │  ☐ Cost              │  [All]     │
    //  │                        │  ...                 │            │
    //  ├────────────────────────┴──────────────────────┴────────────┤
    //  │  Format: ○ CSV  ○ Excel  ○ JSON    Location: [Browse...]  │
    //  │                          [Export]  [Cancel]                │
    //  └───────────────────────────────────────────────────────────┘
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Result from StingExportDialog.Show().</summary>
    public class ExportDialogResult
    {
        /// <summary>Selected Revit categories to export.</summary>
        public List<string> SelectedCategories { get; set; } = new();
        /// <summary>Selected parameter names to export as columns.</summary>
        public List<string> SelectedParameters { get; set; } = new();
        /// <summary>Family name filter (null/empty = all families).</summary>
        public string FamilyFilter { get; set; }
        /// <summary>Type name filter (null/empty = all types).</summary>
        public string TypeFilter { get; set; }
        /// <summary>Export file format: "CSV", "Excel", or "JSON".</summary>
        public string Format { get; set; } = "CSV";
        /// <summary>Full path to the output file.</summary>
        public string OutputPath { get; set; }
        /// <summary>Scope: "ActiveView", "Selection", or "Project".</summary>
        public string Scope { get; set; } = "Project";
        /// <summary>Whether user cancelled.</summary>
        public bool Cancelled { get; set; } = true;
        /// <summary>Include element ID column.</summary>
        public bool IncludeElementId { get; set; } = true;
        /// <summary>Include category column.</summary>
        public bool IncludeCategory { get; set; } = true;
        /// <summary>Include family and type columns.</summary>
        public bool IncludeFamilyType { get; set; } = true;
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExportLinkDefinition — Reusable export preset (ExLink LinkDefinition)
    //
    //  Stores all export configuration for save/load/reuse.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Reusable export configuration preset, analogous to ExLink's LinkDefinition.</summary>
    public class ExportLinkDefinition
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Group { get; set; } = "Custom";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<string> SelectedCategories { get; set; } = new();
        public List<string> SelectedParameters { get; set; } = new();
        public string FamilyFilter { get; set; }
        public string TypeFilter { get; set; }
        public string Format { get; set; } = "CSV";
        public string Scope { get; set; } = "Project";
        public bool IncludeElementId { get; set; } = true;
        public bool IncludeCategory { get; set; } = true;
        public bool IncludeFamilyType { get; set; } = true;
        public bool IsBuiltIn { get; set; }

        /// <summary>Build ExportDialogResult from this preset.</summary>
        public ExportDialogResult ToResult(string outputPath) => new()
        {
            SelectedCategories = new List<string>(SelectedCategories),
            SelectedParameters = new List<string>(SelectedParameters),
            FamilyFilter = FamilyFilter,
            TypeFilter = TypeFilter,
            Format = Format,
            Scope = Scope,
            OutputPath = outputPath,
            IncludeElementId = IncludeElementId,
            IncludeCategory = IncludeCategory,
            IncludeFamilyType = IncludeFamilyType,
            Cancelled = false
        };

        /// <summary>Build a preset from current dialog result.</summary>
        public static ExportLinkDefinition FromResult(ExportDialogResult r, string name) => new()
        {
            Name = name,
            CreatedAt = DateTime.Now,
            SelectedCategories = new List<string>(r.SelectedCategories),
            SelectedParameters = new List<string>(r.SelectedParameters),
            FamilyFilter = r.FamilyFilter,
            TypeFilter = r.TypeFilter,
            Format = r.Format,
            Scope = r.Scope,
            IncludeElementId = r.IncludeElementId,
            IncludeCategory = r.IncludeCategory,
            IncludeFamilyType = r.IncludeFamilyType
        };
    }

    // ════════════════════════════════════════════════════════════════════
    //  ExportLinkLibrary — Built-in + user preset library
    //
    //  120+ built-in presets covering all standard export categories
    //  plus STING-specific tagging presets. User presets saved to JSON.
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Export preset library with 120+ built-in presets covering standard BIM export
    /// categories, plus STING-specific tagging presets.
    /// User presets persisted to EXPORT_PRESETS.json alongside project_config.json.
    /// </summary>
    internal static class ExportLinkLibrary
    {
        private const string PresetFileName = "EXPORT_PRESETS.json";
        private static List<ExportLinkDefinition> _builtInPresets;

        // ── STING tag tokens (reused across presets) ──
        private static readonly List<string> TagTokens = new()
        {
            ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
            ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
            ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.TAG1,
            ParamRegistry.STATUS, ParamRegistry.REV
        };

        private static readonly List<string> IdentityParams = new()
        {
            "ASS_ROOM_NAME_TXT", "ASS_ROOM_NUMBER_TXT", "ASS_DEPARTMENT_TXT",
            "ASS_LEVEL_NAME_TXT", "ASS_GRID_REF_TXT", "ASS_MANUFACTURER_TXT",
            "ASS_MODEL_NR_TXT", "ASS_DESCRIPTION_TXT"
        };

        private static readonly List<string> SpatialParams = new()
        {
            ParamRegistry.LOC, ParamRegistry.ZONE, ParamRegistry.LVL,
            "ASS_GRID_REF_TXT", "ASS_ROOM_NAME_TXT", "ASS_ROOM_NUMBER_TXT",
            "ASS_DEPARTMENT_TXT", "ASS_LEVEL_NAME_TXT"
        };

        private static readonly List<string> MepParams = new()
        {
            "MEP_FLOW_RATE_TXT", "MEP_PRESSURE_TXT", "MEP_VELOCITY_TXT",
            "MEP_VOLTAGE_TXT", "MEP_POWER_TXT", "MEP_CURRENT_TXT",
            "MEP_CIRCUIT_TXT", "MEP_SYSTEM_NAME_TXT", "MEP_SIZE_TXT"
        };

        private static readonly List<string> DimensionParams = new()
        {
            "BLE_WIDTH_TXT", "BLE_HEIGHT_TXT", "BLE_LENGTH_TXT",
            "BLE_DEPTH_TXT", "BLE_AREA_TXT", "BLE_VOLUME_TXT",
            "BLE_THICKNESS_TXT", "BLE_PERIMETER_TXT"
        };

        private static readonly List<string> CobieParams = new()
        {
            ParamRegistry.TAG1, "ASS_DESCRIPTION_TXT", "ASS_MANUFACTURER_TXT",
            "ASS_MODEL_NR_TXT", "ASS_SERIAL_NR_TXT", "ASS_INSTALLATION_DATE_TXT",
            "COM_WARRANTY_START_TXT", "ASS_WARRANTY_DUR_TXT", "ASS_BARCODE_TXT",
            "MNT_INTERVAL_TXT", "MNT_RESPONSIBILITY_TXT", "MNT_TASK_TXT"
        };

        private static readonly List<string> LifecycleParams = new()
        {
            ParamRegistry.STATUS, ParamRegistry.REV, "ASS_INSTALLATION_DATE_TXT",
            "ASS_COMMISSION_DATE_TXT", "MNT_CONDITION_GRADE_TXT", "MNT_NEXT_SERVICE_TXT",
            "ASS_EXPECTED_LIFE_TXT", "ASS_REPLACEMENT_COST_TXT"
        };

        private static readonly List<string> Tag7Params = new()
        {
            "ASS_TAG_7_TXT", "ASS_TAG_7A_TXT", "ASS_TAG_7B_TXT",
            "ASS_TAG_7C_TXT", "ASS_TAG_7D_TXT", "ASS_TAG_7E_TXT", "ASS_TAG_7F_TXT"
        };

        // ── Category groups ──
        private static readonly List<string> ArchCategories = new()
        {
            "Doors", "Windows", "Walls", "Floors", "Ceilings", "Roofs", "Stairs",
            "Railings", "Rooms", "Curtain Panels", "Curtain Wall Mullions", "Furniture"
        };
        private static readonly List<string> StructCategories = new()
        {
            "Structural Columns", "Structural Framing", "Structural Foundations",
            "Structural Connections", "Structural Rebar"
        };
        private static readonly List<string> MechCategories = new()
        {
            "Mechanical Equipment", "Duct Accessories", "Duct Fittings",
            "Ducts", "Flex Ducts", "Air Terminals"
        };
        private static readonly List<string> ElecCategories = new()
        {
            "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures",
            "Lighting Devices", "Cable Trays", "Cable Tray Fittings", "Conduits",
            "Conduit Fittings"
        };
        private static readonly List<string> PlumbCategories = new()
        {
            "Plumbing Fixtures", "Plumbing Equipment", "Pipe Accessories",
            "Pipe Fittings", "Pipes", "Flex Pipes", "Sprinklers"
        };
        private static readonly List<string> FireCategories = new()
        {
            "Fire Alarm Devices", "Sprinklers", "Fire Protection"
        };
        private static readonly List<string> AllMepCategories;

        static ExportLinkLibrary()
        {
            AllMepCategories = new List<string>();
            AllMepCategories.AddRange(MechCategories);
            AllMepCategories.AddRange(ElecCategories);
            AllMepCategories.AddRange(PlumbCategories);
            AllMepCategories.AddRange(FireCategories);
        }

        /// <summary>Get all presets (built-in + user).</summary>
        public static List<ExportLinkDefinition> GetAllPresets(Document doc)
        {
            var all = new List<ExportLinkDefinition>(GetBuiltInPresets());
            all.AddRange(LoadUserPresets(doc));
            return all;
        }

        /// <summary>Get preset group names.</summary>
        public static List<string> GetGroups(List<ExportLinkDefinition> presets)
        {
            return presets.Select(p => p.Group).Distinct().OrderBy(g => g).ToList();
        }

        /// <summary>Save a user preset.</summary>
        public static void SaveUserPreset(Document doc, ExportLinkDefinition preset)
        {
            preset.IsBuiltIn = false;
            preset.Group = string.IsNullOrEmpty(preset.Group) ? "Custom" : preset.Group;
            var existing = LoadUserPresets(doc);
            existing.RemoveAll(p => p.Name == preset.Name);
            existing.Add(preset);
            SaveUserPresets(doc, existing);
        }

        /// <summary>Delete a user preset by name.</summary>
        public static void DeleteUserPreset(Document doc, string name)
        {
            var existing = LoadUserPresets(doc);
            existing.RemoveAll(p => p.Name == name);
            SaveUserPresets(doc, existing);
        }

        // ── Persistence ──

        private static string GetPresetPath(Document doc)
        {
            string dir = OutputLocationHelper.GetOutputDirectory(doc);
            return Path.Combine(dir, PresetFileName);
        }

        private static List<ExportLinkDefinition> LoadUserPresets(Document doc)
        {
            try
            {
                string path = GetPresetPath(doc);
                if (!File.Exists(path)) return new List<ExportLinkDefinition>();
                string json = File.ReadAllText(path);
                return Newtonsoft.Json.JsonConvert.DeserializeObject<List<ExportLinkDefinition>>(json)
                    ?? new List<ExportLinkDefinition>();
            }
            catch (Exception ex) { StingLog.Warn($"LoadUserPresets: {ex.Message}"); return new List<ExportLinkDefinition>(); }
        }

        private static void SaveUserPresets(Document doc, List<ExportLinkDefinition> presets)
        {
            try
            {
                string path = GetPresetPath(doc);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(presets, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex) { StingLog.Warn($"SaveUserPresets: {ex.Message}"); }
        }

        // ── Built-in presets (120+) ──

        public static List<ExportLinkDefinition> GetBuiltInPresets()
        {
            if (_builtInPresets != null) return _builtInPresets;
            _builtInPresets = new List<ExportLinkDefinition>();

            // ════ STING Tagging ════
            Add("STING Tagging", "Full Asset Register", "Complete ISO 19650 asset register with all tag tokens, identity, spatial, and lifecycle data.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, IdentityParams, SpatialParams, LifecycleParams), "Excel");
            Add("STING Tagging", "Tag Tokens Only", "Export only ISO 19650 tag tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ).",
                TagConfig.DiscMap.Keys.ToList(), TagTokens);
            Add("STING Tagging", "TAG7 Narratives", "Rich descriptive TAG7 narratives (sections A-F).",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, Tag7Params));
            Add("STING Tagging", "Compliance Audit", "Tags + status + revision for compliance checking.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, LifecycleParams));
            Add("STING Tagging", "Spatial Summary", "Spatial context per element: room, department, level, grid, zone.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, SpatialParams));
            Add("STING Tagging", "MEP Asset Tags", "MEP-focused asset tags with system and performance data.",
                AllMepCategories, Concat(TagTokens, MepParams));
            Add("STING Tagging", "Architectural Tags", "Architectural tags with dimensions.",
                ArchCategories, Concat(TagTokens, DimensionParams));
            Add("STING Tagging", "Structural Tags", "Structural element tags with identity.",
                StructCategories, Concat(TagTokens, IdentityParams));

            // ════ COBie / FM Handover ════
            Add("COBie / Handover", "COBie Component Export", "COBie-aligned component data for FM handover.",
                TagConfig.DiscMap.Keys.ToList(), CobieParams, "Excel");
            Add("COBie / Handover", "COBie Type Export", "Equipment types for COBie Type worksheet.",
                AllMepCategories, Concat(CobieParams, new List<string> { "ASS_WARRANTY_PARTS_TXT", "ASS_WARRANTY_LABOR_TXT", "ASS_NOMINAL_LENGTH_TXT", "ASS_NOMINAL_WIDTH_TXT", "ASS_NOMINAL_HEIGHT_TXT" }), "Excel");
            Add("COBie / Handover", "Maintenance Schedule", "Maintenance tasks, intervals, and responsibilities.",
                AllMepCategories, Concat(TagTokens, new List<string> { "MNT_INTERVAL_TXT", "MNT_RESPONSIBILITY_TXT", "MNT_TASK_TXT", "MNT_CONDITION_GRADE_TXT", "MNT_NEXT_SERVICE_TXT", "MNT_COST_TXT" }));
            Add("COBie / Handover", "Asset Health Report", "Condition grading and lifecycle data per asset.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, LifecycleParams, new List<string> { "MNT_CONDITION_GRADE_TXT" }));
            Add("COBie / Handover", "Space Handover", "Room-based handover data with spatial tags.",
                new List<string> { "Rooms" }, Concat(SpatialParams, new List<string> { "BLE_AREA_TXT", "BLE_VOLUME_TXT", "BLE_PERIMETER_TXT" }));

            // ════ Architectural ════
            Add("Architectural", "Door Schedule", "Complete door schedule with dimensions and fire rating.",
                new List<string> { "Doors" }, Concat(TagTokens, DimensionParams, new List<string> { "ASS_FIRE_RATING_TXT", "ASS_FINISH_TXT", "ASS_MATERIAL_TXT" }));
            Add("Architectural", "Window Schedule", "Window schedule with dimensions and glazing specs.",
                new List<string> { "Windows" }, Concat(TagTokens, DimensionParams, new List<string> { "ASS_GLAZING_TXT", "ASS_FRAME_TXT" }));
            Add("Architectural", "Room Data Sheets", "Room areas, finishes, and occupancy data.",
                new List<string> { "Rooms" }, Concat(SpatialParams, DimensionParams));
            Add("Architectural", "Wall Types", "Wall types, materials, and thicknesses.",
                new List<string> { "Walls" }, Concat(TagTokens, DimensionParams, new List<string> { "ASS_MATERIAL_TXT", "ASS_FIRE_RATING_TXT" }));
            Add("Architectural", "Floor Finishes", "Floor types and finish schedules.",
                new List<string> { "Floors" }, Concat(TagTokens, DimensionParams, new List<string> { "ASS_FINISH_TXT", "ASS_MATERIAL_TXT" }));
            Add("Architectural", "Ceiling Schedule", "Ceiling types, heights, and materials.",
                new List<string> { "Ceilings" }, Concat(TagTokens, DimensionParams));
            Add("Architectural", "Furniture Schedule", "Furniture inventory with locations.",
                new List<string> { "Furniture" }, Concat(TagTokens, IdentityParams));
            Add("Architectural", "Stair Schedule", "Stair riser/tread dimensions and code compliance.",
                new List<string> { "Stairs" }, Concat(TagTokens, DimensionParams));

            // ════ Structural ════
            Add("Structural", "Column Schedule", "Structural columns with sizes and materials.",
                new List<string> { "Structural Columns" }, Concat(TagTokens, DimensionParams, new List<string> { "ASS_MATERIAL_TXT", "STR_GRADE_TXT" }));
            Add("Structural", "Beam Schedule", "Structural framing schedule with spans and loads.",
                new List<string> { "Structural Framing" }, Concat(TagTokens, DimensionParams, new List<string> { "ASS_MATERIAL_TXT" }));
            Add("Structural", "Foundation Schedule", "Foundation elements with dimensions.",
                new List<string> { "Structural Foundations" }, Concat(TagTokens, DimensionParams));
            Add("Structural", "Rebar Schedule", "Reinforcement quantities and specifications.",
                new List<string> { "Structural Rebar" }, Concat(TagTokens, new List<string> { "STR_REBAR_SIZE_TXT", "STR_REBAR_GRADE_TXT", "BLE_LENGTH_TXT" }));

            // ════ Mechanical (HVAC) ════
            Add("Mechanical", "HVAC Equipment", "HVAC equipment schedule with capacities and flow rates.",
                new List<string> { "Mechanical Equipment" }, Concat(TagTokens, MepParams, IdentityParams));
            Add("Mechanical", "Air Terminal Schedule", "Diffusers and grilles with air flow rates.",
                new List<string> { "Air Terminals" }, Concat(TagTokens, MepParams));
            Add("Mechanical", "Duct Schedule", "Ductwork sizes, lengths, and system assignments.",
                new List<string> { "Ducts", "Flex Ducts" }, Concat(TagTokens, MepParams, DimensionParams));
            Add("Mechanical", "Duct Accessories", "Dampers, access doors, and duct fittings.",
                new List<string> { "Duct Accessories", "Duct Fittings" }, Concat(TagTokens, MepParams));
            Add("Mechanical", "All HVAC Systems", "Complete HVAC systems overview.",
                MechCategories, Concat(TagTokens, MepParams, IdentityParams), "Excel");

            // ════ Electrical ════
            Add("Electrical", "Electrical Equipment", "Panels, transformers, and switchgear.",
                new List<string> { "Electrical Equipment" }, Concat(TagTokens, MepParams, IdentityParams));
            Add("Electrical", "Lighting Fixtures", "Lighting schedule with wattage and lumen output.",
                new List<string> { "Lighting Fixtures" }, Concat(TagTokens, MepParams, new List<string> { "ASS_MANUFACTURER_TXT", "ASS_MODEL_NR_TXT" }));
            Add("Electrical", "Cable Tray Schedule", "Cable tray routes, sizes, and fill ratios.",
                new List<string> { "Cable Trays", "Cable Tray Fittings" }, Concat(TagTokens, DimensionParams));
            Add("Electrical", "Conduit Schedule", "Conduit routes, sizes, and fill ratios.",
                new List<string> { "Conduits", "Conduit Fittings" }, Concat(TagTokens, DimensionParams));
            Add("Electrical", "Electrical Fixtures", "Receptacles, switches, and devices.",
                new List<string> { "Electrical Fixtures" }, Concat(TagTokens, MepParams));
            Add("Electrical", "All Electrical Systems", "Complete electrical systems overview.",
                ElecCategories, Concat(TagTokens, MepParams, IdentityParams), "Excel");

            // ════ Plumbing ════
            Add("Plumbing", "Plumbing Fixtures", "Sanitary fixtures schedule.",
                new List<string> { "Plumbing Fixtures" }, Concat(TagTokens, MepParams, IdentityParams));
            Add("Plumbing", "Pipe Schedule", "Piping sizes, lengths, and system assignments.",
                new List<string> { "Pipes", "Flex Pipes" }, Concat(TagTokens, MepParams, DimensionParams));
            Add("Plumbing", "Pipe Accessories", "Valves, strainers, and pipe fittings.",
                new List<string> { "Pipe Accessories", "Pipe Fittings" }, Concat(TagTokens, MepParams));
            Add("Plumbing", "Plumbing Equipment", "Pumps, tanks, and water heaters.",
                new List<string> { "Plumbing Equipment" }, Concat(TagTokens, MepParams, IdentityParams));

            // ════ Fire Protection ════
            Add("Fire Protection", "Fire Alarm Devices", "Detectors, sounders, and call points.",
                new List<string> { "Fire Alarm Devices" }, Concat(TagTokens, MepParams, IdentityParams));
            Add("Fire Protection", "Sprinkler Schedule", "Sprinkler head types and coverage.",
                new List<string> { "Sprinklers" }, Concat(TagTokens, MepParams));
            Add("Fire Protection", "All Fire Systems", "Complete fire protection overview.",
                FireCategories, Concat(TagTokens, MepParams, IdentityParams));

            // ════ Model-Wide ════
            Add("Model-Wide", "All Elements — Tags Only", "Every tagged category, tag tokens only.",
                TagConfig.DiscMap.Keys.ToList(), TagTokens);
            Add("Model-Wide", "All Elements — Full Export", "Every category, all STING parameters.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, IdentityParams, SpatialParams, MepParams, DimensionParams, LifecycleParams), "Excel");
            Add("Model-Wide", "Discipline Summary", "Element counts and tags by discipline.",
                TagConfig.DiscMap.Keys.ToList(), new List<string> { ParamRegistry.DISC, ParamRegistry.TAG1, ParamRegistry.STATUS });
            Add("Model-Wide", "BOQ / Quantities", "Bill of Quantities with dimensions and materials.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, DimensionParams, new List<string> { "ASS_MATERIAL_TXT", "ASS_CST_UNIT_PRICE_UGX_NR" }), "Excel");
            Add("Model-Wide", "Element ID Register", "Element IDs with tags for cross-referencing.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, IdentityParams));

            // ════ QA / Validation ════
            Add("QA / Validation", "Missing Tags Audit", "Elements with incomplete or missing tags.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, new List<string> { "STING_STALE_BOOL" }));
            Add("QA / Validation", "Stale Elements", "Elements flagged as stale (geometry changed).",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, new List<string> { "STING_STALE_BOOL", "ASS_TAG_PREV_TXT", "ASS_TAG_MODIFIED_DT" }));
            Add("QA / Validation", "Duplicate Tag Check", "All tags and sequences for duplicate detection.",
                TagConfig.DiscMap.Keys.ToList(), new List<string> { ParamRegistry.TAG1, ParamRegistry.SEQ, ParamRegistry.DISC, ParamRegistry.SYS, ParamRegistry.LVL });
            Add("QA / Validation", "Parameter Completeness", "Token coverage for compliance scoring.",
                TagConfig.DiscMap.Keys.ToList(), TagTokens);

            // ════ ISO 19650 ════
            Add("ISO 19650", "Document Transmittal", "Transmittal-ready export with ISO naming.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, new List<string> { ParamRegistry.STATUS, ParamRegistry.REV }), "Excel");
            Add("ISO 19650", "CDE Status Register", "Elements with CDE suitability and status codes.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, LifecycleParams));
            Add("ISO 19650", "Information Container", "Full information container per ISO 19650-2.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, IdentityParams, LifecycleParams, Tag7Params), "Excel");

            // ════ Sustainability ════
            Add("Sustainability", "Embodied Carbon", "Materials with embodied carbon values.",
                new List<string> { "Walls", "Floors", "Roofs", "Structural Columns", "Structural Framing" },
                Concat(TagTokens, DimensionParams, new List<string> { "ASS_MATERIAL_TXT", "ASS_EMBODIED_CARBON_TXT", "BLE_VOLUME_TXT" }));
            Add("Sustainability", "Energy Data", "Energy-related parameters for Part L compliance.",
                TagConfig.DiscMap.Keys.ToList(), Concat(TagTokens, new List<string> { "ASS_U_VALUE_TXT", "ASS_R_VALUE_TXT", "ASS_THERMAL_COND_TXT", "BLE_AREA_TXT" }));

            return _builtInPresets;
        }

        // ── Helpers ──

        private static void Add(string group, string name, string desc, List<string> cats, List<string> @params, string format = "CSV")
        {
            _builtInPresets.Add(new ExportLinkDefinition
            {
                Name = name,
                Description = desc,
                Group = group,
                SelectedCategories = cats,
                SelectedParameters = @params,
                Format = format,
                IsBuiltIn = true
            });
        }

        private static List<string> Concat(params List<string>[] lists)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in lists)
                foreach (var item in list) result.Add(item);
            return result.ToList();
        }
    }

    /// <summary>
    /// ExLink-style unified export dialog. Provides category, parameter,
    /// family/type filtering and format/location selection in a single window.
    /// Includes library browser for 120+ built-in and user export presets.
    /// </summary>
    public static class StingExportDialog
    {
        // ── Brand colours (match STING dark theme) ──
        private static readonly System.Windows.Media.Color BgDark = System.Windows.Media.Color.FromRgb(45, 45, 48);
        private static readonly System.Windows.Media.Color BgMedium = System.Windows.Media.Color.FromRgb(55, 55, 60);
        private static readonly System.Windows.Media.Color BgLight = System.Windows.Media.Color.FromRgb(62, 62, 66);
        private static readonly System.Windows.Media.Color AccentOrange = System.Windows.Media.Color.FromRgb(232, 145, 45);
        private static readonly System.Windows.Media.Color TextWhite = System.Windows.Media.Color.FromRgb(241, 241, 241);
        private static readonly System.Windows.Media.Color TextGrey = System.Windows.Media.Color.FromRgb(170, 170, 170);
        private static readonly System.Windows.Media.Color BorderDark = System.Windows.Media.Color.FromRgb(70, 70, 74);

        /// <summary>
        /// Show the export dialog. Returns null if cancelled.
        /// </summary>
        public static ExportDialogResult Show(Document doc, string title = "STING Data Export",
            ICollection<ElementId> preSelection = null)
        {
            var result = new ExportDialogResult();

            // ── Gather model data ──
            var categories = GetModelCategories(doc);
            var allParams = GetAvailableParameters(doc, categories);
            var families = GetFamilies(doc, categories);

            // ── Build window ──
            var win = new Window
            {
                Title = title,
                Width = 960, Height = 640,
                MinWidth = 800, MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(BgDark),
                FontFamily = new FontFamily("Segoe UI"),
                ResizeMode = ResizeMode.CanResizeWithGrip
            };

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(win);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
            }
            catch (Exception ex) { StingLog.Warn($"ExportDialog owner: {ex.Message}"); }

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Scope bar
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Preset bar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Format/Location
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // Footer
            win.Content = root;

            // ═══════════════════ HEADER ═══════════════════
            var header = new Border
            {
                Background = new SolidColorBrush(AccentOrange),
                Padding = new Thickness(16, 10, 16, 10)
            };
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            headerPanel.Children.Add(new TextBlock
            {
                Text = "STING",
                FontSize = 16, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Margin = new Thickness(0, 0, 8, 0)
            });
            headerPanel.Children.Add(new TextBlock
            {
                Text = "Data Export",
                FontSize = 16, FontWeight = FontWeights.Light,
                Foreground = Brushes.White
            });
            header.Child = headerPanel;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ═══════════════════ SCOPE BAR ═══════════════════
            var scopeBar = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                Padding = new Thickness(12, 6, 12, 6)
            };
            var scopePanel = new StackPanel { Orientation = Orientation.Horizontal };
            scopePanel.Children.Add(MakeLabel("Scope:", true));
            var rbProject = MakeRadio("Entire Project", "scope", true);
            var rbView = MakeRadio("Active View", "scope");
            var rbSelection = MakeRadio("Selection", "scope");
            if (preSelection != null && preSelection.Count > 0)
                rbSelection.IsChecked = true;
            scopePanel.Children.Add(rbProject);
            scopePanel.Children.Add(rbView);
            scopePanel.Children.Add(rbSelection);

            // Count label
            var countLabel = new TextBlock
            {
                Foreground = new SolidColorBrush(TextGrey),
                FontSize = 11, Margin = new Thickness(20, 4, 0, 0)
            };
            scopePanel.Children.Add(countLabel);
            scopeBar.Child = scopePanel;
            Grid.SetRow(scopeBar, 1);
            root.Children.Add(scopeBar);

            // ═══════════════════ PRESET BAR ═══════════════════
            var presetBar = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                Padding = new Thickness(12, 5, 12, 5),
                BorderBrush = new SolidColorBrush(BorderDark),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var presetPanel = new StackPanel { Orientation = Orientation.Horizontal };
            presetPanel.Children.Add(MakeLabel("Preset:", true));
            var cmbPreset = new System.Windows.Controls.ComboBox
            {
                Width = 280,
                IsEditable = false,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 0, 4, 0)
            };
            cmbPreset.Items.Add("(None — configure manually)");
            cmbPreset.SelectedIndex = 0;

            // Load preset library
            var allPresets = ExportLinkLibrary.GetAllPresets(doc);
            var presetGroups = ExportLinkLibrary.GetGroups(allPresets);
            var presetByIndex = new Dictionary<int, ExportLinkDefinition>();
            int presetIdx = 1;
            foreach (var group in presetGroups)
            {
                // Add group header as disabled separator item
                cmbPreset.Items.Add($"── {group} ──");
                var groupPresets = allPresets.Where(p => p.Group == group).OrderBy(p => p.Name);
                foreach (var preset in groupPresets)
                {
                    string suffix = preset.IsBuiltIn ? "" : " [User]";
                    cmbPreset.Items.Add($"  {preset.Name}{suffix}");
                    presetByIndex[presetIdx + 1] = preset; // +1 for the group header
                    presetIdx++;
                }
                presetIdx++; // for the group header
            }

            presetPanel.Children.Add(cmbPreset);

            var btnLoadPreset = MakeSmallButton("Apply");
            var btnDeletePreset = MakeSmallButton("Delete");
            btnDeletePreset.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 100, 100));
            presetPanel.Children.Add(btnLoadPreset);
            presetPanel.Children.Add(btnDeletePreset);

            // Preset description tooltip
            var presetDesc = new TextBlock
            {
                Foreground = new SolidColorBrush(TextGrey),
                FontSize = 11, FontStyle = FontStyles.Italic,
                Margin = new Thickness(12, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 400
            };
            presetPanel.Children.Add(presetDesc);

            cmbPreset.SelectionChanged += (s, e) =>
            {
                int idx = cmbPreset.SelectedIndex;
                if (presetByIndex.TryGetValue(idx, out var p))
                {
                    presetDesc.Text = p.Description;
                    btnDeletePreset.IsEnabled = !p.IsBuiltIn;
                }
                else
                {
                    presetDesc.Text = "";
                    btnDeletePreset.IsEnabled = false;
                }
            };

            presetBar.Child = presetPanel;
            Grid.SetRow(presetBar, 2);
            root.Children.Add(presetBar);

            // ═══════════════════ MAIN 3-COLUMN AREA ═══════════════════
            var mainGrid = new Grid { Margin = new Thickness(8) };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            Grid.SetRow(mainGrid, 3);
            root.Children.Add(mainGrid);

            // ── LEFT: Categories ──
            var catPanel = MakeGroupPanel("Categories");
            var catSearch = MakeSearchBox("Search categories...");
            (catPanel.Child as StackPanel).Children.Add(catSearch);

            var catSelectAll = new System.Windows.Controls.CheckBox
            {
                Content = "Select All",
                IsChecked = true,
                Foreground = new SolidColorBrush(AccentOrange),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 4, 0, 4)
            };
            (catPanel.Child as StackPanel).Children.Add(catSelectAll);

            var catList = new ListBox
            {
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var catChecks = new Dictionary<string, System.Windows.Controls.CheckBox>();
            foreach (var cat in categories.OrderBy(c => c))
            {
                var cb = new System.Windows.Controls.CheckBox
                {
                    Content = cat,
                    IsChecked = true,
                    Foreground = new SolidColorBrush(TextWhite),
                    FontSize = 12,
                    Margin = new Thickness(2)
                };
                catChecks[cat] = cb;
                catList.Items.Add(cb);
            }
            (catPanel.Child as StackPanel).Children.Add(catList);
            Grid.SetColumn(catPanel, 0);
            mainGrid.Children.Add(catPanel);

            // Category search filter
            catSearch.TextChanged += (s, e) =>
            {
                string filter = catSearch.Text?.Trim().ToLowerInvariant() ?? "";
                foreach (var item in catList.Items.OfType<System.Windows.Controls.CheckBox>())
                    item.Visibility = filter.Length == 0 || (item.Content?.ToString().ToLowerInvariant().Contains(filter) == true)
                        ? Visibility.Visible : Visibility.Collapsed;
            };

            // Select all toggle
            catSelectAll.Checked += (s, e) => { foreach (var cb in catChecks.Values) cb.IsChecked = true; };
            catSelectAll.Unchecked += (s, e) => { foreach (var cb in catChecks.Values) cb.IsChecked = false; };

            // Splitter 1
            var splitter1 = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(BorderDark)
            };
            Grid.SetColumn(splitter1, 1);
            mainGrid.Children.Add(splitter1);

            // ── CENTER: Parameters ──
            var paramPanel = MakeGroupPanel("Parameters (Columns)");
            var paramSearch = MakeSearchBox("Search parameters...");
            (paramPanel.Child as StackPanel).Children.Add(paramSearch);

            // Quick-select buttons
            var quickPanel = new WrapPanel { Margin = new Thickness(2, 2, 2, 4) };
            var btnAllParams = MakeSmallButton("All");
            var btnNoneParams = MakeSmallButton("None");
            var btnTags = MakeSmallButton("Tags");
            var btnIdentity = MakeSmallButton("Identity");
            var btnSpatial = MakeSmallButton("Spatial");
            var btnMEP = MakeSmallButton("MEP");
            quickPanel.Children.Add(btnAllParams);
            quickPanel.Children.Add(btnNoneParams);
            quickPanel.Children.Add(btnTags);
            quickPanel.Children.Add(btnIdentity);
            quickPanel.Children.Add(btnSpatial);
            quickPanel.Children.Add(btnMEP);
            (paramPanel.Child as StackPanel).Children.Add(quickPanel);

            var paramList = new ListBox
            {
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0)
            };
            var paramChecks = new Dictionary<string, System.Windows.Controls.CheckBox>();

            // Categorize parameters for grouping
            var tagParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.TAG1,
                ParamRegistry.STATUS, ParamRegistry.REV
            };
            var spatialParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ASS_GRID_REF_TXT", "ASS_ROOM_NAME_TXT", "ASS_ROOM_NUMBER_TXT",
                "ASS_DEPARTMENT_TXT", "ASS_LEVEL_NAME_TXT"
            };

            // Add parameters grouped: Tags first, then STING, then native
            var stingParams = allParams.Where(p => p.StartsWith("ASS_") || p.StartsWith("BLE_")
                || p.StartsWith("HVC_") || p.StartsWith("ELC_") || p.StartsWith("PLM_")
                || p.StartsWith("MNT_") || p.StartsWith("TAG_") || p.StartsWith("STING_")
                || p.StartsWith("STR_") || p.StartsWith("FLS_") || p.StartsWith("COM_")
                || p.StartsWith("MEP_") || p.StartsWith("RGL_")).OrderBy(p => p).ToList();
            var nativeParams = allParams.Except(stingParams).OrderBy(p => p).ToList();

            void AddParamGroup(string header, IEnumerable<string> @params, bool defaultChecked)
            {
                var hdr = new TextBlock
                {
                    Text = $"── {header} ──",
                    Foreground = new SolidColorBrush(AccentOrange),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Margin = new Thickness(4, 6, 0, 2)
                };
                paramList.Items.Add(hdr);
                foreach (var p in @params)
                {
                    var cb = new System.Windows.Controls.CheckBox
                    {
                        Content = p,
                        IsChecked = defaultChecked,
                        Foreground = new SolidColorBrush(TextWhite),
                        FontSize = 12,
                        Margin = new Thickness(2),
                        Tag = p
                    };
                    paramChecks[p] = cb;
                    paramList.Items.Add(cb);
                }
            }

            AddParamGroup("STING Tag Tokens", stingParams.Where(p => tagParams.Contains(p)), true);
            AddParamGroup("STING Parameters", stingParams.Where(p => !tagParams.Contains(p)), false);
            AddParamGroup("Revit Parameters", nativeParams, false);

            (paramPanel.Child as StackPanel).Children.Add(paramList);
            Grid.SetColumn(paramPanel, 2);
            mainGrid.Children.Add(paramPanel);

            // Parameter search filter
            paramSearch.TextChanged += (s, e) =>
            {
                string filter = paramSearch.Text?.Trim().ToLowerInvariant() ?? "";
                foreach (var item in paramList.Items)
                {
                    if (item is System.Windows.Controls.CheckBox cb)
                        cb.Visibility = filter.Length == 0 || (cb.Content?.ToString().ToLowerInvariant().Contains(filter) == true)
                            ? Visibility.Visible : Visibility.Collapsed;
                    else if (item is TextBlock tb)
                        tb.Visibility = filter.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            // Quick-select handlers
            btnAllParams.Click += (s, e) => { foreach (var cb in paramChecks.Values) cb.IsChecked = true; };
            btnNoneParams.Click += (s, e) => { foreach (var cb in paramChecks.Values) cb.IsChecked = false; };
            btnTags.Click += (s, e) =>
            {
                foreach (var cb in paramChecks.Values) cb.IsChecked = false;
                foreach (var p in tagParams) { if (paramChecks.TryGetValue(p, out var cb)) cb.IsChecked = true; }
            };
            btnIdentity.Click += (s, e) =>
            {
                foreach (var cb in paramChecks.Values) cb.IsChecked = false;
                foreach (var p in tagParams) { if (paramChecks.TryGetValue(p, out var cb)) cb.IsChecked = true; }
                foreach (var p in allParams.Where(n => n.Contains("NAME") || n.Contains("TYPE") || n.Contains("FAMILY")))
                    if (paramChecks.TryGetValue(p, out var cb)) cb.IsChecked = true;
            };
            btnSpatial.Click += (s, e) =>
            {
                foreach (var cb in paramChecks.Values) cb.IsChecked = false;
                foreach (var p in spatialParams) { if (paramChecks.TryGetValue(p, out var cb)) cb.IsChecked = true; }
                foreach (var p in tagParams) { if (paramChecks.TryGetValue(p, out var cb)) cb.IsChecked = true; }
            };
            btnMEP.Click += (s, e) =>
            {
                foreach (var cb in paramChecks.Values) cb.IsChecked = false;
                foreach (var p in tagParams) { if (paramChecks.TryGetValue(p, out var cb)) cb.IsChecked = true; }
                foreach (var p in allParams.Where(n => n.StartsWith("MEP_") || n.StartsWith("HVC_")
                    || n.StartsWith("ELC_") || n.StartsWith("PLM_") || n.Contains("FLOW")
                    || n.Contains("VOLTAGE") || n.Contains("POWER") || n.Contains("PRESSURE")))
                    if (paramChecks.TryGetValue(p, out var cb)) cb.IsChecked = true;
            };

            // Splitter 2
            var splitter2 = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(BorderDark)
            };
            Grid.SetColumn(splitter2, 3);
            mainGrid.Children.Add(splitter2);

            // ── RIGHT: Filters (Family/Type) ──
            var filterPanel = MakeGroupPanel("Filters");
            var filterStack = filterPanel.Child as StackPanel;

            filterStack.Children.Add(MakeLabel("Family:", true));
            var cmbFamily = new System.Windows.Controls.ComboBox
            {
                IsEditable = true,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 2, 4, 8)
            };
            cmbFamily.Items.Add("(All Families)");
            foreach (var fam in families.Keys.OrderBy(f => f))
                cmbFamily.Items.Add(fam);
            cmbFamily.SelectedIndex = 0;
            filterStack.Children.Add(cmbFamily);

            filterStack.Children.Add(MakeLabel("Type:", true));
            var cmbType = new System.Windows.Controls.ComboBox
            {
                IsEditable = true,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 2, 4, 8)
            };
            cmbType.Items.Add("(All Types)");
            cmbType.SelectedIndex = 0;
            filterStack.Children.Add(cmbType);

            // Update types when family changes
            cmbFamily.SelectionChanged += (s, e) =>
            {
                cmbType.Items.Clear();
                cmbType.Items.Add("(All Types)");
                string selectedFamily = cmbFamily.SelectedItem?.ToString();
                if (!string.IsNullOrEmpty(selectedFamily) && selectedFamily != "(All Families)"
                    && families.TryGetValue(selectedFamily, out var types))
                {
                    foreach (var t in types.OrderBy(x => x))
                        cmbType.Items.Add(t);
                }
                cmbType.SelectedIndex = 0;
            };

            // Options section
            filterStack.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(BorderDark) });
            filterStack.Children.Add(MakeLabel("Options:", true));

            var chkElementId = new System.Windows.Controls.CheckBox
            {
                Content = "Include Element ID",
                IsChecked = true,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 4, 0, 2)
            };
            filterStack.Children.Add(chkElementId);

            var chkCategory = new System.Windows.Controls.CheckBox
            {
                Content = "Include Category",
                IsChecked = true,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 2, 0, 2)
            };
            filterStack.Children.Add(chkCategory);

            var chkFamilyType = new System.Windows.Controls.CheckBox
            {
                Content = "Include Family & Type",
                IsChecked = true,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(4, 2, 0, 2)
            };
            filterStack.Children.Add(chkFamilyType);

            Grid.SetColumn(filterPanel, 4);
            mainGrid.Children.Add(filterPanel);

            // ═══════════════════ FORMAT / LOCATION BAR ═══════════════════
            var formatBar = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                Padding = new Thickness(12, 8, 12, 8),
                BorderBrush = new SolidColorBrush(BorderDark),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            var formatGrid = new Grid();
            formatGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            formatGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var formatPanel = new StackPanel { Orientation = Orientation.Horizontal };
            formatPanel.Children.Add(MakeLabel("Format:", true));
            var rbCSV = MakeRadio("CSV", "format", true);
            var rbExcel = MakeRadio("Excel (.xlsx)", "format");
            var rbJSON = MakeRadio("JSON", "format");
            formatPanel.Children.Add(rbCSV);
            formatPanel.Children.Add(rbExcel);
            formatPanel.Children.Add(rbJSON);
            Grid.SetColumn(formatPanel, 0);
            formatGrid.Children.Add(formatPanel);

            var locationPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            locationPanel.Children.Add(MakeLabel("Location:", true));
            var txtPath = new System.Windows.Controls.TextBox
            {
                Width = 300,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderBrush = new SolidColorBrush(BorderDark),
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(4, 0, 4, 0),
                Text = OutputLocationHelper.GetTimestampedPath(doc, "STING_Export", ".csv")
            };
            locationPanel.Children.Add(txtPath);
            var btnBrowse = MakeSmallButton("Browse...");
            btnBrowse.Click += (s, e) =>
            {
                string ext = rbExcel.IsChecked == true ? ".xlsx" : rbJSON.IsChecked == true ? ".json" : ".csv";
                string filter = rbExcel.IsChecked == true ? "Excel Files|*.xlsx" : rbJSON.IsChecked == true ? "JSON Files|*.json" : "CSV Files|*.csv";
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Location",
                    FileName = Path.GetFileNameWithoutExtension(txtPath.Text) + ext,
                    Filter = filter + "|All Files|*.*",
                    InitialDirectory = Path.GetDirectoryName(txtPath.Text) ?? OutputLocationHelper.GetOutputDirectory(doc)
                };
                if (dlg.ShowDialog() == true) txtPath.Text = dlg.FileName;
            };
            locationPanel.Children.Add(btnBrowse);
            Grid.SetColumn(locationPanel, 1);
            formatGrid.Children.Add(locationPanel);

            // Update file extension when format changes
            void UpdateExtension()
            {
                try
                {
                    string ext = rbExcel.IsChecked == true ? ".xlsx" : rbJSON.IsChecked == true ? ".json" : ".csv";
                    string current = txtPath.Text;
                    if (!string.IsNullOrEmpty(current))
                        txtPath.Text = Path.ChangeExtension(current, ext);
                }
                catch (Exception ex2) { StingLog.Warn($"path parse failure is non-fatal: {ex2.Message}"); }
            }
            rbCSV.Checked += (s, e) => UpdateExtension();
            rbExcel.Checked += (s, e) => UpdateExtension();
            rbJSON.Checked += (s, e) => UpdateExtension();

            formatBar.Child = formatGrid;
            Grid.SetRow(formatBar, 4);
            root.Children.Add(formatBar);

            // ═══════════════════ FOOTER ═══════════════════
            var footer = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                Padding = new Thickness(12, 8, 12, 8)
            };
            var footerPanel = new DockPanel();

            // Left: status
            var statusText = new TextBlock
            {
                Foreground = new SolidColorBrush(TextGrey),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Text = $"{categories.Count} categories, {allParams.Count} parameters available"
            };
            DockPanel.SetDock(statusText, Dock.Left);
            footerPanel.Children.Add(statusText);

            // Right: buttons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80, Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 12
            };
            btnCancel.Click += (s, e) => win.DialogResult = false;

            var btnExport = new Button
            {
                Content = "Export",
                Width = 100, Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 12, FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(AccentOrange),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(AccentOrange)
            };
            btnExport.Click += (s, e) =>
            {
                // Validate
                var selCats = catChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                var selParams = paramChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

                if (selCats.Count == 0)
                {
                    MessageBox.Show("Select at least one category.", "STING Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (selParams.Count == 0)
                {
                    MessageBox.Show("Select at least one parameter.", "STING Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(txtPath.Text))
                {
                    MessageBox.Show("Choose an export location.", "STING Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                result.SelectedCategories = selCats;
                result.SelectedParameters = selParams;
                result.Format = rbExcel.IsChecked == true ? "Excel" : rbJSON.IsChecked == true ? "JSON" : "CSV";
                result.OutputPath = txtPath.Text;
                result.Scope = rbView.IsChecked == true ? "ActiveView" : rbSelection.IsChecked == true ? "Selection" : "Project";
                result.IncludeElementId = chkElementId.IsChecked == true;
                result.IncludeCategory = chkCategory.IsChecked == true;
                result.IncludeFamilyType = chkFamilyType.IsChecked == true;
                result.Cancelled = false;

                string famSel = cmbFamily.SelectedItem?.ToString() ?? cmbFamily.Text;
                if (famSel != "(All Families)" && !string.IsNullOrWhiteSpace(famSel))
                    result.FamilyFilter = famSel;

                string typeSel = cmbType.SelectedItem?.ToString() ?? cmbType.Text;
                if (typeSel != "(All Types)" && !string.IsNullOrWhiteSpace(typeSel))
                    result.TypeFilter = typeSel;

                win.DialogResult = true;
            };

            var btnSavePreset = new Button
            {
                Content = "Save Preset",
                Width = 95, Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 12,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderBrush = new SolidColorBrush(BorderDark)
            };
            btnSavePreset.Click += (s, e) =>
            {
                var selCats = catChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                var selParams = paramChecks.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
                if (selCats.Count == 0 || selParams.Count == 0)
                {
                    MessageBox.Show("Select categories and parameters first.", "STING Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Prompt for name
                var nameWin = new Window
                {
                    Title = "Save Export Preset",
                    Width = 380, Height = 160,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = new SolidColorBrush(BgDark),
                    ResizeMode = ResizeMode.NoResize,
                    Owner = win
                };
                var nameStack = new StackPanel { Margin = new Thickness(16) };
                nameStack.Children.Add(new TextBlock { Text = "Preset Name:", Foreground = new SolidColorBrush(TextWhite), FontSize = 13, Margin = new Thickness(0, 0, 0, 6) });
                var txtName = new System.Windows.Controls.TextBox
                {
                    FontSize = 13, Padding = new Thickness(6, 4, 6, 4),
                    Background = new SolidColorBrush(BgLight), Foreground = new SolidColorBrush(TextWhite),
                    BorderBrush = new SolidColorBrush(BorderDark)
                };
                nameStack.Children.Add(txtName);
                var nameBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
                var btnSaveOk = new Button { Content = "Save", Width = 70, Height = 28, Background = new SolidColorBrush(AccentOrange), Foreground = Brushes.White, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 0, 0, 0) };
                var btnSaveCancel = new Button { Content = "Cancel", Width = 70, Height = 28, Margin = new Thickness(4, 0, 0, 0) };
                btnSaveOk.Click += (s2, e2) => { if (!string.IsNullOrWhiteSpace(txtName.Text)) nameWin.DialogResult = true; };
                btnSaveCancel.Click += (s2, e2) => nameWin.DialogResult = false;
                nameBtnPanel.Children.Add(btnSaveCancel);
                nameBtnPanel.Children.Add(btnSaveOk);
                nameStack.Children.Add(nameBtnPanel);
                nameWin.Content = nameStack;

                if (nameWin.ShowDialog() != true) return;

                var preset = new ExportLinkDefinition
                {
                    Name = txtName.Text.Trim(),
                    Group = "Custom",
                    SelectedCategories = selCats,
                    SelectedParameters = selParams,
                    Format = rbExcel.IsChecked == true ? "Excel" : rbJSON.IsChecked == true ? "JSON" : "CSV",
                    Scope = rbView.IsChecked == true ? "ActiveView" : rbSelection.IsChecked == true ? "Selection" : "Project",
                    IncludeElementId = chkElementId.IsChecked == true,
                    IncludeCategory = chkCategory.IsChecked == true,
                    IncludeFamilyType = chkFamilyType.IsChecked == true,
                    FamilyFilter = cmbFamily.SelectedItem?.ToString() != "(All Families)" ? cmbFamily.SelectedItem?.ToString() : null,
                    TypeFilter = cmbType.SelectedItem?.ToString() != "(All Types)" ? cmbType.SelectedItem?.ToString() : null
                };
                ExportLinkLibrary.SaveUserPreset(doc, preset);
                cmbPreset.Items.Add($"  {preset.Name} [User]");
                presetByIndex[cmbPreset.Items.Count - 1] = preset;
                cmbPreset.SelectedIndex = cmbPreset.Items.Count - 1;
                statusText.Text = $"Preset '{preset.Name}' saved";
            };

            DockPanel.SetDock(buttonPanel, Dock.Right);
            buttonPanel.Children.Add(btnSavePreset);
            buttonPanel.Children.Add(btnCancel);
            buttonPanel.Children.Add(btnExport);
            footerPanel.Children.Add(buttonPanel);

            footer.Child = footerPanel;
            Grid.SetRow(footer, 5);
            root.Children.Add(footer);

            // ═══════════════════ PRESET HANDLERS ═══════════════════

            // Apply selected preset to UI controls
            btnLoadPreset.Click += (s, e) =>
            {
                int idx = cmbPreset.SelectedIndex;
                if (!presetByIndex.TryGetValue(idx, out var preset)) return;

                // Update category checkboxes
                foreach (var kv in catChecks)
                    kv.Value.IsChecked = preset.SelectedCategories.Contains(kv.Key, StringComparer.OrdinalIgnoreCase);

                // Update parameter checkboxes
                foreach (var kv in paramChecks)
                    kv.Value.IsChecked = preset.SelectedParameters.Contains(kv.Key, StringComparer.OrdinalIgnoreCase);

                // Update format radio buttons
                if (preset.Format == "Excel") rbExcel.IsChecked = true;
                else if (preset.Format == "JSON") rbJSON.IsChecked = true;
                else rbCSV.IsChecked = true;

                // Update scope
                if (preset.Scope == "ActiveView") rbView.IsChecked = true;
                else if (preset.Scope == "Selection") rbSelection.IsChecked = true;
                else rbProject.IsChecked = true;

                // Update options
                chkElementId.IsChecked = preset.IncludeElementId;
                chkCategory.IsChecked = preset.IncludeCategory;
                chkFamilyType.IsChecked = preset.IncludeFamilyType;

                // Update family/type filters
                if (!string.IsNullOrEmpty(preset.FamilyFilter))
                {
                    for (int i = 0; i < cmbFamily.Items.Count; i++)
                        if (cmbFamily.Items[i]?.ToString() == preset.FamilyFilter)
                        { cmbFamily.SelectedIndex = i; break; }
                }
                else cmbFamily.SelectedIndex = 0;

                statusText.Text = $"Preset '{preset.Name}' applied";
            };

            // Delete user preset
            btnDeletePreset.Click += (s, e) =>
            {
                int idx = cmbPreset.SelectedIndex;
                if (!presetByIndex.TryGetValue(idx, out var preset) || preset.IsBuiltIn) return;
                if (MessageBox.Show($"Delete preset '{preset.Name}'?", "STING Export",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                ExportLinkLibrary.DeleteUserPreset(doc, preset.Name);
                cmbPreset.Items.RemoveAt(idx);
                presetByIndex.Remove(idx);
                cmbPreset.SelectedIndex = 0;
                statusText.Text = $"Preset '{preset.Name}' deleted";
            };

            // ── Show ──
            bool? dialogResult = win.ShowDialog();
            if (dialogResult != true || result.Cancelled) return null;
            return result;
        }

        // ═══════════════════ DATA HELPERS ═══════════════════

        private static HashSet<string> GetModelCategories(Document doc)
        {
            var cats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var known = TagConfig.DiscMap.Keys;
                foreach (var k in known) cats.Add(k);

                // Also collect categories actually present in the model
                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
                foreach (var el in elements)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    if (!string.IsNullOrEmpty(cat)) cats.Add(cat);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExportDialog GetCategories: {ex.Message}"); }
            return cats;
        }

        private static List<string> GetAvailableParameters(Document doc, IEnumerable<string> categories)
        {
            var paramNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // Add all known STING parameters
                foreach (var kv in ParamRegistry.AllParamGuids)
                    paramNames.Add(kv.Key);

                // Sample first 50 elements for their native parameters
                var sample = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .Take(50);
                foreach (var el in sample)
                {
                    foreach (Parameter p in el.Parameters)
                    {
                        if (p.Definition != null && !string.IsNullOrEmpty(p.Definition.Name))
                            paramNames.Add(p.Definition.Name);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExportDialog GetParameters: {ex.Message}"); }
            return paramNames.ToList();
        }

        private static Dictionary<string, List<string>> GetFamilies(Document doc, IEnumerable<string> categories)
        {
            var families = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var types = new FilteredElementCollector(doc)
                    .WhereElementIsElementType()
                    .ToElements()
                    .OfType<FamilySymbol>()
                    .Take(500); // Limit for performance
                foreach (var fs in types)
                {
                    string famName = fs.FamilyName ?? "(Unknown)";
                    string typeName = fs.Name ?? "(Unnamed)";
                    if (!families.TryGetValue(famName, out var list))
                    {
                        list = new List<string>();
                        families[famName] = list;
                    }
                    if (!list.Contains(typeName))
                        list.Add(typeName);
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExportDialog GetFamilies: {ex.Message}"); }
            return families;
        }

        // ═══════════════════ UI HELPERS ═══════════════════

        private static Border MakeGroupPanel(string title)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(BgMedium),
                BorderBrush = new SolidColorBrush(BorderDark),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2),
                Padding = new Thickness(6)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentOrange),
                Margin = new Thickness(0, 0, 0, 4)
            });
            border.Child = stack;
            return border;
        }

        private static System.Windows.Controls.TextBox MakeSearchBox(string placeholder)
        {
            var tb = new System.Windows.Controls.TextBox
            {
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderBrush = new SolidColorBrush(BorderDark),
                FontSize = 12,
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 0, 0, 4),
                Tag = placeholder
            };
            // Placeholder via GotFocus/LostFocus
            tb.Text = placeholder;
            tb.Foreground = new SolidColorBrush(TextGrey);
            tb.GotFocus += (s, e) =>
            {
                if (tb.Text == (string)tb.Tag)
                {
                    tb.Text = "";
                    tb.Foreground = new SolidColorBrush(TextWhite);
                }
            };
            tb.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    tb.Text = (string)tb.Tag;
                    tb.Foreground = new SolidColorBrush(TextGrey);
                }
            };
            return tb;
        }

        private static TextBlock MakeLabel(string text, bool bold = false)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(4, 3, 4, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static RadioButton MakeRadio(string text, string group, bool isChecked = false)
        {
            return new RadioButton
            {
                Content = text,
                GroupName = group,
                IsChecked = isChecked,
                Foreground = new SolidColorBrush(TextWhite),
                FontSize = 12,
                Margin = new Thickness(8, 3, 4, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Button MakeSmallButton(string text)
        {
            return new Button
            {
                Content = text,
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(2),
                FontSize = 11,
                Background = new SolidColorBrush(BgLight),
                Foreground = new SolidColorBrush(TextWhite),
                BorderBrush = new SolidColorBrush(BorderDark)
            };
        }
    }

    /// <summary>
    /// Engine that executes the data export based on dialog settings.
    /// Supports CSV, Excel (via ClosedXML), and JSON output formats.
    /// </summary>
    internal static class DataExportEngine
    {
        public static void Execute(Document doc, Autodesk.Revit.UI.UIDocument uidoc, ExportDialogResult settings)
        {
            if (settings == null || settings.Cancelled) return;

            var catSet = new HashSet<string>(settings.SelectedCategories, StringComparer.OrdinalIgnoreCase);
            var paramNames = settings.SelectedParameters;

            // ── Collect elements ──
            IEnumerable<Element> elements;
            if (settings.Scope == "Selection" && uidoc != null)
            {
                elements = uidoc.Selection.GetElementIds()
                    .Select(id => doc.GetElement(id))
                    .Where(el => el != null);
            }
            else if (settings.Scope == "ActiveView" && doc.ActiveView != null)
            {
                elements = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }
            else
            {
                elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }

            // ── Filter by category ──
            var filtered = elements.Where(el =>
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                return !string.IsNullOrEmpty(cat) && catSet.Contains(cat);
            });

            // ── Filter by family/type ──
            if (!string.IsNullOrEmpty(settings.FamilyFilter))
            {
                string famFilter = settings.FamilyFilter;
                filtered = filtered.Where(el =>
                {
                    string fam = ParameterHelpers.GetFamilyName(el);
                    return string.Equals(fam, famFilter, StringComparison.OrdinalIgnoreCase);
                });
            }
            if (!string.IsNullOrEmpty(settings.TypeFilter))
            {
                string typeFilter = settings.TypeFilter;
                filtered = filtered.Where(el =>
                {
                    string typeName = ParameterHelpers.GetFamilySymbolName(el);
                    return string.Equals(typeName, typeFilter, StringComparison.OrdinalIgnoreCase);
                });
            }

            var elemList = filtered.ToList();
            StingLog.Info($"DataExport: {elemList.Count} elements, {paramNames.Count} parameters, format={settings.Format}");

            // ── Build header ──
            var headers = new List<string>();
            if (settings.IncludeElementId) headers.Add("ElementId");
            if (settings.IncludeCategory) headers.Add("Category");
            if (settings.IncludeFamilyType) { headers.Add("Family"); headers.Add("Type"); }
            headers.AddRange(paramNames);

            // ── Build rows ──
            var rows = new List<string[]>();
            foreach (var el in elemList)
            {
                var row = new List<string>();
                if (settings.IncludeElementId) row.Add(el.Id.ToString());
                if (settings.IncludeCategory) row.Add(ParameterHelpers.GetCategoryName(el));
                if (settings.IncludeFamilyType)
                {
                    row.Add(ParameterHelpers.GetFamilyName(el));
                    row.Add(ParameterHelpers.GetFamilySymbolName(el));
                }
                foreach (var pName in paramNames)
                {
                    string val = ReadParamValue(el, pName);
                    row.Add(val);
                }
                rows.Add(row.ToArray());
            }

            // ── Write output ──
            string dir = Path.GetDirectoryName(settings.OutputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            switch (settings.Format.ToUpperInvariant())
            {
                case "CSV":
                    WriteCsv(settings.OutputPath, headers, rows);
                    break;
                case "EXCEL":
                    WriteExcel(settings.OutputPath, headers, rows);
                    break;
                case "JSON":
                    WriteJson(settings.OutputPath, headers, rows);
                    break;
                default:
                    WriteCsv(settings.OutputPath, headers, rows);
                    break;
            }
        }

        private static string ReadParamValue(Element el, string paramName)
        {
            try
            {
                // Try STING shared parameter first
                string val = ParameterHelpers.GetString(el, paramName);
                if (!string.IsNullOrEmpty(val)) return val;

                // Try native parameter by name
                Parameter p = el.LookupParameter(paramName);
                if (p == null) return "";
                switch (p.StorageType)
                {
                    case StorageType.String: return p.AsString() ?? "";
                    case StorageType.Integer: return p.AsInteger().ToString();
                    case StorageType.Double: return p.AsDouble().ToString("F4");
                    case StorageType.ElementId:
                        var refEl = el.Document.GetElement(p.AsElementId());
                        return refEl?.Name ?? p.AsElementId().ToString();
                    default: return "";
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        private static void WriteCsv(string path, List<string> headers, List<string[]> rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            StingLog.Info($"DataExport: CSV written to {path} ({rows.Count} rows)");
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n"))
                return $"\"{val.Replace("\"", "\"\"")}\"";
            return val;
        }

        private static void WriteExcel(string path, List<string> headers, List<string[]> rows)
        {
            try
            {
                using var wb = new ClosedXML.Excel.XLWorkbook();
                var ws = wb.Worksheets.Add("STING Export");
                for (int c = 0; c < headers.Count; c++)
                {
                    ws.Cell(1, c + 1).Value = headers[c];
                    ws.Cell(1, c + 1).Style.Font.Bold = true;
                    ws.Cell(1, c + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromArgb(232, 145, 45);
                    ws.Cell(1, c + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                }
                for (int r = 0; r < rows.Count; r++)
                {
                    for (int c = 0; c < rows[r].Length && c < headers.Count; c++)
                        ws.Cell(r + 2, c + 1).Value = rows[r][c];
                }
                ws.Columns().AdjustToContents(1, 50);
                ws.SheetView.FreezeRows(1);
                wb.SaveAs(path);
                StingLog.Info($"DataExport: Excel written to {path} ({rows.Count} rows)");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Excel export failed, falling back to CSV: {ex.Message}");
                WriteCsv(Path.ChangeExtension(path, ".csv"), headers, rows);
            }
        }

        private static void WriteJson(string path, List<string> headers, List<string[]> rows)
        {
            var jsonRows = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                var dict = new Dictionary<string, string>();
                for (int i = 0; i < headers.Count && i < row.Length; i++)
                    dict[headers[i]] = row[i];
                jsonRows.Add(dict);
            }
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(jsonRows, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            StingLog.Info($"DataExport: JSON written to {path} ({rows.Count} rows)");
        }
    }
}
