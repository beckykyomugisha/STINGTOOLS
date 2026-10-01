using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// B6 — MaterialClass → discipline affinity lookup.
    /// First-match-wins regex over MaterialClass returning
    /// (PrimaryDiscipline, SecondaryDisciplines).
    /// </summary>
    public class DisciplineAffinity
    {
        public Regex Pattern { get; set; }
        public string Primary { get; set; }
        public List<string> Secondary { get; set; } = new List<string>();
        public string Notes { get; set; }
    }

    public static class MaterialDisciplineAffinity
    {
        private static List<DisciplineAffinity> _rules;
        private static readonly object _lock = new object();

        public static void Reload() { lock (_lock) { _rules = null; } }

        public static DisciplineAffinity Resolve(string materialClass)
        {
            if (string.IsNullOrWhiteSpace(materialClass)) return null;
            EnsureLoaded();
            lock (_lock)
            {
                if (_rules == null) return null;
                foreach (var r in _rules)
                {
                    try { if (r.Pattern.IsMatch(materialClass)) return r; }
                    catch (Exception ex) { StingLog.WarnRateLimited("Affinity.Match", $"Affinity match: {ex.Message}"); }
                }
            }
            return null;
        }

        public static string ResolvePrimary(string materialClass) => Resolve(materialClass)?.Primary;

        /// <summary>True when <paramref name="discipline"/> is an allowed
        /// discipline for materials of class <paramref name="materialClass"/>
        /// (primary or secondary). False when the affinity is known but
        /// the discipline isn't on the list. True (permissive) when no
        /// affinity rule matches.</summary>
        public static bool IsAllowedOn(string materialClass, string discipline)
        {
            var aff = Resolve(materialClass);
            if (aff == null) return true; // unknown class — permissive
            if (string.Equals(aff.Primary, discipline, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var s in aff.Secondary)
                if (string.Equals(s, discipline, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_rules != null) return;
                _rules = new List<DisciplineAffinity>();
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_MATERIAL_DISCIPLINE_AFFINITY.csv");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    // DSCH-2: columns by header name, not position.
                    var t = CsvTable.Parse(File.ReadAllLines(path), StingToolsApp.ParseCsvLine);
                    var missing = t.Missing("MaterialClassPattern", "PrimaryDiscipline");
                    if (missing.Count > 0)
                    {
                        StingLog.Warn($"MaterialDisciplineAffinity: {path} header has no {string.Join(", ", missing)} column; no rules loaded.");
                        return;
                    }
                    int iPrimary = t.Col("PrimaryDiscipline");
                    foreach (var r in t.Rows)
                    {
                        if (r.Count <= iPrimary) continue;
                        string pat = r["MaterialClassPattern"];
                        if (string.IsNullOrEmpty(pat)) continue;
                        try
                        {
                            var aff = new DisciplineAffinity
                            {
                                Pattern = new Regex(pat, RegexOptions.Compiled),
                                Primary = r["PrimaryDiscipline"],
                                Notes = r["Notes"],
                            };
                            string secondary = r["SecondaryDisciplines"];
                            if (!string.IsNullOrWhiteSpace(secondary))
                                aff.Secondary.AddRange(secondary.Split(','));
                            _rules.Add(aff);
                        }
                        catch (Exception ex) { StingLog.Warn($"Affinity bad pattern '{pat}': {ex.Message}"); }
                    }
                    StingLog.Info($"MaterialDisciplineAffinity: loaded {_rules.Count} rule(s).");
                }
                catch (Exception ex) { StingLog.Warn($"MaterialDisciplineAffinity load: {ex.Message}"); }
            }
        }
    }
}
