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
                    // C5: A user JSON entry with no Verdict and a matching Id
                    //     against an existing built-in is treated as a Params
                    //     override — merge values into the built-in's Params
                    //     dict so the existing predicate picks them up.
                    if (string.IsNullOrEmpty(u.Verdict) && u.Params != null && u.Params.Count > 0)
                    {
                        var match = all.Find(r => string.Equals(r.Id, u.Id, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            foreach (var kv in u.Params) match.Params[kv.Key] = kv.Value;
                            continue;
                        }
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
