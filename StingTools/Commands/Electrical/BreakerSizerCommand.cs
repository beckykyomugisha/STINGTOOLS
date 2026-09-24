using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using StingTools.Commands.Electrical.VoltageDrop;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Electrical
{
    public class BreakerProposal
    {
        public ElementId CircuitId { get; set; }
        public string PanelName { get; set; }
        public string CircuitNumber { get; set; }
        public string LoadName { get; set; }
        public double DesignCurrentA { get; set; }
        public int MinBreakerA { get; set; }
        public int ProposedBreakerA { get; set; }
        /// <summary>Iz used for the In ≤ Iz check (A); 0 when the cable size was unknown.</summary>
        public double IzA { get; set; }
        /// <summary>True when the device must NOT be applied: In &gt; Iz (cable unprotected
        /// against overload) or no standard rating is large enough.</summary>
        public bool Blocked { get; set; }
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// Previews the next standard breaker size for every power circuit.
    /// Read-only — never writes to the model. The user reviews the table
    /// and clicks "Apply to Model" (BreakerSizerApplyCommand) to commit.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BreakerSizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var opts = StingElectricalCommandHandler.CurrentBreakerOptions
                       ?? new BreakerOptionsSnapshot { Standard = "BS_MCB", ContinuousFactor = true };

            var proposals = Compute(doc, opts.Standard, opts.ContinuousFactor);
            StingElectricalCommandHandler.LastBreakerProposals = proposals;
            var blocked = proposals.Where(p => p.Blocked).ToList();
            string head = string.Equals(opts.Standard, "NEC", StringComparison.OrdinalIgnoreCase)
                ? "NEC 240.6(A) ratings" + (opts.ContinuousFactor ? ", ×1.25 continuous (210.20(A))" : "")
                : "BS 7671 Reg 433.1.1: Ib ≤ In ≤ Iz (no ×1.25 continuous factor — that is an NEC rule)";
            StingLog.Info($"BreakerSizer: {proposals.Count} proposal(s), {blocked.Count} blocked ({opts.Standard}).");
            TaskDialog.Show("STING Breaker Sizing",
                $"{head}\n\nComputed proposals for {proposals.Count} circuit(s). " +
                $"{blocked.Count} will NOT be applied (device larger than the cable can carry, or no rating large enough):\n" +
                string.Join("\n", blocked.Take(10).Select(b => $"  {b.PanelName}-{b.CircuitNumber}: {b.Note}")) +
                (blocked.Count > 10 ? $"\n  …and {blocked.Count - 10} more" : "") +
                "\n\nClick Apply to commit the rest.");
            return Result.Succeeded;
        }

        public static List<BreakerProposal> Compute(Document doc, string standard, bool continuous)
        {
            var list = new List<BreakerProposal>();
            if (doc == null) return list;
            try
            {
                var systems = new FilteredElementCollector(doc)
                    .OfClass(typeof(ElectricalSystem))
                    .Cast<ElectricalSystem>()
                    .Where(s => { try { return s.SystemType == ElectricalSystemType.PowerCircuit; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return true; } })
                    .ToList();

                bool useNec = string.Equals(standard, "NEC", StringComparison.OrdinalIgnoreCase);
                bool useMccb = string.Equals(standard, "BS_MCCB", StringComparison.OrdinalIgnoreCase);
                int[] ratings = useNec ? VoltageDropEngine.BreakerSizesNEC
                              : useMccb ? VoltageDropEngine.BreakerSizesBSMCCB
                              : VoltageDropEngine.BreakerSizesBSMCB;

                // BS 7671 In ≤ Iz: the circuit does not carry installation method / insulation,
                // so Iz is taken from the cable tab's assumptions (else PVC70 method C) at the
                // tabulated It, i.e. 30 °C and ungrouped — the BEST case. A device that fails
                // even that is certainly too large; one that passes still needs derating checked.
                var cableSnap = StingElectricalCommandHandler.CurrentCableSizeInput;
                string ins = cableSnap?.Insulation ?? "PVC70";
                string method = cableSnap?.InstallMethod ?? "C";
                string mat = cableSnap?.Material ?? "Cu";
                var table = useNec ? null
                    : StingTools.Commands.Electrical.CableSizer.CableSizerEngine.Bs7671Tables().FindTable(mat, ins, method);

                foreach (var sys in systems)
                {
                    try
                    {
                        double iA = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0;
                        if (iA <= 0) continue;
                        int phases = 1;
                        try { phases = sys.PolesNumber >= 3 ? 3 : 1; } catch (Exception ex) { StingLog.Warn($"Breaker poles: {ex.Message}"); }

                        double? iz = null;
                        string izBasis = null;
                        if (!useNec)
                        {
                            string wire = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM)?.AsString() ?? "";
                            double csa = StingTools.Core.Electrical.WireSizeParser.ParseCsaMm2(wire);
                            if (csa > 0 && table != null)
                            {
                                double it = StingTools.Core.Electrical.Bs7671Data.TabulatedIt(table, csa, phases);
                                if (it > 0)
                                {
                                    iz = it;
                                    izBasis = $"Table {table.Id} {mat}/{ins} method {method} It for {csa:0.#} mm², 30 °C, ungrouped";
                                }
                                else izBasis = $"{csa:0.#} mm² not in Table {table.Id}";
                            }
                            else if (csa > 0)
                                izBasis = $"no BS 7671 table for {mat}/{ins} method {method}";
                        }

                        var sel = StingTools.Core.Electrical.ProtectiveDeviceSelection.Select(
                            iA, useNec, continuous, ratings, iz, izBasis);
                        string note = sel.Note;
                        if (!useNec && !iz.HasValue && !string.IsNullOrEmpty(izBasis))
                            note = (string.IsNullOrEmpty(note) ? "" : note + "; ") + "In ≤ Iz not checked: " + izBasis;

                        list.Add(new BreakerProposal
                        {
                            CircuitId = sys.Id,
                            PanelName = sys.PanelName ?? "",
                            CircuitNumber = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "",
                            LoadName = sys.LoadName ?? sys.Name,
                            DesignCurrentA = iA,
                            MinBreakerA = (int)Math.Ceiling(sel.MinimumA),
                            ProposedBreakerA = sel.ProposedA,
                            IzA = iz ?? 0,
                            Blocked = sel.Blocked,
                            Note = note
                        });
                    }
                    catch (Exception ex2) { StingLog.Warn($"Breaker compute: {ex2.Message}"); }
                }
            }
            catch (Exception ex2) { StingLog.Warn($"BreakerSizer.Compute: {ex2.Message}"); }
            return list;
        }
    }

    /// <summary>
    /// Writes the proposed breaker ratings back to each circuit via
    /// RBS_ELEC_CIRCUIT_RATING_PARAM. Wrapped in a single transaction.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BreakerSizerApplyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var proposals = StingElectricalCommandHandler.LastBreakerProposals;
            if (proposals == null || proposals.Count == 0)
            {
                TaskDialog.Show("STING Electrical", "Run Preview first to compute breaker proposals.");
                return Result.Cancelled;
            }

            int updated = 0, skipped = 0, blocked = 0;
            using (var tx = new Transaction(doc, "STING Apply Breaker Sizing"))
            {
                tx.Start();
                foreach (var prop in proposals)
                {
                    try
                    {
                        // In > Iz (or no rating) — applying would leave the cable unprotected.
                        if (prop.Blocked) { blocked++; continue; }
                        var sys = doc.GetElement(prop.CircuitId) as ElectricalSystem;
                        if (sys == null) { skipped++; continue; }
                        var p = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_RATING_PARAM);
                        if (p == null || p.IsReadOnly) { skipped++; continue; }
                        // RBS_ELEC_CIRCUIT_RATING_PARAM is stored in internal current units (amperes).
                        try { p.Set((double)prop.ProposedBreakerA); updated++; }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"BreakerApply set: {ex.Message}");
                            skipped++;
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"BreakerApply: {ex.Message}"); skipped++; }
                }
                tx.Commit();
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            TaskDialog.Show("STING Electrical",
                $"Applied breaker ratings to {updated} circuit(s). Skipped: {skipped}. " +
                $"Not applied (In > Iz or no rating): {blocked}");
            return Result.Succeeded;
        }
    }
}
