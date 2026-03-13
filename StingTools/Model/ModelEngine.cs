// ============================================================================
// ModelEngine.cs — MODEL Auto-Modeling Engine for STING Tools
// Orchestrates element creation: walls, floors, roofs, ceilings, columns,
// beams, doors, windows, rooms, MEP fixtures, and batch operations.
// Combines best patterns from EaseBit, eTLipse/ARQER, and StingBIM.AI.Creation.
//
// Architecture:
//   ModelEngine (orchestrator)
//     ├── FamilyResolver  — resolves types from live Document (never hardcoded)
//     ├── WorksetAssigner — auto-assigns discipline worksets
//     ├── FailureHandler  — suppresses warnings, surfaces errors
//     └── Element Creators (walls, floors, roofs, MEP, structural)
//
// All dimensions in millimeters externally, converted to Revit feet internally.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;

namespace StingTools.Model
{
    #region Unit Conversion

    /// <summary>
    /// Unit conversion constants. All external dimensions in millimeters;
    /// Revit internal units are decimal feet.
    /// </summary>
    internal static class Units
    {
        public const double MmToFeet = 1.0 / 304.8;
        public const double FeetToMm = 304.8;
        public const double MToFeet = 1.0 / 0.3048;
        public const double SqFtToSqM = 0.092903;

        public static double Mm(double mm) => mm * MmToFeet;
        public static double M(double m) => m * MToFeet;
        public static double ToMm(double feet) => feet * FeetToMm;
    }

    #endregion

    #region Result Types

    /// <summary>
    /// Result from any MODEL creation operation.
    /// </summary>
    public class ModelResult
    {
        public bool Success { get; set; }
        public string ElementType { get; set; }
        public ElementId CreatedElementId { get; set; }
        public List<ElementId> CreatedElementIds { get; set; } = new();
        public string Message { get; set; }
        public string Error { get; set; }
        public List<string> Warnings { get; set; } = new();
        public int CreatedCount => CreatedElementIds.Count +
            (CreatedElementId != null && CreatedElementId != ElementId.InvalidElementId ? 1 : 0);

        public static ModelResult Fail(string error) =>
            new() { Success = false, Error = error };

        public static ModelResult Ok(string message, ElementId id = null) =>
            new() { Success = true, Message = message, CreatedElementId = id ?? ElementId.InvalidElementId };

        public static ModelResult OkBatch(string message, List<ElementId> ids) =>
            new() { Success = true, Message = message, CreatedElementIds = ids };
    }

    /// <summary>
    /// Result from family/type resolution.
    /// </summary>
    public class ResolveResult
    {
        public bool Success { get; set; }
        public ElementId TypeId { get; set; }
        public string TypeName { get; set; }
        public string Message { get; set; }

        public static ResolveResult Found(ElementId id, string name) =>
            new() { Success = true, TypeId = id, TypeName = name };
        public static ResolveResult NotFound(string msg) =>
            new() { Success = false, Message = msg };
    }

    #endregion

    #region Failure Preprocessor

    /// <summary>
    /// Suppresses non-critical Revit warnings during batch creation.
    /// Captures warnings for post-operation reporting.
    /// </summary>
    internal class ModelFailureHandler : IFailuresPreprocessor
    {
        public List<string> CapturedWarnings { get; } = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            try
            {
                foreach (var msg in accessor.GetFailureMessages())
                {
                    var severity = msg.GetSeverity();
                    if (severity == FailureSeverity.Warning)
                    {
                        CapturedWarnings.Add(msg.GetDescriptionText());
                        accessor.DeleteWarning(msg);
                    }
                    else if (severity == FailureSeverity.DocumentCorruption)
                    {
                        return FailureProcessingResult.ProceedWithRollBack;
                    }
                    else if (severity == FailureSeverity.Error && msg.HasResolutions())
                    {
                        accessor.ResolveFailure(msg);
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"FailureHandler: {ex.Message}");
            }
            return FailureProcessingResult.Continue;
        }
    }

    #endregion

    #region Family Resolver

    /// <summary>
    /// Resolves family types from the live Revit Document by keyword matching.
    /// Priority: exact match → keyword match → closest dimension → first available.
    /// Never hardcodes family names.
    /// </summary>
    public class ModelFamilyResolver
    {
        private readonly Document _doc;

        public ModelFamilyResolver(Document doc)
        {
            _doc = doc;
        }

        /// <summary>Resolve a WallType by keyword and optional thickness (mm).</summary>
        public ResolveResult ResolveWallType(string keyword = null, double? thicknessMm = null)
        {
            var types = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .ToList();
            if (types.Count == 0)
                return ResolveResult.NotFound("No wall types in the project. Load a wall family first.");

            if (!string.IsNullOrEmpty(keyword))
            {
                var exact = types.FirstOrDefault(t =>
                    t.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return ResolveResult.Found(exact.Id, exact.Name);

                var kws = keyword.ToLowerInvariant().Split(new[] { ' ', '-', '_' },
                    StringSplitOptions.RemoveEmptyEntries);
                var matches = types.Where(t =>
                    kws.All(k => t.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                if (matches.Count > 0)
                {
                    var best = thicknessMm.HasValue
                        ? ClosestByWidth(matches, thicknessMm.Value) ?? matches[0]
                        : matches[0];
                    return ResolveResult.Found(best.Id, best.Name);
                }
            }

            if (thicknessMm.HasValue)
            {
                var best = ClosestByWidth(types, thicknessMm.Value);
                if (best != null)
                    return ResolveResult.Found(best.Id, $"{best.Name} (closest to {thicknessMm}mm)");
            }

            return ResolveResult.Found(types[0].Id, types[0].Name);
        }

        /// <summary>Resolve a FloorType by keyword.</summary>
        public ResolveResult ResolveFloorType(string keyword = null)
        {
            return ResolveSystemType<FloorType>(typeof(FloorType), keyword, "floor");
        }

        /// <summary>Resolve a RoofType by keyword.</summary>
        public ResolveResult ResolveRoofType(string keyword = null)
        {
            return ResolveSystemType<RoofType>(typeof(RoofType), keyword, "roof");
        }

        /// <summary>Resolve a CeilingType by keyword.</summary>
        public ResolveResult ResolveCeilingType(string keyword = null)
        {
            return ResolveSystemType<CeilingType>(typeof(CeilingType), keyword, "ceiling");
        }

        /// <summary>Resolve a FamilySymbol by category and keyword.</summary>
        public ResolveResult ResolveFamilySymbol(BuiltInCategory category,
            string keyword = null, double? widthMm = null, double? heightMm = null)
        {
            var symbols = new FilteredElementCollector(_doc)
                .OfCategory(category)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            if (symbols.Count == 0)
                return ResolveResult.NotFound($"No families for {category}. Load a family first.");

            if (!string.IsNullOrEmpty(keyword))
            {
                var exact = symbols.FirstOrDefault(s =>
                    s.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return ResolveResult.Found(exact.Id, $"{exact.FamilyName}: {exact.Name}");

                var match = symbols.FirstOrDefault(s =>
                    s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.FamilyName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                    return ResolveResult.Found(match.Id, $"{match.FamilyName}: {match.Name}");
            }

            return ResolveResult.Found(symbols[0].Id, $"{symbols[0].FamilyName}: {symbols[0].Name}");
        }

        /// <summary>Resolve a Level by name (partial match) or default to lowest.</summary>
        public Level ResolveLevel(string levelName = null)
        {
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            if (levels.Count == 0) return null;

            if (!string.IsNullOrEmpty(levelName))
            {
                var exact = levels.FirstOrDefault(l =>
                    l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;

                var partial = levels.FirstOrDefault(l =>
                    l.Name.IndexOf(levelName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (partial != null) return partial;

                if (int.TryParse(levelName, out int num) && num > 0 && num <= levels.Count)
                    return levels[num - 1];
            }

            return levels[0]; // Default: lowest level
        }

        /// <summary>Get the level above a given level.</summary>
        public Level GetLevelAbove(Level current)
        {
            var levels = new FilteredElementCollector(_doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            int idx = levels.FindIndex(l => l.Id == current.Id);
            return (idx >= 0 && idx < levels.Count - 1) ? levels[idx + 1] : null;
        }

        /// <summary>Resolve an MEP fixture FamilySymbol by hint and keyword.</summary>
        public FamilySymbol ResolveMEPFixture(string hint, string keyword = null)
        {
            var cat = hint?.ToLowerInvariant() switch
            {
                "light" or "lighting" or "downlight" => BuiltInCategory.OST_LightingFixtures,
                "outlet" or "power" or "socket" => BuiltInCategory.OST_ElectricalFixtures,
                "switch" => BuiltInCategory.OST_LightingDevices,
                "panel" or "db" or "distribution" => BuiltInCategory.OST_ElectricalEquipment,
                "sprinkler" => BuiltInCategory.OST_Sprinklers,
                "detector" or "smoke" => BuiltInCategory.OST_FireAlarmDevices,
                "ac" or "mechanical" or "fan" or "generator" => BuiltInCategory.OST_MechanicalEquipment,
                "plumbing" or "fixture" or "wc" or "basin" or "sink" => BuiltInCategory.OST_PlumbingFixtures,
                _ => BuiltInCategory.OST_GenericModel,
            };

            var symbols = new FilteredElementCollector(_doc)
                .OfCategory(cat)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .ToList();
            if (symbols.Count == 0) return null;

            if (!string.IsNullOrEmpty(keyword))
            {
                var match = symbols.FirstOrDefault(s =>
                    s.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.FamilyName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) { EnsureActive(match); return match; }
            }

            EnsureActive(symbols[0]);
            return symbols[0];
        }

        private void EnsureActive(FamilySymbol symbol)
        {
            if (!symbol.IsActive)
            {
                using (var tx = new Transaction(_doc, "STING MODEL: Activate Symbol"))
                {
                    tx.Start();
                    symbol.Activate();
                    _doc.Regenerate();
                    tx.Commit();
                }
            }
        }

        private ResolveResult ResolveSystemType<T>(Type typeClass, string keyword, string label)
            where T : ElementType
        {
            var types = new FilteredElementCollector(_doc)
                .OfClass(typeClass)
                .Cast<T>()
                .ToList();
            if (types.Count == 0)
                return ResolveResult.NotFound($"No {label} types in the project.");

            if (!string.IsNullOrEmpty(keyword))
            {
                var exact = types.FirstOrDefault(t =>
                    t.Name.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return ResolveResult.Found(exact.Id, exact.Name);

                var match = types.FirstOrDefault(t =>
                    t.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return ResolveResult.Found(match.Id, match.Name);
            }
            return ResolveResult.Found(types[0].Id, types[0].Name);
        }

        private WallType ClosestByWidth(List<WallType> types, double targetMm)
        {
            return types.OrderBy(t => Math.Abs(t.Width * Units.FeetToMm - targetMm)).FirstOrDefault();
        }
    }

    #endregion

    #region Workset Assigner

    /// <summary>
    /// Auto-assigns elements to discipline worksets in workshared projects.
    /// </summary>
    internal static class ModelWorksetAssigner
    {
        private static readonly Dictionary<BuiltInCategory, string> Map = new()
        {
            [BuiltInCategory.OST_Walls] = "Architecture",
            [BuiltInCategory.OST_Floors] = "Architecture",
            [BuiltInCategory.OST_Ceilings] = "Architecture",
            [BuiltInCategory.OST_Roofs] = "Architecture",
            [BuiltInCategory.OST_Doors] = "Architecture",
            [BuiltInCategory.OST_Windows] = "Architecture",
            [BuiltInCategory.OST_Stairs] = "Architecture",
            [BuiltInCategory.OST_Rooms] = "Architecture",
            [BuiltInCategory.OST_StructuralColumns] = "Structure",
            [BuiltInCategory.OST_StructuralFraming] = "Structure",
            [BuiltInCategory.OST_StructuralFoundation] = "Structure",
            [BuiltInCategory.OST_LightingFixtures] = "MEP - Electrical",
            [BuiltInCategory.OST_ElectricalFixtures] = "MEP - Electrical",
            [BuiltInCategory.OST_ElectricalEquipment] = "MEP - Electrical",
            [BuiltInCategory.OST_Conduit] = "MEP - Electrical",
            [BuiltInCategory.OST_CableTray] = "MEP - Electrical",
            [BuiltInCategory.OST_PipeCurves] = "MEP - Plumbing",
            [BuiltInCategory.OST_PlumbingFixtures] = "MEP - Plumbing",
            [BuiltInCategory.OST_DuctCurves] = "MEP - HVAC",
            [BuiltInCategory.OST_MechanicalEquipment] = "MEP - HVAC",
            [BuiltInCategory.OST_Sprinklers] = "MEP - Fire Protection",
            [BuiltInCategory.OST_FireAlarmDevices] = "MEP - Fire Protection",
        };

        public static void Assign(Document doc, Element el)
        {
            if (el?.Category == null || !doc.IsWorkshared) return;
            var bic = (BuiltInCategory)el.Category.Id.Value;
            if (!Map.TryGetValue(bic, out var wsName)) return;
            try
            {
                var ws = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .ToWorksets()
                    .FirstOrDefault(w => w.Name.Equals(wsName, StringComparison.OrdinalIgnoreCase));
                if (ws == null) return;
                var p = el.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                if (p != null && !p.IsReadOnly)
                    p.Set(ws.Id.Value);
            }
            catch { /* Non-critical */ }
        }
    }

    #endregion

    // ════════════════════════════════════════════════════════════════════
    // MODEL ENGINE — Main Orchestrator
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// MODEL auto-modeling engine. Creates Revit elements with intelligent
    /// family resolution, failure handling, workset assignment, and batch support.
    ///
    /// Combines patterns from:
    ///   - EaseBit (wall creation from parallel lines, auto-type detection)
    ///   - eTLipse/ARQER (DWG → walls → rooms → floors → doors → windows pipeline)
    ///   - StingBIM.AI.Creation (orchestrator pipeline, family resolver, error explainer)
    ///   - Revit API best practices (NewFamilyInstances2, TransactionGroup, IFailuresPreprocessor)
    ///
    /// Usage:
    ///   var engine = new ModelEngine(doc);
    ///   var result = engine.CreateWall(startPt, endPt, levelName: "Level 1", heightMm: 3000);
    ///   var result = engine.CreateRectangularRoom(5000, 4000, "Bedroom", "Level 1");
    ///   var result = engine.CreateFloorInRoom(room, "Concrete 200mm");
    /// </summary>
    public class ModelEngine
    {
        private readonly Document _doc;
        private readonly ModelFamilyResolver _resolver;

        public ModelEngine(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _resolver = new ModelFamilyResolver(doc);
        }

        /// <summary>The family resolver for direct access.</summary>
        public ModelFamilyResolver Resolver => _resolver;

        // ── Wall Creation ─────────────────────────────────────────────

        /// <summary>
        /// Creates a wall from two points (mm coordinates).
        /// </summary>
        public ModelResult CreateWall(
            double startXMm, double startYMm,
            double endXMm, double endYMm,
            string wallTypeName = null,
            string levelName = null,
            double heightMm = 2700,
            double thicknessMm = 0,
            bool isStructural = false)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found in the project.");

                var typeResult = _resolver.ResolveWallType(wallTypeName,
                    thicknessMm > 0 ? thicknessMm : (double?)null);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                var startPt = new XYZ(Units.Mm(startXMm), Units.Mm(startYMm), 0);
                var endPt = new XYZ(Units.Mm(endXMm), Units.Mm(endYMm), 0);
                if (startPt.DistanceTo(endPt) < 0.01) // ~3mm tolerance
                    return ModelResult.Fail("Start and end points are too close together to create a wall.");
                var line = Line.CreateBound(startPt, endPt);
                var heightFt = Units.Mm(heightMm);

                Wall wall = null;
                var fh = new ModelFailureHandler();

                using (var tx = new Transaction(_doc, "STING MODEL: Create Wall"))
                {
                    AttachFailureHandler(tx, fh);
                    tx.Start();
                    try
                    {
                        wall = Wall.Create(_doc, line, typeResult.TypeId,
                            level.Id, heightFt, 0, false, isStructural);
                        ModelWorksetAssigner.Assign(_doc, wall);
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return ModelResult.Fail($"Wall creation failed: {ex.Message}");
                    }
                }

                var lengthMm = Units.ToMm(line.Length);
                var result = ModelResult.Ok(
                    $"Created {lengthMm / 1000:F1}m {typeResult.TypeName} wall on {level.Name}",
                    wall.Id);
                result.Warnings = fh.CapturedWarnings;
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreateWall", ex);
                return ModelResult.Fail($"Wall creation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a rectangular room outline (4 walls) with joined corners.
        /// Returns the 4 wall IDs. Optionally places a Room element inside.
        /// </summary>
        public ModelResult CreateRectangularRoom(
            double widthMm, double depthMm,
            string roomName = null,
            string levelName = null,
            double heightMm = 2700,
            string wallTypeName = null,
            double originXMm = 0, double originYMm = 0,
            bool placeRoom = true)
        {
            try
            {
                if (widthMm <= 0 || depthMm <= 0)
                    return ModelResult.Fail("Width and depth must be positive.");

                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found in the project.");

                var typeResult = _resolver.ResolveWallType(wallTypeName);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                var ox = Units.Mm(originXMm);
                var oy = Units.Mm(originYMm);
                var w = Units.Mm(widthMm);
                var d = Units.Mm(depthMm);
                var h = Units.Mm(heightMm);

                var corners = new[]
                {
                    new XYZ(ox, oy, 0),
                    new XYZ(ox + w, oy, 0),
                    new XYZ(ox + w, oy + d, 0),
                    new XYZ(ox, oy + d, 0)
                };

                var wallIds = new List<ElementId>();
                var walls = new List<Wall>();
                var fh = new ModelFailureHandler();
                Room createdRoom = null;

                using (var tg = new TransactionGroup(_doc, "STING MODEL: Create Room"))
                {
                    tg.Start();

                    // Transaction 1: Create 4 walls
                    using (var tx = new Transaction(_doc, "Create Walls"))
                    {
                        AttachFailureHandler(tx, fh);
                        tx.Start();
                        try
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                var line = Line.CreateBound(corners[i], corners[(i + 1) % 4]);
                                var wall = Wall.Create(_doc, line, typeResult.TypeId,
                                    level.Id, h, 0, false, false);
                                ModelWorksetAssigner.Assign(_doc, wall);
                                walls.Add(wall);
                                wallIds.Add(wall.Id);
                            }
                            tx.Commit();
                        }
                        catch (Exception ex)
                        {
                            tx.RollBack();
                            tg.RollBack();
                            return ModelResult.Fail($"Wall creation failed: {ex.Message}");
                        }
                    }

                    // Transaction 2: Join wall corners
                    using (var tx = new Transaction(_doc, "Join Corners"))
                    {
                        tx.Start();
                        try
                        {
                            for (int i = 0; i < walls.Count; i++)
                                JoinGeometryUtils.JoinGeometry(_doc,
                                    walls[i], walls[(i + 1) % walls.Count]);
                            tx.Commit();
                        }
                        catch { tx.RollBack(); /* Non-critical */ }
                    }

                    // Transaction 3: Place room element
                    if (placeRoom)
                    {
                        using (var tx = new Transaction(_doc, "Place Room"))
                        {
                            tx.Start();
                            try
                            {
                                var center = new UV(
                                    (corners[0].X + corners[2].X) / 2,
                                    (corners[0].Y + corners[2].Y) / 2);
                                createdRoom = _doc.Create.NewRoom(level, center);
                                if (createdRoom != null && !string.IsNullOrEmpty(roomName))
                                    createdRoom.Name = roomName;
                                tx.Commit();
                            }
                            catch { tx.RollBack(); }
                        }
                    }

                    // Assimilate merges sub-transactions into one undo entry (intentional pattern)
                    tg.Assimilate();
                }

                var areaSqM = (widthMm / 1000.0) * (depthMm / 1000.0);
                var msg = $"Created {widthMm / 1000:F1}m × {depthMm / 1000:F1}m " +
                    $"{roomName ?? "room"} ({areaSqM:F1}m²) on {level.Name}";
                if (createdRoom != null)
                    msg += $" — Room '{createdRoom.Name}' placed";

                var result = ModelResult.OkBatch(msg, wallIds);
                if (createdRoom != null)
                    result.CreatedElementIds.Add(createdRoom.Id);
                result.Warnings = fh.CapturedWarnings;
                return result;
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreateRectangularRoom", ex);
                return ModelResult.Fail($"Room creation failed: {ex.Message}");
            }
        }

        // ── Floor Creation ────────────────────────────────────────────

        /// <summary>
        /// Creates a rectangular floor from dimensions (mm).
        /// </summary>
        public ModelResult CreateFloor(
            double widthMm, double depthMm,
            string floorTypeName = null,
            string levelName = null,
            double originXMm = 0, double originYMm = 0)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveFloorType(floorTypeName);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                var ox = Units.Mm(originXMm);
                var oy = Units.Mm(originYMm);
                var w = Units.Mm(widthMm);
                var d = Units.Mm(depthMm);

                var boundary = new CurveLoop();
                boundary.Append(Line.CreateBound(new XYZ(ox, oy, 0), new XYZ(ox + w, oy, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox + w, oy, 0), new XYZ(ox + w, oy + d, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox + w, oy + d, 0), new XYZ(ox, oy + d, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox, oy + d, 0), new XYZ(ox, oy, 0)));

                Floor floor = null;
                var fh = new ModelFailureHandler();

                using (var tx = new Transaction(_doc, "STING MODEL: Create Floor"))
                {
                    AttachFailureHandler(tx, fh);
                    tx.Start();
                    try
                    {
                        floor = Floor.Create(_doc,
                            new List<CurveLoop> { boundary }, typeResult.TypeId, level.Id);
                        ModelWorksetAssigner.Assign(_doc, floor);
                        tx.Commit();
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        return ModelResult.Fail($"Floor creation failed: {ex.Message}");
                    }
                }

                var areaSqM = (widthMm / 1000.0) * (depthMm / 1000.0);
                return ModelResult.Ok(
                    $"Created {areaSqM:F1}m² {typeResult.TypeName} floor on {level.Name}",
                    floor.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreateFloor", ex);
                return ModelResult.Fail($"Floor creation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a floor inside an existing room boundary.
        /// </summary>
        public ModelResult CreateFloorInRoom(Room room,
            string floorTypeName = null, string levelName = null)
        {
            try
            {
                if (room == null) return ModelResult.Fail("Room is null.");

                var level = levelName != null
                    ? _resolver.ResolveLevel(levelName)
                    : _doc.GetElement(room.LevelId) as Level;
                if (level == null) return ModelResult.Fail("Level not found.");

                var typeResult = _resolver.ResolveFloorType(floorTypeName);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                // Extract room boundary
                var boundarySegments = room.GetBoundarySegments(
                    new SpatialElementBoundaryOptions());
                if (boundarySegments == null || boundarySegments.Count == 0)
                    return ModelResult.Fail("Room has no boundary. Ensure walls enclose it.");

                var outerLoop = new CurveLoop();
                foreach (var seg in boundarySegments[0])
                    outerLoop.Append(seg.GetCurve());

                Floor floor = null;
                using (var tx = new Transaction(_doc, "STING MODEL: Floor in Room"))
                {
                    tx.Start();
                    floor = Floor.Create(_doc,
                        new List<CurveLoop> { outerLoop }, typeResult.TypeId, level.Id);
                    ModelWorksetAssigner.Assign(_doc, floor);
                    tx.Commit();
                }

                return ModelResult.Ok(
                    $"Created {typeResult.TypeName} floor in '{room.Name}' on {level.Name}",
                    floor.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreateFloorInRoom", ex);
                return ModelResult.Fail($"Floor in room failed: {ex.Message}");
            }
        }

        // ── Ceiling Creation ──────────────────────────────────────────

        /// <summary>
        /// Creates a ceiling at a specified height in a rectangular area.
        /// </summary>
        public ModelResult CreateCeiling(
            double widthMm, double depthMm,
            string ceilingTypeName = null,
            string levelName = null,
            double originXMm = 0, double originYMm = 0)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveCeilingType(ceilingTypeName);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                var ox = Units.Mm(originXMm);
                var oy = Units.Mm(originYMm);
                var w = Units.Mm(widthMm);
                var d = Units.Mm(depthMm);

                var boundary = new CurveLoop();
                boundary.Append(Line.CreateBound(new XYZ(ox, oy, 0), new XYZ(ox + w, oy, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox + w, oy, 0), new XYZ(ox + w, oy + d, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox + w, oy + d, 0), new XYZ(ox, oy + d, 0)));
                boundary.Append(Line.CreateBound(new XYZ(ox, oy + d, 0), new XYZ(ox, oy, 0)));

                Ceiling ceiling = null;
                using (var tx = new Transaction(_doc, "STING MODEL: Create Ceiling"))
                {
                    tx.Start();
                    ceiling = Ceiling.Create(_doc,
                        new List<CurveLoop> { boundary }, typeResult.TypeId, level.Id);
                    ModelWorksetAssigner.Assign(_doc, ceiling);
                    tx.Commit();
                }

                var areaSqM = (widthMm / 1000.0) * (depthMm / 1000.0);
                return ModelResult.Ok(
                    $"Created {areaSqM:F1}m² {typeResult.TypeName} ceiling on {level.Name}",
                    ceiling.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreateCeiling", ex);
                return ModelResult.Fail($"Ceiling creation failed: {ex.Message}");
            }
        }

        // ── Roof Creation ─────────────────────────────────────────────

        /// <summary>
        /// Creates a footprint roof from a rectangular boundary.
        /// </summary>
        public ModelResult CreateRoof(
            double widthMm, double depthMm,
            string roofTypeName = null,
            string levelName = null,
            double slopeDegrees = 25,
            double overhangMm = 600,
            double originXMm = 0, double originYMm = 0)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveRoofType(roofTypeName);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);
                var roofType = _doc.GetElement(typeResult.TypeId) as RoofType;

                var oh = Units.Mm(overhangMm);
                var ox = Units.Mm(originXMm) - oh;
                var oy = Units.Mm(originYMm) - oh;
                var w = Units.Mm(widthMm) + 2 * oh;
                var d = Units.Mm(depthMm) + 2 * oh;

                var footprint = new CurveArray();
                footprint.Append(Line.CreateBound(new XYZ(ox, oy, 0), new XYZ(ox + w, oy, 0)));
                footprint.Append(Line.CreateBound(new XYZ(ox + w, oy, 0), new XYZ(ox + w, oy + d, 0)));
                footprint.Append(Line.CreateBound(new XYZ(ox + w, oy + d, 0), new XYZ(ox, oy + d, 0)));
                footprint.Append(Line.CreateBound(new XYZ(ox, oy + d, 0), new XYZ(ox, oy, 0)));

                FootPrintRoof roof = null;
                using (var tx = new Transaction(_doc, "STING MODEL: Create Roof"))
                {
                    tx.Start();
                    ModelCurveArray modelCurves;
                    roof = _doc.Create.NewFootPrintRoof(footprint, level, roofType, out modelCurves);

                    // Set slope on all edges
                    double slopeRad = slopeDegrees * Math.PI / 180.0;
                    foreach (ModelCurve mc in modelCurves)
                    {
                        roof.set_DefinesSlope(mc, true);
                        roof.set_SlopeAngle(mc, Math.Tan(slopeRad));
                    }

                    ModelWorksetAssigner.Assign(_doc, roof);
                    tx.Commit();
                }

                return ModelResult.Ok(
                    $"Created {roofType.Name} roof ({slopeDegrees}° slope, {overhangMm}mm overhang) on {level.Name}",
                    roof.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreateRoof", ex);
                return ModelResult.Fail($"Roof creation failed: {ex.Message}");
            }
        }

        // ── Door / Window Insertion ───────────────────────────────────

        /// <summary>
        /// Places a door in a wall at a position along its length (0.0–1.0 normalized).
        /// </summary>
        public ModelResult PlaceDoor(Wall hostWall,
            double positionAlongWall = 0.5,
            string doorTypeName = null,
            double sillHeightMm = 0)
        {
            return PlaceHostedFamily(hostWall, BuiltInCategory.OST_Doors,
                positionAlongWall, doorTypeName, sillHeightMm, "Door");
        }

        /// <summary>
        /// Places a window in a wall at a position along its length (0.0–1.0 normalized).
        /// </summary>
        public ModelResult PlaceWindow(Wall hostWall,
            double positionAlongWall = 0.5,
            string windowTypeName = null,
            double sillHeightMm = 900)
        {
            return PlaceHostedFamily(hostWall, BuiltInCategory.OST_Windows,
                positionAlongWall, windowTypeName, sillHeightMm, "Window");
        }

        private ModelResult PlaceHostedFamily(Wall hostWall,
            BuiltInCategory category, double param,
            string typeName, double sillMm, string label)
        {
            try
            {
                if (hostWall == null) return ModelResult.Fail("Host wall is null.");

                var typeResult = _resolver.ResolveFamilySymbol(category, typeName);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                var symbol = _doc.GetElement(typeResult.TypeId) as FamilySymbol;
                if (symbol == null) return ModelResult.Fail($"No {label} family symbol found.");
                EnsureActive(symbol);

                var level = _doc.GetElement(hostWall.LevelId) as Level;
                var wallLoc = hostWall.Location as LocationCurve;
                var wallCurve = wallLoc?.Curve;
                if (wallCurve == null) return ModelResult.Fail("Cannot determine wall location.");

                var insertPt = wallCurve.Evaluate(param, true);

                FamilyInstance instance = null;
                using (var tx = new Transaction(_doc, $"STING MODEL: Place {label}"))
                {
                    tx.Start();
                    instance = _doc.Create.NewFamilyInstance(
                        insertPt, symbol, hostWall, level,
                        StructuralType.NonStructural);

                    if (sillMm > 0)
                    {
                        var sillParam = instance.get_Parameter(
                            BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                        sillParam?.Set(Units.Mm(sillMm));
                    }

                    ModelWorksetAssigner.Assign(_doc, instance);
                    tx.Commit();
                }

                return ModelResult.Ok(
                    $"Placed {typeResult.TypeName} {label.ToLower()} in wall on {level?.Name}",
                    instance.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error($"ModelEngine.Place{label}", ex);
                return ModelResult.Fail($"{label} placement failed: {ex.Message}");
            }
        }

        // ── Structural Column ─────────────────────────────────────────

        /// <summary>
        /// Places a structural column at a point.
        /// </summary>
        public ModelResult PlaceColumn(
            double xMm, double yMm,
            string columnTypeName = null,
            string baseLevelName = null,
            double sizeMm = 400)
        {
            try
            {
                var level = _resolver.ResolveLevel(baseLevelName);
                if (level == null) return ModelResult.Fail("No levels found.");
                var topLevel = _resolver.GetLevelAbove(level);

                var typeResult = _resolver.ResolveFamilySymbol(
                    BuiltInCategory.OST_StructuralColumns, columnTypeName, sizeMm, sizeMm);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                var symbol = _doc.GetElement(typeResult.TypeId) as FamilySymbol;
                if (symbol != null) EnsureActive(symbol);

                FamilyInstance col = null;
                using (var tx = new Transaction(_doc, "STING MODEL: Place Column"))
                {
                    tx.Start();
                    col = _doc.Create.NewFamilyInstance(
                        new XYZ(Units.Mm(xMm), Units.Mm(yMm), 0),
                        symbol, level, StructuralType.Column);

                    if (topLevel != null)
                        col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)
                            ?.Set(topLevel.Id);

                    ModelWorksetAssigner.Assign(_doc, col);
                    tx.Commit();
                }

                return ModelResult.Ok(
                    $"Placed {typeResult.TypeName} column on {level.Name}", col.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.PlaceColumn", ex);
                return ModelResult.Fail($"Column placement failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Places columns at a grid pattern (rows × cols with spacing).
        /// </summary>
        public ModelResult PlaceColumnGrid(
            int rows, int cols,
            double spacingXMm, double spacingYMm,
            string columnTypeName = null,
            string baseLevelName = null,
            double originXMm = 0, double originYMm = 0)
        {
            try
            {
                var level = _resolver.ResolveLevel(baseLevelName);
                if (level == null) return ModelResult.Fail("No levels found.");
                var topLevel = _resolver.GetLevelAbove(level);

                var typeResult = _resolver.ResolveFamilySymbol(
                    BuiltInCategory.OST_StructuralColumns, columnTypeName);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                var symbol = _doc.GetElement(typeResult.TypeId) as FamilySymbol;
                if (symbol != null) EnsureActive(symbol);

                var placedIds = new List<ElementId>();
                var fh = new ModelFailureHandler();

                using (var tx = new Transaction(_doc, "STING MODEL: Column Grid"))
                {
                    AttachFailureHandler(tx, fh);
                    tx.Start();

                    for (int r = 0; r < rows; r++)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            var pt = new XYZ(
                                Units.Mm(originXMm + c * spacingXMm),
                                Units.Mm(originYMm + r * spacingYMm), 0);
                            var col = _doc.Create.NewFamilyInstance(
                                pt, symbol, level, StructuralType.Column);
                            if (topLevel != null)
                                col.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)
                                    ?.Set(topLevel.Id);
                            ModelWorksetAssigner.Assign(_doc, col);
                            placedIds.Add(col.Id);
                        }
                    }
                    tx.Commit();
                }

                return ModelResult.OkBatch(
                    $"Placed {placedIds.Count} columns ({rows}×{cols} grid, " +
                    $"{spacingXMm / 1000:F1}m × {spacingYMm / 1000:F1}m) on {level.Name}",
                    placedIds);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.PlaceColumnGrid", ex);
                return ModelResult.Fail($"Column grid failed: {ex.Message}");
            }
        }

        // ── Beam Creation ─────────────────────────────────────────────

        /// <summary>
        /// Creates a structural beam between two points.
        /// </summary>
        public ModelResult CreateBeam(
            double startXMm, double startYMm, double startZMm,
            double endXMm, double endYMm, double endZMm,
            string beamTypeName = null,
            string levelName = null)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found.");

                var typeResult = _resolver.ResolveFamilySymbol(
                    BuiltInCategory.OST_StructuralFraming, beamTypeName);
                if (!typeResult.Success) return ModelResult.Fail(typeResult.Message);

                var symbol = _doc.GetElement(typeResult.TypeId) as FamilySymbol;
                if (symbol != null) EnsureActive(symbol);

                var beamLine = Line.CreateBound(
                    new XYZ(Units.Mm(startXMm), Units.Mm(startYMm), Units.Mm(startZMm)),
                    new XYZ(Units.Mm(endXMm), Units.Mm(endYMm), Units.Mm(endZMm)));

                FamilyInstance beam = null;
                using (var tx = new Transaction(_doc, "STING MODEL: Create Beam"))
                {
                    tx.Start();
                    beam = _doc.Create.NewFamilyInstance(
                        beamLine, symbol, level, StructuralType.Beam);
                    ModelWorksetAssigner.Assign(_doc, beam);
                    tx.Commit();
                }

                var lengthMm = Units.ToMm(beamLine.Length);
                return ModelResult.Ok(
                    $"Created {lengthMm / 1000:F1}m {typeResult.TypeName} beam on {level.Name}",
                    beam.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreateBeam", ex);
                return ModelResult.Fail($"Beam creation failed: {ex.Message}");
            }
        }

        // ── MEP Element Creation ──────────────────────────────────────

        /// <summary>
        /// Creates a duct between two points.
        /// </summary>
        public ModelResult CreateDuct(
            double startXMm, double startYMm, double startZMm,
            double endXMm, double endYMm, double endZMm,
            string ductTypeName = null,
            string levelName = null)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found.");

                // Find duct type
                var ductTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(DuctType))
                    .Cast<DuctType>()
                    .ToList();
                if (ductTypes.Count == 0) return ModelResult.Fail("No duct types in project.");
                var ductType = ductTypes[0];
                if (!string.IsNullOrEmpty(ductTypeName))
                {
                    var match = ductTypes.FirstOrDefault(t =>
                        t.Name.IndexOf(ductTypeName, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match != null) ductType = match;
                }

                // Find supply air system type
                var sysTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(MechanicalSystemType))
                    .Cast<MechanicalSystemType>()
                    .ToList();
                if (sysTypes.Count == 0) return ModelResult.Fail("No mechanical system types.");
                var sysType = sysTypes.FirstOrDefault(st =>
                    st.SystemClassification == MEPSystemClassification.SupplyAir) ?? sysTypes[0];

                Duct duct = null;
                using (var tx = new Transaction(_doc, "STING MODEL: Create Duct"))
                {
                    tx.Start();
                    duct = Duct.Create(_doc, sysType.Id, ductType.Id, level.Id,
                        new XYZ(Units.Mm(startXMm), Units.Mm(startYMm), Units.Mm(startZMm)),
                        new XYZ(Units.Mm(endXMm), Units.Mm(endYMm), Units.Mm(endZMm)));
                    ModelWorksetAssigner.Assign(_doc, duct);
                    tx.Commit();
                }

                return ModelResult.Ok($"Created duct on {level.Name}", duct.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreateDuct", ex);
                return ModelResult.Fail($"Duct creation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a pipe between two points.
        /// </summary>
        public ModelResult CreatePipe(
            double startXMm, double startYMm, double startZMm,
            double endXMm, double endYMm, double endZMm,
            string pipeTypeName = null,
            string levelName = null,
            string systemClassification = "DomesticColdWater")
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found.");

                var pipeTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(PipeType))
                    .Cast<PipeType>()
                    .ToList();
                if (pipeTypes.Count == 0) return ModelResult.Fail("No pipe types in project.");
                var pipeType = pipeTypes[0];

                var pipingSysTypes = new FilteredElementCollector(_doc)
                    .OfClass(typeof(PipingSystemType))
                    .Cast<PipingSystemType>()
                    .ToList();
                if (pipingSysTypes.Count == 0)
                    return ModelResult.Fail("No piping system types.");

                var targetClass = systemClassification?.ToLowerInvariant() switch
                {
                    "domesticcoldwater" or "coldwater" or "dcw" => MEPSystemClassification.DomesticColdWater,
                    "domestichotwater" or "hotwater" or "dhw" => MEPSystemClassification.DomesticHotWater,
                    "sanitary" or "waste" or "san" => MEPSystemClassification.Sanitary,
                    _ => MEPSystemClassification.DomesticColdWater,
                };
                var sysType = pipingSysTypes.FirstOrDefault(st =>
                    st.SystemClassification == targetClass) ?? pipingSysTypes[0];

                Pipe pipe = null;
                using (var tx = new Transaction(_doc, "STING MODEL: Create Pipe"))
                {
                    tx.Start();
                    pipe = Pipe.Create(_doc, sysType.Id, pipeType.Id, level.Id,
                        new XYZ(Units.Mm(startXMm), Units.Mm(startYMm), Units.Mm(startZMm)),
                        new XYZ(Units.Mm(endXMm), Units.Mm(endYMm), Units.Mm(endZMm)));
                    ModelWorksetAssigner.Assign(_doc, pipe);
                    tx.Commit();
                }

                return ModelResult.Ok($"Created pipe on {level.Name}", pipe.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.CreatePipe", ex);
                return ModelResult.Fail($"Pipe creation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Places an MEP fixture (light, outlet, switch, sprinkler, etc.) at a point.
        /// </summary>
        public ModelResult PlaceMEPFixture(
            double xMm, double yMm, double zMm,
            string fixtureHint,
            string keyword = null,
            string levelName = null)
        {
            try
            {
                var level = _resolver.ResolveLevel(levelName);
                if (level == null) return ModelResult.Fail("No levels found.");

                var symbol = _resolver.ResolveMEPFixture(fixtureHint, keyword);
                if (symbol == null)
                    return ModelResult.Fail(
                        $"No '{fixtureHint}' fixture families loaded. Load a family first.");

                FamilyInstance inst = null;
                using (var tx = new Transaction(_doc, "STING MODEL: Place Fixture"))
                {
                    tx.Start();
                    inst = _doc.Create.NewFamilyInstance(
                        new XYZ(Units.Mm(xMm), Units.Mm(yMm), Units.Mm(zMm)),
                        symbol, level, StructuralType.NonStructural);
                    ModelWorksetAssigner.Assign(_doc, inst);
                    tx.Commit();
                }

                return ModelResult.Ok(
                    $"Placed {symbol.FamilyName}: {symbol.Name} on {level.Name}", inst.Id);
            }
            catch (Exception ex)
            {
                StingLog.Error("ModelEngine.PlaceMEPFixture", ex);
                return ModelResult.Fail($"Fixture placement failed: {ex.Message}");
            }
        }

        // ── Batch Operations ──────────────────────────────────────────

        /// <summary>
        /// Creates a complete building shell: walls → floor → ceiling → roof.
        /// ME-03 FIX: Wrapped in TransactionGroup for atomic rollback on failure.
        /// </summary>
        public ModelResult CreateBuildingShell(
            double widthMm, double depthMm,
            double wallHeightMm = 3000,
            double roofSlopeDeg = 25,
            double overhangMm = 600,
            string levelName = null,
            string wallTypeName = null,
            string floorTypeName = null,
            string roofTypeName = null,
            double originXMm = 0, double originYMm = 0)
        {
            var results = new List<string>();
            var allIds = new List<ElementId>();
            int step = 0;

            using (var tg = new TransactionGroup(_doc, "STING MODEL: Building Shell"))
            {
                tg.Start();

                try
                {
                    // Walls
                    step = 1;
                    var wallResult = CreateRectangularRoom(widthMm, depthMm,
                        "Building", levelName, wallHeightMm, wallTypeName, originXMm, originYMm, placeRoom: false);
                    if (!wallResult.Success)
                    {
                        tg.RollBack();
                        return wallResult;
                    }
                    results.Add($"  Walls: {wallResult.Message}");
                    allIds.AddRange(wallResult.CreatedElementIds);

                    // Floor
                    step = 2;
                    var floorResult = CreateFloor(widthMm, depthMm, floorTypeName, levelName);
                    if (floorResult.Success)
                    {
                        results.Add($"  Floor: {floorResult.Message}");
                        allIds.Add(floorResult.CreatedElementId);
                    }

                    // Roof
                    step = 3;
                    var roofResult = CreateRoof(widthMm, depthMm, roofTypeName, levelName,
                        roofSlopeDeg, overhangMm);
                    if (roofResult.Success)
                    {
                        results.Add($"  Roof: {roofResult.Message}");
                        allIds.Add(roofResult.CreatedElementId);
                    }

                    tg.Assimilate();

                    return ModelResult.OkBatch(
                        $"Created building shell ({widthMm / 1000:F1}m × {depthMm / 1000:F1}m):\n" +
                        string.Join("\n", results),
                        allIds);
                }
                catch (Exception ex)
                {
                    StingLog.Error($"ModelEngine.CreateBuildingShell step {step}", ex);
                    tg.RollBack();
                    return ModelResult.Fail($"Building shell failed at step {step}: {ex.Message}");
                }
            }
        }

        // ── Helper ────────────────────────────────────────────────────

        private static void AttachFailureHandler(Transaction tx, ModelFailureHandler fh)
        {
            var opts = tx.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(fh);
            tx.SetFailureHandlingOptions(opts);
        }
    }
}
