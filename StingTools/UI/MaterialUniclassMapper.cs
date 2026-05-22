using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// A3 — Free-text MaterialClass → Uniclass 2015 Pr_ code mapping.
    ///
    /// Rules ship in <c>Data/STING_MATERIAL_UNICLASS.csv</c>; projects
    /// override via <c>_BIM_COORD/material_uniclass.csv</c>. Regex-keyed
    /// first-match-wins.
    ///
    /// Consumers (BOQ paragraph picker, COBie writer, IFC exporter) call
    /// <see cref="ResolveCode(string)"/> to translate a MaterialClass
    /// string into a stable Uniclass code.
    /// </summary>
    public class UniclassMapping
    {
        public Regex Pattern { get; set; }
        public string Code { get; set; }
        public string Title { get; set; }
    }

    public static class MaterialUniclassMapper
    {
        private static List<UniclassMapping> _rules;
        private static readonly object _lock = new object();
        private const string CsvFile = "STING_MATERIAL_UNICLASS.csv";
        private const string ProjectFile = "material_uniclass.csv";

        public static void Reload()
        {
            lock (_lock) { _rules = null; }
        }

        public static string ResolveCode(string materialClass) => Resolve(materialClass)?.Code;
        public static string ResolveTitle(string materialClass) => Resolve(materialClass)?.Title;

        public static UniclassMapping Resolve(string materialClass)
        {
            if (string.IsNullOrWhiteSpace(materialClass)) return null;
            EnsureLoaded();
            lock (_lock)
            {
                if (_rules == null) return null;
                foreach (var r in _rules)
                {
                    try { if (r.Pattern.IsMatch(materialClass)) return r; }
                    catch (Exception ex) { StingLog.WarnRateLimited("UniclassMatch", $"Uniclass match: {ex.Message}"); }
                }
            }
            return null;
        }

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_rules != null) return;
                _rules = new List<UniclassMapping>();
                try
                {
                    // Corporate baseline first.
                    string corp = StingToolsApp.FindDataFile(CsvFile);
                    LoadCsvInto(_rules, corp);
                    // Project override appends (later rules win on ambiguous
                    // matches because we still walk first-match-wins, but
                    // projects can put their specific rules at the top of
                    // the file).
                    var doc = StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                    if (doc != null)
                    {
                        string proj = Path.Combine(
                            Core.ProjectFolderEngine.GetDataPath(doc, "") ?? "", ProjectFile);
                        if (File.Exists(proj)) LoadCsvInto(_rules, proj);
                    }
                    StingLog.Info($"MaterialUniclassMapper: loaded {_rules.Count} rule(s).");
                }
                catch (Exception ex) { StingLog.Warn($"MaterialUniclassMapper load: {ex.Message}"); }
            }
        }

        private static void LoadCsvInto(List<UniclassMapping> target, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return;
                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var f = StingToolsApp.ParseCsvLine(line);
                    if (f == null || f.Length < 3) continue;
                    string pat = (f[0] ?? "").Trim();
                    string code = (f[1] ?? "").Trim();
                    string title = (f[2] ?? "").Trim();
                    if (string.IsNullOrEmpty(pat) || string.IsNullOrEmpty(code)) continue;
                    try
                    {
                        target.Add(new UniclassMapping
                        {
                            Pattern = new Regex(pat, RegexOptions.Compiled),
                            Code = code,
                            Title = title,
                        });
                    }
                    catch (Exception ex) { StingLog.Warn($"Uniclass bad pattern '{pat}': {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"LoadCsvInto '{path}': {ex.Message}"); }
        }
    }
}
