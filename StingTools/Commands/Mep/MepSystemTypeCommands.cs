// StingTools — MEP System Type commands (Phase A: System Type Materializer).
//
//   MEP_BuildSystemTypes    — create any missing duct/pipe system types from
//                             STING_MEP_SYSTEM_TYPES.json (+ project override),
//                             stamping abbreviation + graphics on NEW types only.
//   MEP_RestyleSystemTypes  — same, but also re-applies the baseline graphics to
//                             EXISTING types (use when you want the corporate
//                             colours back after a project hand-tuned them).
//   MEP_InspectSystemTypes  — read-only: which definitions exist in the model vs
//                             which are still to be built; cross-refs classification.
//   MEP_ReloadSystemTypes   — drop the registry cache so an edit to the baseline or
//                             project override is picked up without restarting Revit.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepBuildSystemTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => MepSystemTypeRunner.Run(commandData, ref message, overwriteGraphics: false);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepRestyleSystemTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
            => MepSystemTypeRunner.Run(commandData, ref message, overwriteGraphics: true);
    }

    internal static class MepSystemTypeRunner
    {
        public static Result Run(ExternalCommandData commandData, ref string message, bool overwriteGraphics)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var rules = MepSystemTypeRegistry.Get(doc);
                int enabled = rules.Enabled.Count();
                if (enabled == 0)
                {
                    TaskDialog.Show("STING MEP",
                        "No enabled MEP system-type definitions found in STING_MEP_SYSTEM_TYPES.json " +
                        "(or the project override). Nothing to build.");
                    return Result.Cancelled;
                }

                MepSystemTypeResult res;
                using (var t = new Transaction(doc,
                    overwriteGraphics ? "STING Restyle MEP System Types" : "STING Build MEP System Types"))
                {
                    t.Start();
                    res = MepSystemTypeMaterializer.Materialize(doc, rules, overwriteGraphics);
                    t.Commit();
                }

                var panel = StingResultPanel.Create(
                    overwriteGraphics ? "MEP — Restyle System Types" : "MEP — Build System Types");
                panel.SetSubtitle(
                    $"{res.Created} created · {res.Updated} updated · {res.Skipped} skipped · {res.Failed} failed " +
                    $"(of {enabled} enabled definitions)");

                panel.AddSection("SUMMARY")
                     .Metric("Created",  res.Created.ToString())
                     .Metric("Updated",  res.Updated.ToString())
                     .Metric("Skipped",  res.Skipped.ToString())
                     .Metric("Failed",   res.Failed.ToString());

                panel.AddSection("SYSTEM TYPES");
                foreach (var r in res.Rows)
                    panel.Text($"{Glyph(r.Action)} {r.Name,-30} {r.Discipline,-5} {r.Classification,-20} {r.Note}");

                if (res.Warnings.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({res.Warnings.Count})");
                    foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                }

                panel.AddSection("NEXT");
                panel.Text("These types carry the Revit system CLASSIFICATION the 19 MEP filters in " +
                           "STING_AEC_FILTERS.json match on (RBS_SYSTEM_CLASSIFICATION_PARAM) — run " +
                           "AecFilters_Create + apply a coordination View Style Pack to see them colour up.");
                panel.Text("Assign ducts/pipes to these types (or route new runs) so the System Browser " +
                           "and the colour filters have data to act on.");
                panel.Show();

                StingLog.Info($"MEP system types: created={res.Created} updated={res.Updated} " +
                              $"skipped={res.Skipped} failed={res.Failed} overwrite={overwriteGraphics}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepBuildSystemTypesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string Glyph(MepTypeAction a) => a switch
        {
            MepTypeAction.Created => "✚",
            MepTypeAction.Updated => "↻",
            MepTypeAction.Skipped => "–",
            MepTypeAction.Failed  => "✖",
            _ => " "
        };
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepInspectSystemTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var rules = MepSystemTypeRegistry.Get(doc);

                var mechNames = new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType))
                    .Cast<MechanicalSystemType>().Select(t => t.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var pipeNames = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType))
                    .Cast<PipingSystemType>().Select(t => t.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                int present = 0, missing = 0;
                var panel = StingResultPanel.Create("MEP — System Type Inventory");

                panel.AddSection("DEFINITIONS");
                foreach (var d in rules.All)
                {
                    bool exists = d.IsDuct ? mechNames.Contains(d.Name)
                                : d.IsPipe ? pipeNames.Contains(d.Name) : false;
                    if (!d.Enabled) { panel.Text($"  (disabled) {d.Name}"); continue; }
                    if (exists) present++; else missing++;
                    panel.Text($"{(exists ? "✓" : "·")} {d.Name,-30} {d.Discipline,-5} " +
                               $"{d.Classification,-20} abbr={d.Abbreviation,-6} sys={d.StingSysCode}");
                }

                panel.SetSubtitle(
                    $"{present} present · {missing} to build · " +
                    $"{mechNames.Count} duct + {pipeNames.Count} pipe system types in model");

                if (missing > 0)
                    panel.AddSection("ACTION").Text($"{missing} definition(s) not yet in the model — run MEP_BuildSystemTypes.");
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepInspectSystemTypesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepReloadSystemTypesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                MepSystemTypeRegistry.Reload();
                TaskDialog.Show("STING MEP",
                    "MEP system-type registry reloaded — corporate baseline + project override re-read on next use.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepReloadSystemTypesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
