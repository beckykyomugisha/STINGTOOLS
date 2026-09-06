namespace Planscape.Core.Coordinates;

/// <summary>
/// How much the platform trusts an automatically-derived model transform.
///
/// This is the switch that decides whether a model MOVES on its own. It exists
/// because "we computed a transform" and "we believe it enough to place a
/// building with it" are different claims, and conflating them gives you one of
/// two bad products: either nothing ever aligns without manual data entry, or
/// every model without real survey data gets scattered across the site by a
/// guess.
/// </summary>
public enum TransformConfidence
{
    /// <summary>
    /// No usable georeference. The model stays where its author put it —
    /// at the project origin. Explicitly NOT "apply a best guess": a model
    /// parked at the origin is obviously un-placed and a coordinator fixes it
    /// in seconds, whereas a model moved 500 km by a bad guess looks placed and
    /// costs an investigation.
    /// </summary>
    None = 0,

    /// <summary>
    /// A transform was derived, but from incomplete or unconfirmed evidence
    /// (survey origin with no projected CRS to anchor it, or an alignment
    /// report that FAILed). Stored and offered as a suggestion; NOT applied
    /// automatically. A coordinator confirms it to make it live.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Real, self-consistent georeferencing: a survey origin from
    /// IfcMapConversion plus a CRS that either declares itself
    /// (IfcProjectedCRS) or matches the project's own coordinate system, and an
    /// alignment report that did not FAIL. Safe to apply without asking.
    /// </summary>
    High = 2,
}

/// <summary>
/// The ONE place that decides whether an auto-derived transform is trustworthy
/// enough to apply on its own.
///
/// <para><b>Why it is shared and Revit-free.</b> Two independent writers produce
/// automatic transforms — the IFC ingest path and <c>AutoAlignService</c> — and
/// a third (the Revit GLB upload path) is planned. If each carried its own
/// notion of "good enough", the same model would render in different places
/// depending on which pipeline touched it last. Keeping the rule here also makes
/// it a pure function that can be unit-tested exhaustively without a database,
/// an IFC file, or a Revit session.</para>
/// </summary>
public static class TransformConfidencePolicy
{
    /// <summary>
    /// Grade the georeferencing evidence behind a transform.
    /// </summary>
    /// <param name="hasMapConversion">
    /// The source declared an IfcMapConversion (or an equivalent host georef
    /// block). Without it there is no survey origin and nothing to trust.
    /// </param>
    /// <param name="hasProjectedCrs">
    /// The source declared an IfcProjectedCRS, so the easting/northing are
    /// anchored to a named coordinate reference system.
    /// </param>
    /// <param name="crsMatchesProject">
    /// The source's CRS matches the project's declared
    /// <c>ProjectCoordinateSystem</c>. An alternative anchor to
    /// <paramref name="hasProjectedCrs"/>: a file that names no CRS but whose
    /// coordinates sit in the project's declared frame is equally placeable.
    /// </param>
    /// <param name="surveyEasting">Survey origin easting, if any.</param>
    /// <param name="surveyNorthing">Survey origin northing, if any.</param>
    /// <param name="verdict">
    /// The IfcAlignmentReport verdict — "PASS" / "WARN" / "FAIL". WARN is
    /// deliberately accepted: it is raised for things like a true-north delta
    /// against a sibling model, which is a coordination note, not a reason to
    /// leave the model unplaced. FAIL is not.
    /// </param>
    public static TransformConfidence Evaluate(
        bool hasMapConversion,
        bool hasProjectedCrs,
        bool crsMatchesProject,
        double? surveyEasting,
        double? surveyNorthing,
        string? verdict)
    {
        // No survey origin → nothing was derived from georeferencing at all.
        if (!hasMapConversion || surveyEasting is null || surveyNorthing is null)
            return TransformConfidence.None;

        // A FAILed report means the validator found a contradiction it could not
        // reconcile. Compute the transform, store it, but do not act on it.
        if (string.Equals(verdict, "FAIL", StringComparison.OrdinalIgnoreCase))
            return TransformConfidence.Low;

        // Anchored either by its own declared CRS or by agreeing with the
        // project's. Unanchored coordinates are plausible but unverifiable, so
        // they stay a suggestion.
        return (hasProjectedCrs || crsMatchesProject)
            ? TransformConfidence.High
            : TransformConfidence.Low;
    }

    /// <summary>
    /// Whether a transform of this confidence may be applied to the model
    /// without a coordinator confirming it first.
    /// </summary>
    public static bool ShouldAutoApply(TransformConfidence confidence)
        => confidence == TransformConfidence.High;

    /// <summary>
    /// Persisted / wire form. Stored as a string rather than an int so a column
    /// value is readable in a database session and survives enum reordering.
    /// </summary>
    public static string ToStorageString(TransformConfidence confidence) => confidence switch
    {
        TransformConfidence.High => "HIGH",
        TransformConfidence.Low  => "LOW",
        _                        => "NONE",
    };

    /// <summary>Inverse of <see cref="ToStorageString"/>; unknown input reads as None.</summary>
    public static TransformConfidence FromStorageString(string? value) => value?.ToUpperInvariant() switch
    {
        "HIGH" => TransformConfidence.High,
        "LOW"  => TransformConfidence.Low,
        _      => TransformConfidence.None,
    };

    /// <summary>
    /// True when two CRS identifiers refer to the same system. Tolerates the
    /// common spellings ("EPSG:27700", "epsg:27700", "27700") because the string
    /// arrives from IFC files written by half a dozen authoring tools.
    /// Null/blank on either side is NOT a match — unknown is not equal.
    /// </summary>
    public static bool CrsEquivalent(string? a, string? b)
    {
        var na = NormaliseCrs(a);
        var nb = NormaliseCrs(b);
        return na.Length > 0 && na == nb;
    }

    private static string NormaliseCrs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var s = value.Trim().ToUpperInvariant();
        if (s.StartsWith("EPSG:", StringComparison.Ordinal)) s = s[5..].Trim();
        else if (s.StartsWith("EPSG", StringComparison.Ordinal)) s = s[4..].Trim(':', ' ');
        return s;
    }
}
