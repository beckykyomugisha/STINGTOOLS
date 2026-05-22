using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// N+3 — Lightweight hook called by <see cref="StingStaleMarker"/>
    /// when a material change is detected on a tagged element.
    ///
    /// Today it just emits an audit log entry so the change is
    /// traceable (via the existing Planscape audit log infrastructure
    /// the UI layer wraps in MaterialAuditLogger). Future commits can
    /// extend this hook to:
    ///   • bump a revision number on issued sheets carrying the
    ///     affected element
    ///   • enqueue a re-tag job on the StingIdlingScheduler
    ///   • push an entry into the BIM Coordination Center coord log
    ///
    /// Kept in StingTools.Core (not StingTools.UI) so the IUpdater
    /// remains free of UI-layer dependencies.
    /// </summary>
    public static class StingMaterialUpdaterStaleHook
    {
        // Shared revision-cloud job — long-lived so a batch of material
        // changes accumulates into the same Idling queue.
        private static MaterialRevisionCloudJob _revCloudJob;
        private static readonly object _revCloudLock = new object();

        public static void OnMaterialChanged(Document doc, ElementId elementId)
        {
            if (doc == null || elementId == null || elementId.Value <= 0) return;
            try
            {
                string elName = doc.GetElement(elementId)?.Name ?? "(unknown)";
                StingLog.Info($"MaterialChange: element {elementId.Value} '{elName}' material changed → tag + BOQ marked stale.");

                // Invalidate the material caches so the next BOQ build /
                // rate lookup picks up the change. Cheap to rebuild.
                try { StingTools.UI.MaterialNameCache.Invalidate(doc); StingTools.UI.MaterialUsageIndex.Invalidate(doc); }
                catch (Exception cEx) { StingLog.Warn($"OnMaterialChanged cache invalidate: {cEx.Message}"); }

                // D7 — Also enqueue a re-tag job so PROD codes that were
                // material-aware (N+2) refresh after the material swap.
                // The job runs OUTSIDE the IUpdater's transaction (Idling
                // tick) using the existing StingAutoTagger retag path.
                EnqueueForRetag(elementId.Value);

                // D4 — Enqueue the element on the revision-cloud Idling job.
                // The job runs OUTSIDE the IUpdater's transaction (Revit
                // doesn't allow nested transactions; clouds need their own).
                // The job auto-exits when no revisions exist on the
                // project — so dev / pre-issue projects pay no cost.
                EnqueueForRevisionCloud(elementId.Value);
            }
            catch (Exception ex) { StingLog.Warn($"OnMaterialChanged: {ex.Message}"); }
        }

        private static MaterialRetagJob _retagJob;
        private static readonly object _retagLock = new object();

        private static void EnqueueForRetag(long elementId)
        {
            try
            {
                lock (_retagLock)
                {
                    if (_retagJob == null)
                    {
                        _retagJob = new MaterialRetagJob();
                        StingIdlingScheduler.Enqueue(_retagJob);
                    }
                    _retagJob.Enqueue(elementId);
                }
            }
            catch (Exception ex) { StingLog.Warn($"EnqueueForRetag: {ex.Message}"); }
        }

        private static void EnqueueForRevisionCloud(long elementId)
        {
            try
            {
                lock (_revCloudLock)
                {
                    if (_revCloudJob == null)
                    {
                        _revCloudJob = new MaterialRevisionCloudJob();
                        StingIdlingScheduler.Enqueue(_revCloudJob);
                    }
                    _revCloudJob.Enqueue(elementId);
                }
            }
            catch (Exception ex) { StingLog.Warn($"EnqueueForRevisionCloud: {ex.Message}"); }
        }
    }
}
