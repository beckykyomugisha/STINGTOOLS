using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// N+2 — Material-driven PROD code suffix lookup.
    ///
    /// Lets <see cref="TagConfig.GetFamilyAwareProdCode"/> append a
    /// material-aware tail to its base PROD when the element's primary
    /// material matches a rule. Closes the "Generic Beam" ambiguity:
    /// steel beam → BM-STL, concrete beam → BM-CON.
    ///
    /// Rules live in <c>Data/STING_MATERIAL_PROD_OVERRIDES.csv</c>.
    /// Columns: Category, MaterialPattern (regex), Suffix
    /// First matching rule wins. Category may be `*` for all categories.
    ///
    /// Element's primary material:
    ///   1) MATERIAL_ID_PARAM (instance Material parameter)
    ///   2) GetMaterialIds(false) first non-zero id (for compound elements)
    ///
    /// Lazy-loaded; <see cref="Reload"/> drops the cache for live edits.
    /// </summary>
    public static class MaterialProdOverrideRegistry
    {
        private static List<Rule> _rules;
        private static readonly object _lock = new object();

        private class Rule
        {
            public string Category;            // category name; "*" wildcard
            public System.Text.RegularExpressions.Regex MaterialPattern;
            public string Suffix;
        }

        public static void Reload()
        {
            lock (_lock) { _rules = null; }
        }

        public static string ResolveSuffix(Element el, string categoryName)
        {
            if (el == null) return null;
            EnsureLoaded();
            lock (_lock)
            {
                if (_rules == null || _rules.Count == 0) return null;
                string matName = ReadPrimaryMaterialName(el);
                if (string.IsNullOrEmpty(matName)) return null;
                foreach (var r in _rules)
                {
                    if (!string.IsNullOrEmpty(r.Category) && r.Category != "*" &&
                        !string.Equals(r.Category, categoryName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    try
                    {
                        if (r.MaterialPattern.IsMatch(matName)) return r.Suffix;
                    }
                    catch (Exception ex) { StingLog.Warn($"MaterialProdOverride match: {ex.Message}"); }
                }
            }
            return null;
        }

        private static string ReadPrimaryMaterialName(Element el)
        {
            try
            {
                Parameter p = el.LookupParameter("Material") ?? el.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    var mid = p.AsElementId();
                    if (mid != null && mid.Value > 0)
                        return el.Document?.GetElement(mid)?.Name;
                }
                var mats = el.GetMaterialIds(false);
                if (mats != null)
                {
                    foreach (var mid in mats)
                    {
                        if (mid != null && mid.Value > 0)
                            return el.Document?.GetElement(mid)?.Name;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadPrimaryMaterialName {el?.Id}: {ex.Message}"); }
            return null;
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_rules != null) return;
                _rules = new List<Rule>();
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_MATERIAL_PROD_OVERRIDES.csv");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        StingLog.Info("MaterialProdOverrideRegistry: no CSV found — material-aware PROD disabled.");
                        return;
                    }
                    var lines = File.ReadAllLines(path);
                    if (lines.Length < 2) return;
                    var header = StingToolsApp.ParseCsvLine(lines[0]);
                    int iCat   = Array.FindIndex(header, h => string.Equals(h, "Category",        StringComparison.OrdinalIgnoreCase));
                    int iPat   = Array.FindIndex(header, h => string.Equals(h, "MaterialPattern", StringComparison.OrdinalIgnoreCase));
                    int iSuf   = Array.FindIndex(header, h => string.Equals(h, "Suffix",          StringComparison.OrdinalIgnoreCase));
                    if (iCat < 0 || iPat < 0 || iSuf < 0)
                    { StingLog.Warn($"MaterialProdOverrideRegistry: bad header in {path}"); return; }
                    for (int li = 1; li < lines.Length; li++)
                    {
                        var f = StingToolsApp.ParseCsvLine(lines[li]);
                        if (f == null || f.Length <= iSuf) continue;
                        string cat = (f[iCat] ?? "").Trim();
                        string pat = (f[iPat] ?? "").Trim();
                        string suf = (f[iSuf] ?? "").Trim();
                        if (string.IsNullOrEmpty(pat) || string.IsNullOrEmpty(suf)) continue;
                        try
                        {
                            _rules.Add(new Rule
                            {
                                Category = string.IsNullOrEmpty(cat) ? "*" : cat,
                                MaterialPattern = new System.Text.RegularExpressions.Regex(pat,
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
                                Suffix = suf,
                            });
                        }
                        catch (Exception ex) { StingLog.Warn($"MaterialProdOverride bad regex '{pat}': {ex.Message}"); }
                    }
                    StingLog.Info($"MaterialProdOverrideRegistry: loaded {_rules.Count} rule(s) from {path}");
                }
                catch (Exception ex) { StingLog.Warn($"MaterialProdOverride load: {ex.Message}"); }
            }
        }
    }
}
