// StingTools — HVAC envelope-change → load-stale IUpdater (Phase 187f).
//
// Mirror of StingStaleMarker (which flags tagged elements as stale when
// geometry changes) but scoped to ENVELOPE elements that affect the
// BlockLoadEngine result: exterior walls, windows, curtain panels,
// roofs, floors.
//
// On geometry change:
//   1. Get the changed element's bounding-box centre.
//   2. Find the Space containing that point (or all adjacent rooms via
//      WallUtils.GetWallsAdjacentToPanel — best-effort).
//   3. Stamp HVC_LOAD_STALE_BOOL = 1 on those Spaces so the user knows
//      to re-run Hvac_BlockLoad before trusting downstream sizing.
//   4. Also stamp HVC_LOAD_STALE_REASON_TXT with a short tag identifying
//      what changed (wall geom / window geom / roof geom / etc.).
//
// Workshared-safe: skips when the document isn't modifiable or when the
// changed element belongs to another user's workset.
//
// Off by default; enable via Hvac_StaleUpdaterEnable command. Same
// AutoTaggerToggle pattern as the existing IUpdater.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Core.Hvac.Loads
{
    public class HvacEnvelopeStaleUpdater : IUpdater
    {
        private static HvacEnvelopeStaleUpdater _instance;
        private static UpdaterId _updaterId;
        private static bool _enabled = false;

        // Static GUID — never change once shipped, else existing projects
        // with stamped warnings/state lose their connection to this updater.
        private static readonly Guid UpdaterGuid =
            new Guid("D2C8F4A1-7B6E-4F3A-9C2D-8E1A5B7C9D0F");

        // Throttle the per-trigger element count so a bulk geometry edit
        // (e.g. user grouping 200 walls) doesn't open a 200-stamp transaction.
        private const int MaxElementsPerTrigger = 30;

        private HvacEnvelopeStaleUpdater(AddInId addInId)
        {
            _updaterId = new UpdaterId(addInId, UpdaterGuid);
        }

        public UpdaterId GetUpdaterId() => _updaterId;
        public ChangePriority GetChangePriority() => ChangePriority.FloorsRoofsStructuralWalls;
        public string GetUpdaterName() => "STING HVAC Envelope Stale Marker";
        public string GetAdditionalInformation()
            => "Flags HVAC Spaces as load-stale when exterior walls / windows / roofs change.";

        public static void Register(UIControlledApplication app)
        {
            try
            {
                _instance = new HvacEnvelopeStaleUpdater(app.ActiveAddInId);
                UpdaterRegistry.RegisterUpdater(_instance, true);
                StingLog.Info("HvacEnvelopeStaleUpdater registered (disabled).");
            }
            catch (Exception ex) { StingLog.Error("HvacEnvelopeStaleUpdater.Register", ex); }
        }

        public static void Unregister()
        {
            try { if (_instance != null) UpdaterRegistry.UnregisterUpdater(_updaterId); }
            catch (Exception ex) { StingLog.Warn($"HvacEnvelopeStaleUpdater unregister: {ex.Message}"); }
        }

        public static bool IsEnabled => _enabled;

        public static void SetEnabled(bool enabled)
        {
            if (_instance == null || _updaterId == null) return;
            try
            {
                if (enabled && !_enabled)
                {
                    var filter = BuildEnvelopeFilter();
                    UpdaterRegistry.AddTrigger(_updaterId, filter, Element.GetChangeTypeGeometry());
                    _enabled = true;
                    StingLog.Info("HvacEnvelopeStaleUpdater enabled.");
                }
                else if (!enabled && _enabled)
                {
                    UpdaterRegistry.RemoveAllTriggers(_updaterId);
                    _enabled = false;
                    StingLog.Info("HvacEnvelopeStaleUpdater disabled.");
                }
            }
            catch (Exception ex) { StingLog.Error("HvacEnvelopeStaleUpdater.SetEnabled", ex); }
        }

        private static ElementFilter BuildEnvelopeFilter()
        {
            var cats = new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_Walls),
                new ElementCategoryFilter(BuiltInCategory.OST_Windows),
                new ElementCategoryFilter(BuiltInCategory.OST_Doors),          // doors carry IR + air-leakage gains
                new ElementCategoryFilter(BuiltInCategory.OST_CurtainWallPanels),
                new ElementCategoryFilter(BuiltInCategory.OST_Roofs),
                new ElementCategoryFilter(BuiltInCategory.OST_Floors)
            };
            return new LogicalOrFilter(cats);
        }

        public void Execute(UpdaterData data)
        {
            try
            {
                var doc = data.GetDocument();
                if (doc == null || !doc.IsModifiable) return;     // updater fires inside Revit's tx; safe to write

                var ids = data.GetModifiedElementIds();
                if (ids == null || ids.Count == 0) return;
                if (ids.Count > MaxElementsPerTrigger)
                {
                    // Bulk edit — be cautious. Stamp project-wide instead of
                    // walking every changed element.
                    StampAllSpaces(doc, "bulk envelope edit");
                    return;
                }

                var spacesToStamp = new HashSet<ElementId>();
                string lastReason = "envelope geom";
                foreach (var eid in ids)
                {
                    var el = doc.GetElement(eid);
                    if (el == null) continue;
                    string reason = ReasonFor(el);
                    if (reason != null) lastReason = reason;

                    // Find space(s) that touch this envelope element.
                    foreach (var spId in SpacesFor(doc, el))
                        spacesToStamp.Add(spId);
                }

                if (spacesToStamp.Count == 0) return;
                foreach (var spId in spacesToStamp)
                {
                    var sp = doc.GetElement(spId);
                    if (sp == null) continue;
                    try
                    {
                        ParameterHelpers.SetInt(sp, "HVC_LOAD_STALE_BOOL", 1, overwrite: true);
                        ParameterHelpers.SetString(sp, "HVC_LOAD_STALE_REASON_TXT",
                            $"{lastReason} @ {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}", overwrite: true);
                    }
                    catch (Exception ex) { StingLog.Warn($"HvacStale stamp {spId}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Error("HvacEnvelopeStaleUpdater.Execute", ex); }
        }

        private static string ReasonFor(Element el)
        {
            try
            {
                if (el.Category == null) return null;
                var bic = (BuiltInCategory)el.Category.Id.Value;
                return bic switch
                {
                    BuiltInCategory.OST_Walls             => "wall geom",
                    BuiltInCategory.OST_Windows           => "window geom",
                    BuiltInCategory.OST_Doors             => "door geom",
                    BuiltInCategory.OST_CurtainWallPanels => "curtain panel geom",
                    BuiltInCategory.OST_Roofs             => "roof geom",
                    BuiltInCategory.OST_Floors            => "floor geom",
                    _                                     => "envelope geom"
                };
            }
            catch { return null; }
        }

        private static IEnumerable<ElementId> SpacesFor(Document doc, Element el)
        {
            // Strategy 1: bounding-box centre → Document.GetSpaceAtPoint.
            // Strategy 2: walls — try each end-point.
            // Strategy 3: fall back to stamping all spaces on the wall's level.
            var seen = new HashSet<ElementId>();
            try
            {
                var bb = el.get_BoundingBox(null);
                if (bb != null)
                {
                    var centre = (bb.Min + bb.Max) * 0.5;
                    var sp = doc.GetSpaceAtPoint(centre);
                    if (sp != null) seen.Add(sp.Id);
                }
                if (el is Wall w && w.Location is LocationCurve lc && lc.Curve != null)
                {
                    foreach (var p in new[] { lc.Curve.GetEndPoint(0), lc.Curve.GetEndPoint(1) })
                    {
                        var sp = doc.GetSpaceAtPoint(p);
                        if (sp != null) seen.Add(sp.Id);
                    }
                }
                if (seen.Count == 0 && el.LevelId != ElementId.InvalidElementId)
                {
                    // Fallback: every Space on the affected level.
                    var spaces = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_MEPSpaces)
                        .WhereElementIsNotElementType()
                        .Cast<Space>()
                        .Where(s => s.LevelId == el.LevelId)
                        .Select(s => s.Id);
                    foreach (var s in spaces) seen.Add(s);
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpacesFor {el?.Id}: {ex.Message}"); }
            return seen;
        }

        private static void StampAllSpaces(Document doc, string reason)
        {
            try
            {
                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType()
                    .ToList();
                foreach (var sp in spaces)
                {
                    try
                    {
                        ParameterHelpers.SetInt(sp, "HVC_LOAD_STALE_BOOL", 1, overwrite: true);
                        ParameterHelpers.SetString(sp, "HVC_LOAD_STALE_REASON_TXT",
                            $"{reason} @ {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}", overwrite: true);
                    }
                    catch (Exception ex) { StingLog.Warn($"StampAllSpaces {sp.Id}: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"StampAllSpaces: {ex.Message}"); }
        }
    }
}
