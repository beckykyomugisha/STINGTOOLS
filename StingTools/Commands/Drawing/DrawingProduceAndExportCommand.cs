// StingTools — AUTO-2: One-click drawing production, style sync, and export
//
// DrawingProduceAndExportCommand is the end-to-end "fire and forget" command
// that chains every drawing-production sub-system in the correct order:
//
//   Phase A — Production (optional, idempotent)
//     For every Plan-purpose DrawingType the person ticks (pre-ticked: the
//     routed plan type of each discipline modelled) × every Level,
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
//     Every STING-stamped sheet (or, when the project has a current revision, the
//     ones carrying it — asked in the dialog, params.sheets in a workflow) is
//     exported to PDF via doc.Export and recorded in the document register the way
//     the Export Centre records its output, ordered
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
                        "  Asks which plan drawing types (those of the disciplines\n" +
                        "  modelled here are pre-ticked), produces them on every level, syncs\n" +
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

                // Option 1 used to produce EVERY Plan drawing type (50+: architectural,
                // structural, presentation, healthcare…) on every level. Ask which,
                // with the plan types of the disciplines modelled here ticked.
                if (doProduction && planTypes.Count > 0)
                {
                    planTypes = PickPlanTypes(doc, planTypes);
                    if (planTypes == null) return Result.Cancelled;
                    if (planTypes.Count == 0)
                    {
                        TaskDialog.Show("STING — Produce & Export", "No drawing type was ticked — nothing produced, nothing exported.");
                        return Result.Cancelled;
                    }
                }

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

                // ── Which sheets: those carrying the current revision, or all ────
                // Exporting every stamped sheet re-issued sheets the current revision
                // never touched. When some stamped sheets do not carry it, ask.
                var onRevision = ScopeToRevision(doc, stampedSheets, ExportSheetScope.CurrentRevision,
                    out var currentRev, out int notOnRevision);
                if (currentRev != null && notOnRevision > 0)
                {
                    var ask = new TaskDialog("STING — Produce & Export")
                    {
                        MainInstruction = $"{onRevision.Count} of {stampedSheets.Count} stamped sheet(s) carry the current revision {currentRev}.",
                        MainContent = "Export only the sheets being issued in this revision, or every stamped sheet?",
                        CommonButtons = TaskDialogCommonButtons.Cancel,
                    };
                    ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        $"Only the {onRevision.Count} sheet(s) carrying revision {currentRev}");
                    ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        $"All {stampedSheets.Count} stamped sheets");
                    ask.DefaultButton = doProduction ? TaskDialogResult.CommandLink2 : TaskDialogResult.CommandLink1;
                    var pick = ask.Show();
                    if (pick == TaskDialogResult.CommandLink1)
                    {
                        if (onRevision.Count == 0)
                        {
                            TaskDialog.Show("STING — Produce & Export",
                                $"No stamped sheet carries revision {currentRev}; nothing exported.");
                            return Result.Cancelled;
                        }
                        stampedSheets = onRevision;
                    }
                    else if (pick != TaskDialogResult.CommandLink2) return Result.Cancelled;
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
        /// or "finalize" (existing sheets only); params.sheets "current-revision" | "all"
        /// (ExportSheetScope); params.drawingTypes (default the routed plan type of each
        /// modelled M/E/P/FP/MG discipline, on the levels it occupies),
        /// params.levels (default every level), params.output /
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

            // Which stamped sheets the export covers. A produce run defaults to all (the
            // sheets it has just made carry no revision yet); a finalize run to the sheets
            // carrying the current revision (ExportSheetScope).
            var scope = ExportSheetScope.Parse(WorkflowEngine.StepParam("sheets"),
                doProduction ? ExportSheetScope.All : ExportSheetScope.CurrentRevision, out var scopeErr);
            if (scope == null) { message = $"{title}: {scopeErr}"; return Result.Failed; }

            var stats = new RunStats();
            if (doProduction)
            {
                if (!BatchProduceCommons.TryStepPerLevelTypes(doc, out var sel, out var err)
                    || !BatchProduceCommons.TryStepOptions(out var opts, out var packageId, out err))
                { message = $"{title}: {err}"; return Result.Failed; }
                var types = sel.Types;
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (levels.Count == 0) { message = $"{title}: the model has no levels."; return Result.Failed; }
                var names = HeadlessProductionInputs.SelectNames(levels.Select(l => l.Name).ToList(),
                    HeadlessProductionInputs.ParseList(WorkflowEngine.StepParam("levels")), out var unknownLevels);
                if (unknownLevels.Count > 0)
                { message = $"{title}: params.levels names level(s) not in the model: {string.Join(", ", unknownLevels)}."; return Result.Failed; }
                RunProductionPhase(doc, types, levels.Where(l => names.Contains(l.Name)).ToList(), stats, opts, packageId, sel.Include);
            }

            RunStyleSyncPhase(doc, stats);
            RunRevisionSyncPhase(doc, stats);

            var allStamped = CollectStampedSheets(doc);
            if (allStamped.Count == 0)
            { message = $"{title}: no STING-stamped sheets to export — produce sheets first."; return Result.Failed; }
            var stampedSheets = ScopeToRevision(doc, allStamped, scope, out var currentRev, out int excluded);
            if (stampedSheets.Count == 0)
            {
                message = $"{title}: none of the {allStamped.Count} stamped sheet(s) carries the current revision {currentRev}; "
                        + "nothing exported. Add the revision to the sheets being issued, or set params.sheets to 'all'.";
                return Result.Failed;
            }

            ExportStamped(doc, stampedSheets, stats);

            foreach (var w in stats.Warnings.Distinct()) StingLog.Warn($"{title}: {w}");
            message = $"{title}: {stats.ViewsProduced} view(s), {stats.SheetsProduced} new sheet(s), "
                    + $"{stats.PdfsExported} PDF(s) of {stampedSheets.Count} sheet(s)"
                    + (excluded > 0 ? $" carrying revision {currentRev} ({excluded} other stamped sheet(s) not exported)" : "")
                    + $", {stats.Registered} recorded in the document register"
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
            // The PDFs go into the document register the way Export Centre output does
            // (ExportCenterEngine.RegisterExportedFiles): until now Produce & Export left
            // them as loose files the Document Manager never listed.
            try
            {
                stats.Registered = StingTools.Docs.ExportCenterEngine.RegisterExportedFiles(doc, stats.Exported);
                if (stats.Exported.Count > 0 && stats.Registered == 0)
                    stats.Warnings.Add($"Document register: none of the {stats.Exported.Count} exported PDF(s) was recorded (see the STING log).");
            }
            catch (Exception ex)
            {
                stats.Warnings.Add($"Document register: {ex.Message}");
                StingLog.Warn($"ProduceAndExport register: {ex.Message}");
            }
            string regDir = ProjectFolderEngine.GetExportFolder(doc, "DocRegister");
            RunSheetRegisterPhase(doc, stampedSheets, string.IsNullOrEmpty(regDir) ? outDir : regDir, stats);
            return outDir;
        }

        /// <summary>
        /// The project's current revision (newest in the revision sequence) and the
        /// stamped sheets <paramref name="scope"/> keeps (ExportSheetScope.Select).
        /// <paramref name="currentLabel"/> is null when the project has no revision.
        /// </summary>
        private static List<ViewSheet> ScopeToRevision(Document doc, List<ViewSheet> sheets, string scope,
            out string currentLabel, out int excluded)
        {
            currentLabel = null;
            long? currentId = null;
            try
            {
                var ids = Revision.GetAllRevisionIds(doc);
                if (ids != null && ids.Count > 0 && doc.GetElement(ids[ids.Count - 1]) is Revision cur)
                {
                    currentId = cur.Id.Value;
                    currentLabel = string.IsNullOrWhiteSpace(cur.RevisionNumber) ? cur.Description : cur.RevisionNumber;
                    if (string.IsNullOrWhiteSpace(currentLabel)) currentLabel = $"#{cur.SequenceNumber}";
                }
            }
            catch (Exception ex) { StingLog.Warn($"ProduceAndExport current revision: {ex.Message}"); }

            IEnumerable<long> RevsOf(ViewSheet s)
            {
                try { return s.GetAllRevisionIds().Select(i => i.Value).ToList(); }
                catch (Exception ex) { StingLog.Warn($"ProduceAndExport revisions of '{s.SheetNumber}': {ex.Message}"); return new List<long>(); }
            }
            return ExportSheetScope.Select(sheets, RevsOf, currentId, scope, out excluded);
        }

        // ── Type selection (dialog only) ────────────────────────────────────────

        /// <summary>
        /// Let the person tick the Plan types to produce. Pre-ticked: the type each
        /// discipline modelled in the project routes its PLAN to (DisciplinePlanRouting,
        /// the same answer the wizard and the HVAC per-level button use). Null on cancel.
        /// </summary>
        private static List<DrawingType> PickPlanTypes(Document doc, List<DrawingType> planTypes)
        {
            var present = DisciplinesModelled(doc);
            var routed = BatchProduceCommons.RoutePerLevel(doc, present);
            var preselect = new HashSet<string>(routed.Types.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);

            var items = planTypes
                .OrderBy(t => t.Discipline ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .Select(t => new StingTools.Select.StingListPicker.ListItem
                {
                    Label = t.Id,
                    Detail = $"{t.Discipline} · {t.Name}",
                    Tag = t,
                    IsSelected = preselect.Contains(t.Id),
                })
                .ToList();
            string subtitle = present.Count == 0
                ? "Nothing is modelled yet, so nothing is pre-ticked. Tick the plan drawing types to produce on every level."
                : $"Pre-ticked: the plan type of each discipline modelled here ({string.Join(", ", present)}). "
                  + "Each ticked type is produced on every level.";
            var picked = StingTools.Select.StingListPicker.Show("Produce & Export — drawing types", subtitle, items, true);
            return picked?.Select(i => i.Tag as DrawingType).Where(t => t != null).ToList();
        }

        /// <summary>The discipline codes with anything modelled: A, S and the MEP set.</summary>
        private static List<string> DisciplinesModelled(Document doc)
        {
            var list = new List<string>();
            bool Any(params BuiltInCategory[] cats)
            {
                try
                {
                    return new FilteredElementCollector(doc).WhereElementIsNotElementType()
                        .WherePasses(new ElementMulticategoryFilter(cats)).FirstElementId() != ElementId.InvalidElementId;
                }
                catch (Exception ex) { StingLog.Warn($"ProduceAndExport presence: {ex.Message}"); return false; }
            }
            if (Any(BuiltInCategory.OST_Walls, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows, BuiltInCategory.OST_Rooms))
                list.Add("A");
            if (Any(BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralFoundation))
                list.Add("S");
            var mep = StingTools.Core.Mep.MepLevelViewProducer.LevelsByDiscipline(doc);
            list.AddRange(StingTools.Core.Mep.MepLevelViewProducer.Disciplines.Where(mep.ContainsKey));
            return list;
        }

        // ── Phase A ─────────────────────────────────────────────────────────────

        private static void RunProductionPhase(
            Document doc, List<DrawingType> planTypes, List<Level> levels, RunStats stats,
            ProduceOptions opts = null, string packageId = null,
            Func<DrawingType, Level, bool> include = null)
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
                        // The routed default skips a discipline on a level where it has nothing modelled.
                        if (include != null && !include(dt, level)) continue;
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
                            stats.Warnings.AddRange(res.Warnings);
                            // Counted only once Revit has committed: a commit a failure
                            // handler rolls back kept nothing.
                            var status = t.Commit();
                            if (status != TransactionStatus.Committed)
                            {
                                stats.Warnings.Add($"Produce [{dt.Id}@{level.Name}]: the transaction did not commit ({status}); "
                                                 + $"{res.ViewIds.Count} view(s) were not kept.");
                                continue;
                            }

                            stats.ViewsProduced   += res.ViewIds.Count;
                            stats.SheetsProduced  += res.SheetId != ElementId.InvalidElementId && !res.SheetReused ? 1 : 0;
                            stats.ViewsIdempotent += res.WasIdempotent ? 1 : 0;
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
                int resyncedBefore = stats.StylesResynced;

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
                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        stats.Warnings.Add($"StyleSync: the transaction did not commit ({status}); no view was re-aligned.");
                        stats.StylesResynced = resyncedBefore;
                    }
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
                    bool ok = doc.Export(dir, new List<ElementId> { sheet.Id }, exportOpts);
                    string path = Path.Combine(dir, filename + ".pdf");
                    if (!ok)
                    {
                        stats.Warnings.Add($"PDF [{sheet.SheetNumber}]: Revit reported the export as failed.");
                        continue;
                    }
                    stats.PdfsExported++;
                    stats.PdfFolders.Add(dir);
                    if (!File.Exists(path))
                    {
                        stats.Warnings.Add($"PDF [{sheet.SheetNumber}]: exported, but not found at {path}, so it was not recorded in the document register.");
                        continue;
                    }
                    stats.Exported.Add(new StingTools.Docs.ExportCenterEngine.ExportedFile
                    {
                        Sheet = sheet, Path = path, Format = "PDF",
                        Title = sheet.Name, SheetNumber = sheet.SheetNumber,
                    });
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

            sb.AppendLine($"Doc register: {stats.Registered} PDF(s) recorded");
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
            public int Registered      { get; set; }
            public List<StingTools.Docs.ExportCenterEngine.ExportedFile> Exported { get; }
                = new List<StingTools.Docs.ExportCenterEngine.ExportedFile>();
            public string RegisterCsvPath { get; set; }
            public List<string> Warnings { get; } = new List<string>();
            public HashSet<string> PdfFolders { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
