using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Tags
{
    // ════════════════════════════════════════════════════════════════════════════
    //  CONFIGURE LOADED FAMILIES COMMAND
    //
    //  Walks every editable family loaded in the active project and runs the
    //  FamilyParamEngine injection pipeline on each in-memory (via EditFamily +
    //  ProcessFamilyDocument + LoadFamily). Used by the project-retrofit flow to
    //  adopt a non-STING project without re-authoring each .rfa on disk.
    //
    //  Scope modes:
    //    Mapped   → only families whose category is on TagFamilyConfig.CategoryTemplateMap
    //               (the ~50 categories STING actually tags). Default.
    //    All      → every editable family regardless of category. Opt-in.
    //
    //  Skip rules (applied even in All mode):
    //    • fam.IsEditable == false          (in-place / system families)
    //    • fam.IsInPlace                    (can't be loaded back via LoadFamily)
    //    • worksharing: element not owned   (avoid mid-batch checkout failures)
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inject STING shared parameters into every loaded family in the active project.
    /// Each family is opened in-memory via <see cref="Document.EditFamily(Family)"/>,
    /// processed through <see cref="FamilyParamEngine.ProcessFamilyDocument"/>, then
    /// re-loaded into the project.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConfigureLoadedFamiliesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx?.Doc == null || ctx.Doc.IsFamilyDocument)
            {
                TaskDialog.Show("STING — Configure Loaded Families",
                    "Open a Revit project (.rvt) first. This command runs against loaded " +
                    "families in the active project and cannot run inside the Family Editor.");
                return Result.Failed;
            }

            var app = ctx.App.Application;
            var doc = ctx.Doc;

            // Collect every non-system, editable family in the project.
            var allFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f != null && f.IsEditable && !f.IsInPlace)
                .OrderBy(f => f.Name)
                .ToList();

            if (allFamilies.Count == 0)
            {
                TaskDialog.Show("STING — Configure Loaded Families",
                    "No editable loaded families found in the active project.");
                return Result.Succeeded;
            }

            // Build the "STING tags this category" set from the tag-family mapping.
            var stingCatIds = new HashSet<long>(
                TagFamilyConfig.CategoryTemplateMap.Keys.Select(bic => (long)bic));

            var mapped = allFamilies
                .Where(f => f.FamilyCategory != null && stingCatIds.Contains(f.FamilyCategory.Id.Value))
                .ToList();
            int unmappedCount = allFamilies.Count - mapped.Count;

            // Scope choice.
            var scopeDlg = new TaskDialog("STING — Configure Loaded Families");
            scopeDlg.MainInstruction = $"Scope: which families to configure?";
            scopeDlg.MainContent =
                $"Mapped to STING tag categories: {mapped.Count}\n" +
                $"Other editable families: {unmappedCount}\n" +
                $"Total: {allFamilies.Count}\n\n" +
                "Mapped scope is recommended — it matches the categories STING actually tags " +
                "(doors, windows, MEP equipment, fixtures, etc.). Use 'All editable' only when " +
                "custom categories are wired into project_config.json.";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                $"Mapped only — {mapped.Count} families",
                "Scopes to families whose category is on the STING tag-family mapping list.");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                $"All editable — {allFamilies.Count} families",
                "Every editable family, including furniture, entourage, site, detail components, etc.");
            scopeDlg.CommonButtons = TaskDialogCommonButtons.Cancel;
            var scopeRes = scopeDlg.Show();
            if (scopeRes == TaskDialogResult.Cancel) return Result.Cancelled;
            var workingSet = (scopeRes == TaskDialogResult.CommandLink2) ? allFamilies : mapped;
            if (workingSet.Count == 0)
            {
                TaskDialog.Show("STING — Configure Loaded Families",
                    "Working set is empty under the chosen scope.");
                return Result.Succeeded;
            }

            // Purge-mode choice — mirrors the FamilyParamCreator UI so users learn one pattern.
            var purgeItems = new List<StingListPicker.ListItem>
            {
                new StingListPicker.ListItem
                    { Label = "Add missing params only (recommended)", Detail = "Pure additive. Leaves existing shared params in place.", Tag = PurgeMode.None },
                new StingListPicker.ListItem
                    { Label = "Clean non-STING params + inject", Detail = "Remove shared params whose GUID is not in the STING registry before injecting. Use when adopting third-party families.", Tag = PurgeMode.NonSting },
                new StingListPicker.ListItem
                    { Label = "Purge STING + reinject (destructive)", Detail = "Remove every STING-registered shared param then re-inject. Use only for schema migrations.", Tag = PurgeMode.StingOnly },
            };
            var purgePicked = StingListPicker.Show(
                "STING — Purge mode",
                $"Pre-injection purge behaviour for {workingSet.Count} families",
                purgeItems);
            if (purgePicked == null || purgePicked.Count == 0) return Result.Cancelled;
            var purgeMode = (PurgeMode)purgePicked[0].Tag;

            // Final confirmation + dry-run summary.
            var confirm = new TaskDialog("STING — Configure Loaded Families");
            confirm.MainInstruction = $"Process {workingSet.Count} loaded families?";
            confirm.MainContent =
                $"Project: {doc.Title}\n" +
                $"Scope: {(workingSet == mapped ? "Mapped to STING tag categories" : "All editable")}\n" +
                $"Purge: {purgeMode}\n\n" +
                "Each family will be opened in-memory, have STING shared parameters injected, " +
                "and be loaded back into this project. Instance parameter values on already-placed " +
                "instances are preserved.\n\n" +
                "Cancellation during the run is supported (Escape) but any families already " +
                "processed will remain updated.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            var results = new List<FamilyParamEngine.FamilyResult>();
            var skipLog = new List<string>();
            int cancelled = 0;
            int processed = 0;

            var progress = StingProgressDialog.Show("STING — Configure Loaded Families", workingSet.Count);
            try
            {
                foreach (var fam in workingSet)
                {
                    if (EscapeChecker.IsEscapePressed())
                    {
                        cancelled = workingSet.Count - processed;
                        break;
                    }

                    progress.Increment($"{fam.Name} ({processed + 1}/{workingSet.Count})");
                    processed++;

                    Document famDoc = null;
                    try
                    {
                        famDoc = doc.EditFamily(fam);
                    }
                    catch (Exception editEx)
                    {
                        skipLog.Add($"{fam.Name}: EditFamily failed — {editEx.Message}");
                        StingLog.Warn($"EditFamily '{fam.Name}': {editEx.Message}");
                        continue;
                    }

                    if (famDoc == null)
                    {
                        skipLog.Add($"{fam.Name}: EditFamily returned null");
                        continue;
                    }

                    try
                    {
                        var opts = new FamilyParamEngine.ProcessOptions
                        {
                            Purge = purgeMode,
                            LoadAfterSave = true,
                            TargetProjectDoc = doc,
                            LoadOverwriteParameterValues = false,
                        };
                        var result = FamilyParamEngine.ProcessFamilyDocument(app, famDoc, fam.Name, opts);
                        results.Add(result);
                    }
                    finally
                    {
                        try { famDoc?.Close(false); }
                        catch (Exception closeEx) { StingLog.Warn($"Close famDoc '{fam.Name}': {closeEx.Message}"); }
                    }
                }
            }
            finally
            {
                progress.Close();
            }

            // Report.
            int succeeded = results.Count(r => r.Success);
            int failed    = results.Count(r => !r.Success);
            int loaded    = results.Count(r => r.LoadedIntoProject);
            int paramsAdded  = results.Sum(r => r.ParamsAdded);
            int paramsPurged = results.Sum(r => r.ParamsPurged);

            var report = new StringBuilder();
            report.AppendLine($"Families processed: {processed} / {workingSet.Count}");
            if (cancelled > 0) report.AppendLine($"  (cancelled — {cancelled} remaining)");
            report.AppendLine($"  succeeded: {succeeded}");
            report.AppendLine($"  failed:    {failed}");
            report.AppendLine($"  skipped:   {skipLog.Count}");
            report.AppendLine();
            report.AppendLine($"Reloaded into project: {loaded}");
            report.AppendLine($"Parameters added:      {paramsAdded}");
            if (paramsPurged > 0)
                report.AppendLine($"Parameters purged:     {paramsPurged} (mode: {purgeMode})");

            // CSV log beside the project file (fall back to temp).
            try
            {
                string logDir = !string.IsNullOrEmpty(doc.PathName)
                    ? Path.GetDirectoryName(doc.PathName)
                    : Path.GetTempPath();
                string logPath = Path.Combine(logDir,
                    $"STING_ConfigureLoadedFamilies_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var csv = new StringBuilder();
                csv.AppendLine("Family,Category,DiscCode,ParamsAdded,ParamsPurged,LoadedIntoProject,Status,Error");
                foreach (var r in results)
                {
                    csv.AppendLine($"\"{r.SourcePath}\",\"{r.Category}\",{r.DiscCode}," +
                        $"{r.ParamsAdded},{r.ParamsPurged},{r.LoadedIntoProject}," +
                        $"{(r.Success ? "OK" : "FAILED")},\"{r.ErrorMessage ?? ""}\"");
                }
                foreach (string s in skipLog) csv.AppendLine($"\"{s}\",,,,,,SKIPPED,");
                File.WriteAllText(logPath, csv.ToString());
                report.AppendLine();
                report.AppendLine($"Log: {logPath}");
            }
            catch (Exception logEx)
            {
                StingLog.Warn($"ConfigureLoadedFamilies CSV log: {logEx.Message}");
            }

            TaskDialog.Show("STING — Configure Loaded Families", report.ToString());
            StingLog.Info($"ConfigureLoadedFamilies: {succeeded}/{processed} succeeded, {paramsAdded} params added, {loaded} loaded into project");
            return Result.Succeeded;
        }
    }
}
