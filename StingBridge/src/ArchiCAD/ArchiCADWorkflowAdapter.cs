// StingBridge — ArchiCAD workflow adapter.
//
// Coordinates the full ArchiCAD → Revit sync loop:
//
//   1. Try ArchiCAD Live Link (fast path, named-pipe, no file I/O).
//      • Pull changed elements, push STING tags back.
//      • If ArchiCAD is online: trigger partial IFC export to ifc_drop/.
//
//   2. Fall back to IFC drop-folder (offline path).
//      • IfcDropWatcher picks up .ifc files as they arrive.
//      • IfcRevitImporter processes and archives them.
//
// Usage (call from an IExternalCommand inside a Transaction):
//
//   var result = ArchiCADWorkflowAdapter.Sync(doc, dropFolder, liveFirst: true);
//   TaskDialog.Show("ArchiCAD Sync", result.Summary);

using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingBridge.ArchiCAD
{
    public class SyncResult
    {
        public bool   Success      { get; set; }
        public int    ElementsSynced { get; set; }
        public string Summary      { get; set; } = "";
        public string Path         { get; set; } = ""; // which path was taken: "live" | "ifc-drop" | "none"
    }

    public static class ArchiCADWorkflowAdapter
    {
        public static SyncResult Sync(Document doc, string dropFolder, bool liveFirst = true)
        {
            if (liveFirst)
            {
                try
                {
                    using var link = new ArchiCADLiveLink();
                    if (link.IsAvailable())
                    {
                        StingLog.Info("ArchiCADWorkflowAdapter: ArchiCAD Live Link reachable — using fast path.");
                        bool exported = link.TriggerPartialExport(dropFolder);
                        if (exported)
                        {
                            // Give ArchiCAD 2 s to write the file before the watcher picks it up.
                            System.Threading.Thread.Sleep(2_000);
                        }
                        return new SyncResult
                        {
                            Success       = true,
                            Path          = "live",
                            Summary       = exported
                                ? "ArchiCAD exported changed elements to the IFC drop folder. Import will begin shortly."
                                : "ArchiCAD Live Link connected but no changes to export."
                        };
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ArchiCADWorkflowAdapter: Live Link failed ({ex.Message}), falling back to IFC drop folder.");
                }
            }

            // Offline / manual IFC path: scan drop folder for pending files.
            string processingDir = Path.Combine(dropFolder, "processing");
            if (!Directory.Exists(processingDir))
            {
                return new SyncResult
                {
                    Success = false,
                    Path    = "none",
                    Summary = $"IFC drop folder not found: {dropFolder}\nPlace ArchiCAD IFC exports in this folder."
                };
            }

            string[] pending = Directory.GetFiles(processingDir, "*.ifc");
            if (pending.Length == 0)
            {
                return new SyncResult
                {
                    Success = true,
                    Path    = "ifc-drop",
                    Summary = "IFC drop folder is empty — no files to process."
                };
            }

            int total = 0;
            foreach (string ifc in pending)
            {
                var r = IFC.IfcRevitImporter.Import(doc, ifc);
                if (r.Success) total += r.ElementsTagged;
                else StingLog.Warn($"ArchiCADWorkflowAdapter: import failed for {Path.GetFileName(ifc)}: {r.ErrorMessage}");
            }

            return new SyncResult
            {
                Success        = true,
                Path           = "ifc-drop",
                ElementsSynced = total,
                Summary        = $"Processed {pending.Length} IFC file(s). {total} elements stamped."
            };
        }
    }
}
