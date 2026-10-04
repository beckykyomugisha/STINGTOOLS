// StingTools — MEP Coordination commands (Phase C).
//
//   MEP_ApplyMepCoordination — turn the ACTIVE view into a coordinated MEP drawing:
//     resolve the MEP coordination/plan DrawingType (view template + style pack via
//     DrawingDispatcher), apply its presentation, then overlay the system-classification
//     colour filters so each system reads in its discipline colour. Closes the loop:
//     create systems (A) → stamp classification (B) → coordinated MEP drawing (C).
//
//     params.scope = "produced" (the MEPDrawingProduction preset's last step) colours
//     every STING-produced MEP plan instead: each non-template plan view stamped with
//     an M / E / P / FP / MG Plan or Coordination drawing type. Those views already
//     carry their drawing type's presentation, so only the system colour filters are
//     added. A view whose template controls V/G filters is coloured through that
//     template (once, however many views share it) — Revit refuses filters on the
//     view itself. Re-runs are idempotent (a filter already on a view is re-set, not
//     duplicated). Inside a preset the result goes to the log and the step message.
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

                string scope = (WorkflowEngine.StepParam("scope") ?? "").Trim().ToLowerInvariant();
                if (scope == "produced") return ApplyToProducedViews(doc, ref message);
                if (scope.Length > 0 && scope != "view")
                {
                    message = $"MEP coordination: params.scope '{scope}' is not 'view' or 'produced'.";
                    return Result.Failed;
                }

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
                    // DTW-217: Presentation.Apply may just have given the view a managed
                    // template that controls V/G filters; the filters go where they show.
                    res = MepCoordinationEngine.ApplyThroughHost(doc, view);
                    StingTx.Commit(t);
                }

                var panel = StingResultPanel.Create("MEP — Apply Coordination to View");
                panel.SetSubtitle(
                    $"'{view.Name}' · {res.Applied} system filter(s) applied {res.WhereText} · {res.Unmatched} unmatched" +
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
                PresetDialog.Show(panel, ref message);

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

        /// <summary>
        /// Colour every STING-produced MEP plan view by system. See the file header.
        /// No produced view, or no system in the model, is reported (not an error):
        /// the preset step is optional and a model without MEP systems has nothing to colour.
        /// </summary>
        private static Result ApplyToProducedViews(Document doc, ref string message)
        {
            var mepDisc = new System.Collections.Generic.HashSet<string>(
                HeadlessProductionInputs.MepDisciplines, StringComparer.OrdinalIgnoreCase);

            // Views stamped with an MEP Plan / Coordination drawing type.
            var targets = new System.Collections.Generic.List<(View View, DrawingType Dt)>();
            foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>())
            {
                if (v.IsTemplate) continue;
                string id = DrawingTypeStamper.Read(v);
                if (string.IsNullOrEmpty(id)) continue;
                DrawingType dt = null;
                try { dt = DrawingTypeRegistry.Get(doc, id); }
                catch (Exception ex) { StingLog.Warn($"MEP coord: drawing type '{id}' lookup: {ex.Message}"); }
                if (dt == null) continue;
                bool purposeOk = string.Equals(dt.Purpose, DrawingPurpose.Plan, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(dt.Purpose, DrawingPurpose.Coordination, StringComparison.OrdinalIgnoreCase);
                if (purposeOk && mepDisc.Contains((dt.Discipline ?? "").Trim())) targets.Add((v, dt));
            }

            var panel = StingResultPanel.Create("MEP — Colour Produced Plans by System");
            if (targets.Count == 0)
            {
                panel.SetSubtitle("No STING-produced MEP plan views");
                panel.Text("No plan view carries an M / E / P / FP / MG Plan or Coordination drawing-type stamp. " +
                           "Produce the MEP plans first (MEPDrawingProduction).");
                PresetDialog.Show(panel, ref message);
                if (PresetDialog.Quiet) message = "MEP coordination: no STING-produced MEP plan views to colour.";
                return Result.Succeeded;
            }

            var systems = MepCoordinationEngine.PresentSystems(doc);
            if (systems.Count == 0)
            {
                panel.SetSubtitle($"{targets.Count} produced MEP plan(s) · no systems to colour");
                panel.Text("No duct/pipe member carries a system yet — run MEP_BuildSystems (MEPDrawingSetup).");
                PresetDialog.Show(panel, ref message);
                if (PresetDialog.Quiet) message = $"MEP coordination: {targets.Count} produced MEP plan(s), but no duct/pipe system to colour by.";
                return Result.Succeeded;
            }

            // A view whose template controls V/G filters is coloured through the
            // template, once per template.
            var hosts = new System.Collections.Generic.Dictionary<ElementId, (View Host, int Views)>();
            foreach (var (v, _) in targets)
            {
                View host = MepCoordinationEngine.FilterHost(doc, v);   // DTW-217: one rule for all three paths
                hosts[host.Id] = hosts.TryGetValue(host.Id, out var h) ? (h.Host, h.Views + 1) : (host, 1);
            }

            int ok = 0, partial = 0, failed = 0;
            var lines = new System.Collections.Generic.List<string>();
            var warnings = new System.Collections.Generic.List<string>();
            using (var t = new Transaction(doc, "STING Colour Produced MEP Plans"))
            {
                t.Start();
                foreach (var (host, count) in hosts.Values)
                {
                    MepCoordResult res;
                    try { res = MepCoordinationEngine.ApplyToView(doc, host, systems); }
                    catch (Exception ex)
                    {
                        failed++;
                        warnings.Add($"{host.Name}: {ex.Message}");
                        StingLog.Warn($"MEP coord (produced): '{host.Name}': {ex.Message}");
                        continue;
                    }
                    if (res.Applied == 0) failed++;
                    else if (res.Unmatched > 0) partial++;
                    else ok++;
                    string what = host.IsTemplate ? $"template '{host.Name}' ({count} view(s))" : $"'{host.Name}'";
                    lines.Add($"{(res.Applied > 0 ? "✓" : "·")} {what}: {res.Applied} applied, {res.Unmatched} unmatched");
                    foreach (var w in res.Warnings) warnings.Add($"{host.Name}: {w}");
                }
                var status = t.Commit();
                if (status != TransactionStatus.Committed)
                {
                    message = $"MEP coordination: the transaction ended {status}; no colours were kept.";
                    StingLog.Warn(message);
                    return Result.Failed;
                }
            }

            panel.SetSubtitle($"{targets.Count} produced MEP plan(s) · {hosts.Count} view/template target(s) · " +
                              $"{ok} fully coloured · {partial} partly · {failed} not coloured");
            panel.AddSection("TARGETS");
            foreach (var l in lines) panel.Text(l);
            if (warnings.Count > 0)
            {
                panel.AddSection($"WARNINGS ({warnings.Count})");
                foreach (var w in warnings.Take(40)) panel.Text(w);
            }
            PresetDialog.Show(panel, ref message);
            string summary = $"MEP coordination: coloured {ok + partial} of {hosts.Count} target(s) " +
                             $"covering {targets.Count} produced MEP plan(s); {failed} not coloured.";
            if (PresetDialog.Quiet || ok + partial == 0) message = summary;
            StingLog.Info(summary);
            // Every target failing is a failed step, not a quiet success.
            return (ok + partial) == 0 ? Result.Failed : Result.Succeeded;
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
