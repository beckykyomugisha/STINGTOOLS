// StingTools — Spatial Code Registry (F-9)
//
// One vocabulary for LVL and LOC, replacing five level vocabularies and three
// LOC vocabularies that did not agree. See
// docs/F9_SPATIAL_CODE_RECONCILIATION.md for what each of the eight was and
// where they diverged.
//
// Corporate baseline : Data/STING_SPATIAL_CODES.json
// Project override   : <project>/_BIM_COORD/spatial_codes.json
//
// Mirrors AecFilterRegistry / ViewStylePackRegistry / DrawingTypeRegistry in
// shape so consumers learn one pattern: static Get, per-document cache keyed on
// the project path, explicit Reload.
//
// TWO THINGS THIS FILE DELIBERATELY DOES NOT DO
//
//   1. It does not enumerate L06-L99. The baseline declares five levels and a
//      synthesis rule; SynthesiseLevel() applies the rule. Shipping 99 rows AND
//      a rule would let the two disagree, which is how the vocabularies got
//      into this state.
//
//   2. isoCode is NULLABLE and null is meaningful: "this code has no ISO 19650
//      equivalent". LG, UG, SB, POD, TR, AT, PL, MZ, UR, ZZ and XX are all
//      null. A consumer must fall back explicitly rather than emit a non-ISO
//      code into an ISO field — the drafted baseline mapped these to
//      themselves, which quietly asserted an equivalence that does not exist.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core
{
    public class SpatialLevelCode
    {
        [JsonProperty("code")]      public string Code { get; set; } = "";
        /// <summary>ISO 19650 level field, or NULL when there is no equivalent.</summary>
        [JsonProperty("isoCode")]   public string IsoCode { get; set; }
        [JsonProperty("label")]     public string Label { get; set; } = "";
        [JsonProperty("aliases")]   public List<string> Aliases { get; set; } = new List<string>();
        [JsonProperty("sortOrder")] public int SortOrder { get; set; }
        /// <summary>"reconciled" (an existing vocabulary already produces it) or
        /// "new" (this registry introduces it).</summary>
        [JsonProperty("origin")]    public string Origin { get; set; } = "reconciled";
        [JsonProperty("note")]      public string Note { get; set; }
    }

    public class SpatialLocCode
    {
        [JsonProperty("code")]    public string Code { get; set; } = "";
        [JsonProperty("label")]   public string Label { get; set; } = "";
        [JsonProperty("aliases")] public List<string> Aliases { get; set; } = new List<string>();
        [JsonProperty("kind")]    public string Kind { get; set; } = "";
        /// <summary>When true, an alias must match on a word boundary. EXT must
        /// not match NEXT / TEXTILE / EXTENSION — the guard ParseLocCode already
        /// had, made declarative so every short code can use it.</summary>
        [JsonProperty("wordBoundary")] public bool WordBoundary { get; set; }
        [JsonProperty("note")]    public string Note { get; set; }
    }

    public class SpatialCodeLibrary
    {
        [JsonProperty("schemaVersion")] public string SchemaVersion { get; set; } = "1.0";
        [JsonProperty("levels")]        public List<SpatialLevelCode> Levels { get; set; } = new List<SpatialLevelCode>();
        [JsonProperty("locations")]     public List<SpatialLocCode> Locations { get; set; } = new List<SpatialLocCode>();
    }

    public static class SpatialCodeRegistry
    {
        public const string DataFileName = "STING_SPATIAL_CODES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/spatial_codes.json";

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, SpatialCodeLibrary> _cache =
            new Dictionary<string, SpatialCodeLibrary>(StringComparer.OrdinalIgnoreCase);

        // ── entry points ────────────────────────────────────────────────────

        public static IReadOnlyList<SpatialLevelCode> LevelCodes(Document doc)
            => GetLibrary(doc).Levels;

        public static IReadOnlyList<SpatialLocCode> LocCodes(Document doc)
            => GetLibrary(doc).Locations;

        /// <summary>Human label for a code, for prose. Kills the LG → "Level G"
        /// bug: BuildLocationPhrase should read this instead of decorating the
        /// raw code with the word "Level".</summary>
        public static string Prose(Document doc, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            var lvl = MatchExactLevel(doc, code);
            if (lvl != null) return lvl.Label;
            var loc = GetLibrary(doc).Locations
                .FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
            return loc?.Label ?? code;
        }

        /// <summary>Match a Revit LEVEL NAME to a code. Null when nothing matches —
        /// the caller keeps its own miss path (GetLevelCode's sanitize-passthrough).</summary>
        public static SpatialLevelCode MatchLevel(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string n = name.Trim().ToLowerInvariant();
            var lib = GetLibrary(doc);

            // Exact code first, so "GF" resolves without touching aliases.
            var exact = lib.Levels.FirstOrDefault(l =>
                string.Equals(l.Code, n, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Longest alias first: "lower ground" must beat "ground".
            foreach (var l in lib.Levels)
                foreach (var a in (l.Aliases ?? new List<string>()).OrderByDescending(x => x?.Length ?? 0))
                    if (!string.IsNullOrWhiteSpace(a) && n == a.ToLowerInvariant())
                        return l;
            foreach (var l in lib.Levels.OrderByDescending(x => (x.Aliases ?? new List<string>()).Max(a => a?.Length ?? 0)))
                foreach (var a in (l.Aliases ?? new List<string>()).OrderByDescending(x => x?.Length ?? 0))
                    if (!string.IsNullOrWhiteSpace(a) && n.Contains(a.ToLowerInvariant()))
                        return l;

            return SynthesiseLevel(n);
        }

        /// <summary>Match text to a LOC code. Honours per-code wordBoundary.</summary>
        public static SpatialLocCode MatchLoc(Document doc, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string upper = text.Trim().ToUpperInvariant();
            var lib = GetLibrary(doc);

            // Exact code, then code-as-substring on a word boundary.
            foreach (var l in lib.Locations)
            {
                if (string.IsNullOrEmpty(l.Code)) continue;
                if (string.Equals(upper, l.Code, StringComparison.OrdinalIgnoreCase)) return l;
            }
            foreach (var l in lib.Locations.OrderByDescending(x => x.Code?.Length ?? 0))
            {
                if (string.IsNullOrEmpty(l.Code)) continue;
                if (ContainsToken(upper, l.Code.ToUpperInvariant(), l.WordBoundary)) return l;
            }
            // Aliases, longest first so "block a" beats "a".
            foreach (var l in lib.Locations)
                foreach (var a in (l.Aliases ?? new List<string>()).OrderByDescending(x => x?.Length ?? 0))
                    if (!string.IsNullOrWhiteSpace(a) &&
                        ContainsToken(upper, a.ToUpperInvariant(), l.WordBoundary))
                        return l;
            return null;
        }

        public static void Reload(Document doc)
        {
            lock (_lock) _cache.Remove(KeyFor(doc));
        }

        public static void Reload()
        {
            lock (_lock) _cache.Clear();
        }

        // ── synthesis ───────────────────────────────────────────────────────

        /// <summary>
        /// L06-L99 and SB1-SB9 are not enumerated in the baseline — they follow a
        /// rule, and the rule lives here so the data cannot contradict it.
        /// </summary>
        internal static SpatialLevelCode SynthesiseLevel(string lowerName)
        {
            // "level 7", "l07", "07"
            var m = System.Text.RegularExpressions.Regex.Match(
                lowerName, @"^(?:level\s*|l)?(\d{1,3})$");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n >= 0 && n <= 999)
            {
                if (n == 0)
                    return new SpatialLevelCode { Code = "GF", IsoCode = "00", Label = "Ground floor", SortOrder = 0, Origin = "synthesised" };
                return new SpatialLevelCode
                {
                    Code = "L" + n.ToString("00"),
                    IsoCode = n.ToString("00"),
                    Label = "Level " + n,
                    SortOrder = 100 + n,
                    Origin = "synthesised"
                };
            }
            var sb = System.Text.RegularExpressions.Regex.Match(
                lowerName, @"^sub[\s-]?basement\s*(\d)?$|^sb(\d)$");
            if (sb.Success)
            {
                string d = string.IsNullOrEmpty(sb.Groups[1].Value) ? sb.Groups[2].Value : sb.Groups[1].Value;
                return new SpatialLevelCode
                {
                    Code = "SB" + d,
                    IsoCode = null,
                    Label = "Sub-basement" + (string.IsNullOrEmpty(d) ? "" : " " + d),
                    SortOrder = -40,
                    Origin = "synthesised"
                };
            }
            return null;
        }

        // ── loading ─────────────────────────────────────────────────────────

        private static SpatialLevelCode MatchExactLevel(Document doc, string code)
            => GetLibrary(doc).Levels
                .FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
               ?? SynthesiseLevel((code ?? "").ToLowerInvariant());

        private static bool ContainsToken(string haystack, string needle, bool wordBoundary)
        {
            if (string.IsNullOrEmpty(needle)) return false;
            if (!wordBoundary) return haystack.IndexOf(needle, StringComparison.Ordinal) >= 0;
            return System.Text.RegularExpressions.Regex.IsMatch(
                haystack, @"(^|[^A-Z0-9])" + System.Text.RegularExpressions.Regex.Escape(needle) + @"([^A-Z0-9]|$)");
        }

        private static string KeyFor(Document doc)
        {
            try { return doc?.PathName ?? "<no-doc>"; } catch { return "<no-doc>"; }
        }

        public static SpatialCodeLibrary GetLibrary(Document doc)
        {
            string key = KeyFor(doc);
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var hit)) return hit;
                var lib = Load(doc);
                _cache[key] = lib;
                return lib;
            }
        }

        private static SpatialCodeLibrary Load(Document doc)
        {
            var lib = new SpatialCodeLibrary();
            try
            {
                string basePath = StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    lib = JsonConvert.DeserializeObject<SpatialCodeLibrary>(File.ReadAllText(basePath)) ?? lib;
                else
                    StingLog.Warn($"SpatialCodeRegistry: {DataFileName} not found — the registry is empty and "
                                + "every caller will fall back to its own miss path.");

                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projPath = ProjectFolderEngine.ResolveProjectOverridePath(doc, ProjectOverrideRelPath);
                    if (!string.IsNullOrEmpty(projPath) && File.Exists(projPath))
                    {
                        var over = JsonConvert.DeserializeObject<SpatialCodeLibrary>(File.ReadAllText(projPath));
                        if (over != null) Merge(lib, over);
                    }
                }

                // F-1 bridge. A project that already declares its LOC vocabulary
                // through the existing three config keys must be reachable WITHOUT
                // authoring a second file — otherwise this registry would be one
                // more vocabulary rather than one fewer. TagConfig.LocCodes is the
                // key those three fold into; anything it carries that the library
                // does not becomes an exact-match code with no aliases.
                //
                // This is what makes Kibale's COT01-COT08 / STF / KDR / POOL
                // reachable today, from config that already exists.
                try
                {
                    foreach (var code in TagConfig.LocCodes ?? new List<string>())
                    {
                        if (string.IsNullOrWhiteSpace(code)) continue;
                        if (lib.Locations.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        lib.Locations.Add(new SpatialLocCode
                        {
                            Code = code.Trim(),
                            Label = code.Trim(),
                            Aliases = new List<string>(),
                            Kind = "project",
                            // Short project codes are the ones most at risk of
                            // matching inside an unrelated word, so they get the
                            // same guard EXT has always had.
                            WordBoundary = code.Trim().Length <= 4,
                            Note = "Contributed by TagConfig.LocCodes (project configuration), not by the baseline."
                        });
                    }
                }
                catch (Exception ex)
                {
                    StingLog.WarnRateLimited("SpatialCodes.TagConfigBridge", $"LocCodes bridge: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("SpatialCodeRegistry.Load", ex);
            }
            return lib;
        }

        /// <summary>Project entries win by code; new codes extend. Same merge rule
        /// as AecFilterRegistry, so a project can correct a corporate code without
        /// restating the whole vocabulary.</summary>
        private static void Merge(SpatialCodeLibrary bas, SpatialCodeLibrary over)
        {
            foreach (var l in over.Levels ?? new List<SpatialLevelCode>())
            {
                bas.Levels.RemoveAll(x => string.Equals(x.Code, l.Code, StringComparison.OrdinalIgnoreCase));
                bas.Levels.Add(l);
            }
            foreach (var l in over.Locations ?? new List<SpatialLocCode>())
            {
                bas.Locations.RemoveAll(x => string.Equals(x.Code, l.Code, StringComparison.OrdinalIgnoreCase));
                bas.Locations.Add(l);
            }
        }
    }
}
