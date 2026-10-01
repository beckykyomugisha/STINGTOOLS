// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/IfcPsetMapping.cs — S6.9 (N-G14).
//
// Parses STING_IFC_PSET_MAPPING.json and answers "where does this STING parameter
// go in IFC?". NOTHING IN THE PLUGIN CONSUMES THIS AT RUNTIME YET (DSCH-46): no IFC
// export path reads the map, so the runtime loader (GetMapping / AllMappings / Reload)
// was deleted rather than left looking wired. What remains is the parse / translate
// contract shared with the server's reader (Planscape.API IfcPsetMappingTable), held
// by StingTools.Tags.Tests. An export that needs it loads the linked file at
// data/IFC/STING_IFC_PSET_MAPPING.json and calls Parse + FirstExport.
//
// DSCH-24: there is ONE mapping file, shared/ifc/mappings/STING_IFC_PSET_MAPPING.json,
// linked into this plugin's data/IFC/ and into Planscape.API (whose ingest reads the
// same rows). Rows are ordered; for export the FIRST row for a parameter whose
// direction is not "import" (and whose element_types admit the entity) wins. One
// field spelling: pset_name / property_name — a row spelled ifc_pset / ifc_property
// is refused and logged, never guessed at.
//
// DSCH-38: a row may declare a value_map (STING value -> IFC value), e.g. the
// Pset_*Common.Status rows translate DEMOLISHED <-> DEMOLISH. The map is applied in
// both directions (TryToIfc on export, TryFromIfc on import). A value the map does
// not list is reported (false + reason), never passed through or guessed. A map that
// sends two STING values to one IFC value is refused at parse, because import could
// not tell them apart.
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
        /// <summary>DSCH-38: STING value -> IFC value. Null = values pass through unchanged.</summary>
        public IReadOnlyDictionary<string, string> ValueMap { get; set; }

        public bool IsExport => !string.Equals(Direction, "import", StringComparison.OrdinalIgnoreCase);

        public bool AppliesTo(string ifcEntity) =>
            string.IsNullOrEmpty(ifcEntity) || ElementTypes == null || ElementTypes.Length == 0
            || ElementTypes.Any(t => string.Equals(t, ifcEntity, StringComparison.OrdinalIgnoreCase));

        /// <summary>Export: the IFC value for a STING value. With no value_map the value
        /// passes through. With one, a value it does not list returns false and
        /// <paramref name="reason"/> says why; nothing is guessed.</summary>
        public bool TryToIfc(string stingValue, out string ifcValue, out string reason)
        {
            ifcValue = null; reason = null;
            if (ValueMap == null) { ifcValue = stingValue; return true; }
            foreach (var kv in ValueMap)
                if (string.Equals(kv.Key, (stingValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                { ifcValue = kv.Value; return true; }
            reason = $"{StingParam} = '{stingValue}' has no IFC value in the value_map of {IfcPsetName}.{IfcPropertyName} "
                   + $"(known: {string.Join(", ", ValueMap.Keys)})";
            return false;
        }

        /// <summary>Import: the STING value for an IFC value (the inverse of the
        /// value_map). An IFC value the map does not list (e.g. OTHER / NOTKNOWN /
        /// UNSET for Status) returns false with a reason.</summary>
        public bool TryFromIfc(string ifcValue, out string stingValue, out string reason)
        {
            stingValue = null; reason = null;
            if (ValueMap == null) { stingValue = ifcValue; return true; }
            foreach (var kv in ValueMap)
                if (string.Equals(kv.Value, (ifcValue ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                { stingValue = kv.Key; return true; }
            reason = $"{IfcPsetName}.{IfcPropertyName} = '{ifcValue}' has no STING value in the value_map for {StingParam} "
                   + $"(known: {string.Join(", ", ValueMap.Values)})";
            return false;
        }
    }

    public static class IfcPsetMapping
    {

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
                    ValueMap        = ParseValueMap(t["value_map"], i),
                };
                if (string.IsNullOrEmpty(e.StingParam) || string.IsNullOrEmpty(e.IfcPropertyName))
                    throw new InvalidDataException(
                        $"STING_IFC_PSET_MAPPING.json row {i}: sting_param and property_name are required");
                list.Add(e);
                i++;
            }
            return list;
        }

        private static IReadOnlyDictionary<string, string> ParseValueMap(JToken tok, int row)
        {
            if (tok == null || tok.Type == JTokenType.Null) return null;
            if (!(tok is JObject obj))
                throw new InvalidDataException($"STING_IFC_PSET_MAPPING.json row {row}: value_map must be an object of STING value -> IFC value");
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var seenIfc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in obj.Properties())
            {
                if (p.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)p.Value) || string.IsNullOrWhiteSpace(p.Name))
                    throw new InvalidDataException($"STING_IFC_PSET_MAPPING.json row {row}: value_map entry '{p.Name}' must map to a non-empty string");
                if (!seenIfc.Add((string)p.Value))
                    throw new InvalidDataException($"STING_IFC_PSET_MAPPING.json row {row}: value_map sends two STING values to IFC '{(string)p.Value}'; import could not tell them apart");
                map[p.Name] = (string)p.Value;
            }
            if (map.Count == 0)
                throw new InvalidDataException($"STING_IFC_PSET_MAPPING.json row {row}: value_map is empty");
            return map;
        }

        /// <summary>
        /// Format a property-value pair as IFC STEP syntax for direct
        /// use in an IFC writer. The row's value_map is applied first (DSCH-38);
        /// a value it does not list is logged and nothing is written (null).
        /// </summary>
        public static string FormatStepPropertyValue(IfcPsetEntry entry, string rawValue)
        {
            if (entry == null || string.IsNullOrEmpty(rawValue)) return null;
            if (!entry.TryToIfc(rawValue, out var ifcValue, out var reason))
            {
                StingLog.Warn($"IfcPsetMapping: not exported — {reason}");
                return null;
            }
            rawValue = ifcValue;
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
