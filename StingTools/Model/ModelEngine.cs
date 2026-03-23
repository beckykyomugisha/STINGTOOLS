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

                // Phase 56 MA-004: Log warning when keyword doesn't match any type
                StingLog.Warn($"ResolveFamilySymbol: keyword '{keyword}' not found in {category}. " +
                    $"Using first available: '{symbols[0].FamilyName}: {symbols[0].Name}'");
            }

            return ResolveResult.Found(symbols[0].Id, $"{symbols[0].FamilyName}: {symbols[0].Name} (default)");
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

        public void EnsureActive(FamilySymbol symbol)
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
                {
                    try { p.Set((int)ws.Id.IntegerValue); }
                    catch (Exception ex) { StingLog.Warn($"WorksetAssign: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"Non-critical: {ex.Message}"); }
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
                        catch (Exception ex) { StingLog.Warn($"Rollback: {ex.Message}"); tx.RollBack(); }
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
                            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); tx.RollBack(); }
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
                _resolver.EnsureActive(symbol);

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
                if (symbol != null) _resolver.EnsureActive(symbol);

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
                if (symbol != null) _resolver.EnsureActive(symbol);

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
                if (symbol != null) _resolver.EnsureActive(symbol);

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
                    // R4-A FIX: Pass origin so floor aligns with walls when origin != 0
                    var floorResult = CreateFloor(widthMm, depthMm, floorTypeName, levelName, originXMm, originYMm);
                    if (floorResult.Success)
                    {
                        results.Add($"  Floor: {floorResult.Message}");
                        allIds.Add(floorResult.CreatedElementId);
                    }

                    // Roof
                    step = 3;
                    // R4-A FIX: Pass origin so roof aligns with walls when origin != 0
                    var roofResult = CreateRoof(widthMm, depthMm, roofTypeName, levelName,
                        roofSlopeDeg, overhangMm, originXMm, originYMm);
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

        // ══════════════════════════════════════════════════════════════
        // POST-CREATION AUTO-TAGGING PIPELINE
        // Phase 55: Created elements are auto-tagged via RunFullPipeline
        // so they immediately have ISO 19650 tags, containers, TAG7
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Auto-tag all created elements using the full STING tagging pipeline.
        /// Called after model creation to ensure zero-touch compliance.
        /// Runs in a separate transaction so model creation succeeds even if tagging fails.
        /// </summary>
        public static int AutoTagCreatedElements(Document doc, List<ElementId> createdIds)
        {
            if (doc == null || createdIds == null || createdIds.Count == 0) return 0;

            int tagged = 0;
            try
            {
                var ctx = TokenAutoPopulator.PopulationContext.Build(doc);
                if (ctx == null) { StingLog.Warn("AutoTagCreatedElements: PopulationContext.Build returned null"); return 0; }

                var (existingTags, seqCounters) = TagConfig.BuildTagIndexAndCounters(doc);
                var formulaPath = StingToolsApp.FindDataFile("FORMULAS_WITH_DEPENDENCIES.csv");
                var formulas = formulaPath != null ? Temp.FormulaEngine.LoadFormulas(formulaPath) : new List<Temp.FormulaEngine.FormulaDefinition>();
                var gridLines = new FilteredElementCollector(doc)
                    .OfClass(typeof(Grid)).Cast<Grid>().ToList();

                using (var tx = new Transaction(doc, "STING MODEL: Auto-Tag Created Elements"))
                {
                    tx.Start();
                    foreach (var id in createdIds)
                    {
                        var el = doc.GetElement(id);
                        if (el == null) continue;
                        try
                        {
                            bool ok = TagPipelineHelper.RunFullPipeline(
                                doc, el, ctx, existingTags, seqCounters,
                                formulas, gridLines,
                                overwrite: false, skipComplete: true,
                                collisionMode: TagCollisionMode.AutoIncrement);
                            if (ok) tagged++;
                        }
                        catch (Exception ex) { StingLog.Warn($"AutoTag element {id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }

                if (tagged > 0)
                {
                    TagConfig.SaveSeqSidecar(doc, seqCounters);
                    ComplianceScan.InvalidateCache();
                    StingAutoTagger.InvalidateContext();
                }

                // Phase 56: Post-creation model validation
                var validationIssues = WarningsEngine.ValidateModelElements(doc, createdIds);
                if (validationIssues.Count > 0)
                {
                    foreach (var issue in validationIssues.Take(5))
                        StingLog.Warn($"ModelValidation: {issue}");
                }
            }
            catch (Exception ex) { StingLog.Warn($"AutoTagCreatedElements: {ex.Message}"); }
            return tagged;
        }

        /// <summary>
        /// Auto-tag a single ModelResult's created elements.
        /// Returns the number of elements successfully tagged.
        /// </summary>
        public static int AutoTagResult(Document doc, ModelResult result)
        {
            if (result == null || !result.Success) return 0;
            var ids = new List<ElementId>();
            if (result.CreatedElementId != null && result.CreatedElementId != ElementId.InvalidElementId)
                ids.Add(result.CreatedElementId);
            ids.AddRange(result.CreatedElementIds.Where(id => id != null && id != ElementId.InvalidElementId));
            return AutoTagCreatedElements(doc, ids);
        }
    }

    // ════════════════════════════════════════════════════════════════
    // MEP ROUTING ENGINE — Auto-routing with clash avoidance
    // Phase 55: Fills critical MODEL tab gap for automated MEP layout
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// MEP auto-routing engine for duct and pipe layout.
    /// Provides optimal path finding between equipment with clash avoidance,
    /// automatic sizing per CIBSE Guide C, and branch/header connections.
    ///
    /// Standards: CIBSE Guide C, BS EN 12237, ASHRAE Fundamentals
    /// </summary>
    internal static class MEPRoutingEngine
    {
        /// <summary>Route segment between two points with sizing.</summary>
        public class RouteSegment
        {
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public double DiameterMm { get; set; }
            public double LengthMm { get; set; }
            public string SegmentType { get; set; } // "Straight", "Elbow", "Tee", "Reducer"
        }

        /// <summary>Route result with all segments and sizing data.</summary>
        public class RouteResult
        {
            public bool Success { get; set; }
            public List<RouteSegment> Segments { get; set; } = new();
            public double TotalLengthMm { get; set; }
            public int ElbowCount { get; set; }
            public double PressureDropPa { get; set; }
            public string Message { get; set; }
        }

        /// <summary>
        /// Calculate optimal duct diameter for given airflow rate per CIBSE Guide C.
        /// Target velocity: 3-6 m/s for low-velocity, 6-12 m/s for high-velocity.
        /// </summary>
        public static double SizeDuctDiameterMm(double airflowLps, bool lowVelocity = true)
        {
            double targetVel = lowVelocity ? 5.0 : 8.0; // m/s
            double areaM2 = (airflowLps / 1000.0) / targetVel;
            double diamM = Math.Sqrt(4.0 * areaM2 / Math.PI);
            double diamMm = diamM * 1000.0;

            // Round up to standard duct sizes per BS EN 12237
            double[] standardSizes = { 100, 125, 150, 160, 200, 250, 300, 315, 355,
                400, 450, 500, 560, 630, 710, 800, 900, 1000, 1120, 1250 };
            foreach (double s in standardSizes)
                if (s >= diamMm) return s;
            return standardSizes[^1];
        }

        /// <summary>
        /// Calculate optimal pipe diameter for given flow rate per CIBSE Guide C.
        /// Target velocity: 0.5-1.5 m/s for LTHW/CHW, 1.0-3.0 m/s for mains.
        /// </summary>
        public static double SizePipeDiameterMm(double flowLps, bool mainsPressure = false)
        {
            double targetVel = mainsPressure ? 2.0 : 1.0; // m/s
            double areaM2 = (flowLps / 1000.0) / targetVel;
            double diamM = Math.Sqrt(4.0 * areaM2 / Math.PI);
            double diamMm = diamM * 1000.0;

            // Standard copper/steel pipe sizes
            double[] standardSizes = { 15, 22, 28, 35, 42, 54, 67, 76, 108, 133, 159, 219, 273 };
            foreach (double s in standardSizes)
                if (s >= diamMm) return s;
            return standardSizes[^1];
        }

        /// <summary>
        /// Phase 67: Validate duct velocity against CIBSE Guide C limits.
        /// Returns (pass, actualVelocity, maxVelocity, message).
        /// </summary>
        public static (bool Pass, double ActualMs, double MaxMs, string Message) ValidateDuctVelocity(
            double airflowLps, double diamMm, string ductType = "Supply Duct - Main")
        {
            if (diamMm <= 0) return (false, 0, 0, "Invalid duct diameter");
            double areaM2 = Math.PI * (diamMm / 1000.0) * (diamMm / 1000.0) / 4.0;
            if (areaM2 < 1e-10) return (false, 0, 0, "Zero area");
            double velocity = (airflowLps / 1000.0) / areaM2;

            // CIBSE Guide C velocity limits by duct type
            var limits = Temp.StandardsEngine.CibseVelocityLimits;
            double maxVel = 10.0; // default fallback
            if (limits.TryGetValue(ductType, out var limit))
                maxVel = limit.MaxVelocity;

            bool pass = velocity <= maxVel;
            string msg = pass
                ? $"OK: {velocity:F1} m/s ≤ {maxVel:F1} m/s ({ductType})"
                : $"FAIL: {velocity:F1} m/s > {maxVel:F1} m/s limit ({ductType}) — increase duct size";
            return (pass, velocity, maxVel, msg);
        }

        /// <summary>
        /// Phase 67: Validate pipe velocity against CIBSE Guide C limits.
        /// Returns (pass, actualVelocity, maxVelocity, message).
        /// </summary>
        public static (bool Pass, double ActualMs, double MaxMs, string Message) ValidatePipeVelocity(
            double flowLps, double diamMm, string pipeType = "Chilled Water")
        {
            if (diamMm <= 0) return (false, 0, 0, "Invalid pipe diameter");
            double areaM2 = Math.PI * (diamMm / 1000.0) * (diamMm / 1000.0) / 4.0;
            if (areaM2 < 1e-10) return (false, 0, 0, "Zero area");
            double velocity = (flowLps / 1000.0) / areaM2;

            var limits = Temp.StandardsEngine.CibseVelocityLimits;
            double maxVel = 3.0;
            if (limits.TryGetValue(pipeType, out var limit))
                maxVel = limit.MaxVelocity;

            bool pass = velocity <= maxVel;
            string msg = pass
                ? $"OK: {velocity:F2} m/s ≤ {maxVel:F1} m/s ({pipeType})"
                : $"FAIL: {velocity:F2} m/s > {maxVel:F1} m/s limit ({pipeType}) — increase pipe size";
            return (pass, velocity, maxVel, msg);
        }

        /// <summary>
        /// Calculate pressure drop through a straight duct segment per CIBSE Guide C.
        /// Uses Darcy-Weisbach equation with Colebrook-White friction factor.
        /// </summary>
        public static double DuctPressureDropPa(double lengthM, double diamMm, double airflowLps)
        {
            // Phase 56: Guard against division by zero (BUG-01)
            if (diamMm <= 0 || lengthM <= 0 || airflowLps <= 0) return 0;
            double d = diamMm / 1000.0;
            double area = Math.PI * d * d / 4.0;
            if (area < 1e-10) return 0; // Prevent division by zero
            double velocity = (airflowLps / 1000.0) / area;
            double rho = 1.2; // kg/m³ air density at ~20°C
            double roughness = 0.00015; // m (galvanised steel)

            // Simplified Colebrook-White (single iteration)
            double Re = velocity * d / 1.5e-5; // kinematic viscosity ~1.5e-5 m²/s
            if (Re < 1) return 0;
            double logTerm = roughness / (3.7 * d) + 5.74 / Math.Pow(Re, 0.9);
            if (logTerm <= 0 || double.IsNaN(logTerm) || double.IsInfinity(logTerm)) return 0;
            double logVal = Math.Log10(logTerm);
            if (Math.Abs(logVal) < 1e-12 || double.IsNaN(logVal) || double.IsInfinity(logVal)) return 0;
            double f = 0.25 / Math.Pow(logVal, 2);
            return f * (lengthM / d) * (rho * velocity * velocity / 2.0);
        }

        /// <summary>
        /// Generate an L-shaped route between two points (Manhattan routing).
        /// Avoids direct diagonal runs — uses horizontal + vertical segments.
        /// </summary>
        public static RouteResult RouteManhattan(XYZ startFt, XYZ endFt, double diamMm)
        {
            var result = new RouteResult { Success = true };
            double dx = endFt.X - startFt.X;
            double dy = endFt.Y - startFt.Y;
            double dz = endFt.Z - startFt.Z;

            // Horizontal first, then vertical offset
            var mid1 = new XYZ(endFt.X, startFt.Y, startFt.Z);
            var mid2 = new XYZ(endFt.X, endFt.Y, startFt.Z);

            if (Math.Abs(dx) > 0.01)
            {
                result.Segments.Add(new RouteSegment
                {
                    Start = startFt, End = mid1,
                    DiameterMm = diamMm,
                    LengthMm = Math.Abs(dx) * Units.FeetToMm,
                    SegmentType = "Straight"
                });
            }

            if (Math.Abs(dy) > 0.01)
            {
                var segStart = result.Segments.Count > 0 ? mid1 : startFt;
                result.Segments.Add(new RouteSegment
                {
                    Start = segStart, End = mid2,
                    DiameterMm = diamMm,
                    LengthMm = Math.Abs(dy) * Units.FeetToMm,
                    SegmentType = "Straight"
                });
                if (result.Segments.Count > 1) result.ElbowCount++;
            }

            if (Math.Abs(dz) > 0.01)
            {
                var segStart = result.Segments.Count > 0 ? mid2 : startFt;
                result.Segments.Add(new RouteSegment
                {
                    Start = segStart, End = endFt,
                    DiameterMm = diamMm,
                    LengthMm = Math.Abs(dz) * Units.FeetToMm,
                    SegmentType = "Straight"
                });
                if (result.Segments.Count > 1) result.ElbowCount++;
            }

            result.TotalLengthMm = result.Segments.Sum(s => s.LengthMm);

            // Phase 56 GAP-A02 fix: Calculate pressure drop for the complete route
            if (diamMm > 0 && result.TotalLengthMm > 0)
            {
                double totalLengthM = result.TotalLengthMm / 1000.0;
                // Estimate airflow from duct area using mid-range velocity (5 m/s)
                double areaSqM = Math.PI * (diamMm / 1000.0) * (diamMm / 1000.0) / 4.0;
                double estimatedAirflowLps = areaSqM * 5.0 * 1000.0; // 5 m/s target velocity
                result.PressureDropPa = DuctPressureDropPa(totalLengthM, diamMm, estimatedAirflowLps);
                // Add elbow losses (CIBSE equivalent length: ~1m per 90° elbow at this diameter)
                result.PressureDropPa += result.ElbowCount * DuctPressureDropPa(1.0, diamMm, estimatedAirflowLps);
            }

            result.Message = $"Route: {result.Segments.Count} segments, {result.ElbowCount} elbows, " +
                $"{result.TotalLengthMm / 1000:F1}m total, Ø{diamMm}mm" +
                (result.PressureDropPa > 0 ? $", ΔP={result.PressureDropPa:F1}Pa" : "");
            return result;
        }

        /// <summary>
        /// Check if a proposed route segment clashes with existing elements.
        /// Returns list of clashing element IDs.
        /// </summary>
        public static List<ElementId> DetectClashes(Document doc, XYZ startFt, XYZ endFt,
            double clearanceFt = 0.25)
        {
            var clashes = new List<ElementId>();
            try
            {
                var mid = (startFt + endFt) / 2.0;
                var halfLen = startFt.DistanceTo(endFt) / 2.0 + clearanceFt;
                var outline = new Outline(
                    new XYZ(mid.X - halfLen, mid.Y - halfLen, mid.Z - clearanceFt),
                    new XYZ(mid.X + halfLen, mid.Y + halfLen, mid.Z + clearanceFt));
                var bbFilter = new BoundingBoxIntersectsFilter(outline);
                var nearby = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbFilter)
                    .ToElementIds();
                clashes.AddRange(nearby);
            }
            catch (Exception ex) { StingLog.Warn($"DetectClashes: {ex.Message}"); }
            return clashes;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // ROOM LAYOUT INTELLIGENCE — Space planning algorithms
    // Phase 55: Automated room layout based on area program
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Room layout intelligence engine for automated space planning.
    /// Generates room arrangements from area programs using bin-packing
    /// and adjacency-aware placement algorithms.
    ///
    /// Standards: BS EN 15221-6 (area measurement), BCO Guide (office standards)
    /// </summary>
    internal static class RoomLayoutEngine
    {
        /// <summary>Space requirement from area program.</summary>
        public class SpaceRequirement
        {
            public string Name { get; set; }
            public double AreaSqM { get; set; }
            public double MinWidthM { get; set; } = 2.5;
            public double MaxAspectRatio { get; set; } = 3.0;
            public string Department { get; set; }
            public List<string> AdjacentTo { get; set; } = new();
            public bool RequiresWindow { get; set; }
            public bool RequiresDoor { get; set; } = true;
        }

        /// <summary>Placed room with position and dimensions.</summary>
        public class PlacedRoom
        {
            public SpaceRequirement Requirement { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double WidthM { get; set; }
            public double DepthM { get; set; }
        }

        /// <summary>
        /// Calculate optimal room dimensions from area requirement.
        /// Respects minimum width and maximum aspect ratio constraints.
        /// </summary>
        public static (double widthM, double depthM) CalculateDimensions(SpaceRequirement req)
        {
            double area = req.AreaSqM;
            double minW = req.MinWidthM;
            double maxRatio = req.MaxAspectRatio;

            // Start with square aspect
            double side = Math.Sqrt(area);
            double width = Math.Max(side, minW);
            double depth = area / width;

            // Enforce aspect ratio
            if (width / depth > maxRatio) { depth = width / maxRatio; }
            if (depth / width > maxRatio) { width = depth / maxRatio; }

            // Round to 100mm module
            width = Math.Ceiling(width * 10) / 10.0;
            depth = Math.Ceiling(depth * 10) / 10.0;

            return (width, depth);
        }

        /// <summary>
        /// Generate a strip layout for multiple rooms along a corridor.
        /// Rooms placed side by side with shared party walls.
        /// BCO Guide standard: 1.5m corridor, rooms either side.
        /// </summary>
        public static List<PlacedRoom> StripLayout(
            List<SpaceRequirement> rooms, double corridorWidthM = 1.5)
        {
            var placed = new List<PlacedRoom>();
            double currentX = 0;
            double maxDepth = 0;

            // Sort by department then area (largest first) for adjacency
            var sorted = rooms.OrderBy(r => r.Department).ThenByDescending(r => r.AreaSqM).ToList();

            foreach (var req in sorted)
            {
                var (w, d) = CalculateDimensions(req);
                placed.Add(new PlacedRoom
                {
                    Requirement = req,
                    X = currentX,
                    Y = corridorWidthM,
                    WidthM = w,
                    DepthM = d
                });
                currentX += w;
                maxDepth = Math.Max(maxDepth, d);
            }

            return placed;
        }

        /// <summary>
        /// Execute room layout in Revit: create walls, place rooms, tag.
        /// </summary>
        public static ModelResult ExecuteLayout(Document doc, List<PlacedRoom> layout,
            string levelName = null)
        {
            var engine = new ModelEngine(doc);
            var allIds = new List<ElementId>();
            int roomCount = 0;

            using (var tg = new TransactionGroup(doc, "STING MODEL: Room Layout"))
            {
                tg.Start();
                try
                {
                    foreach (var room in layout)
                    {
                        var result = engine.CreateRectangularRoom(
                            room.WidthM * 1000, room.DepthM * 1000,
                            room.Requirement.Name, levelName,
                            originXMm: room.X * 1000, originYMm: room.Y * 1000);

                        if (result.Success)
                        {
                            allIds.AddRange(result.CreatedElementIds);
                            if (result.CreatedElementId != ElementId.InvalidElementId)
                                allIds.Add(result.CreatedElementId);
                            roomCount++;
                        }
                    }
                    tg.Assimilate();
                }
                catch (Exception ex)
                {
                    tg.RollBack();
                    return ModelResult.Fail($"Room layout failed: {ex.Message}");
                }
            }

            // Auto-tag all created elements
            int tagged = ModelEngine.AutoTagCreatedElements(doc, allIds);

            return ModelResult.OkBatch(
                $"Created {roomCount} rooms, {allIds.Count} elements, {tagged} auto-tagged",
                allIds);
        }
    }

    #region Phase 68: Model Intelligence Algorithms

    // ═════════════════════════════════════════════════════════════════
    //  EMBODIED CARBON CALCULATOR — per RICS Whole Life Carbon / EN 15978
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Calculates embodied carbon (kgCO2e) for model elements using material
    /// quantity extraction and carbon factor lookup from MATERIAL_LOOKUP.csv.
    /// Supports A1-A3 (product stage) and B4 (replacement) lifecycle stages.
    /// </summary>
    internal static class ModelEmbodiedCarbonCalculator
    {
        // Carbon factors in kgCO2e/kg (typical UK values per ICE Database v3.0)
        private static readonly Dictionary<string, double> CarbonFactors = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Concrete", 0.13 }, { "Steel", 1.55 }, { "Timber", -1.0 },
            { "Aluminium", 6.67 }, { "Brick", 0.24 }, { "Glass", 1.44 },
            { "Copper", 2.71 }, { "Plasterboard", 0.39 }, { "Insulation", 1.86 },
            { "Stone", 0.06 }, { "Mortar", 0.19 }, { "Clay", 0.23 },
            { "PVC", 2.41 }, { "HDPE", 1.93 }, { "Bitumen", 0.49 },
            { "Ceramic", 0.78 }, { "Zinc", 3.09 }, { "Lead", 1.57 },
        };

        // Material density in kg/m³
        private static readonly Dictionary<string, double> MaterialDensity = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Concrete", 2400 }, { "Steel", 7850 }, { "Timber", 500 },
            { "Aluminium", 2700 }, { "Brick", 1900 }, { "Glass", 2500 },
            { "Copper", 8940 }, { "Plasterboard", 800 }, { "Insulation", 40 },
            { "Stone", 2600 }, { "Mortar", 1900 }, { "Clay", 2000 },
            { "PVC", 1380 }, { "HDPE", 960 }, { "Bitumen", 1100 },
        };

        /// <summary>
        /// Calculate embodied carbon for elements. Returns total kgCO2e and per-element breakdown.
        /// </summary>
        internal static (double TotalKgCO2e, List<(ElementId Id, string Category, string Material, double VolM3, double KgCO2e)> Breakdown)
            CalculateForElements(Document doc, IList<ElementId> elementIds)
        {
            var breakdown = new List<(ElementId, string, string, double, double)>();
            double total = 0;

            foreach (var id in elementIds)
            {
                try
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;

                    string category = el.Category?.Name ?? "Unknown";
                    double volFt3 = 0;
                    string material = "Unknown";

                    // Extract volume from element geometry
                    try
                    {
                        var volParam = el.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
                        if (volParam != null) volFt3 = volParam.AsDouble();
                    }
                    catch (Exception ex) { StingLog.Warn($"Volume extraction {id}: {ex.Message}"); }

                    if (volFt3 <= 0) continue;

                    // Get material from element
                    try
                    {
                        var matIds = el.GetMaterialIds(false);
                        if (matIds.Count > 0)
                        {
                            var mat = doc.GetElement(matIds.First()) as Material;
                            material = mat?.Name ?? "Unknown";
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"Material lookup {id}: {ex.Message}"); }

                    double volM3 = volFt3 * 0.0283168; // ft³ to m³
                    string matchedMaterial = MatchMaterialCategory(material);
                    double density = MaterialDensity.GetValueOrDefault(matchedMaterial, 2000);
                    double factor = CarbonFactors.GetValueOrDefault(matchedMaterial, 0.5);
                    double kgCO2e = volM3 * density * factor;

                    breakdown.Add((id, category, material, volM3, kgCO2e));
                    total += kgCO2e;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"EmbodiedCarbon element {id}: {ex.Message}");
                }
            }

            return (total, breakdown);
        }

        private static string MatchMaterialCategory(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return "Concrete";
            string lower = materialName.ToLowerInvariant();
            foreach (var kvp in CarbonFactors)
            {
                if (lower.Contains(kvp.Key.ToLowerInvariant())) return kvp.Key;
            }
            return "Concrete"; // default
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  SPATIAL ANALYSIS ENGINE — area, volume, adjacency metrics
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provides spatial analysis metrics for model elements: room adjacency,
    /// circulation efficiency, area compliance per BCO Guide / BS EN 15221-6.
    /// </summary>
    internal static class SpatialAnalysisEngine
    {
        // BCO Guide minimum areas (m²) per space function
        private static readonly Dictionary<string, double> MinAreaStandards = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Office", 6.0 },       // BCO Guide: 6m² NIA per person
            { "Toilet", 1.5 },       // BS 6465: individual cubicle
            { "Corridor", 1.2 },     // Part B: min 1.2m clear width × length
            { "Meeting", 2.0 },      // BCO: 2m² per person
            { "Reception", 10.0 },   // Typical minimum
            { "Kitchen", 5.0 },      // BS 4250
            { "Plant", 4.0 },        // Typical
            { "Store", 2.0 },        // Typical
            { "Stair", 1.1 },        // BS 5395: min 1.1m wide
        };

        /// <summary>
        /// Audit rooms for area compliance. Returns list of (room, area, minArea, isCompliant).
        /// </summary>
        internal static List<(Room Room, double AreaSqM, double MinSqM, bool Compliant, string Standard)>
            AuditRoomAreas(Document doc)
        {
            var results = new List<(Room, double, double, bool, string)>();
            try
            {
                foreach (Room room in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>())
                {
                    if (room.Area <= 0) continue;
                    double areaSqM = room.Area * Units.SqFtToSqM;
                    string name = (room.Name ?? "").ToLowerInvariant();

                    // Match to standard
                    double minArea = 0;
                    string standard = "";
                    foreach (var kvp in MinAreaStandards)
                    {
                        if (name.Contains(kvp.Key.ToLowerInvariant()))
                        {
                            minArea = kvp.Value;
                            standard = kvp.Key switch
                            {
                                "Office" => "BCO Guide",
                                "Toilet" => "BS 6465",
                                "Corridor" => "Approved Document B",
                                "Meeting" => "BCO Guide",
                                "Stair" => "BS 5395",
                                _ => "General"
                            };
                            break;
                        }
                    }

                    results.Add((room, areaSqM, minArea, minArea <= 0 || areaSqM >= minArea, standard));
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpatialAnalysis.AuditRoomAreas: {ex.Message}"); }
            return results;
        }

        /// <summary>
        /// Calculate gross-to-net floor area ratio per level (NIA efficiency).
        /// Values per BCO Guide: >80% excellent, 70-80% good, <70% poor.
        /// </summary>
        internal static List<(string Level, double GrossSqM, double NetSqM, double Efficiency)>
            CalculateFloorEfficiency(Document doc)
        {
            var results = new List<(string, double, double, double)>();
            try
            {
                var roomsByLevel = new Dictionary<string, double>();
                foreach (Room room in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>())
                {
                    if (room.Area <= 0) continue;
                    string lvl = room.Level?.Name ?? "Unknown";
                    if (!roomsByLevel.ContainsKey(lvl)) roomsByLevel[lvl] = 0;
                    roomsByLevel[lvl] += room.Area * Units.SqFtToSqM;
                }

                // Estimate gross from floor elements
                var floorsByLevel = new Dictionary<string, double>();
                foreach (var floor in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Floors)
                    .WhereElementIsNotElementType())
                {
                    try
                    {
                        var areaParam = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                        if (areaParam == null) continue;
                        double area = areaParam.AsDouble() * Units.SqFtToSqM;
                        string lvl = (doc.GetElement(floor.LevelId) as Level)?.Name ?? "Unknown";
                        if (!floorsByLevel.ContainsKey(lvl)) floorsByLevel[lvl] = 0;
                        floorsByLevel[lvl] += area;
                    }
                    catch (Exception ex) { StingLog.Warn($"Floor area: {ex.Message}"); }
                }

                foreach (var kvp in floorsByLevel)
                {
                    double net = roomsByLevel.GetValueOrDefault(kvp.Key, 0);
                    double gross = kvp.Value;
                    double efficiency = gross > 0 ? (net / gross * 100) : 0;
                    results.Add((kvp.Key, gross, net, efficiency));
                }
            }
            catch (Exception ex) { StingLog.Warn($"SpatialAnalysis.CalculateFloorEfficiency: {ex.Message}"); }
            return results;
        }
    }

    // ═════════════════════════════════════════════════════════════════
    //  MODEL METRICS ENGINE — element counts, quantities, complexity
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Extracts model-level metrics for BIM coordinator dashboards:
    /// element counts by category, material quantities, model complexity score.
    /// </summary>
    internal static class ModelMetricsEngine
    {
        /// <summary>
        /// Calculate model complexity score (0-100). Considers element count,
        /// linked models, worksets, warnings, and MEP system count.
        /// </summary>
        internal static (int Score, Dictionary<string, int> ByCategory, int LinkCount, int WorksetCount, int SystemCount)
            CalculateComplexity(Document doc)
        {
            var byCategory = new Dictionary<string, int>();
            int linkCount = 0;
            int worksetCount = 0;
            int systemCount = 0;
            int totalElements = 0;

            try
            {
                // Element counts by category
                foreach (var el in new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType())
                {
                    string cat = el.Category?.Name ?? "Uncategorized";
                    if (!byCategory.ContainsKey(cat)) byCategory[cat] = 0;
                    byCategory[cat]++;
                    totalElements++;
                }

                linkCount = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance)).GetElementCount();

                try { worksetCount = doc.IsWorkshared ? new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets().Count : 0; }
                catch (Exception ex) { StingLog.Warn($"Workset count: {ex.Message}"); }

                try
                {
                    systemCount = new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Mechanical.MechanicalSystem)).GetElementCount()
                        + new FilteredElementCollector(doc)
                        .OfClass(typeof(Autodesk.Revit.DB.Plumbing.PipingSystem)).GetElementCount();
                }
                catch (Exception ex) { StingLog.Warn($"System count: {ex.Message}"); }
            }
            catch (Exception ex) { StingLog.Warn($"ModelMetrics: {ex.Message}"); }

            // Complexity scoring: 0-100
            int score = 0;
            score += Math.Min(30, totalElements / 1000);         // Up to 30 for element count
            score += Math.Min(20, linkCount * 5);                // Up to 20 for links
            score += Math.Min(15, worksetCount);                 // Up to 15 for worksets
            score += Math.Min(15, systemCount / 2);              // Up to 15 for MEP systems
            score += Math.Min(20, byCategory.Count / 3);         // Up to 20 for category diversity
            score = Math.Min(100, score);

            return (score, byCategory, linkCount, worksetCount, systemCount);
        }

        /// <summary>
        /// Extract material quantities (volume m³, area m², weight kg) by material name.
        /// </summary>
        internal static Dictionary<string, (double VolM3, double AreaM2, double WeightKg)>
            ExtractMaterialQuantities(Document doc)
        {
            var quantities = new Dictionary<string, (double, double, double)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var el in new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType())
                {
                    try
                    {
                        var matIds = el.GetMaterialIds(false);
                        foreach (var matId in matIds)
                        {
                            var mat = doc.GetElement(matId) as Material;
                            if (mat == null) continue;
                            string name = mat.Name;

                            double vol = 0;
                            try { vol = el.GetMaterialVolume(matId) * 0.0283168; } // ft³ → m³
                            catch (Exception ex) { StingLog.Warn($"MatVol: {ex.Message}"); }

                            double area = 0;
                            try { area = el.GetMaterialArea(matId, false) * 0.092903; } // ft² → m²
                            catch (Exception ex) { StingLog.Warn($"MatArea: {ex.Message}"); }

                            if (!quantities.ContainsKey(name)) quantities[name] = (0, 0, 0);
                            var current = quantities[name];
                            quantities[name] = (current.Item1 + vol, current.Item2 + area, current.Item3);
                        }
                    }
                    catch (Exception ex) { StingLog.Warn($"MaterialQuantity element: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"ExtractMaterialQuantities: {ex.Message}"); }
            return quantities;
        }
    }

    #endregion
}
