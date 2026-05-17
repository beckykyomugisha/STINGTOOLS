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
            TaskDialog.Show("STING Breaker Sizing",
                $"Computed proposals for {proposals.Count} circuit(s). " +
                $"Review the BREAKER SIZING grid and click Apply to commit.");
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

                foreach (var sys in systems)
                {
                    try
                    {
                        double iA = sys.get_Parameter(BuiltInParameter.RBS_ELEC_APPARENT_CURRENT_PARAM)?.AsDouble() ?? 0;
                        if (iA <= 0) continue;
                        bool useNec = string.Equals(standard, "NEC", StringComparison.OrdinalIgnoreCase);
                        bool useMccb = string.Equals(standard, "BS_MCCB", StringComparison.OrdinalIgnoreCase);
                        int proposed = useNec
                            ? VoltageDropEngine.NextStandardBreakerSizeNEC(iA, continuous)
                            : VoltageDropEngine.NextStandardBreakerSizeBS(iA, continuous, useMccb);
                        int minA = (int)Math.Ceiling(continuous ? iA * 1.25 : iA);

                        list.Add(new BreakerProposal
                        {
                            CircuitId = sys.Id,
                            PanelName = sys.PanelName ?? "",
                            CircuitNumber = sys.get_Parameter(BuiltInParameter.RBS_ELEC_CIRCUIT_NUMBER)?.AsString() ?? "",
                            LoadName = sys.LoadName ?? sys.Name,
                            DesignCurrentA = iA,
                            MinBreakerA = minA,
                            ProposedBreakerA = proposed
                        });
                    }
                    catch (Exception ex2) { StingLog.Warn($"Breaker compute: {ex2.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"BreakerSizer.Compute: {ex.Message}"); }
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

            int updated = 0, skipped = 0;
            using (var tx = new Transaction(doc, "STING Apply Breaker Sizing"))
            {
                tx.Start();
                foreach (var prop in proposals)
                {
                    try
                    {
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
                $"Applied breaker ratings to {updated} circuit(s). Skipped: {skipped}");
            return Result.Succeeded;
        }
    }
}
