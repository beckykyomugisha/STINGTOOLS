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
//  HOST-FREE — no Autodesk.Revit references, and it builds no project paths: the
//  caller resolves the snapshot file through StingPaths and hands it in. That keeps
//  the network/file behaviour decoupled from the command shell.
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
        /// <summary>
        /// Resolve the commissioning points: live first, cached on live failure.
        /// <paramref name="snapshotPath"/> is the ALREADY-RESOLVED cache file path (the
        /// caller holds the Document and asks StingPaths); it may be null, in which case
        /// nothing is persisted and only a live read can succeed.
        /// <paramref name="conn"/> may be null, in which case only the cache is consulted.
        /// </summary>
        public static CommissioningSnapshot Resolve(string snapshotPath, NiagaraConnection conn)
        {
            // 1. Try live.
            if (conn != null && !string.IsNullOrEmpty(conn.BaseUrl))
            {
                // FetchPoints returns null for EVERY unsuccessful read: a transport error,
                // a body that is not JSON, a JSON error envelope, or a feed whose entries
                // were all unreadable. A malformed feed used to arrive here as a non-null
                // EMPTY dictionary, which took this branch, reported Live, and then wrote
                // the empty result over the cached snapshot.
                var live = NiagaraJsonClient.FetchPoints(conn);
                if (live != null)
                {
                    var now = DateTime.UtcNow;
                    Persist(snapshotPath, live, now);
                    return new CommissioningSnapshot { Points = live, Source = CommissioningSourceKind.Live, AsOfUtc = now };
                }
                StingLog.Warn("CommissioningSource: live fetch failed — falling back to cached snapshot.");
            }

            // 2. Fall back to the persisted snapshot.
            var cached = LoadCache(snapshotPath);
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

        /// <summary>Write the snapshot that a later outage will fall back to.
        ///
        /// Never trades a usable snapshot for an unusable one. <see cref="LoadCache"/>
        /// rejects a snapshot holding zero points, so writing an empty read over a good one
        /// does not "update" the cache — it DELETES the fallback that exists precisely for
        /// the station being unreachable, turning a recoverable outage into data loss.
        /// A genuinely empty live read is still reported as Live with zero points; it just
        /// does not get to destroy the last good read on its way past.</summary>
        private static void Persist(string path, Dictionary<string, NiagaraPoint> pts, DateTime capturedUtc)
        {
            if (string.IsNullOrEmpty(path) || pts == null) return;
            if (pts.Count == 0 && HasUsableSnapshot(path))
            {
                StingLog.Warn("CommissioningSource: the live read returned zero points; keeping the " +
                              "existing snapshot rather than overwriting the last good read.");
                return;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var wire = new Wire { CapturedUtc = capturedUtc };
                foreach (var kv in pts)
                    wire.Points[kv.Key] = new WirePoint { Status = kv.Value?.Status ?? "", HasValue = kv.Value?.HasValue ?? false };
                File.WriteAllText(path, JsonConvert.SerializeObject(wire, Formatting.Indented));
            }
            catch (Exception ex) { StingLog.Warn($"CommissioningSource persist: {ex.Message}"); }
        }

        /// <summary>Whether a snapshot exists that LoadCache would actually use. Asked
        /// before overwriting, so "there is a file" is never mistaken for "there is a
        /// fallback" — a zero-point snapshot is a file and not a fallback.</summary>
        private static bool HasUsableSnapshot(string path) => LoadCache(path) != null;

        private static CommissioningSnapshot LoadCache(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
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
