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
        public static Dictionary<string, TierPlan> LoadAll(Document doc)
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
                string path = StingToolsApp.FindDataFile(name);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn($"TagConfigPlanResolver: CSV '{name}' not resolved on disk — skipping.");
                    continue;
                }
                try
                {
                    var perFile = TagConfigCsvReader.LoadFile(path);
                    foreach (var kv in perFile) merged[kv.Key] = kv.Value;
                }
                catch (Exception ex2)
                {
                    StingLog.Warn($"TagConfigPlanResolver: parsing '{name}' failed — {ex2.Message}");
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
                    string path = StingToolsApp.FindDataFile(name);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
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
                var jo = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(cfg));
                var tok = jo["PRESERVE_HAND_EDITS"];
                if (tok == null) return false;
                if (tok.Type == Newtonsoft.Json.Linq.JTokenType.Boolean) return (bool)tok;
                return string.Equals(tok.ToString(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagConfigPlanResolver.ReadPreserveHandEdits: {ex.Message}");
                return false;
            }
        }
    }
}
