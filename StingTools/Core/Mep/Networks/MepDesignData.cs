// MepDesignData — the design-data files behind the sprinkler, gas and
// stair-pressurisation calculations, parsed explicitly from JSON.
//
// Revit-free so StingTools.Mep.Tests can parse and Validate() the SHIPPED
// files: a mistyped key or a zero density then fails a test instead of
// silently becoming a default at runtime (CLAUDE.md §7). Keys are read by
// name, never by attribute binding, and ApplyJson only touches keys that are
// present — which is what makes a sparse project override work.
//
// Layering: corporate file first, then the project's _BIM_COORD override;
// list entries replace by id, scalars and maps replace per key.
// MepDesignDataLoader does the Revit-side file lookup.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core.Fire;
using StingTools.Core.Gas;

namespace StingTools.Core.Mep.Networks
{
    internal static class J
    {
        public static double D(JToken t, double fallback) =>
            t == null || t.Type == JTokenType.Null ? fallback
            : t.Type == JTokenType.Float || t.Type == JTokenType.Integer ? t.Value<double>()
            : double.TryParse(t.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        public static string S(JToken t, string fallback = "") => t == null || t.Type == JTokenType.Null ? fallback : t.ToString();
        public static bool B(JToken t, bool fallback = false) => t == null || t.Type != JTokenType.Boolean ? fallback : t.Value<bool>();

        public static void MergeMap(Dictionary<string, double> map, JToken t)
        {
            if (!(t is JObject o)) return;
            foreach (var p in o.Properties())
            {
                if (p.Name.StartsWith("_")) continue;
                if (p.Value.Type == JTokenType.Float || p.Value.Type == JTokenType.Integer)
                    map[p.Name] = p.Value.Value<double>();
            }
        }

        public static void MergeStrings(List<string> list, JToken t)
        {
            if (!(t is JArray a)) return;
            list.Clear();
            list.AddRange(a.Select(x => x.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        public static void MergeById<T>(List<T> list, JToken t, Func<T, string> id, Func<JObject, T, T> read) where T : class
        {
            if (!(t is JArray a)) return;
            foreach (var o in a.OfType<JObject>())
            {
                string key = S(o["id"]);
                if (string.IsNullOrWhiteSpace(key)) continue;
                int i = list.FindIndex(x => string.Equals(id(x), key, StringComparison.OrdinalIgnoreCase));
                var merged = read(o, i >= 0 ? list[i] : null);
                if (i >= 0) list[i] = merged; else list.Add(merged);
            }
        }

        /// <summary>Equivalent bores for a Revit part type name, falling back to "Default".</summary>
        public static double Bores(Dictionary<string, double> map, string partType)
        {
            if (!string.IsNullOrEmpty(partType))
                foreach (var kv in map)
                    if (partType.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0 && kv.Key != "Default")
                        return kv.Value;
            return map.TryGetValue("Default", out var d) ? d : 0;
        }
    }

    // ── Sprinklers ─────────────────────────────────────────────────────

    public sealed class SprinklerHazard
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public double DensityMmMin { get; set; }
        public double DesignAreaM2 { get; set; }
        public double MinHeadPressureBar { get; set; }
        public double MaxAreaPerHeadM2 { get; set; }
        public bool   Verify { get; set; } = true;
        public string Source { get; set; } = "";
    }

    public sealed class SprinklerDesignData
    {
        public List<SprinklerHazard> Hazards { get; } = new List<SprinklerHazard>();
        public string DefaultHazardId { get; set; } = "";
        public Dictionary<string, double> HazenWilliamsC { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public double PipeVelocityLimitMs { get; set; } = 10;
        public double ValveVelocityLimitMs { get; set; } = 6;
        public Dictionary<string, double> FittingEquivalentBores { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public List<string> KFactorParameters { get; } = new List<string>();
        public double UsKFactorBelow { get; set; } = 30;
        public List<string> Sources { get; } = new List<string>();

        public static SprinklerDesignData Parse(params string[] jsonLayers)
        {
            var d = new SprinklerDesignData();
            foreach (var json in jsonLayers.Where(s => !string.IsNullOrWhiteSpace(s)))
                d.ApplyJson(JObject.Parse(json));
            return d;
        }

        public void ApplyJson(JObject j)
        {
            J.MergeById(Hazards, j["hazards"], h => h.Id, (o, prev) => new SprinklerHazard
            {
                Id = J.S(o["id"]),
                Label = J.S(o["label"], prev?.Label ?? ""),
                DensityMmMin = J.D(o["densityMmMin"], prev?.DensityMmMin ?? 0),
                DesignAreaM2 = J.D(o["designAreaM2"], prev?.DesignAreaM2 ?? 0),
                MinHeadPressureBar = J.D(o["minHeadPressureBar"], prev?.MinHeadPressureBar ?? 0),
                MaxAreaPerHeadM2 = J.D(o["maxAreaPerHeadM2"], prev?.MaxAreaPerHeadM2 ?? 0),
                Verify = J.B(o["verify"], prev?.Verify ?? true),
                Source = J.S(o["source"], prev?.Source ?? "")
            });
            if (j["defaultHazardId"] != null) DefaultHazardId = J.S(j["defaultHazardId"]);
            J.MergeMap(HazenWilliamsC, j["hazenWilliamsC"]);
            if (j["velocityLimitsMs"] is JObject v)
            {
                PipeVelocityLimitMs = J.D(v["pipe"], PipeVelocityLimitMs);
                ValveVelocityLimitMs = J.D(v["valve"], ValveVelocityLimitMs);
            }
            J.MergeMap(FittingEquivalentBores, j["fittingEquivalentBores"]);
            if (j["kFactorParameters"] != null) J.MergeStrings(KFactorParameters, j["kFactorParameters"]);
            UsKFactorBelow = J.D(j["usKFactorBelow"], UsKFactorBelow);
        }

        public SprinklerHazard Hazard(string id) =>
            Hazards.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? Hazards.FirstOrDefault(h => string.Equals(h.Id, DefaultHazardId, StringComparison.OrdinalIgnoreCase))
            ?? Hazards.FirstOrDefault();

        public double EquivalentBores(string partType) => J.Bores(FittingEquivalentBores, partType);

        public double DefaultC => HazenWilliamsC.TryGetValue("default", out var c) && c > 0 ? c : 120;

        /// <summary>Model K-factor → L/min/bar^0.5 (small values are US gpm/psi^0.5).</summary>
        public double KFactorToSi(double k) => k > 0 && k < UsKFactorBelow ? k * 14.4 : k;

        public SprinklerDesignCriteria Criteria(SprinklerHazard h) => new SprinklerDesignCriteria
        {
            HazardId = h.Id,
            DensityMmMin = h.DensityMmMin,
            DesignAreaM2 = h.DesignAreaM2,
            MinHeadPressureBar = h.MinHeadPressureBar,
            DefaultC = DefaultC,
            MaxVelocityMs = PipeVelocityLimitMs,
            MaxValveVelocityMs = ValveVelocityLimitMs
        };

        public List<string> Validate()
        {
            var p = new List<string>();
            if (Hazards.Count == 0) p.Add("No hazards.");
            foreach (var h in Hazards)
            {
                if (h.DensityMmMin <= 0) p.Add($"Hazard {h.Id}: densityMmMin must be > 0.");
                if (h.DesignAreaM2 <= 0) p.Add($"Hazard {h.Id}: designAreaM2 must be > 0.");
                if (h.MinHeadPressureBar <= 0) p.Add($"Hazard {h.Id}: minHeadPressureBar must be > 0.");
                if (h.MaxAreaPerHeadM2 <= 0) p.Add($"Hazard {h.Id}: maxAreaPerHeadM2 must be > 0.");
            }
            if (!string.IsNullOrEmpty(DefaultHazardId) && Hazards.All(h => !string.Equals(h.Id, DefaultHazardId, StringComparison.OrdinalIgnoreCase)))
                p.Add($"defaultHazardId '{DefaultHazardId}' is not a hazard.");
            if (!FittingEquivalentBores.ContainsKey("Default")) p.Add("fittingEquivalentBores has no Default.");
            if (PipeVelocityLimitMs <= 0 || ValveVelocityLimitMs <= 0) p.Add("Velocity limits must be > 0.");
            return p;
        }
    }

    // ── Gas ────────────────────────────────────────────────────────────

    public sealed class GasPipeSeries
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public List<GasPipeSize> Sizes { get; } = new List<GasPipeSize>();
    }

    public sealed class GasDesignData
    {
        public List<GasProperties> Gases { get; } = new List<GasProperties>();
        public Dictionary<string, bool> GasVerify { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public string DefaultGasId { get; set; } = "";
        public List<GasPipeSeries> PipeSeries { get; } = new List<GasPipeSeries>();
        public string DefaultSeriesId { get; set; } = "";
        public Dictionary<string, double> FittingEquivalentBores { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public List<string> ApplianceLoadParameters { get; } = new List<string>();
        public List<string> Sources { get; } = new List<string>();

        public static GasDesignData Parse(params string[] jsonLayers)
        {
            var d = new GasDesignData();
            foreach (var json in jsonLayers.Where(s => !string.IsNullOrWhiteSpace(s)))
                d.ApplyJson(JObject.Parse(json));
            return d;
        }

        public void ApplyJson(JObject j)
        {
            J.MergeById(Gases, j["gases"], g => g.Id, (o, prev) =>
            {
                var g = new GasProperties
                {
                    Id = J.S(o["id"]),
                    Label = J.S(o["label"], prev?.Label ?? ""),
                    RelativeDensity = J.D(o["relativeDensity"], prev?.RelativeDensity ?? 0),
                    CalorificValueMJm3 = J.D(o["calorificValueMJm3"], prev?.CalorificValueMJm3 ?? 0),
                    MaxDropMbar = J.D(o["maxDropMbar"], prev?.MaxDropMbar ?? 0),
                    Source = J.S(o["source"], prev?.Source ?? "")
                };
                GasVerify[g.Id] = J.B(o["verify"], GasVerify.TryGetValue(g.Id, out var pv) && pv);
                return g;
            });
            if (j["defaultGasId"] != null) DefaultGasId = J.S(j["defaultGasId"]);
            J.MergeById(PipeSeries, j["pipeSeries"], s => s.Id, (o, prev) =>
            {
                var s = new GasPipeSeries { Id = J.S(o["id"]), Label = J.S(o["label"], prev?.Label ?? "") };
                if (o["sizes"] is JArray arr)
                    s.Sizes.AddRange(arr.OfType<JObject>().Select(z => new GasPipeSize
                    {
                        Label = J.S(z["label"]),
                        OuterDiameterMm = J.D(z["outerDiameterMm"], 0),
                        NominalMm = J.D(z["nominalMm"], 0),
                        BoreMm = J.D(z["boreMm"], 0)
                    }));
                else if (prev != null) s.Sizes.AddRange(prev.Sizes);
                return s;
            });
            if (j["defaultSeriesId"] != null) DefaultSeriesId = J.S(j["defaultSeriesId"]);
            J.MergeMap(FittingEquivalentBores, j["fittingEquivalentBores"]);
            if (j["applianceLoadParameters"] != null) J.MergeStrings(ApplianceLoadParameters, j["applianceLoadParameters"]);
        }

        public double EquivalentBores(string partType) => J.Bores(FittingEquivalentBores, partType);

        public GasProperties Gas(string id) =>
            Gases.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? Gases.FirstOrDefault(g => string.Equals(g.Id, DefaultGasId, StringComparison.OrdinalIgnoreCase))
            ?? Gases.FirstOrDefault();

        public GasPipeSeries Series(string id) =>
            PipeSeries.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? PipeSeries.FirstOrDefault(s => string.Equals(s.Id, DefaultSeriesId, StringComparison.OrdinalIgnoreCase))
            ?? PipeSeries.FirstOrDefault();

        public List<string> Validate()
        {
            var p = new List<string>();
            if (Gases.Count == 0) p.Add("No gases.");
            foreach (var g in Gases)
            {
                if (g.RelativeDensity <= 0) p.Add($"Gas {g.Id}: relativeDensity must be > 0.");
                if (g.CalorificValueMJm3 <= 0) p.Add($"Gas {g.Id}: calorificValueMJm3 must be > 0.");
                if (g.MaxDropMbar <= 0) p.Add($"Gas {g.Id}: maxDropMbar must be > 0.");
            }
            if (PipeSeries.Count == 0) p.Add("No pipe series.");
            foreach (var s in PipeSeries)
            {
                if (s.Sizes.Count == 0) p.Add($"Series {s.Id}: no sizes.");
                foreach (var z in s.Sizes)
                    if (z.BoreMm <= 0 || z.OuterDiameterMm <= z.BoreMm)
                        p.Add($"Series {s.Id} size {z.Label}: bore must be > 0 and below the outer diameter.");
                foreach (var z in s.Sizes.Where(z => z.NominalMm <= 0))
                    p.Add($"Series {s.Id} size {z.Label}: nominalMm must be > 0.");
                var bores = s.Sizes.Select(z => z.BoreMm).ToList();
                if (!bores.SequenceEqual(bores.OrderBy(b => b))) p.Add($"Series {s.Id}: sizes must ascend by bore.");
            }
            if (!string.IsNullOrEmpty(DefaultGasId) && Gases.All(g => !string.Equals(g.Id, DefaultGasId, StringComparison.OrdinalIgnoreCase)))
                p.Add($"defaultGasId '{DefaultGasId}' is not a gas.");
            if (!string.IsNullOrEmpty(DefaultSeriesId) && PipeSeries.All(s => !string.Equals(s.Id, DefaultSeriesId, StringComparison.OrdinalIgnoreCase)))
                p.Add($"defaultSeriesId '{DefaultSeriesId}' is not a series.");
            if (!FittingEquivalentBores.ContainsKey("Default")) p.Add("fittingEquivalentBores has no Default.");
            if (ApplianceLoadParameters.Count == 0) p.Add("applianceLoadParameters is empty.");
            return p;
        }
    }

    // ── Smoke control ──────────────────────────────────────────────────

    public sealed class PressurisationClass
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public double DesignPressurePa { get; set; }
        public double OpenDoorVelocityMs { get; set; }
        public int    OpenDoors { get; set; } = 1;
        public bool   Verify { get; set; } = true;
        public string Source { get; set; } = "";
    }

    public sealed class SmokeControlDesignData
    {
        public List<PressurisationClass> Classes { get; } = new List<PressurisationClass>();
        public string DefaultClassId { get; set; } = "";
        public Dictionary<string, double> DoorLeakageAreasM2 { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public double LeakageAllowance { get; set; } = 1.0;
        public double OpenCaseResidualPressurePa { get; set; }
        public double DoorCloserForceN { get; set; } = 30;
        public double HandleToEdgeM { get; set; } = 0.075;
        public double MaxDoorOpeningForceN { get; set; } = 100;
        public List<string> Sources { get; } = new List<string>();

        public static SmokeControlDesignData Parse(params string[] jsonLayers)
        {
            var d = new SmokeControlDesignData();
            foreach (var json in jsonLayers.Where(s => !string.IsNullOrWhiteSpace(s)))
                d.ApplyJson(JObject.Parse(json));
            return d;
        }

        public void ApplyJson(JObject j)
        {
            J.MergeById(Classes, j["systemClasses"], c => c.Id, (o, prev) => new PressurisationClass
            {
                Id = J.S(o["id"]),
                Label = J.S(o["label"], prev?.Label ?? ""),
                DesignPressurePa = J.D(o["designPressurePa"], prev?.DesignPressurePa ?? 0),
                OpenDoorVelocityMs = J.D(o["openDoorVelocityMs"], prev?.OpenDoorVelocityMs ?? 0),
                OpenDoors = (int)J.D(o["openDoors"], prev?.OpenDoors ?? 1),
                Verify = J.B(o["verify"], prev?.Verify ?? true),
                Source = J.S(o["source"], prev?.Source ?? "")
            });
            if (j["defaultClassId"] != null) DefaultClassId = J.S(j["defaultClassId"]);
            J.MergeMap(DoorLeakageAreasM2, j["doorLeakageAreasM2"]);
            LeakageAllowance = J.D(j["leakageAllowance"], LeakageAllowance);
            OpenCaseResidualPressurePa = J.D(j["openCaseResidualPressurePa"], OpenCaseResidualPressurePa);
            DoorCloserForceN = J.D(j["doorCloserForceN"], DoorCloserForceN);
            HandleToEdgeM = J.D(j["handleToEdgeM"], HandleToEdgeM);
            MaxDoorOpeningForceN = J.D(j["maxDoorOpeningForceN"], MaxDoorOpeningForceN);
        }

        public PressurisationClass Class(string id) =>
            Classes.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? Classes.FirstOrDefault(c => string.Equals(c.Id, DefaultClassId, StringComparison.OrdinalIgnoreCase))
            ?? Classes.FirstOrDefault();

        public double DoorLeakage(string key) => DoorLeakageAreasM2.TryGetValue(key, out var a) ? a : 0;

        public List<string> Validate()
        {
            var p = new List<string>();
            if (Classes.Count == 0) p.Add("No system classes.");
            foreach (var c in Classes)
            {
                if (c.DesignPressurePa <= 0) p.Add($"Class {c.Id}: designPressurePa must be > 0.");
                if (c.OpenDoorVelocityMs < 0) p.Add($"Class {c.Id}: openDoorVelocityMs cannot be negative.");
                if (c.OpenDoors < 0) p.Add($"Class {c.Id}: openDoors cannot be negative.");
            }
            foreach (var key in new[] { "singleLeafOpeningIntoStair", "singleLeafOpeningOutOfStair", "doubleLeaf", "liftLandingDoor" })
                if (DoorLeakage(key) <= 0) p.Add($"doorLeakageAreasM2.{key} must be > 0.");
            if (LeakageAllowance < 1.0) p.Add("leakageAllowance must be ≥ 1.");
            if (HandleToEdgeM <= 0 || MaxDoorOpeningForceN <= 0) p.Add("Door force inputs must be > 0.");
            if (!string.IsNullOrEmpty(DefaultClassId) && Classes.All(c => !string.Equals(c.Id, DefaultClassId, StringComparison.OrdinalIgnoreCase)))
                p.Add($"defaultClassId '{DefaultClassId}' is not a class.");
            return p;
        }
    }
}
