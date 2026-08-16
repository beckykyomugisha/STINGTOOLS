using Planscape.Core.Coordinates;

namespace Planscape.Tests;

/// <summary>
/// TRACK B / P3 — mesh units.
///
/// THE DEFECT
/// ----------
/// Two GLB writers in this repo disagreed by a factor of 1000:
/// <c>GlbSerializer</c> converted Revit feet → METRES, <c>RevitGltfExporter</c>
/// converted feet → MILLIMETRES, and both uploaded to the same endpoint for the
/// same viewer. glTF 2.0 is unambiguous ("The units for all linear distances are
/// meters") and the viewer assumes it — <c>applyModelTransform</c> divides the
/// stored millimetre translation by 1000 precisely because the geometry it
/// positions is metres.
///
/// It hid for so long because a model viewed ALONE looks correct at any uniform
/// scale: the camera fits to whatever bounds it finds. The mismatch only
/// surfaces once a Revit model is federated with a model from another tool, and
/// then it presents as "the models don't line up" — which reads like a
/// coordinate problem rather than a unit one, and sends you looking in the wrong
/// place.
///
/// THE FAIL-SAFE DIRECTION
/// ----------------------
/// An unknown unit reads as metres (1.0), never as a guess. Getting this
/// backwards silently rescales an entire building, so "change nothing" is the
/// only safe default.
/// </summary>
public class MeshUnitsTests
{
    [Theory]
    [InlineData("m", 1.0)]
    [InlineData("metre", 1.0)]
    [InlineData("meters", 1.0)]
    [InlineData("mm", 0.001)]
    [InlineData("millimetre", 0.001)]
    [InlineData("millimeters", 0.001)]
    [InlineData("cm", 0.01)]
    [InlineData("ft", 0.3048)]
    [InlineData("feet", 0.3048)]
    [InlineData("in", 0.0254)]
    public void Known_units_convert_to_metres(string unit, double expected)
        => Assert.Equal(expected, MeshUnits.ToMetres(unit), 9);

    [Theory]
    [InlineData("M")]
    [InlineData("MM")]
    [InlineData("  mm  ")]
    [InlineData("Millimetres")]
    public void Unit_parsing_ignores_case_and_padding(string unit)
        => Assert.True(MeshUnits.IsRecognised(unit));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("furlongs")]
    public void Unknown_units_read_as_metres_not_as_a_guess(string? unit)
    {
        // 1.0 means "leave the geometry alone". Anything else would rescale a
        // building on the strength of a string nobody recognised.
        Assert.Equal(1.0, MeshUnits.ToMetres(unit));
        Assert.False(MeshUnits.IsRecognised(unit));
    }

    [Fact]
    public void The_canonical_unit_is_metres_and_round_trips()
    {
        Assert.Equal("m", MeshUnits.Canonical);
        Assert.Equal(1.0, MeshUnits.ToMetres(MeshUnits.Canonical));
        Assert.True(MeshUnits.IsRecognised(MeshUnits.Canonical));
    }

    [Fact]
    public void A_millimetre_mesh_scales_down_by_exactly_one_thousand()
    {
        // The specific number that was wrong. A 10 m wall exported as 10 000
        // millimetre units must render 10 m long, not 10 km.
        double wallLengthInMeshUnits = 10_000;
        Assert.Equal(10.0, wallLengthInMeshUnits * MeshUnits.ToMetres("mm"), 9);
    }
}
