// StingTools v4 MVP — Fabrication undo + incremental state.
//
// Two concerns in one file because they both need a durable record of
// the last fabrication run:
//
//   FabricationUndoManager — cheap single-step undo for the last
//   Generate Package call. Records the AssemblyInstance ids + sheet
//   ids that were created so "Undo last run" can delete them in a
//   single transaction. Revit's own undo stack can handle this
//   *inside the same session* but users almost always want to undo
//   after reloading, which blows the undo buffer; this file-backed
//   record fixes that.
//
//   FabricationIncrementalTracker — records a content hash (element
//   id + Modified timestamp) per assembly group so the next Generate
//   Package call can skip groups whose members haven't changed. Saves
//   minutes on 1000-pipe models.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Commands.Fabrication
{
    // ── Undo manager ──────────────────────────────────────────

    public class FabricationUndoRecord
    {
        public DateTime RanAtUtc  { get; set; } = DateTime.UtcNow;
        public List<long> AssemblyIds { get; set; } = new List<long>();
        public List<long> SheetIds    { get; set; } = new List<long>();
        public List<long> ViewIds     { get; set; } = new List<long>();
        public string Summary { get; set; } = "";
    }

    public static class FabricationUndoManager
    {
        private const string FileName = "fab_last_run.json";

        private static string ResolvePath(Document doc)
        {
            try
            {
                string projectDir = Path.GetDirectoryName(doc?.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projectDir))
                    projectDir = OutputLocationHelper.GetOutputDirectory(doc);
                string dir = Path.Combine(projectDir, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, FileName);
            }
            catch (Exception ex) { StingLog.Warn($"FabricationUndoManager.ResolvePath: {ex.Message}"); return ""; }
        }

        public static void Record(Document doc, FabricationResult res)
        {
            if (res == null) return;
            var path = ResolvePath(doc);
            if (string.IsNullOrEmpty(path)) return;
            var rec = new FabricationUndoRecord
            {
                AssemblyIds = res.AssemblyIds?.Select(i => i.Value).ToList() ?? new List<long>(),
                SheetIds    = res.SheetIds?.Select(i => i.Value).ToList()    ?? new List<long>(),
                Summary     = res.FormatSummary(),
            };
            try { File.WriteAllText(path, JsonConvert.SerializeObject(rec, Formatting.Indented)); }
            catch (Exception ex) { StingLog.Warn($"FabricationUndoManager.Record: {ex.Message}"); }
        }

        public static FabricationUndoRecord Peek(Document doc)
        {
            var path = ResolvePath(doc);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<FabricationUndoRecord>(File.ReadAllText(path)); }
            catch (Exception ex) { StingLog.Warn($"FabricationUndoManager.Peek: {ex.Message}"); return null; }
        }

        public static int UndoLast(UIDocument uidoc)
        {
            var doc = uidoc?.Document;
            var rec = Peek(doc);
            if (rec == null) return 0;

            int removed = 0;
            using (var tg = new TransactionGroup(doc, "STING v4 — Undo Fabrication"))
            {
                tg.Start();
                try
                {
                    // Delete sheets first so viewports release their views
                    using (var t = new Transaction(doc, "Undo fab sheets"))
                    {
                        t.Start();
                        foreach (var id in rec.SheetIds)
                        {
                            try
                            {
                                var el = doc.GetElement(new ElementId(id));
                                if (el != null) { doc.Delete(el.Id); removed++; }
                            }
                            catch (Exception ex) { StingLog.Warn($"UndoLast sheet {id}: {ex.Message}"); }
                        }
                        t.Commit();
                    }
                    using (var t = new Transaction(doc, "Undo fab assemblies"))
                    {
                        t.Start();
                        foreach (var id in rec.AssemblyIds)
                        {
                            try
                            {
                                var el = doc.GetElement(new ElementId(id));
                                if (el != null) { doc.Delete(el.Id); removed++; }
                            }
                            catch (Exception ex) { StingLog.Warn($"UndoLast assembly {id}: {ex.Message}"); }
                        }
                        t.Commit();
                    }
                    tg.Assimilate();
                }
                catch (Exception ex)
                {
                    StingLog.Error("FabricationUndoManager.UndoLast", ex);
                    if (tg.HasStarted()) tg.RollBack();
                    return 0;
                }
            }

            // Clear record so Undo can't be pressed twice by mistake.
            try { File.Delete(ResolvePath(doc)); } catch { }
            return removed;
        }
    }

    // ── Incremental change tracker ────────────────────────────

    public class FabricationIncrementalState
    {
        // Map from assembly group-key → content hash (element ids + Modified ticks)
        public Dictionary<string, string> GroupHashes { get; set; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public static class FabricationIncrementalTracker
    {
        private const string FileName = "fab_incremental_state.json";

        private static string ResolvePath(Document doc)
        {
            try
            {
                string projectDir = Path.GetDirectoryName(doc?.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(projectDir))
                    projectDir = OutputLocationHelper.GetOutputDirectory(doc);
                string dir = Path.Combine(projectDir, "_BIM_COORD");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, FileName);
            }
            catch (Exception ex) { StingLog.Warn($"FabricationIncrementalTracker.ResolvePath: {ex.Message}"); return ""; }
        }

        public static FabricationIncrementalState Load(Document doc)
        {
            var path = ResolvePath(doc);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new FabricationIncrementalState();
            try { return JsonConvert.DeserializeObject<FabricationIncrementalState>(File.ReadAllText(path)) ?? new FabricationIncrementalState(); }
            catch (Exception ex) { StingLog.Warn($"FabricationIncrementalTracker.Load: {ex.Message}"); return new FabricationIncrementalState(); }
        }

        public static void Save(Document doc, FabricationIncrementalState state)
        {
            var path = ResolvePath(doc);
            if (string.IsNullOrEmpty(path)) return;
            try { File.WriteAllText(path, JsonConvert.SerializeObject(state ?? new FabricationIncrementalState(), Formatting.Indented)); }
            catch (Exception ex) { StingLog.Warn($"FabricationIncrementalTracker.Save: {ex.Message}"); }
        }

        /// <summary>
        /// Compute a content hash for a group of elements. Uses element
        /// id + the string rep of the Modified-related parameter so
        /// hashing does not require Revit's internal version counters.
        /// </summary>
        public static string HashGroup(Document doc, IEnumerable<ElementId> ids)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var id in ids.OrderBy(i => i.Value))
            {
                sb.Append(id.Value);
                sb.Append(':');
                var el = doc?.GetElement(id);
                try
                {
                    sb.Append(el?.get_Parameter(BuiltInParameter.EDITED_BY)?.AsString() ?? "");
                    sb.Append(';');
                    var p = el?.LookupParameter("ASS_TAG_MODIFIED_DT");
                    sb.Append(p?.AsString() ?? "");
                }
                catch { }
                sb.Append('|');
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] buf = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                return BitConverter.ToString(sha.ComputeHash(buf)).Replace("-", "").Substring(0, 16);
            }
        }

        /// <summary>
        /// Filter a list of (groupKey, elementIds) tuples down to only
        /// the groups whose content has changed since the last run.
        /// </summary>
        public static List<(string Key, List<ElementId> Ids)> FilterChanged(
            Document doc, IEnumerable<(string Key, List<ElementId> Ids)> groups, FabricationIncrementalState state)
        {
            var result = new List<(string, List<ElementId>)>();
            foreach (var g in groups)
            {
                string h = HashGroup(doc, g.Ids);
                if (!state.GroupHashes.TryGetValue(g.Key, out var prev) || prev != h)
                    result.Add(g);
            }
            return result;
        }

        public static void RecordHashes(Document doc, IEnumerable<(string Key, List<ElementId> Ids)> groups)
        {
            var state = Load(doc);
            foreach (var g in groups)
                state.GroupHashes[g.Key] = HashGroup(doc, g.Ids);
            state.UpdatedAtUtc = DateTime.UtcNow;
            Save(doc, state);
        }
    }
}
