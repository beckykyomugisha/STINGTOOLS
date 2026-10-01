// StingTools — which walls can carry a length / opening-chain dimension, and
// what to say about the ones that cannot.
//
// Revit-free half of ElementDimensioner.TryWallAxis. Kept here so the rule
// "a wall that is skipped is NAMED in a warning" is one definition under test
// (StingTools.Tags.Tests) instead of a `continue` buried in each engine.

namespace StingTools.Core.Drawing.Dimensioning
{
    /// <summary>What a wall's location curve is, as far as dimensioning cares.</summary>
    public enum WallLocationKind
    {
        /// <summary>A straight location line with a usable direction.</summary>
        Line,
        /// <summary>An arc — a curved wall.</summary>
        Arc,
        /// <summary>Some other curve (ellipse, spline).</summary>
        OtherCurve,
        /// <summary>No LocationCurve at all.</summary>
        NoLocationCurve,
        /// <summary>A line of zero length, or an axis that could not be read.</summary>
        Degenerate,
    }

    public static class WallAxisRules
    {
        /// <summary>Only a straight wall has an axis to dimension along.</summary>
        public static bool HasAxis(WallLocationKind kind) => kind == WallLocationKind.Line;

        /// <summary>
        /// The warning for a wall <paramref name="pass"/> skips because it has no
        /// straight axis, naming the wall; null when the wall is dimensionable.
        /// </summary>
        public static string SkipWarning(string pass, string wallId, WallLocationKind kind)
        {
            if (HasAxis(kind)) return null;
            string why;
            switch (kind)
            {
                case WallLocationKind.Arc:
                    why = "is curved — it has no straight axis and no planar end faces to measure between";
                    break;
                case WallLocationKind.OtherCurve:
                    why = "has a non-linear location curve (not a line or arc) — no straight axis to measure along";
                    break;
                case WallLocationKind.NoLocationCurve:
                    why = "has no location curve — no axis to measure along";
                    break;
                default:
                    why = "has a zero-length or unreadable location line";
                    break;
            }
            return $"{pass}: wall {wallId} {why} — skipped, dimension it by hand.";
        }
    }
}
