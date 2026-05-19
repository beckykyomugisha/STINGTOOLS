// ══════════════════════════════════════════════════════════════════════════
//  BcisHttpRateProvider.cs — Live rates from BCIS Online (or any
//  HTTP-accessible price book).
//
//  Configuration (project_config.json):
//    BCIS_BASE_URL  = "https://service.bcis.co.uk/api"
//    BCIS_API_KEY   = "..."           (Authorization: Bearer <key>)
//    BCIS_TTL_MIN   = 1440            cache TTL in minutes (default 24h)
//
//  Caches lookups under <project>/_bim_manager/rate_cache/bcis_*.json
//  so offline operation falls back to last-good values. Priority 50 —
//  sits between project rate cards (40) and COBie type map (75).
//
//  The actual BCIS schema is proprietary and varies by tier; this
//  implementation targets a generic GET /rates?category=&unit= shape
//  that returns { unitRate, currency, unit }. Adapter classes can wrap
//  Spon's, RICS BCIS, or any other rate-book API behind the same
//  IRateProvider contract.
//
//  P8 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.BOQ.Rates.Providers
{
    public sealed class BcisHttpRateProvider : IRateProvider, IDisposable
    {
        public string Id => "bcis-http";
        public int Priority => 50;
        public bool RequiresNetwork => true;

        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly int _ttlMinutes;
        private readonly string _cacheDir;

        // In-memory hot cache — survives the BOQ build but not the
        // Revit session. Disk cache persists across sessions.
        private readonly ConcurrentDictionary<string, (RateLookup lookup, DateTime fetchedUtc)> _hot
            = new ConcurrentDictionary<string, (RateLookup, DateTime)>();

        public BcisHttpRateProvider(string baseUrl, string apiKey, int ttlMinutes, string cacheDir)
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
            _apiKey = apiKey ?? "";
            _ttlMinutes = ttlMinutes > 0 ? ttlMinutes : 1440;
            _cacheDir = cacheDir;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            if (!string.IsNullOrEmpty(_apiKey))
                _http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public RateLookup Resolve(RateRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.CategoryName)) return null;
            if (string.IsNullOrEmpty(_baseUrl)) return null;
            string cacheKey = $"{req.CategoryName}|{req.Unit}|{req.LocationCode}";

            // Hot cache — never expires within a single build.
            if (_hot.TryGetValue(cacheKey, out var hot)) return hot.lookup;

            // Disk cache — TTL-bounded.
            try
            {
                var disk = TryReadDiskCache(cacheKey);
                if (disk != null) { _hot[cacheKey] = (disk, DateTime.UtcNow); return disk; }
            }
            catch (Exception ex) { StingLog.Warn($"BcisHttpRateProvider disk cache: {ex.Message}"); }

            // Network call — fail soft, return null so other providers
            // get their chance.
            try
            {
                string url = $"{_baseUrl}/rates?category={Uri.EscapeDataString(req.CategoryName)}" +
                             $"&unit={Uri.EscapeDataString(req.Unit ?? "")}" +
                             $"&location={Uri.EscapeDataString(req.LocationCode ?? "")}";
                var resp = _http.GetAsync(url).GetAwaiter().GetResult();
                if (!resp.IsSuccessStatusCode) return null;
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var json = JObject.Parse(body);
                double rate = json.Value<double?>("unitRate") ?? 0;
                if (rate <= 0) return null;
                var lookup = new RateLookup
                {
                    UnitRate = rate,
                    CurrencyCode = json.Value<string>("currency") ?? "GBP",
                    Unit = json.Value<string>("unit") ?? (req.Unit ?? "each"),
                    SourceId = Id,
                    Confidence = 50,
                    Provenance = $"BCIS live ({DateTime.UtcNow:yyyy-MM-dd})",
                    MatchedKey = req.CategoryName
                };
                _hot[cacheKey] = (lookup, DateTime.UtcNow);
                TryWriteDiskCache(cacheKey, lookup);
                return lookup;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"BcisHttpRateProvider HTTP {req.CategoryName}: {ex.Message}");
                return null;
            }
        }

        private RateLookup TryReadDiskCache(string key)
        {
            if (string.IsNullOrEmpty(_cacheDir) || !Directory.Exists(_cacheDir)) return null;
            string path = Path.Combine(_cacheDir, $"bcis_{SafeKey(key)}.json");
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if ((DateTime.UtcNow - info.LastWriteTimeUtc).TotalMinutes > _ttlMinutes) return null;
            var raw = File.ReadAllText(path);
            var json = JObject.Parse(raw);
            return new RateLookup
            {
                UnitRate = json.Value<double?>("unitRate") ?? 0,
                CurrencyCode = json.Value<string>("currency") ?? "GBP",
                Unit = json.Value<string>("unit") ?? "each",
                SourceId = Id,
                Confidence = 50,
                Provenance = $"BCIS cached ({info.LastWriteTimeUtc:yyyy-MM-dd})",
                MatchedKey = key
            };
        }

        private void TryWriteDiskCache(string key, RateLookup lookup)
        {
            try
            {
                if (string.IsNullOrEmpty(_cacheDir)) return;
                Directory.CreateDirectory(_cacheDir);
                string path = Path.Combine(_cacheDir, $"bcis_{SafeKey(key)}.json");
                var json = new JObject
                {
                    ["unitRate"] = lookup.UnitRate,
                    ["currency"] = lookup.CurrencyCode,
                    ["unit"]     = lookup.Unit
                };
                File.WriteAllText(path, json.ToString());
            }
            catch (Exception ex) { StingLog.Warn($"BcisHttpRateProvider write cache: {ex.Message}"); }
        }

        private static string SafeKey(string key)
        {
            var sb = new StringBuilder(key.Length);
            foreach (char c in key)
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }

        public void Dispose() => _http?.Dispose();
    }
}
