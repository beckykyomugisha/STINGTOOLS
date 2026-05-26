// StingTools Phase 183 — generalised pressure-regime validator.
//
// Closes gap C3 from the post-Phase-181 review. The healthcare-only
// PressureRegimeValidator (HTM 03-01 + ASHRAE 170) has the right shape
// but its class table is hardcoded in StingTools.Standards.HTM and only
// fires under PRJ_ORG_HEALTH_FACILITY_TYPE_TXT.
//
// This validator works the same way but reads room-class → regime/Δp/ACH
// tables from Data/STING_PRESSURE_REGIMES.json. Project picks a profile
// via PRJ_ORG_PRESSURE_PROFILE_TXT. Supports:
//   - healthcare-htm03-01        (mirrors the historic healthcare rules)
//   - gmp-annex1                 (EU GMP Annex 1, 2022)
//   - iso-14644-cleanroom        (ISO 14644-4 commercial cleanroom)
//   - bs-en-12128-lab            (containment laboratory)
//
// Designed to coexist with the healthcare validator — RunAllValidators
// runs the healthcare one when PRJ_ORG_HEALTH_FACILITY_TYPE_TXT is set,
// and this one when PRJ_ORG_PRESSURE_PROFILE_TXT is set. Both can run
// (e.g. a hospital cleanroom).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Validation.Mep
{
    public class PressureRegimeClass
    {
        public string Id      { get; set; } = "";
        public string Regime  { get; set; } = "";   // POS / NEG / EITHER
        public double DeltaPa { get; set; }
        public double MinAch  { get; set; }
        public string Notes   { get; set; } = "";
    }

    public class PressureRegimeProfile
    {
        public string Id          { get; set; } = "";
        public string Label       { get; set; } = "";
        public string Standard    { get; set; } = "";
        public string ClassParam  { get; set; } = "CLN_ROOM_CLASS_TXT";
        public string DeltaParam  { get; set; } = "CLN_PRESS_DELTA_DESIGN_PA_NR";
        public string RegimeParam { get; set; } = "CLN_PRESS_REGIME_TXT";
        public string AchParam    { get; set; } = "HVC_AIR_CHANGES_PER_HR";
        public Dictionary<string, PressureRegimeClass> Classes { get; }
            = new Dictionary<string, PressureRegimeClass>(StringComparer.OrdinalIgnoreCase);
    }

    public static class PressureRegimeProfileRegistry
    {
        public const string DataFileName = "STING_PRESSURE_REGIMES.json";
        private static readonly object _lock = new object();
        private static Dictionary<string, PressureRegimeProfile> _cached;

        public static PressureRegimeProfile GetProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return null;
            EnsureLoaded();
            return _cached != null && _cached.TryGetValue(profileId, out var p) ? p : null;
        }

        public static IEnumerable<string> ListProfiles()
        {
            EnsureLoaded();
            return _cached?.Keys ?? Enumerable.Empty<string>();
        }

        public static void Reload()
        {
            lock (_lock) { _cached = null; }
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_cached != null) return;
                _cached = new Dictionary<string, PressureRegimeProfile>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    string path = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    var j = JObject.Parse(File.ReadAllText(path));
                    var profiles = j["profiles"] as JObject;
                    if (profiles == null) return;
                    foreach (var kv in profiles)
                    {
                        var po = kv.Value as JObject; if (po == null) continue;
                        var profile = new PressureRegimeProfile
                        {
                            Id          = kv.Key,
                            Label       = (string)po["label"] ?? kv.Key,
                            Standard    = (string)po["_standard"] ?? "",
                            ClassParam  = (string)po["classParam"]  ?? "CLN_ROOM_CLASS_TXT",
                            DeltaParam  = (string)po["deltaParam"]  ?? "CLN_PRESS_DELTA_DESIGN_PA_NR",
                            RegimeParam = (string)po["regimeParam"] ?? "CLN_PRESS_REGIME_TXT",
                            AchParam    = (string)po["achParam"]    ?? "HVC_AIR_CHANGES_PER_HR"
                        };
                        var classes = po["classes"] as JArray;
                        if (classes != null)
                        {
                            foreach (var co in classes.OfType<JObject>())
                            {
                                var cls = new PressureRegimeClass
                                {
                                    Id      = (string)co["id"] ?? "",
                                    Regime  = (string)co["regime"] ?? "",
                                    DeltaPa = (double?)co["deltaPa"] ?? 0,
                                    MinAch  = (double?)co["minAch"]  ?? 0,
                                    Notes   = (string)co["notes"] ?? ""
                                };
                                if (!string.IsNullOrEmpty(cls.Id))
                                    profile.Classes[cls.Id] = cls;
                            }
                        }
                        _cached[profile.Id] = profile;
                    }
                    StingLog.Info($"PressureRegimeProfileRegistry: {_cached.Count} profiles loaded.");
                }
                catch (Exception ex) { StingLog.Warn($"PressureRegime load: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Profile-driven pressure-regime + ACH check. Sibling to the healthcare
    /// validator; emits the same shape of <see cref="ValidationResult"/>.
    /// </summary>
    public class GeneralPressureRegimeValidator
    {
        private const string Tag = "GeneralPressureRegimeValidator";

        public string Name => "GeneralPressureRegimeValidator";

        public List<ValidationResult> Validate(Document doc)
        {
            var res = new List<ValidationResult>();
            if (doc == null) return res;

            string profileId = "";
            try
            {
                var p = doc.ProjectInformation?.LookupParameter(ParamRegistry.PRJ_ORG_PRESSURE_PROFILE_TXT);
                if (p?.HasValue == true && p.StorageType == StorageType.String)
                    profileId = (p.AsString() ?? "").Trim();
            }
            catch (Exception ex) { StingLog.Warn($"PressureProfile param: {ex.Message}"); }
            if (string.IsNullOrEmpty(profileId)) return res; // disabled — let healthcare validator handle it

            var profile = PressureRegimeProfileRegistry.GetProfile(profileId);
            if (profile == null)
            {
                StingLog.Warn($"GeneralPressureRegimeValidator: unknown profile '{profileId}'");
                return res;
            }

            // Walk all rooms/spaces, evaluate against the profile classes.
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType().ToList();

            foreach (var r in rooms)
            {
                string classId = ReadString(r, profile.ClassParam);
                if (string.IsNullOrEmpty(classId)) continue;
                if (!profile.Classes.TryGetValue(classId, out var cls)) continue;

                // 1. Regime
                if (!string.IsNullOrEmpty(cls.Regime) && cls.Regime != "EITHER")
                {
                    string actualRegime = ReadString(r, profile.RegimeParam);
                    if (!string.IsNullOrEmpty(actualRegime)
                        && !string.Equals(actualRegime, cls.Regime, StringComparison.OrdinalIgnoreCase))
                    {
                        res.Add(new ValidationResult(r.Id, ValidationSeverity.Error,
                            "PRESS.REGIME",
                            $"Room {r.Name} class {classId} expects {cls.Regime} pressure but is {actualRegime} [{profile.Standard}]",
                            Tag));
                    }
                }

                // 2. Δp
                if (cls.DeltaPa > 0)
                {
                    double actualPa = ReadDouble(r, profile.DeltaParam);
                    if (actualPa > 0 && actualPa < cls.DeltaPa - 1)
                    {
                        res.Add(new ValidationResult(r.Id, ValidationSeverity.Warning,
                            "PRESS.DELTA_PA_LOW",
                            $"Room {r.Name} class {classId} Δp={actualPa:F1} Pa < required {cls.DeltaPa} Pa [{profile.Standard}]",
                            Tag));
                    }
                }

                // 3. Minimum ACH
                if (cls.MinAch > 0)
                {
                    double actualAch = ReadDouble(r, profile.AchParam);
                    if (actualAch > 0 && actualAch < cls.MinAch - 0.5)
                    {
                        res.Add(new ValidationResult(r.Id, ValidationSeverity.Error,
                            "PRESS.ACH.LOW",
                            $"Room {r.Name} class {classId} ACH={actualAch:F1} < required {cls.MinAch} [{profile.Standard}]",
                            Tag));
                    }
                }
            }

            return res;
        }

        private static string ReadString(Element e, string paramName)
        {
            try
            {
                var p = e?.LookupParameter(paramName);
                if (p == null) return "";
                if (p.StorageType == StorageType.String) return p.AsString() ?? "";
                if (p.StorageType == StorageType.Integer) return p.AsInteger().ToString();
            }
            catch { }
            return "";
        }

        private static double ReadDouble(Element e, string paramName)
        {
            try
            {
                var p = e?.LookupParameter(paramName);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch { }
            return 0;
        }
    }
}
