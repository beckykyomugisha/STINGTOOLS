// StingTools — Content Library (root resolution)
//
// ContentRoots resolves the ordered list of on-disk folders the ContentResolver
// searches for .rfa content. It generalises MepSymbolEngine.ResolveSharedLibraryRoot
// into a precedence chain over three tiers:
//   project   — <project>/_BIM_COORD/Content (+ legacy Families/Symbols, Families/Seeds)
//   shared    — firm-wide library via STING_CONTENT_LIB env or
//               %APPDATA%/STING/sting_content.json:"content_root"
//               (legacy STING_SYMBOL_LIB / sting_symbols.json honoured as fallback)
//   baseline  — the plugin's shipped/deployed Families + TagFamilies folders
//
// Precedence is driven by ContentManifest.RootPrecedence:
//   "projectFirst" (default) — a frozen project ignores firm-level changes
//   "sharedFirst"            — legacy symbol-engine order (shared wins)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StingTools.Core.Content
{
    public static class ContentRoots
    {
        public static IReadOnlyList<string> Resolve(Autodesk.Revit.DB.Document doc, string precedence = "projectFirst")
        {
            var project = new List<string>();
            try
            {
                var docDir = (doc != null && !string.IsNullOrEmpty(doc.PathName))
                    ? Path.GetDirectoryName(doc.PathName) : null;
                if (!string.IsNullOrEmpty(docDir))
                {
                    project.Add(StingPaths.Meta(doc, "_BIM_COORD", "Content"));
                    project.Add(StingPaths.Meta(doc, "_BIM_COORD", "Families", "Symbols"));
                    project.Add(StingPaths.Meta(doc, "_BIM_COORD", "Families", "Seeds"));
                    project.Add(StingPaths.Meta(doc, "_BIM_COORD", "Families"));
                }
            }
            catch { /* unsaved doc */ }

            var shared = new List<string>();
            var s = ResolveSharedRoot();
            if (!string.IsNullOrEmpty(s)) shared.Add(s);

            var baseline = new List<string>();
            try
            {
                var asmDir = string.IsNullOrEmpty(StingToolsApp.AssemblyPath)
                    ? null : Path.GetDirectoryName(StingToolsApp.AssemblyPath);
                if (!string.IsNullOrEmpty(asmDir)) baseline.Add(Path.Combine(asmDir, "Families"));
                var data = StingToolsApp.DataPath;
                if (!string.IsNullOrEmpty(data))
                {
                    baseline.Add(Path.Combine(data, "Families"));
                    baseline.Add(Path.Combine(data, "TagFamilies"));            // deployed flat set
                    baseline.Add(Path.Combine(data, "TagFamilies", "Seeds"));   // source subset
                }
            }
            catch { }

            var ordered = string.Equals(precedence, "sharedFirst", StringComparison.OrdinalIgnoreCase)
                ? shared.Concat(project).Concat(baseline)
                : project.Concat(shared).Concat(baseline);

            // Dedupe, preserve order.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var r in ordered)
                if (!string.IsNullOrEmpty(r) && seen.Add(r)) result.Add(r);
            return result;
        }

        /// <summary>Firm-wide content root: STING_CONTENT_LIB env →
        /// %APPDATA%/STING/sting_content.json:"content_root" → (legacy)
        /// STING_SYMBOL_LIB / sting_symbols.json:"symbol_library_root". Null when unset.</summary>
        public static string ResolveSharedRoot()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("STING_CONTENT_LIB");
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

                var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var cfg = Path.Combine(appdata, "STING", "sting_content.json");
                if (File.Exists(cfg))
                {
                    var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(cfg));
                    var lib = (string)root["content_root"];
                    if (!string.IsNullOrWhiteSpace(lib)) return lib.Trim();
                }

                // Legacy symbol-library config (back-compat).
                var legacyEnv = Environment.GetEnvironmentVariable("STING_SYMBOL_LIB");
                if (!string.IsNullOrWhiteSpace(legacyEnv)) return legacyEnv.Trim();
                var legacyCfg = Path.Combine(appdata, "STING", "sting_symbols.json");
                if (File.Exists(legacyCfg))
                {
                    var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(legacyCfg));
                    var lib = (string)root["symbol_library_root"];
                    if (!string.IsNullOrWhiteSpace(lib)) return lib.Trim();
                }
            }
            catch (Exception ex) { StingLog.Warn($"ContentRoots.ResolveSharedRoot: {ex.Message}"); }
            return null;
        }
    }
}
