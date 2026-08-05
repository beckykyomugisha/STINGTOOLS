// Phase 108m — Multi-building extras: B3 SEQ range validator + B4 CDE
// folder generator with per-building sub-folders.
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
    // ══════════════════════════════════════════════════════════════════════
    //  B3 — SEQ range validator (ranges exist; this surfaces the config)
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SeqRangeValidationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                string raw = TagConfig.GetConfigValue("SEQ_RANGE_ALLOCATION") ?? "";
                var rp = StingResultPanel.Create("SEQ Range Allocation")
                    .SetSubtitle("Per-building SEQ counter ranges (Phase 59 FUT-01).")
                    .AddSection("CONFIG");
                if (string.IsNullOrEmpty(raw))
                {
                    rp.Text("No SEQ_RANGE_ALLOCATION in project_config.json.");
                    rp.AddSection("TO ENABLE").Text("Add to project_config.json:");
                    rp.Text("  \"SEQ_RANGE_ALLOCATION\": {");
                    rp.Text("    \"BLD1\": { \"min\": 1, \"max\": 4999 },");
                    rp.Text("    \"BLD2\": { \"min\": 5000, \"max\": 7999 },");
                    rp.Text("    \"BLD3\": { \"min\": 8000, \"max\": 9999 }");
                    rp.Text("  }");
                }
                else
                {
                    try
                    {
                        var j = JObject.Parse(raw);
                        foreach (var kv in j.Properties())
                        {
                            var body = kv.Value as JObject;
                            rp.Metric(kv.Name, body != null ? $"{body["min"]} — {body["max"]}" : kv.Value.ToString());
                        }
                    }
                    catch (Exception ex)
                    {
                        rp.Text($"Malformed SEQ_RANGE_ALLOCATION: {ex.Message}");
                    }
                }
                rp.Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("SeqRangeValidationCommand", ex); message = ex.Message; return Result.Failed; }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  B4 — Building-aware CDE folder generator
    //  For each LOC code in the vocabulary, creates the ISO 19650 folder
    //  tree under {projectDir}/_CDE/{state}/{LOC}/ for all four CDE states.
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BuildingAwareCDEFoldersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Result.Failed;
                var doc = ctx.Doc;
                string projDir = Path.GetDirectoryName(doc.PathName ?? "");
                if (string.IsNullOrEmpty(projDir) || !Directory.Exists(projDir))
                {
                    TaskDialog.Show("CDE Folders", "Save the project before generating CDE folders.");
                    return Result.Cancelled;
                }

                var locs = LocVocabularyOverride.GetAllLocCodes();
                var states = new[] { "WIP", "SHARED", "PUBLISHED", "ARCHIVE" };
                var subs = new[] { "MODELS", "DRAWINGS", "SCHEDULES", "BOQ", "COBie", "REPORTS" };

                int created = 0;
                foreach (var state in states)
                {
                    foreach (var loc in locs)
                    {
                        string bldDir = Path.Combine(projDir, "_CDE", state, loc);
                        if (!Directory.Exists(bldDir)) { Directory.CreateDirectory(bldDir); created++; }
                        foreach (var sub in subs)
                        {
                            string subDir = Path.Combine(bldDir, sub);
                            if (!Directory.Exists(subDir)) { Directory.CreateDirectory(subDir); created++; }
                        }
                    }
                    // Common (non-building) folders under each state
                    string commonDir = Path.Combine(projDir, "_CDE", state, "_COMMON");
                    if (!Directory.Exists(commonDir)) { Directory.CreateDirectory(commonDir); created++; }
                }

                StingResultPanel.Create("CDE Folders")
                    .SetSubtitle("ISO 19650 CDE folder tree created per-building.")
                    .AddSection("CREATED")
                    .Metric("Directories created", created.ToString())
                    .Metric("Buildings",           locs.Count.ToString())
                    .Metric("States",              states.Length.ToString())
                    .Metric("Subfolders per building-state", subs.Length.ToString())
                    .AddSection("ROOT")
                    .Text(Path.Combine(projDir, "_CDE"))
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("BuildingAwareCDEFolders", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
