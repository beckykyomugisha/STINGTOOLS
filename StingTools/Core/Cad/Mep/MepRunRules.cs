// ============================================================================
// MepRunRules.cs — Phase: MEP-from-DWG P6-3 (data-driven run rules).
//
// Moves the run engine's hardcoded policy — per-kind default size + run
// elevation + riser default size, the wall-mount category set, the fitting
// coincidence tolerance, and the drainage fall bands — out of C# into
// Data/STING_DWG_RUN_RULES.json (corporate) + <project>/_BIM_COORD/
// dwg_run_rules.json (per-field override, project wins). Same loader shape as
// MepFixtureMap.
//
// Every accessor falls back to the original MepRunClassifier constant when the
// data is absent, so a missing/partial file converts byte-for-byte unchanged —
// and the shipped corporate JSON reproduces those constants exactly.
// ============================================================================
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using StingTools.Core;

namespace StingTools.Core.Cad.Mep
{
    public class MepKindRule
    {
        public MepSize DefaultSize { get; set; }
        public double OffsetMm { get; set; }
        public MepSize RiserDefaultSize { get; set; }
    }

    public class MepDrainageBand
    {
        public double MaxDiaMm { get; set; }
        public double SlopePercent { get; set; }
    }

    /// <summary>Project override surface: an extra service token → MEPSystemClassification
    /// rule (the corporate mapping stays in MepServiceClassifier).</summary>
    public class MepServiceRule
    {
        public string Kind { get; set; } = "";            // "Duct" | "Pipe" | "" (any)
        public string Regex { get; set; } = "";           // matched (IgnoreCase) against the layer name
        public string Classification { get; set; } = "";  // MEPSystemClassification enum name

        private Regex _rx; private bool _compiled;
        public bool Matches(string layer, MepRunKind kind)
        {
            if (!string.IsNullOrEmpty(Kind) && !string.Equals(Kind, kind.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;
            if (!_compiled)
            {
                _compiled = true;
                if (!string.IsNullOrEmpty(Regex))
                    try { _rx = new Regex(Regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
                    catch (Exception ex) { StingLog.Warn($"MepServiceRule bad regex '{Regex}': {ex.Message}"); }
            }
            return _rx != null && !string.IsNullOrEmpty(layer) && _rx.IsMatch(layer);
        }
    }

    /// <summary>Root document of STING_DWG_RUN_RULES.json.</summary>
    public class MepRunRules
    {
        public string Version { get; set; } = "v1";
        public string Description { get; set; } = "";
        public double FittingToleranceMm { get; set; } = 12.0;
        public Dictionary<string, MepKindRule> Kinds { get; set; }
        public List<string> WallMountCategories { get; set; }
        public List<MepDrainageBand> DrainageSlopeBands { get; set; }
        public List<MepServiceRule> ServiceRules { get; set; }

        private static readonly string[] DefaultWallMount =
        {
            "Electrical Fixtures", "Data Devices", "Communication Devices",
            "Security Devices", "Fire Alarm Devices", "Nurse Call Devices"
        };

        private MepKindRule Kind(MepRunKind k)
            => Kinds != null && Kinds.TryGetValue(k.ToString(), out var kr) ? kr : null;

        /// <summary>Per-kind default size (fresh copy so callers can't mutate the rule).</summary>
        public MepSize DefaultSize(MepRunKind k)
        {
            var s = Kind(k)?.DefaultSize;
            return s != null ? CloneSize(s) : MepRunClassifier.Default(k);
        }

        /// <summary>Per-kind riser default size — a stack is larger than a branch default.</summary>
        public MepSize RiserDefaultSize(MepRunKind k)
        {
            var s = Kind(k)?.RiserDefaultSize;
            return s != null ? CloneSize(s) : DefaultSize(k);
        }

        public double OffsetMm(MepRunKind k)
        {
            double o = Kind(k)?.OffsetMm ?? 0;
            return o > 0 ? o : MepRunClassifier.DefaultOffsetMm(k);
        }

        public double FittingToleranceFt => (FittingToleranceMm > 0 ? FittingToleranceMm : 12.0) / 304.8;

        public HashSet<string> WallMountSet()
            => new HashSet<string>(WallMountCategories ?? DefaultWallMount.ToList(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Drainage fall (%) for a pipe diameter: the first band (by ascending MaxDiaMm)
        /// whose MaxDiaMm ≥ dia. Falls back to the flat 1.25 % constant when no band applies.</summary>
        public double SlopePercentForDiameter(double diaMm)
        {
            if (DrainageSlopeBands != null && DrainageSlopeBands.Count > 0)
            {
                foreach (var b in DrainageSlopeBands.OrderBy(x => x.MaxDiaMm))
                    if (diaMm <= b.MaxDiaMm && b.SlopePercent > 0) return b.SlopePercent;
            }
            return MepRunClassifier.DefaultDrainageSlopePercent;
        }

        /// <summary>Service token → classification. Project ServiceRules win when one matches;
        /// otherwise the corporate MepServiceClassifier mapping (in code) is used.</summary>
        public MEPSystemClassification Classify(string layer, MepRunKind kind, out bool defaulted)
        {
            if (ServiceRules != null)
                foreach (var r in ServiceRules)
                    if (r.Matches(layer, kind) && Enum.TryParse<MEPSystemClassification>(r.Classification, out var cls))
                    { defaulted = false; return cls; }
            return MepServiceClassifier.Classify(layer, kind, out defaulted);
        }

        private static MepSize CloneSize(MepSize s) => new MepSize
        {
            IsRound = s.IsRound, WidthMm = s.WidthMm, HeightMm = s.HeightMm,
            DiameterMm = s.DiameterMm, FromLayer = false, RectCoerced = false,
        };
    }

    /// <summary>Per-document loader + cache. Corporate baseline + project override merged
    /// per-field (project wins). Mirrors MepFixtureMap.</summary>
    public static class MepRunRulesRegistry
    {
        private const string CorporateFile = "STING_DWG_RUN_RULES.json";
        private const string ProjectFile   = "dwg_run_rules.json";

        private static readonly ConcurrentDictionary<string, MepRunRules> _cache
            = new ConcurrentDictionary<string, MepRunRules>(StringComparer.OrdinalIgnoreCase);
        private static MepRunRules _corporate;
        private static readonly object _corpLock = new object();

        public static MepRunRules Get(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _cache.GetOrAdd(key, _ => Merge(LoadCorporate(), LoadProjectOverride(doc)));
        }

        public static void Invalidate() { _cache.Clear(); lock (_corpLock) _corporate = null; }

        private static MepRunRules LoadCorporate()
        {
            lock (_corpLock)
            {
                if (_corporate != null) return _corporate;
                _corporate = new MepRunRules();
                try
                {
                    string path = StingToolsApp.FindDataFile(CorporateFile);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        var r = JsonConvert.DeserializeObject<MepRunRules>(File.ReadAllText(path));
                        if (r != null) _corporate = r;
                    }
                    else StingLog.Warn($"MepRunRules: {CorporateFile} not found — using built-in defaults.");
                }
                catch (Exception ex) { StingLog.Warn($"MepRunRules corporate load: {ex.Message}"); }
                return _corporate;
            }
        }

        private static MepRunRules LoadProjectOverride(Document doc)
        {
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (string.IsNullOrEmpty(dir)) return null;
                string path = Path.Combine(dir, "_BIM_COORD", ProjectFile);
                if (!File.Exists(path)) return null;
                return JsonConvert.DeserializeObject<MepRunRules>(File.ReadAllText(path));
            }
            catch (Exception ex) { StingLog.Warn($"MepRunRules project override: {ex.Message}"); return null; }
        }

        // Per-field overlay: any field the project file sets replaces the corporate one.
        private static MepRunRules Merge(MepRunRules c, MepRunRules p)
        {
            if (p == null) return c ?? new MepRunRules();
            var m = new MepRunRules
            {
                Version = p.Version ?? c?.Version ?? "v1",
                Description = c?.Description ?? "",
                FittingToleranceMm = p.FittingToleranceMm > 0 ? p.FittingToleranceMm : (c?.FittingToleranceMm ?? 12.0),
                Kinds = p.Kinds ?? c?.Kinds,
                WallMountCategories = p.WallMountCategories ?? c?.WallMountCategories,
                DrainageSlopeBands = p.DrainageSlopeBands ?? c?.DrainageSlopeBands,
                ServiceRules = (p.ServiceRules != null && p.ServiceRules.Count > 0) ? p.ServiceRules : c?.ServiceRules,
            };
            return m;
        }
    }
}
