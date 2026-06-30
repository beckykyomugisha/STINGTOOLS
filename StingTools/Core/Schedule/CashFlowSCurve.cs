// ══════════════════════════════════════════════════════════════════════════
//  CashFlowSCurve.cs — schedule-driven cash-flow S-curve (real PV). PM-3.
//
//  The audit (§3) found the cash-flow generator spreads the GRAND TOTAL ONLY over
//  a fixed sigmoid, ignoring per-task start/finish/value — so a front-loaded and a
//  back-loaded programme give the SAME curve, and EVM PV was hand-keyed ("No 4D
//  wiring yet"). There was no true time-phased Planned Value.
//
//  This engine time-phases each task's value across its OWN start→finish window
//  (linear spread by working-day overlap with each month), then accumulates into a
//  monthly cumulative — the genuine Planned Value (BCWS) curve. Actual cumulative
//  is the same spread weighted by per-task % complete (the model-driven earned
//  value). PV at a given "as of" date is read straight off this curve and feeds
//  EvmCalculator.
//
//  Per-task value precedence: CostLoadUGX when set (the 5D cost-loaded baseline);
//  otherwise BAC × (task working-days / Σ working-days) so an un-cost-loaded
//  programme still produces a defensible curve.
//
//  Pure (no Revit / no I/O) — unit-tested in StingTools.Scheduling.Tests.
//  All money rounds through MoneyRound (half-even).
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Schedule
{
    public class SCurvePoint
    {
        public DateTime MonthEnd { get; set; }
        public string MonthLabel { get; set; } = "";
        public double PlannedThisMonth { get; set; }
        public double PlannedCumulative { get; set; }
        public double EarnedThisMonth { get; set; }
        public double EarnedCumulative { get; set; }
        public double PlannedPercent { get; set; }   // cumulative planned / total × 100
        public double EarnedPercent { get; set; }     // cumulative earned / total × 100
    }

    public class SCurveResult
    {
        public List<SCurvePoint> Points { get; set; } = new List<SCurvePoint>();
        public double TotalValue { get; set; }
        public DateTime ProjectStart { get; set; }
        public DateTime ProjectFinish { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>Cumulative PLANNED value (PV / BCWS) at an "as of" date — the
        /// EVM Planned Value. Interpolates linearly between month points.</summary>
        public double PlannedValueAt(DateTime asOf)
        {
            if (Points.Count == 0) return 0;
            if (asOf <= ProjectStart) return 0;
            if (asOf >= Points[^1].MonthEnd) return Points[^1].PlannedCumulative;
            for (int i = 0; i < Points.Count; i++)
            {
                if (asOf <= Points[i].MonthEnd)
                {
                    double prevCum = i == 0 ? 0 : Points[i - 1].PlannedCumulative;
                    DateTime prevEnd = i == 0 ? ProjectStart : Points[i - 1].MonthEnd;
                    double span = (Points[i].MonthEnd - prevEnd).TotalDays;
                    double frac = span > 0 ? (asOf - prevEnd).TotalDays / span : 1.0;
                    return MoneyRound.Round(prevCum + frac * (Points[i].PlannedCumulative - prevCum));
                }
            }
            return Points[^1].PlannedCumulative;
        }
    }

    public static class CashFlowSCurve
    {
        /// <summary>Build the schedule-driven S-curve. <paramref name="bac"/> is used
        /// to derive per-task value for tasks with no CostLoadUGX.</summary>
        public static SCurveResult Build(ScheduleModel model, double bac)
        {
            var r = new SCurveResult();
            if (model == null || model.Tasks == null) return r;
            var tasks = model.Tasks.Where(t => !t.IsSummary && t.End >= t.Start).ToList();
            if (tasks.Count == 0) return r;

            DateTime projStart = tasks.Min(t => t.Start).Date;
            DateTime projFinish = tasks.Max(t => t.End).Date;
            r.ProjectStart = projStart;
            r.ProjectFinish = projFinish;

            // Per-task value: CostLoadUGX, else BAC × working-day share.
            double totalDurDays = tasks.Sum(t => Math.Max(1, (t.End - t.Start).TotalDays + 1));
            double Value(ScheduleTask t)
            {
                if (t.CostLoadUGX > 0) return t.CostLoadUGX;
                if (bac <= 0 || totalDurDays <= 0) return 0;
                double share = (Math.Max(1, (t.End - t.Start).TotalDays + 1)) / totalDurDays;
                return bac * share;
            }
            double total = tasks.Sum(Value);
            r.TotalValue = MoneyRound.Round(total);
            if (total <= 0) { r.Warnings.Add("No cost-loaded or BAC-derived value — S-curve is flat."); return r; }

            // Month buckets from projStart to projFinish (inclusive).
            var months = new List<DateTime>();
            var cursor = new DateTime(projStart.Year, projStart.Month, 1);
            var last = new DateTime(projFinish.Year, projFinish.Month, 1);
            while (cursor <= last) { months.Add(cursor); cursor = cursor.AddMonths(1); }

            double cumPlanned = 0, cumEarned = 0;
            foreach (var m in months)
            {
                DateTime mStart = m;
                DateTime mEnd = m.AddMonths(1).AddDays(-1);
                double planned = 0, earned = 0;
                foreach (var t in tasks)
                {
                    double overlap = DayOverlap(t.Start, t.End, mStart, mEnd);
                    if (overlap <= 0) continue;
                    double taskDays = Math.Max(1, (t.End - t.Start).TotalDays + 1);
                    double frac = overlap / taskDays;        // share of the task in this month
                    double v = Value(t) * frac;
                    planned += v;
                    earned += v * (t.PercentComplete / 100.0);
                }
                planned = MoneyRound.Round(planned);
                earned = MoneyRound.Round(earned);
                cumPlanned = MoneyRound.Round(cumPlanned + planned);
                cumEarned = MoneyRound.Round(cumEarned + earned);
                r.Points.Add(new SCurvePoint
                {
                    MonthEnd = mEnd,
                    MonthLabel = m.ToString("MMM yyyy"),
                    PlannedThisMonth = planned,
                    PlannedCumulative = cumPlanned,
                    EarnedThisMonth = earned,
                    EarnedCumulative = cumEarned,
                    PlannedPercent = total > 0 ? MoneyRound.Round(cumPlanned / total * 100.0, 2) : 0,
                    EarnedPercent = total > 0 ? MoneyRound.Round(cumEarned / total * 100.0, 2) : 0,
                });
            }
            // Snap the final cumulative to the exact total (float residue guard).
            if (r.Points.Count > 0)
            {
                r.Points[^1].PlannedCumulative = r.TotalValue;
                r.Points[^1].PlannedPercent = 100.0;
            }
            return r;
        }

        /// <summary>Whole-day overlap (inclusive) between [aStart,aEnd] and
        /// [bStart,bEnd], in days; 0 when disjoint.</summary>
        private static double DayOverlap(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
        {
            DateTime s = aStart > bStart ? aStart : bStart;
            DateTime e = aEnd < bEnd ? aEnd : bEnd;
            if (e < s) return 0;
            return (e.Date - s.Date).TotalDays + 1;
        }
    }
}
