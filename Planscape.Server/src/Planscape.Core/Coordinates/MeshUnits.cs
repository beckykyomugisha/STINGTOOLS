namespace Planscape.Core.Coordinates;

/// <summary>
/// The length unit a model's MESH is expressed in, and the factor that takes it
/// to the platform's canonical mesh unit.
///
/// <para><b>The canonical mesh unit is the METRE</b>, because glTF 2.0 says so:
/// "The units for all linear distances are meters." Every GLB the viewer loads
/// is interpreted that way — <c>applyModelTransform</c> divides the stored
/// millimetre translation by 1000 precisely because the geometry it is
/// positioning is in metres.</para>
///
/// <para><b>Why this type has to exist.</b> The two Revit GLB writers disagreed
/// by a factor of 1000: <c>GlbSerializer</c> converted feet → metres while
/// <c>RevitGltfExporter</c> converted feet → millimetres, and both uploaded to
/// the same endpoint for the same viewer. A model alone hides it — the camera
/// fits to whatever bounds it finds, so a building rendered 1000× too large
/// looks perfectly normal. It only shows when you federate that model with one
/// from another tool, at which point they are a thousand times apart and no
/// amount of transform tuning will reconcile them. That is exactly the class of
/// bug that reads as "the models just don't line up".</para>
///
/// <para><b>Mesh unit is NOT georeferencing scale.</b> They are multiplied
/// together but they answer different questions:
/// <see cref="ProjectModelTransformScale"/> (the transform's
/// <c>ScaleFactor</c>) is a survey correction declared by an
/// <c>IfcMapConversion</c>; the mesh unit is how the vertex data happens to be
/// written. Conflating them is how a unit fix silently becomes a survey error.
/// </para>
/// </summary>
public static class MeshUnits
{
    /// <summary>Documentation anchor for the paragraph above.</summary>
    internal const string ProjectModelTransformScale = "ProjectModelTransform.ScaleFactor";

    /// <summary>The canonical mesh unit's name, as reported on upload.</summary>
    public const string Canonical = "m";

    /// <summary>
    /// Factor that converts one unit of the named length unit into metres.
    ///
    /// <para>Unknown or unspecified reads as <b>1.0 (metres)</b>, deliberately:
    /// it is the glTF default and the value that leaves existing behaviour
    /// untouched. A wrong guess here silently rescales a whole building, so the
    /// fail-safe direction is "change nothing".</para>
    /// </summary>
    public static double ToMetres(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return 1.0;

        return unit.Trim().ToLowerInvariant() switch
        {
            "m" or "metre" or "meter" or "metres" or "meters" => 1.0,
            "mm" or "millimetre" or "millimeter" or "millimetres" or "millimeters" => 0.001,
            "cm" or "centimetre" or "centimeter" or "centimetres" or "centimeters" => 0.01,
            "ft" or "feet" or "foot" => 0.3048,
            "in" or "inch" or "inches" => 0.0254,
            _ => 1.0,
        };
    }

    /// <summary>
    /// True when the named unit is understood. Callers that want to WARN about
    /// an unrecognised unit need this, because <see cref="ToMetres"/>
    /// deliberately cannot distinguish "metres" from "no idea".
    /// </summary>
    public static bool IsRecognised(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        return unit.Trim().ToLowerInvariant() switch
        {
            "m" or "metre" or "meter" or "metres" or "meters"
                or "mm" or "millimetre" or "millimeter" or "millimetres" or "millimeters"
                or "cm" or "centimetre" or "centimeter" or "centimetres" or "centimeters"
                or "ft" or "feet" or "foot"
                or "in" or "inch" or "inches" => true,
            _ => false,
        };
    }
}
