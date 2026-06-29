// StingTools — Sustainability staleness IUpdater (WS I13).
//
// Mirrors HvacEnvelopeStaleUpdater but for the sustainability result: when an
// element that feeds the dashboard changes (envelope, plumbing fixtures), the
// cached run is invalidated and a "stale" flag is raised so the dashboard signals
// it's out of date. Unlike the HVAC updater it stamps nothing on elements — it
// just drops the cache (SustainabilityEngine.Invalidate) and flips a flag the
// panel reads, so it's cheap.
//
// Registered disabled at startup; the dashboard enables it after the first run
// (and marks the result fresh). Off until then, like the HVAC updater.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Sustainability
{
    public class SustainStaleUpdater : IUpdater
    {
        private static SustainStaleUpdater _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled = false;

        // Static GUID — never change once shipped.
        private static readonly Guid UpdaterGuid =
            new Guid("3F9A1C7D-2E4B-4A6C-8D1F-5B0E9C2A7D34");

        /// <summary>Delegates to the pure SustainStaleState (unit-tested).</summary>
        public static bool IsStale => SustainStaleState.IsStale;
        public static string StaleReason => SustainStaleState.Reason;

        private SustainStaleUpdater(AddInId addInId) { _updaterId = new UpdaterId(addInId, UpdaterGuid); }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.FloorsRoofsStructuralWalls;
        public string GetUpdaterName() => "STING Sustainability Stale Marker";
        public string GetAdditionalInformation()
            => "Marks the sustainability result stale when envelope / fixtures change.";

        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new SustainStaleUpdater(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);
                StingLog.Info("SustainStaleUpdater registered (disabled).");
            }
            catch (Exception ex) { StingLog.Error("SustainStaleUpdater.Register", ex); }
        }

        public static void Unregister()
        {
            try { if (_instance != null) UpdaterRegistry.UnregisterUpdater(_updaterId); }
            catch (Exception ex) { StingLog.Warn($"SustainStaleUpdater unregister: {ex.Message}"); }
        }

        public static bool IsEnabled => _enabled;

        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    var filter = BuildFilter();
                    UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeGeometry());
                    UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeElementAddition());
                    UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeElementDeletion());
                    _enabled = true;
                    StingLog.Info("SustainStaleUpdater enabled.");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                }
            }
            catch (Exception ex) { StingLog.Error("SustainStaleUpdater.SetEnabled", ex); }
        }

        /// <summary>Called by the dashboard after a fresh run.</summary>
        public static void MarkFresh()
        {
            SustainStaleState.MarkFresh();
            try { SetEnabled(true); } catch { }   // start watching once there's a result to invalidate
        }

        private static ElementFilter BuildFilter()
        {
            var cats = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_Walls),
                new ElementCategoryFilter(BuiltInCategory.OST_Windows),
                new ElementCategoryFilter(BuiltInCategory.OST_Doors),
                new ElementCategoryFilter(BuiltInCategory.OST_Roofs),
                new ElementCategoryFilter(BuiltInCategory.OST_Floors),
                new ElementCategoryFilter(BuiltInCategory.OST_CurtainWallPanels),
                new ElementCategoryFilter(BuiltInCategory.OST_PlumbingFixtures),
            };
            return new LogicalOrFilter(cats);
        }

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data?.GetDocument();
                if (doc == null) return;
                SustainStaleState.MarkStale($"model changed @ {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
                try { SustainabilityEngine.Invalidate(doc); } catch { }
                try { StingTools.UI.Sustainability.StingSustainabilityPanel.Instance?
                        .UpdateStatus("Sustainability: stale — model changed, re-run the dashboard"); }
                catch { }
            }
            catch (Exception ex) { StingLog.Error("SustainStaleUpdater.Execute", ex); }
        }
    }
}
