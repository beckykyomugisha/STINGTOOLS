// SlaScanner.cs — template engine v1.1 (S15).
//
// Opportunistic SLA checker: called on BCC open, tab switch, and every
// dispatch; not a real-time timer. Emits AuditLog entries on breach and
// triggers registered escalation actions.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace Planscape.Docs.Workflow
{
    public static class SlaScanner
    {
        private static DateTime _lastRan = DateTime.MinValue;

        public static List<SlaBreach> Scan(Document doc, TimeSpan? minInterval = null)
        {
            if (minInterval.HasValue && (DateTime.UtcNow - _lastRan) < minInterval.Value)
                return new List<SlaBreach>();
            _lastRan = DateTime.UtcNow;

            List<SlaBreach> breaches;
            try { breaches = WorkflowEngine.CheckSlaBreaches(doc); }
            catch (Exception ex)
            {
                StingLog.Warn($"SlaScanner: CheckSlaBreaches failed: {ex.Message}");
                return new List<SlaBreach>();
            }

            foreach (var b in breaches)
            {
                try
                {
                    AuditLog.Append(doc, "wf.sla_breach", b.DocId, new JObject
                    {
                        ["instance_id"]      = b.InstanceId,
                        ["state"]            = b.State,
                        ["overdue_by_hours"] = b.OverdueByHours,
                        ["escalation"]       = b.NextEscalation != null
                            ? JObject.FromObject(b.NextEscalation) : null
                    });

                    if (b.NextEscalation != null) ApplyEscalation(doc, b);
                }
                catch (Exception ex2) { StingLog.Warn($"SlaScanner breach handler: {ex2.Message}"); }
            }
            return breaches;
        }

        private static void ApplyEscalation(Document doc, SlaBreach b)
        {
            // Currently emits audit entries; actual notification wiring is
            // intentionally left to downstream integrations (email, SignalR,
            // Planscape Server). See docs/ROADMAP.md v1.2 for the full plan.
            var action = b.NextEscalation?.Action?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action)) return;

            AuditLog.Append(doc, "wf.escalation", b.DocId, new JObject
            {
                ["action"] = action,
                ["to"]     = b.NextEscalation.To,
                ["state"]  = b.State
            });
        }
    }
}
