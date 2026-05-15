// EmbeddedTemplates.cs — template engine v1.1 (S11 + S15).
//
// On first project open, streams every embedded .docx / .xlsx template into
// _BIM_COORD/templates/, every embedded workflow JSON into _BIM_COORD/
// workflows/, and writes the default manifest.json seeded from
// ProjectInformation + PRJ_ORG_* parameters.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Planscape.Docs.Workflow;
using StingTools.Core;

namespace Planscape.Docs.Templates
{
    public static class EmbeddedTemplates
    {
        private const string TemplateResourcePrefix = "StingTools.Docs._template_sources.";
        private const string WorkflowResourcePrefix = "StingTools.Docs._workflow_sources.";

        /// <summary>Registered template metadata (id → filename + family + purpose).</summary>
        private static readonly (string Id, string File, string Family, string Purpose, string Workflow, string Desc)[] Catalogue =
        {
            // S11 (six defaults)
            ("A01", "deliverable_standard.docx",    "A", "standard",           "deliverable_issue_default", "Deliverable Cover Sheet"),
            ("A02", "deliverable_cancelled.docx",   "A", "cancelled",          "deliverable_issue_default", "Deliverable Cancellation Notice"),
            ("A03", "deliverable_superseded.docx",  "A", "superseded",         "deliverable_issue_default", "Deliverable Superseded Notice"),
            ("A04", "deliverable_replacing.docx",   "A", "replacing",          "deliverable_issue_default", "Deliverable Replacing Notice"),
            ("B06", "transmittal.docx",             "B", "transmittal",        "transmittal_default",       "Transmittal Memo"),
            ("C13", "letter_transmittal.docx",      "C", "letter_transmittal", "transmittal_default",       "Letter of Transmittal"),

            // S14a
            ("A05", "deliverable_tabular.xlsx",     "A", "tabular",            "deliverable_issue_default", "Tabular Deliverable"),
            ("B07", "technical_query.docx",         "B", "technical_query",    "tq_default",                "Technical Query"),
            ("B08", "rfi.docx",                     "B", "rfi",                "rfi_default",               "Request for Information"),

            // S14b
            ("C10", "material_requisition.docx",    "C", "material_requisition", "mr_default",              "Material Requisition"),
            ("C11", "submittal_cover.docx",         "C", "submittal_cover",      "deliverable_issue_default", "Submittal Cover"),
            ("C12", "variation.docx",               "C", "variation",            "deliverable_issue_default", "Variation"),
            ("B09", "technical_response.docx",      "B", "technical_response",   "tq_default",              "Technical Response"),

            // S14c
            ("D14", "meeting_minutes.docx",         "D", "meeting_minutes",    null, "Meeting Minutes"),
            ("D15", "progress_report.docx",         "D", "progress_report",    null, "Progress Report"),
            ("D16", "handover_certificate.docx",    "D", "handover",           null, "Handover Certificate"),

            // Healthcare Pack H-8 — Room Data Sheet (NHS ADB / HBN-driven)
            ("E17", "healthcare_rds.docx",          "E", "rds",                null, "Room Data Sheet"),
        };

        /// <summary>Streams embedded files + writes defaults on first run (idempotent).</summary>
        public static void ExtractIfMissing(Document doc)
        {
            if (doc == null) return;
            try
            {
                ExtractTemplates(doc);
                ExtractDefaultWorkflows(doc);
                ExtractDefaultManifest(doc);
            }
            catch (Exception ex)
            {
                StingLog.Error("EmbeddedTemplates.ExtractIfMissing failed", ex);
            }
        }

        public static void ExtractTemplates(Document doc)
        {
            string templatesDir = Path.Combine(ResolveProjectRoot(doc), "_BIM_COORD", "templates");
            Directory.CreateDirectory(templatesDir);

            var asm = typeof(EmbeddedTemplates).Assembly;
            foreach (var entry in Catalogue)
            {
                string target = Path.Combine(templatesDir, entry.File);
                if (File.Exists(target)) continue;
                string resourceName = FindResource(asm, TemplateResourcePrefix, entry.File);
                if (resourceName == null)
                {
                    // HC-03: For the healthcare RDS template specifically, fall back to writing a
                    // plain-text stub that explains the token contract.  The RDS render pipeline
                    // (RdsTokenContext → TemplateEngine) will detect the .docx absence, find the
                    // .txt stub, and write a plain-text summary to _BIM_COORD/generated/ so that
                    // the issue command always produces output even without the Word template.
                    if (entry.Id == "E17" && entry.Purpose == "rds")
                    {
                        WritePlainTextRdsStub(target);
                    }
                    else
                    {
                        StingLog.Warn($"EmbeddedTemplates: resource missing for {entry.File}");
                    }
                    continue;
                }
                StreamToDisk(asm, resourceName, target);
            }
        }

        public static void ExtractDefaultWorkflows(Document doc)
        {
            string workflowsDir = Path.Combine(ResolveProjectRoot(doc), "_BIM_COORD", "workflows");
            Directory.CreateDirectory(workflowsDir);

            var asm = typeof(EmbeddedTemplates).Assembly;
            foreach (string id in new[] {
                "transmittal_default", "rfi_default", "tq_default",
                "mr_default", "deliverable_issue_default" })
            {
                string fileName = id + ".json";
                string target = Path.Combine(workflowsDir, fileName);
                if (File.Exists(target)) continue;
                string resourceName = FindResource(asm, WorkflowResourcePrefix, fileName);
                if (resourceName == null)
                {
                    StingLog.Warn($"EmbeddedTemplates: workflow resource missing for {fileName}");
                    continue;
                }
                StreamToDisk(asm, resourceName, target);
            }
        }

        public static void ExtractDefaultManifest(Document doc)
        {
            string root = ResolveProjectRoot(doc);
            string templatesDir = Path.Combine(root, "_BIM_COORD", "templates");
            Directory.CreateDirectory(templatesDir);
            string manifestPath = Path.Combine(templatesDir, "manifest.json");
            if (File.Exists(manifestPath)) return;

            var manifest = TemplateManifestIO.CreateDefault(doc);
            foreach (var entry in Catalogue)
            {
                manifest.Templates.Add(new TemplateEntry
                {
                    Id          = entry.Id,
                    Family      = entry.Family,
                    Purpose     = entry.Purpose,
                    Name        = entry.Desc,
                    Description = entry.Desc,
                    FileRelative= entry.File,
                    Extension   = Path.GetExtension(entry.File),
                    WorkflowId  = entry.Workflow
                });
            }
            manifest.Save(manifestPath);
        }

        // HC-03 — Plain-text RDS fallback written to the templates directory when the
        // healthcare_rds.docx embedded resource is absent.  The RDS render pipeline checks
        // for a .txt file with the same base name and renders a plain-text room-data summary
        // using the token context rather than failing silently.
        private static void WritePlainTextRdsStub(string docxPath)
        {
            try
            {
                string txtPath = Path.ChangeExtension(docxPath, ".txt");
                if (File.Exists(txtPath)) return;

                string stubContent =
                    "STING HEALTHCARE — ROOM DATA SHEET TEMPLATE STUB\r\n" +
                    "=================================================\r\n" +
                    "\r\n" +
                    "This file is a plain-text fallback used by the STING RDS render pipeline\r\n" +
                    "when the full healthcare_rds.docx Word template is not present.\r\n" +
                    "\r\n" +
                    "To author the full Word template, use the token contract defined in\r\n" +
                    "StingTools/Docs/Templates/TokenContext.cs (RdsTokenContext inner class).\r\n" +
                    "\r\n" +
                    "Mandatory tokens that must be present in the .docx as {{token}} placeholders:\r\n" +
                    "  {{RoomNumber}}       — Room number (ASS_LOC_TXT + ASS_LVL_COD_TXT + SEQ)\r\n" +
                    "  {{RoomName}}         — Room name from Revit Room.Name\r\n" +
                    "  {{RoomClass}}        — CLN_ROOM_CLASS_TXT (e.g. CONSULTING, OR, ICU_BAY)\r\n" +
                    "  {{DesignAch}}        — CLN_DESIGN_ACH_INT (target air changes per hour)\r\n" +
                    "  {{DesignPressure}}   — CLN_DESIGN_PRESSURE_DELTA_PA_INT (Pa relative pressure)\r\n" +
                    "  {{DesignTemp}}       — CLN_DESIGN_TEMP_C_DBL (°C set-point)\r\n" +
                    "  {{DesignRh}}         — CLN_DESIGN_RH_PCT_INT (% relative humidity)\r\n" +
                    "  {{NoiseNr}}          — CLN_NOISE_NR_TXT (NR target, e.g. NR-35)\r\n" +
                    "  {{AntiLigature}}     — LIG_PRODUCT_RATING_TXT (YES / NO / PARTIAL)\r\n" +
                    "  {{FireRating}}       — FIR_RATING_TXT (e.g. 60/60/60)\r\n" +
                    "  {{ProjectCode}}      — PRJ_ORG_PROJECT_CODE_TXT\r\n" +
                    "  {{OriginatorCode}}   — PRJ_ORG_ORIGINATOR_CODE_TXT\r\n" +
                    "  {{IssueDate}}        — Current date at render time\r\n" +
                    "  {{Revision}}         — ASS_REV_TXT\r\n" +
                    "\r\n" +
                    "Place the authored healthcare_rds.docx in the same folder as this file\r\n" +
                    "(_BIM_COORD/templates/) to enable rich Word rendering.\r\n" +
                    "\r\n" +
                    "When this .txt stub is present and the .docx is absent, the RDS pipeline\r\n" +
                    "writes a plain-text summary to _BIM_COORD/generated/ with all token values\r\n" +
                    "substituted, so RDS issue commands always produce output.\r\n";

                File.WriteAllText(txtPath, stubContent, System.Text.Encoding.UTF8);
                StingLog.Info("EmbeddedTemplates: created plain-text RDS stub at " + txtPath);
            }
            catch (Exception ex)
            {
                StingLog.Error("EmbeddedTemplates.WritePlainTextRdsStub failed", ex);
            }
        }

        private static string FindResource(Assembly asm, string prefix, string fileName)
        {
            string simple = prefix + fileName;
            var names = asm.GetManifestResourceNames();
            return names.FirstOrDefault(n => string.Equals(n, simple, StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static void StreamToDisk(Assembly asm, string resourceName, string target)
        {
            using var src = asm.GetManifestResourceStream(resourceName);
            if (src == null) { StingLog.Warn($"EmbeddedTemplates: null stream for {resourceName}"); return; }
            using var dst = File.Create(target);
            src.CopyTo(dst);
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
