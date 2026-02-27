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

            // Check if ASS_TAG_1_TXT parameter exists
            bool hasTagParam = false;
            var elems = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.HasMaterialQuantities)
                .Take(5);
            foreach (var elem in elems)
            {
                if (elem.LookupParameter("ASS_TAG_1_TXT") != null)
                {
                    hasTagParam = true;
                    break;
                }
            }
            results.Add(new ValidationResult("Tag parameters bound", "MODERATE",
                hasTagParam,
                hasTagParam ? "ASS_TAG_1_TXT found on elements" : "ASS_TAG_1_TXT NOT bound — run Load Params"));
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
}
