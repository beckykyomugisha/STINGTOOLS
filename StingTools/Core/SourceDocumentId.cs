// SourceDocumentId.cs — the ONE derivation of "which authoring document is this".
//
// The server links a published ProjectModel to the FederatedElement rows the
// geometry-delta pipeline pushed by comparing the source-document GUID the two
// requests carry. That link is only as good as the two strings being identical,
// and the two are produced by different commands in different folders — a
// classic place for a silent drift (one falls back to PathName, the other to
// Title, and a model delete quietly stops retiring anything).
//
// So both call this. It is deliberately the same expression the clash tree has
// always used for ClashElementKey.DocGuid, because the delta's element ids come
// from those keys.
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    public static class SourceDocumentId
    {
        /// <summary>
        /// Stable per-document identity: <c>ProjectInformation.UniqueId</c>,
        /// falling back to the model path, then to a literal so callers never
        /// have to null-check.
        ///
        /// <para>The UniqueId survives Save-As and renames, which is what makes
        /// it usable as a cross-request key. The PathName fallback covers an
        /// unsaved or family-less document within a single session only —
        /// it is not stable across a move, and a link built on it is best
        /// effort.</para>
        /// </summary>
        public static string For(Document doc)
            => doc?.ProjectInformation?.UniqueId ?? doc?.PathName ?? "host";
    }
}
