using Planscape.Core.Entities;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Fills NRM2-format item description templates by substituting element
/// property tokens. Templates use {{TokenName}} placeholders resolved
/// against the element's property bag (IFC property sets or Revit parameters).
/// </summary>
public class Nrm2DescriptionBuilder
{
    // Compiled once — safe to use from multiple threads (Regex is thread-safe after compilation).
    private static readonly System.Text.RegularExpressions.Regex _tokenRx =
        new(@"\{\{(\w+)\}\}", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _doubleCommaRx =
        new(@",\s*,", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _trailingCommaRx =
        new(@",\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex _multiSpaceRx =
        new(@"\s{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly Dictionary<string, string[]> _fallbackChain = new(StringComparer.OrdinalIgnoreCase)
    {
        ["material"]      = new[] { "Pset_MaterialProperties:Name", "Material", "BaseMaterial" },
        ["thickness"]     = new[] { "Qto_WallBaseQuantities:Width", "Width", "Thickness" },
        ["height"]        = new[] { "Qto_WallBaseQuantities:Height", "Height", "StoreyHeight" },
        ["finish"]        = new[] { "Pset_Coating:Description", "SurfaceFinish", "Finish" },
        ["grade"]         = new[] { "Pset_ConcreteElementGeneral:StrengthClass", "Grade", "ConcreteGrade" },
        ["type"]          = new[] { "ObjectType", "Type", "PredefinedType" },
        ["nominal_diam"]  = new[] { "Pset_PipeConnectionProperties:NominalDiameter", "NominalDiameter", "Diameter" },
        ["insulation"]    = new[] { "Pset_InsulationGeneral:Description", "InsulationType", "Insulation" },
    };

    /// <summary>
    /// Resolves a description template string, substituting {{token}} placeholders
    /// with values from <paramref name="properties"/>.
    /// </summary>
    public string Build(string template, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;

        var result = _tokenRx.Replace(template, m =>
        {
            var key = m.Groups[1].Value;

            // Direct match (case-insensitive via OrdinalIgnoreCase lookup).
            if (properties.TryGetValue(key, out var direct) && !string.IsNullOrEmpty(direct))
                return direct;

            // Fallback chain for well-known NRM2 token names.
            if (_fallbackChain.TryGetValue(key, out var chain))
                foreach (var alias in chain)
                    // Try exact key and TitleCase variant of incoming properties.
                    if (properties.TryGetValue(alias, out var v) && !string.IsNullOrEmpty(v))
                        return v;

            return string.Empty;
        });

        // Collapse artefacts left by missing tokens (double commas, trailing commas, excess spaces).
        result = _doubleCommaRx.Replace(result, ",");
        result = _trailingCommaRx.Replace(result, "");
        result = _multiSpaceRx.Replace(result, " ").Trim();
        return result;
    }

    /// <summary>
    /// Builds a description from a <see cref="TakeoffRule"/> template and a
    /// flat property dictionary sourced from the IFC element.
    /// </summary>
    public string BuildFromRule(TakeoffRule rule, IReadOnlyDictionary<string, string> properties)
        => Build(rule.DescriptionTemplate, properties);
}
