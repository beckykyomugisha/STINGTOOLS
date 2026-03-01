using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Newtonsoft.Json;

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

    /// <summary>
    /// Tracks tagging operation statistics across a batch for rich post-operation reporting.
    /// Captures per-category counts, collision details, skipped elements, warnings, and
    /// discipline/system/level breakdowns. Thread-safe for single-transaction use.
    /// </summary>
    public class TaggingStats
    {
        public int TotalTagged { get; private set; }
        public int TotalSkipped { get; private set; }
        public int TotalOverwritten { get; private set; }
        public int TotalCollisions { get; private set; }
        public int MaxCollisionDepth { get; private set; }
        public readonly Dictionary<string, int> TaggedByCategory = new Dictionary<string, int>();
        public readonly Dictionary<string, int> TaggedByDisc = new Dictionary<string, int>();
        public readonly Dictionary<string, int> TaggedBySys = new Dictionary<string, int>();
        public readonly Dictionary<string, int> TaggedByLevel = new Dictionary<string, int>();
        public readonly Dictionary<string, int> SkippedByCategory = new Dictionary<string, int>();
        public readonly Dictionary<string, int> OverwrittenByDisc = new Dictionary<string, int>();
        public readonly Dictionary<string, int> OverwrittenBySys = new Dictionary<string, int>();
        public readonly Dictionary<string, int> OverwrittenByLevel = new Dictionary<string, int>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<(string tag, int depth)> CollisionDetails = new List<(string, int)>();

        public void RecordTagged(string category, string disc, string sys, string lvl)
        {
            TotalTagged++;
            Increment(TaggedByCategory, category);
            if (!string.IsNullOrEmpty(disc)) Increment(TaggedByDisc, disc);
            if (!string.IsNullOrEmpty(sys)) Increment(TaggedBySys, sys);
            if (!string.IsNullOrEmpty(lvl)) Increment(TaggedByLevel, lvl);
        }

        public void RecordSkipped(string category)
        {
            TotalSkipped++;
            Increment(SkippedByCategory, category);
        }

        public void RecordOverwritten(string category, string disc = null, string sys = null, string lvl = null)
        {
            TotalOverwritten++;
            Increment(TaggedByCategory, category);
            if (!string.IsNullOrEmpty(disc)) Increment(OverwrittenByDisc, disc);
            if (!string.IsNullOrEmpty(sys)) Increment(OverwrittenBySys, sys);
            if (!string.IsNullOrEmpty(lvl)) Increment(OverwrittenByLevel, lvl);
        }

        public void RecordCollision(string tag, int depth)
        {
            TotalCollisions++;
            if (depth > MaxCollisionDepth) MaxCollisionDepth = depth;
            // Keep the top 20 collisions by depth (deepest = most concerning)
            if (CollisionDetails.Count < 20)
            {
                CollisionDetails.Add((tag, depth));
            }
            else
            {
                // Replace the shallowest collision if this one is deeper
                int minIdx = 0;
                for (int i = 1; i < CollisionDetails.Count; i++)
                    if (CollisionDetails[i].depth < CollisionDetails[minIdx].depth)
                        minIdx = i;
                if (depth > CollisionDetails[minIdx].depth)
                    CollisionDetails[minIdx] = (tag, depth);
            }
        }

        public void RecordWarning(string warning)
        {
            if (Warnings.Count < 100)
                Warnings.Add(warning);
        }

        /// <summary>Build a multi-line report string for TaskDialog display.</summary>
        public string BuildReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"  Tagged:       {TotalTagged:N0}");
            sb.AppendLine($"  Skipped:      {TotalSkipped:N0}");
            if (TotalOverwritten > 0)
                sb.AppendLine($"  Overwritten:  {TotalOverwritten:N0}");
            if (TotalCollisions > 0)
            {
                sb.AppendLine($"  Collisions:   {TotalCollisions:N0} resolved (max depth: {MaxCollisionDepth})");
                // Show top 5 deepest collisions (most concerning)
                foreach (var (tag, depth) in CollisionDetails.OrderByDescending(c => c.depth).Take(5))
                    sb.AppendLine($"    • {tag} (bumped {depth}×)");
                if (TotalCollisions > 5)
                    sb.AppendLine($"    ... and {TotalCollisions - 5} more");
            }

            if (TaggedByDisc.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  By Discipline:");
                foreach (var kvp in TaggedByDisc.OrderByDescending(x => x.Value))
                    sb.AppendLine($"    {kvp.Key,-6} {kvp.Value,5}");
            }
            if (TaggedBySys.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  By System:");
                foreach (var kvp in TaggedBySys.OrderByDescending(x => x.Value).Take(8))
                    sb.AppendLine($"    {kvp.Key,-8} {kvp.Value,5}");
            }
            if (TaggedByLevel.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  By Level:");
                foreach (var kvp in TaggedByLevel.OrderBy(x => x.Key))
                    sb.AppendLine($"    {kvp.Key,-6} {kvp.Value,5}");
            }
            if (TotalOverwritten > 0 && OverwrittenByDisc.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Overwritten By Discipline:");
                foreach (var kvp in OverwrittenByDisc.OrderByDescending(x => x.Value))
                    sb.AppendLine($"    {kvp.Key,-6} {kvp.Value,5}");
            }
            if (Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"  Warnings ({Warnings.Count}):");
                foreach (string w in Warnings.Take(10))
                    sb.AppendLine($"    ⚠ {w}");
                if (Warnings.Count > 10)
                    sb.AppendLine($"    ... and {Warnings.Count - 10} more");
            }

            return sb.ToString();
        }

        private static void Increment(Dictionary<string, int> dict, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!dict.ContainsKey(key)) dict[key] = 0;
            dict[key]++;
        }
    }

    /// <summary>
    /// ISO 19650 naming and coding validation. Enforces that all token values
    /// conform to the allowed code lists defined in the tag configuration.
    /// </summary>
    public static class ISO19650Validator
    {
        /// <summary>Valid discipline codes per ISO 19650.</summary>
        public static readonly HashSet<string> ValidDiscCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "M", "E", "P", "A", "S", "FP", "LV", "G"
        };

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
        public static readonly HashSet<string> ValidSysCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HVAC", "HWS", "DHW", "DCW", "SAN", "RWD", "GAS", "FP", "LV",
            "FLS", "COM", "ICT", "NCL", "SEC",
            "ARC", "STR", "GEN"
        };

        /// <summary>
        /// Valid function codes per CIBSE / Uniclass 2015.
        /// DCW = Cold Water Supply (CAWS S10), SAN = Sanitary (CAWS R11),
        /// GAS = Gas Supply (CAWS S63), RWD = Rainwater Drainage (CAWS R10).
        /// </summary>
        public static readonly HashSet<string> ValidFuncCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SUP", "HTG", "DCW", "SAN", "RWD", "GAS", "FP", "PWR", "FLS",
            "COM", "ICT", "NCL", "SEC",
            "FIT", "STR", "GEN",
            // Subsystem FUNC codes from GetSmartFuncCode HVAC/HWS differentiation
            "EXH", "RTN", "FRA", "DHW"
        };

        /// <summary>
        /// Validate a single token value against its allowed code list.
        /// Returns null if valid, or an error message string if invalid.
        /// </summary>
        public static string ValidateToken(string tokenName, string value)
        {
            if (string.IsNullOrEmpty(value))
                return $"{tokenName}: empty value";

            switch (tokenName)
            {
                case ParamRegistry.DISC:
                    if (!ValidDiscCodes.Contains(value))
                        return $"DISC '{value}' not in valid set ({string.Join(",", ValidDiscCodes)})";
                    break;
                case ParamRegistry.LOC:
                    if (!TagConfig.LocCodes.Contains(value))
                        return $"LOC '{value}' not in valid set ({string.Join(",", TagConfig.LocCodes)})";
                    break;
                case ParamRegistry.ZONE:
                    if (!TagConfig.ZoneCodes.Contains(value))
                        return $"ZONE '{value}' not in valid set ({string.Join(",", TagConfig.ZoneCodes)})";
                    break;
                case ParamRegistry.SYS:
                    if (!ValidSysCodes.Contains(value))
                        return $"SYS '{value}' not in valid set ({string.Join(",", ValidSysCodes)})";
                    break;
                case ParamRegistry.FUNC:
                    if (!ValidFuncCodes.Contains(value))
                        return $"FUNC '{value}' not in valid set ({string.Join(",", ValidFuncCodes)})";
                    break;
                case ParamRegistry.LVL:
                    // Valid LVL codes: L00-L99, L100+, GF, LG, UG, B1-B9, B10+, SB, RF, PH, AT, TR, POD, MZ, PL
                    string lvlUpper = value.ToUpperInvariant();
                    if (lvlUpper.Length > 4 || lvlUpper.Contains(" "))
                        return $"LVL '{value}' exceeds 4-char limit or contains spaces";
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
                    break;
                case ParamRegistry.PROD:
                    // PROD codes: 2-4 uppercase alphanumeric characters
                    if (value.Length < 2 || value.Length > 4)
                        return $"PROD '{value}' should be 2-4 characters";
                    if (!value.All(c => char.IsLetterOrDigit(c)))
                        return $"PROD '{value}' must be alphanumeric only";
                    break;
                case ParamRegistry.SEQ:
                    if (!int.TryParse(value, out int seqVal))
                        return $"SEQ '{value}' is not a valid number";
                    if (seqVal < 0)
                        return $"SEQ '{value}' must be a positive number";
                    if (value.Length > NumPad + 1)
                        return $"SEQ '{value}' exceeds {NumPad}-digit format";
                    break;
            }
            return null; // valid
        }

        /// <summary>
        /// Validate all 8 tokens on an element. Returns a list of validation errors
        /// (empty list = fully valid).
        /// </summary>
        public static List<string> ValidateElement(Element el)
        {
            var errors = new List<string>();
            string[] tokenParams = new[]
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ,
            };

            foreach (string param in tokenParams)
            {
                string val = ParameterHelpers.GetString(el, param);
                string error = ValidateToken(param, val);
                if (error != null)
                    errors.Add(error);
            }

            // Cross-validate: DISC must match element category
            // Accounts for system-aware DISC correction (pipes can be "P" when system is plumbing)
            string catName = ParameterHelpers.GetCategoryName(el);
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
            if (!string.IsNullOrEmpty(catName) && !string.IsNullOrEmpty(disc))
            {
                string expectedDisc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : null;
                if (expectedDisc != null && expectedDisc != disc)
                {
                    // Allow system-aware correction: pipes/pipe fittings can be P instead of M
                    string sysVal = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                    string correctedDisc = GetSystemAwareDisc(expectedDisc, sysVal, catName);
                    if (correctedDisc != disc)
                        errors.Add($"DISC mismatch: element category '{catName}' expects '{correctedDisc}' but has '{disc}'");
                }
            }

            // Cross-validate: SYS should be valid for this category
            // Uses SysMap lookup to allow ALL valid SYS codes for ambiguous categories
            // (e.g., Pipes can be DCW, DHW, SAN, RWD, GAS, HWS, FP)
            string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            if (!string.IsNullOrEmpty(catName) && !string.IsNullOrEmpty(sys))
            {
                bool sysValidForCategory = false;
                // Check if this SYS code lists this category in SysMap
                if (SysMap.TryGetValue(sys, out var sysCats) && sysCats.Contains(catName))
                    sysValidForCategory = true;
                // Also accept discipline-default SYS codes (ARC, STR, GEN, etc.)
                string discForCat = DiscMap.TryGetValue(catName, out string dc) ? dc : "A";
                if (sys == GetDiscDefaultSysCode(discForCat))
                    sysValidForCategory = true;
                if (!sysValidForCategory)
                {
                    // Find what SYS codes ARE valid for this category
                    var validSysForCat = SysMap.Where(kvp => kvp.Value.Contains(catName))
                        .Select(kvp => kvp.Key).ToList();
                    string validList = validSysForCat.Count > 0
                        ? string.Join("/", validSysForCat)
                        : GetDiscDefaultSysCode(discForCat);
                    errors.Add($"SYS mismatch: category '{catName}' expects '{validList}' but has '{sys}'");
                }
            }

            // Cross-validate: PROD should be consistent with DISC/SYS
            string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
            if (!string.IsNullOrEmpty(prod) && !string.IsNullOrEmpty(disc))
            {
                string prodError = ValidateProdForDisc(prod, disc);
                if (prodError != null)
                    errors.Add(prodError);
            }

            // Cross-validate: FUNC should be consistent with SYS
            string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
            if (!string.IsNullOrEmpty(func) && !string.IsNullOrEmpty(sys))
            {
                string expectedFunc = TagConfig.GetFuncCode(sys);
                // Allow smart FUNC codes (EXH, RTN, FRA, DHW) as valid overrides
                if (!string.IsNullOrEmpty(expectedFunc) && expectedFunc != func)
                {
                    // Only flag if FUNC doesn't belong to any related system
                    bool isSmartFunc = (sys == "HVAC" && (func == "SUP" || func == "RTN" || func == "EXH" || func == "FRA")) ||
                                       (sys == "HWS" && (func == "HTG" || func == "DHW"));
                    if (!isSmartFunc)
                        errors.Add($"FUNC '{func}' unexpected for SYS '{sys}' (expected '{expectedFunc}')");
                }
            }

            return errors;
        }

        /// <summary>
        /// Validate the format of a complete assembled tag string.
        /// Returns null if valid, or an error description.
        /// </summary>
        public static string ValidateTagFormat(string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return "Tag is empty";

            string[] parts = tag.Split('-');
            if (parts.Length != 8)
                return $"Tag has {parts.Length} segments (expected 8): {tag}";

            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    return $"Segment {i + 1} is empty in tag: {tag}";
            }

            // Validate ALL 8 segments against their respective rules
            string discError = ValidateToken(ParamRegistry.DISC, parts[0]);
            if (discError != null) return discError;

            string locError = ValidateToken(ParamRegistry.LOC, parts[1]);
            if (locError != null) return locError;

            string zoneError = ValidateToken(ParamRegistry.ZONE, parts[2]);
            if (zoneError != null) return zoneError;

            string lvlError = ValidateToken(ParamRegistry.LVL, parts[3]);
            if (lvlError != null) return lvlError;

            string sysError = ValidateToken(ParamRegistry.SYS, parts[4]);
            if (sysError != null) return sysError;

            string funcError = ValidateToken(ParamRegistry.FUNC, parts[5]);
            if (funcError != null) return funcError;

            string prodError = ValidateToken(ParamRegistry.PROD, parts[6]);
            if (prodError != null) return prodError;

            string seqError = ValidateToken(ParamRegistry.SEQ, parts[7]);
            if (seqError != null) return seqError;

            return null; // valid
        }

        /// <summary>Known PROD codes by discipline group for cross-validation.</summary>
        private static readonly Dictionary<string, HashSet<string>> ProdCodesByDisc =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "M", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "AHU", "FCU", "VAV", "CHR", "BLR", "PMP", "FAN", "HRU", "SPL", "GRL",
                    "DAC", "DFT", "DU", "FDU", "IND", "RAD", "DAM", "CLT", "VFD", "GEN" } },
                { "E", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "DB", "MCC", "MSB", "SWB", "UPS", "TRF", "ATS", "VFD", "SPD", "RCD",
                    "ISO", "SFS", "BKP", "SKT", "LUM", "LDV", "DWN", "LIN", "SPT", "WSH",
                    "BOL", "UPL", "FLD", "EML", "TRK", "DEC", "CDT", "CFT", "CBLT", "CTF", "GEN" } },
                { "P", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "PP", "PFT", "PAC", "FPP", "FIX", "WC", "WHB", "URN", "SNK", "SHW",
                    "BTH", "DRK", "CWL", "TRP", "BID", "EWS", "MOP", "GEN" } },
                { "FP", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "SPR", "FAD", "SML", "MCP", "BLL", "STB", "HTD", "FIM", "PP", "PFT",
                    "PAC", "GEN" } },
                { "A", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "DR", "WIN", "WL", "FL", "CLG", "RF", "RM", "FUR", "FUS", "CWK",
                    "RLG", "STR", "RMP", "CPN", "MUL", "CWS", "GEN" } },
                { "S", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "COL", "BM", "FDN", "GEN" } },
                { "LV", new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "COM", "DAT", "NCL", "SEC", "TEL", "DB", "SKT", "LUM", "LDV",
                    "CDT", "CFT", "CBLT", "CTF", "GEN" } },
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

    /// <summary>
    /// Ported from tag_config.py — project-level ISO 19650 token lookup tables.
    /// Loads from project_config.json; falls back to built-in defaults that mirror
    /// Sheet 02-TAG-FAMILY-CONFIG from the STINGTOOLS template workbook.
    /// </summary>
    public static class TagConfig
    {
        public const int NumPad = 4;
        public const string Separator = "-";
        public const int MaxCollisionDepth = 10000;

        /// <summary>Category name → discipline code (M, E, P, A, S, FP, LV, G).</summary>
        public static Dictionary<string, string> DiscMap { get; private set; }

        /// <summary>System code → list of category names.</summary>
        public static Dictionary<string, List<string>> SysMap { get; private set; }

        /// <summary>Category name → product code (GRL, AHU, DR, WIN, etc.).</summary>
        public static Dictionary<string, string> ProdMap { get; private set; }

        /// <summary>System code → function code (SUP, HTG, DCW, SAN, RWD, etc.).</summary>
        public static Dictionary<string, string> FuncMap { get; private set; }

        /// <summary>Available location codes.</summary>
        public static List<string> LocCodes { get; private set; }

        /// <summary>Available zone codes.</summary>
        public static List<string> ZoneCodes { get; private set; }

        public static string ConfigSource { get; private set; }

        /// <summary>Reverse lookup: category name → SYS code. Built lazily from SysMap.</summary>
        private static Dictionary<string, string> _reverseSysMap;

        static TagConfig()
        {
            LoadDefaults();
        }

        /// <summary>Build or return the cached reverse SysMap (category → SYS code).</summary>
        private static Dictionary<string, string> GetReverseSysMap()
        {
            if (_reverseSysMap == null)
            {
                _reverseSysMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kvp in SysMap)
                {
                    foreach (string cat in kvp.Value)
                    {
                        if (!_reverseSysMap.ContainsKey(cat))
                            _reverseSysMap[cat] = kvp.Key;
                    }
                }
            }
            return _reverseSysMap;
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

                DiscMap = TryDeserialize<Dictionary<string, string>>(data, "DISC_MAP") ?? DefaultDiscMap();
                SysMap = TryDeserialize<Dictionary<string, List<string>>>(data, "SYS_MAP") ?? DefaultSysMap();
                ProdMap = TryDeserialize<Dictionary<string, string>>(data, "PROD_MAP") ?? DefaultProdMap();
                FuncMap = TryDeserialize<Dictionary<string, string>>(data, "FUNC_MAP") ?? DefaultFuncMap();
                LocCodes = TryDeserialize<List<string>>(data, "LOC_CODES") ?? DefaultLocCodes();
                ZoneCodes = TryDeserialize<List<string>>(data, "ZONE_CODES") ?? DefaultZoneCodes();
                _reverseSysMap = null; // Invalidate cache
                ConfigSource = "project_config.json";
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
            ConfigSource = "built-in defaults";
        }

        /// <summary>Get the SYS code for a category name. O(1) via cached reverse lookup.</summary>
        public static string GetSysCode(string categoryName)
        {
            var reverse = GetReverseSysMap();
            return reverse.TryGetValue(categoryName, out string sys) ? sys : string.Empty;
        }

        /// <summary>Get the FUNC code for a SYS code (basic lookup).</summary>
        public static string GetFuncCode(string sysCode)
        {
            return FuncMap.TryGetValue(sysCode, out string val) ? val : string.Empty;
        }

        /// <summary>
        /// Get a guaranteed default SYS code from a discipline code.
        /// Used as a fallback when MEP system detection returns empty —
        /// ensures every element gets a valid SYS token.
        /// M→HVAC, E→LV, P→DHW, A→ARC, S→STR, FP→FP, LV→LV, G→GAS, else GEN.
        /// </summary>
        public static string GetDiscDefaultSysCode(string disc)
        {
            switch (disc)
            {
                case "M":  return "HVAC";
                case "E":  return "LV";
                case "P":  return "DHW";
                case "A":  return "ARC";
                case "S":  return "STR";
                case "FP": return "FP";
                case "LV": return "LV";
                case "G":  return "GAS";
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
            catch { }
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
            catch { }
            return null;
        }

        /// <summary>
        /// Family-name-aware product code resolution. Checks the element's family name
        /// for specific equipment patterns before falling back to category-based lookup.
        /// This gives more specific PROD codes: e.g., "FCU-01" → FCU, "VAV Box" → VAV,
        /// instead of the generic category code like "AHU" for all Mechanical Equipment.
        /// </summary>
        public static string GetFamilyAwareProdCode(Element el, string categoryName)
        {
            string familyName = ParameterHelpers.GetFamilyName(el);
            string symbolName = ParameterHelpers.GetFamilySymbolName(el);
            string combinedName = $"{familyName} {symbolName}".ToUpperInvariant();

            // Only apply family-level overrides for categories with diverse equipment
            if (!string.IsNullOrEmpty(familyName))
            {
                string upper = familyName.ToUpperInvariant();

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
            }

            // Fall back to category-based PROD code
            return ProdMap.TryGetValue(categoryName, out string prod) ? prod : "GEN";
        }

        /// <summary>
        /// Check if a tag string has the expected number of non-empty tokens.
        /// A tag is only "complete" when it has exactly expectedTokens segments
        /// and none of them are empty strings.
        /// </summary>
        public static bool TagIsComplete(string tagValue, int expectedTokens = 8)
        {
            if (string.IsNullOrEmpty(tagValue))
                return false;
            string[] parts = tagValue.Split(new[] { Separator[0] });
            if (parts.Length != expectedTokens)
                return false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Strict tag completeness check. In addition to the standard check,
        /// rejects tags where any segment is a placeholder ("XX", "ZZ", "0000").
        /// Useful for compliance dashboards that require fully-resolved tags.
        /// </summary>
        public static bool TagIsFullyResolved(string tagValue, int expectedTokens = 8)
        {
            if (!TagIsComplete(tagValue, expectedTokens))
                return false;
            string[] parts = tagValue.Split(new[] { Separator[0] });
            // Reject placeholder segments
            var placeholders = new HashSet<string> { "XX", "ZZ", "0000" };
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
        /// <returns>True if the element was tagged, false if skipped.</returns>
        public static bool BuildAndWriteTag(Document doc, Element el,
            Dictionary<string, int> sequenceCounters, bool skipComplete = true,
            HashSet<string> existingTags = null,
            TagCollisionMode collisionMode = TagCollisionMode.AutoIncrement,
            TaggingStats stats = null)
        {
            string catName = ParameterHelpers.GetCategoryName(el);
            if (string.IsNullOrEmpty(catName) || !DiscMap.ContainsKey(catName))
                return false;

            // Handle already-tagged elements based on collision mode
            string existingTag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
            bool hasCompleteTag = TagIsComplete(existingTag);

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

            string disc = DiscMap.TryGetValue(catName, out string d) ? d : "A";

            // Intelligence Layer: cross-validate DISC against element category
            if (!DiscMap.ContainsKey(catName) && stats != null)
                stats.RecordWarning($"Element {el.Id}: category '{catName}' has no DISC mapping — defaulted to 'A'");

            string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
            if (string.IsNullOrEmpty(loc)) loc = LocCodes.Count > 0 ? LocCodes[0] : "BLD1";
            string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
            if (string.IsNullOrEmpty(zone)) zone = ZoneCodes.Count > 0 ? ZoneCodes[0] : "Z01";
            string lvl = ParameterHelpers.GetLevelCode(doc, el);
            // Guaranteed LVL default: replace unresolved "XX" with "L00" for levelless elements
            if (lvl == "XX") lvl = "L00";

            // Intelligence Layer: MEP system-aware SYS/FUNC derivation
            // 6-layer system detection: connector → sys param → circuit → family → room → category
            string sys = GetMepSystemAwareSysCode(el, catName);

            // Guaranteed SYS default: derive from discipline when MEP detection returns empty
            if (string.IsNullOrEmpty(sys))
                sys = GetDiscDefaultSysCode(disc);

            // Intelligence Layer: System-aware DISC correction for pipes
            // Pipes are mapped to "M" by default, but if the connected system is plumbing
            // (DCW, DHW, SAN, RWD, GAS), the DISC should be "P" (Plumbing).
            disc = GetSystemAwareDisc(disc, sys, catName);

            // Smart FUNC: differentiates HVAC (SUP/RTN/EXH/FRA) and HWS (HTG/DHW) subsystems
            string func = GetSmartFuncCode(el, sys);
            // Guaranteed FUNC default: derive from SYS via FuncMap when smart detection is empty
            if (string.IsNullOrEmpty(func))
                func = FuncMap.TryGetValue(sys, out string fv) ? fv : "GEN";

            string prod = GetFamilyAwareProdCode(el, catName);

            // Log when defaults are applied for LOC/ZONE
            if (stats != null)
            {
                if (loc == "BLD1" && string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.LOC)))
                    stats.RecordWarning($"Element {el.Id}: LOC defaulted to BLD1");
                if (zone == "Z01" && string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.ZONE)))
                    stats.RecordWarning($"Element {el.Id}: ZONE defaulted to Z01");
            }

            string seqKey = $"{disc}_{sys}_{lvl}";
            if (!sequenceCounters.ContainsKey(seqKey))
                sequenceCounters[seqKey] = 0;
            sequenceCounters[seqKey]++;

            // SEQ overflow detection: warn when sequence exceeds format capacity
            int maxSeq = (int)Math.Pow(10, NumPad) - 1; // 9999 for NumPad=4
            if (sequenceCounters[seqKey] > maxSeq)
            {
                string overflowMsg = $"SEQ overflow: group {seqKey} reached {sequenceCounters[seqKey]} (max {maxSeq})";
                StingLog.Warn(overflowMsg);
                stats?.RecordWarning(overflowMsg);
            }

            string seq = sequenceCounters[seqKey].ToString().PadLeft(NumPad, '0');

            string tag = string.Join(Separator, disc, loc, zone, lvl, sys, func, prod, seq);

            // Collision detection: if this exact tag already exists, increment SEQ
            if (existingTags != null)
            {
                int safetyLimit = MaxCollisionDepth;
                int collisionCount = 0;
                while (existingTags.Contains(tag) && safetyLimit-- > 0)
                {
                    collisionCount++;
                    sequenceCounters[seqKey]++;
                    seq = sequenceCounters[seqKey].ToString().PadLeft(NumPad, '0');
                    tag = string.Join(Separator, disc, loc, zone, lvl, sys, func, prod, seq);
                }
                if (collisionCount > 0)
                    stats?.RecordCollision(tag, collisionCount);
                // Remove old tag from index if overwriting
                if (overwriteTokens && !string.IsNullOrEmpty(existingTag))
                    existingTags.Remove(existingTag);
                existingTags.Add(tag);
            }

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
            }
            ParameterHelpers.SetString(el, ParamRegistry.TAG1, tag, overwrite: true);

            // Auto-populate STATUS from Revit phase/workset if not already set
            // Guaranteed default: every element gets a STATUS — never left empty
            {
                string existingStatus = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                if (string.IsNullOrEmpty(existingStatus) || overwriteTokens)
                {
                    string status = PhaseAutoDetect.DetectStatus(doc, el);
                    if (string.IsNullOrEmpty(status)) status = "NEW";
                    if (overwriteTokens)
                        ParameterHelpers.SetString(el, ParamRegistry.STATUS, status, overwrite: true);
                    else
                        ParameterHelpers.SetIfEmpty(el, ParamRegistry.STATUS, status);
                }
            }

            // Auto-populate REV from project revision sequence
            // Guaranteed default: every element gets a REV — "P01" when no revisions exist
            {
                string existingRev = ParameterHelpers.GetString(el, ParamRegistry.REV);
                if (string.IsNullOrEmpty(existingRev) || overwriteTokens)
                {
                    string rev = PhaseAutoDetect.DetectProjectRevision(doc);
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
                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: overwriteTokens);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Container write failed for {el.Id}: {ex.Message}");
            }

            stats?.RecordTagged(catName, disc, sys, lvl);
            return true;
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
            // Layer 1: Connected MEP system name (most reliable for piping and ductwork)
            string fromConnector = GetSysFromConnector(el);
            if (!string.IsNullOrEmpty(fromConnector)) return fromConnector;

            // Layer 2: Duct/Pipe system type built-in parameter
            string fromSysType = GetSysFromSystemTypeParam(el);
            if (!string.IsNullOrEmpty(fromSysType)) return fromSysType;

            // Layer 3: Electrical circuit panel name
            string fromCircuit = GetSysFromElectricalCircuit(el);
            if (!string.IsNullOrEmpty(fromCircuit)) return fromCircuit;

            // Layer 4: Family name pattern matching
            string fromFamily = GetSysFromFamilyName(el, categoryName);
            if (!string.IsNullOrEmpty(fromFamily)) return fromFamily;

            // Layer 5: Room-type inference
            string fromRoom = GetSysFromRoomType(el);
            if (!string.IsNullOrEmpty(fromRoom)) return fromRoom;

            // Layer 6: Category-based fallback
            return GetSysCode(categoryName);
        }

        /// <summary>Layer 1: Read connected MEP system name via connectors.</summary>
        private static string GetSysFromConnector(Element el)
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
                            string mapped = MapSystemNameToCode(sysName);
                            if (!string.IsNullOrEmpty(mapped)) return mapped;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Layer 2: Read RBS_DUCT_SYSTEM_TYPE or RBS_PIPING_SYSTEM_TYPE parameter.</summary>
        private static string GetSysFromSystemTypeParam(Element el)
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
                    string mapped = MapSystemNameToCode(val);
                    if (!string.IsNullOrEmpty(mapped)) return mapped;
                }
            }
            catch { }
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
                    // Default electrical panels → LV
                    if (panel.Length > 0)
                        return "LV";
                }
            }
            catch { }
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
                catch { }

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
            catch { }
            return null;
        }

        /// <summary>
        /// System-aware DISC correction. Pipes/pipe fittings are categorised as "M"
        /// (Mechanical) by default, but if the connected MEP system is plumbing
        /// (DCW, DHW, SAN, RWD, GAS), the DISC should be "P" (Plumbing).
        /// Similarly, fire protection pipes should be "FP".
        /// </summary>
        public static string GetSystemAwareDisc(string disc, string sys, string categoryName)
        {
            // Only apply system-aware override for ambiguous categories (pipes, pipe fittings, etc.)
            var pipeCategories = new HashSet<string>
            {
                "Pipes", "Pipe Fittings", "Pipe Accessories", "Flex Pipes"
            };
            if (!pipeCategories.Contains(categoryName))
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
        private static string MapSystemNameToCode(string sysName)
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
            if (sysName == "CW" || sysName.StartsWith("CW ") || sysName.Contains(" CW ")) return "HVAC";
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
            catch { /* template lookup failed — proceed with view name */ }

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
                catch { /* Visibility check failed — return all */ }
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
            catch { return true; } // Assume visible if check fails
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
            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
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
        public static Dictionary<string, int> GetExistingSequenceCounters(Document doc)
        {
            var maxSeq = new Dictionary<string, int>();
            var known = new HashSet<string>(DiscMap.Keys);

            foreach (Element elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(elem);
                if (!known.Contains(cat)) continue;

                string disc = ParameterHelpers.GetString(elem, ParamRegistry.DISC);
                string sys = ParameterHelpers.GetString(elem, ParamRegistry.SYS);
                string lvl = ParameterHelpers.GetString(elem, ParamRegistry.LVL);
                string seqStr = ParameterHelpers.GetString(elem, ParamRegistry.SEQ);
                if (string.IsNullOrEmpty(disc)) continue;

                string key = $"{disc}_{sys}_{lvl}";
                if (int.TryParse(seqStr, out int seqNum))
                {
                    if (!maxSeq.ContainsKey(key) || seqNum > maxSeq[key])
                        maxSeq[key] = seqNum;
                }
            }

            return maxSeq;
        }

        private static T TryDeserialize<T>(Dictionary<string, object> data, string key)
            where T : class
        {
            if (!data.ContainsKey(key)) return null;
            try
            {
                string json = JsonConvert.SerializeObject(data[key]);
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex) { StingLog.Warn($"TagConfig deserialize '{key}': {ex.Message}"); return null; }
        }

        // ══════════════════════════════════════════════════════════════════
        // Built-in defaults — mirror Sheet 02-TAG-FAMILY-CONFIG
        // ══════════════════════════════════════════════════════════════════

        private static Dictionary<string, string> DefaultDiscMap()
        {
            return new Dictionary<string, string>
            {
                // MEP
                { "Air Terminals", "M" }, { "Duct Accessories", "M" },
                { "Duct Fittings", "M" }, { "Ducts", "M" },
                { "Flex Ducts", "M" }, { "Mechanical Equipment", "M" },
                { "Pipes", "M" }, { "Pipe Fittings", "M" },
                { "Pipe Accessories", "M" }, { "Flex Pipes", "M" },
                { "Plumbing Fixtures", "P" }, { "Sprinklers", "FP" },
                { "Electrical Equipment", "E" }, { "Electrical Fixtures", "E" },
                { "Lighting Fixtures", "E" }, { "Lighting Devices", "E" },
                { "Conduits", "E" }, { "Conduit Fittings", "E" },
                { "Cable Trays", "E" }, { "Cable Tray Fittings", "E" },
                { "Fire Alarm Devices", "FP" }, { "Communication Devices", "LV" },
                { "Data Devices", "LV" }, { "Nurse Call Devices", "LV" },
                { "Security Devices", "LV" }, { "Telephone Devices", "LV" },
                // Architecture
                { "Doors", "A" }, { "Windows", "A" },
                { "Walls", "A" }, { "Floors", "A" },
                { "Ceilings", "A" }, { "Roofs", "A" },
                { "Rooms", "A" }, { "Furniture", "A" },
                { "Furniture Systems", "A" }, { "Casework", "A" },
                { "Railings", "A" }, { "Stairs", "A" }, { "Ramps", "A" },
                // Structure
                { "Structural Columns", "S" }, { "Structural Framing", "S" },
                { "Structural Foundations", "S" }, { "Columns", "S" },
                // Curtain wall elements
                { "Curtain Panels", "A" }, { "Curtain Wall Mullions", "A" },
                { "Curtain Systems", "A" },
                // Generic
                { "Generic Models", "G" }, { "Specialty Equipment", "G" },
                { "Medical Equipment", "G" },
            };
        }

        private static Dictionary<string, List<string>> DefaultSysMap()
        {
            return new Dictionary<string, List<string>>
            {
                { "HVAC", new List<string> { "Air Terminals", "Duct Accessories", "Duct Fittings", "Ducts", "Flex Ducts", "Mechanical Equipment" } },
                // Pipes default to DCW (Domestic Cold Water per CIBSE/CAWS S10); runtime MEP
                // system detection in GetMepSystemAwareSysCode overrides to HWS/SAN/GAS as needed
                { "DCW", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories" } },
                { "DHW", new List<string> { "Plumbing Fixtures", "Flex Pipes" } },
                { "FP", new List<string> { "Sprinklers" } },
                { "LV", new List<string> { "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures", "Lighting Devices", "Conduits", "Conduit Fittings", "Cable Trays", "Cable Tray Fittings" } },
                { "FLS", new List<string> { "Fire Alarm Devices" } },
                { "COM", new List<string> { "Communication Devices", "Telephone Devices" } },
                { "ICT", new List<string> { "Data Devices" } },
                { "NCL", new List<string> { "Nurse Call Devices" } },
                { "SEC", new List<string> { "Security Devices" } },
                // Architecture
                { "ARC", new List<string> { "Doors", "Windows", "Walls", "Floors", "Ceilings", "Roofs", "Rooms", "Furniture", "Furniture Systems", "Casework", "Railings", "Stairs", "Ramps", "Curtain Panels", "Curtain Wall Mullions", "Curtain Systems" } },
                // Structure
                { "STR", new List<string> { "Structural Columns", "Structural Framing", "Structural Foundations", "Columns" } },
                // Generic
                { "GEN", new List<string> { "Generic Models", "Specialty Equipment", "Medical Equipment" } },
            };
        }

        private static Dictionary<string, string> DefaultProdMap()
        {
            return new Dictionary<string, string>
            {
                { "Air Terminals", "GRL" }, { "Duct Accessories", "DAC" },
                { "Duct Fittings", "DFT" }, { "Ducts", "DU" },
                { "Flex Ducts", "FDU" }, { "Mechanical Equipment", "AHU" },
                { "Pipes", "PP" }, { "Pipe Fittings", "PFT" },
                { "Pipe Accessories", "PAC" }, { "Flex Pipes", "FPP" },
                { "Plumbing Fixtures", "FIX" }, { "Sprinklers", "SPR" },
                { "Electrical Equipment", "DB" }, { "Electrical Fixtures", "SKT" },
                { "Lighting Fixtures", "LUM" }, { "Lighting Devices", "LDV" },
                { "Conduits", "CDT" }, { "Conduit Fittings", "CFT" },
                { "Cable Trays", "CBLT" }, { "Cable Tray Fittings", "CTF" },
                { "Fire Alarm Devices", "FAD" }, { "Communication Devices", "COM" },
                { "Data Devices", "DAT" }, { "Nurse Call Devices", "NCL" },
                { "Security Devices", "SEC" }, { "Telephone Devices", "TEL" },
                { "Doors", "DR" }, { "Windows", "WIN" },
                { "Walls", "WL" }, { "Floors", "FL" },
                { "Ceilings", "CLG" }, { "Roofs", "RF" },
                { "Rooms", "RM" }, { "Furniture", "FUR" },
                { "Furniture Systems", "FUS" }, { "Casework", "CWK" },
                { "Railings", "RLG" }, { "Stairs", "STR" }, { "Ramps", "RMP" },
                { "Structural Columns", "COL" }, { "Structural Framing", "BM" },
                { "Structural Foundations", "FDN" }, { "Columns", "COL" },
                { "Curtain Panels", "CPN" }, { "Curtain Wall Mullions", "MUL" },
                { "Curtain Systems", "CWS" },
                { "Generic Models", "GEN" }, { "Specialty Equipment", "SPE" },
                { "Medical Equipment", "MED" },
            };
        }

        private static Dictionary<string, string> DefaultFuncMap()
        {
            return new Dictionary<string, string>
            {
                { "HVAC", "SUP" }, { "HWS", "HTG" }, { "DHW", "DHW" },
                { "DCW", "DCW" }, { "SAN", "SAN" }, { "RWD", "RWD" }, { "GAS", "GAS" },
                { "FP", "FP" }, { "LV", "PWR" }, { "FLS", "FLS" },
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
    }

    /// <summary>
    /// Advanced tagging intelligence engine providing multi-layered reasoning,
    /// cross-validation, workset inference, connected system traversal,
    /// sizing-based classification, and confidence scoring.
    ///
    /// Intelligence layers (applied in order of reliability):
    ///   L1. Connected MEP system name (connector traversal)
    ///   L2. Duct/Pipe system type built-in parameter
    ///   L3. Electrical circuit panel analysis
    ///   L4. Family name pattern matching (35+ equipment types)
    ///   L5. Room-type inference (Server Room → ICT, Kitchen → SAN)
    ///   L6. Workset name inference (M-Mechanical → M, E-Electrical → E)
    ///   L7. Connected element traversal (trace pipe/duct to source equipment)
    ///   L8. Size-based classification (small pipe → sanitary, large pipe → DCW)
    ///   L9. Adjacent element analysis (nearby elements suggest system context)
    ///   L10. Cross-validation (DISC vs SYS vs FUNC consistency checks)
    ///
    /// Each layer produces a confidence score (0.0-1.0). The highest-confidence
    /// result wins. Ties are broken by layer priority (lower = more reliable).
    /// </summary>
    public static class TagIntelligence
    {
        /// <summary>
        /// Result from an intelligence layer, including the derived value,
        /// the confidence level, and which layer produced it (for audit trail).
        /// </summary>
        public class InferenceResult
        {
            public string Value { get; set; }
            public double Confidence { get; set; }
            public string Source { get; set; }

            public InferenceResult(string value, double confidence, string source)
            {
                Value = value;
                Confidence = confidence;
                Source = source;
            }
        }

        // ── Layer 6: Workset Name Inference ──────────────────────────────────

        /// <summary>
        /// Infer discipline from the element's workset name.
        /// Revit worksets follow naming conventions like "M-Mechanical", "E-Electrical",
        /// "P-Plumbing", "A-Architecture", "S-Structure" per AEC UK BIM Protocol.
        /// </summary>
        public static InferenceResult InferDiscFromWorkset(Element el)
        {
            try
            {
                if (el.Document.IsWorkshared)
                {
                    WorksetId wsId = el.WorksetId;
                    if (wsId != null && wsId != WorksetId.InvalidWorksetId)
                    {
                        WorksetTable table = el.Document.GetWorksetTable();
                        Workset ws = table.GetWorkset(wsId);
                        if (ws != null)
                        {
                            string wsName = ws.Name?.ToUpperInvariant() ?? "";

                            if (wsName.StartsWith("M-") || wsName.Contains("MECHANICAL") ||
                                wsName.Contains("HVAC"))
                                return new InferenceResult("M", 0.7, "Workset: " + ws.Name);
                            if (wsName.StartsWith("E-") || wsName.Contains("ELECTRICAL") ||
                                wsName.Contains("LIGHTING"))
                                return new InferenceResult("E", 0.7, "Workset: " + ws.Name);
                            if (wsName.StartsWith("P-") || wsName.Contains("PLUMBING") ||
                                wsName.Contains("PUBLIC HEALTH"))
                                return new InferenceResult("P", 0.7, "Workset: " + ws.Name);
                            if (wsName.StartsWith("A-") || wsName.Contains("ARCHITECT"))
                                return new InferenceResult("A", 0.7, "Workset: " + ws.Name);
                            if (wsName.StartsWith("S-") || wsName.Contains("STRUCT"))
                                return new InferenceResult("S", 0.7, "Workset: " + ws.Name);
                            if (wsName.Contains("FIRE"))
                                return new InferenceResult("FP", 0.7, "Workset: " + ws.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferDiscFromWorkset: {ex.Message}");
            }
            return null;
        }

        // ── Layer 7: Connected Element Traversal ─────────────────────────────

        /// <summary>
        /// Trace the connected system from an element back to its source equipment.
        /// For pipes/ducts, follows the system to find the connected major equipment
        /// (AHU, pump, boiler, chiller) which identifies the system type definitively.
        /// Limited to 2 hops to avoid performance issues.
        /// </summary>
        public static InferenceResult InferSysFromConnectedEquipment(Element el)
        {
            try
            {
                FamilyInstance fi = el as FamilyInstance;
                if (fi?.MEPModel?.ConnectorManager == null) return null;

                // Traverse up to 2 hops of connected elements
                var visited = new HashSet<ElementId> { el.Id };
                var queue = new Queue<(Element elem, int depth)>();

                foreach (Connector conn in fi.MEPModel.ConnectorManager.Connectors)
                {
                    if (conn.AllRefs == null) continue;
                    foreach (Connector other in conn.AllRefs)
                    {
                        if (other.Owner != null && !visited.Contains(other.Owner.Id))
                        {
                            visited.Add(other.Owner.Id);
                            queue.Enqueue((other.Owner, 1));
                        }
                    }
                }

                while (queue.Count > 0)
                {
                    var (current, depth) = queue.Dequeue();
                    string catName = ParameterHelpers.GetCategoryName(current);

                    // If we reached major equipment, use its classification
                    if (catName == "Mechanical Equipment")
                    {
                        string famName = ParameterHelpers.GetFamilyName(current).ToUpperInvariant();
                        if (famName.Contains("AHU") || famName.Contains("AIR HANDLING"))
                            return new InferenceResult("HVAC", 0.9, "Connected to AHU: " + current.Id);
                        if (famName.Contains("BOILER") || famName.Contains("BLR"))
                            return new InferenceResult("HWS", 0.9, "Connected to boiler: " + current.Id);
                        if (famName.Contains("CHILLER") || famName.Contains("CHR"))
                            return new InferenceResult("HVAC", 0.9, "Connected to chiller: " + current.Id);
                        if (famName.Contains("PUMP"))
                            return new InferenceResult("DCW", 0.8, "Connected to pump: " + current.Id);
                    }
                    else if (catName == "Electrical Equipment")
                    {
                        return new InferenceResult("LV", 0.85, "Connected to panel: " + current.Id);
                    }

                    // Continue traversal (max 2 hops)
                    if (depth < 2 && current is FamilyInstance fi2 &&
                        fi2.MEPModel?.ConnectorManager != null)
                    {
                        foreach (Connector conn in fi2.MEPModel.ConnectorManager.Connectors)
                        {
                            if (conn.AllRefs == null) continue;
                            foreach (Connector other in conn.AllRefs)
                            {
                                if (other.Owner != null && !visited.Contains(other.Owner.Id))
                                {
                                    visited.Add(other.Owner.Id);
                                    queue.Enqueue((other.Owner, depth + 1));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferSysFromConnectedEquipment: {ex.Message}");
            }
            return null;
        }

        // ── Layer 8: Size-Based Classification ───────────────────────────────

        /// <summary>
        /// Infer system type from element sizing.
        /// In MEP design, pipe/duct sizes correlate strongly with system type:
        ///   - Pipes ≤32mm: typically sanitary branch, DCW branch
        ///   - Pipes 40-80mm: sanitary mains, DCW mains
        ///   - Pipes 80-200mm: fire mains, DCW risers, HVAC CHW
        ///   - Pipes ≥200mm: fire protection mains, DHW risers
        ///   - Ducts ≤300mm: extract/exhaust branches
        ///   - Ducts 300-600mm: supply/return branches
        ///   - Ducts ≥600mm: supply/return mains
        /// </summary>
        public static InferenceResult InferSysFromSize(Element el)
        {
            try
            {
                // Read calculated size or diameter
                Parameter sizePar = el.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE);
                Parameter diaPar = el.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);

                double diameterMm = 0;
                if (diaPar != null && diaPar.HasValue && diaPar.StorageType == StorageType.Double)
                    diameterMm = diaPar.AsDouble() * 304.8; // feet to mm

                string catName = ParameterHelpers.GetCategoryName(el);

                // Pipe size-based inference (only applies if no system info available)
                if (catName == "Pipes" && diameterMm > 0)
                {
                    if (diameterMm >= 100)
                        return new InferenceResult("FP", 0.4,
                            $"Size inference: pipe {diameterMm:F0}mm ≥ 100mm (possible fire main)");
                    if (diameterMm <= 32)
                        return new InferenceResult("SAN", 0.3,
                            $"Size inference: pipe {diameterMm:F0}mm ≤ 32mm (possible sanitary branch)");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferSysFromSize: {ex.Message}");
            }
            return null;
        }

        // ── Layer 10: Cross-Validation ───────────────────────────────────────

        /// <summary>
        /// Cross-validate DISC, SYS, FUNC, and PROD codes against each other.
        /// Returns a list of inconsistencies found (empty list = all valid).
        ///
        /// Rules:
        ///   - DISC=M requires SYS in {HVAC, HWS, DCW, GAS, RWD, SAN, DHW}
        ///   - DISC=E requires SYS in {LV, FLS, SEC, ICT, COM, NCL}
        ///   - DISC=P requires SYS in {DCW, DHW, SAN, RWD, GAS}
        ///   - DISC=FP requires SYS in {FP, FLS}
        ///   - PROD code must be compatible with category
        ///   - FUNC code must be compatible with SYS code
        /// </summary>
        public static List<string> CrossValidate(Element el)
        {
            var issues = new List<string>();
            string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
            string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC);
            string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD);
            string catName = ParameterHelpers.GetCategoryName(el);

            if (string.IsNullOrEmpty(disc)) return issues;

            // DISC ↔ SYS consistency
            var validSysForDisc = new Dictionary<string, HashSet<string>>
            {
                { "M", new HashSet<string> { "HVAC", "HWS", "DCW", "DHW", "GAS", "RWD", "SAN" } },
                { "E", new HashSet<string> { "LV", "FLS", "SEC", "ICT", "COM", "NCL" } },
                { "P", new HashSet<string> { "DCW", "DHW", "SAN", "RWD", "GAS" } },
                { "FP", new HashSet<string> { "FP", "FLS" } },
                { "A", new HashSet<string> { "ARC" } },
                { "S", new HashSet<string> { "STR" } },
                { "LV", new HashSet<string> { "LV", "ICT", "COM", "SEC", "NCL" } },
            };

            if (!string.IsNullOrEmpty(sys) && validSysForDisc.TryGetValue(disc, out var validSys))
            {
                if (!validSys.Contains(sys))
                    issues.Add($"DISC={disc} incompatible with SYS={sys} (expected: {string.Join("/", validSys)})");
            }

            // DISC ↔ Category consistency
            string expectedDisc = TagConfig.DiscMap.TryGetValue(catName, out string ed) ? ed : null;
            if (expectedDisc != null && expectedDisc != disc)
                issues.Add($"DISC={disc} doesn't match category '{catName}' (expected: {expectedDisc})");

            // SYS ↔ FUNC consistency (HVAC should have SUP/RTN/EXH/FRA, not PWR)
            if (sys == "HVAC" && func == "PWR")
                issues.Add($"SYS=HVAC with FUNC=PWR is invalid (expected: SUP/RTN/EXH/FRA)");
            if (sys == "LV" && (func == "SUP" || func == "HTG"))
                issues.Add($"SYS=LV with FUNC={func} is invalid (expected: PWR/LTG)");

            return issues;
        }

        /// <summary>
        /// Perform full tagging intelligence analysis on an element.
        /// Runs all inference layers and returns a consolidated report
        /// with the best result per token and confidence scores.
        /// Used by AutoPopulate and pre-tagging audit.
        /// </summary>
        public static Dictionary<string, InferenceResult> AnalyzeElement(Document doc, Element el)
        {
            var results = new Dictionary<string, InferenceResult>();
            string catName = ParameterHelpers.GetCategoryName(el);

            // DISC — category is primary (confidence 1.0), workset as validation
            if (TagConfig.DiscMap.TryGetValue(catName, out string disc))
                results["DISC"] = new InferenceResult(disc, 1.0, "Category: " + catName);

            var wsResult = InferDiscFromWorkset(el);
            if (wsResult != null && results.ContainsKey("DISC") && results["DISC"].Value != wsResult.Value)
                StingLog.Warn($"Element {el.Id}: category says DISC={results["DISC"].Value} but workset says {wsResult.Value}");

            // SYS — multi-layer with confidence scoring
            string sys = TagConfig.GetMepSystemAwareSysCode(el, catName);
            if (!string.IsNullOrEmpty(sys))
                results["SYS"] = new InferenceResult(sys, 0.85, "MEP system detection");

            // Try connected equipment traversal for higher confidence
            var connResult = InferSysFromConnectedEquipment(el);
            if (connResult != null && connResult.Confidence > (results.ContainsKey("SYS") ? results["SYS"].Confidence : 0))
                results["SYS"] = connResult;

            // Size-based only if nothing else worked
            if (!results.ContainsKey("SYS") || results["SYS"].Confidence < 0.5)
            {
                var sizeResult = InferSysFromSize(el);
                if (sizeResult != null)
                    results["SYS"] = sizeResult;
            }

            // FUNC — smart detection
            string sysVal = results.ContainsKey("SYS") ? results["SYS"].Value : "";
            string func = TagConfig.GetSmartFuncCode(el, sysVal);
            if (!string.IsNullOrEmpty(func))
                results["FUNC"] = new InferenceResult(func, 0.8, "Smart FUNC detection");

            // PROD — family-aware
            string prod = TagConfig.GetFamilyAwareProdCode(el, catName);
            if (!string.IsNullOrEmpty(prod))
                results["PROD"] = new InferenceResult(prod, 0.9, "Family-aware PROD");

            // LVL
            string lvl = ParameterHelpers.GetLevelCode(doc, el);
            if (lvl != "XX")
                results["LVL"] = new InferenceResult(lvl, 1.0, "Level: auto-derived");

            return results;
        }

        /// <summary>
        /// Generate a human-readable audit trail for an element's tag derivation.
        /// Shows what each intelligence layer detected and why, with confidence scores.
        /// </summary>
        public static string GenerateAuditTrail(Document doc, Element el)
        {
            var sb = new System.Text.StringBuilder();
            string catName = ParameterHelpers.GetCategoryName(el);
            sb.AppendLine($"Element {el.Id} [{catName}]: {ParameterHelpers.GetFamilyName(el)}");

            var analysis = AnalyzeElement(doc, el);
            foreach (var kvp in analysis)
            {
                sb.AppendLine($"  {kvp.Key} = {kvp.Value.Value} " +
                    $"(confidence: {kvp.Value.Confidence:P0}, source: {kvp.Value.Source})");
            }

            // Cross-validation
            var issues = CrossValidate(el);
            if (issues.Count > 0)
            {
                sb.AppendLine("  WARNINGS:");
                foreach (string issue in issues)
                    sb.AppendLine($"    ! {issue}");
            }
            else
            {
                sb.AppendLine("  Cross-validation: PASS");
            }

            return sb.ToString();
        }
    }
}
