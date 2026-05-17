// GeometrySyncHandler.cs — IExternalEventHandler that fires after DocumentSaved
// (or DocumentSynchronizedWithCentral on workshared models) to push delta geometry
// to the Planscape federated-model endpoint.
//
// Flow:
//   1. OnDocumentSaved checks LiveClashUpdater.GeometrySyncQueue → raises this event
//   2. Execute() drains dirty element IDs for the active document
//   3. For each element: extract triangulated geometry via Element.get_Geometry()
//   4. Deleted elements (negative IDs) are included as empty-mesh tombstones
//   5. Serialise to GLB via GlbSerializer (off-thread) → POST to server
//
// HTTP must NEVER happen on the Revit API thread — all network calls are
// Task.Run fire-and-forget, consistent with the rest of the plugin.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Clash;
using System.Linq;

namespace StingTools.Commands.IFC
{
    public sealed class GeometrySyncHandler : IExternalEventHandler
    {
        private static GeometrySyncHandler _inst;
        public static ExternalEvent Event { get; private set; }

        private GeometrySyncHandler() { }

        public static GeometrySyncHandler Instance
        {
            get
            {
                if (_inst == null) { _inst = new GeometrySyncHandler(); Event = ExternalEvent.Create(_inst); }
                return _inst;
            }
        }

        // Raise the event only when the server client is connected and geometry
        // sync is enabled. Silently skips if conditions aren't met.
        public static void RaiseIfConnected()
        {
            try
            {
                var client = StingTools.BIMManager.PlanscapeServerClient.Instance;
                if (client == null || !client.IsConnected) return;
                var _ = Instance; // ensure created
                Event?.Raise();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GeometrySyncHandler.RaiseIfConnected: {ex.Message}");
            }
        }

        public string GetName() => "STING Geometry Sync Handler";

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null || doc.IsFamilyDocument) return;

                var dirtyIds = LiveClashUpdater.DrainGeometrySyncIds(doc);
                if (dirtyIds.Count == 0) return;

                // Separate additions/modifications from deletions
                var changedIds = new List<int>();
                var deletedIds = new List<int>();
                foreach (int id in dirtyIds)
                {
                    if (id < 0) deletedIds.Add(-id);
                    else        changedIds.Add(id);
                }

                // Extract mesh geometry on the Revit API thread (required)
                var buffers = new List<ClashMeshBuffer>(changedIds.Count);
                string docGuid = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";
                foreach (int eid in changedIds)
                {
                    var buf = TryExtractElement(doc, eid, docGuid);
                    if (buf != null) buffers.Add(buf);
                }

                StingLog.Info($"GeometrySyncHandler: {buffers.Count} changed, {deletedIds.Count} deleted");

                if (buffers.Count == 0 && deletedIds.Count == 0) return;

                // Fire-and-forget: serialise + HTTP off the Revit API thread
                var capturedBuffers  = buffers;
                var capturedDeleted  = deletedIds;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        byte[] glb = capturedBuffers.Count > 0
                            ? GlbSerializer.Serialize(capturedBuffers)
                            : Array.Empty<byte>();

                        var client = StingTools.BIMManager.PlanscapeServerClient.Instance;
                        if (client == null || !client.IsConnected) return;

                        await client.PostGeometryDeltaAsync(glb, capturedDeleted);
                        StingLog.Info($"GeometrySyncHandler: delta uploaded ({glb.Length / 1024} kB, {capturedDeleted.Count} tombstones)");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"GeometrySyncHandler background upload: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                StingLog.Error("GeometrySyncHandler.Execute", ex);
            }
        }

        // ── Per-element tessellation ─────────────────────────────────────────

        private static ClashMeshBuffer TryExtractElement(Document doc, int elementId, string docGuid)
        {
            try
            {
                var el = doc.GetElement(new ElementId((long)elementId));
                if (el == null || el.Category == null) return null;

                var opts = new Options
                {
                    DetailLevel             = ViewDetailLevel.Medium,
                    ComputeReferences       = false,
                    IncludeNonVisibleObjects = false
                };
                var geom = el.get_Geometry(opts);
                if (geom == null) return null;

                var verts   = new List<float>(256);
                var indices = new List<int>(256);
                CollectGeometry(geom, verts, indices);

                if (verts.Count == 0) return null;

                // IFC GUID: ExportUtils is in RevitAPIIFC.dll (not referenced).
                // UniqueId is the same base string Revit uses to derive IFC GUIDs —
                // consistent with ClashExportContext.TryGetIfcGuid pattern.
                string ifcGuid = el.UniqueId;

                var key = new ClashElementKey(docGuid, -1, (int)el.Id.Value, el.UniqueId, ifcGuid);
                return new ClashMeshBuffer(key, el.Category.Name, verts.ToArray(), indices.ToArray());
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GeometrySyncHandler.TryExtractElement({elementId}): {ex.Message}");
                return null;
            }
        }

        private static void CollectGeometry(GeometryElement geom, List<float> verts, List<int> indices)
        {
            foreach (GeometryObject obj in geom)
                CollectObject(obj, verts, indices);
        }

        private static void CollectObject(GeometryObject obj, List<float> verts, List<int> indices)
        {
            if (obj is Solid solid && solid.Volume > 0)
            {
                foreach (Face face in solid.Faces)
                {
                    Mesh mesh = face.Triangulate();
                    if (mesh == null) continue;
                    int baseIdx = verts.Count / 3;
                    foreach (XYZ pt in mesh.Vertices)
                    {
                        verts.Add((float)pt.X);
                        verts.Add((float)pt.Y);
                        verts.Add((float)pt.Z);
                    }
                    for (int t = 0; t < mesh.NumTriangles; t++)
                    {
                        var tri = mesh.get_Triangle(t);
                        indices.Add(baseIdx + (int)tri.get_Index(0));
                        indices.Add(baseIdx + (int)tri.get_Index(1));
                        indices.Add(baseIdx + (int)tri.get_Index(2));
                    }
                }
            }
            else if (obj is GeometryInstance gi)
            {
                // GetInstanceGeometry() applies the instance transform (family/link placement)
                var inst = gi.GetInstanceGeometry();
                if (inst != null) CollectGeometry(inst, verts, indices);
            }
            else if (obj is GeometryElement ge)
            {
                CollectGeometry(ge, verts, indices);
            }
        }
    }
}
