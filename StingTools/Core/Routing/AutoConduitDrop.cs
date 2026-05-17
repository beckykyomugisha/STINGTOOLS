using StingTools.Core;
// StingTools v4 MVP — AutoConduitDrop.
//
// For each selected fixture with an electrical connector, finds the
// nearest conduit or cable tray within search radius and emits a
// vertical drop conduit from the fixture point up to the intercept.
// The created conduit is tagged with ELC_CDT_INSTALL_METHOD_TXT and
// ELC_CDT_FAB_METHOD_TXT for downstream fabrication takeoff.
//
// Algorithm gaps addressed (Phase 179 review):
//   Gap 1  — Multi-category search: prefers OST_CableTray, falls back
//             to OST_Conduit when no tray is in radius. Controlled via
//             SearchFallbackToConduit (default true).
//   Gap 2  — Minimum run-length guard: drops shorter than MinDropMm
//             (default 50 mm) are rejected with a warning rather than
//             creating degenerate geometry.
//   Gap 3  — Chase router stamps ELC_CDT_RUN_LENGTH_M and
//             ELC_CDT_BEND_COUNT_NR on every created segment (was: only
//             INSTALL_METHOD + FAB_METHOD were stamped).
//   Gap 4  — Level fallback: when both host.LevelId and active-view
//             GenLevel are unavailable (3D view, no linked model), the
//             engine walks FilteredElementCollector<Level> and picks the
//             nearest level below the drop origin.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using StingTools.Core.Placement;

namespace StingTools.Core.Routing
{
    public class AutoConduitDrop : DropEngineBase
    {
        /// <summary>
        /// Default search radius when looking for a cable tray / conduit
        /// above a fixture. 3000mm is conservative for building services.
        /// </summary>
        public double SearchRadiusMm { get; set; } = 3000.0;

        /// <summary>
        /// Gap 1 fix — when no cable tray is found within SearchRadiusMm,
        /// attempt a second pass against OST_Conduit so that fixtures that
        /// feed into an existing conduit branch rather than a tray are
        /// handled correctly. Default true. Set false in projects where
        /// every fixture must terminate on a tray (strict BS EN 50174-2
        /// containment-system enforcement).
        /// </summary>
        public bool SearchFallbackToConduit { get; set; } = true;

        /// <summary>
        /// Gap 2 fix — reject drops shorter than this threshold (mm).
        /// Fixtures placed directly on or within touching distance of a
        /// tray would create sub-millimetre conduit geometry that confuses
        /// Revit's MEP topology engine. 50 mm is the practical minimum for
        /// a conduit entry stub with a locknut and bushing.
        /// </summary>
        public double MinDropMm { get; set; } = 50.0;

        /// <summary>
        /// Installation method written to ELC_CDT_INSTALL_METHOD_TXT
        /// on the created drop. Clipped / suspended / embedded / chased.
        /// </summary>
        public string InstallMethod { get; set; } = "CLIPPED";

        /// <summary>Fabrication method; typically "SITE" for drops.</summary>
        public string FabMethod { get; set; } = "SITE";

        /// <summary>
        /// When true, fixtures whose host is a Wall and whose
        /// InstallMethod is "CHASED" are routed via
        /// <see cref="InWallChaseRouter"/> instead of the standard
        /// fixture-to-tray drop. The chase router reads the wall's
        /// compound structure, computes available chase depth, and
        /// rejects routes that don't fit the conduit OD + cover. Falls
        /// back to the standard drop on any chase-router failure.
        /// </summary>
        public bool UseChaseRoutingWhenAvailable { get; set; } = false;

        /// <summary>
        /// Default cover (mm) over the chased conduit. Used when the
        /// hosting wall is concrete and a structural cover figure isn't
        /// available from the rule.
        /// </summary>
        public double ChaseCoverMm { get; set; } = 30.0;

        /// <summary>
        /// Try to route a fixture's drop in-wall when the host wall has
        /// a compound structure that admits a chase. Returns true on
        /// successful chase route, false to signal "fall back to plumb-
        /// line drop". Always non-destructive when it returns false —
        /// no half-routed conduits left behind.
        /// </summary>
        public bool TryDropViaChase(Element fixtureEl, double conduitDiamMm, DropResult result)
        {
            if (!UseChaseRoutingWhenAvailable || fixtureEl == null) return false;

            try
            {
                // 1) Host must be a Wall — chases only make sense in walls.
                //    Element doesn't expose Host; FamilyInstance does.
                var fi = fixtureEl as FamilyInstance;
                var wall = fi?.Host as Wall;
                if (wall == null) return false;

                // 2) Compound structure must exist for a meaningful depth check.
                var cs = wall.WallType?.GetCompoundStructure();
                if (cs == null) return false;

                // 3) Synthesize a minimal PlacementRule for the chase router.
                //    Only the subset of fields the router actually reads is
                //    populated — keeps the bridge cheap and avoids surprising
                //    cross-talk with the wider placement engine.
                var rule = new PlacementRule
                {
                    RuleId = "auto-conduit-chase",
                    MountingContext = "CHASED",
                    NominalDiameterMm = conduitDiamMm,
                    InsulationThicknessMm = 0,
                    ExposureClass = "XC2",     // typical interior-wall chase
                };

                // 4) Endpoints: from the fixture's connector / location to the
                //    intercept point on the host containment, exactly as the
                //    standard drop would compute. The chase router projects
                //    these onto the wall's location curve internally.
                Connector fxConn = FindBestFreeConnector(fixtureEl);
                XYZ origin = fxConn?.Origin
                          ?? (fixtureEl.Location as LocationPoint)?.Point;
                if (origin == null) return false;

                Element containment = FindNearestContainment(origin,
                    BuiltInCategory.OST_CableTray, SearchRadiusMm);
                XYZ end = containment != null ? ComputeInterceptPoint(origin, containment) : null;
                if (end == null) end = new XYZ(origin.X, origin.Y, origin.Z + (3000.0 / 304.8));

                // 5) Delegate.
                var router = new InWallChaseRouter(Doc, null);
                var route = router.Route(wall, origin, end, rule, ConduitTypeId, ElementId.InvalidElementId);

                if (route.CreatedSegments.Count == 0)
                {
                    foreach (var w in route.Warnings) result.Warnings.Add($"Chase: {w}");
                    return false;        // caller will fall back
                }

                // Gap 3 fix — compute total run length and bend count across all
                // chase segments so downstream QA reads the same fields as a
                // standard plumb-line drop.
                double chaseTotalMm = 0;
                foreach (var segId in route.CreatedSegments)
                {
                    var segEl = Doc.GetElement(segId) as MEPCurve;
                    var segLoc = segEl?.Location as LocationCurve;
                    if (segLoc?.Curve != null)
                        chaseTotalMm += segLoc.Curve.Length * 304.8;
                }
                int chaseBends = Math.Max(0, route.CreatedSegments.Count - 1);

                foreach (var id in route.CreatedSegments)
                {
                    result.CreatedIds.Add(id);
                    var cdt = Doc.GetElement(id);
                    TrySetString(cdt, "ELC_CDT_INSTALL_METHOD_TXT", "CHASED");
                    TrySetString(cdt, "ELC_CDT_FAB_METHOD_TXT", FabMethod);
                    try
                    {
                        TrySetString(cdt, "ELC_CDT_RUN_LENGTH_M",
                            (chaseTotalMm / 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                        TrySetString(cdt, "ELC_CDT_BEND_COUNT_NR", chaseBends.ToString());
                    }
                    catch (Exception ex) { result.Warnings.Add($"Stamp chase run-length: {ex.Message}"); }
                }
                foreach (var w in route.Warnings) result.Warnings.Add($"Chase: {w}");
                return true;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"TryDropViaChase: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ConduitType used for the drop. If null, the engine uses the
        /// first available ConduitType it finds in the document.
        /// </summary>
        public ElementId ConduitTypeId { get; set; }

        public AutoConduitDrop(Document doc) : base(doc)
        {
            ConnectorDomain = Domain.DomainCableTrayConduit;
            ServiceId       = "ELC_PWR"; // default — overridable from command
        }

        /// <summary>
        /// Cable-tray / conduit has no NewTakeoffFitting API — wire-up at
        /// the host end uses Connector.ConnectTo with an adjacent tray
        /// connector instead.
        /// </summary>
        protected override bool SupportsTakeoff => false;

        public DropResult Execute(IList<Element> fixtures)
        {
            var result = new DropResult { Discipline = "Electrical" };
            if (fixtures == null || fixtures.Count == 0)
            {
                result.Warnings.Add("AutoConduitDrop: no fixtures supplied");
                return result;
            }

            ResolveConduitType(result);
            if (ConduitTypeId == null || ConduitTypeId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("AutoConduitDrop: no ConduitType found in project");
                return result;
            }

            // Inspect the RoutingPreferenceManager on the chosen ConduitType.
            try
            {
                var ct = Doc.GetElement(ConduitTypeId) as ConduitType;
                var rpt = RoutingPreferenceInspector.Inspect(ct);
                if (!rpt.IsProductionReady)
                    result.Warnings.Add($"RoutingPreferenceManager gaps: {rpt}");
                else
                    StingLog.Info($"AutoConduitDrop: {rpt}");
            }
            catch (Exception ex)
            { result.Warnings.Add($"RoutingPreferenceInspector: {ex.Message}"); }

            using (var tx = new Transaction(Doc, "STING v4 Auto-conduit drop"))
            {
                try { tx.Start(); }
                catch (Exception ex2)
                {
                    result.Warnings.Add($"Transaction start failed: {ex2.Message}");
                    return result;
                }

                try
                {
                    foreach (var fx in fixtures)
                    {
                        try
                        {
                            // Gap 1 fix — try cable tray first; fall back to conduit
                            // when the fixture is not beneath any tray.
                            bool dropped = TryDropFromFixture(
                                fx, BuiltInCategory.OST_CableTray, SearchRadiusMm, result);

                            if (!dropped && SearchFallbackToConduit)
                            {
                                // Remove the "no tray found" warning from the first pass
                                // so the caller sees only the conduit-pass result.
                                var removable = result.Warnings.FindAll(
                                    w => w.Contains("CableTray") && w.Contains(fx?.Id.Value.ToString() ?? ""));
                                foreach (var w in removable) result.Warnings.Remove(w);
                                // Reset the skip/fail counts so the fallback can re-score.
                                result.SkippedCount = Math.Max(0, result.SkippedCount - 1);

                                TryDropFromFixture(
                                    fx, BuiltInCategory.OST_Conduit, SearchRadiusMm, result);
                            }
                        }
                        catch (Exception ex3)
                        {
                            result.FailedCount++;
                            result.Warnings.Add($"Drop from {fx?.Id}: {ex3.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex3)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"AutoConduitDrop fatal: {ex3.Message}");
                }
            }

            return result;
        }

        private void ResolveConduitType(DropResult result)
        {
            if (ConduitTypeId != null && ConduitTypeId != ElementId.InvalidElementId) return;
            try
            {
                var col = new FilteredElementCollector(Doc).OfClass(typeof(ConduitType));
                foreach (var el in col)
                {
                    if (el is ConduitType ct) { ConduitTypeId = ct.Id; break; }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Resolve ConduitType: {ex.Message}");
            }
        }

        protected override ElementId CreateRunBetween(XYZ from, XYZ to, Element host, DropResult result)
        {
            if (from == null || to == null) return ElementId.InvalidElementId;
            if (ConduitTypeId == null || ConduitTypeId == ElementId.InvalidElementId) return ElementId.InvalidElementId;

            // Gap 2 fix — reject sub-MinDropMm runs before touching the document.
            // Fixtures placed directly on or nearly touching a tray would otherwise
            // produce degenerate conduit geometry that breaks Revit's MEP topology.
            double dropMm = from.DistanceTo(to) * 304.8;
            if (dropMm < MinDropMm)
            {
                result.Warnings.Add(
                    $"Drop skipped: {dropMm:F0} mm < MinDropMm ({MinDropMm:F0} mm). " +
                    "Fixture is too close to containment for a valid conduit entry stub.");
                result.SkippedCount++;
                return ElementId.InvalidElementId;
            }

            ElementId levelId = host?.LevelId ?? ElementId.InvalidElementId;
            if (levelId == ElementId.InvalidElementId && Doc.ActiveView != null)
                levelId = Doc.ActiveView.GenLevel?.Id ?? ElementId.InvalidElementId;

            // Gap 4 fix — when the host has no LevelId and the active view is a
            // 3D view (no GenLevel), walk every Level in the document and pick the
            // nearest one below the drop origin. This covers detached/linked-model
            // workflows where a 3D view is open and fixtures have no host level.
            if (levelId == ElementId.InvalidElementId)
            {
                try
                {
                    double originZFt = from.Z;
                    Level nearest = null;
                    double nearestDelta = double.MaxValue;
                    foreach (var lvlEl in new FilteredElementCollector(Doc).OfClass(typeof(Level)))
                    {
                        if (!(lvlEl is Level lvl)) continue;
                        double delta = originZFt - lvl.Elevation;
                        if (delta >= 0 && delta < nearestDelta)
                        {
                            nearestDelta = delta;
                            nearest = lvl;
                        }
                    }
                    if (nearest != null) levelId = nearest.Id;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Level fallback collect: {ex.Message}");
                }
            }

            if (levelId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("CreateRunBetween: no level found (host, view, or collector); skipping conduit drop");
                return ElementId.InvalidElementId;
            }

            try
            {
                // Conduit.Create(doc, conduitTypeId, from, to, levelId) —
                // verified against Revit 2025 API. Returned Conduit
                // exposes two end connectors that DropEngineBase wires
                // up via Connector.ConnectTo (conduits have no takeoff
                // fitting so SupportsTakeoff is false).
                var cdt = Conduit.Create(Doc, ConduitTypeId, from, to, levelId);
                if (cdt == null) return ElementId.InvalidElementId;

                TrySetString(cdt, "ELC_CDT_INSTALL_METHOD_TXT", InstallMethod);
                TrySetString(cdt, "ELC_CDT_FAB_METHOD_TXT",     FabMethod);

                // Stamp run length so ElectricalStandardsValidator + downstream
                // QA can read it without recomputing from LocationCurve. Bend
                // count starts at 0 (this is a single straight drop) and is
                // re-evaluated by the validator's geometric fallback when
                // fittings get added later.
                try
                {
                    double mm = from.DistanceTo(to) * 304.8; // ft → mm
                    TrySetString(cdt, "ELC_CDT_RUN_LENGTH_M",
                        (mm / 1000.0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                    TrySetString(cdt, "ELC_CDT_BEND_COUNT_NR", "0");
                }
                catch (Exception ex) { result.Warnings.Add($"Stamp run-length: {ex.Message}"); }

                // Phase A — validate every connected bend's radius
                // against the manufacturer minimum (BS EN 61386 by
                // material). Findings flow to result.Warnings so the
                // user sees them in the conduit-drop result panel.
                try
                {
                    var findings = BendRadiusValidator.ValidateRun(Doc, cdt);
                    foreach (var f in findings)
                        result.Warnings.Add($"Bend radius: {f.Reason}");
                }
                catch (Exception ex)
                { result.Warnings.Add($"BendRadiusValidator: {ex.Message}"); }

                return cdt.Id;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Conduit.Create failed: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }
    }
}
