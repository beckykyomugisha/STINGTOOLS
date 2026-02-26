using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
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
    /// ISO 19650 naming and coding validation. Enforces that all token values
    /// conform to the allowed code lists defined in the tag configuration.
    /// </summary>
    public static class ISO19650Validator
    {
        /// <summary>Valid discipline codes per ISO 19650.</summary>
        public static readonly HashSet<string> ValidDiscCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "M", "E", "P", "A", "S", "FP", "LV", "G", "XX"
        };

        /// <summary>Valid system codes.</summary>
        public static readonly HashSet<string> ValidSysCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HVAC", "HWS", "DHW", "FP", "LV", "FLS", "COM", "ICT", "NCL", "SEC",
            "ARC", "STR", "GEN", ""
        };

        /// <summary>Valid function codes.</summary>
        public static readonly HashSet<string> ValidFuncCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SUP", "HTG", "SAN", "FP", "PWR", "FLS", "COM", "ICT", "NCL", "SEC",
            "FIT", "STR", "GEN", ""
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
                case "ASS_DISCIPLINE_COD_TXT":
                    if (!ValidDiscCodes.Contains(value))
                        return $"DISC '{value}' not in valid set ({string.Join(",", ValidDiscCodes)})";
                    break;
                case "ASS_LOC_TXT":
                    if (!TagConfig.LocCodes.Contains(value))
                        return $"LOC '{value}' not in valid set ({string.Join(",", TagConfig.LocCodes)})";
                    break;
                case "ASS_ZONE_TXT":
                    if (!TagConfig.ZoneCodes.Contains(value))
                        return $"ZONE '{value}' not in valid set ({string.Join(",", TagConfig.ZoneCodes)})";
                    break;
                case "ASS_SYSTEM_TYPE_TXT":
                    if (!ValidSysCodes.Contains(value))
                        return $"SYS '{value}' not in valid set ({string.Join(",", ValidSysCodes)})";
                    break;
                case "ASS_FUNC_TXT":
                    if (!ValidFuncCodes.Contains(value))
                        return $"FUNC '{value}' not in valid set ({string.Join(",", ValidFuncCodes)})";
                    break;
                case "ASS_LVL_COD_TXT":
                    // LVL codes: L01-L99, GF, B1-B9, RF, XX, or up to 4 uppercase chars
                    if (value.Length > 4 || value.Contains(" "))
                        return $"LVL '{value}' exceeds 4-char limit or contains spaces";
                    break;
                case "ASS_PRODCT_COD_TXT":
                    // PROD codes: 2-4 uppercase alphanumeric
                    if (value.Length < 2 || value.Length > 4)
                        return $"PROD '{value}' should be 2-4 characters";
                    break;
                case "ASS_SEQ_NUM_TXT":
                    if (!int.TryParse(value, out _))
                        return $"SEQ '{value}' is not a valid number";
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
                "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT",
                "ASS_LVL_COD_TXT", "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT",
                "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            };

            foreach (string param in tokenParams)
            {
                string val = ParameterHelpers.GetString(el, param);
                string error = ValidateToken(param, val);
                if (error != null)
                    errors.Add(error);
            }

            // Cross-validate: DISC must match element category
            string catName = ParameterHelpers.GetCategoryName(el);
            string disc = ParameterHelpers.GetString(el, "ASS_DISCIPLINE_COD_TXT");
            if (!string.IsNullOrEmpty(catName) && !string.IsNullOrEmpty(disc))
            {
                string expectedDisc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : null;
                if (expectedDisc != null && expectedDisc != disc)
                    errors.Add($"DISC mismatch: element category '{catName}' expects '{expectedDisc}' but has '{disc}'");
            }

            // Cross-validate: SYS should match category
            string sys = ParameterHelpers.GetString(el, "ASS_SYSTEM_TYPE_TXT");
            if (!string.IsNullOrEmpty(catName) && !string.IsNullOrEmpty(sys))
            {
                string expectedSys = TagConfig.GetSysCode(catName);
                if (!string.IsNullOrEmpty(expectedSys) && expectedSys != sys)
                    errors.Add($"SYS mismatch: category '{catName}' expects '{expectedSys}' but has '{sys}'");
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

            // Validate individual segments
            string discError = ValidateToken("ASS_DISCIPLINE_COD_TXT", parts[0]);
            if (discError != null) return discError;

            string locError = ValidateToken("ASS_LOC_TXT", parts[1]);
            if (locError != null) return locError;

            string zoneError = ValidateToken("ASS_ZONE_TXT", parts[2]);
            if (zoneError != null) return zoneError;

            string seqError = ValidateToken("ASS_SEQ_NUM_TXT", parts[7]);
            if (seqError != null) return seqError;

            return null; // valid
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

        /// <summary>Category name → discipline code (M, E, P, A, S, FP, LV, G).</summary>
        public static Dictionary<string, string> DiscMap { get; private set; }

        /// <summary>System code → list of category names.</summary>
        public static Dictionary<string, List<string>> SysMap { get; private set; }

        /// <summary>Category name → product code (GRL, AHU, DR, WIN, etc.).</summary>
        public static Dictionary<string, string> ProdMap { get; private set; }

        /// <summary>System code → function code (SUP, HTG, SAN, etc.).</summary>
        public static Dictionary<string, string> FuncMap { get; private set; }

        /// <summary>Available location codes.</summary>
        public static List<string> LocCodes { get; private set; }

        /// <summary>Available zone codes.</summary>
        public static List<string> ZoneCodes { get; private set; }

        public static string ConfigSource { get; private set; }

        static TagConfig()
        {
            LoadDefaults();
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
            ConfigSource = "built-in defaults";
        }

        /// <summary>Get the SYS code for a category name.</summary>
        public static string GetSysCode(string categoryName)
        {
            foreach (var kvp in SysMap)
            {
                if (kvp.Value.Contains(categoryName))
                    return kvp.Key;
            }
            return string.Empty;
        }

        /// <summary>Get the FUNC code for a SYS code.</summary>
        public static string GetFuncCode(string sysCode)
        {
            return FuncMap.TryGetValue(sysCode, out string val) ? val : string.Empty;
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

                // Mechanical Equipment — distinguish AHU, FCU, VAV, CHR, BLR, PMP, FAN
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
                    if (upper.Contains("AHU") || upper.Contains("AIR HANDLING")) return "AHU";
                }
                // Electrical Equipment — distinguish DB, MCC, MSB, SWB, UPS, TRF, GEN
                else if (categoryName == "Electrical Equipment")
                {
                    if (upper.Contains("MCC") || upper.Contains("MOTOR CONTROL")) return "MCC";
                    if (upper.Contains("MSB") || upper.Contains("MAIN SWITCH")) return "MSB";
                    if (upper.Contains("SWB") || upper.Contains("SWITCHBOARD")) return "SWB";
                    if (upper.Contains("UPS") || upper.Contains("UNINTERRUPT")) return "UPS";
                    if (upper.Contains("TRANSFORMER") || upper.Contains("TRF")) return "TRF";
                    if (upper.Contains("GENERATOR") || upper.Contains("GEN SET")) return "GEN";
                    if (upper.Contains("ATS") || upper.Contains("AUTO TRANSFER")) return "ATS";
                    if (upper.Contains("DB") || upper.Contains("DISTRIBUTION")) return "DB";
                }
                // Lighting — distinguish LUM, EML, DEC, TRK
                else if (categoryName == "Lighting Fixtures")
                {
                    if (upper.Contains("EMERGENCY") || upper.Contains("EML") || upper.Contains("EXIT")) return "EML";
                    if (upper.Contains("TRACK") || upper.Contains("TRK")) return "TRK";
                    if (upper.Contains("DECORATIVE") || upper.Contains("PENDANT") || upper.Contains("CHANDELIER")) return "DEC";
                    if (upper.Contains("DOWNLIGHT") || upper.Contains("RECESSED")) return "DWN";
                }
                // Plumbing Fixtures — distinguish WC, WHB, URN, SNK, SHW, BTH
                else if (categoryName == "Plumbing Fixtures")
                {
                    if (upper.Contains("WC") || upper.Contains("WATER CLOSET") || upper.Contains("TOILET")) return "WC";
                    if (upper.Contains("WHB") || upper.Contains("WASH HAND") || upper.Contains("BASIN")) return "WHB";
                    if (upper.Contains("URINAL") || upper.Contains("URN")) return "URN";
                    if (upper.Contains("SINK") || upper.Contains("SNK")) return "SNK";
                    if (upper.Contains("SHOWER") || upper.Contains("SHW")) return "SHW";
                    if (upper.Contains("BATH") || upper.Contains("BTH")) return "BTH";
                    if (upper.Contains("DRINKING") || upper.Contains("FOUNTAIN")) return "DRK";
                }
                // Fire Alarm — distinguish FAD, SML, MCP, BLL
                else if (categoryName == "Fire Alarm Devices")
                {
                    if (upper.Contains("SMOKE") || upper.Contains("DETECTOR") || upper.Contains("SML")) return "SML";
                    if (upper.Contains("MCP") || upper.Contains("CALL POINT") || upper.Contains("MANUAL")) return "MCP";
                    if (upper.Contains("BELL") || upper.Contains("SOUNDER") || upper.Contains("BLL")) return "BLL";
                    if (upper.Contains("STROBE") || upper.Contains("BEACON")) return "STB";
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
        /// Shared tag-building logic. Derives all 8 tokens for an element and writes
        /// both the individual token parameters and the assembled tag.
        /// Used by AutoTag, BatchTag, TagSelected to eliminate code duplication.
        /// Includes collision detection: if the generated tag already exists in the
        /// project, the SEQ is auto-incremented until a unique tag is found.
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
        /// <returns>True if the element was tagged, false if skipped.</returns>
        public static bool BuildAndWriteTag(Document doc, Element el,
            Dictionary<string, int> sequenceCounters, bool skipComplete = true,
            HashSet<string> existingTags = null,
            TagCollisionMode collisionMode = TagCollisionMode.AutoIncrement)
        {
            string catName = ParameterHelpers.GetCategoryName(el);
            if (string.IsNullOrEmpty(catName) || !DiscMap.ContainsKey(catName))
                return false;

            // Handle already-tagged elements based on collision mode
            string existingTag = ParameterHelpers.GetString(el, "ASS_TAG_1_TXT");
            bool hasCompleteTag = TagIsComplete(existingTag);

            if (hasCompleteTag)
            {
                switch (collisionMode)
                {
                    case TagCollisionMode.Skip:
                        return false; // Never touch existing complete tags
                    case TagCollisionMode.AutoIncrement:
                        if (skipComplete) return false; // Default: skip complete tags
                        break;
                    case TagCollisionMode.Overwrite:
                        break; // Proceed to overwrite
                }
            }

            bool overwriteTokens = (collisionMode == TagCollisionMode.Overwrite);

            string disc = DiscMap.TryGetValue(catName, out string d) ? d : "XX";
            string loc = ParameterHelpers.GetString(el, "ASS_LOC_TXT");
            if (string.IsNullOrEmpty(loc) || overwriteTokens) loc = string.IsNullOrEmpty(loc) ? "BLD1" : loc;
            string zone = ParameterHelpers.GetString(el, "ASS_ZONE_TXT");
            if (string.IsNullOrEmpty(zone) || overwriteTokens) zone = string.IsNullOrEmpty(zone) ? "Z01" : zone;
            string lvl = ParameterHelpers.GetLevelCode(doc, el);
            string sys = GetSysCode(catName);
            string func = GetFuncCode(sys);
            string prod = GetFamilyAwareProdCode(el, catName);

            string seqKey = $"{disc}_{sys}_{lvl}";
            if (!sequenceCounters.ContainsKey(seqKey))
                sequenceCounters[seqKey] = 0;
            sequenceCounters[seqKey]++;
            string seq = sequenceCounters[seqKey].ToString().PadLeft(NumPad, '0');

            string tag = string.Join(Separator, disc, loc, zone, lvl, sys, func, prod, seq);

            // Collision detection: if this exact tag already exists, increment SEQ
            if (existingTags != null)
            {
                int safetyLimit = 10000;
                while (existingTags.Contains(tag) && safetyLimit-- > 0)
                {
                    sequenceCounters[seqKey]++;
                    seq = sequenceCounters[seqKey].ToString().PadLeft(NumPad, '0');
                    tag = string.Join(Separator, disc, loc, zone, lvl, sys, func, prod, seq);
                }
                // Remove old tag from index if overwriting
                if (overwriteTokens && !string.IsNullOrEmpty(existingTag))
                    existingTags.Remove(existingTag);
                existingTags.Add(tag);
            }

            if (overwriteTokens)
            {
                ParameterHelpers.SetString(el, "ASS_DISCIPLINE_COD_TXT", disc, overwrite: true);
                ParameterHelpers.SetString(el, "ASS_LOC_TXT", loc, overwrite: true);
                ParameterHelpers.SetString(el, "ASS_ZONE_TXT", zone, overwrite: true);
                ParameterHelpers.SetString(el, "ASS_LVL_COD_TXT", lvl, overwrite: true);
                ParameterHelpers.SetString(el, "ASS_SYSTEM_TYPE_TXT", sys, overwrite: true);
                ParameterHelpers.SetString(el, "ASS_FUNC_TXT", func, overwrite: true);
                ParameterHelpers.SetString(el, "ASS_PRODCT_COD_TXT", prod, overwrite: true);
                ParameterHelpers.SetString(el, "ASS_SEQ_NUM_TXT", seq, overwrite: true);
            }
            else
            {
                ParameterHelpers.SetIfEmpty(el, "ASS_DISCIPLINE_COD_TXT", disc);
                ParameterHelpers.SetIfEmpty(el, "ASS_LOC_TXT", loc);
                ParameterHelpers.SetIfEmpty(el, "ASS_ZONE_TXT", zone);
                ParameterHelpers.SetIfEmpty(el, "ASS_LVL_COD_TXT", lvl);
                ParameterHelpers.SetIfEmpty(el, "ASS_SYSTEM_TYPE_TXT", sys);
                ParameterHelpers.SetIfEmpty(el, "ASS_FUNC_TXT", func);
                ParameterHelpers.SetIfEmpty(el, "ASS_PRODCT_COD_TXT", prod);
                ParameterHelpers.SetIfEmpty(el, "ASS_SEQ_NUM_TXT", seq);
            }
            ParameterHelpers.SetString(el, "ASS_TAG_1_TXT", tag, overwrite: true);
            return true;
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
                string tag = ParameterHelpers.GetString(elem, "ASS_TAG_1_TXT");
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

                string disc = ParameterHelpers.GetString(elem, "ASS_DISCIPLINE_COD_TXT");
                string sys = ParameterHelpers.GetString(elem, "ASS_SYSTEM_TYPE_TXT");
                string lvl = ParameterHelpers.GetString(elem, "ASS_LVL_COD_TXT");
                string seqStr = ParameterHelpers.GetString(elem, "ASS_SEQ_NUM_TXT");
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
                { "HWS", new List<string> { "Pipes", "Pipe Fittings", "Pipe Accessories" } },
                { "DHW", new List<string> { "Plumbing Fixtures", "Flex Pipes" } },
                { "FP", new List<string> { "Sprinklers" } },
                { "LV", new List<string> { "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures", "Lighting Devices", "Conduits", "Conduit Fittings", "Cable Trays", "Cable Tray Fittings" } },
                { "FLS", new List<string> { "Fire Alarm Devices" } },
                { "COM", new List<string> { "Communication Devices", "Telephone Devices" } },
                { "ICT", new List<string> { "Data Devices" } },
                { "NCL", new List<string> { "Nurse Call Devices" } },
                { "SEC", new List<string> { "Security Devices" } },
                // Architecture
                { "ARC", new List<string> { "Doors", "Windows", "Walls", "Floors", "Ceilings", "Roofs", "Rooms", "Furniture", "Furniture Systems", "Casework", "Railings", "Stairs", "Ramps" } },
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
                { "Generic Models", "GEN" }, { "Specialty Equipment", "SPE" },
                { "Medical Equipment", "MED" },
            };
        }

        private static Dictionary<string, string> DefaultFuncMap()
        {
            return new Dictionary<string, string>
            {
                { "HVAC", "SUP" }, { "HWS", "HTG" }, { "DHW", "SAN" },
                { "FP", "FP" }, { "LV", "PWR" }, { "FLS", "FLS" },
                { "COM", "COM" }, { "ICT", "ICT" }, { "NCL", "NCL" },
                { "SEC", "SEC" },
                { "ARC", "FIT" }, { "STR", "STR" }, { "GEN", "GEN" },
            };
        }

        private static List<string> DefaultLocCodes()
        {
            return new List<string> { "BLD1", "BLD2", "BLD3", "EXT", "XX" };
        }

        private static List<string> DefaultZoneCodes()
        {
            return new List<string> { "Z01", "Z02", "Z03", "Z04", "ZZ", "XX" };
        }
    }
}
