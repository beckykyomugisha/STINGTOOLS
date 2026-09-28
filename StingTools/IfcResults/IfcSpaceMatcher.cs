using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.IfcResults
{
    /// <summary>
    /// Matches IfcSpace records from a calculation tool's IFC back to Revit rooms.
    /// Revit-free so the matching rules are testable.
    ///
    /// Order: IFC GlobalId → room NUMBER (IfcSpace.Name, Revit's export convention)
    /// → room NAME (IfcSpace.LongName, then Name). A key shared by more than one
    /// room is AMBIGUOUS and never matched; a room claimed by a second space is a
    /// DUPLICATE and is reported, not silently overwritten (last-wins).
    /// </summary>
    public static class IfcSpaceMatcher
    {
        public sealed class RoomKey
        {
            public string Id { get; set; } = "";
            /// <summary>Every 22-char IFC GlobalId this room may carry (IfcGUID param, encoded UniqueId).</summary>
            public List<string> IfcGuids { get; set; } = new List<string>();
            public string Number { get; set; } = "";
            public string Name { get; set; } = "";
        }

        public sealed class SpaceKey
        {
            public string GlobalId { get; set; } = "";
            public string Name { get; set; } = "";
            public string LongName { get; set; } = "";
            public string Label => !string.IsNullOrEmpty(LongName) ? $"{Name} / {LongName}" : Name;
        }

        public sealed class Match
        {
            public int SpaceIndex { get; set; }
            public string RoomId { get; set; } = "";
            public string Via { get; set; } = "";
        }

        public sealed class Result
        {
            public List<Match> Matches { get; } = new List<Match>();
            public List<string> Unmatched { get; } = new List<string>();
            public List<string> Ambiguous { get; } = new List<string>();
            public List<string> Duplicates { get; } = new List<string>();
        }

        public static Result MatchAll(IList<RoomKey> rooms, IList<SpaceKey> spaces)
        {
            var res = new Result();
            if (spaces == null) return res;
            rooms ??= new List<RoomKey>();

            var byGuid   = Index(rooms, r => r.IfcGuids, StringComparer.Ordinal);   // GlobalIds are case-sensitive
            var byNumber = Index(rooms, r => new[] { Norm(r.Number) }, StringComparer.OrdinalIgnoreCase);
            var byName   = Index(rooms, r => new[] { Norm(r.Name) }, StringComparer.OrdinalIgnoreCase);
            var claimedBy = new Dictionary<string, int>();

            for (int i = 0; i < spaces.Count; i++)
            {
                var sp = spaces[i];
                string roomId = null, via = null;
                bool ambiguous = false;

                if (TryUnique(byGuid, sp.GlobalId, out roomId, ref ambiguous)) via = "GlobalId";
                else if (!ambiguous && TryUnique(byNumber, Norm(sp.Name), out roomId, ref ambiguous)) via = "number";
                else if (!ambiguous && TryUnique(byName, Norm(sp.LongName), out roomId, ref ambiguous)) via = "name";
                else if (!ambiguous && TryUnique(byName, Norm(sp.Name), out roomId, ref ambiguous)) via = "name";

                if (ambiguous) { res.Ambiguous.Add(sp.Label); continue; }
                if (roomId == null) { res.Unmatched.Add(sp.Label); continue; }
                if (claimedBy.TryGetValue(roomId, out int first))
                {
                    res.Duplicates.Add($"{sp.Label} → same room as {spaces[first].Label}");
                    continue;
                }
                claimedBy[roomId] = i;
                res.Matches.Add(new Match { SpaceIndex = i, RoomId = roomId, Via = via });
            }
            return res;
        }

        private static Dictionary<string, List<string>> Index(IList<RoomKey> rooms,
            Func<RoomKey, IEnumerable<string>> keys, StringComparer cmp)
        {
            var d = new Dictionary<string, List<string>>(cmp);
            foreach (var r in rooms)
                foreach (var k in (keys(r) ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrEmpty(k)).Distinct(cmp))
                {
                    if (!d.TryGetValue(k, out var list)) d[k] = list = new List<string>();
                    if (!list.Contains(r.Id)) list.Add(r.Id);
                }
            return d;
        }

        private static bool TryUnique(Dictionary<string, List<string>> idx, string key,
            out string roomId, ref bool ambiguous)
        {
            roomId = null;
            if (string.IsNullOrEmpty(key) || !idx.TryGetValue(key, out var ids)) return false;
            if (ids.Count > 1) { ambiguous = true; return false; }
            roomId = ids[0];
            return true;
        }

        private static string Norm(string s) => (s ?? "").Trim();
    }
}
