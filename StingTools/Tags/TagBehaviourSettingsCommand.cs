// TagBehaviourSettingsCommand — the tagging behaviour switches, one click each.
//
// RETAG_MOVED_ELEMENTS and RENUMBER_ON_OVERWRITE (TAGACC-4 / -5) change what a tagging
// run does to tags that already exist, so a user has to be able to see and change them
// without opening project_config.json. This dialog shows the current value of each in
// plain words, flips one per click, writes only that key to the project_config.json
// beside the model (the file the plugin loads on open), applies it immediately, and
// reopens so several can be changed in one visit.
//
// Dispatched as "TagBehaviour" (button "Tag Rules" beside Batch Tag). The same switches
// are also in Project Cfg → View Full Configuration.

using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    [Transaction(TransactionMode.ReadOnly)]
    public class TagBehaviourSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            string projectDir = string.IsNullOrEmpty(doc.PathName) ? null : global::StingTools.Core.StingPaths.ModelDir(doc);
            string configPath = string.IsNullOrEmpty(projectDir) ? null : Path.Combine(projectDir, "project_config.json");

            for (int guard = 0; guard < 20; guard++)
            {
                var td = new TaskDialog("STING — Tagging behaviour");
                td.MainInstruction = "How tagging treats existing tags";
                td.MainContent =
                    "Moved elements:  " + (TagConfig.RetagMovedElements
                        ? "tags UPDATE when an element moves to another level or room (sequence number kept)"
                        : "tags stay FIXED when an element moves") + "\n" +
                    "Overwrite runs:  " + (TagConfig.RenumberOnOverwrite
                        ? "every element gets a NEW sequence number"
                        : "sequence numbers are KEPT where still unique") + "\n" +
                    "STATUS token:    " + (TagConfig.AutoCorrectStatusFromPhase
                        ? "always re-derived from the Revit phase"
                        : "kept once set") + "\n" +
                    "Shared models:   " + SeqLockText(TagConfig.SeqLockMode) + "\n\n" +
                    (configPath == null
                        ? "Save the model first: settings are stored in project_config.json beside it."
                        : "Saved to: " + configPath);

                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    TagConfig.RetagMovedElements ? "Keep tags fixed when elements move" : "Update tags when elements move",
                    $"RETAG_MOVED_ELEMENTS = {TagConfig.RetagMovedElements} → {!TagConfig.RetagMovedElements}");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    TagConfig.RenumberOnOverwrite ? "Keep sequence numbers on Overwrite" : "Renumber everything on Overwrite",
                    $"RENUMBER_ON_OVERWRITE = {TagConfig.RenumberOnOverwrite} → {!TagConfig.RenumberOnOverwrite}");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    TagConfig.AutoCorrectStatusFromPhase ? "Keep STATUS once set" : "Always re-derive STATUS from the phase",
                    $"AUTO_CORRECT_STATUS_FROM_PHASE = {TagConfig.AutoCorrectStatusFromPhase} → {!TagConfig.AutoCorrectStatusFromPhase}");
                string nextMode = NextSeqLockMode(TagConfig.SeqLockMode);
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                    "Shared models: " + SeqLockText(nextMode),
                    $"SEQ_LOCK_MODE = {TagConfig.SeqLockMode} → {nextMode}");
                td.CommonButtons = TaskDialogCommonButtons.Close;

                var r = td.Show();
                bool changed;
                if (r == TaskDialogResult.CommandLink1)
                    changed = Toggle(configPath, "RETAG_MOVED_ELEMENTS", !TagConfig.RetagMovedElements,
                        v => TagConfig.RetagMovedElements = v);
                else if (r == TaskDialogResult.CommandLink2)
                    changed = Toggle(configPath, "RENUMBER_ON_OVERWRITE", !TagConfig.RenumberOnOverwrite,
                        v => TagConfig.RenumberOnOverwrite = v);
                else if (r == TaskDialogResult.CommandLink3)
                    changed = Toggle(configPath, "AUTO_CORRECT_STATUS_FROM_PHASE", !TagConfig.AutoCorrectStatusFromPhase,
                        v => TagConfig.AutoCorrectStatusFromPhase = v);
                else if (r == TaskDialogResult.CommandLink4)
                    changed = Save(configPath, "SEQ_LOCK_MODE", nextMode, () => TagConfig.SeqLockMode = nextMode);
                else
                    break;

                if (changed) StingAutoTagger.InvalidateContext();
            }
            return Result.Succeeded;
        }

        private static string NextSeqLockMode(string mode)
        {
            switch ((mode ?? "block").ToLowerInvariant())
            {
                case "block": return "warn";
                case "warn": return "off";
                default: return "block";
            }
        }

        private static string SeqLockText(string mode)
        {
            switch ((mode ?? "block").ToLowerInvariant())
            {
                case "warn": return "number anyway when another user holds the SEQ counter (duplicates repaired after sync)";
                case "off": return "ignore the SEQ counter lock";
                default: return "wait for the SEQ counter (no duplicate numbers between users)";
            }
        }

        private static bool Toggle(string configPath, string key, bool value, Action<bool> apply)
            => Save(configPath, key, value, () => apply(value));

        private static bool Save(string configPath, string key, object value, Action apply)
        {
            if (string.IsNullOrEmpty(configPath))
            {
                TaskDialog.Show("STING", "Save the Revit model first — the setting is stored in project_config.json beside it.");
                return false;
            }
            try
            {
                var data = File.Exists(configPath)
                    ? Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(configPath))
                      ?? new System.Collections.Generic.Dictionary<string, object>()
                    : new System.Collections.Generic.Dictionary<string, object>();
                data[key] = value;
                string tmp = configPath + ".tmp";
                File.WriteAllText(tmp, Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented));
                File.Move(tmp, configPath, true);
                apply();
                StingLog.Info($"Tagging behaviour: {key} → {value} ({configPath})");
                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error($"Tagging behaviour: could not save {key}", ex);
                TaskDialog.Show("Save failed", $"Could not update project_config.json:\n{ex.Message}");
                return false;
            }
        }
    }
}
