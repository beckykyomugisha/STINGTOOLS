// StingTools — AUTO-2: One-click drawing production, style sync, and export
//
// DrawingProduceAndExportCommand is the end-to-end "fire and forget" command
// that chains every drawing-production sub-system in the correct order:
//
//   Phase A — Production (optional, idempotent)
//     For every Plan-purpose DrawingType × every Level in the model,
//     DrawingProducer.ProduceAllViews creates the view+sheet (or reuses the
//     existing stamped one). All writes run inside a TransactionGroup so a
//     failure rolls back only the current view, not the whole batch.
//
//   Phase B — Style synchronisation
//     DrawingDriftDetector.Scan finds every STING-stamped view whose
//     scale/detail/template/pack has drifted.  DrawingTypePresentation.Apply
//     re-aligns each drifted view to its profile (annotation skipped —
//     same as the manual SyncStyles command).
//
//   Phase C — Revision strip synchronisation
//     TitleBlockRevisionSyncer.SyncAll writes the current Revit Revision
//     sequence into PRJ_TB_REV_COL_n / _DATE_n / _DESC_n cells on every
//     stamped sheet.
//
//   Phase D — PDF export
//     Every STING-stamped sheet is exported to PDF via doc.Export, ordered
//     by STING_SHEET_SEQUENCE_INT then SheetNumber.  Output goes to the
//     project output folder (OutputLocationHelper).
//
//   Phase E — Sheet register CSV
//     A lean CSV register is written alongside the PDFs: SheetNumber, Name,
//     DrawingTypeId, Discipline, Scale, Status.
//
// Tag: DrawingTypes_ProduceAndExport
// UI:  DOCS tab → DRAWING TYPES section → "Produce & Export" button

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingProduceAndExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                // ── Scope dialog ────────────────────────────────────────────────
                var scopeDlg = new TaskDialog("STING — Produce & Export")
                {
                    MainInstruction = "What would you like to do?",
                    MainContent =
                        "Produce + Finalize + Export\n" +
                        "  Creates plan views for every level × drawing type, syncs\n" +
                        "  styles and revisions, then exports all stamped sheets to PDF.\n\n" +
                        "Finalize + Export (existing sheets only)\n" +
                        "  Syncs styles and revisions on already-produced sheets,\n" +
                        "  then exports all stamped sheets to PDF.",
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Produce + Finalize + Export");
                scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Finalize + Export  (existing sheets only)");

                var scopeAnswer = scopeDlg.Show();
                if (scopeAnswer == TaskDialogResult.Cancel || scopeAnswer == TaskDialogResult.Close)
                    return Result.Cancelled;

                bool doProduction = scopeAnswer == TaskDialogResult.CommandLink1;

                // ── Collect model data ───────────────────────────────────────────
                var allTypes = DrawingTypeRegistry.ListAll(doc);
                var planTypes = allTypes
                    .Where(t => string.Equals(t.Purpose, "Plan", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .ToList();

                var stats = new RunStats();

                // ── Phase A: Production ──────────────────────────────────────────
                if (doProduction)
                {
                    if (planTypes.Count == 0)
                    {
                        TaskDialog.Show("STING", "No Plan-purpose Drawing Types found in the registry.\nSkipping production phase.");
                    }
                    else if (levels.Count == 0)
                    {
                        TaskDialog.Show("STING", "No levels found in the model.\nSkipping production phase.");
                    }
                    else
                    {
                        RunProductionPhase(doc, planTypes, levels, stats);
                    }
                }

                // ── Phase B: Style sync ──────────────────────────────────────────
                RunStyleSyncPhase(doc, stats);

                // ── Phase C: Revision sync ───────────────────────────────────────
                RunRevisionSyncPhase(doc, stats);

                // ── Collect all stamped sheets for export ────────────────────────
                var stampedSheets = CollectStampedSheets(doc);

                if (stampedSheets.Count == 0)
                {
                    TaskDialog.Show("STING — Produce & Export",
                        "No STING-stamped sheets found to export.\n" +
                        "Use 'Produce Per Level' or another production command first.");
                    return Result.Succeeded;
                }

                // ── Phase D: PDF export ──────────────────────────────────────────
                var outDir = OutputLocationHelper.GetOutputDirectory(doc);
                RunPdfExportPhase(doc, stampedSheets, outDir, stats);

                // ── Phase E: Sheet register CSV ──────────────────────────────────
                RunSheetRegisterPhase(doc, stampedSheets, outDir, stats);

                // ── Summary ──────────────────────────────────────────────────────
                ShowSummary(stats, outDir, doProduction);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingProduceAndExport", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Phase A ─────────────────────────────────────────────────────────────

        private static void RunProductionPhase(
            Document doc, List<DrawingType> planTypes, List<Level> levels, RunStats stats)
        {
            var opts = new ProduceOptions
            {
                CreateSheet    = true,
                PlaceOnSheet   = true,
                RunAnnotation  = true,
                Idempotent     = true,
            };

            using (DrawingProducer.PrimeBatchScope(doc))
            using (var tg = new TransactionGroup(doc, "STING Produce & Export — Production"))
            {
                tg.Start();
                foreach (var dt in planTypes)
                {
                    foreach (var level in levels)
                    {
                        try
                        {
                            var ctx = new DrawingContext { Level = level, Tag = level.Name };
                            var res = DrawingProducer.ProduceAllViews(doc, dt, ctx, opts);

                            stats.ViewsProduced   += res.ViewIds.Count;
                            stats.SheetsProduced  += res.SheetId != ElementId.InvalidElementId && !res.WasIdempotent ? 1 : 0;
                            stats.ViewsIdempotent += res.WasIdempotent ? 1 : 0;
                            stats.Warnings.AddRange(res.Warnings);
                        }
                        catch (Exception ex)
                        {
                            stats.Warnings.Add($"Produce [{dt.Id}@{level.Name}]: {ex.Message}");
                            StingLog.Warn($"ProduceAndExport produce: {dt.Id}@{level.Name} — {ex.Message}");
                        }
                    }
                }
                tg.Assimilate();
            }
        }

        // ── Phase B ─────────────────────────────────────────────────────────────

        private static void RunStyleSyncPhase(Document doc, RunStats stats)
        {
            try
            {
                var allReports = DrawingDriftDetector.Scan(doc);
                var reports    = allReports.Where(r => r.AnyActionable).ToList();
                if (reports.Count == 0) return;

                using (TitleBlockParamApplier.Batch())
                using (var tx = new Transaction(doc, "STING Produce & Export — Sync Styles"))
                {
                    tx.Start();
                    foreach (var r in reports)
                    {
                        if (!(doc.GetElement(r.ViewId) is View v)) continue;
                        var dt = DrawingTypeRegistry.Get(doc, r.DrawingTypeId);
                        if (dt == null) continue;

                        try
                        {
                            if (v is ViewSheet sheet)
                            {
                                var res = DrawingTypePresentation.ApplyToSheet(doc, sheet, dt);
                                stats.StylesResynced++;
                                stats.Warnings.AddRange(res.Warnings.Select(w => $"[Styles/{v.Name}] {w}"));
                            }
                            else
                            {
                                var res = DrawingTypePresentation.Apply(doc, v, dt,
                                    new DrawingTypePresentation.ApplyOptions
                                    {
                                        AnnotationOptions = new AnnotationRunOptions
                                        {
                                            SkipAutoTag   = true,
                                            SkipAutoDim   = true,
                                            SkipDecorative = true,
                                            SkipSpots     = true,
                                        }
                                    });
                                if (res.ScaleApplied || res.DetailLevelApplied || res.TemplateApplied || res.PackApplied)
                                    stats.StylesResynced++;
                                stats.Warnings.AddRange(res.Warnings.Select(w => $"[Styles/{v.Name}] {w}"));
                            }
                        }
                        catch (Exception ex)
                        {
                            stats.Warnings.Add($"StyleSync [{v.Name}]: {ex.Message}");
                        }
                    }
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                stats.Warnings.Add($"StyleSync phase: {ex.Message}");
                StingLog.Warn($"ProduceAndExport StyleSync: {ex.Message}");
            }
        }

        // ── Phase C ─────────────────────────────────────────────────────────────

        private static void RunRevisionSyncPhase(Document doc, RunStats stats)
        {
            try
            {
                var result = TitleBlockRevisionSyncer.SyncAll(doc);
                stats.RevisionsUpdated = result.SheetsProcessed;
                stats.Warnings.AddRange(result.Warnings.Select(w => $"[RevSync] {w}"));
            }
            catch (Exception ex)
            {
                stats.Warnings.Add($"RevisionSync phase: {ex.Message}");
                StingLog.Warn($"ProduceAndExport RevisionSync: {ex.Message}");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static List<ViewSheet> CollectStampedSheets(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !string.IsNullOrEmpty(
                    ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID)))
                .OrderBy(s => ParameterHelpers.GetInt(s, DrawingTypeStamper.PARAM_SHEET_SEQUENCE, 0))
                .ThenBy(s => s.SheetNumber)
                .ToList();
        }

        // ── Phase D ─────────────────────────────────────────────────────────────

        private static void RunPdfExportPhase(
            Document doc, List<ViewSheet> sheets, string outDir, RunStats stats)
        {
            try
            {
                if (!Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);
            }
            catch (Exception ex)
            {
                stats.Warnings.Add($"PDF output dir: {ex.Message}");
                return;
            }

            foreach (var sheet in sheets)
            {
                try
                {
                    string filename = MakeSafeFilename(
                        $"{sheet.SheetNumber}_{sheet.Name}");
                    var exportOpts = new PDFExportOptions { FileName = filename };
                    doc.Export(outDir, new List<ElementId> { sheet.Id }, exportOpts);
                    stats.PdfsExported++;
                }
                catch (Exception ex)
                {
                    stats.Warnings.Add($"PDF [{sheet.SheetNumber}]: {ex.Message}");
                }
            }
        }

        // ── Phase E ─────────────────────────────────────────────────────────────

        private static void RunSheetRegisterPhase(
            Document doc, List<ViewSheet> sheets, string outDir, RunStats stats)
        {
            try
            {
                if (!Directory.Exists(outDir))
                    Directory.CreateDirectory(outDir);

                string csvPath = Path.Combine(outDir,
                    $"SheetRegister_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                var sb = new StringBuilder();
                sb.AppendLine("SheetNumber,SheetName,DrawingTypeId,Discipline,Scale,Status");

                foreach (var sheet in sheets)
                {
                    string dtId   = ParameterHelpers.GetString(sheet, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID);
                    var dt        = string.IsNullOrEmpty(dtId) ? null : DrawingTypeRegistry.Get(doc, dtId);
                    string disc   = dt?.Discipline ?? "";
                    string scale  = dt?.Scale > 0 ? $"1:{dt.Scale}" : "";
                    string status = ParameterHelpers.GetString(sheet, "STING_CDE_STATUS_TXT");
                    if (string.IsNullOrEmpty(status)) status = "WIP";

                    sb.AppendLine(string.Join(",",
                        CsvEscape(sheet.SheetNumber),
                        CsvEscape(sheet.Name),
                        CsvEscape(dtId),
                        CsvEscape(disc),
                        CsvEscape(scale),
                        CsvEscape(status)));
                }

                OutputLocationHelper.WriteAllTextAtomic(csvPath, sb.ToString());
                stats.RegisterCsvPath = csvPath;
            }
            catch (Exception ex)
            {
                stats.Warnings.Add($"SheetRegister CSV: {ex.Message}");
            }
        }

        // ── Summary ──────────────────────────────────────────────────────────────

        private static void ShowSummary(RunStats stats, string outDir, bool didProduction)
        {
            var sb = new StringBuilder();

            if (didProduction)
            {
                sb.AppendLine($"Production:  {stats.ViewsProduced} view(s), {stats.SheetsProduced} new sheet(s)" +
                              (stats.ViewsIdempotent > 0 ? $", {stats.ViewsIdempotent} reused" : ""));
            }

            sb.AppendLine($"Style sync:  {stats.StylesResynced} view(s) re-aligned");
            sb.AppendLine($"Rev strip:   {stats.RevisionsUpdated} sheet(s) updated");
            sb.AppendLine($"PDF export:  {stats.PdfsExported} sheet(s) → {outDir}");

            if (!string.IsNullOrEmpty(stats.RegisterCsvPath))
                sb.AppendLine($"Register:    {Path.GetFileName(stats.RegisterCsvPath)}");

            if (stats.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Warnings ({stats.Warnings.Count}):");
                foreach (var w in stats.Warnings.Take(20)) sb.AppendLine("  • " + w);
                if (stats.Warnings.Count > 20)
                    sb.AppendLine($"  …({stats.Warnings.Count - 20} more — see STING log)");
            }

            TaskDialog.Show("STING — Produce & Export Complete", sb.ToString());
        }

        // ── Utilities ────────────────────────────────────────────────────────────

        private static string MakeSafeFilename(string name)
        {
            if (string.IsNullOrEmpty(name)) return "sheet";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string CsvEscape(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.Contains(',') || v.Contains('"') || v.Contains('\n')
                ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        }

        // ── Stats container ───────────────────────────────────────────────────────

        private sealed class RunStats
        {
            public int ViewsProduced   { get; set; }
            public int SheetsProduced  { get; set; }
            public int ViewsIdempotent { get; set; }
            public int StylesResynced  { get; set; }
            public int RevisionsUpdated { get; set; }
            public int PdfsExported    { get; set; }
            public string RegisterCsvPath { get; set; }
            public List<string> Warnings { get; } = new List<string>();
        }
    }
}
