// ══════════════════════════════════════════════════════════════════════════
//  StingCostStaleMarker.cs — IUpdater that flags BOQ rows as stale when
//  geometry, material assignment, or type swap changes invalidate the
//  last-costed line item.
//
//  Mirrors the StingStaleMarker pattern (geometry → STING_STALE_BOOL = 1)
//  but writes ASS_CST_STALE_BOOL + ASS_CST_STALE_REASON_TXT so cost has
//  its own dirty-flag independent of tag staleness.
//
//  Triggers on three Revit changes:
//    1. GetChangeTypeGeometry()                    → "Geometry"
//    2. GetChangeTypeParameter(MATERIAL_ID_PARAM)  → "Material"   (when bound)
//    3. GetChangeTypeElementAddition()             → not used; new elements
//                                                    have no cost yet
//
//  Performance guards (mirrors StingStaleMarker):
//    - 20-element-per-trigger cap so a bulk paste doesn't fan out
//    - LRU eviction of the recently-processed set at 10 000 entries
//    - Disabled by default; user toggles via Cost_ToggleStaleMarker
//
//  Workshared-safe: skips elements not checked out by the current user.
//
//  P2 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace StingTools.Core
{
    public class StingCostStaleMarker : IUpdater
    {
        private static StingCostStaleMarker _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled = false;

        private const int MaxElementsPerTrigger = 20;
        private const int RecentlyProcessedMax = 10000;
        private const int RecentlyProcessedEvictCount = 2000;

        private static readonly HashSet<long> _recentlyProcessed = new HashSet<long>();
        private static readonly Queue<long> _recentlyProcessedQueue = new Queue<long>();
        private static readonly object _lock = new object();

        private StingCostStaleMarker(AddInId addInId)
        {
            _updaterId = new UpdaterId(addInId,
                new Guid("B9D4E1A2-7C63-4F89-9E0A-1F5A2C8B3D50"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.FloorsRoofsStructuralWalls;
        public string GetUpdaterName() => "STING Cost Stale Marker";
        public string GetAdditionalInformation()
            => "Marks BOQ rows as stale when geometry, material or type changes invalidate the last cost calculation.";

        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new StingCostStaleMarker(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);
                StingLog.Info("StingCostStaleMarker registered (disabled).");
            }
            catch (Exception ex)
            {
                StingLog.Error("StingCostStaleMarker.Register", ex);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    // Reuse the multi-category filter exposed by the tag
                    // auto-tagger — same cost-bearing categories.
                    var filter = StingAutoTagger.CreateMultiCategoryFilterStatic();
                    UpdaterRegistry.AddTrigger(_updaterId, filter,
                        Element.GetChangeTypeGeometry());
                    UpdaterRegistry.AddTrigger(_updaterId, filter,
                        Element.GetChangeTypeElementAddition());
                    _enabled = true;
                    StingLog.Info("StingCostStaleMarker enabled.");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                    StingLog.Info("StingCostStaleMarker disabled.");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("StingCostStaleMarker.SetEnabled", ex);
            }
        }

        public static bool IsEnabled => _enabled;

        public static void Unregister()
        {
            try
            {
                if (_instance != null)
                    UpdaterRegistry.UnregisterUpdater(_updaterId);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostStaleMarker unregister: {ex.Message}");
            }
        }

        public void Execute(UpdaterData data)
        {
            if (data == null) return;
            try
            {
                Document doc = data.GetDocument();
                if (doc == null) return;

                var modified = data.GetModifiedElementIds();
                var added = data.GetAddedElementIds();

                if (modified != null && modified.Count > MaxElementsPerTrigger)
                {
                    StingLog.Info($"StingCostStaleMarker: bulk change ({modified.Count}) — skipped to avoid fan-out.");
                    return;
                }

                ProcessElements(doc, modified, reason: "Geometry");
                ProcessElements(doc, added, reason: "New");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostStaleMarker.Execute: {ex.Message}");
            }
        }

        private static void ProcessElements(Document doc,
            ICollection<ElementId> ids, string reason)
        {
            if (doc == null || ids == null || ids.Count == 0) return;
            foreach (var id in ids)
            {
                Element el = doc.GetElement(id);
                if (el == null) continue;

                // Skip elements that don't carry a cost line yet — only
                // mark stale if the element was previously costed (has a
                // non-zero CST_MODELED_TOTAL_UGX or a CST_RATE_SOURCE).
                if (!WasPreviouslyCosted(el)) continue;

                // Workshared-safe — only mark elements we own.
                if (!IsCheckedOutByUs(doc, el)) continue;

                // LRU dedup so fast successive changes don't double-mark.
                if (!ShouldProcess(id.Value)) continue;

                MarkStale(el, reason);
            }
        }

        private static bool WasPreviouslyCosted(Element el)
        {
            try
            {
                string src = ParameterHelpers.GetString(el, "CST_RATE_SOURCE");
                if (!string.IsNullOrEmpty(src)) return true;
                Parameter p = el.LookupParameter("CST_MODELED_TOTAL_UGX");
                if (p != null && p.HasValue && Math.Abs(p.AsDouble()) > 0.0001) return true;
            }
            catch { /* swallow — best-effort detection */ }
            return false;
        }

        private static bool IsCheckedOutByUs(Document doc, Element el)
        {
            try
            {
                if (doc == null || !doc.IsWorkshared || el == null) return true;
                var status = WorksharingUtils.GetCheckoutStatus(doc, el.Id);
                return status == CheckoutStatus.OwnedByCurrentUser
                    || status == CheckoutStatus.NotOwned;
            }
            catch { return true; }
        }

        private static void MarkStale(Element el, string reason)
        {
            try
            {
                // Boolean stale flag.
                Parameter stale = el.LookupParameter(ParamRegistry.CST_STALE_BOOL);
                if (stale != null && !stale.IsReadOnly)
                    stale.Set(1);

                // Reason text — small, indexed string so the validator
                // can group by reason for the report.
                Parameter reasonP = el.LookupParameter(ParamRegistry.CST_STALE_REASON_TXT);
                if (reasonP != null && !reasonP.IsReadOnly)
                    reasonP.Set(reason);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StingCostStaleMarker.MarkStale {el?.Id}: {ex.Message}");
            }
        }

        private static bool ShouldProcess(long elementIdValue)
        {
            lock (_lock)
            {
                if (_recentlyProcessed.Contains(elementIdValue)) return false;
                _recentlyProcessed.Add(elementIdValue);
                _recentlyProcessedQueue.Enqueue(elementIdValue);

                // LRU eviction — 20% of the cap at a time so we don't
                // thrash on the boundary.
                if (_recentlyProcessed.Count > RecentlyProcessedMax)
                {
                    int toRemove = RecentlyProcessedEvictCount;
                    while (toRemove-- > 0 && _recentlyProcessedQueue.Count > 0)
                        _recentlyProcessed.Remove(_recentlyProcessedQueue.Dequeue());
                }
                return true;
            }
        }

        /// <summary>Clear the LRU set — called by Cost_ClearStale after a successful BOQ_Build.</summary>
        public static void ResetRecentlyProcessed()
        {
            lock (_lock)
            {
                _recentlyProcessed.Clear();
                _recentlyProcessedQueue.Clear();
            }
        }
    }
}
