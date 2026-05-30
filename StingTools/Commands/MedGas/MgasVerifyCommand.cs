// Healthcare Pack H-7 — Walk-through verification command.
// Runs the NFPA 99 §5.1.12 12-step checklist as a TaskDialog
// and persists a JSON record to <project>/_BIM_COORD/healthcare/.

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.MedGas;
using StingTools.Standards.HTM;
using System;
using System.IO;

namespace StingTools.Commands.MedGas
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MgasVerifyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;

                // Phase D — honour HcOptions.MgasStep. 0 ⇒ run all 12 steps (legacy
                // behaviour); 1..12 ⇒ run only that single NFPA 99 §5.1.12 step.
                // Out-of-range values fall back to "All" rather than silently
                // running the wrong step.
                int requestedStep = HcOptions.MgasStep;
                var checklist = HTMStandards.MgpsVerificationChecklist;
                bool runSingleStep = requestedStep >= 1 && requestedStep <= checklist.Length;

                var rec = new MgasVerificationRecord
                {
                    DateUtc = DateTime.UtcNow,
                    ProjectCode = doc.ProjectInformation?.LookupParameter("PRJ_PROJECT_COD_TXT")?.AsString() ?? "",
                    Zone = string.IsNullOrEmpty(HcOptions.MgasZone) ? "ALL" : HcOptions.MgasZone,
                    GasCode = string.IsNullOrEmpty(HcOptions.MgasGas) ? "ALL" : HcOptions.MgasGas,
                    VerifierName = string.IsNullOrEmpty(HcOptions.MgasVerifier) ? Environment.UserName : HcOptions.MgasVerifier
                };

                // Build the step list to run: single step (1-based → 0-based index)
                // or the full 12-step checklist.
                var stepsToRun = runSingleStep
                    ? new[] { checklist[requestedStep - 1] }
                    : checklist;

                string headerSuffix = runSingleStep
                    ? $" — Step {requestedStep}/{checklist.Length}"
                    : " — Full";

                int pass = 0, fail = 0;
                foreach (var step in stepsToRun)
                {
                    var dlg = new TaskDialog("STING — MGPS Verification step" + headerSuffix)
                    {
                        MainInstruction = step,
                        MainContent = "Mark this step as PASS or FAIL.",
                        AllowCancellation = true
                    };
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "PASS");
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "FAIL");
                    dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel verification");
                    var r = dlg.Show();
                    if (r == TaskDialogResult.CommandLink3) return Result.Cancelled;
                    bool ok = r == TaskDialogResult.CommandLink1;
                    rec.CheckResults[step] = ok;
                    if (ok) pass++; else fail++;
                }

                rec.OverallPass = fail == 0;
                var doc_path = doc.PathName;
                var root = string.IsNullOrEmpty(doc_path) ? Path.GetTempPath() : Path.GetDirectoryName(doc_path);
                var written = MgasVerificationLog.Persist(root, rec);

                string scopeLabel = runSingleStep
                    ? $"Step {requestedStep}/{checklist.Length}"
                    : $"All {checklist.Length} steps";

                TaskDialog.Show("STING — MGPS Verification complete",
                    $"Scope: {scopeLabel}\n" +
                    $"Result: {(rec.OverallPass ? "PASS" : "FAIL")}\n" +
                    $"Pass={pass}  Fail={fail}\nLog: {written}");
                StingLog.Info($"MGPS verification ({scopeLabel}) persisted to {written} (pass={pass} fail={fail})");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("MgasVerifyCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
