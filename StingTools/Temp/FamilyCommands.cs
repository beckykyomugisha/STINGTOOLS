using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Materials;

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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
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
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;
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
        public enum ElementKind { Wall, Floor, Ceiling, Roof, Duct, Pipe, CableTray, Conduit }

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

            // PERF-001: Pre-collect base types ONCE before the CSV row loop.
            // Each CreateXxxType previously ran a FilteredElementCollector per row — O(rows) collectors.
            WallType    baseWallType     = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().FirstOrDefault(wt => wt.Kind == WallKind.Basic);
            FloorType   baseFloorType    = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault(ft => ft.IsFoundationSlab == false);
            CeilingType baseCeilingType  = new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>().FirstOrDefault();
            RoofType    baseRoofType     = new FilteredElementCollector(doc).OfClass(typeof(RoofType)).Cast<RoofType>().FirstOrDefault();
            var baseDuctType     = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Mechanical.DuctType)).FirstOrDefault() as ElementType;
            var basePipeType     = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType)).FirstOrDefault() as ElementType;
            var baseCableTrayType = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Electrical.CableTrayType)).FirstOrDefault() as ElementType;
            var baseConduitType  = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Electrical.ConduitType)).FirstOrDefault() as ElementType;

            // created/skipped counts live on the tally now: a type built with the
            // declared build-up and a type refused for want of one must not share a
            // number, which is what "Created N" used to do.
            int matCreated = 0;
            var errors = new List<string>();
            // W1/W2: kept apart from `errors` on purpose. A type that was refused because
            // its layers could not be read is a different fact from a type whose name
            // clashed, and one count for both is how this stayed invisible.
            var tally = new TypeCreationTally();
            var layerNotes = new List<string>();

            bool cancelled = false;
            using (Transaction tx = new Transaction(doc, $"Create {label} Types"))
            {
                tx.Start();
                int processed = 0;

                foreach (string[] cols in rows)
                {
                    // Cancellation check every 50 types
                    if (++processed % 50 == 0 && EscapeChecker.IsEscapePressed())
                    {
                        cancelled = true;
                        StingLog.Info($"Create {label}: cancelled by user at {processed}/{rows.Count}");
                        break;
                    }
                    string matName = cols[ColName].Trim();
                    string category = cols.Length > ColCategory
                        ? cols[ColCategory].Trim() : "";

                    // Build a type name from MAT_NAME to ensure uniqueness
                    // (MAT_CATEGORY alone is not unique — many rows share a category)
                    string typeName = matName;

                    if (existingTypeNames.Contains(typeName))
                    {
                        tally.Add(label, typeName, TypeCreationOutcome.AlreadyPresent);
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
                                    thicknessMm, cols, materialCache, baseWallType,
                                    tally, layerNotes);
                                break;
                            case ElementKind.Floor:
                                success = CreateFloorType(doc, typeName, matId,
                                    thicknessMm, cols, materialCache, baseFloorType,
                                    tally, layerNotes);
                                break;
                            case ElementKind.Ceiling:
                                success = CreateCeilingType(doc, typeName, matId,
                                    thicknessMm, cols, materialCache, baseCeilingType,
                                    tally, layerNotes);
                                break;
                            case ElementKind.Roof:
                                success = CreateRoofType(doc, typeName, matId,
                                    thicknessMm, cols, materialCache, baseRoofType,
                                    tally, layerNotes);
                                break;
                            case ElementKind.Duct:
                            case ElementKind.Pipe:
                            case ElementKind.CableTray:
                            case ElementKind.Conduit:
                                success = CreateMEPType(doc, typeName, matId,
                                    thicknessMm, kind,
                                    baseDuctType, basePipeType,
                                    baseCableTrayType, baseConduitType);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{typeName}: {ex.Message}");
                        StingLog.Warn($"Type create failed: {typeName}: {ex.Message}");
                    }

                    // The outcome was recorded by ApplyStructureOrFail, which is the only
                    // place that knows whether the structure actually landed.
                    if (success) existingTypeNames.Add(typeName);
                }

                tx.Commit();
            }
            string report = tally.Report() +
                $"Materials created: {matCreated}\n" +
                (cancelled ? "CANCELLED by user (Escape key). Types created so far are kept.\n" : "") +
                $"Source: {Path.GetFileName(csvPath)} " +
                $"({rows.Count} matching rows)";
            if (layerNotes.Count > 0)
                report += $"\n\nRead notes ({layerNotes.Count}) — what the register said versus "
                    + $"what could be built from it:\n"
                    + string.Join("\n", layerNotes.Take(10))
                    + (layerNotes.Count > 10
                        ? $"\n… and {layerNotes.Count - 10} more, in the log" : "");
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

        // ══════════════════════════════════════════════════════════════════
        //  W1 — a type whose structure failed was reported as created
        // ══════════════════════════════════════════════════════════════════
        //
        // All four Create*Type functions below caught a SetCompoundStructure
        // failure, logged a warning, and returned TRUE. The comment said so
        // honestly — "type was created, just no layers" — and then the function
        // told its caller the whole operation had succeeded. The type existed,
        // carried the BASE type's build-up, and was counted as created.
        //
        // CreateMEPType, eighty lines below, has always done the opposite: every
        // failure path logs and returns false. The codebase knew.
        //
        // THE CHOICE, made explicitly because the brief asked for it to be:
        // the half-made type is DELETED, inside the same transaction. Keeping it
        // would leave a type carrying a build-up nobody asked for, and a wrong
        // quantity that looks like a right one is worse than a missing type — it
        // measures, prices and carbon-counts exactly like a real one. Deleting is
        // safe here specifically because the type was duplicated microseconds
        // earlier in this same transaction and nothing can reference it yet:
        // this command places no instances. If that ever stops being true, keep
        // it and report it as unbuilt instead — but do not go back to reporting
        // it as built.
        //
        // NOT established: whether this path is what produced the 87 identical
        // 100 mm concrete floors on the 2026-09-09 model. It is consistent with
        // their shape, and the logs on the live plugin path start 2026-08-17 and
        // hold no type-creation line. Fixed on its own terms.
        private static bool ApplyStructureOrFail(
            Document doc, HostObjAttributes newType, string kind, string typeName,
            IList<CompoundStructureLayer> layers, TypeCreationTally tally)
        {
            if (layers == null || layers.Count == 0)
                return Fail(doc, newType, kind, typeName,
                            TypeCreationOutcome.RefusedNoLayers,
                            "no layer could be built from its register row", tally);

            try
            {
                CompoundStructure cs = CompoundStructure.CreateSimpleCompoundStructure(layers);
                // Revit applies default end-cap / wrapping conditions that are invalid on
                // non-wall types; clearing them is why this ever threw.
                cs.OpeningWrapping = OpeningWrappingCondition.None;
                newType.SetCompoundStructure(cs);
                tally.Add(kind, typeName, TypeCreationOutcome.CreatedAsDeclared);
                return true;
            }
            catch (Exception ex)
            {
                return Fail(doc, newType, kind, typeName,
                            TypeCreationOutcome.RefusedStructureFailed, ex.Message, tally);
            }
        }

        private static bool Fail(Document doc, HostObjAttributes newType, string kind,
                                 string typeName, TypeCreationOutcome outcome, string why,
                                 TypeCreationTally tally)
        {
            var landed = outcome;
            string note = why;
            try
            {
                doc.Delete(newType.Id);
                note += " (the half-made type was removed)";
            }
            catch (Exception delEx)
            {
                // Reported, never swallowed: a type left behind carrying the base type's
                // build-up is the exact shape this change exists to stop, and it is a
                // WORSE outcome than a clean refusal rather than the same one.
                landed = TypeCreationOutcome.RefusedButLeftBehind;
                note += $" — and it could NOT be removed ({delEx.Message}), so a type named "
                      + $"'{typeName}' is in the model carrying the BASE type's build-up";
            }
            tally.Add(kind, typeName, landed, note);
            StingLog.Warn($"CompoundTypeCreator: {kind} '{typeName}' NOT created: {note}");
            return false;
        }

        private static bool CreateWallType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols,
            Dictionary<string, ElementId> materialCache,
            WallType baseWallType,
            TypeCreationTally tally, ICollection<string> notes)
        {
            // PERF-001: base type pre-collected by caller; fall back to per-call collect only if null
            var baseType = baseWallType ?? new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .FirstOrDefault(wt => wt.Kind == WallKind.Basic);

            if (baseType == null) return false;

            WallType newType = baseType.Duplicate(typeName) as WallType;
            if (newType == null) return false;

            var layers = BuildLayers(cols, matId, thicknessMm, doc, materialCache, notes);
            return ApplyStructureOrFail(doc, newType, "Wall", typeName, layers, tally);
        }

        private static bool CreateFloorType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols,
            Dictionary<string, ElementId> materialCache,
            FloorType baseFloorType,
            TypeCreationTally tally, ICollection<string> notes)
        {
            // PERF-001: base type pre-collected by caller; fall back to per-call collect only if null
            var baseType = baseFloorType ?? new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault(ft => ft.IsFoundationSlab == false);

            if (baseType == null) return false;

            FloorType newType = baseType.Duplicate(typeName) as FloorType;
            if (newType == null) return false;

            var layers = BuildLayers(cols, matId, thicknessMm, doc, materialCache, notes);
            return ApplyStructureOrFail(doc, newType, "Floor", typeName, layers, tally);
        }

        private static bool CreateCeilingType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols,
            Dictionary<string, ElementId> materialCache,
            CeilingType baseCeilingType,
            TypeCreationTally tally, ICollection<string> notes)
        {
            // PERF-001: base type pre-collected by caller; fall back to per-call collect only if null
            var baseType = baseCeilingType ?? new FilteredElementCollector(doc)
                .OfClass(typeof(CeilingType))
                .Cast<CeilingType>()
                .FirstOrDefault();

            if (baseType == null) return false;

            CeilingType newType = baseType.Duplicate(typeName) as CeilingType;
            if (newType == null) return false;

            var layers = BuildLayers(cols, matId, thicknessMm, doc, materialCache, notes);
            return ApplyStructureOrFail(doc, newType, "Ceiling", typeName, layers, tally);
        }

        private static bool CreateRoofType(Document doc, string typeName,
            ElementId matId, double thicknessMm, string[] cols,
            Dictionary<string, ElementId> materialCache,
            RoofType baseRoofType,
            TypeCreationTally tally, ICollection<string> notes)
        {
            // PERF-001: base type pre-collected by caller; fall back to per-call collect only if null
            var baseType = baseRoofType ?? new FilteredElementCollector(doc)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .FirstOrDefault();

            if (baseType == null) return false;

            RoofType newType = baseType.Duplicate(typeName) as RoofType;
            if (newType == null) return false;

            var layers = BuildLayers(cols, matId, thicknessMm, doc, materialCache, notes);
            return ApplyStructureOrFail(doc, newType, "Roof", typeName, layers, tally);
        }

        private static bool CreateMEPType(Document doc, string typeName,
            ElementId matId, double thicknessMm, ElementKind kind,
            ElementType baseDuctType = null, ElementType basePipeType = null,
            ElementType baseCableTrayType = null, ElementType baseConduitType = null)
        {
            // PERF-001: base types pre-collected by caller; fall back to per-call collect only if null
            switch (kind)
            {
                case ElementKind.Duct:
                {
                    var bt = baseDuctType ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Mechanical.DuctType))
                        .FirstOrDefault() as ElementType;
                    if (bt == null) return false;
                    try { return bt.Duplicate(typeName) != null; }
                    catch (Exception ex) { StingLog.Warn($"Duplicate DuctType '{typeName}': {ex.Message}"); return false; }
                }
                case ElementKind.Pipe:
                {
                    var bt = basePipeType ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipeType))
                        .FirstOrDefault() as ElementType;
                    if (bt == null) return false;
                    try { return bt.Duplicate(typeName) != null; }
                    catch (Exception ex) { StingLog.Warn($"Duplicate PipeType '{typeName}': {ex.Message}"); return false; }
                }
                case ElementKind.CableTray:
                {
                    var bt = baseCableTrayType ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Electrical.CableTrayType))
                        .FirstOrDefault() as ElementType;
                    if (bt == null) return false;
                    try { return bt.Duplicate(typeName) != null; }
                    catch (Exception ex) { StingLog.Warn($"Duplicate CableTrayType '{typeName}': {ex.Message}"); return false; }
                }
                case ElementKind.Conduit:
                {
                    var bt = baseConduitType ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Electrical.ConduitType))
                        .FirstOrDefault() as ElementType;
                    if (bt == null) return false;
                    try { return bt.Duplicate(typeName) != null; }
                    catch (Exception ex) { StingLog.Warn($"Duplicate ConduitType '{typeName}': {ex.Message}"); return false; }
                }
                default:
                    return false;
            }
        }

        /// <summary>
        /// Build CompoundStructureLayers from CSV layer columns.
        /// Detects actual layer count by scanning columns (handles MAT_LAYER_COUNT mismatches).
        /// Falls back to a single-layer structure for homogeneous materials with no layer data.
        /// Skips R-value codes and cable cross-section values stored in thickness columns.
        /// </summary>
        /// <summary>
        /// The build-up the register row asks for, as Revit layers.
        ///
        /// <para>The decisions live in <see cref="CompoundLayerPlanner"/>, which is
        /// Revit-free and therefore assertable. They used to live here, where a missing
        /// thickness silently became 10 mm, a layer over 500 mm was silently dropped, a
        /// failed material create silently substituted the type's own material, and a row
        /// that declared layers nobody could read came out as a single default layer —
        /// indistinguishable from a genuinely single-material row. None of it reached the
        /// caller, so a run that read forty real thicknesses printed the same line as one
        /// that invented forty.</para>
        ///
        /// <para><paramref name="notes"/> carries them out. A fatal issue means the type
        /// was NOT built as declared, and <c>ApplyStructureOrFail</c> refuses it.</para>
        /// </summary>
        private static IList<CompoundStructureLayer> BuildLayers(
            string[] cols, ElementId defaultMatId, double defaultThickMm,
            Document doc, Dictionary<string, ElementId> materialCache,
            ICollection<string> notes = null)
        {
            string rowName = cols.Length > ColName ? cols[ColName].Trim() : "";

            var slotMat = new List<string>();
            var slotThick = new List<string>();
            var slotFunc = new List<string>();
            for (int i = 0; i < MaxLayers; i++)
            {
                int b = ColLayer1Start + (i * LayerStride);
                slotMat.Add(b < cols.Length ? cols[b] : "");
                slotThick.Add(b + 1 < cols.Length ? cols[b + 1] : "");
                slotFunc.Add(b + 2 < cols.Length ? cols[b + 2] : "");
            }

            var plan = CompoundLayerPlanner.Plan(
                slotMat, slotThick, slotFunc, rowName, defaultThickMm);

            foreach (var issue in plan.Issues)
                notes?.Add($"{rowName}: {issue}");

            // A fatal issue means the row was not read as declared. Returning no layers is
            // how that reaches ApplyStructureOrFail, which refuses the type and says why —
            // rather than building something nobody asked for and counting it.
            if (plan.Fatal.Any())
            {
                notes?.Add($"{rowName}: NOT built — {plan.Fatal.Count()} unreadable or dropped "
                         + "layer(s), so its build-up would not be the one the register declares");
                return new List<CompoundStructureLayer>();
            }

            var layers = new List<CompoundStructureLayer>();
            foreach (var pl in plan.Layers)
            {
                ElementId layerMatId = ResolveLayerMaterial(
                    doc, pl.Material, defaultMatId, materialCache, rowName, notes);
                layers.Add(new CompoundStructureLayer(
                    pl.ThicknessMm / 304.8, MapLayerFunction(pl.Function), layerMatId));
            }

            // Revit requires at least one STRUCTURE layer. Promoting the thickest is a
            // presentation choice, not a quantity change — the thicknesses and materials
            // are untouched — so it is a note rather than a failure.
            if (layers.Count > 0 && !layers.Any(l => l.Function == MaterialFunctionAssignment.Structure))
            {
                int thickest = 0;
                for (int i = 1; i < layers.Count; i++)
                    if (layers[i].Width > layers[thickest].Width) thickest = i;
                var old = layers[thickest];
                layers[thickest] = new CompoundStructureLayer(
                    old.Width, MaterialFunctionAssignment.Structure, old.MaterialId);
                notes?.Add($"{rowName}: no layer is declared STRUCTURE, so the thickest was "
                         + "promoted to satisfy Revit (thicknesses and materials unchanged)");
            }

            return layers;
        }

        /// <summary>
        /// The material for one layer, created if the project does not have it.
        ///
        /// <para>A create failure used to leave <c>layerMatId</c> on the TYPE's own
        /// material and add the layer anyway — a layer with the wrong material, warned for
        /// the create and silent about the substitution. It is now a note the caller sees,
        /// and the substitution is named.</para>
        /// </summary>
        private static ElementId ResolveLayerMaterial(
            Document doc, string layerMatName, ElementId defaultMatId,
            Dictionary<string, ElementId> materialCache, string rowName, ICollection<string> notes)
        {
            if (string.IsNullOrWhiteSpace(layerMatName)) return defaultMatId;
            if (materialCache.TryGetValue(layerMatName, out ElementId found)) return found;

            try
            {
                ElementId created = Material.Create(doc, layerMatName);
                if (created != ElementId.InvalidElementId)
                {
                    materialCache[layerMatName] = created;
                    return created;
                }
                notes?.Add($"{rowName}: layer material '{layerMatName}' could not be created, "
                         + "so that layer carries the type's own material instead");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Layer material create failed '{layerMatName}': {ex.Message}");
                notes?.Add($"{rowName}: layer material '{layerMatName}' could not be created "
                         + $"({ex.Message}), so that layer carries the type's own material instead");
            }
            return defaultMatId;
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
                case ElementKind.CableTray:
                    names = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Electrical.CableTrayType))
                        .Select(e => e.Name);
                    break;
                case ElementKind.Conduit:
                    names = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Electrical.ConduitType))
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
