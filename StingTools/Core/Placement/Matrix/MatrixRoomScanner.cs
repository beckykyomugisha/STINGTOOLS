// StingTools — Matrix room scanner (the "read the drawing" step of Matrix Place).
//
// Collects the placeable spatial elements and groups them into room-TYPES by name
// (e.g. "Office 1" / "Office 2" -> "Office"), each carrying member count + typical /
// total area. Mirrors FixturePlacementEngine's spatial collection (OST_Rooms +
// OST_MEPSpaces, area > 0) so the matrix and the engine agree on what a "room" is.
//
// Host rooms + host spaces are returned as PLACEABLE (the engine's roomIds path scopes
// to host-doc ids). Linked rooms are collected for display/counts but flagged
// IsLinked=true and excluded from placement (the engine's per-room scoping cannot
// target linked-doc ids); the dialog shows them read-only. This is a deliberate
// backbone constraint — linked-room placement is a documented follow-up.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace StingTools.Core.Placement.Matrix
{
    /// <summary>One collected spatial element (room or space).</summary>
    public sealed class MatrixRoom
    {
        public SpatialElement Element;
        public ElementId Id;
        public string UniqueId = "";
        public string Name = "";
        public string Number = "";
        public string TypeKey = "";        // grouped name, e.g. "Office"
        public double AreaM2;
        public string LevelName = "";
        public double LevelElevationFt;
        public bool IsSpace;               // OST_MEPSpaces vs OST_Rooms
        public bool IsLinked;              // linked-doc room (display-only in the backbone)
    }

    /// <summary>A group of rooms that share a normalised name — the matrix's frozen first column.</summary>
    public sealed class MatrixRoomType
    {
        public string Key = "";
        public List<MatrixRoom> Rooms = new List<MatrixRoom>();
        public int Count => Rooms?.Count ?? 0;
        public int PlaceableCount => Rooms?.Count(r => !r.IsLinked) ?? 0;
        public double TotalAreaM2 => Rooms?.Sum(r => r.AreaM2) ?? 0.0;
        /// <summary>Median area — a robust "typical" for the row (mean skews on outliers).</summary>
        public double TypicalAreaM2
        {
            get
            {
                var a = (Rooms ?? new List<MatrixRoom>()).Select(r => r.AreaM2).OrderBy(x => x).ToList();
                if (a.Count == 0) return 0.0;
                int mid = a.Count / 2;
                return a.Count % 2 == 1 ? a[mid] : (a[mid - 1] + a[mid]) / 2.0;
            }
        }
    }

    public sealed class MatrixScanResult
    {
        public List<MatrixRoomType> Types = new List<MatrixRoomType>();
        public List<MatrixRoom> AllRooms = new List<MatrixRoom>();
        public int HostRoomCount;
        public int HostSpaceCount;
        public int LinkedRoomCount;
        public List<string> Notes = new List<string>();
    }

    public static class MatrixRoomScanner
    {
        private const double SqFtToSqM = 0.09290304;

        /// <summary>Scan the document for placeable rooms/spaces and group them into room-types.
        /// Host rooms first; host MEP spaces added when the model has spaces; linked rooms are
        /// collected read-only for context.</summary>
        public static MatrixScanResult Scan(Document doc)
        {
            var res = new MatrixScanResult();
            if (doc == null) { res.Notes.Add("No document."); return res; }

            // ── Host rooms ──
            var rooms = CollectSpatial(doc, BuiltInCategory.OST_Rooms, isSpace: false, isLinked: false);
            res.HostRoomCount = rooms.Count;
            res.AllRooms.AddRange(rooms);

            // ── Host spaces (MEP models). Always collected; the prompt calls out "read Spaces
            //    if the doc is an MEP model with no Rooms" — we include them additively so a
            //    mixed model gets both, deduped later only implicitly (rooms/spaces are distinct). ──
            var spaces = CollectSpatial(doc, BuiltInCategory.OST_MEPSpaces, isSpace: true, isLinked: false);
            res.HostSpaceCount = spaces.Count;
            if (res.HostRoomCount == 0 || spaces.Count > 0)
                res.AllRooms.AddRange(spaces);

            // ── Linked rooms (display-only). ──
            var linked = CollectLinkedRooms(doc);
            res.LinkedRoomCount = linked.Count;
            res.AllRooms.AddRange(linked);

            // ── Group into types by normalised name. ──
            foreach (var grp in res.AllRooms
                         .GroupBy(r => r.TypeKey, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                res.Types.Add(new MatrixRoomType
                {
                    Key = grp.Key,
                    Rooms = grp.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                               .ThenBy(r => r.Number, StringComparer.OrdinalIgnoreCase).ToList()
                });
            }

            if (res.AllRooms.Count == 0)
                res.Notes.Add("No rooms or spaces found. Add Rooms (or MEP Spaces) to the model, then re-scan.");
            if (res.LinkedRoomCount > 0)
                res.Notes.Add($"{res.LinkedRoomCount} linked room(s) shown read-only — matrix placement targets host rooms/spaces only.");
            return res;
        }

        private static List<MatrixRoom> CollectSpatial(Document doc, BuiltInCategory bic, bool isSpace, bool isLinked)
        {
            var list = new List<MatrixRoom>();
            try
            {
                var els = new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .Cast<SpatialElement>();
                foreach (var se in els)
                {
                    double area;
                    try { area = se.Area; } catch { continue; }
                    if (area <= 1e-6) continue;
                    var mr = Build(se, isSpace, isLinked);
                    if (mr != null) list.Add(mr);
                }
            }
            catch (Exception ex) { StingLog.Warn($"MatrixRoomScanner.CollectSpatial({bic}): {ex.Message}"); }
            return list;
        }

        private static List<MatrixRoom> CollectLinkedRooms(Document hostDoc)
        {
            var list = new List<MatrixRoom>();
            try
            {
                var links = new FilteredElementCollector(hostDoc)
                    .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>();
                foreach (var link in links)
                {
                    Document ld;
                    try { ld = link.GetLinkDocument(); } catch { ld = null; }
                    if (ld == null) continue;
                    foreach (var bic in new[] { BuiltInCategory.OST_Rooms, BuiltInCategory.OST_MEPSpaces })
                    {
                        try
                        {
                            var els = new FilteredElementCollector(ld)
                                .OfCategory(bic).WhereElementIsNotElementType().Cast<SpatialElement>();
                            foreach (var se in els)
                            {
                                double area; try { area = se.Area; } catch { continue; }
                                if (area <= 1e-6) continue;
                                var mr = Build(se, isSpace: bic == BuiltInCategory.OST_MEPSpaces, isLinked: true);
                                if (mr != null) list.Add(mr);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"MatrixRoomScanner.CollectLinkedRooms: {ex.Message}"); }
            return list;
        }

        private static MatrixRoom Build(SpatialElement se, bool isSpace, bool isLinked)
        {
            try
            {
                string name = "";
                try { name = se.Name ?? ""; } catch { }
                string number = "";
                try { number = se.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? ""; } catch { }
                double area = 0; try { area = se.Area; } catch { }
                string levelName = ""; double levelZ = 0;
                try { levelName = se.Level?.Name ?? ""; levelZ = se.Level?.Elevation ?? 0; } catch { }
                return new MatrixRoom
                {
                    Element = se,
                    Id = se.Id,
                    UniqueId = SafeUniqueId(se),
                    Name = name,
                    Number = number,
                    TypeKey = NormaliseTypeKey(name),
                    AreaM2 = area * SqFtToSqM,
                    LevelName = levelName,
                    LevelElevationFt = levelZ,
                    IsSpace = isSpace,
                    IsLinked = isLinked
                };
            }
            catch (Exception ex) { StingLog.Warn($"MatrixRoomScanner.Build: {ex.Message}"); return null; }
        }

        private static string SafeUniqueId(Element el)
        { try { return el?.UniqueId ?? ""; } catch { return ""; } }

        // "Office 1" / "Office 02" / "Bedroom 2A" / "WC-3" -> base name. Rooms often embed a
        // trailing instance number; strip it (and its separators) to group the type. The Revit
        // room NAME (not Number) is used because that's what carries the type semantic.
        private static readonly Regex TrailingInstance =
            new Regex(@"[\s\-_.]*\d+[A-Za-z]?\s*$", RegexOptions.Compiled);

        public static string NormaliseTypeKey(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName)) return "(unnamed)";
            string s = roomName.Trim();
            string stripped = TrailingInstance.Replace(s, "").Trim();
            // Don't collapse a name that is ONLY a number (e.g. "101") to empty.
            return string.IsNullOrWhiteSpace(stripped) ? s : stripped;
        }
    }
}
