// Healthcare Pack H-10 — Room graph builder.
// Builds an undirected graph keyed by Room ElementId based on door
// connectivity: a door whose ToRoom and FromRoom are both populated
// produces an edge between those rooms. Used by adjacency / clean-dirty
// flow analysis.

using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Adjacency
{
    public class RoomGraph
    {
        public Dictionary<int, HashSet<int>> Adj = new();   // roomId → connected roomIds
        public Dictionary<int, Element> Rooms = new();      // roomId → Room

        public IEnumerable<int> Neighbours(int roomId) =>
            Adj.TryGetValue(roomId, out var n) ? n : System.Linq.Enumerable.Empty<int>();
    }

    public static class RoomGraphBuilder
    {
        public static RoomGraph Build(Document doc)
        {
            var g = new RoomGraph();
            if (doc == null) return g;

            foreach (var r in new FilteredElementCollector(doc)
                                .OfCategory(BuiltInCategory.OST_Rooms)
                                .WhereElementIsNotElementType().ToElements())
            {
                g.Rooms[r.Id.IntegerValue] = r;
                g.Adj[r.Id.IntegerValue] = new HashSet<int>();
            }

            var doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType().OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>().ToList();

            foreach (var d in doors)
            {
                Element fromRoom = null, toRoom = null;
                try { fromRoom = d.FromRoom; toRoom = d.ToRoom; } catch { }
                if (fromRoom == null || toRoom == null) continue;
                int a = fromRoom.Id.IntegerValue;
                int b = toRoom.Id.IntegerValue;
                if (g.Adj.ContainsKey(a) && g.Adj.ContainsKey(b))
                {
                    g.Adj[a].Add(b);
                    g.Adj[b].Add(a);
                }
            }
            return g;
        }
    }
}
