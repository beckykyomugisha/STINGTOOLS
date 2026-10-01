// StingTools — Uniclass 2015 category map registry (A-2, code half).
//
// Was a 20-entry Dictionary<BuiltInCategory,(string,string)> literal inside
// Temp/StandardsEngine.ClassifyUniclass. Layered the same way every other
// STING rule set is:
//
//   corporate baseline -> Data/STING_UNICLASS_MAP.csv
//   project override   -> <project>/_BIM_COORD/uniclass_map.csv
//
// so a project can extend or correct the map without a rebuild.
//
// The registry also owns PREFIX ROUTING. A Uniclass code carries its table in
// its prefix, and the three tables land in three different parameters that
// ClassificationReader reads independently:
//
//     Pr_ -> UNICLASS_PR_TXT    Ss_ -> UNICLASS_SS_TXT    EF_ -> UNICLASS_EF_TXT
//
// Without routing, the three Pr_ rows in the shipped map (Doors, Windows,
// Furniture) would be written into the systems parameter, where the reader's
// Uniclass.Pr tier would never see them and the Uniclass.Ss tier would report
// a product code as a system.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Core.Classification
{
    /// <summary>Which Uniclass table a code belongs to, and therefore which
    /// parameter it is written to.</summary>
    public enum UniclassTable
    {
        /// <summary>Prefix did not match a table STING writes. Never written.</summary>
        Unknown = 0,
        Product,   // Pr_ -> UNICLASS_PR_TXT
        System,    // Ss_ -> UNICLASS_SS_TXT
        Element    // EF_ -> UNICLASS_EF_TXT
    }

    /// <summary>One row of the map.</summary>
    public class UniclassMapEntry
    {
        public BuiltInCategory Category { get; set; }
        public string Code { get; set; } = "";
        public string Description { get; set; } = "";
        public string Source { get; set; } = "";

        /// <summary>Table implied by the code's prefix.</summary>
        public UniclassTable Table => UniclassMapRegistry.TableOf(Code);

        /// <summary>Parameter this row writes to, or null when the prefix is
        /// unrecognised.</summary>
        public string TargetParameter => UniclassMapRegistry.ParameterFor(Table);
    }

    public static class UniclassMapRegistry
    {
        public const string DataFileName = "STING_UNICLASS_MAP.csv";
        public const string ProjectOverrideRelPath = "_BIM_COORD/uniclass_map.csv";

        public const string ParamProduct = "UNICLASS_PR_TXT";
        public const string ParamSystem  = "UNICLASS_SS_TXT";
        public const string ParamElement = "UNICLASS_EF_TXT";

        // Per-document cache keyed by project path, matching MepSizingRegistry.
        private static readonly ConcurrentDictionary<string, Dictionary<BuiltInCategory, UniclassMapEntry>> _cache
            = new ConcurrentDictionary<string, Dictionary<BuiltInCategory, UniclassMapEntry>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Merged baseline + project override map for a document.</summary>
        public static Dictionary<BuiltInCategory, UniclassMapEntry> Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        /// <summary>Drop every cached map so the next Get re-reads from disk.</summary>
        public static void Reload() => _cache.Clear();

        /// <summary>Drop one document's cached map (e.g. after Save As).</summary>
        public static void Reload(Document doc)
            => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        /// <summary>Table a Uniclass code belongs to, from its prefix.</summary>
        public static UniclassTable TableOf(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return UniclassTable.Unknown;
            string c = code.Trim();
            if (c.StartsWith("Pr_", StringComparison.OrdinalIgnoreCase)) return UniclassTable.Product;
            if (c.StartsWith("Ss_", StringComparison.OrdinalIgnoreCase)) return UniclassTable.System;
            if (c.StartsWith("EF_", StringComparison.OrdinalIgnoreCase)) return UniclassTable.Element;
            return UniclassTable.Unknown;
        }

        /// <summary>Parameter a table writes to; null for <see cref="UniclassTable.Unknown"/>.</summary>
        public static string ParameterFor(UniclassTable table)
        {
            switch (table)
            {
                case UniclassTable.Product: return ParamProduct;
                case UniclassTable.System:  return ParamSystem;
                case UniclassTable.Element: return ParamElement;
                default:                    return null;
            }
        }

        private static Dictionary<BuiltInCategory, UniclassMapEntry> Load(Document doc)
        {
            var map = new Dictionary<BuiltInCategory, UniclassMapEntry>();
            try
            {
                string basePath = StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(File.ReadAllLines(basePath), map, basePath);
                else
                    StingLog.Warn($"UniclassMapRegistry: {DataFileName} not found; Uniclass classification has no map.");

                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projPath = ProjectFolderEngine.ResolveProjectOverridePath(doc, ProjectOverrideRelPath);
                    if (!string.IsNullOrEmpty(projPath) && File.Exists(projPath))
                        Apply(File.ReadAllLines(projPath), map, projPath);
                }
            }
            catch (Exception ex)
            {
                // Deliberately no hardcoded fallback map. A silent revert to the
                // old 20 entries would hide exactly the drift this file exists to
                // make visible; the command reports an empty map instead.
                StingLog.Error("UniclassMapRegistry.Load failed", ex);
            }
            return map;
        }

        private static void Apply(string[] lines, Dictionary<BuiltInCategory, UniclassMapEntry> map, string origin)
        {
            if (lines == null) return;
            // DSCH-2: columns by header name, not position.
            var t = CsvTable.Parse(lines, StingToolsApp.ParseCsvLine);
            if (t.HeaderLine == 0) return;   // empty / comment-only file
            var missing = t.Missing("BuiltInCategory", "UniclassCode");
            if (missing.Count > 0)
            {
                StingLog.Warn($"UniclassMapRegistry: {origin} header has no {string.Join(", ", missing)} column; file skipped.");
                return;
            }
            foreach (var r in t.Rows)
            {
                string catName = r["BuiltInCategory"];
                if (catName.Length == 0) continue;

                if (!Enum.TryParse(catName, ignoreCase: false, result: out BuiltInCategory bic))
                {
                    StingLog.Warn($"UniclassMapRegistry: '{catName}' is not a BuiltInCategory name ({origin}); row skipped.");
                    continue;
                }

                string code = r["UniclassCode"];
                if (code.Length == 0) continue;

                var entry = new UniclassMapEntry
                {
                    Category    = bic,
                    Code        = code,
                    Description = r["Description"],
                    Source      = r["Source"]
                };

                if (entry.Table == UniclassTable.Unknown)
                    StingLog.Warn($"UniclassMapRegistry: '{code}' on {catName} has no Pr_/Ss_/EF_ prefix ({origin}); " +
                                  "it will be reported as unrouted and written nowhere.");

                map[bic] = entry;   // project override replaces baseline by category
            }
        }
    }
}
