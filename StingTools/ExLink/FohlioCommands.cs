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

        /// <summary>A project fohlio_map.json that exists but cannot be read must stop the
        /// command. Falling back to the built-in defaults silently drops the cost and currency
        /// columns, so an import would quietly stop writing prices.</summary>
        public static bool RefuseBrokenMap(FohlioMap map, string title)
        {
            if (string.IsNullOrEmpty(map?.LoadError)) return false;
            TaskDialog.Show(title, "The project's fohlio_map.json could not be read, so nothing was done.\n\n" +
                map.LoadError + "\n\nFix the file (or remove it to use the built-in mapping) and run again.");
            return true;
        }
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
            if (FohlioScope.RefuseBrokenMap(map, "Fohlio Export")) return Result.Failed;
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
                path = OutputLocationHelper.GetRoutedPath(doc, "Schedule", $"STING_Fohlio_Export_{DateTime.Now:yyyyMMdd}.csv");
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
            if (FohlioScope.RefuseBrokenMap(map, "Fohlio Import")) return Result.Failed;
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the Fohlio export (CSV or XLSX)",
                Filter = "Fohlio export (*.csv;*.xlsx)|*.csv;*.xlsx",
                InitialDirectory = OutputLocationHelper.GetRoutedDirectory(doc, "Schedule")
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

            // Match through the planner: the Fohlio ref (the link key) first, then a tag that is
            // unique in the model and in the file. The old index was "first element with this
            // tag wins", so a re-tag sent a row to whichever element now carried the tag, and a
            // duplicate tag wrote to one element and reported both rows matched.
            var scope = FohlioScope.Collect(doc, map);
            var scopeById = scope.ToDictionary(e => e.Id.Value);
            string refHeader = map.Columns.FirstOrDefault(c => string.Equals(c.Param, ParamRegistry.FOHLIO_REF, StringComparison.OrdinalIgnoreCase))?.Header
                               ?? "Fohlio Ref";
            var plan = FohlioImportPlanner.Match(
                rows.Select((row, i) => new FohlioRowIdentity
                {
                    RowIndex = i,
                    Key = row.TryGetValue(tagCol.Header, out var tv) ? tv : "",
                    FohlioRef = row.TryGetValue(refHeader, out var rv) ? rv : "",
                }),
                scope.Select(e => new FohlioCandidate
                {
                    Id = e.Id.Value,
                    Key = ParameterHelpers.GetString(e, "ASS_TAG_1_TXT"),
                    FohlioRef = ParameterHelpers.GetString(e, ParamRegistry.FOHLIO_REF),
                }),
                useFohlioRef: true);

            var writeCols = map.Columns.Where(c => c.WriteBack && !FohlioMap.IsPseudo(c.Param)).ToList();

            // Phase C (KUT lifecycle) — pull the procurement cost columns. The numeric
            // unit cost is written via SetDouble (not the text-diff path), so exclude it
            // from writeCols. Headers resolved by Param so the map controls the names.
            string costHeader = map.Columns.FirstOrDefault(c => string.Equals(c.Param, ParamRegistry.FOHLIO_UNIT_COST, StringComparison.OrdinalIgnoreCase))?.Header;
            string curHeader  = map.Columns.FirstOrDefault(c => string.Equals(c.Param, ParamRegistry.FOHLIO_CURRENCY, StringComparison.OrdinalIgnoreCase))?.Header;
            string qtyHeader  = map.Columns.FirstOrDefault(c => string.Equals(c.Param, "$FohlioQty", StringComparison.OrdinalIgnoreCase))?.Header;
            string leadHeader = map.Columns.FirstOrDefault(c => string.Equals(c.Param, "$FohlioLeadDays", StringComparison.OrdinalIgnoreCase))?.Header;
            // The currency is written only WITH a price that parsed (L2-FOH-1): on the text path a
            // "UGX" row beside a "TBC" price relabelled a USD 1,250 item as UGX 1,250.
            writeCols = writeCols.Where(c => !string.Equals(c.Param, ParamRegistry.FOHLIO_UNIT_COST, StringComparison.OrdinalIgnoreCase)
                                          && !string.Equals(c.Param, ParamRegistry.FOHLIO_CURRENCY, StringComparison.OrdinalIgnoreCase)).ToList();

            var changes = new List<ProposedChange>();
            var snapshots = new Dictionary<long, (string fref, Dictionary<string, string> snap)>();
            var costData = new Dictionary<long, (double cost, string cur, double qty, int lead)>();
            var costChanges = new List<(Element el, double oldCost, string oldCur, double newCost, string newCur)>();
            var costProblems = new List<string>();
            int matched = plan.Matches.Count, unmatched = plan.Unmatched.Count;

            foreach (var m in plan.Matches)
            {
                var row = rows[m.RowIndex];
                var el = scopeById[m.CandidateId];
                string label = ParameterHelpers.GetString(el, "ASS_TAG_1_TXT");
                if (string.IsNullOrEmpty(label)) label = el.Id.ToString();

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
                row.TryGetValue(refHeader, out string fref);
                snapshots[el.Id.Value] = (snap.TryGetValue(ParamRegistry.FOHLIO_REF, out var fr) ? fr : (fref ?? ""), snap);

                // Procurement cost. A price that cannot be read, or that names no currency, is
                // reported and NOT written: it used to become 0 (dropped) or be priced as USD.
                double cost = 0;
                string cellCur = null;
                string costRaw = costHeader != null && row.TryGetValue(costHeader, out string crw) ? (crw ?? "").Trim() : "";
                if (costRaw.Length > 0 && !FohlioMoney.TryParseCost(costRaw, out cost, out cellCur))
                {
                    costProblems.Add($"{label}: price '{costRaw}' could not be read");
                    cost = 0;
                }
                string curRaw = curHeader != null && row.TryGetValue(curHeader, out string cv) ? (cv ?? "").Trim() : "";
                string colCur = FohlioMoney.NormalizeCurrency(curRaw);
                if (cost > 0 && cellCur != null && colCur != null && !string.Equals(cellCur, colCur, StringComparison.OrdinalIgnoreCase))
                {
                    // "USD 1,250" in a row whose Currency column says UGX: one of them is wrong (L2-FOH-3).
                    costProblems.Add($"{label}: price '{costRaw}' is in {cellCur} but the currency column says {colCur}");
                    cost = 0;
                }
                string cur = colCur ?? cellCur;
                if (cost > 0 && cur == null)
                {
                    costProblems.Add($"{label}: price {costRaw} has no recognisable currency ('{curRaw}')");
                    cost = 0;
                }
                double qty = ParseNum(qtyHeader, row);
                int lead = (int)ParseNum(leadHeader, row);
                costData[el.Id.Value] = (cost, cur ?? "", qty, lead);

                if (cost > 0)
                {
                    double oldCost = el.LookupParameter(ParamRegistry.FOHLIO_UNIT_COST)?.AsDouble() ?? 0;
                    string oldCur = ParameterHelpers.GetString(el, ParamRegistry.FOHLIO_CURRENCY);
                    if (Math.Abs(oldCost - cost) > 1e-9 || !string.Equals(oldCur, cur, StringComparison.OrdinalIgnoreCase))
                        costChanges.Add((el, oldCost, oldCur, cost, cur));
                }
            }

            string unmatchedText = FohlioImportPlanner.DescribeUnmatched(plan, "FF&E");
            string problemText = costProblems.Count == 0 ? "" :
                "\n\nPrices NOT written (" + costProblems.Count + "):\n" +
                string.Join("\n", costProblems.Take(10).Select(x => "  " + x)) +
                (costProblems.Count > 10 ? $"\n  … +{costProblems.Count - 10} more (see the log)" : "");
            foreach (var x in costProblems) StingLog.Warn("Fohlio_Import: " + x);

            if (changes.Count == 0 && costChanges.Count == 0)
            {
                TaskDialog.Show("Fohlio Import",
                    $"Matched {matched} row(s), {unmatched} unmatched. No field or cost changes to write." +
                    unmatchedText + problemText);
                return Result.Succeeded;
            }

            // Preview before ANY write, cost included. It used to be skipped when only prices
            // changed, and cost changes were shown only as a count.
            var preview = new StringBuilder();
            preview.AppendLine($"Matched {matched} row(s) — {unmatched} unmatched.");
            preview.AppendLine($"{changes.Count} field change(s) across {changes.Select(c => c.El.Id.Value).Distinct().Count()} element(s); " +
                               $"{costChanges.Count} price change(s).");
            preview.AppendLine();
            foreach (var c in changes.Take(12))
                preview.AppendLine($"  {c.El.Id} {c.Header}: '{c.Old}' → '{c.New}'");
            if (changes.Count > 12) preview.AppendLine($"  … +{changes.Count - 12} more");
            foreach (var c in costChanges.Take(8))
                preview.AppendLine($"  {c.el.Id} Unit cost: {(c.oldCost > 0 ? $"{c.oldCost:N2} {c.oldCur}" : "(none)")} → {c.newCost:N2} {c.newCur}");
            if (costChanges.Count > 8) preview.AppendLine($"  … +{costChanges.Count - 8} more price(s)");
            preview.Append(unmatchedText);
            preview.Append(problemText);

            var confirm = new TaskDialog("Fohlio Import — preview")
            {
                MainInstruction = "Review before writing to the model",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Apply — fill empty only",
                "Write only where the model value is currently blank (prices too)");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply — overwrite",
                "Overwrite existing model values and prices with the Fohlio values");
            var choice = confirm.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;
            bool overwrite = choice == TaskDialogResult.CommandLink2;

            string fxDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            int written = 0, costWritten = 0, snapshotsStored = 0, skippedNotEditable = 0;
            using (var t = new Transaction(doc, "STING Fohlio Import"))
            {
                t.Start();
                foreach (var c in changes)
                {
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, c.El)) { skippedNotEditable++; continue; }
                    bool ok = overwrite
                        ? ParameterHelpers.SetString(c.El, c.Param, c.New, overwrite: true)
                        : ParameterHelpers.SetIfEmpty(c.El, c.Param, c.New);
                    if (ok) written++;
                }
                // Procurement cost — the user's fill-empty / overwrite choice applies to prices
                // too. The FX-fixing date is stamped only when a price or currency actually
                // changed: re-stamping it on every import moved the date a QS defends at tender.
                foreach (var c in costChanges)
                {
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, c.el)) { skippedNotEditable++; continue; }
                    if (!overwrite && c.oldCost > 0) continue;
                    if (!ParameterHelpers.SetDouble(c.el, ParamRegistry.FOHLIO_UNIT_COST, c.newCost, overwrite: true)) continue;
                    costWritten++;
                    ParameterHelpers.SetString(c.el, ParamRegistry.FOHLIO_CURRENCY, c.newCur, overwrite: true);
                    ParameterHelpers.SetString(c.el, ParamRegistry.CST_FX_DATE_DT, fxDate, overwrite: true);
                }
                // Snapshot what the model now HOLDS, not what the file said — under fill-empty the
                // two differ, and recording the file value made the element read stale forever.
                // Elements another user owns are skipped: writing their entity can fail the
                // whole commit in a workshared model.
                foreach (var kv in snapshots)
                {
                    var el = doc.GetElement(new ElementId(kv.Key));
                    if (el == null) continue;
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, el)) continue;
                    var held = kv.Value.snap.Keys.ToDictionary(k => k, k => ParameterHelpers.GetString(el, k));
                    costData.TryGetValue(kv.Key, out var cd);
                    // The price and currency the model now holds, not the file's: under fill-empty a
                    // declined price was recorded and later read as the item's cost (L2-FOH-4).
                    double heldCost = ParameterHelpers.GetDouble(el, ParamRegistry.FOHLIO_UNIT_COST);
                    string heldCur = ParameterHelpers.GetString(el, ParamRegistry.FOHLIO_CURRENCY);
                    if (StingFohlioSnapshotSchema.Write(el, kv.Value.fref, JsonConvert.SerializeObject(held), DateTime.UtcNow,
                            heldCost, heldCur, cd.qty, cd.lead))
                        snapshotsStored++;
                }
                t.Commit();
            }

            new TaskDialog("Fohlio Import")
            {
                MainInstruction = $"Wrote {written} field value(s) + {costWritten} cost(s)",
                MainContent = $"Matched: {matched}\nUnmatched: {unmatched}\nSnapshots stored: {snapshotsStored} of {snapshots.Count}\n" +
                              (skippedNotEditable > 0 ? $"Not written — owned by another user: {skippedNotEditable}\n" : "") +
                              $"Procurement costs written: {costWritten}\n\n" +
                              "FOHLIO_REF_TXT links each item to Fohlio; FOHLIO_UNIT_COST_NR feeds the BOQ " +
                              "(FohlioRateProvider), and ASS_CST_FX_DATE_DT records the FX-fixing date, which the " +
                              "BOQ Item Schedule reports beside the converted rate."
            }.Show();
            StingLog.Info($"Fohlio_Import: matched={matched} wrote={written} costs={costWritten} snapshots={snapshotsStored}/{snapshots.Count} notEditable={skippedNotEditable}");
            return Result.Succeeded;
        }

        // Phase C — parse an optional numeric column from a row; 0 when absent/blank.
        private static double ParseNum(string header, Dictionary<string, string> row)
        {
            if (header == null || !row.TryGetValue(header, out string s) || string.IsNullOrWhiteSpace(s)) return 0;
            return double.TryParse(s.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
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
                var lines = FohlioCsv.ReadUtf8Lines(path);
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
            if (FohlioScope.RefuseBrokenMap(map, "Fohlio Audit")) return Result.Failed;
            var scope = FohlioScope.Collect(doc, map);
            if (scope.Count == 0)
            {
                // Not "100% linked": nothing was examined. Usually a wrong category list, a bad
                // map or a model without FF&E.
                TaskDialog.Show("Fohlio Audit", "NO FF&E IN SCOPE — nothing was audited.\n\n" +
                    $"Mapped categories: {string.Join(", ", map.Categories)}");
                return Result.Succeeded;
            }
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
                    var heldCost = ParameterHelpers.GetDouble(el, ParamRegistry.FOHLIO_UNIT_COST);
                    var heldCur = ParameterHelpers.GetString(el, ParamRegistry.FOHLIO_CURRENCY);
                    isStale = FohlioStale.IsStale(writeParams, p => ParameterHelpers.GetString(el, p),
                        heldCost, heldCur, snapVals, snap.UnitCost, snap.Currency,
                        ParamRegistry.FOHLIO_UNIT_COST, ParamRegistry.FOHLIO_CURRENCY);
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
