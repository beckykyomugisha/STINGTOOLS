// StingTools — Phase F commands: per-level view fan-out + auto sheet-placement,
// and opt-in electrical circuit auto-grouping.
//
//   MEP_ProduceMepViewsByLevel — each modelled MEP discipline's routed PLAN drawing
//     type (M / E / P / FP / MG), produced through DrawingProducer on every level
//     that discipline occupies: stamped views and sheets, re-runs reuse them.
//   MEP_AutoGroupCircuits      — FIRST-PASS: group uncircuited electrical devices
//     by nearest panel into power circuits (engineer reviews / rebalances after).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.Core.Drawing;
using StingTools.Commands.Drawing;
using StingTools.UI;

namespace StingTools.Commands.Mep
{
    /// <summary>
    /// HVAC → SYS → "Per-level + sheets". For each MEP discipline modelled in the project
    /// (M, E, P, FP, MG) the drawing type its PLAN (and RCP) routes to is produced, through
    /// DrawingProducer, on every level where that discipline has something modelled — the
    /// same producer, stamps, title blocks, numbering and reuse as DOCS → Produce Per Level.
    /// A re-run reuses the views and sheets it made. Inside a workflow preset
    /// params.drawingTypes / levels / output / duplicateOption / packageId are read as for
    /// DrawingTypes_ProducePerLevel and the summary goes to the step message, not a dialog.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepProduceMepViewsByLevelCommand : IExternalCommand
    {
        private const string Title = "MEP — Per-Level Views + Sheets";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }
                bool headless = WorkflowEngine.IsRunningPreset;

                var presence = MepLevelViewProducer.LevelsByDiscipline(doc);
                var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).ToList();

                // Which types, and for which disciplines.
                List<DrawingType> types;
                Func<DrawingType, List<string>> discsFor;
                var notes = new List<string>();
                var requested = HeadlessProductionInputs.ParseList(headless ? WorkflowEngine.StepParam("drawingTypes") : "");
                if (requested.Count > 0)
                {
                    types = HeadlessProductionInputs.SelectTypes(DrawingTypeRegistry.ListAll(doc), requested,
                        new string[0], out var unknown);
                    if (unknown.Count > 0)
                    { message = $"{Title}: params.drawingTypes names type(s) not in the catalogue: {string.Join(", ", unknown)}."; return Result.Failed; }
                    discsFor = dt => new List<string> { (dt.Discipline ?? "").Trim() };
                }
                else
                {
                    var present = MepLevelViewProducer.Disciplines.Where(presence.ContainsKey).ToList();
                    var routing = BatchProduceCommons.RoutePerLevel(doc, present);
                    types = routing.Types;
                    discsFor = dt => routing.DisciplinesFor(dt.Id);
                    foreach (var p in routing.Picks) notes.Add($"{p.Discipline} / {p.DocType} → {p.Type.Id}");
                    foreach (var d in routing.Unrouted) notes.Add($"{d}: no drawing type routes from {d} / PLAN — not produced.");
                    foreach (var n in routing.NotPerLevel) notes.Add($"{n} is not a per-level plan — not produced.");
                    foreach (var d in MepLevelViewProducer.Disciplines.Where(d => !presence.ContainsKey(d)))
                        notes.Add($"{d}: nothing modelled — skipped.");
                }
                if (types.Count == 0)
                {
                    message = $"{Title}: nothing to produce — " + (notes.Count > 0 ? string.Join(" ", notes) : "no MEP is modelled.");
                    if (!headless) TaskDialog.Show("STING", message);
                    return headless ? Result.Failed : Result.Succeeded;
                }

                // Levels: every level a type's discipline occupies (params.levels narrows it).
                if (headless)
                {
                    var names = HeadlessProductionInputs.SelectNames(levels.Select(l => l.Name).ToList(),
                        HeadlessProductionInputs.ParseList(WorkflowEngine.StepParam("levels")), out var unknownLevels);
                    if (unknownLevels.Count > 0)
                    { message = $"{Title}: params.levels names level(s) not in the model: {string.Join(", ", unknownLevels)}."; return Result.Failed; }
                    levels = levels.Where(l => names.Contains(l.Name)).ToList();
                }
                bool Include(DrawingType dt, Level lvl)
                {
                    foreach (var d in discsFor(dt))
                        if (!presence.TryGetValue(d, out var set) || set.Contains(lvl.Id)) return true;
                    return false;
                }

                ProduceOptions opts;
                string packageId = null;
                if (headless)
                {
                    if (!BatchProduceCommons.TryStepOptions(out opts, out packageId, out var err))
                    { message = $"{Title}: {err}"; return Result.Failed; }
                }
                else
                    opts = new ProduceOptions { CreateSheet = true, PlaceOnSheet = true, RunAnnotation = true, Idempotent = true };

                int views = 0, sheets = 0; var warnings = new List<string>();
                DrawingTypePresentation.Prewarm(doc);
                using (DrawingProducer.PrimeBatchScope(doc))
                    ProduceViewsPerLevelCommand.Produce(doc, types, levels, opts, packageId,
                        ref views, ref sheets, warnings, Include);

                foreach (var n in notes) StingLog.Info($"{Title}: {n}");
                string summary = BatchProduceCommons.StepSummary(Title, views, sheets, warnings);
                if (headless)
                {
                    message = summary;
                    return views > 0 ? Result.Succeeded : Result.Failed;
                }

                var panel = StingResultPanel.Create(Title);
                panel.SetSubtitle($"{views} view(s) · {sheets} new sheet(s) — stamped drawing types, re-runs reuse them");
                panel.AddSection("DRAWING TYPES");
                foreach (var n in notes) panel.Text(n);
                var distinct = warnings.Distinct().ToList();
                if (distinct.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({distinct.Count})");
                    foreach (var w in distinct.Take(40)) panel.Text(w);
                }
                panel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepProduceMepViewsByLevelCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MepAutoGroupCircuitsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                const int maxPerCircuit = 8;     // sensible default; engineer rebalances after
                const double maxDistM = 30.0;

                CircuitGroupResult res;
                using (var t = new Transaction(doc, "STING Auto-Group Circuits"))
                {
                    t.Start();
                    res = MepCircuitBuilder.AutoGroup(doc, maxPerCircuit, maxDistM);
                    t.Commit();
                }

                var panel = StingResultPanel.Create("MEP — Auto-Group Circuits (first pass)");
                panel.SetSubtitle($"{res.Created} circuit(s) created across {res.Groups} panel(s) · " +
                                  $"{res.Unreachable} device(s) out of range");

                panel.AddSection("⚠ REVIEW REQUIRED")
                     .Text($"First pass only: ≤{maxPerCircuit} devices per circuit, nearest panel within {maxDistM:F0} m, " +
                           "same level preferred. No load balancing or phase allocation — review and rebalance " +
                           "in the panel schedules before issue.");

                panel.AddSection("CIRCUITS CREATED");
                foreach (var r in res.Rows.Take(80)) panel.Text(r);
                if (res.Created == 0)
                    panel.Text("Nothing created — all electrical devices are already circuited, or no panels/devices found.");

                if (res.Warnings.Count > 0)
                {
                    panel.AddSection($"WARNINGS ({res.Warnings.Count})");
                    foreach (var w in res.Warnings.Take(40)) panel.Text(w);
                }
                panel.Show();

                StingLog.Info($"MEP auto-group circuits: created={res.Created} groups={res.Groups} unreachable={res.Unreachable}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MepAutoGroupCircuitsCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
