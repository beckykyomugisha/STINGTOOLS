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

            // H-1 — build the cross-host /ifc/data element payload on the Revit
            // API thread (ParameterHelpers reads must run here, not on the
            // background thread). This is the producer that feeds the server's
            // ExternalElementMapping (host="revit") so a Revit element resolves
            // cross-host by IFC GlobalId. Elements with an empty IFC_GLOBAL_ID_TXT
            // are skipped (skip-don't-mis-key) — run "Stabilize IFC GUIDs" first
            // to populate the canonical 22-char key.
            var ifcElements = BuildIfcElements(doc);
            string hostDocGuid = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";
            string revitUser = uiApp.Application?.Username ?? "";
            StingLog.Info($"IFC_PushModel: built {ifcElements.Count} IFC-data element(s) with a stabilised GlobalId.");

            // Serialise + upload on a background thread so Revit stays responsive
            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] glb = GlbSerializer.Serialize(buffers);
                    StingLog.Info($"IFC_PushModel: GLB serialised ({glb.Length / 1024:N0} kB), uploading…");

                    bool ok = await client.PostGeometryDeltaAsync(glb, System.Array.Empty<int>());
                    StingLog.Info(ok
                        ? $"IFC_PushModel: geometry upload succeeded ({glb.Length / 1024:N0} kB)"
                        : $"IFC_PushModel: geometry upload failed — {client.LastError}");

                    // H-1 — push the cross-host element identity payload to
                    // /api/projects/{id}/ifc/data (host="revit"). No-ops when no
                    // element carries a stabilised GlobalId (nothing to map).
                    if (ifcElements.Count > 0)
                    {
                        var resp = await client.PushIfcDataAsync(
                            client.CurrentProjectId, ifcElements,
                            host: "revit", hostDocumentGuid: hostDocGuid, userName: revitUser);
                        if (resp != null)
                            StingLog.Info(
                                $"IFC_PushModel: /ifc/data push OK — newMappings={resp["newMappings"]} " +
                                $"updatedMappings={resp["updatedMappings"]} newElements={resp["newElements"]} " +
                                $"updatedElements={resp["updatedElements"]} skipped={resp["skipped"]}");
                        else
                            StingLog.Warn($"IFC_PushModel: /ifc/data push failed — {client.LastError}");
                    }
                    else
                    {
                        StingLog.Info("IFC_PushModel: no stabilised IFC GlobalIds — skipping /ifc/data push. " +
                                      "Run 'Stabilize IFC GUIDs' to enable cross-host mapping.");
                    }
                }
                catch (Exception ex2)
                {
                    StingLog.Error("IFC_PushModel background task", ex2);
                }
            });

            string ifcNote = ifcElements.Count > 0
                ? $"\nPlus {ifcElements.Count:N0} element(s) to the cross-host registry (host=revit)."
                : "\n(No stabilised IFC GlobalIds yet — run 'Stabilize IFC GUIDs' for cross-host mapping.)";
            TaskDialog.Show("Push to Planscape",
                $"Uploading {buffers.Count:N0} elements in the background.{ifcNote}\n" +
                $"Check the Planscape web viewer in a few moments.");
            return Result.Succeeded;
        }

        /// <summary>
        /// H-1 — build the <c>/ifc/data</c> element payload (server
        /// <c>IfcElementDto</c> shape, camelCase). One object per element that
        /// carries a stabilised <c>IFC_GLOBAL_ID_TXT</c>; elements without one
        /// are skipped so the server never keys a mapping on a wrong id
        /// (skip-don't-mis-key, matching IfcIngestService). Must be called on the
        /// Revit API thread (reads element parameters).
        /// </summary>
        private static List<object> BuildIfcElements(Document doc)
        {
            var list = new List<object>();
            using var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (Element el in collector)
            {
                string ifcGid = ParameterHelpers.GetString(el, "IFC_GLOBAL_ID_TXT") ?? "";
                if (string.IsNullOrWhiteSpace(ifcGid)) continue;   // skip-don't-mis-key

                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC) ?? "";
                string loc  = ParameterHelpers.GetString(el, ParamRegistry.LOC)  ?? "";
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE) ?? "";
                string lvl  = ParameterHelpers.GetString(el, ParamRegistry.LVL)  ?? "";
                string sys  = ParameterHelpers.GetString(el, ParamRegistry.SYS)  ?? "";
                string func = ParameterHelpers.GetString(el, ParamRegistry.FUNC) ?? "";
                string prod = ParameterHelpers.GetString(el, ParamRegistry.PROD) ?? "";
                string seq  = ParameterHelpers.GetString(el, ParamRegistry.SEQ)  ?? "";
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1) ?? "";
                string status = ParameterHelpers.GetString(el, ParamRegistry.STATUS) ?? "";
                string rev  = ParameterHelpers.GetString(el, ParamRegistry.REV)  ?? "";
                string cat  = ParameterHelpers.GetCategoryName(el) ?? "";
                string fam  = (el as FamilyInstance)?.Symbol?.FamilyName ?? "";
                string typeName = "";
                try { typeName = doc.GetElement(el.GetTypeId())?.Name ?? ""; } catch { /* type may be invalid */ }

                bool isComplete      = !string.IsNullOrEmpty(disc) && !string.IsNullOrEmpty(seq);
                bool isFullyResolved = isComplete && !string.IsNullOrEmpty(loc) && !string.IsNullOrEmpty(lvl);

                list.Add(new
                {
                    ifcGlobalId      = ifcGid,
                    hostElementId    = el.Id.Value.ToString(),
                    hostDisplayLabel = string.IsNullOrEmpty(tag1) ? (el.Name ?? "") : tag1,
                    discipline       = disc,
                    location         = loc,
                    zone             = zone,
                    level            = lvl,
                    system           = sys,
                    function         = func,
                    product          = prod,
                    sequence         = seq,
                    fullTag          = tag1,
                    categoryName     = cat,
                    familyName       = fam,
                    typeName         = typeName,
                    status           = status,
                    rev              = rev,
                    isComplete       = isComplete,
                    isFullyResolved  = isFullyResolved,
                });
            }
            return list;
        }
    }
}
