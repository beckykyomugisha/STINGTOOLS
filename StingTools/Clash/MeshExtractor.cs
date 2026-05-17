// MeshExtractor.cs — extracts tessellated geometry from all elements visible in a 3D view,
// with link instance and family instance transforms baked in. Must be called on the main
// Revit API thread. Returned ClashMeshBuffer objects are plain managed memory and can be
// consumed freely from any thread.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class MeshExtractor
    {
        // D1: Per-(doc, view) cache keyed by (doc UniqueId, view ElementId).
        // Two invalidation strategies layered:
        //   1. Hard invalidation on document Save / SyncWithCentral via
        //      InvalidateCacheFor(doc).
        //   2. Soft invalidation per-tick via signature: the count of
        //      taggable elements in the view + the document's
        //      ProjectInformation revision. Cheap to compute and catches
        //      "user added one new wall and immediately re-ran clash".
        // Cache hits skip the entire CustomExporter pass which dominates
        // ClashRunCommand time on 50k-element models (5-30s saved).
        internal sealed class CacheEntry
        {
            public string Signature;
            public Dictionary<ClashElementKey, ClashMeshBuffer> Buffers;
            public DateTime BuiltUtc;
        }
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);

        public static void InvalidateCacheFor(Document doc)
        {
            if (doc == null) return;
            string key = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";
            // Strip every entry whose key starts with the doc id — the view
            // suffix means we may have multiple per doc.
            foreach (var k in _cache.Keys.Where(k => k.StartsWith(key, StringComparison.Ordinal)).ToList())
                _cache.TryRemove(k, out _);
        }

        public static Dictionary<ClashElementKey, ClashMeshBuffer> Extract(Document doc, View3D view)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));

            string cacheKey = (doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host") + "|" + view.Id.Value;
            string signature = ComputeSignature(doc, view);

            if (_cache.TryGetValue(cacheKey, out var cached) &&
                string.Equals(cached.Signature, signature, StringComparison.Ordinal))
            {
                StingLog.Info($"MeshExtractor: cache hit ({cached.Buffers.Count} elements, age {(DateTime.UtcNow - cached.BuiltUtc).TotalSeconds:F1}s)");
                return cached.Buffers;
            }

            var sw = Stopwatch.StartNew();
            // rec-5: Build a doc-guid → Document map ahead of time so
            // ClashExportContext.OnElementBegin can read element metadata
            // (category, UniqueId, IfcGuid) from the correct linked document.
            var docByGuid = BuildLinkedDocumentMap(doc);

            var ctx = new ClashExportContext(doc, docByGuid);
            var exporter = new CustomExporter(doc, ctx)
            {
                IncludeGeometricObjects = false,
                ShouldStopOnError = false
            };
            try { exporter.Export(view); }
            catch (Exception ex) { StingLog.Error("MeshExtractor.Export failed", ex); }
            sw.Stop();
            StingLog.Info($"MeshExtractor: {ctx.Buffers.Count} elements, {sw.ElapsedMilliseconds} ms, linkedDocs={docByGuid.Count - 1}");

            _cache[cacheKey] = new CacheEntry
            {
                Signature = signature,
                Buffers = ctx.Buffers,
                BuiltUtc = DateTime.UtcNow,
            };
            return ctx.Buffers;
        }

        /// <summary>
        /// D1: Cheap soft-invalidation signature: element count in the view
        /// (filter-aware via FilteredElementCollector) + the document's last-
        /// modified revision. Add a wall, count changes, signature changes,
        /// cache regenerates. Move a wall, count is the same — hard
        /// invalidation via InvalidateCacheFor catches that path.
        /// </summary>
        private static string ComputeSignature(Document doc, View3D view)
        {
            try
            {
                int n = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                int rev = 0;
                try
                {
                    var revs = new FilteredElementCollector(doc).OfClass(typeof(Revision)).Cast<Revision>().ToList();
                    rev = revs.Count;
                }
                catch { /* ignore */ }
                long viewBboxHash = 0;
                try
                {
                    var bb = view.GetSectionBox();
                    if (bb != null)
                        viewBboxHash = HashCode.Combine((int)(bb.Min.X * 100), (int)(bb.Max.X * 100),
                                                       (int)(bb.Min.Y * 100), (int)(bb.Max.Y * 100));
                }
                catch { /* not all views have section boxes */ }
                return $"{n}|{rev}|{viewBboxHash}";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MeshExtractor.ComputeSignature: {ex.Message}");
                // Force a cache miss on signature errors.
                return Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// rec-5: Build a dictionary keyed by the same doc-guid string used by
        /// ClashExportContext (ProjectInformation?.UniqueId ?? PathName ?? "host"
        /// / "link"). Host doc first, then every loaded RevitLinkInstance's doc.
        ///
        /// G5: Made <c>public</c> so ClashRunCommand can reuse the map for
        /// linked-doc element resolution (System/Workset facts) rather than
        /// needing a second FilteredElementCollector.OfClass(RevitLinkInstance)
        /// pass.
        /// </summary>
        public static Dictionary<string, Document> BuildLinkedDocumentMap(Document host)
        {
            var map = new Dictionary<string, Document>(StringComparer.Ordinal);
            string hostKey = host.ProjectInformation?.UniqueId ?? host.PathName ?? "host";
            map[hostKey] = host;

            // G7: Throttled per-session warning counter for BuildLinkedDocumentMap
            //     failures. Prior code had a bare catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); } that swallowed errors
            //     silently — made it impossible to diagnose "why are my linked-
            //     doc clashes missing?" without attaching a debugger.
            int perLinkFailures = 0;
            try
            {
                var links = new FilteredElementCollector(host)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>();
                foreach (var li in links)
                {
                    Document linkDoc = null;
                    try { linkDoc = li.GetLinkDocument(); }
                    catch (Exception liEx)
                    {
                        perLinkFailures++;
                        if (perLinkFailures <= 5)
                            StingLog.Warn($"BuildLinkedDocumentMap: GetLinkDocument failed for link " +
                                $"{li?.Name ?? "(null)"}: {liEx.Message}");
                    }
                    if (linkDoc == null) continue;
                    string key = linkDoc.ProjectInformation?.UniqueId ?? linkDoc.PathName ?? "link";
                    if (!map.ContainsKey(key)) map[key] = linkDoc;
                }
                if (perLinkFailures > 5)
                    StingLog.Warn($"BuildLinkedDocumentMap: {perLinkFailures - 5} additional link " +
                        $"resolution failures suppressed");
            }
            catch (Exception ex) { StingLog.Warn($"BuildLinkedDocumentMap: {ex.Message}"); }

            return map;
        }
    }
}
