using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Bind shared parameters (universal + discipline-specific) to project categories.
    /// Pass 1: universal ASS_MNG parameters → all 53 categories (single transaction).
    /// Pass 2: discipline-specific tag containers → correct category subsets (single transaction).
    ///
    /// CRASH FIX: Uses ONE transaction per pass instead of many batched transactions.
    /// Rapid-fire transaction commits trigger Revit's deferred regeneration engine which
    /// causes native segfaults (C++ level) — the same root cause documented in
    /// StingCommandHandler.cs ENH-003.
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

            // ── Step 1: Ensure shared parameter file is set ──
            StingLog.Info("LoadSharedParams: step 1 — checking shared parameter file");
            string spFile = app.SharedParametersFilename;
            if (string.IsNullOrEmpty(spFile) || !File.Exists(spFile))
            {
                string autoPath = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                if (!string.IsNullOrEmpty(autoPath) && File.Exists(autoPath))
                {
                    app.SharedParametersFilename = autoPath;
                    spFile = autoPath;
                    StingLog.Info($"Auto-set shared parameter file: {autoPath}");
                }
                else
                {
                    TaskDialog.Show("STING Tools - Load Shared Params",
                        "Could not find MR_PARAMETERS.txt.\n\n" +
                        "Expected location: " +
                        (StingToolsApp.DataPath ?? "(DataPath not set)") +
                        "\n\nEither place the file there or go to " +
                        "Manage → Shared Parameters and set the path manually.");
                    return Result.Failed;
                }
            }

            // ── Step 2: Open definition file and build lookup ──
            StingLog.Info("LoadSharedParams: step 2 — opening definition file");
            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "Could not open shared parameter file:\n" + spFile);
                return Result.Failed;
            }

            var defLookup = new Dictionary<string, ExternalDefinition>(StringComparer.Ordinal);
            foreach (DefinitionGroup group in defFile.Groups)
                foreach (Definition def in group.Definitions)
                    if (def is ExternalDefinition ext)
                        defLookup[def.Name] = ext;
            StingLog.Info($"LoadSharedParams: {defLookup.Count} definitions indexed");

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

            // ── Step 4: Prepare category sets (read-only, no transaction needed) ──
            StingLog.Info("LoadSharedParams: step 4 — building category sets");
            var universalCatEnums = SharedParamGuids.AllCategoryEnums;
            StingLog.Info($"AllCategoryEnums: {universalCatEnums?.Length ?? 0} entries");
            CategorySet allCats = SharedParamGuids.BuildCategorySet(doc, universalCatEnums);
            StingLog.Info($"CategorySet built: {allCats.Size} categories resolved");

            if (allCats.Size == 0)
            {
                StingLog.Warn("No categories resolved — PARAMETER_REGISTRY.json may be missing. " +
                    $"DataPath: {StingToolsApp.DataPath}");
            }

            // ── Step 5: Filter params to bind ──
            StingLog.Info("LoadSharedParams: step 5 — filtering parameters");
            var allUniversalParams = SharedParamGuids.UniversalParams ?? Array.Empty<string>();
            var universalToBind = allUniversalParams
                .Where(p => !existingBindings.Contains(p)).ToArray();
            int pass1AlreadyBound = allUniversalParams.Length - universalToBind.Length;

            // Load CSV bindings for Pass 2
            Dictionary<string, List<(string category, string bindingType, bool isShared)>> csvBindings;
            try
            {
                csvBindings = Temp.TemplateManager.LoadCategoryBindings();
                StingLog.Info($"CSV bindings loaded: {csvBindings.Count} parameter entries");
            }
            catch (Exception ex)
            {
                csvBindings = new Dictionary<string, List<(string, string, bool)>>();
                StingLog.Warn($"LoadCategoryBindings failed (continuing without CSV): {ex.Message}");
            }

            var allDisciplineParams = new HashSet<string>(
                SharedParamGuids.DisciplineBindings.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (string csvParam in csvBindings.Keys)
                allDisciplineParams.Add(csvParam);

            var disciplineToBind = allDisciplineParams
                .Where(p => !existingBindings.Contains(p)).ToList();
            int pass2AlreadyBound = allDisciplineParams.Count - disciplineToBind.Count;

            StingLog.Info($"Pass 1: {universalToBind.Length} to bind, {pass1AlreadyBound} already bound");
            StingLog.Info($"Pass 2: {disciplineToBind.Count} to bind, {pass2AlreadyBound} already bound");

            // Short-circuit: nothing to do
            if (universalToBind.Length == 0 && disciplineToBind.Count == 0)
            {
                int totalAlready = pass1AlreadyBound + pass2AlreadyBound;
                string msg = $"All parameters are already bound — nothing to do.\n\n" +
                    $"{totalAlready} parameters already present in project.\n" +
                    $"\nSource: {spFile}";
                TaskDialog.Show("STING Tools - Load Shared Params", msg);
                StingLog.Info("LoadSharedParams: all params already bound, skipping");
                return Result.Succeeded;
            }

            int pass1Bound = 0;
            int pass1Skipped = 0;
            int pass2Bound = 0;
            int pass2Skipped = 0;
            var errors = new List<string>();
            int csvExtras = 0;

            // ── Pass 1: Universal parameters → all 53 categories (SINGLE transaction) ──
            // CRASH FIX: One transaction instead of 20+ batched transactions.
            // Multiple rapid tx.Commit() calls trigger Revit's deferred regeneration
            // which causes native segfaults (same root cause as ENH-003).
            if (universalToBind.Length > 0)
            {
                StingLog.Info($"Pass 1: binding {universalToBind.Length} universal params in single transaction");
                using (Transaction tx = new Transaction(doc, "STING Load Shared Params - Universal"))
                {
                    tx.Start();
                    try
                    {
                        foreach (string paramName in universalToBind)
                        {
                            if (!defLookup.TryGetValue(paramName, out ExternalDefinition extDef))
                            {
                                pass1Skipped++;
                                continue;
                            }

                            try
                            {
                                InstanceBinding binding = app.Create.NewInstanceBinding(allCats);
                                bool result = doc.ParameterBindings.Insert(
                                    extDef, binding, GroupTypeId.General);
                                if (!result)
                                    result = doc.ParameterBindings.ReInsert(
                                        extDef, binding, GroupTypeId.General);
                                if (result) pass1Bound++;
                                else pass1Skipped++;
                            }
                            catch (Exception ex)
                            {
                                pass1Skipped++;
                                errors.Add($"P1 {paramName}: {ex.Message}");
                            }
                        }

                        tx.Commit();
                        StingLog.Info($"Pass 1 committed: {pass1Bound} bound, {pass1Skipped} skipped");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error("Pass 1 transaction failed", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();
                    }
                }
            }

            // ── Pass 2: Discipline-specific parameters (SINGLE transaction) ──
            if (disciplineToBind.Count > 0)
            {
                StingLog.Info($"Pass 2: binding {disciplineToBind.Count} discipline params in single transaction");
                using (Transaction tx = new Transaction(doc, "STING Load Shared Params - Discipline"))
                {
                    tx.Start();
                    try
                    {
                        foreach (string paramName in disciplineToBind)
                        {
                            if (!defLookup.TryGetValue(paramName, out ExternalDefinition extDef))
                            {
                                pass2Skipped++;
                                continue;
                            }

                            try
                            {
                                CategorySet cats;
                                if (SharedParamGuids.DisciplineBindings.TryGetValue(paramName,
                                    out BuiltInCategory[] hardcodedCats))
                                {
                                    cats = SharedParamGuids.BuildCategorySet(doc, hardcodedCats);
                                }
                                else
                                {
                                    cats = new CategorySet();
                                }

                                if (csvBindings.TryGetValue(paramName, out var csvEntries))
                                {
                                    foreach (var entry in csvEntries)
                                    {
                                        if (Temp.TemplateManager.CategoryNameToEnum.TryGetValue(
                                            entry.category, out BuiltInCategory bic))
                                        {
                                            try
                                            {
                                                Category cat = doc.Settings.Categories.get_Item(bic);
                                                if (cat != null && cat.AllowsBoundParameters)
                                                {
                                                    if (!cats.Contains(cat))
                                                    {
                                                        cats.Insert(cat);
                                                        csvExtras++;
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                    }
                                }

                                // FIX: If no specific categories resolved (CSV-only param with
                                // unmapped category names), bind to all categories as fallback.
                                // This ensures all parameters in the shared param file get bound.
                                if (cats.Size == 0 && allCats.Size > 0)
                                {
                                    cats = allCats;
                                    csvExtras++;
                                }

                                if (cats.Size == 0)
                                {
                                    pass2Skipped++;
                                    continue;
                                }

                                InstanceBinding binding = app.Create.NewInstanceBinding(cats);
                                bool result = doc.ParameterBindings.Insert(
                                    extDef, binding, GroupTypeId.General);
                                if (!result)
                                    result = doc.ParameterBindings.ReInsert(
                                        extDef, binding, GroupTypeId.General);
                                if (result) pass2Bound++;
                                else pass2Skipped++;
                            }
                            catch (Exception ex)
                            {
                                pass2Skipped++;
                                errors.Add($"P2 {paramName}: {ex.Message}");
                            }
                        }

                        tx.Commit();
                        StingLog.Info($"Pass 2 committed: {pass2Bound} bound, {pass2Skipped} skipped");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error("Pass 2 transaction failed", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();
                    }
                }
            }

            // ── Report ──
            int universalCount = allUniversalParams.Length;
            int catCount = universalCatEnums?.Length ?? 0;
            string report = $"Shared parameter binding complete.\n\n" +
                $"Pass 1 (Universal):   {pass1Bound} bound, {pass1AlreadyBound} already present, {pass1Skipped} skipped  ({universalCount} params, {catCount} categories)\n" +
                $"Pass 2 (Discipline):  {pass2Bound} bound, {pass2AlreadyBound} already present, {pass2Skipped} skipped\n" +
                (csvExtras > 0 ? $"  CSV extras: {csvExtras} categories added from CATEGORY_BINDINGS.csv\n" : "") +
                $"\nSource: {spFile}";
            if (errors.Count > 0)
                report += $"\n\nErrors ({errors.Count}):\n" +
                    string.Join("\n", errors.Take(10));

            // Clear parameter lookup cache so newly bound parameters are found immediately
            ParameterHelpers.ClearParamCache();

            var td = new TaskDialog("STING Tools - Load Shared Params");
            td.MainInstruction = "Shared parameter binding complete.";
            td.MainContent = report;
            td.CommonButtons = TaskDialogCommonButtons.Ok;
            td.DefaultButton = TaskDialogResult.Ok;
            td.Show();
            StingLog.Info($"LoadSharedParams complete: P1={pass1Bound}, P2={pass2Bound}");

            return Result.Succeeded;
        }

    }
}
