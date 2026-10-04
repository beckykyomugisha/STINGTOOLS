using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.ArcFlash
{
    /// <summary>
    /// Creates a drafting view containing one indicative arc-flash label per
    /// panel that <see cref="ArcFlashCommand"/> calculated (IEEE 1584-2018 —
    /// every label text carries <see cref="ArcFlashEngine.Basis"/>). Each label is a
    /// white body, an ANSI Z535.4 signal-word header strip (WARNING black on orange;
    /// DANGER white on red above the configured incident energy) led by the ANSI Z535
    /// safety-alert triangle drawn as filled regions (<see cref="ArcSafetyAlertSymbol"/>,
    /// DSCH-32 — no font glyph), and the label text,
    /// laid out 5 per row at 110 mm column pitch (paper-side units, drafting-view
    /// scale 1:1). Header colours and the DANGER threshold come only from
    /// STING_ARC_FLASH_PPE.json (DSCH-25); if it does not load, no sheet is drawn.
    /// Every label carries a DRAFT line: these are not labels for posting.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArcFlashLabelSheetCommand : IExternalCommand
    {
        private const double LabelWidthMm  = 100;
        private const double LabelHeightMm = 115;  // header strip + label text (~13 lines) + DRAFT line
        private const double HeaderHeightMm = 12;
        private const double MarginMm      = 5;
        private const double SymbolLeftMm  = 3;    // safety-alert triangle inset from the label edge
        private const double SymbolGapMm   = 2;    // triangle to signal word
        private const double RowSpacingMm  = 125;
        private const double ColWidthMm    = 110;
        private const int    LabelsPerRow  = 5;
        private const string ViewName      = "STING - Arc Flash Labels (" + ArcFlashEngine.BasisShort + ")";
        public const string DraftLine =
            "DRAFT — NOT FOR POSTING WITHOUT A LICENSED ARC-FLASH STUDY. Does not specify PPE.";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var rows = ArcFlashCommand.LastResults;
            if (rows == null || rows.Count == 0)
            {
                PresetDialog.Show("STING Arc Flash Labels",
                    "No arc-flash results found. Run Arc Flash Calc first.", ref message);
                return Result.Cancelled;
            }

            var presentation = ArcFlashPresentation.Current;
            if (!presentation.Loaded)
            {
                message = "Arc-flash label headers not drawn: " + presentation.LoadError;
                PresetDialog.Show("STING Arc Flash Labels", message, ref message);
                return Result.Failed;
            }

            // The drawing type routing gives E / ARC_FLASH_LABELS. The id this stamped,
            // "elec-arc-flash-labels", used to be in no drawing type.
            var req = StingTools.Core.Drawing.DrawingRouteRequests.ArcFlashLabels;
            string drawingTypeId = StingTools.Core.Drawing.DrawingRouteResolver.IdFor(doc, req);

            ViewDrafting view = null;
            using (var tx = new Transaction(doc, "STING Arc Flash Label Sheet"))
            {
                tx.Start();
                // One view, redrawn on every run (a timestamped name left one per run).
                view = StingTools.Core.Drawing.SchematicViewFactory.CreateOrReplace(doc, ViewName, out string viewError);
                if (view == null)
                {
                    tx.RollBack();
                    message = "Could not make the drafting view: " + viewError;
                    if (!PresetDialog.Quiet) TaskDialog.Show("STING Arc Flash Labels", message);
                    return Result.Failed;
                }

                var solidFill = ParameterHelpers.GetSolidFillPattern(doc);
                var frt = new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().FirstOrDefault();
                var textType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();

                double Ft(double mm) => mm / 304.8;
                int col = 0, row = 0;
                var headerText = new Dictionary<string, TextNoteType>();
                foreach (var r in rows.OrderByDescending(x => x.IncidentEnergy_CalCm2))
                {
                    double x = col * Ft(ColWidthMm);
                    double y = -row * Ft(RowSpacingMm);
                    var origin = new XYZ(x, y, 0);
                    var header = presentation.HeaderFor(r.IncidentEnergy_CalCm2);
                    // Safety-alert triangle at the left of the header strip, in the signal-word
                    // text colour; the exclamation mark in the header background colour.
                    var alert = ArcSafetyAlertSymbol.Compute(SymbolLeftMm, HeaderHeightMm, SymbolGapMm);
                    XYZ P(ArcPt p) => new XYZ(x + Ft(p.X), y + Ft(p.Y), 0);
                    var bg = new Color(header.Background.R, header.Background.G, header.Background.B);
                    var fg = new Color(header.Text.R, header.Text.G, header.Text.B);
                    if (frt != null)
                    {
                        DrawLabelBorder(doc, view, origin, Ft(LabelWidthMm), Ft(LabelHeightMm), frt, solidFill, new Color(255, 255, 255));
                        DrawLabelBorder(doc, view, origin, Ft(LabelWidthMm), Ft(HeaderHeightMm), frt, solidFill, bg);
                        DrawFilledPolygon(doc, view, alert.Triangle.Select(P).ToList(), frt, solidFill, fg);
                        DrawFilledPolygon(doc, view, alert.Bar.Select(P).ToList(), frt, solidFill, bg);
                        DrawFilledCircle(doc, view, P(alert.DotCentre), Ft(alert.DotRadius), frt, solidFill, bg);
                    }
                    else StingLog.Warn("Arc flash labels: no filled region type — header strip and safety-alert symbol not drawn.");

                    if (textType != null)
                    {
                        try
                        {
                            var hdrType = HeaderTextType(doc, textType, header, headerText);
                            TextNote.Create(doc, view.Id, new XYZ(x + Ft(alert.TextStartX), y - Ft(3), 0),
                                header.SignalWord, hdrType.Id);
                            var pos = new XYZ(x + Ft(MarginMm), y - Ft(HeaderHeightMm + MarginMm), 0);
                            TextNote.Create(doc, view.Id, pos, r.LabelText + "\n" + DraftLine, textType.Id);
                        }
                        catch (Exception ex2) { StingLog.Warn($"TextNote: {ex2.Message}"); }
                    }
                    col++;
                    if (col >= LabelsPerRow) { col = 0; row++; }
                }

                StampDrawingType(view, drawingTypeId);
                tx.Commit();
            }

            // Onto its drawing type's sheet (found again by stamp; a re-run's labels replace these).
            string sheetLine = StingTools.Core.SLD.SldSheetPlacement.Place(doc, req, view);

            if (!PresetDialog.Quiet)
            {
                try { ctx.UIDoc.ActiveView = view; } catch (Exception ex) { StingLog.Warn($"Arc flash labels: activate view: {ex.Message}"); }
            }
            try { ComplianceScan.InvalidateCache(); } catch (Exception ex) { StingLog.Warn($"Arc flash labels: compliance cache: {ex.Message}"); }
            PresetDialog.Show("STING Arc Flash Labels",
                $"Drafting view '{view?.Name}' drawn with {rows.Count} label(s).\n\n" +
                $"Basis: {ArcFlashEngine.Basis}. Panels that could not be calculated have no label.\n\n" + sheetLine, ref message);
            return Result.Succeeded;
        }

        /// <summary>A text type in the header's ANSI Z535.4 text colour (black on WARNING,
        /// white on DANGER), duplicated once per signal word from the base type.</summary>
        private static TextNoteType HeaderTextType(Document doc, TextNoteType baseType, ArcLabelHeader header,
            Dictionary<string, TextNoteType> cache)
        {
            if (cache.TryGetValue(header.SignalWord, out var t)) return t;
            string name = "STING Arc Flash Header - " + header.SignalWord;
            t = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                    .FirstOrDefault(x => x.Name == name);
            try
            {
                if (t == null) t = baseType.Duplicate(name) as TextNoteType;
                var colour = t?.get_Parameter(BuiltInParameter.LINE_COLOR);
                if (colour != null && !colour.IsReadOnly)
                    colour.Set(header.Text.R + header.Text.G * 256 + header.Text.B * 65536);
            }
            catch (Exception ex) { StingLog.Warn($"Arc flash header text type '{name}': {ex.Message}"); }
            t = t ?? baseType;
            cache[header.SignalWord] = t;
            return t;
        }

        private static void DrawLabelBorder(Document doc, View view, XYZ origin,
            double w, double h, FilledRegionType frt, FillPatternElement fill, Color color)
        {
            try
            {
                var pts = new[]
                {
                    origin,
                    new XYZ(origin.X + w, origin.Y, 0),
                    new XYZ(origin.X + w, origin.Y - h, 0),
                    new XYZ(origin.X, origin.Y - h, 0)
                };
                var loop = new CurveLoop();
                for (int i = 0; i < pts.Length; i++)
                    loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Length]));
                if (!loop.IsCounterclockwise(XYZ.BasisZ))
                    loop = CurveLoop.CreateViaTransform(loop,
                        Transform.CreateReflection(Plane.CreateByNormalAndOrigin(XYZ.BasisX, origin)));

                var fr = FilledRegion.Create(doc, frt.Id, view.Id, new List<CurveLoop> { loop });
                if (fr != null && fill != null)
                {
                    var ogs = new OverrideGraphicSettings();
                    ogs.SetSurfaceForegroundPatternId(fill.Id);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    view.SetElementOverrides(fr.Id, ogs);
                }
            }
            catch (Exception ex) { StingLog.Warn($"DrawLabelBorder: {ex.Message}"); }
        }

        /// <summary>A solid filled region on a counter-clockwise polygon (see
        /// <see cref="ArcSafetyAlertSymbol"/>, which returns them that way).</summary>
        private static void DrawFilledPolygon(Document doc, View view, IList<XYZ> pts,
            FilledRegionType frt, FillPatternElement fill, Color color)
        {
            try
            {
                var loop = new CurveLoop();
                for (int i = 0; i < pts.Count; i++)
                    loop.Append(Line.CreateBound(pts[i], pts[(i + 1) % pts.Count]));
                ColourRegion(view, FilledRegion.Create(doc, frt.Id, view.Id, new List<CurveLoop> { loop }), fill, color);
            }
            catch (Exception ex) { StingLog.Warn($"Arc flash safety-alert symbol: {ex.Message}"); }
        }

        private static void DrawFilledCircle(Document doc, View view, XYZ c, double r,
            FilledRegionType frt, FillPatternElement fill, Color color)
        {
            try
            {
                var loop = new CurveLoop();
                loop.Append(Arc.Create(c, r, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                loop.Append(Arc.Create(c, r, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
                ColourRegion(view, FilledRegion.Create(doc, frt.Id, view.Id, new List<CurveLoop> { loop }), fill, color);
            }
            catch (Exception ex) { StingLog.Warn($"Arc flash safety-alert dot: {ex.Message}"); }
        }

        private static void ColourRegion(View view, FilledRegion fr, FillPatternElement fill, Color color)
        {
            if (fr == null || fill == null) return;
            var ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternId(fill.Id);
            ogs.SetSurfaceForegroundPatternColor(color);
            ogs.SetProjectionLineColor(color);
            view.SetElementOverrides(fr.Id, ogs);
        }

        private static void StampDrawingType(View v, string drawingTypeId)
        {
            try
            {
                StingTools.Core.Drawing.DrawingTypeStamper.Stamp(v, drawingTypeId);
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }
    }
}
