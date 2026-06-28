// ══════════════════════════════════════════════════════════════════════════
//  BoqWbsMap.cs — Phase 2E. User-defined WBS / CBS assignment rules.
//
//  Rules assign a Work-/Cost-Breakdown-Structure code to each BOQ line from its
//  attributes (category / discipline / NRM2 section / level / zone). First-match
//  wins. Persisted to <project>/_BIM_COORD/boq_wbs_map.json (same pattern as
//  rate_feeds.json). Applied on every build (so map edits take effect without a
//  cache rebuild); a line with no matching rule falls back to the linked 4D
//  ScheduleTask's WBS so the programme and the bill share one structure.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.BOQ
{
    public class BoqWbsRule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string MatchCategory { get; set; } = "*";
        public string MatchDiscipline { get; set; } = "*";
        public string MatchNrm2Section { get; set; } = "*";
        public string MatchLevel { get; set; } = "*";
        public string MatchZone { get; set; } = "*";
        public string Wbs { get; set; } = "";
        public string Cbs { get; set; } = "";

        public bool Matches(BOQLineItem line)
        {
            if (line == null) return false;
            return F(MatchCategory, line.Category)
                && F(MatchDiscipline, line.Discipline)
                && F(MatchNrm2Section, line.NRM2Section)
                && F(MatchLevel, line.Level)
                && F(MatchZone, line.Zone);
        }

        private static bool F(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public class BoqWbsMap
    {
        public string SchemaVersion = "1.0";
        public List<BoqWbsRule> Rules { get; set; } = new List<BoqWbsRule>();
    }

    internal static class BoqWbsMapStore
    {
        private const string FileName = "boq_wbs_map.json";

        public static BoqWbsMap Load(Document doc)
        {
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new BoqWbsMap();
                return JsonConvert.DeserializeObject<BoqWbsMap>(File.ReadAllText(path)) ?? new BoqWbsMap();
            }
            catch (Exception ex) { StingLog.Warn($"BoqWbsMapStore.Load: {ex.Message}"); return new BoqWbsMap(); }
        }

        public static bool Save(Document doc, BoqWbsMap map)
        {
            if (map == null) return false;
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path)) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(map, Formatting.Indented));
                return true;
            }
            catch (Exception ex) { StingLog.Warn($"BoqWbsMapStore.Save: {ex.Message}"); return false; }
        }

        private static string ResolvePath(Document doc)
        {
            string parent = Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(parent)) return null;
            return Path.Combine(parent, "_BIM_COORD", FileName);
        }
    }
}
