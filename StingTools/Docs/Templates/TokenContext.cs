// TokenContext.cs — template engine v1.1 (S05).
//
// A flat-but-structured context object passed to renderers. Top-level buckets
// (Doc, Project, People, Transmittal, Loops) mirror the token groups in
// generated Word/Excel templates. AsDictionary() flattens to dotted keys
// (doc.number, project.company_name, loops.items -> List<Dictionary<...>>)
// so MiniWord / ClosedXML can consume the context directly.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using System.Linq;

namespace Planscape.Docs.Templates
{
    /// <summary>Typed view over the dictionary that renderers consume.</summary>
    public class TokenContext
    {
        public Dictionary<string, object> Doc         { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        public Dictionary<string, object> Project     { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        public Dictionary<string, object> People      { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        public Dictionary<string, object> Transmittal { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        public Dictionary<string, object> Meeting     { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        public Dictionary<string, List<Dictionary<string, object>>> Loops { get; } =
            new Dictionary<string, List<Dictionary<string, object>>>(StringComparer.Ordinal);

        /// <summary>Flatten to dotted-key dictionary for MiniWord / ClosedXML.</summary>
        public Dictionary<string, object> AsDictionary()
        {
            var o = new Dictionary<string, object>(StringComparer.Ordinal);
            Prefix(o, "doc.",         Doc);
            Prefix(o, "project.",     Project);
            Prefix(o, "people.",      People);
            Prefix(o, "transmittal.", Transmittal);
            Prefix(o, "meeting.",     Meeting);
            foreach (var kv in Loops)
                o["loops." + kv.Key] = kv.Value;
            // Convenience roots so MiniWord templates can also foreach top-level names.
            foreach (var kv in Loops) o[kv.Key] = kv.Value;
            return o;
        }

        private static void Prefix(Dictionary<string, object> sink, string prefix, Dictionary<string, object> src)
        {
            if (src == null) return;
            foreach (var kv in src) sink[prefix + kv.Key] = kv.Value ?? "";
        }

        // ── Factories ───────────────────────────────────────────────────────

        /// <summary>Builds a context from a DeliverableRow + project + manifest.</summary>
        public static TokenContext FromDeliverable(dynamic deliverable, Document doc, TemplateManifest m)
        {
            var ctx = new TokenContext();
            if (m?.Project != null) PopulateProject(ctx, m.Project);

            if (deliverable != null)
            {
                // Falls back to Code: a deliverable identified only by Code left "number" absent,
                // so TemplateEngine.RenderEntry named the output <date>_UNKNOWN_<template> — and
                // every such deliverable rendered on the same day OVERWROTE the previous one.
                AddIfPresent(ctx.Doc, "number",           SafeString(() =>
                {
                    string n = (string)deliverable.DocNumber;
                    return string.IsNullOrWhiteSpace(n) ? (string)deliverable.Code : n;
                }));
                AddIfPresent(ctx.Doc, "revision",         SafeString(() => (string)deliverable.Revision));
                AddIfPresent(ctx.Doc, "title",            SafeString(() => (string)deliverable.Name));
                AddIfPresent(ctx.Doc, "type",             SafeString(() => (string)deliverable.Type));
                AddIfPresent(ctx.Doc, "discipline",       SafeString(() => (string)deliverable.Discipline));
                AddIfPresent(ctx.Doc, "role",             SafeString(() => (string)deliverable.RoleCode));
                AddIfPresent(ctx.Doc, "suitability",      SafeString(() => (string)deliverable.Suitability));
                AddIfPresent(ctx.Doc, "cde",              SafeString(() => (string)deliverable.CDE));
                AddIfPresent(ctx.Doc, "status",           SafeString(() => (string)deliverable.Status));
                AddIfPresent(ctx.Doc, "fb",               SafeString(() => (string)deliverable.FunctionalBreakdown));
                AddIfPresent(ctx.Doc, "sb",               SafeString(() => (string)deliverable.SpatialBreakdown));
                AddIfPresent(ctx.Doc, "originator",       SafeString(() => (string)deliverable.Originator));
                AddIfPresent(ctx.Doc, "owner",            SafeString(() => (string)deliverable.Owner));
                AddIfPresent(ctx.Doc, "due_date",         SafeString(() => (string)deliverable.DueDate));
                AddIfPresent(ctx.Doc, "supersedes",       SafeString(() => (string)deliverable.Supersedes));
                AddIfPresent(ctx.Doc, "superseded_by",    SafeString(() => (string)deliverable.SupersededBy));
                AddIfPresent(ctx.Doc, "file_hash_sha256", SafeString(() => (string)deliverable.FileHashSha256));
                AddIfPresent(ctx.Doc, "contractor_ref",   SafeString(() => (string)deliverable.ContractorRef));
                AddIfPresent(ctx.Doc, "system",           SafeString(() => (string)deliverable.System));
                AddIfPresent(ctx.Doc, "subsystem",        SafeString(() => (string)deliverable.Subsystem));
                AddIfPresent(ctx.Doc, "equipment_type",   SafeString(() => (string)deliverable.EquipmentType));

                AddIfPresent(ctx.People, "issued_by",   SafeString(() => (string)deliverable.IssuedBy));
                AddIfPresent(ctx.People, "reviewed_by", SafeString(() => (string)deliverable.ReviewedBy));
                AddIfPresent(ctx.People, "approved_by", SafeString(() => (string)deliverable.ApprovedBy));

                ctx.Loops["revision_history"] = FlattenList(deliverable, "RevisionHistory");
                ctx.Loops["holds"]             = FlattenList(deliverable, "Holds");
                ctx.Loops["references"]        = FlattenList(deliverable, "References");
                ctx.Loops["workflow_history"]  = FlattenList(deliverable, "WorkflowHistory");
            }

            ctx.Doc["generated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            return ctx;
        }

        /// <summary>Builds a context from a TransmittalRequest.</summary>
        public static TokenContext FromTransmittalRequest(TransmittalRequest r, Document doc, TemplateManifest m)
        {
            var ctx = new TokenContext();
            if (m?.Project != null) PopulateProject(ctx, m.Project);
            if (r == null) return ctx;

            // doc.* is the header/footer block every template shares. FromDeliverable populated
            // it and this factory did not, so {{doc.generated_at}} could only ever render as
            // <TOKEN_NOT_FOUND> on a transmittal — visible in the issued document.
            ctx.Doc["generated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
            ctx.Doc["generator"]    = "StingTools";
            ctx.Doc["number"]       = r.TransmittalId ?? "";
            ctx.Doc["type"]         = "TRANSMITTAL";

            ctx.Transmittal["id"]             = r.TransmittalId ?? "";
            ctx.Transmittal["subject"]        = r.Subject ?? "";
            ctx.Transmittal["reason"]         = r.Reason ?? "";
            ctx.Transmittal["method"]         = r.Method ?? "Email";
            ctx.Transmittal["issue_date"]     = (r.IssueDate ?? DateTime.UtcNow).ToString("yyyy-MM-dd");
            ctx.Transmittal["response_due"]   = r.ResponseDueDate?.ToString("yyyy-MM-dd") ?? "";
            ctx.Transmittal["recipients"]     = string.Join("; ", r.Recipients ?? new List<string>());
            ctx.Transmittal["cc"]             = string.Join("; ", r.Cc ?? new List<string>());
            ctx.Transmittal["covering_note"]  = r.CoveringNote ?? "";

            ctx.People["issued_by"]   = r.IssuedBy   ?? "";
            ctx.People["reviewed_by"] = r.ReviewedBy ?? "";
            ctx.People["approved_by"] = r.ApprovedBy ?? "";

            var loop = new List<Dictionary<string, object>>();
            if (r.Documents != null)
                foreach (var d in r.Documents)
                    loop.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "number",      d.Number ?? "" },
                        { "title",       d.Title  ?? "" },
                        { "revision",    d.Revision ?? "" },
                        { "suitability", d.Suitability ?? "" },
                        { "type",        d.Type ?? "" },
                        { "file",        d.FilePath ?? "" }
                    });
            ctx.Loops["documents"] = loop;
            return ctx;
        }

        /// <summary>
        /// Builds a context from a meetings.json row for the D14 minutes template.
        /// <para>
        /// The store and the template disagree on field names — the store writes
        /// <c>assigned_to</c>/<c>due_date</c>/<c>num</c>/<c>duration_min</c>, the template asks
        /// for <c>owner</c>/<c>due</c>/<c>no</c>/<c>duration</c> — so the mapping happens here
        /// rather than by renaming either side. Empty collections still get one visible
        /// "none recorded" row: a blank table in an issued minute reads as an omission, whereas
        /// an explicit row states that nothing was recorded.
        /// </para>
        /// </summary>
        public static TokenContext FromMeeting(JObject meeting, Document doc, TemplateManifest m)
        {
            var ctx = new TokenContext();
            if (m?.Project != null) PopulateProject(ctx, m.Project);
            if (meeting == null) return ctx;

            string id = meeting["id"]?.ToString() ?? "";
            ctx.Doc["generated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'");
            ctx.Doc["generator"]    = "StingTools";
            ctx.Doc["number"]       = id;
            ctx.Doc["type"]         = "MEETING_MINUTES";

            ctx.Meeting["id"]       = id;
            ctx.Meeting["type"]     = meeting["type"]?.ToString() ?? "";
            ctx.Meeting["date"]     = meeting["date"]?.ToString() ?? "";
            ctx.Meeting["time"]     = meeting["time"]?.ToString() ?? "";
            ctx.Meeting["location"] = meeting["location"]?.ToString() ?? "";
            ctx.Meeting["chair"]    = meeting["chair"]?.ToString() ?? "";
            ctx.Meeting["status"]   = meeting["status"]?.ToString() ?? "";
            ctx.Meeting["minutes"]  = meeting["minutes"]?.ToString() ?? "";
            ctx.People["issued_by"] = meeting["created_by"]?.ToString() ?? "";

            ctx.Loops["attendees"] = Rows(meeting["attendees"], (a, i) => new Dictionary<string, object>
            {
                { "name",    Str(a, "name") },
                { "role",    Str(a, "role") },
                { "company", First(Str(a, "company"), Str(a, "discipline")) },
                { "email",   Str(a, "email") },
            }, "attendees", new[] { "name", "role", "company", "email" });

            ctx.Loops["agenda"] = Rows(meeting["agenda"], (a, i) => new Dictionary<string, object>
            {
                { "no",       First(Str(a, "no"), Str(a, "num"), (i + 1).ToString()) },
                { "topic",    Str(a, "topic") },
                { "lead",     First(Str(a, "lead"), Str(a, "source")) },
                { "duration", First(Str(a, "duration"), Str(a, "duration_min")) },
            }, "agenda", new[] { "no", "topic", "lead", "duration" });

            ctx.Loops["actions"] = Rows(meeting["actions"], (a, i) => new Dictionary<string, object>
            {
                { "no",          First(Str(a, "no"), Str(a, "id"), (i + 1).ToString()) },
                { "description", Str(a, "description") },
                { "owner",       First(Str(a, "owner"), Str(a, "assigned_to")) },
                { "due",         First(Str(a, "due"), Str(a, "due_date")) },
                { "status",      Str(a, "status") },
            }, "actions", new[] { "no", "description", "owner", "due", "status" });

            ctx.Loops["discussion"] = Rows(meeting["discussion"], (a, i) => new Dictionary<string, object>
            {
                { "topic",      Str(a, "topic") },
                { "ref",        Str(a, "ref") },
                { "discussion", Str(a, "discussion") },
                { "decision",   Str(a, "decision") },
            }, "discussion", new[] { "topic", "ref", "discussion", "decision" });

            return ctx;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static string Str(JToken t, string field) => t?[field]?.ToString() ?? "";

        private static string First(params string[] candidates)
        {
            foreach (string c in candidates)
                if (!string.IsNullOrWhiteSpace(c)) return c;
            return "";
        }

        /// <summary>
        /// Project a stored JArray into loop rows, substituting a single "none recorded" row
        /// when the collection is absent or empty so the table never renders as a blank box.
        /// </summary>
        private static List<Dictionary<string, object>> Rows(
            JToken source, Func<JToken, int, Dictionary<string, object>> map,
            string label, string[] fields)
        {
            var rows = new List<Dictionary<string, object>>();
            if (source is JArray arr)
            {
                int i = 0;
                foreach (var item in arr) rows.Add(map(item, i++));
            }
            if (rows.Count == 0)
            {
                var placeholder = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (string f in fields) placeholder[f] = "";
                // Put the notice in the widest column so it reads as a sentence, not a gap.
                placeholder[fields.Length > 1 ? fields[1] : fields[0]] = $"(no {label} recorded)";
                rows.Add(placeholder);
            }
            return rows;
        }

        private static void PopulateProject(TokenContext ctx, ProjectManifestBlock p)
        {
            ctx.Project["code"]                  = p.ProjectCode ?? "";
            ctx.Project["name"]                  = p.ProjectName ?? "";
            ctx.Project["originator"]            = p.OriginatorCode ?? "PLNS";
            ctx.Project["company_name"]          = p.CompanyName ?? "Planscape Limited";
            ctx.Project["company_address"]       = p.CompanyAddress ?? "Kampala, Uganda";
            ctx.Project["company_address_line_1"]= p.CompanyAddress ?? "Kampala, Uganda";
            ctx.Project["company_logo_path"]     = p.CompanyLogoPath ?? "";
            ctx.Project["client_name"]           = p.ClientName ?? "";
            ctx.Project["appointing_party"]      = p.AppointingParty ?? "";
            ctx.Project["lead_appointed_party"]  = p.LeadAppointedParty ?? "Planscape Limited";
            ctx.Project["participants"]          = p.Participants ?? "";
            ctx.Project["phase"]                 = p.Phase ?? "DE";
            ctx.Project["class"]                 = p.Class ?? "2";
            ctx.Project["workflow_profile"]      = p.WorkflowProfile ?? "default";
        }

        private static void AddIfPresent(Dictionary<string, object> sink, string key, string value)
        {
            sink[key] = value ?? "";
        }

        private static string SafeString(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }

        private static List<Dictionary<string, object>> FlattenList(object owner, string propertyName)
        {
            var flat = new List<Dictionary<string, object>>();
            try
            {
                var prop = owner?.GetType().GetProperty(propertyName);
                if (prop?.GetValue(owner) is System.Collections.IEnumerable ie)
                {
                    foreach (var item in ie)
                    {
                        var row = new Dictionary<string, object>(StringComparer.Ordinal);
                        if (item != null)
                        {
                            foreach (var p in item.GetType().GetProperties())
                            {
                                object v;
                                try { v = p.GetValue(item); } catch { v = null; }
                                row[CamelToSnake(p.Name)] = v ?? "";
                            }
                        }
                        flat.Add(row);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"TokenContext.FlattenList({propertyName}) failed: {ex.Message}"); }
            return flat;
        }

        private static string CamelToSnake(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var chars = new List<char>();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1])) chars.Add('_');
                chars.Add(char.ToLowerInvariant(c));
            }
            return new string(chars.ToArray());
        }
    }

    /// <summary>Transmittal creation DTO consumed by TransmittalOrchestrator (S10).</summary>
    public class TransmittalRequest
    {
        public string TransmittalId { get; set; }
        public string TemplateFamily { get; set; } = "B";     // "B" transmittal memo, "C" letter
        public string Subject { get; set; }
        public string Reason { get; set; }
        public string Method { get; set; } = "Email";
        public DateTime? IssueDate { get; set; }
        public DateTime? ResponseDueDate { get; set; }
        public List<string> Recipients { get; set; } = new List<string>();
        public List<string> Cc { get; set; } = new List<string>();
        public string CoveringNote { get; set; }
        public string IssuedBy { get; set; }
        public string ReviewedBy { get; set; }
        public string ApprovedBy { get; set; }
        public List<TransmittalDocumentRef> Documents { get; set; } = new List<TransmittalDocumentRef>();
    }

    public class TransmittalDocumentRef
    {
        public string Number { get; set; }
        public string Title { get; set; }
        public string Revision { get; set; }
        public string Suitability { get; set; }
        public string Type { get; set; }
        public string FilePath { get; set; }
    }
}
