// ClashRuleLibrary.cs — JSON-editable rules that supplement the built-in rule set.
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
                    ClashVerdict v = u.Verdict == "Pseudo" ? ClashVerdict.Pseudo : ClashVerdict.Intentional;
                    all.Add(new ClashRuleDefinition
                    {
                        Id = u.Id, Description = u.Description, FilterA = u.FilterA, FilterB = u.FilterB,
                        Predicate = (h, a, b) =>
                        {
                            if (u.VolumeBelowMm3.HasValue && h.VolumeMm3 >= u.VolumeBelowMm3.Value) return ClashVerdict.Keep;
                            if (u.VolumeAboveMm3.HasValue && h.VolumeMm3 <= u.VolumeAboveMm3.Value) return ClashVerdict.Keep;
                            return v;
                        }
                    });
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
