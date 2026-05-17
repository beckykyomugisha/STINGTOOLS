// MedGasOutletPlacementCommand.cs — STING Phase 179 F6
//
// For each Room that carries a MGS_GAS_REQUIREMENT_TXT parameter the command
// reads the comma-separated list of gas codes (e.g. "O2,VAC,AIR,N2O") and
// places one medical gas outlet family instance per gas type.  Each outlet is
// positioned on the nearest wall face to the room centroid so it sits flush
// on a wall surface at 1 350 mm AFF (standard BS HTM 02-01 bedhead height).
//
// Working pressures (kPa) applied per gas code:
//   O2  = 400   N2O = 400   CO2 = 400   AIR = 400
//   VAC = -80   AGSS = -25
//
// Workflow tag: Placement_MedGasOutlets

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MedGasOutletPlacementCommand : IExternalCommand
    {
        // Mount height above finished floor (mm → ft).
        private const double MountHeightMm = 1350.0;
        private static double MmToFt(double mm) => mm / 304.8;

        // Working pressures per gas code (kPa).
        private static readonly Dictionary<string, double> GasPressureKpa =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "O2",   400  },
                { "N2O",  400  },
                { "CO2",  400  },
                { "AIR",  400  },
                { "VAC",  -80  },
                { "AGSS", -25  },
            };

        // Name fragments used to search for family symbols per gas code.
        private static readonly Dictionary<string, string[]> GasFamilyKeywords =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "O2",   new[] { "O2 Outlet", "Oxygen Outlet", "Oxygen Medical" } },
                { "N2O",  new[] { "N2O Outlet", "Nitrous Outlet", "Nitrous Oxide" } },
                { "CO2",  new[] { "CO2 Outlet", "Carbon Dioxide Outlet" } },
                { "AIR",  new[] { "Air Outlet", "Medical Air Outlet", "Compressed Air Medical" } },
                { "VAC",  new[] { "VAC Outlet", "Vacuum Outlet", "Medical Vacuum" } },
                { "AGSS", new[] { "AGSS Outlet", "Scavenging Outlet", "Gas Scavenging" } },
            };

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

            var symCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in GasFamilyKeywords)
            {
                string gasCode = kvp.Key;
                var keywords   = kvp.Value;

                var sym = allSymbols.FirstOrDefault(s =>
                    keywords.Any(kw =>
                        s.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (s.Family?.Name ?? "").IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0));

                if (sym != null)
                    symCache[gasCode] = sym;
            }

            var missingFamilies = GasFamilyKeywords.Keys
                .Where(k => !symCache.ContainsKey(k))
                .ToList();

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
                        try { s.Activate(); } catch { /* non-fatal */ }
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
                    string reqParam = room.LookupParameter("MGS_GAS_REQUIREMENT_TXT")?.AsString() ?? "";
                    var gasCodes = reqParam
                        .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(g => g.Trim().ToUpperInvariant())
                        .Distinct()
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

                        double pressure = GasPressureKpa.TryGetValue(gasCode, out double kpa)
                            ? kpa : 400.0;

                        // Determine the placement point: on nearest wall face if
                        // found, otherwise fall back to room centroid.
                        XYZ placePt = DeriveWallFacePoint(nearestWall, centroid, mountZ);

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
                            FamilyInstance inst;
                            if (nearestWall != null)
                            {
                                inst = doc.Create.NewFamilyInstance(
                                    new Reference(nearestWall),
                                    placePt,
                                    XYZ.BasisZ,
                                    sym);
                            }
                            else
                            {
                                inst = level != null
                                    ? doc.Create.NewFamilyInstance(placePt, sym, level,
                                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural)
                                    : doc.Create.NewFamilyInstance(placePt, sym,
                                        Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            }

                            if (inst != null)
                            {
                                ParameterHelpers.SetString(inst, "MGS_GAS_TYPE_TXT",  gasCode.ToUpperInvariant(), true);
                                ParameterHelpers.SetString(inst, "MGS_OUTLET_ZONE_TXT",
                                    room.LookupParameter("ZONE")?.AsString() ?? "", false);
                                ParameterHelpers.SetString(inst, "MGS_WORKING_PRESSURE_KPA",
                                    pressure.ToString("F0"), true);

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
                    sb.AppendLine($"  {mf}: load a family named '{GasFamilyKeywords[mf][0]}'");
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
                        return new XYZ(proj.XYZPoint.X, proj.XYZPoint.Y, z);
                }
            }
            catch { /* fallback */ }

            return new XYZ(centroid.X, centroid.Y, z);
        }
    }
}
