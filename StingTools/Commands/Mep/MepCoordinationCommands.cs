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

                panel.AddSection("SYSTEMS → FILTER");
                foreach (var r in res.Rows)
                    panel.Text($"{(r.Applied ? "✓" : "·")} {Label(r),-26} [{r.Source,-13}] {r.FilterName,-28} {r.Note}");

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

        private static string Label(MepCoordRow r)
            => string.IsNullOrEmpty(r.Abbreviation) ? r.Classification : $"{r.Abbreviation} ({r.Classification})";

        internal static DrawingType ResolveMepDrawingType(Document doc)
        {
            try
            {
                // "COORD" is the docType the corporate routing table uses for
                // mep-coord-A1-1to50; try it first so the rule resolves deterministically,
                // then the longer aliases, then a purpose-based candidate scan.
                return DrawingDispatcher.Resolve(doc, "M", "*", "COORD")
                    ?? DrawingDispatcher.Resolve(doc, "M", "*", "Coordination")
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

                // Same resolution the Apply command uses (no writes) — keeps the
                // dry run and the real run perfectly consistent.
                var plan = MepCoordinationEngine.BuildPlan(doc);
                var dt = MepApplyMepCoordinationCommand.ResolveMepDrawingType(doc);

                var panel = StingResultPanel.Create("MEP — Coordination Inspect (dry run)");
                int resolvable = plan.Count(r => r.Source != "none");
                panel.SetSubtitle($"{plan.Count} system(s) present · {resolvable} resolvable · " +
                                  (dt != null ? $"DrawingType '{dt.Id}'" : "no DrawingType matched"));

                panel.AddSection("SYSTEM → FILTER (source)");
                if (plan.Count == 0)
                    panel.Text("No duct/pipe members carry a system yet — run MEP_BuildSystems (Phase B).");
                foreach (var r in plan)
                {
                    string label = string.IsNullOrEmpty(r.Abbreviation) ? r.Classification : $"{r.Abbreviation} ({r.Classification})";
                    bool ok = r.Source != "none";
                    string filter = ok ? r.FilterName : "(no filter & no Phase A colour)";
                    panel.Text($"{(ok ? "✓" : "·")} {label,-26} [{r.Source,-13}] {filter}");
                }

                panel.AddSection("LEGEND");
                panel.Text("abbreviation = distinguishes services that share a classification (CHWF vs LTHWF) · " +
                           "classification = corporate STING_AEC_FILTERS.json · synthesised = auto-authored from Phase A colour.");

                panel.AddSection("DRAWING TYPE");
                panel.Text(dt != null
                    ? $"{dt.Id}  ({dt.Discipline}/{dt.Purpose})  pack='{dt.ViewStylePackId}'"
                    : "No MEP coordination/plan DrawingType resolved.");
                panel.Text("Generate the abbreviation filters with MEP_GenerateSystemFilters, then " +
                           "MEP_ApplyMepCoordination on the target view to apply the template + colours.");
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
    }
}
