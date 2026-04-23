// StingTools v4 MVP — HardyCrossCommand.
//
// Takes the current pipe selection (or all pipes in the active view),
// extracts the network topology via NetworkExtractor, runs
// HardyCrossSolver to convergence, and reports the balanced flow
// per pipe plus convergence diagnostics. Optionally writes the
// solved flow back to each pipe's RBS_PIPE_FLOW_PARAM so downstream
// DuctFrictionSolver / velocity checks operate on balanced values
// rather than the designer's initial guess.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Calc;
using StingTools.UI;

namespace StingTools.Commands.Routing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HardyCrossCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // Scope: selection first, fall back to active view.
            var pipes = new List<Pipe>();
            var sel = uidoc.Selection.GetElementIds();
            if (sel != null && sel.Count > 0)
            {
                foreach (var id in sel)
                    if (doc.GetElement(id) is Pipe p) pipes.Add(p);
            }
            if (pipes.Count == 0 && doc.ActiveView != null)
            {
                var col = new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfClass(typeof(Pipe));
                foreach (var el in col) if (el is Pipe p) pipes.Add(p);
            }
            if (pipes.Count == 0)
            {
                TaskDialog.Show("STING v4 — Hardy Cross",
                    "Select pipes or switch to a view showing a looped pipe network.");
                return Result.Cancelled;
            }

            NetworkExtraction net;
            try { net = NetworkExtractor.Extract(doc, pipes); }
            catch (Exception ex)
            {
                StingLog.Error("HardyCrossCommand: extraction failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            if (net.Loops.Count == 0)
            {
                var panel = StingResultPanel.Create("v4 Hardy Cross");
                panel.SetSubtitle("No closed loops detected");
                panel.AddSection("DIAGNOSTICS")
                     .Metric("Pipes extracted", net.Pipes.Count.ToString())
                     .Metric("Loops found",     "0");
                panel.Text("Hardy Cross only applies to looped networks. A dead-end tree " +
                           "has a unique flow distribution determined by fixture demand and " +
                           "does not need balancing.");
                if (net.Warnings.Count > 0)
                {
                    panel.AddSection("WARNINGS");
                    foreach (var w in net.Warnings.Take(20)) panel.Text(w);
                }
                panel.Show();
                return Result.Succeeded;
            }

            // Seed initial flows: any pipe with 0 assumed flow gets a
            // small positive guess so the first head-loss calc is
            // non-trivial. The sign of the guess is irrelevant —
            // iteration corrects it.
            foreach (var np in net.Pipes)
                if (Math.Abs(np.FlowM3S) < 1e-9) np.FlowM3S = 1e-3;

            var td = new TaskDialog("STING v4 — Hardy Cross")
            {
                MainInstruction = "Preview or apply balanced flows?",
                MainContent =
                    $"{net.Pipes.Count} pipe(s), {net.Loops.Count} loop(s) detected.\n\n" +
                    "PREVIEW: run solver, show balanced Q per pipe, do not write back.\n" +
                    "APPLY:   also write the solved Q to RBS_PIPE_FLOW_PARAM.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Preview (dry run)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Apply flows");
            var choice = td.Show();
            if (choice != TaskDialogResult.CommandLink1 && choice != TaskDialogResult.CommandLink2)
                return Result.Cancelled;
            bool apply = choice == TaskDialogResult.CommandLink2;

            HardyCrossResult hcRes;
            try { hcRes = HardyCrossSolver.Solve(net.Pipes, net.Loops, NetworkFluid.Water); }
            catch (Exception ex)
            {
                StingLog.Error("HardyCrossCommand: solver failed", ex);
                message = ex.Message;
                return Result.Failed;
            }

            int written = 0;
            if (apply && hcRes.Converged)
            {
                using (var tx = new Transaction(doc, "STING v4 Hardy Cross write-back"))
                {
                    try
                    {
                        tx.Start();
                        foreach (var np in net.Pipes)
                        {
                            if (!net.PipeIdByNetworkId.TryGetValue(np.Id, out var pid)) continue;
                            var pipeEl = doc.GetElement(pid) as Pipe;
                            if (pipeEl == null) continue;
                            try
                            {
                                var p = pipeEl.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);
                                if (p != null && !p.IsReadOnly)
                                {
                                    // ft³/s internal unit
                                    p.Set(np.FlowM3S / 0.028316846592);
                                    written++;
                                }
                            }
                            catch (Exception ex)
                            { StingLog.Warn($"Hardy Cross write-back {pipeEl.Id}: {ex.Message}"); }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                        StingLog.Error("HardyCrossCommand write-back", ex);
                        message = ex.Message;
                        return Result.Failed;
                    }
                }
            }

            ShowResult(net, hcRes, apply, written);
            return Result.Succeeded;
        }

        private void ShowResult(NetworkExtraction net, HardyCrossResult hc, bool applied, int written)
        {
            var panel = StingResultPanel.Create("v4 Hardy Cross (Water Network)");
            panel.SetSubtitle(hc.Converged
                ? $"Converged in {hc.Iterations} iter"
                : $"DID NOT CONVERGE after {hc.Iterations} iter");

            panel.AddSection("TOPOLOGY")
                 .Metric("Pipes",    net.Pipes.Count.ToString())
                 .Metric("Loops",    net.Loops.Count.ToString())
                 .Metric("Applied",  applied ? $"{written} pipes" : "no");

            panel.AddSection("CONVERGENCE")
                 .Metric("Iterations",  hc.Iterations.ToString())
                 .Metric("Max |ΔQ|/|Q|", hc.MaxDeltaQ.ToString("E3"));

            if (hc.IterationLog.Count > 0)
            {
                panel.AddSection("ITERATION LOG");
                foreach (var line in hc.IterationLog) panel.Text(line);
            }

            panel.AddSection("SOLVED FLOWS (first 60)");
            foreach (var p in net.Pipes.Take(60))
                panel.Text($"{p.Id} {p.NodeA}→{p.NodeB}  Ø{p.DiameterM*1000:F0}  L={p.LengthM:F2}m  Q={p.FlowM3S*1000:F3} l/s");

            if (net.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in net.Warnings.Take(20)) panel.Text(w);
            }
            panel.Show();
        }
    }
}
