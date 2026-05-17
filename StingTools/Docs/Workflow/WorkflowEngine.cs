// WorkflowEngine.cs — template engine v1.1 (S15).
//
// Runtime engine. State persisted to _BIM_COORD/workflow_state.json.
// Transitions validate role + allowed action against the loaded
// WorkflowDefinition; history rows are appended and an AuditLog entry is
// recorded for each hop.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace Planscape.Docs.Workflow
{
    public static class WorkflowEngine
    {
        private static readonly object _lock = new object();

        public static string Start(Document doc, string workflowId, string docId)
        {
            lock (_lock)
            {
                var reg = WorkflowRegistry.Load(doc);
                var wf = reg.Get(workflowId);
                if (wf == null) throw new InvalidOperationException($"Workflow '{workflowId}' not registered.");

                var now = DateTime.UtcNow;
                var inst = new WorkflowInstance
                {
                    Id = Guid.NewGuid().ToString("N"),
                    WorkflowId = wf.Id,
                    DocId = docId,
                    State = wf.StartState ?? wf.States.FirstOrDefault()?.Name,
                    StartedAt = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    StateEnteredAt = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    SlaDeadline = ComputeSlaDeadline(wf, wf.StartState, now)
                };
                var store = LoadStore(doc);
                store.Add(inst);
                SaveStore(doc, store);

                AuditLog.Append(doc, "wf.started", docId, new JObject
                {
                    ["workflow_id"] = wf.Id, ["instance_id"] = inst.Id, ["state"] = inst.State
                });
                return inst.Id;
            }
        }

        public static WorkflowInstance Transition(Document doc, string docId, string action, string byUser, string comment)
        {
            lock (_lock)
            {
                var reg = WorkflowRegistry.Load(doc);
                var store = LoadStore(doc);
                var inst = store.FirstOrDefault(i => string.Equals(i.DocId, docId, StringComparison.Ordinal) && !i.Closed);
                if (inst == null) throw new InvalidOperationException($"No open workflow instance for doc '{docId}'.");

                var wf = reg.Get(inst.WorkflowId);
                if (wf == null) throw new InvalidOperationException($"Workflow definition '{inst.WorkflowId}' missing.");

                var transition = wf.Transitions.FirstOrDefault(t =>
                    string.Equals(t.From, inst.State, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(t.Action, action,    StringComparison.OrdinalIgnoreCase));
                if (transition == null)
                    throw new InvalidOperationException($"Action '{action}' not valid from state '{inst.State}'.");

                string from = inst.State;
                inst.State = transition.To;
                var now = DateTime.UtcNow;
                inst.StateEnteredAt = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
                inst.SlaDeadline = ComputeSlaDeadline(wf, transition.To, now);
                inst.History.Add(new WorkflowHistoryRow
                {
                    Ts = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    FromState = from, ToState = transition.To,
                    Action = action, User = byUser, Comment = comment
                });
                var targetState = wf.States.FirstOrDefault(s => string.Equals(s.Name, transition.To, StringComparison.OrdinalIgnoreCase));
                if (targetState?.IsTerminal == true) inst.Closed = true;

                SaveStore(doc, store);

                AuditLog.Append(doc, "wf.transitioned", docId, new JObject
                {
                    ["workflow_id"] = wf.Id,
                    ["instance_id"] = inst.Id,
                    ["from"]        = from,
                    ["to"]          = transition.To,
                    ["action"]      = action,
                    ["user"]        = byUser,
                    ["comment"]     = comment
                });
                return inst;
            }
        }

        public static WorkflowInstance GetInstance(Document doc, string docId)
        {
            var store = LoadStore(doc);
            return store.FirstOrDefault(i => string.Equals(i.DocId, docId, StringComparison.Ordinal) && !i.Closed);
        }

        public static List<WorkflowInstance> GetMyQueue(Document doc, string userEmail)
        {
            var store = LoadStore(doc);
            return store
                .Where(i => !i.Closed && !string.IsNullOrEmpty(i.AssignedTo) &&
                            string.Equals(i.AssignedTo, userEmail, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.SlaDeadline ?? string.Empty)
                .ToList();
        }

        public static List<SlaBreach> CheckSlaBreaches(Document doc)
        {
            var reg = WorkflowRegistry.Load(doc);
            var store = LoadStore(doc);
            var now = DateTime.UtcNow;
            var result = new List<SlaBreach>();

            foreach (var inst in store.Where(i => !i.Closed))
            {
                if (string.IsNullOrEmpty(inst.SlaDeadline)) continue;
                if (!DateTime.TryParse(inst.SlaDeadline, out var deadline)) continue;
                if (deadline > now) continue;

                var wf = reg.Get(inst.WorkflowId);
                var state = wf?.States.FirstOrDefault(s => string.Equals(s.Name, inst.State, StringComparison.OrdinalIgnoreCase));
                var next = state?.Escalations
                    .OrderBy(e => e.AfterHours)
                    .FirstOrDefault(e => now >= deadline.AddHours(e.AfterHours));

                int hoursOverdue = (int)(now - deadline).TotalHours;
                result.Add(new SlaBreach
                {
                    InstanceId = inst.Id,
                    DocId = inst.DocId,
                    State = inst.State,
                    OverdueByHours = hoursOverdue,
                    NextEscalation = next
                });
            }
            return result;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string ComputeSlaDeadline(WorkflowDefinition wf, string stateName, DateTime enteredAt)
        {
            var s = wf?.States.FirstOrDefault(x => string.Equals(x.Name, stateName, StringComparison.OrdinalIgnoreCase));
            if (s?.SlaHours == null || s.SlaHours <= 0) return null;
            return enteredAt.AddHours(s.SlaHours.Value).ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        private static string StorePath(Document doc)
        {
            string root = ResolveProjectRoot(doc);
            string dir  = Path.Combine(root, "_BIM_COORD");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "workflow_state.json");
        }

        private static List<WorkflowInstance> LoadStore(Document doc)
        {
            string path = StorePath(doc);
            if (!File.Exists(path)) return new List<WorkflowInstance>();
            try
            {
                return JsonConvert.DeserializeObject<List<WorkflowInstance>>(File.ReadAllText(path))
                       ?? new List<WorkflowInstance>();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"WorkflowEngine: workflow_state.json unreadable — starting fresh: {ex.Message}");
                return new List<WorkflowInstance>();
            }
        }

        private static void SaveStore(Document doc, List<WorkflowInstance> store)
        {
            string path = StorePath(doc);
            string tmp  = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(store, Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static string ResolveProjectRoot(Document doc)
        {
            try
            {
                string p = doc?.PathName;
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { /* ignored */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord");
        }
    }
}
