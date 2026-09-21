// ============================================================================
// FixTagFamilyParamTypesCommand.cs — make a tag family's shared parameters
// agree with MR_PARAMETERS.txt, so it can be loaded at all.
//
// WHY THIS EXISTS
//
// Loading "STING - Duct Tag" into a project that already holds the universal
// master produces 17 errors, every one the same shape (observed 2026-09-21):
//
//     The shared parameter "39e255ff-…" cannot be added with name
//     "HVC_DCT_FLW_CFM" and type "Text" because it conflicts with the existing
//     name "HVC_DCT_FLW_CFM" and type "Number".
//
// The family offers Text; the project holds the type MR_PARAMETERS.txt
// declares. Revit keys a shared parameter on its GUID and refuses rather than
// pick one, so the load stops dead.
//
// This is not one family's problem. The conformance audit's check (10) measured
// it across the deployed library on 2026-09-18: 152 distinct parameters
// wrong-typed across 180 of 206 families — ASS_CST_UNIT_PRICE_UGX_NR as Text in
// 189 families where the declaration says Currency, ASS_ELEVATION_M as Text in
// 188 where it says Length, and so on. Correcting the master by hand fixed one
// file out of 189.
//
// WHAT THIS DOES
//
// Opens each .rfa standalone and, for every shared parameter whose type
// disagrees with MR_PARAMETERS.txt, calls
//
//     FamilyManager.ReplaceParameter(old, mrDefinition, group, isInstance)
//
// which re-points the parameter at the declared definition and carries label
// cells, formulas and type values across with it. Revit refuses the call when
// the parameter is used somewhere the new type cannot go — that refusal is the
// safety net, and it is reported per family rather than worked around.
//
// WHY IT IS SAFE TO RUN ON THE TAG LIBRARY
//
// The 206 tag families carry no label rows at all until they are propagated to
// (confirmed in Revit, 2026-09-21), so in the common case nothing references
// these parameters and the replacement is mechanical. The master is the
// exception — it carries the hand-built 65-row label — which is why AUDIT is the
// default, APPLY asks which files, and every file is copied first.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Select;
using StingTools.Tags;
using StingTools.UI;

namespace StingTools.Commands.TagStudio
{
    /// <summary>One family's parameter-type audit, and what was done about it.</summary>
    public class ParamTypeFixRow
    {
        public string FamilyName { get; set; } = "";
        /// <summary>OK / FIX / PARTIAL / REFUSED / ERROR.</summary>
        public string Verdict { get; set; } = "";
        public int Disagreeing { get; set; }
        public int Replaced { get; set; }
        public int Refused { get; set; }
        public string Detail { get; set; } = "";
    }

    /// <summary>
    /// Re-points wrong-typed shared parameters at their MR_PARAMETERS.txt
    /// definitions, in the .rfa, so the family can be loaded into a project that
    /// holds the declared types.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class FixTagFamilyParamTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp;
            try { uiApp = ParameterHelpers.GetApp(commandData); }
            catch (Exception ex)
            {
                StingLog.Warn($"FixTagFamilyParamTypes: no UIApplication: {ex.Message}");
                TaskDialog.Show("Fix Tag Family Parameter Types",
                    "Could not reach Revit. Run this from the STING dock panel or the ribbon.");
                return Result.Failed;
            }
            var app = uiApp.Application;
            Document projectDoc = null;
            try { projectDoc = uiApp.ActiveUIDocument?.Document; } catch { }

            // ── The declaration. Without it there is nothing to compare against,
            //    and a run that cannot compare must not report families as clean.
            string mrPath = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (string.IsNullOrEmpty(mrPath) || !File.Exists(mrPath))
            {
                TaskDialog.Show("Fix Tag Family Parameter Types",
                    "MR_PARAMETERS.txt not found in the data directory, so there is nothing to " +
                    "check the families against. Nothing was examined.");
                StingLog.Warn("FixTagFamilyParamTypes: MR_PARAMETERS.txt not found - aborted");
                return Result.Failed;
            }

            string folder = PickFolder();
            if (string.IsNullOrEmpty(folder))
            {
                StingLog.Info("FixTagFamilyParamTypes: no folder chosen - nothing done");
                return Result.Cancelled;
            }

            var rfas = Directory.EnumerateFiles(folder, "*.rfa", SearchOption.TopDirectoryOnly)
                .Where(p => !Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal))
                .Where(p => !FixTagFamilyCategoriesCommand.IsRevitBackup(p))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (rfas.Count == 0)
            {
                TaskDialog.Show("Fix Tag Family Parameter Types", $"No .rfa files directly under:\n{folder}");
                return Result.Cancelled;
            }

            var mode = ChooseMode(rfas.Count);
            if (mode == RunMode.Cancel)
            {
                StingLog.Info("FixTagFamilyParamTypes: mode dialog cancelled - nothing done");
                return Result.Cancelled;
            }

            if (mode == RunMode.Apply)
            {
                rfas = ChooseFiles(rfas);
                if (rfas == null || rfas.Count == 0)
                {
                    StingLog.Info("FixTagFamilyParamTypes: no files chosen - nothing done");
                    return Result.Cancelled;
                }
                var confirm = new TaskDialog("Fix Tag Family Parameter Types");
                confirm.MainInstruction = $"Re-point wrong-typed parameters in {rfas.Count} .rfa file(s)?";
                confirm.MainContent =
                    "Each parameter whose type disagrees with MR_PARAMETERS.txt is replaced by the " +
                    "declared definition, which carries label cells, formulas and type values with " +
                    "it.\n\n" +
                    "A copy of every file is taken first, into a _preparamtypes folder beside them.\n\n" +
                    "Revit refuses the replacement where a parameter is used somewhere the declared " +
                    "type cannot go. Those are reported per family, not worked around.\n\n" +
                    "This does NOT change any project.";
                confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
                confirm.DefaultButton = TaskDialogResult.Cancel;
                if (confirm.Show() != TaskDialogResult.Ok)
                {
                    StingLog.Info("FixTagFamilyParamTypes: apply declined - nothing done");
                    return Result.Cancelled;
                }
            }

            string backupDir = null;
            if (mode == RunMode.Apply)
            {
                backupDir = Path.Combine(folder, "_preparamtypes_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                try
                {
                    Directory.CreateDirectory(backupDir);
                    foreach (string src in rfas)
                        File.Copy(src, Path.Combine(backupDir, Path.GetFileName(src)), overwrite: false);
                    StingLog.Info($"FixTagFamilyParamTypes: {rfas.Count} file(s) copied to {backupDir}");
                }
                catch (Exception ex)
                {
                    StingLog.Error("FixTagFamilyParamTypes: pre-change copy failed", ex);
                    TaskDialog.Show("Fix Tag Family Parameter Types",
                        $"Could not copy the files to a backup folder, so nothing was changed.\n\n{ex.Message}");
                    return Result.Failed;
                }
            }

            // ── Read the declaration once, with the shared-parameter file forced to
            //    MR so the definitions carry MR's types whatever the operator has selected.
            var declared = new List<SharedParamFacts>();
            var defByName = new Dictionary<string, ExternalDefinition>(StringComparer.OrdinalIgnoreCase);
            string originalSp = null;
            try
            {
                originalSp = app.SharedParametersFilename;
                app.SharedParametersFilename = mrPath;
                var defFile = app.OpenSharedParameterFile();
                declared = SharedParamPreflight.CollectAllDefinitions(defFile);
                if (defFile != null)
                    foreach (DefinitionGroup g in defFile.Groups)
                        foreach (Definition d in g.Definitions)
                            if (d is ExternalDefinition ext && !defByName.ContainsKey(ext.Name))
                                defByName[ext.Name] = ext;
            }
            catch (Exception ex)
            {
                StingLog.Error("FixTagFamilyParamTypes: reading MR_PARAMETERS.txt", ex);
                TaskDialog.Show("Fix Tag Family Parameter Types",
                    $"Could not read MR_PARAMETERS.txt, so nothing was examined.\n\n{ex.Message}");
                try { if (originalSp != null) app.SharedParametersFilename = originalSp; } catch { }
                return Result.Failed;
            }
            StingLog.Info($"FixTagFamilyParamTypes: MR_PARAMETERS.txt declares {declared.Count} parameter(s); " +
                          $"{rfas.Count} file(s) under {folder}; mode={mode}");

            var rows = new List<ParamTypeFixRow>(rfas.Count);
            var progress = rfas.Count > 5 ? StingProgressDialog.Show("Fix Parameter Types", rfas.Count) : null;
            int fixedFiles = 0, cleanFiles = 0, partialFiles = 0, errorFiles = 0, totalReplaced = 0, totalRefused = 0;

            try
            {
                for (int i = 0; i < rfas.Count; i++)
                {
                    string path = rfas[i];
                    if (progress != null)
                    {
                        progress.Increment($"{Path.GetFileNameWithoutExtension(path)} ({i + 1}/{rfas.Count})");
                        if (progress.IsCancelled)
                        {
                            StingLog.Info($"FixTagFamilyParamTypes: cancelled after {i} of {rfas.Count}");
                            break;
                        }
                    }

                    var row = InspectOne(app, path, declared, defByName, mode == RunMode.Apply);
                    rows.Add(row);
                    totalReplaced += row.Replaced;
                    totalRefused += row.Refused;
                    switch (row.Verdict)
                    {
                        case "OK": cleanFiles++; break;
                        case "FIX": fixedFiles++; break;
                        case "PARTIAL": partialFiles++; break;
                        case "ERROR": errorFiles++; break;
                    }
                }
            }
            finally
            {
                progress?.Close();
                try { if (originalSp != null) app.SharedParametersFilename = originalSp; }
                catch (Exception ex) { StingLog.Warn($"FixTagFamilyParamTypes: restore SharedParametersFilename: {ex.Message}"); }
            }

            string xlsx = null;
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(projectDoc);
                xlsx = Path.Combine(outDir, $"STING_TagFamilyParamTypes_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                StingExcelExporter.ExportTable(
                    xlsx, "ParamTypes",
                    new List<string> { "Family", "Verdict", "Disagreeing", "Replaced", "Refused", "Detail" },
                    rows.Select(r => new List<string>
                    {
                        r.FamilyName, r.Verdict, r.Disagreeing.ToString(),
                        r.Replaced.ToString(), r.Refused.ToString(), r.Detail
                    }).ToList(),
                    openFolder: false);
            }
            catch (Exception ex) { StingLog.Warn($"FixTagFamilyParamTypes: Excel export: {ex.Message}"); }

            string verb = mode == RunMode.Apply ? "re-pointed" : "would be re-pointed";
            var td = new TaskDialog("Fix Tag Family Parameter Types — " +
                                    (mode == RunMode.Apply ? "done" : "audit"));
            td.MainInstruction = $"{totalReplaced} parameter(s) {verb} across {fixedFiles + partialFiles} famil" +
                                 (fixedFiles + partialFiles == 1 ? "y" : "ies");
            td.MainContent =
                $"Scanned: {rows.Count}\n" +
                $"Already agree with MR_PARAMETERS.txt: {cleanFiles}\n" +
                (partialFiles > 0 ? $"Partly fixed (Revit refused some): {partialFiles}\n" : "") +
                (totalRefused > 0 ? $"Parameters Revit refused to replace: {totalRefused}\n" : "") +
                (errorFiles > 0 ? $"Errors: {errorFiles}\n" : "") +
                (mode == RunMode.Apply
                    ? $"\nPre-change copies: {backupDir}\n"
                    : "\nNothing was written — this was an audit.\n") +
                (xlsx != null ? $"\nReport: {xlsx}" : "\nNo report was written — see the log.");
            td.Show();

            StingLog.Info($"FixTagFamilyParamTypes: mode={mode}, scanned={rows.Count}, clean={cleanFiles}, " +
                          $"fixed={fixedFiles}, partial={partialFiles}, errors={errorFiles}, " +
                          $"replaced={totalReplaced}, refused={totalRefused}");
            return Result.Succeeded;
        }

        // ──────────────────────────────────────────────────────────────────

        private static ParamTypeFixRow InspectOne(
            Autodesk.Revit.ApplicationServices.Application app, string rfaPath,
            IList<SharedParamFacts> declared, Dictionary<string, ExternalDefinition> defByName, bool apply)
        {
            var row = new ParamTypeFixRow { FamilyName = Path.GetFileNameWithoutExtension(rfaPath) ?? "" };
            Document famDoc = null;
            try
            {
                famDoc = app.OpenDocumentFile(rfaPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    row.Verdict = "ERROR"; row.Detail = "not a family document";
                    return row;
                }

                FamilyManager fm = famDoc.FamilyManager;
                var conflicts = SharedParamConflictDetector.Detect(
                    SharedParamPreflight.CollectFamily(fm), declared)
                    .Where(c => c.Kind == SharedParamConflictKind.TypeMismatch)
                    .ToList();

                row.Disagreeing = conflicts.Count;
                if (conflicts.Count == 0) { row.Verdict = "OK"; return row; }

                if (!apply)
                {
                    row.Verdict = "FIX";
                    row.Detail = string.Join("; ", conflicts.Take(6).Select(c => c.Describe("MR declares"))) +
                                 (conflicts.Count > 6 ? $"; …and {conflicts.Count - 6} more" : "");
                    return row;
                }

                var refused = new List<string>();
                using (var tx = new Transaction(famDoc, "STING Re-point parameter types"))
                {
                    tx.Start();
                    foreach (var c in conflicts)
                    {
                        if (!defByName.TryGetValue(c.FamilyName ?? "", out var ext))
                        {
                            refused.Add($"{c.FamilyName}: not in MR_PARAMETERS.txt by name");
                            continue;
                        }
                        try
                        {
                            // Re-snapshot each time: ReplaceParameter can invalidate the
                            // FamilyParameter objects held from an earlier pass.
                            FamilyParameter fp = fm.GetParameters()
                                .FirstOrDefault(p => string.Equals(p?.Definition?.Name, c.FamilyName, StringComparison.Ordinal));
                            if (fp == null) { refused.Add($"{c.FamilyName}: gone from the family"); continue; }

                            bool isInstance = fp.IsInstance;
                            var group = fp.Definition?.GetGroupTypeId() ?? GroupTypeId.General;
                            fm.ReplaceParameter(fp, ext, group, isInstance);
                            row.Replaced++;
                        }
                        catch (Exception ex)
                        {
                            // Revit refuses where the declared type cannot go - a label cell
                            // or formula that needs Text, most often. Reported, never forced.
                            refused.Add($"{c.FamilyName}: {ex.Message}");
                        }
                    }
                    if (row.Replaced > 0) tx.Commit(); else tx.RollBack();
                }

                row.Refused = refused.Count;
                if (row.Replaced == 0)
                {
                    row.Verdict = "REFUSED";
                    row.Detail = string.Join("; ", refused.Take(4));
                    return row;
                }

                try
                {
                    famDoc.SaveAs(rfaPath, new SaveAsOptions { OverwriteExistingFile = true, MaximumBackups = 1 });
                }
                catch (Exception ex)
                {
                    row.Verdict = "ERROR";
                    row.Detail = $"replaced {row.Replaced} but SAVE FAILED: {ex.Message}";
                    return row;
                }

                row.Verdict = refused.Count == 0 ? "FIX" : "PARTIAL";
                row.Detail = $"re-pointed {row.Replaced} of {conflicts.Count}" +
                             (refused.Count > 0 ? "; refused: " + string.Join("; ", refused.Take(3)) : "");
                StingLog.Info($"FixTagFamilyParamTypes: '{row.FamilyName}' re-pointed {row.Replaced} " +
                              $"of {conflicts.Count} parameter(s)" +
                              (refused.Count > 0 ? $", {refused.Count} refused" : ""));
                return row;
            }
            catch (Exception ex)
            {
                row.Verdict = "ERROR";
                row.Detail = ex.Message;
                StingLog.Warn($"FixTagFamilyParamTypes '{row.FamilyName}': {ex.Message}");
                return row;
            }
            finally
            {
                try { famDoc?.Close(false); }
                catch (Exception ex) { StingLog.Warn($"FixTagFamilyParamTypes: close '{row.FamilyName}': {ex.Message}"); }
            }
        }

        private enum RunMode { Cancel, Audit, Apply }

        private static RunMode ChooseMode(int fileCount)
        {
            var td = new TaskDialog("Fix Tag Family Parameter Types");
            td.MainInstruction = $"{fileCount} tag famil{(fileCount == 1 ? "y" : "ies")} found.";
            td.MainContent =
                "A family that types a shared parameter differently from MR_PARAMETERS.txt cannot " +
                "be loaded into a project that holds the declared type — Revit refuses with one " +
                "error per parameter.\n\n" +
                "This re-points those parameters at the declared definitions, in the .rfa.";
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "AUDIT — report only (recommended first)",
                "Opens each family, lists the parameters that disagree and what the declaration says.");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "APPLY — re-point them in the .rfa files",
                "Choose which files. Copies them first. Revit's refusals are reported, not forced.");
            var choice = td.Show();
            if (choice == TaskDialogResult.CommandLink1) return RunMode.Audit;
            if (choice == TaskDialogResult.CommandLink2) return RunMode.Apply;
            return RunMode.Cancel;
        }

        private static string PickFolder()
        {
            string start = null;
            try { start = TagFamilyConfig.GetOutputDirectory(); }
            catch (Exception ex) { StingLog.Warn($"FixTagFamilyParamTypes: default folder: {ex.Message}"); }
            try
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Select the folder of STING tag families (.rfa) to correct.";
                    dlg.ShowNewFolderButton = false;
                    if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dlg.SelectedPath = start;
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
                    return dlg.SelectedPath;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FixTagFamilyParamTypes: folder picker: {ex.Message}");
                return null;
            }
        }

        private static List<string> ChooseFiles(List<string> rfas)
        {
            var items = rfas.Select(p => new StingListPicker.ListItem
            {
                Label = Path.GetFileNameWithoutExtension(p),
                Tag = p,
                IsSelected = (Path.GetFileNameWithoutExtension(p) ?? "")
                    .IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0,
            }).ToList();

            List<StingListPicker.ListItem> picked;
            try
            {
                picked = StingListPicker.Show(
                    "Choose families to correct",
                    "Duct is already highlighted — press OK to take just that one. This list allows " +
                    "multiple selections, so clicking a highlighted row turns it OFF; Ctrl+click adds.",
                    items, allowMultiSelect: true);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FixTagFamilyParamTypes: file picker: {ex.Message}");
                return null;
            }

            if (picked == null) { StingLog.Info("FixTagFamilyParamTypes: file picker cancelled"); return null; }
            if (picked.Count == 0)
            {
                StingLog.Warn("FixTagFamilyParamTypes: OK pressed with nothing highlighted");
                TaskDialog.Show("Fix Tag Family Parameter Types",
                    "No families were highlighted, so nothing was changed.\n\n" +
                    "The list toggles on click and Duct starts highlighted, so clicking it turns it " +
                    "off. Re-run and press OK without clicking.");
                return null;
            }
            return picked.Select(p => p.Tag as string).Where(s => !string.IsNullOrEmpty(s)).ToList();
        }
    }
}
