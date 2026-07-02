// Healthcare Pack — canonical CLN_ROOM_CLASS_TXT vocabulary + resolver.
//
// Single source of truth for the healthcare room-class value set. Before this
// registry the producer (HbnRoomAutoPopulator) keyed on underscore codes
// (CT, WARD, PE_ROOM) while every consumer (validators, standards tables,
// specialist audits, clean-dirty flow) keyed on hyphenated codes
// (IMG-CT, WARD-INPT, PE-PROT) — so a room populated one way silently fell
// through the other's lookups. RoomClassCodes.Canonicalize() maps any known
// alias to the one canonical code so producer and consumer always agree.
//
// Data-driven: the vocabulary lives in Data/HEALTHCARE_ROOM_CLASSES.json
// (corporate baseline) with an optional <project>/_BIM_COORD/room_classes.json
// override — editable without a recompile, mirroring OwnerStandardsRegistry.

using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StingTools.Core.Validation.Healthcare
{
    public class RoomClassEntry
    {
        [JsonProperty("code")] public string Code { get; set; }
        [JsonProperty("label")] public string Label { get; set; }
        [JsonProperty("department")] public string Department { get; set; }
        [JsonProperty("aliases")] public List<string> Aliases { get; set; } = new List<string>();
        [JsonProperty("xref")] public Dictionary<string, string> Xref { get; set; } = new Dictionary<string, string>();
    }

    public class RoomClassDef
    {
        [JsonProperty("roomClasses")] public List<RoomClassEntry> RoomClasses { get; set; } = new List<RoomClassEntry>();
    }

    /// <summary>Compiled, per-project view of the room-class vocabulary: the
    /// canonical code set plus an alias→canonical resolver map.</summary>
    public class RoomClassTable
    {
        public IReadOnlyDictionary<string, RoomClassEntry> ByCode { get; }
        // alias (and canonical code) → canonical code, case-insensitive.
        private readonly Dictionary<string, string> _resolve;

        public RoomClassTable(RoomClassDef def)
        {
            var byCode = new Dictionary<string, RoomClassEntry>(StringComparer.OrdinalIgnoreCase);
            _resolve = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in def?.RoomClasses ?? new List<RoomClassEntry>())
            {
                if (string.IsNullOrWhiteSpace(e?.Code)) continue;
                var code = e.Code.Trim();
                byCode[code] = e;
                _resolve[code] = code;                       // canonical resolves to itself
                foreach (var a in e.Aliases ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(a)) continue;
                    var alias = a.Trim();
                    // First writer wins — a canonical code never loses to an alias,
                    // and an earlier entry's alias is not silently stolen by a later one.
                    if (!_resolve.ContainsKey(alias)) _resolve[alias] = code;
                }
            }
            ByCode = byCode;
        }

        /// <summary>Maps any known alias (or canonical code) to the canonical code.
        /// Unknown values are returned trimmed and unchanged so callers keep the
        /// original for drift reporting (RoomClassCodeValidator flags them).</summary>
        public string Canonicalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw?.Trim() ?? "";
            var key = raw.Trim();
            return _resolve.TryGetValue(key, out var canon) ? canon : key;
        }

        public bool IsCanonical(string code) =>
            !string.IsNullOrWhiteSpace(code) && ByCode.ContainsKey(code.Trim());

        /// <summary>True when the raw value is a canonical code OR a known alias.</summary>
        public bool IsRecognised(string raw) =>
            !string.IsNullOrWhiteSpace(raw) && _resolve.ContainsKey(raw.Trim());

        public string DepartmentOf(string raw) =>
            ByCode.TryGetValue(Canonicalize(raw), out var e) ? (e.Department ?? "") : "";

        public IEnumerable<string> AllCodes => ByCode.Keys;
    }

    public static class RoomClassCodes
    {
        private const string CorporateFileName = "HEALTHCARE_ROOM_CLASSES.json";
        private const string ProjectFileName = "room_classes.json";

        private static readonly ConcurrentDictionary<string, RoomClassTable> _cache
            = new ConcurrentDictionary<string, RoomClassTable>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            try { return Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; }
            catch { return ""; }
        }

        /// <summary>The compiled vocabulary for a document's project (corporate
        /// baseline + optional project override), cached per project directory.</summary>
        public static RoomClassTable Get(Document doc) => _cache.GetOrAdd(DocKey(doc), _ => Load(doc));

        public static void Reload(Document doc = null)
        {
            if (doc == null) _cache.Clear();
            else _cache.TryRemove(DocKey(doc), out _);
        }

        // Convenience pass-throughs — every caller has a Document in hand.
        public static string Canonicalize(string raw, Document doc) => Get(doc).Canonicalize(raw);
        public static bool IsRecognised(string raw, Document doc) => Get(doc).IsRecognised(raw);

        private static RoomClassTable Load(Document doc)
        {
            RoomClassDef def = null;
            try
            {
                string corp = StingToolsApp.FindDataFile(CorporateFileName);
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    def = JsonConvert.DeserializeObject<RoomClassDef>(File.ReadAllText(corp));
            }
            catch (Exception ex) { StingLog.Warn($"RoomClassCodes corporate load: {ex.Message}"); }
            def = def ?? new RoomClassDef();

            try
            {
                string dir = DocKey(doc);
                if (!string.IsNullOrEmpty(dir))
                {
                    string proj = Path.Combine(dir, "_BIM_COORD", ProjectFileName);
                    if (File.Exists(proj))
                    {
                        var overlay = JsonConvert.DeserializeObject<RoomClassDef>(File.ReadAllText(proj));
                        foreach (var e in overlay?.RoomClasses ?? new List<RoomClassEntry>())
                        {
                            if (string.IsNullOrWhiteSpace(e?.Code)) continue;
                            def.RoomClasses.RemoveAll(x => string.Equals(x.Code, e.Code, StringComparison.OrdinalIgnoreCase));
                            def.RoomClasses.Add(e);
                        }
                        StingLog.Info($"RoomClassCodes: project overlay loaded from {proj}");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"RoomClassCodes overlay load: {ex.Message}"); }

            if (def.RoomClasses.Count == 0)
                StingLog.Warn("RoomClassCodes: no canonical room classes loaded — " +
                              "check Data/HEALTHCARE_ROOM_CLASSES.json is deployed alongside the plugin.");
            return new RoomClassTable(def);
        }
    }
}
