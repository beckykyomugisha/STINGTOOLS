// GeometrySyncUpdater.cs — the IUpdater that feeds Planscape geometry sync.
//
// CRITICAL: never throws. Never starts a transaction. All real work is deferred
// to GeometrySyncHandler via an ExternalEvent on document save.
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    /// <summary>
    /// C2 — geometry sync gets its own updater, over every model category,
    /// independent of clash detection.
    ///
    /// <para><b>What was wrong.</b> <see cref="LiveClashUpdater"/> was the sole
    /// producer for <see cref="LiveClashUpdater.GeometrySyncQueue"/>, and its
    /// trigger covers the nine categories CLASH cares about: ducts, pipes, cable
    /// tray, conduit, walls, floors, ceilings, structural framing and columns.
    /// Doors, windows, mechanical equipment, plumbing and lighting fixtures,
    /// furniture, specialty equipment, generic models, stairs, railings, roofs
    /// and curtain panels therefore never reached the server automatically — a
    /// coordinator could move every door in the building and the federated model
    /// would not change. Worse, the clash trigger is opt-out
    /// (<c>LIVE_CLASH_TRIGGERS_ENABLED=false</c>), so a project that turned off
    /// clash triggers silently turned off ALL geometry sync while tag sync kept
    /// flowing — the model looked connected and was not.</para>
    ///
    /// <para><b>Why a separate updater rather than a wider clash filter.</b>
    /// Widening the clash trigger would drag every door and chair into clash
    /// re-checks, which is a different feature with different performance
    /// characteristics and a different opt-out. The two consumers want different
    /// scopes, so they get their own triggers.</para>
    ///
    /// <para><b>Scope.</b> All non-type, non-view-specific elements —
    /// "everything in the model that has a physical presence". Enqueueing is
    /// O(1) per changed element and the expensive part (tessellation and
    /// upload) happens once per document save, so the broad trigger costs a
    /// queue push per edit rather than per-edit geometry work.</para>
    /// </summary>
    public sealed class GeometrySyncUpdater : IUpdater
    {
        // Distinct from LiveClashUpdater's id — two updaters, two triggers, two
        // lifecycles. Reusing the id would make one registration silently
        // replace the other.
        public static readonly UpdaterId UpdaterGuid = new UpdaterId(
            new AddInId(new Guid("3C9A4E2D-5F7B-4A12-9B8F-C1D2E3F4A5B6")),
            new Guid("7E1B5C34-9A26-4D8F-B0E7-2F5A6C8D9E01"));

        public UpdaterId GetUpdaterId() => UpdaterGuid;
        public string GetUpdaterName() => "STING Geometry Sync Updater";
        public string GetAdditionalInformation() => "Queues changed model elements for Planscape geometry sync.";
        public ChangePriority GetChangePriority() => ChangePriority.MEPAccessoriesFittingsSegmentsWires;

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                string docGuid = doc.ProjectInformation?.UniqueId ?? doc.PathName ?? "host";

                // id.Value is a 64-bit ElementId (Revit 2024+). Enqueue the full
                // long: the old (int) cast wrapped a large id negative, and the
                // handler reads a negative id as a delete tombstone — so a large
                // modified/added element was mis-split as a deletion and the
                // server soft-deleted live geometry the user had just edited.
                foreach (var id in data.GetModifiedElementIds())
                    LiveClashUpdater.GeometrySyncQueue.Enqueue((docGuid, id.Value));
                foreach (var id in data.GetAddedElementIds())
                    LiveClashUpdater.GeometrySyncQueue.Enqueue((docGuid, id.Value));
                // Negative id is the tombstone sentinel the handler splits on.
                foreach (var id in data.GetDeletedElementIds())
                    LiveClashUpdater.GeometrySyncQueue.Enqueue((docGuid, -id.Value));
            }
            catch (Exception ex)
            {
                // An updater that throws is disabled by Revit for the session,
                // which would silently stop all geometry sync.
                StingLog.Error("GeometrySyncUpdater.Execute swallowed", ex);
            }
        }

        /// <summary>
        /// Register the updater and attach triggers. Deliberately NOT gated on
        /// <c>LIVE_CLASH_TRIGGERS_ENABLED</c>: that flag turns off clash
        /// detection, and turning off clash detection must not silently stop the
        /// federated model from updating.
        /// </summary>
        public static void Register(UIControlledApplication uiApp)
        {
            try
            {
                UpdaterRegistry.RegisterUpdater(new GeometrySyncUpdater());

                // Every model element instance: not an ElementType, and not
                // owned by a view (which excludes annotation, tags, dimensions
                // and other view-specific graphics that carry no model
                // geometry). Expressed as filters rather than a category list so
                // it cannot drift out of date as categories are added — a list
                // is exactly how the clash trigger ended up missing doors.
                var modelInstances = new LogicalAndFilter(
                    new ElementIsElementTypeFilter(true),                 // inverted: instances only
                    new ElementOwnerViewFilter(ElementId.InvalidElementId));

                UpdaterRegistry.AddTrigger(UpdaterGuid, modelInstances, Element.GetChangeTypeGeometry());
                UpdaterRegistry.AddTrigger(UpdaterGuid, modelInstances, Element.GetChangeTypeElementAddition());
                UpdaterRegistry.AddTrigger(UpdaterGuid, modelInstances, Element.GetChangeTypeElementDeletion());

                StingLog.Info("GeometrySyncUpdater registered over all model categories.");
            }
            catch (Exception ex)
            {
                StingLog.Error("GeometrySyncUpdater.Register failed", ex);
            }
        }
    }
}
