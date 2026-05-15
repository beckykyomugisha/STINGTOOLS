using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BIMManager
{
    /// <summary>
    /// Feature gap 1 — Cost File Browser.
    /// Lets the user point to their own cost-rate CSV without hand-editing files.
    /// The chosen path is saved to &lt;project&gt;/_BIM_COORD/cost_rates_override.json.
    /// The existing cost_rates_5d.csv in StingTools/Data/ is used as fallback
    /// when no override exists.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostFileBrowserCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uidoc = commandData.Application.ActiveUIDocument;
                var doc   = uidoc?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("STING — Cost File Browser", "No active document.");
                    return Result.Cancelled;
                }

                // Show current override (if any)
                string? currentOverridePath = LoadOverridePath(doc);
                string currentInfo = currentOverridePath != null
                    ? $"Current override:\n{currentOverridePath}\n\n"
                    : "No override set — using built-in cost_rates_5d.csv.\n\n";

                var dlg = new TaskDialog("STING — Cost File Browser")
                {
                    MainContent       = currentInfo + "Choose a CSV file to use as your cost rate source.\n" +
                                        "Required columns (case-insensitive): MAT_CODE, RATE, UNIT",
                    AllowCancellation = true,
                };
                dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Browse for CSV file…");
                if (currentOverridePath != null)
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Clear override (revert to built-in)");

                var result = dlg.Show();
                if (result == TaskDialogResult.Cancel)
                    return Result.Cancelled;

                if (result == TaskDialogResult.CommandLink2)
                {
                    ClearOverride(doc);
                    TaskDialog.Show("STING — Cost File Browser", "Override cleared. Built-in cost_rates_5d.csv will be used.");
                    return Result.Succeeded;
                }

                // Open file dialog
                var ofd = new OpenFileDialog
                {
                    Title             = "Select Cost Rate CSV",
                    Filter            = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    CheckFileExists   = true,
                    Multiselect       = false,
                };
                if (currentOverridePath != null && File.Exists(currentOverridePath))
                    ofd.InitialDirectory = Path.GetDirectoryName(currentOverridePath);

                if (ofd.ShowDialog() != true)
                    return Result.Cancelled;

                string chosenPath = ofd.FileName;

                // Validate CSV headers
                string? validationError = ValidateCsvHeaders(chosenPath);
                if (validationError != null)
                {
                    TaskDialog.Show("STING — Cost File Browser",
                        $"The selected file is missing required columns:\n{validationError}\n\n" +
                        "Required columns: MAT_CODE, RATE, UNIT (case-insensitive).\n" +
                        "Please check the file and try again.");
                    return Result.Failed;
                }

                // Save override
                SaveOverride(doc, chosenPath);

                StingLog.Info($"CostFileBrowser: override set to '{chosenPath}'");
                TaskDialog.Show("STING — Cost File Browser",
                    $"Cost rate file override saved.\n\nFile: {chosenPath}\n\n" +
                    "All 5D commands will use this file instead of the built-in rates.\n" +
                    "Use 'Clear override' to revert.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("CostFileBrowserCommand", ex);
                TaskDialog.Show("STING — Cost File Browser", $"Error: {ex.Message}");
                return Result.Failed;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Validates that the CSV file has at least MAT_CODE, RATE, and UNIT columns.</summary>
        private static string? ValidateCsvHeaders(string path)
        {
            try
            {
                using var sr = new StreamReader(path);
                string? headerLine = sr.ReadLine();
                if (string.IsNullOrWhiteSpace(headerLine))
                    return "File is empty.";

                var headers = headerLine.Split(',')
                    .Select(h => h.Trim().Trim('"').ToLowerInvariant())
                    .ToHashSet();

                var missing = new System.Collections.Generic.List<string>();
                if (!headers.Contains("mat_code")) missing.Add("MAT_CODE");
                if (!headers.Contains("rate"))     missing.Add("RATE");
                if (!headers.Contains("unit"))     missing.Add("UNIT");

                return missing.Count == 0 ? null : string.Join(", ", missing);
            }
            catch (Exception ex)
            {
                return $"Could not read file: {ex.Message}";
            }
        }

        /// <summary>Returns the override path from the project's _BIM_COORD folder, or null.</summary>
        public static string? LoadOverridePath(Document doc)
        {
            string overrideFile = GetOverrideFilePath(doc);
            if (!File.Exists(overrideFile)) return null;
            try
            {
                var json = File.ReadAllText(overrideFile);
                dynamic? obj = JsonConvert.DeserializeObject(json);
                string? path = obj?.path?.ToString();
                return path != null && File.Exists(path) ? path : null;
            }
            catch { return null; }
        }

        private static void SaveOverride(Document doc, string csvPath)
        {
            string overrideFile = GetOverrideFilePath(doc);
            Directory.CreateDirectory(Path.GetDirectoryName(overrideFile)!);
            var payload = new
            {
                path    = csvPath,
                updated = DateTime.UtcNow.ToString("O"),
            };
            File.WriteAllText(overrideFile, JsonConvert.SerializeObject(payload, Formatting.Indented));
        }

        private static void ClearOverride(Document doc)
        {
            string overrideFile = GetOverrideFilePath(doc);
            if (File.Exists(overrideFile))
                File.Delete(overrideFile);
        }

        private static string GetOverrideFilePath(Document doc)
        {
            string projDir = OutputLocationHelper.GetBimCoordDirectory(doc);
            return Path.Combine(projDir, "cost_rates_override.json");
        }
    }
}
