// StingTools — Drawing Template Manager · A-2
//
// ONE definition of "how big is this element" for the annotation rule field
// minSizeMm. Before this, MepAnnotator measured a duct by Width and
// MEPDimensioner by max(Width, Height), and AutoTag did not measure at all.
//
//   MEPCurve (pipe, duct, conduit, cable tray, flex) -> SECTION size:
//       round -> diameter; rectangular / oval -> max(width, height).
//       This is what "don't annotate anything under DN50" means.
//   Any other element with a LocationCurve (wall, beam, grid-hosted run)
//       -> curve LENGTH, matching ElementDimensioner's wall gate.
//   Everything else -> the larger PLAN extent of its bounding box in the view.
//
// 0 means "could not be measured"; AnnotationMinSize.Keeps keeps those and
// callers count them.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Drawing.Dimensioning
{
    internal static class ElementSize
    {
        /// <summary>Section size of an MEP curve in feet (0 when unreadable).</summary>
        public static double SectionFt(MEPCurve e)
        {
            if (e == null) return 0;
            try
            {
                if (e is Pipe p) return p.Diameter;
                if (e is Duct d)
                {
                    // A round duct throws on Width/Height; its Diameter is the size.
                    var dia = d.LookupParameter("Diameter");
                    if (dia != null && dia.StorageType == StorageType.Double && dia.HasValue && dia.AsDouble() > 0)
                        return dia.AsDouble();
                    return Math.Max(d.Width, d.Height);
                }
                var diaP = e.LookupParameter("Diameter");
                if (diaP != null && diaP.StorageType == StorageType.Double && diaP.AsDouble() > 0) return diaP.AsDouble();
                double w = 0, h = 0;
                var wP = e.LookupParameter("Width");
                if (wP != null && wP.StorageType == StorageType.Double) w = wP.AsDouble();
                var hP = e.LookupParameter("Height");
                if (hP != null && hP.StorageType == StorageType.Double) h = hP.AsDouble();
                return Math.Max(w, h);
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("ElementSize.SectionFt", $"ElementSize.SectionFt({e.Id}): {ex.Message}");
                return 0;
            }
        }

        /// <summary>Size in feet by the rules in the file header (0 when unmeasurable).</summary>
        public static double MeasureFt(Element e, View view)
        {
            if (e == null) return 0;
            if (e is MEPCurve mc) return SectionFt(mc);
            try
            {
                if (e.Location is LocationCurve lc && lc.Curve != null) return lc.Curve.Length;
                var bb = e.get_BoundingBox(view) ?? e.get_BoundingBox(null);
                if (bb == null) return 0;
                return Math.Max(Math.Abs(bb.Max.X - bb.Min.X), Math.Abs(bb.Max.Y - bb.Min.Y));
            }
            catch (Exception ex)
            {
                StingLog.WarnRateLimited("ElementSize.MeasureFt", $"ElementSize.MeasureFt({e.Id}): {ex.Message}");
                return 0;
            }
        }

        /// <summary>minSizeMm gate on a measured element.</summary>
        public static bool Keeps(Element e, View view, double? minSizeMm, out bool unmeasured)
        {
            unmeasured = false;
            if (!minSizeMm.HasValue || minSizeMm.Value <= 0) return true;
            double ft = MeasureFt(e, view);
            unmeasured = ft <= 0;
            return AnnotationMinSize.Keeps(ft * AnnotationMinSize.MmPerFt, minSizeMm);
        }
    }
}
