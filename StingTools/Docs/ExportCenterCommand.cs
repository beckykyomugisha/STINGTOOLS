using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterCommand — IExternalCommand entry point for the unified
    //  STING Export Centre. Replaces ad-hoc batch-print/PDF/DWG flows by
    //  routing every export request through the StingExportCenterDialog.
    //
    //  Command tag: "ExportCenter" (registered in StingCommandHandler).
    //  Workflow tag: same.
    // ════════════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCenterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.UIDoc == null)
                {
                    TaskDialog.Show("STING Export Centre", "No active document.");
                    return Result.Failed;
                }
                StingExportCenterDialog.Show(ctx.UIDoc);
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                StingLog.Error("ExportCenterCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Quick-launch shortcut that opens the Export Centre with PDF preselected.
    /// Wired to the legacy "BatchPrintSheets" tag so existing buttons keep working.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExportCenterPdfCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.UIDoc == null)
                {
                    TaskDialog.Show("STING Export Centre", "No active document.");
                    return Result.Failed;
                }

                // Pre-set the PDF-only profile by mutating state, so the dialog
                // opens already configured for PDF export.
                var state = ExportCenterEngine.LoadState();
                var pdfProfile = state.Profiles.Find(p => p.Name == "Default — PDF only")
                                ?? state.Profiles.Find(p => p.BuiltIn);
                if (pdfProfile != null)
                {
                    state.LastProfile = pdfProfile.Name;
                    ExportCenterEngine.SaveState(state);
                }

                StingExportCenterDialog.Show(ctx.UIDoc);
                return Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                StingLog.Error("ExportCenterPdfCommand failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
