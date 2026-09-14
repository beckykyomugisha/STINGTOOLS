using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    // ══════════════════════════════════════════════════════════════════════
    //  Sheet_SetQRAnchor — point at the QR cell your title block already draws.
    //
    //  WHY
    //  ---
    //  Real title blocks reserve a QR cell and label it, and it is in a different
    //  place on every one. Three sheets exported from a live project on 2026-09-14
    //  had "SCAN · VERIFY ISSUE" / "SCAN CURRENT ISSUE" cells at (196,403),
    //  (767,109) and (591,34) mm — with an empty square drawn ready for the code,
    //  and nothing filling it.
    //
    //  Editing STING_TITLE_BLOCKS.json is not the answer for those: they are not in
    //  it, and a project should not have to edit a corporate catalogue to say where
    //  its own cell is. This records the cell in Extensible Storage on the
    //  title-block TYPE, which the stamper prefers above the catalogue.
    //
    //  It is stored rather than written to TB_QR_ANCHOR_JSON_TXT because Revit
    //  will not bind a project parameter to OST_TitleBlocks — the category
    //  answers false to AllowsBoundParameters — so that parameter can only ever
    //  exist on a family authored with it. Storing works on every title block,
    //  including the vendor and hand-drawn ones this command exists for.
    //
    //  Pick two opposite corners of the cell and it records the square that fits.
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetSetQrAnchorCommand : IExternalCommand
    {
        private const double MmPerFoot = 304.8;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = ParameterHelpers.GetApp(commandData);
                var uiDoc = uiApp?.ActiveUIDocument;
                if (uiDoc == null) { TaskDialog.Show("STING", "No active document."); return Result.Failed; }
                var doc = uiDoc.Document;

                if (doc.ActiveView is not ViewSheet sheet)
                {
                    TaskDialog.Show("STING — QR Anchor",
                        "Open the sheet whose title block you want to set the QR cell on, then run this again.");
                    return Result.Cancelled;
                }

                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstElement();
                if (tb == null)
                {
                    TaskDialog.Show("STING — QR Anchor", "This sheet has no title block.");
                    return Result.Cancelled;
                }

                var existing = SheetQrStamper.ResolveAnchor(doc, sheet, tb);
                var intro = new TaskDialog("STING — Set QR Anchor")
                {
                    MainInstruction = "Pick the two opposite corners of the QR cell",
                    MainContent =
                        "Most title blocks already draw an empty square next to a \"SCAN\" label. " +
                        "Click two opposite corners of it and the QR will be stamped there from now on.\n\n" +
                        $"Title block : {TitleBlockName(doc, tb)}\n" +
                        $"Current     : {(existing == null ? "nothing declared — falling back to a corner" : existing.ToString())}\n\n" +
                        "The cell is recorded on this title-block TYPE, so every sheet using " +
                        "it gets the same cell. No shared parameter, no Load Params step.",
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok,
                };
                if (intro.Show() != TaskDialogResult.Ok) return Result.Cancelled;

                XYZ p1, p2;
                try
                {
                    p1 = uiDoc.Selection.PickPoint("First corner of the QR cell");
                    p2 = uiDoc.Selection.PickPoint("Opposite corner of the QR cell");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;   // the user pressed Escape; that is not an error
                }

                double x0 = Math.Min(p1.X, p2.X) * MmPerFoot;
                double y0 = Math.Min(p1.Y, p2.Y) * MmPerFoot;
                double w = Math.Abs(p2.X - p1.X) * MmPerFoot;
                double h = Math.Abs(p2.Y - p1.Y) * MmPerFoot;
                double size = Math.Min(w, h);

                if (size < SheetQrPlacement.MinScannableMm)
                {
                    // Refuse rather than write it. An anchor the stamper will then
                    // reject is worse than no anchor: the operator would believe the
                    // cell was set and find a corner stamp on the plot.
                    TaskDialog.Show("STING — QR Anchor",
                        $"That cell is {size:F1} mm, under the {SheetQrPlacement.MinScannableMm:F0} mm a printed " +
                        "code needs to stay scannable.\n\nNothing was written. Pick a larger cell.");
                    return Result.Cancelled;
                }

                // Centre the square in the picked rectangle, so a slightly non-square
                // pick still lands in the middle of the drawn box rather than its corner.
                double cx = x0 + w / 2.0, cy = y0 + h / 2.0;
                double ax = cx - size / 2.0, ay = cy - size / 2.0;

                string shown = string.Format(CultureInfo.InvariantCulture,
                    "x {0:0.##} · y {1:0.##} · {2:0.##} mm square", ax, ay, size);

                // Written to Extensible Storage on the title-block TYPE, not to a
                // parameter. OST_TitleBlocks answers false to
                // Category.AllowsBoundParameters, so a project parameter can never
                // hold this — LoadSharedParams skips every TB_ parameter aimed at
                // that category, which is what made this command refuse on every
                // title block it was ever pointed at. Storing it means this works on
                // ANY family: STING-authored, vendor, or hand-drawn, with no shared
                // parameter and no Load Params step.
                //
                // On the TYPE so one pick covers every sheet using that title block,
                // which is the unit an operator thinks in.
                var tbType = doc.GetElement(tb.GetTypeId());
                if (tbType == null)
                {
                    TaskDialog.Show("STING — QR Anchor",
                        "This title block has no resolvable type, so there is nothing to " +
                        "record the cell against. Nothing was written.");
                    return Result.Failed;
                }

                using (var t = new Transaction(doc, "STING Set QR Anchor"))
                {
                    t.Start();
                    if (!Core.Storage.StingQrAnchorSchema.Write(tbType, ax, ay, size))
                    {
                        t.RollBack();
                        TaskDialog.Show("STING — QR Anchor",
                            "The QR cell could not be recorded on this title-block type.\n\n" +
                            "Nothing was written — see the STING log for the reason. Reporting " +
                            "success here would leave you expecting a cell that is not stored.");
                        return Result.Failed;
                    }
                    t.Commit();
                }

                StingLog.Info($"Sheet_SetQRAnchor: '{sheet.SheetNumber}' anchor stored on type " +
                    $"'{TitleBlockName(doc, tb)}' — {shown}");

                var done = new TaskDialog("STING — QR Anchor")
                {
                    MainInstruction = "QR cell recorded",
                    MainContent =
                        $"{shown}\n\n" +
                        $"({ax:F0}, {ay:F0}) mm from the sheet origin, {size:F0} mm square.\n\n" +
                        "Stamp it now to see it in place.",
                    CommonButtons = TaskDialogCommonButtons.Ok,
                };
                done.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Stamp this sheet now");
                if (done.Show() == TaskDialogResult.CommandLink1)
                {
                    SheetQrResult r;
                    using (var t = new Transaction(doc, "STING Stamp Sheet QR"))
                    {
                        t.Start();
                        r = SheetQrStamper.Stamp(doc, new[] { sheet });
                        t.Commit();
                    }
                    SheetQrCommandHelpers.Report("STING — Sheet QR", r, $"sheet '{sheet.SheetNumber}'");
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("SheetSetQrAnchorCommand failed", ex);
                TaskDialog.Show("STING", $"Set QR anchor failed: {ex.Message}");
                return Result.Failed;
            }
        }

        private static string TitleBlockName(Document doc, Element tb)
        {
            try { return (doc.GetElement(tb.GetTypeId()) as ElementType)?.FamilyName ?? tb.Name; }
            catch (Exception ex) { StingLog.Warn($"Sheet_SetQRAnchor: name read: {ex.Message}"); return tb.Name; }
        }
    }
}
