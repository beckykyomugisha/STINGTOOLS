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
        public const int ColName = 6;              // MAT_NAME
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
        /// </summary>
        /// <summary>Max materials to create per transaction before committing and
        /// regenerating. Large batches (464 MEP materials in one go) push Revit's
        /// regeneration past its limits, causing crashes especially when other plugins
        /// (DiRoots, pyRevit) have loaded conflicting assembly versions.</summary>
        private const int MaterialBatchSize = 50;

        public static Result CreateMaterialsFromCsv(Document doc, string csvFileName,
            string dialogTitle)
        {
            string csvPath = StingToolsApp.FindDataFile(csvFileName);
            if (csvPath == null)
            {
                TaskDialog.Show(dialogTitle,
                    $"{csvFileName} not found in the data directory.\n" +
                    $"Searched: {StingToolsApp.DataPath}");
                return Result.Failed;
            }

            var lines = File.ReadAllLines(csvPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Skip(1) // skip header
                .ToList();

            int created = 0;
            int skipped = 0;
            int duplicated = 0;
            int batchCount = 0;

            // Build caches OUTSIDE the transaction (read-only operations)
            var baseMaterialCache = BuildBaseMaterialCache(doc);
            var existingNames = new HashSet<string>(
                baseMaterialCache.Keys, StringComparer.OrdinalIgnoreCase);
            var fillPatternCache = BuildFillPatternCache(doc);

            // Process in batches to avoid overwhelming Revit's regeneration engine.
            // Creating 464+ materials in a single transaction triggers internal DOPT
            // failures, especially when assembly version conflicts from co-loaded
            // plugins (DiRoots.One, pyRevitLoader) destabilize the CLR.
            for (int batchStart = 0; batchStart < lines.Count; batchStart += MaterialBatchSize)
            {
                int batchEnd = Math.Min(batchStart + MaterialBatchSize, lines.Count);
                batchCount++;

                using (Transaction tx = new Transaction(doc,
                    $"STING {dialogTitle} (batch {batchCount})"))
                {
                    tx.Start();

                    for (int i = batchStart; i < batchEnd; i++)
                    {
                        string[] cols = StingToolsApp.ParseCsvLine(lines[i]);
                        if (cols.Length < 7) continue;

                        string matName = cols[ColName].Trim();
                        if (string.IsNullOrEmpty(matName)) continue;

                        if (existingNames.Contains(matName))
                        {
                            skipped++;
                            continue;
                        }

                        try
                        {
                            // Create material from base (duplicate native) or blank
                            Material newMat = CreateFromBase(doc, matName, cols, baseMaterialCache);
                            if (newMat != null)
                            {
                                // Apply all CSV properties (color, patterns, class, etc.)
                                ApplyMaterialProperties(newMat, cols, doc, fillPatternCache);

                                // Track whether base material was used
                                string baseMatName = GetCol(cols, ColBaseMaterial);
                                if (!string.IsNullOrEmpty(baseMatName) &&
                                    baseMaterialCache.ContainsKey(baseMatName))
                                    duplicated++;

                                created++;
                                existingNames.Add(matName);
                                // Add to base cache so later rows can reference this material
                                baseMaterialCache[matName] = newMat;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Material create failed '{matName}': {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                // Let Revit regenerate between batches — prevents internal state overflow
                try { doc.Regenerate(); } catch (Exception ex)
                {
                    StingLog.Warn($"Regenerate after batch {batchCount}: {ex.Message}");
                }

                // Progress logging per batch
                StingLog.Info($"{dialogTitle}: batch {batchCount} done — {created} created, {skipped} skipped");
            }

            string report = $"Created {created} materials in {batchCount} batches.\n" +
                $"  Duplicated from base: {duplicated}\n" +
                $"  Created blank: {created - duplicated}\n" +
                $"Skipped {skipped} (already exist).\n" +
                $"Source: {Path.GetFileName(csvPath)} ({lines.Count} rows)";
            TaskDialog.Show(dialogTitle, report);

            StingLog.Info($"{dialogTitle}: {report.Replace("\n", " | ")}");
            return Result.Succeeded;
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
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            if (uidoc == null) { TaskDialog.Show("STING Tools", "No document is open."); return Result.Failed; }
            return MaterialPropertyHelper.CreateMaterialsFromCsv(
                uidoc.Document, "BLE_MATERIALS.csv", "Create BLE Materials");
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
            UIDocument uidoc = ParameterHelpers.GetApp(commandData).ActiveUIDocument;
            if (uidoc == null) { TaskDialog.Show("STING Tools", "No document is open."); return Result.Failed; }
            return MaterialPropertyHelper.CreateMaterialsFromCsv(
                uidoc.Document, "MEP_MATERIALS.csv", "Create MEP Materials");
        }
    }
}
