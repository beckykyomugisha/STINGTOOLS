using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core.Placement;
using System.Collections.Concurrent;

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
        // CRASH FIX: volatile ensures double-checked locking works correctly —
        // without it, a thread can see _loaded=true while dictionaries are
        // still being written by the loading thread (CPU cache coherency issue)
        private static volatile bool _loaded;
        private static readonly object _lock = new object();

        // ── Tag format ──────────────────────────────────────────────────
        // Base values loaded from PARAMETER_REGISTRY.json; project overrides applied on top.
        private static string _baseSeparator = "-";
        private static int _baseNumPad = 4;
        private static string[] _baseSegmentOrder = { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };
        private static string _overrideSeparator;
        private static int? _overrideNumPad;
        private static string[] _overrideSegmentOrder;
        // PERF-05: lazy cache, invalidated on override change. Reader was
        // refactored away in a later sweep but the invalidation hooks
        // remain as guard infrastructure for the next time the cache is
        // re-introduced. Silence CS0414 since the field is intentionally
        // write-only for now.
#pragma warning disable CS0169 // never used — kept for future write-side caching
        private static string[] _cachedSegmentOrder;
#pragma warning restore CS0169

        public static string Separator => _overrideSeparator ?? _baseSeparator;
        public static int NumPad => _overrideNumPad ?? _baseNumPad;
        /// <summary>CR-03 FIX: Returns defensive clone every time to prevent callers from mutating shared state.</summary>
        public static string[] SegmentOrder
        {
            get
            {
                return (string[])(_overrideSegmentOrder ?? _baseSegmentOrder).Clone();
            }
        }

        private static readonly HashSet<string> ValidSegmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };

        /// <summary>
        /// Apply project-level tag format overrides (from project_config.json).
        /// Validates segment order contains only known segment names.
        /// </summary>
        public static void ApplyTagFormatOverrides(string separator, int? numPad, string[] segmentOrder)
        {
            // FL-03: Track old separator in history before overriding
            if (!string.IsNullOrEmpty(separator) && separator != Separator
                && !string.IsNullOrEmpty(Separator))
            {
                if (!TagConfig.SeparatorHistory.Contains(Separator))
                    TagConfig.SeparatorHistory.Add(Separator);
            }
            _overrideSeparator = separator;
            _overrideNumPad = numPad;
            if (segmentOrder != null)
            {
                foreach (var seg in segmentOrder)
                {
                    if (!ValidSegmentNames.Contains(seg))
                    {
                        StingLog.Warn($"Invalid segment name '{seg}' in tag format override — ignoring segment order override");
                        _overrideSegmentOrder = null;
                        _cachedSegmentOrder = null; // PERF-05: Invalidate on rejection too
                        StingLog.Info($"Tag format override applied: sep='{Separator}', pad={NumPad}, segments={SegmentOrder.Length} (segment order rejected)");
                        return;
                    }
                }
                _overrideSegmentOrder = (string[])segmentOrder.Clone();
            }
            _cachedSegmentOrder = null; // PERF-05: invalidate cached order
            StingLog.Info($"Tag format override applied: sep='{Separator}', pad={NumPad}, segments={SegmentOrder.Length}");
        }

        /// <summary>
        /// Clear project-level tag format overrides (revert to PARAMETER_REGISTRY.json values).
        /// </summary>
        public static void ClearTagFormatOverrides()
        {
            _overrideSeparator = null;
            _overrideNumPad = null;
            _overrideSegmentOrder = null;
            _cachedSegmentOrder = null; // PERF-05: invalidate cached order
        }

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

        // ── Required/Optional parameter flags ────────────────────────────
        /// <summary>
        /// DATA-02: Parameter names flagged as required in PARAMETER_REGISTRY.json.
        /// Defaults to the 8 source tokens + TAG1 if not specified in JSON.
        /// </summary>
        public static HashSet<string> RequiredParams { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT",
            "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            "ASS_TAG_1_TXT"
        };

        // ── Stale detection + display mode + tag position ───────────────
        public const string STALE = "STING_STALE_BOOL";
        public const string STALE_GUID = "C9D0E1F2-A3B4-4C5D-8E6F-7A8B9C0D1E2F";
        public const string CLUSTER_COUNT = "STING_CLUSTER_COUNT";
        public const string CLUSTER_COUNT_GUID = "D1E2F3A4-B5C6-4D7E-8F9A-0B1C2D3E4F5A";
        public const string CLUSTER_LABEL = "STING_CLUSTER_LABEL";
        public const string CLUSTER_LABEL_GUID = "D2E3F4A5-B6C7-4D8E-9F0A-1B2C3D4E5F6B";
        /// <summary>FIX-B04: JSON array of cluster member bounding box centers for decluster restore.</summary>
        public const string CLUSTER_MEMBER_POS = "STING_CLUSTER_MEMBER_POS_TXT";
        public const string DISPLAY_MODE = "STING_DISPLAY_MODE";
        public const string DISPLAY_MODE_GUID = "D0E1F2A3-B4C5-4D6E-8F7A-8B9C0D1E2F3A";
        /// <summary>
        /// Default display mode when STING_DISPLAY_MODE is 0 (unset).
        /// 1=SEQ, 2=PROD-SEQ, 3=DISC-SYS-SEQ, 4=DISC-PROD-SEQ, 5=Full 8-segment.
        /// </summary>
        public static int DisplayModeDefault = 2;
        public const string DISPLAY_TXT = "ASS_DISPLAY_TXT";
        public const string DISPLAY_TXT_GUID = "D3E4F5A6-B7C8-4D9E-0F1A-2B3C4D5E6F7C";
        public const string TAG_POS = "STING_TAG_POS";
        public const string TAG_POS_GUID = "E1F2A3B4-C5D6-4E7F-8A9B-0C1D2E3F4A5B";
        public const string VIEW_TAG_STYLE = "STING_VIEW_TAG_STYLE";
        public const string VIEW_TAG_STYLE_GUID = "E2F3A4B5-C6D7-4E8F-9A0B-1C2D3E4F5A6C";
        public const string TAG_SEG_MASK = "TAG_SEG_MASK_TXT";
        public const string TAG_SEG_MASK_GUID = "F3A4B5C6-D7E8-4F9A-0B1C-2D3E4F5A6B7D";
        // ── Tag audit trail ─────────────────────────────────────────────
        /// <summary>Previous ASS_TAG_1 value before the last tag write — used for change detection and reverse diff.</summary>
        public const string TAG_PREV = "ASS_TAG_PREV_TXT";
        public const string TAG_PREV_GUID = "c1f4d6b8-2a3e-4d5b-9c6f-7a8b9c0d1e2e";
        /// <summary>ISO-8601 datetime of the last tag modification written by any STING command.</summary>
        public const string TAG_MODIFIED_DT = "ASS_TAG_MODIFIED_DT";
        public const string TAG_MODIFIED_DT_GUID = "c1f4d6b8-2a3e-4d5b-9c6f-7a8b9c0d1e2d";
        /// <summary>Revit Environment.UserName that performed the last tag modification.</summary>
        public const string TAG_MODIFIED_BY = "ASS_TAG_MODIFIED_BY_TXT";
        public const string TAG_MODIFIED_BY_GUID = "c1f4d6b8-2a3e-4d5b-9c6f-7a8b9c0d1e2f";
        // ── Project tag scheme (Phase 191) ──────────────────────────────
        /// <summary>Default target container for project-grammar tag renderings
        /// (TagSchemeEngine) — e.g. the ISO 19650 PROJECT-ORIGINATOR-VOLUME-
        /// LEVEL-DISCIPLINE-NUMBER form. Always derived from the source
        /// tokens; never edited directly. UUIDv5 in the Planscape docs
        /// namespace a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a.</summary>
        public const string TAG_SCHEME = "ASS_TAG_SCHEME_TXT";
        public const string TAG_SCHEME_GUID = "2c8224df-92e0-567b-a9df-c8cd1e4402a3";
        /// <summary>Phase 192 (B1) — milestone id stamped by LOD_Stamp on elements
        /// that PASS LodVerificationEngine at that milestone's LOD (e.g.
        /// "deliverable-c"). UUIDv5 in the Planscape docs namespace
        /// a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a.</summary>
        public const string LOD_VERIFIED = "ASS_LOD_VERIFIED_TXT";
        public const string LOD_VERIFIED_GUID = "60440963-a414-5667-88f4-d12082344c4d";
        /// <summary>Phase 192 (C2) — CSI MasterFormat section (e.g. "23 31 00")
        /// resolved by CSI_Assign from STING_CSI_MASTERFORMAT_MAP.csv. Reconciled
        /// against the RIB SpecLink spec TOC. UUIDv5 in namespace
        /// a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a.</summary>
        public const string CSI_SECTION = "CSI_SECTION_TXT";
        public const string CSI_SECTION_GUID = "3c2c7d9d-93e2-5f95-a002-69b17450efe6";
        public const string CSI_TITLE = "CSI_TITLE_TXT";
        public const string CSI_TITLE_GUID = "160a2335-1886-5503-b569-28d9e63f5a75";
        // Per-view 8-char "1"/"0" mask gating which segments render in
        // BuildDisplayTag without mutating the canonical ASS_TAG_1_TXT.
        // Bound to OST_Views so users can hide ZONE in a presentation view
        // without breaking exports — review fix for TAG-token-toggling #1.
        public const string VIEW_TOKEN_MASK = "STING_VIEW_TOKEN_MASK_TXT";
        public const string VIEW_TOKEN_MASK_GUID = "F4A5B6C7-D8E9-4F0A-1B2C-3D4E5F6A7B8E";

        // ── Cost management — currency-neutral parameters (Phase 184 / P0.2) ─
        //
        // Replace the UGX-/USD-locked legacy params with currency-neutral
        // storage. Legacy params (ASS_CST_UNIT_PRICE_UGX_NR etc.) remain
        // bound for backwards-compat; the migration command
        // `Cost_MigrateCurrencyParams` copies UGX values to the neutral
        // params with CurrencyCode="UGX" + FX-at-date populated from the
        // project's current FX rate. GUIDs UUIDv5 in the cost namespace
        // b9d4e1a2-7c63-4f89-9e0a-1f5a2c8b3d40.
        public const string CST_UNIT_RATE_NR     = "ASS_CST_UNIT_RATE_NR";
        public const string CST_UNIT_RATE_NR_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D41";
        public const string CST_CURRENCY_TXT     = "ASS_CST_CURRENCY_TXT";
        public const string CST_CURRENCY_TXT_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D42";
        public const string CST_FX_TO_BASE_NR    = "ASS_CST_FX_TO_BASE_NR";
        public const string CST_FX_TO_BASE_NR_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D43";
        public const string CST_FX_DATE_DT       = "ASS_CST_FX_DATE_DT";
        public const string CST_FX_DATE_DT_GUID  = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D44";
        public const string CST_AS_OF_DT         = "ASS_CST_AS_OF_DT";
        public const string CST_AS_OF_DT_GUID    = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D45";

        // P2 — stale-cost detection (mirrors STING_STALE_BOOL for cost).
        // Set by StingCostStaleMarker IUpdater when geometry / material /
        // type changes invalidate the last-costed line item. Cleared by
        // `Cost_ClearStale` after a successful BOQ_Build.
        public const string CST_STALE_BOOL       = "ASS_CST_STALE_BOOL";
        public const string CST_STALE_BOOL_GUID  = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D46";
        public const string CST_STALE_REASON_TXT = "ASS_CST_STALE_REASON_TXT";
        public const string CST_STALE_REASON_TXT_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D47";

        // Phase 184g / P5.1 — payment certificate params
        public const string PMT_PCT_COMPLETE_NR  = "ASS_PMT_PCT_COMPLETE_NR";
        public const string PMT_PCT_COMPLETE_NR_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D50";
        public const string PMT_CERT_NO_NR       = "ASS_PMT_CERT_NO_NR";
        public const string PMT_CERT_NO_NR_GUID  = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D51";
        public const string PMT_CERT_DATE_DT     = "ASS_PMT_CERT_DATE_DT";
        public const string PMT_CERT_DATE_DT_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D52";
        public const string PMT_LAST_VALUED_DT   = "ASS_PMT_LAST_VALUED_DT";
        public const string PMT_LAST_VALUED_DT_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D53";

        // Phase 184g / P5.2 — variation tracking
        public const string VAR_NO_TXT           = "ASS_VAR_NO_TXT";
        public const string VAR_NO_TXT_GUID      = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D60";
        public const string VAR_INSTRUCTION_DT   = "ASS_VAR_INSTRUCTION_DT";
        public const string VAR_INSTRUCTION_DT_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D61";
        public const string VAR_VALUATION_NR     = "ASS_VAR_VALUATION_NR";
        public const string VAR_VALUATION_NR_GUID = "B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D62";

        // ── Phase 182–183 — HVAC sizing-registry audit-trail params ──────
        // Set by MepAutoSizeCommand / HvacSegmentRoleDetector /
        // HvacDetectStaleSizesCommand / HvacCarbonReportCommand. GUIDs
        // match MR_PARAMETERS.txt + MR_PARAMETERS.csv + PARAMETER_REGISTRY.json.
        public const string HVC_SEGMENT_ROLE_TXT      = "HVC_SEGMENT_ROLE_TXT";
        public const string HVC_SEGMENT_ROLE_TXT_GUID = "5BF0485F-08DD-53D1-9CC5-D956305D42E0";
        public const string HVC_SIZE_PREV_TXT         = "HVC_SIZE_PREV_TXT";
        public const string HVC_SIZE_PREV_TXT_GUID    = "B4385937-438E-5D5F-8CE0-F13C2A94A63D";
        public const string HVC_SIZE_MODIFIED_DT      = "HVC_SIZE_MODIFIED_DT";
        public const string HVC_SIZE_MODIFIED_DT_GUID = "B485412F-0A10-5CF7-9A49-DD2AE6199442";
        public const string HVC_SIZE_RULE_ID_TXT      = "HVC_SIZE_RULE_ID_TXT";
        public const string HVC_SIZE_RULE_ID_TXT_GUID = "B02AE4EA-C9A0-5424-9E20-7D4406352260";
        public const string HVC_PIPE_SERVICE_TXT      = "HVC_PIPE_SERVICE_TXT";
        public const string HVC_PIPE_SERVICE_TXT_GUID = "97E69122-4E43-5B88-9C82-6EAF586DDC07";
        public const string HVC_PRESSURE_CLASS_TXT      = "HVC_PRESSURE_CLASS_TXT";
        public const string HVC_PRESSURE_CLASS_TXT_GUID = "61D432D6-77FE-5811-972F-0B28493D3DE7";
        public const string HVC_SIZE_STALE_BOOL       = "HVC_SIZE_STALE_BOOL";
        public const string HVC_SIZE_STALE_BOOL_GUID  = "ECBC8E8A-3466-53DD-92C9-A28D15EBF43D";
        public const string HVC_REFRIGERANT_KG_NR     = "HVC_REFRIGERANT_KG_NR";
        public const string HVC_REFRIGERANT_KG_NR_GUID = "B99D07D1-6ECA-50CF-B983-B6FE2442BC8C";
        public const string HVC_REFRIGERANT_TYPE_TXT     = "HVC_REFRIGERANT_TYPE_TXT";
        public const string HVC_REFRIGERANT_TYPE_TXT_GUID = "10D87A6E-B7D8-5058-81A9-BC62394D9BAD";
        public const string HVC_CAPACITY_KW           = "HVC_CAPACITY_KW";
        public const string HVC_CAPACITY_KW_GUID      = "397EE526-7AF0-5516-A2A1-48DB5A42F249";
        public const string PRJ_ORG_PRESSURE_PROFILE_TXT      = "PRJ_ORG_PRESSURE_PROFILE_TXT";
        public const string PRJ_ORG_PRESSURE_PROFILE_TXT_GUID = "8B3BFDCF-AAB3-5944-A451-E4766BFAF8CE";

        // ── Phase 175 — Symbol system parameters ─────────────────────────
        public const string SYMBOL_ID                 = "STING_SYMBOL_ID";
        public const string SYMBOL_ID_GUID            = "A4B5C6D7-E8F9-4A0B-1C2D-3E4F5A6B7C8D";
        public const string SYMBOL_STANDARD           = "STING_SYMBOL_STANDARD";
        public const string SYMBOL_STANDARD_GUID      = "B5C6D7E8-F9A0-4B1C-2D3E-4F5A6B7C8D9E";
        public const string SYMBOL_HOST_ELEMENT_ID    = "STING_HOST_ELEMENT_ID";
        public const string SYMBOL_HOST_ELEMENT_ID_GUID = "C6D7E8F9-A0B1-4C2D-3E4F-5A6B7C8D9E0F";
        public const string SYMBOL_LABEL_ID           = "STING_SYMBOL_LABEL_ID";
        public const string SYMBOL_LABEL_ID_GUID      = "D7E8F9A0-B1C2-4D3E-4F5A-6B7C8D9E0F1A";
        public const string SYMBOL_OVERRIDE           = "STING_SYMBOL_OVERRIDE";
        public const string SYMBOL_OVERRIDE_GUID      = "E8F9A0B1-C2D3-4E4F-5A6B-7C8D9E0F1A2B";
        public const string VIEW_SYMBOL_STANDARD      = "STING_VIEW_SYMBOL_STANDARD";
        public const string VIEW_SYMBOL_STANDARD_GUID = "F9A0B1C2-D3E4-4F5A-6B7C-8D9E0F1A2B3C";
        public const string SLD_ELEMENT_ID            = "STING_SLD_ELEMENT_ID";
        public const string SLD_ELEMENT_ID_GUID       = "0A1B2C3D-4E5F-4A6B-7C8D-9E0F1A2B3C4D";
        public const string SYMBOL_LIBRARY_VERSION    = "STING_SYMBOL_LIBRARY_VERSION";
        public const string SYMBOL_LIBRARY_VERSION_GUID = "1B2C3D4E-5F6A-4B7C-8D9E-0F1A2B3C4D5E";
        public const string SYMBOL_COMPOUND_PARENT_ID = "STING_COMPOUND_PARENT_ID";
        public const string SYMBOL_COMPOUND_PARENT_ID_GUID = "2C3D4E5F-6A7B-4C8D-9E0F-1A2B3C4D5E6F";

        // Family-embedded standard switching — model family (.rfa) parameters.
        // STING_SYMBOL_STD is an Integer type param; each value gates one
        // standard's embedded symbolic curve set via derived Yes/No formulas.
        //   0 = IEC (IEC 60617 / EN 60617)
        //   1 = ANSI (ANSI/IEEE 315)
        //   2 = BS (BS 1553 / BS 8888)
        //   3 = NFPA (NFPA 170)
        //   4 = CIBSE (CIBSE Guide symbols)
        public const string SYMBOL_STD_PARAM    = "STING_SYMBOL_STD";
        public const string SHOW_IEC_BOOL       = "STING_SHOW_IEC_BOOL";
        public const string SHOW_ANSI_BOOL      = "STING_SHOW_ANSI_BOOL";
        public const string SHOW_BS_BOOL        = "STING_SHOW_BS_BOOL";
        public const string SHOW_NFPA_BOOL      = "STING_SHOW_NFPA_BOOL";
        public const string SHOW_CIBSE_BOOL     = "STING_SHOW_CIBSE_BOOL";
        public const int    STD_CODE_IEC        = 0;
        public const int    STD_CODE_ANSI       = 1;
        public const int    STD_CODE_BS         = 2;
        public const int    STD_CODE_NFPA       = 3;
        public const int    STD_CODE_CIBSE      = 4;

        // Phase 175 — Circuit-annotation parameters consumed by
        // Core/Symbols/SymbolAnnotationEngine.BuildLabel. STING-prefixed
        // names take precedence; the legacy bare names (CIRCUIT_REF /
        // RATING / POLES / LABEL) remain supported as fallback so imported
        // third-party families render labels without re-binding.
        public const string CIRCUIT_REF               = "ELC_CIRCUIT_REF_TXT";
        public const string CIRCUIT_REF_GUID          = "C2A7E5B1-3001-5333-9333-300000000001";
        public const string CIRCUIT_RATING            = "ELC_CIRCUIT_RATING_TXT";
        public const string CIRCUIT_RATING_GUID       = "C2A7E5B1-3002-5333-9333-300000000002";
        public const string CIRCUIT_POLES             = "ELC_CIRCUIT_POLES_NR";
        public const string CIRCUIT_POLES_GUID        = "C2A7E5B1-3003-5333-9333-300000000003";
        public const string CIRCUIT_LABEL             = "ELC_CIRCUIT_LABEL_TXT";
        public const string CIRCUIT_LABEL_GUID        = "C2A7E5B1-3004-5333-9333-300000000004";

        // ── Phase 137 — Drawing production stamps ────────────────────────
        // Written onto views/sheets by the production engine so audits and
        // browser organisers can find STING-produced artefacts.
        public const string STING_VIEW_CONTEXT_TAG     = "STING_VIEW_CONTEXT_TAG_TXT";
        public const string STING_DRAWING_PACKAGE_ID   = "STING_DRAWING_PACKAGE_ID_TXT";
        public const string STING_AUTO_PLACED_BOOL     = "STING_AUTO_PLACED_BOOL";
        public const string STING_PRODUCTION_RULE_IDX  = "STING_PRODUCTION_RULE_IDX_INT";
        public const string STING_SHEET_SEQUENCE       = "STING_SHEET_SEQUENCE_INT";

        // ── Annotation marker constants (Phase 179) ──────────────────────
        public const string STING_WIRE_ANNOT_MARKER   = "STING_WIRE_ANNOT";
        public const string STING_HOMERUN_MARKER      = "STING_WIRE_HOMERUN";
        public const string STING_TICK_MARKER         = "STING_WIRE_TICK";

        // ── Phase 168 — Match-line subsystem ─────────────────────────────
        // Stamped onto every auto-placed match-line DetailCurve + caption
        // tag by MatchLineEngine.PlacePair. STING_MATCH_REF_TXT carries
        // the paired sheet's STING_SHEET_FULL_REF (so cross-references
        // re-resolve when sheets are renumbered); STING_MATCH_LINE_GUID
        // is the stable pair identifier that lets re-runs find existing
        // pairs and update them in place; STING_MATCH_DIR encodes
        // vertical/horizontal/dogleg so the drift detector knows what
        // shape to expect when validating against the scope-box graph.
        public const string MATCH_REF       = "STING_MATCH_REF_TXT";
        public const string MATCH_REF_GUID  = "A6B7C8D9-EAFB-4ACC-5D6E-7F8A9BACDBEC";
        public const string MATCH_LINE_GUID = "STING_MATCH_LINE_GUID_TXT";
        public const string MATCH_LINE_GUID_GUID = "A7B8C9DA-FBAC-4BCD-6E7F-8A9BACDBECFD";
        public const string MATCH_DIR       = "STING_MATCH_DIR_TXT";
        public const string MATCH_DIR_GUID  = "A8B9CADB-ACBD-4CDE-7F8A-9BACDBECFDAE";


        // LOG-01: Detection source tracking parameters
        public const string LOC_SOURCE = "ASS_LOC_SOURCE_TXT";
        public const string LOC_SOURCE_GUID = "A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D";
        public const string ZONE_SOURCE = "ASS_ZONE_SOURCE_TXT";
        public const string ZONE_SOURCE_GUID = "A2B3C4D5-E6F7-4A8B-9C0D-1E2F3A4B5C6E";
        public const string SYS_DETECT_LAYER = "ASS_SYS_DETECT_LAYER_INT";
        public const string SYS_DETECT_LAYER_GUID = "A3B4C5D6-E7F8-4A9B-0C1D-2E3F4A5B6C7F";

        // ORF-02: COBie Serial Number
        public const string SERIAL_NR = "ASS_SERIAL_NR_TXT";
        public const string SERIAL_NR_GUID = "B1C2D3E4-F5A6-4B7C-8D9E-0F1A2B3C4D5E";

        // ORF-03: COBie Installation Date and Warranty
        public const string INSTALL_DATE = "ASS_INSTALLATION_DATE_TXT";
        public const string INSTALL_DATE_GUID = "B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6F";
        public const string WARRANTY = "ASS_WARRANTY_TXT";
        public const string WARRANTY_GUID = "B3C4D5E6-F7A8-4B9C-0D1E-2F3A4B5C6D7A";

        // ORF-04: Notes
        public const string NOTES = "ASS_NOTES_TXT";
        public const string NOTES_GUID = "B4C5D6E7-F8A9-4BAC-1D2E-3F4A5B6C7D8B";

        // ORF-05: Flow Rate and Power Rating
        public const string FLOW_RATE = "ASS_FLOW_RATE_TXT";
        public const string FLOW_RATE_GUID = "B5C6D7E8-F9AA-4BBC-2D3E-4F5A6B7C8D9C";
        public const string POWER_RATING = "ASS_POWER_RATING_TXT";
        public const string POWER_RATING_GUID = "B6C7D8E9-FAAB-4BCC-3D4E-5F6A7B8C9DAD";

        // ORF-06: Room Height
        public const string ROOM_HEIGHT = "ASS_ROOM_HEIGHT_MM";
        public const string ROOM_HEIGHT_GUID = "B7C8D9EA-FBAC-4BDC-4D5E-6F7A8B9CADBE";

        // Phase 19: PROD detection source tracking
        public const string PROD_DETECT = "ASS_PROD_DETECT_TXT";
        public const string PROD_DETECT_GUID = "C1D2E3F4-A5B6-4C7D-8E9F-0A1B2C3D4E5F";
        public const string PROD_PATTERN_SRC = "ASS_PROD_PATTERN_SRC_TXT";
        public const string PROD_PATTERN_SRC_GUID = "C2D3E4F5-A6B7-4C8D-9E0F-1A2B3C4D5E6A";

        // Phase 19: Type-level LOC/ZONE overrides
        public const string TYPE_LOC_OVERRIDE = "ASS_TYPE_LOC_OVERRIDE_TXT";
        public const string TYPE_LOC_OVERRIDE_GUID = "C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7B";
        public const string TYPE_ZONE_OVERRIDE = "ASS_TYPE_ZONE_OVERRIDE_TXT";
        public const string TYPE_ZONE_OVERRIDE_GUID = "C4D5E6F7-A8B9-4CAD-1E2F-3A4B5C6D7E8C";

        // Phase 19: Level ID tracking
        public const string LVL_ELEM_ID = "ASS_LVL_ELEM_ID_INT";
        public const string LVL_ELEM_ID_GUID = "C5D6E7F8-A9BA-4CBD-2E3F-4A5B6C7D8E9D";

        // Phase 19: Grid reference tracking
        public const string GRID_X_ID = "ASS_GRID_X_ID_INT";
        public const string GRID_X_ID_GUID = "C6D7E8F9-AABB-4CCD-3E4F-5A6B7C8D9EAE";
        public const string GRID_Y_ID = "ASS_GRID_Y_ID_INT";
        public const string GRID_Y_ID_GUID = "C7D8E9FA-ABBC-4CDE-4E5F-6A7B8C9DAEBF";
        public const string GRID_DIST = "ASS_GRID_DIST_NR";
        public const string GRID_DIST_GUID = "C8D9EAFB-ACBD-4CEF-5E6F-7A8B9CADBECF";

        // Phase 19: MEP System Name
        public const string MEP_SYS_NAME = "ASS_MEP_SYS_NAME_TXT";
        public const string MEP_SYS_NAME_GUID = "C9DAEBFC-ADBE-4CFA-6E7F-8A9BACBDCED0";

        // Phase 19: Host Type
        public const string HOST_TYPE = "ASS_HOST_TYPE_TXT";
        public const string HOST_TYPE_GUID = "CADBECFD-AECF-4D0B-7E8F-9AABBBCCDDEE";

        // Phase 39: Sheet-Level Tagging Containers
        public const string SHT_NUMBER = "SHT_NUMBER_TXT";
        public const string SHT_NAME = "SHT_NAME_TXT";
        public const string SHT_DISC = "SHT_DISC_TXT";
        public const string SHT_ORIGINATOR = "SHT_ORIGINATOR_TXT";
        public const string SHT_FORM = "SHT_FORM_TXT";
        public const string SHT_LEVEL = "SHT_LEVEL_TXT";
        public const string SHT_REV = "SHT_REV_TXT";
        public const string SHT_TAG_1 = "SHT_TAG_1_TXT";
        public const string SHT_TAG_7 = "SHT_TAG_7_TXT";

        // Phase 79: MEP Sleeve Container
        public const string SLV_TAG = "SLV_TAG";

        // ── Phase 97: Title Block parameters (per STING TB spec v1.0, 2026-04-19) ──
        // GUIDs are UUIDv5 with fixed namespace; values mirror MR_PARAMETERS.txt entries.
        // All bound to ViewSheet category; runtime dispatch via TitleBlockCommands.cs.
        public const string TB_VARIANT             = "PRJ_TB_VARIANT_TXT";
        public const string TB_VARIANT_GUID        = "e4c060c3-1c31-5860-b0d0-ef9472016895";
        public const string TB_SCHEMA_VERSION      = "PRJ_TB_SCHEMA_VERSION_TXT";
        public const string TB_SCHEMA_VERSION_GUID = "9832b76e-07df-509e-b139-d402bc50ba68";
        public const string TB_LOGO_PATH           = "PRJ_TB_LOGO_PATH_TXT";
        public const string TB_LOGO_PATH_GUID      = "3bb5edc1-54f9-56ff-92c9-405a8dde646c";
        public const string TB_LAST_SYNC           = "PRJ_TB_LAST_SYNC_TXT";
        public const string TB_LAST_SYNC_GUID      = "1817eeb3-4c56-50c2-b386-6b3be3d98fc4";
        public const string TB_LAST_SYNC_BY        = "PRJ_TB_LAST_SYNC_BY_TXT";
        public const string TB_LAST_SYNC_BY_GUID   = "eb514ec7-6636-5987-9667-8e85c31a8f85";
        public const string TB_LOCK                = "PRJ_TB_LOCK_BOOL";
        public const string TB_LOCK_GUID           = "74c9d75f-840c-5263-9acf-8fecf80ec6aa";
        // Canonical home for these toggles is TB_SHOW_*_BOOL on the GROUP 26 TBL_TITLEBLOCK
        // FamilyInstance (added in Drawing Template Manager). The PRJ_TB_SHOW_*_BOOL
        // constants below are kept on ViewSheet for backwards compat with sheets that
        // were authored before STING TB v1; new title block families should bind to the
        // GROUP 26 TB_ versions.
        public const string TB_SHOW_KEYPLAN        = "TB_SHOW_KEY_PLAN_BOOL";
        public const string TB_SHOW_KEYPLAN_GUID   = "9a64e982-1b97-5922-9831-0948aaf1cf76";
        public const string TB_SHOW_SCALEBAR       = "TB_SHOW_SCALEBAR_BOOL";
        public const string TB_SHOW_SCALEBAR_GUID  = "afcd0647-42e0-537f-bd18-5f46ed1871df";
        public const string TB_SHOW_NORTHARROW     = "TB_SHOW_NORTH_ARROW_BOOL";
        public const string TB_SHOW_NORTHARROW_GUID= "0981c0a9-7805-568a-8fee-abb012f6239c";
        public const string TB_SHOW_DISCBAND       = "PRJ_TB_SHOW_DISCBAND_BOOL";
        public const string TB_SHOW_DISCBAND_GUID  = "483f47d7-a6cd-5fa7-bfde-ff2ab6e43178";
        public const string TB_SCALE_OVERRIDE      = "PRJ_TB_SCALE_OVERRIDE_TXT";
        public const string TB_SCALE_OVERRIDE_GUID = "624563ac-3067-5990-ba13-a4d750e9ffc2";
        public const string TB_ISSUE_SUMMARY       = "PRJ_TB_ISSUE_SUMMARY_TXT";
        public const string TB_ISSUE_SUMMARY_GUID  = "a3408dee-9ced-5ccd-970c-0958bcc713a9";
        public const string TB_DELIVERABLE_DATADROP      = "PRJ_TB_DELIVERABLE_DATADROP_TXT";
        public const string TB_DELIVERABLE_DATADROP_GUID = "d63919e8-7cf5-5202-bd59-1dc03554fee4";
        public const string TB_DELIVERABLE_STATUS        = "PRJ_TB_DELIVERABLE_STATUS_TXT";
        public const string TB_DELIVERABLE_STATUS_GUID   = "5fea853a-6ed7-505e-a677-50fb83f435b0";
        public const string TB_DELIVERABLE_DUE           = "PRJ_TB_DELIVERABLE_DUE_TXT";
        public const string TB_DELIVERABLE_DUE_GUID      = "525f8b24-26eb-52ae-8760-c6aa1621815a";
        public const string TB_DELIVERABLE_CDE           = "PRJ_TB_DELIVERABLE_CDE_TXT";
        public const string TB_DELIVERABLE_CDE_GUID      = "0d917e49-c6f6-5951-b2b7-7a00bdb3b0df";
        public const string TB_LAST_TRANSMITTAL          = "PRJ_TB_LAST_TRANSMITTAL_TXT";
        public const string TB_LAST_TRANSMITTAL_GUID     = "953d56bb-e854-5817-9fa0-90ed013f276c";
        public const string TB_LAST_TRANSMITTAL_DATE     = "PRJ_TB_LAST_TRANSMITTAL_DATE_TXT";
        public const string TB_LAST_TRANSMITTAL_DATE_GUID= "8edb7300-d8a4-5df3-b0ba-b21710da9724";
        public const string TB_NOTES_LEGEND_REF          = "PRJ_TB_NOTES_LEGEND_REF_TXT";
        public const string TB_NOTES_LEGEND_REF_GUID     = "a083c0ca-5782-59a2-a459-85107690aa6d";

        /// <summary>All 19 PRJ_TB_* parameters added in STING Title Block System v1.0.</summary>
        public static readonly string[] AllTitleBlockParams = new[]
        {
            TB_VARIANT, TB_SCHEMA_VERSION, TB_LOGO_PATH, TB_LAST_SYNC, TB_LAST_SYNC_BY,
            TB_LOCK, TB_SHOW_KEYPLAN, TB_SHOW_SCALEBAR, TB_SHOW_NORTHARROW, TB_SHOW_DISCBAND,
            TB_SCALE_OVERRIDE, TB_ISSUE_SUMMARY,
            TB_DELIVERABLE_DATADROP, TB_DELIVERABLE_STATUS, TB_DELIVERABLE_DUE, TB_DELIVERABLE_CDE,
            TB_LAST_TRANSMITTAL, TB_LAST_TRANSMITTAL_DATE, TB_NOTES_LEGEND_REF
        };

        /// <summary>Subset of TB params that are YESNO flags (for TitleBlockPopulate type coercion).</summary>
        public static readonly HashSet<string> TitleBlockBoolParams = new HashSet<string>(StringComparer.Ordinal)
        {
            TB_LOCK, TB_SHOW_KEYPLAN, TB_SHOW_SCALEBAR, TB_SHOW_NORTHARROW, TB_SHOW_DISCBAND
        };

        // ── Organisation parameters (v1.1 template engine + workflow) ──
        // Thirteen PRJ_ORG_* shared parameters scoped to ProjectInformation.
        // GUIDs are deterministic UUIDv5 in the Planscape docs namespace
        // UUID('a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a'). Originator code defaults to
        // "PLNS"; company name to "Planscape Limited". These feed TemplateManifest,
        // DocumentIdentityGenerator, TokenContext.FromDeliverable, and WorkflowEngine.
        public const string ORG_PROJECT_CODE            = "PRJ_ORG_PROJECT_CODE_TXT";
        public const string ORG_PROJECT_CODE_GUID       = "d72513d3-2aed-5048-a949-b262fcd51a39";

        // ── Template Manager v2 — drift + lockdown + library + profile ──
        // Five parameters added in the Template Manager v2 rebuild. Auto-loaded
        // from MR_PARAMETERS.txt by the FIX-12.4 supplement path so projects
        // pick them up without a code change. Constants here are the canonical
        // names that DriftDetector, CorporateLibrary, TemplateRulesRegistry,
        // and the dashboard's Lock toggle look up via LookupParameter.
        public const string TM_TEMPLATE_CHECKSUM        = "STING_TEMPLATE_CHECKSUM_TXT";
        public const string TM_TEMPLATE_CHECKSUM_GUID   = "a1f2b3c4-d5e6-4f70-8123-456789abcd01";
        public const string TM_TEMPLATE_LOCKED          = "STING_TEMPLATE_LOCKED_BOOL";
        public const string TM_TEMPLATE_LOCKED_GUID     = "a1f2b3c4-d5e6-4f70-8123-456789abcd02";
        public const string TM_CORP_LIB_PATH            = "PRJ_CORPORATE_LIBRARY_PATH_TXT";
        public const string TM_CORP_LIB_PATH_GUID       = "a1f2b3c4-d5e6-4f70-8123-456789abcd03";
        public const string TM_CORP_LIB_VERSION         = "PRJ_CORPORATE_LIBRARY_VERSION_TXT";
        public const string TM_CORP_LIB_VERSION_GUID    = "a1f2b3c4-d5e6-4f70-8123-456789abcd04";
        public const string TM_PROFILE                  = "PRJ_TEMPLATE_PROFILE_TXT";
        public const string TM_PROFILE_GUID             = "a1f2b3c4-d5e6-4f70-8123-456789abcd05";
        public const string ORG_ORIGINATOR_CODE         = "PRJ_ORG_ORIGINATOR_CODE_TXT";
        public const string ORG_ORIGINATOR_CODE_GUID    = "d9b568c8-0dcf-5226-add0-a6e3643589e8";
        public const string ORG_COMPANY_NAME            = "PRJ_ORG_COMPANY_NAME_TXT";
        public const string ORG_COMPANY_NAME_GUID       = "f08b9a37-5e44-5074-a9e1-0a0f6418a305";
        public const string ORG_COMPANY_ADDRESS         = "PRJ_ORG_COMPANY_ADDRESS_TXT";
        public const string ORG_COMPANY_ADDRESS_GUID    = "834df80b-0472-5724-afab-1c90ce7eac80";
        public const string ORG_CLIENT_NAME             = "PRJ_ORG_CLIENT_NAME_TXT";
        public const string ORG_CLIENT_NAME_GUID        = "32487484-61c4-5043-aec1-0851720902a6";
        public const string ORG_APPOINTING_PARTY        = "PRJ_ORG_APPOINTING_PARTY_TXT";
        public const string ORG_APPOINTING_PARTY_GUID   = "b9df91ba-d8ee-561c-9786-d0ce3c74c55e";
        public const string ORG_LEAD_APPOINTED_PARTY    = "PRJ_ORG_LEAD_APPOINTED_PARTY_TXT";
        public const string ORG_LEAD_APPOINTED_PARTY_GUID = "77069632-0604-5cb1-b6ef-5e2211f6b3f4";
        public const string ORG_PARTICIPANTS            = "PRJ_ORG_PARTICIPANTS_TXT";
        public const string ORG_PARTICIPANTS_GUID       = "a4c8ef52-5bb2-579f-9308-8a6c2177bf52";
        public const string ORG_PHASE                   = "PRJ_ORG_PHASE_TXT";
        public const string ORG_PHASE_GUID              = "d187fdbd-f701-5334-90da-1ab6694c5034";
        public const string ORG_CLASS                   = "PRJ_ORG_CLASS_TXT";
        public const string ORG_CLASS_GUID              = "cef45220-b201-5c44-baed-275a0fd556a7";
        public const string ORG_WORKFLOW_PROFILE        = "PRJ_ORG_WORKFLOW_PROFILE_TXT";
        public const string ORG_WORKFLOW_PROFILE_GUID   = "48a26ee9-211d-5525-8fbb-9f8eb1f38878";
        public const string ORG_SIGNATURE_PROVIDER      = "PRJ_ORG_SIGNATURE_PROVIDER_TXT";
        public const string ORG_SIGNATURE_PROVIDER_GUID = "e669eea3-d1fa-51b7-b820-83fa21d40877";
        public const string ORG_AI_EXTRACT_ENABLED      = "PRJ_ORG_AI_EXTRACT_ENABLED_BOOL";
        public const string ORG_AI_EXTRACT_ENABLED_GUID = "a7c93ee1-9df2-5531-b873-1df826526e82";

        /// <summary>All 13 PRJ_ORG_* parameters added in template engine v1.1 (S01).</summary>
        public static readonly string[] AllOrganisationParams = new[]
        {
            ORG_PROJECT_CODE, ORG_ORIGINATOR_CODE, ORG_COMPANY_NAME, ORG_COMPANY_ADDRESS,
            ORG_CLIENT_NAME, ORG_APPOINTING_PARTY, ORG_LEAD_APPOINTED_PARTY, ORG_PARTICIPANTS,
            ORG_PHASE, ORG_CLASS, ORG_WORKFLOW_PROFILE, ORG_SIGNATURE_PROVIDER,
            ORG_AI_EXTRACT_ENABLED
        };

        // I-4 — Material Manager cost-split + EPD params (registered so the
        // parameter audit / drift detection picks them up).
        public const string MAT_COST_SUPPLY  = "MAT_COST_SUPPLY_NR";
        public const string MAT_COST_INSTALL = "MAT_COST_INSTALL_NR";
        public const string MAT_VAT_PCT      = "MAT_VAT_PCT_NR";
        public const string MAT_EMB_CARBON   = "STING_EMB_CARBON_NR";
        public const string MAT_EPD_SRC      = "STING_MAT_EPD_SRC_TXT";
        public const string MAT_EPD_DATE     = "STING_MAT_EPD_DATE_TXT";

        /// <summary>All Material-scoped STING parameters surfaced by the
        /// Material Manager. Drift detection + ParameterHelpers refresh
        /// passes consult this list.</summary>
        public static readonly string[] AllMaterialParams = new[]
        {
            MAT_COST_SUPPLY, MAT_COST_INSTALL, MAT_VAT_PCT,
            MAT_EMB_CARBON, MAT_EPD_SRC, MAT_EPD_DATE,
        };

        /// <summary>Default values for PRJ_ORG_* parameters (used by TemplateManifest.CreateDefault).</summary>
        public static readonly Dictionary<string, string> OrganisationDefaults = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { ORG_ORIGINATOR_CODE,      "PLNS" },
            { ORG_COMPANY_NAME,         "Planscape Limited" },
            { ORG_COMPANY_ADDRESS,      "Kampala, Uganda" },
            { ORG_LEAD_APPOINTED_PARTY, "Planscape Limited" },
            { ORG_PHASE,                "DE" },
            { ORG_CLASS,                "2" },
            { ORG_WORKFLOW_PROFILE,     "default" },
            { ORG_SIGNATURE_PROVIDER,   "" },
            { ORG_AI_EXTRACT_ENABLED,   "0" }
        };

        // ── Extended parameter names (identity, spatial, dimensional, MEP) ──
        // Loaded from extended_params section. Keys map to param_name values.
        private static Dictionary<string, string> _extendedParams = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>DATA-02: Set of parameter names marked as required in PARAMETER_REGISTRY.json.</summary>
        private static readonly HashSet<string> _requiredParams = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>DATA-02: Check if a parameter is marked as required in the registry.</summary>
        public static bool IsRequired(string paramName)
        {
            return !string.IsNullOrEmpty(paramName) && _requiredParams.Contains(paramName);
        }

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

        // ── Phase 178 — Advanced calculations & automation ───────────────
        // 4 of these reuse existing TEXT params (short-circuit, voltage drop,
        // cable size, conduit fill); the remaining 7 are net-new in
        // MR_PARAMETERS.txt (AIC tier, feeder CSA + rating, emerg coverage,
        // LPD value + limit + status). All TEXT for cross-binding flexibility.
        public static string ELC_PNL_FAULT_KA      => Ext("ELC_PNL_FAULT_KA");      // → ELC_PNL_SHORT_CIRCUIT_RATING_KA (existing)
        public static string ELC_PNL_AIC_KA        => Ext("ELC_PNL_AIC_KA");        // → ELC_PNL_AIC_RATING_KA (new)
        public static string ELC_FEEDER_CSA        => Ext("ELC_FEEDER_CSA");        // → ELC_FEEDER_CSA_MM2 (new)
        public static string ELC_FEEDER_RATING_A   => Ext("ELC_FEEDER_RATING_A");   // → ELC_FEEDER_RATING_A (new)
        public static string ELC_CKT_VD_PCT        => Ext("ELC_CKT_VD_PCT");        // → ELC_VLT_DROP_PCT (existing)
        public static string ELC_CKT_CSA_MM2       => Ext("ELC_CKT_CSA_MM2");       // → ELC_CBL_SZ_MM (existing)
        public static string ELC_CONDUIT_FILL_PCT  => Ext("ELC_CONDUIT_FILL_PCT");  // → ELC_CDT_CBL_FILL_PCT (existing)
        public static string ELC_CDT_BEND_ANGLE_DEG => Ext("ELC_CDT_BEND_ANGLE_DEG"); // BS 7671 §522.8 bend angle on conduit fittings
        public static string ELC_CDT_BEND_COUNT_NR  => Ext("ELC_CDT_BEND_COUNT_NR");  // BS 7671 §522.8.5 — max 3 between draw-in points
        public static string ELC_CDT_RUN_LENGTH_M   => Ext("ELC_CDT_RUN_LENGTH_M");   // BS 7671 — typical max 6m between draw-in points
        public static string ELC_CDT_CABLE_COUNT_NR => Ext("ELC_CDT_CABLE_COUNT_NR"); // BS EN 61386 — fill table varies by cable count (1/2/3+)
        public static string ELC_EMERG_COVERED     => Ext("ELC_EMERG_COVERED");     // → ELC_EMERG_COVERED_BOOL (new)
        public static string ELC_LPD_W_M2          => Ext("ELC_LPD_W_M2");          // → ELC_LPD_W_PER_M2 (new)
        public static string ELC_LPD_LIMIT_W_M2    => Ext("ELC_LPD_LIMIT_W_M2");    // → ELC_LPD_LIMIT_W_PER_M2 (new)
        public static string ELC_LPD_STATUS        => Ext("ELC_LPD_STATUS");        // → ELC_LPD_STATUS_TXT (new)

        // ── Phase 179 — Advanced analysis & external integration ─────────
        public static string ELC_ARC_FLASH_IE     => Ext("ELC_ARC_FLASH_IE");     // → ELC_ARC_FLASH_IE_CAL_CM2
        public static string ELC_ARC_FLASH_BD     => Ext("ELC_ARC_FLASH_BD");     // → ELC_ARC_FLASH_BOUNDARY_MM
        public static string ELC_ARC_FLASH_PPE    => Ext("ELC_ARC_FLASH_PPE");    // → ELC_ARC_FLASH_PPE_CAT
        public static string ELC_ARC_FLASH_WD     => Ext("ELC_ARC_FLASH_WD");     // → ELC_ARC_FLASH_WORK_DIST_MM
        public static string ELC_ARC_FLASH_LABEL  => Ext("ELC_ARC_FLASH_LABEL");  // → ELC_ARC_FLASH_LABEL_TXT
        public static string ELC_SEL_COORD_OK     => Ext("ELC_SEL_COORD_OK");     // → ELC_SEL_COORD_VERIFIED_BOOL
        public static string ELC_BUSBAR_CSA       => Ext("ELC_BUSBAR_CSA");       // → ELC_BUSBAR_CSA_MM2
        public static string ELC_BUSBAR_RATING    => Ext("ELC_BUSBAR_RATING");    // → ELC_BUSBAR_RATING_A
        public static string ELC_BUSBAR_FILL      => Ext("ELC_BUSBAR_FILL");      // → ELC_BUSBAR_FILL_PCT
        public static string ELC_CONDUIT_ROUTE    => Ext("ELC_CONDUIT_ROUTE");    // → ELC_CONDUIT_ROUTE_TXT
        public static string ELC_PHOTO_LUX        => Ext("ELC_PHOTO_LUX");        // → ELC_PHOTO_LUX_CALC
        public static string ELC_PHOTO_UGR        => Ext("ELC_PHOTO_UGR");        // → ELC_PHOTO_UGR_CALC

        // ── Phase 180 — photometric library / luminaire metadata ──────────
        public static string ELC_PHOTO_FILE_PATH  => Ext("ELC_PHOTO_FILE_PATH");  // → ELC_PHOTO_FILE_PATH_TXT
        public static string ELC_PHOTO_LUMENS     => Ext("ELC_PHOTO_LUMENS");     // → ELC_PHOTO_LUMENS_NR
        public static string ELC_PHOTO_WATTS      => Ext("ELC_PHOTO_WATTS");      // → ELC_PHOTO_WATTS_NR
        public static string ELC_PHOTO_EFFICACY   => Ext("ELC_PHOTO_EFFICACY");   // → ELC_PHOTO_EFFICACY_LM_W
        public static string ELC_PHOTO_BEAM_ANGLE => Ext("ELC_PHOTO_BEAM_ANGLE"); // → ELC_PHOTO_BEAM_ANGLE_DEG
        public static string ELC_PHOTO_CCT        => Ext("ELC_PHOTO_CCT");        // → ELC_PHOTO_CCT_K
        public static string ELC_PHOTO_CRI        => Ext("ELC_PHOTO_CRI");        // → ELC_PHOTO_CRI_NR
        public static string ELC_PHOTO_SYMMETRY   => Ext("ELC_PHOTO_SYMMETRY");   // → ELC_PHOTO_SYMMETRY_TXT

        // ── Phase 181 — multi-engine photometric results ──────────────────
        public static string ELC_PHOTO_LUX_DIALUX     => Ext("ELC_PHOTO_LUX_DIALUX");     // → ELC_PHOTO_LUX_DIALUX_NR
        public static string ELC_PHOTO_LUX_ELUMTOOLS  => Ext("ELC_PHOTO_LUX_ELUMTOOLS");  // → ELC_PHOTO_LUX_ELUMTOOLS_NR
        public static string ELC_PHOTO_LUX_RELUX      => Ext("ELC_PHOTO_LUX_RELUX");      // → ELC_PHOTO_LUX_RELUX_NR
        public static string ELC_PHOTO_UNIFORMITY     => Ext("ELC_PHOTO_UNIFORMITY");     // → ELC_PHOTO_UNIFORMITY_NR
        public static string ELC_PHOTO_LAST_ENGINE    => Ext("ELC_PHOTO_LAST_ENGINE");    // → ELC_PHOTO_LAST_ENGINE_TXT
        public static string ELC_PHOTO_LAST_CALC_DATE => Ext("ELC_PHOTO_LAST_CALC_DATE"); // → ELC_PHOTO_LAST_CALC_DATE_TXT

        // ── Wire annotation / conduit-fill new params (Phase 179) ──
        public const string ELC_WIRE_BEND_COUNT_INT        = "ELC_WIRE_BEND_COUNT_INT";
        public const string ELC_CDT_STALE_ANNOT_BOOL       = "ELC_CDT_STALE_ANNOT_BOOL";
        public const string ELC_RECONCILE_DRIFT_BOOL       = "ELC_RECONCILE_DRIFT_BOOL";
        public const string STING_CONDUIT_FILL_SUMMARY_TXT = "STING_CONDUIT_FILL_SUMMARY_TXT";

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

        // ── Phase 178b plumbing engine constants ────────────────────────
        public static string PLM_DFU_COUNT     => Ext("PLM_DFU_COUNT");
        public static string PLM_WSFU_COUNT    => Ext("PLM_WSFU_COUNT");
        public static string PLM_TRAP_TYPE     => Ext("PLM_TRAP_TYPE");
        public static string PLM_TRAP_SEAL     => Ext("PLM_TRAP_SEAL");
        public static string PLM_VENT_DN       => Ext("PLM_VENT_DN");
        public static string PLM_AAV_REQ       => Ext("PLM_AAV_REQ");
        public static string PLM_CALC_DN       => Ext("PLM_CALC_DN");
        public static string PLM_CALC_SLOPE    => Ext("PLM_CALC_SLOPE");
        public static string PLM_DEMAND_FLOW   => Ext("PLM_DEMAND_FLOW");
        public static string PLM_VEL           => Ext("PLM_VEL");
        public static string PLM_FRICTION      => Ext("PLM_FRICTION");
        public static string PLM_PTEST         => Ext("PLM_PTEST");
        public static string PLM_FLUID_CAT     => Ext("PLM_FLUID_CAT");
        public static string PLM_BF_TYPE       => Ext("PLM_BF_TYPE");
        public static string PLM_PPE_STD       => Ext("PLM_PPE_STD");
        public static string PLM_PPE_SCH       => Ext("PLM_PPE_SCH");
        public static string PLM_PPE_GRADE     => Ext("PLM_PPE_GRADE");
        public static string PLM_PPE_WRAS      => Ext("PLM_PPE_WRAS");
        public static string PLM_PPE_COLOR     => Ext("PLM_PPE_COLOR");
        public static string PLM_PPE_WALL_THK  => Ext("PLM_PPE_WALL_THK");
        public static string PLM_MAT           => Ext("PLM_MAT");
        public static string PLM_RWH_AREA      => Ext("PLM_RWH_AREA");
        public static string PLM_RWH_TANK      => Ext("PLM_RWH_TANK");
        public static string PLM_RWH_YIELD     => Ext("PLM_RWH_YIELD");
        public static string PLM_SUDS_VOL      => Ext("PLM_SUDS_VOL");
        public static string PLM_SEPTIC_VOL    => Ext("PLM_SEPTIC_VOL");
        public static string PLM_PRV_SET       => Ext("PLM_PRV_SET");
        public static string PLM_PRV_INLET     => Ext("PLM_PRV_INLET");
        public static string PLM_PRESSURE_ZONE => Ext("PLM_PRESSURE_ZONE");
        public static string PLM_TMV_CLASS     => Ext("PLM_TMV_CLASS");
        public static string PLM_TMV_BLEND     => Ext("PLM_TMV_BLEND");
        public static string PLM_VLV_SET_P     => Ext("PLM_VLV_SET_P");
        public static string PLM_VLV_FLOW      => Ext("PLM_VLV_FLOW");
        public static string PLM_VLV_DP        => Ext("PLM_VLV_DP");
        public static string PLM_VLV_FAIL      => Ext("PLM_VLV_FAIL");
        public static string PLM_VLV_WRAS      => Ext("PLM_VLV_WRAS");
        public static string PLM_DEAD_LEG_M    => Ext("PLM_DEAD_LEG_M");
        public static string PLM_AUG_CARE      => Ext("PLM_AUG_CARE");
        public static string PLM_RO_LOOP       => Ext("PLM_RO_LOOP");
        public static string PLM_POU_FILTER    => Ext("PLM_POU_FILTER");
        public static string PLM_SENTINEL      => Ext("PLM_SENTINEL");
        public static string PRJ_PLUMBING_CODE => Ext("PRJ_PLUMBING_CODE");

        // ── Phase 179a — Plumbing enhancement (drainage / supply / system) ──
        public static string PLM_DRN_DU            => Ext("PLM_DRN_DU");
        public static string PLM_DRN_DN_REQ        => Ext("PLM_DRN_DN_REQ");
        public static string PLM_DRN_QWW           => Ext("PLM_DRN_QWW");
        public static string PLM_DRN_HD_RATIO      => Ext("PLM_DRN_HD_RATIO");
        public static string PLM_DRN_INV_US        => Ext("PLM_DRN_INV_US");
        public static string PLM_DRN_INV_DS        => Ext("PLM_DRN_INV_DS");
        public static string PLM_DRN_COVER_US      => Ext("PLM_DRN_COVER_US");
        public static string PLM_DRN_COVER_DS      => Ext("PLM_DRN_COVER_DS");
        public static string PLM_HAS_TRAP          => Ext("PLM_HAS_TRAP");
        public static string PLM_TRAP_ARM          => Ext("PLM_TRAP_ARM");
        public static string PLM_VENT_TYPE         => Ext("PLM_VENT_TYPE");
        public static string PLM_SUP_LU_CW         => Ext("PLM_SUP_LU_CW");
        public static string PLM_SUP_LU_HW         => Ext("PLM_SUP_LU_HW");
        public static string PLM_SUP_WSFU          => Ext("PLM_SUP_WSFU");
        public static string PLM_SUP_QD            => Ext("PLM_SUP_QD");
        public static string PLM_SUP_DN_REQ        => Ext("PLM_SUP_DN_REQ");
        public static string PLM_SUP_PRES          => Ext("PLM_SUP_PRES");
        public static string PLM_SUP_VEL           => Ext("PLM_SUP_VEL");
        public static string PLM_SUP_DP            => Ext("PLM_SUP_DP");
        public static string PLM_DRV_PRESET        => Ext("PLM_DRV_PRESET");
        public static string PLM_EXPVSL_SZ         => Ext("PLM_EXPVSL_SZ");
        public static string PLM_PRV_SET_BAR       => Ext("PLM_PRV_SET_BAR");
        public static string PLM_MAT_DCW           => Ext("PLM_MAT_DCW");
        public static string PLM_MAT_DHW           => Ext("PLM_MAT_DHW");
        public static string PLM_MAT_DRN           => Ext("PLM_MAT_DRN");
        public static string PLM_MAT_VNT           => Ext("PLM_MAT_VNT");
        public static string PLM_BLDG_TYPE         => Ext("PLM_BLDG_TYPE");
        public static string PLM_K_FACTOR          => Ext("PLM_K_FACTOR");
        public static string PLM_STD_DRAIN         => Ext("PLM_STD_DRAIN");
        public static string PLM_STD_SUPPLY        => Ext("PLM_STD_SUPPLY");
        public static string PLM_AUDIT_DATE        => Ext("PLM_AUDIT_DATE");

        // ── Phase 179d — Plumbing network, pump, TMV, spool, real-time sizer ──
        public static string PLM_PUMP_DUTY_HEAD_M   => Ext("PLM_PUMP_DUTY_HEAD_M");
        public static string PLM_PUMP_DUTY_FLOW_LPS => Ext("PLM_PUMP_DUTY_FLOW_LPS");
        public static string PLM_PUMP_MODEL         => Ext("PLM_PUMP_MODEL");
        public static string PLM_PUMP_EFF_PCT       => Ext("PLM_PUMP_EFF_PCT");
        public static string PLM_TMV_INLET_HOT_C    => Ext("PLM_TMV_INLET_HOT_C");
        public static string PLM_TMV_INLET_COLD_C   => Ext("PLM_TMV_INLET_COLD_C");
        // PLM_TMV_BLEND_TEMP_C is the design set-point; PLM_TMV_MEASURED_C is the
        // commissioning-measurement reading. Keeping them separate is required
        // by BS 8680:2022 §5 tolerance validation (otherwise outlet ≡ setpoint
        // and the ±1/±2°C check is meaningless).
        public static string PLM_TMV_MEASURED_C     => Ext("PLM_TMV_MEASURED_C");
        public static string PLM_TMV_TEST_DATE      => Ext("PLM_TMV_TEST_DATE");
        public static string PLM_TMV_NEXT_TEST      => Ext("PLM_TMV_NEXT_TEST");
        public static string PLM_TMV_OVERDUE        => Ext("PLM_TMV_OVERDUE");
        public static string PLM_VENT_PIPE_ID       => Ext("PLM_VENT_PIPE_ID");
        public static string PLM_PIPE_REAL_SIZE     => Ext("PLM_PIPE_REAL_SIZE");
        public static string PLM_PRESSURE_KPA       => Ext("PLM_PRESSURE_KPA");
        public static string PLM_SPOOL_NR           => Ext("PLM_SPOOL_NR");
        public static string PLM_NETWORK_NODE_TYPE  => Ext("PLM_NETWORK_NODE_TYPE");

        // ── COBie / Warranty / Asset fields ──
        public static string WARR_GUAR_PARTS  => Ext("WARR_GUAR_PARTS");
        public static string WARR_DUR_PARTS   => Ext("WARR_DUR_PARTS");
        public static string WARR_GUAR_LABOR  => Ext("WARR_GUAR_LABOR");
        public static string WARR_DUR_LABOR   => Ext("WARR_DUR_LABOR");
        public static string WARR_DUR_UNIT    => Ext("WARR_DUR_UNIT");
        public static string REPLACE_COST     => Ext("REPLACE_COST");
        public static string DUR_UNIT         => Ext("DUR_UNIT");
        public static string NOM_LENGTH       => Ext("NOM_LENGTH");
        public static string NOM_WIDTH        => Ext("NOM_WIDTH");
        public static string NOM_HEIGHT       => Ext("NOM_HEIGHT");
        public static string MODEL_REF        => Ext("MODEL_REF");
        public static string SHAPE            => Ext("SHAPE");
        public static string COLOR            => Ext("COLOR");
        public static string FINISH           => Ext("FINISH");
        public static string GRADE            => Ext("GRADE");
        public static string MATERIAL         => Ext("MATERIAL");
        public static string CONSTITUENTS     => Ext("CONSTITUENTS");
        public static string FEATURES         => Ext("FEATURES");
        public static string ACCESS_PERF      => Ext("ACCESS_PERF");
        public static string CODE_PERF        => Ext("CODE_PERF");
        public static string SUSTAIN_PERF     => Ext("SUSTAIN_PERF");
        public static string WARRANTY_START   => Ext("WARRANTY_START");
        public static string BARCODE          => Ext("BARCODE");
        public static string ASSET_ID         => Ext("ASSET_ID");
        public static string CONDITION        => Ext("CONDITION");
        public static string SUPPLIER         => Ext("SUPPLIER");

        // ── Tag style fields ──
        public static string STYLE_SIZE       => Ext("STYLE_SIZE");
        public static string STYLE_WEIGHT     => Ext("STYLE_WEIGHT");

        // ── Paragraph visibility controls (v4.2, expanded to 10 states) ──
        /// <summary>Compact paragraph depth (State 1 only).</summary>
        public static string PARA_STATE_1 { get; private set; } = "TAG_PARA_STATE_1_BOOL";
        /// <summary>Standard paragraph depth (States 1+2).</summary>
        public static string PARA_STATE_2 { get; private set; } = "TAG_PARA_STATE_2_BOOL";
        /// <summary>Comprehensive paragraph depth (States 1+2+3).</summary>
        public static string PARA_STATE_3 { get; private set; } = "TAG_PARA_STATE_3_BOOL";
        /// <summary>State visibility control tier 4.</summary>
        public static string PARA_STATE_4 { get; private set; } = "TAG_PARA_STATE_4_BOOL";
        /// <summary>State visibility control tier 5.</summary>
        public static string PARA_STATE_5 { get; private set; } = "TAG_PARA_STATE_5_BOOL";
        /// <summary>State visibility control tier 6.</summary>
        public static string PARA_STATE_6 { get; private set; } = "TAG_PARA_STATE_6_BOOL";
        /// <summary>State visibility control tier 7.</summary>
        public static string PARA_STATE_7 { get; private set; } = "TAG_PARA_STATE_7_BOOL";
        /// <summary>State visibility control tier 8.</summary>
        public static string PARA_STATE_8 { get; private set; } = "TAG_PARA_STATE_8_BOOL";
        /// <summary>State visibility control tier 9.</summary>
        public static string PARA_STATE_9 { get; private set; } = "TAG_PARA_STATE_9_BOOL";
        /// <summary>State visibility control tier 10.</summary>
        public static string PARA_STATE_10 { get; private set; } = "TAG_PARA_STATE_10_BOOL";
        /// <summary>Enable/disable warning text in tags.</summary>
        public static string WARN_VISIBLE { get; private set; } = "TAG_WARN_VISIBLE_BOOL";
        /// <summary>Warning severity filter: CRITICAL, HIGH, MEDIUM, ALL.</summary>
        public static string WARN_SEVERITY_FILTER { get; private set; } = "TAG_WARN_SEVERITY_FILTER_TXT";

        // ── Paragraph pattern selectors (2026-04, dual-wire T4-T10) ──
        // Families carry BOTH the Handover and Design & Construction row sets.
        // Each row's formula AND-gates TAG_PARA_STATE_N_BOOL with one of these so
        // flipping the pattern is a project-level BOOL toggle instead of a
        // family re-author pass.
        /// <summary>Pattern selector — Handover / FM T4-T10 payload is visible.</summary>
        public static string MODE_HANDOVER { get; private set; } = "HANDOVER_MODE_HANDOVER_BOOL";
        /// <summary>Shared-parameter GUID for HANDOVER_MODE_HANDOVER_BOOL.</summary>
        public const string MODE_HANDOVER_GUID = "A1E2F3B4-C5D6-4E7F-8A9B-0C1D2E3F4A5B";
        /// <summary>Pattern selector — Design & Construction T4-T10 payload is visible.</summary>
        public static string MODE_DC { get; private set; } = "HANDOVER_MODE_DC_BOOL";
        /// <summary>Shared-parameter GUID for HANDOVER_MODE_DC_BOOL.</summary>
        public const string MODE_DC_GUID = "B2F3A4C5-D6E7-4F8A-9B0C-1D2E3F4A5B6C";
        /// <summary>Pattern selector — Custom (user-defined) T4-T10 payload is visible.</summary>
        public static string MODE_CUSTOM { get; private set; } = "HANDOVER_MODE_CUSTOM_BOOL";
        /// <summary>Shared-parameter GUID for HANDOVER_MODE_CUSTOM_BOOL.</summary>
        public const string MODE_CUSTOM_GUID = "C3A4B5D6-E7F8-4A9B-0C1D-2E3F4A5B6C7D";

        // Phase 165 — stable GUIDs for the three pattern-mode BOOLs so they can
        // be registered as Revit shared parameters (Issue #16 in the tagging
        // workflow audit). Without GUIDs, AddBindings can't bind these to
        // ProjectInformation, and they cannot be written to elements as type
        // params. Constants are named with the same _GUID suffix convention as
        // the rest of the file.

        // ── Warning threshold definitions (v5.5) ─────────────────────────
        // Loaded from warning_thresholds section of PARAMETER_REGISTRY.json.
        // Each entry defines a compliance check with threshold, unit, and severity.

        /// <summary>Warning threshold definition loaded from PARAMETER_REGISTRY.json.</summary>
        public class WarningThresholdDef
        {
            public string ParamName { get; set; }
            public string Guid { get; set; }
            public string Description { get; set; }
            public string Threshold { get; set; }
            public string Unit { get; set; }
            public string Severity { get; set; } // CRITICAL, HIGH, MEDIUM, LOW
        }

        /// <summary>All warning threshold definitions keyed by param name.</summary>
        public static Dictionary<string, WarningThresholdDef> WarningThresholds { get; private set; }
            = new Dictionary<string, WarningThresholdDef>(StringComparer.Ordinal);

        /// <summary>Get warning thresholds applicable to a category (looked up from LABEL_DEFINITIONS).</summary>
        private static Dictionary<string, List<string>> _categoryWarnings
            = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register which warning params apply to a given category.
        /// Called during LABEL_DEFINITIONS loading in TagConfig or presentation commands.
        /// </summary>
        public static void RegisterCategoryWarnings(string categoryName, List<string> warningParamNames)
        {
            if (!string.IsNullOrEmpty(categoryName) && warningParamNames != null)
                _categoryWarnings[categoryName] = warningParamNames;
        }

        /// <summary>Get warning param names for a category (empty list if none registered).</summary>
        public static List<string> GetCategoryWarnings(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return new List<string>();
            return _categoryWarnings.TryGetValue(categoryName, out var list) ? list : new List<string>();
        }

        /// <summary>
        /// Evaluate a warning threshold against an element's current value.
        /// Returns a warning message if threshold is exceeded, or null if compliant.
        /// </summary>
        public static string EvaluateWarning(WarningThresholdDef def, string currentValue)
        {
            if (def == null || string.IsNullOrEmpty(currentValue) || string.IsNullOrEmpty(def.Threshold))
                return null;
            // Try numeric comparison
            if (double.TryParse(currentValue, out double val) && double.TryParse(def.Threshold, out double thresh))
            {
                // For most thresholds: value exceeding limit is a warning
                // For minimums (coverage, width, depth): value below threshold is a warning
                bool isMinimum = def.Description.Contains("minimum") || def.Description.Contains("min ");
                bool isLimit = def.Description.Contains("limit") || def.Description.Contains("maximum") || def.Description.Contains("max ");

                if (isMinimum && val < thresh)
                    return $"[!{def.Severity}: {def.Description} — {currentValue} {def.Unit} < {def.Threshold} {def.Unit}]";
                else if (isLimit && val > thresh)
                    return $"[!{def.Severity}: {def.Description} — {currentValue} {def.Unit} > {def.Threshold} {def.Unit}]";
                else if (!isMinimum && !isLimit && val > thresh)
                    return $"[!{def.Severity}: {def.Description} — {currentValue} {def.Unit} exceeds {def.Threshold} {def.Unit}]";
            }
            return null;
        }

        // ── Paragraph container mapping (v5.5) ──────────────────────────
        // Maps category names to their paragraph container parameter names.
        // Loaded from LABEL_DEFINITIONS.json category_labels.

        private static Dictionary<string, string> _paragraphContainers
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Register a paragraph container param for a category.</summary>
        public static void RegisterParagraphContainer(string categoryName, string paramName)
        {
            if (!string.IsNullOrEmpty(categoryName) && !string.IsNullOrEmpty(paramName))
            {
                _paragraphContainers[categoryName] = paramName;
                // Phase 165 perf — invalidate the AllParagraphContainers cache
                // so the next reader rebuilds it including this entry.
                _allParagraphContainersCache = null;
            }
        }

        /// <summary>Get the paragraph container param name for a category (null if none).</summary>
        public static string GetParagraphContainer(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return null;
            return _paragraphContainers.TryGetValue(categoryName, out string p) ? p : null;
        }

        /// <summary>
        /// Phase 165 — Issue #22. Distinct paragraph-container parameter names
        /// across every category, used by WriteTag7All to clear stale entries
        /// before writing the new narrative.
        ///
        /// Phase 165 perf — materialised once into a string[] on first call,
        /// invalidated by RegisterParagraphContainer (which sets the field
        /// back to null). Replaces a LINQ chain that allocated an iterator
        /// + a HashSet for Distinct on every WriteTag7All call (~1000 elements
        /// × ~47 containers in a typical model = wasted millions of cycles).
        /// </summary>
        private static string[] _allParagraphContainersCache;

        // Phase 165 perf — per-thread reusable buffers for AssembleContainer
        // and WriteContainers. ThreadStatic so each Revit thread gets its
        // own. Reset on every call to be safe even though Revit pins
        // commands to the API thread.
        [ThreadStatic] private static List<string> _assembleScratch;
        [ThreadStatic] private static HashSet<string> _writtenParamsScratch;
        public static string[] AllParagraphContainers
        {
            get
            {
                var cache = _allParagraphContainersCache;
                if (cache != null) return cache;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var list = new List<string>(_paragraphContainers.Count);
                foreach (var v in _paragraphContainers.Values)
                {
                    if (string.IsNullOrEmpty(v)) continue;
                    if (seen.Add(v)) list.Add(v);
                }
                cache = list.ToArray();
                _allParagraphContainersCache = cache;
                return cache;
            }
        }

        /// <summary>All 10 paragraph state parameter names indexed by tier (1-based: index 0 = state 1).</summary>
        // PERF-010 FIX: Cache array to avoid per-access allocation in hot loops (WriteTag7All)
        private static string[] _allParaStates;
        public static string[] AllParaStates => _allParaStates ??= new[]
        {
            PARA_STATE_1, PARA_STATE_2, PARA_STATE_3, PARA_STATE_4, PARA_STATE_5,
            PARA_STATE_6, PARA_STATE_7, PARA_STATE_8, PARA_STATE_9, PARA_STATE_10
        };

        // ── Tag style visibility parameters (v5.0) ────────────────────────
        // Controls which tag label row is visible via the {SIZE}{STYLE}_{COLOR}_BOOL pattern.
        // In tag families, each label row has its Visible property bound to one of these.
        // Setting e.g. TAG_2BOLD_RED_BOOL=true makes the "2pt bold red" label row visible.
        // Sizes: 2, 2.5, 3, 3.5  |  Styles: NOM, BOLD, ITALIC  |  Colors: BLACK, BLUE, GREEN, RED
        //
        // The tag family has one Type per combination (e.g. type "2BOLD_RED") with its
        // corresponding BOOL set to Yes; switching type switches visible label row.
        //
        // These are TEXT type names, resolved dynamically. Use TagStyleParamName() to build.
        //
        // TAG TEXT STYLE MATRIX — 128 universal YESNO parameters
        // Pattern: TAG_{size}{style}_{color}_BOOL where
        //   size  = 2 / 2.5 / 3 / 3.5 (mm text height)
        //   style = NOM / BOLD / ITALIC / BOLDITALIC
        //   color = BLACK / BLUE / GREEN / RED / GREY / ORANGE / PURPLE / WHITE
        //
        // Exactly one of these 128 is set to true per element at any given time,
        // making the corresponding label row visible in the tag family. The mutual
        // exclusion is enforced by TagStyleEngine.ApplyStyle(), not by the data model.
        //
        // ARCHITECTURE NOTE: Adding a new size or color requires adding 32 or 16
        // new shared parameters respectively. The long-term replacement is a single
        // TAG_STYLE_CODE_TXT param ("2BOLD_BLUE") with calculated BOOL formulas
        // inside the tag family — see ROADMAP.md TAG-01. Do not expand this matrix
        // further without updating ROADMAP.md.
        /// <summary>Tag text colour (Integer code for calculated value rendering).</summary>
        public static string TAG_TEXT_COLOUR { get; private set; } = "TAG_TEXT_COLOUR_TEXT";
        /// <summary>VG Projection/Surface visibility control.</summary>
        public static string VGPS_VISIBLE { get; private set; } = "VGPS_VISIBLE_BOOL";
        /// <summary>TAG-01: Single style code replaces 128 BOOL params. Value is the type-name
        /// string, e.g. "2BOLD_BLUE". Backwards-compatible: BOOL params still written alongside.</summary>
        public const string TAG_STYLE_CODE = "TAG_STYLE_CODE_TXT";
        public const string TAG_STYLE_CODE_GUID = "d4e5f6a7-b8c9-4d0e-af12-345678901bcd";

        /// <summary>Available text sizes for tag style parameters.</summary>
        public static readonly string[] TagStyleSizes = { "2", "2.5", "3", "3.5" };
        /// <summary>Available text styles for tag style parameters.</summary>
        public static readonly string[] TagStyleStyles = { "NOM", "BOLD", "ITALIC", "BOLDITALIC" };
        /// <summary>Available text colors for tag style parameters.</summary>
        public static readonly string[] TagStyleColors = { "BLACK", "BLUE", "GREEN", "RED", "ORANGE", "PURPLE", "GREY", "WHITE" };

        /// <summary>Original 4-color subset (for backwards compatibility with 48-param projects).</summary>
        public static readonly string[] TagStyleColorsCore = { "BLACK", "BLUE", "GREEN", "RED" };
        /// <summary>Extended colors added in v2 expansion.</summary>
        public static readonly string[] TagStyleColorsExtended = { "ORANGE", "PURPLE", "GREY", "WHITE" };
        /// <summary>Original 3-style subset (for backwards compatibility).</summary>
        public static readonly string[] TagStyleStylesCore = { "NOM", "BOLD", "ITALIC" };

        /// <summary>
        /// Build a tag style parameter name from size, style, and color.
        /// E.g. TagStyleParamName("2.5", "BOLD", "RED") => "TAG_2.5BOLD_RED_BOOL"
        /// </summary>
        public static string TagStyleParamName(string size, string style, string color)
            => $"TAG_{size}{style}_{color}_BOOL";

        private static string[] _cachedAllTagStyleParams;
        private static string[] _cachedCoreTagStyleParams;

        /// <summary>
        /// Get ALL tag style parameter names (4 sizes x 4 styles x 8 colors = 128). Cached.
        /// </summary>
        public static string[] AllTagStyleParams
        {
            get
            {
                if (_cachedAllTagStyleParams == null)
                {
                    var list = new List<string>();
                    foreach (var sz in TagStyleSizes)
                        foreach (var st in TagStyleStyles)
                            foreach (var co in TagStyleColors)
                                list.Add(TagStyleParamName(sz, st, co));
                    _cachedAllTagStyleParams = list.ToArray();
                }
                return _cachedAllTagStyleParams;
            }
        }

        /// <summary>
        /// Get CORE tag style parameter names only (4 sizes x 3 styles x 4 colors = 48). Cached.
        /// </summary>
        public static string[] CoreTagStyleParams
        {
            get
            {
                if (_cachedCoreTagStyleParams == null)
                {
                    var list = new List<string>();
                    foreach (var sz in TagStyleSizes)
                        foreach (var st in TagStyleStylesCore)
                            foreach (var co in TagStyleColorsCore)
                                list.Add(TagStyleParamName(sz, st, co));
                    _cachedCoreTagStyleParams = list.ToArray();
                }
                return _cachedCoreTagStyleParams;
            }
        }

        // ── Bounding box color parameters (separate from text color) ─────
        /// <summary>Tag bounding box fill color — Red channel (0-255).</summary>
        public static string TAG_BOX_COLOR_R { get; private set; } = "TAG_BOX_COLOR_R_INT";
        /// <summary>Tag bounding box fill color — Green channel (0-255).</summary>
        public static string TAG_BOX_COLOR_G { get; private set; } = "TAG_BOX_COLOR_G_INT";
        /// <summary>Tag bounding box fill color — Blue channel (0-255).</summary>
        public static string TAG_BOX_COLOR_B { get; private set; } = "TAG_BOX_COLOR_B_INT";
        /// <summary>Tag bounding box visibility.</summary>
        public static string TAG_BOX_VISIBLE { get; private set; } = "TAG_BOX_VISIBLE_BOOL";
        /// <summary>Tag bounding box style (SOLID/DASHED/NONE/ROUND).</summary>
        public static string TAG_BOX_STYLE { get; private set; } = "TAG_BOX_STYLE_TXT";
        /// <summary>Tag leader line color — Red channel (0-255).</summary>
        public static string TAG_LEADER_COLOR_R { get; private set; } = "TAG_LEADER_COLOR_R_INT";
        /// <summary>Tag leader line color — Green channel (0-255).</summary>
        public static string TAG_LEADER_COLOR_G { get; private set; } = "TAG_LEADER_COLOR_G_INT";
        /// <summary>Tag leader line color — Blue channel (0-255).</summary>
        public static string TAG_LEADER_COLOR_B { get; private set; } = "TAG_LEADER_COLOR_B_INT";
        /// <summary>Tag scale tier auto-selection flag (type BOOL).</summary>
        public static string TAG_SCALE_TIER_AUTO { get; private set; } = "TAG_SCALE_TIER_AUTO_BOOL";
        /// <summary>Active depth tier cached on tag family type (1-10, type INTEGER).</summary>
        public static string TAG_DEPTH_TIER { get; private set; } = "TAG_DEPTH_TIER_INT";

        // ── Semantic color meaning registry ──────────────────────────────
        // Maps colors to what they represent in each context:
        //   DISC: M=BLUE, E=ORANGE, P=GREEN, A=GREY, S=RED, FP=ORANGE, LV=PURPLE, G=BLACK
        //   STATUS: NEW=GREEN, EXISTING=BLUE, DEMOLISHED=RED, TEMPORARY=ORANGE
        //   SYS: HVAC=BLUE, ELEC=ORANGE, PLUMB=GREEN, FIRE=RED, LV=PURPLE, STRUCT=RED, GEN=GREY
        //   ZONE: Z01=BLUE, Z02=GREEN, Z03=ORANGE, Z04=RED
        //   LEVEL: GF=GREEN, L01=BLUE, L02=PURPLE, B1=RED, RF=ORANGE

        /// <summary>View color scheme parameter.</summary>
        public static string VIEW_COLOR_SCHEME { get; private set; } = "VIEW_COLOR_SCHEME_TXT";
        /// <summary>View discipline filter parameter.</summary>
        public static string VIEW_DISC_FILTER { get; private set; } = "VIEW_DISC_FILTER_TXT";

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

        // ── Warning threshold parameters ────────────────────────────────
        public static string ELC_PNL_RATED  => Ext("ELC_PNL_RATED");
        public static string WARN_RAMP_SLOPE      => Ext("WARN_RAMP_SLOPE");
        public static string WARN_VLT_DROP         => Ext("WARN_VLT_DROP");
        public static string WARN_SPR_COVER        => Ext("WARN_SPR_COVER");
        public static string WARN_NOISE            => Ext("WARN_NOISE");
        public static string WARN_COP_EER          => Ext("WARN_COP_EER");
        public static string WARN_FLEX_VEL         => Ext("WARN_FLEX_VEL");
        public static string WARN_CARBON           => Ext("WARN_CARBON");
        public static string WARN_UVAL_FLR         => Ext("WARN_UVAL_FLR");
        public static string WARN_UVAL_ROOF        => Ext("WARN_UVAL_ROOF");
        public static string WARN_UVAL_WALL        => Ext("WARN_UVAL_WALL");
        public static string WARN_HW_FLOW          => Ext("WARN_HW_FLOW");
        public static string WARN_ACCESS_WIDTH     => Ext("WARN_ACCESS_WIDTH");

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

        private static string[] _tag7Sections;
        /// <summary>All TAG7 sub-section parameter names in order (A-F).</summary>
        public static string[] TAG7Sections => _tag7Sections ??= new[] { TAG7A, TAG7B, TAG7C, TAG7D, TAG7E, TAG7F };

        /// <summary>Check if a parameter is any TAG7 variant (main or sub-section).</summary>
        public static bool IsTag7Param(string paramName)
        {
            return paramName == TAG7 || paramName == TAG7A || paramName == TAG7B ||
                   paramName == TAG7C || paramName == TAG7D || paramName == TAG7E ||
                   paramName == TAG7F;
        }

        // Phase 165 — Issue #10. Ordered lead parameter names for System B
        // (Handover) tiers T4..T10. WriteTag7All in Handover mode picks the
        // first non-empty parameter from each tier's group; this property
        // surfaces the lead/canonical param of each tier so callers can
        // iterate Tag7SystemBSections[i] alongside AllParaStates[i+3].
        // Index 0 = T4, index 6 = T10.
        private static string[] _tag7SystemBSections;
        public static string[] Tag7SystemBSections => _tag7SystemBSections ??= new[]
        {
            COMM_STATE_TXT,             // T4 — Commissioning lead
            CST_UG_PRICE_UGX,           // T5 — Cost lead
            CBN_A1_A3_KG_CO2E,          // T6 — Carbon lead
            ASS_SPOOL_NR_TXT,           // T7 — Fabrication lead
            CLASH_TRIAGE_SEVERITY_NR,   // T8 — Clash triage lead
            ASBUILT_DEVIATION_MM,       // T9 — As-built lead
            IFC_PSET_OVERRIDE_TXT,      // T10 — Compliance lead
        };

        /// <summary>
        /// Phase 165 — Issue #17. Active tag-mode enum used by WriteTag7All
        /// branch selection, depth-label rendering and UI mode toggles.
        /// </summary>
        public enum TagMode
        {
            /// <summary>Design &amp; Construction (default). T4-T6 == TAG7D-F.</summary>
            DC,
            /// <summary>Handover / FM. T4-T10 == COMM_/CST_/CBN_/FAB_/CLH_/ASB_/AUD_ groups.</summary>
            Handover,
            /// <summary>Project-specific custom T4-T10 payload.</summary>
            Custom,
        }

        /// <summary>
        /// Phase 165 — Issue #17. Resolve the active tag mode from the document.
        ///
        /// Lookup order: <c>ProjectInformation</c> first (so the mode is a
        /// project-wide setting), then <c>HANDOVER_MODE_HANDOVER_BOOL</c> /
        /// <c>HANDOVER_MODE_CUSTOM_BOOL</c> on Project Information; <c>DC</c>
        /// is the default when nothing is explicitly set.
        ///
        /// Per-element overrides (e.g. element-type level pattern flags) are
        /// resolved separately by <c>TagConfig.ResolveActivePatternMode</c>;
        /// this helper answers the project-default question.
        /// </summary>
        public static TagMode GetActiveTagMode(Document doc)
        {
            if (doc == null) return TagMode.DC;

            // Phase 165 perf — doc-keyed mode cache. WriteTag7All used to call
            // this per element; for a 1000-element batch that was 3000
            // LookupParameter calls on ProjectInformation per second tag pass.
            // Cache by doc.PathName (cheap key, stable for the duration of a
            // doc session). InvalidateModeCache flips it on Reload /
            // SetActiveTagMode.
            string key = doc.PathName ?? doc.Title ?? string.Empty;
            if (_modeCache.TryGetValue(key, out var cached)) return cached;

            TagMode resolved = TagMode.DC;
            try
            {
                var pi = doc.ProjectInformation;
                if (pi != null)
                {
                    if      (ReadBoolParam(pi, MODE_HANDOVER)) resolved = TagMode.Handover;
                    else if (ReadBoolParam(pi, MODE_CUSTOM))   resolved = TagMode.Custom;
                    else                                        resolved = TagMode.DC;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); resolved = TagMode.DC; }

            _modeCache[key] = resolved;
            return resolved;
        }

        // Phase 165 perf — doc-keyed cache for GetActiveTagMode.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, TagMode>
            _modeCache = new System.Collections.Concurrent.ConcurrentDictionary<string, TagMode>(StringComparer.Ordinal);

        /// <summary>
        /// Phase 165 perf — invalidate the mode cache for a given document
        /// (or all documents when doc is null). Called by SetActiveTagMode
        /// after writing the trio so subsequent reads see the fresh value.
        /// </summary>
        public static void InvalidateModeCache(Document doc = null)
        {
            if (doc == null) { _modeCache.Clear(); return; }
            string key = doc.PathName ?? doc.Title ?? string.Empty;
            _modeCache.TryRemove(key, out _);
        }

        /// <summary>
        /// Phase 165 — Issue #17. Set the active tag mode on
        /// ProjectInformation, flipping the three HANDOVER_MODE_*_BOOL params
        /// mutually exclusively. Caller must wrap the call in a Transaction.
        /// </summary>
        public static bool SetActiveTagMode(Document doc, TagMode mode)
        {
            if (doc == null) return false;
            var pi = doc.ProjectInformation;
            if (pi == null) return false;
            bool a = WriteBoolParam(pi, MODE_DC,       mode == TagMode.DC);
            bool b = WriteBoolParam(pi, MODE_HANDOVER, mode == TagMode.Handover);
            bool c = WriteBoolParam(pi, MODE_CUSTOM,   mode == TagMode.Custom);
            // Phase 165 perf — drop any cached value for this doc so the next
            // GetActiveTagMode read sees what we just wrote.
            InvalidateModeCache(doc);
            return a || b || c;
        }

        // Internal Yes/No helpers — match the storage convention used by
        // SetParagraphDepthCommand (string "Yes"/"No" or integer 0/1) so the
        // mode params behave identically to PARA_STATE_N_BOOL.
        private static bool ReadBoolParam(Element host, string paramName)
        {
            if (host == null || string.IsNullOrEmpty(paramName)) return false;
            try
            {
                var p = host.LookupParameter(paramName);
                if (p == null) return false;
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

        private static bool WriteBoolParam(Element host, string paramName, bool target)
        {
            if (host == null || string.IsNullOrEmpty(paramName)) return false;
            try
            {
                var p = host.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return false;
                if (p.StorageType == StorageType.String)
                {
                    string want = target ? "Yes" : "No";
                    string cur = p.AsString() ?? "";
                    if (string.Equals(cur, want, StringComparison.OrdinalIgnoreCase)) return false;
                    p.Set(want);
                    return true;
                }
                if (p.StorageType == StorageType.Integer)
                {
                    int want = target ? 1 : 0;
                    if (p.AsInteger() == want) return false;
                    p.Set(want);
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        // ── Token presets (named token index arrays) ────────────────────
        public static Dictionary<string, int[]> TokenPresets { get; private set; } = new Dictionary<string, int[]>();

        // ── Container groups and flat container list ─────────────────────
        public static ContainerGroupDef[] ContainerGroups { get; private set; } = Array.Empty<ContainerGroupDef>();
        private static ContainerParamDef[] _allContainers;
        private static Dictionary<string, List<ContainerParamDef>> _containersByCategory;
        // F-02: Cache for ContainersForCategory results — avoids List+ToArray allocation per call
        private static System.Collections.Concurrent.ConcurrentDictionary<string, ContainerParamDef[]>
            _containerForCategoryCache;
        // F-15: Cache for GetContainerTuples result — avoids LINQ Select+ToArray per call
        private static (string param, int[] tokens, string sep, string[] categories)[] _containerTuplesCache;

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
            // Use _allContainers directly to avoid reentrant EnsureLoaded() calls
            if (_allContainers == null)
                _allContainers = ContainerGroups.SelectMany(g => g.Params).ToArray();
            if (string.IsNullOrEmpty(categoryName)) return _allContainers.Where(c => c.Categories == null).ToArray();

            // F-02: Cache result per category — ContainersForCategory is called per-element in hot loops
            // R1-PR-01: Snapshot to local to avoid race between null check and use during Reload()
            var cache = _containerForCategoryCache;
            if (cache == null)
            {
                cache = new System.Collections.Concurrent.ConcurrentDictionary<string, ContainerParamDef[]>(StringComparer.OrdinalIgnoreCase);
                _containerForCategoryCache = cache;
            }
            return cache.GetOrAdd(categoryName, key =>
            {
                var result = new List<ContainerParamDef>();
                // Universal containers (null categories) always apply
                foreach (var c in _allContainers)
                {
                    if (c.Categories == null)
                        result.Add(c);
                }
                // Plus category-specific matches
                if (_containersByCategory.TryGetValue(key, out var specific))
                    result.AddRange(specific);
                return result.ToArray();
            });
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
            // Use _allContainers directly to avoid reentrant EnsureLoaded() calls
            if (_allContainers == null)
                _allContainers = ContainerGroups.SelectMany(g => g.Params).ToArray();
            // F-15: Cache result — GetContainerTuples is called per-element in WriteContainers hot path
            return _containerTuplesCache ??= _allContainers
                .Select(c => (c.ParamName, c.TokenIndices, c.Separator, c.Categories))
                .ToArray();
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
            StingLog.Info($"ResolveUniversalCategoryEnums: resolving {UniversalCategories?.Length ?? 0} categories");
            var result = ResolveCategoryEnums(UniversalCategories);
            StingLog.Info($"ResolveUniversalCategoryEnums: resolved to {result?.Length ?? 0} BuiltInCategory enums");
            return result;
        }

        /// <summary>
        /// Build the discipline bindings dictionary in the format SharedParamGuids expects:
        /// paramName → BuiltInCategory[]. Derived from container_groups.
        /// </summary>
        public static Dictionary<string, BuiltInCategory[]> BuildDisciplineBindings()
        {
            EnsureLoaded();
            var bindings = new Dictionary<string, BuiltInCategory[]>();
            foreach (var group in ContainerGroups)
            {
                if (group.Categories == null) continue; // universal — handled by Pass 1
                var enums = ResolveCategoryEnums(group.Categories);
                foreach (var param in group.Params)
                    bindings[param.ParamName] = enums;
            }
            return bindings;
        }

        /// <summary>
        /// Override tag format settings from project_config.json.
        /// Called by TagConfig.LoadFromFile when the config has TAG_FORMAT section.
        /// </summary>
        internal static void OverrideTagFormat(string separator, int numPad, string[] segmentOrder)
        {
            if (!string.IsNullOrEmpty(separator)) _overrideSeparator = separator;
            if (numPad > 0) _overrideNumPad = numPad;
            if (segmentOrder != null && segmentOrder.Length > 0) _overrideSegmentOrder = segmentOrder;
        }

        /// <summary>R2-FIX: Clear container-for-category cache so reloaded schema is reflected.</summary>
        public static void ClearContainerCache()
        {
            lock (_lock) { _containerForCategoryCache = null; }
        }

        /// <summary>Force reload from disk. Call after editing PARAMETER_REGISTRY.json.</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _loaded = false;
                _allContainers = null;
                _containersByCategory = null;
                _containerForCategoryCache = null;   // F-02
                _containerTuplesCache = null;         // F-15
                WarningThresholds = new Dictionary<string, WarningThresholdDef>(StringComparer.Ordinal);
                _categoryWarnings = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _paragraphContainers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            // Invalidate downstream caches that depend on our data
            SharedParamGuids.InvalidateCache();
            // Phase 165 perf — refresh the source-token-name gate and the
            // doc-keyed mode cache so a Reload that renames any token /
            // changes any HANDOVER_MODE_* binding doesn't leak stale cached
            // values into the next tag-write batch.
            ParameterHelpers.InvalidateSourceTokenSet();
            InvalidateModeCache();
            _allParagraphContainersCache = null;
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
                StingLog.Info("ParamRegistry.EnsureLoaded: first-time load starting");
                try
                {
                    LoadFromFile();
                    StingLog.Info("ParamRegistry.EnsureLoaded: LoadFromFile completed successfully");
                }
                catch (Exception ex)
                {
                    StingLog.Error("EnsureLoaded: LoadFromFile failed, using minimal defaults", ex);
                    // Set minimal defaults so the plugin doesn't crash entirely
                    if (UniversalParams == null || UniversalParams.Length == 0)
                    {
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
                    if (AllTokenParams == null || AllTokenParams.Length == 0)
                    {
                        AllTokenParams = new[]
                        {
                            "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                            "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                            "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
                        };
                    }
                    if (ContainerGroups == null)
                        ContainerGroups = Array.Empty<ContainerGroupDef>();
                    if (CategoryEnumMap == null)
                        CategoryEnumMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (UniversalCategories == null)
                        UniversalCategories = Array.Empty<string>();
                    // Ensure TAG convenience names are set — many commands access these
                    if (string.IsNullOrEmpty(TAG1)) TAG1 = "ASS_TAG_1_TXT";
                    if (string.IsNullOrEmpty(TAG2)) TAG2 = "ASS_TAG_2_TXT";
                    if (string.IsNullOrEmpty(TAG3)) TAG3 = "ASS_TAG_3_TXT";
                    if (string.IsNullOrEmpty(TAG4)) TAG4 = "ASS_TAG_4_TXT";
                    if (string.IsNullOrEmpty(TAG5)) TAG5 = "ASS_TAG_5_TXT";
                    if (string.IsNullOrEmpty(TAG6)) TAG6 = "ASS_TAG_6_TXT";
                    if (string.IsNullOrEmpty(TAG7)) TAG7 = "ASS_TAG_7_TXT";
                    if (string.IsNullOrEmpty(STATUS)) STATUS = "ASS_STATUS_TXT";
                    if (string.IsNullOrEmpty(DETAIL_NUM)) DETAIL_NUM = "ASS_INST_DETAIL_NUM_TXT";
                    if (string.IsNullOrEmpty(MNT_TYPE)) MNT_TYPE = "MNT_TYPE_TXT";
                    // Ensure GUID maps exist
                    if (_guidByName == null) _guidByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
                    if (_nameByGuid == null) _nameByGuid = new Dictionary<Guid, string>();
                    if (_extendedParams == null) _extendedParams = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (TokenPresets == null) TokenPresets = new Dictionary<string, int[]>();
                    if (SourceTokens == null || SourceTokens.Length == 0)
                    {
                        SourceTokens = new[]
                        {
                            new TokenDef { Slot = 0, Key = "DISC", ParamName = "ASS_DISCIPLINE_COD_TXT" },
                            new TokenDef { Slot = 1, Key = "LOC",  ParamName = "ASS_LOC_TXT" },
                            new TokenDef { Slot = 2, Key = "ZONE", ParamName = "ASS_ZONE_TXT" },
                            new TokenDef { Slot = 3, Key = "LVL",  ParamName = "ASS_LVL_COD_TXT" },
                            new TokenDef { Slot = 4, Key = "SYS",  ParamName = "ASS_SYSTEM_TYPE_TXT" },
                            new TokenDef { Slot = 5, Key = "FUNC", ParamName = "ASS_FUNC_TXT" },
                            new TokenDef { Slot = 6, Key = "PROD", ParamName = "ASS_PRODCT_COD_TXT" },
                            new TokenDef { Slot = 7, Key = "SEQ",  ParamName = "ASS_SEQ_NUM_TXT" },
                        };
                    }
                    StingLog.Info("ParamRegistry.EnsureLoaded: minimal defaults applied");
                }
                _loaded = true;
            }
        }

        private static void LoadFromFile()
        {
            StingLog.Info("ParamRegistry.LoadFromFile: starting");
            string path = StingToolsApp.FindDataFile("PARAMETER_REGISTRY.json");
            if (path == null)
            {
                StingLog.Warn("PARAMETER_REGISTRY.json not found — using compiled defaults");
                LoadDefaults();
                return;
            }
            StingLog.Info($"ParamRegistry.LoadFromFile: found at {path}");

            try
            {
                StingLog.Info("ParamRegistry.LoadFromFile: reading file");
                string json = File.ReadAllText(path);
                StingLog.Info($"ParamRegistry.LoadFromFile: read {json.Length} chars, parsing JSON");

                // CRASH FIX: Newtonsoft.Json version conflicts with other Revit addins
                // can cause native crashes during JObject.Parse(). Isolate JSON parsing
                // in its own try/catch so a conflict falls back to compiled defaults
                // instead of crashing Revit entirely.
                JObject root;
                try
                {
                    root = JObject.Parse(json);
                }
                catch (Exception jsonEx)
                {
                    StingLog.Error("ParamRegistry: JObject.Parse FAILED — possible Newtonsoft.Json " +
                        "version conflict with another Revit addin. Using compiled defaults.", jsonEx);
                    LoadDefaults();
                    return;
                }
                StingLog.Info("ParamRegistry.LoadFromFile: JSON parsed OK");

                // Tag format (base values from PARAMETER_REGISTRY.json)
                var fmt = root["tag_format"];
                if (fmt != null)
                {
                    _baseSeparator = fmt["separator"]?.ToString() ?? "-";
                    _baseNumPad = fmt["num_pad"]?.Value<int>() ?? 4;
                    _baseSegmentOrder = fmt["segment_order"]?.ToObject<string[]>() ?? _baseSegmentOrder;
                    _cachedSegmentOrder = null; // PERF-05: Invalidate cache after loading base values
                }

                StingLog.Info("ParamRegistry.LoadFromFile: tag_format loaded");

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

                StingLog.Info($"ParamRegistry.LoadFromFile: {SourceTokens.Length} source tokens loaded");

                // Support params
                var supArr = root["support_params"] as JArray;
                if (supArr != null)
                {
                    foreach (JObject s in supArr)
                    {
                        string name = s["param_name"]?.ToString() ?? "";
                        // Exact-match the canonical singletons. The earlier substring
                        // form ("name.Contains") let any later support_params row
                        // overwrite the binding (e.g. SLV_STATUS_TXT was clobbering
                        // STATUS). Defaults at the field declarations remain authoritative;
                        // these assignments just confirm them when the registry agrees.
                        if      (name == "ASS_STATUS_TXT")             STATUS = name;
                        else if (name == "ASS_INST_DETAIL_NUM_TXT")    DETAIL_NUM = name;
                        else if (name == "MNT_TYPE_TXT")               MNT_TYPE = name;
                        else if (name == "TAG_PARA_STATE_1_BOOL")      PARA_STATE_1 = name;
                        else if (name == "TAG_PARA_STATE_2_BOOL")      PARA_STATE_2 = name;
                        else if (name == "TAG_PARA_STATE_3_BOOL")      PARA_STATE_3 = name;
                        else if (name == "TAG_WARN_VISIBLE_BOOL")      WARN_VISIBLE = name;
                        else if (name == "TAG_WARN_SEVERITY_FILTER_TXT") WARN_SEVERITY_FILTER = name;

                        // DATA-02: Track required/optional status
                        bool isReq = s["required"]?.Value<bool>() ?? false;
                        if (isReq) _requiredParams.Add(name);
                    }
                }

                // DATA-02: Also track required flag on source_tokens
                if (tokArr != null)
                {
                    foreach (JObject t in tokArr)
                    {
                        bool isReq = t["required"]?.Value<bool>() ?? false;
                        string pn = t["param_name"]?.ToString() ?? "";
                        if (isReq && !string.IsNullOrEmpty(pn)) _requiredParams.Add(pn);
                    }
                }

                // DATA-02: Also track required flag on containers
                var contArr = root["containers"] as JArray;
                if (contArr != null)
                {
                    foreach (JObject c in contArr)
                    {
                        bool isReq = c["required"]?.Value<bool>() ?? false;
                        string pn = c["param_name"]?.ToString() ?? "";
                        if (isReq && !string.IsNullOrEmpty(pn)) _requiredParams.Add(pn);
                    }
                }

                StingLog.Info("ParamRegistry.LoadFromFile: support params loaded");

                // Token presets
                var presets = root["token_presets"] as JObject;
                TokenPresets = new Dictionary<string, int[]>();
                if (presets != null)
                {
                    foreach (var kvp in presets)
                        TokenPresets[kvp.Key] = kvp.Value.ToObject<int[]>();
                }

                StingLog.Info($"ParamRegistry.LoadFromFile: {TokenPresets.Count} token presets loaded");

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

                StingLog.Info($"ParamRegistry.LoadFromFile: {ContainerGroups.Length} container groups loaded");

                // Category enum map
                var catMap = root["category_enum_map"] as JObject;
                CategoryEnumMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (catMap != null)
                {
                    foreach (var kvp in catMap)
                        CategoryEnumMap[kvp.Key] = kvp.Value.ToString();
                }

                StingLog.Info($"ParamRegistry.LoadFromFile: {CategoryEnumMap.Count} category enum mappings loaded");

                // Universal categories — exclude "Materials" which is handled separately
                // via BuildGroupCategoryOverrides() in LoadSharedParamsCommand.
                // Including it here would bind ALL params to OST_Materials.
                var rawUCats = root["universal_categories"]?.ToObject<string[]>() ?? Array.Empty<string>();
                UniversalCategories = rawUCats
                    .Where(c => !c.Equals("Materials", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                StingLog.Info($"ParamRegistry.LoadFromFile: {UniversalCategories.Length} universal categories loaded (Materials excluded)");

                // Load extended params
                LoadExtendedParams(root);
                StingLog.Info($"ParamRegistry.LoadFromFile: {_extendedParams?.Count ?? 0} extended params loaded");

                // DATA-02: Load required/optional flags from all param sections
                LoadRequiredFlags(root);
                StingLog.Info($"ParamRegistry.LoadFromFile: {RequiredParams.Count} required params loaded");

                // Load warning thresholds (v5.5)
                LoadWarningThresholds(root);
                StingLog.Info($"ParamRegistry.LoadFromFile: {WarningThresholds.Count} warning thresholds loaded");

                // Build GUID lookups
                StingLog.Info("ParamRegistry.LoadFromFile: building GUID maps");
                BuildGuidMaps(root);
                StingLog.Info($"ParamRegistry.LoadFromFile: {_guidByName?.Count ?? 0} GUIDs mapped");

                // Build universal params list
                StingLog.Info("ParamRegistry.LoadFromFile: building universal params list");
                BuildUniversalParams(root);
                StingLog.Info($"ParamRegistry.LoadFromFile: {UniversalParams?.Length ?? 0} universal params");

                // Build discipline category name mappings
                StingLog.Info("ParamRegistry.LoadFromFile: building discipline category names");
                BuildDisciplineCategoryNames();

                // CRASH FIX: Build _allContainers here instead of lazily in the AllContainers
                // property. The old code used AllContainers.Length in the success log line below,
                // which called EnsureLoaded() — but _loaded is still false at this point, causing
                // INFINITE RECURSION (C# lock is reentrant on the same thread).
                _allContainers = ContainerGroups.SelectMany(g => g.Params).ToArray();

                // FIX-12.4: Supplement GUID map from MR_PARAMETERS.txt
                try
                {
                    string _mrFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                    if (!string.IsNullOrEmpty(_mrFile))
                    {
                        if (_guidByName == null)
                            _guidByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
                        int _sup = 0;
                        foreach (string _ml in File.ReadAllLines(_mrFile))
                        {
                            if (!_ml.StartsWith("PARAM")) continue;
                            var _mp = _ml.Split('\t');
                            if (_mp.Length < 3) continue;
                            string _mg = _mp[1]; string _mn = _mp[2];
                            if (string.IsNullOrEmpty(_mn) || _guidByName.ContainsKey(_mn)) continue;
                            if (Guid.TryParse(_mg, out Guid _gg))
                            { _guidByName[_mn] = _gg; _sup++; }
                        }
                        StingLog.Info($"ParamRegistry: supplemented {_sup} GUIDs from MR_PARAMETERS.txt");
                    }
                }
                catch (Exception _mrEx) { StingLog.Warn($"ParamRegistry MR supplement: {_mrEx.Message}"); }

                StingLog.Info($"ParamRegistry loaded: {SourceTokens.Length} tokens, {ContainerGroups.Length} groups, {_allContainers.Length} containers, {_guidByName?.Count ?? 0} GUIDs");
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

        /// <summary>
        /// DATA-02: Scan all param sections in PARAMETER_REGISTRY.json for a "required" field.
        /// Params flagged as required are added to RequiredParams. If no "required" flags are
        /// found at all, the default set (8 source tokens + TAG1) is retained.
        /// </summary>
        private static void LoadRequiredFlags(JObject root)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Scan source_tokens
            var tokArr = root["source_tokens"] as JArray;
            if (tokArr != null)
            {
                foreach (JObject t in tokArr)
                {
                    string paramName = t["param_name"]?.ToString() ?? "";
                    bool? req = t["required"]?.Value<bool>();
                    if (req == true && !string.IsNullOrEmpty(paramName))
                        found.Add(paramName);
                }
            }

            // Scan support_params
            var supArr = root["support_params"] as JArray;
            if (supArr != null)
            {
                foreach (JObject s in supArr)
                {
                    string paramName = s["param_name"]?.ToString() ?? "";
                    bool? req = s["required"]?.Value<bool>();
                    if (req == true && !string.IsNullOrEmpty(paramName))
                        found.Add(paramName);
                }
            }

            // Scan container_groups params
            var groupArr = root["container_groups"] as JArray;
            if (groupArr != null)
            {
                foreach (JObject g in groupArr)
                {
                    var paramArr = g["params"] as JArray;
                    if (paramArr == null) continue;
                    foreach (JObject p in paramArr)
                    {
                        string paramName = p["param_name"]?.ToString() ?? "";
                        bool? req = p["required"]?.Value<bool>();
                        if (req == true && !string.IsNullOrEmpty(paramName))
                            found.Add(paramName);
                    }
                }
            }

            // Scan extended_params
            var extArr = root["extended_params"] as JArray;
            if (extArr != null)
            {
                foreach (JObject e in extArr)
                {
                    string paramName = e["param_name"]?.ToString() ?? "";
                    bool? req = e["required"]?.Value<bool>();
                    if (req == true && !string.IsNullOrEmpty(paramName))
                        found.Add(paramName);
                }
            }

            // Only replace defaults if we actually found required flags in JSON
            if (found.Count > 0)
                RequiredParams = found;
            // else keep the default set (8 source tokens + TAG1)
        }

        /// <summary>Load warning threshold definitions from PARAMETER_REGISTRY.json.</summary>
        private static void LoadWarningThresholds(JObject root)
        {
            WarningThresholds = new Dictionary<string, WarningThresholdDef>(StringComparer.Ordinal);
            var warnArr = root["warning_thresholds"] as JArray;
            if (warnArr == null) return;

            foreach (JObject w in warnArr)
            {
                var def = new WarningThresholdDef
                {
                    ParamName   = w["param_name"]?.ToString() ?? "",
                    Guid        = w["guid"]?.ToString() ?? "",
                    Description = w["description"]?.ToString() ?? "",
                    Threshold   = w["threshold"]?.ToString() ?? "",
                    Unit        = w["unit"]?.ToString() ?? "",
                    Severity    = w["severity"]?.ToString() ?? "MEDIUM",
                };
                if (!string.IsNullOrEmpty(def.ParamName))
                    WarningThresholds[def.ParamName] = def;
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

            // Warning thresholds — add GUIDs to lookup
            foreach (var wt in WarningThresholds.Values)
            {
                if (System.Guid.TryParse(wt.Guid, out Guid wg))
                {
                    _guidByName[wt.ParamName] = wg;
                    _nameByGuid[wg] = wt.ParamName;
                }
            }

            // Phase 165 — register the three HANDOVER_MODE_*_BOOL GUIDs so
            // ParamRegistry.AddBindings can bind them as ProjectInformation
            // shared parameters and ParamRegistry.GetGuid resolves them.
            // Issue #16 — without this, the mode toggles can't be written.
            RegisterModeGuid(MODE_HANDOVER, MODE_HANDOVER_GUID);
            RegisterModeGuid(MODE_DC,       MODE_DC_GUID);
            RegisterModeGuid(MODE_CUSTOM,   MODE_CUSTOM_GUID);
            // TAG-01: single style code parameter
            RegisterModeGuid(TAG_STYLE_CODE, TAG_STYLE_CODE_GUID);
        }

        private static void RegisterModeGuid(string name, string guidStr)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(guidStr)) return;
            if (Guid.TryParse(guidStr, out Guid g))
            {
                _guidByName[name] = g;
                _nameByGuid[g] = name;
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
            // Use _allContainers directly (not the AllContainers property) to avoid
            // re-entering EnsureLoaded() if this is called during loading
            if (_allContainers == null)
                _allContainers = ContainerGroups.SelectMany(g => g.Params).ToArray();
            foreach (var c in _allContainers)
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
            _baseSeparator = "-";
            _baseNumPad = 4;
            _baseSegmentOrder = new[] { "DISC", "LOC", "ZONE", "LVL", "SYS", "FUNC", "PROD", "SEQ" };
            _cachedSegmentOrder = null; // PERF-05: Invalidate cache when defaults are reloaded

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

            // Extended params defaults — use indexer syntax (dict[key] = value) instead
            // of collection initializer to prevent duplicate-key crashes if keys overlap.
            _extendedParams = new Dictionary<string, string>(StringComparer.Ordinal);
            // Identity
            _extendedParams["ID"] = "ASS_ID_TXT"; _extendedParams["DESC"] = "ASS_DESCRIPTION_TXT";
            _extendedParams["MFR"] = "ASS_MANUFACTURER_TXT"; _extendedParams["MODEL"] = "ASS_MODEL_NR_TXT";
            _extendedParams["TYPE_NAME"] = "ASS_TYPE_NAME_TXT"; _extendedParams["FAMILY_NAME"] = "ASS_FAMILY_NAME_TXT";
            _extendedParams["CAT"] = "ASS_CAT_TXT"; _extendedParams["TYPE_MARK"] = "ASS_TYPE_MARK_TXT";
            _extendedParams["TYPE_COMMENTS"] = "ASS_TYPE_COMMENTS_TXT"; _extendedParams["KEYNOTE"] = "ASS_KEYNOTE_TXT";
            _extendedParams["UNIFORMAT"] = "ASS_UNIFORMAT_TXT"; _extendedParams["UNIFORMAT_DESC"] = "ASS_UNIFORMAT_DESC_TXT";
            _extendedParams["OMNICLASS"] = "ASS_OMNICLASS_TXT"; _extendedParams["SIZE"] = "ASS_SIZE_TXT";
            _extendedParams["COST"] = "ASS_CST_UNIT_PRICE_UGX_NR"; _extendedParams["PRJ_COMMENTS"] = "PRJ_COMMENTS_TXT";
            // Spatial
            _extendedParams["ROOM_NAME"] = "ASS_ROOM_NAME_TXT"; _extendedParams["ROOM_NUM"] = "ASS_ROOM_NUM_TXT";
            _extendedParams["ROOM_AREA"] = "ASS_ROOM_AREA_SQ_M"; _extendedParams["ROOM_VOLUME"] = "ASS_ROOM_VOLUME_CU_M";
            _extendedParams["DEPT"] = "ASS_DEPARTMENT_ASSIGNMENT_TXT"; _extendedParams["GRID_REF"] = "PRJ_GRID_REF_TXT";
            _extendedParams["BLE_ROOM_NAME"] = "BLE_ROOM_NAME_TXT"; _extendedParams["BLE_ROOM_NUM"] = "ASS_ROOM_NUM_TXT";
            // Extended tokens
            _extendedParams["ORIGIN"] = "ASS_ORIGIN_TXT"; _extendedParams["PROJECT"] = "ASS_PROJECT_TXT";
            _extendedParams["REV"] = "ASS_REV_TXT"; _extendedParams["VOLUME"] = "ASS_VOL_TXT";
            // BLE dimensional
            _extendedParams["WALL_HEIGHT"] = "BLE_WALL_HEIGHT_MM"; _extendedParams["WALL_LENGTH"] = "BLE_WALL_LENGTH_MM";
            _extendedParams["WALL_THICKNESS"] = "BLE_WALL_THICKNESS_MM"; _extendedParams["DOOR_WIDTH"] = "BLE_DOOR_WIDTH_MM";
            _extendedParams["DOOR_HEIGHT"] = "BLE_DOOR_HEIGHT_MM"; _extendedParams["WINDOW_WIDTH"] = "BLE_WINDOW_WIDTH_MM";
            _extendedParams["WINDOW_HEIGHT"] = "BLE_WINDOW_HEIGHT_MM";
            _extendedParams["WINDOW_SILL"] = "BLE_WINDOW_SILL_HEIGHT_FROM_FLR_MM";
            _extendedParams["FLR_THICKNESS"] = "BLE_FLR_THICKNESS_MM"; _extendedParams["ELE_AREA"] = "BLE_ELE_AREA_SQ_M";
            _extendedParams["CBL_TRAY_WIDTH"] = "BLE_CBL_TRAY_WIDTH_MM"; _extendedParams["CBL_TRAY_DEPTH"] = "BLE_CBL_TRAY_DEPTH_MM";
            _extendedParams["CEILING_HEIGHT"] = "BLE_CEILING_HEIGHT_MM"; _extendedParams["ROOF_SLOPE"] = "BLE_ROOF_SLOPE_DEG";
            _extendedParams["STAIR_TREAD"] = "BLE_STAIR_GOING_MM"; _extendedParams["STAIR_RISE"] = "BLE_STAIR_RISE_MM";
            _extendedParams["STAIR_WIDTH"] = "BLE_STAIR_WIDTH_MM"; _extendedParams["RAMP_SLOPE"] = "BLE_RAMP_SLOPE_PCT";
            _extendedParams["RAMP_WIDTH"] = "BLE_RAMP_WIDTH_MM"; _extendedParams["STRUCT_TYPE"] = "BLE_STRUCT_ELE_TYPE_TXT";
            _extendedParams["FIRE_RATING"] = "FLS_PROT_FLS_RESISTANCE_RATING_MINUTES_MIN";
            // Electrical
            _extendedParams["ELC_POWER"] = "ELC_CKT_PWR_KW"; _extendedParams["ELC_VOLTAGE"] = "ELC_CKT_VLT_V";
            _extendedParams["ELC_CIRCUIT_NR"] = "ELC_CKT_NR"; _extendedParams["ELC_PNL_NAME"] = "ELC_PNL_DESIGNATION_NAME_TXT";
            _extendedParams["ELC_PNL_VOLTAGE"] = "ELC_VLT_PRIMARY_RATING_V"; _extendedParams["ELC_PHASES"] = "ELC_CKT_PHASE_COUNT_NR";
            _extendedParams["ELC_PNL_LOAD"] = "ELC_PNL_CONNECTED_LOAD_KW"; _extendedParams["ELC_PNL_FED_FROM"] = "ELC_PNL_FED_FROM_PNL_TXT";
            _extendedParams["ELC_MAIN_BRK"] = "ELC_PNL_MAIN_BRK_A"; _extendedParams["ELC_WAYS"] = "ELC_PNL_NUM_OF_WAYS_NR";
            _extendedParams["ELC_IP_RATING"] = "ELC_IP_RATING_TXT";
            // Phase 178 — Electrical advanced calculations & automation
            // Reuse existing 4 (no MR_PARAMETERS additions); add 7 new ones.
            _extendedParams["ELC_PNL_FAULT_KA"]      = "ELC_PNL_SHORT_CIRCUIT_RATING_KA";
            _extendedParams["ELC_PNL_AIC_KA"]        = "ELC_PNL_AIC_RATING_KA";
            _extendedParams["ELC_FEEDER_CSA"]        = "ELC_FEEDER_CSA_MM2";
            _extendedParams["ELC_FEEDER_RATING_A"]   = "ELC_FEEDER_RATING_A";
            _extendedParams["ELC_CKT_VD_PCT"]        = "ELC_VLT_DROP_PCT";
            _extendedParams["ELC_CKT_CSA_MM2"]       = "ELC_CBL_SZ_MM";
            _extendedParams["ELC_CONDUIT_FILL_PCT"]  = "ELC_CDT_CBL_FILL_PCT";
            _extendedParams["ELC_CDT_BEND_ANGLE_DEG"] = "ELC_CDT_BEND_ANGLE_DEG";
            _extendedParams["ELC_CDT_BEND_COUNT_NR"]  = "ELC_CDT_BEND_COUNT_NR";
            _extendedParams["ELC_CDT_RUN_LENGTH_M"]   = "ELC_CDT_RUN_LENGTH_M";
            _extendedParams["ELC_CDT_CABLE_COUNT_NR"] = "ELC_CDT_CABLE_COUNT_NR";
            _extendedParams["ELC_EMERG_COVERED"]     = "ELC_EMERG_COVERED_BOOL";
            _extendedParams["ELC_LPD_W_M2"]          = "ELC_LPD_W_PER_M2";
            _extendedParams["ELC_LPD_LIMIT_W_M2"]    = "ELC_LPD_LIMIT_W_PER_M2";
            _extendedParams["ELC_LPD_STATUS"]        = "ELC_LPD_STATUS_TXT";
            // Phase 179 — advanced analysis & external integration
            _extendedParams["ELC_ARC_FLASH_IE"]     = "ELC_ARC_FLASH_IE_CAL_CM2";
            _extendedParams["ELC_ARC_FLASH_BD"]     = "ELC_ARC_FLASH_BOUNDARY_MM";
            _extendedParams["ELC_ARC_FLASH_PPE"]    = "ELC_ARC_FLASH_PPE_CAT";
            _extendedParams["ELC_ARC_FLASH_WD"]     = "ELC_ARC_FLASH_WORK_DIST_MM";
            _extendedParams["ELC_ARC_FLASH_LABEL"]  = "ELC_ARC_FLASH_LABEL_TXT";
            _extendedParams["ELC_SEL_COORD_OK"]     = "ELC_SEL_COORD_VERIFIED_BOOL";
            _extendedParams["ELC_BUSBAR_CSA"]       = "ELC_BUSBAR_CSA_MM2";
            _extendedParams["ELC_BUSBAR_RATING"]    = "ELC_BUSBAR_RATING_A";
            _extendedParams["ELC_BUSBAR_FILL"]      = "ELC_BUSBAR_FILL_PCT";
            _extendedParams["ELC_CONDUIT_ROUTE"]    = "ELC_CONDUIT_ROUTE_TXT";
            _extendedParams["ELC_PHOTO_LUX"]        = "ELC_PHOTO_LUX_CALC";
            _extendedParams["ELC_PHOTO_UGR"]        = "ELC_PHOTO_UGR_CALC";
            // Phase 180 — photometric library / luminaire metadata
            _extendedParams["ELC_PHOTO_FILE_PATH"]  = "ELC_PHOTO_FILE_PATH_TXT";
            _extendedParams["ELC_PHOTO_LUMENS"]     = "ELC_PHOTO_LUMENS_NR";
            _extendedParams["ELC_PHOTO_WATTS"]      = "ELC_PHOTO_WATTS_NR";
            _extendedParams["ELC_PHOTO_EFFICACY"]   = "ELC_PHOTO_EFFICACY_LM_W";
            _extendedParams["ELC_PHOTO_BEAM_ANGLE"] = "ELC_PHOTO_BEAM_ANGLE_DEG";
            _extendedParams["ELC_PHOTO_CCT"]        = "ELC_PHOTO_CCT_K";
            _extendedParams["ELC_PHOTO_CRI"]        = "ELC_PHOTO_CRI_NR";
            _extendedParams["ELC_PHOTO_SYMMETRY"]   = "ELC_PHOTO_SYMMETRY_TXT";
            // Phase 181 — multi-engine photometric results
            _extendedParams["ELC_PHOTO_LUX_DIALUX"]     = "ELC_PHOTO_LUX_DIALUX_NR";
            _extendedParams["ELC_PHOTO_LUX_ELUMTOOLS"]  = "ELC_PHOTO_LUX_ELUMTOOLS_NR";
            _extendedParams["ELC_PHOTO_LUX_RELUX"]      = "ELC_PHOTO_LUX_RELUX_NR";
            _extendedParams["ELC_PHOTO_UNIFORMITY"]     = "ELC_PHOTO_UNIFORMITY_NR";
            _extendedParams["ELC_PHOTO_LAST_ENGINE"]    = "ELC_PHOTO_LAST_ENGINE_TXT";
            _extendedParams["ELC_PHOTO_LAST_CALC_DATE"] = "ELC_PHOTO_LAST_CALC_DATE_TXT";
            // Lighting
            _extendedParams["LTG_WATTAGE"] = "LTG_FIX_LMP_WATTAGE_W"; _extendedParams["LTG_LUMENS"] = "CST_FIX_LUMEN_OUTPUT_LM";
            _extendedParams["LTG_EFFICACY"] = "LTG_FIX_EFFICACY_LM_W"; _extendedParams["LTG_LAMP_TYPE"] = "LTG_FIX_LAMP_TYPE_TXT";
            // HVAC
            _extendedParams["HVC_DUCT_FLOW"] = "HVC_DCT_FLW_CFM"; _extendedParams["HVC_VELOCITY"] = "HVC_VEL_MPS";
            _extendedParams["HVC_PRESSURE"] = "HVC_PRESSURE_DROP_PA"; _extendedParams["HVC_AIRFLOW"] = "HVC_AIRFLOW_LPS";
            // Plumbing
            _extendedParams["PLM_PIPE_FLOW"] = "PLM_PPE_FLW_LPS"; _extendedParams["PLM_PIPE_SIZE"] = "PLM_PPE_SZ_MM";
            _extendedParams["PLM_VELOCITY"] = "PLM_VEL_MPS"; _extendedParams["PLM_FLOW_RATE"] = "PLM_FLOW_RATE_LPS";
            _extendedParams["PLM_PIPE_LENGTH"] = "PLM_PPE_LENGTH_M";
            // Phase 178b plumbing engine constants — fixture units, sizing, vent, trap, backflow,
            // RWH/SuDS, PRV/TMV, valve metadata, materials, healthcare flags, project standards toggle.
            _extendedParams["PLM_DFU_COUNT"]    = "PLM_DFU_COUNT_INT";
            _extendedParams["PLM_WSFU_COUNT"]   = "PLM_WSFU_COUNT_INT";
            _extendedParams["PLM_TRAP_TYPE"]    = "PLM_TRAP_TYPE_TXT";
            _extendedParams["PLM_TRAP_SEAL"]    = "PLM_TRAP_SEAL_DEPTH_MM";
            _extendedParams["PLM_VENT_DN"]      = "PLM_VENT_SIZE_DN";
            _extendedParams["PLM_AAV_REQ"]      = "PLM_AAV_REQUIRED_BOOL";
            _extendedParams["PLM_CALC_DN"]      = "PLM_CALC_DN_MM";
            _extendedParams["PLM_CALC_SLOPE"]   = "PLM_CALC_SLOPE_PCT";
            _extendedParams["PLM_DEMAND_FLOW"]  = "PLM_DEMAND_FLOW_LPS";
            _extendedParams["PLM_VEL"]          = "PLM_VELOCITY_MPS";
            _extendedParams["PLM_FRICTION"]     = "PLM_FRICTION_LOSS_PA_M";
            _extendedParams["PLM_PTEST"]        = "PLM_PRESSURE_TEST_KPA";
            _extendedParams["PLM_FLUID_CAT"]    = "PLM_FLUID_CATEGORY_TXT";
            _extendedParams["PLM_BF_TYPE"]      = "PLM_VLV_BACKFLOW_TYPE_TXT";
            _extendedParams["PLM_PPE_STD"]      = "PLM_PPE_STANDARD_TXT";
            _extendedParams["PLM_PPE_SCH"]      = "PLM_PPE_SCHEDULE_TXT";
            _extendedParams["PLM_PPE_GRADE"]    = "PLM_PPE_GRADE_TXT";
            _extendedParams["PLM_PPE_WRAS"]     = "PLM_PPE_WRAS_APPROVED_BOOL";
            _extendedParams["PLM_PPE_COLOR"]    = "PLM_PPE_COLOR_BS1710_TXT";
            _extendedParams["PLM_PPE_WALL_THK"] = "PLM_PPE_WALL_THK_MM";
            _extendedParams["PLM_MAT"]          = "PLM_MAT_TXT";
            _extendedParams["PLM_RWH_AREA"]     = "PLM_RWH_ROOF_AREA_M2";
            _extendedParams["PLM_RWH_TANK"]     = "PLM_RWH_TANK_VOL_M3";
            _extendedParams["PLM_RWH_YIELD"]    = "PLM_RWH_ANNUAL_YIELD_M3";
            _extendedParams["PLM_SUDS_VOL"]     = "PLM_SUDS_ATTEN_VOL_M3";
            _extendedParams["PLM_SEPTIC_VOL"]   = "PLM_SEPTIC_TANK_VOL_L";
            _extendedParams["PLM_PRV_SET"]      = "PLM_PRV_SET_PRESSURE_KPA";
            _extendedParams["PLM_PRV_INLET"]    = "PLM_PRV_INLET_PRESSURE_KPA";
            _extendedParams["PLM_PRESSURE_ZONE"]= "PLM_PRESSURE_ZONE_TXT";
            _extendedParams["PLM_TMV_CLASS"]    = "PLM_TMV_CLASS_TXT";
            _extendedParams["PLM_TMV_BLEND"]    = "PLM_TMV_BLEND_TEMP_C";
            _extendedParams["PLM_VLV_SET_P"]    = "PLM_VLV_SET_PRESSURE_BAR";
            _extendedParams["PLM_VLV_FLOW"]     = "PLM_VLV_DESIGN_FLOW_LS";
            _extendedParams["PLM_VLV_DP"]       = "PLM_VLV_DESIGN_DP_KPA";
            _extendedParams["PLM_VLV_FAIL"]     = "PLM_VLV_FAIL_POSITION_TXT";
            _extendedParams["PLM_VLV_WRAS"]     = "PLM_VLV_WRAS_APPROVED_BOOL";
            _extendedParams["PLM_DEAD_LEG_M"]   = "PLM_DEAD_LEG_LENGTH_M";
            _extendedParams["PLM_AUG_CARE"]     = "PLM_AUG_CARE_BOOL";
            _extendedParams["PLM_RO_LOOP"]      = "PLM_RO_LOOP_BOOL";
            _extendedParams["PLM_POU_FILTER"]   = "PLM_POU_FILTER_BOOL";
            _extendedParams["PLM_SENTINEL"]     = "PLM_SENTINEL_BOOL";
            _extendedParams["PRJ_PLUMBING_CODE"]= "PLM_PRJ_PLUMBING_CODE_TXT";
            // Phase 179a — plumbing enhancement: drainage / supply / system params.
            _extendedParams["PLM_DRN_DU"]       = "PLM_DRN_DU_NR";
            _extendedParams["PLM_DRN_DN_REQ"]   = "PLM_DRN_DN_REQ_MM";
            _extendedParams["PLM_DRN_QWW"]      = "PLM_DRN_QWW_LPS";
            _extendedParams["PLM_DRN_HD_RATIO"] = "PLM_DRN_HD_RATIO_NR";
            _extendedParams["PLM_DRN_INV_US"]   = "PLM_DRN_INV_US_M";
            _extendedParams["PLM_DRN_INV_DS"]   = "PLM_DRN_INV_DS_M";
            _extendedParams["PLM_DRN_COVER_US"] = "PLM_DRN_COVER_US_M";
            _extendedParams["PLM_DRN_COVER_DS"] = "PLM_DRN_COVER_DS_M";
            _extendedParams["PLM_HAS_TRAP"]     = "PLM_HAS_TRAP_BOOL";
            _extendedParams["PLM_TRAP_ARM"]     = "PLM_TRAP_ARM_M";
            _extendedParams["PLM_VENT_TYPE"]    = "PLM_VENT_TYPE_TXT";
            _extendedParams["PLM_SUP_LU_CW"]    = "PLM_SUP_LU_CW_NR";
            _extendedParams["PLM_SUP_LU_HW"]    = "PLM_SUP_LU_HW_NR";
            _extendedParams["PLM_SUP_WSFU"]     = "PLM_SUP_WSFU_NR";
            _extendedParams["PLM_SUP_QD"]       = "PLM_SUP_QD_LPS";
            _extendedParams["PLM_SUP_DN_REQ"]   = "PLM_SUP_DN_REQ_MM";
            _extendedParams["PLM_SUP_PRES"]     = "PLM_SUP_PRES_BAR";
            _extendedParams["PLM_SUP_VEL"]      = "PLM_SUP_VEL_MPS";
            _extendedParams["PLM_SUP_DP"]       = "PLM_SUP_DP_PAM";
            _extendedParams["PLM_DRV_PRESET"]   = "PLM_DRV_PRESET_KPA";
            _extendedParams["PLM_EXPVSL_SZ"]    = "PLM_EXPVSL_SZ_L";
            _extendedParams["PLM_PRV_SET_BAR"]  = "PLM_PRV_SET_BAR_NR";
            _extendedParams["PLM_MAT_DCW"]      = "PLM_MAT_DCW_TXT";
            _extendedParams["PLM_MAT_DHW"]      = "PLM_MAT_DHW_TXT";
            _extendedParams["PLM_MAT_DRN"]      = "PLM_MAT_DRN_TXT";
            _extendedParams["PLM_MAT_VNT"]      = "PLM_MAT_VNT_TXT";
            _extendedParams["PLM_BLDG_TYPE"]    = "PLM_BLDG_TYPE_TXT";
            _extendedParams["PLM_K_FACTOR"]     = "PLM_K_FACTOR_NR";
            _extendedParams["PLM_STD_DRAIN"]    = "PLM_STD_DRAIN_TXT";
            _extendedParams["PLM_STD_SUPPLY"]   = "PLM_STD_SUPPLY_TXT";
            _extendedParams["PLM_AUDIT_DATE"]   = "PLM_AUDIT_DATE_TXT";
            // Phase 179d — pump, TMV, vent, spool, real-time sizer
            _extendedParams["PLM_PUMP_DUTY_HEAD_M"]   = "PLM_PUMP_DUTY_HEAD_M";
            _extendedParams["PLM_PUMP_DUTY_FLOW_LPS"] = "PLM_PUMP_DUTY_FLOW_LPS";
            _extendedParams["PLM_PUMP_MODEL"]         = "PLM_PUMP_MODEL_TXT";
            _extendedParams["PLM_PUMP_EFF_PCT"]       = "PLM_PUMP_EFF_PCT";
            _extendedParams["PLM_TMV_INLET_HOT_C"]    = "PLM_TMV_INLET_HOT_C";
            _extendedParams["PLM_TMV_INLET_COLD_C"]   = "PLM_TMV_INLET_COLD_C";
            _extendedParams["PLM_TMV_MEASURED_C"]     = "PLM_TMV_MEASURED_C";
            _extendedParams["PLM_TMV_TEST_DATE"]      = "PLM_TMV_TEST_DATE_TXT";
            _extendedParams["PLM_TMV_NEXT_TEST"]      = "PLM_TMV_NEXT_TEST_TXT";
            _extendedParams["PLM_TMV_OVERDUE"]        = "PLM_TMV_OVERDUE_BOOL";
            _extendedParams["PLM_VENT_PIPE_ID"]       = "PLM_VENT_PIPE_ID_TXT";
            _extendedParams["PLM_PIPE_REAL_SIZE"]     = "PLM_PIPE_REAL_SIZE_BOOL";
            _extendedParams["PLM_PRESSURE_KPA"]       = "PLM_PRESSURE_KPA";
            _extendedParams["PLM_SPOOL_NR"]           = "PLM_SPOOL_NR_TXT";
            _extendedParams["PLM_NETWORK_NODE_TYPE"]  = "PLM_NETWORK_NODE_TYPE_TXT";
            // Volume, length, head heights, function
            _extendedParams["ELE_VOLUME"] = "BLE_ELE_VOLUME_CU_M"; _extendedParams["ELE_LENGTH"] = "BLE_ELE_LENGTH_M";
            _extendedParams["DOOR_HEAD_HT"] = "BLE_DOOR_HEAD_HEIGHT_MM"; _extendedParams["DOOR_FUNC"] = "BLE_DOOR_FUNCTION_TXT";
            _extendedParams["WINDOW_HEAD_HT"] = "BLE_WINDOW_HEAD_HEIGHT_MM";
            // Room finishes
            _extendedParams["ROOM_FINISH_FLR"] = "BLE_ROOM_FINISH_FLOOR_TXT";
            _extendedParams["ROOM_FINISH_WALL"] = "BLE_ROOM_FINISH_WALL_TXT";
            _extendedParams["ROOM_FINISH_CLG"] = "BLE_ROOM_FINISH_CEILING_TXT";
            _extendedParams["ROOM_FINISH_BASE"] = "BLE_ROOM_FINISH_BASE_TXT";
            // Duct dimensions
            _extendedParams["HVC_DUCT_WIDTH"] = "HVC_DCT_WIDTH_MM"; _extendedParams["HVC_DUCT_HEIGHT"] = "HVC_DCT_HEIGHT_MM";
            _extendedParams["HVC_INSULATION"] = "HVC_INS_THICKNESS_MM"; _extendedParams["HVC_DUCT_LENGTH"] = "HVC_DCT_LENGTH_M";
            // ISO 19650 naming
            _extendedParams["PROJECT_COD"] = "ASS_PROJECT_COD_TXT"; _extendedParams["ORIGINATOR_COD"] = "ASS_ORIGINATOR_COD_TXT";
            _extendedParams["VOLUME_COD"] = "ASS_VOLUME_COD_TXT"; _extendedParams["STATUS_COD"] = "ASS_CDE_SUITABILITY_TXT";
            _extendedParams["REV_COD"] = "ASS_REV_COD_TXT";
            // Paragraph containers
            _extendedParams["PARA_WALL"] = "ARCH_TAG_7_PARA_WALL_TXT"; _extendedParams["PARA_FLOOR"] = "ARCH_TAG_7_PARA_FLOOR_TXT";
            _extendedParams["PARA_CEIL"] = "ARCH_TAG_7_PARA_CEIL_TXT"; _extendedParams["PARA_ROOF"] = "ARCH_TAG_7_PARA_ROOF_TXT";
            _extendedParams["PARA_DOOR"] = "ARCH_TAG_7_PARA_DOOR_TXT"; _extendedParams["PARA_WIN"] = "ARCH_TAG_7_PARA_WIN_TXT";
            _extendedParams["PARA_STAIR"] = "ARCH_TAG_7_PARA_STAIR_TXT"; _extendedParams["PARA_RAMP"] = "ARCH_TAG_7_PARA_RAMP_TXT";
            _extendedParams["PARA_ROOM"] = "ARCH_TAG_7_PARA_ROOM_TXT"; _extendedParams["PARA_FACADE"] = "ARCH_TAG_7_PARA_FACADE_TXT";
            _extendedParams["PARA_CASEWORK"] = "ARCH_TAG_7_PARA_CASEWORK_TXT"; _extendedParams["PARA_FURNITURE"] = "ARCH_TAG_7_PARA_FURNITURE_TXT";
            _extendedParams["PARA_STR_FDN"] = "STR_TAG_7_PARA_FDN_TXT"; _extendedParams["PARA_STR_COL"] = "STR_TAG_7_PARA_COL_TXT";
            _extendedParams["PARA_STR_BEAM"] = "STR_TAG_7_PARA_BEAM_TXT";
            _extendedParams["PARA_HVC_SPEC"] = "HVC_TAG_7_PARA_SPEC_TXT"; _extendedParams["PARA_HVC_DUCT"] = "HVC_TAG_7_PARA_DUCT_TXT";
            _extendedParams["PARA_HVC_AT"] = "HVC_TAG_7_PARA_AT_TXT";
            // MEP paragraph containers
            _extendedParams["PARA_ELC_PANEL"] = "ELC_TAG_7_PARA_PANEL_TXT"; _extendedParams["PARA_ELC_CIRCUIT"] = "ELC_TAG_7_PARA_CIRCUIT_TXT";
            _extendedParams["PARA_LTG_SPEC"] = "LTG_TAG_7_PARA_SPEC_TXT";
            _extendedParams["PARA_PLM_FIXTURE"] = "PLM_TAG_7_PARA_FIXTURE_TXT"; _extendedParams["PARA_PLM_PIPE"] = "PLM_TAG_7_PARA_PIPE_TXT";
            _extendedParams["PARA_FLS_FA"] = "FLS_TAG_7_PARA_FA_TXT"; _extendedParams["PARA_FLS_SPR"] = "FLS_TAG_7_PARA_SPR_TXT";
            _extendedParams["PARA_COM_BMS"] = "COM_TAG_7_PARA_BMS_TXT";
            // Extended paragraph containers (v4.3)
            _extendedParams["PARA_HVC_FLEXDUCT"] = "HVC_TAG_7_PARA_FLEXDUCT_TXT"; _extendedParams["PARA_HVC_DCTACC"] = "HVC_TAG_7_PARA_DCTACC_TXT";
            _extendedParams["PARA_ELC_CONDUIT"] = "ELC_TAG_7_PARA_CONDUIT_TXT"; _extendedParams["PARA_ELC_TRAY"] = "ELC_TAG_7_PARA_TRAY_TXT";
            _extendedParams["PARA_ELC_CABLE"] = "ELC_TAG_7_PARA_CABLE_TXT";
            _extendedParams["PARA_PLM_EQUIP"] = "PLM_TAG_7_PARA_EQUIP_TXT"; _extendedParams["PARA_PLM_PIPEACC"] = "PLM_TAG_7_PARA_PIPEACC_TXT";
            _extendedParams["PARA_PLM_DRAIN"] = "PLM_TAG_7_PARA_DRAIN_TXT";
            _extendedParams["PARA_ICT_DATA"] = "ICT_TAG_7_PARA_DATA_TXT"; _extendedParams["PARA_NCL"] = "NCL_TAG_7_PARA_TXT";
            _extendedParams["PARA_SEC"] = "SEC_TAG_7_PARA_TXT"; _extendedParams["PARA_ASS_EQUIP"] = "ASS_TAG_7_PARA_EQUIP_TXT";
            _extendedParams["PARA_RGL_CMPL"] = "RGL_TAG_7_PARA_CMPL_TXT"; _extendedParams["PARA_PER_ENV"] = "PER_TAG_7_PARA_ENV_TXT";
            _extendedParams["PARA_CST_CONC"] = "CST_TAG_7_PARA_CONC_TXT";
            // v5.0 paragraph containers (new categories)
            _extendedParams["PARA_FURN_SYS"] = "ARCH_TAG_7_PARA_FURN_SYS_TXT";
            _extendedParams["PARA_PARKING"] = "ARCH_TAG_7_PARA_PARKING_TXT";
            _extendedParams["PARA_SITE"] = "ARCH_TAG_7_PARA_SITE_TXT";
            _extendedParams["PARA_TEL"] = "ICT_TAG_7_PARA_TEL_TXT";
            _extendedParams["PARA_AV_DEV"] = "ICT_TAG_7_PARA_AV_DEV_TXT";
            _extendedParams["PARA_MED_EQUIP"] = "MED_TAG_7_PARA_MED_EQUIP_TXT";
            _extendedParams["PARA_FIRE_PROT"] = "FLS_TAG_7_PARA_FIRE_PROT_TXT";
            _extendedParams["PARA_MECH_CTRL"] = "HVC_TAG_7_PARA_MECH_CTRL_DEV_TXT";
            _extendedParams["PARA_MECH_SETS"] = "HVC_TAG_7_PARA_MECH_EQUIP_SETS_TXT";
            _extendedParams["PARA_DUCT_INS"] = "HVC_TAG_7_PARA_DUCT_INSULATION_TXT";
            _extendedParams["PARA_DUCT_LINING"] = "HVC_TAG_7_PARA_DUCT_LINING_TXT";
            _extendedParams["PARA_PIPE_INS"] = "PLM_TAG_7_PARA_PIPE_INSULATION_TXT";
            _extendedParams["PARA_PLM_EQUIP2"] = "PLM_TAG_7_PARA_PLM_EQUIP_TXT";
            _extendedParams["PARA_ELC_CONN"] = "ELC_TAG_7_PARA_ELC_CONNECTORS_TXT";
            _extendedParams["PARA_FAB_CONT"] = "ELC_TAG_7_PARA_FAB_CONTAINMENT_TXT";
            _extendedParams["PARA_FAB_DUCT"] = "HVC_TAG_7_PARA_FAB_DUCTWORK_TXT";
            _extendedParams["PARA_FAB_STIFF"] = "HVC_TAG_7_PARA_FAB_DCT_STIFFENERS_TXT";
            _extendedParams["PARA_FAB_HANG"] = "MEP_TAG_7_PARA_FAB_HANGERS_TXT";
            _extendedParams["PARA_FAB_PIPE"] = "PLM_TAG_7_PARA_FAB_PIPEWORK_TXT";
            _extendedParams["PARA_ANCILLARY"] = "MEP_TAG_7_PARA_ANCILLARY_TXT";
            _extendedParams["PARA_MATERIALS"] = "PROP_TAG_7_PARA_MATERIALS_TXT";
            _extendedParams["PARA_CURTAIN_PNL"] = "BLE_TAG_7_PARA_CURTAIN_PANELS_TXT";
            _extendedParams["PARA_CURTAIN_MUL"] = "BLE_TAG_7_PARA_CURTAIN_MULLIONS_TXT";
            _extendedParams["PARA_WALL_SWEEP"] = "BLE_TAG_7_PARA_WALL_SWEEPS_TXT";
            _extendedParams["PARA_SLAB_EDGE"] = "BLE_TAG_7_PARA_SLAB_EDGES_TXT";
            _extendedParams["PARA_ROOF_SOFFIT"] = "BLE_TAG_7_PARA_ROOF_SOFFITS_TXT";
            _extendedParams["PARA_FASCIA"] = "BLE_TAG_7_PARA_FASCIA_TXT";
            _extendedParams["PARA_GUTTER"] = "BLE_TAG_7_PARA_GUTTER_TXT";
            _extendedParams["PARA_HANDRAILS"] = "BLE_TAG_7_PARA_HANDRAILS_TXT";
            _extendedParams["PARA_RAILINGS"] = "BLE_TAG_7_PARA_RAILINGS_TXT";
            _extendedParams["PARA_TOP_RAILS"] = "BLE_TAG_7_PARA_TOP_RAILS_TXT";
            _extendedParams["PARA_STAIR_RUNS"] = "BLE_TAG_7_PARA_STAIR_RUNS_TXT";
            _extendedParams["PARA_STAIR_LAND"] = "BLE_TAG_7_PARA_STAIR_LANDINGS_TXT";
            _extendedParams["PARA_STAIR_SUPP"] = "BLE_TAG_7_PARA_STAIR_SUPPORTS_TXT";
            // Warning thresholds
            _extendedParams["WARN_RAMP_SLOPE"] = "WARN_BLE_RAMP_SLOPE_PCT_RAMPS";
            _extendedParams["WARN_VLT_DROP"] = "WARN_ELC_VLT_DROP_PCT_ELECTRICAL_EQUI";
            _extendedParams["WARN_SPR_COVER"] = "WARN_FLS_SFTY_COVERAGE_AREA_SQ_M_SPRINKLERS__FIR";
            _extendedParams["WARN_NOISE"] = "WARN_HVC_DCT_SOUNDLVL_DB";
            _extendedParams["WARN_COP_EER"] = "WARN_HVC_EFF_RATIO_NR_MECHANICAL_EQUI";
            _extendedParams["WARN_FLEX_VEL"] = "WARN_HVC_VEL_MPS_FLEX_DUCTS";
            _extendedParams["WARN_CARBON"] = "WARN_PER_SUST_CARBON_FOOTPRINT_KG_WALLS__FLOORS__";
            _extendedParams["WARN_UVAL_FLR"] = "WARN_PER_THERM_U_VALUE_W_M2K_NR_FLOORS";
            _extendedParams["WARN_UVAL_ROOF"] = "WARN_PER_THERM_U_VALUE_W_M2K_NR_ROOFS";
            _extendedParams["WARN_UVAL_WALL"] = "WARN_PER_THERM_U_VALUE_W_M2K_NR_WALLS";
            _extendedParams["WARN_HW_FLOW"] = "WARN_PLM_PPE_FLW_LPS_PIPES__HOT_WATE";
            _extendedParams["WARN_ACCESS_WIDTH"] = "WARN_RGL_ACCESS_CLEAR_WIDTH_MM_DOORS__RAMPS__C";
            // ISO 19650 project-level naming (PRJ_ prefix variants)
            _extendedParams["PRJ_PROJECT_COD"] = "PRJ_PROJECT_COD_TXT"; _extendedParams["PRJ_ORIGINATOR_COD"] = "PRJ_ORG_ORIGINATOR_CODE_TXT";
            _extendedParams["PRJ_VOLUME_COD"] = "PRJ_VOLUME_CODE"; _extendedParams["PRJ_STATUS_COD"] = "PRJ_STATUS_COD_TXT";
            _extendedParams["PRJ_REV_COD"] = "PRJ_REV_COD_TXT";
            // COBie / warranty / commissioning / asset management
            _extendedParams["BARCODE"] = "ASS_BARCODE_TXT"; _extendedParams["ASSET_ID"] = "ASS_ASSET_ID_TXT";
            _extendedParams["CONDITION"] = "ASS_CONDITION_TXT"; _extendedParams["WARRANTY_START"] = "COM_WARRANTY_START_TXT";
            _extendedParams["WARR_GUAR_PARTS"] = "ASS_WARRANTY_PARTS_TXT"; _extendedParams["WARR_DUR_PARTS"] = "ASS_WARRANTY_DURATION_PARTS_YRS";
            _extendedParams["WARR_GUAR_LABOR"] = "ASS_WARRANTY_LABOR_TXT"; _extendedParams["WARR_DUR_LABOR"] = "ASS_WARRANTY_DURATION_LABOR_YRS";
            _extendedParams["WARR_DUR_UNIT"] = "ASS_WARRANTY_DUR_UNIT_TXT"; _extendedParams["MODEL_REF"] = "ASS_MODEL_REF_TXT";

            ContainerGroups = Array.Empty<ContainerGroupDef>();
            UniversalParams = new[]
            {
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
                "ASS_TAG_1_TXT", "ASS_TAG_2_TXT", "ASS_TAG_3_TXT",
                "ASS_TAG_4_TXT", "ASS_TAG_5_TXT", "ASS_TAG_6_TXT",
                "ASS_STATUS_TXT", "ASS_INST_DETAIL_NUM_TXT", "MNT_TYPE_TXT",
                "ASS_TAG_SCHEME_TXT", "ASS_LOD_VERIFIED_TXT",
                "CSI_SECTION_TXT", "CSI_TITLE_TXT",
            };

            // CRASH FIX: Initialize GUID maps from SourceTokens when JSON is missing.
            // Without this, _guidByName stays null → AllParamGuids returns empty dict →
            // all GUID lookups fail → compliance scan fails → commands that check GUIDs crash.
            _guidByName = new Dictionary<string, Guid>(StringComparer.Ordinal);
            _nameByGuid = new Dictionary<Guid, string>();
            foreach (var tok in SourceTokens)
            {
                if (Guid.TryParse(tok.GuidStr, out Guid g))
                {
                    _guidByName[tok.ParamName] = g;
                    _nameByGuid[g] = tok.ParamName;
                }
            }

            // CRASH FIX: Initialize CategoryEnumMap with all taggable categories
            // plus the tag-family aliases produced by TagFamilyCreatorCommand
            // (count: TagFamilyConfig.TotalFamilyCount at runtime).
            // Without this, ResolveUniversalCategoryEnums() returns empty array →
            // AllCategoryEnums = empty → BuildCategorySet = empty → 0 params bound →
            // LoadSharedParamsCommand silently does nothing, leaving project unconfigured.
            // The Tag Categories sub-tab in the dockable panel reads from this map.
            CategoryEnumMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Air Terminals", "OST_DuctTerminal" },
                { "Analytical Duct Segments", "OST_AnalyticalDuctSegments" },
                { "Analytical Links", "OST_AnalyticalLinks" },
                { "Analytical Members", "OST_AnalyticalMember" },
                { "Analytical Nodes", "OST_AnalyticalNodes" },
                { "Analytical Openings", "OST_AnalyticalOpenings" },
                { "Analytical Panels", "OST_AnalyticalPanels" },
                { "Analytical Pipe Segments", "OST_AnalyticalPipeSegments" },
                { "Area Based Loads", "OST_AreaLoads" },
                { "Area Loads", "OST_AreaLoads" },
                { "Areas", "OST_Areas" },
                { "Assemblies", "OST_Assemblies" },
                { "Audio Visual Devices", "OST_AudioVisualDevices" },
                { "Boundary Conditions", "OST_BoundaryConditions" },
                { "Cable Tray Fittings", "OST_CableTrayFitting" },
                { "Cable Tray Runs", "OST_CableTrayRun" },
                { "Cable Trays", "OST_CableTray" },
                { "Casework", "OST_Casework" },
                { "Ceilings", "OST_Ceilings" },
                { "Columns", "OST_Columns" },
                { "Communication Devices", "OST_CommunicationDevices" },
                { "Conduit Fittings", "OST_ConduitFitting" },
                { "Conduit Runs", "OST_ConduitRun" },
                { "Conduits", "OST_Conduit" },
                { "Coordination Model", "OST_CoordinationModel" },
                { "Curtain Panels", "OST_CurtainWallPanels" },
                { "Curtain Wall Mullions", "OST_CurtainWallMullions" },
                { "Curtain Systems", "OST_Curtain_Systems" },
                { "Data Devices", "OST_DataDevices" },
                { "Detail Items", "OST_DetailComponents" },
                { "Doors", "OST_Doors" },
                { "Duct Accessories", "OST_DuctAccessory" },
                { "Duct Fittings", "OST_DuctFitting" },
                { "Duct Insulation", "OST_DuctInsulations" },
                { "Duct Lining", "OST_DuctLinings" },
                { "Duct Placeholders", "OST_PlaceHolderDucts" },
                { "Ducts", "OST_DuctCurves" },
                { "Electrical Circuits", "OST_ElectricalCircuit" },
                { "Electrical Connectors", "OST_ElectricalConnectors" },
                { "Electrical Equipment", "OST_ElectricalEquipment" },
                { "Electrical Spare/Space Circuits", "OST_ElectricalInternalCircuits" },
                { "Electrical Fixtures", "OST_ElectricalFixtures" },
                { "Entourage", "OST_Entourage" },
                { "Fascia", "OST_Fascia" },
                { "Fire Alarm Devices", "OST_FireAlarmDevices" },
                { "Fire Protection", "OST_FireProtection" },
                { "Flex Ducts", "OST_FlexDuctCurves" },
                { "Flex Pipes", "OST_FlexPipeCurves" },
                { "Floors", "OST_Floors" },
                { "Food Service Equipment", "OST_FoodServiceEquipment" },
                { "Furniture", "OST_Furniture" },
                { "Furniture Systems", "OST_FurnitureSystems" },
                { "Generic Models", "OST_GenericModel" },
                { "Gutter", "OST_Gutter" },
                { "HVAC Zones", "OST_HVAC_Zones" },
                { "Handrails", "OST_StairsRailingHandRail" },
                { "Hardscape", "OST_Hardscape" },
                { "Internal Area Loads", "OST_InternalAreaLoads" },
                { "Internal Line Loads", "OST_InternalLineLoads" },
                { "Internal Point Loads", "OST_InternalPointLoads" },
                { "Lighting Devices", "OST_LightingDevices" },
                { "Lighting Fixtures", "OST_LightingFixtures" },
                { "Line Loads", "OST_LineLoads" },
                { "MEP Ancillary", "OST_MechanicalEquipment" },
                { "MEP Fabrication Containment", "OST_FabricationContainment" },
                { "MEP Fabrication Ductwork", "OST_FabricationDuctwork" },
                { "MEP Fabrication Ductwork Stiffeners", "OST_FabricationDuctworkStiffeners" },
                { "MEP Fabrication Hangers", "OST_FabricationHangers" },
                { "MEP Fabrication Pipework", "OST_FabricationPipework" },
                { "Mass", "OST_Mass" },
                // NOTE: OST_Materials intentionally EXCLUDED — materials use native Revit
                // properties (Color, Transparency, ThermalAsset, StructuralAsset) set via
                // MaterialCommands.cs, NOT shared parameter bindings.
                { "Mechanical Control Devices", "OST_MechanicalControlDevices" },
                { "Mechanical Equipment", "OST_MechanicalEquipment" },
                { "Mechanical Equipment Sets", "OST_MechanicalEquipmentSets" },
                { "Medical Equipment", "OST_MedicalEquipment" },
                { "Model Groups", "OST_IOSModelGroups" },
                { "Nurse Call Devices", "OST_NurseCallDevices" },
                { "Pads", "OST_BuildingPad" },
                { "Parking", "OST_Parking" },
                { "Parts", "OST_Parts" },
                { "Pipe Accessories", "OST_PipeAccessory" },
                { "Pipe Fittings", "OST_PipeFitting" },
                { "Pipe Insulation", "OST_PipeInsulations" },
                { "Pipe Placeholders", "OST_PlaceHolderPipes" },
                { "Pipes", "OST_PipeCurves" },
                { "Piping Systems", "OST_PipingSystem" },
                { "Planting", "OST_Planting" },
                { "Plumbing Equipment", "OST_PlumbingEquipment" },
                { "Plumbing Fixtures", "OST_PlumbingFixtures" },
                { "Point Loads", "OST_PointLoads" },
                { "Profiles", "OST_ProfileFamilies" },
                { "Property Line Segments", "OST_SitePropertyLineSegment" },
                { "Property Lines", "OST_SiteProperty" },
                { "RVT Links", "OST_RvtLinks" },
                { "Railings", "OST_StairsRailing" },
                { "Ramps", "OST_Ramps" },
                { "Revision Clouds", "OST_RevisionClouds" },
                { "Roads", "OST_Roads" },
                { "Roof Soffits", "OST_RoofSoffit" },
                { "Roofs", "OST_Roofs" },
                { "Rooms", "OST_Rooms" },
                { "Shaft Openings", "OST_ShaftOpening" },
                { "Security Devices", "OST_SecurityDevices" },
                { "Signage", "OST_Signage" },
                { "Site", "OST_Site" },
                { "Slab Edges", "OST_EdgeSlab" },
                { "Spaces", "OST_MEPSpaces" },
                { "Specialty Equipment", "OST_SpecialityEquipment" },
                { "Sprinklers", "OST_Sprinklers" },
                { "Stair Landings", "OST_StairsLandings" },
                { "Stair Runs", "OST_StairsRuns" },
                { "Stair Supports", "OST_StairsSupports" },
                { "Stairs", "OST_Stairs" },
                { "Structural Area Reinforcement", "OST_AreaRein" },
                { "Structural Beam Systems", "OST_StructuralFramingSystem" },
                { "Structural Columns", "OST_StructuralColumns" },
                { "Structural Connections", "OST_StructConnections" },
                { "Structural Fabric Reinforcement", "OST_FabricReinforcement" },
                { "Structural Foundations", "OST_StructuralFoundation" },
                { "Structural Framing", "OST_StructuralFraming" },
                { "Structural Path Reinforcement", "OST_PathRein" },
                { "Structural Rebar", "OST_Rebar" },
                { "Structural Load Cases", "OST_LoadCases" },
                { "Structural Rebar Couplers", "OST_RebarCoupler" },
                { "Structural Stiffeners", "OST_StructuralStiffener" },
                { "Structural Trusses", "OST_StructuralTruss" },
                { "Telephone Devices", "OST_TelephoneDevices" },
                { "Temporary Structures", "OST_TemporaryStructure" },
                { "Top Rails", "OST_RailingTopRail" },
                { "Toposolid", "OST_Toposolid" },
                { "Toposolid Links", "OST_Toposolid" },
                { "Vertical Circulation", "OST_VerticalCirculation" },
                { "Vibration Dampers", "OST_VibrationDampers" },
                { "Vibration Isolators", "OST_VibrationIsolators" },
                { "Vibration Management", "OST_VibrationManagement" },
                { "Wall Sweeps", "OST_WallSweeps" },
                { "Walls", "OST_Walls" },
                { "Wash", "OST_Planting" },
                { "Windows", "OST_Windows" },
                { "Wire", "OST_Wire" },
                { "Zones", "OST_Zones" },

                // ════════════════════════════════════════════════════════════════
                // TAG-FAMILY-CREATOR ALIGNMENT (Phase 78 follow-up)
                //
                // The CreateTagFamilies command in Tags/TagFamilyCreatorCommand.cs
                // creates 137 .rfa tag families: 121 unique BuiltInCategory bases
                // plus 16 variants (8 tie-in + 3 sheet + 4 structural + 1 MEP).
                //
                // The 13 base entries below cover BICs that the TagFamilyCreator
                // produces but were missing from the original 124-entry map. Some
                // are alias enums (Revit ships overlapping enum names for the same
                // category — e.g. OST_Cornices and OST_WallSweeps both resolve to
                // "Wall Sweeps"). Adding both keys is safe because BuildCategorySet
                // uses CategorySet.Insert which dedupes by Category.Id.
                //
                // The 16 variant display names share their BIC with an existing
                // base entry. They surface in the Tag Categories sub-tab so the
                // checkbox count matches the tag families a coordinator has
                // just created (TagFamilyConfig.TotalFamilyCount at runtime).
                //
                // CATEGORY_SKIP semantic note: skipping a variant entry (e.g.
                // "Floors (Structural)") via the runtime element-category filter
                // also skips its base ("Floors") because Revit reports the base
                // category at element level. Variants are presented for parity
                // with the tag-family list, not for independent runtime gating.
                // ════════════════════════════════════════════════════════════════

                // ── Missing base BICs created by TagFamilyCreator ─────────────
                { "Materials", "OST_Materials" },                       // Material Tag.rft (excluded from UniversalCategories below)
                { "Sheets", "OST_Sheets" },                             // Generic Tag.rft — base sheet document tag
                { "Structural Connection Bolts", "OST_StructConnectionBolts" },
                { "Structural Connection Welds", "OST_StructConnectionWelds" },

                // ── Alias enums (different BIC name, same Revit category) ─────
                // Both forms compile against current Revit API; exposing both
                // ensures Tags created with either spelling resolve correctly.
                { "Mechanical Equipment Set", "OST_MechanicalEquipmentSet" }, // alias of OST_MechanicalEquipmentSets
                { "Analytical Opening", "OST_AnalyticalOpening" },            // alias of OST_AnalyticalOpenings
                { "Analytical Panel", "OST_AnalyticalPanel" },                // alias of OST_AnalyticalPanels
                { "Rigid Links (Analytical)", "OST_RigidLinksAnalytical" },   // alias of OST_AnalyticalLinks
                { "Hand Rail", "OST_RailingHandRail" },                       // alias of OST_StairsRailingHandRail
                { "Railings (Std)", "OST_Railings" },                         // alias of OST_StairsRailing
                { "Rebar Coupler", "OST_Coupler" },                           // alias of OST_RebarCoupler
                { "Cornices", "OST_Cornices" },                               // alias of OST_WallSweeps
                { "Toposolid Link", "OST_ToposolidLink" },                    // distinct enum — was previously folded into OST_Toposolid

                // ── Tie-in point variant tag families (ISO 19650-3) ───────────
                { "Tie-In Point (Pipe)", "OST_PipeCurves" },
                { "Tie-In Point (Duct)", "OST_DuctCurves" },
                { "Tie-In Point (Conduit)", "OST_Conduit" },
                { "Tie-In Point (Cable Tray)", "OST_CableTray" },
                { "Tie-In Point (Fire Protection)", "OST_Sprinklers" },
                { "Tie-In Point (Gas)", "OST_GenericModel" },
                { "Tie-In Point (Fire Protection Pipe)", "OST_PipeCurves" },
                { "Tie-In Point (Gas Pipe)", "OST_PipeCurves" },

                // ── Discipline-specific sheet tag variants ────────────────────
                { "Sheets (Architectural)", "OST_Sheets" },
                { "Sheets (MEP)", "OST_Sheets" },
                { "Sheets (Structural)", "OST_Sheets" },

                // ── Structural variant tag families ───────────────────────────
                { "Floors (Structural)", "OST_Floors" },
                { "Walls (Structural/Load-bearing)", "OST_Walls" },
                { "Structural Framing (Bracing)", "OST_StructuralFraming" },
                { "Columns (Architectural)", "OST_Columns" },

                // ── MEP variant tag family ────────────────────────────────────
                { "MEP Sleeve (Fire-rated penetration)", "OST_GenericModel" },
            };

            // Set UniversalCategories to the full category list so
            // ResolveUniversalCategoryEnums returns all categories even without JSON.
            // CRITICAL: Exclude "Materials" and any "Materials"-suffix variant —
            // material-specific params are bound via BuildGroupCategoryOverrides()
            // in LoadSharedParamsCommand. Including Materials here would bind
            // ALL 2300+ parameters to OST_Materials, polluting every material's
            // custom properties panel in Revit.
            UniversalCategories = CategoryEnumMap.Keys
                .Where(k => !k.Equals("Materials", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            StingLog.Info($"LoadDefaults: {_guidByName.Count} GUIDs, {CategoryEnumMap.Count} categories (v5.0), {UniversalParams.Length} universal params");
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

            // Phase 165 follow-up — token-write hardening pass.
            // Two defensive measures applied here:
            //
            //  1. De-duplicate TokenIndices in-flight. If a config drift, hand-
            //     edit, or upstream merge introduces the same slot twice (e.g.
            //     [0,6,6,7]), the assembled string previously rendered the
            //     PROD value twice ("M-AHU-AHU-0001"). We now emit each slot
            //     at most once and preserve the original FIRST-OCCURRENCE
            //     order so legitimate non-duplicated configs are unchanged.
            //
            //  2. Skip empty token values rather than emitting an empty
            //     string. The previous code added '' to `parts` and let
            //     string.Join produce double separators ("M-BLD1--L02"
            //     when ZONE was empty). Stripping empties at this layer is
            //     consistent with the "any non-empty token writes" sanity
            //     in WriteContainers.

            // Phase 165 perf — replace the per-call HashSet<int> with an int
            // bitmask. TokenIndices slots are 0..7 (eight ISO 19650 source
            // tokens), so a single int holds the seen-set. Replaces a heap
            // allocation per AssembleContainer call (~30k allocations for a
            // 1000-element batch — AssembleContainer fires once per
            // container per element).
            // The parts list is recycled via a [ThreadStatic] buffer so the
            // List<string> backing array is reused across calls within the
            // same thread. Revit hosts plugins on a single thread, so this
            // is safe and eliminates another ~30k allocations per batch.
            int seen = 0;
            var parts = _assembleScratch ??= new List<string>(8);
            parts.Clear();
            bool anyValue = false;
            foreach (int idx in container.TokenIndices)
            {
                if (idx < 0 || idx > 31) continue;          // bitmask scope
                int bit = 1 << idx;
                if ((seen & bit) != 0) continue;            // dedupe
                seen |= bit;
                string val = idx < tokenValues.Length ? tokenValues[idx] : "";
                if (string.IsNullOrEmpty(val)) continue;    // strip empty slots
                parts.Add(val);
                anyValue = true;
            }
            if (!anyValue) return "";

            // Phase 165 follow-up — honour the literal escape form '\\n' /
            // '\\r\\n' (two-char) emitted by JSON authors who can't paste a
            // real newline into the file. Tag families render the resulting
            // characters as line breaks, fixing the "label breaks not
            // honoured" report. A real '\n' in the JSON value is left intact.
            string sep = ResolveSeparator(container.Separator);

            string assembled = string.Join(sep, parts);
            if (!string.IsNullOrEmpty(container.Prefix)) assembled = container.Prefix + assembled;
            if (!string.IsNullOrEmpty(container.Suffix)) assembled = assembled + container.Suffix;
            return assembled;
        }

        /// <summary>
        /// Phase 165 follow-up — turn an escaped-string separator from JSON
        /// into the real character(s) it represents. Recognised escapes:
        ///   '\\n'   → '\n'   (LF — single line break, Revit-native)
        ///   '\\r\\n'→ '\r\n' (CRLF — Windows newline, also accepted by Revit)
        ///   '\\t'   → '\t'   (rarely useful, but supported for symmetry)
        /// All other inputs (including real LF / CRLF already in the JSON
        /// value, dashes, pipes, etc.) pass through unchanged.
        /// </summary>
        private static string ResolveSeparator(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "-";
            if (raw == "\\n")    return "\n";
            if (raw == "\\r\\n") return "\r\n";
            if (raw == "\\t")    return "\t";
            return raw;
        }

        /// <summary>
        /// Read all 8 token values from an element into an array matching AllTokenParams order.
        /// Sanitises each value so malformed upstream writes (e.g. a PROD token
        /// accidentally set to "Plumbing FixturesRAINSHOWER Shower Set" or to a
        /// previously-assembled full tag) cannot propagate into TAG1 / TAG7 /
        /// discipline containers on subsequent pipeline runs.
        ///
        /// Sanitisation rules (applied per-slot, only touching obviously malformed data):
        ///   1. Strip any content after the configured separator — a token may
        ///      never contain the separator since that produces a double-join.
        ///   2. Trim whitespace; reject values that still contain inner whitespace
        ///      (real ISO 19650 codes are whitespace-free identifiers like DCW,
        ///      AHU, BLD1) — replaced with empty so BuildAndWriteTag re-derives.
        ///   3. Cap length at 40 chars (real codes are ≤ 8). Longer values are
        ///      treated as corruption and cleared.
        /// The first three occurrences per session are logged; further instances
        /// are counted but not spammed into the log.
        /// </summary>
        public static string[] ReadTokenValues(Element el)
        {
            EnsureLoaded();
            string[] values = new string[AllTokenParams.Length];
            for (int i = 0; i < AllTokenParams.Length; i++)
            {
                string raw = ParameterHelpers.GetString(el, AllTokenParams[i]);
                values[i] = SanitiseTokenValue(raw, AllTokenParams[i], el);
            }
            return values;
        }

        // PROD-CONCAT-FIX: session-scoped counters so a noisy batch doesn't drown
        // the log but operators can still see that sanitisation was needed.
        private static int _tokenSanitiseLogCount = 0;
        private static int _tokenSanitiseSuppressed = 0;
        private const int _tokenSanitiseLogCap = 3;

        /// <summary>
        /// PROD-CONCAT-FIX: Defensive token sanitiser. Returns a clean token value
        /// (possibly empty) whenever the stored parameter looks like a concatenation
        /// or a carry-over of an earlier full tag. Callers treat an empty string
        /// as "re-derive on next tag build", which is the safe behaviour.
        /// </summary>
        private static string SanitiseTokenValue(string raw, string paramName, Element el)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string v = raw;
            string sep = !string.IsNullOrEmpty(Separator) ? Separator : "-";

            // Rule 1: drop anything after the separator — a well-formed token
            // can never contain it.
            int sepIdx = v.IndexOf(sep, StringComparison.Ordinal);
            if (sepIdx >= 0)
            {
                LogTokenSanitise(paramName, raw, "contains separator", el);
                v = v.Substring(0, sepIdx);
            }

            // Rule 2: reject values with inner whitespace.
            string trimmed = v.Trim();
            if (trimmed.Length != v.Length) v = trimmed;
            if (!string.IsNullOrEmpty(v))
            {
                for (int j = 0; j < v.Length; j++)
                {
                    if (char.IsWhiteSpace(v[j]))
                    {
                        LogTokenSanitise(paramName, raw, "contains whitespace", el);
                        return "";
                    }
                }
            }

            // Rule 3: cap length. Real ISO 19650 codes are ≤ 8 chars;
            // 40 leaves head-room for unusual but legitimate values.
            if (v.Length > 40)
            {
                LogTokenSanitise(paramName, raw, $"length {v.Length} exceeds safe cap", el);
                return "";
            }

            return v;
        }

        private static void LogTokenSanitise(string paramName, string raw, string reason, Element el)
        {
            int n = System.Threading.Interlocked.Increment(ref _tokenSanitiseLogCount);
            if (n <= _tokenSanitiseLogCap)
            {
                string elRef = el != null ? $"element {el.Id.Value}" : "(no element)";
                StingLog.Warn($"Token sanitise: '{paramName}'='{raw}' on {elRef} — {reason}. Cleaning before reuse.");
            }
            else
            {
                System.Threading.Interlocked.Increment(ref _tokenSanitiseSuppressed);
                if (n == _tokenSanitiseLogCap + 1)
                    StingLog.Warn("Token sanitise: further occurrences suppressed this session (counter kept in ParamRegistry._tokenSanitiseSuppressed).");
            }
        }

        /// <summary>
        /// Write all applicable containers for an element based on its category.
        /// Returns count of containers written.
        /// TAG7 is always skipped here — it requires the narrative builder
        /// (TagConfig.BuildTag7Narrative) rather than simple token concatenation.
        /// </summary>
        // FUT-20: Discipline-to-container prefix mapping for selective writes.
        // Elements with DISC=M skip ELC_*, PLM_*, FLS_*, COM_*, etc. containers.
        private static readonly Dictionary<string, HashSet<string>> _discContainerPrefixes =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["M"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASS_", "HVC_", "MAT_" },
                ["E"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASS_", "ELC_", "ELE_", "LTG_", "MAT_" },
                ["P"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASS_", "PLM_", "MAT_" },
                ["A"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASS_", "MAT_" },
                ["S"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASS_", "STR_", "MAT_" },
                ["FP"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASS_", "FLS_", "MAT_" },
                ["LV"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASS_", "COM_", "SEC_", "NCL_", "ICT_", "MAT_" },
                ["G"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ASS_", "MAT_", "SLV_" },
            };

        /// <summary>FUT-20: Check if a container param is relevant for the given discipline.</summary>
        private static bool IsContainerRelevantForDisc(string paramName, string disc)
        {
            if (string.IsNullOrEmpty(disc) || !_discContainerPrefixes.TryGetValue(disc, out var prefixes))
                return true; // Unknown discipline — write all containers
            foreach (string prefix in prefixes)
            {
                if (paramName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>FUT-20 public accessor for ComplianceScan discipline filtering.</summary>
        public static bool IsContainerRelevantForDiscPublic(string paramName, string disc)
            => IsContainerRelevantForDisc(paramName, disc);

        public static int WriteContainers(Element el, string[] tokenValues, string categoryName,
            bool overwrite = true, string skipParam = null)
        {
            if (tokenValues == null || tokenValues.Length < 8) return 0;
            int written = 0;

            // EFF-15 (Phase 149c): re-tag fast path. Hash the 8 token values; if
            // the element's stored ASS_LAST_TOKEN_HASH_TXT matches, none of the
            // ~53 containers can have changed, so we can skip the entire write
            // sweep. First tag write produces a fresh hash and the writes
            // proceed normally. Massive win on re-tag passes (the dominant
            // case for daily users) — ~50× fewer SetString calls per element.
            //
            // Skipped when skipParam is set (caller is doing a partial /
            // single-container write) so we don't lie about behaviour.
            string newHash = ComputeTokenHash(tokenValues);
            if (string.IsNullOrEmpty(skipParam))
            {
                string priorHash = ParameterHelpers.GetString(el, "ASS_LAST_TOKEN_HASH_TXT");
                if (!string.IsNullOrEmpty(priorHash)
                    && string.Equals(priorHash, newHash, StringComparison.Ordinal))
                {
                    return 0; // tokens unchanged — every container would have written its current value
                }
            }

            // FUT-20: Get discipline code for selective container writes (60-80% fewer writes)
            string disc = tokenValues.Length > 0 ? tokenValues[0] : null;

            // ORPHAN-FIX: honour the Tokens & Depth sub-tab container checkboxes.
            // User can disable any group (ARCH / MEP / STR / GEN / discipline / TAG1..7).
            HashSet<string> allowedGroupCodes = LoadAllowedContainerGroups();

            var containers = ContainersForCategory(categoryName);

            // Phase 165 follow-up — write-once guard. ContainersForCategory
            // unions the universal-group containers (Categories == null) with
            // any category-specific group entries. If a future config defines
            // the same ParamName in both lists (e.g. ASS_TAG_2_TXT in
            // Universal AND in HVAC for one category), the loop below would
            // assemble + write that param twice — producing TAG7 narrative
            // duplication if the two definitions disagree on TokenIndices.
            // We dedupe on ParamName so each container writes exactly once
            // per element.
            // Phase 165 perf — recycle a per-thread HashSet so a 1000-element
            // batch doesn't allocate 1000 transient HashSets.
            var writtenParams = _writtenParamsScratch
                ??= new HashSet<string>(StringComparer.Ordinal);
            writtenParams.Clear();

            foreach (var c in containers)
            {
                if (c.ParamName == skipParam) continue;
                if (!writtenParams.Add(c.ParamName)) continue; // already wrote this param this pass
                // TAG7 + sub-sections use the narrative builder, not token concatenation
                if (IsTag7Param(c.ParamName)) continue;

                // FUT-20: Skip containers not relevant for this element's discipline
                if (!IsContainerRelevantForDisc(c.ParamName, disc)) continue;

                // ORPHAN-FIX: Skip containers whose group the user has de-selected.
                if (allowedGroupCodes != null && !IsContainerInAllowedGroup(c.ParamName, allowedGroupCodes))
                    continue;

                string assembled = AssembleContainer(c, tokenValues);
                if (!string.IsNullOrEmpty(assembled))
                {
                    if (ParameterHelpers.SetString(el, c.ParamName, assembled, overwrite))
                        written++;
                    else
                        StingLog.Warn($"WriteContainers: failed to write {c.ParamName} on element {el.Id.Value}");
                }
            }

            // EFF-15: stamp the new hash AFTER successful writes so a partial
            // failure (logged above) doesn't trick the next pass into skipping
            // containers that didn't actually get written. We only stamp on
            // the full-sweep path (skipParam null).
            if (string.IsNullOrEmpty(skipParam))
                ParameterHelpers.SetString(el, "ASS_LAST_TOKEN_HASH_TXT", newHash, overwrite: true);

            return written;
        }

        /// <summary>
        /// EFF-15 (Phase 149c): build a stable hash of the 8 ISO 19650 tokens
        /// for the container fast-path gate. djb2-style hash returned as
        /// 16-char hex — short enough to fit any TEXT param without bloat,
        /// long enough to make collisions vanishingly rare for the value
        /// space (alphanumeric tokens, ~10^15 distinct combinations).
        /// </summary>
        private static string ComputeTokenHash(string[] tokenValues)
        {
            unchecked
            {
                ulong h = 5381;
                if (tokenValues != null)
                {
                    for (int i = 0; i < tokenValues.Length; i++)
                    {
                        string t = tokenValues[i] ?? "";
                        for (int j = 0; j < t.Length; j++)
                            h = ((h << 5) + h) ^ (byte)t[j];
                        h = ((h << 5) + h) ^ 0x1F; // unit separator between tokens
                    }
                }
                return h.ToString("x16");
            }
        }

        /// <summary>
        /// ORPHAN-FIX: Parse the TagContainers ExtraParam (set by the Tokens &amp;
        /// Depth sub-tab) into a HashSet of allowed group codes. Returns null
        /// when the user hasn't pushed a selection yet — callers treat null as
        /// "accept everything".
        /// </summary>
        // Phase 165 perf — cache the parsed HashSet keyed by the raw CSV
        // value. WriteContainers calls LoadAllowedContainerGroups per element;
        // for an unchanged CSV (the typical case during a batch) the cache
        // hits and avoids the per-element string.Split + HashSet allocation.
        private static string _allowedGroupsCsvCache;
        private static HashSet<string> _allowedGroupsSetCache;
        private static HashSet<string> LoadAllowedContainerGroups()
        {
            try
            {
                string csv = StingTools.UI.StingCommandHandler.GetExtraParam("TagContainers");
                if (string.IsNullOrWhiteSpace(csv))
                {
                    _allowedGroupsCsvCache = null;
                    _allowedGroupsSetCache = null;
                    return null;
                }
                if (string.Equals(csv, _allowedGroupsCsvCache, StringComparison.Ordinal))
                    return _allowedGroupsSetCache;

                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string raw in csv.Split(','))
                {
                    string tok = raw?.Trim();
                    if (!string.IsNullOrEmpty(tok)) set.Add(tok);
                }
                if (set.Count == 0) { _allowedGroupsCsvCache = csv; _allowedGroupsSetCache = null; return null; }
                _allowedGroupsCsvCache = csv;
                _allowedGroupsSetCache = set;
                return set;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        /// <summary>
        /// ORPHAN-FIX: Decide whether a given container parameter name belongs
        /// to one of the allowed groups. TAG1..TAG6 are treated as a separate
        /// group code each (matching the sub-tab checkboxes); everything else
        /// is matched against discipline prefixes so that a user-disabled
        /// "MEP" group suppresses HVC_* / PLM_* / ELC_* containers.
        /// </summary>
        private static bool IsContainerInAllowedGroup(string paramName, HashSet<string> allowed)
        {
            if (string.IsNullOrEmpty(paramName) || allowed == null) return true;
            // Universal TAG1..TAG6
            for (int i = 1; i <= 6; i++)
            {
                string t = "ASS_TAG_" + i;
                if (paramName.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                    return allowed.Contains("TAG" + i);
            }
            // Discipline-keyed containers
            bool MEP = paramName.StartsWith("HVC_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("PLM_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("ELC_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("ELE_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("LTG_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("FLS_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("COM_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("SEC_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("NCL_", StringComparison.OrdinalIgnoreCase)
                    || paramName.StartsWith("ICT_", StringComparison.OrdinalIgnoreCase);
            if (MEP) return allowed.Contains("MEP") || allowed.Contains("M") || allowed.Contains("E")
                || allowed.Contains("P") || allowed.Contains("FP") || allowed.Contains("LV");
            bool ARCH = paramName.StartsWith("ARC_", StringComparison.OrdinalIgnoreCase)
                     || paramName.StartsWith("ASS_", StringComparison.OrdinalIgnoreCase);
            if (ARCH) return allowed.Contains("ARCH") || allowed.Contains("A") || allowed.Contains("GEN");
            bool STR = paramName.StartsWith("STR_", StringComparison.OrdinalIgnoreCase);
            if (STR) return allowed.Contains("STR") || allowed.Contains("S");
            // Unknown prefix — allow unless user has fully locked down (no GEN ticked either)
            return allowed.Contains("GEN") || allowed.Contains("G");
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

        #region V4 placement + CPC parameters

        // ASS_PLACE_ANCHOR_TXT — anchor reference used by FixturePlacementEngine (e.g. "DOOR_HINGE", "ROOM_CENTRE", "WALL_MIDPOINT")
        public const string PLACE_ANCHOR = "ASS_PLACE_ANCHOR_TXT";
        public const string PLACE_ANCHOR_GUID = "a4b5c6d7-e8f9-4a0b-8c1d-2e3f4a5b6c7d";

        // ASS_PLACE_OFFSET_X_MM — signed horizontal offset from anchor in millimetres
        public const string PLACE_OFFSET_X_MM = "ASS_PLACE_OFFSET_X_MM";
        public const string PLACE_OFFSET_X_MM_GUID = "b5c6d7e8-f9a0-4b1c-9d2e-3f4a5b6c7d8e";

        // ASS_PLACE_SIDE_TXT — wall/host side flag ("LEFT", "RIGHT", "EITHER")
        public const string PLACE_SIDE = "ASS_PLACE_SIDE_TXT";
        public const string PLACE_SIDE_GUID = "c6d7e8f9-a0b1-4c2d-ae3f-4a5b6c7d8e9f";

        // ELC_CPC_SZ_MM — circuit protective conductor size in mm² per BS 7671
        public const string CPC_SZ_MM = "ELC_CPC_SZ_MM";
        public const string CPC_SZ_MM_GUID = "d7e8f9a0-b1c2-4d3e-bf4a-5b6c7d8e9fa0";

        // PLM_PPE_INSULATION_THK_MM — pipe insulation thickness in millimetres
        public const string PPE_INSULATION_THK_MM = "PLM_PPE_INSULATION_THK_MM";
        public const string PPE_INSULATION_THK_MM_GUID = "e8f9a0b1-c2d3-4e4f-ca5b-6c7d8e9fa0b1";

        // PLM_SLOPE_PCT — drainage pipe slope per BS EN 12056 (1:80 default for sanitary)
        public const string PLM_SLOPE_PCT_V4 = "PLM_SLOPE_PCT";
        public const string PLM_SLOPE_PCT_V4_GUID = "f9a0b1c2-d3e4-4f5a-db6c-7d8e9fa0b1c2";

        // Phase 139.2 — first-fix box ↔ second-fix device matching key.
        public const string BOX_LOCATION_ID = "STING_BOX_LOCATION_ID";
        public const string BOX_LOCATION_ID_GUID = "C7A3F2E1-9B04-4D88-B5A1-3E6F8D2C1047";

        // Phase 139.2 — flag set on placed pendant/downlight when noggin is required.
        public const string NOGGIN_REQUIRED = "STING_NOGGIN_REQUIRED";
        public const string NOGGIN_REQUIRED_GUID = "D8B4A3F2-7C05-4E99-C6B2-4F7B9E3D2158";

        #endregion

        #region V6 / Tier 4-11 parameters
        //
        // Parameters backing tag label tiers T4..T11 (schema v5.3 — see STING_TAG_CONFIG_v5_0_*.csv).
        // Two GUID schemes coexist:
        //   1. Fabrication / LPS / cost params (T5 cost, T7 fab, T11 lightning) use canonical
        //      UUIDv5 hashes from `Core/Fabrication/FabricationParamsV4.cs` under namespace
        //      7f9f5e3a-a7c0-b2e4-4d91-4a557c5e3a00. These are the binding GUIDs the v4 family
        //      library publishes — MR_PARAMETERS.txt + STING_PARAMS_V6.txt + this region must
        //      match it byte-for-byte or shared-param binding silently fails.
        //   2. Tag-label-only params (T4 commissioning, T6 carbon, T8 clash, T9 as-built/health,
        //      T10 ACC/IFC) use the placeholder pattern `5753b5aa-000T-4000-8000-0000000000PP`
        //      where T is the tier (4..a hex) and PP is the per-tier row index. Pattern is valid
        //      hex so Guid.TryParse succeeds. Stable GUIDs will be assigned when those tiers'
        //      family library lands.
        // Each constant is mirrored into PARAMETER_REGISTRY.json extended_params.tier_4_10
        // so ParamRegistry.GetGuid() resolves correctly at runtime.
        //
        // NOTE: The earlier `v4-0001-...` / `v6-0001-...` pseudo-GUIDs were invalid hex and
        // silently dropped out of _guidByName. The 73 affected entries are now repaired across
        // MR_PARAMETERS.txt + .csv, STING_PARAMS_V6.txt, PARAMETER_REGISTRY.json, and this region.

        // --- T4: Commissioning & handover (N-G16 QR workflow) ---
        public const string COMM_STATE_TXT               = "COMM_STATE_TXT";
        public const string COMM_STATE_TXT_GUID          = "5753b5aa-0004-4000-8000-000000000001";
        public const string COMM_DATE_TXT                = "COMM_DATE_TXT";
        public const string COMM_DATE_TXT_GUID           = "5753b5aa-0004-4000-8000-000000000002";
        public const string COMM_OPERATIVE_TXT           = "COMM_OPERATIVE_TXT";
        public const string COMM_OPERATIVE_TXT_GUID      = "5753b5aa-0004-4000-8000-000000000003";
        public const string COMM_WITNESS_TXT             = "COMM_WITNESS_TXT";
        public const string COMM_WITNESS_TXT_GUID        = "5753b5aa-0004-4000-8000-000000000010";
        public const string COMM_NOTES_TXT               = "COMM_NOTES_TXT";
        public const string COMM_NOTES_TXT_GUID          = "5753b5aa-0004-4000-8000-000000000011";

        // --- T5: Cost & procurement (N-G12 install/labour + UGX/USD quote) ---
        public const string CST_UG_PRICE_UGX             = "CST_UG_PRICE_UGX";
        public const string CST_UG_PRICE_UGX_GUID        = "694fcd57-d0c2-5ed3-afca-f225781b3bc8";
        public const string CST_INTL_PRICE_USD           = "CST_INTL_PRICE_USD";
        public const string CST_INTL_PRICE_USD_GUID      = "c40720fa-3e80-5880-86c3-a82f43055fbf";
        public const string CST_QUOTE_REF_TXT            = "CST_QUOTE_REF_TXT";
        public const string CST_QUOTE_REF_TXT_GUID       = "4de58d8f-38e2-584f-b8aa-5a5744a80fcd";
        public const string CST_INSTALL_HRS              = "CST_INSTALL_HRS";
        public const string CST_INSTALL_HRS_GUID         = "5753b5aa-0005-4000-8000-000000000010";
        public const string CST_LABOUR_CREW_TXT          = "CST_LABOUR_CREW_TXT";
        public const string CST_LABOUR_CREW_TXT_GUID     = "5753b5aa-0005-4000-8000-000000000011";
        public const string CST_LABOUR_RATE_GBP          = "CST_LABOUR_RATE_GBP";
        public const string CST_LABOUR_RATE_GBP_GUID     = "5753b5aa-0005-4000-8000-000000000012";
        public const string CST_FX_RATE_USD_UGX          = "CST_FX_RATE_USD_UGX";
        public const string CST_FX_RATE_USD_UGX_GUID     = "d4e003e1-1f43-5d22-93c1-d9e91d672c52";
        public const string CST_LABOUR_HOURS             = "CST_LABOUR_HOURS";
        public const string CST_LABOUR_HOURS_GUID        = "cb945ed3-ff4d-531c-89fa-c06f503ab46c";
        public const string CST_LABOUR_RATE_UGX          = "CST_LABOUR_RATE_UGX";
        public const string CST_LABOUR_RATE_UGX_GUID     = "3d736d48-cba0-570b-a521-844539bd998c";
        public const string CST_SHIPPING_UGX             = "CST_SHIPPING_UGX";
        public const string CST_SHIPPING_UGX_GUID        = "5758facf-7a3f-5900-b3ea-abf487990b25";
        public const string CST_DUTY_PCT                 = "CST_DUTY_PCT";
        public const string CST_DUTY_PCT_GUID            = "c26d2b96-a012-50d6-bcb9-6a32f24212e0";

        // --- T6: Carbon & sustainability (N-G13 — ISO 14064 / BS EN 15978) ---
        public const string CBN_A1_A3_KG_CO2E            = "CBN_A1_A3_KG_CO2E";
        public const string CBN_A1_A3_KG_CO2E_GUID       = "5753b5aa-0006-4000-8000-000000000001";
        public const string CBN_A4_KG_CO2E               = "CBN_A4_KG_CO2E";
        public const string CBN_A4_KG_CO2E_GUID          = "5753b5aa-0006-4000-8000-000000000002";
        public const string CBN_B6_KG_CO2E_YR            = "CBN_B6_KG_CO2E_YR";
        public const string CBN_B6_KG_CO2E_YR_GUID       = "5753b5aa-0006-4000-8000-000000000003";
        public const string CBN_A5_KG_CO2E               = "CBN_A5_KG_CO2E";
        public const string CBN_A5_KG_CO2E_GUID          = "5753b5aa-0006-4000-8000-000000000010";
        public const string CBN_C1_KG_CO2E               = "CBN_C1_KG_CO2E";
        public const string CBN_C1_KG_CO2E_GUID          = "5753b5aa-0006-4000-8000-000000000011";
        public const string CBN_C2_KG_CO2E               = "CBN_C2_KG_CO2E";
        public const string CBN_C2_KG_CO2E_GUID          = "5753b5aa-0006-4000-8000-000000000012";
        public const string CBN_C3_C4_KG_CO2E            = "CBN_C3_C4_KG_CO2E";
        public const string CBN_C3_C4_KG_CO2E_GUID       = "5753b5aa-0006-4000-8000-000000000013";

        // --- T7: Fabrication & QC (BS EN ISO 6412 spool / QC inspector chain) ---
        public const string ASS_SPOOL_NR_TXT             = "ASS_SPOOL_NR_TXT";
        public const string ASS_SPOOL_NR_TXT_GUID        = "1a4353be-eaaa-5e46-95ee-b64a74667194";
        public const string ASS_FAB_STATUS_TXT           = "ASS_FAB_STATUS_TXT";
        public const string ASS_FAB_STATUS_TXT_GUID      = "29ba93ba-238e-5aad-930a-a621b0f43b5b";
        public const string ASS_QC_INSPECTOR_TXT         = "ASS_QC_INSPECTOR_TXT";
        public const string ASS_QC_INSPECTOR_TXT_GUID    = "a028f908-b100-53bc-b21a-1a0a6a03ffac";
        public const string ASS_WEIGHT_KG                = "ASS_WEIGHT_KG";
        public const string ASS_WEIGHT_KG_GUID           = "eacedb67-b65b-58f7-a5b7-f1b0253ac6c9";
        public const string ASS_TEST_PRESSURE_BAR        = "ASS_TEST_PRESSURE_BAR";
        public const string ASS_TEST_PRESSURE_BAR_GUID   = "3e3624d3-c79d-5dd5-8014-46c8d273b9ea";
        public const string ASS_FAB_LOC_TXT              = "ASS_FAB_LOC_TXT";
        public const string ASS_FAB_LOC_TXT_GUID         = "e420804b-d43f-593c-91b1-fd00a18aa584";
        public const string ASS_FAB_SEQ_NR               = "ASS_FAB_SEQ_NR";
        public const string ASS_FAB_SEQ_NR_GUID          = "5fc70bc2-9955-583c-9d96-5b54c8f34f53";
        public const string ASS_SHIP_DATE_TXT            = "ASS_SHIP_DATE_TXT";
        public const string ASS_SHIP_DATE_TXT_GUID       = "c2fc8e62-b793-517c-94c6-d2d7ae7584fe";
        // Migrated to canonical ASS_INSTALLATION_DATE_TXT (GROUP 1, GUID cfc716aa); the
        // 953575a9 alias remains in MR_PARAMETERS.txt for backwards compat but is marked
        // DEPRECATED. See FabricationParamsV4.INSTALL_DATE_TXT for the v4 fabrication entry.
        public const string ASS_INSTALL_DATE_TXT         = "ASS_INSTALLATION_DATE_TXT";
        public const string ASS_INSTALL_DATE_TXT_GUID    = "cfc716aa-126d-5e9e-a9e8-3c2a2b52d933";
        public const string ASS_BOM_REV_TXT              = "ASS_BOM_REV_TXT";
        public const string ASS_BOM_REV_TXT_GUID         = "0293f487-2ca9-5514-9b18-ac98b1a20b27";
        public const string ASS_WELD_COUNT_NR            = "ASS_WELD_COUNT_NR";
        public const string ASS_WELD_COUNT_NR_GUID       = "6c77833e-4b97-57f5-9a8b-97cc20d6cb61";
        public const string ASS_BOLT_COUNT_NR            = "ASS_BOLT_COUNT_NR";
        public const string ASS_BOLT_COUNT_NR_GUID       = "77c9f963-0164-5c71-879d-ee7308091866";
        public const string ASS_FLANGE_COUNT_NR          = "ASS_FLANGE_COUNT_NR";
        public const string ASS_FLANGE_COUNT_NR_GUID     = "016faa7f-1e8f-5a5d-acfb-14983937de69";
        public const string ASS_FITTING_COUNT_NR         = "ASS_FITTING_COUNT_NR";
        public const string ASS_FITTING_COUNT_NR_GUID    = "ead7d5f3-68fa-58c6-8a21-fa8c6a1ff318";
        public const string ASS_LENGTH_TOTAL_MM          = "ASS_LENGTH_TOTAL_MM";
        public const string ASS_LENGTH_TOTAL_MM_GUID     = "2605366f-f56b-5843-b8cb-9781b42a4345";
        public const string ASS_CUT_COUNT_NR             = "ASS_CUT_COUNT_NR";
        public const string ASS_CUT_COUNT_NR_GUID        = "16e7224e-cab9-5233-b155-3fbe194a3d56";
        public const string ASS_INSULATION_AREA_M2       = "ASS_INSULATION_AREA_M2";
        public const string ASS_INSULATION_AREA_M2_GUID  = "21a49d34-9ae2-5058-8d7e-43db4dabd545";
        public const string ASS_SUPPORT_COUNT_NR         = "ASS_SUPPORT_COUNT_NR";
        public const string ASS_SUPPORT_COUNT_NR_GUID    = "9fadd466-7dfa-5845-9a18-d618c77c418d";
        public const string ASS_FAB_NOTES_TXT            = "ASS_FAB_NOTES_TXT";
        public const string ASS_FAB_NOTES_TXT_GUID       = "9107dff2-054c-5371-b3ae-6d329aa12542";
        public const string ASS_SPOOL_DRAWING_REF_TXT    = "ASS_SPOOL_DRAWING_REF_TXT";
        public const string ASS_SPOOL_DRAWING_REF_TXT_GUID = "c1a5983c-333d-53ff-94d1-4326d9ffff86";

        // --- T8: Clash triage + resolution (N-G5 / N-G6) ---
        public const string CLASH_TRIAGE_SEVERITY_NR     = "CLASH_TRIAGE_SEVERITY_NR";
        public const string CLASH_TRIAGE_SEVERITY_NR_GUID = "5753b5aa-0008-4000-8000-000000000001";
        public const string CLASH_TRIAGE_CATEGORY_TXT    = "CLASH_TRIAGE_CATEGORY_TXT";
        public const string CLASH_TRIAGE_CATEGORY_TXT_GUID = "5753b5aa-0008-4000-8000-000000000002";
        public const string CLASH_RESOLUTION_STATUS_TXT  = "CLASH_RESOLUTION_STATUS_TXT";
        public const string CLASH_RESOLUTION_STATUS_TXT_GUID = "5753b5aa-0008-4000-8000-000000000003";
        public const string CLASH_TRIAGE_SCORE           = "CLASH_TRIAGE_SCORE";
        public const string CLASH_TRIAGE_SCORE_GUID      = "5753b5aa-0008-4000-8000-000000000010";
        public const string CLASH_RESOLUTION_ACTION_TXT  = "CLASH_RESOLUTION_ACTION_TXT";
        public const string CLASH_RESOLUTION_ACTION_TXT_GUID = "5753b5aa-0008-4000-8000-000000000011";

        // --- T9: As-built reconciliation & model health (N-G4 / N-G9) ---
        public const string ASBUILT_DEVIATION_MM         = "ASBUILT_DEVIATION_MM";
        public const string ASBUILT_DEVIATION_MM_GUID    = "5753b5aa-0009-4000-8000-000000000001";
        public const string ASBUILT_CAPTURE_DATE_TXT     = "ASBUILT_CAPTURE_DATE_TXT";
        public const string ASBUILT_CAPTURE_DATE_TXT_GUID = "5753b5aa-0009-4000-8000-000000000002";
        public const string HEALTH_SCORE_LAST_NR         = "HEALTH_SCORE_LAST_NR";
        public const string HEALTH_SCORE_LAST_NR_GUID    = "5753b5aa-0009-4000-8000-000000000003";
        public const string HEALTH_SCORE_DATE_TXT        = "HEALTH_SCORE_DATE_TXT";
        public const string HEALTH_SCORE_DATE_TXT_GUID   = "5753b5aa-0009-4000-8000-000000000010";

        // --- T10: Compliance / audit trail (N-G8 ACC round-trip + N-G14 IFC PSet) ---
        public const string IFC_PSET_OVERRIDE_TXT        = "IFC_PSET_OVERRIDE_TXT";
        public const string IFC_PSET_OVERRIDE_TXT_GUID   = "5753b5aa-000a-4000-8000-000000000001";
        public const string ACC_ISSUE_ID_TXT             = "ACC_ISSUE_ID_TXT";
        public const string ACC_ISSUE_ID_TXT_GUID        = "5753b5aa-000a-4000-8000-000000000002";
        public const string ACC_SYNC_STATUS_TXT          = "ACC_SYNC_STATUS_TXT";
        public const string ACC_SYNC_STATUS_TXT_GUID     = "5753b5aa-000a-4000-8000-000000000003";

        // --- T11: Lightning protection system (BS EN 62305) ---
        public const string ELC_LPS_CLASS_TXT                  = "ELC_LPS_CLASS_TXT";
        public const string ELC_LPS_CLASS_TXT_GUID             = "081c2e86-3af9-5658-8a26-63da9c1eccc2";
        public const string ELC_LPS_ROLLING_SPHERE_RADIUS_M    = "ELC_LPS_ROLLING_SPHERE_RADIUS_M";
        public const string ELC_LPS_ROLLING_SPHERE_RADIUS_M_GUID = "c4eeed34-608c-56a5-b97f-7c899d76f208";
        public const string ELC_LPS_MESH_SIZE_M                = "ELC_LPS_MESH_SIZE_M";
        public const string ELC_LPS_MESH_SIZE_M_GUID           = "d6a9566f-eda9-5e6d-9dcf-fd14440c395b";
        public const string ELC_LPS_AIR_TERMINAL_COUNT_NR      = "ELC_LPS_AIR_TERMINAL_COUNT_NR";
        public const string ELC_LPS_AIR_TERMINAL_COUNT_NR_GUID = "36889f59-a8ba-55c8-8777-6ba332b39bff";
        public const string ELC_LPS_DOWN_CONDUCTOR_COUNT_NR    = "ELC_LPS_DOWN_CONDUCTOR_COUNT_NR";
        public const string ELC_LPS_DOWN_CONDUCTOR_COUNT_NR_GUID = "157527ba-17a8-5014-b6c5-f70273ccd5f5";
        public const string ELC_LPS_EARTH_ELECTRODE_COUNT_NR   = "ELC_LPS_EARTH_ELECTRODE_COUNT_NR";
        public const string ELC_LPS_EARTH_ELECTRODE_COUNT_NR_GUID = "d02bca9d-9159-5477-8488-48f9076841fa";
        public const string ELC_LPS_EARTH_RESISTANCE_OHM       = "ELC_LPS_EARTH_RESISTANCE_OHM";
        public const string ELC_LPS_EARTH_RESISTANCE_OHM_GUID  = "80da349f-708b-5165-bb2e-f369dec80e4b";
        public const string ELC_LPS_BOND_TYPE_TXT              = "ELC_LPS_BOND_TYPE_TXT";
        public const string ELC_LPS_BOND_TYPE_TXT_GUID         = "1cb4c3d3-8c12-5be3-9eeb-4072b4be3240";
        public const string ELC_LPS_PROTECTION_ANGLE_DEG       = "ELC_LPS_PROTECTION_ANGLE_DEG";
        public const string ELC_LPS_PROTECTION_ANGLE_DEG_GUID  = "0063477e-cda5-58a3-a802-061838e57a47";
        public const string ELC_LPS_ZONE_TXT                   = "ELC_LPS_ZONE_TXT";
        public const string ELC_LPS_ZONE_TXT_GUID              = "a01025f4-6155-524e-8514-72507f5e04ef";
        public const string ELC_LPS_RISK_ASSESSMENT_TXT        = "ELC_LPS_RISK_ASSESSMENT_TXT";
        public const string ELC_LPS_RISK_ASSESSMENT_TXT_GUID   = "330d6fb5-2891-5a28-8ec6-e04618c9d1e4";
        public const string ELC_LPS_SURGE_PROTECTION_LVL_TXT   = "ELC_LPS_SURGE_PROTECTION_LVL_TXT";
        public const string ELC_LPS_SURGE_PROTECTION_LVL_TXT_GUID = "c1605d30-bdcb-560e-9d97-bae3303a078e";
        public const string ELC_LPS_SEPARATION_DISTANCE_MM     = "ELC_LPS_SEPARATION_DISTANCE_MM";
        public const string ELC_LPS_SEPARATION_DISTANCE_MM_GUID = "441346ff-828f-5298-9fbb-96f27feb22ef";
        public const string ELC_LPS_CONDUCTOR_CROSS_SECT_MM2   = "ELC_LPS_CONDUCTOR_CROSS_SECT_MM2";
        public const string ELC_LPS_CONDUCTOR_CROSS_SECT_MM2_GUID = "423133ca-7535-521d-9c37-65ec7ae68166";
        public const string ELC_LPS_EARTH_TYPE_TXT             = "ELC_LPS_EARTH_TYPE_TXT";
        public const string ELC_LPS_EARTH_TYPE_TXT_GUID        = "3703245d-a866-5e05-8737-72babfbb85a4";
        public const string ELC_LPS_INSPECTION_INTERVAL_MONTHS = "ELC_LPS_INSPECTION_INTERVAL_MONTHS";
        public const string ELC_LPS_INSPECTION_INTERVAL_MONTHS_GUID = "5339fe4f-caa3-5edc-99b1-53c0defd4ad8";
        public const string ELC_LPS_TEST_DATE_TXT              = "ELC_LPS_TEST_DATE_TXT";
        public const string ELC_LPS_TEST_DATE_TXT_GUID         = "d654df13-0913-5e8f-8dfe-98b3971beb86";
        public const string ELC_LPS_CERT_REF_TXT               = "ELC_LPS_CERT_REF_TXT";
        public const string ELC_LPS_CERT_REF_TXT_GUID          = "0a8dbcfb-6f73-5c8c-94eb-b72606feae87";

        #endregion
    }
}
