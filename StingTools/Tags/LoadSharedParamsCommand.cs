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
    /// Ported from shared_params.py LoadSharedParams logic.
    /// Bind shared parameters (universal + discipline-specific) to project categories.
    /// Pass 1: 17 universal ASS_MNG parameters → all 53 categories (type-safe enum resolution).
    /// Pass 2: discipline-specific tag containers → correct category subsets
    ///         using DisciplineBindings map (no longer simplified — full discipline targeting).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class LoadSharedParamsCommand : IExternalCommand
    {
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
            // instead of scanning all groups × definitions per parameter
            var defIndex = BuildDefinitionIndex(defFile);
            StingLog.Info($"Definition index: {defIndex.Count} definitions loaded");

            int pass1Bound = 0;
            int pass1Skipped = 0;
            int pass2Bound = 0;
            int pass2Skipped = 0;
            var errors = new List<string>();

            // ── Pass 1: Universal parameters → all 53 categories ──
            try
            {
                using (Transaction tx = new Transaction(doc, "STING Load Universal Params"))
                {
                    tx.Start();

                    CategorySet allCats = SharedParamGuids.BuildCategorySet(
                        doc, SharedParamGuids.AllCategoryEnums);
                    StingLog.Info($"Pass 1: {allCats.Size} categories resolved");

                    foreach (string paramName in SharedParamGuids.UniversalParams)
                    {
                        if (!defIndex.TryGetValue(paramName, out ExternalDefinition extDef))
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
            }
            catch (Exception ex)
            {
                StingLog.Error("Pass 1 transaction failed", ex);
                errors.Add($"Pass 1 transaction: {ex.Message}");
            }

            // ── Pass 2: Discipline-specific parameters → category subsets ──
            try
            {
                using (Transaction tx = new Transaction(doc, "STING Load Discipline Params"))
                {
                    tx.Start();

                    // Use only hardcoded DisciplineBindings — no CSV file I/O
                    // during the critical binding path. CSV supplement is available
                    // separately via DynamicBindingsCommand.
                    foreach (var kvp in SharedParamGuids.DisciplineBindings)
                    {
                        string paramName = kvp.Key;

                        if (!defIndex.TryGetValue(paramName, out ExternalDefinition extDef))
                        {
                            pass2Skipped++;
                            StingLog.Warn($"Pass 2: Definition not found: {paramName}");
                            continue;
                        }

                        try
                        {
                            CategorySet cats = SharedParamGuids.BuildCategorySet(doc, kvp.Value);

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
            }
            catch (Exception ex)
            {
                StingLog.Error("Pass 2 transaction failed", ex);
                errors.Add($"Pass 2 transaction: {ex.Message}");
            }

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
