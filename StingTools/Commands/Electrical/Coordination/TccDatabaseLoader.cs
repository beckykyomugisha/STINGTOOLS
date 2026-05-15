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

        /// <summary>Gap 14 — resolve full TCC curve for log-log interpolation.</summary>
        public TccCurve ResolveCurve(string ratingLabel)
        {
            if (string.IsNullOrEmpty(ratingLabel) || Curves == null) return null;
            return Curves.FirstOrDefault(c =>
                string.Equals(c.DeviceLabel, ratingLabel, StringComparison.OrdinalIgnoreCase));
        }

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
        /// Gap 14 — Log-log interpolation from TccCurve.Points when available;
        /// falls back to the previous linear-ramp formula for entries without curve data.
        /// Log-log interpolation matches the TCC graphical presentation used by
        /// manufacturers (x = log10(kA), y = log10(ms)), which is linear on a
        /// log-log plot.
        /// </summary>
        public double ClearingTimeMs(double faultKa, TccCurve curve = null)
        {
            // Prefer full log-log curve if supplied
            if (curve?.Points != null && curve.Points.Count >= 2)
                return LogLogInterpolate(curve.Points, faultKa);

            // Fallback: linear ramp (original behaviour)
            if (MaxFaultKa <= MinFaultKa) return ClearingMs_At_10xIn;
            double ratio = Math.Min(1.0, Math.Max(0.0,
                (faultKa - MinFaultKa) / (MaxFaultKa - MinFaultKa)));
            return Math.Max(ClearingMs_At_10xIn, 300.0 * (1.0 - ratio));
        }

        /// <summary>
        /// Log-log interpolation between TccPoint pairs.
        /// Points must be sorted ascending by FaultKa.
        /// </summary>
        public static double LogLogInterpolate(IList<TccPoint> pts, double faultKa)
        {
            if (pts == null || pts.Count == 0) return 100;
            if (faultKa <= pts[0].FaultKa) return pts[0].ClearingMs;
            if (faultKa >= pts[pts.Count - 1].FaultKa) return pts[pts.Count - 1].ClearingMs;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var lo = pts[i]; var hi = pts[i + 1];
                if (faultKa >= lo.FaultKa && faultKa <= hi.FaultKa)
                {
                    // log-log interpolation: ln(y) = ln(y0) + t*(ln(y1)-ln(y0))
                    // where t = (ln(x)-ln(x0))/(ln(x1)-ln(x0))
                    if (lo.FaultKa <= 0 || hi.FaultKa <= 0 ||
                        lo.ClearingMs <= 0 || hi.ClearingMs <= 0)
                        return lo.ClearingMs; // guard against zeros
                    double logX0 = Math.Log(lo.FaultKa);
                    double logX1 = Math.Log(hi.FaultKa);
                    double logX  = Math.Log(faultKa);
                    double t = (logX - logX0) / (logX1 - logX0);
                    double logY = Math.Log(lo.ClearingMs)
                                + t * (Math.Log(hi.ClearingMs) - Math.Log(lo.ClearingMs));
                    return Math.Exp(logY);
                }
            }
            return pts[pts.Count - 1].ClearingMs;
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
