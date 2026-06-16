using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using StingTools.Core;
using Newtonsoft.Json.Linq;

namespace StingTools.Tags
{
    /// <summary>
    /// Wave-1 commit 3 helper: glues <see cref="HandoverModeHelper"/> to
    /// <see cref="TagConfigCsvReader"/> so <see cref="CreateTagFamiliesCommand"/>
    /// can ask "for the active mode on this document, give me a family-name
    /// → TierPlan map across ARCH / GEN / MEP / STR".
    ///
    /// The resolver is the only place that needs to know about mode
    /// resolution + CSV location + CSV parsing at the same time. Adding a
    /// new discipline or a new mode variant is a one-line change to either
    /// HandoverModeHelper or TagConfigCsvReader — the command stays unchanged.
    /// </summary>
    internal static class TagConfigPlanResolver
    {
        /// <summary>
        /// Load every discipline's tag-config CSV for the document's active
        /// mode and merge the results. Later files shadow earlier ones when
        /// a family is listed twice (the discipline CSVs should not
        /// overlap in practice).
        /// </summary>
        /// <param name="failedCsvs">
        /// When non-null, receives the names of any CSVs that could not be
        /// located on disk — callers can surface these in a diagnostic banner.
        /// </param>
        public static Dictionary<string, TierPlan> LoadAll(Document doc,
            List<string> failedCsvs = null)
        {
            var merged = new Dictionary<string, TierPlan>(StringComparer.Ordinal);
            string[] csvNames;
            try { csvNames = HandoverModeHelper.GetAllTagConfigCsvs(doc); }
            catch (Exception ex)
            {
                StingLog.Warn($"TagConfigPlanResolver.LoadAll: GetAllTagConfigCsvs failed — {ex.Message}");
                return merged;
            }

            foreach (string name in csvNames)
            {
                if (string.IsNullOrEmpty(name)) continue;
                string path = ResolveCsvPath(name);
                if (string.IsNullOrEmpty(path))
                {
                    StingLog.Warn($"TagConfigPlanResolver: CSV '{name}' not resolved on disk — skipping.");
                    failedCsvs?.Add(name);
                    continue;
                }
                try
                {
                    var perFile = TagConfigCsvReader.LoadFile(path);
                    foreach (var kv in perFile) merged[kv.Key] = kv.Value;
                    StingLog.Info($"TagConfigPlanResolver: loaded {perFile.Count} families from '{name}'.");
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"TagConfigPlanResolver: parsing '{name}' failed — {ex2.Message}");
                    failedCsvs?.Add(name);
                }
            }
            return merged;
        }

        /// <summary>
        /// Load every built-in mode's tag-config CSVs and return a per-mode
        /// family → <see cref="TierPlan"/> map. Used by the dual-wire authoring
        /// path so a single family can be stamped with both Handover and
        /// Design & Construction T4-T10 row sets, each row gated by its
        /// mode selector BOOL (see <see cref="HandoverModeHelper.ModeSelectorBool"/>).
        /// </summary>
        /// <returns>
        /// Outer dict keyed by mode name ("Handover", "DesignConstruction", …);
        /// inner dict is the same shape <see cref="LoadAll"/> returns for a
        /// single mode. Modes whose CSVs are missing from disk are omitted.
        /// </returns>
        public static Dictionary<string, Dictionary<string, TierPlan>> LoadAllPerMode(Document doc)
        {
            var result = new Dictionary<string, Dictionary<string, TierPlan>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string[]> perModeCsvs;
            try { perModeCsvs = HandoverModeHelper.GetAllTagConfigCsvsForAllModes(doc); }
            catch (Exception ex)
            {
                StingLog.Warn($"TagConfigPlanResolver.LoadAllPerMode: {ex.Message}");
                return result;
            }

            foreach (var modeKv in perModeCsvs)
            {
                var merged = new Dictionary<string, TierPlan>(StringComparer.Ordinal);
                foreach (string name in modeKv.Value)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    string path = ResolveCsvPath(name);
                    if (string.IsNullOrEmpty(path)) continue;
                    try
                    {
                        var perFile = TagConfigCsvReader.LoadFile(path);
                        foreach (var kv in perFile) merged[kv.Key] = kv.Value;
                    }
                    catch (Exception ex2)
                    {
                        StingLog.Warn($"TagConfigPlanResolver.LoadAllPerMode: parsing '{name}' failed — {ex2.Message}");
                    }
                }
                if (merged.Count > 0) result[modeKv.Key] = merged;
            }
            return result;
        }

        /// <summary>
        /// Read the PRESERVE_HAND_EDITS flag from project_config.json next to
        /// the .rvt. Default false so a fresh project re-authors normally.
        /// When the key is absent or the file is missing we return false
        /// (same fall-through shape as <see cref="HandoverModeHelper.ReadProjectMode"/>).
        /// </summary>
        public static bool ReadPreserveHandEdits(Document doc)
        {
            try
            {
                if (doc == null) return false;
                string dir = Path.GetDirectoryName(doc.PathName ?? "") ?? "";
                if (string.IsNullOrEmpty(dir)) return false;
                string cfg = Path.Combine(dir, "project_config.json");
                if (!File.Exists(cfg)) return false;
                var jo = JObject.Parse(File.ReadAllText(cfg));
                var tok = jo["PRESERVE_HAND_EDITS"];
                if (tok == null) return false;
                if (tok.Type == JTokenType.Boolean) return (bool)tok;
                return string.Equals(tok.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagConfigPlanResolver.ReadPreserveHandEdits: {ex.Message}");
                return false;
            }
        }

        // ── private helpers ────────────────────────────────────────────────

        /// <summary>
        /// Locate a tag-config CSV file by filename. Tries the standard
        /// <see cref="StingToolsApp.FindDataFile"/> chain first, then a set of
        /// deployment-layout–specific fallback paths so that the file can be
        /// found whether the plugin is running from <c>CompiledPlugin/</c>,
        /// a Revit add-ins folder, or directly from the source tree.
        /// Returns null when the file cannot be found anywhere.
        /// </summary>
        private static string ResolveCsvPath(string csvFileName)
        {
            // 1. Standard search (DataPath + recursive + known alternates).
            string found = StingToolsApp.FindDataFile(csvFileName);
            if (!string.IsNullOrEmpty(found) && File.Exists(found))
                return found;

            // 2. Extra fallback paths relative to the assembly directory.
            //    These cover layouts where the DLL lives in CompiledPlugin/
            //    but the CSV files are only in the source tree under
            //    StingTools/Data/, or in a sibling data/ folder that was not
            //    picked up by the primary search chain.
            string asmDir = "";
            try
            {
                asmDir = Path.GetDirectoryName(StingToolsApp.AssemblyPath ?? "") ?? "";
            }
            catch { /* AssemblyPath not yet set in a unit-test context */ }

            if (!string.IsNullOrEmpty(asmDir))
            {
                string[] extras = {
                    // CompiledPlugin/data/<file>  (case-variant the primary search may miss)
                    Path.Combine(asmDir, "data",  csvFileName),
                    Path.Combine(asmDir, "Data",  csvFileName),
                    // Sibling StingTools/Data/ — works when DLL is in CompiledPlugin/
                    Path.Combine(asmDir, "..", "StingTools", "Data", csvFileName),
                    Path.Combine(asmDir, "..", "StingTools", "data", csvFileName),
                    // Up two levels then into StingTools/Data/ (nested build outputs)
                    Path.Combine(asmDir, "..", "..", "StingTools", "Data", csvFileName),
                    // Plain sibling data/ next to parent folder
                    Path.Combine(asmDir, "..", "data", csvFileName),
                    Path.Combine(asmDir, "..", "Data", csvFileName),
                };

                foreach (string candidate in extras)
                {
                    try
                    {
                        string full = Path.GetFullPath(candidate);
                        if (File.Exists(full))
                        {
                            StingLog.Info($"TagConfigPlanResolver: resolved '{csvFileName}' via fallback → {full}");
                            return full;
                        }
                    }
                    catch { /* Path.GetFullPath can throw on malformed paths */ }
                }
            }

            return null;
        }
    }
}
