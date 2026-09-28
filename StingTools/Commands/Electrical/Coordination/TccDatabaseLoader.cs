using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Coordination
{
    /// <summary>
    /// Protective-device database POCO. An entry names a device (rating label +
    /// type) and its breaking capacity. Its time-current behaviour is NOT stored:
    /// MCB entries (type MCB-B / MCB-C / MCB-D) are modelled as the generic
    /// IEC 60898-1 band (<see cref="IecMcbBands"/>); MCCB / ACB entries have no
    /// generic characteristic and report "no curve data". A <see cref="TccCurve"/>
    /// may carry manufacturer points for plotting only — a single line is not a
    /// band, so it is never used to claim selectivity.
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

        /// <summary>
        /// Band for a device label. Uses the database entry's type when the label is
        /// listed, otherwise parses the label itself ("C16", "16A C", "250A MCCB").
        /// A curve letter taken from the entry's (generic default) type rather than the
        /// label is flagged <see cref="DeviceBand.CurveAssumed"/>.
        /// </summary>
        public DeviceBand ResolveBand(string deviceLabel)
        {
            if (string.IsNullOrWhiteSpace(deviceLabel)) return null;
            var e = Resolve(deviceLabel.Trim());
            return e != null
                ? IecMcbBands.FromDatabaseEntry(e.DeviceLabel, e.Type, e.CurveConfirmed)
                : IecMcbBands.Parse(deviceLabel);
        }

        /// <summary>Convenience alias — returns null when no curve is registered for the label.</summary>
        public TccCurve FindCurvePoints(string deviceLabel) => ResolveCurve(deviceLabel);

        public static TccDatabase BuildDefault()
        {
            var entries = new List<TccEntry>
            {
                // Curve letters are generic defaults — confirm per project.
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

            // No default curves. The per-label curves that used to live here were
            // invented (a 6 A breaker "clearing" 50 A in 290 ms fits no IEC 60898
            // characteristic). Manufacturer curves may be supplied in the JSON.
            var curves = new List<TccCurve>();

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
        /// <summary>Set true in a project copy once the entry's curve letter has been
        /// checked against the installed device; until then it is a generic default and
        /// results say "(curve assumed)".</summary>
        [JsonProperty("curveConfirmed")]     public bool   CurveConfirmed     { get; set; }

        /// <summary>IEC 60898-1 band for this entry (see <see cref="IecMcbBands"/>).</summary>
        public DeviceBand ToBand() => IecMcbBands.FromDatabaseEntry(DeviceLabel, Type, CurveConfirmed);

        /// <summary>
        /// Maximum clearing time (ms) at the given fault level, for display and
        /// clearing-time lookups:
        ///   • manufacturer <paramref name="curve"/> with ≥ 2 points → log-log interpolation;
        ///   • MCB entry (MCB-B/C/D) → upper edge of the IEC 60898-1 band, 0 where no
        ///     trip is guaranteed (callers treat ≤ 0 as "unknown");
        ///   • MCCB / ACB entry → 0 ("no curve data").
        ///   • untyped entry → LEGACY synthetic linear ramp. Kept ONLY because
        ///     BS7671ComplianceEngine builds an untyped fallback entry and its adiabatic
        ///     check would otherwise change behaviour; that ramp is not a device
        ///     characteristic (ROADMAP ELEC-4) and nothing in Coordination/ or ArcFlash/
        ///     relies on it.
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

            // ── Generic IEC 60898-1 band (MCB) / no data (MCCB, ACB) ────────
            if (!string.IsNullOrWhiteSpace(Type))
            {
                var band = ToBand();
                if (!band.HasBand) return 0;
                double tMax = band.MaxClearTimeS(faultKa * 1000.0);
                return double.IsInfinity(tMax) || double.IsNaN(tMax) ? 0 : tMax * 1000.0;
            }

            // ── LEGACY synthetic ramp — untyped entries only (see summary) ──
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
