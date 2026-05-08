// Healthcare Pack H-28 — Validator gating by pack profile.
//
// Reads PRJ_ORG_HEALTH_PACK_PROFILE_TXT and HEALTHCARE_PACK_PROFILES.json
// to filter the validator chain. RunAllHealthcareValidators delegates
// to this gate before invoking individual validators.

using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace StingTools.Core.Validation.Healthcare
{
    public static class HealthcareValidatorGate
    {
        public static HashSet<string> AllowedValidators(Document doc)
        {
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "PressureRegimeValidator","MgasFlowValidator","EesBranchValidator",
                "WaterSafetyValidator","RadShieldValidator","AdjacencyValidator",
                "AntiLigatureValidator","RdsCompletenessValidator","IoTStalenessValidator",
                "StructuralLoadValidator","AcousticValidator","AdvancedRadShieldValidator",
                "EndoscopeTraceValidator","EesResilienceValidator","WasteFlowValidator",
                "RtlsCoverageValidator"
            };
            string profile = "FULL";
            try {
                var p = doc?.ProjectInformation?.LookupParameter("PRJ_ORG_HEALTH_PACK_PROFILE_TXT");
                if (p?.HasValue == true && p.StorageType == StorageType.String)
                    profile = (p.AsString() ?? "FULL").Trim().ToUpperInvariant();
            } catch { }
            if (profile == "FULL" || string.IsNullOrEmpty(profile)) return all;

            try
            {
                var path = Path.Combine(StingToolsApp.DataPath, "HEALTHCARE_PACK_PROFILES.json");
                if (!File.Exists(path)) return all;
                var root = JObject.Parse(File.ReadAllText(path));
                var profilesNode = root["profiles"] as JObject;
                if (profilesNode == null) return all;
                if (!(profilesNode[profile] is JObject prof)) return all;
                var list = prof["validators"] as JArray;
                if (list == null) return all;
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in list)
                {
                    var s = t?.ToString() ?? "";
                    if (s.Equals("all", StringComparison.OrdinalIgnoreCase)) return all;
                    allowed.Add(s);
                }
                return allowed;
            }
            catch (Exception ex) { StingLog.Warn($"HealthcareValidatorGate fallback to FULL: {ex.Message}"); return all; }
        }
    }
}
