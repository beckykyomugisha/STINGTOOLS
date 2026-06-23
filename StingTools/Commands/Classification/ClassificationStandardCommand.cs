// StingTools — set the project's authoritative classification standard (Phase G).
//
//   Classification_SetStandard — pick Uniclass / CSI / OmniClass / Native; writes
//   <project>/_BIM_COORD/sting_classification.json. The BOQ / COBie / handover /
//   IFC grouping cascade (ClassificationReader.ResolveFallback) then leads with
//   that standard. Default (no file) is Uniclass 2015.

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Classification;

namespace StingTools.Commands.Classification
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ClassificationSetStandardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                var doc = ctx?.Doc;
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var current = ClassificationStandard.Active(doc);

                var td = new TaskDialog("STING — Classification Standard")
                {
                    MainInstruction = "Choose the authoritative classification standard",
                    MainContent = $"Current: {ClassificationStandard.Label(current)}.\n\n" +
                                  "This leads the BOQ / COBie / handover / IFC grouping cascade. " +
                                  "Uniclass is the default; the others promote their codes to the front.",
                    AllowCancellation = true
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Uniclass 2015", "Uniclass Pr → Ss → Ef (STING default)");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "CSI MasterFormat", "CSI section first, then Uniclass");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "OmniClass 2.3", "OmniClass first, then Uniclass");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Native (Category / Family)", "No classification codes — group by Revit category/family");

                var res = td.Show();
                ClassStandard? choice = res switch
                {
                    TaskDialogResult.CommandLink1 => ClassStandard.Uniclass,
                    TaskDialogResult.CommandLink2 => ClassStandard.Csi,
                    TaskDialogResult.CommandLink3 => ClassStandard.OmniClass,
                    TaskDialogResult.CommandLink4 => ClassStandard.Native,
                    _ => (ClassStandard?)null
                };
                if (choice == null) return Result.Cancelled;

                string path = ClassificationStandard.Set(doc, choice.Value);
                TaskDialog.Show("STING — Classification Standard",
                    $"Active standard set to {ClassificationStandard.Label(choice.Value)}.\n\n" +
                    $"Written to: {path}\n\n" +
                    "Re-run BOQ / COBie / handover exports to group by the new standard.");
                StingLog.Info($"Classification standard set to {choice.Value}");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("ClassificationSetStandardCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
