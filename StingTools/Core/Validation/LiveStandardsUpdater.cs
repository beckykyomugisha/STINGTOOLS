// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/Core/Validation/LiveStandardsUpdater.cs — S4.10 (N-G3).
//
// IUpdater that runs a subset of the Validation engines (S4.2 .. S4.8)
// on newly-added or modified elements. Warnings are pushed into
// WarningsManager within ~200 ms of the user placing the element so
// feedback is immediate rather than batched at day-end.
//
// Only runs the cheap validators (connectivity, separation). Heavy
// ones (fill calc, slope gradient) stay batch-only via
// Validation_RunAll.
//
// Toggle via LiveStandardsUpdater.Enable / Disable. Disabled by
// default on DocumentOpened so users must opt in — avoids surprising
// fresh sessions with balloon spam on legacy models.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Validation
{
    public class LiveStandardsUpdater : IUpdater
    {
        private static LiveStandardsUpdater _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled;
        private readonly AddInId _addInId;

        public LiveStandardsUpdater(AddInId addInId)
        {
            _addInId = addInId;
            _updaterId = new UpdaterId(addInId, new Guid("e8a5f9b2-4d73-4d2f-a7c1-94e0f0b23eb7"));
        }

        /// <summary>Register the updater on plug-in startup. Disabled until Enable().</summary>
        public static void Register(UIControlledApplication uiApp)
        {
            if (_instance != null) return;
            try
            {
                _instance = new LiveStandardsUpdater(uiApp.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);

                // Trigger on geometry addition or modification for MEP
                // curves + fittings + equipment. Conservative set — the
                // separation validator only reads geometry so these are
                // the categories that can change a validation outcome.
                var cats = new List<ElementId>
                {
                    new ElementId(BuiltInCategory.OST_DuctCurves),
                    new ElementId(BuiltInCategory.OST_PipeCurves),
                    new ElementId(BuiltInCategory.OST_Conduit),
                    new ElementId(BuiltInCategory.OST_CableTray),
                    new ElementId(BuiltInCategory.OST_DuctFitting),
                    new ElementId(BuiltInCategory.OST_PipeFitting),
                    new ElementId(BuiltInCategory.OST_ElectricalEquipment),
                    new ElementId(BuiltInCategory.OST_MechanicalEquipment),
                };
                var filter = new ElementMulticategoryFilter(
                    cats.Select(id => (BuiltInCategory)id.Value).ToList());

                UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeElementAddition());
                UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeGeometry());
                UpdaterRegistry.DisableUpdater(_updaterId);

                StingTools.Core.StingLog.Info("LiveStandardsUpdater registered (disabled)");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("LiveStandardsUpdater.Register failed", ex);
            }
        }

        public static void Unregister()
        {
            if (_instance == null) return;
            try
            {
                UpdaterRegistry.UnregisterUpdater(_updaterId);
                _instance = null;
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("LiveStandardsUpdater.Unregister failed", ex);
            }
        }

        public static void Enable()
        {
            if (_instance == null || _enabled) return;
            try { UpdaterRegistry.EnableUpdater(_updaterId); _enabled = true; }
            catch (Exception ex) { StingTools.Core.StingLog.Error("Enable failed", ex); }
        }

        public static void Disable()
        {
            if (_instance == null || !_enabled) return;
            try { UpdaterRegistry.DisableUpdater(_updaterId); _enabled = false; }
            catch (Exception ex) { StingTools.Core.StingLog.Error("Disable failed", ex); }
        }

        public static bool IsEnabled => _enabled;

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                if (doc == null) return;
                var ids = new List<ElementId>(data.GetAddedElementIds());
                ids.AddRange(data.GetModifiedElementIds());
                if (ids.Count == 0) return;

                int reported = 0;
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    var results = SeparationValidator.ValidateElement(doc, el);
                    foreach (var r in results)
                    {
                        // Push into WarningsManager via its internal
                        // static API (same assembly — OK). Falls back
                        // to StingLog if the manager call fails.
                        try
                        {
                            StingTools.Core.WarningsEngine.LogCoordinationAction(
                                doc, "LIVE_STANDARDS", "Validation", r.ToString(), "MEDIUM");
                        }
                        catch (Exception logEx)
                        {
                            StingTools.Core.StingLog.Warn(
                                $"Live standards log failed: {logEx.Message}; message: {r}");
                        }
                        reported++;
                        if (reported >= 20) return; // throttle per-trigger
                    }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("LiveStandardsUpdater.Execute failed", ex);
            }
        }

        public string GetAdditionalInformation() => "STING v6 live standards IUpdater (S4.10 / N-G3)";
        public ChangePriority GetChangePriority() => ChangePriority.Annotations;
        public UpdaterId GetUpdaterId() => _updaterId;
        public string GetUpdaterName() => "STING LiveStandardsUpdater";
    }
}
