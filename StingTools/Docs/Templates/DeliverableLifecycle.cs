// DeliverableLifecycle.cs — template engine v1.1 (S09).
//
// State machine over DeliverableRow. Every call validates the current state,
// mutates identity/revision/suitability, appends RevisionHistory, persists
// deliverables.json, writes an AuditLog entry (S16), and returns the template
// id that callers should render.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Planscape.Docs.Workflow;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    public static class DeliverableLifecycle
    {
        public class LifecycleResult
        {
            public object Updated { get; set; }           // dynamic DeliverableRow (BIMCoordinationCenter-nested)
            public string TemplateId { get; set; }
            public string Message { get; set; }
            public bool Ok { get; set; } = true;
        }

        public static LifecycleResult Issue(dynamic d, Document doc, TemplateManifest m, string issuedBy, string reason)
            => Transition(d, doc, m, "Issued", "A01", issuedBy, null, null, reason, action: "issued");

        public static LifecycleResult ReIssue(dynamic d, Document doc, TemplateManifest m, string issuedBy, string reason)
            => Transition(d, doc, m, "Re-Issued", "A01", issuedBy, null, null, reason, action: "reissued", bumpRevision: true);

        public static LifecycleResult Publish(dynamic d, Document doc, TemplateManifest m, string issuedBy, int stage)
        {
            string suit = "S4";
            if (stage == 1) suit = "S2";
            else if (stage == 2) suit = "S3";
            else if (stage == 3) suit = "S4";
            else if (stage >= 4) suit = "S5";
            var res = Transition(d, doc, m, "Published", "A01", issuedBy, newSuitability: suit,
                                 newCde: stage >= 3 ? "PUBLISHED" : "SHARED",
                                 reason: $"Publish stage {stage}", action: $"published_stage_{stage}");
            return res;
        }

        public static LifecycleResult Cancel(dynamic d, Document doc, TemplateManifest m, string issuedBy, string reason)
            => Transition(d, doc, m, "Cancelled", "A02", issuedBy, "CANCELLED", "ARCHIVE", reason, action: "cancelled");

        public static LifecycleResult Supersede(dynamic existing, string newDocNumber, Document doc, TemplateManifest m, string issuedBy, string reason)
        {
            if (existing == null) return new LifecycleResult { Ok = false, Message = "Existing deliverable is null" };
            try
            {
                existing.SupersededBy = newDocNumber;
                existing.Status       = "Superseded";
                AppendRevHistory(existing, reason, issuedBy, "A03");
                WriteAudit(doc, "doc.superseded", (string)existing.DocNumber,
                    new JObject { ["superseded_by"] = newDocNumber, ["reason"] = reason, ["user"] = issuedBy });
                Persist(doc, existing);
                MirrorToServer(doc, existing, "superseded", reason);
                return new LifecycleResult { Updated = existing, TemplateId = "A03", Message = $"Superseded by {newDocNumber}" };
            }
            catch (Exception ex)
            {
                StingLog.Error("Supersede failed", ex);
                return new LifecycleResult { Ok = false, Message = ex.Message };
            }
        }

        public static LifecycleResult Replace(dynamic existing, dynamic newReplacing, Document doc, TemplateManifest m, string issuedBy, string reason)
        {
            if (existing == null || newReplacing == null)
                return new LifecycleResult { Ok = false, Message = "Existing and replacing deliverables are required" };
            try
            {
                newReplacing.Supersedes = existing.DocNumber;
                existing.SupersededBy   = newReplacing.DocNumber;
                existing.Status         = "Replaced";
                AppendRevHistory(existing,   $"Replaced by {newReplacing.DocNumber}: {reason}", issuedBy, "A04");
                AppendRevHistory(newReplacing, $"Replacing {existing.DocNumber}: {reason}", issuedBy, "A04");
                WriteAudit(doc, "doc.replaced", (string)newReplacing.DocNumber, new JObject
                {
                    ["replacing"] = (string)existing.DocNumber,
                    ["reason"]    = reason,
                    ["user"]      = issuedBy
                });
                Persist(doc, existing);
                Persist(doc, newReplacing);
                MirrorToServer(doc, existing, "replaced", reason);
                MirrorToServer(doc, newReplacing, "replacing", reason);
                return new LifecycleResult { Updated = newReplacing, TemplateId = "A04", Message = $"Replaces {existing.DocNumber}" };
            }
            catch (Exception ex)
            {
                StingLog.Error("Replace failed", ex);
                return new LifecycleResult { Ok = false, Message = ex.Message };
            }
        }

        // ── Shared transition logic ─────────────────────────────────────────

        private static LifecycleResult Transition(dynamic d, Document doc, TemplateManifest m,
            string newStatus, string templateId, string user,
            string newSuitability, string newCde, string reason,
            string action, bool bumpRevision = false)
        {
            if (d == null) return new LifecycleResult { Ok = false, Message = "Deliverable is null" };
            try
            {
                // Run the workflow state machine FIRST so a role-gated transition can block
                // the lifecycle change before anything is persisted. Only a genuine role
                // denial blocks; undefined paths / an unstarted engine / no server never do —
                // the workflow is a tracking overlay that enforces only when a transition
                // declares allowed_roles (empty ⇒ any; K/C always permitted).
                bool cancelling = string.Equals(action, "cancelled", StringComparison.OrdinalIgnoreCase);
                // Explicitly-typed target: DriveWorkflow takes a dynamic 'd', so the call is
                // dynamic-dispatched and cannot be `var`-deconstructed.
                (bool blocked, string blockMsg) wf =
                    DriveWorkflow(doc, d, MapCdeToWfState(newCde), user, reason, cancelling);
                if (wf.blocked)
                    return new LifecycleResult { Ok = false, Message = wf.blockMsg ?? "Workflow role gate denied this transition." };

                d.Status = newStatus;
                if (!string.IsNullOrEmpty(newSuitability)) d.Suitability = newSuitability;
                if (!string.IsNullOrEmpty(newCde))         d.CDE = newCde;
                if (bumpRevision)                          d.Revision = BumpRevision((string)d.Revision ?? "P01");
                // ISO 19650: promote the preliminary P-series revision to the contractual
                // C-series once the deliverable is authorised for publication.
                if (string.Equals(newCde, "PUBLISHED", StringComparison.OrdinalIgnoreCase))
                    d.Revision = PromoteToContractual((string)d.Revision);
                d.IssuedBy = user;

                AppendRevHistory(d, reason, user, templateId);
                WriteAudit(doc, "doc." + action, (string)d.DocNumber, new JObject
                {
                    ["status"]      = newStatus,
                    ["revision"]    = (string)d.Revision,
                    ["suitability"] = (string)d.Suitability,
                    ["user"]        = user,
                    ["reason"]      = reason
                });
                Persist(doc, d);
                MirrorToServer(doc, d, action, reason);
                return new LifecycleResult { Updated = d, TemplateId = templateId, Message = newStatus };
            }
            catch (Exception ex)
            {
                StingLog.Error($"Lifecycle transition {action} failed", ex);
                return new LifecycleResult { Ok = false, Message = ex.Message };
            }
        }

        // ── Workflow state machine (deliverable_issue_default) ──────────────
        //
        // The lifecycle now DRIVES Planscape.Docs.Workflow.WorkflowEngine so the
        // role-enforced gates (WP8.3) actually fire for deliverables, not just
        // transmittals. Issue starts the instance (at WIP); every CDE-changing action
        // walks the linear WIP→Shared→Published→Archived machine to the target state,
        // one role-gated Transition per hop; Cancel jumps to Archived via a "cancel"
        // transition. The instance state + SLA are synced back onto the deliverable.

        private static readonly string[] _wfOrder = { "WIP", "Shared", "Published", "Archived" };
        private static readonly Dictionary<string, string> _wfForward =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["WIP"] = "share", ["Shared"] = "publish", ["Published"] = "archive" };

        private static string MapCdeToWfState(string cde)
        {
            if (string.IsNullOrEmpty(cde)) return null;
            switch (cde.ToUpperInvariant())
            {
                case "WIP":       return "WIP";
                case "SHARED":    return "Shared";
                case "PUBLISHED": return "Published";
                case "ARCHIVE":
                case "ARCHIVED":  return "Archived";
                default:          return null;
            }
        }

        /// <summary>
        /// Ensure the deliverable's workflow instance exists and advance it toward
        /// <paramref name="targetWf"/> (or to Archived when <paramref name="cancelling"/>).
        /// Returns (blocked, message); blocked is true ONLY when a role gate denied a hop,
        /// in which case the caller must abort the lifecycle change. All other issues are
        /// logged and non-blocking.
        /// </summary>
        private static (bool blocked, string msg) DriveWorkflow(
            Document doc, dynamic d, string targetWf, string user, string reason, bool cancelling)
        {
            string docNumber = (string)d.DocNumber ?? (string)d.Code ?? "";
            if (string.IsNullOrEmpty(docNumber)) return (false, null);
            try
            {
                var inst = Planscape.Docs.Workflow.WorkflowEngine.GetInstance(doc, docNumber);
                if (inst == null)
                {
                    try { Planscape.Docs.Workflow.WorkflowEngine.Start(doc, "deliverable_issue_default", docNumber); }
                    catch (Exception ex) { StingLog.Warn($"DriveWorkflow start: {ex.Message}"); return (false, null); }
                    inst = Planscape.Docs.Workflow.WorkflowEngine.GetInstance(doc, docNumber);
                }
                if (inst == null) return (false, null);

                if (cancelling)
                {
                    if (!string.Equals(inst.State, "Archived", StringComparison.OrdinalIgnoreCase))
                    {
                        var r = TryTransition(doc, docNumber, "cancel", user, reason);
                        if (r.blocked) return r;
                    }
                }
                else if (!string.IsNullOrEmpty(targetWf))
                {
                    for (int guard = 0; guard < 8; guard++)
                    {
                        int cur = Array.FindIndex(_wfOrder, s => string.Equals(s, inst.State, StringComparison.OrdinalIgnoreCase));
                        int tgt = Array.FindIndex(_wfOrder, s => string.Equals(s, targetWf, StringComparison.OrdinalIgnoreCase));
                        if (cur < 0 || tgt < 0 || tgt <= cur) break;
                        if (!_wfForward.TryGetValue(_wfOrder[cur], out string act)) break;
                        var r = TryTransition(doc, docNumber, act, user, reason);
                        if (r.blocked) return r;
                        inst = Planscape.Docs.Workflow.WorkflowEngine.GetInstance(doc, docNumber);
                        if (inst == null) break;
                    }
                }

                SyncWorkflowFields(d, Planscape.Docs.Workflow.WorkflowEngine.GetInstance(doc, docNumber));
                return (false, null);
            }
            catch (Exception ex) { StingLog.Warn($"DriveWorkflow: {ex.Message}"); return (false, null); }
        }

        /// <summary>One Transition; blocks only on a role denial, warns on an undefined path.</summary>
        private static (bool blocked, string msg) TryTransition(
            Document doc, string docNumber, string action, string user, string reason)
        {
            try { Planscape.Docs.Workflow.WorkflowEngine.Transition(doc, docNumber, action, user, reason); return (false, null); }
            catch (InvalidOperationException ex)
            {
                if (ex.Message.IndexOf("not permitted", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (true, ex.Message);   // role gate — abort the lifecycle change
                StingLog.Warn($"DriveWorkflow transition '{action}': {ex.Message}"); // undefined path — non-blocking
                return (false, null);
            }
            catch (Exception ex) { StingLog.Warn($"DriveWorkflow transition '{action}': {ex.Message}"); return (false, null); }
        }

        private static void SyncWorkflowFields(dynamic d, WorkflowInstance inst)
        {
            if (inst == null) return;
            try
            {
                d.WorkflowState = inst.State;
                if (!string.IsNullOrEmpty(inst.SlaDeadline) &&
                    DateTime.TryParse(inst.SlaDeadline, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    d.SlaDeadline = (DateTime?)dt;
            }
            catch (Exception ex) { StingLog.Warn($"SyncWorkflowFields: {ex.Message}"); }
        }

        /// <summary>ISO 19650 P→C revision promotion (P01 → C01). Idempotent for non-P revisions.</summary>
        private static string PromoteToContractual(string rev)
        {
            if (string.IsNullOrEmpty(rev)) return "C01";
            if (rev.StartsWith("P", StringComparison.OrdinalIgnoreCase)) return "C" + rev.Substring(1);
            return rev;
        }

        private static string BumpRevision(string cur)
        {
            if (string.IsNullOrEmpty(cur)) return "P02";
            try
            {
                string prefix = new string(cur.TakeWhile(char.IsLetter).ToArray());
                string numStr = new string(cur.SkipWhile(char.IsLetter).ToArray());
                if (int.TryParse(numStr, out int n)) return $"{prefix}{(n + 1).ToString(new string('0', numStr.Length))}";
            }
            catch { /* fallthrough */ }
            return cur + "+1";
        }

        private static void AppendRevHistory(dynamic d, string reason, string user, string templateId)
        {
            try
            {
                // Direct construction — the POCO is in this assembly, so the old
                // Activator + per-property SetValue bridge only added a silent
                // failure mode if any property were ever renamed.
                var entry = new StingTools.UI.BIMCoordinationCenter.RevisionHistoryEntry
                {
                    Revision    = (string)d.Revision,
                    Suitability = (string)d.Suitability,
                    Timestamp   = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    User        = user,
                    Reason      = reason,
                    TemplateId  = templateId
                };
                (d.RevisionHistory as System.Collections.IList)?.Add(entry);
            }
            catch (Exception ex) { StingLog.Warn($"AppendRevHistory failed: {ex.Message}"); }
        }

        /// <summary>
        /// Mirror a lifecycle event to the Planscape server. DeliverableServerSync
        /// .FireAndForget previously had zero callers, so transitions only reached the
        /// server via the periodic reconcile tick (and only while authenticated). This
        /// makes the mirror event-driven, with the reconcile retained as a backstop.
        /// No-ops when the project is not linked to a server project.
        /// </summary>
        private static void MirrorToServer(Document doc, dynamic d, string action, string reason)
        {
            try { DeliverableServerSync.FireAndForget(doc, d, action, reason); }
            catch (Exception ex) { StingLog.Warn($"DeliverableServerSync ({action}): {ex.Message}"); }
        }

        // ── Persistence to _BIM_COORD/deliverables.json ─────────────────────

        public static void Persist(Document doc, dynamic d)
        {
            try
            {
                string path = DeliverablesPath(doc);
                JArray arr;
                if (File.Exists(path))
                {
                    // S3.6.2 — version gate before deserialise.
                    StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                        path, "planscape.deliverables",
                        StingTools.Core.PluginSchemaVersion.CurrentDeliverables);
                    arr = JArray.Parse(File.ReadAllText(path));
                }
                else arr = new JArray();

                string docNumber = (string)d.DocNumber ?? (string)d.Code ?? "";
                JObject row = JObject.FromObject(d);
                int idx = -1;
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JObject o && string.Equals(
                        o.Value<string>("DocNumber") ?? o.Value<string>("Code"),
                        docNumber, StringComparison.Ordinal))
                    { idx = i; break; }
                }
                if (idx >= 0) arr[idx] = row; else arr.Add(row);

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, arr.ToString(Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex) { StingLog.Error("DeliverableLifecycle.Persist failed", ex); }
        }

        private static string DeliverablesPath(Document doc)
        {
            string root = ResolveProjectRoot(doc);
            string dir  = Path.Combine(root, "_BIM_COORD");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "deliverables.json");
        }

        private static void WriteAudit(Document doc, string action, string docId, JObject payload)
        {
            try { AuditLog.Append(doc, action, docId, payload); }
            catch (Exception ex) { StingLog.Warn($"AuditLog unavailable for {action}: {ex.Message}"); }
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
