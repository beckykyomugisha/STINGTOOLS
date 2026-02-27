using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Create wall types from BLE_MATERIALS.csv rows where MAT_ELEMENT_TYPE
    /// starts with "A-STR" (structural wall cores) or "A-ASM" (wall assemblies).
    /// Duplicates the default WallType and sets compound structure layers.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateWallsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Walls",
                "BLE_MATERIALS.csv",
                new[] { "A-STR", "A-ASM", "A-BLK" },
                CompoundTypeCreator.ElementKind.Wall);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFloorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Floors",
                "BLE_MATERIALS.csv",
                new[] { "A-FLR" },
                CompoundTypeCreator.ElementKind.Floor);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateCeilingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Ceilings",
                "BLE_MATERIALS.csv",
                new[] { "A-CLG" },
                CompoundTypeCreator.ElementKind.Ceiling);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateRoofsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Roofs",
                "BLE_MATERIALS.csv",
                new[] { "A-RF" },
                CompoundTypeCreator.ElementKind.Roof);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateDuctsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Ducts",
                "MEP_MATERIALS.csv",
                new[] { "M-DCT", "M-INS" },
                CompoundTypeCreator.ElementKind.Duct);
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreatePipesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            return CompoundTypeCreator.CreateTypes(doc, "Pipes",
                "MEP_MATERIALS.csv",
                new[] { "M-PPE", "P-DRN", "P-WSP" },
                CompoundTypeCreator.ElementKind.Pipe);
        }
    }

    /// <summary>
    /// Shared helper that reads CSV rows, creates or finds materials, builds
    /// CompoundStructure layers, and duplicates base types.
    /// Handles CSV data quirks: homogeneous materials with no layer breakdown,
    /// MEP layer-count mismatches, and thickness-sum discrepancies.
    /// </summary>
    internal static class CompoundTypeCreator
    {
        public enum ElementKind { Wall, Floor, Ceiling, Roof, Duct, Pipe }

        // CSV column indices (BLE_MATERIALS / MEP_MATERIALS)
        private const int ColElementType = 4;   // MAT_ELEMENT_TYPE
        private const int ColCategory = 5;      // MAT_CATEGORY
        private const int ColName = 6;          // MAT_NAME
        private const int ColThicknessMm = 9;   // MAT_THICKNESS_MM
        private const int ColLayerCount = 15;   // MAT_LAYER_COUNT
        // Layer columns repeat: MAT_LAYER_N_MATERIAL, MAT_LAYER_N_THICKNESS_MM, MAT_LAYER_N_FUNCTION
        // Layer 1 starts at 16, each layer occupies 3 columns
        private const int ColLayer1Start = 16;
        private const int LayerStride = 3;
        private const int MaxLayers = 5;        // CSV supports up to 5 layers

        public static Result CreateTypes(Document doc, string label,
            string csvFileName, string[] typeFilters, ElementKind kind)
        {
            string csvPath = StingToolsApp.FindDataFile(csvFileName);
            if (csvPath == null)
            {
                TaskDialog.Show($"Create {label}",
                    $"{csvFileName} not found in data directory.\n" +
                    $"Searched: {StingToolsApp.DataPath}");
                return Result.Failed;
            }

            // Parse CSV: skip comment lines and header
            var dataLines = File.ReadAllLines(csvPath)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .Skip(1) // skip header
                .ToList();

            // Filter to matching element types
            var rows = new List<string[]>();
            foreach (string line in dataLines)
            {
                string[] cols = StingToolsApp.ParseCsvLine(line);
                if (cols.Length <= ColName) continue;

                string elType = cols[ColElementType].Trim();
                if (typeFilters.Any(f =>
                    elType.Equals(f, StringComparison.OrdinalIgnoreCase)))
                {
                    rows.Add(cols);
                }
            }

            if (rows.Count == 0)
            {
                TaskDialog.Show($"Create {label}",
                    $"No matching rows found for {string.Join("/", typeFilters)} " +
                    $"in {csvFileName}.");
                return Result.Succeeded;
            }

            // Build material cache (name → ElementId)
            var materialCache = new Dictionary<string, ElementId>(
                StringComparer.OrdinalIgnoreCase);
            foreach (Material mat in new FilteredElementCollector(doc)
                .OfClass(typeof(Material)).Cast<Material>())
            {
                materialCache[mat.Name] = mat.Id;
            }

            // For compound types (wall/floor/ceiling/roof), collect existing type names
            var existingTypeNames = GetExistingTypeNames(doc, kind);

            int created = 0;
            int skipped = 0;
            int matCreated = 0;
            var errors = new List<string>();

            using (Transaction tx = new Transaction(doc, $"Create {label} Types"))
            {
                tx.Start();

                foreach (string[] cols in rows)
                {
                    string matName = cols[ColName].Trim();
                    string category = cols.Length > ColCategory
                        ? cols[ColCategory].Trim() : "";

                    // Build a type name from MAT_NAME to ensure uniqueness
                    // (MAT_CATEGORY alone is not unique — many rows share a category)
                    string typeName = $"STING - {matName}";

                    if (existingTypeNames.Contains(typeName))
                    {
                        skipped++;
                        continue;
                    }

                    // Ensure primary material exists in project
                    if (!materialCache.ContainsKey(matName) &&
                        !string.IsNullOrEmpty(matName))
                    {
                        try
                        {
                            ElementId newMatId = Material.Create(doc, matName);
                            if (newMatId != ElementId.InvalidElementId)
                            {
                                materialCache[matName] = newMatId;
                                matCreated++;
                                // Apply material appearance properties from CSV
                                Material newMat = doc.GetElement(newMatId) as Material;
                                if (newMat != null)
                                    MaterialPropertyHelper.ApplyMaterialProperties(newMat, cols);
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn($"Material create failed: {matName}: {ex.Message}");
                        }
                    }

                    ElementId matId = materialCache.TryGetValue(matName, out ElementId mid)
                        ? mid : ElementId.InvalidElementId;

                    // Parse thickness — use layer sum if total is zero/missing
                    double thicknessMm = ParseThickness(cols);

                    bool success = false;
                    try
                    {
                        switch (kind)
                        {
                            case ElementKind.Wall:
                                success = CreateWallType(doc, typeName, matId,
                                    thicknessMm, cols, materialCache);
                                break;
                            case ElementKind.Floor:
                                success = CreateFloorType(doc, typeName, matId,
                                    thicknessMm, cols, materialCache);
                                break;
                            case ElementKind.Ceiling:
                                success = CreateCeilingType(doc, typeName, matId,
                                    thicknessMm, cols, materialCache);
                                break;
                            case ElementKind.Roof:
                                success = CreateRoofType(doc, typeName, matId,
                                    thicknessMm, cols, materialCache);
                                break;
                            case ElementKind.Duct:
                            case ElementKind.Pipe:
                                success = CreateMEPType(doc, typeName, matId,
                                    thicknessMm, kind);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{typeName}: {ex.Message}");
                        StingLog.Warn($"Type create failed: {typeName}: {ex.Message}");
                    }

                    if (success)
                    {
                        created++;
                        existingTypeNames.Add(typeName);
                    }
                    else
                    {
                        skipped++;
                    }
                }

                tx.Commit();
            }

            string report = $"Created {created} {label.ToLower()} types.\n" +
                $"Skipped {skipped} (exist or failed).\n" +
                $"Materials created: {matCreated}\n" +
                $"Source: {Path.GetFileName(csvPath)} " +
                $"({rows.Count} matching rows)";
            if (errors.Count > 0)
                report += $"\n\nErrors ({errors.Count}):\n" +
                    string.Join("\n", errors.Take(10));

            TaskDialog.Show($"Create {label}", report);

            return Result.Succeeded;
        }

        /// <summary>
        /// Parse thickness from CSV, handling data quirks:
        /// - If MAT_THICKNESS_MM is set, use it
        /// - If zero/missing, compute from sum of layer thicknesses
        /// - Skip R-value codes (e.g. "R-0.01") in layer thickness columns
        /// - Apply sensible defaults per element kind
        /// </summary>
        private static double ParseThickness(string[] cols)
        {
            double totalMm = 0;
            if (cols.Length > ColThicknessMm)
            {
                string raw = cols[ColThicknessMm].Trim();
                double.TryParse(raw, out totalMm);
            }

            // If total is zero, sum actual layer thicknesses
            if (totalMm <= 0)
            {
                double layerSum = 0;
                for (int i = 0; i < MaxLayers; i++)
                {
                    int thickIdx = ColLayer1Start + (i * LayerStride) + 1;
                    if (thickIdx >= cols.Length) break;

                    string thickStr = cols[thickIdx].Trim();
                    if (string.IsNullOrEmpty(thickStr)) continue;
                    // Skip R-value codes (e.g. "R-3.6") which are thermal resistance, not thickness
                    if (thickStr.StartsWith("R-", StringComparison.OrdinalIgnoreCase)) continue;

                    if (double.TryParse(thickStr, out double lmm) && lmm > 0 && lmm < 1000)
                        layerSum += lmm;
                }
                if (layerSum > 0)
                    totalMm = layerSum;
            }

            // Final fallback
            if (totalMm <= 0) totalMm = 10;

            return totalMm;
        }

        private static bool CreateWallType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols,
            Dictionary<string, ElementId> materialCache)
        {
            var baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Kind == WallKind.Basic);

            if (baseType == null) return false;

            WallType newType = baseType.Duplicate(typeName) as WallType;
            if (newType == null) return false;

            var layers = BuildLayers(cols, matId, thicknessMm, doc, materialCache);
            if (layers.Count > 0)
            {
                CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                newType.SetCompoundStructure(cs);
            }

            return true;
        }

        private static bool CreateFloorType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols,
            Dictionary<string, ElementId> materialCache)
        {
            var baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault(ft => ft.IsFoundationSlab == false);

            if (baseType == null) return false;

            FloorType newType = baseType.Duplicate(typeName) as FloorType;
            if (newType == null) return false;

            var layers = BuildLayers(cols, matId, thicknessMm, doc, materialCache);
            if (layers.Count > 0)
            {
                CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                newType.SetCompoundStructure(cs);
            }

            return true;
        }

        private static bool CreateCeilingType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols,
            Dictionary<string, ElementId> materialCache)
        {
            var baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .FirstOrDefault();

            if (baseType == null) return false;

            CeilingType newType = baseType.Duplicate(typeName) as CeilingType;
            if (newType == null) return false;

            var layers = BuildLayers(cols, matId, thicknessMm, doc, materialCache);
            if (layers.Count > 0)
            {
                CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                newType.SetCompoundStructure(cs);
            }

            return true;
        }

        private static bool CreateRoofType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols,
            Dictionary<string, ElementId> materialCache)
        {
            var baseType = new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .FirstOrDefault();

            if (baseType == null) return false;

            RoofType newType = baseType.Duplicate(typeName) as RoofType;
            if (newType == null) return false;

            var layers = BuildLayers(cols, matId, thicknessMm, doc, materialCache);
            if (layers.Count > 0)
            {
                CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                newType.SetCompoundStructure(cs);
            }

            return true;
        }

        private static bool CreateMEPType(Document doc, string typeName,
            ElementId matId, double thicknessMm, ElementKind kind)
        {
            if (kind == ElementKind.Duct)
            {
                var baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Mechanical.DuctType))
                    .Cast<Autodesk.Revit.DB.Mechanical.DuctType>()
                    .FirstOrDefault();

                if (baseType == null) return false;
                var newType = baseType.Duplicate(typeName);
                return newType != null;
            }
            else // Pipe
            {
                var baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType))
                    .Cast<Autodesk.Revit.DB.Plumbing.PipeType>()
                    .FirstOrDefault();

                if (baseType == null) return false;
                var newType = baseType.Duplicate(typeName);
                return newType != null;
            }
        }

        /// <summary>
        /// Build CompoundStructureLayers from CSV layer columns.
        /// Detects actual layer count by scanning columns (handles MAT_LAYER_COUNT mismatches).
        /// Falls back to a single-layer structure for homogeneous materials with no layer data.
        /// Skips R-value codes and cable cross-section values stored in thickness columns.
        /// </summary>
        private static IList<CompoundStructureLayer> BuildLayers(
            string[] cols, ElementId defaultMatId, double defaultThickMm,
            Document doc, Dictionary<string, ElementId> materialCache)
        {
            var layers = new List<CompoundStructureLayer>();

            // Scan actual populated layers (don't trust MAT_LAYER_COUNT — 94 MEP rows wrong)
            int actualLayers = CountActualLayers(cols);

            if (actualLayers > 0)
            {
                for (int i = 0; i < actualLayers; i++)
                {
                    int baseIdx = ColLayer1Start + (i * LayerStride);
                    if (baseIdx + 2 >= cols.Length) break;

                    string layerMatName = cols[baseIdx].Trim();
                    string layerThickStr = cols[baseIdx + 1].Trim();
                    string layerFuncStr = cols[baseIdx + 2].Trim();

                    // Skip R-value codes in material column (thermal resistance, not materials)
                    if (layerMatName.StartsWith("R-", StringComparison.OrdinalIgnoreCase))
                        continue;

                    double layerThickMm = 0;
                    if (!string.IsNullOrEmpty(layerThickStr) &&
                        !layerThickStr.StartsWith("R-", StringComparison.OrdinalIgnoreCase))
                    {
                        double.TryParse(layerThickStr, out layerThickMm);
                    }

                    // Skip cable cross-section values (mm² stored as mm, e.g. 300.0 for a 300mm² conductor)
                    if (layerThickMm > 500) continue;
                    if (layerThickMm <= 0) layerThickMm = 10;

                    // Convert mm to feet (Revit internal units)
                    double thickFeet = layerThickMm / 304.8;

                    // Resolve layer material — create if missing
                    ElementId layerMatId = defaultMatId;
                    if (!string.IsNullOrEmpty(layerMatName))
                    {
                        if (materialCache.TryGetValue(layerMatName, out ElementId foundId))
                        {
                            layerMatId = foundId;
                        }
                        else
                        {
                            // Auto-create sub-materials (GALVANIZED STEEL, TILE ADHESIVE, etc.)
                            try
                            {
                                ElementId newId = Material.Create(doc, layerMatName);
                                if (newId != ElementId.InvalidElementId)
                                {
                                    materialCache[layerMatName] = newId;
                                    layerMatId = newId;
                                }
                            }
                            catch (Exception ex)
                            {
                                StingLog.Warn($"Layer material create failed '{layerMatName}': {ex.Message}");
                            }
                        }
                    }

                    MaterialFunctionAssignment func = MapLayerFunction(layerFuncStr);
                    layers.Add(new CompoundStructureLayer(thickFeet, func, layerMatId));
                }
            }

            // Fallback: single-layer homogeneous material (covers 165+ BLE rows with no layer data)
            if (layers.Count == 0)
            {
                double thickFeet = defaultThickMm / 304.8;
                layers.Add(new CompoundStructureLayer(
                    thickFeet,
                    MaterialFunctionAssignment.Structure,
                    defaultMatId));
            }

            return layers;
        }

        /// <summary>
        /// Count actual populated layers by scanning columns, ignoring MAT_LAYER_COUNT.
        /// A layer is considered populated if its material name column is non-empty
        /// and its thickness is parseable and positive.
        /// </summary>
        private static int CountActualLayers(string[] cols)
        {
            int count = 0;
            for (int i = 0; i < MaxLayers; i++)
            {
                int matIdx = ColLayer1Start + (i * LayerStride);
                int thickIdx = matIdx + 1;
                if (matIdx >= cols.Length) break;

                string matName = cols[matIdx].Trim();
                if (string.IsNullOrEmpty(matName)) continue;

                // Check if thickness column has a valid number
                if (thickIdx < cols.Length)
                {
                    string thickStr = cols[thickIdx].Trim();
                    if (!string.IsNullOrEmpty(thickStr))
                    {
                        // R-value codes count as populated (they indicate a real layer)
                        if (thickStr.StartsWith("R-", StringComparison.OrdinalIgnoreCase) ||
                            (double.TryParse(thickStr, out double v) && v > 0))
                        {
                            count++;
                        }
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Map CSV layer function strings to Revit MaterialFunctionAssignment.
        /// Handles the exact strings from BLE/MEP CSVs: "FINISH 1 [4]", "STRUCTURE [1]",
        /// "SUBSTRATE [2]", "THERMAL/AIR LAYER [3]", "MEMBRANE LAYER", etc.
        /// </summary>
        private static MaterialFunctionAssignment MapLayerFunction(string func)
        {
            if (string.IsNullOrEmpty(func))
                return MaterialFunctionAssignment.Structure;

            string upper = func.ToUpperInvariant().Trim();

            // Match exact CSV patterns: "FINISH 1 [4]", "FINISH 2 [5]"
            if (upper.StartsWith("FINISH 2"))
                return MaterialFunctionAssignment.Finish2;
            if (upper.StartsWith("FINISH") || upper.Contains("SURFACE"))
                return MaterialFunctionAssignment.Finish1;
            if (upper.Contains("STRUCT") || upper.Contains("CORE") || upper.StartsWith("STRUCTURE"))
                return MaterialFunctionAssignment.Structure;
            if (upper.Contains("SUBSTRATE"))
                return MaterialFunctionAssignment.Substrate;
            if (upper.Contains("THERMAL") || upper.Contains("AIR LAYER") || upper.Contains("INSUL"))
                return MaterialFunctionAssignment.Insulation;
            if (upper.Contains("MEMBRANE") || upper.Contains("BARRIER") || upper.Contains("VAPOR"))
                return MaterialFunctionAssignment.Membrane;

            return MaterialFunctionAssignment.Structure;
        }

        private static HashSet<string> GetExistingTypeNames(Document doc,
            ElementKind kind)
        {
            IEnumerable<string> names;
            switch (kind)
            {
                case ElementKind.Wall:
                    names = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Select(e => e.Name);
                    break;
                case ElementKind.Floor:
                    names = new FilteredElementCollector(doc)
                        .OfClass(typeof(FloorType))
                        .Select(e => e.Name);
                    break;
                case ElementKind.Ceiling:
                    names = new FilteredElementCollector(doc)
                        .OfClass(typeof(CeilingType))
                        .Select(e => e.Name);
                    break;
                case ElementKind.Roof:
                    names = new FilteredElementCollector(doc)
                        .OfClass(typeof(RoofType))
                        .Select(e => e.Name);
                    break;
                case ElementKind.Duct:
                    names = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Mechanical.DuctType))
                        .Select(e => e.Name);
                    break;
                case ElementKind.Pipe:
                    names = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType))
                        .Select(e => e.Name);
                    break;
                default:
                    names = Enumerable.Empty<string>();
                    break;
            }
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }
    }
}
