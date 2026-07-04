// ════════════════════════════════════════════════════════════════════════════
// McpCapabilityCatalogue — the single source the Tier 3 discovery tools read
//
// Covers the ENTIRE command surface: every non-abstract IExternalCommand class in
// the assembly (~1,580), not just the ~444 NLP-tagged ones. The catalogue is the
// UNION of two sources, de-duplicated by command Type.FullName:
//
//   (1) TAGGED / dispatchable — NLPEngine.IntentPatterns (rich description + trigger
//       phrases) plus the engine-registry tags. These carry a real dispatch tag, so
//       invoke_capability can actually run them (subject to the engine-backed gate).
//   (2) REFLECTED / discover-only — every remaining IExternalCommand type found by
//       McpCommandScan. These are describable/searchable but have no dispatch path
//       exposed to MCP, so Dispatchable = false.
//
// A tagged command and its reflected type collapse to ONE record (the tagged one
// wins — it carries the real tag + NLP description + triggers). Per record:
//   description / triggers — from IntentPatterns (else synthesized from the class name)
//   category               — namespace leaf (Tags / Docs / Electrical / …); Intent kept where present
//   readOnly               — the [Transaction] attribute read OFF THE TYPE (no instantiation)
//   opensUI                — curated dialog/wizard set + a name heuristic
//   engineBacked           — McpEngineRegistry.IsEngineBacked(tag) (single source)
//   dispatchable           — true only when a real dispatch tag exists
//   inputContract          — "none / uses active selection+view" for now
//
// Built once and cached for the process. Reflecting the whole assembly + reading
// attributes is fast (<1–2 s) — the ~1,580 reflected types are NEVER instantiated.
// The only instantiation is a best-effort tag→Type resolution for the tagged subset
// (name-match first, WorkflowEngine fallback) purely to establish the dedup identity.
// ════════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Mcp
{
    /// <summary>One capability record — the full metadata for a single command.</summary>
    internal class McpCapability
    {
        public string Tag { get; set; }               // real dispatch tag when dispatchable; else the class short name
        public string TypeName { get; set; }          // class short name (may be null if a tag failed to resolve to a type)
        public string TypeFullName { get; set; }      // dedup key
        public string Description { get; set; }
        public bool Synthesized { get; set; }         // true when the description was synthesized (no NLP text)
        public List<string> Triggers { get; set; } = new List<string>();
        public string Category { get; set; }          // namespace leaf
        public string Intent { get; set; }            // NLP intent/module label (null for reflected-only)
        public bool? ReadOnly { get; set; }           // null = no [Transaction] attribute / type not resolved
        public bool OpensUI { get; set; }
        public bool EngineBacked { get; set; }
        public bool Dispatchable { get; set; }        // true → invoke_capability has a real dispatch/engine path
        public string InputContract { get; set; } = "none / uses active selection+view";
    }

    internal static class McpCapabilityCatalogue
    {
        private static Dictionary<string, McpCapability> _byTag;
        private static readonly object _lock = new object();

        // Reflected class short name → Type, for no-instantiation tag→Type name matching.
        private static Dictionary<string, Type> _typeByName;

        // Curated set of tags known to open a WPF dialog / wizard / dockable centre.
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
        /// triggers + category + type name). Returns up to <paramref name="limit"/> matches.
        /// </summary>
        public static List<McpCapability> Search(string query, int limit)
        {
            var all = GetAll().Values;
            if (string.IsNullOrWhiteSpace(query))
                return all.OrderByDescending(c => c.Dispatchable).ThenBy(c => c.Tag).Take(limit).ToList();

            string q = query.Trim().ToLowerInvariant();
            string[] terms = q.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            return all
                .Select(c => new { Cap = c, Score = Score(c, q, terms) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Cap.Dispatchable)   // prefer runnable on ties
                .ThenBy(x => x.Cap.Tag)
                .Take(limit)
                .Select(x => x.Cap)
                .ToList();
        }

        private static int Score(McpCapability c, string wholeQuery, string[] terms)
        {
            int score = 0;
            string tag  = c.Tag?.ToLowerInvariant() ?? "";
            string type = c.TypeName?.ToLowerInvariant() ?? "";
            string desc = c.Description?.ToLowerInvariant() ?? "";
            string cat  = c.Category?.ToLowerInvariant() ?? "";
            string trig = string.Join(" ", c.Triggers).ToLowerInvariant();

            if (tag == wholeQuery) score += 100;
            if (tag.Contains(wholeQuery)) score += 25;
            if (type.Contains(wholeQuery)) score += 20;
            if (desc.Contains(wholeQuery)) score += 15;

            foreach (string t in terms)
            {
                if (tag.Contains(t))  score += 8;
                if (type.Contains(t)) score += 6;
                if (desc.Contains(t)) score += 5;
                if (trig.Contains(t)) score += 4;
                if (cat.Contains(t))  score += 3;
            }
            return score;
        }

        // ── Build ────────────────────────────────────────────────────────────────

        private static Dictionary<string, McpCapability> Build()
        {
            // Keyed by Type.FullName — the dedup identity for reflected commands.
            var byType = new Dictionary<string, McpCapability>(StringComparer.Ordinal);
            // Reflected class short name → Type (first wins) for no-instantiation name matching.
            _typeByName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            // Tagged / dispatchable records that never resolved to a reflected type.
            var standalone = new List<McpCapability>();
            // Real dispatch tag → record, so repeated NLP patterns accumulate triggers.
            var tagIndex = new Dictionary<string, McpCapability>(StringComparer.OrdinalIgnoreCase);

            // Pass 1 — reflect EVERY IExternalCommand type (discover-only baseline).
            foreach (Type t in McpCommandScan.AllCommandTypes())
            {
                if (t?.FullName == null || byType.ContainsKey(t.FullName)) continue;

                string cat = NamespaceLeaf(t);
                var cap = new McpCapability
                {
                    Tag          = t.Name,              // reflected-only id = class short name
                    TypeName     = t.Name,
                    TypeFullName = t.FullName,
                    Category     = cat,
                    Description  = SynthesizeDescription(t, cat),
                    Synthesized  = true,
                    ReadOnly     = ReadOnlyFromType(t), // OFF THE TYPE — no instantiation
                    OpensUI      = ResolveOpensUi(t.Name, null),
                    EngineBacked = false,
                    Dispatchable = false,
                };
                byType[t.FullName] = cap;
                if (!_typeByName.ContainsKey(t.Name)) _typeByName[t.Name] = t;
            }

            int reflectedTotal = byType.Count;

            // Pass 2 — overlay NLP-tagged commands (dispatchable) onto their reflected type.
            foreach (var p in NLPEngine.IntentPatterns)
            {
                if (string.IsNullOrWhiteSpace(p.CommandTag)) continue;
                string tag = p.CommandTag.Trim();

                if (!tagIndex.TryGetValue(tag, out McpCapability cap))
                {
                    cap = ResolveTaggedRecord(tag, byType, standalone);
                    cap.Tag          = tag;                 // tagged tag wins
                    cap.Dispatchable = true;
                    cap.EngineBacked = McpEngineRegistry.IsEngineBacked(tag);
                    if (!string.IsNullOrWhiteSpace(p.Description))
                    {
                        cap.Description = p.Description;
                        cap.Synthesized = false;
                    }
                    cap.Intent   = string.IsNullOrWhiteSpace(p.Intent) ? cap.Intent : p.Intent;
                    if (string.IsNullOrWhiteSpace(cap.Category))
                        cap.Category = string.IsNullOrWhiteSpace(p.Intent) ? "General" : p.Intent;
                    cap.OpensUI  = cap.OpensUI || ResolveOpensUi(tag, cap.Description);
                    tagIndex[tag] = cap;
                }

                string cleaned = CleanPattern(p.Pattern);
                if (!string.IsNullOrWhiteSpace(cleaned) && !cap.Triggers.Contains(cleaned))
                    cap.Triggers.Add(cleaned);
            }

            // Pass 3 — ensure every engine-registry tag is dispatchable even without an NLP pattern.
            foreach (string tag in McpEngineRegistry.Tags)
            {
                if (tagIndex.ContainsKey(tag)) { tagIndex[tag].EngineBacked = true; continue; }
                var cap = ResolveTaggedRecord(tag, byType, standalone);
                cap.Tag          = tag;
                cap.Dispatchable = true;
                cap.EngineBacked = true;
                cap.OpensUI      = cap.OpensUI || ResolveOpensUi(tag, cap.Description);
                tagIndex[tag] = cap;
            }

            // Assemble the final tag-keyed catalogue. Dispatchable records claim their
            // clean key first; reflected-only records that collide get a disambiguated key.
            var byTag = new Dictionary<string, McpCapability>(StringComparer.OrdinalIgnoreCase);
            var ordered = byType.Values.Concat(standalone)
                .OrderByDescending(c => c.Dispatchable);
            int dispatchable = 0;
            foreach (McpCapability cap in ordered)
            {
                if (cap.Dispatchable) dispatchable++;
                string key = cap.Tag;
                if (string.IsNullOrWhiteSpace(key)) key = cap.TypeFullName ?? Guid.NewGuid().ToString();
                if (byTag.ContainsKey(key))
                {
                    // Class-name collision (or a tag equal to another class name): keep the
                    // one already in (dispatchable-first ordering means it is preferred) and
                    // re-key the loser by its full type name so it stays discoverable.
                    string alt = cap.TypeFullName ?? (key + "#" + Guid.NewGuid().ToString("N").Substring(0, 6));
                    if (byTag.ContainsKey(alt)) alt = alt + "#" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    key = alt;
                }
                byTag[key] = cap;
            }

            int discoverOnly = byTag.Count - dispatchable;
            StingLog.Info($"McpCapabilityCatalogue built: {byTag.Count} total " +
                          $"({dispatchable} dispatchable, {discoverOnly} discover-only); " +
                          $"reflected types={reflectedTotal}.");
            return byTag;
        }

        /// <summary>
        /// Resolve a dispatch tag to its reflected record (dedup) or a fresh standalone
        /// record when the type cannot be resolved. Name-match is tried first (zero
        /// instantiation); WorkflowEngine is the fallback for tags whose class name does
        /// not mirror the tag (e.g. "Cable_Calculate" → CableSizerCommand).
        /// </summary>
        private static McpCapability ResolveTaggedRecord(string tag,
            Dictionary<string, McpCapability> byType, List<McpCapability> standalone)
        {
            Type ct = ResolveTagType(tag);

            if (ct?.FullName != null && byType.TryGetValue(ct.FullName, out McpCapability existing))
                return existing;   // DEDUP — reuse the reflected record; caller stamps the tag

            // Tagged command with no reflected match — build a standalone record.
            var cap = new McpCapability
            {
                TypeName     = ct?.Name,
                TypeFullName = ct?.FullName,
                Category     = ct != null ? NamespaceLeaf(ct) : null,
                ReadOnly     = ct != null ? ReadOnlyFromType(ct) : (bool?)null,
                Description  = ct != null ? SynthesizeDescription(ct, NamespaceLeaf(ct)) : tag,
                Synthesized  = true,
            };
            standalone.Add(cap);
            return cap;
        }

        /// <summary>
        /// Best-effort tag→Type. (1) exact / +Command name match against the reflected
        /// set — no instantiation. (2) WorkflowEngine.ResolveCommandPublic fallback which
        /// DOES instantiate once (only for tags that name-match cannot resolve); guarded.
        /// </summary>
        private static Type ResolveTagType(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || _typeByName == null) return null;

            if (_typeByName.TryGetValue(tag, out Type t1)) return t1;
            if (_typeByName.TryGetValue(tag + "Command", out Type t2)) return t2;
            string flat = tag.Replace("_", "");
            if (_typeByName.TryGetValue(flat + "Command", out Type t3)) return t3;

            try
            {
                var inst = WorkflowEngine.ResolveCommandPublic(tag);
                return inst?.GetType();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Catalogue tag→type '{tag}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Read the [Transaction] mode OFF THE TYPE: ReadOnly → true, Manual/Automatic →
        /// false, no attribute → null. No command is ever instantiated here.
        /// </summary>
        private static bool? ReadOnlyFromType(Type t)
        {
            try
            {
                var attr = t.GetCustomAttributes(typeof(Autodesk.Revit.Attributes.TransactionAttribute), false)
                    .OfType<Autodesk.Revit.Attributes.TransactionAttribute>()
                    .FirstOrDefault();
                if (attr == null) return null;
                return attr.Mode == Autodesk.Revit.Attributes.TransactionMode.ReadOnly;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Catalogue readOnly reflect '{t?.FullName}': {ex.Message}");
                return null;
            }
        }

        private static bool ResolveOpensUi(string tag, string description)
        {
            if (!string.IsNullOrEmpty(tag) && _curatedOpensUi.Contains(tag)) return true;
            string hay = ((tag ?? "") + " " + (description ?? "")).ToLowerInvariant();
            return _opensUiHints.Any(h => hay.Contains(h));
        }

        /// <summary>Namespace leaf — "StingTools.Commands.Electrical.Routing" → "Routing".</summary>
        private static string NamespaceLeaf(Type t)
        {
            string ns = t.Namespace ?? "";
            string leaf = ns.Split('.').LastOrDefault();
            return string.IsNullOrWhiteSpace(leaf) ? "General" : leaf;
        }

        /// <summary>
        /// Synthesize a readable description for a reflected-only command from its
        /// namespace leaf + humanized class name, e.g.
        /// "Electrical: ConduitAutoRouteCommand — conduit auto route".
        /// </summary>
        private static string SynthesizeDescription(Type t, string category)
        {
            string cls = t.Name;
            string human = HumanizeClassName(cls);
            return $"{category}: {cls} — {human}.";
        }

        /// <summary>"ConduitAutoRouteCommand" → "conduit auto route".</summary>
        private static string HumanizeClassName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "command";
            string s = name;
            if (s.EndsWith("Command", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - "Command".Length);
            // Insert spaces at camel-case / digit boundaries.
            s = Regex.Replace(s, @"(?<=[a-z0-9])(?=[A-Z])", " ");
            s = Regex.Replace(s, @"(?<=[A-Z])(?=[A-Z][a-z])", " ");
            s = s.Replace('_', ' ');
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            return s.Length == 0 ? "command" : s.ToLowerInvariant();
        }

        /// <summary>Turn a regex trigger pattern into a readable keyword phrase.</summary>
        private static string CleanPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return "";
            string s = pattern;
            s = s.Replace(@"\b", " ").Replace(@"\s+", " ").Replace(@"\s*", " ")
                 .Replace(@"\s", " ").Replace(".?", " ").Replace(".*", " ");
            s = Regex.Replace(s, @"\{[^}]*\}", " ");
            s = Regex.Replace(s, @"[()\\?+*^$\[\]]", " ");
            s = s.Replace("|", " / ");
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            return s;
        }
    }
}
