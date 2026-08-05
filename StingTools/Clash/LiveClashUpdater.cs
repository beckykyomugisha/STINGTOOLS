// LiveClashUpdater.cs — IUpdater that queues edited elements for background clash recheck.
// CRITICAL: never throws. Never starts a transaction. All real work deferred to ExternalEvent.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class LiveClashUpdater : IUpdater
    {
        public static readonly UpdaterId UpdaterGuid = new UpdaterId(
            new AddInId(new Guid("3C9A4E2D-5F7B-4A12-9B8F-C1D2E3F4A5B6")),
            new Guid("3C9A4E2D-5F7B-4A12-9B8F-C1D2E3F4A5B7"));

        public static readonly ConcurrentQueue<(string DocGuid, int ElementId)> DirtyQueue =
            new ConcurrentQueue<(string, int)>();

        // Parallel queue consumed by GeometrySyncHandler (delta geometry push to Planscape).
        // Kept separate from DirtyQueue so clash detection and geometry sync can drain
        // independently at their own rates without racing on the same queue.
        public static readonly ConcurrentQueue<(string DocGuid, int ElementId)> GeometrySyncQueue =
            new ConcurrentQueue<(string, int)>();

        /// <summary>
        /// Drain all geometry-sync entries that belong to <paramref name="doc"/>.
        /// Items for other documents are re-enqueued so they aren't lost.
        /// Returns element IDs (positive = changed, negative = deleted sentinel).
        /// </summary>
        public static List<int> DrainGeometrySyncIds(Document doc)
        {
            if (doc == null) return new System.Collections.Generic.List<int>();
            string docGuid = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";
            var result   = new System.Collections.Generic.List<int>();
            var requeue  = new System.Collections.Generic.List<(string, int)>();
            while (GeometrySyncQueue.TryDequeue(out var item))
            {
                if (string.Equals(item.DocGuid, docGuid, StringComparison.Ordinal))
                    result.Add(item.ElementId);
                else
                    requeue.Add(item);
            }
            foreach (var r in requeue) GeometrySyncQueue.Enqueue(r);
            return result;
        }

        public UpdaterId GetUpdaterId() => UpdaterGuid;
        public string GetUpdaterName() => "STING Live Clash Updater";
        public string GetAdditionalInformation() => "Queues edited elements for clash re-check.";
        public ChangePriority GetChangePriority() => ChangePriority.MEPAccessoriesFittingsSegmentsWires;

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                string docGuid = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";
                // ElementId.IntegerValue is obsolete in Revit 2024+; use Value (Int64).
                foreach (var id in data.GetModifiedElementIds())
                {
                    DirtyQueue.Enqueue((docGuid, (int)id.Value));
                    GeometrySyncQueue.Enqueue((docGuid, (int)id.Value));
                }
                foreach (var id in data.GetAddedElementIds())
                {
                    DirtyQueue.Enqueue((docGuid, (int)id.Value));
                    GeometrySyncQueue.Enqueue((docGuid, (int)id.Value));
                }
                // Deleted elements: pushed with -1 sentinel to trigger removal from BVH.
                foreach (var id in data.GetDeletedElementIds())
                {
                    DirtyQueue.Enqueue((docGuid, -(int)id.Value));
                    GeometrySyncQueue.Enqueue((docGuid, -(int)id.Value));
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("LiveClashUpdater.Execute swallowed", ex);
            }
        }

        public static void Register(UIControlledApplication uiApp)
        {
            try
            {
                var updater = new LiveClashUpdater();
                UpdaterRegistry.RegisterUpdater(updater);
                var filter = new LogicalOrFilter(new System.Collections.Generic.List<ElementFilter>
                {
                    new ElementCategoryFilter(BuiltInCategory.OST_DuctCurves),
                    new ElementCategoryFilter(BuiltInCategory.OST_PipeCurves),
                    new ElementCategoryFilter(BuiltInCategory.OST_CableTray),
                    new ElementCategoryFilter(BuiltInCategory.OST_Conduit),
                    new ElementCategoryFilter(BuiltInCategory.OST_Walls),
                    new ElementCategoryFilter(BuiltInCategory.OST_Floors),
                    new ElementCategoryFilter(BuiltInCategory.OST_Ceilings),
                    new ElementCategoryFilter(BuiltInCategory.OST_StructuralFraming),
                    new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns),
                });
                UpdaterRegistry.AddTrigger(UpdaterGuid, filter,
                    Element.GetChangeTypeGeometry());
                UpdaterRegistry.AddTrigger(UpdaterGuid, filter,
                    Element.GetChangeTypeElementAddition());
                UpdaterRegistry.AddTrigger(UpdaterGuid, filter,
                    Element.GetChangeTypeElementDeletion());
            }
            catch (Exception ex)
            {
                StingLog.Error("LiveClashUpdater.Register failed", ex);
            }
        }
    }
}
