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
    // Tier 1 (shipped): CSV/XLSX exchange (Fohlio_Export / Fohlio_Import).
    // Tier 2 (stub):    IFohlioTransport REST skeleton behind a "Test connection"
    //                   gate; base URL + key from _BIM_COORD/fohlio_connection.json
    //                   (never hardcoded, never committed).
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One column in the Fohlio ↔ STING/Revit mapping.</summary>
    public class FohlioColumn
    {
        /// <summary>Header used in the Fohlio import/export sheet.</summary>
        public string Header { get; set; } = "";
        /// <summary>STING/Revit parameter, or a "$"-pseudo (Family / Type / Category / Room).</summary>
        public string Param { get; set; } = "";
        /// <summary>True if Fohlio_Import may write this value back into the model.</summary>
        public bool WriteBack { get; set; }
    }

    public class FohlioMap
    {
        /// <summary>FF&E categories exchanged with Fohlio.</summary>
        public List<string> Categories { get; set; } = new List<string>
        {
            "Furniture", "Furniture Systems", "Casework", "Plumbing Fixtures",
            "Lighting Fixtures", "Specialty Equipment"
        };

        public List<FohlioColumn> Columns { get; set; } = new List<FohlioColumn>
        {
            new FohlioColumn { Header = "Item Tag",      Param = "ASS_TAG_1_TXT" },
            new FohlioColumn { Header = "Category",      Param = "$Category" },
            new FohlioColumn { Header = "Product",       Param = "$Type" },
            new FohlioColumn { Header = "Family",        Param = "$Family" },
            new FohlioColumn { Header = "Manufacturer",  Param = "ASS_MANUFACTURER_TXT", WriteBack = true },
            new FohlioColumn { Header = "Model",         Param = "ASS_MODEL_REF_TXT",    WriteBack = true },
            new FohlioColumn { Header = "Room",          Param = "$Room" },
            new FohlioColumn { Header = "Fohlio Ref",    Param = "FOHLIO_REF_TXT",       WriteBack = true },
        };

        public static FohlioMap Load(Document doc)
        {
            var map = new FohlioMap();
            try
            {
                string p = ProjectFile(doc, "fohlio_map.json");
                if (p != null && File.Exists(p))
                {
                    var o = JsonConvert.DeserializeObject<FohlioMap>(File.ReadAllText(p));
                    if (o != null) map = o;
                }
            }
            catch (Exception ex) { StingLog.Warn($"FohlioMap load: {ex.Message}"); }
            return map;
        }

        public static string ProjectFile(Document doc, string name)
        {
            string dir = Path.GetDirectoryName(doc?.PathName ?? "");
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, "_BIM_COORD", name);
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
                default: return ParameterHelpers.GetString(el, param);
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
    /// Intentionally a clean stub — the CSV exchange is the contractual deliverable;
    /// wire the HTTP calls here when the API docs + a key are available, gated behind
    /// the "Test connection" button so CSV stays the default path.
    /// </summary>
    public class FohlioRestTransport : IFohlioTransport
    {
        private readonly FohlioConnection _conn;
        public FohlioRestTransport(FohlioConnection conn) { _conn = conn; }

        public bool TestConnection()
        {
            // TODO C1-T2: GET {BaseUrl}/ping (or /projects) with header
            // "Authorization: Bearer {ApiKey}". Return true on 2xx.
            return _conn != null && !string.IsNullOrEmpty(_conn.BaseUrl) && !string.IsNullOrEmpty(_conn.ApiKey);
        }

        public List<FohlioItem> ListItems(string projectId)
            => throw new NotImplementedException("Fohlio REST list — Tier 2, not yet wired (use CSV import).");

        public FohlioItem GetItem(string projectId, string itemRef)
            => throw new NotImplementedException("Fohlio REST get — Tier 2, not yet wired.");

        public bool UpdateItem(string projectId, FohlioItem item)
            => throw new NotImplementedException("Fohlio REST update — Tier 2, not yet wired.");
    }
}
