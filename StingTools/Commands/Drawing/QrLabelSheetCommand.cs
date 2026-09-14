using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    // ══════════════════════════════════════════════════════════════════════
    //  QR_LabelSheet — put the element QR codes on a sheet that can be plotted.
    //
    //  WHY
    //  ---
    //  `QRCodeCommand` has always written loose PNGs into a folder and stopped
    //  there. Nothing placed them in the model, so producing labels meant a person
    //  dragging files into Word by hand, one per asset. The codes existed and the
    //  workflow did not.
    //
    //  `SheetQrStamper` proved the image-placement pattern on title blocks; this
    //  applies it to a grid. The output is a sheet of QR + tag pairs at a known
    //  physical size, meant to be plotted, cut up, and stuck on assets.
    //
    //  WHAT IT REFUSES TO DO
    //  ---------------------
    //  It never labels an element with no tag. A blank or placeholder label on a
    //  physical asset is worse than no label: it gets stuck on, scanned, and
    //  resolves to nothing, and by then the asset is in a ceiling void.
    // ══════════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class QrLabelSheetCommand : IExternalCommand
    {
        /// <summary>Printed size of each code, mm. Bigger than the title-block stamp
        /// because these get stuck on plant in dusty rooms and scanned at an angle.</summary>
        private const double CodeMm = 30.0;

        /// <summary>Gap between cells, mm. Wide enough to cut along with scissors
        /// without clipping a finder pattern.</summary>
        private const double GutterMm = 12.0;

        /// <summary>Margin from the sheet edge, mm — clear of the plotter's
        /// non-printable border.</summary>
        private const double MarginMm = 20.0;

        private const double MmPerFoot = 304.8;
        private const string ImagePrefix = "STING QR LABEL - ";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var uiApp = ParameterHelpers.GetApp(commandData);
                var uiDoc = uiApp?.ActiveUIDocument;
                if (uiDoc == null) { TaskDialog.Show("STING", "No active document."); return Result.Failed; }
                var doc = uiDoc.Document;

                var selIds = uiDoc.Selection.GetElementIds();
                if (selIds.Count == 0)
                {
                    TaskDialog.Show("STING — QR Label Sheet",
                        "Select the elements to make labels for.\n\n" +
                        "Every selected element needs a tag (ASS_TAG_1_TXT) — untagged ones are " +
                        "reported and skipped, never given a blank label.");
                    return Result.Cancelled;
                }

                // Gather + de-duplicate BEFORE writing anything. Two instances sharing
                // one tag would otherwise produce two identical labels, and a person
                // sticking them on two different assets creates a duplicate nobody can
                // untangle from the scan.
                string projectCode = SheetQrStamper.ResolveProjectCode(doc);
                var byTag = new Dictionary<string, Element>(StringComparer.OrdinalIgnoreCase);
                var untagged = new List<string>();
                var duplicates = new List<string>();

                foreach (var id in selIds)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    string tag = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                    if (string.IsNullOrWhiteSpace(tag))
                    {
                        untagged.Add($"{el.Category?.Name ?? "?"} · id {el.Id.Value}");
                        continue;
                    }
                    if (byTag.ContainsKey(tag)) { duplicates.Add(tag); continue; }
                    byTag[tag] = el;
                }

                if (byTag.Count == 0)
                {
                    TaskDialog.Show("STING — QR Label Sheet",
                        $"None of the {selIds.Count} selected element(s) carries a tag, so there is " +
                        "nothing to label.\n\nRun the tagging pipeline first.");
                    return Result.Cancelled;
                }

                var titleBlockId = FirstTitleBlockTypeId(doc);
                string qrDir = StingPaths.Meta(doc, "_BIM_COORD", "qr", "labels");
                Directory.CreateDirectory(qrDir);

                var sheets = new List<ViewSheet>();
                int placed = 0;
                var warnings = new List<string>();

                var ordered = byTag.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).ToList();

                using (var tg = new TransactionGroup(doc, "STING QR Label Sheets"))
                {
                    tg.Start();
                    using (var t = new Transaction(doc, "STING Build QR Label Sheets"))
                    {
                        t.Start();

                        // Measure the paper before placing anything, from a first sheet
                        // that is kept and used. The grid is then computed ONCE — the
                        // naive version started a row whenever the cursor was above the
                        // bottom margin, so the last row could begin with less than a
                        // cell of height left and run off the plot. A clipped QR is
                        // unreadable while still looking like a usable label.
                        var first = NewLabelSheet(doc, titleBlockId, 1, projectCode);
                        sheets.Add(first);
                        var extent = SheetExtentMm(doc, first);
                        var grid = QrLabelLayout.PlanGrid(extent.w, extent.h, CodeMm, GutterMm, MarginMm);

                        if (grid.PerSheet <= 0)
                        {
                            t.RollBack();
                            tg.RollBack();
                            TaskDialog.Show("STING — QR Label Sheet",
                                $"A {extent.w:F0} x {extent.h:F0} mm sheet cannot carry a {CodeMm:F0} mm label " +
                                $"with {MarginMm:F0} mm margins.\n\nNothing was created. Use a larger title block.");
                            return Result.Cancelled;
                        }

                        var cells = QrLabelLayout.Place(ordered.Count, grid);

                        for (int i = 0; i < ordered.Count; i++)
                        {
                            var kv = ordered[i];
                            var cell = cells[i];

                            while (sheets.Count <= cell.SheetIndex)
                                sheets.Add(NewLabelSheet(doc, titleBlockId, sheets.Count + 1, projectCode));
                            var sheet = sheets[cell.SheetIndex];

                            string url = StingQrFormat.BuildElementUrl(projectCode, kv.Key, kv.Value.UniqueId);
                            string png = Path.Combine(qrDir, SanitiseFileName(kv.Key) + ".png");

                            try
                            {
                                StingQRHelper.SaveQRPng(url, png, 800);
                                PlaceLabel(doc, sheet, png, kv.Key, cell.XMm, cell.YMm);
                                placed++;
                            }
                            catch (Exception ex)
                            {
                                // Name the tag. "3 failed" tells nobody which asset is
                                // going to arrive on site without a label.
                                warnings.Add($"{kv.Key}: {ex.Message}");
                                StingLog.Error($"QrLabelSheet: '{kv.Key}' failed", ex);
                            }
                        }

                        t.Commit();
                    }
                    tg.Assimilate();
                }

                var body = new System.Text.StringBuilder();
                body.AppendLine($"Labels placed : {placed}");
                body.AppendLine($"Sheets created: {sheets.Count}");
                if (untagged.Count > 0)
                {
                    body.AppendLine();
                    body.AppendLine($"Skipped, no tag ({untagged.Count}) — a blank label on a real asset is");
                    body.AppendLine("worse than no label, so these were not given one:");
                    foreach (var u in untagged.Take(8)) body.AppendLine("  • " + u);
                    if (untagged.Count > 8) body.AppendLine($"  … {untagged.Count - 8} more");
                }
                if (duplicates.Count > 0)
                {
                    body.AppendLine();
                    body.AppendLine($"Duplicate tags ({duplicates.Distinct().Count()}) — labelled once each,");
                    body.AppendLine("because two identical labels on two assets cannot be told apart later:");
                    foreach (var d in duplicates.Distinct().Take(8)) body.AppendLine("  • " + d);
                }
                if (warnings.Count > 0)
                {
                    body.AppendLine();
                    body.AppendLine($"Failed ({warnings.Count}):");
                    foreach (var w in warnings.Take(8)) body.AppendLine("  • " + w);
                }
                if (sheets.Count > 0)
                {
                    body.AppendLine();
                    body.AppendLine("Sheets: " + string.Join(", ", sheets.Select(s => s.SheetNumber)));
                }

                TaskDialog.Show("STING — QR Label Sheet",
                    (placed > 0 ? $"Placed {placed} label(s)" : "No labels placed") + "\n\n" + body);

                if (sheets.Count > 0)
                {
                    try { uiDoc.ActiveView = sheets[0]; }
                    catch (Exception ex) { StingLog.Warn($"QrLabelSheet: could not activate sheet: {ex.Message}"); }
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("QrLabelSheetCommand failed", ex);
                TaskDialog.Show("STING", $"QR label sheet failed: {ex.Message}");
                return Result.Failed;
            }
        }

        private static void PlaceLabel(Document doc, ViewSheet sheet, string pngPath,
                                       string tag, double xMm, double yMm)
        {
            var opts = new ImageTypeOptions(pngPath, false, ImageTypeSource.Import);
            var imageType = ImageType.Create(doc, opts);
            try { imageType.Name = ImagePrefix + tag; }
            catch (Exception ex) { StingLog.Warn($"QrLabelSheet: naming image type for '{tag}': {ex.Message}"); }

            double sizeFt = CodeMm / MmPerFoot;
            var centre = new XYZ((xMm + CodeMm / 2) / MmPerFoot, (yMm + CodeMm / 2) / MmPerFoot, 0);

            var instance = ImageInstance.Create(doc, sheet, imageType.Id,
                new ImagePlacementOptions(centre, BoxPlacement.Center));
            var w = instance.get_Parameter(BuiltInParameter.RASTER_SHEETWIDTH);
            if (w != null && !w.IsReadOnly) w.Set(sizeFt);

            // The tag in readable text under the code. A QR alone is unreadable to a
            // person holding it, so a label with no caption cannot be matched to an
            // asset by eye — which is exactly what someone does when the scan fails.
            try
            {
                var textType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();
                if (textType != null)
                {
                    TextNote.Create(doc, sheet.Id,
                        new XYZ(centre.X, (yMm - QrLabelLayout.CaptionBandMm / 2.0) / MmPerFoot, 0),
                        tag, textType.Id);
                }
                else
                {
                    StingLog.Warn("QrLabelSheet: no TextNoteType in the document — labels have no caption.");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"QrLabelSheet: caption for '{tag}': {ex.Message}");
            }
        }

        private static ViewSheet NewLabelSheet(Document doc, ElementId titleBlockId, int index, string projectCode)
        {
            var sheet = ViewSheet.Create(doc, titleBlockId ?? ElementId.InvalidElementId);
            try { sheet.Name = $"QR ASSET LABELS {index:D2}"; }
            catch (Exception ex) { StingLog.Warn($"QrLabelSheet: naming sheet: {ex.Message}"); }
            // Number defensively: a clash with an existing sheet number throws, and a
            // failed rename must not abort a run that is otherwise fine.
            foreach (var candidate in new[] { $"QR-{index:D2}", $"QR-{projectCode}-{index:D2}", $"QR-{Guid.NewGuid():N}".Substring(0, 10) })
            {
                try { sheet.SheetNumber = candidate; break; }
                catch (Exception ex) { StingLog.Warn($"QrLabelSheet: sheet number '{candidate}' rejected: {ex.Message}"); }
            }
            return sheet;
        }

        /// <summary>Sheet size in mm, from the title block's footprint. Falls back to
        /// A1 landscape when there is no title block to measure — stated rather than
        /// silently assumed, because every cell position depends on it.</summary>
        private static (double w, double h) SheetExtentMm(Document doc, ViewSheet sheet)
        {
            try
            {
                var tb = new FilteredElementCollector(doc, sheet.Id)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType()
                    .FirstElement();
                var bb = tb?.get_BoundingBox(sheet);
                if (bb != null)
                    return ((bb.Max.X - bb.Min.X) * MmPerFoot, (bb.Max.Y - bb.Min.Y) * MmPerFoot);
            }
            catch (Exception ex) { StingLog.Warn($"QrLabelSheet: sheet extent read: {ex.Message}"); }

            StingLog.Info("QrLabelSheet: no title block to measure; assuming A1 landscape (841x594mm).");
            return (841.0, 594.0);
        }

        private static ElementId FirstTitleBlockTypeId(Document doc)
            => new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .FirstElementId();

        private static string SanitiseFileName(string value)
        {
            var chars = (value ?? "").ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            var name = new string(chars).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "label" : name;
        }
    }
}
