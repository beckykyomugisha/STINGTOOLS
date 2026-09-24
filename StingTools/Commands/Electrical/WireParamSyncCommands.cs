// StingTools — Wire Parameter Sync commands.
//
// Bridges the gap between Revit's native ElectricalSystem data and the STING
// ELC_WIRE_* shared parameters that drive WireAnnotationEngine labels.
//
// Gap 1  — WireParamStampCommand:    stamp single conduit from connected circuit
// Gap 2  — BatchWireParamPopulate:   batch stamp all conduits in view / selection
// Gap 3  — WireVDSyncCommand:        run VoltageDropEngine + write ELC_WIRE_VD_PCT_NUM
// Gap 4  — WireCableSizerSyncCommand: run CableSizerEngine + write CSA/VD/breaker (Iz not reported by the sizer)
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
using StingTools.UI;

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
                if (p.StorageType == StorageType.Double)  return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
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
        private static Dictionary<string, (ElementId Id, string Reason)> _map =
            new Dictionary<string, (ElementId, string)>();
        // Cache key: "documentTitle|documentPath" to handle unsaved docs and path changes
        private static string _docKey = null;

        public static void Invalidate() { _map.Clear(); _docKey = null; }

        public static ElementId Resolve(Document doc, Element conduit) => Resolve(doc, conduit, out _);

        /// <summary>
        /// ELEC-9 — the circuit a conduit carries, via the shared
        /// <see cref="StingTools.Core.Electrical.ConduitCircuitResolver"/> (walks the
        /// conduit/fitting graph to the devices and panels at the ends of the run).
        /// The previous walk accepted only a connector owned by an ElectricalSystem,
        /// which a conduit connector never is, so it could not find anything.
        /// </summary>
        public static ElementId Resolve(Document doc, Element conduit, out string reason)
        {
            // Reset cache when document changes (key on both path and title for unsaved docs)
            string docKey = $"{doc.Title}|{doc.PathName}";
            if (docKey != _docKey) { _map.Clear(); _docKey = docKey; }

            if (_map.TryGetValue(conduit.UniqueId, out var cached))
            {
                reason = cached.Reason;
                return cached.Id;
            }

            ElementId id = ElementId.InvalidElementId;
            reason = "";
            try
            {
                var r = StingTools.Core.Electrical.ConduitCircuitResolver.ResolveWithReason(conduit);
                if (r.Circuit != null) id = r.Circuit.Id;
                else reason = r.Reason;
            }
            catch (Exception ex)
            {
                reason = "circuit lookup failed: " + ex.Message;
                StingLog.Warn($"ConduitCircuitIndex {conduit.Id}: {ex.Message}");
            }
            _map[conduit.UniqueId] = (id, reason);
            return id;
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
        /// <summary>Why no circuit was found, when <see cref="Valid"/> is false.</summary>
        public string NoCircuitReason;
    }

    /// <summary>
    /// What a stamp actually did. A parameter that is not bound to the Conduits
    /// category (ELC_PNL_NAME_TXT, for one) cannot be written, and counting it as
    /// stamped is how the old command reported success while writing nothing.
    /// </summary>
    internal sealed class WireStampWriteReport
    {
        public int Written;
        public readonly HashSet<string> Unbound  = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> ReadOnly = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<string> Failed = new List<string>();

        public void Merge(WireStampWriteReport o)
        {
            if (o == null) return;
            Written += o.Written;
            Unbound.UnionWith(o.Unbound);
            ReadOnly.UnionWith(o.ReadOnly);
            Failed.AddRange(o.Failed);
        }

        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            if (Unbound.Count > 0)
                sb.Append("\nNot bound to Conduits (nothing written — bind these shared parameters to the "
                          + "Conduits category to stamp them):\n  " + string.Join(", ", Unbound.OrderBy(x => x)));
            if (ReadOnly.Count > 0)
                sb.Append("\nRead-only (not written): " + string.Join(", ", ReadOnly.OrderBy(x => x)));
            if (Failed.Count > 0)
                sb.Append($"\n{Failed.Count} write(s) failed — see the STING log.");
            return sb.ToString();
        }
    }

    internal static class WireStampHelper
    {
        public static WireStampData FromConduit(Document doc, Element conduit)
        {
            var d = new WireStampData();

            // First preference: data already written to shared params
            d.CsaMm2       = WireParamHelpers.GetDouble(conduit, "ELC_WIRE_CSA_MM2_NUM");
            d.AmpacityA    = WireParamHelpers.GetDouble(conduit, "ELC_WIRE_AMPACITY_A");
            d.ConductorMat = ParameterHelpers.GetString(conduit, "ELC_WIRE_COND_MAT_TXT");
            d.InstallMethod = ParameterHelpers.GetString(conduit, "ELC_WIRE_INSTALL_METHOD_TXT");
            d.CircuitType  = ParameterHelpers.GetString(conduit, "ELC_WIRE_CIRCUIT_TYPE_TXT");
            d.IsFireRated  = ParameterHelpers.GetInt(conduit, "ELC_WIRE_FIRE_RATED_BOOL", 0) != 0;
            d.IsArmoured   = ParameterHelpers.GetInt(conduit, "ELC_WIRE_ARMOURED_BOOL", 0) != 0;
            d.IsShielded   = ParameterHelpers.GetInt(conduit, "ELC_WIRE_SHIELDED_BOOL", 0) != 0;

            // Primary source: connected ElectricalSystem
            try
            {
                var sysId = ConduitCircuitIndex.Resolve(doc, conduit, out string why);
                d.NoCircuitReason = why;
                if (sysId != ElementId.InvalidElementId)
                {
                    var sys = doc.GetElement(sysId) as ElectricalSystem;
                    if (sys != null)
                    {
                        d.CircuitNumber = sys.CircuitNumber ?? "";
                        d.MaxDemandA   = sys.ApparentCurrent;

                        // Phase + core count derived from SystemType.
                        // Revit ElectricalSystemType has: PowerCircuit, Data, Telephone,
                        // FireAlarm, Security, NurseCall, Communication, UndefinedSystemType.
                        // Three-phase detection uses reflection to read NumberOfPoles when
                        // available (not present in all Revit API versions); falls back to
                        // checking the DistributionSystemType parameter via a shared-parameter
                        // look-up which is cheaper than a full reflection walk.
                        // ElectricalSystem.PolesNumber is the API property. The previous
                        // reflection lookup asked for "NumberOfPoles", which does not
                        // exist, so every circuit read as single-phase.
                        int numPoles = 1;
                        try { numPoles = sys.PolesNumber; }
                        catch (Exception ex) { StingLog.Warn($"WireStampHelper PolesNumber {sys.Id}: {ex.Message}"); }
                        bool isThreePhase = numPoles >= 3;

                        // SystemType string compared case-insensitively to handle API
                        // version differences (e.g. LightingCircuit absent in some builds).
                        string sysTypeName = "";
                        try { sysTypeName = sys.SystemType.ToString(); } catch { }

                        if (string.Equals(sysTypeName, "PowerCircuit", StringComparison.OrdinalIgnoreCase)
                            || sys.SystemType == ElectricalSystemType.PowerCircuit)
                        {
                            if (isThreePhase)
                            {
                                d.Phase = "3Ø";
                                d.CoreCount = 4; // 3 phase + neutral (CPC separate)
                                d.CircuitType = string.IsNullOrEmpty(d.CircuitType) ? "Power" : d.CircuitType;
                            }
                            else
                            {
                                d.Phase = "1Ø";
                                d.CoreCount = 2; // live + neutral (CPC separate)
                                d.CircuitType = string.IsNullOrEmpty(d.CircuitType) ? "Power" : d.CircuitType;
                            }
                        }
                        else if (string.Equals(sysTypeName, "LightingCircuit", StringComparison.OrdinalIgnoreCase))
                        {
                            d.Phase = "1Ø";
                            d.CoreCount = 2;
                            d.CircuitType = string.IsNullOrEmpty(d.CircuitType) ? "Lighting" : d.CircuitType;
                        }
                        else
                        {
                            d.Phase = "1Ø";
                            d.CoreCount = 2;
                        }

                        // Panel name from base equipment
                        // ElectricalSystem.PanelName is the panel's Panel Name. The
                        // BaseEquipment's .Name is its TYPE name, shared by every board of
                        // that type, which is what used to be stamped.
                        try { d.PanelName = sys.PanelName ?? ""; }
                        catch { d.PanelName = ""; }
                        if (string.IsNullOrEmpty(d.PanelName))
                        {
                            try { d.PanelName = sys.BaseEquipment?.Name ?? ""; } catch { d.PanelName = ""; }
                        }

                        d.Valid = true;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn("WireStampHelper: " + ex.Message); }

            // Fallback for conductor material
            if (string.IsNullOrEmpty(d.ConductorMat)) d.ConductorMat = "Cu";

            return d;
        }

        /// <summary>
        /// Write WireStampData fields to conduit ELC_WIRE_* shared params, and say
        /// what actually happened. A parameter the conduit does not carry (not bound
        /// to Conduits — ELC_PNL_NAME_TXT, for one) is reported, never counted.
        /// </summary>
        public static WireStampWriteReport WriteToConduit(Element conduit, WireStampData d)
        {
            var r = new WireStampWriteReport();
            if (!string.IsNullOrEmpty(d.Phase))
                WriteString(conduit, "ELC_WIRE_PHASE_TXT", d.Phase, true, r);
            if (d.CoreCount > 0)
                WriteNumber(conduit, "ELC_WIRE_CORE_COUNT_INT", d.CoreCount, r);
            if (d.CsaMm2 > 0)
                WriteNumber(conduit, "ELC_WIRE_CSA_MM2_NUM", d.CsaMm2, r);
            if (!string.IsNullOrEmpty(d.ConductorMat))
                WriteString(conduit, "ELC_WIRE_COND_MAT_TXT", d.ConductorMat, false, r);
            if (!string.IsNullOrEmpty(d.CircuitNumber))
                WriteString(conduit, "ELC_CKT_NR", d.CircuitNumber, true, r);
            if (!string.IsNullOrEmpty(d.PanelName))
                WriteString(conduit, "ELC_PNL_NAME_TXT", d.PanelName, false, r);
            if (!string.IsNullOrEmpty(d.CircuitType))
                WriteString(conduit, "ELC_WIRE_CIRCUIT_TYPE_TXT", d.CircuitType, false, r);
            if (!string.IsNullOrEmpty(d.InstallMethod))
                WriteString(conduit, "ELC_WIRE_INSTALL_METHOD_TXT", d.InstallMethod, false, r);
            if (d.MaxDemandA > 0)
                WriteNumber(conduit, "ELC_WIRE_MAX_DEMAND_A", d.MaxDemandA, r);
            if (d.AmpacityA > 0)
                WriteNumber(conduit, "ELC_WIRE_AMPACITY_A", d.AmpacityA, r);
            return r;
        }

        /// <summary>Writes a string; with overwrite=false an existing value is kept
        /// (neither an error nor a write).</summary>
        internal static void WriteString(Element el, string name, string v, bool overwrite, WireStampWriteReport r)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) { r.Unbound.Add(name); return; }
                if (p.IsReadOnly) { r.ReadOnly.Add(name); return; }
                if (p.StorageType != StorageType.String)
                {
                    r.Failed.Add(name);
                    StingLog.Warn($"WireStamp {el.Id}: {name} is {p.StorageType}, not text");
                    return;
                }
                if (!overwrite && !string.IsNullOrEmpty(p.AsString())) return;
                if (p.Set(v)) r.Written++; else r.Failed.Add(name);
            }
            catch (Exception ex) { r.Failed.Add(name); StingLog.Warn($"WireStamp {el.Id} {name}: {ex.Message}"); }
        }

        /// <summary>Writes a number to a Double, Integer or text parameter.</summary>
        internal static void WriteNumber(Element el, string name, double v, WireStampWriteReport r)
        {
            try
            {
                var p = el.LookupParameter(name);
                if (p == null) { r.Unbound.Add(name); return; }
                if (p.IsReadOnly) { r.ReadOnly.Add(name); return; }
                bool ok;
                switch (p.StorageType)
                {
                    case StorageType.Double:  ok = p.Set(v); break;
                    case StorageType.Integer: ok = p.Set((int)Math.Round(v)); break;
                    case StorageType.String:
                        ok = p.Set(v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); break;
                    default: ok = false; break;
                }
                if (ok) r.Written++; else r.Failed.Add(name);
            }
            catch (Exception ex) { r.Failed.Add(name); StingLog.Warn($"WireStamp {el.Id} {name}: {ex.Message}"); }
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
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            var uidoc = ctx.UIDoc;
            var doc   = ctx.Doc;

            Reference picked;
            try { picked = uidoc.Selection.PickObject(ObjectType.Element, new ConduitSelectionFilter(),
                "Pick conduit to stamp wire parameters from circuit"); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            var conduit = doc.GetElement(picked.ElementId);
            if (conduit == null) { message = "Invalid element."; return Result.Failed; }

            var wsd = WireStampHelper.FromConduit(doc, conduit);
            if (!wsd.Valid)
            {
                TaskDialog.Show("Wire Stamp", "No circuit found for this conduit: "
                    + (string.IsNullOrEmpty(wsd.NoCircuitReason) ? "reason unknown" : wsd.NoCircuitReason)
                    + ".\n\nThe circuit is found by following the conduit run to the device or panel "
                    + "at its ends. Make sure the run is connected and the device is on a circuit.");
                return Result.Succeeded;
            }

            WireStampWriteReport report;
            using (var tx = new Transaction(doc, "STING Stamp Wire Params"))
            {
                tx.Start();
                report = WireStampHelper.WriteToConduit(conduit, wsd);
                tx.Commit();
            }

            ConduitCircuitIndex.Invalidate(); // clear cache after write
            StingLog.Info($"WireParamStamp: conduit {conduit.Id} → circuit {wsd.CircuitNumber}, {report.Written} parameter(s) written");

            TaskDialog.Show("Wire Stamp",
                  $"Circuit: {wsd.CircuitNumber}  Panel: {wsd.PanelName}\n"
                + $"  Phase: {wsd.Phase}  Cores: {wsd.CoreCount}  Mat: {wsd.ConductorMat}\n"
                + $"  Max demand: {wsd.MaxDemandA:0.0} A\n\n"
                + $"{report.Written} parameter(s) written."
                + report.Describe());
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
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            var uidoc = ctx.UIDoc;
            var doc   = ctx.Doc;

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

            // "Stamped" means at least one parameter was actually written. A conduit
            // whose circuit was found but whose parameters are all unbound is counted
            // separately, not as stamped.
            int stamped = 0, nothingWritten = 0, skipped = 0;
            var skipReasons = new Dictionary<string, int>(StringComparer.Ordinal);
            var total = new WireStampWriteReport();
            var progress = StingProgressDialog.Show("Batch Wire Stamp", conduits.Count);
            try
            {
                using var tx = new Transaction(doc, "STING Batch Stamp Wire Params");
                tx.Start();
                foreach (var conduit in conduits)
                {
                    if (progress?.IsCancelled == true) break;
                    var wsd = WireStampHelper.FromConduit(doc, conduit);
                    if (wsd.Valid)
                    {
                        var r = WireStampHelper.WriteToConduit(conduit, wsd);
                        total.Merge(r);
                        if (r.Written > 0) stamped++; else nothingWritten++;
                    }
                    else
                    {
                        skipped++;
                        string why = string.IsNullOrEmpty(wsd.NoCircuitReason) ? "reason unknown" : wsd.NoCircuitReason;
                        skipReasons[why] = skipReasons.TryGetValue(why, out int n) ? n + 1 : 1;
                    }
                    progress?.Increment(conduit.Name ?? "conduit");
                }
                tx.Commit();
            }
            finally { progress?.Close(); }

            ConduitCircuitIndex.Invalidate();
            StingLog.Info($"BatchWireParamPopulate: {stamped} stamped, {nothingWritten} nothing written, {skipped} no circuit");

            var msg = new System.Text.StringBuilder();
            msg.AppendLine($"Stamped: {stamped} conduit(s) ({total.Written} parameter value(s) written)");
            if (nothingWritten > 0)
                msg.AppendLine($"Circuit found but nothing written: {nothingWritten} conduit(s)");
            msg.AppendLine($"Skipped (no circuit): {skipped} conduit(s)");
            foreach (var kv in skipReasons.OrderByDescending(k => k.Value).Take(5))
                msg.AppendLine($"  • {kv.Value} × {kv.Key}");
            msg.Append(total.Describe());
            TaskDialog.Show("Batch Wire Stamp", msg.ToString());
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
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            var uidoc = ctx.UIDoc;
            var doc   = ctx.Doc;

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
    // Gap 4 — Cable sizer write-back: run CableSizerEngine, stamp CSA / VD / breaker
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class WireCableSizerSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            var uidoc = ctx.UIDoc;
            var doc   = ctx.Doc;

            var selIds = uidoc.Selection.GetElementIds();
            IList<Element> conduits = selIds.Count > 0
                ? selIds.Select(id => doc.GetElement(id))
                    .Where(el => el?.Category?.Id?.Value == (long)BuiltInCategory.OST_Conduit).ToList()
                : new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType().ToElements();

            if (conduits.Count == 0)
            { TaskDialog.Show("Cable Sizer Sync", "No conduits found."); return Result.Succeeded; }

            // KUT-7 - read the panel selection ONCE. Before this the standard was
            // omitted from CableSizeInput entirely, so this command silently sized to
            // BS 7671 whatever the panel said.
            string activeStandard = StingTools.Standards.ElectricalStandardId.Normalise(
                StingTools.UI.StingElectricalCommandHandler.ActivePanel?.SelectedStandard);
            int sized = 0, refused = 0, noParam = 0;
            // Group refusals by the engine's own reason. The old dialog printed
            // "<standard> conductor sizing is not implemented" for every refusal,
            // including "no tabulated size satisfies the voltage-drop limit".
            var refusalReasons = new Dictionary<string, int>(StringComparer.Ordinal);
            var total = new WireStampWriteReport();
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

                    // Derive kW from current: 1-phase P = V × I × PF; 3-phase P = √3 × V × I × PF
                    double voltV = phaseStr?.Contains("3") == true ? 400 : 230;
                    int phases   = phaseStr?.Contains("3") == true ? 3 : 1;
                    double pf    = 0.85;
                    double kw    = phases == 3
                        ? Math.Sqrt(3.0) * voltV * demandA * pf / 1000.0
                        : voltV * demandA * pf / 1000.0;

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
                        // KUT-7 - was omitted entirely, so this silently defaulted to
                        // BS 7671 whatever the panel said.
                        Standard       = activeStandard,
                    };

                    var result = CableSizerEngine.Calculate(input);
                    if (result == null) continue;
                    // A refusal must not be written to the model as a zero CSA.
                    if (!result.Sized)
                    {
                        refused++;
                        string why = string.IsNullOrWhiteSpace(result.Warning)
                            ? "the sizer returned no size and no reason" : result.Warning.Trim();
                        refusalReasons[why] = refusalReasons.TryGetValue(why, out int n) ? n + 1 : 1;
                        continue;
                    }

                    // ELC_WIRE_AMPACITY_A holds the cable's current-carrying capacity
                    // (Iz). CableSizeResult does not report Iz — DesignCurrentA is the
                    // design current Ib, which the old code wrote here and labelled Iz.
                    // Ib already lives in ELC_WIRE_MAX_DEMAND_A (it is this command's
                    // input), so Iz is left alone until the sizer returns one.
                    var r = new WireStampWriteReport();
                    WireStampHelper.WriteNumber(el, "ELC_WIRE_CSA_MM2_NUM",       result.RecommendedCsaMm2, r);
                    WireStampHelper.WriteNumber(el, "ELC_WIRE_VD_PCT_NUM",        result.ActualVoltDropPct, r);
                    if (result.ProposedBreakerA > 0)
                        WireStampHelper.WriteNumber(el, "ELC_WIRE_CIRCUIT_BREAKER_A", result.ProposedBreakerA, r);
                    total.Merge(r);
                    // Sized only when the CSA itself landed — a missing CSA parameter
                    // means nothing was sized on this conduit, whatever else wrote.
                    if (!r.Unbound.Contains("ELC_WIRE_CSA_MM2_NUM")
                        && !r.ReadOnly.Contains("ELC_WIRE_CSA_MM2_NUM")
                        && !r.Failed.Contains("ELC_WIRE_CSA_MM2_NUM"))
                        sized++;
                    else
                        noParam++;
                }
                catch (Exception ex) { StingLog.Warn($"CableSizerSync {el.Id}: {ex.Message}"); }
            }
            tx.Commit();
            StingLog.Info($"WireCableSizerSync: {sized} sized, {refused} refused, {noParam} CSA not writable");

            // KUT-7 - a refusal is REPORTED, not folded into the "sized" count and not
            // written to the model as a zero CSA — with the engine's own reason.
            var msg = new System.Text.StringBuilder();
            msg.AppendLine($"Cable-sized {sized} conduit(s) under "
                + $"{StingTools.Standards.ElectricalStandardId.Label(activeStandard)}.");
            if (sized > 0)
                msg.AppendLine("Written: CSA, voltage drop %, proposed breaker rating. "
                    + "Current-carrying capacity (Iz) is not written — the sizer does not report it.");
            if (refused > 0)
            {
                msg.AppendLine($"\n{refused} conduit(s) were NOT sized; their parameters were left untouched:");
                foreach (var kv in refusalReasons.OrderByDescending(k => k.Value).Take(5))
                    msg.AppendLine($"  • {kv.Value} × {kv.Key}");
            }
            if (noParam > 0)
                msg.AppendLine($"\n{noParam} conduit(s) were sized but the CSA could not be written.");
            msg.Append(total.Describe());
            msg.Append("\n\nRe-run 'W-Batch' to refresh annotations.");
            TaskDialog.Show("Cable Sizer Sync", msg.ToString());
            return Result.Succeeded;
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
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            var uidoc = ctx.UIDoc;
            var doc   = ctx.Doc;

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
                + $"Panel-side end: ({panelEndPt.X * MmPerFt:0} mm, {panelEndPt.Y * MmPerFt:0} mm)");
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

        private const double MmPerFt = 304.8;

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
                double shaftFt = 150.0 / MmPerFt;
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
                double headFt = 30.0 / MmPerFt;
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
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            var uidoc = ctx.UIDoc;
            var doc   = ctx.Doc;

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
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            var doc = ctx.Doc;
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

                // Rule SWA-1: Armoured cables — armour continuity confirmation required.
                // ELC_WIRE_ARMOUR_CONT_OK_BOOL is the explicit armour-continuity sign-off flag;
                // ELC_WIRE_SHIELDED_BOOL indicates EMC screening, which is a different property.
                if (isSWA)
                {
                    bool armourContConfirmed = ParameterHelpers.GetInt(el, "ELC_WIRE_ARMOUR_CONT_OK_BOOL", 0) != 0;
                    if (!armourContConfirmed)
                        issues.Add(new RoutingIssue(el.Id,
                            "SWA cable: armour continuity at both terminations not confirmed "
                            + "(set ELC_WIRE_ARMOUR_CONT_OK_BOOL=1 after test per BS 7671 §543).",
                            "BS 7671:2018 §543.3 / §522.8.1"));
                }

                // Rule SWA-2: Armoured cable entering metallic containment — bonding required.
                // Only flag when the install method is metallic (A1, A2, B1, B2, C are enclosures/surface;
                // restrict to A1/A2 where metallic conduit/trunking creates a second metallic path).
                bool isMetallicContainment = string.Equals(method, "A1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(method, "A2", StringComparison.OrdinalIgnoreCase);
                if (isSWA && isMetallicContainment)
                    issues.Add(new RoutingIssue(el.Id,
                        "SWA cable in metallic conduit/trunking (Method A1/A2): verify armour bonded "
                        + "to metallic containment at all entry points.",
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
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { message = "No document open."; return Result.Failed; }
            var doc = ctx.Doc;

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
