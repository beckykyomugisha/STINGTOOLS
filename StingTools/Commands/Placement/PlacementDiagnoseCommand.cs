// Phase 139.19 — Placement diagnostic. Walks the active document and
// the loaded rule pack and prints every fact the placement engine would
// see at run time. Use when "the engine isn't placing things where I
// expect" to pinpoint the real cause: wrong family placement type,
// missing FromRoom/ToRoom, host-wall mismatch, or stale plug-in build.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Placement;

namespace StingTools.Commands.Placement
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class PlacementDiagnoseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cd, ref string message, ElementSet elements)
        {
            var doc = cd?.Application?.ActiveUIDocument?.Document;
            if (doc == null) { message = "No active document."; return Result.Failed; }

            var sb = new StringBuilder();
            sb.AppendLine($"Build: STING placement diagnostic — Phase 139.19 ({DateTime.UtcNow:O})");
            sb.AppendLine($"Document: {doc.Title}");

            // 1. Plug-in build sanity: confirm the user is on a current dll.
            sb.AppendLine($"FixturePlacementEngine.CurrentPhase: '{FixturePlacementEngine.CurrentPhase}'");
            sb.AppendLine();

            // 2. Active view + scope.
            var view = cd.Application.ActiveUIDocument.ActiveView;
            sb.AppendLine($"Active view: {view?.Name} ({view?.GetType().Name})");
            int roomsAll = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Count();
            int roomsView = view is ViewPlan vp && vp.GenLevel != null
                ? new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                    .Cast<Room>().Count(r => r.LevelId == vp.GenLevel.Id)
                : 0;
            sb.AppendLine($"Rooms total: {roomsAll}, on active level: {roomsView}");
            sb.AppendLine();

            // 3. Doors — placement engine's view of FromRoom / ToRoom.
            var doors = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
            sb.AppendLine($"DOORS ({doors.Count}):");
            int doorsWithFromRoom = 0, doorsWithToRoom = 0, doorsHostedByWall = 0, doorsHostedByOther = 0;
            foreach (var d in doors.Take(50))
            {
                Room from = null, to = null;
                try { from = d.FromRoom; } catch { }
                try { to = d.ToRoom; } catch { }
                if (from != null) doorsWithFromRoom++;
                if (to != null) doorsWithToRoom++;
                bool wallHost = d.Host is Wall;
                if (wallHost) doorsHostedByWall++; else doorsHostedByOther++;
                string hostKind = d.Host == null ? "<null>" : d.Host.GetType().Name;
                sb.AppendLine($"  Door {d.Id} ({d.Symbol?.Family?.Name}): " +
                              $"FromRoom={from?.Name ?? "<null>"}, ToRoom={to?.Name ?? "<null>"}, Host={hostKind}");
            }
            if (doors.Count > 50) sb.AppendLine($"  + {doors.Count - 50} more (truncated)");
            sb.AppendLine($"Stats: FromRoom set on {doorsWithFromRoom}/{doors.Count}, ToRoom set on {doorsWithToRoom}/{doors.Count}, " +
                          $"hosted-by-Wall {doorsHostedByWall}, hosted-by-other {doorsHostedByOther}");
            sb.AppendLine();

            // 4. Family placement types for switch + light + socket categories.
            sb.AppendLine("FAMILY PLACEMENT TYPES (the engine's biggest blind spot):");
            var watchCats = new[]
            {
                BuiltInCategory.OST_LightingDevices,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_ElectricalFixtures,
                BuiltInCategory.OST_PlumbingFixtures,
            };
            foreach (var bic in watchCats)
            {
                var symbols = new FilteredElementCollector(doc).OfCategory(bic)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
                sb.AppendLine($"  Category '{bic}' — {symbols.Count} type(s) loaded:");
                foreach (var fs in symbols.Take(20))
                {
                    var fpt = fs.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;
                    sb.AppendLine($"    '{fs.Family?.Name}' :: '{fs.Name}' → {fpt} (active={fs.IsActive})");
                }
                if (symbols.Count > 20) sb.AppendLine($"    + {symbols.Count - 20} more");
            }
            sb.AppendLine();

            // 5. Active rule pack — count rules + show wall-anchored ones whose
            // resolved family is the wrong placement type.
            try
            {
                var rules = PlacementRuleLoader.Load(doc.PathName);
                sb.AppendLine($"RULE PACK: {rules.Count} rule(s) loaded.");
                int wallAnchored = rules.Count(r =>
                {
                    var a = (r.AnchorType ?? "").ToUpperInvariant();
                    return a == "WALL_MIDPOINT" || a == "WALL_CORNER" || a == "WALL_FACE_OFFSET"
                        || a.StartsWith("DOOR_") || a.StartsWith("WINDOW_");
                });
                int ceilingAnchored = rules.Count(r =>
                {
                    var a = (r.AnchorType ?? "").ToUpperInvariant();
                    return a == "CEILING_CENTRE" || a == "CEILING_TILE_CENTRE"
                        || a == "LIGHTING_GRID" || a == "LUX_GRID";
                });
                sb.AppendLine($"  Wall-anchored:    {wallAnchored}");
                sb.AppendLine($"  Ceiling-anchored: {ceilingAnchored}");
            }
            catch (Exception ex) { sb.AppendLine($"Rule pack load: {ex.Message}"); }

            // Write to disk + show in a window the user can copy-paste.
            string outDir = OutputLocationHelper.GetOutputPath(doc, "PlacementDiagnose") ?? Path.GetTempPath();
            Directory.CreateDirectory(outDir);
            string txtPath = Path.Combine(outDir,
                $"STING_PlacementDiagnose_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            try { File.WriteAllText(txtPath, sb.ToString()); } catch { }

            string preview = sb.Length > 4000 ? sb.ToString().Substring(0, 4000) + "\n…(truncated, see file)" : sb.ToString();
            TaskDialog.Show("STING - Placement Diagnose", preview + $"\n\nFull report: {txtPath}");
            return Result.Succeeded;
        }
    }
}
