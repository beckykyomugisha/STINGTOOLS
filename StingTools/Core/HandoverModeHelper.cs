using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace StingTools.Core
{
    /// <summary>
    /// Resolves the active HANDOVER_MODE per project and maps a discipline
    /// tag (ARCH / GEN / MEP / STR) to the mode-appropriate tag-config CSV.
    ///
    /// The mode comes from project_config.json next to the .rvt, falling
    /// back to PARAGRAPH_PRESETS.json active_preset, then "Handover".
    ///
    /// The CSVs are primarily reference data for external tag-family authoring
    /// scripts; at runtime they feed the schema-version check and the
    /// category-warning loader. Using this helper keeps those two consumers
    /// in sync with the Paragraph Builder / mode toggle so a Design &amp;
    /// Construction project picks up DC-specific warnings out of the box.
    /// </summary>
    public static class HandoverModeHelper
    {
        public const string DefaultMode = "Handover";

        /// <summary>Handover (built-in universal CSV) + DC sibling. Custom is user-edited,
        /// gated by <c>HANDOVER_MODE_CUSTOM_BOOL</c>; its label rows only live inside
        /// dual-wired families if a Custom CSV variant ships on disk.</summary>
        public static readonly string[] BuiltInModes = { "Handover", "DesignConstruction", "Custom" };

        /// <summary>
        /// Maps a built-in mode to the project-level YESNO selector BOOL that
        /// gates its T4-T10 label rows inside dual-wired tag families. Exactly
        /// one is true at a time; a mode without an entry here is treated as
        /// "no gate" (single-mode fallback).
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> ModeSelectorBool =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Handover",           "HANDOVER_MODE_HANDOVER_BOOL" },
                { "DesignConstruction", "HANDOVER_MODE_DC_BOOL" },
                { "Custom",             "HANDOVER_MODE_CUSTOM_BOOL" },
            };

        private static readonly string[] Disciplines = { "ARCH", "GEN", "MEP", "STR" };

        /// <summary>
        /// Read HANDOVER_MODE from project_config.json next to the document.
        /// Returns null if the document is unsaved, the file is missing, or
        /// the key is absent — callers fall back to PARAGRAPH_PRESETS.json.
        /// </summary>
        public static string ReadProjectMode(Document doc)
        {
            if (doc == null) return null;
            try
            {
                string cfgPath = ProjectConfigPath(doc);
                if (string.IsNullOrEmpty(cfgPath) || !File.Exists(cfgPath)) return null;
                var jo = JObject.Parse(File.ReadAllText(cfgPath));
                string m = (string)jo["HANDOVER_MODE"];
                return string.IsNullOrEmpty(m) ? null : m.Trim();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HandoverModeHelper.ReadProjectMode: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Full resolution chain: project_config.json → PARAGRAPH_PRESETS.json
        /// active_preset → "Handover". Always returns a non-null mode name.
        /// </summary>
        public static string GetActiveMode(Document doc)
        {
            string m = ReadProjectMode(doc);
            if (!string.IsNullOrEmpty(m)) return m;

            try
            {
                string path = StingToolsApp.FindDataFile("PARAGRAPH_PRESETS.json");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var jo = JObject.Parse(File.ReadAllText(path));
                    string k = (string)jo["active_preset"];
                    if (!string.IsNullOrEmpty(k)) return k.Trim();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"HandoverModeHelper.GetActiveMode: {ex.Message}");
            }
            return DefaultMode;
        }

        /// <summary>
        /// Map a discipline tag (ARCH / GEN / MEP / STR) to the CSV filename
        /// for the active mode. Falls back to the Handover default if the
        /// variant file is not present on disk (so projects without DC CSVs
        /// installed keep working).
        /// </summary>
        public static string GetTagConfigCsv(string discipline, Document doc)
        {
            return GetTagConfigCsv(discipline, GetActiveMode(doc));
        }

        public static string GetTagConfigCsv(string discipline, string mode)
        {
            if (string.IsNullOrEmpty(discipline)) discipline = "GEN";
            if (string.IsNullOrEmpty(mode)) mode = DefaultMode;

            string baseName = $"STING_TAG_CONFIG_v5_0_{discipline}";
            string defaultCsv = baseName + ".csv";  // built-in Handover

            if (string.Equals(mode, DefaultMode, StringComparison.OrdinalIgnoreCase))
                return defaultCsv;

            string variantCsv = $"{baseName}_{mode}.csv";
            string resolved = StingToolsApp.FindDataFile(variantCsv);
            if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                return variantCsv;

            // Variant not shipped — log once per mode, return Handover fallback
            StingLog.Warn($"HandoverModeHelper: variant '{variantCsv}' not on disk; " +
                          $"falling back to '{defaultCsv}'.");
            return defaultCsv;
        }

        /// <summary>Enumerate all CSV filenames for the active mode (4 disciplines).</summary>
        public static string[] GetAllTagConfigCsvs(Document doc)
        {
            string mode = GetActiveMode(doc);
            string[] outNames = new string[Disciplines.Length];
            for (int i = 0; i < Disciplines.Length; i++)
                outNames[i] = GetTagConfigCsv(Disciplines[i], mode);
            return outNames;
        }

        /// <summary>
        /// Enumerate CSV filenames for EVERY built-in mode whose variant actually
        /// ships on disk. Unlike <see cref="GetAllTagConfigCsvs"/>, this does NOT
        /// fall back to the Handover CSV when a variant is missing — it just
        /// omits that mode. Used by the dual-wire authoring path so families get
        /// stamped with both pattern row sets in one pass.
        /// </summary>
        /// <returns>Dictionary keyed by mode name (e.g. "Handover",
        /// "DesignConstruction"), each value the 4-discipline CSV name array.</returns>
        public static Dictionary<string, string[]> GetAllTagConfigCsvsForAllModes(Document doc)
        {
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string mode in BuiltInModes)
            {
                var names = new List<string>(Disciplines.Length);
                for (int i = 0; i < Disciplines.Length; i++)
                {
                    string csv = TryGetTagConfigCsvStrict(Disciplines[i], mode);
                    if (!string.IsNullOrEmpty(csv)) names.Add(csv);
                }
                if (names.Count > 0) result[mode] = names.ToArray();
            }
            return result;
        }

        /// <summary>Resolves <paramref name="mode"/> to its selector BOOL name, or null if unmapped.</summary>
        public static string GetSelectorBool(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return null;
            return ModeSelectorBool.TryGetValue(mode, out string b) ? b : null;
        }

        /// <summary>
        /// Strict variant of <see cref="GetTagConfigCsv(string,string)"/>:
        /// returns null if the variant CSV is missing instead of falling back
        /// to the default Handover CSV. The Handover mode itself still resolves
        /// to the non-suffixed base CSV.
        /// </summary>
        private static string TryGetTagConfigCsvStrict(string discipline, string mode)
        {
            string baseName = $"STING_TAG_CONFIG_v5_0_{discipline}";
            if (string.Equals(mode, DefaultMode, StringComparison.OrdinalIgnoreCase))
            {
                string defaultCsv = baseName + ".csv";
                string resolvedDefault = StingToolsApp.FindDataFile(defaultCsv);
                return string.IsNullOrEmpty(resolvedDefault) || !File.Exists(resolvedDefault)
                    ? null
                    : defaultCsv;
            }

            string variantCsv = $"{baseName}_{mode}.csv";
            string resolved = StingToolsApp.FindDataFile(variantCsv);
            return string.IsNullOrEmpty(resolved) || !File.Exists(resolved) ? null : variantCsv;
        }

        private static string ProjectConfigPath(Document doc)
        {
            try
            {
                string dir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "project_config.json");
            }
            catch { return null; }
        }
    }
}
