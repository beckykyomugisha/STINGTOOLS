using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Shared material creation helper for BLE and MEP commands.
    /// Implements base-material duplication: finds native Revit material specified
    /// in CSV column BLE_APP-REVIT-BASE-MATERIAL, duplicates it to inherit
    /// appearance/structural/thermal assets, then applies CSV overrides.
    /// </summary>
    internal static class MaterialPropertyHelper
    {
        // SAFETY-001: Resets the static "shared parameter not bound" warning counter
        // so subsequent runs of CreateBLE/MEPMaterials commands re-emit warnings on
        // first encounter rather than silently suppressing them. Currently a no-op
        // (the engine emits warnings unconditionally), retained for caller compatibility.
        public static void ResetSharedParamWarnings() { }

        // CSV column indices (0-based, matches MATERIAL_SCHEMA.json column_order)
        // --- Identity & Classification ---
        public const int ColSourceSheet = 0;        // SOURCE_SHEET
        public const int ColDiscipline = 1;         // MAT_DISCIPLINE
        public const int ColIso19650Id = 2;         // MAT_ISO_19650_ID
        public const int ColCode = 3;               // MAT_CODE
        public const int ColElementType = 4;        // MAT_ELEMENT_TYPE
        public const int ColCategory = 5;           // MAT_CATEGORY
        public const int ColName = 6;               // MAT_NAME
        public const int ColApplication = 7;        // MAT_APPLICATION
        public const int ColLocation = 8;           // MAT_LOCATION
        public const int ColThicknessMm = 9;        // MAT_THICKNESS_MM
        public const int ColThicknessInch = 10;     // MAT_THICKNESS_INCH
        public const int ColCostUsd = 11;           // MAT_COST_UNIT_USD
        public const int ColCostUgx = 12;           // MAT_COST_UNIT_UGX
        public const int ColDurability = 13;        // MAT_DURABILITY
        public const int ColSpecifications = 14;    // MAT_SPECIFICATIONS
        public const int ColLayerCount = 15;        // MAT_LAYER_COUNT
        public const int ColLayer1Material = 16;    // MAT_LAYER_1_MATERIAL
        public const int ColLayer1Thickness = 17;   // MAT_LAYER_1_THICKNESS_MM
        public const int ColLayer1Function = 18;    // MAT_LAYER_1_FUNCTION
        public const int ColLayer2Material = 19;    // MAT_LAYER_2_MATERIAL
        public const int ColLayer2Thickness = 20;   // MAT_LAYER_2_THICKNESS_MM
        public const int ColLayer2Function = 21;    // MAT_LAYER_2_FUNCTION
        public const int ColLayer3Material = 22;    // MAT_LAYER_3_MATERIAL
        public const int ColLayer3Thickness = 23;   // MAT_LAYER_3_THICKNESS_MM
        public const int ColLayer3Function = 24;    // MAT_LAYER_3_FUNCTION
        public const int ColLayer4Material = 25;    // MAT_LAYER_4_MATERIAL
        public const int ColLayer4Thickness = 26;   // MAT_LAYER_4_THICKNESS_MM
        public const int ColLayer4Function = 27;    // MAT_LAYER_4_FUNCTION
        public const int ColLayer5Material = 28;    // MAT_LAYER_5_MATERIAL
        public const int ColLayer5Thickness = 29;   // MAT_LAYER_5_THICKNESS_MM
        public const int ColLayer5Function = 30;    // MAT_LAYER_5_FUNCTION
        public const int ColManufacturer = 31;      // MAT_MANUFACTURER
        public const int ColStandard = 32;          // MAT_STANDARD
        public const int ColFeatures = 33;          // MAT_FEATURES
        public const int ColBaseMaterial = 34;      // BLE_APP-REVIT-BASE-MATERIAL
        public const int ColIdentityClass = 35;     // BLE_APP-IDENTITY-CLASS
        public const int ColColor = 36;             // BLE_APP-COLOR
        public const int ColTransparency = 37;      // BLE_APP-TRANSPARENCY
        public const int ColSmoothness = 38;        // BLE_APP-SMOOTHNESS
        public const int ColShininess = 39;         // BLE_APP-SHININESS
        public const int ColSurfaceFgColor = 40;    // BLE_APP-SURFACE-FG-COLOR
        public const int ColSurfaceFgPattern = 41;  // BLE_APP-SURFACE-FG-PATTERN
        public const int ColCutFgColor = 42;        // BLE_APP-CUT-FG-COLOR
        public const int ColCutFgPattern = 43;      // BLE_APP-CUT-FG-PATTERN
        public const int ColImage = 44;             // BLE_APP-IMAGE
        public const int ColDescription = 45;       // BLE_APP-DESCRIPTION
        public const int ColComments = 46;          // BLE_APP-COMMENTS
        public const int ColSurfaceBgPattern = 47;  // BLE_APP-SURFACE-BG-PATTERN
        public const int ColSurfaceBgColor = 48;    // BLE_APP-SURFACE-BG-COLOR
        public const int ColCutBgPattern = 49;      // BLE_APP-CUT-BG-PATTERN
        public const int ColCutBgColor = 50;        // BLE_APP-CUT-BG-COLOR
        public const int ColCutPatternName = 64;    // BLE_APP-CUT_PATTERN
        public const int ColSurfacePatternName = 65;// BLE_APP-SURFACE_PATTERN
        public const int ColBgPatternName = 66;     // BLE_APP-BACKGROUND_PATTERN
        public const int ColShadingRgb = 67;        // BLE_APP-SHADING_RGB
        // --- Physical / Thermal / Performance properties ---
        public const int ColPhysicalAsset = 51;     // BLE_APP-PHYSICAL-ASSET
        public const int ColThermalAsset = 52;      // BLE_APP-THERMAL-ASSET
        public const int ColTextureUrl = 53;        // BLE_MAT_TEXTURE_URL
        public const int ColDensity = 54;           // PROP_DENSITY_KG_M3
        public const int ColThermalCond = 55;       // PROP_THERMAL_COND_W_MK
        public const int ColThermalRes = 56;        // PROP_THERMAL_RES_M2K_W
        public const int ColSpecificHeat = 57;      // PROP_SPECIFIC_HEAT_J_KGK
        public const int ColFireRating = 58;        // PROP_FIRE_RATING
        public const int ColAcousticAbs = 59;       // PROP_ACOUSTIC_ABS
        public const int ColSoundRed = 60;          // PROP_SOUND_RED_DB
        public const int ColCarbon = 61;            // PROP_CARBON_KG_M3
        public const int ColCostAssemblyUsd = 62;   // MAT_COST_ASSEMBLY_USD
        public const int ColCostAssemblyUgx = 63;   // MAT_COST_ASSEMBLY_UGX
        public const int ColCompStrength = 68;      // PROP_COMP_STRENGTH_MPA
        public const int ColTensStrength = 69;      // PROP_TENS_STRENGTH_MPA

        /// <summary>
        /// Column-to-parameter mappings for populating ALL CSV data as shared parameters.
        /// Each entry: (column_index, parameter_name_to_look_up).
        /// Uses LookupParameter to find shared params bound to the Material category.
        /// </summary>
        private static readonly (int col, string paramName)[] SharedParamMappings = new[]
        {
            // Classification
            (ColDiscipline,     "MAT_DISCIPLINE"),
            (ColIso19650Id,     "MAT_ISO_19650_ID"),
            (ColCode,           "MAT_CODE"),
            (ColElementType,    "MAT_ELEMENT_TYPE"),
            (ColCategory,       "MAT_CATEGORY"),
            (ColApplication,    "MAT_APPLICATION"),
            (ColLocation,       "MAT_LOCATION"),
            // Dimensions
            (ColThicknessMm,    "MAT_THICKNESS_MM"),
            (ColThicknessInch,  "MAT_THICKNESS_INCH"),
            // Cost — names must match MR_PARAMETERS.txt exactly
            (ColCostUsd,        "MAT_COST_USD"),
            (ColCostUgx,        "MAT_COST_UGX"),
            (ColCostAssemblyUsd,"MAT_COST_ASSEMBLY_USD"),
            (ColCostAssemblyUgx,"MAT_COST_ASSEMBLY_UGX"),
            // Identity
            (ColName,           "MAT_NAME"),
            (ColManufacturer,   "MAT_MANUFACTURER"),
            // Layer composition
            (ColLayerCount,     "MAT_LAYER_COUNT"),
            (ColLayer1Material, "MAT_LAYER_1_MATERIAL"),
            (ColLayer1Thickness,"MAT_LAYER_1_THICKNESS_MM"),
            (ColLayer1Function, "MAT_LAYER_1_FUNCTION"),
            (ColLayer2Material, "MAT_LAYER_2_MATERIAL"),
            (ColLayer2Thickness,"MAT_LAYER_2_THICKNESS_MM"),
            (ColLayer2Function, "MAT_LAYER_2_FUNCTION"),
            (ColLayer3Material, "MAT_LAYER_3_MATERIAL"),
            (ColLayer3Thickness,"MAT_LAYER_3_THICKNESS_MM"),
            (ColLayer3Function, "MAT_LAYER_3_FUNCTION"),
            (ColLayer4Material, "MAT_LAYER_4_MATERIAL"),
            (ColLayer4Thickness,"MAT_LAYER_4_THICKNESS_MM"),
            (ColLayer4Function, "MAT_LAYER_4_FUNCTION"),
            (ColLayer5Material, "MAT_LAYER_5_MATERIAL"),
            (ColLayer5Thickness,"MAT_LAYER_5_THICKNESS_MM"),
            (ColLayer5Function, "MAT_LAYER_5_FUNCTION"),
            // Durability & Specs
            (ColDurability,     "MAT_DURABILITY"),
            (ColSpecifications, "MAT_SPECIFICATIONS"),
            (ColFeatures,       "MAT_FEATURES"),
            (ColStandard,       "MAT_STANDARD"),
            // Physical Properties
            (ColDensity,        "PROP_DENSITY_KG_M3"),
            (ColThermalCond,    "PROP_THERMAL_COND_W_MK"),
            (ColThermalRes,     "PROP_THERMAL_RES_M2K_W"),
            (ColSpecificHeat,   "PROP_SPECIFIC_HEAT_J_KGK"),
            (ColFireRating,     "PROP_FIRE_RATING"),
            (ColAcousticAbs,    "PROP_ACOUSTIC_ABS"),
            (ColSoundRed,       "PROP_SOUND_RED_DB"),
            (ColCarbon,         "PROP_CARBON_KG_M3"),
            (ColCompStrength,   "PROP_COMP_STRENGTH_MPA"),
            (ColTensStrength,   "PROP_TENS_STRENGTH_MPA"),
            // Assets — hyphens, not underscores (must match MR_PARAMETERS.txt)
            (ColPhysicalAsset,  "BLE_APP-PHYSICAL-ASSET"),
            (ColThermalAsset,   "BLE_APP-THERMAL-ASSET"),
            (ColTextureUrl,     "BLE_MAT_TEXTURE_URL"),
            // --- BLE_APP-* appearance/visual properties (stored as shared params for scheduling) ---
            (ColBaseMaterial,       "BLE_APP-REVIT-BASE-MATERIAL"),
            (ColIdentityClass,      "BLE_APP-IDENTITY-CLASS"),
            (ColColor,              "BLE_APP-COLOR"),
            (ColTransparency,       "BLE_APP-TRANSPARENCY"),
            (ColSmoothness,         "BLE_APP-SMOOTHNESS"),
            (ColShininess,          "BLE_APP-SHININESS"),
            (ColSurfaceFgColor,     "BLE_APP-SURFACE-FG-COLOR"),
            (ColSurfaceFgPattern,   "BLE_APP-SURFACE-FG-PATTERN"),
            (ColCutFgColor,         "BLE_APP-CUT-FG-COLOR"),
            (ColCutFgPattern,       "BLE_APP-CUT-FG-PATTERN"),
            (ColImage,              "BLE_APP-IMAGE"),
            (ColDescription,        "BLE_APP-DESCRIPTION"),
            (ColComments,           "BLE_APP-COMMENTS"),
            (ColSurfaceBgPattern,   "BLE_APP-SURFACE-BG-PATTERN"),
            (ColSurfaceBgColor,     "BLE_APP-SURFACE-BG-COLOR"),
            (ColCutBgPattern,       "BLE_APP-CUT-BG-PATTERN"),
            (ColCutBgColor,         "BLE_APP-CUT-BG-COLOR"),
            (ColCutPatternName,     "BLE_APP-CUT_PATTERN"),
            (ColSurfacePatternName, "BLE_APP-SURFACE_PATTERN"),
            (ColBgPatternName,      "BLE_APP-BACKGROUND_PATTERN"),
            (ColShadingRgb,         "BLE_APP-SHADING_RGB"),
            // --- BLE_MAT_* element-level scheduling mirrors (same CSV source as MAT_*) ---
            (ColName,               "BLE_MAT_NAME_TXT"),
            (ColDiscipline,         "BLE_MAT_DISCIPLINE_TXT"),
            (ColIso19650Id,         "BLE_MAT_ID_TXT"),
            (ColCategory,           "BLE_MAT_CATEGORY_TXT"),
            (ColElementType,        "BLE_MAT_ELEM_TYPE_TXT"),
            (ColApplication,        "BLE_MAT_APPLICATION_TXT"),
            (ColManufacturer,       "ASS_MANUFACTURER_TXT"),
            (ColThicknessMm,        "BLE_MAT_THICK_MM"),
            (ColDurability,         "BLE_MAT_DURABILITY_TXT"),
            (ColSpecifications,     "BLE_MAT_SPEC_TXT"),
            (ColStandard,           "BLE_MAT_STANDARD_TXT"),
            (ColLayerCount,         "BLE_MAT_LAYER_CNT_INT"),
            (ColLayer1Material,     "BLE_MAT_L1_MAT_TXT"),
            (ColLayer1Thickness,    "BLE_MAT_L1_THK_MM"),
            (ColLayer1Function,     "BLE_MAT_L1_FUNC_TXT"),
            (ColLayer2Material,     "BLE_MAT_L2_MAT_TXT"),
            (ColLayer2Thickness,    "BLE_MAT_L2_THK_MM"),
            (ColLayer2Function,     "BLE_MAT_L2_FUNC_TXT"),
        };

        /// <summary>
        /// Create a material by duplicating from a native Revit base material.
        /// If base material is found, copies its appearance, structural, and thermal assets.
        /// If not found, creates a blank material and logs a warning.
        /// </summary>
        public static Material CreateFromBase(Document doc, string matName, string[] cols,
            Dictionary<string, Material> baseMaterialCache)
        {
            string baseMatName = GetCol(cols, ColBaseMaterial);
            Material baseMat = null;

            // Look up the native Revit base material
            if (!string.IsNullOrEmpty(baseMatName) &&
                baseMaterialCache.TryGetValue(baseMatName, out Material found))
            {
                baseMat = found;
            }

            // Create the new material
            ElementId newId = Material.Create(doc, matName);
            if (newId == ElementId.InvalidElementId) return null;

            Material newMat = doc.GetElement(newId) as Material;
            if (newMat == null) return null;

            // If base material found, copy its assets (appearance, structural, thermal)
            if (baseMat != null)
            {
                try
                {
                    // Copy appearance asset (rendering appearance)
                    if (baseMat.AppearanceAssetId != ElementId.InvalidElementId)
                    {
                        AppearanceAssetElement baseAsset =
                            doc.GetElement(baseMat.AppearanceAssetId) as AppearanceAssetElement;
                        if (baseAsset != null)
                        {
                            // Duplicate to get an independent copy
                            string assetName = matName + "_Appearance";
                            try
                            {
                                AppearanceAssetElement newAsset = baseAsset.Duplicate(assetName);
                                newMat.AppearanceAssetId = newAsset.Id;
                            }
                            catch (Exception exApp)
                            {
                                // Retry with a unique suffix (name collision is the most common cause)
                                try
                                {
                                    string retryName = matName + "_App_" + DateTime.UtcNow.Ticks.ToString().Substring(10);
                                    AppearanceAssetElement retryAsset = baseAsset.Duplicate(retryName);
                                    newMat.AppearanceAssetId = retryAsset.Id;
                                }
                                catch (Exception ex2)
                                {
                                    StingLog.Warn($"Appearance asset duplication for '{matName}' failed (sharing base asset): {exApp.Message} / retry: {ex2.Message}");
                                    newMat.AppearanceAssetId = baseMat.AppearanceAssetId;
                                }
                            }
                        }
                    }

                    // NOTE: Do NOT share structural/thermal assets from base — each material
                    // needs its own copy so CSV overrides don't corrupt the base material.
                    // Assets are created fresh in ApplyStructuralAssetProperties/ApplyThermalAssetProperties.

                    StingLog.Info($"Material '{matName}' duplicated from base '{baseMatName}'");
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Asset copy for '{matName}' from '{baseMatName}': {ex.Message}");
                }
            }
            else if (!string.IsNullOrEmpty(baseMatName))
            {
                StingLog.Warn($"Base material '{baseMatName}' not found for '{matName}' — created blank");
            }

            return newMat;
        }

        /// <summary>
        /// Apply all material properties from CSV columns:
        /// identity class, color, transparency, smoothness, shininess,
        /// surface/cut patterns with colors, description, comments, shading.
        /// </summary>
        public static void ApplyMaterialProperties(Material mat, string[] cols,
            Document doc = null,
            Dictionary<string, ElementId> fillPatternCache = null)
        {
            try
            {
                // Identity class (column 35): Generic, Concrete, Metal, Plastic
                string matClass = GetCol(cols, ColIdentityClass);
                if (!string.IsNullOrEmpty(matClass))
                    mat.MaterialClass = matClass;

                // Color (column 36): appearance/shading color
                Color color = ParseRgb(GetCol(cols, ColColor));
                if (color != null)
                    mat.Color = color;

                // Shading RGB (column 67): overrides Color if present
                Color shadingColor = ParseRgb(GetCol(cols, ColShadingRgb));
                if (shadingColor != null)
                    mat.Color = shadingColor;

                // Transparency (column 37): 0-100
                string transStr = GetCol(cols, ColTransparency);
                if (!string.IsNullOrEmpty(transStr) &&
                    double.TryParse(transStr, out double transparencyD))
                {
                    mat.Transparency = Math.Max(0, Math.Min(100, (int)transparencyD));
                }

                // Smoothness (column 38): 0-100
                string smoothStr = GetCol(cols, ColSmoothness);
                if (!string.IsNullOrEmpty(smoothStr) &&
                    double.TryParse(smoothStr, out double smoothnessD))
                {
                    mat.Smoothness = Math.Max(0, Math.Min(100, (int)smoothnessD));
                }

                // Shininess (column 39): 0-128
                string shinyStr = GetCol(cols, ColShininess);
                if (!string.IsNullOrEmpty(shinyStr) &&
                    double.TryParse(shinyStr, out double shininessD))
                {
                    mat.Shininess = Math.Max(0, Math.Min(128, (int)shininessD));
                }

                // --- Identity properties (populates Material Browser fields) ---
                ApplyIdentityProperties(mat, cols);

                // Surface and cut patterns (requires doc and pattern cache)
                if (doc != null && fillPatternCache != null)
                    ApplyPatterns(mat, cols, fillPatternCache);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Material props '{mat.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Populate material properties from CSV data using:
        /// 1. Revit BuiltInParameters (Identity tab: Description, Manufacturer, Cost, etc.)
        /// 2. ThermalAsset properties (thermal conductivity, specific heat, density)
        /// 3. StructuralAsset properties (density, compressive/tensile strength)
        /// Shared parameters are populated separately by PopulateSharedParameters().
        /// </summary>
        private static void ApplyIdentityProperties(Material mat, string[] cols)
        {
            // ---- 1. Revit BuiltInParameters (Identity tab in Material Browser) ----

            // Description: full technical description enriched with physical properties
            string desc = GetCol(cols, ColDescription);
            string enrichedDesc = BuildEnrichedDescription(cols, desc);
            SetParam(mat, BuiltInParameter.ALL_MODEL_DESCRIPTION, enrichedDesc);

            // Comments: installation notes
            SetParam(mat, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, GetCol(cols, ColComments));

            // Manufacturer
            SetParam(mat, BuiltInParameter.ALL_MODEL_MANUFACTURER, GetCol(cols, ColManufacturer));

            // Model: standards compliance
            SetParam(mat, BuiltInParameter.ALL_MODEL_MODEL, GetCol(cols, ColStandard));

            // Cost: unit cost USD
            string costStr = GetCol(cols, ColCostUsd);
            if (!string.IsNullOrEmpty(costStr) && double.TryParse(costStr,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double costVal))
                SetParamDouble(mat, BuiltInParameter.ALL_MODEL_COST, costVal);

            // Keynote: ISO 19650 material ID
            SetParam(mat, BuiltInParameter.KEYNOTE_PARAM, GetCol(cols, ColIso19650Id));

            // Mark: material code
            SetParam(mat, BuiltInParameter.ALL_MODEL_MARK, GetCol(cols, ColCode));

            // URL: texture/reference URL
            string textureUrl = GetCol(cols, ColTextureUrl);
            if (!string.IsNullOrEmpty(textureUrl))
                SetParam(mat, BuiltInParameter.ALL_MODEL_URL, textureUrl);

            // ---- 2. ThermalAsset properties (Thermal tab in Material Browser) ----
            ApplyThermalAssetProperties(mat, cols);

            // ---- 3. StructuralAsset properties (Physical tab in Material Browser) ----
            ApplyStructuralAssetProperties(mat, cols);
        }

        /// <summary>
        /// Build an enriched Description string that includes key physical properties.
        /// Embeds important data into the Description BuiltInParameter for quick
        /// visibility in the Material Browser alongside the shared parameter values.
        /// </summary>
        private static string BuildEnrichedDescription(string[] cols, string baseDesc)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(baseDesc)) parts.Add(baseDesc);

            string category = GetCol(cols, ColCategory);
            string application = GetCol(cols, ColApplication);
            string features = GetCol(cols, ColFeatures);
            string specs = GetCol(cols, ColSpecifications);
            string durability = GetCol(cols, ColDurability);
            string fireRating = GetCol(cols, ColFireRating);
            string density = GetCol(cols, ColDensity);
            string thermalCond = GetCol(cols, ColThermalCond);
            string acousticAbs = GetCol(cols, ColAcousticAbs);
            string soundRed = GetCol(cols, ColSoundRed);
            string carbon = GetCol(cols, ColCarbon);
            string compStr = GetCol(cols, ColCompStrength);
            string tensStr = GetCol(cols, ColTensStrength);

            if (!string.IsNullOrEmpty(category)) parts.Add($"Category: {category}");
            if (!string.IsNullOrEmpty(application)) parts.Add($"Application: {application}");
            if (!string.IsNullOrEmpty(features)) parts.Add($"Features: {features}");
            if (!string.IsNullOrEmpty(specs)) parts.Add($"Specifications: {specs}");
            if (!string.IsNullOrEmpty(durability)) parts.Add($"Durability: {durability}");
            if (!string.IsNullOrEmpty(fireRating)) parts.Add($"Fire Rating: {fireRating}");
            if (!string.IsNullOrEmpty(density)) parts.Add($"Density: {density} kg/m³");
            if (!string.IsNullOrEmpty(thermalCond)) parts.Add($"Thermal Conductivity: {thermalCond} W/mK");
            if (!string.IsNullOrEmpty(acousticAbs)) parts.Add($"Acoustic Absorption: {acousticAbs}");
            if (!string.IsNullOrEmpty(soundRed)) parts.Add($"Sound Reduction: {soundRed} dB");
            if (!string.IsNullOrEmpty(carbon)) parts.Add($"Embodied Carbon: {carbon} kgCO₂/m³");
            if (!string.IsNullOrEmpty(compStr)) parts.Add($"Compressive Strength: {compStr} MPa");
            if (!string.IsNullOrEmpty(tensStr)) parts.Add($"Tensile Strength: {tensStr} MPa");

            return parts.Count > 0 ? string.Join(" | ", parts) : "";
        }

        /// <summary>
        /// Apply thermal properties to the material's ThermalAsset.
        /// Creates a new ThermalAsset if one doesn't exist.
        /// Sets: ThermalConductivity, SpecificHeat, Density.
        /// These appear in the Material Browser under the Thermal tab.
        /// </summary>
        private static void ApplyThermalAssetProperties(Material mat, string[] cols)
        {
            string thermalCond = GetCol(cols, ColThermalCond);
            string specificHeat = GetCol(cols, ColSpecificHeat);
            string density = GetCol(cols, ColDensity);

            // Only proceed if we have at least one thermal property to set
            if (string.IsNullOrEmpty(thermalCond) && string.IsNullOrEmpty(specificHeat)
                && string.IsNullOrEmpty(density))
                return;

            try
            {
                Document doc = mat.Document;

                // Parse values up front
                double tcVal = 0, shVal = 0, dVal = 0;
                bool hasTc = !string.IsNullOrEmpty(thermalCond) && double.TryParse(thermalCond,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out tcVal);
                bool hasSh = !string.IsNullOrEmpty(specificHeat) && double.TryParse(specificHeat,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out shVal);
                bool hasD = !string.IsNullOrEmpty(density) && double.TryParse(density,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out dVal);

                PropertySetElement thermalPse = null;

                if (mat.ThermalAssetId != ElementId.InvalidElementId)
                {
                    thermalPse = doc.GetElement(mat.ThermalAssetId) as PropertySetElement;
                }

                if (thermalPse == null)
                {
                    // Create a new thermal asset with properties pre-set
                    var thermalAsset = new ThermalAsset(mat.Name + "_Thermal", ThermalMaterialType.Solid);
                    if (hasTc) thermalAsset.ThermalConductivity = tcVal;
                    if (hasSh) thermalAsset.SpecificHeat = shVal;
                    if (hasD) thermalAsset.Density = dVal;
                    thermalPse = PropertySetElement.Create(doc, thermalAsset);
                    mat.ThermalAssetId = thermalPse.Id;
                }
                else
                {
                    // Update existing thermal asset via BuiltInParameters where available
                    if (hasTc)
                        SetAssetParam(thermalPse, BuiltInParameter.PHY_MATERIAL_PARAM_THERMAL_CONDUCTIVITY, tcVal);
                    if (hasD)
                        SetAssetParam(thermalPse, BuiltInParameter.PHY_MATERIAL_PARAM_STRUCTURAL_DENSITY, dVal);
                    // Specific heat: set via schema parameter lookup since no BuiltInParameter exists
                    if (hasSh)
                    {
                        try
                        {
                            foreach (Parameter p in thermalPse.GetOrderedParameters())
                            {
                                if (p.Definition?.Name == "Specific Heat" && !p.IsReadOnly
                                    && p.StorageType == StorageType.Double)
                                {
                                    p.Set(shVal);
                                    break;
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"SpecificHeat param: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ThermalAsset for '{mat.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Apply structural/physical properties to the material's StructuralAsset.
        /// Creates a new StructuralAsset if one doesn't exist.
        /// Sets: Density, Compressive Strength, Tensile Strength, Young's Modulus.
        /// These appear in the Material Browser under the Physical tab.
        /// </summary>
        private static void ApplyStructuralAssetProperties(Material mat, string[] cols)
        {
            string density = GetCol(cols, ColDensity);
            string compStr = GetCol(cols, ColCompStrength);
            string tensStr = GetCol(cols, ColTensStrength);

            if (string.IsNullOrEmpty(density) && string.IsNullOrEmpty(compStr)
                && string.IsNullOrEmpty(tensStr))
                return;

            try
            {
                Document doc = mat.Document;
                PropertySetElement structPse = null;

                if (mat.StructuralAssetId != ElementId.InvalidElementId)
                {
                    structPse = doc.GetElement(mat.StructuralAssetId) as PropertySetElement;
                }

                // Parse values up front
                double dVal = 0, compVal = 0, tensVal = 0;
                bool hasD = !string.IsNullOrEmpty(density) && double.TryParse(density,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out dVal);
                bool hasComp = !string.IsNullOrEmpty(compStr) && double.TryParse(compStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out compVal);
                bool hasTens = !string.IsNullOrEmpty(tensStr) && double.TryParse(tensStr,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out tensVal);

                // Determine structural asset class from material category
                StructuralAssetClass assetClass = InferStructuralAssetClass(
                    GetCol(cols, ColIdentityClass), GetCol(cols, ColCategory));

                if (structPse == null)
                {
                    // Create new structural asset with properties pre-set
                    var structAsset = new StructuralAsset(mat.Name + "_Structural", assetClass);
                    if (hasD) structAsset.Density = dVal;
                    if (hasComp)
                    {
                        // MPa → Pa for Revit internal units (Pa)
                        structAsset.MinimumYieldStress = compVal * 1e6;
                    }
                    if (hasTens) structAsset.MinimumTensileStrength = tensVal * 1e6;
                    structPse = PropertySetElement.Create(doc, structAsset);
                    mat.StructuralAssetId = structPse.Id;
                    // Set Young's modulus via PropertySetElement (not available on StructuralAsset directly)
                    if (hasComp)
                    {
                        double youngsPa = EstimateYoungsModulusPa(assetClass, compVal);
                        SetAssetParamByName(structPse, "Young's Modulus X", youngsPa);
                    }
                }
                else
                {
                    // Update existing structural asset via parameters
                    if (hasD)
                        SetAssetParam(structPse, BuiltInParameter.PHY_MATERIAL_PARAM_STRUCTURAL_DENSITY, dVal);
                    if (hasComp)
                    {
                        SetAssetParamByName(structPse, "Minimum Yield Stress", compVal * 1e6);
                        SetAssetParamByName(structPse, "Young's Modulus X", EstimateYoungsModulusPa(assetClass, compVal));
                    }
                    if (hasTens)
                        SetAssetParamByName(structPse, "Minimum Tensile Strength", tensVal * 1e6);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"StructuralAsset for '{mat.Name}': {ex.Message}");
            }
        }

        /// <summary>Infer StructuralAssetClass from CSV material class/category.</summary>
        private static StructuralAssetClass InferStructuralAssetClass(string matClass, string category)
        {
            string combined = ((matClass ?? "") + " " + (category ?? "")).ToLowerInvariant();
            if (combined.Contains("concrete")) return StructuralAssetClass.Concrete;
            if (combined.Contains("steel") || combined.Contains("metal") ||
                combined.Contains("iron") || combined.Contains("alumin"))
                return StructuralAssetClass.Metal;
            if (combined.Contains("wood") || combined.Contains("timber"))
                return StructuralAssetClass.Wood;
            if (combined.Contains("masonry") || combined.Contains("brick") ||
                combined.Contains("block") || combined.Contains("stone"))
                return StructuralAssetClass.Generic;
            if (combined.Contains("glass")) return StructuralAssetClass.Generic;
            if (combined.Contains("plastic") || combined.Contains("polymer"))
                return StructuralAssetClass.Plastic;
            return StructuralAssetClass.Generic;
        }

        /// <summary>
        /// Estimate Young's modulus (Pa) from compressive strength (MPa) using
        /// material-class-specific formulas instead of a blanket E=1000×fck.
        /// Concrete: Ecm = 22000 × (fck/10)^0.3 per EC2 Table 3.1.
        /// Steel/Metal: 210 GPa (BS EN 1993). Timber: 11 GPa (BS EN 1995 C24 mean).
        /// Plastic: 2.5 GPa typical. Generic fallback: E = 1000 × fck.
        /// </summary>
        private static double EstimateYoungsModulusPa(StructuralAssetClass assetClass, double compStrengthMPa)
        {
            double eMPa;
            switch (assetClass)
            {
                case StructuralAssetClass.Concrete:
                    // EC2 Table 3.1: Ecm = 22000 × (fcm/10)^0.3 where fcm = fck + 8
                    double fcm = compStrengthMPa + 8.0;
                    eMPa = 22000.0 * Math.Pow(fcm / 10.0, 0.3);
                    break;
                case StructuralAssetClass.Metal:
                    eMPa = 210000.0; // 210 GPa for structural steel per BS EN 1993-1-1
                    break;
                case StructuralAssetClass.Wood:
                    eMPa = 11000.0; // C24 mean modulus per BS EN 338
                    break;
                case StructuralAssetClass.Plastic:
                    eMPa = 2500.0; // typical engineering plastic
                    break;
                default:
                    eMPa = compStrengthMPa * 1000.0; // generic fallback
                    break;
            }
            return eMPa * 1e6; // MPa → Pa
        }

        /// <summary>Set a double parameter on a PropertySetElement by parameter name.</summary>
        private static void SetAssetParamByName(PropertySetElement pse, string paramName, double value)
        {
            try
            {
                foreach (Parameter p in pse.GetOrderedParameters())
                {
                    if (p.Definition?.Name == paramName && !p.IsReadOnly
                        && p.StorageType == StorageType.Double)
                    {
                        p.Set(value);
                        return;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"SetAssetParamByName '{paramName}': {ex.Message}"); }
        }

        /// <summary>Set a double parameter on a PropertySetElement (asset), with null/readonly guard.</summary>
        private static void SetAssetParam(PropertySetElement pse, BuiltInParameter bip, double value)
        {
            try
            {
#pragma warning disable CS0618 // BuiltInParameter overload still functional in Revit 2025+
                Parameter p = pse.get_Parameter(bip);
#pragma warning restore CS0618
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                    p.Set(value);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SetAssetParam {bip}: {ex.Message}");
            }
        }

        /// <summary>Set a string BuiltInParameter on the material if the value is non-empty.</summary>
        private static void SetParam(Material mat, BuiltInParameter bip, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
#pragma warning disable CS0618 // BuiltInParameter overload still functional in Revit 2025+
                Parameter p = mat.get_Parameter(bip);
#pragma warning restore CS0618
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"param not available on this material: {ex.Message}"); }
        }

        /// <summary>Set a double BuiltInParameter on the material.</summary>
        private static void SetParamDouble(Material mat, BuiltInParameter bip, double value)
        {
            try
            {
#pragma warning disable CS0618 // BuiltInParameter overload still functional in Revit 2025+
                Parameter p = mat.get_Parameter(bip);
#pragma warning restore CS0618
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                    p.Set(value);
            }
            catch (Exception ex) { StingLog.Warn($"param not available on this material: {ex.Message}"); }
        }

        // ── SHARED PARAMETER VALUE WRITER ─────────────────────────────────

        /// <summary>
        /// Write CSV column values to material shared parameters using the
        /// SharedParamMappings array.  Parameters must already be bound to
        /// OST_Materials (via LoadSharedParamsCommand / CleanMaterialBindings).
        /// </summary>
        private static void ApplySharedParamValues(Material mat, string[] cols)
        {
            if (mat == null) return;

            foreach (var (col, paramName) in SharedParamMappings)
            {
                string value = GetCol(cols, col);
                if (string.IsNullOrEmpty(value)) continue;

                try
                {
                    Parameter p = mat.LookupParameter(paramName);
                    if (p == null || p.IsReadOnly) continue;

                    switch (p.StorageType)
                    {
                        case StorageType.String:
                            p.Set(value);
                            break;

                        case StorageType.Double:
                            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                                p.Set(dVal);
                            break;

                        case StorageType.Integer:
                            if (int.TryParse(value, out int iVal))
                                p.Set(iVal);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SharedParam '{paramName}' on '{mat.Name}': {ex.Message}");
                }
            }
        }

        // ── READ helpers (for export CSV round-trip) ──────────────────────

        /// <summary>Read a string BuiltInParameter from a material. Returns empty string on failure.</summary>
        internal static string ReadMatParam(Material mat, BuiltInParameter bip)
        {
            try
            {
#pragma warning disable CS0618
                Parameter p = mat.get_Parameter(bip);
#pragma warning restore CS0618
                if (p != null && p.StorageType == StorageType.String)
                    return p.AsString() ?? "";
                if (p != null && p.StorageType == StorageType.Double)
                    return p.AsDouble().ToString("F4");
            }
            catch (Exception ex) { StingLog.Warn($"ReadMatParam {bip}: {ex.Message}"); }
            return "";
        }

        /// <summary>Read a double BuiltInParameter from a material, formatted as string.</summary>
        internal static string ReadMatParamDouble(Material mat, BuiltInParameter bip)
        {
            try
            {
#pragma warning disable CS0618
                Parameter p = mat.get_Parameter(bip);
#pragma warning restore CS0618
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double v = p.AsDouble();
                    return v == 0 ? "" : v.ToString("F4");
                }
                if (p != null && p.StorageType == StorageType.String)
                    return p.AsString() ?? "";
            }
            catch (Exception ex) { StingLog.Warn($"ReadMatParamDouble {bip}: {ex.Message}"); }
            return "";
        }

        /// <summary>Read a double from a PropertySetElement (thermal/structural asset) by BuiltInParameter.</summary>
        internal static string ReadAssetParam(PropertySetElement pse, BuiltInParameter bip)
        {
            if (pse == null) return "";
            try
            {
#pragma warning disable CS0618
                Parameter p = pse.get_Parameter(bip);
#pragma warning restore CS0618
                if (p != null && p.StorageType == StorageType.Double)
                {
                    double v = p.AsDouble();
                    return v == 0 ? "" : v.ToString("F6");
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadAssetParam {bip}: {ex.Message}"); }
            return "";
        }

        /// <summary>Read a double from a PropertySetElement (thermal/structural asset) by parameter name.</summary>
        internal static string ReadAssetParamByName(PropertySetElement pse, string paramName)
        {
            if (pse == null) return "";
            try
            {
                foreach (Parameter p in pse.GetOrderedParameters())
                {
                    if (p.Definition?.Name == paramName && p.StorageType == StorageType.Double)
                    {
                        double v = p.AsDouble();
                        return v == 0 ? "" : v.ToString("F6");
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ReadAssetParamByName '{paramName}': {ex.Message}"); }
            return "";
        }

        /// <summary>
        /// Apply surface and cut fill patterns with their colors.
        /// Uses columns 64-66 for pattern names, 40/42/48/50 for colors.
        /// Falls back to columns 41/43 if 64/65 are empty.
        /// </summary>
        private static void ApplyPatterns(Material mat, string[] cols,
            Dictionary<string, ElementId> patternCache)
        {
            try
            {
                // Surface foreground: pattern from col[65] or col[41], color from col[40]
                string surfPattern = GetCol(cols, ColSurfacePatternName);
                if (string.IsNullOrEmpty(surfPattern))
                    surfPattern = GetCol(cols, ColSurfaceFgPattern);
                if (!string.IsNullOrEmpty(surfPattern) &&
                    patternCache.TryGetValue(surfPattern, out ElementId surfId))
                {
                    mat.SurfaceForegroundPatternId = surfId;
                }
                Color surfFgColor = ParseRgb(GetCol(cols, ColSurfaceFgColor));
                if (surfFgColor != null)
                    mat.SurfaceForegroundPatternColor = surfFgColor;

                // Cut foreground: pattern from col[64] or col[43], color from col[42]
                string cutPattern = GetCol(cols, ColCutPatternName);
                if (string.IsNullOrEmpty(cutPattern))
                    cutPattern = GetCol(cols, ColCutFgPattern);
                if (!string.IsNullOrEmpty(cutPattern) &&
                    patternCache.TryGetValue(cutPattern, out ElementId cutId))
                {
                    mat.CutForegroundPatternId = cutId;
                }
                Color cutFgColor = ParseRgb(GetCol(cols, ColCutFgColor));
                if (cutFgColor != null)
                    mat.CutForegroundPatternColor = cutFgColor;

                // Background pattern (same for surface and cut): col[66]
                string bgPattern = GetCol(cols, ColBgPatternName);
                if (string.IsNullOrEmpty(bgPattern))
                    bgPattern = GetCol(cols, ColSurfaceBgPattern);
                if (!string.IsNullOrEmpty(bgPattern) &&
                    patternCache.TryGetValue(bgPattern, out ElementId bgId))
                {
                    mat.SurfaceBackgroundPatternId = bgId;
                    mat.CutBackgroundPatternId = bgId;
                }

                // Background colors
                Color surfBgColor = ParseRgb(GetCol(cols, ColSurfaceBgColor));
                if (surfBgColor != null)
                    mat.SurfaceBackgroundPatternColor = surfBgColor;

                Color cutBgColor = ParseRgb(GetCol(cols, ColCutBgColor));
                if (cutBgColor != null)
                    mat.CutBackgroundPatternColor = cutBgColor;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Pattern apply '{mat.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Build a cache of native Revit materials by name (case-insensitive)
        /// for base material lookup.
        /// </summary>
        public static Dictionary<string, Material> BuildBaseMaterialCache(Document doc)
        {
            var cache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (Material mat in new FilteredElementCollector(doc)
                .OfClass(typeof(Material)).Cast<Material>())
            {
                if (!cache.ContainsKey(mat.Name))
                    cache[mat.Name] = mat;
            }
            return cache;
        }

        /// <summary>
        /// Build a cache of fill pattern elements by name (case-insensitive).
        /// </summary>
        public static Dictionary<string, ElementId> BuildFillPatternCache(Document doc)
        {
            var cache = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            foreach (FillPatternElement fp in new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>())
            {
                if (!cache.ContainsKey(fp.Name))
                    cache[fp.Name] = fp.Id;
            }
            return cache;
        }

        /// <summary>
        /// Shared material creation logic for BLE and MEP commands.
        /// Reads CSV, finds/duplicates base materials, applies all properties.
        /// Each batch is a standalone Transaction so Revit regenerates between batches.
        /// </summary>
        private const int MaterialBatchSize = 50;

        public static Result CreateMaterialsFromCsv(Document doc, string csvFileName,
            string dialogTitle)
        {
            // (shared param warnings tracking removed — using ThermalAsset/StructuralAsset approach)
            string csvPath = StingToolsApp.FindDataFile(csvFileName);
            if (csvPath == null)
            {
                string dllDir = System.IO.Path.GetDirectoryName(StingToolsApp.AssemblyPath) ?? "(unknown)";
                TaskDialog.Show(dialogTitle,
                    $"{csvFileName} not found in the data directory.\n\n" +
                    $"Primary search: {StingToolsApp.DataPath}\n" +
                    $"DLL location:   {dllDir}\n\n" +
                    "Ensure the data/ folder (with CSV files) is deployed\n" +
                    "alongside StingTools.dll.");
                return Result.Failed;
            }

            var lines = File.ReadAllLines(csvPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Skip(1) // skip header
                .ToList();

            int created = 0;
            int skipped = 0;
            int duplicated = 0;
            int batchErrors = 0;

            // Build caches OUTSIDE any transaction (read-only collectors)
            var baseMaterialCache = BuildBaseMaterialCache(doc);
            var existingNames = new HashSet<string>(
                baseMaterialCache.Keys, StringComparer.OrdinalIgnoreCase);
            var fillPatternCache = BuildFillPatternCache(doc);

            // CRASH FIX: Cache duplicated AppearanceAssetElements by base material name.
            // Without this, 100 materials sharing the same base each duplicate the asset
            // separately, creating 100 document elements instead of 1.
            var assetCache = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);

            // Parse all rows upfront
            var rows = new List<(string matName, string[] cols)>();
            foreach (string line in lines)
            {
                string[] cols = StingToolsApp.ParseCsvLine(line);
                if (cols.Length < 7) continue;
                string matName = cols[ColName].Trim();
                if (string.IsNullOrEmpty(matName)) continue;
                if (existingNames.Contains(matName)) { skipped++; continue; }
                rows.Add((matName, cols));
            }

            StingLog.Info($"{dialogTitle}: {rows.Count} materials to create in batches of {MaterialBatchSize} (skipping {skipped} existing)");
            bool cancelled = false;

            for (int batchStart = 0; batchStart < rows.Count; batchStart += MaterialBatchSize)
            {
                // Cancellation check between batches
                if (EscapeChecker.IsEscapePressed())
                {
                    cancelled = true;
                    StingLog.Info($"{dialogTitle}: cancelled by user at batch {(batchStart / MaterialBatchSize) + 1}");
                    break;
                }

                int batchEnd = Math.Min(batchStart + MaterialBatchSize, rows.Count);
                int batchNum = (batchStart / MaterialBatchSize) + 1;

                using (Transaction tx = new Transaction(doc,
                    $"STING Materials batch {batchNum}"))
                {
                    tx.Start();

                    try
                    {
                        for (int i = batchStart; i < batchEnd; i++)
                        {
                            var (matName, cols) = rows[i];

                            // Double-check: may have been created by an earlier batch
                            if (existingNames.Contains(matName)) { skipped++; continue; }

                            try
                            {
                                Material newMat = CreateFromBaseWithCache(
                                    doc, matName, cols, baseMaterialCache, assetCache);
                                if (newMat != null)
                                {
                                    ApplyMaterialProperties(newMat, cols, doc, fillPatternCache);
                                    ApplySharedParamValues(newMat, cols);

                                    string baseMatName = GetCol(cols, ColBaseMaterial);
                                    if (!string.IsNullOrEmpty(baseMatName) &&
                                        baseMaterialCache.ContainsKey(baseMatName))
                                        duplicated++;

                                    created++;
                                    existingNames.Add(matName);
                                    baseMaterialCache[matName] = newMat;
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"Material create failed '{matName}': {ex.Message}");
                            }
                        }

                        tx.Commit();
                        StingLog.Info($"{dialogTitle}: batch {batchNum} committed ({created} created so far)");
                    }
                    catch (Exception ex)
                    {
                        batchErrors++;
                        StingLog.Error($"{dialogTitle}: batch {batchNum} failed, rolling back batch", ex);
                        if (tx.HasStarted() && !tx.HasEnded())
                            tx.RollBack();
                    }
                }
            }

            string report = $"Created {created} materials.\n" +
                $"  Duplicated from base: {duplicated}\n" +
                $"  Created blank: {created - duplicated}\n" +
                $"Skipped {skipped} (already exist).\n" +
                (cancelled ? "CANCELLED by user (Escape key). Materials created so far are kept.\n" : "") +
                (batchErrors > 0 ? $"Batch errors: {batchErrors}\n" : "") +
                $"Source: {Path.GetFileName(csvPath)} ({lines.Count} rows)";
            TaskDialog.Show(dialogTitle, report);

            StingLog.Info($"{dialogTitle}: {report.Replace("\n", " | ")}");
            return Result.Succeeded;
        }

        /// <summary>
        /// CRASH FIX: Create material with cached AppearanceAssetElement duplication.
        /// If multiple materials share the same base, the appearance asset is duplicated
        /// only once and reused — avoids creating hundreds of redundant document elements.
        /// </summary>
        public static Material CreateFromBaseWithCache(Document doc, string matName, string[] cols,
            Dictionary<string, Material> baseMaterialCache,
            Dictionary<string, ElementId> assetCache)
        {
            string baseMatName = GetCol(cols, ColBaseMaterial);
            Material baseMat = null;

            if (!string.IsNullOrEmpty(baseMatName) &&
                baseMaterialCache.TryGetValue(baseMatName, out Material found))
            {
                baseMat = found;
            }

            ElementId newId = Material.Create(doc, matName);
            if (newId == ElementId.InvalidElementId) return null;

            Material newMat = doc.GetElement(newId) as Material;
            if (newMat == null) return null;

            if (baseMat != null)
            {
                try
                {
                    // CRASH FIX: Reuse cached appearance asset instead of duplicating per-material
                    if (baseMat.AppearanceAssetId != ElementId.InvalidElementId)
                    {
                        if (assetCache.TryGetValue(baseMatName, out ElementId cachedAssetId))
                        {
                            // Reuse previously duplicated asset
                            newMat.AppearanceAssetId = cachedAssetId;
                        }
                        else
                        {
                            // First time seeing this base — duplicate and cache
                            AppearanceAssetElement baseAsset =
                                doc.GetElement(baseMat.AppearanceAssetId) as AppearanceAssetElement;
                            if (baseAsset != null)
                            {
                                try
                                {
                                    string assetName = "STING_" + baseMatName + "_Asset";
                                    AppearanceAssetElement newAsset = baseAsset.Duplicate(assetName);
                                    newMat.AppearanceAssetId = newAsset.Id;
                                    assetCache[baseMatName] = newAsset.Id;
                                }
                                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); // Name collision — share the base asset directly
                                    newMat.AppearanceAssetId = baseMat.AppearanceAssetId;
                                    assetCache[baseMatName] = baseMat.AppearanceAssetId; }
                            }
                        }
                    }

                    // NOTE: Do NOT share structural/thermal assets from base — each material
                    // needs its own copy so CSV overrides don't corrupt the base material.
                    // Assets are created fresh in ApplyStructuralAssetProperties/ApplyThermalAssetProperties
                    // when CSV data provides density/strength/thermal values.
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Asset copy for '{matName}' from '{baseMatName}': {ex.Message}");
                }
            }
            else if (!string.IsNullOrEmpty(baseMatName))
            {
                StingLog.Warn($"Base material '{baseMatName}' not found for '{matName}' — created blank");
            }

            return newMat;
        }

        /// <summary>Parse "RGB 221-221-219" or "245,245,220" into a Color.</summary>
        public static Color ParseRgb(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var match = Regex.Match(value.Trim(),
                @"(?:RGB\s+)?(\d{1,3})\D+(\d{1,3})\D+(\d{1,3})");
            if (match.Success &&
                byte.TryParse(match.Groups[1].Value, out byte r) &&
                byte.TryParse(match.Groups[2].Value, out byte g) &&
                byte.TryParse(match.Groups[3].Value, out byte b))
            {
                return new Color(r, g, b);
            }
            return null;
        }

        /// <summary>Safe column access — returns empty string if index out of range.</summary>
        public static string GetCol(string[] cols, int index)
        {
            return (index >= 0 && index < cols.Length) ? cols[index].Trim() : "";
        }

        /// <summary>
        /// Populate shared parameters on a Material element from CSV column data.
        /// Uses the SharedParamMappings array to write all 43 mapped CSV columns
        /// to their corresponding shared parameters via LookupParameter.
        /// Requires shared parameters to be Instance-bound to OST_Materials.
        /// </summary>
        public static void PopulateSharedParameters(Material mat, string[] cols)
        {
            int written = 0;
            foreach (var (col, paramName) in SharedParamMappings)
            {
                string value = GetCol(cols, col);
                if (string.IsNullOrEmpty(value)) continue;

                try
                {
                    Parameter p = mat.LookupParameter(paramName);
                    if (p == null || p.IsReadOnly) continue;

                    if (p.StorageType == StorageType.String)
                    {
                        p.Set(value);
                        written++;
                    }
                    else if (p.StorageType == StorageType.Double)
                    {
                        if (double.TryParse(value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                        {
                            p.Set(dVal);
                            written++;
                        }
                    }
                    else if (p.StorageType == StorageType.Integer)
                    {
                        if (int.TryParse(value, out int iVal))
                        {
                            p.Set(iVal);
                            written++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Material '{mat.Name}' param '{paramName}': {ex.Message}");
                }
            }

            if (written > 0)
                StingLog.Info($"Material '{mat.Name}': populated {written} shared parameters");
        }
    }

    /// <summary>
    /// Create BLE (Building Lifecycle Element) materials from BLE_MATERIALS.csv.
    /// Duplicates from native Revit base materials where specified, then applies
    /// CSV appearance properties (color, patterns, class, transparency).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateBLEMaterialsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            // SAFETY-001: Reset static warning counter so a previous failed run
            // does not suppress warnings for this execution.
            MaterialPropertyHelper.ResetSharedParamWarnings();
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;
                return MaterialPropertyHelper.CreateMaterialsFromCsv(
                    doc, "BLE_MATERIALS.csv", "STING Tools - Create BLE Materials");
            }
            catch (Exception ex)
            {
                StingLog.Error("CreateBLEMaterialsCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"BLE Materials failed:\n{ex.Message}"); } catch (Exception ex2) { StingLog.Warn($"TaskDialog fallback: {ex2.Message}"); }
                return Result.Failed;
            }
        }
    }

    /// <summary>
    /// Create MEP materials from MEP_MATERIALS.csv.
    /// Duplicates from native Revit base materials where specified, then applies
    /// CSV appearance properties (color, patterns, class, transparency).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateMEPMaterialsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            // SAFETY-001: Reset static warning counter so a previous failed run
            // does not suppress warnings for this execution.
            MaterialPropertyHelper.ResetSharedParamWarnings();
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;
                return MaterialPropertyHelper.CreateMaterialsFromCsv(
                    doc, "MEP_MATERIALS.csv", "STING Tools - Create MEP Materials");
            }
            catch (Exception ex)
            {
                StingLog.Error("CreateMEPMaterialsCommand crashed", ex);
                try { TaskDialog.Show("STING Tools", $"MEP Materials failed:\n{ex.Message}"); } catch (Exception ex2) { StingLog.Warn($"TaskDialog fallback: {ex2.Message}"); }
                return Result.Failed;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  StingMaterialManagerCommand — Unified material management UI
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unified material management: browse project materials in a proper WPF
    /// dialog (with search + filter + colour swatches), create BLE/MEP
    /// materials from CSV, and export material list to CSV. Replaces the
    /// legacy 4-option TaskDialog with <see cref="UI.MaterialManagerDialog"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class StingMaterialManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }

                var res = StingTools.UI.MaterialManagerDialog.Show(ctx.UIDoc);
                if (res != null && res.Confirmed)
                {
                    // Browse / Apply / Export are handled inline by the dialog.
                    // Create paths return through here so they can run under the
                    // transaction model their own command provides.
                    if (res.Operation == "CreateBLEMaterials")
                    {
                        string msg = "";
                        new CreateBLEMaterialsCommand().Execute(commandData, ref msg, elements);
                    }
                    else if (res.Operation == "CreateMEPMaterials")
                    {
                        string msg = "";
                        new CreateMEPMaterialsCommand().Execute(commandData, ref msg, elements);
                    }
                }
                else if (result == TaskDialogResult.CommandLink2)
                {
                    // Delegate to CreateBLEMaterialsCommand
                    string msg = "";
                    var cmd = new CreateBLEMaterialsCommand();
                    cmd.Execute(commandData, ref msg, elements);
                }
                else if (result == TaskDialogResult.CommandLink3)
                {
                    // Delegate to CreateMEPMaterialsCommand
                    string msg = "";
                    var cmd = new CreateMEPMaterialsCommand();
                    cmd.Execute(commandData, ref msg, elements);
                }
                else if (result == TaskDialogResult.CommandLink4)
                {
                    // Export material list to CSV (reuses materials collected above)
                    string outputDir = OutputLocationHelper.GetOutputDirectory(doc);
                    string filePath = Path.Combine(outputDir, "STING_MATERIALS_EXPORT.csv");

                    var sb = new StringBuilder();
                    sb.AppendLine("Name,Class,Color,Transparency,Smoothness,Shininess,Description,Manufacturer,Model,Cost,Keynote,Mark,URL,Density_kg_m3,ThermalCond_W_mK,SpecificHeat_J_kgK,CompStrength_MPa,TensStrength_MPa");
                    foreach (var mat in materials)
                    {
                        string name = (mat.Name ?? "").Replace(",", ";");
                        string matClass = (mat.MaterialClass ?? "").Replace(",", ";");
                        string color = "";
                        int transparency = 0, smoothness = 0, shininess = 0;
                        try { var c = mat.Color; if (c != null && c.IsValid) color = $"RGB({c.Red},{c.Green},{c.Blue})"; }
                        catch (Exception ex) { StingLog.Warn($"Read material color for '{name}': {ex.Message}"); }
                        try { transparency = mat.Transparency; } catch (Exception ex) { StingLog.Warn($"Read material transparency: {ex.Message}"); }
                        try { smoothness = mat.Smoothness; } catch (Exception ex) { StingLog.Warn($"Read material smoothness: {ex.Message}"); }
                        try { shininess = mat.Shininess; } catch (Exception ex) { StingLog.Warn($"Read material shininess: {ex.Message}"); }

                        // Read BuiltInParameters (Identity tab)
                        string desc = MaterialPropertyHelper.ReadMatParam(mat, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                        string mfr = MaterialPropertyHelper.ReadMatParam(mat, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                        string model = MaterialPropertyHelper.ReadMatParam(mat, BuiltInParameter.ALL_MODEL_MODEL);
                        string cost = MaterialPropertyHelper.ReadMatParamDouble(mat, BuiltInParameter.ALL_MODEL_COST);
                        string keynote = MaterialPropertyHelper.ReadMatParam(mat, BuiltInParameter.KEYNOTE_PARAM);
                        string mark = MaterialPropertyHelper.ReadMatParam(mat, BuiltInParameter.ALL_MODEL_MARK);
                        string url = MaterialPropertyHelper.ReadMatParam(mat, BuiltInParameter.ALL_MODEL_URL);

                        // Read ThermalAsset properties
                        string density = "", thermalCond = "", specificHeat = "";
                        try
                        {
                            if (mat.ThermalAssetId != ElementId.InvalidElementId)
                            {
                                var tPse = doc.GetElement(mat.ThermalAssetId) as PropertySetElement;
                                if (tPse != null)
                                {
                                    density = MaterialPropertyHelper.ReadAssetParam(tPse, BuiltInParameter.PHY_MATERIAL_PARAM_STRUCTURAL_DENSITY);
                                    thermalCond = MaterialPropertyHelper.ReadAssetParam(tPse, BuiltInParameter.PHY_MATERIAL_PARAM_THERMAL_CONDUCTIVITY);
                                    specificHeat = MaterialPropertyHelper.ReadAssetParamByName(tPse, "Specific Heat");
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Read thermal '{name}': {ex.Message}"); }

                        // Read StructuralAsset properties
                        string compStr = "", tensStr = "";
                        try
                        {
                            if (mat.StructuralAssetId != ElementId.InvalidElementId)
                            {
                                var sPse = doc.GetElement(mat.StructuralAssetId) as PropertySetElement;
                                if (sPse != null)
                                {
                                    if (string.IsNullOrEmpty(density))
                                        density = MaterialPropertyHelper.ReadAssetParam(sPse, BuiltInParameter.PHY_MATERIAL_PARAM_STRUCTURAL_DENSITY);
                                    compStr = MaterialPropertyHelper.ReadAssetParamByName(sPse, "Minimum Yield Stress");
                                    if (!string.IsNullOrEmpty(compStr) && double.TryParse(compStr,
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out double cVal))
                                        compStr = (cVal / 1e6).ToString("F1"); // Pa → MPa
                                    tensStr = MaterialPropertyHelper.ReadAssetParamByName(sPse, "Minimum Tensile Strength");
                                    if (!string.IsNullOrEmpty(tensStr) && double.TryParse(tensStr,
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out double tVal))
                                        tensStr = (tVal / 1e6).ToString("F1"); // Pa → MPa
                                }
                            }
                        }
                        catch (Exception ex) { StingLog.Warn($"Read structural '{name}': {ex.Message}"); }

                        sb.AppendLine($"\"{name}\",\"{matClass}\",\"{color}\",{transparency},{smoothness},{shininess},\"{desc}\",\"{mfr}\",\"{model}\",{cost},\"{keynote}\",\"{mark}\",\"{url}\",{density},{thermalCond},{specificHeat},{compStr},{tensStr}");
                    }

                    File.WriteAllText(filePath, sb.ToString());
                    TaskDialog.Show("Export Materials",
                        $"Exported {materials.Count} materials to:\n{filePath}");
                    StingLog.Info($"MaterialManager: exported {materials.Count} materials to {filePath}");
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("StingMaterialManagerCommand failed", ex);
                try { TaskDialog.Show("STING", $"Material Manager failed:\n{ex.Message}"); }
                catch (Exception ex2) { StingLog.Warn($"TaskDialog fallback: {ex2.Message}"); }
                return Result.Failed;
            }
        }
    }
}
