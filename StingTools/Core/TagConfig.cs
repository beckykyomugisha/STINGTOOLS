using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core.Drawing;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace StingTools.Core
{
    /// <summary>
    /// Controls how tag collisions (duplicate tags) are handled during tagging operations.
    /// </summary>
    public enum TagCollisionMode
    {
        /// <summary>Auto-increment SEQ until a unique tag is found (default).</summary>
        AutoIncrement,
        /// <summary>Skip elements that already have a complete tag — do not modify.</summary>
        Skip,
        /// <summary>Overwrite existing tags with newly generated values.</summary>
        Overwrite,
    }

    // SeqScheme enum relocated to Core/SeqAssigner.cs (same namespace) alongside
    // the pure sequence-assignment logic it parameterises.



    /// <summary>
    /// Ported from tag_config.py — project-level ISO 19650 token lookup tables.
    /// Loads from project_config.json; falls back to built-in defaults that mirror
    /// Sheet 02-TAG-FAMILY-CONFIG from the STINGTOOLS template workbook.
    /// </summary>
    public static class TagConfig
    {
        public static int NumPad => ParamRegistry.NumPad;
        public static string Separator => ParamRegistry.Separator;
        public static string[] SegmentOrder => ParamRegistry.SegmentOrder;
        public const int MaxCollisionDepth = 10000;

        /// <summary>
        /// TW-02: Configurable SEQ zero-pad width. Defaults to NumPad (4) but can be
        /// overridden independently (e.g., 2 for small projects, 6 for large estates).
        /// </summary>
        public static int SeqPadWidth { get; internal set; } = 4;

        /// <summary>
        /// TW-03: Optional tag prefix prepended before the first segment.
        /// Example: "PRJ" produces "PRJ-M-BLD1-Z01-L01-HVAC-SUP-AHU-0001".
        /// </summary>
        public static string TagPrefix { get; internal set; } = "";

        /// <summary>
        /// TW-03: Optional tag suffix appended after the last segment.
        /// Example: "R01" produces "M-BLD1-Z01-L01-HVAC-SUP-AHU-0001-R01".
        /// </summary>
        public static string TagSuffix { get; internal set; } = "";

        /// <summary>
        /// Per-discipline tagging profiles loaded from DISCIPLINE_PROFILES in project_config.json.
        /// Key is discipline code (e.g., "M", "E", "P"). Provides token defaults and validation constraints.
        /// </summary>
        public static Dictionary<string, DisciplineProfile> DisciplineProfiles { get; internal set; }
            = new Dictionary<string, DisciplineProfile>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the discipline profile for the given discipline code, or null if none is defined.
        /// </summary>
        public static DisciplineProfile GetDisciplineProfile(string disc)
        {
            if (string.IsNullOrEmpty(disc)) return null;
            return DisciplineProfiles.TryGetValue(disc, out var profile) ? profile : null;
        }

        /// <summary>
        /// Validates token values against discipline profile constraints.
        /// Returns a list of validation error messages (empty if all valid).
        /// </summary>
        public static List<string> ValidateAgainstProfile(string disc, string sys, string func, string prod)
        {
            var errors = new List<string>();
            var profile = GetDisciplineProfile(disc);
            if (profile == null) return errors;

            // F-17: Use HashSet.Contains for O(1) lookup instead of List.Any(StringEquals) O(n)
            if (profile.AllowedSysCodes != null && profile.AllowedSysCodes.Count > 0
                && !string.IsNullOrEmpty(sys)
                && !profile.AllowedSysCodes.Contains(sys))
            {
                errors.Add($"SYS '{sys}' not in allowed codes for DISC '{disc}': {string.Join(", ", profile.AllowedSysCodes)}");
            }

            if (profile.AllowedFuncCodes != null && profile.AllowedFuncCodes.Count > 0
                && !string.IsNullOrEmpty(func)
                && !profile.AllowedFuncCodes.Contains(func))
            {
                errors.Add($"FUNC '{func}' not in allowed codes for DISC '{disc}': {string.Join(", ", profile.AllowedFuncCodes)}");
            }

            if (profile.ValidationStrictness)
            {
                if (profile.RequiredTokens != null)
                {
                    // F-17: HashSet.Contains is O(1) vs List.Any(StringEquals) O(n)
                    if (profile.RequiredTokens.Contains("SYS") && string.IsNullOrEmpty(sys))
                        errors.Add($"SYS is required for DISC '{disc}'");
                    if (profile.RequiredTokens.Contains("FUNC") && string.IsNullOrEmpty(func))
                        errors.Add($"FUNC is required for DISC '{disc}'");
                    if (profile.RequiredTokens.Contains("PROD") && string.IsNullOrEmpty(prod))
                        errors.Add($"PROD is required for DISC '{disc}'");
                }
            }

            return errors;
        }

        /// <summary>
        /// Historical separators that have been used in this project.
        /// TagIsComplete will try these if the current separator doesn't produce
        /// the expected number of tokens, allowing tags created with old separators
        /// to still be recognised as complete.
        /// </summary>
        public static List<string> SeparatorHistory { get; internal set; } = new List<string>();

        /// <summary>G1.1: Categories to skip entirely during batch tag operations. Loaded from project_config.json CATEGORY_SKIP array.</summary>
        public static HashSet<string> CategorySkipList { get; internal set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>G1.1: Per-category SYS code overrides. Loaded from project_config.json CATEGORY_FORCE_SYS dict.</summary>
        public static Dictionary<string, string> CategoryForceSys { get; internal set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>AL-05: Minimum compliance % gate after batch tag operations. 0 = disabled. Loaded from project_config.json COMPLIANCE_GATE_PCT.</summary>
        public static int ComplianceGatePct { get; internal set; } = 0;

        /// <summary>HC-001: Configurable proximity radius in feet for CopyTokensFromNearest. Default 10 ft.</summary>
        public static double ProximityRadiusFt { get; internal set; } = 10.0;

        /// <summary>
        /// Default collision mode for bulk commands that don't show an explicit user dialog
        /// (TagAndCombine, StingAutoTagger). Loaded from project_config.json
        /// <c>DEFAULT_COLLISION_MODE</c>: "skip" | "overwrite" | "increment" (default).
        /// AutoTagCommand always shows its own dialog and ignores this setting.
        /// </summary>
        public static TagCollisionMode DefaultCollisionMode { get; internal set; } = TagCollisionMode.AutoIncrement;

        /// <summary>HC-003: Configurable batch size for ResolveAllIssues. Default 500.</summary>
        public static int ResolveBatchSize { get; internal set; } = 500;

        /// <summary>TAG-STALE-WARN-01: Minimum stale-element count before the auto-warning
        /// promotion job opens a BIM issue. 0 disables the auto-promotion. Default 5.</summary>
        public static int StaleWarningThreshold { get; internal set; } = 5;

        /// <summary>BIM-CDE-FOLDER-01: When true, the plugin runs
        /// `ProjectFolderEngine.CreateFolderStructure(doc)` on every
        /// DocumentOpened event so the WIP / SHARED / PUBLISHED / ARCHIVE
        /// CDE folders exist before any export tries to write into them.
        /// Idempotent — folders that already exist are skipped. Default true.</summary>
        public static bool AutoCreateCdeFolders { get; internal set; } = true;

        /// <summary>Phase 165 (NEW-02): When true, ClashScheduler starts on
        /// DocumentOpened with the cadence from default_clash_matrix.json
        /// (SchedulerIntervalMinutes; default 60). Off keeps the scheduler
        /// dormant until the user starts it from the Clash tab. Default false
        /// because the per-tick run on a large model is non-trivial.</summary>
        public static bool AutoStartClashScheduler { get; internal set; } = false;

        /// <summary>Configurable batch size for streaming COBie export. Default 5000.</summary>
        public static int CobieStreamBatchSize { get; internal set; } = 5000;

        /// <summary>BIM-EXCEL-STREAM-01: Configurable batch size for streaming Excel import. Default 2000.</summary>
        public static int ExcelImportBatchSize { get; internal set; } = 2000;

        /// <summary>Phase 40: Configurable cost rates CSV filename (via COST_RATES_FILE config key).
        /// Defaults to "cost_rates_5d.csv". Allows per-phase or per-region cost files.</summary>
        public static string CostRatesFileName { get; internal set; } = "cost_rates_5d.csv";

        // Phase 77: Custom title block family for sheet operations
        public static string PreferredTitleBlockFamily { get; set; }

        // Phase 77: Configurable sheet margins (mm)
        public static double SheetMarginLeftMm { get; set; } = 15.0;
        public static double SheetMarginRightMm { get; set; } = 55.0;
        public static double SheetMarginTopMm { get; set; } = 10.0;
        public static double SheetMarginBottomMm { get; set; } = 15.0;
        public static double SheetMarginGapMm { get; set; } = 8.0;

        /// <summary>FUT-01: SEQ namespace range allocation per linked model.
        /// Loaded from SEQ_RANGE_ALLOCATION in project_config.json.
        /// Format: {"ARCH": [1, 4999], "MEP": [5000, 8999], "STR": [9000, 9999]}.</summary>
        public static Dictionary<string, (int Min, int Max)> SeqRangeAllocation { get; internal set; }
            = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

        // ── GAP-FIX: Configurable formula cache TTL ──

        /// <summary>Formula cache TTL in minutes. Loaded from FORMULA_CACHE_TTL_MINUTES in project_config.json.
        /// Default 5 minutes. Auto-scales: models with 50K+ elements get 10 min, 100K+ get 15 min.</summary>
        public static int FormulaCacheTTLMinutes { get; internal set; } = 5;

        /// <summary>Grid line cache TTL in minutes. Loaded from GRID_CACHE_TTL_MINUTES in project_config.json. Default 2.</summary>
        public static int GridCacheTTLMinutes { get; internal set; } = 2;

        // ── GAP-FIX: Configurable SLA thresholds ──

        /// <summary>Configurable SLA thresholds in hours per priority level.
        /// Loaded from SLA_THRESHOLDS in project_config.json.
        /// Format: { "CRITICAL": 4, "HIGH": 24, "MEDIUM": 168, "LOW": 336 }.</summary>
        public static Dictionary<string, double> SLAThresholdsHours { get; internal set; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["CRITICAL"] = 4, ["HIGH"] = 24, ["MEDIUM"] = 168, ["LOW"] = 336, ["INFO"] = 0
            };

        /// <summary>Whether to auto-save warning baseline on document close. Default true.</summary>
        public static bool AutoSaveWarningBaseline { get; internal set; } = true;

        /// <summary>Whether to auto-save warning baseline on revision creation. Default true.</summary>
        public static bool AutoSaveBaselineOnRevision { get; internal set; } = true;

        /// <summary>FUT-01: Get the SEQ range for the current model's discipline.
        /// Returns (minSeq, maxSeq) or (1, 9999) if no allocation defined.</summary>
        public static (int Min, int Max) GetSeqRange(string modelDiscipline)
        {
            if (string.IsNullOrEmpty(modelDiscipline) || SeqRangeAllocation.Count == 0)
                return (1, 9999);
            if (SeqRangeAllocation.TryGetValue(modelDiscipline, out var range))
                return range;
            return (1, 9999);
        }

        /// <summary>FUT-01: Validate a SEQ number is within the allocated range for the model discipline.
        /// Returns null if valid, error message if out of range.</summary>
        public static string ValidateSeqRange(int seqNumber, string modelDiscipline)
        {
            if (SeqRangeAllocation.Count == 0) return null; // No allocation defined
            var (min, max) = GetSeqRange(modelDiscipline);
            if (seqNumber < min || seqNumber > max)
                return $"SEQ {seqNumber:D4} is outside allocated range {min:D4}-{max:D4} for model '{modelDiscipline}'. " +
                       $"Configure SEQ_RANGE_ALLOCATION in project_config.json.";
            return null;
        }

        /// <summary>R4-B: Generic double config getter — reads from cached config, not disk.
        /// Falls back to LoadFromFile-parsed values where possible.</summary>
        private static Newtonsoft.Json.Linq.JObject _cachedConfigObj;
        private static string _cachedConfigPath;
        private static DateTime _cachedConfigModified;

        internal static double GetConfigDouble(string key, double defaultValue)
        {
            try
            {
                string path = ConfigSource;
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return defaultValue;

                // Use cached JObject if file hasn't changed
                var lastWrite = System.IO.File.GetLastWriteTimeUtc(path);
                if (_cachedConfigObj == null || _cachedConfigPath != path || _cachedConfigModified != lastWrite)
                {
                    string json = System.IO.File.ReadAllText(path);
                    _cachedConfigObj = Newtonsoft.Json.Linq.JObject.Parse(json);
                    _cachedConfigPath = path;
                    _cachedConfigModified = lastWrite;
                }

                var token = _cachedConfigObj[key];
                if (token == null) return defaultValue;
                if (token.Type == Newtonsoft.Json.Linq.JTokenType.Float) return (double)token;
                if (token.Type == Newtonsoft.Json.Linq.JTokenType.Integer) return (long)token;
                if (double.TryParse(token.ToString(), out double val)) return val;
            }
            catch (Exception ex) { StingLog.Warn($"GetConfigDouble({key}): {ex.Message}"); }
            return defaultValue;
        }

        /// <summary>AL-07: Workflow preset name to auto-run on DocumentOpened. Empty = disabled.</summary>
        public static string AutoRunWorkflowOnOpen { get; internal set; } = string.Empty;

        /// <summary>FIX-B10: Persisted auto-tagger enabled state. Null = not set in config (use default).</summary>
        public static bool? AutoTaggerEnabled { get; internal set; }
        /// <summary>FIX-B10: Persisted auto-tagger visual state. Null = not set in config.</summary>
        public static bool? AutoTaggerVisual { get; internal set; }
        /// <summary>FIX-B10: Persisted stale marker state. Null = not set in config.</summary>
        public static bool? AutoTaggerStaleMarker { get; internal set; }

        /// <summary>
        /// GAP-STATUS-01: When true, STATUS token is always re-derived from Revit phase data and
        /// overwritten even if the element already has a STATUS value. Prevents drift between the
        /// Revit phase model and the ISO 19650 tag when phases are reorganised post-tagging.
        /// Set via AUTO_CORRECT_STATUS_FROM_PHASE in project_config.json.
        /// </summary>
        public static bool AutoCorrectStatusFromPhase { get; internal set; } = false;

        /// <summary>FE-06: Full per-category token overrides. Key=category name, Value=dict of token->value.</summary>
        public static Dictionary<string, Dictionary<string, string>> CategoryTokenOverrides { get; internal set; }
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);


        /// <summary>Current sequence numbering scheme (loaded from project_config.json).</summary>
        internal static SeqScheme CurrentSeqScheme { get; set; } = SeqScheme.Numeric;
        /// <summary>Whether SEQ resets per zone.</summary>
        internal static bool SeqIncludeZone { get; set; } = false;
        /// <summary>Whether SEQ resets per level within DISC-SYS group.</summary>
        internal static bool SeqLevelReset { get; set; } = false;

        // A1: Track SEQ scheme changes to warn users about potential counter misalignment
        private static bool _seqSchemeChanged = false;
        private static bool _seqSchemeWarned = false;

        // Phase 177 — lazy-load guard for valid FUNC CSV data.
        // EnsureValidFuncsLoaded() is a no-op because _validFuncsForSys is
        // initialised from a static field initialiser; the flag is retained
        // so callers (ParameterHelpers) can query IsLoaded without coupling
        // to internal implementation details.
        private static bool _validFuncsCsvLoaded = false;

        /// <summary>True once the valid-FUNC lookup table has been populated (always true after first static init).</summary>
        public static bool IsLoaded => _validFuncsCsvLoaded || ISO19650Validator.ValidFuncsForSysCount > 0;

        /// <summary>
        /// Ensures the valid-FUNC per-SYS lookup table is populated.
        /// The table is built from a static field initialiser so this is
        /// effectively a no-op; it exists so callers can guarantee readiness
        /// without depending on internal implementation details.
        /// </summary>
        public static void EnsureValidFuncsLoaded()
        {
            if (_validFuncsCsvLoaded) return;
            _validFuncsCsvLoaded = true;
            // _validFuncsForSys is populated by its field initialiser above;
            // nothing more to load here.
        }

        /// <summary>
        /// Build a canonical SEQ counter key from element token values.
        /// Used to ensure consistent grouping across all tagging commands.
        /// Format matches BuildAndWriteTag: DISC_SYS_LVL (or DISC_ZONE_SYS_LVL when SeqIncludeZone).
        /// </summary>
        public static string BuildSeqKey(Element el)
        {
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
            string sys  = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
            string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
            string lvl  = ParameterHelpers.GetString(el, ParamRegistry.LVL);

            // Normalise empty tokens to avoid key drift
            if (string.IsNullOrEmpty(disc)) disc = "A";
            if (string.IsNullOrEmpty(sys))  sys  = "GEN";
            if (string.IsNullOrEmpty(func)) func = "GEN";
            if (string.IsNullOrEmpty(prod)) prod = "GEN";
            if (string.IsNullOrEmpty(lvl) || lvl == "XX") lvl = "L00";

            if (SeqIncludeZone)
            {
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                if (string.IsNullOrEmpty(zone) || zone == "XX" || zone == "ZZ") zone = "Z01";
                return $"{disc}_{zone}_{sys}_{lvl}";
            }

            return $"{disc}_{sys}_{lvl}";
        }

        /// <summary>
        /// Build a canonical SEQ key from explicit token values.
        /// Matches the same format as BuildSeqKey(Element) for consistency.
        /// </summary>
        public static string BuildSeqKey(string disc, string sys, string func, string prod, string lvl, string zone = null)
            => SeqAssigner.BuildSeqKey(disc, sys, lvl, zone, SeqIncludeZone);

        /// <summary>
        /// Build a SEQ string for sequence number n using the configured scheme.
        /// Delegates to <see cref="SeqAssigner.BuildSeqString"/>, supplying the
        /// project-configured pad width.
        /// </summary>
        public static string BuildSeqString(int n, SeqScheme scheme, string zoneOrDisc = "")
            => SeqAssigner.BuildSeqString(n, scheme, SeqPadWidth > 0 ? SeqPadWidth : ParamRegistry.NumPad, zoneOrDisc);

        /// <summary>Convert alphabetic SEQ string back to integer (A=1, B=2... Z=26, AA=27...).</summary>
        private static int FromAlpha(string alpha)
        {
            if (string.IsNullOrEmpty(alpha)) return 0;
            alpha = alpha.ToUpperInvariant();
            int result = 0;
            foreach (char c in alpha)
            {
                if (c < 'A' || c > 'Z') return 0;
                result = result * 26 + (c - 'A' + 1);
            }
            return result;
        }

        // ── Segment mask + display mode helpers ──────────────────────────

        /// <summary>
        /// Apply a segment mask to a full tag. Mask is 8-char string of 1/0 (e.g. "10000001"
        /// shows DISC + SEQ only). Returns the masked tag with suppressed segments removed.
        /// </summary>
        public static string ApplySegmentMask(string fullTag, string mask)
        {
            if (string.IsNullOrEmpty(fullTag) || string.IsNullOrEmpty(mask) || mask.Length < 8)
                return fullTag;

            string sepStr = !string.IsNullOrEmpty(ParamRegistry.Separator) ? ParamRegistry.Separator : "-";
            string[] parts = fullTag.Split(new[] { sepStr }, StringSplitOptions.None);
            if (parts.Length < 8) return fullTag;

            var visible = new List<string>();
            for (int i = 0; i < 8 && i < parts.Length; i++)
            {
                if (i < mask.Length && mask[i] == '1')
                    visible.Add(parts[i]);
            }
            return visible.Count > 0 ? string.Join(ParamRegistry.Separator, visible) : fullTag;
        }

        /// <summary>Category name → discipline code (M, E, P, A, S, FP, LV, G).</summary>
        public static Dictionary<string, string> DiscMap { get; private set; }

        /// <summary>System code → list of category names.</summary>
        public static Dictionary<string, List<string>> SysMap { get; private set; }

        /// <summary>Category name → product code (GRL, AHU, DR, WIN, etc.).</summary>
        public static Dictionary<string, string> ProdMap { get; private set; }

        /// <summary>System code → function code (SUP, HTG, DCW, SAN, RWD, etc.).</summary>
        public static Dictionary<string, string> FuncMap { get; private set; }

        /// <summary>Available location codes.</summary>
        public static List<string> LocCodes { get; internal set; }

        /// <summary>Available zone codes.</summary>
        public static List<string> ZoneCodes { get; internal set; }

        /// <summary>Phase 19: LOC pattern configuration — maps LOC codes to room name/number patterns for auto-detection.</summary>
        public static Dictionary<string, List<string>> LocPatterns { get; internal set; } = new();

        /// <summary>Phase 19: ZONE pattern configuration — maps ZONE codes to department/room name patterns for auto-detection.</summary>
        public static Dictionary<string, List<string>> ZonePatterns { get; internal set; } = new();

        public static string ConfigSource { get; private set; }

        // ── Scope auto-detection and session memory ──

        /// <summary>Last scope used by tagging commands. Persists across commands in same session.
        /// Values: "selection", "active_view", "project". Auto-detected from selection state.</summary>
        public static string LastScope { get; set; }

        /// <summary>Auto-detect scope from current state: if selection exists use it,
        /// otherwise default to active view. Returns "selection", "active_view", or "project".</summary>
        public static string AutoDetectScope(Autodesk.Revit.UI.UIDocument uidoc)
        {
            if (uidoc == null) return LastScope ?? "active_view";
            try
            {
                var sel = uidoc.Selection.GetElementIds();
                if (sel != null && sel.Count > 0)
                {
                    LastScope = "selection";
                    return "selection";
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoDetectScope: {ex.Message}"); }

            // Default to last used scope, or active view
            return LastScope ?? "active_view";
        }

        /// <summary>Get scope label for display in reports.</summary>
        public static string GetScopeLabel(string scope, Autodesk.Revit.UI.UIDocument uidoc)
        {
            return scope switch
            {
                "selection" => $"selected elements ({uidoc?.Selection?.GetElementIds()?.Count ?? 0})",
                "active_view" => $"active view '{uidoc?.ActiveView?.Name ?? "unknown"}'",
                "project" => "entire project",
                _ => scope ?? "unknown"
            };
        }

        /// <summary>
        /// When false (default), LOC/ZONE validation uses format checks (alphanumeric, 1-8 chars)
        /// instead of strict code-list validation. Set to true in project_config.json via
        /// VALIDATE_STRICT_MODE to enforce project-specific LOC/ZONE code lists.
        /// </summary>
        public static bool ValidateStrictMode { get; set; } = false;

        /// <summary>GAP-019: Default STATUS value from project_config.json (null = use "NEW").</summary>
        public static string StatusDefault { get; internal set; }

        /// <summary>GAP-019: Default REV value from project_config.json (null = use "P01").</summary>
        public static string RevDefault { get; internal set; }

        /// <summary>Reverse lookup: category name → SYS code. Built lazily from SysMap.</summary>
        private static volatile Dictionary<string, List<string>> _reverseSysMap;

        static TagConfig()
        {
            LoadDefaults();
        }

        /// <summary>Build or return the cached reverse SysMap (category → list of valid SYS codes).</summary>
        private static Dictionary<string, List<string>> GetReverseSysMap()
        {
            var map = _reverseSysMap;
            if (map == null)
            {
                map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var kvp in SysMap)
                {
                    foreach (string cat in kvp.Value)
                    {
                        if (!map.TryGetValue(cat, out var list))
                        {
                            list = new List<string>();
                            map[cat] = list;
                        }
                        if (!list.Contains(kvp.Key))
                            list.Add(kvp.Key);
                    }
                }
                _reverseSysMap = map;
            }
            return map;
        }

        /// <summary>Load from a JSON config file, falling back to defaults.</summary>
        public static void LoadFromFile(string path)
        {
            if (!File.Exists(path))
            {
                LoadDefaults();
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (data == null)
                {
                    LoadDefaults();
                    return;
                }

                // Validate config keys — warn on unknown keys to catch typos
                var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "DISC_MAP","SYS_MAP","PROD_MAP","FUNC_MAP","LOC_CODES","ZONE_CODES","TAG_FORMAT",
                    "TAG_PREFIX","TAG_SUFFIX","CATEGORY_SKIP","CATEGORY_FORCE_SYS","SEQ_SCHEME",
                    "SEQ_INCLUDE_ZONE","SEQ_LEVEL_RESET","STATUS_DEFAULT","REV_DEFAULT",
                    "VALIDATE_STRICT_MODE","LOC_PATTERNS","ZONE_PATTERNS","COMPLIANCE_GATE_PCT",
                    "SEPARATOR_HISTORY","AUTO_RUN_WORKFLOW_ON_OPEN","ACTIVE_PRESET",
                    "CATEGORY_TOKEN_OVERRIDES","tag3DFamilyPath",
                    "AUTO_TAGGER_ENABLED","AUTO_TAGGER_VISUAL","AUTO_TAGGER_STALE_MARKER",
                    "CUSTOM_VALID_DISC","CUSTOM_VALID_SYS","CUSTOM_VALID_FUNC",
                    "CUSTOM_VALID_LOC","CUSTOM_VALID_ZONE",
                    "PROXIMITY_RADIUS_FT","RESOLVE_BATCH_SIZE","STALE_WARNING_THRESHOLD",
                    "AUTO_CREATE_CDE_FOLDERS",
                    "COBIE_STREAM_BATCH_SIZE","PERF_TRACKING_ENABLED",
                    "COST_RATES_FILE","SHEET_NAMING_STRICT_MODE",
                    "COST_PRELIMINARIES_PCT","COST_CONTINGENCY_PCT","COST_OVERHEAD_PROFIT_PCT",
                    "TRADE_DURATION_OVERRIDES","SEQ_RANGE_ALLOCATION",
                    "CDE_SHARED_MIN_COMPLIANCE","CDE_PUBLISHED_MIN_COMPLIANCE",
                    "DD_SCHEDULE","DD_REQUIREMENTS",
                    "TITLE_BLOCK_FAMILY","SHEET_MARGINS",
                    "DISCIPLINE_PROFILES","FORMULA_CACHE_TTL_MINUTES","GRID_CACHE_TTL_MINUTES",
                    "SLA_THRESHOLDS","AUTO_SAVE_WARNING_BASELINE","AUTO_SAVE_BASELINE_ON_REVISION",
                    "DISCIPLINE_LEADS","WARNING_SUPPRESS_PATTERNS","AUTO_TAGGER_DISC_FILTER",
                    "USER_ROLE","PROJECT_TYPE","LAST_WORKFLOW_NAME",
                    "EXCEL_IMPORT_BATCH_SIZE",
                    "AUTO_CORRECT_STATUS_FROM_PHASE","LEADER_CLEARANCE_MARGIN_FT"
                };
                var unknownKeys = data.Keys.Where(k => !knownKeys.Contains(k)).ToList();
                if (unknownKeys.Count > 0)
                {
                    string unknownList = string.Join(", ", unknownKeys.Take(5));
                    StingLog.Warn($"TagConfig: unknown config key(s) in project_config.json: {unknownList}" +
                        (unknownKeys.Count > 5 ? $" (+{unknownKeys.Count - 5} more)" : "") +
                        " — check for typos");
                }

                DiscMap = TryDeserialize<Dictionary<string, string>>(data, "DISC_MAP") ?? DefaultDiscMap();
                SysMap = TryDeserialize<Dictionary<string, List<string>>>(data, "SYS_MAP") ?? DefaultSysMap();
                ProdMap = TryDeserialize<Dictionary<string, string>>(data, "PROD_MAP") ?? DefaultProdMap();
                FuncMap = TryDeserialize<Dictionary<string, string>>(data, "FUNC_MAP") ?? DefaultFuncMap();
                LocCodes = TryDeserialize<List<string>>(data, "LOC_CODES") ?? DefaultLocCodes();
                ZoneCodes = TryDeserialize<List<string>>(data, "ZONE_CODES") ?? DefaultZoneCodes();
                _reverseSysMap = null; // Invalidate cache

                // Load tag format overrides (optional — fall back to ParamRegistry defaults)
                ParamRegistry.ClearTagFormatOverrides();
                if (data.TryGetValue("TAG_FORMAT", out object fmtObj))
                {
                    try
                    {
                        var fmt = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                            JsonConvert.SerializeObject(fmtObj));
                        if (fmt != null)
                        {
                            string sep = null;
                            int? pad = null;
                            string[] segs = null;

                            if (fmt.TryGetValue("separator", out object sepVal) && sepVal is string s)
                                sep = s;
                            if (fmt.TryGetValue("num_pad", out object padVal))
                            {
                                if (padVal is long lv) pad = (int)lv;
                                else if (int.TryParse(padVal?.ToString(), out int iv)) pad = iv;
                            }
                            if (fmt.TryGetValue("segment_order", out object segVal))
                            {
                                var parsed = JsonConvert.DeserializeObject<string[]>(
                                    JsonConvert.SerializeObject(segVal));
                                if (parsed != null && parsed.Length > 0)
                                    segs = parsed;
                            }

                            ParamRegistry.ApplyTagFormatOverrides(sep, pad, segs);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Failed to parse TAG_FORMAT from config: {ex.Message}");
                    }
                }

                // Load STATUS/REV defaults from config (optional)
                StatusDefault = null;
                RevDefault = null;
                if (data.TryGetValue("STATUS_DEFAULT", out object statusObj) && statusObj is string statusStr
                    && !string.IsNullOrWhiteSpace(statusStr))
                    StatusDefault = statusStr;
                if (data.TryGetValue("REV_DEFAULT", out object revObj) && revObj is string revStr
                    && !string.IsNullOrWhiteSpace(revStr))
                    RevDefault = revStr;

                // A4: Load strict validation mode (optional — defaults to false/lenient)
                ValidateStrictMode = false;
                if (data.TryGetValue("VALIDATE_STRICT_MODE", out object strictObj))
                {
                    if (strictObj is bool sb) ValidateStrictMode = sb;
                    else if (strictObj is string ss) ValidateStrictMode =
                        ss.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                // Phase 19: Load LOC/ZONE pattern overrides from config (optional — fall back to LoadDefaults)
                var locPat = TryDeserialize<Dictionary<string, List<string>>>(data, "LOC_PATTERNS");
                if (locPat != null && locPat.Count > 0)
                    LocPatterns = new Dictionary<string, List<string>>(locPat, StringComparer.OrdinalIgnoreCase);
                var zonePat = TryDeserialize<Dictionary<string, List<string>>>(data, "ZONE_PATTERNS");
                if (zonePat != null && zonePat.Count > 0)
                    ZonePatterns = new Dictionary<string, List<string>>(zonePat, StringComparer.OrdinalIgnoreCase);

                // A1: Track previous SEQ scheme settings for change warning
                SeqScheme prevScheme = CurrentSeqScheme;
                bool prevIncludeZone = SeqIncludeZone;

                // Load SEQ scheme settings from config (optional)
                if (data.TryGetValue("SEQ_SCHEME", out object seqSchemeObj) && seqSchemeObj is string seqSchemeStr)
                {
                    if (Enum.TryParse<SeqScheme>(seqSchemeStr, true, out var parsed))
                        CurrentSeqScheme = parsed;
                }
                if (data.TryGetValue("SEQ_INCLUDE_ZONE", out object seqZoneObj))
                {
                    if (seqZoneObj is bool szb) SeqIncludeZone = szb;
                    else if (seqZoneObj is string szs) SeqIncludeZone =
                        szs.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                // A1: Detect SEQ scheme changes for warning in BuildAndWriteTag
                if (CurrentSeqScheme != prevScheme || SeqIncludeZone != prevIncludeZone)
                {
                    _seqSchemeChanged = true;
                    _seqSchemeWarned = false;
                }

                // TW-03c / G1.1: Load optional global tag prefix and suffix
                TagPrefix = string.Empty;
                TagSuffix = string.Empty;
                if (data.TryGetValue("TAG_PREFIX", out object pfxObj) && pfxObj is string pfxStr
                    && !string.IsNullOrWhiteSpace(pfxStr))
                    TagPrefix = pfxStr.Trim();
                if (data.TryGetValue("TAG_SUFFIX", out object sfxObj) && sfxObj is string sfxStr
                    && !string.IsNullOrWhiteSpace(sfxStr))
                    TagSuffix = sfxStr.Trim();

                // G1.1: Load category-level skip list
                CategorySkipList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var skipList = TryDeserialize<List<string>>(data, "CATEGORY_SKIP");
                if (skipList != null)
                    foreach (var cat in skipList) CategorySkipList.Add(cat);

                // G1.1: Load category-level SYS force overrides
                CategoryForceSys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var forceSys = TryDeserialize<Dictionary<string, string>>(data, "CATEGORY_FORCE_SYS");
                if (forceSys != null)
                    foreach (var kvp in forceSys) CategoryForceSys[kvp.Key] = kvp.Value;

                // Load custom token validators from config
                ISO19650Validator.CustomDiscCodes = LoadCustomCodes(data, "CUSTOM_VALID_DISC");
                ISO19650Validator.CustomSysCodes = LoadCustomCodes(data, "CUSTOM_VALID_SYS");
                ISO19650Validator.CustomFuncCodes = LoadCustomCodes(data, "CUSTOM_VALID_FUNC");
                ISO19650Validator.CustomLocCodes = LoadCustomCodes(data, "CUSTOM_VALID_LOC");
                ISO19650Validator.CustomZoneCodes = LoadCustomCodes(data, "CUSTOM_VALID_ZONE");
                int customCount = ISO19650Validator.CustomDiscCodes.Count + ISO19650Validator.CustomSysCodes.Count
                    + ISO19650Validator.CustomFuncCodes.Count + ISO19650Validator.CustomLocCodes.Count
                    + ISO19650Validator.CustomZoneCodes.Count;
                if (customCount > 0)
                    StingLog.Info($"TagConfig: loaded {customCount} custom validator codes from project_config.json");

                // Per-discipline tagging profiles
                DisciplineProfiles = new Dictionary<string, DisciplineProfile>(StringComparer.OrdinalIgnoreCase);
                var profilesDict = TryDeserialize<Dictionary<string, DisciplineProfile>>(data, "DISCIPLINE_PROFILES");
                if (profilesDict != null)
                {
                    foreach (var kvp in profilesDict)
                    {
                        var p = kvp.Value;
                        if (p != null)
                        {
                            if (string.IsNullOrEmpty(p.DefaultDisc))
                                p.DefaultDisc = kvp.Key; // Use the dictionary key as DefaultDisc if not explicitly set
                            DisciplineProfiles[kvp.Key] = p;
                        }
                    }
                    if (DisciplineProfiles.Count > 0)
                        StingLog.Info($"TagConfig: loaded {DisciplineProfiles.Count} discipline profile(s): {string.Join(", ", DisciplineProfiles.Keys)}");
                }

                // Configurable proximity radius for CopyTokensFromNearest.
                // Revit internal coordinates are always in feet, so ProximityRadiusFt
                // is the canonical internal unit.  Three config keys are accepted so
                // metric-project teams can author the value in their natural units:
                //   PROXIMITY_RADIUS_FT  — value is already in feet (legacy/default)
                //   PROXIMITY_RADIUS_M   — value in metres  → converted to feet
                //   PROXIMITY_RADIUS_MM  — value in millimetres → converted to feet
                // All keys share the same 1–200 ft clamp after conversion.
                ProximityRadiusFt = 10.0; // default 10 ft ≈ 3 m
                double rawRadius = double.NaN;
                double unitToFt = 1.0; // default: value already in feet
                if (data.TryGetValue("PROXIMITY_RADIUS_FT", out object proxFt))
                {
                    if (proxFt is double pd) rawRadius = pd;
                    else if (proxFt is long pl) rawRadius = pl;
                    else double.TryParse(proxFt?.ToString(), out rawRadius);
                    unitToFt = 1.0;
                }
                else if (data.TryGetValue("PROXIMITY_RADIUS_M", out object proxM))
                {
                    if (proxM is double pd) rawRadius = pd;
                    else if (proxM is long pl) rawRadius = pl;
                    else double.TryParse(proxM?.ToString(), out rawRadius);
                    unitToFt = 3.28084; // 1 m = 3.28084 ft
                }
                else if (data.TryGetValue("PROXIMITY_RADIUS_MM", out object proxMm))
                {
                    if (proxMm is double pd) rawRadius = pd;
                    else if (proxMm is long pl) rawRadius = pl;
                    else double.TryParse(proxMm?.ToString(), out rawRadius);
                    unitToFt = 0.00328084; // 1 mm = 0.00328084 ft
                }
                if (!double.IsNaN(rawRadius))
                {
                    ProximityRadiusFt = rawRadius * unitToFt;
                    if (ProximityRadiusFt < 1.0) ProximityRadiusFt = 1.0;
                    if (ProximityRadiusFt > 200.0) ProximityRadiusFt = 200.0;
                    StingLog.Info($"TagConfig: ProximityRadiusFt = {ProximityRadiusFt:F2} ft (raw={rawRadius}, unitToFt={unitToFt})");
                }

                // Configurable batch size for ResolveAllIssues
                ResolveBatchSize = 500; // default
                if (data.TryGetValue("RESOLVE_BATCH_SIZE", out object bsObj))
                {
                    if (bsObj is long bl) ResolveBatchSize = (int)bl;
                    else if (int.TryParse(bsObj?.ToString(), out int bi)) ResolveBatchSize = bi;
                    if (ResolveBatchSize < 50) ResolveBatchSize = 50;
                    if (ResolveBatchSize > 5000) ResolveBatchSize = 5000;
                }

                // DEFAULT_COLLISION_MODE: controls TagAndCombineCommand and StingAutoTagger bulk path.
                // AutoTagCommand always shows its own dialog and ignores this setting.
                DefaultCollisionMode = TagCollisionMode.AutoIncrement;
                if (data.TryGetValue("DEFAULT_COLLISION_MODE", out object dcmObj))
                {
                    string dcmStr = dcmObj?.ToString()?.ToLowerInvariant() ?? "";
                    DefaultCollisionMode = dcmStr switch
                    {
                        "skip"      => TagCollisionMode.Skip,
                        "overwrite" => TagCollisionMode.Overwrite,
                        _           => TagCollisionMode.AutoIncrement,
                    };
                    StingLog.Info($"TagConfig: DefaultCollisionMode = {DefaultCollisionMode}");
                }

                // Configurable threshold for auto-creating stale-element issues.
                StaleWarningThreshold = 5; // default
                if (data.TryGetValue("STALE_WARNING_THRESHOLD", out object swtObj))
                {
                    if (swtObj is long sl) StaleWarningThreshold = (int)sl;
                    else if (int.TryParse(swtObj?.ToString(), out int si)) StaleWarningThreshold = si;
                    if (StaleWarningThreshold < 0) StaleWarningThreshold = 0;
                    if (StaleWarningThreshold > 100000) StaleWarningThreshold = 100000;
                }

                // Auto-bootstrap CDE folder structure on doc open.
                AutoCreateCdeFolders = true;
                if (data.TryGetValue("AUTO_CREATE_CDE_FOLDERS", out object accfObj))
                {
                    if (accfObj is bool b) AutoCreateCdeFolders = b;
                    else if (bool.TryParse(accfObj?.ToString(), out bool bp)) AutoCreateCdeFolders = bp;
                }

                // Streaming COBie batch size
                CobieStreamBatchSize = 5000; // default
                if (data.TryGetValue("COBIE_STREAM_BATCH_SIZE", out object csObj))
                {
                    if (csObj is long cl) CobieStreamBatchSize = (int)cl;
                    else if (int.TryParse(csObj?.ToString(), out int ci)) CobieStreamBatchSize = ci;
                    if (CobieStreamBatchSize < 500) CobieStreamBatchSize = 500;
                    if (CobieStreamBatchSize > 50000) CobieStreamBatchSize = 50000;
                }

                // Streaming Excel import batch size
                ExcelImportBatchSize = 2000; // default
                if (data.TryGetValue("EXCEL_IMPORT_BATCH_SIZE", out object eiObj))
                {
                    if (eiObj is long eil) ExcelImportBatchSize = (int)eil;
                    else if (int.TryParse(eiObj?.ToString(), out int eii)) ExcelImportBatchSize = eii;
                    if (ExcelImportBatchSize < 100) ExcelImportBatchSize = 100;
                    if (ExcelImportBatchSize > 50000) ExcelImportBatchSize = 50000;
                }

                // Compliance gate threshold
                ComplianceGatePct = 0;
                if (data.TryGetValue("COMPLIANCE_GATE_PCT", out object gateObj))
                {
                    if (gateObj is long gl) ComplianceGatePct = (int)gl;
                    else if (int.TryParse(gateObj?.ToString(), out int gi)) ComplianceGatePct = gi;
                }

                // Phase 40: Configurable cost rates filename
                if (data.TryGetValue("COST_RATES_FILE", out object crfObj) && crfObj != null)
                {
                    string crfVal = crfObj.ToString().Trim();
                    if (!string.IsNullOrEmpty(crfVal)) CostRatesFileName = crfVal;
                }

                // Phase 40: Sheet naming strict mode
                // (read here for reference but validated in SheetNamingCheckCommand directly)

                // PerformanceTracker opt-in via config
                if (data.TryGetValue("PERF_TRACKING_ENABLED", out object perfObj) && perfObj is bool perfEnabled)
                    PerformanceTracker.Enabled = perfEnabled;

                // FIX-10.2: Restore auto-tagger visual setting (use SetVisualTaggingQuiet to avoid re-save loop)
                if (data.TryGetValue("AUTO_TAGGER_VISUAL", out object _avt) && _avt is bool _avtb)
                    try { Core.StingAutoTagger.SetVisualTaggingQuiet(_avtb); } catch (Exception ex) { StingLog.Warn($"Restore auto-tagger visual setting: {ex.Message}"); }

                // Load separator history for cross-session tag validation compatibility
                var sepHistory = TryDeserialize<List<string>>(data, "SEPARATOR_HISTORY");
                if (sepHistory != null && sepHistory.Count > 0)
                    SeparatorHistory = sepHistory;
                else
                    SeparatorHistory = new List<string>();

                // Auto-run workflow on document open
                AutoRunWorkflowOnOpen = string.Empty;
                if (data.TryGetValue("AUTO_RUN_WORKFLOW_ON_OPEN", out object arwObj) && arwObj is string arwStr
                    && !string.IsNullOrWhiteSpace(arwStr))
                    AutoRunWorkflowOnOpen = arwStr.Trim();

                // Load full per-category token overrides
                CategoryTokenOverrides = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                var catOverrides = TryDeserialize<Dictionary<string, Dictionary<string, string>>>(data, "CATEGORY_TOKEN_OVERRIDES");
                if (catOverrides != null)
                    foreach (var kvp in catOverrides) CategoryTokenOverrides[kvp.Key] = kvp.Value;


                // Load auto-tagger state from config
                if (data.TryGetValue("AUTO_TAGGER_ENABLED", out object ateObj))
                {
                    bool ateVal = false;
                    if (ateObj is bool atb) ateVal = atb;
                    else if (ateObj is string ats) ateVal = ats.Equals("true", StringComparison.OrdinalIgnoreCase);
                    AutoTaggerEnabled = ateVal;
                }
                else { AutoTaggerEnabled = null; }
                if (data.TryGetValue("AUTO_TAGGER_VISUAL", out object atvObj))
                {
                    bool atvVal = false;
                    if (atvObj is bool avb) atvVal = avb;
                    else if (atvObj is string avs) atvVal = avs.Equals("true", StringComparison.OrdinalIgnoreCase);
                    AutoTaggerVisual = atvVal;
                }
                else { AutoTaggerVisual = null; }
                if (data.TryGetValue("AUTO_TAGGER_STALE_MARKER", out object atsmObj))
                {
                    bool atsmVal = false;
                    if (atsmObj is bool asmb) atsmVal = asmb;
                    else if (atsmObj is string asms) atsmVal = asms.Equals("true", StringComparison.OrdinalIgnoreCase);
                    AutoTaggerStaleMarker = atsmVal;
                }
                else { AutoTaggerStaleMarker = null; }

                // Auto-correct STATUS from Revit phase data (default off for back-compat)
                AutoCorrectStatusFromPhase = false;
                if (data.TryGetValue("AUTO_CORRECT_STATUS_FROM_PHASE", out object acsObj))
                {
                    if (acsObj is bool acsb) AutoCorrectStatusFromPhase = acsb;
                    else if (acsObj is string acss) AutoCorrectStatusFromPhase =
                        acss.Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (AutoCorrectStatusFromPhase)
                        StingLog.Info("TagConfig: AUTO_CORRECT_STATUS_FROM_PHASE = true — STATUS will always reflect Revit phase");
                }

                // Load configurable formula/grid cache TTL
                FormulaCacheTTLMinutes = 5;
                if (data.TryGetValue("FORMULA_CACHE_TTL_MINUTES", out object fctObj))
                {
                    if (fctObj is long fcl) FormulaCacheTTLMinutes = (int)fcl;
                    else if (int.TryParse(fctObj?.ToString(), out int fci)) FormulaCacheTTLMinutes = fci;
                    FormulaCacheTTLMinutes = Math.Max(1, Math.Min(60, FormulaCacheTTLMinutes));
                }
                GridCacheTTLMinutes = 2;
                if (data.TryGetValue("GRID_CACHE_TTL_MINUTES", out object gctObj))
                {
                    if (gctObj is long gcl) GridCacheTTLMinutes = (int)gcl;
                    else if (int.TryParse(gctObj?.ToString(), out int gci)) GridCacheTTLMinutes = gci;
                    GridCacheTTLMinutes = Math.Max(1, Math.Min(30, GridCacheTTLMinutes));
                }

                // Load configurable SLA thresholds
                SLAThresholdsHours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    { ["CRITICAL"] = 4, ["HIGH"] = 24, ["MEDIUM"] = 168, ["LOW"] = 336, ["INFO"] = 0 };
                if (data.TryGetValue("SLA_THRESHOLDS", out object slaObj) && slaObj != null)
                {
                    try
                    {
                        var slaDict = JsonConvert.DeserializeObject<Dictionary<string, double>>(
                            JsonConvert.SerializeObject(slaObj));
                        if (slaDict != null)
                            foreach (var kvp in slaDict) SLAThresholdsHours[kvp.Key.ToUpper()] = kvp.Value;
                    }
                    catch (Exception ex2) { StingLog.Warn($"TagConfig: failed to parse SLA_THRESHOLDS: {ex2.Message}"); }
                }

                // Auto-save warning baseline settings
                AutoSaveWarningBaseline = true;
                if (data.TryGetValue("AUTO_SAVE_WARNING_BASELINE", out object aswbObj))
                {
                    if (aswbObj is bool aswbb) AutoSaveWarningBaseline = aswbb;
                    else if (aswbObj is string aswbs) AutoSaveWarningBaseline = aswbs.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                AutoSaveBaselineOnRevision = true;
                if (data.TryGetValue("AUTO_SAVE_BASELINE_ON_REVISION", out object asbrObj))
                {
                    if (asbrObj is bool asbrb) AutoSaveBaselineOnRevision = asbrb;
                    else if (asbrObj is string asbrs) AutoSaveBaselineOnRevision = asbrs.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                // Phase 77: Custom title block family
                PreferredTitleBlockFamily = null;
                if (data.TryGetValue("TITLE_BLOCK_FAMILY", out object tbfObj) && tbfObj is string tbfStr
                    && !string.IsNullOrWhiteSpace(tbfStr))
                    PreferredTitleBlockFamily = tbfStr.Trim();

                // Phase 77: Configurable sheet margins
                SheetMarginLeftMm = 15.0;
                SheetMarginRightMm = 55.0;
                SheetMarginTopMm = 10.0;
                SheetMarginBottomMm = 15.0;
                SheetMarginGapMm = 8.0;
                if (data.TryGetValue("SHEET_MARGINS", out object smObj) && smObj != null)
                {
                    try
                    {
                        var smDict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, double>>(
                            Newtonsoft.Json.JsonConvert.SerializeObject(smObj));
                        if (smDict != null)
                        {
                            if (smDict.TryGetValue("Left", out double ml)) SheetMarginLeftMm = ml;
                            if (smDict.TryGetValue("Right", out double mr)) SheetMarginRightMm = mr;
                            if (smDict.TryGetValue("Top", out double mt)) SheetMarginTopMm = mt;
                            if (smDict.TryGetValue("Bottom", out double mb)) SheetMarginBottomMm = mb;
                            if (smDict.TryGetValue("Gap", out double mg)) SheetMarginGapMm = mg;
                        }
                    }
                    catch (Exception ex2) { StingLog.Warn($"TagConfig: failed to parse SHEET_MARGINS: {ex2.Message}"); }
                }

                ConfigSource = path;
                // Reload CSV-derived lookup tables so project-specific additions survive config reload
                // Note: _validFuncsCsvLoaded/EnsureValidFuncsLoaded live in ISO19650Validator; use InvalidateValidatorCaches.
                _csvProdRulesLoaded = false;
                ISO19650Validator.InvalidateValidatorCaches(); // PERF-01: clear cached code sets after config reload
                try { BIMManager.ExcelLinkEngine.InvalidateValidationCache(); } // DI-02: clear Excel validation caches on config reload
                catch (Exception) { /* ExcelLinkEngine may not be loaded yet */ }
                // Drop the cached PopulationContext because
                // KnownCategories is derived from DiscMap and may have changed.
                try { TokenAutoPopulator.PopulationContext.InvalidateCache(); }
                catch (Exception) { /* harmless if helper not yet initialised */ }

                // Load category warnings and paragraph containers from LABEL_DEFINITIONS
                LoadCategoryWarningsFromLabels();

                // Restore persisted active preset
                if (data.TryGetValue("ACTIVE_PRESET", out object presetObj) && presetObj is string presetStr)
                {
                    _activePresetName = presetStr;
                    // Defer actual SetActivePreset since BuiltInPresets may not be loaded yet
                    // — will be applied when first accessed
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagConfig load failed from {path}: {ex.Message}");
                LoadDefaults();
            }
        }

        public static void LoadDefaults()
        {
            DiscMap = DefaultDiscMap();
            SysMap = DefaultSysMap();
            ProdMap = DefaultProdMap();
            FuncMap = DefaultFuncMap();
            LocCodes = DefaultLocCodes();
            ZoneCodes = DefaultZoneCodes();
            _reverseSysMap = null; // Invalidate cache
            ParamRegistry.ClearTagFormatOverrides();
            StatusDefault = null;
            RevDefault = null;
            TagPrefix = string.Empty;
            TagSuffix = string.Empty;
            CategorySkipList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CategoryForceSys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            DisciplineProfiles = new Dictionary<string, DisciplineProfile>(StringComparer.OrdinalIgnoreCase);
            LocPatterns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "BLD1", new List<string> { "building 1", "main building", "block a", "primary" } },
                { "BLD2", new List<string> { "building 2", "annex", "block b", "secondary" } },
                { "BLD3", new List<string> { "building 3", "block c", "tertiary" } },
                { "EXT", new List<string> { "external", "exterior", "outside", "site", "landscape", "car park" } },
            };
            ZonePatterns = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "Z01", new List<string> { "zone 1", "zone a", "north", "front" } },
                { "Z02", new List<string> { "zone 2", "zone b", "south", "rear" } },
                { "Z03", new List<string> { "zone 3", "zone c", "east", "left" } },
                { "Z04", new List<string> { "zone 4", "zone d", "west", "right" } },
            };
            AutoRunWorkflowOnOpen = string.Empty;
            CategoryTokenOverrides = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            ConfigSource = "built-in defaults";
            ISO19650Validator.InvalidateValidatorCaches(); // PERF-01: clear cached code sets
            try { BIMManager.ExcelLinkEngine.InvalidateValidationCache(); } // DI-02: clear Excel validation caches
            catch (Exception) { /* ExcelLinkEngine may not be loaded yet */ }
            ComplianceGatePct = 0;
            SeparatorHistory = new List<string>();
            AutoRunWorkflowOnOpen = string.Empty;
            // NP11: Reset SEQ scheme state on LoadDefaults to prevent cross-project bleed
            CurrentSeqScheme = SeqScheme.Numeric;
            SeqIncludeZone = false;
            SeqLevelReset = false;
            _seqSchemeChanged = false;
            _seqSchemeWarned = false;
            _activePresetName = null;
            // Phase 77: Reset title block and sheet margin settings
            PreferredTitleBlockFamily = null;
            SheetMarginLeftMm = 15.0;
            SheetMarginRightMm = 55.0;
            SheetMarginTopMm = 10.0;
            SheetMarginBottomMm = 15.0;
            SheetMarginGapMm = 8.0;
            // Reset auto-tagger and phase-correction flags so cross-project state cannot bleed
            // when LoadDefaults() is called without a subsequent LoadFromFile().
            AutoTaggerEnabled = null;
            AutoTaggerVisual = null;
            AutoTaggerStaleMarker = null;
            AutoCorrectStatusFromPhase = false;
            // Reload FUNC/SYS matrix from CSV so custom project additions aren't lost on reset
            // Note: _validFuncsCsvLoaded/EnsureValidFuncsLoaded live in ISO19650Validator; use InvalidateValidatorCaches.
            ISO19650Validator.InvalidateValidatorCaches();
            // Load PROD code rules from CSV (lazy — invalidate so next GetFamilyAwareProdCode call reloads)
            _csvProdRulesLoaded = false;
            // Load category warnings and paragraph containers from LABEL_DEFINITIONS
            LoadCategoryWarningsFromLabels();
        }

        /// <summary>
        /// Load category-level warning assignments and paragraph container mappings
        /// from LABEL_DEFINITIONS.json. Populates ParamRegistry lookup tables used
        /// by EvaluateElementWarnings() and WriteTag7All() paragraph container writes.
        /// </summary>
        private static void LoadCategoryWarningsFromLabels()
        {
            try
            {
                string path = StingToolsApp.FindDataFile("LABEL_DEFINITIONS.json");
                if (path == null) return;

                string json = System.IO.File.ReadAllText(path);
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                var catLabels = root["category_labels"] as Newtonsoft.Json.Linq.JObject;
                if (catLabels == null) return;

                int warnCount = 0, paraCount = 0;
                foreach (var kvp in catLabels)
                {
                    string catName = kvp.Key;
                    var catDef = kvp.Value as Newtonsoft.Json.Linq.JObject;
                    if (catDef == null) continue;

                    // Warnings — supports both string arrays and rich objects with "param" field
                    var warnArr = catDef["warnings"] as Newtonsoft.Json.Linq.JArray;
                    if (warnArr != null && warnArr.Count > 0)
                    {
                        var warnNames = new List<string>();
                        foreach (var w in warnArr)
                        {
                            if (w.Type == Newtonsoft.Json.Linq.JTokenType.String)
                                warnNames.Add(w.ToString());
                            else if (w.Type == Newtonsoft.Json.Linq.JTokenType.Object)
                            {
                                string paramName = w["param"]?.ToString();
                                if (!string.IsNullOrEmpty(paramName))
                                    warnNames.Add(paramName);
                            }
                        }
                        if (warnNames.Count > 0)
                        {
                            ParamRegistry.RegisterCategoryWarnings(catName, warnNames);
                            warnCount += warnNames.Count;
                        }
                    }

                    // Paragraph containers
                    string paraContainer = catDef["paragraph_container"]?.ToString();
                    if (!string.IsNullOrEmpty(paraContainer))
                    {
                        ParamRegistry.RegisterParagraphContainer(catName, paraContainer);
                        paraCount++;
                    }
                }
                StingLog.Info($"TagConfig: loaded {warnCount} warning refs across categories, {paraCount} paragraph containers from LABEL_DEFINITIONS.json");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagConfig.LoadCategoryWarningsFromLabels: {ex.Message}");
            }

            // Supplement with warnings from tag config CSVs (ARCH/MEP/STR)
            LoadCategoryWarningsFromTagConfigCsvs();
        }

        /// <summary>
        /// Load category-level warning assignments from STING_TAG_CONFIG_v5_0_*.csv files.
        /// Supplements LABEL_DEFINITIONS.json warnings with any additional WARN_ params
        /// defined in the tag family warning sections.
        /// </summary>
        private static void LoadCategoryWarningsFromTagConfigCsvs()
        {
            // Routed through HandoverModeHelper so a DesignConstruction project
            // picks up warnings from the DC-variant CSVs and a Handover project
            // keeps using the default CSVs. Missing variants fall back to the
            // Handover defaults, so this is safe in any install.
            // Pass (Document)null so the helper falls through to the
            // PARAGRAPH_PRESETS.json active_preset (mode set by the last user
            // Apply); the string-mode overload would short-circuit to Handover.
            string[] csvFiles = new[]
            {
                HandoverModeHelper.GetTagConfigCsv("ARCH", (Document)null),
                HandoverModeHelper.GetTagConfigCsv("MEP",  (Document)null),
                HandoverModeHelper.GetTagConfigCsv("STR",  (Document)null),
            };

            int added = 0;
            foreach (string fileName in csvFiles)
            {
                try
                {
                    string path = StingToolsApp.FindDataFile(fileName);
                    if (path == null) continue;

                    string[] lines = System.IO.File.ReadAllLines(path);
                    string currentCategory = null;
                    bool inWarningSection = false;

                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();

                        // Parse category from "Category: Xxx" after tag family header
                        if (line.Contains("Category:"))
                        {
                            int idx = line.IndexOf("Category:");
                            currentCategory = line.Substring(idx + 9).Trim();
                            inWarningSection = false;
                            continue;
                        }

                        if (line.Contains("WARNING PARAMETERS"))
                        {
                            inWarningSection = true;
                            continue;
                        }

                        // End of warning section
                        if (inWarningSection && (line.StartsWith("Tag Family") || string.IsNullOrEmpty(line)))
                        {
                            inWarningSection = false;
                            continue;
                        }

                        // Skip header row
                        if (inWarningSection && line.StartsWith("#,"))
                            continue;

                        if (inWarningSection && currentCategory != null)
                        {
                            var fields = StingToolsApp.ParseCsvLine(line);
                            if (fields != null && fields.Length >= 3)
                            {
                                string warnParam = fields[2].Trim();
                                if (warnParam.StartsWith("WARN_"))
                                {
                                    // Merge with existing category warnings
                                    var existing = ParamRegistry.GetCategoryWarnings(currentCategory);
                                    if (!existing.Contains(warnParam))
                                    {
                                        existing.Add(warnParam);
                                        ParamRegistry.RegisterCategoryWarnings(currentCategory, existing);
                                        added++;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagConfig.LoadCategoryWarningsFromTagConfigCsvs({fileName}): {ex.Message}");
                }
            }

            if (added > 0)
                StingLog.Info($"TagConfig: supplemented {added} additional warning refs from tag config CSVs");
        }

        /// <summary>
        /// GAP-006: Persist current TagConfig state to project_config.json.
        /// Called by ProjectSetupWizard to ensure settings survive Revit restart.
        /// </summary>
        public static bool SaveToFile(string path)
        {
            try
            {
                var data = new Dictionary<string, object>
                {
                    ["DISC_MAP"] = DiscMap,
                    ["SYS_MAP"] = SysMap,
                    ["PROD_MAP"] = ProdMap,
                    ["FUNC_MAP"] = FuncMap,
                    ["LOC_CODES"] = LocCodes,
                    ["ZONE_CODES"] = ZoneCodes,
                    ["TAG_FORMAT"] = new Dictionary<string, object>
                    {
                        ["separator"] = Separator,
                        ["num_pad"] = NumPad,
                        ["segment_order"] = SegmentOrder
                    },
                    ["TAG_PREFIX"] = TagPrefix,
                    ["TAG_SUFFIX"] = TagSuffix,
                    ["CATEGORY_SKIP"] = CategorySkipList.ToList(),
                    ["CATEGORY_FORCE_SYS"] = CategoryForceSys,
                    ["COMPLIANCE_GATE_PCT"] = ComplianceGatePct,
                    ["SEPARATOR_HISTORY"] = SeparatorHistory,
                    ["AUTO_RUN_WORKFLOW_ON_OPEN"] = AutoRunWorkflowOnOpen ?? "",
                    ["CATEGORY_TOKEN_OVERRIDES"] = CategoryTokenOverrides,
                };

                // Persist auto-tagger state
                if (AutoTaggerEnabled.HasValue) data["AUTO_TAGGER_ENABLED"] = AutoTaggerEnabled.Value;
                // Phase 86b: Removed duplicate AUTO_TAGGER_VISUAL from initial dict — this is the single write point
                if (AutoTaggerVisual.HasValue) data["AUTO_TAGGER_VISUAL"] = AutoTaggerVisual.Value;
                else data["AUTO_TAGGER_VISUAL"] = Core.StingAutoTagger.IsVisualTaggingEnabled;
                if (AutoTaggerStaleMarker.HasValue) data["AUTO_TAGGER_STALE_MARKER"] = AutoTaggerStaleMarker.Value;

                string json = JsonConvert.SerializeObject(data, Formatting.Indented);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, json);
                StingLog.Info($"TagConfig saved to {path}");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error($"TagConfig save failed to {path}: {ex.Message}", ex);
                return false;
            }
        }

        private static readonly object _configWriteLock = new object();

        /// <summary>FIX-10.1: Set a single config key and persist to project_config.json (if ConfigSource is a file path).</summary>
        public static void SetConfigValue(string key, object value)
        {
            lock (_configWriteLock)
            {
                try
                {
                    if (string.IsNullOrEmpty(ConfigSource) || !File.Exists(ConfigSource)) return;
                    string json = File.ReadAllText(ConfigSource);
                    var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                        ?? new Dictionary<string, object>();
                    data[key] = value;
                    string tmp = ConfigSource + ".tmp";
                    File.WriteAllText(tmp, JsonConvert.SerializeObject(data, Formatting.Indented));
                    try { File.Replace(tmp, ConfigSource, ConfigSource + ".bak"); }
                    catch { File.Copy(tmp, ConfigSource, true); try { File.Delete(tmp); } catch { } }
                    // Invalidate cached config — GetConfigValue will re-read on next hit.
                    lock (_cfgCacheLock) { _cfgCached = null; _cfgCachedPath = null; _cfgCachedMTicks = 0; }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SetConfigValue '{key}': {ex.Message}");
                }
            }
        }

        // AE-02 cache — GetConfigValue was hit from dashboards / command
        // entry points and re-read + re-parsed project_config.json on every
        // call. Cache the deserialised dictionary keyed by (path, mtime)
        // so repeated reads are zero I/O.
        private static readonly object _cfgCacheLock = new object();
        private static string _cfgCachedPath;
        private static long _cfgCachedMTicks;
        private static Dictionary<string, object> _cfgCached;

        private static Dictionary<string, object> LoadConfigCached()
        {
            try
            {
                if (string.IsNullOrEmpty(ConfigSource) || !File.Exists(ConfigSource)) return null;
                long mtime = File.GetLastWriteTimeUtc(ConfigSource).Ticks;
                lock (_cfgCacheLock)
                {
                    if (_cfgCached != null
                        && string.Equals(_cfgCachedPath, ConfigSource, StringComparison.OrdinalIgnoreCase)
                        && _cfgCachedMTicks == mtime)
                        return _cfgCached;
                }
                string json = File.ReadAllText(ConfigSource);
                var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                lock (_cfgCacheLock)
                {
                    _cfgCached = data;
                    _cfgCachedPath = ConfigSource;
                    _cfgCachedMTicks = mtime;
                }
                return data;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadConfigCached: {ex.Message}");
                return null;
            }
        }

        /// <summary>AE-02: Read a single config key from project_config.json. Returns null if not found.
        /// Uses a (path, mtime) cache so repeat reads never touch disk.</summary>
        public static string GetConfigValue(string key)
        {
            try
            {
                var data = LoadConfigCached();
                if (data != null && data.TryGetValue(key, out object val) && val != null)
                    return val.ToString();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetConfigValue '{key}': {ex.Message}");
            }
            return null;
        }

        /// <summary>AL-05: Check compliance gate after a batch tag operation. Shows warning dialog if below threshold.</summary>
        /// <summary>
        /// GAP-12: Enhanced compliance gate with per-discipline breakdown and suggested actions.
        /// Shows which disciplines are below threshold and what actions to take.
        /// </summary>
        public static void CheckComplianceGate(Document doc, string commandName)
        {
            if (ComplianceGatePct <= 0) return;
            try
            {
                var result = ComplianceScan.Scan(doc, forceRefresh: true);
                if (result != null && result.CompliancePercent < ComplianceGatePct)
                {
                    double gap = ComplianceGatePct - result.CompliancePercent;
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"{commandName} complete but compliance ({result.CompliancePercent:F0}%) " +
                        $"is below the configured gate ({ComplianceGatePct}%).");
                    sb.AppendLine($"Gap: {gap:F0}% ({result.Untagged} untagged elements)");
                    sb.AppendLine();

                    // Per-discipline breakdown
                    if (result.ByDisc != null && result.ByDisc.Count > 0)
                    {
                        sb.AppendLine("By discipline:");
                        foreach (var kv in result.ByDisc.OrderBy(d => d.Value.CompliancePct))
                        {
                            string status = kv.Value.CompliancePct >= ComplianceGatePct ? "✓" : "✗";
                            sb.AppendLine($"  {kv.Key}: {kv.Value.CompliancePct:F0}% {status} " +
                                $"({kv.Value.Tagged}/{kv.Value.Total} tagged, " +
                                $"{kv.Value.MissingProd} missing PROD)");
                        }
                        sb.AppendLine();
                    }

                    // Suggested actions based on gap analysis
                    sb.AppendLine("Suggested actions:");
                    if (result.StaleCount > 0)
                        sb.AppendLine($"  1. Run 'Retag Stale' — {result.StaleCount} stale elements detected");
                    sb.AppendLine("  2. Run 'Pre-Tag Audit' to identify specific gaps");
                    if (result.Untagged > 50)
                        sb.AppendLine("  3. Run 'Batch Tag' to tag all untagged elements");
                    else if (result.Untagged > 0)
                        sb.AppendLine("  3. Run 'Resolve All Issues' for auto-fix");

                    Autodesk.Revit.UI.TaskDialog.Show("STING Compliance Gate", sb.ToString());
                    StingLog.Warn($"ComplianceGate: {commandName} result {result.CompliancePercent:F0}% < gate {ComplianceGatePct}%");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ComplianceGate check failed: {ex.Message}");
            }
        }

        /// <summary>Get the first valid SYS code for a category name. O(1) via cached reverse lookup.
        /// For categories with multiple valid systems (e.g., Pipes), returns the first match.
        /// Use <see cref="GetAllSysCodes"/> when the full list is needed.</summary>
        public static string GetSysCode(string categoryName)
        {
            var reverse = GetReverseSysMap();
            return reverse.TryGetValue(categoryName, out var list) && list.Count > 0 ? list[0] : string.Empty;
        }

        /// <summary>Get ALL valid SYS codes for a category (e.g., Pipes → DCW, DHW, SAN, RWD, GAS, FP, HWS).</summary>
        public static List<string> GetAllSysCodes(string categoryName)
        {
            var reverse = GetReverseSysMap();
            return reverse.TryGetValue(categoryName, out var list) ? list : new List<string>();
        }

        /// <summary>Get the FUNC code for a SYS code (basic lookup).</summary>
        public static string GetFuncCode(string sysCode)
        {
            return FuncMap.TryGetValue(sysCode, out string val) ? val : string.Empty;
        }

        /// <summary>
        /// Phase 176 — Lightning Protection family-aware FUNC resolution.
        /// When SYS=LPS, the FUNC token is one of 6 sub-functions
        /// (AT / DC / EE / BOND / SPD / TC) chosen from family/type name.
        /// Returns null for non-LPS elements (caller should fall back to
        /// FuncMap[sys]). Wired into the tagging pipeline by the
        /// Smart-FUNC layer so LPS finials → AT, down conductors → DC, etc.
        /// </summary>
        public static string ResolveLpsFunc(Element el)
        {
            if (el == null) return null;
            string fam = ParameterHelpers.GetFamilyName(el);
            string sym = ParameterHelpers.GetFamilySymbolName(el);
            string upper = ($"{fam} {sym}").ToUpperInvariant();
            if (upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                upper.Contains("STRIKE TERMINATION") || upper.Contains("AIR ROD") ||
                upper.Contains("AIR MESH") || upper.Contains("CATENARY"))
                return "AT";
            if (upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                upper.Contains("DESCENT"))
                return "DC";
            if (upper.Contains("EARTH ROD") || upper.Contains("EARTH ELECTRODE") ||
                upper.Contains("RING EARTH") || upper.Contains("FOUNDATION EARTH") ||
                upper.Contains("MESH EARTH") || upper.Contains("EARTH MESH") ||
                upper.Contains("EARTH PLATE"))
                return "EE";
            if (upper.Contains("EQUIPOTENTIAL") || upper.Contains("BONDING BAR") ||
                upper.Contains("EARTH BAR") || (upper.Contains("BOND") && upper.Contains("LPS")) ||
                upper.Contains("SPARK GAP"))
                return "BOND";
            if ((upper.Contains("SPD") || upper.Contains("SURGE PROTECT")) &&
                (upper.Contains("LIGHTNING") || upper.Contains("TYPE 1") ||
                 upper.Contains("TYPE 2") || upper.Contains("TYPE 3")))
                return "SPD";
            if (upper.Contains("TEST CLAMP") || upper.Contains("INSPECTION POINT"))
                return "TC";
            // Generic LPS family — leave as base "LPS" so caller falls back
            return null;
        }

        /// <summary>
        /// Phase 176 — true when family/type name shows lightning-protection
        /// markers. Used by validators, container resolvers and LPS warning
        /// writers to scope BS EN 62305 checks to the relevant elements only.
        /// </summary>
        public static bool IsLightningProtection(Element el)
        {
            if (el == null) return false;
            string fam = ParameterHelpers.GetFamilyName(el);
            string sym = ParameterHelpers.GetFamilySymbolName(el);
            string upper = ($"{fam} {sym}").ToUpperInvariant();
            return upper.Contains("LPS") || upper.Contains("LIGHTNING") ||
                   upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                   upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                   upper.Contains("EARTH ROD") || upper.Contains("EARTH ELECTRODE") ||
                   upper.Contains("RING EARTH") || upper.Contains("FOUNDATION EARTH") ||
                   upper.Contains("MESH EARTH") || upper.Contains("EARTH MESH") ||
                   upper.Contains("EQUIPOTENTIAL") || upper.Contains("BONDING BAR") ||
                   upper.Contains("EARTH BAR") || upper.Contains("TEST CLAMP") ||
                   upper.Contains("INSPECTION POINT") || upper.Contains("SPARK GAP");
        }

        /// <summary>
        /// Get a guaranteed default SYS code from a discipline code.
        /// Used as a fallback when MEP system detection returns empty —
        /// ensures every element gets a valid SYS token.
        /// M→HVAC, E→LV, P→DCW (cold water bias), A→ARC, S→STR, FP→FP, LV→LV, G→GEN, else GEN.
        /// </summary>
        public static string GetDiscDefaultSysCode(string disc)
        {
            switch (disc)
            {
                case "M":  return "HVAC";
                case "E":  return "LV";
                case "P":  return "DCW"; // Cold water is more prevalent than DHW for unconnected pipes
                case "A":  return "ARC";
                case "S":  return "STR";
                case "FP": return "FP";
                case "LV": return "LV";
                case "G":  return "GEN"; // Generic Models/Specialty Equipment — not gas-specific
                default:   return "GEN";
            }
        }

        /// <summary>
        /// Enhanced FUNC code derivation using element's MEP system context.
        /// For HVAC, differentiates Supply (SUP), Return (RTN), Exhaust (EXH), Fresh Air (FRA).
        /// For HWS, differentiates Heating (HTG) vs Domestic Hot Water (DHW).
        /// Falls back to FuncMap lookup when no subsystem detail is available.
        /// </summary>
        public static string GetSmartFuncCode(Element el, string sysCode)
        {
            if (string.IsNullOrEmpty(sysCode))
                return "GEN";

            // For HVAC, try to detect subsystem function from connector/system name
            if (sysCode == "HVAC")
            {
                string hvacFunc = GetHvacSubFunction(el);
                if (!string.IsNullOrEmpty(hvacFunc)) return hvacFunc;
            }

            // For HWS, distinguish heating vs domestic hot water
            if (sysCode == "HWS")
            {
                string hwsFunc = GetHwsSubFunction(el);
                if (!string.IsNullOrEmpty(hwsFunc)) return hwsFunc;
            }

            return FuncMap.TryGetValue(sysCode, out string val) ? val : string.Empty;
        }

        /// <summary>
        /// Detect HVAC subsystem function: Supply, Return, Exhaust, Fresh Air, Extract.
        /// Reads from connector system name, duct system type parameter, and family name.
        /// </summary>
        private static string GetHvacSubFunction(Element el)
        {
            try
            {
                // Check connector system name
                FamilyInstance fi = el as FamilyInstance;
                if (fi?.MEPModel?.ConnectorManager != null)
                {
                    foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                    {
                        if (conn.MEPSystem != null)
                        {
                            string sysName = conn.MEPSystem.Name?.ToUpperInvariant() ?? "";
                            if (sysName.Contains("SUPPLY")) return "SUP";
                            if (sysName.Contains("RETURN")) return "RTN";
                            if (sysName.Contains("EXHAUST") || sysName.Contains("EXTRACT")) return "EXH";
                            if (sysName.Contains("FRESH") || sysName.Contains("OUTSIDE AIR")) return "FRA";
                        }
                    }
                }

                // Check duct system type parameter
                Parameter ductSys = el.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                if (ductSys != null && ductSys.HasValue)
                {
                    string val = ductSys.AsValueString()?.ToUpperInvariant() ?? "";
                    if (val.Contains("SUPPLY")) return "SUP";
                    if (val.Contains("RETURN")) return "RTN";
                    if (val.Contains("EXHAUST") || val.Contains("EXTRACT")) return "EXH";
                }

                // Check family name for duct-related equipment
                string familyName = ParameterHelpers.GetFamilyName(el).ToUpperInvariant();
                if (familyName.Contains("SUPPLY") || familyName.Contains("DIFFUSER")) return "SUP";
                if (familyName.Contains("RETURN") || familyName.Contains("RETURN GRILLE")) return "RTN";
                if (familyName.Contains("EXHAUST") || familyName.Contains("EXTRACT FAN")) return "EXH";
            }
            catch (Exception ex) { StingLog.Warn($"HVAC sub-function detection failed: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Detect HWS sub-function: Heating (HTG) vs Domestic Hot Water (DHW).
        /// </summary>
        private static string GetHwsSubFunction(Element el)
        {
            try
            {
                FamilyInstance fi = el as FamilyInstance;
                if (fi?.MEPModel?.ConnectorManager != null)
                {
                    foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                    {
                        if (conn.MEPSystem != null)
                        {
                            string sysName = conn.MEPSystem.Name?.ToUpperInvariant() ?? "";
                            if (sysName.Contains("HEATING") || sysName.Contains("LTHW") ||
                                sysName.Contains("MTHW") || sysName.Contains("RADIATOR"))
                                return "HTG";
                            if (sysName.Contains("DOMESTIC") || sysName.Contains("DHW") ||
                                sysName.Contains("HOT WATER SUPPLY"))
                                return "DHW";
                        }
                    }
                }

                string familyName = ParameterHelpers.GetFamilyName(el).ToUpperInvariant();
                if (familyName.Contains("RADIATOR") || familyName.Contains("UNDERFLOOR")) return "HTG";
                if (familyName.Contains("CALORIFIER") || familyName.Contains("WATER HEATER")) return "DHW";
            }
            catch (Exception ex) { StingLog.Warn($"HWS sub-function detection failed: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Family-name-aware product code resolution. Checks the element's family name
        /// for specific equipment patterns before falling back to category-based lookup.
        /// This gives more specific PROD codes: e.g., "FCU-01" → FCU, "VAV Box" → VAV,
        /// instead of the generic category code like "AHU" for all Mechanical Equipment.
        /// </summary>
        // ── CSV-driven PROD code rule table ─────────────────────────────
        // Loaded lazily from STING_PROD_CODES.csv on first call.
        // Key = category name (case-insensitive); value = ordered list of (pattern, prodCode) pairs.
        private static Dictionary<string, List<(string Pattern, string ProdCode)>> _csvProdRules;
        private static bool _csvProdRulesLoaded = false;

        private static void EnsureProdRulesLoaded()
        {
            if (_csvProdRulesLoaded) return;
            _csvProdRulesLoaded = true;
            _csvProdRules = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string csvPath = StingToolsApp.FindDataFile("STING_PROD_CODES.csv");
                if (string.IsNullOrEmpty(csvPath) || !System.IO.File.Exists(csvPath)) return;

                bool first = true;
                foreach (string raw in System.IO.File.ReadLines(csvPath))
                {
                    if (first) { first = false; continue; }
                    string line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    // CSV: PROD_CODE,CATEGORY,FAMILY_PATTERN,DESCRIPTION,...
                    var cols = StingToolsApp.ParseCsvLine(line);
                    if (cols == null || cols.Length < 3) continue;
                    string prodCode = cols[0].Trim();
                    string category = cols[1].Trim();
                    string pattern  = cols[2].Trim().ToUpperInvariant();
                    if (string.IsNullOrEmpty(prodCode) || string.IsNullOrEmpty(category) || string.IsNullOrEmpty(pattern)) continue;

                    if (!_csvProdRules.TryGetValue(category, out var list))
                    { list = new List<(string, string)>(); _csvProdRules[category] = list; }
                    list.Add((pattern, prodCode));
                }
                StingLog.Info($"TagConfig: loaded {_csvProdRules.Count} category rule sets from STING_PROD_CODES.csv");
            }
            catch (Exception ex) { StingLog.Warn($"TagConfig: EnsureProdRulesLoaded failed: {ex.Message}"); }
        }

        /// <summary>
        /// N+2 — Material-aware wrapper around the legacy
        /// <see cref="GetFamilyAwareProdCodeCore"/>. Looks up an
        /// optional material-driven suffix (-STL / -CON / -TIM / etc.)
        /// from <c>STING_MATERIAL_PROD_OVERRIDES.csv</c> and appends it
        /// to the base PROD code. Falls through to the legacy behaviour
        /// when no material rule matches.
        /// </summary>
        public static string GetFamilyAwareProdCode(Element el, string categoryName)
        {
            string baseProd = GetFamilyAwareProdCodeCore(el, categoryName);
            try
            {
                string suffix = MaterialProdOverrideRegistry.ResolveSuffix(el, categoryName);
                if (string.IsNullOrEmpty(suffix)) return baseProd;
                if (string.IsNullOrEmpty(baseProd)) return suffix;
                // Avoid double-suffixing when an explicit CSV PROD already
                // ends with the same material code (e.g. "STL" → "STL-STL").
                if (baseProd.EndsWith("-" + suffix, StringComparison.OrdinalIgnoreCase) ||
                    baseProd.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return baseProd;
                return $"{baseProd}-{suffix}";
            }
            catch (Exception ex) { StingLog.Warn($"GetFamilyAwareProdCode material suffix: {ex.Message}"); }
            return baseProd;
        }

        private static string GetFamilyAwareProdCodeCore(Element el, string categoryName)
        {
            string familyName = ParameterHelpers.GetFamilyName(el);
            string symbolName = ParameterHelpers.GetFamilySymbolName(el);
            // Combined name checks both family AND type name for broader pattern matching
            string combinedName = $"{familyName} {symbolName}".ToUpperInvariant();

            // CSV pre-lookup: try data-driven rules before falling through to hardcoded branches
            EnsureProdRulesLoaded();
            if (!string.IsNullOrEmpty(familyName) && _csvProdRules != null
                && _csvProdRules.TryGetValue(categoryName, out var csvRules))
            {
                foreach (var (pattern, prodCode) in csvRules)
                    if (combinedName.Contains(pattern)) return prodCode;
            }

            // Only apply family-level overrides for categories with diverse equipment
            if (!string.IsNullOrEmpty(familyName))
            {
                // Search both family name and combined (family + type) name for patterns
                string upper = combinedName;

                // ── Lightning Protection System (BS EN 62305) ────────────────
                // LPS elements may be modelled in Electrical Equipment, Generic Models,
                // Conduits, or Specialty Equipment. Family-name discriminates the
                // 6 LPS sub-element kinds (12 PROD codes) regardless of category.
                // Phase 176 — wired into LPS tag families #54 to #59.
                if (upper.Contains("LPS") || upper.Contains("LIGHTNING") ||
                    upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                    upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                    upper.Contains("EARTH ROD") || upper.Contains("EARTH ELECTRODE") ||
                    upper.Contains("RING EARTH") || upper.Contains("FOUNDATION EARTH") ||
                    upper.Contains("TEST CLAMP") || upper.Contains("EQUIPOTENTIAL"))
                {
                    if (upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                        upper.Contains("STRIKE TERMINATION") || upper.Contains("AIR ROD"))
                        return "ATR";  // Air terminal rod
                    if (upper.Contains("AIR MESH") || upper.Contains("MESH NODE"))
                        return "AMS";  // Air mesh section
                    if (upper.Contains("CATENARY"))
                        return "ACT";  // Air catenary
                    if (upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                        upper.Contains("DESCENT"))
                        return "DCN";  // Down conductor
                    if (upper.Contains("EARTH ROD") || upper.Contains("ROD EARTH"))
                        return "ERD";  // Earth rod
                    if (upper.Contains("RING EARTH") || upper.Contains("EARTH RING"))
                        return "ERG";  // Ring earth
                    if (upper.Contains("FOUNDATION EARTH"))
                        return "EFE";  // Foundation earth
                    if (upper.Contains("MESH EARTH") || upper.Contains("EARTH MESH"))
                        return "EME";  // Mesh earth
                    if (upper.Contains("EARTH ELECTRODE") || upper.Contains("EARTH PLATE"))
                        return "ERD";
                    if (upper.Contains("BOND") || upper.Contains("EQUIPOTENTIAL"))
                        return "BCN";  // Bond conductor (default)
                    if (upper.Contains("BONDING BAR") || upper.Contains("EARTH BAR"))
                        return "BBR";
                    if (upper.Contains("SPARK GAP"))
                        return "BSG";
                    if (upper.Contains("TYPE 1") && upper.Contains("SPD"))
                        return "SPD1";
                    if (upper.Contains("TYPE 2") && upper.Contains("SPD"))
                        return "SPD2";
                    if (upper.Contains("TYPE 3") && upper.Contains("SPD"))
                        return "SPD3";
                    if (upper.Contains("TEST CLAMP") || upper.Contains("INSPECTION POINT"))
                        return "TCL";
                    // Generic LPS fallback — always return some LPS PROD code
                    if (upper.Contains("LPS") || upper.Contains("LIGHTNING"))
                        return "LPS";
                }

                // Mechanical Equipment — distinguish AHU, FCU, VAV, CHR, BLR, PMP, FAN, etc.
                if (categoryName == "Mechanical Equipment")
                {
                    if (upper.Contains("FCU") || upper.Contains("FAN COIL")) return "FCU";
                    if (upper.Contains("VAV") || upper.Contains("VARIABLE AIR")) return "VAV";
                    if (upper.Contains("CHILLER") || upper.Contains("CHR")) return "CHR";
                    if (upper.Contains("BOILER") || upper.Contains("BLR")) return "BLR";
                    if (upper.Contains("PUMP") || upper.Contains("PMP")) return "PMP";
                    if (upper.Contains("FAN") || upper.Contains("EXF")) return "FAN";
                    if (upper.Contains("HRU") || upper.Contains("HEAT RECOVERY")) return "HRU";
                    if (upper.Contains("SPLIT") || upper.Contains("CASSETTE")) return "SPL";
                    if (upper.Contains("INDUCTION")) return "IND";
                    if (upper.Contains("RADIANT") || upper.Contains("RAD PANEL")) return "RAD";
                    if (upper.Contains("DAMPER") || upper.Contains("DAM")) return "DAM";
                    if (upper.Contains("COOLING TOWER") || upper.Contains("CLT")) return "CLT";
                    if (upper.Contains("VFD") || upper.Contains("VARIABLE FREQ") || upper.Contains("INVERTER")) return "VFD";
                    if (upper.Contains("AHU") || upper.Contains("AIR HANDLING")) return "AHU";
                }
                // Electrical Equipment — distinguish DB, MCC, MSB, SWB, UPS, TRF, GEN, etc.
                else if (categoryName == "Electrical Equipment")
                {
                    if (upper.Contains("MCC") || upper.Contains("MOTOR CONTROL")) return "MCC";
                    if (upper.Contains("MSB") || upper.Contains("MAIN SWITCH")) return "MSB";
                    if (upper.Contains("SWB") || upper.Contains("SWITCHBOARD")) return "SWB";
                    if (upper.Contains("UPS") || upper.Contains("UNINTERRUPT")) return "UPS";
                    if (upper.Contains("TRANSFORMER") || upper.Contains("TRF")) return "TRF";
                    if (upper.Contains("GENERATOR") || upper.Contains("GEN SET")) return "GEN";
                    if (upper.Contains("ATS") || upper.Contains("AUTO TRANSFER")) return "ATS";
                    if (upper.Contains("VFD") || upper.Contains("VARIABLE FREQ") || upper.Contains("DRIVE")) return "VFD";
                    if (upper.Contains("SPD") || upper.Contains("SURGE")) return "SPD";
                    if (upper.Contains("RCD") || upper.Contains("RESIDUAL")) return "RCD";
                    if (upper.Contains("ISOLAT") || upper.Contains("DISCONNECT")) return "ISO";
                    if (upper.Contains("SOFT START")) return "SFS";
                    if (upper.Contains("BATTERY") || upper.Contains("BKP")) return "BKP";
                    if (upper.Contains("DB") || upper.Contains("DISTRIBUTION")) return "DB";
                }
                // Lighting — distinguish LUM, EML, DEC, TRK, DWN, LIN, SPT, etc.
                else if (categoryName == "Lighting Fixtures")
                {
                    if (upper.Contains("EMERGENCY") || upper.Contains("EML") || upper.Contains("EXIT")) return "EML";
                    if (upper.Contains("TRACK") || upper.Contains("TRK")) return "TRK";
                    if (upper.Contains("DECORATIVE") || upper.Contains("PENDANT") || upper.Contains("CHANDELIER")) return "DEC";
                    if (upper.Contains("DOWNLIGHT") || upper.Contains("RECESSED")) return "DWN";
                    if (upper.Contains("LINEAR") || upper.Contains("CONTINUOUS") || upper.Contains("BATTEN")) return "LIN";
                    if (upper.Contains("SPOTLIGHT") || upper.Contains("PROJECTOR")) return "SPT";
                    if (upper.Contains("WALL") && (upper.Contains("WASH") || upper.Contains("LIGHT"))) return "WSH";
                    if (upper.Contains("BOLLARD")) return "BOL";
                    if (upper.Contains("UPLIGHT") || upper.Contains("UPLIGHTER")) return "UPL";
                    if (upper.Contains("FLOOD") || upper.Contains("FLOODLIGHT")) return "FLD";
                }
                // Plumbing Fixtures — distinguish WC, WHB, URN, SNK, SHW, BTH, etc.
                else if (categoryName == "Plumbing Fixtures")
                {
                    if (upper.Contains("WC") || upper.Contains("WATER CLOSET") || upper.Contains("TOILET")) return "WC";
                    if (upper.Contains("WHB") || upper.Contains("WASH HAND") || upper.Contains("BASIN")) return "WHB";
                    if (upper.Contains("URINAL") || upper.Contains("URN")) return "URN";
                    if (upper.Contains("SINK") || upper.Contains("SNK")) return "SNK";
                    if (upper.Contains("SHOWER") || upper.Contains("SHW")) return "SHW";
                    if (upper.Contains("BATH") || upper.Contains("BTH")) return "BTH";
                    if (upper.Contains("DRINKING") || upper.Contains("FOUNTAIN")) return "DRK";
                    if (upper.Contains("COOLER") || upper.Contains("WATER COOLER")) return "CWL";
                    if (upper.Contains("GREASE") || upper.Contains("TRAP")) return "TRP";
                    if (upper.Contains("BIDET")) return "BID";
                    if (upper.Contains("EYEWASH") || upper.Contains("EYE WASH")) return "EWS";
                    if (upper.Contains("MOP") && upper.Contains("SINK")) return "MOP";
                }
                // Fire Alarm — distinguish FAD, SML, MCP, BLL, STB, etc.
                else if (categoryName == "Fire Alarm Devices")
                {
                    if (upper.Contains("SMOKE") || upper.Contains("DETECTOR") || upper.Contains("SML")) return "SML";
                    if (upper.Contains("MCP") || upper.Contains("CALL POINT") || upper.Contains("MANUAL")) return "MCP";
                    if (upper.Contains("BELL") || upper.Contains("SOUNDER") || upper.Contains("BLL")) return "BLL";
                    if (upper.Contains("STROBE") || upper.Contains("BEACON")) return "STB";
                    if (upper.Contains("HEAT") && upper.Contains("DETECT")) return "HTD";
                    if (upper.Contains("INTERFACE") || upper.Contains("MODULE")) return "FIM";
                }
                // Pipe Accessories — distinguish valve types
                else if (categoryName == "Pipe Accessories")
                {
                    if (upper.Contains("BALANCING") || upper.Contains("BLV")) return "BLV";
                    if (upper.Contains("TRV") || upper.Contains("THERMOSTATIC") || upper.Contains("RADIATOR VALVE")) return "TRV";
                    if (upper.Contains("ISOLATION") || upper.Contains("GATE") || upper.Contains("BALL")) return "IVL";
                    if (upper.Contains("CHECK") || upper.Contains("NON RETURN") || upper.Contains("NRV")) return "NRV";
                    if (upper.Contains("PRESSURE REDUC") || upper.Contains("PRV")) return "PRV";
                    if (upper.Contains("STRAINER") || upper.Contains("FILTER")) return "STN";
                }
                // Generic Models — MEP Sleeves (fire-rated penetration elements)
                else if (categoryName == "Generic Models")
                {
                    if (upper.Contains("SLEEVE") || upper.Contains("SLV") || upper.Contains("PENETRATION")
                        || upper.Contains("FIRESTOP") || upper.Contains("FIRE STOP") || upper.Contains("FIRE SEAL")) return "SLV";
                }
            }

            // Fall back to category-based PROD code
            string fallbackProd = ProdMap.TryGetValue(categoryName, out string prod) ? prod : "GEN";
            return fallbackProd;
        }

        /// <summary>
        /// Check if a tag string has the expected number of non-empty tokens.
        /// A tag is only "complete" when it has exactly expectedTokens segments
        /// and none of them are empty strings.
        /// </summary>
        // Removed dead _separatorHistory char[] array — SeparatorHistory list property
        // (loaded from project_config.json) is the actual implementation used in TagIsComplete.

        public static bool TagIsComplete(string tagValue, int expectedTokens = 8)
        {
            if (string.IsNullOrEmpty(tagValue))
                return false;
            // Adjust expected count for global tag prefix/suffix
            int adjusted = expectedTokens
                + (!string.IsNullOrEmpty(TagPrefix) ? 1 : 0)
                + (!string.IsNullOrEmpty(TagSuffix) ? 1 : 0);
            string sepStr = !string.IsNullOrEmpty(Separator) ? Separator : "-";
            string[] parts = tagValue.Split(new[] { sepStr }, StringSplitOptions.None);

            if (parts.Length != adjusted)
            {
                // Try historical separators — tags created before a separator change
                // should still be recognised as complete.
                bool foundViaHistory = false;
                foreach (var histSep in SeparatorHistory)
                {
                    if (histSep == Separator) continue;
                    var histParts = tagValue.Split(new[] { histSep }, StringSplitOptions.None);
                    if (histParts.Length == adjusted && histParts.All(p => !string.IsNullOrWhiteSpace(p)))
                    {
                        foundViaHistory = true;
                        break;
                    }
                }
                if (!foundViaHistory)
                    return false;
            }
            else
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(parts[i]))
                        return false;
                }
            }
            // Reject tags containing placeholder tokens
            if (TagHasPlaceholders(tagValue))
                return false;

            return true;
        }

        /// <summary>
        /// Checks whether a tag string contains placeholder tokens ("-XX-", "-ZZ-", "-GEN-", "-0000")
        /// that indicate incomplete or unresolved segments.
        /// </summary>
        public static bool TagHasPlaceholders(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return false;
            string sep = !string.IsNullOrEmpty(Separator) ? Separator : "-";
            foreach (string ph in _placeholders)
            {
                // Check for placeholder as a delimited segment (not substring of a real token)
                if (tag.StartsWith(ph + sep, StringComparison.Ordinal) ||
                    tag.EndsWith(sep + ph, StringComparison.Ordinal) ||
                    tag.Contains(sep + ph + sep, StringComparison.Ordinal) ||
                    tag == ph)
                    return true;
            }
            return false;
        }

        private static readonly HashSet<string> _placeholders = new HashSet<string> { "XX", "ZZ", "GEN", "0000" };

        /// <summary>
        /// Strict tag completeness check. In addition to the standard check,
        /// rejects tags where any segment is a placeholder ("XX", "ZZ", "0000").
        /// Useful for compliance dashboards that require fully-resolved tags.
        /// </summary>
        public static bool TagIsFullyResolved(string tagValue, int expectedTokens = 8)
        {
            if (!TagIsComplete(tagValue, expectedTokens))
                return false;
            string sepStr = !string.IsNullOrEmpty(Separator) ? Separator : "-";
            string[] parts = tagValue.Split(new[] { sepStr }, StringSplitOptions.None);
            // Reject placeholder segments
            var placeholders = _placeholders;
            for (int i = 0; i < parts.Length; i++)
            {
                if (placeholders.Contains(parts[i]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Shared tag-building logic. Derives all 8 tokens for an element and writes
        /// both the individual token parameters and the assembled tag.
        /// Used by AutoTag, BatchTag, TagSelected to eliminate code duplication.
        /// Includes collision detection: if the generated tag already exists in the
        /// project, the SEQ is auto-incremented until a unique tag is found.
        ///
        /// Intelligence layers:
        ///   1. Category → DISC/SYS/FUNC/PROD lookup (with family-aware PROD)
        ///   2. Spatial auto-detect for LOC/ZONE (pre-populated by caller)
        ///   3. Level auto-derivation
        ///   4. O(1) collision detection via existingTags HashSet
        ///   5. Cross-validation: DISC vs category consistency check
        ///   6. MEP system-aware grouping: uses connected system name for SYS/FUNC when available
        ///   7. Collision stats tracked via TaggingStats for post-batch reporting
        /// </summary>
        /// <param name="existingTags">
        /// Project-wide tag index for collision detection. Pass null to skip collision
        /// checks (legacy behaviour). Build once per batch via <see cref="BuildExistingTagIndex"/>.
        /// New tags are added to this set automatically so subsequent calls stay current.
        /// </param>
        /// <param name="collisionMode">
        /// Controls how existing/duplicate tags are handled:
        /// AutoIncrement (default) = auto-increment SEQ on collision;
        /// Skip = skip already-tagged elements entirely;
        /// Overwrite = overwrite all tokens with fresh values.
        /// </param>
        /// <param name="stats">Optional stats tracker for batch reporting.</param>
        /// <param name="cachedRev">Optional pre-cached project revision string. When provided,
        /// skips the per-element FilteredElementCollector call in PhaseAutoDetect.DetectProjectRevision,
        /// improving batch performance from O(n²) to O(n).</param>
        /// <returns>True if the element was tagged, false if skipped.</returns>
        public static bool BuildAndWriteTag(Document doc, Element el,
            Dictionary<string, int> sequenceCounters, bool skipComplete = true,
            HashSet<string> existingTags = null,
            TagCollisionMode collisionMode = TagCollisionMode.AutoIncrement,
            TaggingStats stats = null,
            string cachedRev = null,
            List<Phase> cachedPhases = null,
            ElementId lastPhaseId = null,
            string prevTagHint = null,
            string[] tokenValuesOut = null)
        {
            string catName = ParameterHelpers.GetCategoryName(el);
            // F-14: Merge ContainsKey guard + TryGetValue into a single map lookup
            if (string.IsNullOrEmpty(catName) || !DiscMap.TryGetValue(catName, out string disc))
                return false;

            // RunFullPipeline already read TAG1 once — accept
            // the value via the new prevTagHint parameter to avoid a second read.
            string existingTag = prevTagHint
                ?? ParameterHelpers.GetString(el, ParamRegistry.TAG1);
            bool hasCompleteTag = TagIsComplete(existingTag);

            // A-9: idempotency guard — if the element's last-written tag equals
            // the current tag and the tag is complete, the element was already
            // processed in this session. Cheap O(1) escape that avoids the
            // collision loop entirely (only honoured when not overwriting).
            if (collisionMode != TagCollisionMode.Overwrite && hasCompleteTag)
            {
                string prev = ParameterHelpers.GetString(el, ParamRegistry.TAG_PREV);
                if (!string.IsNullOrEmpty(prev)
                    && string.Equals(prev, existingTag, StringComparison.Ordinal))
                {
                    stats?.RecordSkipped(catName);
                    return true;
                }
            }

            if (hasCompleteTag)
            {
                switch (collisionMode)
                {
                    case TagCollisionMode.Skip:
                        stats?.RecordSkipped(catName);
                        return false; // Never touch existing complete tags
                    case TagCollisionMode.AutoIncrement:
                        if (skipComplete)
                        {
                            stats?.RecordSkipped(catName);
                            return false; // Default: skip complete tags
                        }
                        break;
                    case TagCollisionMode.Overwrite:
                        // Record with existing token values being overwritten
                        stats?.RecordOverwritten(catName,
                            ParameterHelpers.GetString(el, ParamRegistry.DISC),
                            ParameterHelpers.GetString(el, ParamRegistry.SYS),
                            ParameterHelpers.GetString(el, ParamRegistry.LVL));
                        break; // Proceed to overwrite
                }
            }

            bool overwriteTokens = (collisionMode == TagCollisionMode.Overwrite);

            // disc already retrieved above via TryGetValue (F-14). Fallback to "G" if null (safety).
            if (string.IsNullOrEmpty(disc)) disc = "G";

            string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
            if (string.IsNullOrEmpty(loc) || loc == "XX")
            {
                // First valid non-placeholder code from LocCodes, else hardcoded default
                loc = LocCodes.FirstOrDefault(c => c != "XX" && !string.IsNullOrEmpty(c)) ?? "BLD1";
            }
            string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
            // M-04 FIX: Also normalize "ZZ" placeholder (matching BuildTagIndexAndCounters
            // which normalizes XX/ZZ→Z01) to prevent SEQ counter key mismatch
            if (string.IsNullOrEmpty(zone) || zone == "XX" || zone == "ZZ")
            {
                zone = ZoneCodes.FirstOrDefault(c => c != "XX" && c != "ZZ" && !string.IsNullOrEmpty(c)) ?? "Z01";
            }
            string lvl = ParameterHelpers.GetLevelCode(doc, el);
            // Guaranteed LVL default: replace unresolved "XX"/"" with "L00" for levelless elements
            if (string.IsNullOrEmpty(lvl) || lvl == "XX") lvl = "L00";

            // on the non-overwrite path we trust whatever
            // PopulateAll already wrote — reading the element bypasses the
            // expensive per-element MEP connector walk inside
            // GetMepSystemAwareSysCode (and the Smart FUNC / family-aware PROD
            // helpers). On the overwrite path we deliberately want fresh
            // derivation so users can force a re-detect, so the legacy path
            // still runs.
            //
            // Independent callers like BuildTagsCommand pass overwriteTokens=
            // false but DON'T pre-populate the element first; for them, the
            // GetString returns empty and we fall through to the same derivation
            // that ran before — behaviour unchanged.
            string sys = null;
            if (!overwriteTokens) sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            if (string.IsNullOrEmpty(sys))
            {
                // Intelligence Layer: MEP system-aware SYS/FUNC derivation
                // 6-layer system detection: connector → sys param → circuit → family → room → category
                sys = GetMepSystemAwareSysCode(el, catName);
                if (string.IsNullOrEmpty(sys))
                    sys = GetDiscDefaultSysCode(disc);
            }

            // Intelligence Layer: System-aware DISC correction for pipes
            // Pipes are mapped to "M" by default, but if the connected system is plumbing
            // (DCW, DHW, SAN, RWD, GAS), the DISC should be "P" (Plumbing).
            disc = GetSystemAwareDisc(disc, sys, catName);

            string func = null;
            if (!overwriteTokens) func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
            if (string.IsNullOrEmpty(func))
            {
                // Smart FUNC: differentiates HVAC (SUP/RTN/EXH/FRA) and HWS (HTG/DHW) subsystems
                func = GetSmartFuncCode(el, sys);
                if (string.IsNullOrEmpty(func))
                    func = FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";
            }

            string prod = null;
            if (!overwriteTokens) prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
            if (string.IsNullOrEmpty(prod))
            {
                prod = GetFamilyAwareProdCode(el, catName);
                if (string.IsNullOrEmpty(prod))
                    prod = ProdMap.TryGetValue(catName, out string cp) ? cp : "GEN";
            }

            // Throttle default-value warnings — record count, not per-element message.
            // Previously: 1000 elements with default ZONE → 1000 warning records with file I/O.
            if (stats != null)
            {
                if (loc == "BLD1") stats.DefaultLocCount++;
                if (zone == "Z01") stats.DefaultZoneCount++;
            }

            // Validate-before-write — guarantee all 7 tokens are non-empty
            // before building the tag string. Applies hardcoded defaults as a safety net.
            if (string.IsNullOrEmpty(disc)) disc = "A";
            if (string.IsNullOrEmpty(loc))  loc  = "BLD1";
            if (string.IsNullOrEmpty(zone)) zone = "Z01";
            if (string.IsNullOrEmpty(lvl))  lvl  = "L00";
            if (string.IsNullOrEmpty(sys))  sys  = "GEN";
            if (string.IsNullOrEmpty(func)) func = "GEN";
            if (string.IsNullOrEmpty(prod)) prod = "GEN";

            // Always use DERIVED token values for seqKey, not stored values.
            // In non-overwrite mode, SetIfEmpty preserves existing stored values on the element,
            // but the SEQ counter group MUST use the canonical derived values to prevent:
            //   - Counter group mismatch when stored SYS="HWS" but derived SYS="DCW"
            //   - Duplicate SEQ numbers across mismatched groups
            //   - Counter drift between sessions
            // The tag string itself will be rebuilt from actual stored values later (line 2234+).
            string seqKey = SeqIncludeZone
                ? $"{disc}_{zone}_{sys}_{lvl}"
                : $"{disc}_{sys}_{lvl}";

            // A1: Warn once per session when SEQ scheme has changed — counter keys may not
            // match existing tags, leading to duplicate or restarted sequences.
            if (_seqSchemeChanged && !_seqSchemeWarned)
            {
                StingLog.Warn($"SEQ scheme changed (scheme={CurrentSeqScheme}, includeZone={SeqIncludeZone}). " +
                    "Existing SEQ counters may not align with the new key format. " +
                    "Run 'Assign Numbers' or 'Batch Tag' with Overwrite to re-sequence.");
                _seqSchemeWarned = true;
            }

            // Build SEQ-scheme context + the tag body/suffix (Revit-side config),
            // then delegate the counter / overflow / collision arithmetic to the
            // pure, unit-tested SeqAssigner. The tag is composed as
            // tagBody + seq + tagSuffix so the collision check matches the stored
            // string. AssignNext rolls the counter back on any failure.
            string seqSchemeContext = CurrentSeqScheme == SeqScheme.ZonePrefix ? zone
                                   : CurrentSeqScheme == SeqScheme.DiscPrefix ? disc
                                   : "";

            string tagBody = string.Join(Separator, disc, loc, zone, lvl, sys, func, prod);
            if (!string.IsNullOrEmpty(TagPrefix)) tagBody = TagPrefix + Separator + tagBody;
            tagBody += Separator;
            string tagSuffix = string.IsNullOrEmpty(TagSuffix) ? string.Empty : Separator + TagSuffix;

            // Snapshot the counter before allocation so a later TAG1-write failure
            // can roll it back (AssignNext leaves the counter at the allocated value
            // on success; on its own failure it has already rolled back).
            int seqPreAlloc = sequenceCounters.TryGetValue(seqKey, out int _preAlloc) ? _preAlloc : 0;

            int seqPad = SeqPadWidth > 0 ? SeqPadWidth : NumPad;
            SeqResult seqRes = SeqAssigner.AssignNext(
                seqKey, sequenceCounters, tagBody, tagSuffix,
                CurrentSeqScheme, seqPad, seqSchemeContext,
                MaxCollisionDepth, existingTags);

            if (!seqRes.Success)
            {
                string why = seqRes.Failure switch
                {
                    SeqFailureReason.InitialOverflow =>
                        $"SEQ overflow: group {seqKey} exceeded pad-{seqPad} capacity — skipping element {el.Id}",
                    SeqFailureReason.CollisionOverflow =>
                        $"SEQ overflow in collision loop: group {seqKey} exceeded pad-{seqPad} capacity — skipping element {el.Id}",
                    SeqFailureReason.SafetyExhausted =>
                        $"Collision safety limit ({MaxCollisionDepth}) exhausted for group {seqKey} — element {el.Id} skipped to prevent a duplicate tag",
                    _ => $"SEQ assignment failed for element {el.Id}",
                };
                if (seqRes.Failure == SeqFailureReason.SafetyExhausted) StingLog.Error(why);
                else StingLog.Warn(why);
                stats?.RecordWarning(why);
                return false; // AssignNext already rolled the counter back
            }

            string seq = seqRes.Seq;
            string tag = seqRes.Tag;
            if (seqRes.CollisionCount > 0)
                stats?.RecordCollision(tag, seqRes.CollisionCount);

            // Remove the element's old tag from the collision index (it's being
            // replaced) so stale entries don't trigger false collisions for other
            // elements. The final written tag is added back after the TAG1 write.
            if (existingTags != null && !string.IsNullOrEmpty(existingTag) && existingTag != tag)
                existingTags.Remove(existingTag);

            // F-03: Track whether we already have a fresh ReadTokenValues result from the non-overwrite branch
            string[] _cachedReadTokens = null;

            if (overwriteTokens)
            {
                ParameterHelpers.SetString(el, ParamRegistry.DISC, disc, overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.LOC, loc, overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.ZONE, zone, overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.LVL, lvl, overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.SYS, sys, overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.FUNC, func, overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.PROD, prod, overwrite: true);
                ParameterHelpers.SetString(el, ParamRegistry.SEQ, seq, overwrite: true);

                // we just wrote 8 known values — populate
                // _cachedReadTokens from them so the container-write path below
                // and the caller's tokenValuesOut don't trigger a fresh
                // ReadTokenValues that would just read what we wrote.
                _cachedReadTokens = new[] { disc, loc, zone, lvl, sys, func, prod, seq };
            }
            else
            {
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISC, disc);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.LOC, loc);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.ZONE, zone);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.LVL, lvl);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.SYS, sys);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.FUNC, func);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.PROD, prod);
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.SEQ, seq);

                // Re-read actual stored token values to ensure TAG1 reflects
                // what's on the element. Do NOT fill empty slots with derived defaults —
                // that would overwrite manually-set values that SetIfEmpty preserved.
                // The malformed-tag guard below blocks incomplete tags correctly.
                // F-03: Cache result so container write at line ~2808 can reuse without second read
                string[] actualTokens = ParamRegistry.ReadTokenValues(el);
                _cachedReadTokens = actualTokens;
                if (actualTokens.Length < 8)
                    return false;
                // Remove the derived-value tag from collision index (it may differ from actual)
                string removedTag = null;
                if (existingTags != null && !string.IsNullOrEmpty(tag))
                {
                    removedTag = tag;
                    existingTags.Remove(tag);
                }
                tag = string.Join(Separator, actualTokens);
                // Re-apply prefix/suffix to re-read tag
                if (!string.IsNullOrEmpty(TagPrefix)) tag = TagPrefix + Separator + tag;
                if (!string.IsNullOrEmpty(TagSuffix)) tag = tag + Separator + TagSuffix;
                // Update collision index with actual tag
                if (existingTags != null)
                    existingTags.Add(tag);
                // Also update the SEQ key variables to reflect actual stored values
                // so collision detection uses the right tag string
                disc = actualTokens[0];
                loc = actualTokens[1];
                zone = actualTokens[2];
                lvl = actualTokens[3];
                sys = actualTokens[4];
                func = actualTokens[5];
                prod = actualTokens[6];
                seq = actualTokens[7];
            }

            // segment-count validation only runs on the
            // SetIfEmpty path where the actual stored tokens may legitimately
            // differ from the freshly-derived ones (e.g. user manually edited
            // ASS_DISCIPLINE_COD_TXT). On the overwrite path we just built the
            // tag from 8 known non-empty tokens via string.Join with a fixed
            // separator, so the segment count is statically 8 and the check is
            // dead work. Skip it.
            if (!overwriteTokens)
            {
                // Validate segment count by counting separators instead of allocating split array.
                // Phase 86b: Use full separator string (not Separator[0] char) for multi-char separator support.
                int sepCount = 0;
                string sepStr = !string.IsNullOrEmpty(Separator) ? Separator : "-";
                int sIdx = 0;
                while ((sIdx = tag.IndexOf(sepStr, sIdx, StringComparison.Ordinal)) >= 0)
                {
                    sepCount++;
                    sIdx += sepStr.Length;
                }
                if (sepCount < 7) // 8 segments = 7 separators
                {
                    StingLog.Warn($"Malformed tag for element {el.Id}: '{tag}' has {sepCount + 1} segments (expected 8)");
                    stats?.RecordWarning($"Element {el.Id}: malformed tag with {sepCount + 1} segments — skipped");
                    return false;
                }
            }
            bool tagWriteSucceeded = ParameterHelpers.SetString(el, ParamRegistry.TAG1, tag, overwrite: true);

            // SEQ counter fix: rollback increment if TAG1 write failed
            if (!tagWriteSucceeded)
            {
                sequenceCounters[seqKey] = seqPreAlloc;
                StingLog.Warn($"TAG1 write failed on {el.Id} — SEQ counter rolled back for key '{seqKey}'");
                stats?.RecordWarning($"Element {el.Id}: TAG1 write failed — SEQ rolled back");
                return false;
            }

            // 5.3: Re-read TAG1 to catch write failures and add to existingTags
            // to prevent same-batch duplicates even when existingTags was null at entry
            {
                string writtenTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(writtenTag) && writtenTag != tag)
                    StingLog.Warn($"TAG1 write mismatch on {el.Id}: wrote '{tag}', read back '{writtenTag}'");
                // Ensure the tag is in the index for same-batch duplicate prevention
                if (existingTags != null && !string.IsNullOrEmpty(writtenTag))
                    existingTags.Add(writtenTag);
            }

            // Auto-populate STATUS from Revit phase/workset if not already set
            // Guaranteed default: every element gets a STATUS — never left empty
            {
                string existingStatus = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                if (string.IsNullOrEmpty(existingStatus) || overwriteTokens)
                {
                    // PERF-003 FIX: Use cached phase list when available to avoid per-element FilteredElementCollector
                    string status = (cachedPhases != null && lastPhaseId != null)
                        ? PhaseAutoDetect.DetectStatusCached(doc, el, cachedPhases, lastPhaseId)
                        : PhaseAutoDetect.DetectStatus(doc, el);
                    if (string.IsNullOrEmpty(status)) status = "NEW";
                    if (overwriteTokens)
                        ParameterHelpers.SetString(el, ParamRegistry.STATUS, status, overwrite: true);
                    else
                        ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, status);
                }
            }

            // Auto-populate REV from project revision sequence
            // Guaranteed default: every element gets a REV — "P01" when no revisions exist
            // Uses cachedRev when provided to avoid O(n) collector per element
            {
                string existingRev = ParameterHelpers.GetString(el, ParamRegistry.REV);
                if (string.IsNullOrEmpty(existingRev) || overwriteTokens)
                {
                    string rev = cachedRev ?? PhaseAutoDetect.DetectProjectRevision(doc);
                    if (string.IsNullOrEmpty(rev)) rev = "P01";
                    if (overwriteTokens)
                        ParameterHelpers.SetString(el, ParamRegistry.REV, rev, overwrite: true);
                    else
                        ParameterHelpers.SetIfEmpty(el, ParamRegistry.REV, rev);
                }
            }

            // Auto-write containers: populate discipline-specific and universal containers
            // from the token values just written. This eliminates the need for a separate
            // "Combine" step after tagging — tags are immediately available in all containers.
            // Always write containers — even partial token values should propagate.
            try
            {
                // F-03: Reuse cached token read from non-overwrite branch; only re-read for overwrite path
                string[] tokenVals = _cachedReadTokens ?? ParamRegistry.ReadTokenValues(el);
                // Validate token array before container write
                for (int i = 0; i < tokenVals.Length; i++)
                {
                    if (tokenVals[i] == null) tokenVals[i] = "";
                }
                ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: overwriteTokens);

                // hand the freshly-built token array back to
                // the caller so RunFullPipeline doesn't have to do its own
                // ReadTokenValues a second time after we return.
                if (tokenValuesOut != null && tokenValuesOut.Length >= 8)
                {
                    int copyLen = Math.Min(tokenValuesOut.Length, tokenVals.Length);
                    for (int i = 0; i < copyLen; i++) tokenValuesOut[i] = tokenVals[i];
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Container write failed for {el.Id}: {ex.Message}");
                stats?.RecordWarning($"Element {el.Id}: container write failed — {ex.Message}");
            }

            // ── Auto-initialize display BOOLs (v5.6) ─────────────────────────
            // Ensure tag families show content immediately after tagging by setting
            // default visibility parameters. Without this, tag families using
            // paragraph depth or style matrix BOOLs would show blank labels.
            // Uses SetYesNo to handle YESNO (StorageType.Integer) parameters correctly.
            //
            // the display-mode sentinel is only empty on a
            // first-ever tag; once it has any value the 13 init writes below are
            // all overwriting current state with their default values, wasting a
            // LookupParameter per call. Skip the block when STING_DISPLAY_MODE is
            // already populated (sentinel covers the whole init group).
            string displayModeSentinel = ParameterHelpers.GetString(el, ParamRegistry.DISPLAY_MODE);
            if (!string.IsNullOrEmpty(displayModeSentinel))
            {
                stats?.RecordTagged(catName, disc, sys, lvl);
                return true;
            }
            try
            {
                // LOG-08 FIX: Initialize DISPLAY_MODE so tag families show the correct
                // display variant immediately (default = PROD-SEQ mode 2)
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.DISPLAY_MODE, ParamRegistry.DisplayModeDefault.ToString());

                // honour the Tokens & Depth paragraph-depth slider.
                // When the user has pushed a ParaDepth value from the sub-tab we
                // overwrite all 10 PARA_STATE BOOLs so tiers 1..N are enabled and
                // tiers N+1..10 are disabled. When the slider hasn't been touched
                // we keep the historic behaviour of only seeding PARA_STATE_1 to
                // avoid stomping manual tier selections.
                int paraDepth = 0;
                bool preserveParaState = false;
                try
                {
                    string pd = StingTools.UI.StingCommandHandler.GetExtraParam("ParaDepth");
                    if (!string.IsNullOrEmpty(pd) && int.TryParse(pd, out int v) && v >= 1 && v <= 10)
                        paraDepth = v;

                    // Review fix for token-depth issue #2: when the user has
                    // explicitly run SetParagraphDepthCommand the resulting
                    // type-level state must not be clobbered by every Auto-Tag
                    // pass. The ExtraParam below is set by that command and
                    // makes WriteTag7All only seed PARA_STATE_1 when depth has
                    // never been written.
                    string ps = StingTools.UI.StingCommandHandler.GetExtraParam("PreserveParaState");
                    if (!string.IsNullOrEmpty(ps) &&
                        (ps.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                         ps.Equals("true", StringComparison.OrdinalIgnoreCase)))
                        preserveParaState = true;
                }
                catch { /* ignore — use default */ }

                // Discipline-default fallback (review fix for token-depth #3):
                // when no slider value is set, prefer the active discipline's
                // configured DefaultParagraphDepth before defaulting to depth=1.
                if (paraDepth == 0 && doc != null)
                {
                    try
                    {
                        string elDisc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                        if (!string.IsNullOrEmpty(elDisc))
                        {
                            var profile = GetDisciplineProfile(elDisc);
                            if (profile?.DefaultParagraphDepth.HasValue == true)
                                paraDepth = profile.DefaultParagraphDepth.Value;
                        }
                    }
                    catch { /* discipline-default resolution is best-effort */ }
                }

                if (preserveParaState)
                {
                    // Honour any existing PARA_STATE_1 setting; only seed if the
                    // element has never been touched (all states empty).
                    bool anySet = false;
                    foreach (var pn in ParamRegistry.AllParaStates)
                    {
                        Parameter pp = el.LookupParameter(pn);
                        if (pp == null) continue;
                        if (pp.StorageType == StorageType.Integer && pp.AsInteger() != 0) { anySet = true; break; }
                        if (pp.StorageType == StorageType.String &&
                            !string.IsNullOrEmpty(pp.AsString())) { anySet = true; break; }
                    }
                    if (!anySet)
                        ParameterHelpers.SetYesNo(el, ParamRegistry.PARA_STATE_1, true);
                }
                else if (paraDepth >= 1)
                {
                    string[] paraStates = ParamRegistry.AllParaStates;
                    for (int i = 0; i < paraStates.Length; i++)
                        ParameterHelpers.SetYesNo(el, paraStates[i], i < paraDepth, overwrite: true);
                }
                else
                {
                    // TAG_PARA_STATE_1_BOOL = Yes (compact mode default — ensures at least
                    // Tier 1 content is visible in tag families after tagging)
                    ParameterHelpers.SetYesNo(el, ParamRegistry.PARA_STATE_1, true);
                }

                // TAG_WARN_VISIBLE_BOOL = No (default off — prevents expensive per-element
                // warning evaluation on every WriteTag7All call for large models)
                ParameterHelpers.SetYesNo(el, ParamRegistry.WARN_VISIBLE, false);

                // TAG_7_SECTION_VISIBLE_A-F and default tag style: resolve the active
                // ViewStylePack once so both features share the same lookup overhead.
                bool tag7Visible = true;
                string resolvedStyleCode = null;   // null → fall back to hard-coded default
                try
                {
                    if (doc?.ActiveView != null)
                    {
                        string dtId = DrawingTypeStamper.Read(doc.ActiveView);
                        if (!string.IsNullOrEmpty(dtId))
                        {
                            var dt = DrawingTypeRegistry.Get(doc, dtId);
                            if (!string.IsNullOrEmpty(dt?.ViewStylePackId))
                            {
                                var activePack = DrawingTypeRegistry.TryGetPack(doc, dt.ViewStylePackId);
                                if (activePack != null)
                                {
                                    // TAG7 section visibility per category.
                                    if (activePack.CategoryTag7Sections != null &&
                                        activePack.CategoryTag7Sections.TryGetValue(catName, out bool sectFlag))
                                        tag7Visible = sectFlag;

                                    // Tag style: per-category first, then pack default.
                                    if (activePack.CategoryTagStyles != null &&
                                        activePack.CategoryTagStyles.TryGetValue(catName, out var catStyle) &&
                                        !string.IsNullOrEmpty(catStyle))
                                        resolvedStyleCode = catStyle;
                                    else if (!string.IsNullOrEmpty(activePack.DefaultTagStyle))
                                        resolvedStyleCode = activePack.DefaultTagStyle;
                                }
                            }
                        }
                    }
                }
                catch { /* pack resolution is best-effort; fall back to defaults */ }

                ParameterHelpers.SetYesNo(el, "TAG_7_SECTION_VISIBLE_A_BOOL", tag7Visible);
                ParameterHelpers.SetYesNo(el, "TAG_7_SECTION_VISIBLE_B_BOOL", tag7Visible);
                ParameterHelpers.SetYesNo(el, "TAG_7_SECTION_VISIBLE_C_BOOL", tag7Visible);
                ParameterHelpers.SetYesNo(el, "TAG_7_SECTION_VISIBLE_D_BOOL", tag7Visible);
                ParameterHelpers.SetYesNo(el, "TAG_7_SECTION_VISIBLE_E_BOOL", tag7Visible);
                ParameterHelpers.SetYesNo(el, "TAG_7_SECTION_VISIBLE_F_BOOL", tag7Visible);

                // Default tag style: pack-resolved style code wins; fall back to 2.5mm Normal Black.
                // Mirrors TagStyleEngine.ApplyStyleCode without crossing the internal-class boundary.
                if (!string.IsNullOrEmpty(resolvedStyleCode))
                {
                    try
                    {
                        ParameterHelpers.SetString(el, ParamRegistry.TAG_STYLE_CODE, resolvedStyleCode, overwrite: true);
                        string activeStyleParam = $"TAG_{resolvedStyleCode}_BOOL";
                        foreach (string sp in ParamRegistry.AllTagStyleParams)
                        {
                            var p = el.LookupParameter(sp);
                            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) continue;
                            p.Set(string.Equals(sp, activeStyleParam, StringComparison.Ordinal) ? 1 : 0);
                        }
                    }
                    catch { ParameterHelpers.SetYesNo(el, "TAG_2.5NOM_BLACK_BOOL", true); }
                }
                else
                {
                    ParameterHelpers.SetYesNo(el, "TAG_2.5NOM_BLACK_BOOL", true);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Display BOOL init on {el.Id}: {ex.Message}"); }

            stats?.RecordTagged(catName, disc, sys, lvl);
            return true;
        }

        /// <summary>
        /// Build a display variant of the ISO tag based on display mode:
        ///   1 = SEQ only            (e.g. "0042")
        ///   2 = PROD-SEQ            (e.g. "AHU-0042")
        ///   3 = DISC-SYS-SEQ        (e.g. "M-HVAC-0042")
        ///   4 = DISC-PROD-SEQ       (e.g. "M-AHU-0042")
        ///   5 = Full 8-segment      (default — current behaviour)
        ///   6 = TAG7 plain narrative (Phase 165 — client-facing prose
        ///       e.g. "AHU-01 — primary supply unit serving Level 02. Located
        ///       in plant room PR-02. Status: NEW.")
        /// Returns the full tag if mode is unrecognised.
        /// </summary>
        public static string BuildDisplayTag(Element el, int mode)
        {
            if (el == null) return "";

            // Phase 165 — mode 6 reads the rich TAG7 narrative directly.
            // The narrative is composed by WriteTag7All; if empty (element
            // hasn't been tagged yet) we fall through to the technical tag
            // so the display never goes blank on a partially-tagged model.
            if (mode == 6)
            {
                string narrative = ParameterHelpers.GetString(el, ParamRegistry.TAG7);
                if (!string.IsNullOrEmpty(narrative)) return narrative;
                // Fallback: best plain-language hint we can build right now.
                // Throttled log so a partially-tagged model surfaces the
                // missing-narrative state instead of pretending mode-4 is by
                // design — see review TAG-token-toggling issue #2.
                StingLog.Warn($"BuildDisplayTag: mode 6 requested on element {el.Id} but ASS_TAG_7_TXT is empty; falling back to mode 4. Run Auto Tag / WriteTag7All to populate the narrative.");
                mode = 4; // DISC-PROD-SEQ — most readable compact form
            }

            string[] tokens = ParamRegistry.ReadTokenValues(el);
            if (tokens == null || tokens.Length < 8) return "";

            string sep = ParamRegistry.Separator;
            switch (mode)
            {
                case 1: return tokens[7]; // SEQ only
                case 2: return $"{tokens[6]}{sep}{tokens[7]}"; // PROD-SEQ
                case 3: return $"{tokens[0]}{sep}{tokens[4]}{sep}{tokens[7]}"; // DISC-SYS-SEQ
                case 4: return $"{tokens[0]}{sep}{tokens[6]}{sep}{tokens[7]}"; // DISC-PROD-SEQ
                case 5:
                {
                    string full = string.Join(sep, tokens);
                    if (!string.IsNullOrEmpty(TagPrefix)) full = TagPrefix + sep + full;
                    if (!string.IsNullOrEmpty(TagSuffix)) full = full + sep + TagSuffix;
                    return full;
                }
                default:
                {
                    string full = string.Join(sep, tokens);
                    if (!string.IsNullOrEmpty(TagPrefix)) full = TagPrefix + sep + full;
                    if (!string.IsNullOrEmpty(TagSuffix)) full = full + sep + TagSuffix;
                    return full;
                }
            }
        }

        /// <summary>
        /// Reads STING_DISPLAY_MODE from the element (int parameter) and builds the
        /// appropriate display tag variant. Writes the result to ASS_DISPLAY_TXT.
        /// Modes: 0/5=full 8-segment, 1=SEQ only, 2=PROD-SEQ, 3=DISC-SYS-SEQ, 4=DISC-PROD-SEQ.
        /// Returns the display string (empty if element is null or has no tokens).
        /// Uses ParamRegistry.DisplayModeDefault for unset parameters.
        /// </summary>
        public static string BuildDisplayTag(Element el)
        {
            if (el == null) return "";
            int mode = ParameterHelpers.GetInt(el, ParamRegistry.DISPLAY_MODE, 0);
            // Mode 0 means unset — use the configurable default from ParamRegistry
            if (mode == 0) mode = ParamRegistry.DisplayModeDefault;
            string display = BuildDisplayTag(el, mode);

            // Token-mask precedence (highest first):
            //   1. STING_VIEW_TOKEN_MASK_TXT on the active view — user-set
            //      "hide ZONE in this view" without mutating ASS_TAG_1_TXT
            //      (review fix for TAG-token-toggling #1).
            //   2. TAG_SEG_MASK_TXT on the element — written by
            //      TokenProfileApplier step 7.5.
            //   3. UI ExtraParam "TokenMask" — ad-hoc preview override.
            // Mask now applies in modes 1-5/0 (was 5/0 only). Modes that
            // already drop segments by design just no-op when the mask
            // matches, so layered masks stay safe.
            try
            {
                string mask = null;
                try
                {
                    var doc = el.Document;
                    var view = doc?.ActiveView;
                    if (view != null)
                        mask = ParameterHelpers.GetString(view, ParamRegistry.VIEW_TOKEN_MASK);
                }
                catch { /* view lookup is best-effort */ }

                if (string.IsNullOrEmpty(mask))
                    mask = ParameterHelpers.GetString(el, ParamRegistry.TAG_SEG_MASK);
                if (string.IsNullOrEmpty(mask))
                    mask = StingTools.UI.StingCommandHandler.GetExtraParam("TokenMask");

                if (!string.IsNullOrEmpty(mask) && mask.Length >= 8 && mask != "11111111"
                    && (mode == 0 || mode == 5))
                {
                    display = ApplySegmentMask(display, mask);
                }
            }
            catch { /* mask is an optional UX hint — ignore failures */ }
            if (!string.IsNullOrEmpty(display))
            {
                try
                {
                    ParameterHelpers.SetString(el, ParamRegistry.DISPLAY_TXT, display, overwrite: true);
                }
                catch (Exception ex) { StingLog.Warn($"ASS_DISPLAY_TXT param may not be bound: {ex.Message}"); }
            }
            return display;
        }

        /// <summary>
        /// MEP system-aware SYS code derivation. Checks if the element belongs to a
        /// connected MEP system (e.g., "Supply Air", "Domestic Hot Water") and uses
        /// that for more accurate SYS code assignment. Falls back to category lookup.
        ///
        /// Intelligence layers (evaluated in order):
        ///   1. FamilyInstance.MEPModel connector system name (piping + ductwork)
        ///   2. Duct/Pipe system type parameter (RBS_DUCT_SYSTEM_TYPE, RBS_PIPING_SYSTEM_TYPE)
        ///   3. Electrical circuit panel name analysis
        ///   4. Family name pattern matching (e.g., "Exhaust Fan" → HVAC)
        ///   5. Room-type inference (Server Room → ICT, Kitchen → SAN)
        ///   6. Category-based fallback via SysMap
        /// </summary>
        public static string GetMepSystemAwareSysCode(Element el, string categoryName)
        {
            return GetMepSystemAwareSysCodeWithLayer(el, categoryName).Item1;
        }

        /// <summary>
        /// LOG-01: Returns (sysCode, detectionLayer) where layer indicates which
        /// detection method succeeded: 1=Connector, 2=SystemType, 3=Circuit,
        /// 4=FamilyName, 5=RoomType, 6=CategoryFallback.
        /// </summary>
        public static (string, int) GetMepSystemAwareSysCodeWithLayer(Element el, string categoryName)
        {
            // Layer 1: Connected MEP system name (most reliable for piping and ductwork)
            string fromConnector = GetSysFromConnector(el, categoryName);
            if (!string.IsNullOrEmpty(fromConnector)) return (fromConnector, 1);

            // Layer 2: Duct/Pipe system type built-in parameter
            string fromSysType = GetSysFromSystemTypeParam(el, categoryName);
            if (!string.IsNullOrEmpty(fromSysType)) return (fromSysType, 2);

            // Layer 3: Electrical circuit panel name
            string fromCircuit = GetSysFromElectricalCircuit(el);
            if (!string.IsNullOrEmpty(fromCircuit)) return (fromCircuit, 3);

            // Layer 4: Family name pattern matching
            string fromFamily = GetSysFromFamilyName(el, categoryName);
            if (!string.IsNullOrEmpty(fromFamily)) return (fromFamily, 4);

            // Layer 5: Room-type inference
            string fromRoom = GetSysFromRoomType(el);
            if (!string.IsNullOrEmpty(fromRoom)) return (fromRoom, 5);

            // Layer 6: Category-based fallback
            return (GetSysCode(categoryName), 6);
        }

        /// <summary>Layer 1: Read connected MEP system name via connectors.</summary>
        private static string GetSysFromConnector(Element el, string categoryName = null)
        {
            try
            {
                FamilyInstance fi2 = el as FamilyInstance;
                if (fi2?.MEPModel?.ConnectorManager != null)
                {
                    foreach (Connector conn in fi2.MEPModel.ConnectorManager.Connectors)
                    {
                        if (conn.MEPSystem != null)
                        {
                            string sysName = conn.MEPSystem.Name?.ToUpperInvariant() ?? "";
                            string mapped = MapSystemNameToCode(sysName, categoryName);
                            if (!string.IsNullOrEmpty(mapped)) return mapped;
                        }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SYS detection from connector failed: {ex.Message}"); }
            return null;
        }

        /// <summary>Layer 2: Read RBS_DUCT_SYSTEM_TYPE or RBS_PIPING_SYSTEM_TYPE parameter.</summary>
        private static string GetSysFromSystemTypeParam(Element el, string categoryName = null)
        {
            try
            {
                // Duct system type (Supply Air, Return Air, Exhaust Air)
                Parameter ductSys = el.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                if (ductSys != null && ductSys.HasValue)
                {
                    string val = ductSys.AsValueString()?.ToUpperInvariant() ?? "";
                    if (val.Contains("SUPPLY")) return "HVAC";
                    if (val.Contains("RETURN")) return "HVAC";
                    if (val.Contains("EXHAUST")) return "HVAC";
                }

                // Piping system type
                Parameter pipeSys = el.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                if (pipeSys != null && pipeSys.HasValue)
                {
                    string val = pipeSys.AsValueString()?.ToUpperInvariant() ?? "";
                    string mapped = MapSystemNameToCode(val, categoryName);
                    if (!string.IsNullOrEmpty(mapped)) return mapped;
                }
            }
            catch (Exception ex) { StingLog.Warn($"SYS detection from system type param failed: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Layer 3: Infer SYS from electrical circuit panel name.
        /// If element is connected to an electrical circuit, read the panel name
        /// for subsystem classification (e.g., "LP-1" → LV, "EDB-01" → LV,
        /// "UPS-DB-01" → LV with UPS hint, "FIRE ALARM PANEL" → FLS).
        /// </summary>
        private static string GetSysFromElectricalCircuit(Element el)
        {
            try
            {
                // Check for circuit-related parameters
                Parameter circuitNum = el.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER);
                Parameter circuitPanel = el.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_PANEL_PARAM);

                if (circuitPanel != null && circuitPanel.HasValue)
                {
                    string panel = circuitPanel.AsString()?.ToUpperInvariant() ?? "";
                    if (panel.Contains("FIRE") || panel.Contains("FA-") || panel.Contains("FAP"))
                        return "FLS";
                    if (panel.Contains("SECURITY") || panel.Contains("CCTV") || panel.Contains("ACCESS"))
                        return "SEC";
                    if (panel.Contains("DATA") || panel.Contains("ICT") || panel.Contains("SERVER"))
                        return "ICT";
                    if (panel.Contains("COMMS") || panel.Contains("TELECOM"))
                        return "COM";
                    if (panel.Contains("UPS"))
                        return "LV";
                    // F2: Plumbing/HVAC panels connected to electrical circuits
                    if (panel.Contains("SAN") || panel.Contains("SEWAGE") || panel.Contains("DRAIN")) return "SAN";
                    if (panel.Contains("DHW") || panel.Contains("HWS") || panel.Contains("HOT WATER")) return "HWS";
                    if (panel.Contains("DCW") || panel.Contains("COLD WATER") || panel.Contains("MAINS")) return "DCW";
                    if (panel.Contains("GAS")) return "GAS";
                    if (panel.Contains("HVAC") || panel.Contains("AHU") || panel.Contains("FCU")) return "HVAC";
                    // Default electrical panels → LV
                    if (panel.Length > 0)
                        return "LV";
                }
            }
            catch (Exception ex) { StingLog.Warn($"SYS detection from electrical circuit failed: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Layer 4: Infer SYS from family name patterns.
        /// Catches equipment that isn't connected to a system yet (during early design).
        /// </summary>
        private static string GetSysFromFamilyName(Element el, string categoryName)
        {
            string familyName = ParameterHelpers.GetFamilyName(el);
            if (string.IsNullOrEmpty(familyName)) return null;
            string upper = familyName.ToUpperInvariant();

            // Phase 176 — Lightning Protection System pattern (BS EN 62305).
            // Match BEFORE LV / electrical patterns so an LPS finial in
            // Electrical Equipment doesn't get tagged as LV.
            if (upper.Contains("LPS") || upper.Contains("LIGHTNING") ||
                upper.Contains("AIR TERMINAL") || upper.Contains("FINIAL") ||
                upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                upper.Contains("EARTH ROD") || upper.Contains("EARTH ELECTRODE") ||
                upper.Contains("RING EARTH") || upper.Contains("FOUNDATION EARTH") ||
                upper.Contains("MESH EARTH") || upper.Contains("EARTH MESH") ||
                upper.Contains("EQUIPOTENTIAL") || upper.Contains("BONDING BAR") ||
                upper.Contains("EARTH BAR") || upper.Contains("TEST CLAMP") ||
                upper.Contains("INSPECTION POINT") || upper.Contains("SPARK GAP") ||
                ((upper.Contains("SPD") || upper.Contains("SURGE PROTECT")) && upper.Contains("LIGHTNING")))
                return "LPS";

            // HVAC equipment patterns
            if (upper.Contains("AHU") || upper.Contains("AIR HANDLING") ||
                upper.Contains("FCU") || upper.Contains("FAN COIL") ||
                upper.Contains("VAV") || upper.Contains("VARIABLE AIR") ||
                upper.Contains("EXHAUST FAN") || upper.Contains("EXTRACT FAN") ||
                upper.Contains("HRU") || upper.Contains("HEAT RECOVERY") ||
                upper.Contains("SPLIT") || upper.Contains("CASSETTE") ||
                upper.Contains("CHILLER") || upper.Contains("COOLING TOWER") ||
                upper.Contains("GRILLE") || upper.Contains("DIFFUSER"))
                return "HVAC";

            // Heating equipment
            if (upper.Contains("BOILER") || upper.Contains("RADIATOR") ||
                upper.Contains("UNDERFLOOR HEAT") || upper.Contains("CALORIFIER") ||
                upper.Contains("HEAT EXCHANGER"))
                return "HWS";

            // Plumbing-specific equipment
            if (upper.Contains("PUMP") && (categoryName == "Plumbing Fixtures" ||
                upper.Contains("SUMP") || upper.Contains("SEWAGE") || upper.Contains("BOOSTER")))
                return "DCW";

            // Fire protection
            if (upper.Contains("SPRINKLER") || upper.Contains("FIRE HOSE") ||
                upper.Contains("HYDRANT") || upper.Contains("DELUGE") ||
                upper.Contains("SUPPRESSION"))
                return "FP";

            // Fire alarm
            if (upper.Contains("SMOKE") || upper.Contains("DETECTOR") ||
                upper.Contains("CALL POINT") || upper.Contains("SOUNDER") ||
                upper.Contains("BEACON") || upper.Contains("FIRE ALARM"))
                return "FLS";

            // Security
            if (upper.Contains("CCTV") || upper.Contains("CAMERA") ||
                upper.Contains("ACCESS CONTROL") || upper.Contains("CARD READER") ||
                upper.Contains("INTERCOM"))
                return "SEC";

            // ICT/Data
            if (upper.Contains("DATA OUTLET") || upper.Contains("NETWORK") ||
                upper.Contains("SERVER RACK") || upper.Contains("PATCH PANEL") ||
                upper.Contains("WIFI") || upper.Contains("ACCESS POINT"))
                return "ICT";

            return null;
        }

        /// <summary>
        /// Layer 5: Infer SYS from the room type the element is in.
        /// Room name/department patterns suggest the system context.
        /// Only applied when no other layer produced a result.
        /// </summary>
        private static string GetSysFromRoomType(Element el)
        {
            try
            {
                Document doc = el.Document;
                Room room = ParameterHelpers.GetRoomAtElement(doc, el);
                if (room == null) return null;

                string roomName = (room.Name ?? "").ToUpperInvariant();
                string dept = "";
                try
                {
                    Parameter deptParam = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
                    if (deptParam != null) dept = (deptParam.AsString() ?? "").ToUpperInvariant();
                }
                catch (Exception ex) { StingLog.Warn($"Room department read failed: {ex.Message}"); }

                string combined = $"{roomName} {dept}";
                string catUpper = (el.Category?.Name ?? "").ToUpperInvariant();

                // Server/comms rooms → ICT for generic devices
                if (combined.Contains("SERVER") || combined.Contains("COMMS") ||
                    combined.Contains("DATA CENTRE") || combined.Contains("DATA CENTER") ||
                    combined.Contains("TELECOM") || combined.Contains("SWITCH ROOM") ||
                    combined.Contains("SWITCHROOM") || combined.Contains("COMMS ROOM"))
                {
                    if (catUpper.Contains("GENERIC") || catUpper.Contains("DATA") ||
                        catUpper.Contains("COMMUNICATION"))
                        return "ICT";
                }

                // Plant rooms → HVAC or DCW depending on equipment category
                if (combined.Contains("PLANT ROOM") || combined.Contains("MECHANICAL ROOM") ||
                    combined.Contains("BOILER ROOM") || combined.Contains("AHU ROOM"))
                {
                    // Don't override — plant rooms have mixed systems
                }

                // Electrical rooms → LV
                if (combined.Contains("ELECTRICAL") || combined.Contains("SUBSTATION") ||
                    combined.Contains("TRANSFORMER") || combined.Contains("METER ROOM") ||
                    combined.Contains("DB ROOM") || combined.Contains("DISTRIBUTION"))
                {
                    if (catUpper.Contains("ELECTRICAL") || catUpper.Contains("GENERIC") ||
                        catUpper.Contains("LIGHTING"))
                        return "LV";
                }

                // Fire protection rooms → FP
                if (combined.Contains("FIRE PUMP") || combined.Contains("SPRINKLER") ||
                    combined.Contains("FIRE RISER"))
                {
                    if (catUpper.Contains("GENERIC") || catUpper.Contains("PIPE") ||
                        catUpper.Contains("SPRINKLER") || catUpper.Contains("FIRE"))
                        return "FP";
                }

                // Gas rooms → GAS
                if (combined.Contains("GAS ROOM") || combined.Contains("GAS RISER") ||
                    combined.Contains("GAS METER"))
                {
                    if (catUpper.Contains("PIPE") || catUpper.Contains("GENERIC"))
                        return "GAS";
                }

                // Water tank / pump rooms → DCW
                if (combined.Contains("WATER TANK") || combined.Contains("PUMP ROOM") ||
                    combined.Contains("COLD WATER") || combined.Contains("TANK ROOM"))
                {
                    if (catUpper.Contains("PIPE") || catUpper.Contains("PLUMBING") ||
                        catUpper.Contains("GENERIC") || catUpper.Contains("MECHANICAL"))
                        return "DCW";
                }

                // Bathrooms / toilets → SAN for plumbing fixtures
                if (combined.Contains("BATHROOM") || combined.Contains("TOILET") ||
                    combined.Contains("WC") || combined.Contains("WASHROOM") ||
                    combined.Contains("SHOWER") || combined.Contains("ENSUITE"))
                {
                    if (catUpper.Contains("PLUMBING") || catUpper.Contains("GENERIC"))
                        return "SAN";
                }

                // Kitchen → SAN for plumbing, GAS for gas-related
                if (combined.Contains("KITCHEN") || combined.Contains("KITCHENETTE"))
                {
                    if (catUpper.Contains("PLUMBING"))
                        return "SAN";
                    if (catUpper.Contains("PIPE") && catUpper.Contains("GAS"))
                        return "GAS";
                }

                // Security / CCTV rooms → SEC
                if (combined.Contains("SECURITY") || combined.Contains("CCTV") ||
                    combined.Contains("GUARD"))
                {
                    if (catUpper.Contains("GENERIC") || catUpper.Contains("SECURITY"))
                        return "SEC";
                }
            }
            catch (Exception ex) { StingLog.Warn($"SYS detection from room type failed: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// System-aware DISC correction. Pipes/pipe fittings are categorised as "M"
        /// (Mechanical) by default, but if the connected MEP system is plumbing
        /// (DCW, DHW, SAN, RWD, GAS), the DISC should be "P" (Plumbing).
        /// Similarly, fire protection pipes should be "FP".
        /// </summary>
        private static readonly HashSet<string> _pipeCategories = new HashSet<string>
        {
            "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes"
        };

        public static string GetSystemAwareDisc(string disc, string sys, string categoryName)
        {
            // Only apply system-aware override for ambiguous categories (pipes, pipe fittings, etc.)
            if (!_pipeCategories.Contains(categoryName))
                return disc;

            // Override DISC based on the detected system
            switch (sys)
            {
                case "DCW":
                case "DHW":
                case "SAN":
                case "RWD":
                case "GAS":
                case "HWS":
                    return "P";
                case "FP":
                    return "FP";
                case "HVAC":
                    return "M";
                default:
                    return disc; // Keep original mapping
            }
        }

        /// <summary>
        /// Map a system name string to a SYS code. Used by Layer 1 (connector) and Layer 2 (parameter).
        /// Centralised mapping for all MEP system naming conventions.
        /// </summary>
        private static string MapSystemNameToCode(string sysName, string categoryName = null)
        {
            if (string.IsNullOrEmpty(sysName)) return null;

            // HVAC systems — full names and Revit abbreviated system types
            if (sysName.Contains("SUPPLY AIR") || sysName.Contains("SUPPLY DUCT")) return "HVAC";
            if (sysName.Contains("RETURN AIR") || sysName.Contains("RETURN DUCT")) return "HVAC";
            if (sysName.Contains("EXHAUST") || sysName.Contains("EXTRACT")) return "HVAC";
            if (sysName.Contains("FRESH AIR") || sysName.Contains("OUTSIDE AIR")) return "HVAC";
            if (sysName.Contains("CHILLED") || sysName.Contains("COOLING")) return "HVAC";
            if (sysName.Contains("VENT") || sysName.Contains("VENTILATION")) return "HVAC";
            // Abbreviated HVAC system names (Revit defaults and common shorthand)
            if (sysName == "SA" || sysName.StartsWith("SA ") || sysName.Contains(" SA ")) return "HVAC";
            if (sysName == "RA" || sysName.StartsWith("RA ") || sysName.Contains(" RA ")) return "HVAC";
            if (sysName == "EA" || sysName.StartsWith("EA ") || sysName.Contains(" EA ")) return "HVAC";
            if (sysName == "OA" || sysName.StartsWith("OA ") || sysName.Contains(" OA ")) return "HVAC";
            if (sysName == "CHW" || sysName.StartsWith("CHW ") || sysName.Contains(" CHW ")) return "HVAC";
            // "CW" is ambiguous — Condenser Water (HVAC) vs Cold Water (Plumbing)
            // If the element is a pipe category, map to DCW; otherwise HVAC (Condenser Water)
            if (sysName == "CW" || sysName.StartsWith("CW ") || sysName.Contains(" CW "))
            {
                if (!string.IsNullOrEmpty(categoryName) &&
                    (categoryName == "Pipes" || categoryName == "Pipe Fittings" ||
                     categoryName == "Pipe Accessories" || categoryName == "Flex Pipes" ||
                     categoryName == "Plumbing Fixtures" || categoryName == "Plumbing Equipment"))
                    return "DCW";
                return "HVAC";
            }
            if (sysName == "FCU" || sysName.StartsWith("FCU ")) return "HVAC";

            // Heating / hot water systems
            if (sysName.Contains("HOT WATER") || sysName.Contains("DHW") || sysName.Contains("HWS")) return "HWS";
            if (sysName.Contains("HEATING") || sysName.Contains("LTHW") || sysName.Contains("MTHW")) return "HWS";
            if (sysName.Contains("RADIATOR") || sysName.Contains("UNDERFLOOR")) return "HWS";
            if (sysName.Contains("STEAM") || sysName.Contains("CONDENSATE")) return "HWS";
            // Abbreviated heating
            if (sysName == "LTHW" || sysName == "MTHW" || sysName == "HTHW") return "HWS";
            if (sysName == "HW" || sysName.StartsWith("HW ")) return "HWS";

            // Domestic cold water
            if (sysName.Contains("COLD WATER") || sysName.Contains("CWS") || sysName.Contains("DCW")) return "DCW";
            if (sysName.Contains("DOMESTIC COLD") || sysName.Contains("BOOSTED COLD")) return "DCW";
            if (sysName.Contains("MAINS WATER") || sysName.Contains("POTABLE")) return "DCW";

            // Fire protection
            if (sysName.Contains("FIRE") || sysName.Contains("SPRINKLER") || sysName.Contains("WET RISER")) return "FP";
            if (sysName.Contains("DRY RISER") || sysName.Contains("HYDRANT")) return "FP";

            // Sanitary / drainage
            if (sysName.Contains("SANITARY") || sysName.Contains("WASTE") || sysName.Contains("SOIL")) return "SAN";
            if (sysName.Contains("DRAIN") || sysName.Contains("SEWAGE") || sysName.Contains("FOUL")) return "SAN";
            // Abbreviated sanitary
            if (sysName == "SVP" || sysName == "WP" || sysName.StartsWith("SVP ") || sysName.StartsWith("WP ")) return "SAN";

            // Rainwater
            if (sysName.Contains("RAINWATER") || sysName.Contains("STORM") || sysName.Contains("SURFACE WATER")) return "RWD";
            if (sysName.Contains("ROOF DRAIN")) return "RWD";
            if (sysName == "RWP" || sysName.StartsWith("RWP ")) return "RWD";

            // Gas
            if (sysName.Contains("GAS") || sysName.Contains("NATURAL GAS") || sysName.Contains("LPG")) return "GAS";

            // Additional HVAC abbreviations (relief, balanced, thermal)
            if (sysName.Contains("RELIEF")) return "HVAC";
            if (sysName.Contains("BALANCED") && sysName.Contains("VENT")) return "HVAC";
            if (sysName == "UFH" || sysName.StartsWith("UFH ") || sysName.Contains("UNDERFLOOR HEAT")) return "HWS";
            if (sysName.Contains("THERMAL STORAGE") || sysName.Contains("BUFFER TANK")) return "HWS";
            if (sysName.Contains("SOLAR THERMAL") || sysName.Contains("SOLAR PANEL")) return "HWS";

            return null;
        }

        /// <summary>
        /// Determine which discipline codes are relevant for a given view.
        /// Inspects the view name, view template, and visible categories to build
        /// a set of discipline codes that should be tagged in this view.
        ///
        /// Intelligence layers:
        ///   1. View name pattern matching (e.g., "Mechanical" → M, "Electrical" → E)
        ///   2. View template name analysis for discipline hints
        ///   3. Category visibility inspection — if Mechanical Equipment is hidden,
        ///      the M discipline is excluded
        ///   4. View type: 3D coordination views → all disciplines
        ///
        /// Returns null if all disciplines should be included (no filtering).
        /// </summary>
        public static HashSet<string> GetViewRelevantDisciplines(View view)
        {
            if (view == null) return null;

            // Schedules are always all-discipline
            if (view.ViewType == ViewType.Schedule)
                return null;

            // 3D views: check name for discipline hints before defaulting to all
            if (view.ViewType == ViewType.ThreeD)
            {
                string name3d = (view.Name ?? "").ToUpperInvariant();
                // Discipline-specific 3D views (e.g., "3D - Mechanical", "HVAC 3D")
                var detected3d = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (name3d.Contains("MECHANICAL") || name3d.Contains("HVAC"))
                    detected3d.Add("M");
                if (name3d.Contains("ELECTRICAL") || name3d.Contains("LIGHTING"))
                    detected3d.Add("E");
                if (name3d.Contains("PLUMBING") || name3d.Contains("PUBLIC HEALTH"))
                    detected3d.Add("P");
                if (name3d.Contains("FIRE"))
                    detected3d.Add("FP");
                if (name3d.Contains("COORDINATION") || name3d.Contains("COMBINED") || name3d.Contains("MEP"))
                { detected3d.Add("M"); detected3d.Add("E"); detected3d.Add("P"); }
                // If no discipline detected in 3D view name, tag all
                return detected3d.Count > 0 ? detected3d : null;
            }

            string viewName = (view.Name ?? "").ToUpperInvariant();
            string templateName = "";
            try
            {
                if (view.ViewTemplateId != null && view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    View template = view.Document.GetElement(view.ViewTemplateId) as View;
                    if (template != null)
                        templateName = (template.Name ?? "").ToUpperInvariant();
                }
            }
            catch (Exception ex) { StingLog.Warn($"template lookup failed — proceed with view name: {ex.Message}"); }

            string combined = $"{viewName} {templateName}";

            // Check for specific discipline indicators in view/template name
            var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (combined.Contains("MECHANICAL") || combined.Contains("HVAC") ||
                combined.Contains("HEATING") || combined.Contains("VENTILATION"))
                detected.Add("M");
            if (combined.Contains("ELECTRICAL") || combined.Contains("POWER") ||
                combined.Contains("LIGHTING"))
                detected.Add("E");
            if (combined.Contains("PLUMBING") || combined.Contains("DRAINAGE") ||
                combined.Contains("SANITARY") || combined.Contains("PUBLIC HEALTH"))
                detected.Add("P");
            if (combined.Contains("ARCHITECT") || combined.Contains("GENERAL ARRANGEMENT"))
                detected.Add("A");
            if (combined.Contains("STRUCTURAL") || combined.Contains("STRUCTURE"))
                detected.Add("S");
            if (combined.Contains("FIRE") || combined.Contains("SPRINKLER"))
                detected.Add("FP");
            if (combined.Contains("LOW VOLTAGE") || combined.Contains("COMMS") ||
                combined.Contains("DATA") || combined.Contains("SECURITY"))
                detected.Add("LV");

            // If discipline was detected from name, also check for "coordination" keyword
            // which signals multi-discipline
            if (combined.Contains("COORDINATION") || combined.Contains("COMBINED") ||
                combined.Contains("ALL SERVICES") || combined.Contains("MEP"))
            {
                detected.Add("M");
                detected.Add("E");
                detected.Add("P");
            }

            // If no discipline was detected, check category visibility as fallback
            if (detected.Count == 0)
            {
                try
                {
                    Document doc = view.Document;
                    // Check key category visibility
                    if (IsCategoryVisible(view, BuiltInCategory.OST_MechanicalEquipment) ||
                        IsCategoryVisible(view, BuiltInCategory.OST_DuctCurves))
                        detected.Add("M");
                    if (IsCategoryVisible(view, BuiltInCategory.OST_ElectricalEquipment) ||
                        IsCategoryVisible(view, BuiltInCategory.OST_ElectricalFixtures) ||
                        IsCategoryVisible(view, BuiltInCategory.OST_LightingFixtures))
                        detected.Add("E");
                    if (IsCategoryVisible(view, BuiltInCategory.OST_PipeCurves) ||
                        IsCategoryVisible(view, BuiltInCategory.OST_PlumbingFixtures))
                        detected.Add("P");
                    if (IsCategoryVisible(view, BuiltInCategory.OST_Walls) ||
                        IsCategoryVisible(view, BuiltInCategory.OST_Doors))
                        detected.Add("A");
                    if (IsCategoryVisible(view, BuiltInCategory.OST_StructuralFraming) ||
                        IsCategoryVisible(view, BuiltInCategory.OST_StructuralColumns))
                        detected.Add("S");
                    if (IsCategoryVisible(view, BuiltInCategory.OST_Sprinklers) ||
                        IsCategoryVisible(view, BuiltInCategory.OST_FireAlarmDevices))
                        detected.Add("FP");
                }
                catch (Exception ex2) { StingLog.Warn($"Visibility check failed — return all: {ex2.Message}"); }
            }

            // If still no disciplines detected, tag everything
            return detected.Count > 0 ? detected : null;
        }

        /// <summary>Check if a BuiltInCategory is visible in the given view.</summary>
        private static bool IsCategoryVisible(View view, BuiltInCategory bic)
        {
            try
            {
                Category cat = view.Document.Settings.Categories.get_Item(bic);
                if (cat == null) return false;
                return view.GetCategoryHidden(cat.Id) == false;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return true; } // Assume visible if check fails
        }

        /// <summary>
        /// Filter elements to only those matching the relevant disciplines for a view.
        /// If relevantDisciplines is null, all elements pass through (no filtering).
        /// </summary>
        public static List<Element> FilterByViewDisciplines(List<Element> elements,
            HashSet<string> relevantDisciplines)
        {
            if (relevantDisciplines == null) return elements;

            // Build reverse lookup: which categories belong to which discipline
            var relevantCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in DiscMap)
            {
                if (relevantDisciplines.Contains(kvp.Value))
                    relevantCategories.Add(kvp.Key);
            }

            return elements.Where(e =>
            {
                string cat = ParameterHelpers.GetCategoryName(e);
                return relevantCategories.Contains(cat);
            }).ToList();
        }

        /// <summary>
        /// Build a HashSet of all existing ASS_TAG_1_TXT values in the project.
        /// Call once before a batch tagging loop and pass to BuildAndWriteTag
        /// for collision detection. O(n) scan, O(1) per lookup thereafter.
        /// </summary>
        public static HashSet<string> BuildExistingTagIndex(Document doc)
        {
            var index = new HashSet<string>(StringComparer.Ordinal);
            // Use ElementMulticategoryFilter to skip non-taggable elements
            // (views, sheets, annotations, text notes, dimensions, etc.)
            var cats = SharedParamGuids.AllCategoryEnums;
            IEnumerable<Element> collector;
            if (cats != null && cats.Length > 0)
            {
                var catFilter = new ElementMulticategoryFilter(cats);
                collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(catFilter);
            }
            else
            {
                collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            }
            foreach (Element elem in collector)
            {
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag))
                    index.Add(tag);
            }
            StingLog.Info($"Tag index built: {index.Count} existing tags");
            return index;
        }

        /// <summary>
        /// Scan the entire project and find the highest existing sequence number
        /// for each (DISC, SYS, LVL) group. Returns a dictionary that can be passed
        /// to BuildAndWriteTag so new tags continue from existing numbering.
        /// </summary>
        /// <remarks>
        /// <b>Obsolete</b>: Use <see cref="BuildTagIndexAndCounters"/> instead.
        /// That method merges the sidecar <c>.sting_seq.json</c> counter store with
        /// the live project scan so SEQ numbering survives Revit session boundaries.
        /// Calling this method directly skips the sidecar, which can produce SEQ
        /// collisions after the project has been reopened.
        /// </remarks>
        [Obsolete("Use BuildTagIndexAndCounters(doc) — it merges sidecar counters with the live scan, preventing SEQ collisions across sessions.")]
        public static Dictionary<string, int> GetExistingSequenceCounters(Document doc)
        {
            var maxSeq = new Dictionary<string, int>();
            var known = new HashSet<string>(DiscMap.Keys);

            // Use ElementMulticategoryFilter to skip non-taggable elements
            var seqCats = SharedParamGuids.AllCategoryEnums;
            FilteredElementCollector seqCollector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();
            if (seqCats != null && seqCats.Length > 0)
                seqCollector.WherePasses(new ElementMulticategoryFilter(
                    new List<BuiltInCategory>(seqCats)));
            foreach (Element elem in seqCollector)
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                string seqStr = ParameterHelpers.GetString(elem, ParamRegistry.SEQ);
                if (string.IsNullOrEmpty(disc)) continue;

                // Normalise empty tokens to match BuildAndWriteTag key format
                if (string.IsNullOrEmpty(sys))
                    sys = GetDiscDefaultSysCode(disc);
                if (string.IsNullOrEmpty(lvl) || lvl == "XX")
                    lvl = "L00";

                // Match SeqIncludeZone key format used by BuildAndWriteTag/BuildSeqKey
                string key;
                if (SeqIncludeZone)
                {
                    string zone = ParameterHelpers.GetString(elem, ParamRegistry.ZONE);
                    if (string.IsNullOrEmpty(zone) || zone == "XX" || zone == "ZZ") zone = "Z01";
                    key = $"{disc}_{zone}_{sys}_{lvl}";
                }
                else
                {
                    key = $"{disc}_{sys}_{lvl}";
                }

                if (int.TryParse(seqStr, out int seqNum) && seqNum >= 0)
                {
                    if (!maxSeq.TryGetValue(key, out int curMax) || seqNum > curMax)
                        maxSeq[key] = seqNum;
                }
                else if (CurrentSeqScheme == SeqScheme.Alpha && !string.IsNullOrEmpty(seqStr))
                {
                    int alphaNum = FromAlpha(seqStr);
                    if (alphaNum > 0 && (!maxSeq.TryGetValue(key, out int curAlphaMax) || alphaNum > curAlphaMax))
                        maxSeq[key] = alphaNum;
                }
            }

            return maxSeq;
        }

        /// <summary>
        /// Combined single-pass scan: builds both the tag index and sequence counters
        /// in one iteration over all project elements. Use this instead of calling
        /// BuildExistingTagIndex + GetExistingSequenceCounters separately.
        /// </summary>
        public static (HashSet<string> tagIndex, Dictionary<string, int> seqCounters)
            BuildTagIndexAndCounters(Document doc)
        {
            var index = new HashSet<string>(StringComparer.Ordinal);
            var maxSeq = new Dictionary<string, int>();
            var known = new HashSet<string>(DiscMap.Keys);

            // Use category filter to skip non-taggable elements (views, sheets, etc.)
            var catEnums = SharedParamGuids.AllCategoryEnums;
            IEnumerable<Element> elements;
            if (catEnums != null && catEnums.Length > 0)
            {
                var bicList = new List<BuiltInCategory>(catEnums);
                elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(new ElementMulticategoryFilter(bicList));
            }
            else
            {
                elements = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            }

            foreach (Element elem in elements)
            {
                string tag = ParameterHelpers.GetString(elem, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag))
                    index.Add(tag);

                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                string seqStr = ParameterHelpers.GetString(elem, ParamRegistry.SEQ);
                if (string.IsNullOrEmpty(disc)) continue;

                // Normalise empty tokens to match BuildAndWriteTag key format
                if (string.IsNullOrEmpty(sys))
                    sys = GetDiscDefaultSysCode(disc);
                if (string.IsNullOrEmpty(lvl) || lvl == "XX")
                    lvl = "L00";

                // Match SeqIncludeZone key format used by BuildAndWriteTag/BuildSeqKey
                string key;
                if (SeqIncludeZone)
                {
                    string zone = ParameterHelpers.GetString(elem, ParamRegistry.ZONE);
                    if (string.IsNullOrEmpty(zone) || zone == "XX" || zone == "ZZ") zone = "Z01";
                    key = $"{disc}_{zone}_{sys}_{lvl}";
                }
                else
                {
                    key = $"{disc}_{sys}_{lvl}";
                }

                if (int.TryParse(seqStr, out int seqNum) && seqNum >= 0)
                {
                    if (!maxSeq.TryGetValue(key, out int curMax) || seqNum > curMax)
                        maxSeq[key] = seqNum;
                }
                else if (CurrentSeqScheme == SeqScheme.Alpha && !string.IsNullOrEmpty(seqStr))
                {
                    int alphaNum = FromAlpha(seqStr);
                    if (alphaNum > 0 && (!maxSeq.TryGetValue(key, out int curAlphaMax) || alphaNum > curAlphaMax))
                        maxSeq[key] = alphaNum;
                }
            }

            StingLog.Info($"Tag index built: {index.Count} existing tags, {maxSeq.Count} SEQ groups");

            // P6 / G3.1: Merge sidecar counters — take max(doc_count, sidecar_count) per key
            try
            {
                var sidecar = LoadSeqSidecar(doc);
                if (sidecar != null) MergeSeqSidecar(maxSeq, sidecar);
            }
            catch (Exception ex) { StingLog.Warn($"BuildTagIndexAndCounters sidecar merge: {ex.Message}"); }

            return (index, maxSeq);
        }

        // ── ENH-02: SEQ Sidecar JSON Persistence ────────────────────────────

        /// <summary>
        /// ENH-02: Save SEQ counters to a sidecar JSON file alongside the Revit project.
        /// File name: {ProjectFileName}_STING_SEQ.json. This provides crash-safe
        /// sequence continuity — if Revit crashes during tagging, the last known
        /// counters are preserved on disk.
        /// </summary>
        public static void SaveSeqSidecar(Document doc, Dictionary<string, int> seqCounters)
        {
            try
            {
                string sidecarPath = GetSeqSidecarPath(doc);
                if (sidecarPath == null) return;

                // CRASH-04 fix: ensure parent directory exists before writing
                string sidecarDir = System.IO.Path.GetDirectoryName(sidecarPath);
                if (!string.IsNullOrEmpty(sidecarDir) && !System.IO.Directory.Exists(sidecarDir))
                    System.IO.Directory.CreateDirectory(sidecarDir);

                BIMManager.SidecarVersioning.WriteSidecar(sidecarPath, seqCounters, "1.0");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SaveSeqSidecar: {ex.Message}");
            }
        }

        /// <summary>
        /// ENH-02: Load SEQ counters from sidecar JSON. Returns null if file doesn't exist.
        /// Merges with document-scanned counters by taking the MAX of each key.
        /// </summary>
        public static Dictionary<string, int> LoadSeqSidecar(Document doc)
        {
            try
            {
                string sidecarPath = GetSeqSidecarPath(doc);
                if (sidecarPath == null || !System.IO.File.Exists(sidecarPath)) return null;

                // S3.6.2 — version gate before deserialise.
                StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                    sidecarPath, "planscape.sting-seq-sidecar",
                    StingTools.Core.PluginSchemaVersion.CurrentSeqSidecar);

                var (loaded, ver) = BIMManager.SidecarVersioning.ReadSidecar<Dictionary<string, int>>(sidecarPath, "1.0");
                if (loaded != null && loaded.Count > 0)
                    StingLog.Info($"SEQ sidecar loaded: {loaded.Count} groups (v{ver ?? "legacy"}) from {sidecarPath}");
                return loaded;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadSeqSidecar: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ENH-02: Merge sidecar counters with document-scanned counters.
        /// Takes the MAX of each key to ensure continuity after crashes.
        /// </summary>
        public static void MergeSeqSidecar(Dictionary<string, int> target, Dictionary<string, int> sidecar)
        {
            if (sidecar == null) return;
            foreach (var kvp in sidecar)
            {
                string key = kvp.Key;

                // Key format migration: if SeqIncludeZone changed between sessions,
                // translate old-format keys to new-format keys using max-value strategy
                if (!target.TryGetValue(key, out _))
                {
                    // Try stripping zone segment: "M_Z01_HVAC_L01" → "M_HVAC_L01"
                    // Old format (no zone): DISC_SYS_LVL (3 parts)
                    // New format (with zone): DISC_ZONE_SYS_LVL (4 parts)
                    string[] parts = key.Split('_');
                    string altKey = null;
                    if (SeqIncludeZone && parts.Length == 3)
                    {
                        // H-02 FIX: Sidecar has old format (no zone), current format includes zone.
                        // Previously set altKey=null which caused old counters to be added as-is
                        // with the wrong key format, effectively losing them and restarting SEQ from 1.
                        // Now merge into default zone key so counters are preserved.
                        altKey = $"{parts[0]}_Z01_{parts[1]}_{parts[2]}";
                    }
                    else if (!SeqIncludeZone && parts.Length == 4)
                    {
                        // Sidecar has zone format, current format excludes zone — strip zone
                        altKey = $"{parts[0]}_{parts[2]}_{parts[3]}";
                    }

                    if (altKey != null && target.TryGetValue(altKey, out int altVal))
                    {
                        if (kvp.Value > altVal)
                            target[altKey] = kvp.Value;
                        continue;
                    }
                }

                if (!target.TryGetValue(key, out int tVal) || kvp.Value > tVal)
                    target[key] = kvp.Value;
            }
        }

        private static string GetSeqSidecarPath(Document doc)
        {
            if (doc == null || !doc.IsValidObject) return null;
            string projectPath = doc.PathName;
            if (string.IsNullOrEmpty(projectPath)) return null;
            // Phase 167: prefer _data folder
            try
            {
                string p = StingTools.Core.ProjectFolderEngine.GetDataPath(doc, "seq_counters.json");
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            string dir = System.IO.Path.GetDirectoryName(projectPath);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(projectPath);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(fileName)) return null;
            return System.IO.Path.Combine(dir, $"{fileName}_STING_SEQ.json");
        }

        private static T TryDeserialize<T>(Dictionary<string, object> data, string key)
            where T : class
        {
            if (!data.TryGetValue(key, out object val)) return null;
            try
            {
                string json = JsonConvert.SerializeObject(val);
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex) { StingLog.Warn($"TagConfig deserialize '{key}': {ex.Message}"); return null; }
        }

        /// <summary>FLEX-001: Load custom validation codes from config as a HashSet.</summary>
        private static HashSet<string> LoadCustomCodes(Dictionary<string, object> data, string key)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var codes = TryDeserialize<List<string>>(data, key);
            if (codes != null)
                foreach (var code in codes)
                    if (!string.IsNullOrWhiteSpace(code)) result.Add(code.Trim());
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        // Built-in defaults — mirror Sheet 02-TAG-FAMILY-CONFIG
        // ══════════════════════════════════════════════════════════════════

        private static Dictionary<string, string> DefaultDiscMap()
        {
            return new Dictionary<string, string>
            {
                // MEP — Mechanical
                { "Air Terminals", "M" }, { "Duct Accessories", "M" },
                { "Duct Fittings", "M" }, { "Ducts", "M" },
                { "Duct Insulation", "M" }, { "Duct Lining", "M" },
                { "Flex Ducts", "M" }, { "Mechanical Equipment", "M" },
                { "Mechanical Control Devices", "M" }, { "Mechanical Equipment Sets", "M" },
                { "Pipes", "M" }, { "Pipe Fittings", "M" },
                { "Pipe Accessories", "M" }, { "Pipe Insulation", "M" },
                { "Flex Pipes", "M" },
                // MEP — Plumbing
                { "Plumbing Fixtures", "P" }, { "Plumbing Equipment", "P" },
                // MEP — Fire Protection
                { "Sprinklers", "FP" }, { "Fire Alarm Devices", "FLS" },
                { "Fire Protection", "FP" },
                // MEP — Electrical
                { "Electrical Equipment", "E" }, { "Electrical Fixtures", "E" },
                { "Electrical Connectors", "E" },
                { "Lighting Fixtures", "E" }, { "Lighting Devices", "E" },
                { "Conduits", "E" }, { "Conduit Fittings", "E" },
                { "Cable Trays", "E" }, { "Cable Tray Fittings", "E" },
                // MEP — Low Voltage / ICT
                { "Communication Devices", "LV" },
                { "Data Devices", "LV" }, { "Nurse Call Devices", "LV" },
                { "Security Devices", "LV" }, { "Telephone Devices", "LV" },
                { "Audio Visual Devices", "LV" },
                // MEP — Fabrication
                { "MEP Fabrication Containment", "E" },
                { "MEP Fabrication Ductwork", "M" },
                { "MEP Fabrication Ductwork Stiffeners", "M" },
                { "MEP Fabrication Hangers", "M" },
                { "MEP Fabrication Pipework", "M" },
                { "MEP Ancillary", "M" },
                // MEP — Analytical
                { "Analytical Duct Segments", "M" }, { "Analytical Pipe Segments", "M" },
                // Architecture — Enclosure
                { "Doors", "A" }, { "Windows", "A" },
                { "Walls", "A" }, { "Floors", "A" },
                { "Ceilings", "A" }, { "Roofs", "A" },
                { "Curtain Panels", "A" }, { "Curtain Wall Mullions", "A" },
                { "Curtain Systems", "A" },
                { "Wall Sweeps", "A" }, { "Slab Edges", "A" },
                { "Roof Soffits", "A" }, { "Fascia", "A" }, { "Gutter", "A" },
                // Architecture — Interior
                { "Rooms", "A" }, { "Furniture", "A" },
                { "Furniture Systems", "A" }, { "Casework", "A" },
                { "Food Service Equipment", "A" }, { "Signage", "A" },
                // Architecture — Circulation
                { "Railings", "A" }, { "Handrails", "A" }, { "Top Rails", "A" },
                { "Stairs", "A" }, { "Stair Runs", "A" },
                { "Stair Landings", "A" }, { "Stair Supports", "A" },
                { "Ramps", "A" }, { "Vertical Circulation", "A" },
                // Architecture — Site/Misc
                { "Parking", "A" }, { "Site", "A" }, { "Entourage", "A" },
                { "Planting", "A" }, { "Hardscape", "A" }, { "Roads", "A" },
                { "Pads", "A" }, { "Toposolid", "A" }, { "Toposolid Links", "A" },
                { "Temporary Structures", "A" }, { "Wash", "A" },
                { "Areas", "A" }, { "Spaces", "A" },
                { "Property Lines", "A" }, { "Property Line Segments", "A" },
                // Structure
                { "Structural Columns", "S" }, { "Structural Framing", "S" },
                { "Structural Foundations", "S" }, { "Columns", "S" },
                { "Structural Stiffeners", "S" }, { "Structural Trusses", "S" },
                { "Structural Connections", "S" }, { "Structural Beam Systems", "S" },
                { "Structural Rebar", "S" }, { "Structural Rebar Couplers", "S" },
                { "Structural Area Reinforcement", "S" },
                { "Structural Path Reinforcement", "S" },
                { "Structural Fabric Reinforcement", "S" },
                // Structure — Analytical
                { "Analytical Members", "S" }, { "Analytical Nodes", "S" },
                { "Analytical Links", "S" }, { "Analytical Openings", "S" },
                { "Analytical Panels", "S" },
                // Loads
                { "Area Based Loads", "S" }, { "Area Loads", "S" },
                { "Line Loads", "S" }, { "Point Loads", "S" },
                { "Internal Area Loads", "S" }, { "Internal Line Loads", "S" },
                { "Internal Point Loads", "S" },
                // Generic
                { "Generic Models", "G" }, { "Specialty Equipment", "G" },
                { "Medical Equipment", "G" }, { "Mass", "G" },
                { "Parts", "G" }, { "Assemblies", "G" },
                { "Detail Items", "G" }, { "Model Groups", "G" },
                { "Materials", "G" }, { "Profiles", "G" },
                { "RVT Links", "G" }, { "Zones", "G" },
            };
        }

        private static Dictionary<string, List<string>> DefaultSysMap()
        {
            return new Dictionary<string, List<string>>
            {
                { "HVAC", new List<string> { "Air Terminals", "Duct Accessories", "Duct Fittings", "Ducts", "Duct Insulation", "Duct Lining", "Flex Ducts", "Mechanical Equipment", "Mechanical Control Devices", "Mechanical Equipment Sets", "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes", "MEP Fabrication Ductwork", "MEP Fabrication Ductwork Stiffeners", "MEP Fabrication Hangers", "MEP Ancillary", "Analytical Duct Segments" } },
                // Pipes default to DCW (cold water bias); runtime MEP detection overrides.
                // All pipe categories appear in every applicable system entry so
                // GetAllSysCodes() returns the full list for validation (BUG-001 fix).
                { "DCW", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes", "Plumbing Fixtures", "Plumbing Equipment", "MEP Fabrication Pipework", "Analytical Pipe Segments" } },
                { "DHW", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes" } },
                { "HWS", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes" } },
                { "SAN", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes", "Plumbing Fixtures", "Plumbing Equipment" } },
                { "RWD", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes" } },
                { "GAS", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories", "Pipe Insulation", "Flex Pipes" } },
                { "FP", new List<string> { "Sprinklers", "Fire Protection", "Fire Alarm Devices", "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes" } },
                { "LV", new List<string> { "Electrical Equipment", "Electrical Fixtures", "Electrical Connectors", "Lighting Fixtures", "Lighting Devices", "Conduits", "Conduit Fittings", "Cable Trays", "Cable Tray Fittings", "MEP Fabrication Containment" } },
                // Lightning Protection — BS EN 62305. LPS-bearing elements may be modelled as
                // Electrical Equipment (SPDs, test clamps), Generic Models (rods, mesh, ring earth),
                // Conduits / Conduit Fittings (down-conductor channels), or Specialty Equipment.
                // Family-name discrimination in GetFamilyAwareProdCode picks the correct LPS sub-tag.
                // Phase 176 cross-discipline integration:
                //   - Structural Foundations / Rebar / Reinforcement reused as Type-B foundation earth
                //     (BS EN 62305-3 Annex E.5.3) → STR Tag #22 STING - LPS Foundation Earth Tag
                //   - Roofs / Walls / Curtain Wall / Wall Sweeps / Fascia / Gutter / Roof Soffits acting
                //     as natural air termination (BS EN 62305-3 §5.2.5) → ARCH Tag #36 STING - LPS Natural
                //     Air Termination Tag
                //   - Detail Items used for LPS schematic / installation details → GEN Tag #34 STING - LPS
                //     Generic Component Tag (catch-all)
                { "LPS", new List<string>
                    {
                        // Electrical / generic LPS modelling
                        "Electrical Equipment", "Generic Models", "Conduits", "Conduit Fittings", "Specialty Equipment", "Detail Items",
                        // Structural reuse — Type-B foundation earth (BS EN 62305-3 Annex E.5.3)
                        "Structural Foundations", "Structural Rebar", "Structural Area Reinforcement", "Structural Path Reinforcement", "Structural Fabric Reinforcement",
                        // Architectural reuse — natural air termination (BS EN 62305-3 §5.2.5)
                        "Roofs", "Walls", "Curtain Wall Mullions", "Wall Sweeps", "Fascia", "Gutter", "Roof Soffits"
                    } },
                { "FLS", new List<string> { "Fire Alarm Devices", "Fire Protection" } },
                { "COM", new List<string> { "Communication Devices", "Telephone Devices", "Audio Visual Devices" } },
                { "ICT", new List<string> { "Data Devices" } },
                { "NCL", new List<string> { "Nurse Call Devices" } },
                { "SEC", new List<string> { "Security Devices" } },
                // Architecture
                { "ARC", new List<string> { "Doors", "Windows", "Walls", "Floors", "Ceilings", "Roofs", "Rooms", "Furniture", "Furniture Systems", "Casework", "Railings", "Handrails", "Top Rails", "Stairs", "Stair Runs", "Stair Landings", "Stair Supports", "Ramps", "Vertical Circulation", "Curtain Panels", "Curtain Wall Mullions", "Curtain Systems", "Wall Sweeps", "Slab Edges", "Roof Soffits", "Fascia", "Gutter", "Food Service Equipment", "Signage", "Parking", "Site", "Entourage", "Planting", "Hardscape", "Roads", "Pads", "Toposolid", "Temporary Structures", "Areas", "Spaces" } },
                // Structure
                { "STR", new List<string> { "Structural Columns", "Structural Framing", "Structural Foundations", "Columns", "Structural Stiffeners", "Structural Trusses", "Structural Connections", "Structural Beam Systems", "Structural Rebar", "Structural Rebar Couplers", "Structural Area Reinforcement", "Structural Path Reinforcement", "Structural Fabric Reinforcement", "Analytical Members", "Analytical Nodes", "Analytical Links", "Analytical Openings", "Analytical Panels" } },
                // Generic
                { "GEN", new List<string> { "Generic Models", "Specialty Equipment", "Medical Equipment", "Mass", "Parts", "Assemblies", "Detail Items", "Model Groups", "Materials", "Profiles", "RVT Links", "Zones" } },
            };
        }

        private static Dictionary<string, string> DefaultProdMap()
        {
            return new Dictionary<string, string>
            {
                // MEP — Mechanical
                { "Air Terminals", "GRL" }, { "Duct Accessories", "DAC" },
                { "Duct Fittings", "DFT" }, { "Ducts", "DU" },
                { "Duct Insulation", "DIN" }, { "Duct Lining", "DLN" },
                { "Flex Ducts", "FDU" }, { "Mechanical Equipment", "AHU" },
                { "Mechanical Control Devices", "MCD" }, { "Mechanical Equipment Sets", "MES" },
                { "Pipes", "PP" }, { "Pipe Fittings", "PFT" },
                { "Pipe Accessories", "PAC" }, { "Pipe Insulation", "PIN" },
                { "Flex Pipes", "FPP" },
                // MEP — Plumbing
                { "Plumbing Fixtures", "FIX" }, { "Plumbing Equipment", "PEQ" },
                // MEP — Fire Protection
                { "Sprinklers", "SPR" }, { "Fire Alarm Devices", "FAD" },
                { "Fire Protection", "FPR" },
                // MEP — Electrical
                { "Electrical Equipment", "DB" }, { "Electrical Fixtures", "SKT" },
                { "Electrical Connectors", "ECN" },
                { "Lighting Fixtures", "LUM" }, { "Lighting Devices", "LDV" },
                { "Conduits", "CDT" }, { "Conduit Fittings", "CFT" },
                { "Cable Trays", "CBLT" }, { "Cable Tray Fittings", "CTF" },
                // MEP — Low Voltage / ICT
                { "Communication Devices", "COM" },
                { "Data Devices", "DAT" }, { "Nurse Call Devices", "NCL" },
                { "Security Devices", "SEC" }, { "Telephone Devices", "TEL" },
                { "Audio Visual Devices", "AVD" },
                // MEP — Fabrication
                { "MEP Fabrication Containment", "FCN" },
                { "MEP Fabrication Ductwork", "FDW" },
                { "MEP Fabrication Ductwork Stiffeners", "FDS" },
                { "MEP Fabrication Hangers", "FHG" },
                { "MEP Fabrication Pipework", "FPW" },
                { "MEP Ancillary", "ANC" },
                // MEP — Analytical
                { "Analytical Duct Segments", "ADS" }, { "Analytical Pipe Segments", "APS" },
                // Architecture — Enclosure
                { "Doors", "DR" }, { "Windows", "WIN" },
                { "Walls", "WL" }, { "Floors", "FL" },
                { "Ceilings", "CLG" }, { "Roofs", "RF" },
                { "Curtain Panels", "CPN" }, { "Curtain Wall Mullions", "MUL" },
                { "Curtain Systems", "CWS" },
                { "Wall Sweeps", "WSP" }, { "Slab Edges", "SLE" },
                { "Roof Soffits", "RSF" }, { "Fascia", "FAS" }, { "Gutter", "GTR" },
                // Architecture — Interior
                { "Rooms", "RM" }, { "Furniture", "FUR" },
                { "Furniture Systems", "FUS" }, { "Casework", "CWK" },
                { "Food Service Equipment", "FSE" }, { "Signage", "SGN" },
                // Architecture — Circulation
                { "Railings", "RLG" }, { "Handrails", "HRL" }, { "Top Rails", "TRL" },
                { "Stairs", "STR" }, { "Stair Runs", "SRN" },
                { "Stair Landings", "SLN" }, { "Stair Supports", "SSP" },
                { "Ramps", "RMP" }, { "Vertical Circulation", "VCR" },
                // Architecture — Site/Misc
                { "Parking", "PKG" }, { "Site", "STE" }, { "Entourage", "ENT" },
                { "Planting", "PLT" }, { "Hardscape", "HSC" }, { "Roads", "RD" },
                { "Pads", "PAD" }, { "Toposolid", "TPO" }, { "Toposolid Links", "TPL" },
                { "Temporary Structures", "TMP" }, { "Wash", "WSH" },
                { "Areas", "ARA" }, { "Spaces", "SPC" },
                { "Property Lines", "PRL" }, { "Property Line Segments", "PLS" },
                // Structure
                { "Structural Columns", "COL" }, { "Structural Framing", "BM" },
                { "Structural Foundations", "FDN" }, { "Columns", "COL" },
                { "Structural Stiffeners", "STF" }, { "Structural Trusses", "TRS" },
                { "Structural Connections", "SCN" }, { "Structural Beam Systems", "SBS" },
                { "Structural Rebar", "RBR" }, { "Structural Rebar Couplers", "RBC" },
                { "Structural Area Reinforcement", "SAR" },
                { "Structural Path Reinforcement", "SPT" },
                { "Structural Fabric Reinforcement", "SFR" },
                // Structure — Analytical
                { "Analytical Members", "AMB" }, { "Analytical Nodes", "AND" },
                { "Analytical Links", "ALK" }, { "Analytical Openings", "AOP" },
                { "Analytical Panels", "APN" },
                // Loads
                { "Area Based Loads", "ABL" }, { "Area Loads", "ARL" },
                { "Line Loads", "LNL" }, { "Point Loads", "PTL" },
                { "Internal Area Loads", "IAL" }, { "Internal Line Loads", "ILL" },
                { "Internal Point Loads", "IPL" },
                // Generic
                { "Generic Models", "GEN" }, { "Specialty Equipment", "SPE" },
                { "Medical Equipment", "MED" }, { "Mass", "MAS" },
                { "Parts", "PRT" }, { "Assemblies", "ASM" },
                { "Detail Items", "DTL" }, { "Model Groups", "GRP" },
                { "Materials", "MAT" }, { "Profiles", "PRF" },
                { "RVT Links", "LNK" }, { "Zones", "ZNE" },
            };
        }

        private static Dictionary<string, string> DefaultFuncMap()
        {
            return new Dictionary<string, string>
            {
                { "HVAC", "SUP" }, { "HWS", "HTG" }, { "DHW", "DHW" },
                { "DCW", "DCW" }, { "SAN", "SAN" }, { "RWD", "RWD" }, { "GAS", "GAS" },
                { "FP", "FP" }, { "LV", "PWR" }, { "FLS", "FLS" },
                // Lightning protection — default FUNC. The 6 LPS sub-functions (AT / DC / EE / BOND / SPD / TC)
                // are family-aware overrides resolved by GetFamilyAwareProdCode + ResolveLpsFunc.
                { "LPS", "LPS" },
                { "COM", "COM" }, { "ICT", "ICT" }, { "NCL", "NCL" },
                { "SEC", "SEC" },
                { "ARC", "FIT" }, { "STR", "STR" }, { "GEN", "GEN" },
            };
        }

        private static List<string> DefaultLocCodes()
        {
            return new List<string> { "BLD1", "BLD2", "BLD3", "EXT" };
        }

        private static List<string> DefaultZoneCodes()
        {
            return new List<string> { "Z01", "Z02", "Z03", "Z04" };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAG7: Rich Descriptive Narrative Builder with Markup & Sub-Sections
        // ═══════════════════════════════════════════════════════════════════════
        //
        // Formatting Strategy — exploiting 5 Revit output surfaces:
        //
        //  Surface              Bold  Italic  Underline  Color        How
        //  ───────────────────  ────  ──────  ─────────  ──────────   ──────────────────────────────
        //  Revit Parameters     NO    NO      NO         NO           Split into TAG7A-TAG7F sub-params
        //  TextNote+Formatted   YES   YES     YES        Per-type     FormattedText SetBold/Italic/Underline
        //  Tag Family Labels    YES   YES     NO         Per-label    Multi-label families reference sub-params
        //  WPF Dockable Panel   YES   YES     YES        Per-Run      TextBlock Inlines with Run elements
        //  HTML Export          YES   YES     YES        Per-span     Full CSS styling
        //
        // Markup tokens embedded in TAG7 (parsed by RichTagNote + WPF + HTML export):
        //   «H»text«/H»  — Header/emphasis (Bold + Underline in TextNote, Bold in WPF)
        //   «L»text«/L»  — Label text (Italic in TextNote, muted color in WPF)
        //   «V»text«/V»  — Value text (Normal weight, accent color in WPF/HTML)
        //   «S»text«/S»  — Section separator (pipe "|" with spacing)
        //   «C»text«/C»  — Connector phrase (prose joining words between sections)
        //
        // Sub-section parameters (TAG7A-TAG7F) hold PLAIN text versions for
        // tag family labels. TAG7 holds the MARKED-UP full narrative.
        // ═══════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════
        // TAG1-TAG6 Segment Styling — Per-Segment Color and Style Definitions
        //
        // The 8-segment tag format (DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ) can
        // be styled per segment for rich rendering via TextNote, HTML export,
        // and WPF panel. Each segment gets a distinct color and font style
        // enabling instant visual parsing of the tag structure.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Style definition for a single tag segment (DISC, LOC, ZONE, etc.).</summary>
        public class TagSegmentStyle
        {
            /// <summary>Segment position index (0-7).</summary>
            public int Index { get; set; }
            /// <summary>Short segment name (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ).</summary>
            public string Name { get; set; }
            /// <summary>Full human-readable description.</summary>
            public string Description { get; set; }
            /// <summary>Hex color for rich rendering.</summary>
            public string Color { get; set; }
            /// <summary>Bold rendering hint.</summary>
            public bool Bold { get; set; }
            /// <summary>Italic rendering hint.</summary>
            public bool Italic { get; set; }
        }

        /// <summary>
        /// Default styles for each of the 8 tag segments.
        /// Used by RichTagNote, HTML export, and WPF panel for segment-aware coloring.
        /// </summary>
        public static readonly TagSegmentStyle[] SegmentStyles = new[]
        {
            new TagSegmentStyle { Index = 0, Name = "DISC", Description = "Discipline",      Color = "#1565C0", Bold = true,  Italic = false },
            new TagSegmentStyle { Index = 1, Name = "LOC",  Description = "Location",         Color = "#2E7D32", Bold = false, Italic = false },
            new TagSegmentStyle { Index = 2, Name = "ZONE", Description = "Zone",              Color = "#E65100", Bold = false, Italic = false },
            new TagSegmentStyle { Index = 3, Name = "LVL",  Description = "Level",             Color = "#6A1B9A", Bold = false, Italic = false },
            new TagSegmentStyle { Index = 4, Name = "SYS",  Description = "System Type",       Color = "#C62828", Bold = true,  Italic = false },
            new TagSegmentStyle { Index = 5, Name = "FUNC", Description = "Function",          Color = "#00838F", Bold = false, Italic = true  },
            new TagSegmentStyle { Index = 6, Name = "PROD", Description = "Product Code",      Color = "#4527A0", Bold = true,  Italic = false },
            new TagSegmentStyle { Index = 7, Name = "SEQ",  Description = "Sequence Number",   Color = "#37474F", Bold = false, Italic = false },
        };

        /// <summary>
        /// Result of parsing a TAG1-TAG6 value into styled segments.
        /// Each segment has text, style, and whether it was populated.
        /// </summary>
        public class TagSegmentResult
        {
            /// <summary>The full tag string (e.g. "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003").</summary>
            public string FullTag { get; set; } = "";
            /// <summary>Individual segment values in order (DISC, LOC, ZONE, LVL, SYS, FUNC, PROD, SEQ).</summary>
            public string[] Segments { get; set; } = new string[8];
            /// <summary>Whether each segment is populated (non-empty and not a placeholder).</summary>
            public bool[] Populated { get; set; } = new bool[8];
            /// <summary>Marked-up tag with segment color tokens: «D0»DISC«/D0» «S»-«/S» «D1»LOC«/D1» ...</summary>
            public string MarkedUpTag { get; set; } = "";
        }

        /// <summary>
        /// Parse a tag string (TAG1-TAG6) into styled segments.
        /// Returns segment data for rich rendering.
        /// </summary>
        public static TagSegmentResult ParseTagSegments(string tagValue)
        {
            var result = new TagSegmentResult { FullTag = tagValue ?? "" };
            if (string.IsNullOrEmpty(tagValue)) return result;

            string[] parts = tagValue.Split(new[] { Separator }, StringSplitOptions.None);
            var marked = new System.Text.StringBuilder();

            for (int i = 0; i < 8; i++)
            {
                string val = i < parts.Length ? parts[i] : "";
                result.Segments[i] = val;
                result.Populated[i] = !string.IsNullOrEmpty(val) && val != "XX" && val != "ZZ" && val != "0000";

                if (i > 0) marked.Append($"\u00ABS\u00BB{Separator}\u00AB/S\u00BB");
                marked.Append($"\u00ABD{i}\u00BB{val}\u00AB/D{i}\u00BB");
            }

            result.MarkedUpTag = marked.ToString();
            return result;
        }

        /// <summary>
        /// Parse segment markup tokens from a marked-up tag string.
        /// Returns list of (text, segmentIndex) tuples where segmentIndex is 0-7 for segments,
        /// -1 for separators, -2 for plain text.
        /// </summary>
        public static List<(string text, int segmentIndex)> ParseSegmentMarkup(string marked)
        {
            var result = new List<(string text, int segmentIndex)>();
            if (string.IsNullOrEmpty(marked)) return result;

            int pos = 0;
            var plain = new System.Text.StringBuilder();

            while (pos < marked.Length)
            {
                if (pos + 3 < marked.Length && marked[pos] == '\u00AB')
                {
                    // Flush plain text
                    if (plain.Length > 0)
                    {
                        result.Add((plain.ToString(), -2));
                        plain.Clear();
                    }

                    int tagEnd = marked.IndexOf('\u00BB', pos);
                    if (tagEnd > pos)
                    {
                        string tag = marked.Substring(pos + 1, tagEnd - pos - 1);

                        if (tag == "S")
                        {
                            // Separator
                            string closeTag = "\u00AB/S\u00BB";
                            int closeIdx = marked.IndexOf(closeTag, tagEnd + 1);
                            if (closeIdx > tagEnd)
                            {
                                string content = marked.Substring(tagEnd + 1, closeIdx - tagEnd - 1);
                                result.Add((content, -1));
                                pos = closeIdx + closeTag.Length;
                                continue;
                            }
                        }
                        else if (tag.Length == 2 && tag[0] == 'D' && char.IsDigit(tag[1]))
                        {
                            int segIdx = tag[1] - '0';
                            string closeTag = $"\u00AB/D{segIdx}\u00BB";
                            int closeIdx = marked.IndexOf(closeTag, tagEnd + 1);
                            if (closeIdx > tagEnd)
                            {
                                string content = marked.Substring(tagEnd + 1, closeIdx - tagEnd - 1);
                                result.Add((content, segIdx));
                                pos = closeIdx + closeTag.Length;
                                continue;
                            }
                        }

                        // Fallback: skip the opening tag
                        pos = tagEnd + 1;
                        continue;
                    }
                }

                plain.Append(marked[pos]);
                pos++;
            }

            if (plain.Length > 0)
                result.Add((plain.ToString(), -2));

            return result;
        }

        /// <summary>
        /// Result of building TAG7 narrative — contains the full marked-up narrative
        /// plus individual plain-text sections for TAG7A-TAG7F sub-parameters.
        /// </summary>
        public class Tag7Result
        {
            /// <summary>Full narrative with markup tokens (for TAG7 parameter + rich rendering).</summary>
            public string MarkedUpNarrative { get; set; } = "";
            /// <summary>Full narrative without markup (plain text fallback).</summary>
            public string PlainNarrative { get; set; } = "";
            /// <summary>Section A: Identity Header — asset name, product, manufacturer (plain).</summary>
            public string SectionA { get; set; } = "";
            /// <summary>Section B: System &amp; Function Context (plain).</summary>
            public string SectionB { get; set; } = "";
            /// <summary>Section C: Spatial Context — room, department, grid (plain).</summary>
            public string SectionC { get; set; } = "";
            /// <summary>Section D: Lifecycle &amp; Status (plain).</summary>
            public string SectionD { get; set; } = "";
            /// <summary>Section E: Technical Specifications (plain).</summary>
            public string SectionE { get; set; } = "";
            /// <summary>Section F: Classification &amp; Reference (plain).</summary>
            public string SectionF { get; set; } = "";

            /// <summary>
            /// All 6 sections as an array (A-F), matching TAG7Sections order.
            /// Phase 165 perf — array is materialised once on first read and
            /// cached on the instance. Tag7Result is per-tagging-call so the
            /// cache lives only as long as the surrounding write.
            /// </summary>
            public string[] AllSections =>
                _allSectionsCache ??= new[] { SectionA, SectionB, SectionC, SectionD, SectionE, SectionF };
            private string[] _allSectionsCache;

            // ─── T4-T10 tier summaries (Phase 165 — tagging workflow repair) ───
            // Each is a single-line formatted summary built from the relevant
            // shared parameters. Empty string means the element carries no data
            // for that tier — the writer skips those tiers silently.

            /// <summary>T4: Commissioning &amp; handover summary.</summary>
            public string SectionT4 { get; set; } = "";
            /// <summary>T5: Cost &amp; procurement summary (UGX/USD/install hrs/labour).</summary>
            public string SectionT5 { get; set; } = "";
            /// <summary>T6: Carbon &amp; sustainability summary (A1-A3, A4, B6, C-stages).</summary>
            public string SectionT6 { get; set; } = "";
            /// <summary>T7: Fabrication &amp; QC summary (spool / status / inspector).</summary>
            public string SectionT7 { get; set; } = "";
            /// <summary>T8: Clash triage &amp; resolution summary.</summary>
            public string SectionT8 { get; set; } = "";
            /// <summary>T9: As-built reconciliation &amp; model-health summary.</summary>
            public string SectionT9 { get; set; } = "";
            /// <summary>T10: Compliance / audit-trail summary (IFC PSet / ACC).</summary>
            public string SectionT10 { get; set; } = "";

            /// <summary>
            /// T4..T10 summaries indexed 0..6 (i.e. Tier4Summaries[0] == SectionT4).
            /// Phase 165 perf — cached on first read, identical lifetime to AllSections.
            /// </summary>
            public string[] Tier4Summaries => _tier4SummariesCache ??= new[]
            {
                SectionT4, SectionT5, SectionT6, SectionT7, SectionT8, SectionT9, SectionT10
            };
            private string[] _tier4SummariesCache;
        }

        /// <summary>
        /// Section style definitions for rich rendering.
        /// Each section has a name, color (hex), and font style hint.
        /// Used by RichTagNoteCommand, WPF panel, and HTML export.
        /// </summary>
        public static readonly Tag7SectionStyle[] SectionStyles = new[]
        {
            new Tag7SectionStyle { Key = "A", Name = "Identity",       Color = "#1565C0", Bold = true,  Italic = false, Underline = true  },
            new Tag7SectionStyle { Key = "B", Name = "System",         Color = "#2E7D32", Bold = false, Italic = true,  Underline = false },
            new Tag7SectionStyle { Key = "C", Name = "Spatial",        Color = "#E65100", Bold = false, Italic = false, Underline = false },
            new Tag7SectionStyle { Key = "D", Name = "Lifecycle",      Color = "#C62828", Bold = false, Italic = false, Underline = false },
            new Tag7SectionStyle { Key = "E", Name = "Technical",      Color = "#6A1B9A", Bold = true,  Italic = false, Underline = false },
            new Tag7SectionStyle { Key = "F", Name = "Classification", Color = "#37474F", Bold = false, Italic = true,  Underline = false },
        };

        /// <summary>Style definition for a TAG7 narrative section.</summary>
        public class Tag7SectionStyle
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public string Color { get; set; }
            public bool Bold { get; set; }
            public bool Italic { get; set; }
            public bool Underline { get; set; }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // TAG7 Display Presets — Configurable Color/Style Schemes
        //
        // Each preset defines how TAG7 sections are presented based on context:
        //   - By Discipline: M=Blue, E=Yellow, P=Green headers
        //   - By Status: NEW=Green, EXISTING=Blue, DEMOLISHED=Red
        //   - By System: HVAC=Orange, Electrical=Yellow, Plumbing=Green
        //   - By Completeness: Full=Green, Partial=Orange, Missing=Red
        //   - By Priority: Critical=Red, Standard=Blue, Low=Grey
        //   - Monochrome: Print-ready black/grey scheme
        //   - Accessible: Colorblind-safe palette
        //
        // Each preset maps a discriminator value (discipline code, status, etc.)
        // to a Tag7DisplayStyle containing header color, section colors, and
        // font style overrides.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Display style for a TAG7 rendering — applied per-element based on
        /// the active preset and the element's discriminator value.
        /// </summary>
        public class Tag7DisplayStyle
        {
            /// <summary>Primary color for card header / element highlight.</summary>
            public string HeaderColor { get; set; }
            /// <summary>Background tint for the card body.</summary>
            public string BackgroundTint { get; set; }
            /// <summary>Override colors for sections A-F (null = use default SectionStyles).</summary>
            public string[] SectionColors { get; set; }
            /// <summary>Sections to render in bold (overrides default).</summary>
            public bool[] BoldOverrides { get; set; }
            /// <summary>Sections to show/hide (true = show, false = hide).</summary>
            public bool[] SectionVisibility { get; set; }
            /// <summary>Human-readable label for this style.</summary>
            public string Label { get; set; }
        }

        /// <summary>
        /// A TAG7 display preset — a named scheme mapping discriminator values
        /// to display styles. Used by RichTagNote, HTML export, and WPF panel.
        /// </summary>
        public class Tag7DisplayPreset
        {
            /// <summary>Unique preset name (e.g. "Discipline", "Status", "System").</summary>
            public string Name { get; set; }
            /// <summary>Human-readable description.</summary>
            public string Description { get; set; }
            /// <summary>Which element attribute to discriminate on.</summary>
            public string DiscriminatorParam { get; set; }
            /// <summary>Mapping of discriminator value → display style.</summary>
            public Dictionary<string, Tag7DisplayStyle> Styles { get; set; }
            /// <summary>Fallback style when discriminator value doesn't match.</summary>
            public Tag7DisplayStyle DefaultStyle { get; set; }
        }

        /// <summary>Active preset (changed by user via command or panel).
        /// GAP-009: Lazily restores from persisted name on first access.</summary>
        private static Tag7DisplayPreset _activePreset;
        public static Tag7DisplayPreset ActivePreset
        {
            get
            {
                if (_activePreset == null && !string.IsNullOrEmpty(_activePresetName))
                {
                    var preset = BuiltInPresets.FirstOrDefault(p =>
                        p.Name.Equals(_activePresetName, StringComparison.OrdinalIgnoreCase));
                    if (preset != null) _activePreset = preset;
                    else _activePresetName = null; // Invalid preset name, clear it
                }
                return _activePreset;
            }
            set { _activePreset = value; }
        }

        /// <summary>Get the display style for an element based on the active preset.</summary>
        public static Tag7DisplayStyle GetDisplayStyle(Element el)
        {
            if (ActivePreset == null) return null;

            string value = ParameterHelpers.GetString(el, ActivePreset.DiscriminatorParam);
            if (!string.IsNullOrEmpty(value) && ActivePreset.Styles.TryGetValue(value, out var style))
                return style;

            return ActivePreset.DefaultStyle;
        }

        /// <summary>All built-in TAG7 display presets.</summary>
        public static readonly Tag7DisplayPreset[] BuiltInPresets = BuildPresets();

        private static Tag7DisplayPreset[] BuildPresets()
        {
            var all6Visible = new bool[] { true, true, true, true, true, true };
            var defaultBold = new bool[] { true, false, false, false, true, false };

            return new[]
            {
                // ── Preset 1: By Discipline ──────────────────────────────────
                new Tag7DisplayPreset
                {
                    Name = "Discipline",
                    Description = "Color-code by discipline: Mechanical=Blue, Electrical=Amber, Plumbing=Green, etc.",
                    DiscriminatorParam = ParamRegistry.DISC,
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "M",  new Tag7DisplayStyle { HeaderColor = "#1565C0", BackgroundTint = "#E3F2FD", Label = "Mechanical",
                            SectionColors = new[] { "#1565C0", "#1976D2", "#1E88E5", "#42A5F5", "#0D47A1", "#1565C0" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "E",  new Tag7DisplayStyle { HeaderColor = "#F9A825", BackgroundTint = "#FFFDE7", Label = "Electrical",
                            SectionColors = new[] { "#F9A825", "#FBC02D", "#FDD835", "#FFD54F", "#F57F17", "#F9A825" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "P",  new Tag7DisplayStyle { HeaderColor = "#2E7D32", BackgroundTint = "#E8F5E9", Label = "Plumbing",
                            SectionColors = new[] { "#2E7D32", "#388E3C", "#43A047", "#66BB6A", "#1B5E20", "#2E7D32" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "A",  new Tag7DisplayStyle { HeaderColor = "#757575", BackgroundTint = "#F5F5F5", Label = "Architectural",
                            SectionColors = new[] { "#616161", "#757575", "#9E9E9E", "#BDBDBD", "#424242", "#616161" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "S",  new Tag7DisplayStyle { HeaderColor = "#C62828", BackgroundTint = "#FFEBEE", Label = "Structural",
                            SectionColors = new[] { "#C62828", "#D32F2F", "#E53935", "#EF5350", "#B71C1C", "#C62828" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "FP", new Tag7DisplayStyle { HeaderColor = "#E65100", BackgroundTint = "#FFF3E0", Label = "Fire Protection",
                            SectionColors = new[] { "#E65100", "#EF6C00", "#F57C00", "#FB8C00", "#BF360C", "#E65100" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "LV", new Tag7DisplayStyle { HeaderColor = "#6A1B9A", BackgroundTint = "#F3E5F5", Label = "Low Voltage",
                            SectionColors = new[] { "#6A1B9A", "#7B1FA2", "#8E24AA", "#AB47BC", "#4A148C", "#6A1B9A" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "G",  new Tag7DisplayStyle { HeaderColor = "#795548", BackgroundTint = "#EFEBE9", Label = "Gas",
                            SectionColors = new[] { "#795548", "#8D6E63", "#A1887F", "#BCAAA4", "#4E342E", "#795548" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#455A64", BackgroundTint = "#ECEFF1", Label = "Unknown",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 2: By Status ──────────────────────────────────────
                new Tag7DisplayPreset
                {
                    Name = "Status",
                    Description = "Color-code by lifecycle status: NEW=Green, EXISTING=Blue, DEMOLISHED=Red, TEMPORARY=Orange",
                    DiscriminatorParam = ParamRegistry.STATUS,
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "NEW",         new Tag7DisplayStyle { HeaderColor = "#2E7D32", BackgroundTint = "#E8F5E9", Label = "New Construction",
                            SectionColors = new[] { "#2E7D32", "#388E3C", "#2E7D32", "#43A047", "#2E7D32", "#388E3C" },
                            BoldOverrides = new[] { true, false, false, true, true, false }, SectionVisibility = all6Visible } },
                        { "EXISTING",    new Tag7DisplayStyle { HeaderColor = "#1565C0", BackgroundTint = "#E3F2FD", Label = "Existing Asset",
                            SectionColors = new[] { "#1565C0", "#1976D2", "#1565C0", "#42A5F5", "#1565C0", "#1976D2" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "DEMOLISHED",  new Tag7DisplayStyle { HeaderColor = "#C62828", BackgroundTint = "#FFEBEE", Label = "Demolished",
                            SectionColors = new[] { "#C62828", "#D32F2F", "#C62828", "#EF5350", "#C62828", "#D32F2F" },
                            BoldOverrides = new[] { true, false, false, true, false, false },
                            SectionVisibility = new[] { true, true, true, true, false, true } } },
                        { "TEMPORARY",   new Tag7DisplayStyle { HeaderColor = "#E65100", BackgroundTint = "#FFF3E0", Label = "Temporary",
                            SectionColors = new[] { "#E65100", "#EF6C00", "#E65100", "#FB8C00", "#E65100", "#EF6C00" },
                            BoldOverrides = new[] { true, false, false, true, false, false }, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#757575", BackgroundTint = "#FAFAFA", Label = "No Status",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 3: By System ──────────────────────────────────────
                new Tag7DisplayPreset
                {
                    Name = "System",
                    Description = "Color-code by system type: HVAC=Blue, DCW=Cyan, HWS=Red, SAN=Brown, LV=Amber, FP=Orange",
                    DiscriminatorParam = ParamRegistry.SYS,
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "HVAC", new Tag7DisplayStyle { HeaderColor = "#1565C0", BackgroundTint = "#E3F2FD", Label = "HVAC",
                            SectionColors = new[] { "#1565C0", "#0D47A1", "#1565C0", "#1976D2", "#0D47A1", "#1565C0" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "DCW",  new Tag7DisplayStyle { HeaderColor = "#00838F", BackgroundTint = "#E0F7FA", Label = "Domestic Cold Water",
                            SectionColors = new[] { "#00838F", "#006064", "#00838F", "#0097A7", "#006064", "#00838F" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "HWS",  new Tag7DisplayStyle { HeaderColor = "#D32F2F", BackgroundTint = "#FFEBEE", Label = "Hot Water Supply",
                            SectionColors = new[] { "#D32F2F", "#C62828", "#D32F2F", "#E53935", "#C62828", "#D32F2F" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "SAN",  new Tag7DisplayStyle { HeaderColor = "#6D4C41", BackgroundTint = "#EFEBE9", Label = "Sanitary",
                            SectionColors = new[] { "#6D4C41", "#5D4037", "#6D4C41", "#795548", "#5D4037", "#6D4C41" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "LV",   new Tag7DisplayStyle { HeaderColor = "#F9A825", BackgroundTint = "#FFFDE7", Label = "Low Voltage",
                            SectionColors = new[] { "#F9A825", "#F57F17", "#F9A825", "#FBC02D", "#F57F17", "#F9A825" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "FP",   new Tag7DisplayStyle { HeaderColor = "#E65100", BackgroundTint = "#FFF3E0", Label = "Fire Protection",
                            SectionColors = new[] { "#E65100", "#BF360C", "#E65100", "#EF6C00", "#BF360C", "#E65100" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "FLS",  new Tag7DisplayStyle { HeaderColor = "#FF6F00", BackgroundTint = "#FFF8E1", Label = "Fire Life Safety",
                            SectionColors = new[] { "#FF6F00", "#E65100", "#FF6F00", "#FF8F00", "#E65100", "#FF6F00" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#546E7A", BackgroundTint = "#ECEFF1", Label = "Other System",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 4: By Completeness ────────────────────────────────
                // Discriminates on TAG1 presence and section fill rate
                new Tag7DisplayPreset
                {
                    Name = "Completeness",
                    Description = "RAG status: Green=Complete (all 8 tokens), Orange=Partial, Red=Missing critical tokens",
                    DiscriminatorParam = "_COMPLETENESS_", // Special: computed by GetDisplayStyle override
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "COMPLETE",    new Tag7DisplayStyle { HeaderColor = "#2E7D32", BackgroundTint = "#E8F5E9", Label = "Complete",
                            SectionColors = new[] { "#2E7D32", "#388E3C", "#2E7D32", "#43A047", "#2E7D32", "#388E3C" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "PARTIAL",     new Tag7DisplayStyle { HeaderColor = "#F9A825", BackgroundTint = "#FFFDE7", Label = "Partial",
                            SectionColors = new[] { "#F9A825", "#FBC02D", "#F9A825", "#FDD835", "#F9A825", "#FBC02D" },
                            BoldOverrides = new[] { true, false, false, true, false, true }, SectionVisibility = all6Visible } },
                        { "INCOMPLETE",  new Tag7DisplayStyle { HeaderColor = "#C62828", BackgroundTint = "#FFEBEE", Label = "Incomplete",
                            SectionColors = new[] { "#C62828", "#D32F2F", "#C62828", "#EF5350", "#C62828", "#D32F2F" },
                            BoldOverrides = new[] { true, false, false, true, false, true }, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#9E9E9E", BackgroundTint = "#FAFAFA", Label = "Untagged",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 5: Monochrome (Print-Ready) ───────────────────────
                new Tag7DisplayPreset
                {
                    Name = "Monochrome",
                    Description = "Print-friendly black/grey scheme with no color — suitable for B&W printing",
                    DiscriminatorParam = "_ALWAYS_DEFAULT_",
                    Styles = new Dictionary<string, Tag7DisplayStyle>(),
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#212121", BackgroundTint = "#FAFAFA", Label = "Asset",
                        SectionColors = new[] { "#212121", "#424242", "#616161", "#757575", "#212121", "#424242" },
                        BoldOverrides = new[] { true, false, false, false, true, true }, SectionVisibility = all6Visible },
                },

                // ── Preset 6: Accessible (Colorblind-Safe) ──────────────────
                new Tag7DisplayPreset
                {
                    Name = "Accessible",
                    Description = "Colorblind-safe palette using blue/orange contrast (deuteranopia/protanopia friendly)",
                    DiscriminatorParam = ParamRegistry.DISC,
                    Styles = new Dictionary<string, Tag7DisplayStyle>
                    {
                        { "M",  new Tag7DisplayStyle { HeaderColor = "#0072B2", BackgroundTint = "#E1F5FE", Label = "Mechanical",
                            SectionColors = new[] { "#0072B2", "#0072B2", "#0072B2", "#0072B2", "#0072B2", "#0072B2" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "E",  new Tag7DisplayStyle { HeaderColor = "#E69F00", BackgroundTint = "#FFF8E1", Label = "Electrical",
                            SectionColors = new[] { "#E69F00", "#E69F00", "#E69F00", "#E69F00", "#E69F00", "#E69F00" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "P",  new Tag7DisplayStyle { HeaderColor = "#009E73", BackgroundTint = "#E0F2F1", Label = "Plumbing",
                            SectionColors = new[] { "#009E73", "#009E73", "#009E73", "#009E73", "#009E73", "#009E73" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "A",  new Tag7DisplayStyle { HeaderColor = "#56B4E9", BackgroundTint = "#E1F5FE", Label = "Architectural",
                            SectionColors = new[] { "#56B4E9", "#56B4E9", "#56B4E9", "#56B4E9", "#56B4E9", "#56B4E9" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                        { "FP", new Tag7DisplayStyle { HeaderColor = "#D55E00", BackgroundTint = "#FBE9E7", Label = "Fire Protection",
                            SectionColors = new[] { "#D55E00", "#D55E00", "#D55E00", "#D55E00", "#D55E00", "#D55E00" },
                            BoldOverrides = defaultBold, SectionVisibility = all6Visible } },
                    },
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#CC79A7", BackgroundTint = "#FCE4EC", Label = "Other",
                        SectionColors = null, BoldOverrides = defaultBold, SectionVisibility = all6Visible },
                },

                // ── Preset 7: Technical Focus ────────────────────────────────
                new Tag7DisplayPreset
                {
                    Name = "Technical Focus",
                    Description = "Emphasize Technical (E) and Classification (F) sections, dim Identity. For engineering review.",
                    DiscriminatorParam = "_ALWAYS_DEFAULT_",
                    Styles = new Dictionary<string, Tag7DisplayStyle>(),
                    DefaultStyle = new Tag7DisplayStyle { HeaderColor = "#6A1B9A", BackgroundTint = "#F3E5F5", Label = "Engineering Review",
                        SectionColors = new[] { "#9E9E9E", "#9E9E9E", "#9E9E9E", "#757575", "#6A1B9A", "#1565C0" },
                        BoldOverrides = new[] { false, false, false, false, true, true },
                        SectionVisibility = new[] { true, true, false, true, true, true } },
                },
            };
        }

        /// <summary>
        /// Get display style with completeness-aware discrimination.
        /// When the active preset discriminates on "_COMPLETENESS_", computes
        /// the completeness level from the element's token fill rate.
        /// </summary>
        public static Tag7DisplayStyle GetDisplayStyleSmart(Element el)
        {
            if (ActivePreset == null) return null;

            // Special computed discriminators
            if (ActivePreset.DiscriminatorParam == "_COMPLETENESS_")
            {
                string[] tokens = ParamRegistry.ReadTokenValues(el);
                int filled = tokens.Count(t => !string.IsNullOrEmpty(t) && t != "XX" && t != "ZZ");
                string level = filled >= 8 ? "COMPLETE" : filled >= 5 ? "PARTIAL" : "INCOMPLETE";
                if (ActivePreset.Styles.TryGetValue(level, out var style))
                    return style;
                return ActivePreset.DefaultStyle;
            }

            if (ActivePreset.DiscriminatorParam == "_ALWAYS_DEFAULT_")
                return ActivePreset.DefaultStyle;

            // Standard parameter-based discrimination
            string value = ParameterHelpers.GetString(el, ActivePreset.DiscriminatorParam);
            if (!string.IsNullOrEmpty(value) && ActivePreset.Styles.TryGetValue(value, out var s))
                return s;

            return ActivePreset.DefaultStyle;
        }

        /// <summary>Set the active preset by name. Returns true if found.
        /// GAP-009: Also persists the preset name so it survives Revit restart.</summary>
        public static bool SetActivePreset(string presetName)
        {
            var preset = BuiltInPresets.FirstOrDefault(p =>
                p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (preset != null)
            {
                ActivePreset = preset;
                _activePresetName = presetName;
                PersistPresetName(presetName);
                return true;
            }
            return false;
        }

        /// <summary>The stored preset name for config persistence.</summary>
        private static string _activePresetName;

        /// <summary>GAP-009: Persist preset name to project_config.json.</summary>
        private static void PersistPresetName(string presetName)
        {
            try
            {
                string configPath = StingToolsApp.FindDataFile("project_config.json");
                if (string.IsNullOrEmpty(configPath)) return;

                string json = File.ReadAllText(configPath);
                Dictionary<string, object> data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                    ?? new Dictionary<string, object>();
                data["ACTIVE_PRESET"] = presetName;
                File.WriteAllText(configPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Failed to persist preset name: {ex.Message}");
            }
        }

        /// <summary>Strip all markup tokens from a string, returning plain text.</summary>
        public static string StripMarkup(string marked)
        {
            if (string.IsNullOrEmpty(marked)) return "";
            return marked
                .Replace("«H»", "").Replace("«/H»", "")
                .Replace("«L»", "").Replace("«/L»", "")
                .Replace("«V»", "").Replace("«/V»", "")
                .Replace("«S»", "").Replace("«/S»", "")
                .Replace("«C»", "").Replace("«/C»", "");
        }

        /// <summary>
        /// Parse markup tokens from TAG7 text into styled segments.
        /// Returns a list of (text, style) tuples where style is "H", "L", "V", "S", or "" (plain).
        /// Used by WPF panel and HTML export for rich rendering.
        /// </summary>
        public static List<(string text, string style)> ParseMarkup(string marked)
        {
            var result = new List<(string text, string style)>();
            if (string.IsNullOrEmpty(marked)) return result;

            int i = 0;
            var plain = new System.Text.StringBuilder();

            while (i < marked.Length)
            {
                // Check for markup token start
                if (i + 2 < marked.Length && marked[i] == '\u00AB') // «
                {
                    // Flush any accumulated plain text
                    if (plain.Length > 0)
                    {
                        result.Add((plain.ToString(), ""));
                        plain.Clear();
                    }

                    // Find the style character and closing »
                    int tagEnd = marked.IndexOf('\u00BB', i); // »
                    if (tagEnd > i)
                    {
                        string tag = marked.Substring(i + 1, tagEnd - i - 1);
                        if (tag.Length == 1 && "HLVS".Contains(tag))
                        {
                            // Opening tag — find matching close
                            string closeTag = $"\u00AB/{tag}\u00BB";
                            int closeIdx = marked.IndexOf(closeTag, tagEnd + 1);
                            if (closeIdx > tagEnd)
                            {
                                string content = marked.Substring(tagEnd + 1, closeIdx - tagEnd - 1);
                                result.Add((content, tag));
                                i = closeIdx + closeTag.Length;
                                continue;
                            }
                        }
                        else if (tag.StartsWith("/"))
                        {
                            // Orphan close tag — skip
                            i = tagEnd + 1;
                            continue;
                        }
                    }
                }

                plain.Append(marked[i]);
                i++;
            }

            if (plain.Length > 0)
                result.Add((plain.ToString(), ""));

            return result;
        }

        /// <summary>Full discipline name for human-readable narrative.</summary>
        internal static readonly Dictionary<string, string> DisciplineDescriptions = new Dictionary<string, string>
        {
            { "M", "Mechanical" }, { "E", "Electrical" }, { "P", "Plumbing" },
            { "A", "Architectural" }, { "S", "Structural" }, { "FP", "Fire Protection" },
            { "LV", "Low Voltage" }, { "G", "Gas" }, { "GEN", "General" },
            { "H", "Healthcare" }, { "MG", "Medical Gas" }, { "RP", "Radiation Protection" },
        };

        /// <summary>Full system name for human-readable narrative.</summary>
        internal static readonly Dictionary<string, string> SystemDescriptions = new Dictionary<string, string>
        {
            { "HVAC", "Heating Ventilation and Air Conditioning" },
            { "HWS", "Hot Water Supply" }, { "DHW", "Domestic Hot Water" },
            { "DCW", "Domestic Cold Water" }, { "SAN", "Sanitary Drainage" },
            { "RWD", "Rainwater Drainage" }, { "GAS", "Gas Supply" },
            { "FP", "Fire Protection" }, { "FLS", "Fire Life Safety" },
            { "LV", "Low Voltage Distribution" }, { "SEC", "Security Systems" },
            { "ICT", "Information and Communications Technology" },
            { "COM", "Communications" }, { "NCL", "Nurse Call Systems" },
            { "ARC", "Architectural Fabric" }, { "STR", "Structural Elements" },
            { "GEN", "General Services" },
            // Healthcare Pack (Phase H-1)
            { "MGS-O2", "Medical Oxygen Supply" }, { "MGS-AIR", "Medical Compressed Air" },
            { "MGS-VAC", "Medical Vacuum" }, { "MGS-N2O", "Nitrous Oxide Supply" },
            { "MGS-CO2", "Carbon Dioxide Supply" }, { "MGS-N2", "Nitrogen Supply" },
            { "MGS-AGS", "Anaesthetic Gas Scavenging" },
            { "EES-LS", "Essential Electrical Services (Life Safety)" },
            { "EES-CR", "Essential Electrical Services (Critical)" },
            { "EES-EB", "Essential Electrical Services (Enhanced)" },
            { "LPS", "Lightning Protection System" },
            { "CLN", "Clinical Environment" }, { "RAD", "Radiation Shielding" },
        };

        /// <summary>Full function description for human-readable narrative.</summary>
        internal static readonly Dictionary<string, string> FunctionDescriptions = new Dictionary<string, string>
        {
            { "SUP", "Supply" }, { "RTN", "Return" }, { "EXH", "Exhaust" },
            { "FRA", "Fresh Air Intake" }, { "HTG", "Heating" },
            { "DHW", "Domestic Hot Water Distribution" },
            { "DCW", "Domestic Cold Water Distribution" },
            { "SAN", "Sanitary Waste Disposal" }, { "RWD", "Rainwater Disposal" },
            { "GAS", "Gas Distribution" }, { "FP", "Fire Protection Suppression" },
            { "FLS", "Fire Detection and Alarm" },
            { "PWR", "Power Distribution" }, { "LTG", "Lighting" },
            { "COM", "Voice and Data Communications" },
            { "ICT", "Data Network and Infrastructure" },
            { "NCL", "Patient Nurse Call" }, { "SEC", "Security and Access Control" },
            { "FIT", "Finishes and Fitout" }, { "STR", "Primary Structure" },
            { "GEN", "General Purpose" },
            // Healthcare Pack (Phase H-1)
            { "AT", "Air Termination" }, { "DC", "Down Conductor" }, { "EB", "Equipotential Bond" },
            { "EP", "Earth Pit" }, { "SPD", "Surge Protection Device" },
            { "DIST", "Distribution" }, { "ISO", "Isolation" }, { "ALM", "Area Alarm" },
            { "TU", "Terminal Unit" }, { "ZVB", "Zone Valve Box" },
            { "AAP", "Area Alarm Panel" }, { "SHLD", "Shielding" }, { "ZONE", "Safety Zone" },
        };

        /// <summary>Full product type description for human-readable narrative.</summary>
        internal static readonly Dictionary<string, string> ProductDescriptions = new Dictionary<string, string>
        {
            // Mechanical
            { "AHU", "Air Handling Unit" }, { "FCU", "Fan Coil Unit" },
            { "VAV", "Variable Air Volume Box" }, { "CHR", "Chiller" },
            { "BLR", "Boiler" }, { "PMP", "Pump" }, { "FAN", "Fan" },
            { "HRU", "Heat Recovery Unit" }, { "SPL", "Split System Unit" },
            { "IND", "Induction Unit" }, { "RAD", "Radiant Panel" },
            { "DAM", "Damper" }, { "CLT", "Cooling Tower" },
            { "VFD", "Variable Frequency Drive" },
            // Electrical
            { "DB", "Distribution Board" }, { "MCC", "Motor Control Centre" },
            { "MSB", "Main Switchboard" }, { "SWB", "Switchboard" },
            { "UPS", "Uninterruptible Power Supply" }, { "TRF", "Transformer" },
            { "GEN", "Generator" }, { "ATS", "Automatic Transfer Switch" },
            { "SPD", "Surge Protection Device" }, { "RCD", "Residual Current Device" },
            { "ISO", "Isolator" }, { "SFS", "Soft Starter" }, { "BKP", "Battery Backup" },
            // Lighting
            { "LUM", "Luminaire" }, { "EML", "Emergency Luminaire" },
            { "TRK", "Track Luminaire" }, { "DEC", "Decorative Luminaire" },
            { "DWN", "Downlight" }, { "LIN", "Linear Luminaire" },
            { "SPT", "Spotlight" }, { "WSH", "Wall Washer" },
            { "BOL", "Bollard Light" }, { "UPL", "Uplighter" }, { "FLD", "Floodlight" },
            // Plumbing
            { "WC", "Water Closet" }, { "WHB", "Wash Hand Basin" },
            { "URN", "Urinal" }, { "SNK", "Sink" }, { "SHW", "Shower" },
            { "BTH", "Bath" }, { "DRK", "Drinking Fountain" },
            { "CWL", "Water Cooler" }, { "TRP", "Grease Trap" },
            { "BID", "Bidet" }, { "EWS", "Eyewash Station" }, { "MOP", "Mop Sink" },
            // Fire
            { "SML", "Smoke Detector" }, { "MCP", "Manual Call Point" },
            { "BLL", "Fire Bell or Sounder" }, { "STB", "Strobe Beacon" },
            { "HTD", "Heat Detector" }, { "FIM", "Fire Interface Module" },
            { "SPR", "Sprinkler Head" }, { "FAD", "Fire Alarm Device" },
            // Valves
            { "BLV", "Balancing Valve" }, { "TRV", "Thermostatic Radiator Valve" },
            { "IVL", "Isolation Valve" }, { "NRV", "Non-Return Valve" },
            { "PRV", "Pressure Reducing Valve" }, { "STN", "Strainer" },
            // Building Elements
            { "WL", "Wall" }, { "FL", "Floor" }, { "CLG", "Ceiling" },
            { "RF", "Roof" }, { "DR", "Door" }, { "WN", "Window" },
            { "COL", "Column" }, { "BMG", "Beam" }, { "FND", "Foundation" },
            { "STR", "Staircase" }, { "RMP", "Ramp" }, { "RLG", "Railing" },
            { "FUR", "Furniture" }, { "CSW", "Casework" },
            // MEP Elements
            { "DCT", "Ductwork" }, { "PPE", "Pipework" },
            { "CDT", "Conduit" }, { "CTR", "Cable Tray" },
            { "ATR", "Air Terminal" }, { "ACC", "Accessory" },
        };

        /// <summary>
        /// Build TAG7: a comprehensive, richly descriptive asset narrative with embedded
        /// markup tokens for rich rendering across all 5 output surfaces.
        ///
        /// Returns a Tag7Result containing:
        ///   - MarkedUpNarrative: full narrative with «H»/«L»/«V» markup tokens
        ///   - PlainNarrative: same narrative without markup (parameter storage fallback)
        ///   - SectionA-F: individual plain sections for TAG7A-TAG7F sub-parameters
        ///
        /// Markup tokens:
        ///   «H»text«/H» — Header (Bold+Underline in TextNote, Bold in WPF, &lt;strong&gt; in HTML)
        ///   «L»text«/L» — Label (Italic in TextNote, muted color in WPF, &lt;em&gt; in HTML)
        ///   «V»text«/V» — Value (accent color in WPF, highlighted in HTML)
        /// </summary>
        public static Tag7Result BuildTag7Sections(Document doc, Element el, string categoryName, string[] tokenValues)
        {
            var result = new Tag7Result();
            if (tokenValues == null) return result;
            var markedSections = new List<string>();

            string disc = tokenValues.Length > 0 ? tokenValues[0] : "";
            string loc  = tokenValues.Length > 1 ? tokenValues[1] : "";
            string zone = tokenValues.Length > 2 ? tokenValues[2] : "";
            string lvl  = tokenValues.Length > 3 ? tokenValues[3] : "";
            string sys  = tokenValues.Length > 4 ? tokenValues[4] : "";
            string func = tokenValues.Length > 5 ? tokenValues[5] : "";
            string prod = tokenValues.Length > 6 ? tokenValues[6] : "";
            string seq  = tokenValues.Length > 7 ? tokenValues[7] : "";

            // ── Section A: Asset Identity and Classification ──────────────────
            string discDesc = DisciplineDescriptions.TryGetValue(disc, out string dd) ? dd : disc;
            string prodDesc = ProductDescriptions.TryGetValue(prod, out string pd) ? pd : "";
            string familyName = ParameterHelpers.GetString(el, ParamRegistry.FAMILY_NAME);
            string typeName   = ParameterHelpers.GetString(el, ParamRegistry.TYPE_NAME);
            string description = ParameterHelpers.GetString(el, ParamRegistry.DESC);
            string mfr    = ParameterHelpers.GetString(el, ParamRegistry.MFR);
            string model  = ParameterHelpers.GetString(el, ParamRegistry.MODEL);
            string size   = ParameterHelpers.GetString(el, ParamRegistry.SIZE);

            var identityPlain = new System.Text.StringBuilder();
            var identityMarked = new System.Text.StringBuilder();

            // Asset name (BOLD in marked)
            string assetName = discDesc;
            if (!string.IsNullOrEmpty(prodDesc))
                assetName += $" {prodDesc}";
            else if (!string.IsNullOrEmpty(categoryName))
                assetName += $" {categoryName}";
            if (!string.IsNullOrEmpty(prod))
                assetName += $" ({prod})";

            identityPlain.Append(assetName);
            identityMarked.Append($"\u00ABH\u00BB{assetName}\u00AB/H\u00BB");

            if (!string.IsNullOrEmpty(mfr) || !string.IsNullOrEmpty(model))
            {
                string mfrText = " manufactured by ";
                if (!string.IsNullOrEmpty(mfr))
                    mfrText += mfr;
                if (!string.IsNullOrEmpty(model))
                {
                    if (!string.IsNullOrEmpty(mfr)) mfrText += " ";
                    mfrText += $"Model {model}";
                }
                identityPlain.Append(mfrText);
                identityMarked.Append($" \u00ABL\u00BBmanufactured by\u00AB/L\u00BB \u00ABV\u00BB{(mfr + " " + (string.IsNullOrEmpty(model) ? "" : $"Model {model}")).Trim()}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(familyName) && string.IsNullOrEmpty(mfr) && string.IsNullOrEmpty(model))
            {
                identityPlain.Append($" from the {familyName} family");
                identityMarked.Append($" \u00ABL\u00BBfrom the\u00AB/L\u00BB \u00ABV\u00BB{familyName}\u00AB/V\u00BB \u00ABL\u00BBfamily\u00AB/L\u00BB");
                if (!string.IsNullOrEmpty(typeName))
                {
                    identityPlain.Append($" configured as {typeName}");
                    identityMarked.Append($" \u00ABL\u00BBconfigured as\u00AB/L\u00BB \u00ABV\u00BB{typeName}\u00AB/V\u00BB");
                }
            }
            if (!string.IsNullOrEmpty(description))
            {
                identityPlain.Append($" described as {description}");
                identityMarked.Append($" \u00ABL\u00BBdescribed as\u00AB/L\u00BB \u00ABV\u00BB{description}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(size))
            {
                identityPlain.Append($" sized at {size}");
                identityMarked.Append($" \u00ABL\u00BBsized at\u00AB/L\u00BB \u00ABV\u00BB{size}\u00AB/V\u00BB");
            }

            result.SectionA = identityPlain.ToString().Trim();
            markedSections.Add(identityMarked.ToString().Trim());

            // ── Section B: System and Function Context ────────────────────────
            string sysDesc  = SystemDescriptions.TryGetValue(sys, out string sd) ? sd : sys;
            string funcDesc = FunctionDescriptions.TryGetValue(func, out string fd) ? fd : func;

            if (!string.IsNullOrEmpty(sysDesc))
            {
                var sysPlain = new System.Text.StringBuilder(sysDesc);
                var sysMarked = new System.Text.StringBuilder($"\u00ABH\u00BB{sysDesc}\u00AB/H\u00BB");
                if (!string.IsNullOrEmpty(funcDesc) && funcDesc != sysDesc)
                {
                    sysPlain.Append($" providing {funcDesc}");
                    sysMarked.Append($" \u00ABL\u00BBproviding\u00AB/L\u00BB \u00ABV\u00BB{funcDesc}\u00AB/V\u00BB");
                }
                string servingText = $" serving Zone {zone} on Level {lvl} within Building {loc}";
                sysPlain.Append(servingText);
                sysMarked.Append($" \u00ABL\u00BBserving\u00AB/L\u00BB Zone \u00ABV\u00BB{zone}\u00AB/V\u00BB \u00ABL\u00BBon\u00AB/L\u00BB Level \u00ABV\u00BB{lvl}\u00AB/V\u00BB \u00ABL\u00BBwithin\u00AB/L\u00BB Building \u00ABV\u00BB{loc}\u00AB/V\u00BB");

                result.SectionB = sysPlain.ToString();
                markedSections.Add(sysMarked.ToString());
            }

            // ── Section C: Spatial Context and Room Information ────────────────
            string roomName = ParameterHelpers.GetString(el, ParamRegistry.ROOM_NAME);
            string roomNum  = ParameterHelpers.GetString(el, ParamRegistry.ROOM_NUM);
            string dept     = ParameterHelpers.GetString(el, ParamRegistry.DEPT);
            string gridRef  = ParameterHelpers.GetString(el, ParamRegistry.GRID_REF);
            string bleRoom  = ParameterHelpers.GetString(el, ParamRegistry.BLE_ROOM_NAME);
            string bleNum   = ParameterHelpers.GetString(el, ParamRegistry.BLE_ROOM_NUM);
            if (string.IsNullOrEmpty(roomName) && !string.IsNullOrEmpty(bleRoom)) roomName = bleRoom;
            if (string.IsNullOrEmpty(roomNum) && !string.IsNullOrEmpty(bleNum)) roomNum = bleNum;

            if (!string.IsNullOrEmpty(roomName) || !string.IsNullOrEmpty(gridRef))
            {
                var spatialPlain = new System.Text.StringBuilder("Located in ");
                var spatialMarked = new System.Text.StringBuilder("\u00ABL\u00BBLocated in\u00AB/L\u00BB ");
                if (!string.IsNullOrEmpty(roomName))
                {
                    spatialPlain.Append(roomName);
                    spatialMarked.Append($"\u00ABV\u00BB{roomName}\u00AB/V\u00BB");
                    if (!string.IsNullOrEmpty(roomNum))
                    {
                        spatialPlain.Append($" (Room {roomNum})");
                        spatialMarked.Append($" (Room \u00ABV\u00BB{roomNum}\u00AB/V\u00BB)");
                    }
                }
                if (!string.IsNullOrEmpty(dept))
                {
                    spatialPlain.Append($" within the {dept} department");
                    spatialMarked.Append($" \u00ABL\u00BBwithin the\u00AB/L\u00BB \u00ABV\u00BB{dept}\u00AB/V\u00BB \u00ABL\u00BBdepartment\u00AB/L\u00BB");
                }
                if (!string.IsNullOrEmpty(gridRef))
                {
                    spatialPlain.Append($" near grid reference {gridRef}");
                    spatialMarked.Append($" \u00ABL\u00BBnear grid reference\u00AB/L\u00BB \u00ABV\u00BB{gridRef}\u00AB/V\u00BB");
                }
                result.SectionC = spatialPlain.ToString();
                markedSections.Add(spatialMarked.ToString());
            }

            // ── Section D: Lifecycle Status, Revision, Origin, Workset, Phase,
            //    Design Option, Maintenance, Commissioning ─────────────────────
            string status  = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
            string rev     = ParameterHelpers.GetString(el, ParamRegistry.REV);
            string origin  = ParameterHelpers.GetString(el, ParamRegistry.ORIGIN);
            string project = ParameterHelpers.GetString(el, ParamRegistry.PROJECT);
            string volume  = ParameterHelpers.GetString(el, ParamRegistry.VOLUME);
            string mntType = ParameterHelpers.GetString(el, ParamRegistry.MNT_TYPE);
            string detailNum = ParameterHelpers.GetString(el, ParamRegistry.DETAIL_NUM);
            // New exploited parameters — workset, phase, design option, maintenance, commissioning
            string workset       = ParameterHelpers.GetString(el, "ASS_WORKSET_TXT");
            string phaseCreated  = ParameterHelpers.GetString(el, "ASS_PHASE_CREATED_TXT");
            string designOption  = ParameterHelpers.GetString(el, "ASS_DESIGN_OPTION_TXT");
            string mntFreq       = ParameterHelpers.GetString(el, "MNT_FREQUENCY_TXT");
            string mntWarranty   = ParameterHelpers.GetString(el, "MNT_WARRANTY_EXPIRY_TXT");
            string comStatus     = ParameterHelpers.GetString(el, "COM_COMMISSION_STATUS_TXT");
            string expectedLife  = ParameterHelpers.GetString(el, "PER_EXPECTED_LIFE_YEARS");
            string accessReqs    = ParameterHelpers.GetString(el, "MNT_ACCESS_REQUIREMENTS_TXT");

            var lifecyclePlain = new System.Text.StringBuilder();
            var lifecycleMarked = new System.Text.StringBuilder();
            // Use natural language connectors instead of just commas
            if (!string.IsNullOrEmpty(status))
            {
                lifecyclePlain.Append($"This element is {status.ToLower()}");
                lifecycleMarked.Append($"This element is \u00ABV\u00BB{status.ToLower()}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(rev))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", currently at "); lifecycleMarked.Append(", currently at "); }
                lifecyclePlain.Append($"revision {rev}");
                lifecycleMarked.Append($"\u00ABL\u00BBrevision\u00AB/L\u00BB \u00ABV\u00BB{rev}\u00AB/V\u00BB");
                // Add tag timestamp for audit trail
                string tagDate = DateTime.Now.ToString("yyyy-MM-dd");
                lifecyclePlain.Append($" (tagged {tagDate})");
                lifecycleMarked.Append($" (\u00ABL\u00BBtagged\u00AB/L\u00BB \u00ABV\u00BB{tagDate}\u00AB/V\u00BB)");
            }
            if (!string.IsNullOrEmpty(origin))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", originating from "); lifecycleMarked.Append(", originating from "); }
                lifecyclePlain.Append(origin);
                lifecycleMarked.Append($"\u00ABV\u00BB{origin}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(project))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(" within "); lifecycleMarked.Append(" within "); }
                else { lifecyclePlain.Append("Part of "); lifecycleMarked.Append("Part of "); }
                lifecyclePlain.Append($"project {project}");
                lifecycleMarked.Append($"\u00ABL\u00BBproject\u00AB/L\u00BB \u00ABV\u00BB{project}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(volume))
            {
                lifecyclePlain.Append($" (volume {volume})");
                lifecycleMarked.Append($" (\u00ABL\u00BBvolume\u00AB/L\u00BB \u00ABV\u00BB{volume}\u00AB/V\u00BB)");
            }
            // Workset and phase — unexploited data not in schedules
            if (!string.IsNullOrEmpty(workset))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(". "); lifecycleMarked.Append(". "); }
                lifecyclePlain.Append($"Managed under the {workset} workset");
                lifecycleMarked.Append($"Managed under the \u00ABV\u00BB{workset}\u00AB/V\u00BB \u00ABL\u00BBworkset\u00AB/L\u00BB");
            }
            if (!string.IsNullOrEmpty(phaseCreated))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", created during the "); lifecycleMarked.Append(", created during the "); }
                lifecyclePlain.Append($"{phaseCreated} phase");
                lifecycleMarked.Append($"\u00ABV\u00BB{phaseCreated}\u00AB/V\u00BB \u00ABL\u00BBphase\u00AB/L\u00BB");
            }
            if (!string.IsNullOrEmpty(designOption))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", part of design option "); lifecycleMarked.Append(", part of \u00ABL\u00BBdesign option\u00AB/L\u00BB "); }
                lifecyclePlain.Append(designOption);
                lifecycleMarked.Append($"\u00ABV\u00BB{designOption}\u00AB/V\u00BB");
            }
            // Maintenance and FM
            if (!string.IsNullOrEmpty(mntType))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(". "); lifecycleMarked.Append(". "); }
                lifecyclePlain.Append($"Requires {mntType.ToLower()} maintenance");
                lifecycleMarked.Append($"Requires \u00ABV\u00BB{mntType.ToLower()}\u00AB/V\u00BB \u00ABL\u00BBmaintenance\u00AB/L\u00BB");
            }
            if (!string.IsNullOrEmpty(mntFreq))
            {
                if (!string.IsNullOrEmpty(mntType))
                {
                    lifecyclePlain.Append($" on a {mntFreq.ToLower()} basis");
                    lifecycleMarked.Append($" on a \u00ABV\u00BB{mntFreq.ToLower()}\u00AB/V\u00BB basis");
                }
                else if (lifecyclePlain.Length > 0)
                {
                    lifecyclePlain.Append($". Maintenance frequency: {mntFreq.ToLower()}");
                    lifecycleMarked.Append($". \u00ABL\u00BBMaintenance frequency:\u00AB/L\u00BB \u00ABV\u00BB{mntFreq.ToLower()}\u00AB/V\u00BB");
                }
            }
            if (!string.IsNullOrEmpty(expectedLife))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", with an expected service life of "); lifecycleMarked.Append(", with an expected \u00ABL\u00BBservice life\u00AB/L\u00BB of "); }
                lifecyclePlain.Append($"{expectedLife} years");
                lifecycleMarked.Append($"\u00ABV\u00BB{expectedLife}\u00AB/V\u00BB years");
            }
            if (!string.IsNullOrEmpty(mntWarranty))
            {
                lifecyclePlain.Append($" (warranty expires {mntWarranty})");
                lifecycleMarked.Append($" (\u00ABL\u00BBwarranty expires\u00AB/L\u00BB \u00ABV\u00BB{mntWarranty}\u00AB/V\u00BB)");
            }
            if (!string.IsNullOrEmpty(accessReqs))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(". "); lifecycleMarked.Append(". "); }
                lifecyclePlain.Append($"Maintenance access requires {accessReqs.ToLower()}");
                lifecycleMarked.Append($"\u00ABL\u00BBMaintenance access requires\u00AB/L\u00BB \u00ABV\u00BB{accessReqs.ToLower()}\u00AB/V\u00BB");
            }
            // Commissioning status
            if (!string.IsNullOrEmpty(comStatus))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(". "); lifecycleMarked.Append(". "); }
                lifecyclePlain.Append($"Commissioning status: {comStatus.ToLower()}");
                lifecycleMarked.Append($"\u00ABL\u00BBCommissioning status:\u00AB/L\u00BB \u00ABV\u00BB{comStatus.ToLower()}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(detailNum))
            {
                if (lifecyclePlain.Length > 0) { lifecyclePlain.Append(", see "); lifecycleMarked.Append(", see "); }
                lifecyclePlain.Append($"detail {detailNum}");
                lifecycleMarked.Append($"\u00ABL\u00BBdetail\u00AB/L\u00BB \u00ABV\u00BB{detailNum}\u00AB/V\u00BB");
            }

            if (lifecyclePlain.Length > 0)
            {
                result.SectionD = lifecyclePlain.ToString();
                markedSections.Add(lifecycleMarked.ToString());
            }

            // ── Section E: Technical Data (discipline-specific + dimensions) ──
            string techData = BuildDisciplineTechSection(el, disc, categoryName);
            string dimData = BuildDimensionalSection(el, categoryName);
            var techPlain = new System.Text.StringBuilder();
            var techMarked = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(techData))
            {
                techPlain.Append(techData);
                // Build marked version with label/value pairs
                techMarked.Append(BuildMarkedTechSection(el, disc, categoryName));
            }
            if (!string.IsNullOrEmpty(dimData))
            {
                if (techPlain.Length > 0) { techPlain.Append(". In terms of its dimensions, it is "); techMarked.Append(". \u00ABC\u00BBIn terms of its dimensions, it is\u00AB/C\u00BB "); }
                techPlain.Append(dimData);
                techMarked.Append(BuildMarkedDimSection(el, categoryName));
            }
            // Append room finishes if this is a room or spatial element
            if (categoryName == "Rooms" || categoryName == "Spaces")
            {
                string finFlr = ParameterHelpers.GetString(el, ParamRegistry.ROOM_FINISH_FLR);
                string finWall = ParameterHelpers.GetString(el, ParamRegistry.ROOM_FINISH_WALL);
                string finClg = ParameterHelpers.GetString(el, ParamRegistry.ROOM_FINISH_CLG);
                string finBase = ParameterHelpers.GetString(el, ParamRegistry.ROOM_FINISH_BASE);
                if (!string.IsNullOrEmpty(finFlr) || !string.IsNullOrEmpty(finWall) || !string.IsNullOrEmpty(finClg))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append("Room finishes include");
                    techMarked.Append("\u00ABL\u00BBRoom finishes include\u00AB/L\u00BB");
                    if (!string.IsNullOrEmpty(finFlr))
                    {
                        techPlain.Append($" floor: {finFlr}");
                        techMarked.Append($" \u00ABL\u00BBfloor:\u00AB/L\u00BB \u00ABV\u00BB{finFlr}\u00AB/V\u00BB");
                    }
                    if (!string.IsNullOrEmpty(finWall))
                    {
                        techPlain.Append($", walls: {finWall}");
                        techMarked.Append($", \u00ABL\u00BBwalls:\u00AB/L\u00BB \u00ABV\u00BB{finWall}\u00AB/V\u00BB");
                    }
                    if (!string.IsNullOrEmpty(finClg))
                    {
                        techPlain.Append($", ceiling: {finClg}");
                        techMarked.Append($", \u00ABL\u00BBceiling:\u00AB/L\u00BB \u00ABV\u00BB{finClg}\u00AB/V\u00BB");
                    }
                    if (!string.IsNullOrEmpty(finBase))
                    {
                        techPlain.Append($", base: {finBase}");
                        techMarked.Append($", \u00ABL\u00BBbase:\u00AB/L\u00BB \u00ABV\u00BB{finBase}\u00AB/V\u00BB");
                    }
                }
            }
            // Append door function and head height
            if (categoryName == "Doors")
            {
                string doorFunc = ParameterHelpers.GetString(el, ParamRegistry.DOOR_FUNC);
                string doorHead = ParameterHelpers.GetString(el, ParamRegistry.DOOR_HEAD_HT);
                if (!string.IsNullOrEmpty(doorFunc))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Functioning as a {doorFunc.ToLower()} door");
                    techMarked.Append($"\u00ABC\u00BBFunctioning as a\u00AB/C\u00BB \u00ABV\u00BB{doorFunc.ToLower()}\u00AB/V\u00BB \u00ABL\u00BBdoor\u00AB/L\u00BB");
                }
                if (!string.IsNullOrEmpty(doorHead))
                {
                    if (techPlain.Length > 0) { techPlain.Append($" with head height at {doorHead} mm"); techMarked.Append($" with \u00ABL\u00BBhead height\u00AB/L\u00BB at \u00ABV\u00BB{doorHead} mm\u00AB/V\u00BB"); }
                }
            }
            // Append window head height
            if (categoryName == "Windows")
            {
                string winHead = ParameterHelpers.GetString(el, ParamRegistry.WINDOW_HEAD_HT);
                if (!string.IsNullOrEmpty(winHead))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Window head height at {winHead} mm");
                    techMarked.Append($"\u00ABL\u00BBWindow head height\u00AB/L\u00BB at \u00ABV\u00BB{winHead} mm\u00AB/V\u00BB");
                }
            }
            // Append fire rating for any element that has it
            {
                string fr = ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING);
                if (!string.IsNullOrEmpty(fr) && categoryName != "Rooms" && categoryName != "Spaces")
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Fire resistance rated at {fr} minutes");
                    techMarked.Append($"\u00ABL\u00BBFire resistance rated at\u00AB/L\u00BB \u00ABV\u00BB{fr} minutes\u00AB/V\u00BB");
                }
            }
            // Append sustainability data if available
            {
                string carbonFp = ParameterHelpers.GetString(el, "PER_SUST_CARBON_FOOTPRINT_KG");
                string recyclability = ParameterHelpers.GetString(el, "PER_RECYCLABILITY_PCT");
                if (!string.IsNullOrEmpty(carbonFp))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Embodied carbon: {carbonFp} kgCO₂e");
                    techMarked.Append($"\u00ABL\u00BBEmbodied carbon:\u00AB/L\u00BB \u00ABV\u00BB{carbonFp} kgCO₂e\u00AB/V\u00BB");
                }
                if (!string.IsNullOrEmpty(recyclability))
                {
                    if (techPlain.Length > 0) { techPlain.Append($", {recyclability}% recyclable"); techMarked.Append($", \u00ABV\u00BB{recyclability}%\u00AB/V\u00BB \u00ABL\u00BBrecyclable\u00AB/L\u00BB"); }
                }
            }
            // Append acoustic data
            {
                string stc = ParameterHelpers.GetString(el, "PER_ACOUSTIC_WALL_STC");
                string iic = ParameterHelpers.GetString(el, "PER_ACOUSTIC_FLOOR_IIC");
                if (!string.IsNullOrEmpty(stc))
                {
                    if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                    techPlain.Append($"Acoustic rating STC {stc}");
                    techMarked.Append($"\u00ABL\u00BBAcoustic rating STC\u00AB/L\u00BB \u00ABV\u00BB{stc}\u00AB/V\u00BB");
                }
                if (!string.IsNullOrEmpty(iic))
                {
                    if (!string.IsNullOrEmpty(stc)) { techPlain.Append($", IIC {iic}"); techMarked.Append($", \u00ABL\u00BBIIC\u00AB/L\u00BB \u00ABV\u00BB{iic}\u00AB/V\u00BB"); }
                    else
                    {
                        if (techPlain.Length > 0) { techPlain.Append(". "); techMarked.Append(". "); }
                        techPlain.Append($"Acoustic rating IIC {iic}");
                        techMarked.Append($"\u00ABL\u00BBAcoustic rating IIC\u00AB/L\u00BB \u00ABV\u00BB{iic}\u00AB/V\u00BB");
                    }
                }
            }

            if (techPlain.Length > 0)
            {
                result.SectionE = techPlain.ToString();
                markedSections.Add(techMarked.ToString());
            }

            // ── Section F: Classification + Cost + ISO Reference ──────────────
            string uniformat     = ParameterHelpers.GetString(el, ParamRegistry.UNIFORMAT);
            string uniformatDesc = ParameterHelpers.GetString(el, ParamRegistry.UNIFORMAT_DESC);
            string omniclass     = ParameterHelpers.GetString(el, ParamRegistry.OMNICLASS);
            string keynote       = ParameterHelpers.GetString(el, ParamRegistry.KEYNOTE);
            string typeMark      = ParameterHelpers.GetString(el, ParamRegistry.TYPE_MARK);
            string cost          = ParameterHelpers.GetString(el, ParamRegistry.COST);

            var classPlain = new System.Text.StringBuilder();
            var classMarked = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(uniformat))
            {
                classPlain.Append($"Uniformat code {uniformat}");
                classMarked.Append($"\u00ABL\u00BBUniformat code\u00AB/L\u00BB \u00ABV\u00BB{uniformat}\u00AB/V\u00BB");
                if (!string.IsNullOrEmpty(uniformatDesc))
                {
                    classPlain.Append($" ({uniformatDesc})");
                    classMarked.Append($" ({uniformatDesc})");
                }
            }
            if (!string.IsNullOrEmpty(omniclass))
            {
                if (classPlain.Length > 0) { classPlain.Append(", with OmniClass reference "); classMarked.Append(", with \u00ABL\u00BBOmniClass reference\u00AB/L\u00BB "); }
                else { classPlain.Append("OmniClass reference "); classMarked.Append("\u00ABL\u00BBOmniClass reference\u00AB/L\u00BB "); }
                classPlain.Append(omniclass);
                classMarked.Append($"\u00ABV\u00BB{omniclass}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(keynote))
            {
                if (classPlain.Length > 0) { classPlain.Append(", keynote "); classMarked.Append(", \u00ABL\u00BBkeynote\u00AB/L\u00BB "); }
                else { classPlain.Append("Keynote "); classMarked.Append("\u00ABL\u00BBKeynote\u00AB/L\u00BB "); }
                classPlain.Append(keynote);
                classMarked.Append($"\u00ABV\u00BB{keynote}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(typeMark))
            {
                if (classPlain.Length > 0) { classPlain.Append(", identified as type mark "); classMarked.Append(", identified as \u00ABL\u00BBtype mark\u00AB/L\u00BB "); }
                else { classPlain.Append("Type mark "); classMarked.Append("\u00ABL\u00BBType mark\u00AB/L\u00BB "); }
                classPlain.Append(typeMark);
                classMarked.Append($"\u00ABV\u00BB{typeMark}\u00AB/V\u00BB");
            }
            if (!string.IsNullOrEmpty(cost))
            {
                if (classPlain.Length > 0) { classPlain.Append(", with an estimated unit cost of "); classMarked.Append(", with an estimated \u00ABL\u00BBunit cost\u00AB/L\u00BB of "); }
                else { classPlain.Append("Estimated unit cost of "); classMarked.Append("Estimated \u00ABL\u00BBunit cost\u00AB/L\u00BB of "); }
                classPlain.Append(cost);
                classMarked.Append($"\u00ABV\u00BB{cost}\u00AB/V\u00BB");
            }
            // Type comments — unexploited rich text from Revit type properties
            string typeComments = ParameterHelpers.GetString(el, ParamRegistry.TYPE_COMMENTS);
            if (!string.IsNullOrEmpty(typeComments))
            {
                if (classPlain.Length > 0) { classPlain.Append(". Type notes: "); classMarked.Append(". \u00ABL\u00BBType notes:\u00AB/L\u00BB "); }
                else { classPlain.Append("Type notes: "); classMarked.Append("\u00ABL\u00BBType notes:\u00AB/L\u00BB "); }
                classPlain.Append(typeComments);
                classMarked.Append($"\u00ABV\u00BB{typeComments}\u00AB/V\u00BB");
            }

            // prefer ASS_TAG_1 (already assembled by BuildAndWriteTag,
            // the single source of truth for tag composition) when it is
            // populated. Falls back to inline re-assembly only when the
            // canonical tag has not been built yet — avoids divergence
            // between Section F and the assembled tag.
            string fullTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
            if (string.IsNullOrEmpty(fullTag))
            {
                // S02 defensive guards — trap upstream token corruption so the narrative stays readable
                // even when a PROD/SYS/etc. writer accidentally concatenated multiple descriptors into
                // one token slot, or when TagPrefix/TagSuffix already appears in the joined string.
                string[] isoTokens = new string[tokenValues.Length];
                for (int i = 0; i < tokenValues.Length; i++)
                {
                    string v = tokenValues[i];
                    if (!string.IsNullOrEmpty(v) && !string.IsNullOrEmpty(Separator) && v.Contains(Separator))
                    {
                        StingLog.Warn($"BuildTag7Sections: token[{i}]='{v}' contains separator '{Separator}'. " +
                                      $"ElementId={el?.Id}. Truncating to first segment.");
                        v = v.Split(new[] { Separator }, 2, StringSplitOptions.None)[0];
                    }
                    isoTokens[i] = v;
                }
                fullTag = string.Join(Separator, isoTokens);
                if (!string.IsNullOrEmpty(TagPrefix) &&
                    !fullTag.StartsWith(TagPrefix + Separator, StringComparison.Ordinal) &&
                    !fullTag.StartsWith(TagPrefix, StringComparison.Ordinal))
                {
                    fullTag = TagPrefix + Separator + fullTag;
                }
                if (!string.IsNullOrEmpty(TagSuffix) &&
                    !fullTag.EndsWith(Separator + TagSuffix, StringComparison.Ordinal) &&
                    !fullTag.EndsWith(TagSuffix, StringComparison.Ordinal))
                {
                    fullTag = fullTag + Separator + TagSuffix;
                }
            }
            if (classPlain.Length > 0) { classPlain.Append(". Assigned "); classMarked.Append(". Assigned "); }
            classPlain.Append($"ISO 19650 tag {fullTag}");
            classMarked.Append($"\u00ABL\u00BBISO 19650 tag\u00AB/L\u00BB \u00ABH\u00BB{fullTag}\u00AB/H\u00BB");

            result.SectionF = classPlain.ToString();
            markedSections.Add(classMarked.ToString());

            // ── Assemble final narratives with meaningful connecting phrases ──
            // Instead of pipe separators, connect sections with logical transition words
            // that form a coherent description of the asset.
            var plainParts = new System.Text.StringBuilder();
            var markedParts = new System.Text.StringBuilder();

            // A: Identity — opening statement, no prefix needed
            if (!string.IsNullOrEmpty(result.SectionA))
            {
                plainParts.Append(result.SectionA);
                markedParts.Append(markedSections.Count > 0 ? markedSections[0] : result.SectionA);
            }
            // Use a running index to track position in markedSections (only non-empty sections are added)
            int mIdx = 1; // 0 = Section A (already consumed above)
            // B: System context — connects with ". This asset operates within"
            if (!string.IsNullOrEmpty(result.SectionB))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". This asset operates within the ");
                    markedParts.Append(". \u00ABC\u00BBThis asset operates within the\u00AB/C\u00BB ");
                }
                plainParts.Append(result.SectionB);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionB);
                mIdx++;
            }
            // C: Spatial — connects with ". It is" (SectionC already starts with "Located in")
            if (!string.IsNullOrEmpty(result.SectionC))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". It is ");
                    markedParts.Append(". \u00ABC\u00BBIt is\u00AB/C\u00BB ");
                    // SectionC starts with "Located in" — lowercase it after "It is"
                    result.SectionC = char.ToLower(result.SectionC[0]) + result.SectionC.Substring(1);
                    // Also lowercase the marked section
                    if (markedSections.Count > mIdx)
                    {
                        string mc = markedSections[mIdx];
                        mc = mc.Replace("\u00ABL\u00BBLocated in\u00AB/L\u00BB", "\u00ABL\u00BBlocated in\u00AB/L\u00BB");
                        markedSections[mIdx] = mc;
                    }
                }
                plainParts.Append(result.SectionC);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionC);
                mIdx++;
            }
            // D: Lifecycle — connects with ". Regarding its lifecycle,"
            if (!string.IsNullOrEmpty(result.SectionD))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". Regarding its lifecycle, ");
                    markedParts.Append(". \u00ABC\u00BBRegarding its lifecycle,\u00AB/C\u00BB ");
                    // SectionD starts with "This element is" — lowercase it after connector
                    if (result.SectionD.StartsWith("This element is"))
                        result.SectionD = "this element is" + result.SectionD.Substring("This element is".Length);
                    if (markedSections.Count > mIdx)
                    {
                        string md = markedSections[mIdx];
                        md = md.Replace("This element is", "this element is");
                        markedSections[mIdx] = md;
                    }
                }
                plainParts.Append(result.SectionD);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionD);
                mIdx++;
            }
            // E: Technical — connects with ". Technical specifications include"
            if (!string.IsNullOrEmpty(result.SectionE))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". Technical specifications include ");
                    markedParts.Append(". \u00ABC\u00BBTechnical specifications include\u00AB/C\u00BB ");
                }
                plainParts.Append(result.SectionE);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionE);
                mIdx++;
            }
            // F: Classification — connects with ". Classified under"
            if (!string.IsNullOrEmpty(result.SectionF))
            {
                if (plainParts.Length > 0)
                {
                    plainParts.Append(". Classified under ");
                    markedParts.Append(". \u00ABC\u00BBClassified under\u00AB/C\u00BB ");
                }
                plainParts.Append(result.SectionF);
                markedParts.Append(markedSections.Count > mIdx ? markedSections[mIdx] : result.SectionF);
            }

            result.PlainNarrative = plainParts.ToString();
            result.MarkedUpNarrative = markedParts.ToString();

            // ─── T4-T10 tier summaries (Phase 165) ────────────────────────
            // Read element data and build single-line summaries per tier. Each
            // builder is independently try/catch wrapped — a failure in one
            // tier never breaks TAG7A-TAG7F or the other tiers.
            BuildTier4To10Summaries(el, result);

            return result;
        }

        /// <summary>
        /// Phase 165 — assemble T4-T10 tier summaries from element parameters.
        /// Each tier reads a small group of shared parameters and formats a
        /// human-readable single-line summary. Empty tier → empty string
        /// (callers skip silently).
        /// </summary>
        private static void BuildTier4To10Summaries(Element el, Tag7Result result)
        {
            if (el == null) return;

            // T4 — Commissioning & handover (N-G16 QR workflow)
            try
            {
                string state    = ParameterHelpers.GetString(el, ParamRegistry.COMM_STATE_TXT);
                string date     = ParameterHelpers.GetString(el, ParamRegistry.COMM_DATE_TXT);
                string oper     = ParameterHelpers.GetString(el, ParamRegistry.COMM_OPERATIVE_TXT);
                string witness  = ParameterHelpers.GetString(el, ParamRegistry.COMM_WITNESS_TXT);
                string notes    = ParameterHelpers.GetString(el, ParamRegistry.COMM_NOTES_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(state))   parts.Add(state);
                if (!string.IsNullOrEmpty(date))    parts.Add(date);
                if (!string.IsNullOrEmpty(oper))    parts.Add($"by {oper}");
                if (!string.IsNullOrEmpty(witness)) parts.Add($"witness {witness}");
                if (!string.IsNullOrEmpty(notes))   parts.Add(notes);
                if (parts.Count > 0) result.SectionT4 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T4 failed: " + ex.Message); }

            // T5 — Cost & procurement
            try
            {
                string ugx      = ParameterHelpers.GetString(el, ParamRegistry.CST_UG_PRICE_UGX);
                string usd      = ParameterHelpers.GetString(el, ParamRegistry.CST_INTL_PRICE_USD);
                string quote    = ParameterHelpers.GetString(el, ParamRegistry.CST_QUOTE_REF_TXT);
                string hrs      = ParameterHelpers.GetString(el, ParamRegistry.CST_INSTALL_HRS);
                string crew     = ParameterHelpers.GetString(el, ParamRegistry.CST_LABOUR_CREW_TXT);
                string rate     = ParameterHelpers.GetString(el, ParamRegistry.CST_LABOUR_RATE_GBP);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(ugx))   parts.Add($"UGX {ugx}");
                if (!string.IsNullOrEmpty(usd))   parts.Add($"USD {usd}");
                if (!string.IsNullOrEmpty(quote)) parts.Add($"quote {quote}");
                if (!string.IsNullOrEmpty(hrs))   parts.Add($"{hrs} hrs install");
                if (!string.IsNullOrEmpty(crew))  parts.Add($"crew {crew}");
                if (!string.IsNullOrEmpty(rate))  parts.Add($"GBP {rate}/hr");
                if (parts.Count > 0) result.SectionT5 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T5 failed: " + ex.Message); }

            // T6 — Carbon & sustainability (BS EN 15978 lifecycle stages)
            try
            {
                string a13   = ParameterHelpers.GetString(el, ParamRegistry.CBN_A1_A3_KG_CO2E);
                string a4    = ParameterHelpers.GetString(el, ParamRegistry.CBN_A4_KG_CO2E);
                string a5    = ParameterHelpers.GetString(el, ParamRegistry.CBN_A5_KG_CO2E);
                string b6    = ParameterHelpers.GetString(el, ParamRegistry.CBN_B6_KG_CO2E_YR);
                string c1    = ParameterHelpers.GetString(el, ParamRegistry.CBN_C1_KG_CO2E);
                string c2    = ParameterHelpers.GetString(el, ParamRegistry.CBN_C2_KG_CO2E);
                string c34   = ParameterHelpers.GetString(el, ParamRegistry.CBN_C3_C4_KG_CO2E);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(a13)) parts.Add($"A1-A3 {a13} kgCO2e");
                if (!string.IsNullOrEmpty(a4))  parts.Add($"A4 {a4}");
                if (!string.IsNullOrEmpty(a5))  parts.Add($"A5 {a5}");
                if (!string.IsNullOrEmpty(b6))  parts.Add($"B6 {b6}/yr");
                if (!string.IsNullOrEmpty(c1))  parts.Add($"C1 {c1}");
                if (!string.IsNullOrEmpty(c2))  parts.Add($"C2 {c2}");
                if (!string.IsNullOrEmpty(c34)) parts.Add($"C3-C4 {c34}");
                if (parts.Count > 0) result.SectionT6 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T6 failed: " + ex.Message); }

            // T7 — Fabrication & QC
            try
            {
                string spool   = ParameterHelpers.GetString(el, ParamRegistry.ASS_SPOOL_NR_TXT);
                string status  = ParameterHelpers.GetString(el, ParamRegistry.ASS_FAB_STATUS_TXT);
                string insp    = ParameterHelpers.GetString(el, ParamRegistry.ASS_QC_INSPECTOR_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(spool))  parts.Add($"spool {spool}");
                if (!string.IsNullOrEmpty(status)) parts.Add(status);
                if (!string.IsNullOrEmpty(insp))   parts.Add($"insp {insp}");
                if (parts.Count > 0) result.SectionT7 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T7 failed: " + ex.Message); }

            // T8 — Clash triage & resolution
            try
            {
                string sev     = ParameterHelpers.GetString(el, ParamRegistry.CLASH_TRIAGE_SEVERITY_NR);
                string cat     = ParameterHelpers.GetString(el, ParamRegistry.CLASH_TRIAGE_CATEGORY_TXT);
                string resStat = ParameterHelpers.GetString(el, ParamRegistry.CLASH_RESOLUTION_STATUS_TXT);
                string score   = ParameterHelpers.GetString(el, ParamRegistry.CLASH_TRIAGE_SCORE);
                string action  = ParameterHelpers.GetString(el, ParamRegistry.CLASH_RESOLUTION_ACTION_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(sev))     parts.Add($"sev {sev}");
                if (!string.IsNullOrEmpty(cat))     parts.Add(cat);
                if (!string.IsNullOrEmpty(resStat)) parts.Add(resStat);
                if (!string.IsNullOrEmpty(score))   parts.Add($"score {score}");
                if (!string.IsNullOrEmpty(action))  parts.Add($"action: {action}");
                if (parts.Count > 0) result.SectionT8 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T8 failed: " + ex.Message); }

            // T9 — As-built reconciliation & model health
            try
            {
                string dev      = ParameterHelpers.GetString(el, ParamRegistry.ASBUILT_DEVIATION_MM);
                string capDate  = ParameterHelpers.GetString(el, ParamRegistry.ASBUILT_CAPTURE_DATE_TXT);
                string health   = ParameterHelpers.GetString(el, ParamRegistry.HEALTH_SCORE_LAST_NR);
                string healthDt = ParameterHelpers.GetString(el, ParamRegistry.HEALTH_SCORE_DATE_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(dev))      parts.Add($"Δ {dev} mm");
                if (!string.IsNullOrEmpty(capDate))  parts.Add($"captured {capDate}");
                if (!string.IsNullOrEmpty(health))   parts.Add($"health {health}");
                if (!string.IsNullOrEmpty(healthDt)) parts.Add($"on {healthDt}");
                if (parts.Count > 0) result.SectionT9 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T9 failed: " + ex.Message); }

            // T10 — Compliance / audit (IFC PSet + ACC round-trip)
            try
            {
                string pset    = ParameterHelpers.GetString(el, ParamRegistry.IFC_PSET_OVERRIDE_TXT);
                string accId   = ParameterHelpers.GetString(el, ParamRegistry.ACC_ISSUE_ID_TXT);
                string accStat = ParameterHelpers.GetString(el, ParamRegistry.ACC_SYNC_STATUS_TXT);
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(pset))    parts.Add($"IFC PSet: {pset}");
                if (!string.IsNullOrEmpty(accId))   parts.Add($"ACC #{accId}");
                if (!string.IsNullOrEmpty(accStat)) parts.Add($"sync {accStat}");
                if (parts.Count > 0) result.SectionT10 = string.Join(" • ", parts);
            }
            catch (Exception ex) { StingLog.Warn("BuildTier4To10Summaries T10 failed: " + ex.Message); }
        }

        /// <summary>
        /// Backward-compatible wrapper: returns the plain narrative string.
        /// All existing callers use this — returns exactly the same output as before.
        /// </summary>
        public static string BuildTag7Narrative(Document doc, Element el, string categoryName, string[] tokenValues)
        {
            return BuildTag7Sections(doc, el, categoryName, tokenValues).PlainNarrative;
        }

        /// <summary>
        /// Phase 165 — Issue #5. Mode-aware overload of BuildTag7Sections.
        ///
        /// Both modes return identical SectionA-C (Identity / System / Spatial)
        /// because T1-T3 are common. The remaining sections differ:
        ///
        ///  - <c>TagMode.DC</c> — SectionD/E/F = Lifecycle / Technical / Classification
        ///    (the System A TAG7D-F content). Tier4Summaries are still hydrated
        ///    from element parameters so the data is visible to both clients,
        ///    but in DC mode the consumer (WriteTag7All) writes only A-F.
        ///
        ///  - <c>TagMode.Handover</c> — Section D/E/F return the same plain
        ///    System A content, but the writer pulls T4-T10 from
        ///    <see cref="Tag7Result.Tier4Summaries"/> (T4=Commissioning,
        ///    T5=Cost, T6=Carbon, T7=Fab, T8=Clash, T9=AsBuilt, T10=Audit).
        ///
        ///  - <c>TagMode.Custom</c> — same shape as Handover; project supplies
        ///    its own T4-T10 mapping via project_config.json overrides.
        ///
        /// The method itself is mode-agnostic at read time — it just hydrates
        /// every section + every tier — so two calls return identical Tag7Result.
        /// The branch happens at write time in <see cref="WriteTag7All"/>.
        /// </summary>
        public static Tag7Result BuildTag7Sections(Document doc, Element el,
            string categoryName, string[] tokenValues, ParamRegistry.TagMode mode)
        {
            // Hydrate the Tag7Result the same way regardless of mode — the writer
            // selects which subset to persist based on `mode`. Centralising the
            // build keeps DC ↔ Handover round-trips lossless: switching modes
            // does NOT erase data that lives outside the active mode's tier set.
            var result = BuildTag7Sections(doc, el, categoryName, tokenValues);
            return result;
        }

        /// <summary>
        /// Write TAG7 + all sub-section parameters (TAG7A-TAG7F) for an element.
        /// Also writes warning text, and populates the category-specific paragraph container.
        /// Returns number of parameters written.
        ///
        /// Phase 165 — Issue #2. The writer is mode-aware: it reads
        /// <see cref="ParamRegistry.GetActiveTagMode"/> from the document and
        /// branches the T4-T10 surface:
        ///
        ///  - <c>TagMode.DC</c>     — writes TAG7A-F as today (System A).
        ///  - <c>TagMode.Handover</c> — writes TAG7A-C as today; appends T4-T10
        ///    via <see cref="Tag7Result.Tier4Summaries"/> read out of the System
        ///    B parameter groups (COMM_*, CST_*, CBN_*, FAB_*, CLH_*, ASB_*,
        ///    AUD_*) which were hydrated by BuildTier4To10Summaries.
        ///  - <c>TagMode.Custom</c>  — same surface as Handover; project-defined
        ///    payload via project_config.json.
        ///
        /// In every mode the assembled narrative is also written to
        /// <see cref="ParamRegistry.TAG7"/> as the combined human-readable form.
        ///
        /// Switching mode does NOT erase the other system's parameter data —
        /// only the visible TAG7A-F + appended tier set changes.
        /// </summary>
        public static int WriteTag7All(Document doc, Element el, string categoryName, string[] tokenValues, bool overwrite = true)
        {
            if (tokenValues == null || tokenValues.Length < 8) return 0;
            var tag7 = BuildTag7Sections(doc, el, categoryName, tokenValues);
            int written = 0;

            // Phase 165 — resolve active mode once per element-write so the
            // branch is stable for the duration of this call.
            ParamRegistry.TagMode activeMode = ParamRegistry.GetActiveTagMode(doc);

            // build the final TAG7 string locally before
            // the single write so the post-write read-back can be eliminated.
            // The previous code wrote TAG7, then re-read it just to append
            // warnings — wasted LookupParameter + GetString per element.
            string tag7Final = tag7.MarkedUpNarrative ?? "";

            // TAG7A-TAG7F get plain section text for tag family labels
            // Phase 165 — Issue #2. Mode branch:
            //   DC      → write all six (TAG7A-F = System A T1-T6)
            //   Handover → write only TAG7A-C (T1-T3 == identity/system/spatial,
            //              shared with DC). TAG7D-F are blanked because in
            //              Handover mode the tier 4-6 narrative is owned by
            //              the System B append (T4 commissioning / T5 cost /
            //              T6 carbon) appended below — leaving the old DC
            //              text in TAG7D-F would conflict.
            //   Custom  → same as Handover.
            string[] sectionParams = ParamRegistry.TAG7Sections;
            string[] sectionValues = tag7.AllSections;
            int sectionLimit = (activeMode == ParamRegistry.TagMode.DC)
                ? sectionParams.Length     // 6 — full A-F
                : 3;                       // Handover / Custom: A-C only
            for (int i = 0; i < sectionParams.Length && i < sectionValues.Length; i++)
            {
                if (i < sectionLimit)
                {
                    if (!string.IsNullOrEmpty(sectionValues[i]))
                    {
                        if (ParameterHelpers.SetString(el, sectionParams[i], sectionValues[i], overwrite))
                            written++;
                    }
                }
                else
                {
                    // Issue #22 — Handover/Custom: blank D-F so old DC content
                    // doesn't shadow the System B tier appends that follow.
                    // Phase 165 perf — CachedLookup avoids per-instance
                    // LookupParameter cost.
                    Parameter p = ParameterHelpers.CachedLookup(el, sectionParams[i]);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    {
                        string cur = p.AsString();
                        if (!string.IsNullOrEmpty(cur))
                        {
                            try { p.Set(string.Empty); written++; } catch { /* defensive */ }
                        }
                    }
                }
            }

            // ── T4-T10 tier appends (Phase 165 — tagging workflow repair) ──
            // Issue #2 / #11 — only fire the tier append in Handover or Custom
            // mode. DC mode uses TAG7D-F (written above) for tiers 4-6; firing
            // the tier append in DC would write T4-T6 twice (once via TAG7D-F
            // narrative, once via tier-summary append).
            // Pattern mode (HANDOVER / DC / CUSTOM) is read once and surfaced as
            // a tag prefix once at least one tier 4+ payload is appended.
            // Reads pull from the element-type first (depth lives on type per
            // SetParagraphDepthCommand) then fall back to the instance.
            if (activeMode != ParamRegistry.TagMode.DC)
            try
            {
                Element typeEl = doc?.GetElement(el.GetTypeId());
                bool[] enabled = new bool[7]; // index 0 = T4 .. 6 = T10
                // Phase 165 perf — reuse the cached ParamRegistry.AllParaStates
                // (10 entries) instead of allocating a 7-string array per
                // element. Slot index 3 in AllParaStates == PARA_STATE_4.
                string[] allStates = ParamRegistry.AllParaStates;
                for (int i = 0; i < 7; i++)
                {
                    string pname = allStates[i + 3]; // 0..6 → PARA_STATE_4..10
                    enabled[i] = ReadParaStateBool(typeEl, pname)
                              || ReadParaStateBool(el,     pname);
                }

                string[] tierStrings = tag7.Tier4Summaries; // T4..T10
                bool anyTierAppended = false;
                var tierAppend = new System.Text.StringBuilder();

                for (int i = 0; i < tierStrings.Length; i++)
                {
                    if (!enabled[i] || string.IsNullOrEmpty(tierStrings[i])) continue;
                    string label = $"T{i + 4}";
                    tierAppend.Append(" | ").Append(label).Append(": ").Append(tierStrings[i]);
                    anyTierAppended = true;
                }

                if (anyTierAppended)
                {
                    // Resolve active pattern mode for prefix; default to DC.
                    string mode = ResolveActivePatternMode(typeEl, el);
                    tag7Final = tag7Final + " | [" + mode + "]" + tierAppend.ToString();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn("WriteTag7All T4-T10 append failed: " + ex.Message);
            }

            // ── Warning parameter population (v5.6) ────────────────────────
            // combined warning evaluation. The previous
            // code called PopulateWarningParameters AND EvaluateElementWarnings,
            // each walking the same GetCategoryWarnings list, calling the same
            // GetWarningDataValue per warning, calling the same EvaluateWarning.
            // The new EvaluateAndPopulateWarnings does both in one pass and
            // returns (writtenCount, concatenatedText).
            var warnPass = EvaluateAndPopulateWarnings(doc, el, categoryName);
            written += warnPass.WrittenCount;
            string warningText = warnPass.ConcatenatedText;
            if (!string.IsNullOrEmpty(warningText)
                && !string.IsNullOrEmpty(tag7Final)
                && !tag7Final.Contains(warningText))
            {
                tag7Final = tag7Final + " | " + warningText;
            }

            // Single TAG7 write covers narrative + appended warnings.
            if (!string.IsNullOrEmpty(tag7Final))
            {
                if (ParameterHelpers.SetString(el, ParamRegistry.TAG7, tag7Final, overwrite))
                    written++;
            }

            // ── Paragraph container write (v5.5 + Phase 165 Issue #22) ───
            // Write the full plain narrative to the category-specific paragraph parameter
            string paraContainer = ParamRegistry.GetParagraphContainer(categoryName);

            // Phase 165 — Issue #22. Clear stale paragraph data before
            // re-writing. When an element changes category or pattern mode,
            // an old narrative could otherwise persist in containers that
            // should now be empty (each category has its own paragraph
            // container, and the active one varies). Iterate the registered
            // container set and blank every container EXCEPT the one we're
            // about to write so the active payload survives.
            //
            // Phase 165 perf — skip the entire clear pass when the active
            // paragraph container hasn't changed since the last write. We
            // stamp ASS_LAST_PARA_CONTAINER_TXT after a successful clear and
            // re-read it next time. If the value matches the active
            // container, no other container can have stale data on this
            // element (only WriteTag7All ever writes them). The pass falls
            // through to the actual write below either way.
            const string LAST_PARA_PARAM = "ASS_LAST_PARA_CONTAINER_TXT";
            string lastPara = ParameterHelpers.GetString(el, LAST_PARA_PARAM);
            bool needClear = !string.Equals(lastPara, paraContainer ?? "", StringComparison.Ordinal);
            if (needClear)
            {
                try
                {
                    string[] allParas = ParamRegistry.AllParagraphContainers;
                    for (int i = 0; i < allParas.Length; i++)
                    {
                        string containerName = allParas[i];
                        if (string.IsNullOrEmpty(containerName)) continue;
                        if (!string.IsNullOrEmpty(paraContainer)
                            && string.Equals(containerName, paraContainer, StringComparison.Ordinal))
                            continue; // skip the active container — we're writing it next.
                        // Phase 165 perf — CachedLookup short-circuits the
                        // LookupParameter cost when the same family carries
                        // (or doesn't carry) this container parameter.
                        Parameter p = ParameterHelpers.CachedLookup(el, containerName);
                        if (p == null || p.IsReadOnly) continue;
                        if (p.StorageType != StorageType.String) continue;
                        string cur = p.AsString();
                        if (string.IsNullOrEmpty(cur)) continue;
                        try { p.Set(string.Empty); written++; } catch { /* defensive */ }
                    }
                    // Stamp the new active container name so the next pass
                    // can short-circuit when nothing has changed.
                    ParameterHelpers.SetString(el, LAST_PARA_PARAM, paraContainer ?? "", overwrite: true);
                }
                catch (Exception ex2)
                {
                    StingLog.Warn("WriteTag7All paragraph clear pass failed: " + ex2.Message);
                }
            }

            if (!string.IsNullOrEmpty(paraContainer) && !string.IsNullOrEmpty(tag7.PlainNarrative))
            {
                // Use a local StringBuilder to avoid intermediate string allocations when
                // warnings are appended — one paraText per element across 1000-element batches
                // was generating 1000 throwaway string objects.
                var paraBuilder = new System.Text.StringBuilder(tag7.PlainNarrative);
                if (!string.IsNullOrEmpty(warningText))
                {
                    paraBuilder.Append(" | WARNINGS: ");
                    paraBuilder.Append(warningText);
                }
                if (ParameterHelpers.SetString(el, paraContainer, paraBuilder.ToString(), overwrite))
                    written++;
            }

            return written;
        }

        /// <summary>
        /// Phase 165 — read a TAG_PARA_STATE_N_BOOL parameter. Mirrors the
        /// Yes/No vs. integer storage handling in SetParagraphDepthCommand.
        /// Treats missing parameter as false.
        /// </summary>
        public static bool ReadParaStateBool(Element host, string paramName)
        {
            if (host == null || string.IsNullOrEmpty(paramName)) return false;
            // Phase 165 perf — use the shared parameter cache so the same
            // (typeId, paramName) pair pays the LookupParameter cost only once
            // per session. WriteTag7All calls this 14× per element so a
            // 1000-element batch was 14000 fresh LookupParameter calls; with
            // CachedLookup most calls become an O(1) Definition fetch.
            Parameter p = ParameterHelpers.CachedLookup(host, paramName);
            if (p == null) return false;
            try
            {
                if (p.StorageType == StorageType.String)
                {
                    string s = p.AsString();
                    if (string.IsNullOrEmpty(s)) return false;
                    return s.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("1", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                if (p.StorageType == StorageType.Integer) return p.AsInteger() != 0;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        /// <summary>
        /// Phase 165 — resolve active pattern mode (HANDOVER / DC / CUSTOM) by
        /// inspecting the type then instance. Defaults to DC if none set.
        /// </summary>
        public static string ResolveActivePatternMode(Element typeEl, Element instEl)
        {
            // Type takes precedence (matches paragraph-state location).
            if (ReadParaStateBool(typeEl, ParamRegistry.MODE_HANDOVER)) return "HANDOVER";
            if (ReadParaStateBool(typeEl, ParamRegistry.MODE_CUSTOM))   return "CUSTOM";
            if (ReadParaStateBool(typeEl, ParamRegistry.MODE_DC))       return "DC";
            if (ReadParaStateBool(instEl, ParamRegistry.MODE_HANDOVER)) return "HANDOVER";
            if (ReadParaStateBool(instEl, ParamRegistry.MODE_CUSTOM))   return "CUSTOM";
            if (ReadParaStateBool(instEl, ParamRegistry.MODE_DC))       return "DC";
            return "DC"; // default per Phase 165 spec
        }

        /// <summary>
        /// Phase 165 — read active paragraph depth (1-10) on an element type.
        /// Returns the highest enabled PARA_STATE_N (cumulative scheme).
        /// Defaults to 3 if no states are enabled (legacy "Comprehensive").
        /// </summary>
        public static int ReadActiveParagraphDepth(Element typeEl, Element instEl)
        {
            // Phase 165 perf — use the cached ParamRegistry.AllParaStates
            // array instead of allocating a 10-string array per call.
            string[] paraNames = ParamRegistry.AllParaStates;
            int max = 0;
            for (int i = 0; i < paraNames.Length; i++)
            {
                if (ReadParaStateBool(typeEl, paraNames[i]) ||
                    ReadParaStateBool(instEl, paraNames[i]))
                    max = i + 1;
            }
            return max == 0 ? 3 : max;
        }

        /// <summary>
        /// Evaluate all applicable warning thresholds for an element.
        /// Returns a concatenated warning string, or null if no warnings triggered.
        /// Respects TAG_WARN_VISIBLE_BOOL and TAG_WARN_SEVERITY_FILTER_TXT.
        /// </summary>
        /// <summary>EFF-07 (Phase 149d): one-pass replacement for the
        /// PopulateWarningParameters + EvaluateElementWarnings combo. Both
        /// legacy methods walked the same warning list and called the same
        /// per-warning helpers; this method does it once, returning the
        /// number of WARN_ params written and the concatenated narrative
        /// fragment for the TAG7 append.</summary>
        public static (int WrittenCount, string ConcatenatedText)
            EvaluateAndPopulateWarnings(Document doc, Element el, string categoryName)
        {
            if (el == null || string.IsNullOrEmpty(categoryName))
                return (0, null);

            // Visibility gate (matches EvaluateElementWarnings).
            string warnVisible = ParameterHelpers.GetString(el, ParamRegistry.WARN_VISIBLE);
            bool visible = !(warnVisible == "No" || warnVisible == "0"
                || warnVisible == "FALSE" || warnVisible == "false");

            // Severity filter (matches EvaluateElementWarnings).
            string severityFilter = ParameterHelpers.GetString(el, ParamRegistry.WARN_SEVERITY_FILTER);
            if (string.IsNullOrEmpty(severityFilter)) severityFilter = "ALL";
            int filterLevel = severityFilter == "ALL" ? 0 : SeverityLevel(severityFilter);

            var warningParamNames = ParamRegistry.GetCategoryWarnings(categoryName);
            if (warningParamNames == null || warningParamNames.Count == 0)
                return (0, null);

            int written = 0;
            List<string> concat = null;

            foreach (string warnParam in warningParamNames)
            {
                if (!ParamRegistry.WarningThresholds.TryGetValue(warnParam, out var def))
                    continue;

                string dataValue = GetWarningDataValue(el, warnParam, categoryName);
                string evalResult = string.IsNullOrEmpty(dataValue)
                    ? null : ParamRegistry.EvaluateWarning(def, dataValue);

                // PopulateWarningParameters semantic — always overwrite to keep
                // params current; clear when no violation so stale text doesn't
                // linger.
                string warnText = string.IsNullOrEmpty(evalResult) ? "" : evalResult;
                if (ParameterHelpers.SetString(el, warnParam, warnText, overwrite: true))
                    written++;

                // EvaluateElementWarnings semantic — collect into concat string
                // when visible AND severity passes the filter AND there's a
                // violation to report.
                if (visible
                    && !string.IsNullOrEmpty(evalResult)
                    && (filterLevel == 0 || SeverityLevel(def.Severity) >= filterLevel))
                {
                    if (concat == null) concat = new List<string>();
                    concat.Add(evalResult);
                }
            }

            return (written, concat == null ? null : string.Join(" ", concat));
        }

        public static string EvaluateElementWarnings(Document doc, Element el, string categoryName)
        {
            // Check if warnings are enabled on this element
            string warnVisible = ParameterHelpers.GetString(el, ParamRegistry.WARN_VISIBLE);
            if (warnVisible == "No" || warnVisible == "0" || warnVisible == "FALSE" || warnVisible == "false")
                return null;

            // Get severity filter
            string severityFilter = ParameterHelpers.GetString(el, ParamRegistry.WARN_SEVERITY_FILTER);
            if (string.IsNullOrEmpty(severityFilter)) severityFilter = "ALL";

            // Get applicable warnings for this category
            var warningParamNames = ParamRegistry.GetCategoryWarnings(categoryName);
            if (warningParamNames == null || warningParamNames.Count == 0)
                return null;

            var warnings = new List<string>();
            foreach (string warnParam in warningParamNames)
            {
                if (!ParamRegistry.WarningThresholds.TryGetValue(warnParam, out var def))
                    continue;

                // Apply severity filter
                if (severityFilter != "ALL")
                {
                    int filterLevel = SeverityLevel(severityFilter);
                    int defLevel = SeverityLevel(def.Severity);
                    if (defLevel < filterLevel) continue; // Skip lower-severity warnings
                }

                // Try to get the element's current value for comparison
                // Map the warning param to its corresponding data parameter
                string dataValue = GetWarningDataValue(el, warnParam, categoryName);
                if (string.IsNullOrEmpty(dataValue)) continue;

                string warning = ParamRegistry.EvaluateWarning(def, dataValue);
                if (!string.IsNullOrEmpty(warning))
                    warnings.Add(warning);
            }

            return warnings.Count > 0 ? string.Join(" ", warnings) : null;
        }

        /// <summary>
        /// Populate individual WARN_ parameters on an element with their evaluated warning text.
        /// Each WARN_ parameter gets its own text so tag family labels can display them via
        /// calculated value formulas (Type: Text): if(TAG_WARN_VISIBLE_BOOL, WARN_xxx, "").
        /// All WARN_ parameters MUST be TEXT type to avoid Revit "Inconsistent Units" errors.
        /// Returns the number of WARN_ parameters written.
        /// </summary>
        public static int PopulateWarningParameters(Document doc, Element el, string categoryName)
        {
            if (el == null || string.IsNullOrEmpty(categoryName))
                return 0;

            var warningParamNames = ParamRegistry.GetCategoryWarnings(categoryName);
            if (warningParamNames == null || warningParamNames.Count == 0)
                return 0;

            int written = 0;
            foreach (string warnParam in warningParamNames)
            {
                if (!ParamRegistry.WarningThresholds.TryGetValue(warnParam, out var def))
                    continue;

                // Get the element's current measured value for this warning check
                string dataValue = GetWarningDataValue(el, warnParam, categoryName);

                string warningText;
                if (string.IsNullOrEmpty(dataValue))
                {
                    // No data available — write empty (tag label shows nothing)
                    warningText = "";
                }
                else
                {
                    string evalResult = ParamRegistry.EvaluateWarning(def, dataValue);
                    if (!string.IsNullOrEmpty(evalResult))
                    {
                        // Threshold violated — write the warning text
                        warningText = evalResult;
                    }
                    else
                    {
                        // Compliant — clear any previous warning
                        warningText = "";
                    }
                }

                // Write to the individual WARN_ parameter on the element
                // Always overwrite so warnings stay current with element data
                if (ParameterHelpers.SetString(el, warnParam, warningText, overwrite: true))
                    written++;
            }

            return written;
        }

        /// <summary>Get severity level as numeric (for filtering). Higher = more severe.</summary>
        private static int SeverityLevel(string severity)
        {
            switch (severity?.ToUpperInvariant())
            {
                case "CRITICAL": return 4;
                case "HIGH": return 3;
                case "MEDIUM": return 2;
                case "LOW": return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// Map a warning parameter name to the actual data value on the element.
        /// Warning param names encode the type of check; this method finds the
        /// corresponding measured/actual value to compare against the threshold.
        /// </summary>
        private static string GetWarningDataValue(Element el, string warnParam, string categoryName)
        {
            // Map warning params to their corresponding data sources
            // Pattern: WARN_{prefix}_{metric} maps to the element's actual parameter
            string wp = warnParam.ToUpperInvariant();

            // U-value checks
            if (wp.Contains("U_VALUE"))
                return ParameterHelpers.GetString(el, "PER_THERM_U_VALUE_W_M2K");
            // Voltage drop
            if (wp.Contains("VLT_DROP"))
                return ParameterHelpers.GetString(el, ParamRegistry.ELC_VOLTAGE);
            // Velocity
            if (wp.Contains("VEL_MPS") && wp.Contains("HVC"))
                return ParameterHelpers.GetString(el, ParamRegistry.HVC_VELOCITY);
            if (wp.Contains("VEL_MPS") && wp.Contains("PLM"))
                return ParameterHelpers.GetString(el, ParamRegistry.PLM_VELOCITY);
            // Sound
            if (wp.Contains("SOUNDLVL"))
                return ParameterHelpers.GetString(el, "HVC_DCT_SOUNDLVL_DB");
            // Fire rating
            if (wp.Contains("FRR") || (wp.Contains("FIRE") && wp.Contains("RESISTANCE")))
                return ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING);
            // Floor load
            if (wp.Contains("FLR_LD_CAP"))
                return ParameterHelpers.GetString(el, "BLE_FLR_LD_CAP_KPA");
            // Wall height ratio
            if (wp.Contains("WALL_HEIGHT_RATIO"))
            {
                string h = ParameterHelpers.GetString(el, ParamRegistry.WALL_HEIGHT);
                string t = ParameterHelpers.GetString(el, ParamRegistry.WALL_THICKNESS);
                if (double.TryParse(h, out double hv) && double.TryParse(t, out double tv) && tv > 0)
                    return (hv / tv).ToString("F1");
                return null;
            }
            // Ramp slope
            if (wp.Contains("RAMP_SLOPE"))
                return ParameterHelpers.GetString(el, ParamRegistry.RAMP_SLOPE);
            // Stair dimensions
            if (wp.Contains("STAIR_RISE"))
                return ParameterHelpers.GetString(el, ParamRegistry.STAIR_RISE);
            if (wp.Contains("STAIR_GOING") || wp.Contains("STAIR_TREAD"))
                return ParameterHelpers.GetString(el, ParamRegistry.STAIR_TREAD);
            if (wp.Contains("STAIR_WIDTH"))
                return ParameterHelpers.GetString(el, ParamRegistry.STAIR_WIDTH);
            if (wp.Contains("STAIR_HEADROOM"))
                return ParameterHelpers.GetString(el, "BLE_STAIR_HEADROOM_MM");
            // Door height
            if (wp.Contains("DOOR_HEIGHT"))
                return ParameterHelpers.GetString(el, ParamRegistry.DOOR_HEIGHT);
            // Ceiling height
            if (wp.Contains("CEIL_HEIGHT"))
                return ParameterHelpers.GetString(el, ParamRegistry.CEILING_HEIGHT);
            // Room area
            if (wp.Contains("ROOM_AREA"))
                return ParameterHelpers.GetString(el, ParamRegistry.ROOM_AREA);
            // Room height
            if (wp.Contains("ROOM_HEIGHT"))
                return ParameterHelpers.GetString(el, "ASS_ROOM_HEIGHT_MM");
            // Roof slope
            if (wp.Contains("ROOF_SLOPE"))
                return ParameterHelpers.GetString(el, ParamRegistry.ROOF_SLOPE);
            // SHGC / window performance
            if (wp.Contains("SHGC"))
                return ParameterHelpers.GetString(el, "BLE_CW_PANEL_SHGC");
            // Window U-value
            if (wp.Contains("WINDOW_U_VALUE"))
                return ParameterHelpers.GetString(el, "BLE_WINDOW_U_VALUE_W_M_2K_NR");
            // Rail height
            if (wp.Contains("RAIL_HEIGHT"))
                return ParameterHelpers.GetString(el, "BLE_RAIL_HEIGHT_MM");
            // Pipe flow
            if (wp.Contains("FLW_LPS") || wp.Contains("FIXTURE_FLOW"))
                return ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_FLOW);
            // Fill ratio
            if (wp.Contains("FILL_RATIO"))
                return ParameterHelpers.GetString(el, "ELC_CDT_FILL_RATIO");
            // Access width
            if (wp.Contains("ACCESS_CLEAR_WIDTH") || wp.Contains("CORRIDOR_WIDTH"))
                return ParameterHelpers.GetString(el, ParamRegistry.DOOR_WIDTH);
            // Column slenderness
            if (wp.Contains("SLENDERNESS"))
                return ParameterHelpers.GetString(el, "BLE_COLUMN_SLENDERNESS");
            // Beam span/depth
            if (wp.Contains("SPAN_DEPTH"))
                return ParameterHelpers.GetString(el, "STR_BEAM_SPAN_DEPTH");
            // Beam deflection
            if (wp.Contains("DEFLECTION"))
                return ParameterHelpers.GetString(el, "STR_BEAM_DEFLECTION");
            // Rebar cover
            if (wp.Contains("RBR_COVER"))
                return ParameterHelpers.GetString(el, "STR_RBR_COVER_MM");
            // Insulation thickness
            if (wp.Contains("INS_THICKNESS"))
                return ParameterHelpers.GetString(el, ParamRegistry.HVC_INSULATION);
            // Illuminance
            if (wp.Contains("ILLUMINANCE"))
                return ParameterHelpers.GetString(el, "LTG_ILLUMINANCE_LUX");
            // Carbon footprint
            if (wp.Contains("CARBON"))
                return ParameterHelpers.GetString(el, "PER_SUST_CARBON_FOOTPRINT_KG");
            // Acoustic ratings
            if (wp.Contains("ACOUSTIC") && wp.Contains("STC"))
                return ParameterHelpers.GetString(el, "PER_ACOUSTIC_WALL_STC");
            if (wp.Contains("ACOUSTIC") && wp.Contains("IIC"))
                return ParameterHelpers.GetString(el, "PER_ACOUSTIC_FLOOR_IIC");
            // ELC efficiency
            if (wp.Contains("EFF_RATIO"))
                return ParameterHelpers.GetString(el, "HVC_EFF_RATIO_NR");
            // Short circuit
            if (wp.Contains("SHORT_CIRCUIT"))
                return ParameterHelpers.GetString(el, "ELC_PNL_SHORT_CIRCUIT_KA");
            // Spare ways
            if (wp.Contains("SPARE_WAYS"))
            // Pipe gradient
            if (wp.Contains("PIPE_GRADIENT"))
                return ParameterHelpers.GetString(el, "PLM_PIPE_GRADIENT_PCT");
            // Trap seal
            if (wp.Contains("TRAP_SEAL"))
                return ParameterHelpers.GetString(el, "PLM_TRAP_SEAL_MM");
            // Foundation bearing/depth
            if (wp.Contains("FDN_BEARING") || wp.Contains("BEARING_CAP"))
                return ParameterHelpers.GetString(el, "BLE_STRUCT_FDN_BEARING_KPA");
            if (wp.Contains("FDN_DEPTH"))
                return ParameterHelpers.GetString(el, "STR_FDN_DEPTH_MM");
            // Weld
            if (wp.Contains("WLD_STRENGTH"))
                return ParameterHelpers.GetString(el, "STR_WLD_STRENGTH_MPA");
            if (wp.Contains("WLD_THROAT"))
                return ParameterHelpers.GetString(el, "STR_WLD_THROAT_MM");
            // Connection capacity
            if (wp.Contains("CONN_CAPACITY"))
                return ParameterHelpers.GetString(el, "STR_CONN_CAPACITY_KN");
            // Sprinkler coverage
            if (wp.Contains("SPR_COVER") || wp.Contains("COVERAGE_AREA"))
                return ParameterHelpers.GetString(el, "FLS_SFTY_COVERAGE_AREA_SQ_M");
            // IP rating
            if (wp.Contains("IP_RATING"))
                return ParameterHelpers.GetString(el, ParamRegistry.ELC_IP_RATING);
            // VOC
            if (wp.Contains("VOC"))
                return ParameterHelpers.GetString(el, "PER_VOC_EMISSIONS_UG_M3");
            // Hanger load
            if (wp.Contains("HANGER_LOAD"))
                return ParameterHelpers.GetString(el, "FAB_HANGER_LOAD_KN");
            // Duct pressure drop
            if (wp.Contains("PRESSURE_DROP"))
                return ParameterHelpers.GetString(el, ParamRegistry.HVC_PRESSURE);
            // Parking width
            if (wp.Contains("PARKING_WIDTH"))
                return ParameterHelpers.GetString(el, "BLE_PARKING_WIDTH_MM");

            // Generic fallback: no mapping found
            return null;
        }

        /// <summary>Append a label:value pair to both plain and marked StringBuilders.</summary>
        private static void AppendLabelValue(System.Text.StringBuilder plain, System.Text.StringBuilder marked,
            string label, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (plain.Length > 0) { plain.Append(", "); marked.Append(", "); }
            plain.Append($"{label}: {value}");
            marked.Append($"\u00ABL\u00BB{label}:\u00AB/L\u00BB \u00ABV\u00BB{value}\u00AB/V\u00BB");
        }

        /// <summary>Build marked-up technical data with «L»label«/L» «V»value«/V» tokens.</summary>
        private static string BuildMarkedTechSection(Element el, string disc, string categoryName)
        {
            var sb = new System.Text.StringBuilder();
            void AddM(string paramName, string connector, string unit)
            {
                string v = ParameterHelpers.GetString(el, paramName);
                if (!string.IsNullOrEmpty(v))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"\u00ABL\u00BB{connector}\u00AB/L\u00BB \u00ABV\u00BB{v}{(string.IsNullOrEmpty(unit) ? "" : $" {unit}")}\u00AB/V\u00BB");
                }
            }
            if (disc == "E" || categoryName == "Electrical Equipment" || categoryName == "Electrical Fixtures")
            {
                AddM(ParamRegistry.ELC_POWER, "rated at", "kW");
                AddM(ParamRegistry.ELC_VOLTAGE, "operating at", "V");
                AddM(ParamRegistry.ELC_CIRCUIT_NR, "connected to circuit", "");
                AddM(ParamRegistry.ELC_PNL_NAME, "supplied by panel", "");
                AddM(ParamRegistry.ELC_PHASES, "configured for", "phase supply");
                AddM(ParamRegistry.ELC_PNL_FED_FROM, "fed from", "");
                AddM(ParamRegistry.ELC_MAIN_BRK, "protected by a", "A main breaker");
                AddM(ParamRegistry.ELC_WAYS, "with", "ways");
                AddM(ParamRegistry.ELC_IP_RATING, "sealed to IP", "");
                AddM(ParamRegistry.ELC_PNL_LOAD, "carrying a connected load of", "kW");
            }
            else if (categoryName == "Lighting Fixtures" || categoryName == "Lighting Devices")
            {
                AddM(ParamRegistry.LTG_WATTAGE, "consuming", "W");
                AddM(ParamRegistry.LTG_LUMENS, "delivering", "lm of luminous output");
                AddM(ParamRegistry.LTG_EFFICACY, "achieving an efficacy of", "lm/W");
                AddM(ParamRegistry.LTG_LAMP_TYPE, "using a", "lamp");
                AddM(ParamRegistry.ELC_CIRCUIT_NR, "wired to circuit", "");
            }
            else if (disc == "M" || categoryName == "Mechanical Equipment" || categoryName == "Ducts" ||
                     categoryName == "Air Terminals" || categoryName == "Duct Fittings")
            {
                AddM(ParamRegistry.HVC_AIRFLOW, "delivering an airflow of", "L/s");
                AddM(ParamRegistry.HVC_DUCT_FLOW, "with a duct flow of", "CFM");
                AddM(ParamRegistry.HVC_VELOCITY, "at a velocity of", "m/s");
                AddM(ParamRegistry.HVC_PRESSURE, "against a pressure drop of", "Pa");
            }
            else if (disc == "P" || categoryName == "Pipes" || categoryName == "Plumbing Fixtures" || categoryName == "Pipe Fittings")
            {
                AddM(ParamRegistry.PLM_PIPE_FLOW, "conveying a flow of", "L/s");
                AddM(ParamRegistry.PLM_PIPE_SIZE, "through", "mm diameter pipework");
                AddM(ParamRegistry.PLM_VELOCITY, "at a velocity of", "m/s");
                AddM(ParamRegistry.PLM_FLOW_RATE, "with a design flow rate of", "L/s");
                AddM(ParamRegistry.PLM_PIPE_LENGTH, "running", "m in length");
            }
            else if (disc == "FP" || categoryName == "Sprinklers" || categoryName == "Fire Alarm Devices")
            {
                AddM(ParamRegistry.FIRE_RATING, "providing", "minutes of fire resistance");
            }
            return sb.ToString();
        }

        /// <summary>Build marked-up dimensional data with natural language connectors and «L»/«V» tokens.</summary>
        private static string BuildMarkedDimSection(Element el, string categoryName)
        {
            var sb = new System.Text.StringBuilder();
            void AddM(string paramName, string connector, string unit)
            {
                string v = ParameterHelpers.GetString(el, paramName);
                if (!string.IsNullOrEmpty(v))
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append($"\u00ABL\u00BB{connector}\u00AB/L\u00BB \u00ABV\u00BB{v}{(string.IsNullOrEmpty(unit) ? "" : $" {unit}")}\u00AB/V\u00BB");
                }
            }
            if (categoryName == "Walls")
            {
                AddM(ParamRegistry.WALL_HEIGHT, "standing", "mm high");
                AddM(ParamRegistry.WALL_LENGTH, "spanning", "mm in length");
                AddM(ParamRegistry.WALL_THICKNESS, "with a thickness of", "mm");
                AddM(ParamRegistry.ELE_AREA, "covering an area of", "m\u00B2");
                AddM(ParamRegistry.FIRE_RATING, "achieving", "minutes of fire resistance");
                AddM(ParamRegistry.STRUCT_TYPE, "classified structurally as", "");
            }
            else if (categoryName == "Doors")
            {
                AddM(ParamRegistry.DOOR_WIDTH, "measuring", "mm wide");
                AddM(ParamRegistry.DOOR_HEIGHT, "by", "mm high");
                AddM(ParamRegistry.FIRE_RATING, "with", "minutes of fire resistance");
            }
            else if (categoryName == "Windows")
            {
                AddM(ParamRegistry.WINDOW_WIDTH, "measuring", "mm wide");
                AddM(ParamRegistry.WINDOW_HEIGHT, "by", "mm high");
                AddM(ParamRegistry.WINDOW_SILL, "set at a sill height of", "mm");
            }
            else if (categoryName == "Floors")
            {
                AddM(ParamRegistry.FLR_THICKNESS, "with a build-up of", "mm thick");
                AddM(ParamRegistry.ELE_AREA, "covering an area of", "m\u00B2");
                AddM(ParamRegistry.STRUCT_TYPE, "classified structurally as", "");
                AddM(ParamRegistry.FIRE_RATING, "achieving", "minutes of fire resistance");
            }
            else if (categoryName == "Ceilings")
            {
                AddM(ParamRegistry.CEILING_HEIGHT, "suspended at", "mm above floor level");
                AddM(ParamRegistry.ELE_AREA, "covering an area of", "m\u00B2");
            }
            else if (categoryName == "Roofs")
            {
                AddM(ParamRegistry.ROOF_SLOPE, "pitched at", "degrees");
                AddM(ParamRegistry.ELE_AREA, "covering an area of", "m\u00B2");
            }
            else if (categoryName == "Stairs")
            {
                AddM(ParamRegistry.STAIR_TREAD, "with treads", "mm deep");
                AddM(ParamRegistry.STAIR_RISE, "risers of", "mm");
                AddM(ParamRegistry.STAIR_WIDTH, "and a clear width of", "mm");
            }
            else if (categoryName == "Ramps")
            {
                AddM(ParamRegistry.RAMP_SLOPE, "inclined at", "%");
                AddM(ParamRegistry.RAMP_WIDTH, "with a clear width of", "mm");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Build discipline-specific technical data section for TAG7 narrative.
        /// Reads electrical ratings, HVAC airflow, plumbing flow rates, and lighting
        /// performance data based on the element's discipline code.
        /// </summary>
        private static string BuildDisciplineTechSection(Element el, string disc, string categoryName)
        {
            var tech = new System.Text.StringBuilder();

            if (disc == "E" || categoryName == "Electrical Equipment" || categoryName == "Electrical Fixtures")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_POWER), "rated at {0} kW");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_VOLTAGE), "operating at {0} V");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_CIRCUIT_NR), "connected to circuit {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_PNL_NAME), "supplied by panel {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_PHASES), "configured for {0} phase supply");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_PNL_FED_FROM), "fed from {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_MAIN_BRK), "protected by a {0} A main breaker");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_WAYS), "with {0} ways");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_IP_RATING), "sealed to IP {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_PNL_LOAD), "carrying a connected load of {0} kW");
            }
            else if (categoryName == "Lighting Fixtures" || categoryName == "Lighting Devices")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.LTG_WATTAGE), "consuming {0} W");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.LTG_LUMENS), "delivering {0} lm of luminous output");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.LTG_EFFICACY), "achieving an efficacy of {0} lm/W");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.LTG_LAMP_TYPE), "using a {0} lamp");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.ELC_CIRCUIT_NR), "wired to circuit {0}");
            }
            else if (disc == "M" || categoryName == "Mechanical Equipment" || categoryName == "Ducts" ||
                     categoryName == "Air Terminals" || categoryName == "Duct Fittings")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.HVC_AIRFLOW), "delivering an airflow of {0} L/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.HVC_DUCT_FLOW), "with a duct flow of {0} CFM");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.HVC_VELOCITY), "at a velocity of {0} m/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.HVC_PRESSURE), "against a pressure drop of {0} Pa");
            }
            else if (disc == "P" || categoryName == "Pipes" || categoryName == "Plumbing Fixtures" ||
                     categoryName == "Pipe Fittings")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_FLOW), "conveying a flow of {0} L/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_SIZE), "through {0} mm diameter pipework");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_VELOCITY), "at a velocity of {0} m/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_FLOW_RATE), "with a design flow rate of {0} L/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.PLM_PIPE_LENGTH), "running {0} m in length");
            }
            else if (disc == "FP" || categoryName == "Sprinklers" || categoryName == "Fire Alarm Devices")
            {
                AppendNatural(tech, ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING), "providing {0} minutes of fire resistance");
            }
            else if (disc == "H" || categoryName == "Rooms" && !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "CLN_ROOM_CLASS_TXT")))
            {
                // Healthcare: Clinical Room
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_ROOM_CLASS_TXT"), "classified as a {0} clinical space");
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_PRESS_REGIME_TXT"), "operating under {0} pressure regime");
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_INFECT_CLASS_TXT"), "with infection control class {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_HTM_REF_TXT"), "per {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, "CLN_ADB_CODE_TXT"), "ADB room code {0}");
            }
            else if (disc == "MG" || categoryName == "Pipes" && !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "MGS_GAS_TYPE_TXT")))
            {
                // Healthcare: Medical Gas
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_GAS_TYPE_TXT"), "carrying {0} medical gas");
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_DESIGN_FLOW_LS_NR"), "at a design flow of {0} L/s");
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_DESIGN_PRESS_KPA_NR"), "at {0} kPa design pressure");
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_OUTLET_COUNT_INT"), "serving {0} outlets");
                AppendNatural(tech, ParameterHelpers.GetString(el, "MGS_NFPA99_ZONE_TXT"), "in NFPA 99 zone {0}");
            }
            else if (disc == "RP" || !string.IsNullOrEmpty(ParameterHelpers.GetString(el, "RAD_LEAD_MM_NR")))
            {
                // Healthcare: Radiation Protection
                AppendNatural(tech, ParameterHelpers.GetString(el, "RAD_LEAD_MM_NR"), "with {0} mm Pb shielding");
                AppendNatural(tech, ParameterHelpers.GetString(el, "RAD_MODALITY_TXT"), "protecting against {0}");
                AppendNatural(tech, ParameterHelpers.GetString(el, "RAD_WORKLOAD_WK_NR"), "workload {0} mA·min/wk");
                AppendNatural(tech, ParameterHelpers.GetString(el, "RAD_QE_NAME_TXT"), "certified by {0}");
            }

            return tech.Length > 0 ? tech.ToString() : "";
        }

        /// <summary>
        /// Build dimensional properties section for TAG7 narrative.
        /// Reads category-specific BLE dimensional parameters (height, width, thickness,
        /// area, slope, fire rating) for building elements.
        /// </summary>
        private static string BuildDimensionalSection(Element el, string categoryName)
        {
            var dim = new System.Text.StringBuilder();

            if (categoryName == "Walls")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WALL_HEIGHT), "standing {0} mm high");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WALL_LENGTH), "spanning {0} mm in length");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WALL_THICKNESS), "with a thickness of {0} mm");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ELE_AREA), "covering an area of {0} m\u00B2");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING), "achieving {0} minutes of fire resistance");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STRUCT_TYPE), "classified structurally as {0}");
            }
            else if (categoryName == "Doors")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.DOOR_WIDTH), "measuring {0} mm wide");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.DOOR_HEIGHT), "by {0} mm high");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING), "with {0} minutes of fire resistance");
            }
            else if (categoryName == "Windows")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WINDOW_WIDTH), "measuring {0} mm wide");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WINDOW_HEIGHT), "by {0} mm high");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.WINDOW_SILL), "set at a sill height of {0} mm");
            }
            else if (categoryName == "Floors")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.FLR_THICKNESS), "with a build-up of {0} mm thick");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ELE_AREA), "covering an area of {0} m\u00B2");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STRUCT_TYPE), "classified structurally as {0}");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.FIRE_RATING), "achieving {0} minutes of fire resistance");
            }
            else if (categoryName == "Ceilings")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.CEILING_HEIGHT), "suspended at {0} mm above floor level");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ELE_AREA), "covering an area of {0} m\u00B2");
            }
            else if (categoryName == "Roofs")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ROOF_SLOPE), "pitched at {0} degrees");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.ELE_AREA), "covering an area of {0} m\u00B2");
            }
            else if (categoryName == "Stairs")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STAIR_TREAD), "with treads {0} mm deep");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STAIR_RISE), "risers of {0} mm");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.STAIR_WIDTH), "and a clear width of {0} mm");
            }
            else if (categoryName == "Ramps")
            {
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.RAMP_SLOPE), "inclined at {0}%");
                AppendNatural(dim, ParameterHelpers.GetString(el, ParamRegistry.RAMP_WIDTH), "with a clear width of {0} mm");
            }

            return dim.Length > 0 ? dim.ToString() : "";
        }

        /// <summary>
        /// Append a natural-language phrase to a StringBuilder if the value is not empty.
        /// Uses comma separators between items for prose-like flow.
        /// </summary>
        private static void AppendNatural(System.Text.StringBuilder sb, string value, string format)
        {
            if (!string.IsNullOrEmpty(value))
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(string.Format(format, value));
            }
        }
    }

}
