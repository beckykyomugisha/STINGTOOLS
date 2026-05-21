// StingTools — MEP Sizing Registry.
//
// Single source of truth for velocity targets, friction limits, aspect
// ratios, standard sizes, gauge breakpoints, balancing tolerances and
// NC targets used by MepAutoSize*, MEPBalancingEngine, NC checks and
// the HVAC dock panel.
//
// Layered:
//   corporate baseline → Data/STING_MEP_SIZING_RULES.json
//   project override   → <project>/_BIM_COORD/mep_sizing_rules.json
//
// Replaces the hardcoded constants flagged in the flexibility review:
//   - PipeMaxVelMs = 2.5            (Commands/Mep/MepAutoSizeCommand.cs)
//   - DuctMaxVelMs = 6.0
//   - MaxAspect    = 3.0
//   - MaxFillPct   = 45.0
//   - 1:1.5 aspect baked into Math.Sqrt(area * 1.5)
//   - SMACNA-only standard size table

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Mep
{
    public class MepSizingRegion
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Standard { get; set; } = "";
        public double AirDensityKgM3 { get; set; } = 1.20;
    }

    public class DuctRole
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public double MaxVelocityMs { get; set; }
        public double MaxFrictionPaPerM { get; set; }
        public double AspectMax { get; set; } = 3.0;
        public string Source { get; set; } = "";
    }

    public class DuctPressureClass
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public double MaxPa { get; set; }
        public string Standard { get; set; } = "";
    }

    public class DuctGaugeBreakpoint
    {
        public double UptoWidthMm { get; set; }
        public double ThicknessMm { get; set; }
        public string Seam { get; set; } = "A";
        public string Label { get; set; } = "";
    }

    public class PipeService
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public double MaxVelocityMs { get; set; }
        public double MaxPaPerM { get; set; }
        public string Source { get; set; } = "";
    }

    public class SizingStrategyOption
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public bool Default { get; set; }
        public string Notes { get; set; } = "";
    }

    public class BalancingSettings
    {
        public int MaxIterations { get; set; } = 100;
        public double TolerancePa { get; set; } = 1.0;
        public double DampingFactor { get; set; } = 0.7;
        public double MinBranchFlowLs { get; set; } = 0.01;
        public string Notes { get; set; } = "";
    }

    public class NcTarget
    {
        public string SpaceType { get; set; } = "";
        public int Target { get; set; }
    }

    /// <summary>
    /// Loaded view of STING_MEP_SIZING_RULES.json + project override.
    /// </summary>
    public class MepSizingRules
    {
        public Dictionary<string, MepSizingRegion> Regions { get; set; } = new();

        // Duct
        public string DuctDefaultRegion { get; set; } = "UK_SI";
        public List<DuctRole> DuctRoles { get; set; } = new();
        public double DuctDefaultAspect { get; set; } = 1.5;
        public List<DuctPressureClass> DuctPressureClasses { get; set; } = new();
        public Dictionary<string, double[]> DuctStandardSizesMm { get; set; } = new();
        public List<DuctGaugeBreakpoint> DuctGaugeBreakpoints { get; set; } = new();
        public Dictionary<string, double> DuctFittingLossK { get; set; }
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        // Pipe
        public string PipeDefaultRegion { get; set; } = "UK_SI";
        public List<PipeService> PipeServices { get; set; } = new();
        public Dictionary<string, double[]> PipeStandardBoreMm { get; set; } = new();

        // Conduit + tray
        public double ConduitMaxFillPct { get; set; } = 45.0;
        public double CableTrayMaxFillPct { get; set; } = 50.0;

        // Strategy + balancing + acoustics
        public List<SizingStrategyOption> SizingStrategies { get; set; } = new();
        public BalancingSettings Balancing { get; set; } = new();
        public List<NcTarget> NcTargets { get; set; } = new();

        public DuctRole GetDuctRole(string id)
            => DuctRoles.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? DuctRoles.FirstOrDefault(r => r.Id == "branch")
               ?? new DuctRole { Id = "branch", MaxVelocityMs = 6.0, MaxFrictionPaPerM = 1.0, AspectMax = 3.0 };

        public PipeService GetPipeService(string id)
            => PipeServices.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
               ?? new PipeService { Id = "chw", MaxVelocityMs = 1.5, MaxPaPerM = 250 };

        public double[] DuctSizesForRegion(string region)
        {
            if (DuctStandardSizesMm.TryGetValue(region ?? DuctDefaultRegion, out var arr) && arr != null) return arr;
            if (DuctStandardSizesMm.TryGetValue(DuctDefaultRegion, out var def) && def != null) return def;
            return new double[] { 100, 150, 200, 250, 300, 400, 500, 600, 800, 1000, 1200 };
        }

        public double[] PipeBoresForRegion(string region)
        {
            if (PipeStandardBoreMm.TryGetValue(region ?? PipeDefaultRegion, out var arr) && arr != null) return arr;
            if (PipeStandardBoreMm.TryGetValue(PipeDefaultRegion, out var def) && def != null) return def;
            return new double[] { 15, 20, 25, 32, 40, 50, 65, 80, 100, 125, 150 };
        }

        public DuctGaugeBreakpoint GaugeForWidth(double widthMm)
        {
            foreach (var g in DuctGaugeBreakpoints.OrderBy(b => b.UptoWidthMm))
                if (widthMm <= g.UptoWidthMm) return g;
            // Past the largest breakpoint: log once per width-bucket so the project
            // owner sees that the SMACNA table doesn't cover this duct, rather than
            // silently shipping the heaviest gauge as if it were authoritative.
            var fallback = DuctGaugeBreakpoints.LastOrDefault()
                ?? new DuctGaugeBreakpoint { UptoWidthMm = 9999, ThicknessMm = 1.2, Seam = "D" };
            StingTools.Core.StingLog.Warn(
                $"MepSizingRegistry: duct width {widthMm:F0} mm exceeds largest gauge breakpoint " +
                $"({fallback.UptoWidthMm:F0} mm); using fallback gauge {fallback.ThicknessMm} mm / seam {fallback.Seam}.");
            return fallback;
        }
    }

    /// <summary>
    /// Loader / cache for MepSizingRules. Layered baseline + project override
    /// pattern mirrors DrawingTypeRegistry / AecFilterRegistry / ViewStylePackRegistry.
    /// </summary>
    public static class MepSizingRegistry
    {
        // Per-document cache. Keying by doc.PathName so multiple open RVTs each
        // keep their own merged baseline+override rather than thrashing a
        // single-slot cache on every focus switch.
        private static readonly ConcurrentDictionary<string, MepSizingRules> _cache
            = new ConcurrentDictionary<string, MepSizingRules>(StringComparer.OrdinalIgnoreCase);

        public const string DataFileName = "STING_MEP_SIZING_RULES.json";
        public const string ProjectOverrideRelPath = "_BIM_COORD/mep_sizing_rules.json";

        /// <summary>Resolve the active rule set for a Revit document (cached by project file path).</summary>
        public static MepSizingRules Get(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            return _cache.GetOrAdd(key, _ => Load(doc));
        }

        /// <summary>Force a reload from disk for every cached project.</summary>
        public static void Reload()
        {
            _cache.Clear();
        }

        /// <summary>Force a reload for a single document (e.g. after Save As).</summary>
        public static void Reload(Document doc)
        {
            string key = doc?.PathName ?? "<no-doc>";
            _cache.TryRemove(key, out _);
        }

        private static MepSizingRules Load(Document doc)
        {
            var rules = new MepSizingRules();
            try
            {
                // 1. Corporate baseline from Data/STING_MEP_SIZING_RULES.json
                string basePath = StingTools.Core.StingToolsApp.FindDataFile(DataFileName);
                if (!string.IsNullOrEmpty(basePath) && File.Exists(basePath))
                {
                    JObject baseJ = JObject.Parse(File.ReadAllText(basePath));
                    Apply(baseJ, rules);
                }

                // 2. Project override
                if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                {
                    string projDir = Path.GetDirectoryName(doc.PathName) ?? "";
                    string projPath = Path.Combine(projDir, ProjectOverrideRelPath);
                    if (File.Exists(projPath))
                    {
                        JObject projJ = JObject.Parse(File.ReadAllText(projPath));
                        Apply(projJ, rules);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MepSizingRegistry.Load failed; using defaults", ex);
                ApplyDefaults(rules);
            }

            if (rules.DuctRoles.Count == 0) ApplyDefaults(rules);
            return rules;
        }

        private static void Apply(JObject j, MepSizingRules rules)
        {
            // Regions
            var regions = j["regions"] as JObject;
            if (regions != null)
            {
                foreach (var kv in regions)
                {
                    var r = kv.Value as JObject; if (r == null) continue;
                    rules.Regions[kv.Key] = new MepSizingRegion
                    {
                        Id             = kv.Key,
                        Label          = (string)r["label"] ?? kv.Key,
                        Standard       = (string)r["standard"] ?? "",
                        AirDensityKgM3 = (double?)r["airDensityKgM3"] ?? 1.20
                    };
                }
            }

            // Duct
            var duct = j["duct"] as JObject;
            if (duct != null)
            {
                rules.DuctDefaultRegion = (string)duct["_defaultRegion"] ?? rules.DuctDefaultRegion;
                rules.DuctDefaultAspect = (double?)duct["defaultAspect"] ?? rules.DuctDefaultAspect;

                var roles = duct["roles"] as JArray;
                if (roles != null)
                {
                    rules.DuctRoles.Clear();
                    foreach (var t in roles.OfType<JObject>())
                    {
                        rules.DuctRoles.Add(new DuctRole
                        {
                            Id                = (string)t["id"] ?? "",
                            Label             = (string)t["label"] ?? "",
                            MaxVelocityMs     = (double?)t["maxVelocityMs"] ?? 6.0,
                            MaxFrictionPaPerM = (double?)t["maxFrictionPaPerM"] ?? 1.0,
                            AspectMax         = (double?)t["aspectMax"] ?? 3.0,
                            Source            = (string)t["source"] ?? ""
                        });
                    }
                }

                var pcls = duct["pressureClasses"] as JArray;
                if (pcls != null)
                {
                    rules.DuctPressureClasses.Clear();
                    foreach (var t in pcls.OfType<JObject>())
                    {
                        rules.DuctPressureClasses.Add(new DuctPressureClass
                        {
                            Id       = (string)t["id"] ?? "",
                            Label    = (string)t["label"] ?? "",
                            MaxPa    = (double?)t["maxPa"] ?? 500,
                            Standard = (string)t["standard"] ?? ""
                        });
                    }
                }

                var sizes = duct["standardSizesMm"] as JObject;
                if (sizes != null)
                {
                    rules.DuctStandardSizesMm.Clear();
                    foreach (var kv in sizes)
                    {
                        var arr = kv.Value as JArray;
                        if (arr == null) continue;
                        rules.DuctStandardSizesMm[kv.Key] = arr.Select(v => (double)v).ToArray();
                    }
                }

                var gauges = duct["gaugeBreakpoints"] as JArray;
                if (gauges != null)
                {
                    rules.DuctGaugeBreakpoints.Clear();
                    foreach (var t in gauges.OfType<JObject>())
                    {
                        rules.DuctGaugeBreakpoints.Add(new DuctGaugeBreakpoint
                        {
                            UptoWidthMm = (double?)t["uptoWidthMm"] ?? 9999,
                            ThicknessMm = (double?)t["thicknessMm"] ?? 1.0,
                            Seam        = (string)t["seam"] ?? "A",
                            Label       = (string)t["label"] ?? ""
                        });
                    }
                }

                // Fitting loss coefficients (project-overrideable subset of the
                // SMACNA table baked into DuctFrictionSolver.SmacnaCoefficients).
                var fits = duct["fittingLossCoefficients"] as JObject;
                if (fits != null)
                {
                    foreach (var kv in fits)
                    {
                        if (kv.Key.StartsWith("_")) continue; // _notes etc.
                        if (kv.Value is JValue jv &&
                            (jv.Type == JTokenType.Float || jv.Type == JTokenType.Integer))
                        {
                            try { rules.DuctFittingLossK[kv.Key] = (double)kv.Value; }
                            catch { /* skip malformed entry */ }
                        }
                    }
                }
            }

            // Pipe
            var pipe = j["pipe"] as JObject;
            if (pipe != null)
            {
                rules.PipeDefaultRegion = (string)pipe["_defaultRegion"] ?? rules.PipeDefaultRegion;
                var svcs = pipe["services"] as JArray;
                if (svcs != null)
                {
                    rules.PipeServices.Clear();
                    foreach (var t in svcs.OfType<JObject>())
                    {
                        rules.PipeServices.Add(new PipeService
                        {
                            Id            = (string)t["id"] ?? "",
                            Label         = (string)t["label"] ?? "",
                            MaxVelocityMs = (double?)t["maxVelocityMs"] ?? 2.0,
                            MaxPaPerM     = (double?)t["maxPaPerM"] ?? 250,
                            Source        = (string)t["source"] ?? ""
                        });
                    }
                }
                var bores = pipe["standardBoreMm"] as JObject;
                if (bores != null)
                {
                    rules.PipeStandardBoreMm.Clear();
                    foreach (var kv in bores)
                    {
                        var arr = kv.Value as JArray; if (arr == null) continue;
                        rules.PipeStandardBoreMm[kv.Key] = arr.Select(v => (double)v).ToArray();
                    }
                }
            }

            var conduit = j["conduit"] as JObject;
            if (conduit != null) rules.ConduitMaxFillPct = (double?)conduit["maxFillPct"] ?? rules.ConduitMaxFillPct;
            var tray = j["cableTray"] as JObject;
            if (tray != null) rules.CableTrayMaxFillPct = (double?)tray["maxFillPct"] ?? rules.CableTrayMaxFillPct;

            var strat = j["sizingStrategy"] as JObject;
            if (strat?["options"] is JArray opts)
            {
                rules.SizingStrategies.Clear();
                foreach (var t in opts.OfType<JObject>())
                {
                    rules.SizingStrategies.Add(new SizingStrategyOption
                    {
                        Id      = (string)t["id"] ?? "",
                        Label   = (string)t["label"] ?? "",
                        Default = (bool?)t["default"] ?? false,
                        Notes   = (string)t["notes"] ?? ""
                    });
                }
            }

            var bal = j["balancing"] as JObject;
            if (bal != null)
            {
                rules.Balancing.MaxIterations   = (int?)bal["maxIterations"] ?? rules.Balancing.MaxIterations;
                rules.Balancing.TolerancePa     = (double?)bal["tolerancePa"] ?? rules.Balancing.TolerancePa;
                rules.Balancing.DampingFactor   = (double?)bal["dampingFactor"] ?? rules.Balancing.DampingFactor;
                rules.Balancing.MinBranchFlowLs = (double?)bal["minBranchFlowLs"] ?? rules.Balancing.MinBranchFlowLs;
                rules.Balancing.Notes           = (string)bal["notes"] ?? rules.Balancing.Notes;
            }

            var ac = j["acoustics"] as JObject;
            if (ac?["ncTargets"] is JArray nca)
            {
                rules.NcTargets.Clear();
                foreach (var t in nca.OfType<JObject>())
                {
                    rules.NcTargets.Add(new NcTarget
                    {
                        SpaceType = (string)t["spaceType"] ?? "",
                        Target    = (int?)t["ncTarget"] ?? 35
                    });
                }
            }
        }

        private static void ApplyDefaults(MepSizingRules r)
        {
            r.DuctRoles.Clear();
            r.DuctRoles.AddRange(new[]
            {
                new DuctRole { Id="main",   Label="Main",   MaxVelocityMs=8.0,  MaxFrictionPaPerM=1.2, AspectMax=3.0, Source="CIBSE B3" },
                new DuctRole { Id="branch", Label="Branch", MaxVelocityMs=6.0,  MaxFrictionPaPerM=1.0, AspectMax=2.5, Source="CIBSE B3" },
                new DuctRole { Id="runout", Label="Runout", MaxVelocityMs=4.5,  MaxFrictionPaPerM=0.8, AspectMax=2.0, Source="CIBSE B3" }
            });

            if (r.PipeServices.Count == 0)
            {
                r.PipeServices.AddRange(new[]
                {
                    new PipeService { Id="chw", Label="Chilled water", MaxVelocityMs=1.5, MaxPaPerM=250 },
                    new PipeService { Id="hws", Label="Heating water", MaxVelocityMs=2.0, MaxPaPerM=350 },
                    new PipeService { Id="dcw", Label="Domestic cold", MaxVelocityMs=2.0, MaxPaPerM=300 },
                    new PipeService { Id="dhw", Label="Domestic hot",  MaxVelocityMs=1.0, MaxPaPerM=200 }
                });
            }
        }
    }
}
