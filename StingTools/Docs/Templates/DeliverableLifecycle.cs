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
                d.Status = newStatus;
                if (!string.IsNullOrEmpty(newSuitability)) d.Suitability = newSuitability;
                if (!string.IsNullOrEmpty(newCde))         d.CDE = newCde;
                if (bumpRevision)                          d.Revision = BumpRevision((string)d.Revision ?? "P01");
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
                return new LifecycleResult { Updated = d, TemplateId = templateId, Message = newStatus };
            }
            catch (Exception ex)
            {
                StingLog.Error($"Lifecycle transition {action} failed", ex);
                return new LifecycleResult { Ok = false, Message = ex.Message };
            }
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
                var entry = Activator.CreateInstance(Type.GetType("StingTools.UI.BIMCoordinationCenter+RevisionHistoryEntry, StingTools"));
                if (entry == null) return;
                var t = entry.GetType();
                t.GetProperty("Revision")?.SetValue(entry, (string)d.Revision);
                t.GetProperty("Suitability")?.SetValue(entry, (string)d.Suitability);
                t.GetProperty("Timestamp")?.SetValue(entry, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                t.GetProperty("User")?.SetValue(entry, user);
                t.GetProperty("Reason")?.SetValue(entry, reason);
                t.GetProperty("TemplateId")?.SetValue(entry, templateId);
                (d.RevisionHistory as System.Collections.IList)?.Add(entry);
            }
            catch (Exception ex) { StingLog.Warn($"AppendRevHistory failed: {ex.Message}"); }
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
