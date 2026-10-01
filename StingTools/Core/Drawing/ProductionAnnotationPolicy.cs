// StingTools — Drawing Template Manager · which annotation passes production runs (DTW-114)
//
// Production runs a drawing type's annotation pack on the views it makes. It used to
// run NOTHING on a view it found already produced: the idempotent refresh hard-coded
// every pass off, because the passes once doubled on a re-run. So a re-run never
// tagged or dimensioned anything modelled since the first run, and no command ran a
// drawing type's pack on an existing view.
//
// The passes are now idempotent (C-4, DTW-83/84/85, provenance on the dimensioners),
// so a refresh runs the same passes a new view gets — the dialog's annotation boxes
// and preset fields decide, exactly as for a new view. One piece is still not
// idempotent and stays off on a refresh:
//
//   * the decorative pass's MATCHLINE FRAME (pack.matchlineOffsetMm) — four plain
//     detail lines with no record of what drew them, so a second run draws a second
//     frame. The north arrow / scale bar / key plan (one instance per family per
//     view) and the flow-arrow symbols (indexed by host) ARE idempotent, but they
//     share the decorative switch with the frame, so when the pack draws a frame the
//     whole decorative switch is held back on a refresh and the reason is reported.
//
// Revit-free so the decision is tested (ProductionAnnotationPolicyTests).

namespace StingTools.Core.Drawing
{
    /// <summary>The passes one production run asks the annotation runner for.</summary>
    public sealed class AnnotationPassChoice
    {
        public bool SkipTags       { get; set; }
        public bool SkipDims       { get; set; }
        public bool SkipDecorative { get; set; }
        public bool SkipSpots      { get; set; }
        /// <summary>Why a requested pass was held back on a refresh; null when none was.</summary>
        public string HeldBack     { get; set; }

        public bool RunsAnything => !(SkipTags && SkipDims && SkipDecorative && SkipSpots);
    }

    public static class ProductionAnnotationPolicy
    {
        public const string MatchlineHeldBack =
            "decorative pass not re-run: this drawing type draws a matchline frame "
            + "(matchlineOffsetMm), which is plain detail lines with no record of what drew "
            + "them, so a re-run would draw a second frame — north arrow, scale bar, key plan "
            + "and flow arrows are therefore not refreshed either";

        /// <summary>
        /// The passes for one view. <paramref name="runAnnotation"/> is the master switch;
        /// the four <c>run*</c> flags are the dialog / preset boxes (true when no preset);
        /// <paramref name="refresh"/> is true for a view production found already made.
        /// </summary>
        public static AnnotationPassChoice Choose(bool runAnnotation, bool runTags, bool runDims,
            bool runDecorative, bool runSpots, bool refresh, bool packDrawsMatchlineFrame)
        {
            if (!runAnnotation)
                return new AnnotationPassChoice { SkipTags = true, SkipDims = true, SkipDecorative = true, SkipSpots = true };

            var c = new AnnotationPassChoice
            {
                SkipTags       = !runTags,
                SkipDims       = !runDims,
                SkipDecorative = !runDecorative,
                SkipSpots      = !runSpots,
            };
            if (refresh && runDecorative && packDrawsMatchlineFrame)
            {
                c.SkipDecorative = true;
                c.HeldBack = MatchlineHeldBack;
            }
            return c;
        }

        /// <summary>The per-view report line for a refresh; null when it placed nothing.</summary>
        public static string RefreshLine(string viewName, int tags, int dims, int spotsAndSymbols)
        {
            if (tags <= 0 && dims <= 0 && spotsAndSymbols <= 0) return null;
            return $"'{viewName}' (already produced): annotated what was modelled since — "
                 + $"{tags} tag(s), {dims} dimension(s), {spotsAndSymbols} spot / symbol(s).";
        }
    }
}
