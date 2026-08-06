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
                            catch (Exception ex2)
                            { StingLog.Warn($"Hardy Cross write-back {pipeEl.Id}: {ex2.Message}"); }
                        }
                        tx.Commit();
                    }
                    catch (Exception ex2)
                    {
                        if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                        StingLog.Error("HardyCrossCommand write-back", ex2);
                        message = ex2.Message;
                        return Result.Failed;
                    }
                }
            }

            // Attempt a pump duty point when a pump curve is resolvable.
            // No STING data source defines pump curves yet, so this normally
            // yields (0,0) and the report says the intersection is pending a
            // data source. When a curve is present it intersects the solved
            // system-resistance curve along the longest tree path.
            double opFlow = 0, opHead = 0;
            try
            {
                var pump = ResolvePumpCurve(doc);
                if (pump != null && net.Pipes.Count > 0)
                {
                    var seriesPath = net.Pipes
                        .OrderByDescending(p => p.LengthM)
                        .Take(Math.Max(1, net.Pipes.Count))
                        .ToList();
                    var op = HardyCrossSolver.OperatingPoint(seriesPath, pump, NetworkFluid.Water);
                    opFlow = op.PumpOpFlowM3S;
                    opHead = op.PumpOpHeadM;
                }
            }
            catch (Exception ex) { StingLog.Warn($"Pump duty point: {ex.Message}"); }

            ShowResult(net, hcRes, apply, written, opFlow, opHead);
            return Result.Succeeded;
        }

        /// <summary>
        /// Resolve a pump head-curve for the duty-point intersection. STING
        /// has no dedicated pump-curve data file yet; this reads three
        /// optional ProjectInformation params — PRJ_PUMP_SHUTOFF_QH,
        /// PRJ_PUMP_BEP_QH, PRJ_PUMP_RUNOUT_QH (each "Q_lps,H_m") — so a
        /// project team can supply a curve without a code change. Returns
        /// null (and the report says so) when none are present.
        /// </summary>
        private static PumpCurve ResolvePumpCurve(Document doc)
        {
            try
            {
                var pi = doc?.ProjectInformation;
                if (pi == null) return null;
                if (!TryReadQh(pi, "PRJ_PUMP_SHUTOFF_QH", out var shut)) return null;
                if (!TryReadQh(pi, "PRJ_PUMP_BEP_QH",     out var bep))  return null;
                if (!TryReadQh(pi, "PRJ_PUMP_RUNOUT_QH",  out var run))  return null;
                return PumpCurve.FromQuadraticThreePoints(shut, bep, run);
            }
            catch (Exception ex) { StingLog.Warn($"ResolvePumpCurve: {ex.Message}"); return null; }
        }

        private static bool TryReadQh(Element el, string param, out (double q, double h) qh)
        {
            qh = (0, 0);
            try
            {
                var s = el.LookupParameter(param)?.AsString();
                if (string.IsNullOrWhiteSpace(s)) return false;
                var parts = s.Split(',', ';');
                if (parts.Length < 2) return false;
                if (!double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double qLps)) return false;
                if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double hM)) return false;
                qh = (qLps * 1e-3, hM);   // L/s → m³/s, head in m
                return true;
            }
            catch { return false; }
        }

        private void ShowResult(NetworkExtraction net, HardyCrossResult hc, bool applied, int written,
            double opFlow, double opHead)
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

            // ── Control-valve / PICV authority (Tier-3 3.2) ──────────────
            var valved = net.Pipes.Where(p => p.ValveKvs > 0 || p.PicvQMaxLs > 0).ToList();
            if (valved.Count > 0)
            {
                panel.AddSection("CONTROL VALVES / PICV AUTHORITY")
                     .Metric("Valved branches", valved.Count.ToString());
                foreach (var p in valved.Take(60))
                {
                    string picv = p.PicvQMaxLs > 0
                        ? (p.PicvInWindow ? " · PICV IN window" : " · PICV OUT of window")
                        : "";
                    panel.Text($"{p.Id} {p.ValveLabel}: ΔP {p.ValveDpKpa:F1} kPa · " +
                               $"authority β={p.ValveAuthority:F2}{picv}" +
                               (p.ValveAuthority > 0 && p.ValveAuthority < 0.25
                                   ? "  ⚠ low authority (<0.25 — poor control)" : ""));
                }
            }
            else
            {
                panel.AddSection("CONTROL VALVES / PICV AUTHORITY");
                panel.Text("No control valves / PICVs resolved on this network. Valve " +
                           "authority is folded into the balance only when a pipe accessory " +
                           "matches a (brand:code) entry in STING_MEP_SIZING_RULES.json " +
                           "(valveCv / picvCurves) — behaviour is otherwise unchanged.");
            }

            // ── Pump duty point (Tier-3 3.2) ─────────────────────────────
            // OperatingPoint intersects the system-resistance curve with a
            // pump head-curve. STING carries no pump-curve data source yet
            // (neither the model nor STING_MEP_SIZING_RULES.json define one),
            // so this reports the duty point only when a curve was resolvable.
            panel.AddSection("PUMP DUTY POINT");
            if (opFlow > 0)
            {
                panel.Metric("Duty flow", $"{opFlow*1000:F2} l/s")
                     .Metric("Duty head", $"{opHead:F2} m");
            }
            else
            {
                panel.Text("Not computed — no pump head-curve is available. Provide a " +
                           "pump curve (shut-off / BEP / run-out (Q,H) points) via a future " +
                           "STING_PUMP_CURVES.json or ProjectInformation params to intersect " +
                           "the solved system-resistance curve and report the real duty point. " +
                           "The intersection solver (HardyCrossSolver.OperatingPoint) is wired " +
                           "and ready; only the curve data source is pending.");
            }

            if (net.Warnings.Count > 0)
            {
                panel.AddSection("WARNINGS");
                foreach (var w in net.Warnings.Take(20)) panel.Text(w);
            }
            panel.Show();
        }
    }
}
