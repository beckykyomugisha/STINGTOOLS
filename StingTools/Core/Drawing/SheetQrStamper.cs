// StingTools — Drawing Template Manager · Sheet QR stamp
//
// WHY THIS FILE EXISTS
// --------------------
// The title-block QR was fully SPECIFIED and never BUILT. Three documents
// described it as shipped:
//
//   * MR_PARAMETERS.txt declared TB_QR_PAYLOAD_TXT, described as "Engine
//     populates with deep link URL to CDE record" — no engine populated it.
//     Zero C# references, in a repo of 1,400 source files.
//   * SLOT_TAXONOMY.md listed a `qr-code` purposeTag gated on
//     PRJ_TB_SHOW_QR_CODE_BOOL — no title-block spec declared either.
//   * TITLE_BLOCK_CREATION_GUIDE.md described "the QR-code stamper" and a
//     cover page whose "QR code links back to the CDE record".
//
// So a reader of this codebase — human or otherwise — would conclude the
// feature existed, and a reader of a produced sheet would find no QR and
// assume a toggle was off. This file is that engine.
//
// WHAT IT DOES, PRECISELY
//   1. Builds the sheet's deep-link URL (StingQrFormat.BuildSheetUrl).
//   2. Writes it to TB_QR_PAYLOAD_TXT on the title-block instance.
//   3. Renders it to a PNG under the project's _data coordination folder.
//   4. Places that PNG on the sheet as an ImageInstance, at the `qr-code` slot
//      when the family declares one, else at a corner fallback.
//
// WHAT IT DOES NOT DO
//   It does not invent a QR when it cannot do the job. A family with no
//   TB_QR_PAYLOAD_TXT parameter, a sheet with no title block, a locked title
//   block and an operator-disabled toggle are each REPORTED, individually, by
//   name. The whole defect class this codebase produces is a no-op that reads
//   as a success, and an unplaced QR on an issued drawing is not recoverable
//   after the drawing leaves.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public sealed class SheetQrResult
    {
        /// <summary>Sheets where the payload was written AND the image placed.</summary>
        public int Stamped { get; set; }
        /// <summary>Payload parameter writes (may exceed Stamped when image
        /// placement is off or the image step failed).</summary>
        public int PayloadsWritten { get; set; }
        public int ImagesPlaced { get; set; }
        public int ImagesReplaced { get; set; }
        /// <summary>Sheets the operator turned the QR off on, via the toggle.</summary>
        public int HiddenByToggle { get; set; }
        /// <summary>Sheets skipped because PRJ_TB_LOCK_BOOL was set.</summary>
        public int LockedSkipped { get; set; }
        public int Failed { get; set; }
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>True when nothing at all happened. Callers MUST branch on this
        /// before reporting success — "0 stamped, 0 failed" is not a good outcome,
        /// it is a run that did nothing, and the two must not read alike.</summary>
        public bool DidNothing => Stamped == 0 && PayloadsWritten == 0 && ImagesPlaced == 0;
    }

    public static class SheetQrStamper
    {
        /// <summary>Every image type this stamper creates carries this prefix. It is
        /// the cleanup contract: the idempotency pass finds and replaces by it, and
        /// touches nothing else on the sheet. Same shape as the "STING VIS - " prefix
        /// in the Visibility Center.</summary>
        public const string ImageNamePrefix = "STING QR - ";

        /// <summary>Printed size of the stamp, mm. Delegates to the planner rather than
        /// restating the number: two copies of one constant is how the sizes drift, and
        /// only the planner's copy is under test.</summary>
        public static double DefaultSizeMm => SheetQrPlacement.DefaultSizeMm;

        /// <summary>Render resolution. 600 px over 20 mm is ~760 dpi — well past
        /// plotter resolution, so the printed edges stay crisp.</summary>
        private const int RenderPx = 600;

        private const double MmPerFoot = 304.8;

        /// <summary>Stamp one or more sheets. Never throws; every failure lands in
        /// <see cref="SheetQrResult.Warnings"/> naming the sheet.</summary>
        /// <param name="placeImage">False writes only TB_QR_PAYLOAD_TXT — useful when
        /// the title block draws the code itself from the payload label.</param>
        public static SheetQrResult Stamp(Document doc, IEnumerable<ViewSheet> sheets, bool placeImage = true)
        {
            var r = new SheetQrResult();
            if (doc == null || sheets == null) return r;

            string projectCode = ResolveProjectCode(doc);
            string qrDir = null;

            foreach (var sheet in sheets)
            {
                if (sheet == null) continue;
                try
                {
                    var tb = FindTitleBlock(doc, sheet);
                    if (tb == null)
                    {
                        r.Warnings.Add($"Sheet '{sheet.SheetNumber}': no title block — nothing to stamp.");
                        r.Failed++;
                        continue;
                    }

                    if (TitleBlockParamApplier.IsTitleBlockLocked(tb, sheet))
                    {
                        r.LockedSkipped++;
                        r.Warnings.Add($"Sheet '{sheet.SheetNumber}': title block locked ({ParamRegistry.TB_LOCK}); left untouched.");
                        continue;
                    }

                    // The operator's opt-out. Absent parameter means the family predates
                    // the toggle — treat as ON and say so once per sheet, rather than
                    // silently deciding either way.
                    var toggle = tb.LookupParameter(ParamRegistry.TB_SHOW_QR_CODE);
                    if (toggle != null && toggle.StorageType == StorageType.Integer && toggle.AsInteger() == 0)
                    {
                        r.HiddenByToggle++;
                        RemoveExistingStamp(doc, sheet, r);   // honour the toggle retroactively
                        continue;
                    }
                    if (toggle == null)
                    {
                        r.Warnings.Add(
                            $"Sheet '{sheet.SheetNumber}': title block has no {ParamRegistry.TB_SHOW_QR_CODE} — " +
                            "stamping anyway. Run TitleBlock_CreateAll to regenerate the family with the toggle.");
                    }

                    string url = StingQrFormat.BuildSheetUrl(projectCode, sheet.SheetNumber, ReadRevision(sheet));

                    // 1 — the payload parameter.
                    var payload = tb.LookupParameter(ParamRegistry.TB_QR_PAYLOAD);
                    bool payloadOk = false;
                    if (payload == null)
                    {
                        r.Warnings.Add(
                            $"Sheet '{sheet.SheetNumber}': title block has no {ParamRegistry.TB_QR_PAYLOAD}; " +
                            "the URL is encoded in the image but not readable as text.");
                    }
                    else if (payload.IsReadOnly)
                    {
                        r.Warnings.Add($"Sheet '{sheet.SheetNumber}': {ParamRegistry.TB_QR_PAYLOAD} is read-only.");
                    }
                    else
                    {
                        payload.Set(url);
                        r.PayloadsWritten++;
                        payloadOk = true;
                    }

                    if (!placeImage)
                    {
                        if (payloadOk) r.Stamped++;
                        continue;
                    }

                    if (qrDir == null)
                    {
                        qrDir = StingPaths.Meta(doc, "_BIM_COORD", "qr", "sheets");
                        Directory.CreateDirectory(qrDir);
                    }

                    string png = Path.Combine(qrDir, SanitiseFileName(sheet.SheetNumber) + ".png");
                    StingQRHelper.SaveQRPng(url, png, RenderPx);

                    if (PlaceStamp(doc, sheet, tb, png, r))
                    {
                        r.ImagesPlaced++;
                        r.Stamped++;
                    }
                    else
                    {
                        r.Failed++;
                    }
                }
                catch (Exception ex)
                {
                    // Log AND count AND name the sheet. A swallowed failure here leaves
                    // a drawing that looks stamped in the summary and is not.
                    StingLog.Error($"SheetQrStamper: sheet '{sheet.SheetNumber}' failed", ex);
                    r.Warnings.Add($"Sheet '{sheet.SheetNumber}': {ex.Message}");
                    r.Failed++;
                }
            }

            StingLog.Info(
                $"SheetQrStamper: stamped {r.Stamped}, payloads {r.PayloadsWritten}, images {r.ImagesPlaced} " +
                $"(replaced {r.ImagesReplaced}), hidden {r.HiddenByToggle}, locked {r.LockedSkipped}, failed {r.Failed}");
            return r;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Image placement
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Create/replace the QR ImageInstance on the sheet. Returns false
        /// (with a warning recorded) rather than throwing.</summary>
        private static bool PlaceStamp(Document doc, ViewSheet sheet, Element tb, string pngPath, SheetQrResult r)
        {
            try
            {
                // Idempotent: a re-run must REPLACE, not accumulate. Stacked QR images
                // are invisible on screen (they overlap exactly) and show up only as a
                // muddy print and a bloated file.
                if (RemoveExistingStamp(doc, sheet, r)) r.ImagesReplaced++;

                var opts = new ImageTypeOptions(pngPath, false, ImageTypeSource.Import);
                var imageType = ImageType.Create(doc, opts);
                // Name carries the prefix so RemoveExistingStamp can find it again.
                try { imageType.Name = ImageNamePrefix + sheet.SheetNumber; }
                catch (Exception ex) { StingLog.Warn($"SheetQrStamper: could not name image type: {ex.Message}"); }

                var (centre, sizeFt) = ResolvePlacement(doc, sheet, tb);
                var instance = ImageInstance.Create(doc, sheet, imageType.Id,
                    new ImagePlacementOptions(centre, BoxPlacement.Center));

                // Size it in sheet space. Setting width alone preserves the aspect
                // ratio, and a QR is square, so height follows.
                var w = instance.get_Parameter(BuiltInParameter.RASTER_SHEETWIDTH);
                if (w != null && !w.IsReadOnly) w.Set(sizeFt);

                return true;
            }
            catch (Exception ex)
            {
                StingLog.Error($"SheetQrStamper: placing image on '{sheet.SheetNumber}' failed", ex);
                r.Warnings.Add($"Sheet '{sheet.SheetNumber}': could not place QR image — {ex.Message}");
                return false;
            }
        }

        /// <summary>Delete any STING QR image already on this sheet. Matches by the
        /// <see cref="ImageNamePrefix"/> contract only — an operator's own placed
        /// images are never touched.</summary>
        private static bool RemoveExistingStamp(Document doc, ViewSheet sheet, SheetQrResult r)
        {
            try
            {
                var doomed = new FilteredElementCollector(doc, sheet.Id)
                    .OfClass(typeof(ImageInstance))
                    .Cast<ImageInstance>()
                    .Where(i =>
                    {
                        var t = doc.GetElement(i.GetTypeId()) as ImageType;
                        return t != null && (t.Name ?? "").StartsWith(ImageNamePrefix, StringComparison.Ordinal);
                    })
                    .Select(i => i.Id)
                    .ToList();

                if (doomed.Count == 0) return false;
                doc.Delete(doomed);
                return true;
            }
            catch (Exception ex)
            {
                r?.Warnings.Add($"Sheet '{sheet.SheetNumber}': could not clear the previous QR — {ex.Message}");
                StingLog.Warn($"SheetQrStamper: RemoveExistingStamp on '{sheet.SheetNumber}': {ex.Message}");
                return false;
            }
        }

        /// <summary>Where the stamp goes, in sheet feet, and how big.
        ///
        /// Prefers a `qr-code` slot declared by the title-block spec. Falls back to
        /// the top-right of the title block's own bounding box — chosen over
        /// bottom-right because the bottom strip is where every STING title block
        /// puts its project-info block, so a fallback there would overlap real
        /// content on a family that simply has no slot yet.</summary>
        /// <summary>Where the stamp goes, in sheet feet, and how big.
        ///
        /// This method now does ONE thing: convert Revit's units and types into
        /// millimetres, hand the decision to <see cref="SheetQrPlacement.Plan"/>, and
        /// convert back. The decision itself is Revit-free and unit-tested
        /// (SheetQrPlacementTests), because that is where this feature's only bug so
        /// far has lived — a slot resolving off the paper is arithmetic, not API.</summary>
        private static (XYZ centre, double sizeFt) ResolvePlacement(Document doc, ViewSheet sheet, Element tb)
        {
            QrRect? slotRect = null;
            try
            {
                var slots = Commands.Drawing.TitleBlockSlotUtils.ReadSlotBoundsFromTitleBlock(doc, tb);
                var slot = slots.Values.FirstOrDefault(s =>
                    string.Equals(s.PurposeTag, "qr-code", StringComparison.OrdinalIgnoreCase));
                if (slot?.Bbox != null) slotRect = ToMm(slot.Bbox);
            }
            catch (Exception ex)
            {
                // A slot we cannot read is not a slot. Fall through to the fallback,
                // having said why.
                StingLog.Warn($"SheetQrStamper: qr-code slot lookup failed for '{sheet.SheetNumber}': {ex.Message}");
            }

            QrRect? tbRect = null;
            try { tbRect = ToMm(tb.get_BoundingBox(sheet)); }
            catch (Exception ex) { StingLog.Warn($"SheetQrStamper: title-block bbox read failed: {ex.Message}"); }

            var plan = SheetQrPlacement.Plan(slotRect, tbRect);

            // Report the fallback AND its reason. "Placed at the slot" and "placed in a
            // corner because the slot was nonsense" must never read alike in the log.
            if (plan.Source != QrPlacementSource.Slot && !string.IsNullOrEmpty(plan.Rejection))
            {
                StingLog.Warn(
                    $"SheetQrStamper: sheet '{sheet.SheetNumber}' fell back to {plan.Source} — " +
                    $"{plan.Rejection}. Fix the slot in STING_TITLE_BLOCKS.json.");
            }

            return (new XYZ(plan.CentreXMm / MmPerFoot, plan.CentreYMm / MmPerFoot, 0),
                    plan.SizeMm / MmPerFoot);
        }

        /// <summary>Revit feet to millimetres, in ONE place, so a unit slip has one
        /// place to live. Returns null for a null box rather than a zero rect — "no
        /// extent" and "an extent of zero" lead to different decisions.</summary>
        private static QrRect? ToMm(BoundingBoxXYZ bb)
        {
            if (bb == null) return null;
            return new QrRect(bb.Min.X * MmPerFoot, bb.Min.Y * MmPerFoot,
                              bb.Max.X * MmPerFoot, bb.Max.Y * MmPerFoot);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Small readers
        // ─────────────────────────────────────────────────────────────────

        private static Element FindTitleBlock(Document doc, ViewSheet sheet)
            => new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstElement();

        /// <summary>Current sheet revision, or null. Reads the native
        /// SHEET_CURRENT_REVISION so the QR agrees with the revision box the
        /// title block prints — rather than a STING mirror of it that can drift.</summary>
        private static string ReadRevision(ViewSheet sheet)
        {
            try
            {
                var p = sheet.get_Parameter(BuiltInParameter.SHEET_CURRENT_REVISION);
                var v = p?.AsString();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetQrStamper: revision read on '{sheet.SheetNumber}': {ex.Message}");
                return null;
            }
        }

        internal static string ResolveProjectCode(Document doc)
        {
            string code = null;
            try { code = ParameterHelpers.GetString(doc?.ProjectInformation, ParamRegistry.ORG_PROJECT_CODE); }
            catch (Exception ex) { StingLog.Warn($"SheetQrStamper: project code read: {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(code)) code = doc?.ProjectInformation?.Number;
            return string.IsNullOrWhiteSpace(code) ? "PRJ" : code;
        }

        private static string SanitiseFileName(string value)
        {
            var chars = (value ?? "").ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            var name = new string(chars).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "sheet" : name;
        }
    }
}
