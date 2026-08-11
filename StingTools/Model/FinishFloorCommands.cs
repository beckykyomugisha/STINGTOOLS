// ============================================================================
// FinishFloorCommands.cs — Create floor-finish elements from room finish data
//
// K-3: nothing in the product turned a room's floor finish into a finish
// element. SmartCoveringFactory.ApplyCovering injects finish layers into wall
// compound types and writes parameters on beams and columns; it operates on
// selected elements, not from room data, and it never creates a Floor. On a
// project whose floor finishes are the product being sold, those floors were
// drawn entirely by hand.
//
// Method follows KIBALE_NP_BIM_MODELLING_PLAYBOOK Part 3A — "layer by trade,
// not by room, and align by top face, never by centre":
//   * one finish floor per room, sketched on the room boundary
//   * Level = the room's level, Height Offset From Level = 0, so the top of
//     the finish sits exactly at FFL
//   * Room Bounding OFF, so the finish does not slice the room volumes it was
//     derived from (left on, room areas go wrong and the finishes schedule
//     the floors were derived from is corrupted)
//   * Structural OFF — the slab is a separate element and a separate pour
//
// Re-runnable: rooms that already carry a finish floor are skipped, not
// duplicated. An unresolved finish code is reported, never silently
// substituted with an arbitrary floor type.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Model
{
    // ════════════════════════════════════════════════════════════════
    // FINISH FLOOR CREATOR — room finish data → Revit Floor elements
    // ════════════════════════════════════════════════════════════════

    internal static class FinishFloorCreator
    {
        /// <summary>Idempotency stamp: source room element id, written on each created floor.</summary>
        public const string StampRoomId = "STING_FINISH_SRC_ROOM_ID_TXT";
        /// <summary>Resolved finish code, written on each created floor.</summary>
        public const string StampCode = "STING_FINISH_CODE_TXT";

        public class RoomOutcome
        {
            public string RoomLabel { get; set; }
            public string Code { get; set; }
            public string Detail { get; set; }
        }

        public class CreateResult
        {
            public int RoomsScanned { get; set; }
            public List<RoomOutcome> Created { get; } = new();
            public List<RoomOutcome> SkippedExisting { get; } = new();
            public List<RoomOutcome> SkippedNoCode { get; } = new();
            public List<RoomOutcome> UnresolvedCode { get; } = new();
            public List<RoomOutcome> Failed { get; } = new();
            /// <summary>True when the idempotency stamp is not bound, so the weaker spatial fallback was used.</summary>
            public bool StampUnavailable { get; set; }
        }

        private static string Label(Room room)
        {
            string num = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
            string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
            string s = (num + " " + name).Trim();
            return s.Length > 0 ? s : $"Room {room.Id}";
        }

        /// <summary>
        /// Reads the room's floor finish code. Prefers the STING code parameter, then the
        /// prose parameter and the Revit built-in — a value in either of those is only
        /// accepted when it actually matches a code in the legend, so free-text prose is
        /// correctly reported as "no code" rather than as an unresolved code.
        /// </summary>
        private static string ReadFloorCode(Document doc, Room room, out bool looksLikeProse)
        {
            looksLikeProse = false;

            string code = ParameterHelpers.GetString(room, "BLE_ROOM_FINISH_FLOOR_COD_TXT");
            if (!string.IsNullOrWhiteSpace(code)) return code.Trim();

            // Fall back to the prose slots, but only when what is stored is in fact a code.
            foreach (string candidate in new[]
            {
                ParameterHelpers.GetString(room, "BLE_ROOM_FINISH_FLOOR_TXT"),
                room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR)?.AsString(),
            })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                string trimmed = candidate.Trim();
                if (FinishCodeRegistry.TryGet(doc, trimmed, out _)) return trimmed;
                looksLikeProse = true;
            }

            return null;
        }

        /// <summary>
        /// Resolves a FloorType for the finish code. Strict on purpose: exact name, then
        /// case-insensitive substring on the hint, then the material name. Returns null
        /// rather than falling back to an arbitrary type — ModelFamilyResolver's
        /// "use the first available type" behaviour would silently model the wrong build-up.
        /// </summary>
        private static FloorType ResolveFloorType(IList<FloorType> types, FinishCode finish)
        {
            if (finish == null) return null;

            foreach (string key in new[] { finish.RevitTypeHint, finish.MatName, finish.Description })
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                var exact = types.FirstOrDefault(t =>
                    t.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (exact != null) return exact;

                var contains = types.FirstOrDefault(t =>
                    t.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                if (contains != null) return contains;
            }

            return null;
        }

        /// <summary>Reads the idempotency stamp; returns null when the parameter is not bound.</summary>
        private static string ReadStamp(Element el, string paramName)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || p.StorageType != StorageType.String) return null;
            return p.AsString() ?? string.Empty;
        }

        private static bool WriteStamp(Element el, string paramName, string value)
        {
            Parameter p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
            try { p.Set(value ?? string.Empty); return true; }
            catch (Exception ex)
            {
                StingLog.Warn($"Finish floor stamp '{paramName}' on {el.Id} failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Builds the set of room ids that already carry a finish floor.
        /// Primary source is the stamp. When the stamp parameter is not bound (the project
        /// has not reloaded shared parameters), falls back to a bounding-box test against
        /// floors whose type is one the legend would have produced. The fallback can only
        /// over-match, which skips a room rather than duplicating a floor — reported as
        /// skipped, never silently.
        /// </summary>
        private static HashSet<ElementId> ExistingFinishFloors(
            Document doc, IList<Floor> floors, HashSet<ElementId> finishTypeIds, out bool stampAvailable)
        {
            var covered = new HashSet<ElementId>();
            stampAvailable = false;

            foreach (var floor in floors)
            {
                string stamp = ReadStamp(floor, StampRoomId);
                if (stamp == null) continue;      // parameter not bound on this element
                stampAvailable = true;
                if (string.IsNullOrWhiteSpace(stamp)) continue;

                if (long.TryParse(stamp, out long raw))
                    covered.Add(new ElementId(raw));
            }

            if (stampAvailable) return covered;

            // Fallback: a floor of a finish type whose footprint contains the room point.
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            foreach (var floor in floors.Where(f => finishTypeIds.Contains(f.GetTypeId())))
            {
                BoundingBoxXYZ bb = null;
                try { bb = floor.get_BoundingBox(null); } catch { }
                if (bb == null) continue;

                foreach (var room in rooms)
                {
                    if (covered.Contains(room.Id)) continue;
                    if (room.LevelId != floor.LevelId) continue;
                    if (!(room.Location is LocationPoint lp)) continue;

                    XYZ p = lp.Point;
                    if (p.X >= bb.Min.X && p.X <= bb.Max.X &&
                        p.Y >= bb.Min.Y && p.Y <= bb.Max.Y)
                    {
                        covered.Add(room.Id);
                    }
                }
            }

            return covered;
        }

        /// <summary>
        /// Creates one finish floor per room that carries a resolvable floor finish code
        /// and does not already have one. Caller owns the transaction.
        /// </summary>
        public static CreateResult Run(Document doc)
        {
            var result = new CreateResult();

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(r => r.Area > 0)
                .ToList();

            result.RoomsScanned = rooms.Count;
            if (rooms.Count == 0) return result;

            var floorTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .ToList();

            var legend = FinishCodeRegistry.Get(doc);

            // Every FloorType the legend could resolve to — used by the spatial fallback.
            var finishTypeIds = new HashSet<ElementId>();
            foreach (var f in legend.Values.Where(f => f.IsFloor))
            {
                var ft = ResolveFloorType(floorTypes, f);
                if (ft != null) finishTypeIds.Add(ft.Id);
            }

            var existingFloors = new FilteredElementCollector(doc)
                .OfClass(typeof(Floor))
                .Cast<Floor>()
                .ToList();

            var covered = ExistingFinishFloors(doc, existingFloors, finishTypeIds, out bool stampAvailable);
            result.StampUnavailable = !stampAvailable;

            var boundaryOptions = new SpatialElementBoundaryOptions();

            foreach (var room in rooms)
            {
                string label = Label(room);
                string code = ReadFloorCode(doc, room, out bool looksLikeProse);

                if (string.IsNullOrWhiteSpace(code))
                {
                    result.SkippedNoCode.Add(new RoomOutcome
                    {
                        RoomLabel = label,
                        Detail = looksLikeProse
                            ? "floor finish is free text, not a code from the legend"
                            : "no floor finish set",
                    });
                    continue;
                }

                if (covered.Contains(room.Id))
                {
                    result.SkippedExisting.Add(new RoomOutcome { RoomLabel = label, Code = code });
                    continue;
                }

                if (!legend.TryGetValue(code, out var finish) || !finish.IsFloor)
                {
                    result.UnresolvedCode.Add(new RoomOutcome
                    {
                        RoomLabel = label,
                        Code = code,
                        Detail = legend.ContainsKey(code)
                            ? $"'{code}' is a {legend[code].Surface} code, not a FLOOR code"
                            : $"'{code}' is not in STING_FINISH_CODES.csv",
                    });
                    continue;
                }

                var floorType = ResolveFloorType(floorTypes, finish);
                if (floorType == null)
                {
                    result.UnresolvedCode.Add(new RoomOutcome
                    {
                        RoomLabel = label,
                        Code = code,
                        Detail = $"no FloorType matches '{finish.RevitTypeHint}' or '{finish.MatName}' " +
                                 "— create or rename a floor type, then re-run",
                    });
                    continue;
                }

                try
                {
                    var loops = BuildLoops(room, boundaryOptions);
                    if (loops == null || loops.Count == 0)
                    {
                        result.Failed.Add(new RoomOutcome
                        {
                            RoomLabel = label,
                            Code = code,
                            Detail = "room has no usable boundary — check the walls enclose it",
                        });
                        continue;
                    }

                    var floor = Floor.Create(doc, loops, floorType.Id, room.LevelId);
                    if (floor == null)
                    {
                        result.Failed.Add(new RoomOutcome
                        {
                            RoomLabel = label, Code = code, Detail = "Floor.Create returned null",
                        });
                        continue;
                    }

                    ApplyFinishSettings(floor);
                    WriteStamp(floor, StampRoomId, room.Id.ToString());
                    WriteStamp(floor, StampCode, finish.Code);
                    ModelWorksetAssigner.Assign(doc, floor);

                    result.Created.Add(new RoomOutcome
                    {
                        RoomLabel = label,
                        Code = code,
                        Detail = $"{floorType.Name} ({finish.Description})",
                    });
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"Finish floor for '{label}' failed: {ex.Message}");
                    result.Failed.Add(new RoomOutcome
                    {
                        RoomLabel = label, Code = code, Detail = ex.Message,
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Room boundary → curve loops. The first boundary list is the outer loop; any
        /// further lists are inner loops (columns, cores, shafts) and become openings.
        /// </summary>
        private static IList<CurveLoop> BuildLoops(Room room, SpatialElementBoundaryOptions options)
        {
            var segments = room.GetBoundarySegments(options);
            if (segments == null || segments.Count == 0) return null;

            var loops = new List<CurveLoop>();
            foreach (var list in segments)
            {
                if (list == null || list.Count == 0) continue;
                try
                {
                    var loop = new CurveLoop();
                    foreach (var seg in list) loop.Append(seg.GetCurve());
                    loops.Add(loop);
                }
                catch (Exception ex)
                {
                    // A non-contiguous inner loop is not fatal — drop it and keep the outer.
                    StingLog.Warn($"Finish floor loop skipped on room {room.Id}: {ex.Message}");
                }
            }

            return loops;
        }

        /// <summary>
        /// Height Offset From Level = 0 so the top face sits at FFL; Room Bounding off so
        /// the finish does not slice the room volumes it was derived from; Structural off
        /// because the slab is a separate element.
        /// </summary>
        private static void ApplyFinishSettings(Floor floor)
        {
            TrySet(floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM), 0.0);
            TrySetInt(floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL), 0);

            // Room Bounding rides on WALL_ATTR_ROOM_BOUNDING for floors as well as walls;
            // fall back to the display name for hosts where that id is not exposed.
            Parameter rb = floor.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING)
                           ?? floor.LookupParameter("Room Bounding");
            TrySetInt(rb, 0);
        }

        private static void TrySet(Parameter p, double value)
        {
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return;
            try { p.Set(value); }
            catch (Exception ex) { StingLog.Warn($"Finish floor param set failed: {ex.Message}"); }
        }

        private static void TrySetInt(Parameter p, int value)
        {
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) return;
            try { p.Set(value); }
            catch (Exception ex) { StingLog.Warn($"Finish floor param set failed: {ex.Message}"); }
        }
    }

    // ════════════════════════════════════════════════════════════════
    // COMMAND — Finish_CreateFloorsFromRooms
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CreateFinishFloorsFromRoomsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument;
            var doc = uidoc?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                FinishFloorCreator.CreateResult result;
                using (var tx = new Transaction(doc, "STING Finish Floors From Rooms"))
                {
                    tx.Start();
                    result = FinishFloorCreator.Run(doc);
                    if (result.Created.Count > 0) tx.Commit(); else tx.RollBack();
                }

                if (result.RoomsScanned == 0)
                {
                    TaskDialog.Show("FINISHES", "No placed rooms found.");
                    return Result.Cancelled;
                }

                TaskDialog.Show("FINISHES — Floors From Rooms", BuildReport(result));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Finish_CreateFloorsFromRooms", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private static string BuildReport(FinishFloorCreator.CreateResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"FINISH FLOORS — {r.RoomsScanned} rooms scanned\n");
            sb.AppendLine($"  Created            : {r.Created.Count}");
            sb.AppendLine($"  Already had a floor: {r.SkippedExisting.Count}");
            sb.AppendLine($"  No finish code     : {r.SkippedNoCode.Count}");
            sb.AppendLine($"  Unresolved code    : {r.UnresolvedCode.Count}");
            if (r.Failed.Count > 0)
                sb.AppendLine($"  Failed             : {r.Failed.Count}");

            if (r.StampUnavailable)
            {
                sb.AppendLine();
                sb.AppendLine("  NOTE: STING_FINISH_SRC_ROOM_ID_TXT is not bound to Floors, so");
                sb.AppendLine("  re-run detection used the weaker footprint test. Run");
                sb.AppendLine("  LoadSharedParams to bind it for exact re-run behaviour.");
            }

            Section(sb, "CREATED", r.Created, 15);
            Section(sb, "UNRESOLVED CODE", r.UnresolvedCode, 10);
            Section(sb, "FAILED", r.Failed, 10);
            Section(sb, "ALREADY HAD A FLOOR", r.SkippedExisting, 5);

            return sb.ToString();
        }

        private static void Section(StringBuilder sb, string title,
            List<FinishFloorCreator.RoomOutcome> rows, int max)
        {
            if (rows.Count == 0) return;
            sb.AppendLine($"\n{title}:");
            foreach (var row in rows.Take(max))
            {
                string code = string.IsNullOrEmpty(row.Code) ? "" : $" [{row.Code}]";
                string detail = string.IsNullOrEmpty(row.Detail) ? "" : $" — {row.Detail}";
                sb.AppendLine($"  {row.RoomLabel}{code}{detail}");
            }
            if (rows.Count > max) sb.AppendLine($"  ... and {rows.Count - max} more");
        }
    }

    // ════════════════════════════════════════════════════════════════
    // COMMAND — Finish_ReloadCodes
    // ════════════════════════════════════════════════════════════════

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class ReloadFinishCodesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var doc = ParameterHelpers.GetApp(commandData)?.ActiveUIDocument?.Document;
            if (doc == null) return Result.Failed;

            try
            {
                FinishCodeRegistry.Reload();
                var legend = FinishCodeRegistry.Get(doc);

                var sb = new StringBuilder();
                sb.AppendLine($"FINISH CODES — {legend.Count} loaded\n");
                foreach (string surface in new[] { "FLOOR", "WALL", "CEILING", "BASE" })
                {
                    var rows = FinishCodeRegistry.ForSurface(doc, surface);
                    sb.AppendLine($"{surface} ({rows.Count}):");
                    foreach (var f in rows)
                        sb.AppendLine($"  {f.Code}  {f.Description}  → {f.MatCode}");
                    sb.AppendLine();
                }

                TaskDialog.Show("FINISHES — Code Legend", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Finish_ReloadCodes", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
