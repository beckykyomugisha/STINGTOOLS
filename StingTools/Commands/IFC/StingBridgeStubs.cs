// StingBridge stub implementations — provides the StingBridge.IFC and
// StingBridge.ArchiCAD namespaces so the IFC command files compile without
// requiring the optional StingBridge external assembly.
//
// Replace these stubs with a real StingBridge assembly reference when
// StingBridge is available; the using directives in ArchiCADSyncCommand.cs
// and IfcDropImportCommand.cs will pick up the external types automatically.

using System;
using System.IO;
using Autodesk.Revit.DB;

namespace StingBridge.IFC
{
    public enum IfcImportMode { Link, Import }

    public class IfcImportResult
    {
        public bool   Success        { get; set; }
        public int    ElementsTagged { get; set; }
        public string ErrorMessage   { get; set; }
    }

    public static class IfcRevitImporter
    {
        public static IfcImportResult Import(
            Document doc, string ifcPath, IfcImportMode mode, bool applyTags = false)
        {
            if (!File.Exists(ifcPath))
                return new IfcImportResult { Success = false, ErrorMessage = $"File not found: {ifcPath}" };

            try
            {
                using var t = new Transaction(doc, "STING IFC Import");
                t.Start();

                var opts  = new IFCImportOptions();
                var view  = doc.ActiveView;
                ElementId importedId;
                doc.Import(ifcPath, opts, view, out importedId);

                t.Commit();
                return new IfcImportResult { Success = true, ElementsTagged = 0, ErrorMessage = string.Empty };
            }
            catch (Exception ex)
            {
                return new IfcImportResult { Success = false, ErrorMessage = ex.Message };
            }
        }
    }

    // Watches a drop folder for new .ifc files and fires FileDropped when one arrives.
    public sealed class IfcDropWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        public event Action<string> FileDropped;

        public IfcDropWatcher(string folder)
        {
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            _watcher = new FileSystemWatcher(folder, "*.ifc")
            {
                NotifyFilter        = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = false,
            };
            _watcher.Created += (_, e) => FileDropped?.Invoke(e.FullPath);
        }

        public void Start() => _watcher.EnableRaisingEvents = true;
        public void Stop()  => _watcher.EnableRaisingEvents = false;

        public void Dispose() => _watcher?.Dispose();
    }
}

namespace StingBridge.ArchiCAD
{
    public class SyncResult
    {
        public string Path    { get; set; }
        public string Summary { get; set; }
    }

    public static class ArchiCADWorkflowAdapter
    {
        public static SyncResult Sync(Document doc, string dropFolder, bool liveFirst = true)
        {
            if (!Directory.Exists(dropFolder))
                return new SyncResult
                {
                    Path    = "drop-folder",
                    Summary = $"Drop folder does not exist: {dropFolder}\n\nCreate it and export the ArchiCAD model as IFC into that folder."
                };

            string[] pending = Directory.GetFiles(dropFolder, "*.ifc");
            if (pending.Length == 0)
                return new SyncResult
                {
                    Path    = "drop-folder",
                    Summary = $"No .ifc files found in:\n{dropFolder}\n\nExport the model from ArchiCAD into that folder, then run this command again."
                };

            string newest = pending[pending.Length - 1];
            var result = StingBridge.IFC.IfcRevitImporter.Import(
                doc, newest, StingBridge.IFC.IfcImportMode.Link, applyTags: true);

            return new SyncResult
            {
                Path    = result.Success ? "IFC Link" : "failed",
                Summary = result.Success
                    ? $"Linked {Path.GetFileName(newest)} successfully."
                    : $"Import failed: {result.ErrorMessage}"
            };
        }
    }
}
