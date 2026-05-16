using Planscape.Core.Entities;

namespace Planscape.Infrastructure.Services;

/// <summary>
/// Fills NRM2-format item description templates by substituting element
/// property tokens. Templates use {{TokenName}} placeholders resolved
/// against the element's property bag (IFC property sets or Revit parameters).
/// </summary>
public class Nrm2DescriptionBuilder
{
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

        var result = System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{\{(\w+)\}\}",
            m =>
            {
                var key = m.Groups[1].Value;

                // Direct match first
                if (properties.TryGetValue(key, out var direct) && !string.IsNullOrEmpty(direct))
                    return direct;

                // Fallback chain
                if (_fallbackChain.TryGetValue(key, out var chain))
                    foreach (var alias in chain)
                        if (properties.TryGetValue(alias, out var v) && !string.IsNullOrEmpty(v))
                            return v;

                return string.Empty;
            });

        // Collapse multiple spaces / trailing commas left by missing tokens
        result = System.Text.RegularExpressions.Regex.Replace(result, @",\s*,", ",");
        result = System.Text.RegularExpressions.Regex.Replace(result, @",\s*$", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ").Trim();
        return result;
    }

    /// <summary>
    /// Builds a description from a <see cref="TakeoffRule"/> template and a
    /// flat property dictionary sourced from the IFC element.
    /// </summary>
    public string BuildFromRule(TakeoffRule rule, IReadOnlyDictionary<string, string> properties)
        => Build(rule.DescriptionTemplate, properties);
}
