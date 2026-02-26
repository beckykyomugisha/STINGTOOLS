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

            using (Transaction tx = new Transaction(doc, $"Create {label} Types"))
            {
                tx.Start();

                foreach (string[] cols in rows)
                {
                    string matName = cols[ColName].Trim();
                    string category = cols.Length > ColCategory
                        ? cols[ColCategory].Trim() : "";

                    // Build a type name: "STING - CATEGORY - MAT_NAME"
                    string typeName = $"STING - {category}";

                    if (existingTypeNames.Contains(typeName))
                    {
                        skipped++;
                        continue;
                    }

                    // Ensure material exists
                    if (!materialCache.ContainsKey(matName) &&
                        !string.IsNullOrEmpty(matName))
                    {
                        ElementId newMatId = Material.Create(doc, matName);
                        if (newMatId != ElementId.InvalidElementId)
                        {
                            materialCache[matName] = newMatId;
                            matCreated++;
                        }
                    }

                    ElementId matId = materialCache.ContainsKey(matName)
                        ? materialCache[matName]
                        : ElementId.InvalidElementId;

                    // Parse thickness
                    double thicknessMm = 0;
                    if (cols.Length > ColThicknessMm)
                        double.TryParse(cols[ColThicknessMm].Trim(), out thicknessMm);
                    if (thicknessMm <= 0) thicknessMm = 200; // default 200mm

                    bool success = false;
                    switch (kind)
                    {
                        case ElementKind.Wall:
                            success = CreateWallType(doc, typeName, matId,
                                thicknessMm, cols);
                            break;
                        case ElementKind.Floor:
                            success = CreateFloorType(doc, typeName, matId,
                                thicknessMm, cols);
                            break;
                        case ElementKind.Ceiling:
                            success = CreateCeilingType(doc, typeName, matId,
                                thicknessMm, cols);
                            break;
                        case ElementKind.Roof:
                            success = CreateRoofType(doc, typeName, matId,
                                thicknessMm, cols);
                            break;
                        case ElementKind.Duct:
                        case ElementKind.Pipe:
                            success = CreateMEPType(doc, typeName, matId,
                                thicknessMm, kind);
                            break;
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

            TaskDialog.Show($"Create {label}",
                $"Created {created} {label.ToLower()} types.\n" +
                $"Skipped {skipped} (exist or failed).\n" +
                $"Materials created: {matCreated}\n" +
                $"Source: {Path.GetFileName(csvPath)} " +
                $"({rows.Count} matching rows)");

            return Result.Succeeded;
        }

        private static bool CreateWallType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols)
        {
            try
            {
                // Find a base wall type to duplicate
                var baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault(wt => wt.Kind == WallKind.Basic);

                if (baseType == null) return false;

                WallType newType = baseType.Duplicate(typeName) as WallType;
                if (newType == null) return false;

                // Build compound structure with layers from CSV
                var layers = BuildLayers(cols, matId, thicknessMm, doc);
                if (layers.Count > 0)
                {
                    CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                    newType.SetCompoundStructure(cs);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CreateFloorType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols)
        {
            try
            {
                var baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(FloorType))
                    .Cast<FloorType>()
                    .FirstOrDefault(ft => ft.IsFoundationSlab == false);

                if (baseType == null) return false;

                FloorType newType = baseType.Duplicate(typeName) as FloorType;
                if (newType == null) return false;

                var layers = BuildLayers(cols, matId, thicknessMm, doc);
                if (layers.Count > 0)
                {
                    CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                    newType.SetCompoundStructure(cs);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CreateCeilingType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols)
        {
            try
            {
                var baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(CeilingType))
                    .Cast<CeilingType>()
                    .FirstOrDefault();

                if (baseType == null) return false;

                CeilingType newType = baseType.Duplicate(typeName) as CeilingType;
                if (newType == null) return false;

                var layers = BuildLayers(cols, matId, thicknessMm, doc);
                if (layers.Count > 0)
                {
                    CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                    newType.SetCompoundStructure(cs);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CreateRoofType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols)
        {
            try
            {
                var baseType = new FilteredElementCollector(doc)
                    .OfClass(typeof(RoofType))
                    .Cast<RoofType>()
                    .FirstOrDefault();

                if (baseType == null) return false;

                RoofType newType = baseType.Duplicate(typeName) as RoofType;
                if (newType == null) return false;

                var layers = BuildLayers(cols, matId, thicknessMm, doc);
                if (layers.Count > 0)
                {
                    CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                    newType.SetCompoundStructure(cs);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CreateMEPType(Document doc, string typeName,
            ElementId matId, double thicknessMm, ElementKind kind)
        {
            try
            {
                if (kind == ElementKind.Duct)
                {
                    var baseType = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Mechanical.DuctType))
                        .FirstOrDefault();

                    if (baseType == null) return false;
                    var newType = baseType.Duplicate(typeName);
                    return newType != null;
                }
                else // Pipe
                {
                    var baseType = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType))
                        .FirstOrDefault();

                    if (baseType == null) return false;
                    var newType = baseType.Duplicate(typeName);
                    return newType != null;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Build CompoundStructureLayers from CSV layer columns.
        /// Falls back to a single-layer structure if no layer data is present.
        /// </summary>
        private static IList<CompoundStructureLayer> BuildLayers(
            string[] cols, ElementId defaultMatId, double defaultThickMm,
            Document doc)
        {
            var layers = new List<CompoundStructureLayer>();

            // Try to read layer count from CSV
            int layerCount = 0;
            if (cols.Length > ColLayerCount)
                int.TryParse(cols[ColLayerCount].Trim(), out layerCount);

            if (layerCount > 0 && layerCount <= 10)
            {
                // Build material cache for layer material lookups
                var matCache = new Dictionary<string, ElementId>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (Material m in new FilteredElementCollector(doc)
                    .OfClass(typeof(Material)).Cast<Material>())
                {
                    matCache[m.Name] = m.Id;
                }

                for (int i = 0; i < layerCount; i++)
                {
                    int baseIdx = ColLayer1Start + (i * LayerStride);
                    if (baseIdx + 2 >= cols.Length) break;

                    string layerMatName = cols[baseIdx].Trim();
                    string layerThickStr = cols[baseIdx + 1].Trim();
                    string layerFuncStr = cols[baseIdx + 2].Trim();

                    double layerThickMm = 0;
                    double.TryParse(layerThickStr, out layerThickMm);
                    if (layerThickMm <= 0) layerThickMm = 10;

                    // Convert mm to feet (Revit internal units)
                    double thickFeet = layerThickMm / 304.8;

                    // Resolve material
                    ElementId layerMatId = defaultMatId;
                    if (!string.IsNullOrEmpty(layerMatName) &&
                        matCache.TryGetValue(layerMatName, out ElementId foundId))
                    {
                        layerMatId = foundId;
                    }

                    // Map CSV function to Revit MaterialFunctionAssignment
                    MaterialFunctionAssignment func = MapLayerFunction(layerFuncStr);

                    layers.Add(new CompoundStructureLayer(
                        thickFeet, func, layerMatId));
                }
            }

            // Fallback: single layer with default material and thickness
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
        /// Map CSV layer function strings to Revit MaterialFunctionAssignment.
        /// </summary>
        private static MaterialFunctionAssignment MapLayerFunction(string func)
        {
            if (string.IsNullOrEmpty(func))
                return MaterialFunctionAssignment.Structure;

            string upper = func.ToUpperInvariant().Trim();
            if (upper.Contains("STRUCT") || upper.Contains("CORE"))
                return MaterialFunctionAssignment.Structure;
            if (upper.Contains("FINISH") || upper.Contains("SURFACE"))
                return MaterialFunctionAssignment.Finish1;
            if (upper.Contains("SUBSTRATE") || upper.Contains("BOARD"))
                return MaterialFunctionAssignment.Substrate;
            if (upper.Contains("INSUL") || upper.Contains("THERMAL"))
                return MaterialFunctionAssignment.ThermalOrAir;
            if (upper.Contains("MEMBRANE") || upper.Contains("BARRIER") ||
                upper.Contains("VAPOR"))
                return MaterialFunctionAssignment.MembraneLayer;

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
