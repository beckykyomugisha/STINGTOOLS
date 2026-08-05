using StingTools.Core;
// StingTools — SLD sync updater (Phase 175)
//
// IUpdater that syncs every "STING - SLD" drafting view with electrical
// model changes. Strategy per Execute call:
//
//   * One full rebuild per view IF any element was added or deleted
//     (those are structural — layout depends on the hierarchy).
//   * Otherwise, one targeted label refresh per modified element per
//     view (fast path; falls back to rebuild internally if the element
//     doesn't have a corresponding SLD symbol).
//
// Registered in StingToolsApp.OnStartup; self-gates on
// project_config.json `sld_sync_enabled`.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.UI;

namespace StingTools.Core.SLD
{
    public class SLDSyncUpdater : IUpdater
    {
        private readonly UpdaterId _updaterId;

        // Per-document gate cache. Reading project_config.json on every
        // IUpdater fire is excessive; SLDSyncToggleCommand calls
        // InvalidateGateCache after writing the new value so the cache
        // refreshes on the next fire.
        private static readonly Dictionary<string, bool> _gateCache
            = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _gateLock = new object();

        public static void InvalidateGateCache(string docPathName = null)
        {
            lock (_gateLock)
            {
                if (string.IsNullOrEmpty(docPathName)) _gateCache.Clear();
                else _gateCache.Remove(docPathName);
            }
        }

        public SLDSyncUpdater(AddInId addInId)
        {
            // Stable GUID for this updater — used to register/unregister.
            _updaterId = new UpdaterId(addInId,
                new Guid("E6E3FD2A-4E9B-4F7B-9C42-0B7D1F9E4B17"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "STING SLD Sync Updater";
        public string GetAdditionalInformation() => "Keeps STING SLD views in sync with electrical model changes.";
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                if (doc == null || !IsSyncEnabled(doc)) return;

                var added    = new HashSet<ElementId>(data.GetAddedElementIds());
                var deleted  = new HashSet<ElementId>(data.GetDeletedElementIds());
                var modified = new HashSet<ElementId>(data.GetModifiedElementIds());

                // No work to do.
                if (added.Count == 0 && deleted.Count == 0 && modified.Count == 0) return;

                var views = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewDrafting))
                    .Cast<ViewDrafting>()
                    .Where(v => v.Name?.StartsWith("STING - SLD",
                        StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
                if (views.Count == 0) return;

                bool structural = added.Count > 0 || deleted.Count > 0;

                foreach (var v in views)
                {
                    try
                    {
                        var layoutOpts = StingElectricalCommandHandler.CurrentSLDLayoutOptions;
                        var annotOpts  = StingElectricalCommandHandler.CurrentSLDAnnotationOptions;

                        if (structural)
                        {
                            // One rebuild per view, regardless of how many
                            // additions / deletions arrived in this batch.
                            SLDGenerator.UpdateSLD(doc, v, ElementId.InvalidElementId,
                                layoutOpts, annotOpts);
                        }
                        else
                        {
                            // Targeted label refresh for each modified
                            // element. Each call is O(1) on the view —
                            // avoids the per-ID full-rebuild storm.
                            foreach (var id in modified)
                            {
                                SLDGenerator.UpdateSLD(doc, v, id, layoutOpts, annotOpts);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StingTools.Core.StingLog.Warn(
                            $"SLDSyncUpdater view {v.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SLDSyncUpdater: {ex.Message}");
            }
        }

        private static bool IsSyncEnabled(Document doc)
        {
            string key = doc?.PathName ?? "";
            if (string.IsNullOrEmpty(key)) return false;

            lock (_gateLock)
            {
                if (_gateCache.TryGetValue(key, out var cached)) return cached;
            }
            bool value = ReadGateFromDisk(doc);
            lock (_gateLock) { _gateCache[key] = value; }
            return value;
        }

        private static bool ReadGateFromDisk(Document doc)
        {
            try
            {
                if (string.IsNullOrEmpty(doc?.PathName)) return false;
                string p = Path.Combine(Path.GetDirectoryName(doc.PathName), "project_config.json");
                if (!File.Exists(p)) return false;
                var root = JObject.Parse(File.ReadAllText(p));
                return (bool)(root["sld_sync_enabled"] ?? false);
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SLDSyncUpdater.ReadGateFromDisk: {ex.Message}");
                return false;
            }
        }
    }
}
