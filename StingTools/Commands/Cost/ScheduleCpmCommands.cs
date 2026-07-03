// ══════════════════════════════════════════════════════════════════════════
//  ScheduleCpmCommands.cs — PM-4 Revit hooks over the pure scheduling engines.
//
//  Wires the pure Core.Schedule engines (ScheduleImporter / ScheduleCpmBridge /
//  CashFlowSCurve / WorkingCalendar) into the unified ScheduleStore so a QS/PM
//  gets: converged programme import (one parser, predecessors read), CPM
//  critical-path + float, model-driven % complete, and the schedule-driven
//  cash-flow S-curve — the real EVM Planned Value source.
//
//  Command tags:
//    Sched_Import        — import .xml/.xer through the converged parser
//    Sched_Cpm           — run CPM, report critical path + float, persist back
//    Sched_ModelPercent  — derive % complete from phase-reached / elements-placed
//    Sched_SCurve        — build + export the schedule-driven cash-flow S-curve
//
//  Working-calendar override: <project>/_BIM_COORD/working_calendar.json
//  (extra holidays e.g. lunar Eid, or a 6-day week). Absent ⇒ Uganda statutory.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.Schedule;
using StingTools.UI;

namespace StingTools.Commands.Cost
{
    /// <summary>Loads the Uganda working calendar with a project override.</summary>
    internal static class WorkingCalendarStore
    {
        public static WorkingCalendarConfig Load(Document doc)
        {
            try
            {
                string parent = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return new WorkingCalendarConfig();
                string p = Path.Combine(parent, "_BIM_COORD", "working_calendar.json");
                if (!File.Exists(p)) return new WorkingCalendarConfig();
                var cfg = JsonConvert.DeserializeObject<WorkingCalendarConfig>(File.ReadAllText(p));
                return cfg ?? new WorkingCalendarConfig();
            }
            catch (Exception ex) { StingLog.Warn($"WorkingCalendarStore.Load: {ex.Message}"); return new WorkingCalendarConfig(); }
        }
    }

    // ── Sched_Import — converged programme parser → unified store ─────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick a programme (MS Project .xml or Primavera .xer/.xml)",
                    Filter = "Programme files (*.xml;*.xer)|*.xml;*.xer|All files (*.*)|*.*",
                };
                if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.FileName)) return Result.Cancelled;
                string path = dlg.FileName;

                var res = ScheduleImporter.Parse(path);
                if (res.Tasks.Count == 0)
                {
                    StingResultPanel.Create("Schedule import")
                        .AddSection("NOTHING IMPORTED")
                        .Text($"No tasks parsed from {Path.GetFileName(path)}.")
                        .Text(res.Warnings.Count > 0 ? string.Join("\n", res.Warnings) : "")
                        .Show();
                    return Result.Cancelled;
                }

                // Merge into the unified store (replace Tasks; keep periods/milestones).
                var model = ScheduleStore.Load(doc);
                model.Tasks = res.Tasks;
                model.Source = res.Source + ":" + Path.GetFileName(path);
                model.ImportedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                ScheduleStore.Save(doc, model);

                int withPreds = res.Tasks.Count(t => t.Predecessors.Count > 0);
                var panel = StingResultPanel.Create("Schedule imported")
                    .AddSection("RESULT")
                    .Metric("Source", res.Source)
                    .Metric("Tasks", res.Tasks.Count.ToString())
                    .Metric("With predecessors", withPreds.ToString())
                    .Text("Saved to the unified _BIM_COORD/schedule.json. Run Sched_Cpm for "
                        + "critical path + float.");
                if (res.Warnings.Count > 0)
                    panel.AddSection("WARNINGS").Text(string.Join("\n", res.Warnings.Take(20)));
                panel.Show();
                StingLog.Info($"Schedule imported: {res.Tasks.Count} task(s), {withPreds} with preds, source {res.Source}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Sched_Import", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    // ── Sched_Cpm — critical path + float over the unified schedule ───────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleCpmCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var model = ScheduleStore.Load(doc);
                if (model.Tasks.Count == 0)
                {
                    StingResultPanel.Create("CPM")
                        .AddSection("NO SCHEDULE")
                        .Text("No tasks in the unified schedule. Import a programme (Sched_Import) "
                            + "or generate a 4D schedule first.")
                        .Show();
                    return Result.Cancelled;
                }

                var cal = WorkingCalendarStore.Load(doc);
                var r = ScheduleCpmBridge.Solve(model, cal);

                // Persist float/critical back onto the model (transient fields → also
                // write a small CSV the QS can hand off).
                foreach (var t in model.Tasks)
                    if (r.ByTask.TryGetValue(t.Id, out var tr))
                    {
                        t.TotalFloatDays = tr.TotalFloatDays;
                        t.FreeFloatDays = tr.FreeFloatDays;
                        t.IsCritical = tr.IsCritical;
                    }

                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_CPM", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("TaskId,Name,Start,Finish,TotalFloatDays,FreeFloatDays,Critical,EarlyStart,LateFinish");
                foreach (var t in model.Tasks.Where(x => !x.IsSummary).OrderBy(x => x.Start))
                {
                    r.ByTask.TryGetValue(t.Id, out var tr);
                    sb.AppendLine($"{t.Id},\"{Esc(t.Name)}\",{t.Start:yyyy-MM-dd},{t.End:yyyy-MM-dd}," +
                                  $"{tr?.TotalFloatDays ?? 0:F1},{tr?.FreeFloatDays ?? 0:F1}," +
                                  $"{(tr?.IsCritical ?? false ? "YES" : "")}," +
                                  $"{tr?.EarlyStart:yyyy-MM-dd},{tr?.LateFinish:yyyy-MM-dd}");
                }
                File.WriteAllText(csv, sb.ToString());

                var critNames = r.CriticalPath
                    .Select(id => model.Tasks.FirstOrDefault(t => t.Id == id)?.Name)
                    .Where(n => !string.IsNullOrEmpty(n)).Take(15).ToList();

                var panel = StingResultPanel.Create("Critical Path Method")
                    .AddSection("PROJECT")
                    .Metric("Tasks (working)", model.Tasks.Count(t => !t.IsSummary).ToString())
                    .Metric("Duration (working days)", r.ProjectDurationWorkingDays.ToString("F0"))
                    .Metric("Finish (calendar)", r.ProjectFinish.ToString("yyyy-MM-dd"))
                    .Metric("Critical tasks", r.CriticalPath.Count.ToString())
                    .AddSection("CRITICAL PATH")
                    .Text(critNames.Count > 0 ? string.Join("  →  ", critNames) : "(none resolved)")
                    .Text($"CSV: {Path.GetFileName(csv)}");
                if (r.HasCycle || r.Warnings.Count > 0)
                    panel.AddSection("WARNINGS").Text(string.Join("\n", r.Warnings.Distinct().Take(20)));
                panel.Show();
                StingLog.Info($"CPM: {r.CriticalPath.Count} critical task(s), {r.ProjectDurationWorkingDays:F0} working days.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Sched_Cpm", ex);
                message = ex.Message; return Result.Failed;
            }
        }

        private static string Esc(string s) => (s ?? "").Replace("\"", "\"\"");
    }

    // ── Sched_ModelPercent — model-driven % complete ─────────────────────────
    //  The audit (§2/PM-4) found % complete is always 0. Derive it from model
    //  state: for tasks linked to elements (ElementIds), % = placed/expected; for
    //  tasks named after a Revit Phase, % = elements created in that phase ÷ all
    //  elements created up to it (a phase-reached proxy). Writes back into the
    //  unified schedule (and feeds EV / the S-curve earned curve).
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleModelPercentCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var model = ScheduleStore.Load(doc);
                if (model.Tasks.Count == 0)
                {
                    StingResultPanel.Create("Model % complete")
                        .AddSection("NO SCHEDULE").Text("No tasks in the unified schedule.").Show();
                    return Result.Cancelled;
                }

                // Single filtered sweep (PM-6): only STING-tracked categories, once.
                var all = new FilteredElementCollector(doc)
                    .WherePasses(new ElementMulticategoryFilter(SharedParamGuids.AllCategoryEnums))
                    .WhereElementIsNotElementType()
                    .ToList();

                // Per-element % complete (ASS_PMT_PCT_COMPLETE_NR) for element-linked tasks.
                var pctById = new Dictionary<long, double>();
                foreach (var el in all)
                {
                    var p = el.LookupParameter(ParamRegistry.PMT_PCT_COMPLETE_NR);
                    if (p != null && p.HasValue) pctById[el.Id.Value] = p.AsDouble();
                }

                // Phase-created index for phase-reached proxy.
                var phaseOrder = new FilteredElementCollector(doc).OfClass(typeof(Phase))
                    .Cast<Phase>().Select((ph, i) => (ph, i)).ToList();
                var phaseIndexById = phaseOrder.ToDictionary(t => t.ph.Id, t => t.i);
                var phaseByName = phaseOrder.ToDictionary(t => t.ph.Name, t => t.ph, StringComparer.OrdinalIgnoreCase);
                var createdCounts = new Dictionary<int, int>();
                int totalCreated = 0;
                foreach (var el in all)
                {
                    var pc = el.get_Parameter(BuiltInParameter.PHASE_CREATED);
                    if (pc == null || !pc.HasValue) continue;
                    if (!phaseIndexById.TryGetValue(pc.AsElementId(), out int idx)) continue;
                    createdCounts[idx] = createdCounts.GetValueOrDefault(idx) + 1;
                    totalCreated++;
                }

                int updated = 0;
                foreach (var t in model.Tasks)
                {
                    double? pct = null;
                    if (t.ElementIds != null && t.ElementIds.Count > 0)
                    {
                        // Element-linked: average of the linked elements' % (0 when absent
                        // from the model — placed-vs-expected).
                        double sum = 0; int n = t.ElementIds.Count;
                        foreach (var eid in t.ElementIds) sum += pctById.GetValueOrDefault(eid, 0);
                        pct = n > 0 ? sum / n : 0;
                    }
                    else if (phaseByName.TryGetValue(t.Name, out var ph)
                             && phaseIndexById.TryGetValue(ph.Id, out int idx) && totalCreated > 0)
                    {
                        // Phase-reached proxy: cumulative elements created up to and
                        // including this phase ÷ total created.
                        int cum = 0;
                        for (int i = 0; i <= idx; i++) cum += createdCounts.GetValueOrDefault(i);
                        pct = (double)cum / totalCreated * 100.0;
                    }
                    if (pct.HasValue)
                    {
                        double v = Math.Max(0, Math.Min(100, Math.Round(pct.Value, 1)));
                        if (Math.Abs(t.PercentComplete - v) > 0.01) { t.PercentComplete = v; updated++; }
                    }
                }
                ScheduleStore.Save(doc, model);

                double overall = model.Tasks.Where(t => !t.IsSummary).DefaultIfEmpty().Average(t => t?.PercentComplete ?? 0);
                StingResultPanel.Create("Model-driven % complete")
                    .AddSection("RESULT")
                    .Metric("Tasks updated", updated.ToString())
                    .Metric("Elements scanned", all.Count.ToString())
                    .Metric("Overall (avg of tasks)", $"{overall:F1}%")
                    .Text("Derived from element % (linked tasks) or phase-reached (named tasks). "
                        + "Feeds EVM earned value + the S-curve earned curve.")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Sched_ModelPercent", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    // ── Sched_SCurve — schedule-driven cash-flow S-curve (real PV) ────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ScheduleSCurveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var model = ScheduleStore.Load(doc);
                if (model.Tasks.Count == 0)
                {
                    StingResultPanel.Create("Cash-flow S-curve")
                        .AddSection("NO SCHEDULE").Text("No tasks in the unified schedule.").Show();
                    return Result.Cancelled;
                }

                // BAC for un-cost-loaded tasks: the frozen contract sum + agreed VOs.
                double bac = 0;
                try
                {
                    var boq = BOQCostManager.BuildBOQDocument(doc);
                    bac = ContractSumResolver.Resolve(doc, boq, out _);
                }
                catch (Exception ex) { StingLog.Warn($"SCurve BAC: {ex.Message}"); }

                var curve = CashFlowSCurve.Build(model, bac);
                if (curve.Points.Count == 0)
                {
                    StingResultPanel.Create("Cash-flow S-curve")
                        .AddSection("FLAT").Text(string.Join("\n", curve.Warnings)).Show();
                    return Result.Cancelled;
                }

                // Persist the curve so EVM can read its PV at the valuation date.
                SCurveStore.Save(doc, curve);

                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_SCurve", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("Month,PlannedThisMonth,PlannedCumulative,EarnedThisMonth,EarnedCumulative,PlannedPct,EarnedPct");
                foreach (var p in curve.Points)
                    sb.AppendLine($"{p.MonthLabel},{p.PlannedThisMonth:F0},{p.PlannedCumulative:F0}," +
                                  $"{p.EarnedThisMonth:F0},{p.EarnedCumulative:F0},{p.PlannedPercent:F1},{p.EarnedPercent:F1}");
                File.WriteAllText(csv, sb.ToString());

                double pvNow = curve.PlannedValueAt(DateTime.UtcNow);
                StingResultPanel.Create("Schedule-driven cash-flow S-curve")
                    .AddSection("CURVE")
                    .Metric("Total value", $"UGX {curve.TotalValue:N0}")
                    .Metric("Months", curve.Points.Count.ToString())
                    .Metric("Start", curve.ProjectStart.ToString("yyyy-MM"))
                    .Metric("Finish", curve.ProjectFinish.ToString("yyyy-MM"))
                    .AddSection("PLANNED VALUE")
                    .Metric("PV at today", $"UGX {pvNow:N0}")
                    .Text("This is the REAL time-phased Planned Value — EVM (Evm_Calculate) now reads "
                        + "PV off this curve instead of a hand-keyed planned %.")
                    .Text($"CSV: {Path.GetFileName(csv)}")
                    .Show();
                StingLog.Info($"S-curve built: {curve.Points.Count} months, total {curve.TotalValue:N0}, PV today {pvNow:N0}.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Sched_SCurve", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    /// <summary>Persists the latest S-curve so EVM can read PV at a date without
    /// rebuilding. <project>/_BIM_COORD/cash_flow_scurve.json.</summary>
    internal static class SCurveStore
    {
        private static string PathFor(Document doc)
        {
            try
            {
                string parent = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", "cash_flow_scurve.json");
            }
            catch { return null; }
        }

        public static void Save(Document doc, SCurveResult curve)
        {
            try
            {
                string p = PathFor(doc);
                if (p == null || curve == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, JsonConvert.SerializeObject(curve, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"SCurveStore.Save: {ex.Message}"); }
        }

        public static SCurveResult Load(Document doc)
        {
            try
            {
                string p = PathFor(doc);
                if (p == null || !File.Exists(p)) return null;
                return JsonConvert.DeserializeObject<SCurveResult>(File.ReadAllText(p));
            }
            catch (Exception ex) { StingLog.Warn($"SCurveStore.Load: {ex.Message}"); return null; }
        }
    }
}
