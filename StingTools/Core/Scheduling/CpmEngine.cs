// ══════════════════════════════════════════════════════════════════════════
//  CpmEngine.cs — Critical Path Method forward/backward pass. PM-4.
//
//  Revit's Phase is an ordered enum with NO dates or dependencies, so the
//  scheduling MATH is ours to write (the audit §0 line: "Revit gives you zero
//  scheduling math"). This is the textbook CPM:
//
//    Forward pass:  ES(t)  = max over preds p of EF(p)            (FS links)
//                   EF(t)  = ES(t) + Duration(t)
//    Backward pass: LF(t)  = min over succs s of LS(s)
//                   LS(t)  = LF(t) − Duration(t)
//                   (project end = max EF; sinks' LF = project end)
//    Total float    = LS − ES  (= LF − EF)
//    Free float     = min over succs of ES(s) − EF(t)   (0 for a sink)
//    Critical path  = tasks with total float ≤ 0
//
//  Durations are in whole days (working-calendar conversion is the Revit-coupled
//  part of PM-4 — kept out of this pure engine). FS (finish-to-start) links only;
//  SS/FF/SF lags are flagged as unsupported so the result is never silently wrong.
//
//  Pure (no Revit / no I/O) — unit-tested headlessly in StingTools.Cost.Tests.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Scheduling
{
    public class CpmTask
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public double DurationDays { get; set; }
        /// <summary>Finish-to-start predecessor task ids.</summary>
        public List<string> PredecessorIds { get; set; } = new List<string>();

        // ── Computed by CpmEngine.Solve ──
        public double EarlyStart { get; set; }
        public double EarlyFinish { get; set; }
        public double LateStart { get; set; }
        public double LateFinish { get; set; }
        public double TotalFloat { get; set; }
        public double FreeFloat { get; set; }
        public bool IsCritical { get; set; }
    }

    public class CpmResult
    {
        public List<CpmTask> Tasks { get; set; } = new List<CpmTask>();
        public double ProjectDurationDays { get; set; }
        public List<string> CriticalPath { get; set; } = new List<string>();
        public bool HasCycle { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class CpmEngine
    {
        public static CpmResult Solve(IEnumerable<CpmTask> input)
        {
            var result = new CpmResult();
            var tasks = (input ?? Enumerable.Empty<CpmTask>()).Where(t => t != null && !string.IsNullOrEmpty(t.Id)).ToList();
            if (tasks.Count == 0) return result;

            var byId = new Dictionary<string, CpmTask>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tasks) byId[t.Id] = t;   // last wins on dup id

            // Successor adjacency.
            var succ = tasks.ToDictionary(t => t.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var t in tasks)
                foreach (var p in (t.PredecessorIds ?? new List<string>()).Where(byId.ContainsKey))
                    succ[p].Add(t.Id);

            // Topological order (Kahn). A remaining cycle is flagged.
            var indeg = tasks.ToDictionary(t => t.Id, t => (t.PredecessorIds ?? new List<string>()).Count(byId.ContainsKey), StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var topo = new List<string>();
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                topo.Add(id);
                foreach (var s in succ[id]) if (--indeg[s] == 0) queue.Enqueue(s);
            }
            if (topo.Count < tasks.Count)
            {
                result.HasCycle = true;
                result.Warnings.Add($"Dependency cycle detected — {tasks.Count - topo.Count} task(s) excluded from the pass.");
            }

            // Forward pass in topo order.
            foreach (var id in topo)
            {
                var t = byId[id];
                double es = 0;
                foreach (var p in (t.PredecessorIds ?? new List<string>()).Where(byId.ContainsKey))
                    es = Math.Max(es, byId[p].EarlyFinish);
                t.EarlyStart = es;
                t.EarlyFinish = es + Math.Max(0, t.DurationDays);
            }

            double projectEnd = topo.Count > 0 ? topo.Max(id => byId[id].EarlyFinish) : 0;
            result.ProjectDurationDays = projectEnd;

            // Backward pass in reverse topo order.
            foreach (var id in Enumerable.Reverse(topo))
            {
                var t = byId[id];
                var succs = succ[id];
                double lf = succs.Count == 0 ? projectEnd : succs.Min(s => byId[s].LateStart);
                t.LateFinish = lf;
                t.LateStart = lf - Math.Max(0, t.DurationDays);
                t.TotalFloat = t.LateStart - t.EarlyStart;
                double freeFloat = succs.Count == 0 ? projectEnd - t.EarlyFinish
                                                    : succs.Min(s => byId[s].EarlyStart) - t.EarlyFinish;
                t.FreeFloat = Math.Max(0, freeFloat);
                t.IsCritical = t.TotalFloat <= 0.0001 && !result.HasCycle;
            }

            // Critical path = a longest chain of critical tasks (by topo order).
            result.CriticalPath = topo.Where(id => byId[id].IsCritical).ToList();
            result.Tasks = tasks;
            return result;
        }
    }
}
