#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
                var uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
                var doc   = uidoc?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("STING — Cost File Browser", "No active document.");
                    return Result.Cancelled;
                }

                string? currentOverridePath = LoadOverridePath(doc);
                string currentInfo = currentOverridePath != null
                    ? $"Current override:\n{currentOverridePath}\n\n"
                    : "No override set — using built-in cost_rates_5d.csv.\n\n";

                var dlg = new TaskDialog("STING — Cost File Browser")
                {
                    MainContent       = currentInfo + "Choose a CSV file to use as your cost rate source.\n" +
                                        "Required columns (case-insensitive): Category, Unit_Rate_UGX or Unit_Rate_USD, Unit",
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

                var ofd = new OpenFileDialog
                {
                    Title           = "Select Cost Rate CSV",
                    Filter          = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    CheckFileExists = true,
                    Multiselect     = false,
                };
                if (currentOverridePath != null && File.Exists(currentOverridePath))
                    ofd.InitialDirectory = Path.GetDirectoryName(currentOverridePath);

                if (ofd.ShowDialog() != true)
                    return Result.Cancelled;

                string chosenPath = ofd.FileName;

                string? validationError = ValidateCsvHeaders(chosenPath);
                if (validationError != null)
                {
                    TaskDialog.Show("STING — Cost File Browser",
                        $"The selected file is missing required columns:\n{validationError}\n\n" +
                        "Required columns (case-insensitive): Category, Unit_Rate_UGX\n" +
                        "(or Unit_Rate_USD), Unit - the layout of data/cost_rates_5d.csv.\n" +
                        "Please check the file and try again.");
                    return Result.Failed;
                }

                SaveOverride(doc, chosenPath);

                StingLog.Info($"CostFileBrowser: override set to '{chosenPath}'");
                TaskDialog.Show("STING — Cost File Browser",
                    $"Cost rate file override saved.\n\nFile: {chosenPath}\n\n" +
                    // DSCH-1: no rate loader reads cost_rates_override.json yet
                    // (BOQCostManager.LoadCsvRates resolves TagConfig.CostRatesFileName
                    // and has no document). This used to say "All 5D commands will use
                    // this file", which nothing made true. ROADMAP DSCH-1.
                    "The path is recorded for this project. Pricing does NOT read it yet:\n" +
                    "the BOQ and 5D commands still use data/cost_rates_5d.csv (or the file\n" +
                    "named by CostRatesFileName in project_config.json). To price from this\n" +
                    "file today, copy it over that file. Use 'Clear override' to remove it.");

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

        private static string? ValidateCsvHeaders(string path)
        {
            try
            {
                using var sr = new StreamReader(path);
                string? headerLine = sr.ReadLine();
                if (string.IsNullOrWhiteSpace(headerLine))
                    return "File is empty.";

                // The SAME header contract the loaders use (BOQ/Rates/CostRateCsv).
                // This used to demand MAT_CODE, RATE and UNIT — and the shipped
                // cost_rates_5d.csv has no RATE column, so the browser rejected the
                // very file format it was meant to accept.
                var missing = StingTools.BOQ.Rates.CostRateCsvLayout
                    .FromHeader(StingToolsApp.ParseCsvLine(headerLine))
                    .MissingRequired();

                return missing.Count == 0 ? null : string.Join(", ", missing);
            }
            catch (Exception ex)
            {
                return $"Could not read file: {ex.Message}";
            }
        }

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
            File.WriteAllText(overrideFile, JsonConvert.SerializeObject(payload, Newtonsoft.Json.Formatting.Indented));
        }

        private static void ClearOverride(Document doc)
        {
            string overrideFile = GetOverrideFilePath(doc);
            if (File.Exists(overrideFile))
                File.Delete(overrideFile);
        }

        private static string GetOverrideFilePath(Document doc)
        {
            // The class comment always said _BIM_COORD; the code wrote MISC (DOCX-11).
            return OutputLocationHelper.GetStorePath(doc, "cost_rates_override.json");
        }
    }
}
