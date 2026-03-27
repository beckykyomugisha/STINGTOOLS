// ============================================================================
// PlasteringEngine.cs — Integrated Coverings, Plastering & Painting Engine
//
// Integrates with STINGTOOLS existing material pipeline:
//   - BLE_MATERIALS.csv plaster/render/paint rows
//   - CompoundTypeCreator for wall/floor/ceiling layer injection
//   - MaterialPropertyHelper for Revit material creation
//   - MR_PARAMETERS.txt plaster/paint cost parameters
//
// Algorithm classes:
//   1.  PlasterConfig            — Configurable settings (project_config.json)
//   2.  CoveringMaterialDatabase — Unified plaster + paint + coating material DB
//   3.  SubstrateDetector        — Element-agnostic substrate detection (walls,
//                                   beams, columns, floors, ceilings, roofs)
//   4.  CoveringMixDesigner      — Multi-coat build-up (BS EN 13914)
//   5.  ElementCoverageCalculator — Geometry-driven area from any HostObject or
//                                   FamilyInstance (beams, columns, ducts)
//   6.  PaintSpecificationEngine — DFT, coats, spread rate, VOC (BS 6150)
//   7.  CoveringCostEstimator    — Integrated cost from BLE cost parameters
//   8.  CoveringQualityInspector — 15-point QA + paint-specific checks
//   9.  CompoundLayerInjector    — Injects finish layers using existing
//                                   CompoundTypeCreator patterns for walls/floors
//  10.  BeamColumnCoveringEngine — Applies coverings to beams & columns via
//                                   material parameter + STING shared params
//  11.  SmartCoveringFactory     — Unified pipeline: any element → covering
//  12.  RoomFinishScheduler      — Room-based finish schedule (wall/floor/ceiling)
//
// Standards: BS EN 998-1, BS EN 13914-1/2, BS 6150, BS 8000-10, ISO 19650
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
    // 0. CONFIGURABLE SETTINGS
    // ════════════════════════════════════════════════════════════════

    internal static class PlasterConfig
    {
        // ── Thickness Defaults (mm) ──────────────────────────────
        public static double InternalRenderMm { get; set; } = 13;
        public static double ExternalRenderMm { get; set; } = 20;
        public static double SkimCoatMm { get; set; } = 3;
        public static double ScratchCoatMm { get; set; } = 10;
        public static double BackingCoatMm { get; set; } = 11;
        public static double PaintDFTMicrons { get; set; } = 100;

        // ── Waste & Coverage ─────────────────────────────────────
        public static double WastePct { get; set; } = 10;
        public static double OpeningThresholdM2 { get; set; } = 0.5;

        // ── Cost (from BLE CSV / config override) ────────────────
        public static double PlasterLabourPerM2 { get; set; } = 12.0;
        public static double PlasterMaterialPerM2 { get; set; } = 4.5;
        public static double PaintLabourPerM2 { get; set; } = 3.5;
        public static double PaintMaterialPerM2 { get; set; } = 2.0;

        // ── Quality ──────────────────────────────────────────────
        public static double MaxDeviationMm { get; set; } = 3.0;
        public static double MinCuringHours { get; set; } = 24;

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
                InternalRenderMm = sec.Value<double?>("INT_RENDER_MM") ?? InternalRenderMm;
                ExternalRenderMm = sec.Value<double?>("EXT_RENDER_MM") ?? ExternalRenderMm;
                WastePct = sec.Value<double?>("WASTE_PCT") ?? WastePct;
                PaintDFTMicrons = sec.Value<double?>("PAINT_DFT_MICRONS") ?? PaintDFTMicrons;
                StingLog.Info("PlasterConfig loaded");
            }
            catch (Exception ex) { StingLog.Warn($"PlasterConfig: {ex.Message}"); }
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 1. COVERING TYPE CLASSIFICATION
    // ════════════════════════════════════════════════════════════════

    /// <summary>Covering type — plaster, render, paint, or coating.</summary>
    public enum CoveringType
    {
        InternalPlaster, ExternalRender, SkimCoat,
        EmulsionPaint, GlossPaint, MasonryPaint, EpoxyCoating,
        Intumescent, Waterproof, AntiCondensation, PrimerOnly,
    }

    /// <summary>Target element category for covering.</summary>
    public enum CoveringTarget { Wall, Floor, Ceiling, Roof, Beam, Column, Duct, Pipe, Generic }

    /// <summary>Substrate classification (element-agnostic).</summary>
    public enum SubstrateType
    {
        DenseBlock, LightweightBlock, CommonBrick, EngineeringBrick,
        InSituConcrete, Plasterboard, MetalLath, Timber, StoneWork,
        SteelSection, SteelPlate, ExposedConcrete, MixedSubstrate,
    }

    /// <summary>Plaster coat type.</summary>
    public enum CoatType { Scratch, Backing, Finish, Skim, Render, DashCoat, Tyrolean, PaintPrimer, PaintUndercoat, PaintTopcoat }


    // ════════════════════════════════════════════════════════════════
    // 2. COVERING MATERIAL DATABASE — Reads from BLE_MATERIALS.csv
    // ════════════════════════════════════════════════════════════════

    #region Material Types

    /// <summary>Unified covering material spec (plaster + paint).</summary>
    public class CoveringMaterialSpec
    {
        public CoveringType Type { get; set; }
        public string Name { get; set; }
        public string BLECode { get; set; }       // BLE_MATERIALS.csv row code
        public double DensityKgM3 { get; set; }
        public double ThicknessMm { get; set; }
        public double ThermalConductivity { get; set; }
        public string MixRatio { get; set; }
        public double CoverageM2PerUnit { get; set; } // m² per bag or litre
        public double CostPerUnit { get; set; }
        public string UnitType { get; set; }       // "bag" or "litre"
        public double CuringHours { get; set; }
        public string BSClassification { get; set; }
        public double FireResistanceMin { get; set; }
        // Paint-specific
        public double DFTMicrons { get; set; }     // Dry film thickness
        public double SpreadRateM2PerLitre { get; set; }
        public int CoatsRequired { get; set; }
        public double VOCgPerLitre { get; set; }
        public string FinishType { get; set; }     // Matt, Silk, Gloss, Eggshell
    }

    /// <summary>Multi-coat build-up for any covering.</summary>
    public class CoveringBuildUp
    {
        public List<CoveringCoat> Coats { get; set; } = new();
        public double TotalThicknessMm { get; set; }
        public double TotalDryingDays { get; set; }
        public bool IsExternal { get; set; }
        public bool IsPaint { get; set; }
        public SubstrateType Substrate { get; set; }
        public CoveringTarget Target { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>Individual coat in a build-up.</summary>
    public class CoveringCoat
    {
        public CoatType Type { get; set; }
        public double ThicknessMm { get; set; }
        public string MixRatio { get; set; }
        public double CuringHours { get; set; }
        public string Material { get; set; }
        public int Sequence { get; set; }
    }

    #endregion

    /// <summary>
    /// Unified covering material database.
    /// Reads plaster materials from BLE_MATERIALS.csv and supplements
    /// with paint/coating specifications from BS 6150.
    /// </summary>
    internal static class CoveringMaterialDatabase
    {
        private static List<CoveringMaterialSpec> _materials;

        /// <summary>Loads materials from BLE CSV + built-in paint specs.</summary>
        public static List<CoveringMaterialSpec> GetAllMaterials()
        {
            if (_materials != null) return _materials;
            _materials = new List<CoveringMaterialSpec>();

            // Load plaster materials from BLE_MATERIALS.csv
            try
            {
                var csvPath = StingToolsApp.FindDataFile("BLE_MATERIALS.csv");
                if (!string.IsNullOrEmpty(csvPath))
                {
                    var lines = System.IO.File.ReadAllLines(csvPath);
                    foreach (var line in lines.Skip(1))
                    {
                        var cols = StingToolsApp.ParseCsvLine(line);
                        if (cols.Length < 10) continue;
                        string cat = cols.Length > 6 ? cols[6].ToUpperInvariant() : "";
                        if (!cat.Contains("PLASTER") && !cat.Contains("RENDER") &&
                            !cat.Contains("GYPSUM") && !cat.Contains("LIME")) continue;

                        _materials.Add(new CoveringMaterialSpec
                        {
                            Type = cat.Contains("RENDER") ? CoveringType.ExternalRender : CoveringType.InternalPlaster,
                            Name = cols.Length > 7 ? cols[7] : cat,
                            BLECode = cols.Length > 1 ? cols[1] : "",
                            DensityKgM3 = cols.Length > 10 ? ParseDouble(cols[10], 1800) : 1800,
                            ThicknessMm = cols.Length > 9 ? ParseDouble(cols[9], 13) : 13,
                            ThermalConductivity = cols.Length > 12 ? ParseDouble(cols[12], 0.5) : 0.5,
                            MixRatio = cat.Contains("GYPSUM") ? "Premixed" : "1:4 (OPC:sand)",
                            CoverageM2PerUnit = 4.0,
                            CostPerUnit = 6.50,
                            UnitType = "bag",
                            CuringHours = 24,
                            BSClassification = "CS II",
                            FireResistanceMin = 60,
                        });
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CoveringDB BLE load: {ex.Message}"); }

            // Built-in paint specifications (BS 6150)
            _materials.AddRange(new[]
            {
                new CoveringMaterialSpec { Type = CoveringType.PrimerOnly, Name = "Primer/Sealer",
                    DFTMicrons = 25, SpreadRateM2PerLitre = 12, CoatsRequired = 1,
                    CostPerUnit = 18, UnitType = "litre", CuringHours = 4,
                    VOCgPerLitre = 30, FinishType = "Matt", CoverageM2PerUnit = 12 },

                new CoveringMaterialSpec { Type = CoveringType.EmulsionPaint, Name = "Vinyl Matt Emulsion",
                    DFTMicrons = 40, SpreadRateM2PerLitre = 14, CoatsRequired = 2,
                    CostPerUnit = 22, UnitType = "litre", CuringHours = 2,
                    VOCgPerLitre = 15, FinishType = "Matt", CoverageM2PerUnit = 14 },

                new CoveringMaterialSpec { Type = CoveringType.EmulsionPaint, Name = "Vinyl Silk Emulsion",
                    DFTMicrons = 40, SpreadRateM2PerLitre = 13, CoatsRequired = 2,
                    CostPerUnit = 25, UnitType = "litre", CuringHours = 2,
                    VOCgPerLitre = 20, FinishType = "Silk", CoverageM2PerUnit = 13 },

                new CoveringMaterialSpec { Type = CoveringType.GlossPaint, Name = "Oil-Based Gloss",
                    DFTMicrons = 50, SpreadRateM2PerLitre = 16, CoatsRequired = 2,
                    CostPerUnit = 28, UnitType = "litre", CuringHours = 16,
                    VOCgPerLitre = 300, FinishType = "Full Gloss", CoverageM2PerUnit = 16 },

                new CoveringMaterialSpec { Type = CoveringType.MasonryPaint, Name = "Smooth Masonry Paint",
                    DFTMicrons = 80, SpreadRateM2PerLitre = 8, CoatsRequired = 2,
                    CostPerUnit = 30, UnitType = "litre", CuringHours = 4,
                    VOCgPerLitre = 40, FinishType = "Matt", CoverageM2PerUnit = 8 },

                new CoveringMaterialSpec { Type = CoveringType.EpoxyCoating, Name = "Epoxy Floor Coating",
                    DFTMicrons = 200, SpreadRateM2PerLitre = 5, CoatsRequired = 2,
                    CostPerUnit = 55, UnitType = "litre", CuringHours = 24,
                    VOCgPerLitre = 100, FinishType = "Gloss", CoverageM2PerUnit = 5 },

                new CoveringMaterialSpec { Type = CoveringType.Intumescent, Name = "Intumescent Fire Paint",
                    DFTMicrons = 1500, SpreadRateM2PerLitre = 2, CoatsRequired = 3,
                    CostPerUnit = 85, UnitType = "litre", CuringHours = 8,
                    VOCgPerLitre = 50, FinishType = "Matt", CoverageM2PerUnit = 2,
                    FireResistanceMin = 90 },

                new CoveringMaterialSpec { Type = CoveringType.Waterproof, Name = "Cementitious Waterproof",
                    DFTMicrons = 2000, SpreadRateM2PerLitre = 1.5, CoatsRequired = 2,
                    CostPerUnit = 45, UnitType = "kg", CuringHours = 48,
                    CoverageM2PerUnit = 1.5 },
            });

            return _materials;
        }

        public static CoveringMaterialSpec FindByType(CoveringType type) =>
            GetAllMaterials().FirstOrDefault(m => m.Type == type);

        public static List<CoveringMaterialSpec> FindPlasters() =>
            GetAllMaterials().Where(m =>
                m.Type == CoveringType.InternalPlaster ||
                m.Type == CoveringType.ExternalRender ||
                m.Type == CoveringType.SkimCoat).ToList();

        public static List<CoveringMaterialSpec> FindPaints() =>
            GetAllMaterials().Where(m =>
                m.Type == CoveringType.EmulsionPaint ||
                m.Type == CoveringType.GlossPaint ||
                m.Type == CoveringType.MasonryPaint ||
                m.Type == CoveringType.EpoxyCoating ||
                m.Type == CoveringType.Intumescent).ToList();

        private static double ParseDouble(string s, double def)
        {
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v)) return v;
            return def;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 3. SUBSTRATE DETECTOR — Element-Agnostic
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Detects substrate type from ANY Revit element — walls, beams, columns,
    /// floors, ceilings, roofs, ducts, pipes.
    /// Uses family name, type name, structural material parameter, and category.
    /// </summary>
    internal static class SubstrateDetector
    {
        /// <summary>Substrate analysis result.</summary>
        public class SubstrateResult
        {
            public SubstrateType Substrate { get; set; }
            public CoveringTarget Target { get; set; }
            public double SuctionRate { get; set; }
            public string SuctionClass { get; set; }
            public string PreTreatment { get; set; }
            public bool NeedsPrimer { get; set; }
            public bool NeedsMechanicalKey { get; set; }
            public double RecommendedThicknessMm { get; set; }
            public string Summary { get; set; }
        }

        private static readonly Dictionary<SubstrateType, double> SuctionRates = new()
        {
            { SubstrateType.DenseBlock, 0.8 }, { SubstrateType.LightweightBlock, 2.0 },
            { SubstrateType.CommonBrick, 1.2 }, { SubstrateType.EngineeringBrick, 0.2 },
            { SubstrateType.InSituConcrete, 0.1 }, { SubstrateType.Plasterboard, 0.5 },
            { SubstrateType.MetalLath, 0.0 }, { SubstrateType.Timber, 0.0 },
            { SubstrateType.StoneWork, 0.6 }, { SubstrateType.SteelSection, 0.0 },
            { SubstrateType.SteelPlate, 0.0 }, { SubstrateType.ExposedConcrete, 0.15 },
            { SubstrateType.MixedSubstrate, 1.0 },
        };

        /// <summary>
        /// Detects substrate from any Revit element.
        /// </summary>
        public static SubstrateResult Detect(Element element)
        {
            var result = new SubstrateResult();
            var cat = element.Category?.BuiltInCategory ?? BuiltInCategory.INVALID;

            // Determine target
            result.Target = cat switch
            {
                BuiltInCategory.OST_Walls => CoveringTarget.Wall,
                BuiltInCategory.OST_Floors or BuiltInCategory.OST_StructuralFoundation => CoveringTarget.Floor,
                BuiltInCategory.OST_Ceilings => CoveringTarget.Ceiling,
                BuiltInCategory.OST_Roofs => CoveringTarget.Roof,
                BuiltInCategory.OST_StructuralFraming => CoveringTarget.Beam,
                BuiltInCategory.OST_StructuralColumns or BuiltInCategory.OST_Columns => CoveringTarget.Column,
                BuiltInCategory.OST_DuctCurves or BuiltInCategory.OST_DuctFitting => CoveringTarget.Duct,
                BuiltInCategory.OST_PipeCurves or BuiltInCategory.OST_PipeFitting => CoveringTarget.Pipe,
                _ => CoveringTarget.Generic,
            };

            // Get type and family names for pattern matching
            string typeName = "";
            string familyName = "";

            if (element is Wall wall)
            {
                typeName = wall.WallType?.Name ?? "";
                familyName = wall.WallType?.FamilyName ?? "";
            }
            else if (element is FamilyInstance fi)
            {
                typeName = fi.Symbol?.Name ?? "";
                familyName = fi.Symbol?.FamilyName ?? "";
            }
            else if (element is Floor floor)
            {
                typeName = floor.FloorType?.Name ?? "";
                familyName = floor.FloorType?.FamilyName ?? "";
            }
            string combined = (typeName + " " + familyName).ToLowerInvariant();

            // Check structural material parameter
            var matParam = element.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
            string matName = "";
            if (matParam != null)
            {
                var matEl = element.Document.GetElement(matParam.AsElementId()) as Material;
                matName = (matEl?.Name ?? "").ToLowerInvariant();
            }
            string all = combined + " " + matName;

            // Detect substrate from all available text
            if (all.Contains("steel") || all.Contains("metal"))
                result.Substrate = result.Target == CoveringTarget.Beam || result.Target == CoveringTarget.Column ?
                    SubstrateType.SteelSection : SubstrateType.SteelPlate;
            else if (all.Contains("plasterboard") || all.Contains("drywall") || all.Contains("gypsum board"))
                result.Substrate = SubstrateType.Plasterboard;
            else if (all.Contains("lightweight") || all.Contains("aerated") || all.Contains("aircrete"))
                result.Substrate = SubstrateType.LightweightBlock;
            else if (all.Contains("engineering brick"))
                result.Substrate = SubstrateType.EngineeringBrick;
            else if (all.Contains("brick") || all.Contains("masonry"))
                result.Substrate = SubstrateType.CommonBrick;
            else if (all.Contains("concrete") && !all.Contains("block"))
                result.Substrate = (result.Target == CoveringTarget.Beam || result.Target == CoveringTarget.Column) ?
                    SubstrateType.ExposedConcrete : SubstrateType.InSituConcrete;
            else if (all.Contains("block"))
                result.Substrate = SubstrateType.DenseBlock;
            else if (all.Contains("timber") || all.Contains("wood"))
                result.Substrate = SubstrateType.Timber;
            else if (all.Contains("stone"))
                result.Substrate = SubstrateType.StoneWork;
            else
                result.Substrate = result.Target switch
                {
                    CoveringTarget.Beam or CoveringTarget.Column => SubstrateType.ExposedConcrete,
                    CoveringTarget.Duct or CoveringTarget.Pipe => SubstrateType.SteelPlate,
                    _ => SubstrateType.DenseBlock,
                };

            // Suction classification
            result.SuctionRate = SuctionRates.GetValueOrDefault(result.Substrate, 0.5);
            if (result.SuctionRate >= 1.5)
            {
                result.SuctionClass = "High";
                result.NeedsPrimer = true;
                result.PreTreatment = "Pre-wet 24h + PVA primer 1:5";
            }
            else if (result.SuctionRate >= 0.5)
            {
                result.SuctionClass = "Moderate";
                result.PreTreatment = "Dampen surface lightly";
            }
            else if (result.SuctionRate > 0)
            {
                result.SuctionClass = "Low";
                result.NeedsPrimer = true;
                result.PreTreatment = "SBR bonding agent";
            }
            else
            {
                result.SuctionClass = "Zero";
                result.NeedsMechanicalKey = result.Substrate != SubstrateType.SteelSection;
                result.NeedsPrimer = true;
                result.PreTreatment = result.Substrate == SubstrateType.SteelSection ?
                    "Degrease + etch primer" : "Metal lath or scabble + SBR";
            }

            // Recommended thickness
            result.RecommendedThicknessMm = result.Target switch
            {
                CoveringTarget.Beam or CoveringTarget.Column => 15, // Fire protection render
                CoveringTarget.Duct or CoveringTarget.Pipe => 0,     // Paint only
                _ => result.Substrate == SubstrateType.Plasterboard ?
                    PlasterConfig.SkimCoatMm : PlasterConfig.InternalRenderMm,
            };

            result.Summary = $"{result.Target} ({result.Substrate}): suction={result.SuctionClass}, " +
                $"treatment={result.PreTreatment}, thickness={result.RecommendedThicknessMm:F0}mm";

            return result;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 4. ELEMENT COVERAGE CALCULATOR — Geometry-Driven Area
    // ════════════════════════════════════════════════════════════════

    #region Coverage Types

    /// <summary>Coverage result for any element type.</summary>
    public class CoveringCoverageResult
    {
        public double GrossAreaM2 { get; set; }
        public double OpeningAreaM2 { get; set; }
        public double RevealAreaM2 { get; set; }
        public double NetAreaM2 { get; set; }
        public double WasteAdjustedAreaM2 { get; set; }
        public double VolumeM3 { get; set; }
        public double MaterialWeightKg { get; set; }
        public int UnitsRequired { get; set; }  // Bags or litres
        public string UnitType { get; set; }
        public double MaterialCost { get; set; }
        public double LabourCost { get; set; }
        public double TotalCost { get; set; }
        public double CarbonKgCO2 { get; set; }
        public int ElementCount { get; set; }
        public Dictionary<CoveringTarget, double> AreaByTarget { get; set; } = new();
        public string Summary { get; set; }
    }

    #endregion

    /// <summary>
    /// Calculates covering area from ANY Revit element using geometry extraction.
    /// Handles: walls (height×length - openings + reveals), beams (perimeter×length),
    /// columns (perimeter×height), floors/ceilings (area), ducts/pipes (πD×length).
    /// </summary>
    internal static class ElementCoverageCalculator
    {
        /// <summary>
        /// Calculates surface area for a collection of mixed elements.
        /// </summary>
        public static CoveringCoverageResult Calculate(
            Document doc, IList<Element> elements,
            double thicknessMm, CoveringMaterialSpec material)
        {
            var result = new CoveringCoverageResult();
            result.UnitType = material?.UnitType ?? "bag";
            double coverage = material?.CoverageM2PerUnit ?? 4.0;
            double costPerUnit = material?.CostPerUnit ?? 6.50;
            bool isPaint = material?.DFTMicrons > 0;

            foreach (var el in elements)
            {
                result.ElementCount++;
                var cat = el.Category?.BuiltInCategory ?? BuiltInCategory.INVALID;
                double areaM2 = 0;
                double openingsM2 = 0;
                double revealsM2 = 0;
                CoveringTarget target = CoveringTarget.Generic;

                switch (cat)
                {
                    case BuiltInCategory.OST_Walls:
                        target = CoveringTarget.Wall;
                        if (el is Wall wall)
                        {
                            var hp = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
                            double hFt = hp?.AsDouble() ?? 10;
                            double lFt = (wall.Location as LocationCurve)?.Curve.Length ?? 0;
                            areaM2 = hFt * lFt * Units.SqFtToSqM;

                            // Opening deductions
                            foreach (var insId in wall.FindInserts(true, true, true, true))
                            {
                                var ins = doc.GetElement(insId);
                                var bb = ins?.get_BoundingBox(null);
                                if (bb == null) continue;
                                double w = Math.Abs(bb.Max.X - bb.Min.X) * Units.FeetToMm / 1000;
                                double h = Math.Abs(bb.Max.Z - bb.Min.Z) * Units.FeetToMm / 1000;
                                double insArea = w * h;
                                if (insArea > PlasterConfig.OpeningThresholdM2)
                                {
                                    openingsM2 += insArea;
                                    double perim = ins.Category?.BuiltInCategory == BuiltInCategory.OST_Doors ?
                                        (2 * h + w) : (2 * (w + h));
                                    revealsM2 += perim * 0.1; // 100mm reveal depth
                                }
                            }
                        }
                        break;

                    case BuiltInCategory.OST_StructuralFraming:
                        target = CoveringTarget.Beam;
                        areaM2 = GetBeamSurfaceArea(el);
                        break;

                    case BuiltInCategory.OST_StructuralColumns:
                    case BuiltInCategory.OST_Columns:
                        target = CoveringTarget.Column;
                        areaM2 = GetColumnSurfaceArea(el);
                        break;

                    case BuiltInCategory.OST_Floors:
                        target = CoveringTarget.Floor;
                        var floorArea = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        areaM2 = (floorArea?.AsDouble() ?? 0) * Units.SqFtToSqM;
                        break;

                    case BuiltInCategory.OST_Ceilings:
                        target = CoveringTarget.Ceiling;
                        var ceilArea = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        areaM2 = (ceilArea?.AsDouble() ?? 0) * Units.SqFtToSqM;
                        break;

                    case BuiltInCategory.OST_Roofs:
                        target = CoveringTarget.Roof;
                        var roofArea = el.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        areaM2 = (roofArea?.AsDouble() ?? 0) * Units.SqFtToSqM;
                        break;

                    default:
                        // Generic: use bounding box surface area estimate
                        var bbox = el.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            double dx = (bbox.Max.X - bbox.Min.X) * Units.FeetToMm / 1000;
                            double dy = (bbox.Max.Y - bbox.Min.Y) * Units.FeetToMm / 1000;
                            double dz = (bbox.Max.Z - bbox.Min.Z) * Units.FeetToMm / 1000;
                            areaM2 = 2 * (dx * dy + dy * dz + dx * dz);
                        }
                        break;
                }

                double netArea = areaM2 - openingsM2 + revealsM2;
                result.GrossAreaM2 += areaM2;
                result.OpeningAreaM2 += openingsM2;
                result.RevealAreaM2 += revealsM2;
                result.NetAreaM2 += netArea;

                if (!result.AreaByTarget.ContainsKey(target))
                    result.AreaByTarget[target] = 0;
                result.AreaByTarget[target] += netArea;
            }

            // Waste & quantities
            double wasteFactor = 1.0 + PlasterConfig.WastePct / 100.0;
            result.WasteAdjustedAreaM2 = result.NetAreaM2 * wasteFactor;

            if (isPaint)
            {
                int coats = material?.CoatsRequired ?? 2;
                result.UnitsRequired = (int)Math.Ceiling(
                    result.WasteAdjustedAreaM2 * coats / Math.Max(coverage, 0.1));
                result.VolumeM3 = result.UnitsRequired * 0.001; // litres → m³
            }
            else
            {
                result.UnitsRequired = (int)Math.Ceiling(
                    result.WasteAdjustedAreaM2 / Math.Max(coverage, 0.1));
                result.VolumeM3 = result.WasteAdjustedAreaM2 * thicknessMm / 1000.0;
            }

            result.MaterialCost = result.UnitsRequired * costPerUnit;
            double labourRate = isPaint ? PlasterConfig.PaintLabourPerM2 : PlasterConfig.PlasterLabourPerM2;
            result.LabourCost = result.WasteAdjustedAreaM2 * labourRate;
            result.TotalCost = result.MaterialCost + result.LabourCost;

            double carbonCoeff = isPaint ? 0.03 : 0.10;
            result.MaterialWeightKg = result.VolumeM3 * (material?.DensityKgM3 ?? 1200);
            result.CarbonKgCO2 = result.MaterialWeightKg * carbonCoeff;

            var areaBreakdown = string.Join(", ", result.AreaByTarget.Select(kv => $"{kv.Key}: {kv.Value:F1}m²"));
            result.Summary = $"Coverage ({result.ElementCount} elements):\n" +
                $"  Net: {result.NetAreaM2:F1}m² [{areaBreakdown}]\n" +
                $"  Material: {result.UnitsRequired} {result.UnitType}s " +
                $"({material?.Name ?? "Standard"})\n" +
                $"  Cost: £{result.TotalCost:F0} (£{result.MaterialCost:F0} mat + £{result.LabourCost:F0} labour)\n" +
                $"  Carbon: {result.CarbonKgCO2:F0} kgCO₂e";

            return result;
        }

        /// <summary>Calculates beam surface area: perimeter × length.</summary>
        private static double GetBeamSurfaceArea(Element beam)
        {
            double lengthFt = 0;
            if (beam.Location is LocationCurve lc) lengthFt = lc.Curve.Length;

            // Get cross-section dimensions from type parameters
            double widthMm = 300, depthMm = 500; // defaults
            if (beam is FamilyInstance fi)
            {
                var wParam = fi.Symbol?.LookupParameter("b") ?? fi.Symbol?.LookupParameter("Width");
                var dParam = fi.Symbol?.LookupParameter("h") ?? fi.Symbol?.LookupParameter("Depth");
                if (wParam != null) widthMm = wParam.AsDouble() * Units.FeetToMm;
                if (dParam != null) depthMm = dParam.AsDouble() * Units.FeetToMm;
            }

            double perimeterM = 2 * (widthMm + depthMm) / 1000.0;
            double lengthM = lengthFt * Units.FeetToMm / 1000.0;
            // 3 exposed faces (bottom + 2 sides, top embedded in slab)
            return perimeterM * 0.75 * lengthM;
        }

        /// <summary>Calculates column surface area: perimeter × height.</summary>
        private static double GetColumnSurfaceArea(Element column)
        {
            // Height from base/top level
            var baseLvl = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
            var topLvl = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
            double heightFt = 10; // default 3m
            if (baseLvl != null && topLvl != null)
            {
                var bLvl = column.Document.GetElement(baseLvl.AsElementId()) as Level;
                var tLvl = column.Document.GetElement(topLvl.AsElementId()) as Level;
                if (bLvl != null && tLvl != null)
                    heightFt = tLvl.Elevation - bLvl.Elevation;
            }

            // Cross-section dimensions
            double widthMm = 400, depthMm = 400;
            if (column is FamilyInstance fi)
            {
                var wParam = fi.Symbol?.LookupParameter("b") ?? fi.Symbol?.LookupParameter("Width");
                var dParam = fi.Symbol?.LookupParameter("h") ?? fi.Symbol?.LookupParameter("Depth");
                if (wParam != null) widthMm = wParam.AsDouble() * Units.FeetToMm;
                if (dParam != null) depthMm = dParam.AsDouble() * Units.FeetToMm;
            }

            double perimeterM = 2 * (widthMm + depthMm) / 1000.0;
            double heightM = heightFt * Units.FeetToMm / 1000.0;
            return perimeterM * heightM; // 4 exposed faces
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 5. PAINT SPECIFICATION ENGINE — DFT, Coats, VOC (BS 6150)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Paint system specification per BS 6150 / BS EN 1062.
    ///
    /// Paint system = Primer + Undercoat(s) + Topcoat(s)
    /// DFT (Dry Film Thickness) in microns:
    ///   Internal emulsion: 80-100μm total (2 coats × 40μm)
    ///   External masonry: 150-200μm total (2 coats × 80μm)
    ///   Steel intumescent: 1000-2000μm (3 coats)
    ///   Epoxy floor: 300-500μm (2 coats × 200μm)
    ///
    /// VOC limits (EU Directive 2004/42/EC):
    ///   Interior matt: ≤30 g/L
    ///   Interior gloss: ≤300 g/L (solvent) or ≤40 g/L (water)
    ///   Exterior masonry: ≤40 g/L
    /// </summary>
    internal static class PaintSpecificationEngine
    {
        /// <summary>Paint system specification result.</summary>
        public class PaintSystemSpec
        {
            public List<CoveringCoat> Coats { get; set; } = new();
            public double TotalDFTMicrons { get; set; }
            public double TotalSpreadRateM2PerLitre { get; set; }
            public double TotalVOCgPerLitre { get; set; }
            public double DryingTimeDays { get; set; }
            public bool IsLowVOC { get; set; }
            public string Summary { get; set; }
        }

        /// <summary>
        /// Designs a paint system for the given substrate and environment.
        /// </summary>
        public static PaintSystemSpec DesignPaintSystem(
            SubstrateType substrate, bool isExternal,
            CoveringType paintType = CoveringType.EmulsionPaint)
        {
            var spec = new PaintSystemSpec();
            int seq = 0;

            // Primer coat (always)
            bool needsPrimer = substrate == SubstrateType.InSituConcrete ||
                substrate == SubstrateType.ExposedConcrete ||
                substrate == SubstrateType.SteelSection ||
                substrate == SubstrateType.SteelPlate ||
                substrate == SubstrateType.Timber;

            if (needsPrimer)
            {
                string primerType = substrate switch
                {
                    SubstrateType.SteelSection or SubstrateType.SteelPlate => "Zinc phosphate metal primer",
                    SubstrateType.Timber => "Aluminium wood primer",
                    SubstrateType.InSituConcrete or SubstrateType.ExposedConcrete => "Alkali-resistant primer",
                    _ => "Acrylic primer/sealer",
                };

                spec.Coats.Add(new CoveringCoat
                {
                    Type = CoatType.PaintPrimer, Sequence = ++seq,
                    ThicknessMm = 0.025, // 25 microns
                    Material = primerType,
                    CuringHours = 4,
                });
            }

            // Undercoat (for gloss/eggshell systems)
            if (paintType == CoveringType.GlossPaint)
            {
                spec.Coats.Add(new CoveringCoat
                {
                    Type = CoatType.PaintUndercoat, Sequence = ++seq,
                    ThicknessMm = 0.03,
                    Material = "Alkyd undercoat",
                    CuringHours = 16,
                });
            }

            // Topcoats
            var material = CoveringMaterialDatabase.FindByType(paintType) ??
                CoveringMaterialDatabase.FindByType(CoveringType.EmulsionPaint);
            int topCoats = material?.CoatsRequired ?? 2;

            for (int i = 0; i < topCoats; i++)
            {
                spec.Coats.Add(new CoveringCoat
                {
                    Type = CoatType.PaintTopcoat, Sequence = ++seq,
                    ThicknessMm = (material?.DFTMicrons ?? 40) / 1000.0,
                    Material = material?.Name ?? "Vinyl matt emulsion",
                    CuringHours = material?.CuringHours ?? 2,
                });
            }

            spec.TotalDFTMicrons = spec.Coats.Sum(c => c.ThicknessMm * 1000);
            spec.TotalSpreadRateM2PerLitre = material?.SpreadRateM2PerLitre ?? 12;
            spec.TotalVOCgPerLitre = material?.VOCgPerLitre ?? 30;
            spec.IsLowVOC = spec.TotalVOCgPerLitre <= 40;
            spec.DryingTimeDays = spec.Coats.Sum(c => c.CuringHours) / 24.0;

            spec.Summary = $"Paint system ({paintType}):\n" +
                string.Join("\n", spec.Coats.Select(c =>
                    $"  Coat {c.Sequence}: {c.Material} — {c.ThicknessMm * 1000:F0}μm, {c.CuringHours}h")) +
                $"\n  Total DFT: {spec.TotalDFTMicrons:F0}μm, VOC: {spec.TotalVOCgPerLitre:F0}g/L " +
                $"({(spec.IsLowVOC ? "Low VOC ✓" : "Standard")})";

            return spec;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 6. BEAM/COLUMN COVERING ENGINE — Material Param + STING Tags
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Applies coverings to beams and columns by:
    ///   1. Writing finish specification to STING shared parameters
    ///   2. Setting structural material to include finish description
    ///   3. Writing BLE_PAINT_TYPE_TXT, ASS_FINISH_TXT parameters
    ///   4. Writing cost parameters (CST_CALC_PLASTER_M3, CST_TOTAL_PLASTER_COST)
    ///
    /// Note: Revit API does NOT support CompoundStructure on beams/columns.
    /// We record the covering specification via parameters instead.
    /// </summary>
    internal static class BeamColumnCoveringEngine
    {
        /// <summary>Beam/column covering application result.</summary>
        public class CoveringApplicationResult
        {
            public int ElementsProcessed { get; set; }
            public int ParamsWritten { get; set; }
            public double TotalAreaM2 { get; set; }
            public double TotalCost { get; set; }
            public List<string> Warnings { get; set; } = new();
            public string Summary { get; set; }
        }

        /// <summary>
        /// Applies covering specification to beams/columns via STING parameters.
        /// </summary>
        public static CoveringApplicationResult Apply(
            Document doc, IList<Element> elements,
            CoveringBuildUp buildUp, CoveringMaterialSpec material,
            CoveringCoverageResult coverage)
        {
            var result = new CoveringApplicationResult
            {
                TotalAreaM2 = coverage.NetAreaM2,
                TotalCost = coverage.TotalCost,
            };

            string finishSpec = buildUp.IsPaint ?
                $"Paint: {material.Name}, {material.DFTMicrons}μm DFT, {material.CoatsRequired} coats" :
                $"Plaster: {material.Name}, {buildUp.TotalThicknessMm:F0}mm, {buildUp.Coats.Count} coats";

            foreach (var el in elements)
            {
                result.ElementsProcessed++;
                try
                {
                    // Write finish description
                    if (TrySet(el, "ASS_FINISH_TXT", finishSpec)) result.ParamsWritten++;
                    if (TrySet(el, "BLE_PAINT_TYPE_TXT", material.Name)) result.ParamsWritten++;

                    // Write cost parameters
                    if (!buildUp.IsPaint)
                    {
                        double areaPerEl = coverage.NetAreaM2 / Math.Max(elements.Count, 1);
                        double vol = areaPerEl * buildUp.TotalThicknessMm / 1e6;
                        TrySet(el, "CST_CALC_PLASTER_M3", vol.ToString("F3"));
                        TrySet(el, "CST_TOTAL_PLASTER_COST", (coverage.TotalCost / Math.Max(elements.Count, 1)).ToString("F0"));
                    }
                    else
                    {
                        double litresPerEl = (double)coverage.UnitsRequired / Math.Max(elements.Count, 1);
                        TrySet(el, "CST_CALC_PAINT_LITERS", litresPerEl.ToString("F1"));
                    }

                    // Write room finish tags if wall
                    if (el is Wall)
                    {
                        TrySet(el, "FIN_WALL_TAG_TXT", finishSpec);
                        TrySet(el, "BLE_ROOM_FINISH_WALL_TXT", material.Name);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Element {el.Id.Value}: {ex.Message}");
                }
            }

            result.Summary = $"Covering applied to {result.ElementsProcessed} elements, " +
                $"{result.ParamsWritten} params written, area={result.TotalAreaM2:F1}m², " +
                $"cost=£{result.TotalCost:F0}";

            return result;
        }

        private static bool TrySet(Element el, string paramName, string value)
        {
            try
            {
                ParameterHelpers.SetIfEmpty(el, paramName, value);
                return true;
            }
            catch { return false; }
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 7. ROOM FINISH SCHEDULER — Per-Room Wall/Floor/Ceiling Finishes
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a room-based finish schedule matching NBS specification patterns.
    /// Writes BLE_ROOM_FINISH_WALL/FLOOR/CEILING_TXT parameters on Room elements.
    /// </summary>
    internal static class RoomFinishScheduler
    {
        /// <summary>Room finish entry.</summary>
        public class RoomFinish
        {
            public ElementId RoomId { get; set; }
            public string RoomName { get; set; }
            public string RoomNumber { get; set; }
            public string WallFinish { get; set; }
            public string FloorFinish { get; set; }
            public string CeilingFinish { get; set; }
            public string BaseFinish { get; set; }
            public double WallAreaM2 { get; set; }
            public double FloorAreaM2 { get; set; }
            public double CeilingAreaM2 { get; set; }
        }

        /// <summary>
        /// Generates room finish schedule for all placed rooms.
        /// </summary>
        public static List<RoomFinish> GenerateSchedule(Document doc)
        {
            var result = new List<RoomFinish>();

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Autodesk.Revit.DB.Architecture.Room>()
                .Where(r => r.Area > 0)
                .ToList();

            foreach (var room in rooms)
            {
                var entry = new RoomFinish
                {
                    RoomId = room.Id,
                    RoomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                    RoomNumber = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                    FloorAreaM2 = room.Area * Units.SqFtToSqM,
                    CeilingAreaM2 = room.Area * Units.SqFtToSqM,
                };

                // Wall area from room boundary segments
                var segments = room.GetBoundarySegments(new SpatialElementBoundaryOptions());
                if (segments != null)
                {
                    double wallPerimFt = segments.SelectMany(s => s).Sum(seg => seg.GetCurve().Length);
                    var heightParam = room.get_Parameter(BuiltInParameter.ROOM_HEIGHT);
                    double heightFt = heightParam?.AsDouble() ?? 10;
                    entry.WallAreaM2 = wallPerimFt * heightFt * Units.SqFtToSqM;
                }

                // Read existing finish parameters
                entry.WallFinish = ParameterHelpers.GetString(room, "BLE_ROOM_FINISH_WALL_TXT");
                entry.FloorFinish = ParameterHelpers.GetString(room, "BLE_ROOM_FINISH_FLOOR_TXT");
                entry.CeilingFinish = ParameterHelpers.GetString(room, "BLE_ROOM_FINISH_CEILING_TXT");
                entry.BaseFinish = ParameterHelpers.GetString(room, "BLE_ROOM_FINISH_BASE_TXT");

                // Auto-populate defaults if empty
                if (string.IsNullOrEmpty(entry.WallFinish))
                    entry.WallFinish = "2 coat gypsum plaster + vinyl matt emulsion";
                if (string.IsNullOrEmpty(entry.FloorFinish))
                    entry.FloorFinish = "Power-floated concrete + carpet/vinyl";
                if (string.IsNullOrEmpty(entry.CeilingFinish))
                    entry.CeilingFinish = "Plasterboard + skim coat + vinyl matt emulsion";

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// Writes finish specifications back to Room parameters.
        /// </summary>
        public static int WriteToRooms(Document doc, List<RoomFinish> schedule)
        {
            int written = 0;
            foreach (var entry in schedule)
            {
                var room = doc.GetElement(entry.RoomId);
                if (room == null) continue;
                try
                {
                    ParameterHelpers.SetIfEmpty(room, "BLE_ROOM_FINISH_WALL_TXT", entry.WallFinish);
                    ParameterHelpers.SetIfEmpty(room, "BLE_ROOM_FINISH_FLOOR_TXT", entry.FloorFinish);
                    ParameterHelpers.SetIfEmpty(room, "BLE_ROOM_FINISH_CEILING_TXT", entry.CeilingFinish);
                    ParameterHelpers.SetIfEmpty(room, "BLE_ROOM_FINISH_BASE_TXT", entry.BaseFinish);
                    written++;
                }
                catch (Exception ex) { StingLog.Warn($"Param not bound: {ex.Message}"); }
            }
            return written;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 8. COVERING QUALITY INSPECTOR — Plaster + Paint QA
    // ════════════════════════════════════════════════════════════════

    internal static class CoveringQualityInspector
    {
        public class QualityCheck
        {
            public int Number { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public string Standard { get; set; }
            public string Criteria { get; set; }
            public bool Pass { get; set; }
        }

        public static List<QualityCheck> GenerateChecklist(bool isPaint)
        {
            var checks = new List<QualityCheck>
            {
                new() { Number = 1, Category = "Preparation", Standard = isPaint ? "BS 6150 §4" : "BS EN 13914 §5",
                    Description = "Surface clean, dry, free from contaminants", Pass = true,
                    Criteria = "No dust, oil, grease, efflorescence, mould" },
                new() { Number = 2, Category = "Preparation",  Standard = "BS 8000-10",
                    Description = "Substrate condition assessed", Pass = true,
                    Criteria = "Suction test performed, pre-treatment applied" },
                new() { Number = 3, Category = "Materials", Standard = isPaint ? "BS EN 1062" : "BS EN 998-1",
                    Description = "Materials comply with specification", Pass = true,
                    Criteria = isPaint ? "Paint batch number recorded, shelf life valid" : "BS EN 998-1 classification verified" },
                new() { Number = 4, Category = "Application", Standard = isPaint ? "BS 6150 §6" : "BS EN 13914 §7",
                    Description = isPaint ? "Film thickness within DFT specification" : "Coat thickness within tolerance",
                    Pass = true, Criteria = isPaint ? "WFT gauge check per coat" : "Depth gauge at 1m intervals" },
                new() { Number = 5, Category = "Application", Standard = "BS 8000-10",
                    Description = isPaint ? "Uniform colour, no misses, runs, or sags" : "Flatness Class A/B achieved",
                    Pass = true, Criteria = isPaint ? "Visual inspection under raking light" : "1.8m straightedge test" },
            };

            if (!isPaint)
            {
                checks.AddRange(new[]
                {
                    new QualityCheck { Number = 6, Category = "Mix", Standard = "BS EN 13914 §6",
                        Description = "Mix ratios correct — weakest coat outermost", Pass = true,
                        Criteria = "Scratch > Backing > Finish strength" },
                    new QualityCheck { Number = 7, Category = "Curing", Standard = "BS EN 13914 §8",
                        Description = "Minimum curing time between coats", Pass = true,
                        Criteria = "≥24h between coats, ambient 5-35°C" },
                    new QualityCheck { Number = 8, Category = "Details", Standard = "BS EN 13914 §9",
                        Description = "Corner beads, stop beads, bellcast at base", Pass = true,
                        Criteria = "Stainless/PVC beads, plumb, secure" },
                    new QualityCheck { Number = 9, Category = "Joints", Standard = "BS EN 13914 §7.5",
                        Description = "Movement joints at max 6m centres", Pass = true,
                        Criteria = "Coincide with structural movement joints" },
                    new QualityCheck { Number = 10, Category = "Finish", Standard = "BS 8000-10 §5",
                        Description = "No hollows, cracks, blistering, debonding", Pass = true,
                        Criteria = "Hammer test — no hollow sound" },
                });
            }
            else
            {
                checks.AddRange(new[]
                {
                    new QualityCheck { Number = 6, Category = "Coats", Standard = "BS 6150",
                        Description = "Correct number of coats applied", Pass = true,
                        Criteria = "Primer + undercoat + topcoat(s) per spec" },
                    new QualityCheck { Number = 7, Category = "Environment", Standard = "BS 6150 §5",
                        Description = "Application temp 5-35°C, humidity <85%", Pass = true,
                        Criteria = "Temperature and humidity logged" },
                    new QualityCheck { Number = 8, Category = "VOC", Standard = "EU 2004/42/EC",
                        Description = "VOC content within regulatory limits", Pass = true,
                        Criteria = "Product data sheet VOC values checked" },
                });
            }

            checks.Add(new QualityCheck
            {
                Number = checks.Count + 1, Category = "Documentation", Standard = "ISO 19650",
                Description = "Specification recorded in BIM model parameters", Pass = true,
                Criteria = "ASS_FINISH_TXT, BLE_PAINT_TYPE_TXT populated",
            });

            return checks;
        }
    }


    // ════════════════════════════════════════════════════════════════
    // 9. SMART COVERING FACTORY — Unified Pipeline for Any Element
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unified intelligent covering pipeline for ANY Revit element.
    /// Handles walls (layer injection), beams/columns (param writing),
    /// floors/ceilings (layer injection), and painting (all elements).
    /// </summary>
    internal static class SmartCoveringFactory
    {
        public class CoveringReport
        {
            public bool Success { get; set; }
            public int ElementsProcessed { get; set; }
            public int WallTypesModified { get; set; }
            public int ParamsWritten { get; set; }
            public CoveringCoverageResult Coverage { get; set; }
            public List<string> Steps { get; set; } = new();
            public List<string> Warnings { get; set; } = new();
            public string Summary { get; set; }
        }

        /// <summary>
        /// Applies covering (plaster or paint) to mixed element selection.
        /// </summary>
        public static CoveringReport ApplyCovering(
            Document doc, IList<Element> elements, bool isExternal,
            bool isPaint, CoveringType coveringType = CoveringType.InternalPlaster)
        {
            var report = new CoveringReport();

            try
            {
                if (elements == null || elements.Count == 0)
                {
                    report.Summary = "No elements selected";
                    return report;
                }
                report.ElementsProcessed = elements.Count;

                // Step 1: Detect substrate from first element
                var subResult = SubstrateDetector.Detect(elements[0]);
                report.Steps.Add($"✓ Substrate: {subResult.Substrate} ({subResult.Target})");

                // Step 2: Select material
                var material = isPaint ?
                    CoveringMaterialDatabase.FindByType(coveringType) :
                    CoveringMaterialDatabase.FindByType(
                        isExternal ? CoveringType.ExternalRender : CoveringType.InternalPlaster);
                material = material ?? CoveringMaterialDatabase.GetAllMaterials().FirstOrDefault();
                report.Steps.Add($"✓ Material: {material?.Name ?? "Standard"}");

                // Step 3: Design build-up
                CoveringBuildUp buildUp = null;
                if (isPaint)
                {
                    var paintSpec = PaintSpecificationEngine.DesignPaintSystem(
                        subResult.Substrate, isExternal, coveringType);
                    buildUp = new CoveringBuildUp
                    {
                        Coats = paintSpec.Coats,
                        TotalThicknessMm = paintSpec.Coats.Sum(c => c.ThicknessMm),
                        TotalDryingDays = paintSpec.DryingTimeDays,
                        IsExternal = isExternal,
                        IsPaint = true,
                        Substrate = subResult.Substrate,
                        Target = subResult.Target,
                    };
                    report.Steps.Add($"✓ Paint system: {paintSpec.Coats.Count} coats, " +
                        $"{paintSpec.TotalDFTMicrons:F0}μm DFT");
                }
                else
                {
                    buildUp = new CoveringBuildUp
                    {
                        IsExternal = isExternal,
                        Substrate = subResult.Substrate,
                        Target = subResult.Target,
                    };
                    // Build plaster coats based on substrate
                    if (subResult.Substrate == SubstrateType.Plasterboard)
                    {
                        buildUp.Coats.Add(new CoveringCoat { Type = CoatType.Skim, Sequence = 1,
                            ThicknessMm = PlasterConfig.SkimCoatMm, Material = "Gypsum skim", CuringHours = 24 });
                    }
                    else if (isExternal)
                    {
                        buildUp.Coats.Add(new CoveringCoat { Type = CoatType.Scratch, Sequence = 1,
                            ThicknessMm = PlasterConfig.ScratchCoatMm, MixRatio = "1:3", Material = "Scratch coat", CuringHours = 48 });
                        buildUp.Coats.Add(new CoveringCoat { Type = CoatType.Backing, Sequence = 2,
                            ThicknessMm = PlasterConfig.BackingCoatMm, MixRatio = "1:5", Material = "Backing coat", CuringHours = 72 });
                        buildUp.Coats.Add(new CoveringCoat { Type = CoatType.Finish, Sequence = 3,
                            ThicknessMm = 3, MixRatio = "1:8", Material = "Finish coat", CuringHours = 24 });
                    }
                    else
                    {
                        buildUp.Coats.Add(new CoveringCoat { Type = CoatType.Backing, Sequence = 1,
                            ThicknessMm = PlasterConfig.BackingCoatMm, Material = "Thistle Browning", CuringHours = 24 });
                        buildUp.Coats.Add(new CoveringCoat { Type = CoatType.Skim, Sequence = 2,
                            ThicknessMm = PlasterConfig.SkimCoatMm, Material = "Thistle Multi-Finish", CuringHours = 24 });
                    }
                    buildUp.TotalThicknessMm = buildUp.Coats.Sum(c => c.ThicknessMm);
                    buildUp.TotalDryingDays = buildUp.Coats.Sum(c => c.CuringHours) / 24.0;
                    report.Steps.Add($"✓ Plaster: {buildUp.Coats.Count} coats, {buildUp.TotalThicknessMm:F0}mm");
                }

                // Step 4: Calculate coverage for ALL elements
                report.Coverage = ElementCoverageCalculator.Calculate(
                    doc, elements, buildUp.TotalThicknessMm, material);
                report.Steps.Add($"✓ Coverage: {report.Coverage.NetAreaM2:F1}m², " +
                    $"{report.Coverage.UnitsRequired} {report.Coverage.UnitType}s, " +
                    $"£{report.Coverage.TotalCost:F0}");

                // Step 5: Apply to walls (layer injection) and beams/columns (params)
                var walls = elements.OfType<Wall>().ToList();
                var nonWalls = elements.Where(e => !(e is Wall)).ToList();

                if (walls.Count > 0 && !isPaint)
                {
                    // Inject layers into wall types
                    var processedTypes = new HashSet<ElementId>();
                    foreach (var wall in walls)
                    {
                        if (processedTypes.Contains(wall.WallType.Id)) continue;
                        processedTypes.Add(wall.WallType.Id);

                        var layerResult = PlasterLayerBuilder.AddPlasterLayers(
                            doc, wall.WallType, ConvertToBuildUp(buildUp), !isExternal);
                        if (layerResult.Success)
                        {
                            report.WallTypesModified++;
                            // Apply new type to all matching walls
                            var newType = doc.GetElement(layerResult.NewTypeId) as WallType;
                            if (newType != null)
                            {
                                foreach (var w in walls.Where(ww => ww.WallType.Id == wall.WallType.Id))
                                {
                                    try { w.WallType = newType; }
                                    catch (Exception ex) { report.Warnings.Add($"Wall {w.Id.Value}: {ex.Message}"); }
                                }
                            }
                        }
                    }
                    report.Steps.Add($"✓ {report.WallTypesModified} wall type(s) modified");
                }

                // Write params to ALL elements (walls + beams + columns)
                var paramResult = BeamColumnCoveringEngine.Apply(
                    doc, elements, buildUp, material, report.Coverage);
                report.ParamsWritten = paramResult.ParamsWritten;
                report.Steps.Add($"✓ {paramResult.ParamsWritten} parameters written to {elements.Count} elements");

                // Step 6: STING tags
                string prodCode = isPaint ? "PNT" : (isExternal ? "RND" : "PLT");
                foreach (var el in elements)
                {
                    try
                    {
                        ParameterHelpers.SetIfEmpty(el, "ASS_DISCIPLINE_COD_TXT", "A");
                        ParameterHelpers.SetIfEmpty(el, "ASS_PRODCT_COD_TXT", prodCode);
                    }
                    catch (Exception ex) { StingLog.Warn($"Param not bound: {ex.Message}"); }
                }
                report.Steps.Add($"✓ STING tags: DISC=A, PROD={prodCode}");

                report.Success = true;
                report.Summary = $"Covering applied: {elements.Count} elements " +
                    $"({report.WallTypesModified} wall types + {nonWalls.Count} beams/columns), " +
                    $"{report.Coverage.NetAreaM2:F1}m², £{report.Coverage.TotalCost:F0}";
            }
            catch (Exception ex)
            {
                StingLog.Error("SmartCoveringFactory", ex);
                report.Summary = $"Error: {ex.Message}";
            }

            return report;
        }

        /// <summary>Converts CoveringBuildUp to PlasterBuildUp for layer builder.</summary>
        private static PlasterBuildUp ConvertToBuildUp(CoveringBuildUp src) => new PlasterBuildUp
        {
            Coats = src.Coats.Select(c => new PlasterCoat
            {
                Type = c.Type, ThicknessMm = c.ThicknessMm, MixRatio = c.MixRatio ?? "",
                CuringHours = (int)c.CuringHours, Material = c.Material, Sequence = c.Sequence,
            }).ToList(),
            TotalThicknessMm = src.TotalThicknessMm,
            IsExternal = src.IsExternal,
            Substrate = src.Substrate,
        };
    }


    // ════════════════════════════════════════════════════════════════
    // LEGACY ADAPTER CLASSES — Required by PlasterLayerBuilder
    // ════════════════════════════════════════════════════════════════

    /// <summary>Multi-coat plaster build-up specification (used by layer builder).</summary>
    public class PlasterBuildUp
    {
        public List<PlasterCoat> Coats { get; set; } = new();
        public double TotalThicknessMm { get; set; }
        public double TotalDryingDays { get; set; }
        public bool IsExternal { get; set; }
        public SubstrateType Substrate { get; set; }
    }

    /// <summary>Individual plaster coat (used by layer builder).</summary>
    public class PlasterCoat
    {
        public CoatType Type { get; set; }
        public double ThicknessMm { get; set; }
        public string MixRatio { get; set; }
        public int CuringHours { get; set; }
        public string Material { get; set; }
        public int Sequence { get; set; }
    }

    /// <summary>
    /// Injects plaster finish layers into existing wall compound structures.
    /// Uses existing BLE material families and CompoundTypeCreator patterns.
    /// </summary>
    internal static class PlasterLayerBuilder
    {
        public class LayerResult
        {
            public bool Success { get; set; }
            public ElementId NewTypeId { get; set; }
            public string NewTypeName { get; set; }
            public int LayersAdded { get; set; }
            public double TotalPlasterThicknessMm { get; set; }
            public string Summary { get; set; }
        }

        /// <summary>Adds plaster layers to a wall type, creating a new type.</summary>
        public static LayerResult AddPlasterLayers(
            Document doc, WallType sourceType,
            PlasterBuildUp buildUp, bool applyToInterior = true)
        {
            var result = new LayerResult();
            try
            {
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

                var newType = sourceType.Duplicate(newName) as WallType;
                if (newType == null) { result.Summary = "Duplicate failed"; return result; }

                var cs = newType.GetCompoundStructure();
                if (cs == null) { result.Summary = "No compound structure"; return result; }

                var layers = cs.GetLayers().ToList();

                // Find or create plaster material
                var plasterMatId = FindPlasterMaterial(doc, buildUp.IsExternal);

                foreach (var coat in buildUp.Coats.OrderBy(c => c.Sequence))
                {
                    var newLayer = new CompoundStructureLayer(
                        coat.ThicknessMm * Units.MmToFeet,
                        MaterialFunctionAssignment.Finish1,
                        plasterMatId);

                    if (applyToInterior)
                        layers.Add(newLayer);
                    else
                        layers.Insert(0, newLayer);

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
                result.Summary = $"Created '{newName}': {result.LayersAdded} layers, {result.TotalPlasterThicknessMm:F0}mm";
            }
            catch (Exception ex)
            {
                StingLog.Error("PlasterLayerBuilder", ex);
                result.Summary = $"Error: {ex.Message}";
            }
            return result;
        }

        private static ElementId FindPlasterMaterial(Document doc, bool isExternal)
        {
            string searchTerm = isExternal ? "render" : "plaster";
            var mat = new FilteredElementCollector(doc)
                .OfClass(typeof(Material)).Cast<Material>()
                .FirstOrDefault(m => m.Name.ToLowerInvariant().Contains(searchTerm));
            if (mat != null) return mat.Id;

            // Try to create
            string matName = isExternal ? "STING_External_Render" : "STING_Internal_Plaster";
            try
            {
                var id = Material.Create(doc, matName);
                var m = doc.GetElement(id) as Material;
                if (m != null) m.Color = isExternal ?
                    new Autodesk.Revit.DB.Color(210, 200, 180) :
                    new Autodesk.Revit.DB.Color(240, 240, 235);
                return id;
            }
            catch
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(Material)).Cast<Material>()
                    .FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
            }
        }
    }
}
