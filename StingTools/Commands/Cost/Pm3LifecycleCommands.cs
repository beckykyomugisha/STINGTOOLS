// ══════════════════════════════════════════════════════════════════════════
//  Pm3LifecycleCommands.cs — Revit hooks over the PM-3 lifecycle engines.
//
//  Surfaces the pure Core.Cost engines (CvrEngine / LossAndExpenseEngine /
//  CostToCompleteEngine / CommitmentsRegister) against the established cost
//  seams — BuildBOQDocument, ContractSumResolver, WeightedPctComplete,
//  CertifiedToDate, the EVM actuals importer and the VariationEngine.
//
//  Command tags:
//    Cvr_Report          — cost-value reconciliation at today's cut-off
//    LossExpense_Value    — value a prolongation claim off agreed-VO EOT days
//    CostToComplete_Lines — per-BOQ-line cost-to-complete CSV + summary
//    Commitments_Report   — sub-contract/PO commitments rolled up vs budget
//
//  Commitments are QS-authored in <BIM manager>/commitments.json (additive,
//  safe-defaulted); this command reads + rolls them up against the live bill.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.BIMManager;   // BIMManagerEngine
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Cost;
using StingTools.Core.Evm;     // EvmCalculator
using StingTools.Core.PaymentCert;
using StingTools.Core.Variation;
using StingTools.Select;       // StingListPicker
using StingTools.UI;

namespace StingTools.Commands.Cost
{
    // ── Cvr_Report — cost-value reconciliation ────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CvrReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var boq = BOQCostManager.BuildBOQDocument(doc);
                // Forecast final VALUE = frozen contract sum + agreed variations.
                double forecastValue = ContractSumResolver.Resolve(doc, boq, out string baseSrc);
                double pctEarned = EvmCalculateCommand.WeightedPctComplete(doc);
                double value = forecastValue * pctEarned / 100.0;

                string actualsDir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "actuals");
                double cost = EvmCalculator.ImportAllActualsToDate(actualsDir, DateTime.UtcNow, out _, out _);

                double certified = 0;
                try { certified = PaymentCertEngine.CertifiedToDate(doc, doc.ProjectInformation?.Number ?? ""); }
                catch (Exception ex) { StingLog.Warn($"CVR certified-to-date: {ex.Message}"); }

                // Forecast final COST = CPI-typical EAC when actuals exist, else value-rate.
                double forecastCost = forecastValue;
                if (cost > 0 && value > 0)
                    forecastCost = EvmCalculator.Compute(forecastValue, value, value, cost, DateTime.UtcNow).Eac;

                double provisions = TagConfig.GetConfigDouble("CVR_PROVISIONS_UGX", 0.0);

                var r = CvrEngine.Compute(new CvrInput
                {
                    ValueOfWorkDoneUGX = value,
                    CostToDateUGX = cost,
                    AmountCertifiedUGX = certified,
                    ProvisionsUGX = provisions,
                    ForecastFinalCostUGX = forecastCost,
                    ForecastFinalValueUGX = forecastValue,
                });

                string cur = boq.Currency;
                var panel = StingResultPanel.Create("Cost-Value Reconciliation")
                    .SetSubtitle($"Cut-off {DateTime.UtcNow:yyyy-MM-dd} · earned {pctEarned:0.#}%")
                    .AddSection("POSITION")
                    .Metric("Value of work done", $"{cur} {r.ValueUGX:N0}")
                    .Metric("Cost to date", $"{cur} {r.CostUGX:N0}")
                    .Metric("Provisions", $"{cur} {r.ProvisionsUGX:N0}")
                    .Metric("Gross margin", $"{cur} {r.GrossMarginUGX:N0}")
                    .Metric("Margin %", $"{r.MarginPct:F1}%")
                    .AddSection("CLAIM")
                    .Metric("Certified to date", $"{cur} {certified:N0}")
                    .Metric("WIP (value − certified)", $"{cur} {r.WipUGX:N0}")
                    .Metric("Position", r.ClaimPosition);
                if (r.HasForecast)
                    panel.AddSection("FORECAST")
                        .Metric("Cost to complete", $"{cur} {r.CostToCompleteUGX:N0}")
                        .Metric("Forecast margin", $"{cur} {r.ForecastMarginUGX:N0}")
                        .Metric("Forecast margin %", $"{r.ForecastMarginPct:F1}%");
                panel.Text($"Contract-sum basis: {baseSrc}. Provisions knob CVR_PROVISIONS_UGX = {provisions:N0}.")
                    .Show();

                // CSV trail.
                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_CVR", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("Metric,Value");
                sb.AppendLine($"ValueOfWorkDone,{r.ValueUGX:F0}");
                sb.AppendLine($"CostToDate,{r.CostUGX:F0}");
                sb.AppendLine($"Provisions,{r.ProvisionsUGX:F0}");
                sb.AppendLine($"GrossMargin,{r.GrossMarginUGX:F0}");
                sb.AppendLine($"MarginPct,{r.MarginPct:F2}");
                sb.AppendLine($"Certified,{certified:F0}");
                sb.AppendLine($"WIP,{r.WipUGX:F0}");
                sb.AppendLine($"ClaimPosition,{r.ClaimPosition}");
                sb.AppendLine($"CostToComplete,{r.CostToCompleteUGX:F0}");
                sb.AppendLine($"ForecastMargin,{r.ForecastMarginUGX:F0}");
                File.WriteAllText(csv, sb.ToString());
                StingLog.Info($"CVR: margin {r.GrossMarginUGX:N0} ({r.MarginPct:F1}%), {r.ClaimPosition} WIP {r.WipUGX:N0}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cvr_Report", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    // ── LossExpense_Value — prolongation off agreed-VO EOT days ────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LossExpenseValueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // Sum EOT days across AGREED variations (Approved / Incorporated).
                int eot = 0; int counted = 0;
                foreach (var p in VariationEngine.ListVariations(doc))
                {
                    var v = VariationEngine.Load(p);
                    if (v == null) continue;
                    if (v.Status == VariationStatus.Approved || v.Status == VariationStatus.Incorporated)
                    { eot += Math.Max(0, v.EotDays); counted++; }
                }
                if (eot <= 0)
                {
                    StingResultPanel.Create("Loss & Expense")
                        .AddSection("NO EOT")
                        .Text("No agreed variation carries extension-of-time days. Capture EOT on a "
                            + "variation (Approve it) before valuing prolongation.")
                        .Show();
                    return Result.Cancelled;
                }

                // Weekly time-related prelims: config knob, else a quick band picker.
                double weekly = TagConfig.GetConfigDouble("COST_WEEKLY_PRELIMS_UGX", 0.0);
                if (weekly <= 0)
                {
                    var bands = new[] { 2_000_000, 5_000_000, 10_000_000, 20_000_000, 35_000_000, 50_000_000 }
                        .Select(b => new StingListPicker.ListItem { Label = $"UGX {b:N0} / week", Tag = (double)b }).ToList();
                    var picked = StingListPicker.Show("STING — Weekly prelims (time-related)",
                        $"Time-related site preliminaries per week (staff, welfare, plant standing). "
                        + $"{counted} agreed VO(s) → {eot} EOT day(s). Set COST_WEEKLY_PRELIMS_UGX to skip this prompt.",
                        bands, allowMultiSelect: false);
                    if (picked == null || picked.Count == 0) return Result.Cancelled;
                    weekly = (double)picked[0].Tag;
                }

                int daysPerWeek = (int)TagConfig.GetConfigDouble("COST_SITE_DAYS_PER_WEEK", 7.0);
                double ohpPct = TagConfig.GetConfigDouble("COST_HEAD_OFFICE_OHP_PCT", 0.0);

                var le = LossAndExpenseEngine.Value(new LossExpenseInput
                {
                    EotDays = eot,
                    WeeklyPrelimsUGX = weekly,
                    DaysPerWeek = daysPerWeek,
                    HeadOfficeOhpPct = ohpPct,
                });

                StingResultPanel.Create("Loss & Expense (prolongation)")
                    .SetSubtitle($"{eot} EOT days · {le.Weeks:0.##} weeks")
                    .AddSection("BUILD-UP")
                    .Metric("Prolongation", $"UGX {le.ProlongationUGX:N0}")
                    .Metric($"Head-office OHP ({ohpPct:0.#}%)", $"UGX {le.HeadOfficeUGX:N0}")
                    .Metric("Disruption", $"UGX {le.DisruptionUGX:N0}")
                    .Metric("Finance charges", $"UGX {le.FinanceChargesUGX:N0}")
                    .AddSection("TOTAL")
                    .Metric("L&E claim", $"UGX {le.TotalUGX:N0}")
                    .Text($"weekly prelims {weekly:N0} × {le.Weeks:0.##} weeks ({daysPerWeek}-day week). "
                        + "Raise a CompensationEvent VO for this sum to carry it into the cert/forecast.")
                    .Show();
                StingLog.Info($"L&E: {eot} EOT days → UGX {le.TotalUGX:N0}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("LossExpense_Value", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    // ── CostToComplete_Lines — per-BOQ-line CTC ───────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostToCompleteLinesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var boq = BOQCostManager.BuildBOQDocument(doc);

                // One filtered sweep → elementId → % complete (PM-6).
                var pctById = new Dictionary<long, double>();
                var sweep = new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                    .WhereElementIsNotElementType();
                foreach (var el in sweep)
                {
                    var p = el.LookupParameter(ParamRegistry.PMT_PCT_COMPLETE_NR);
                    if (p != null && p.HasValue) pctById[el.Id.Value] = p.AsDouble();
                }

                double LinePct(BOQLineItem it)
                {
                    var ids = (it.ConstituentElementIds != null && it.ConstituentElementIds.Count > 0)
                        ? it.ConstituentElementIds
                        : (it.RevitElementId > 0 ? new List<long> { it.RevitElementId } : new List<long>());
                    if (ids.Count == 0) return 0;
                    double sum = 0; foreach (var id in ids) sum += pctById.GetValueOrDefault(id, 0);
                    return sum / ids.Count;
                }

                double totBudget = 0, totForecast = 0, totCtc = 0;
                var sb = new StringBuilder();
                sb.AppendLine("LineRef,Section,Item,BudgetUGX,PercentComplete,EarnedUGX,CostToCompleteUGX,ForecastFinalUGX,VarianceUGX");
                foreach (var sec in boq.Sections)
                    foreach (var it in sec.Items)
                    {
                        double budget = it.TotalUGX;
                        if (budget <= 0) continue;
                        double pct = LinePct(it);
                        var c = CostToCompleteEngine.ForLine(budget, pct);
                        totBudget += c.BudgetUGX; totForecast += c.ForecastFinalUGX; totCtc += c.CostToCompleteUGX;
                        sb.AppendLine($"{Csv(it.BOQLineRef)},{Csv(sec.NRM2Section)},{Csv(it.ItemName)}," +
                                      $"{c.BudgetUGX:F0},{c.PercentComplete:F1},{c.EarnedCostUGX:F0}," +
                                      $"{c.CostToCompleteUGX:F0},{c.ForecastFinalUGX:F0},{c.VarianceUGX:F0}");
                    }

                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_CTC_Lines", ".csv");
                File.WriteAllText(csv, sb.ToString());

                StingResultPanel.Create("Cost-to-Complete (line level)")
                    .AddSection("PROJECT ROLL-UP")
                    .Metric("Budget", $"{boq.Currency} {Math.Round(totBudget):N0}")
                    .Metric("Cost to complete", $"{boq.Currency} {Math.Round(totCtc):N0}")
                    .Metric("Forecast final", $"{boq.Currency} {Math.Round(totForecast):N0}")
                    .Metric("Variance", $"{boq.Currency} {Math.Round(totForecast - totBudget):N0}")
                    .Text($"Per-line CTC from each line's budget × remaining %, with a CPI-implied "
                        + $"productivity factor where actuals exist. CSV: {Path.GetFileName(csv)}")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CostToComplete_Lines", ex);
                message = ex.Message; return Result.Failed;
            }
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }

    // ── Commitments_Report — PO/sub-contract commitments vs budget ─────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CommitmentsReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                string path = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "commitments.json");
                List<Commitment> commitments = new List<Commitment>();
                if (File.Exists(path))
                {
                    try { commitments = JsonConvert.DeserializeObject<List<Commitment>>(File.ReadAllText(path)) ?? new List<Commitment>(); }
                    catch (Exception ex) { StingLog.Warn($"commitments.json: {ex.Message}"); }
                }
                if (commitments.Count == 0)
                {
                    // Seed a template so a QS can start editing, then report empty.
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        if (!File.Exists(path))
                            File.WriteAllText(path, JsonConvert.SerializeObject(new List<Commitment>
                            {
                                new Commitment { Id = "PO-0001", Supplier = "(example supplier)", Description = "(what)",
                                    Kind = CommitmentKind.PurchaseOrder, BudgetLineRef = "14", CommittedUGX = 0, Status = "Open" }
                            }, Formatting.Indented));
                    }
                    catch (Exception ex) { StingLog.Warn($"seed commitments.json: {ex.Message}"); }

                    StingResultPanel.Create("Commitments register")
                        .AddSection("EMPTY")
                        .Text($"No commitments yet. A starter template was written to:\n{path}\n\n"
                            + "Add PO / sub-contract rows (BudgetLineRef = NRM2 section code) and re-run.")
                        .Show();
                    return Result.Succeeded;
                }

                // Budget by NRM2 section from the live bill.
                var boq = BOQCostManager.BuildBOQDocument(doc);
                var budgetByLine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var sec in boq.Sections)
                {
                    string key = string.IsNullOrEmpty(sec.NRM2Section) ? sec.Name : sec.NRM2Section;
                    if (string.IsNullOrEmpty(key)) continue;
                    budgetByLine.TryGetValue(key, out double cur);
                    budgetByLine[key] = cur + sec.TotalUGX;
                }

                var s = CommitmentsRegister.Rollup(commitments, budgetByLine);

                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_Commitments", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("BudgetLineRef,BudgetUGX,CommittedUGX,CertifiedUGX,OutstandingUGX,UncommittedUGX,OverCommitted");
                foreach (var l in s.ByBudgetLine)
                    sb.AppendLine($"{l.BudgetLineRef},{l.BudgetUGX:F0},{l.CommittedUGX:F0},{l.CertifiedUGX:F0}," +
                                  $"{l.OutstandingUGX:F0},{l.UncommittedUGX:F0},{(l.OverCommitted ? "YES" : "")}");
                File.WriteAllText(csv, sb.ToString());

                var panel = StingResultPanel.Create("Commitments register")
                    .SetSubtitle($"{commitments.Count} order(s)")
                    .AddSection("TOTALS")
                    .Metric("Budget", $"{boq.Currency} {s.TotalBudgetUGX:N0}")
                    .Metric("Committed", $"{boq.Currency} {s.TotalCommittedUGX:N0}")
                    .Metric("Outstanding", $"{boq.Currency} {s.TotalOutstandingUGX:N0}")
                    .Metric("Uncommitted balance", $"{boq.Currency} {s.TotalUncommittedUGX:N0}");
                if (s.OverCommittedLines.Count > 0)
                    panel.AddSection("OVER-COMMITTED").Text(string.Join(", ", s.OverCommittedLines));
                panel.Text($"CSV: {Path.GetFileName(csv)} · source: {path}").Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Commitments_Report", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }
}
