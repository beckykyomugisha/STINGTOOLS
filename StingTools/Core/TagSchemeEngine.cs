using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace StingTools.Core
{
    // ─────────────────────────────────────────────────────────────────────────
    // Tag Scheme engine — project-grammar tag renderings (Phase 191).
    //
    // A TagScheme is a named, data-driven *rendering* of the canonical STING
    // source tokens (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ + STATUS/REV) into a
    // second tag string, written to its own container parameter. The tokens
    // remain the single source of truth — schemes never store independent
    // data, so the corporate 8-segment tag and any project grammar (e.g. the
    // ISO 19650 PROJECT-ORIGINATOR-VOLUME-LEVEL-DISCIPLINE-NUMBER form) can
    // never drift apart: both are derived from the same token values.
    //
    // Segment kinds:
    //   token       — one of the 8 source tokens (or STATUS/REV), with an
    //                 optional value map (e.g. LOC "BLD1" → volume code "01")
    //   projectInfo — a parameter read from ProjectInformation (e.g.
    //                 PRJ_ORG_PROJECT_CODE_TXT), resolved once per document
    //   literal     — fixed text
    //
    // Corporate baseline ships in Data/STING_TAG_SCHEMES.json (all schemes
    // disabled); projects enable/override via <project>/_BIM_COORD/
    // tag_schemes.json merged by id (project wins). A render stamp at
    // _BIM_COORD/.sting_tag_scheme_stamp.json records the checksum of each
    // scheme at its last full render so the Inspect command can surface
    // "scheme edited since last render" drift.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One segment of a tag scheme rendering.</summary>
    public class TagSchemeSegment
    {
        /// <summary>"token" | "projectInfo" | "literal"</summary>
        [JsonProperty("kind")]
        public string Kind { get; set; } = "token";

        /// <summary>Token key for kind=token: DISC/LOC/ZONE/LVL/SYS/FUNC/PROD/SEQ/STATUS/REV.</summary>
        [JsonProperty("token")]
        public string Token { get; set; }

        /// <summary>ProjectInformation parameter name for kind=projectInfo.</summary>
        [JsonProperty("param")]
        public string Param { get; set; }

        /// <summary>Fixed text for kind=literal.</summary>
        [JsonProperty("text")]
        public string Text { get; set; }

        /// <summary>Value used when the source resolves empty (after map lookup).</summary>
        [JsonProperty("fallback")]
        public string Fallback { get; set; }

        /// <summary>Optional value remap applied after the source value resolves
        /// (e.g. {"BLD1":"01"} to translate STING LOC codes to BEP volume codes).
        /// Unmapped values pass through unchanged.</summary>
        [JsonProperty("map")]
        public Dictionary<string, string> Map { get; set; }

        /// <summary>When true (default) an empty segment is omitted rather than
        /// producing a double separator.</summary>
        [JsonProperty("omitIfEmpty")]
        public bool OmitIfEmpty { get; set; } = true;
    }

    /// <summary>A named project-grammar tag rendering.</summary>
    public class TagScheme
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>"corporate" | "project" | "example"</summary>
        [JsonProperty("origin")]
        public string Origin { get; set; } = "corporate";

        /// <summary>Schemes render only when enabled — corporate baseline ships
        /// everything disabled; projects opt in via the overlay file.</summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>Target text parameter the rendered string is written to.
        /// Must not be a source token parameter or the canonical ASS_TAG_1_TXT.</summary>
        [JsonProperty("targetParam")]
        public string TargetParam { get; set; } = TagSchemeRegistry.DefaultTargetParam;

        [JsonProperty("separator")]
        public string Separator { get; set; } = "-";

        [JsonProperty("segments")]
        public List<TagSchemeSegment> Segments { get; set; } = new List<TagSchemeSegment>();
    }

    /// <summary>Root JSON document for STING_TAG_SCHEMES.json.</summary>
    public class TagSchemeLibrary
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("schemes")]
        public List<TagScheme> Schemes { get; set; } = new List<TagScheme>();
    }

    /// <summary>
    /// Loader + per-document cache for tag schemes. Corporate baseline from
    /// Data/STING_TAG_SCHEMES.json, project overlay from
    /// &lt;project&gt;/_BIM_COORD/tag_schemes.json (merged by id, project wins).
    /// </summary>
    public static class TagSchemeRegistry
    {
        public const string DefaultTargetParam = "ASS_TAG_SCHEME_TXT";
        private const string CorporateFileName = "STING_TAG_SCHEMES.json";
        private const string ProjectFileName = "tag_schemes.json";
        private const string StampFileName = ".sting_tag_scheme_stamp.json";

        // Parameters a scheme may never target — the canonical tag and the
        // source tokens themselves. Writing those from a rendering would
        // corrupt the single source of truth.
        private static readonly HashSet<string> _forbiddenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ASS_TAG_1_TXT",
            "ASS_DISCIPLINE_COD_TXT", "ASS_LOC_TXT", "ASS_ZONE_TXT", "ASS_LVL_COD_TXT",
            "ASS_SYSTEM_TYPE_TXT", "ASS_FUNC_TXT", "ASS_PRODCT_COD_TXT", "ASS_SEQ_NUM_TXT",
            "ASS_STATUS_TXT", "ASS_REV_TXT",
        };

        // Cache keyed on the project directory ("" for unsaved docs).
        private static readonly ConcurrentDictionary<string, List<TagScheme>> _cache
            = new ConcurrentDictionary<string, List<TagScheme>>(StringComparer.OrdinalIgnoreCase);

        // ProjectInformation value cache: docKey → (paramName → value).
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> _projInfoCache
            = new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            try { return Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; }
            catch { return ""; }
        }

        /// <summary>All schemes visible to this document (corporate + project overlay).</summary>
        public static List<TagScheme> GetAll(Document doc)
        {
            string key = DocKey(doc);
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        /// <summary>Schemes that are enabled and structurally valid for rendering.</summary>
        public static List<TagScheme> EnabledSchemes(Document doc)
        {
            var result = new List<TagScheme>();
            foreach (var s in GetAll(doc))
            {
                if (!s.Enabled) continue;
                string reason = ValidateScheme(s);
                if (reason == null) result.Add(s);
                else StingLog.Warn($"TagScheme '{s.Id}' enabled but invalid — skipped: {reason}");
            }
            return result;
        }

        /// <summary>Returns null when the scheme is renderable, else a reason string.</summary>
        public static string ValidateScheme(TagScheme s)
        {
            if (s == null) return "null scheme";
            if (string.IsNullOrWhiteSpace(s.Id)) return "missing id";
            if (s.Segments == null || s.Segments.Count == 0) return "no segments";
            if (string.IsNullOrWhiteSpace(s.TargetParam)) return "missing targetParam";
            if (_forbiddenTargets.Contains(s.TargetParam))
                return $"targetParam '{s.TargetParam}' is a protected source/canonical parameter";
            foreach (var seg in s.Segments)
            {
                switch ((seg.Kind ?? "").ToLowerInvariant())
                {
                    case "token":
                        if (string.IsNullOrWhiteSpace(seg.Token)) return "token segment missing 'token'";
                        break;
                    case "projectinfo":
                        if (string.IsNullOrWhiteSpace(seg.Param)) return "projectInfo segment missing 'param'";
                        break;
                    case "literal":
                        if (seg.Text == null) return "literal segment missing 'text'";
                        break;
                    default:
                        return $"unknown segment kind '{seg.Kind}'";
                }
            }
            return null;
        }

        /// <summary>Force re-read from disk for this document (or all when doc is null).</summary>
        public static void Reload(Document doc = null)
        {
            if (doc == null)
            {
                _cache.Clear();
                _projInfoCache.Clear();
            }
            else
            {
                string key = DocKey(doc);
                _cache.TryRemove(key, out _);
                _projInfoCache.TryRemove(key, out _);
            }
        }

        /// <summary>Drop caches for a closing document — wired to document-close cleanup.</summary>
        public static void InvalidateCache(Document doc) => Reload(doc);

        private static List<TagScheme> Load(Document doc)
        {
            var byId = new Dictionary<string, TagScheme>(StringComparer.OrdinalIgnoreCase);

            // 1. Corporate baseline
            try
            {
                string corpPath = StingToolsApp.FindDataFile(CorporateFileName);
                if (!string.IsNullOrEmpty(corpPath) && File.Exists(corpPath))
                {
                    var lib = JsonConvert.DeserializeObject<TagSchemeLibrary>(File.ReadAllText(corpPath));
                    foreach (var s in lib?.Schemes ?? new List<TagScheme>())
                        if (!string.IsNullOrWhiteSpace(s?.Id))
                            byId[s.Id] = s;
                }
                else
                {
                    StingLog.Info($"TagSchemeRegistry: corporate {CorporateFileName} not found — schemes available via project overlay only");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchemeRegistry: corporate load failed: {ex.Message}");
            }

            // 2. Project overlay — merged by id, project wins; new ids appended
            try
            {
                string projPath = ProjectOverlayPath(doc);
                if (!string.IsNullOrEmpty(projPath) && File.Exists(projPath))
                {
                    var lib = JsonConvert.DeserializeObject<TagSchemeLibrary>(File.ReadAllText(projPath));
                    foreach (var s in lib?.Schemes ?? new List<TagScheme>())
                    {
                        if (string.IsNullOrWhiteSpace(s?.Id)) continue;
                        s.Origin = "project";
                        byId[s.Id] = s;
                    }
                    StingLog.Info($"TagSchemeRegistry: project overlay loaded from {projPath}");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchemeRegistry: project overlay load failed: {ex.Message}");
            }

            return byId.Values.ToList();
        }

        /// <summary>Path of the project overlay file (may not exist yet); null for unsaved docs.</summary>
        public static string ProjectOverlayPath(Document doc)
        {
            string dir = DocKey(doc);
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "_BIM_COORD", ProjectFileName);
        }

        /// <summary>SHA-256 checksum of a scheme's canonical JSON — used for the render stamp.</summary>
        public static string ComputeChecksum(TagScheme s)
        {
            try
            {
                string json = JsonConvert.SerializeObject(s, Formatting.None);
                using (var sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                    var sb = new StringBuilder(hash.Length * 2);
                    foreach (byte b in hash) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        // ── Render stamp: schemeId → checksum at last full render ──

        private class StampFile
        {
            [JsonProperty("renderedUtc")] public DateTime RenderedUtc { get; set; }
            [JsonProperty("checksums")] public Dictionary<string, string> Checksums { get; set; }
                = new Dictionary<string, string>();
        }

        private static string StampPath(Document doc)
        {
            string dir = DocKey(doc);
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "_BIM_COORD", StampFileName);
        }

        /// <summary>Checksums recorded at the last full render, empty when never rendered.</summary>
        public static Dictionary<string, string> LoadStamp(Document doc)
        {
            try
            {
                string path = StampPath(doc);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var stamp = JsonConvert.DeserializeObject<StampFile>(File.ReadAllText(path));
                    return stamp?.Checksums ?? new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchemeRegistry.LoadStamp: {ex.Message}");
            }
            return new Dictionary<string, string>();
        }

        /// <summary>Record current checksums of the given schemes as "rendered now".</summary>
        public static void SaveStamp(Document doc, IEnumerable<TagScheme> schemes)
        {
            try
            {
                string path = StampPath(doc);
                if (string.IsNullOrEmpty(path)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Preserve stamps of schemes not in this render pass
                var merged = LoadStamp(doc);
                foreach (var s in schemes)
                    merged[s.Id] = ComputeChecksum(s);
                var stamp = new StampFile { RenderedUtc = DateTime.UtcNow, Checksums = merged };
                File.WriteAllText(path, JsonConvert.SerializeObject(stamp, Formatting.Indented));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TagSchemeRegistry.SaveStamp: {ex.Message}");
            }
        }

        /// <summary>Scheme ids whose definition changed since the last full render
        /// (plus enabled schemes never rendered at all).</summary>
        public static List<string> DriftedSchemeIds(Document doc)
        {
            var drifted = new List<string>();
            var stamp = LoadStamp(doc);
            foreach (var s in GetAll(doc))
            {
                if (!s.Enabled) continue;
                if (!stamp.TryGetValue(s.Id, out string prev) || prev != ComputeChecksum(s))
                    drifted.Add(s.Id);
            }
            return drifted;
        }

        /// <summary>Cached ProjectInformation parameter read (per document).</summary>
        internal static string GetProjectInfoValue(Document doc, string paramName)
        {
            if (doc == null || string.IsNullOrEmpty(paramName)) return "";
            string key = DocKey(doc) + "::" + (doc.Title ?? "");
            var map = _projInfoCache.GetOrAdd(key, _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            lock (map)
            {
                if (map.TryGetValue(paramName, out string cached)) return cached;
                string value = "";
                try
                {
                    Element pi = doc.ProjectInformation;
                    if (pi != null)
                        value = ParameterHelpers.GetString(pi, paramName) ?? "";
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagScheme projectInfo read '{paramName}': {ex.Message}");
                }
                map[paramName] = value;
                return value;
            }
        }
    }

    /// <summary>
    /// Renders enabled tag schemes for an element. Called from
    /// TagPipelineHelper.RunFullPipeline (covers batch commands AND the
    /// real-time IUpdater) and from the batch RenderSchemeTags command.
    /// </summary>
    public static class TagSchemeRenderer
    {
        // tokenVals layout (ParamRegistry.AllTokenParams order):
        // [0]=DISC [1]=LOC [2]=ZONE [3]=LVL [4]=SYS [5]=FUNC [6]=PROD [7]=SEQ
        private static readonly Dictionary<string, int> _tokenSlots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["DISC"] = 0, ["LOC"] = 1, ["ZONE"] = 2, ["LVL"] = 3,
            ["SYS"] = 4, ["FUNC"] = 5, ["PROD"] = 6, ["SEQ"] = 7,
        };

        /// <summary>
        /// Render every enabled scheme for this element and write the result to
        /// each scheme's target parameter. Pass the freshly-built token array
        /// when available (pipeline path); pass null to re-read from the element
        /// (batch render path). Returns the number of scheme strings written.
        /// Must be called inside an open transaction.
        /// </summary>
        public static int RenderAll(Document doc, Element el, string[] tokenVals)
        {
            var schemes = TagSchemeRegistry.EnabledSchemes(doc);
            if (schemes.Count == 0) return 0;

            if (tokenVals == null)
                tokenVals = ParamRegistry.ReadTokenValues(el);

            int written = 0;
            foreach (var scheme in schemes)
            {
                try
                {
                    string rendered = Render(doc, el, scheme, tokenVals);
                    if (string.IsNullOrEmpty(rendered)) continue;
                    if (ParameterHelpers.SetString(el, scheme.TargetParam, rendered, overwrite: true))
                        written++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TagScheme '{scheme.Id}' render on {el?.Id}: {ex.Message}");
                }
            }
            return written;
        }

        /// <summary>
        /// Build the rendered string for one scheme without writing it —
        /// shared by RenderAll and the consistency audit.
        /// </summary>
        public static string Render(Document doc, Element el, TagScheme scheme, string[] tokenVals)
        {
            if (scheme?.Segments == null || scheme.Segments.Count == 0) return "";
            if (tokenVals == null)
                tokenVals = ParamRegistry.ReadTokenValues(el);

            var parts = new List<string>(scheme.Segments.Count);
            foreach (var seg in scheme.Segments)
            {
                string value = ResolveSegment(doc, el, seg, tokenVals);
                if (string.IsNullOrEmpty(value))
                {
                    if (seg.OmitIfEmpty) continue;
                    value = "";
                }
                parts.Add(value);
            }
            if (parts.Count == 0) return "";

            string sep = string.IsNullOrEmpty(scheme.Separator) ? "-" : scheme.Separator;
            return string.Join(sep, parts);
        }

        private static string ResolveSegment(Document doc, Element el, TagSchemeSegment seg, string[] tokenVals)
        {
            string raw;
            switch ((seg.Kind ?? "token").ToLowerInvariant())
            {
                case "literal":
                    return seg.Text ?? "";

                case "projectinfo":
                    raw = TagSchemeRegistry.GetProjectInfoValue(doc, seg.Param);
                    break;

                case "token":
                default:
                    string tok = (seg.Token ?? "").ToUpperInvariant();
                    if (_tokenSlots.TryGetValue(tok, out int slot)
                        && tokenVals != null && slot < tokenVals.Length)
                    {
                        raw = tokenVals[slot] ?? "";
                    }
                    else if (tok == "STATUS")
                    {
                        raw = ParameterHelpers.GetString(el, ParamRegistry.STATUS);
                    }
                    else if (tok == "REV")
                    {
                        raw = ParameterHelpers.GetString(el, ParamRegistry.REV);
                    }
                    else
                    {
                        raw = "";
                    }
                    break;
            }

            // Optional value remap (e.g. STING LOC code → BEP volume code)
            if (!string.IsNullOrEmpty(raw)
                && seg.Map != null
                && seg.Map.TryGetValue(raw, out string mapped))
            {
                raw = mapped;
            }

            if (string.IsNullOrEmpty(raw) && !string.IsNullOrEmpty(seg.Fallback))
                raw = seg.Fallback;

            return raw ?? "";
        }
    }
}
