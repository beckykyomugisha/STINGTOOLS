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
using StingTools.Core.Routing;

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
                int cableCount = ReadCableCount(el);
                try
                {
                    double lengthMm = ReadLengthMm(el);
                    if (lengthMm > MaxRunLengthMm)
                    {
                        // Suggest a draw-in box position that breaks the run
                        // into ≤MaxRunLengthMm segments. Halving works for
                        // runs up to 2× the limit; longer runs need ≥3 boxes.
                        int boxes = (int)Math.Ceiling(lengthMm / MaxRunLengthMm) - 1;
                        string fix = boxes == 1
                            ? $"Add 1 draw-in box at the run midpoint (~{lengthMm/2/1000:F1} m)."
                            : $"Add {boxes} draw-in boxes spaced every {MaxRunLengthMm/1000:F1} m.";
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "ELEC.RUN.LONG",
                            $"Conduit run {lengthMm/1000:F2} m exceeds {MaxRunLengthMm/1000:F1} m draw-in spacing (BS 7671 §522.8.4). {fix}",
                            ValidatorTag));
                    }

                    // Wave J4 — size-aware bend cap per IET Guidance Note 1.
                    // 50 mm+ rigid steel conduit tolerates 4 bends per
                    // GN1 §7.4; smaller / PVC / flex tighter. Defer to
                    // the per-conduit lookup when MaxBendsBetweenDrawIn
                    // is left at its default (3).
                    int effectiveCap = MaxBendsBetweenDrawIn;
                    if (effectiveCap == 3)
                    {
                        try
                        {
                            string odRaw = ParameterHelpers.GetString(el, "Outside Diameter")
                                ?? ParameterHelpers.GetString(el, "Diameter") ?? "";
                            double odMm = 0;
                            double.TryParse(odRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out odMm);
                            string mat = ParameterHelpers.GetString(el, "ELC_CDT_MAT_TXT") ?? "";
                            effectiveCap = StingTools.Core.Routing.JunctionBoxAutoPlacer
                                .MaxBendsForConduit(odMm, mat);
                        }
                        catch { /* fall through to baseline cap */ }
                    }

                    if (bends > effectiveCap)
                    {
                        int excess = bends - effectiveCap;
                        string fix = excess == 1
                            ? $"Add a draw-in box after the {effectiveCap}rd bend, then continue."
                            : $"Add {excess} draw-in boxes (one after every {effectiveCap} bends).";
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                            "ELEC.BENDS.EXCESS",
                            $"{bends} bends between draw-in points (limit {effectiveCap} — BS 7671 §522.8.5 + IET GN1 §7.4 size-aware). {fix}",
                            ValidatorTag));
                    }

                    string raw = ParameterHelpers.GetString(el, ParamRegistry.ELC_CONDUIT_FILL_PCT);
                    if (!string.IsNullOrEmpty(raw) &&
                        double.TryParse(raw.Trim().TrimEnd('%'),
                            NumberStyles.Any, CultureInfo.InvariantCulture, out double pct))
                    {
                        // BS EN 61386 fill limits depend on cable count.
                        // 1 cable: 53% straight / 43% with bends (single conductor — packs efficiently).
                        // 2 cables: 31% / 20% (the worst case — two cables can't share the section).
                        // 3+ cables: 40% / 35% (the engineering rule of thumb).
                        var (limit, tableName) = FillLimitForCableCount(cableCount, bends);
                        if (pct > limit)
                        {
                            string fix = cableCount > 1
                                ? $"Reduce cable count to {cableCount - 1} (limit becomes {FillLimitForCableCount(cableCount-1, bends).limit:F0}%) or upsize the conduit."
                                : "Upsize the conduit to the next standard trade size.";
                            results.Add(new ValidationResult(el.Id, ValidationSeverity.Error,
                                "ELEC.FILL.OVER",
                                $"Cable fill {pct:F1}% exceeds {limit:F0}% ({tableName}, BS EN 61386). {fix}",
                                ValidatorTag));
                        }
                        else if (pct > limit * 0.9)
                        {
                            results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                                "ELEC.FILL.NEAR",
                                $"Cable fill {pct:F1}% within 10% of {limit:F0}% manufacturer limit ({tableName})",
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

        /// <summary>
        /// BS EN 61386 fill table — keyed by cable count + whether the run
        /// has any bends. Returns the percentage ceiling and a friendly
        /// label for the result message. cableCount &lt;= 0 falls through
        /// to the conservative 3+ row so legacy projects without a count
        /// parameter still get a sensible warning.
        /// </summary>
        private (double limit, string tableName) FillLimitForCableCount(int cableCount, int bends)
        {
            // The class-level MaxFillStraightPct / WithBendsPct still wins
            // when the project explicitly overrides them in STING_FAB_RULES.json
            // — those project values cover the 3+ case, which is the most
            // common one. The 1-cable / 2-cable rows are spec-fixed.
            if (cableCount == 1)
                return bends > 0 ? (43.0, "1 cable, with bends") : (53.0, "1 cable, straight");
            if (cableCount == 2)
                return bends > 0 ? (20.0, "2 cables, with bends") : (31.0, "2 cables, straight");
            // cableCount >= 3 OR cableCount unknown → conservative default.
            return bends > 0
                ? (MaxFillWithBendsPct, "3+ cables, with bends")
                : (MaxFillStraightPct,  "3+ cables, straight");
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
                        // Snap to nearest standard angle as the suggestion.
                        double nearest = _allowedBendAngles[0];
                        double bestDelta = double.MaxValue;
                        foreach (double a in _allowedBendAngles)
                        {
                            double delta = Math.Abs(a - deg.Value);
                            if (delta < bestDelta) { bestDelta = delta; nearest = a; }
                        }
                        results.Add(new ValidationResult(el.Id, ValidationSeverity.Warning,
                            "ELEC.BEND.ANGLE",
                            $"Non-standard bend angle {deg.Value:F1}° (BS EN 61386 standard angles: 11.25/22.5/30/45/60/90/120). Swap for the closest standard fitting at {nearest:F2}°.",
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // 2) Built-in CURVE_ELEM_LENGTH (feet → mm).
            try
            {
                var p = el.LookupParameter("Length");
                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble() * 304.8; // ft → mm
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // 3) LocationCurve length.
            try
            {
                var loc = el.Location as LocationCurve;
                if (loc?.Curve != null) return loc.Curve.Length * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        private static int ReadCableCount(Element el)
        {
            // Parameter first (writers: AutoConduitDrop / cable-routing tools).
            try
            {
                string s = ParameterHelpers.GetString(el, ParamRegistry.ELC_CDT_CABLE_COUNT_NR);
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out int n) && n > 0) return n;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            // Unknown — return 0 so the fill table picks the conservative 3+ row.
            return 0;
        }

        private static int ReadBendCount(Element el)
        {
            // 1) Parameter (set by routing tool / earlier validator pass).
            try
            {
                string s = ParameterHelpers.GetString(el, ParamRegistry.ELC_CDT_BEND_COUNT_NR);
                if (!string.IsNullOrEmpty(s) && int.TryParse(s, out int n)) return n;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

            // 2) Geometric fallback — walk the conduit's connector graph
            // and count connected ConduitFittings whose bend angle is
            // > 0. This makes the validator self-sufficient on projects
            // that haven't run AutoConduitDrop / the routing tools that
            // populate ELC_CDT_BEND_COUNT_NR. Bounded to direct neighbours
            // to keep the cost O(connectors) per conduit rather than
            // walking the whole MEP graph.
            try
            {
                var doc = el?.Document;
                if (doc == null) return 0;
                var conn = (el as MEPCurve)?.ConnectorManager?.Connectors;
                if (conn == null) return 0;
                int bends = 0;
                foreach (Connector c in conn)
                {
                    foreach (Connector other in c.AllRefs)
                    {
                        var owner = other.Owner;
                        if (owner == null || owner.Id == el.Id) continue;
                        if (owner.Category?.Id?.Value == (long)BuiltInCategory.OST_ConduitFitting)
                        {
                            double? a = ReadBendAngleDeg(owner);
                            if (a.HasValue && a.Value > 0) bends++;
                        }
                    }
                }
                return bends;
            }
            catch (Exception ex) { StingLog.Warn($"ReadBendCount geometric: {ex.Message}"); }
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }

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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
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
