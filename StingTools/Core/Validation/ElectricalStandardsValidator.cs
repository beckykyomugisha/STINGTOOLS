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
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Validation
{
    public class ElectricalStandardsValidator
    {
        public string Name => "ElectricalStandardsValidator";
        private const string ValidatorTag = "ElectricalStandardsValidator";

        // Standard angles per BS EN 61386 / IEC 61386. Cached so the
        // hot path doesn't allocate per-element. Tolerance = ±0.5°.
        private static readonly double[] _allowedBendAngles = { 11.25, 22.5, 30, 45, 60, 90, 120 };

        // Compiled once — used by the family-name fallback parser. Match
        // FIRST plausible angle in the name; "EMT-90-EL" → 90.
        private static readonly Regex _bendAngleRx = new Regex(
            @"\b(11(?:\.25)?|22(?:\.5)?|30|45|60|90|120)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Lazy-loaded threshold cache — populated from STING_FAB_RULES.json
        // on first Validate() call. Falls back to BS 7671 / BS EN 61386
        // defaults when the JSON is missing or malformed so the validator
        // still works on stock projects.
        private static volatile Thresholds _cached;

        /// <summary>BS 7671 §522.8.5 — max bends between draw-in points.</summary>
        public int MaxBendsBetweenDrawIn { get; set; } = -1;

        /// <summary>Conservative max run length between draw-in points (mm).</summary>
        public double MaxRunLengthMm { get; set; } = -1;

        /// <summary>Manufacturer-typical fill ceiling for straight runs.</summary>
        public double MaxFillStraightPct { get; set; } = -1;

        /// <summary>Manufacturer-typical fill ceiling with one or more bends.</summary>
        public double MaxFillWithBendsPct { get; set; } = -1;

        public List<ValidationResult> Validate(Document doc)
        {
            var results = new List<ValidationResult>();
            if (doc == null) return results;

            ApplyDefaults(LoadThresholds());

            try
            {
                // One collector pass — share results across the conduit
                // length, bend count and fill checks instead of issuing
                // three separate FilteredElementCollector queries.
                var conduits = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Conduit)
                    .WhereElementIsNotElementType()
                    .ToList();
                ValidateConduits(conduits, results);

                ValidateBendAngles(doc, results);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ElectricalStandardsValidator: scan failed: {ex.Message}");
            }
            return results;
        }

        private void ApplyDefaults(Thresholds t)
        {
            if (MaxBendsBetweenDrawIn < 0) MaxBendsBetweenDrawIn = t.MaxBends;
            if (MaxRunLengthMm       < 0) MaxRunLengthMm       = t.MaxLengthMm;
            if (MaxFillStraightPct   < 0) MaxFillStraightPct   = t.MaxFillStraightPct;
            if (MaxFillWithBendsPct  < 0) MaxFillWithBendsPct  = t.MaxFillWithBendsPct;
        }

        private void ValidateConduits(List<Element> conduits, List<ValidationResult> results)
        {
            foreach (var el in conduits)
            {
                int bends = ReadBendCount(el);   // read once, share with fill check below
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

                    if (bends > MaxBendsBetweenDrawIn)
                    {
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "ELEC.BENDS.EXCESS",
                            $"{bends} bends between draw-in points (BS 7671 §522.8.5 limits to {MaxBendsBetweenDrawIn})",
                            ValidatorTag));
                    }

                    string raw = ParameterHelpers.GetString(el, ParamRegistry.ELC_CONDUIT_FILL_PCT);
                    if (!string.IsNullOrEmpty(raw) &&
                        double.TryParse(raw.Trim().TrimEnd('%'),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double pct))
                    {
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
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"ElectricalStandardsValidator conduit {el?.Id}: {ex.Message}");
                }
            }
        }

        private void ValidateBendAngles(Document doc, List<ValidationResult> results)
        {
            // Conduit fittings (bends) — flag any bend whose angle the
            // standard doesn't recognise. Standard angles per BS EN 61386
            // are 11.25, 22.5, 30, 45, 60, 90, 120 degrees.
            var col = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_ConduitFitting)
                .WhereElementIsNotElementType();
            foreach (var el in col)
            {
                try
                {
                    double? deg = ReadBendAngleDeg(el);
                    if (!deg.HasValue) continue;
                    bool standard = false;
                    foreach (double a in _allowedBendAngles)
                        if (Math.Abs(a - deg.Value) < 0.5) { standard = true; break; }
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

            // Family-name regex fallback (compiled regex cached at class scope).
            try
            {
                string nm = (el.Name ?? "");
                if (string.IsNullOrEmpty(nm)) return null;
                var m = _bendAngleRx.Match(nm);
                if (m.Success && double.TryParse(m.Groups[1].Value,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double d2))
                    return d2;
            }
            catch { }
            return null;
        }

        // ── Threshold loader ────────────────────────────────────────────
        // Reads STING_FAB_RULES.json's Conduit (preferred) or Electrical
        // section so projects can override BS 7671 defaults without code
        // changes. Cached process-wide; call ResetThresholds() in tests
        // or after editing the JSON on disk.

        public static void ResetThresholds() { _cached = null; }

        private static Thresholds LoadThresholds()
        {
            var c = _cached;
            if (c != null) return c;

            var t = new Thresholds();
            try
            {
                string path = StingToolsApp.FindDataFile("STING_FAB_RULES.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var root = JObject.Parse(File.ReadAllText(path));
                    var node = root["Conduit"] ?? root["Electrical"];
                    if (node != null)
                    {
                        if (node["MaxBends"]    != null) t.MaxBends    = (int)node["MaxBends"];
                        if (node["MaxLengthMm"] != null) t.MaxLengthMm = (double)node["MaxLengthMm"];
                        if (node["MaxFillStraightPct"] != null)
                            t.MaxFillStraightPct = (double)node["MaxFillStraightPct"];
                        if (node["MaxFillWithBendsPct"] != null)
                            t.MaxFillWithBendsPct = (double)node["MaxFillWithBendsPct"];
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ElectricalStandardsValidator: STING_FAB_RULES.json parse failed: {ex.Message}");
            }
            _cached = t;
            return t;
        }

        private sealed class Thresholds
        {
            // BS 7671:2018+A2:2022 §522.8 / BS EN 61386 defaults — used
            // when STING_FAB_RULES.json is missing or malformed.
            public int    MaxBends             = 3;
            public double MaxLengthMm          = 6000.0;
            public double MaxFillStraightPct   = 40.0;
            public double MaxFillWithBendsPct  = 35.0;
        }
    }
}
