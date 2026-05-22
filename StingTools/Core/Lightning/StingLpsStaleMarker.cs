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

        private static bool LooksLikeLps(Element el)
        {
            if (el == null) return false;
            try
            {
                // Tier 1 — explicit param tags set by Mark / Sync / Inventory
                string elementType = StingTools.Core.ParameterHelpers.GetString(el, LpsParams.ELEMENT_TYPE_TXT);
                if (!string.IsNullOrWhiteSpace(elementType)) return true;
                string compStatus = StingTools.Core.ParameterHelpers.GetString(el, LpsParams.COMPLIANCE_STATUS_TXT);
                if (!string.IsNullOrWhiteSpace(compStatus)) return true;

                // Tier 2 — any of the 6 LPS *_TAG_TXT containers populated
                foreach (var p in new[] {
                    LpsParams.AIRTERM_TAG_TXT, LpsParams.DOWNCOND_TAG_TXT,
                    LpsParams.EARTH_TAG_TXT,   LpsParams.BOND_TAG_TXT,
                    LpsParams.SPD_TAG_TXT,     LpsParams.TESTCLAMP_TAG_TXT })
                {
                    if (!string.IsNullOrWhiteSpace(StingTools.Core.ParameterHelpers.GetString(el, p)))
                        return true;
                }

                // Tier 3 — family / type name pattern (catches freshly-placed
                // families before any STING command has touched them)
                if (el is FamilyInstance fi)
                {
                    string fam = fi.Symbol?.FamilyName ?? "";
                    string sym = fi.Symbol?.Name ?? "";
                    string upper = ($"{fam} {sym}").ToUpperInvariant();
                    if (upper.Contains("LPS") || upper.Contains("LIGHTNING") ||
                        upper.Contains("AIR TERMINAL") || upper.Contains("FRANKLIN") ||
                        upper.Contains("DOWN CONDUCTOR") || upper.Contains("DOWNCOND") ||
                        upper.Contains("EARTH ROD") || upper.Contains("EARTH ELECTRODE") ||
                        upper.Contains("TEST CLAMP") || upper.Contains("EQUIPOTENTIAL") ||
                        upper.Contains("SPD") || upper.Contains("SURGE"))
                        return true;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"LooksLikeLps: {ex.Message}"); }
            return false;
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

                    // Wave A #2 — Three-way LPS-classification check.
                    // Previous version required ELEMENT_TYPE_TXT to be
                    // non-empty, but nothing writes that param until
                    // LpsMarkElementTypesCommand has been run — so the
                    // marker silently never fired on first-time projects.
                    // Now also accept (a) family-name pattern matching
                    // any LPS keyword (works on freshly-placed families)
                    // and (b) any of the LPS sub-tag containers being
                    // populated.
                    if (!LooksLikeLps(el))
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
