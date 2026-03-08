using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public const int ColImage = 44;              // BLE_APP-IMAGE
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
        /// Names MUST match MR_PARAMETERS.txt exactly (including hyphens).
        /// Uses LookupParameter to find shared params bound to the Material category.
        /// </summary>
        private static readonly (int col, string paramName)[] SharedParamMappings = new[]
        {
            // Classification (cols 0-8)
            (ColDiscipline,     "MAT_DISCIPLINE"),
            (ColIso19650Id,     "MAT_ISO_19650_ID"),
            (ColCode,           "MAT_CODE"),
            (ColElementType,    "MAT_ELEMENT_TYPE"),
            (ColCategory,       "MAT_CATEGORY"),
            (ColApplication,    "MAT_APPLICATION"),
            (ColLocation,       "MAT_LOCATION"),
            // Dimensions (cols 9-10)
            (ColThicknessMm,    "MAT_THICKNESS_MM"),
            (ColThicknessInch,  "MAT_THICKNESS_INCH"),
            // Cost (cols 11-12, 62-63) — names match MR_PARAMETERS.txt
            (ColCostUsd,        "MAT_COST_USD"),
            (ColCostUgx,        "MAT_COST_UGX"),
            (ColCostAssemblyUsd,"MAT_COST_ASSEMBLY_USD"),
            (ColCostAssemblyUgx,"MAT_COST_ASSEMBLY_UGX"),
            // Durability & Specs (cols 13-14, 31-33)
            (ColDurability,     "MAT_DURABILITY"),
            (ColSpecifications, "MAT_SPECIFICATIONS"),
            (ColManufacturer,   "MAT_MANUFACTURER"),
            (ColStandard,       "MAT_STANDARD"),
            (ColFeatures,       "MAT_FEATURES"),
            // Appearance (cols 34-50, 64-67) — written as shared params for data round-trip
            (ColBaseMaterial,   "BLE_APP-REVIT-BASE-MATERIAL"),
            (ColIdentityClass,  "BLE_APP-IDENTITY-CLASS"),
            (ColColor,          "BLE_APP-COLOR"),
            (ColTransparency,   "BLE_APP-TRANSPARENCY"),
            (ColSmoothness,     "BLE_APP-SMOOTHNESS"),
            (ColShininess,      "BLE_APP-SHININESS"),
            (ColSurfaceFgColor, "BLE_APP-SURFACE-FG-COLOR"),
            (ColSurfaceFgPattern,"BLE_APP-SURFACE-FG-PATTERN"),
            (ColCutFgColor,     "BLE_APP-CUT-FG-COLOR"),
            (ColCutFgPattern,   "BLE_APP-CUT-FG-PATTERN"),
            (ColImage,          "BLE_APP-IMAGE"),
            (ColDescription,    "BLE_APP-DESCRIPTION"),
            (ColComments,       "BLE_APP-COMMENTS"),
            (ColSurfaceBgPattern,"BLE_APP-SURFACE-BG-PATTERN"),
            (ColSurfaceBgColor, "BLE_APP-SURFACE-BG-COLOR"),
            (ColCutBgPattern,   "BLE_APP-CUT-BG-PATTERN"),
            (ColCutBgColor,     "BLE_APP-CUT-BG-COLOR"),
            (ColCutPatternName, "BLE_APP-CUT_PATTERN"),
            (ColSurfacePatternName,"BLE_APP-SURFACE_PATTERN"),
            (ColBgPatternName,  "BLE_APP-BACKGROUND_PATTERN"),
            (ColShadingRgb,     "BLE_APP-SHADING_RGB"),
            // Physical / Thermal / Performance (cols 51-61, 68-69)
            (ColPhysicalAsset,  "BLE_APP-PHYSICAL-ASSET"),
            (ColThermalAsset,   "BLE_APP-THERMAL-ASSET"),
            (ColTextureUrl,     "BLE_MAT_TEXTURE_URL"),
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
                            catch
                            {
                                // If duplicate name exists, share the base asset
                                newMat.AppearanceAssetId = baseMat.AppearanceAssetId;
                            }
                        }
                    }

                    // Share structural asset (physical properties)
                    if (baseMat.StructuralAssetId != ElementId.InvalidElementId)
                        newMat.StructuralAssetId = baseMat.StructuralAssetId;

                    // Share thermal asset
                    if (baseMat.ThermalAssetId != ElementId.InvalidElementId)
                        newMat.ThermalAssetId = baseMat.ThermalAssetId;

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
                    int.TryParse(transStr.Replace(".0", ""), out int transparency))
                {
                    mat.Transparency = Math.Max(0, Math.Min(100, transparency));
                }

                // Smoothness (column 38): 0-100
                string smoothStr = GetCol(cols, ColSmoothness);
                if (!string.IsNullOrEmpty(smoothStr) &&
                    int.TryParse(smoothStr.Replace(".0", ""), out int smoothness))
                {
                    mat.Smoothness = Math.Max(0, Math.Min(100, smoothness));
                }

                // Shininess (column 39): 0-128
                string shinyStr = GetCol(cols, ColShininess);
                if (!string.IsNullOrEmpty(shinyStr) &&
                    int.TryParse(shinyStr.Replace(".0", ""), out int shininess))
                {
                    mat.Shininess = Math.Max(0, Math.Min(128, shininess));
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
        /// Populate ALL material properties from CSV data into both:
        /// 1. Revit BuiltInParameters (Identity tab: Description, Manufacturer, Cost, etc.)
        /// 2. Shared parameters bound to the Material category (Material Parameters dialog)
        ///
        /// This ensures the Material Browser shows full data in Identity, Product Information,
        /// and Revit Annotation sections, PLUS all STING custom parameters in Material Parameters.
        /// </summary>
        private static void ApplyIdentityProperties(Material mat, string[] cols)
        {
            // ---- 1. Revit BuiltInParameters (Identity tab in Material Browser) ----

            // Description: full technical description
            SetParam(mat, BuiltInParameter.ALL_MODEL_DESCRIPTION, GetCol(cols, ColDescription));

            // Comments: installation notes
            SetParam(mat, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, GetCol(cols, ColComments));

            // Manufacturer
            SetParam(mat, BuiltInParameter.ALL_MODEL_MANUFACTURER, GetCol(cols, ColManufacturer));

            // Model: standards compliance
            SetParam(mat, BuiltInParameter.ALL_MODEL_MODEL, GetCol(cols, ColStandard));

            // Cost: unit cost USD
            string costStr = GetCol(cols, ColCostUsd);
            if (!string.IsNullOrEmpty(costStr) && double.TryParse(costStr, out double costVal))
                SetParamDouble(mat, BuiltInParameter.ALL_MODEL_COST, costVal);

            // Keynote: ISO 19650 material ID
            SetParam(mat, BuiltInParameter.KEYNOTE_PARAM, GetCol(cols, ColIso19650Id));

            // Mark: material code
            SetParam(mat, BuiltInParameter.ALL_MODEL_MARK, GetCol(cols, ColCode));

            // URL: texture/reference URL
            string textureUrl = GetCol(cols, ColTextureUrl);
            if (!string.IsNullOrEmpty(textureUrl))
                SetParam(mat, BuiltInParameter.ALL_MODEL_URL, textureUrl);

            // Keywords: combine category + application + features + specs for searchability
            var keywords = new List<string>();
            string category = GetCol(cols, ColCategory);
            string application = GetCol(cols, ColApplication);
            string features = GetCol(cols, ColFeatures);
            string specs = GetCol(cols, ColSpecifications);
            string durability = GetCol(cols, ColDurability);
            if (!string.IsNullOrEmpty(category)) keywords.Add(category);
            if (!string.IsNullOrEmpty(application)) keywords.Add(application);
            if (!string.IsNullOrEmpty(features)) keywords.Add(features);
            if (!string.IsNullOrEmpty(specs)) keywords.Add(specs);
            if (!string.IsNullOrEmpty(durability)) keywords.Add(durability);
            // Keywords is not a standard BuiltInParameter — try shared param
            if (keywords.Count > 0)
            {
                string keywordsStr = string.Join(", ", keywords);
                SetSharedParam(mat, "Keywords", keywordsStr);
            }

            // ---- 2. ALL CSV properties as shared parameters (Material Parameters dialog) ----
            // These populate the custom properties section visible in Material Parameters.
            // Parameters must be bound to the Material category first (via LoadSharedParams).
            foreach (var (col, paramName) in SharedParamMappings)
            {
                string value = GetCol(cols, col);
                if (string.IsNullOrEmpty(value)) continue;
                SetSharedParam(mat, paramName, value);
            }

            // Also write layer info for compound materials (useful for BOQ)
            string layerCount = GetCol(cols, ColLayerCount);
            if (!string.IsNullOrEmpty(layerCount))
                SetSharedParam(mat, "MAT_LAYER_COUNT", layerCount);

            // Write layer details (up to 5 layers × 3 properties each)
            for (int i = 1; i <= 5; i++)
            {
                int baseIdx = 16 + ((i - 1) * 3); // Layer 1 starts at col 16
                string layerMat = GetCol(cols, baseIdx);
                string layerThick = GetCol(cols, baseIdx + 1);
                string layerFunc = GetCol(cols, baseIdx + 2);

                if (!string.IsNullOrEmpty(layerMat))
                    SetSharedParam(mat, $"MAT_LAYER_{i}_MATERIAL", layerMat);
                if (!string.IsNullOrEmpty(layerThick))
                    SetSharedParam(mat, $"MAT_LAYER_{i}_THICKNESS_MM", layerThick);
                if (!string.IsNullOrEmpty(layerFunc))
                    SetSharedParam(mat, $"MAT_LAYER_{i}_FUNCTION", layerFunc);
            }
        }

        /// <summary>Set a shared parameter by name on the material, handling both string and double storage.</summary>
        private static void SetSharedParam(Material mat, string paramName, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                Parameter p = mat.LookupParameter(paramName);
                if (p == null || p.IsReadOnly) return;

                switch (p.StorageType)
                {
                    case StorageType.String:
                        p.Set(value);
                        break;
                    case StorageType.Double:
                        if (double.TryParse(value, out double dVal))
                            p.Set(dVal);
                        break;
                    case StorageType.Integer:
                        if (int.TryParse(value.Replace(".0", ""), out int iVal))
                            p.Set(iVal);
                        break;
                }
            }
            catch { /* Parameter not bound to Material category — skip silently */ }
        }

        /// <summary>Set a string BuiltInParameter on the material if the value is non-empty.</summary>
        private static void SetParam(Material mat, BuiltInParameter bip, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                Parameter p = mat.get_Parameter(bip);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    p.Set(value);
            }
            catch { /* param not available on this material */ }
        }

        /// <summary>Set a double BuiltInParameter on the material.</summary>
        private static void SetParamDouble(Material mat, BuiltInParameter bip, double value)
        {
            try
            {
                Parameter p = mat.get_Parameter(bip);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                    p.Set(value);
            }
            catch { /* param not available on this material */ }
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
        /// All shared parameter names that should be bound to the Material category.
        /// Collected from SharedParamMappings + layer params for comprehensive coverage.
        /// </summary>
        private static string[] AllMaterialParamNames
        {
            get
            {
                var names = new List<string>();
                foreach (var (_, paramName) in SharedParamMappings)
                    names.Add(paramName);
                // Layer data params
                names.Add("MAT_LAYER_COUNT");
                for (int i = 1; i <= 5; i++)
                {
                    names.Add($"MAT_LAYER_{i}_MATERIAL");
                    names.Add($"MAT_LAYER_{i}_THICKNESS_MM");
                    names.Add($"MAT_LAYER_{i}_FUNCTION");
                }
                return names.ToArray();
            }
        }

        /// <summary>
        /// Auto-bind all material shared parameters to OST_Materials if not already bound.
        /// This ensures LookupParameter works when writing CSV data to materials.
        /// Must be called BEFORE creating materials.
        /// </summary>
        public static int EnsureMaterialParamsBound(Document doc)
        {
            var app = doc.Application;
            string spFile = app.SharedParametersFilename;
            if (string.IsNullOrEmpty(spFile) || !File.Exists(spFile))
            {
                string autoPath = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                if (!string.IsNullOrEmpty(autoPath) && File.Exists(autoPath))
                {
                    app.SharedParametersFilename = autoPath;
                    spFile = autoPath;
                }
                else
                {
                    StingLog.Warn("EnsureMaterialParamsBound: MR_PARAMETERS.txt not found");
                    return 0;
                }
            }

            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
            {
                StingLog.Warn("EnsureMaterialParamsBound: cannot open shared param file");
                return 0;
            }

            // Build definition lookup
            var defLookup = new Dictionary<string, ExternalDefinition>(StringComparer.Ordinal);
            foreach (DefinitionGroup group in defFile.Groups)
                foreach (Definition def in group.Definitions)
                    if (def is ExternalDefinition ext)
                        defLookup[def.Name] = ext;

            // Get Material category
            Category matCat;
            try
            {
                matCat = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Materials);
                if (matCat == null || !matCat.AllowsBoundParameters)
                {
                    StingLog.Warn("EnsureMaterialParamsBound: OST_Materials not available");
                    return 0;
                }
            }
            catch
            {
                StingLog.Warn("EnsureMaterialParamsBound: OST_Materials not available");
                return 0;
            }

            CategorySet matCatSet = new CategorySet();
            matCatSet.Insert(matCat);

            // Pre-scan existing bindings to see which params are already bound to Materials
            var existingOnMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var iter = doc.ParameterBindings.ForwardIterator();
            while (iter.MoveNext())
            {
                string paramName = iter.Key?.Name;
                if (string.IsNullOrEmpty(paramName)) continue;

                if (iter.Current is InstanceBinding ib)
                {
                    var catIter = ib.Categories.ForwardIterator();
                    while (catIter.MoveNext())
                    {
                        if (catIter.Current is Category c &&
                            c.Id.Value == (int)BuiltInCategory.OST_Materials)
                        {
                            existingOnMaterials.Add(paramName);
                            break;
                        }
                    }
                }
            }

            // Bind missing params
            int bound = 0;
            string[] paramNames = AllMaterialParamNames;

            using (Transaction tx = new Transaction(doc, "STING Bind Material Params"))
            {
                tx.Start();
                try
                {
                    foreach (string paramName in paramNames)
                    {
                        if (existingOnMaterials.Contains(paramName)) continue;
                        if (!defLookup.TryGetValue(paramName, out ExternalDefinition extDef)) continue;

                        try
                        {
                            // Try to get existing binding and add Materials category to it
                            var existingBinding = doc.ParameterBindings.get_Item(extDef);
                            if (existingBinding is InstanceBinding existingIb)
                            {
                                existingIb.Categories.Insert(matCat);
                                doc.ParameterBindings.ReInsert(extDef, existingIb,
                                    GroupTypeId.General);
                                bound++;
                            }
                            else
                            {
                                // New binding just for Materials
                                InstanceBinding binding = app.Create.NewInstanceBinding(matCatSet);
                                bool result = doc.ParameterBindings.Insert(
                                    extDef, binding, GroupTypeId.General);
                                if (!result)
                                    result = doc.ParameterBindings.ReInsert(
                                        extDef, binding, GroupTypeId.General);
                                if (result) bound++;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Bind '{paramName}' to Materials: {ex.Message}");
                        }
                    }

                    tx.Commit();
                    StingLog.Info($"EnsureMaterialParamsBound: {bound} params newly bound to Materials " +
                        $"({existingOnMaterials.Count} already bound, {paramNames.Length} total)");
                }
                catch (Exception ex)
                {
                    StingLog.Error("EnsureMaterialParamsBound transaction failed", ex);
                    if (tx.HasStarted() && !tx.HasEnded())
                        tx.RollBack();
                }
            }

            return bound;
        }

        /// <summary>
        /// Shared material creation logic for BLE and MEP commands.
        /// Reads CSV, finds/duplicates base materials, applies all properties.
        /// Auto-binds shared params to Materials category before creation.
        /// Each batch is a standalone Transaction so Revit regenerates between batches.
        /// </summary>
        private const int MaterialBatchSize = 50;

        public static Result CreateMaterialsFromCsv(Document doc, string csvFileName,
            string dialogTitle)
        {
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

            // ── Step 0: Auto-bind material shared params to OST_Materials ──
            int paramsBound = EnsureMaterialParamsBound(doc);
            if (paramsBound > 0)
                StingLog.Info($"{dialogTitle}: auto-bound {paramsBound} shared params to Materials category");

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

            for (int batchStart = 0; batchStart < rows.Count; batchStart += MaterialBatchSize)
            {
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
                (paramsBound > 0 ? $"Material params bound: {paramsBound}\n" : "") +
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
                                catch
                                {
                                    // Name collision — share the base asset directly
                                    newMat.AppearanceAssetId = baseMat.AppearanceAssetId;
                                    assetCache[baseMatName] = baseMat.AppearanceAssetId;
                                }
                            }
                        }
                    }

                    if (baseMat.StructuralAssetId != ElementId.InvalidElementId)
                        newMat.StructuralAssetId = baseMat.StructuralAssetId;
                    if (baseMat.ThermalAssetId != ElementId.InvalidElementId)
                        newMat.ThermalAssetId = baseMat.ThermalAssetId;
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
                try { TaskDialog.Show("STING Tools", $"BLE Materials failed:\n{ex.Message}"); } catch { }
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
                try { TaskDialog.Show("STING Tools", $"MEP Materials failed:\n{ex.Message}"); } catch { }
                return Result.Failed;
            }
        }
    }
}
