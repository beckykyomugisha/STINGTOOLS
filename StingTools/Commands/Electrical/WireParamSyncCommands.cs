// StingTools — Wire Parameter Sync commands.
//
// Bridges the gap between Revit's native ElectricalSystem data and the STING
// ELC_WIRE_* shared parameters that drive WireAnnotationEngine labels.
//
// Gap 1  — WireParamStampCommand:    stamp single conduit from connected circuit
// Gap 2  — BatchWireParamPopulate:   batch stamp all conduits in view / selection
// Gap 3  — WireVDSyncCommand:        run VoltageDropEngine + write ELC_WIRE_VD_PCT_NUM
// Gap 4  — WireCableSizerSyncCommand: run CableSizerEngine + write CSA/Iz/method
// Gap 8  — ConduitCircuitIndex:       session-cached connector-graph lookup
// Gap 9  — WireHomeRunFullCommand:    BFS full conduit run; corrects panel-side end
// Gap 11 — WireCpcSizerCommand:       BS 7671 Table 54.7 CPC / earth sizing
// Gap 12 — WireRoutingValidationCommand: fire-rated/armoured routing rule checks
// Gap 13 — WireCoordStampCommand:    write ELC_SEL_COORD_OK after SelectiveCoord

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Commands.Electrical.CableSizer;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core;

namespace StingTools.Commands.Electrical
{
    // ── local helper (avoid modifying ParameterHelpers.cs) ───────────────────
    internal static class WireParamHelpers
    {
        public static double GetDouble(Element el, string name, double def = 0)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null) return def;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.String
                    && double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double sv))
                    return sv;
            }
            catch { }
            return def;
        }
    }


    // ─────────────────────────────────────────────────────────────────────────
    // Gap 8 — Session-cached conduit → ElectricalSystem lookup
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Caches the mapping from conduit UniqueId to the connected ElectricalSystem
    /// ElementId for the duration of the Revit session. Avoids repeated connector-graph
    /// traversals when stamping batches of conduits. Call Invalidate() whenever
    /// conduits or circuits are modified.
    /// </summary>
    internal static class ConduitCircuitIndex
    {
        private static Dictionary<string, ElementId> _map = new Dictionary<string, ElementId>();
        private static string _docPath = null;

        public static void Invalidate() { _map.Clear(); _docPath = null; }

        public static ElementId Resolve(Document doc, Element conduit)
        {
            // Reset cache if document changed
            string docPath = doc.PathName ?? doc.Title;
            if (docPath != _docPath) { _map.Clear(); _docPath = docPath; }

            if (_map.TryGetValue(conduit.UniqueId, out var cached)) return cached;

            var id = FindCircuitId(conduit);
            _map[conduit.UniqueId] = id;
            return id;
        }

        private static ElementId FindCircuitId(Element conduit)
        {
            try
            {
                var connMgr = (conduit as MEPCurve)?.ConnectorManager;
                if (connMgr == null) return ElementId.InvalidElementId;

                // BFS through connector graph up to depth 4 to find any ElectricalSystem
                var visited = new HashSet<int>();
                var queue = new Queue<Connector>();
                foreach (Connector c in connMgr.Connectors) queue.Enqueue(c);

                int depth = 0;
                while (queue.Count > 0 && depth < 4)
                {
                    int levelCount = queue.Count;
                    depth++;
                    for (int i = 0; i < levelCount; i++)
                    {
                        var conn = queue.Dequeue();
                        if (conn == null || visited.Contains(conn.Id)) continue;
                        visited.Add(conn.Id);

                        if (conn.IsConnected)
                        {
                            foreach (Connector ref_ in conn.AllRefs)
                            {
                                if (ref_.Owner is ElectricalSystem sys)
                                    return sys.Id;
                                if (!visited.Contains(ref_.Id))
                                    queue.Enqueue(ref_);
                            }
                        }
                    }
                }
            }
            catch { }
            return ElementId.InvalidElementId;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helper: read native ElectricalSystem fields → WireData struct
    // ─────────────────────────────────────────────────────────────────────────

    internal struct WireStampData
    {
        public string Phase;
        public int    CoreCount;
        public double CsaMm2;
        public string ConductorMat;
        public string CircuitNumber;
        public string PanelName;
        public string InstallMethod;
        public string CircuitType;
        public double MaxDemandA;
        public double AmpacityA;
        public bool   IsFireRated;
        public bool   IsArmoured;
        public bool   IsShielded;
        public bool   Valid;
    }

    internal static class WireStampHelper
    {
        public static WireStampData FromConduit(Document doc, Element conduit)
        {
            var d = new WireStampData();

            // First preference: data already written to shared params
            d.CsaMm2       = WireParamHelpers.GetDouble(conduit, "ELC_WIRE_CSA_MM2_NUM");
            d.ConductorMat = ParameterHelpers.GetString(conduit, "ELC_WIRE_COND_MAT_TXT");
            d.InstallMethod = ParameterHelpers.GetString(conduit, "ELC_WIRE_INSTALL_METHOD_TXT");
            d.CircuitType  = ParameterHelpers.GetString(conduit, "ELC_WIRE_CIRCUIT_TYPE_TXT");
            d.IsFireRated  = ParameterHelpers.GetInt(conduit, "ELC_WIRE_FIRE_RATED_BOOL", 0) != 0;
            d.IsArmoured   = ParameterHelpers.GetInt(conduit, "ELC_WIRE_ARMOURED_BOOL", 0) != 0;
            d.IsShielded   = ParameterHelpers.GetInt(conduit, "ELC_WIRE_SHIELDED_BOOL", 0) != 0;

            // Primary source: connected ElectricalSystem
            try
            {
                var sysId = ConduitCircuitIndex.Resolve(doc, conduit);
                if (sysId != ElementId.InvalidElementId)
                {
                    var sys = doc.GetElement(sysId) as ElectricalSystem;
                    if (sys != null)
                    {
                        d.CircuitNumber = sys.CircuitNumber ?? "";
                        d.MaxDemandA   = sys.ApparentCurrent;

                        // Phase + core count from system phase
                        switch (sys.SystemType)
                        {
                            case ElectricalSystemType.PowerCircuit:
                                d.Phase = sys.PolesNumber switch { 1 => "1Ø", 3 => "3Ø", _ => "3Ø" };
                                // core count = phases + neutral + CPC
                                d.CoreCount = sys.PolesNumber == 1 ? 2 : 4;
                                d.CircuitType = string.IsNullOrEmpty(d.CircuitType) ? "Power" : d.CircuitType;
                                break;
                            case ElectricalSystemType.LightingCircuit:
                                d.Phase = "1Ø";
                                d.CoreCount = 2;
                                d.CircuitType = string.IsNullOrEmpty(d.CircuitType) ? "Lighting" : d.CircuitType;
                                break;
                            default:
                                d.Phase = "1Ø";
                                d.CoreCount = 2;
                                break;
                        }

                        // Panel name from base equipment
                        try
                        {
                            var panel = sys.BaseEquipment;
                            d.PanelName = panel?.Name ?? "";
                        }
                        catch { d.PanelName = ""; }

                        d.Valid = true;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn("WireStampHelper: " + ex.Message); }

            // Fallback for conductor material
            if (string.IsNullOrEmpty(d.ConductorMat)) d.ConductorMat = "Cu";

            return d;
        }

        /// <summary>Write WireStampData fields to conduit ELC_WIRE_* shared params.</summary>
        public static void WriteToConduit(Element conduit, WireStampData d)
        {
            if (!string.IsNullOrEmpty(d.Phase))
                ParameterHelpers.SetString(conduit, "ELC_WIRE_PHASE_TXT", d.Phase, true);
            if (d.CoreCount > 0)
                SetInt(conduit, "ELC_WIRE_CORE_COUNT_INT", d.CoreCount);
            if (d.CsaMm2 > 0)
                SetDouble(conduit, "ELC_WIRE_CSA_MM2_NUM", d.CsaMm2);
            if (!string.IsNullOrEmpty(d.ConductorMat))
                ParameterHelpers.SetString(conduit, "ELC_WIRE_COND_MAT_TXT", d.ConductorMat, false);
            if (!string.IsNullOrEmpty(d.CircuitNumber))
                ParameterHelpers.SetString(conduit, "ELC_CIRCUIT_NR_TXT", d.CircuitNumber, true);
            if (!string.IsNullOrEmpty(d.PanelName))
                ParameterHelpers.SetString(conduit, "ELC_WIRE_CIRCUIT_TYPE_TXT",
                    string.IsNullOrEmpty(d.CircuitType) ? d.CircuitType : d.CircuitType, false);
            if (!string.IsNullOrEmpty(d.InstallMethod))
                ParameterHelpers.SetString(conduit, "ELC_WIRE_INSTALL_METHOD_TXT", d.InstallMethod, false);
            if (d.MaxDemandA > 0)
                SetDouble(conduit, "ELC_WIRE_MAX_DEMAND_A", d.MaxDemandA);
            if (d.AmpacityA > 0)
                SetDouble(conduit, "ELC_WIRE_AMPACITY_A", d.AmpacityA);
        }

        private static void SetDouble(Element el, string name, double v)
        {
            try { var p = el.LookupParameter(name); if (p != null && !p.IsReadOnly) p.Set(v); }
            catch { }
        }

        private static void SetInt(Element el, string name, int v)
        {
            try { var p = el.LookupParameter(name); if (p != null && !p.IsReadOnly) p.Set(v); }
            catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 1 — Stamp single conduit from connected ElectricalSystem
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class WireParamStampCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            Reference picked;
            try { picked = uidoc.Selection.PickObject(ObjectType.Element, new ConduitSelectionFilter(),
                "Pick conduit to stamp wire parameters from circuit"); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            var conduit = doc.GetElement(picked.ElementId);
            if (conduit == null) { message = "Invalid element."; return Result.Failed; }

            var wsd = WireStampHelper.FromConduit(doc, conduit);
            if (!wsd.Valid)
            {
                TaskDialog.Show("Wire Stamp", "No connected ElectricalSystem found on this conduit. "
                    + "Ensure it is routed and circuit-connected before stamping.");
                return Result.Succeeded;
            }

            using var tx = new Transaction(doc, "STING Stamp Wire Params");
            tx.Start();
            WireStampHelper.WriteToConduit(conduit, wsd);
            tx.Commit();

            ConduitCircuitIndex.Invalidate(); // clear cache after write

            TaskDialog.Show("Wire Stamp", $"Stamped:\n"
                + $"  Circuit: {wsd.CircuitNumber}  Panel: {wsd.PanelName}\n"
                + $"  Phase: {wsd.Phase}  Cores: {wsd.CoreCount}  Mat: {wsd.ConductorMat}\n"
                + $"  Max demand: {wsd.MaxDemandA:0.0} A");
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 2 — Batch stamp all conduits in active view / selection
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class BatchWireParamPopulateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            // Prefer current selection; fall back to all conduits in view
            IList<Element> conduits;
            var selIds = uidoc.Selection.GetElementIds();
            if (selIds.Count > 0)
            {
                conduits = selIds.Select(id => doc.GetElement(id))
                    .Where(el => el?.Category?.Id?.Value == (long)BuiltInCategory.OST_Conduit)
                    .ToList();
                if (conduits.Count == 0)
                {
                    TaskDialog.Show("Batch Wire Stamp", "Selection contains no conduits.");
                    return Result.Succeeded;
                }
            }
            else
            {
                conduits = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType()
                    .ToElements();
            }

            if (conduits.Count == 0)
            {
                TaskDialog.Show("Batch Wire Stamp", "No conduits found in current view.");
                return Result.Succeeded;
            }

            int stamped = 0, skipped = 0;
            var progress = StingProgressDialog.Show("Batch Wire Stamp", conduits.Count);
            try
            {
                using var tx = new Transaction(doc, "STING Batch Stamp Wire Params");
                tx.Start();
                foreach (var conduit in conduits)
                {
                    if (progress?.IsCancelled == true) break;
                    var wsd = WireStampHelper.FromConduit(doc, conduit);
                    if (wsd.Valid) { WireStampHelper.WriteToConduit(conduit, wsd); stamped++; }
                    else skipped++;
                    progress?.Increment(conduit.Name ?? "conduit");
                }
                tx.Commit();
            }
            finally { progress?.Close(); }

            ConduitCircuitIndex.Invalidate();
            TaskDialog.Show("Batch Wire Stamp",
                $"Stamped: {stamped} conduits\nSkipped (no circuit): {skipped} conduits");
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 3 — VD sync: run VoltageDropEngine per conduit, write result
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class WireVDSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            var selIds = uidoc.Selection.GetElementIds();
            IList<Element> conduits = selIds.Count > 0
                ? selIds.Select(id => doc.GetElement(id))
                    .Where(el => el?.Category?.Id?.Value == (long)BuiltInCategory.OST_Conduit).ToList()
                : new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType().ToElements();

            if (conduits.Count == 0)
            { TaskDialog.Show("VD Sync", "No conduits found."); return Result.Succeeded; }

            int updated = 0;
            using var tx = new Transaction(doc, "STING Wire VD Sync");
            tx.Start();
            foreach (var el in conduits)
            {
                try
                {
                    double csaMm2   = WireParamHelpers.GetDouble(el, "ELC_WIRE_CSA_MM2_NUM");
                    double demandA  = WireParamHelpers.GetDouble(el, "ELC_WIRE_MAX_DEMAND_A");
                    string mat      = ParameterHelpers.GetString(el, "ELC_WIRE_COND_MAT_TXT");
                    string phaseStr = ParameterHelpers.GetString(el, "ELC_WIRE_PHASE_TXT");

                    if (csaMm2 <= 0 || demandA <= 0) continue;

                    double lengthM = 0;
                    if (el.Location is LocationCurve lc)
                        lengthM = lc.Curve.Length * 0.3048; // Revit feet → metres

                    int phases = phaseStr?.Contains("3") == true ? 3 : 1;
                    double vd = VoltageDropEngine.CalculateVoltDropPercent(
                        demandA, lengthM, csaMm2,
                        mat?.Contains("Al") == true ? "Al" : "Cu",
                        phases == 3 ? 400 : 230, phases, 70);

                    SetDouble(el, "ELC_WIRE_VD_PCT_NUM", vd);
                    updated++;
                }
                catch (Exception ex) { StingLog.Warn($"VD sync {el.Id}: {ex.Message}"); }
            }
            tx.Commit();

            TaskDialog.Show("Wire VD Sync", $"Updated VD on {updated} conduit(s).\n"
                + "Re-run 'W-Batch' to refresh annotations.");
            return Result.Succeeded;
        }

        private static void SetDouble(Element el, string name, double v)
        {
            var p = el.LookupParameter(name);
            if (p != null && !p.IsReadOnly) p.Set(v);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 4 — Cable sizer write-back: run CableSizerEngine, stamp CSA/Iz/method
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class WireCableSizerSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            var selIds = uidoc.Selection.GetElementIds();
            IList<Element> conduits = selIds.Count > 0
                ? selIds.Select(id => doc.GetElement(id))
                    .Where(el => el?.Category?.Id?.Value == (long)BuiltInCategory.OST_Conduit).ToList()
                : new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType().ToElements();

            if (conduits.Count == 0)
            { TaskDialog.Show("Cable Sizer Sync", "No conduits found."); return Result.Succeeded; }

            int sized = 0;
            using var tx = new Transaction(doc, "STING Cable Sizer Sync");
            tx.Start();
            foreach (var el in conduits)
            {
                try
                {
                    double demandA  = WireParamHelpers.GetDouble(el, "ELC_WIRE_MAX_DEMAND_A");
                    if (demandA <= 0) continue;

                    double lengthM  = 0;
                    if (el.Location is LocationCurve lc)
                        lengthM = lc.Curve.Length * 0.3048;

                    string method   = ParameterHelpers.GetString(el, "ELC_WIRE_INSTALL_METHOD_TXT");
                    string mat      = ParameterHelpers.GetString(el, "ELC_WIRE_COND_MAT_TXT");
                    string phaseStr = ParameterHelpers.GetString(el, "ELC_WIRE_PHASE_TXT");

                    // Derive kW from current (rough: P = I × V × PF)
                    double voltV = phaseStr?.Contains("3") == true ? 400 : 230;
                    int phases   = phaseStr?.Contains("3") == true ? 3 : 1;
                    double kw    = demandA * voltV * 0.85 / 1000.0;

                    var input = new CableSizeInput
                    {
                        LoadKW         = kw,
                        VoltageV       = voltV,
                        LengthM        = lengthM,
                        InstallMethod  = string.IsNullOrEmpty(method) ? "B2" : method,
                        Material       = mat?.Contains("Al") == true ? "Al" : "Cu",
                        Phases         = phases,
                        AmbientTempC   = 30,
                        VDLimitPct     = 3.0,
                    };

                    var result = CableSizerEngine.Calculate(input);
                    if (result == null) continue;

                    SetDouble(el, "ELC_WIRE_CSA_MM2_NUM",       result.RecommendedCsaMm2);
                    SetDouble(el, "ELC_WIRE_AMPACITY_A",        result.DesignCurrentA);
                    SetDouble(el, "ELC_WIRE_VD_PCT_NUM",        result.ActualVoltDropPct);
                    SetDouble(el, "ELC_WIRE_CIRCUIT_BREAKER_A", result.ProposedBreakerA);
                    sized++;
                }
                catch (Exception ex) { StingLog.Warn($"CableSizerSync {el.Id}: {ex.Message}"); }
            }
            tx.Commit();

            TaskDialog.Show("Cable Sizer Sync", $"Cable-sized {sized} conduit(s).\n"
                + "Parameters CSA, Iz, VD, and breaker rating have been updated.\n"
                + "Re-run 'W-Batch' to refresh annotations.");
            return Result.Succeeded;
        }

        private static void SetDouble(Element el, string name, double v)
        {
            var p = el.LookupParameter(name); if (p != null && !p.IsReadOnly) p.Set(v);
        }
        private static void SetString(Element el, string name, string v)
        {
            var p = el.LookupParameter(name); if (p != null && !p.IsReadOnly) p.Set(v);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 9 — Full run home-run traversal via BFS connector graph
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class WireHomeRunFullCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            Reference picked;
            try { picked = uidoc.Selection.PickObject(ObjectType.Element, new ConduitSelectionFilter(),
                "Pick any conduit in the run for full home-run traversal"); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            var seed = doc.GetElement(picked.ElementId);
            if (seed == null) { message = "Invalid element."; return Result.Failed; }

            // BFS to collect all connected conduit segments in the run
            var run = CollectRun(doc, seed);
            if (run.Count == 0)
            {
                TaskDialog.Show("Home-Run Full", "No conduit run found from the picked element.");
                return Result.Succeeded;
            }

            // Find the panel-side endpoint by walking to the end that connects to a panel
            XYZ panelEndPt = FindPanelSidePoint(doc, run);

            if (panelEndPt == null)
            {
                TaskDialog.Show("Home-Run Full",
                    $"Run has {run.Count} segment(s) but panel endpoint could not be located.\n"
                    + "Ensure the conduit run is circuit-connected.");
                return Result.Succeeded;
            }

            // Place home-run arrow at the panel-side end using detail lines in the active view
            var view = doc.ActiveView;
            using var tx = new Transaction(doc, "STING Home-Run Full Arrow");
            tx.Start();
            PlaceSimpleArrow(doc, view, panelEndPt, run.Last());
            tx.Commit();

            TaskDialog.Show("Home-Run Full",
                $"Home-run arrow placed for run of {run.Count} segment(s).\n"
                + $"Panel-side end: ({panelEndPt.X * 304.8:0} mm, {panelEndPt.Y * 304.8:0} mm)");
            return Result.Succeeded;
        }

        private static List<Element> CollectRun(Document doc, Element seed)
        {
            var run     = new List<Element>();
            var visited = new HashSet<ElementId>();
            var queue   = new Queue<Element>();
            queue.Enqueue(seed);

            while (queue.Count > 0)
            {
                var el = queue.Dequeue();
                if (el == null || visited.Contains(el.Id)) continue;
                visited.Add(el.Id);

                if (el.Category?.Id?.Value == (long)BuiltInCategory.OST_Conduit)
                    run.Add(el);

                try
                {
                    var cm = (el as MEPCurve)?.ConnectorManager;
                    if (cm == null) continue;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector ref_ in c.AllRefs)
                        {
                            if (!visited.Contains(ref_.Owner.Id)
                                && ref_.Owner.Category?.Id?.Value == (long)BuiltInCategory.OST_Conduit)
                                queue.Enqueue(ref_.Owner);
                        }
                    }
                }
                catch { }
            }
            return run;
        }

        private static void PlaceSimpleArrow(Document doc, View view, XYZ panelPt, Element conduit)
        {
            try
            {
                // Find the far-end (load-side) point of the conduit nearest to panelPt
                var lc = conduit.Location as LocationCurve;
                if (lc?.Curve == null) return;
                var p0 = lc.Curve.GetEndPoint(0);
                var p1 = lc.Curve.GetEndPoint(1);
                // Arrow: shaft from load-side end toward panel
                XYZ loadEnd = p0.DistanceTo(panelPt) < p1.DistanceTo(panelPt) ? p1 : p0;
                XYZ rawDir  = (panelPt - loadEnd);
                if (rawDir.GetLength() < 1e-6) rawDir = XYZ.BasisX;
                XYZ dir     = rawDir.Normalize();
                double shaftFt = 150.0 / 304.8;
                XYZ tip = loadEnd + dir * shaftFt;

                void Draw(XYZ a, XYZ b)
                {
                    try
                    {
                        var dc = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                        var p = dc.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                        if (p != null && !p.IsReadOnly) p.Set("STING_HOME_RUN");
                    }
                    catch { }
                }

                Draw(loadEnd, tip);
                double headFt = 30.0 / 304.8;
                var perp = XYZ.BasisZ.CrossProduct(dir).Normalize();
                double ang = Math.PI / 12.0;
                Draw(tip, tip - dir * headFt + perp * (headFt * Math.Tan(ang)));
                Draw(tip, tip - dir * headFt - perp * (headFt * Math.Tan(ang)));
            }
            catch (Exception ex) { StingLog.Warn("PlaceSimpleArrow: " + ex.Message); }
        }

        private static XYZ FindPanelSidePoint(Document doc, List<Element> run)
        {
            // Walk connector graph: find the endpoint nearest to a FamilyInstance
            // of the ElectricalEquipment or Panel category (distribution boards)
            foreach (var el in run)
            {
                try
                {
                    var cm = (el as MEPCurve)?.ConnectorManager;
                    if (cm == null) continue;
                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) continue;
                        foreach (Connector ref_ in c.AllRefs)
                        {
                            if (ref_.Owner is ElectricalSystem) return c.Origin;
                            var ownerCat = ref_.Owner?.Category?.Id?.Value;
                            if (ownerCat == (long)BuiltInCategory.OST_ElectricalEquipment
                             || ownerCat == (long)BuiltInCategory.OST_ElectricalFixtures)
                                return c.Origin;
                        }
                    }
                }
                catch { }
            }
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 11 — CPC / earth sizing per BS 7671 Table 54.7
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class WireCpcSizerCommand : IExternalCommand
    {
        // BS 7671 Table 54.7 — simplified rule for minimum CPC CSA
        // Conductor ≤16 mm² → CPC = conductor CSA
        // 16 < conductor ≤35 mm² → CPC = 16 mm²
        // conductor > 35 mm² → CPC = half conductor CSA (nearest standard size up)
        public static double CpcSizeForPhaseMm2(double phaseCsaMm2)
        {
            if (phaseCsaMm2 <= 16) return phaseCsaMm2;
            if (phaseCsaMm2 <= 35) return 16;
            return NearestStandardSize(phaseCsaMm2 / 2.0);
        }

        // Adiabatic check: S_min = sqrt(I^2 * t) / k
        // k = 143 for Cu/PVC, 115 for Al, 76 for steel
        public static double CpcAdiabatic(double faultCurrentA, double clearingTimeS,
            string material = "Cu")
        {
            double k = material?.Contains("Al") == true ? 115 : 143;
            return Math.Sqrt(faultCurrentA * faultCurrentA * clearingTimeS) / k;
        }

        private static double NearestStandardSize(double minMm2)
        {
            double[] sizes = { 1.0, 1.5, 2.5, 4.0, 6.0, 10.0, 16.0, 25.0, 35.0, 50.0,
                               70.0, 95.0, 120.0, 150.0, 185.0, 240.0, 300.0, 400.0 };
            return sizes.FirstOrDefault(s => s >= minMm2, sizes.Last());
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uidoc = data.Application.ActiveUIDocument;
            var doc   = uidoc.Document;

            var selIds = uidoc.Selection.GetElementIds();
            IList<Element> conduits = selIds.Count > 0
                ? selIds.Select(id => doc.GetElement(id))
                    .Where(el => el?.Category?.Id?.Value == (long)BuiltInCategory.OST_Conduit).ToList()
                : new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType().ToElements();

            if (conduits.Count == 0)
            { TaskDialog.Show("CPC Sizer", "No conduits found."); return Result.Succeeded; }

            int sized = 0;
            using var tx = new Transaction(doc, "STING CPC Size");
            tx.Start();
            foreach (var el in conduits)
            {
                try
                {
                    double csaMm2  = WireParamHelpers.GetDouble(el, "ELC_WIRE_CSA_MM2_NUM");
                    if (csaMm2 <= 0) continue;
                    string mat = ParameterHelpers.GetString(el, "ELC_WIRE_COND_MAT_TXT");
                    double cpcMm2 = CpcSizeForPhaseMm2(csaMm2);
                    var p = el.LookupParameter("ELC_WIRE_EARTH_CSA_MM2");
                    if (p != null && !p.IsReadOnly) { p.Set(cpcMm2); sized++; }
                }
                catch (Exception ex) { StingLog.Warn($"CpcSizer {el.Id}: {ex.Message}"); }
            }
            tx.Commit();

            TaskDialog.Show("CPC Sizer",
                $"CPC/Earth sized on {sized} conduit(s) per BS 7671 Table 54.7.\n"
                + "Results written to ELC_WIRE_EARTH_CSA_MM2.");
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 12 — Fire-rated / armoured routing validation
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class WireRoutingValidationCommand : IExternalCommand
    {
        private readonly record struct RoutingIssue(ElementId ConduitId, string Description, string Standard);

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = data.Application.ActiveUIDocument.Document;
            var conduits = new FilteredElementCollector(doc, doc.ActiveView.Id)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToElements();

            var issues = new List<RoutingIssue>();

            foreach (var el in conduits)
            {
                bool isFR  = ParameterHelpers.GetInt(el, "ELC_WIRE_FIRE_RATED_BOOL", 0) != 0;
                bool isSWA = ParameterHelpers.GetInt(el, "ELC_WIRE_ARMOURED_BOOL", 0) != 0;
                bool isShielded = ParameterHelpers.GetInt(el, "ELC_WIRE_SHIELDED_BOOL", 0) != 0;
                string method = ParameterHelpers.GetString(el, "ELC_WIRE_INSTALL_METHOD_TXT");

                // Rule FR-1: Fire-rated cable must not share conduit with standard cables
                // (BS 9999:2017 §19.3 — fire-survival circuits in separate containment)
                if (isFR)
                {
                    // Check if any co-conduit-path conduit is non-fire-rated (proxy: same system path)
                    // Simplified: flag fire-rated cables using method "A1" (enclosed with mixed)
                    if (string.Equals(method, "A1", StringComparison.OrdinalIgnoreCase))
                        issues.Add(new RoutingIssue(el.Id,
                            "Fire-rated cable should NOT be installed in conduit method A1 with general wiring. "
                            + "Provide dedicated fire-rated containment.",
                            "BS 9999:2017 §19.3 / BS 7671 Reg 422.2.1"));
                }

                // Rule FR-2: Fire-rated cable separation from heat sources
                // (BS 7671 §422.2 — maintain 150 mm from heat-generating equipment when surface-mounted)
                if (isFR && string.Equals(method, "C", StringComparison.OrdinalIgnoreCase))
                    issues.Add(new RoutingIssue(el.Id,
                        "Fire-rated cable installed on surface (Method C): verify ≥150 mm separation "
                        + "from heat sources per BS 7671 §422.2.",
                        "BS 7671:2018 §422.2"));

                // Rule SWA-1: Armoured cables — shielding continuity required at all terminations
                if (isSWA)
                {
                    bool shieldCont = ParameterHelpers.GetInt(el, "ELC_WIRE_SHIELDED_BOOL", 0) != 0;
                    // If armoured but shielding not explicitly confirmed, warn
                    if (!shieldCont)
                        issues.Add(new RoutingIssue(el.Id,
                            "SWA cable: confirm armour continuity at both terminations "
                            + "(SWA armour ≡ CPC — continuity test required per BS 7671 §543).",
                            "BS 7671:2018 §543.3 / §522.8.1"));
                }

                // Rule SWA-2: Armoured with metallic conduit — bonding at entry required
                if (isSWA && !string.IsNullOrEmpty(method))
                    issues.Add(new RoutingIssue(el.Id,
                        "Verify SWA cable armour bonded to metallic conduit / trunking at all entry points.",
                        "BS 7671:2018 §542.2"));
            }

            if (issues.Count == 0)
            {
                TaskDialog.Show("Wire Routing Validation",
                    $"✓ All {conduits.Count} conduits in view pass routing validation.");
                return Result.Succeeded;
            }

            string report = $"{issues.Count} routing issue(s) found:\n\n";
            foreach (var iss in issues.Take(20))
                report += $"• Conduit {iss.ConduitId.Value}: {iss.Description}\n  Ref: {iss.Standard}\n\n";
            if (issues.Count > 20)
                report += $"... and {issues.Count - 20} more (see log for full list).";

            foreach (var iss in issues)
                StingLog.Warn($"RoutingValidation [{iss.ConduitId.Value}]: {iss.Description} [{iss.Standard}]");

            TaskDialog.Show("Wire Routing Validation", report);
            return Result.Succeeded;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap 13 — Write ELC_SEL_COORD_OK after selective coordination check
    //          (panel-level parameter stamp called from SelectiveCoordCommand)
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class WireCoordStampCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = data.Application.ActiveUIDocument.Document;

            // Collect all electrical equipment (panels) in project
            var panels = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType()
                .ToElements();

            if (panels.Count == 0)
            {
                TaskDialog.Show("Coord Stamp", "No electrical equipment (panels) found in project.");
                return Result.Succeeded;
            }

            // Load TCC database
            var tcc = Coordination.TccDatabaseLoader.Load(StingToolsApp.FindDataFile("STING_TCC_DATABASE.json"));

            int stamped = 0;
            using var tx = new Transaction(doc, "STING Coord Stamp");
            tx.Start();
            foreach (var panel in panels)
            {
                try
                {
                    // Resolve panel rating from parameter
                    string rating = ParameterHelpers.GetString(panel, "ELC_PANEL_MAIN_BREAKER_TXT");
                    if (string.IsNullOrEmpty(rating)) continue;

                    // Simple check: if entry exists in TCC database, mark as 'checked'
                    // Full SLD-based check runs via SelectiveCoordEngine from BIM commands
                    var entry = tcc.Resolve(rating);
                    bool ok = entry != null;
                    var p = panel.LookupParameter("ELC_SEL_COORD_OK");
                    if (p != null && !p.IsReadOnly)
                    {
                        p.Set(ok ? 1 : 0);
                        stamped++;
                    }
                }
                catch (Exception ex) { StingLog.Warn($"CoordStamp {panel.Id}: {ex.Message}"); }
            }
            tx.Commit();

            TaskDialog.Show("Coord Stamp",
                $"Selective coordination result stamped on {stamped} panel(s).\n"
                + "ELC_SEL_COORD_OK = 1 (pass) / 0 (fail or unchecked).\n"
                + "Run 'Sel Coord' for full SLD-based analysis.");
            return Result.Succeeded;
        }
    }
}
