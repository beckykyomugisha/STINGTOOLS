// CoordStores.cs — ISO 19650 consolidation (WP2).
//
// ONE resolver for the coordination JSON stores that were previously split across
// three physical folders for the same logical concept:
//
//   <rvtDir>/_bim_manager/issues.json        ← WarningsManager, LPS auto-raiser, WorkflowEngine
//   <rvtDir>/STING_BIM_MANAGER/issues.json   ← BIM Manager register, BCC
//   <root>/_data/…                           ← consolidated location
//
// The same split applied to meetings.json and to documents.json vs
// document_register.json. Issues raised by one subsystem were therefore invisible
// to the tool that manages them.
//
// Every store now resolves through this class, which:
//   * returns a path under the consolidated metadata root
//     (ProjectFolderEngine.GetMetaPath), so the user sees ONE folder per project;
//   * on first access per document, append-merges any legacy file found in the
//     other folders into the canonical one, keyed by id so rows are never lost,
//     and drops a ".migrated" marker beside the legacy file so the merge is
//     performed exactly once;
//   * offers atomic writes only — WriteArray/WriteObject go through
//     OutputLocationHelper.WriteAllTextAtomic, so a crash mid-write cannot
//     truncate a coordination store.
//
// Transmittals deliberately resolve to the "_BIM_COORD" bucket: that is where
// Planscape.Docs.Templates.TransmittalOrchestrator already persists, and WP1 made
// the auto-log path delegate to it. Pointing this resolver anywhere else would
// re-fork the store it was written to unify.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>Single resolver + atomic accessor for the project coordination stores.</summary>
    public static class CoordStores
    {
        // Canonical bucket for coordination data (issues, meetings, register, revisions).
        private const string CoordBucket = "STING_BIM_MANAGER";
        // Bucket owned by the template engine (transmittals, deliverables, workflow state).
        private const string TemplateBucket = "_BIM_COORD";

        // Legacy sibling folder names that may still hold rows for these stores.
        private static readonly string[] LegacyFolders = { "_bim_manager", "STING_BIM_MANAGER", "_BIM_COORD" };

        // Documents whose legacy merge has already run this session (path-keyed).
        private static readonly HashSet<string> _merged = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new();

        // ── Typed store paths ─────────────────────────────────────────────

        /// <summary>BIM issues (RFI/TQ/NCR/clash/design…). Legacy: _bim_manager/issues.json.</summary>
        public static string Issues(Document doc) => Resolve(doc, CoordBucket, "issues.json");

        /// <summary>Coordination meetings. Legacy: _bim_manager/meetings.json.</summary>
        public static string Meetings(Document doc) => Resolve(doc, CoordBucket, "meetings.json");

        /// <summary>
        /// Document register. Also absorbs the legacy "documents.json" spelling that
        /// WarningsManager used for the same concept.
        /// </summary>
        public static string Register(Document doc)
        {
            string path = Resolve(doc, CoordBucket, "document_register.json");
            MergeAlias(doc, path, "documents.json");
            return path;
        }

        /// <summary>Transmittals — the TransmittalOrchestrator store (TX-NNNN rows).</summary>
        public static string Transmittals(Document doc) => Resolve(doc, TemplateBucket, "transmittals.json");

        /// <summary>Revision records.</summary>
        public static string Revisions(Document doc) => Resolve(doc, CoordBucket, "revisions.json");

        // ── Document-free resolution ──────────────────────────────────────
        //
        // A few readers (the BCC meetings panels, the parallel file loader) only
        // have a directory, not a Document. They cannot trigger the legacy merge —
        // that needs a project root — so they probe the known layouts under the
        // directory they were given and read whichever exists. Writers must always
        // use the Document-based overloads above.

        /// <summary>Issues store resolved from a bare directory (read-side only).</summary>
        public static string IssuesIn(string baseDir) => ResolveIn(baseDir, "issues.json");

        /// <summary>Meetings store resolved from a bare directory (read-side only).</summary>
        public static string MeetingsIn(string baseDir) => ResolveIn(baseDir, "meetings.json");

        private static string ResolveIn(string baseDir, string fileName)
        {
            if (string.IsNullOrEmpty(baseDir)) return null;

            // The directory may already BE the store folder, or the store may sit in
            // the canonical bucket, the consolidated _data root, or the legacy sibling.
            var candidates = new[]
            {
                Path.Combine(baseDir, fileName),
                Path.Combine(baseDir, CoordBucket, fileName),
                Path.Combine(baseDir, "_data", CoordBucket, fileName),
                Path.Combine(baseDir, "_bim_manager", fileName),
            };

            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; }
                catch (Exception ex) { StingLog.Warn($"CoordStores.ResolveIn({fileName}): {ex.Message}"); }
            }
            return candidates[1];
        }

        // ── Atomic accessors ──────────────────────────────────────────────

        /// <summary>Read a store as a JSON array; empty array when absent or unreadable.</summary>
        public static JArray ReadArray(string path)
        {
            TryReadArray(path, out JArray rows);
            return rows;
        }

        /// <summary>
        /// Read a store, distinguishing "absent" (true, empty) from "exists but UNREADABLE"
        /// (false) — a locked file on a shared drive, or corrupt JSON. Callers that write the
        /// result back MUST honour a false return: treating an unreadable store as empty and
        /// saving would silently truncate a live register down to whatever rows were merged in.
        /// </summary>
        private static bool TryReadArray(string path, out JArray rows)
        {
            rows = new JArray();
            if (string.IsNullOrEmpty(path)) return true;
            if (!File.Exists(path)) return true;          // absent ⇒ legitimately empty
            try { rows = JArray.Parse(File.ReadAllText(path)); return true; }
            catch (Exception ex)
            {
                StingLog.Warn($"CoordStores.TryReadArray({Path.GetFileName(path)}): {ex.Message}");
                return false;
            }
        }

        /// <summary>Write a store atomically (temp file + File.Replace with .bak).</summary>
        public static void WriteArray(string path, JArray rows)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                OutputLocationHelper.WriteAllTextAtomic(path, (rows ?? new JArray()).ToString(Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Error($"CoordStores.WriteArray({Path.GetFileName(path)}) failed", ex); }
        }

        /// <summary>Append one row to a store atomically. Refuses to write if the store is unreadable.</summary>
        public static void Append(string path, JObject row)
        {
            if (string.IsNullOrEmpty(path) || row == null) return;
            if (!TryReadArray(path, out JArray rows))
            {
                StingLog.Warn($"CoordStores.Append refused — {path} exists but is unreadable; not overwriting it.");
                return;
            }
            rows.Add(row);
            WriteArray(path, rows);
        }

        // ── Resolution + legacy merge ─────────────────────────────────────

        private static string Resolve(Document doc, string bucket, string fileName)
        {
            string dir = null;
            try { dir = ProjectFolderEngine.GetMetaPath(doc, bucket); }
            catch (Exception ex) { StingLog.Warn($"CoordStores.Resolve({fileName}): {ex.Message}"); }

            if (string.IsNullOrEmpty(dir))
            {
                // No resolvable project root (unsaved doc): fall back to the legacy
                // sibling so behaviour is unchanged rather than silently lost.
                string docDir = null;
                try { docDir = Path.GetDirectoryName(doc?.PathName ?? ""); } catch { /* unsaved */ }
                if (string.IsNullOrEmpty(docDir)) return null;
                dir = Path.Combine(docDir, bucket);
                try { Directory.CreateDirectory(dir); } catch (Exception ex) { StingLog.Warn($"CoordStores fallback dir: {ex.Message}"); }
            }

            string canonical = Path.Combine(dir, fileName);
            MergeLegacy(doc, canonical, fileName);
            return canonical;
        }

        /// <summary>
        /// Append-merge any legacy copies of <paramref name="fileName"/> into the canonical
        /// store. Rows already present (matched on id) are skipped, so a repeated merge is a
        /// no-op and no row is ever dropped.
        /// </summary>
        private static void MergeLegacy(Document doc, string canonicalPath, string fileName)
        {
            string key = canonicalPath ?? "";
            lock (_lock)
            {
                if (_merged.Contains(key)) return;
                _merged.Add(key);
            }

            try
            {
                string owner = OwnerKey(doc);
                foreach (string legacyPath in LegacyCandidates(doc, fileName))
                {
                    if (string.Equals(legacyPath, canonicalPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!ClaimLegacyFolder(legacyPath, owner)) continue;
                    MergeFile(legacyPath, canonicalPath);
                }
            }
            catch (Exception ex) { StingLog.Warn($"CoordStores.MergeLegacy({fileName}): {ex.Message}"); }
        }

        /// <summary>Merge a differently-named legacy store (e.g. documents.json → document_register.json).</summary>
        private static void MergeAlias(Document doc, string canonicalPath, string aliasFileName)
        {
            string key = (canonicalPath ?? "") + "|" + aliasFileName;
            lock (_lock)
            {
                if (_merged.Contains(key)) return;
                _merged.Add(key);
            }

            try
            {
                string owner = OwnerKey(doc);
                foreach (string legacyPath in LegacyCandidates(doc, aliasFileName))
                {
                    if (!ClaimLegacyFolder(legacyPath, owner)) continue;
                    MergeFile(legacyPath, canonicalPath);
                }
            }
            catch (Exception ex) { StingLog.Warn($"CoordStores.MergeAlias({aliasFileName}): {ex.Message}"); }
        }

        /// <summary>
        /// Stable identity of the project a legacy folder gets merged into.
        ///
        /// The PROJECT CODE, not the root path. A path is not a stable identity: the same project
        /// opened as \\server\share\Proj and Z:\Proj yields two different absolute paths, and
        /// GetRootPath's MyDocuments fallback can produce a third — each of which would then
        /// permanently refuse to merge its own legacy folders, reporting them as claimed by
        /// "another project". Falls back to the root folder name (still not the full path) when
        /// no project code is set.
        /// </summary>
        private static string OwnerKey(Document doc)
        {
            try
            {
                string code = ProjectFolderEngine.DetectProjectCode(doc);
                if (!string.IsNullOrWhiteSpace(code)) return code.Trim();

                string root = ProjectFolderEngine.GetRootPath(doc);
                if (string.IsNullOrEmpty(root)) return "";
                string leaf = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
                return string.IsNullOrEmpty(leaf) ? "" : leaf;
            }
            catch (Exception ex) { StingLog.Warn($"CoordStores.OwnerKey: {ex.Message}"); return ""; }
        }

        private const string OwnerClaimFile = ".sting_legacy_owner";

        /// <summary>
        /// Claim a legacy store folder for one project, returning false when another project
        /// already owns it.
        ///
        /// LegacyCandidates probes the .rvt's own directory, and the pre-consolidation layout
        /// put &lt;docDir&gt;/_BIM_COORD next to the model. When two UNRELATED projects share a
        /// models directory, that sibling folder belongs to whichever project wrote it — and
        /// merging it into the other project's canonical store silently absorbs a stranger's
        /// issues, transmittals and register rows. The .migrated marker did not help: it made
        /// the outcome first-open-wins, so the rightful owner then found its own data already
        /// migrated into someone else's store.
        ///
        /// Federated models of the SAME project share a project code, so they share the claim and
        /// still merge normally.
        ///
        /// SCOPE — what this does and does not prevent. It stops a folder that another project
        /// has already claimed from being merged again, so a project cannot repeatedly absorb a
        /// neighbour's rows and the neighbour's own client reports the conflict instead of
        /// silently losing its data. It does NOT identify a legacy folder's rightful owner: if
        /// project A opens first, the unclaimed folder is still merged into A even when the rows
        /// belong to B. Attributing an unlabelled legacy folder needs a grouping signal the
        /// stores do not carry — see the guid-sharing note in docs/ROADMAP.md.
        /// </summary>
        private static bool ClaimLegacyFolder(string legacyPath, string owner)
        {
            if (string.IsNullOrEmpty(owner)) return true;   // no identity to check against

            string folder = null, claimPath = null;
            try
            {
                folder = Path.GetDirectoryName(legacyPath);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return true;
                claimPath = Path.Combine(folder, OwnerClaimFile);

                if (File.Exists(claimPath))
                {
                    string existing = (File.ReadAllText(claimPath) ?? "").Trim();
                    if (string.IsNullOrEmpty(existing)) return true;
                    if (string.Equals(existing, owner, StringComparison.OrdinalIgnoreCase)) return true;
                    StingLog.Warn($"CoordStores: legacy folder '{folder}' is claimed by project " +
                                  $"'{existing}'; skipping merge into '{owner}'.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Could not READ the claim. Without evidence of a conflicting owner, refusing
                // would strand the folder's rows forever and re-fail identically every session.
                StingLog.Warn($"CoordStores.ClaimLegacyFolder read '{legacyPath}': {ex.Message} — merging anyway.");
                return true;
            }

            // No conflicting claim. Recording ours is best-effort: legacy folders routinely sit
            // on read-only shares and in archived project directories, and refusing the merge
            // because we cannot WRITE a marker would block the very migration this exists to
            // support (pre-batch-4, migrating needed read access only).
            try
            {
                File.WriteAllText(claimPath, owner);
                try { File.SetAttributes(claimPath, FileAttributes.Hidden); } catch { /* cosmetic */ }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CoordStores: could not record ownership of '{folder}' " +
                              $"({ex.Message}); merging without a claim marker.");
            }
            return true;
        }

        /// <summary>Every place a legacy copy of a store could physically live.</summary>
        private static IEnumerable<string> LegacyCandidates(Document doc, string fileName)
        {
            var roots = new List<string>();

            try
            {
                string docDir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(docDir)) roots.Add(docDir);
            }
            catch (Exception ex) { StingLog.Warn($"CoordStores.LegacyCandidates docDir: {ex.Message}"); }

            try
            {
                string root = ProjectFolderEngine.GetRootPath(doc);
                if (!string.IsNullOrEmpty(root)) roots.Add(root);
                string data = ProjectFolderEngine.GetDataPath(doc);
                if (!string.IsNullOrEmpty(data)) roots.Add(data);
            }
            catch (Exception ex) { StingLog.Warn($"CoordStores.LegacyCandidates root: {ex.Message}"); }

            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (string folder in LegacyFolders)
                {
                    string p = Path.Combine(root, folder, fileName);
                    if (File.Exists(p)) yield return p;
                }
        }

        /// <summary>
        /// Append rows from <paramref name="legacyPath"/> that are not already in
        /// <paramref name="canonicalPath"/>, then mark the legacy file as migrated.
        /// </summary>
        private static void MergeFile(string legacyPath, string canonicalPath)
        {
            if (string.IsNullOrEmpty(canonicalPath) || !File.Exists(legacyPath)) return;
            if (File.Exists(legacyPath + ".migrated")) return;

            try
            {
                // Bail (WITHOUT marking migrated) if either side is unreadable — merging into a
                // store we failed to read would write back only the legacy rows and destroy it,
                // and marking a legacy file migrated after a failed read would strand its rows.
                if (!TryReadArray(legacyPath, out JArray legacyRows))
                {
                    StingLog.Warn($"CoordStores.MergeFile: legacy store unreadable, deferring: {legacyPath}");
                    return;
                }
                if (legacyRows.Count == 0) { MarkMigrated(legacyPath); return; }

                if (!TryReadArray(canonicalPath, out JArray canonical))
                {
                    StingLog.Warn($"CoordStores.MergeFile: canonical store unreadable, deferring merge into {canonicalPath}");
                    return;
                }
                var seen = new HashSet<string>(
                    canonical.OfType<JObject>().Select(RowId).Where(s => !string.IsNullOrEmpty(s)),
                    StringComparer.OrdinalIgnoreCase);

                int added = 0;
                foreach (var row in legacyRows.OfType<JObject>())
                {
                    string id = RowId(row);
                    if (!string.IsNullOrEmpty(id) && !seen.Add(id)) continue;
                    canonical.Add(row);
                    added++;
                }

                if (added > 0)
                {
                    WriteArray(canonicalPath, canonical);
                    StingLog.Info($"CoordStores: merged {added} row(s) from {legacyPath} → {canonicalPath}");
                }
                MarkMigrated(legacyPath);
            }
            catch (Exception ex) { StingLog.Warn($"CoordStores.MergeFile({legacyPath}): {ex.Message}"); }
        }

        /// <summary>Best-effort row identity across the several schemas these stores use.</summary>
        private static string RowId(JObject row)
        {
            if (row == null) return null;
            foreach (string k in new[] { "id", "issue_id", "document_id", "doc_id", "transmittal_id",
                                         "meeting_id", "revision_id", "Id", "DocNumber", "number" })
            {
                string v = (string)row[k];
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }

        private static void MarkMigrated(string legacyPath)
        {
            try { File.WriteAllText(legacyPath + ".migrated", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")); }
            catch (Exception ex) { StingLog.Warn($"CoordStores.MarkMigrated: {ex.Message}"); }
        }

        /// <summary>Drop the per-session merge memo (used by tests and folder-migration commands).</summary>
        public static void ResetMergeState()
        {
            lock (_lock) { _merged.Clear(); }
        }
    }
}
