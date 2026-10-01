// WaterSafetyLimits — TMV outlet limits and dead-leg limits (Revit-free).
//
// The ONE owner of these values is Data/Plumbing/STING_TMV_STANDARDS.json
// (DSCH-25). There is no constant fallback: a missing or invalid file means
// every check reports NOT CHECKED with the reason, never a guessed limit.
//
// TMV outlet limits depend on three things, and a check that cannot establish
// all of them reports NOT CHECKED rather than defaulting:
//   * the outlet the TMV serves (BATH / SHOWER / BASIN / BIDET)
//   * the TMV scheme (TMV2 = BS EN 1111 / BS EN 1287; TMV3 = NHS D08)
//   * for a TMV3 bath, whether bathing is assisted (HTM 04-01 Pt A Table 2:
//     44 °C unassisted, 46 °C assisted, in exceptional circumstances only)
// Healthcare premises require TMV3 (HTM 04-01 Pt A Table 2; HSG274 Pt 2 §2.76).
//
// Dead-leg limits: HSG274 Part 2 gives NO numeric length — its test is time to
// temperature (§2.82). The lengths are design proxies: HTM 04-01 Pt A §12.5
// (healthcare spur ≤ 3 m), §10.48 (blended pipe downstream of a mixer ≤ 2 m),
// and BS 8558 Tables 6/7 for uninsulated hot pipe (values marked VERIFY).

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Plumbing
{
    /// <summary>One row of outletLimits: the maximum set delivery temperature for
    /// an outlet under a TMV scheme.</summary>
    public class TmvOutletLimit
    {
        public string Outlet       { get; set; } = "";
        public string Scheme       { get; set; } = "";
        public bool   Assisted     { get; set; }
        public double MaxSetC      { get; set; }
        public double NeverExceedC { get; set; }
        public string Source       { get; set; } = "";
        public string Verify       { get; set; } = "";
        public string Note         { get; set; } = "";
    }

    /// <summary>A length limit with its source and, when unconfirmed, what to verify.</summary>
    public class SourcedLengthLimit
    {
        public double ValueM { get; set; }
        public string Source { get; set; } = "";
        public string Verify { get; set; } = "";
    }

    /// <summary>One row of the uninsulated hot-pipe length table, by outside diameter.
    /// UpToOdMm null = no upper bound (the last row).</summary>
    public class HotPipeLengthRow
    {
        public double? UpToOdMm   { get; set; }
        public double  MaxLengthM { get; set; }
    }

    public class DeadLegLimits
    {
        public SourcedLengthLimit HealthcareSpur     { get; set; }
        public SourcedLengthLimit BlendedDownstream  { get; set; }
        public List<HotPipeLengthRow> UninsulatedHotByOd { get; set; } = new List<HotPipeLengthRow>();
        public string UninsulatedHotSource { get; set; } = "";
        public string UninsulatedHotVerify { get; set; } = "";
        public double RedundantBranchMaxDiameters { get; set; }
        public string RedundantBranchSource { get; set; } = "";
        public string RedundantBranchVerify { get; set; } = "";
        public string GoverningTest { get; set; } = "";
    }

    /// <summary>Root of STING_TMV_STANDARDS.json.</summary>
    public class WaterSafetyLimitsFile
    {
        public string Version { get; set; } = "";
        public string HealthcareRequiredScheme { get; set; } = "";
        public string HealthcareRequiredSchemeSource { get; set; } = "";
        public List<TmvOutletLimit> OutletLimits { get; set; } = new List<TmvOutletLimit>();
        public DeadLegLimits DeadLegLimits { get; set; }
    }

    public enum WaterCheckStatus { Pass, Fail, NotChecked }

    public class TmvCheck
    {
        public WaterCheckStatus Status { get; set; }
        public string Reason { get; set; } = "";
        public string StandardRef { get; set; } = "";
        public TmvOutletLimit Limit { get; set; }
    }

    public class DeadLegLimitResult
    {
        /// <summary>Null when no sourced limit applies (NOT CHECKED).</summary>
        public double? LimitM { get; set; }
        public string Basis { get; set; } = "";
        public string Verify { get; set; } = "";
        public string NotCheckedReason { get; set; } = "";
    }

    public static class WaterSafetyLimits
    {
        public static readonly string[] Outlets = { "BATH", "SHOWER", "BASIN", "BIDET" };
        public static readonly string[] Schemes = { "TMV2", "TMV3" };

        /// <summary>Parses the file and validates it. Returns null (with errors)
        /// when it cannot be used — the caller must then report NOT CHECKED.</summary>
        public static WaterSafetyLimitsFile Parse(string json, out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) { errors.Add("file is empty or missing"); return null; }
            WaterSafetyLimitsFile f;
            try { f = JsonConvert.DeserializeObject<WaterSafetyLimitsFile>(json); }
            catch (Exception ex) { errors.Add("not valid JSON: " + ex.Message); return null; }
            if (f == null) { errors.Add("empty document"); return null; }

            if (f.OutletLimits == null || f.OutletLimits.Count == 0) errors.Add("outletLimits is empty");
            else
            {
                foreach (var r in f.OutletLimits)
                {
                    string id = $"{r.Outlet}/{r.Scheme}{(r.Assisted ? "/assisted" : "")}";
                    if (!Outlets.Contains(r.Outlet)) errors.Add($"outletLimits {id}: unknown outlet");
                    if (!Schemes.Contains(r.Scheme)) errors.Add($"outletLimits {id}: unknown scheme");
                    if (r.MaxSetC <= 0 || r.NeverExceedC < r.MaxSetC) errors.Add($"outletLimits {id}: maxSetC/neverExceedC invalid");
                    if (string.IsNullOrWhiteSpace(r.Source)) errors.Add($"outletLimits {id}: no source");
                }
                var dup = f.OutletLimits.GroupBy(r => (r.Outlet, r.Scheme, r.Assisted)).FirstOrDefault(g => g.Count() > 1);
                if (dup != null) errors.Add($"outletLimits: duplicate row {dup.Key}");
            }
            if (!Schemes.Contains(f.HealthcareRequiredScheme ?? "")) errors.Add("healthcareRequiredScheme must be TMV2 or TMV3");

            var d = f.DeadLegLimits;
            if (d == null) errors.Add("deadLegLimits missing");
            else
            {
                if (d.HealthcareSpur == null || d.HealthcareSpur.ValueM <= 0) errors.Add("deadLegLimits.healthcareSpur invalid");
                if (d.BlendedDownstream == null || d.BlendedDownstream.ValueM <= 0) errors.Add("deadLegLimits.blendedDownstream invalid");
                if (d.UninsulatedHotByOd == null || d.UninsulatedHotByOd.Count == 0) errors.Add("deadLegLimits.uninsulatedHotByOd empty");
                else
                {
                    double prev = 0;
                    for (int i = 0; i < d.UninsulatedHotByOd.Count; i++)
                    {
                        var row = d.UninsulatedHotByOd[i];
                        bool last = i == d.UninsulatedHotByOd.Count - 1;
                        if (row.MaxLengthM <= 0) errors.Add($"uninsulatedHotByOd[{i}]: maxLengthM invalid");
                        if (last ? row.UpToOdMm.HasValue : (!row.UpToOdMm.HasValue || row.UpToOdMm.Value <= prev))
                            errors.Add($"uninsulatedHotByOd[{i}]: rows must ascend by upToOdMm, last row open-ended");
                        if (row.UpToOdMm.HasValue) prev = row.UpToOdMm.Value;
                    }
                }
                if (d.RedundantBranchMaxDiameters <= 0) errors.Add("deadLegLimits.redundantBranchMaxDiameters invalid");
            }
            return errors.Count == 0 ? f : null;
        }

        /// <summary>PLM_FIX_TYPE_TXT (or similar) → BATH / SHOWER / BASIN / BIDET, else null.</summary>
        public static string NormaliseOutlet(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var u = s.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
            switch (u)
            {
                case "BATH": case "BATHTUB": case "BATH_FILL": return "BATH";
                case "SHOWER": case "HAIR_WASH": case "HAIRWASH": return "SHOWER";
                case "BASIN": case "WHB": case "WASH_BASIN": case "WASHBASIN": case "HAND_BASIN": case "WASH_HAND_BASIN": return "BASIN";
                case "BIDET": return "BIDET";
                default: return null;
            }
        }

        /// <summary>PLM_TMV_CLASS_TXT / PLM_TMV_TYPE_TXT → TMV2 / TMV3, else null.</summary>
        public static string NormaliseScheme(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var u = s.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            if (u == "TMV3" || u == "TYPE3") return "TMV3";
            if (u == "TMV2" || u == "TYPE2") return "TMV2";
            return null;
        }

        /// <summary>
        /// Checks one TMV. <paramref name="setC"/> is the design set point and
        /// <paramref name="measuredC"/> the commissioning reading (0 or less = none).
        /// <paramref name="assisted"/> null = not recorded.
        /// </summary>
        public static TmvCheck CheckTmv(WaterSafetyLimitsFile limits, string outlet, string scheme,
            bool? assisted, bool isHealthcare, double setC, double measuredC)
        {
            TmvCheck NotChecked(string why) => new TmvCheck { Status = WaterCheckStatus.NotChecked, Reason = "NOT CHECKED — " + why };
            if (limits == null) return NotChecked("TMV limits data (STING_TMV_STANDARDS.json) not loaded");
            if (outlet == null) return NotChecked("outlet type unknown (set PLM_FIX_TYPE_TXT to BATH, SHOWER, BASIN or BIDET)");
            if (scheme == null) return NotChecked("TMV scheme unknown (PLM_TMV_CLASS_TXT must be TMV2 or TMV3)");

            if (isHealthcare && scheme != limits.HealthcareRequiredScheme)
                return new TmvCheck
                {
                    Status = WaterCheckStatus.Fail,
                    Reason = $"{scheme} fitted on healthcare premises; {limits.HealthcareRequiredScheme} required",
                    StandardRef = limits.HealthcareRequiredSchemeSource
                };

            if (setC <= 0 && measuredC <= 0) return NotChecked("no set point (PLM_TMV_BLEND_TEMP_C) and no measured outlet temperature");

            var unassisted = Find(limits, outlet, scheme, false);
            var assistedRow = Find(limits, outlet, scheme, true);
            if (unassisted == null) return NotChecked($"no limit for {outlet} under {scheme}");

            // An assisted row exists only where the standard gives one (TMV3 bath);
            // elsewhere the unassisted limit applies whether or not bathing is assisted.
            var row = (assisted == true && assistedRow != null) ? assistedRow : unassisted;

            string fail = Check(row, setC, measuredC, out bool ok);
            if (!ok && assisted == null && assistedRow != null)
            {
                Check(assistedRow, setC, measuredC, out bool okAssisted);
                if (okAssisted)
                    return NotChecked($"{outlet} at {Math.Max(setC, measuredC):0.#} °C is within the assisted-bathing limit " +
                                      $"({assistedRow.MaxSetC:0.#} °C) but not the unassisted one ({unassisted.MaxSetC:0.#} °C); " +
                                      "whether bathing is assisted is not recorded (PLM_TMV_ASSISTED_BOOL)");
            }
            return new TmvCheck
            {
                Status = ok ? WaterCheckStatus.Pass : WaterCheckStatus.Fail,
                Reason = ok ? "" : fail.TrimStart(';', ' '),
                StandardRef = row.Source,
                Limit = row
            };
        }

        private static string Check(TmvOutletLimit row, double setC, double measuredC, out bool ok)
        {
            var parts = new List<string>();
            if (setC > 0 && setC > row.MaxSetC + 1e-9)
                parts.Add($"set point {setC:0.#} °C exceeds {row.MaxSetC:0.#} °C maximum for {Describe(row)}");
            if (measuredC > 0 && measuredC > row.NeverExceedC + 1e-9)
                parts.Add($"measured {measuredC:0.#} °C exceeds {row.NeverExceedC:0.#} °C never-exceed for {Describe(row)}");
            ok = parts.Count == 0;
            return ok ? "" : "; " + string.Join("; ", parts);
        }

        private static string Describe(TmvOutletLimit r) => $"{(r.Assisted ? "assisted " : "")}{r.Outlet.ToLowerInvariant()} ({r.Scheme})";

        private static TmvOutletLimit Find(WaterSafetyLimitsFile f, string outlet, string scheme, bool assisted) =>
            f.OutletLimits.FirstOrDefault(r => r.Outlet == outlet && r.Scheme == scheme && r.Assisted == assisted);

        /// <summary>
        /// The dead-leg length limit for one leg.
        /// <paramref name="openEnd"/>: the leg ends in nothing (capped / redundant branch).
        /// <paramref name="blended"/>: the leg carries blended water downstream of a mixer.
        /// <paramref name="hot"/>: hot (DHW) rather than cold.
        /// </summary>
        public static DeadLegLimitResult DeadLegLimitFor(WaterSafetyLimitsFile limits, bool isHealthcare,
            bool openEnd, bool blended, bool hot, double outsideDiameterMm, double nominalDiameterMm)
        {
            var d = limits?.DeadLegLimits;
            if (d == null) return new DeadLegLimitResult { NotCheckedReason = "dead-leg limits data (STING_TMV_STANDARDS.json) not loaded" };

            if (openEnd)
            {
                if (nominalDiameterMm <= 0) return new DeadLegLimitResult { NotCheckedReason = "open-ended leg with no diameter" };
                return new DeadLegLimitResult
                {
                    LimitM = d.RedundantBranchMaxDiameters * nominalDiameterMm / 1000.0,
                    Basis = $"capped / redundant branch ≤ {d.RedundantBranchMaxDiameters:0.#} × DN ({d.RedundantBranchSource})",
                    Verify = d.RedundantBranchVerify
                };
            }
            if (blended)
                return new DeadLegLimitResult { LimitM = d.BlendedDownstream.ValueM, Basis = d.BlendedDownstream.Source, Verify = d.BlendedDownstream.Verify };
            if (isHealthcare)
                return new DeadLegLimitResult { LimitM = d.HealthcareSpur.ValueM, Basis = d.HealthcareSpur.Source, Verify = d.HealthcareSpur.Verify };
            if (!hot)
                return new DeadLegLimitResult { NotCheckedReason = "cold branch outside healthcare: no sourced length limit (governing test is cold < 20 °C within 2 minutes, HSG274 Pt 2)" };
            if (outsideDiameterMm <= 0)
                return new DeadLegLimitResult { NotCheckedReason = "hot branch with no outside diameter" };
            foreach (var row in d.UninsulatedHotByOd)
                if (!row.UpToOdMm.HasValue || outsideDiameterMm <= row.UpToOdMm.Value + 1e-9)
                    return new DeadLegLimitResult
                    {
                        LimitM = row.MaxLengthM,
                        Basis = $"uninsulated hot pipe OD {outsideDiameterMm:0.#} mm ≤ {row.MaxLengthM:0.#} m ({d.UninsulatedHotSource})",
                        Verify = d.UninsulatedHotVerify
                    };
            return new DeadLegLimitResult { NotCheckedReason = "no BS 8558 row for this diameter" };
        }

        /// <summary>A panel override may only tighten a limit, never relax it.
        /// Zero or less means no override.</summary>
        public static double TightenOnly(double standardLimit, double overrideValue, out bool overrideIgnored)
        {
            overrideIgnored = false;
            if (overrideValue <= 0) return standardLimit;
            if (overrideValue > standardLimit + 1e-9) { overrideIgnored = true; return standardLimit; }
            return overrideValue;
        }
    }
}
