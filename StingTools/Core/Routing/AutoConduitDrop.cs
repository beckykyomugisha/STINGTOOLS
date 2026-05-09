// StingTools v4 MVP — AutoConduitDrop.
//
// For each selected fixture with an electrical connector, finds the
// nearest conduit or cable tray within search radius and emits a
// vertical drop conduit from the fixture point up to the intercept.
// The created conduit is tagged with ELC_CDT_INSTALL_METHOD_TXT and
// ELC_CDT_FAB_METHOD_TXT for downstream fabrication takeoff.

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
                var host = fixtureEl.Host;
                var wall = host as Wall;
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

                foreach (var id in route.CreatedSegments)
                {
                    result.CreatedIds.Add(id);
                    var cdt = Doc.GetElement(id);
                    TrySetString(cdt, "ELC_CDT_INSTALL_METHOD_TXT", "CHASED");
                    TrySetString(cdt, "ELC_CDT_FAB_METHOD_TXT", FabMethod);
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
                catch (Exception ex)
                {
                    result.Warnings.Add($"Transaction start failed: {ex.Message}");
                    return result;
                }

                try
                {
                    foreach (var fx in fixtures)
                    {
                        try
                        {
                            TryDropFromFixture(fx, BuiltInCategory.OST_CableTray, SearchRadiusMm, result);
                        }
                        catch (Exception ex)
                        {
                            result.FailedCount++;
                            result.Warnings.Add($"Drop from {fx?.Id}: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    result.Warnings.Add($"AutoConduitDrop fatal: {ex.Message}");
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

            ElementId levelId = host?.LevelId ?? ElementId.InvalidElementId;
            if (levelId == ElementId.InvalidElementId && Doc.ActiveView != null)
                levelId = Doc.ActiveView.GenLevel?.Id ?? ElementId.InvalidElementId;

            if (levelId == ElementId.InvalidElementId)
            {
                result.Warnings.Add("CreateRunBetween: no host level; skipping conduit drop");
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
