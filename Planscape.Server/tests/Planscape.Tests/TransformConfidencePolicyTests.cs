using Planscape.Core.Coordinates;

namespace Planscape.Tests;

/// <summary>
/// TRACK B / P1 — the rule that decides whether a model MOVES on its own.
///
/// This is a pure function with no database, IFC file, or Revit session behind
/// it, which is the point: two independent pipelines (IFC ingest and
/// AutoAlignService, with the Revit GLB path to follow) produce automatic
/// transforms, and if each carried its own notion of "good enough" the same
/// model would render in different places depending on which one touched it
/// last. Exhaustive coverage is cheap here and expensive anywhere else.
///
/// The product decision under test: a model with no usable georeference is left
/// at the origin rather than moved by a guess. An un-placed model at the origin
/// is obviously un-placed and a coordinator fixes it in seconds; a model moved
/// 500 km by a bad guess looks placed and costs an investigation.
/// </summary>
public class TransformConfidencePolicyTests
{
    // ── HIGH — safe to apply without asking ─────────────────────────────────

    [Theory]
    [InlineData("PASS")]
    [InlineData("WARN")]   // WARN is a coordination note, not a placement error
    public void MapConversion_plus_own_projected_crs_is_High(string verdict)
    {
        var c = TransformConfidencePolicy.Evaluate(
            hasMapConversion: true, hasProjectedCrs: true, crsMatchesProject: false,
            surveyEasting: 1000, surveyNorthing: 2000, verdict: verdict);

        Assert.Equal(TransformConfidence.High, c);
        Assert.True(TransformConfidencePolicy.ShouldAutoApply(c));
    }

    [Fact]
    public void MapConversion_with_no_own_crs_but_matching_project_crs_is_High()
    {
        // A file that names no CRS of its own but whose coordinates sit in the
        // project's declared frame is just as placeable as one that does.
        var c = TransformConfidencePolicy.Evaluate(
            hasMapConversion: true, hasProjectedCrs: false, crsMatchesProject: true,
            surveyEasting: 1000, surveyNorthing: 2000, verdict: "PASS");

        Assert.Equal(TransformConfidence.High, c);
    }

    // ── LOW — stored as a suggestion, never applied on its own ──────────────

    [Fact]
    public void MapConversion_with_no_crs_anchor_is_Low()
    {
        // Plausible coordinates, but nothing says which coordinate system they
        // are in — unverifiable, so it stays a suggestion.
        var c = TransformConfidencePolicy.Evaluate(
            hasMapConversion: true, hasProjectedCrs: false, crsMatchesProject: false,
            surveyEasting: 1000, surveyNorthing: 2000, verdict: "PASS");

        Assert.Equal(TransformConfidence.Low, c);
        Assert.False(TransformConfidencePolicy.ShouldAutoApply(c));
    }

    [Fact]
    public void A_FAILed_alignment_report_caps_confidence_at_Low()
    {
        // Even with a perfect-looking georef: FAIL means the validator found a
        // contradiction it could not reconcile. Compute it, store it, don't act.
        var c = TransformConfidencePolicy.Evaluate(
            hasMapConversion: true, hasProjectedCrs: true, crsMatchesProject: true,
            surveyEasting: 1000, surveyNorthing: 2000, verdict: "FAIL");

        Assert.Equal(TransformConfidence.Low, c);
        Assert.False(TransformConfidencePolicy.ShouldAutoApply(c));
    }

    [Fact]
    public void Verdict_matching_is_case_insensitive()
    {
        Assert.Equal(TransformConfidence.Low, TransformConfidencePolicy.Evaluate(
            true, true, true, 1000, 2000, "fail"));
    }

    // ── NONE — nothing was derived from georeferencing at all ───────────────

    [Fact]
    public void No_map_conversion_is_None()
    {
        var c = TransformConfidencePolicy.Evaluate(
            hasMapConversion: false, hasProjectedCrs: true, crsMatchesProject: true,
            surveyEasting: 1000, surveyNorthing: 2000, verdict: "PASS");

        Assert.Equal(TransformConfidence.None, c);
        Assert.False(TransformConfidencePolicy.ShouldAutoApply(c));
    }

    [Theory]
    [InlineData(null, 2000.0)]
    [InlineData(1000.0, null)]
    [InlineData(null, null)]
    public void A_missing_survey_ordinate_is_None(double? easting, double? northing)
    {
        var c = TransformConfidencePolicy.Evaluate(
            hasMapConversion: true, hasProjectedCrs: true, crsMatchesProject: true,
            surveyEasting: easting, surveyNorthing: northing, verdict: "PASS");

        Assert.Equal(TransformConfidence.None, c);
    }

    // ── CRS equivalence — the string arrives from many authoring tools ───────

    [Theory]
    [InlineData("EPSG:27700", "EPSG:27700")]
    [InlineData("epsg:27700", "EPSG:27700")]
    [InlineData("EPSG:27700", "27700")]
    [InlineData("  EPSG:27700  ", "epsg:27700")]
    [InlineData("EPSG27700", "27700")]
    public void Equivalent_crs_spellings_match(string a, string b)
        => Assert.True(TransformConfidencePolicy.CrsEquivalent(a, b));

    [Theory]
    [InlineData("EPSG:27700", "EPSG:4326")]
    [InlineData("EPSG:27700", null)]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "EPSG:27700")]
    public void Different_or_unknown_crs_do_not_match(string? a, string? b)
        => Assert.False(TransformConfidencePolicy.CrsEquivalent(a, b));

    [Fact]
    public void Unknown_is_not_equal_to_unknown()
    {
        // Load-bearing: if null == null matched, EVERY model without a declared
        // CRS in a project without a declared CRS would grade HIGH and be moved
        // automatically on no evidence whatsoever.
        Assert.False(TransformConfidencePolicy.CrsEquivalent(null, null));

        var c = TransformConfidencePolicy.Evaluate(
            hasMapConversion: true, hasProjectedCrs: false,
            crsMatchesProject: TransformConfidencePolicy.CrsEquivalent(null, null),
            surveyEasting: 1000, surveyNorthing: 2000, verdict: "PASS");
        Assert.Equal(TransformConfidence.Low, c);
    }

    // ── storage round-trip ──────────────────────────────────────────────────

    [Theory]
    [InlineData(TransformConfidence.High, "HIGH")]
    [InlineData(TransformConfidence.Low, "LOW")]
    [InlineData(TransformConfidence.None, "NONE")]
    public void Storage_strings_round_trip(TransformConfidence c, string text)
    {
        Assert.Equal(text, TransformConfidencePolicy.ToStorageString(c));
        Assert.Equal(c, TransformConfidencePolicy.FromStorageString(text));
    }

    [Theory]
    [InlineData("high")]
    [InlineData("HIGH")]
    public void Storage_parsing_is_case_insensitive(string text)
        => Assert.Equal(TransformConfidence.High, TransformConfidencePolicy.FromStorageString(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("banana")]
    public void Unrecognised_storage_values_read_as_None(string? text)
    {
        // Rows written before this column existed read as null. They must NOT
        // read as "trusted" — the fail-safe direction is "do not move it".
        Assert.Equal(TransformConfidence.None, TransformConfidencePolicy.FromStorageString(text));
        Assert.False(TransformConfidencePolicy.ShouldAutoApply(
            TransformConfidencePolicy.FromStorageString(text)));
    }
}
