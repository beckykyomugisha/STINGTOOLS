// ============================================================================
// MepFixtureMap.cs — Phase: MEP-from-DWG V1.
//
// Block-name → Revit-fixture mapping for DWG→MEP conversion. The corporate
// baseline ships in Data/STING_DWG_FIXTURE_MAP.json; a project override at
// <project>/_BIM_COORD/dwg_fixture_map.json is layered over it by id (project
// wins, new ids append) — mirrors AecFilterRegistry / STING_TAG_SCHEMES.
//
// This is the single source of truth for "what block name is which fixture",
// replacing the hardcoded ~6-name lists scattered across Temp/. It carries NO
// Revit API dependency in its data model so the matching is unit-testable; the
// registry's project-override loader takes a Document only for the path.
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Cad.Mep
{
    /// <summary>One block-name → fixture rule.</summary>
    public class MepFixtureRule
    {
        /// <summary>Stable id — the override key (project entry with the same id wins).</summary>
        public string Id { get; set; } = "";
        /// <summary>Regex matched (case-insensitive) against the DWG block name.</summary>
        public string BlockNameRegex { get; set; } = "";
        /// <summary>Revit category display name, e.g. "Electrical Fixtures", "Air Terminals".</summary>
        public string Category { get; set; } = "";
        /// <summary>Optional regex narrowing the family name when resolving the symbol.</summary>
        public string FamilyHint { get; set; } = "";
        /// <summary>Optional regex narrowing the type/symbol name.</summary>
        public string TypeHint { get; set; } = "";
        /// <summary>Key into STING_HEIGHT_STANDARDS.json for the mounting height (preferred).</summary>
        public string MountingHeightSource { get; set; } = "";
        /// <summary>Explicit mounting height (mm above the level) when no source resolves.</summary>
        public double MountingHeightMm { get; set; }
        /// <summary>FFL | Ceiling | Structure — informational in V1 (Z = level + height);
        /// drives host-snapping in V2.</summary>
        public string MountingReference { get; set; } = "FFL";
        /// <summary>Higher wins when several rules match a block name.</summary>
        public int Priority { get; set; }
        /// <summary>"corporate" or "project" — stamped by the loader.</summary>
        public string Origin { get; set; } = "";

        private Regex _blockRx;
        private bool _compiled;

        /// <summary>Match score against a block name, or -1 when it does not apply.
        /// Score is the rule Priority (so a higher-priority rule beats a lower one);
        /// ties resolve to the longer regex (more specific).</summary>
        public int Score(string blockName)
        {
            if (!_compiled)
            {
                _compiled = true;
                if (!string.IsNullOrEmpty(BlockNameRegex))
                    try { _blockRx = new Regex(BlockNameRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                    catch (Exception ex) { StingLog.Warn($"MepFixtureRule '{Id}' bad regex '{BlockNameRegex}': {ex.Message}"); }
            }
            if (_blockRx == null || string.IsNullOrEmpty(blockName)) return -1;
            return _blockRx.IsMatch(blockName) ? Priority : -1;
        }
    }

    /// <summary>Root document of STING_DWG_FIXTURE_MAP.json.</summary>
    public class MepFixtureMapLibrary
    {
        public string Version { get; set; } = "v1";
        public string Description { get; set; } = "";
        public List<MepFixtureRule> Fixtures { get; set; } = new List<MepFixtureRule>();

        /// <summary>Best-matching rule for a block name, or null. Highest score
        /// (Priority) wins; ties break to the longer regex (more specific).</summary>
        public MepFixtureRule Match(string blockName)
        {
            if (string.IsNullOrEmpty(blockName) || Fixtures == null) return null;
            MepFixtureRule best = null;
            int bestScore = -1;
            foreach (var r in Fixtures)
            {
                int s = r.Score(blockName);
                if (s < 0) continue;
                if (s > bestScore ||
                    (s == bestScore && (r.BlockNameRegex?.Length ?? 0) > (best?.BlockNameRegex?.Length ?? 0)))
                { bestScore = s; best = r; }
            }
            return best;
        }
    }

    /// <summary>Per-document loader + cache. Corporate baseline + project override
    /// merged by id (project wins). Mirrors AecFilterRegistry.</summary>
    public static class MepFixtureMap
    {
        private const string CorporateFile = "STING_DWG_FIXTURE_MAP.json";
        private const string ProjectFile   = "dwg_fixture_map.json";

        private static readonly ConcurrentDictionary<string, MepFixtureMapLibrary> _cache
            = new ConcurrentDictionary<string, MepFixtureMapLibrary>(StringComparer.OrdinalIgnoreCase);
        private static MepFixtureMapLibrary _corporate;
        private static readonly object _corpLock = new object();

        /// <summary>Library for this document (cached). Project override layered over corporate.</summary>
        public static MepFixtureMapLibrary Get(Autodesk.Revit.DB.Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _cache.GetOrAdd(key, _ => Merge(LoadCorporate(), LoadProjectOverride(doc)));
        }

        /// <summary>Corporate-only library (no document) — used by the legacy doc-less
        /// plan converter in Temp/DWGImportCommands.</summary>
        public static MepFixtureMapLibrary Corporate() => LoadCorporate();

        /// <summary>Classify a block name to the legacy ConvertedElement token used by the
        /// doc-less DWG plan converter (Temp/DWGImportCommands.ConvertBlockReference),
        /// so the block-name patterns live only in the map. Returns null for categories
        /// without a legacy token (the caller's regex fallback then runs) — guarantees no
        /// regression on the existing plumbing/lighting/electrical/sprinkler cases.</summary>
        public static (string Token, string ElementType)? ClassifyLegacy(string blockName)
        {
            var rule = Corporate()?.Match(blockName);
            if (rule == null) return null;
            switch (rule.Category)
            {
                case "Plumbing Fixtures":  return ("PlumbingFixtures", "PlumbingFixture");
                case "Lighting Fixtures":  return ("LightingFixtures", "LightingFixture");
                case "Electrical Fixtures":
                case "Electrical Equipment": return ("ElectricalEquipment", "ElectricalEquipment");
                case "Sprinklers":         return ("Sprinklers", "Sprinkler");
                default: return null;   // Air Terminals / Fire Alarm / etc. → caller fallback
            }
        }

        public static void Invalidate() { _cache.Clear(); lock (_corpLock) _corporate = null; }

        private static MepFixtureMapLibrary LoadCorporate()
        {
            lock (_corpLock)
            {
                if (_corporate != null) return _corporate;
                _corporate = new MepFixtureMapLibrary();
                try
                {
                    string path = StingToolsApp.FindDataFile(CorporateFile);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        var lib = JsonConvert.DeserializeObject<MepFixtureMapLibrary>(File.ReadAllText(path));
                        if (lib != null)
                        {
                            foreach (var r in lib.Fixtures ?? new List<MepFixtureRule>())
                                if (string.IsNullOrEmpty(r.Origin)) r.Origin = "corporate";
                            _corporate = lib;
                        }
                    }
                    else StingLog.Warn($"MepFixtureMap: {CorporateFile} not found in DataPath");
                }
                catch (Exception ex) { StingLog.Warn($"MepFixtureMap corporate load: {ex.Message}"); }
                return _corporate;
            }
        }

        private static MepFixtureMapLibrary LoadProjectOverride(Autodesk.Revit.DB.Document doc)
        {
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(dir)) return null;
                string path = Path.Combine(dir, "_BIM_COORD", ProjectFile);
                if (!File.Exists(path)) return null;
                var lib = JsonConvert.DeserializeObject<MepFixtureMapLibrary>(File.ReadAllText(path));
                if (lib?.Fixtures != null)
                    foreach (var r in lib.Fixtures)
                        if (string.IsNullOrEmpty(r.Origin)) r.Origin = "project";
                return lib;
            }
            catch (Exception ex) { StingLog.Warn($"MepFixtureMap project override: {ex.Message}"); return null; }
        }

        private static MepFixtureMapLibrary Merge(MepFixtureMapLibrary corporate, MepFixtureMapLibrary project)
        {
            var merged = new MepFixtureMapLibrary
            {
                Version = corporate?.Version ?? "v1",
                Description = corporate?.Description ?? "",
                Fixtures = new List<MepFixtureRule>(corporate?.Fixtures ?? new List<MepFixtureRule>())
            };
            if (project?.Fixtures == null) return merged;

            var byId = merged.Fixtures
                .Where(f => !string.IsNullOrEmpty(f.Id))
                .ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var pf in project.Fixtures)
            {
                if (string.IsNullOrEmpty(pf.Id)) { merged.Fixtures.Add(pf); continue; }
                byId[pf.Id] = pf;            // project wins
            }
            // Rebuild: ided entries from the dictionary + any id-less project rows already appended.
            merged.Fixtures = byId.Values.Concat(merged.Fixtures.Where(f => string.IsNullOrEmpty(f.Id))).ToList();
            return merged;
        }
    }
}
