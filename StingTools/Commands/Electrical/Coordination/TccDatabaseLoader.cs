using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Coordination
{
    /// <summary>
    /// Time-current curve database POCO. Each entry holds a breaker's
    /// clearing characteristic. The minimal form is one anchor (10× In) plus
    /// the rated fault-level range, used as a linear-ramp fallback. When the
    /// JSON entry supplies a <c>points</c> array, <see cref="TccEntry.ClearingTimeMs"/>
    /// log-log interpolates between them — the standard way TCC curves are
    /// read off manufacturer datasheets.
    /// </summary>
    public class TccDatabase
    {
        [JsonProperty("defaultClearingMs")]
        public double DefaultClearingMs { get; set; } = 100;
        [JsonProperty("entries")]
        public List<TccEntry> Entries { get; set; } = new List<TccEntry>();
        [JsonProperty("curves")]
        public List<TccCurve> Curves { get; set; } = new List<TccCurve>();

        /// <summary>Resolve by exact device label (case-insensitive). Returns null if no match.</summary>
        public TccEntry Resolve(string ratingLabel)
        {
            if (string.IsNullOrEmpty(ratingLabel)) return null;
            return Entries.FirstOrDefault(e =>
                string.Equals(e.DeviceLabel, ratingLabel, StringComparison.OrdinalIgnoreCase));
        }

        public TccEntry Resolve(string ratingLabel, int poles) => Resolve(ratingLabel);

        public static TccDatabase BuildDefault() => new TccDatabase
        {
            DefaultClearingMs = 100,
            Entries = new List<TccEntry>
            {
                new TccEntry { DeviceLabel="6A",   Type="MCB-B", ClearingMs_At_10xIn=10, MinFaultKa=0.05, MaxFaultKa=6  },
                new TccEntry { DeviceLabel="10A",  Type="MCB-B", ClearingMs_At_10xIn=10, MinFaultKa=0.05, MaxFaultKa=6  },
                new TccEntry { DeviceLabel="16A",  Type="MCB-C", ClearingMs_At_10xIn=10, MinFaultKa=0.05, MaxFaultKa=6  },
                new TccEntry { DeviceLabel="20A",  Type="MCB-C", ClearingMs_At_10xIn=10, MinFaultKa=0.05, MaxFaultKa=10 },
                new TccEntry { DeviceLabel="32A",  Type="MCB-C", ClearingMs_At_10xIn=10, MinFaultKa=0.05, MaxFaultKa=10 },
                new TccEntry { DeviceLabel="63A",  Type="MCCB",  ClearingMs_At_10xIn=20, MinFaultKa=0.1,  MaxFaultKa=25 },
                new TccEntry { DeviceLabel="100A", Type="MCCB",  ClearingMs_At_10xIn=20, MinFaultKa=0.1,  MaxFaultKa=36 },
                new TccEntry { DeviceLabel="200A", Type="ACB",   ClearingMs_At_10xIn=50, MinFaultKa=0.1,  MaxFaultKa=65 },
                new TccEntry { DeviceLabel="400A", Type="ACB",   ClearingMs_At_10xIn=50, MinFaultKa=0.1,  MaxFaultKa=85 },
            }
        };
    }

    public class TccEntry
    {
        [JsonProperty("deviceLabel")]        public string DeviceLabel        { get; set; } = "";
        [JsonProperty("type")]               public string Type               { get; set; } = "";
        [JsonProperty("clearingMs_At_10xIn")]public double ClearingMs_At_10xIn{ get; set; }
        [JsonProperty("minFaultKa")]         public double MinFaultKa         { get; set; }
        [JsonProperty("maxFaultKa")]         public double MaxFaultKa         { get; set; }

        /// <summary>
        /// Optional point list (faultKa, clearingMs) supplying a real
        /// time-current curve. When populated, <see cref="ClearingTimeMs"/>
        /// log-log interpolates between adjacent points (the standard way TCC
        /// curves are read off manufacturer datasheets — both axes are log
        /// scaled). Missing → falls back to the linear ramp.
        /// </summary>
        [JsonProperty("points")] public List<TccPoint> Points { get; set; } = new List<TccPoint>();

        /// <summary>
        /// Clearing time at a given fault current. Uses log-log interpolation
        /// against <see cref="Points"/> when ≥ 2 points are tabulated;
        /// otherwise applies a linear ramp between the rated-floor (10× In) and
        /// long-time pickup ceiling (300 ms).
        /// </summary>
        public double ClearingTimeMs(double faultKa)
        {
            if (faultKa <= 0) return ClearingMs_At_10xIn;
            var pts = Points;
            if (pts != null && pts.Count >= 2)
            {
                var sorted = pts.Where(p => p.FaultKa > 0 && p.ClearingMs > 0)
                                .OrderBy(p => p.FaultKa).ToList();
                if (sorted.Count >= 2)
                {
                    if (faultKa <= sorted[0].FaultKa) return sorted[0].ClearingMs;
                    if (faultKa >= sorted[sorted.Count - 1].FaultKa) return sorted[sorted.Count - 1].ClearingMs;
                    for (int i = 0; i < sorted.Count - 1; i++)
                    {
                        var a = sorted[i]; var b = sorted[i + 1];
                        if (faultKa < a.FaultKa || faultKa > b.FaultKa) continue;
                        // log-log interp: log(t) is linear in log(I)
                        double lx = Math.Log10(faultKa);
                        double la = Math.Log10(a.FaultKa);
                        double lb = Math.Log10(b.FaultKa);
                        double f  = (lx - la) / (lb - la);
                        double lt = Math.Log10(a.ClearingMs) + f * (Math.Log10(b.ClearingMs) - Math.Log10(a.ClearingMs));
                        return Math.Pow(10, lt);
                    }
                }
            }
            // Linear-ramp fallback (Phase 178 baseline behaviour).
            if (MaxFaultKa <= MinFaultKa) return ClearingMs_At_10xIn;
            double ratio = Math.Min(1.0, Math.Max(0.0,
                (faultKa - MinFaultKa) / (MaxFaultKa - MinFaultKa)));
            return Math.Max(ClearingMs_At_10xIn, 300.0 * (1.0 - ratio));
        }
    }

    public class TccCurve
    {
        [JsonProperty("deviceLabel")] public string DeviceLabel { get; set; } = "";
        [JsonProperty("points")]      public List<TccPoint> Points { get; set; } = new List<TccPoint>();
    }
    public class TccPoint
    {
        [JsonProperty("faultKa")]   public double FaultKa   { get; set; }
        [JsonProperty("clearingMs")]public double ClearingMs{ get; set; }
    }

    public static class TccDatabaseLoader
    {
        private static TccDatabase _cache;
        private static DateTime _cacheTime;
        private static readonly object _lock = new object();

        public static TccDatabase Load(string dataPath)
        {
            lock (_lock)
            {
                if (_cache != null && (DateTime.Now - _cacheTime).TotalMinutes < 5) return _cache;
                try
                {
                    string path = string.IsNullOrEmpty(dataPath)
                        ? StingToolsApp.FindDataFile("STING_TCC_DATABASE.json")
                        : dataPath;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        _cache = JsonConvert.DeserializeObject<TccDatabase>(File.ReadAllText(path))
                                 ?? TccDatabase.BuildDefault();
                    }
                    else { _cache = TccDatabase.BuildDefault(); }
                    _cacheTime = DateTime.Now;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TccDatabaseLoader.Load: {ex.Message}");
                    _cache = TccDatabase.BuildDefault();
                }
                return _cache;
            }
        }

        public static void InvalidateCache() { lock (_lock) _cache = null; }

        /// <summary>Convenience entry point used by ArcFlashCommand.</summary>
        public static TccEntry Resolve(string ratingLabel, double faultKa)
        {
            var db = Load(null);
            return db.Entries.FirstOrDefault(e =>
                string.Equals(e.DeviceLabel, ratingLabel, StringComparison.OrdinalIgnoreCase)
                && faultKa >= e.MinFaultKa && faultKa <= e.MaxFaultKa);
        }
    }
}
