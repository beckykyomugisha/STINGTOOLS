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
    /// Greedy largest-first 3-phase load balancer.
    ///
    /// API limit: <c>ElectricalSystem.StartingPhase</c> is not exposed as a
    /// writable property in this Revit version, and the
    /// "Phase" display-name parameter is read-only on most installations
    /// because phase comes from the panel slot. The command therefore
    /// runs in two modes:
    ///   • Preview always — pushes before/after phase totals to the dock panel.
    ///   • Best-effort apply — attempts the parameter write inside try/catch
    ///     and reports how many circuits could not be reassigned.
    /// To physically reassign phases, users move circuits to the appropriate
    /// slot column in the panel schedule (left = A, middle = B, right = C
    /// for the standard 3-phase format).
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
                        $"After:  A {pre.PhaseAAfter:0.0} | B {pre.PhaseBAfter:0.0} | C {pre.PhaseCAfter:0.0} kW (Δ {pre.ImbalanceAfter:0.0})\n\n" +
                        "Note: phase parameter is often read-only — circuits that cannot be written are reported in the result.",
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

                        bool ok = false;
                        try
                        {
                            var phaseParam = sys.LookupParameter("Phase")
                                          ?? sys.LookupParameter("Circuit Phase")
                                          ?? sys.LookupParameter("Starting Phase");
                            if (phaseParam != null && !phaseParam.IsReadOnly)
                            {
                                int v = PhaseToInt(assign.NewPhase);
                                if (phaseParam.StorageType == StorageType.Integer) { phaseParam.Set(v); ok = true; }
                                else if (phaseParam.StorageType == StorageType.String) { phaseParam.Set(assign.NewPhase ?? "A"); ok = true; }
                                if (ok) reassigned++;
                            }
                        }
                        catch (Exception ex) { StingLog.Info($"Phase write soft-fail on {sys.Name}: {ex.Message}"); }
                        if (!ok) skipped++;
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"Balance assign {assign.SystemId}: {ex.Message}");
                        skipped++;
                    }
                }
                tx.Commit();
            }

            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
            /// <summary>"A", "B" or "C".</summary>
            public string NewPhase;
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
                    string ph = SafePhase(s);
                    if (ph == "A") aBefore += load;
                    else if (ph == "B") bBefore += load;
                    else if (ph == "C") cBefore += load;
                    else if (SafePoles(s) >= 3)
                    {
                        aBefore += load / 3.0; bBefore += load / 3.0; cBefore += load / 3.0;
                    }
                }

                var preview = new BalancePreview
                {
                    PhaseABefore = aBefore, PhaseBBefore = bBefore, PhaseCBefore = cBefore,
                    ImbalanceBefore = ImbalanceKW(aBefore, bBefore, cBefore),
                };

                double aAfter = 0, bAfter = 0, cAfter = 0;
                foreach (var s in systems.Where(s => SafePoles(s) >= 3))
                {
                    double l = SafeLoadKW(s);
                    aAfter += l / 3.0; bAfter += l / 3.0; cAfter += l / 3.0;
                }
                if (opts.RespectGrouped)
                {
                    foreach (var s in systems.Where(s => SafePoles(s) == 2 && IsGroupedTwoPole(s)))
                    {
                        string ph = SafePhase(s);
                        double l = SafeLoadKW(s);
                        if (ph == "A") aAfter += l;
                        else if (ph == "B") bAfter += l;
                        else if (ph == "C") cAfter += l;
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
                    string target;
                    double minBucket = Math.Min(aAfter, Math.Min(bAfter, cAfter));
                    if (Math.Abs(aAfter - minBucket) < 1e-6) { target = "A"; aAfter += l; }
                    else if (Math.Abs(bAfter - minBucket) < 1e-6) { target = "B"; bAfter += l; }
                    else { target = "C"; cAfter += l; }

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

        private static int PhaseToInt(string phase) => phase switch
        {
            "A" => 0, "B" => 1, "C" => 2, _ => 0
        };

        private static double ImbalanceKW(double a, double b, double c)
            => Math.Max(a, Math.Max(b, c)) - Math.Min(a, Math.Min(b, c));

        // ── safe wrappers ────────────────────────────────────────────────
        private static bool SafeIsPower(ElectricalSystem s)
        { try { return s.SystemType == ElectricalSystemType.PowerCircuit; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return true; } }

        private static int SafePoles(ElectricalSystem s)
        { try { return s.PolesNumber; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return 1; } }

        private static double SafeLoadKW(ElectricalSystem s)
        {
            try { return s.ApparentLoad / 1000.0; } catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); return 0; }
        }

        /// <summary>
        /// Read the circuit's current phase via the "Phase" display-name
        /// parameter (0/A, 1/B, 2/C). The BIP enum constant for this varies
        /// between Revit versions, so we fall back through several names.
        /// </summary>
        private static string SafePhase(ElectricalSystem s)
        {
            try
            {
                var p = s.LookupParameter("Phase")
                     ?? s.LookupParameter("Circuit Phase")
                     ?? s.LookupParameter("Starting Phase");
                if (p == null) return "A";
                if (p.StorageType == StorageType.Integer)
                {
                    int v = p.AsInteger();
                    return v switch { 1 => "B", 2 => "C", _ => "A" };
                }
                if (p.StorageType == StorageType.String)
                {
                    string v = (p.AsString() ?? "").Trim().ToUpperInvariant();
                    if (v.StartsWith("B")) return "B";
                    if (v.StartsWith("C")) return "C";
                    return "A";
                }
            }
            catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); }
            return "A";
        }

        private static bool IsGroupedTwoPole(ElectricalSystem s)
        {
            try
            {
                var p1 = s.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_START_SLOT)?.AsDouble() ?? 0;
                return s.PolesNumber == 2 && p1 > 0;
            }
            catch (Exception ex2) { StingLog.Warn($"Suppressed: {ex2.Message}"); return false; }
        }
    }
}
