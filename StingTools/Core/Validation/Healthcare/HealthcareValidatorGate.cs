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
        // Cache the parsed profiles JSON keyed by (path, mtime) so we don't re-read +
        // re-parse on every Validate call. Refreshes automatically when the file is edited.
        private static (string path, DateTime mtime, JObject profiles) _cache;
        private static readonly object _cacheLock = new object();

        private static readonly HashSet<string> _allValidators =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "PressureRegimeValidator","MgasFlowValidator","EesBranchValidator",
                "WaterSafetyValidator","RadShieldValidator","AdjacencyValidator",
                "AntiLigatureValidator","RdsCompletenessValidator","IoTStalenessValidator",
                "StructuralLoadValidator","AcousticValidator","AdvancedRadShieldValidator",
                "EndoscopeTraceValidator","EesResilienceValidator","WasteFlowValidator",
                "RtlsCoverageValidator"
            };

        public static HashSet<string> AllowedValidators(Document doc)
        {
            // Read the requested profile from project info.
            string profile = "FULL";
            try {
                var p = doc?.ProjectInformation?.LookupParameter("PRJ_ORG_HEALTH_PACK_PROFILE_TXT");
                if (p?.HasValue == true && p.StorageType == StorageType.String)
                    profile = (p.AsString() ?? "FULL").Trim().ToUpperInvariant();
            } catch { }
            if (profile == "FULL" || string.IsNullOrEmpty(profile))
                return new HashSet<string>(_allValidators, StringComparer.OrdinalIgnoreCase);

            try
            {
                var profilesNode = LoadCachedProfiles();
                if (profilesNode == null || !(profilesNode[profile] is JObject prof))
                    return new HashSet<string>(_allValidators, StringComparer.OrdinalIgnoreCase);
                var list = prof["validators"] as JArray;
                if (list == null) return new HashSet<string>(_allValidators, StringComparer.OrdinalIgnoreCase);
                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in list)
                {
                    var s = t?.ToString() ?? "";
                    if (s.Equals("all", StringComparison.OrdinalIgnoreCase))
                        return new HashSet<string>(_allValidators, StringComparer.OrdinalIgnoreCase);
                    allowed.Add(s);
                }
                return allowed;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HealthcareValidatorGate fallback to FULL: {ex.Message}");
                return new HashSet<string>(_allValidators, StringComparer.OrdinalIgnoreCase);
            }
        }

        private static JObject LoadCachedProfiles()
        {
            var path = Path.Combine(StingToolsApp.DataPath, "HEALTHCARE_PACK_PROFILES.json");
            if (!File.Exists(path)) return null;
            var mtime = File.GetLastWriteTimeUtc(path);
            lock (_cacheLock)
            {
                if (_cache.profiles != null && _cache.path == path && _cache.mtime == mtime)
                    return _cache.profiles;
                var root = JObject.Parse(File.ReadAllText(path));
                var profilesNode = root["profiles"] as JObject;
                _cache = (path, mtime, profilesNode);
                return profilesNode;
            }
        }
    }
}
