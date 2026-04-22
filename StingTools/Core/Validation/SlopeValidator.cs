// StingTools v4 MVP — SlopeValidator.
//
// Walks drainage pipes and asserts slope meets BS EN 12056 minimums.
// Uses PLM_SLOPE_PCT (the v4-MVP-introduced parameter) when present;
// otherwise computes slope from the pipe LocationCurve start/end Z.
//
// BS EN 12056-2 (sanitary) — secondary discharge (75-100mm) typically
// 1:80 to 1:40 (1.25%-2.5%); main runs 1:100 to 1:80.
// Implementation gates anything below 1.0% as warning and below
// 0.5% as error.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Validation
{
    public class SlopeValidator
    {
        public string Name => "SlopeValidator";
        private const string ValidatorTag = "SlopeValidator";

        /// <summary>
        /// Minimum slope (percent) for a "warning" finding. 1.0% is the
        /// BS EN 12056-2 norm for sanitary 75/100 mm pipes (1:100).
        /// </summary>
        public double MinSlopeWarnPct { get; set; } = 1.0;

        /// <summary>
        /// Minimum slope (percent) for an "error" finding. 0.5% is the
        /// hard floor below which solids transport breaks down.
        /// </summary>
        public double MinSlopeErrorPct { get; set; } = 0.5;

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            try
            {
                var col = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType();
                foreach (var el in col)
                {
                    try
                    {
                        if (!IsDrainagePipe(el)) continue;
                        double slope = ComputeSlopePct(el);
                        if (slope <= 0) continue;
                        if (slope < MinSlopeErrorPct)
                        {
                            results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                                "SLOPE.SAN.UNDER",
                                $"Slope {slope:F2}% below {MinSlopeErrorPct:F2}% (BS EN 12056-2 hard floor)",
                                ValidatorTag));
                        }
                        else if (slope < MinSlopeWarnPct)
                        {
                            results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                                "SLOPE.SAN.LOW",
                                $"Slope {slope:F2}% below {MinSlopeWarnPct:F2}% recommended (BS EN 12056-2)",
                                ValidatorTag));
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"SlopeValidator: pipe {el?.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SlopeValidator: scan failed: {ex.Message}");
            }
            return results;
        }

        private bool IsDrainagePipe(Element el)
        {
            // Treat anything in the SAN / SOIL / WASTE / DRN systems as
            // a drainage pipe. Heuristic uses PLM_SYS_TXT or the
            // attached system name keyword.
            try
            {
                var pSys = el.LookupParameter("PLM_SYS_TXT");
                string sys = (pSys?.AsString() ?? "").ToUpperInvariant();
                if (sys.Contains("SAN") || sys.Contains("SOIL") ||
                    sys.Contains("WASTE") || sys.Contains("DRN") ||
                    sys.Contains("RWO") || sys.Contains("RWP")) return true;
                if (el is Autodesk.Revit.DB.Plumbing.Pipe p)
                {
                    string nm = p.MEPSystem?.Name ?? "";
                    nm = nm.ToUpperInvariant();
                    if (nm.Contains("SANITARY") || nm.Contains("WASTE") ||
                        nm.Contains("STORM") || nm.Contains("DRAIN")) return true;
                }
            }
            catch { }
            return false;
        }

        private double ComputeSlopePct(Element el)
        {
            // Prefer the v4 MVP parameter when present.
            try
            {
                var p = el.LookupParameter(StingTools.Core.ParamRegistry.PLM_SLOPE_PCT_V4);
                if (p != null && p.StorageType == StorageType.String)
                {
                    string v = p.AsString();
                    if (!string.IsNullOrEmpty(v) &&
                        double.TryParse(v.Trim().TrimEnd('%'),
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double pct)) return pct;
                }
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch { }

            // Fall back to geometric slope from LocationCurve endpoints.
            try
            {
                var loc = el.Location as LocationCurve;
                var curve = loc?.Curve;
                if (curve == null) return 0;
                XYZ s = curve.GetEndPoint(0);
                XYZ e = curve.GetEndPoint(1);
                double dz = Math.Abs(e.Z - s.Z);
                double dxy = Math.Sqrt(Math.Pow(e.X - s.X, 2) + Math.Pow(e.Y - s.Y, 2));
                if (dxy < 0.0001) return 0;
                return (dz / dxy) * 100.0;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SlopeValidator: geometric slope failed for {el?.Id}: {ex.Message}");
                return 0;
            }
        }
    }
}
