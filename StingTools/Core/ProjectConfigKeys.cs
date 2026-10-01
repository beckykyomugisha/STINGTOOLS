using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// TAGACC-26: the keys <c>project_config.json</c> may hold, in one place.
    ///
    /// <c>TagConfig.LoadFromFile</c> warns about any key it does not recognise ("check for
    /// typos"). Its list was kept by hand inside the loader and had drifted both ways: about
    /// 70 keys the plugin really reads — every COST_* / BOQ_TENDER_* rate, the SLA hours, and
    /// even keys the loader itself reads or <c>SaveToFile</c> writes (CATEGORY_VISUAL_POLICY,
    /// FOLDER_CODE_SUFFIX, DEFAULT_COLLISION_MODE) — were reported as typos, while four keys
    /// on the list are read by nothing. <c>ProjectConfigKeysTests</c> rebuilds the set from
    /// the source (GetConfig* reads, SetConfigValue writes, the loader and SaveToFile) and
    /// fails when this list and the code disagree.
    /// </summary>
    internal static class ProjectConfigKeys
    {
        /// <summary>Keys some part of the plugin reads or writes.</summary>
        public static readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal)
        {
            "ACTIVE_PRESET", "ACTIVE_SECTOR_PACK", "AUTO_CORRECT_STATUS_FROM_PHASE", "AUTO_CREATE_CDE_FOLDERS",
            "AUTO_NEXT_REVISION_ON_ISSUE", "AUTO_RUN_WORKFLOW_ON_OPEN", "AUTO_SAVE_BASELINE_ON_REVISION",
            "AUTO_SAVE_WARNING_BASELINE", "AUTO_TAGGER_DISC_FILTER", "AUTO_TAGGER_ENABLED",
            "AUTO_TAGGER_STALE_MARKER", "AUTO_TAGGER_VISUAL", "BCC_HIDDEN_ISO_REVISIONS",
            "BOQ_TENDER_CONTINGENCY_PCT", "BOQ_TENDER_GIA_M2", "BOQ_TENDER_OHP_PCT", "BOQ_TENDER_PRELIMINARIES_PCT",
            "BOQ_TENDER_PROJECT_TYPE", "BOQ_TENDER_UGX_PER_USD", "BOQ_TENDER_VAT_PCT", "CARBON_A4_DISTANCE_KM",
            "CARBON_C2_DISTANCE_KM", "CATEGORY_FORCE_SYS", "CATEGORY_SKIP", "CATEGORY_TOKEN_OVERRIDES",
            "CATEGORY_VISUAL_POLICY", "CDE_FIRST_LAYOUT", "CDE_PUBLISHED_MIN_COMPLIANCE",
            "CDE_SHARED_MIN_COMPLIANCE", "COBIE_STREAM_BATCH_SIZE", "COMPLIANCE_GATE_PCT", "CONCRETE_OVERORDER_PCT",
            "COST_AGGREGATE_SIMILAR", "COST_AUTO_REFRESH", "COST_AUTO_REFRESH_QUIET_MS", "COST_BIG_MODEL_THRESHOLD",
            "COST_BILL_PRIMARY_OPTION_ONLY", "COST_CARBON_RAG_AMBER_KGM2", "COST_CARBON_RAG_GREEN_KGM2",
            "COST_COMPOUND_TAKEOFF", "COST_CONTINGENCY_PCT", "COST_CONTRACT_SUM_UGX", "COST_DAYWORK_LABOUR_PCT",
            "COST_DAYWORK_MATERIALS_PCT", "COST_DAYWORK_PLANT_PCT", "COST_DEFAULT_WASTE_PCT",
            "COST_FLUCTUATIONS_UGX", "COST_HEAD_OFFICE_OHP_PCT", "COST_MEASUREMENT_STANDARD",
            "COST_MIN_RATE_CONFIDENCE_EXPORT", "COST_OVERHEAD_PROFIT_PCT", "COST_PRELIMINARIES_PCT",
            "COST_RATES_FILE", "COST_RATE_CONFIDENCE_FLOOR", "COST_REQUIRE_SPEC_FOR_TENDER",
            "COST_RETENTION_EXCLUDES_MOS", "COST_RETENTION_FIRST_MOIETY_PCT", "COST_RETENTION_HALF_AT_PCT",
            "COST_RETENTION_PCT", "COST_SITE_DAYS_PER_WEEK", "COST_TAKEOFF_EXCLUDE_CATEGORIES",
            "COST_TENDER_OUTLIER_PCT", "COST_WEEKLY_PRELIMS_UGX", "CURRENT_DATA_DROP", "CUSTOM_VALID_DISC",
            "CUSTOM_VALID_FUNC", "CUSTOM_VALID_LOC", "CUSTOM_VALID_SYS", "CUSTOM_VALID_ZONE", "CVR_PROVISIONS_UGX",
            "DEFAULT_COLLISION_MODE", "DEFAULT_TAG_STYLE", "DISCIPLINE_LEADS", "DISCIPLINE_PROFILES", "DISC_MAP",
            "EXCEL_IMPORT_BATCH_SIZE", "FAMILY_LIBRARY_LOCAL_ZIP", "FAMILY_LIBRARY_SHA256", "FAMILY_LIBRARY_URL",
            "FOLDER_CODE_SUFFIX", "FORMULA_CACHE_TTL_MINUTES", "FUNC_MAP",
            "GRID_CACHE_TTL_MINUTES", "KUT_ACC_GAP_VALUE_FLOOR", "LAST_WORKFLOW_NAME", "LEADER_CLEARANCE_MARGIN_FT",
            "LIVE_CLASH_TRIGGERS_ENABLED", "LOC_CODES", "LOC_CODES_EXTRA", "LOC_PATTERNS", "NOTIFY_ISSUE_CREATE",
            "NOTIFY_SLA_VIOLATION", "OPEN_EXPORTS_AUTOMATICALLY", "PERF_TRACKING_ENABLED", "PRJ_COUNTRY",
            "PRJ_VOLUME_CODE", "PROD_MAP", "PROJECT_BUDGET_UGX", "PROJECT_TYPE", "PROPAGATE_REV_ON_CREATE",
            "PROXIMITY_RADIUS_FT", "PROXIMITY_RADIUS_M", "PROXIMITY_RADIUS_MM", "REBAR_LAP_ALLOWANCE_PCT",
            "RENUMBER_ON_OVERWRITE", "RESOLVE_BATCH_SIZE", "RETAG_MOVED_ELEMENTS", "REV_DEFAULT", "RIBA_STAGE",
            "SEPARATOR_HISTORY", "SEQ_INCLUDE_LOC", "SEQ_INCLUDE_ZONE", "SEQ_LOCK_MODE", "SEQ_RANGE_ALLOCATION",
            "SEQ_SCHEME", "SHEET_MARGINS", "SHEET_NAMING_STRICT_MODE", "SITE_CART_BULKING_FACTOR",
            "SITE_CUT_REUSE_FRACTION", "SLA_THRESHOLDS", "STALE_WARNING_THRESHOLD", "STATUS_DEFAULT", "SYS_MAP",
            "TAG1_ONLY", "TAG_FORMAT", "TAG_PREFIX", "TAG_SUFFIX", "TITLE_BLOCK_FAMILY",
            "TRANSMITTAL_CONTAINER_THRESHOLD", "TRANSMITTAL_MIN_COMPLIANCE", "TRANSMITTAL_TAG_THRESHOLD",
            "UGX_PER_GBP", "UGX_PER_USD", "USER_ROLE", "VALIDATE_STRICT_MODE", "WARNING_SLA_CRITICAL_HOURS",
            "WARNING_SLA_HIGH_HOURS", "WARNING_SLA_LOW_HOURS", "WARNING_SLA_MEDIUM_HOURS",
            "WARNING_SUPPRESS_PATTERNS", "WRITE_COST_ON_TAG", "ZONE_CODES", "ZONE_PATTERNS", "tag3DFamilyPath"
        };

        /// <summary>
        /// Keys other commands write into <c>project_config.json</c> with their own JObject
        /// code (so the source scan in the test cannot find them). Checked by hand 2026-10-01.
        /// </summary>
        public static readonly HashSet<string> WrittenElsewhere = new HashSet<string>(StringComparer.Ordinal)
        {
            "TAG_RULES",                  // Tag Rule Engine (TagIntelligenceCommands)
            "HANDOVER_MODE",              // ApplyParagraphPresetCommand
            "permissions", "USER_ROLE_CHANGED", // WarningsManager
            "sld_sync_enabled",           // SLDSyncToggleCommand / SLDSyncUpdater
            "OUTPUT_DIRECTORY",           // OutputLocationHelper
            "WARNING_SUPPRESSIONS", "WORKFLOW_SCHEDULES", // Phase75Enhancements
            "SCALE_TIERS", "SCALE_CATEGORY_MULTIPLIERS",  // ScaleTiers
            "PROJECT_FOLDER_ROOT",        // ProjectFolderEngine
        };

        /// <summary>Families of keys built at run time (prefix + suffix).</summary>
        public static readonly string[] KnownPrefixes = { "BOQ_TENDER_" };

        /// <summary>
        /// Keys that were once documented but that nothing applies. Setting one has no effect,
        /// and the loader says so rather than calling it a typo or staying silent.
        /// </summary>
        public static readonly Dictionary<string, string> NotApplied = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEQ_LEVEL_RESET"] = "SEQ numbers are not reset per level; the setting is not implemented",
            ["DD_SCHEDULE"] = "no data-drop schedule reads it",
            ["DD_REQUIREMENTS"] = "no data-drop check reads it",
            ["TRADE_DURATION_OVERRIDES"] = "no 4D scheduling code reads it",
        };

        public enum KeyStatus { Known, NotApplied, Unknown }

        public static KeyStatus Classify(string key)
        {
            if (string.IsNullOrEmpty(key)) return KeyStatus.Unknown;
            if (NotApplied.ContainsKey(key)) return KeyStatus.NotApplied;
            if (Known.Contains(key) || WrittenElsewhere.Contains(key)) return KeyStatus.Known;
            if (KnownPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal) && key.Length > p.Length))
                return KeyStatus.Known;
            return KeyStatus.Unknown;
        }
    }
}
