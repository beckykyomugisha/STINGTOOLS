// StingTools — MEP Coordination commands (Phase C).
//
//   MEP_ApplyMepCoordination — turn the ACTIVE view into a coordinated MEP drawing:
//     resolve the MEP coordination/plan DrawingType (view template + style pack via
//     DrawingDispatcher), apply its presentation, then overlay the system-classification
//     colour filters so each system reads in its discipline colour. Closes the loop:
//     create systems (A) → stamp classification (B) → coordinated MEP drawing (C).
//
//   MEP_InspectMepCoordination — read-only dry run: which classifications are present,
//     which AEC filter each resolves to, and which DrawingType would be applied.

using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepApplyMepCoordinationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                var view = doc.ActiveView;
                if (view == null) { message = "No active view."; return Result.Failed; }

                DrawingType dt = ResolveMepDrawingType(doc);

                MepCoordResult res;
                using (var t = new Transaction(doc, "STING Apply MEP Coordination"))
                {
                    t.Start();
                    if (dt != null)
                    {
                        try { DrawingTypePresentation.Apply(doc, view, dt, runAnnotation: false); }
                        catch (Exception ex) { StingLog.Warn($"MEP coord: presentation apply: {ex.Message}"); }
                    }
                    res = MepCoordinationEngine.ApplyToView(doc, view);
                    t.Commit();
                }

                var panel = StingResultPanel.Create("MEP — Apply Coordination to View");
                panel.SetSubtitle(
                    $"'{view.Name}' · {res.Applied} system filter(s) applied · {res.Unmatched} unmatched" +
                    (dt != null ? $" · DrawingType '{dt.Id}'" : " · no DrawingType matched"));

                panel.AddSection("DRAWING TYPE");
                panel.Text(dt != null
                    ? $"{dt.Id}  ({dt.Discipline}/{dt.Purpose}) — template + style pack applied"
                    : "No MEP coordination/plan DrawingType resolved — filters applied without a template.");

                panel.AddSection("SYSTEM CLASSIFICATIONS");
                foreach (var r in res.Rows)
                    panel.Text($"{(r.Applied ? "✓" : "·")} {r.Classification,-22} {r.FilterName,-30} {r.Note}");

                if (res.Warnings.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({res.Warnings.Count})");
                    foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                }
                panel.Show();

                StingLog.Info($"MEP coordination: view='{view.Name}' applied={res.Applied} unmatched={res.Unmatched} dt={dt?.Id}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepApplyMepCoordinationCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        internal static DrawingType ResolveMepDrawingType(Document doc)
        {
            try
            {
                return DrawingDispatcher.Resolve(doc, "M", "*", "Coordination")
                    ?? DrawingDispatcher.Resolve(doc, "*", "*", "MEP")
                    ?? DrawingDispatcher.CandidatesForDiscipline(doc, "M")
                        .FirstOrDefault(d => d.Purpose == DrawingPurpose.Coordination
                                          || d.Purpose == DrawingPurpose.Plan);
            }
            catch (Exception ex) { StingLog.Warn($"MEP coord: DrawingType resolve: {ex.Message}"); return null; }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepInspectMepCoordinationCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var present = MepCoordinationEngine.PresentClassifications(doc);
                var defs = AecFilterRegistry.ListAll(doc);
                var dt = MepApplyMepCoordinationCommand.ResolveMepDrawingType(doc);

                var panel = StingResultPanel.Create("MEP — Coordination Inspect (dry run)");
                panel.SetSubtitle($"{present.Count} system classification(s) present · " +
                                  (dt != null ? $"DrawingType '{dt.Id}'" : "no DrawingType matched"));

                panel.AddSection("CLASSIFICATION → FILTER");
                if (present.Count == 0)
                    panel.Text("No duct/pipe members carry a classification yet — run MEP_BuildSystems (Phase B).");
                foreach (var cls in present)
                {
                    var def = defs.FirstOrDefault(d => RuleHas(d?.Rule, cls));
                    panel.Text($"{(def != null ? "✓" : "·")} {cls,-22} {(def != null ? def.Name : "(no matching AEC filter)")}");
                }

                panel.AddSection("DRAWING TYPE");
                panel.Text(dt != null
                    ? $"{dt.Id}  ({dt.Discipline}/{dt.Purpose})  pack='{dt.ViewStylePackId}'"
                    : "No MEP coordination/plan DrawingType resolved.");
                panel.Text("Run MEP_ApplyMepCoordination on the target view to apply the template + colours.");
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepInspectMepCoordinationCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static bool RuleHas(AecFilterRule rule, string value)
        {
            if (rule == null) return false;
            if (rule.IsLeaf)
                return string.Equals(rule.Param, "RBS_SYSTEM_CLASSIFICATION_PARAM", StringComparison.OrdinalIgnoreCase)
                    && string.Equals((rule.Value ?? "").Trim(), value, StringComparison.OrdinalIgnoreCase);
            if (rule.Rules != null)
                foreach (var c in rule.Rules) if (RuleHas(c, value)) return true;
            return false;
        }
    }
}
