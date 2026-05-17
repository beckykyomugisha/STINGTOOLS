// EVChargerLayoutCommand.cs — STING Phase 179 F5
//
// Places EV charger family instances beside every parking space element in
// the active document.  Parking spaces are found via the built-in
// BuiltInCategory.OST_Parking collector.  For each space the charger is
// offset 0.5 m (1.64 ft) toward the nearest face of the space bounding box
// to simulate wall-mounted placement.
//
// If no EV charger family is loaded the command reports which family name to
// load and exits with Succeeded.
//
// Workflow tag: Placement_EVCharger

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class EVChargerLayoutCommand : IExternalCommand
    {
        // Wall-offset from the parking space location point (metres → ft).
        private const double WallOffsetFt = 0.5 / 0.3048; // 0.5 m

        // Default charger parameters.
        private const string DefaultChargerType = "Mode 3 / Type 2";
        private const double DefaultChargerKw   = 7.4;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }

            var doc   = ctx.Doc;
            var uidoc = ctx.UIDoc;

            // ----------------------------------------------------------------
            // 1. Collect parking space elements.
            // ----------------------------------------------------------------
            var parkingSpaces = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Parking)
                .WhereElementIsNotElementType()
                .ToList();

            // Also accept elements that have EV_CHARGER_TYPE_TXT pre-stamped
            // (e.g. imported from a BMS export) regardless of category.
            var evPreStamped = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToList()
                .Where(e =>
                {
                    var p = e.LookupParameter("EV_CHARGER_TYPE_TXT");
                    return p != null && !string.IsNullOrWhiteSpace(p.AsString());
                })
                .ToList();

            // Combine and deduplicate.
            var allSpaces = parkingSpaces
                .Union(evPreStamped, new ElementIdComparer())
                .ToList();

            if (allSpaces.Count == 0)
            {
                TaskDialog.Show("STING EV Charger Layout",
                    "No parking space elements found in the active document.\n" +
                    "Parking spaces must be in the OST_Parking category, or carry\n" +
                    "an EV_CHARGER_TYPE_TXT parameter, for this command to work.");
                return Result.Cancelled;
            }

            // ----------------------------------------------------------------
            // 2. Find an EV charger family symbol.
            // ----------------------------------------------------------------
            var evSym = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    ContainsAny(s.Name, "EV", "Charger", "EVSE", "Electric Vehicle") ||
                    ContainsAny(s.Family?.Name ?? "", "EV", "Charger", "EVSE", "Electric Vehicle"));

            int placed   = 0;
            int skipped  = 0;
            var warnings = new List<string>();

            using (var tx = new Transaction(doc, "STING EV Charger Layout"))
            {
                tx.Start();

                if (evSym != null && !evSym.IsActive)
                {
                    try { evSym.Activate(); } catch { /* non-fatal */ }
                }

                foreach (var space in allSpaces)
                {
                    try
                    {
                        // Determine a placement point from the space location.
                        XYZ origin = null;

                        if (space.Location is LocationPoint lp)
                        {
                            origin = lp.Point;
                        }
                        else if (space.Location is LocationCurve lc)
                        {
                            origin = lc.Curve.Evaluate(0.5, true);
                        }
                        else
                        {
                            var bb = space.get_BoundingBox(null);
                            if (bb != null)
                                origin = new XYZ(
                                    (bb.Min.X + bb.Max.X) * 0.5,
                                    (bb.Min.Y + bb.Max.Y) * 0.5,
                                    bb.Min.Z);
                        }

                        if (origin == null)
                        {
                            warnings.Add($"Element {space.Id}: no locatable point — skipped.");
                            skipped++;
                            continue;
                        }

                        // Offset the charger toward the bounding box's minimum-Y
                        // face (back-wall convention for parking bays).
                        var bb2 = space.get_BoundingBox(null);
                        XYZ chargerPt;
                        if (bb2 != null)
                        {
                            // Place at the rear of the space (min Y) offset by WallOffsetFt.
                            chargerPt = new XYZ(
                                origin.X,
                                bb2.Min.Y - WallOffsetFt,
                                origin.Z);
                        }
                        else
                        {
                            // Fallback: simply offset in -Y direction.
                            chargerPt = new XYZ(origin.X, origin.Y - WallOffsetFt, origin.Z);
                        }

                        // Find the level for this space.
                        var levelId = space.LevelId;
                        Level level = levelId != ElementId.InvalidElementId
                            ? doc.GetElement(levelId) as Level
                            : null;

                        if (level == null)
                        {
                            // Fall back to nearest level by Z.
                            level = new FilteredElementCollector(doc)
                                .OfClass(typeof(Level))
                                .Cast<Level>()
                                .OrderBy(l => Math.Abs(l.Elevation - origin.Z))
                                .FirstOrDefault();
                        }

                        if (evSym == null)
                        {
                            // Can't place — just stamp parameters.
                            ParameterHelpers.SetString(space, "EV_CHARGER_TYPE_TXT", DefaultChargerType, false);
                            ParameterHelpers.SetString(space, "EV_CHARGER_KW",
                                DefaultChargerKw.ToString("F1"), false);
                            placed++;
                            continue;
                        }

                        if (level == null)
                        {
                            warnings.Add($"Element {space.Id}: no level found — skipped.");
                            skipped++;
                            continue;
                        }

                        var inst = doc.Create.NewFamilyInstance(
                            chargerPt, evSym, level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural);

                        if (inst != null)
                        {
                            ParameterHelpers.SetString(inst, "EV_CHARGER_TYPE_TXT",   DefaultChargerType, true);
                            ParameterHelpers.SetString(inst, "EV_CHARGER_KW",
                                DefaultChargerKw.ToString("F1"), true);
                            ParameterHelpers.SetString(inst, "EV_CHARGER_CIRCUIT_REF", "", false);
                            placed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        StingLog.Warn($"EVCharger element {space.Id}: {ex.Message}");
                        skipped++;
                    }
                }

                tx.Commit();
            }

            // ----------------------------------------------------------------
            // 4. Report.
            // ----------------------------------------------------------------
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Parking spaces found : {allSpaces.Count}");
            sb.AppendLine($"EV chargers placed   : {placed}");
            if (skipped > 0)
                sb.AppendLine($"Skipped              : {skipped}");

            if (evSym == null)
            {
                sb.AppendLine();
                sb.AppendLine("No EV charger family was found in the project.");
                sb.AppendLine("Parameters stamped on parking elements instead.");
                sb.AppendLine();
                sb.AppendLine("To place 3D/plan instances load a family whose name");
                sb.AppendLine("contains 'EV Charger', 'EVSE', or 'Electric Vehicle'.");
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine();
                foreach (var w in warnings.Take(8))
                    sb.AppendLine("• " + w);
                if (warnings.Count > 8)
                    sb.AppendLine($"  … and {warnings.Count - 8} more (see StingTools.log).");
            }

            TaskDialog.Show("STING EV Charger Layout", sb.ToString().TrimEnd());
            return Result.Succeeded;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static bool ContainsAny(string source, params string[] terms)
        {
            foreach (var t in terms)
                if (source.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        /// <summary>Equality comparer for deduplicating Element collections by Id.</summary>
        private sealed class ElementIdComparer : IEqualityComparer<Element>
        {
            public bool Equals(Element x, Element y) => x?.Id == y?.Id;
            public int GetHashCode(Element obj) => obj?.Id?.GetHashCode() ?? 0;
        }
    }
}
