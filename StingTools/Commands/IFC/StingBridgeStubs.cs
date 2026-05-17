// StingBridge — Stub implementations of the ArchiCAD and IFC bridge adapters.
//
// These classes provide compile-time stubs for the StingBridge.ArchiCAD and
// StingBridge.IFC namespaces.  Real implementations (named-pipe ArchiCAD
// Live Link, Revit IFC importer wrapper) are plugged in at deploy time.
// Until then the stubs return graceful "not available" results so the rest
// of the plugin compiles and runs cleanly.

using System;
using System.IO;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingBridge.ArchiCAD
{
    public sealed class SyncResult
    {
        public string Path    { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    public static class ArchiCADWorkflowAdapter
    {
        /// <summary>
        /// Attempts a live ArchiCAD connection first; falls back to scanning
        /// <paramref name="dropFolder"/> for waiting .ifc files.
        /// Stub: always returns a "not available" result.
        /// </summary>
        public static SyncResult Sync(Document doc, string dropFolder, bool liveFirst)
        {
            try
            {
                // Scan for IFC files already present in the drop folder.
                if (!string.IsNullOrWhiteSpace(dropFolder) && Directory.Exists(dropFolder))
                {
                    var files = Directory.GetFiles(dropFolder, "*.ifc");
                    if (files.Length > 0)
                        return new SyncResult
                        {
                            Path    = "drop-folder",
                            Summary = $"Found {files.Length} IFC file(s) in drop folder. " +
                                      "Use 'IFC Drop Import' to import them individually."
                        };
                }

                return new SyncResult
                {
                    Path    = "none",
                    Summary = "ArchiCAD Live Link is not available in this build. " +
                              "Export an IFC from ArchiCAD and place it in the drop folder, " +
                              "then use 'IFC Drop Import'."
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn("ArchiCADWorkflowAdapter.Sync: " + ex.Message);
                return new SyncResult { Path = "error", Summary = ex.Message };
            }
        }
    }
}

namespace StingBridge.IFC
{
    public enum IfcImportMode { Import, Link }

    public sealed class IfcImportResult
    {
        public bool   Success        { get; set; }
        public int    ElementsTagged { get; set; }
        public string ErrorMessage   { get; set; } = string.Empty;
    }

    public static class IfcRevitImporter
    {
        /// <summary>
        /// Imports or links an IFC file into the active document.
        /// Stub: delegates to Revit's built-in IFC link/import commands.
        /// </summary>
        public static IfcImportResult Import(Document doc, string ifcPath,
            IfcImportMode mode, bool applyTags)
        {
            if (doc == null || string.IsNullOrWhiteSpace(ifcPath))
                return new IfcImportResult { Success = false, ErrorMessage = "Invalid arguments." };

            if (!File.Exists(ifcPath))
                return new IfcImportResult { Success = false,
                    ErrorMessage = $"File not found: {ifcPath}" };

            try
            {
                // Stub: the full implementation calls Revit's IFC import/link APIs.
                // For now, report that the file is present and let the user link manually.
                return new IfcImportResult
                {
                    Success        = false,
                    ElementsTagged = 0,
                    ErrorMessage   = "IFC import bridge not yet available. " +
                                     "Use Revit's built-in Insert → Link IFC or Import IFC command."
                };
            }
            catch (Exception ex)
            {
                StingLog.Warn("IfcRevitImporter.Import: " + ex.Message);
                return new IfcImportResult { Success = false, ErrorMessage = ex.Message };
            }
        }
    }
}
