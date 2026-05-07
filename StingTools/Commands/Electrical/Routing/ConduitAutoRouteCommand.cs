using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Electrical;

namespace StingTools.Commands.Electrical.Routing
{
    /// <summary>
    /// Best-effort rectilinear conduit auto-routing. Walks the cable
    /// manifest's un-routed cables and creates Conduit elements along an
    /// L/Z path between each circuit's load and panel locations. The MEP
    /// Routing API isn't required (it isn't enabled on every project);
    /// production hardening could swap in proper clash avoidance — Phase
    /// 179 honestly delivers the simple Manhattan path.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConduitAutoRouteCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            CableManifest manifest;
            try { manifest = CableManifest.Load(doc); }
            catch (Exception ex) { StingLog.Warn($"CableManifest.Load: {ex.Message}"); manifest = null; }
            if (manifest == null || manifest.Cables == null || manifest.Cables.Count == 0)
            {
                TaskDialog.Show("STING Auto-Route",
                    "No cable manifest found. Run Cable Sizer / Add Cable first to populate the manifest.");
                return Result.Cancelled;
            }
            var unrouted = manifest.Cables
                .Where(c => c.RouteTrayIds == null || c.RouteTrayIds.Count == 0)
                .ToList();
            if (unrouted.Count == 0)
            {
                TaskDialog.Show("STING Auto-Route", "All cables in the manifest already have routes assigned.");
                return Result.Succeeded;
            }

            var conduitType = new FilteredElementCollector(doc)
                .OfClass(typeof(ConduitType)).Cast<ConduitType>().FirstOrDefault();
            if (conduitType == null)
            {
                TaskDialog.Show("STING Auto-Route",
                    "No conduit type found in the project. Load a conduit family first.");
                return Result.Failed;
            }

            int routed = 0;
            using (var tx = new Transaction(doc, "STING Auto-Route Conduit"))
            {
                tx.Start();
                foreach (var cable in unrouted)
                {
                    try
                    {
                        ElectricalSystem sys = null;
                        try
                        {
                            sys = new FilteredElementCollector(doc)
                                .OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                                .FirstOrDefault(s => string.Equals(
                                    s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "",
                                    cable.CircuitId, StringComparison.OrdinalIgnoreCase));
                        }
                        catch { }
                        if (sys == null) continue;

                        Element loadEl = null;
                        Element panelEl = null;
                        try { loadEl = sys.Elements?.Cast<Element>().FirstOrDefault(); } catch { }
                        try { panelEl = sys.BaseEquipment; } catch { }
                        if (loadEl == null || panelEl == null) continue;

                        var startPt = (loadEl.Location as LocationPoint)?.Point;
                        var endPt   = (panelEl.Location as LocationPoint)?.Point;
                        if (startPt == null || endPt == null) continue;

                        var levelId = loadEl.LevelId;
                        if (levelId == null || levelId == ElementId.InvalidElementId)
                            levelId = doc.ActiveView?.GenLevel?.Id ?? ElementId.InvalidElementId;
                        if (levelId == null || levelId == ElementId.InvalidElementId) continue;

                        double diamMm = ConduitRouteEngine.SelectConduitDiameterMm(
                            new List<StingCable> { cable });
                        var segments = ConduitRouteEngine.ComputeRoute(startPt, endPt, diamMm, cable.CircuitId);
                        var routeIds = new List<long>();
                        foreach (var seg in segments)
                        {
                            if (seg.Start.DistanceTo(seg.End) < 0.01) continue;
                            try
                            {
                                // TODO-VERIFY-API: Conduit.Create(Document, ElementId conduitTypeId,
                                //   XYZ start, XYZ end, ElementId levelId) — signature differs across
                                //   Revit versions. The 5-arg form is the Revit 2024+ canonical.
                                var conduit = Conduit.Create(doc, conduitType.Id,
                                    seg.Start, seg.End, levelId);
                                if (conduit != null)
                                {
                                    try { ParameterHelpers.SetString(conduit, ParamRegistry.ELC_CONDUIT_ROUTE,
                                        $"AUTO:{cable.CircuitId}", overwrite: true); }
                                    catch { }
                                    routeIds.Add(conduit.Id.Value);
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"Conduit.Create: {ex.Message}"); }
                        }
                        if (routeIds.Count > 0)
                        {
                            cable.RouteTrayIds = routeIds;
                            routed++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"AutoRoute cable {cable.CircuitId}: {ex.Message}"); }
                }
                tx.Commit();
            }
            try { manifest.Save(doc); } catch (Exception ex) { StingLog.Warn($"Manifest save: {ex.Message}"); }
            try { ComplianceScan.InvalidateCache(); } catch { }

            TaskDialog.Show("STING Auto-Route",
                $"Routed {routed} of {unrouted.Count} cable(s).\n\n" +
                "Note: routes use simplified L-shaped paths.\n" +
                "Review and adjust in Revit for clash avoidance.");
            return Result.Succeeded;
        }
    }
}
