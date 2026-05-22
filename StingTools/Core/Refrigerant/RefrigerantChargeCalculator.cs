// StingTools — Refrigerant additional-charge calculator (Phase 187g).
//
// Vendor data books publish per-OD per-metre charge factors for the
// liquid line. Field-charge = factory-charge + Σ(length_per_OD ×
// charge_per_meter) + offset (if total system length is below a
// vendor-defined threshold).
//
// Reads STING_REFRIG_CHARGE_TABLES.json (per-vendor / per-refrigerant)
// with per-project override at <project>/_BIM_COORD/refrig_charge_tables.json.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Refrigerant
{
    public class RefrigerantChargeTable
    {
        public string Id              { get; set; } = "";
        public string VendorSeriesId  { get; set; } = "";
        public string RefrigerantId   { get; set; } = "";
        public string Label           { get; set; } = "";
        /// <summary>Liquid-line charge factor in kg per metre of pipe,
        /// keyed by OD in millimetres (e.g. 9.52, 12.70).</summary>
        public Dictionary<double, double> PerOdKgPerM { get; set; } = new();
        /// <summary>Vendor short-system offset: if total system length is
        /// less than ThresholdM, subtract OffsetKg from the additional
        /// charge. Captures vendors that ship more refrigerant than the
        /// short-system actually needs.</summary>
        public double ThresholdM      { get; set; }
        public double OffsetKg        { get; set; }
        public string Source          { get; set; } = "";
    }

    public class RefrigerantChargeLibrary
    {
        public Dictionary<string, RefrigerantChargeTable> ById { get; }
            = new Dictionary<string, RefrigerantChargeTable>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Find a charge table by (vendorSeriesId, refrigerantId).
        /// First exact-match wins; null when no match.</summary>
        public RefrigerantChargeTable Find(string vendorSeriesId, string refrigerantId)
        {
            return ById.Values.FirstOrDefault(t =>
                string.Equals(t.VendorSeriesId, vendorSeriesId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.RefrigerantId, refrigerantId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class RefrigerantChargeRegistry
    {
        public const string DataFileName = "STING_REFRIG_CHARGE_TABLES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/refrig_charge_tables.json";

        private static readonly ConcurrentDictionary<string, RefrigerantChargeLibrary> _cache
            = new ConcurrentDictionary<string, RefrigerantChargeLibrary>(StringComparer.OrdinalIgnoreCase);

        public static RefrigerantChargeLibrary Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        public static void Reload()              => _cache.Clear();
        public static void Reload(Document doc)  => _cache.TryRemove(doc?.PathName ?? "<no-doc>", out _);

        private static RefrigerantChargeLibrary Load(Document doc)
        {
            var lib = new RefrigerantChargeLibrary();
            try
            {
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                    Apply(JObject.Parse(File.ReadAllText(basePath)), lib);
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                        Apply(JObject.Parse(File.ReadAllText(projPath)), lib);
                }
            }
            catch (Exception ex)
            { StingTools.Core.StingLog.Error("RefrigerantChargeRegistry.Load", ex); }
            return lib;
        }

        private static void Apply(JObject j, RefrigerantChargeLibrary lib)
        {
            var arr = j["tables"] as JArray;
            if (arr == null) return;
            foreach (var t in arr.OfType<JObject>())
            {
                var table = new RefrigerantChargeTable
                {
                    Id             = (string)t["id"] ?? "",
                    VendorSeriesId = (string)t["vendorSeriesId"] ?? "",
                    RefrigerantId  = (string)t["refrigerantId"] ?? "",
                    Label          = (string)t["label"] ?? "",
                    Source         = (string)t["source"] ?? ""
                };
                var per = t["perOdKgPerM"] as JObject;
                if (per != null)
                    foreach (var kv in per)
                    {
                        if (double.TryParse(kv.Key,
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out double od))
                            table.PerOdKgPerM[od] = (double)kv.Value;
                    }
                var off = t["offsetIfTotalUnderThreshold"] as JObject;
                if (off != null)
                {
                    table.ThresholdM = (double?)off["thresholdM"] ?? 0;
                    table.OffsetKg   = (double?)off["offsetKg"]   ?? 0;
                }
                if (!string.IsNullOrEmpty(table.Id)) lib.ById[table.Id] = table;
            }
        }
    }

    /// <summary>Per-OD pipe run for charge calculation.</summary>
    public class PipeRun
    {
        public double OdMm    { get; set; }
        public double LengthM { get; set; }
    }

    public class ChargeBreakdown
    {
        public double TotalKg        { get; set; }
        public double OffsetKg       { get; set; }
        public bool   OffsetApplied  { get; set; }
        public List<(double OdMm, double LengthM, double KgPerM, double SubKg, bool Matched)> Lines { get; }
            = new();
    }

    public static class RefrigerantChargeCalculator
    {
        /// <summary>
        /// Compute the additional refrigerant field-charge for a set of
        /// per-OD liquid-line runs against a vendor table.
        /// Returns 0 kg + empty breakdown when no table matches.
        /// </summary>
        public static ChargeBreakdown Compute(
            IEnumerable<PipeRun> runs, RefrigerantChargeTable table)
        {
            var br = new ChargeBreakdown();
            if (runs == null || table == null) return br;

            double totalLen = 0;
            foreach (var run in runs)
            {
                if (run == null || run.LengthM <= 0 || run.OdMm <= 0) continue;
                // Match the closest tabulated OD (within ±0.5 mm). If no
                // match the row gets 0 kg/m but stays in the breakdown so
                // the user can see what was skipped.
                double bestOd = 0; double bestDelta = double.MaxValue; double kgPerM = 0;
                foreach (var kv in table.PerOdKgPerM)
                {
                    double d = Math.Abs(kv.Key - run.OdMm);
                    if (d < bestDelta) { bestDelta = d; bestOd = kv.Key; kgPerM = kv.Value; }
                }
                bool matched = bestDelta <= 0.5;
                double sub = matched ? run.LengthM * kgPerM : 0;
                br.Lines.Add((run.OdMm, run.LengthM, matched ? kgPerM : 0, sub, matched));
                br.TotalKg += sub;
                totalLen   += run.LengthM;
            }
            if (table.ThresholdM > 0 && totalLen > 0 && totalLen < table.ThresholdM)
            {
                br.OffsetKg      = table.OffsetKg;
                br.OffsetApplied = true;
                br.TotalKg      += table.OffsetKg;
            }
            // Don't allow negative result — vendor offsets are credit-only.
            if (br.TotalKg < 0) br.TotalKg = 0;
            return br;
        }
    }
}
