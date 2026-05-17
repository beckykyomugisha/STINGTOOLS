// StingTools Phase 177 — Place Toilet Room Fixtures command.
//
// Thin IExternalCommand wrapper around ToiletRoomPlacerService.
// User configures occupant count, building use, and gender split
// via a compact TaskDialog before the engine runs.

using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;
using StingTools.UI;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlaceToiletRoomCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var uiDoc = data?.Application?.ActiveUIDocument;
            var doc   = uiDoc?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            // ── Step 1: quick config dialog ──────────────────────────────
            var (occupants, use, split, dryRun, routePlumbing) = ShowConfigDialog();
            if (occupants < 0) return Result.Cancelled; // user hit Cancel

            // ── Step 2: show BS 6465 provision preview ───────────────────
            var provision = ToiletRoomPlacerService.ComputeProvision(occupants, use, split);
            var previewDlg = new TaskDialog("STING — Toilet Room Placement")
            {
                MainInstruction = "BS 6465-1 Provision Preview",
                MainContent     = provision.Summary() + "\n\nProceed with fixture placement?",
                CommonButtons   = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
            };
            if (previewDlg.Show() == TaskDialogResult.Cancel) return Result.Cancelled;

            // ── Step 3: run placement ────────────────────────────────────
            var svc = new ToiletRoomPlacerService
            {
                OccupantCount    = occupants,
                Use              = use,
                Split            = split,
                DryRun           = dryRun,
                AutoRoutePlumbing = routePlumbing,
            };

            ToiletRoomPlacementResult result;
            using (var txn = new Transaction(doc, "STING Place Toilet Room Fixtures"))
            {
                txn.Start();
                try
                {
                    result = svc.PlaceAll(doc, txn);
                    txn.Commit();
                }
                catch (Exception ex)
                {
                    txn.RollBack();
                    StingLog.Error("PlaceToiletRoomCommand", ex);
                    TaskDialog.Show("STING Error", $"Placement failed: {ex.Message}");
                    return Result.Failed;
                }
            }

            // ── Step 4: show report ──────────────────────────────────────
            ShowReport(result);
            return Result.Succeeded;
        }

        // ── Config dialog ─────────────────────────────────────────────────

        private static (int occupants, BuildingUse use, OccupantSplit split,
                        bool dryRun, bool routePlumbing)
            ShowConfigDialog()
        {
            // Compact input via TaskDialog (no custom WPF to keep the
            // command self-contained). For a richer UI, this can be
            // replaced with a StingWizardDialog page.
            var dlg = new TaskDialog("STING — Toilet Room Placement Setup")
            {
                MainInstruction = "Toilet Room Fixture Placement",
                MainContent     =
                    "This command places fixtures in all toilet / WC / bathroom rooms " +
                    "found in the model, checks BS 6465-1 provision, and optionally " +
                    "auto-routes drainage.\n\n" +
                    "Options:\n" +
                    "  OK        = Dry-run (preview only, no elements created)\n" +
                    "  Retry     = Full placement (creates Revit elements)\n" +
                    "  Close     = Cancel",
                CommonButtons   = TaskDialogCommonButtons.Ok
                                | TaskDialogCommonButtons.Retry
                                | TaskDialogCommonButtons.Close,
            };
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Office (50/50 split, 100 occupants)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Healthcare (50/50 split, 30 occupants)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "School / Education (50/50 split, 200 pupils)");
            dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4,
                "Assembly / Public (50/50 split, 300 persons)");

            var r = dlg.Show();

            if (r == TaskDialogResult.Close) return (-1, default, default, false, false);

            bool dry = (r != TaskDialogResult.Retry);

            return r switch
            {
                TaskDialogResult.CommandLink1 =>
                    (100, BuildingUse.Office,     OccupantSplit.Equal5050, dry, false),
                TaskDialogResult.CommandLink2 =>
                    (30,  BuildingUse.Healthcare, OccupantSplit.Equal5050, dry, false),
                TaskDialogResult.CommandLink3 =>
                    (200, BuildingUse.EducationSecondary, OccupantSplit.Equal5050, dry, false),
                TaskDialogResult.CommandLink4 =>
                    (300, BuildingUse.Assembly,   OccupantSplit.Equal5050, dry, false),
                _ =>
                    (100, BuildingUse.Office,     OccupantSplit.Equal5050, dry, false),
            };
        }

        // ── Result display ───────────────────────────────────────────────

        private static void ShowReport(ToiletRoomPlacementResult result)
        {
            string report = result.ReportText();
            StingLog.Info(report);

            var dlg = new TaskDialog("STING — Toilet Room Placement Result")
            {
                MainInstruction = result.IsCompliant
                    ? $"✓ Compliant — {result.FixturesPlaced} fixtures placed"
                    : $"⚠ Compliance gaps — {result.ComplianceGaps.Count} issue(s)",
                MainContent     = result.IsCompliant
                    ? $"All BS 6465-1 provisions met.\n\nRooms: {result.RoomsProcessed}   " +
                      $"Fixtures: {result.FixturesPlaced}" +
                      (result.PlacementResult?.DryRun == true ? "\n\n(Dry-run — no elements written)" : "")
                    : string.Join("\n", result.ComplianceGaps),
                CommonButtons   = TaskDialogCommonButtons.Ok,
                ExpandedContent = report,
            };
            dlg.Show();
        }
    }
}
