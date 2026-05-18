using StingTools.Core;
// ClashRuleLibrary.cs — JSON-editable rules that supplement the built-in rule set.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace StingTools.Core.Clash
{
    public sealed class UserClashRuleJson
    {
        public string Id;
        public string Description;
        public string FilterA;
        public string FilterB;
        public string Verdict;   // "Intentional" or "Pseudo"
        public float? VolumeBelowMm3;   // apply only if volume below this
        public float? VolumeAboveMm3;
        // C5: Override params on a built-in rule (matched by Id) without
        // having to redefine the rule from scratch. e.g.
        //   { "Id": "R008_STRUCTURAL_JOINT", "Params": { "volume_threshold_mm3": 1.0e9 } }
        public Dictionary<string, double> Params;
    }

    public static class ClashRuleLibrary
    {
        public static List<ClashRuleDefinition> LoadAugmented(string jsonPath)
        {
            var all = ClashRule.BuiltIns();
            if (!File.Exists(jsonPath)) return all;
            try
            {
                var user = JsonConvert.DeserializeObject<List<UserClashRuleJson>>(File.ReadAllText(jsonPath)) ?? new List<UserClashRuleJson>();
                foreach (var u in user)
                {
                    // F14: Any combination of FilterA / FilterB / Verdict /
                    //      Description / Params is now an override on a
                    //      matching built-in (matched by Id, case-insensitive).
                    //      Project authors no longer have to copy a full rule
                    //      definition just to tweak one field. C5's Params-only
                    //      override is the same path with empty other fields.
                    var match = all.Find(r => string.Equals(r.Id, u.Id, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        bool anyOverride = false;
                        if (!string.IsNullOrEmpty(u.FilterA)) { match.FilterA = u.FilterA; anyOverride = true; }
                        if (!string.IsNullOrEmpty(u.FilterB)) { match.FilterB = u.FilterB; anyOverride = true; }
                        if (!string.IsNullOrEmpty(u.Description)) { match.Description = u.Description; anyOverride = true; }
                        if (u.Params != null && u.Params.Count > 0)
                        {
                            foreach (var kv in u.Params) match.Params[kv.Key] = kv.Value;
                            anyOverride = true;
                        }
                        if (!string.IsNullOrEmpty(u.Verdict))
                        {
                            ClashVerdict overrideV = u.Verdict == "Pseudo" ? ClashVerdict.Pseudo : ClashVerdict.Intentional;
                            // Wrap the existing predicate so volume-band gates
                            // still apply (when supplied) but the verdict
                            // returned matches the override.
                            var inner = match.Predicate;
                            match.Predicate = (h, a, b, def) =>
                            {
                                if (u.VolumeBelowMm3.HasValue && h.VolumeMm3 >= u.VolumeBelowMm3.Value) return ClashVerdict.Keep;
                                if (u.VolumeAboveMm3.HasValue && h.VolumeMm3 <= u.VolumeAboveMm3.Value) return ClashVerdict.Keep;
                                return overrideV;
                            };
                            anyOverride = true;
                        }
                        if (anyOverride) continue;
                        // Fall through to "add as new" if no override fields
                        // were set (i.e. user listed only Id with nothing else).
                    }

                    ClashVerdict v = u.Verdict == "Pseudo" ? ClashVerdict.Pseudo : ClashVerdict.Intentional;
                    var def = new ClashRuleDefinition
                    {
                        Id = u.Id,
                        Description = u.Description,
                        FilterA = u.FilterA,
                        FilterB = u.FilterB,
                    };
                    if (u.Params != null) foreach (var kv in u.Params) def.Params[kv.Key] = kv.Value;
                    def.Predicate = (h, a, b, _) =>
                    {
                        if (u.VolumeBelowMm3.HasValue && h.VolumeMm3 >= u.VolumeBelowMm3.Value) return ClashVerdict.Keep;
                        if (u.VolumeAboveMm3.HasValue && h.VolumeMm3 <= u.VolumeAboveMm3.Value) return ClashVerdict.Keep;
                        return v;
                    };
                    all.Add(def);
                }
            }
            // H9: User-edited rules JSON with a syntax error previously reverted
            // silently to built-ins-only. Log so rule authors see the mistake.
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ClashRuleLibrary.LoadAugmented({jsonPath}) failed: {ex.Message}. Using built-ins only.");
            }
            return all;
        }
    }
}
