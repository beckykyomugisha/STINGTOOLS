// StingTools v4 MVP — smart fabrication grouping engine (#7).
//
// Greedy bin-packer that splits a filtered element set into spool-
// sized groups honouring STING_FAB_RULES.json: max_length_mm and
// max_bends per discipline. Reuses the FabRules loader already in
// AssemblyGrouper so the two engines stay consistent. Produces an
// ordered list of (group key, ElementId list) tuples the workspace
// dialog renders in the Package preview with name previews like
// SP-P-CHW-L02-0001, SP-P-CHW-L02-0002 once a spool overflows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Fabrication;

namespace StingTools.Commands.Fabrication
{
    public class FabricationGroupResult
    {
        public string Key          { get; set; } = "";
        public string Discipline   { get; set; } = "";
        public string System       { get; set; } = "";
        public string Level        { get; set; } = "";
        public int    Seq          { get; set; } = 1;
        public double AccumulatedLengthMm { get; set; }
        public int    AccumulatedBends    { get; set; }
        public List<ElementId> ElementIds { get; } = new List<ElementId>();

        public string AssemblyNamePreview => $"SP-{Safe(Discipline)}-{Safe(System)}-{Safe(Level)}-{Seq:0000}";
        private static string Safe(string s) => string.IsNullOrWhiteSpace(s) ? "XX" : s.Trim();
    }

    public static class FabricationGrouper
    {
        /// <summary>
        /// Smart pack an arbitrary element set into one or more
        /// spool groups, respecting per-discipline length / bend
        /// limits from STING_FAB_RULES.json.
        /// </summary>
        public static List<FabricationGroupResult> Pack(Document doc, IList<ElementId> ids)
        {
            var rulesMap = LoadRulesByDiscipline();
            var result = new List<FabricationGroupResult>();

            // First group by (discipline, system, level) like the
            // existing AssemblyGrouper — then overflow inside each
            // group when length/bend limits are exceeded.
            var buckets = ids.Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .GroupBy(e => new
                {
                    Discipline = DisciplineCode(e),
                    System     = Read(e, "PLM_SYS_TXT") + Read(e, "MEC_SYS_TXT") + Read(e, "ELC_SYS_TXT"),
                    Level      = Read(e, "ASS_LVL_COD_TXT"),
                });

            foreach (var b in buckets.OrderBy(b => b.Key.Discipline).ThenBy(b => b.Key.System).ThenBy(b => b.Key.Level))
            {
                string disc = b.Key.Discipline;
                string sys  = string.IsNullOrWhiteSpace(b.Key.System) ? "GEN" : b.Key.System;
                string lvl  = string.IsNullOrWhiteSpace(b.Key.Level)  ? "XX"  : b.Key.Level;
                var rule = ResolveRuleFor(rulesMap, disc);
                int seq = 1;
                var current = NewGroup(disc, sys, lvl, seq);
                foreach (var el in b.OrderBy(e => e.Id.Value))
                {
                    double addLenMm = ElementLengthMm(el);
                    int addBends    = IsBend(el) ? 1 : 0;

                    if (current.ElementIds.Count > 0
                        && (current.AccumulatedLengthMm + addLenMm > rule.MaxLengthMm
                         || current.AccumulatedBends     + addBends > rule.MaxBends))
                    {
                        result.Add(current);
                        seq++;
                        current = NewGroup(disc, sys, lvl, seq);
                    }

                    current.ElementIds.Add(el.Id);
                    current.AccumulatedLengthMm += addLenMm;
                    current.AccumulatedBends    += addBends;
                }
                if (current.ElementIds.Count > 0) result.Add(current);
            }

            return result;
        }

        private static FabricationGroupResult NewGroup(string disc, string sys, string lvl, int seq)
            => new FabricationGroupResult { Key = $"{disc}|{sys}|{lvl}|{seq:0000}", Discipline = disc, System = sys, Level = lvl, Seq = seq };

        private static string DisciplineCode(Element e)
        {
            if (e?.Category == null) return "GEN";
            int bic = (int)e.Category.Id.Value;
            return bic switch
            {
                (int)BuiltInCategory.OST_PipeCurves or (int)BuiltInCategory.OST_FlexPipeCurves
                    or (int)BuiltInCategory.OST_PipeFitting or (int)BuiltInCategory.OST_PipeAccessory => "P",
                (int)BuiltInCategory.OST_DuctCurves or (int)BuiltInCategory.OST_FlexDuctCurves
                    or (int)BuiltInCategory.OST_DuctFitting or (int)BuiltInCategory.OST_DuctAccessory => "M",
                (int)BuiltInCategory.OST_Conduit or (int)BuiltInCategory.OST_ConduitFitting
                    or (int)BuiltInCategory.OST_CableTray or (int)BuiltInCategory.OST_CableTrayFitting => "E",
                _ => "GEN",
            };
        }

        private static double ElementLengthMm(Element e)
        {
            try
            {
                if (e?.Location is LocationCurve lc && lc.Curve != null)
                    return lc.Curve.Length * 304.8;
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return 0;
        }

        private static bool IsBend(Element e)
        {
            if (e?.Category == null) return false;
            int bic = (int)e.Category.Id.Value;
            return bic == (int)BuiltInCategory.OST_PipeFitting
                || bic == (int)BuiltInCategory.OST_DuctFitting
                || bic == (int)BuiltInCategory.OST_ConduitFitting
                || bic == (int)BuiltInCategory.OST_CableTrayFitting;
        }

        private static string Read(Element e, string p)
        { try { return e?.LookupParameter(p)?.AsString() ?? ""; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return ""; } }

        // ── Rules loader (mirrors AssemblyGrouper.LoadRules) ──────

        private class DiscRule
        {
            public double MaxLengthMm { get; set; } = 6000;
            public int    MaxBends    { get; set; } = 4;
        }

        private class RulesFile
        {
            public Dictionary<string, DiscRule> ByDiscipline { get; set; }
                = new Dictionary<string, DiscRule>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, DiscRule> LoadRulesByDiscipline()
        {
            try
            {
                string path = StingToolsApp.FindDataFile("STING_FAB_RULES.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new Dictionary<string, DiscRule>(StringComparer.OrdinalIgnoreCase);
                var rf = JsonConvert.DeserializeObject<RulesFile>(File.ReadAllText(path));
                return rf?.ByDiscipline ?? new Dictionary<string, DiscRule>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FabricationGrouper.LoadRulesByDiscipline: {ex.Message}");
                return new Dictionary<string, DiscRule>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static DiscRule ResolveRuleFor(Dictionary<string, DiscRule> map, string disc)
        {
            if (map != null && map.TryGetValue(disc ?? "", out var r)) return r;
            return new DiscRule();
        }
    }
}
