// StingPaths.cs — ISO 19650 consolidation (WP6).
//
// THE single legal entry point for every StingTools project path. Exports,
// imports, metadata stores, staging areas and the recycle bin all resolve
// through here, so the on-disk layout is defined in ONE place: change the
// layout (e.g. the WP9 CDE-first tree) and every caller follows without a
// site-by-site edit.
//
// This is a thin, allocation-free delegation over ProjectFolderEngine, which
// owns the actual tree, the per-document root cache and the project setup.
// New code MUST use StingPaths (or ProjectFolderEngine directly) rather than
// hand-building Path.Combine(<projectDir>, "_BIM_COORD", …) sibling paths —
// tools/check_path_discipline.ps1 fails the build on new hand-rolled siblings.

using System;
using System.IO;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>Single resolver for every StingTools project path (delegates to ProjectFolderEngine).</summary>
    public static class StingPaths
    {
        /// <summary>
        /// A CDE state folder, optionally scoped to a discipline and content type.
        /// <paramref name="state"/> is a folder id: "WIP" / "SHARED" / "PUBLISHED" /
        /// "ARCHIVE". The directory is created if missing.
        /// </summary>
        public static string Cde(Document doc, string state, string discipline = null, string contentType = null)
        {
            string dir = ProjectFolderEngine.GetFolderPath(doc, state);
            if (string.IsNullOrEmpty(dir)) return dir;
            if (!string.IsNullOrEmpty(discipline)) dir = Path.Combine(dir, discipline);
            if (!string.IsNullOrEmpty(contentType)) dir = Path.Combine(dir, contentType);
            try { Directory.CreateDirectory(dir); }
            catch (Exception ex) { StingLog.Warn($"StingPaths.Cde({state}): {ex.Message}"); }
            return dir;
        }

        /// <summary>
        /// A machine-state bucket under &lt;root&gt;/_data/&lt;bucket&gt;/… — the single home
        /// for JSON stores, registries and per-subsystem state. Replaces hand-built
        /// &lt;projectDir&gt;/_BIM_COORD / STING_BIM_MANAGER / _bim_manager siblings.
        /// </summary>
        public static string Meta(Document doc, string bucket, params string[] subParts)
            => ProjectFolderEngine.GetMetaPath(doc, bucket, subParts);

        /// <summary>The consolidated &lt;root&gt;/_data root, or a named file inside it.</summary>
        public static string Data(Document doc, string fileName = null)
            => ProjectFolderEngine.GetDataPath(doc, fileName);

        /// <summary>Transient outbound staging area (&lt;root&gt;/_data/staging/&lt;channel&gt;).</summary>
        public static string Staging(Document doc, string channel)
            => ProjectFolderEngine.GetStagingPath(doc, channel);

        /// <summary>The single project recycle bin (&lt;root&gt;/_data/recycle/).</summary>
        public static string Recycle(Document doc)
            => ProjectFolderEngine.GetRecyclePath(doc);

        /// <summary>Routed export folder for an export-type key (e.g. "PDF" / "IFC" / "BOQ").</summary>
        public static string Export(Document doc, string exportTypeKey)
            => ProjectFolderEngine.GetExportFolder(doc, exportTypeKey);

        /// <summary>Timestamped export path routed to the correct folder for an export-type key.</summary>
        public static string ExportFile(Document doc, string exportTypeKey, string baseName, string extension)
            => ProjectFolderEngine.GetExportPath(doc, exportTypeKey, baseName, extension);
    }
}
