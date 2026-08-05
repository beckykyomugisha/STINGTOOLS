// ══════════════════════════════════════════════════════════════════════════
//  EvmCalculator.cs — Earned Value Management metrics (PMI standard).
//
//  The QS's monthly forecasting tool. Given:
//    BCWS (Planned value)    = cost-loaded schedule at this date
//    BCWP (Earned value)     = % complete × baseline budget
//    ACWP (Actual cost)      = imported actuals from accounts
//
//  We compute:
//    CV  = BCWP − ACWP                 (cost variance, GBP)
//    SV  = BCWP − BCWS                 (schedule variance, GBP)
//    CPI = BCWP / ACWP                 (cost performance index)
//    SPI = BCWP / BCWS                 (schedule performance index)
//    EAC = BAC / CPI                   (estimate at completion)
//    ETC = EAC − ACWP                  (estimate to complete)
//    VAC = BAC − EAC                   (variance at completion)
//    TCPI = (BAC − BCWP) / (BAC − ACWP) (to-complete performance index)
//
//  BAC (budget at completion) = total contract value at baseline.
//
//  P5.3 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.BIMManager;

namespace StingTools.Core.Evm
{
    public class EvmPeriod
    {
        /// <summary>Period end date (typically last day of month).</summary>
        public DateTime PeriodEnd { get; set; } = DateTime.UtcNow;
        public string PeriodLabel { get; set; } = "";

        // ── Inputs ──────────────────────────────────────────────────
        public double Bac { get; set; }     // Budget at Completion (£)
        public double Bcws { get; set; }    // Planned Value (£)
        public double Bcwp { get; set; }    // Earned Value (£)
        public double Acwp { get; set; }    // Actual Cost (£)

        // ── Derived ─────────────────────────────────────────────────
        public double Cv => Math.Round(Bcwp - Acwp, 2);
        public double Sv => Math.Round(Bcwp - Bcws, 2);
        public double Cpi => Acwp > 0 ? Math.Round(Bcwp / Acwp, 4) : 0;
        public double Spi => Bcws > 0 ? Math.Round(Bcwp / Bcws, 4) : 0;
        public double Eac => Cpi > 0 ? Math.Round(Bac / Cpi, 2) : 0;
        public double Etc => Math.Round(Eac - Acwp, 2);
        public double Vac => Math.Round(Bac - Eac, 2);
        public double Tcpi
        {
            get
            {
                double denom = Bac - Acwp;
                if (Math.Abs(denom) < 0.01) return 0;
                return Math.Round((Bac - Bcwp) / denom, 4);
            }
        }

        public string CostHealth => Cpi >= 1.0 ? "Green" : Cpi >= 0.95 ? "Amber" : "Red";
        public string ScheduleHealth => Spi >= 1.0 ? "Green" : Spi >= 0.95 ? "Amber" : "Red";
    }

    public class EvmReport
    {
        public string ProjectName { get; set; } = "";
        public string ContractRef { get; set; } = "";
        public string Currency { get; set; } = "GBP";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public List<EvmPeriod> Periods { get; set; } = new List<EvmPeriod>();

        public EvmPeriod Latest => Periods.OrderByDescending(p => p.PeriodEnd).FirstOrDefault();
    }

    internal static class EvmCalculator
    {
        private static readonly JsonSerializerSettings _json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            Culture = CultureInfo.InvariantCulture
        };

        /// <summary>Build a new EVM period from raw inputs.</summary>
        public static EvmPeriod Compute(double bac, double bcws, double bcwp, double acwp,
            DateTime periodEnd, string label = null)
        {
            return new EvmPeriod
            {
                PeriodEnd = periodEnd,
                PeriodLabel = label ?? periodEnd.ToString("yyyy-MM"),
                Bac = bac,
                Bcws = bcws,
                Bcwp = bcwp,
                Acwp = acwp
            };
        }

        /// <summary>
        /// Import actuals from a CSV with columns Date,Section,Amount.
        /// Returns the total ACWP up to and including <paramref name="asOf"/>.
        /// </summary>
        public static double ImportActualsToDate(string csvPath, DateTime asOf)
        {
            if (string.IsNullOrEmpty(csvPath) || !File.Exists(csvPath)) return 0;
            double total = 0;
            try
            {
                bool headerSeen = false;
                foreach (string raw in File.ReadAllLines(csvPath))
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                    var cols = StingToolsApp.ParseCsvLine(raw);
                    if (cols == null || cols.Length < 3) continue;
                    if (!headerSeen)
                    {
                        headerSeen = true;
                        if (!double.TryParse(cols[2], NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                            continue;
                    }
                    if (!DateTime.TryParse(cols[0], CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out DateTime d)) continue;
                    if (d > asOf) continue;
                    if (!double.TryParse(cols[2], NumberStyles.Any, CultureInfo.InvariantCulture,
                            out double amt)) continue;
                    total += amt;
                }
            }
            catch (Exception ex) { StingLog.Warn($"EvmCalculator.ImportActualsToDate: {ex.Message}"); }
            return Math.Round(total, 2);
        }

        public static string Save(Document doc, EvmReport report)
        {
            string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "evm");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir,
                $"evm_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(report, _json));
            return path;
        }

        public static EvmReport Load(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<EvmReport>(File.ReadAllText(path), _json); }
            catch (Exception ex) { StingLog.Warn($"EvmCalculator.Load: {ex.Message}"); return null; }
        }

        public static List<string> ListReports(Document doc)
        {
            try
            {
                string dir = Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "evm");
                if (!Directory.Exists(dir)) return new List<string>();
                return Directory.EnumerateFiles(dir, "evm_*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
            }
            catch { return new List<string>(); }
        }
    }
}
