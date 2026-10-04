using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.ExLink
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (C1) — Fohlio ExLink profile.
    //
    // Fohlio is the Owner's single source of truth for FF&E / finishes / O&M.
    // The information hierarchy is "CDE links to Fohlio, never duplicates", so
    // this integration is a parameter-sync + reference-link layer, NOT a data
    // copy. FOHLIO_REF_TXT carries the Fohlio item URL/ID — the link key.
    //
    // Tier 1 (shipped, and the CONTRACTED route): CSV/XLSX exchange
    //                   (Fohlio_Export / Fohlio_Import). BEP risk row
    //                   "Fohlio API delay -> use file/Add-in route now (Option A)".
    // Tier 2 (UNIMPLEMENTED): IFohlioTransport is an interface declaration only.
    //                   Every method throws NotImplementedException. There is NO
    //                   "Test connection" gate anywhere in the UI - an earlier
    //                   version of this comment described one, and TestConnection()
    //                   returned true whenever BaseUrl and ApiKey were merely
    //                   non-empty, i.e. it passed against a typo. Removed: a
    //                   connection test that makes no connection is worse than none.
    //                   Base URL + key would come from _BIM_COORD/fohlio_connection.json
    //                   (never hardcoded, never committed) if this were ever wired.
    // ─────────────────────────────────────────────────────────────────────────

    // The data model (FohlioColumn, Categories, Columns, BoqTreatment, IsFfeCategory, Parse)
    // lives in FohlioMapData.cs, which is Revit-free and unit-tested against the shipped map.
    public partial class FohlioMap
    {
        // Per-document cache: the BOQ build asks once per FF&E element, so the JSON must
        // be read once per run. Cleared by Cost_ReloadRules alongside the rate registry.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, FohlioMap> _cache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, FohlioMap>(StringComparer.OrdinalIgnoreCase);

        public static FohlioMap Cached(Document doc)
            => _cache.GetOrAdd(doc?.PathName ?? "default", _ => Load(doc));

        public static void Invalidate() => _cache.Clear();

        /// <summary>Set when a project fohlio_map.json exists but could not be read. The map then
        /// falls back to the built-in defaults, which carry no cost or currency columns — so a
        /// command must say so rather than quietly import without prices.</summary>
        [JsonIgnore]
        public string LoadError { get; set; }

        public static FohlioMap Load(Document doc)
        {
            string p = null;
            try
            {
                p = ProjectFile(doc, "fohlio_map.json");
                if (p != null && File.Exists(p))
                    return Parse(File.ReadAllText(p));
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FohlioMap load {p}: {ex.Message}");
                return new FohlioMap { LoadError = $"{p}: {ex.Message}" };
            }
            return new FohlioMap();
        }

        public static string ProjectFile(Document doc, string name)
        {
            string dir = Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(dir)) return null;
            return StingPaths.MetaFile(doc, "_BIM_COORD", name);
        }

        /// <summary>Resolve a mapped value for an element, honouring "$"-pseudo params.</summary>
        public static string ResolveValue(Document doc, Element el, string param)
        {
            if (string.IsNullOrEmpty(param)) return "";
            switch (param)
            {
                case "$Family": return ParameterHelpers.GetFamilyName(el) ?? "";
                case "$Type": return ParameterHelpers.GetFamilySymbolName(el) ?? "";
                case "$Category": return ParameterHelpers.GetCategoryName(el) ?? "";
                case "$Room":
                    try
                    {
                        var room = ParameterHelpers.GetRoomAtElement(doc, el);
                        if (room == null) return "";
                        string num = room.Number ?? "", name = room.Name ?? "";
                        return string.IsNullOrEmpty(num) ? name : $"{num} {name}".Trim();
                    }
                    catch { return ""; }
                // GetValueText, not GetString: FOHLIO_UNIT_COST_NR is NUMBER, and GetString
                // returns "" for anything but TEXT, so the Unit Cost column always exported blank.
                default: return ParameterHelpers.GetValueText(el, param);
            }
        }

        public static bool IsPseudo(string param) => !string.IsNullOrEmpty(param) && param.StartsWith("$");
    }

    // ── Tier 2 — REST transport (stub; CSV path stays the default) ──────────
    public class FohlioConnection
    {
        public string BaseUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";

        /// <summary>Loads the connection from _BIM_COORD/fohlio_connection.json (user-created,
        /// gitignored, never committed). Returns null when absent.</summary>
        public static FohlioConnection Load(Document doc)
        {
            try
            {
                string p = FohlioMap.ProjectFile(doc, "fohlio_connection.json");
                if (p != null && File.Exists(p))
                    return JsonConvert.DeserializeObject<FohlioConnection>(File.ReadAllText(p));
            }
            catch (Exception ex) { StingLog.Warn($"FohlioConnection load: {ex.Message}"); }
            return null;
        }
    }

    public class FohlioItem
    {
        public string Ref { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>Tier-2 transport contract. The CSV path does NOT use this; it exists so a
    /// future REST implementation drops in behind the same interface without touching the
    /// commands.</summary>
    public interface IFohlioTransport
    {
        bool TestConnection();
        List<FohlioItem> ListItems(string projectId);
        FohlioItem GetItem(string projectId, string itemRef);
        bool UpdateItem(string projectId, FohlioItem item);
    }

    /// <summary>
    /// REST implementation skeleton. Fohlio exposes a v2 REST API (API-key header).
    /// Intentionally a clean stub — the CSV exchange is the contractual deliverable
    /// (BEP: "use file/Add-in route now"), and this class has no callers.
    ///
    /// EVERY method throws. TestConnection() used to return true whenever BaseUrl and
    /// ApiKey were non-empty, without making any network call — a green that a typo,
    /// a revoked key or an unreachable host would all have passed. It was harmless only
    /// because nothing called it; the first person to wire a "Test connection" button
    /// would have inherited it. Wire the real HTTP calls here when the API docs and a
    /// key are available, and make TestConnection() actually connect.
    /// </summary>
    public class FohlioRestTransport : IFohlioTransport
    {
        private readonly FohlioConnection _conn;
        public FohlioRestTransport(FohlioConnection conn) { _conn = conn; }

        public bool TestConnection()
            // TODO C1-T2: GET {BaseUrl}/ping (or /projects) with header
            // "Authorization: Bearer {ApiKey}". Return true on 2xx, false otherwise.
            // Until then this must NOT return a value: a success that did no work is
            // indistinguishable from a real one, and that is the whole failure mode.
            => throw new NotImplementedException(
                "Fohlio REST connection test — Tier 2, not yet wired" +
                (string.IsNullOrEmpty(_conn?.BaseUrl) ? "" : $" (configured base URL: {_conn.BaseUrl})") +
                ". The contracted route is the CSV/XLSX exchange (Fohlio_Export / Fohlio_Import); " +
                "no API key is needed for it.");

        public List<FohlioItem> ListItems(string projectId)
            => throw new NotImplementedException("Fohlio REST list — Tier 2, not yet wired (use CSV import).");

        public FohlioItem GetItem(string projectId, string itemRef)
            => throw new NotImplementedException("Fohlio REST get — Tier 2, not yet wired.");

        public bool UpdateItem(string projectId, FohlioItem item)
            => throw new NotImplementedException("Fohlio REST update — Tier 2, not yet wired.");
    }
}
