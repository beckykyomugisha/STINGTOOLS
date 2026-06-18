using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Docs
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (C3) — Bluebeam Studio comment close-out tracker commands.
    //
    // ReviewComments_Import     parse a Bluebeam comment-summary CSV/XLSX, pick a
    //                           gate, upsert into _BIM_COORD/review_comments.json.
    // ReviewComments_Dashboard  grid of tracked comments + close-out rate.
    // ReviewComments_Export     monthly KPI CSV (gate, total, closed, %, mean age,
    //                           overdue).
    // ─────────────────────────────────────────────────────────────────────────

    public class ReviewCommentMap
    {
        public List<string> CommentIdHeaders { get; set; } = new List<string> { "index", "id", "comment id", "#", "number" };
        public List<string> SubjectHeaders { get; set; } = new List<string> { "subject", "comment", "text", "contents" };
        public List<string> PageHeaders { get; set; } = new List<string> { "page label", "page", "sheet" };
        public List<string> AuthorHeaders { get; set; } = new List<string> { "author", "by", "created by" };
        public List<string> DateHeaders { get; set; } = new List<string> { "date", "created", "modified" };
        public List<string> StatusHeaders { get; set; } = new List<string> { "status", "state" };
        public List<string> ReplyHeaders { get; set; } = new List<string> { "reply count", "replies", "comment count" };
        public int OverdueSlaDays { get; set; } = 14;

        public static ReviewCommentMap Load(Document doc)
        {
            var map = new ReviewCommentMap();
            try
            {
                string p = ProjectFile(doc, "review_comment_map.json");
                if (p != null && File.Exists(p))
                {
                    var o = JsonConvert.DeserializeObject<ReviewCommentMap>(File.ReadAllText(p));
                    if (o != null) map = o;
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReviewCommentMap load: {ex.Message}"); }
            return map;
        }

        internal static string ProjectFile(Document doc, string name)
        {
            string dir = Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "_BIM_COORD", name);
        }
    }

    internal static class ReviewCommentStoreIO
    {
        public static ReviewCommentStore Load(Document doc)
        {
            try
            {
                string p = ReviewCommentMap.ProjectFile(doc, "review_comments.json");
                if (p != null && File.Exists(p))
                    return JsonConvert.DeserializeObject<ReviewCommentStore>(File.ReadAllText(p)) ?? new ReviewCommentStore();
            }
            catch (Exception ex) { StingLog.Warn($"ReviewCommentStore load: {ex.Message}"); }
            return new ReviewCommentStore();
        }

        public static string Save(Document doc, ReviewCommentStore store)
        {
            string p = ReviewCommentMap.ProjectFile(doc, "review_comments.json");
            if (p == null) throw new InvalidOperationException("Save the project first (the store lives in _BIM_COORD).");
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, JsonConvert.SerializeObject(store, Formatting.Indented), Encoding.UTF8);
            return p;
        }
    }

    /// <summary>Display row for the dashboard grid.</summary>
    public class ReviewCommentDisplay
    {
        public string Gate { get; set; }
        public string Status { get; set; }
        public string Owner { get; set; }
        public string Subject { get; set; }
        public string Page { get; set; }
        public string Author { get; set; }
        public double AgeDays { get; set; }
        public string Session { get; set; }
        public string CommentId { get; set; }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReviewCommentsImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the Bluebeam comment summary (CSV or XLSX)",
                Filter = "Comment summary (*.csv;*.xlsx)|*.csv;*.xlsx",
                InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            string gate = StingListPicker.Show("Review gate",
                "Which review gate does this comment summary belong to?",
                new List<string> { "Deliverable A", "Deliverable B (50%)", "Deliverable C (100%)", "Conformed set", "(unassigned)" });
            if (string.IsNullOrEmpty(gate)) return Result.Cancelled;
            if (gate == "(unassigned)") gate = "";

            var map = ReviewCommentMap.Load(doc);
            string sessionId = Path.GetFileNameWithoutExtension(dlg.FileName);
            List<ReviewComment> incoming;
            try
            {
                incoming = dlg.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? ParseXlsx(dlg.FileName, map, sessionId, gate)
                    : ParseCsv(dlg.FileName, map, sessionId, gate);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Review Comments", $"Could not read the file:\n{ex.Message}");
                return Result.Failed;
            }
            if (incoming.Count == 0)
            {
                TaskDialog.Show("Review Comments", "No comment rows parsed — check the header row / column map.");
                return Result.Succeeded;
            }

            var store = ReviewCommentStoreIO.Load(doc);
            var (added, updated) = ReviewCommentTracker.Upsert(store.Comments, incoming, DateTime.UtcNow);
            string path;
            try { path = ReviewCommentStoreIO.Save(doc, store); }
            catch (Exception ex)
            {
                TaskDialog.Show("Review Comments", ex.Message);
                return Result.Failed;
            }

            double rate = ReviewCommentTracker.CloseOutRate(store.Comments);
            new TaskDialog("Review Comments — Import")
            {
                MainInstruction = $"{added} added, {updated} updated — {store.Comments.Count} tracked",
                MainContent = $"Session: {sessionId}\nGate: {(string.IsNullOrEmpty(gate) ? "(unassigned)" : gate)}\n" +
                              $"Close-out rate (all): {rate:F1}%\n\nStore: {path}"
            }.Show();
            StingLog.Info($"ReviewComments_Import: +{added}/~{updated} from {sessionId} ({gate})");
            return Result.Succeeded;
        }

        private static List<ReviewComment> ParseCsv(string path, ReviewCommentMap map, string session, string gate)
        {
            var lines = File.ReadAllLines(path);
            var result = new List<ReviewComment>();
            if (lines.Length < 2) return result;
            var header = StingToolsApp.ParseCsvLine(lines[0]).Select(NormHeader).ToList();
            int Find(List<string> cands) => FindCol(header, cands);
            int cId = Find(map.CommentIdHeaders), cSub = Find(map.SubjectHeaders), cPage = Find(map.PageHeaders),
                cAuth = Find(map.AuthorHeaders), cDate = Find(map.DateHeaders), cStat = Find(map.StatusHeaders),
                cRep = Find(map.ReplyHeaders);

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var f = StingToolsApp.ParseCsvLine(lines[i]);
                string Get(int c) => (c >= 0 && c < f.Length) ? f[c]?.Trim() ?? "" : "";
                result.Add(BuildComment(session, gate, i,
                    Get(cId), Get(cSub), Get(cPage), Get(cAuth), Get(cDate), Get(cStat), Get(cRep)));
            }
            return result;
        }

        private static List<ReviewComment> ParseXlsx(string path, ReviewCommentMap map, string session, string gate)
        {
            var result = new List<ReviewComment>();
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.First();
            var used = ws.RangeUsed();
            if (used == null) return result;
            int firstRow = used.FirstRow().RowNumber(), lastRow = used.LastRow().RowNumber();
            int firstCol = used.FirstColumn().ColumnNumber(), lastCol = used.LastColumn().ColumnNumber();
            var header = new List<string>();
            for (int c = firstCol; c <= lastCol; c++) header.Add(NormHeader(ws.Cell(firstRow, c).GetString()));
            int Find(List<string> cands) => FindCol(header, cands);
            int cId = Find(map.CommentIdHeaders), cSub = Find(map.SubjectHeaders), cPage = Find(map.PageHeaders),
                cAuth = Find(map.AuthorHeaders), cDate = Find(map.DateHeaders), cStat = Find(map.StatusHeaders),
                cRep = Find(map.ReplyHeaders);
            for (int r = firstRow + 1; r <= lastRow; r++)
            {
                string Get(int c) => c >= 0 ? ws.Cell(r, firstCol + c).GetString().Trim() : "";
                result.Add(BuildComment(session, gate, r,
                    Get(cId), Get(cSub), Get(cPage), Get(cAuth), Get(cDate), Get(cStat), Get(cRep)));
            }
            return result;
        }

        private static ReviewComment BuildComment(string session, string gate, int rowIdx,
            string id, string subject, string page, string author, string date, string status, string reply)
        {
            if (string.IsNullOrEmpty(id)) id = $"row{rowIdx}";
            int.TryParse(reply, out int replyCount);
            return new ReviewComment
            {
                SessionId = session,
                CommentId = id,
                Subject = subject,
                PageLabel = page,
                Author = author,
                Date = date,
                RawStatus = status,
                Status = ReviewCommentTracker.NormalizeStatus(status),
                ReplyCount = replyCount,
                Gate = gate,
            };
        }

        private static string NormHeader(string s) => (s ?? "").Trim().ToLowerInvariant();

        private static int FindCol(List<string> header, List<string> cands)
        {
            foreach (var cand in cands.OrderByDescending(c => c.Length))
            {
                string nc = cand.Trim().ToLowerInvariant();
                for (int i = 0; i < header.Count; i++)
                    if (!string.IsNullOrEmpty(header[i]) && header[i].Contains(nc)) return i;
            }
            return -1;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReviewCommentsDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var store = ReviewCommentStoreIO.Load(doc);
            if (store.Comments.Count == 0)
            {
                TaskDialog.Show("Review Comments", "No tracked comments. Run Review Comments → Import first.");
                return Result.Succeeded;
            }

            var now = DateTime.UtcNow;
            var rows = store.Comments
                .OrderBy(c => ReviewCommentTracker.IsClosed(c.Status))
                .ThenByDescending(c => ReviewCommentTracker.AgeDays(c, now))
                .Select(c => new ReviewCommentDisplay
                {
                    Gate = string.IsNullOrEmpty(c.Gate) ? "(unassigned)" : c.Gate,
                    Status = c.Status.ToString(),
                    Owner = c.Owner,
                    Subject = c.Subject,
                    Page = c.PageLabel,
                    Author = c.Author,
                    AgeDays = ReviewCommentTracker.AgeDays(c, now),
                    Session = c.SessionId,
                    CommentId = c.CommentId,
                }).ToList();

            double rate = ReviewCommentTracker.CloseOutRate(store.Comments);
            int open = store.Comments.Count(c => !ReviewCommentTracker.IsClosed(c.Status));
            var dlg = new StingDataGridDialog("Bluebeam Review Comments",
                $"{store.Comments.Count} tracked — close-out {rate:F1}% — {open} open");
            dlg.AddTextColumn("Gate", "Gate", 110);
            dlg.AddTextColumn("Status", "Status", 90);
            dlg.AddTextColumn("Owner", "Owner", 90);
            dlg.AddTextColumn("Page", "Page", 70);
            dlg.AddTextColumn("Subject", "Subject");
            dlg.AddTextColumn("Author", "Author", 100);
            dlg.AddTextColumn("Age (d)", "AgeDays", 60);
            dlg.SetItems(rows);
            dlg.AddActionButton("Close", "Cancel");
            StingTools.UI.StingWindowHelper.ApplyOwner(dlg);
            dlg.ShowDialog();
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReviewCommentsExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var store = ReviewCommentStoreIO.Load(doc);
            if (store.Comments.Count == 0)
            {
                TaskDialog.Show("Review Comments", "No tracked comments to export.");
                return Result.Succeeded;
            }

            var map = ReviewCommentMap.Load(doc);
            var kpi = ReviewCommentTracker.BuildKpi(store.Comments, DateTime.UtcNow, map.OverdueSlaDays);

            string path = null;
            try
            {
                var rows = new List<string> { "Gate,Total,Closed,CloseOutPct,MeanAgeDays,Overdue" };
                foreach (var k in kpi)
                    rows.Add($"\"{k.Gate}\",{k.Total},{k.Closed},{k.CloseOutPct},{k.MeanAgeDays},{k.Overdue}");
                path = OutputLocationHelper.GetOutputPath(doc, $"STING_ReviewComments_KPI_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
            }
            catch (Exception ex) { StingLog.Warn($"ReviewComments KPI export: {ex.Message}"); }

            var sb = new StringBuilder();
            sb.AppendLine($"Overdue SLA: {map.OverdueSlaDays} days");
            sb.AppendLine();
            foreach (var k in kpi)
                sb.AppendLine($"{k.Gate,-22} {k.Closed}/{k.Total} closed ({k.CloseOutPct:F0}%)  mean {k.MeanAgeDays:F0}d  overdue {k.Overdue}");
            if (path != null) { sb.AppendLine(); sb.AppendLine($"CSV: {path}"); }

            new TaskDialog("Review Comments — KPI")
            {
                MainInstruction = "Monthly close-out KPI",
                MainContent = sb.ToString()
            }.Show();
            return Result.Succeeded;
        }
    }
}
