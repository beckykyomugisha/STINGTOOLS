// StingTools — Drawing Template Manager · Phase 183
//
// LiveProfileSync tracks, per-document, which DrawingType and
// ViewStylePack ids have changed between successive
// DrawingTypeRegistry.Reload / ViewStylePackRegistry.Reload calls.
//
// Why: the previous design required the user to manually press
// "Sync Styles" after editing a profile or pack on disk. Now both
// registries auto-call OnRegistryReloaded after eviction, which
// snapshots the post-reload state and diffs it against the pre-
// reload state. The diff is cached per-document and consumed by
// Inspect / SyncStyles to surface "5 profiles changed since last
// load — 42 views need resync" without the user having to remember
// what they edited.
//
// LiveProfileSync is purely an in-memory accounting layer. It does
// no Revit-API writes; the actual resync happens through the existing
// DrawingTypePresentation.Apply / DrawingSyncStylesCommand path. By
// keeping the auto-detection passive (snapshot + diff), we avoid the
// thread-safety pitfalls an IUpdater-based approach would have when
// editing JSON outside Revit's transaction model.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core.Drawing
{
    public static class LiveProfileSync
    {
        private sealed class Snapshot
        {
            public Dictionary<string, string> ProfileChecksums { get; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> PackChecksums { get; }
                = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class StagedDiff
        {
            public HashSet<string> ChangedProfileIds { get; }
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ChangedPackIds { get; }
                = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public DateTime DetectedUtc { get; set; }

            public bool Any => ChangedProfileIds.Count > 0 || ChangedPackIds.Count > 0;
        }

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, Snapshot> _snapshots
            = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, StagedDiff> _staged
            = new Dictionary<string, StagedDiff>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            if (doc == null) return "__null__";
            try { return string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName; }
            catch { return "__unknown__"; }
        }

        /// <summary>
        /// Called by DrawingTypeRegistry.Reload + ViewStylePackRegistry.Reload
        /// AFTER the new library has been loaded. Computes fresh checksums,
        /// diffs against the previous snapshot, stages the changed ids for
        /// later consumption by Inspect / SyncStyles. Safe to call from any
        /// thread; safe to call when no document is open.
        /// </summary>
        public static void OnRegistryReloaded(Document doc)
        {
            if (doc == null) return;
            try
            {
                var fresh = BuildSnapshot(doc);
                lock (_lock)
                {
                    var key = DocKey(doc);
                    _snapshots.TryGetValue(key, out var prior);
                    if (prior != null)
                    {
                        var diff = ComputeDiff(prior, fresh);
                        if (diff.Any)
                        {
                            if (!_staged.TryGetValue(key, out var existing))
                                _staged[key] = diff;
                            else
                            {
                                foreach (var id in diff.ChangedProfileIds)
                                    existing.ChangedProfileIds.Add(id);
                                foreach (var id in diff.ChangedPackIds)
                                    existing.ChangedPackIds.Add(id);
                                existing.DetectedUtc = diff.DetectedUtc;
                            }
                            StingTools.Core.StingLog.Info(
                                $"LiveProfileSync: {diff.ChangedProfileIds.Count} profile(s) and " +
                                $"{diff.ChangedPackIds.Count} pack(s) changed since last load.");
                        }
                    }
                    _snapshots[key] = fresh;
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LiveProfileSync.OnRegistryReloaded: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the set of DrawingType ids that have changed since the
        /// last <see cref="ConsumeStagedDiff"/> call (or since session start).
        /// Read-only — does not clear the staged diff.
        /// </summary>
        public static IReadOnlyCollection<string> GetChangedProfileIds(Document doc)
        {
            lock (_lock)
            {
                if (_staged.TryGetValue(DocKey(doc), out var diff))
                    return diff.ChangedProfileIds.ToList();
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Returns the set of ViewStylePack ids that have changed since the
        /// last <see cref="ConsumeStagedDiff"/> call. Read-only.
        /// </summary>
        public static IReadOnlyCollection<string> GetChangedPackIds(Document doc)
        {
            lock (_lock)
            {
                if (_staged.TryGetValue(DocKey(doc), out var diff))
                    return diff.ChangedPackIds.ToList();
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Resolve the set of stamped View ids whose DrawingType id (or
        /// whose DrawingType's resolved ViewStylePack id) is in the
        /// staged-changed list. Caller passes this to
        /// DrawingTypePresentation.Apply inside a transaction to heal.
        /// </summary>
        public static List<ElementId> GetAffectedViewIds(Document doc)
        {
            var result = new List<ElementId>();
            if (doc == null) return result;
            HashSet<string> changedProfiles, changedPacks;
            lock (_lock)
            {
                if (!_staged.TryGetValue(DocKey(doc), out var diff)) return result;
                changedProfiles = new HashSet<string>(diff.ChangedProfileIds, StringComparer.OrdinalIgnoreCase);
                changedPacks = new HashSet<string>(diff.ChangedPackIds, StringComparer.OrdinalIgnoreCase);
            }
            if (changedProfiles.Count == 0 && changedPacks.Count == 0) return result;

            // Expand pack-change set to the profile ids that reference
            // those packs, so changing corp-coordination flags every
            // profile bound to it.
            try
            {
                foreach (var dt in DrawingTypeRegistry.ListAll(doc))
                {
                    if (dt == null || string.IsNullOrEmpty(dt.Id)) continue;
                    if (!string.IsNullOrEmpty(dt.ViewStylePackId)
                        && changedPacks.Contains(dt.ViewStylePackId))
                        changedProfiles.Add(dt.Id);
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LiveProfileSync.GetAffectedViewIds expand: {ex.Message}");
                return result;
            }

            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                {
                    if (!(el is View v) || v.IsTemplate) continue;
                    var stampedId = DrawingTypeStamper.Read(v);
                    if (string.IsNullOrEmpty(stampedId)) continue;
                    if (changedProfiles.Contains(stampedId)) result.Add(v.Id);
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LiveProfileSync.GetAffectedViewIds scan: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Clear the staged diff after the caller has acted on it.
        /// SyncStyles calls this once it has re-applied every affected
        /// view; subsequent Inspect calls then show "in sync".
        /// </summary>
        public static void ConsumeStagedDiff(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_staged.ContainsKey(key)) _staged.Remove(key);
            }
        }

        /// <summary>
        /// Invalidate snapshots + staged diffs for a document. Wired to
        /// the document-closed handler.
        /// </summary>
        public static void InvalidateCache(Document doc)
        {
            lock (_lock)
            {
                var key = DocKey(doc);
                if (_snapshots.ContainsKey(key)) _snapshots.Remove(key);
                if (_staged.ContainsKey(key)) _staged.Remove(key);
            }
        }

        // ── Internals ──

        private static Snapshot BuildSnapshot(Document doc)
        {
            var snap = new Snapshot();
            try
            {
                foreach (var dt in DrawingTypeRegistry.ListAll(doc))
                {
                    if (dt == null || string.IsNullOrEmpty(dt.Id)) continue;
                    snap.ProfileChecksums[dt.Id] = HashOf(dt);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"LiveProfileSync.BuildSnapshot profiles: {ex.Message}"); }
            try
            {
                foreach (var pack in ViewStylePackRegistry.ListAll(doc))
                {
                    if (pack == null || string.IsNullOrEmpty(pack.Id)) continue;
                    snap.PackChecksums[pack.Id] = HashOf(pack);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"LiveProfileSync.BuildSnapshot packs: {ex.Message}"); }
            return snap;
        }

        private static StagedDiff ComputeDiff(Snapshot prior, Snapshot fresh)
        {
            var diff = new StagedDiff { DetectedUtc = DateTime.UtcNow };

            // Profiles: added / removed / mutated
            foreach (var kv in fresh.ProfileChecksums)
            {
                if (!prior.ProfileChecksums.TryGetValue(kv.Key, out var oldCs)
                    || !string.Equals(oldCs, kv.Value, StringComparison.Ordinal))
                    diff.ChangedProfileIds.Add(kv.Key);
            }
            foreach (var kv in prior.ProfileChecksums)
            {
                if (!fresh.ProfileChecksums.ContainsKey(kv.Key))
                    diff.ChangedProfileIds.Add(kv.Key); // deleted — surface so views can be re-bound
            }

            // Packs: added / removed / mutated
            foreach (var kv in fresh.PackChecksums)
            {
                if (!prior.PackChecksums.TryGetValue(kv.Key, out var oldCs)
                    || !string.Equals(oldCs, kv.Value, StringComparison.Ordinal))
                    diff.ChangedPackIds.Add(kv.Key);
            }
            foreach (var kv in prior.PackChecksums)
            {
                if (!fresh.PackChecksums.ContainsKey(kv.Key))
                    diff.ChangedPackIds.Add(kv.Key);
            }

            return diff;
        }

        private static string HashOf(object obj)
        {
            try
            {
                var json = JsonConvert.SerializeObject(obj, Formatting.None);
                using (var sha = SHA256.Create())
                {
                    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                    var sb = new StringBuilder(bytes.Length * 2);
                    foreach (var b in bytes) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"LiveProfileSync.HashOf: {ex.Message}");
                return Guid.NewGuid().ToString("N"); // never collide
            }
        }
    }
}
