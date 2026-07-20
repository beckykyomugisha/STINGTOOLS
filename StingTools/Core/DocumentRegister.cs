// DocumentRegister.cs — ISO 19650 consolidation (WP8, safe additive step).
//
// A READ-ONLY unified view over the two document registers that coexist today:
//
//   _data/STING_BIM_MANAGER/document_register.json  — the broad register (manual
//        entries + auto-registered exports; snake_case; direction IN/OUT)
//   _data/_BIM_COORD/deliverables.json              — the lifecycle deliverables
//        (DeliverableRow via JObject.FromObject; PascalCase; DocNumber-keyed)
//
// These are genuinely different schemas for overlapping-but-distinct purposes, so a
// destructive merge is deferred (it needs a dry-run migration command + in-Revit
// verification — see docs/ROADMAP.md). This class touches NEITHER write path: it reads
// both stores, normalises each row into a common RegisterEntry, de-duplicates by id, and
// returns one list. It gives "one register" for reporting/export without risking data.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace StingTools.Core
{
    /// <summary>One normalised row of the unified document register.</summary>
    public class RegisterEntry
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Type { get; set; } = "";
        public string Discipline { get; set; } = "";
        public string Suitability { get; set; } = "";
        public string CdeStatus { get; set; } = "";
        public string Revision { get; set; } = "";
        public string Direction { get; set; } = "";
        public string Status { get; set; } = "";
        public string ReviewedBy { get; set; } = "";
        public string ApprovedBy { get; set; } = "";
        public string DateCreated { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string FileFormat { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        /// <summary>Which store(s) this row came from: "register", "deliverable", or "both".</summary>
        public string Source { get; set; } = "";
    }

    /// <summary>Read-only unifier over the two document-register stores.</summary>
    public static class DocumentRegister
    {
        /// <summary>
        /// Build the merged, de-duplicated register view. Never writes. Rows present in
        /// both stores (matched on id) collapse to one entry sourced "both", preferring the
        /// deliverable row's lifecycle fields and filling gaps from the register row.
        /// </summary>
        public static List<RegisterEntry> BuildUnified(Document doc)
        {
            var byId = new Dictionary<string, RegisterEntry>(StringComparer.OrdinalIgnoreCase);
            var noId = new List<RegisterEntry>();

            void Add(RegisterEntry e)
            {
                if (e == null) return;
                if (string.IsNullOrWhiteSpace(e.Id)) { noId.Add(e); return; }
                if (byId.TryGetValue(e.Id, out var existing))
                {
                    Merge(existing, e);
                    existing.Source = "both";
                }
                else byId[e.Id] = e;
            }

            // Deliverables first, so their richer lifecycle fields win on collision.
            foreach (var e in ReadDeliverables(doc)) Add(e);
            foreach (var e in ReadRegister(doc)) Add(e);

            return byId.Values.Concat(noId)
                .OrderBy(e => e.Direction).ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Export the unified view to a CSV in the routed REGISTERS folder; returns the path.</summary>
        public static string ExportCsv(Document doc)
        {
            var rows = BuildUnified(doc);
            var sb = new StringBuilder();
            sb.AppendLine("Id,Title,Type,Discipline,Suitability,CDEStatus,Revision,Direction,Status," +
                          "ReviewedBy,ApprovedBy,CreatedBy,DateCreated,FileFormat,Source,FilePath");
            foreach (var r in rows)
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(r.Id), Csv(r.Title), Csv(r.Type), Csv(r.Discipline), Csv(r.Suitability),
                    Csv(r.CdeStatus), Csv(r.Revision), Csv(r.Direction), Csv(r.Status),
                    Csv(r.ReviewedBy), Csv(r.ApprovedBy), Csv(r.CreatedBy), Csv(r.DateCreated),
                    Csv(r.FileFormat), Csv(r.Source), Csv(r.FilePath)
                }));

            string path = ProjectFolderEngine.GetExportPath(doc, "DocRegister", "STING_Unified_Register", ".csv");
            OutputLocationHelper.WriteAllTextAtomic(path, sb.ToString());
            ProjectFolderEngine.LogActivity(doc, "EXPORT_UNIFIED_REGISTER", Path.GetFileName(path), $"{rows.Count} rows");
            return path;
        }

        /// <summary>
        /// Write the merged view to the canonical <c>_data/register.json</c>. NON-destructive:
        /// the two source stores are left intact, so this can be re-run any time and the UIs
        /// keep working. Returns the path written.
        /// </summary>
        public static string WriteCanonical(Document doc)
        {
            var rows = BuildUnified(doc);
            var arr = new JArray(rows.Select(r => new JObject
            {
                ["id"] = r.Id, ["title"] = r.Title, ["type"] = r.Type, ["discipline"] = r.Discipline,
                ["suitability"] = r.Suitability, ["cde_status"] = r.CdeStatus, ["revision"] = r.Revision,
                ["direction"] = r.Direction, ["status"] = r.Status, ["reviewed_by"] = r.ReviewedBy,
                ["approved_by"] = r.ApprovedBy, ["created_by"] = r.CreatedBy,
                ["date_created"] = r.DateCreated, ["file_format"] = r.FileFormat,
                ["file_path"] = r.FilePath, ["source"] = r.Source
            }));
            string path = ProjectFolderEngine.GetDataPath(doc, CanonicalFileName);
            OutputLocationHelper.WriteAllTextAtomic(path, arr.ToString(Newtonsoft.Json.Formatting.Indented));
            ProjectFolderEngine.LogActivity(doc, "WRITE_CANONICAL_REGISTER", CanonicalFileName, $"{rows.Count} rows");
            return path;
        }

        /// <summary>Canonical unified register filename under &lt;root&gt;/_data/.</summary>
        public const string CanonicalFileName = "register.json";

        // ── Store readers ─────────────────────────────────────────────────

        private static IEnumerable<RegisterEntry> ReadRegister(Document doc)
        {
            string path;
            try { path = CoordStores.Register(doc); }
            catch (Exception ex) { StingLog.Warn($"DocumentRegister.ReadRegister path: {ex.Message}"); yield break; }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) yield break;

            JArray arr;
            try { arr = JArray.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"DocumentRegister.ReadRegister parse: {ex.Message}"); yield break; }

            foreach (var t in arr.OfType<JObject>())
            {
                string source = Str(t, "source");
                string direction = Str(t, "direction");
                if (string.IsNullOrEmpty(direction))
                    direction = source.IndexOf("Auto-Export", StringComparison.OrdinalIgnoreCase) >= 0 ? "OUT" : "";
                yield return new RegisterEntry
                {
                    // doc_number first: deliverable-sourced rows carry the ISO 19650 number,
                    // which is the SAME key deliverables.json uses — without it the two
                    // stores live in disjoint id namespaces and dedup could never fire
                    // ("both" was always 0 and every deliverable appeared twice).
                    Id          = First(t, "doc_number", "doc_id", "document_id"),
                    Title       = First(t, "title", "file_name", "description"),
                    Type        = First(t, "type", "document_type"),
                    Discipline  = Str(t, "discipline"),
                    Suitability = Str(t, "suitability"),
                    CdeStatus   = First(t, "cde_status", "status"),
                    Revision    = Str(t, "revision"),
                    Direction   = direction,
                    Status      = First(t, "status", "review_status"),
                    ReviewedBy  = Str(t, "reviewed_by"),
                    DateCreated = Str(t, "date_created"),
                    FilePath    = First(t, "file_reference", "file_path"),
                    FileFormat  = First(t, "file_format", "format"),
                    CreatedBy   = First(t, "created_by", "author"),
                    Source      = "register"
                };
            }
        }

        private static IEnumerable<RegisterEntry> ReadDeliverables(Document doc)
        {
            string dir;
            try { dir = ProjectFolderEngine.GetMetaPath(doc, "_BIM_COORD"); }
            catch (Exception ex) { StingLog.Warn($"DocumentRegister.ReadDeliverables path: {ex.Message}"); yield break; }
            if (string.IsNullOrEmpty(dir)) yield break;
            string path = Path.Combine(dir, "deliverables.json");
            if (!File.Exists(path)) yield break;

            JArray arr;
            try { arr = JArray.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"DocumentRegister.ReadDeliverables parse: {ex.Message}"); yield break; }

            foreach (var t in arr.OfType<JObject>())
            {
                yield return new RegisterEntry
                {
                    Id          = First(t, "DocNumber", "Code"),
                    Title       = First(t, "Name", "Title"),
                    Type        = Str(t, "Type"),
                    Discipline  = Str(t, "Discipline"),
                    Suitability = Str(t, "Suitability"),
                    CdeStatus   = Str(t, "CDE"),
                    Revision    = Str(t, "Revision"),
                    Direction   = "OUT",
                    Status      = Str(t, "Status"),
                    ReviewedBy  = Str(t, "ReviewedBy"),
                    ApprovedBy  = Str(t, "ApprovedBy"),
                    DateCreated = LatestRevisionTs(t),
                    FilePath    = Str(t, "SignedFilePath"),
                    FileFormat  = First(t, "FileFormat", "Format"),
                    CreatedBy   = First(t, "CreatedBy", "Author", "Originator"),
                    Source      = "deliverable"
                };
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>Fill empty fields of <paramref name="keep"/> from <paramref name="other"/>.</summary>
        private static void Merge(RegisterEntry keep, RegisterEntry other)
        {
            if (string.IsNullOrEmpty(keep.Title))       keep.Title       = other.Title;
            if (string.IsNullOrEmpty(keep.Type))        keep.Type        = other.Type;
            if (string.IsNullOrEmpty(keep.Discipline))  keep.Discipline  = other.Discipline;
            if (string.IsNullOrEmpty(keep.Suitability)) keep.Suitability = other.Suitability;
            if (string.IsNullOrEmpty(keep.CdeStatus))   keep.CdeStatus   = other.CdeStatus;
            if (string.IsNullOrEmpty(keep.Revision))    keep.Revision    = other.Revision;
            if (string.IsNullOrEmpty(keep.Direction))   keep.Direction   = other.Direction;
            if (string.IsNullOrEmpty(keep.Status))      keep.Status      = other.Status;
            if (string.IsNullOrEmpty(keep.ReviewedBy))  keep.ReviewedBy  = other.ReviewedBy;
            if (string.IsNullOrEmpty(keep.ApprovedBy))  keep.ApprovedBy  = other.ApprovedBy;
            if (string.IsNullOrEmpty(keep.DateCreated)) keep.DateCreated = other.DateCreated;
            if (string.IsNullOrEmpty(keep.FilePath))    keep.FilePath    = other.FilePath;
            if (string.IsNullOrEmpty(keep.FileFormat))  keep.FileFormat  = other.FileFormat;
            if (string.IsNullOrEmpty(keep.CreatedBy))   keep.CreatedBy   = other.CreatedBy;
        }

        private static string LatestRevisionTs(JObject row)
        {
            var hist = row["RevisionHistory"] as JArray ?? row["revision_history"] as JArray;
            if (hist == null) return "";
            string best = "";
            DateTime bestDt = DateTime.MinValue;
            foreach (var h in hist.OfType<JObject>())
            {
                string ts = First(h, "Timestamp", "timestamp");
                if (DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) && dt > bestDt)
                { bestDt = dt; best = ts; }
            }
            return best;
        }

        private static string Str(JObject o, string key) => o[key]?.ToString() ?? "";

        private static string First(JObject o, params string[] keys)
        {
            foreach (string k in keys)
            {
                string v = o[k]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return "";
        }

        private static string Csv(string s)
        {
            s ??= "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }

    /// <summary>
    /// Read-only export of the unified document register (both stores merged).
    /// Command tag: <c>DocRegister_Unified</c>.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class UnifiedRegisterExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var rows = DocumentRegister.BuildUnified(doc);
                string path = DocumentRegister.ExportCsv(doc);

                int both = rows.Count(r => r.Source == "both");
                int reg  = rows.Count(r => r.Source == "register");
                int del  = rows.Count(r => r.Source == "deliverable");
                TaskDialog.Show("STING — Unified Register",
                    $"Merged register exported ({rows.Count} rows):\n\n" +
                    $"  {del} deliverables\n  {reg} register-only\n  {both} in both stores\n\n{path}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("UnifiedRegisterExportCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Consolidate the two document registers into the canonical <c>_data/register.json</c>.
    /// Dry-run first: it always writes a preview CSV of the merged view and shows the counts,
    /// then asks before writing <c>register.json</c>. NON-destructive — the two source stores
    /// are never modified or deleted, so this is safe to run and re-run. Command tag:
    /// <c>Register_Consolidate</c>.
    /// </summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class RegisterConsolidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var rows = DocumentRegister.BuildUnified(doc);
                string csv = DocumentRegister.ExportCsv(doc); // dry-run preview
                int both = rows.Count(r => r.Source == "both");
                int reg  = rows.Count(r => r.Source == "register");
                int del  = rows.Count(r => r.Source == "deliverable");

                var td = new TaskDialog("STING — Consolidate Register")
                {
                    MainInstruction = $"Merge {rows.Count} rows into one canonical register?",
                    MainContent =
                        $"Dry-run preview written to:\n{csv}\n\n" +
                        $"  {del} deliverables\n  {reg} register-only\n  {both} in both stores\n\n" +
                        "Applying writes _data/register.json. The two source stores " +
                        "(deliverables.json, document_register.json) are kept unchanged, so this is " +
                        "reversible — just delete register.json. Continue?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                if (td.Show() != TaskDialogResult.Yes)
                {
                    TaskDialog.Show("STING — Consolidate Register",
                        $"Dry-run only. No canonical register written.\nPreview: {csv}");
                    return Result.Cancelled;
                }

                string path = DocumentRegister.WriteCanonical(doc);
                TaskDialog.Show("STING — Consolidate Register",
                    $"Canonical register written ({rows.Count} rows):\n{path}\n\n" +
                    "Source stores were left intact.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("RegisterConsolidateCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
