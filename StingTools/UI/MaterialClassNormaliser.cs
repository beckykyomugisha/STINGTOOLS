using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// A-1 — Free-text MaterialClass → canonical class normaliser.
    /// Drives consistent pivot keys / colour swatches / BOQ groups so
    /// "Concrete C40" / "Concrete - C40" / "Reinforced Concrete" all
    /// resolve to "Concrete".
    /// </summary>
    public class ClassNormalisationRule { public Regex Pattern; public string Canonical; }

    public static class MaterialClassNormaliser
    {
        private static List<ClassNormalisationRule> _rules;
        private static readonly object _lock = new object();

        public static void Reload() { lock (_lock) { _rules = null; } }

        public static string Resolve(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            EnsureLoaded();
            lock (_lock)
            {
                if (_rules == null) return raw;
                foreach (var r in _rules)
                    try { if (r.Pattern.IsMatch(raw)) return r.Canonical; }
                    catch (Exception ex) { StingLog.WarnRateLimited("ClassNorm", $"Match: {ex.Message}"); }
            }
            return raw;
        }

        /// <summary>One-shot bulk pass — rewrites Material.MaterialClass on
        /// every project material that has a canonical mapping. Returns
        /// the count of materials updated. Caller owns the transaction.
        /// </summary>
        public static int NormaliseProject(Document doc)
        {
            if (doc == null) return 0;
            int touched = 0;
            using (var t = new Transaction(doc, "STING Normalise Material Classes"))
            {
                t.Start();
                foreach (var m in new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>())
                {
                    try
                    {
                        string cur = m.MaterialClass ?? "";
                        string canonical = Resolve(cur);
                        if (!string.Equals(cur, canonical, StringComparison.Ordinal))
                        {
                            m.MaterialClass = canonical;
                            touched++;
                            MaterialAuditLogger.Log(doc, "MAT_ClassNormalise", m.Name,
                                new Dictionary<string, object> { ["old"] = cur, ["new"] = canonical });
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"NormaliseProject '{m?.Name}': {ex.Message}"); }
                }
                t.Commit();
            }
            return touched;
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_rules != null) return;
                _rules = new List<ClassNormalisationRule>();
                try
                {
                    string path = StingToolsApp.FindDataFile("STING_MATERIAL_CLASS_NORMALISER.csv");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                    foreach (var line in File.ReadAllLines(path).Skip(1))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var f = StingToolsApp.ParseCsvLine(line);
                        if (f == null || f.Length < 2) continue;
                        string pat = (f[0] ?? "").Trim();
                        string can = (f[1] ?? "").Trim();
                        if (string.IsNullOrEmpty(pat) || string.IsNullOrEmpty(can)) continue;
                        try { _rules.Add(new ClassNormalisationRule { Pattern = new Regex(pat, RegexOptions.Compiled), Canonical = can }); }
                        catch (Exception ex) { StingLog.Warn($"ClassNorm bad regex '{pat}': {ex.Message}"); }
                    }
                    StingLog.Info($"MaterialClassNormaliser: {_rules.Count} rule(s) loaded.");
                }
                catch (Exception ex) { StingLog.Warn($"ClassNorm load: {ex.Message}"); }
            }
        }
    }
}
