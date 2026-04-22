// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/ClashTriageEngine.cs — S6.1 (N-G5).
//
// Weighted-rule clash triage. Consumes the existing ClashSession
// output (see StingTools/Clash/ClashSession.cs) and scores each clash
// on six factors, then returns a top-20 critical list rather than a
// 500-clash dump. Weights live in Data_ClashTriage.json so the
// scoring can tune per project without recompiling.
//
// Factors (all normalised 0..1):
//   f1 severity    structural=1.0, services-vs-services=0.7, svc-vs-arch=0.4, non-critical=0.1
//   f2 schedule    element phase already cast / installed = 1.0, future = 0.3
//   f3 repair cost USD cost estimate normalised into 0..1 via triage config cap
//   f4 recurrence  history of same element pair clashing across runs (requires sidecar state)
//   f5 clearance   penetration depth vs element dimension 0..1
//   f6 user dismiss rate — past decisions tag clash signature as low-priority
//
// Weighted sum = score. Top-20 by score populates the BCC dashboard.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingTools.V6
{
    public sealed class ClashTriageConfig
    {
        public double[] Weights { get; set; } = { 0.30, 0.15, 0.25, 0.10, 0.15, 0.05 };
        public double RepairCostCapUsd { get; set; } = 5_000.0;
        public int    TopN            { get; set; } = 20;
    }

    public sealed class ScoredClash
    {
        public string ClashId { get; set; } = string.Empty;
        public long ElementA { get; set; }
        public long ElementB { get; set; }
        public double Score { get; set; }
        public double[] FactorScores { get; set; } = new double[6];
        public string Category { get; set; } = string.Empty;
        public string Rationale { get; set; } = string.Empty;
    }

    public static class ClashTriageEngine
    {
        private static ClashTriageConfig _cfg;
        private static readonly object _lk = new object();

        public static ClashTriageConfig Config
        {
            get { lock (_lk) { return _cfg ??= LoadConfig(); } }
            set { lock (_lk) { _cfg = value; } }
        }

        private static ClashTriageConfig LoadConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(ClashTriageEngine).Assembly.Location) ?? "";
                string path = Path.Combine(dir, "Data", "Routing", "STING_CLASH_TRIAGE.json");
                if (!File.Exists(path)) return new ClashTriageConfig();
                var j = JObject.Parse(File.ReadAllText(path));
                var cfg = new ClashTriageConfig();
                var w = (JArray)j["weights"];
                if (w != null && w.Count == 6)
                    cfg.Weights = w.Select(t => (double)t).ToArray();
                cfg.RepairCostCapUsd = (double?)j["repair_cost_cap_usd"] ?? cfg.RepairCostCapUsd;
                cfg.TopN             = (int?)j["top_n"] ?? cfg.TopN;
                return cfg;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ClashTriageEngine.LoadConfig failed: {ex.Message}");
                return new ClashTriageConfig();
            }
        }

        /// <summary>
        /// Score and rank a set of clashes. Input is agnostic: each
        /// clash is represented as a (clashId, elementA-catName,
        /// elementB-catName, penetrationMm, estCostUsd, phaseInstalled,
        /// recurrenceCount, dismissCount) tuple.
        /// </summary>
        public static List<ScoredClash> Triage(IEnumerable<ClashInput> inputs)
        {
            if (inputs == null) return new List<ScoredClash>();
            var cfg = Config;
            var scored = new List<ScoredClash>();
            foreach (var c in inputs)
            {
                var f1 = SeverityFactor(c.CategoryA, c.CategoryB);
                var f2 = c.PhaseInstalled ? 1.0 : 0.3;
                var f3 = Math.Min(1.0, (c.EstCostUsd ?? 0.0) / cfg.RepairCostCapUsd);
                var f4 = Math.Min(1.0, c.RecurrenceCount / 5.0);
                var f5 = Math.Min(1.0, (c.PenetrationMm ?? 0.0) / 200.0);
                var f6 = 1.0 - Math.Min(1.0, c.DismissCount / 3.0);
                var factors = new[] { f1, f2, f3, f4, f5, f6 };
                double score = 0;
                for (int i = 0; i < factors.Length; i++) score += cfg.Weights[i] * factors[i];
                scored.Add(new ScoredClash
                {
                    ClashId = c.ClashId,
                    ElementA = c.ElementAId,
                    ElementB = c.ElementBId,
                    Score = score,
                    FactorScores = factors,
                    Category = ClassifyCategory(c.CategoryA, c.CategoryB),
                    Rationale = $"sev {f1:F2} sched {f2:F2} cost {f3:F2} recur {f4:F2} pen {f5:F2} notDismiss {f6:F2}",
                });
            }
            return scored.OrderByDescending(s => s.Score).Take(cfg.TopN).ToList();
        }

        private static double SeverityFactor(string a, string b)
        {
            bool aStr = IsStructural(a), bStr = IsStructural(b);
            bool aSvc = IsServices(a),   bSvc = IsServices(b);
            bool aArch = !aStr && !aSvc, bArch = !bStr && !bSvc;
            if (aStr || bStr) return 1.0;
            if (aSvc && bSvc) return 0.7;
            if ((aSvc && bArch) || (bSvc && aArch)) return 0.4;
            return 0.1;
        }

        private static string ClassifyCategory(string a, string b)
            => (IsStructural(a) || IsStructural(b)) ? "STRUCTURAL"
               : (IsServices(a) && IsServices(b))  ? "SERVICES"
               : "NON_CRITICAL";

        private static bool IsStructural(string cat)
            => cat != null && (cat.StartsWith("Structural", StringComparison.OrdinalIgnoreCase)
                            || cat.Contains("Column") || cat.Contains("Beam") || cat.Contains("Wall"));

        private static bool IsServices(string cat)
            => cat != null && (cat.Contains("Duct") || cat.Contains("Pipe") || cat.Contains("Conduit")
                            || cat.Contains("Cable") || cat.Contains("Electrical") || cat.Contains("Mechanical"));
    }

    public sealed class ClashInput
    {
        public string ClashId { get; set; } = string.Empty;
        public long ElementAId { get; set; }
        public long ElementBId { get; set; }
        public string CategoryA { get; set; } = string.Empty;
        public string CategoryB { get; set; } = string.Empty;
        public double? PenetrationMm { get; set; }
        public double? EstCostUsd { get; set; }
        public bool PhaseInstalled { get; set; }
        public int RecurrenceCount { get; set; }
        public int DismissCount { get; set; }
    }
}
