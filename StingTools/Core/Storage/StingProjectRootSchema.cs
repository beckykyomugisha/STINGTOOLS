// StingProjectRootSchema.cs — ISO 19650 consolidation (WP9, safe additive step).
//
// Stamps the STING project-folder root onto the document's ProjectInformation as an
// Extensible Storage entity, so the root becomes a STABLE, STORED identity rather than a
// value recomputed from the project number on every resolve.
//
// The problem it fixes: ProjectFolderEngine.GetRootPath derives the root folder name from
// the Revit project Number (<projDir>/<CODE>). If the number is edited, the derived CODE
// changes and a brand-new sibling root is minted — the project's exports fork into a second
// tree. Storing the resolved root (relative to the .rvt) means a later rename no longer
// forks: GetRootPath reads the stamped path and keeps using the original root.
//
// Fully additive + graceful:
//   * Read needs no transaction; GetRootPath prefers the stamp only when it resolves to an
//     existing directory, else it falls through to the unchanged per-document resolution.
//   * EnsureStamped writes best-effort — inside the caller's transaction if one is open,
//     otherwise it opens its own short transaction (the pattern the HVAC climate stamp uses
//     from OnDocumentOpened). Any failure is swallowed; a missing stamp just means the old
//     behaviour. It never force-moves anything.

using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using StingTools.Core;

namespace StingTools.Core.Storage
{
    public static class StingProjectRootSchema
    {
        public static readonly Guid SchemaGuid =
            new Guid("C4D5E6F7-2233-4455-A677-1B2C3D4E5F60");

        private const string SchemaName    = "StingProjectRootSchema";
        private const string FieldRelPath  = "RootRelativePath";
        private const string FieldStampUtc  = "StampedUtcTicks";

        public class Stamp
        {
            public string RootRelativePath;
            public long   StampedUtcTicks;
        }

        public static Schema GetOrCreate()
        {
            try
            {
                var existing = Schema.Lookup(SchemaGuid);
                if (existing != null) return existing;

                var sb = new SchemaBuilder(SchemaGuid);
                sb.SetSchemaName(SchemaName);
                sb.SetVendorId(StingSchemaBuilder.VendorId);
                sb.SetReadAccessLevel(AccessLevel.Public);
                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                sb.AddSimpleField(FieldRelPath, typeof(string))
                    .SetDocumentation("STING project-folder root, relative to the .rvt directory");
                sb.AddSimpleField(FieldStampUtc, typeof(long))
                    .SetDocumentation("DateTime.UtcNow.Ticks when the root was first stamped");
                return sb.Finish();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingProjectRootSchema.GetOrCreate: {ex.Message}");
                return null;
            }
        }

        public static Stamp Read(Document doc)
        {
            if (doc?.ProjectInformation == null) return null;
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;
                var entity = doc.ProjectInformation.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return new Stamp
                {
                    RootRelativePath = entity.Get<string>(FieldRelPath),
                    StampedUtcTicks  = entity.Get<long>(FieldStampUtc),
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingProjectRootSchema.Read: {ex.Message}");
                return null;
            }
        }

        /// <summary>Write the stamp. Requires an active transaction (see EnsureStamped).</summary>
        public static bool Write(Document doc, string rootRelativePath)
        {
            if (doc?.ProjectInformation == null || string.IsNullOrEmpty(rootRelativePath)) return false;
            try
            {
                var schema = GetOrCreate();
                if (schema == null) return false;
                var entity = new Entity(schema);
                entity.Set(FieldRelPath, rootRelativePath);
                entity.Set(FieldStampUtc, DateTime.UtcNow.Ticks);
                doc.ProjectInformation.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingProjectRootSchema.Write: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resolve the stamped root to an absolute path, or null when unstamped / unresolvable /
        /// no longer on disk. No transaction required.
        /// </summary>
        public static string ResolveStampedRoot(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                var stamp = Read(doc);
                if (stamp == null || string.IsNullOrEmpty(stamp.RootRelativePath)) return null;
                string rvtDir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(rvtDir)) return null;
                string resolved = Path.GetFullPath(Path.Combine(rvtDir, stamp.RootRelativePath));
                return Directory.Exists(resolved) ? resolved : null;
            }
            catch (Exception ex) { StingLog.Warn($"StingProjectRootSchema.ResolveStampedRoot: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Best-effort: stamp the current resolved root if it is not already stamped. Writes
        /// inside the caller's transaction when one is open, otherwise opens a short one.
        /// No-op for family / read-only / unsaved docs, or when already stamped. Never throws.
        /// </summary>
        public static bool EnsureStamped(Document doc)
        {
            try
            {
                if (doc?.ProjectInformation == null) return false;
                if (doc.IsFamilyDocument || doc.IsReadOnly) return false;
                if (string.IsNullOrEmpty(doc.PathName)) return false;
                if (Read(doc) != null) return false; // already stamped

                string root = ProjectFolderEngine.GetRootPath(doc);
                string rvtDir = Path.GetDirectoryName(doc.PathName);
                if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(rvtDir)) return false;

                string rel = MakeRelative(rvtDir, root);
                if (string.IsNullOrEmpty(rel)) return false;

                if (doc.IsModifiable) return Write(doc, rel); // a transaction is already open

                using (var t = new Transaction(doc, "STING: Stamp project root identity"))
                {
                    if (t.Start() != TransactionStatus.Started) return false;
                    bool ok = Write(doc, rel);
                    if (ok) t.Commit(); else t.RollBack();
                    return ok;
                }
            }
            catch (Exception ex) { StingLog.Warn($"StingProjectRootSchema.EnsureStamped: {ex.Message}"); return false; }
        }

        private static string MakeRelative(string baseDir, string target)
        {
            try
            {
                // Different volumes (or local vs UNC) have no relative path between them —
                // Uri.MakeRelativeUri would return an ABSOLUTE "file:///…" string, which then
                // gets stamped and can never resolve. Since EnsureStamped refuses to re-stamp
                // once a stamp exists, that would poison the root permanently. This is reachable:
                // GetRootPath falls back to MyDocuments/<code> when the project dir is unwritable
                // (e.g. the .rvt sits on a read-only share).
                string baseRoot   = Path.GetPathRoot(Path.GetFullPath(baseDir))   ?? "";
                string targetRoot = Path.GetPathRoot(Path.GetFullPath(target))    ?? "";
                if (!string.Equals(baseRoot, targetRoot, StringComparison.OrdinalIgnoreCase)) return null;

                var baseUri = new Uri(AppendSlash(baseDir));
                var targetUri = new Uri(AppendSlash(target));
                var relUri = baseUri.MakeRelativeUri(targetUri);
                if (relUri.IsAbsoluteUri) return null;

                string rel = Uri.UnescapeDataString(relUri.ToString())
                    .Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

                // Reject anything that still looks absolute / scheme-qualified.
                if (string.IsNullOrEmpty(rel)) return ".";
                if (rel.IndexOf(':') >= 0 || rel.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
                return rel;
            }
            catch { return null; }
        }

        private static string AppendSlash(string p) =>
            p.EndsWith(Path.DirectorySeparatorChar.ToString()) ? p : p + Path.DirectorySeparatorChar;
    }
}
