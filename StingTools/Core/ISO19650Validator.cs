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

// ISO19650Validator relocated out of the oversized TagConfig.cs.
// Same namespace (StingTools.Core) — transparent to all callers.

namespace StingTools.Core
{
    /// <summary>
    /// ISO 19650 naming and coding validation. Enforces that all token values
    /// conform to the allowed code lists defined in the tag configuration.
    /// </summary>
    public static class ISO19650Validator
    {
        /// <summary>FLEX-001: Custom DISC codes from project_config.json CUSTOM_VALID_DISC.
        /// When non-empty, these are ADDED to the built-in codes for validation.</summary>
        public static HashSet<string> CustomDiscCodes { get; internal set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>FLEX-001: Custom SYS codes from project_config.json CUSTOM_VALID_SYS.</summary>
        public static HashSet<string> CustomSysCodes { get; internal set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>FLEX-001: Custom FUNC codes from project_config.json CUSTOM_VALID_FUNC.</summary>
        public static HashSet<string> CustomFuncCodes { get; internal set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>FLEX-001: Custom LOC codes from project_config.json CUSTOM_VALID_LOC.</summary>
        public static HashSet<string> CustomLocCodes { get; internal set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>FLEX-001: Custom ZONE codes from project_config.json CUSTOM_VALID_ZONE.</summary>
        public static HashSet<string> CustomZoneCodes { get; internal set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Built-in valid discipline codes per ISO 19650.</summary>
        private static readonly HashSet<string> _builtInDiscCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "M", "E", "P", "A", "S", "FP", "LV", "G"
        };

        /// <summary>Valid discipline codes: built-in + custom from config (FLEX-001). Cached to avoid per-access allocation.</summary>
        private static HashSet<string> _cachedValidDiscCodes;
        public static HashSet<string> ValidDiscCodes
        {
            get
            {
                if (CustomDiscCodes.Count == 0) return _builtInDiscCodes;
                var cached = _cachedValidDiscCodes;
                if (cached != null) return cached;
                var combined = new HashSet<string>(_builtInDiscCodes, StringComparer.OrdinalIgnoreCase);
                foreach (string c in CustomDiscCodes) combined.Add(c);
                _cachedValidDiscCodes = combined;
                return combined;
            }
        }

        /// <summary>
        /// Valid system codes per CIBSE / Uniclass 2015 / ISO 19650.
        /// DCW = Domestic Cold Water (Uniclass Ss_55_70_38_15, CAWS S10)
        /// DHW = Domestic Hot Water (Uniclass Ss_55_70_38, CAWS S11)
        /// HWS = Heating Water System (LTHW heating circuits)
        /// SAN = Sanitary drainage (Uniclass Ss_50_30_04, CAWS R11)
        /// GAS = Gas Supply (Uniclass Ss_55_20_34, CAWS S63)
        /// RWD = Rainwater Drainage (Uniclass Ss_50_30_02, CAWS R10)
        /// CSV element type codes (P-WSP, P-DRN) map to these system codes.
        /// </summary>
        /// <summary>ISO 19650 fallback SYS codes used when no project-specific config is loaded.</summary>
        private static readonly HashSet<string> _fallbackSysCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HVAC", "HWS", "DHW", "DCW", "SAN", "RWD", "GAS", "FP", "LV",
            "FLS", "COM", "ICT", "NCL", "SEC",
            "ARC", "STR", "GEN"
        };

        /// <summary>
        /// Valid system codes. Dynamically derived from TagConfig.SysMap keys when available,
        /// falling back to hardcoded CIBSE/Uniclass codes when SysMap is not loaded.
        /// This ensures custom project SYS codes in project_config.json are accepted.
        /// BUG-06: Uses Count > 0 check to handle empty but non-null SysMap.
        /// </summary>
        /// <summary>PERF-01: Cached SYS codes set — rebuilt only when SysMap changes (via InvalidateValidatorCaches).</summary>
        private static HashSet<string> _cachedValidSysCodes;
        public static HashSet<string> ValidSysCodes
        {
            get
            {
                if (TagConfig.SysMap == null || TagConfig.SysMap.Count == 0) return _fallbackSysCodes;
                var cached = _cachedValidSysCodes;
                if (cached != null) return cached;
                var set = new HashSet<string>(TagConfig.SysMap.Keys, StringComparer.OrdinalIgnoreCase);
                if (CustomSysCodes.Count > 0) foreach (string c in CustomSysCodes) set.Add(c);
                _cachedValidSysCodes = set;
                return set;
            }
        }

        /// <summary>ISO 19650 fallback FUNC codes used when no project-specific config is loaded.</summary>
        private static readonly HashSet<string> _fallbackFuncCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SUP", "HTG", "DCW", "SAN", "RWD", "GAS", "FP", "PWR", "FLS",
            "COM", "ICT", "NCL", "SEC",
            "FIT", "STR", "GEN",
            "EXH", "RTN", "FRA", "DHW"
        };

        /// <summary>
        /// BUG-06: Valid FUNC codes derived from TagConfig.FuncMap keys when available,
        /// so project-specific codes added via project_config.json are accepted.
        /// Falls back to ISO 19650 standard codes when FuncMap is empty.
        /// </summary>
        /// <summary>PERF-01: Cached FUNC codes set — rebuilt only when FuncMap changes (via InvalidateValidatorCaches).</summary>
        private static HashSet<string> _cachedValidFuncCodes;
        public static HashSet<string> ValidFuncCodes
        {
            get
            {
                if (TagConfig.FuncMap == null || TagConfig.FuncMap.Count == 0) return _fallbackFuncCodes;
                var cached = _cachedValidFuncCodes;
                if (cached != null) return cached;
                var set = new HashSet<string>(TagConfig.FuncMap.Keys, StringComparer.OrdinalIgnoreCase);
                if (CustomFuncCodes.Count > 0) foreach (string c in CustomFuncCodes) set.Add(c);
                _cachedValidFuncCodes = set;
                return set;
            }
        }

        // F-08: Cached HashSet<string> for LocCodes and ZoneCodes — avoids O(n) List.Contains in ValidateToken
        private static HashSet<string> _locCodesSet;
        private static HashSet<string> _zoneCodesSet;

        /// <summary>PERF-01: Invalidate cached validator code sets. Call after LoadFromFile/LoadDefaults or custom code changes.</summary>
        internal static void InvalidateValidatorCaches()
        {
            _cachedValidDiscCodes = null;
            _cachedValidSysCodes = null;
            _cachedValidFuncCodes = null;
            _locCodesSet = null;  // F-08
            _zoneCodesSet = null; // F-08
            _tokenValidationCache.Clear(); // Phase 78: Clear memoized validation results
            _validFuncsCsvLoaded = false;  // Force reload of CSV-derived func codes
            EnsureValidFuncsLoaded();
        }

        /// <summary>
        /// Validate a single token value against its allowed code list.
        /// Returns null if valid, or an error message string if invalid.
        /// </summary>
        public static string ValidateToken(string tokenName, string value)
        {
            if (string.IsNullOrEmpty(value))
                return $"{tokenName}: empty value";

            // Use if/else chain instead of switch — ParamRegistry properties are not compile-time constants
            if (tokenName == ParamRegistry.DISC)
            {
                if (!ValidDiscCodes.Contains(value))
                    return $"DISC '{value}' not in valid set ({string.Join(",", ValidDiscCodes)})";
            }
            else if (tokenName == ParamRegistry.LOC)
            {
                // FLEX-001: Accept custom LOC codes from config before strict/lenient check
                if (CustomLocCodes.Count > 0 && CustomLocCodes.Contains(value))
                    return null; // Custom code accepted
                if (TagConfig.ValidateStrictMode)
                {
                    // F-08: Use cached HashSet for O(1) lookup instead of List.Contains O(n)
                    var locSet = _locCodesSet ??= new HashSet<string>(TagConfig.LocCodes, StringComparer.OrdinalIgnoreCase);
                    if (!locSet.Contains(value))
                        return $"LOC '{value}' not in valid set ({string.Join(",", TagConfig.LocCodes)})";
                }
                else
                {
                    // Lenient: accept any alphanumeric string 1-8 chars, no spaces
                    if (value.Length < 1 || value.Length > 8 || value.Contains(" ") || !value.All(char.IsLetterOrDigit))
                        return $"LOC '{value}' must be 1-8 alphanumeric characters with no spaces";
                }
            }
            else if (tokenName == ParamRegistry.ZONE)
            {
                // FLEX-001: Accept custom ZONE codes from config before strict/lenient check
                if (CustomZoneCodes.Count > 0 && CustomZoneCodes.Contains(value))
                    return null; // Custom code accepted
                if (TagConfig.ValidateStrictMode)
                {
                    // F-08: Use cached HashSet for O(1) lookup instead of List.Contains O(n)
                    var zoneSet = _zoneCodesSet ??= new HashSet<string>(TagConfig.ZoneCodes, StringComparer.OrdinalIgnoreCase);
                    if (!zoneSet.Contains(value))
                        return $"ZONE '{value}' not in valid set ({string.Join(",", TagConfig.ZoneCodes)})";
                }
                else
                {
                    // Lenient: accept any alphanumeric string 1-8 chars, no spaces
                    if (value.Length < 1 || value.Length > 8 || value.Contains(" ") || !value.All(char.IsLetterOrDigit))
                        return $"ZONE '{value}' must be 1-8 alphanumeric characters with no spaces";
                }
            }
            else if (tokenName == ParamRegistry.SYS)
            {
                if (!ValidSysCodes.Contains(value))
                    return $"SYS '{value}' not in valid set ({string.Join(",", ValidSysCodes)})";
            }
            else if (tokenName == ParamRegistry.FUNC)
            {
                if (!ValidFuncCodes.Contains(value))
                    return $"FUNC '{value}' not in valid set ({string.Join(",", ValidFuncCodes)})";
            }
            else if (tokenName == ParamRegistry.LVL)
            {
                // Valid LVL codes: L00-L99, L100+, GF, LG, UG, B1-B9, B10+, SB, RF, PH, AT, TR, POD, MZ, PL
                string lvlUpper = value.ToUpperInvariant();
                if (lvlUpper.Length > 4 || lvlUpper.Contains(" "))
                    return $"LVL '{value}' exceeds 4-char limit or contains spaces";
                if (lvlUpper == "XX")
                    return null; // XX is valid but a placeholder
                bool isKnownLvl = lvlUpper == "GF" || lvlUpper == "RF" || lvlUpper == "LG" ||
                    lvlUpper == "UG" || lvlUpper == "MZ" || lvlUpper == "PL" || lvlUpper == "PH" ||
                    lvlUpper == "AT" || lvlUpper == "TR" || lvlUpper == "POD" ||
                    (lvlUpper.StartsWith("L") && lvlUpper.Length >= 2 && lvlUpper.Length <= 4 &&
                        lvlUpper.Substring(1).All(char.IsDigit)) ||
                    (lvlUpper.StartsWith("B") && lvlUpper.Length >= 2 && lvlUpper.Length <= 3 &&
                        lvlUpper.Substring(1).All(char.IsDigit)) ||
                    (lvlUpper.StartsWith("SB") && (lvlUpper.Length == 2 ||
                        lvlUpper.Substring(2).All(char.IsDigit)));
                if (!isKnownLvl && !lvlUpper.All(c => char.IsLetterOrDigit(c)))
                    return $"LVL '{value}' contains invalid characters";
            }
            else if (tokenName == ParamRegistry.PROD)
            {
                // PROD codes: 2-4 uppercase alphanumeric characters
                if (value.Length < 2 || value.Length > 4)
                    return $"PROD '{value}' should be 2-4 characters";
                if (!value.All(c => char.IsLetterOrDigit(c)))
                    return $"PROD '{value}' must be alphanumeric only";
            }
            else if (tokenName == ParamRegistry.SEQ)
            {
                if (!int.TryParse(value, out int seqVal))
                    return $"SEQ '{value}' is not a valid number";
                if (seqVal < 0)
                    return $"SEQ '{value}' must be a positive number";
                int seqWidth = TagConfig.SeqPadWidth > 0 ? TagConfig.SeqPadWidth : TagConfig.NumPad;
                if (value.Length > seqWidth + 1)
                    return $"SEQ '{value}' exceeds {seqWidth}-digit format";
            }
            return null; // valid
        }

        /// <summary>Phase 78: Validation memoization cache — caches ValidateToken results per (token,value) pair.
        /// For 50K elements with ~200 unique token combinations, reduces validation calls from 50K×8 to ~200.
        /// Thread-safe ConcurrentDictionary. Cleared via InvalidateValidatorCaches().</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string>
            _tokenValidationCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();

        /// <summary>Phase 78: Memoized token validation — O(1) lookup for repeated (token,value) pairs.</summary>
        private static string ValidateTokenCached(string tokenName, string value)
        {
            string cacheKey = $"{tokenName}|{value ?? ""}";
            return _tokenValidationCache.GetOrAdd(cacheKey, _ => ValidateToken(tokenName, value));
        }

        /// <summary>
        /// Validate all 8 tokens on an element. Returns a list of validation errors
        /// (empty list = fully valid). Uses memoized token validation for O(1) repeated lookups.
        /// </summary>
        // F-16: Static readonly token param array — avoids per-call allocation
        private static readonly string[] _validateElementTokenParams = new[]
        {
            ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
            ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
            ParamRegistry.PROD, ParamRegistry.SEQ,
        };
        // F-16: [ThreadStatic] reusable error list — avoids List<ValidationError> allocation per call
        [ThreadStatic]
        private static List<ValidationError> _validateElementErrors;

        public static List<ValidationError> ValidateElement(Element el)
        {
            // F-16: Reuse thread-local list instead of allocating new on each call
            if (_validateElementErrors == null) _validateElementErrors = new List<ValidationError>();
            else _validateElementErrors.Clear();
            var errors = _validateElementErrors;
            string[] tokenParams = _validateElementTokenParams;

            foreach (string param in tokenParams)
            {
                string val = ParameterHelpers.GetString(el, param);
                string error = ValidateTokenCached(param, val);
                if (error != null)
                {
                    var errorType = string.IsNullOrEmpty(val)
                        ? ValidationErrorType.TokenEmpty
                        : ValidationErrorType.TokenFormat;
                    errors.Add(new ValidationError(error, errorType));
                }
            }

            // Cross-validate: DISC must match element category
            // Accounts for system-aware DISC correction (pipes can be "P" when system is plumbing)
            string catName = ParameterHelpers.GetCategoryName(el);
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
            string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            if (!string.IsNullOrEmpty(catName) && !string.IsNullOrEmpty(disc))
            {
                string expectedDisc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : null;
                // Apply system-aware DISC correction (e.g., M→P for plumbing pipes, M→FP for fire)
                if (expectedDisc != null && !string.IsNullOrEmpty(sys))
                    expectedDisc = TagConfig.GetSystemAwareDisc(expectedDisc, sys, catName);
                if (expectedDisc != null && expectedDisc != disc)
                    errors.Add(new ValidationError(
                        $"DISC mismatch: element category '{catName}' expects '{expectedDisc}' but has '{disc}'",
                        ValidationErrorType.CrossValidation));
            }

            // Cross-validate: SYS should be valid for this category
            // Uses SysMap lookup to allow ALL valid SYS codes for ambiguous categories
            // (e.g., Pipes can be DCW, DHW, SAN, RWD, GAS, HWS, FP)
            if (!string.IsNullOrEmpty(catName) && !string.IsNullOrEmpty(sys))
            {
                bool sysValidForCategory = false;
                // Check if this SYS code lists this category in SysMap
                if (TagConfig.SysMap.TryGetValue(sys, out var sysCats) && sysCats.Contains(catName))
                    sysValidForCategory = true;
                // Also accept discipline-default SYS codes (ARC, STR, GEN, etc.)
                string discForCat = TagConfig.DiscMap.TryGetValue(catName, out string dc) ? dc : "A";
                if (sys == TagConfig.GetDiscDefaultSysCode(discForCat))
                    sysValidForCategory = true;
                if (!sysValidForCategory)
                {
                    // Find what SYS codes ARE valid for this category
                    var validSysForCat = TagConfig.SysMap.Where(kvp => kvp.Value.Contains(catName))
                        .Select(kvp => kvp.Key).ToList();
                    string validList = validSysForCat.Count > 0
                        ? string.Join("/", validSysForCat)
                        : TagConfig.GetDiscDefaultSysCode(discForCat);
                    errors.Add(new ValidationError(
                        $"SYS mismatch: category '{catName}' expects '{validList}' but has '{sys}'",
                        ValidationErrorType.CrossValidation));
                }
            }

            // Cross-validate: PROD should be consistent with DISC/SYS
            string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
            if (!string.IsNullOrEmpty(prod) && !string.IsNullOrEmpty(disc))
            {
                string prodError = ValidateProdForDisc(prod, disc);
                if (prodError != null)
                    errors.Add(new ValidationError(prodError, ValidationErrorType.CrossValidation));
            }

            // Cross-validate: FUNC should be consistent with SYS
            // Phase 39: Expanded FUNC-SYS cross-validation with discipline-aware mapping.
            // Each SYS code has a set of valid FUNC codes (primary + smart sub-functions).
            // FUNC codes from unrelated disciplines are flagged as cross-validation errors.
            string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
            if (!string.IsNullOrEmpty(func) && !string.IsNullOrEmpty(sys))
            {
                var validFuncsForSys = GetValidFuncsForSys(sys);
                if (validFuncsForSys.Count > 0 && !validFuncsForSys.Contains(func))
                {
                    string expectedList = string.Join("/", validFuncsForSys);
                    errors.Add(new ValidationError(
                        $"FUNC '{func}' not valid for SYS '{sys}' (expected one of: {expectedList})",
                        ValidationErrorType.CrossValidation));
                }
            }

            // Phase 66b: Cross-validate FUNC→PROD — detect contradictory function/product pairs.
            // e.g., FUNC=SUP (Supply) with PROD=WC (WC fixture) is contradictory.
            if (!string.IsNullOrEmpty(func) && !string.IsNullOrEmpty(prod) &&
                func != "GEN" && prod != "GEN")
            {
                string funcProdError = ValidateFuncProdPair(func, prod, disc);
                if (funcProdError != null)
                    errors.Add(new ValidationError(funcProdError, ValidationErrorType.CrossValidation));
            }

            // Phase 86: Return defensive copy — raw [ThreadStatic] reference would be
            // cleared on next call, corrupting any caller that stored the result.
            return new List<ValidationError>(errors);
        }

        /// <summary>Phase 66b: Validate FUNC→PROD pair consistency.
        /// Detects contradictory function/product combinations like FUNC=SUP with PROD=WC.</summary>
        // PERF: Static readonly to avoid per-call Dictionary+HashSet allocation
        private static readonly Dictionary<string, HashSet<string>> _incompatibleFuncProdPairs =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                // Supply function should not have sanitary/plumbing products
                { "SUP", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WC", "WHB", "URN", "SNK", "SHW", "BTH", "BID", "MOP" } },
                // Return function should not have electrical products
                { "RTN", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DB", "MCC", "MSB", "SWB", "SKT", "LUM" } },
                // Lighting function should not have HVAC products
                { "LTG", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AHU", "FCU", "VAV", "CHR", "BLR", "RAD", "DAM" } },
                // Power function should not have plumbing products
                { "PWR", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WC", "WHB", "PP", "PFT", "PAC", "FPP", "TRP" } },
                // Sanitary function should not have HVAC products
                { "SAN", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AHU", "FCU", "VAV", "FAN", "HRU", "DAM", "CLT" } },
                // Fire protection function should not have architectural products
                { "FLS", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DR", "WIN", "WL", "FL", "CLG", "RF", "FUR" } },
            };

        private static string ValidateFuncProdPair(string func, string prod, string disc)
        {
            if (_incompatibleFuncProdPairs.TryGetValue(func, out var badProds) && badProds.Contains(prod))
                return $"FUNC '{func}' is incompatible with PROD '{prod}' — check discipline assignment";

            return null;
        }

        /// <summary>
        /// Validate the format of a complete assembled tag string.
        /// Returns null if valid, or an error description.
        /// </summary>
        public static string ValidateTagFormat(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return "Tag is empty";

            // Phase 86b: Use full separator string for multi-char separator support
            string sepStr = !string.IsNullOrEmpty(TagConfig.Separator) ? TagConfig.Separator : "-";
            string[] parts = tag.Split(new[] { sepStr }, StringSplitOptions.None);
            if (parts.Length != 8)
                return $"Tag has {parts.Length} segments (expected 8): {tag}";

            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    return $"Segment {i + 1} is empty in tag: {tag}";
            }

            // Validate ALL 8 segments against their respective rules
            // Phase 86: Use cached validation for O(1) repeated lookups
            string discError = ValidateTokenCached(ParamRegistry.DISC, parts[0]);
            if (discError != null) return discError;

            string locError = ValidateTokenCached(ParamRegistry.LOC, parts[1]);
            if (locError != null) return locError;

            string zoneError = ValidateTokenCached(ParamRegistry.ZONE, parts[2]);
            if (zoneError != null) return zoneError;

            string lvlError = ValidateTokenCached(ParamRegistry.LVL, parts[3]);
            if (lvlError != null) return lvlError;

            string sysError = ValidateTokenCached(ParamRegistry.SYS, parts[4]);
            if (sysError != null) return sysError;

            string funcError = ValidateTokenCached(ParamRegistry.FUNC, parts[5]);
            if (funcError != null) return funcError;

            string prodError = ValidateTokenCached(ParamRegistry.PROD, parts[6]);
            if (prodError != null) return prodError;

            string seqError = ValidateTokenCached(ParamRegistry.SEQ, parts[7]);
            if (seqError != null) return seqError;

            return null; // valid
        }

        /// <summary>
        /// Phase 39: Get valid FUNC codes for a given SYS code.
        /// Each system has primary FUNC + allowed sub-function variants (e.g., HVAC allows SUP/RTN/EXH/FRA).
        /// Returns empty set if SYS is unknown (no validation applied).
        /// Cross-references CIBSE TM40, Uniclass 2015 Ss tables, and ISO 19650 annex.
        /// </summary>
        internal static HashSet<string> GetValidFuncsForSys(string sys)
        {
            EnsureValidFuncsLoaded();
            if (_validFuncsForSys.TryGetValue(sys, out var funcs)) return funcs;
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool _validFuncsCsvLoaded = false;
        private static void EnsureValidFuncsLoaded()
        {
            if (_validFuncsCsvLoaded) return;
            _validFuncsCsvLoaded = true;
            TryLoadValidFuncsFromCsv();
        }

        /// <summary>
        /// Attempts to rebuild _validFuncsForSys from STING_FUNC_SYS_MATRIX.csv.
        /// Falls back to the hardcoded defaults if the file is absent or malformed.
        /// Called from LoadFromFile, LoadDefaults, and lazily from GetValidFuncsForSys.
        /// </summary>
        private static void TryLoadValidFuncsFromCsv()
        {
            try
            {
                string csvPath = StingToolsApp.FindDataFile("STING_FUNC_SYS_MATRIX.csv");
                if (string.IsNullOrEmpty(csvPath) || !System.IO.File.Exists(csvPath)) return;

                var loaded = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                bool first = true;
                foreach (string raw in System.IO.File.ReadLines(csvPath))
                {
                    if (first) { first = false; continue; } // skip header
                    string line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    // CSV: SYS_CODE,SYS_DESCRIPTION,FUNC_CODE,FUNC_DESCRIPTION,...
                    var cols = StingToolsApp.ParseCsvLine(line);
                    if (cols == null || cols.Length < 3) continue;
                    string sysCode  = cols[0].Trim();
                    string funcCode = cols[2].Trim();
                    if (string.IsNullOrEmpty(sysCode) || string.IsNullOrEmpty(funcCode)) continue;

                    if (!loaded.TryGetValue(sysCode, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        loaded[sysCode] = set;
                    }
                    set.Add(funcCode);
                }

                if (loaded.Count > 0)
                {
                    // Merge into existing dict: CSV wins; hardcoded entries not in CSV are retained
                    foreach (var kvp in loaded)
                    {
                        if (_validFuncsForSys.TryGetValue(kvp.Key, out var existing))
                            foreach (string fc in kvp.Value) existing.Add(fc);
                        else
                            _validFuncsForSys[kvp.Key] = kvp.Value;
                    }
                    StingLog.Info($"TagConfig: merged STING_FUNC_SYS_MATRIX.csv → {loaded.Count} SYS entries into _validFuncsForSys");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagConfig: TryLoadValidFuncsFromCsv failed — using hardcoded defaults: {ex.Message}");
            }
        }

        /// <summary>Number of SYS entries in the valid-FUNC lookup table. Used by TagConfig.IsLoaded.</summary>
        internal static int ValidFuncsForSysCount => _validFuncsForSys.Count;

        /// <summary>Phase 39: Comprehensive SYS→FUNC mapping — hardcoded baseline.
        /// Extended at runtime by TryLoadValidFuncsFromCsv() from STING_FUNC_SYS_MATRIX.csv.</summary>
        private static Dictionary<string, HashSet<string>> _validFuncsForSys =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                // Mechanical systems — aligned with STING_FUNC_SYS_MATRIX.csv
                { "HVAC", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SUP", "RTN", "EXH", "FRA", "HTG", "CLG", "VNT", "OA", "TRF", "REL", "GRP", "GEN" } },
                { "HWS",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SUP", "RTN", "HTG", "DHW", "CND", "GEN" } },
                { "DHW",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SUP", "RTN", "DHW", "GEN" } },
                // Plumbing systems
                { "DCW",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SUP", "CLD", "DCW", "GEN" } },
                { "SAN",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SAN", "WAS", "SOI", "VNT", "GEN" } },
                { "RWD",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RWD", "SRF", "ATT", "GEN" } },
                { "GAS",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SUP", "GAS", "MED", "LOW", "GEN" } },
                // Fire protection
                { "FP",   new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SUP", "SYS", "WET", "DRY", "DEL", "PRE", "FOA", "FP", "FLS", "GEN" } },
                // Electrical systems — extended for BS 7671 sub-circuits
                { "LV",   new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PWR", "LTG", "EMG", "SML", "EAR", "UPS", "GNR", "GEN" } },
                { "HV",   new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PWR", "TRF", "GEN" } },
                { "FLS",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FLS", "GEN" } },
                // Fire alarm
                { "FA",   new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DET", "MCP", "SND", "AVD", "IO", "SDA", "LDS", "GEN" } },
                // Communications / low voltage — extended for ICT/AV sub-types
                { "COM",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PA", "TV", "TEL", "AV", "COM", "GEN" } },
                { "ICT",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NET", "WAP", "FBR", "SER", "PTS", "ICT", "COM", "GEN" } },
                { "NCL",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NCS", "EMR", "BDH", "NCL", "GEN" } },
                { "SEC",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CCTV", "ACC", "INT", "DOR", "SEC", "GEN" } },
                { "BMS",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MON", "CTL", "SNS", "FCT", "GEN" } },
                // Medical gas systems (HTM 02-01)
                { "MGS",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "O2", "N2O", "MAP", "VAC", "EVAC", "N2", "CO2", "GEN" } },
                // Lightning protection (BS EN 62305)
                { "LPS",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AIR", "DOW", "ERT", "BND", "SPD", "TST", "GEN" } },
                // Radiation protection (NCRP 147)
                { "RAD",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SHD", "ZNE", "MON", "GEN" } },
                // Architectural / structural / general
                { "ARC",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WLL", "FLR", "CLG", "ROF", "DOR", "WIN", "STR", "RMP", "FAS", "FIT", "GEN" } },
                { "STR",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "COL", "BM", "SLB", "FND", "WLL", "BRC", "TRS", "RBR", "STR", "GEN" } },
                { "GEN",  new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GEN" } },
            };

        /// <summary>Known PROD codes by discipline group for cross-validation.</summary>
        private static readonly Dictionary<string, HashSet<string>> ProdCodesByDisc =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "M", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "AHU", "FCU", "VAV", "CHR", "BLR", "PMP", "FAN", "HRU", "SPL", "GRL",
                    "DAC", "DFT", "DU", "FDU", "IND", "RAD", "DAM", "CLT", "VFD", "SLV", "GEN" } },
                { "E", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "DB", "MCC", "MSB", "SWB", "UPS", "TRF", "ATS", "VFD", "SPD", "RCD",
                    "ISO", "SFS", "BKP", "SKT", "LUM", "LDV", "DWN", "LIN", "SPT", "WSH",
                    "BOL", "UPL", "FLD", "EML", "TRK", "DEC", "CDT", "CFT", "CBLT", "CTF", "SLV", "GEN" } },
                { "P", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "PP", "PFT", "PAC", "FPP", "FIX", "WC", "WHB", "URN", "SNK", "SHW",
                    "BTH", "DRK", "CWL", "TRP", "BID", "EWS", "MOP", "SLV", "GEN" } },
                { "FP", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "SPR", "FAD", "SML", "MCP", "BLL", "STB", "HTD", "FIM", "PP", "PFT",
                    "PAC", "SLV", "GEN" } },
                { "A", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "DR", "WIN", "WL", "FL", "CLG", "RF", "RM", "FUR", "FUS", "CWK",
                    "RLG", "STR", "RMP", "CPN", "MUL", "CWS", "GEN" } },
                { "S", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "COL", "BM", "FDN", "GEN" } },
                { "LV", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "COM", "DAT", "NCL", "SEC", "TEL", "DB", "SKT", "LUM", "LDV",
                    "CDT", "CFT", "CBLT", "CTF", "GEN" } },
            };

        /// <summary>Static lookup for DISC → valid SYS codes. Used in ValidateCrossSystemConsistency.</summary>
        internal static readonly Dictionary<string, HashSet<string>> _validSysForDisc =
            new Dictionary<string, HashSet<string>>
            {
                { "M",  new HashSet<string> { "HVAC", "HWS", "DCW", "DHW", "GAS", "RWD", "SAN" } },
                { "E",  new HashSet<string> { "LV", "FLS", "SEC", "ICT", "COM", "NCL" } },
                { "P",  new HashSet<string> { "DCW", "DHW", "SAN", "RWD", "GAS" } },
                { "FP", new HashSet<string> { "FP", "FLS" } },
                { "A",  new HashSet<string> { "ARC" } },
                { "S",  new HashSet<string> { "STR" } },
                { "LV", new HashSet<string> { "LV", "ICT", "COM", "SEC", "NCL" } },
            };

        /// <summary>
        /// Validate that a PROD code is reasonable for the given DISC.
        /// Returns null if valid, error message if mismatched.
        /// Only flags clear mismatches (e.g., plumbing PROD on an M element).
        /// </summary>
        private static string ValidateProdForDisc(string prod, string disc)
        {
            // GEN is valid for all disciplines
            if (prod == "GEN" || prod == "SPE" || prod == "MED") return null;
            // If we don't have a mapping for this discipline, skip
            if (!ProdCodesByDisc.TryGetValue(disc, out var validProds)) return null;
            // VFD appears in both M and E — skip cross-disc check for shared codes
            if (prod == "VFD" || prod == "PMP") return null;
            // Only flag if the PROD is known to belong exclusively to a different discipline
            if (!validProds.Contains(prod))
            {
                // Check if the PROD belongs to a clearly different discipline
                foreach (var kvp in ProdCodesByDisc)
                {
                    if (kvp.Key != disc && kvp.Value.Contains(prod))
                        return $"PROD '{prod}' typically belongs to DISC '{kvp.Key}', not '{disc}'";
                }
            }
            return null;
        }
    }
}
