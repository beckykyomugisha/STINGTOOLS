#nullable enable annotations
// StingBridgeStubs.cs — Compile-time stubs for StingBridge types.
//
// StingBridge is a standalone executable (not a referenced library).
// These stubs let StingTools.csproj compile the IFC/ArchiCAD command
// wrappers without a direct ProjectReference to StingBridge.
//
// When StingBridge IS present as a project reference, remove this file
// (or guard with a conditional compilation symbol) to avoid duplicate-
// type errors.
//
// Types covered:
//   StingBridge.ArchiCAD — SyncResult, ArchiCADWorkflowAdapter
//   StingBridge.IFC      — IfcImportMode, IfcImportResult,
//                          IfcFileArrivedEventArgs, IfcDropWatcher,
//                          DropFolderImportEventHandler

#if !STINGBRIDGE_PROJECT_REFERENCE

using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

// ── StingBridge.ArchiCAD ──────────────────────────────────────────────────────

namespace StingBridge.ArchiCAD
{
    /// <summary>Result of an ArchiCAD ↔ Revit sync attempt.</summary>
    public class SyncResult
    {
        public bool   Success       { get; set; }
        public int    ElementsSynced { get; set; }
        public string Summary       { get; set; } = "";
        /// <summary>Which path was taken: "live" | "ifc-drop" | "none".</summary>
        public string Path          { get; set; } = "none";
    }

    /// <summary>
    /// Stub adapter — tries the ArchiCAD named-pipe live link first; falls back
    /// to scanning the IFC drop folder. The real implementation lives in
    /// StingBridge/src/ArchiCAD/ArchiCADWorkflowAdapter.cs.
    /// </summary>
    public static class ArchiCADWorkflowAdapter
    {
        public static SyncResult Sync(
            Document doc,
            string   dropFolder,
            bool     liveFirst = true)
        {
            // Live-link path — StingBridge.exe is not in-process; show guidance.
            if (liveFirst)
                StingLog.Info("ArchiCADWorkflowAdapter (stub): live link not available in-process — falling back to IFC drop folder.");

            // IFC drop-folder path.
            if (!string.IsNullOrEmpty(dropFolder) && Directory.Exists(dropFolder))
            {
                var ifcFiles = Directory.GetFiles(dropFolder, "*.ifc", SearchOption.TopDirectoryOnly);
                if (ifcFiles.Length > 0)
                {
                    string newest = ifcFiles[0];
                    foreach (var f in ifcFiles)
                        if (File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(newest)) newest = f;

                    var result = StingBridge.IFC.IfcRevitImporter.Import(
                        doc, newest, StingBridge.IFC.IfcImportMode.Link, applyTags: true);

                    return new SyncResult
                    {
                        Success        = result.Success,
                        ElementsSynced = result.ElementsTagged,
                        Summary        = result.Success
                                            ? $"Linked {System.IO.Path.GetFileName(newest)} — {result.ElementsTagged} elements stamped."
                                            : $"IFC import failed: {result.ErrorMessage}",
                        Path           = "ifc-drop",
                    };
                }
            }

            return new SyncResult
            {
                Success = false,
                Summary = "No ArchiCAD live link available and no IFC files found in drop folder.",
                Path    = "none",
            };
        }
    }
}

// ── StingBridge.IFC ───────────────────────────────────────────────────────────

namespace StingBridge.IFC
{
    /// <summary>How to bring an IFC file into Revit.</summary>
    public enum IfcImportMode
    {
        /// <summary>Keep the IFC as a live linked document (non-destructive).</summary>
        Link,
        /// <summary>Convert IFC geometry into native Revit elements (destructive).</summary>
        Import,
    }

    /// <summary>Result of an IFC import / link operation.</summary>
    public class IfcImportResult
    {
        public bool   Success        { get; set; }
        public string SourceFile     { get; set; } = "";
        public int    ElementsTagged { get; set; }
        public string ErrorMessage   { get; set; } = "";
    }

    /// <summary>
    /// Stub IFC importer. The production implementation with survey-origin
    /// translation and STING tag stamping lives in
    /// StingBridge/src/IFC/IfcRevitImporter.cs and runs in the StingBridge
    /// process. This stub allows StingTools.csproj to compile when StingBridge
    /// is not referenced as a project — it logs a diagnostic and returns a
    /// graceful failure so the caller can surface a user-friendly message.
    /// </summary>
    public static class IfcRevitImporter
    {
        public static IfcImportResult Import(
            Document      doc,
            string        ifcPath,
            IfcImportMode mode,
            bool          applyTags = true)
        {
            if (!File.Exists(ifcPath))
                return new IfcImportResult { ErrorMessage = $"IFC file not found: {ifcPath}" };

            StingLog.Warn(
                $"IfcRevitImporter (stub): StingBridge is not loaded in-process. " +
                $"Run StingBridge.exe separately and use the drop-folder at " +
                $"{System.IO.Path.GetDirectoryName(ifcPath)} to import '{System.IO.Path.GetFileName(ifcPath)}'.");

            return new IfcImportResult
            {
                Success      = false,
                SourceFile   = ifcPath,
                ErrorMessage = "StingBridge not available in-process. Use StingBridge.exe for IFC import.",
            };
        }
    }

    /// <summary>Event args raised when a new IFC file arrives in the drop folder.</summary>
    public sealed class IfcFileArrivedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public IfcFileArrivedEventArgs(string path) => FilePath = path;
    }

    /// <summary>
    /// Watches a drop folder for newly-arrived .ifc files and raises
    /// <see cref="FileArrived"/> for each one. Stub version wraps
    /// <see cref="FileSystemWatcher"/>; the full implementation includes
    /// file-ready polling and step-format detection.
    /// </summary>
    public sealed class IfcDropWatcher : IDisposable
    {
        public event EventHandler<IfcFileArrivedEventArgs>? FileArrived;

        private readonly FileSystemWatcher _fsw;
        private bool _disposed;

        public IfcDropWatcher(string dropRoot)
        {
            if (!Directory.Exists(dropRoot))
                Directory.CreateDirectory(dropRoot);

            _fsw = new FileSystemWatcher(dropRoot, "*.ifc")
            {
                NotifyFilter        = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents  = false,
            };
            _fsw.Created += OnFileCreated;
        }

        public void Start()
        {
            if (_disposed) return;
            _fsw.EnableRaisingEvents = true;
            StingLog.Info($"IfcDropWatcher (stub): watching {_fsw.Path}");
        }

        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            // Small delay so the file writer has time to flush.
            System.Threading.Thread.Sleep(500);
            FileArrived?.Invoke(this, new IfcFileArrivedEventArgs(e.FullPath));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _fsw.EnableRaisingEvents = false;
            _fsw.Dispose();
        }
    }

    /// <summary>
    /// Revit external event handler that runs an IFC import on the Revit API
    /// thread in response to a drop-folder file-arrived event.
    /// </summary>
    public sealed class DropFolderImportEventHandler : IExternalEventHandler
    {
        private readonly Document _doc;
        private readonly string   _ifcPath;

        public DropFolderImportEventHandler(Document doc, string ifcPath)
        {
            _doc    = doc;
            _ifcPath = ifcPath;
        }

        public void Execute(UIApplication app)
        {
            if (_doc == null || !File.Exists(_ifcPath)) return;
            try
            {
                var result = IfcRevitImporter.Import(_doc, _ifcPath, IfcImportMode.Link, applyTags: true);
                StingLog.Info(result.Success
                    ? $"DropFolderImportEventHandler: linked '{System.IO.Path.GetFileName(_ifcPath)}', {result.ElementsTagged} elements tagged."
                    : $"DropFolderImportEventHandler: import failed — {result.ErrorMessage}");
            }
            catch (Exception ex)
            {
                StingLog.Error("DropFolderImportEventHandler.Execute failed", ex);
            }
        }

        public string GetName() => "STING Drop-Folder IFC Import";
    }
}

#endif // !STINGBRIDGE_PROJECT_REFERENCE
