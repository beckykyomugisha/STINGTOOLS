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
    /// Pass 1: universal ASS_MNG parameters → all 53 categories.
    /// Pass 2: discipline-specific tag containers → correct category subsets.
    ///
    /// CRASH FIX: No TransactionGroup, no SilentWarningSwallower, small batch size.
    /// Each batch is an independent Transaction so Revit regenerates between batches.
    /// This prevents the native access violations caused by accumulating hundreds of
    /// schema modifications before Revit can process them.
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

        /// <summary>
        /// CRASH FIX: Reduced from 25 to 10 parameters per transaction.
        /// Smaller batches = less native regeneration pressure per commit.
        /// </summary>
        private const int BindingBatchSize = 10;

        private Result ExecuteCore(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uiApp = ParameterHelpers.GetApp(commandData);
            Document doc = uiApp.ActiveUIDocument.Document;
            Autodesk.Revit.ApplicationServices.Application app =
                uiApp.Application;

            string spFile = app.SharedParametersFilename;
            if (string.IsNullOrEmpty(spFile) || !File.Exists(spFile))
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "No shared parameter file is set in Revit.\n\n" +
                    "Go to Manage → Shared Parameters and set the file path to " +
                    "MR_PARAMETERS.txt first.");
                return Result.Failed;
            }

            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                TaskDialog.Show("STING Tools - Load Shared Params",
                    "Could not open shared parameter file:\n" + spFile);
                return Result.Failed;
            }

            int pass1Bound = 0;
            int pass1Skipped = 0;
            int pass2Bound = 0;
            int pass2Skipped = 0;
            var errors = new List<string>();
            int csvExtras = 0;

            // Build category set ONCE outside transactions (read-only)
            StingLog.Info($"Pass 1: UniversalParams has {SharedParamGuids.UniversalParams?.Length ?? 0} entries, " +
                $"AllCategoryEnums has {SharedParamGuids.AllCategoryEnums?.Length ?? 0} entries");
            CategorySet allCats = SharedParamGuids.BuildCategorySet(
                doc, SharedParamGuids.AllCategoryEnums);
            StingLog.Info($"Pass 1: {allCats.Size} categories resolved, {SharedParamGuids.UniversalParams?.Length ?? 0} params to bind");

            if (allCats.Size == 0)
            {
                StingLog.Warn("No categories resolved — PARAMETER_REGISTRY.json may be missing. " +
                    $"DataPath: {StingToolsApp.DataPath}");
            }

            // Load CSV bindings ONCE outside transactions
            Dictionary<string, List<(string category, string bindingType, bool isShared)>> csvBindings;
            try
            {
                csvBindings = Temp.TemplateManager.LoadCategoryBindings();
            }
            catch (Exception ex)
            {
                csvBindings = new Dictionary<string, List<(string, string, bool)>>();
                StingLog.Warn($"LoadCategoryBindings failed (continuing without CSV): {ex.Message}");
            }

            // CRASH FIX: No TransactionGroup wrapper.  Each batch is a standalone
            // Transaction so Revit fully regenerates between batches.  This avoids
            // the native access violations caused by TransactionGroup accumulating
            // hundreds of schema modifications before allowing regeneration.

            // ── Pass 1: Universal parameters → all 53 categories ──
            var universalParams = SharedParamGuids.UniversalParams ?? Array.Empty<string>();
            for (int batchStart = 0; batchStart < universalParams.Length; batchStart += BindingBatchSize)
            {
                int batchEnd = Math.Min(batchStart + BindingBatchSize, universalParams.Length);
                int batchNum = (batchStart / BindingBatchSize) + 1;

                using (Transaction tx = new Transaction(doc,
                    $"STING Params P1 batch {batchNum}"))
                {
                    // CRASH FIX: No SilentWarningSwallower.  Swallowing warnings
                    // can leave Revit's failure processing in a bad state, causing
                    // delayed native crashes.  Let Revit handle warnings naturally.

                    tx.Start();

                    try
                    {
                        for (int i = batchStart; i < batchEnd; i++)
                        {
                            string paramName = universalParams[i];
                            ExternalDefinition extDef = FindDefinition(defFile, paramName);
                            if (extDef == null)
                            {
                                pass1Skipped++;
                                StingLog.Warn($"Pass 1: Definition not found: {paramName}");
                                continue;
                            }

                            try
                            {
                                InstanceBinding binding = app.Create.NewInstanceBinding(allCats);
                                bool result = doc.ParameterBindings.Insert(
                                    extDef, binding, GroupTypeId.General);
                                if (!result)
                                {
                                    result = doc.ParameterBindings.ReInsert(
                                        extDef, binding, GroupTypeId.General);
                                }
                                if (result) pass1Bound++;
                                else pass1Skipped++;
                            }
                            catch (Exception ex)
                            {
                                pass1Skipped++;
                                errors.Add($"P1 {paramName}: {ex.Message}");
                                StingLog.Error($"Pass 1 bind failed: {paramName}", ex);
                            }
                        }

                        tx.Commit();
                        StingLog.Info($"Pass 1 batch {batchNum} committed ({pass1Bound} bound so far)");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"Pass 1 batch {batchNum} failed", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();
                    }
                }
            }

            // ── Pass 2: Discipline-specific parameters → correct category subsets ──
            var allDisciplineParams = new HashSet<string>(
                SharedParamGuids.DisciplineBindings.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (string csvParam in csvBindings.Keys)
                allDisciplineParams.Add(csvParam);

            var disciplineParamList = allDisciplineParams.ToList();
            csvExtras = 0;

            for (int batchStart = 0; batchStart < disciplineParamList.Count; batchStart += BindingBatchSize)
            {
                int batchEnd = Math.Min(batchStart + BindingBatchSize, disciplineParamList.Count);
                int batchNum = (batchStart / BindingBatchSize) + 1;

                using (Transaction tx = new Transaction(doc,
                    $"STING Params P2 batch {batchNum}"))
                {
                    tx.Start();

                    try
                    {
                        for (int i = batchStart; i < batchEnd; i++)
                        {
                            string paramName = disciplineParamList[i];
                            ExternalDefinition extDef = FindDefinition(defFile, paramName);
                            if (extDef == null)
                            {
                                pass2Skipped++;
                                StingLog.Warn($"Pass 2: Definition not found: {paramName}");
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

                                if (cats.Size == 0)
                                {
                                    pass2Skipped++;
                                    StingLog.Warn($"Pass 2: No valid categories for {paramName}");
                                    continue;
                                }

                                InstanceBinding binding = app.Create.NewInstanceBinding(cats);
                                bool result = doc.ParameterBindings.Insert(
                                    extDef, binding, GroupTypeId.General);
                                if (!result)
                                {
                                    result = doc.ParameterBindings.ReInsert(
                                        extDef, binding, GroupTypeId.General);
                                }
                                if (result) pass2Bound++;
                                else pass2Skipped++;
                            }
                            catch (Exception ex)
                            {
                                pass2Skipped++;
                                errors.Add($"P2 {paramName}: {ex.Message}");
                                StingLog.Error($"Pass 2 bind failed: {paramName}", ex);
                            }
                        }

                        tx.Commit();
                        StingLog.Info($"Pass 2 batch {batchNum} committed ({pass2Bound} bound so far)");
                    }
                    catch (Exception ex)
                    {
                        StingLog.Error($"Pass 2 batch {batchNum} failed", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();
                    }
                }
            }

            int universalCount = SharedParamGuids.UniversalParams?.Length ?? 0;
            int catCount = SharedParamGuids.AllCategoryEnums?.Length ?? 0;
            string report = $"Shared parameter binding complete.\n\n" +
                $"Pass 1 (Universal):   {pass1Bound} bound, {pass1Skipped} skipped  ({universalCount} params, {catCount} categories)\n" +
                $"Pass 2 (Discipline):  {pass2Bound} bound, {pass2Skipped} skipped\n" +
                (csvExtras > 0 ? $"  CSV extras: {csvExtras} categories added from CATEGORY_BINDINGS.csv\n" : "") +
                $"\nSource: {spFile}";
            if (errors.Count > 0)
                report += $"\n\nErrors ({errors.Count}):\n" +
                    string.Join("\n", errors.Take(10));

            TaskDialog.Show("STING Tools - Load Shared Params", report);
            StingLog.Info($"LoadSharedParams: P1={pass1Bound}, P2={pass2Bound}");

            return Result.Succeeded;
        }

        private static ExternalDefinition FindDefinition(DefinitionFile defFile, string name)
        {
            foreach (DefinitionGroup group in defFile.Groups)
            {
                foreach (Definition def in group.Definitions)
                {
                    if (def.Name == name && def is ExternalDefinition extDef)
                        return extDef;
                }
            }
            return null;
        }
    }
}
