// ════════════════════════════════════════════════════════════════════════════
// McpCapabilityCatalogue — the single source the Tier 3 discovery tools read
//
// Turns NLPEngine.IntentPatterns (444 tag + trigger + description entries — the
// capability index that already ships) into per-command metadata records:
//   description / triggers — from IntentPatterns
//   category               — the pattern's Intent/module label
//   readOnly               — reflected from the command class [Transaction]
//                            attribute where the tag resolves, else null
//   opensUI                — curated dialog/wizard set + a name heuristic
//   engineBacked           — false for now (Phase 3 grows the engine-backed set)
//   inputContract          — "none / uses active selection+view" for now
//
// Built once and cached for the process. This is a pure in-memory catalogue —
// no Revit API calls — so it is safe to build off the API thread.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Mcp
{
    /// <summary>One capability record — the full metadata for a single command tag.</summary>
    internal class McpCapability
    {
        public string Tag { get; set; }
        public string Description { get; set; }
        public List<string> Triggers { get; set; } = new List<string>();
        public string Category { get; set; }
        public bool? ReadOnly { get; set; }          // null = could not resolve the command class
        public bool OpensUI { get; set; }
        public bool EngineBacked { get; set; }
        public string InputContract { get; set; } = "none / uses active selection+view";
    }

    internal static class McpCapabilityCatalogue
    {
        private static Dictionary<string, McpCapability> _byTag;
        private static readonly object _lock = new object();

        // Curated set of tags known to open a WPF dialog / wizard / dockable centre.
        // Grown over time — it is the driver for the dialog→engine work (exposure §7).
        private static readonly HashSet<string> _curatedOpensUi = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "ProjectSetupWizard", "WorkflowPresets", "SheetManager", "DocumentManagement",
            "BIMCoordinationCenter", "CircuitWizard", "SelectiveCoord", "COBieExport",
            "ExcelExchange", "IssueWizard", "SmartPlacementWizard", "BEPWizard",
            "DocAutomation", "ModelCreation", "ScheduleWizard", "NewSheet",
            "DrawingTypeEditor", "Placement_OpenCenter", "TemplateSetupWizard",
            "MasterSetup", "CompletenessDashboard", "StandardsDashboard",
            "PhotometricLibrary", "BOQCostManager", "Fabrication_OpenWorkspace",
        };

        // Name-suffix / keyword heuristic layered on top of the curated set.
        private static readonly string[] _opensUiHints =
            { "wizard", "dashboard", "center", "centre", "manager", "browser", "editor", "dialog" };

        /// <summary>Build (once) and return the catalogue keyed by command tag.</summary>
        public static Dictionary<string, McpCapability> GetAll()
        {
            if (_byTag != null) return _byTag;
            lock (_lock)
            {
                if (_byTag != null) return _byTag;
                _byTag = Build();
                return _byTag;
            }
        }

        /// <summary>Lookup one capability record; null when the tag is unknown.</summary>
        public static McpCapability Get(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            GetAll().TryGetValue(tag.Trim(), out McpCapability cap);
            return cap;
        }

        /// <summary>
        /// Rank capabilities against a free-text query (fuzzy over tag + description +
        /// triggers + category). Returns up to <paramref name="limit"/> best matches.
        /// </summary>
        public static List<McpCapability> Search(string query, int limit)
        {
            var all = GetAll().Values;
            if (string.IsNullOrWhiteSpace(query))
                return all.OrderBy(c => c.Tag).Take(limit).ToList();

            string q = query.Trim().ToLowerInvariant();
            string[] terms = q.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            return all
                .Select(c => new { Cap = c, Score = Score(c, q, terms) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Cap.Tag)
                .Take(limit)
                .Select(x => x.Cap)
                .ToList();
        }

        private static int Score(McpCapability c, string wholeQuery, string[] terms)
        {
            int score = 0;
            string tag  = c.Tag?.ToLowerInvariant() ?? "";
            string desc = c.Description?.ToLowerInvariant() ?? "";
            string cat  = c.Category?.ToLowerInvariant() ?? "";
            string trig = string.Join(" ", c.Triggers).ToLowerInvariant();

            if (tag == wholeQuery) score += 100;
            if (tag.Contains(wholeQuery)) score += 25;
            if (desc.Contains(wholeQuery)) score += 15;

            foreach (string t in terms)
            {
                if (tag.Contains(t))  score += 8;
                if (desc.Contains(t)) score += 5;
                if (trig.Contains(t)) score += 4;
                if (cat.Contains(t))  score += 3;
            }
            return score;
        }

        // ── Build ────────────────────────────────────────────────────────────────

        private static Dictionary<string, McpCapability> Build()
        {
            var map = new Dictionary<string, McpCapability>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in NLPEngine.IntentPatterns)
            {
                if (string.IsNullOrWhiteSpace(p.CommandTag)) continue;

                if (!map.TryGetValue(p.CommandTag, out McpCapability cap))
                {
                    cap = new McpCapability
                    {
                        Tag         = p.CommandTag,
                        Description = p.Description,
                        Category    = string.IsNullOrWhiteSpace(p.Intent) ? "General" : p.Intent,
                        OpensUI     = ResolveOpensUi(p.CommandTag, p.Description),
                        EngineBacked = false,
                    };
                    cap.ReadOnly = ResolveReadOnly(p.CommandTag);
                    map[p.CommandTag] = cap;
                }

                // Accumulate cleaned trigger phrases from every pattern for this tag.
                string cleaned = CleanPattern(p.Pattern);
                if (!string.IsNullOrWhiteSpace(cleaned) && !cap.Triggers.Contains(cleaned))
                    cap.Triggers.Add(cleaned);
            }

            StingLog.Info($"McpCapabilityCatalogue built: {map.Count} capabilities.");
            return map;
        }

        private static bool ResolveOpensUi(string tag, string description)
        {
            if (_curatedOpensUi.Contains(tag)) return true;
            string hay = (tag + " " + (description ?? "")).ToLowerInvariant();
            return _opensUiHints.Any(h => hay.Contains(h));
        }

        /// <summary>
        /// Reflect the command class [Transaction] attribute: ReadOnly → true,
        /// Manual/Automatic → false. Returns null when the tag does not resolve to a
        /// concrete command (the resolver covers a subset), so callers can treat
        /// "unknown" distinctly from "known write".
        /// </summary>
        private static bool? ResolveReadOnly(string tag)
        {
            try
            {
                object instance = null;
                try { instance = WorkflowEngine.GetCommandInstance(tag); }
                catch (Exception ex) { StingLog.Warn($"Catalogue resolve '{tag}': {ex.Message}"); }

                if (instance == null) return null;

                var attr = instance.GetType()
                    .GetCustomAttributes(typeof(Autodesk.Revit.Attributes.TransactionAttribute), false)
                    .OfType<Autodesk.Revit.Attributes.TransactionAttribute>()
                    .FirstOrDefault();
                if (attr == null) return null;

                return attr.Mode == Autodesk.Revit.Attributes.TransactionMode.ReadOnly;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Catalogue readOnly reflect '{tag}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Turn a regex trigger pattern into a readable keyword phrase.</summary>
        private static string CleanPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return "";
            string s = pattern;
            s = s.Replace(@"\b", " ").Replace(@"\s+", " ").Replace(@"\s*", " ")
                 .Replace(@"\s", " ").Replace(".?", " ").Replace(".*", " ");
            // Drop {0,5}-style quantifiers and remaining metacharacters.
            s = Regex.Replace(s, @"\{[^}]*\}", " ");
            s = Regex.Replace(s, @"[()\\?+*^$\[\]]", " ");
            s = s.Replace("|", " / ");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            return s;
        }
    }
}
