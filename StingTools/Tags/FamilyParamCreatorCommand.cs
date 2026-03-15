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
    // ════════════════════════════════════════════════════════════════════════════
    //  FAMILY PARAMETER CREATOR ENGINE
    //
    //  Injects STING shared parameters into .rfa family files, detects category,
    //  adds tag position parameter with formulas, and creates position variant types.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Engine for processing Revit family files: detecting categories, injecting
    /// shared parameters, formulas, tag anchor planes, and position variant types.
    /// </summary>
    internal static class FamilyParamEngine
    {
        /// <summary>Options for family processing.</summary>
        public class ProcessOptions
        {
            public bool InjectTagPos { get; set; } = true;
            public bool InjectFormulas { get; set; } = true;
            public bool PlaceAnchor { get; set; } = false;
            public bool CreatePositionTypes { get; set; } = true;
            public List<string> ParamNames { get; set; } = new List<string>();
        }

        /// <summary>Result of processing a single family file.</summary>
        public class FamilyResult
        {
            public string SourcePath { get; set; }
            public string OutputPath { get; set; }
            public string Category { get; set; }
            public string DiscCode { get; set; }
            public int ParamsAdded { get; set; }
            public int ParamsSkipped { get; set; }
            public bool TagPosInjected { get; set; }
            public int FormulasInjected { get; set; }
            public bool AnchorPlaced { get; set; }
            public int PositionTypesCreated { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// Detect the family's BuiltInCategory with secondary fingerprint for GenericModel.
        /// </summary>
        public static (BuiltInCategory cat, string catName, string discCode) DetectFamilyCategory(Document famDoc)
        {
            if (famDoc == null || !famDoc.IsFamilyDocument)
                return (BuiltInCategory.INVALID, "", "A");

            try
            {
                Category famCat = famDoc.OwnerFamily.FamilyCategory;
                if (famCat == null)
                    return (BuiltInCategory.INVALID, "Unknown", "A");

                string catName = famCat.Name ?? "";
                BuiltInCategory bic = (BuiltInCategory)famCat.Id.Value;

                // Secondary fingerprint for GenericModel families
                if (bic == BuiltInCategory.OST_GenericModel)
                {
                    try
                    {
                        var paramNames = famDoc.FamilyManager.GetParameters()
                            .Select(p => p.Definition.Name.ToUpperInvariant())
                            .ToList();

                        if (paramNames.Any(n => n.Contains("HVAC") || n.Contains("AHU") ||
                            n.Contains("FCU") || n.Contains("VAV") || n.Contains("FAN")))
                        {
                            return (BuiltInCategory.OST_MechanicalEquipment, "Mechanical Equipment", "M");
                        }
                        if (paramNames.Any(n => n.Contains("VOLTAGE") || n.Contains("CIRCUIT") ||
                            n.Contains("PANEL") || n.Contains("BREAKER")))
                        {
                            return (BuiltInCategory.OST_ElectricalFixtures, "Electrical Fixtures", "E");
                        }
                        if (paramNames.Any(n => n.Contains("FLOW") || n.Contains("PIPE") ||
                            n.Contains("DRAIN") || n.Contains("VALVE")))
                        {
                            return (BuiltInCategory.OST_PlumbingFixtures, "Plumbing Fixtures", "P");
                        }
                    }
                    catch { }
                }

                // Map to discipline code
                string disc = TagConfig.DiscMap.TryGetValue(catName, out string d) ? d : "A";
                return (bic, catName, disc);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DetectFamilyCategory: {ex.Message}");
                return (BuiltInCategory.INVALID, "Unknown", "A");
            }
        }

        /// <summary>
        /// Inject STING shared parameters into a family document.
        /// </summary>
        public static (int added, int skipped) InjectSharedParams(
            Document famDoc, Autodesk.Revit.ApplicationServices.Application app, List<string> paramNames)
        {
            int added = 0, skipped = 0;
            if (famDoc == null || app == null || paramNames == null) return (0, 0);

            try
            {
                string spFile = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                if (string.IsNullOrEmpty(spFile))
                {
                    StingLog.Warn("InjectSharedParams: MR_PARAMETERS.txt not found");
                    return (0, 0);
                }

                string origFile = app.SharedParametersFilename;
                app.SharedParametersFilename = spFile;

                try
                {
                    DefinitionFile defFile = app.OpenSharedParameterFile();
                    if (defFile == null) return (0, 0);

                    FamilyManager fm = famDoc.FamilyManager;
                    var existingNames = new HashSet<string>(
                        fm.GetParameters().Select(p => p.Definition.Name),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (string paramName in paramNames)
                    {
                        try
                        {
                            if (existingNames.Contains(paramName))
                            {
                                skipped++;
                                continue;
                            }

                            // Find the definition in shared param file
                            ExternalDefinition extDef = null;
                            foreach (DefinitionGroup group in defFile.Groups)
                            {
                                extDef = group.Definitions
                                    .Cast<ExternalDefinition>()
                                    .FirstOrDefault(d => d.Name == paramName);
                                if (extDef != null) break;
                            }

                            if (extDef == null)
                            {
                                skipped++;
                                continue;
                            }

                            bool isInstance = paramName != ParamRegistry.TAG_POS;
                            fm.AddParameter(extDef,
                                GroupTypeId.General,
                                isInstance);
                            added++;
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"InjectSharedParams '{paramName}': {ex.Message}");
                            skipped++;
                        }
                    }
                }
                finally
                {
                    try { app.SharedParametersFilename = origFile; }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("InjectSharedParams", ex);
            }

            return (added, skipped);
        }

        /// <summary>
        /// Inject tag position formulas into the family document.
        /// </summary>
        public static int InjectTagPosFormulas(Document famDoc)
        {
            int injected = 0;
            if (famDoc == null) return 0;

            try
            {
                FamilyManager fm = famDoc.FamilyManager;
                var tagPosParam = fm.GetParameters()
                    .FirstOrDefault(p => p.Definition.Name == ParamRegistry.TAG_POS);
                if (tagPosParam == null) return 0;

                // Check for required formula target params
                var paramNames = fm.GetParameters()
                    .Select(p => p.Definition.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                string[] formulaPairs = {
                    "Label_Offset_X", "if(STING_TAG_POS = 2, 0.01, if(STING_TAG_POS = 4, -0.01, 0))",
                    "Label_Offset_Y", "if(STING_TAG_POS = 1, 0.01, if(STING_TAG_POS = 3, -0.01, 0))"
                };

                for (int i = 0; i < formulaPairs.Length; i += 2)
                {
                    string targetParam = formulaPairs[i];
                    string formula = formulaPairs[i + 1];

                    if (!paramNames.Contains(targetParam)) continue;

                    try
                    {
                        var param = fm.GetParameters()
                            .FirstOrDefault(p => p.Definition.Name == targetParam);
                        if (param != null)
                        {
                            fm.SetFormula(param, formula);
                            injected++;
                            StingLog.Info($"InjectTagPosFormulas: set formula on '{targetParam}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"InjectTagPosFormulas '{targetParam}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("InjectTagPosFormulas", ex);
            }

            return injected;
        }

        /// <summary>
        /// Create named position types in the family document (Item 13).
        /// </summary>
        public static int InjectPositionTypes(Document famDoc)
        {
            int created = 0;
            if (famDoc == null) return 0;

            try
            {
                FamilyManager fm = famDoc.FamilyManager;
                var tagPosParam = fm.GetParameters()
                    .FirstOrDefault(p => p.Definition.Name == ParamRegistry.TAG_POS);
                if (tagPosParam == null) return 0;

                var existingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FamilyType ft in fm.Types)
                {
                    if (ft != null && !string.IsNullOrEmpty(ft.Name))
                        existingTypes.Add(ft.Name);
                }

                string[] posNames = { "Above", "Right", "Below", "Left" };
                for (int pos = 1; pos <= posNames.Length; pos++)
                {
                    string typeName = $"2.5mm - {posNames[pos - 1]}";
                    if (existingTypes.Contains(typeName)) continue;

                    try
                    {
                        FamilyType newType = fm.NewType(typeName);
                        fm.CurrentType = newType;
                        fm.Set(tagPosParam, pos);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"InjectPositionTypes '{typeName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("InjectPositionTypes", ex);
            }

            return created;
        }

        /// <summary>
        /// Process a single family file: detect category, inject params, formulas, position types.
        /// </summary>
        public static FamilyResult ProcessFamily(
            Autodesk.Revit.ApplicationServices.Application app,
            string rfaPath, string outputPath, ProcessOptions opts)
        {
            var result = new FamilyResult
            {
                SourcePath = rfaPath,
                OutputPath = outputPath
            };

            Document famDoc = null;
            try
            {
                if (!File.Exists(rfaPath))
                {
                    result.ErrorMessage = "File not found";
                    return result;
                }

                famDoc = app.OpenDocumentFile(rfaPath);
                if (famDoc == null || !famDoc.IsFamilyDocument)
                {
                    result.ErrorMessage = "Not a valid family document";
                    if (famDoc != null) famDoc.Close(false);
                    return result;
                }

                var (cat, catName, discCode) = DetectFamilyCategory(famDoc);
                result.Category = catName;
                result.DiscCode = discCode;

                using (Transaction tx = new Transaction(famDoc, "STING Family Param Creator"))
                {
                    tx.Start();

                    // Inject shared parameters
                    var paramList = opts.ParamNames.Count > 0
                        ? opts.ParamNames
                        : GetDefaultParamsForCategory(catName, discCode);
                    var (added, skipped) = InjectSharedParams(famDoc, app, paramList);
                    result.ParamsAdded = added;
                    result.ParamsSkipped = skipped;

                    // Inject TAG_POS
                    if (opts.InjectTagPos)
                    {
                        try
                        {
                            var tagPosList = new List<string> { ParamRegistry.TAG_POS };
                            var (tpAdded, _) = InjectSharedParams(famDoc, app, tagPosList);
                            result.TagPosInjected = tpAdded > 0;
                        }
                        catch { }
                    }

                    // Inject formulas
                    if (opts.InjectFormulas && result.TagPosInjected)
                    {
                        result.FormulasInjected = InjectTagPosFormulas(famDoc);
                    }

                    // Create position types (Item 13)
                    if (opts.CreatePositionTypes && result.TagPosInjected)
                    {
                        result.PositionTypesCreated = InjectPositionTypes(famDoc);
                    }

                    tx.Commit();
                }

                // Save
                if (!string.IsNullOrEmpty(outputPath))
                {
                    var saveOpts = new SaveAsOptions
                    {
                        OverwriteExistingFile = true,
                        MaximumBackups = 1
                    };
                    famDoc.SaveAs(outputPath, saveOpts);
                }
                else
                {
                    famDoc.Save();
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                StingLog.Error($"ProcessFamily '{rfaPath}'", ex);
            }
            finally
            {
                try { famDoc?.Close(false); }
                catch { }
            }

            return result;
        }

        /// <summary>Get default parameter names for a category/discipline.</summary>
        private static List<string> GetDefaultParamsForCategory(string catName, string discCode)
        {
            var list = new List<string>
            {
                ParamRegistry.DISC, ParamRegistry.LOC, ParamRegistry.ZONE,
                ParamRegistry.LVL, ParamRegistry.SYS, ParamRegistry.FUNC,
                ParamRegistry.PROD, ParamRegistry.SEQ, ParamRegistry.STATUS
            };

            // Add TAG container param names
            var containers = ParamRegistry.AllContainers;
            if (containers != null)
            {
                foreach (var c in containers)
                {
                    if (c != null && !string.IsNullOrEmpty(c.ParamName) && !list.Contains(c.ParamName))
                        list.Add(c.ParamName);
                }
            }

            return list;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  FAMILY PARAMETER CREATOR COMMAND
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Inject STING shared parameters into .rfa family files with category detection,
    /// tag position formulas, and named position variant types.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class FamilyParamCreatorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

            var app = ctx.App.Application;

            // Mode selection
            var modeTd = new TaskDialog("STING — Family Param Creator");
            modeTd.MainInstruction = "Family Parameter Injection Mode";
            modeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Single family", "Process one .rfa file");
            modeTd.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Batch folder", "Process all .rfa files in a folder");
            modeTd.CommonButtons = TaskDialogCommonButtons.Cancel;
            var modeResult = modeTd.Show();

            if (modeResult != TaskDialogResult.CommandLink1 &&
                modeResult != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            bool isBatch = modeResult == TaskDialogResult.CommandLink2;

            // Options
            var opts = new FamilyParamEngine.ProcessOptions
            {
                InjectTagPos = true,
                InjectFormulas = true,
                CreatePositionTypes = true
            };

            // Get file(s) to process
            List<string> rfaFiles;
            string outputDir;

            if (isBatch)
            {
                // Use folder dialog via a simple path prompt
                string searchDir = StingToolsApp.DataPath ?? "";
                if (string.IsNullOrEmpty(searchDir) || !Directory.Exists(searchDir))
                {
                    TaskDialog.Show("STING", "No valid data directory found for batch processing.");
                    return Result.Failed;
                }
                rfaFiles = Directory.GetFiles(searchDir, "*.rfa", SearchOption.AllDirectories).ToList();
                outputDir = searchDir;
            }
            else
            {
                // For single file, look for .rfa in data path
                string searchDir = StingToolsApp.DataPath ?? "";
                rfaFiles = Directory.Exists(searchDir)
                    ? Directory.GetFiles(searchDir, "*.rfa", SearchOption.TopDirectoryOnly).Take(1).ToList()
                    : new List<string>();
                outputDir = searchDir;
            }

            if (rfaFiles.Count == 0)
            {
                TaskDialog.Show("STING", "No .rfa family files found to process.");
                return Result.Succeeded;
            }

            // Process
            var results = new List<FamilyParamEngine.FamilyResult>();
            int processed = 0;

            foreach (string rfaPath in rfaFiles)
            {
                if (processed % 5 == 0 && EscapeChecker.IsEscapePressed())
                {
                    TaskDialog.Show("STING", $"Cancelled after processing {processed} of {rfaFiles.Count} files.");
                    break;
                }

                string outPath = rfaPath; // overwrite in-place
                var result = FamilyParamEngine.ProcessFamily(app, rfaPath, outPath, opts);
                results.Add(result);
                processed++;
            }

            // Report
            int succeeded = results.Count(r => r.Success);
            int failed = results.Count(r => !r.Success);
            int totalParamsAdded = results.Sum(r => r.ParamsAdded);
            var catBreakdown = results.Where(r => r.Success)
                .GroupBy(r => r.Category)
                .Select(g => $"  {g.Key}: {g.Count()}")
                .ToList();

            var report = new StringBuilder();
            report.AppendLine($"Files: {processed} processed, {succeeded} succeeded, {failed} failed");
            report.AppendLine($"Parameters added: {totalParamsAdded}");
            report.AppendLine($"Position types created: {results.Sum(r => r.PositionTypesCreated)}");
            if (catBreakdown.Count > 0)
            {
                report.AppendLine("\nCategories:");
                foreach (string line in catBreakdown)
                    report.AppendLine(line);
            }

            // Write CSV log
            try
            {
                string logPath = Path.Combine(outputDir,
                    $"STING_FamilyParamCreator_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var csv = new StringBuilder();
                csv.AppendLine("SourcePath,OutputPath,Category,DiscCode,ParamsAdded,ParamsSkipped," +
                    "TagPosInjected,FormulasInjected,PositionTypesCreated,Status,ErrorMessage");
                foreach (var r in results)
                {
                    csv.AppendLine($"\"{r.SourcePath}\",\"{r.OutputPath}\",\"{r.Category}\"," +
                        $"{r.DiscCode},{r.ParamsAdded},{r.ParamsSkipped},{r.TagPosInjected}," +
                        $"{r.FormulasInjected},{r.PositionTypesCreated}," +
                        $"{(r.Success ? "OK" : "FAILED")},\"{r.ErrorMessage ?? ""}\"");
                }
                File.WriteAllText(logPath, csv.ToString());
                report.AppendLine($"\nLog: {logPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FamilyParamCreator CSV log: {ex.Message}");
            }

            if (failed > 0)
            {
                report.AppendLine("\nFailed files:");
                foreach (var r in results.Where(r => !r.Success).Take(10))
                    report.AppendLine($"  {Path.GetFileName(r.SourcePath)}: {r.ErrorMessage}");
            }

            TaskDialog.Show("STING — Family Param Creator", report.ToString());
            StingLog.Info($"FamilyParamCreator: {succeeded}/{processed} succeeded, {totalParamsAdded} params added");
            return Result.Succeeded;
        }
    }
}
