using System;
using System.IO;
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

            TaskDialog td = new TaskDialog("Check Data");
            td.MainInstruction = $"{fileCount} data files found";
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
    }
}
