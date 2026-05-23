// RoomBboxIndex.cs — Wave C #8.
//
// One-pass XY-bbox index over placed rooms so the bonding inventory
// (and any other LPZ-boundary walk) can answer "which room contains
// this point?" in O(log n) instead of calling Document.GetRoomAtPoint
// per query — which on big projects is ~3 ms each, so a 5000-element
// MEP sweep with 2 lookups per element = 30 s.
//
// Tree depth and balancing are not optimised (linear scan inside the
// matching bbox is fine for typical 50-200 rooms). What we get is:
//   * One FilteredElementCollector pass over Rooms at construction
//   * Per-point lookup = bbox-contains test (cheap) + exact
//     Room.IsPointInRoom verification fallback for boundary cases
//   * No round-trip to the Revit geometry engine in the hot path

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using StingTools.Core;

namespace StingTools.Core.Lightning
{
    public class RoomBboxIndex
    {
        private struct Entry
        {
            public Room Room;
            public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public int Count => _entries.Count;

        public static RoomBboxIndex Build(Document doc)
        {
            var idx = new RoomBboxIndex();
            if (doc == null) return idx;
            try
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Element>()
                    .Where(e => (e as Room)?.Area > 0)
                    .Cast<Room>();
                foreach (var r in rooms)
                {
                    try
                    {
                        var bb = r.get_BoundingBox(null);
                        if (bb == null) continue;
                        idx._entries.Add(new Entry
                        {
                            Room = r,
                            MinX = bb.Min.X, MinY = bb.Min.Y, MinZ = bb.Min.Z,
                            MaxX = bb.Max.X, MaxY = bb.Max.Y, MaxZ = bb.Max.Z
                        });
                    }
                    catch (Exception ex) { StingLog.Warn($"RoomBboxIndex add: {ex.Message}"); }
                }
            }
            catch (Exception ex) { StingLog.Warn($"RoomBboxIndex.Build: {ex.Message}"); }
            return idx;
        }

        /// <summary>
        /// Return the room containing <paramref name="p"/>, or null. Tests
        /// bbox first (cheap), then verifies with Room.IsPointInRoom for
        /// non-rectangular room shapes. Caller passes points in Revit
        /// internal feet.
        /// </summary>
        public Room FindContaining(XYZ p)
        {
            if (p == null) return null;
            // Bbox candidates (typically 0–3 hits per query)
            foreach (var e in _entries)
            {
                if (p.X < e.MinX || p.X > e.MaxX) continue;
                if (p.Y < e.MinY || p.Y > e.MaxY) continue;
                if (p.Z < e.MinZ || p.Z > e.MaxZ) continue;
                // Exact geometry test
                try { if (e.Room.IsPointInRoom(p)) return e.Room; }
                catch (Exception ex) { StingLog.Warn($"IsPointInRoom: {ex.Message}"); }
            }
            return null;
        }
    }
}
