// ══════════════════════════════════════════════════════════════════════════
//  Icms3PhaseMap.cs — Multi-language phase-name → ICMS3 group code map.
//
//  Loaded once from Data/STING_ICMS3_PHASE_MAP.json + project override
//  at <project>/_BIM_COORD/icms3_phase_map.json. Cached per Document.
//
//  Matching is case-insensitive substring on the phase name across all
//  configured languages. Group order in JSON drives priority — 04 is
//  evaluated first so an explicit demolition phase wins over a generic
//  "operation" keyword.
//
//  Phase 184l of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.Core;

namespace StingTools.BOQ.MeasurementStandard
{
    /// <summary>
    /// Resolved ICMS3 group entry — one of "01" / "02" / "03" / "04"
    /// with its trigger semantics + keyword set.
    /// </summary>
    public class Icms3PhaseGroup
    {
        public string Code { get; set; } = "02";
        public string Label { get; set; } = "Construction";

        /// <summary>"created" / "demolished" / "created-or-demolished".</summary>
        public string Trigger { get; set; } = "created";

        /// <summary>Flattened keyword list across all languages.</summary>
        public List<string> KeywordsFlat { get; set; } = new List<string>();

        public bool Matches(string phaseName)
        {
            if (string.IsNullOrEmpty(phaseName) || KeywordsFlat == null) return false;
            string lower = phaseName.ToLowerInvariant();
            foreach (var kw in KeywordsFlat)
                if (!string.IsNullOrEmpty(kw) && lower.Contains(kw.ToLowerInvariant()))
                    return true;
            return false;
        }
    }

    public sealed class Icms3PhaseMap
    {
        private static readonly ConcurrentDictionary<string, Icms3PhaseMap> _cache
            = new ConcurrentDictionary<string, Icms3PhaseMap>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<Icms3PhaseGroup> Groups { get; }
        public string DefaultCode { get; }
        public string DefaultLabel { get; }

        private Icms3PhaseMap(IReadOnlyList<Icms3PhaseGroup> groups, string defaultCode, string defaultLabel)
        {
            Groups = groups;
            DefaultCode = defaultCode;
            DefaultLabel = defaultLabel;
        }

        public static Icms3PhaseMap Get(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Invalidate() => _cache.Clear();

        /// <summary>
        /// Classify a phase-name pair into the ICMS3 group code. Returns
        /// the configured default (typically "02 Construction") when no
        /// group matches.
        /// </summary>
        public string Classify(string createdPhaseName, string demolishedPhaseName, bool isDemolished)
        {
            // Evaluate in JSON order. 04 is first so explicit demolition
            // phases win over a generic operation keyword.
            foreach (var g in Groups)
            {
                bool considerCreated  = g.Trigger == "created"
                                     || g.Trigger == "created-or-demolished";
                bool considerDemo     = g.Trigger == "demolished"
                                     || g.Trigger == "created-or-demolished";

                if (considerDemo && isDemolished && g.Matches(demolishedPhaseName))
                    return g.Code;

                if (considerCreated && !string.IsNullOrEmpty(createdPhaseName) && g.Matches(createdPhaseName))
                    return g.Code;
            }

            // Operation special case: PHASE_DEMOLISHED set in a
            // non-demolition phase means the element was replaced during
            // an operational cycle. 03 should win over the default 02.
            if (isDemolished)
            {
                var op = Groups.FirstOrDefault(g => g.Code == "03");
                if (op != null) return op.Code;
            }

            return DefaultCode;
        }

        // ── Loader ────────────────────────────────────────────────────

        private static Icms3PhaseMap Load(Document doc)
        {
            string corpPath = StingToolsApp.FindDataFile("STING_ICMS3_PHASE_MAP.json");
            string projectPath = ResolveProjectOverridePath(doc);

            var corp = LoadFile(corpPath);
            var project = LoadFile(projectPath);

            // Project entries replace corporate entries with the same code;
            // other project groups are appended in their declared order.
            var merged = new List<Icms3PhaseGroup>();
            var projectCodes = new HashSet<string>(
                project?.Groups?.Select(g => g.Code) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (project?.Groups != null)
                merged.AddRange(project.Groups);
            if (corp?.Groups != null)
                merged.AddRange(corp.Groups.Where(g => !projectCodes.Contains(g.Code)));

            string defaultCode = project?.DefaultCode ?? corp?.DefaultCode ?? "02";
            string defaultLabel = project?.DefaultLabel ?? corp?.DefaultLabel ?? "Construction";
            return new Icms3PhaseMap(merged, defaultCode, defaultLabel);
        }

        private static Icms3PhaseMap LoadFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var json = JObject.Parse(File.ReadAllText(path));
                var groups = new List<Icms3PhaseGroup>();
                if (json["groups"] is JArray arr)
                {
                    foreach (var gNode in arr)
                    {
                        var g = new Icms3PhaseGroup
                        {
                            Code = gNode["code"]?.Value<string>() ?? "02",
                            Label = gNode["label"]?.Value<string>() ?? "",
                            Trigger = gNode["trigger"]?.Value<string>() ?? "created"
                        };
                        if (gNode["keywords"] is JObject kw)
                        {
                            foreach (var prop in kw.Properties())
                            {
                                if (prop.Value is JArray langArr)
                                    foreach (var kwNode in langArr)
                                        g.KeywordsFlat.Add(kwNode?.Value<string>() ?? "");
                            }
                        }
                        groups.Add(g);
                    }
                }
                string defaultCode = json["default_code"]?.Value<string>() ?? "02";
                string defaultLabel = json["default_label"]?.Value<string>() ?? "Construction";
                return new Icms3PhaseMap(groups, defaultCode, defaultLabel);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Icms3PhaseMap.LoadFile({Path.GetFileName(path)}): {ex.Message}");
                return null;
            }
        }

        private static string ResolveProjectOverridePath(Document doc)
        {
            try
            {
                string bimDir = BIMManagerEngine.GetBIMManagerDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return null;
                string parent = Path.GetDirectoryName(bimDir);
                if (string.IsNullOrEmpty(parent)) return null;
                return Path.Combine(parent, "_BIM_COORD", "icms3_phase_map.json");
            }
            catch { return null; }
        }
    }
}
