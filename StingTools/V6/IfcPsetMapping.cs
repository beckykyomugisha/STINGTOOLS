// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/IfcPsetMapping.cs — S6.9 (N-G14).
//
// Loads STING_IFC_PSET_MAPPING.json (S6.10) and exposes a
// lookup-per-parameter API: GetIfcPsetMapping(stingParam) returns a
// tuple (ifcPsetName, ifcPropertyName, ifcDataType) that the IFC
// export pipeline (future ExporterIfcUtils integration) can use to
// place the STING value in the correct IFC 4.3 property set.
//
// The mapping is the bridge between STING's 2,307 MR_PARAMETERS and
// the IFC 4.3 schema. MVP scaffolds ~50 representative mappings so
// downstream code can wire up without a complete table. The runner's
// full-mapping deliverable (S6.10) is tracked as follow-up work.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class IfcPsetEntry
    {
        public string StingParam { get; set; } = string.Empty;
        public string IfcPsetName { get; set; } = string.Empty;
        public string IfcPropertyName { get; set; } = string.Empty;
        public string IfcDataType { get; set; } = "IfcText";
        public string IfcEntity { get; set; } = string.Empty;     // optional restriction
        public string Notes { get; set; } = string.Empty;
    }

    public static class IfcPsetMapping
    {
        private static Dictionary<string, IfcPsetEntry> _cache;
        private static readonly object _lk = new object();

        public static IfcPsetEntry GetMapping(string stingParam)
        {
            lock (_lk)
            {
                _cache ??= Load();
                return _cache.TryGetValue(stingParam, out var v) ? v : null;
            }
        }

        public static IEnumerable<IfcPsetEntry> AllMappings()
        {
            lock (_lk)
            {
                _cache ??= Load();
                return _cache.Values.ToList();
            }
        }

        public static void Reload()
        {
            lock (_lk) { _cache = null; }
        }

        private static Dictionary<string, IfcPsetEntry> Load()
        {
            var dict = new Dictionary<string, IfcPsetEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string dir = Path.GetDirectoryName(typeof(IfcPsetMapping).Assembly.Location) ?? "";
                string path = Path.Combine(dir, "Data", "IFC", "STING_IFC_PSET_MAPPING.json");
                if (!File.Exists(path))
                {
                    StingLog.Warn($"IfcPsetMapping: file missing {path}");
                    return dict;
                }
                var arr = JArray.Parse(File.ReadAllText(path));
                foreach (var t in arr)
                {
                    var e = new IfcPsetEntry
                    {
                        StingParam      = (string)t["sting_param"] ?? string.Empty,
                        IfcPsetName     = (string)t["ifc_pset"] ?? string.Empty,
                        IfcPropertyName = (string)t["ifc_property"] ?? string.Empty,
                        IfcDataType     = (string)t["ifc_data_type"] ?? "IfcText",
                        IfcEntity       = (string)t["ifc_entity"] ?? string.Empty,
                        Notes           = (string)t["notes"] ?? string.Empty,
                    };
                    if (!string.IsNullOrEmpty(e.StingParam)) dict[e.StingParam] = e;
                }
                StingLog.Info($"IfcPsetMapping: loaded {dict.Count} mappings from {path}");
            }
            catch (Exception ex)
            {
                StingLog.Error("IfcPsetMapping.Load failed", ex);
            }
            return dict;
        }

        /// <summary>
        /// Format a property-value pair as IFC STEP syntax for direct
        /// use in an IFC writer.
        /// </summary>
        public static string FormatStepPropertyValue(IfcPsetEntry entry, string rawValue)
        {
            if (entry == null || string.IsNullOrEmpty(rawValue)) return null;
            return entry.IfcDataType switch
            {
                "IfcText"         => $"#?=IFCPROPERTYSINGLEVALUE('{entry.IfcPropertyName}',$,IFCTEXT('{Escape(rawValue)}'),$);",
                "IfcLabel"        => $"#?=IFCPROPERTYSINGLEVALUE('{entry.IfcPropertyName}',$,IFCLABEL('{Escape(rawValue)}'),$);",
                "IfcIdentifier"   => $"#?=IFCPROPERTYSINGLEVALUE('{entry.IfcPropertyName}',$,IFCIDENTIFIER('{Escape(rawValue)}'),$);",
                "IfcBoolean"      => $"#?=IFCPROPERTYSINGLEVALUE('{entry.IfcPropertyName}',$,IFCBOOLEAN(.{(rawValue.Equals("1") ? "T" : "F")}.),$);",
                "IfcInteger"      => $"#?=IFCPROPERTYSINGLEVALUE('{entry.IfcPropertyName}',$,IFCINTEGER({rawValue}),$);",
                "IfcReal"         => $"#?=IFCPROPERTYSINGLEVALUE('{entry.IfcPropertyName}',$,IFCREAL({rawValue}),$);",
                "IfcLengthMeasure" => $"#?=IFCPROPERTYSINGLEVALUE('{entry.IfcPropertyName}',$,IFCLENGTHMEASURE({rawValue}),$);",
                _ => $"#?=IFCPROPERTYSINGLEVALUE('{entry.IfcPropertyName}',$,IFCTEXT('{Escape(rawValue)}'),$);",
            };
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
