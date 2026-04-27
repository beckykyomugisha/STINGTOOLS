// StingTools v4 MVP — lighting grid calculator.
//
// Given a room, classifies it via ROOM_TYPE_CLASSIFIER.csv, looks up
// the target lux from LUX_TARGETS_EN12464.csv, derives grid spacing
// from fixture lumen output + utilisation factor + maintenance
// factor, and returns the list of XYZ placement points (room-local
// Revit feet).
//
// Calculation basis (CIBSE / IESNA lumen method):
//     N = (E_target * A) / (LUMENS_per_luminaire * UF * MF)
// where
//     E_target : target maintained lux from EN 12464-1
//     A        : room floor area (m2)
//     UF       : utilisation factor (room index + reflectance lookup)
//     MF       : maintenance factor (0.80 typical clean LED)
//     N        : required luminaire count, rounded up to grid

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace StingTools.Core.Placement
{
    public class LightingGridResult
    {
        public string RoomTypeCode { get; set; } = "";
        public double TargetLux { get; set; }
        public double RoomAreaM2 { get; set; }
        public int FixturesRequired { get; set; }
        public int FixturesPlaced { get; set; }
        public double SpacingXMm { get; set; }
        public double SpacingYMm { get; set; }
        public List<XYZ> Points { get; } = new List<XYZ>();
        public List<string> Warnings { get; } = new List<string>();

        // Phase 139.2 — extensions.
        public List<XYZ> NogginRequiredPoints { get; } = new List<XYZ>();
        public int       TileSnapAdjustments  { get; set; }
        public double    ActualUniformityRatio { get; set; }
    }

    /// <summary>
    /// Stateless grid calculator. Pure math; reads two CSV data files
    /// once per instance and caches them for subsequent calls.
    /// </summary>
    public class LightingGridCalculator
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double FtToMm = 304.8;
        private const double FtToM  = 0.3048;

        /// <summary>
        /// Per-luminaire lumen output used when no family-specific
        /// value is resolvable. 4000 lm is a reasonable default for
        /// a recessed 600x600 office LED panel.
        /// </summary>
        public double DefaultLumensPerLuminaire { get; set; } = 4000.0;

        /// <summary>
        /// Utilisation factor. 0.60 is a reasonable starting point for
        /// small-to-medium office rooms with 70/50/20 reflectances.
        /// </summary>
        public double UtilisationFactor { get; set; } = 0.60;

        /// <summary>
        /// Maintenance factor per CIBSE LG Guides. 0.80 = clean LED.
        /// </summary>
        public double MaintenanceFactor { get; set; } = 0.80;

        private readonly List<(Regex pattern, string code, double targetLuxOverride)> _classifier
            = new List<(Regex, string, double)>();
        private readonly Dictionary<string, double> _luxTargets
            = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public LightingGridCalculator()
        {
            LoadClassifier();
            LoadLuxTargets();
        }

        public LightingGridResult Compute(Room room) => Compute(room, null);

        /// <summary>
        /// Phase 139.2 — rule-aware grid compute. When the rule asks for
        /// CeilingTileSnap, StructuralFixingCheck or uniformity reporting,
        /// the corresponding post-processing pass runs after grid generation.
        /// </summary>
        public LightingGridResult Compute(Room room, PlacementRule rule)
        {
            var r = new LightingGridResult();
            if (room == null) { r.Warnings.Add("Room is null"); return r; }

            string roomName = SafeRoomName(room);
            r.RoomTypeCode = ClassifyRoom(roomName, out double overrideLux);
            r.TargetLux = overrideLux > 0
                ? overrideLux
                : (_luxTargets.TryGetValue(r.RoomTypeCode, out double lux) ? lux : 300.0);

            // Room area in Revit internal units = ft^2
            double areaFt2 = 0.0;
            try { areaFt2 = room.Area; }
            catch (Exception ex) { r.Warnings.Add($"Area read failed: {ex.Message}"); }
            r.RoomAreaM2 = areaFt2 * FtToM * FtToM;

            if (r.RoomAreaM2 <= 0.1)
            {
                r.Warnings.Add($"Room '{roomName}' has zero/tiny area; skipping.");
                return r;
            }

            double requiredFloat = (r.TargetLux * r.RoomAreaM2)
                                 / (DefaultLumensPerLuminaire * UtilisationFactor * MaintenanceFactor);
            r.FixturesRequired = (int)Math.Ceiling(requiredFloat);
            if (r.FixturesRequired < 1) r.FixturesRequired = 1;

            GenerateGridPoints(room, r);

            if (rule != null)
            {
                if (rule.CeilingTileSnap) SnapToCeilingTileGrid(room, r, rule);
                if (rule.StructuralFixingCheck) CheckStructuralFixing(room, r, rule);
                CheckSprinklerSeparation(room, r);   // Phase 139.3 — BS 5306 ≥600mm
                EnforceMinSpacing(r, rule);          // Phase 139.6 LX-1 — drop points violating MinSpacingMm
                ComputeUniformityRatio(room, r);
            }

            return r;
        }

        private void GenerateGridPoints(Room room, LightingGridResult r)
        {
            var bb = room.get_BoundingBox(null);
            if (bb == null) { r.Warnings.Add("Room has no bounding box"); return; }

            double dxFt = bb.Max.X - bb.Min.X;
            double dyFt = bb.Max.Y - bb.Min.Y;
            double levelZ = bb.Min.Z;
            if (dxFt <= 0 || dyFt <= 0) { r.Warnings.Add("Degenerate bounding box"); return; }

            // Aspect-aware grid: choose (cols x rows) such that
            // cols * rows >= FixturesRequired and cells are as square
            // as possible.
            int cols = Math.Max(1, (int)Math.Round(Math.Sqrt(r.FixturesRequired * dxFt / dyFt)));
            int rows = (int)Math.Ceiling((double)r.FixturesRequired / cols);
            if (cols < 1) cols = 1;
            if (rows < 1) rows = 1;

            double spacingX = dxFt / cols;
            double spacingY = dyFt / rows;
            r.SpacingXMm = spacingX * FtToMm;
            r.SpacingYMm = spacingY * FtToMm;

            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    double x = bb.Min.X + (i + 0.5) * spacingX;
                    double y = bb.Min.Y + (j + 0.5) * spacingY;
                    r.Points.Add(new XYZ(x, y, levelZ));
                }
            }
            r.FixturesPlaced = r.Points.Count;
        }

        private string ClassifyRoom(string roomName, out double targetLuxOverride)
        {
            targetLuxOverride = 0.0;
            if (string.IsNullOrEmpty(roomName)) return "GENERAL";
            foreach (var (rx, code, lux) in _classifier)
            {
                try
                {
                    if (rx.IsMatch(roomName))
                    {
                        targetLuxOverride = lux;
                        return code;
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"LightingGridCalculator: classifier regex failed: {ex.Message}");
                }
            }
            return "GENERAL";
        }

        private void LoadClassifier()
        {
            string path = StingToolsApp.FindDataFile("ROOM_TYPE_CLASSIFIER.csv");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                // Fallback: always-GENERAL classifier so the calculator
                // still returns a sensible default.
                return;
            }
            try
            {
                var lines = File.ReadAllLines(path);
                bool first = true;
                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                    if (first) { first = false; continue; } // header
                    var cols = StingToolsApp.ParseCsvLine(raw);
                    if (cols == null || cols.Length < 3) continue;
                    string patt = cols[0];
                    string code = cols[1];
                    double lux = 0.0;
                    double.TryParse(cols[2], System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out lux);
                    if (string.IsNullOrEmpty(patt) || string.IsNullOrEmpty(code)) continue;
                    Regex rx;
                    try { rx = new Regex(patt, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"LightingGridCalculator: invalid classifier regex '{patt}': {ex.Message}");
                        continue;
                    }
                    _classifier.Add((rx, code, lux));
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LightingGridCalculator: loading ROOM_TYPE_CLASSIFIER.csv failed: {ex.Message}");
            }
        }

        private void LoadLuxTargets()
        {
            string path = StingToolsApp.FindDataFile("LUX_TARGETS_EN12464.csv");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var lines = File.ReadAllLines(path);
                bool first = true;
                foreach (var raw in lines)
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                    if (first) { first = false; continue; }
                    var cols = StingToolsApp.ParseCsvLine(raw);
                    if (cols == null || cols.Length < 2) continue;
                    string code = cols[0];
                    if (!double.TryParse(cols[1], System.Globalization.NumberStyles.Any,
                                         System.Globalization.CultureInfo.InvariantCulture,
                                         out double lux)) continue;
                    _luxTargets[code] = lux;
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LightingGridCalculator: loading LUX_TARGETS_EN12464.csv failed: {ex.Message}");
            }
        }

        private string SafeRoomName(Room room)
        {
            try
            {
                var p = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                return p?.AsString() ?? room.Name ?? "";
            }
            catch { return ""; }
        }

        // ── Phase 139.2 — post-processing passes ────────────────────

        private void SnapToCeilingTileGrid(Room room, LightingGridResult r, PlacementRule rule)
        {
            try
            {
                double tileXMm = rule.TileGridSpacingXMm > 0 ? rule.TileGridSpacingXMm : 600.0;
                double tileYMm = rule.TileGridSpacingYMm > 0 ? rule.TileGridSpacingYMm : 600.0;

                Document doc = room?.Document;
                if (doc == null) return;

                var ceilingTypeNames = new List<string>();
                XYZ tileOrigin = null;

                try
                {
                    var bb = room.get_BoundingBox(null);
                    if (bb != null)
                    {
                        var pad = 0.5;
                        var outline = new Outline(
                            new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                            new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + pad));
                        var bbf = new BoundingBoxIntersectsFilter(outline);
                        foreach (var el in new FilteredElementCollector(doc)
                            .OfCategory(BuiltInCategory.OST_Ceilings)
                            .WhereElementIsNotElementType()
                            .WherePasses(bbf))
                        {
                            var ct = doc.GetElement(el.GetTypeId()) as ElementType;
                            string typeName = ct?.Name ?? "";
                            if (!string.IsNullOrEmpty(typeName)) ceilingTypeNames.Add(typeName);
                            if (tileOrigin == null)
                            {
                                var bb2 = el.get_BoundingBox(null);
                                if (bb2 != null) tileOrigin = bb2.Min;
                            }
                        }
                    }
                }
                catch { }

                bool nameSuggestsTiles = ceilingTypeNames.Any(n =>
                    n.IndexOf("600x600", StringComparison.OrdinalIgnoreCase) >= 0
                 || n.IndexOf("600 x 600", StringComparison.OrdinalIgnoreCase) >= 0
                 || n.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0
                 || n.IndexOf("Tile", StringComparison.OrdinalIgnoreCase) >= 0);

                // Fall back to room bbox if no ceiling found.
                if (tileOrigin == null)
                {
                    var bb = room.get_BoundingBox(null);
                    if (bb == null) return;
                    tileOrigin = bb.Min;
                }

                double stepX = tileXMm * MmToFt;
                double stepY = tileYMm * MmToFt;

                int adjustments = 0;
                for (int i = 0; i < r.Points.Count; i++)
                {
                    var p = r.Points[i];
                    double snappedX = tileOrigin.X + Math.Round((p.X - tileOrigin.X) / stepX) * stepX + stepX * 0.5;
                    double snappedY = tileOrigin.Y + Math.Round((p.Y - tileOrigin.Y) / stepY) * stepY + stepY * 0.5;
                    var snapped = new XYZ(snappedX, snappedY, p.Z);
                    if (snapped.DistanceTo(p) > 50.0 * MmToFt) adjustments++;
                    r.Points[i] = snapped;
                }
                r.TileSnapAdjustments = adjustments;
                if (nameSuggestsTiles)
                    StingLog.Info($"LightingGridCalculator: tile snap applied ({adjustments} adjustments) to room {room.Id}.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LightingGridCalculator.SnapToCeilingTileGrid: {ex.Message}");
            }
        }

        private void CheckStructuralFixing(Room room, LightingGridResult r, PlacementRule rule)
        {
            try
            {
                Document doc = room?.Document;
                if (doc == null) return;
                double clearanceFt = (rule.JoistClearanceMm > 0 ? rule.JoistClearanceMm : 300.0) * MmToFt;
                double clearanceSqFt = clearanceFt * clearanceFt;

                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                double pad = clearanceFt + 1.0;
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + 5.0));
                var bbf = new BoundingBoxIntersectsFilter(outline);

                var joists = new List<XYZ>();
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbf))
                {
                    var fbb = el.get_BoundingBox(null);
                    if (fbb == null) continue;
                    joists.Add((fbb.Min + fbb.Max) * 0.5);
                }

                foreach (var p in r.Points)
                {
                    bool foundJoist = false;
                    foreach (var j in joists)
                    {
                        double dx = j.X - p.X, dy = j.Y - p.Y;
                        if (dx * dx + dy * dy <= clearanceSqFt) { foundJoist = true; break; }
                    }
                    if (!foundJoist && rule.EmitNogginRequirement)
                    {
                        r.NogginRequiredPoints.Add(p);
                        StingLog.Info($"LightingGridCalculator: noggin required at {p.X:F2},{p.Y:F2} room {room.Id}.");
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LightingGridCalculator.CheckStructuralFixing: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase 139.3 — BS 5306-2 / BS EN 12845 require ≥ 600 mm clear
        /// horizontal distance between a sprinkler head and any obstruction
        /// to spray pattern (lighting fixtures count). Push grid points
        /// outside the 600 mm exclusion ring of every sprinkler in scope;
        /// when no clear nudge is possible, drop the point and warn.
        /// </summary>
        private void CheckSprinklerSeparation(Room room, LightingGridResult r)
        {
            try
            {
                Document doc = room?.Document;
                if (doc == null || r.Points.Count == 0) return;
                const double minSepFt = 600.0 / 304.8;
                double minSepSq = minSepFt * minSepFt;

                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                double pad = minSepFt + 0.5;
                var outline = new Outline(
                    new XYZ(bb.Min.X - pad, bb.Min.Y - pad, bb.Min.Z - pad),
                    new XYZ(bb.Max.X + pad, bb.Max.Y + pad, bb.Max.Z + 5.0));
                var bbf = new BoundingBoxIntersectsFilter(outline);

                var sprinklers = new List<XYZ>();
                foreach (var el in new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Sprinklers)
                    .WhereElementIsNotElementType()
                    .WherePasses(bbf))
                {
                    XYZ p = (el.Location as LocationPoint)?.Point;
                    if (p != null) sprinklers.Add(p);
                }
                if (sprinklers.Count == 0) return;

                // Phase 139.5 Q13 — nudge each conflicting point along the
                // axis away from the nearest sprinkler. If nudging keeps it
                // inside the room bbox AND clear of every other sprinkler,
                // accept the nudge. Drop only as last resort so the lux grid
                // doesn't lose fixture count to false rejection.
                var bbRoom = room.get_BoundingBox(null);
                int nudged = 0, dropped = 0;
                var keep = new List<XYZ>();
                foreach (var pt in r.Points)
                {
                    XYZ nearest = null; double nearestSq = double.MaxValue;
                    foreach (var s in sprinklers)
                    {
                        double dx = s.X - pt.X, dy = s.Y - pt.Y;
                        double d2 = dx * dx + dy * dy;
                        if (d2 < nearestSq) { nearestSq = d2; nearest = s; }
                    }
                    if (nearest == null || nearestSq >= minSepSq) { keep.Add(pt); continue; }

                    XYZ away = pt - nearest;
                    if (away.GetLength() < 1e-6) away = new XYZ(1, 0, 0);
                    away = away.Normalize();
                    XYZ nudgedPt = nearest + away.Multiply(minSepFt + 0.05);

                    bool inRoom = bbRoom == null
                        || (nudgedPt.X >= bbRoom.Min.X && nudgedPt.X <= bbRoom.Max.X
                         && nudgedPt.Y >= bbRoom.Min.Y && nudgedPt.Y <= bbRoom.Max.Y);
                    bool clearOfOthers = true;
                    if (inRoom)
                    {
                        foreach (var s in sprinklers)
                        {
                            double dx = s.X - nudgedPt.X, dy = s.Y - nudgedPt.Y;
                            if (dx * dx + dy * dy < minSepSq) { clearOfOthers = false; break; }
                        }
                    }
                    if (inRoom && clearOfOthers)
                    {
                        keep.Add(new XYZ(nudgedPt.X, nudgedPt.Y, pt.Z));
                        nudged++;
                    }
                    else
                    {
                        dropped++;
                    }
                }

                if (nudged > 0 || dropped > 0)
                {
                    r.Points.Clear();
                    r.Points.AddRange(keep);
                    r.FixturesPlaced = r.Points.Count;
                    if (nudged > 0)
                        r.Warnings.Add($"BS 5306-2: nudged {nudged} luminaire grid point(s) away from sprinklers.");
                    if (dropped > 0)
                        r.Warnings.Add($"BS 5306-2: dropped {dropped} luminaire grid point(s) — no clear position within room.");
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LightingGridCalculator.CheckSprinklerSeparation: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase 139.6 LX-1 — drop grid points that fall closer than
        /// rule.MinSpacingMm to a previously kept point. The lumen-method
        /// formula and ceiling-tile snap can both produce two points
        /// inside the minimum-spacing radius, especially in narrow rooms;
        /// this pass is the last word before placement.
        /// </summary>
        private void EnforceMinSpacing(LightingGridResult r, PlacementRule rule)
        {
            try
            {
                if (rule == null || rule.MinSpacingMm <= 0) return;
                if (r.Points == null || r.Points.Count < 2) return;
                double minFt = rule.MinSpacingMm * MmToFt;
                double minSq = minFt * minFt;

                var keep = new List<XYZ>();
                int dropped = 0;
                foreach (var pt in r.Points)
                {
                    bool tooClose = false;
                    foreach (var k in keep)
                    {
                        double dx = k.X - pt.X, dy = k.Y - pt.Y;
                        if (dx * dx + dy * dy < minSq) { tooClose = true; break; }
                    }
                    if (tooClose) dropped++;
                    else keep.Add(pt);
                }
                if (dropped > 0)
                {
                    r.Points.Clear();
                    r.Points.AddRange(keep);
                    r.FixturesPlaced = r.Points.Count;
                    r.Warnings.Add($"MinSpacing: dropped {dropped} luminaire grid point(s) closer than {rule.MinSpacingMm:F0}mm.");
                }
            }
            catch (Exception ex) { StingLog.Warn($"LightingGridCalculator.EnforceMinSpacing: {ex.Message}"); }
        }

        private void ComputeUniformityRatio(Room room, LightingGridResult r)
        {
            try
            {
                if (r.Points == null || r.Points.Count == 0) return;
                var bb = room.get_BoundingBox(null);
                if (bb == null) return;
                double areaFt2 = (bb.Max.X - bb.Min.X) * (bb.Max.Y - bb.Min.Y);
                if (areaFt2 <= 0) return;

                // Cap sample density when fixture count is high — uniformity
                // surface converges quickly and 8×8 × 30+ fixtures dominates
                // per-room runtime in large open-plan offices.
                int samplesX = (r.FixturesRequired > 30) ? 4 : 8;
                int samplesY = samplesX;
                double minE = double.MaxValue, sumE = 0; int n = 0;
                double illumPerLumPerFt2 = DefaultLumensPerLuminaire * UtilisationFactor * MaintenanceFactor / 1000.0;
                for (int j = 0; j < samplesY; j++)
                for (int i = 0; i < samplesX; i++)
                {
                    double sx = bb.Min.X + (i + 0.5) * (bb.Max.X - bb.Min.X) / samplesX;
                    double sy = bb.Min.Y + (j + 0.5) * (bb.Max.Y - bb.Min.Y) / samplesY;
                    double E = 0.0;
                    foreach (var p in r.Points)
                    {
                        double dx = sx - p.X, dy = sy - p.Y;
                        double d2 = Math.Max(dx * dx + dy * dy, 0.5);
                        E += illumPerLumPerFt2 / d2;
                    }
                    if (E < minE) minE = E;
                    sumE += E; n++;
                }
                if (n == 0) return;
                double avgE = sumE / n;
                if (avgE <= 0) return;
                double uniformity = minE / avgE;
                r.ActualUniformityRatio = uniformity;
                if (uniformity < 0.40)
                    r.Warnings.Add($"Uniformity Uo={uniformity:F2} below BS EN 12464-1 0.40 minimum for general areas.");
            }
            catch (Exception ex)
            {
                StingLog.Warn($"LightingGridCalculator.ComputeUniformityRatio: {ex.Message}");
            }
        }
    }
}
