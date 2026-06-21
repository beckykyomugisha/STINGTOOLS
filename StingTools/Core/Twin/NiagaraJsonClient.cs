// ══════════════════════════════════════════════════════════════════════════
//  NiagaraJsonClient.cs — Phase H3 (KUT lifecycle, max automation).
//
//  Minimal read-only HTTP client for a Tridium Niagara station's JSON Toolkit /
//  oBIX export. GETs a points feed and returns deviceId → (status, hasValue) so
//  KutValuationFromBmsCommand can decide which priced, monitorable assets are live
//  on the BMS (= commissioned). Read-only — never writes to the station.
//
//  Connection config (gitignored, never committed):
//    <project>/_BIM_COORD/niagara_connection.json
//    { "baseUrl": "http://station:8080", "pointsPath": "/obix/.../points",
//      "apiKey": "…" | "username":"…","password":"…" }
//
//  NETWORK CODE — not exercised in the dev sandbox (no live station). Built clean
//  against the documented HttpClient API; verify against a real Niagara station
//  before production use.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Core.Twin
{
    public sealed class NiagaraConnection
    {
        public string BaseUrl = "";
        public string PointsPath = "/obix";   // station-specific points feed
        public string ApiKey = "";
        public string Username = "";
        public string Password = "";

        public static NiagaraConnection Load(string projectDir)
        {
            try
            {
                if (string.IsNullOrEmpty(projectDir)) return null;
                string p = Path.Combine(projectDir, "_BIM_COORD", "niagara_connection.json");
                if (!File.Exists(p)) return null;
                var o = JObject.Parse(File.ReadAllText(p));
                return new NiagaraConnection
                {
                    BaseUrl = ((string)o["baseUrl"] ?? "").TrimEnd('/'),
                    PointsPath = (string)o["pointsPath"] ?? "/obix",
                    ApiKey = (string)o["apiKey"] ?? "",
                    Username = (string)o["username"] ?? "",
                    Password = (string)o["password"] ?? "",
                };
            }
            catch (Exception ex) { StingLog.Warn($"Niagara connection load: {ex.Message}"); return null; }
        }
    }

    public sealed class NiagaraPoint { public string Status = ""; public bool HasValue; }

    public static class NiagaraJsonClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>Pure parse of a Niagara/oBIX points-feed JSON into deviceId →
        /// point. Tolerant of the common shapes: a bare array, { points:[…] }, or a
        /// dict keyed by id. Each point carries id/name/deviceId + status +
        /// present_value/out/value. Host-free so it is unit-testable.</summary>
        public static Dictionary<string, NiagaraPoint> ParsePoints(string json)
        {
            var d = new Dictionary<string, NiagaraPoint>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return d;
            try
            {
                var tok = JToken.Parse(json);
                JArray arr = tok as JArray ?? (tok as JObject)?["points"] as JArray;
                if (arr != null) { foreach (var t in arr) AddPoint(d, t as JObject); }
                else if (tok is JObject obj)
                    foreach (var pr in obj.Properties()) AddPoint(d, pr.Value as JObject, pr.Name);
            }
            catch (Exception ex) { StingLog.Warn($"Niagara parse: {ex.Message}"); }
            return d;
        }

        private static void AddPoint(Dictionary<string, NiagaraPoint> d, JObject o, string keyFallback = null)
        {
            if (o == null) return;
            string id = ((string)o["deviceId"] ?? (string)o["id"] ?? (string)o["name"] ?? keyFallback ?? "").Trim();
            if (id.Length == 0) return;
            var val = o["present_value"] ?? o["presentValue"] ?? o["out"] ?? o["value"] ?? o["val"];
            // Type-safe presence test — never (string)-cast a JArray (throws). oBIX
            // <real val="…"/> arrives as an object with a "val" child; primitives arrive
            // as JValue; an absent/null value means a configured-but-dead point.
            bool hasVal;
            if (val == null || val.Type == JTokenType.Null) hasVal = false;
            else if (val.Type == JTokenType.Object) { var inner = val["val"]; hasVal = inner != null && inner.Type != JTokenType.Null && !string.IsNullOrWhiteSpace(inner.ToString()); }
            else hasVal = !string.IsNullOrWhiteSpace(val.ToString());
            d[id] = new NiagaraPoint { Status = (string)o["status"] ?? "", HasValue = hasVal };
        }

        /// <summary>GET the station points feed (read-only). Returns null on any
        /// transport failure (logged) so the caller falls back to "no live data".</summary>
        public static Dictionary<string, NiagaraPoint> FetchPoints(NiagaraConnection c)
        {
            if (c == null || string.IsNullOrEmpty(c.BaseUrl)) return null;
            try
            {
                string url = c.BaseUrl + (c.PointsPath.StartsWith("/") ? c.PointsPath : "/" + c.PointsPath);
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    if (!string.IsNullOrEmpty(c.ApiKey))
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", c.ApiKey);
                    else if (!string.IsNullOrEmpty(c.Username))
                    {
                        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.Username}:{c.Password}"));
                        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                    }
                    var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode)
                    {
                        StingLog.Warn($"Niagara fetch HTTP {(int)resp.StatusCode}");
                        return null;
                    }
                    string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return ParsePoints(body);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Niagara fetch: {ex.Message}"); return null; }
        }
    }
}
