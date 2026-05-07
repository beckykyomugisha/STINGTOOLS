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
    /// Creates a drafting view containing one NFPA 70E warning label per
    /// panel that <see cref="ArcFlashCommand"/> assessed. Each label is a
    /// FilledRegion border + TextNote pair laid out 5 per row at 110 mm
    /// column pitch (paper-side units, drafting-view scale 1:1).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArcFlashLabelSheetCommand : IExternalCommand
    {
        private const double LabelWidthMm  = 100;
        private const double LabelHeightMm = 60;
        private const double MarginMm      = 5;
        private const double RowSpacingMm  = 70;
        private const double ColWidthMm    = 110;
        private const int    LabelsPerRow  = 5;
        private const string DrawingTypeId = "elec-arc-flash-labels";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            var rows = ArcFlashCommand.LastResults;
            if (rows == null || rows.Count == 0)
            {
                TaskDialog.Show("STING Arc Flash Labels",
                    "No arc-flash results found. Run Arc Flash Calc first.");
                return Result.Cancelled;
            }

            ViewDrafting view = null;
            using (var tx = new Transaction(doc, "STING Arc Flash Label Sheet"))
            {
                tx.Start();
                var dvft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.Drafting);
                if (dvft == null) { tx.RollBack(); message = "No drafting view family type found."; return Result.Failed; }

                view = ViewDrafting.Create(doc, dvft.Id);
                try { view.Name = $"STING - Arc Flash Labels - {DateTime.Now:yyyyMMdd-HHmm}"; } catch { }

                var solidFill = ParameterHelpers.GetSolidFillPattern(doc);
                var frt = new FilteredElementCollector(doc)
                    .OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().FirstOrDefault();
                var textType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault();

                double Ft(double mm) => mm / 304.8;
                int col = 0, row = 0;
                foreach (var r in rows.OrderByDescending(x => x.PpeCategory))
                {
                    double x = col * Ft(ColWidthMm);
                    double y = -row * Ft(RowSpacingMm);
                    var origin = new XYZ(x, y, 0);
                    Color borderColor = r.PpeCategory switch
                    {
                        < 0 => new Color(180, 0, 0),
                        4   => new Color(220, 50, 50),
                        3   => new Color(220, 120, 0),
                        2   => new Color(220, 180, 0),
                        _   => new Color(180, 160, 0)
                    };
                    if (frt != null)
                        DrawLabelBorder(doc, view, origin, Ft(LabelWidthMm), Ft(LabelHeightMm), frt, solidFill, borderColor);

                    if (textType != null)
                    {
                        try
                        {
                            var pos = new XYZ(x + Ft(MarginMm), y - Ft(MarginMm), 0);
                            TextNote.Create(doc, view.Id, pos, r.LabelText, textType.Id);
                        }
                        catch (Exception ex) { StingLog.Warn($"TextNote: {ex.Message}"); }
                    }
                    col++;
                    if (col >= LabelsPerRow) { col = 0; row++; }
                }

                StampDrawingType(view);
                tx.Commit();
            }
            try { ctx.UIDoc.ActiveView = view; } catch { }
            try { ComplianceScan.InvalidateCache(); } catch { }
            TaskDialog.Show("STING Arc Flash Labels",
                $"Created drafting view '{view?.Name}' with {rows.Count} label(s).");
            return Result.Succeeded;
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

        private static void StampDrawingType(View v)
        {
            try
            {
                var t = Type.GetType("StingTools.Core.Drawing.DrawingTypeStamper");
                t?.GetMethod("Stamp", new[] { typeof(Element), typeof(string) })
                  ?.Invoke(null, new object[] { v, DrawingTypeId });
            }
            catch (Exception ex) { StingLog.Warn($"StampDrawingType: {ex.Message}"); }
        }
    }
}
