using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>
    /// Project configuration editor — creates, loads, and edits project_config.json.
    /// Allows customising TagConfig lookup tables (DISC, SYS, PROD, FUNC, LOC, ZONE)
    /// without editing source code. The config file is stored alongside the Revit
    /// project file or in the plugin data directory.
    ///
    /// Features:
    ///   - View current configuration (built-in or from project_config.json)
    ///   - Save current config to project_config.json for project-specific overrides
    ///   - Load config from an existing project_config.json
    ///   - Reset to built-in defaults
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ConfigEditorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            // Find existing config
            string projectDir = Path.GetDirectoryName(doc.PathName);
            string configPath = null;
            if (!string.IsNullOrEmpty(projectDir))
                configPath = Path.Combine(projectDir, "project_config.json");

            bool configExists = configPath != null && File.Exists(configPath);

            TaskDialog td = new TaskDialog("Tag Configuration Editor");
            td.MainInstruction = "Tag Configuration";
            td.MainContent =
                $"Current source: {TagConfig.ConfigSource}\n" +
                $"Disciplines:   {TagConfig.DiscMap.Count} category mappings\n" +
                $"Systems:       {TagConfig.SysMap.Count} system groups\n" +
                $"Products:      {TagConfig.ProdMap.Count} product codes\n" +
                $"Functions:     {TagConfig.FuncMap.Count} function codes\n" +
                $"Locations:     {string.Join(", ", TagConfig.LocCodes)}\n" +
                $"Zones:         {string.Join(", ", TagConfig.ZoneCodes)}\n\n" +
                (configExists
                    ? $"Config file found: {configPath}"
                    : "No project_config.json found.");

            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "View Full Configuration",
                "Display all lookup tables in detail");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Save Config to Project",
                "Export current settings to project_config.json alongside the .rvt file");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                configExists ? "Reload from project_config.json" : "Load from Data Directory",
                configExists ? "Re-read project_config.json" : "Look for config in plugin data folder");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Reset to Defaults",
                "Restore all lookup tables to built-in defaults");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var result = td.Show();

            switch (result)
            {
                case TaskDialogResult.CommandLink1:
                    ShowFullConfig();
                    break;

                case TaskDialogResult.CommandLink2:
                    SaveConfig(configPath, doc);
                    break;

                case TaskDialogResult.CommandLink3:
                    if (configExists)
                    {
                        TagConfig.LoadFromFile(configPath);
                        TaskDialog.Show("Config Loaded",
                            $"Loaded configuration from:\n{configPath}\n\n" +
                            $"Source: {TagConfig.ConfigSource}");
                    }
                    else
                    {
                        string dataConfig = StingToolsApp.FindDataFile("project_config.json");
                        if (dataConfig != null)
                        {
                            TagConfig.LoadFromFile(dataConfig);
                            TaskDialog.Show("Config Loaded",
                                $"Loaded from data directory:\n{dataConfig}");
                        }
                        else
                        {
                            TaskDialog.Show("Config Not Found",
                                "No project_config.json found in data directory.");
                        }
                    }
                    break;

                case TaskDialogResult.CommandLink4:
                    TagConfig.LoadDefaults();
                    TaskDialog.Show("Config Reset", "All lookup tables reset to built-in defaults.");
                    break;

                default:
                    return Result.Cancelled;
            }

            return Result.Succeeded;
        }

        private void ShowFullConfig()
        {
            var report = new StringBuilder();
            report.AppendLine("═══ Tag Configuration ═══");
            report.AppendLine($"Source: {TagConfig.ConfigSource}");
            report.AppendLine();

            report.AppendLine($"── DISC Map ({TagConfig.DiscMap.Count} entries) ──");
            foreach (var kvp in TagConfig.DiscMap.OrderBy(x => x.Value).ThenBy(x => x.Key))
                report.AppendLine($"  {kvp.Value,-4} ← {kvp.Key}");

            report.AppendLine();
            report.AppendLine($"── SYS Map ({TagConfig.SysMap.Count} groups) ──");
            foreach (var kvp in TagConfig.SysMap.OrderBy(x => x.Key))
                report.AppendLine($"  {kvp.Key,-6} → {string.Join(", ", kvp.Value)}");

            report.AppendLine();
            report.AppendLine($"── PROD Map ({TagConfig.ProdMap.Count} entries) ──");
            foreach (var kvp in TagConfig.ProdMap.OrderBy(x => x.Value))
                report.AppendLine($"  {kvp.Value,-5} ← {kvp.Key}");

            report.AppendLine();
            report.AppendLine($"── FUNC Map ({TagConfig.FuncMap.Count} entries) ──");
            foreach (var kvp in TagConfig.FuncMap.OrderBy(x => x.Key))
                report.AppendLine($"  {kvp.Key,-6} → {kvp.Value}");

            report.AppendLine();
            report.AppendLine($"── LOC Codes ──");
            report.AppendLine($"  {string.Join(", ", TagConfig.LocCodes)}");
            report.AppendLine($"── ZONE Codes ──");
            report.AppendLine($"  {string.Join(", ", TagConfig.ZoneCodes)}");

            TaskDialog full = new TaskDialog("Full Tag Configuration");
            full.MainInstruction = $"{TagConfig.DiscMap.Count} disciplines | {TagConfig.SysMap.Count} systems | {TagConfig.ProdMap.Count} products";
            full.MainContent = report.ToString();
            full.Show();
        }

        private void SaveConfig(string path, Document doc)
        {
            if (string.IsNullOrEmpty(path))
            {
                TaskDialog.Show("Save Config", "Save the Revit project first to establish a file path.");
                return;
            }

            try
            {
                var config = new Dictionary<string, object>
                {
                    { "DISC_MAP", TagConfig.DiscMap },
                    { "SYS_MAP", TagConfig.SysMap },
                    { "PROD_MAP", TagConfig.ProdMap },
                    { "FUNC_MAP", TagConfig.FuncMap },
                    { "LOC_CODES", TagConfig.LocCodes },
                    { "ZONE_CODES", TagConfig.ZoneCodes },
                };

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(path, json);

                TaskDialog.Show("Config Saved",
                    $"Configuration saved to:\n{path}\n\n" +
                    "Edit this JSON file to customise lookup tables for this project. " +
                    "Use 'Reload' to apply changes.");
                StingLog.Info($"Config saved to {path}");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Save Failed", $"Could not save config:\n{ex.Message}");
                StingLog.Error("Config save failed", ex);
            }
        }
    }
}
