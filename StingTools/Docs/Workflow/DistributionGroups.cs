// DistributionGroups.cs — template engine v1.1 (S18).
//
// Persistent distribution groups for transmittals and deliverable issues.
// Stored in _BIM_COORD/distribution_groups.json; suggested per deliverable
// via (type, role, suitability) matching rules.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace Planscape.Docs.Workflow
{
    public class DistributionGroup
    {
        [JsonProperty("id")]          public string Id { get; set; }
        [JsonProperty("name")]        public string Name { get; set; }
        [JsonProperty("description")] public string Description { get; set; }
        [JsonProperty("members")]     public List<DistributionMember> Members { get; set; } = new List<DistributionMember>();
        [JsonProperty("applies_types")]      public List<string> AppliesTypes { get; set; } = new List<string>();
        [JsonProperty("applies_roles")]      public List<string> AppliesRoles { get; set; } = new List<string>();
        [JsonProperty("applies_suitabilities")] public List<string> AppliesSuitabilities { get; set; } = new List<string>();
    }

    public class DistributionMember
    {
        [JsonProperty("name")]     public string Name { get; set; }
        [JsonProperty("email")]    public string Email { get; set; }
        [JsonProperty("role")]     public string Role { get; set; }
        [JsonProperty("company")]  public string Company { get; set; }
        [JsonProperty("delivery")] public string Delivery { get; set; } = "TO"; // TO|CC|BCC
    }

    public static class DistributionGroups
    {
        public static List<DistributionGroup> LoadAll(Document doc)
        {
            string path = StorePath(doc);
            if (!File.Exists(path)) return new List<DistributionGroup>();
            try
            {
                return JsonConvert.DeserializeObject<List<DistributionGroup>>(File.ReadAllText(path))
                       ?? new List<DistributionGroup>();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"DistributionGroups: load failed: {ex.Message}");
                return new List<DistributionGroup>();
            }
        }

        public static void Save(Document doc, List<DistributionGroup> groups)
        {
            string path = StorePath(doc);
            string tmp  = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(groups ?? new List<DistributionGroup>(),
                Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Returns the highest-match group for a deliverable, or null.</summary>
        public static DistributionGroup SuggestFor(Document doc, dynamic deliverable)
        {
            if (deliverable == null) return null;
            var groups = LoadAll(doc);
            if (groups.Count == 0) return null;

            string type = Safe(() => (string)deliverable.Type) ?? "";
            string role = Safe(() => (string)deliverable.RoleCode) ?? "";
            string suit = Safe(() => (string)deliverable.Suitability) ?? "";

            return groups
                .Select(g => (g, score: MatchScore(g, type, role, suit)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.g)
                .FirstOrDefault();
        }

        private static int MatchScore(DistributionGroup g, string type, string role, string suit)
        {
            int s = 0;
            if (g.AppliesTypes?.Contains(type, StringComparer.OrdinalIgnoreCase) == true) s += 3;
            if (g.AppliesRoles?.Contains(role, StringComparer.OrdinalIgnoreCase) == true) s += 2;
            if (g.AppliesSuitabilities?.Contains(suit, StringComparer.OrdinalIgnoreCase) == true) s += 1;
            return s;
        }

        private static string Safe(Func<string> f) { try { return f(); } catch { return null; } }

        private static string StorePath(Document doc)
        {
            string root = ResolveProjectRoot(doc);
            string dir  = Path.Combine(root, "_BIM_COORD");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "distribution_groups.json");
        }

        private static string ResolveProjectRoot(Document doc)
        {
            try
            {
                string p = doc?.PathName;
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = Path.GetDirectoryName(p);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
                }
            }
            catch { /* ignored */ }
            return Path.Combine(Path.GetTempPath(), "Planscape", "BIMCoord");
        }
    }
}
