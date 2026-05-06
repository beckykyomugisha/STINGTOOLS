// StingTools — Symbol Standards registry (Phase 175)
//
// Per-process cache over STING_SYMBOL_STANDARDS.json + STING_MIXED_STANDARD_PROFILES.json.
// Mirrors AecFilterRegistry / DrawingTypeRegistry pattern.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace StingTools.Core.Symbols
{
    public static class SymbolStandardRegistry
    {
        private static readonly object _lock = new object();
        private static SymbolStandardsFile _standards;
        private static MixedStandardProfilesFile _profiles;
        private static bool _loaded;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_lock)
            {
                if (_loaded) return;
                _standards = LoadStandards();
                _profiles  = LoadProfiles();
                _loaded = true;
            }
        }

        public static void Reload()
        {
            lock (_lock) { _loaded = false; _standards = null; _profiles = null; }
        }

        public static StandardDefinition GetStandard(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id)) return null;
            return _standards.Standards.TryGetValue(id, out var s) ? s : null;
        }

        public static string GetFallback(string id)
        {
            EnsureLoaded();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string current = id;
            while (!string.IsNullOrEmpty(current) && seen.Add(current))
            {
                if (_standards.FallbackChain.TryGetValue(current, out var next)
                    && !string.IsNullOrWhiteSpace(next))
                    current = next;
                else
                    break;
            }
            return current ?? "IEC";
        }

        public static string GetScaleTier(int viewScale)
        {
            EnsureLoaded();
            int large    = TryGetThreshold("large",    75);
            int standard = TryGetThreshold("standard", 150);
            int small    = TryGetThreshold("small",    300);
            if (viewScale <= large)    return "large";
            if (viewScale <= standard) return "standard";
            if (viewScale <= small)    return "small";
            return "diagram";
        }

        private static int TryGetThreshold(string key, int defaultValue)
        {
            return _standards.ScaleTierThresholds.TryGetValue(key, out var v) ? v : defaultValue;
        }

        public static AnnotationRules GetAnnotationRules(string id)
        {
            var s = GetStandard(id);
            return s?.AnnotationRules ?? new AnnotationRules();
        }

        public static MixedStandardProfile GetMixedProfile(string profileId)
        {
            EnsureLoaded();
            if (_profiles?.Profiles == null) return null;
            return _profiles.Profiles.FirstOrDefault(p =>
                string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        }

        public static MixedStandardProfile GetDefaultProfile()
        {
            EnsureLoaded();
            if (_profiles?.Profiles == null || _profiles.Profiles.Count == 0) return null;
            return _profiles.Profiles.FirstOrDefault(p => p.IsDefault) ?? _profiles.Profiles[0];
        }

        public static IReadOnlyList<MixedStandardProfile> ListProfiles()
        {
            EnsureLoaded();
            return _profiles?.Profiles ?? new List<MixedStandardProfile>();
        }

        public static string GetProfileForDiscipline(string profileId, string discipline)
        {
            var p = GetMixedProfile(profileId) ?? GetDefaultProfile();
            if (p?.DisciplineMappings == null) return "IEC";
            return p.DisciplineMappings.TryGetValue(discipline ?? "", out var std) ? std : "IEC";
        }

        public static IReadOnlyCollection<string> ListStandards()
        {
            EnsureLoaded();
            return _standards?.Standards?.Keys?.ToList() ?? new List<string>();
        }

        // ── Loaders ─────────────────────────────────────────────────────

        private static SymbolStandardsFile LoadStandards()
        {
            try
            {
                var path = StingTools.Core.StingToolsApp.FindDataFile("STING_SYMBOL_STANDARDS.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return JsonConvert.DeserializeObject<SymbolStandardsFile>(File.ReadAllText(path))
                        ?? Defaults();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SymbolStandardRegistry: standards load failed — {ex.Message}");
            }
            return Defaults();
        }

        private static MixedStandardProfilesFile LoadProfiles()
        {
            try
            {
                var path = StingTools.Core.StingToolsApp.FindDataFile("STING_MIXED_STANDARD_PROFILES.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return JsonConvert.DeserializeObject<MixedStandardProfilesFile>(File.ReadAllText(path))
                        ?? new MixedStandardProfilesFile();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"SymbolStandardRegistry: profiles load failed — {ex.Message}");
            }
            return new MixedStandardProfilesFile();
        }

        private static SymbolStandardsFile Defaults()
        {
            var f = new SymbolStandardsFile();
            f.Standards["IEC"] = new StandardDefinition
            {
                Name = "IEC 60617", SymbolSizeMm = 3.0,
                AnnotationRules = new AnnotationRules { LabelPosition = "Above", TextHeightMm = 2.0 }
            };
            f.ScaleTierThresholds["large"]    = 75;
            f.ScaleTierThresholds["standard"] = 150;
            f.ScaleTierThresholds["small"]    = 300;
            return f;
        }
    }
}
