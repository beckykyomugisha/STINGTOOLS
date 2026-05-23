// TransmittalOrchestrator.cs — template engine v1.1 (S10).
//
// Orchestrates transmittal creation end-to-end:
//   1. Generate transmittal id (TX-NNNN)
//   2. Build TokenContext from a TransmittalRequest
//   3. Render the appropriate template (B/transmittal or C/letter_transmittal)
//   4. Append row to transmittals.json with template_id / rendered_file_path /
//      workflow_instance_id
//   5. Start the workflow via WorkflowEngine (S15)
//   6. Append audit entry via AuditLog (S16)
//
// Kept decoupled from UI: CreateTransmittalCommand and
// DocumentManagementDialog.QuickTransmittal delegate here.

using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Planscape.Docs.Workflow;
using StingTools.Core;

// StingTools.Core ships its own WorkflowEngine (ribbon workflow
// presets) which shadows Planscape.Docs.Workflow.WorkflowEngine
// inside this file. Alias the template-engine one so the
// orchestrator binds to the correct class at line 68.
using WorkflowEngine = Planscape.Docs.Workflow.WorkflowEngine;

namespace Planscape.Docs.Templates
{
    public class TransmittalResult
    {
        public JObject Record { get; set; }
        public string DocxPath { get; set; }
        public string TemplateId { get; set; }
        public string WorkflowInstanceId { get; set; }
        public bool Ok { get; set; } = true;
        public string Error { get; set; }
    }

    public static class TransmittalOrchestrator
    {
        /// <summary>Main entry — idempotent per TransmittalId when supplied by caller.</summary>
        public static TransmittalResult Create(Document doc, TransmittalRequest request)
        {
            if (request == null) return new TransmittalResult { Ok = false, Error = "Request is null" };
            try
            {
                var engine = new TemplateEngine(doc);
                var manifest = engine.Registry.Manifest;

                if (string.IsNullOrEmpty(request.TransmittalId))
                    request.TransmittalId = NextTransmittalId(doc);

                // Context + render.
                var ctx = TokenContext.FromTransmittalRequest(request, doc, manifest);
                ctx.Doc["number"] = request.TransmittalId;
                ctx.Doc["type"]   = "TX";

                string family = string.IsNullOrEmpty(request.TemplateFamily) ? "B" : request.TemplateFamily;
                string purpose = family == "C" ? "letter_transmittal" : "transmittal";
                var entry = manifest.FindByPurpose(family, purpose);
                if (entry == null)
                {
                    return new TransmittalResult { Ok = false,
                        Error = $"No transmittal template registered for family='{family}'." };
                }

                string renderedPath = engine.RenderByPurpose(family, purpose, ctx);

                // Persist record.
                string wfInstanceId = null;
                try { wfInstanceId = WorkflowEngine.Start(doc, entry.WorkflowId ?? "transmittal_default", request.TransmittalId); }
                catch (Exception ex) { StingLog.Warn($"WorkflowEngine.Start failed (continuing): {ex.Message}"); }

                var record = new JObject
                {
                    ["id"]                    = request.TransmittalId,
                    ["subject"]               = request.Subject,
                    ["reason"]                = request.Reason,
                    ["method"]                = request.Method,
                    ["issue_date"]            = (request.IssueDate ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["response_due"]          = request.ResponseDueDate?.ToString("yyyy-MM-dd"),
                    ["recipients"]            = new JArray(request.Recipients ?? new List<string>()),
                    ["cc"]                    = new JArray(request.Cc ?? new List<string>()),
                    ["covering_note"]         = request.CoveringNote,
                    ["issued_by"]             = request.IssuedBy,
                    ["reviewed_by"]           = request.ReviewedBy,
                    ["approved_by"]           = request.ApprovedBy,
                    ["documents"]             = JArray.FromObject(request.Documents ?? new List<TransmittalDocumentRef>()),
                    ["template_id"]           = entry.Id,
                    ["rendered_file_path"]    = renderedPath,
                    ["workflow_instance_id"]  = wfInstanceId
                };
                AppendTransmittalsJson(doc, record);

                // Audit trail.
                try
                {
                    AuditLog.Append(doc, "doc.rendered", request.TransmittalId, new JObject
                    {
                        ["template_id"]        = entry.Id,
                        ["rendered_file_path"] = renderedPath,
                        ["workflow_instance_id"] = wfInstanceId
                    });
                }
                catch (Exception ex) { StingLog.Warn($"AuditLog.Append failed: {ex.Message}"); }

                return new TransmittalResult
                {
                    Record = record,
                    DocxPath = renderedPath,
                    TemplateId = entry.Id,
                    WorkflowInstanceId = wfInstanceId
                };
            }
            catch (Exception ex)
            {
                StingLog.Error("TransmittalOrchestrator.Create failed", ex);
                return new TransmittalResult { Ok = false, Error = ex.Message };
            }
        }

        private static string NextTransmittalId(Document doc)
        {
            string path = TransmittalsPath(doc);
            int count = 0;
            if (File.Exists(path))
            {
                try
                {
                    // S3.6.2 — version gate before deserialise.
                    StingTools.Core.PluginSchemaVersion.EnsureFileVersion(
                        path, "planscape.transmittals",
                        StingTools.Core.PluginSchemaVersion.CurrentTransmittals);
                    var arr = JArray.Parse(File.ReadAllText(path));
                    count = arr.Count;
                }
                catch { count = 0; }
            }
            return $"TX-{(count + 1):D4}";
        }

        private static string TransmittalsPath(Document doc)
        {
            string root = ResolveProjectRoot(doc);
            string dir  = Path.Combine(root, "_BIM_COORD");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "transmittals.json");
        }

        private static void AppendTransmittalsJson(Document doc, JObject record)
        {
            string path = TransmittalsPath(doc);
            JArray arr;
            try
            {
                arr = File.Exists(path) ? JArray.Parse(File.ReadAllText(path)) : new JArray();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"transmittals.json parse failed — starting fresh: {ex.Message}");
                arr = new JArray();
            }
            arr.Add(record);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, arr.ToString(Formatting.Indented));
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
