// ══════════════════════════════════════════════════════════════════════════
//  CostDirtyMarker.cs — Phase 2D. Feeds the incremental host take-off.
//
//  A lightweight IUpdater that records which cost-relevant elements changed so
//  the next incremental BOQ refresh re-takes-off only those (instead of walking
//  the whole model). Mirrors StingStaleMarker's registration shape.
//
//  Correctness rails (the incremental result must never diverge from a full
//  rebuild):
//    • Watches cost categories with GetChangeTypeAny — ANY change (geometry or
//      parameter) to a watched element marks it dirty, so a localised re-takeoff
//      reproduces exactly what a full walk would.
//    • A change to an ElementType (a type edit affecting many instances) forces
//      a full rebuild — instances aren't individually marked, so we can't safely
//      localise it.
//    • Level / grid changes force a full rebuild (they move many lines' Level /
//      Location fields).
//    • A large bulk edit (> cap) forces a full rebuild rather than classifying
//      thousands of ids in the updater.
//  Adds / deletes are handled by the id-universe diff in BuildHostRawItems, so
//  the marker only needs to track modifications. Document close + rate/measure
//  config changes invalidate the cache elsewhere.
//
//  Execute touches only static state (BOQCostManager dirty set / force-full
//  flag) — no model writes, no transactions.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class StingCostDirtyMarker : IUpdater
    {
        private static StingCostDirtyMarker _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled;

        /// <summary>WP6 — monotonically increasing model-edit epoch. Bumped each
        /// time this updater records a cost-relevant change, so downstream caches
        /// (e.g. <see cref="BOQBccBridge"/>) can detect an in-place edit that
        /// leaves the document path unchanged and rebuild instead of serving
        /// stale rates into the 4D/5D cash-flow.</summary>
        public static long ChangeEpoch;

        // Above this many changed elements in one transaction, force a full
        // rebuild rather than classify each id.
        private const int BulkForceFullThreshold = 500;

        private StingCostDirtyMarker(AddInId addInId)
        {
            _updaterId = new UpdaterId(addInId, new Guid("C0577D1A-7E2B-4F3C-9A6D-2B8E1F4A5C7D"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.FloorsRoofsStructuralWalls;
        public string GetUpdaterName() => "STING Cost Dirty Marker";
        public string GetAdditionalInformation() => "Records cost-relevant element changes for incremental BOQ take-off.";

        public static bool IsEnabled => _enabled;

        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new StingCostDirtyMarker(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);
                StingLog.Info("StingCostDirtyMarker registered (disabled).");
            }
            catch (Exception ex) { StingLog.Error("StingCostDirtyMarker.Register", ex); }
        }

        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    var costCats = new List<BuiltInCategory>();
                    var enums = SharedParamGuids.AllCategoryEnums;
                    if (enums != null) costCats.AddRange(enums);
                    if (costCats.Count > 0)
                        UpdaterRegistry.AddTrigger(_updaterId,
                            new ElementMulticategoryFilter(costCats), Element.GetChangeTypeAny());

                    // Force-full categories (broad impact). Built defensively —
                    // a category Revit won't accept in a filter just gets skipped.
                    foreach (var bic in new[] { BuiltInCategory.OST_Levels, BuiltInCategory.OST_Grids, BuiltInCategory.OST_Materials })
                    {
                        try
                        {
                            UpdaterRegistry.AddTrigger(_updaterId,
                                new ElementCategoryFilter(bic), Element.GetChangeTypeAny());
                        }
                        catch (Exception ex) { StingLog.WarnRateLimited("DirtyTrigger", $"trigger {bic}: {ex.Message}"); }
                    }
                    _enabled = true;
                    StingLog.Info("StingCostDirtyMarker enabled.");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                    StingLog.Info("StingCostDirtyMarker disabled.");
                }
            }
            catch (Exception ex) { StingLog.Error("StingCostDirtyMarker.SetEnabled", ex); }
        }

        public static void Unregister()
        {
            try { if (_instance != null) UpdaterRegistry.UnregisterUpdater(_updaterId); }
            catch (Exception ex) { StingLog.Warn($"StingCostDirtyMarker unregister: {ex.Message}"); }
        }

        public void Execute(UpdaterData data)
        {
            try
            {
                if (BOQCostManager.IsDirtySuppressed) return;   // plugin's own writes
                Document doc = data?.GetDocument();
                if (doc == null) return;

                var modified = data.GetModifiedElementIds();
                var added = data.GetAddedElementIds();
                int total = (modified?.Count ?? 0) + (added?.Count ?? 0);
                if (total == 0) return;
                if (total > BulkForceFullThreshold) { BOQCostManager.ForceHostFull(doc); return; }

                bool forceFull = false;
                var dirty = new List<long>(total);
                void Classify(ICollection<ElementId> ids)
                {
                    if (ids == null) return;
                    foreach (var id in ids)
                    {
                        Element el;
                        try { el = doc.GetElement(id); }
                        catch (Exception ex) { StingLog.WarnRateLimited("CostDirty.GetEl", $"GetElement({id}): {ex.Message}"); continue; }
                        if (el == null) continue;
                        // A type / level / grid / material edit is broad-impact.
                        if (el is ElementType || el is Level || el is Grid || el is Material)
                        { forceFull = true; continue; }
                        if (id != null && id.Value > 0) dirty.Add(id.Value);
                    }
                }
                Classify(modified);
                Classify(added);

                if (forceFull) BOQCostManager.ForceHostFull(doc);
                else if (dirty.Count > 0) BOQCostManager.MarkHostDirty(doc, dirty);

                // WP6 — bump the model-edit epoch so caches keyed off it (e.g.
                // BOQBccBridge, which feeds 4D/5D cash-flow) rebuild instead of
                // serving stale rates after an in-place geometry/type edit.
                if (forceFull || dirty.Count > 0)
                    System.Threading.Interlocked.Increment(ref ChangeEpoch);
            }
            catch (Exception ex) { StingLog.WarnRateLimited("CostDirtyExec", $"StingCostDirtyMarker.Execute: {ex.Message}"); }
        }
    }
}
