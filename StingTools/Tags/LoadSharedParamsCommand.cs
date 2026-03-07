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
    /// IMPORTANT: Parameters are bound in small batches (≤10 per transaction) with
    /// doc.Regenerate() between batches. Binding many parameters in a single transaction
    /// causes Revit's native parameter database (ExternalParamDatabase.cpp) to crash
    /// during internal DOPT processing. Batching avoids this.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadSharedParamsCommand : IExternalCommand
    {
        /// <summary>Max parameters to bind per transaction before regenerating.</summary>
        private const int BatchSize = 10;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            UIApplication uiApp;
            try
            {
                uiApp = ParameterHelpers.GetApp(commandData);
            }
            catch (Exception ex)
            {
                StingLog.Error("LoadSharedParams: cannot get UIApplication", ex);
                return Result.Failed;
            }

            if (uiApp.ActiveUIDocument == null)
            {
                TaskDialog.Show("Load Shared Params", "No document is open.");
                return Result.Failed;
            }

            Document doc = uiApp.ActiveUIDocument.Document;
            Autodesk.Revit.ApplicationServices.Application app =
                uiApp.Application;

            string spFile = app.SharedParametersFilename;
            if (string.IsNullOrEmpty(spFile) || !File.Exists(spFile))
            {
                TaskDialog.Show("Load Shared Params",
                    "No shared parameter file is set in Revit.\n\n" +
                    "Go to Manage → Shared Parameters and set the file path to " +
                    "MR_PARAMETERS.txt first.");
                return Result.Failed;
            }

            DefinitionFile defFile;
            try
            {
                defFile = app.OpenSharedParameterFile();
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to open shared parameter file", ex);
                TaskDialog.Show("Load Shared Params",
                    "Error opening shared parameter file:\n" + spFile +
                    "\n\n" + ex.Message);
                return Result.Failed;
            }

            if (defFile == null)
            {
                TaskDialog.Show("Load Shared Params",
                    "Could not open shared parameter file:\n" + spFile);
                return Result.Failed;
            }

            // Pre-index all definitions once — O(n) build, O(1) lookups
            var defIndex = BuildDefinitionIndex(defFile);
            StingLog.Info($"Definition index: {defIndex.Count} definitions loaded");

            int pass1Bound = 0;
            int pass1Skipped = 0;
            int pass2Bound = 0;
            int pass2Skipped = 0;
            var errors = new List<string>();

            // ── Pass 1: Universal parameters → all 53 categories ──
            // Bind in batches to avoid native crash in Revit's parameter database
            StingLog.Info("Pass 1: starting universal parameter binding");
            try
            {
                CategorySet allCats = SharedParamGuids.BuildCategorySet(
                    doc, SharedParamGuids.AllCategoryEnums);
                StingLog.Info($"Pass 1: {allCats.Size} categories resolved");

                string[] universalParams = SharedParamGuids.UniversalParams;
                for (int batchStart = 0; batchStart < universalParams.Length; batchStart += BatchSize)
                {
                    int batchEnd = Math.Min(batchStart + BatchSize, universalParams.Length);
                    StingLog.Info($"Pass 1: binding batch {batchStart + 1}–{batchEnd} of {universalParams.Length}");

                    using (Transaction tx = new Transaction(doc,
                        $"STING Load Universal Params ({batchStart + 1}-{batchEnd})"))
                    {
                        tx.Start();

                        for (int i = batchStart; i < batchEnd; i++)
                        {
                            string paramName = universalParams[i];
                            if (!defIndex.TryGetValue(paramName, out ExternalDefinition extDef))
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
                                {
                                    result = doc.ParameterBindings.ReInsert(
                                        extDef, binding, GroupTypeId.General);
                                }
                                if (result)
                                    pass1Bound++;
                                else
                                    pass1Skipped++;
                            }
                            catch (Exception ex)
                            {
                                pass1Skipped++;
                                errors.Add($"P1 {paramName}: {ex.Message}");
                                StingLog.Error($"Pass 1 bind failed: {paramName}", ex);
                            }
                        }

                        tx.Commit();
                    }

                    // Let Revit process internal state between batches
                    doc.Regenerate();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Pass 1 failed", ex);
                errors.Add($"Pass 1: {ex.Message}");
            }

            StingLog.Info($"Pass 1 complete: {pass1Bound} bound, {pass1Skipped} skipped");

            // ── Pass 2: Discipline-specific parameters → category subsets ──
            // Also batched to prevent native crashes
            StingLog.Info("Pass 2: starting discipline parameter binding");
            try
            {
                var disciplineEntries = SharedParamGuids.DisciplineBindings.ToArray();
                for (int batchStart = 0; batchStart < disciplineEntries.Length; batchStart += BatchSize)
                {
                    int batchEnd = Math.Min(batchStart + BatchSize, disciplineEntries.Length);
                    StingLog.Info($"Pass 2: binding batch {batchStart + 1}–{batchEnd} of {disciplineEntries.Length}");

                    using (Transaction tx = new Transaction(doc,
                        $"STING Load Discipline Params ({batchStart + 1}-{batchEnd})"))
                    {
                        tx.Start();

                        for (int i = batchStart; i < batchEnd; i++)
                        {
                            string paramName = disciplineEntries[i].Key;
                            BuiltInCategory[] catEnums = disciplineEntries[i].Value;

                            if (!defIndex.TryGetValue(paramName, out ExternalDefinition extDef))
                            {
                                pass2Skipped++;
                                continue;
                            }

                            try
                            {
                                CategorySet cats = SharedParamGuids.BuildCategorySet(doc, catEnums);
                                if (cats.Size == 0)
                                {
                                    pass2Skipped++;
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
                                if (result)
                                    pass2Bound++;
                                else
                                    pass2Skipped++;
                            }
                            catch (Exception ex)
                            {
                                pass2Skipped++;
                                errors.Add($"P2 {paramName}: {ex.Message}");
                                StingLog.Error($"Pass 2 bind failed: {paramName}", ex);
                            }
                        }

                        tx.Commit();
                    }

                    // Let Revit process internal state between batches
                    doc.Regenerate();
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Pass 2 failed", ex);
                errors.Add($"Pass 2: {ex.Message}");
            }

            StingLog.Info($"Pass 2 complete: {pass2Bound} bound, {pass2Skipped} skipped");

            // Clear stale parameter cache so subsequent commands find newly-bound params
            ParameterHelpers.ClearParamCache();
            StingLog.Info("Parameter lookup cache cleared after binding");

            string report = $"Shared parameter binding complete.\n\n" +
                $"Pass 1 (Universal):   {pass1Bound} bound, {pass1Skipped} skipped\n" +
                $"Pass 2 (Discipline):  {pass2Bound} bound, {pass2Skipped} skipped\n" +
                $"\nSource: {spFile}";
            if (errors.Count > 0)
                report += $"\n\nErrors ({errors.Count}):\n" +
                    string.Join("\n", errors.Take(10));

            TaskDialog.Show("Load Shared Params", report);
            StingLog.Info($"LoadSharedParams: P1={pass1Bound}, P2={pass2Bound}");

            return Result.Succeeded;
        }

        /// <summary>
        /// Build a dictionary of parameter name → ExternalDefinition from the shared
        /// parameter file. Scanned once upfront for O(1) lookups per parameter.
        /// </summary>
        private static Dictionary<string, ExternalDefinition> BuildDefinitionIndex(
            DefinitionFile defFile)
        {
            var index = new Dictionary<string, ExternalDefinition>(
                StringComparer.Ordinal);
            try
            {
                foreach (DefinitionGroup group in defFile.Groups)
                {
                    foreach (Definition def in group.Definitions)
                    {
                        if (def is ExternalDefinition extDef &&
                            !index.ContainsKey(extDef.Name))
                        {
                            index[extDef.Name] = extDef;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("Failed to index definition file", ex);
            }
            return index;
        }
    }
}
