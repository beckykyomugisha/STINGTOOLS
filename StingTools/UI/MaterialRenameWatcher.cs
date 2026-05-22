using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// I-5 — Detect Material renames that happen OUTSIDE the MAT panel
    /// (i.e. via Revit's native Material Browser, Rhino import, Dynamo, …).
    /// Subscribes to Application.DocumentChanged, snapshots material names
    /// per document, and diffs to find renames.
    ///
    /// When a rename is detected:
    ///   • MaterialNameCache.Invalidate(doc)
    ///   • MaterialUsageIndex.Invalidate(doc)
    ///   • CobieMaterialBridge.RenameInCobie(oldName → newName)
    ///   • Audit-log a MAT_ExternalRename entry
    /// </summary>
    public static class MaterialRenameWatcher
    {
        private static bool _subscribed;
        private static readonly object _lock = new object();
        private static readonly ConcurrentDictionary<long, string> _snapshot
            = new ConcurrentDictionary<long, string>();

        public static void Subscribe(Application app)
        {
            if (_subscribed || app == null) return;
            lock (_lock)
            {
                if (_subscribed) return;
                try
                {
                    app.DocumentChanged += OnDocumentChanged;
                    _subscribed = true;
                    StingLog.Info("MaterialRenameWatcher: subscribed to DocumentChanged.");
                }
                catch (Exception ex) { StingLog.Warn($"MaterialRenameWatcher.Subscribe: {ex.Message}"); }
            }
        }

        private static void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            try
            {
                var doc = e.GetDocument();
                if (doc == null) return;
                // Modified element ids contain Material ids when names change.
                var modifiedIds = e.GetModifiedElementIds();
                if (modifiedIds == null || modifiedIds.Count == 0) return;
                foreach (var id in modifiedIds)
                {
                    var el = doc.GetElement(id);
                    if (el is Material mat)
                    {
                        long key = mat.Id.Value;
                        string newName = mat.Name ?? "";
                        if (_snapshot.TryGetValue(key, out string oldName) &&
                            !string.Equals(oldName, newName, StringComparison.Ordinal))
                        {
                            HandleRename(doc, oldName, newName);
                        }
                        _snapshot[key] = newName;
                    }
                }
            }
            catch (Exception ex) { StingLog.WarnRateLimited("MatRename.OnChange", $"OnDocumentChanged: {ex.Message}"); }
        }

        public static void Seed(Document doc)
        {
            if (doc == null) return;
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(Material));
                foreach (Material m in col) _snapshot[m.Id.Value] = m.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"MaterialRenameWatcher.Seed: {ex.Message}"); }
        }

        private static void HandleRename(Document doc, string oldName, string newName)
        {
            try
            {
                StingLog.Info($"MaterialRenameWatcher: external rename '{oldName}' → '{newName}'.");
                MaterialNameCache.Invalidate(doc);
                MaterialUsageIndex.Invalidate(doc);
                try { CobieMaterialBridge.RenameInCobie(doc, oldName, newName); }
                catch (Exception ex) { StingLog.Warn($"MatRename Cobie: {ex.Message}"); }
                MaterialAuditLogger.Log(doc, "MAT_ExternalRename", newName,
                    new Dictionary<string, object> { ["oldName"] = oldName, ["newName"] = newName });
            }
            catch (Exception ex) { StingLog.Warn($"HandleRename: {ex.Message}"); }
        }
    }
}
