// LiveClashUpdater.cs — IUpdater that queues edited elements for background clash recheck.
// CRITICAL: never throws. Never starts a transaction. All real work deferred to ExternalEvent.
using System;
using System.Collections.Concurrent;
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
                // Revit 2024+: ElementId.Value replaces IntegerValue. Cast to
                // int for DirtyQueue — Revit's actual element ids fit in int
                // for all practical models; the dirty-queue int is ephemeral
                // so even a theoretical overflow is self-healing on the next
                // RefreshElement / RemoveElement cycle.
                foreach (var id in data.GetModifiedElementIds())
                    DirtyQueue.Enqueue((docGuid, (int)id.Value));
                foreach (var id in data.GetAddedElementIds())
                    DirtyQueue.Enqueue((docGuid, (int)id.Value));
                // Deleted elements: pushed with negative sentinel to trigger
                // removal from BVH.
                foreach (var id in data.GetDeletedElementIds())
                    DirtyQueue.Enqueue((docGuid, -(int)id.Value));
            }
            catch (Exception ex)
            {
                StingLog.Error("LiveClashUpdater.Execute swallowed", ex);
            }
        }

        public static void Register(UIControlledApplication uiApp, Document sampleDoc)
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
