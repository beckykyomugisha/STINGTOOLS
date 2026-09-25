// MedGasOutletPlacementCommand.cs — STING Phase 179 F6
//
// For each Room that carries a MGS_GAS_REQUIREMENT_TXT parameter the command
// reads the comma-separated list of gas codes (e.g. "O2,VAC,MA4,N2O") and
// places one medical gas outlet family instance per gas type.  Each outlet is
// positioned on the face of the nearest wall to the room centroid, at
// 1 350 mm AFF (standard BS HTM 02-01 bedhead height).
//
// Gas codes are the MGS_GAS_TYPE_TXT vocabulary (O2 MA4 MA7 N2O N2 CO2 HE VAC
// AGS); common aliases (AIR, AGSS, SURGAIR, …) are mapped onto it by
// MedicalGasFixtures.CanonicalGasCode, and an unknown code is reported, not
// placed. Working pressures come from NFPA99Standards.NominalGasPressureKPa —
// the same table MgasNetwork reads.
//
// Families: a type whose MGS_GAS_TYPE_TXT equals the gas wins (the STING seed's
// TERMINAL_UNIT_* types carry it); otherwise a family/type name keyword. The
// placement follows the family: face-based on the wall face, wall-hosted in the
// wall, anything else free-standing at the wall's face.
//
// Workflow tag: Placement_MedGasOutlets

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Plumbing;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MedGasOutletPlacementCommand : IExternalCommand
    {
        // Mount height above finished floor (mm → ft).
        private const double MountHeightMm = 1350.0;
        private static double MmToFt(double mm) => mm / 304.8;

        // Name fragments used when no type carries the gas in MGS_GAS_TYPE_TXT.
        // The TERMINAL_UNIT_* entries are the STING seed's own type names.
        private static readonly Dictionary<string, string[]> GasFamilyKeywords =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "O2",  new[] { "TERMINAL_UNIT_O2", "O2 Outlet", "Oxygen Outlet", "Oxygen Medical" } },
                { "N2O", new[] { "TERMINAL_UNIT_N2O", "N2O Outlet", "Nitrous Outlet", "Nitrous Oxide" } },
                { "CO2", new[] { "TERMINAL_UNIT_CO2", "CO2 Outlet", "Carbon Dioxide Outlet" } },
                { "MA4", new[] { "TERMINAL_UNIT_MEDAIR", "Medical Air Outlet", "Compressed Air Medical", "MA4 Outlet" } },
                { "MA7", new[] { "TERMINAL_UNIT_SURGAIR", "Surgical Air Outlet", "MA7 Outlet" } },
                { "N2",  new[] { "TERMINAL_UNIT_N2", "Nitrogen Outlet" } },
                { "HE",  new[] { "TERMINAL_UNIT_HELIOX", "Heliox Outlet" } },
                { "VAC", new[] { "TERMINAL_UNIT_VAC", "VAC Outlet", "Vacuum Outlet", "Medical Vacuum" } },
                { "AGS", new[] { "AGSS Outlet", "Scavenging Outlet", "Gas Scavenging" } },
            };

        private static double PressureKpa(string gas)
            => StingTools.Standards.NFPA99.NFPA99Standards.NominalGasPressureKPa.TryGetValue(gas, out double kpa) ? kpa : double.NaN;

        /// <summary>
        /// The symbol for one gas: first a type that carries the gas as a TYPE parameter (some
        /// manufacturer families do); then a keyword match. STING's own seed never matches the
        /// first step — its MGS_GAS_TYPE_TXT is an instance parameter a FamilySymbol cannot
        /// read (ROADMAP MG-1) — so it is found by its TERMINAL_UNIT_* type names, exact only.
        /// Keywords are matched in order and whole-name-first, so "TERMINAL_UNIT_N2" does
        /// not pick up TERMINAL_UNIT_N2O.
        /// </summary>
        private static FamilySymbol FindSymbol(IList<FamilySymbol> symbols, string gas)
        {
            var stamped = symbols.FirstOrDefault(s =>
            {
                try
                {
                    // Outlets only: pipe fittings and accessories carry the gas type too.
                    if (!IsOutletCategory(s)) return false;
                    var p = s.LookupParameter("MGS_GAS_TYPE_TXT");
                    return p != null && string.Equals((p.AsString() ?? "").Trim(), gas, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex) { StingLog.Warn($"MedGasOutlet type gas read: {ex.Message}"); return false; }
            });
            if (stamped != null) return stamped;

            foreach (var kw in GasFamilyKeywords[gas])
            {
                var exact = symbols.FirstOrDefault(s => IsOutletCategory(s)
                    && string.Equals(s.Name, kw, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;
                var partial = symbols.FirstOrDefault(s => IsOutletCategory(s) &&
                    (s.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (s.Family?.Name ?? "").IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0));
                if (partial != null && !kw.StartsWith("TERMINAL_UNIT_", StringComparison.Ordinal)) return partial;
            }
            return null;
        }

        // Categories a gas outlet or outlet panel is modelled in (STING's seed is a
        // Plumbing Fixture; manufacturer outlets also ship as Specialty / Medical /
        // Mechanical Equipment). Pipe fittings and accessories carry MGS_GAS_TYPE_TXT
        // too and must never be picked as an outlet.
        private static readonly HashSet<long> OutletCategoryIds = new HashSet<long>
        {
            (long)BuiltInCategory.OST_PlumbingFixtures,
            (long)BuiltInCategory.OST_SpecialityEquipment,
            (long)BuiltInCategory.OST_MedicalEquipment,
            (long)BuiltInCategory.OST_MechanicalEquipment,
        };

        private static bool IsOutletCategory(FamilySymbol s)
        {
            try { return s?.Category != null && OutletCategoryIds.Contains(s.Category.Id.Value); }
            catch (Exception ex) { StingLog.Warn($"MedGasOutlet category read: {ex.Message}"); return false; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // ----------------------------------------------------------------
            // 1. Collect rooms that have a gas requirement parameter.
            // ----------------------------------------------------------------
            var rooms = new FilteredElementCollector(doc)
                .OfClass(typeof(SpatialElement))
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r =>
                {
                    var p = r.LookupParameter("MGS_GAS_REQUIREMENT_TXT");
                    return p != null && !string.IsNullOrWhiteSpace(p.AsString());
                })
                .ToList();

            if (rooms.Count == 0)
            {
                TaskDialog.Show("STING Med Gas Outlets",
                    "No rooms with a 'MGS_GAS_REQUIREMENT_TXT' parameter found.\n\n" +
                    "Set this parameter on each clinical room to a comma-separated\n" +
                    "list of gas codes, e.g.:  O2,VAC,AIR,N2O");
                return Result.Cancelled;
            }

            // ----------------------------------------------------------------
            // 2. Pre-cache available family symbols (one lookup per gas code).
            // ----------------------------------------------------------------
            var allSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .ToList();

            // Only the gases some room asks for — a gas nobody needs is not a missing family.
            var unknownCodes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var room in rooms)
                foreach (var raw in GasCodesOf(room))
                {
                    var gas = MedicalGasFixtures.CanonicalGasCode(raw);
                    if (gas == null) unknownCodes.Add(raw); else wanted.Add(gas);
                }

            var symCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var gas in wanted)
            {
                var sym = FindSymbol(allSymbols, gas);
                if (sym != null) symCache[gas] = sym;
            }

            var missingFamilies = wanted.Where(k => !symCache.ContainsKey(k)).OrderBy(k => k).ToList();

            // ----------------------------------------------------------------
            // 3. Activate all found symbols in a single short transaction.
            // ----------------------------------------------------------------
            var toActivate = symCache.Values.Where(s => !s.IsActive).ToList();
            if (toActivate.Count > 0)
            {
                using (var txAct = new Transaction(doc, "STING Activate MedGas Symbols"))
                {
                    txAct.Start();
                    foreach (var s in toActivate)
                    {
                        try { s.Activate(); }
                        catch (Exception ex) { StingLog.Warn($"MedGasOutlet activate {s.Name}: {ex.Message}"); }
                    }
                    txAct.Commit();
                }
            }

            // ----------------------------------------------------------------
            // 4. Collect walls for nearest-face lookup (lightweight by level).
            // ----------------------------------------------------------------
            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .ToList();

            // ----------------------------------------------------------------
            // 5. Place outlets.
            // ----------------------------------------------------------------
            var placedCounts  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var warningList   = new List<string>();
            int totalPlaced   = 0;
            int totalSkipped  = 0;

            double mountZ_offset = MmToFt(MountHeightMm);

            using (var tx = new Transaction(doc, "STING Place Med Gas Outlets"))
            {
                tx.Start();

                foreach (var room in rooms)
                {
                    var gasCodes = GasCodesOf(room)
                        .Select(MedicalGasFixtures.CanonicalGasCode)
                        .Where(g => g != null)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (gasCodes.Count == 0) continue;

                    // Room centroid.
                    LocationPoint roomLoc = room.Location as LocationPoint;
                    XYZ centroid = roomLoc?.Point;
                    if (centroid == null)
                    {
                        warningList.Add($"Room '{room.Name}' (Id {room.Id}): no location point — skipped.");
                        totalSkipped += gasCodes.Count;
                        continue;
                    }

                    // Level Z for mount height calculation.
                    var level = doc.GetElement(room.LevelId) as Level;
                    double levelZ = level?.Elevation ?? centroid.Z;
                    double mountZ = levelZ + mountZ_offset;

                    // Nearest wall to the room centroid on the same level.
                    Wall nearestWall = FindNearestWall(walls, centroid, level);

                    foreach (var gasCode in gasCodes)
                    {
                        symCache.TryGetValue(gasCode, out FamilySymbol sym);

                        double pressure = PressureKpa(gasCode);

                        if (sym == null)
                        {
                            warningList.Add(
                                $"Room '{room.Name}': gas '{gasCode}' — no matching family loaded. " +
                                $"Load a family containing '{string.Join("' or '", GasFamilyKeywords.TryGetValue(gasCode, out var kws) ? kws : new[] { gasCode })}' and re-run.");
                            totalSkipped++;
                            continue;
                        }

                        try
                        {
                            FamilyInstance inst = PlaceOutlet(doc, sym, nearestWall, level, centroid, mountZ);

                            if (inst != null)
                            {
                                ParameterHelpers.SetString(inst, "MGS_GAS_TYPE_TXT", gasCode, true);
                                ParameterHelpers.SetString(inst, "MGS_OUTLET_ZONE_TXT",
                                    ParameterHelpers.GetString(room, ParamRegistry.ZONE), false);
                                if (!double.IsNaN(pressure))
                                    ParameterHelpers.SetString(inst, "MGS_WORKING_PRESSURE_KPA",
                                        pressure.ToString("F0", System.Globalization.CultureInfo.InvariantCulture), true);

                                if (!placedCounts.ContainsKey(gasCode))
                                    placedCounts[gasCode] = 0;
                                placedCounts[gasCode]++;
                                totalPlaced++;
                            }
                        }
                        catch (Exception ex)
                        {
                            StingLog.Warn(
                                $"MedGasOutlet room {room.Id} gas {gasCode}: {ex.Message}");
                            totalSkipped++;
                        }
                    }
                }

                tx.Commit();
            }

            // ----------------------------------------------------------------
            // 6. Build result report.
            // ----------------------------------------------------------------
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Rooms processed     : {rooms.Count}");
            sb.AppendLine($"Outlets placed      : {totalPlaced}");
            if (totalSkipped > 0)
                sb.AppendLine($"Skipped / warnings  : {totalSkipped}");

            if (placedCounts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Placed by gas type:");
                foreach (var kvp in placedCounts.OrderBy(k => k.Key))
                    sb.AppendLine($"  {kvp.Key,-6}: {kvp.Value}");
            }

            if (missingFamilies.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Families not loaded (outlets NOT placed for these gases):");
                foreach (var mf in missingFamilies)
                    sb.AppendLine($"  {mf}: load the STING medical-gas seed, or a family named '{GasFamilyKeywords[mf][1]}'");
            }

            if (unknownCodes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Unrecognised gas codes (NOT placed): " + string.Join(", ", unknownCodes));
                sb.AppendLine("  Use " + string.Join(" / ", MedicalGasFixtures.GasCodes) + ".");
            }

            if (warningList.Count > 0)
            {
                sb.AppendLine();
                foreach (var w in warningList.Take(8))
                    sb.AppendLine("• " + w);
                if (warningList.Count > 8)
                    sb.AppendLine($"  … and {warningList.Count - 8} more (see StingTools.log).");
            }

            TaskDialog.Show("STING Medical Gas Outlet Placement", sb.ToString().TrimEnd());
            return Result.Succeeded;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Finds the nearest Wall to <paramref name="centroid"/> that is
        /// associated with the given level (null level = any level).
        /// </summary>
        private static Wall FindNearestWall(IList<Wall> walls, XYZ centroid, Level level)
        {
            Wall nearest = null;
            double nearestDist = double.MaxValue;

            foreach (var w in walls)
            {
                try
                {
                    // Optional level filter — keeps outlets on the correct storey.
                    if (level != null && w.LevelId != level.Id) continue;

                    if (w.Location is LocationCurve lc)
                    {
                        var pt = lc.Curve.Project(centroid);
                        double d = pt.Distance;
                        if (d < nearestDist)
                        {
                            nearestDist = d;
                            nearest = w;
                        }
                    }
                }
                catch { /* skip inaccessible walls */ }
            }

            return nearest;
        }

        /// <summary>
        /// Derives the placement point on the face of <paramref name="wall"/>
        /// closest to <paramref name="centroid"/>, at the given <paramref name="z"/>.
        /// Falls back to <paramref name="centroid"/> with the new Z if wall is null.
        /// </summary>
        private static XYZ DeriveWallFacePoint(Wall wall, XYZ centroid, double z)
        {
            if (wall == null)
                return new XYZ(centroid.X, centroid.Y, z);

            try
            {
                if (wall.Location is LocationCurve lc)
                {
                    var proj = lc.Curve.Project(centroid);
                    if (proj != null)
                    {
                        // The location line is the wall's centre: step half the wall's
                        // thickness toward the room so the outlet sits on the room face,
                        // not buried in the wall.
                        var onLine = new XYZ(proj.XYZPoint.X, proj.XYZPoint.Y, z);
                        var toRoom = new XYZ(centroid.X - onLine.X, centroid.Y - onLine.Y, 0);
                        if (toRoom.GetLength() > 1e-6)
                            onLine += toRoom.Normalize() * (wall.Width / 2.0);
                        return onLine;
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"MedGasOutlet wall face point: {ex.Message}"); }

            return new XYZ(centroid.X, centroid.Y, z);
        }

        private static IEnumerable<string> GasCodesOf(Room room)
            => (room.LookupParameter("MGS_GAS_REQUIREMENT_TXT")?.AsString() ?? "")
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(g => g.Trim())
                .Where(g => g.Length > 0);

        /// <summary>
        /// Places one outlet the way its family allows. The old code always used the
        /// face-reference overload with an element (not face) reference, which throws
        /// for every family that is not face-based — including STING's own seed.
        /// </summary>
        private static FamilyInstance PlaceOutlet(Document doc, FamilySymbol sym, Wall wall, Level level, XYZ centroid, double z)
        {
            var nonStructural = Autodesk.Revit.DB.Structure.StructuralType.NonStructural;
            XYZ pt = DeriveWallFacePoint(wall, centroid, z);
            var placement = sym.Family?.FamilyPlacementType ?? FamilyPlacementType.OneLevelBased;

            if (wall != null && placement == FamilyPlacementType.WorkPlaneBased)
            {
                var face = RoomSideFace(doc, wall, centroid);
                if (face.Face != null && wall.Location is LocationCurve lc)
                {
                    var onFace = face.Face.Project(pt)?.XYZPoint ?? pt;
                    var along = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize();
                    return doc.Create.NewFamilyInstance(face.Ref, onFace, along, sym);
                }
            }
            if (wall != null && placement == FamilyPlacementType.OneLevelBasedHosted && level != null)
                return doc.Create.NewFamilyInstance(pt, sym, wall, level, nonStructural);

            return level != null
                ? doc.Create.NewFamilyInstance(pt, sym, level, nonStructural)
                : doc.Create.NewFamilyInstance(pt, sym, nonStructural);
        }

        /// <summary>The wall's side face nearer the room centroid.</summary>
        private static (Reference Ref, Face Face) RoomSideFace(Document doc, Wall wall, XYZ centroid)
        {
            (Reference, Face) best = (null, null);
            double bestD = double.MaxValue;
            foreach (var side in new[] { ShellLayerType.Interior, ShellLayerType.Exterior })
            {
                try
                {
                    foreach (var r in HostObjectUtils.GetSideFaces(wall, side))
                    {
                        if (!(wall.GetGeometryObjectFromReference(r) is Face f)) continue;
                        var proj = f.Project(centroid);
                        double d = proj?.Distance ?? double.MaxValue;
                        if (d < bestD) { bestD = d; best = (r, f); }
                    }
                }
                catch (Exception ex) { StingLog.Warn($"MedGasOutlet side face {side}: {ex.Message}"); }
            }
            return best;
        }
    }
}
