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
    public static partial class TagConfig
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
        /// <summary>Phase 191 — whether SEQ groups per location (building/volume),
        /// giving each building an independent counter on multi-building campuses.
        /// Set via project_config.json key SEQ_INCLUDE_LOC.</summary>
        internal static bool SeqIncludeLoc { get; set; } = false;
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

            string zoneKey = null;
            if (SeqIncludeZone)
                zoneKey = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
            string locKey = null;
            if (SeqIncludeLoc)
                locKey = ParameterHelpers.GetString(el, ParamRegistry.LOC);

            return SeqAssigner.BuildSeqKey(disc, sys, lvl, zoneKey, locKey, SeqIncludeZone, SeqIncludeLoc);
        }

        /// <summary>
        /// Build a canonical SEQ key from explicit token values.
        /// Matches the same format as BuildSeqKey(Element) for consistency.
        /// </summary>
        public static string BuildSeqKey(string disc, string sys, string func, string prod, string lvl, string zone = null, string loc = null)
            => SeqAssigner.BuildSeqKey(disc, sys, lvl, zone, loc, SeqIncludeZone, SeqIncludeLoc);

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
                    "SEQ_INCLUDE_ZONE","SEQ_INCLUDE_LOC","SEQ_LEVEL_RESET","STATUS_DEFAULT","REV_DEFAULT",
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
                bool prevIncludeLoc = SeqIncludeLoc;

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
                // Phase 191: per-building (volume) sequence grouping
                if (data.TryGetValue("SEQ_INCLUDE_LOC", out object seqLocObj))
                {
                    if (seqLocObj is bool slb) SeqIncludeLoc = slb;
                    else if (seqLocObj is string sls) SeqIncludeLoc =
                        sls.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                // A1: Detect SEQ scheme changes for warning in BuildAndWriteTag
                if (CurrentSeqScheme != prevScheme || SeqIncludeZone != prevIncludeZone
                    || SeqIncludeLoc != prevIncludeLoc)
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
            SeqIncludeLoc = false;
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
            string seqKey = SeqAssigner.BuildSeqKey(disc, sys, lvl, zone, loc, SeqIncludeZone, SeqIncludeLoc);

            // A1: Warn once per session when SEQ scheme has changed — counter keys may not
            // match existing tags, leading to duplicate or restarted sequences.
            if (_seqSchemeChanged && !_seqSchemeWarned)
            {
                StingLog.Warn($"SEQ scheme changed (scheme={CurrentSeqScheme}, includeZone={SeqIncludeZone}, includeLoc={SeqIncludeLoc}). " +
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


    }

}
