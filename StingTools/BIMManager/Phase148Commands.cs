// Phase 148 — IExternalCommand surface for the engines.
//
// Each engine in Phase148Engine.cs gets a thin IExternalCommand wrapper
// here so users can run it from the dock panel / ribbon / command tags.
// The handlers are intentionally short — every line of business logic
// stays in the engine; the command body just gathers a doc, calls the
// engine, and renders the result in a TaskDialog (or CSV when the
// payload is large enough that a dialog would be unreadable).

using System;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.BIMManager
{
    // ── Rebar spacing pre-check ───────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RunRebarSpacingCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx?.Doc == null) { message = "No document open."; return Result.Failed; }
            var issues = RebarSpacingChecker.Check(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine($"Rebar spacing check (EC2 §8.2): {issues.Count} issue(s).");
            foreach (var i in issues.Take(50))
                sb.AppendLine($"  • {i.Id}: {i.Reason}");
            if (issues.Count > 50) sb.AppendLine($"  … and {issues.Count - 50} more.");
            TaskDialog.Show("STING — Rebar Spacing", sb.ToString());
            return Result.Succeeded;
        }
    }

    // ── MEP commissioning schedule mint ───────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateMepCommissioningSchedulesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx?.Doc == null) { message = "No document open."; return Result.Failed; }
            int created = MepCommissioningSchedules.CreateMissing(ctx.Doc);
            string defs = string.Join("\n  • ", MepCommissioningSchedules.All.Select(d => d.Name));
            TaskDialog.Show("STING — MEP Commissioning Schedules",
                $"Created {created} schedule(s).\n\nLibrary:\n  • {defs}");
            return Result.Succeeded;
        }
    }

    // ── Schedule field consistency check ──────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CheckScheduleFieldConsistencyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx?.Doc == null) { message = "No document open."; return Result.Failed; }
            var hits = ScheduleTemplateLib.CheckFieldConsistency(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine($"Cross-schedule field consistency: {hits.Count} inconsistent field(s).");
            foreach (var h in hits.Take(30))
            {
                sb.AppendLine($"\n• {h.FieldName}");
                foreach (var lk in h.LabelsBySchedule)
                    sb.AppendLine($"    \"{lk.Key}\" ← {string.Join(", ", lk.Value)}");
            }
            if (hits.Count > 30) sb.AppendLine($"\n… and {hits.Count - 30} more.");
            TaskDialog.Show("STING — Schedule Field Consistency", sb.ToString());
            return Result.Succeeded;
        }
    }

    // ── Team workload report ──────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TeamWorkloadReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx?.Doc == null) { message = "No document open."; return Result.Failed; }
            var rows = TeamWorkloadEngine.Build(ctx.Doc);
            if (rows.Count == 0)
            {
                TaskDialog.Show("STING — Team Workload", "No open issues — nothing to balance.");
                return Result.Succeeded;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"Open issue workload by assignee ({rows.Count} assignees):\n");
            sb.AppendLine($"{"Assignee",-25}{"Open",6}{"Critical",10}{"High",6}{"Overdue",10}{"Oldest(d)",12}");
            foreach (var r in rows)
                sb.AppendLine($"{r.Assignee,-25}{r.OpenTotal,6}{r.Critical,10}{r.High,6}{r.Overdue,10}{r.OldestDays ?? "-",12}");
            TaskDialog.Show("STING — Team Workload", sb.ToString());
            return Result.Succeeded;
        }
    }

    // ── Compliance forecast ───────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ComplianceForecastCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx?.Doc == null) { message = "No document open."; return Result.Failed; }
            var summary = ComplianceForecast.Build(ctx.Doc, targetPct: 80);
            string detail = summary.HasTrend
                ? $"Days to {summary.TargetPct:F0}%: {summary.DaysToTarget:F1}\n" +
                  $"Projected pct (30 d): {summary.ProjectedPct:F1}%"
                : "No forecast yet.";
            TaskDialog.Show("STING — Compliance Forecast", $"{summary.Caption}\n\n{detail}");
            return Result.Succeeded;
        }
    }

    // ── Data drop tracker editor ──────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DataDropStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(cd);
            if (ctx?.Doc == null) { message = "No document open."; return Result.Failed; }
            var milestones = DataDropTracker.Load(ctx.Doc);
            var scan = ComplianceScan.Scan(ctx.Doc);
            var sb = new StringBuilder();
            sb.AppendLine($"ISO 19650 Data Drop status (current compliance: {scan.CompliancePercent:F0}%):\n");
            foreach (var m in milestones)
            {
                string rag = DataDropTracker.Rag(m, scan.CompliancePercent);
                sb.AppendLine($"  [{rag}] {m.Id} — {m.Description}");
                if (!string.IsNullOrEmpty(m.PlannedDate)) sb.AppendLine($"        Planned: {m.PlannedDate}");
                if (!string.IsNullOrEmpty(m.ActualDate))  sb.AppendLine($"        Actual:  {m.ActualDate}");
                if (m.RequiredCompliancePct > 0)         sb.AppendLine($"        Required compliance: {m.RequiredCompliancePct}%");
            }
            sb.AppendLine($"\nEdit dates in {DataDropTracker.SidecarPath(ctx.Doc) ?? "(no project path)"}.");
            TaskDialog.Show("STING — Data Drop Tracker", sb.ToString());
            return Result.Succeeded;
        }
    }
}
