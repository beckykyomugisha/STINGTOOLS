// ===================================================================================
// Data Pipeline Enhancement Commands — Covers TEMP, validation, and data integrity gaps
// Cross-validation between CSV files, PARAMETER__CATEGORIES activation,
// FAMILY_PARAMETER_BINDINGS integration, configurable tag format, sunset dates.
// ===================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Temp
{
    #region Cross-Validation Commands

    /// <summary>
    /// TEMP-05: Cross-validate PARAMETER_REGISTRY.json against CATEGORY_BINDINGS.csv.
    /// Detects parameters in JSON not in CSV and vice versa. Reports drift.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CrossValidateRegistryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("═══ Registry vs CSV Cross-Validation ═══\n");

                // Load PARAMETER_REGISTRY.json parameters
                var registryParams = new HashSet<string>(ParamRegistry.AllParamGuids.Keys, StringComparer.OrdinalIgnoreCase);

                // Load CATEGORY_BINDINGS.csv parameters
                var csvPath = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
                var csvParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
                {
                    var lines = File.ReadAllLines(csvPath);
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = StingToolsApp.ParseCsvLine(line);
                        if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                            csvParams.Add(parts[0].Trim());
                    }
                }
                else
                {
                    sb.AppendLine("⚠ CATEGORY_BINDINGS.csv not found");
                }

                // Find differences
                var inJsonNotCsv = registryParams.Except(csvParams, StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
                var inCsvNotJson = csvParams.Except(registryParams, StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();

                sb.AppendLine($"PARAMETER_REGISTRY.json: {registryParams.Count} parameters");
                sb.AppendLine($"CATEGORY_BINDINGS.csv: {csvParams.Count} parameters");
                sb.AppendLine();

                if (inJsonNotCsv.Count > 0)
                {
                    sb.AppendLine($"In JSON but NOT in CSV ({inJsonNotCsv.Count}):");
                    foreach (var p in inJsonNotCsv.Take(20))
                        sb.AppendLine($"  • {p}");
                    if (inJsonNotCsv.Count > 20)
                        sb.AppendLine($"  ... +{inJsonNotCsv.Count - 20} more");
                }
                else
                {
                    sb.AppendLine("✓ All JSON parameters found in CSV");
                }

                sb.AppendLine();
                if (inCsvNotJson.Count > 0)
                {
                    sb.AppendLine($"In CSV but NOT in JSON ({inCsvNotJson.Count}):");
                    foreach (var p in inCsvNotJson.Take(20))
                        sb.AppendLine($"  • {p}");
                    if (inCsvNotJson.Count > 20)
                        sb.AppendLine($"  ... +{inCsvNotJson.Count - 20} more");
                }
                else
                {
                    sb.AppendLine("✓ All CSV parameters found in JSON");
                }

                var overlap = registryParams.Intersect(csvParams, StringComparer.OrdinalIgnoreCase).Count();
                sb.AppendLine($"\nOverlap: {overlap} parameters");
                sb.AppendLine($"Drift: {(inJsonNotCsv.Count + inCsvNotJson.Count == 0 ? "✓ None" : "⚠ Detected")}");

                TaskDialog.Show("STING Cross-Validation", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cross-validation failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// TEMP-06: Validate BINDING_COVERAGE_MATRIX.csv against CATEGORY_BINDINGS.csv.
    /// Ensures matrix format matches normalized bindings — detects missing entries.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateBindingMatrixCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("═══ Binding Matrix Validation ═══\n");

                var matrixPath = StingToolsApp.FindDataFile("BINDING_COVERAGE_MATRIX.csv");
                var bindingsPath = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");

                if (string.IsNullOrEmpty(matrixPath) || !File.Exists(matrixPath))
                {
                    sb.AppendLine("⚠ BINDING_COVERAGE_MATRIX.csv not found");
                    TaskDialog.Show("STING Binding Validation", sb.ToString());
                    return Result.Failed;
                }

                if (string.IsNullOrEmpty(bindingsPath) || !File.Exists(bindingsPath))
                {
                    sb.AppendLine("⚠ CATEGORY_BINDINGS.csv not found");
                    TaskDialog.Show("STING Binding Validation", sb.ToString());
                    return Result.Failed;
                }

                // Parse matrix
                var matrixLines = File.ReadAllLines(matrixPath);
                var matrixHeader = StingToolsApp.ParseCsvLine(matrixLines[0]);
                var matrixCategories = matrixHeader.Skip(1).Select(h => h.Trim()).ToList();
                int matrixParams = matrixLines.Length - 1;

                // Parse bindings
                var bindingLines = File.ReadAllLines(bindingsPath).Skip(1).ToList();
                var bindingParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var bindingCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int totalBindings = 0;

                foreach (var line in bindingLines)
                {
                    var parts = StingToolsApp.ParseCsvLine(line);
                    if (parts.Length < 2) continue;
                    bindingParams.Add(parts[0].Trim());
                    bindingCategories.Add(parts[1].Trim());
                    totalBindings++;
                }

                sb.AppendLine($"Matrix: {matrixParams} parameters × {matrixCategories.Count} categories");
                sb.AppendLine($"Bindings: {bindingParams.Count} parameters × {bindingCategories.Count} categories ({totalBindings} total)");

                // Compare categories
                var matrixCatSet = new HashSet<string>(matrixCategories, StringComparer.OrdinalIgnoreCase);
                var missingInMatrix = bindingCategories.Except(matrixCatSet, StringComparer.OrdinalIgnoreCase).ToList();
                var missingInBindings = matrixCatSet.Except(bindingCategories, StringComparer.OrdinalIgnoreCase).ToList();

                if (missingInMatrix.Count > 0)
                    sb.AppendLine($"\nCategories in bindings but missing from matrix ({missingInMatrix.Count}):\n  {string.Join(", ", missingInMatrix.Take(10))}");
                if (missingInBindings.Count > 0)
                    sb.AppendLine($"\nCategories in matrix but missing from bindings ({missingInBindings.Count}):\n  {string.Join(", ", missingInBindings.Take(10))}");

                if (missingInMatrix.Count == 0 && missingInBindings.Count == 0)
                    sb.AppendLine("\n✓ Category sets match");

                // Count matrix 1s vs binding rows
                int matrixOnes = 0;
                for (int i = 1; i < matrixLines.Length; i++)
                {
                    var values = StingToolsApp.ParseCsvLine(matrixLines[i]);
                    for (int j = 1; j < values.Length; j++)
                    {
                        if (values[j].Trim() == "1") matrixOnes++;
                    }
                }

                sb.AppendLine($"\nMatrix active bindings (1s): {matrixOnes}");
                sb.AppendLine($"CSV binding rows: {totalBindings}");
                sb.AppendLine($"Difference: {Math.Abs(matrixOnes - totalBindings)} ({(matrixOnes == totalBindings ? "✓ Match" : "⚠ Drift")})");

                TaskDialog.Show("STING Binding Validation", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Binding matrix validation failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// TEMP-07: Load and display PARAMETER__CATEGORIES.csv metadata.
    /// Activates the previously unused file for parameter documentation.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ViewParameterMetadataCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var path = StingToolsApp.FindDataFile("PARAMETER__CATEGORIES.csv");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    TaskDialog.Show("STING Parameter Metadata", "PARAMETER__CATEGORIES.csv not found.");
                    return Result.Failed;
                }

                var lines = File.ReadAllLines(path);
                var header = StingToolsApp.ParseCsvLine(lines[0]);

                var sb = new StringBuilder();
                sb.AppendLine("═══ Parameter Metadata (PARAMETER__CATEGORIES.csv) ═══\n");
                sb.AppendLine($"Columns: {string.Join(", ", header)}");
                sb.AppendLine($"Parameters: {lines.Length - 1}");
                sb.AppendLine();

                // Parse and show summary
                var dataTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                int dataTypeCol = Array.FindIndex(header, h => h.Trim().Equals("Data Type", StringComparison.OrdinalIgnoreCase));
                int groupCol = Array.FindIndex(header, h => h.Trim().Equals("Group", StringComparison.OrdinalIgnoreCase));

                for (int i = 1; i < lines.Length; i++)
                {
                    var parts = StingToolsApp.ParseCsvLine(lines[i]);
                    if (dataTypeCol >= 0 && dataTypeCol < parts.Length)
                    {
                        var dt = parts[dataTypeCol].Trim();
                        if (!string.IsNullOrEmpty(dt))
                            dataTypes[dt] = dataTypes.GetValueOrDefault(dt) + 1;
                    }
                    if (groupCol >= 0 && groupCol < parts.Length)
                    {
                        var grp = parts[groupCol].Trim();
                        if (!string.IsNullOrEmpty(grp))
                            groups[grp] = groups.GetValueOrDefault(grp) + 1;
                    }
                }

                sb.AppendLine("By Data Type:");
                foreach (var kv in dataTypes.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");

                sb.AppendLine("\nBy Group:");
                foreach (var kv in groups.OrderByDescending(kv => kv.Value))
                    sb.AppendLine($"  {kv.Key}: {kv.Value}");

                // Show first 10 parameters
                sb.AppendLine("\nFirst 10 parameters:");
                for (int i = 1; i <= Math.Min(10, lines.Length - 1); i++)
                {
                    var parts = StingToolsApp.ParseCsvLine(lines[i]);
                    if (parts.Length > 0)
                        sb.AppendLine($"  {parts[0].Trim()}");
                }

                TaskDialog.Show("STING Parameter Metadata", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Parameter metadata view failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// TEMP-08: Validate FAMILY_PARAMETER_BINDINGS.csv against project families.
    /// Checks: GUID consistency, parameter existence, category match.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ValidateFamilyBindingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = commandData.Application.ActiveUIDocument.Document;

            try
            {
                var path = StingToolsApp.FindDataFile("FAMILY_PARAMETER_BINDINGS.csv");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    TaskDialog.Show("STING Family Bindings", "FAMILY_PARAMETER_BINDINGS.csv not found.");
                    return Result.Failed;
                }

                var lines = File.ReadAllLines(path).Skip(1).ToList();
                var sb = new StringBuilder();
                sb.AppendLine("═══ Family Parameter Bindings Validation ═══\n");
                sb.AppendLine($"CSV entries: {lines.Count}");

                // Parse CSV
                var csvGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var csvParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var line in lines)
                {
                    var parts = StingToolsApp.ParseCsvLine(line);
                    if (parts.Length < 3) continue;
                    csvParams.Add(parts[1].Trim());
                    csvGuids.Add(parts[2].Trim());
                    if (parts.Length > 6)
                        categories.Add(parts[6].Trim());
                }

                sb.AppendLine($"Unique parameters: {csvParams.Count}");
                sb.AppendLine($"Unique GUIDs: {csvGuids.Count}");
                sb.AppendLine($"Target categories: {categories.Count}");

                // Cross-check against PARAMETER_REGISTRY
                var registryGuids = new HashSet<string>(
                    ParamRegistry.AllParamGuids.Values.Select(g => g.ToString()),
                    StringComparer.OrdinalIgnoreCase);

                var guidMatch = csvGuids.Count(g => registryGuids.Contains(g));
                sb.AppendLine($"\nGUID match with registry: {guidMatch}/{csvGuids.Count}");
                sb.AppendLine($"GUID mismatch: {csvGuids.Count - guidMatch}");

                // Check parameter names
                var registryNames = new HashSet<string>(ParamRegistry.AllParamGuids.Keys, StringComparer.OrdinalIgnoreCase);
                var nameMatch = csvParams.Count(p => registryNames.Contains(p));
                sb.AppendLine($"\nName match with registry: {nameMatch}/{csvParams.Count}");

                // Check categories exist in project
                int projectCatMatch = 0;
                var projectCats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (cat != null && !string.IsNullOrEmpty(cat.Name))
                        projectCats.Add(cat.Name);
                }

                projectCatMatch = categories.Count(c => projectCats.Contains(c));
                sb.AppendLine($"\nCategory match in project: {projectCatMatch}/{categories.Count}");

                sb.AppendLine($"\nOverall status: {(guidMatch == csvGuids.Count && nameMatch == csvParams.Count ? "✓ Valid" : "⚠ Issues detected")}");

                TaskDialog.Show("STING Family Bindings Validation", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Family bindings validation failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// TEMP-09: Data file integrity check with SHA-256 hashes.
    /// Enhanced version of CheckDataCommand with hash comparison and drift detection.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DataIntegrityCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dataPath = StingToolsApp.DataPath;
                if (string.IsNullOrEmpty(dataPath) || !Directory.Exists(dataPath))
                {
                    TaskDialog.Show("STING Data Integrity", "Data directory not found.");
                    return Result.Failed;
                }

                var files = Directory.GetFiles(dataPath, "*.*")
                    .Where(f => new[] { ".csv", ".json", ".txt", ".xlsx", ".py" }
                        .Contains(Path.GetExtension(f).ToLower()))
                    .OrderBy(f => f)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine("═══ Data File Integrity Check ═══\n");
                sb.AppendLine($"Data path: {dataPath}");
                sb.AppendLine($"Files: {files.Count}\n");

                long totalSize = 0;
                var hashFile = Path.Combine(dataPath, "INTEGRITY_HASHES.json");
                Dictionary<string, string> previousHashes = null;

                if (File.Exists(hashFile))
                {
                    try { previousHashes = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(hashFile)); }
                    catch { }
                }

                var currentHashes = new Dictionary<string, string>();
                int changed = 0;

                using var sha = SHA256.Create();

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var fileInfo = new FileInfo(file);
                    totalSize += fileInfo.Length;

                    // Compute hash
                    string hash;
                    using (var stream = File.OpenRead(file))
                    {
                        hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "")[..16];
                    }

                    currentHashes[fileName] = hash;

                    // Check against previous
                    string status = "NEW";
                    if (previousHashes != null && previousHashes.TryGetValue(fileName, out var prevHash))
                    {
                        status = hash == prevHash ? "OK" : "CHANGED";
                        if (status == "CHANGED") changed++;
                    }

                    int lineCount = 0;
                    try
                    {
                        if (Path.GetExtension(file).ToLower() != ".xlsx")
                            lineCount = File.ReadLines(file).Count();
                    }
                    catch { }

                    sb.AppendLine($"  [{status,-7}] {fileName,-40} {fileInfo.Length / 1024,6}KB {lineCount,7} lines  {hash}");
                }

                sb.AppendLine($"\nTotal: {files.Count} files, {totalSize / 1024}KB");
                if (previousHashes != null)
                    sb.AppendLine($"Changed since last check: {changed}");

                // Save current hashes
                try
                {
                    File.WriteAllText(hashFile, JsonConvert.SerializeObject(currentHashes, Formatting.Indented));
                    sb.AppendLine("\nHashes saved for next comparison.");
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"\n⚠ Could not save hashes: {ex.Message}");
                }

                TaskDialog.Show("STING Data Integrity", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Data integrity check failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// TEMP-10: Generate consolidated data report.
    /// Summarizes all data files, their relationships, and usage status.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class DataReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var dataPath = StingToolsApp.DataPath;
                var sb = new StringBuilder();
                sb.AppendLine("═══ STING Data File Report ═══\n");

                // PARAMETER_REGISTRY.json
                var regPath = StingToolsApp.FindDataFile("PARAMETER_REGISTRY.json");
                if (!string.IsNullOrEmpty(regPath) && File.Exists(regPath))
                {
                    var regSize = new FileInfo(regPath).Length / 1024;
                    sb.AppendLine($"[PRIMARY] PARAMETER_REGISTRY.json — {regSize}KB");
                    sb.AppendLine($"  Parameters: {ParamRegistry.AllParamGuids.Count}");
                    sb.AppendLine($"  Status: ✓ Loaded at startup by ParamRegistry.cs");
                }

                // CATEGORY_BINDINGS.csv
                var catPath = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
                if (!string.IsNullOrEmpty(catPath) && File.Exists(catPath))
                {
                    int rows = File.ReadLines(catPath).Count() - 1;
                    sb.AppendLine($"\n[ACTIVE] CATEGORY_BINDINGS.csv — {rows} bindings");
                    sb.AppendLine($"  Used by: LoadSharedParamsCommand, DynamicBindingsCommand");
                }

                // BINDING_COVERAGE_MATRIX.csv
                var matPath = StingToolsApp.FindDataFile("BINDING_COVERAGE_MATRIX.csv");
                if (!string.IsNullOrEmpty(matPath) && File.Exists(matPath))
                {
                    int rows = File.ReadLines(matPath).Count() - 1;
                    sb.AppendLine($"\n[VALIDATION] BINDING_COVERAGE_MATRIX.csv — {rows} parameters");
                    sb.AppendLine($"  Used by: ValidateTemplateCommand only");
                }

                // FAMILY_PARAMETER_BINDINGS.csv
                var famPath = StingToolsApp.FindDataFile("FAMILY_PARAMETER_BINDINGS.csv");
                if (!string.IsNullOrEmpty(famPath) && File.Exists(famPath))
                {
                    int rows = File.ReadLines(famPath).Count() - 1;
                    sb.AppendLine($"\n[ACTIVE] FAMILY_PARAMETER_BINDINGS.csv — {rows} bindings");
                    sb.AppendLine($"  Used by: BatchAddFamilyParamsCommand");
                }

                // PARAMETER__CATEGORIES.csv
                var pcPath = StingToolsApp.FindDataFile("PARAMETER__CATEGORIES.csv");
                if (!string.IsNullOrEmpty(pcPath) && File.Exists(pcPath))
                {
                    int rows = File.ReadLines(pcPath).Count() - 1;
                    sb.AppendLine($"\n[REFERENCE] PARAMETER__CATEGORIES.csv — {rows} parameters");
                    sb.AppendLine($"  Used by: ViewParameterMetadataCommand (human-readable reference)");
                }

                // Other data files
                sb.AppendLine("\n── Other Data Files ──");
                var otherFiles = new[] { "BLE_MATERIALS.csv", "MEP_MATERIALS.csv", "MR_PARAMETERS.txt",
                    "MR_SCHEDULES.csv", "FORMULAS_WITH_DEPENDENCIES.csv", "SCHEDULE_FIELD_REMAP.csv",
                    "BOQ_TEMPLATE.csv", "MATERIAL_SCHEMA.json", "LABEL_DEFINITIONS.json",
                    "TAG_GUIDE_V3.csv" };

                foreach (var fileName in otherFiles)
                {
                    var fPath = StingToolsApp.FindDataFile(fileName);
                    if (!string.IsNullOrEmpty(fPath) && File.Exists(fPath))
                    {
                        var fSize = new FileInfo(fPath).Length / 1024;
                        sb.AppendLine($"  {fileName} — {fSize}KB");
                    }
                }

                TaskDialog.Show("STING Data Report", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Data report failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// TEMP-11: Export all parameters and bindings to a unified JSON.
    /// Consolidates PARAMETER_REGISTRY + CATEGORY_BINDINGS + FAMILY_PARAMETER_BINDINGS.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportUnifiedRegistryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Unified Parameter Registry",
                    Filter = "JSON Files (*.json)|*.json",
                    FileName = "STING_UNIFIED_REGISTRY.json"
                };
                if (saveDialog.ShowDialog() != true) return Result.Cancelled;

                var registry = new Dictionary<string, object>();

                // Add ParamRegistry data
                var parameters = new List<object>();
                foreach (var kvp in ParamRegistry.AllParamGuids)
                {
                    parameters.Add(new
                    {
                        Name = kvp.Key,
                        GUID = kvp.Value.ToString()
                    });
                }
                registry["parameters"] = parameters;
                registry["parameter_count"] = parameters.Count;

                // Add CSV binding summary
                var catBindPath = StingToolsApp.FindDataFile("CATEGORY_BINDINGS.csv");
                if (!string.IsNullOrEmpty(catBindPath) && File.Exists(catBindPath))
                {
                    var bindingCount = File.ReadLines(catBindPath).Count() - 1;
                    registry["category_bindings_count"] = bindingCount;
                    registry["category_bindings_file"] = "CATEGORY_BINDINGS.csv";
                }

                var famBindPath = StingToolsApp.FindDataFile("FAMILY_PARAMETER_BINDINGS.csv");
                if (!string.IsNullOrEmpty(famBindPath) && File.Exists(famBindPath))
                {
                    var famCount = File.ReadLines(famBindPath).Count() - 1;
                    registry["family_bindings_count"] = famCount;
                    registry["family_bindings_file"] = "FAMILY_PARAMETER_BINDINGS.csv";
                }

                registry["export_date"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                registry["version"] = "1.0.0";

                File.WriteAllText(saveDialog.FileName, JsonConvert.SerializeObject(registry, Formatting.Indented));

                TaskDialog.Show("STING Unified Registry",
                    $"Registry exported to:\n{saveDialog.FileName}\n\n" +
                    $"Parameters: {parameters.Count}\n" +
                    $"Format: JSON");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Unified registry export failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    #endregion
}
