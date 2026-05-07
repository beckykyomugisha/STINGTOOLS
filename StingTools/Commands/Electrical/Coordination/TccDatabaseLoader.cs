using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Coordination
{
    /// <summary>
    /// Time-current curve database POCO. Each entry is a coarse summary of a
    /// breaker's clearing characteristic — single anchor point at 10× In plus
    /// the fault-level range it is rated for. Production hardening could add
    /// the full <see cref="TccCurve"/> point list for log-log interpolation.
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
        /// Linear ramp between an instantaneous-trip floor (the table's 10× In
        /// value) and a long-time pickup ceiling (300 ms) over the rated fault
        /// range. Production hardening should swap this for log-log
        /// interpolation against <see cref="TccCurve.Points"/>.
        /// </summary>
        public double ClearingTimeMs(double faultKa)
        {
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
