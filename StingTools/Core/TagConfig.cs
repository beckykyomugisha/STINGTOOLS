using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core
{
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

        /// <summary>Check if a tag string has the expected number of tokens.</summary>
        public static bool TagIsComplete(string tagValue, int expectedTokens = 8)
        {
            if (string.IsNullOrEmpty(tagValue))
                return false;
            return tagValue.Split('-').Length == expectedTokens;
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
