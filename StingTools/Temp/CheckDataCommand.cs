using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Ported from STINGTemp 1_Setup.panel — Check Data Files.
    /// Verifies data files and shows file inventory with SHA-256 hashes.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CheckDataCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            string dataDir = StingToolsApp.DataPath;
            if (string.IsNullOrEmpty(dataDir) || !Directory.Exists(dataDir))
            {
                TaskDialog.Show("Check Data",
                    "Data directory not found.\n\n" +
                    $"Expected: {dataDir ?? "(not set)"}\n\n" +
                    "Place data files (CSV, XLSX) in a 'data' folder " +
                    "alongside StingTools.dll.");
                return Result.Succeeded;
            }

            var report = new StringBuilder();
            report.AppendLine("Data File Inventory");
            report.AppendLine(new string('─', 60));

            string[] extensions = { "*.csv", "*.xlsx", "*.json", "*.txt" };
            int fileCount = 0;
            long totalSize = 0;

            foreach (string ext in extensions)
            {
                foreach (string file in Directory.GetFiles(dataDir, ext, SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(file);
                    fileCount++;
                    totalSize += fi.Length;

                    string hash = ComputeShortHash(file);
                    string relPath = file.Substring(dataDir.Length).TrimStart('\\', '/');
                    double sizeKb = fi.Length / 1024.0;

                    report.AppendLine(
                        $"  {relPath,-35} {sizeKb,8:F1} KB  {hash}");
                }
            }

            report.AppendLine(new string('─', 60));
            report.AppendLine(
                $"  {fileCount} files | {totalSize / (1024.0 * 1024.0):F1} MB total");

            // Validate category bindings against CATEGORY_BINDINGS.csv
            report.AppendLine();
            int bindingResult = SharedParamGuids.ValidateBindingsFromCsv();
            if (bindingResult == 0)
                report.AppendLine("  Binding validation: PASS (code matches CSV)");
            else if (bindingResult > 0)
                report.AppendLine($"  Binding validation: {bindingResult} discrepancy(ies) — see log");
            else
                report.AppendLine("  Binding validation: skipped (CSV not found)");

            // DAT-002: Data file version drift check
            report.AppendLine();
            report.AppendLine("── Version Drift Check ──");
            string registryVersion = GetRegistryVersion(dataDir);
            if (!string.IsNullOrEmpty(registryVersion))
            {
                report.AppendLine($"  PARAMETER_REGISTRY.json: v{registryVersion}");
                int driftCount = 0;
                string[] csvFiles = { "BLE_MATERIALS.csv", "MEP_MATERIALS.csv",
                    "MR_PARAMETERS.csv", "MR_SCHEDULES.csv",
                    "FORMULAS_WITH_DEPENDENCIES.csv", "SCHEDULE_FIELD_REMAP.csv",
                    "BINDING_COVERAGE_MATRIX.csv", "CATEGORY_BINDINGS.csv",
                    "FAMILY_PARAMETER_BINDINGS.csv", "PARAMETER__CATEGORIES.csv" };
                foreach (string csvName in csvFiles)
                {
                    string csvFilePath = Path.Combine(dataDir, csvName);
                    if (!File.Exists(csvFilePath)) continue;
                    string csvVer = GetCsvVersion(csvFilePath);
                    if (!string.IsNullOrEmpty(csvVer))
                    {
                        bool match = csvVer == registryVersion;
                        if (!match)
                        {
                            report.AppendLine($"  {csvName,-40} v{csvVer}  ← DRIFT (registry v{registryVersion})");
                            StingLog.Warn($"Version drift: {csvName} v{csvVer} vs PARAMETER_REGISTRY v{registryVersion}");
                            driftCount++;
                        }
                    }
                }
                if (driftCount == 0)
                    report.AppendLine("  All CSV versions consistent (no drift detected)");
                else
                    report.AppendLine($"  {driftCount} file(s) have version drift — consider updating");
            }
            else
            {
                report.AppendLine("  PARAMETER_REGISTRY.json: version not found (skipping drift check)");
            }

            // GAP-012: SHA-256 checksum verification against stored checksums
            report.AppendLine();
            report.AppendLine("── Integrity Check ──");
            string checksumPath = Path.Combine(dataDir, "checksums.json");
            int integrityIssues = VerifyChecksums(dataDir, checksumPath, report);

            TaskDialog td = new TaskDialog("Check Data");
            td.MainInstruction = $"{fileCount} data files found" +
                (integrityIssues > 0 ? $" ({integrityIssues} integrity warning(s))" : "");
            td.MainContent = report.ToString();
            td.Show();

            return Result.Succeeded;
        }

        private static string ComputeShortHash(string filePath)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    return BitConverter.ToString(hash, 0, 4)
                        .Replace("-", "")
                        .ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Hash compute failed for {filePath}: {ex.Message}");
                return "????????";
            }
        }

        /// <summary>Extract version from PARAMETER_REGISTRY.json "version" field.</summary>
        private static string GetRegistryVersion(string dataDir)
        {
            try
            {
                string path = Path.Combine(dataDir, "PARAMETER_REGISTRY.json");
                if (!File.Exists(path)) return null;
                // Read first few lines to find "version" without parsing full JSON
                foreach (string line in File.ReadLines(path).Take(10))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("\"version\""))
                    {
                        int colon = trimmed.IndexOf(':');
                        if (colon < 0) continue;
                        string val = trimmed.Substring(colon + 1).Trim().Trim(',').Trim('"');
                        return val;
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Failed to read registry version: {ex.Message}");
            }
            return null;
        }

        /// <summary>Extract version from CSV header comment (format: # vX.Y | date | desc).</summary>
        private static string GetCsvVersion(string csvPath)
        {
            try
            {
                using (var reader = new StreamReader(csvPath))
                {
                    string firstLine = reader.ReadLine();
                    if (firstLine != null && firstLine.StartsWith("# v"))
                    {
                        // Parse "# v2.3 | 20260227 | ..."
                        string afterHash = firstLine.Substring(2).Trim(); // "v2.3 | ..."
                        int pipe = afterHash.IndexOf('|');
                        string verPart = (pipe > 0 ? afterHash.Substring(0, pipe) : afterHash).Trim();
                        if (verPart.StartsWith("v"))
                            return verPart.Substring(1); // strip leading 'v'
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Failed to read CSV version from {csvPath}: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// GAP-012: Verify data file checksums. On first run, generates and saves checksums.json.
        /// On subsequent runs, compares current file hashes to stored ones.
        /// </summary>
        private static int VerifyChecksums(string dataDir, string checksumPath, StringBuilder report)
        {
            int issues = 0;
            try
            {
                // Compute current checksums for all data files
                var currentChecksums = new Dictionary<string, string>();
                string[] exts = { "*.csv", "*.json", "*.txt" };
                foreach (string ext in exts)
                {
                    foreach (string file in Directory.GetFiles(dataDir, ext))
                    {
                        if (file == checksumPath) continue; // skip self
                        string relName = Path.GetFileName(file);
                        currentChecksums[relName] = ComputeFullHash(file);
                    }
                }

                if (File.Exists(checksumPath))
                {
                    // Compare against stored checksums
                    string json = File.ReadAllText(checksumPath);
                    var stored = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (stored != null)
                    {
                        foreach (var kvp in stored)
                        {
                            if (currentChecksums.TryGetValue(kvp.Key, out string currentHash))
                            {
                                if (!string.Equals(currentHash, kvp.Value, StringComparison.OrdinalIgnoreCase))
                                {
                                    report.AppendLine($"  MODIFIED: {kvp.Key}");
                                    StingLog.Warn($"Data integrity: {kvp.Key} checksum changed (expected {kvp.Value.Substring(0, 8)}..., got {currentHash.Substring(0, 8)}...)");
                                    issues++;
                                }
                            }
                            else
                            {
                                report.AppendLine($"  MISSING: {kvp.Key}");
                                issues++;
                            }
                        }

                        // Report new files not in stored checksums
                        foreach (var kvp in currentChecksums)
                        {
                            if (!stored.ContainsKey(kvp.Key))
                                report.AppendLine($"  NEW: {kvp.Key} (not in baseline)");
                        }
                    }

                    if (issues == 0)
                        report.AppendLine("  All checksums match baseline — data integrity OK");
                    else
                        report.AppendLine($"  {issues} file(s) changed since baseline — run Check Data again to update");
                }
                else
                {
                    // First run: save checksums
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(currentChecksums, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(checksumPath, json);
                    report.AppendLine($"  Baseline checksums saved ({currentChecksums.Count} files)");
                    StingLog.Info($"Data integrity: baseline checksums saved to {checksumPath}");
                }
            }
            catch (Exception ex)
            {
                report.AppendLine($"  Checksum verification error: {ex.Message}");
                StingLog.Warn($"Checksum verification failed: {ex.Message}");
            }
            return issues;
        }

        /// <summary>Compute full SHA-256 hash of a file.</summary>
        private static string ComputeFullHash(string filePath)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch
            {
                return "error";
            }
        }
    }
}
