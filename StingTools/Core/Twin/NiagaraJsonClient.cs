// ══════════════════════════════════════════════════════════════════════════
//  NiagaraJsonClient.cs — Phase H3 (KUT lifecycle, max automation).
//
//  Minimal read-only HTTP client for a Tridium Niagara station's JSON Toolkit /
//  oBIX export. GETs a points feed and returns deviceId → (status, hasValue) so
//  KutValuationFromBmsCommand can decide which priced, monitorable assets are live
//  on the BMS (= commissioned). Read-only — never writes to the station.
//
//  Connection config (gitignored, never committed), resolved by the CALLER through
//  StingPaths so this class builds no project paths:
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

        /// <summary>Read the station connection from an ALREADY-RESOLVED path. The caller
        /// holds the Document and resolves through StingPaths; this class stays host-free
        /// and builds no project paths of its own.</summary>
        public static NiagaraConnection Load(string configPath)
        {
            try
            {
                if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return null;
                var o = JObject.Parse(File.ReadAllText(configPath));
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

    public static class NiagaraJsonClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>Parse a Niagara/oBIX points feed into deviceId → point. The shape
        /// handling lives in <see cref="NiagaraPointParser"/>, which carries no logging so
        /// it can be linked into the Revit-free test projects; this wrapper adds the
        /// logging and keeps the dictionary-returning signature its callers use.
        ///
        /// Returns NULL when the body cannot be trusted as a points feed — the same signal
        /// <see cref="FetchPoints"/> already uses for a transport failure, so a caller
        /// cannot mistake it for a successful empty read.
        ///
        /// This used to return <c>r.Points</c> unconditionally: it LOGGED the parse failure
        /// and then handed back the empty dictionary anyway. That empty dictionary is not
        /// null, so CommissioningSource took the live branch, reported "live (captured …)",
        /// and wrote the empty result over the cached snapshot — a fabricated 0%
        /// commissioned presented as a live reading, with the recovery cache destroyed.
        /// KUT_ValuationFromBms carries that percentage toward payment certification.</summary>
        public static Dictionary<string, NiagaraPoint> ParsePoints(string json)
        {
            var r = NiagaraPointParser.Parse(json);
            if (r.Failed)
            {
                StingLog.Warn($"Niagara parse: {r.Error} — treating as a FAILED read, not an empty station.");
                return null;
            }
            if (!r.RecognisedShape)
            {
                // A JSON error envelope, or a bare scalar. Valid JSON, not a points feed.
                StingLog.Warn("Niagara parse: the body was valid JSON but not a points feed " +
                              "(no array, no points wrapper, no id-keyed point objects) — treating as a FAILED read.");
                return null;
            }
            // A feed whose id field we do not read yields an EMPTY dictionary, which reads
            // downstream as "no asset is commissioned" — the same as a station with nothing
            // on it. Say which one it was.
            if (r.SkippedNoId > 0)
                StingLog.Warn($"Niagara parse: {r.SkippedNoId} entr(ies) carried no deviceId/id/name and were skipped.");
            if (r.Unusable)
            {
                StingLog.Warn($"Niagara parse: {r.CandidateEntries} entr(ies) were present and none was understood " +
                              "— treating as a FAILED read rather than an empty station.");
                return null;
            }
            return r.Points;
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
