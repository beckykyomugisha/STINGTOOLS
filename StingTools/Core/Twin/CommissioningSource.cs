// ══════════════════════════════════════════════════════════════════════════
//  CommissioningSource.cs — Phase 195 (KUT lifecycle, max automation).
//
//  Single source of truth for "what does the BMS say is live?". Every consumer
//  of Niagara commissioning data (KUT_ValuationFromBms, the lifecycle reconcile,
//  any future PayCert auto-feed) goes through Resolve() instead of calling
//  NiagaraJsonClient.FetchPoints directly, so they all share one behaviour:
//
//    1. Station reachable  → fetch live points, PERSIST a snapshot to
//       <project>/_BIM_COORD/twin/last_station_points.json (with capturedUtc),
//       return Source=Live.
//    2. Station down / no connection but a snapshot exists → return the CACHED
//       points + their capturedUtc, Source=Cached. A valuation can still run
//       off the last good read (with the staleness surfaced to the user).
//    3. Neither → Source=None.
//
//  HOST-FREE — no Autodesk.Revit references — so it stays unit-testable and the
//  network/file behaviour is decoupled from the command shell.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Twin
{
    public enum CommissioningSourceKind { None, Live, Cached }

    /// <summary>The points the valuation should use, plus their provenance.</summary>
    public sealed class CommissioningSnapshot
    {
        public Dictionary<string, NiagaraPoint> Points = new Dictionary<string, NiagaraPoint>(StringComparer.OrdinalIgnoreCase);
        public CommissioningSourceKind Source = CommissioningSourceKind.None;
        public DateTime AsOfUtc;

        /// <summary>One-line provenance for the report + CSV header, e.g.
        /// "live (captured 2026-06-21 14:33Z)" or "CACHED 2026-06-20 09:12Z (station unreachable)".</summary>
        public string Detail
        {
            get
            {
                switch (Source)
                {
                    case CommissioningSourceKind.Live:
                        return $"live (captured {AsOfUtc:yyyy-MM-dd HH:mm}Z)";
                    case CommissioningSourceKind.Cached:
                        return $"CACHED {AsOfUtc:yyyy-MM-dd HH:mm}Z (station unreachable — last good read)";
                    default:
                        return "no BMS data";
                }
            }
        }
    }

    public static class CommissioningSource
    {
        private const string RelPath = "twin/last_station_points.json";

        /// <summary>
        /// Resolve the commissioning points: live first, cached on live failure.
        /// <paramref name="projectDir"/> is the directory that contains _BIM_COORD.
        /// <paramref name="conn"/> may be null (then only the cache is consulted).
        /// </summary>
        public static CommissioningSnapshot Resolve(string projectDir, NiagaraConnection conn)
        {
            // 1. Try live.
            if (conn != null && !string.IsNullOrEmpty(conn.BaseUrl))
            {
                var live = NiagaraJsonClient.FetchPoints(conn);
                if (live != null)
                {
                    var now = DateTime.UtcNow;
                    Persist(projectDir, live, now);
                    return new CommissioningSnapshot { Points = live, Source = CommissioningSourceKind.Live, AsOfUtc = now };
                }
                StingLog.Warn("CommissioningSource: live fetch failed — falling back to cached snapshot.");
            }

            // 2. Fall back to the persisted snapshot.
            var cached = LoadCache(projectDir);
            if (cached != null) return cached;

            // 3. Nothing.
            return new CommissioningSnapshot { Source = CommissioningSourceKind.None };
        }

        // ── snapshot persistence (host-free, never throws into the caller) ──

        private sealed class Wire
        {
            [JsonProperty("capturedUtc")] public DateTime CapturedUtc { get; set; }
            [JsonProperty("points")] public Dictionary<string, WirePoint> Points { get; set; }
                = new Dictionary<string, WirePoint>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class WirePoint
        {
            [JsonProperty("status")] public string Status { get; set; } = "";
            [JsonProperty("hasValue")] public bool HasValue { get; set; }
        }

        private static void Persist(string projectDir, Dictionary<string, NiagaraPoint> pts, DateTime capturedUtc)
        {
            if (string.IsNullOrEmpty(projectDir) || pts == null) return;
            try
            {
                string path = Path.Combine(projectDir, "_BIM_COORD", RelPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var wire = new Wire { CapturedUtc = capturedUtc };
                foreach (var kv in pts)
                    wire.Points[kv.Key] = new WirePoint { Status = kv.Value?.Status ?? "", HasValue = kv.Value?.HasValue ?? false };
                File.WriteAllText(path, JsonConvert.SerializeObject(wire, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"CommissioningSource persist: {ex.Message}"); }
        }

        private static CommissioningSnapshot LoadCache(string projectDir)
        {
            if (string.IsNullOrEmpty(projectDir)) return null;
            try
            {
                string path = Path.Combine(projectDir, "_BIM_COORD", RelPath);
                if (!File.Exists(path)) return null;
                var wire = JsonConvert.DeserializeObject<Wire>(File.ReadAllText(path));
                if (wire?.Points == null || wire.Points.Count == 0) return null;

                var pts = new Dictionary<string, NiagaraPoint>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in wire.Points)
                    pts[kv.Key] = new NiagaraPoint { Status = kv.Value?.Status ?? "", HasValue = kv.Value?.HasValue ?? false };

                return new CommissioningSnapshot
                {
                    Points = pts,
                    Source = CommissioningSourceKind.Cached,
                    AsOfUtc = wire.CapturedUtc
                };
            }
            catch (Exception ex) { StingLog.Warn($"CommissioningSource load cache: {ex.Message}"); return null; }
        }
    }
}
