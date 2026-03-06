using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Single source of truth for all parameter names, GUIDs, container definitions,
    /// and category bindings. Loaded once from PARAMETER_REGISTRY.json at startup.
    ///
    /// USAGE:
    ///   Instead of:  ParameterHelpers.GetString(el, "ASS_TAG_1_TXT")
    ///   Write:       ParameterHelpers.GetString(el, ParamRegistry.TAG1)
    ///
    ///   Instead of:  duplicating 36 container definitions in 4 files
    ///   Write:       ParamRegistry.ContainerGroups / ParamRegistry.ContainersForCategory(cat)
    ///
    /// To add/rename a parameter:
    ///   1. Edit PARAMETER_REGISTRY.json
    ///   2. Run "Sync Parameter Schema" command
    ///   3. All code automatically uses the new name
    /// </summary>
    public static class ParamRegistry
    {
        // ── Loaded state ────────────────────────────────────────────────
        private static bool _loaded;
        private static readonly object _lock = new object();

        // ── Tag format ──────────────────────────────────────────────────
        public static string Separator { get; private set; } = "-";
        public static int NumPad { get; private set; } = 4;
        public static string[] SegmentOrder { get; private set; } = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };

        // ── Source token definitions ────────────────────────────────────
        public static TokenDef[] SourceTokens { get; private set; } = Array.Empty<TokenDef>();

        /// <summary>All 8 source token parameter names in tag segment order.</summary>
        public static string[] AllTokenParams { get; private set; } = Array.Empty<string>();

        // ── Convenience accessors: source token param names by slot ─────
        /// <summary>Discipline token parameter name (slot 0).</summary>
        public static string DISC => TokenParamName(0);
        /// <summary>Location token parameter name (slot 1).</summary>
        public static string LOC  => TokenParamName(1);
        /// <summary>Zone token parameter name (slot 2).</summary>
        public static string ZONE => TokenParamName(2);
        /// <summary>Level token parameter name (slot 3).</summary>
        public static string LVL  => TokenParamName(3);
        /// <summary>System token parameter name (slot 4).</summary>
        public static string SYS  => TokenParamName(4);
        /// <summary>Function token parameter name (slot 5).</summary>
        public static string FUNC => TokenParamName(5);
        /// <summary>Product token parameter name (slot 6).</summary>
        public static string PROD => TokenParamName(6);
        /// <summary>Sequence token parameter name (slot 7).</summary>
        public static string SEQ  => TokenParamName(7);

        // ── Support parameter names ────────────────────────────────────
        public static string STATUS { get; private set; } = "ASS_STATUS_TXT";
        public static string DETAIL_NUM { get; private set; } = "ASS_INST_DETAIL_NUM_TXT";
        public static string MNT_TYPE { get; private set; } = "MNT_TYPE_TXT";

        // ── Extended parameter names (identity, spatial, dimensional, MEP) ──
        // Loaded from extended_params section. Keys map to param_name values.
        private static Dictionary<string, string> _extendedParams = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Get an extended parameter name by its key (e.g. "DESC", "WALL_HEIGHT").</summary>
        public static string Ext(string key)
        {
            EnsureLoaded();
            if (_extendedParams.TryGetValue(key, out string name))
                return name;
            StingLog.Warn($"ParamRegistry.Ext: key '{key}' not found in extended_params");
            return "";
        }

        // ── Identity parameters ──────────────────────────────────────────
        public static string ID             => Ext("ID");
        public static string DESC           => Ext("DESC");
        public static string MFR            => Ext("MFR");
        public static string MODEL          => Ext("MODEL");
        public static string TYPE_NAME      => Ext("TYPE_NAME");
        public static string FAMILY_NAME    => Ext("FAMILY_NAME");
        public static string CAT            => Ext("CAT");
        public static string TYPE_MARK      => Ext("TYPE_MARK");
        public static string TYPE_COMMENTS  => Ext("TYPE_COMMENTS");
        public static string KEYNOTE        => Ext("KEYNOTE");
        public static string UNIFORMAT      => Ext("UNIFORMAT");
        public static string UNIFORMAT_DESC => Ext("UNIFORMAT_DESC");
        public static string OMNICLASS      => Ext("OMNICLASS");
        public static string SIZE           => Ext("SIZE");
        public static string COST           => Ext("COST");
        public static string PRJ_COMMENTS   => Ext("PRJ_COMMENTS");

        // ── Spatial parameters ───────────────────────────────────────────
        public static string ROOM_NAME      => Ext("ROOM_NAME");
        public static string ROOM_NUM       => Ext("ROOM_NUM");
        public static string ROOM_AREA      => Ext("ROOM_AREA");
        public static string ROOM_VOLUME    => Ext("ROOM_VOLUME");
        public static string DEPT           => Ext("DEPT");
        public static string GRID_REF       => Ext("GRID_REF");
        public static string BLE_ROOM_NAME  => Ext("BLE_ROOM_NAME");
        public static string BLE_ROOM_NUM   => Ext("BLE_ROOM_NUM");

        // ── Extended token parameters ────────────────────────────────────
        public static string ORIGIN         => Ext("ORIGIN");
        public static string PROJECT        => Ext("PROJECT");
        public static string REV            => Ext("REV");
        public static string VOLUME         => Ext("VOLUME");

        // ── BLE dimensional parameters ───────────────────────────────────
        public static string WALL_HEIGHT    => Ext("WALL_HEIGHT");
        public static string WALL_LENGTH    => Ext("WALL_LENGTH");
        public static string WALL_THICKNESS => Ext("WALL_THICKNESS");
        public static string DOOR_WIDTH     => Ext("DOOR_WIDTH");
        public static string DOOR_HEIGHT    => Ext("DOOR_HEIGHT");
        public static string WINDOW_WIDTH   => Ext("WINDOW_WIDTH");
        public static string WINDOW_HEIGHT  => Ext("WINDOW_HEIGHT");
        public static string WINDOW_SILL    => Ext("WINDOW_SILL");
        public static string FLR_THICKNESS  => Ext("FLR_THICKNESS");
        public static string ELE_AREA       => Ext("ELE_AREA");
        public static string CEILING_HEIGHT => Ext("CEILING_HEIGHT");
        public static string ROOF_SLOPE     => Ext("ROOF_SLOPE");
        public static string STAIR_TREAD    => Ext("STAIR_TREAD");
        public static string STAIR_RISE     => Ext("STAIR_RISE");
        public static string STAIR_WIDTH    => Ext("STAIR_WIDTH");
        public static string RAMP_SLOPE     => Ext("RAMP_SLOPE");
        public static string RAMP_WIDTH     => Ext("RAMP_WIDTH");
        public static string STRUCT_TYPE    => Ext("STRUCT_TYPE");
        public static string FIRE_RATING    => Ext("FIRE_RATING");
        public static string ELE_VOLUME     => Ext("ELE_VOLUME");
        public static string ELE_LENGTH     => Ext("ELE_LENGTH");
        public static string DOOR_HEAD_HT   => Ext("DOOR_HEAD_HT");
        public static string DOOR_FUNC      => Ext("DOOR_FUNC");
        public static string WINDOW_HEAD_HT => Ext("WINDOW_HEAD_HT");
        public static string ROOM_FINISH_FLR  => Ext("ROOM_FINISH_FLR");
        public static string ROOM_FINISH_WALL => Ext("ROOM_FINISH_WALL");
        public static string ROOM_FINISH_CLG  => Ext("ROOM_FINISH_CLG");
        public static string ROOM_FINISH_BASE => Ext("ROOM_FINISH_BASE");

        // ── Electrical parameters ────────────────────────────────────────
        public static string ELC_POWER      => Ext("ELC_POWER");
        public static string ELC_VOLTAGE    => Ext("ELC_VOLTAGE");
        public static string ELC_CIRCUIT_NR => Ext("ELC_CIRCUIT_NR");
        public static string ELC_PNL_NAME   => Ext("ELC_PNL_NAME");
        public static string ELC_PNL_VOLTAGE => Ext("ELC_PNL_VOLTAGE");
        public static string ELC_PHASES     => Ext("ELC_PHASES");
        public static string ELC_PNL_LOAD   => Ext("ELC_PNL_LOAD");
        public static string ELC_PNL_FED_FROM => Ext("ELC_PNL_FED_FROM");
        public static string ELC_MAIN_BRK   => Ext("ELC_MAIN_BRK");
        public static string ELC_WAYS       => Ext("ELC_WAYS");
        public static string ELC_IP_RATING  => Ext("ELC_IP_RATING");

        // ── Lighting parameters ──────────────────────────────────────────
        public static string LTG_WATTAGE    => Ext("LTG_WATTAGE");
        public static string LTG_LUMENS     => Ext("LTG_LUMENS");
        public static string LTG_EFFICACY   => Ext("LTG_EFFICACY");
        public static string LTG_LAMP_TYPE  => Ext("LTG_LAMP_TYPE");

        // ── HVAC parameters ─────────────────────────────────────────────
        public static string HVC_DUCT_FLOW  => Ext("HVC_DUCT_FLOW");
        public static string HVC_VELOCITY   => Ext("HVC_VELOCITY");
        public static string HVC_PRESSURE   => Ext("HVC_PRESSURE");
        public static string HVC_AIRFLOW    => Ext("HVC_AIRFLOW");
        public static string HVC_DUCT_WIDTH => Ext("HVC_DUCT_WIDTH");
        public static string HVC_DUCT_HEIGHT => Ext("HVC_DUCT_HEIGHT");
        public static string HVC_INSULATION => Ext("HVC_INSULATION");
        public static string HVC_DUCT_LENGTH => Ext("HVC_DUCT_LENGTH");

        // ── Plumbing parameters ──────────────────────────────────────────
        public static string PLM_PIPE_FLOW  => Ext("PLM_PIPE_FLOW");
        public static string PLM_PIPE_SIZE  => Ext("PLM_PIPE_SIZE");
        public static string PLM_VELOCITY   => Ext("PLM_VELOCITY");
        public static string PLM_FLOW_RATE  => Ext("PLM_FLOW_RATE");
        public static string PLM_PIPE_LENGTH => Ext("PLM_PIPE_LENGTH");

        // ── Paragraph visibility controls (v4.2) ────────────────────────
        /// <summary>Compact paragraph depth (State 1 only).</summary>
        public static string PARA_STATE_1 { get; private set; } = "TAG_PARA_STATE_1_BOOL";
        /// <summary>Standard paragraph depth (States 1+2).</summary>
        public static string PARA_STATE_2 { get; private set; } = "TAG_PARA_STATE_2_BOOL";
        /// <summary>Comprehensive paragraph depth (States 1+2+3).</summary>
        public static string PARA_STATE_3 { get; private set; } = "TAG_PARA_STATE_3_BOOL";
        /// <summary>Enable/disable warning text in tags.</summary>
        public static string WARN_VISIBLE { get; private set; } = "TAG_WARN_VISIBLE_BOOL";
        /// <summary>Warning severity filter: CRITICAL, HIGH, MEDIUM, ALL.</summary>
        public static string WARN_SEVERITY_FILTER { get; private set; } = "TAG_WARN_SEVERITY_FILTER_TXT";

        // ── Paragraph container parameter names (v4.2/v4.3) ─────────────
        public static string PARA_WALL      => Ext("PARA_WALL");
        public static string PARA_FLOOR     => Ext("PARA_FLOOR");
        public static string PARA_DOOR      => Ext("PARA_DOOR");
        public static string PARA_WIN       => Ext("PARA_WIN");
        public static string PARA_ROOM      => Ext("PARA_ROOM");
        public static string PARA_CEIL      => Ext("PARA_CEIL");
        public static string PARA_ROOF      => Ext("PARA_ROOF");
        public static string PARA_STAIR     => Ext("PARA_STAIR");
        public static string PARA_RAMP      => Ext("PARA_RAMP");
        public static string PARA_FACADE    => Ext("PARA_FACADE");
        public static string PARA_CASEWORK  => Ext("PARA_CASEWORK");
        public static string PARA_FURNITURE => Ext("PARA_FURNITURE");
        public static string PARA_STR_COL   => Ext("PARA_STR_COL");
        public static string PARA_STR_BEAM  => Ext("PARA_STR_BEAM");
        public static string PARA_STR_FDN   => Ext("PARA_STR_FDN");
        public static string PARA_HVC_SPEC  => Ext("PARA_HVC_SPEC");
        public static string PARA_HVC_DUCT  => Ext("PARA_HVC_DUCT");
        public static string PARA_HVC_AT    => Ext("PARA_HVC_AT");
        public static string PARA_ELC_PANEL => Ext("PARA_ELC_PANEL");
        public static string PARA_ELC_CIRCUIT => Ext("PARA_ELC_CIRCUIT");
        public static string PARA_LTG_SPEC  => Ext("PARA_LTG_SPEC");
        public static string PARA_PLM_FIXTURE => Ext("PARA_PLM_FIXTURE");
        public static string PARA_PLM_PIPE  => Ext("PARA_PLM_PIPE");
        public static string PARA_FLS_FA    => Ext("PARA_FLS_FA");
        public static string PARA_FLS_SPR   => Ext("PARA_FLS_SPR");
        public static string PARA_COM_BMS   => Ext("PARA_COM_BMS");
        // ── Paragraph containers added v4.3 (completing 15 missing) ────
        public static string PARA_HVC_FLEXDUCT => Ext("PARA_HVC_FLEXDUCT");
        public static string PARA_HVC_DCTACC  => Ext("PARA_HVC_DCTACC");
        public static string PARA_ELC_CONDUIT => Ext("PARA_ELC_CONDUIT");
        public static string PARA_ELC_TRAY   => Ext("PARA_ELC_TRAY");
        public static string PARA_ELC_CABLE  => Ext("PARA_ELC_CABLE");
        public static string PARA_PLM_EQUIP  => Ext("PARA_PLM_EQUIP");
        public static string PARA_PLM_PIPEACC => Ext("PARA_PLM_PIPEACC");
        public static string PARA_PLM_DRAIN  => Ext("PARA_PLM_DRAIN");
        public static string PARA_ICT_DATA   => Ext("PARA_ICT_DATA");
        public static string PARA_NCL        => Ext("PARA_NCL");
        public static string PARA_SEC        => Ext("PARA_SEC");
        public static string PARA_ASS_EQUIP  => Ext("PARA_ASS_EQUIP");
        public static string PARA_RGL_CMPL   => Ext("PARA_RGL_CMPL");
        public static string PARA_PER_ENV    => Ext("PARA_PER_ENV");
        public static string PARA_CST_CONC   => Ext("PARA_CST_CONC");

        // ── ISO 19650 naming parameters ────────────────────────────────
        public static string PROJECT_COD    => Ext("PROJECT_COD");
        public static string ORIGINATOR_COD => Ext("ORIGINATOR_COD");
        public static string VOLUME_COD     => Ext("VOLUME_COD");
        public static string STATUS_COD     => Ext("STATUS_COD");
        public static string REV_COD        => Ext("REV_COD");

        // ── Warning threshold parameter ─────────────────────────────────
        public static string ELC_PNL_RATED  => Ext("ELC_PNL_RATED");

        // ── Universal tag container names (convenience) ─────────────────
        /// <summary>Full 8-segment tag: DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ</summary>
        public static string TAG1 { get; private set; } = "ASS_TAG_1_TXT";
        /// <summary>Short ID: DISC-PROD-SEQ</summary>
        public static string TAG2 { get; private set; } = "ASS_TAG_2_TXT";
        /// <summary>Location: LOC-ZONE-LVL</summary>
        public static string TAG3 { get; private set; } = "ASS_TAG_3_TXT";
        /// <summary>System: SYS-FUNC</summary>
        public static string TAG4 { get; private set; } = "ASS_TAG_4_TXT";
        /// <summary>Multi-line top: DISC-LOC-ZONE-LVL</summary>
        public static string TAG5 { get; private set; } = "ASS_TAG_5_TXT";
        /// <summary>Multi-line bottom: SYS-FUNC-PROD-SEQ</summary>
        public static string TAG6 { get; private set; } = "ASS_TAG_6_TXT";
        /// <summary>Comprehensive descriptive narrative — AI-assembled asset profile with embedded markup.</summary>
        public static string TAG7 { get; private set; } = "ASS_TAG_7_TXT";

        // ── TAG7 Sub-Section Parameters ──────────────────────────────────
        // Split TAG7 into independently stylable sections for multi-label tag families.
        // Each sub-param can have its own font/size/color/bold in annotation family labels.
        /// <summary>TAG7 Section A: Identity Header — asset name, product, manufacturer (BOLD in tag families).</summary>
        public static string TAG7A { get; private set; } = "ASS_TAG_7A_TXT";
        /// <summary>TAG7 Section B: System &amp; Function Context — full descriptions (ITALIC in tag families).</summary>
        public static string TAG7B { get; private set; } = "ASS_TAG_7B_TXT";
        /// <summary>TAG7 Section C: Spatial Context — room, department, grid reference.</summary>
        public static string TAG7C { get; private set; } = "ASS_TAG_7C_TXT";
        /// <summary>TAG7 Section D: Lifecycle &amp; Status — status, revision, origin, maintenance.</summary>
        public static string TAG7D { get; private set; } = "ASS_TAG_7D_TXT";
        /// <summary>TAG7 Section E: Technical Specifications — discipline-specific performance data.</summary>
        public static string TAG7E { get; private set; } = "ASS_TAG_7E_TXT";
        /// <summary>TAG7 Section F: Classification &amp; Reference — codes, cost, ISO tag.</summary>
        public static string TAG7F { get; private set; } = "ASS_TAG_7F_TXT";

        /// <summary>All TAG7 sub-section parameter names in order (A-F).</summary>
        public static string[] TAG7Sections => new[] { TAG7A, TAG7B, TAG7C, TAG7D, TAG7E, TAG7F };

        /// <summary>Check if a parameter is any TAG7 variant (main or sub-section).</summary>
        public static bool IsTag7Param(string paramName)
        {
            return paramName == TAG7 || paramName == TAG7A || paramName == TAG7B ||
                   paramName == TAG7C || paramName == TAG7D || paramName == TAG7E ||
                   paramName == TAG7F;
        }

        // ── Token presets (named token index arrays) ────────────────────
        public static Dictionary<string, int[]> TokenPresets { get; private set; } = new Dictionary<string, int[]>();

        // ── Container groups and flat container list ─────────────────────
        public static ContainerGroupDef[] ContainerGroups { get; private set; } = Array.Empty<ContainerGroupDef>();
        private static ContainerParamDef[] _allContainers;
        private static Dictionary<string, List<ContainerParamDef>> _containersByCategory;

        // ── GUID lookups ────────────────────────────────────────────────
        private static Dictionary<string, Guid> _guidByName;
        private static Dictionary<Guid, string> _nameByGuid;

        // ── Universal params (Pass 1) ───────────────────────────────────
        /// <summary>All parameter names that should be bound to all 53 categories (Pass 1).</summary>
        public static string[] UniversalParams { get; private set; } = Array.Empty<string>();

        // ── Category mappings ───────────────────────────────────────────
        /// <summary>Category display name → BuiltInCategory enum string.</summary>
        public static Dictionary<string, string> CategoryEnumMap { get; private set; } = new Dictionary<string, string>();
        /// <summary>All universal category display names.</summary>
        public static string[] UniversalCategories { get; private set; } = Array.Empty<string>();

        // ── Discipline bindings (Pass 2): param → category enums ────────
        private static Dictionary<string, string[]> _disciplineCategoryNames;

        // ════════════════════════════════════════════════════════════════
        // Public API
        // ════════════════════════════════════════════════════════════════

        /// <summary>Get token parameter name by slot index (0-7).</summary>
        public static string TokenParamName(int slot)
        {
            EnsureLoaded();
            return slot >= 0 && slot < AllTokenParams.Length ? AllTokenParams[slot] : "";
        }

        /// <summary>Get token parameter name by key ("DISC", "LOC", etc.).</summary>
        public static string TokenParamName(string key)
        {
            EnsureLoaded();
            var tok = Array.Find(SourceTokens, t => t.Key == key);
            return tok?.ParamName ?? "";
        }

        /// <summary>Get GUID for a parameter name. Returns Guid.Empty if not found.</summary>
        public static Guid GetGuid(string paramName)
        {
            EnsureLoaded();
            return _guidByName != null && _guidByName.TryGetValue(paramName, out Guid g) ? g : Guid.Empty;
        }

        /// <summary>Get parameter name for a GUID. Returns null if not found.</summary>
        public static string GetParamName(Guid guid)
        {
            EnsureLoaded();
            return _nameByGuid != null && _nameByGuid.TryGetValue(guid, out string n) ? n : null;
        }

        /// <summary>Get all parameter names that have GUIDs (tokens + support + containers).</summary>
        public static Dictionary<string, Guid> AllParamGuids
        {
            get { EnsureLoaded(); return _guidByName ?? new Dictionary<string, Guid>(); }
        }

        /// <summary>All container definitions across all groups (flat list).</summary>
        public static ContainerParamDef[] AllContainers
        {
            get
            {
                EnsureLoaded();
                if (_allContainers == null)
                    _allContainers = ContainerGroups.SelectMany(g => g.Params).ToArray();
                return _allContainers;
            }
        }

        /// <summary>Get container definitions that apply to a specific Revit category name.</summary>
        public static ContainerParamDef[] ContainersForCategory(string categoryName)
        {
            EnsureLoaded();
            if (_containersByCategory == null) BuildCategoryIndex();
            if (string.IsNullOrEmpty(categoryName)) return AllContainers.Where(c => c.Categories == null).ToArray();

            var result = new List<ContainerParamDef>();
            // Universal containers (null categories) always apply
            foreach (var c in AllContainers)
            {
                if (c.Categories == null)
                    result.Add(c);
            }
            // Plus category-specific matches
            if (_containersByCategory.TryGetValue(categoryName, out var specific))
                result.AddRange(specific);

            return result.ToArray();
        }

        /// <summary>Get category display names for a discipline-specific parameter.</summary>
        public static string[] GetCategoryNamesForParam(string paramName)
        {
            EnsureLoaded();
            if (_disciplineCategoryNames != null && _disciplineCategoryNames.TryGetValue(paramName, out string[] cats))
                return cats;
            return Array.Empty<string>();
        }

        /// <summary>Resolve token preset name to index array. Returns raw indices if not a preset name.</summary>
        public static int[] ResolveTokenPreset(string presetOrRaw)
        {
            EnsureLoaded();
            if (TokenPresets.TryGetValue(presetOrRaw, out int[] preset))
                return preset;
            return Array.Empty<int>();
        }

        /// <summary>
        /// Build tuple array matching the legacy format used by BuildTagsCommand and TokenWriterCommands.
        /// Returns (paramName, tokenIndices, separator, categoryNames) for all containers.
        /// </summary>
        public static (string param, int[] tokens, string sep, string[] categories)[] GetContainerTuples()
        {
            EnsureLoaded();
            return AllContainers.Select(c => (c.ParamName, c.TokenIndices, c.Separator, c.Categories)).ToArray();
        }

        /// <summary>
        /// Build the BuiltInCategory array for a discipline parameter, resolving
        /// category display names through the CategoryEnumMap. Used by SharedParamGuids
        /// for Pass 2 binding.
        /// </summary>
        public static BuiltInCategory[] ResolveCategoryEnums(string[] categoryNames)
        {
            if (categoryNames == null || categoryNames.Length == 0) return Array.Empty<BuiltInCategory>();

            var result = new List<BuiltInCategory>();
            foreach (string name in categoryNames)
            {
                if (CategoryEnumMap.TryGetValue(name, out string enumStr))
                {
                    if (Enum.TryParse(enumStr, out BuiltInCategory bic))
                        result.Add(bic);
                }
            }
            return result.ToArray();
        }

        /// <summary>
        /// Resolve the universal category list to BuiltInCategory enums.
        /// </summary>
        public static BuiltInCategory[] ResolveUniversalCategoryEnums()
        {
            EnsureLoaded();
            return ResolveCategoryEnums(UniversalCategories);
        }

        /// <summary>
        /// Build the discipline bindings dictionary in the format SharedParamGuids expects:
        /// paramName → BuiltInCategory[]. Derived from container_groups, then merged with
        /// CATEGORY_BINDINGS.csv (10,661 entries) as the primary data-driven source.
        /// CSV categories augment (never reduce) the container_group base bindings.
        /// </summary>
        public static Dictionary<string, BuiltInCategory[]> BuildDisciplineBindings()
        {
            EnsureLoaded();
            // Phase 1: Base bindings from container_groups (PARAMETER_REGISTRY.json)
            var bindingSets = new Dictionary<string, HashSet<BuiltInCategory>>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in ContainerGroups)
            {
                if (group.Categories == null) continue; // universal — handled by Pass 1
                var enums = ResolveCategoryEnums(group.Categories);
                foreach (var param in group.Params)
                {
                    if (!bindingSets.TryGetValue(param.ParamName, out var set))
                    {
                        set = new HashSet<BuiltInCategory>();
                        bindingSets[param.ParamName] = set;
                    }
                    foreach (var e in enums) set.Add(e);
                }
            }

            // Phase 2: Merge CATEGORY_BINDINGS.csv (data-driven primary source)
            int csvAdded = 0;
            try
            {
                string csvPath = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
                if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
                {
                    bool headerSkipped = false;
                    foreach (string line in File.ReadAllLines(csvPath))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        if (!headerSkipped) { headerSkipped = true; continue; }

                        string[] cols = StingToolsApp.ParseCsvLine(line);
                        if (cols.Length < 2) continue;

                        string paramName = cols[0].Trim();
                        string catName = cols[1].Trim();
                        if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(catName)) continue;

                        // Resolve category name to enum
                        if (!CategoryEnumMap.TryGetValue(catName, out string enumStr)) continue;
                        if (!Enum.TryParse(enumStr, out BuiltInCategory bic)) continue;

                        if (!bindingSets.TryGetValue(paramName, out var set))
                        {
                            set = new HashSet<BuiltInCategory>();
                            bindingSets[paramName] = set;
                        }
                        if (set.Add(bic)) csvAdded++;
                    }
                    if (csvAdded > 0)
                        StingLog.Info($"DisciplineBindings: {csvAdded} categories added from CATEGORY_BINDINGS.csv");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CATEGORY_BINDINGS.csv merge: {ex.Message}");
            }

            // Convert to final format
            var bindings = new Dictionary<string, BuiltInCategory[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in bindingSets)
                bindings[kvp.Key] = kvp.Value.ToArray();
            return bindings;
        }

        /// <summary>Override separator from project_config.json (takes precedence over PARAMETER_REGISTRY.json).</summary>
        public static void OverrideSeparator(string sep) { if (!string.IsNullOrEmpty(sep)) Separator = sep; }

        /// <summary>Override SEQ padding width from project_config.json.</summary>
        public static void OverrideNumPad(int pad) { if (pad >= 1 && pad <= 8) NumPad = pad; }

        /// <summary>Override segment order from project_config.json.</summary>
        public static void OverrideSegmentOrder(string[] order) { if (order != null && order.Length > 0) SegmentOrder = order; }

        /// <summary>Force reload from disk. Call after editing PARAMETER_REGISTRY.json.</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _loaded = false;
                _allContainers = null;
                _containersByCategory = null;
            }
            // Invalidate downstream caches that depend on our data
            SharedParamGuids.InvalidateCache();
            EnsureLoaded();
        }

        // ════════════════════════════════════════════════════════════════
        // Loading
        // ════════════════════════════════════════════════════════════════

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                LoadFromFile();
                _loaded = true;
            }
        }

        private static void LoadFromFile()
        {
            string path = StingToolsApp.FindDataFile("PARAMETER_REGISTRY.json");
            if (path == null || !File.Exists(path))
            {
                StingLog.Warn("PARAMETER_REGISTRY.json not found — using compiled defaults");
                LoadDefaults();
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                JObject root = JObject.Parse(json);

                // Tag format
                var fmt = root["tag_format"];
                if (fmt != null)
                {
                    Separator = fmt["separator"]?.ToString() ?? "-";
                    NumPad = fmt["num_pad"]?.Value<int>() ?? 4;
                    SegmentOrder = fmt["segment_order"]?.ToObject<string[]>() ?? SegmentOrder;
                }

                // Source tokens
                var tokArr = root["source_tokens"] as JArray;
                if (tokArr != null)
                {
                    var tokens = new List<TokenDef>();
                    var tokenNames = new List<string>();
                    foreach (JObject t in tokArr)
                    {
                        var def = new TokenDef
                        {
                            Slot = t["slot"]?.Value<int>() ?? 0,
                            Key = t["key"]?.ToString() ?? "",
                            ParamName = t["param_name"]?.ToString() ?? "",
                            GuidStr = t["guid"]?.ToString() ?? "",
                            Description = t["description"]?.ToString() ?? "",
                        };
                        tokens.Add(def);
                        tokenNames.Add(def.ParamName);
                    }
                    SourceTokens = tokens.OrderBy(t => t.Slot).ToArray();
                    // Build AllTokenParams from sorted SourceTokens to ensure slot ordering matches
                    AllTokenParams = SourceTokens.Select(t => t.ParamName).ToArray();
                }

                // Support params
                var supArr = root["support_params"] as JArray;
                if (supArr != null)
                {
                    foreach (JObject s in supArr)
                    {
                        string name = s["param_name"]?.ToString() ?? "";
                        if (name.Contains("STATUS") && !name.Contains("PARA") && !name.Contains("WARN")) STATUS = name;
                        else if (name.Contains("DETAIL")) DETAIL_NUM = name;
                        else if (name.Contains("MNT")) MNT_TYPE = name;
                        else if (name == "TAG_PARA_STATE_1_BOOL") PARA_STATE_1 = name;
                        else if (name == "TAG_PARA_STATE_2_BOOL") PARA_STATE_2 = name;
                        else if (name == "TAG_PARA_STATE_3_BOOL") PARA_STATE_3 = name;
                        else if (name == "TAG_WARN_VISIBLE_BOOL") WARN_VISIBLE = name;
                        else if (name == "TAG_WARN_SEVERITY_FILTER_TXT") WARN_SEVERITY_FILTER = name;
                    }
                }

                // Token presets
                var presets = root["token_presets"] as JObject;
                TokenPresets = new Dictionary<string, int[]>();
                if (presets != null)
                {
                    foreach (var kvp in presets)
                        TokenPresets[kvp.Key] = kvp.Value.ToObject<int[]>();
                }

                // Container groups
                var groupArr = root["container_groups"] as JArray;
                if (groupArr != null)
                {
                    var groups = new List<ContainerGroupDef>();
                    foreach (JObject g in groupArr)
                    {
                        var groupDef = new ContainerGroupDef
                        {
                            Group = g["group"]?.ToString() ?? "",
                            GroupCode = g["group_code"]?.ToString() ?? "",
                            Categories = g["categories"]?.Type == JTokenType.Null ? null : g["categories"]?.ToObject<string[]>(),
                        };

                        var paramArr = g["params"] as JArray;
                        if (paramArr != null)
                        {
                            var parms = new List<ContainerParamDef>();
                            foreach (JObject p in paramArr)
                            {
                                string tokensRef = p["tokens"]?.ToString() ?? "all";
                                int[] tokenIndices = TokenPresets.TryGetValue(tokensRef, out int[] preset)
                                    ? preset
                                    : (tokensRef.StartsWith("[") ? p["tokens"].ToObject<int[]>() : new int[] { 0,1,2,3,4,5,6,7 });

                                parms.Add(new ContainerParamDef
                                {
                                    ParamName = p["param_name"]?.ToString() ?? "",
                                    GuidStr = p["guid"]?.ToString() ?? "",
                                    TokenIndices = tokenIndices,
                                    TokenPresetName = tokensRef,
                                    Separator = p["separator"]?.ToString() ?? "-",
                                    Prefix = p["prefix"]?.ToString() ?? "",
                                    Suffix = p["suffix"]?.ToString() ?? "",
                                    Description = p["description"]?.ToString() ?? "",
                                    Categories = groupDef.Categories,
                                });
                            }
                            groupDef.Params = parms.ToArray();
                        }
                        else
                        {
                            groupDef.Params = Array.Empty<ContainerParamDef>();
                        }

                        groups.Add(groupDef);
                    }
                    ContainerGroups = groups.ToArray();
                }

                // Set convenience names from first container group (Universal)
                if (ContainerGroups.Length > 0 && ContainerGroups[0].Params.Length >= 6)
                {
                    TAG1 = ContainerGroups[0].Params[0].ParamName;
                    TAG2 = ContainerGroups[0].Params[1].ParamName;
                    TAG3 = ContainerGroups[0].Params[2].ParamName;
                    TAG4 = ContainerGroups[0].Params[3].ParamName;
                    TAG5 = ContainerGroups[0].Params[4].ParamName;
                    TAG6 = ContainerGroups[0].Params[5].ParamName;
                    if (ContainerGroups[0].Params.Length >= 7)
                        TAG7 = ContainerGroups[0].Params[6].ParamName;
                    if (ContainerGroups[0].Params.Length >= 8)
                        TAG7A = ContainerGroups[0].Params[7].ParamName;
                    if (ContainerGroups[0].Params.Length >= 9)
                        TAG7B = ContainerGroups[0].Params[8].ParamName;
                    if (ContainerGroups[0].Params.Length >= 10)
                        TAG7C = ContainerGroups[0].Params[9].ParamName;
                    if (ContainerGroups[0].Params.Length >= 11)
                        TAG7D = ContainerGroups[0].Params[10].ParamName;
                    if (ContainerGroups[0].Params.Length >= 12)
                        TAG7E = ContainerGroups[0].Params[11].ParamName;
                    if (ContainerGroups[0].Params.Length >= 13)
                        TAG7F = ContainerGroups[0].Params[12].ParamName;
                }

                // Category enum map
                var catMap = root["category_enum_map"] as JObject;
                CategoryEnumMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (catMap != null)
                {
                    foreach (var kvp in catMap)
                        CategoryEnumMap[kvp.Key] = kvp.Value.ToString();
                }

                // Universal categories
                UniversalCategories = root["universal_categories"]?.ToObject<string[]>() ?? Array.Empty<string>();

                // Load extended params
                LoadExtendedParams(root);

                // Build GUID lookups
                BuildGuidMaps(root);

                // Build universal params list
                BuildUniversalParams(root);

                // Build discipline category name mappings
                BuildDisciplineCategoryNames();

                StingLog.Info($"ParamRegistry loaded: {SourceTokens.Length} tokens, {ContainerGroups.Length} groups, {AllContainers.Length} containers, {_guidByName?.Count ?? 0} GUIDs");
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to load PARAMETER_REGISTRY.json", ex);
                LoadDefaults();
            }
        }

        private static void LoadExtendedParams(JObject root)
        {
            _extendedParams = new Dictionary<string, string>(StringComparer.Ordinal);
            var ext = root["extended_params"] as JObject;
            if (ext == null) return;

            foreach (var group in ext)
            {
                var arr = group.Value as JArray;
                if (arr == null) continue;
                foreach (JObject item in arr)
                {
                    string key = item["key"]?.ToString();
                    string paramName = item["param_name"]?.ToString();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(paramName))
                        _extendedParams[key] = paramName;
                }
            }
        }

        private static void BuildGuidMaps(JObject root)
        {
            _guidByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
            _nameByGuid = new Dictionary<Guid, string>();

            // Source tokens
            foreach (var tok in SourceTokens)
            {
                if (Guid.TryParse(tok.GuidStr, out Guid g))
                {
                    _guidByName[tok.ParamName] = g;
                    _nameByGuid[g] = tok.ParamName;
                }
            }

            // Support params
            var supArr = root["support_params"] as JArray;
            if (supArr != null)
            {
                foreach (JObject s in supArr)
                {
                    string name = s["param_name"]?.ToString();
                    string guidStr = s["guid"]?.ToString();
                    if (!string.IsNullOrEmpty(name) && Guid.TryParse(guidStr, out Guid g))
                    {
                        _guidByName[name] = g;
                        _nameByGuid[g] = name;
                    }
                }
            }

            // Container params
            foreach (var group in ContainerGroups)
            {
                foreach (var p in group.Params)
                {
                    if (Guid.TryParse(p.GuidStr, out Guid g))
                    {
                        _guidByName[p.ParamName] = g;
                        _nameByGuid[g] = p.ParamName;
                    }
                }
            }

            // Extended params (iso19650_naming, paragraph_containers, warning_thresholds, etc.)
            var ext = root["extended_params"] as JObject;
            if (ext != null)
            {
                foreach (var group in ext)
                {
                    var arr = group.Value as JArray;
                    if (arr == null) continue;
                    foreach (JObject item in arr)
                    {
                        string name = item["param_name"]?.ToString();
                        string guidStr = item["guid"]?.ToString();
                        if (!string.IsNullOrEmpty(name) && Guid.TryParse(guidStr, out Guid g))
                        {
                            _guidByName[name] = g;
                            _nameByGuid[g] = name;
                        }
                    }
                }
            }
        }

        private static void BuildUniversalParams(JObject root)
        {
            var list = new List<string>();
            // All source tokens are universal
            list.AddRange(AllTokenParams);
            // All universal container params
            if (ContainerGroups.Length > 0 && ContainerGroups[0].Categories == null)
            {
                foreach (var p in ContainerGroups[0].Params)
                    list.Add(p.ParamName);
            }
            // Support params
            var supArr = root["support_params"] as JArray;
            if (supArr != null)
            {
                foreach (JObject s in supArr)
                {
                    string name = s["param_name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                        list.Add(name);
                }
            }
            UniversalParams = list.Distinct().ToArray();
        }

        private static void BuildDisciplineCategoryNames()
        {
            _disciplineCategoryNames = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var group in ContainerGroups)
            {
                if (group.Categories == null) continue;
                foreach (var param in group.Params)
                    _disciplineCategoryNames[param.ParamName] = group.Categories;
            }
        }

        private static void BuildCategoryIndex()
        {
            _containersByCategory = new Dictionary<string, List<ContainerParamDef>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in AllContainers)
            {
                if (c.Categories == null) continue;
                foreach (string cat in c.Categories)
                {
                    if (!_containersByCategory.TryGetValue(cat, out var list))
                    {
                        list = new List<ContainerParamDef>();
                        _containersByCategory[cat] = list;
                    }
                    list.Add(c);
                }
            }
        }

        /// <summary>Fallback defaults matching the original hardcoded values.</summary>
        private static void LoadDefaults()
        {
            Separator = "-";
            NumPad = 4;
            SegmentOrder = new[] { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };

            AllTokenParams = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            };

            SourceTokens = new[]
            {
                new TokenDef { Slot = 0, Key = "DISC", ParamName = "ASS_DISCIPLINE_COD_TXT", GuidStr = "8c7dcfd7-f922-52d0-b859-81cae8d17dc0" },
                new TokenDef { Slot = 1, Key = "LOC",  ParamName = "ASS_LOC_TXT",             GuidStr = "b7469c27-c80e-5b59-b999-1a99ba620cd1" },
                new TokenDef { Slot = 2, Key = "ZONE", ParamName = "ASS_ZONE_TXT",            GuidStr = "dc0d940f-e4ce-5e73-a0a7-fc7094148c84" },
                new TokenDef { Slot = 3, Key = "LVL",  ParamName = "ASS_LVL_COD_TXT",        GuidStr = "b1e51fab-fa88-50df-8b2f-bcdbe48e7c78" },
                new TokenDef { Slot = 4, Key = "SYS",  ParamName = "ASS_SYSTEM_TYPE_TXT",     GuidStr = "2b3658d9-bfc6-56db-9df5-901337fde0f5" },
                new TokenDef { Slot = 5, Key = "FUNC", ParamName = "ASS_FUNC_TXT",            GuidStr = "1ddff9a8-6e66-4a93-88fe-f3b94fbd5710" },
                new TokenDef { Slot = 6, Key = "PROD", ParamName = "ASS_PRODCT_COD_TXT",      GuidStr = "082a2a05-3387-5501-b355-51dd45e23e9f" },
                new TokenDef { Slot = 7, Key = "SEQ",  ParamName = "ASS_SEQ_NUM_TXT",         GuidStr = "bbe1cd55-247b-48bd-94ba-a08031f06d5b" },
            };

            TAG1 = "ASS_TAG_1_TXT"; TAG2 = "ASS_TAG_2_TXT"; TAG3 = "ASS_TAG_3_TXT";
            TAG4 = "ASS_TAG_4_TXT"; TAG5 = "ASS_TAG_5_TXT"; TAG6 = "ASS_TAG_6_TXT";
            TAG7 = "ASS_TAG_7_TXT";
            TAG7A = "ASS_TAG_7A_TXT"; TAG7B = "ASS_TAG_7B_TXT"; TAG7C = "ASS_TAG_7C_TXT";
            TAG7D = "ASS_TAG_7D_TXT"; TAG7E = "ASS_TAG_7E_TXT"; TAG7F = "ASS_TAG_7F_TXT";
            STATUS = "ASS_STATUS_TXT"; DETAIL_NUM = "ASS_INST_DETAIL_NUM_TXT"; MNT_TYPE = "MNT_TYPE_TXT";

            TokenPresets = new Dictionary<string, int[]>
            {
                { "all", new[] {0,1,2,3,4,5,6,7} },
                { "short_id", new[] {0,6,7} },
                { "location", new[] {1,2,3} },
                { "system", new[] {4,5} },
                { "sys_ref", new[] {4,5,6} },
                { "line1", new[] {0,1,2,3} },
                { "line2", new[] {4,5,6,7} },
            };

            // Extended params defaults
            _extendedParams = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Identity
                { "ID", "ASS_ID_TXT" }, { "DESC", "ASS_DESCRIPTION_TXT" },
                { "MFR", "ASS_MANUFACTURER_TXT" }, { "MODEL", "ASS_MODEL_NR_TXT" },
                { "TYPE_NAME", "ASS_TYPE_NAME_TXT" }, { "FAMILY_NAME", "ASS_FAMILY_NAME_TXT" },
                { "CAT", "ASS_CAT_TXT" }, { "TYPE_MARK", "ASS_TYPE_MARK_TXT" },
                { "TYPE_COMMENTS", "ASS_TYPE_COMMENTS_TXT" }, { "KEYNOTE", "ASS_KEYNOTE_TXT" },
                { "UNIFORMAT", "ASS_UNIFORMAT_TXT" }, { "UNIFORMAT_DESC", "ASS_UNIFORMAT_DESC_TXT" },
                { "OMNICLASS", "ASS_OMNICLASS_TXT" }, { "SIZE", "ASS_SIZE_TXT" },
                { "COST", "ASS_CST_UNIT_PRICE_UGX_NR" }, { "PRJ_COMMENTS", "PRJ_COMMENTS_TXT" },
                // Spatial
                { "ROOM_NAME", "ASS_ROOM_NAME_TXT" }, { "ROOM_NUM", "ASS_ROOM_NUM_TXT" },
                { "ROOM_AREA", "ASS_ROOM_AREA_SQ_M" }, { "ROOM_VOLUME", "ASS_ROOM_VOLUME_CU_M" },
                { "DEPT", "ASS_DEPARTMENT_ASSIGNMENT_TXT" }, { "GRID_REF", "PRJ_GRID_REF_TXT" },
                { "BLE_ROOM_NAME", "BLE_ROOM_NAME_TXT" }, { "BLE_ROOM_NUM", "BLE_ROOM_NUMBER_TXT" },
                // Extended tokens
                { "ORIGIN", "ASS_ORIGIN_TXT" }, { "PROJECT", "ASS_PROJECT_TXT" },
                { "REV", "ASS_REV_TXT" }, { "VOLUME", "ASS_VOL_TXT" },
                // BLE dimensional
                { "WALL_HEIGHT", "BLE_WALL_HEIGHT_MM" }, { "WALL_LENGTH", "BLE_WALL_LENGTH_MM" },
                { "WALL_THICKNESS", "BLE_WALL_THICKNESS_MM" }, { "DOOR_WIDTH", "BLE_DOOR_WIDTH_MM" },
                { "DOOR_HEIGHT", "BLE_DOOR_HEIGHT_MM" }, { "WINDOW_WIDTH", "BLE_WINDOW_WIDTH_MM" },
                { "WINDOW_HEIGHT", "BLE_WINDOW_HEIGHT_MM" },
                { "WINDOW_SILL", "BLE_WINDOW_SILL_HEIGHT_FROM_FLR_MM" },
                { "FLR_THICKNESS", "BLE_FLR_THICKNESS_MM" }, { "ELE_AREA", "BLE_ELE_AREA_SQ_M" },
                { "CEILING_HEIGHT", "BLE_CEILING_HEIGHT_MM" }, { "ROOF_SLOPE", "BLE_ROOF_SLOPE_DEG" },
                { "STAIR_TREAD", "BLE_STAIR_GOING_MM" }, { "STAIR_RISE", "BLE_STAIR_RISE_MM" },
                { "STAIR_WIDTH", "BLE_STAIR_WIDTH_MM" }, { "RAMP_SLOPE", "BLE_RAMP_SLOPE_PCT" },
                { "RAMP_WIDTH", "BLE_RAMP_WIDTH_MM" }, { "STRUCT_TYPE", "BLE_STRUCT_ELE_TYPE_TXT" },
                { "FIRE_RATING", "FLS_PROT_FLS_RESISTANCE_RATING_MINUTES_MIN" },
                // Electrical
                { "ELC_POWER", "ELC_CKT_PWR_KW" }, { "ELC_VOLTAGE", "ELC_CKT_VLT_V" },
                { "ELC_CIRCUIT_NR", "ELC_CKT_NR" }, { "ELC_PNL_NAME", "ELC_PNL_DESIGNATION_NAME_TXT" },
                { "ELC_PNL_VOLTAGE", "ELC_VLT_PRIMARY_RATING_V" }, { "ELC_PHASES", "ELC_CKT_PHASE_COUNT_NR" },
                { "ELC_PNL_LOAD", "ELC_PNL_CONNECTED_LOAD_KW" }, { "ELC_PNL_FED_FROM", "ELC_PNL_FED_FROM_PNL_TXT" },
                { "ELC_MAIN_BRK", "ELC_PNL_MAIN_BRK_A" }, { "ELC_WAYS", "ELC_PNL_NUM_OF_WAYS_NR" },
                { "ELC_IP_RATING", "ELC_IP_RATING_TXT" },
                // Lighting
                { "LTG_WATTAGE", "LTG_FIX_LMP_WATTAGE_W" }, { "LTG_LUMENS", "CST_FIX_LUMEN_OUTPUT_LM" },
                { "LTG_EFFICACY", "LTG_FIX_EFFICACY_LM_W" }, { "LTG_LAMP_TYPE", "LTG_FIX_LAMP_TYPE_TXT" },
                // HVAC
                { "HVC_DUCT_FLOW", "HVC_DCT_FLW_CFM" }, { "HVC_VELOCITY", "HVC_VEL_MPS" },
                { "HVC_PRESSURE", "HVC_PRESSURE_DROP_PA" }, { "HVC_AIRFLOW", "HVC_AIRFLOW_LPS" },
                // Plumbing
                { "PLM_PIPE_FLOW", "PLM_PPE_FLW_LPS" }, { "PLM_PIPE_SIZE", "PLM_PPE_SZ_MM" },
                { "PLM_VELOCITY", "PLM_VEL_MPS" }, { "PLM_FLOW_RATE", "PLM_FLOW_RATE_LPS" },
                { "PLM_PIPE_LENGTH", "PLM_PPE_LENGTH_M" },
                // Volume, length, head heights, function
                { "ELE_VOLUME", "BLE_ELE_VOLUME_CU_M" }, { "ELE_LENGTH", "BLE_ELE_LENGTH_M" },
                { "DOOR_HEAD_HT", "BLE_DOOR_HEAD_HEIGHT_MM" }, { "DOOR_FUNC", "BLE_DOOR_FUNCTION_TXT" },
                { "WINDOW_HEAD_HT", "BLE_WINDOW_HEAD_HEIGHT_MM" },
                // Room finishes
                { "ROOM_FINISH_FLR", "BLE_ROOM_FINISH_FLOOR_TXT" },
                { "ROOM_FINISH_WALL", "BLE_ROOM_FINISH_WALL_TXT" },
                { "ROOM_FINISH_CLG", "BLE_ROOM_FINISH_CEILING_TXT" },
                { "ROOM_FINISH_BASE", "BLE_ROOM_FINISH_BASE_TXT" },
                // Duct dimensions
                { "HVC_DUCT_WIDTH", "HVC_DCT_WIDTH_MM" }, { "HVC_DUCT_HEIGHT", "HVC_DCT_HEIGHT_MM" },
                { "HVC_INSULATION", "HVC_INS_THICKNESS_MM" }, { "HVC_DUCT_LENGTH", "HVC_DCT_LENGTH_M" },
                // ISO 19650 naming
                { "PROJECT_COD", "ASS_PROJECT_COD_TXT" }, { "ORIGINATOR_COD", "ASS_ORIGINATOR_COD_TXT" },
                { "VOLUME_COD", "ASS_VOLUME_COD_TXT" }, { "STATUS_COD", "ASS_STATUS_COD_TXT" },
                { "REV_COD", "ASS_REV_COD_TXT" },
                // Paragraph containers
                { "PARA_WALL", "ARCH_TAG_7_PARA_WALL_TXT" }, { "PARA_FLOOR", "ARCH_TAG_7_PARA_FLOOR_TXT" },
                { "PARA_CEIL", "ARCH_TAG_7_PARA_CEIL_TXT" }, { "PARA_ROOF", "ARCH_TAG_7_PARA_ROOF_TXT" },
                { "PARA_DOOR", "ARCH_TAG_7_PARA_DOOR_TXT" }, { "PARA_WIN", "ARCH_TAG_7_PARA_WIN_TXT" },
                { "PARA_STAIR", "ARCH_TAG_7_PARA_STAIR_TXT" }, { "PARA_RAMP", "ARCH_TAG_7_PARA_RAMP_TXT" },
                { "PARA_ROOM", "ARCH_TAG_7_PARA_ROOM_TXT" }, { "PARA_FACADE", "ARCH_TAG_7_PARA_FACADE_TXT" },
                { "PARA_CASEWORK", "ARCH_TAG_7_PARA_CASEWORK_TXT" }, { "PARA_FURNITURE", "ARCH_TAG_7_PARA_FURNITURE_TXT" },
                { "PARA_STR_FDN", "STR_TAG_7_PARA_FDN_TXT" }, { "PARA_STR_COL", "STR_TAG_7_PARA_COL_TXT" },
                { "PARA_STR_BEAM", "STR_TAG_7_PARA_BEAM_TXT" },
                { "PARA_HVC_SPEC", "HVC_TAG_7_PARA_SPEC_TXT" }, { "PARA_HVC_DUCT", "HVC_TAG_7_PARA_DUCT_TXT" },
                { "PARA_HVC_AT", "HVC_TAG_7_PARA_AT_TXT" },
                // MEP paragraph containers
                { "PARA_ELC_PANEL", "ELC_TAG_7_PARA_PANEL_TXT" }, { "PARA_ELC_CIRCUIT", "ELC_TAG_7_PARA_CIRCUIT_TXT" },
                { "PARA_LTG_SPEC", "LTG_TAG_7_PARA_SPEC_TXT" },
                { "PARA_PLM_FIXTURE", "PLM_TAG_7_PARA_FIXTURE_TXT" }, { "PARA_PLM_PIPE", "PLM_TAG_7_PARA_PIPE_TXT" },
                { "PARA_FLS_FA", "FLS_TAG_7_PARA_FA_TXT" }, { "PARA_FLS_SPR", "FLS_TAG_7_PARA_SPR_TXT" },
                { "PARA_COM_BMS", "COM_TAG_7_PARA_BMS_TXT" },
                // Extended paragraph containers (v4.3)
                { "PARA_HVC_FLEXDUCT", "HVC_TAG_7_PARA_FLEXDUCT_TXT" }, { "PARA_HVC_DCTACC", "HVC_TAG_7_PARA_DCTACC_TXT" },
                { "PARA_ELC_CONDUIT", "ELC_TAG_7_PARA_CONDUIT_TXT" }, { "PARA_ELC_TRAY", "ELC_TAG_7_PARA_TRAY_TXT" },
                { "PARA_ELC_CABLE", "ELC_TAG_7_PARA_CABLE_TXT" },
                { "PARA_PLM_EQUIP", "PLM_TAG_7_PARA_EQUIP_TXT" }, { "PARA_PLM_PIPEACC", "PLM_TAG_7_PARA_PIPEACC_TXT" },
                { "PARA_PLM_DRAIN", "PLM_TAG_7_PARA_DRAIN_TXT" },
                { "PARA_ICT_DATA", "ICT_TAG_7_PARA_DATA_TXT" }, { "PARA_NCL", "NCL_TAG_7_PARA_TXT" },
                { "PARA_SEC", "SEC_TAG_7_PARA_TXT" }, { "PARA_ASS_EQUIP", "ASS_TAG_7_PARA_EQUIP_TXT" },
                { "PARA_RGL_CMPL", "RGL_TAG_7_PARA_CMPL_TXT" }, { "PARA_PER_ENV", "PER_TAG_7_PARA_ENV_TXT" },
                { "PARA_CST_CONC", "CST_TAG_7_PARA_CONC_TXT" },
                // Warning threshold
                { "ELC_PNL_RATED", "ELC_PNL_RATED_BOOL" },
                // ISO 19650 project-level naming (PRJ_ prefix to avoid collision with ASS_ set above)
                { "PRJ_PROJECT_COD", "PRJ_PROJECT_COD_TXT" }, { "PRJ_ORIGINATOR_COD", "PRJ_ORIGINATOR_COD_TXT" },
                { "PRJ_VOLUME_COD", "PRJ_VOLUME_COD_TXT" }, { "PRJ_STATUS_COD", "PRJ_STATUS_COD_TXT" },
                { "PRJ_REV_COD", "PRJ_REV_COD_TXT" },
            };

            ContainerGroups = Array.Empty<ContainerGroupDef>();
            UniversalParams = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
                "ASS_TAG_1_TXT", "ASS_TAG_2_TXT", "ASS_TAG_3_TXT",
                "ASS_TAG_4_TXT", "ASS_TAG_5_TXT", "ASS_TAG_6_TXT",
                "ASS_STATUS_TXT", "ASS_INST_DETAIL_NUM_TXT", "MNT_TYPE_TXT",
            };
        }

        // ════════════════════════════════════════════════════════════════
        // Helper: assemble container value from token values
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Assemble a container tag string from token values using the container's
        /// token indices and separator. Shared logic used by all combine/build commands.
        /// </summary>
        public static string AssembleContainer(ContainerParamDef container, string[] tokenValues)
        {
            if (container.TokenIndices == null || container.TokenIndices.Length == 0)
                return "";
            var parts = new List<string>();
            bool anyValue = false;
            foreach (int idx in container.TokenIndices)
            {
                string val = idx >= 0 && idx < tokenValues.Length ? tokenValues[idx] : "";
                parts.Add(val);
                if (!string.IsNullOrEmpty(val)) anyValue = true;
            }
            if (!anyValue) return "";

            string assembled = string.Join(container.Separator, parts);
            if (!string.IsNullOrEmpty(container.Prefix)) assembled = container.Prefix + assembled;
            if (!string.IsNullOrEmpty(container.Suffix)) assembled = assembled + container.Suffix;
            return assembled;
        }

        /// <summary>
        /// Read all 8 token values from an element into an array matching AllTokenParams order.
        /// </summary>
        public static string[] ReadTokenValues(Element el)
        {
            EnsureLoaded();
            string[] values = new string[AllTokenParams.Length];
            for (int i = 0; i < AllTokenParams.Length; i++)
                values[i] = ParameterHelpers.GetString(el, AllTokenParams[i]);
            return values;
        }

        /// <summary>
        /// Write all applicable containers for an element based on its category.
        /// Returns count of containers written.
        /// TAG7 is always skipped here — it requires the narrative builder
        /// (TagConfig.BuildTag7Narrative) rather than simple token concatenation.
        /// </summary>
        public static int WriteContainers(Element el, string[] tokenValues, string categoryName,
            bool overwrite = true, string skipParam = null)
        {
            int written = 0;
            var containers = ContainersForCategory(categoryName);
            foreach (var c in containers)
            {
                if (c.ParamName == skipParam) continue;
                // TAG7 + sub-sections use the narrative builder, not token concatenation
                if (IsTag7Param(c.ParamName)) continue;
                string assembled = AssembleContainer(c, tokenValues);
                if (!string.IsNullOrEmpty(assembled))
                {
                    if (ParameterHelpers.SetString(el, c.ParamName, assembled, overwrite))
                        written++;
                }
            }
            return written;
        }

        // ════════════════════════════════════════════════════════════════
        // Data types
        // ════════════════════════════════════════════════════════════════

        public class TokenDef
        {
            public int Slot { get; set; }
            public string Key { get; set; }
            public string ParamName { get; set; }
            public string GuidStr { get; set; }
            public string Description { get; set; }
        }

        public class ContainerGroupDef
        {
            public string Group { get; set; }
            public string GroupCode { get; set; }
            public string[] Categories { get; set; }
            public ContainerParamDef[] Params { get; set; }
        }

        public class ContainerParamDef
        {
            public string ParamName { get; set; }
            public string GuidStr { get; set; }
            public int[] TokenIndices { get; set; }
            public string TokenPresetName { get; set; }
            public string Separator { get; set; }
            public string Prefix { get; set; }
            public string Suffix { get; set; }
            public string Description { get; set; }
            public string[] Categories { get; set; }
        }
    }
}
