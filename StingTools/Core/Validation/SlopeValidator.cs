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

                        // §5.3 — family-level override. When a family declares
                        // SLOPE_MIN_PCT / SLOPE_MAX_PCT the validator honours
                        // those bounds instead of the global BS EN 12056-2
                        // defaults. Missing or zero values fall back to the
                        // standard thresholds so existing projects behave
                        // identically.
                        var routing = Routing.RoutingParamReader.Read(el);
                        double warnFloor = routing.SlopeMinPct > 0 ? routing.SlopeMinPct : MinSlopeWarnPct;
                        double errorFloor = MinSlopeErrorPct;
                        double errorCeiling = routing.SlopeMaxPct > 0 ? routing.SlopeMaxPct : double.PositiveInfinity;

                        if (slope < errorFloor)
                        {
                            results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                                "SLOPE.SAN.UNDER",
                                $"Slope {slope:F2}% below {errorFloor:F2}% (BS EN 12056-2 hard floor)",
                                ValidatorTag));
                        }
                        else if (slope < warnFloor)
                        {
                            string src = routing.SlopeMinPct > 0 ? "family SLOPE_MIN_PCT" : "BS EN 12056-2";
                            results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                                "SLOPE.SAN.LOW",
                                $"Slope {slope:F2}% below {warnFloor:F2}% recommended ({src})",
                                ValidatorTag));
                        }
                        else if (slope > errorCeiling)
                        {
                            results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                                "SLOPE.SAN.OVER",
                                $"Slope {slope:F2}% above family SLOPE_MAX_PCT {errorCeiling:F2}%",
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
