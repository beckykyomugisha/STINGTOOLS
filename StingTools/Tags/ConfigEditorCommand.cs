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
                $"Tag format:    {string.Join(TagConfig.Separator, TagConfig.SegmentOrder)} (pad {TagConfig.NumPad})\n" +
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

            // G2: Snapshot current SEQ settings before any config change
            bool prevSeqIncludeZone = TagConfig.SeqIncludeZone;
            bool prevSeqLevelReset = TagConfig.SeqLevelReset;
            SeqScheme prevSeqScheme = TagConfig.CurrentSeqScheme;

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
                        if (!CheckSeqMigrationGuard(doc, prevSeqIncludeZone, prevSeqLevelReset, prevSeqScheme))
                        {
                            // User cancelled — restore original values
                            TagConfig.SeqIncludeZone = prevSeqIncludeZone;
                            TagConfig.SeqLevelReset = prevSeqLevelReset;
                            TagConfig.CurrentSeqScheme = prevSeqScheme;
                            StingLog.Info("ConfigEditor: SEQ setting change reverted by user");
                            return Result.Cancelled;
                        }
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
                            if (!CheckSeqMigrationGuard(doc, prevSeqIncludeZone, prevSeqLevelReset, prevSeqScheme))
                            {
                                TagConfig.SeqIncludeZone = prevSeqIncludeZone;
                                TagConfig.SeqLevelReset = prevSeqLevelReset;
                                TagConfig.CurrentSeqScheme = prevSeqScheme;
                                StingLog.Info("ConfigEditor: SEQ setting change reverted by user");
                                return Result.Cancelled;
                            }
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
                    if (!CheckSeqMigrationGuard(doc, prevSeqIncludeZone, prevSeqLevelReset, prevSeqScheme))
                    {
                        TagConfig.SeqIncludeZone = prevSeqIncludeZone;
                        TagConfig.SeqLevelReset = prevSeqLevelReset;
                        TagConfig.CurrentSeqScheme = prevSeqScheme;
                        StingLog.Info("ConfigEditor: SEQ setting change reverted by user on reset");
                        return Result.Cancelled;
                    }
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

            report.AppendLine("── Tag Format ──");
            report.AppendLine($"  Separator:     \"{TagConfig.Separator}\"");
            report.AppendLine($"  Padding:       {TagConfig.NumPad} digits");
            report.AppendLine($"  Segments:      {string.Join(", ", TagConfig.SegmentOrder)}");
            report.AppendLine($"  Example:       M{TagConfig.Separator}BLD1{TagConfig.Separator}Z01{TagConfig.Separator}L02{TagConfig.Separator}HVAC{TagConfig.Separator}SUP{TagConfig.Separator}AHU{TagConfig.Separator}{"1".PadLeft(TagConfig.NumPad, '0')}");
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
                    { "TAG_FORMAT", new Dictionary<string, object>
                        {
                            { "separator", TagConfig.Separator },
                            { "num_pad", TagConfig.NumPad },
                            { "segment_order", TagConfig.SegmentOrder }
                        }
                    },
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

        /// <summary>
        /// G2: SEQ scheme migration guard. If existing tags exist and SEQ settings
        /// have changed, warn the user and let them cancel to restore original values.
        /// Returns true to proceed, false to revert.
        /// </summary>
        private bool CheckSeqMigrationGuard(Document doc,
            bool prevSeqIncludeZone, bool prevSeqLevelReset, SeqScheme prevSeqScheme)
        {
            // Check if any SEQ setting actually changed
            bool seqChanged = TagConfig.SeqIncludeZone != prevSeqIncludeZone ||
                              TagConfig.SeqLevelReset != prevSeqLevelReset ||
                              TagConfig.CurrentSeqScheme != prevSeqScheme;
            if (!seqChanged)
                return true; // No change — proceed without warning

            // Count existing tags in the project
            var known = new HashSet<string>(TagConfig.DiscMap.Keys);
            int tagCount = 0;
            foreach (Element el in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                string cat = ParameterHelpers.GetCategoryName(el);
                if (!known.Contains(cat)) continue;
                string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (!string.IsNullOrEmpty(tag))
                {
                    tagCount++;
                    if (tagCount > 0) break; // Only need to know if any exist
                }
            }

            if (tagCount == 0)
                return true; // No existing tags — safe to change

            // Build change summary
            var changes = new StringBuilder();
            changes.AppendLine("The following SEQ numbering settings have changed:");
            if (TagConfig.CurrentSeqScheme != prevSeqScheme)
                changes.AppendLine($"  Scheme: {prevSeqScheme} -> {TagConfig.CurrentSeqScheme}");
            if (TagConfig.SeqIncludeZone != prevSeqIncludeZone)
                changes.AppendLine($"  Include Zone in SEQ key: {prevSeqIncludeZone} -> {TagConfig.SeqIncludeZone}");
            if (TagConfig.SeqLevelReset != prevSeqLevelReset)
                changes.AppendLine($"  Reset SEQ per level: {prevSeqLevelReset} -> {TagConfig.SeqLevelReset}");

            TaskDialog warn = new TaskDialog("STING — SEQ Scheme Change Warning");
            warn.MainInstruction = "SEQ numbering settings changed with existing tags";
            warn.MainContent =
                $"This project already has tagged elements.\n\n" +
                changes.ToString() + "\n" +
                "Changing SEQ settings will cause newly tagged elements to use a different " +
                "numbering scheme than existing tags. This may create inconsistent or " +
                "duplicate sequence numbers.\n\n" +
                "Recommendation: Run 'Tag Format Migration' or 'Batch Tag (Overwrite)' " +
                "after changing SEQ settings to re-number all elements consistently.";
            warn.CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel;
            warn.DefaultButton = TaskDialogResult.Cancel;

            if (warn.Show() == TaskDialogResult.Cancel)
            {
                StingLog.Warn("ConfigEditor: SEQ setting change cancelled by user " +
                    $"(scheme: {prevSeqScheme}->{TagConfig.CurrentSeqScheme}, " +
                    $"zone: {prevSeqIncludeZone}->{TagConfig.SeqIncludeZone}, " +
                    $"levelReset: {prevSeqLevelReset}->{TagConfig.SeqLevelReset})");
                return false;
            }

            StingLog.Info("ConfigEditor: SEQ setting change accepted by user");
            return true;
        }
    }
}
