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
    /// <summary>
    /// Thrown when a transition's <c>allowed_roles</c> gate rejects the acting user. A distinct
    /// type so callers can react to a DENIAL specifically instead of substring-matching the
    /// message — and so an unrelated failure can never be mistaken for "permitted".
    /// </summary>
    public class WorkflowRoleDeniedException : InvalidOperationException
    {
        public WorkflowRoleDeniedException(string message) : base(message) { }
    }

    public static class WorkflowEngine
    {
        private static readonly object _lock = new object();

        /// <summary>
        /// Role gate for one transition. Empty/absent allowed_roles ⇒ any role; the Information
        /// Manager (K) and Coordinator (C) administer the CDE and are always permitted.
        /// </summary>
        private static bool IsRolePermitted(WorkflowTransition transition, out string actingRole)
        {
            actingRole = "?";
            try { actingRole = RoleBasedAccessControl.GetCurrentUserRole(); }
            catch (Exception ex)
            {
                // Fail CLOSED when the role cannot be resolved but the gate is active.
                StingLog.Warn($"IsRolePermitted: role lookup failed: {ex.Message}");
                return transition?.AllowedRoles == null || transition.AllowedRoles.Count == 0;
            }
            if (transition?.AllowedRoles == null || transition.AllowedRoles.Count == 0) return true;
            string role = actingRole;   // out params cannot be captured by the lambda below
            return string.Equals(role, "K", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "C", StringComparison.OrdinalIgnoreCase)
                || transition.AllowedRoles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Simulate a sequence of actions from the document's current state and report the first
        /// role denial WITHOUT mutating anything. Lets a caller that must walk several hops
        /// verify the whole path up front — otherwise an early hop is committed to disk and the
        /// later denial leaves the workflow ahead of the record it describes, with no rollback.
        /// Returns true when every hop is permitted (or the path can't be resolved, which the
        /// caller handles as a non-blocking condition).
        /// </summary>
        public static bool ValidatePath(Document doc, string docId, IEnumerable<string> actions, out string denyReason)
        {
            denyReason = null;
            if (actions == null) return true;
            var plan = actions.ToList();
            if (plan.Count == 0) return true;

            lock (_lock)
            {
                try
                {
                    var reg = WorkflowRegistry.Load(doc);
                    // SelectInstance — the same rule Transition uses, so validation and mutation
                    // can never be talking about different instances of the same document.
                    var inst = SelectInstance(LoadStore(doc), docId, false);
                    if (inst == null) return true;
                    var wf = reg.Get(inst.WorkflowId);
                    if (wf == null) return true;

                    string state = inst.State;
                    foreach (string action in plan)
                    {
                        var t = wf.Transitions.FirstOrDefault(x =>
                            string.Equals(x.From, state, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.Action, action, StringComparison.OrdinalIgnoreCase));
                        if (t == null)
                        {
                            // A single undefined hop stays non-blocking (Transition throws, the
                            // caller warns, nothing is half-written). But in a MULTI-hop plan an
                            // undefined hop means the plan disagrees with the workflow definition
                            // — proceeding would commit the earlier hops and then fail, which is
                            // exactly the partial-commit this method exists to prevent.
                            if (plan.Count == 1) return true;
                            denyReason = $"Action '{action}' is not defined from state '{state}' in workflow " +
                                         $"'{wf.Id}'. The {plan.Count}-step path was not started.";
                            return false;
                        }
                        if (!IsRolePermitted(t, out string role))
                        {
                            denyReason = $"Role '{role}' is not permitted to perform '{action}' from '{state}' " +
                                         $"(requires one of: {string.Join(", ", t.AllowedRoles)}).";
                            return false;
                        }
                        state = t.To;
                    }
                    return true;
                }
                catch (Exception ex) { StingLog.Warn($"ValidatePath: {ex.Message}"); return true; }
            }
        }

        /// <summary>
        /// Re-open a closed (terminal-state) instance when <paramref name="action"/> is a defined
        /// transition out of its current state, so terminal-state exits declared in the workflow
        /// JSON — Published→Archived via cancel/archive — can actually run. Returns false when no
        /// such transition exists, leaving the instance closed. Cancelling a published deliverable
        /// otherwise wrote Status=Cancelled with no workflow movement and no audit row.
        /// </summary>
        public static bool Reopen(Document doc, string docId, string action)
        {
            lock (_lock)
            {
                try
                {
                    var store = LoadStore(doc);
                    var inst = SelectInstance(store, docId, true);
                    if (inst == null) return false;
                    if (!inst.Closed) return true;

                    var wf = WorkflowRegistry.Load(doc).Get(inst.WorkflowId);
                    bool defined = wf?.Transitions.Any(t =>
                        string.Equals(t.From, inst.State, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(t.Action, action, StringComparison.OrdinalIgnoreCase)) == true;
                    if (!defined) return false;

                    inst.Closed = false;
                    SaveStore(doc, store);
                    try
                    {
                        AuditLog.Append(doc, "wf.reopened", docId, new JObject
                        {
                            ["workflow_id"] = inst.WorkflowId,
                            ["instance_id"] = inst.Id,
                            ["state"]       = inst.State,
                            ["for_action"]  = action
                        });
                    }
                    catch (Exception aex) { StingLog.Warn($"wf.reopened audit: {aex.Message}"); }
                    return true;
                }
                catch (Exception ex) { StingLog.Warn($"Reopen({docId}, {action}): {ex.Message}"); return false; }
            }
        }

        /// <summary>
        /// The one rule for picking a document's instance, shared by Transition, ValidatePath,
        /// Reopen and GetInstance. Latest-wins: pre-fix project files can contain several
        /// instances per document (the terminal-state leak), and three different selection rules
        /// meant the state that was planned from, validated and mutated could all differ.
        /// </summary>
        private static WorkflowInstance SelectInstance(List<WorkflowInstance> store, string docId, bool includeClosed)
        {
            if (store == null) return null;
            IEnumerable<WorkflowInstance> m = store.Where(i => string.Equals(i.DocId, docId, StringComparison.Ordinal));
            if (!includeClosed) m = m.Where(i => !i.Closed);
            return m.LastOrDefault();
        }

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
                var inst = SelectInstance(store, docId, false);
                if (inst == null) throw new InvalidOperationException($"No open workflow instance for doc '{docId}'.");

                var wf = reg.Get(inst.WorkflowId);
                if (wf == null) throw new InvalidOperationException($"Workflow definition '{inst.WorkflowId}' missing.");

                var transition = wf.Transitions.FirstOrDefault(t =>
                    string.Equals(t.From, inst.State, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(t.Action, action,    StringComparison.OrdinalIgnoreCase));
                if (transition == null)
                    throw new InvalidOperationException($"Action '{action}' not valid from state '{inst.State}'.");

                // Enforce the transition's role gate. Previously allowed_roles was declared
                // in the workflow JSON but never checked, so Check→Review→Approve gates could
                // be driven by anyone. The acting role is resolved from project_config.json
                // (USER_ROLE); the Information Manager (K) and Coordinator (C) administer the
                // CDE and are always permitted. An empty allowed_roles means "any role".
                if (!IsRolePermitted(transition, out string actingRole))
                {
                    // Audit is best-effort and must NEVER swallow the denial: if it threw here
                    // the caller would see a generic exception and (previously) treat the
                    // transition as permitted — a fail-open security hole.
                    try
                    {
                        AuditLog.Append(doc, "wf.transition_denied", docId, new JObject
                        {
                            ["workflow_id"]   = wf.Id,
                            ["instance_id"]   = inst.Id,
                            ["from"]          = inst.State,
                            ["action"]        = action,
                            ["user"]          = byUser,
                            ["role"]          = actingRole,
                            ["allowed_roles"] = new JArray(transition.AllowedRoles)
                        });
                    }
                    catch (Exception aex) { StingLog.Warn($"wf.transition_denied audit: {aex.Message}"); }

                    // Typed, so callers match on the TYPE rather than substring-matching the
                    // message (which silently disabled every gate if the wording changed).
                    throw new WorkflowRoleDeniedException(
                        $"Role '{actingRole}' is not permitted to perform '{action}' from '{inst.State}' " +
                        $"(requires one of: {string.Join(", ", transition.AllowedRoles)}).");
                }

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

        public static WorkflowInstance GetInstance(Document doc, string docId) => GetInstance(doc, docId, false);

        /// <summary>
        /// Latest instance for a document. <paramref name="includeClosed"/> matters because
        /// Published and Archived are TERMINAL: entering them closes the instance, after which
        /// the default lookup returns null. A caller that treats null as "never started" would
        /// then Start a brand-new instance back at WIP on every later action — leaking one
        /// instance per action and recording a WIP→… history for an already-published document.
        /// </summary>
        public static WorkflowInstance GetInstance(Document doc, string docId, bool includeClosed)
            => SelectInstance(LoadStore(doc), docId, includeClosed);

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
                // PM-1 — the deadline is an ISO "…Z" UTC string. Parse it as
                // universal (AssumeUniversal | AdjustToUniversal) and compare to
                // DateTime.UtcNow; without this it parsed as Local, so in Kampala
                // (UTC+3) every breach fired 3 hours early. (AuditLog.cs does it
                // correctly — this matches it.)
                if (!DateTime.TryParse(inst.SlaDeadline,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var deadline)) continue;
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
                // S3.6.1 — version gate before deserialise.
                StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                    path, "planscape.workflow-state",
                    StingTools.Core.PluginSchemaVersion.CurrentWorkflowState);
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
            // Folder consolidation: nest "_BIM_COORD" inside the unified
            // project root's _data folder rather than as a sibling of the .rvt.
            try
            {
                string consolidated = StingTools.Core.ProjectFolderEngine.GetDataPath(doc);
                if (!string.IsNullOrEmpty(consolidated)) return consolidated;
            }
            catch { /* fall through to legacy lookup */ }
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
