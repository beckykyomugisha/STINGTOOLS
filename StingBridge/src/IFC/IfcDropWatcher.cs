// StingBridge — IFC Drop-folder watcher.
//
// Monitors a configurable "hot folder" (default: <project>/_ifc_drop/) for
// incoming .ifc files produced by ArchiCAD, Tekla, Vectorworks, or any
// IFC-authoring tool.  On arrival the file is:
//
//   1. Validated (header check — not zero-byte, is STEP/P21 format).
//   2. Moved to <project>/_ifc_drop/processing/ to prevent double-pick.
//   3. Queued for import via IfcRevitImporter (Revit IFC import pipeline).
//   4. On success, archived to <project>/_ifc_drop/done/YYYYMMDD_<filename>.
//   5. On failure, moved to <project>/_ifc_drop/failed/ with a .log sidecar.
//
// The watcher runs as a background thread and raises IfcFileArrived events
// that the Revit ExternalEventHandler picks up on the main thread.

using System;
using System.IO;
using System.Threading;
using StingTools.Core;

namespace StingBridge.IFC
{
    public class IfcFileArrivedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public IfcFileArrivedEventArgs(string path) => FilePath = path;
    }

    public sealed class IfcDropWatcher : IDisposable
    {
        public event EventHandler<IfcFileArrivedEventArgs>? FileArrived;

        private FileSystemWatcher? _watcher;
        private readonly string _dropRoot;
        private bool _disposed;

        public IfcDropWatcher(string dropRoot)
        {
            _dropRoot = dropRoot;
        }

        public void Start()
        {
            Directory.CreateDirectory(_dropRoot);
            Directory.CreateDirectory(Path.Combine(_dropRoot, "processing"));
            Directory.CreateDirectory(Path.Combine(_dropRoot, "done"));
            Directory.CreateDirectory(Path.Combine(_dropRoot, "failed"));

            _watcher = new FileSystemWatcher(_dropRoot, "*.ifc")
            {
                NotifyFilter         = NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents  = true
            };

            _watcher.Created += OnCreated;
            StingLog.Info($"IfcDropWatcher: monitoring {_dropRoot}");
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            // Wait up to 5 s for the writing application to finish flushing.
            if (!WaitForFile(e.FullPath, TimeSpan.FromSeconds(5)))
            {
                StingLog.Warn($"IfcDropWatcher: timed out waiting for {e.Name} to be released.");
                return;
            }

            if (!IsStepFormat(e.FullPath))
            {
                StingLog.Warn($"IfcDropWatcher: {e.Name} does not appear to be a valid IFC/STEP file.");
                return;
            }

            string dest = Path.Combine(_dropRoot, "processing", e.Name);
            try
            {
                File.Move(e.FullPath, dest, overwrite: true);
                FileArrived?.Invoke(this, new IfcFileArrivedEventArgs(dest));
            }
            catch (Exception ex)
            {
                StingLog.Error($"IfcDropWatcher: could not move {e.Name} to processing", ex);
            }
        }

        private static bool WaitForFile(string path, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true;
                }
                catch (IOException) { Thread.Sleep(250); }
            }
            return false;
        }

        private static bool IsStepFormat(string path)
        {
            try
            {
                using var sr = new StreamReader(path);
                string? firstLine = sr.ReadLine();
                return firstLine != null && firstLine.TrimStart().StartsWith("ISO-10303", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _watcher?.Dispose();
        }
    }
}
