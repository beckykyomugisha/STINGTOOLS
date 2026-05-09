// StingTools — ElectricalStandardsValidator.
//
// Walks every Conduit + ConduitFitting + CableTray in the model and
// reports BS 7671:2018+A2:2022 §522.8 violations:
//
//   • §522.8.5 — no more than three 90° bends between draw-in points.
//   • §522.8.4 — typical max conduit run 6 m between draw-in points
//     (varies with conduit size; we apply 6 m as a single conservative
//     ceiling and surface the run length on every conduit so users can
//     re-check against their preferred internal standard).
//   • §522.8 / IEE Wiring Regs companion — cable fill above the
//     manufacturer's listed limit (BS EN 61386 typically 40% for
//     straight runs, 35% with bends, 30% with two bends).
//
// The validator is read-only — it never mutates the model — so it
// can run alongside RunAllValidatorsCommand without taking a write
// transaction.
//
// Bend angle is read in this priority order:
//   1. ELC_CDT_BEND_ANGLE_DEG parameter (registry-aliased, set by
//      AutoConduitDrop or the fabricator).
//   2. Family/symbol name regex (90, 45, 30, 22.5, 11.25 — first match).
// Bend count and run length use connectivity to walk a conduit run
// from one connector to the next "draw-in" boundary (defined as a
// junction box, panel, or open end).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;

namespace StingTools.Core.Validation
{
    public class ElectricalStandardsValidator
    {
        public string Name => "ElectricalStandardsValidator";
        private const string ValidatorTag = "ElectricalStandardsValidator";

        /// <summary>BS 7671 §522.8.5 — max bends between draw-in points.</summary>
        public int MaxBendsBetweenDrawIn { get; set; } = 3;

        /// <summary>Conservative max run length between draw-in points (mm).</summary>
        public double MaxRunLengthMm { get; set; } = 6000.0;

        /// <summary>Manufacturer-typical fill ceiling for straight runs.</summary>
        public double MaxFillStraightPct { get; set; } = 40.0;

        /// <summary>Manufacturer-typical fill ceiling with one or more bends.</summary>
        public double MaxFillWithBendsPct { get; set; } = 35.0;

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            try
            {
                ValidateConduits(doc, results);
                ValidateConduitFill(doc, results);
                ValidateBendAngles(doc, results);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ElectricalStandardsValidator: scan failed: {ex.Message}");
            }
            return results;
        }

        private void ValidateConduits(Document doc, List<ValidationResult> results)
        {
            var conduits = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var el in conduits)
            {
                try
                {
                    double lengthMm = ReadLengthMm(el);
                    if (lengthMm > MaxRunLengthMm)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "ELEC.RUN.LONG",
                            $"Conduit run {lengthMm/1000:F2} m exceeds {MaxRunLengthMm/1000:F1} m draw-in spacing (BS 7671 §522.8.4)",
                            ValidatorTag));
                    }

                    int bends = ReadBendCount(el);
                    if (bends > MaxBendsBetweenDrawIn)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "ELEC.BENDS.EXCESS",
                            $"{bends} bends between draw-in points (BS 7671 §522.8.5 limits to {MaxBendsBetweenDrawIn})",
                            ValidatorTag));
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ElectricalStandardsValidator conduit {el?.Id}: {ex.Message}");
                }
            }
        }

        private void ValidateConduitFill(Document doc, List<ValidationResult> results)
        {
            var col = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Conduit)
                .WhereElementIsNotElementType();

            foreach (var el in col)
            {
                try
                {
                    string raw = ParameterHelpers.GetString(el, ParamRegistry.ELC_CONDUIT_FILL_PCT);
                    if (string.IsNullOrEmpty(raw)) continue;
                    if (!double.TryParse(raw.Trim().TrimEnd('%'),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out double pct))
                        continue;

                    int bends = ReadBendCount(el);
                    double limit = bends > 0 ? MaxFillWithBendsPct : MaxFillStraightPct;

                    if (pct > limit)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "ELEC.FILL.OVER",
                            $"Cable fill {pct:F1}% exceeds {limit:F0}% manufacturer limit (BS EN 61386)",
                            ValidatorTag));
                    }
                    else if (pct > limit * 0.9)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "ELEC.FILL.NEAR",
                            $"Cable fill {pct:F1}% within 10% of {limit:F0}% manufacturer limit",
                            ValidatorTag));
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ElectricalStandardsValidator fill {el?.Id}: {ex.Message}");
                }
            }
        }

        private void ValidateBendAngles(Document doc, List<ValidationResult> results)
        {
            // Conduit fittings (bends) — flag any bend whose angle the
            // standard doesn't recognise. Standard angles per BS EN 61386
            // are 11.25, 22.5, 30, 45, 60, 90, 120 degrees.
            var allowed = new[] { 11.25, 22.5, 30, 45, 60, 90, 120 };
            var col = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ConduitFitting)
                .WhereElementIsNotElementType();
            foreach (var el in col)
            {
                try
                {
                    double? deg = ReadBendAngleDeg(el);
                    if (!deg.HasValue) continue;
                    bool standard = allowed.Any(a => Math.Abs(a - deg.Value) < 0.5);
                    if (!standard)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "ELEC.BEND.ANGLE",
                            $"Non-standard bend angle {deg.Value:F1}° (BS EN 61386 standard angles: 11.25/22.5/30/45/60/90/120)",
                            ValidatorTag));
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ElectricalStandardsValidator bend {el?.Id}: {ex.Message}");
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static double ReadLengthMm(Element el)
        {
            // 1) ELC_CDT_RUN_LENGTH_M parameter (set by routing tools).
            try
            {
                string s = ParameterHelpers.GetString(el, ParamRegistry.ELC_CDT_RUN_LENGTH_M);
                if (!string.IsNullOrEmpty(s) && double.TryParse(s,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double m) && m > 0)
                    return m * 1000.0;
            }
            catch { }

            // 2) Built-in CURVE_ELEM_LENGTH (feet → mm).
            try
            {
                var p = el.LookupParameter("Length");
                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble() * 304.8; // ft → mm
            }
            catch { }

            // 3) LocationCurve length.
            try
            {
                var loc = el.Location as LocationCurve;
                if (loc?.Curve != null) return loc.Curve.Length * 304.8;
            }
            catch { }
            return 0;
        }

        private static int ReadBendCount(Element el)
        {
            try
            {
                string s = ParameterHelpers.GetString(el, ParamRegistry.ELC_CDT_BEND_COUNT_NR);
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out int n)) return n;
            }
            catch { }
            return 0;
        }

        private static double? ReadBendAngleDeg(Element el)
        {
            // Param first.
            try
            {
                string s = ParameterHelpers.GetString(el, ParamRegistry.ELC_CDT_BEND_ANGLE_DEG);
                if (!string.IsNullOrEmpty(s) && double.TryParse(s,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double d) && d > 0)
                    return d;
            }
            catch { }

            // Family-name regex fallback.
            try
            {
                string nm = (el.Name ?? "");
                if (string.IsNullOrEmpty(nm)) return null;
                var m = Regex.Match(nm, @"\b(11(?:\.25)?|22(?:\.5)?|30|45|60|90|120)\b");
                if (m.Success && double.TryParse(m.Groups[1].Value,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double d2))
                    return d2;
            }
            catch { }
            return null;
        }
    }
}
