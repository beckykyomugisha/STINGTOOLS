// StingTools — Work Item E · Sheet-graphics orchestrator
//
// TitleBlock_StampSheetGraphics runs the full toggle-gated slot-graphics
// pass on a sheet: QR (W4) + north arrow + scale bar + key plan + legend.
// Each stamper is invoked independently and a single failure is collected,
// never aborting the others. Two entry points mirror the QR commands:
//   TitleBlock_StampSheetGraphics    — the ACTIVE sheet.
//   TitleBlock_StampSheetGraphicsAll — every sheet, one TransactionGroup.
// The same orchestrator is reused (optionally) by DrawingProducer.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    /// <summary>Per-sheet result of the full graphics pass.</summary>
    internal sealed class SheetGraphicsResult
    {
        public int Placed, Skipped, Failed;
        public void Fold(bool placed, bool failed)
        {
            if (placed) Placed++; else if (failed) Failed++; else Skipped++;
        }
    }

    /// <summary>Runs every slot-graphic stamper on a single sheet. Caller must
    /// have an open transaction. Collects per-graphic notes into
    /// <paramref name="log"/>; never throws for a single-graphic failure.</summary>
    internal static class TitleBlockGraphicsOrchestrator
    {
        public static SheetGraphicsResult StampSheet(Document doc, ViewSheet sheet, List<string> log)
        {
            var r = new SheetGraphicsResult();

            // QR (W4) — reuse the existing stamper for parity with TitleBlock_StampQR.
            try
            {
                var q = TitleBlockQrStamper.Stamp(doc, sheet, log);
                r.Fold(q == TitleBlockQrStamper.StampOutcome.Placed,
                       q == TitleBlockQrStamper.StampOutcome.Failed);
            }
            catch (Exception ex) { r.Failed++; log?.Add($"{sheet?.SheetNumber}: QR — {ex.Message}"); }

            FoldGraphic(r, log, sheet, () => NorthArrowStamper.Stamp(doc, sheet, log), "north arrow");
            FoldGraphic(r, log, sheet, () => ScaleBarStamper.Stamp(doc, sheet, log),  "scale bar");
            FoldGraphic(r, log, sheet, () => KeyPlanStamper.Stamp(doc, sheet, log),   "key plan");
            FoldGraphic(r, log, sheet, () => LegendStamper.Stamp(doc, sheet, log),    "legend");
            return r;
        }

        private static void FoldGraphic(SheetGraphicsResult r, List<string> log, ViewSheet sheet,
            Func<GraphicOutcome> run, string label)
        {
            try
            {
                var o = run();
                r.Fold(o == GraphicOutcome.Placed, o == GraphicOutcome.Failed);
            }
            catch (Exception ex)
            {
                r.Failed++;
                log?.Add($"{sheet?.SheetNumber}: {label} — {ex.Message}");
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockStampSheetGraphicsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("STING — Stamp Sheet Graphics", "Open a sheet view, then re-run.");
                return Result.Cancelled;
            }

            var log = new List<string>();
            SheetGraphicsResult r;
            using (var tx = new Transaction(doc, "STING Stamp Sheet Graphics"))
            {
                tx.Start();
                r = TitleBlockGraphicsOrchestrator.StampSheet(doc, sheet, log);
                tx.Commit();
            }

            var report = $"Sheet {sheet.SheetNumber}\n\nPlaced: {r.Placed}\nSkipped: {r.Skipped} "
                       + $"(toggle off / no slot / no family / no data)\nFailed: {r.Failed}.";
            if (log.Count > 0) report += "\n\n" + string.Join("\n", log.Take(15));
            TaskDialog.Show("STING — Stamp Sheet Graphics", report);
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockStampSheetGraphicsAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>().Where(s => !s.IsPlaceholder).ToList();
            if (sheets.Count == 0)
            { TaskDialog.Show("STING — Stamp Sheet Graphics", "No sheets found."); return Result.Cancelled; }

            var total = new SheetGraphicsResult();
            var log = new List<string>();
            using (var tg = new TransactionGroup(doc, "STING Stamp Sheet Graphics (all)"))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "STING Stamp Sheet Graphics"))
                {
                    tx.Start();
                    foreach (var s in sheets)
                    {
                        var r = TitleBlockGraphicsOrchestrator.StampSheet(doc, s, log);
                        total.Placed += r.Placed; total.Skipped += r.Skipped; total.Failed += r.Failed;
                    }
                    tx.Commit();
                }
                tg.Assimilate();
            }

            var report = $"{sheets.Count} sheet(s).\n\nGraphics placed: {total.Placed}\n"
                       + $"Skipped: {total.Skipped}\nFailed: {total.Failed}.";
            if (log.Count > 0) report += "\n\n" + string.Join("\n", log.Take(20));
            TaskDialog.Show("STING — Stamp Sheet Graphics — All Sheets", report);
            return Result.Succeeded;
        }
    }
}
