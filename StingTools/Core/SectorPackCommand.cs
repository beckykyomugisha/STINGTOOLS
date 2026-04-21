// Phase 108m — Sector starter packs. ApplySectorPackCommand reads a
// per-sector JSON and injects its defaults into project_config.json +
// tag style + recommended presets. Lets a new project kickoff pick
// "Healthcare" and start with the right bundle of settings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using StingTools.UI;

namespace StingTools.Core
{
    public class SectorPack
    {
        public string Id;
        public string Label;
        public string Description;
        public List<string> Families = new List<string>();
        public List<string> Presets = new List<string>();
        public string TagStyle;
        public string PreambleProfile;
        public JObject BoqDefaults;
        public List<string> WorkflowPresets = new List<string>();
        public string Notes;
    }

    internal static class SectorPackLoader
    {
        public static List<SectorPack> LoadAll()
        {
            var list = new List<SectorPack>();
            string dir = Path.Combine(StingToolsApp.DataPath ?? "", "SectorPacks");
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*_PACK.json"))
            {
                try
                {
                    var j = JObject.Parse(File.ReadAllText(f));
                    var p = new SectorPack
                    {
                        Id = j["id"]?.ToString() ?? "",
                        Label = j["label"]?.ToString() ?? "",
                        Description = j["description"]?.ToString() ?? "",
                        TagStyle = j["tag_style"]?.ToString() ?? "",
                        PreambleProfile = j["preamble_profile"]?.ToString() ?? "",
                        BoqDefaults = j["boq_defaults"] as JObject,
                        Notes = j["notes"]?.ToString() ?? ""
                    };
                    if (j["families"] is JArray fa) foreach (var x in fa) p.Families.Add(x.ToString());
                    if (j["presets"]  is JArray pa) foreach (var x in pa) p.Presets.Add(x.ToString());
                    if (j["workflow_presets"] is JArray wa) foreach (var x in wa) p.WorkflowPresets.Add(x.ToString());
                    list.Add(p);
                }
                catch (Exception ex) { StingLog.Warn($"SectorPackLoader {Path.GetFileName(f)}: {ex.Message}"); }
            }
            return list;
        }

        public static void Apply(SectorPack pack)
        {
            if (pack == null) return;
            try
            {
                if (!string.IsNullOrEmpty(pack.PreambleProfile))
                    TagConfig.SetConfigValue("BOQ_TENDER_PROJECT_TYPE", pack.PreambleProfile);
                if (pack.BoqDefaults != null)
                {
                    foreach (var kv in pack.BoqDefaults.Properties())
                        TagConfig.SetConfigValue("BOQ_TENDER_" + kv.Name, kv.Value.ToString());
                }
                if (!string.IsNullOrEmpty(pack.TagStyle))
                    TagConfig.SetConfigValue("DEFAULT_TAG_STYLE", pack.TagStyle);
                TagConfig.SetConfigValue("ACTIVE_SECTOR_PACK", pack.Id);
                StingLog.Info($"Sector pack applied: {pack.Label} (families={pack.Families.Count}, presets={pack.Presets.Count})");
            }
            catch (Exception ex) { StingLog.Error("SectorPack.Apply", ex); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ApplySectorPackCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var packs = SectorPackLoader.LoadAll();
                if (packs.Count == 0)
                {
                    TaskDialog.Show("Sector Packs", "No sector packs found in Data/SectorPacks/.");
                    return Result.Cancelled;
                }

                var listItems = packs.Select(p => new StingTools.UI.StingListPicker.ListItem
                {
                    Label = p.Label,
                    Detail = p.Description,
                    Tag = p
                }).ToList();
                var picked = StingTools.UI.StingListPicker.Show(
                    "Choose Sector Starter Pack",
                    "Apply a pre-built bundle: BOQ defaults, tag style, preamble profile, recommended families + presets.",
                    listItems, allowMultiSelect: false);
                var pack = picked?.FirstOrDefault()?.Tag as SectorPack;
                if (pack == null) return Result.Cancelled;

                SectorPackLoader.Apply(pack);
                var rp = StingResultPanel.Create($"Sector Pack: {pack.Label}")
                    .SetSubtitle(pack.Description)
                    .AddSection("APPLIED")
                    .Metric("Preamble profile", pack.PreambleProfile)
                    .Metric("Tag style",        pack.TagStyle)
                    .Metric("Recommended families", pack.Families.Count.ToString())
                    .Metric("Recommended presets",  pack.Presets.Count.ToString())
                    .Metric("Workflow presets",     pack.WorkflowPresets.Count.ToString());
                if (pack.BoqDefaults != null)
                {
                    rp.AddSection("BOQ DEFAULTS");
                    foreach (var kv in pack.BoqDefaults.Properties())
                        rp.Metric(kv.Name, kv.Value.ToString());
                }
                rp.AddSection("NOTES").Text(pack.Notes);
                rp.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("ApplySectorPackCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
