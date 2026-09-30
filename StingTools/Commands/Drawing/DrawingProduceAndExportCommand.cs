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
//   Phase C — Revision synchronisation
//     TitleBlockRevisionSyncer.SyncAll writes the newest Revit Revision's
//     number / date / description onto every stamped sheet (SHT_REV_TXT,
//     SHT_REV_DATE_TXT) and its title-block instances
//     (PRJ_TB_REVISION_NR_TXT / _DATE_TXT / _DESCRIPTION_TXT).
//
//   Phase D — PDF export
//     Every STING-stamped sheet is exported to PDF via doc.Export, ordered
//     by PRJ_SHEET_SEQUENCE_INT then SheetNumber.  Output goes to the
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
                var doc = (commandData?.Application ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                if (BatchProduceCommons.Headless) return ExecuteInWorkflow(doc, ref message);

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

                // ── Phases D + E: PDF export, sheet register ─────────────────────
                var outDir = ExportStamped(doc, stampedSheets, stats);

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

        /// <summary>
        /// Inside a workflow: params.mode "produce" (default — produce, finalize, export)
        /// or "finalize" (existing sheets only); params.drawingTypes (default every MEP
        /// Plan type), params.levels (default every level), params.output /
        /// duplicateOption / packageId as for Produce Per Level. The summary goes to the
        /// workflow report and the log, not a dialog. No stamped sheet to export fails
        /// the step: an export step that exported nothing has not done its job.
        /// </summary>
        private static Result ExecuteInWorkflow(Document doc, ref string message)
        {
            const string title = "Produce & Export";
            var modeRaw = (WorkflowEngine.StepParam("mode") ?? "").Trim();
            bool doProduction;
            if (modeRaw.Length == 0 || modeRaw.StartsWith("produce", StringComparison.OrdinalIgnoreCase)) doProduction = true;
            else if (modeRaw.StartsWith("finali", StringComparison.OrdinalIgnoreCase)) doProduction = false;
            else { message = $"{title}: params.mode '{modeRaw}' is not 'produce' or 'finalize'."; return Result.Failed; }

            var stats = new RunStats();
            if (doProduction)
            {
                if (!BatchProduceCommons.TryStepTypes(DrawingTypeRegistry.ListAll(doc), new[] { "Plan" }, out var types, out var err)
                    || !BatchProduceCommons.TryStepOptions(out var opts, out var packageId, out err))
                { message = $"{title}: {err}"; return Result.Failed; }
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (levels.Count == 0) { message = $"{title}: the model has no levels."; return Result.Failed; }
                var names = HeadlessProductionInputs.SelectNames(levels.Select(l => l.Name).ToList(),
                    HeadlessProductionInputs.ParseList(WorkflowEngine.StepParam("levels")), out var unknownLevels);
                if (unknownLevels.Count > 0)
                { message = $"{title}: params.levels names level(s) not in the model: {string.Join(", ", unknownLevels)}."; return Result.Failed; }
                RunProductionPhase(doc, types, levels.Where(l => names.Contains(l.Name)).ToList(), stats, opts, packageId);
            }

            RunStyleSyncPhase(doc, stats);
            RunRevisionSyncPhase(doc, stats);

            var stampedSheets = CollectStampedSheets(doc);
            if (stampedSheets.Count == 0)
            { message = $"{title}: no STING-stamped sheets to export — produce sheets first."; return Result.Failed; }

            ExportStamped(doc, stampedSheets, stats);

            foreach (var w in stats.Warnings.Distinct()) StingLog.Warn($"{title}: {w}");
            message = $"{title}: {stats.ViewsProduced} view(s), {stats.SheetsProduced} new sheet(s), "
                    + $"{stats.PdfsExported} PDF(s) of {stampedSheets.Count} sheet(s)"
                    + (stats.Warnings.Count > 0 ? $", {stats.Warnings.Distinct().Count()} warning(s) (see the STING log)." : ".");
            StingLog.Info(message);
            return stats.PdfsExported > 0 ? Result.Succeeded : Result.Failed;
        }

        /// <summary>
        /// Phases D and E, shared by the dialog and the workflow. Returns the fallback
        /// output folder.
        /// Phase D was OutputLocationHelper.GetOutputDirectory — the MISC folder — for
        /// every sheet of every discipline. Each PDF now goes to its own deliverable
        /// folder (CDE state + discipline); outDir is the fallback for an unsaved model
        /// and the home of the register CSV.
        /// </summary>
        private static string ExportStamped(Document doc, List<ViewSheet> stampedSheets, RunStats stats)
        {
            var outDir = OutputLocationHelper.GetOutputDirectory(doc);
            RunPdfExportPhase(doc, stampedSheets, outDir, stats);
            string regDir = ProjectFolderEngine.GetExportFolder(doc, "DocRegister");
            RunSheetRegisterPhase(doc, stampedSheets, string.IsNullOrEmpty(regDir) ? outDir : regDir, stats);
            return outDir;
        }

        // ── Phase A ─────────────────────────────────────────────────────────────

        private static void RunProductionPhase(
            Document doc, List<DrawingType> planTypes, List<Level> levels, RunStats stats,
            ProduceOptions opts = null, string packageId = null)
        {
            opts = opts ?? new ProduceOptions
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
                        // The producer does not open transactions (see its header), and
                        // this loop used to run it under the group alone — so every sheet
                        // and view creation threw "outside a transaction" and was caught
                        // as a warning: Produce + Finalize + Export produced nothing.
                        using (var t = new Transaction(doc, $"STING Produce & Export — {dt.Id} @ {level.Name}"))
                        try
                        {
                            t.Start();
                            // P-9: ctx.Tag must stay null here. ProduceViewsPerLevelCommand leaves
                            // it null, and BuildContextTag folds Tag into the view
                            // idempotency key — so setting it to the level name gave the
                            // two per-level paths different keys and running both
                            // DUPLICATED every per-level view, despite both claiming
                            // idempotency. The level is already in the key via ctx.Level.
                            var ctx = new DrawingContext { Level = level, PackageId = packageId };
                            var res = DrawingProducer.ProduceAllViews(doc, dt, ctx, opts);
                            t.Commit();

                            stats.ViewsProduced   += res.ViewIds.Count;
                            stats.SheetsProduced  += res.SheetId != ElementId.InvalidElementId && !res.SheetReused ? 1 : 0;
                            stats.ViewsIdempotent += res.WasIdempotent ? 1 : 0;
                            stats.Warnings.AddRange(res.Warnings);
                        }
                        catch (Exception ex)
                        {
                            if (t.GetStatus() == TransactionStatus.Started) t.RollBack();
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
                                        },
                                        SkipSymbolDriftCheck = true // styles re-sync pass
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
                // Produce & Export operates on STING-stamped sheets throughout,
                // so keep Phase C to the same scope.
                var result = TitleBlockRevisionSyncer.SyncAll(doc, stampedOnly: true);
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
                    string dir = StingTools.Docs.ExportCenterEngine.DeliverableFolderForSheet(doc, sheet) ?? outDir;
                    doc.Export(dir, new List<ElementId> { sheet.Id }, exportOpts);
                    stats.PdfsExported++;
                    stats.PdfFolders.Add(dir);
                }
                catch (Exception ex2)
                {
                    stats.Warnings.Add($"PDF [{sheet.SheetNumber}]: {ex2.Message}");
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
                    // The CDE state the title-block sync stamps. STING_CDE_STATUS_TXT does not
                    // exist, so every sheet was reported "WIP"; an unstamped sheet stays blank.
                    string status = ParameterHelpers.GetString(sheet, ParamRegistry.TB_DELIVERABLE_CDE);

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
            sb.AppendLine(stats.PdfFolders.Count <= 1
                ? $"PDF export:  {stats.PdfsExported} sheet(s) → {stats.PdfFolders.FirstOrDefault() ?? outDir}"
                : $"PDF export:  {stats.PdfsExported} sheet(s) into {stats.PdfFolders.Count} discipline/state folders:\n    "
                  + string.Join("\n    ", stats.PdfFolders.OrderBy(f => f)));

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
            public HashSet<string> PdfFolders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
