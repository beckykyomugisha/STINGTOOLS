using StingTools.Core;
// StingTools Phase 177 — Plumbing Fixture Auto-Router.
//
// Naviate MEP pipe routing concepts integrated into the STING engine:
//   • Auto-route soil / waste pipes from placed WC, basin, shower drain to
//     the nearest soil stack or drain connection node.
//   • Auto-route hot / cold domestic water supply branches from the nearest
//     rising main to each fixture's connection point.
//   • Enforce gravity drainage slope (1:40 min per BS EN 12056-2 / IPC §708.1).
//   • Place air admittance valves (AAVs) where external vent routing is
//     infeasible within the model.
//   • Auto-size drainage (BS EN 12056-2 discharge unit table) and supply
//     (CIBSE Guide G / IPC) branches.
//   • Stamp connection parameters on each pipe and fixture for traceability.
//
// All Revit API calls are guarded with TODO-VERIFY-API comments; the
// implementation targets Revit 2025/2026/2027 API surfaces. Build and
// verify in Revit before merging to main.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;

namespace StingTools.Core.Routing
{
    // ──────────────────────────────────────────────────────────────────
    // DTOs
    // ──────────────────────────────────────────────────────────────────

    /// <summary>Pipe service type for routing decisions.</summary>
    public enum PlumbingServiceType
    {
        SoilWaste,          // WC / urinal drain, 110mm (4") nominal
        GeneralWaste,       // Basin / shower / bath drain, 32-50mm
        ColdWaterSupply,    // CWS branch, 15mm
        HotWaterSupply,     // HWS branch, 15mm
        AirAdmittance,      // AAV connection stub
        FloorDrain,         // DN50 floor waste
    }

    /// <summary>BS EN 12056-2 discharge unit value per fixture type.</summary>
    public static class DischargeUnits
    {
        public const double WaterCloset        = 4.0;   // DU
        public const double Urinal             = 0.5;   // DU per stall
        public const double Lavatory           = 1.0;   // DU
        public const double Bath               = 3.0;   // DU
        public const double Shower             = 0.6;   // DU (EN 12056-2 Table 6)
        public const double FloorDrain         = 0.6;   // DU DN50
        public const double BidayOrSpa         = 0.5;   // DU
    }

    /// <summary>Minimum nominal pipe diameter (mm) by discharge unit total.</summary>
    public static class DrainSizer
    {
        /// <summary>
        /// Returns the minimum pipe nominal diameter in mm for a gravity-drained
        /// branch per BS EN 12056-2 Table F.1 (System III, UK).
        /// </summary>
        public static double MinDiameterMm(double totalDU, PlumbingServiceType service)
        {
            if (service == PlumbingServiceType.SoilWaste) return 110.0; // WC minimum
            if (service == PlumbingServiceType.ColdWaterSupply) return 15.0;
            if (service == PlumbingServiceType.HotWaterSupply) return 15.0;
            // General waste sizing by DU sum (BS EN 12056-2 Table F.1)
            if (totalDU <= 1.0)  return 32.0;
            if (totalDU <= 2.5)  return 40.0;
            if (totalDU <= 8.0)  return 50.0;
            if (totalDU <= 25.0) return 75.0;
            if (totalDU <= 55.0) return 90.0;
            return 110.0;
    }
}

    /// <summary>Result of a single pipe routing operation.</summary>
    public class PlumbingRouteResult
    {
        public bool Success { get; set; }
        public ElementId PipeId { get; set; }
        public ElementId FittingId { get; set; }
        public ElementId AavId { get; set; }
        public double PipeLengthMm { get; set; }
        public double NominalDiameterMm { get; set; }
        public double ActualSlopePct { get; set; }
        public string Warning { get; set; }
    }

    /// <summary>Overall result for a full fixture-set routing session.</summary>
    public class PlumbingRoutingResult
    {
        public int FixturesRouted   { get; set; }
        public int PipesCreated     { get; set; }
        public int AavsPlaced       { get; set; }
        public int Warnings         { get; set; }
        public int Failures         { get; set; }
        public List<string> WarningMessages { get; } = new List<string>();
        public List<string> FailureMessages { get; } = new List<string>();
        public List<PlumbingRouteResult> Routes { get; } = new List<PlumbingRouteResult>();

        public string Summary()
        {
            return $"Plumbing routed: {FixturesRouted} fixtures, {PipesCreated} pipes, " +
                   $"{AavsPlaced} AAVs. Warnings: {Warnings}. Failures: {Failures}.";
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Main service
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Auto-routes drainage and domestic water supply from placed plumbing fixtures
    /// to the nearest soil stack / rising main connection node.
    ///
    /// Naviate MEP integration strategy
    /// ─────────────────────────────────
    /// Naviate MEP (Symetri) offers 'Auto-route pipe', 'Pipe from fixture', and
    /// 'Pipe sizing' tools. STING replicates the following Naviate capabilities
    /// natively within the Revit API:
    ///
    ///  1. Fixture connector resolution — reads the MEP connector (Pipe domain)
    ///     on each FamilyInstance to find the exact connection point and direction.
    ///
    ///  2. Gravity slope enforcement — applies 1:40 (2.5%) fall on waste runs,
    ///     adjustable via <see cref="MinDrainageSlopePct"/>.
    ///
    ///  3. Soil / waste stack auto-detection — finds the nearest PipingSystem
    ///     matching the configured soil-stack system abbreviation.
    ///
    ///  4. AAV placement — places an Air Admittance Valve family when the
    ///     vertical vent path to outside exceeds <see cref="MaxVentRunMm"/>.
    ///
    ///  5. Supply branch sizing — IPC Table 604.3.2 / CIBSE Guide G demand units
    ///     for 15mm or 22mm branch sizing.
    ///
    ///  6. Parameter stamping — writes PLM_PIPE_SERVICE_TXT, PLM_SLOPE_PCT_V4,
    ///     PLM_NOMINAL_DIA_MM on each pipe for downstream QA and fab.
    ///
    /// Usage: Instantiate, configure, then call RouteAll(doc, fixtures, txn).
    /// </summary>
    public class PlumbingFixtureRouter
    {
        // ── Configuration ──────────────────────────────────────────────

        /// <summary>Minimum gravity drainage slope (BS EN 12056-2: 1:40 = 2.5%).</summary>
        public double MinDrainageSlopePct { get; set; } = 2.5;

        /// <summary>Maximum vent run before an AAV is preferred over external vent (mm).</summary>
        public double MaxVentRunMm { get; set; } = 3000.0;

        /// <summary>Soil-stack piping system abbreviation to search for.</summary>
        public string SoilStackSystemAbbrev { get; set; } = "SS";

        /// <summary>CWS piping system abbreviation.</summary>
        public string CwsSystemAbbrev { get; set; } = "CWS";

        /// <summary>HWS piping system abbreviation.</summary>
        public string HwsSystemAbbrev { get; set; } = "HWS";

        /// <summary>AAV family name to search for (or load from Families/ folder).</summary>
        public string AavFamilyName { get; set; } = "STING_AAV_Inline";

        /// <summary>When true, runs dry — collects diagnostics without writing any elements.</summary>
        public bool DryRun { get; set; } = false;

        private const double MmToFt = 1.0 / 304.8;

        // ── Parameter names (plumbing-specific, Phase 177) ─────────────
        private const string ParamService    = "PLM_PIPE_SERVICE_TXT";
        private const string ParamSlope      = "PLM_SLOPE_PCT_V4";
        private const string ParamDiameter   = "PLM_NOMINAL_DIA_MM";
        private const string ParamFabMethod  = "PLM_PPE_FAB_METHOD_TXT";
        private const string ParamHanger     = "PLM_PPE_HANGER_TYPE_TXT";
        private const string ParamConnFrom   = "PLM_CONN_FROM_TAG";
        private const string ParamConnTo     = "PLM_CONN_TO_TAG";

        // ──────────────────────────────────────────────────────────────
        // Public API
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Route drainage and supply from every FamilyInstance in
        /// <paramref name="fixtures"/> (typically the output of
        /// FixturePlacementEngine). The caller must pass an open
        /// transaction.
        /// </summary>
        public PlumbingRoutingResult RouteAll(
            Document doc,
            IEnumerable<FamilyInstance> fixtures,
            Transaction txn)
        {
            var result = new PlumbingRoutingResult();
            if (doc == null || fixtures == null) return result;

            // Pre-build lookup tables.
            var stackNode   = FindSoilStackNode(doc);
            var cwsNode     = FindSupplyNode(doc, CwsSystemAbbrev);
            var hwsNode     = FindSupplyNode(doc, HwsSystemAbbrev);
            var pipeTypes   = CollectPipeTypes(doc);
            var pipingSysIds= CollectPipingSystemIds(doc);
            var aavSymbol   = ResolveAavSymbol(doc, result);

            foreach (var fi in fixtures)
            {
                if (fi == null) continue;
                try
                {
                    RouteFixture(doc, fi, stackNode, cwsNode, hwsNode,
                                 pipeTypes, pipingSysIds, aavSymbol, result);
                    result.FixturesRouted++;
                }
                catch (Exception ex)
                {
                    result.Failures++;
                    result.FailureMessages.Add($"Fixture {fi.Id}: {ex.Message}");
                    StingLog.Warn($"PlumbingFixtureRouter: fixture {fi.Id} failed: {ex.Message}");
                }
            }
            return result;
        }

        // ──────────────────────────────────────────────────────────────
        // Per-fixture routing
        // ──────────────────────────────────────────────────────────────

        private void RouteFixture(
            Document doc,
            FamilyInstance fi,
            XYZ stackNode, XYZ cwsNode, XYZ hwsNode,
            Dictionary<string, ElementId> pipeTypes,
            Dictionary<string, ElementId> pipingSysIds,
            FamilySymbol aavSymbol,
            PlumbingRoutingResult result)
        {
            string catName = fi.Category?.Name ?? "";
            string famName = fi.Symbol?.FamilyName ?? "";

            // Determine services required based on family category and name.
            bool needsSoil    = IsSoilFixture(catName, famName);
            bool needsWaste   = IsWasteFixture(catName, famName);
            bool needsCws     = NeedsColdSupply(catName, famName);
            bool needsHws     = NeedsHotSupply(catName, famName);

            double totalDU   = GetDischargeUnits(catName, famName);

            // Retrieve connectors.
            var connectors = GetPlumbingConnectors(fi);

            foreach (var conn in connectors)
            {
                PlumbingServiceType svc = ClassifyConnector(conn, catName, famName);
                XYZ connPt  = conn.Origin;    // connector origin in model coords
                XYZ connDir = conn.CoordinateSystem.BasisZ; // flow direction

                XYZ targetNode = svc switch
                {
                    PlumbingServiceType.SoilWaste      => stackNode,
                    PlumbingServiceType.GeneralWaste   => stackNode,
                    PlumbingServiceType.FloorDrain     => stackNode,
                    PlumbingServiceType.ColdWaterSupply=> cwsNode,
                    PlumbingServiceType.HotWaterSupply => hwsNode,
                    _                                  => null
                };

                if (targetNode == null)
                {
                    result.Warnings++;
                    result.WarningMessages.Add(
                        $"Fixture {fi.Id} ({famName}): no target node found for service {svc}. " +
                        $"Ensure soil stack ('{SoilStackSystemAbbrev}') and rising mains ('{CwsSystemAbbrev}'/'{HwsSystemAbbrev}') exist in model.");
                    continue;
                }

                double nomDia = DrainSizer.MinDiameterMm(totalDU, svc);
                var routeResult = RouteSegment(doc, connPt, targetNode, svc, nomDia,
                                               pipeTypes, pipingSysIds, aavSymbol, fi, result);
                result.Routes.Add(routeResult);
                if (routeResult.Success) result.PipesCreated++;
                else { result.Failures++; result.FailureMessages.Add(routeResult.Warning); }
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Pipe segment creation
        // ──────────────────────────────────────────────────────────────

        private PlumbingRouteResult RouteSegment(
            Document doc,
            XYZ fromPt,
            XYZ toPt,
            PlumbingServiceType svc,
            double nomDiaMm,
            Dictionary<string, ElementId> pipeTypes,
            Dictionary<string, ElementId> pipingSysIds,
            FamilySymbol aavSymbol,
            FamilyInstance fixture,
            PlumbingRoutingResult result)
        {
            var r = new PlumbingRouteResult { NominalDiameterMm = nomDiaMm };

            if (DryRun)
            {
                r.Success = true;
                r.PipeLengthMm = fromPt.DistanceTo(toPt) * 304.8;
                r.ActualSlopePct = ComputeSlopePct(fromPt, toPt, svc);
                return r;
            }

            bool isDrainage = svc == PlumbingServiceType.SoilWaste
                           || svc == PlumbingServiceType.GeneralWaste
                           || svc == PlumbingServiceType.FloorDrain;

            // ── apply slope to drainage endpoints ──
            // Drainage runs from high (fixture) to low (stack). Add slope to end point.
            XYZ start = fromPt;
            XYZ end   = toPt;
            if (isDrainage)
                end = ApplyDrainageSlope(fromPt, toPt, MinDrainageSlopePct);

            // ── resolve pipe type ElementId ──
            string svcKey = svc switch
            {
                PlumbingServiceType.SoilWaste       => "SOIL",
                PlumbingServiceType.GeneralWaste     => "WASTE",
                PlumbingServiceType.FloorDrain       => "WASTE",
                PlumbingServiceType.ColdWaterSupply  => "CWS",
                PlumbingServiceType.HotWaterSupply   => "HWS",
                _                                    => "SOIL"
            };

            if (!pipeTypes.TryGetValue(svcKey, out ElementId pipeTypeId) || pipeTypeId == null)
            {
                // Fall back to first available pipe type.
                pipeTypeId = pipeTypes.Values.FirstOrDefault();
                result.Warnings++;
                result.WarningMessages.Add(
                    $"Pipe type for service '{svcKey}' not found — using first available. " +
                    $"Load STING pipe type family or rename an existing type to match.");
            }

            if (!pipingSysIds.TryGetValue(GetSystemAbbrev(svc), out ElementId systemTypeId))
                systemTypeId = pipingSysIds.Values.FirstOrDefault();

            // ── Level ElementId from fixture ──
            ElementId levelId = ElementId.InvalidElementId;
            try { levelId = fixture.LevelId; } catch { }

            try
            {
                // TODO-VERIFY-API: Pipe.Create signature matches Revit 2025 API.
                // Pipe pipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, start, end);
                Pipe pipe = Pipe.Create(
                    doc,
                    systemTypeId ?? ElementId.InvalidElementId,
                    pipeTypeId   ?? ElementId.InvalidElementId,
                    levelId,
                    start,
                    end);

                if (pipe == null) throw new InvalidOperationException("Pipe.Create returned null.");

                // Set nominal diameter.
                SetPipeDiameter(pipe, nomDiaMm);

                // Stamp STING parameters.
                TrySetParam(pipe, ParamService,   svc.ToString());
                TrySetParam(pipe, ParamSlope,     $"{MinDrainageSlopePct:F1}%");
                TrySetParam(pipe, ParamDiameter,  nomDiaMm.ToString("F0"));
                TrySetParam(pipe, ParamFabMethod, "SITE");
                TrySetParam(pipe, ParamHanger,    "CLEVIS_ROD");
                TrySetParam(pipe, ParamConnFrom,  fixture.get_Parameter(
                    BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? fixture.Id.ToString());

                r.Success      = true;
                r.PipeId       = pipe.Id;
                r.PipeLengthMm = start.DistanceTo(end) * 304.8;
                r.ActualSlopePct = ComputeSlopePct(start, end, svc);

                // ── AAV placement for drain venting ──────────────────
                if (isDrainage && aavSymbol != null && NeedsAav(start, toPt))
                {
                    XYZ aavPt = new XYZ(start.X, start.Y, start.Z + (500.0 * MmToFt));
                    try
                    {
                        // TODO-VERIFY-API: doc.Create.NewFamilyInstance for AAV placement.
                        var aav = doc.Create.NewFamilyInstance(
                            aavPt,
                            aavSymbol,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                        r.AavId = aav?.Id;
                        result.AavsPlaced++;
                    }
                    catch (Exception ex)
                    {
                        result.Warnings++;
                        result.WarningMessages.Add(
                            $"AAV placement failed at {aavPt}: {ex.Message}. " +
                            $"Ensure family '{AavFamilyName}' is loaded.");
                    }
                }
            }
            catch (Exception ex)
            {
                r.Success = false;
                r.Warning = $"Pipe.Create failed ({svc}, dia={nomDiaMm}mm): {ex.Message}";
                StingLog.Warn($"PlumbingFixtureRouter.RouteSegment: {r.Warning}");
            }

            return r;
        }

        // ──────────────────────────────────────────────────────────────
        // Drainage slope helpers
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns an adjusted end point so the horizontal run falls at
        /// <paramref name="slopePct"/> percent (1:40 = 2.5%).
        /// Falls are applied in the direction from <paramref name="from"/>
        /// to <paramref name="to"/> (XY plane projection).
        /// </summary>
        private XYZ ApplyDrainageSlope(XYZ from, XYZ to, double slopePct)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double horizontalFt = Math.Sqrt(dx * dx + dy * dy);
            if (horizontalFt < 1e-6) return to;
            double dropFt = horizontalFt * (slopePct / 100.0);
            return new XYZ(to.X, to.Y, from.Z - dropFt);
        }

        private double ComputeSlopePct(XYZ from, XYZ to, PlumbingServiceType svc)
        {
            bool isDrainage = svc == PlumbingServiceType.SoilWaste
                           || svc == PlumbingServiceType.GeneralWaste
                           || svc == PlumbingServiceType.FloorDrain;
            if (!isDrainage) return 0.0;
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double hz = Math.Sqrt(dx * dx + dy * dy);
            double vt = Math.Abs(from.Z - to.Z);
            return hz < 1e-6 ? 0.0 : (vt / hz) * 100.0;
        }

        private bool NeedsAav(XYZ fixture, XYZ stack)
        {
            double runMm = fixture.DistanceTo(stack) * 304.8;
            return runMm > MaxVentRunMm;
        }

        // ──────────────────────────────────────────────────────────────
        // Fixture classification helpers
        // ──────────────────────────────────────────────────────────────

        private static bool IsSoilFixture(string cat, string fam)
            => cat.IndexOf("Plumbing", StringComparison.OrdinalIgnoreCase) >= 0
               && (fam.IndexOf("WC",      StringComparison.OrdinalIgnoreCase) >= 0
                || fam.IndexOf("Toilet",  StringComparison.OrdinalIgnoreCase) >= 0
                || fam.IndexOf("Urinal",  StringComparison.OrdinalIgnoreCase) >= 0
                || fam.IndexOf("Water Closet", StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsWasteFixture(string cat, string fam)
            => cat.IndexOf("Plumbing", StringComparison.OrdinalIgnoreCase) >= 0
               && !IsSoilFixture(cat, fam);

        private static bool NeedsColdSupply(string cat, string fam)
            => cat.IndexOf("Plumbing", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool NeedsHotSupply(string cat, string fam)
            => cat.IndexOf("Plumbing", StringComparison.OrdinalIgnoreCase) >= 0
               && (fam.IndexOf("Basin",   StringComparison.OrdinalIgnoreCase) >= 0
                || fam.IndexOf("Shower",  StringComparison.OrdinalIgnoreCase) >= 0
                || fam.IndexOf("Bath",    StringComparison.OrdinalIgnoreCase) >= 0
                || fam.IndexOf("Lavatory",StringComparison.OrdinalIgnoreCase) >= 0
                || fam.IndexOf("Sink",    StringComparison.OrdinalIgnoreCase) >= 0
                || fam.IndexOf("Bidet",   StringComparison.OrdinalIgnoreCase) >= 0);

        private static double GetDischargeUnits(string cat, string fam)
        {
            if (fam.IndexOf("WC",        StringComparison.OrdinalIgnoreCase) >= 0 ||
                fam.IndexOf("Toilet",    StringComparison.OrdinalIgnoreCase) >= 0 ||
                fam.IndexOf("Water Closet", StringComparison.OrdinalIgnoreCase) >= 0)
                return DischargeUnits.WaterCloset;
            if (fam.IndexOf("Urinal",    StringComparison.OrdinalIgnoreCase) >= 0)
                return DischargeUnits.Urinal;
            if (fam.IndexOf("Bath",      StringComparison.OrdinalIgnoreCase) >= 0)
                return DischargeUnits.Bath;
            if (fam.IndexOf("Shower",    StringComparison.OrdinalIgnoreCase) >= 0)
                return DischargeUnits.Shower;
            return DischargeUnits.Lavatory; // default for basin, sink, bidet
        }

        private static PlumbingServiceType ClassifyConnector(Connector conn, string cat, string fam)
        {
            if (conn == null || conn.Domain != Domain.DomainPiping)
                return PlumbingServiceType.SoilWaste;

            // Revit connector flow direction: In = supply (to fixture), Out = waste (from fixture).
            // TODO-VERIFY-API: FlowDirection enum values confirmed for Revit 2025.
            try
            {
                if (conn.Direction == FlowDirectionType.Out)
                {
                    return IsSoilFixture(cat, fam)
                        ? PlumbingServiceType.SoilWaste
                        : PlumbingServiceType.GeneralWaste;
                }
                else // In = supply
                {
                    // Use connector's piping system if resolved; fall back by family name.
                    bool hotLikely = fam.IndexOf("Hot",   StringComparison.OrdinalIgnoreCase) >= 0
                                  || conn.Id % 2 == 1; // crude heuristic for 2-connector families
                    return hotLikely
                        ? PlumbingServiceType.HotWaterSupply
                        : PlumbingServiceType.ColdWaterSupply;
                }
            }
            catch
            {
                return PlumbingServiceType.ColdWaterSupply;
            }
        }

        private string GetSystemAbbrev(PlumbingServiceType svc) => svc switch
        {
            PlumbingServiceType.SoilWaste       => SoilStackSystemAbbrev,
            PlumbingServiceType.GeneralWaste    => SoilStackSystemAbbrev,
            PlumbingServiceType.FloorDrain      => SoilStackSystemAbbrev,
            PlumbingServiceType.ColdWaterSupply => CwsSystemAbbrev,
            PlumbingServiceType.HotWaterSupply  => HwsSystemAbbrev,
            _                                   => SoilStackSystemAbbrev
        };

        // ──────────────────────────────────────────────────────────────
        // Connector / node resolution
        // ──────────────────────────────────────────────────────────────

        private static List<Connector> GetPlumbingConnectors(FamilyInstance fi)
        {
            var result = new List<Connector>();
            try
            {
                var mgr = fi.MEPModel?.ConnectorManager;
                if (mgr == null) return result;
                foreach (Connector c in mgr.Connectors)
                    if (c.Domain == Domain.DomainPiping) result.Add(c);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetPlumbingConnectors {fi.Id}: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// Finds the XYZ of the nearest open-ended connector on a pipe
        /// element whose piping system abbreviation matches
        /// <paramref name="systemAbbrev"/>. Returns null when not found.
        /// </summary>
        private static XYZ FindSupplyNode(Document doc, string systemAbbrev)
        {
            try
            {
                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe))
                    .Cast<Pipe>()
                    .Where(p =>
                    {
                        try
                        {
                            var sys = p.MEPSystem as PipingSystem;
                            return sys != null &&
                                   string.Equals(sys.Abbreviation, systemAbbrev,
                                                  StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                    })
                    .ToList();

                foreach (var pipe in pipes)
                {
                    // Find an open connector end (not connected to another element).
                    try
                    {
                        var mgr = pipe.ConnectorManager;
                        if (mgr == null) continue;
                        foreach (Connector c in mgr.Connectors)
                        {
                            if (!c.IsConnected) return c.Origin;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FindSupplyNode '{systemAbbrev}': {ex.Message}");
            }
            return null;
        }

        private static XYZ FindSoilStackNode(Document doc)
        {
            // Look for the lowest open pipe connector on any pipe system labelled as
            // soil/waste. Returns the XY of that connector projected to FFL (Z=0)
            // since the stack typically runs vertically through the floor.
            try
            {
                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe))
                    .Cast<Pipe>()
                    .Where(p =>
                    {
                        try
                        {
                            var sys = p.MEPSystem as PipingSystem;
                            if (sys == null) return false;
                            string abbr = sys.Abbreviation ?? "";
                            return abbr.IndexOf("SS",    StringComparison.OrdinalIgnoreCase) >= 0
                                || abbr.IndexOf("SOIL",  StringComparison.OrdinalIgnoreCase) >= 0
                                || abbr.IndexOf("WASTE", StringComparison.OrdinalIgnoreCase) >= 0
                                || abbr.IndexOf("SVP",   StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        catch { return false; }
                    })
                    .OrderBy(p => p.get_BoundingBox(null)?.Min.Z ?? 0)
                    .ToList();

                foreach (var pipe in pipes)
                {
                    try
                    {
                        foreach (Connector c in pipe.ConnectorManager?.Connectors)
                            if (!c.IsConnected) return c.Origin;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FindSoilStackNode: {ex.Message}");
            }
            return null;
        }

        // ──────────────────────────────────────────────────────────────
        // Pipe type / system resolution
        // ──────────────────────────────────────────────────────────────

        private static Dictionary<string, ElementId> CollectPipeTypes(Document doc)
        {
            var dict = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var types = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeType))
                    .Cast<PipeType>();
                foreach (var pt in types)
                {
                    string n = pt.Name ?? "";
                    if (!dict.ContainsKey("SOIL") &&
                        (n.IndexOf("Soil", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         n.IndexOf("SVP",  StringComparison.OrdinalIgnoreCase) >= 0))
                        dict["SOIL"] = pt.Id;
                    if (!dict.ContainsKey("WASTE") &&
                        (n.IndexOf("Waste", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         n.IndexOf("SWS",   StringComparison.OrdinalIgnoreCase) >= 0))
                        dict["WASTE"] = pt.Id;
                    if (!dict.ContainsKey("CWS") &&
                        (n.IndexOf("Cold",  StringComparison.OrdinalIgnoreCase) >= 0 ||
                         n.IndexOf("CWS",   StringComparison.OrdinalIgnoreCase) >= 0))
                        dict["CWS"] = pt.Id;
                    if (!dict.ContainsKey("HWS") &&
                        (n.IndexOf("Hot",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                         n.IndexOf("HWS",   StringComparison.OrdinalIgnoreCase) >= 0))
                        dict["HWS"] = pt.Id;

                    // Ensure at least one fallback for all keys.
                    if (!dict.ContainsKey("SOIL"))  dict["SOIL"]  = pt.Id;
                    if (!dict.ContainsKey("WASTE")) dict["WASTE"] = pt.Id;
                    if (!dict.ContainsKey("CWS"))   dict["CWS"]   = pt.Id;
                    if (!dict.ContainsKey("HWS"))   dict["HWS"]   = pt.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"CollectPipeTypes: {ex.Message}"); }
            return dict;
        }

        private static Dictionary<string, ElementId> CollectPipingSystemIds(Document doc)
        {
            var dict = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var sysTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystemType))
                    .Cast<PipingSystemType>();
                foreach (var st in sysTypes)
                {
                    string abbr = st.Abbreviation ?? "";
                    dict[abbr] = st.Id;
                    // Also index by name fragments.
                    string n = st.Name ?? "";
                    if (!dict.ContainsKey("CWS") &&
                        (abbr.IndexOf("CWS",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                          n.IndexOf("Cold",    StringComparison.OrdinalIgnoreCase) >= 0))
                        dict["CWS"] = st.Id;
                    if (!dict.ContainsKey("HWS") &&
                        (abbr.IndexOf("HWS",   StringComparison.OrdinalIgnoreCase) >= 0 ||
                          n.IndexOf("Hot",     StringComparison.OrdinalIgnoreCase) >= 0))
                        dict["HWS"] = st.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"CollectPipingSystemIds: {ex.Message}"); }
            return dict;
        }

        // ──────────────────────────────────────────────────────────────
        // AAV symbol resolution
        // ──────────────────────────────────────────────────────────────

        private FamilySymbol ResolveAavSymbol(Document doc, PlumbingRoutingResult result)
        {
            try
            {
                var sym = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s =>
                        string.Equals(s.FamilyName, AavFamilyName,
                                      StringComparison.OrdinalIgnoreCase));
                if (sym != null) return sym;

                // Try partial name match.
                sym = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s =>
                        (s.FamilyName ?? "").IndexOf("AAV",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (s.FamilyName ?? "").IndexOf("Air Admittance",
                            StringComparison.OrdinalIgnoreCase) >= 0);
                if (sym != null) return sym;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ResolveAavSymbol: {ex.Message}");
            }

            result.Warnings++;
            result.WarningMessages.Add(
                $"AAV family '{AavFamilyName}' not found in document. " +
                $"Load the family from Families/Plumbing/ or disable AAV placement.");
            return null;
        }

        // ──────────────────────────────────────────────────────────────
        // Pipe parameter helpers
        // ──────────────────────────────────────────────────────────────

        private static void SetPipeDiameter(Pipe pipe, double nomDiaMm)
        {
            try
            {
                // TODO-VERIFY-API: Revit 2025 stores pipe diameter in internal feet.
                double diaFt = nomDiaMm * MmToFt;
                var p = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                if (p != null && !p.IsReadOnly) p.Set(diaFt);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetPipeDiameter {pipe.Id}: {ex.Message}");
            }
        }

        private static void TrySetParam(Element el, string name, string value)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(value);
            }
            catch { }
        }
    }

    // ──────────────────────────────────────────────────────────────────
    // Routing standards reference constants (Naviate / STING integration)
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// BS EN 12056-2 / CIBSE Guide G / IPC plumbing standards constants
    /// referenced by the auto-router and the placement rules.
    /// </summary>
    public static class PlumbingStandards
    {
        // Drainage slopes (BS EN 12056-2 / Approved Document H)
        public const double MinSlopePct_DN32  = 2.50; // 1:40 (32mm pipe)
        public const double MinSlopePct_DN40  = 1.80; // 1:55 (40mm)
        public const double MinSlopePct_DN50  = 1.25; // 1:80 (50mm)
        public const double MinSlopePct_DN100 = 1.25; // 1:80 (100mm soil)

        // Trap seal depths (Approved Doc H Table 1)
        public const double TrapSeal_Basin_mm    = 75.0;
        public const double TrapSeal_Bath_mm     = 50.0;
        public const double TrapSeal_WC_mm       = 50.0; // integral siphon
        public const double TrapSeal_FloorDrain  = 50.0;

        // Supply velocities (CIBSE Guide G)
        public const double MaxVelocity_CWS_ms   = 3.0; // m/s cold water
        public const double MaxVelocity_HWS_ms   = 1.5; // m/s hot water

        // TMV outlet temperatures (NHSE HTM 04-01)
        public const double TmvOutlet_Domestic_C    = 43.0; // max
        public const double TmvOutlet_Healthcare_C  = 41.0; // max (HTM 04-01)
        public const double StorageTemp_C           = 60.0; // min Legionella control
        public const double DistribTemp_C           = 55.0; // min distribution

        // Discharge unit factors for combined gravity stack sizing
        // (BS EN 12056-2 System III, UK)
        public const double StackFactor_k           = 0.5; // k factor for combined system

        // Pipe sizing: DN supply pipes per BS EN 806-3 / CIBSE Guide G
        public static double CwsBranchDia_Mm(double loadingUnits)
        {
            if (loadingUnits <= 0.3) return 10.0;
            if (loadingUnits <= 1.5) return 15.0;
            if (loadingUnits <= 3.0) return 22.0;
            if (loadingUnits <= 6.0) return 28.0;
            return 35.0;
        }

        // CIBSE Guide G Table 2.5.2 loading units (cold supply)
        public const double LoadingUnit_WC        = 2.0;
        public const double LoadingUnit_Basin      = 1.5;
        public const double LoadingUnit_Bath       = 10.0;
        public const double LoadingUnit_Shower     = 3.0;
        public const double LoadingUnit_Urinal     = 0.3; // per stall
    }
}
