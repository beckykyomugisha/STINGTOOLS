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
                    ShowFullConfig(configPath);
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

        private void ShowFullConfig(string configPath = null)
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

            report.AppendLine();
            report.AppendLine("── Advanced Settings ──");
            report.AppendLine($"  AUTO_CORRECT_STATUS_FROM_PHASE : {TagConfig.AutoCorrectStatusFromPhase}");
            report.AppendLine($"    When true, STATUS token is always re-derived from Revit phase data,");
            report.AppendLine($"    overwriting any manually set value. Default: false.");
            double leaderMargin = TagConfig.GetConfigDouble("LEADER_CLEARANCE_MARGIN_FT", 0.5);
            report.AppendLine($"  LEADER_CLEARANCE_MARGIN_FT     : {leaderMargin:F2} ft  ({leaderMargin * 304.8:F0} mm)");
            report.AppendLine($"    Minimum distance from element centre before a leader is added.");
            report.AppendLine($"    Edit project_config.json to change either value.");

            TaskDialog full = new TaskDialog("Full Tag Configuration");
            full.MainInstruction = $"{TagConfig.DiscMap.Count} disciplines | {TagConfig.SysMap.Count} systems | {TagConfig.ProdMap.Count} products";
            full.MainContent = report.ToString();
            full.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Toggle AUTO_CORRECT_STATUS_FROM_PHASE",
                $"Currently: {TagConfig.AutoCorrectStatusFromPhase} — flips and saves to project_config.json");
            full.CommonButtons = TaskDialogCommonButtons.Close;
            var fullResult = full.Show();
            if (fullResult == TaskDialogResult.CommandLink1)
                ToggleAutoCorrectStatus(configPath);
        }

        /// <summary>GAP-UI-01: Toggle AUTO_CORRECT_STATUS_FROM_PHASE and persist to project_config.json.</summary>
        private void ToggleAutoCorrectStatus(string configPath)
        {
            if (string.IsNullOrEmpty(configPath))
            {
                TaskDialog.Show("STING", "Save the Revit project first — config path is not set.");
                return;
            }
            bool newValue = !TagConfig.AutoCorrectStatusFromPhase;
            TagConfig.AutoCorrectStatusFromPhase = newValue;
            try
            {
                // Read existing JSON (if any), patch the key, write back
                Dictionary<string, object> data;
                if (System.IO.File.Exists(configPath))
                {
                    string existingJson = System.IO.File.ReadAllText(configPath);
                    data = JsonConvert.DeserializeObject<Dictionary<string, object>>(existingJson)
                           ?? new Dictionary<string, object>();
                }
                else data = new Dictionary<string, object>();

                data["AUTO_CORRECT_STATUS_FROM_PHASE"] = newValue;
                string tmpPath = configPath + ".tmp";
                System.IO.File.WriteAllText(tmpPath, JsonConvert.SerializeObject(data, Formatting.Indented));
                System.IO.File.Move(tmpPath, configPath, true);
                StingLog.Info($"ConfigEditor: AUTO_CORRECT_STATUS_FROM_PHASE → {newValue}");
                TaskDialog.Show("Setting Saved",
                    $"AUTO_CORRECT_STATUS_FROM_PHASE = {newValue}\n\nThis setting is now active for this project.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Save Failed", $"Could not update config:\n{ex.Message}");
                StingLog.Error("ToggleAutoCorrectStatus failed", ex);
            }
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
                    // GAP-UI-01: Advanced settings — included so users can see and edit them in the JSON
                    { "AUTO_CORRECT_STATUS_FROM_PHASE", TagConfig.AutoCorrectStatusFromPhase },
                    // GAP-UI-02: Leader clearance margin — persisted at whatever the current config value is
                    { "LEADER_CLEARANCE_MARGIN_FT", TagConfig.GetConfigDouble("LEADER_CLEARANCE_MARGIN_FT", 0.5) },
                };

                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                string tmpPath = path + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, path, true);

                // GAP-6B: Reload config immediately after save so changes take effect
                try
                {
                    TagConfig.LoadFromFile(path);
                    ComplianceScan.InvalidateCache();
                    StingAutoTagger.InvalidateContext();
                    ParameterHelpers.InvalidateSessionCaches();
                    StingLog.Info("Config auto-reloaded after save");
                }
                catch (Exception reloadEx) { StingLog.Warn($"Config auto-reload: {reloadEx.Message}"); }

                TaskDialog.Show("Config Saved & Applied",
                    $"Configuration saved and reloaded:\n{path}\n\n" +
                    "Changes are now active. Edit this JSON file to customise lookup tables.");
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

    /// <summary>FIX-11.1: Guided editor for all STING data files with format hints and sync.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class GuidedDataEditorCommand : IExternalCommand
    {
        private static readonly (string key, string file, string desc)[] Files =
        {
            ("config",    "project_config.json",              "DISC/SYS maps, SEQ, separator, compliance gate"),
            ("params",    "MR_PARAMETERS.txt",                "Shared parameter definitions (Revit format)"),
            ("registry",  "PARAMETER_REGISTRY.json",          "Parameter GUIDs, groups, container definitions"),
            ("materials", "MATERIAL_SCHEMA.json",             "Material properties and cost schema"),
            ("labels",    "LABEL_DEFINITIONS.json",           "Tag label display definitions"),
            ("placement", "TAG_PLACEMENT_PRESETS_DEFAULT.json","Tag placement preset coordinates"),
            ("workflow",  "WORKFLOW_DailyQA_Enhanced.json",   "Workflow step sequences"),
        };

        public Result Execute(ExternalCommandData commandData, ref string msg, ElementSet el)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var td = new TaskDialog("STING — Data File Editor");
            td.MainInstruction = "Edit a STING data file";
            td.MainContent = "Opens the file in your system editor.\nClick Sync after saving to reload STING.";
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "project_config.json — Tag Configuration", "DISC map, SYS map, codes, SEQ, compliance gate");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "MR_PARAMETERS.txt — Shared Parameters", "Add/edit parameter definitions (Revit format)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "MATERIAL_SCHEMA.json — Materials", "Material properties and unit costs");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Other files (Registry, Labels, Presets, Workflow)", "Browse other data files");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;

            var choice = td.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;

            string fileKey = choice switch
            {
                TaskDialogResult.CommandLink1 => "config",
                TaskDialogResult.CommandLink2 => "params",
                TaskDialogResult.CommandLink3 => "materials",
                TaskDialogResult.CommandLink4 => PickOther(),
                _ => null
            };
            if (fileKey == null) return Result.Cancelled;

            var def = Array.Find(Files, f => f.key == fileKey);
            if (def.file == null) return Result.Cancelled;

            string path = StingToolsApp.FindDataFile(def.file);
            if (string.IsNullOrEmpty(path))
            {
                string pd = Path.GetDirectoryName(doc.PathName ?? "");
                if (!string.IsNullOrEmpty(pd)) path = Path.Combine(pd, def.file);
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                TaskDialog.Show("Not Found", $"{def.file} not found.\nRun setup first to create it.");
                return Result.Succeeded;
            }

            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) { TaskDialog.Show("Error", ex.Message); return Result.Failed; }

            var wait = new TaskDialog($"Editing: {def.file}");
            wait.MainInstruction = "Edit, save, then click Sync";
            wait.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Sync Changes", "Reload with updated file");
            wait.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Skip", "Don't reload now");
            wait.CommonButtons = TaskDialogCommonButtons.Cancel;
            if (wait.Show() != TaskDialogResult.CommandLink1) return Result.Succeeded;

            try
            {
                if (fileKey == "config")
                {
                    TagConfig.LoadFromFile(path);
                    ComplianceScan.InvalidateCache();
                    StingAutoTagger.InvalidateContext();
                    TaskDialog.Show("Synced", "project_config.json reloaded. TagConfig updated.");
                }
                else
                {
                    TaskDialog.Show("Saved", $"{def.file} saved. Restart may be needed for full effect.");
                }
            }
            catch (Exception ex) { TaskDialog.Show("Sync Error", ex.Message); }
            return Result.Succeeded;
        }

        private static string PickOther()
        {
            var td2 = new TaskDialog("Other Files");
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "PARAMETER_REGISTRY.json", "");
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "LABEL_DEFINITIONS.json", "");
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "TAG_PLACEMENT_PRESETS_DEFAULT.json", "");
            td2.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "WORKFLOW_DailyQA_Enhanced.json", "");
            td2.CommonButtons = TaskDialogCommonButtons.Cancel;
            return td2.Show() switch
            {
                TaskDialogResult.CommandLink1 => "registry",
                TaskDialogResult.CommandLink2 => "labels",
                TaskDialogResult.CommandLink3 => "placement",
                TaskDialogResult.CommandLink4 => "workflow",
                _ => null
            };
        }
    }
}
