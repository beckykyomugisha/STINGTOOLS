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
    /// Bind ALL shared parameters from MR_PARAMETERS.txt to project categories.
    ///
    /// Always uses MR_PARAMETERS.txt from the data directory (overrides whatever
    /// shared parameter file is currently set in Revit).
    ///
    /// PERFORMANCE — why Revit hangs with naive "bind all to all":
    ///   Each doc.ParameterBindings.Insert() with N categories creates N internal
    ///   schema entries. 1,527 params × 200 categories = 305,000 registrations
    ///   which overwhelms Revit's document engine.
    ///
    /// Strategy: bind ONE GROUP per transaction. Each group gets the categories
    /// relevant to that group (e.g., MEP params → MEP categories only).
    /// Groups without specific mappings get a core taggable set (~53 categories).
    /// This keeps each transaction small and targeted.
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

            // ── Step 1: Locate and set MR_PARAMETERS.txt ──
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

            // ── Step 2: Open definition file ──
            StingLog.Info("LoadSharedParams: step 2 — opening definition file");
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "Could not open shared parameter file:\n" + spFile);
                return Result.Failed;
            }

            // Index params by group
            var groupDefs = new List<(string groupName, List<ExternalDefinition> defs)>();
            int totalDefs = 0;
            foreach (DefinitionGroup group in defFile.Groups)
            {
                var defs = new List<ExternalDefinition>();
                foreach (Definition def in group.Definitions)
                {
                    if (def is ExternalDefinition ext)
                        defs.Add(ext);
                }
                if (defs.Count > 0)
                {
                    groupDefs.Add((group.Name, defs));
                    totalDefs += defs.Count;
                }
            }
            StingLog.Info($"LoadSharedParams: {totalDefs} definitions in {groupDefs.Count} groups");

            if (totalDefs < 100)
            {
                StingLog.Warn($"Only {totalDefs} parameters found — expected 1,527+. " +
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

            // Filter each group to only unbound params
            int totalToBind = 0;
            int alreadyBound = 0;
            var groupsToProcess = new List<(string groupName, List<ExternalDefinition> defs)>();
            foreach (var (groupName, defs) in groupDefs)
            {
                var unbound = defs.Where(d => !existingBindings.Contains(d.Name)).ToList();
                alreadyBound += defs.Count - unbound.Count;
                if (unbound.Count > 0)
                {
                    groupsToProcess.Add((groupName, unbound));
                    totalToBind += unbound.Count;
                }
            }

            StingLog.Info($"To bind: {totalToBind}, already bound: {alreadyBound}");

            if (totalToBind == 0)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    $"All {totalDefs} parameters are already bound — nothing to do.\n\n" +
                    $"{alreadyBound} parameters already present in project.\n" +
                    $"\nSource: {spFile}");
                return Result.Succeeded;
            }

            // ── Step 4: Build category sets ──
            // Core set: ParamRegistry categories (~53 proven categories)
            // This avoids the 200+ categories from doc.Settings.Categories which
            // overwhelm Revit. ParamRegistry categories are the ones STING actually uses.
            StingLog.Info("LoadSharedParams: step 4 — building category sets");

            var coreEnums = SharedParamGuids.AllCategoryEnums;
            CategorySet coreCats = SharedParamGuids.BuildCategorySet(doc, coreEnums);

            // Also add Materials category if not in core set (needed for MAT_* params)
            try
            {
                Category matCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Materials);
                if (matCat != null && matCat.AllowsBoundParameters && !coreCats.Contains(matCat))
                    coreCats.Insert(matCat);
            }
            catch { }

            StingLog.Info($"Core CategorySet: {coreCats.Size} categories");

            if (coreCats.Size == 0)
            {
                // Fallback: if ParamRegistry failed to load, use doc categories
                // but cap at reasonable number
                StingLog.Warn("ParamRegistry categories empty — falling back to doc categories");
                foreach (Category cat in doc.Settings.Categories)
                {
                    try
                    {
                        if (cat != null && cat.AllowsBoundParameters)
                            coreCats.Insert(cat);
                    }
                    catch { }
                }
                StingLog.Info($"Fallback CategorySet: {coreCats.Size} categories");
            }

            if (coreCats.Size == 0)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "No categories found that accept bound parameters.");
                return Result.Failed;
            }

            // Build group-specific category sets for discipline groups
            var disciplineBindings = SharedParamGuids.DisciplineBindings;
            // Map group names to specific category sets where applicable
            var groupCatOverrides = BuildGroupCategoryOverrides(doc, coreCats);

            // ── Step 5: Bind ONE GROUP per transaction ──
            // Each group gets its own transaction to keep Revit responsive.
            // Reuse a single InstanceBinding per group (avoids CategorySet copy overhead).
            int bound = 0;
            int skipped = 0;
            var errors = new List<string>();
            var boundByGroup = new Dictionary<string, int>();

            StingLog.Info($"Binding {totalToBind} params across {groupsToProcess.Count} groups");

            for (int gi = 0; gi < groupsToProcess.Count; gi++)
            {
                var (groupName, defs) = groupsToProcess[gi];

                // Pick the right category set for this group
                CategorySet cats = groupCatOverrides.TryGetValue(groupName, out CategorySet groupCats)
                    ? groupCats : coreCats;

                // Create ONE binding for this group, reuse for all params in the group
                InstanceBinding groupBinding = app.Create.NewInstanceBinding(cats);

                StingLog.Info($"Group [{gi + 1}/{groupsToProcess.Count}] '{groupName}': " +
                    $"{defs.Count} params → {cats.Size} categories");

                using (Transaction tx = new Transaction(doc,
                    $"STING Params: {groupName}"))
                {
                    var failOpts = tx.GetFailureHandlingOptions();
                    failOpts.SetFailuresPreprocessor(new BindingWarningSwallower());
                    tx.SetFailureHandlingOptions(failOpts);

                    tx.Start();
                    try
                    {
                        int groupBound = 0;
                        foreach (ExternalDefinition extDef in defs)
                        {
                            try
                            {
                                // Check if this param has discipline-specific categories
                                InstanceBinding paramBinding = groupBinding;
                                if (disciplineBindings.TryGetValue(extDef.Name,
                                    out BuiltInCategory[] paramCats) && paramCats.Length > 0)
                                {
                                    CategorySet specific = SharedParamGuids.BuildCategorySet(doc, paramCats);
                                    if (specific.Size > 0)
                                        paramBinding = app.Create.NewInstanceBinding(specific);
                                }

                                bool result = doc.ParameterBindings.Insert(
                                    extDef, paramBinding, GroupTypeId.General);

                                if (result)
                                {
                                    bound++;
                                    groupBound++;
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
                        boundByGroup[groupName] = groupBound;
                        StingLog.Info($"  → committed: {groupBound} bound");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"Group '{groupName}' transaction failed", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();

                        skipped += defs.Count;
                        if (errors.Count < 10)
                            errors.Add($"Group '{groupName}' failed: {ex.Message}");
                    }
                }
            }

            // ── Report ──
            var report = new StringBuilder();
            report.AppendLine($"Bound: {bound} parameters");
            report.AppendLine($"Already present: {alreadyBound}");
            report.AppendLine($"Skipped/failed: {skipped}");
            report.AppendLine($"Total in file: {totalDefs}");
            report.AppendLine($"Categories: {coreCats.Size}");
            report.AppendLine($"Groups processed: {groupsToProcess.Count}");
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
        /// Build group-specific category overrides. Groups with discipline-specific
        /// parameters get smaller, targeted category sets instead of the full core set.
        /// This dramatically reduces the number of internal bindings Revit must create.
        /// </summary>
        private static Dictionary<string, CategorySet> BuildGroupCategoryOverrides(
            Document doc, CategorySet coreCats)
        {
            var overrides = new Dictionary<string, CategorySet>(StringComparer.OrdinalIgnoreCase);

            // MEP groups → only MEP categories
            var mepCats = BuildCatSet(doc, new[]
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_FlexDuctCurves,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_FlexPipeCurves,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_Sprinklers,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_CommunicationDevices,
                BuiltInCategory.OST_DataDevices,
                BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_NurseCallDevices,
                BuiltInCategory.OST_SecurityDevices,
                BuiltInCategory.OST_TelephoneDevices,
            });

            if (mepCats.Size > 0)
            {
                overrides["ELC_PWR"] = mepCats;
                overrides["HVC_SYSTEMS"] = mepCats;
                overrides["PLM_DRN"] = mepCats;
                overrides["LTG_CONTROLS"] = mepCats;
                overrides["FLS_LIFE_SFTY"] = mepCats;
                overrides["MEP_GENERIC"] = mepCats;
            }

            // BLE groups → building element categories
            var bleCats = BuildCatSet(doc, new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralFoundation,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_StairsRailing,
                BuiltInCategory.OST_Ramps,
                BuiltInCategory.OST_CurtainWallPanels,
                BuiltInCategory.OST_CurtainWallMullions,
                BuiltInCategory.OST_Casework,
                BuiltInCategory.OST_Furniture,
                BuiltInCategory.OST_FurnitureSystems,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_SpecialityEquipment,
            });

            if (bleCats.Size > 0)
            {
                overrides["BLE_ELES"] = bleCats;
                overrides["BLE_STRUCTURE"] = bleCats;
            }

            // Material groups → Materials category only
            var matCats = BuildCatSet(doc, new[]
            {
                BuiltInCategory.OST_Materials,
            });

            if (matCats.Size > 0)
            {
                overrides["MAT_INFO"] = matCats;
                overrides["PROP_PHYSICAL"] = matCats;
            }

            // TAG_STYLES → use core cats (these go on all taggable elements)
            // No override needed — will use coreCats

            return overrides;
        }

        private static CategorySet BuildCatSet(Document doc, BuiltInCategory[] enums)
        {
            var set = new CategorySet();
            foreach (var bic in enums)
            {
                try
                {
                    Category cat = doc.Settings.Categories.get_Item(bic);
                    if (cat != null && cat.AllowsBoundParameters)
                        set.Insert(cat);
                }
                catch { }
            }
            return set;
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
                if (failure.GetSeverity() == FailureSeverity.Warning)
                    failuresAccessor.DeleteWarning(failure);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
