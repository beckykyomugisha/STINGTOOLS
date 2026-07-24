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
                if (!Persist(doc, existing))
                    return new LifecycleResult { Ok = false, Message = "Supersede not saved — see StingTools.log." };
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
            // A deliverable cannot replace itself: doing so writes Supersedes == SupersededBy ==
            // its own number and flips its status to "Replaced", leaving a record that claims to
            // have been superseded by a document that does not exist.
            if (ReferenceEquals(existing, newReplacing) ||
                string.Equals(DeliverableKey(existing), DeliverableKey(newReplacing), StringComparison.OrdinalIgnoreCase))
                return new LifecycleResult { Ok = false, Message = "A deliverable cannot replace itself — pick a distinct replacement." };
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
                bool okA = Persist(doc, existing), okB = Persist(doc, newReplacing);
                if (!okA || !okB)
                    return new LifecycleResult { Ok = false, Message =
                        $"Replace only partially saved (existing: {(okA ? "ok" : "FAILED")}, " +
                        $"replacement: {(okB ? "ok" : "FAILED")}). See StingTools.log." };
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
                // Revision prefixes come from the project manifest (default P→C), so an
                // appointment mandating another convention is configuration, not a code change.
                var revScheme = SchemeFor(m);
                if (bumpRevision)
                {
                    string before = (string)d.Revision ?? revScheme.FirstPreliminary;
                    string after  = revScheme.Bump(before);
                    // Bump returns the input unchanged when it cannot parse the sequence
                    // (it refuses to persist a corrupting sentinel). Say so, or the
                    // re-issue silently keeps the old revision number.
                    if (!string.IsNullOrWhiteSpace(before) && string.Equals(before, after, StringComparison.Ordinal))
                        StingLog.Warn($"BumpRevision: cannot increment revision '{before}'; leaving it unchanged.");
                    d.Revision = after;
                }
                // ISO 19650: promote the preliminary revision series to the contractual
                // series once the deliverable is authorised for publication.
                if (string.Equals(newCde, "PUBLISHED", StringComparison.OrdinalIgnoreCase))
                    d.Revision = revScheme.PromoteToContractual((string)d.Revision);
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
                // A failed Persist must NOT report success: the caller goes on to render the
                // document, register it and tell the user "Transition succeeded" while nothing
                // was written to deliverables.json.
                if (!Persist(doc, d))
                    return new LifecycleResult { Ok = false, Message =
                        "Transition not saved — the deliverable has no DocNumber/Code, or deliverables.json is unwritable. See StingTools.log." };
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
            string docNumber = DeliverableKey(d);
            if (string.IsNullOrEmpty(docNumber)) return (false, null);
            try
            {
                // includeClosed: Published and Archived are TERMINAL, so reaching them closes the
                // instance. Looking up open-only would return null there and Start a fresh WIP
                // instance on every subsequent action — one leaked instance per action, each
                // rewriting the deliverable's workflow fields back to WIP.
                var inst = Planscape.Docs.Workflow.WorkflowEngine.GetInstance(doc, docNumber, true);
                if (inst == null)
                {
                    try { Planscape.Docs.Workflow.WorkflowEngine.Start(doc, "deliverable_issue_default", docNumber); }
                    catch (Exception ex) { StingLog.Warn($"DriveWorkflow start: {ex.Message}"); return (false, null); }
                    inst = Planscape.Docs.Workflow.WorkflowEngine.GetInstance(doc, docNumber, true);
                }
                if (inst == null) return (false, null);

                if (cancelling)
                {
                    // Cancel must still run on a CLOSED instance. Published is terminal, so a
                    // published deliverable's instance is closed — returning early here (as the
                    // first cut did) left the record claiming Status=Cancelled / CDE=ARCHIVE
                    // while its workflow state still read Published, with no audit row for the
                    // cancellation at all. Reopen lets the shipped Published→Archived
                    // transition fire.
                    if (!string.Equals(inst.State, "Archived", StringComparison.OrdinalIgnoreCase))
                    {
                        if (inst.Closed &&
                            !Planscape.Docs.Workflow.WorkflowEngine.Reopen(doc, docNumber, "cancel"))
                        {
                            StingLog.Warn($"DriveWorkflow: cannot cancel '{docNumber}' — no 'cancel' " +
                                          $"transition from terminal state '{inst.State}'.");
                        }
                        else
                        {
                            var r = TryTransition(doc, docNumber, "cancel", user, reason);
                            if (r.blocked) return r;
                        }
                    }
                }
                else if (inst.Closed)
                {
                    // Terminal and not cancelling: nothing left to drive. Sync so the
                    // deliverable's workflow fields reflect the final state.
                    SyncWorkflowFields(d, inst);
                    return (false, null);
                }
                else if (!string.IsNullOrEmpty(targetWf))
                {
                    // Plan the whole walk, then validate it BEFORE committing any hop. Each
                    // Transition persists immediately, so a denial on hop 3 of 3 used to leave
                    // hops 1-2 written with no rollback — the workflow ends up ahead of the
                    // deliverable record it is supposed to describe.
                    var hops = PlanForwardHops(inst.State, targetWf);
                    if (hops.Count > 0)
                    {
                        if (!Planscape.Docs.Workflow.WorkflowEngine.ValidatePath(doc, docNumber, hops, out string deny))
                            return (true, deny);

                        // Verify each hop LANDED before firing the next. TryTransition reports
                        // only role denials as blocking; an IO error or a definition that has
                        // drifted from _wfForward is swallowed as non-blocking, and the old
                        // loop then fired the remaining hops from a state they are not defined
                        // for — every one of them failing silently while the lifecycle went on
                        // to stamp CDE=PUBLISHED over a workflow still sitting at WIP.
                        foreach (string act in hops)
                        {
                            string before = inst.State;
                            var r = TryTransition(doc, docNumber, act, user, reason);
                            if (r.blocked) return r;

                            inst = Planscape.Docs.Workflow.WorkflowEngine.GetInstance(doc, docNumber, true);
                            if (inst == null || string.Equals(inst.State, before, StringComparison.OrdinalIgnoreCase))
                            {
                                StingLog.Warn($"DriveWorkflow: hop '{act}' did not advance '{docNumber}' " +
                                              $"from '{before}'; stopping the walk here.");
                                break;
                            }
                        }
                    }
                }

                SyncWorkflowFields(d, Planscape.Docs.Workflow.WorkflowEngine.GetInstance(doc, docNumber, true));
                return (false, null);
            }
            catch (Exception ex) { StingLog.Warn($"DriveWorkflow: {ex.Message}"); return (false, null); }
        }

        /// <summary>
        /// Actions needed to walk forward from <paramref name="fromState"/> to
        /// <paramref name="targetState"/> along _wfOrder. Empty when already at/past the target
        /// or when either state is unknown.
        /// </summary>
        private static List<string> PlanForwardHops(string fromState, string targetState)
        {
            var hops = new List<string>();
            int cur = Array.FindIndex(_wfOrder, s => string.Equals(s, fromState, StringComparison.OrdinalIgnoreCase));
            int tgt = Array.FindIndex(_wfOrder, s => string.Equals(s, targetState, StringComparison.OrdinalIgnoreCase));
            if (cur < 0 || tgt < 0 || tgt <= cur) return hops;
            for (int i = cur; i < tgt && hops.Count < 8; i++)
            {
                if (!_wfForward.TryGetValue(_wfOrder[i], out string act)) break;
                hops.Add(act);
            }
            return hops;
        }

        /// <summary>One Transition; blocks only on a role denial, warns on an undefined path.</summary>
        private static (bool blocked, string msg) TryTransition(
            Document doc, string docNumber, string action, string user, string reason)
        {
            try { Planscape.Docs.Workflow.WorkflowEngine.Transition(doc, docNumber, action, user, reason); return (false, null); }
            // Typed — a role denial is matched on the exception TYPE. The previous substring
            // match on "not permitted" would have silently opened the gate the moment the
            // message wording changed.
            catch (Planscape.Docs.Workflow.WorkflowRoleDeniedException ex) { return (true, ex.Message); }
            catch (InvalidOperationException ex)
            {
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

        /// <summary>
        /// Resolves the project's revision scheme from the manifest. The parsing rules and
        /// the Bump / PromoteToContractual behaviour live in the Revit-free
        /// <see cref="RevisionScheme"/> so they can be unit-tested outside Revit; this
        /// wrapper only supplies the manifest value. Unset ⇒ the ISO 19650 P→C default.
        /// </summary>
        private static RevisionScheme SchemeFor(TemplateManifest m)
            => RevisionScheme.Parse(m?.Project?.RevisionScheme);

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
                // The `?.` used to swallow a null list entirely: the deliverable would transition
                // with NO audit trail of the revision and nobody would ever know. ISO 19650
                // revision history is the point of the record — say so when it can't be written.
                var list = d.RevisionHistory as System.Collections.IList;
                if (list == null)
                {
                    StingLog.Warn($"AppendRevHistory: deliverable '{DeliverableKey(d)}' has no RevisionHistory " +
                                  $"list; revision {(string)d.Revision} not recorded.");
                    return;
                }
                list.Add(entry);
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

        /// <summary>
        /// Identity of a deliverable: DocNumber, else Code, else empty. Uses IsNullOrWhiteSpace
        /// rather than `??` because these fields are commonly EMPTY STRINGS, not nulls — `??`
        /// then keeps the empty DocNumber and never falls back to a perfectly good Code.
        /// </summary>
        /// <summary>
        /// Identity of a deliverable POCO. IM-13: shares the trim + first-non-blank rule with
        /// DocumentRegister and CoordStores via <see cref="StingTools.Core.DocumentIdentity"/>,
        /// so all three stores key the same document the same way.
        /// </summary>
        internal static string DeliverableKey(dynamic d)
        {
            try
            {
                return StingTools.Core.DocumentIdentity
                    .FirstNonBlankValue((string)d.DocNumber, (string)d.Code) ?? "";
            }
            catch { return ""; }
        }

        /// <summary>Same identity rule, for a persisted row.</summary>
        /// <summary>
        /// Identity of a persisted deliverable row. IM-13: same shared rule as
        /// <see cref="DeliverableKey"/>, keyed off the canonical DocNumber/Code candidates.
        /// </summary>
        internal static string RowKey(JObject o)
            => StingTools.Core.DocumentIdentity
                .FirstNonBlank(o, StingTools.Core.DocumentIdentity.DeliverableKeys) ?? "";

        /// <summary>Write the deliverable to deliverables.json. False when nothing was saved.</summary>
        public static bool Persist(Document doc, dynamic d)
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

                string docNumber = DeliverableKey(d);
                if (string.IsNullOrEmpty(docNumber))
                {
                    // A record with neither DocNumber nor Code cannot be identified. Writing it
                    // would either append a new row on EVERY save (unbounded growth) or — with
                    // the old `??`-only key — collide with any other blank-keyed row and
                    // overwrite an unrelated deliverable. Refuse instead.
                    StingLog.Error("DeliverableLifecycle.Persist: deliverable has no DocNumber or Code; not persisted.", null);
                    return false;
                }

                JObject row = JObject.FromObject(d);
                int idx = -1;
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JObject o && string.Equals(RowKey(o), docNumber, StringComparison.Ordinal))
                    { idx = i; break; }
                }
                if (idx >= 0) arr[idx] = row; else arr.Add(row);

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, arr.ToString(Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex) { StingLog.Error("DeliverableLifecycle.Persist failed", ex); return false; }
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
