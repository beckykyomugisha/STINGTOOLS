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
            Document doc = commandData.Application.ActiveUIDocument.Document;
            Autodesk.Revit.ApplicationServices.Application app =
                commandData.Application.Application;

            string spFile = app.SharedParametersFilename;
            if (string.IsNullOrEmpty(spFile) || !File.Exists(spFile))
            {
                TaskDialog.Show("Load Shared Params",
                    "No shared parameter file is set in Revit.\n\n" +
                    "Go to Manage → Shared Parameters and set the file path to " +
                    "MR_PARAMETERS.txt first.");
                return Result.Failed;
            }

            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                TaskDialog.Show("Load Shared Params",
                    "Could not open shared parameter file:\n" + spFile);
                return Result.Failed;
            }

            int pass1Bound = 0;
            int pass1Skipped = 0;
            int pass2Bound = 0;
            int pass2Skipped = 0;
            var errors = new List<string>();

            using (Transaction tx = new Transaction(doc, "STING Load Shared Params"))
            {
                tx.Start();

                // Pass 1: Universal parameters → all 53 categories (type-safe)
                CategorySet allCats = SharedParamGuids.BuildCategorySet(
                    doc, SharedParamGuids.AllCategoryEnums);
                StingLog.Info($"Pass 1: {allCats.Size} categories resolved");

                foreach (string paramName in SharedParamGuids.UniversalParams)
                {
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
                            // Already bound — update with ReInsert to add new categories
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

                // GAP-003: Load CSV-driven category bindings to supplement hardcoded DisciplineBindings
                var csvBindings = Temp.TemplateManager.LoadCategoryBindings();
                int csvExtras = 0;

                // Pass 2: Discipline-specific parameters → correct category subsets
                // Merge hardcoded DisciplineBindings with any extra categories from CSV
                var allDisciplineParams = new HashSet<string>(
                    SharedParamGuids.DisciplineBindings.Keys, StringComparer.OrdinalIgnoreCase);
                foreach (string csvParam in csvBindings.Keys)
                    allDisciplineParams.Add(csvParam);

                foreach (string paramName in allDisciplineParams)
                {
                    ExternalDefinition extDef = FindDefinition(defFile, paramName);
                    if (extDef == null)
                    {
                        pass2Skipped++;
                        StingLog.Warn($"Pass 2: Definition not found: {paramName}");
                        continue;
                    }

                    try
                    {
                        // Start with hardcoded categories
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

                        // Merge CSV categories (GAP-003: data-driven supplement)
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
