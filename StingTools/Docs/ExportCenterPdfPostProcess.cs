using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using StingTools.Core;

namespace StingTools.Docs
{
    // ════════════════════════════════════════════════════════════════════════════
    //  ExportCenterPdfPostProcess — bookmark + watermark injection for the
    //  combined-PDF outputs produced by ExportCenterEngine.RunPdf.
    //
    //  Backed by PDFsharp 6.x (added to StingTools.csproj). Each method opens
    //  the PDF in Modify mode, mutates it, and saves over the same path.
    //  Failures are logged but never thrown — post-processing is best-effort.
    // ════════════════════════════════════════════════════════════════════════════

    internal static class ExportCenterPdfPostProcess
    {
        /// <summary>
        /// Insert a document outline (bookmark tree) into a combined PDF whose
        /// pages match <paramref name="views"/> in order. Honours
        /// <c>NestBookmarksByDiscipline</c> and <c>NestBookmarksByLevel</c>.
        /// </summary>
        internal static void InjectBookmarks(string pdfPath, List<View> views, ExportProfile profile)
        {
            if (!File.Exists(pdfPath) || views == null || views.Count == 0) return;

            try
            {
                using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
                int pageCount = doc.PageCount;
                if (pageCount == 0) return;

                bool nestDisc  = profile.Pdf.NestBookmarksByDiscipline;
                bool nestLevel = profile.Pdf.NestBookmarksByLevel;
                string template = string.IsNullOrEmpty(profile.Pdf.BookmarkLabelTemplate)
                    ? "{SheetNumber} - {SheetTitle}"
                    : profile.Pdf.BookmarkLabelTemplate;

                // Group lookups so each parent appears once.
                var discParents  = new Dictionary<string, PdfOutline>(StringComparer.OrdinalIgnoreCase);
                var levelParents = new Dictionary<string, PdfOutline>(StringComparer.OrdinalIgnoreCase);

                int bound = Math.Min(pageCount, views.Count);
                for (int i = 0; i < bound; i++)
                {
                    var v = views[i];
                    var page = doc.Pages[i];

                    string disc  = v is ViewSheet s ? ExportCenterEngine.GetDisciplinePrefix(s.SheetNumber) : "";
                    string level = ResolveLevelLabel(v);
                    string label = ResolveTemplate(template, v, disc, level);

                    // PdfSharp: doc.Outlines is a PdfOutlineCollection; each
                    // PdfOutline node carries its own .Outlines collection
                    // for children. We track the current parent collection
                    // and only descend when nesting is enabled.
                    PdfOutlineCollection hostCol = doc.Outlines;

                    if (nestDisc && !string.IsNullOrEmpty(disc))
                    {
                        if (!discParents.TryGetValue(disc, out var dp))
                        {
                            dp = hostCol.Add(disc, page, true);
                            discParents[disc] = dp;
                        }
                        hostCol = dp.Outlines;
                    }

                    if (nestLevel && !string.IsNullOrEmpty(level))
                    {
                        string key = (nestDisc ? disc + "::" : "") + level;
                        if (!levelParents.TryGetValue(key, out var lp))
                        {
                            lp = hostCol.Add(level, page, true);
                            levelParents[key] = lp;
                        }
                        hostCol = lp.Outlines;
                    }

                    hostCol.Add(label, page, true);
                }

                // Document properties
                if (doc.Info != null)
                {
                    var titleTok  = ResolveTemplate(profile.Pdf.DocumentTitleTemplate,  views[0], "", "");
                    var authorTok = ResolveTemplate(profile.Pdf.DocumentAuthorTemplate, views[0], "", "");
                    var subjTok   = ResolveTemplate(profile.Pdf.DocumentSubjectTemplate, views[0], "", "");
                    var kwTok     = ResolveTemplate(profile.Pdf.DocumentKeywordsTemplate, views[0], "", "");
                    if (!string.IsNullOrWhiteSpace(titleTok))  doc.Info.Title    = titleTok;
                    if (!string.IsNullOrWhiteSpace(authorTok)) doc.Info.Author   = authorTok;
                    if (!string.IsNullOrWhiteSpace(subjTok))   doc.Info.Subject  = subjTok;
                    if (!string.IsNullOrWhiteSpace(kwTok))     doc.Info.Keywords = kwTok;
                    doc.Info.Creator = "STING Export Centre";
                }

                doc.Save(pdfPath);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InjectBookmarks {Path.GetFileName(pdfPath)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Stamp every page with the configured watermark — diagonal centre by
        /// default. Honours opacity (0–100) and font size.
        /// </summary>
        internal static void InjectWatermark(string pdfPath, PdfExportSettings pdf)
        {
            if (!File.Exists(pdfPath) || string.IsNullOrEmpty(pdf?.WatermarkText)) return;

            try
            {
                using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

                XColor colour = ParseHexColour(pdf.WatermarkColourHex, 0x99, 0x99, 0x99);
                colour.A = Math.Clamp(pdf.WatermarkOpacityPct, 0, 100) / 100.0;

                var brush = new XSolidBrush(colour);
                int baseFontSize = Math.Max(24, pdf.WatermarkFontSize);
                var cornerFont = new XFont("Arial", baseFontSize, XFontStyleEx.Bold);

                foreach (PdfPage page in doc.Pages)
                {
                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

                    double w = page.Width.Point;
                    double h = page.Height.Point;

                    gfx.Save();
                    switch (pdf.WatermarkPosition)
                    {
                        case "TopLeft":
                        {
                            var size = gfx.MeasureString(pdf.WatermarkText, cornerFont);
                            gfx.DrawString(pdf.WatermarkText, cornerFont, brush,
                                new XPoint(20, 20 + size.Height));
                            break;
                        }
                        case "BottomRight":
                        {
                            var size = gfx.MeasureString(pdf.WatermarkText, cornerFont);
                            gfx.DrawString(pdf.WatermarkText, cornerFont, brush,
                                new XPoint(w - size.Width - 20, h - 20));
                            break;
                        }
                        default: // DiagonalCentre — large, dead-centre, corner-to-corner
                        {
                            // Scale the text so it spans ~70% of the page diagonal,
                            // then centre it exactly on the page centre via
                            // XStringFormats.Center. Centre is invariant under the
                            // page's /Rotate (which pivots about the same centre),
                            // so this reads centred whatever the sheet orientation.
                            double diag = Math.Sqrt(w * w + h * h);
                            var probe = gfx.MeasureString(pdf.WatermarkText, cornerFont);
                            int big = baseFontSize;
                            if (probe.Width > 1)
                                big = (int)Math.Clamp(
                                    baseFontSize * (diag * 0.70) / probe.Width, baseFontSize, 400);
                            var bigFont = new XFont("Arial", big, XFontStyleEx.Bold);

                            double angleDeg = -Math.Atan2(h, w) * 180.0 / Math.PI; // corner-to-corner
                            gfx.TranslateTransform(w / 2.0, h / 2.0);
                            gfx.RotateTransform(angleDeg);
                            gfx.DrawString(pdf.WatermarkText, bigFont, brush,
                                new XPoint(0, 0), XStringFormats.Center);
                            break;
                        }
                    }
                    gfx.Restore();
                }

                doc.Save(pdfPath);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"InjectWatermark {Path.GetFileName(pdfPath)}: {ex.Message}");
            }
        }

        /// <summary>
        /// Output-verification probe used by the robust export path: opens the
        /// PDF read-only and reports its page count. A file that fails to open is
        /// corrupt; 0 pages means an empty render. Returns false (with a reason)
        /// in either case so the engine can fail the row rather than silently
        /// publishing a bad drawing.
        /// </summary>
        internal static bool TryReadPdfInfo(string pdfPath, out int pageCount, out string error)
        {
            pageCount = 0; error = null;
            try
            {
                // InformationOnly is deprecated in PDFsharp 6 (never implemented);
                // Import opens read-only for inspection and exposes PageCount.
                using var d = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                pageCount = d.PageCount;
                if (pageCount <= 0) { error = "PDF reports 0 pages"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = "PDF failed to open (corrupt): " + ex.Message;
                return false;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private static string ResolveLevelLabel(View v)
        {
            try
            {
                var lvl = v?.GenLevel ?? null;
                return lvl?.Name ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; }
        }

        private static string ResolveTemplate(string template, View v, string disc, string level)
        {
            if (string.IsNullOrEmpty(template)) return v?.Name ?? "";
            return template
                .Replace("{SheetNumber}", v is ViewSheet s ? s.SheetNumber ?? "" : "")
                .Replace("{SheetTitle}",  v?.Name ?? "")
                .Replace("{DrawingNumber}", v is ViewSheet s2 ? s2.SheetNumber ?? "" : "")
                .Replace("{DrawingTitle}",  v?.Name ?? "")
                .Replace("{Discipline}", disc ?? "")
                .Replace("{Level}", level ?? "")
                .Replace("{ProjectName}", v?.Document?.ProjectInformation?.Name ?? "")
                .Replace("{DrawingSet}", "");
        }

        private static XColor ParseHexColour(string hex, byte rDef, byte gDef, byte bDef)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return XColor.FromArgb(rDef, gDef, bDef);
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    return XColor.FromArgb(
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16));
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return XColor.FromArgb(rDef, gDef, bDef);
        }
    }
}
