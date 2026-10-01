// StingTools — Drawing Template Manager · Excel Round-Trip
//
// Bidirectional Excel ↔ JSON exchange for the corporate Drawing Type
// catalogue and the View Style Pack library. Power users and BIM
// managers can export everything to a structured workbook, edit values
// (with validation dropdowns and live colour swatches), and import
// back into the project's _BIM_COORD/ override files. The runtime
// caches are invalidated on import so changes take effect without a
// Revit restart.
//
// Wired from the DOCS tab (DrawingTypes_ExportExcel /
// DrawingTypes_ImportExcel button tags) via StingCommandHandler.
//
// Workbook layout: 8 sheets (DrawingTypes, StylePacks, VgOverrides,
// FilterRules, Slots, TitleBlockParams, Routing, _Legend hidden).
// Colour cells in VgOverrides + FilterRules render the resolved hex
// value as the cell background fill — instant visual swatch.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.UI;

namespace StingTools.BIMManager
{
    internal static partial class DrawingTypeExcelEngine
    {
        // ──────────────────────────────────────────────────────────────────
        //  ApplyImport — write JSON files + invalidate runtime cache
        // ──────────────────────────────────────────────────────────────────

        public static void ApplyImport(Document doc,
            DrawingTypeLibrary updatedDtLib, StylePackDoc updatedPacks, string outputDir)
        {
            if (string.IsNullOrEmpty(outputDir))
                throw new ArgumentException("outputDir must not be empty.", nameof(outputDir));
            Directory.CreateDirectory(outputDir);

            // Project override files only contain entries whose origin is "project".
            // Corporate baselines on disk stay untouched — drift on a corporate row
            // flips its origin to "project" first (FlipModifiedCorporateOrigin*).
            var (projectDt, projectPacks) = BuildProjectOverride(updatedDtLib, updatedPacks);

            var dtPath   = Path.Combine(outputDir, "drawing_types.json");
            var packPath = Path.Combine(outputDir, "view_style_packs.json");
            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore };
            OutputLocationHelper.WriteAllTextAtomic(dtPath,   JsonConvert.SerializeObject(projectDt,    settings));
            OutputLocationHelper.WriteAllTextAtomic(packPath, JsonConvert.SerializeObject(projectPacks, settings));
            StingLog.Info($"DrawingTypeExcel: wrote {dtPath}");
            StingLog.Info($"DrawingTypeExcel: wrote {packPath}");

            try { DrawingTypeRegistry.Reload(doc); }       catch (Exception ex) { StingLog.Warn($"DrawingTypeRegistry.Reload failed: {ex.Message}"); }
            try { ViewStylePackRegistry.Reload(doc); }     catch (Exception ex) { StingLog.Warn($"ViewStylePackRegistry.Reload failed: {ex.Message}"); }
        }

        // ──────────────────────────────────────────────────────────────────
        //  Helpers used by both export + import
        // ──────────────────────────────────────────────────────────────────

        public static StylePackDoc LoadStylePackDocFromCorporate()
        {
            try
            {
                var path = StingToolsApp.FindDataFile("STING_VIEW_STYLE_PACKS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new StylePackDoc();
                return JsonConvert.DeserializeObject<StylePackDoc>(File.ReadAllText(path)) ?? new StylePackDoc();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawingTypeExcel: corporate style pack load failed — {ex.Message}");
                return new StylePackDoc();
            }
        }

        public static StylePackDoc LoadStylePackDocFromProject(Document doc)
            => LoadStylePackDocFromProject(doc, out _);

        /// <summary>
        /// The project's view_style_packs.json, or null when there is none.
        /// DTW-187: <paramref name="error"/> is set when the file EXISTS but
        /// cannot be read — the caller must not write the file in that case,
        /// or it replaces packs it never saw.
        /// </summary>
        public static StylePackDoc LoadStylePackDocFromProject(Document doc, out string error)
        {
            error = null;
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                var path = StingPaths.MetaFile(doc, "_BIM_COORD", "view_style_packs.json");
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                var parsed = JsonConvert.DeserializeObject<StylePackDoc>(json);
                if (parsed == null && !string.IsNullOrWhiteSpace(json))
                    error = "view_style_packs.json deserialised to nothing";
                return parsed;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DrawingTypeExcel: project style pack load failed — {ex.Message}");
                error = ex.Message;
                return null;
            }
        }

    }

    // ════════════════════════════════════════════════════════════════════════════
    //  DrawingTypeExportExcelCommand — export everything to a .xlsx workbook
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingTypeExportExcelCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => Run(commandData.SafeApp(), ref message);

        public Result Execute(UIApplication app) { string m = ""; return Run(app, ref m); }

        private static Result Run(UIApplication app, ref string message)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                var dtLib = DrawingTypeRegistry.GetLibrary(doc) ?? new DrawingTypeLibrary();

                var corpPacks = DrawingTypeExcelEngine.LoadStylePackDocFromCorporate();
                var projPacks = DrawingTypeExcelEngine.LoadStylePackDocFromProject(doc);
                var packLib   = DrawingTypeExcelEngine.MergeStylePacks(corpPacks, projPacks);

                using var stream = DrawingTypeExcelEngine.ExportWorkbook(dtLib, packLib);
                var ts   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var path = OutputLocationHelper.GetRoutedPath(doc, "Excel", $"DrawingTypes_Export_{ts}.xlsx");
                File.WriteAllBytes(path, stream.ToArray());

                StingLog.Info($"DrawingTypeExcel: exported {dtLib.DrawingTypes?.Count ?? 0} drawing types and {packLib.StylePacks?.Count ?? 0} style packs to {path}");

                var dlg = new TaskDialog("STING — Drawing Types Excel Export")
                {
                    MainInstruction = "Export complete",
                    MainContent =
                        $"Exported {dtLib.DrawingTypes?.Count ?? 0} drawing types and " +
                        $"{packLib.StylePacks?.Count ?? 0} style packs.\n\n" +
                        $"File: {path}\n\n" +
                        "Sheets: DrawingTypes · StylePacks · VgOverrides · FilterRules · Slots · " +
                        "TitleBlockParams · Routing · _Legend (hidden)\n\n" +
                        "Locked columns (grey fill): id, origin, checksum.\n" +
                        "Colour columns render the hex value as a cell fill swatch.",
                };
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Open in Excel");
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Open file location");
                dlg.CommonButtons = TaskDialogCommonButtons.Close;
                var r = dlg.Show();
                if (r == TaskDialogResult.CommandLink1) TryOpen(path);
                else if (r == TaskDialogResult.CommandLink2) TryOpen(Path.GetDirectoryName(path));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypeExportExcel failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING — Drawing Types Excel Export", $"Export failed:\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static void TryOpen(string path)
        {
            try { if (!string.IsNullOrEmpty(path)) Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) { StingLog.Warn($"DrawingTypeExcel: open failed: {ex.Message}"); }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  DrawingTypeImportExcelCommand — import edited .xlsx with validation
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingTypeImportExcelCommand : IExternalCommand, IPanelCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => Run(commandData.SafeApp(), ref message);

        public Result Execute(UIApplication app) { string m = ""; return Run(app, ref m); }

        private static Result Run(UIApplication app, ref string message)
        {
            try
            {
                var doc = app?.ActiveUIDocument?.Document;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                // DTW-189: project overrides live beside the .rvt — refuse an
                // unsaved model up front, as the Drawing Type editor does.
                if (string.IsNullOrEmpty(doc.PathName))
                {
                    TaskDialog.Show("STING — Drawing Types Excel Import",
                        "Save the Revit project first — project overrides live under the .rvt directory, " +
                        "and an import into an unsaved model would write files the registry never reads.");
                    return Result.Cancelled;
                }

                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title       = "Import Drawing Types from Excel",
                    Filter      = "Excel workbook (*.xlsx)|*.xlsx",
                    InitialDirectory = OutputLocationHelper.GetRoutedDirectory(doc, "Excel"),
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;
                var path = dlg.FileName;

                using var wb = new XLWorkbook(path);

                // DTW-187: an override that exists but cannot be read must not
                // be overwritten by an import that never saw its contents.
                var dtError = DrawingTypeRegistry.ProjectOverrideLoadError(doc);
                var projPacks = DrawingTypeExcelEngine.LoadStylePackDocFromProject(doc, out var packError);
                if (dtError != null || packError != null)
                {
                    TaskDialog.Show("STING — Drawing Types Excel Import",
                        "Nothing was imported.\n\nThe project's existing override could not be read:\n"
                        + (dtError != null ? "• drawing_types.json: " + dtError + "\n" : "")
                        + (packError != null ? "• view_style_packs.json: " + packError + "\n" : "")
                        + "\nImporting would overwrite it with only what the workbook holds. Repair or move the file, "
                        + "press Reload JSON (DOCS tab), and import again.");
                    return Result.Cancelled;
                }

                var corpPacks = DrawingTypeExcelEngine.LoadStylePackDocFromCorporate();
                var existingPacks = DrawingTypeExcelEngine.MergeStylePacks(corpPacks, projPacks);
                var existingDt = DrawingTypeRegistry.GetLibrary(doc) ?? new DrawingTypeLibrary();

                var problems = DrawingTypeExcelEngine.ValidateImport(wb, existingDt, existingPacks);
                int errCount  = problems.Count(p => p.Severity == ImportSeverity.Error);
                int warnCount = problems.Count(p => p.Severity == ImportSeverity.Warning);

                if (errCount > 0)
                {
                    var b = StingResultPanel.Create("Drawing Types Import — Validation Errors")
                        .SetSubtitle($"{errCount} error(s) and {warnCount} warning(s) — import blocked.")
                        .AddSection("Errors");
                    foreach (var p in problems.Where(x => x.Severity == ImportSeverity.Error).Take(200))
                        b.Text(p.ToString());
                    if (warnCount > 0)
                    {
                        b.AddSection("Warnings");
                        foreach (var p in problems.Where(x => x.Severity == ImportSeverity.Warning).Take(200))
                            b.Text(p.ToString());
                    }
                    b.Show();
                    return Result.Cancelled;
                }

                if (warnCount > 0)
                {
                    var lines = string.Join("\n", problems.Where(p => p.Severity == ImportSeverity.Warning).Take(20).Select(p => "• " + p.Message));
                    var td = new TaskDialog("STING — Import Warnings")
                    {
                        MainInstruction = $"{warnCount} warning(s)",
                        MainContent = lines + (warnCount > 20 ? $"\n…and {warnCount - 20} more." : "") + "\n\nProceed anyway?",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.Yes,
                    };
                    if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                }

                var imp = DrawingTypeExcelEngine.ImportWorkbook(wb, existingDt, existingPacks);

                var summary = StingResultPanel.Create("Drawing Types Import — Change Summary")
                    .SetSubtitle($"{imp.Changes.Count} change(s) detected. Confirm to write to _BIM_COORD/.");
                foreach (var grp in imp.Changes.GroupBy(c => c.EntityType).OrderBy(g => g.Key))
                {
                    summary.AddSection($"{grp.Key} ({grp.Count()})");
                    foreach (var c in grp.Take(80)) summary.Text(c.ToString());
                    if (grp.Count() > 80) summary.Text($"… and {grp.Count() - 80} more.");
                }
                summary.Show();

                var confirm = new TaskDialog("STING — Apply Import")
                {
                    MainInstruction = "Apply changes?",
                    MainContent = $"Write {imp.Changes.Count} change(s) to _BIM_COORD/drawing_types.json and view_style_packs.json?\n\n" +
                                  "Corporate baseline files on disk are not touched. Modified corporate entries flip to project origin.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No,
                };
                if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                var outDir = ResolveProjectOverrideDir(doc);
                using (var tg = new Transaction(doc, "STING Import Drawing Types"))
                {
                    tg.Start();
                    DrawingTypeExcelEngine.ApplyImport(doc, imp.UpdatedDtLib, imp.UpdatedPackLib, outDir);
                    tg.Commit();
                }

                StingLog.Info($"DrawingTypeExcel: applied {imp.Changes.Count} changes from {path}");

                TaskDialog.Show("STING — Import Complete",
                    $"Wrote project overrides to:\n{outDir}\n\nApplied {imp.Changes.Count} change(s). Registry caches refreshed.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingTypeImportExcel failed", ex);
                message = ex.Message;
                TaskDialog.Show("STING — Drawing Types Excel Import", $"Import failed:\n{ex.Message}");
                return Result.Failed;
            }
        }

        private static string ResolveProjectOverrideDir(Document doc)
        {
            // DTW-189: no exports-folder fallback. The registries read the
            // override only from the project's _BIM_COORD bucket, so writing
            // it anywhere else reported "imported" for files nothing reads.
            // Run() refuses an unsaved model before getting here.
            if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
            var d = StingPaths.Meta(doc, "_BIM_COORD");
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
