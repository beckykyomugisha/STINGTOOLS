// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/IfcPsetMapping.cs — S6.9 (N-G14).
//
// Loads STING_IFC_PSET_MAPPING.json and answers "where does this STING parameter
// go in IFC?" for the IFC export pipeline (future ExporterIfcUtils integration).
//
// DSCH-24: there is ONE mapping file, shared/ifc/mappings/STING_IFC_PSET_MAPPING.json,
// linked into this plugin's data/IFC/ and into Planscape.API (whose ingest reads the
// same rows). Rows are ordered; for export the FIRST row for a parameter whose
// direction is not "import" (and whose element_types admit the entity) wins. One
// field spelling: pset_name / property_name — a row spelled ifc_pset / ifc_property
// is refused and logged, never guessed at.
//
// Revit-free: parsed and tested in StingTools.Tags.Tests.

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
        public string[] ElementTypes { get; set; }                // null = every IFC class
        public string Direction { get; set; } = "both";           // both | export | import
        public string Notes { get; set; } = string.Empty;
        public string Verify { get; set; } = string.Empty;        // non-empty = unconfirmed target

        public bool IsExport => !string.Equals(Direction, "import", StringComparison.OrdinalIgnoreCase);

        public bool AppliesTo(string ifcEntity) =>
            string.IsNullOrEmpty(ifcEntity) || ElementTypes == null || ElementTypes.Length == 0
            || ElementTypes.Any(t => string.Equals(t, ifcEntity, StringComparison.OrdinalIgnoreCase));
    }

    public static class IfcPsetMapping
    {
        private static List<IfcPsetEntry> _cache;
        private static readonly object _lk = new object();

        /// <summary>The export target for <paramref name="stingParam"/>: the first
        /// non-import row, optionally restricted to an IFC entity (e.g. "IfcWall").
        /// Null when the map has no export row for it.</summary>
        public static IfcPsetEntry GetMapping(string stingParam, string ifcEntity = null)
        {
            lock (_lk)
            {
                _cache ??= Load();
                return FirstExport(_cache, stingParam, ifcEntity);
            }
        }

        public static IEnumerable<IfcPsetEntry> AllMappings()
        {
            lock (_lk)
            {
                _cache ??= Load();
                return _cache.ToList();
            }
        }

        public static void Reload()
        {
            lock (_lk) { _cache = null; }
        }

        /// <summary>First export row for the parameter, in file order.</summary>
        public static IfcPsetEntry FirstExport(IEnumerable<IfcPsetEntry> rows, string stingParam, string ifcEntity = null)
        {
            if (rows == null || string.IsNullOrEmpty(stingParam)) return null;
            return rows.FirstOrDefault(e =>
                string.Equals(e.StingParam, stingParam, StringComparison.OrdinalIgnoreCase)
                && e.IsExport && e.AppliesTo(ifcEntity));
        }

        /// <summary>Parse the mapping JSON (array root). Rows keep file order.
        /// Throws <see cref="InvalidDataException"/> on the retired field spelling or a
        /// row missing sting_param / property_name.</summary>
        public static List<IfcPsetEntry> Parse(string json)
        {
            var arr = JArray.Parse(json);
            var list = new List<IfcPsetEntry>(arr.Count);
            int i = 0;
            foreach (var t in arr)
            {
                if (t["ifc_pset"] != null || t["ifc_property"] != null)
                    throw new InvalidDataException(
                        $"STING_IFC_PSET_MAPPING.json row {i}: uses ifc_pset / ifc_property; the only spelling is pset_name / property_name");
                var e = new IfcPsetEntry
                {
                    StingParam      = (string)t["sting_param"] ?? string.Empty,
                    IfcPsetName     = (string)t["pset_name"] ?? string.Empty,
                    IfcPropertyName = (string)t["property_name"] ?? string.Empty,
                    IfcDataType     = (string)t["ifc_data_type"] ?? "IfcText",
                    IfcEntity       = (string)t["ifc_entity"] ?? string.Empty,
                    ElementTypes    = (t["element_types"] as JArray)?.Select(x => (string)x).ToArray(),
                    Direction       = (string)t["direction"] ?? "both",
                    Notes           = (string)t["notes"] ?? string.Empty,
                    Verify          = (string)t["verify"] ?? string.Empty,
                };
                if (string.IsNullOrEmpty(e.StingParam) || string.IsNullOrEmpty(e.IfcPropertyName))
                    throw new InvalidDataException(
                        $"STING_IFC_PSET_MAPPING.json row {i}: sting_param and property_name are required");
                list.Add(e);
                i++;
            }
            return list;
        }

        private static List<IfcPsetEntry> Load()
        {
            string dir = Path.GetDirectoryName(typeof(IfcPsetMapping).Assembly.Location) ?? "";
            string path = Path.Combine(dir, "data", "IFC", "STING_IFC_PSET_MAPPING.json");
            try
            {
                if (!File.Exists(path))
                {
                    StingLog.Error($"IfcPsetMapping: mapping file missing ({path}) — no IFC export targets");
                    return new List<IfcPsetEntry>();
                }
                var list = Parse(File.ReadAllText(path));
                StingLog.Info($"IfcPsetMapping: loaded {list.Count} rows from {path}");
                return list;
            }
            catch (Exception ex)
            {
                StingLog.Error($"IfcPsetMapping: mapping file invalid ({path}) — no IFC export targets", ex);
                return new List<IfcPsetEntry>();
            }
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
