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
                var rec = new MgasVerificationRecord
                {
                    DateUtc = DateTime.UtcNow,
                    ProjectCode = doc.ProjectInformation?.LookupParameter("PRJ_ORG_PROJECT_CODE_TXT")?.AsString() ?? "",
                    Zone = "ALL",
                    GasCode = "ALL",
                    VerifierName = Environment.UserName
                };

                int pass = 0, fail = 0;
                foreach (var step in HTMStandards.MgpsVerificationChecklist)
                {
                    var dlg = new TaskDialog("STING — MGPS Verification step")
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

                TaskDialog.Show("STING — MGPS Verification complete",
                    $"Result: {(rec.OverallPass ? "PASS" : "FAIL")}\n" +
                    $"Pass={pass}  Fail={fail}\nLog: {written}");
                StingLog.Info($"MGPS verification persisted to {written} (pass={pass} fail={fail})");
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
