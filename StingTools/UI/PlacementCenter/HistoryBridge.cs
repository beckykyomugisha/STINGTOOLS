// Phase 127-D — provenance / history bridge.
//
// Reads StingProvenanceSchema (Pack 123/E) across the project, groups
// by engine + creation hour, and surfaces a flat history list the
// centre's right-hand panel renders. "Show on screen" + "Undo last"
// operations also live here.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Storage;

namespace StingTools.UI.PlacementCenter
{
    public static class HistoryBridge
    {
        public class HistoryRow
        {
            public string Engine { get; set; } = "";
            public string RuleId { get; set; } = "";
            public string Operator { get; set; } = "";
            public string CreatedUtc { get; set; } = "";
            public int Count { get; set; }
            public List<ElementId> Ids { get; set; } = new List<ElementId>();

            // Phase 139 I4 — extra columns for the history panel.
            public double CoveragePercent { get; set; } = -1;   // -1 = unknown
            public int    SkippedCount    { get; set; } = 0;
            public int    WarningCount    { get; set; } = 0;
            public string Detail          { get; set; } = "";   // free-text appended by run results

            public string CoverageDisplay  => CoveragePercent < 0 ? "—" : $"{CoveragePercent:F0}%";
            public string SkippedDisplay   => SkippedCount == 0 ? "—" : SkippedCount.ToString();
            public string WarningDisplay   => WarningCount == 0 ? "—" : WarningCount.ToString();
        }

        /// <summary>
        /// Walk every element with a Provenance entity, group into hourly
        /// buckets per (engine, ruleId), return newest first. Caps at the
        /// most recent 30 rows so the panel stays responsive.
        /// </summary>
        public static List<HistoryRow> ReadHistory(Document doc, int maxRows = 30)
        {
            var rows = new List<HistoryRow>();
            if (doc == null) return rows;

            var bucket = new Dictionary<string, HistoryRow>();
            try
            {
                var col = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    StingProvenanceSchema.Provenance p = null;
                    try { p = StingProvenanceSchema.Read(el); } catch { }
                    if (p == null) continue;

                    DateTime utc = p.CreatedUtcTicks > 0
                        ? new DateTime(p.CreatedUtcTicks, DateTimeKind.Utc)
                        : DateTime.MinValue;
                    string hourKey = utc == DateTime.MinValue ? "(unknown)"
                        : utc.ToString("yyyy-MM-dd HH:00 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
                    string key = $"{p.Engine}|{p.RuleId}|{hourKey}";

                    if (!bucket.TryGetValue(key, out var row))
                    {
                        row = new HistoryRow
                        {
                            Engine     = p.Engine,
                            RuleId     = p.RuleId,
                            Operator   = p.Operator,
                            CreatedUtc = hourKey,
                        };
                        bucket[key] = row;
                    }
                    row.Count++;
                    if (row.Ids.Count < 5000) row.Ids.Add(el.Id); // cap per row
                }
            }
            catch (Exception ex) { StingLog.Warn($"HistoryBridge.ReadHistory: {ex.Message}"); }

            return bucket.Values
                .OrderByDescending(r => r.CreatedUtc)
                .Take(maxRows)
                .ToList();
        }

        /// <summary>
        /// Find the most-recent run row (highest CreatedUtc) and return
        /// its element-id list. The centre's "Undo last" button deletes
        /// these inside a TransactionGroup.
        /// </summary>
        public static HistoryRow MostRecent(Document doc)
        {
            var rows = ReadHistory(doc, maxRows: 1);
            return rows.Count > 0 ? rows[0] : null;
        }

        /// <summary>
        /// Delete every element in the supplied list inside a single
        /// transaction. Returns (deleted, skipped). Skipped covers
        /// elements that have already been removed or that Revit refuses
        /// to delete (pinned, hosted by something else, etc.).
        /// </summary>
        public static (int deleted, int skipped) DeleteIds(Document doc, IList<ElementId> ids)
        {
            int deleted = 0, skipped = 0;
            if (doc == null || ids == null || ids.Count == 0) return (0, 0);
            try
            {
                using (var t = new Transaction(doc, "STING — Undo last placement run"))
                {
                    t.Start();
                    foreach (var id in ids)
                    {
                        try
                        {
                            if (id == null || id == ElementId.InvalidElementId) { skipped++; continue; }
                            var el = doc.GetElement(id);
                            if (el == null) { skipped++; continue; }
                            doc.Delete(id);
                            deleted++;
                        }
                        catch { skipped++; }
                    }
                    t.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("HistoryBridge.DeleteIds", ex);
            }
            return (deleted, skipped);
        }
    }
}
