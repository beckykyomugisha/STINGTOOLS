// MeshExtractor.cs — extracts tessellated geometry from all elements visible in a 3D view,
// with link instance and family instance transforms baked in. Must be called on the main
// Revit API thread. Returned ClashMeshBuffer objects are plain managed memory and can be
// consumed freely from any thread.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class MeshExtractor
    {
        public static Dictionary<ClashElementKey, ClashMeshBuffer> Extract(Document doc, View3D view)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (view == null) throw new ArgumentNullException(nameof(view));

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
            return ctx.Buffers;
        }

        /// <summary>
        /// rec-5: Build a dictionary keyed by the same doc-guid string used by
        /// ClashExportContext (ProjectInformation?.UniqueId ?? PathName ?? "host"
        /// / "link"). Host doc first, then every loaded RevitLinkInstance's doc.
        /// </summary>
        private static Dictionary<string, Document> BuildLinkedDocumentMap(Document host)
        {
            var map = new Dictionary<string, Document>(StringComparer.Ordinal);
            string hostKey = host.ProjectInformation?.UniqueId ?? host.PathName ?? "host";
            map[hostKey] = host;

            try
            {
                var links = new FilteredElementCollector(host)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>();
                foreach (var li in links)
                {
                    Document linkDoc = null;
                    try { linkDoc = li.GetLinkDocument(); }
                    catch { /* unloaded link — skip */ }
                    if (linkDoc == null) continue;
                    string key = linkDoc.ProjectInformation?.UniqueId ?? linkDoc.PathName ?? "link";
                    if (!map.ContainsKey(key)) map[key] = linkDoc;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildLinkedDocumentMap: {ex.Message}"); }

            return map;
        }
    }
}
