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
        string[]? ElementTypes);  // null = every IFC class

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
                ElementTypes: GetStringArray(item, "element_types")));
        }
        return list;
    }

    /// <summary>
    /// Resolve <paramref name="stingParam"/> from a flattened "Pset.Property" bag. Rows
    /// are tried in file order; export-only rows and rows for another IFC class are
    /// skipped. Returns null when no row yields a non-empty value.
    /// </summary>
    public static string? Resolve(
        IReadOnlyList<Entry> map,
        IReadOnlyDictionary<string, string> props,
        string stingParam,
        string? ifcType = null)
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
                if (hit != null && props.TryGetValue(hit, out var sv) && !string.IsNullOrEmpty(sv)) return sv;
            }
            else if (props.TryGetValue($"{m.PsetName}.{m.PropertyName}", out var pv) && !string.IsNullOrEmpty(pv))
            {
                return pv;
            }
        }
        return null;
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
