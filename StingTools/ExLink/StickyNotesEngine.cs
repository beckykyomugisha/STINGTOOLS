using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.ExLink
{
    // ════════════════════════════════════════════════════════════════════════
    //  STICKY NOTES ENGINE — Per-element notes with JSON sidecar
    //
    //  Stores notes in {name}.sting_sticky_notes.json alongside the .rvt.
    //  Each note is linked to an ElementId and optionally an ISO 19650 tag.
    //
    //  StickyNote (data class):
    //    int Id, long ElementId, string ElementTag, Note, Priority,
    //    Owner, DueDate, CreatedDate, Status, Category
    //
    //  StickyNotesEngine (internal static):
    //    GetSidecarPath, LoadNotes, SaveNotes, AddNote, UpdateNote,
    //    DeleteNote, GetOverdueNotes, ExportToCSV, GetNextId
    //
    //  4 IExternalCommand classes:
    //    1. StickyNoteCreateCommand      [Manual]
    //    2. StickyNoteDashboardCommand   [ReadOnly]
    //    3. StickyNoteExportCommand      [ReadOnly]
    //    4. StickyNoteBulkUpdateCommand  [Manual]
    // ════════════════════════════════════════════════════════════════════════

    #region -- Data Model --

    internal class StickyNote
    {
        public int Id { get; set; }
        public long ElementId { get; set; }
        public string ElementTag { get; set; } = "";
        public string Note { get; set; } = "";
        public string Priority { get; set; } = "MEDIUM";
        public string Owner { get; set; } = "";
        public string DueDate { get; set; } = "";
        public string CreatedDate { get; set; } = "";
        public string Status { get; set; } = "OPEN";
        public string Category { get; set; } = "";

        internal JObject ToJson()
        {
            var obj = new JObject
            {
                ["id"] = Id,
                ["element_id"] = ElementId,
                ["element_tag"] = ElementTag ?? "",
                ["note"] = Note ?? "",
                ["priority"] = Priority ?? "MEDIUM",
                ["owner"] = Owner ?? "",
                ["due_date"] = DueDate ?? "",
                ["created_date"] = CreatedDate ?? "",
                ["status"] = Status ?? "OPEN",
                ["category"] = Category ?? ""
            };
            return obj;
        }

        internal static StickyNote FromJson(JObject obj)
        {
            if (obj == null) return null;
            return new StickyNote
            {
                Id = (int)(obj["id"] ?? 0),
                ElementId = (long)(obj["element_id"] ?? 0L),
                ElementTag = (string)obj["element_tag"] ?? "",
                Note = (string)obj["note"] ?? "",
                Priority = (string)obj["priority"] ?? "MEDIUM",
                Owner = (string)obj["owner"] ?? "",
                DueDate = (string)obj["due_date"] ?? "",
                CreatedDate = (string)obj["created_date"] ?? "",
                Status = (string)obj["status"] ?? "OPEN",
                Category = (string)obj["category"] ?? ""
            };
        }
    }

    #endregion

    #region -- Engine --

    internal static class StickyNotesEngine
    {
        /// <summary>
        /// Returns {name}.sting_sticky_notes.json alongside the .rvt file.
        /// Falls back to temp directory for unsaved documents.
        /// </summary>
        internal static string GetSidecarPath(Document doc)
        {
            try
            {
                if (doc == null) return null;
                string rvtPath = doc.PathName;
                if (!string.IsNullOrEmpty(rvtPath))
                {
                    string dir = Path.GetDirectoryName(rvtPath);
                    string name = Path.GetFileNameWithoutExtension(rvtPath);
                    return Path.Combine(dir, $"{name}.sting_sticky_notes.json");
                }
                string tempDir = Path.Combine(Path.GetTempPath(), "StingTools");
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);
                string title = doc.Title ?? "Untitled";
                return Path.Combine(tempDir, $"{title}.sting_sticky_notes.json");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StickyNotesEngine.GetSidecarPath: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load all sticky notes from the JSON sidecar. Returns empty list on failure.
        /// </summary>
        internal static List<StickyNote> LoadNotes(Document doc)
        {
            var notes = new List<StickyNote>();
            try
            {
                string path = GetSidecarPath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return notes;

                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return notes;

                var arr = JArray.Parse(json);
                foreach (var token in arr)
                {
                    if (token is JObject obj)
                    {
                        var note = StickyNote.FromJson(obj);
                        if (note != null)
                            notes.Add(note);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StickyNotesEngine.LoadNotes: {ex.Message}");
            }
            return notes;
        }

        /// <summary>
        /// Save notes to sidecar using atomic temp+rename.
        /// </summary>
        internal static void SaveNotes(Document doc, List<StickyNote> notes)
        {
            try
            {
                string path = GetSidecarPath(doc);
                if (string.IsNullOrEmpty(path)) return;

                var arr = new JArray();
                foreach (var n in notes)
                    arr.Add(n.ToJson());

                string json = arr.ToString(Newtonsoft.Json.Formatting.Indented);
                string tmpPath = path + ".tmp";
                File.WriteAllText(tmpPath, json);

                if (File.Exists(path))
                {
                    string bakPath = path + ".bak";
                    File.Replace(tmpPath, path, bakPath);
                    try { if (File.Exists(bakPath)) File.Delete(bakPath); }
                    catch (Exception) { /* ignore */ }
                }
                else
                {
                    File.Move(tmpPath, path);
                }

                StingLog.Info($"StickyNotesEngine: Saved {notes.Count} notes");
            }
            catch (Exception ex)
            {
                StingLog.Error("StickyNotesEngine.SaveNotes failed", ex);
            }
        }

        /// <summary>
        /// Returns the next available note Id (max existing + 1).
        /// </summary>
        internal static int GetNextId(List<StickyNote> notes)
        {
            if (notes == null || notes.Count == 0) return 1;
            return notes.Max(n => n.Id) + 1;
        }

        /// <summary>
        /// Add a new sticky note for an element. Returns the created note.
        /// </summary>
        internal static StickyNote AddNote(
            Document doc, long elementId, string elementTag,
            string note, string priority, string owner, string dueDate)
        {
            var notes = LoadNotes(doc);
            int nextId = GetNextId(notes);

            string category = "";
            try
            {
                var el = doc.GetElement(new ElementId(elementId));
                if (el?.Category != null)
                    category = el.Category.Name;
            }
            catch (Exception ex) { StingLog.Warn($"StickyNotesEngine.AddNote category: {ex.Message}"); }

            var stickyNote = new StickyNote
            {
                Id = nextId,
                ElementId = elementId,
                ElementTag = elementTag ?? "",
                Note = note ?? "",
                Priority = string.IsNullOrEmpty(priority) ? "MEDIUM" : priority.ToUpperInvariant(),
                Owner = string.IsNullOrEmpty(owner) ? Environment.UserName : owner,
                DueDate = dueDate ?? "",
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Status = "OPEN",
                Category = category
            };

            notes.Add(stickyNote);
            SaveNotes(doc, notes);
            StingLog.Info($"StickyNote #{nextId} created for element {elementId}");
            return stickyNote;
        }

        /// <summary>
        /// Update the status of an existing note by Id.
        /// </summary>
        internal static bool UpdateNote(Document doc, int noteId, string status)
        {
            try
            {
                var notes = LoadNotes(doc);
                var note = notes.FirstOrDefault(n => n.Id == noteId);
                if (note == null) return false;

                note.Status = status?.ToUpperInvariant() ?? "OPEN";
                SaveNotes(doc, notes);
                StingLog.Info($"StickyNote #{noteId} → {note.Status}");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StickyNotesEngine.UpdateNote: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete a note by Id. Returns true if found and removed.
        /// </summary>
        internal static bool DeleteNote(Document doc, int noteId)
        {
            try
            {
                var notes = LoadNotes(doc);
                int removed = notes.RemoveAll(n => n.Id == noteId);
                if (removed == 0) return false;

                SaveNotes(doc, notes);
                StingLog.Info($"StickyNote #{noteId} deleted");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StickyNotesEngine.DeleteNote: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Returns notes that are overdue (DueDate is in the past and status is OPEN/IN_PROGRESS).
        /// </summary>
        internal static List<StickyNote> GetOverdueNotes(Document doc)
        {
            var overdue = new List<StickyNote>();
            try
            {
                var notes = LoadNotes(doc);
                var now = DateTime.Now;
                foreach (var n in notes)
                {
                    if (n.Status != "OPEN" && n.Status != "IN_PROGRESS") continue;
                    if (string.IsNullOrEmpty(n.DueDate)) continue;
                    if (DateTime.TryParse(n.DueDate, out var due) && due < now)
                        overdue.Add(n);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StickyNotesEngine.GetOverdueNotes: {ex.Message}");
            }
            return overdue;
        }

        /// <summary>
        /// Export all notes to CSV. Returns the number exported.
        /// </summary>
        internal static int ExportToCSV(Document doc, string csvPath)
        {
            try
            {
                var notes = LoadNotes(doc);
                if (notes.Count == 0) return 0;

                using (var sw = new StreamWriter(csvPath))
                {
                    sw.WriteLine("Id,ElementId,ElementTag,Category,Note,Priority,Owner,DueDate,CreatedDate,Status");
                    foreach (var n in notes)
                    {
                        sw.WriteLine(string.Join(",",
                            n.Id,
                            n.ElementId,
                            Esc(n.ElementTag),
                            Esc(n.Category),
                            Esc(n.Note),
                            Esc(n.Priority),
                            Esc(n.Owner),
                            Esc(n.DueDate),
                            Esc(n.CreatedDate),
                            Esc(n.Status)));
                    }
                }
                StingLog.Info($"StickyNotesEngine: Exported {notes.Count} notes to {csvPath}");
                return notes.Count;
            }
            catch (Exception ex)
            {
                StingLog.Error("StickyNotesEngine.ExportToCSV failed", ex);
                return 0;
            }
        }

        private static string Esc(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════════
    //  STICKY NOTE COMMANDS
    // ════════════════════════════════════════════════════════════════════════

    // ────────────────────────────────────────────────────────────────────────
    //  1. StickyNoteCreateCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StickyNoteCreateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;
                var uidoc = ctx.UIDoc;

                var selIds = uidoc.Selection.GetElementIds()?.ToList();
                if (selIds == null || selIds.Count == 0)
                {
                    TaskDialog.Show("STING Sticky Notes",
                        "Please select one or more elements before creating a sticky note.");
                    return Result.Succeeded;
                }

                // Priority selection
                var dlg = new TaskDialog("STING - Create Sticky Note")
                {
                    MainInstruction = $"Create note for {selIds.Count} element(s)",
                    MainContent = "Select priority:"
                };
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "LOW", "Informational");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "MEDIUM", "Standard review");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "HIGH", "Attention needed");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "CRITICAL", "Urgent action required");

                var priResult = dlg.Show();
                if (priResult == TaskDialogResult.Cancel)
                    return Result.Cancelled;

                string priority = priResult switch
                {
                    TaskDialogResult.CommandLink1 => "LOW",
                    TaskDialogResult.CommandLink2 => "MEDIUM",
                    TaskDialogResult.CommandLink3 => "HIGH",
                    TaskDialogResult.CommandLink4 => "CRITICAL",
                    _ => "MEDIUM"
                };

                string noteText = $"Review required ({priority}) — {selIds.Count} element(s)";

                int created = 0;
                foreach (var elId in selIds)
                {
                    var el = doc.GetElement(elId);
                    if (el == null) continue;
                    string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    StickyNotesEngine.AddNote(doc, elId.Value, tag, noteText,
                        priority, Environment.UserName, "");
                    created++;
                }

                TaskDialog.Show("STING Sticky Notes",
                    $"Created {created} sticky note(s).\n\n" +
                    $"Priority: {priority}\n" +
                    $"Owner: {Environment.UserName}");

                StingLog.Info($"StickyNoteCreate: {created} notes, priority={priority}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StickyNoteCreateCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  2. StickyNoteDashboardCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StickyNoteDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                var notes = StickyNotesEngine.LoadNotes(doc);
                if (notes.Count == 0)
                {
                    TaskDialog.Show("STING Sticky Notes", "No sticky notes found.");
                    return Result.Succeeded;
                }

                int openCount = notes.Count(n => n.Status == "OPEN");
                int inProgress = notes.Count(n => n.Status == "IN_PROGRESS");
                int resolved = notes.Count(n => n.Status == "RESOLVED");
                int closed = notes.Count(n => n.Status == "CLOSED");
                int overdue = StickyNotesEngine.GetOverdueNotes(doc).Count;

                int critical = notes.Count(n => n.Priority == "CRITICAL" && n.Status != "CLOSED");
                int high = notes.Count(n => n.Priority == "HIGH" && n.Status != "CLOSED");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Total notes: {notes.Count}");
                sb.AppendLine();
                sb.AppendLine("-- Status --");
                sb.AppendLine($"  OPEN:        {openCount}");
                sb.AppendLine($"  IN_PROGRESS: {inProgress}");
                sb.AppendLine($"  RESOLVED:    {resolved}");
                sb.AppendLine($"  CLOSED:      {closed}");
                sb.AppendLine();
                sb.AppendLine("-- Priority (active) --");
                sb.AppendLine($"  CRITICAL: {critical}");
                sb.AppendLine($"  HIGH:     {high}");
                if (overdue > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"OVERDUE: {overdue} note(s) past due date");
                }

                TaskDialog.Show("STING Sticky Notes Dashboard", sb.ToString());

                StingLog.Info($"StickyDashboard: {notes.Count} notes, {openCount} open, {overdue} overdue");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StickyNoteDashboardCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  3. StickyNoteExportCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StickyNoteExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                var notes = StickyNotesEngine.LoadNotes(doc);
                if (notes.Count == 0)
                {
                    TaskDialog.Show("STING Sticky Notes", "No sticky notes to export.");
                    return Result.Succeeded;
                }

                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                string fileName = $"STING_StickyNotes_{DateTime.Now:yyyyMMdd_HHmm}.csv";
                string csvPath = Path.Combine(outDir, fileName);

                int count = StickyNotesEngine.ExportToCSV(doc, csvPath);

                TaskDialog.Show("STING Sticky Notes",
                    $"Exported {count} notes to:\n{csvPath}");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StickyNoteExportCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  4. StickyNoteBulkUpdateCommand
    // ────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StickyNoteBulkUpdateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No document open."; return Result.Failed; }
                var doc = ctx.Doc;

                var notes = StickyNotesEngine.LoadNotes(doc);
                if (notes.Count == 0)
                {
                    TaskDialog.Show("STING Sticky Notes", "No sticky notes to update.");
                    return Result.Succeeded;
                }

                // Offer 3 bulk operations
                var dlg = new TaskDialog("STING - Bulk Update Notes")
                {
                    MainInstruction = "Bulk update sticky notes",
                    MainContent = $"Total notes: {notes.Count}"
                };
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Close all RESOLVED notes",
                    "Move resolved notes to CLOSED status");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Mark all OPEN as IN_PROGRESS",
                    "Acknowledge all open notes");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Resolve all overdue notes",
                    "Mark overdue notes as RESOLVED for review");

                var result = dlg.Show();
                if (result == TaskDialogResult.Cancel)
                    return Result.Cancelled;

                int updated = 0;
                bool modified = false;

                if (result == TaskDialogResult.CommandLink1)
                {
                    foreach (var n in notes.Where(n => n.Status == "RESOLVED"))
                    {
                        n.Status = "CLOSED";
                        updated++;
                    }
                    modified = updated > 0;
                }
                else if (result == TaskDialogResult.CommandLink2)
                {
                    foreach (var n in notes.Where(n => n.Status == "OPEN"))
                    {
                        n.Status = "IN_PROGRESS";
                        updated++;
                    }
                    modified = updated > 0;
                }
                else if (result == TaskDialogResult.CommandLink3)
                {
                    var overdue = new List<StickyNote>();
                    var now = DateTime.Now;
                    foreach (var n in notes)
                    {
                        if (n.Status != "OPEN" && n.Status != "IN_PROGRESS") continue;
                        if (string.IsNullOrEmpty(n.DueDate)) continue;
                        if (DateTime.TryParse(n.DueDate, out var due) && due < now)
                            overdue.Add(n);
                    }
                    foreach (var n in overdue)
                    {
                        n.Status = "RESOLVED";
                        updated++;
                    }
                    modified = updated > 0;
                }

                if (modified)
                    StickyNotesEngine.SaveNotes(doc, notes);

                TaskDialog.Show("STING Sticky Notes",
                    updated > 0
                        ? $"Updated {updated} note(s)."
                        : "No notes matched the criteria.");

                StingLog.Info($"StickyNoteBulkUpdate: {updated} notes updated");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StickyNoteBulkUpdateCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
