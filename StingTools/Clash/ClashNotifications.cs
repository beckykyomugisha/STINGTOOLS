// ClashNotifications.cs — F10. Append-only sidecar JSONL of notification-
// worthy clash events. Local-first: written to {output}/clash_notifications.jsonl
// without any network coupling. A future Planscape server adapter (or a
// client-side push agent) can tail this file and forward to FCM / SignalR /
// SMTP / Slack — the contract is just "newline-delimited JSON objects, one
// notification per line".
//
// Triggered from ClashRunCommand after SeedFromRun for every CRITICAL or
// HIGH severity clash whose state is "New" or "Reintroduced". One event per
// clash, deduplicated within a single run by clash Id.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Clash
{
    public sealed class ClashNotificationEvent
    {
        public DateTime AtUtc { get; set; }
        public string Kind { get; set; }   // "critical_new" | "critical_reintroduced" | "severity_escalated" | "stage_gate_breach"
        public string ClashId { get; set; }
        public string GroupId { get; set; }
        public string Severity { get; set; }
        public string PriorSeverity { get; set; }   // populated for severity_escalated
        public string MatrixPair { get; set; }
        public string State { get; set; }
        public string Assignee { get; set; }
        public int RecurrenceCount { get; set; }
        public double TriageScore { get; set; }
        public int ElementAId { get; set; }
        public int ElementBId { get; set; }
        public string ProjectGuid { get; set; }
    }

    public static class ClashNotifications
    {
        public const string DefaultFileName = "clash_notifications.jsonl";

        /// <summary>
        /// F10: Append every notification-worthy event in this run to the
        /// sidecar JSONL. Caller (ClashRunCommand) is responsible for picking
        /// what counts as "worthy" — typically CRITICAL/HIGH new + escalated.
        /// </summary>
        public static int Append(string outDir, IEnumerable<ClashNotificationEvent> events)
        {
            if (string.IsNullOrEmpty(outDir) || events == null) return 0;
            try
            {
                Directory.CreateDirectory(outDir);
                string path = Path.Combine(outDir, DefaultFileName);
                int n = 0;
                using var writer = File.AppendText(path);
                foreach (var e in events)
                {
                    if (e == null) continue;
                    writer.WriteLine(JsonConvert.SerializeObject(e));
                    n++;
                }
                if (n > 0)
                    StingLog.Info($"ClashNotifications: appended {n} events → {path}");
                return n;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashNotifications.Append: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// F10: Build the list of notifications for a run. Centralised so
        /// callers (ClashRunCommand, F11 stage-gate command) emit consistent
        /// event payloads. Severity escalation is detected by reading the
        /// last StateTransition.To text — set by ClashHistory.MergeWithPrior.
        /// </summary>
        public static List<ClashNotificationEvent> BuildFromRun(ClashRunRecord run, string projectGuid)
        {
            var result = new List<ClashNotificationEvent>();
            if (run?.Clashes == null) return result;
            var groupAssignee = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var g in run.Groups ?? new List<ClashGroupRecord>())
                if (!string.IsNullOrEmpty(g.Id)) groupAssignee[g.Id] = g.Assignee ?? "";
            foreach (var c in run.Clashes)
            {
                bool isCritical = c.Severity == "CRITICAL" || c.Severity == "HIGH";
                bool isFresh = c.State == "New" || c.State == "Reintroduced";
                bool isEscalated = c.StateHistory != null && c.StateHistory
                    .Any(h => (h.To ?? "").Contains("Severity escalated"));
                if (!isCritical && !isEscalated) continue;
                if (!isFresh && !isEscalated) continue;
                groupAssignee.TryGetValue(c.GroupId ?? "", out var assignee);
                string priorSev = isEscalated ? ExtractPriorSeverity(c.StateHistory) : null;
                result.Add(new ClashNotificationEvent
                {
                    AtUtc = DateTime.UtcNow,
                    Kind = isEscalated ? "severity_escalated" :
                           (c.State == "Reintroduced" ? "critical_reintroduced" : "critical_new"),
                    ClashId = c.Id,
                    GroupId = c.GroupId,
                    Severity = c.Severity,
                    PriorSeverity = priorSev,
                    MatrixPair = c.MatrixPairId,
                    State = c.State,
                    Assignee = assignee,
                    RecurrenceCount = c.RecurrenceCount,
                    TriageScore = c.TriageScore,
                    ElementAId = c.ElementA?.ElementId ?? 0,
                    ElementBId = c.ElementB?.ElementId ?? 0,
                    ProjectGuid = projectGuid,
                });
            }
            return result;
        }

        /// <summary>
        /// F10: Pull "Severity escalated X → Y" out of the StateHistory if
        /// the F1 promotion ran this tick. Returns the X half (prior tier).
        /// </summary>
        private static string ExtractPriorSeverity(List<StateTransition> history)
        {
            if (history == null) return null;
            foreach (var h in history)
            {
                var to = h?.To ?? "";
                int escIdx = to.IndexOf("Severity escalated ", StringComparison.Ordinal);
                if (escIdx < 0) continue;
                int arrowIdx = to.IndexOf(" → ", escIdx, StringComparison.Ordinal);
                if (arrowIdx < 0) continue;
                int start = escIdx + "Severity escalated ".Length;
                return to.Substring(start, arrowIdx - start).Trim();
            }
            return null;
        }
    }
}
