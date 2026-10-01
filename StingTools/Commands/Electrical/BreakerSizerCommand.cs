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
        /// <summary>NEC: applied under the 240.4(B) next-size-up allowance, whose
        /// receptacle-circuit condition the engineer must confirm (DSCH-30).</summary>
        public bool NeedsConfirmation { get; set; }
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// Previews the next standard breaker size for every power circuit.
    /// Read-only — never writes to the model. The user reviews the table
    /// and clicks "Apply to Model" (BreakerSizerApplyCommand) to commit.
    /// </summary>
    // Workflow preset (Calc_SizeBreakers): no dialog; the summary goes to the step message.
    // Step params (ElectricalStepInputs.BreakerOptions): standard (BS_MCB | BS_MCCB | NEC),
    // continuous (true | false). Defaults: the Electrical panel's BREAKER SIZING expander
    // (re-read at the step), else BS_MCB, continuous on (NEC-only factor). The In ≤ Iz cable
    // assumptions come from the panel's CABLE tab, else PVC70 multicore method C, Cu.
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class BreakerSizerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            var fallback = new BreakerOptionsSnapshot { Standard = "BS_MCB", ContinuousFactor = true };
            BreakerOptionsSnapshot opts;
            if (WorkflowEngine.IsRunningPreset)
            {
                if (!ElectricalStepInputs.BreakerOptions(fallback, out opts, out var err))
                { message = "Breaker sizing: " + err; return Result.Failed; }
            }
            else opts = StingElectricalCommandHandler.CurrentBreakerOptions ?? fallback;

            var proposals = Compute(doc, opts.Standard, opts.ContinuousFactor);
            StingElectricalCommandHandler.LastBreakerProposals = proposals;
            var blocked = proposals.Where(p => p.Blocked).ToList();
            var confirm = proposals.Where(p => !p.Blocked && p.NeedsConfirmation).ToList();
            string head = StingTools.Standards.ElectricalStandardId.IsNec(opts.Standard)
                ? "NEC 240.6(A) ratings" + (opts.ContinuousFactor ? ", ×1.25 continuous (210.20(A))" : "")
                : "BS 7671 Reg 433.1.1: Ib ≤ In ≤ Iz (no ×1.25 continuous factor — that is an NEC rule)";
            StingLog.Info($"BreakerSizer: {proposals.Count} proposal(s), {blocked.Count} blocked ({opts.Standard}).");
            string loadError = VoltageDropEngine.BreakerSizesLoadError;
            if (loadError != null) head = "RATING DATA PROBLEM: " + loadError + "\n\n" + head;
            PresetDialog.Show("STING Breaker Sizing",
                $"{head}\n\nComputed proposals for {proposals.Count} circuit(s). " +
                $"{blocked.Count} will NOT be applied (device larger than the cable can carry, or no rating large enough):\n" +
                string.Join("\n", blocked.Take(10).Select(b => $"  {b.PanelName}-{b.CircuitNumber}: {b.Note}")) +
                (blocked.Count > 10 ? $"\n  …and {blocked.Count - 10} more" : "") +
                (confirm.Count == 0 ? "" :
                    $"\n\n{confirm.Count} rely on the NEC 240.4(B) next-size-up allowance — CONFIRM each is not a " +
                    "multi-outlet receptacle branch circuit for cord-and-plug portable loads:\n" +
                    string.Join("\n", confirm.Take(10).Select(b => $"  {b.PanelName}-{b.CircuitNumber}: {b.ProposedBreakerA} A over {b.IzA:0.#} A")) +
                    (confirm.Count > 10 ? $"\n  …and {confirm.Count - 10} more" : "")) +
                (WorkflowEngine.IsRunningPreset ? "\n\nCalc_ApplyBreakers commits the rest." : "\n\nClick Apply to commit the rest."),
                ref message);
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

                bool useNec = StingTools.Standards.ElectricalStandardId.IsNec(standard);
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
                string cableType = string.IsNullOrEmpty(cableSnap?.CableType)
                    ? StingTools.Core.Electrical.Bs7671Data.DefaultCableType : cableSnap.CableType;
                var bsData = useNec ? null : StingTools.Commands.Electrical.CableSizer.CableSizerEngine.Bs7671Tables(doc);
                // An invalid project override leaves no tables: In ≤ Iz is then not checked, and says why.
                string tablesBlocked = bsData != null && !string.IsNullOrEmpty(bsData.LoadError) ? bsData.LoadError : null;
                var table = useNec || tablesBlocked != null ? null : bsData.FindTable(mat, ins, method, cableType);

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
                        string necSizeForLimit = null;
                        var necMatForLimit = StingTools.Standards.NEC2023.ConductorMaterial.Copper;
                        if (!useNec)
                        {
                            string wire = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM)?.AsString() ?? "";
                            double csa = StingTools.Core.Electrical.WireSizeParser.ParseCsaMm2(wire);
                            // Prefer the cable recorded on the circuit when a size was applied.
                            var rec = StingTools.Core.Electrical.CircuitCableRecord.Read(sys);
                            var own = csa > 0 && tablesBlocked == null ? rec.FindTable(bsData, mat) : null;
                            double ownIt = own != null ? StingTools.Core.Electrical.Bs7671Data.TabulatedIt(own, csa, phases) : 0;
                            if (ownIt > 0)
                            {
                                iz = ownIt;
                                izBasis = $"{own.Cite()} ({rec}, recorded on the circuit) It for {csa:0.#} mm², 30 °C, ungrouped";
                            }
                            else if (csa > 0 && table != null)
                            {
                                double it = StingTools.Core.Electrical.Bs7671Data.TabulatedIt(table, csa, phases);
                                if (it > 0)
                                {
                                    iz = it;
                                    izBasis = $"{table.Cite()} {mat}/{ins} {cableType} method {method} It for {csa:0.#} mm², 30 °C, ungrouped (CABLE tab assumption)";
                                }
                                else izBasis = $"{csa:0.#} mm² not in {table.Cite()}";
                            }
                            else if (csa > 0 && tablesBlocked != null)
                                izBasis = tablesBlocked;
                            else if (csa > 0)
                                izBasis = $"no BS 7671 table for {mat}/{ins} {cableType} method {method}";
                        }
                        else
                        {
                            // NEC 240.4 (DSCH-30): the conductor ampacity from Table 310.16 at the
                            // 75 °C column for the recorded wire size, UNcorrected (30 °C, ≤ 3 CCC) —
                            // the best case, as the BS branch uses It. A device that fails even that
                            // is certainly wrong; one that passes still needs 310.15 checked.
                            string wire = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_WIRE_SIZE_PARAM)?.AsString() ?? "";
                            string necSize = StingTools.Core.Electrical.WireSizeParser.ParseNecSize(wire);
                            if (necSize == null)
                                izBasis = string.IsNullOrWhiteSpace(wire) ? "no wire size on the circuit"
                                        : $"wire size \"{wire}\" is not a single AWG / kcmil conductor";
                            else
                            {
                                var necMat = string.Equals(mat, "Al", StringComparison.OrdinalIgnoreCase)
                                    ? StingTools.Standards.NEC2023.ConductorMaterial.Aluminum
                                    : StingTools.Standards.NEC2023.ConductorMaterial.Copper;
                                try
                                {
                                    iz = StingTools.Standards.NEC2023.NECStandards.GetConductorAmpacity(necSize, necMat, 75);
                                    necSizeForLimit = necSize;
                                    necMatForLimit = necMat;
                                    izBasis = $"Table 310.16 {(necMat == StingTools.Standards.NEC2023.ConductorMaterial.Aluminum ? "Al" : "Cu")} " +
                                              $"{StingTools.Commands.Electrical.CableSizer.CableSizerEngine.NecSizeLabel(necSize)} @75°C, uncorrected (30 °C, ≤ 3 CCC)";
                                }
                                catch (ArgumentException) { izBasis = $"{necSize} not in NEC Table 310.16"; }
                            }
                        }

                        var sel = StingTools.Core.Electrical.ProtectiveDeviceSelection.Select(
                            iA, useNec, continuous, ratings, iz, izBasis);
                        // NEC 240.4(D): 14/12/10 AWG (Cu) and 12/10 AWG (Al) have a fixed
                        // device ceiling below their tabulated ampacity.
                        if (useNec && necSizeForLimit != null)
                            StingTools.Core.Electrical.NecConductorSelection.ApplySmallConductorLimit(sel, necSizeForLimit, necMatForLimit);
                        string note = sel.Note;
                        if (ratings.Length == 0)
                            note = "rating list not loaded: " + (VoltageDropEngine.BreakerSizesLoadError ?? "empty list");
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
                            NeedsConfirmation = sel.NeedsConfirmation,
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
    // Workflow preset (Calc_ApplyBreakers): no step params — it applies the proposals the
    // last Calc_SizeBreakers computed. Without them the step fails (put Calc_SizeBreakers
    // earlier in the preset); the summary goes to the step message.
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
                if (WorkflowEngine.IsRunningPreset)
                {
                    message = "Apply breakers: no breaker proposals to apply — run Calc_SizeBreakers earlier in the preset.";
                    return Result.Failed;
                }
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
            PresetDialog.Show("STING Electrical",
                $"Applied breaker ratings to {updated} circuit(s). Skipped: {skipped}. " +
                $"Not applied (In > Iz or no rating): {blocked}", ref message);
            return Result.Succeeded;
        }
    }
}
