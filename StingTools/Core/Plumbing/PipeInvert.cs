// StingTools — reading a pipe's invert levels out of Revit.
//
// The Revit-bound half of InvertMath: pulls the centreline ends, the internal
// diameter and the datum out of the document, and hands back both ends'
// bore inverts with upstream/downstream decided by HEIGHT, not by which end
// Revit happens to call 0. The dimensioner, InvertLevelEngine and the manhole
// schedule all read through here, so the three can no longer disagree.

using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Plumbing
{
    public sealed class PipeInvertResult
    {
        public XYZ UpPoint { get; set; }
        public XYZ DownPoint { get; set; }
        /// <summary>Upstream (higher) bore invert, metres on the reporting datum.</summary>
        public double UpInvertM { get; set; }
        /// <summary>Downstream (lower) bore invert, metres on the reporting datum.</summary>
        public double DownInvertM { get; set; }
        public double InnerDiameterMm { get; set; }
        public double HorizontalRunM { get; set; }
        public bool IsLevel { get; set; }
        /// <summary>"1:80", or null for a level pipe.</summary>
        public string Gradient { get; set; }
        public InvertSource Source { get; set; }
        /// <summary>Set when Revit's own computed invert disagrees with ours by more than 5 mm.</summary>
        public string CrossCheckNote { get; set; }
    }

    public static class PipeInvert
    {
        private const double FtToM = 0.3048;

        /// <summary>
        /// Null when the pipe has no straight centreline or no usable diameter —
        /// the reason is in <paramref name="why"/>. Never a guessed level.
        /// </summary>
        public static PipeInvertResult Compute(Document doc, Pipe pipe, IlReportingOptions opts, out string why)
        {
            why = null;
            opts = opts ?? IlReportingOptions.Default;
            if (!(pipe?.Location is LocationCurve lc) || lc.Curve == null)
            {
                why = "no location curve";
                return null;
            }
            var a = lc.Curve.GetEndPoint(0);
            var b = lc.Curve.GetEndPoint(1);

            double? idM = ReadLengthM(pipe, BuiltInParameter.RBS_PIPE_INNER_DIAM_PARAM);
            double? nomM = null;
            try { nomM = pipe.Diameter * FtToM; } catch (Exception ex) { StingLog.Warn($"PipeInvert diameter {pipe.Id}: {ex.Message}"); }

            double datumOffsetM = DatumOffsetM(doc, opts.Datum);
            double za = a.Z * FtToM + datumOffsetM, zb = b.Z * FtToM + datumOffsetM;

            int up = InvertMath.UpstreamIndex(za, zb, out bool level);
            var upPt = up == 0 ? a : b;
            var dnPt = up == 0 ? b : a;
            double upZ = up == 0 ? za : zb, dnZ = up == 0 ? zb : za;

            var upInv = InvertMath.Invert(upZ, idM, nomM, out var src);
            var dnInv = InvertMath.Invert(dnZ, idM, nomM, out _);
            if (!upInv.HasValue || !dnInv.HasValue)
            {
                why = "no internal or nominal diameter";
                return null;
            }

            double runM = new XYZ(b.X - a.X, b.Y - a.Y, 0).GetLength() * FtToM;
            var r = new PipeInvertResult
            {
                UpPoint = upPt,
                DownPoint = dnPt,
                UpInvertM = upInv.Value,
                DownInvertM = dnInv.Value,
                InnerDiameterMm = (idM ?? nomM ?? 0) * 1000.0,
                HorizontalRunM = runM,
                IsLevel = level,
                Gradient = InvertMath.GradientText(upZ, dnZ, runM),
                Source = src,
            };
            r.CrossCheckNote = CrossCheck(doc, pipe, r, datumOffsetM);
            return r;
        }

        /// <summary>
        /// Revit computes its own upper/lower invert. Its reference (level-relative
        /// is expected) has NOT been verified in Revit, so it is used only to
        /// cross-check, never as the reported value: a disagreement is surfaced,
        /// not silently resolved either way.
        /// </summary>
        private static string CrossCheck(Document doc, Pipe pipe, PipeInvertResult r, double datumOffsetM)
        {
            try
            {
                var upP = pipe.get_Parameter(BuiltInParameter.MEP_PIPE_UPPER_INVERT_ELEVATION);
                var dnP = pipe.get_Parameter(BuiltInParameter.MEP_PIPE_LOWER_INVERT_ELEVATION);
                if (upP == null || dnP == null || !upP.HasValue || !dnP.HasValue) return null;
                double levelFt = 0;
                if (doc.GetElement(pipe.ReferenceLevel?.Id ?? ElementId.InvalidElementId) is Level lvl) levelFt = lvl.Elevation;
                double revUp = (levelFt + upP.AsDouble()) * FtToM + datumOffsetM;
                double revDn = (levelFt + dnP.AsDouble()) * FtToM + datumOffsetM;
                if (Math.Abs(revUp - r.UpInvertM) > 0.005 || Math.Abs(revDn - r.DownInvertM) > 0.005)
                    return $"Revit's own invert ({revUp:F3} / {revDn:F3}) differs from the bore invert "
                         + $"({r.UpInvertM:F3} / {r.DownInvertM:F3}) by more than 5 mm.";
            }
            catch (Exception ex) { StingLog.Warn($"PipeInvert cross-check {pipe?.Id}: {ex.Message}"); }
            return null;
        }

        private static double? ReadLengthM(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el.get_Parameter(bip);
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return null;
                double v = p.AsDouble();
                return v > 0 ? v * FtToM : (double?)null;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PipeInvert read {bip} on {el?.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Metres to ADD to an internal-coordinate elevation to report it on the
        /// chosen datum. Z only: horizontal rotation of shared coordinates does not
        /// affect elevation.
        /// </summary>
        public static double DatumOffsetM(Document doc, IlDatum datum)
        {
            try
            {
                switch (datum)
                {
                    case IlDatum.SurveyPoint:
                    {
                        var sp = BasePoint.GetSurveyPoint(doc);
                        if (sp == null) return 0;
                        return (sp.SharedPosition.Z - sp.Position.Z) * FtToM;
                    }
                    case IlDatum.ProjectBasePoint:
                    {
                        var pbp = BasePoint.GetProjectBasePoint(doc);
                        return pbp == null ? 0 : -pbp.Position.Z * FtToM;
                    }
                    default:
                        return 0;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PipeInvert datum {datum}: {ex.Message} — reporting on the internal origin.");
                return 0;
            }
        }
    }
}
