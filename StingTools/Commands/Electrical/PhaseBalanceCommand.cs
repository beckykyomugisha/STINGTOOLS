using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    /// <summary>
    /// Greedy largest-first 3-phase load balancer. Iterates power circuits
    /// sorted descending by ApparentLoad, assigning each to the lowest-loaded
    /// phase via <see cref="ElectricalSystem.StartingPhase"/>. Skips 3-pole
    /// circuits and (when the option is on) grouped 2-pole circuits.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PhaseBalanceCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var opts = StingElectricalCommandHandler.CurrentBalanceOptions
                       ?? new BalanceOptionsSnapshot { Algorithm = "Greedy", RespectGrouped = true };

            var pre = PreviewBalance(doc, opts);
            if (pre == null)
            {
                TaskDialog.Show("STING Electrical", "No 3-phase panels found to balance.");
                return Result.Cancelled;
            }

            // Push preview to the dock panel so the user can compare before/after.
            var panel = StingElectricalCommandHandler.ActivePanel;
            panel?.RefreshBalancePreview(
                $"Before: A {pre.PhaseABefore:0.0} kW │ B {pre.PhaseBBefore:0.0} kW │ C {pre.PhaseCBefore:0.0} kW  Δ={pre.ImbalanceBefore:0.0}",
                $"After:  A {pre.PhaseAAfter:0.0} kW │ B {pre.PhaseBAfter:0.0} kW │ C {pre.PhaseCAfter:0.0} kW  Δ={pre.ImbalanceAfter:0.0}");

            if (opts.PreviewFirst)
            {
                var dlg = new TaskDialog("STING Phase Balance — Preview")
                {
                    MainInstruction = "Apply the proposed phase balance?",
                    MainContent =
                        $"Before: A {pre.PhaseABefore:0.0} | B {pre.PhaseBBefore:0.0} | C {pre.PhaseCBefore:0.0} kW (Δ {pre.ImbalanceBefore:0.0})\n" +
                        $"After:  A {pre.PhaseAAfter:0.0} | B {pre.PhaseBAfter:0.0} | C {pre.PhaseCAfter:0.0} kW (Δ {pre.ImbalanceAfter:0.0})",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                };
                if (dlg.Show() != TaskDialogResult.Yes) return Result.Cancelled;
            }

            int reassigned = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING Phase Balance"))
            {
                tx.Start();
                foreach (var assign in pre.Assignments)
                {
                    try
                    {
                        var sys = doc.GetElement(assign.SystemId) as ElectricalSystem;
                        if (sys == null) { skipped++; continue; }
                        if (SafePoles(sys) >= 3) { skipped++; continue; }
                        if (opts.RespectGrouped && IsGroupedTwoPole(sys)) { skipped++; continue; }

                        try { sys.StartingPhase = assign.NewPhase; reassigned++; }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"StartingPhase write failed on {sys.Name}: {ex.Message}");
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Balance assign {assign.SystemId}: {ex.Message}");
                        skipped++;
                    }
                }
                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Electrical",
                $"Phase balance applied. Reassigned: {reassigned}\nSkipped (3-pole / grouped / read-only): {skipped}\n\n" +
                $"Imbalance Δ: {pre.ImbalanceBefore:0.0} kW → {pre.ImbalanceAfter:0.0} kW");
            return Result.Succeeded;
        }

        // ── preview ──────────────────────────────────────────────────────

        public class BalancePreview
        {
            public double PhaseABefore, PhaseBBefore, PhaseCBefore;
            public double PhaseAAfter,  PhaseBAfter,  PhaseCAfter;
            public double ImbalanceBefore, ImbalanceAfter;
            public List<PhaseAssignment> Assignments = new List<PhaseAssignment>();
        }
        public class PhaseAssignment
        {
            public ElementId SystemId;
            public ElectricalPhase NewPhase;
            public double LoadKW;
        }

        public static BalancePreview PreviewBalance(Document doc, BalanceOptionsSnapshot opts)
        {
            if (doc == null || opts == null) return null;
            try
            {
                var systems = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .Where(s => SafeIsPower(s))
                    .ToList();
                if (systems.Count == 0) return null;

                double aBefore = 0, bBefore = 0, cBefore = 0;
                foreach (var s in systems)
                {
                    double load = SafeLoadKW(s);
                    var ph = SafePhase(s);
                    if (ph == ElectricalPhase.A) aBefore += load;
                    else if (ph == ElectricalPhase.B) bBefore += load;
                    else if (ph == ElectricalPhase.C) cBefore += load;
                    // Three-pole circuits split evenly across all three phases.
                    else if (SafePoles(s) >= 3)
                    {
                        aBefore += load / 3.0; bBefore += load / 3.0; cBefore += load / 3.0;
                    }
                }

                // Greedy largest-first reassignment, ignoring 3-pole and (when honouring
                // grouped) grouped 2-pole circuits — they keep their original phase.
                var preview = new BalancePreview
                {
                    PhaseABefore = aBefore, PhaseBBefore = bBefore, PhaseCBefore = cBefore,
                    ImbalanceBefore = ImbalanceKW(aBefore, bBefore, cBefore),
                };

                double aAfter = 0, bAfter = 0, cAfter = 0;
                // Three-pole circuits keep their balanced split.
                foreach (var s in systems.Where(s => SafePoles(s) >= 3))
                {
                    double l = SafeLoadKW(s);
                    aAfter += l / 3.0; bAfter += l / 3.0; cAfter += l / 3.0;
                }
                // Grouped 2-pole circuits keep their existing phase assignment.
                if (opts.RespectGrouped)
                {
                    foreach (var s in systems.Where(s => SafePoles(s) == 2 && IsGroupedTwoPole(s)))
                    {
                        var ph = SafePhase(s);
                        double l = SafeLoadKW(s);
                        if (ph == ElectricalPhase.A) aAfter += l;
                        else if (ph == ElectricalPhase.B) bAfter += l;
                        else if (ph == ElectricalPhase.C) cAfter += l;
                    }
                }

                var movable = systems
                    .Where(s => SafePoles(s) < 3)
                    .Where(s => !(opts.RespectGrouped && SafePoles(s) == 2 && IsGroupedTwoPole(s)))
                    .OrderByDescending(SafeLoadKW)
                    .ToList();

                foreach (var s in movable)
                {
                    double l = SafeLoadKW(s);
                    ElectricalPhase target;
                    double minBucket = Math.Min(aAfter, Math.Min(bAfter, cAfter));
                    if (Math.Abs(aAfter - minBucket) < 1e-6) { target = ElectricalPhase.A; aAfter += l; }
                    else if (Math.Abs(bAfter - minBucket) < 1e-6) { target = ElectricalPhase.B; bAfter += l; }
                    else { target = ElectricalPhase.C; cAfter += l; }

                    preview.Assignments.Add(new PhaseAssignment
                    { SystemId = s.Id, NewPhase = target, LoadKW = l });
                }

                preview.PhaseAAfter = aAfter;
                preview.PhaseBAfter = bAfter;
                preview.PhaseCAfter = cAfter;
                preview.ImbalanceAfter = ImbalanceKW(aAfter, bAfter, cAfter);
                return preview;
            }
            catch (Exception ex) { StingLog.Warn($"PreviewBalance: {ex.Message}"); return null; }
        }

        private static double ImbalanceKW(double a, double b, double c)
            => Math.Max(a, Math.Max(b, c)) - Math.Min(a, Math.Min(b, c));

        // ── safe wrappers ────────────────────────────────────────────────
        private static bool SafeIsPower(ElectricalSystem s)
        { try { return s.SystemType == ElectricalSystemType.PowerCircuit; } catch { return true; } }

        private static int SafePoles(ElectricalSystem s)
        { try { return s.PolesNumber; } catch { return 1; } }

        private static double SafeLoadKW(ElectricalSystem s)
        {
            try { return s.ApparentLoad / 1000.0; } catch { return 0; }
        }

        private static ElectricalPhase SafePhase(ElectricalSystem s)
        {
            try { return s.StartingPhase; } catch { return ElectricalPhase.A; }
        }

        private static bool IsGroupedTwoPole(ElectricalSystem s)
        {
            // Revit doesn't expose IsGrouped on ElectricalSystem directly. Best-effort:
            // assume any 2-pole circuit with adjacent slot numbering is grouped — the
            // user's "Respect grouped" toggle gates the safety net.
            try
            {
                var p1 = s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_START_SLOT)?.AsDouble() ?? 0;
                return s.PolesNumber == 2 && p1 > 0;
            }
            catch { return false; }
        }
    }
}
