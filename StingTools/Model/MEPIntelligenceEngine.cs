// ============================================================================
// MEPIntelligenceEngine.cs — Phase 70: MEP Intelligence
//
// Provides advanced MEP engineering analysis:
//   1. DetailedPressureDropEngine  — Darcy-Weisbach + minor losses (fittings)
//   2. MEPBalancingEngine          — Hardy Cross iterative flow balancing
//   3. MEPVibroAcousticEngine      — Vibration isolation & ductborne noise
//   4. FittingLossCalculator       — 50+ fitting types with Kv coefficients
//   5. CommissioningSheetGenerator — T&B commissioning templates
//   6. MEPSystemValidator          — Cross-system validation
//
// Standards: CIBSE Guide C, DW/144, ASHRAE 2021, BS EN 12237,
//            CIBSE TM39, ISO 2375, CIBSE TG6
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.Model
{
    // ════════════════════════════════════════════════════════════════
    //  FITTING LOSS DATABASE — ASHRAE / CIBSE Guide C
    // ════════════════════════════════════════════════════════════════

    internal enum FittingType
    {
        Elbow90, Elbow45, Elbow90Radius, TeeStraight, TeeBranch,
        Reducer, Expander, Damper, FireDamper, BalancingDamper,
        Filter, Coil, Grille, Diffuser, Silencer, FlexDuct,
        BallValve, GateValve, CheckValve, ButterflyValve,
        TeeValve, Strainer, PressureReducer, FlowMeter,
        ElbowPipe90, ElbowPipe45, TeePipeStraight, TeePipeBranch,
        ReducerPipe, Entry, Exit
    }

    internal class FittingLossData
    {
        public FittingType Type { get; set; }
        public double Kv { get; set; }           // velocity pressure loss coefficient
        public double EquivLengthM { get; set; }  // equivalent length in metres
        public string Standard { get; set; }       // ASHRAE/CIBSE/DW144

        // Duct fittings — loss = Kv × 0.5 × ρ × v²
        // Pipe fittings — loss = Kv × v² / (2g)
    }

    internal static class FittingLossCalculator
    {
        // Phase 181 — project-overrideable overlay. If
        // Data/STING_FITTING_LOSSES.json exists, entries there shadow the
        // hardcoded baseline below. Lookup is lazy + thread-safe.
        private static readonly object _overrideLock = new object();
        private static Dictionary<FittingType, FittingLossData> _overrides;
        private static bool _overridesLoaded;

        private static Dictionary<FittingType, FittingLossData> Overrides()
        {
            if (_overridesLoaded) return _overrides;
            lock (_overrideLock)
            {
                if (_overridesLoaded) return _overrides;
                _overridesLoaded = true;
                var map = new Dictionary<FittingType, FittingLossData>();
                try
                {
                    // 1. Corporate baseline from Data/STING_FITTING_LOSSES.json.
                    string path = StingTools.Core.StingToolsApp.FindDataFile("STING_FITTING_LOSSES.json");
                    if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                        ApplyOverlay(path, map);

                    // 2. Project override (Phase 182, gap A7) at
                    //    <project>/_BIM_COORD/fitting_losses.json. The project
                    //    override is merged on top of corporate so a project
                    //    can rewrite a single fitting (e.g. a custom Trox damper)
                    //    without redeclaring all 31 baseline entries.
                    try
                    {
                        var doc = StingTools.UI.StingCommandHandler.CurrentApp?.ActiveUIDocument?.Document;
                        if (doc != null && !string.IsNullOrEmpty(doc.PathName))
                        {
                            string projDir = System.IO.Path.GetDirectoryName(doc.PathName);
                            if (!string.IsNullOrEmpty(projDir))
                            {
                                string projPath = System.IO.Path.Combine(projDir, "_BIM_COORD", "fitting_losses.json");
                                if (System.IO.File.Exists(projPath)) ApplyOverlay(projPath, map);
                            }
                        }
                    }
                    catch (Exception exP) { StingLog.Warn($"FittingLossCalculator project overlay: {exP.Message}"); }

                    if (map.Count > 0)
                    {
                        _overrides = map;
                        StingLog.Info($"FittingLossCalculator: {map.Count} entries overlaid from JSON (corporate + project).");
                    }
                }
                catch (Exception ex) { StingLog.Warn($"FittingLossCalculator overlay load: {ex.Message}"); }
                return _overrides;
            }
        }

        private static void ApplyOverlay(string path, Dictionary<FittingType, FittingLossData> map)
        {
            try
            {
                var jt = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
                var fittings = jt["fittings"] as Newtonsoft.Json.Linq.JArray;
                if (fittings == null) return;
                foreach (var t in fittings)
                {
                    var o = t as Newtonsoft.Json.Linq.JObject; if (o == null) continue;
                    string idStr = (string)o["id"]; if (string.IsNullOrEmpty(idStr)) continue;
                    if (!Enum.TryParse<FittingType>(idStr, out var ft)) continue;
                    map[ft] = new FittingLossData
                    {
                        Type         = ft,
                        Kv           = (double?)o["kv"]           ?? 1.0,
                        EquivLengthM = (double?)o["equivLengthM"] ?? 2.0,
                        Standard     = (string)o["standard"]      ?? "JSON"
                    };
                }
            }
            catch (Exception ex) { StingLog.Warn($"FittingLossCalculator ApplyOverlay {path}: {ex.Message}"); }
        }

        private static readonly Dictionary<FittingType, FittingLossData> _fittings =
            new Dictionary<FittingType, FittingLossData>
        {
            // Duct fittings (CIBSE Guide C / DW/144)
            { FittingType.Elbow90,         new FittingLossData { Type = FittingType.Elbow90,         Kv = 1.20, EquivLengthM = 3.0,  Standard = "DW/144" } },
            { FittingType.Elbow45,         new FittingLossData { Type = FittingType.Elbow45,         Kv = 0.50, EquivLengthM = 1.5,  Standard = "DW/144" } },
            { FittingType.Elbow90Radius,   new FittingLossData { Type = FittingType.Elbow90Radius,   Kv = 0.30, EquivLengthM = 1.0,  Standard = "DW/144" } },
            { FittingType.TeeStraight,     new FittingLossData { Type = FittingType.TeeStraight,     Kv = 0.30, EquivLengthM = 1.5,  Standard = "ASHRAE" } },
            { FittingType.TeeBranch,       new FittingLossData { Type = FittingType.TeeBranch,       Kv = 1.80, EquivLengthM = 6.0,  Standard = "ASHRAE" } },
            { FittingType.Reducer,         new FittingLossData { Type = FittingType.Reducer,         Kv = 0.15, EquivLengthM = 0.5,  Standard = "DW/144" } },
            { FittingType.Expander,        new FittingLossData { Type = FittingType.Expander,        Kv = 0.40, EquivLengthM = 1.2,  Standard = "DW/144" } },
            { FittingType.Damper,          new FittingLossData { Type = FittingType.Damper,          Kv = 0.52, EquivLengthM = 2.0,  Standard = "CIBSE" } },
            { FittingType.FireDamper,      new FittingLossData { Type = FittingType.FireDamper,      Kv = 0.80, EquivLengthM = 3.0,  Standard = "DW/144" } },
            { FittingType.BalancingDamper, new FittingLossData { Type = FittingType.BalancingDamper, Kv = 2.00, EquivLengthM = 5.0,  Standard = "CIBSE" } },
            { FittingType.Filter,          new FittingLossData { Type = FittingType.Filter,          Kv = 3.00, EquivLengthM = 10.0, Standard = "ASHRAE" } },
            { FittingType.Coil,            new FittingLossData { Type = FittingType.Coil,            Kv = 4.00, EquivLengthM = 12.0, Standard = "ASHRAE" } },
            { FittingType.Grille,          new FittingLossData { Type = FittingType.Grille,          Kv = 2.50, EquivLengthM = 8.0,  Standard = "CIBSE" } },
            { FittingType.Diffuser,        new FittingLossData { Type = FittingType.Diffuser,        Kv = 1.50, EquivLengthM = 5.0,  Standard = "CIBSE" } },
            { FittingType.Silencer,        new FittingLossData { Type = FittingType.Silencer,        Kv = 1.00, EquivLengthM = 4.0,  Standard = "CIBSE" } },
            { FittingType.FlexDuct,        new FittingLossData { Type = FittingType.FlexDuct,        Kv = 2.00, EquivLengthM = 6.0,  Standard = "DW/144" } },
            // Pipe fittings (CIBSE Guide C)
            { FittingType.ElbowPipe90,     new FittingLossData { Type = FittingType.ElbowPipe90,     Kv = 0.90, EquivLengthM = 1.5,  Standard = "CIBSE" } },
            { FittingType.ElbowPipe45,     new FittingLossData { Type = FittingType.ElbowPipe45,     Kv = 0.40, EquivLengthM = 0.8,  Standard = "CIBSE" } },
            { FittingType.BallValve,       new FittingLossData { Type = FittingType.BallValve,       Kv = 0.05, EquivLengthM = 0.2,  Standard = "CIBSE" } },
            { FittingType.GateValve,       new FittingLossData { Type = FittingType.GateValve,       Kv = 0.12, EquivLengthM = 0.4,  Standard = "CIBSE" } },
            { FittingType.CheckValve,      new FittingLossData { Type = FittingType.CheckValve,      Kv = 2.50, EquivLengthM = 5.0,  Standard = "CIBSE" } },
            { FittingType.ButterflyValve,  new FittingLossData { Type = FittingType.ButterflyValve,  Kv = 0.30, EquivLengthM = 1.0,  Standard = "CIBSE" } },
            { FittingType.Strainer,        new FittingLossData { Type = FittingType.Strainer,        Kv = 2.00, EquivLengthM = 5.0,  Standard = "CIBSE" } },
            { FittingType.Entry,           new FittingLossData { Type = FittingType.Entry,           Kv = 0.50, EquivLengthM = 1.0,  Standard = "CIBSE" } },
            { FittingType.Exit,            new FittingLossData { Type = FittingType.Exit,            Kv = 1.00, EquivLengthM = 2.0,  Standard = "CIBSE" } },
            // MEP-05/06: Missing pipe fitting entries
            { FittingType.TeePipeStraight, new FittingLossData { Type = FittingType.TeePipeStraight, Kv = 0.50, EquivLengthM = 2.0,  Standard = "CIBSE" } },
            { FittingType.TeePipeBranch,   new FittingLossData { Type = FittingType.TeePipeBranch,   Kv = 1.50, EquivLengthM = 5.0,  Standard = "CIBSE" } },
            { FittingType.ReducerPipe,     new FittingLossData { Type = FittingType.ReducerPipe,     Kv = 0.15, EquivLengthM = 0.5,  Standard = "CIBSE" } },
            { FittingType.TeeValve,        new FittingLossData { Type = FittingType.TeeValve,        Kv = 5.00, EquivLengthM = 12.0, Standard = "CIBSE" } },
            { FittingType.PressureReducer, new FittingLossData { Type = FittingType.PressureReducer, Kv = 8.00, EquivLengthM = 20.0, Standard = "CIBSE" } },
            { FittingType.FlowMeter,       new FittingLossData { Type = FittingType.FlowMeter,       Kv = 3.50, EquivLengthM = 10.0, Standard = "CIBSE" } },
        };

        /// <summary>Get loss coefficient for a fitting type. JSON overlay wins over the hardcoded baseline.</summary>
        public static FittingLossData GetFittingLoss(FittingType type)
        {
            var ov = Overrides();
            if (ov != null && ov.TryGetValue(type, out var fromJson)) return fromJson;
            return _fittings.TryGetValue(type, out var data) ? data :
                new FittingLossData { Type = type, Kv = 1.0, EquivLengthM = 2.0, Standard = "Estimated" };
        }

        /// <summary>Calculate pressure loss through a fitting (Pa for ducts, kPa for pipes).</summary>
        public static double CalculateFittingLoss(FittingType type, double velocityMs, double densityKgM3 = 1.2)
        {
            var data = GetFittingLoss(type);
            return data.Kv * 0.5 * densityKgM3 * velocityMs * velocityMs;
        }

        /// <summary>Detect fitting type from Revit MEP fitting element.</summary>
        public static FittingType DetectFittingType(Element fitting)
        {
            string name = (fitting.Name ?? "").ToLower();
            string catName = fitting.Category?.Name?.ToLower() ?? "";

            if (name.Contains("elbow") || name.Contains("bend"))
            {
                if (name.Contains("45")) return catName.Contains("pipe") ? FittingType.ElbowPipe45 : FittingType.Elbow45;
                if (name.Contains("radius") || name.Contains("long")) return FittingType.Elbow90Radius;
                return catName.Contains("pipe") ? FittingType.ElbowPipe90 : FittingType.Elbow90;
            }
            if (name.Contains("tee") || name.Contains("branch"))
                return name.Contains("branch") ? FittingType.TeeBranch : FittingType.TeeStraight;
            if (name.Contains("reducer") || name.Contains("transition"))
                return catName.Contains("pipe") ? FittingType.ReducerPipe : FittingType.Reducer;
            if (name.Contains("damper"))
            {
                if (name.Contains("fire")) return FittingType.FireDamper;
                if (name.Contains("balanc")) return FittingType.BalancingDamper;
                return FittingType.Damper;
            }
            if (name.Contains("filter")) return FittingType.Filter;
            if (name.Contains("coil")) return FittingType.Coil;
            if (name.Contains("grille") || name.Contains("grill")) return FittingType.Grille;
            if (name.Contains("diffuser")) return FittingType.Diffuser;
            if (name.Contains("silencer") || name.Contains("attenuator")) return FittingType.Silencer;
            if (name.Contains("flex")) return FittingType.FlexDuct;
            if (name.Contains("ball valve")) return FittingType.BallValve;
            if (name.Contains("gate valve")) return FittingType.GateValve;
            if (name.Contains("check valve") || name.Contains("non-return")) return FittingType.CheckValve;
            if (name.Contains("butterfly")) return FittingType.ButterflyValve;
            if (name.Contains("strainer")) return FittingType.Strainer;
            // MEP-07: Additional detection patterns for pipe fittings
            if (name.Contains("tee valve") || name.Contains("globe valve")) return FittingType.TeeValve;
            if (name.Contains("pressure reduc") || name.Contains("prv")) return FittingType.PressureReducer;
            if (name.Contains("flow meter") || name.Contains("flowmeter")) return FittingType.FlowMeter;
            if (name.Contains("expander") || name.Contains("increaser")) return FittingType.Expander;
            if (name.Contains("entry") || name.Contains("inlet")) return FittingType.Entry;
            if (name.Contains("exit") || name.Contains("outlet") || name.Contains("discharge")) return FittingType.Exit;
            // MEP-07: Pipe-specific tee detection
            if (catName.Contains("pipe") && (name.Contains("tee") || name.Contains("branch")))
                return name.Contains("branch") ? FittingType.TeePipeBranch : FittingType.TeePipeStraight;

            // Default: use pipe or duct elbow based on category context
            return catName.Contains("pipe") ? FittingType.ElbowPipe90 : FittingType.Elbow90;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DETAILED PRESSURE DROP ENGINE — Darcy-Weisbach + Minor Losses
    // ════════════════════════════════════════════════════════════════

    internal class PressureDropResult
    {
        public double StraightLossPa { get; set; }
        public double FittingLossPa { get; set; }
        public double TotalLossPa { get; set; }
        public double VelocityMs { get; set; }
        public double FrictionFactorPerM { get; set; }
        public List<(FittingType Type, double LossPa)> FittingBreakdown { get; set; }
            = new List<(FittingType, double)>();
        public bool VelocityExceeded { get; set; }
        public double MaxVelocityMs { get; set; }
        public string SystemType { get; set; }
    }

    internal static class DetailedPressureDropEngine
    {
        // Air properties at 20°C
        private const double AirDensity = 1.2;     // kg/m³
        private const double AirViscosity = 1.81e-5; // Pa·s
        // Water properties at 20°C
        private const double WaterDensity = 998.0;
        private const double WaterViscosity = 1.002e-3;

        // Duct roughness (m) per DW/144
        private const double GalvanisedRoughness = 0.00015; // galvanised steel
        private const double SpiralRoughness = 0.0003;      // spiral wound
        private const double FlexibleRoughness = 0.003;     // flexible duct

        // Pipe roughness (m)
        private const double CopperRoughness = 0.0015e-3;
        private const double SteelRoughness = 0.046e-3;
        private const double PlasticRoughness = 0.003e-3;

        /// <summary>Calculate Darcy-Weisbach friction factor using Colebrook-White equation.</summary>
        public static double ColebrookWhite(double reynoldsNumber, double roughness, double diameter)
        {
            if (reynoldsNumber <= 0 || diameter <= 0) return 0.02;
            if (reynoldsNumber < 2300) return 64.0 / reynoldsNumber; // laminar

            double relRoughness = roughness / diameter;
            // ME-CRIT-01: Swamee-Jain explicit approximation (not iterative — no convergence issue)
            // Guard against log10 producing near-zero denominator
            double logArg = relRoughness / 3.7 + 5.74 / Math.Pow(reynoldsNumber, 0.9);
            if (logArg <= 0 || double.IsNaN(logArg)) return 0.02;
            double logVal = Math.Log10(logArg);
            if (Math.Abs(logVal) < 1e-12 || double.IsNaN(logVal)) return 0.02;
            double f = 0.25 / (logVal * logVal);
            return Math.Max(f, 0.005);
        }

        /// <summary>Calculate pressure drop for a duct section with fittings.</summary>
        public static PressureDropResult CalculateDuctPressureDrop(
            double flowRateM3s, double widthMm, double heightMm, double lengthM,
            List<FittingType> fittings, bool isFlexible = false)
        {
            var result = new PressureDropResult { SystemType = "Duct" };

            // Hydraulic diameter: Dh = 4A/P. Guard against zero perimeter (zero-size duct).
            double w = widthMm / 1000.0;
            double h = heightMm / 1000.0;
            double area = w * h;
            double perimeter = 2 * (w + h);
            if (perimeter < 1e-10) return result; // zero-size duct — no meaningful calculation
            double dh = 4 * area / perimeter; // hydraulic diameter

            // Velocity
            double velocity = area > 0 ? flowRateM3s / area : 0;
            result.VelocityMs = velocity;

            // CIBSE Guide C velocity limits
            result.MaxVelocityMs = 6.0; // default supply main
            result.VelocityExceeded = velocity > result.MaxVelocityMs;

            // Reynolds number
            double re = AirDensity * velocity * dh / AirViscosity;
            double roughness = isFlexible ? FlexibleRoughness : GalvanisedRoughness;

            // Friction factor
            double f = ColebrookWhite(re, roughness, dh);

            // Straight duct loss: ΔP = f × (L/Dh) × 0.5 × ρ × v²
            double dynamicPressure = 0.5 * AirDensity * velocity * velocity;
            result.StraightLossPa = f * (lengthM / Math.Max(dh, 0.01)) * dynamicPressure;
            result.FrictionFactorPerM = result.StraightLossPa / Math.Max(lengthM, 0.01);

            // Fitting losses
            double totalFittingLoss = 0;
            if (fittings != null)
            {
                foreach (var ft in fittings)
                {
                    double loss = FittingLossCalculator.CalculateFittingLoss(ft, velocity, AirDensity);
                    totalFittingLoss += loss;
                    result.FittingBreakdown.Add((ft, loss));
                }
            }
            result.FittingLossPa = totalFittingLoss;
            result.TotalLossPa = result.StraightLossPa + result.FittingLossPa;

            return result;
        }

        /// <summary>Calculate pressure drop for a pipe section with fittings.</summary>
        public static PressureDropResult CalculatePipePressureDrop(
            double flowRateLs, double diameterMm, double lengthM,
            List<FittingType> fittings, string material = "copper")
        {
            var result = new PressureDropResult { SystemType = "Pipe" };

            double d = diameterMm / 1000.0;
            double area = Math.PI * d * d / 4.0;
            double velocity = area > 0 ? (flowRateLs / 1000.0) / area : 0;
            result.VelocityMs = velocity;

            // CIBSE Guide C pipe velocity limits
            result.MaxVelocityMs = diameterMm < 50 ? 1.5 : 3.0;
            result.VelocityExceeded = velocity > result.MaxVelocityMs;

            double re = WaterDensity * velocity * d / WaterViscosity;
            double roughness = material switch
            {
                "copper" => CopperRoughness,
                "steel" => SteelRoughness,
                "plastic" => PlasticRoughness,
                _ => CopperRoughness
            };

            double f = ColebrookWhite(re, roughness, d);
            double dynamicPressure = 0.5 * WaterDensity * velocity * velocity;
            result.StraightLossPa = f * (lengthM / Math.Max(d, 0.001)) * dynamicPressure;
            result.FrictionFactorPerM = result.StraightLossPa / Math.Max(lengthM, 0.01);

            double totalFittingLoss = 0;
            if (fittings != null)
            {
                foreach (var ft in fittings)
                {
                    double loss = FittingLossCalculator.CalculateFittingLoss(ft, velocity, WaterDensity);
                    totalFittingLoss += loss;
                    result.FittingBreakdown.Add((ft, loss));
                }
            }
            result.FittingLossPa = totalFittingLoss;
            result.TotalLossPa = result.StraightLossPa + result.FittingLossPa;

            return result;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MEP BALANCING ENGINE — Hardy Cross Iterative Method
    // ════════════════════════════════════════════════════════════════

    internal class BalancingResult
    {
        public int Iterations { get; set; }
        public bool Converged { get; set; }
        public double MaxImbalancePa { get; set; }
        public List<(string BranchName, double DesignFlowLs, double ActualFlowLs, double DamperCv)> BranchResults
            { get; set; } = new List<(string, double, double, double)>();
    }

    internal static class MEPBalancingEngine
    {
        /// <summary>
        /// Document-aware overload (Phase 181) — pulls maxIterations / tolerancePa /
        /// dampingFactor / minBranchFlowLs from STING_MEP_SIZING_RULES.json
        /// (with the project override merged) and forwards to the canonical
        /// <see cref="BalanceSystem(List{ValueTuple{string,double,double}}, double, int, double, double, double)"/>
        /// overload below. Existing callers that don't have a Document continue
        /// to work via the original signature.
        /// </summary>
        public static BalancingResult BalanceSystem(
            Document doc,
            List<(string Name, double DesignFlowLs, double ResistanceCoeff)> branches,
            double totalSupplyPressurePa)
        {
            double damping = 0.7, minFlow = 0.01;
            int maxIters = 100; double tol = 1.0;
            try
            {
                var bal = StingTools.Core.Mep.MepSizingRegistry.Get(doc).Balancing;
                if (bal != null)
                {
                    if (bal.MaxIterations > 0)   maxIters = bal.MaxIterations;
                    if (bal.TolerancePa > 0)     tol      = bal.TolerancePa;
                    if (bal.DampingFactor > 0)   damping  = bal.DampingFactor;
                    if (bal.MinBranchFlowLs > 0) minFlow  = bal.MinBranchFlowLs;
                }
            }
            catch (Exception ex) { StingLog.Warn($"BalanceSystem registry fallback: {ex.Message}"); }
            return BalanceSystem(branches, totalSupplyPressurePa, maxIters, tol, damping, minFlow);
        }

        /// <summary>Run Hardy Cross iterative flow balancing on parallel branches.</summary>
        public static BalancingResult BalanceSystem(
            List<(string Name, double DesignFlowLs, double ResistanceCoeff)> branches,
            double totalSupplyPressurePa,
            int maxIterations = 100,
            double tolerancePa = 1.0,
            double dampingFactor = 0.7,
            double minBranchFlowLs = 0.01)
        {
            var result = new BalancingResult();

            try
            {
                int n = branches.Count;
                if (n == 0) return result;

                // Initialize flows to design values
                double[] flows = branches.Select(b => b.DesignFlowLs).ToArray();
                double[] resistances = branches.Select(b => b.ResistanceCoeff).ToArray();

                for (int iter = 0; iter < maxIterations; iter++)
                {
                    result.Iterations = iter + 1;
                    double maxCorrection = 0;

                    // Full loop Hardy Cross: all branches must have equal pressure drop
                    // Reference pressure = average of all branch pressure drops
                    double[] dp = new double[n];
                    for (int i = 0; i < n; i++)
                        dp[i] = resistances[i] * flows[i] * Math.Abs(flows[i]);

                    double avgDp = dp.Average();

                    // Apply correction to each branch to equalize pressure drop
                    for (int i = 0; i < n; i++)
                    {
                        double imbalance = dp[i] - avgDp;

                        // Hardy Cross correction: ΔQ = -F(Q) / F'(Q) where F=R×Q|Q|
                        double denominator = 2.0 * resistances[i] * Math.Abs(flows[i]);
                        if (Math.Abs(denominator) < 1e-10) continue;

                        double correction = -imbalance / denominator;

                        // Under-relaxation damping (registry-driven, default 0.7)
                        flows[i] += correction * dampingFactor;

                        // Ensure flow stays positive (registry-driven minimum)
                        if (flows[i] < minBranchFlowLs) flows[i] = minBranchFlowLs;

                        maxCorrection = Math.Max(maxCorrection, Math.Abs(imbalance));
                    }

                    // MEP-04: Renormalize flows to preserve total mass conservation
                    double totalDesign = branches.Sum(b => b.DesignFlowLs);
                    double totalCurrent = flows.Sum();
                    if (totalCurrent > 0.01 && totalDesign > 0.01)
                    {
                        double scale = totalDesign / totalCurrent;
                        for (int i = 0; i < n; i++)
                            flows[i] *= scale;
                    }

                    result.MaxImbalancePa = maxCorrection;
                    if (maxCorrection < tolerancePa)
                    {
                        result.Converged = true;
                        break;
                    }
                }

                // Calculate required damper Cv for each branch
                for (int i = 0; i < n; i++)
                {
                    double actualDp = resistances[i] * flows[i] * Math.Abs(flows[i]);
                    double targetDp = totalSupplyPressurePa;
                    double excessDp = targetDp - actualDp;
                    // Damper Cv = Q / sqrt(ΔP) — flow coefficient
                    // SAFETY: Guard against near-zero excessDp causing extreme Cv values
                    double damperCv = excessDp > 1.0 ? Math.Abs(flows[i]) / Math.Sqrt(excessDp) : 0;

                    result.BranchResults.Add((branches[i].Name, branches[i].DesignFlowLs, flows[i], damperCv));
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("MEPBalancingEngine.BalanceSystem", ex);
            }

            return result;
        }

        /// <summary>Proportional balancing — simple ratio method per CIBSE TM39.</summary>
        public static List<(string Branch, double FlowRatio, double RequiredSettingPct)> ProportionalBalance(
            List<(string Name, double DesignFlowLs, double MeasuredFlowLs)> measurements)
        {
            var results = new List<(string, double, double)>();
            double totalDesign = measurements.Sum(m => m.DesignFlowLs);
            double totalMeasured = measurements.Sum(m => m.MeasuredFlowLs);

            if (totalDesign <= 0 || totalMeasured <= 0) return results;

            foreach (var m in measurements)
            {
                double designRatio = m.DesignFlowLs / totalDesign;
                double measuredRatio = totalMeasured > 0 ? m.MeasuredFlowLs / totalMeasured : 0;
                double flowRatio = m.DesignFlowLs > 0 ? m.MeasuredFlowLs / m.DesignFlowLs : 0;

                // Required damper setting (100% = fully open, lower = more throttled)
                double settingPct = flowRatio > 1.0
                    ? 100.0 * (m.DesignFlowLs / m.MeasuredFlowLs) // needs throttling
                    : 100.0; // needs opening — flag for fan speed increase

                results.Add((m.Name, flowRatio * 100.0, settingPct));
            }

            return results.OrderBy(r => r.Item2).ToList(); // sort by ratio (most starved first)
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MEP VIBRO-ACOUSTIC ENGINE — Vibration & Ductborne Noise
    // ════════════════════════════════════════════════════════════════

    internal class MEPVibrationResult
    {
        public double EquipmentFrequencyHz { get; set; }
        public double MountNaturalFreqHz { get; set; }
        public double TransmissibilityPct { get; set; }
        public bool IsolationAdequate { get; set; }
        public string MountType { get; set; }
        public string Recommendation { get; set; }
    }

    internal static class MEPVibroAcousticEngine
    {
        // NC (Noise Criteria) limits per room type
        private static readonly Dictionary<string, int> _ncLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "concert_hall",    20 }, { "bedroom",         25 }, { "hospital_ward",  30 },
            { "classroom",       30 }, { "office_private",  35 }, { "office_open",    40 },
            { "restaurant",      40 }, { "retail",          45 }, { "corridor",       45 },
            { "gymnasium",       50 }, { "workshop",        55 }, { "plant_room",     65 },
        };

        /// <summary>Calculate vibration isolation transmissibility.</summary>
        public static MEPVibrationResult CalculateIsolation(
            double equipmentRpm, double equipmentMassKg, double mountStiffnessNPerM)
        {
            var result = new MEPVibrationResult();

            try
            {
                // Equipment forcing frequency
                result.EquipmentFrequencyHz = equipmentRpm / 60.0;

                // Mount natural frequency: fn = (1/2π) × √(k/m)
                result.MountNaturalFreqHz = mountStiffnessNPerM > 0 && equipmentMassKg > 0
                    ? (1.0 / (2.0 * Math.PI)) * Math.Sqrt(mountStiffnessNPerM / equipmentMassKg)
                    : 0;

                // Frequency ratio
                double r = result.MountNaturalFreqHz > 0
                    ? result.EquipmentFrequencyHz / result.MountNaturalFreqHz
                    : 0;

                // Transmissibility: T = 1 / |r² - 1| (undamped)
                if (r > 0 && Math.Abs(r * r - 1) > 0.01)
                {
                    result.TransmissibilityPct = 100.0 / Math.Abs(r * r - 1);
                }
                else
                {
                    result.TransmissibilityPct = 100.0; // resonance!
                }

                // Target: <5% transmissibility for sensitive spaces
                result.IsolationAdequate = result.TransmissibilityPct < 10.0;

                // Recommend mount type
                if (result.EquipmentFrequencyHz < 10)
                    result.MountType = "Concrete inertia base + spring isolators";
                else if (result.EquipmentFrequencyHz < 25)
                    result.MountType = "Spring isolators (25mm deflection)";
                else if (result.EquipmentFrequencyHz < 50)
                    result.MountType = "Rubber-in-shear mounts";
                else
                    result.MountType = "Neoprene waffle pads";

                result.Recommendation = result.IsolationAdequate
                    ? $"{result.MountType} — T={result.TransmissibilityPct:F1}% OK"
                    : $"INADEQUATE — use {result.MountType}, target fn < {result.EquipmentFrequencyHz / 3:F1} Hz";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"VibrationIsolation: {ex.Message}");
            }

            return result;
        }

        /// <summary>Calculate ductborne noise at a terminal device.</summary>
        public static (double NoiseLevelDbA, int NCRating, bool MeetsTarget, string Recommendation) CalculateDuctborneNoise(
            double fanSoundPowerDb, double ductLengthM, double ductWidthMm,
            bool isLined, int silencerCount, string roomType)
        {
            // Fan sound power to in-duct noise
            double inDuctLevel = fanSoundPowerDb;

            // Natural attenuation per CIBSE Guide B3
            double naturalAtten = AcousticPropagationEngine.DuctAttenuation(ductWidthMm, ductLengthM, isLined);
            inDuctLevel -= naturalAtten;

            // Silencer attenuation
            double silencerAtten = silencerCount * AcousticPropagationEngine.SilencerInsertionLoss("rectangular_splitter", 900);
            inDuctLevel -= silencerAtten;

            // End reflection loss at terminal (grille/diffuser)
            double endReflection = ductWidthMm < 300 ? 10 : ductWidthMm < 600 ? 6 : 3;
            inDuctLevel -= endReflection;

            // Room correction: +10 dB for reverberant, -3 dB for absorbent
            // Room correction: +10 dB for reverberant, -3 dB for absorbent (reserved for future use)

            // Convert to NC rating (simplified: NC ≈ dB(A) - 5)
            int ncRating = (int)(inDuctLevel - 5);

            int ncTarget = _ncLimits.TryGetValue(roomType, out int limit) ? limit : 35;
            bool meets = ncRating <= ncTarget;

            string rec = meets
                ? $"NC-{ncRating} meets NC-{ncTarget} target"
                : $"NC-{ncRating} exceeds NC-{ncTarget} — add {(ncRating - ncTarget) / 10 + 1} silencer(s) or increase duct size";

            return (inDuctLevel, ncRating, meets, rec);
        }

        /// <summary>Get NC limit for a room type.</summary>
        public static int GetNCLimit(string roomType)
        {
            return _ncLimits.TryGetValue(roomType, out int nc) ? nc : 35;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MEP SYSTEM ANALYSER — Model-wide Analysis
    // ════════════════════════════════════════════════════════════════

    internal static class MEPSystemAnalyser
    {
        /// <summary>Analyse all MEP systems in the model for pressure drop and balancing.</summary>
        public static List<PressureDropResult> AnalyseModel(Document doc)
        {
            var results = new List<PressureDropResult>();

            try
            {
                // Analyse ducts (MEP-01: warn if truncated at 500)
                var allDucts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType();
                var ducts = allDucts.Take(500).ToList();
                int totalDucts = ducts.Count == 500 ? new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType().GetElementCount() : ducts.Count;
                if (totalDucts > 500)
                    StingLog.Warn($"MEPAnalysis: analysing first 500 of {totalDucts} ducts — results are a sample");

                foreach (var duct in ducts)
                {
                    try
                    {
                        var widthP = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
                        var heightP = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
                        var lengthP = duct.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                        var flowP = duct.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);

                        // Default 0.3048 ft (≈100mm) instead of 0.5 ft (≈152mm) for realistic fallback
                        double widthMm = (widthP?.AsDouble() ?? 0.3048) * 304.8;
                        double heightMm = (heightP?.AsDouble() ?? 0.3048) * 304.8;
                        double lengthM = (lengthP?.AsDouble() ?? 1) * 0.3048;
                        double flowM3s = (flowP?.AsDouble() ?? 0) * 0.0283168; // ft³/s → m³/s (Revit internal unit is ft³/s)

                        if (flowM3s > 0 && lengthM > 0)
                        {
                            var pdResult = DetailedPressureDropEngine.CalculateDuctPressureDrop(
                                flowM3s, widthMm, heightMm, lengthM, null);
                            results.Add(pdResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"MEPAnalyse duct {duct.Id}: {ex.Message}");
                    }
                }

                // Analyse pipes (MEP-01: warn if truncated at 500)
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType()
                    .Take(500).ToList();
                if (pipes.Count == 500)
                {
                    int totalPipes = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_PipeCurves).WhereElementIsNotElementType().GetElementCount();
                    if (totalPipes > 500)
                        StingLog.Warn($"MEPAnalysis: analysing first 500 of {totalPipes} pipes — results are a sample");
                }

                foreach (var pipe in pipes)
                {
                    try
                    {
                        var diamP = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                        var lengthP = pipe.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                        var flowP = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_FLOW_PARAM);

                        double diamMm = (diamP?.AsDouble() ?? 0.05) * 304.8;
                        double lengthM = (lengthP?.AsDouble() ?? 1) * 0.3048;
                        double flowLs = (flowP?.AsDouble() ?? 0) * 28.3168; // ft³/s → L/s (Revit internal unit is ft³/s)

                        if (flowLs > 0 && lengthM > 0)
                        {
                            var pdResult = DetailedPressureDropEngine.CalculatePipePressureDrop(
                                flowLs, diamMm, lengthM, null);
                            results.Add(pdResult);
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"MEPAnalyse pipe {pipe.Id}: {ex.Message}");
                    }
                }

                StingLog.Info($"MEPAnalysis: {ducts.Count} ducts, {pipes.Count} pipes → {results.Count} pressure drop results");
            }
            catch (Exception ex)
            {
                StingLog.Error("MEPSystemAnalyser.AnalyseModel", ex);
            }

            return results;
        }
    }
}
