// StingTools — Drawing Template Manager · the context tags exterior elevations carry
//
// DTW-80. Exterior elevations are produced by one routine
// (ProduceExteriorElevationsCommand.Produce), called by DOCS → Exterior Elevations and
// by the Project Setup wizard. The tags it uses, and the older ones it adopts, live
// here so the two callers and the tests cannot disagree about them.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Drawing
{
    public static class ExteriorElevationTags
    {
        /// <summary>The tag four faces share when they go on one 1+4 sheet (rules 0-3).</summary>
        public const string Combined = "Exterior";

        /// <summary>The raw tag an earlier build and the old Setup Wizard stamped.</summary>
        public const string LegacyPrefix = "exterior::face::";

        /// <summary>The tag of one face on its own (rule 0): "Exterior-North".</summary>
        public static string PerFace(string face) => Combined + "-" + (face ?? "");

        /// <summary>The raw tag an earlier build or the old wizard stamped for this face.</summary>
        public static string Legacy(string face) => LegacyPrefix + (face ?? "");

        /// <summary>True for any raw legacy tag.</summary>
        public static bool IsLegacy(string stamp)
            => !string.IsNullOrEmpty(stamp) && stamp.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);

        /// <summary>True when <paramref name="stamp"/> is the raw legacy tag of <paramref name="face"/>.</summary>
        public static bool IsLegacyFor(string stamp, string face)
            => string.Equals(stamp, Legacy(face), StringComparison.OrdinalIgnoreCase);

        /// <summary>The STING_VIEW_CONTEXT_TAG stamp the producer writes for a tag:
        /// no level, room or box, so "::::Exterior-North".</summary>
        public static string Stamp(string tag) => ProductionContextKey.Compose(null, null, null, tag, null, null);

        /// <summary>
        /// True when a view stamped <paramref name="stamp"/> is the per-face view of
        /// <paramref name="face"/> — the one a views-only run (the wizard, or DOCS with
        /// sheets off) makes, and which a 1+4 run adopts rather than duplicating.
        /// </summary>
        public static bool IsPerFaceStamp(string stamp, string face)
            => string.Equals(stamp, Stamp(PerFace(face)), StringComparison.Ordinal);

        /// <summary>
        /// True when a view stamped <paramref name="stamp"/> and named
        /// <paramref name="viewName"/> is the <paramref name="face"/> view of a 1+4 set —
        /// such views are named "&lt;type&gt; - Exterior - &lt;Face&gt;". The name is the
        /// only thing that says which face a combined rule was, so a renamed view is not
        /// recognised (and a views-only run makes its own view rather than guess).
        /// </summary>
        public static bool IsCombinedStampFor(string stamp, string viewName, string face)
            => string.Equals(stamp, Stamp(Combined), StringComparison.Ordinal)
            && !string.IsNullOrEmpty(viewName) && !string.IsNullOrEmpty(face)
            && viewName.EndsWith(" - " + face, StringComparison.OrdinalIgnoreCase);
    }
}
