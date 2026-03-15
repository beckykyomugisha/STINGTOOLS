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
    /// shared parameter file is currently set in Revit).
    ///
    /// PERFORMANCE:
    ///   - Reuses a single InstanceBinding object (avoids copying CategorySet per param)
    ///   - Small batches (25 params/tx) to avoid overwhelming Revit's regeneration engine
    ///   - Suppresses failure warnings within transactions
    ///   - Skips ReInsert (only needed for category expansion, not initial binding)
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadSharedParamsCommand : IExternalCommand
    {
        private const int BatchSize = 25;

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
            StingLog.Info("LoadSharedParams: step 1 — locating MR_PARAMETERS.txt");
            string previousSpFile = app.SharedParametersFilename;
            string mrParamsPath = FindMrParametersFile(previousSpFile);

            if (!string.IsNullOrEmpty(mrParamsPath) && File.Exists(mrParamsPath))
            {
                app.SharedParametersFilename = mrParamsPath;
                StingLog.Info($"Set shared parameter file: {mrParamsPath}");
            }
            else
            {
                if (string.IsNullOrEmpty(previousSpFile) || !File.Exists(previousSpFile))
                {
                    TaskDialog.Show("STING Tools - Load Shared Params",
                        "Could not find MR_PARAMETERS.txt.\n\n" +
                        "Expected location: " +
                        (StingToolsApp.DataPath ?? "(DataPath not set)") +
                        "\n\nSearched paths:\n" +
                        string.Join("\n", GetSearchPaths()) +
                        "\n\nEither place the file in the data directory or go to " +
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
            StingLog.Info($"LoadSharedParams: {allDefs.Count} definitions from {groupCounts.Count} groups");

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

            // ── Step 4: Build category set ──
            StingLog.Info("LoadSharedParams: step 4 — building category set");
            CategorySet allCats = new CategorySet();
            int catAdded = 0;

            foreach (Category cat in doc.Settings.Categories)
            {
                try
                {
                    if (cat != null && cat.AllowsBoundParameters)
                    {
                        allCats.Insert(cat);
                        catAdded++;
                    }
                }
                catch { /* skip problematic categories */ }
            }
            StingLog.Info($"CategorySet: {catAdded} categories accept bound parameters");

            if (allCats.Size == 0)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "No categories found that accept bound parameters.");
                return Result.Failed;
            }

            // ── Step 5: Bind parameters in small batches ──
            // PERFORMANCE CRITICAL:
            //   - Create ONE InstanceBinding and REUSE it for all params.
            //     NewInstanceBinding copies the CategorySet internally — creating
            //     it once avoids 1,527 × N-category copy operations.
            //   - Small batches (25) to keep Revit responsive.
            //   - No ReInsert fallback — Insert handles fresh bindings;
            //     ReInsert is only for expanding categories on existing bindings.
            //   - Suppress failure warnings to avoid memory buildup.
            int bound = 0;
            int skipped = 0;
            var errors = new List<string>();
            var boundByGroup = new Dictionary<string, int>();
            int totalBatches = (toBind.Count + BatchSize - 1) / BatchSize;

            // Create the binding ONCE — reuse for every Insert call
            InstanceBinding sharedBinding = app.Create.NewInstanceBinding(allCats);

            StingLog.Info($"Binding {toBind.Count} params to {allCats.Size} categories " +
                $"in {totalBatches} batches of {BatchSize}");

            for (int batchIdx = 0; batchIdx < totalBatches; batchIdx++)
            {
                int startIdx = batchIdx * BatchSize;
                int endIdx = Math.Min(startIdx + BatchSize, toBind.Count);
                int batchNum = batchIdx + 1;

                using (Transaction tx = new Transaction(doc,
                    $"STING Load Params {batchNum}/{totalBatches}"))
                {
                    // Suppress failure warnings to prevent memory buildup
                    var failOpts = tx.GetFailureHandlingOptions();
                    failOpts.SetFailuresPreprocessor(new BindingWarningSwallower());
                    tx.SetFailureHandlingOptions(failOpts);

                    tx.Start();
                    try
                    {
                        for (int i = startIdx; i < endIdx; i++)
                        {
                            ExternalDefinition extDef = toBind[i];
                            try
                            {
                                bool result = doc.ParameterBindings.Insert(
                                    extDef, sharedBinding, GroupTypeId.General);

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
                                    if (skipped <= 10)
                                        StingLog.Warn($"Insert failed: {extDef.Name}");
                                }
                            }
                            catch (Exception ex)
                            {
                                skipped++;
                                if (errors.Count < 10)
                                    errors.Add($"{extDef.Name}: {ex.Message}");
                            }
                        }

                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"Batch {batchNum} failed", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();

                        int batchSkipped = endIdx - startIdx;
                        skipped += batchSkipped;
                        if (errors.Count < 10)
                            errors.Add($"Batch {batchNum} ({batchSkipped} params): {ex.Message}");
                    }
                }

                // Log progress every 5 batches
                if (batchNum % 5 == 0 || batchNum == totalBatches)
                    StingLog.Info($"Progress: batch {batchNum}/{totalBatches}, {bound} bound so far");
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
                foreach (var kvp in boundByGroup.OrderByDescending(kv => kv.Value).Take(15))
                    report.AppendLine($"  {kvp.Key}: {kvp.Value}");
                if (boundByGroup.Count > 15)
                    report.AppendLine($"  ... and {boundByGroup.Count - 15} more groups");
            }

            report.AppendLine($"\nSource: {spFile}");

            if (errors.Count > 0)
            {
                report.AppendLine($"\nErrors ({errors.Count}):");
                foreach (string err in errors.Take(5))
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

        /// <summary>
        /// Search multiple locations for MR_PARAMETERS.txt.
        /// </summary>
        private static string FindMrParametersFile(string currentSpFile)
        {
            const string fileName = "MR_PARAMETERS.txt";

            // 1. Standard data path lookup
            string found = StingToolsApp.FindDataFile(fileName);
            if (!string.IsNullOrEmpty(found) && File.Exists(found))
                return found;

            // 2. Search next to the currently set shared parameter file
            if (!string.IsNullOrEmpty(currentSpFile) && File.Exists(currentSpFile))
            {
                string spDir = Path.GetDirectoryName(currentSpFile);
                if (!string.IsNullOrEmpty(spDir))
                {
                    string candidate = Path.Combine(spDir, fileName);
                    if (File.Exists(candidate))
                    {
                        StingLog.Info($"Found {fileName} next to current SP file: {candidate}");
                        return candidate;
                    }
                }
            }

            // 3. Search common deployment paths
            foreach (string path in GetSearchPaths())
            {
                try
                {
                    if (File.Exists(path))
                    {
                        StingLog.Info($"Found {fileName} at: {path}");
                        return path;
                    }
                }
                catch { }
            }

            // 4. Recursive search from DLL directory
            string dllDir = !string.IsNullOrEmpty(StingToolsApp.AssemblyPath)
                ? Path.GetDirectoryName(StingToolsApp.AssemblyPath) : null;
            if (!string.IsNullOrEmpty(dllDir))
            {
                try
                {
                    var files = Directory.GetFiles(dllDir, fileName, SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        StingLog.Info($"Found {fileName} via recursive search: {files[0]}");
                        return files[0];
                    }

                    string parentDir = Path.GetDirectoryName(dllDir);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        files = Directory.GetFiles(parentDir, fileName, SearchOption.AllDirectories);
                        if (files.Length > 0)
                        {
                            StingLog.Info($"Found {fileName} via parent search: {files[0]}");
                            return files[0];
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Recursive search for {fileName} failed: {ex.Message}");
                }
            }

            StingLog.Warn($"{fileName} not found in any search path");
            return null;
        }

        private static string[] GetSearchPaths()
        {
            const string fileName = "MR_PARAMETERS.txt";
            var paths = new List<string>();

            string dllDir = !string.IsNullOrEmpty(StingToolsApp.AssemblyPath)
                ? Path.GetDirectoryName(StingToolsApp.AssemblyPath) : null;

            if (!string.IsNullOrEmpty(dllDir))
            {
                paths.Add(Path.Combine(dllDir, "data", fileName));
                paths.Add(Path.Combine(dllDir, "Data", fileName));
                paths.Add(Path.Combine(dllDir, fileName));
                paths.Add(Path.Combine(dllDir, "..", "data", fileName));
                paths.Add(Path.Combine(dllDir, "..", "Data", fileName));
                paths.Add(Path.Combine(dllDir, "..", "StingTools", "Data", fileName));
                paths.Add(Path.Combine(dllDir, "..", "StingTools", "data", fileName));
            }

            if (!string.IsNullOrEmpty(StingToolsApp.DataPath))
            {
                paths.Add(Path.Combine(StingToolsApp.DataPath, fileName));
                paths.Add(Path.Combine(StingToolsApp.DataPath, "..", fileName));
            }

            return paths.ToArray();
        }
    }

    /// <summary>
    /// Dismisses all warnings during parameter binding transactions.
    /// Without this, Revit accumulates FailureMessage objects in memory
    /// for each binding operation, causing slowdown and eventual crash.
    /// </summary>
    internal class BindingWarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var failures = failuresAccessor.GetFailureMessages();
            foreach (FailureMessageAccessor failure in failures)
            {
                // Delete warnings (not errors) — binding warnings are non-critical
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(failure);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
