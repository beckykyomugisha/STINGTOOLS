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
    /// the fault-level range it is rated for. When a matching <see cref="TccCurve"/>
    /// is available the engine uses log-log interpolation across the curve points;
    /// otherwise it falls back to the legacy linear ramp.
    /// </summary>
    public class TccDatabase
    {
        [JsonProperty("defaultClearingMs")]
        public double DefaultClearingMs { get; set; } = 100;
        [JsonProperty("entries")]
        public List<TccEntry> Entries { get; set; } = new List<TccEntry>();
        [JsonProperty("curves")]
        public List<TccCurve> Curves { get; set; } = new List<TccCurve>();

        /// <summary>Resolve entry by exact device label (case-insensitive). Returns null if no match.</summary>
        public TccEntry Resolve(string ratingLabel)
        {
            if (string.IsNullOrEmpty(ratingLabel)) return null;
            return Entries.FirstOrDefault(e =>
                string.Equals(e.DeviceLabel, ratingLabel, StringComparison.OrdinalIgnoreCase));
        }

        public TccEntry Resolve(string ratingLabel, int poles) => Resolve(ratingLabel);

        /// <summary>Resolve TCC curve by device label (case-insensitive). Returns null if not found.</summary>
        public TccCurve ResolveCurve(string deviceLabel)
        {
            if (string.IsNullOrEmpty(deviceLabel)) return null;
            return Curves.FirstOrDefault(c =>
                string.Equals(c.DeviceLabel, deviceLabel, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Convenience alias — returns null when no curve is registered for the label.</summary>
        public TccCurve FindCurvePoints(string deviceLabel) => ResolveCurve(deviceLabel);

        public static TccDatabase BuildDefault()
        {
            var entries = new List<TccEntry>
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
            };

            var curves = new List<TccCurve>
            {
                new TccCurve
                {
                    DeviceLabel = "6A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.05, ClearingMs=290 },
                        new TccPoint { FaultKa=0.1,  ClearingMs=200 },
                        new TccPoint { FaultKa=0.5,  ClearingMs=80  },
                        new TccPoint { FaultKa=1,    ClearingMs=30  },
                        new TccPoint { FaultKa=3,    ClearingMs=10  },
                        new TccPoint { FaultKa=6,    ClearingMs=10  },
                    }
                },
                new TccCurve
                {
                    DeviceLabel = "10A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.05, ClearingMs=290 },
                        new TccPoint { FaultKa=0.1,  ClearingMs=200 },
                        new TccPoint { FaultKa=0.5,  ClearingMs=80  },
                        new TccPoint { FaultKa=1,    ClearingMs=25  },
                        new TccPoint { FaultKa=3,    ClearingMs=10  },
                        new TccPoint { FaultKa=6,    ClearingMs=10  },
                    }
                },
                new TccCurve
                {
                    DeviceLabel = "16A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.05, ClearingMs=290 },
                        new TccPoint { FaultKa=0.2,  ClearingMs=150 },
                        new TccPoint { FaultKa=1,    ClearingMs=40  },
                        new TccPoint { FaultKa=3,    ClearingMs=10  },
                        new TccPoint { FaultKa=6,    ClearingMs=10  },
                    }
                },
                new TccCurve
                {
                    DeviceLabel = "20A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.05, ClearingMs=290 },
                        new TccPoint { FaultKa=0.2,  ClearingMs=150 },
                        new TccPoint { FaultKa=1,    ClearingMs=35  },
                        new TccPoint { FaultKa=3,    ClearingMs=10  },
                        new TccPoint { FaultKa=10,   ClearingMs=10  },
                    }
                },
                new TccCurve
                {
                    DeviceLabel = "32A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.05, ClearingMs=290 },
                        new TccPoint { FaultKa=0.2,  ClearingMs=150 },
                        new TccPoint { FaultKa=1,    ClearingMs=30  },
                        new TccPoint { FaultKa=5,    ClearingMs=10  },
                        new TccPoint { FaultKa=10,   ClearingMs=10  },
                    }
                },
                new TccCurve
                {
                    DeviceLabel = "63A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.1,  ClearingMs=500 },
                        new TccPoint { FaultKa=0.5,  ClearingMs=200 },
                        new TccPoint { FaultKa=2,    ClearingMs=50  },
                        new TccPoint { FaultKa=10,   ClearingMs=20  },
                        new TccPoint { FaultKa=25,   ClearingMs=20  },
                    }
                },
                new TccCurve
                {
                    DeviceLabel = "100A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.1,  ClearingMs=500 },
                        new TccPoint { FaultKa=0.5,  ClearingMs=200 },
                        new TccPoint { FaultKa=2,    ClearingMs=50  },
                        new TccPoint { FaultKa=15,   ClearingMs=20  },
                        new TccPoint { FaultKa=36,   ClearingMs=20  },
                    }
                },
                new TccCurve
                {
                    DeviceLabel = "200A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.1,  ClearingMs=800 },
                        new TccPoint { FaultKa=1,    ClearingMs=300 },
                        new TccPoint { FaultKa=5,    ClearingMs=100 },
                        new TccPoint { FaultKa=20,   ClearingMs=50  },
                        new TccPoint { FaultKa=65,   ClearingMs=50  },
                    }
                },
                new TccCurve
                {
                    DeviceLabel = "400A",
                    Points = new List<TccPoint>
                    {
                        new TccPoint { FaultKa=0.1,  ClearingMs=1000 },
                        new TccPoint { FaultKa=1,    ClearingMs=400  },
                        new TccPoint { FaultKa=5,    ClearingMs=150  },
                        new TccPoint { FaultKa=20,   ClearingMs=80   },
                        new TccPoint { FaultKa=85,   ClearingMs=50   },
                    }
                },
            };

            return new TccDatabase
            {
                DefaultClearingMs = 100,
                Entries = entries,
                Curves = curves
            };
        }
    }

    public class TccEntry
    {
        [JsonProperty("deviceLabel")]        public string DeviceLabel        { get; set; } = "";
        [JsonProperty("type")]               public string Type               { get; set; } = "";
        [JsonProperty("clearingMs_At_10xIn")]public double ClearingMs_At_10xIn{ get; set; }
        [JsonProperty("minFaultKa")]         public double MinFaultKa         { get; set; }
        [JsonProperty("maxFaultKa")]         public double MaxFaultKa         { get; set; }

        /// <summary>
        /// Returns the clearing time in milliseconds at the given fault level.
        /// When a <paramref name="curve"/> with at least two points is supplied,
        /// log-log interpolation is used for accuracy. When the fault level falls
        /// outside the curve's range the nearest endpoint value is returned
        /// (flat extrapolation — conservative). When no curve is supplied the
        /// legacy linear ramp between 300 ms and <see cref="ClearingMs_At_10xIn"/>
        /// is used as a fallback.
        /// </summary>
        public double ClearingTimeMs(double faultKa, TccCurve curve = null)
        {
            // ── Log-log interpolation when curve data is available ──────────
            if (curve != null && curve.Points != null && curve.Points.Count >= 2)
            {
                var pts = curve.Points;

                // Clamp to endpoints (flat extrapolation)
                if (faultKa <= pts[0].FaultKa)
                    return pts[0].ClearingMs;
                if (faultKa >= pts[pts.Count - 1].FaultKa)
                    return pts[pts.Count - 1].ClearingMs;

                // Find bracketing pair
                TccPoint p1 = pts[0], p2 = pts[1];
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    if (pts[i].FaultKa <= faultKa && faultKa <= pts[i + 1].FaultKa)
                    {
                        p1 = pts[i];
                        p2 = pts[i + 1];
                        break;
                    }
                }

                // Guard against zero or negative values before taking logarithms
                double logF  = Math.Log(Math.Max(faultKa,    1e-9));
                double logF1 = Math.Log(Math.Max(p1.FaultKa, 1e-9));
                double logF2 = Math.Log(Math.Max(p2.FaultKa, 1e-9));
                double logT1 = Math.Log(Math.Max(p1.ClearingMs, 1e-9));
                double logT2 = Math.Log(Math.Max(p2.ClearingMs, 1e-9));

                double dLogF = logF2 - logF1;
                // Coincident x-values — return first point's value
                if (Math.Abs(dLogF) < 1e-12)
                    return p1.ClearingMs;

                double logT = logT1 + (logF - logF1) / dLogF * (logT2 - logT1);
                return Math.Round(Math.Exp(logT), 2);
            }

            // ── Legacy linear ramp fallback ─────────────────────────────────
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
