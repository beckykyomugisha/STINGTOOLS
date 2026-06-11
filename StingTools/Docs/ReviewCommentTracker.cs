using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Docs
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (C3) — Bluebeam Studio comment close-out tracker (pure logic).
    //
    // Owner review on Bluebeam Studio is a phase-completion gate (A1) + a monthly
    // KPI (proposal §4.6). This is the Revit/IO-free core: upsert (re-import =
    // merge with first-seen/last-seen), status normalisation, age, close-out
    // rate, and per-gate KPI. The command (ReviewCommentCommands) does the
    // CSV/XLSX parse, the JSON persistence, and the dashboard.
    // ─────────────────────────────────────────────────────────────────────────

    public enum ReviewStatus
    {
        Open,
        Answered,
        ResolvedPendingOwner,
        Closed
    }

    public class ReviewComment
    {
        public string SessionId { get; set; } = "";
        public string CommentId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string PageLabel { get; set; } = "";
        public string Author { get; set; } = "";
        public string Date { get; set; } = "";          // raw date string from Bluebeam
        public ReviewStatus Status { get; set; } = ReviewStatus.Open;
        public string RawStatus { get; set; } = "";
        public int ReplyCount { get; set; }
        public string Gate { get; set; } = "";           // Deliverable A/B/C/conformed — picked at import
        public string Owner { get; set; } = "";          // assignable in the dashboard
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }

        public string Key => SessionId + "|" + CommentId;
    }

    public class ReviewCommentStore
    {
        public List<ReviewComment> Comments { get; set; } = new List<ReviewComment>();
    }

    public class ReviewKpiRow
    {
        public string Gate { get; set; } = "";
        public int Total { get; set; }
        public int Closed { get; set; }
        public double CloseOutPct { get; set; }
        public double MeanAgeDays { get; set; }
        public int Overdue { get; set; }
    }

    public static class ReviewCommentTracker
    {
        public static bool IsClosed(ReviewStatus s) => s == ReviewStatus.Closed;

        /// <summary>Map a raw Bluebeam status string to a canonical ReviewStatus.</summary>
        public static ReviewStatus NormalizeStatus(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return ReviewStatus.Open;
            string s = raw.Trim().ToLowerInvariant();
            if (s.Contains("pending")) return ReviewStatus.ResolvedPendingOwner;
            if (s.Contains("closed") || s.Contains("complete") || s.Contains("accept") ||
                s.Contains("resolved") || s.Contains("done") || s.Contains("reject") || s.Contains("cancel"))
                return ReviewStatus.Closed;
            if (s.Contains("answer") || s.Contains("replied") || s.Contains("response"))
                return ReviewStatus.Answered;
            return ReviewStatus.Open;
        }

        /// <summary>
        /// Merge an incoming import batch into the existing tracked set, keyed by
        /// SessionId|CommentId. New rows get FirstSeen=LastSeen=importUtc; existing
        /// rows keep FirstSeen, refresh LastSeen + mutable fields, and preserve a
        /// previously-assigned Owner unless the import carries one.
        /// </summary>
        public static (int added, int updated) Upsert(
            List<ReviewComment> existing, IEnumerable<ReviewComment> incoming, DateTime importUtc)
        {
            var byKey = existing.ToDictionary(c => c.Key, c => c, StringComparer.Ordinal);
            int added = 0, updated = 0;
            foreach (var inc in incoming ?? Enumerable.Empty<ReviewComment>())
            {
                if (inc == null || string.IsNullOrEmpty(inc.CommentId)) continue;
                if (byKey.TryGetValue(inc.Key, out var cur))
                {
                    cur.Subject = inc.Subject;
                    cur.PageLabel = inc.PageLabel;
                    cur.Author = inc.Author;
                    cur.Date = inc.Date;
                    cur.Status = inc.Status;
                    cur.RawStatus = inc.RawStatus;
                    cur.ReplyCount = inc.ReplyCount;
                    if (!string.IsNullOrEmpty(inc.Gate)) cur.Gate = inc.Gate;
                    if (!string.IsNullOrEmpty(inc.Owner)) cur.Owner = inc.Owner; // import owner only overrides when present
                    cur.LastSeenUtc = importUtc;
                    updated++;
                }
                else
                {
                    inc.FirstSeenUtc = importUtc;
                    inc.LastSeenUtc = importUtc;
                    existing.Add(inc);
                    byKey[inc.Key] = inc;
                    added++;
                }
            }
            return (added, updated);
        }

        /// <summary>Days a comment has been live: first-seen → now (open) or → last-seen (closed).</summary>
        public static double AgeDays(ReviewComment c, DateTime nowUtc)
        {
            DateTime end = IsClosed(c.Status) ? c.LastSeenUtc : nowUtc;
            double d = (end - c.FirstSeenUtc).TotalDays;
            return d < 0 ? 0 : Math.Round(d, 1);
        }

        /// <summary>Closed / total × 100 over the supplied set (100 when empty).</summary>
        public static double CloseOutRate(IEnumerable<ReviewComment> comments)
        {
            var list = (comments ?? Enumerable.Empty<ReviewComment>()).ToList();
            if (list.Count == 0) return 100.0;
            int closed = list.Count(c => IsClosed(c.Status));
            return Math.Round(100.0 * closed / list.Count, 1);
        }

        /// <summary>Per-gate KPI rows + an "ALL" total row. Overdue = open and older than the SLA.</summary>
        public static List<ReviewKpiRow> BuildKpi(IEnumerable<ReviewComment> comments, DateTime nowUtc, int overdueSlaDays)
        {
            var list = (comments ?? Enumerable.Empty<ReviewComment>()).ToList();
            var rows = new List<ReviewKpiRow>();
            foreach (var grp in list.GroupBy(c => string.IsNullOrEmpty(c.Gate) ? "(unassigned)" : c.Gate)
                                     .OrderBy(g => g.Key))
                rows.Add(Kpi(grp.Key, grp.ToList(), nowUtc, overdueSlaDays));
            if (rows.Count > 0)
                rows.Add(Kpi("ALL", list, nowUtc, overdueSlaDays));
            return rows;
        }

        private static ReviewKpiRow Kpi(string gate, List<ReviewComment> g, DateTime nowUtc, int sla)
        {
            int closed = g.Count(c => IsClosed(c.Status));
            double meanAge = g.Count > 0 ? Math.Round(g.Average(c => AgeDays(c, nowUtc)), 1) : 0;
            int overdue = g.Count(c => !IsClosed(c.Status) && AgeDays(c, nowUtc) > sla);
            return new ReviewKpiRow
            {
                Gate = gate,
                Total = g.Count,
                Closed = closed,
                CloseOutPct = g.Count > 0 ? Math.Round(100.0 * closed / g.Count, 1) : 100.0,
                MeanAgeDays = meanAge,
                Overdue = overdue
            };
        }
    }
}
