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

                // C1 - CHECK BEFORE DRAINING.
                //
                // Draining is destructive: the ids leave the queue and the only
                // other copy is the local list below. PostGeometryDeltaAsync
                // returns false immediately when CurrentProjectId is unset, so
                // draining first meant every edit made before the document was
                // linked to a project was silently and permanently discarded -
                // no error, no retry, and the model on the server just quietly
                // stopped matching the model in Revit.
                var client = StingTools.BIMManager.PlanscapeServerClient.Instance;
                if (client == null || !client.IsConnected)
                {
                    StingLog.Info("GeometrySyncHandler: not connected - leaving changes queued for the next save.");
                    return;
                }
                if (client.CurrentProjectId == Guid.Empty)
                {
                    StingLog.Info(
                        "GeometrySyncHandler: this document is not linked to a Planscape project - " +
                        "leaving changes queued. Publish the model or link the project to start syncing.");
                    return;
                }

                var dirtyIds = LiveClashUpdater.DrainGeometrySyncIds(doc);
                if (dirtyIds.Count == 0) return;

                // Separate additions/modifications from deletions. The sign IS
                // the semantics (see GeometrySyncPlan), so it lives in one
                // tested place rather than being re-derived here.
                var (changedIds, deletedIds) = GeometrySyncPlan.Partition(dirtyIds);

                // Extract mesh geometry on the Revit API thread (required)
                var buffers = new List<ClashMeshBuffer>(changedIds.Count);
                var extractedIds = new List<long>(changedIds.Count);
                string docGuid = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";
                foreach (long eid in changedIds)
                {
                    var buf = TryExtractElement(doc, eid, docGuid);
                    if (buf != null) { buffers.Add(buf); extractedIds.Add(eid); }
                }
                // Only what was actually sendable is worth retrying — an element
                // that cannot be tessellated would fail again on every save.
                var attemptedIds = GeometrySyncPlan.BuildRetrySet(extractedIds, deletedIds);

                StingLog.Info($"GeometrySyncHandler: {buffers.Count} changed, {deletedIds.Count} deleted");

                if (buffers.Count == 0 && deletedIds.Count == 0) return;

                // Serialise + HTTP off the Revit API thread. Still detached from
                // the API thread (it must be), but no longer fire-and-FORGET:
                // the result decides whether the drained ids are dropped or put
                // back.
                var capturedBuffers  = buffers;
                var capturedDeleted  = deletedIds;
                var capturedAttempts = attemptedIds;
                var capturedDocGuid  = docGuid;
                _ = Task.Run(async () =>
                {
                    bool delivered = false;
                    string reason = "unknown";
                    try
                    {
                        byte[] glb = capturedBuffers.Count > 0
                            ? GlbSerializer.Serialize(capturedBuffers)
                            : Array.Empty<byte>();

                        var c = StingTools.BIMManager.PlanscapeServerClient.Instance;
                        if (c == null || !c.IsConnected)
                        {
                            reason = "connection dropped before the upload started";
                        }
                        else
                        {
                            // C1 - the return value was previously discarded, so
                            // a non-2xx, an expired token or an unset project id
                            // all looked identical to success.
                            delivered = await c.PostGeometryDeltaAsync(glb, capturedDeleted);
                            if (delivered)
                                StingLog.Info($"GeometrySyncHandler: delta uploaded ({glb.Length / 1024} kB, {capturedDeleted.Count} tombstones)");
                            else
                                reason = c.LastError ?? "server rejected the delta";
                        }
                    }
                    catch (Exception ex)
                    {
                        reason = ex.Message;
                    }

                    if (!delivered) RequeueForRetry(capturedDocGuid, capturedAttempts, reason);
                });
            }
            catch (Exception ex)
            {
                StingLog.Error("GeometrySyncHandler.Execute", ex);
            }
        }

        /// <summary>
        /// C1 - put drained ids back so the next save retries them.
        ///
        /// <para>Without this a failed upload was terminal: the ids had already
        /// left the queue, the task's result was discarded, and the elements
        /// were never considered again. The Revit model and the server model
        /// diverged permanently, and nothing anywhere said so - the next edit to
        /// a DIFFERENT element would sync fine, which makes the gap look like it
        /// never happened.</para>
        ///
        /// <para>Re-queued rather than retried in a loop on purpose: the trigger
        /// is a document save, so the natural retry is the next one. A tight
        /// retry loop here would hammer an unreachable server from a background
        /// thread with no backoff and no way for the user to stop it.</para>
        /// </summary>
        private static void RequeueForRetry(string docGuid, List<long> elementIds, string reason)
        {
            try
            {
                if (elementIds == null || elementIds.Count == 0) return;
                foreach (long id in elementIds)
                    LiveClashUpdater.GeometrySyncQueue.Enqueue((docGuid, id));

                StingLog.Warn(
                    $"GeometrySyncHandler: delta upload failed ({reason}) - " +
                    $"{elementIds.Count} element(s) re-queued and will retry on the next save.");
            }
            catch (Exception ex)
            {
                // If even the re-queue fails the changes really are lost, so say
                // so loudly rather than letting it read as a delivered delta.
                StingLog.Error(
                    $"GeometrySyncHandler: delta upload failed AND {elementIds?.Count ?? 0} element(s) " +
                    "could not be re-queued - these changes will not reach the server.", ex);
            }
        }

        // ── Per-element tessellation ─────────────────────────────────────────

        private static ClashMeshBuffer TryExtractElement(Document doc, long elementId, string docGuid)
        {
            try
            {
                var el = doc.GetElement(new ElementId(elementId));
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

                // BLK-1 / Drift 4 — ONE canonical cross-host key. The geometry
                // GLB path now reads the SAME source the tag-sync path
                // (PlatformLinkCommands.cs:2179) + the server ExternalElementMapping
                // use: the stabilised IFC_GLOBAL_ID_TXT shared param, written by
                // StabilizeIfcGuidsCommand from Revit's IfcGloballyUniqueId. This
                // is deliberately NOT BuiltInParameter.IFC_GUID — the live value
                // can re-map on export (the exact reason Stabilize snapshots it),
                // and NOT Element.UniqueId (45-char, ≠ the 22-char IFC GlobalId).
                // Empty until the model is stabilised; left empty here (the mesh
                // still carries elementId + UniqueId for LOCAL clash) so geometry
                // is never keyed on a wrong cross-host id — matching 8486cf0's
                // skip-don't-mis-key rule. Run "Stabilize IFC GUIDs" for cross-host.
                string ifcGuid = ParameterHelpers.GetString(el, "IFC_GLOBAL_ID_TXT");

                var key = new ClashElementKey(docGuid, -1, el.Id.Value, el.UniqueId, ifcGuid);
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
