using System.Text.Json;

namespace Planscape.API.Services;

/// <summary>
/// DSCH-24 — the IFC pset &lt;-&gt; STING parameter map, as read by IFC ingest.
///
/// The map is ONE file, shared/ifc/mappings/STING_IFC_PSET_MAPPING.json, linked into
/// both this API and the Revit plugin. Rows are ordered: for one STING parameter the
/// first row whose property is present wins, so the canonical Pset_Sting* row comes
/// first and vendor / buildingSMART fallbacks follow.
///
/// One field spelling: pset_name / property_name. A row spelled the old way
/// (ifc_pset / ifc_property) is a data error and is refused loudly, not read.
///
/// DSCH-38: a row may carry value_map (STING value -> IFC value, one-to-one), e.g.
/// the Pset_*Common.Status rows map DEMOLISHED to DEMOLISH. It is applied in both
/// directions (<see cref="Entry.TryFromIfc"/> on ingest, <see cref="Entry.TryToIfc"/>
/// on export) by this reader and by the plugin's StingTools/V6/IfcPsetMapping.cs. A
/// value the map does not list (Status OTHER / NOTKNOWN / UNSET) is reported, never
/// passed through or guessed.
/// </summary>
public static class IfcPsetMappingTable
{
    /// <summary>The STING parameter that holds the full 8-segment tag.</summary>
    public const string TagParam = "ASS_TAG_1_TXT";

    public sealed record Entry(
        string    StingParam,
        string    PsetName,
        string    PropertyName,
        string    Direction,      // both | import | export
        bool      QuantityType,   // read from the element-quantity bag (same key form)
        bool      ScanAllPsets,   // match ".{PropertyName}" in any pset
        string[]? ElementTypes,   // null = every IFC class
        IReadOnlyDictionary<string, string>? ValueMap = null) // STING -> IFC; null = pass-through
    {
        /// <summary>Ingest: the STING value for an IFC value. False (with a reason)
        /// when the row has a value_map that does not list the value.</summary>
        public bool TryFromIfc(string ifcValue, out string? stingValue, out string? reason)
        {
            stingValue = null; reason = null;
            if (ValueMap is null) { stingValue = ifcValue; return true; }
            var hit = ValueMap.FirstOrDefault(kv =>
                string.Equals(kv.Value, ifcValue?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (hit.Key is not null) { stingValue = hit.Key; return true; }
            reason = $"{PsetName}.{PropertyName} = '{ifcValue}' has no STING value in the value_map for {StingParam} "
                   + $"(known: {string.Join(", ", ValueMap.Values)})";
            return false;
        }

        /// <summary>Export: the IFC value for a STING value. False (with a reason)
        /// when the row has a value_map that does not list the value.</summary>
        public bool TryToIfc(string stingValue, out string? ifcValue, out string? reason)
        {
            ifcValue = null; reason = null;
            if (ValueMap is null) { ifcValue = stingValue; return true; }
            var hit = ValueMap.FirstOrDefault(kv =>
                string.Equals(kv.Key, stingValue?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (hit.Key is not null) { ifcValue = hit.Value; return true; }
            reason = $"{StingParam} = '{stingValue}' has no IFC value in the value_map of {PsetName}.{PropertyName} "
                   + $"(known: {string.Join(", ", ValueMap.Keys)})";
            return false;
        }
    }

    /// <summary>Parse the mapping JSON. Throws <see cref="InvalidDataException"/> on a
    /// row that uses the retired field spelling or lacks sting_param / property_name.</summary>
    public static IReadOnlyList<Entry> Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<JsonElement[]>(json,
            new JsonSerializerOptions { AllowTrailingCommas = true })
            ?? throw new InvalidDataException("STING_IFC_PSET_MAPPING.json: root is not an array");

        var list = new List<Entry>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            var item = raw[i];
            if (item.TryGetProperty("ifc_pset", out _) || item.TryGetProperty("ifc_property", out _))
                throw new InvalidDataException(
                    $"STING_IFC_PSET_MAPPING.json row {i}: uses ifc_pset / ifc_property; the only spelling is pset_name / property_name");

            var stingParam = GetStr(item, "sting_param");
            var propName   = GetStr(item, "property_name");
            if (string.IsNullOrEmpty(stingParam) || string.IsNullOrEmpty(propName))
                throw new InvalidDataException(
                    $"STING_IFC_PSET_MAPPING.json row {i}: sting_param and property_name are required");

            list.Add(new Entry(
                StingParam:   stingParam,
                PsetName:     GetStr(item, "pset_name") ?? "",
                PropertyName: propName,
                Direction:    GetStr(item, "direction") ?? "both",
                QuantityType: GetBool(item, "quantity_type"),
                ScanAllPsets: GetBool(item, "scan_all_psets"),
                ElementTypes: GetStringArray(item, "element_types"),
                ValueMap:     GetValueMap(item, i)));
        }
        return list;
    }

    /// <summary>
    /// Resolve <paramref name="stingParam"/> from a flattened "Pset.Property" bag. Rows
    /// are tried in file order; export-only rows and rows for another IFC class are
    /// skipped. A row's value_map translates the IFC value to the STING value; a value
    /// it does not list is added to <paramref name="unmapped"/> (when given) and that
    /// row yields nothing. Returns null when no row yields a value.
    /// </summary>
    public static string? Resolve(
        IReadOnlyList<Entry> map,
        IReadOnlyDictionary<string, string> props,
        string stingParam,
        string? ifcType = null,
        ICollection<string>? unmapped = null)
    {
        foreach (var m in map)
        {
            if (!string.Equals(m.StingParam, stingParam, StringComparison.Ordinal)) continue;
            if (string.Equals(m.Direction, "export", StringComparison.OrdinalIgnoreCase)) continue;
            if (ifcType != null && m.ElementTypes != null && m.ElementTypes.Length > 0
                && !m.ElementTypes.Any(t => string.Equals(t, ifcType, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (m.ScanAllPsets)
            {
                var suffix = $".{m.PropertyName}";
                var hit = props.Keys.FirstOrDefault(k => k.EndsWith(suffix, StringComparison.Ordinal));
                if (hit != null && props.TryGetValue(hit, out var sv) && !string.IsNullOrEmpty(sv)
                    && Translate(m, sv, unmapped) is { } ts) return ts;
            }
            else if (props.TryGetValue($"{m.PsetName}.{m.PropertyName}", out var pv) && !string.IsNullOrEmpty(pv)
                     && Translate(m, pv, unmapped) is { } tp)
            {
                return tp;
            }
        }
        return null;
    }

    private static string? Translate(Entry m, string ifcValue, ICollection<string>? unmapped)
    {
        if (m.TryFromIfc(ifcValue, out var sting, out var reason)) return sting;
        unmapped?.Add(reason!);
        return null;
    }

    private static IReadOnlyDictionary<string, string>? GetValueMap(JsonElement el, int row)
    {
        if (!el.TryGetProperty("value_map", out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"STING_IFC_PSET_MAPPING.json row {row}: value_map must be an object of STING value -> IFC value");
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenIfc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in v.EnumerateObject())
        {
            var s = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
            if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(p.Name))
                throw new InvalidDataException(
                    $"STING_IFC_PSET_MAPPING.json row {row}: value_map entry '{p.Name}' must map to a non-empty string");
            if (!seenIfc.Add(s))
                throw new InvalidDataException(
                    $"STING_IFC_PSET_MAPPING.json row {row}: value_map sends two STING values to IFC '{s}'; ingest could not tell them apart");
            map[p.Name] = s;
        }
        if (map.Count == 0)
            throw new InvalidDataException($"STING_IFC_PSET_MAPPING.json row {row}: value_map is empty");
        return map;
    }

    private static string? GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static string[]? GetStringArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return null;
        return v.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToArray();
    }
}
