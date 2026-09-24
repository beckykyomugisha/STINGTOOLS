using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core;
using StingTools.Core.Electrical;
using StingTools.UI;

namespace StingTools.Commands.Panels
{
    /// <summary>
    /// PNL-2 — per-circuit BS 7671 check (Ib ≤ In ≤ Iz, VD ≤ limit, PSC ≤ Icn) on every
    /// power circuit. Writes the verdict to ELC_CKT_CHECK_TXT (a column in the STING
    /// panel schedules), colours the devices of failing circuits red in the active
    /// view, and exports a colour-coded workbook. Rules that cannot run are reported
    /// as NOT CHECKED, never as a pass.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelComplianceCheckCommand : IExternalCommand
    {
        public const string CheckParam = "ELC_CKT_CHECK_TXT";
        private const string IzBasis = "Table 4D2A method C, 70 °C PVC Cu, no derating (best case)";
        private const byte RedR = 220, RedG = 40, RedB = 40;
        private const int RedWeight = 6;

        private static bool IsOurRed(OverrideGraphicSettings o)
        {
            if (o == null || o.ProjectionLineWeight != RedWeight) return false;
            var c = o.ProjectionLineColor;
            return c != null && c.IsValid && c.Red == RedR && c.Green == RedG && c.Blue == RedB;
        }

        private sealed class Row
        {
            public string Board = "", Way = "", Load = "", Summary = "";
            public double Ib, In; public double? Iz, Vd, Psc, Icn; public double VdLimit;
            public CircuitCheckResult Result;
            public ElectricalSystem Sys;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var circuits = new FilteredElementCollector(doc).OfClass(typeof(ElectricalSystem))
                .Cast<ElectricalSystem>()
                .Where(s => { try { return s.SystemType == ElectricalSystemType.PowerCircuit && s.BaseEquipment != null; }
                              catch (Exception ex) { StingLog.Warn($"Check filter: {ex.Message}"); return false; } })
                .ToList();
            if (circuits.Count == 0)
            {
                TaskDialog.Show("STING Circuit Check", "No power circuits connected to a board were found.");
                return Result.Cancelled;
            }

            var opts = StingElectricalCommandHandler.CurrentVDOptions;
            var table = StingTools.Commands.Electrical.CableSizer.CableSizerEngine.Bs7671Tables()
                            .FindTable("Cu", "PVC70", "C");
            var rows = new List<Row>();
            int written = 0, unbound = 0, writeFailed = 0;
            var view = doc.ActiveView;
            bool colourView = view != null && !view.IsTemplate && view.ViewType != ViewType.Schedule
                              && view.ViewType != ViewType.PanelSchedule && view.ViewType != ViewType.DrawingSheet;
            var red = new OverrideGraphicSettings()
                .SetProjectionLineColor(new Color(RedR, RedG, RedB)).SetProjectionLineWeight(RedWeight);
            int coloured = 0, cleared = 0;
            // Only elements the view actually draws are coloured and counted; members on
            // other levels or outside the crop would inflate the count.
            HashSet<ElementId> inView = null;
            if (colourView)
            {
                try { inView = new HashSet<ElementId>(new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElementIds()); }
                catch (Exception ex) { StingLog.Warn($"Check view scope: {ex.Message}"); colourView = false; }
            }

            using (var tx = new Transaction(doc, "STING Circuit Compliance Check"))
            {
                tx.Start();
                foreach (var sys in circuits)
                {
                    var row = Evaluate(doc, sys, table, opts);
                    rows.Add(row);
                    // The parameter write and the colouring are independent: an unbound
                    // parameter must not stop failing devices being shown red.
                    if (sys.LookupParameter(CheckParam) == null) unbound++;
                    else if (ParameterHelpers.SetString(sys, CheckParam, row.Summary, overwrite: true)) written++;
                    else { writeFailed++; StingLog.Warn($"Check write refused on circuit {sys.Id.Value}"); }
                    if (colourView)
                        foreach (Element el in SafeMembers(sys))
                        {
                            if (!inView.Contains(el.Id)) continue;
                            try
                            {
                                if (row.Result.Failed) { view.SetElementOverrides(el.Id, red); coloured++; }
                                // "Red to green": a device this check coloured on an earlier run
                                // whose circuit now passes is cleared, but only when its override is
                                // exactly our red, so a hand-set override is never wiped.
                                else if (IsOurRed(view.GetElementOverrides(el.Id)))
                                { view.SetElementOverrides(el.Id, new OverrideGraphicSettings()); cleared++; }
                            }
                            catch (Exception ex) { StingLog.Info($"Check colour {el.Id}: {ex.Message}"); }
                        }
                }
                tx.Commit();
            }

            string xlsx = WriteWorkbook(doc, rows);

            int fail = rows.Count(r => r.Result.Failed);
            int full = rows.Count(r => r.Result.FullyVerified);
            int partial = rows.Count - fail - full;
            var panel = StingResultPanel.Create("STING Circuit Compliance (BS 7671)");
            panel.SetSubtitle($"{rows.Count} circuits · {fail} fail · {full} fully verified · {partial} partly checked");
            panel.AddSection("SUMMARY")
                 .MetricError("Failing circuits", fail.ToString())
                 .MetricHighlight("Fully verified OK", full.ToString())
                 .MetricWarn("OK but not every rule could run", partial.ToString())
                 .Metric("Written to " + CheckParam, written.ToString(),
                         unbound > 0 ? $"{unbound} circuit(s) lack the parameter — run Load Params" : null);
            if (writeFailed > 0)
                panel.MetricError("Writes refused", writeFailed.ToString(), "see the STING log");
            panel
                 .Metric("Devices coloured red", coloured.ToString(), colourView ? $"in '{view.Name}'" : "open a plan to colour devices")
                 .Metric("Devices cleared (now passing)", cleared.ToString());
            if (fail > 0)
            {
                panel.AddSection("FAILURES");
                foreach (var r in rows.Where(x => x.Result.Failed).OrderBy(x => x.Board).ThenBy(x => x.Way).Take(30))
                    panel.Text($"{r.Board} / {r.Way}  {r.Load}: {string.Join("; ", r.Result.Failures)}");
                if (fail > 30) panel.Text($"… {fail - 30} more in the workbook.");
            }
            var why = rows.SelectMany(r => r.Result.NotChecked).GroupBy(x => x).OrderByDescending(g => g.Count());
            if (why.Any())
            {
                panel.AddSection("NOT CHECKED (inputs missing)");
                foreach (var g in why) panel.Text($"{g.Count()} × {g.Key}");
            }
            panel.AddSection("BASIS")
                 .Text("Ib ≤ In ≤ Iz (Reg 433.1.1); VD ≤ " + (opts?.OtherLimitPct > 0 ? $"{opts.OtherLimitPct:0.#}" : "5") + " % other / "
                       + (opts?.LightingLimitPct > 0 ? $"{opts.LightingLimitPct:0.#}" : "3") + " % lighting (App 12); PSC ≤ board breaking capacity (434.5.1).")
                 .Text("Iz: " + IzBasis + ". VD from CALCS → Recalculate All. PSC from CALCS → Calculate Fault Levels.")
                 .Text(string.IsNullOrEmpty(xlsx) ? "Workbook not written — see the STING log." : "Workbook: " + xlsx);
            panel.Show();
            return Result.Succeeded;
        }

        private static Row Evaluate(Document doc, ElectricalSystem sys, Bs7671CapacityTable table, VDOptionsSnapshot opts)
        {
            var row = new Row { Sys = sys };
            try { row.Board = sys.PanelName ?? ""; } catch (Exception ex) { StingLog.Info($"Check board: {ex.Message}"); }
            try { row.Way = sys.CircuitNumber ?? ""; } catch (Exception ex) { StingLog.Info($"Check way: {ex.Message}"); }
            try { row.Load = sys.LoadName ?? ""; } catch (Exception ex) { StingLog.Info($"Check load: {ex.Message}"); }

            row.Ib = ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM);
            row.In = ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM);

            int poles = 1;
            try { poles = sys.PolesNumber; } catch (Exception ex) { StingLog.Info($"Check poles: {ex.Message}"); }
            string wire = "";
            try { wire = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM)?.AsString() ?? ""; }
            catch (Exception ex) { StingLog.Info($"Check wire: {ex.Message}"); }
            double csa = WireSizeParser.ParseCsaMm2(wire);
            double it = table != null && csa > 0 ? Bs7671Data.TabulatedIt(table, csa, poles >= 3 ? 3 : 1) : 0;
            row.Iz = it > 0 ? it : (double?)null;

            var vdp = sys.LookupParameter(ParamRegistry.ELC_CKT_VD_PCT);
            if (vdp != null && vdp.HasValue && vdp.StorageType == StorageType.Double) row.Vd = vdp.AsDouble();
            bool lighting = false;
            try { lighting = VoltageDropCommand.IsLightingCircuit(sys); } catch (Exception ex) { StingLog.Info($"Check lighting: {ex.Message}"); }
            row.VdLimit = VoltageDropEngine.LimitFor(lighting, opts?.LightingLimitPct ?? 0, opts?.OtherLimitPct ?? 0);

            if (sys.BaseEquipment is FamilyInstance board)
            {
                var psc = board.LookupParameter(ParamRegistry.ELC_PNL_FAULT_KA);
                if (psc != null && psc.HasValue && psc.StorageType == StorageType.Double && psc.AsDouble() > 0)
                    row.Psc = psc.AsDouble();
                row.Icn = ReadKa(board.get_Parameter(BuiltInParameter.RBS_ELEC_SHORT_CIRCUIT_RATING));
            }

            row.Result = CircuitComplianceRule.Evaluate(new CircuitCheckInput
            {
                IbA = row.Ib, InA = row.In, IzA = row.Iz, IzBasis = IzBasis, IzIsUpperBound = true,
                VdPct = row.Vd, VdLimitPct = row.VdLimit,
                ProspectiveFaultKa = row.Psc, BreakingCapacityKa = row.Icn,
            });
            row.Summary = row.Result.Summary;
            return row;
        }

        /// <summary>kA from a parameter stored as a number or as text ("10 kA", "10kA").</summary>
        private static double? ReadKa(Parameter p)
        {
            if (p == null || !p.HasValue) return null;
            try
            {
                if (p.StorageType == StorageType.Double)
                {
                    double v = ElecUnits.ToSi(p);           // amps when the spec is current
                    return v > 0 ? KaFrom(v, hasKaUnit: false, raw: v.ToString(CultureInfo.InvariantCulture)) : (double?)null;
                }
                string s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
                var m = Regex.Match(s ?? "", @"(\d+(?:[.,]\d+)?)");
                if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double v2) && v2 > 0)
                    return KaFrom(v2, Regex.IsMatch(s, @"k\s*A", RegexOptions.IgnoreCase), s);
            }
            catch (Exception ex) { StingLog.Info($"Check kA read: {ex.Message}"); }
            return null;
        }

        private static double KaFrom(double value, bool hasKaUnit, string raw)
        {
            double ka = CircuitComplianceRule.BreakingCapacityKa(value, hasKaUnit);
            if (ka != value) StingLog.Info($"Check kA: '{raw}' read as amps → {ka:0.##} kA");
            return ka;
        }

        private static IEnumerable<Element> SafeMembers(ElectricalSystem sys)
        {
            var list = new List<Element>();
            try { foreach (Element e in sys.Elements) list.Add(e); }
            catch (Exception ex) { StingLog.Info($"Check members: {ex.Message}"); }
            return list;
        }

        private static string WriteWorkbook(Document doc, List<Row> rows)
        {
            try
            {
                string path = StingPaths.ExportFile(doc, "Schedule", "CircuitCompliance", ".xlsx");
                using (var wb = new XLWorkbook())
                {
                    var ws = wb.Worksheets.Add("Circuit check");
                    string[] h = { "Board", "Way", "Circuit", "Ib (A)", "In (A)", "Iz (A)", "VD (%)", "VD limit (%)", "PSC (kA)", "Icn (kA)", "Result" };
                    for (int i = 0; i < h.Length; i++) { ws.Cell(1, i + 1).Value = h[i]; ws.Cell(1, i + 1).Style.Font.Bold = true; }
                    int r = 2;
                    foreach (var x in rows.OrderBy(a => a.Board).ThenBy(a => a.Way))
                    {
                        ws.Cell(r, 1).Value = x.Board; ws.Cell(r, 2).Value = x.Way; ws.Cell(r, 3).Value = x.Load;
                        ws.Cell(r, 4).Value = Math.Round(x.Ib, 1); ws.Cell(r, 5).Value = x.In;
                        if (x.Iz.HasValue) ws.Cell(r, 6).Value = x.Iz.Value;
                        if (x.Vd.HasValue) ws.Cell(r, 7).Value = Math.Round(x.Vd.Value, 2);
                        ws.Cell(r, 8).Value = x.VdLimit;
                        if (x.Psc.HasValue) ws.Cell(r, 9).Value = x.Psc.Value;
                        if (x.Icn.HasValue) ws.Cell(r, 10).Value = x.Icn.Value;
                        ws.Cell(r, 11).Value = x.Summary;
                        ws.Range(r, 1, r, h.Length).Style.Fill.BackgroundColor =
                            x.Result.Failed ? XLColor.LightSalmon : x.Result.FullyVerified ? XLColor.LightGreen : XLColor.LightYellow;
                        r++;
                    }
                    ws.Cell(r + 1, 1).Value = "Iz basis: " + IzBasis + ". Green = every rule ran and passed; yellow = passed what could be checked; red = fails.";
                    ws.Columns().AdjustToContents();
                    wb.SaveAs(path);
                }
                return path;
            }
            catch (Exception ex) { StingLog.Error("Circuit compliance workbook", ex); return ""; }
        }
    }

    /// <summary>
    /// PNL-5 — applied phase balancing. For three-phase boards with a panel schedule,
    /// plans slot moves (PhaseBalancer) that shift single-pole, unlocked circuits onto
    /// the lighter phase, previews them, and on confirmation applies them with
    /// PanelScheduleView.MoveSlotTo — into empty slots only. Slot phases are learned
    /// from the board's own circuits and must agree; otherwise the board is skipped.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PanelBalanceApplyCommand : IExternalCommand
    {
        private sealed class BoardPlan
        {
            public PanelScheduleView View;
            public FamilyInstance Panel;
            public string Name = "";
            public BalancePlan Plan;
            public string Skip;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var views = doc.ActiveView is PanelScheduleView active && !active.IsPanelScheduleTemplate()
                ? new List<PanelScheduleView> { active }
                : new FilteredElementCollector(doc).OfClass(typeof(PanelScheduleView)).Cast<PanelScheduleView>()
                    .Where(v => !v.IsPanelScheduleTemplate()).ToList();
            if (views.Count == 0)
            {
                TaskDialog.Show("STING Phase Balance", "No panel schedules found. Run PNLS → ⚡ Batch Create Schedules first.");
                return Result.Cancelled;
            }

            var plans = views.Select(v => PlanBoard(doc, v)).ToList();
            var todo = plans.Where(p => p.Skip == null && p.Plan.Moves.Count > 0).ToList();

            string preview = string.Join("\n", plans.Select(p => p.Skip != null
                ? $"• {p.Name}: skipped — {p.Skip}"
                : p.Plan.Moves.Count == 0
                    ? $"• {p.Name}: already balanced ({p.Plan.BeforeImbalancePct:0.#} %)"
                    : $"• {p.Name}: {p.Plan.BeforeImbalancePct:0.#} % → {p.Plan.AfterImbalancePct:0.#} % with {p.Plan.Moves.Count} move(s)"));
            if (todo.Count == 0)
            {
                TaskDialog.Show("STING Phase Balance", "Nothing to move.\n\n" + preview);
                return Result.Succeeded;
            }
            var ask = new TaskDialog("STING Phase Balance")
            {
                MainInstruction = $"Move {todo.Sum(p => p.Plan.Moves.Count)} circuit(s) to balance {todo.Count} board(s)?",
                MainContent = preview + "\n\nOnly single-pole, unlocked circuits move, into empty slots. " +
                              "Circuit numbers follow the slot. Lock a slot in the panel schedule to keep a way fixed.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No,
            };
            if (ask.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            var panel = StingResultPanel.Create("STING Phase Balance (applied)");
            int applied = 0, refused = 0;
            using (var tx = new Transaction(doc, "STING Phase Balance"))
            {
                tx.Start();
                foreach (var bp in todo)
                {
                    int ok = 0; var refusedHere = new List<string>();
                    foreach (var m in bp.Plan.Moves)
                    {
                        if (TryMove(bp.View, m.FromSlot, m.ToSlot)) { ok++; doc.Regenerate(); }
                        else refusedHere.Add($"{m.Label} ({m.FromSlot}→{m.ToSlot})");
                    }
                    applied += ok; refused += refusedHere.Count;
                    var actual = PhaseLoads(doc, bp.Panel);
                    panel.AddSection(bp.Name.ToUpperInvariant())
                         .Metric("Planned", $"{bp.Plan.BeforeImbalancePct:0.#} % → {bp.Plan.AfterImbalancePct:0.#} %")
                         .MetricHighlight("Actual now", $"{BalancePlan.ImbalancePct(actual):0.#} %",
                             $"L1 {actual[0] / 1000:0.0} · L2 {actual[1] / 1000:0.0} · L3 {actual[2] / 1000:0.0} kVA")
                         .Metric("Moves applied", $"{ok}/{bp.Plan.Moves.Count}");
                    foreach (var r in refusedHere) panel.Text("⚠ Revit refused: " + r);
                }
                if (applied == 0) tx.RollBack(); else tx.Commit();
            }
            foreach (var p in plans.Where(x => x.Skip != null))
                panel.AddSection(p.Name.ToUpperInvariant()).Text("Skipped — " + p.Skip);
            panel.SetSubtitle($"{applied} move(s) applied · {refused} refused");
            panel.Show();
            return Result.Succeeded;
        }

        private static BoardPlan PlanBoard(Document doc, PanelScheduleView psv)
        {
            var bp = new BoardPlan { View = psv, Name = psv.Name };
            try
            {
                bp.Panel = doc.GetElement(psv.GetPanel()) as FamilyInstance;
                if (bp.Panel == null) { bp.Skip = "no board"; return bp; }
                bp.Name = bp.Panel.get_Parameter(BuiltInParameter.RBS_ELEC_PANEL_NAME)?.AsString() ?? psv.Name;
                if (bp.Panel.MEPModel is ElectricalEquipment ee)
                {
                    if (ee.IsSwitchboard) { bp.Skip = "switchboard (feeders are balanced by design, not by slot moves)"; return bp; }
                    if (!(doc.GetElement(ee.DistributionSystem?.Id ?? ElementId.InvalidElementId) is DistributionSysType ds)
                        || ds.ElectricalPhase != ElectricalPhase.ThreePhase)
                    { bp.Skip = "not a three-phase board"; return bp; }
                }

                int total = psv.GetTableData()?.NumberOfSlots ?? 0;
                if (total <= 0) { bp.Skip = "slot count unknown"; return bp; }
                int step = PanelSlotReader.SlotStep(psv, out _);

                var slotCell = new Dictionary<int, (int row, int col)>();
                for (int s = 1; s <= total; s++)
                {
                    psv.GetCellsBySlotNumber(s, out IList<int> rs, out IList<int> cs);
                    if (rs != null && cs != null && rs.Count > 0 && cs.Count > 0) slotCell[s] = (rs[0], cs[0]);
                }

                var circuits = BoardCircuits(doc, bp.Panel);
                var occupied = new HashSet<int>();
                var movers = new List<BalanceCircuit>();
                var known = new List<(int row, int phase)>();
                foreach (var sys in circuits)
                {
                    int start = 0, poles = 1;
                    try { start = sys.StartSlot; poles = Math.Max(1, sys.PolesNumber); }
                    catch (Exception ex) { StingLog.Info($"Balance slot: {ex.Message}"); }
                    foreach (int sl in CircuitSlotParser.FromStartSlot(start, poles, step)) occupied.Add(sl);
                    if (poles != 1 || !slotCell.TryGetValue(start, out var cell)) continue;

                    double a = ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASEA);
                    double b = ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASEB);
                    double c = ElecUnits.Read(sys, BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASEC);
                    int nz = (a > 0 ? 1 : 0) + (b > 0 ? 1 : 0) + (c > 0 ? 1 : 0);
                    if (nz != 1) continue;
                    int ph = a > 0 ? 0 : b > 0 ? 1 : 2;
                    known.Add((cell.row, ph));
                    bool locked = false;
                    try { locked = psv.IsSlotLocked(cell.row, cell.col); } catch (Exception ex) { StingLog.Info($"Balance lock: {ex.Message}"); }
                    string label = "";
                    try { label = $"{sys.CircuitNumber} {sys.LoadName}".Trim(); } catch (Exception ex) { StingLog.Info($"Balance label: {ex.Message}"); }
                    movers.Add(new BalanceCircuit { Id = sys.Id.Value, Label = label, Phase = ph, LoadVa = a + b + c, Slot = start, Locked = locked });
                }

                // Spares and spaces occupy their slots too.
                foreach (var kv in slotCell)
                {
                    try { if (psv.IsSpare(kv.Value.row, kv.Value.col) || psv.IsSpace(kv.Value.row, kv.Value.col)) occupied.Add(kv.Key); }
                    catch (Exception ex) { StingLog.Info($"Balance spare/space: {ex.Message}"); }
                }

                int? offset = PhaseBalancer.CalibrateRowOffset(known);
                if (offset == null)
                {
                    bp.Skip = known.Count == 0
                        ? "no loaded single-pole circuits to learn the slot phases from"
                        : "slot phases could not be determined (circuits disagree on the row-to-phase pattern)";
                    return bp;
                }

                var free = new List<BalanceFreeSlot>();
                foreach (var kv in slotCell.Where(k => !occupied.Contains(k.Key)))
                {
                    bool locked = false;
                    try { locked = psv.IsSlotLocked(kv.Value.row, kv.Value.col); } catch (Exception ex) { StingLog.Info($"Balance lock: {ex.Message}"); }
                    free.Add(new BalanceFreeSlot { Slot = kv.Key, Phase = PhaseBalancer.PhaseOfRow(kv.Value.row, offset.Value), Locked = locked });
                }

                bp.Plan = PhaseBalancer.Plan(PhaseLoads(doc, bp.Panel), movers, free);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Balance plan {psv.Name}: {ex.Message}");
                bp.Skip = "could not read the schedule: " + ex.Message;
            }
            return bp;
        }

        private static List<ElectricalSystem> BoardCircuits(Document doc, FamilyInstance panel)
            => new FilteredElementCollector(doc).OfClass(typeof(ElectricalSystem)).Cast<ElectricalSystem>()
                .Where(s => { try { return s.BaseEquipment != null && s.BaseEquipment.Id == panel.Id; }
                              catch (Exception ex) { StingLog.Info($"Balance filter: {ex.Message}"); return false; } })
                .ToList();

        private static double[] PhaseLoads(Document doc, FamilyInstance panel)
        {
            var l = new double[3];
            foreach (var s in BoardCircuits(doc, panel))
            {
                l[0] += ElecUnits.Read(s, BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASEA);
                l[1] += ElecUnits.Read(s, BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASEB);
                l[2] += ElecUnits.Read(s, BuiltInParameter.RBS_ELEC_APPARENT_LOAD_PHASEC);
            }
            return l;
        }

        private static bool TryMove(PanelScheduleView psv, int from, int to)
        {
            try
            {
                psv.GetCellsBySlotNumber(from, out IList<int> fr, out IList<int> fc);
                psv.GetCellsBySlotNumber(to, out IList<int> tr, out IList<int> tc);
                if (fr == null || fc == null || tr == null || tc == null
                    || fr.Count == 0 || fc.Count == 0 || tr.Count == 0 || tc.Count == 0) return false;
                if (psv.GetCircuitIdByCell(tr[0], tc[0]) is ElementId occ && occ != ElementId.InvalidElementId) return false;
                if (!psv.CanMoveSlotTo(fr[0], fc[0], tr[0], tc[0])) return false;
                psv.MoveSlotTo(fr[0], fc[0], tr[0], tc[0]);
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Balance move {from}→{to}: {ex.Message}");
                return false;
            }
        }
    }
}
