using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Electrical;
using Newtonsoft.Json.Linq;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Commands.Panels;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Read-only helper that scans the active document and returns an
    /// <see cref="ElectricalPanelSnapshot"/> for the dock-panel ViewModels.
    /// All access is wrapped in try/catch so a partial failure in one
    /// section doesn't blank the whole panel.
    /// </summary>
    internal static class ElectricalSnapshotBuilder
    {
        public static ElectricalPanelSnapshot Build(Document doc)
        {
            var snap = new ElectricalPanelSnapshot();
            if (doc == null) return snap;
            try
            {
                snap.Panels = BuildPanels(doc);
                snap.Circuits = BuildCircuits(doc);
                snap.SLDRoot = SafeBuildSLD(doc);
                snap.LoadSummary = BuildLoadSummary(doc);
                snap.TemplateRules = BuildTemplateRules(doc);
                snap.LightingRows = BuildLighting(doc);
                snap.RoomTargets = BuildRoomTargets(doc);
                // Matches the grid's default selection (copper / PVC 70 °C / method C =
                // Table 4D2A, the one shipped Appendix 4 table).
                snap.WireRefRows = BuildWireRefRows("Cu", "PVC70", "C", out string wireRefBasis);
                snap.WireRefBasis = wireRefBasis;
                snap.ComplianceItems = BuildCompliance(doc);
                // KUT-7 — canonical id, so the snapshot carries the same token the
                // engines route on. The panel emits "NEC2023"; the engine used to test
                // for "NEC", which is why selecting NEC 2023 produced a BS 7671 answer.
                snap.Standard = StingTools.Standards.ElectricalStandardId.Normalise(
                    StingTools.UI.StingElectricalCommandHandler.ActivePanel?.SelectedStandard);
                // Phase 178 — surface LastResults caches (no extra Revit reads).
                snap.Feeders = StingTools.Commands.Electrical.FeederSizing.FeederSizerCommand.LastResults
                    .Select(r => new StingTools.UI.FeederData
                    {
                        PanelName = r.PanelName, DemandKW = r.DemandKW,
                        FeederCurrentA = r.DesignCurrentA,
                        ProposedCsaMm2 = r.ProposedCsaMm2,
                        VoltDropPct = r.ActualVDPct,
                        ProposedRatingA = r.ProposedRatingA,
                        Status = r.Status
                    }).ToList();
                snap.FaultResults = StingTools.Commands.Electrical.FaultCurrent.FaultCurrentCommand.LastResults
                    .Select(r => new StingTools.UI.FaultData
                    {
                        PanelName = r.PanelName, Voltage = r.Voltage,
                        FeederCsaMm2 = r.FeederCsaMm2,
                        ZtotalMohm = r.ZtotalMohm,
                        FaultKa = r.FaultKa,
                        AicRequiredKa = r.AicRequiredKa,
                        Status = r.AicRequiredKa > 0 && r.FaultKa > r.AicRequiredKa ? "EXCEEDS_AIC" : "OK"
                    }).ToList();
                snap.ConduitFills = StingTools.UI.StingElectricalCommandHandler.LastConduitFills;
                snap.EmergAudit   = StingTools.UI.StingElectricalCommandHandler.LastEmergAudit;
                snap.LpdRows      = StingTools.UI.StingElectricalCommandHandler.LastLpdRows;
            }
            catch (Exception ex) { StingLog.Warn($"SnapshotBuilder: {ex.Message}"); }
            return snap;
        }

        private static List<PanelData> BuildPanels(Document doc)
        {
            var rows = new List<PanelData>();
            try
            {
                var psvByPanel = new Dictionary<long, PanelScheduleView>();
                foreach (var psv in new FilteredElementCollector(doc)
                    .OfClass(typeof(PanelScheduleView))
                    .Cast<PanelScheduleView>())
                {
                    var pid = psv.GetPanel();
                    if (pid != null && pid != ElementId.InvalidElementId)
                        psvByPanel[pid.Value] = psv;
                }

                foreach (var p in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    string status = psvByPanel.ContainsKey(p.Id.Value) ? "OK" : "Missing";
                    int phaseCount = SafeIntByName(p, "Number of Phases");
                    int wayCount   = SafeIntByName(p, "Number of Circuits");
                    rows.Add(new PanelData
                    {
                        Id = p.Id,
                        // The board's Panel Name, not p.Name (the family TYPE name): two
                        // DBs of one type were indistinguishable in the PNLS grid.
                        Name = BoardName(p),
                        Voltage = SafeStrByName(p, "Voltage", "Panel Voltage"),
                        Phase = phaseCount > 0 ? $"{phaseCount}Ph" : "",
                        Ways = wayCount,
                        ScheduleStatus = status,
                        FedFrom = SafeStr(p, BuiltInParameter.RBS_ELEC_PANEL_SUPPLY_FROM_PARAM)
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildPanels: {ex.Message}"); }
            return rows;
        }

        private static List<CircuitData> BuildCircuits(Document doc)
        {
            var rows = new List<CircuitData>();
            try
            {
                foreach (var sys in new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>())
                {
                    try
                    {
                        if (sys.SystemType != ElectricalSystemType.PowerCircuit) continue;
                    }
                    catch { /* unknown system type — include cautiously */ }
                    rows.Add(new CircuitData
                    {
                        Id = sys.Id,
                        PanelName = TrySafe(() => sys.PanelName) ?? "",
                        CircuitNumber = SafeStr(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER),
                        Description = TrySafe(() => sys.LoadName) ?? sys.Name,
                        Phase = ReadCircuitPhase(sys),
                        CurrentA = SafeDouble(sys, BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM),
                        LoadKW = TrySafe(() => StingTools.Core.Electrical.ElecUnits.VAFromInternal(sys.ApparentLoad) / 1000.0),
                        VoltDropPct = 0,
                        WireSize = SafeStr(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM),
                        LengthM = TrySafe(() => sys.Length * 0.3048),
                        IsSpare = false,
                        IsSpace = false
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildCircuits: {ex.Message}"); }
            return rows;
        }

        private static StingTools.Core.SLD.SLDNode SafeBuildSLD(Document doc)
        {
            try { return StingTools.Core.SLD.SLDCircuitTraverser.BuildHierarchy(doc); }
            catch (Exception ex) { StingLog.Warn($"SafeBuildSLD: {ex.Message}"); return null; }
        }

        private static List<LoadSummaryRow> BuildLoadSummary(Document doc)
        {
            var rows = new List<LoadSummaryRow>();
            try
            {
                foreach (var p in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>())
                {
                    double connected = SafeDouble(p, BuiltInParameter.RBS_ELEC_PANEL_TOTALLOAD_PARAM) / 1000.0;
                    double demand = connected; // No demand factors applied yet — Phase 178.
                    // Panel rating BIP name varies by Revit version; read by parameter
                    // display-name fallback to stay version-portable.
                    int feederA = (int)SafeDoubleByName(p, "Mains", "Max Number of Single Pole Breakers", "Number of Mains");
                    double sparePct = feederA > 0 && connected > 0
                        ? Math.Max(0, (1.0 - (demand / (feederA * 0.001 * 240))) * 100.0)
                        : 0;
                    rows.Add(new LoadSummaryRow
                    {
                        Name = p.Name ?? "",
                        ConnectedKW = connected,
                        DemandKW = demand,
                        SparePct = sparePct
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildLoadSummary: {ex.Message}"); }
            return rows;
        }

        private static List<TemplateRuleRow> BuildTemplateRules(Document doc)
        {
            var rows = new List<TemplateRuleRow>();
            try
            {
                string path = StingToolsApp.FindDataFile("STING_PANEL_SCHEDULE_TEMPLATES.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return rows;
                var root = JObject.Parse(File.ReadAllText(path));
                int pri = 0;
                foreach (var rule in root["rules"] as JArray ?? new JArray())
                {
                    pri++;
                    string pattern = string.Join(",",
                        ((rule["match"]?["namePatterns"] as JArray)?.Select(t => t.ToString()) ?? Enumerable.Empty<string>()));
                    string template = rule["template"]?.ToString() ?? "";
                    rows.Add(new TemplateRuleRow { Priority = pri, Pattern = pattern, Template = template });
                }
                if (root["globalFallback"] != null)
                    rows.Add(new TemplateRuleRow { Priority = 999, Pattern = ".*", Template = root["globalFallback"].ToString() });
            }
            catch (Exception ex) { StingLog.Warn($"BuildTemplateRules: {ex.Message}"); }
            return rows;
        }

        private static List<LightingRow> BuildLighting(Document doc)
        {
            var rows = new List<LightingRow>();
            try
            {
                var grouped = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_LightingFixtures)
                    .WhereElementIsNotElementType()
                    .OfType<FamilyInstance>()
                    .GroupBy(f => f.Symbol?.Name ?? f.Name);
                foreach (var g in grouped)
                {
                    var first = g.First();
                    double watts = SafeDouble(first, BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
                    rows.Add(new LightingRow
                    {
                        FamilyType = g.Key,
                        Watts = watts,
                        Qty = g.Count(),
                        Circuit = "",
                        LmPerW = 0
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildLighting: {ex.Message}"); }
            return rows;
        }

        private static List<RoomTargetRow> BuildRoomTargets(Document doc)
        {
            var rows = new List<RoomTargetRow>();
            try
            {
                foreach (var r in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .OfType<Room>()
                    .Take(50))
                {
                    string name = r.Name ?? "";
                    string target = LuxTargetFor(name);
                    rows.Add(new RoomTargetRow
                    {
                        Room = name, TargetLx = target, EstimatedLx = "—", Delta = "—"
                    });
                }
            }
            catch (Exception ex) { StingLog.Warn($"BuildRoomTargets: {ex.Message}"); }
            return rows;
        }

        private static string LuxTargetFor(string roomName)
            => StingTools.Photometrics.LuxTargetTable.Load().TargetFor(roomName).ToString("0");

        /// <summary>
        /// The wire-reference grid, driven by the SAME Appendix 4 data the BS 7671
        /// cable sizer uses (STING_WIRE_TABLES.json → bs7671Appendix4: Table 4D2A
        /// It, Table 4D2B mV/A/m). A combination with no shipped table (XLPE,
        /// aluminium, other reference methods) gets a "no table shipped" row —
        /// never numbers scaled from another table.
        ///
        /// Before: the grid defaulted to the legacy copperTables "XLPE90" entry
        /// (mislabelled — its currents are 70 °C thermoplastic 4D2A values), showed a
        /// "PVC70" legacy table that does not match 4D2A, and derated aluminium by an
        /// unsourced ×0.78.
        /// </summary>
        public static List<WireRefRow> BuildWireRefRows(string material, string insulation, string method)
            => BuildWireRefRows(material, insulation, method, out _);

        public static List<WireRefRow> BuildWireRefRows(string material, string insulation, string method, out string basis)
        {
            var rows = new List<WireRefRow>();
            basis = "";
            try
            {
                var data = StingTools.Commands.Electrical.CableSizer.CableSizerEngine.Bs7671Tables();
                var table = data?.FindTable(material, insulation, method);
                if (table == null)
                {
                    string have = data == null || data.Tables.Count == 0
                        ? "none (STING_WIRE_TABLES.json bs7671Appendix4 not found)"
                        : string.Join(", ", data.Tables.Select(t => $"{t.Conductor} {t.Insulation} method {t.InstallMethod} (Table {t.Id})"));
                    basis = $"No BS 7671 Appendix 4 table shipped for {material} / {insulation} / method {method}. " +
                            $"Shipped: {have}. Values are not approximated from another table.";
                    rows.Add(new WireRefRow { Size = "—", Imax1Ph = "no table shipped", Imax3Ph = "", Mv1Ph = "", Mv3Ph = "" });
                    return rows;
                }

                int unverified = 0;
                foreach (var r in table.Rows)
                {
                    if (!r.Verified) unverified++;
                    string flag = r.Verified ? "" : " *";
                    rows.Add(new WireRefRow
                    {
                        Size = (r.CsaMm2 < 10 ? $"{r.CsaMm2:0.0}mm²" : $"{r.CsaMm2:0}mm²") + flag,
                        Imax1Ph = $"{r.It1ph:0.#}",
                        Imax3Ph = $"{r.It3ph:0.#}",
                        Mv1Ph = $"{r.MvAm1ph:0.###}",
                        Mv3Ph = $"{r.MvAm3ph:0.###}",
                    });
                }
                basis = $"BS 7671 Appendix 4 Table {table.Id} (It, A — {table.Description}, method {table.InstallMethod}, " +
                        $"30 °C, ungrouped) and Table {table.VoltDropTable} (mV/A/m: 2-core 1-ph / 3–4-core 3-ph)." +
                        (unverified > 0 ? $" * {unverified} row(s) not yet checked against the printed table — verify before use." : "");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BuildWireRefRows: {ex.Message}");
                basis = "Wire reference table could not be loaded — see the STING log.";
            }
            return rows;
        }

        private static List<ComplianceItemViewModel> BuildCompliance(Document doc)
        {
            var items = new List<ComplianceItemViewModel>();
            try
            {
                int panels = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                    .WhereElementIsNotElementType().GetElementCount();
                int psv = new FilteredElementCollector(doc).OfClass(typeof(PanelScheduleView)).GetElementCount();
                if (panels > psv)
                    items.Add(new ComplianceItemViewModel
                    { Icon = "❌", Severity = "error",
                      Message = $"{panels - psv} panel(s) without a panel schedule — run Panel → Batch Schedules." });
                else
                    items.Add(new ComplianceItemViewModel
                    { Icon = "✅", Severity = "info",
                      Message = $"All {panels} panels have a schedule." });

                // VD scan
                var opts = StingElectricalCommandHandler.CurrentVDOptions
                           ?? new VDOptionsSnapshot { LightingLimitPct = 3.0, OtherLimitPct = 5.0, Material = "Cu", OperatingTempC = 70.0 };
                var vds = VoltageDropCommand.Calculate(doc, opts.Standard, opts.LightingLimitPct, opts.OtherLimitPct,
                                                       opts.Material, opts.OperatingTempC);
                int bad = vds.Count(v => v.ExceedsThreshold);
                if (bad > 0)
                    items.Add(new ComplianceItemViewModel
                    { Icon = "⚠", Severity = "warn",
                      Message = $"{bad} circuit(s) exceed the voltage-drop threshold." });
                else
                    items.Add(new ComplianceItemViewModel
                    { Icon = "✅", Severity = "info",
                      Message = $"Voltage drop within limits across {vds.Count} circuit(s)." });
            }
            catch (Exception ex) { StingLog.Warn($"BuildCompliance: {ex.Message}"); }
            return items;
        }

        // ── safe param accessors ─────────────────────────────────────────
        private static string SafeStr(Element e, BuiltInParameter bip)
        { try { return e.get_Parameter(bip)?.AsString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; } }
        private static double SafeDouble(Element e, BuiltInParameter bip)
        { try { return StingTools.Core.Electrical.ElecUnits.ToSi(e.get_Parameter(bip)); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; } }
        private static int SafeInt(Element e, BuiltInParameter bip)
        { try { return e.get_Parameter(bip)?.AsInteger() ?? 0; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 0; } }
        private static T TrySafe<T>(Func<T> f) { try { return f(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return default(T); } }

        private static string ReadCircuitPhase(ElectricalSystem sys)
        {
            try
            {
                var p = sys.LookupParameter("Phase")
                     ?? sys.LookupParameter("Circuit Phase")
                     ?? sys.LookupParameter("Starting Phase");
                if (p == null) return "";
                if (p.StorageType == StorageType.Integer)
                {
                    int v = p.AsInteger();
                    return v switch { 1 => "B", 2 => "C", _ => "A" };
                }
                if (p.StorageType == StorageType.String)
                {
                    string v = (p.AsString() ?? "").Trim().ToUpperInvariant();
                    if (v.StartsWith("B")) return "B";
                    if (v.StartsWith("C")) return "C";
                    return string.IsNullOrEmpty(v) ? "" : "A";
                }
            }
            catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
            return "";
        }

        // Display-name lookup — used when a BIP enum constant differs between
        // Revit versions; tries each fallback name in order.
        private static string SafeStrByName(Element e, params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    var p = e.LookupParameter(n);
                    if (p == null) continue;
                    if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                    string v = p.AsValueString();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
            }
            return "";
        }
        private static string BoardName(FamilyInstance p)
        {
            try
            {
                string n = p.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString();
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            catch (Exception ex) { StingLog.Info($"BoardName {p.Id.Value}: {ex.Message}"); }
            return p.Name ?? "";
        }

        private static int SafeIntByName(Element e, params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    var p = e.LookupParameter(n);
                    if (p == null) continue;
                    if (p.StorageType == StorageType.Integer) return p.AsInteger();
                    if (p.StorageType == StorageType.Double) return (int)p.AsDouble();
                }
                catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
            }
            return 0;
        }
        private static double SafeDoubleByName(Element e, params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    var p = e.LookupParameter(n);
                    if (p == null) continue;
                    if (p.StorageType == StorageType.Double) return p.AsDouble();
                    if (p.StorageType == StorageType.Integer) return p.AsInteger();
                }
                catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
            }
            return 0;
        }
    }
}
