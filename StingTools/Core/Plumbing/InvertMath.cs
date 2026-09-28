// StingTools — drainage invert levels, the one calculation.
//
// WHY THIS FILE EXISTS
//
// Three places computed an invert and each got it differently wrong:
//
//   * DrainageInvertDimensioner used Pipe.Diameter / 2. Pipe.Diameter is the
//     NOMINAL size, not the bore: a "100" uPVC soil pipe is 110 OD / ~103 ID,
//     so nominal/2 can be off by several millimetres either way. It annotated
//     the MIDPOINT, which on a sloped drain is a level nobody can use.
//   * InvertLevelEngine did the same nominal/2, assumed endpoint 0 was
//     upstream, and wrote to four parameters that were defined nowhere.
//   * The manhole schedule used a connector radius (nominal again).
//
// The invert is the lowest point of the pipe BORE — centreline minus the
// INTERNAL radius (BS EN 752 / BS EN 12056-2). Revit exposes the internal
// diameter (RBS_PIPE_INNER_DIAM_PARAM); nominal is only a flagged fallback.
//
// Everything here is Revit-free and in metres, so the rules are unit-tested.
// Revit-bound callers (Core/Plumbing/PipeInvert.cs) convert units and datum.

using System;
using System.Globalization;

namespace StingTools.Core.Plumbing
{
    /// <summary>Where an invert level's radius came from.</summary>
    public enum InvertSource
    {
        /// <summary>Internal diameter — the correct basis.</summary>
        InnerDiameter,
        /// <summary>Nominal size, used only when no internal diameter is available. Flagged.</summary>
        NominalFallback,
        /// <summary>No usable diameter at all; no invert can be stated.</summary>
        None,
    }

    /// <summary>The vertical datum invert levels are reported against.</summary>
    public enum IlDatum
    {
        /// <summary>Shared coordinates via the Survey Point — m AOD on a set-up project.</summary>
        SurveyPoint,
        /// <summary>Relative to the Project Base Point.</summary>
        ProjectBasePoint,
        /// <summary>Revit's internal origin. Rarely what a drawing wants.</summary>
        InternalOrigin,
    }

    /// <summary>
    /// How invert levels are reported. THE OWNER DECISION POINT: datum and
    /// precision are presentation choices for the drainage drawing, set here once
    /// rather than scattered through the callers.
    /// </summary>
    public sealed class IlReportingOptions
    {
        public IlDatum Datum { get; set; } = IlDatum.SurveyPoint;
        /// <summary>Decimal places on a printed IL. UK drainage drawings commonly use 2 (to the centimetre).</summary>
        public int Decimals { get; set; } = 2;

        public static IlReportingOptions Default => new IlReportingOptions();

        public string DatumLabel => Datum switch
        {
            IlDatum.SurveyPoint      => "m (survey point / shared datum)",
            IlDatum.ProjectBasePoint => "m (project base point)",
            _                        => "m (internal origin)",
        };
    }

    public static class InvertMath
    {
        /// <summary>
        /// Bore invert at a point on the centreline, in metres on the caller's
        /// datum. Internal diameter wins; nominal is used only when no internal
        /// diameter is known, and says so through <paramref name="source"/>.
        /// Returns null when neither is a positive number — no invert is better
        /// than an invented one.
        /// </summary>
        public static double? Invert(double centreZ, double? innerDiameterM, double? nominalDiameterM,
            out InvertSource source)
        {
            if (innerDiameterM.HasValue && innerDiameterM.Value > 0)
            {
                source = InvertSource.InnerDiameter;
                return centreZ - innerDiameterM.Value / 2.0;
            }
            if (nominalDiameterM.HasValue && nominalDiameterM.Value > 0)
            {
                source = InvertSource.NominalFallback;
                return centreZ - nominalDiameterM.Value / 2.0;
            }
            source = InvertSource.None;
            return null;
        }

        /// <summary>
        /// Index (0 or 1) of the UPSTREAM end: the higher one. Drainage flows
        /// downhill; which end Revit calls 0 is a drawing accident. A level pipe
        /// has no upstream by geometry — 0 is returned and <paramref name="level"/> is set.
        /// </summary>
        public static int UpstreamIndex(double z0, double z1, out bool level, double toleranceM = 0.0005)
        {
            level = Math.Abs(z0 - z1) <= toleranceM;
            if (level) return 0;
            return z0 > z1 ? 0 : 1;
        }

        /// <summary>
        /// Gradient as drainage drawings print it: "1:80". Null for a level pipe
        /// (a drainage run should not be level, so the caller reports it).
        /// </summary>
        public static string GradientText(double upZ, double downZ, double horizontalRunM)
        {
            double fall = upZ - downZ;
            if (horizontalRunM <= 0 || fall <= 0.0005) return null;
            double ratio = horizontalRunM / fall;
            return "1:" + Math.Round(ratio).ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>"IL 9.95" at the configured precision, invariant culture.</summary>
        public static string FormatIl(double invertM, int decimals)
        {
            decimals = Math.Max(0, Math.Min(4, decimals));
            return "IL " + invertM.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>True when <paramref name="text"/> is a note this module writes
        /// (an IL label or a gradient), so a re-run can update it in place.</summary>
        public static bool IsOurNote(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var t = text.Trim();
            if (t.StartsWith("IL ", StringComparison.Ordinal))
                return double.TryParse(t.Substring(3), NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            if (t.StartsWith("1:", StringComparison.Ordinal))
                return int.TryParse(t.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            return false;
        }

        /// <summary>
        /// Parse a stored invert/cover value (TEXT parameter, metres, invariant or
        /// current culture). Null when blank or unparseable — never zero, because a
        /// zero invert is a real level and would be read as one.
        /// </summary>
        public static double? ParseMetres(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var t = text.Trim();
            if (t.EndsWith("m", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 1).Trim();
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
            return null;
        }

        /// <summary>Text form written to the TEXT invert/cover parameters: metres, 3 dp, invariant.</summary>
        public static string ToParamText(double metres)
            => metres.ToString("F3", CultureInfo.InvariantCulture);
    }
}
