// MeshExtractor.cs — extracts tessellated geometry from all elements visible in a 3D view,
// with link instance and family instance transforms baked in. Must be called on the main
// Revit API thread. Returned ClashMeshBuffer objects are plain managed memory and can be
// consumed freely from any thread.
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var ctx = new ClashExportContext(doc);
            var exporter = new CustomExporter(doc, ctx)
            {
                IncludeGeometricObjects = false,
                ShouldStopOnError = false
            };
            try { exporter.Export(view); }
            catch (Exception ex) { StingLog.Error("MeshExtractor.Export failed", ex); }
            sw.Stop();
            StingLog.Info($"MeshExtractor: {ctx.Buffers.Count} elements, {sw.ElapsedMilliseconds} ms");
            return ctx.Buffers;
        }
    }
}
