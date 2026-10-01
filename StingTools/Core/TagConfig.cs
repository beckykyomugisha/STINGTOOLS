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
    // TagCollisionMode enum relocated to Core/TagCollisionMode.cs (same namespace) so
    // it can be used without dragging in this Revit-bound file. Same move as SeqScheme.

    // SeqScheme enum relocated to Core/SeqAssigner.cs (same namespace) alongside
    // the pure sequence-assignment logic it parameterises.



    /// <summary>
    /// Ported from tag_config.py — project-level ISO 19650 token lookup tables.
    /// Loads from project_config.json; falls back to built-in defaults that mirror
    /// Sheet 02-TAG-FAMILY-CONFIG from the STINGTOOLS template workbook.
    /// </summary>
    public static partial class TagConfig
    {
        // F-2 / TAGACC-3 — elements whose LOC nothing could derive, so the token policy's
        // fallback was used. Counted so the number is reported, not absorbed.
        private static int _unresolvedLocCount;

        /// <summary>F-2: count of elements given the policy's LOC fallback because none could be derived.</summary>
        public static int UnresolvedLocCount => System.Threading.Volatile.Read(ref _unresolvedLocCount);

        /// <summary>F-2: reset at a batch boundary, alongside the other per-batch counters.</summary>
        private static bool ReadConfigBool(IDictionary<string, object> data, string key, bool fallback)
        {
            if (data == null || !data.TryGetValue(key, out object o) || o == null) return fallback;
            if (o is bool b) return b;
            string sv = o.ToString().Trim();
            if (sv.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (sv.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        public static void ResetUnresolvedLocCount()
            => System.Threading.Interlocked.Exchange(ref _unresolvedLocCount, 0);

        /// <summary>TAGACC-3: PopulateAll counts an element that nothing could locate.</summary>
        internal static void NoteUnresolvedLoc()
            => System.Threading.Interlocked.Increment(ref _unresolvedLocCount);

        public static int NumPad => ParamRegistry.NumPad;
        public static string Separator => ParamRegistry.Separator;
        public static string[] SegmentOrder => ParamRegistry.SegmentOrder;
        public const int MaxCollisionDepth = 10000;

        /// <summary>
        /// TW-02: Configurable SEQ zero-pad width. Defaults to NumPad (4) but can be
        /// overridden independently (e.g., 2 for small projects, 6 for large estates).
        /// Set by the Tokens &amp; Depth panel (the live driver); read via
        /// <see cref="EffectiveSeqPad"/>.
        /// </summary>
        public static int SeqPadWidth { get; internal set; } = 4;

        /// <summary>
        /// Single source of truth for the SEQ zero-pad width used across the tag builder:
        /// the explicit <see cref="SeqPadWidth"/> when set (&gt; 0), else <see cref="ParamRegistry.NumPad"/>
        /// (still the fallback + <c>num_pad</c> export driver). Callers must read this rather
        /// than re-deriving <c>SeqPadWidth &gt; 0 ? SeqPadWidth : NumPad</c> so the two never desync.
        /// </summary>
        public static int EffectiveSeqPad => SeqPadWidth > 0 ? SeqPadWidth : ParamRegistry.NumPad;

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

        // ------------------------------------------------------------------
        // Tag-formula gate emission (Inconsistent-Units fix)
        // ------------------------------------------------------------------
        /// <summary>
        /// Resolve the correct condition-FORM for a boolean gate parameter used
        /// inside a tag label / style-row Calculated Value formula, so the
        /// emitted formula is consistent with the gate's STORAGE TYPE and never
        /// trips Revit's "Inconsistent Units" error.
        ///
        /// <para>STING stores its tag-formula gates — the 10
        /// <c>TAG_PARA_STATE_*_BOOL</c>, the 128
        /// <c>TAG_{size}{style}_{colour}_BOOL</c> style params, the 6
        /// <c>TAG_7_SECTION_VISIBLE_*_BOOL</c>, <c>TAG_WARN_VISIBLE_BOOL</c>, and
        /// the mode gates — as YESNO (v5.4+). YESNO (Integer) is Revit's native
        /// <c>if()</c>/<c>and()</c>/<c>or()</c> condition type and must stay bare.
        /// A legacy TEXT gate, by contrast, has no truthy-string semantics: a
        /// bare TEXT parameter is NOT a valid condition and must be tested
        /// explicitly as <c>gate = "Yes"</c>.</para>
        ///
        /// <para>Returns the bare <paramref name="gateName"/> when the gate
        /// resolves to <see cref="StorageType.Integer"/> (YESNO — the v5.4+
        /// canonical type), or <c>gateName + " = \"Yes\""</c> when it resolves to
        /// <see cref="StorageType.String"/> (legacy TEXT). Because this reads the
        /// gate's ACTUAL bound storage, it self-heals the runtime formula: a
        /// YESNO family gets bare, a legacy TEXT family still gets the explicit
        /// comparison. When the gate cannot be resolved on <paramref name="fm"/>
        /// at all, it conservatively defaults to the TEXT comparison form (the
        /// legacy-safe shape, unchanged from when TEXT was the default).</para>
        /// </summary>
        public static string GateToken(FamilyManager fm, string gateName)
        {
            if (string.IsNullOrEmpty(gateName)) return gateName;

            StorageType storage = StorageType.String; // TEXT is the v5.3+ default
            try
            {
                if (fm != null)
                {
                    foreach (FamilyParameter fp in fm.Parameters)
                    {
                        if (string.Equals(fp?.Definition?.Name, gateName, StringComparison.Ordinal))
                        {
                            storage = fp.StorageType;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Tolerate API surface drift — fall back to TEXT comparison form.
                storage = StorageType.String;
            }

            // Integer/YESNO gate → bare token (valid Revit condition).
            // String/TEXT gate (and unknown) → explicit "Yes" comparison.
            return storage == StorageType.Integer
                ? gateName
                : gateName + " = \"Yes\"";
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

        /// <summary>KUT phased tagging: when true, every tag-write path populates the
        /// 8 source tokens + ASS_TAG_1_TXT (the ISO 19650 first line) only, and SKIPS the
        /// discipline containers (ASS_TAG_2..7) + the TAG7 narrative — so colleagues can
        /// complete those tiers later while coordination proceeds. Enforced centrally in
        /// ParamRegistry.WriteContainers + TagConfig.WriteTag7All. Loaded from
        /// project_config.json TAG1_ONLY. Default false (full pipeline).</summary>
        public static bool Tag1Only { get; internal set; } = false;

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
        /// Idempotent — folders that already exist are skipped.
        /// <para>
        /// Default FALSE. It defaulted true so exports "never race a missing
        /// directory", but every write path already creates its own directory
        /// (GetFolderPath / GetMetaPath / GetDataPath / StingPaths.Cde all call
        /// Directory.CreateDirectory), so the eager pass is redundant with the
        /// lazy one. What it did cost was ~53 (CdeFirst) to ~60 (BIM) empty
        /// directories materialised beside the model the moment it is opened —
        /// 11_ISSUES/CVI, 12_CLASHES/Snapshots and the rest existing before the
        /// project has a single issue. That is the most visible source of the
        /// "STING creates a mess of folders" complaint, for no behaviour gained.
        /// Set AUTO_CREATE_CDE_FOLDERS=true in project_config.json to pre-seed
        /// the tree (e.g. so a coordinator can populate it by hand up front).
        /// </para></summary>
        public static bool AutoCreateCdeFolders { get; internal set; } = false;

        /// <summary>When true (default), project folder display names carry a
        /// `_&lt;PROJECT_CODE&gt;` suffix (01_WIP → 01_WIP_FIRESTONE) so a folder stays
        /// identifiable once copied out of the root. Set FOLDER_CODE_SUFFIX=false in
        /// project_config.json for shorter names — but only BEFORE a project's first
        /// setup: the suffixed names are persisted in project_setup.json, so flipping it
        /// mid-project makes new unsuffixed folders appear alongside the existing
        /// suffixed ones. See <see cref="ProjectSetup.WithCodeSuffix"/>.</summary>
        public static bool FolderCodeSuffix { get; internal set; } = true;

        /// <summary>Phase 165 (NEW-02): When true, ClashScheduler starts on
        /// DocumentOpened with the cadence from default_clash_matrix.json
        /// (SchedulerIntervalMinutes; default 60). Off keeps the scheduler
        /// dormant until the user starts it from the Clash tab. Default false
        /// because the per-tick run on a large model is non-trivial.</summary>
        public static bool AutoStartClashScheduler { get; internal set; } = false;

        /// <summary>WP9: When true (default), brand-new (greenfield) projects adopt the ISO
        /// 19650 CDE-first folder tree (content types nested inside WIP/SHARED/PUBLISHED).
        /// Existing projects are unaffected. Set CDE_FIRST_LAYOUT=false in project_config.json
        /// to keep new projects on the numbered BIM tree.</summary>
        public static bool CdeFirstLayout { get; internal set; } = true;

        /// <summary>BIM-CLASH-LIVE-01: When true (default), LiveClashUpdater attaches
        /// its geometry/addition/deletion triggers at startup so live clash capture
        /// works out of the box. Set LIVE_CLASH_TRIGGERS_ENABLED=false in
        /// project_config.json on models that never use clash detection to skip the
        /// trigger attachment entirely.</summary>
        public static bool LiveClashTriggersEnabled { get; internal set; } = true;

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

        /// <summary>Whether creating a revision overwrites ASS_REV_TXT on EVERY tagged
        /// element with the new revision code (opt-in: REV as a project-wide "current
        /// revision" mirror). Default false, in which case REV keeps its normal
        /// semantics — the revision an element last CHANGED in, written per-element by
        /// RevisionEngine.StampAffectedElements. Loaded from PROPAGATE_REV_ON_CREATE.</summary>
        public static bool PropagateRevOnCreate { get; internal set; } = false;

        /// <summary>Whether issuing a revision auto-opens the next DRAFT revision in
        /// the same numbering sequence. Revit locks an Issued revision — no new clouds
        /// can target it — so without a fresh draft the team is blocked until someone
        /// manually adds one. Default true. Loaded from AUTO_NEXT_REVISION_ON_ISSUE.</summary>
        public static bool AutoNextRevisionOnIssue { get; internal set; } = true;

        /// <summary>FUT-01: Get the SEQ range for the current model's discipline.
        /// Returns (minSeq, maxSeq), or 1 to the largest number the SEQ pad width can hold
        /// when no allocation is defined (9999 at the default 4 digits; it was 9999 at
        /// every width).</summary>
        public static (int Min, int Max) GetSeqRange(string modelDiscipline)
        {
            var whole = (1, SeqAssigner.MaxSeqForPad(EffectiveSeqPad));
            if (string.IsNullOrEmpty(modelDiscipline) || SeqRangeAllocation.Count == 0)
                return whole;
            if (SeqRangeAllocation.TryGetValue(modelDiscipline, out var range))
                return range;
            return whole;
        }

        /// <summary>FUT-01: Validate a SEQ number is within the allocated range for the model discipline.
        /// Returns null if valid, error message if out of range.</summary>
        public static string ValidateSeqRange(int seqNumber, string modelDiscipline)
        {
            string Pad(int n) => n.ToString().PadLeft(EffectiveSeqPad, '0');
            if (SeqRangeAllocation.Count == 0) return null; // No allocation defined
            var (min, max) = GetSeqRange(modelDiscipline);
            if (seqNumber < min || seqNumber > max)
                return $"SEQ {Pad(seqNumber)} is outside allocated range {Pad(min)}-{Pad(max)} for model '{modelDiscipline}'. " +
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

        /// <summary>
        /// TAGACC-4: when false (the default) an Overwrite run keeps each element's stored
        /// SEQ wherever it is still unique, so asset identifiers survive a re-derivation.
        /// Set RENUMBER_ON_OVERWRITE = true in project_config.json to renumber instead.
        /// </summary>
        public static bool RenumberOnOverwrite { get; internal set; } = false;

        /// <summary>
        /// TAGACC-5: when true (the default) an element that has moved to another level, or
        /// that the stale marker flagged, has LVL / LOC / ZONE re-derived on the next tagging
        /// run even though its tag is complete — the tag describes where the element IS.
        /// Locked tokens and type overrides still win. Set RETAG_MOVED_ELEMENTS = false in
        /// project_config.json to treat tags as fixed identifiers instead.
        /// </summary>
        public static bool RetagMovedElements { get; internal set; } = true;

        /// <summary>
        /// TAGACC-13: how the worksharing SEQ lock (Core/Storage/StingSeqLockStore) is used.
        /// "block" (default): no new sequence number while another user holds the counter or
        /// central has a newer one — the element is deferred and retried. "warn": allocate
        /// anyway and log (duplicates are repaired after sync). "off": ignore the lock.
        /// Set SEQ_LOCK_MODE in project_config.json or with the Tag Rules button.
        /// </summary>
        public static string SeqLockMode { get; internal set; } = "block";

        /// <summary>FE-06: Full per-category token overrides. Key=category name, Value=dict of token->value.</summary>
        public static Dictionary<string, Dictionary<string, string>> CategoryTokenOverrides { get; internal set; }
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Per-category VISUAL tag policy override. Key = category name, Value =
        /// "All" | "PerRun" | "None" (see <see cref="StingTools.Core.Mep.TagVisualPolicy"/>).
        /// Governs how many <c>IndependentTag</c> annotations Smart Placement draws —
        /// NOT how token data is written (every element still gets its ASS_TAG_1).
        /// When a category is absent, linear MEP (pipes / ducts / conduit / tray)
        /// defaults to PerRun (one tag per connected run) and everything else to All.
        /// Set via CATEGORY_VISUAL_POLICY in project_config.json.
        /// </summary>
        public static Dictionary<string, string> CategoryVisualPolicy { get; internal set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


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
        /// Format matches BuildAndWriteTag, which keys on the same STORED tokens:
        /// DISC_SYS_LVL, with _ZONE_ / _LOC_ inserted when SeqIncludeZone / SeqIncludeLoc.
        /// FUNC and PROD are not part of the SEQ group.
        /// </summary>
        public static string BuildSeqKey(Element el)
        {
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
            string sys  = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
            string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
            string lvl  = ParameterHelpers.GetString(el, ParamRegistry.LVL);

            // Normalise empty tokens to avoid key drift — through the SAME policy
            // BuildAndWriteTag uses. These were a second copy of the same literals, which
            // is a latent SEQ defect: a project that overrides DISC's fallback would have
            // BuildAndWriteTag composing the tag with the new value while this method kept
            // keying counters on "A". The counter group and the tag would then disagree,
            // which is precisely the "Counter group mismatch / duplicate SEQ numbers"
            // BuildAndWriteTag already warns about.
            var policy = TagTokenPolicyRegistry.Get(el?.Document);
            if (lvl == "XX") lvl = "";

            disc = TagTokenPolicy.Resolve(policy, "DISC", disc).Value;
            sys  = TagTokenPolicy.Resolve(policy, "SYS",  sys).Value;
            func = TagTokenPolicy.Resolve(policy, "FUNC", func).Value;
            prod = TagTokenPolicy.Resolve(policy, "PROD", prod).Value;
            lvl  = TagTokenPolicy.Resolve(policy, "LVL",  lvl).Value;

            // A refused token resolves to "" here rather than skipping: this method only
            // GROUPS counters, it writes nothing, and a stable empty group is better than
            // throwing from a key builder. BuildAndWriteTag is where refusal is enforced.

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
        /// <paramref name="func"/> and <paramref name="prod"/> are accepted for call-site
        /// symmetry only; the SEQ group is DISC / [LOC] / [ZONE] / SYS / LVL.
        /// </summary>
        public static string BuildSeqKey(string disc, string sys, string func, string prod, string lvl, string zone = null, string loc = null)
            => SeqAssigner.BuildSeqKey(disc, sys, lvl, zone, loc, SeqIncludeZone, SeqIncludeLoc);

        /// <summary>
        /// Build a SEQ string for sequence number n using the configured scheme.
        /// Delegates to <see cref="SeqAssigner.BuildSeqString"/>, supplying the
        /// project-configured pad width.
        /// </summary>
        public static string BuildSeqString(int n, SeqScheme scheme, string zoneOrDisc = "")
            => SeqAssigner.BuildSeqString(n, scheme, EffectiveSeqPad, zoneOrDisc);

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

                // Validate config keys — warn on unknown keys to catch typos. TAGACC-26: the
                // known set lives in ProjectConfigKeys (gated against the code), and keys that
                // are documented but applied by nothing get their own warning. Reads are
                // case-sensitive, so the check is too: "seq_scheme" is not SEQ_SCHEME.
                var unknownKeys = data.Keys
                    .Where(k => ProjectConfigKeys.Classify(k) == ProjectConfigKeys.KeyStatus.Unknown).ToList();
                if (unknownKeys.Count > 0)
                {
                    string unknownList = string.Join(", ", unknownKeys.Take(5));
                    StingLog.Warn($"TagConfig: unknown config key(s) in project_config.json: {unknownList}" +
                        (unknownKeys.Count > 5 ? $" (+{unknownKeys.Count - 5} more)" : "") +
                        " — check for typos (names are case-sensitive)");
                }
                foreach (var k in data.Keys.Where(k => ProjectConfigKeys.Classify(k) == ProjectConfigKeys.KeyStatus.NotApplied))
                    StingLog.Warn($"TagConfig: {k} in project_config.json has no effect — {ProjectConfigKeys.NotApplied[k]} (TAGACC-26).");

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

                            if (fmt.TryGetValue(StingTools.Tags.TagFormatConfig.SeparatorKey, out object sepVal) && sepVal is string s)
                                sep = s;
                            if (fmt.TryGetValue(StingTools.Tags.TagFormatConfig.NumPadKey, out object padVal))
                            {
                                if (padVal is long lv) pad = (int)lv;
                                else if (int.TryParse(padVal?.ToString(), out int iv)) pad = iv;
                            }
                            if (fmt.TryGetValue(StingTools.Tags.TagFormatConfig.SegmentOrderKey, out object segVal))
                            {
                                var parsed = JsonConvert.DeserializeObject<string[]>(
                                    JsonConvert.SerializeObject(segVal));
                                if (parsed != null && parsed.Length > 0)
                                    segs = parsed;
                            }

                            ParamRegistry.ApplyTagFormatOverrides(sep, pad, segs);

                            // TAGACC-23: a format saved by the old Tag Format command used names
                            // nothing reads. Say so rather than apply it silently or forget it.
                            if (StingTools.Tags.TagFormatConfig.IsLegacyUnreadSection(
                                    Newtonsoft.Json.Linq.JObject.FromObject(fmt)))
                                StingLog.Warn("TAG_FORMAT in " + path + " uses NumPad/SegmentOrder/Separator, " +
                                    "which were never applied (TAGACC-23). Re-save it with Tag Format to use it.");
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
                    {
                        // ZonePrefix / DiscPrefix are deprecated (they corrupt the
                        // fixed 8-segment grammar). Heal a persisted value to Numeric.
                        if (parsed == SeqScheme.ZonePrefix || parsed == SeqScheme.DiscPrefix)
                        {
                            StingLog.Warn($"SEQ scheme '{parsed}' is deprecated (breaks the 8-segment tag); using Numeric.");
                            parsed = SeqScheme.Numeric;
                        }
                        CurrentSeqScheme = parsed;
                    }
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

                // MEP declutter: per-category visual tag policy overrides (All/PerRun/None)
                CategoryVisualPolicy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var visPolicy = TryDeserialize<Dictionary<string, string>>(data, "CATEGORY_VISUAL_POLICY");
                if (visPolicy != null)
                    foreach (var kvp in visPolicy) CategoryVisualPolicy[kvp.Key] = kvp.Value;

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
                LiveClashTriggersEnabled = true;
                if (data.TryGetValue("LIVE_CLASH_TRIGGERS_ENABLED", out object lctObj))
                {
                    if (lctObj is bool lb) LiveClashTriggersEnabled = lb;
                    else if (bool.TryParse(lctObj?.ToString(), out bool lbp)) LiveClashTriggersEnabled = lbp;
                }

                CdeFirstLayout = true;
                if (data.TryGetValue("CDE_FIRST_LAYOUT", out object cflObj))
                {
                    if (cflObj is bool cb) CdeFirstLayout = cb;
                    else if (bool.TryParse(cflObj?.ToString(), out bool cbp)) CdeFirstLayout = cbp;
                }

                AutoCreateCdeFolders = false;
                if (data.TryGetValue("AUTO_CREATE_CDE_FOLDERS", out object accfObj))
                {
                    if (accfObj is bool b) AutoCreateCdeFolders = b;
                    else if (bool.TryParse(accfObj?.ToString(), out bool bp)) AutoCreateCdeFolders = bp;
                }

                FolderCodeSuffix = true;
                if (data.TryGetValue("FOLDER_CODE_SUFFIX", out object fcsObj))
                {
                    if (fcsObj is bool fb) FolderCodeSuffix = fb;
                    else if (bool.TryParse(fcsObj?.ToString(), out bool fbp)) FolderCodeSuffix = fbp;
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

                // KUT phased tagging — TAG1-only mode (defer containers + TAG7 narrative)
                Tag1Only = false;
                if (data.TryGetValue("TAG1_ONLY", out object t1Obj))
                {
                    if (t1Obj is bool t1b) Tag1Only = t1b;
                    else if (bool.TryParse(t1Obj?.ToString(), out bool t1p)) Tag1Only = t1p;
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

                RenumberOnOverwrite = ReadConfigBool(data, "RENUMBER_ON_OVERWRITE", false);
                RetagMovedElements = ReadConfigBool(data, "RETAG_MOVED_ELEMENTS", true);
                SeqLockMode = "block";
                if (data.TryGetValue("SEQ_LOCK_MODE", out object slmObj) && slmObj != null)
                {
                    string slm = slmObj.ToString().Trim().ToLowerInvariant();
                    if (slm == "block" || slm == "warn" || slm == "off") SeqLockMode = slm;
                    else StingLog.Warn($"TagConfig: SEQ_LOCK_MODE '{slmObj}' is not block / warn / off — using block.");
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
                PropagateRevOnCreate = false;
                if (data.TryGetValue("PROPAGATE_REV_ON_CREATE", out object procObj))
                {
                    if (procObj is bool procb) PropagateRevOnCreate = procb;
                    else if (procObj is string procs) PropagateRevOnCreate = procs.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                AutoNextRevisionOnIssue = true;
                if (data.TryGetValue("AUTO_NEXT_REVISION_ON_ISSUE", out object anriObj))
                {
                    if (anriObj is bool anrib) AutoNextRevisionOnIssue = anrib;
                    else if (anriObj is string anris) AutoNextRevisionOnIssue = anris.Equals("true", StringComparison.OrdinalIgnoreCase);
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
                InvalidateProdRulesCache(); // clears corporate + project prod_codes overlays
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
            CategoryVisualPolicy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            RenumberOnOverwrite = false;
            RetagMovedElements = true;
            SeqLockMode = "block";
            // Reload FUNC/SYS matrix from CSV so custom project additions aren't lost on reset
            // Note: _validFuncsCsvLoaded/EnsureValidFuncsLoaded live in ISO19650Validator; use InvalidateValidatorCaches.
            ISO19650Validator.InvalidateValidatorCaches();
            // Load PROD code rules from CSV (lazy — invalidate so next GetFamilyAwareProdCode call reloads)
            InvalidateProdRulesCache();
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
                        [StingTools.Tags.TagFormatConfig.SeparatorKey] = Separator,
                        [StingTools.Tags.TagFormatConfig.NumPadKey] = NumPad,
                        [StingTools.Tags.TagFormatConfig.SegmentOrderKey] = SegmentOrder
                    },
                    ["TAG_PREFIX"] = TagPrefix,
                    ["TAG_SUFFIX"] = TagSuffix,
                    ["CATEGORY_SKIP"] = CategorySkipList.ToList(),
                    ["CATEGORY_FORCE_SYS"] = CategoryForceSys,
                    ["CATEGORY_VISUAL_POLICY"] = CategoryVisualPolicy,
                    ["COMPLIANCE_GATE_PCT"] = ComplianceGatePct,
                    ["TAG1_ONLY"] = Tag1Only,
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

                // Tagging behaviour switches. This save rewrites the whole file, so a key it
                // does not write is lost — these two were added 2026-09-29 (TAGACC-4 / -5).
                data["RENUMBER_ON_OVERWRITE"] = RenumberOnOverwrite;
                data["RETAG_MOVED_ELEMENTS"] = RetagMovedElements;
                data["SEQ_LOCK_MODE"] = SeqLockMode;
                data["AUTO_CORRECT_STATUS_FROM_PHASE"] = AutoCorrectStatusFromPhase;

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // TAGACC-19: the file is shared with SEQ_*, folder-layout, COST_* and other
                // keys read through GetConfigValue. Overwrite only the keys above; keep the rest.
                string existing = File.Exists(path) ? File.ReadAllText(path) : null;
                string json;
                int preserved;
                try
                {
                    json = ConfigFileMerge.Merge(existing, data, out preserved);
                }
                catch (JsonException jex)
                {
                    // A file we cannot read is kept, not overwritten.
                    StingLog.Error($"TagConfig save refused: {path} is not valid JSON ({jex.Message}). " +
                                   "Fix or move the file, then save again.", jex);
                    return false;
                }

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, true);
                lock (_cfgCacheLock) { _cfgCached = null; _cfgCachedPath = null; _cfgCachedMTicks = 0; }
                StingLog.Info($"TagConfig saved to {path} ({data.Count} tag keys written, {preserved} other keys kept)");
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

        /// <summary>The category-fallback SYS code. O(1) via cached reverse lookup.
        /// For a category listed under several systems it returns the category's
        /// DISCIPLINE default when that is one of them (Walls → ARC, Generic Models → GEN),
        /// else the first listed. It used to return the first listed, and LPS is listed
        /// before ARC / STR / GEN, so walls, roofs and foundations defaulted to LPS.
        /// Use <see cref="GetAllSysCodes"/> when the full list is needed.</summary>
        public static string GetSysCode(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return string.Empty;
            var reverse = GetReverseSysMap();
            if (!reverse.TryGetValue(categoryName, out var list) || list.Count == 0) return string.Empty;
            string disc = DiscMap != null && DiscMap.TryGetValue(categoryName, out string d) ? d : null;
            return CategoryTokenDefaults.ChooseCategorySys(list, disc);
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
        /// Returns null when the name names no specific component (caller falls
        /// back to FuncMap[sys]). Called by <see cref="GetSmartFuncCode"/> for
        /// SYS=LPS; the keyword rules live in the Revit-free
        /// <see cref="LpsNameClassifier"/>.
        /// </summary>
        public static string ResolveLpsFunc(Element el)
        {
            if (el == null) return null;
            string fam = ParameterHelpers.GetFamilyName(el);
            string sym = ParameterHelpers.GetFamilySymbolName(el);
            return LpsNameClassifier.Func(($"{fam} {sym}").ToUpperInvariant());
        }

        /// <summary>
        /// Phase 176 — true when family/type name shows lightning-protection
        /// markers. Used by validators, container resolvers and LPS warning
        /// writers to scope BS EN 62305 checks to the relevant elements only.
        /// Same keyword set as the LPS PROD resolver (<see cref="LpsNameClassifier"/>).
        /// </summary>
        public static bool IsLightningProtection(Element el)
        {
            if (el == null) return false;
            string fam = ParameterHelpers.GetFamilyName(el);
            string sym = ParameterHelpers.GetFamilySymbolName(el);
            return LpsNameClassifier.IsLps(($"{fam} {sym}").ToUpperInvariant());
        }

        /// <summary>
        /// Get a guaranteed default SYS code from a discipline code.
        /// Used as a fallback when MEP system detection returns empty —
        /// ensures every element gets a valid SYS token.
        /// M→HVAC, E→LV, P→DCW (cold water bias), A→ARC, S→STR, FP→FP, LV→LV, G→GEN, else GEN.
        /// </summary>
        public static string GetDiscDefaultSysCode(string disc)
            => CategoryTokenDefaults.DisciplineDefaultSys(disc);

        /// <summary>
        /// The FUNC resolver — every tagging path calls this rather than carrying its own
        /// fallback chain (there were ten copies, and they had drifted).
        /// For HVAC, differentiates Supply (SUP), Return (RTN), Exhaust (EXH), Fresh Air (FRA).
        /// For HWS, differentiates Heating (HTG) vs Domestic Hot Water (DHW); SAN vents → VNT;
        /// LPS components → AT / DC / EE / BOND / SPD / TC.
        /// Falls back to FuncMap[sys], then "GEN". Never returns blank; "GEN" means the
        /// function could not be established (callers that record assumptions test for it).
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

            // For HWS, a return circuit is RTN (tested first: "LTHW Return" also says
            // LTHW); otherwise heating vs domestic hot water.
            if (sysCode == "HWS")
            {
                foreach (string n in SystemNamesOf(el))
                    if (SystemNameClassifier.FlowDirection(n) == "RTN") return "RTN";
                string hwsFunc = GetHwsSubFunction(el);
                if (!string.IsNullOrEmpty(hwsFunc)) return hwsFunc;
            }

            // For SAN, a vent/soil-vent pipe gets FUNC=VNT (validator allows SAN→VNT)
            if (sysCode == "SAN")
            {
                string sanFunc = GetSanSubFunction(el);
                if (!string.IsNullOrEmpty(sanFunc)) return sanFunc;
            }

            // For medical gas, the gas is the function (O2, MA4, VAC …, the
            // MGS_GAS_TYPE_TXT vocabulary), read off the element or its system name.
            if (sysCode == SystemNameClassifier.MedicalGasSys)
            {
                string gas = GetMgsSubFunction(el);
                if (!string.IsNullOrEmpty(gas)) return gas;
            }

            // DHW return (secondary circulation) is RTN; flow keeps DHW. Chilled and
            // condenser water take their direction; refrigerant its line; LV lighting,
            // emergency lighting and small power their own function. All from the system
            // or family name (SystemNameClassifier), else FuncMap below.
            if (sysCode == "DHW" || sysCode == "CHW" || sysCode == "CDW")
            {
                foreach (string n in SystemNamesOf(el))
                {
                    string dir = SystemNameClassifier.FlowDirection(n);
                    if (dir == "RTN") return "RTN";
                    if (dir == "SUP" && sysCode != "DHW") return "SUP";
                }
            }
            if (sysCode == "REF")
            {
                foreach (string n in SystemNamesOf(el).Append(ParameterHelpers.GetFamilyName(el) + " " + ParameterHelpers.GetFamilySymbolName(el)))
                {
                    string line = SystemNameClassifier.RefrigerantFunction(n);
                    if (line != null) return line;
                }
            }
            if (sysCode == "LV")
            {
                string lv = SystemNameClassifier.LvFunction(ParameterHelpers.GetCategoryName(el),
                    ParameterHelpers.GetFamilyName(el) + " " + ParameterHelpers.GetFamilySymbolName(el));
                if (!string.IsNullOrEmpty(lv)) return lv;
            }

            // HV, BMS and radiation protection: the role is in the family / type name.
            if (sysCode == SystemNameClassifier.HighVoltageSys || sysCode == SystemNameClassifier.BmsSys
                || sysCode == SystemNameClassifier.RadiationSys)
            {
                string named = SystemNameClassifier.FunctionFromName(sysCode,
                    ParameterHelpers.GetFamilyName(el) + " " + ParameterHelpers.GetFamilySymbolName(el));
                if (!string.IsNullOrEmpty(named)) return named;
            }

            // For LPS, the component role (air termination, down conductor, earth,
            // bonding, SPD, test clamp) is read off the family/type name.
            if (sysCode == "LPS")
            {
                string lpsFunc = ResolveLpsFunc(el);
                if (!string.IsNullOrEmpty(lpsFunc)) return lpsFunc;
            }

            return FuncMap.TryGetValue(sysCode, out string val) && !string.IsNullOrEmpty(val) ? val : "GEN";
        }

        /// <summary>
        /// The MEP system names an element carries: its connectors' systems and its pipe /
        /// duct system type. Empty for an element with none.
        /// </summary>
        private static List<string> SystemNamesOf(Element el)
        {
            var names = new List<string>();
            try
            {
                if (el is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
                    foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                        if (!string.IsNullOrEmpty(conn.MEPSystem?.Name)) names.Add(conn.MEPSystem.Name);
                foreach (var bip in new[] { BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM, BuiltInParameter.RBS_SYSTEM_NAME_PARAM })
                {
                    Parameter p = el.get_Parameter(bip);
                    string v = p != null && p.HasValue ? p.AsValueString() ?? p.AsString() : null;
                    if (!string.IsNullOrEmpty(v)) names.Add(v);
                }
            }
            catch (Exception ex) { StingLog.Warn($"System name read failed: {ex.Message}"); }
            return names;
        }

        /// <summary>
        /// Medical gas FUNC: the element's MGS_GAS_TYPE_TXT, else the gas its connected
        /// or assigned piping system names. Null when neither says which gas.
        /// </summary>
        private static string GetMgsSubFunction(Element el)
        {
            try
            {
                string own = Plumbing.MedicalGasFixtures.CanonicalGasCode(ParameterHelpers.GetString(el, "MGS_GAS_TYPE_TXT"));
                if (!string.IsNullOrEmpty(own)) return own;

                if (el is FamilyInstance fi && fi.MEPModel?.ConnectorManager != null)
                    foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                        if (conn.MEPSystem != null
                            && SystemNameClassifier.TryMedicalGas(conn.MEPSystem.Name, out string g) && g != null)
                            return g;

                Parameter pipeSys = el.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                if (pipeSys != null && pipeSys.HasValue
                    && SystemNameClassifier.TryMedicalGas(pipeSys.AsValueString(), out string g2) && g2 != null)
                    return g2;
            }
            catch (Exception ex) { StingLog.Warn($"Medical gas sub-function detection failed: {ex.Message}"); }
            return null;
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

                // Check duct system type parameter (ducts are MEPCurves, not
                // FamilyInstances, so this is the branch that classifies them).
                Parameter ductSys = el.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
                if (ductSys != null && ductSys.HasValue)
                {
                    string val = ductSys.AsValueString()?.ToUpperInvariant() ?? "";
                    if (val.Contains("SUPPLY")) return "SUP";
                    if (val.Contains("RETURN")) return "RTN";
                    if (val.Contains("EXHAUST") || val.Contains("EXTRACT")) return "EXH";
                    if (val.Contains("FRESH") || val.Contains("OUTSIDE AIR")) return "FRA";
                }

                // Check family name for duct-related equipment.
                // Direction beats shape - see HvacDirectionFromName for why a
                // "Return Diffuser" used to resolve as SUP.
                string dir = HvacDirectionFromName.Resolve(ParameterHelpers.GetFamilyName(el));
                if (!string.IsNullOrEmpty(dir)) return dir;
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

                // Pipes are MEPCurves, not FamilyInstances: their system is on the
                // piping system type parameter, as GetSanSubFunction already reads it.
                Parameter pipeSys = el.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                if (pipeSys != null && pipeSys.HasValue)
                {
                    string val = pipeSys.AsValueString()?.ToUpperInvariant() ?? "";
                    if (val.Contains("HEATING") || val.Contains("LTHW") ||
                        val.Contains("MTHW") || val.Contains("RADIATOR"))
                        return "HTG";
                    if (val.Contains("DOMESTIC") || val.Contains("DHW") ||
                        val.Contains("HOT WATER SUPPLY"))
                        return "DHW";
                }

                string familyName = ParameterHelpers.GetFamilyName(el).ToUpperInvariant();
                if (familyName.Contains("RADIATOR") || familyName.Contains("UNDERFLOOR")) return "HTG";
                if (familyName.Contains("CALORIFIER") || familyName.Contains("WATER HEATER")) return "DHW";
            }
            catch (Exception ex) { StingLog.Warn($"HWS sub-function detection failed: {ex.Message}"); }
            return null;
        }

        /// <summary>
        /// Detect SAN sub-function: a vent / soil-vent pipe gets FUNC=VNT
        /// (BS EN 12056-2). Reads from connector system name, pipe system type
        /// parameter, and family name.
        /// </summary>
        private static string GetSanSubFunction(Element el)
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
                            if (IsVentName(sysName)) return "VNT";
                        }
                    }
                }

                Parameter pipeSys = el.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM);
                if (pipeSys != null && pipeSys.HasValue)
                {
                    string val = pipeSys.AsValueString()?.ToUpperInvariant() ?? "";
                    if (IsVentName(val)) return "VNT";
                }

                string familyName = ParameterHelpers.GetFamilyName(el).ToUpperInvariant();
                if (IsVentName(familyName)) return "VNT";
            }
            catch (Exception ex) { StingLog.Warn($"SAN sub-function detection failed: {ex.Message}"); }
            return null;
        }

        // "VENT" as a word (VENT, VENTS, VENTING, SOIL VENT, VENT_PIPE) — not a substring,
        // which also matched PREVENT, EVENT and INVENTORY.
        private static readonly System.Text.RegularExpressions.Regex _ventWord =
            new System.Text.RegularExpressions.Regex(@"(^|[^A-Z])VENT(S|ING)?([^A-Z]|$)",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static bool IsVentName(string upper)
            => !string.IsNullOrEmpty(upper) && (_ventWord.IsMatch(upper) || upper.Contains("SVP"));

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

        // Project-scoped PROD rule overlays, keyed by the project's _BIM_COORD dir.
        // Mirrors the Fohlio / tag-scheme project-override pattern: a project
        // <project>/_BIM_COORD/prod_codes.csv layers OVER the corporate
        // STING_PROD_CODES.csv, and its rules WIN (checked first, first-match).
        private static readonly Dictionary<string, Dictionary<string, List<(string Pattern, string ProdCode)>>>
            _projProdRules = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _projProdLoaded = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _prodRulesLock = new object();

        // The PROD vocabulary a type NAME may declare (PLNS_WBL_Hollow200 → WBL).
        // ProdNameCode.Extract returns null without it, which until 2026-09-27 made
        // the "declared" tier unreachable in the plugin: both callers passed null.
        // Corporate = STING_PROD_CODES.csv codes ∪ ProdMap values; a project overlay
        // adds its own prod_codes.csv codes (cached per overlay instance).
        private static HashSet<string> _corpKnownProdCodes;
        private static Dictionary<string, HashSet<string>> _prodVocabByDisc;
        private static readonly Dictionary<object, HashSet<string>> _projKnownProdCodes = new();

        /// <summary>
        /// Public entry to drop the PROD rule caches so the next lookup re-reads
        /// from disk. Call after writing a project <c>prod_codes.csv</c> (e.g.
        /// Prod_GenerateRules) or before a fresh audit so on-disk edits are
        /// reflected in the same session without a config reload / Revit restart.
        /// </summary>
        public static void ReloadProdRules() => InvalidateProdRulesCache();

        /// <summary>The generic category-default PROD code (no family match), or
        /// "GEN" when the category isn't mapped. Used by the coverage audit to
        /// tell "genuinely specific" from "happens to equal the default".</summary>
        public static string CategoryProdDefault(string categoryName)
            => (!string.IsNullOrEmpty(categoryName) && ProdMap != null &&
                ProdMap.TryGetValue(categoryName, out string p)) ? p : "GEN";

        /// <summary>Drop the corporate + project PROD rule caches so the next
        /// lookup re-reads from disk (called on config reload).</summary>
        private static void InvalidateProdRulesCache()
        {
            lock (_prodRulesLock)
            {
                _csvProdRulesLoaded = false;
                _csvProdRules = null;
                _corpKnownProdCodes = null;
                _prodVocabByDisc = null;
                _projKnownProdCodes.Clear();
                _projProdLoaded.Clear();
                _projProdRules.Clear();
            }
            ProdPatternMatcher.Reset(); // drop compiled-glob cache so edited patterns recompile
        }

        private static void EnsureProdRulesLoaded()
        {
            if (_csvProdRulesLoaded) return;
            lock (_prodRulesLock)
            {
                if (_csvProdRulesLoaded) return;
                _csvProdRules = LoadProdCsv(StingToolsApp.FindDataFile("STING_PROD_CODES.csv"))
                                ?? new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
                _csvProdRulesLoaded = true;
                StingLog.Info($"TagConfig: loaded {_csvProdRules.Count} corporate PROD category rule sets from STING_PROD_CODES.csv");
            }
        }

        /// <summary>
        /// Resolve the project-scoped PROD rule overlay for a document, if any.
        /// Loaded from &lt;project&gt;/_BIM_COORD/prod_codes.csv and cached per
        /// project dir. Returns null when the document is unsaved or no overlay
        /// file exists. Project rules take precedence over the corporate baseline.
        /// </summary>
        private static Dictionary<string, List<(string Pattern, string ProdCode)>> GetProjectProdRules(Document doc)
        {
            string path = doc?.PathName;
            if (string.IsNullOrEmpty(path)) return null;
            string dir = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return null;
            string key = StingPaths.Meta(doc, "_BIM_COORD");
            lock (_prodRulesLock)
            {
                if (_projProdLoaded.Contains(key))
                    return _projProdRules.TryGetValue(key, out var cached) ? cached : null;
                _projProdLoaded.Add(key);
                var rules = LoadProdCsv(System.IO.Path.Combine(key, "prod_codes.csv"));
                if (rules != null && rules.Count > 0)
                {
                    _projProdRules[key] = rules;
                    StingLog.Info($"TagConfig: loaded {rules.Count} project PROD category rule sets from {key}");
                }
                return rules;
            }
        }

        /// <summary>
        /// PROD codes each discipline can carry according to the resolver's own data
        /// (ProdMap + corporate STING_PROD_CODES.csv). ISO19650Validator accepts these
        /// before applying its hand-written cross-discipline list, so the validator can
        /// no longer reject a code the resolver itself produced.
        /// </summary>
        internal static Dictionary<string, HashSet<string>> GetProdVocabularyByDiscipline()
        {
            EnsureProdRulesLoaded();
            lock (_prodRulesLock)
            {
                if (_prodVocabByDisc != null) return _prodVocabByDisc;
                var ruleCodes = new List<KeyValuePair<string, string>>();
                if (_csvProdRules != null)
                    foreach (var kv in _csvProdRules)
                        foreach (var r in kv.Value)
                            ruleCodes.Add(new KeyValuePair<string, string>(kv.Key, r.ProdCode));
                _prodVocabByDisc = CategoryTokenDefaults.ProdVocabularyByDiscipline(DiscMap, ProdMap, ruleCodes);
                return _prodVocabByDisc;
            }
        }

        /// <summary>
        /// Every PROD code a type name may declare for this document: the corporate
        /// rule codes, the category defaults, and the project overlay's codes.
        /// </summary>
        private static HashSet<string> GetKnownProdCodes(
            Dictionary<string, List<(string Pattern, string ProdCode)>> projRules)
        {
            lock (_prodRulesLock)
            {
                if (_corpKnownProdCodes == null)
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (_csvProdRules != null)
                        foreach (var list in _csvProdRules.Values)
                            foreach (var r in list) set.Add(r.ProdCode);
                    if (ProdMap != null)
                        foreach (string v in ProdMap.Values)
                            if (!string.IsNullOrEmpty(v) && v != "GEN") set.Add(v);
                    _corpKnownProdCodes = set;
                }
                if (projRules == null || projRules.Count == 0) return _corpKnownProdCodes;
                if (_projKnownProdCodes.TryGetValue(projRules, out var cached)) return cached;
                var merged = new HashSet<string>(_corpKnownProdCodes, StringComparer.OrdinalIgnoreCase);
                foreach (var list in projRules.Values)
                    foreach (var r in list) merged.Add(r.ProdCode);
                _projKnownProdCodes[projRules] = merged;
                return merged;
            }
        }

        /// <summary>
        /// Parse a STING_PROD_CODES.csv-format file into category → (pattern, prodCode)
        /// rules. The FAMILY_PATTERN cell is stored upper-cased; matching is
        /// glob/alternation-aware via <see cref="ProdPatternMatcher"/>. Returns null
        /// when the file is missing or unreadable.
        /// </summary>
        private static Dictionary<string, List<(string Pattern, string ProdCode)>> LoadProdCsv(string csvPath)
        {
            if (string.IsNullOrEmpty(csvPath) || !System.IO.File.Exists(csvPath)) return null;
            var map = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
            try
            {
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

                    if (!map.TryGetValue(category, out var list))
                    { list = new List<(string, string)>(); map[category] = list; }
                    list.Add((pattern, prodCode));
                }
            }
            catch (Exception ex) { StingLog.Warn($"TagConfig: LoadProdCsv({csvPath}) failed: {ex.Message}"); return null; }
            return map;
        }

        /// <summary>
        /// The PROD token for an element: family-aware, data-driven (project rules →
        /// corporate STING_PROD_CODES.csv → LPS → sleeve → category default → GEN).
        ///
        /// <para>Always a bare code. Until 2026-09-27 this appended the material suffix
        /// from STING_MATERIAL_PROD_OVERRIDES.csv as "COL-STL". The suffix is joined with
        /// "-", which is also the tag separator, so the result could never be stored:
        /// the source-token sanitiser cut ASS_PRODCT_COD_TXT back to "COL", while the
        /// overwrite path in BuildAndWriteTag composed TAG1 from the unsanitised value and
        /// wrote a 9-segment tag. The material suffix is now a separate reading
        /// (<see cref="GetProdMaterialSuffix"/> / <see cref="GetProdCodeWithMaterial"/>)
        /// and never enters a tag token.</para>
        /// </summary>
        public static string GetFamilyAwareProdCode(Element el, string categoryName)
            => GetFamilyAwareProdCodeCore(el, categoryName, out _);

        /// <summary>
        /// Resolve PROD and report which tier produced it (for the coverage
        /// audit). <paramref name="source"/> ∈ project | declared | corporate | lps |
        /// sleeve | category | gen.
        /// </summary>
        public static string GetFamilyAwareProdCode(Element el, string categoryName, out string source)
            => GetFamilyAwareProdCodeCore(el, categoryName, out source);

        /// <summary>
        /// N+2 — the material-driven suffix (-STL / -CON / -TIM …) from
        /// <c>STING_MATERIAL_PROD_OVERRIDES.csv</c>, or null when no rule matches.
        /// Not part of the PROD tag token — see <see cref="GetFamilyAwareProdCode(Element, string)"/>.
        /// </summary>
        public static string GetProdMaterialSuffix(Element el, string categoryName)
        {
            try { return MaterialProdOverrideRegistry.ResolveSuffix(el, categoryName); }
            catch (Exception ex) { StingLog.Warn($"GetProdMaterialSuffix: {ex.Message}"); return null; }
        }

        /// <summary>
        /// PROD code with the material suffix appended ("DR-STL"). For type marks and
        /// display only: the value contains the tag separator and must never be written
        /// to a source-token parameter or composed into ASS_TAG_1.
        /// </summary>
        public static string GetProdCodeWithMaterial(Element el, string categoryName)
        {
            string baseProd = GetFamilyAwareProdCodeCore(el, categoryName, out _);
            string suffix = GetProdMaterialSuffix(el, categoryName);
            if (string.IsNullOrEmpty(suffix)) return baseProd;
            if (string.IsNullOrEmpty(baseProd)) return suffix;
            // Avoid double-suffixing when an explicit CSV PROD already
            // ends with the same material code (e.g. "STL" → "STL-STL").
            if (baseProd.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return baseProd;
            return $"{baseProd}-{suffix}";
        }

        private static string GetFamilyAwareProdCodeCore(Element el, string categoryName, out string source)
        {
            // KUT-11 — both names must answer for system elements or the rule lookup
            // below is unreachable for them. GetFamilyName now returns the SYSTEM family
            // ("Floor", "Rectangular Duct", "Wall Foundation") and GetElementTypeName the
            // type ("Concrete Slab 200"). Together they are what the eleven corporate
            // rules shipped on Ducts / Floors / Structural Foundations match against;
            // with either half empty those rows had never once fired.
            string familyName = ParameterHelpers.GetFamilyName(el);
            string symbolName = ParameterHelpers.GetElementTypeName(el);

            // Data-driven, single-source PROD resolution. The per-category rule
            // lists are fetched from Revit here (project overlay + corporate CSV);
            // the precedence chain (project → corporate → LPS → sleeve → category
            // default → GEN), the glob matching, and the LPS/sleeve special cases
            // all live in the pure, unit-tested ProdResolver.
            EnsureProdRulesLoaded();
            List<(string Pattern, string ProdCode)> projForCat = null;
            List<(string Pattern, string ProdCode)> corpForCat = null;
            var projRules = GetProjectProdRules(el?.Document);
            if (!string.IsNullOrEmpty(familyName) && categoryName != null)
            {
                projRules?.TryGetValue(categoryName, out projForCat);
                _csvProdRules?.TryGetValue(categoryName, out corpForCat);
            }
            return ProdResolver.Resolve(familyName, symbolName, categoryName,
                                        projForCat, corpForCat, ProdMap, out source,
                                        GetKnownProdCodes(projRules));
        }

        /// <summary>
        /// What a TYPE would resolve to, from names alone and with no element in hand.
        ///
        /// <para>The rename planner has to ask this before anything is placed, and it must
        /// get the same answer the element path gives — same rule lists, same precedence,
        /// same source tier. So it goes through the same <see cref="ProdResolver.Resolve"/>
        /// call rather than a second copy of the chain: a rename that consults a private
        /// re-implementation of the resolver would be protecting a code nothing else
        /// computes.</para>
        ///
        /// <para>Returns the BASE code. The material suffix (<c>-CON</c>, <c>-MAS</c>) is
        /// added by <see cref="GetFamilyAwareProdCode(Element, string)"/> from an element's
        /// materials and has no meaning for a type.</para>
        /// </summary>
        public static string ResolveProdForNames(
            Document doc, string familyName, string typeName, string categoryName, out string source)
        {
            EnsureProdRulesLoaded();
            List<(string Pattern, string ProdCode)> projForCat = null;
            List<(string Pattern, string ProdCode)> corpForCat = null;
            var projRules = GetProjectProdRules(doc);
            if (!string.IsNullOrEmpty(familyName))
            {
                projRules?.TryGetValue(categoryName ?? "", out projForCat);
                _csvProdRules?.TryGetValue(categoryName ?? "", out corpForCat);
            }
            return ProdResolver.Resolve(familyName, typeName, categoryName,
                                        projForCat, corpForCat, ProdMap, out source,
                                        GetKnownProdCodes(projRules));
        }

        /// <summary>
        /// Check if a tag string has the expected number of non-empty tokens.
        /// A tag is only "complete" when it has exactly expectedTokens segments
        /// and none of them are empty strings.
        /// </summary>
        // Removed dead _separatorHistory char[] array — SeparatorHistory list property
        // (loaded from project_config.json) is the actual implementation used in TagIsComplete.

        /// <summary>
        /// TAGACC-4: a stored SEQ in the current scheme and pad ("12" → "0012" at pad 4), or ""
        /// when it cannot be one (letters under Numeric, digits under Alpha, zero, or too large
        /// for the pad) and a new number must be allocated.
        /// </summary>
        internal static string NormaliseHeldSeq(string held)
        {
            if (string.IsNullOrEmpty(held)) return "";
            if (CurrentSeqScheme == SeqScheme.Alpha)
                return held.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z') ? held.ToUpperInvariant() : "";
            if (!held.All(c => c >= '0' && c <= '9')) return "";
            if (!int.TryParse(held, out int n) || n <= 0) return "";
            int pad = EffectiveSeqPad;
            if (n > SeqAssigner.MaxSeqForPad(pad)) return "";
            return SeqAssigner.BuildSeqString(n, CurrentSeqScheme, pad);
        }

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
            // Reject tags containing UNRESOLVED placeholder tokens (XX / ZZ / 0000).
            //
            // GEN is deliberately not one of them. It is the token policy's documented
            // fallback for SYS / FUNC / PROD — a tag carrying it is complete-but-ASSUMED
            // (G-27), which TagIsFullyResolved / TagHasPlaceholders still report. Treating
            // GEN as incomplete meant such a tag was never "complete": every Batch Tag
            // re-processed the element, the write-time check logged "a mandatory segment
            // is blank" for a tag with no blank segment, and compliance counted it with
            // the genuinely broken ones.
            if (HasPlaceholderSegment(tagValue, _unresolvedPlaceholders))
                return false;

            return true;
        }

        /// <summary>
        /// Checks whether a tag string contains placeholder tokens ("-XX-", "-ZZ-", "-GEN-", "-0000")
        /// that indicate unresolved or assumed segments — the STRICT test behind
        /// <see cref="TagIsFullyResolved"/> and the compliance "resolved" metric.
        /// </summary>
        public static bool TagHasPlaceholders(string tag)
            => HasPlaceholderSegment(tag, _placeholders);

        private static bool HasPlaceholderSegment(string tag, HashSet<string> placeholders)
        {
            if (string.IsNullOrEmpty(tag))
                return false;
            string sep = !string.IsNullOrEmpty(Separator) ? Separator : "-";
            // Whole segments only (not a substring of a real token). The unassigned SEQ is
            // all zeros at whatever the pad width is (SeqAssigner.IsUnassignedSeq), not the
            // literal "0000", which only matched at pad 4.
            foreach (string part in tag.Split(new[] { sep }, StringSplitOptions.None))
                if (placeholders.Contains(part) || SeqAssigner.IsUnassignedSeq(part))
                    return true;
            return false;
        }

        private static readonly HashSet<string> _placeholders = new HashSet<string> { "XX", "ZZ", "GEN" };

        /// <summary>Placeholders that make a tag INCOMPLETE (plus an all-zero SEQ). GEN is an
        /// assumed value, not an unresolved one — see <see cref="TagIsComplete"/>.</summary>
        private static readonly HashSet<string> _unresolvedPlaceholders = new HashSet<string> { "XX", "ZZ" };

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
        /// <summary>
        /// Why BuildAndWriteTag returned what it returned.
        ///
        /// TAGLOG-1. The bool says "was this element tagged", and the caller logged every
        /// false as "BuildAndWriteTag failed". But TagCollisionMode.Skip returns false on
        /// purpose for an element that already carries a complete tag — so a clean run of
        /// 332 elements emitted 278 WARN lines claiming failure and 0 real ones. Noise at
        /// that ratio is worse than no logging: it buries the one line that matters. In
        /// the run that found this, exactly one element had a genuine fault and it sat
        /// inside 278 false alarms.
        ///
        /// The report is OPTIONAL and additive. Every existing return value is unchanged,
        /// so the eleven call sites that ignore the bool keep their exact behaviour; only
        /// RunFullPipeline passes a report, and it now warns solely on Failed.
        /// </summary>
        public enum TagWriteOutcome
        {
            Tagged,
            AlreadyCurrent,
            SkippedComplete,
            NotTaggable,
            Failed,
            /// <summary>TAGACC-13: a new SEQ was needed but another user holds the counter;
            /// retry after they synchronise. Not a fault.</summary>
            Deferred,
        }

        /// <summary>Optional out-channel for <see cref="TagWriteOutcome"/>.</summary>
        public sealed class TagWriteReport
        {
            public TagWriteOutcome Outcome { get; private set; } = TagWriteOutcome.Failed;
            public void Set(TagWriteOutcome o) { Outcome = o; }
            public bool IsDeliberateSkip =>
                Outcome == TagWriteOutcome.SkippedComplete
                || Outcome == TagWriteOutcome.AlreadyCurrent
                || Outcome == TagWriteOutcome.NotTaggable
                || Outcome == TagWriteOutcome.Deferred;
        }

        public static bool BuildAndWriteTag(Document doc, Element el,
            Dictionary<string, int> sequenceCounters, bool skipComplete = true,
            HashSet<string> existingTags = null,
            TagCollisionMode collisionMode = TagCollisionMode.AutoIncrement,
            TaggingStats stats = null,
            string cachedRev = null,
            List<Phase> cachedPhases = null,
            ElementId lastPhaseId = null,
            string prevTagHint = null,
            string[] tokenValuesOut = null,
            TagWriteReport report = null,
            bool forceRebuild = false)
        {
            string catName = ParameterHelpers.GetCategoryName(el);
            // F-14: Merge ContainsKey guard + TryGetValue into a single map lookup
            if (string.IsNullOrEmpty(catName) || !DiscMap.TryGetValue(catName, out string disc))
            {
                report?.Set(TagWriteOutcome.NotTaggable);
                return false;
            }

            // RunFullPipeline already read TAG1 once — accept
            // the value via the new prevTagHint parameter to avoid a second read.
            string existingTag = prevTagHint
                ?? ParameterHelpers.GetString(el, ParamRegistry.TAG1);
            bool hasCompleteTag = TagIsComplete(existingTag);

            // TAGACC-1: a complete tag that an older, still-existing element also holds is a
            // copy (copy / paste / array / mirror copy instance parameters). It is not a
            // valid identifier for this element, so it is rebuilt with a new SEQ whatever the
            // collision mode — skipping it as "complete" left the duplicate in the model.
            bool duplicateHolder = hasCompleteTag
                && IsDuplicateTagHolder(doc, existingTags, existingTag, el);
            if (duplicateHolder)
            {
                hasCompleteTag = false;
                StingLog.WarnRateLimited("DuplicateTagHolder",
                    $"Element {el.Id} holds '{existingTag}', which an older element also holds (a copy) — re-sequencing it");
                stats?.RecordWarning($"Element {el.Id}: duplicate of an existing tag '{existingTag}' (copied element) — re-sequenced");
            }

            // TAGACC-5: the caller found the element has moved (another level, or flagged
            // stale) and re-derived its spatial tokens, so its complete tag is out of date.
            if (forceRebuild) hasCompleteTag = false;

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
                    report?.Set(TagWriteOutcome.AlreadyCurrent);
                    return true;
                }
            }

            if (hasCompleteTag)
            {
                switch (collisionMode)
                {
                    case TagCollisionMode.Skip:
                        stats?.RecordSkipped(catName);
                        report?.Set(TagWriteOutcome.SkippedComplete);
                        return false; // Never touch existing complete tags
                    case TagCollisionMode.AutoIncrement:
                        if (skipComplete)
                        {
                            stats?.RecordSkipped(catName);
                            report?.Set(TagWriteOutcome.SkippedComplete);
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

            // ONE set of token values drives the SEQ counter key, the collision check and
            // the tag that is written.
            //
            // Until 2026-09-27 they came from three places. The counter key used a derived
            // DISC and LVL, the collision check used a tag built from derived values, and
            // the written tag was then rebuilt from whatever the element STORED (non-overwrite
            // keeps stored tokens). An element whose stored LVL no longer matched its level —
            // tokens pre-populated, element moved, SEQ still blank — took a number from the
            // new level's counter, was tagged under the old level, and that tag was never
            // collision-checked. That is a duplicate-tag path.
            //
            // Now: on the non-overwrite path a real stored token wins over derivation (it is
            // what will be written), and derivation only fills blanks. ReadTokenValues
            // sanitises, so junk ("-", a concatenated family name) reads as blank and is
            // repaired rather than trusted. The counter seeding in BuildTagIndexAndCounters
            // keys on the same stored values through the same SeqAssigner.BuildSeqKey.
            string[] stored = overwriteTokens ? null : ParamRegistry.ReadTokenValues(el);
            string Stored(int i) => (stored != null && i < stored.Length) ? (stored[i] ?? "") : "";

            // Overwrite re-reads the raw parameter (LOC/ZONE are derived upstream by
            // PopulateAll, not here); either way a value that sanitises to nothing is blank.
            string RawToken(string param)
            {
                string v = ParameterHelpers.GetString(el, param);
                return ParamRegistry.IsTokenEffectivelyEmpty(v) ? "" : v.Trim();
            }

            // Overwrite re-derives DISC / LVL / SYS / FUNC / PROD below, but three things
            // must still win over a fresh derivation, exactly as they do in RunFullPipeline
            // (which applies them after PopulateAll and before calling here): a token
            // listed in ASS_TOKEN_LOCK_TXT, a CATEGORY_TOKEN_OVERRIDES value, and a
            // CATEGORY_FORCE_SYS system. Until 2026-09-29 the overwrite path derived these
            // afresh and wrote them with overwrite:true, so "Overwrite all" silently undid
            // every token lock and category override the pipeline had just restored.
            HashSet<string> lockedKeys = null;
            Dictionary<string, string> catOverrides = null;
            if (overwriteTokens)
            {
                string lockStr = ParameterHelpers.GetString(el, "ASS_TOKEN_LOCK_TXT");
                if (!string.IsNullOrWhiteSpace(lockStr))
                    lockedKeys = new HashSet<string>(
                        lockStr.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0),
                        StringComparer.OrdinalIgnoreCase);
                CategoryTokenOverrides?.TryGetValue(catName, out catOverrides);
            }
            string Pinned(string key, string param)
            {
                if (!overwriteTokens) return "";
                if (lockedKeys != null && lockedKeys.Contains(key))
                {
                    string held = RawToken(param);
                    if (held.Length > 0) return held;
                }
                if (catOverrides != null)
                    foreach (var kv in catOverrides)
                        if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)
                            && !ParamRegistry.IsTokenEffectivelyEmpty(kv.Value))
                            return kv.Value.Trim();
                if (key == "SYS" && CategoryForceSys != null
                    && CategoryForceSys.TryGetValue(catName, out string forced)
                    && !ParamRegistry.IsTokenEffectivelyEmpty(forced))
                    return forced.Trim();
                return "";
            }

            string loc = overwriteTokens ? RawToken(ParamRegistry.LOC) : Stored(1);
            // TAGACC-3: a blank LOC or ZONE is left blank here and resolved by the token
            // policy below (ResolveToken), which substitutes the policy's fallback and
            // RECORDS it, or refuses the tag when a project sets the fallback to null.
            // Until 2026-09-29 LOC was forced to a hardcoded "XX" here while PopulateAll
            // wrote "BLD1", and ZONE was forced to the first ZONE_CODES entry, so the two
            // layers disagreed and neither honoured the policy. PopulateAll now uses the
            // same policy, so a blank reaching this point is rare (a caller that skipped
            // PopulateAll); it is counted with the others.
            if (string.IsNullOrEmpty(loc))
                System.Threading.Interlocked.Increment(ref _unresolvedLocCount);

            string zone = overwriteTokens ? RawToken(ParamRegistry.ZONE) : Stored(2);

            string lvl = Stored(3);
            if (string.IsNullOrEmpty(lvl)) lvl = Pinned("LVL", ParamRegistry.LVL);
            if (string.IsNullOrEmpty(lvl))
            {
                lvl = ParameterHelpers.GetLevelCode(doc, el);
                // "XX" is GetLevelCode's "this element has no level". Normalise it to empty
                // and let the token policy below decide — it substitutes "L00" and RECORDS
                // the substitution. A project that would rather refuse to tag a levelless
                // element sets LVL's fallback to null in _BIM_COORD/tag_token_policy.json.
                if (lvl == "XX") lvl = "";
            }

            // On the non-overwrite path we trust whatever PopulateAll already wrote —
            // reading the element bypasses the expensive per-element MEP connector walk.
            // On the overwrite path we deliberately derive afresh so users can force a
            // re-detect.
            //
            // A derived "GEN" is left BLANK here so the token policy supplies it: that is
            // what records the element as complete-but-assumed (and lets a project refuse
            // instead). Until 2026-09-27 SYS, FUNC and PROD each had a hardcoded "GEN"
            // literal ahead of the policy, so the policy's fallback was never reached, no
            // substitution was ever recorded, and a project override changed nothing.
            string sys = Stored(4);
            if (string.IsNullOrEmpty(sys)) sys = Pinned("SYS", ParamRegistry.SYS);
            if (string.IsNullOrEmpty(sys))
            {
                // Intelligence Layer: 6-layer system detection:
                // connector → sys param → circuit → family → room → category
                sys = GetMepSystemAwareSysCode(el, catName);
                if (string.IsNullOrEmpty(sys))
                    sys = GetDiscDefaultSysCode(disc);
                if (sys == "GEN") sys = "";
            }

            // Intelligence Layer: System-aware DISC correction for pipes
            // Pipes are mapped to "M" by default, but if the connected system is plumbing
            // (DCW, DHW, SAN, RWD, GAS), the DISC should be "P" (Plumbing).
            disc = GetSystemAwareDisc(disc, sys, catName);
            if (!string.IsNullOrEmpty(Stored(0))) disc = Stored(0);
            else
            {
                string pinnedDisc = Pinned("DISC", ParamRegistry.DISC);
                if (pinnedDisc.Length > 0) disc = pinnedDisc;
            }

            string func = Stored(5);
            if (string.IsNullOrEmpty(func)) func = Pinned("FUNC", ParamRegistry.FUNC);
            if (string.IsNullOrEmpty(func))
            {
                // Smart FUNC from the SYS that will be WRITTEN, so the pair always agrees:
                // HVAC (SUP/RTN/EXH/FRA), HWS (HTG/DHW), SAN (VNT), LPS (AT/DC/EE/BOND/SPD/TC).
                func = GetSmartFuncCode(el, sys);
                if (func == "GEN") func = "";
            }

            string prod = Stored(6);
            if (string.IsNullOrEmpty(prod)) prod = Pinned("PROD", ParamRegistry.PROD);
            if (string.IsNullOrEmpty(prod))
            {
                prod = GetFamilyAwareProdCode(el, catName);
                if (prod == "GEN") prod = "";
            }

            // Default-value counts. PopulateAll records where LOC / ZONE came from; a
            // "Default" source means nothing located the element and the policy fallback
            // was written. (These compared the value with "BLD1" / "Z01", which counted a
            // room that genuinely says BLD1 and missed a project whose fallback is not BLD1.)
            if (stats != null)
            {
                if (string.IsNullOrEmpty(loc)
                    || ParameterHelpers.GetString(el, ParamRegistry.LOC_SOURCE) == "Default")
                    stats.DefaultLocCount++;
                if (string.IsNullOrEmpty(zone)
                    || ParameterHelpers.GetString(el, ParamRegistry.ZONE_SOURCE) == "Default")
                    stats.DefaultZoneCount++;
            }

            // Validate-before-write. Which tokens may legitimately fall back, which must
            // never, and what value each falls back TO, is corporate-baseline DATA —
            // Data/STING_TAG_TOKEN_POLICY.json, overridable per project at
            // _BIM_COORD/tag_token_policy.json — because sectors disagree (a hospital
            // treats ZONE as mandatory, a single-building lodge does not).
            //
            // A token the policy gives no fallback is REFUSED, not guessed: the element is
            // skipped and counted. The shipped baseline gives every one of the seven tag
            // segments a fallback, so only a project override can turn refusal on.
            var _policy = TagTokenPolicyRegistry.Get(doc);
            bool anyFallback = false;

            bool ResolveToken(string tokenName, ref string slot)
            {
                if (!string.IsNullOrEmpty(slot)) return true;

                var r = TagTokenPolicy.Resolve(_policy, tokenName, slot);
                if (r.Refused)
                {
                    string why = $"{tokenName} is blank and the tag token policy offers no "
                               + $"fallback, so no tag was written for element {el.Id}. "
                               + (r.Reason ?? "");
                    StingLog.WarnRateLimited("TokenPolicyRefusal", why);
                    stats?.RecordTokenRefusal(r.Reason ?? tokenName + " is blank", el.Id?.Value ?? -1);
                    return false;
                }

                slot = r.Value;
                if (r.Substituted)
                {
                    anyFallback = true;
                    stats?.RecordTokenSubstitution(tokenName, r.Level == TagTokenLevel.Mandatory);
                    if (!string.IsNullOrEmpty(r.Reason))
                        StingLog.WarnRateLimited("TokenPolicyGap", r.Reason);
                }
                return true;
            }

            // A GEN that was already STORED (written by PopulateAll or an earlier run) is
            // the same assumption as one substituted now; count it the same way, or a
            // re-run would report the tag as fully measured.
            void NoteStoredGeneric(string tokenName, string value)
            {
                if (value != "GEN") return;
                anyFallback = true;
                stats?.RecordTokenSubstitution(tokenName, false);
            }
            NoteStoredGeneric("SYS", sys);
            NoteStoredGeneric("FUNC", func);
            NoteStoredGeneric("PROD", prod);

            if (!ResolveToken("DISC", ref disc)) return false;
            if (!ResolveToken("LOC",  ref loc))  return false;
            if (!ResolveToken("ZONE", ref zone)) return false;
            if (!ResolveToken("LVL",  ref lvl))  return false;
            if (!ResolveToken("SYS",  ref sys))  return false;
            if (!ResolveToken("FUNC", ref func)) return false;
            if (!ResolveToken("PROD", ref prod)) return false;

            string seqKey = SeqAssigner.BuildSeqKey(disc, sys, lvl, zone, loc, SeqIncludeZone, SeqIncludeLoc);

            // A1: Warn once per session when SEQ scheme has changed — counter keys may not
            // match existing tags, leading to duplicate or restarted sequences.
            if (_seqSchemeChanged && !_seqSchemeWarned)
            {
                StingLog.Warn($"SEQ scheme changed (scheme={CurrentSeqScheme}, includeZone={SeqIncludeZone}, includeLoc={SeqIncludeLoc}). " +
                    "Existing SEQ counters may not align with the new key format. " +
                    "Run 'Batch Tag' with Overwrite and RENUMBER_ON_OVERWRITE = true in project_config.json to re-sequence.");
                _seqSchemeWarned = true;
            }

            string seqSchemeContext = CurrentSeqScheme == SeqScheme.ZonePrefix ? zone
                                   : CurrentSeqScheme == SeqScheme.DiscPrefix ? disc
                                   : "";

            string tagBody = string.Join(Separator, disc, loc, zone, lvl, sys, func, prod);
            if (!string.IsNullOrEmpty(TagPrefix)) tagBody = TagPrefix + Separator + tagBody;
            tagBody += Separator;
            string tagSuffix = string.IsNullOrEmpty(TagSuffix) ? string.Empty : Separator + TagSuffix;

            // Snapshot the counter so any later failure can restore it.
            int seqPreAlloc = sequenceCounters.TryGetValue(seqKey, out int _preAlloc) ? _preAlloc : 0;

            // The element's current tag is being replaced; take it out of the collision
            // index so it cannot collide with itself. Put back on any failure below.
            // A duplicate holder's tag belongs to the older element: it stays in the index.
            bool removedOwnTag = !duplicateHolder
                                 && existingTags != null && !string.IsNullOrEmpty(existingTag)
                                 && existingTags.Remove(existingTag);
            void RestoreOwnTag() { if (removedOwnTag) existingTags.Add(existingTag); }

            // An element that already HOLDS a sequence number keeps it. Until 2026-09-27
            // a number was allocated for it anyway, SetTokenIfEmpty then kept the stored
            // one, and the allocated number was never returned — so every re-run over an
            // element with an incomplete tag burned one number per element per group, and
            // the sidecar persisted the gaps.
            string seq = null;
            string tag = null;
            bool seqAllocated = false;
            bool seqReassigned = false;
            // TAGACC-4: Overwrite keeps the stored SEQ too (normalised to the current pad),
            // unless RENUMBER_ON_OVERWRITE is set. It used to allocate a fresh number for every
            // element on every Overwrite run, so printed tags, QR labels and COBie exports
            // stopped matching the model. A duplicate holder's SEQ is the source element's
            // and is never reused.
            string storedSeq = duplicateHolder ? ""
                : !overwriteTokens ? Stored(7)
                : RenumberOnOverwrite ? ""
                : NormaliseHeldSeq(RawToken(ParamRegistry.SEQ));
            if (duplicateHolder) seqReassigned = true;
            if (!string.IsNullOrEmpty(storedSeq))
            {
                string candidate = tagBody + storedSeq + tagSuffix;
                if (existingTags == null || !existingTags.Contains(candidate))
                {
                    seq = storedSeq;
                    tag = candidate;
                    // Keep the counter ahead of every number in use, so a later
                    // allocation in this group cannot hand the same one out.
                    int held = int.TryParse(storedSeq, out int n) ? n
                             : CurrentSeqScheme == SeqScheme.Alpha ? FromAlpha(storedSeq) : 0;
                    if (held > seqPreAlloc) sequenceCounters[seqKey] = held;
                }
                else
                {
                    // Another element already carries this exact tag. AutoIncrement means
                    // "increment SEQ on collision", so allocate a fresh number and replace
                    // the stored one rather than write a duplicate identifier.
                    seqReassigned = true;
                    StingLog.Warn($"Element {el.Id}: stored SEQ '{storedSeq}' would duplicate tag '{candidate}' — allocating a new SEQ");
                    stats?.RecordWarning($"Element {el.Id}: SEQ {storedSeq} duplicated an existing tag — re-sequenced");
                }
            }

            // TAGACC-13: a NEW number may only be handed out while this user holds the
            // model's SEQ counter (worksharing lock). An element that keeps its stored SEQ
            // never reaches here.
            if (seq == null && !StingTools.Core.Storage.StingSeqLockStore.AllocationAllowed(doc, out string lockReason))
            {
                if (string.Equals(SeqLockMode, "warn", StringComparison.OrdinalIgnoreCase))
                {
                    StingLog.WarnRateLimited("SeqLockWarn",
                        $"Allocating SEQ without the worksharing lock ({lockReason}); duplicates are repaired after sync.");
                }
                else
                {
                    StingLog.WarnRateLimited("SeqLockBlocked",
                        $"No new sequence numbers: {lockReason}. Elements needing one are deferred.");
                    stats?.RecordTokenRefusal("SEQ deferred — " + lockReason, el.Id?.Value ?? -1);
                    RestoreOwnTag();
                    report?.Set(TagWriteOutcome.Deferred);
                    return false;
                }
            }

            if (seq == null)
            {
                int seqPad = EffectiveSeqPad;
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
                    RestoreOwnTag();
                    return false; // AssignNext already rolled the counter back
                }

                seq = seqRes.Seq;
                tag = seqRes.Tag;
                seqAllocated = true;
                if (seqRes.CollisionCount > 0)
                    stats?.RecordCollision(tag, seqRes.CollisionCount);
            }

            string[] _cachedReadTokens;
            string[] intended = { disc, loc, zone, lvl, sys, func, prod, seq };

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
                _cachedReadTokens = intended;
            }
            else
            {
                ParameterHelpers.SetTokenIfEmpty(el, ParamRegistry.DISC, disc);
                ParameterHelpers.SetTokenIfEmpty(el, ParamRegistry.LOC, loc);
                ParameterHelpers.SetTokenIfEmpty(el, ParamRegistry.ZONE, zone);
                ParameterHelpers.SetTokenIfEmpty(el, ParamRegistry.LVL, lvl);
                ParameterHelpers.SetTokenIfEmpty(el, ParamRegistry.SYS, sys);
                ParameterHelpers.SetTokenIfEmpty(el, ParamRegistry.FUNC, func);
                ParameterHelpers.SetTokenIfEmpty(el, ParamRegistry.PROD, prod);
                if (seqReassigned)
                    ParameterHelpers.SetString(el, ParamRegistry.SEQ, seq, overwrite: true);
                else
                    ParameterHelpers.SetTokenIfEmpty(el, ParamRegistry.SEQ, seq);

                // Read back. Every stored value was either used above or blank and just
                // filled, so this matches `intended` unless a write did not take (token
                // parameter unbound or read-only). Then TAG1 must still describe what the
                // element actually holds, so compose it from the read-back — and return a
                // number that was allocated but not stored.
                string[] actualTokens = ParamRegistry.ReadTokenValues(el);
                _cachedReadTokens = actualTokens;
                if (actualTokens.Length < 8)
                {
                    if (seqAllocated) sequenceCounters[seqKey] = seqPreAlloc;
                    RestoreOwnTag();
                    return false;
                }
                bool matches = true;
                for (int i = 0; i < 8; i++)
                    if (!string.Equals(actualTokens[i] ?? "", intended[i], StringComparison.Ordinal)) { matches = false; break; }
                if (!matches)
                {
                    // Name the parameters, not just the fact: an operator needs to know
                    // which token parameter to re-bind, and on which category.
                    var failedParams = new List<string>();
                    var tokenParams = ParamRegistry.AllTokenParams;
                    for (int i = 0; i < 8; i++)
                        if (!string.Equals(actualTokens[i] ?? "", intended[i], StringComparison.Ordinal))
                            failedParams.Add(i < tokenParams.Length ? tokenParams[i] : $"token[{i}]");
                    stats?.RecordTokenWriteFailure(el.Id.Value, catName, failedParams);
                    StingLog.WarnRateLimited("TokenWriteMismatch",
                        $"Element {el.Id}: token write did not take (stored '{string.Join(Separator, actualTokens)}', "
                      + $"intended '{string.Join(Separator, intended)}') — check the token parameters are bound and writable");
                    if (seqAllocated && !string.Equals(actualTokens[7], seq, StringComparison.Ordinal))
                        sequenceCounters[seqKey] = seqPreAlloc;
                    tag = string.Join(Separator, actualTokens);
                    if (!string.IsNullOrEmpty(TagPrefix)) tag = TagPrefix + Separator + tag;
                    if (!string.IsNullOrEmpty(TagSuffix)) tag = tag + Separator + TagSuffix;
                    disc = actualTokens[0];
                    sys = actualTokens[4];
                    lvl = actualTokens[3];
                }
            }

            // Segment-count guard. The tag was built from eight values none of which can
            // contain the separator (they are policy-resolved or sanitised on read), so
            // this only fires on the read-back path above.
            {
                // Counting separators cannot see a BLANK segment, and a blank segment is
                // the failure this guard exists to catch: "A-BLD1-Z01-L01-ARC---" carries
                // exactly seven separators, so it passed as eight segments. Check each one.
                int expectedSegments = 8
                    + (!string.IsNullOrEmpty(TagPrefix) ? 1 : 0)
                    + (!string.IsNullOrEmpty(TagSuffix) ? 1 : 0);
                if (!TagTokenIntegrity.AllSegmentsPresent(tag, Separator, expectedSegments))
                {
                    StingLog.Warn($"Malformed tag for element {el.Id}: '{tag}' is not {expectedSegments} non-empty segments");
                    stats?.RecordWarning($"Element {el.Id}: malformed tag '{tag}' — a segment is missing or blank, skipped");
                    if (seqAllocated) sequenceCounters[seqKey] = seqPreAlloc;
                    RestoreOwnTag();
                    return false;
                }
            }
            bool tagWriteSucceeded = ParameterHelpers.SetString(el, ParamRegistry.TAG1, tag, overwrite: true);

            // SEQ counter fix: rollback increment if TAG1 write failed
            if (!tagWriteSucceeded)
            {
                sequenceCounters[seqKey] = seqPreAlloc;
                RestoreOwnTag();
                StingLog.Warn($"TAG1 write failed on {el.Id} — SEQ counter rolled back for key '{seqKey}'");
                stats?.RecordWarning($"Element {el.Id}: TAG1 write failed — SEQ rolled back");
                report?.Set(TagWriteOutcome.Failed);
                return false;
            }

            // G-42 — completeness at WRITE time.
            //
            // TagIsComplete already existed and was called from eight sites
            // (ComplianceScan:690/842/843/1013, BOQSupportCommands:135,
            // BIMManagerCommands:4068/4909/8950). EVERY ONE IS A READER. Nothing
            // checked at the moment of writing, which is why a tag with two blank
            // segments reached a drawing without a word. Reusing the existing check
            // rather than writing a second one — a parallel notion of "complete" is
            // how the two take-off paths diverged.
            //
            // anyFallback carries G-27's distinction through: a tag completed by
            // substituting GEN is complete-but-ASSUMED, which is not the same as
            // complete, and counting them together would hide exactly what this is
            // meant to surface.
            try
            {
                bool complete = TagIsComplete(tag);
                stats?.RecordTagCompleteness(complete, anyFallback, tag, el.Id?.Value ?? -1);
                if (!complete)
                    StingLog.WarnRateLimited("IncompleteTag",
                        $"Incomplete tag written on {el.Id}: '{tag}'. A segment is blank or unresolved (XX / ZZ / 0000) — "
                      + "see Data/STING_TAG_TOKEN_POLICY.json for which tokens may fall back.");
            }
            catch (Exception ex) { StingLog.Warn($"Tag completeness check on {el.Id}: {ex.Message}"); }

            // ASS_DISPLAY_TXT is the ON-DRAWING tag: the display-mode + segment-mask
            // resolved rendering of the canonical ASS_TAG_1_TXT. Let BuildDisplayTag
            // compute AND write it (it resolves STING_DISPLAY_MODE / DisplayModeDefault,
            // applies any active TAG_SEG_MASK_TXT / STING_VIEW_TOKEN_MASK_TXT / UI
            // "TokenMask", and SetStrings the result). The token params it reads were
            // just written above (lines ~2301/2318), so the tokens are in scope here.
            // Fall back to the full canonical tag only when BuildDisplayTag yields
            // nothing (element has no tokens / ASS_DISPLAY_TXT unbound) so the display
            // never goes blank. ASS_TAG_1_TXT stays the full key — always recoverable.
            string displayResolved = BuildDisplayTag(el);
            if (string.IsNullOrEmpty(displayResolved))
                ParameterHelpers.SetString(el, ParamRegistry.DISPLAY_TXT, tag, overwrite: true);

            // 5.3: Re-read TAG1 to catch write failures and add to existingTags
            // to prevent same-batch duplicates even when existingTags was null at entry
            {
                string writtenTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(writtenTag) && writtenTag != tag)
                    StingLog.Warn($"TAG1 write mismatch on {el.Id}: wrote '{tag}', read back '{writtenTag}'");
                // Ensure the tag is in the index for same-batch duplicate prevention
                if (existingTags != null && !string.IsNullOrEmpty(writtenTag))
                {
                    existingTags.Add(writtenTag);
                    // TAGACC-1: this element now owns the tag it holds; release the old one.
                    ClaimTag(existingTags, writtenTag, duplicateHolder ? null : existingTag, el.Id.Value);
                }
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
                // Containers are a pure function of the tokens and TAG1 was just
                // (re)written from them, so they are always brought into line. With
                // overwrite:false a container that already held an older value (a
                // partial tag from an earlier run, a SEQ re-sequenced for a duplicate,
                // values copied with a pasted element) was left disagreeing with TAG1.
                ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: true);

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
                // CONTAINER-1: this logged a bare message, swallowed the exception and
                // reported nothing to the user, so 175 of these sat in a log unread and
                // undiagnosable — no stack trace, no category, no count in the report.
                // The failure is still non-fatal (the tag itself is already written), but
                // it is now visible and traceable.
                StingLog.Error($"Container write failed for {el.Id} (category '{catName}')", ex);
                stats?.RecordContainerWriteFailure(catName, ex.Message);
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
            string displayModeSentinel = ParameterHelpers.GetValueText(el, ParamRegistry.DISPLAY_MODE);
            if (!string.IsNullOrEmpty(displayModeSentinel))
            {
                stats?.RecordTagged(catName, disc, sys, lvl);
                report?.Set(TagWriteOutcome.Tagged);
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
                    // D11 — shipped tier-gate defaults: tiers 1 and 2 ON, 3..10 OFF.
                    // Tier 1 alone renders an identity code with no context; tier 2 adds
                    // the material/system line a reviewer needs to recognise what the
                    // tag is on. Tiers 3+ stay off so a stock tag is readable.
                    ParameterHelpers.SetYesNo(el, ParamRegistry.PARA_STATE_1, true);
                    ParameterHelpers.SetYesNo(el, ParamRegistry.PARA_STATE_2, true);
                }

                // D11 — TAG_WARN_VISIBLE_BOOL = Yes.
                //
                // It shipped OFF "to avoid expensive per-element warning evaluation",
                // which silently disabled the warning surface entirely: the tag
                // completeness enforcement added under G-42 counts and reports incomplete
                // tags, and the operator could never SEE any of it on the drawing because
                // the family's warning row was gated off by default.
                //
                // A check whose output is invisible is the same defect as a check nobody
                // calls (G-48). Cost is a per-element evaluation on WriteTag7All; the
                // alternative was silence.
                ParameterHelpers.SetYesNo(el, ParamRegistry.WARN_VISIBLE, true);

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
            report?.Set(TagWriteOutcome.Tagged);
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
            //   2. TAG_SEG_MASK_TXT on the element — written PER-ELEMENT by
            //      TokenProfileApplier step 7.5 (FIX-3a: was previously written
            //      to the view, where this consumer never read it).
            //   3. UI ExtraParam "TokenMask" — ad-hoc preview override.
            // D5 (Phase 196): the mask now applies in EVERY display mode, not
            // just 0/5. A mask selects which of the 8 canonical segments show,
            // so it is applied to the FULL 8-token string — not the mode-derived
            // compact form, whose 1-4 segments would give an 8-char mask nothing
            // to map onto. A real mask therefore defines visibility 1:1 and
            // overrides the mode's segment choice; when no real mask is set the
            // mode-derived display stands.
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

                if (!string.IsNullOrEmpty(mask) && mask.Length >= 8 && mask != "11111111")
                {
                    // Map the mask over the canonical 8 segments (not the
                    // compact mode-derived string), so it applies in every mode.
                    string[] maskTokens = ParamRegistry.ReadTokenValues(el);
                    if (maskTokens != null && maskTokens.Length >= 8)
                    {
                        string fullEight = string.Join(ParamRegistry.Separator, maskTokens);
                        string masked = ApplySegmentMask(fullEight, mask);
                        if (!string.IsNullOrEmpty(masked)) display = masked;
                    }
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

        /// <summary>
        /// Layer 1: Read connected MEP system name via connectors.
        ///
        /// TAGACC-7: this returned the FIRST connector whose system mapped, so an AHU
        /// (supply + return air + chilled water), a boiler (LTHW + gas) or a sink (DCW + DHW
        /// + drainage) took whichever service the family author happened to place first.
        /// Now, deterministically: a connector the family marks PRIMARY wins; otherwise the
        /// service in the category's own domain (air for Mechanical Equipment / Air Terminals,
        /// piping for Plumbing Fixtures and pipework); otherwise the first non-auxiliary
        /// service (gas, fuel, condensate and drainage connections are auxiliary on
        /// equipment); otherwise the first mapped one.
        /// </summary>
        private static string GetSysFromConnector(Element el, string categoryName = null)
        {
            try
            {
                FamilyInstance fi2 = el as FamilyInstance;
                if (fi2?.MEPModel?.ConnectorManager == null) return null;

                // The decision is SysConnectorChoice.Choose (Revit-free, tested — TAGACC-20);
                // this loop only reads the connectors, in order.
                var services = new List<ConnectorService>();
                foreach (Connector conn in fi2.MEPModel.ConnectorManager.Connectors)
                {
                    if (conn?.MEPSystem == null) continue;
                    string sysName = conn.MEPSystem.Name?.ToUpperInvariant() ?? "";
                    string mapped = MapSystemNameToCode(sysName, categoryName);
                    if (string.IsNullOrEmpty(mapped)) continue;
                    mapped = RefineHydronic(mapped, sysName, doc: el.Document,
                        systemTypeId: conn.MEPSystem.GetTypeId());

                    bool primary = false;
                    try { primary = conn.GetMEPConnectorInfo()?.IsPrimary == true; }
                    catch (Exception ciEx) { StingLog.WarnRateLimited("SysConnectorInfo", $"Connector info unreadable on {el.Id}: {ciEx.Message}"); }
                    if (primary) return mapped;   // nothing later can outrank it

                    Domain d = Domain.DomainUndefined;
                    try { d = conn.Domain; } catch (Exception dEx) { StingLog.WarnRateLimited("SysConnectorDomain", $"Connector domain unreadable on {el.Id}: {dEx.Message}"); }
                    services.Add(new ConnectorService(mapped, false, ToServiceDomain(d)));
                }
                return SysConnectorChoice.Choose(services, SysConnectorChoice.PreferredDomain(categoryName));
            }
            catch (Exception ex) { StingLog.Warn($"SYS detection from connector failed: {ex.Message}"); }
            return null;
        }

        private static ServiceDomain ToServiceDomain(Domain d)
        {
            switch (d)
            {
                case Domain.DomainHvac: return ServiceDomain.Hvac;
                case Domain.DomainPiping: return ServiceDomain.Piping;
                case Domain.DomainElectrical: return ServiceDomain.Electrical;
                default: return ServiceDomain.Undefined;
            }
        }

        /// <summary>
        /// TAGACC-8: Revit's default hydronic classification ("Hydronic Supply / Return")
        /// serves chilled water as well as heating, and the name classifier reads it as HWS.
        /// When the code came from the word HYDRONIC and the piping system type carries a
        /// design fluid temperature at or below 15 °C, the system is chilled water.
        /// </summary>
        private static string RefineHydronic(string code, string sourceText, Document doc, ElementId systemTypeId)
        {
            // Only an HWS read from the word HYDRONIC is in question; skip the API read otherwise.
            if (SysConnectorChoice.RefineHydronic(code, sourceText, SysConnectorChoice.ChilledWaterMaxKelvin) == code) return code;
            double? kelvin = null;
            try
            {
                if (doc != null && systemTypeId != null && systemTypeId != ElementId.InvalidElementId
                    && doc.GetElement(systemTypeId) is Autodesk.Revit.DB.Plumbing.PipingSystemType pst)
                    kelvin = pst.FluidTemperature;   // Revit internal units: kelvin
            }
            catch (Exception ex) { StingLog.WarnRateLimited("RefineHydronic", $"Hydronic temperature check: {ex.Message}"); }
            return SysConnectorChoice.RefineHydronic(code, sourceText, kelvin);
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
                ElementId pipeSysTypeId = null;
                if (pipeSys != null && pipeSys.HasValue)
                {
                    try { pipeSysTypeId = pipeSys.AsElementId(); } catch (Exception idEx) { StingLog.WarnRateLimited("SysPipeTypeId", $"Piping system type id unreadable on {el.Id}: {idEx.Message}"); }
                    string val = pipeSys.AsValueString()?.ToUpperInvariant() ?? "";
                    string mapped = MapSystemNameToCode(val, categoryName);
                    if (!string.IsNullOrEmpty(mapped)) return RefineHydronic(mapped, val, el.Document, pipeSysTypeId);
                }

                // The system's Revit CLASSIFICATION ("Domestic Cold Water", "Hydronic Return",
                // "Fire Protection Wet", "Sanitary" …), for a system type whose NAME says nothing
                // ("PS-01", "Type 3"). "Other" classifies nothing and falls through.
                Parameter cls = el.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                if (cls != null && cls.HasValue)
                {
                    string clsText = cls.AsString() ?? cls.AsValueString();
                    string mapped = MapSystemNameToCode(clsText, categoryName);
                    if (!string.IsNullOrEmpty(mapped)) return RefineHydronic(mapped, clsText, el.Document, pipeSysTypeId);
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
                    if (panel.Contains("DHW") || panel.Contains("HOT WATER")) return "DHW";
                    if (panel.Contains("HWS") || panel.Contains("LTHW")) return "HWS";
                    // "MAINS WATER", not bare "MAINS": an electrical "MAINS DB" / "MAINS
                    // SWITCHBOARD" fed every fixture on it SYS=DCW.
                    if (panel.Contains("DCW") || panel.Contains("COLD WATER") || panel.Contains("MAINS WATER")) return "DCW";
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
            // Keyword set shared with IsLightningProtection and the LPS PROD codes.
            if (LpsNameClassifier.IsLps(upper))
                return "LPS";

            // Medical gas: an element that declares its gas, or a family whose name names
            // one ("Oxygen Outlet", "Medical Air Plant"). A name that only says "Medical"
            // is not enough — Medical Equipment families say that.
            if ((Plumbing.MedicalGasFixtures.CanonicalGasCode(ParameterHelpers.GetString(el, "MGS_GAS_TYPE_TXT")) != null
                 || (SystemNameClassifier.TryMedicalGas(familyName, out string famGas) && famGas != null))
                && SysMap != null && SysMap.TryGetValue(SystemNameClassifier.MedicalGasSys, out var mgsCats)
                && mgsCats.Contains(categoryName ?? ""))
                return SystemNameClassifier.MedicalGasSys;

            // Named systems (cooling plant, plumbing plant, HV, BMS, radiation protection),
            // before the LV and HVAC patterns below. Only a system the category can belong
            // to is taken: "Pool Table" (Furniture) or "MRI-safe Chair" is not a system.
            string special = SystemNameClassifier.FromFamilyName(
                familyName + " " + ParameterHelpers.GetFamilySymbolName(el));
            if (special != null && SysMap != null && SysMap.TryGetValue(special, out var specialCats)
                && specialCats.Contains(categoryName ?? ""))
                return special;

            // HVAC equipment patterns
            if (upper.Contains("AHU") || upper.Contains("AIR HANDLING") ||
                upper.Contains("FCU") || upper.Contains("FAN COIL") ||
                upper.Contains("VAV") || upper.Contains("VARIABLE AIR") ||
                upper.Contains("EXHAUST FAN") || upper.Contains("EXTRACT FAN") ||
                upper.Contains("HRU") || upper.Contains("HEAT RECOVERY") ||
                upper.Contains("SPLIT") || upper.Contains("CASSETTE") ||
                upper.Contains("GRILLE") || upper.Contains("DIFFUSER"))
                return "HVAC";

            // Domestic hot water generation
            if (upper.Contains("CALORIFIER") || upper.Contains("WATER HEATER") ||
                upper.Contains("DHW CYLINDER"))
                return "DHW";

            // Heating equipment
            if (upper.Contains("BOILER") || upper.Contains("RADIATOR") ||
                upper.Contains("UNDERFLOOR HEAT") || upper.Contains("HEAT EXCHANGER"))
                return "HWS";

            // Plumbing-specific equipment. A sump or sewage pump moves foul water (SAN);
            // it used to be tagged as cold-water supply.
            if (upper.Contains("PUMP") && (upper.Contains("SUMP") || upper.Contains("SEWAGE")))
                return "SAN";
            if (upper.Contains("PUMP") && (categoryName == "Plumbing Fixtures" || upper.Contains("BOOSTER")))
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
                string catUpper = ParameterHelpers.GetCategoryName(el).ToUpperInvariant();

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

        private static HashSet<string> _pipeCategories => CategoryTokenDefaults.PipeCategories;

        /// <summary>
        /// System-aware DISC correction. Pipe-type categories are "M" by default; a
        /// domestic service (DCW, DHW, SAN, RWD, GAS) makes them "P", FP makes them "FP",
        /// and HVAC / HWS (heating water) keep them "M". Rules in
        /// <see cref="CategoryTokenDefaults.SystemAwareDisc"/>.
        /// </summary>

        public static string GetSystemAwareDisc(string disc, string sys, string categoryName)
            => CategoryTokenDefaults.SystemAwareDisc(disc, sys, categoryName);

        /// <summary>
        /// Map a system name string to a SYS code. Used by Layer 1 (connector) and Layer 2 (parameter).
        /// Centralised mapping for all MEP system naming conventions.
        /// </summary>
        private static string MapSystemNameToCode(string sysName, string categoryName = null)
            => SystemNameClassifier.FromSystemName(sysName, categoryName);

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
