// ══════════════════════════════════════════════════════════════════════════
//  ScheduleCpmBridge.cs — wire the unified schedule into the CPM engine. PM-4.
//
//  CpmEngine (PM-4 core) is pure topology + day arithmetic. This bridge:
//    • maps each ScheduleTask → CpmTask (duration in WORKING days via the Uganda
//      WorkingCalendar, predecessors resolved by MsUid OR Id);
//    • runs CpmEngine.Solve;
//    • projects the result back: TotalFloat / FreeFloat / IsCritical, plus
//      EARLY/LATE calendar dates rolled through the working calendar so the
//      caller can stamp them and detect baseline-vs-actual variance.
//
//  Kept pure (operates on the in-memory ScheduleModel, no Revit) so it is unit-
//  tested headlessly. The Revit-coupled command merely loads the model from
//  ScheduleStore, calls this, and stamps the per-task float/critical params.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.Scheduling;

namespace StingTools.Core.Schedule
{
    /// <summary>Per-task CPM output projected back onto the schedule, keyed by
    /// ScheduleTask.Id.</summary>
    public class TaskCpmResult
    {
        public int TaskId { get; set; }
        public double TotalFloatDays { get; set; }
        public double FreeFloatDays { get; set; }
        public bool IsCritical { get; set; }
        public DateTime EarlyStart { get; set; }
        public DateTime EarlyFinish { get; set; }
        public DateTime LateStart { get; set; }
        public DateTime LateFinish { get; set; }
    }

    public class ScheduleCpmResult
    {
        public Dictionary<int, TaskCpmResult> ByTask { get; set; } = new Dictionary<int, TaskCpmResult>();
        public List<int> CriticalPath { get; set; } = new List<int>();
        public double ProjectDurationWorkingDays { get; set; }
        public DateTime ProjectStart { get; set; }
        public DateTime ProjectFinish { get; set; }
        public bool HasCycle { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class ScheduleCpmBridge
    {
        public static ScheduleCpmResult Solve(ScheduleModel model, WorkingCalendarConfig cal = null)
        {
            var outp = new ScheduleCpmResult();
            if (model == null || model.Tasks == null || model.Tasks.Count == 0) return outp;
            cal ??= new WorkingCalendarConfig();

            // Exclude summary rows from the CPM network — they roll up children, they
            // aren't worked. Keep an id index that resolves either MsUid or Id.
            var leaves = model.Tasks.Where(t => !t.IsSummary).ToList();
            if (leaves.Count == 0) leaves = model.Tasks.ToList();

            var idOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in leaves)
            {
                idOf[t.Id.ToString()] = t.Id;
                if (!string.IsNullOrEmpty(t.MsUid)) idOf[t.MsUid] = t.Id;
            }

            DateTime projStart = leaves.Min(t => t.Start).Date;
            outp.ProjectStart = projStart;

            var cpmTasks = new List<CpmTask>();
            foreach (var t in leaves)
            {
                double dur = Math.Max(1, WorkingCalendar.WorkingDaysBetween(t.Start, t.End, cal));
                var ct = new CpmTask { Id = t.Id.ToString(), Name = t.Name, DurationDays = dur };
                foreach (var p in t.Predecessors ?? new List<SchedulePredecessor>())
                {
                    // Only finish-to-start links drive the pure CPM pass; flag the
                    // others rather than treating them silently as FS.
                    if (!string.Equals(p.Type, "FS", StringComparison.OrdinalIgnoreCase))
                        outp.Warnings.Add($"Task {t.Id} '{t.Name}': {p.Type} link to {p.TaskId} treated as FS (only FS drives float).");
                    if (idOf.TryGetValue(p.TaskId ?? "", out int pid))
                        ct.PredecessorIds.Add(pid.ToString());
                }
                cpmTasks.Add(ct);
            }

            var res = CpmEngine.Solve(cpmTasks);
            outp.HasCycle = res.HasCycle;
            outp.Warnings.AddRange(res.Warnings);
            outp.ProjectDurationWorkingDays = res.ProjectDurationDays;

            foreach (var ct in res.Tasks)
            {
                if (!int.TryParse(ct.Id, out int tid)) continue;
                outp.ByTask[tid] = new TaskCpmResult
                {
                    TaskId = tid,
                    TotalFloatDays = ct.TotalFloat,
                    FreeFloatDays = ct.FreeFloat,
                    IsCritical = ct.IsCritical,
                    // Roll the working-day offsets back into calendar dates.
                    EarlyStart = WorkingCalendar.AddWorkingDays(projStart, (int)Math.Round(ct.EarlyStart) + 1, cal),
                    EarlyFinish = WorkingCalendar.AddWorkingDays(projStart, (int)Math.Round(ct.EarlyFinish), cal),
                    LateStart = WorkingCalendar.AddWorkingDays(projStart, (int)Math.Round(ct.LateStart) + 1, cal),
                    LateFinish = WorkingCalendar.AddWorkingDays(projStart, (int)Math.Round(ct.LateFinish), cal),
                };
            }
            outp.CriticalPath = res.CriticalPath.Select(s => int.TryParse(s, out int v) ? v : -1)
                                                .Where(v => v >= 0).ToList();
            outp.ProjectFinish = WorkingCalendar.AddWorkingDays(projStart, (int)Math.Round(res.ProjectDurationDays), cal);
            return outp;
        }
    }
}
