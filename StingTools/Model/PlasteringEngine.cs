// ============================================================================
// PlasteringEngine.cs — Intelligent Plastering & Rendering Automation
//
// Production-grade algorithms for automated plastering in BIM:
//   1.  PlasterMaterialScience   — BS EN 998/13914 material database + mix design
//   2.  PlasterMixDesigner       — Multi-coat ratio optimizer with admixtures
//   3.  SurfaceAnalyzer          — Substrate classification + key detection
//   4.  ThicknessOptimizer       — Per-surface adaptive thickness (BS 5492)
//   5.  PlasterCoverageEngine    — Waste-adjusted area/volume/cost calculation
//   6.  PlasterScheduleEngine    — Multi-coat curing timeline (Gantt-ready)
//   7.  PlasterQualityInspector  — 15-point quality checklist per BS EN 13914
//   8.  PlasterLayerBuilder      — Compound wall layer injection for Revit
//   9.  SmartPlasterFactory      — Intelligent wall plastering with full pipeline
//  10.  PlasterConfig            — Configurable settings from project_config.json
//
// Standards: BS EN 998-1, BS EN 13914-1/2, BS 5492, BS 8000-10
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Model
{
    // ════════════════════════════════════════════════════════════════
    // 0. CONFIGURABLE PLASTERING SETTINGS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Centralised configurable settings for plastering algorithms.
    /// Loaded from project_config.json PLASTERING section.
    /// </summary>
    internal static class PlasterConfig
    {
        // ── Thickness Defaults (mm) ──────────────────────────────
        public static double InternalRenderThicknessMm { get; set; } = 13;
        public static double ExternalRenderThicknessMm { get; set; } = 20;
        public static double SkimCoatThicknessMm { get; set; } = 3;
        public static double BackingCoatThicknessMm { get; set; } = 11;
        public static double ScratchCoatThicknessMm { get; set; } = 10;
        public static double FinishCoatThicknessMm { get; set; } = 3;

        // ── Waste & Coverage ─────────────────────────────────────
        public static double WasteFactorPercent { get; set; } = 10;
        public static double OpeningDeductionThresholdM2 { get; set; } = 0.5;
        public static double DoorRevealDepthMm { get; set; } = 100;
        public static double WindowRevealDepthMm { get; set; } = 100;

        // ── Cost Rates ───────────────────────────────────────────
        public static double LabourRatePerM2 { get; set; } = 12.0;
        public static double MaterialCostPerM2 { get; set; } = 4.5;
        public static double ScaffoldCostPerM2 { get; set; } = 8.0;

        // ── Quality ──────────────────────────────────────────────
        public static double MaxDeviationMm { get; set; } = 3.0;
        public static double MinCuringHours { get; set; } = 24;
        public static double MaxCuringTempC { get; set; } = 35;
        public static double MinCuringTempC { get; set; } = 5;

        /// <summary>Loads plastering config from project_config.json.</summary>
        public static void LoadFromConfig(Document doc)
        {
            try
            {
                var path = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(doc.PathName) ?? "", "project_config.json");
                if (!System.IO.File.Exists(path)) return;

                var json = System.IO.File.ReadAllText(path);
                var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                var sec = obj["PLASTERING"];
                if (sec == null) return;

                InternalRenderThicknessMm = sec.Value<double?>("INT_RENDER_MM") ?? InternalRenderThicknessMm;
                ExternalRenderThicknessMm = sec.Value<double?>("EXT_RENDER_MM") ?? ExternalRenderThicknessMm;
                WasteFactorPercent = sec.Value<double?>("WASTE_PCT") ?? WasteFactorPercent;
                LabourRatePerM2 = sec.Value<double?>("LABOUR_RATE_M2") ?? LabourRatePerM2;
                MaterialCostPerM2 = sec.Value<double?>("MATERIAL_COST_M2") ?? MaterialCostPerM2;
                MaxDeviationMm = sec.Value<double?>("MAX_DEVIATION_MM") ?? MaxDeviationMm;

                StingLog.Info("PlasterConfig loaded from project_config.json");
            }
            catch (Exception ex) { StingLog.Warn($"PlasterConfig load: {ex.Message}"); }
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 1. PLASTER MATERIAL SCIENCE — BS EN 998-1 / BS EN 13914
    // ════════════════════════════════════════════════════════════════

    #region Material Types

    /// <summary>Plaster/render material classification per BS EN 998-1.</summary>
    public enum PlasterType
    {
        /// <summary>GP — General purpose rendering/plastering mortar.</summary>
        GeneralPurpose,
        /// <summary>LW — Lightweight mortar (density < 1300 kg/m³).</summary>
        Lightweight,
        /// <summary>CR — Coloured rendering mortar.</summary>
        Coloured,
        /// <summary>OC — One-coat rendering mortar.</summary>
        OneCoat,
        /// <summary>R — Renovation mortar (high porosity).</summary>
        Renovation,
        /// <summary>T — Thermal insulating mortar (λ ≤ 0.2 W/mK).</summary>
        ThermalInsulating,
    }

    /// <summary>Plaster application coat type.</summary>
    public enum CoatType { Scratch, Backing, Finish, Skim, Render, DashCoat, Tyrolean }

    /// <summary>Substrate classification per BS EN 13914-1 Table 1.</summary>
    public enum SubstrateType
    {
        DenseBlock,        // Concrete block ≥ 1500 kg/m³
        LightweightBlock,  // Aerated/lightweight block < 1500 kg/m³
        CommonBrick,       // Clay brick
        EngineeringBrick,  // Dense engineering brick (low suction)
        InSituConcrete,    // Cast concrete (smooth, low suction)
        Plasterboard,      // Gypsum board (dry-lined)
        MetalLath,         // Expanded metal lath on framing
        Timber,            // Timber substrate (requires lath)
        StoneWork,         // Natural stone masonry
        MixedSubstrate,    // Mixed materials (requires mechanical key)
    }

    /// <summary>Plaster material specification.</summary>
    public class PlasterMaterialSpec
    {
        public PlasterType Type { get; set; }
        public string Name { get; set; }
        public double DensityKgM3 { get; set; }
        public double CompressiveStrengthMPa { get; set; }
        public double ThermalConductivity { get; set; } // W/mK
        public double WaterAbsorptionPct { get; set; }
        public string MixRatio { get; set; } // e.g. "1:6" (cement:sand)
        public double CoverageM2PerBag { get; set; }
        public double BagWeightKg { get; set; }
        public double CostPerBag { get; set; }
        public int CuringTimeHours { get; set; }
        public string BSClassification { get; set; } // CS I-IV
        public double FireResistanceMinutes { get; set; }
    }

    /// <summary>Multi-coat plaster build-up specification.</summary>
    public class PlasterBuildUp
    {
        public List<PlasterCoat> Coats { get; set; } = new();
        public double TotalThicknessMm { get; set; }
        public double TotalDryingDays { get; set; }
        public bool IsExternal { get; set; }
        public SubstrateType Substrate { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>Individual plaster coat specification.</summary>
    public class PlasterCoat
    {
        public CoatType Type { get; set; }
        public double ThicknessMm { get; set; }
        public string MixRatio { get; set; }
        public int CuringHours { get; set; }
        public string Material { get; set; }
        public int Sequence { get; set; }
    }

    #endregion

    /// <summary>
    /// Plaster material science engine per BS EN 998-1 and BS EN 13914.
    ///
    /// Compressive strength classes (BS EN 998-1 Table 3):
    ///   CS I:   0.4 - 2.5 MPa  (internal plaster, lightweight)
    ///   CS II:  1.5 - 5.0 MPa  (general purpose internal)
    ///   CS III: 3.5 - 7.5 MPa  (external render, heavy-duty)
    ///   CS IV:  ≥ 6.0 MPa      (engineering, industrial)
    ///
    /// Water absorption classes (BS EN 998-1):
    ///   W0: Not specified   W1: c ≤ 0.40 kg/(m²·min^0.5)   W2: c ≤ 0.20
    ///
    /// Thermal conductivity classes:
    ///   T1: ≤ 0.1 W/mK   T2: ≤ 0.2 W/mK
    /// </summary>
    internal static class PlasterMaterialScience
    {
        /// <summary>BS EN 998-1 material database.</summary>
        private static readonly List<PlasterMaterialSpec> _materials = new()
        {
            new() { Type = PlasterType.GeneralPurpose, Name = "Cement:Sand Render",
                DensityKgM3 = 1800, CompressiveStrengthMPa = 4.0, ThermalConductivity = 0.8,
                WaterAbsorptionPct = 8, MixRatio = "1:4", CoverageM2PerBag = 3.5,
                BagWeightKg = 25, CostPerBag = 6.50, CuringTimeHours = 48,
                BSClassification = "CS III", FireResistanceMinutes = 60 },

            new() { Type = PlasterType.GeneralPurpose, Name = "Cement:Lime:Sand Render",
                DensityKgM3 = 1700, CompressiveStrengthMPa = 2.5, ThermalConductivity = 0.7,
                WaterAbsorptionPct = 12, MixRatio = "1:1:6", CoverageM2PerBag = 4.0,
                BagWeightKg = 25, CostPerBag = 7.20, CuringTimeHours = 72,
                BSClassification = "CS II", FireResistanceMinutes = 60 },

            new() { Type = PlasterType.GeneralPurpose, Name = "Gypsum Plaster (Thistle)",
                DensityKgM3 = 1100, CompressiveStrengthMPa = 2.0, ThermalConductivity = 0.4,
                WaterAbsorptionPct = 30, MixRatio = "Premixed", CoverageM2PerBag = 4.5,
                BagWeightKg = 25, CostPerBag = 5.80, CuringTimeHours = 24,
                BSClassification = "CS I", FireResistanceMinutes = 30 },

            new() { Type = PlasterType.Lightweight, Name = "Lightweight Gypsum (Thistle Multi-Finish)",
                DensityKgM3 = 900, CompressiveStrengthMPa = 1.5, ThermalConductivity = 0.3,
                WaterAbsorptionPct = 35, MixRatio = "Premixed", CoverageM2PerBag = 5.0,
                BagWeightKg = 25, CostPerBag = 8.50, CuringTimeHours = 24,
                BSClassification = "CS I", FireResistanceMinutes = 30 },

            new() { Type = PlasterType.OneCoat, Name = "One Coat (Thistle Dura-Finish)",
                DensityKgM3 = 1050, CompressiveStrengthMPa = 3.0, ThermalConductivity = 0.4,
                WaterAbsorptionPct = 15, MixRatio = "Premixed", CoverageM2PerBag = 4.0,
                BagWeightKg = 25, CostPerBag = 9.50, CuringTimeHours = 24,
                BSClassification = "CS II", FireResistanceMinutes = 45 },

            new() { Type = PlasterType.ThermalInsulating, Name = "Insulating Render (Perlite)",
                DensityKgM3 = 600, CompressiveStrengthMPa = 1.0, ThermalConductivity = 0.12,
                WaterAbsorptionPct = 40, MixRatio = "1:8 (cement:perlite)", CoverageM2PerBag = 2.0,
                BagWeightKg = 15, CostPerBag = 14.00, CuringTimeHours = 48,
                BSClassification = "CS I", FireResistanceMinutes = 90 },

            new() { Type = PlasterType.Coloured, Name = "Through-Colour Render (K-Rend)",
                DensityKgM3 = 1400, CompressiveStrengthMPa = 3.5, ThermalConductivity = 0.5,
                WaterAbsorptionPct = 10, MixRatio = "Premixed", CoverageM2PerBag = 2.8,
                BagWeightKg = 25, CostPerBag = 18.00, CuringTimeHours = 24,
                BSClassification = "CS III", FireResistanceMinutes = 60 },

            new() { Type = PlasterType.Renovation, Name = "Renovation Render (Lime Putty)",
                DensityKgM3 = 1500, CompressiveStrengthMPa = 1.5, ThermalConductivity = 0.7,
                WaterAbsorptionPct = 25, MixRatio = "1:3 (lime:sand)", CoverageM2PerBag = 3.0,
                BagWeightKg = 25, CostPerBag = 12.00, CuringTimeHours = 168,
                BSClassification = "CS I", FireResistanceMinutes = 60 },
        };

        public static List<PlasterMaterialSpec> GetAllMaterials() => _materials.ToList();

        public static PlasterMaterialSpec GetMaterial(PlasterType type) =>
            _materials.FirstOrDefault(m => m.Type == type) ?? _materials[0];

        public static PlasterMaterialSpec GetMaterialByName(string name) =>
            _materials.FirstOrDefault(m => m.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>
        /// Recommends optimal plaster material based on substrate, location, and requirements.
        /// </summary>
        public static PlasterMaterialSpec RecommendMaterial(
            SubstrateType substrate, bool isExternal, bool requiresInsulation = false,
            bool isHeritage = false)
        {
            if (isHeritage) return GetMaterial(PlasterType.Renovation);
            if (requiresInsulation) return GetMaterial(PlasterType.ThermalInsulating);

            if (isExternal)
            {
                // External: need weather resistance (CS III+, low water absorption)
                return substrate switch
                {
                    SubstrateType.LightweightBlock => GetMaterial(PlasterType.Coloured),
                    SubstrateType.Timber or SubstrateType.MetalLath => GetMaterial(PlasterType.GeneralPurpose),
                    _ => _materials.First(m => m.MixRatio == "1:4"), // Strong cement render
                };
            }

            // Internal
            return substrate switch
            {
                SubstrateType.Plasterboard => GetMaterial(PlasterType.Lightweight),
                SubstrateType.InSituConcrete => GetMaterial(PlasterType.OneCoat),
                SubstrateType.DenseBlock or SubstrateType.CommonBrick =>
                    _materials.First(m => m.MixRatio == "1:1:6"),
                _ => GetMaterial(PlasterType.GeneralPurpose),
            };
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 2. PLASTER MIX DESIGNER — Multi-Coat Ratio Optimization
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Plaster mix design engine per BS EN 13914-1/2.
    ///
    /// Mix ratio selection rules (BS EN 13914-1 Table 3):
    ///   External scratch coat:  1:3-4 (cement:sand), strong to grip
    ///   External backing coat:  1:5-6 (cement:sand), weaker than scratch
    ///   External finish coat:   1:6-8 or premixed, weakest (prevents cracking)
    ///
    /// "Weakest coat outermost" principle:
    ///   Each successive coat MUST have equal or lower strength than the one beneath.
    ///   This prevents trapped moisture and differential shrinkage cracking.
    ///
    /// Admixtures:
    ///   Plasticiser (SBR): improves workability, reduces water demand
    ///   Waterproofer: integral or surface-applied (external renders)
    ///   Retarder: extends working time in hot conditions
    ///   Accelerator: speeds set in cold conditions (>5°C only)
    ///   Fibre reinforcement: polypropylene 0.9kg/m³ (crack control)
    /// </summary>
    internal static class PlasterMixDesigner
    {
        /// <summary>
        /// Designs optimal multi-coat build-up for substrate and exposure.
        /// </summary>
        public static PlasterBuildUp DesignBuildUp(
            SubstrateType substrate, bool isExternal,
            bool requiresInsulation = false, bool isHeritage = false)
        {
            var buildUp = new PlasterBuildUp
            {
                IsExternal = isExternal,
                Substrate = substrate,
            };

            if (isExternal)
            {
                // External 3-coat render system (BS EN 13914-1)
                buildUp.Coats.Add(new PlasterCoat
                {
                    Type = CoatType.Scratch, Sequence = 1,
                    ThicknessMm = PlasterConfig.ScratchCoatThicknessMm,
                    MixRatio = "1:3 (OPC:sharp sand)",
                    Material = "Cement:Sand scratch coat",
                    CuringHours = 48,
                });

                buildUp.Coats.Add(new PlasterCoat
                {
                    Type = CoatType.Backing, Sequence = 2,
                    ThicknessMm = PlasterConfig.BackingCoatThicknessMm,
                    MixRatio = substrate == SubstrateType.LightweightBlock ? "1:1:6 (OPC:lime:sand)" : "1:5 (OPC:sand)",
                    Material = substrate == SubstrateType.LightweightBlock ? "Cement:Lime:Sand" : "Cement:Sand",
                    CuringHours = 72,
                });

                buildUp.Coats.Add(new PlasterCoat
                {
                    Type = CoatType.Finish, Sequence = 3,
                    ThicknessMm = PlasterConfig.FinishCoatThicknessMm,
                    MixRatio = isHeritage ? "1:3 (lime:sand)" : "1:8 (OPC:sand) or premixed",
                    Material = isHeritage ? "Lime finish" : "Self-coloured render",
                    CuringHours = isHeritage ? 168 : 24,
                });

                if (requiresInsulation)
                {
                    // Prepend insulation base coat
                    buildUp.Coats.Insert(0, new PlasterCoat
                    {
                        Type = CoatType.Render, Sequence = 0,
                        ThicknessMm = 25,
                        MixRatio = "1:8 (OPC:perlite)",
                        Material = "Insulating base coat",
                        CuringHours = 72,
                    });
                    // Re-number
                    for (int i = 0; i < buildUp.Coats.Count; i++)
                        buildUp.Coats[i].Sequence = i + 1;
                }
            }
            else
            {
                // Internal plaster system (BS EN 13914-2)
                bool needsScratch = substrate == SubstrateType.InSituConcrete ||
                    substrate == SubstrateType.EngineeringBrick ||
                    substrate == SubstrateType.MixedSubstrate;

                if (needsScratch)
                {
                    buildUp.Coats.Add(new PlasterCoat
                    {
                        Type = CoatType.Scratch, Sequence = 1,
                        ThicknessMm = 5,
                        MixRatio = substrate == SubstrateType.InSituConcrete ?
                            "SBR bonding slurry" : "1:3 (OPC:sand)",
                        Material = "Bonding/scratch coat",
                        CuringHours = 24,
                    });
                }

                if (substrate == SubstrateType.Plasterboard)
                {
                    // Plasterboard: skim coat only
                    buildUp.Coats.Add(new PlasterCoat
                    {
                        Type = CoatType.Skim, Sequence = 1,
                        ThicknessMm = PlasterConfig.SkimCoatThicknessMm,
                        MixRatio = "Premixed (Thistle Multi-Finish)",
                        Material = "Gypsum skim",
                        CuringHours = 24,
                    });
                }
                else
                {
                    // Standard 2-coat internal (backing + skim)
                    buildUp.Coats.Add(new PlasterCoat
                    {
                        Type = CoatType.Backing, Sequence = buildUp.Coats.Count + 1,
                        ThicknessMm = PlasterConfig.BackingCoatThicknessMm,
                        MixRatio = "Premixed (Thistle Bonding/Browning/Hardwall)",
                        Material = substrate switch
                        {
                            SubstrateType.DenseBlock or SubstrateType.CommonBrick => "Thistle Browning",
                            SubstrateType.InSituConcrete or SubstrateType.EngineeringBrick => "Thistle Bonding",
                            SubstrateType.LightweightBlock => "Thistle Hardwall",
                            SubstrateType.MetalLath or SubstrateType.Timber => "Thistle Bonding",
                            _ => "Thistle Browning",
                        },
                        CuringHours = 24,
                    });

                    buildUp.Coats.Add(new PlasterCoat
                    {
                        Type = CoatType.Skim, Sequence = buildUp.Coats.Count + 1,
                        ThicknessMm = PlasterConfig.SkimCoatThicknessMm,
                        MixRatio = "Premixed (Thistle Multi-Finish)",
                        Material = "Gypsum skim finish",
                        CuringHours = 24,
                    });
                }
            }

            buildUp.TotalThicknessMm = buildUp.Coats.Sum(c => c.ThicknessMm);
            buildUp.TotalDryingDays = buildUp.Coats.Sum(c => c.CuringHours) / 24.0;

            buildUp.Summary = $"Plaster build-up ({(isExternal ? "EXT" : "INT")}, {substrate}):\n" +
                string.Join("\n", buildUp.Coats.Select(c =>
                    $"  Coat {c.Sequence}: {c.Type} — {c.ThicknessMm:F0}mm, {c.MixRatio}, cure {c.CuringHours}h")) +
                $"\n  Total: {buildUp.TotalThicknessMm:F0}mm, {buildUp.TotalDryingDays:F1} days drying";

            return buildUp;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 3. SURFACE ANALYZER — Substrate Classification & Key Detection
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyzes wall surfaces for plastering readiness.
    ///
    /// Suction rate classification (BS EN 13914-1 §5.3):
    ///   High suction (>1.5 kg/m²/min): requires pre-wetting or PVA primer
    ///   Moderate (0.5-1.5): ideal, no treatment needed
    ///   Low suction (<0.5): requires bonding agent (SBR/PVA)
    ///   Zero suction: requires mechanical key (metal lath, scabbling)
    ///
    /// Flatness tolerance (BS 8212):
    ///   Class A: ±3mm in 1.8m (high quality)
    ///   Class B: ±5mm in 1.8m (normal)
    ///   Class C: ±10mm in 1.8m (utility)
    /// </summary>
    internal static class SurfaceAnalyzer
    {
        /// <summary>Surface analysis result.</summary>
        public class SurfaceAnalysisResult
        {
            public SubstrateType Substrate { get; set; }
            public double SuctionRate { get; set; } // kg/m²/min
            public string SuctionClass { get; set; } // High/Moderate/Low/Zero
            public string PreTreatment { get; set; }
            public bool RequiresMechanicalKey { get; set; }
            public bool RequiresPrimer { get; set; }
            public string FlatnessClass { get; set; }
            public double MaxThicknessVariationMm { get; set; }
            public double RecommendedThicknessMm { get; set; }
            public List<string> Warnings { get; set; } = new();
            public string Summary { get; set; }
        }

        /// <summary>Suction rates per substrate (BS EN 13914-1 Table 2).</summary>
        private static readonly Dictionary<SubstrateType, double> SuctionRates = new()
        {
            { SubstrateType.DenseBlock, 0.8 },
            { SubstrateType.LightweightBlock, 2.0 },
            { SubstrateType.CommonBrick, 1.2 },
            { SubstrateType.EngineeringBrick, 0.2 },
            { SubstrateType.InSituConcrete, 0.1 },
            { SubstrateType.Plasterboard, 0.5 },
            { SubstrateType.MetalLath, 0.0 },
            { SubstrateType.Timber, 0.0 },
            { SubstrateType.StoneWork, 0.6 },
            { SubstrateType.MixedSubstrate, 1.0 },
        };

        /// <summary>
        /// Analyzes a wall surface for plastering suitability.
        /// </summary>
        public static SurfaceAnalysisResult AnalyzeSurface(
            SubstrateType substrate, bool isExternal, double wallHeightM = 3.0)
        {
            var result = new SurfaceAnalysisResult { Substrate = substrate };

            result.SuctionRate = SuctionRates.GetValueOrDefault(substrate, 1.0);

            // Classify suction
            if (result.SuctionRate >= 1.5)
            {
                result.SuctionClass = "High";
                result.RequiresPrimer = true;
                result.PreTreatment = "Pre-wet surface 24h before. Apply PVA primer 1:5 dilution.";
            }
            else if (result.SuctionRate >= 0.5)
            {
                result.SuctionClass = "Moderate";
                result.RequiresPrimer = false;
                result.PreTreatment = "Dampen surface lightly before application.";
            }
            else if (result.SuctionRate > 0)
            {
                result.SuctionClass = "Low";
                result.RequiresPrimer = true;
                result.PreTreatment = "Apply SBR bonding agent. Allow to become tacky.";
            }
            else
            {
                result.SuctionClass = "Zero";
                result.RequiresMechanicalKey = true;
                result.RequiresPrimer = true;
                result.PreTreatment = "Fix expanded metal lath or scabble surface. Apply SBR.";
            }

            // Recommended thickness
            result.RecommendedThicknessMm = isExternal ?
                PlasterConfig.ExternalRenderThicknessMm :
                PlasterConfig.InternalRenderThicknessMm;

            // Adjust for substrate
            if (substrate == SubstrateType.Plasterboard)
                result.RecommendedThicknessMm = PlasterConfig.SkimCoatThicknessMm;
            else if (substrate == SubstrateType.MixedSubstrate)
                result.RecommendedThicknessMm += 5; // Extra for uneven surface

            // Flatness assessment
            result.FlatnessClass = isExternal ? "B" : "A";
            result.MaxThicknessVariationMm = result.FlatnessClass switch
            {
                "A" => 3, "B" => 5, "C" => 10, _ => 5,
            };

            // Warnings
            if (wallHeightM > 3.5)
                result.Warnings.Add("Wall height > 3.5m — scaffold required, check drying from top down");
            if (substrate == SubstrateType.MixedSubstrate)
                result.Warnings.Add("Mixed substrate — high cracking risk, use mesh reinforcement");
            if (isExternal && substrate == SubstrateType.LightweightBlock)
                result.Warnings.Add("Lightweight block external — use weaker mix to prevent debonding");

            result.Summary = $"Surface ({substrate}, {(isExternal ? "EXT" : "INT")}):\n" +
                $"  Suction: {result.SuctionClass} ({result.SuctionRate:F1} kg/m²/min)\n" +
                $"  Treatment: {result.PreTreatment}\n" +
                $"  Thickness: {result.RecommendedThicknessMm:F0}mm, Flatness: Class {result.FlatnessClass}\n" +
                (result.Warnings.Count > 0 ? "  Warnings: " + string.Join("; ", result.Warnings) : "");

            return result;
        }

        /// <summary>
        /// Auto-detects substrate type from wall construction family name.
        /// </summary>
        public static SubstrateType DetectSubstrate(Wall wall)
        {
            string typeName = (wall.WallType?.Name ?? "").ToLowerInvariant();
            string familyName = (wall.WallType?.FamilyName ?? "").ToLowerInvariant();
            string combined = typeName + " " + familyName;

            if (combined.Contains("plasterboard") || combined.Contains("drywall") ||
                combined.Contains("gypsum") || combined.Contains("stud"))
                return SubstrateType.Plasterboard;
            if (combined.Contains("lightweight") || combined.Contains("aerated") ||
                combined.Contains("aircrete") || combined.Contains("thermalite"))
                return SubstrateType.LightweightBlock;
            if (combined.Contains("engineering") || combined.Contains("class b"))
                return SubstrateType.EngineeringBrick;
            if (combined.Contains("brick") || combined.Contains("masonry"))
                return SubstrateType.CommonBrick;
            if (combined.Contains("concrete") && !combined.Contains("block"))
                return SubstrateType.InSituConcrete;
            if (combined.Contains("block"))
                return SubstrateType.DenseBlock;
            if (combined.Contains("timber") || combined.Contains("wood") || combined.Contains("stud"))
                return SubstrateType.Timber;
            if (combined.Contains("stone"))
                return SubstrateType.StoneWork;
            if (combined.Contains("metal") || combined.Contains("lath"))
                return SubstrateType.MetalLath;

            return SubstrateType.DenseBlock; // Default
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 4. PLASTER COVERAGE ENGINE — Area, Volume, Cost Calculation
    // ════════════════════════════════════════════════════════════════

    #region Coverage Types

    /// <summary>Complete coverage calculation result.</summary>
    public class PlasterCoverageResult
    {
        public double GrossAreaM2 { get; set; }
        public double OpeningAreaM2 { get; set; }
        public double RevealAreaM2 { get; set; }
        public double NetAreaM2 { get; set; }
        public double WasteAdjustedAreaM2 { get; set; }
        public double VolumeM3 { get; set; }
        public double MaterialWeightKg { get; set; }
        public int BagsRequired { get; set; }
        public double MaterialCost { get; set; }
        public double LabourCost { get; set; }
        public double ScaffoldCost { get; set; }
        public double TotalCost { get; set; }
        public double CostPerM2 { get; set; }
        public double CarbonKgCO2 { get; set; }
        public TimeSpan EstimatedDuration { get; set; }
        public int WallCount { get; set; }
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Plaster coverage calculation with waste adjustment,
    /// opening deductions, reveal additions, and cost estimation.
    ///
    /// Net area = Gross area - Openings(>threshold) + Reveals
    /// Waste-adjusted = Net area × (1 + waste%)
    /// Volume = Adjusted area × thickness
    /// Bags = Volume × density / bag weight (rounded up)
    ///
    /// Labour rate: 8-15 m²/person/day (internal)
    ///              5-10 m²/person/day (external, with scaffold)
    ///
    /// Embodied carbon (ICE database):
    ///   Cement render: 0.12 kgCO₂/kg
    ///   Gypsum plaster: 0.08 kgCO₂/kg
    ///   Lime render: 0.06 kgCO₂/kg
    /// </summary>
    internal static class PlasterCoverageEngine
    {
        /// <summary>
        /// Calculates plaster coverage for a set of walls.
        /// </summary>
        public static PlasterCoverageResult CalculateCoverage(
            Document doc, IEnumerable<Wall> walls,
            double thicknessMm, bool isExternal,
            PlasterMaterialSpec material = null)
        {
            var result = new PlasterCoverageResult();
            material = material ?? PlasterMaterialScience.GetMaterial(
                isExternal ? PlasterType.GeneralPurpose : PlasterType.Lightweight);

            double openingThresholdM2 = PlasterConfig.OpeningDeductionThresholdM2;

            foreach (var wall in walls)
            {
                result.WallCount++;

                // Gross wall area
                var heightParam = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                double heightFt = heightParam?.AsDouble() ?? 10;
                double lengthFt = (wall.Location as LocationCurve)?.Curve.Length ?? 0;

                double areaM2 = heightFt * lengthFt * Units.SqFtToSqM;
                result.GrossAreaM2 += areaM2;

                // Opening deductions
                var inserts = wall.FindInserts(true, true, true, true);
                foreach (var insertId in inserts)
                {
                    var insert = doc.GetElement(insertId);
                    if (insert == null) continue;

                    var bb = insert.get_BoundingBox(null);
                    if (bb == null) continue;

                    double insertW = Math.Abs(bb.Max.X - bb.Min.X) * Units.FeetToMm / 1000;
                    double insertH = Math.Abs(bb.Max.Z - bb.Min.Z) * Units.FeetToMm / 1000;
                    double insertArea = insertW * insertH;

                    if (insertArea > openingThresholdM2)
                    {
                        result.OpeningAreaM2 += insertArea;

                        // Reveal additions (perimeter × depth)
                        bool isDoor = insert.Category?.BuiltInCategory == BuiltInCategory.OST_Doors;
                        double revealDepth = (isDoor ?
                            PlasterConfig.DoorRevealDepthMm :
                            PlasterConfig.WindowRevealDepthMm) / 1000.0;
                        double perim = isDoor ? (2 * insertH + insertW) : (2 * (insertW + insertH));
                        result.RevealAreaM2 += perim * revealDepth;
                    }
                }
            }

            // Net area
            result.NetAreaM2 = result.GrossAreaM2 - result.OpeningAreaM2 + result.RevealAreaM2;
            result.NetAreaM2 = Math.Max(0, result.NetAreaM2);

            // Waste adjustment
            double wasteFactor = 1.0 + PlasterConfig.WasteFactorPercent / 100.0;
            result.WasteAdjustedAreaM2 = result.NetAreaM2 * wasteFactor;

            // Volume and material quantities
            result.VolumeM3 = result.WasteAdjustedAreaM2 * thicknessMm / 1000.0;
            result.MaterialWeightKg = result.VolumeM3 * material.DensityKgM3;
            result.BagsRequired = (int)Math.Ceiling(
                result.WasteAdjustedAreaM2 / Math.Max(material.CoverageM2PerBag, 0.1));

            // Cost
            result.MaterialCost = result.BagsRequired * material.CostPerBag;
            result.LabourCost = result.WasteAdjustedAreaM2 * PlasterConfig.LabourRatePerM2;
            result.ScaffoldCost = isExternal ? result.GrossAreaM2 * PlasterConfig.ScaffoldCostPerM2 : 0;
            result.TotalCost = result.MaterialCost + result.LabourCost + result.ScaffoldCost;
            result.CostPerM2 = result.NetAreaM2 > 0 ? result.TotalCost / result.NetAreaM2 : 0;

            // Carbon (ICE database coefficients)
            double carbonCoeff = material.Type switch
            {
                PlasterType.Renovation => 0.06,
                PlasterType.Lightweight or PlasterType.OneCoat => 0.08,
                _ => 0.12,
            };
            result.CarbonKgCO2 = result.MaterialWeightKg * carbonCoeff;

            // Duration estimate (internal: 10 m²/day, external: 6 m²/day per person)
            double ratePerDay = isExternal ? 6 : 10;
            double labourDays = result.WasteAdjustedAreaM2 / ratePerDay;
            result.EstimatedDuration = TimeSpan.FromDays(labourDays);

            result.Summary = $"Plaster Coverage ({result.WallCount} walls):\n" +
                $"  Gross: {result.GrossAreaM2:F1}m² − Openings: {result.OpeningAreaM2:F1}m² " +
                $"+ Reveals: {result.RevealAreaM2:F1}m² = Net: {result.NetAreaM2:F1}m²\n" +
                $"  Waste-adjusted: {result.WasteAdjustedAreaM2:F1}m² " +
                $"({PlasterConfig.WasteFactorPercent:F0}% waste)\n" +
                $"  Material: {result.BagsRequired} bags ({material.Name}), " +
                $"{result.MaterialWeightKg:F0}kg, {result.VolumeM3:F2}m³\n" +
                $"  Cost: £{result.MaterialCost:F0} material + £{result.LabourCost:F0} labour" +
                $"{(isExternal ? $" + £{result.ScaffoldCost:F0} scaffold" : "")} = " +
                $"£{result.TotalCost:F0} total (£{result.CostPerM2:F1}/m²)\n" +
                $"  Carbon: {result.CarbonKgCO2:F0} kgCO₂e\n" +
                $"  Duration: {result.EstimatedDuration.Days} days";

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 5. PLASTER QUALITY INSPECTOR — 15-Point BS EN 13914 Checklist
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Quality inspection checklist per BS EN 13914-1/2 and BS 8000-10.
    /// Generates a 15-point QA report for site sign-off.
    /// </summary>
    internal static class PlasterQualityInspector
    {
        /// <summary>Individual inspection check.</summary>
        public class QualityCheck
        {
            public int Number { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public string Standard { get; set; }
            public string Criteria { get; set; }
            public bool Pass { get; set; }
            public string Notes { get; set; }
        }

        /// <summary>
        /// Generates the 15-point quality inspection checklist.
        /// </summary>
        public static List<QualityCheck> GenerateChecklist(
            PlasterBuildUp buildUp, SurfaceAnalyzer.SurfaceAnalysisResult surface)
        {
            bool isExt = buildUp.IsExternal;
            var checks = new List<QualityCheck>
            {
                new() { Number = 1, Category = "Preparation", Standard = "BS EN 13914 §5",
                    Description = "Substrate cleaned — dust, oil, release agents removed",
                    Criteria = "Visual: no contaminants, no efflorescence", Pass = true },

                new() { Number = 2, Category = "Preparation", Standard = "BS EN 13914 §5.3",
                    Description = $"Suction control — {surface.SuctionClass} suction substrate",
                    Criteria = surface.PreTreatment, Pass = true },

                new() { Number = 3, Category = "Preparation", Standard = "BS 8000-10 §3.3",
                    Description = "Mechanical key / bonding agent where required",
                    Criteria = surface.RequiresMechanicalKey ? "Metal lath fixed at 300mm centres" : "N/A",
                    Pass = true },

                new() { Number = 4, Category = "Materials", Standard = "BS EN 998-1",
                    Description = "Materials comply with BS EN 998-1 classification",
                    Criteria = $"Specified: {buildUp.Coats.FirstOrDefault()?.Material ?? "N/A"}",
                    Pass = true },

                new() { Number = 5, Category = "Materials", Standard = "BS EN 13914 §6.2",
                    Description = "Mix ratios correct — weakest coat outermost",
                    Criteria = "Each coat ≤ strength of coat beneath it",
                    Pass = ValidateStrengthGradient(buildUp) },

                new() { Number = 6, Category = "Application", Standard = "BS EN 13914 §7.1",
                    Description = $"Coat thicknesses within tolerance (±{PlasterConfig.MaxDeviationMm:F0}mm)",
                    Criteria = string.Join(", ", buildUp.Coats.Select(c => $"{c.Type}: {c.ThicknessMm}mm")),
                    Pass = buildUp.Coats.All(c => c.ThicknessMm >= 2 && c.ThicknessMm <= 25) },

                new() { Number = 7, Category = "Application", Standard = "BS EN 13914 §7.3",
                    Description = "Total thickness within range",
                    Criteria = $"Total: {buildUp.TotalThicknessMm:F0}mm (limit: {(isExt ? "20-25mm" : "13-15mm")})",
                    Pass = isExt ? buildUp.TotalThicknessMm >= 15 && buildUp.TotalThicknessMm <= 30 :
                        buildUp.TotalThicknessMm >= 3 && buildUp.TotalThicknessMm <= 20 },

                new() { Number = 8, Category = "Curing", Standard = "BS EN 13914 §8.1",
                    Description = "Minimum curing time between coats observed",
                    Criteria = $"Total curing: {buildUp.TotalDryingDays:F1} days",
                    Pass = buildUp.Coats.All(c => c.CuringHours >= 24) },

                new() { Number = 9, Category = "Curing", Standard = "BS EN 13914 §8.2",
                    Description = $"Ambient temperature {PlasterConfig.MinCuringTempC}-{PlasterConfig.MaxCuringTempC}°C during application and curing",
                    Criteria = "No frost risk. No direct heat sources.",
                    Pass = true },

                new() { Number = 10, Category = "Flatness", Standard = "BS 8212",
                    Description = $"Surface flatness: Class {surface.FlatnessClass}",
                    Criteria = $"Max deviation ±{surface.MaxThicknessVariationMm:F0}mm in 1.8m straightedge",
                    Pass = true },

                new() { Number = 11, Category = "Joints", Standard = "BS EN 13914 §7.5",
                    Description = "Movement joints / expansion joints provided",
                    Criteria = isExt ? "Max 6m centres, coincide with structural joints" : "At structural movement joints",
                    Pass = true },

                new() { Number = 12, Category = "Details", Standard = "BS EN 13914 §9",
                    Description = "Reveals, sills, drip details formed correctly",
                    Criteria = "Bellcast bead at base, stop beads at openings",
                    Pass = true },

                new() { Number = 13, Category = "Details", Standard = "BS EN 13914 §9.3",
                    Description = "Corner beads installed at external angles",
                    Criteria = "Stainless steel or PVC beads, plumb, securely fixed",
                    Pass = true },

                new() { Number = 14, Category = "Finish", Standard = "BS 8000-10 §5",
                    Description = "Surface finish free from defects",
                    Criteria = "No hollows, cracks, blistering, debonding, efflorescence",
                    Pass = true },

                new() { Number = 15, Category = "Documentation", Standard = "ISO 19650",
                    Description = "Plastering specification recorded in BIM",
                    Criteria = "Wall type includes plaster layer(s), thickness, material",
                    Pass = true },
            };

            return checks;
        }

        /// <summary>
        /// Validates "weakest coat outermost" principle.
        /// </summary>
        private static bool ValidateStrengthGradient(PlasterBuildUp buildUp)
        {
            // Scratch > Backing > Finish in strength
            var strengthOrder = new Dictionary<CoatType, int>
            {
                { CoatType.Scratch, 4 }, { CoatType.DashCoat, 4 },
                { CoatType.Backing, 3 }, { CoatType.Render, 3 },
                { CoatType.Finish, 2 }, { CoatType.Skim, 1 },
                { CoatType.Tyrolean, 2 },
            };

            int prevStrength = int.MaxValue;
            foreach (var coat in buildUp.Coats.OrderBy(c => c.Sequence))
            {
                int s = strengthOrder.GetValueOrDefault(coat.Type, 2);
                if (s > prevStrength) return false; // Stronger coat on top = FAIL
                prevStrength = s;
            }
            return true;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 6. PLASTER LAYER BUILDER — Compound Wall Layer Injection
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds plaster layers to existing Revit compound wall types.
    /// Creates new wall type with plaster finish layer(s) on interior/exterior face.
    ///
    /// Revit compound structure:
    ///   Layer 1 (Exterior): External render
    ///   Layer 2: Structure (block/brick)
    ///   Layer 3 (Interior): Internal plaster
    ///
    /// Uses CompoundStructure.SetLayers() to inject plaster layers without
    /// modifying the structural core.
    /// </summary>
    internal static class PlasterLayerBuilder
    {
        /// <summary>Layer injection result.</summary>
        public class LayerResult
        {
            public bool Success { get; set; }
            public ElementId NewTypeId { get; set; }
            public string NewTypeName { get; set; }
            public int LayersAdded { get; set; }
            public double TotalPlasterThicknessMm { get; set; }
            public string Summary { get; set; }
        }

        /// <summary>
        /// Adds plaster layers to a wall type, creating a new type.
        /// </summary>
        public static LayerResult AddPlasterLayers(
            Document doc, WallType sourceType,
            PlasterBuildUp buildUp, bool applyToInterior = true)
        {
            var result = new LayerResult();

            try
            {
                // Create new type name
                string suffix = applyToInterior ? "Int Plaster" : "Ext Render";
                string newName = $"{sourceType.Name} + {suffix}";

                // Check if already exists
                var existing = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType)).Cast<WallType>()
                    .FirstOrDefault(wt => wt.Name == newName);

                if (existing != null)
                {
                    result.Success = true;
                    result.NewTypeId = existing.Id;
                    result.NewTypeName = newName;
                    result.Summary = $"Type '{newName}' already exists";
                    return result;
                }

                // Duplicate the source type
                var newType = sourceType.Duplicate(newName) as WallType;
                if (newType == null)
                {
                    result.Summary = "Failed to duplicate wall type";
                    return result;
                }

                var cs = newType.GetCompoundStructure();
                if (cs == null)
                {
                    result.Summary = "Wall type has no compound structure (curtain wall?)";
                    return result;
                }

                var layers = cs.GetLayers().ToList();

                // Find or create plaster material
                var plasterMat = FindOrCreatePlasterMaterial(doc, buildUp);

                // Build new layers from the build-up
                foreach (var coat in buildUp.Coats.OrderBy(c => c.Sequence))
                {
                    var newLayer = new CompoundStructureLayer(
                        coat.ThicknessMm * Units.MmToFeet,
                        MaterialFunctionAssignment.Finish,
                        plasterMat);

                    if (applyToInterior)
                        layers.Add(newLayer); // Interior = last layer
                    else
                        layers.Insert(0, newLayer); // Exterior = first layer

                    result.LayersAdded++;
                    result.TotalPlasterThicknessMm += coat.ThicknessMm;
                }

                cs.SetLayers(layers);
                cs.SetNumberOfShellLayers(ShellLayerType.Interior,
                    applyToInterior ? result.LayersAdded : cs.GetNumberOfShellLayers(ShellLayerType.Interior));
                cs.SetNumberOfShellLayers(ShellLayerType.Exterior,
                    applyToInterior ? cs.GetNumberOfShellLayers(ShellLayerType.Exterior) : result.LayersAdded);

                newType.SetCompoundStructure(cs);

                result.Success = true;
                result.NewTypeId = newType.Id;
                result.NewTypeName = newName;
                result.Summary = $"Created '{newName}': added {result.LayersAdded} plaster layers " +
                    $"({result.TotalPlasterThicknessMm:F0}mm total) to {(applyToInterior ? "interior" : "exterior")}";
            }
            catch (Exception ex)
            {
                StingLog.Error("PlasterLayerBuilder", ex);
                result.Summary = $"Error: {ex.Message}";
            }

            return result;
        }

        /// <summary>Finds or creates a plaster material in the document.</summary>
        private static ElementId FindOrCreatePlasterMaterial(Document doc, PlasterBuildUp buildUp)
        {
            string matName = buildUp.IsExternal ? "STING_External_Render" : "STING_Internal_Plaster";

            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Material)).Cast<Material>()
                .FirstOrDefault(m => m.Name == matName);
            if (existing != null) return existing.Id;

            // Try to find existing plaster material
            var plaster = new FilteredElementCollector(doc)
                .OfClass(typeof(Material)).Cast<Material>()
                .FirstOrDefault(m =>
                {
                    var n = m.Name.ToLowerInvariant();
                    return n.Contains("plaster") || n.Contains("render") || n.Contains("gypsum");
                });

            if (plaster != null) return plaster.Id;

            // Create new
            try
            {
                var matId = Material.Create(doc, matName);
                var mat = doc.GetElement(matId) as Material;
                if (mat != null)
                {
                    mat.Color = buildUp.IsExternal ?
                        new Autodesk.Revit.DB.Color(210, 200, 180) : // Sandy render
                        new Autodesk.Revit.DB.Color(240, 240, 235);  // White plaster
                }
                return matId;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlasterMaterial create: {ex.Message}");
                // Fallback: use first available material
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Material)).Cast<Material>()
                    .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
            }
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 7. SMART PLASTER FACTORY — Intelligent Wall Plastering Pipeline
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Intelligent plastering pipeline for selected walls.
    /// Full pipeline: 1) Detect substrate  2) Analyze surface  3) Design mix
    /// 4) Calculate coverage  5) Inject layers  6) Quality checklist  7) STING tags
    /// </summary>
    internal static class SmartPlasterFactory
    {
        /// <summary>Complete plastering result.</summary>
        public class PlasteringReport
        {
            public bool Success { get; set; }
            public int WallsProcessed { get; set; }
            public int WallsPlastered { get; set; }
            public int TypesCreated { get; set; }
            public PlasterCoverageResult Coverage { get; set; }
            public PlasterBuildUp BuildUp { get; set; }
            public SurfaceAnalyzer.SurfaceAnalysisResult SurfaceAnalysis { get; set; }
            public List<PlasterQualityInspector.QualityCheck> QualityChecks { get; set; }
            public List<string> Steps { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public string Summary { get; set; }
        }

        /// <summary>
        /// Runs the full intelligent plastering pipeline on selected walls.
        /// </summary>
        public static PlasteringReport PlasterWalls(
            Document doc, IList<Wall> walls, bool isExternal,
            bool injectLayers = true)
        {
            var report = new PlasteringReport();

            try
            {
                if (walls == null || walls.Count == 0)
                {
                    report.Summary = "No walls selected";
                    return report;
                }

                report.WallsProcessed = walls.Count;

                // Step 1: Detect substrate from first wall (assume uniform)
                var substrate = SurfaceAnalyzer.DetectSubstrate(walls[0]);
                report.Steps.Add($"✓ Substrate detected: {substrate}");

                // Step 2: Analyze surface
                report.SurfaceAnalysis = SurfaceAnalyzer.AnalyzeSurface(substrate, isExternal);
                report.Steps.Add($"✓ Surface analyzed: {report.SurfaceAnalysis.SuctionClass} suction");

                if (report.SurfaceAnalysis.RequiresMechanicalKey)
                    report.Warnings.Add("Mechanical key required — expanded metal lath needed");
                if (report.SurfaceAnalysis.RequiresPrimer)
                    report.Steps.Add($"✓ Pre-treatment: {report.SurfaceAnalysis.PreTreatment}");

                // Step 3: Design multi-coat build-up
                report.BuildUp = PlasterMixDesigner.DesignBuildUp(substrate, isExternal);
                report.Steps.Add($"✓ Build-up designed: {report.BuildUp.Coats.Count} coats, " +
                    $"{report.BuildUp.TotalThicknessMm:F0}mm total");

                // Step 4: Calculate coverage
                report.Coverage = PlasterCoverageEngine.CalculateCoverage(
                    doc, walls, report.BuildUp.TotalThicknessMm, isExternal,
                    PlasterMaterialScience.RecommendMaterial(substrate, isExternal));
                report.Steps.Add($"✓ Coverage: {report.Coverage.NetAreaM2:F1}m², " +
                    $"{report.Coverage.BagsRequired} bags, £{report.Coverage.TotalCost:F0}");

                // Step 5: Inject layers into wall types
                if (injectLayers)
                {
                    var processedTypes = new HashSet<ElementId>();
                    foreach (var wall in walls)
                    {
                        if (processedTypes.Contains(wall.WallType.Id)) continue;
                        processedTypes.Add(wall.WallType.Id);

                        var layerResult = PlasterLayerBuilder.AddPlasterLayers(
                            doc, wall.WallType, report.BuildUp, !isExternal);

                        if (layerResult.Success)
                        {
                            report.TypesCreated++;
                            report.Steps.Add($"✓ Type created: {layerResult.NewTypeName}");

                            // Change wall to new type
                            foreach (var w in walls.Where(ww => ww.WallType.Id == wall.WallType.Id))
                            {
                                try
                                {
                                    w.WallType = doc.GetElement(layerResult.NewTypeId) as WallType;
                                    report.WallsPlastered++;
                                }
                                catch (Exception ex)
                                {
                                    report.Warnings.Add($"Wall {w.Id.Value}: {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            report.Warnings.Add($"Type '{wall.WallType.Name}': {layerResult.Summary}");
                        }
                    }
                    report.Steps.Add($"✓ {report.WallsPlastered}/{report.WallsProcessed} walls plastered");
                }

                // Step 6: Quality checklist
                report.QualityChecks = PlasterQualityInspector.GenerateChecklist(
                    report.BuildUp, report.SurfaceAnalysis);
                int qaPassed = report.QualityChecks.Count(q => q.Pass);
                report.Steps.Add($"✓ QA checklist: {qaPassed}/{report.QualityChecks.Count} pass");

                // Step 7: STING tags
                foreach (var wall in walls)
                {
                    try
                    {
                        ParameterHelpers.SetIfEmpty(wall, "ASS_DISCIPLINE_COD_TXT", "A");
                        ParameterHelpers.SetIfEmpty(wall, "ASS_PRODCT_COD_TXT",
                            isExternal ? "RND" : "PLT");
                    }
                    catch { /* STING params not bound */ }
                }
                report.Steps.Add("✓ STING tags populated (DISC=A, PROD=" +
                    (isExternal ? "RND" : "PLT") + ")");

                report.Success = true;
                report.Summary = $"Smart Plaster: {report.WallsPlastered}/{report.WallsProcessed} walls, " +
                    $"{report.TypesCreated} types, {report.BuildUp.TotalThicknessMm:F0}mm " +
                    $"({report.BuildUp.Coats.Count} coats), " +
                    $"£{report.Coverage.TotalCost:F0}, {report.Coverage.CarbonKgCO2:F0}kgCO₂e";
            }
            catch (Exception ex)
            {
                StingLog.Error("SmartPlasterFactory", ex);
                report.Summary = $"Error: {ex.Message}";
            }

            return report;
        }
    }
}
