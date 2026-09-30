// StingTools — Drawing Types QA / finishing rules (Revit-free).
//
// The decisions the drawing QA tools make (Sync Styles, Heal Title Blocks,
// Renumber, managed templates) live here, free of the Revit API, so they can
// be unit-tested by StingTools.Tags.Tests through <Compile Include>. The Revit
// callers gather facts, ask these rules, and act on the answer.

using System;

namespace StingTools.Core.Drawing
{
    internal static class DrawingQaRules
    {
        /// <summary>
        /// DTW-2: may a cached managed-template id be returned as-is? Only when
        /// the template's stamped checksum equals the pack's current checksum.
        /// An empty stamp is never current — the template was never stamped by
        /// the syncer, or the stamp was cleared, so it must be re-applied.
        /// </summary>
        internal static bool IsCachedTemplateCurrent(string storedChecksum, string currentChecksum)
            => !string.IsNullOrEmpty(storedChecksum)
               && string.Equals(storedChecksum, currentChecksum, StringComparison.Ordinal);
    }
}
