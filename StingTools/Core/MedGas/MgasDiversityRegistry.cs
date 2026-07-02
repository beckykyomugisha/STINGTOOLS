// Healthcare Pack HC-DEF-07 — project-supplied medical-gas diversity override.
//
// NFPA99Standards ships diversity factors only for the gases whose values are
// asserted with confidence (O2/MA4/MA7/N2O/VAC/AGS); N2/CO2/He/dental fall back
// to 1.0 (over-sizes, safe, and flagged since Phase 197). This registry is the
// data seam: a project supplies the authoritative HTM 02-01 Pt A Table 8 /
// NFPA 99 Table 5.1.13.3.4 factors via JSON (corporate baseline +
// <project>/_BIM_COORD/mgas_diversity.json override) WITHOUT a recompile — no
// guessed numbers are hardcoded here. MgasFlowSolver consults this first, then
// NFPA99Standards, then the flagged 1.0 fallback.

using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace StingTools.Core.MedGas
{
    internal class MgasDiversityDef
    {
        [JsonProperty("factors")] public Dictionary<string, double> Factors { get; set; }
            = new Dictionary<string, double>();
    }

    public class MgasDiversityTable
    {
        private readonly Dictionary<string, double> _factors;
        public MgasDiversityTable(Dictionary<string, double> f)
            => _factors = new Dictionary<string, double>(f ?? new Dictionary<string, double>(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Project-supplied diversity for a gas, or null when none is configured.</summary>
        public double? Get(string gasCode) =>
            !string.IsNullOrEmpty(gasCode) && _factors.TryGetValue(gasCode, out var v) ? v : (double?)null;
    }

    public static class MgasDiversityRegistry
    {
        private const string CorporateFileName = "HEALTHCARE_MGAS_DIVERSITY.json";
        private const string ProjectFileName = "mgas_diversity.json";

        private static readonly ConcurrentDictionary<string, MgasDiversityTable> _cache
            = new ConcurrentDictionary<string, MgasDiversityTable>(StringComparer.OrdinalIgnoreCase);

        private static string DocKey(Document doc)
        {
            try { return Path.GetDirectoryName(doc?.PathName ?? "") ?? ""; }
            catch { return ""; }
        }

        public static MgasDiversityTable Get(Document doc) => _cache.GetOrAdd(DocKey(doc), _ => Load(doc));

        /// <summary>Project/corporate-supplied diversity for a gas (null when unset).</summary>
        public static double? Get(Document doc, string gasCode) => Get(doc).Get(gasCode);

        public static void Reload(Document doc = null)
        {
            if (doc == null) _cache.Clear();
            else _cache.TryRemove(DocKey(doc), out _);
        }

        private static MgasDiversityTable Load(Document doc)
        {
            var factors = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string corp = StingToolsApp.FindDataFile(CorporateFileName);
                if (!string.IsNullOrEmpty(corp) && File.Exists(corp))
                    Merge(factors, JsonConvert.DeserializeObject<MgasDiversityDef>(File.ReadAllText(corp)));
            }
            catch (Exception ex) { StingLog.Warn($"MgasDiversityRegistry corporate load: {ex.Message}"); }

            try
            {
                string dir = DocKey(doc);
                if (!string.IsNullOrEmpty(dir))
                {
                    string proj = Path.Combine(dir, "_BIM_COORD", ProjectFileName);
                    if (File.Exists(proj))
                    {
                        Merge(factors, JsonConvert.DeserializeObject<MgasDiversityDef>(File.ReadAllText(proj)));
                        StingLog.Info($"MgasDiversityRegistry: project overlay loaded from {proj}");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"MgasDiversityRegistry overlay load: {ex.Message}"); }

            return new MgasDiversityTable(factors);
        }

        private static void Merge(Dictionary<string, double> into, MgasDiversityDef def)
        {
            foreach (var kv in def?.Factors ?? new Dictionary<string, double>())
                if (!string.IsNullOrWhiteSpace(kv.Key)) into[kv.Key.Trim()] = kv.Value;
        }
    }
}
