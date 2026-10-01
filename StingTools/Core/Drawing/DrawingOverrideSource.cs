// StingTools — Drawing Template Manager
//
// DrawingOverrideSource — which copy of a project's drawing-type override to
// read when there are two. Revit-free so the decision is unit-tested.
//
// DTW-184: ES_Migrate copies <project>/_BIM_COORD/drawing_types.json into
// Extensible Storage once, and DrawingTypeRegistry then read ES only. The
// Drawing Type editor and the Excel import both write the FILE, so after a
// migration every later edit was saved, reported as saved, and ignored. The
// ES entry carries the UTC ticks of its write; the file has a last-write
// time. The newer one is the user's latest intent.

using System;

namespace StingTools.Core.Drawing
{
    public enum DrawingOverrideOrigin { None, ExtensibleStorage, File }

    public static class DrawingOverrideSource
    {
        /// <summary>
        /// Pick the override to load. ES wins when the file is absent, when ES
        /// has no timestamp (an entry from before the field was written — keep
        /// the behaviour it was written under), or when the file is not newer
        /// than the ES write. The file wins when it is strictly newer, i.e. it
        /// was written after the migration.
        /// </summary>
        public static DrawingOverrideOrigin Choose(
            bool esHasJson, long esUpdatedUtcTicks,
            bool fileExists, DateTime fileLastWriteUtc)
        {
            if (!esHasJson) return fileExists ? DrawingOverrideOrigin.File : DrawingOverrideOrigin.None;
            if (!fileExists) return DrawingOverrideOrigin.ExtensibleStorage;
            if (esUpdatedUtcTicks <= 0) return DrawingOverrideOrigin.ExtensibleStorage;
            return fileLastWriteUtc.Ticks > esUpdatedUtcTicks
                ? DrawingOverrideOrigin.File
                : DrawingOverrideOrigin.ExtensibleStorage;
        }
    }
}
