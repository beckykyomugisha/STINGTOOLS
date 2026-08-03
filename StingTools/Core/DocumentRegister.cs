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
    // RegisterEntry now lives in DocumentRegisterMerge.cs (same namespace) alongside the
    // Revit-free mapping + merge core it belongs to. This class keeps the Document-facing
    // half: path resolution, file IO, CSV/canonical export.

    /// <summary>Read-only unifier over the two document-register stores.</summary>
    public static class DocumentRegister
    {
        /// <summary>
        /// Build the merged, de-duplicated register view. Never writes. Rows present in
        /// both stores (matched on id) collapse to one entry sourced "both", preferring the
        /// deliverable row's lifecycle fields and filling gaps from the register row.
        /// </summary>
        public static List<RegisterEntry> BuildUnified(Document doc) => BuildUnified(doc, out _);

        /// <summary>
        /// As <see cref="BuildUnified(Document)"/>, reporting whether either source store failed
        /// to read. A merge that silently drops one store looks identical to a merge of an empty
        /// store, so a caller presenting the result to a user needs to be able to tell them apart.
        /// </summary>
        public static List<RegisterEntry> BuildUnified(Document doc, out bool anyStoreFailed)
        {
            var flag = new ReadFlag();
            // Deliverables first, so their richer lifecycle fields win on collision.
            // The id-keyed dedup (including the IM-13 trim rule) lives in DocumentRegisterMerge.
            var merged = DocumentRegisterMerge.Merge(ReadDeliverables(doc, flag).ToList(),
                                                     ReadRegister(doc, flag).ToList());
            anyStoreFailed = flag.Failed;
            return merged;
        }

        /// <summary>Export the unified view to a CSV in the routed REGISTERS folder; returns the path.</summary>
        public static string ExportCsv(Document doc) => ExportCsv(doc, null);

        /// <summary>
        /// As <see cref="ExportCsv(Document)"/>, but reuses an already-built row set. Callers that
        /// report counts to the user MUST pass their rows: rebuilding re-reads both stores, so the
        /// numbers in the dialog could otherwise describe a different set than the file on disk.
        /// </summary>
        public static string ExportCsv(Document doc, List<RegisterEntry> prebuilt)
        {
            var rows = prebuilt ?? BuildUnified(doc);
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
        public static string WriteCanonical(Document doc) => WriteCanonical(doc, null);

        /// <summary>As <see cref="WriteCanonical(Document)"/>, reusing an already-built row set.</summary>
        public static string WriteCanonical(Document doc, List<RegisterEntry> prebuilt)
        {
            var rows = prebuilt ?? BuildUnified(doc);
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

        /// <summary>Mutable failure flag — iterators cannot take ref/out parameters.</summary>
        private class ReadFlag { public bool Failed; }

        private static IEnumerable<RegisterEntry> ReadRegister(Document doc, ReadFlag flag)
        {
            string path;
            try { path = CoordStores.Register(doc); }
            catch (Exception ex) { StingLog.Warn($"DocumentRegister.ReadRegister path: {ex.Message}"); flag.Failed = true; yield break; }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) yield break;   // absent ⇒ legitimately empty

            JArray arr;
            try { arr = JArray.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"DocumentRegister.ReadRegister parse: {ex.Message}"); flag.Failed = true; yield break; }

            foreach (var t in arr.OfType<JObject>())
                yield return DocumentRegisterMerge.MapRegisterRow(t);
        }

        private static IEnumerable<RegisterEntry> ReadDeliverables(Document doc, ReadFlag flag)
        {
            string dir;
            try { dir = ProjectFolderEngine.GetMetaPath(doc, "_BIM_COORD"); }
            catch (Exception ex) { StingLog.Warn($"DocumentRegister.ReadDeliverables path: {ex.Message}"); flag.Failed = true; yield break; }
            if (string.IsNullOrEmpty(dir)) yield break;
            string path = Path.Combine(dir, "deliverables.json");
            if (!File.Exists(path)) yield break;   // absent ⇒ legitimately empty

            JArray arr;
            try { arr = JArray.Parse(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"DocumentRegister.ReadDeliverables parse: {ex.Message}"); flag.Failed = true; yield break; }

            foreach (var t in arr.OfType<JObject>())
                yield return DocumentRegisterMerge.MapDeliverableRow(t);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        // Row mapping, the id-keyed merge, LatestRevisionTs and the First/Str accessors all
        // moved to DocumentRegisterMerge (Revit-free) under IM-13. Keeping private copies here
        // is exactly how the three identity resolvers drifted apart in the first place.

        private static string Csv(string s)
        {
            s ??= "";
            // Neutralise spreadsheet formula injection. Register fields (title, reason, user)
            // are free text sourced from other people's transmittals; a value starting = + - @
            // is executed as a formula the moment the exported CSV is opened in Excel. Quoting
            // alone does NOT prevent this — the leading apostrophe does.
            if (s.Length > 0 && "=+-@\t\r".IndexOf(s[0]) >= 0) s = "'" + s;
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
                string path = DocumentRegister.ExportCsv(doc, rows);

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
                string csv = DocumentRegister.ExportCsv(doc, rows); // dry-run preview
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

                // Same rows the dry-run preview and the counts above describe — rebuilding here
                // would re-read both stores and could write a set the user never reviewed.
                string path = DocumentRegister.WriteCanonical(doc, rows);
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
