using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Bind ALL shared parameters from MR_PARAMETERS.txt to ALL project categories.
    ///
    /// Always uses MR_PARAMETERS.txt from the data directory (overrides whatever
    /// shared parameter file is currently set in Revit). Binds every parameter
    /// definition to every category that allows bound parameters — not just the
    /// taggable subset from ParamRegistry.
    ///
    /// CRASH FIX: Uses ONE transaction instead of many batched transactions.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadSharedParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                return ExecuteCore(commandData, ref message, elements);
            }
            catch (Exception ex)
            {
                StingLog.Error("LoadSharedParamsCommand crashed", ex);
                try
                {
                    TaskDialog.Show("STING Tools - Load Shared Params",
                        $"Command failed with an unexpected error:\n\n{ex.Message}\n\n" +
                        "Check StingTools.log for details.");
                }
                catch { /* If even the dialog fails, don't crash Revit */ }
                return Result.Failed;
            }
        }

        private Result ExecuteCore(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            Document doc = uiApp.ActiveUIDocument.Document;
            Autodesk.Revit.ApplicationServices.Application app =
                uiApp.Application;

            // ── Step 1: ALWAYS set shared parameter file to MR_PARAMETERS.txt ──
            // Override any existing shared parameter file to ensure we use the
            // full STING parameter set (1,527+ params), not a subset.
            StingLog.Info("LoadSharedParams: step 1 — setting shared parameter file to MR_PARAMETERS.txt");
            string previousSpFile = app.SharedParametersFilename;
            string mrParamsPath = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");

            if (!string.IsNullOrEmpty(mrParamsPath) && File.Exists(mrParamsPath))
            {
                app.SharedParametersFilename = mrParamsPath;
                StingLog.Info($"Set shared parameter file: {mrParamsPath}");
                if (!string.IsNullOrEmpty(previousSpFile) && previousSpFile != mrParamsPath)
                    StingLog.Info($"Previous shared parameter file was: {previousSpFile}");
            }
            else
            {
                // Fallback: use whatever is already set, or fail
                if (string.IsNullOrEmpty(previousSpFile) || !File.Exists(previousSpFile))
                {
                    TaskDialog.Show("STING Tools - Load Shared Params",
                        "Could not find MR_PARAMETERS.txt.\n\n" +
                        "Expected location: " +
                        (StingToolsApp.DataPath ?? "(DataPath not set)") +
                        "\n\nEither place the file there or go to " +
                        "Manage → Shared Parameters and set the path manually.");
                    return Result.Failed;
                }
                mrParamsPath = previousSpFile;
                StingLog.Warn($"MR_PARAMETERS.txt not found, using existing: {previousSpFile}");
            }

            string spFile = mrParamsPath;

            // ── Step 2: Open definition file and index ALL parameters ──
            StingLog.Info("LoadSharedParams: step 2 — opening definition file");
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "Could not open shared parameter file:\n" + spFile);
                return Result.Failed;
            }

            // Index all parameters from all groups
            var allDefs = new List<ExternalDefinition>();
            var groupCounts = new Dictionary<string, int>();
            foreach (DefinitionGroup group in defFile.Groups)
            {
                int count = 0;
                foreach (Definition def in group.Definitions)
                {
                    if (def is ExternalDefinition ext)
                    {
                        allDefs.Add(ext);
                        count++;
                    }
                }
                groupCounts[group.Name] = count;
            }
            StingLog.Info($"LoadSharedParams: {allDefs.Count} definitions indexed from {groupCounts.Count} groups");

            if (allDefs.Count < 100)
            {
                StingLog.Warn($"Only {allDefs.Count} parameters found — expected 1,527+. " +
                    "Ensure MR_PARAMETERS.txt is the full STING parameter file.");
            }

            // ── Step 3: Pre-scan existing bindings ──
            StingLog.Info("LoadSharedParams: step 3 — scanning existing bindings");
            var existingBindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var iter = doc.ParameterBindings.ForwardIterator();
            while (iter.MoveNext())
            {
                if (iter.Key is ExternalDefinition exDef)
                    existingBindings.Add(exDef.Name);
                else if (iter.Key != null)
                    existingBindings.Add(iter.Key.Name);
            }
            StingLog.Info($"Pre-scan: {existingBindings.Count} parameters already bound");

            // Filter to only parameters not yet bound
            var toBind = allDefs.Where(d => !existingBindings.Contains(d.Name)).ToList();
            int alreadyBound = allDefs.Count - toBind.Count;

            StingLog.Info($"To bind: {toBind.Count}, already bound: {alreadyBound}");

            if (toBind.Count == 0)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    $"All {allDefs.Count} parameters are already bound — nothing to do.\n\n" +
                    $"{alreadyBound} parameters already present in project.\n" +
                    $"{groupCounts.Count} parameter groups.\n" +
                    $"\nSource: {spFile}");
                return Result.Succeeded;
            }

            // ── Step 4: Build ALL-categories set ──
            // Iterate every category in the document and include all that allow bound parameters.
            // This ensures parameters are available on ALL element types, not just the taggable subset.
            StingLog.Info("LoadSharedParams: step 4 — building ALL-categories set");
            CategorySet allCats = new CategorySet();
            int catTotal = 0;
            int catAdded = 0;

            foreach (Category cat in doc.Settings.Categories)
            {
                catTotal++;
                try
                {
                    if (cat != null && cat.AllowsBoundParameters)
                    {
                        allCats.Insert(cat);
                        catAdded++;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Category '{cat?.Name}' skipped: {ex.Message}");
                }
            }
            StingLog.Info($"CategorySet built: {catAdded} of {catTotal} categories accept bound parameters");

            if (allCats.Size == 0)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "No categories found that accept bound parameters.\n" +
                    "This should not happen — contact support.");
                return Result.Failed;
            }

            // ── Step 5: Bind ALL parameters in a SINGLE transaction ──
            int bound = 0;
            int skipped = 0;
            var errors = new List<string>();
            var boundByGroup = new Dictionary<string, int>();

            StingLog.Info($"Binding {toBind.Count} parameters to {allCats.Size} categories in single transaction");
            using (Transaction tx = new Transaction(doc, "STING Load Shared Params"))
            {
                tx.Start();
                try
                {
                    foreach (ExternalDefinition extDef in toBind)
                    {
                        try
                        {
                            InstanceBinding binding = app.Create.NewInstanceBinding(allCats);
                            bool result = doc.ParameterBindings.Insert(
                                extDef, binding, GroupTypeId.General);
                            if (!result)
                                result = doc.ParameterBindings.ReInsert(
                                    extDef, binding, GroupTypeId.General);

                            if (result)
                            {
                                bound++;
                                string grpName = extDef.OwnerGroup?.Name ?? "Unknown";
                                if (!boundByGroup.ContainsKey(grpName))
                                    boundByGroup[grpName] = 0;
                                boundByGroup[grpName]++;
                            }
                            else
                            {
                                skipped++;
                                if (skipped <= 20)
                                    StingLog.Warn($"Parameter binding failed (Insert+ReInsert): {extDef.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            skipped++;
                            if (errors.Count < 20)
                                errors.Add($"{extDef.Name}: {ex.Message}");
                        }
                    }

                    tx.Commit();
                    StingLog.Info($"Transaction committed: {bound} bound, {skipped} skipped");
                }
                catch (Exception ex)
                {
                    StingLog.Error("Parameter binding transaction failed", ex);
                    if (tx.HasStarted() && !tx.HasEnded())
                        tx.RollBack();
                    TaskDialog.Show("STING Tools - Load Shared Params",
                        $"Transaction failed:\n{ex.Message}");
                    return Result.Failed;
                }
            }

            // ── Report ──
            var report = new StringBuilder();
            report.AppendLine($"Bound: {bound} parameters");
            report.AppendLine($"Already present: {alreadyBound}");
            report.AppendLine($"Skipped/failed: {skipped}");
            report.AppendLine($"Total in file: {allDefs.Count}");
            report.AppendLine($"Categories: {allCats.Size}");
            report.AppendLine($"Groups: {groupCounts.Count}");
            report.AppendLine();

            if (boundByGroup.Count > 0)
            {
                report.AppendLine("Bound by group:");
                foreach (var kvp in boundByGroup.OrderByDescending(kv => kv.Value))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }

            report.AppendLine($"\nSource: {spFile}");

            if (errors.Count > 0)
            {
                report.AppendLine($"\nErrors ({errors.Count}):");
                foreach (string err in errors.Take(10))
                    report.AppendLine($"  {err}");
            }

            var td = new TaskDialog("STING Tools - Load Shared Params");
            td.MainInstruction = $"Shared parameter binding complete — {bound} bound.";
            td.MainContent = report.ToString();
            td.CommonButtons = TaskDialogCommonButtons.Ok;
            td.DefaultButton = TaskDialogResult.Ok;
            td.Show();
            StingLog.Info($"LoadSharedParams complete: {bound} bound, {alreadyBound} already present, {skipped} skipped");

            return Result.Succeeded;
        }

    }
}
