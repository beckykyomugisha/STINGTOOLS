using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.Commands.Electrical.LoadDemand
{
    /// <summary>
    /// Phase 185 load / demand pack engine — pure math, no Revit API.
    ///
    /// Centralises four related calcs that the review flagged:
    ///
    /// <list type="bullet">
    /// <item><b>Diversity factor matrix</b> — per-discipline factors per
    /// BS 7671 App 1 + IEE OSG + NEC 220. Maps a circuit's load name to
    /// a category and returns the demand-to-connected ratio.</item>
    /// <item><b>Spare capacity gauge</b> — connected demand vs busbar
    /// rating per panel, with sector-specific targets (commercial 25%,
    /// industrial 30%, healthcare 35%) per IEC 60364-5-52 / CIBSE K.</item>
    /// <item><b>Harmonics-aware neutral sizing</b> — triplen-harmonic
    /// content per load category drives the neutral derating per BS 7671
    /// §523.6.3 + IEEE 519. IT/server panels with 50% triplens need
    /// 1.5× neutral; LED-only panels stay at 1.0×.</item>
    /// <item><b>Power factor correction sizing</b> — kVAR capacitor bank
    /// for target PF (0.95 default) at panel level, with utility-tariff
    /// payback estimate.</item>
    /// </list>
    ///
    /// All thresholds load from <c>STING_DIVERSITY_FACTORS.json</c>; failing
    /// load yields a hard-coded baseline so the engine remains usable.
    /// </summary>
    public static class LoadDemandEngine
    {
        private static DemandTables _cache;
        private static readonly object _lock = new object();

        public static DemandTables Tables()
        {
            lock (_lock)
            {
                if (_cache != null) return _cache;
                _cache = LoadTables();
                return _cache;
            }
        }

        public static void InvalidateCache() { lock (_lock) _cache = null; }

        private static DemandTables LoadTables()
        {
            var t = new DemandTables();
            try
            {
                string path = StingToolsApp.FindDataFile("STING_DIVERSITY_FACTORS.json");
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { t.SeedDefaults(); return t; }
                var root = JObject.Parse(File.ReadAllText(path));
                foreach (var f in root["factors"] as JArray ?? new JArray())
                {
                    t.Factors.Add(new DiversityRow
                    {
                        Category = f["category"]?.ToString() ?? "",
                        Patterns = (f["patterns"] as JArray)?.Select(x => (x.ToString() ?? "").ToLowerInvariant()).ToList() ?? new List<string>(),
                        Factor   = f["factor"]?.Value<double>() ?? 1.0
                    });
                }
                foreach (var h in root["harmonics"] as JArray ?? new JArray())
                {
                    t.Harmonics[(h["category"]?.ToString() ?? "").ToLowerInvariant()] =
                        new HarmonicRow
                        {
                            ThdH3Pct       = h["thdHarmonic3Pct"]?.Value<double>() ?? 25,
                            NeutralFactor  = h["neutralFactor"]?.Value<double>() ?? 1.0
                        };
                }
                t.PfcTargetPf      = root["pfcDefaults"]?["targetPfLagging"]?.Value<double>() ?? 0.95;
                t.PfcPresentPf     = root["pfcDefaults"]?["presentPfBaseline"]?.Value<double>() ?? 0.85;
                foreach (var kv in (root["pfcDefaults"]?["kvarPerKwAtPf"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    if (double.TryParse(kv.Name, out double pf))
                        t.KvarPerKwAtPf[pf] = kv.Value.Value<double>();
                foreach (var s in (root["spareTargetsPct"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                    t.SpareTargetsPct[s.Name] = s.Value.Value<double>();
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LoadDemand tables: {ex.Message}");
                t.SeedDefaults();
            }
            return t;
        }

        // ── Diversity classification ─────────────────────────────────────

        /// <summary>Classify a load name into a diversity category.</summary>
        public static DiversityRow Classify(string loadName)
        {
            var t = Tables();
            string n = (loadName ?? "").ToLowerInvariant();
            foreach (var row in t.Factors)
            {
                if (row.Patterns.Any(p => p == "*")) continue; // skip catch-all in first pass
                if (row.Patterns.Any(p => !string.IsNullOrEmpty(p) && n.Contains(p))) return row;
            }
            // Catch-all
            return t.Factors.FirstOrDefault(r => r.Patterns.Contains("*"))
                   ?? new DiversityRow { Category = "General", Factor = 0.85 };
        }

        /// <summary>Apply diversity to a list of (name, kw) pairs and return total demand.</summary>
        public static DiversitySummary ApplyDiversity(IEnumerable<(string loadName, double kw)> circuits)
        {
            var perCategory = new Dictionary<string, (double connected, double demand, double factor)>();
            double totalConnected = 0, totalDemand = 0;
            foreach (var (name, kw) in circuits)
            {
                if (kw <= 0) continue;
                var cat = Classify(name);
                totalConnected += kw;
                double demand = kw * cat.Factor;
                totalDemand += demand;
                if (perCategory.TryGetValue(cat.Category, out var t))
                    perCategory[cat.Category] = (t.connected + kw, t.demand + demand, cat.Factor);
                else
                    perCategory[cat.Category] = (kw, demand, cat.Factor);
            }
            return new DiversitySummary
            {
                TotalConnectedKw = totalConnected,
                TotalDemandKw    = totalDemand,
                BlendedFactor    = totalConnected > 0 ? totalDemand / totalConnected : 1.0,
                ByCategory       = perCategory.Select(kv => new DiversityCategoryRow
                                   {
                                       Category    = kv.Key,
                                       ConnectedKw = kv.Value.connected,
                                       DemandKw    = kv.Value.demand,
                                       Factor      = kv.Value.factor
                                   }).OrderByDescending(r => r.DemandKw).ToList()
            };
        }

        // ── Spare capacity ───────────────────────────────────────────────

        /// <summary>RAG status for connected demand vs busbar rating.</summary>
        public static SpareCapacityResult AssessSpareCapacity(double demandKw, double busbarRatingA,
            double voltageV, int phases, string sector = "Commercial")
        {
            var t = Tables();
            double targetPct = t.SpareTargetsPct.TryGetValue(sector, out double s) ? s : 25;
            double busbarKw = phases == 3
                ? busbarRatingA * voltageV * Math.Sqrt(3) / 1000.0
                : busbarRatingA * voltageV / 1000.0;
            double sparePct = busbarKw > 0 ? (1 - demandKw / busbarKw) * 100.0 : 0;
            string verdict = sparePct >= targetPct ? "GREEN"
                           : sparePct >= targetPct / 2 ? "AMBER" : "RED";
            return new SpareCapacityResult
            {
                DemandKw      = demandKw,
                BusbarKw      = busbarKw,
                SparePct      = sparePct,
                TargetPct     = targetPct,
                Sector        = sector,
                Verdict       = verdict
            };
        }

        // ── Harmonics-aware neutral sizing ───────────────────────────────

        /// <summary>
        /// Per BS 7671 §523.6.3 + IEEE 519 — when triplen-harmonic content
        /// exceeds ~33% the neutral can carry as much current as the
        /// phases. Returns a recommended neutral CSA expressed as a multiplier
        /// on the phase CSA. 1.0 = same as phase, 1.5 = oversized neutral.
        /// </summary>
        public static NeutralSizingResult AssessNeutral(string loadCategory, double phaseCsaMm2,
            double phaseCurrentA)
        {
            var t = Tables();
            string key = (loadCategory ?? "general").ToLowerInvariant();
            if (!t.Harmonics.TryGetValue(key, out var h))
                h = t.Harmonics.TryGetValue("general", out var g) ? g : new HarmonicRow { ThdH3Pct = 25, NeutralFactor = 1.0 };

            double neutralCurrent = phaseCurrentA * h.NeutralFactor;
            double neutralCsa = phaseCsaMm2 * h.NeutralFactor;
            return new NeutralSizingResult
            {
                LoadCategory          = loadCategory,
                ThdHarmonic3Pct       = h.ThdH3Pct,
                NeutralFactor         = h.NeutralFactor,
                RecommendedNeutralCsa = Math.Ceiling(neutralCsa),
                NeutralCurrentA       = neutralCurrent,
                Notes                 = h.NeutralFactor > 1.05
                                        ? "Oversize neutral — triplen harmonics > 33%"
                                        : (h.NeutralFactor < 0.7
                                           ? "Half-size neutral acceptable (e.g. VFD-fed motor)"
                                           : "Full-size neutral")
            };
        }

        // ── Power factor correction ──────────────────────────────────────

        /// <summary>
        /// Capacitor-bank kVAR for target PF. Uses the kVAR-per-kW table
        /// at present PF and target PF; linear interp between tabulated
        /// PF values. Returns kVAR + estimated annual savings at a default
        /// utility rate (configurable by caller).
        /// </summary>
        public static PfcResult SizeCapacitorBank(double demandKw, double presentPf = 0,
            double targetPf = 0, double utilityKvarPenaltyGbp = 5.0)
        {
            var t = Tables();
            if (presentPf <= 0) presentPf = t.PfcPresentPf;
            if (targetPf  <= 0) targetPf  = t.PfcTargetPf;
            if (presentPf >= targetPf)
                return new PfcResult { Required = false, Notes = $"Present PF {presentPf:0.00} already meets {targetPf:0.00} target" };

            double presentRatio = LookupKvarRatio(t.KvarPerKwAtPf, presentPf);
            double targetRatio  = LookupKvarRatio(t.KvarPerKwAtPf, targetPf);
            double kvar = demandKw * (presentRatio - targetRatio);
            // Annual saving estimate: kVAR penalty × 8760 × utilisation 0.6.
            double annualSaving = kvar * utilityKvarPenaltyGbp * 8760 * 0.6 / 1000.0;

            return new PfcResult
            {
                Required           = kvar > 0,
                CapacitorKvar      = Math.Ceiling(kvar),
                PresentPf          = presentPf,
                TargetPf           = targetPf,
                AnnualSavingGbp    = annualSaving,
                Notes              = $"Install {Math.Ceiling(kvar):0} kVAR cap-bank to lift PF {presentPf:0.00} → {targetPf:0.00}"
            };
        }

        private static double LookupKvarRatio(IDictionary<double, double> table, double pf)
        {
            if (table == null || table.Count == 0) return 0;
            // Find bracketing PFs; linear interp.
            var sorted = table.OrderBy(kv => kv.Key).ToList();
            if (pf <= sorted.First().Key) return sorted.First().Value;
            if (pf >= sorted.Last().Key)  return sorted.Last().Value;
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var a = sorted[i]; var b = sorted[i + 1];
                if (pf < a.Key || pf > b.Key) continue;
                double f = (pf - a.Key) / (b.Key - a.Key);
                return a.Value + f * (b.Value - a.Value);
            }
            return 0;
        }
    }

    // ── DTOs ────────────────────────────────────────────────────────────

    public class DemandTables
    {
        public List<DiversityRow> Factors { get; } = new();
        public Dictionary<string, HarmonicRow> Harmonics { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<double, double> KvarPerKwAtPf { get; } = new();
        public Dictionary<string, double> SpareTargetsPct { get; } = new(StringComparer.OrdinalIgnoreCase);
        public double PfcTargetPf { get; set; } = 0.95;
        public double PfcPresentPf { get; set; } = 0.85;

        public void SeedDefaults()
        {
            Factors.Add(new DiversityRow { Category = "Lighting",     Factor = 0.90, Patterns = new() { "lighting", "ltg" } });
            Factors.Add(new DiversityRow { Category = "Small Power",  Factor = 0.75, Patterns = new() { "socket", "ring", "spur" } });
            Factors.Add(new DiversityRow { Category = "HVAC",         Factor = 1.00, Patterns = new() { "hvac", "ahu", "fcu" } });
            Factors.Add(new DiversityRow { Category = "General",      Factor = 0.85, Patterns = new() { "*" } });
            Harmonics["lighting"]    = new HarmonicRow { ThdH3Pct = 30, NeutralFactor = 1.0 };
            Harmonics["it / server"] = new HarmonicRow { ThdH3Pct = 50, NeutralFactor = 1.5 };
            Harmonics["hvac"]        = new HarmonicRow { ThdH3Pct = 15, NeutralFactor = 0.5 };
            Harmonics["general"]     = new HarmonicRow { ThdH3Pct = 25, NeutralFactor = 1.0 };
            KvarPerKwAtPf[0.70] = 0.70; KvarPerKwAtPf[0.85] = 0.31; KvarPerKwAtPf[0.95] = 0.10; KvarPerKwAtPf[1.00] = 0.0;
            SpareTargetsPct["Commercial"] = 25; SpareTargetsPct["Industrial"] = 30; SpareTargetsPct["Healthcare"] = 35;
        }
    }

    public class DiversityRow { public string Category; public List<string> Patterns = new(); public double Factor; }
    public class HarmonicRow { public double ThdH3Pct; public double NeutralFactor; }

    public class DiversitySummary
    {
        public double TotalConnectedKw, TotalDemandKw, BlendedFactor;
        public List<DiversityCategoryRow> ByCategory = new();
    }
    public class DiversityCategoryRow { public string Category; public double ConnectedKw, DemandKw, Factor; }

    public class SpareCapacityResult
    {
        public double DemandKw, BusbarKw, SparePct, TargetPct;
        public string Sector, Verdict;
    }

    public class NeutralSizingResult
    {
        public string LoadCategory, Notes;
        public double ThdHarmonic3Pct, NeutralFactor, RecommendedNeutralCsa, NeutralCurrentA;
    }

    public class PfcResult
    {
        public bool Required;
        public double CapacitorKvar, PresentPf, TargetPf, AnnualSavingGbp;
        public string Notes;
    }
}
