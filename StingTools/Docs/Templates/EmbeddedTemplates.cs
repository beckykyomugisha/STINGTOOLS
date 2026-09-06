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

            // Phase 192 — Lightning Protection spec sheet (Wave 4 follow-up).
            // Token surface: spd.tag / spd.location_label / spd.iimp_ka /
            // spd.up_kv / spd.uc_v / spd.poles / spd.verdict / project.lps_class.
            // Rendered by LpsSpdSpecSheetCommand from the LPS panel SPD tab.
            ("E17", "lps_spd_spec.docx",            "E", "lps_spd_spec",       null, "LPS — SPD Product Specification"),
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

        // ── Template pack versioning ──────────────────────────────────────
        //
        // Extraction was "write it if the file is absent", so a project that had ever been
        // opened kept its templates forever. That was fine while the pack only gained NEW
        // files — but the pack shipped with a defect in every existing file (loop bodies used
        // bare {{number}} where MiniWord requires {{documents.number}}, so every generated
        // table came out empty), and no amount of re-opening would deliver the fix.
        //
        // Bump PackVersion whenever the embedded templates change. On open, a project stamped
        // with an older version is re-extracted — but ONLY for files the user has not edited.
        // A pristine file is one whose SHA-256 still matches what we wrote; anything else is
        // the user's work and is never overwritten. Those are logged, and the render gate
        // (TemplateDoctor) still catches the broken output at issue time, so a customised
        // template cannot fail silently either.

        private const int PackVersion = 2;   // v2: loop-body tokens rewritten to dotted form
        private const string StampFile = ".sting_template_pack.json";

        public static void ExtractTemplates(Document doc)
        {
            string templatesDir = StingPaths.MetaFile(doc, "_BIM_COORD", "templates");
            Directory.CreateDirectory(templatesDir);

            string stampPath = Path.Combine(templatesDir, StampFile);
            var stamp = ReadStamp(stampPath);
            bool packIsNewer = stamp.Version < PackVersion;

            var asm = typeof(EmbeddedTemplates).Assembly;
            var customised = new List<string>();
            int refreshed = 0;

            foreach (var entry in Catalogue)
            {
                string target = Path.Combine(templatesDir, entry.File);
                bool exists = File.Exists(target);

                if (exists && !packIsNewer) continue;

                if (exists)
                {
                    // Replace only what we ourselves last wrote.
                    string current = Sha256(target);
                    if (!stamp.Hashes.TryGetValue(entry.File, out string known) ||
                        !string.Equals(current, known, StringComparison.OrdinalIgnoreCase))
                    {
                        customised.Add(entry.File);
                        continue;
                    }
                }

                string resourceName = FindResource(asm, TemplateResourcePrefix, entry.File);
                if (resourceName == null)
                {
                    StingLog.Warn($"EmbeddedTemplates: resource missing for {entry.File}");
                    continue;
                }
                StreamToDisk(asm, resourceName, target);
                if (exists) refreshed++;
                stamp.Hashes[entry.File] = Sha256(target);
            }

            if (customised.Count > 0)
                StingLog.Warn($"EmbeddedTemplates: template pack v{PackVersion} not applied to " +
                              $"{customised.Count} locally-edited template(s): {string.Join(", ", customised)}. " +
                              "They keep your edits — re-apply them to the new pack if documents render with " +
                              "empty tables or leftover {{…}} markers.");
            if (refreshed > 0)
                StingLog.Info($"EmbeddedTemplates: refreshed {refreshed} template(s) to pack v{PackVersion}.");

            stamp.Version = PackVersion;
            WriteStamp(stampPath, stamp);
        }

        private class PackStamp
        {
            public int Version { get; set; }
            public Dictionary<string, string> Hashes { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static PackStamp ReadStamp(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<PackStamp>(File.ReadAllText(path)) ?? new PackStamp();
            }
            catch (Exception ex) { StingLog.Warn($"EmbeddedTemplates stamp read: {ex.Message}"); }
            return new PackStamp();
        }

        private static void WriteStamp(string path, PackStamp stamp)
        {
            try
            {
                OutputLocationHelper.WriteAllTextAtomic(path,
                    JsonConvert.SerializeObject(stamp, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"EmbeddedTemplates stamp write: {ex.Message}"); }
        }

        private static string Sha256(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
            }
            catch (Exception ex) { StingLog.Warn($"EmbeddedTemplates hash({Path.GetFileName(path)}): {ex.Message}"); return null; }
        }

        public static void ExtractDefaultWorkflows(Document doc)
        {
            string workflowsDir = StingPaths.MetaFile(doc, "_BIM_COORD", "workflows");
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
            string templatesDir = StingPaths.MetaFile(doc, "_BIM_COORD", "templates");
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
