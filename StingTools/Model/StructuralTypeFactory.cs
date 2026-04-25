// ============================================================================
// StructuralTypeFactory.cs — Intelligent Structural Family Resolution & Type Creation
//
// When a DWG element has specific dimensions (e.g., 600×400mm beam), this factory:
//   1. Searches ALL loaded families for the best dimensional match
//   2. If exact match found → returns it
//   3. If close match found → duplicates it and adjusts dimensions
//   4. If no match → creates a new type from the closest base family
//
// Algorithms:
//   - Multi-criteria weighted scoring (size match, name relevance, category fit)
//   - Euclidean distance in dimension space for closest-match ranking
//   - Automatic type naming from dimensions (e.g., "UC 305×305" from geometry)
//   - Compound structure manipulation for wall/floor/slab thickness
//   - Parameter-driven resizing for family instances (columns, beams)
//
// Supports: Columns, Beams, Foundations, Walls, Floors, Braces
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using StingTools.Core;

namespace StingTools.Model
{
    #region Type Match Result

    /// <summary>Result from type factory search/creation.</summary>
    public class TypeMatchResult
    {
        public bool Success { get; set; }
        public ElementId TypeId { get; set; }
        public string TypeName { get; set; }
        public string FamilyName { get; set; }
        public TypeMatchMethod MatchMethod { get; set; }
        public double MatchScore { get; set; }
        public string Message { get; set; }
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }

        public static TypeMatchResult Found(ElementId id, string family, string type,
            TypeMatchMethod method, double score) =>
            new() { Success = true, TypeId = id, FamilyName = family, TypeName = type,
                MatchMethod = method, MatchScore = score };

        public static TypeMatchResult NotFound(string msg) =>
            new() { Success = false, Message = msg };
    }

    /// <summary>How the type was matched.</summary>
    public enum TypeMatchMethod
    {
        ExactMatch,         // Perfect dimensional match in loaded families
        CloseMatch,         // Within 10% tolerance
        DuplicatedAndSized, // Duplicated existing type, adjusted dimensions
        NewTypeCreated,     // Created new type from scratch
        FallbackFirst       // Used first available (no dimensional data)
    }

    #endregion

    #region Structural Family Catalog Entry

    /// <summary>Cached entry for a loaded structural family type with extracted dimensions.</summary>
    internal class FamilyCatalogEntry
    {
        public ElementId TypeId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public BuiltInCategory Category { get; set; }
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
        public double ThicknessMm { get; set; }
        public double LengthMm { get; set; }
        public bool IsActive { get; set; }

        /// <summary>
        /// Weighted Euclidean distance in (width, depth) space.
        /// ACC-07 fix: depth weighted 1.5× for beams where depth is the critical dimension.
        /// </summary>
        public double DimensionDistance(double targetWidthMm, double targetDepthMm,
            double depthWeight = 1.0)
        {
            double dw = WidthMm - targetWidthMm;
            double dd = (DepthMm - targetDepthMm) * depthWeight;
            return Math.Sqrt(dw * dw + dd * dd);
        }
    }

    #endregion

    /// <summary>
    /// Intelligent structural type factory. Searches loaded families by dimensions,
    /// duplicates and resizes when no exact match exists.
    ///
    /// Usage:
    ///   var factory = new StructuralTypeFactory(doc);
    ///   var result = factory.FindOrCreateColumnType(400, 400); // 400×400 column
    ///   var result = factory.FindOrCreateBeamType(600, 300);   // 600×300 beam
    ///   var result = factory.FindOrCreateWallType(250);        // 250mm wall
    /// </summary>
    public class StructuralTypeFactory
    {
        private readonly Document _doc;
        private List<FamilyCatalogEntry> _catalog;
        private readonly Dictionary<string, ElementId> _createdTypes = new();

        // ACC-09 fix: Relative tolerance (15% of target size) instead of fixed 200mm
        private const double RelativeDuplicateTolerance = 0.15;

        public StructuralTypeFactory(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        /// <summary>Number of families in the catalog after building.</summary>
        public int CatalogSize => _catalog?.Count ?? 0;

        // ── Catalog Builder ──────────────────────────────────────────────

        /// <summary>
        /// Builds a catalog of all loaded structural families with extracted dimensions.
        /// Scans columns, beams, foundations, and structural walls.
        /// </summary>
        public void BuildCatalog()
        {
            _catalog = new List<FamilyCatalogEntry>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Structural columns
            ScanCategory(BuiltInCategory.OST_StructuralColumns);
            // Structural framing (beams, braces, purlins)
            ScanCategory(BuiltInCategory.OST_StructuralFraming);
            // Structural foundations
            ScanCategory(BuiltInCategory.OST_StructuralFoundation);
            // Generic models (sometimes used for structural)
            // Bug#10 FIX: Skip OST_GenericModel — pollutes catalog with 1000+ non-structural types

            // Wall types (system families — different scanning)
            ScanWallTypes();
            // Floor types
            ScanFloorTypes();

            sw.Stop();
            StingLog.Info($"StructuralTypeFactory: Built catalog of {_catalog.Count} types in {sw.ElapsedMilliseconds}ms");
        }

        private void ScanCategory(BuiltInCategory bic)
        {
            var symbols = new FilteredElementCollector(_doc)
                .OfCategory(bic)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            foreach (var sym in symbols)
            {
                var entry = new FamilyCatalogEntry
                {
                    TypeId = sym.Id,
                    FamilyName = sym.FamilyName,
                    TypeName = sym.Name,
                    Category = bic,
                    IsActive = sym.IsActive,
                };

                // Extract dimensions from type parameters
                ExtractDimensions(sym, entry);
                if (entry.WidthMm <= 0 && entry.DepthMm <= 0 && entry.ThicknessMm <= 0)
                {
                    StingLog.Warn($"StructuralTypeFactory: {sym.FamilyName}/{sym.Name} has no extractable dimensions — skipped");
                    continue;
                }
                _catalog.Add(entry);
            }
        }

        private void ScanWallTypes()
        {
            // Bug#11 FIX: Filter to Basic walls only (exclude curtain, stacked)
            var types = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(wt => wt.Kind == WallKind.Basic)
                .ToList();

            foreach (var wt in types)
            {
                _catalog.Add(new FamilyCatalogEntry
                {
                    TypeId = wt.Id,
                    FamilyName = "System Family: Wall",
                    TypeName = wt.Name,
                    Category = BuiltInCategory.OST_Walls,
                    WidthMm = wt.Width * Units.FeetToMm,
                    ThicknessMm = wt.Width * Units.FeetToMm,
                    IsActive = true,
                });
            }
        }

        private void ScanFloorTypes()
        {
            var types = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .ToList();

            foreach (var ft in types)
            {
                double thickMm = 0;
                try
                {
                    var cs = ft.GetCompoundStructure();
                    if (cs != null) thickMm = cs.GetWidth() * Units.FeetToMm;
                }
                catch (Exception ex) { StingLog.Warn($"FloorType scan: {ex.Message}"); }

                _catalog.Add(new FamilyCatalogEntry
                {
                    TypeId = ft.Id,
                    FamilyName = "System Family: Floor",
                    TypeName = ft.Name,
                    Category = BuiltInCategory.OST_Floors,
                    ThicknessMm = thickMm,
                    IsActive = true,
                });
            }
        }

        /// <summary>
        /// Extracts width/depth dimensions from a FamilySymbol using multiple strategies:
        /// 1. Try standard built-in parameters (Width, Height, Depth)
        /// 2. Try type name parsing ("UC 305x305", "200x400", etc.)
        /// 3. Try family parameter inspection
        /// </summary>
        private void ExtractDimensions(FamilySymbol sym, FamilyCatalogEntry entry)
        {
            // Strategy 1: Built-in parameters
            double w = GetParamMm(sym, BuiltInParameter.FAMILY_WIDTH_PARAM);
            double d = GetParamMm(sym, BuiltInParameter.FAMILY_HEIGHT_PARAM);
            if (w <= 0) w = GetParamMm(sym, BuiltInParameter.STRUCTURAL_SECTION_ISHAPE_WEBTHICKNESS);
            if (d <= 0) d = GetParamMm(sym, BuiltInParameter.STRUCTURAL_SECTION_ISHAPE_WEBHEIGHT);

            // Common structural section parameters
            if (w <= 0) w = GetParamMm(sym, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
            if (d <= 0) d = GetParamMm(sym, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);

            // Nominal parameters
            if (w <= 0) w = GetParamMm(sym, BuiltInParameter.STRUCTURAL_SECTION_COMMON_NOMINAL_WEIGHT);

            // Strategy 2: Parse type name for dimensions (e.g., "305x305", "200x400")
            if (w <= 0 || d <= 0)
            {
                var (pw, pd) = ParseDimensionsFromName(sym.Name);
                if (w <= 0) w = pw;
                if (d <= 0) d = pd;

                // Also try family name
                if (w <= 0 || d <= 0)
                {
                    var (fw, fd) = ParseDimensionsFromName(sym.FamilyName);
                    if (w <= 0) w = fw;
                    if (d <= 0) d = fd;
                }
            }

            // Strategy 3: Scan all type parameters for dimension-like values
            // PERF-03 fix: early exit once both dimensions found
            if (w <= 0 || d <= 0)
            {
                foreach (Parameter p in sym.Parameters)
                {
                    if (w > 0 && d > 0) break; // Early exit
                    if (p.StorageType != StorageType.Double) continue;
                    string name = p.Definition?.Name?.ToLowerInvariant() ?? "";
                    double val = p.AsDouble() * Units.FeetToMm;
                    if (val <= 0 || val > 5000) continue;

                    if (w <= 0 && (name.Contains("width") || (name.Contains("b") && name.Length <= 3)))
                        w = val;
                    else if (d <= 0 && (name.Contains("depth") || name.Contains("height") ||
                        name == "d" || name == "h"))
                        d = val;
                }
            }

            entry.WidthMm = w;
            entry.DepthMm = d;
        }

        private double GetParamMm(Element el, BuiltInParameter bip)
        {
            try
            {
                var p = el.get_Parameter(bip);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                    return p.AsDouble() * Units.FeetToMm;
            }
            catch (Exception ex) { StingLog.Warn($"ParamRead: {ex.Message}"); }
            return 0;
        }

        /// <summary>
        /// Parses dimensions from a name string like "UC 305x305", "200x400mm", "W14x22".
        /// ALG-07 fix: Unit-aware parsing — detects inches from imperial prefixes (W, HP, C).
        /// Returns (width, depth) in mm, or (0,0) if not parseable.
        /// </summary>
        internal static (double Width, double Depth) ParseDimensionsFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return (0, 0);

            // Detect if name uses imperial convention (W shapes, HP shapes, C channels)
            bool isImperial = System.Text.RegularExpressions.Regex.IsMatch(
                name, @"^(W|HP|WT|MC|C|S|L|HSS)\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Pattern: NUMBERxNUMBER or NUMBER×NUMBER
            var match = System.Text.RegularExpressions.Regex.Match(
                name, @"(\d+(?:\.\d+)?)\s*[x×X]\s*(\d+(?:\.\d+)?)");
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, out double v1) &&
                    double.TryParse(match.Groups[2].Value, out double v2))
                {
                    // Imperial sections: values are in inches (W14x22 = 14" deep, 22 lb/ft)
                    if (isImperial)
                        return (v1 * 25.4, v1 * 25.4); // Use depth for both (weight is not a dimension)
                    // Metric: values already in mm if > 20, otherwise likely cm
                    if (v1 < 20 && v2 < 20) { v1 *= 10; v2 *= 10; } // cm → mm
                    return (v1, v2);
                }
            }

            // Pattern: single number after prefix (W14, C200, UB305, etc.)
            match = System.Text.RegularExpressions.Regex.Match(
                name, @"[A-Za-z]+\s*(\d{2,4})");
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value, out double v))
                {
                    if (isImperial) v *= 25.4; // inches → mm
                    return (v, v);
                }
            }

            return (0, 0);
        }


        // ── Column Type Resolution ───────────────────────────────────────

        /// <summary>
        /// Finds or creates a structural column type matching the target dimensions.
        /// Search order:
        ///   1. Exact match in loaded families (within 2mm tolerance)
        ///   2. Close match within 10% tolerance
        ///   3. Duplicate closest match and rename
        ///   4. Return closest available with warning
        /// </summary>
        public TypeMatchResult FindOrCreateColumnType(
            double widthMm, double depthMm = 0, string preferredFamily = null,
            bool allowDuplicate = true)
        {
            if (depthMm <= 0) depthMm = widthMm; // Square column default
            EnsureCatalog();

            string cacheKey = $"COL_{widthMm:F0}x{depthMm:F0}";
            if (_createdTypes.TryGetValue(cacheKey, out var cachedId))
                return TypeMatchResult.Found(cachedId, "", $"Cached {widthMm}×{depthMm}", TypeMatchMethod.DuplicatedAndSized, 1.0);

            // Filter to column types
            var candidates = _catalog
                .Where(e => e.Category == BuiltInCategory.OST_StructuralColumns)
                .ToList();

            if (candidates.Count == 0)
                return TypeMatchResult.NotFound("No structural column families loaded. Load a column family first.");

            // Prefer matching family name if specified
            if (!string.IsNullOrEmpty(preferredFamily))
            {
                var preferred = candidates.Where(e =>
                    e.FamilyName.IndexOf(preferredFamily, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (preferred.Count > 0) candidates = preferred;
            }

            // Score and rank candidates
            var ranked = ScoreAndRank(candidates, widthMm, depthMm);
            var best = ranked.First();

            // Exact match (within 2mm)
            if (best.entry.DimensionDistance(widthMm, depthMm) < 2.0)
            {
                EnsureActive(best.entry);
                return TypeMatchResult.Found(best.entry.TypeId, best.entry.FamilyName,
                    best.entry.TypeName, TypeMatchMethod.ExactMatch, best.score);
            }

            // Close match (within tolerance)
            if (best.entry.DimensionDistance(widthMm, depthMm) < Math.Max(widthMm, depthMm) * RelativeDuplicateTolerance)
            {
                // Phase-97: allowDuplicate=false → reuse closest existing
                // column type as-is; do NOT duplicate & resize.
                if (allowDuplicate)
                {
                    var dupResult = DuplicateAndResize(best.entry, widthMm, depthMm,
                        $"{best.entry.FamilyName} {widthMm:F0}x{depthMm:F0}");

                    if (dupResult.Success)
                    {
                        _createdTypes[cacheKey] = dupResult.TypeId;
                        return dupResult;
                    }
                }

                // Duplication not allowed or failed — return close match
                EnsureActive(best.entry);
                return TypeMatchResult.Found(best.entry.TypeId, best.entry.FamilyName,
                    best.entry.TypeName, TypeMatchMethod.CloseMatch, best.score);
            }

            // No close match — use best available
            EnsureActive(best.entry);
            var result = TypeMatchResult.Found(best.entry.TypeId, best.entry.FamilyName,
                best.entry.TypeName, TypeMatchMethod.FallbackFirst, best.score);
            result.Message = $"No {widthMm}×{depthMm}mm column found. Using {best.entry.TypeName} " +
                $"({best.entry.WidthMm:F0}×{best.entry.DepthMm:F0}mm)";
            return result;
        }

        // ── Beam Type Resolution ─────────────────────────────────────────

        /// <summary>
        /// Finds or creates a structural beam/framing type matching target dimensions.
        /// </summary>
        public TypeMatchResult FindOrCreateBeamType(
            double depthMm, double widthMm = 0, string preferredFamily = null,
            bool allowDuplicate = true)
        {
            if (widthMm <= 0) widthMm = depthMm * 0.5; // Default width = half depth
            EnsureCatalog();

            string cacheKey = $"BEAM_{depthMm:F0}x{widthMm:F0}";
            if (_createdTypes.TryGetValue(cacheKey, out var cachedId))
                return TypeMatchResult.Found(cachedId, "", $"Cached {depthMm}×{widthMm}", TypeMatchMethod.DuplicatedAndSized, 1.0);

            var candidates = _catalog
                .Where(e => e.Category == BuiltInCategory.OST_StructuralFraming)
                .ToList();

            if (candidates.Count == 0)
                return TypeMatchResult.NotFound("No structural framing families loaded. Load a beam family first.");

            if (!string.IsNullOrEmpty(preferredFamily))
            {
                var preferred = candidates.Where(e =>
                    e.FamilyName.IndexOf(preferredFamily, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (preferred.Count > 0) candidates = preferred;
            }

            // For beams, depth is the primary dimension — use 1.5× depth weight
            var ranked = ScoreAndRank(candidates, widthMm, depthMm, depthWeight: 1.5);
            var best = ranked.First();

            if (best.entry.DimensionDistance(widthMm, depthMm, 1.5) < 2.0)
            {
                EnsureActive(best.entry);
                return TypeMatchResult.Found(best.entry.TypeId, best.entry.FamilyName,
                    best.entry.TypeName, TypeMatchMethod.ExactMatch, best.score);
            }

            if (best.entry.DimensionDistance(widthMm, depthMm) < Math.Max(widthMm, depthMm) * RelativeDuplicateTolerance)
            {
                // Phase-97: allowDuplicate=false → reuse closest existing
                // beam type; do NOT duplicate & resize.
                if (allowDuplicate)
                {
                    var dupResult = DuplicateAndResize(best.entry, widthMm, depthMm,
                        $"{best.entry.FamilyName} {depthMm:F0}x{widthMm:F0}");
                    if (dupResult.Success)
                    {
                        _createdTypes[cacheKey] = dupResult.TypeId;
                        return dupResult;
                    }
                }
            }

            EnsureActive(best.entry);
            return TypeMatchResult.Found(best.entry.TypeId, best.entry.FamilyName,
                best.entry.TypeName, TypeMatchMethod.CloseMatch, ranked.First().score);
        }

        // ── Wall Type Resolution ─────────────────────────────────────────

        /// <summary>
        /// Finds or creates a wall type matching target thickness.
        /// For system families, duplicates and adjusts compound structure.
        /// </summary>
        public TypeMatchResult FindOrCreateWallType(double thicknessMm,
            bool isStructural = true, string preferredName = null,
            bool allowDuplicate = true)
        {
            EnsureCatalog();

            string cacheKey = $"WALL_{thicknessMm:F0}_{(isStructural ? "S" : "N")}";
            if (_createdTypes.TryGetValue(cacheKey, out var cachedId))
                return TypeMatchResult.Found(cachedId, "Wall", $"Cached {thicknessMm}mm", TypeMatchMethod.DuplicatedAndSized, 1.0);

            var wallTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(wt => wt.Kind == WallKind.Basic)
                .ToList();

            if (wallTypes.Count == 0)
                return TypeMatchResult.NotFound("No basic wall types in the project.");

            // Find exact or closest thickness match
            WallType exact = null, closest = null;
            double closestDiff = double.MaxValue;

            foreach (var wt in wallTypes)
            {
                double wtThickMm = wt.Width * Units.FeetToMm;
                double diff = Math.Abs(wtThickMm - thicknessMm);

                if (diff < 2.0) { exact = wt; break; }
                if (diff < closestDiff) { closestDiff = diff; closest = wt; }
            }

            if (exact != null)
                return TypeMatchResult.Found(exact.Id, "System Wall", exact.Name,
                    TypeMatchMethod.ExactMatch, 1.0);

            // Phase-97: when allowDuplicate is false, the caller has asked us
            // to pick from the existing catalogue only — no new wall types.
            // Return the closest existing type with a CloseMatch result.
            if (!allowDuplicate && closest != null)
            {
                _createdTypes[cacheKey] = closest.Id;
                return TypeMatchResult.Found(closest.Id, "System Wall", closest.Name,
                    TypeMatchMethod.CloseMatch, 0.6);
            }

            // Duplicate closest and adjust thickness
            if (closest != null)
            {
                string newName = $"STING RC Wall {thicknessMm:F0}mm";
                try
                {
                    // Check if already exists
                    var existing = wallTypes.FirstOrDefault(wt =>
                        wt.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        _createdTypes[cacheKey] = existing.Id;
                        return TypeMatchResult.Found(existing.Id, "System Wall", existing.Name,
                            TypeMatchMethod.ExactMatch, 0.95);
                    }

                    WallType newWt = null;
                    using (var tx = new Transaction(_doc, "STING STRUCT: Create Wall Type"))
                    {
                        tx.Start();
                        newWt = closest.Duplicate(newName) as WallType;
                        if (newWt != null)
                        {
                            // Adjust compound structure to target thickness
                            var cs = newWt.GetCompoundStructure();
                            if (cs != null)
                            {
                                // Scale all layers proportionally
                                double currentWidth = cs.GetWidth();
                                double targetWidth = Units.Mm(thicknessMm);
                                if (currentWidth > 0 && Math.Abs(currentWidth - targetWidth) > 0.001)
                                {
                                    double ratio = targetWidth / currentWidth;
                                    for (int i = 0; i < cs.LayerCount; i++)
                                    {
                                        double layerWidth = cs.GetLayerWidth(i);
                                        cs.SetLayerWidth(i, layerWidth * ratio);
                                    }
                                    newWt.SetCompoundStructure(cs);
                                }
                            }
                        }
                        tx.Commit();
                    }

                    if (newWt != null)
                    {
                        _createdTypes[cacheKey] = newWt.Id;
                        return TypeMatchResult.Found(newWt.Id, "System Wall", newName,
                            TypeMatchMethod.DuplicatedAndSized, 0.9);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Wall type creation: {ex.Message}");
                }

                return TypeMatchResult.Found(closest.Id, "System Wall", closest.Name,
                    TypeMatchMethod.CloseMatch, 0.7);
            }

            return TypeMatchResult.NotFound("No suitable wall type found.");
        }

        // ── Floor/Slab Type Resolution ───────────────────────────────────

        /// <summary>
        /// Finds or creates a floor type matching target thickness.
        /// </summary>
        public TypeMatchResult FindOrCreateFloorType(double thicknessMm,
            string preferredName = null, bool allowDuplicate = true)
        {
            EnsureCatalog();

            string cacheKey = $"FLOOR_{thicknessMm:F0}";
            if (_createdTypes.TryGetValue(cacheKey, out var cachedId))
                return TypeMatchResult.Found(cachedId, "Floor", $"Cached {thicknessMm}mm", TypeMatchMethod.DuplicatedAndSized, 1.0);

            var floorTypes = new FilteredElementCollector(_doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .ToList();

            if (floorTypes.Count == 0)
                return TypeMatchResult.NotFound("No floor types in the project.");

            // Find exact or closest
            FloorType exact = null, closest = null;
            double closestDiff = double.MaxValue;

            foreach (var ft in floorTypes)
            {
                double ftThick = 0;
                try
                {
                    var cs = ft.GetCompoundStructure();
                    if (cs != null) ftThick = cs.GetWidth() * Units.FeetToMm;
                }
                catch (Exception ex) { StingLog.Warn($"FloorType: {ex.Message}"); }

                double diff = Math.Abs(ftThick - thicknessMm);
                if (diff < 2.0) { exact = ft; break; }
                if (diff < closestDiff) { closestDiff = diff; closest = ft; }
            }

            if (exact != null)
                return TypeMatchResult.Found(exact.Id, "System Floor", exact.Name,
                    TypeMatchMethod.ExactMatch, 1.0);

            // Phase-97: allowDuplicate=false → skip straight to closest
            // existing floor type without duplicating.
            if (!allowDuplicate && closest != null)
            {
                _createdTypes[cacheKey] = closest.Id;
                return TypeMatchResult.Found(closest.Id, "System Floor", closest.Name,
                    TypeMatchMethod.CloseMatch, 0.6);
            }

            // Duplicate and adjust
            if (closest != null && closestDiff < 500)
            {
                string newName = $"STING Slab {thicknessMm:F0}mm";
                try
                {
                    var existing = floorTypes.FirstOrDefault(ft =>
                        ft.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        _createdTypes[cacheKey] = existing.Id;
                        return TypeMatchResult.Found(existing.Id, "System Floor", existing.Name,
                            TypeMatchMethod.ExactMatch, 0.95);
                    }

                    FloorType newFt = null;
                    using (var tx = new Transaction(_doc, "STING STRUCT: Create Floor Type"))
                    {
                        tx.Start();
                        newFt = closest.Duplicate(newName) as FloorType;
                        if (newFt != null)
                        {
                            var cs = newFt.GetCompoundStructure();
                            if (cs != null)
                            {
                                double currentWidth = cs.GetWidth();
                                double targetWidth = Units.Mm(thicknessMm);
                                if (currentWidth > 0)
                                {
                                    double ratio = targetWidth / currentWidth;
                                    for (int i = 0; i < cs.LayerCount; i++)
                                        cs.SetLayerWidth(i, cs.GetLayerWidth(i) * ratio);
                                    cs.OpeningWrapping = OpeningWrappingCondition.None;
                                    newFt.SetCompoundStructure(cs);
                                }
                            }
                        }
                        tx.Commit();
                    }

                    if (newFt != null)
                    {
                        _createdTypes[cacheKey] = newFt.Id;
                        return TypeMatchResult.Found(newFt.Id, "System Floor", newName,
                            TypeMatchMethod.DuplicatedAndSized, 0.9);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Floor type creation: {ex.Message}");
                }
            }

            if (closest != null)
                return TypeMatchResult.Found(closest.Id, "System Floor", closest.Name,
                    TypeMatchMethod.FallbackFirst, 0.5);

            return TypeMatchResult.NotFound("No suitable floor type found.");
        }


        // ── Foundation Type Resolution ───────────────────────────────────

        /// <summary>
        /// Finds or creates a foundation type matching target dimensions.
        /// </summary>
        public TypeMatchResult FindOrCreateFoundationType(
            double widthMm, double depthMm = 0, bool allowDuplicate = true)
        {
            if (depthMm <= 0) depthMm = widthMm;
            EnsureCatalog();

            string cacheKey = $"FDN_{widthMm:F0}x{depthMm:F0}";
            if (_createdTypes.TryGetValue(cacheKey, out var cachedId))
                return TypeMatchResult.Found(cachedId, "", $"Cached {widthMm}×{depthMm}", TypeMatchMethod.DuplicatedAndSized, 1.0);

            var candidates = _catalog
                .Where(e => e.Category == BuiltInCategory.OST_StructuralFoundation)
                .ToList();

            if (candidates.Count == 0)
                return TypeMatchResult.NotFound("No structural foundation families loaded.");

            var ranked = ScoreAndRank(candidates, widthMm, depthMm);
            var best = ranked.First();

            // Exact match
            if (best.entry.DimensionDistance(widthMm, depthMm) < 50)
            {
                EnsureActive(best.entry);
                return TypeMatchResult.Found(best.entry.TypeId, best.entry.FamilyName,
                    best.entry.TypeName, TypeMatchMethod.ExactMatch, best.score);
            }

            // Try to duplicate and resize (unless the caller has opted out).
            double maxDup = Math.Max(widthMm, depthMm) * RelativeDuplicateTolerance;
            if (allowDuplicate && best.entry.DimensionDistance(widthMm, depthMm) < maxDup)
            {
                var dupResult = DuplicateAndResize(best.entry, widthMm, depthMm,
                    $"{best.entry.FamilyName} {widthMm:F0}x{depthMm:F0}");
                if (dupResult.Success)
                {
                    _createdTypes[cacheKey] = dupResult.TypeId;
                    return dupResult;
                }
            }

            EnsureActive(best.entry);
            return TypeMatchResult.Found(best.entry.TypeId, best.entry.FamilyName,
                best.entry.TypeName, TypeMatchMethod.CloseMatch, best.score);
        }

        // ── Scoring & Ranking ────────────────────────────────────────────

        /// <summary>
        /// Multi-criteria weighted scoring for type matching.
        /// Weights: dimension proximity (0.7), name relevance (0.2), activation state (0.1)
        /// </summary>
        private List<(FamilyCatalogEntry entry, double score)> ScoreAndRank(
            List<FamilyCatalogEntry> candidates, double targetWidthMm, double targetDepthMm,
            double depthWeight = 1.0)
        {
            const double W_DIMENSION = 0.7;
            const double W_NAME = 0.2;
            const double W_ACTIVE = 0.1;

            var scored = new List<(FamilyCatalogEntry entry, double score)>();

            double maxDist = candidates.Max(c => c.DimensionDistance(targetWidthMm, targetDepthMm, depthWeight));
            if (maxDist < 1.0) maxDist = 1.0;

            foreach (var c in candidates)
            {
                double dist = c.DimensionDistance(targetWidthMm, targetDepthMm, depthWeight);
                double dimScore = 1.0 - Math.Min(1.0, dist / maxDist);

                // ALG-06 fix: Exclusive keyword matching (highest-priority keyword wins)
                string lowerName = (c.FamilyName + " " + c.TypeName).ToLowerInvariant();
                double nameScore;
                if (lowerName.Contains("concrete") || lowerName.Contains("rc"))
                    nameScore = 0.95;
                else if (lowerName.Contains("steel") || lowerName.Contains("stl"))
                    nameScore = 0.90;
                else if (lowerName.Contains("structural") || lowerName.Contains("str"))
                    nameScore = 0.80;
                else
                    nameScore = 0.50;

                // Activation bonus
                double activeScore = c.IsActive ? 1.0 : 0.0;

                double total = W_DIMENSION * dimScore + W_NAME * Math.Min(1.0, nameScore) +
                    W_ACTIVE * activeScore;
                scored.Add((c, total));
            }

            return scored.OrderByDescending(s => s.score).ToList();
        }

        // ── Type Duplication & Resize ────────────────────────────────────

        /// <summary>
        /// Duplicates a FamilySymbol and attempts to resize it to target dimensions.
        /// Uses parameter-based resizing for family instances (columns, beams).
        /// </summary>
        private TypeMatchResult DuplicateAndResize(FamilyCatalogEntry source,
            double targetWidthMm, double targetDepthMm, string newName)
        {
            try
            {
                var sourceSymbol = _doc.GetElement(source.TypeId) as FamilySymbol;
                if (sourceSymbol == null)
                    return TypeMatchResult.NotFound("Source symbol not found.");

                // Check if a type with this name already exists
                var existing = new FilteredElementCollector(_doc)
                    .OfCategory(source.Category)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Name.Equals(newName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    EnsureActive(new FamilyCatalogEntry { TypeId = existing.Id });
                    return TypeMatchResult.Found(existing.Id, existing.FamilyName,
                        existing.Name, TypeMatchMethod.ExactMatch, 0.95);
                }

                FamilySymbol newSymbol = null;
                using (var tx = new Transaction(_doc, "STING STRUCT: Create Type"))
                {
                    tx.Start();
                    newSymbol = sourceSymbol.Duplicate(newName) as FamilySymbol;
                    if (newSymbol != null)
                    {
                        // ACC-08 fix: Track which parameters were actually set
                        int dimsSet = 0;
                        var widthNames = new[] { "Width", "b", "B" };
                        var depthNames = new[] { "Depth", "Height", "d", "h", "D", "H" };

                        foreach (var pn in widthNames)
                            if (TrySetDimension(newSymbol, pn, targetWidthMm)) { dimsSet++; break; }
                        foreach (var pn in depthNames)
                            if (TrySetDimension(newSymbol, pn, targetDepthMm)) { dimsSet++; break; }

                        // Built-in structural section params
                        if (TrySetBuiltIn(newSymbol, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH, targetWidthMm))
                            dimsSet++;
                        if (TrySetBuiltIn(newSymbol, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT, targetDepthMm))
                            dimsSet++;

                        if (dimsSet == 0)
                            StingLog.Warn($"TypeFactory: Created '{newName}' but NO dimension parameters were set — type may be identical to source");

                        newSymbol.Activate();
                        _doc.Regenerate();
                    }
                    tx.Commit();
                }

                if (newSymbol != null)
                {
                    // Add to catalog for future lookups
                    _catalog.Add(new FamilyCatalogEntry
                    {
                        TypeId = newSymbol.Id,
                        FamilyName = newSymbol.FamilyName,
                        TypeName = newSymbol.Name,
                        Category = source.Category,
                        WidthMm = targetWidthMm,
                        DepthMm = targetDepthMm,
                        IsActive = true,
                    });

                    var result = TypeMatchResult.Found(newSymbol.Id, newSymbol.FamilyName,
                        newSymbol.Name, TypeMatchMethod.DuplicatedAndSized, 0.9);
                    result.WidthMm = targetWidthMm;
                    result.DepthMm = targetDepthMm;
                    return result;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"Type duplication failed: {ex.Message}");
            }

            return TypeMatchResult.NotFound("Duplication failed.");
        }

        private bool TrySetDimension(FamilySymbol sym, string paramName, double valueMm)
        {
            try
            {
                var p = sym.LookupParameter(paramName);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                {
                    p.Set(Units.Mm(valueMm));
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"SetDim {paramName}: {ex.Message}"); }
            return false;
        }

        private bool TrySetBuiltIn(FamilySymbol sym, BuiltInParameter bip, double valueMm)
        {
            try
            {
                var p = sym.get_Parameter(bip);
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.Double)
                {
                    p.Set(Units.Mm(valueMm));
                    return true;
                }
            }
            catch (Exception ex) { StingLog.Warn($"SetBuiltIn: {ex.Message}"); }
            return false;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private void EnsureCatalog()
        {
            if (_catalog == null) BuildCatalog();
        }

        private void EnsureActive(FamilyCatalogEntry entry)
        {
            if (!entry.IsActive)
            {
                var sym = _doc.GetElement(entry.TypeId) as FamilySymbol;
                if (sym != null && !sym.IsActive)
                {
                    using (var tx = new Transaction(_doc, "STING: Activate Symbol"))
                    {
                        tx.Start();
                        sym.Activate();
                        _doc.Regenerate();
                        tx.Commit();
                    }
                    entry.IsActive = true;
                }
            }
        }

        /// <summary>
        /// Returns a diagnostic summary of loaded structural families.
        /// </summary>
        public string GetCatalogSummary()
        {
            EnsureCatalog();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Structural Family Catalog: {_catalog.Count} types");
            sb.AppendLine();

            var groups = _catalog.GroupBy(e => e.Category).OrderBy(g => g.Key.ToString());
            foreach (var g in groups)
            {
                sb.AppendLine($"  {g.Key}: {g.Count()} types");
                foreach (var e in g.OrderBy(x => x.FamilyName).ThenBy(x => x.TypeName).Take(8))
                {
                    string dims = (e.WidthMm > 0 || e.DepthMm > 0)
                        ? $" [{e.WidthMm:F0}×{e.DepthMm:F0}mm]"
                        : e.ThicknessMm > 0 ? $" [{e.ThicknessMm:F0}mm thick]" : "";
                    sb.AppendLine($"    {e.FamilyName}: {e.TypeName}{dims}");
                }
                if (g.Count() > 8)
                    sb.AppendLine($"    ... and {g.Count() - 8} more");
            }

            return sb.ToString();
        }
    }
}
