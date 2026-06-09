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
        /// One-call orchestrator for combined / per-sheet post-processing, run on the
        /// verified temp PDF before the atomic move. Order matters: bookmarks map to
        /// page objects, then the watermark stamps content pages, then the cover sheet
        /// is prepended last (so it stays clean and page-object bookmark refs survive).
        /// </summary>
        internal static void PostProcessExport(string pdfPath, List<View> pageViews,
            ExportProfile profile, ExportRunResult result)
        {
            if (profile?.Pdf == null) return;
            if (profile.Pdf.AddBookmarks)
                Safe(() => InjectBookmarks(pdfPath, pageViews, profile), result, "Bookmark", pdfPath);
            if (profile.Pdf.ApplyWatermark)
                Safe(() => InjectWatermark(pdfPath, profile.Pdf, pageViews), result, "Watermark", pdfPath);
            if (profile.Pdf.PrependCoverSheet && pageViews != null && pageViews.Count > 1)
                Safe(() => PrependRegisterCover(pdfPath, pageViews, profile), result, "Cover sheet", pdfPath);
        }

        private static void Safe(Action a, ExportRunResult result, string what, string path)
        {
            try { a(); }
            catch (Exception ex)
            { result?.Warnings.Add($"{what} failed for '{Path.GetFileName(path)}': {ex.Message}"); }
        }

        /// <summary>Back-compat single-text overload — stamps the same text on every page.</summary>
        internal static void InjectWatermark(string pdfPath, PdfExportSettings pdf)
            => InjectWatermark(pdfPath, pdf, null);

        /// <summary>
        /// Stamp each page with the configured watermark — diagonal centre by default,
        /// optionally tiled across the page. When <paramref name="pageViews"/> is
        /// supplied, the text is resolved per page (token substitution + per-suitability
        /// auto-phrase), so a 50-sheet combined PDF can carry "FOR CONSTRUCTION" on the
        /// S4 sheets and "PRELIMINARY" on the S1 sheets in one pass.
        /// </summary>
        internal static void InjectWatermark(string pdfPath, PdfExportSettings pdf, List<View> pageViews)
        {
            if (!File.Exists(pdfPath) || pdf == null) return;
            // With auto-by-suitability the base text may be empty; that's fine.
            if (string.IsNullOrEmpty(pdf.WatermarkText) && !pdf.AutoWatermarkBySuitability) return;

            try
            {
                using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

                XColor colour = ParseHexColour(pdf.WatermarkColourHex, 0x99, 0x99, 0x99);
                colour.A = Math.Clamp(pdf.WatermarkOpacityPct, 0, 100) / 100.0;
                var brush = new XSolidBrush(colour);
                int fontSize = Math.Max(24, pdf.WatermarkFontSize);
                var font = new XFont("Arial", fontSize, XFontStyleEx.Bold);

                for (int i = 0; i < doc.Pages.Count; i++)
                {
                    var page = doc.Pages[i];
                    View v = (pageViews != null && i < pageViews.Count) ? pageViews[i] : null;
                    string text = ResolveWatermarkText(pdf, v);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    double w = page.Width.Point, h = page.Height.Point;
                    var size = gfx.MeasureString(text, font);

                    gfx.Save();
                    if (pdf.WatermarkTile)
                    {
                        gfx.TranslateTransform(w / 2, h / 2);
                        gfx.RotateTransform(-30);
                        double diag = Math.Sqrt(w * w + h * h);
                        double stepX = size.Width + fontSize * 2.5;
                        double stepY = fontSize * 3.0;
                        for (double y = -diag / 2; y < diag / 2; y += stepY)
                            for (double x = -diag / 2; x < diag / 2; x += stepX)
                                gfx.DrawString(text, font, brush, new XPoint(x, y));
                    }
                    else switch (pdf.WatermarkPosition)
                    {
                        case "TopLeft":
                            gfx.DrawString(text, font, brush, new XPoint(20, 20 + size.Height));
                            break;
                        case "BottomRight":
                            gfx.DrawString(text, font, brush, new XPoint(w - size.Width - 20, h - 20));
                            break;
                        default: // DiagonalCentre
                            gfx.TranslateTransform(w / 2, h / 2);
                            gfx.RotateTransform(-30);
                            gfx.DrawString(text, font, brush, new XPoint(-size.Width / 2, size.Height / 2));
                            break;
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
                using var d = PdfReader.Open(pdfPath, PdfDocumentOpenMode.InformationOnly);
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

        /// <summary>
        /// Prepend an auto-generated drawing-register cover page to a combined PDF:
        /// title + project + a Number / Title / Rev table of the contained sheets.
        /// Inserted last so content-page bookmark refs and watermarks are unaffected.
        /// </summary>
        private static void PrependRegisterCover(string pdfPath, List<View> views, ExportProfile profile)
        {
            if (!File.Exists(pdfPath) || views == null || views.Count == 0) return;

            using var doc = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
            if (doc.PageCount == 0) return;

            double w = doc.Pages[0].Width.Point;
            double h = doc.Pages[0].Height.Point;

            var cover = doc.InsertPage(0);
            cover.Width  = XUnit.FromPoint(w);
            cover.Height = XUnit.FromPoint(h);

            using var gfx = XGraphics.FromPdfPage(cover);
            var black = XBrushes.Black;
            var titleFont = new XFont("Arial", 28, XFontStyleEx.Bold);
            var subFont   = new XFont("Arial", 11, XFontStyleEx.Regular);
            var headFont  = new XFont("Arial", 11, XFontStyleEx.Bold);
            var rowFont   = new XFont("Arial", 10, XFontStyleEx.Regular);

            const double margin = 40;
            double y = margin + 20;
            gfx.DrawString("DRAWING REGISTER", titleFont, black, new XPoint(margin, y));
            y += 26;

            string proj = views[0]?.Document?.ProjectInformation?.Name ?? "";
            gfx.DrawString($"{proj}     {views.Count} drawing(s)     {DateTime.Now:yyyy-MM-dd}",
                subFont, black, new XPoint(margin, y));
            y += 28;

            double cNum = margin, cTitle = margin + 150, cRev = w - margin - 60;
            gfx.DrawString("Number", headFont, black, new XPoint(cNum, y));
            gfx.DrawString("Title",  headFont, black, new XPoint(cTitle, y));
            gfx.DrawString("Rev",    headFont, black, new XPoint(cRev, y));
            y += 6;
            gfx.DrawLine(new XPen(XColors.Black, 0.75), margin, y, w - margin, y);
            y += 14;

            const double rowH = 15;
            double bottom = h - margin;
            int shown = 0;
            foreach (var v in views)
            {
                if (y + rowH > bottom)
                {
                    gfx.DrawString($"… and {views.Count - shown} more", rowFont, black, new XPoint(cNum, y));
                    break;
                }
                var tok = ExportCenterEngine.BuildTokenContext(v.Document, v);
                string num   = tok.GetValueOrDefault("SheetNumber", "");
                string title = tok.GetValueOrDefault("SheetTitle", v?.Name ?? "");
                string rev   = tok.GetValueOrDefault("Revision", "");
                if (title.Length > 60) title = title.Substring(0, 58) + "…";
                gfx.DrawString(num,   rowFont, black, new XPoint(cNum, y));
                gfx.DrawString(title, rowFont, black, new XPoint(cTitle, y));
                gfx.DrawString(rev,   rowFont, black, new XPoint(cRev, y));
                y += rowH; shown++;
            }

            doc.Save(pdfPath);
        }

        /// <summary>Resolve the watermark text for one page: per-suitability auto-phrase
        /// (when enabled) and {token} substitution from the sheet's context.</summary>
        private static string ResolveWatermarkText(PdfExportSettings pdf, View v)
        {
            string text = pdf.WatermarkText ?? "";
            if (v != null)
            {
                try
                {
                    var tok = ExportCenterEngine.BuildTokenContext(v.Document, v);
                    if (pdf.AutoWatermarkBySuitability)
                        text = SuitabilityPhrase(tok.GetValueOrDefault("Suitability", ""));
                    text = text
                        .Replace("{Suitability}", tok.GetValueOrDefault("Suitability", ""))
                        .Replace("{Revision}",    tok.GetValueOrDefault("Revision", ""))
                        .Replace("{SheetNumber}", tok.GetValueOrDefault("SheetNumber", ""))
                        .Replace("{Discipline}",  tok.GetValueOrDefault("Discipline", ""))
                        .Replace("{Date}",        DateTime.Now.ToString("yyyy-MM-dd"));
                }
                catch (Exception ex) { StingLog.Warn($"Watermark text resolve: {ex.Message}"); }
            }
            return string.IsNullOrWhiteSpace(text) ? (pdf.WatermarkText ?? "") : text;
        }

        /// <summary>Map an ISO 19650 suitability code to a drawing-stamp phrase.</summary>
        private static string SuitabilityPhrase(string suit)
        {
            switch ((suit ?? "").Trim().ToUpperInvariant())
            {
                case "WIP": case "S0": case "S1": return "PRELIMINARY";
                case "S2": return "FOR INFORMATION";
                case "S3": return "FOR REVIEW & COMMENT";
                case "S4": return "FOR STAGE APPROVAL";
                case "S6": case "S7": return "FOR PIM / AIM AUTHORISATION";
                case "A1": case "A2": case "A3": case "AB":
                case "B1": case "B2": case "B3": return "FOR CONSTRUCTION";
                case "CR": return "AS BUILT";
                default: return string.IsNullOrEmpty(suit) ? "DRAFT" : suit.ToUpperInvariant();
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
