using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    /// <summary>
    /// W4 — stamp a scannable QR code onto a sheet's title-block "qr-code" slot.
    ///
    /// The QR image is placed on the SHEET (via <c>ImageType.Create</c> +
    /// <c>ImageInstance.Create</c>), which sidesteps the Revit 2025 limitation
    /// that title-block labels can no longer be authored from the API.
    ///
    /// Payload: <c>&lt;web-app-base&gt;/sheet/{fullRef}</c>, where the base URL is
    /// resolved from the Planscape client settings (env var → machine file →
    /// baked <c>app.planscape.build</c>). <c>{fullRef}</c> is the ISO 19650
    /// full sheet reference (<c>PRJ_SHEET_FULL_REF_TXT</c>), falling back to
    /// the Revit sheet number.
    ///
    /// Gated by <c>PRJ_TB_SHOW_QR_CODE_BOOL</c> on the title-block instance (skips
    /// when explicitly toggled off). Idempotent: existing STING QR images on the
    /// sheet are removed and re-created, so re-runs never duplicate and the code
    /// regenerates when the reference changes.
    /// </summary>
    internal static class TitleBlockQrStamper
    {
        private const string ImageTypeNamePrefix = "STING_QR_";
        private const double MmPerFoot = 304.8;

        public enum StampOutcome { Placed, SkippedToggleOff, SkippedNoRef, Failed }

        /// <summary>Stamp (or refresh) the QR image on one sheet. Caller must
        /// have an open transaction.</summary>
        public static StampOutcome Stamp(Document doc, ViewSheet sheet, List<string> log)
        {
            try
            {
                var titleBlock = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);

                // Respect the visibility toggle when present on the title block.
                if (titleBlock != null)
                {
                    var toggle = titleBlock.LookupParameter("PRJ_TB_SHOW_QR_CODE_BOOL");
                    if (toggle != null && toggle.StorageType == StorageType.Integer && toggle.AsInteger() == 0)
                    {
                        RemoveExistingQr(doc, sheet);
                        return StampOutcome.SkippedToggleOff;
                    }
                }

                string fullRef = ReadFullRef(sheet);
                if (string.IsNullOrWhiteSpace(fullRef))
                    return StampOutcome.SkippedNoRef;

                string url = BuildSheetUrl(fullRef);
                string pngPath = ResolveQrPngPath(doc, fullRef);
                StingQRHelper.SaveQRPng(url, pngPath, size: 600);

                // Idempotent: drop any prior STING QR on this sheet before placing.
                RemoveExistingQr(doc, sheet);

                // Resolve the qr-code slot (feet, sheet coords). Fall back to a
                // bottom-right corner box when the title block has no slot.
                XYZ center;
                double sizeFt;
                if (TryResolveQrSlot(doc, titleBlock, out var slotCenter, out var slotSizeFt))
                {
                    center = slotCenter;
                    sizeFt = slotSizeFt;
                }
                else
                {
                    var outline = sheet.Outline;
                    double margin = 25.0 / MmPerFoot;
                    sizeFt = 18.0 / MmPerFoot;
                    center = new XYZ(outline.Max.U - margin - sizeFt / 2.0,
                                     outline.Min.V + margin + sizeFt / 2.0, 0);
                    log?.Add($"{sheet.SheetNumber}: no 'qr-code' slot — placed in bottom-right corner.");
                }

                var typeOpts = new ImageTypeOptions(pngPath, false, ImageTypeSource.Import);
                var imageType = ImageType.Create(doc, typeOpts);
                try { imageType.Name = MakeUniqueImageTypeName(doc, fullRef); }
                catch (Exception ex) { StingLog.Warn($"QR image type name: {ex.Message}"); }

                var placeOpts = new ImagePlacementOptions(center, BoxPlacement.Center);
                var instance = ImageInstance.Create(doc, sheet, imageType.Id, placeOpts);

                // Fit to the slot (square QR): width == height.
                try
                {
                    var wPar = instance.get_Parameter(BuiltInParameter.RASTER_SHEETWIDTH);
                    if (wPar != null && !wPar.IsReadOnly) wPar.Set(sizeFt);
                    var hPar = instance.get_Parameter(BuiltInParameter.RASTER_SHEETHEIGHT);
                    if (hPar != null && !hPar.IsReadOnly) hPar.Set(sizeFt);
                }
                catch (Exception ex) { StingLog.Warn($"QR size on {sheet.SheetNumber}: {ex.Message}"); }

                return StampOutcome.Placed;
            }
            catch (Exception ex)
            {
                StingLog.Error($"QR stamp on {sheet?.SheetNumber}: {ex.Message}", ex);
                log?.Add($"{sheet?.SheetNumber}: failed — {ex.Message}");
                return StampOutcome.Failed;
            }
        }

        private static string ReadFullRef(ViewSheet sheet)
        {
            try
            {
                var p = sheet.LookupParameter("PRJ_SHEET_FULL_REF_TXT");
                if (p != null && p.HasValue)
                {
                    var v = p.AsString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch (Exception ex) { StingLog.Warn($"QR ref read: {ex.Message}"); }
            return sheet.SheetNumber ?? "";
        }

        private static string BuildSheetUrl(string fullRef)
        {
            // Web-app base (e.g. https://app.planscape.build/) resolved from the
            // Planscape client settings; append the /sheet/{ref} path.
            string webBase;
            try { webBase = PlanscapeServerClient.FormatWebAppUrl(PlanscapeServerClient.ResolveDefaultServerUrl(), null); }
            catch (Exception ex)
            {
                StingLog.Warn($"QR base url: {ex.Message}");
                webBase = "https://app.planscape.build/";
            }
            return webBase.TrimEnd('/') + "/sheet/" + Uri.EscapeDataString(fullRef);
        }

        private static string ResolveQrPngPath(Document doc, string fullRef)
        {
            string safe = fullRef;
            foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            string dir;
            if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                dir = Path.Combine(Path.GetDirectoryName(doc.PathName) ?? ".", "_BIM_COORD", "qr");
            else
                dir = Path.Combine(Path.GetTempPath(), "STING_QR");
            return Path.Combine(dir, safe + ".png");
        }

        private static bool TryResolveQrSlot(Document doc, Element titleBlock, out XYZ center, out double sizeFt)
        {
            center = XYZ.Zero;
            sizeFt = 0;
            if (titleBlock == null) return false;
            try
            {
                var slotMap = TitleBlockSlotUtils.ReadSlotBoundsFromTitleBlock(doc, titleBlock);
                var slotId = TitleBlockSlotUtils.ResolveSlotIdForTag(slotMap, "qr-code", null);
                if (slotId == null || !slotMap.TryGetValue(slotId, out var bounds) || bounds.Bbox == null)
                    return false;
                var min = bounds.Min;
                var max = bounds.Max;
                center = new XYZ((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0, 0);
                // Square QR: use the smaller slot dimension.
                sizeFt = Math.Max(0.01, Math.Min(max.X - min.X, max.Y - min.Y));
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"QR slot resolve: {ex.Message}"); return false; }
        }

        private static void RemoveExistingQr(Document doc, ViewSheet sheet)
        {
            try
            {
                var toDelete = new FilteredElementCollector(doc, sheet.Id)
                    .OfClass(typeof(ImageInstance))
                    .Where(e =>
                    {
                        try
                        {
                            var t = doc.GetElement(e.GetTypeId()) as ElementType;
                            return t?.Name != null && t.Name.StartsWith(ImageTypeNamePrefix, StringComparison.OrdinalIgnoreCase);
                        }
                        catch { return false; }
                    })
                    .Select(e => e.Id)
                    .ToList();
                if (toDelete.Count > 0) doc.Delete(toDelete);
            }
            catch (Exception ex) { StingLog.Warn($"QR dedup on {sheet.SheetNumber}: {ex.Message}"); }
        }

        private static string MakeUniqueImageTypeName(Document doc, string fullRef)
        {
            string safe = fullRef;
            foreach (var c in new[] { '{', '}', '[', ']', '|', ':', ';', '<', '>', '?', '\\', '/' })
                safe = safe.Replace(c, '_');
            string baseName = ImageTypeNamePrefix + safe;
            var existing = new HashSet<string>(
                new FilteredElementCollector(doc).OfClass(typeof(ImageType))
                    .Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            string name = baseName;
            int n = 2;
            while (existing.Contains(name) && n < 1000) name = $"{baseName}_{n++}";
            return name;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockStampQRCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;
            if (!(doc.ActiveView is ViewSheet sheet))
            {
                TaskDialog.Show("STING QR Stamp", "Open a sheet view, then re-run to stamp its QR code.");
                return Result.Cancelled;
            }

            var log = new List<string>();
            TitleBlockQrStamper.StampOutcome outcome;
            using (var tx = new Transaction(doc, "STING Stamp QR Code"))
            {
                tx.Start();
                outcome = TitleBlockQrStamper.Stamp(doc, sheet, log);
                tx.Commit();
            }

            switch (outcome)
            {
                case TitleBlockQrStamper.StampOutcome.Placed:
                    TaskDialog.Show("STING QR Stamp", $"QR code stamped on sheet {sheet.SheetNumber}.");
                    return Result.Succeeded;
                case TitleBlockQrStamper.StampOutcome.SkippedToggleOff:
                    TaskDialog.Show("STING QR Stamp",
                        $"PRJ_TB_SHOW_QR_CODE_BOOL is off on sheet {sheet.SheetNumber} — QR skipped (any existing QR removed).");
                    return Result.Cancelled;
                case TitleBlockQrStamper.StampOutcome.SkippedNoRef:
                    TaskDialog.Show("STING QR Stamp",
                        $"Sheet {sheet.SheetNumber} has no sheet reference to encode.");
                    return Result.Cancelled;
                default:
                    msg = log.Count > 0 ? string.Join("\n", log) : "QR stamp failed.";
                    return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockStampQRAllCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(data);
            if (ctx == null) { msg = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .ToList();
            if (sheets.Count == 0)
            {
                TaskDialog.Show("STING QR Stamp", "No sheets found in this project.");
                return Result.Cancelled;
            }

            int placed = 0, skipped = 0, failed = 0;
            var log = new List<string>();
            using (var tg = new TransactionGroup(doc, "STING Stamp QR Codes (all sheets)"))
            {
                tg.Start();
                using (var tx = new Transaction(doc, "STING Stamp QR Codes"))
                {
                    tx.Start();
                    foreach (var sheet in sheets)
                    {
                        var outcome = TitleBlockQrStamper.Stamp(doc, sheet, log);
                        if (outcome == TitleBlockQrStamper.StampOutcome.Placed) placed++;
                        else if (outcome == TitleBlockQrStamper.StampOutcome.Failed) failed++;
                        else skipped++;
                    }
                    tx.Commit();
                }
                tg.Assimilate();
            }

            string report = $"QR stamped on {placed} sheet(s).\n" +
                            $"Skipped: {skipped} (toggle off / no reference).\n" +
                            $"Failed: {failed}.";
            if (log.Count > 0) report += "\n\n" + string.Join("\n", log.Take(15));
            TaskDialog.Show("STING QR Stamp — All Sheets", report);
            return Result.Succeeded;
        }
    }
}
