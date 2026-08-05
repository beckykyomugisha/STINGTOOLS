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
                AddIfPresent(ctx.Doc, "number",           SafeString(() => (string)deliverable.DocNumber));
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

        // ── Helpers ─────────────────────────────────────────────────────────

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
