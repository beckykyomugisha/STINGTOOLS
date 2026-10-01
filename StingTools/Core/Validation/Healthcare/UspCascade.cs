// UspCascade — USP <797> / <800> pharmacy cleanroom pressure cascade (Revit-free).
//
// The ONE owner of the pressure and air-change limits is
// Data/Healthcare/Specialist/STING_HC_PHARMACY_USP.json (DSCH-25). Limits differ
// by room: a <797> buffer room is POSITIVE to its ante-room by at least
// 0.020 in w.c. (4.98 Pa); a <800> C-SEC is NEGATIVE to all adjacent areas by
// 0.01–0.03 in w.c. (2.49–7.47 Pa). The file holds inches of water column, as
// USP states them; Pa is derived here (1 in w.c. = 249.0889 Pa).
//
// A panel override may only TIGHTEN a limit. A room with no recorded pressure
// differential or air-change rate is NOT CHECKED, never passed.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Validation.Healthcare
{
    public class UspRoomSpec
    {
        public string  Code            { get; set; } = "";
        /// <summary>CLN_ROOM_CLASS_TXT value this row audits; empty = no room class
        /// identifies it yet (reference only).</summary>
        public string  RoomClass       { get; set; } = "";
        public string  Standard        { get; set; } = "";
        public string  Iso             { get; set; } = "";
        public double  AchMin          { get; set; }
        public string  Polarity        { get; set; } = "";
        public string  RelativeTo      { get; set; } = "";
        public double  MinInWc         { get; set; }
        public double? MaxInWc         { get; set; }
        public bool    ExternalExhaust { get; set; }
        public string  Source          { get; set; } = "";
        public string  Verify          { get; set; } = "";
    }

    public class UspCascadeFile
    {
        public double SchemaVersion { get; set; }
        public string Description { get; set; } = "";
        public List<UspRoomSpec> Rooms { get; set; } = new List<UspRoomSpec>();
        public int    RecertificationCycleMonths { get; set; }
        public string RecertificationSource { get; set; } = "";
        public string RecertificationVerify { get; set; } = "";
    }

    public class UspFinding
    {
        public string Status { get; set; } = "";   // FAIL / NOT CHECKED / INFO
        public string Code   { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public static class UspCascade
    {
        /// <summary>1 inch of water column at 4 °C, in pascals (conversion constant).</summary>
        public const double PaPerInWc = 249.0889;

        public static double MinPa(UspRoomSpec s) => Math.Round(s.MinInWc * PaPerInWc, 2);
        public static double? MaxPa(UspRoomSpec s) => s.MaxInWc.HasValue ? Math.Round(s.MaxInWc.Value * PaPerInWc, 2) : (double?)null;

        public static UspCascadeFile Parse(string json, out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(json)) { errors.Add("file is empty or missing"); return null; }
            UspCascadeFile f;
            try { f = JsonConvert.DeserializeObject<UspCascadeFile>(json); }
            catch (Exception ex) { errors.Add("not valid JSON: " + ex.Message); return null; }
            if (f?.Rooms == null || f.Rooms.Count == 0) { errors.Add("rooms is empty"); return null; }
            foreach (var r in f.Rooms)
            {
                if (string.IsNullOrWhiteSpace(r.Code)) errors.Add("room without code");
                if (r.Polarity != "POS" && r.Polarity != "NEG") errors.Add($"{r.Code}: polarity must be POS or NEG");
                if (r.MinInWc <= 0) errors.Add($"{r.Code}: minInWc must be > 0");
                if (r.MaxInWc.HasValue && r.MaxInWc.Value < r.MinInWc) errors.Add($"{r.Code}: maxInWc below minInWc");
                if (r.AchMin <= 0) errors.Add($"{r.Code}: achMin must be > 0");
                if (string.IsNullOrWhiteSpace(r.Source)) errors.Add($"{r.Code}: no source");
            }
            var dupClass = f.Rooms.Where(r => !string.IsNullOrEmpty(r.RoomClass))
                                  .GroupBy(r => r.RoomClass, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (dupClass != null) errors.Add($"room class {dupClass.Key} mapped to more than one row");
            return errors.Count == 0 ? f : null;
        }

        public static UspRoomSpec ForRoomClass(UspCascadeFile f, string roomClass) =>
            string.IsNullOrWhiteSpace(roomClass) ? null :
            f?.Rooms.FirstOrDefault(r => string.Equals(r.RoomClass, roomClass.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Checks one room. <paramref name="dpPa"/> / <paramref name="ach"/> null =
        /// not recorded. Overrides of zero or less mean none; an override looser than the
        /// standard is ignored and reported.</summary>
        public static List<UspFinding> Check(UspRoomSpec spec, string polarity, double? dpPa, double? ach,
            double dpOverridePa = 0, double achOverride = 0)
        {
            var list = new List<UspFinding>();
            if (spec == null) { list.Add(new UspFinding { Status = "NOT CHECKED", Code = "USP.SPEC", Message = "no USP cascade row for this room class" }); return list; }

            double minPa = MinPa(spec);
            if (dpOverridePa > 0)
            {
                if (dpOverridePa >= minPa) minPa = dpOverridePa;
                else list.Add(new UspFinding { Status = "INFO", Code = "USP.OVERRIDE",
                    Message = $"ΔP override {dpOverridePa:0.##} Pa is looser than {minPa:0.##} Pa ({spec.Standard}) — ignored; an override may only tighten" });
            }
            double achMin = spec.AchMin;
            if (achOverride > 0)
            {
                if (achOverride >= achMin) achMin = achOverride;
                else list.Add(new UspFinding { Status = "INFO", Code = "USP.OVERRIDE",
                    Message = $"ACH override {achOverride:0.#} is looser than {spec.AchMin:0.#} ({spec.Standard}) — ignored; an override may only tighten" });
            }

            if (string.IsNullOrWhiteSpace(polarity))
                list.Add(new UspFinding { Status = "NOT CHECKED", Code = "USP.POL", Message = $"no pressure regime recorded (CLN_PRESS_REGIME_TXT); expected {spec.Polarity}" });
            else if (!string.Equals(polarity.Trim(), spec.Polarity, StringComparison.OrdinalIgnoreCase))
                list.Add(new UspFinding { Status = "FAIL", Code = "USP.POL", Message = $"polarity {polarity} — expected {spec.Polarity} to {spec.RelativeTo} [{spec.Source}]" });

            if (!dpPa.HasValue)
                list.Add(new UspFinding { Status = "NOT CHECKED", Code = "USP.DP", Message = "no pressure differential recorded (CLN_PRESS_DELTA_DESIGN_PA_NR)" });
            else
            {
                double mag = Math.Abs(dpPa.Value);
                var maxPa = MaxPa(spec);
                if (mag < minPa - 1e-9)
                    list.Add(new UspFinding { Status = "FAIL", Code = "USP.DP", Message = $"|ΔP| {mag:0.##} Pa < {minPa:0.##} Pa minimum to {spec.RelativeTo} [{spec.Source}]" });
                else if (maxPa.HasValue && mag > maxPa.Value + 1e-9)
                    list.Add(new UspFinding { Status = "FAIL", Code = "USP.DP", Message = $"|ΔP| {mag:0.##} Pa > {maxPa.Value:0.##} Pa maximum to {spec.RelativeTo} [{spec.Source}]" });
            }

            if (!ach.HasValue)
                list.Add(new UspFinding { Status = "NOT CHECKED", Code = "USP.ACH", Message = "no air-change rate recorded (HVC_AIR_CHANGES_PER_HR)" });
            else if (ach.Value < achMin - 1e-9)
                list.Add(new UspFinding { Status = "FAIL", Code = "USP.ACH", Message = $"ACH {ach.Value:0.#} < {achMin:0.#} [{spec.Source}]" });

            return list;
        }
    }
}
