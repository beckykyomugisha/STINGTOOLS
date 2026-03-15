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
    /// CRASH PREVENTION — multiple vectors addressed:
    ///   1. Volume: group-per-transaction with targeted category sets (~40K bindings vs 305K)
    ///   2. InstanceBinding reuse: ONE per group, never inside the per-param loop
    ///   3. No logging inside transactions: prevents disk I/O stalling Revit
    ///   4. No BuildCategorySet inside loops: all sets pre-built before transactions
    ///   5. No recursive directory search: capped search prevents hanging on broad dirs
    ///   6. BindingWarningSwallower: prevents FailureMessage memory buildup
    ///   7. Null guards: ActiveUIDocument, ParamRegistry fallback capped
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

            // BUG FIX: null check ActiveUIDocument — crashes if no document open
            if (uiApp.ActiveUIDocument == null)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "No document is open. Please open a project first.");
                return Result.Failed;
            }

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

            // ── Step 4: Build ALL category sets BEFORE any transactions ──
            // CRITICAL: Never call BuildCategorySet or NewInstanceBinding inside
            // the per-param loop — each call does doc.Settings.Categories.get_Item()
            // lookups and object allocation that stall Revit mid-transaction.
            StingLog.Info("LoadSharedParams: step 4 — building category sets");

            var coreEnums = SharedParamGuids.AllCategoryEnums;
            CategorySet coreCats = SharedParamGuids.BuildCategorySet(doc, coreEnums);

            // Add Materials category (needed for MAT_* params)
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
                // BUG FIX: Fallback must NOT use all doc categories (causes the 305K hang).
                // Instead, build from our hardcoded MEP + BLE sets.
                StingLog.Warn("ParamRegistry categories empty — building from hardcoded fallback");
                coreCats = BuildFallbackCoreSet(doc);
                StingLog.Info($"Fallback CategorySet: {coreCats.Size} categories");
            }

            if (coreCats.Size == 0)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "No categories found that accept bound parameters.");
                return Result.Failed;
            }

            // Pre-build group-specific category sets
            var groupCatOverrides = BuildGroupCategoryOverrides(doc);

            // Pre-build InstanceBindings for each group OUTSIDE the transaction loops.
            // This avoids calling NewInstanceBinding() inside per-param loops.
            var groupBindings = new Dictionary<string, InstanceBinding>(StringComparer.OrdinalIgnoreCase);
            InstanceBinding coreBinding = app.Create.NewInstanceBinding(coreCats);
            foreach (var kvp in groupCatOverrides)
                groupBindings[kvp.Key] = app.Create.NewInstanceBinding(kvp.Value);

            // ── Step 5: Bind ONE GROUP per transaction ──
            int bound = 0;
            int skipped = 0;
            var errors = new List<string>();
            var boundByGroup = new Dictionary<string, int>();

            StingLog.Info($"Binding {totalToBind} params across {groupsToProcess.Count} groups");

            for (int gi = 0; gi < groupsToProcess.Count; gi++)
            {
                var (groupName, defs) = groupsToProcess[gi];

                // Pick pre-built binding for this group
                InstanceBinding binding = groupBindings.TryGetValue(groupName, out InstanceBinding gb)
                    ? gb : coreBinding;

                int catCount = groupCatOverrides.TryGetValue(groupName, out CategorySet gcs)
                    ? gcs.Size : coreCats.Size;

                // Log BEFORE transaction, not inside
                StingLog.Info($"Group [{gi + 1}/{groupsToProcess.Count}] '{groupName}': " +
                    $"{defs.Count} params → {catCount} categories");

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
                                // Use the pre-built group binding — NEVER create
                                // new InstanceBinding or call BuildCategorySet here
                                bool result = doc.ParameterBindings.Insert(
                                    extDef, binding, GroupTypeId.General);

                                if (result)
                                    groupBound++;
                                else
                                    skipped++;
                            }
                            catch
                            {
                                skipped++;
                            }
                        }

                        tx.Commit();
                        bound += groupBound;
                        boundByGroup[groupName] = groupBound;

                        // Log AFTER transaction, not inside
                        StingLog.Info($"  → committed: {groupBound} bound, {defs.Count - groupBound} skipped");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"Group '{groupName}' transaction failed", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();

                        skipped += defs.Count;
                        if (errors.Count < 10)
                            errors.Add($"Group '{groupName}': {ex.Message}");
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

        // ════════════════════════════════════════════════════════════════
        // Category set builders — called ONCE before any transactions
        // ════════════════════════════════════════════════════════════════

        private static readonly BuiltInCategory[] MepCategories = new[]
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
        };

        private static readonly BuiltInCategory[] BleCategories = new[]
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
        };

        /// <summary>
        /// Build group-specific category overrides. Groups with discipline-specific
        /// parameters get smaller, targeted category sets instead of the full core set.
        /// Called ONCE before transactions — never inside a loop.
        /// </summary>
        private static Dictionary<string, CategorySet> BuildGroupCategoryOverrides(Document doc)
        {
            var overrides = new Dictionary<string, CategorySet>(StringComparer.OrdinalIgnoreCase);

            var mepCats = BuildCatSet(doc, MepCategories);
            if (mepCats.Size > 0)
            {
                overrides["ELC_PWR"] = mepCats;
                overrides["HVC_SYSTEMS"] = mepCats;
                overrides["PLM_DRN"] = mepCats;
                overrides["LTG_CONTROLS"] = mepCats;
                overrides["FLS_LIFE_SFTY"] = mepCats;
                overrides["MEP_GENERIC"] = mepCats;
            }

            var bleCats = BuildCatSet(doc, BleCategories);
            if (bleCats.Size > 0)
            {
                overrides["BLE_ELES"] = bleCats;
                overrides["BLE_STRUCTURE"] = bleCats;
            }

            var matCats = BuildCatSet(doc, new[] { BuiltInCategory.OST_Materials });
            if (matCats.Size > 0)
            {
                overrides["MAT_INFO"] = matCats;
                overrides["PROP_PHYSICAL"] = matCats;
            }

            return overrides;
        }

        /// <summary>
        /// Fallback core category set when ParamRegistry fails to load.
        /// Uses the MEP + BLE arrays (capped, known-good categories) instead
        /// of doc.Settings.Categories which includes 200+ internal categories.
        /// </summary>
        private static CategorySet BuildFallbackCoreSet(Document doc)
        {
            var set = new CategorySet();
            // Combine MEP + BLE + a few extra common categories
            foreach (var bic in MepCategories) TryInsert(doc, set, bic);
            foreach (var bic in BleCategories) TryInsert(doc, set, bic);
            TryInsert(doc, set, BuiltInCategory.OST_Materials);
            TryInsert(doc, set, BuiltInCategory.OST_Rooms);
            TryInsert(doc, set, BuiltInCategory.OST_Areas);
            TryInsert(doc, set, BuiltInCategory.OST_Parking);
            return set;
        }

        private static void TryInsert(Document doc, CategorySet set, BuiltInCategory bic)
        {
            try
            {
                Category cat = doc.Settings.Categories.get_Item(bic);
                if (cat != null && cat.AllowsBoundParameters)
                    set.Insert(cat);
            }
            catch { }
        }

        private static CategorySet BuildCatSet(Document doc, BuiltInCategory[] enums)
        {
            var set = new CategorySet();
            foreach (var bic in enums) TryInsert(doc, set, bic);
            return set;
        }

        // ════════════════════════════════════════════════════════════════
        // File search — finds MR_PARAMETERS.txt across deployment layouts
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Search multiple locations for MR_PARAMETERS.txt.
        /// HANG FIX: No recursive directory search (Directory.GetFiles with
        /// SearchOption.AllDirectories can scan thousands of files on broad paths
        /// like C:\ProgramData\Autodesk\, freezing Revit for minutes).
        /// </summary>
        private static string FindMrParametersFile(string currentSpFile)
        {
            const string fileName = "MR_PARAMETERS.txt";

            // 1. Standard data path lookup
            string found = StingToolsApp.FindDataFile(fileName);
            if (!string.IsNullOrEmpty(found) && File.Exists(found))
                return found;

            // 2. Search next to the currently set shared parameter file
            if (!string.IsNullOrEmpty(currentSpFile))
            {
                try
                {
                    if (File.Exists(currentSpFile))
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
                }
                catch { /* path resolution failed */ }
            }

            // 3. Search known deployment paths — flat lookups only, NO recursive search
            string dllDir = !string.IsNullOrEmpty(StingToolsApp.AssemblyPath)
                ? Path.GetDirectoryName(StingToolsApp.AssemblyPath) : null;

            if (!string.IsNullOrEmpty(dllDir))
            {
                string[] candidates = new[]
                {
                    Path.Combine(dllDir, "data", fileName),
                    Path.Combine(dllDir, "Data", fileName),
                    Path.Combine(dllDir, fileName),
                    Path.Combine(dllDir, "..", "data", fileName),
                    Path.Combine(dllDir, "..", "Data", fileName),
                    Path.Combine(dllDir, "..", "StingTools", "Data", fileName),
                    Path.Combine(dllDir, "..", "StingTools", "data", fileName),
                };

                foreach (string path in candidates)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            string resolved = Path.GetFullPath(path);
                            StingLog.Info($"Found {fileName} at: {resolved}");
                            return resolved;
                        }
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrEmpty(StingToolsApp.DataPath))
            {
                try
                {
                    string inData = Path.Combine(StingToolsApp.DataPath, fileName);
                    if (File.Exists(inData)) return inData;
                    string aboveData = Path.Combine(StingToolsApp.DataPath, "..", fileName);
                    if (File.Exists(aboveData)) return Path.GetFullPath(aboveData);
                }
                catch { }
            }

            StingLog.Warn($"{fileName} not found in any search path");
            return null;
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
            try
            {
                var failures = failuresAccessor.GetFailureMessages();
                if (failures == null) return FailureProcessingResult.Continue;
                foreach (FailureMessageAccessor failure in failures)
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                        failuresAccessor.DeleteWarning(failure);
                }
            }
            catch
            {
                // Never crash inside the failure preprocessor — Revit will hang
            }
            return FailureProcessingResult.Continue;
        }
    }
}
