// StingLpsStaleMarker — IUpdater that flags LPS elements when their
// geometry or LPS-relevant parameters change, so the STING LPS panel
// (or a downstream validator) sees stale rows without a manual
// "Refresh from model" press. Mirrors StingStaleMarker / StingCostStaleMarker.
//
// Triggers on:
//   • Geometry changes on Electrical Equipment + Generic Models +
//     Conduits + Cable Trays (LPS candidate categories)
//
// What it does:
//   • Sets ELC_LPS_COMPLIANCE_STATUS_TXT = "STALE" on the changed
//     element so the panel's loader + compliance check surface it.
//   • Notifies the singleton StingLpsPanel.Instance via PushRunRow so
//     the RPRT tab logs the event.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core.Fabrication;

namespace StingTools.Core.Lightning
{
    public class StingLpsStaleMarker : IUpdater
    {
        private static StingLpsStaleMarker _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled = false;

        private StingLpsStaleMarker(AddInId addInId)
        {
            _updaterId = new UpdaterId(addInId, new Guid("C2E3F4A5-B6C7-4D8E-9F01-23456789ABCD"));
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.MEPSystems;
        public string GetUpdaterName() => "STING LPS Stale Marker";
        public string GetAdditionalInformation()
            => "Marks LPS-related elements (air terminals, down conductors, earth electrodes, bonding, SPDs) as stale when their geometry changes.";

        public static void Register(UIControlledApplication app)
        {
            if (_instance != null) return;
            try
            {
                _instance = new StingLpsStaleMarker(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);
                StingTools.Core.StingLog.Info("StingLpsStaleMarker registered (disabled).");
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("StingLpsStaleMarker.Register", ex);
            }
        }

        public static bool IsEnabled => _enabled;

        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    var filter = BuildLpsFilter();
                    if (filter != null)
                        UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeGeometry());
                    _enabled = true;
                    StingTools.Core.StingLog.Info("StingLpsStaleMarker enabled.");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                    StingTools.Core.StingLog.Info("StingLpsStaleMarker disabled.");
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("StingLpsStaleMarker.SetEnabled", ex);
            }
        }

        private static ElementFilter BuildLpsFilter()
        {
            var cats = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_ElectricalEquipment),
                new ElementCategoryFilter(BuiltInCategory.OST_GenericModel),
                new ElementCategoryFilter(BuiltInCategory.OST_Conduit),
                new ElementCategoryFilter(BuiltInCategory.OST_CableTray)
            };
            return new LogicalOrFilter(cats);
        }

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                if (doc == null) return;
                var changed = data.GetModifiedElementIds();
                if (changed == null || changed.Count == 0) return;

                int touched = 0;
                foreach (var id in changed)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;

                    // Only mark elements that look LPS-classified
                    // (carry ELEMENT_TYPE_TXT or have a non-empty
                    // compliance status). Skips non-LPS electrical
                    // equipment so e.g. panel boards don't get
                    // spammed with STALE flags.
                    string elementType = StingTools.Core.ParameterHelpers.GetString(el, LpsParams.ELEMENT_TYPE_TXT);
                    string compStatus  = StingTools.Core.ParameterHelpers.GetString(el, LpsParams.COMPLIANCE_STATUS_TXT);
                    if (string.IsNullOrWhiteSpace(elementType) && string.IsNullOrWhiteSpace(compStatus))
                        continue;

                    // We're in an IUpdater so a Transaction is already
                    // open — write directly via ParameterHelpers.SetString.
                    StingTools.Core.ParameterHelpers.SetString(el, LpsParams.COMPLIANCE_STATUS_TXT, "STALE", true);
                    touched++;
                }

                if (touched > 0)
                {
                    try
                    {
                        StingTools.UI.StingLpsPanel.Instance?.PushRunRow(
                            $"LPS stale: {touched} element(s)", "⚠");
                    }
                    catch (Exception ex) { StingTools.Core.StingLog.Warn($"PushRunRow: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Error("StingLpsStaleMarker.Execute", ex);
            }
        }
    }
}
