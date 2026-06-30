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
    // EvmPeriod (pure EVM math) lives in EvmPeriod.cs so it is unit-tested without
    // the Revit API. PM-1.

    public class EvmReport
    {
        public string ProjectName { get; set; } = "";
        public string ContractRef { get; set; } = "";
        public string Currency { get; set; } = "UGX";   // project currency — set from BOQDocument.Currency on create
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

        /// <summary>
        /// B.5 — cumulative ACWP across EVERY actuals_*.csv under <paramref name="dir"/>,
        /// to <paramref name="asOf"/>. Files with identical content are counted ONCE
        /// (SHA-256 dedupe) so re-dropping the same export — or a copy of it — can't
        /// double-count. Returns the merged cumulative; outputs how many unique files
        /// were read and how many duplicates were skipped so the caller can warn.
        /// </summary>
        public static double ImportAllActualsToDate(string dir, DateTime asOf,
            out int filesRead, out int duplicatesSkipped)
        {
            filesRead = 0; duplicatesSkipped = 0;
            double total = 0;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(dir, "actuals_*.csv").OrderBy(x => x))
            {
                try
                {
                    string hash = FileHash(f);
                    if (!seen.Add(hash)) { duplicatesSkipped++; continue; }  // identical content already counted
                    total += ImportActualsToDate(f, asOf);
                    filesRead++;
                }
                catch (Exception ex) { StingLog.Warn($"ImportAllActualsToDate({Path.GetFileName(f)}): {ex.Message}"); }
            }
            return Math.Round(total, 2);
        }

        private static string FileHash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path))
                return Convert.ToHexString(sha.ComputeHash(fs));
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
