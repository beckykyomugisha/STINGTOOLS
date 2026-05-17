using StingTools.Core;
// StingTools — Symbol-standard resolver (Phase 175)
//
// Six-level inheritance chain:
//   1. Element override         (STING_SYMBOL_OVERRIDE on host)
//   2. View-level override      (STING_VIEW_SYMBOL_STANDARD on view)
//   3. Drawing-Type discipline  (DrawingType resolved by registry)
//   4. Mixed-profile discipline (project_config "symbol_standard_profile")
//   5. Project global           (project_config "symbol_standard")
//   6. Hardcoded "IEC"

using System;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Symbols
{
    public static class SymbolStandardResolver
    {
        private const string CFG_PROFILE = "symbol_standard_profile";
        private const string CFG_GLOBAL  = "symbol_standard";

        public static string ResolveStandard(Document doc, View view, Element host)
        {
            // Level 1 — element override.
            try
            {
                if (host != null)
                {
                    var p = host.LookupParameter("STING_SYMBOL_OVERRIDE");
                    var s = p?.AsString();
                    if (!string.IsNullOrWhiteSpace(s)
                        && SymbolStandardRegistry.GetStandard(s) != null)
                        return s;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveStandard L1: {ex.Message}"); }

            // Level 2 — view override.
            try
            {
                if (view != null)
                {
                    var p = view.LookupParameter("STING_VIEW_SYMBOL_STANDARD");
                    var s = p?.AsString();
                    if (!string.IsNullOrWhiteSpace(s)
                        && SymbolStandardRegistry.GetStandard(s) != null)
                        return s;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveStandard L2: {ex.Message}"); }

            // Level 3 — DrawingType discipline → mixed profile.
            try
            {
                if (view != null)
                {
                    var p = view.LookupParameter("STING_DRAWING_TYPE_ID_TXT");
                    string dtId = p?.AsString();
                    if (!string.IsNullOrEmpty(dtId))
                    {
                        var dt = StingTools.Core.Drawing.DrawingTypeRegistry.Get(doc, dtId);
                        if (dt != null && !string.IsNullOrEmpty(dt.Discipline))
                            return ResolveStandardForDiscipline(doc, dt.Discipline);
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveStandard L3: {ex.Message}"); }

            // Level 4 — discipline guess from host element.
            try
            {
                if (host != null)
                {
                    string disc = GuessDisciplineFromCategory(host.Category?.Name);
                    if (!string.IsNullOrEmpty(disc))
                        return ResolveStandardForDiscipline(doc, disc);
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveStandard L4: {ex.Message}"); }

            // Level 5 — project global.
            try
            {
                string global = ReadConfig(doc, CFG_GLOBAL);
                if (!string.IsNullOrWhiteSpace(global)
                    && SymbolStandardRegistry.GetStandard(global) != null)
                    return global;
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveStandard L5: {ex.Message}"); }

            return "IEC";
        }

        public static string ResolveStandardForDiscipline(Document doc, string discipline)
        {
            try
            {
                string profileId = GetActiveProfile(doc);
                string std = SymbolStandardRegistry.GetProfileForDiscipline(profileId, discipline);
                if (!string.IsNullOrEmpty(std)) return std;
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"ResolveForDiscipline: {ex.Message}"); }

            string global = ReadConfig(doc, CFG_GLOBAL);
            if (!string.IsNullOrWhiteSpace(global)) return global;
            return "IEC";
        }

        public static string GetActiveProfile(Document doc)
        {
            string s = ReadConfig(doc, CFG_PROFILE);
            if (!string.IsNullOrWhiteSpace(s)) return s;
            return SymbolStandardRegistry.GetDefaultProfile()?.Id ?? "Uganda-Standard";
        }

        public static void SetProjectStandard(Document doc, string standardId)
        {
            WriteConfig(doc, CFG_GLOBAL, standardId);
        }

        public static void SetProjectProfile(Document doc, string profileId)
        {
            WriteConfig(doc, CFG_PROFILE, profileId);
        }

        public static void SetViewStandard(Document doc, View view, string standardId)
        {
            if (doc == null || view == null) return;
            using (var tx = new Transaction(doc, "STING Set View Symbol Standard"))
using StingTools.Core.Drawing;
            {
                tx.Start();
                var p = view.LookupParameter("STING_VIEW_SYMBOL_STANDARD");
                if (p != null && !p.IsReadOnly) p.Set(standardId ?? "");
                tx.Commit();
            }
        }

        // ── project_config.json helpers ─────────────────────────────────

        private static string ConfigPath(Document doc)
        {
            try
            {
                if (doc == null || string.IsNullOrEmpty(doc.PathName)) return null;
                return Path.Combine(Path.GetDirectoryName(doc.PathName), "project_config.json");
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return null; }
        }

        private static string ReadConfig(Document doc, string key)
        {
            try
            {
                var p = ConfigPath(doc);
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
                var root = JObject.Parse(File.ReadAllText(p));
                return root[key]?.ToString();
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"ReadConfig {key}: {ex.Message}");
                return null;
            }
        }

        private static void WriteConfig(Document doc, string key, string value)
        {
            try
            {
                var p = ConfigPath(doc);
                if (string.IsNullOrEmpty(p)) return;
                JObject root = File.Exists(p)
                    ? JObject.Parse(File.ReadAllText(p))
                    : new JObject();
                root[key] = value ?? "";
                File.WriteAllText(p, root.ToString());
            }
            catch (Exception ex)
            {
                StingTools.Core.StingLog.Warn($"WriteConfig {key}: {ex.Message}");
            }
        }

        private static string GuessDisciplineFromCategory(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName)) return null;
            string lc = categoryName.ToLowerInvariant();
            if (lc.Contains("electrical") || lc.Contains("lighting"))     return "Electrical";
            if (lc.Contains("fire alarm") || lc.Contains("fire protect")) return "FireProtection";
            if (lc.Contains("sprinkler"))                                 return "FireProtection";
            if (lc.Contains("mechanical") || lc.Contains("duct") || lc.Contains("hvac")) return "HVAC";
            if (lc.Contains("plumbing")   || lc.Contains("pipe"))          return "Plumbing";
            if (lc.Contains("structural") || lc.Contains("structure"))    return "Structural";
            return null;
        }
    }
}
