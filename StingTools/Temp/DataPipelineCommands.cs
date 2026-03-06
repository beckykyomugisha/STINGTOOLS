using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Temp
{
    // ════════════════════════════════════════════════════════════════════
    //  Validate Template — C# port of VALIDAT_BIM_TEMPLATE.py (45 checks)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// BIM template validation command — ports the key checks from VALIDAT_BIM_TEMPLATE.py.
    /// Validates data file integrity, parameter consistency, material completeness,
    /// formula dependencies, schedule definitions, and cross-references.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateTemplateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            string dataPath = StingToolsApp.DataPath;
            if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
            {
                TaskDialog.Show("Validate Template",
                    $"Data directory not found: {dataPath}");
                return Result.Failed;
            }

            var results = new List<ValidationResult>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Run all check groups
            CheckDataFileInventory(dataPath, results);
            CheckParameterFile(dataPath, results);
            CheckBindingCoverage(dataPath, results);
            CheckMaterialLibraries(dataPath, results);
            CheckScheduleDefinitions(dataPath, results);
            CheckFormulaDependencies(dataPath, results);
            CheckSchemaConsistency(dataPath, results);
            CheckCategoryBindings(dataPath, results);
            CheckRemapTable(dataPath, results);
            CheckProjectState(doc, results);

            sw.Stop();

            // Summarise
            int passed = results.Count(r => r.Passed);
            int failed = results.Count(r => !r.Passed);
            int critical = results.Count(r => !r.Passed && r.Severity == "CRITICAL");
            int moderate = results.Count(r => !r.Passed && r.Severity == "MODERATE");

            var report = new StringBuilder();
            report.AppendLine("BIM Template Validation Report");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Checks run: {results.Count}");
            report.AppendLine($"  Passed:     {passed}");
            report.AppendLine($"  Failed:     {failed}");
            if (critical > 0) report.AppendLine($"  CRITICAL:   {critical}");
            if (moderate > 0) report.AppendLine($"  MODERATE:   {moderate}");
            report.AppendLine($"  Time:       {sw.Elapsed.TotalSeconds:F1}s");
            report.AppendLine();

            // Show failures
            if (failed > 0)
            {
                report.AppendLine("── FAILURES ──");
                foreach (var r in results.Where(r => !r.Passed)
                    .OrderByDescending(r => r.Severity == "CRITICAL")
                    .ThenByDescending(r => r.Severity == "MODERATE"))
                {
                    report.AppendLine($"  [{r.Severity}] {r.CheckName}");
                    report.AppendLine($"           {r.Detail}");
                }
            }

            report.AppendLine();
            report.AppendLine("── ALL CHECKS ──");
            foreach (var r in results)
            {
                string status = r.Passed ? "PASS" : "FAIL";
                report.AppendLine($"  [{status}] {r.CheckName} — {r.Detail}");
            }

            // Export to CSV
            try
            {
                string dir = Path.GetDirectoryName(doc.PathName);
                string csvPath = Path.Combine(
                    string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir,
                    $"STING_Validation_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var csvLines = new List<string> { "Check,Severity,Status,Detail" };
                foreach (var r in results)
                    csvLines.Add($"\"{r.CheckName}\",\"{r.Severity}\",{(r.Passed ? "PASS" : "FAIL")},\"{r.Detail}\"");
                File.WriteAllLines(csvPath, csvLines);
                report.AppendLine($"\nCSV exported: {csvPath}");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Validation CSV export: {ex.Message}");
            }

            TaskDialog td = new TaskDialog("Validate Template");
            td.MainInstruction = $"Validation: {passed}/{results.Count} checks passed" +
                (critical > 0 ? $" ({critical} CRITICAL)" : "");
            td.MainContent = report.ToString();
            td.Show();

            StingLog.Info($"ValidateTemplate: {passed}/{results.Count} passed, " +
                $"critical={critical}, time={sw.Elapsed.TotalSeconds:F1}s");
            return Result.Succeeded;
        }

        // ── Check implementations ──────────────────────────────────────

        private void CheckDataFileInventory(string dataPath, List<ValidationResult> results)
        {
            string[] requiredFiles =
            {
                "BLE_MATERIALS.csv", "MEP_MATERIALS.csv", "MR_PARAMETERS.txt",
                "MR_PARAMETERS.csv", "MR_SCHEDULES.csv", "MATERIAL_SCHEMA.json",
                "FORMULAS_WITH_DEPENDENCIES.csv", "SCHEDULE_FIELD_REMAP.csv",
                "BINDING_COVERAGE_MATRIX.csv", "CATEGORY_BINDINGS.csv"
            };

            int found = 0;
            var missing = new List<string>();
            foreach (string f in requiredFiles)
            {
                string path = StingToolsApp.FindDataFile(f);
                if (path != null && File.Exists(path))
                    found++;
                else
                    missing.Add(f);
            }

            results.Add(new ValidationResult(
                "Data file inventory", "CRITICAL",
                missing.Count == 0,
                missing.Count == 0
                    ? $"All {requiredFiles.Length} files present"
                    : $"Missing: {string.Join(", ", missing)}"));
        }

        /// <summary>Safe file reader — returns null and records error on IO failure.</summary>
        private string[] SafeReadAllLines(string path, string checkName, List<ValidationResult> results)
        {
            try
            {
                return File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(checkName, "CRITICAL", false,
                    $"Failed to read file: {ex.Message}"));
                StingLog.Error($"SafeReadAllLines({path}): {ex.Message}", ex);
                return null;
            }
        }

        private void CheckParameterFile(string dataPath, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (path == null)
            {
                results.Add(new ValidationResult("Parameter file", "CRITICAL", false, "MR_PARAMETERS.txt not found"));
                return;
            }

            var lines = SafeReadAllLines(path, "Parameter file read", results);
            if (lines == null) return;
            int paramCount = lines.Count(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#") && !l.TrimStart().StartsWith("*"));
            results.Add(new ValidationResult("Parameter count", "MODERATE",
                paramCount >= 50,
                $"{paramCount} parameters defined (expected 50+)"));

            // Check for GUID format
            int validGuids = 0;
            foreach (string line in lines)
            {
                if (line.Contains("\t"))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 2)
                    {
                        if (Guid.TryParse(parts[0].Trim(), out _))
                            validGuids++;
                    }
                }
            }
            results.Add(new ValidationResult("Parameter GUIDs", "MODERATE",
                validGuids >= 10,
                $"{validGuids} valid GUIDs found"));
        }

        private void CheckBindingCoverage(string dataPath, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile("BINDING_COVERAGE_MATRIX.csv");
            if (path == null)
            {
                results.Add(new ValidationResult("Binding coverage matrix", "MODERATE", false, "File not found"));
                return;
            }

            var rawLines = SafeReadAllLines(path, Path.GetFileName(path), results);
            if (rawLines == null) return;
            var lines = rawLines.Where(l => !l.StartsWith("#")).ToArray();
            if (lines.Length < 2)
            {
                results.Add(new ValidationResult("BCM row count", "MODERATE", false, "Too few rows"));
                return;
            }

            string[] headers = StingToolsApp.ParseCsvLine(lines[0]);
            int categoryCount = headers.Length - 1; // First column is param name
            int paramRows = lines.Length - 1;

            results.Add(new ValidationResult("BCM dimensions", "MODERATE",
                categoryCount >= 20 && paramRows >= 10,
                $"{paramRows} params × {categoryCount} categories"));

            // Check for empty rows
            int emptyRows = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var cols = StingToolsApp.ParseCsvLine(lines[i]);
                if (cols.All(c => string.IsNullOrWhiteSpace(c) || c == "0"))
                    emptyRows++;
            }
            results.Add(new ValidationResult("BCM empty parameters", "LOW",
                emptyRows == 0,
                emptyRows == 0 ? "No empty rows" : $"{emptyRows} params bound to zero categories"));
        }

        private void CheckMaterialLibraries(string dataPath, List<ValidationResult> results)
        {
            CheckMaterialFile("BLE_MATERIALS.csv", 800, results);
            CheckMaterialFile("MEP_MATERIALS.csv", 400, results);
        }

        private void CheckMaterialFile(string fileName, int expectedMin, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile(fileName);
            if (path == null)
            {
                results.Add(new ValidationResult($"{fileName}", "CRITICAL", false, "File not found"));
                return;
            }

            var rawLines = SafeReadAllLines(path, Path.GetFileName(path), results);
            if (rawLines == null) return;
            var lines = rawLines.Where(l => !l.StartsWith("#")).ToArray();
            int rowCount = lines.Length - 1; // exclude header

            results.Add(new ValidationResult($"{fileName} row count", "MODERATE",
                rowCount >= expectedMin,
                $"{rowCount} materials (expected {expectedMin}+)"));

            // Check header completeness
            if (lines.Length > 0)
            {
                var headers = StingToolsApp.ParseCsvLine(lines[0]);
                bool hasName = headers.Any(h => h.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.IndexOf("MAT_CODE", StringComparison.OrdinalIgnoreCase) >= 0);
                bool hasColor = headers.Any(h => h.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.IndexOf("COLOUR", StringComparison.OrdinalIgnoreCase) >= 0);

                results.Add(new ValidationResult($"{fileName} schema", "MODERATE",
                    hasName,
                    $"{headers.Length} columns, Name={hasName}, Color={hasColor}"));
            }

            // Check for duplicate material codes
            if (lines.Length > 1)
            {
                var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int dupes = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = StingToolsApp.ParseCsvLine(lines[i]);
                    if (cols.Length > 0 && !string.IsNullOrWhiteSpace(cols[0]))
                    {
                        if (!codes.Add(cols[0])) dupes++;
                    }
                }
                results.Add(new ValidationResult($"{fileName} duplicates", "MODERATE",
                    dupes == 0,
                    dupes == 0 ? "No duplicate codes" : $"{dupes} duplicate material codes"));
            }
        }

        private void CheckScheduleDefinitions(string dataPath, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile("MR_SCHEDULES.csv");
            if (path == null)
            {
                results.Add(new ValidationResult("Schedule definitions", "MODERATE", false, "File not found"));
                return;
            }

            var rawLines = SafeReadAllLines(path, Path.GetFileName(path), results);
            if (rawLines == null) return;
            var lines = rawLines.Where(l => !l.StartsWith("#")).ToArray();
            int schedCount = lines.Length - 1;

            results.Add(new ValidationResult("Schedule count", "MODERATE",
                schedCount >= 100,
                $"{schedCount} schedules defined (expected 100+)"));

            // Check for required columns
            if (lines.Length > 0)
            {
                var headers = StingToolsApp.ParseCsvLine(lines[0]);
                bool hasName = headers.Any(h => h.IndexOf("Name", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.IndexOf("Schedule", StringComparison.OrdinalIgnoreCase) >= 0);
                bool hasCategory = headers.Any(h => h.IndexOf("Category", StringComparison.OrdinalIgnoreCase) >= 0);

                results.Add(new ValidationResult("Schedule schema", "MODERATE",
                    hasName && hasCategory,
                    $"Columns: {headers.Length}, Name={hasName}, Category={hasCategory}"));
            }
        }

        private void CheckFormulaDependencies(string dataPath, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile("FORMULAS_WITH_DEPENDENCIES.csv");
            if (path == null)
            {
                results.Add(new ValidationResult("Formula file", "MODERATE", false, "File not found"));
                return;
            }

            var rawLines = SafeReadAllLines(path, Path.GetFileName(path), results);
            if (rawLines == null) return;
            var lines = rawLines.Where(l => !l.StartsWith("#")).ToArray();
            int formulaCount = lines.Length - 1;

            results.Add(new ValidationResult("Formula count", "MODERATE",
                formulaCount >= 100,
                $"{formulaCount} formulas (expected 100+)"));

            // Check for dependency levels (cycle detection proxy)
            if (lines.Length > 1)
            {
                var headers = StingToolsApp.ParseCsvLine(lines[0]);
                int depIdx = Array.FindIndex(headers, h =>
                    h.IndexOf("Dep", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0);

                if (depIdx >= 0)
                {
                    int maxLevel = 0;
                    int invalidLevels = 0;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var cols = StingToolsApp.ParseCsvLine(lines[i]);
                        if (cols.Length > depIdx && int.TryParse(cols[depIdx], out int level))
                        {
                            if (level > maxLevel) maxLevel = level;
                        }
                        else
                        {
                            invalidLevels++;
                        }
                    }

                    results.Add(new ValidationResult("Formula dependency depth", "MODERATE",
                        maxLevel <= 10,
                        $"Max depth: {maxLevel}, invalid levels: {invalidLevels}"));
                }
            }
        }

        private void CheckSchemaConsistency(string dataPath, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile("MATERIAL_SCHEMA.json");
            if (path == null)
            {
                results.Add(new ValidationResult("Material schema", "MODERATE", false, "File not found"));
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var schema = JObject.Parse(json);

                // Check metadata
                var meta = schema["_metadata"];
                string version = meta?["version"]?.ToString() ?? "unknown";
                results.Add(new ValidationResult("Schema version", "LOW",
                    !string.IsNullOrEmpty(version),
                    $"Version: {version}"));

                // Check column definitions
                var columns = schema["columns"] ?? schema["fields"];
                int colCount = 0;
                if (columns is JArray arr)
                    colCount = arr.Count;
                else if (columns is JObject obj)
                    colCount = obj.Count;

                results.Add(new ValidationResult("Schema columns", "MODERATE",
                    colCount >= 20,
                    $"{colCount} columns defined (expected 20+)"));
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult("Schema parse", "CRITICAL", false,
                    $"JSON parse error: {ex.Message}"));
            }
        }

        private void CheckCategoryBindings(string dataPath, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
            if (path == null)
            {
                results.Add(new ValidationResult("Category bindings", "MODERATE", false, "File not found"));
                return;
            }

            var rawLines = SafeReadAllLines(path, Path.GetFileName(path), results);
            if (rawLines == null) return;
            var lines = rawLines.Where(l => !l.StartsWith("#")).ToArray();
            int bindingCount = lines.Length - 1;

            results.Add(new ValidationResult("Binding count", "MODERATE",
                bindingCount >= 5000,
                $"{bindingCount} bindings (expected 5000+)"));

            // Check for required columns
            if (lines.Length > 0)
            {
                var headers = StingToolsApp.ParseCsvLine(lines[0]);
                bool hasParam = headers.Any(h =>
                    h.IndexOf("Param", StringComparison.OrdinalIgnoreCase) >= 0);
                bool hasCat = headers.Any(h =>
                    h.IndexOf("Category", StringComparison.OrdinalIgnoreCase) >= 0);
                bool hasType = headers.Any(h =>
                    h.IndexOf("Binding_Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    h.IndexOf("Type", StringComparison.OrdinalIgnoreCase) >= 0);

                results.Add(new ValidationResult("Bindings schema", "MODERATE",
                    hasParam && hasCat,
                    $"Param={hasParam}, Category={hasCat}, Type={hasType}"));
            }
        }

        private void CheckRemapTable(string dataPath, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile("SCHEDULE_FIELD_REMAP.csv");
            if (path == null)
            {
                results.Add(new ValidationResult("Schedule field remap", "LOW", false, "File not found"));
                return;
            }

            var rawLines = SafeReadAllLines(path, Path.GetFileName(path), results);
            if (rawLines == null) return;
            var lines = rawLines.Where(l => !l.StartsWith("#")).ToArray();
            int remapCount = lines.Length - 1;

            results.Add(new ValidationResult("Remap entries", "LOW",
                remapCount >= 10,
                $"{remapCount} field remaps defined"));
        }

        private void CheckProjectState(Document doc, List<ValidationResult> results)
        {
            // Check shared parameters are loaded
            int sharedParams = 0;
            try
            {
                var defFile = doc.Application.OpenSharedParameterFile();
                if (defFile != null)
                {
                    foreach (DefinitionGroup grp in defFile.Groups)
                        sharedParams += grp.Definitions.Size;
                }
            }
            catch (Exception ex) { StingLog.Warn($"OpenSharedParameterFile: {ex.Message}"); }

            results.Add(new ValidationResult("Shared params loaded", "MODERATE",
                sharedParams >= 50,
                $"{sharedParams} shared parameters in file"));

            // Check project info
            var pi = doc.ProjectInformation;
            bool hasName = !string.IsNullOrWhiteSpace(pi?.Name);
            bool hasNumber = !string.IsNullOrWhiteSpace(pi?.Number);
            results.Add(new ValidationResult("Project information", "LOW",
                hasName && hasNumber,
                $"Name: {(hasName ? pi.Name : "EMPTY")}, Number: {(hasNumber ? pi.Number : "EMPTY")}"));

            // Check views
            int viewCount = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(v => !v.IsTemplate);
            results.Add(new ValidationResult("View count", "LOW",
                viewCount > 0,
                $"{viewCount} views in project"));

            // Check if the primary tag parameter exists
            bool hasTagParam = false;
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .Take(5);
            foreach (var elem in elems)
            {
                if (elem.LookupParameter(ParamRegistry.TAG1) != null)
                {
                    hasTagParam = true;
                    break;
                }
            }
            results.Add(new ValidationResult("Tag parameters bound", "MODERATE",
                hasTagParam,
                hasTagParam ? $"{ParamRegistry.TAG1} found on elements" : $"{ParamRegistry.TAG1} NOT bound — run Load Params"));
        }
    }

    /// <summary>Validation check result.</summary>
    internal class ValidationResult
    {
        public string CheckName { get; }
        public string Severity { get; }
        public bool Passed { get; }
        public string Detail { get; }

        public ValidationResult(string checkName, string severity, bool passed, string detail)
        {
            CheckName = checkName;
            Severity = severity;
            Passed = passed;
            Detail = detail;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Dynamic Bindings — load from CSV instead of hardcode
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Load parameter-category bindings from CATEGORY_BINDINGS.csv and
    /// BINDING_COVERAGE_MATRIX.csv, then bind shared parameters accordingly.
    /// Replaces hardcoded SharedParamGuids.DisciplineBindings for greater flexibility.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DynamicBindingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            string bindingsPath = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
            if (bindingsPath == null)
            {
                TaskDialog.Show("Dynamic Bindings",
                    "CATEGORY_BINDINGS.csv not found.\n" +
                    $"Searched: {StingToolsApp.DataPath}");
                return Result.Failed;
            }

            // Parse binding definitions
            string[] rawBindingLines;
            try
            {
                rawBindingLines = File.ReadAllLines(bindingsPath);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Dynamic Bindings", $"Failed to read file: {ex.Message}");
                StingLog.Error($"DynamicBindings: {ex.Message}", ex);
                return Result.Failed;
            }
            var lines = rawBindingLines
                .Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l))
                .ToArray();

            if (lines.Length < 2)
            {
                TaskDialog.Show("Dynamic Bindings", "No bindings found in CSV.");
                return Result.Failed;
            }

            var headers = StingToolsApp.ParseCsvLine(lines[0]);
            int paramIdx = FindColumn(headers, "Parameter_Name", "Param");
            int catIdx = FindColumn(headers, "Revit_Category", "Category");
            int typeIdx = FindColumn(headers, "Binding_Type", "Type");
            int sharedIdx = FindColumn(headers, "Is_Shared", "Shared");

            if (paramIdx < 0 || catIdx < 0)
            {
                TaskDialog.Show("Dynamic Bindings",
                    "CSV missing required columns: Parameter_Name, Revit_Category");
                return Result.Failed;
            }

            // Group bindings by parameter
            var bindingGroups = new Dictionary<string, List<(string category, string bindType)>>(
                StringComparer.OrdinalIgnoreCase);

            for (int i = 1; i < lines.Length; i++)
            {
                var cols = StingToolsApp.ParseCsvLine(lines[i]);
                if (cols.Length <= Math.Max(paramIdx, catIdx)) continue;

                string paramName = cols[paramIdx].Trim();
                string category = cols[catIdx].Trim();
                string bindType = typeIdx >= 0 && cols.Length > typeIdx ? cols[typeIdx].Trim() : "Instance";

                if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(category))
                    continue;

                if (!bindingGroups.ContainsKey(paramName))
                    bindingGroups[paramName] = new List<(string, string)>();
                bindingGroups[paramName].Add((category, bindType));
            }

            TaskDialog confirm = new TaskDialog("Dynamic Bindings");
            confirm.MainInstruction = $"Bind {bindingGroups.Count} parameters from CSV";
            confirm.MainContent = $"Source: CATEGORY_BINDINGS.csv\n" +
                $"Total bindings: {lines.Length - 1}\n" +
                $"Unique parameters: {bindingGroups.Count}\n\n" +
                "This will create missing parameter bindings.\n" +
                "Existing bindings are not modified.";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            // Open shared parameter file
            var defFile = doc.Application.OpenSharedParameterFile();
            if (defFile == null)
            {
                TaskDialog.Show("Dynamic Bindings",
                    "No shared parameter file set.\n" +
                    "Set it in Manage → Shared Parameters first.");
                return Result.Failed;
            }

            int bound = 0;
            int skipped = 0;
            int failed = 0;

            using (Transaction tx = new Transaction(doc, "STING Dynamic Parameter Bindings"))
            {
                tx.Start();

                foreach (var kvp in bindingGroups)
                {
                    string paramName = kvp.Key;
                    var targets = kvp.Value;

                    // Find definition in shared param file
                    ExternalDefinition def = FindDefinition(defFile, paramName);
                    if (def == null)
                    {
                        skipped++;
                        continue;
                    }

                    // Build category set
                    var catSet = doc.Application.Create.NewCategorySet();
                    foreach (var (catName, _) in targets)
                    {
                        Category cat = FindCategory(doc, catName);
                        if (cat != null)
                            catSet.Insert(cat);
                    }

                    if (catSet.Size == 0)
                    {
                        skipped++;
                        continue;
                    }

                    // Determine binding type (most common in group)
                    bool isType = targets.Count(t =>
                        t.bindType.Equals("Type", StringComparison.OrdinalIgnoreCase)) >
                        targets.Count / 2;

                    try
                    {
                        ElementBinding existingBinding = doc.ParameterBindings.get_Item(def) as ElementBinding;
                        if (existingBinding != null)
                        {
                            // Add missing categories to existing binding
                            bool modified = false;
                            var iter = catSet.GetEnumerator();
                            while (iter.MoveNext())
                            {
                                Category cat = iter.Current as Category;
                                if (cat != null && !existingBinding.Categories.Contains(cat))
                                {
                                    existingBinding.Categories.Insert(cat);
                                    modified = true;
                                }
                            }
                            if (modified)
                            {
                                doc.ParameterBindings.ReInsert(def, existingBinding);
                                bound++;
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                        else
                        {
                            // Create new binding
                            ElementBinding binding = isType
                                ? (ElementBinding)doc.Application.Create.NewTypeBinding(catSet)
                                : (ElementBinding)doc.Application.Create.NewInstanceBinding(catSet);

                            if (doc.ParameterBindings.Insert(def, binding))
                                bound++;
                            else
                                failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        StingLog.Warn($"Bind '{paramName}': {ex.Message}");
                    }
                }

                tx.Commit();
            }

            TaskDialog.Show("Dynamic Bindings",
                $"Bound: {bound}\nSkipped: {skipped} (existing or not found)\nFailed: {failed}");
            StingLog.Info($"DynamicBindings: bound={bound}, skipped={skipped}, failed={failed}");
            return Result.Succeeded;
        }

        private static int FindColumn(string[] headers, params string[] candidates)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                foreach (string cand in candidates)
                {
                    if (headers[i].IndexOf(cand, StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
                }
            }
            return -1;
        }

        private static ExternalDefinition FindDefinition(DefinitionFile defFile, string paramName)
        {
            foreach (DefinitionGroup grp in defFile.Groups)
            {
                foreach (Definition def in grp.Definitions)
                {
                    if (def.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                        return def as ExternalDefinition;
                }
            }
            return null;
        }

        private static Category FindCategory(Document doc, string categoryName)
        {
            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                    return cat;
            }
            return null;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Schema Validate — validate data files against MATERIAL_SCHEMA.json
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validate BLE_MATERIALS.csv and MEP_MATERIALS.csv against MATERIAL_SCHEMA.json.
    /// Checks column presence, data types, required fields, and value ranges.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SchemaValidateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            string schemaPath = StingToolsApp.FindDataFile("MATERIAL_SCHEMA.json");
            if (schemaPath == null)
            {
                TaskDialog.Show("Schema Validate",
                    "MATERIAL_SCHEMA.json not found.");
                return Result.Failed;
            }

            JObject schema;
            try
            {
                schema = JObject.Parse(File.ReadAllText(schemaPath));
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Schema Validate", $"Schema parse error: {ex.Message}");
                return Result.Failed;
            }

            var report = new StringBuilder();
            report.AppendLine("Material Schema Validation");
            report.AppendLine(new string('═', 50));

            // Get expected columns from schema
            var expectedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var requiredColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var columns = schema["columns"] ?? schema["fields"];
            if (columns is JArray colArray)
            {
                foreach (var col in colArray)
                {
                    string name = col["name"]?.ToString() ?? col.ToString();
                    expectedColumns.Add(name);
                    if (col["required"]?.Value<bool>() == true)
                        requiredColumns.Add(name);
                }
            }
            else if (columns is JObject colObj)
            {
                foreach (var prop in colObj.Properties())
                {
                    expectedColumns.Add(prop.Name);
                    if (prop.Value["required"]?.Value<bool>() == true)
                        requiredColumns.Add(prop.Name);
                }
            }

            report.AppendLine($"Schema: {expectedColumns.Count} columns, {requiredColumns.Count} required");
            report.AppendLine();

            // Validate each material file
            int totalIssues = 0;
            totalIssues += ValidateFile("BLE_MATERIALS.csv", expectedColumns, requiredColumns, report);
            totalIssues += ValidateFile("MEP_MATERIALS.csv", expectedColumns, requiredColumns, report);

            TaskDialog td = new TaskDialog("Schema Validate");
            td.MainInstruction = totalIssues == 0
                ? "All material files match schema"
                : $"{totalIssues} schema violations found";
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }

        private int ValidateFile(string fileName, HashSet<string> expectedCols,
            HashSet<string> requiredCols, StringBuilder report)
        {
            string path = StingToolsApp.FindDataFile(fileName);
            if (path == null)
            {
                report.AppendLine($"── {fileName}: NOT FOUND ──");
                return 1;
            }

            string[] rawLines;
            try
            {
                rawLines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                report.AppendLine($"── {fileName}: READ ERROR — {ex.Message} ──");
                StingLog.Error($"ValidateFile({fileName}): {ex.Message}", ex);
                return 1;
            }
            var lines = rawLines.Where(l => !l.StartsWith("#")).ToArray();
            if (lines.Length < 2)
            {
                report.AppendLine($"── {fileName}: EMPTY ──");
                return 1;
            }

            var headers = StingToolsApp.ParseCsvLine(lines[0]);
            var headerSet = new HashSet<string>(headers, StringComparer.OrdinalIgnoreCase);

            report.AppendLine($"── {fileName} ({lines.Length - 1} rows, {headers.Length} cols) ──");

            int issues = 0;

            // Check for missing required columns
            var missingRequired = requiredCols.Where(c => !headerSet.Contains(c)).ToList();
            if (missingRequired.Count > 0)
            {
                report.AppendLine($"  MISSING REQUIRED: {string.Join(", ", missingRequired.Take(10))}");
                issues += missingRequired.Count;
            }

            // Check for missing expected columns
            var missingExpected = expectedCols.Where(c => !headerSet.Contains(c)).ToList();
            if (missingExpected.Count > 0 && missingExpected.Count != missingRequired.Count)
            {
                int extra = missingExpected.Count - missingRequired.Count;
                report.AppendLine($"  Missing optional: {extra} columns");
            }

            // Check for extra columns not in schema
            var extraCols = headers.Where(h => !expectedCols.Contains(h)).ToList();
            if (extraCols.Count > 0)
            {
                report.AppendLine($"  Extra columns (not in schema): {extraCols.Count}");
            }

            // Sample data quality: check first 100 rows for empty required fields
            int emptyRequired = 0;
            for (int i = 1; i < Math.Min(lines.Length, 101); i++)
            {
                var cols = StingToolsApp.ParseCsvLine(lines[i]);
                foreach (string req in requiredCols)
                {
                    int idx = Array.FindIndex(headers, h =>
                        h.Equals(req, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0 && idx < cols.Length && string.IsNullOrWhiteSpace(cols[idx]))
                        emptyRequired++;
                }
            }

            if (emptyRequired > 0)
            {
                report.AppendLine($"  Empty required fields: {emptyRequired} (in first 100 rows)");
                issues += emptyRequired;
            }

            if (issues == 0)
                report.AppendLine("  All checks passed");

            report.AppendLine();
            return issues;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  BOQ Export Command — Professional Bill of Quantities
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exports a professionally formatted Bill of Quantities (BOQ) as Excel (.xlsx)
    /// matching standard Ugandan/East African QS practice. Multi-sheet workbook:
    ///   Cover Page → Bill Sheets per discipline → Collection pages →
    ///   Bill Summaries → Main Summary with Contingencies + VAT.
    /// Columns: ITEM | DESCRIPTION | QTY | UNIT | RATE (UGX) | AMOUNT (UGX)
    /// Items use letter codes (A, B, C...). Sections end with "TO COLLECTION"
    /// or "TO SUMMARY". Full Excel formatting: bold headers, borders, merged cells,
    /// number formats (#,##0), print settings. Zero manual editing required after export.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQExportCommand : IExternalCommand
    {
        // ── Discipline to Bill Number mapping ──
        // Based on standard Ugandan MEP BOQ practice (see reference BOQs)
        private static readonly Dictionary<string, (int billNo, string billTitle)> BillMap =
            new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase)
        {
            { "E", (1, "ELECTRICAL INSTALLATIONS") },
            { "LV", (1, "ELECTRICAL INSTALLATIONS") },
            { "M", (2, "MECHANICAL INSTALLATIONS") },
            { "P", (3, "PLUMBING INSTALLATIONS") },
            { "FP", (4, "FIRE FIGHTING INSTALLATIONS") },
            { "A", (5, "ARCHITECTURAL WORKS") },
            { "S", (6, "STRUCTURAL WORKS") },
            { "G", (7, "GENERAL WORKS") },
        };

        // ── Category → discipline mapping for items without DISC token ──
        private static readonly Dictionary<string, string> CategoryDisc =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Electrical Equipment", "E" }, { "Electrical Fixtures", "E" },
            { "Lighting Fixtures", "E" }, { "Lighting Devices", "E" },
            { "Conduits", "E" }, { "Conduit Fittings", "E" },
            { "Cable Trays", "E" }, { "Cable Tray Fittings", "E" },
            { "Communication Devices", "E" }, { "Data Devices", "E" },
            { "Security Devices", "E" }, { "Nurse Call Devices", "E" },
            { "Mechanical Equipment", "M" }, { "Ducts", "M" },
            { "Duct Fittings", "M" }, { "Duct Accessories", "M" },
            { "Duct Insulations", "M" }, { "Air Terminals", "M" },
            { "Flex Ducts", "M" },
            { "Plumbing Fixtures", "P" }, { "Plumbing Equipment", "P" },
            { "Pipes", "P" }, { "Pipe Fittings", "P" },
            { "Pipe Accessories", "P" }, { "Pipe Insulations", "P" },
            { "Flex Pipes", "P" },
            { "Fire Alarm Devices", "FP" }, { "Sprinklers", "FP" },
            { "Walls", "A" }, { "Doors", "A" }, { "Windows", "A" },
            { "Floors", "A" }, { "Ceilings", "A" }, { "Roofs", "A" },
            { "Stairs", "A" }, { "Ramps", "A" }, { "Railings", "A" },
            { "Curtain Panels", "A" }, { "Curtain Wall Mullions", "A" },
            { "Furniture", "A" }, { "Casework", "A" },
            { "Furniture Systems", "A" }, { "Specialty Equipment", "A" },
            { "Generic Models", "A" },
            { "Structural Columns", "S" }, { "Structural Framing", "S" },
            { "Structural Foundations", "S" },
            { "Topography", "G" },
        };

        // ── Standard percentages (Uganda) ──
        private const double ContingencyPercent = 0.05; // 5%
        private const double VATPercent = 0.18;         // 18% Uganda standard

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            StingLog.Info("BOQ Export starting...");

            // ── Collect all taggable elements ──
            var allElems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            if (allElems.Count == 0)
            {
                TaskDialog.Show("BOQ Export", "No taggable elements found in project.");
                return Result.Succeeded;
            }

            // ── Calculate GIA from rooms ──
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.Room>()
                .Where(r => r.Area > 0)
                .ToList();
            double giaM2 = rooms.Sum(r => r.Area) * 0.092903; // sqft to m²

            // ── DAT-007: Load rate defaults from BOQ_TEMPLATE.csv SECTION 8 ──
            var rateDefaults = LoadRateDefaults();
            int rateDefaultsApplied = 0;

            // ── Build BOQ items ──
            var boqItems = new List<BOQItem>();
            int warnings = 0;
            int zeroCostCount = 0;
            int noDescCount = 0;
            foreach (Element el in allElems)
            {
                try
                {
                    string catName = el.Category?.Name ?? "Uncategorised";
                    var item = new BOQItem
                    {
                        ElementId = el.Id.IntegerValue,
                        Category = catName,
                        FamilyName = ParameterHelpers.GetFamilyName(el),
                        TypeName = ParameterHelpers.GetFamilySymbolName(el),
                        Description = ParameterHelpers.GetString(el, "ASS_DESCRIPTION_TXT"),
                        Tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1),
                        Discipline = ParameterHelpers.GetString(el, ParamRegistry.DISC),
                        Level = ParameterHelpers.GetString(el, ParamRegistry.LVL),
                        Location = ParameterHelpers.GetString(el, ParamRegistry.LOC),
                        Zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE),
                        System = ParameterHelpers.GetString(el, ParamRegistry.SYS),
                        Unit = ParameterHelpers.GetString(el, "ASS_PMT_INV_UNIT_TXT"),
                    };

                    // Read identity from type
                    ElementType eType = doc.GetElement(el.GetTypeId()) as ElementType;
                    if (eType != null)
                    {
                        if (string.IsNullOrEmpty(item.Description))
                            item.Description = eType.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString() ?? "";
                    }

                    // Resolve discipline: STING token → category fallback
                    if (string.IsNullOrEmpty(item.Discipline))
                        CategoryDisc.TryGetValue(catName, out item.Discipline);
                    if (string.IsNullOrEmpty(item.Discipline))
                        item.Discipline = "G";

                    // Resolve level
                    if (string.IsNullOrEmpty(item.Level))
                        item.Level = ParameterHelpers.GetLevelCode(doc, el);

                    // ── Measure true quantities from Revit geometry ──
                    MeasureRevitQuantities(el, item);

                    // Read cost parameters
                    item.UnitPrice = ReadDouble(el, "ASS_CST_UNIT_PRICE_UGX_NR");
                    item.Quantity = ReadDouble(el, "ASS_CST_QUANTITY_NR");
                    if (item.Quantity == 0) item.Quantity = 1;

                    // DAT-007: Apply rate default when unit price is empty
                    if (item.UnitPrice == 0 && rateDefaults.Count > 0)
                    {
                        string prodCode = ParameterHelpers.GetString(el, ParamRegistry.PROD);
                        if (!string.IsNullOrEmpty(prodCode) &&
                            rateDefaults.TryGetValue(prodCode, out double defaultRate))
                        {
                            item.UnitPrice = defaultRate;
                            rateDefaultsApplied++;
                        }
                    }

                    item.TotalCost = ReadDouble(el, "ASS_CST_TOTAL_UGX_NR");
                    if (item.TotalCost == 0 && item.UnitPrice > 0)
                        item.TotalCost = item.UnitPrice * item.Quantity;

                    // Generate unit if empty
                    if (string.IsNullOrEmpty(item.Unit))
                    {
                        item.Unit = catName switch
                        {
                            var c when c.Contains("Pipe") || c.Contains("Duct") ||
                                       c.Contains("Conduit") || c.Contains("Cable Tray") => "LM",
                            var c when c.Contains("Floor") || c.Contains("Ceiling") ||
                                       c.Contains("Wall") || c.Contains("Roof") => "SM",
                            var c when c.Contains("Concrete") || c.Contains("Foundation") => "CM",
                            _ => "NO",
                        };
                    }

                    // Use Revit measured qty as the primary quantity for linear/area items
                    if (item.MeasuredLength_m > 0 && (item.Unit == "LM" || item.Unit == "m"))
                    {
                        item.Quantity = Math.Round(item.MeasuredLength_m, 1);
                        item.Unit = "LM";
                    }
                    else if (item.MeasuredArea_m2 > 0 && (item.Unit == "SM" || item.Unit == "m\u00B2"))
                    {
                        item.Quantity = Math.Round(item.MeasuredArea_m2, 1);
                        item.Unit = "SM";
                    }

                    // Recalculate total from measured qty × rate
                    if (item.TotalCost == 0 && item.UnitPrice > 0)
                        item.TotalCost = item.UnitPrice * item.Quantity;

                    // Data quality tracking
                    if (item.TotalCost == 0 && item.UnitPrice == 0) zeroCostCount++;
                    if (string.IsNullOrEmpty(item.Description)) noDescCount++;

                    boqItems.Add(item);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BOQ item {el.Id}: {ex.Message}");
                    warnings++;
                }
            }

            // ── Group by Bill (discipline) → Level → System/Category → Type ──
            var billGroups = boqItems
                .GroupBy(b =>
                {
                    if (BillMap.TryGetValue(b.Discipline, out var bm))
                        return (bm.billNo, bm.billTitle);
                    return (99, "GENERAL WORKS");
                })
                .OrderBy(g => g.Key.Item1)
                .ToList();

            // ═══════════════════════════════════════════════════════════════
            //  BUILD EXCEL WORKBOOK
            // ═══════════════════════════════════════════════════════════════
            using var wb = new XLWorkbook();
            double grandTotal = 0;
            var billTotals = new List<(int billNo, string title, double total)>();

            // ── Project info ──
            string projInfo = "";
            try { projInfo = doc.ProjectInformation?.Name ?? ""; } catch { }

            // ═══════════════════════════════════════════════════════════════
            //  COVER PAGE
            // ═══════════════════════════════════════════════════════════════
            var coverWs = wb.AddWorksheet("Cover Page");
            SetStandardColumnWidths(coverWs);
            int cr = 4; // start row
            var titleCell = coverWs.Cell(cr, 2);
            titleCell.Value = doc.Title;
            titleCell.Style.Font.Bold = true;
            titleCell.Style.Font.FontSize = 18;
            coverWs.Range(cr, 2, cr, 6).Merge();
            cr += 2;
            if (!string.IsNullOrEmpty(projInfo))
            {
                coverWs.Cell(cr, 2).Value = projInfo;
                coverWs.Cell(cr, 2).Style.Font.FontSize = 14;
                coverWs.Range(cr, 2, cr, 6).Merge();
                cr += 2;
            }
            var boqTitleCell = coverWs.Cell(cr, 2);
            boqTitleCell.Value = "BILL OF QUANTITIES";
            boqTitleCell.Style.Font.Bold = true;
            boqTitleCell.Style.Font.FontSize = 16;
            coverWs.Range(cr, 2, cr, 6).Merge();
            cr += 3;
            coverWs.Cell(cr, 2).Value = DateTime.Now.ToString("MMMM yyyy");
            coverWs.Cell(cr, 2).Style.Font.FontSize = 12;
            coverWs.Range(cr, 2, cr, 6).Merge();

            // ═══════════════════════════════════════════════════════════════
            //  BILL SHEETS (one worksheet per discipline)
            // ═══════════════════════════════════════════════════════════════
            foreach (var billGroup in billGroups)
            {
                int billNo = billGroup.Key.Item1;
                string billTitle = billGroup.Key.Item2;
                var billItems = billGroup.ToList();

                // Worksheet name max 31 chars
                string wsName = $"Bill {billNo}";
                var ws = wb.AddWorksheet(wsName);
                SetStandardColumnWidths(ws);
                int row = 1;

                // ── FLY SHEET header ──
                row++;
                WriteMergedBold(ws, row, 2, 6, $"BILL NO.{billNo}", 14); row++;
                WriteMergedBold(ws, row, 2, 6, billTitle, 12); row++;
                row++; // blank

                // ── Column headers ──
                WriteColumnHeaders(ws, row); row++;

                // ── Level groups ──
                var levelGroups = billItems
                    .GroupBy(b => string.IsNullOrEmpty(b.Level) ? "GENERAL" : b.Level)
                    .OrderBy(g => g.Key).ToList();

                int sectionNum = 0;
                double billTotal = 0;
                var billSectionTotals = new List<(string section, string pageRef, double total)>();

                foreach (var levelGroup in levelGroups)
                {
                    sectionNum++;
                    string levelName = GetLevelDisplayName(levelGroup.Key);
                    string pageRef = $"{billNo}/{sectionNum}";

                    // Section header
                    row++;
                    WriteMergedBold(ws, row, 2, 5, levelName, 11); row++;

                    // Sub-group by system/category
                    var subGroups = levelGroup
                        .GroupBy(b => !string.IsNullOrEmpty(b.System) ? b.System : b.Category)
                        .OrderBy(g => g.Key).ToList();

                    double sectionTotal = 0;
                    var collectionEntries = new List<(string subSection, double total)>();

                    foreach (var subGroup in subGroups)
                    {
                        string subSectionTitle = GetSubSectionTitle(subGroup.Key);

                        // Sub-section title (bold italic)
                        var subCell = ws.Cell(row, 2);
                        subCell.Value = subSectionTitle;
                        subCell.Style.Font.Bold = true;
                        subCell.Style.Font.Italic = true;
                        row++;

                        // Aggregate items by type
                        var typeGroups = subGroup
                            .GroupBy(b => $"{b.FamilyName}|{b.TypeName}")
                            .OrderBy(g => g.Key).ToList();

                        double subSectionTotal = 0;
                        int letterIdx = 0;

                        foreach (var tg in typeGroups)
                        {
                            string itemLetter = GetItemLetter(letterIdx++);
                            var sample = tg.First();
                            double qty = tg.Sum(b => b.Quantity);
                            double avgRate = tg.Where(b => b.UnitPrice > 0)
                                .Select(b => b.UnitPrice).DefaultIfEmpty(0).Average();
                            double lineTotal = tg.Sum(b => b.TotalCost);
                            if (lineTotal == 0 && avgRate > 0)
                                lineTotal = avgRate * qty;

                            string desc = !string.IsNullOrEmpty(sample.Description)
                                ? sample.Description
                                : $"{sample.FamilyName} - {sample.TypeName}";

                            // ITEM | DESCRIPTION | QTY | UNIT | RATE | AMOUNT
                            ws.Cell(row, 1).Value = itemLetter;
                            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            ws.Cell(row, 2).Value = desc;
                            ws.Cell(row, 3).Value = qty;
                            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0";
                            ws.Cell(row, 4).Value = sample.Unit;
                            ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            if (avgRate > 0) WriteAmount(ws, row, 5, avgRate);
                            if (lineTotal > 0) WriteAmount(ws, row, 6, lineTotal);
                            row++;

                            subSectionTotal += lineTotal;
                        }

                        collectionEntries.Add((subSectionTitle, subSectionTotal));
                        sectionTotal += subSectionTotal;
                    }

                    // ── TO COLLECTION (if multiple sub-sections) ──
                    if (subGroups.Count > 1 && collectionEntries.Count > 1)
                    {
                        row++;
                        WriteTotalRow(ws, row, "TO COLLECTION", sectionTotal); row++;
                        ws.Cell(row, 2).Value = pageRef;
                        ws.Cell(row, 2).Style.Font.FontSize = 9;
                        ws.Cell(row, 2).Style.Font.Italic = true;
                        row += 2;

                        // COLLECTION page
                        WriteMergedBold(ws, row, 2, 5, "COLLECTION", 11); row++;
                        foreach (var ce in collectionEntries)
                        {
                            ws.Cell(row, 2).Value = $"Total carried forward: {ce.subSection}";
                            if (ce.total > 0) WriteAmount(ws, row, 6, ce.total);
                            row++;
                        }
                    }

                    // ── TO SUMMARY ──
                    row++;
                    WriteTotalRow(ws, row, "TO SUMMARY", sectionTotal); row++;
                    ws.Cell(row, 2).Value = pageRef;
                    ws.Cell(row, 2).Style.Font.FontSize = 9;
                    ws.Cell(row, 2).Style.Font.Italic = true;
                    row++;

                    billSectionTotals.Add((levelName, pageRef, sectionTotal));
                    billTotal += sectionTotal;
                }

                // ═══════════════════════════════════════════════════════════
                //  BILL SUMMARY (at bottom of same sheet)
                // ═══════════════════════════════════════════════════════════
                row += 2;
                WriteMergedBold(ws, row, 2, 5, $"BILL NO.{billNo}", 12); row++;
                WriteMergedBold(ws, row, 2, 5, billTitle, 11); row++;
                WriteMergedBold(ws, row, 2, 5, "SUMMARY", 11); row++;
                row++;

                // Summary header
                ws.Cell(row, 1).Value = "ELEMENT";
                ws.Cell(row, 2).Value = "DESCRIPTION";
                ws.Cell(row, 4).Value = "PAGE NO.";
                ws.Cell(row, 5).Value = "AMOUNT (UGX)";
                var hdrRange = ws.Range(row, 1, row, 6);
                hdrRange.Style.Font.Bold = true;
                hdrRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                row++;

                int elemNum = 0;
                foreach (var st in billSectionTotals)
                {
                    elemNum++;
                    ws.Cell(row, 1).Value = elemNum;
                    ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 2).Value = st.section;
                    ws.Cell(row, 4).Value = st.pageRef;
                    ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (st.total > 0) WriteAmount(ws, row, 5, st.total);
                    row++;
                }

                // Bill total
                row++;
                WriteTotalRow(ws, row, $"TOTAL BILL NO.{billNo} CARRIED TO MAIN SUMMARY", billTotal); row++;
                ws.Cell(row, 2).Value = $"{billNo}/S";
                ws.Cell(row, 2).Style.Font.FontSize = 9;
                ws.Cell(row, 2).Style.Font.Italic = true;

                // Print settings
                ws.PageSetup.PrintTitleRows = 1;
                ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
                ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
                ws.PageSetup.FitToPages(1, 0); // fit width to 1 page

                billTotals.Add((billNo, billTitle, billTotal));
                grandTotal += billTotal;
            }

            // ═══════════════════════════════════════════════════════════════
            //  MAIN SUMMARY SHEET
            // ═══════════════════════════════════════════════════════════════
            var msWs = wb.AddWorksheet("Main Summary");
            SetStandardColumnWidths(msWs);
            int mr = 2;

            // Project info
            msWs.Cell(mr, 1).Value = "PROJECT:";
            msWs.Cell(mr, 1).Style.Font.Bold = true;
            msWs.Cell(mr, 2).Value = doc.Title;
            msWs.Range(mr, 2, mr, 6).Merge();
            mr++;
            msWs.Cell(mr, 1).Value = "DATE:";
            msWs.Cell(mr, 1).Style.Font.Bold = true;
            msWs.Cell(mr, 2).Value = DateTime.Now.ToString("MMMM yyyy");
            mr++;
            if (giaM2 > 0)
            {
                msWs.Cell(mr, 1).Value = "GIA:";
                msWs.Cell(mr, 1).Style.Font.Bold = true;
                msWs.Cell(mr, 2).Value = $"{giaM2:F2} m\u00B2";
                mr++;
            }
            mr++;
            WriteMergedBold(msWs, mr, 2, 5, "MAIN SUMMARY", 14); mr++;
            mr++;

            // Header row
            msWs.Cell(mr, 1).Value = "BILL NO.";
            msWs.Cell(mr, 2).Value = "DESCRIPTION";
            msWs.Cell(mr, 4).Value = "PAGE NO.";
            msWs.Cell(mr, 6).Value = "AMOUNT (UGX)";
            var msHdr = msWs.Range(mr, 1, mr, 6);
            msHdr.Style.Font.Bold = true;
            msHdr.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            mr++;

            // Bill lines
            foreach (var bt in billTotals)
            {
                msWs.Cell(mr, 1).Value = bt.billNo;
                msWs.Cell(mr, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                msWs.Cell(mr, 2).Value = bt.title;
                msWs.Cell(mr, 4).Value = $"{bt.billNo}/S";
                msWs.Cell(mr, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                if (bt.total > 0) WriteAmount(msWs, mr, 6, bt.total);
                mr++;
            }

            // Sub-Total 1
            mr++;
            WriteTotalRow(msWs, mr, "SUB-TOTAL 1", grandTotal); mr++;

            // Contingencies
            double contingency = grandTotal * ContingencyPercent;
            msWs.Cell(mr, 2).Value = "ADD:  CONTINGENCIES";
            msWs.Cell(mr, 2).Style.Font.Bold = true;
            msWs.Cell(mr, 4).Value = $"{ContingencyPercent:P0}";
            msWs.Cell(mr, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (contingency > 0) WriteAmount(msWs, mr, 5, contingency);
            mr++;
            mr++;

            double subTotal2 = grandTotal + contingency;
            WriteTotalRow(msWs, mr, "SUB-TOTAL 2", subTotal2); mr++;

            // VAT
            double vat = subTotal2 * VATPercent;
            msWs.Cell(mr, 2).Value = "ADD:  VAT";
            msWs.Cell(mr, 2).Style.Font.Bold = true;
            msWs.Cell(mr, 4).Value = $"{VATPercent:P0}";
            msWs.Cell(mr, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (vat > 0) WriteAmount(msWs, mr, 5, vat);
            mr++;
            mr++;

            // TOTAL COST (double-bordered)
            double totalCost = subTotal2 + vat;
            var tcCell = msWs.Cell(mr, 2);
            tcCell.Value = "TOTAL COST";
            tcCell.Style.Font.Bold = true;
            tcCell.Style.Font.FontSize = 12;
            if (totalCost > 0) WriteAmount(msWs, mr, 6, totalCost);
            msWs.Cell(mr, 6).Style.Font.Bold = true;
            msWs.Cell(mr, 6).Style.Font.FontSize = 12;
            var tcRange = msWs.Range(mr, 1, mr, 6);
            tcRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            tcRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            mr++;

            // Cost per m²
            if (giaM2 > 0)
            {
                mr++;
                msWs.Cell(mr, 2).Value = "COST / m\u00B2 GIA (excl. VAT)";
                WriteAmount(msWs, mr, 6, subTotal2 / giaM2); mr++;
                msWs.Cell(mr, 2).Value = "COST / m\u00B2 GIA (incl. VAT)";
                WriteAmount(msWs, mr, 6, totalCost / giaM2); mr++;
            }

            // Signature blocks
            mr += 3;
            msWs.Cell(mr, 1).Value = "Signature of Employer";
            msWs.Cell(mr, 4).Value = "Signature of the Contractor";
            mr += 2;
            msWs.Cell(mr, 1).Value = "Witness: _______________";
            msWs.Cell(mr, 4).Value = "Witness: _______________";
            mr++;
            msWs.Cell(mr, 1).Value = "Date: _________________";
            msWs.Cell(mr, 4).Value = "Date: _________________";

            // Print settings
            msWs.PageSetup.PaperSize = XLPaperSize.A4Paper;
            msWs.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            msWs.PageSetup.FitToPages(1, 1);

            // ═══════════════════════════════════════════════════════════════
            //  DATA QUALITY NOTES (separate sheet)
            // ═══════════════════════════════════════════════════════════════
            int unclassifiedCount = boqItems.Count(b => b.Discipline == "G" && b.Category != "Topography");
            if (zeroCostCount > 0 || noDescCount > 0 || unclassifiedCount > 0 || warnings > 0)
            {
                var dqWs = wb.AddWorksheet("Data Quality");
                SetStandardColumnWidths(dqWs);
                int dr = 2;
                WriteMergedBold(dqWs, dr, 2, 5, "DATA QUALITY NOTES", 12); dr++;
                dr++;
                if (zeroCostCount > 0)
                { dqWs.Cell(dr, 2).Value = $"WARNING: {zeroCostCount} elements have zero cost data"; dr++; }
                if (noDescCount > 0)
                { dqWs.Cell(dr, 2).Value = $"WARNING: {noDescCount} elements have no description"; dr++; }
                if (unclassifiedCount > 0)
                { dqWs.Cell(dr, 2).Value = $"WARNING: {unclassifiedCount} elements in General (review discipline assignment)"; dr++; }
                if (warnings > 0)
                { dqWs.Cell(dr, 2).Value = $"WARNING: {warnings} elements could not be processed"; dr++; }
            }

            // ═══════════════════════════════════════════════════════════════
            //  SAVE WORKBOOK
            // ═══════════════════════════════════════════════════════════════
            string exportDir = StingToolsApp.DataPath ?? Path.GetTempPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeTitle = string.Join("_", doc.Title.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"STING_BOQ_{safeTitle}_{timestamp}.xlsx";
            string exportPath = Path.Combine(exportDir, fileName);
            try
            {
                wb.SaveAs(exportPath);
                StingLog.Info($"BOQ exported to {exportPath}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"BOQ export failed: {ex.Message}");
                try
                {
                    exportPath = Path.Combine(Path.GetTempPath(), fileName);
                    wb.SaveAs(exportPath);
                }
                catch (Exception ex2)
                {
                    StingLog.Error($"BOQ fallback save failed: {ex2.Message}");
                    TaskDialog.Show("BOQ Export", $"Could not save BOQ file.\n{ex.Message}\n{ex2.Message}");
                    return Result.Failed;
                }
            }

            // ── Summary dialog ──
            var summary = new StringBuilder();
            summary.AppendLine($"Bill of Quantities \u2014 {doc.Title}");
            summary.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            summary.AppendLine(new string('\u2550', 55));
            summary.AppendLine();
            summary.AppendLine($"Total Elements: {boqItems.Count:N0}");
            summary.AppendLine($"Unique Types:   {boqItems.Select(b => $"{b.Category}|{b.TypeName}").Distinct().Count():N0}");
            summary.AppendLine($"Bills:          {billGroups.Count}");
            if (giaM2 > 0)
                summary.AppendLine($"GIA:            {giaM2:F2} m\u00B2 ({rooms.Count} rooms)");
            summary.AppendLine();
            summary.AppendLine("BILL BREAKDOWN:");
            foreach (var bt in billTotals)
                summary.AppendLine($"  Bill {bt.billNo,-3} {bt.title,-35} {bt.total,14:N0}");
            summary.AppendLine();
            summary.AppendLine(new string('\u2500', 55));
            summary.AppendLine($"{"SUB-TOTAL 1:",-35} {grandTotal,14:N0}");
            summary.AppendLine($"{"ADD CONTINGENCIES (5%):",-35} {contingency,14:N0}");
            summary.AppendLine($"{"SUB-TOTAL 2:",-35} {subTotal2,14:N0}");
            summary.AppendLine($"{"ADD VAT (18%):",-35} {vat,14:N0}");
            summary.AppendLine(new string('\u2550', 55));
            summary.AppendLine($"{"TOTAL COST:",-35} {totalCost,14:N0}");
            if (giaM2 > 0)
            {
                summary.AppendLine();
                summary.AppendLine($"{"Cost/m\u00B2 GIA (excl. VAT):",-35} {(subTotal2 / giaM2),14:N0}");
                summary.AppendLine($"{"Cost/m\u00B2 GIA (incl. VAT):",-35} {(totalCost / giaM2),14:N0}");
            }
            if (zeroCostCount > 0 || noDescCount > 0 || unclassifiedCount > 0)
            {
                summary.AppendLine();
                summary.AppendLine("DATA QUALITY:");
                if (zeroCostCount > 0)
                    summary.AppendLine($"  {zeroCostCount} elements with zero cost");
                if (noDescCount > 0)
                    summary.AppendLine($"  {noDescCount} elements with no description");
                if (unclassifiedCount > 0)
                    summary.AppendLine($"  {unclassifiedCount} elements in General");
            }
            if (rateDefaultsApplied > 0)
                summary.AppendLine($"\n  {rateDefaultsApplied} elements used rate defaults from BOQ_TEMPLATE.csv");
            summary.AppendLine();
            summary.AppendLine($"Exported to: {exportPath}");

            TaskDialog.Show("BOQ Export", summary.ToString());
            return Result.Succeeded;
        }

        // ── Excel formatting helpers ──

        /// <summary>Set standard BOQ column widths: ITEM=7, DESC=55, QTY=10, UNIT=8, RATE=18, AMOUNT=20.</summary>
        private static void SetStandardColumnWidths(IXLWorksheet ws)
        {
            ws.Column(1).Width = 7;   // ITEM
            ws.Column(2).Width = 55;  // DESCRIPTION
            ws.Column(3).Width = 10;  // QTY
            ws.Column(4).Width = 8;   // UNIT
            ws.Column(5).Width = 18;  // RATE (UGX)
            ws.Column(6).Width = 20;  // AMOUNT (UGX)
            ws.Style.Font.FontName = "Arial";
            ws.Style.Font.FontSize = 10;
        }

        /// <summary>Write a merged bold cell spanning columns.</summary>
        private static void WriteMergedBold(IXLWorksheet ws, int row, int colStart, int colEnd,
            string text, double fontSize = 10)
        {
            var cell = ws.Cell(row, colStart);
            cell.Value = text;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = fontSize;
            ws.Range(row, colStart, row, colEnd).Merge();
        }

        /// <summary>Write 6-column header row with borders.</summary>
        private static void WriteColumnHeaders(IXLWorksheet ws, int row)
        {
            string[] headers = { "ITEM", "DESCRIPTION", "QTY", "UNIT", "RATE (UGX)", "AMOUNT (UGX)" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            var range = ws.Range(row, 1, row, 6);
            range.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            range.Style.Fill.BackgroundColor = XLColor.FromArgb(230, 230, 230);
        }

        /// <summary>Write an amount cell with number format and right-alignment.</summary>
        private static void WriteAmount(IXLWorksheet ws, int row, int col, double amount)
        {
            var cell = ws.Cell(row, col);
            cell.Value = Math.Round(amount, 0);
            cell.Style.NumberFormat.Format = "#,##0";
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        /// <summary>Write a "TO COLLECTION" / "TO SUMMARY" / total row with borders.</summary>
        private static void WriteTotalRow(IXLWorksheet ws, int row, string label, double amount)
        {
            var cell = ws.Cell(row, 2);
            cell.Value = label;
            cell.Style.Font.Bold = true;
            if (amount > 0) WriteAmount(ws, row, 6, amount);
            ws.Cell(row, 6).Style.Font.Bold = true;
            var range = ws.Range(row, 1, row, 6);
            range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            range.Style.Border.BottomBorder = XLBorderStyleValues.Double;
        }

        /// <summary>Convert level code to display name for section headings.</summary>
        private static string GetLevelDisplayName(string levelCode)
        {
            if (string.IsNullOrEmpty(levelCode) || levelCode == "XX") return "GENERAL";
            if (levelCode == "GF" || levelCode == "L00") return "GROUND FLOOR";
            if (levelCode == "RF") return "ROOF LEVEL";
            if (levelCode.StartsWith("B")) return $"BASEMENT {levelCode.Substring(1)}";
            if (levelCode.StartsWith("L") && levelCode.Length > 1)
            {
                string numStr = levelCode.Substring(1).TrimStart('0');
                if (string.IsNullOrEmpty(numStr)) return "GROUND FLOOR";
                int num = int.TryParse(numStr, out int n) ? n : 0;
                return num switch
                {
                    1 => "FIRST FLOOR",
                    2 => "SECOND FLOOR",
                    3 => "THIRD FLOOR",
                    4 => "FOURTH FLOOR",
                    5 => "FIFTH FLOOR",
                    _ => $"LEVEL {num}"
                };
            }
            return levelCode.ToUpperInvariant();
        }

        /// <summary>Convert system/category code to readable sub-section title.</summary>
        private static string GetSubSectionTitle(string code)
        {
            return code switch
            {
                "HVAC" => "HEATING, VENTILATION AND AIR CONDITIONING",
                "DCW" => "DOMESTIC COLD WATER",
                "DHW" => "DOMESTIC HOT WATER",
                "HWS" => "HOT WATER SERVICES",
                "SAN" => "SANITARY INSTALLATIONS",
                "RWD" => "RAINWATER DRAINAGE",
                "GAS" => "GAS INSTALLATIONS",
                "FP" => "FIRE PROTECTION",
                "FLS" => "FIRE ALARM SYSTEM",
                "LV" => "LOW VOLTAGE SYSTEMS",
                "COM" => "COMMUNICATIONS",
                "ICT" => "ICT INSTALLATIONS",
                "SEC" => "SECURITY INSTALLATIONS",
                "NCL" => "NURSE CALL SYSTEM",
                _ => code.ToUpperInvariant()
            };
        }

        /// <summary>Get item letter code (A, B, C... AA, AB...)</summary>
        private static string GetItemLetter(int index)
        {
            if (index < 26) return ((char)('A' + index)).ToString();
            return ((char)('A' + index / 26 - 1)).ToString() + ((char)('A' + index % 26)).ToString();
        }

        /// <summary>
        /// Measures true quantities from Revit geometry (length, area, volume).
        /// </summary>
        private static void MeasureRevitQuantities(Element el, BOQItem item)
        {
            try
            {
                if (el is Autodesk.Revit.DB.MEPCurve curve)
                {
                    Parameter lenParam = curve.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (lenParam != null)
                        item.MeasuredLength_m = lenParam.AsDouble() * 0.3048;
                }
                else if (el is Wall wall)
                {
                    Parameter lenParam = wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                    if (lenParam != null)
                        item.MeasuredLength_m = lenParam.AsDouble() * 0.3048;
                }

                Parameter areaParam = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (areaParam != null && areaParam.AsDouble() > 0)
                    item.MeasuredArea_m2 = areaParam.AsDouble() * 0.092903;

                Parameter volParam = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                if (volParam != null && volParam.AsDouble() > 0)
                    item.Volume_m3 = volParam.AsDouble() * 0.0283168;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"MeasureRevitQuantities {el.Id}: {ex.Message}");
            }
        }

        private static double ReadDouble(Element el, string paramName)
        {
            try
            {
                Parameter p = el.LookupParameter(paramName);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double)
                    return p.AsDouble();
                if (p.StorageType == StorageType.Integer)
                    return p.AsInteger();
                string s = p.AsString();
                return double.TryParse(s, out double d) ? d : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// DAT-007: Loads RATE_DEFAULTS section from BOQ_TEMPLATE.csv.
        /// Returns PROD_CODE → unit rate mapping for fallback pricing.
        /// </summary>
        private static Dictionary<string, double> LoadRateDefaults()
        {
            var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string csvPath = StingToolsApp.FindDataFile("BOQ_TEMPLATE.csv");
                if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath))
                    return rates;

                bool inRateSection = false;
                bool headerSkipped = false;
                foreach (string line in File.ReadLines(csvPath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("##")) continue;
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    if (trimmed.StartsWith("SECTION,RATE_DEFAULTS", StringComparison.OrdinalIgnoreCase))
                    {
                        inRateSection = true;
                        headerSkipped = false;
                        continue;
                    }

                    if (inRateSection && trimmed.StartsWith("SECTION,", StringComparison.OrdinalIgnoreCase))
                        break; // next section

                    if (!inRateSection) continue;

                    if (!headerSkipped)
                    {
                        headerSkipped = true; // skip PROD_CODE,UNIT_RATE_UGX,UNIT,DESCRIPTION header
                        continue;
                    }

                    var cols = StingToolsApp.ParseCsvLine(trimmed);
                    if (cols.Length >= 2 && !string.IsNullOrEmpty(cols[0]))
                    {
                        if (double.TryParse(cols[1], out double rate) && rate > 0)
                            rates[cols[0].Trim()] = rate;
                    }
                }

                if (rates.Count > 0)
                    StingLog.Info($"BOQ rate defaults loaded: {rates.Count} PROD codes");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Failed to load rate defaults: {ex.Message}");
            }
            return rates;
        }

        private class BOQItem
        {
            public int ElementId;
            public string Category, FamilyName, TypeName, Description, Tag;
            public string Discipline, Level, Location, Zone, System, Unit;
            public double UnitPrice, Quantity, TotalCost;
            public double MeasuredLength_m, MeasuredArea_m2, Volume_m3;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Template VG Consistency Audit
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Audits all STING view templates for VG property consistency:
    /// - Validates discipline colour palette alignment across templates
    /// - Checks presentation templates have correct transparency values
    /// - Verifies naming conventions match between templates and filters
    /// - Reports filter coverage gaps and orphaned references
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TemplateVGAuditCommand : IExternalCommand
    {
        // Expected discipline colours — must match TemplateCommands.DisciplineColors
        private static readonly Dictionary<string, (byte R, byte G, byte B)> ExpectedColors =
            new Dictionary<string, (byte, byte, byte)>
        {
            { "Mechanical", (0, 128, 255) },
            { "Electrical", (255, 200, 0) },
            { "Plumbing", (0, 180, 0) },
            { "Architectural", (160, 160, 160) },
            { "Structural", (200, 0, 0) },
            { "Fire Protection", (255, 100, 0) },
            { "Low Voltage", (160, 0, 200) },
            { "Conduits", (180, 180, 0) },
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            var report = new StringBuilder();
            report.AppendLine("STING Template VG Consistency Audit");
            report.AppendLine(new string('═', 50));
            report.AppendLine();

            int issues = 0;

            // Collect all STING templates
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && v.Name.StartsWith("STING"))
                .ToList();

            report.AppendLine($"STING Templates found: {templates.Count}");
            report.AppendLine();

            // Collect all STING filters
            var filters = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .Where(f => f.Name.StartsWith("STING"))
                .ToDictionary(f => f.Name, f => f);

            report.AppendLine($"STING Filters found: {filters.Count}");
            report.AppendLine();

            // 1. Check discipline colour consistency
            report.AppendLine("─── DISCIPLINE COLOUR CONSISTENCY ───");
            foreach (var tmpl in templates)
            {
                var filterIds = tmpl.GetFilters();
                foreach (ElementId fId in filterIds)
                {
                    var fe = doc.GetElement(fId) as ParameterFilterElement;
                    if (fe == null || !fe.Name.StartsWith("STING - ")) continue;

                    try
                    {
                        var ogs = tmpl.GetFilterOverrides(fId);
                        Color lineCol = ogs.ProjectionLineColor;
                        if (!lineCol.IsValid) continue;

                        // Check if colour matches expected discipline
                        foreach (var kvp in ExpectedColors)
                        {
                            if (!fe.Name.Contains(kvp.Key)) continue;
                            var expected = kvp.Value;
                            if (lineCol.Red != expected.R || lineCol.Green != expected.G || lineCol.Blue != expected.B)
                            {
                                // Only report if colour is non-default (not black, not halftone grey)
                                if (lineCol.Red != 0 || lineCol.Green != 0 || lineCol.Blue != 0)
                                {
                                    if (lineCol.Red != 128 || lineCol.Green != 128 || lineCol.Blue != 128)
                                    {
                                        report.AppendLine($"  MISMATCH: {tmpl.Name} → {fe.Name}");
                                        report.AppendLine($"    Expected: RGB({expected.R},{expected.G},{expected.B})");
                                        report.AppendLine($"    Actual:   RGB({lineCol.Red},{lineCol.Green},{lineCol.Blue})");
                                        issues++;
                                    }
                                }
                            }
                            break;
                        }
                    }
                    catch { }
                }
            }
            if (issues == 0) report.AppendLine("  All discipline colours consistent.");
            report.AppendLine();

            // 2. Check presentation template transparency
            int transIssues = 0;
            report.AppendLine("─── PRESENTATION TEMPLATE TRANSPARENCY ───");
            foreach (var tmpl in templates.Where(t => t.Name.Contains("Presentation") || t.Name.Contains("PRES")))
            {
                var filterIds = tmpl.GetFilters();
                foreach (ElementId fId in filterIds)
                {
                    try
                    {
                        var ogs = tmpl.GetFilterOverrides(fId);
                        int trans = ogs.Transparency;
                        // Presentation should have 15-40% transparency
                        if (trans > 0 && (trans < 10 || trans > 50))
                        {
                            var fe = doc.GetElement(fId);
                            report.AppendLine($"  UNUSUAL: {tmpl.Name} → {fe?.Name} transparency={trans}%");
                            transIssues++;
                        }
                    }
                    catch { }
                }
            }
            if (transIssues == 0) report.AppendLine("  All presentation transparencies in range.");
            issues += transIssues;
            report.AppendLine();

            // 3. Check filter coverage — every template should have all discipline filters
            int coverageIssues = 0;
            report.AppendLine("─── FILTER COVERAGE ───");
            string[] requiredFilters = { "Mechanical", "Electrical", "Plumbing", "Architectural", "Structural" };
            foreach (var tmpl in templates.Where(t => !t.Name.Contains("Area") && !t.Name.Contains("Monochrome") && !t.Name.Contains("Dark")))
            {
                var applied = tmpl.GetFilters()
                    .Select(id => doc.GetElement(id)?.Name ?? "")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missing = requiredFilters
                    .Where(rf => !applied.Any(a => a.Contains(rf)))
                    .ToList();

                if (missing.Count > 0)
                {
                    report.AppendLine($"  MISSING: {tmpl.Name} lacks: {string.Join(", ", missing)}");
                    coverageIssues++;
                }
            }
            if (coverageIssues == 0) report.AppendLine("  All templates have full discipline filter coverage.");
            issues += coverageIssues;
            report.AppendLine();

            // 4. Check naming convention consistency
            int namingIssues = 0;
            report.AppendLine("─── NAMING CONVENTIONS ───");
            foreach (var tmpl in templates)
            {
                if (!tmpl.Name.StartsWith("STING - "))
                {
                    report.AppendLine($"  NON-STANDARD: '{tmpl.Name}' should start with 'STING - '");
                    namingIssues++;
                }
            }
            foreach (var kvp in filters)
            {
                if (!kvp.Key.StartsWith("STING - "))
                {
                    report.AppendLine($"  NON-STANDARD: Filter '{kvp.Key}' should start with 'STING - '");
                    namingIssues++;
                }
            }
            if (namingIssues == 0) report.AppendLine("  All naming conventions consistent.");
            issues += namingIssues;
            report.AppendLine();

            // 5. Check orphaned filter references
            int orphanIssues = 0;
            report.AppendLine("─── ORPHANED FILTER REFERENCES ───");
            foreach (var tmpl in templates)
            {
                foreach (ElementId fId in tmpl.GetFilters())
                {
                    if (doc.GetElement(fId) == null)
                    {
                        report.AppendLine($"  ORPHAN: {tmpl.Name} references deleted filter Id={fId}");
                        orphanIssues++;
                    }
                }
            }
            if (orphanIssues == 0) report.AppendLine("  No orphaned filter references.");
            issues += orphanIssues;
            report.AppendLine();

            // 6. Detail level consistency
            int detailIssues = 0;
            report.AppendLine("─── DETAIL LEVEL CONSISTENCY ───");
            foreach (var tmpl in templates)
            {
                bool isPres = tmpl.Name.Contains("Presentation") || tmpl.Name.Contains("Detail");
                var dl = tmpl.DetailLevel;
                if (isPres && dl != ViewDetailLevel.Fine)
                {
                    report.AppendLine($"  WARN: {tmpl.Name} is presentation but DetailLevel={dl} (expected Fine)");
                    detailIssues++;
                }
            }
            if (detailIssues == 0) report.AppendLine("  All detail levels appropriate.");
            issues += detailIssues;

            // Summary
            report.AppendLine();
            report.AppendLine(new string('═', 50));
            report.AppendLine($"TOTAL ISSUES: {issues}");
            report.AppendLine(issues == 0 ? "All checks passed." : "Review issues above and run Auto-Fix if needed.");

            TaskDialog.Show("Template VG Audit", report.ToString());
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ENH-006: IFC Property Set Export Mapping
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates an IFC property set mapping table from PARAMETER_REGISTRY.json
    /// ifc_property_mapping section. Outputs a Revit-compatible IFC export
    /// parameter mapping file (.txt) that maps STING shared parameters to
    /// IFC IfcPropertySingleValue entries within named property sets.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportIfcPropertyMapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            // Load IFC mapping from PARAMETER_REGISTRY.json
            string regPath = StingToolsApp.FindDataFile("PARAMETER_REGISTRY.json");
            if (string.IsNullOrEmpty(regPath) || !File.Exists(regPath))
            {
                TaskDialog.Show("IFC Property Map", "PARAMETER_REGISTRY.json not found.");
                return Result.Failed;
            }

            JObject registry;
            try
            {
                registry = JObject.Parse(File.ReadAllText(regPath));
            }
            catch (Exception ex)
            {
                TaskDialog.Show("IFC Property Map", $"Failed to parse registry: {ex.Message}");
                return Result.Failed;
            }

            var ifcSection = registry["ifc_property_mapping"]?["mappings"] as JArray;
            if (ifcSection == null || ifcSection.Count == 0)
            {
                TaskDialog.Show("IFC Property Map",
                    "No ifc_property_mapping section found in PARAMETER_REGISTRY.json.\n" +
                    "Add mappings to enable IFC export.");
                return Result.Failed;
            }

            // Build the IFC parameter mapping file (Revit format)
            // Format: SubElement[TAB]PropertySet[TAB]RevitParamName[TAB]IfcParamName[TAB]Type
            var sb = new StringBuilder();
            sb.AppendLine("# STING Tools — IFC Property Set Mapping");
            sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"# Mappings: {ifcSection.Count}");
            sb.AppendLine("#");
            sb.AppendLine("# Format: SubElement<TAB>PropertySet<TAB>RevitParamName<TAB>IfcParamName<TAB>Type");
            sb.AppendLine();

            int mapped = 0;
            var propertySets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (JToken mapping in ifcSection)
            {
                string paramName = mapping["param_name"]?.ToString() ?? "";
                string pset = mapping["ifc_property_set"]?.ToString() ?? "";
                string ifcName = mapping["ifc_property_name"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(paramName) || string.IsNullOrEmpty(pset)) continue;

                // Determine type (text vs number)
                string ifcType = paramName.EndsWith("_NR") ? "Real" : "Text";

                sb.AppendLine($"\t{pset}\t{paramName}\t{ifcName}\t{ifcType}");
                mapped++;
                propertySets.Add(pset);
            }

            // Save the mapping file
            string dir = Path.GetDirectoryName(doc.PathName);
            if (string.IsNullOrEmpty(dir)) dir = StingToolsApp.DataPath ?? Path.GetTempPath();
            string outputPath = Path.Combine(dir, "STING_IFC_PropertyMap.txt");

            try
            {
                File.WriteAllText(outputPath, sb.ToString());
            }
            catch (Exception ex)
            {
                StingLog.Error("IFC property map export failed", ex);
                TaskDialog.Show("IFC Property Map", $"Export failed: {ex.Message}");
                return Result.Failed;
            }

            var report = new StringBuilder();
            report.AppendLine("IFC Property Set Mapping Export");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  Parameters mapped: {mapped}");
            report.AppendLine($"  Property sets: {propertySets.Count}");
            foreach (string ps in propertySets.OrderBy(x => x))
                report.AppendLine($"    • {ps}");
            report.AppendLine();
            report.AppendLine($"  Output: {outputPath}");
            report.AppendLine();
            report.AppendLine("To use: In Revit IFC Export dialog → Modify Setup →");
            report.AppendLine("Property Sets → Load this file as custom mapping.");

            TaskDialog.Show("IFC Property Map", report.ToString());
            StingLog.Info($"IFC property map exported: {mapped} mappings to {outputPath}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ENH-008: BEP (BIM Execution Plan) Validation
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates tag token values against a project BEP JSON file that defines
    /// allowed LOC/ZONE/DISC codes. Catches wrong discipline abbreviations,
    /// non-standard zone codes, and codes inconsistent with the project BEP.
    /// BEP file: project_bep.json in data directory with structure:
    ///   { "allowed_loc": ["BLD1","BLD2"], "allowed_zone": ["Z01","Z02"],
    ///     "allowed_disc": ["M","E","P","A","S"], "project_name": "..." }
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateBepComplianceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            // Load BEP from data directory
            string bepPath = StingToolsApp.FindDataFile("project_bep.json");
            if (string.IsNullOrEmpty(bepPath) || !File.Exists(bepPath))
            {
                TaskDialog.Show("BEP Validation",
                    "No project_bep.json found in data directory.\n\n" +
                    "Create a BEP file with allowed codes:\n" +
                    "{\n  \"allowed_loc\": [\"BLD1\", \"BLD2\"],\n" +
                    "  \"allowed_zone\": [\"Z01\", \"Z02\", \"Z03\"],\n" +
                    "  \"allowed_disc\": [\"M\", \"E\", \"P\", \"A\", \"S\"]\n}");
                return Result.Succeeded;
            }

            JObject bep;
            try
            {
                bep = JObject.Parse(File.ReadAllText(bepPath));
            }
            catch (Exception ex)
            {
                TaskDialog.Show("BEP Validation", $"Failed to parse BEP file: {ex.Message}");
                return Result.Failed;
            }

            var allowedLoc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedZone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedDisc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (bep["allowed_loc"] is JArray locArr)
                foreach (string v in locArr) allowedLoc.Add(v);
            if (bep["allowed_zone"] is JArray zoneArr)
                foreach (string v in zoneArr) allowedZone.Add(v);
            if (bep["allowed_disc"] is JArray discArr)
                foreach (string v in discArr) allowedDisc.Add(v);

            if (allowedLoc.Count == 0 && allowedZone.Count == 0 && allowedDisc.Count == 0)
            {
                TaskDialog.Show("BEP Validation", "BEP file contains no allowed code lists.");
                return Result.Succeeded;
            }

            // Scan all tagged elements
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            int scanned = 0;
            int violations = 0;
            var violationsByType = new Dictionary<string, int>
            {
                ["LOC"] = 0, ["ZONE"] = 0, ["DISC"] = 0
            };
            var sampleViolations = new List<string>();

            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;

                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag)) continue;
                scanned++;

                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);

                bool hasViolation = false;

                if (allowedDisc.Count > 0 && !string.IsNullOrEmpty(disc) && !allowedDisc.Contains(disc))
                {
                    violationsByType["DISC"]++;
                    hasViolation = true;
                }
                if (allowedLoc.Count > 0 && !string.IsNullOrEmpty(loc) && loc != "XX" && !allowedLoc.Contains(loc))
                {
                    violationsByType["LOC"]++;
                    hasViolation = true;
                }
                if (allowedZone.Count > 0 && !string.IsNullOrEmpty(zone) && zone != "XX" && zone != "ZZ" && !allowedZone.Contains(zone))
                {
                    violationsByType["ZONE"]++;
                    hasViolation = true;
                }

                if (hasViolation)
                {
                    violations++;
                    if (sampleViolations.Count < 10)
                        sampleViolations.Add($"  {el.Id}: {tag} (DISC={disc}, LOC={loc}, ZONE={zone})");
                }
            }

            var report = new StringBuilder();
            report.AppendLine("BEP Compliance Validation");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"  BEP file: {Path.GetFileName(bepPath)}");
            report.AppendLine($"  Scanned: {scanned} tagged elements");
            report.AppendLine($"  Violations: {violations}");
            report.AppendLine();

            if (allowedDisc.Count > 0)
                report.AppendLine($"  Allowed DISC: {string.Join(", ", allowedDisc)} — violations: {violationsByType["DISC"]}");
            if (allowedLoc.Count > 0)
                report.AppendLine($"  Allowed LOC:  {string.Join(", ", allowedLoc)} — violations: {violationsByType["LOC"]}");
            if (allowedZone.Count > 0)
                report.AppendLine($"  Allowed ZONE: {string.Join(", ", allowedZone)} — violations: {violationsByType["ZONE"]}");

            if (sampleViolations.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Sample violations:");
                foreach (string s in sampleViolations) report.AppendLine(s);
            }

            string status = violations == 0 ? "COMPLIANT" : $"{violations} VIOLATIONS";
            TaskDialog.Show("BEP Validation", report.ToString());
            StingLog.Info($"BEP validation: {scanned} scanned, {violations} violations");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  RULE-BASED CLASH DETECTION (P12)
    //  Uses ElementIntersectsSolidFilter for MEP-structure and MEP-MEP clashes.
    //  Removes Navisworks dependency for common BIM coordination checks.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Rule-based clash detection using Revit API geometry intersection.
    /// Detects pipe-duct, duct-beam, MEP-wall, and MEP-MEP interference.
    /// Exports clash report to CSV with element IDs, categories, and locations.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClashDetectionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            // Define clash groups: MEP vs Structure
            var mepCats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_FlexDuctCurves,
                BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory, BuiltInCategory.OST_FlexPipeCurves,
                BuiltInCategory.OST_Conduit, BuiltInCategory.OST_ConduitFitting,
                BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_ElectricalEquipment,
            };

            var structCats = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Floors, BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_StructuralFoundation,
            };

            // Collect MEP and structure elements
            var mepFilter = new ElementMulticategoryFilter(mepCats);
            var mepElements = new FilteredElementCollector(doc)
                .WherePasses(mepFilter)
                .WhereElementIsNotElementType()
                .ToList();

            var structFilter = new ElementMulticategoryFilter(structCats);
            var structElements = new FilteredElementCollector(doc)
                .WherePasses(structFilter)
                .WhereElementIsNotElementType()
                .ToList();

            if (mepElements.Count == 0)
            {
                TaskDialog.Show("Clash Detection", "No MEP elements found to check for clashes.");
                return Result.Succeeded;
            }

            StingLog.Info($"ClashDetection: checking {mepElements.Count} MEP vs {structElements.Count} structure elements");

            // Phase 1: Bounding box pre-filter for performance
            var clashes = new List<(Element mep, Element other, string type)>();
            int checked_count = 0;

            foreach (var mepEl in mepElements)
            {
                BoundingBoxXYZ mepBB = mepEl.get_BoundingBox(null);
                if (mepBB == null) continue;

                // Check vs structure
                foreach (var strEl in structElements)
                {
                    BoundingBoxXYZ strBB = strEl.get_BoundingBox(null);
                    if (strBB == null) continue;

                    if (BoundingBoxesOverlap(mepBB, strBB))
                    {
                        // Refined check with solid intersection
                        try
                        {
                            Solid mepSolid = GetSolid(mepEl);
                            if (mepSolid != null)
                            {
                                var intersectFilter = new ElementIntersectsSolidFilter(mepSolid);
                                var hits = new FilteredElementCollector(doc)
                                    .WherePasses(intersectFilter)
                                    .Where(e => e.Id == strEl.Id)
                                    .Any();
                                if (hits)
                                {
                                    string mepCat = ParameterHelpers.GetCategoryName(mepEl);
                                    string strCat = ParameterHelpers.GetCategoryName(strEl);
                                    clashes.Add((mepEl, strEl, $"{mepCat} vs {strCat}"));
                                }
                            }
                        }
                        catch { /* Solid extraction can fail on some families */ }
                    }
                    checked_count++;
                }

                // Check MEP vs MEP (same discipline cross-check)
                foreach (var otherMep in mepElements)
                {
                    if (otherMep.Id.Value <= mepEl.Id.Value) continue; // Avoid duplicates
                    BoundingBoxXYZ otherBB = otherMep.get_BoundingBox(null);
                    if (otherBB == null) continue;

                    // Only cross-discipline: don't flag pipe fitting touching pipe
                    string mepCat = mepEl.Category?.Name ?? "";
                    string otherCat = otherMep.Category?.Name ?? "";
                    if (mepCat == otherCat) continue;

                    string mepDisc = TagConfig.DiscMap.ContainsKey(ParameterHelpers.GetCategoryName(mepEl))
                        ? TagConfig.DiscMap[ParameterHelpers.GetCategoryName(mepEl)] : "";
                    string otherDisc = TagConfig.DiscMap.ContainsKey(ParameterHelpers.GetCategoryName(otherMep))
                        ? TagConfig.DiscMap[ParameterHelpers.GetCategoryName(otherMep)] : "";
                    if (mepDisc == otherDisc) continue;

                    if (BoundingBoxesOverlap(mepBB, otherBB))
                    {
                        try
                        {
                            Solid mepSolid = GetSolid(mepEl);
                            if (mepSolid != null)
                            {
                                bool hits = new FilteredElementCollector(doc)
                                    .WherePasses(new ElementIntersectsSolidFilter(mepSolid))
                                    .Where(e => e.Id == otherMep.Id)
                                    .Any();
                                if (hits)
                                    clashes.Add((mepEl, otherMep, $"{mepCat} vs {otherCat} (cross-discipline)"));
                            }
                        }
                        catch { }
                    }
                }

                // Cancel check
                if (checked_count % 500 == 0 && EscapeChecker.IsEscapePressed())
                {
                    StingLog.Info("ClashDetection: cancelled by user");
                    break;
                }
            }

            // Report results
            var report = new StringBuilder();
            report.AppendLine($"STING Clash Detection Report");
            report.AppendLine(new string('═', 50));
            report.AppendLine($"MEP elements: {mepElements.Count}");
            report.AppendLine($"Structure elements: {structElements.Count}");
            report.AppendLine($"Clashes found: {clashes.Count}");
            report.AppendLine();

            // Group by type
            var grouped = clashes.GroupBy(c => c.type).OrderByDescending(g => g.Count());
            foreach (var g in grouped.Take(10))
            {
                report.AppendLine($"  {g.Key}: {g.Count()} clashes");
                foreach (var c in g.Take(3))
                {
                    string loc1 = FormatLocation(c.mep);
                    report.AppendLine($"    ID {c.mep.Id.Value} @ {loc1} ↔ ID {c.other.Id.Value}");
                }
                if (g.Count() > 3) report.AppendLine($"    ... and {g.Count() - 3} more");
            }

            // Export to CSV
            if (clashes.Count > 0)
            {
                try
                {
                    string csvPath = Path.Combine(
                        Path.GetDirectoryName(doc.PathName ?? Path.GetTempPath()) ?? Path.GetTempPath(),
                        $"STING_CLASH_REPORT_{DateTime.Now:yyyyMMdd_HHmm}.csv");

                    var csv = new StringBuilder();
                    csv.AppendLine("ClashType,MEP_ElementId,MEP_Category,MEP_Tag,Other_ElementId,Other_Category,Level");

                    foreach (var c in clashes)
                    {
                        string mepTag = ParameterHelpers.GetString(c.mep, ParamRegistry.TAG1);
                        string lvl = ParameterHelpers.GetString(c.mep, ParamRegistry.LVL);
                        csv.AppendLine($"\"{c.type}\",{c.mep.Id.Value}," +
                            $"\"{ParameterHelpers.GetCategoryName(c.mep)}\",\"{mepTag}\"," +
                            $"{c.other.Id.Value},\"{ParameterHelpers.GetCategoryName(c.other)}\",\"{lvl}\"");
                    }

                    File.WriteAllText(csvPath, csv.ToString());
                    report.AppendLine($"\nCSV exported: {csvPath}");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ClashDetection CSV export: {ex.Message}");
                }
            }

            TaskDialog.Show("Clash Detection", report.ToString());
            StingLog.Info($"ClashDetection: {clashes.Count} clashes found");
            return Result.Succeeded;
        }

        private static bool BoundingBoxesOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                   a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
                   a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;
        }

        private static Solid GetSolid(Element el)
        {
            var geoOpts = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Coarse };
            GeometryElement geoEl = el.get_Geometry(geoOpts);
            if (geoEl == null) return null;

            foreach (GeometryObject gObj in geoEl)
            {
                if (gObj is Solid s && s.Volume > 0) return s;
                if (gObj is GeometryInstance gi)
                {
                    foreach (GeometryObject iObj in gi.GetInstanceGeometry())
                    {
                        if (iObj is Solid is2 && is2.Volume > 0) return is2;
                    }
                }
            }
            return null;
        }

        private static string FormatLocation(Element el)
        {
            BoundingBoxXYZ bb = el.get_BoundingBox(null);
            if (bb == null) return "?";
            XYZ c = (bb.Min + bb.Max) / 2.0;
            return $"({c.X:F1}, {c.Y:F1}, {c.Z:F1})";
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  IFC EXPORT AUTOMATION (P13)
    //  Automated IFC 2x3/4 export with correct STING property sets.
    //  Uses Revit's built-in IFC exporter with custom property set mapping.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Automated IFC export with STING property set configuration.
    /// Generates IFC parameter mapping file, configures export options,
    /// and exports to IFC 2x3 or IFC 4 with ISO 19650 properties.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class IFCExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            if (string.IsNullOrEmpty(doc.PathName))
            {
                TaskDialog.Show("IFC Export", "Save the project before exporting to IFC.");
                return Result.Cancelled;
            }

            // Choose IFC version
            var versionDlg = new TaskDialog("IFC Export");
            versionDlg.MainInstruction = "Select IFC export version";
            versionDlg.MainContent =
                "The export will include STING property sets:\n" +
                "  - STING_AssetTag (tag tokens, containers)\n" +
                "  - STING_AssetLifecycle (status, revision)\n" +
                "  - STING_AssetIdentity (description, manufacturer, model)\n" +
                "  - STING_AssetCost (unit cost, area)";
            versionDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "IFC 4 (Recommended)", "ISO 16739-1:2018 — latest standard");
            versionDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "IFC 2x3", "Legacy — broader software compatibility");

            var vResult = versionDlg.Show();
            bool useIfc4 = vResult == TaskDialogResult.CommandLink1;
            if (vResult != TaskDialogResult.CommandLink1 && vResult != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            // Generate the IFC property mapping file
            string projectDir = Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath();
            string mappingPath = Path.Combine(projectDir, "STING_IFC_MAPPING.txt");

            GeneratePropertyMappingFile(mappingPath);

            // Set up IFC export options
            string ifcFileName = Path.GetFileNameWithoutExtension(doc.PathName) +
                                 $"_STING_{DateTime.Now:yyyyMMdd}";

            // Use Revit IFC export
            try
            {
                var ifcOptions = new IFCExportOptions();
                ifcOptions.FileVersion = useIfc4 ? IFCVersion.IFC4 : IFCVersion.IFC2x3CV2;
                ifcOptions.SpaceBoundaryLevel = 1;
                ifcOptions.ExportBaseQuantities = true;
                ifcOptions.WallAndColumnSplitting = true;
                ifcOptions.AddOption("ExportInternalRevitPropertySets", "true");
                ifcOptions.AddOption("ExportIFCCommonPropertySets", "true");
                ifcOptions.AddOption("ExportUserDefinedPsets", "true");
                ifcOptions.AddOption("ExportUserDefinedPsetsFileName", mappingPath);

                doc.Export(projectDir, ifcFileName, ifcOptions);

                string version = useIfc4 ? "IFC 4" : "IFC 2x3";
                string outputPath = Path.Combine(projectDir, ifcFileName + ".ifc");
                TaskDialog.Show("IFC Export",
                    $"Export complete ({version}).\n\n" +
                    $"File: {outputPath}\n" +
                    $"Mapping: {mappingPath}\n\n" +
                    "STING property sets included:\n" +
                    "  STING_AssetTag\n" +
                    "  STING_AssetLifecycle\n" +
                    "  STING_AssetIdentity\n" +
                    "  STING_AssetCost");

                StingLog.Info($"IFC export: {version} → {outputPath}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("IFC Export", $"Export failed: {ex.Message}");
                StingLog.Error("IFC export failed", ex);
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        private static void GeneratePropertyMappingFile(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# STING IFC Property Mapping File");
            sb.AppendLine("# Generated by StingTools IFC Export");
            sb.AppendLine();

            // STING_AssetTag property set
            sb.AppendLine("PropertySet:\tSTING_AssetTag\tI\t" +
                "IfcBuildingElement,IfcDistributionElement,IfcFlowTerminal,IfcEnergyConversionDevice");
            sb.AppendLine($"\t{ParamRegistry.TAG1}\tText");
            sb.AppendLine($"\t{ParamRegistry.DISC}\tText");
            sb.AppendLine($"\t{ParamRegistry.LOC}\tText");
            sb.AppendLine($"\t{ParamRegistry.ZONE}\tText");
            sb.AppendLine($"\t{ParamRegistry.LVL}\tText");
            sb.AppendLine($"\t{ParamRegistry.SYS}\tText");
            sb.AppendLine($"\t{ParamRegistry.FUNC}\tText");
            sb.AppendLine($"\t{ParamRegistry.PROD}\tText");
            sb.AppendLine($"\t{ParamRegistry.SEQ}\tText");

            // STING_AssetLifecycle
            sb.AppendLine();
            sb.AppendLine("PropertySet:\tSTING_AssetLifecycle\tI\t" +
                "IfcBuildingElement,IfcDistributionElement");
            sb.AppendLine($"\t{ParamRegistry.STATUS}\tText");
            sb.AppendLine($"\t{ParamRegistry.REV}\tText");

            // STING_AssetIdentity
            sb.AppendLine();
            sb.AppendLine("PropertySet:\tSTING_AssetIdentity\tI\t" +
                "IfcBuildingElement,IfcDistributionElement");
            string descParam = ParamRegistry.GetParamName("ASS_DESCRIPTION_TXT") ?? "ASS_DESCRIPTION_TXT";
            string mfgParam = ParamRegistry.GetParamName("ASS_MANUFACTURER_TXT") ?? "ASS_MANUFACTURER_TXT";
            string modelParam = ParamRegistry.GetParamName("ASS_MODEL_TXT") ?? "ASS_MODEL_TXT";
            sb.AppendLine($"\t{descParam}\tText");
            sb.AppendLine($"\t{mfgParam}\tText");
            sb.AppendLine($"\t{modelParam}\tText");

            // STING_AssetCost
            sb.AppendLine();
            sb.AppendLine("PropertySet:\tSTING_AssetCost\tI\t" +
                "IfcBuildingElement,IfcDistributionElement");
            string costParam = ParamRegistry.GetParamName("ASS_UNIT_COST_TXT") ?? "ASS_UNIT_COST_TXT";
            string areaParam = ParamRegistry.GetParamName("ASS_AREA_M2_TXT") ?? "ASS_AREA_M2_TXT";
            sb.AppendLine($"\t{costParam}\tText");
            sb.AppendLine($"\t{areaParam}\tText");

            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"IFC mapping file generated: {path}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  EXCEL ROUND-TRIP IMPORT (P9)
    //  Import BOQ/schedule data back from Excel into Revit parameters.
    //  Closes the quantity management loop for estimators working in Excel.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Import BOQ data from Excel back into Revit element parameters.
    /// Matches elements by STING tag (ASS_TAG_1) and updates unit cost,
    /// quantity, description, and other editable parameters.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExcelBOQImportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            // Find Excel files in the project directory
            string projectDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName)
                : null;

            string xlsxPath = null;
            if (projectDir != null)
            {
                // Look for STING BOQ export files
                var candidates = Directory.GetFiles(projectDir, "STING_BOQ_*.xlsx")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();
                if (candidates.Count > 0) xlsxPath = candidates[0];
            }

            if (xlsxPath == null)
            {
                TaskDialog.Show("Excel BOQ Import",
                    "No STING BOQ Excel file found in the project directory.\n\n" +
                    "Expected file pattern: STING_BOQ_*.xlsx\n" +
                    "Export a BOQ first using the BOQ Export command, edit in Excel,\n" +
                    "then run this command to import changes back.");
                return Result.Cancelled;
            }

            StingLog.Info($"Excel import: reading {xlsxPath}");

            // Read Excel using ClosedXML
            var updates = new List<(string tag, string param, string value)>();
            try
            {
                using (var workbook = new ClosedXML.Excel.XLWorkbook(xlsxPath))
                {
                    var ws = workbook.Worksheets.First();
                    int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                    if (lastRow < 2)
                    {
                        TaskDialog.Show("Excel BOQ Import", "Excel file has no data rows.");
                        return Result.Cancelled;
                    }

                    // Find column headers
                    var headers = new Dictionary<int, string>();
                    int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                    for (int c = 1; c <= lastCol; c++)
                        headers[c] = ws.Cell(1, c).GetString().Trim();

                    int tagCol = headers.FirstOrDefault(h => h.Value == "Tag" || h.Value == "ASS_TAG_1").Key;
                    int costCol = headers.FirstOrDefault(h => h.Value == "Unit_Rate" || h.Value == "UnitPrice").Key;
                    int descCol = headers.FirstOrDefault(h => h.Value == "Description").Key;
                    int qtyCol = headers.FirstOrDefault(h => h.Value == "Quantity" || h.Value == "Qty").Key;

                    if (tagCol == 0)
                    {
                        TaskDialog.Show("Excel BOQ Import",
                            "Cannot find 'Tag' or 'ASS_TAG_1' column in Excel.\n" +
                            "The file must have a column identifying elements by STING tag.");
                        return Result.Cancelled;
                    }

                    for (int r = 2; r <= lastRow; r++)
                    {
                        string tag = ws.Cell(r, tagCol).GetString().Trim();
                        if (string.IsNullOrEmpty(tag)) continue;

                        if (costCol > 0)
                        {
                            string cost = ws.Cell(r, costCol).GetString().Trim();
                            if (!string.IsNullOrEmpty(cost))
                                updates.Add((tag, "ASS_UNIT_COST_TXT", cost));
                        }
                        if (descCol > 0)
                        {
                            string desc = ws.Cell(r, descCol).GetString().Trim();
                            if (!string.IsNullOrEmpty(desc))
                                updates.Add((tag, "ASS_DESCRIPTION_TXT", desc));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Excel BOQ Import", $"Error reading Excel: {ex.Message}");
                StingLog.Error("Excel import read failed", ex);
                return Result.Failed;
            }

            if (updates.Count == 0)
            {
                TaskDialog.Show("Excel BOQ Import", "No updatable data found in the Excel file.");
                return Result.Succeeded;
            }

            // Build element index by tag
            var tagIndex = new Dictionary<string, Element>();
            foreach (var el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string t = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(t) && !tagIndex.ContainsKey(t))
                    tagIndex[t] = el;
            }

            // Confirm
            int matchable = updates.Select(u => u.tag).Distinct().Count(t => tagIndex.ContainsKey(t));
            var confirm = new TaskDialog("Excel BOQ Import");
            confirm.MainInstruction = $"Import {updates.Count} parameter updates";
            confirm.MainContent =
                $"Source: {Path.GetFileName(xlsxPath)}\n" +
                $"Updates: {updates.Count} values across {updates.Select(u => u.tag).Distinct().Count()} tags\n" +
                $"Matchable elements: {matchable}\n\n" +
                "Proceed with import?";
            confirm.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            if (confirm.Show() == TaskDialogResult.Cancel)
                return Result.Cancelled;

            // Apply updates
            int applied = 0, notFound = 0;
            using (Transaction tx = new Transaction(doc, "STING Excel BOQ Import"))
            {
                tx.Start();
                foreach (var (tag, param, value) in updates)
                {
                    if (tagIndex.TryGetValue(tag, out Element el))
                    {
                        ParameterHelpers.SetString(el, param, value, overwrite: true);
                        applied++;
                    }
                    else
                    {
                        notFound++;
                    }
                }
                tx.Commit();
            }

            TaskDialog.Show("Excel BOQ Import",
                $"Import complete:\n\n" +
                $"  Applied: {applied}\n" +
                $"  Not found: {notFound}\n" +
                $"  Source: {Path.GetFileName(xlsxPath)}");

            StingLog.Info($"Excel import: {applied} applied, {notFound} not found from {xlsxPath}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  COBie EXPORT (BS 1192-4 / ISO 19650 handover format)
    //  Exports asset data in COBie-compliant spreadsheet format using ClosedXML.
    //  Maps STING parameters to COBie worksheets (Facility, Floor, Space,
    //  Type, Component, System, Zone, Attribute).
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exports project data in COBie (Construction Operations Building
    /// Information Exchange) spreadsheet format for FM handover.
    /// Creates an Excel workbook with standard COBie worksheets.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class COBieExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();
            string outputPath = Path.Combine(outputDir,
                $"STING_COBie_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");

            try
            {
                using (var wb = new XLWorkbook())
                {
                    // ── Facility sheet ──
                    var wsFacility = wb.AddWorksheet("Facility");
                    COBieHelper.WriteHeaders(wsFacility,
                        "Name", "ProjectName", "SiteName", "LinearUnits",
                        "AreaUnits", "VolumeUnits", "CurrencyUnit", "AreaMeasurement",
                        "Description", "ProjectDescription", "SiteDescription", "Phase");
                    var pi = doc.ProjectInformation;
                    string projName = pi?.Name ?? "Unnamed";
                    string projNumber = pi?.Number ?? "";
                    string buildingName = pi?.BuildingName ?? "";
                    COBieHelper.WriteRow(wsFacility, 2,
                        buildingName, projName, projName, "meters",
                        "square meters", "cubic meters", "GBP", "NRM",
                        $"STING exported {DateTime.Now:yyyy-MM-dd}", projNumber, "", "New Construction");

                    // ── Floor sheet ──
                    var wsFloor = wb.AddWorksheet("Floor");
                    COBieHelper.WriteHeaders(wsFloor,
                        "Name", "Category", "Elevation", "Height", "Description");

                    var levels = new FilteredElementCollector(doc)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .OrderBy(l => l.Elevation)
                        .ToList();

                    int floorRow = 2;
                    foreach (var level in levels)
                    {
                        double elevM = level.Elevation * 0.3048;
                        COBieHelper.WriteRow(wsFloor, floorRow++,
                            level.Name, "Floor", $"{elevM:F2}", "", level.Name);
                    }

                    // ── Space sheet (rooms) ──
                    var wsSpace = wb.AddWorksheet("Space");
                    COBieHelper.WriteHeaders(wsSpace,
                        "Name", "FloorName", "Description", "Category",
                        "RoomTag", "UsableHeight", "GrossArea", "NetArea");

                    var rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Autodesk.Revit.DB.Architecture.Room>()
                        .Where(r => r.Location != null && r.Area > 0)
                        .OrderBy(r => r.Level?.Name ?? "")
                        .ThenBy(r => r.Number ?? "")
                        .ToList();

                    int spaceRow = 2;
                    foreach (var room in rooms)
                    {
                        double areaSqM = room.Area * 0.092903;
                        string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Number;
                        string dept = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                        COBieHelper.WriteRow(wsSpace, spaceRow++,
                            roomName, room.Level?.Name ?? "", dept, "Room",
                            room.Number ?? "", "", $"{areaSqM:F2}", $"{areaSqM:F2}");
                    }

                    // ── Type sheet (family types) ──
                    var wsType = wb.AddWorksheet("Type");
                    COBieHelper.WriteHeaders(wsType,
                        "Name", "Category", "Description", "Manufacturer",
                        "ModelNumber", "AssetType", "NominalLength", "NominalWidth", "NominalHeight");

                    var taggableCategories = SharedParamGuids.AllCategoryEnums;
                    var typeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int typeRow = 2;

                    foreach (var bic in taggableCategories)
                    {
                        try
                        {
                            var types = new FilteredElementCollector(doc)
                                .OfCategory(bic)
                                .WhereElementIsElementType()
                                .ToList();

                            foreach (var type in types)
                            {
                                string typeName = type.Name;
                                if (typeNames.Contains(typeName)) continue;
                                typeNames.Add(typeName);

                                string catName = type.Category?.Name ?? "";
                                string desc = ParameterHelpers.GetString(type, "ASS_DESCRIPTION_TXT");
                                string mfg = ParameterHelpers.GetString(type, "ASS_MANUFACTURER_TXT");
                                string model = ParameterHelpers.GetString(type, "ASS_MODEL_TXT");

                                COBieHelper.WriteRow(wsType, typeRow++,
                                    typeName, catName, desc, mfg, model, "Fixed", "", "", "");

                                if (typeRow > 5000) break; // Safety limit
                            }
                        }
                        catch { }
                        if (typeRow > 5000) break;
                    }

                    // ── Component sheet (instances) ──
                    var wsComponent = wb.AddWorksheet("Component");
                    COBieHelper.WriteHeaders(wsComponent,
                        "Name", "TypeName", "Space", "FloorName",
                        "Description", "SerialNumber", "AssetIdentifier",
                        "STING_Tag", "BarCode");

                    int compRow = 2;
                    foreach (var bic in taggableCategories)
                    {
                        try
                        {
                            var instances = new FilteredElementCollector(doc)
                                .OfCategory(bic)
                                .WhereElementIsNotElementType()
                                .ToList();

                            foreach (var el in instances)
                            {
                                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                                string typeName = "";
                                if (el is FamilyInstance fi && fi.Symbol != null)
                                    typeName = fi.Symbol.Name;
                                else
                                    typeName = el.Name;

                                string mark = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                                string desc = ParameterHelpers.GetString(el, "ASS_DESCRIPTION_TXT");
                                string levelName = "";
                                try { levelName = (doc.GetElement(el.LevelId) as Level)?.Name ?? ""; }
                                catch { }

                                string roomName = "";
                                try
                                {
                                    var room = ParameterHelpers.GetRoomAtElement(doc, el);
                                    if (room != null) roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                                }
                                catch { }

                                COBieHelper.WriteRow(wsComponent, compRow++,
                                    $"{typeName}-{mark}", typeName, roomName, levelName,
                                    desc, "", tag, tag, "");

                                if (compRow > 10000) break;
                            }
                        }
                        catch { }
                        if (compRow > 10000) break;
                    }

                    // ── System sheet ──
                    var wsSystem = wb.AddWorksheet("System");
                    COBieHelper.WriteHeaders(wsSystem,
                        "Name", "Category", "ComponentNames", "Description");

                    var sysCodes = new HashSet<string>();
                    foreach (var bic in taggableCategories)
                    {
                        try
                        {
                            foreach (var el in new FilteredElementCollector(doc).OfCategory(bic).WhereElementIsNotElementType())
                            {
                                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                                if (!string.IsNullOrEmpty(sys)) sysCodes.Add(sys);
                            }
                        }
                        catch { }
                    }
                    int sysRow = 2;
                    foreach (string sys in sysCodes.OrderBy(s => s))
                    {
                        COBieHelper.WriteRow(wsSystem, sysRow++, sys, "System", "", $"{sys} System");
                    }

                    // ── Zone sheet ──
                    var wsZone = wb.AddWorksheet("Zone");
                    COBieHelper.WriteHeaders(wsZone,
                        "Name", "Category", "SpaceNames", "Description");

                    var zoneCodes = rooms
                        .Select(r => ParameterHelpers.GetString(r, ParamRegistry.ZONE))
                        .Where(z => !string.IsNullOrEmpty(z))
                        .Distinct()
                        .OrderBy(z => z);
                    int zoneRow = 2;
                    foreach (string zone in zoneCodes)
                    {
                        var zoneRooms = rooms.Where(r => ParameterHelpers.GetString(r, ParamRegistry.ZONE) == zone)
                            .Select(r => r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "")
                            .Take(20);
                        COBieHelper.WriteRow(wsZone, zoneRow++,
                            zone, "Zone", string.Join(", ", zoneRooms), $"Zone {zone}");
                    }

                    wb.SaveAs(outputPath);
                }

                TaskDialog.Show("COBie Export",
                    $"COBie export complete.\n\n" +
                    $"File: {outputPath}\n\n" +
                    "Worksheets:\n" +
                    "  Facility, Floor, Space, Type, Component, System, Zone");

                StingLog.Info($"COBie export: {outputPath}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("COBie Export", $"Export failed: {ex.Message}");
                StingLog.Error("COBie export failed", ex);
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }

    internal static class COBieHelper
    {
        public static void WriteHeaders(IXLWorksheet ws, params string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
            }
        }

        public static void WriteRow(IXLWorksheet ws, int row, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
                ws.Cell(row, i + 1).Value = values[i] ?? "";
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  JSON DATA EXPORT
    //  Export element data, tags, compliance results, and project info as JSON
    //  for data interchange with external tools, dashboards, and BIM platforms.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exports all tagged element data as a structured JSON file for
    /// data interchange. Includes tag tokens, spatial data, identity,
    /// and compliance status for each element.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class JSONExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            // Scope dialog
            var scopeDlg = new TaskDialog("JSON Export");
            scopeDlg.MainInstruction = "Select export scope";
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Tagged elements only", "Export only elements with STING tags");
            scopeDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "All taggable elements", "Export all elements in taggable categories");
            var scopeResult = scopeDlg.Show();
            bool taggedOnly = scopeResult == TaskDialogResult.CommandLink1;
            if (scopeResult != TaskDialogResult.CommandLink1 && scopeResult != TaskDialogResult.CommandLink2)
                return Result.Cancelled;

            var taggableCategories = SharedParamGuids.AllCategoryEnums;
            var allElements = new List<Element>();

            foreach (var bic in taggableCategories)
            {
                try
                {
                    allElements.AddRange(new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .ToList());
                }
                catch { }
            }

            if (taggedOnly)
                allElements = allElements.Where(e =>
                    !string.IsNullOrEmpty(ParameterHelpers.GetString(e, ParamRegistry.TAG1))).ToList();

            if (allElements.Count == 0)
            {
                TaskDialog.Show("JSON Export", "No elements found for export.");
                return Result.Succeeded;
            }

            // Build JSON structure
            var projectInfo = new JObject
            {
                ["name"] = doc.ProjectInformation?.Name ?? "",
                ["number"] = doc.ProjectInformation?.Number ?? "",
                ["exportDate"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["stingVersion"] = "1.0.0",
                ["elementCount"] = allElements.Count
            };

            var elementArray = new JArray();

            foreach (var el in allElements)
            {
                string catName = el.Category?.Name ?? "";
                string familyName = ParameterHelpers.GetFamilyName(el);
                string typeName = ParameterHelpers.GetFamilySymbolName(el);

                var elObj = new JObject
                {
                    ["elementId"] = el.Id.IntegerValue,
                    ["category"] = catName,
                    ["family"] = familyName,
                    ["type"] = typeName,
                    ["mark"] = el.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                };

                // Tag tokens
                var tags = new JObject
                {
                    ["tag"] = ParameterHelpers.GetString(el, ParamRegistry.TAG1),
                    ["disc"] = ParameterHelpers.GetString(el, ParamRegistry.DISC),
                    ["loc"] = ParameterHelpers.GetString(el, ParamRegistry.LOC),
                    ["zone"] = ParameterHelpers.GetString(el, ParamRegistry.ZONE),
                    ["lvl"] = ParameterHelpers.GetString(el, ParamRegistry.LVL),
                    ["sys"] = ParameterHelpers.GetString(el, ParamRegistry.SYS),
                    ["func"] = ParameterHelpers.GetString(el, ParamRegistry.FUNC),
                    ["prod"] = ParameterHelpers.GetString(el, ParamRegistry.PROD),
                    ["seq"] = ParameterHelpers.GetString(el, ParamRegistry.SEQ),
                    ["status"] = ParameterHelpers.GetString(el, ParamRegistry.STATUS),
                };
                elObj["tags"] = tags;

                // Spatial data
                var spatial = new JObject();
                try
                {
                    var lvl = doc.GetElement(el.LevelId) as Level;
                    if (lvl != null) spatial["level"] = lvl.Name;
                }
                catch { }

                try
                {
                    var room = ParameterHelpers.GetRoomAtElement(doc, el);
                    if (room != null)
                    {
                        spatial["room"] = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                        spatial["roomNumber"] = room.Number ?? "";
                        spatial["department"] = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)?.AsString() ?? "";
                    }
                }
                catch { }

                try
                {
                    var bb = el.get_BoundingBox(null);
                    if (bb != null)
                    {
                        var center = (bb.Min + bb.Max) / 2.0;
                        spatial["x"] = Math.Round(center.X * 0.3048, 3);
                        spatial["y"] = Math.Round(center.Y * 0.3048, 3);
                        spatial["z"] = Math.Round(center.Z * 0.3048, 3);
                    }
                }
                catch { }
                elObj["spatial"] = spatial;

                // Identity data
                var identity = new JObject
                {
                    ["description"] = ParameterHelpers.GetString(el, "ASS_DESCRIPTION_TXT"),
                    ["manufacturer"] = ParameterHelpers.GetString(el, "ASS_MANUFACTURER_TXT"),
                    ["model"] = ParameterHelpers.GetString(el, "ASS_MODEL_TXT"),
                };
                elObj["identity"] = identity;

                // Tag completeness
                string tagValue = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                elObj["isTagComplete"] = TagConfig.TagIsComplete(tagValue);

                elementArray.Add(elObj);
            }

            var root = new JObject
            {
                ["project"] = projectInfo,
                ["tagFormat"] = new JObject
                {
                    ["separator"] = ParamRegistry.Separator,
                    ["numPad"] = ParamRegistry.NumPad,
                    ["segmentOrder"] = new JArray(ParamRegistry.SegmentOrder)
                },
                ["elements"] = elementArray
            };

            // Write JSON file
            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();
            string jsonPath = Path.Combine(outputDir,
                $"STING_Export_{DateTime.Now:yyyyMMdd_HHmm}.json");

            File.WriteAllText(jsonPath, root.ToString(Formatting.Indented));

            // Summary
            var discCounts = allElements
                .GroupBy(e => ParameterHelpers.GetString(e, ParamRegistry.DISC))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Count());

            var sb = new StringBuilder();
            sb.AppendLine("JSON Export Complete");
            sb.AppendLine(new string('═', 40));
            sb.AppendLine($"  Elements exported: {allElements.Count}");
            sb.AppendLine($"  File: {jsonPath}");
            sb.AppendLine($"  Size: {new FileInfo(jsonPath).Length / 1024:N0} KB");
            sb.AppendLine();
            sb.AppendLine("By Discipline:");
            foreach (var g in discCounts)
                sb.AppendLine($"  {g.Key,-6} {g.Count(),5} elements");

            TaskDialog.Show("JSON Export", sb.ToString());
            StingLog.Info($"JSON export: {allElements.Count} elements → {jsonPath}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  CONFIGURE TAG FORMAT
    //  Interactive command to set tag separator, sequence padding, and
    //  segment order. Persists to project_config.json.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Interactively configure the ISO 19650 tag format: separator character,
    /// sequence number padding, and segment order. Changes are persisted to
    /// project_config.json and take effect immediately.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConfigureTagFormatCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string currentSep = ParamRegistry.Separator;
            int currentPad = ParamRegistry.NumPad;
            string currentOrder = string.Join(", ", ParamRegistry.SegmentOrder);

            // Show current config
            var dlg = new TaskDialog("Configure Tag Format");
            dlg.MainInstruction = "Tag Format Configuration";
            dlg.MainContent =
                $"Current settings:\n" +
                $"  Separator: '{currentSep}'\n" +
                $"  Sequence padding: {currentPad} digits\n" +
                $"  Segment order: {currentOrder}\n\n" +
                $"Example tag: M{currentSep}BLD1{currentSep}Z01{currentSep}L02{currentSep}HVAC{currentSep}SUP{currentSep}AHU{currentSep}{"1".PadLeft(currentPad, '0')}";

            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Use hyphen separator (-)", "Standard: M-BLD1-Z01-L02-HVAC-SUP-AHU-0001");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Use dot separator (.)", "Alternative: M.BLD1.Z01.L02.HVAC.SUP.AHU.0001");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Use underscore separator (_)", "Alternative: M_BLD1_Z01_L02_HVAC_SUP_AHU_0001");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Custom configuration", "Enter custom separator and padding");

            var result = dlg.Show();
            string newSep = currentSep;
            int newPad = currentPad;

            switch (result)
            {
                case TaskDialogResult.CommandLink1: newSep = "-"; break;
                case TaskDialogResult.CommandLink2: newSep = "."; break;
                case TaskDialogResult.CommandLink3: newSep = "_"; break;
                case TaskDialogResult.CommandLink4:
                    // Custom config — ask for padding
                    var padDlg = new TaskDialog("Sequence Padding");
                    padDlg.MainInstruction = "Select sequence number padding";
                    padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        "3 digits", "Sequences: 001, 002, ..., 999");
                    padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "4 digits (standard)", "Sequences: 0001, 0002, ..., 9999");
                    padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                        "5 digits", "Sequences: 00001, 00002, ..., 99999");
                    padDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                        "6 digits", "Sequences: 000001, 000002, ..., 999999");
                    var padResult = padDlg.Show();
                    switch (padResult)
                    {
                        case TaskDialogResult.CommandLink1: newPad = 3; break;
                        case TaskDialogResult.CommandLink2: newPad = 4; break;
                        case TaskDialogResult.CommandLink3: newPad = 5; break;
                        case TaskDialogResult.CommandLink4: newPad = 6; break;
                        default: return Result.Cancelled;
                    }
                    // Re-ask for separator
                    var sepDlg = new TaskDialog("Separator");
                    sepDlg.MainInstruction = "Select separator character";
                    sepDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Hyphen (-)", "M-BLD1-Z01");
                    sepDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Dot (.)", "M.BLD1.Z01");
                    sepDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Underscore (_)", "M_BLD1_Z01");
                    sepDlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Slash (/)", "M/BLD1/Z01");
                    var sepResult = sepDlg.Show();
                    switch (sepResult)
                    {
                        case TaskDialogResult.CommandLink1: newSep = "-"; break;
                        case TaskDialogResult.CommandLink2: newSep = "."; break;
                        case TaskDialogResult.CommandLink3: newSep = "_"; break;
                        case TaskDialogResult.CommandLink4: newSep = "/"; break;
                        default: return Result.Cancelled;
                    }
                    break;
                default: return Result.Cancelled;
            }

            // Apply changes
            ParamRegistry.OverrideTagFormat(newSep, newPad, ParamRegistry.SegmentOrder);

            // Persist to project_config.json
            string configPath = StingToolsApp.FindDataFile("project_config.json");
            if (string.IsNullOrEmpty(configPath))
            {
                string dataDir = StingToolsApp.DataPath;
                if (!string.IsNullOrEmpty(dataDir))
                    configPath = Path.Combine(dataDir, "project_config.json");
            }
            if (!string.IsNullOrEmpty(configPath))
                TagConfig.SaveToFile(configPath);

            // Show updated format
            string example = $"M{newSep}BLD1{newSep}Z01{newSep}L02{newSep}HVAC{newSep}SUP{newSep}AHU{newSep}{"1".PadLeft(newPad, '0')}";
            TaskDialog.Show("Tag Format Updated",
                $"Tag format updated:\n\n" +
                $"  Separator: '{newSep}'\n" +
                $"  Padding: {newPad} digits\n\n" +
                $"  Example: {example}\n\n" +
                (configPath != null ? $"Saved to: {configPath}" : "Config file not found — changes apply for this session only."));

            StingLog.Info($"Tag format configured: sep='{newSep}', pad={newPad}");
            return Result.Succeeded;
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  KEYNOTE TABLE AUTO-SYNC (P11)
    //  Auto-syncs keynote entries from a STING CSV file into Revit's keynote
    //  table. Eliminates manual KNO file maintenance.
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Auto-sync keynote table from STING tag data.
    /// Generates a Revit-compatible keynote text file from the current project's
    /// tag configurations (DISC, SYS, FUNC, PROD codes) and loads it.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class KeynoteSyncCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            string outputDir = !string.IsNullOrEmpty(doc.PathName)
                ? Path.GetDirectoryName(doc.PathName) ?? Path.GetTempPath()
                : Path.GetTempPath();

            string knoPath = Path.Combine(outputDir, "STING_KEYNOTES.txt");

            // Generate keynote file from STING tag configuration
            var sb = new StringBuilder();
            sb.AppendLine("# STING Keynote Table");
            sb.AppendLine("# Auto-generated from tag configuration");
            sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();

            // Discipline codes as top-level headings
            var discCodes = new Dictionary<string, string>
            {
                {"M", "Mechanical"}, {"E", "Electrical"}, {"P", "Plumbing"},
                {"A", "Architectural"}, {"S", "Structural"}, {"FP", "Fire Protection"},
                {"LV", "Low Voltage"}, {"G", "General"}
            };

            foreach (var disc in discCodes)
            {
                sb.AppendLine($"{disc.Key}\t\t{disc.Value}");
            }
            sb.AppendLine();

            // System codes under each discipline
            foreach (var sysEntry in TagConfig.SysMap)
            {
                string sysCode = sysEntry.Key;
                string funcCode = TagConfig.GetFuncCode(sysCode);
                sb.AppendLine($"{sysCode}\t\t{sysCode} System ({funcCode})");
            }
            sb.AppendLine();

            // Product codes from PROD map
            foreach (var prodEntry in TagConfig.ProdMap)
            {
                string catName = prodEntry.Key;
                string prodCode = prodEntry.Value;
                sb.AppendLine($"{prodCode}\t\t{catName}");
            }

            File.WriteAllText(knoPath, sb.ToString());

            // Load into Revit via KeynoteTable API
            int entries = discCodes.Count + TagConfig.SysMap.Count + TagConfig.ProdMap.Count;
            try
            {
                KeynoteTable kt = KeynoteTable.GetKeynoteTable(doc);
                using (Transaction tx = new Transaction(doc, "STING Keynote Sync"))
                {
                    tx.Start();
                    ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(knoPath);
                    kt.LoadFrom(mp, null);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Keynote table auto-load: {ex.Message} — file generated, load manually via Annotate > Keynoting Settings");
            }

            TaskDialog.Show("Keynote Sync",
                $"Keynote file generated: {Path.GetFileName(knoPath)}\n\n" +
                $"  Discipline codes: {discCodes.Count}\n" +
                $"  System codes: {TagConfig.SysMap.Count}\n" +
                $"  Product codes: {TagConfig.ProdMap.Count}\n" +
                $"  Total entries: {entries}\n\n" +
                $"File: {knoPath}");

            StingLog.Info($"Keynote sync: {entries} entries → {knoPath}");
            return Result.Succeeded;
        }
    }
}
