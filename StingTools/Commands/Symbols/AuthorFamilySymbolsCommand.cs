// StingTools — AuthorFamilySymbolsCommand.cs (Phase 175)
//
// Dedicated IExternalCommand that drives FamilySymbolAuthor.AuthorSymbols on
// Revit model families.  Two entry points:
//
//   1. Selected family instances in the active view → EditFamily → author → reload
//   2. File/folder picker → open .rfa, author, save (optionally reload into project)
//
// The second path delegates to FamilyParamEngine.ProcessFamily so the same
// reliable open/save/reload loop is reused.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Commands.Symbols
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class AuthorFamilySymbolsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) return Result.Failed;

            var modeItems = new List<StingTools.UI.StingListPicker.ListItem>
            {
                new StingTools.UI.StingListPicker.ListItem
                {
                    Label  = "Selected family instances (in active view)",
                    Detail = "Author symbols into the families of selected instances, then reload into the active project.",
                    Tag    = "selection"
                },
                new StingTools.UI.StingListPicker.ListItem
                {
                    Label  = "Pick one or more .rfa files",
                    Detail = "File-picker: select .rfa file(s), author symbols, save in-place. Optionally reload into the active project.",
                    Tag    = "file_single"
                },
                new StingTools.UI.StingListPicker.ListItem
                {
                    Label  = "Pick a folder of .rfa files",
                    Detail = "Process every .rfa in the selected folder. Optionally reload all into the active project.",
                    Tag    = "file_batch"
                },
            };

            var picked = StingTools.UI.StingListPicker.Show(
                "STING — Author Family Symbols",
                "Wire view-type-aware symbolic geometry (plan bounding box, elevation outline, " +
                "LOD visibility), STING subcategories, annotation plan symbols, and MEP connector " +
                "size parameters into Revit model family (.rfa) files.",
                modeItems);

            if (picked == null || picked.Count == 0) return Result.Cancelled;
            string mode = picked[0].Tag as string ?? "selection";

            if (mode == "selection")
                return AuthorFromSelection(ctx);

            return AuthorFromFiles(ctx, isBatch: mode == "file_batch");
        }

        // ── Mode 1: selected family instances ────────────────────────────────

        private static Result AuthorFromSelection(ParameterHelpers.StingCommandContext ctx)
        {
            // Collect unique Family objects from the selection
            var families = ctx.UIDoc.Selection
                .GetElementIds()
                .Select(id => ctx.Doc.GetElement(id))
                .OfType<FamilyInstance>()
                .Select(fi => fi.Symbol?.Family)
                .Where(f => f != null)
                .GroupBy(f => f.Id.Value)
                .Select(g => g.First())
                .ToList();

            if (families.Count == 0)
            {
                TaskDialog.Show("STING", "Select at least one family instance.");
                return Result.Cancelled;
            }

            var symOpts = new FamilySymbolAuthorOptions();
            var results = new List<(string Name, FamilySymbolAuthorResult SymResult, string Error)>();

            var progress = StingTools.UI.StingProgressDialog.Show(
                "STING — Author Family Symbols", families.Count);
            try
            {
                int n = 0;
                foreach (var fam in families)
                {
                    if (EscapeChecker.IsEscapePressed()) break;
                    progress.Increment($"{fam.Name} ({++n}/{families.Count})");

                    Document famDoc = null;
                    try
                    {
                        famDoc = ctx.Doc.EditFamily(fam);
                        if (famDoc == null || !famDoc.IsFamilyDocument)
                        {
                            results.Add((fam.Name, null, "EditFamily returned null or non-family document"));
                            continue;
                        }

                        using (var tx = new Transaction(famDoc, "STING Author Symbols"))
                        {
                            tx.Start();

                            // Ensure LOD visibility params exist before authoring symbols
                            FamilyParamEngine.InjectAutomationPresentationPack(famDoc);

                            var saResult = FamilySymbolAuthor.AuthorSymbols(famDoc, symOpts);
                            results.Add((fam.Name, saResult, null));
                            if (saResult.Warnings.Count > 0)
                                StingLog.Warn($"AuthorSymbols '{fam.Name}': {string.Join("; ", saResult.Warnings)}");

                            tx.Commit();
                        }

                        // Reload the modified family back into the project.
                        // famDoc.LoadFamily(projectDoc, opts) is the family-doc-side overload —
                        // it loads the current in-memory family into the supplied project document.
                        // TODO-VERIFY-API: Document.LoadFamily(Document, IFamilyLoadOptions) overload
                        // confirmed in Revit 2024/2025/2026/2027 when called on a family document.
                        try
                        {
                            famDoc.LoadFamily(ctx.Doc, new SymbolReloadOptions());
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"AuthorFamilySymbols reload '{fam.Name}': {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.Add((fam.Name, null, ex.Message));
                        StingLog.Error($"AuthorFamilySymbols '{fam.Name}'", ex);
                    }
                    finally
                    {
                        try { famDoc?.Close(false); } catch { }
                    }
                }
            }
            finally { progress.Close(); }

            ShowSelectionReport(results);
            return Result.Succeeded;
        }

        private static void ShowSelectionReport(
            IReadOnlyList<(string Name, FamilySymbolAuthorResult SymResult, string Error)> results)
        {
            var sb = new StringBuilder();
            int ok  = results.Count(r => r.Error == null);
            int err = results.Count(r => r.Error != null);
            sb.AppendLine($"Families processed: {results.Count} — {ok} succeeded, {err} failed");
            sb.AppendLine();

            foreach (var (name, res, error) in results)
            {
                if (error != null)
                {
                    sb.AppendLine($"FAILED  {name}: {error}");
                }
                else
                {
                    sb.Append($"OK      {name}");
                    if (res != null)
                    {
                        sb.Append($"  subcats:{res.SubcategoriesCreated}");
                        int curves = res.PlanCurvesCreated + res.ElevCurvesCreated +
                                     res.ClearanceCurvesCreated + res.AnnotationSymbolCurvesCreated;
                        sb.Append($"  curves:{curves}");
                        if (res.StandardParamsCreated > 0)
                            sb.Append($"  std-params:{res.StandardParamsCreated}");
                        if (res.ConnectorParamsCreated > 0)
                            sb.Append($"  conn-params:{res.ConnectorParamsCreated}");
                    }
                    sb.AppendLine();
                }
            }

            TaskDialog.Show("STING — Author Family Symbols", sb.ToString());
        }

        // ── Mode 2: file / folder picker ──────────────────────────────────────

        private static Result AuthorFromFiles(
            ParameterHelpers.StingCommandContext ctx, bool isBatch)
        {
            List<string> rfaFiles;

            if (isBatch)
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title     = "Select any .rfa file in the target folder (all .rfa will be processed)",
                    Filter    = "Revit Family Files (*.rfa)|*.rfa",
                    Multiselect = false,
                    InitialDirectory = StingToolsApp.DataPath ?? "",
                };
                if (ofd.ShowDialog() != true) return Result.Cancelled;
                string dir = Path.GetDirectoryName(ofd.FileName) ?? "";
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    TaskDialog.Show("STING", "Invalid folder selected.");
                    return Result.Cancelled;
                }
                rfaFiles = Directory.GetFiles(dir, "*.rfa", SearchOption.AllDirectories).ToList();
            }
            else
            {
                var ofd = new Microsoft.Win32.OpenFileDialog
                {
                    Title     = "Select .rfa file(s) to author symbols into",
                    Filter    = "Revit Family Files (*.rfa)|*.rfa",
                    Multiselect = true,
                    InitialDirectory = StingToolsApp.DataPath ?? "",
                };
                if (ofd.ShowDialog() != true) return Result.Cancelled;
                rfaFiles = ofd.FileNames.ToList();
            }

            if (rfaFiles.Count == 0)
            {
                TaskDialog.Show("STING", "No .rfa files found.");
                return Result.Cancelled;
            }

            // Offer to reload into the active project
            bool loadAfterSave = false;
            if (ctx.Doc != null && !ctx.Doc.IsFamilyDocument)
            {
                var td = new TaskDialog("STING — Reload into project?")
                {
                    MainInstruction = "Reload processed families into the active project?",
                    MainContent     =
                        $"After each .rfa is saved, reload it into '{ctx.Doc.Title}'. " +
                        "Existing placed instances keep their parameter values.",
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton   = TaskDialogResult.No,
                };
                loadAfterSave = td.Show() == TaskDialogResult.Yes;
            }

            var opts = new FamilyParamEngine.ProcessOptions
            {
                InjectAutomationPack = true,   // ensures STING_LOD_*_VISIBLE params exist
                InjectSymbolGeometry = true,
                SymbolAuthorOpts     = new FamilySymbolAuthorOptions(),
                LoadAfterSave        = loadAfterSave,
                TargetProjectDoc     = loadAfterSave ? ctx.Doc : null,
            };

            var fileResults = new List<FamilyParamEngine.FamilyResult>();
            var progress = StingTools.UI.StingProgressDialog.Show(
                "STING — Author Family Symbols", rfaFiles.Count);
            try
            {
                int n = 0;
                var app = ctx.App.Application;
                foreach (string path in rfaFiles)
                {
                    if (EscapeChecker.IsEscapePressed()) break;
                    progress.Increment($"{Path.GetFileName(path)} ({++n}/{rfaFiles.Count})");
                    fileResults.Add(FamilyParamEngine.ProcessFamily(app, path, path, opts));
                }
            }
            finally { progress.Close(); }

            ShowFilesReport(fileResults, ctx.Doc?.Title ?? "");
            return Result.Succeeded;
        }

        private static void ShowFilesReport(
            IReadOnlyList<FamilyParamEngine.FamilyResult> results,
            string projectTitle)
        {
            int ok  = results.Count(r => r.Success);
            int err = results.Count(r => !r.Success);
            int totalCurves  = results.Sum(r => r.SymbolCurvesCreated);
            int totalConn    = results.Sum(r => r.ConnectorParamsCreated);
            int totalStdPar  = results.Sum(r => r.StandardParamsCreated);
            int totalLoaded  = results.Count(r => r.LoadedIntoProject);

            var sb = new StringBuilder();
            sb.AppendLine($"Files processed: {results.Count} — {ok} succeeded, {err} failed");
            if (totalCurves > 0)  sb.AppendLine($"Symbol curves created: {totalCurves}");
            if (totalStdPar > 0)  sb.AppendLine($"Standard-switching params created: {totalStdPar} (IEC/ANSI/BS/NFPA/CIBSE)");
            if (totalConn > 0)    sb.AppendLine($"Connector params created: {totalConn}");
            if (totalLoaded > 0 && !string.IsNullOrEmpty(projectTitle))
                sb.AppendLine($"Loaded into '{projectTitle}': {totalLoaded}/{ok}");

            if (err > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Failed:");
                foreach (var r in results.Where(r => !r.Success).Take(10))
                    sb.AppendLine($"  {Path.GetFileName(r.SourcePath)}: {r.ErrorMessage}");
            }

            TaskDialog.Show("STING — Author Family Symbols", sb.ToString());
        }

        // ── Private IFamilyLoadOptions: overwrite definition, preserve instance values ──

        private sealed class SymbolReloadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = false; // preserve values on placed instances
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}
