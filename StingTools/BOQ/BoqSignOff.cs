// ══════════════════════════════════════════════════════════════════════════
//  BoqSignOff.cs (QS gap G9) — QS sign-off record + "uncertified" draft stamp.
//
//  Until a QS records a sign-off against the current snapshot, every exported
//  BOQ / cost-plan / tender carries a clear DRAFT mark. A recorded sign-off
//  (tied to a snapshot) clears the mark for exports of that signed snapshot.
//  Persisted per project at <project>/_BIM_COORD/boq_signoff.json.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.IO;
using Autodesk.Revit.DB;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class BoqSignOff
    {
        public string SignedBy { get; set; } = "";
        public string Role { get; set; } = "";
        public string Date { get; set; } = "";        // yyyy-MM-dd
        public string Scope { get; set; } = "";        // free text (e.g. "Tender BOQ Rev A")
        public string SnapshotRef { get; set; } = "";  // BOQDocument.SnapshotLabel this sign-off covers
    }

    internal static class BoqSignOffStore
    {
        public const string DraftMark =
            "DRAFT — not a certified bill of quantities; subject to QS verification.";

        private static string PathFor(Document doc)
        {
            try
            {
                string parent = System.IO.Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(parent)) return null;
                return System.IO.Path.Combine(parent, "_BIM_COORD", "boq_signoff.json");
            }
            catch { return null; }
        }

        public static BoqSignOff Load(Document doc)
        {
            try
            {
                string path = PathFor(doc);
                if (path == null || !File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<BoqSignOff>(File.ReadAllText(path));
            }
            catch (Exception ex) { StingLog.Warn($"BoqSignOffStore.Load: {ex.Message}"); return null; }
        }

        public static void Save(Document doc, BoqSignOff rec)
        {
            try
            {
                string path = PathFor(doc);
                if (path == null) return;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(rec, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"BoqSignOffStore.Save: {ex.Message}"); }
        }

        /// <summary>True when a sign-off exists AND covers the snapshot being exported.</summary>
        public static bool IsSignedFor(Document doc, BOQDocument boq)
        {
            var rec = Load(doc);
            if (rec == null || string.IsNullOrWhiteSpace(rec.SignedBy)) return false;
            string snap = boq?.SnapshotLabel ?? "Live";
            return string.Equals(rec.SnapshotRef ?? "", snap, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>One-line status for result panels / metrics.</summary>
        public static string StatusLine(Document doc, BOQDocument boq)
        {
            var rec = Load(doc);
            if (rec != null && IsSignedFor(doc, boq))
                return $"Certified by {rec.SignedBy} ({rec.Role}) {rec.Date}";
            if (rec != null && !string.IsNullOrWhiteSpace(rec.SignedBy))
                return $"DRAFT — last sign-off ({rec.SignedBy}, {rec.Date}) covers snapshot '{rec.SnapshotRef}', not '{boq?.SnapshotLabel ?? "Live"}'";
            return "DRAFT — not signed off";
        }

        /// <summary>
        /// Add a prominent first "Status" sheet to an export workbook: a DRAFT
        /// banner when unsigned, or a certification line when the current
        /// snapshot is signed. Called by every BOQ / tender / cost-plan export.
        /// </summary>
        public static void StampWorkbook(IXLWorkbook wb, Document doc, BOQDocument boq)
        {
            if (wb == null) return;
            try
            {
                bool signed = IsSignedFor(doc, boq);
                var rec = Load(doc);
                var ws = wb.Worksheets.Add("Status", 1);
                ws.Column(1).Width = 110;
                var title = ws.Cell(2, 1);
                title.Value = signed ? "CERTIFIED" : "DRAFT — UNCERTIFIED";
                title.Style.Font.Bold = true;
                title.Style.Font.FontSize = 20;
                title.Style.Font.FontColor = signed ? XLColor.FromArgb(46, 125, 50) : XLColor.FromArgb(198, 40, 40);

                var line = ws.Cell(4, 1);
                line.Value = signed
                    ? $"Certified by {rec.SignedBy} ({rec.Role}) on {rec.Date}.  Scope: {rec.Scope}.  Snapshot: {rec.SnapshotRef}."
                    : DraftMark;
                line.Style.Font.FontSize = 12;
                line.Style.Font.FontColor = signed ? XLColor.FromArgb(46, 125, 50) : XLColor.FromArgb(198, 40, 40);
                line.Style.Alignment.WrapText = true;

                ws.Cell(6, 1).Value = signed
                    ? "This export reflects a recorded QS sign-off for the snapshot named above. Re-verify if the model has changed since."
                    : "Record a QS sign-off (Actions → Record QS Sign-off) once a Quantity Surveyor has verified this bill. Until then, treat every figure as a STING-generated draft.";
                ws.Cell(6, 1).Style.Font.FontSize = 10;
                ws.Cell(6, 1).Style.Font.FontColor = XLColor.FromArgb(90, 90, 90);
                ws.Cell(6, 1).Style.Alignment.WrapText = true;
            }
            catch (Exception ex) { StingLog.Warn($"BoqSignOffStore.StampWorkbook: {ex.Message}"); }
        }
    }
}
