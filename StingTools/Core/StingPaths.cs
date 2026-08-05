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
        /// <summary>The four ISO 19650 CDE containers, in lifecycle order.</summary>
        public static readonly string[] CdeStates = { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };

        /// <summary>
        /// Map any recorded CDE value onto one of <see cref="CdeStates"/>. Deliverable records
        /// carry free text ("Published", "ARCHIVED", "Work in Progress", …) and an unrecognised
        /// value would otherwise route the file to the MISC bucket — outside every container the
        /// move-on-transition purge scans, so the stale copy is never cleaned up. Unknown values
        /// fall back to WIP, the least-published container.
        /// </summary>
        public static string NormalizeCdeState(string state)
        {
            string s = (state ?? "").Trim().ToUpperInvariant();
            if (s.Length == 0) return "WIP";
            if (s.StartsWith("PUBLISH")) return "PUBLISHED";   // PUBLISH / PUBLISHED
            if (s.StartsWith("ARCHIV"))  return "ARCHIVE";     // ARCHIVE / ARCHIVED / ARCHIVAL
            if (s.StartsWith("SHARE"))   return "SHARED";      // SHARE / SHARED
            if (s.StartsWith("WIP") || s.StartsWith("WORK")) return "WIP";
            StingLog.Warn($"StingPaths.NormalizeCdeState: unrecognised CDE state '{state}' — routing to WIP.");
            return "WIP";
        }

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

        /// <summary>
        /// A single FILE inside a metadata bucket, resolved so that migrating a call site
        /// off a hand-built <c>&lt;rvtDir&gt;/&lt;bucket&gt;/&lt;file&gt;</c> sibling cannot strand an
        /// existing project's data.
        /// <para>Resolution order:</para>
        /// <list type="number">
        ///   <item>the consolidated <c>&lt;root&gt;/_data/&lt;bucket&gt;/&lt;file&gt;</c>, if it exists;</item>
        ///   <item>the legacy <c>&lt;rvtDir&gt;/&lt;bucket&gt;/&lt;file&gt;</c> sibling, if THAT exists —
        ///         so a project whose data predates consolidation keeps reading and writing
        ///         its own file until the user runs Folders_Consolidate;</item>
        ///   <item>otherwise the consolidated path, so data that does not exist yet is
        ///         BORN consolidated and no new sibling folder is ever created.</item>
        /// </list>
        /// <para>
        /// This is the write-side counterpart to
        /// <see cref="ProjectFolderEngine.ResolveProjectOverridePath"/>, which returns null
        /// when neither location exists and so cannot be used to decide where to write.
        /// Deliberately moves nothing: relocating a user's files is the consented
        /// <see cref="ProjectFolderEngine.MigrateFromLegacy"/> path's job, not a side effect
        /// of opening a store.
        /// </para>
        /// <para>
        /// Returns null for an unsaved document — callers uniformly treat that as
        /// "no project on disk, keep state in memory".
        /// </para>
        /// </summary>
        public static string MetaFile(Document doc, string bucket, params string[] subPathParts)
        {
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
            if (string.IsNullOrEmpty(bucket)) return null;
            if (subPathParts == null || subPathParts.Length == 0) return null;

            string rel = subPathParts[0];
            for (int i = 1; i < subPathParts.Length; i++)
            {
                if (!string.IsNullOrEmpty(subPathParts[i])) rel = Path.Combine(rel, subPathParts[i]);
            }
            if (string.IsNullOrEmpty(rel)) return null;

            string consolidated = null;
            try
            {
                string metaDir = Meta(doc, bucket);
                if (!string.IsNullOrEmpty(metaDir)) consolidated = Path.Combine(metaDir, rel);
            }
            catch (Exception ex) { StingLog.Warn($"StingPaths.MetaFile({bucket}/{rel}): {ex.Message}"); }

            try
            {
                if (!string.IsNullOrEmpty(consolidated) && File.Exists(consolidated)) return consolidated;

                string legacy = ProjectFolderEngine.GetLegacyMetaDir(doc, bucket);
                if (!string.IsNullOrEmpty(legacy))
                {
                    // path-discipline: legacy-fallback -- pre-consolidation data still in place
                    string legacyFile = Path.Combine(legacy, rel);
                    if (File.Exists(legacyFile)) return legacyFile;
                }
            }
            catch (Exception ex) { StingLog.Warn($"StingPaths.MetaFile({bucket}/{rel}) probe: {ex.Message}"); }

            return consolidated;
        }

        /// <summary>
        /// Document-free <see cref="Meta"/> — for the callers that only ever hold a model
        /// PATH (the dock-panel preset UIs, registries invoked off the Revit thread).
        /// Falls back to the legacy sibling when the project has no set-up root yet.
        /// Prefer the Document overload wherever a Document is in scope.
        /// </summary>
        public static string MetaFrom(string rvtPath, string bucket, params string[] subParts)
            => ProjectFolderEngine.GetMetaPathForModelPath(rvtPath, bucket, subParts);

        /// <summary>
        /// Document-free <see cref="MetaFile"/>. Same three-step resolution — existing
        /// consolidated file, then existing legacy sibling, then consolidated — so a
        /// project whose data predates consolidation keeps working untouched.
        /// </summary>
        public static string MetaFileFrom(string rvtPath, string bucket, params string[] subPathParts)
        {
            if (string.IsNullOrEmpty(rvtPath) || string.IsNullOrEmpty(bucket)) return null;
            if (subPathParts == null || subPathParts.Length == 0) return null;

            string rel = subPathParts[0];
            for (int i = 1; i < subPathParts.Length; i++)
                if (!string.IsNullOrEmpty(subPathParts[i])) rel = Path.Combine(rel, subPathParts[i]);
            if (string.IsNullOrEmpty(rel)) return null;

            try
            {
                string metaDir = MetaFrom(rvtPath, bucket);
                string consolidated = string.IsNullOrEmpty(metaDir) ? null : Path.Combine(metaDir, rel);
                if (!string.IsNullOrEmpty(consolidated) && File.Exists(consolidated)) return consolidated;

                string projDir = Path.GetDirectoryName(rvtPath);
                if (!string.IsNullOrEmpty(projDir))
                {
                    // path-discipline: legacy-fallback -- pre-consolidation data still in place
                    string legacy = Path.Combine(projDir, bucket, rel);
                    if (File.Exists(legacy)) return legacy;
                }
                return consolidated;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingPaths.MetaFileFrom({bucket}/{rel}): {ex.Message}");
                return null;
            }
        }

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
