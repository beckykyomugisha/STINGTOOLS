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
        public readonly Dictionary<string, int> OverwrittenByCategory = new Dictionary<string, int>();
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
            Increment(OverwrittenByCategory, category);
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

            // Use if/else chain instead of switch — ParamRegistry properties are not compile-time constants
            if (tokenName == ParamRegistry.DISC)
            {
                if (!ValidDiscCodes.Contains(value))
                    return $"DISC '{value}' not in valid set ({string.Join(",", ValidDiscCodes)})";
            }
            else if (tokenName == ParamRegistry.LOC)
            {
                if (!TagConfig.LocCodes.Contains(value))
                    return $"LOC '{value}' not in valid set ({string.Join(",", TagConfig.LocCodes)})";
            }
            else if (tokenName == ParamRegistry.ZONE)
            {
                if (!TagConfig.ZoneCodes.Contains(value))
                    return $"ZONE '{value}' not in valid set ({string.Join(",", TagConfig.ZoneCodes)})";
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
                if (value.Length > TagConfig.NumPad + 1)
                    return $"SEQ '{value}' exceeds {TagConfig.NumPad}-digit format";
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
            string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
            if (!string.IsNullOrEmpty(catName) && !string.IsNullOrEmpty(disc))
            {
                string expectedDisc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : null;
                // Apply system-aware DISC correction (e.g., M→P for plumbing pipes, M→FP for fire)
                if (expectedDisc != null && !string.IsNullOrEmpty(sys))
                    expectedDisc = TagConfig.GetSystemAwareDisc(expectedDisc, sys, catName);
                if (expectedDisc != null && expectedDisc != disc)
                    errors.Add($"DISC mismatch: element category '{catName}' expects '{expectedDisc}' but has '{disc}'");
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

            char sepChar = !string.IsNullOrEmpty(TagConfig.Separator) ? TagConfig.Separator[0] : '-';
            string[] parts = tag.Split(sepChar);
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
        public static int NumPad => ParamRegistry.NumPad;
        public static string Separator => ParamRegistry.Separator;
        public static string[] SegmentOrder => ParamRegistry.SegmentOrder;
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
        public static List<string> LocCodes { get; internal set; }

        /// <summary>Available zone codes.</summary>
        public static List<string> ZoneCodes { get; internal set; }

        public static string ConfigSource { get; private set; }

        /// <summary>GAP-019: Default STATUS value from project_config.json (null = use "NEW").</summary>
        public static string StatusDefault { get; internal set; }

        /// <summary>GAP-019: Default REV value from project_config.json (null = use "P01").</summary>
        public static string RevDefault { get; internal set; }

        /// <summary>Reverse lookup: category name → SYS code. Built lazily from SysMap.</summary>
        private static Dictionary<string, List<string>> _reverseSysMap;

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

                // GAP-019: Load STATUS/REV defaults from config (optional)
                StatusDefault = null;
                RevDefault = null;
                if (data.TryGetValue("STATUS_DEFAULT", out object statusObj) && statusObj is string statusStr
                    && !string.IsNullOrWhiteSpace(statusStr))
                    StatusDefault = statusStr;
                if (data.TryGetValue("REV_DEFAULT", out object revObj) && revObj is string revStr
                    && !string.IsNullOrWhiteSpace(revStr))
                    RevDefault = revStr;

                ConfigSource = "project_config.json";

                // Load category warnings and paragraph containers from LABEL_DEFINITIONS
                LoadCategoryWarningsFromLabels();

                // GAP-009: Restore persisted active preset
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
            ConfigSource = "built-in defaults";
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
                if (path == null || !System.IO.File.Exists(path)) return;

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
                    }
                };

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
            // Combined name checks both family AND type name for broader pattern matching
            string combinedName = $"{familyName} {symbolName}".ToUpperInvariant();

            // Only apply family-level overrides for categories with diverse equipment
            if (!string.IsNullOrEmpty(familyName))
            {
                // Search both family name and combined (family + type) name for patterns
                string upper = combinedName;

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
            string fallbackProd = ProdMap.TryGetValue(categoryName, out string prod) ? prod : "GEN";
            if (!string.IsNullOrEmpty(familyName))
                StingLog.Info($"PROD fallback: '{familyName}' (cat={categoryName}) → using category default '{fallbackProd}'");
            return fallbackProd;
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
            char sepChar = !string.IsNullOrEmpty(Separator) ? Separator[0] : '-';
            string[] parts = tagValue.Split(new[] { sepChar });
            if (parts.Length != expectedTokens)
                return false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                    return false;
            }
            return true;
        }

        private static readonly HashSet<string> _placeholders = new HashSet<string> { "XX", "ZZ", "0000" };

        /// <summary>
        /// Strict tag completeness check. In addition to the standard check,
        /// rejects tags where any segment is a placeholder ("XX", "ZZ", "0000").
        /// Useful for compliance dashboards that require fully-resolved tags.
        /// </summary>
        public static bool TagIsFullyResolved(string tagValue, int expectedTokens = 8)
        {
            if (!TagIsComplete(tagValue, expectedTokens))
                return false;
            char sepChar = !string.IsNullOrEmpty(Separator) ? Separator[0] : '-';
            string[] parts = tagValue.Split(new[] { sepChar });
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
            string cachedRev = null)
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

            // Note: DiscMap.ContainsKey(catName) is guaranteed true by the early return at line 1105

            string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
            if (string.IsNullOrEmpty(loc) || loc == "XX")
            {
                // First valid non-placeholder code from LocCodes, else hardcoded default
                loc = LocCodes.FirstOrDefault(c => c != "XX" && !string.IsNullOrEmpty(c)) ?? "BLD1";
            }
            string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
            if (string.IsNullOrEmpty(zone) || zone == "XX")
            {
                zone = ZoneCodes.FirstOrDefault(c => c != "XX" && !string.IsNullOrEmpty(c)) ?? "Z01";
            }
            string lvl = ParameterHelpers.GetLevelCode(doc, el);
            // Guaranteed LVL default: replace unresolved "XX"/"" with "L00" for levelless elements
            if (string.IsNullOrEmpty(lvl) || lvl == "XX") lvl = "L00";

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
            // Guaranteed PROD default: category map or GEN
            if (string.IsNullOrEmpty(prod))
                prod = ProdMap.TryGetValue(catName, out string cp) ? cp : "GEN";

            // Log when defaults are applied for LOC/ZONE
            if (stats != null)
            {
                if (loc == "BLD1" && string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.LOC)))
                    stats.RecordWarning($"Element {el.Id}: LOC defaulted to BLD1");
                if (zone == "Z01" && string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.ZONE)))
                    stats.RecordWarning($"Element {el.Id}: ZONE defaulted to Z01");
            }

            // GAP-025: Validate-before-write — guarantee all 7 tokens are non-empty
            // before building the tag string. Applies hardcoded defaults as a safety net.
            if (string.IsNullOrEmpty(disc)) disc = "A";
            if (string.IsNullOrEmpty(loc))  loc  = "BLD1";
            if (string.IsNullOrEmpty(zone)) zone = "Z01";
            if (string.IsNullOrEmpty(lvl))  lvl  = "L00";
            if (string.IsNullOrEmpty(sys))  sys  = "GEN";
            if (string.IsNullOrEmpty(func)) func = "GEN";
            if (string.IsNullOrEmpty(prod)) prod = "GEN";

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
                    // Overflow guard: cap SEQ at format capacity (9999 for NumPad=4)
                    if (sequenceCounters[seqKey] > maxSeq)
                    {
                        string overflowMsg = $"SEQ overflow in collision loop: group {seqKey} reached {sequenceCounters[seqKey]} (max {maxSeq})";
                        StingLog.Warn(overflowMsg);
                        stats?.RecordWarning(overflowMsg);
                        break;
                    }
                    seq = sequenceCounters[seqKey].ToString().PadLeft(NumPad, '0');
                    tag = string.Join(Separator, disc, loc, zone, lvl, sys, func, prod, seq);
                }
                if (collisionCount > 0)
                    stats?.RecordCollision(tag, collisionCount);
                // Remove the element's old tag from index (it's being replaced)
                // so stale entries don't cause false collisions for other elements
                if (!string.IsNullOrEmpty(existingTag) && existingTag != tag)
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

                // Re-read actual token values (some may have been preserved by SetIfEmpty)
                // to ensure TAG1 reflects what's actually on the element
                string[] actualTokens = ParamRegistry.ReadTokenValues(el);
                tag = string.Join(Separator, actualTokens);
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
                string[] tokenVals = ParamRegistry.ReadTokenValues(el);
                ParamRegistry.WriteContainers(el, tokenVals, catName, overwrite: overwriteTokens);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Container write failed for {el.Id}: {ex.Message}");
            }

            // ── Auto-initialize display BOOLs (v5.6) ─────────────────────────
            // Ensure tag families show content immediately after tagging by setting
            // default visibility parameters. Without this, tag families using
            // paragraph depth or style matrix BOOLs would show blank labels.
            try
            {
                // TAG_PARA_STATE_1_BOOL = Yes (compact mode default — ensures at least
                // Tier 1 content is visible in tag families after tagging)
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.PARA_STATE_1, "Yes");

                // TAG_WARN_VISIBLE_BOOL = No (default off — prevents expensive per-element
                // warning evaluation on every WriteTag7All call for large models)
                ParameterHelpers.SetIfEmpty(el, ParamRegistry.WARN_VISIBLE, "No");

                // TAG_7_SECTION_VISIBLE_A-F = Yes (default all TAG7 sub-sections visible)
                ParameterHelpers.SetIfEmpty(el, "TAG_7_SECTION_VISIBLE_A_BOOL", "Yes");
                ParameterHelpers.SetIfEmpty(el, "TAG_7_SECTION_VISIBLE_B_BOOL", "Yes");
                ParameterHelpers.SetIfEmpty(el, "TAG_7_SECTION_VISIBLE_C_BOOL", "Yes");
                ParameterHelpers.SetIfEmpty(el, "TAG_7_SECTION_VISIBLE_D_BOOL", "Yes");
                ParameterHelpers.SetIfEmpty(el, "TAG_7_SECTION_VISIBLE_E_BOOL", "Yes");
                ParameterHelpers.SetIfEmpty(el, "TAG_7_SECTION_VISIBLE_F_BOOL", "Yes");

                // Default tag style: 2.5mm Normal Black (most common AEC standard)
                ParameterHelpers.SetIfEmpty(el, "TAG_2.5NOM_BLACK_BOOL", "Yes");
            }
            catch { /* Display BOOLs are optional — don't block tagging if params not bound */ }

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
            string fromConnector = GetSysFromConnector(el, categoryName);
            if (!string.IsNullOrEmpty(fromConnector)) return fromConnector;

            // Layer 2: Duct/Pipe system type built-in parameter
            string fromSysType = GetSysFromSystemTypeParam(el, categoryName);
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
            catch { }
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
            // BUG-010: "CW" is ambiguous — Condenser Water (HVAC) vs Cold Water (Plumbing)
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

                // Normalise empty tokens to match BuildAndWriteTag key format
                if (string.IsNullOrEmpty(sys))
                    sys = GetDiscDefaultSysCode(disc);
                if (string.IsNullOrEmpty(lvl) || lvl == "XX")
                    lvl = "L00";

                string key = $"{disc}_{sys}_{lvl}";
                if (int.TryParse(seqStr, out int seqNum) && seqNum >= 0)
                {
                    if (!maxSeq.ContainsKey(key) || seqNum > maxSeq[key])
                        maxSeq[key] = seqNum;
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

                string key = $"{disc}_{sys}_{lvl}";
                if (int.TryParse(seqStr, out int seqNum) && seqNum >= 0)
                {
                    if (!maxSeq.ContainsKey(key) || seqNum > maxSeq[key])
                        maxSeq[key] = seqNum;
                }
            }

            StingLog.Info($"Tag index built: {index.Count} existing tags, {maxSeq.Count} SEQ groups");
            return (index, maxSeq);
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

            /// <summary>All 6 sections as an array (A-F), matching TAG7Sections order.</summary>
            public string[] AllSections => new[] { SectionA, SectionB, SectionC, SectionD, SectionE, SectionF };
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
                    DiscriminatorParam = "ASS_DISCIPLINE_COD_TXT",
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
                    DiscriminatorParam = "ASS_STATUS_TXT",
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
                    DiscriminatorParam = "ASS_SYSTEM_TYPE_TXT",
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
                    DiscriminatorParam = "ASS_DISCIPLINE_COD_TXT",
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

                Dictionary<string, object> data;
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    data = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)
                        ?? new Dictionary<string, object>();
                }
                else
                {
                    data = new Dictionary<string, object>();
                }
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
        private static readonly Dictionary<string, string> DisciplineDescriptions = new Dictionary<string, string>
        {
            { "M", "Mechanical" }, { "E", "Electrical" }, { "P", "Plumbing" },
            { "A", "Architectural" }, { "S", "Structural" }, { "FP", "Fire Protection" },
            { "LV", "Low Voltage" }, { "G", "Gas" }, { "GEN", "General" },
        };

        /// <summary>Full system name for human-readable narrative.</summary>
        private static readonly Dictionary<string, string> SystemDescriptions = new Dictionary<string, string>
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
        };

        /// <summary>Full function description for human-readable narrative.</summary>
        private static readonly Dictionary<string, string> FunctionDescriptions = new Dictionary<string, string>
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
        };

        /// <summary>Full product type description for human-readable narrative.</summary>
        private static readonly Dictionary<string, string> ProductDescriptions = new Dictionary<string, string>
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
            string comStatus     = ParameterHelpers.GetString(el, "COM_COMMISSIONING_STATUS_TXT");
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

            // ISO reference always added with connecting language
            string fullTag = string.Join(Separator, tokenValues);
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

            return result;
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
        /// Write TAG7 + all sub-section parameters (TAG7A-TAG7F) for an element.
        /// Also writes warning text, and populates the category-specific paragraph container.
        /// Returns number of parameters written.
        /// </summary>
        public static int WriteTag7All(Document doc, Element el, string categoryName, string[] tokenValues, bool overwrite = true)
        {
            var tag7 = BuildTag7Sections(doc, el, categoryName, tokenValues);
            int written = 0;

            // TAG7 gets the marked-up narrative (with «H»/«L»/«V»/«C» tokens)
            if (!string.IsNullOrEmpty(tag7.MarkedUpNarrative))
            {
                if (ParameterHelpers.SetString(el, ParamRegistry.TAG7, tag7.MarkedUpNarrative, overwrite))
                    written++;
            }

            // TAG7A-TAG7F get plain section text for tag family labels
            string[] sectionParams = ParamRegistry.TAG7Sections;
            string[] sectionValues = tag7.AllSections;
            for (int i = 0; i < sectionParams.Length && i < sectionValues.Length; i++)
            {
                if (!string.IsNullOrEmpty(sectionValues[i]))
                {
                    if (ParameterHelpers.SetString(el, sectionParams[i], sectionValues[i], overwrite))
                        written++;
                }
            }

            // ── Warning evaluation (v5.5) ─────────────────────────────────
            // Check if warnings are enabled and evaluate applicable thresholds
            string warningText = EvaluateElementWarnings(doc, el, categoryName);
            if (!string.IsNullOrEmpty(warningText))
            {
                // Append warnings to TAG7 if warning visibility is enabled
                string existingTag7 = ParameterHelpers.GetString(el, ParamRegistry.TAG7);
                if (!string.IsNullOrEmpty(existingTag7) && !existingTag7.Contains("[!"))
                {
                    string withWarnings = existingTag7 + " | " + warningText;
                    if (ParameterHelpers.SetString(el, ParamRegistry.TAG7, withWarnings, true))
                        written++;
                }
            }

            // ── Paragraph container write (v5.5) ─────────────────────────
            // Write the full plain narrative to the category-specific paragraph parameter
            string paraContainer = ParamRegistry.GetParagraphContainer(categoryName);
            if (!string.IsNullOrEmpty(paraContainer) && !string.IsNullOrEmpty(tag7.PlainNarrative))
            {
                string paraText = tag7.PlainNarrative;
                // If warnings exist and are visible, append them to the paragraph
                if (!string.IsNullOrEmpty(warningText))
                    paraText += " | WARNINGS: " + warningText;
                if (ParameterHelpers.SetString(el, paraContainer, paraText, overwrite))
                    written++;
            }

            return written;
        }

        /// <summary>
        /// Evaluate all applicable warning thresholds for an element.
        /// Returns a concatenated warning string, or null if no warnings triggered.
        /// Respects TAG_WARN_VISIBLE_BOOL and TAG_WARN_SEVERITY_FILTER_TXT.
        /// </summary>
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
                return ParameterHelpers.GetString(el, "PER_THERM_U_VALUE_W_M2K_NR");
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
                return ParameterHelpers.GetString(el, "BLE_WINDOW_U_VALUE");
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
                return ParameterHelpers.GetString(el, "ELC_PNL_SPARE_WAYS_PCT");
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

        // ── Layer 9: Adjacent Element SYS Inference ──────────────────────────

        /// <summary>
        /// ENH-004: Infer SYS from adjacent elements within a 500mm radius.
        /// Uses BoundingBoxIntersectsFilter to find nearby elements with confirmed SYS values.
        /// If 80%+ of adjacent elements agree on a SYS code, returns that code with confidence 0.3.
        /// Useful for unconnected light fittings, generic models, and equipment placed near systems.
        /// </summary>
        public static InferenceResult InferSysFromAdjacentElements(Document doc, Element el)
        {
            try
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb == null) return null;

                // Expand bounding box by 500mm (≈1.64 ft) in all directions
                double expandFt = 500.0 / 304.8; // mm to feet
                XYZ min = new XYZ(bb.Min.X - expandFt, bb.Min.Y - expandFt, bb.Min.Z - expandFt);
                XYZ max = new XYZ(bb.Max.X + expandFt, bb.Max.Y + expandFt, bb.Max.Z + expandFt);
                Outline outline = new Outline(min, max);

                var bbFilter = new BoundingBoxIntersectsFilter(outline);
                var nearby = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbFilter)
                    .Where(e => e.Id != el.Id && e.Category != null)
                    .ToList();

                if (nearby.Count == 0) return null;

                // Count SYS values on nearby elements
                var sysCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int withSys = 0;

                foreach (Element adj in nearby)
                {
                    string adjSys = ParameterHelpers.GetString(adj, ParamRegistry.SYS);
                    if (string.IsNullOrEmpty(adjSys)) continue;

                    withSys++;
                    if (!sysCounts.ContainsKey(adjSys))
                        sysCounts[adjSys] = 0;
                    sysCounts[adjSys]++;
                }

                if (withSys < 2) return null; // Need at least 2 neighbours with SYS

                // Find dominant SYS
                var dominant = sysCounts.OrderByDescending(x => x.Value).First();
                double agreement = (double)dominant.Value / withSys;

                if (agreement >= 0.8)
                {
                    return new InferenceResult(dominant.Key, 0.3,
                        $"Adjacent element inference: {dominant.Value}/{withSys} neighbours have SYS={dominant.Key}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InferSysFromAdjacentElements: {ex.Message}");
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

            // ENH-004: Layer 9 — adjacent element inference (lowest confidence, last resort)
            if (!results.ContainsKey("SYS") || results["SYS"].Confidence < 0.3)
            {
                var adjResult = InferSysFromAdjacentElements(doc, el);
                if (adjResult != null)
                    results["SYS"] = adjResult;
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
