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
using StingTools.Core.Storage;

namespace StingTools.ExLink
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (C1) — Fohlio commands.
    //
    // Fohlio_Export  emit the FF&E register in Fohlio's import shape (CSV).
    // Fohlio_Import  read a Fohlio export, preview the diff, then write back
    //                FOHLIO_REF_TXT + selected fields + an ES snapshot.
    // Fohlio_Audit   FF&E missing FOHLIO_REF_TXT + stale rows (model ≠ snapshot).
    // ─────────────────────────────────────────────────────────────────────────

    internal static class FohlioScope
    {
        public static List<Element> Collect(Document doc, FohlioMap map)
        {
            var cats = new HashSet<string>(map.Categories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && cats.Contains(ParameterHelpers.GetCategoryName(e)))
                .ToList();
        }

        public static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FohlioExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var map = FohlioMap.Load(doc);
            var scope = FohlioScope.Collect(doc, map);
            if (scope.Count == 0)
            {
                TaskDialog.Show("Fohlio Export", "No FF&E elements found in the mapped categories " +
                    $"({string.Join(", ", map.Categories)}).");
                return Result.Succeeded;
            }

            string path;
            try
            {
                var rows = new List<string> { string.Join(",", map.Columns.Select(c => FohlioScope.Csv(c.Header))) };
                foreach (var el in scope)
                    rows.Add(string.Join(",", map.Columns.Select(c => FohlioScope.Csv(FohlioMap.ResolveValue(doc, el, c.Param)))));
                path = OutputLocationHelper.GetOutputPath(doc, $"STING_Fohlio_Export_{DateTime.Now:yyyyMMdd}.csv");
                File.WriteAllLines(path, rows, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Fohlio Export", $"Export failed:\n{ex.Message}");
                return Result.Failed;
            }

            new TaskDialog("Fohlio Export")
            {
                MainInstruction = $"Exported {scope.Count} FF&E item(s)",
                MainContent = $"Columns: {string.Join(", ", map.Columns.Select(c => c.Header))}\n\n" +
                              $"CSV: {path}\n\nImport this into Fohlio, then run Fohlio Import on the Fohlio export " +
                              "to write FOHLIO_REF_TXT back (link, never duplicate)."
            }.Show();
            StingLog.Info($"Fohlio_Export: {scope.Count} items → {path}");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FohlioImportCommand : IExternalCommand
    {
        private class ProposedChange
        {
            public Element El; public string Param; public string Header; public string Old; public string New;
        }

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var map = FohlioMap.Load(doc);
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the Fohlio export (CSV or XLSX)",
                Filter = "Fohlio export (*.csv;*.xlsx)|*.csv;*.xlsx",
                InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            // Tag column drives matching back to the model.
            var tagCol = map.Columns.FirstOrDefault(c => string.Equals(c.Param, "ASS_TAG_1_TXT", StringComparison.OrdinalIgnoreCase));
            if (tagCol == null)
            {
                TaskDialog.Show("Fohlio Import", "The mapping has no Item Tag (ASS_TAG_1_TXT) column to match on.");
                return Result.Failed;
            }

            List<Dictionary<string, string>> rows;
            try { rows = ReadRows(dlg.FileName, map); }
            catch (Exception ex) { TaskDialog.Show("Fohlio Import", $"Read failed:\n{ex.Message}"); return Result.Failed; }

            // Model index by tag
            var scope = FohlioScope.Collect(doc, map);
            var byTag = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in scope)
            {
                string t = ParameterHelpers.GetString(el, "ASS_TAG_1_TXT");
                if (!string.IsNullOrEmpty(t) && !byTag.ContainsKey(t)) byTag[t] = el;
            }

            var writeCols = map.Columns.Where(c => c.WriteBack && !FohlioMap.IsPseudo(c.Param)).ToList();
            var changes = new List<ProposedChange>();
            var snapshots = new Dictionary<long, (string fref, Dictionary<string, string> snap)>();
            int matched = 0, unmatched = 0;

            foreach (var row in rows)
            {
                if (!row.TryGetValue(tagCol.Header, out string tagVal) || string.IsNullOrEmpty(tagVal)) { unmatched++; continue; }
                if (!byTag.TryGetValue(tagVal.Trim(), out var el)) { unmatched++; continue; }
                matched++;

                var snap = new Dictionary<string, string>();
                foreach (var c in writeCols)
                {
                    row.TryGetValue(c.Header, out string newVal);
                    newVal = (newVal ?? "").Trim();
                    snap[c.Param] = newVal;
                    string old = ParameterHelpers.GetString(el, c.Param);
                    if (!string.Equals(old, newVal, StringComparison.Ordinal) && newVal.Length > 0)
                        changes.Add(new ProposedChange { El = el, Param = c.Param, Header = c.Header, Old = old, New = newVal });
                }
                row.TryGetValue("Fohlio Ref", out string fref);
                snapshots[el.Id.Value] = (snap.TryGetValue(ParamRegistry.FOHLIO_REF, out var fr) ? fr : (fref ?? ""), snap);
            }

            if (changes.Count == 0)
            {
                TaskDialog.Show("Fohlio Import",
                    $"Matched {matched} row(s), {unmatched} unmatched. No field changes to write.");
                return Result.Succeeded;
            }

            // Preview / diff before any write.
            var preview = new StringBuilder();
            preview.AppendLine($"Matched {matched} row(s) by Item Tag — {unmatched} unmatched.");
            preview.AppendLine($"{changes.Count} field change(s) proposed across {changes.Select(c => c.El.Id.Value).Distinct().Count()} element(s).");
            preview.AppendLine();
            foreach (var c in changes.Take(15))
                preview.AppendLine($"  {c.El.Id} {c.Header}: '{c.Old}' → '{c.New}'");
            if (changes.Count > 15) preview.AppendLine($"  … +{changes.Count - 15} more");

            var confirm = new TaskDialog("Fohlio Import — preview")
            {
                MainInstruction = "Review before writing to the model",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Apply — fill empty only", "Write only where the model value is currently blank");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply — overwrite", "Overwrite existing model values with the Fohlio values");
            var choice = confirm.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;
            bool overwrite = choice == TaskDialogResult.CommandLink2;

            int written = 0;
            using (var t = new Transaction(doc, "STING Fohlio Import"))
            {
                t.Start();
                foreach (var c in changes)
                {
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, c.El)) continue;
                    bool ok = overwrite
                        ? ParameterHelpers.SetString(c.El, c.Param, c.New, overwrite: true)
                        : ParameterHelpers.SetIfEmpty(c.El, c.Param, c.New);
                    if (ok) written++;
                }
                // Snapshot every matched element (for the staleness audit), even those with no change.
                foreach (var kv in snapshots)
                {
                    var el = doc.GetElement(new ElementId(kv.Key));
                    if (el == null) continue;
                    StingFohlioSnapshotSchema.Write(el, kv.Value.fref,
                        JsonConvert.SerializeObject(kv.Value.snap), DateTime.UtcNow);
                }
                t.Commit();
            }

            new TaskDialog("Fohlio Import")
            {
                MainInstruction = $"Wrote {written} field value(s) ({(overwrite ? "overwrite" : "fill empty")})",
                MainContent = $"Matched: {matched}\nUnmatched: {unmatched}\nSnapshots stored: {snapshots.Count}\n\n" +
                              "FOHLIO_REF_TXT now links each item to Fohlio; the ES snapshot powers the currency audit."
            }.Show();
            StingLog.Info($"Fohlio_Import: matched={matched} wrote={written} snapshots={snapshots.Count}");
            return Result.Succeeded;
        }

        private static List<Dictionary<string, string>> ReadRows(string path, FohlioMap map)
        {
            var rows = new List<Dictionary<string, string>>();
            var wantHeaders = map.Columns.Select(c => c.Header).ToList();

            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheets.First();
                var used = ws.RangeUsed();
                if (used == null) return rows;
                int fr = used.FirstRow().RowNumber(), lr = used.LastRow().RowNumber();
                int fc = used.FirstColumn().ColumnNumber(), lc = used.LastColumn().ColumnNumber();
                var hdr = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int c = fc; c <= lc; c++) hdr[ws.Cell(fr, c).GetString().Trim()] = c;
                for (int r = fr + 1; r <= lr; r++)
                {
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var h in wantHeaders)
                        if (hdr.TryGetValue(h, out int c)) row[h] = ws.Cell(r, c).GetString();
                    rows.Add(row);
                }
            }
            else
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return rows;
                var hdrFields = StingToolsApp.ParseCsvLine(lines[0]);
                var hdr = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < hdrFields.Length; i++) hdr[hdrFields[i].Trim()] = i;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = StingToolsApp.ParseCsvLine(lines[i]);
                    var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var h in wantHeaders)
                        if (hdr.TryGetValue(h, out int c) && c < f.Length) row[h] = f[c];
                    rows.Add(row);
                }
            }
            return rows;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FohlioAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var map = FohlioMap.Load(doc);
            var scope = FohlioScope.Collect(doc, map);
            var writeParams = map.Columns.Where(c => c.WriteBack && !FohlioMap.IsPseudo(c.Param)).Select(c => c.Param).ToList();

            int total = scope.Count, missingRef = 0, stale = 0, current = 0, neverImported = 0;
            var byCat = new Dictionary<string, (int total, int missing, int stale)>(StringComparer.OrdinalIgnoreCase);
            var staleSamples = new List<string>();

            foreach (var el in scope)
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                byCat.TryGetValue(cat, out var cv);

                string fref = ParameterHelpers.GetString(el, ParamRegistry.FOHLIO_REF);
                bool isMissing = string.IsNullOrEmpty(fref);
                if (isMissing) missingRef++;

                var snap = StingFohlioSnapshotSchema.Read(el);
                bool isStale = false;
                if (snap == null || snap.CapturedUtcTicks == 0)
                {
                    neverImported++;
                }
                else
                {
                    Dictionary<string, string> snapVals = null;
                    try { snapVals = JsonConvert.DeserializeObject<Dictionary<string, string>>(snap.SnapshotJson); }
                    catch { }
                    snapVals = snapVals ?? new Dictionary<string, string>();
                    foreach (var p in writeParams)
                    {
                        snapVals.TryGetValue(p, out string snapV);
                        string cur = ParameterHelpers.GetString(el, p);
                        if (!string.Equals(cur, snapV ?? "", StringComparison.Ordinal)) { isStale = true; break; }
                    }
                    if (isStale) { stale++; if (staleSamples.Count < 10) staleSamples.Add($"{el.Id} [{cat}]"); }
                    else current++;
                }

                byCat[cat] = (cv.total + 1, cv.missing + (isMissing ? 1 : 0), cv.stale + (isStale ? 1 : 0));
            }

            double linked = total > 0 ? 100.0 * (total - missingRef) / total : 100.0;
            var sb = new StringBuilder();
            sb.AppendLine($"FF&E elements: {total}   ('Fohlio kept current' KPI)");
            sb.AppendLine($"Linked (FOHLIO_REF set): {total - missingRef}/{total} ({linked:F1}%)");
            sb.AppendLine($"Missing FOHLIO_REF:      {missingRef}");
            sb.AppendLine($"Never imported:          {neverImported}");
            sb.AppendLine($"Stale (model ≠ Fohlio):  {stale}");
            sb.AppendLine($"Current:                 {current}");
            sb.AppendLine();
            sb.AppendLine("By category (total / missing-ref / stale):");
            foreach (var kv in byCat.OrderByDescending(k => k.Value.total))
                sb.AppendLine($"   {kv.Key,-22} {kv.Value.total} / {kv.Value.missing} / {kv.Value.stale}");
            if (staleSamples.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Stale samples: " + string.Join(", ", staleSamples));
            }

            new TaskDialog("Fohlio Audit")
            {
                MainInstruction = $"{linked:F0}% linked — {missingRef} missing ref, {stale} stale",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"Fohlio_Audit: total={total} missingRef={missingRef} stale={stale} current={current}");
            return Result.Succeeded;
        }
    }
}
