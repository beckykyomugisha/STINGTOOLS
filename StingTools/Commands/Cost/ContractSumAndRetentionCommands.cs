// ══════════════════════════════════════════════════════════════════════════
//  ContractSumAndRetentionCommands.cs — WP-Q QS lifecycle fills.
//
//    Cost_SetContractSum  — freeze the contract sum at award (writes
//                           COST_CONTRACT_SUM_UGX + an "Award" snapshot) so the
//                           Final Account / AFC stop guessing via a name-grep.
//    Retention_Release    — write retention RELEASE entries (first moiety at
//                           Practical Completion, second at end of the Defects
//                           Liability Period), turning TotalReleased / Balance live.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BOQ;
using StingTools.Core;
using StingTools.Core.PaymentCert;
using StingTools.Select;   // StingListPicker
using StingTools.UI;

namespace StingTools.Commands.Cost
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class CostSetContractSumCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var boq = BOQCostManager.BuildBOQDocument(doc);
                if (boq == null) { TaskDialog.Show("Set Contract Sum", "Could not build the bill."); return Result.Cancelled; }
                string ccy = boq.Currency ?? "UGX";
                // CA-2 — ONE BASIS: freeze the NET-OF-VAT contract sum (works +
                // prelims + OH&P + contingency). The lifecycle (EVM/CVR/AFC/Final
                // Account/certs) reconciles net because actuals + certs are net;
                // VAT is presented only as the final line. The VAT-inclusive figure
                // is shown for reference but is NOT what gets frozen.
                double net = boq.NetTotalExVatUGX;    // the frozen Contract Sum basis
                double grandInclVat = boq.GrandTotalUGX; // presentation reference only
                double existing = TagConfig.GetConfigDouble("COST_CONTRACT_SUM_UGX", 0.0);

                var td = new TaskDialog("Set Contract Sum / Award Baseline")
                {
                    MainInstruction = $"Freeze the contract sum at {ccy} {net:N0} (net of VAT)?",
                    MainContent =
                        $"This writes COST_CONTRACT_SUM_UGX = {net:N0} — the NET-OF-VAT Contract Sum "
                        + "(works + prelims + OH&P + contingency) — and saves an \"Award\" snapshot, so the "
                        + "Final Account, Anticipated Final Cost and EVM all reconcile on one net basis "
                        + "(certs and actuals are net; VAT is the final presentation line only).\n\n"
                        + $"For reference, VAT-inclusive total: {ccy} {grandInclVat:N0}.\n\n"
                        + (existing > 0 ? $"Current frozen value: {ccy} {existing:N0} (will be replaced).\n\n" : "")
                        + "Tip: to award a NEGOTIATED figure different from the live total, set COST_CONTRACT_SUM_UGX "
                        + "(net of VAT) in the project config before running this.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                TagConfig.SetConfigValue("COST_CONTRACT_SUM_UGX", net);
                string snapPath = null;
                try { snapPath = BOQCostManager.SaveSnapshot(doc, boq, "Award", "Award"); }
                catch (Exception ex) { StingLog.Warn($"SetContractSum snapshot: {ex.Message}"); }

                StingResultPanel.Create("Contract Sum frozen")
                    .AddSection("AWARD BASELINE")
                    .Metric("Contract Sum (net of VAT)", $"{ccy} {net:N0}")
                    .Metric("VAT-inclusive (reference)", $"{ccy} {grandInclVat:N0}")
                    .Metric("Snapshot", snapPath != null ? "Award snapshot saved" : "(snapshot save failed — value still frozen)")
                    .Text("Frozen NET of VAT — the Final Account, Anticipated Final Cost and EVM all reconcile on this one basis.")
                    .Show();
                StingLog.Info($"Contract sum frozen at {ccy} {net:N0} net of VAT (award baseline).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Cost_SetContractSum", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RetentionReleaseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }
                string contractRef = doc.ProjectInformation?.Number ?? "";

                var ledger = PaymentCertEngine.ComputeLedger(doc, contractRef);
                double withheld = ledger.TotalWithheld;
                double released = ledger.TotalReleased;
                double balance = ledger.Balance;
                if (withheld <= 0)
                {
                    StingResultPanel.Create("Retention Release")
                        .AddSection("NOTHING WITHHELD")
                        .Text("No retention has been withheld yet (issue interim certificates first).")
                        .Show();
                    return Result.Cancelled;
                }

                // Offer the moiety to release. First moiety = half the retention
                // fund at Practical Completion; second = the remaining balance at
                // the end of the Defects Liability Period (JCT 2024 §4.20 / NEC4 /
                // FIDIC §14.9). The configurable split defaults to 50%.
                double firstPct = TagConfig.GetConfigDouble("COST_RETENTION_FIRST_MOIETY_PCT", 50.0);
                double firstMoiety = Math.Round(withheld * firstPct / 100.0, 0);
                bool firstDone = released >= firstMoiety - 1;
                var options = new List<string>
                {
                    $"First moiety — Practical Completion (release {firstPct:F0}% = {firstMoiety:N0})",
                    $"Second moiety — end of Defects Liability (release balance {balance:N0})"
                };
                string pick = StingListPicker.Show("STING — Retention release",
                    $"Withheld {withheld:N0} · released {released:N0} · balance {balance:N0}. Choose the release:", options);
                if (string.IsNullOrEmpty(pick)) return Result.Cancelled;
                bool second = options.IndexOf(pick) == 1;

                double amount = second ? balance : Math.Min(firstMoiety - released, balance);
                if (amount <= 0)
                {
                    StingResultPanel.Create("Retention Release")
                        .AddSection("NOTHING TO RELEASE")
                        .Text(second ? "The balance is already fully released."
                                     : "The first moiety has already been released — release the second moiety at the end of the Defects Liability Period.")
                        .Show();
                    return Result.Cancelled;
                }

                PaymentCertEngine.RecordRelease(doc, contractRef, new RetentionEntry
                {
                    CertNumber = second ? -2 : -1,   // negative = release marker (REL-1/REL-2)
                    Date = DateTime.UtcNow,
                    Kind = "release",
                    Amount = amount,
                    Reason = second
                        ? "Second moiety (REL-2) — end of Defects Liability Period / Making-Good-Defects certificate"
                        : "First moiety (REL-1) — Practical Completion"
                });

                var after = PaymentCertEngine.ComputeLedger(doc, contractRef);
                StingResultPanel.Create("Retention released")
                    .AddSection("RELEASE")
                    .Metric("Released now", $"{amount:N0}")
                    .Metric("Total withheld", $"{after.TotalWithheld:N0}")
                    .Metric("Total released", $"{after.TotalReleased:N0}")
                    .Metric("Retention balance", $"{after.Balance:N0}")
                    .Text(second ? "Second moiety released — retention fund cleared at end of Defects Liability."
                                 : "First moiety released at Practical Completion. Release the second moiety at the end of the Defects Liability Period.")
                    .Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Retention_Release", ex);
                message = ex.Message; return Result.Failed;
            }
        }
    }
}
