using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Electrical.Photometric
{
    /// <summary>
    /// Pre-flight gate before any photometric round-trip. Catches the
    /// common "garbage in" failures that ruin DIALux / ElumTools / Relux
    /// imports:
    ///   • Lighting fixtures with no IES/LDT path bound
    ///   • Rooms with no surface reflectances assigned
    ///   • Rooms with zero area / unplaced
    ///   • Lighting fixtures placed outside any room boundary
    /// Read-only (no model writes); produces a TaskDialog summary.
    /// Differentiator #3 from the Phase 180/181 research report.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PhotometricPreflightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { message = "No active document."; return Result.Failed; }
            var doc = ctx.Doc;

            // LIGHTGRID-2: Rooms AND MEP Spaces. Note Collect() filters Area > 0, and the
            // unplaced-room count below deliberately wants the UNFILTERED set, so it uses
            // its own collector rather than this one.
            var rooms = StingTools.Core.Placement.SpatialCompat.Collect(doc);
            var fixtures = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_LightingFixtures)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>().ToList();

            // LIGHTGRID-2: `rooms` is filtered to Area > 0, so counting zero-area entries
            // in it would always be 0 - a preflight check that can never fire. The whole
            // point of this line is the UNPLACED ones, so it needs its own unfiltered pass.
            int unplacedRooms = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>
                    { BuiltInCategory.OST_Rooms, BuiltInCategory.OST_MEPSpaces }))
                .WhereElementIsNotElementType()
                .OfType<SpatialElement>()
                .Count(r => r.Area <= 0);
            var roomsNoRefl = rooms.Where(r => r.Area > 0
                && string.IsNullOrEmpty(GetReflectance(r))).ToList();
            int fixturesNoFile = 0;
            int fixturesOutsideRoom = 0;
            foreach (var fi in fixtures)
            {
                var sym = doc.GetElement(fi.GetTypeId());
                string photoPath = sym != null
                    ? ParameterHelpers.GetString(sym, ParamRegistry.ELC_PHOTO_FILE_PATH)
                    : "";
                if (string.IsNullOrEmpty(photoPath)) fixturesNoFile++;
                SpatialElement hostRoom = null;   // LIGHTGRID-2
                try { hostRoom = StingTools.Core.Placement.SpatialCompat.SpatialOf(fi); } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                if (hostRoom == null) fixturesOutsideRoom++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("PHOTOMETRIC PRE-FLIGHT");
            sb.AppendLine();
            sb.AppendLine($"Lighting fixtures: {fixtures.Count}");
            sb.AppendLine($"  • without bound IES/LDT/GLDF file: {fixturesNoFile}");
            sb.AppendLine($"  • placed outside any room boundary: {fixturesOutsideRoom}");
            sb.AppendLine();
            sb.AppendLine($"Rooms: {rooms.Count}");
            sb.AppendLine($"  • unplaced / zero area: {unplacedRooms}");
            sb.AppendLine($"  • without surface reflectance: {roomsNoRefl.Count}");
            sb.AppendLine();
            int totalIssues = fixturesNoFile + fixturesOutsideRoom + unplacedRooms + roomsNoRefl.Count;
            sb.AppendLine(totalIssues == 0
                ? "✅ No issues found — model is ready for DIALux / ElumTools / Relux export."
                : $"⚠ {totalIssues} issue(s) — review before exporting for photometric calculation.");

            int top = Math.Min(8, roomsNoRefl.Count);
            if (top > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Rooms missing reflectance (first 8):");
                for (int i = 0; i < top; i++)
                    sb.AppendLine($"  • {roomsNoRefl[i].Name}");
                if (roomsNoRefl.Count > top)
                    sb.AppendLine($"  …and {roomsNoRefl.Count - top} more");
            }

            TaskDialog.Show("STING Photometric Pre-Flight", sb.ToString());
            return Result.Succeeded;
        }

        private static string GetReflectance(SpatialElement r)   // LIGHTGRID-2
        {
            // Try the conventional reflectance parameter names used by the
            // commercial lighting tools (DIALux ULD / ElumTools / Relux).
            var names = new[]
            {
                "Ceiling Reflectance", "Wall Reflectance", "Floor Reflectance",
                "ELC_REFL_CEILING", "ELC_REFL_WALL", "ELC_REFL_FLOOR"
            };
            foreach (var n in names)
            {
                try
                {
                    var p = r.LookupParameter(n);
                    if (p == null) continue;
                    if (p.StorageType == StorageType.Double && p.AsDouble() > 0) return n;
                    if (p.StorageType == StorageType.String && !string.IsNullOrEmpty(p.AsString())) return n;
                }
                catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            }
            return "";
        }
    }
}
