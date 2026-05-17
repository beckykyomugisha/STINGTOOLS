// IFC_PushModelCommand.cs — manual "Push Model to Planscape" command.
// Tag: IFC_PushModel
//
// Extracts tessellated geometry for ALL elements visible in the active 3D view
// (or the first available 3D view), serialises to GLB, and uploads to the
// Planscape Server federated-model delta endpoint.
//
// This is the "force full sync" path. The automatic save-triggered flow in
// GeometrySyncHandler only pushes dirty (changed) elements. This command
// pushes everything, useful after the initial project setup or after link
// file updates that wouldn't show up in the geometry diff queue.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Clash;

namespace StingTools.Commands.IFC
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class IFC_PushModelCommand : IExternalCommand
    {
        public const string Tag = "IFC_PushModel";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var doc   = uiApp?.ActiveUIDocument?.Document;
            if (doc == null) { TaskDialog.Show("Push to Planscape", "No document open."); return Result.Cancelled; }

            var client = StingTools.BIMManager.PlanscapeServerClient.Instance;
            if (client == null || !client.IsConnected)
            {
                TaskDialog.Show("Push to Planscape",
                    "Not connected to Planscape Server. Use BIM → Connect to Planscape first.");
                return Result.Cancelled;
            }

            // Find a 3D view
            View3D view3d = uiApp.ActiveUIDocument.ActiveView as View3D;
            if (view3d == null || view3d.IsTemplate)
            {
                view3d = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate);
            }
            if (view3d == null)
            {
                TaskDialog.Show("Push to Planscape", "No 3D view found. Create a 3D view first.");
                return Result.Cancelled;
            }

            // Confirm with element count
            int elementCount = new FilteredElementCollector(doc, view3d.Id)
                .WhereElementIsNotElementType()
                .GetElementCount();

            var confirm = TaskDialog.Show("Push to Planscape",
                $"Extract and upload geometry for {elementCount:N0} elements in view:\n\"{view3d.Name}\"\n\n" +
                $"This replaces the server's current model geometry for this project.\n\nProceed?",
                TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
            if (confirm != TaskDialogResult.Yes) return Result.Cancelled;

            // Extract tessellated meshes (MeshExtractor invalidates its own cache on save;
            // force invalidation here to guarantee we get fresh geometry)
            MeshExtractor.InvalidateCacheFor(doc);
            Dictionary<ClashElementKey, ClashMeshBuffer> bufferMap;
            try
            {
                bufferMap = MeshExtractor.Extract(doc, view3d);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Push to Planscape", $"Mesh extraction failed:\n{ex.Message}");
                return Result.Failed;
            }

            if (bufferMap.Count == 0)
            {
                TaskDialog.Show("Push to Planscape", "No geometry extracted from the selected view.");
                return Result.Cancelled;
            }

            var buffers = new List<ClashMeshBuffer>(bufferMap.Values);
            StingLog.Info($"IFC_PushModel: extracted {buffers.Count} element meshes, serialising to GLB…");

            // Serialise + upload on a background thread so Revit stays responsive
            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] glb = GlbSerializer.Serialize(buffers);
                    StingLog.Info($"IFC_PushModel: GLB serialised ({glb.Length / 1024:N0} kB), uploading…");

                    bool ok = await client.PostGeometryDeltaAsync(glb, System.Array.Empty<int>());
                    StingLog.Info(ok
                        ? $"IFC_PushModel: upload succeeded ({glb.Length / 1024:N0} kB)"
                        : $"IFC_PushModel: upload failed — {client.LastError}");
                }
                catch (Exception ex)
                {
                    StingLog.Error("IFC_PushModel background task", ex);
                }
            });

            TaskDialog.Show("Push to Planscape",
                $"Uploading {buffers.Count:N0} elements in the background.\n" +
                $"Check the Planscape web viewer in a few moments.");
            return Result.Succeeded;
        }
    }
}
