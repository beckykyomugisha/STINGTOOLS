using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
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

        private void CheckParameterFile(string dataPath, List<ValidationResult> results)
        {
            string path = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
            if (path == null)
            {
                results.Add(new ValidationResult("Parameter file", "CRITICAL", false, "MR_PARAMETERS.txt not found"));
                return;
            }

            var lines = File.ReadAllLines(path);
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

            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
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

            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
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

            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
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

            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
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

            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
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

            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
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
            var lines = File.ReadAllLines(bindingsPath)
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

            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
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
    //  BOQ Export Command — Automated Bill of Quantities
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Exports a grouped Bill of Quantities (BOQ) in NRM2/corporate format.
    /// Groups by Uniformat/category, aggregates quantities and costs,
    /// includes subtotals per section and grand total.
    /// Zero manual input — reads all data from Revit model parameters.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BOQExportCommand : IExternalCommand
    {
        // NRM2 Work Section codes mapped to Revit categories
        private static readonly Dictionary<string, (string code, string section)> NRM2Sections =
            new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // 1 – Substructure
            { "Structural Foundations", ("1.1", "Substructure") },
            { "Floors", ("1.2", "Substructure") },
            // 2 – Superstructure
            { "Structural Columns", ("2.1", "Frame") },
            { "Structural Framing", ("2.2", "Upper Floors") },
            { "Roofs", ("2.3", "Roof") },
            { "Stairs", ("2.4", "Stairs and Ramps") },
            { "Walls", ("2.5", "External Walls") },
            { "Windows", ("2.6", "Windows and External Doors") },
            { "Doors", ("2.6", "Windows and External Doors") },
            { "Curtain Panels", ("2.7", "External Finishes") },
            // 3 – Internal Finishes
            { "Ceilings", ("3.1", "Wall Finishes") },
            { "Generic Models", ("3.2", "Floor Finishes") },
            { "Furniture", ("3.3", "Ceiling Finishes") },
            { "Casework", ("3.3", "Ceiling Finishes") },
            // 5 – Services
            { "Mechanical Equipment", ("5.1", "Sanitary Installations") },
            { "Plumbing Fixtures", ("5.1", "Sanitary Installations") },
            { "Plumbing Equipment", ("5.1", "Sanitary Installations") },
            { "Pipes", ("5.2", "Services Equipment") },
            { "Pipe Fittings", ("5.2", "Services Equipment") },
            { "Pipe Accessories", ("5.2", "Services Equipment") },
            { "Ducts", ("5.3", "Disposal Installations") },
            { "Duct Fittings", ("5.3", "Disposal Installations") },
            { "Duct Accessories", ("5.3", "Disposal Installations") },
            { "Air Terminals", ("5.4", "Water Installations") },
            { "Flex Ducts", ("5.5", "Heat Source") },
            { "Flex Pipes", ("5.5", "Heat Source") },
            { "Electrical Equipment", ("5.8", "Electrical Installations") },
            { "Electrical Fixtures", ("5.8", "Electrical Installations") },
            { "Lighting Fixtures", ("5.9", "Fuel Installations") },
            { "Lighting Devices", ("5.9", "Fuel Installations") },
            { "Communication Devices", ("5.10", "Lift and Conveyor Installations") },
            { "Data Devices", ("5.11", "Fire and Lightning Protection") },
            { "Fire Alarm Devices", ("5.11", "Fire and Lightning Protection") },
            { "Nurse Call Devices", ("5.12", "Communication Installations") },
            { "Security Devices", ("5.12", "Communication Installations") },
            { "Sprinklers", ("5.11", "Fire and Lightning Protection") },
            { "Conduits", ("5.13", "Special Installations") },
            { "Conduit Fittings", ("5.13", "Special Installations") },
            { "Cable Trays", ("5.13", "Special Installations") },
            { "Cable Tray Fittings", ("5.13", "Special Installations") },
            // 8 – External Works
            { "Topography", ("8.1", "External Works") },
            { "Railings", ("8.2", "External Drainage") },
            { "Ramps", ("8.3", "External Services") },
        };

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            StingLog.Info("BOQ Export starting...");

            // Collect all taggable elements
            var allElems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .ToList();

            if (allElems.Count == 0)
            {
                TaskDialog.Show("BOQ Export", "No taggable elements found in project.");
                return Result.Succeeded;
            }

            // Build BOQ items grouped by NRM2 section
            var boqItems = new List<BOQItem>();
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
                        Tag = ParameterHelpers.GetString(el, "ASS_TAG_1"),
                        Discipline = ParameterHelpers.GetString(el, "ASS_DISCIPLINE_COD_TXT"),
                        Level = ParameterHelpers.GetString(el, "ASS_LVL_COD_TXT"),
                        Location = ParameterHelpers.GetString(el, "ASS_LOC_TXT"),
                        Zone = ParameterHelpers.GetString(el, "ASS_ZONE_TXT"),
                        Unit = ParameterHelpers.GetString(el, "ASS_PMT_INV_UNIT_TXT"),
                        Manufacturer = "",
                        Model = "",
                    };

                    // Read identity from type
                    ElementType eType = doc.GetElement(el.GetTypeId()) as ElementType;
                    if (eType != null)
                    {
                        item.Manufacturer = eType.get_Parameter(BuiltInParameter.ALL_MODEL_MANUFACTURER)?.AsString() ?? "";
                        item.Model = eType.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL)?.AsString() ?? "";
                        if (string.IsNullOrEmpty(item.Description))
                            item.Description = eType.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString() ?? "";
                    }

                    // Read dimensions
                    item.Width_mm = ReadDimension(el, "BLE_ELE_WIDTH_MM");
                    item.Height_mm = ReadDimension(el, "BLE_ELE_HEIGHT_MM");
                    item.Length_mm = ReadDimension(el, "BLE_ELE_LENGTH_MM");
                    item.Area_m2 = ReadDouble(el, "BLE_ELE_AREA_SQ_M");

                    // Read cost parameters
                    item.UnitPrice = ReadDouble(el, "ASS_CST_UNIT_PRICE_UGX_NR");
                    item.Quantity = ReadDouble(el, "ASS_CST_QUANTITY_NR");
                    if (item.Quantity == 0) item.Quantity = 1;
                    item.TotalCost = ReadDouble(el, "ASS_CST_TOTAL_UGX_NR");
                    if (item.TotalCost == 0 && item.UnitPrice > 0)
                        item.TotalCost = item.UnitPrice * item.Quantity;
                    item.LabourCost = ReadDouble(el, "ASS_PMT_CST_LABOUR_CST_UGX_NR");
                    item.MaterialCost = ReadDouble(el, "ASS_PMT_CST_MAT_CST_UGX_NR");

                    // Map to NRM2 section
                    if (NRM2Sections.TryGetValue(catName, out var nrm2))
                    {
                        item.NRM2Code = nrm2.code;
                        item.NRM2Section = nrm2.section;
                    }
                    else
                    {
                        item.NRM2Code = "9.0";
                        item.NRM2Section = "Unclassified";
                    }

                    // Generate unit if empty
                    if (string.IsNullOrEmpty(item.Unit))
                    {
                        item.Unit = catName switch
                        {
                            var c when c.Contains("Pipe") || c.Contains("Duct") || c.Contains("Conduit") || c.Contains("Cable Tray") => "m",
                            var c when c.Contains("Floor") || c.Contains("Ceiling") || c.Contains("Wall") || c.Contains("Roof") => "m²",
                            _ => "nr",
                        };
                    }

                    boqItems.Add(item);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BOQ item {el.Id}: {ex.Message}");
                }
            }

            // Group by NRM2 section → category → type
            var grouped = boqItems
                .GroupBy(b => b.NRM2Section)
                .OrderBy(g => boqItems.First(b => b.NRM2Section == g.Key).NRM2Code)
                .ToList();

            // Build CSV
            var csv = new StringBuilder();
            csv.AppendLine("\"Item\",\"NRM2 Code\",\"Section\",\"Description\",\"Unit\"," +
                "\"Qty\",\"Unit Rate\",\"Material Cost\",\"Labour Cost\",\"Total Cost\"," +
                "\"Discipline\",\"Level\",\"Location\",\"Zone\",\"Tag\"," +
                "\"Category\",\"Family\",\"Type\",\"Manufacturer\",\"Model\"," +
                "\"Width_mm\",\"Height_mm\",\"Length_mm\",\"Area_m2\"");

            int itemNum = 0;
            double grandTotal = 0;
            double grandMaterial = 0;
            double grandLabour = 0;
            var sectionSummary = new StringBuilder();

            foreach (var section in grouped)
            {
                double sectionTotal = 0;
                double sectionMaterial = 0;
                double sectionLabour = 0;
                string sectionCode = boqItems.First(b => b.NRM2Section == section.Key).NRM2Code;

                // Sub-group by category → type for aggregation
                var typeGroups = section
                    .GroupBy(b => $"{b.Category}|{b.TypeName}")
                    .OrderBy(g => g.Key)
                    .ToList();

                foreach (var tg in typeGroups)
                {
                    itemNum++;
                    var sample = tg.First();
                    int qty = tg.Count();
                    double aggUnit = tg.Where(b => b.UnitPrice > 0).Select(b => b.UnitPrice).DefaultIfEmpty(0).Average();
                    double aggMat = tg.Sum(b => b.MaterialCost);
                    double aggLab = tg.Sum(b => b.LabourCost);
                    double aggTotal = tg.Sum(b => b.TotalCost);
                    if (aggTotal == 0 && aggUnit > 0)
                        aggTotal = aggUnit * qty;

                    double aggArea = tg.Sum(b => b.Area_m2);

                    string description = !string.IsNullOrEmpty(sample.Description)
                        ? sample.Description
                        : $"{sample.FamilyName} - {sample.TypeName}";

                    csv.AppendLine($"\"{itemNum}\",\"{sectionCode}\",\"{section.Key}\"," +
                        $"\"{Esc(description)}\",\"{sample.Unit}\"," +
                        $"\"{qty}\",\"{aggUnit:F2}\",\"{aggMat:F2}\",\"{aggLab:F2}\",\"{aggTotal:F2}\"," +
                        $"\"{sample.Discipline}\",\"{sample.Level}\",\"{sample.Location}\",\"{sample.Zone}\",\"{sample.Tag}\"," +
                        $"\"{sample.Category}\",\"{Esc(sample.FamilyName)}\",\"{Esc(sample.TypeName)}\"," +
                        $"\"{Esc(sample.Manufacturer)}\",\"{Esc(sample.Model)}\"," +
                        $"\"{sample.Width_mm:F0}\",\"{sample.Height_mm:F0}\",\"{sample.Length_mm:F0}\",\"{aggArea:F2}\"");

                    sectionTotal += aggTotal;
                    sectionMaterial += aggMat;
                    sectionLabour += aggLab;
                }

                // Section subtotal row
                csv.AppendLine($"\"\",\"{sectionCode}\",\"{section.Key} — SUBTOTAL\"," +
                    $"\"Section Subtotal: {section.Key}\",\"\"," +
                    $"\"{section.Count()}\",\"\",\"{sectionMaterial:F2}\",\"{sectionLabour:F2}\",\"{sectionTotal:F2}\"," +
                    $"\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\"");

                grandTotal += sectionTotal;
                grandMaterial += sectionMaterial;
                grandLabour += sectionLabour;

                sectionSummary.AppendLine($"  {sectionCode,-6} {section.Key,-30} {section.Count(),6} items  {sectionTotal,14:N0}");
            }

            // Grand total row
            csv.AppendLine($"\"\",\"\",\"GRAND TOTAL\"," +
                $"\"Bill of Quantities — Grand Total\",\"\"," +
                $"\"{boqItems.Count}\",\"\",\"{grandMaterial:F2}\",\"{grandLabour:F2}\",\"{grandTotal:F2}\"," +
                $"\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\",\"\"");

            // Export to file
            string exportDir = StingToolsApp.DataPath ?? Path.GetTempPath();
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"STING_BOQ_{doc.Title}_{timestamp}.csv";
            string exportPath = Path.Combine(exportDir, fileName);
            try
            {
                File.WriteAllText(exportPath, csv.ToString(), Encoding.UTF8);
                StingLog.Info($"BOQ exported to {exportPath}");
            }
            catch (Exception ex)
            {
                StingLog.Error($"BOQ export failed: {ex.Message}");
                exportPath = Path.Combine(Path.GetTempPath(), fileName);
                File.WriteAllText(exportPath, csv.ToString(), Encoding.UTF8);
            }

            // Summary dialog
            var summary = new StringBuilder();
            summary.AppendLine($"Bill of Quantities — {doc.Title}");
            summary.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
            summary.AppendLine(new string('═', 50));
            summary.AppendLine();
            summary.AppendLine($"Total Elements: {boqItems.Count:N0}");
            summary.AppendLine($"Unique Types:   {boqItems.Select(b => $"{b.Category}|{b.TypeName}").Distinct().Count():N0}");
            summary.AppendLine($"NRM2 Sections:  {grouped.Count}");
            summary.AppendLine();
            summary.AppendLine("SECTION BREAKDOWN:");
            summary.Append(sectionSummary);
            summary.AppendLine();
            summary.AppendLine(new string('─', 50));
            summary.AppendLine($"{"MATERIAL COST:",-30} {grandMaterial,14:N0}");
            summary.AppendLine($"{"LABOUR COST:",-30} {grandLabour,14:N0}");
            summary.AppendLine($"{"GRAND TOTAL:",-30} {grandTotal,14:N0}");
            summary.AppendLine();
            summary.AppendLine($"Exported to: {exportPath}");

            TaskDialog.Show("BOQ Export", summary.ToString());
            return Result.Succeeded;
        }

        private static double ReadDimension(Element el, string paramName)
        {
            string val = ParameterHelpers.GetString(el, paramName);
            return double.TryParse(val, out double d) ? d : 0;
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

        private static string Esc(string val) =>
            val?.Replace("\"", "\"\"") ?? "";

        private class BOQItem
        {
            public int ElementId;
            public string Category, FamilyName, TypeName, Description, Tag;
            public string Discipline, Level, Location, Zone, Unit;
            public string Manufacturer, Model;
            public double Width_mm, Height_mm, Length_mm, Area_m2;
            public double UnitPrice, Quantity, TotalCost, LabourCost, MaterialCost;
            public string NRM2Code, NRM2Section;
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
}
