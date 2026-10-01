// StingTools — Drawing Template Manager · reading the ids back out of a context stamp
//
// ProductionContextKey (DTW-42) writes the stable ids of a production context into the
// room segment of the stamp, after a '#':
//
//     Level 1::#L312::::STING-AREA::A01::L01
//     Level 1::#L312|B1f2c…-0004a1::A01::STING-AREA…
//     ::4471#R4471::Kitchen (0.12)::
//
// Readers that took the level from the stamp's NAME part (Renumber, DTW-118) went
// wrong after a level rename: the ISO level map is keyed by the level's current name,
// so the old name found no code. They read the id here and look the level up.
// The Doctor (DTW-123) reads the same ids to find stamps whose level or box is gone.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Drawing
{
    public static class ProductionContextIds
    {
        /// <summary>The ids a stamp carries. Each is null when the stamp has none of that kind.</summary>
        public sealed class Ids
        {
            public long? LevelId { get; set; }
            public string RoomId { get; set; }
            public string BoxUniqueId { get; set; }
            public bool Any => LevelId.HasValue || RoomId != null || BoxUniqueId != null;
        }

        /// <summary>
        /// Parse the ids from <paramref name="stamp"/>. A stamp written before ids were part
        /// of it (no '#') returns an empty <see cref="Ids"/>; so does null or malformed input.
        /// </summary>
        public static Ids Parse(string stamp)
        {
            var ids = new Ids();
            if (string.IsNullOrEmpty(stamp)) return ids;
            const string sep = "::";
            int a = stamp.IndexOf(sep, StringComparison.Ordinal);
            if (a < 0) return ids;
            int b = stamp.IndexOf(sep, a + sep.Length, StringComparison.Ordinal);
            string roomSeg = b < 0 ? stamp.Substring(a + sep.Length) : stamp.Substring(a + sep.Length, b - a - sep.Length);
            int hash = roomSeg.IndexOf('#');
            if (hash < 0) return ids;
            foreach (var raw in roomSeg.Substring(hash + 1).Split('|'))
            {
                if (raw.Length < 2) continue;
                string v = raw.Substring(1);
                switch (raw[0])
                {
                    case 'L':
                        if (long.TryParse(v, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out var l) && l > 0)
                            ids.LevelId = l;
                        break;
                    case 'R': ids.RoomId = v; break;
                    case 'B': ids.BoxUniqueId = v; break;
                }
            }
            return ids;
        }

        /// <summary>The level name part of the stamp (its first segment); null when empty.</summary>
        public static string LevelName(string stamp)
        {
            if (string.IsNullOrEmpty(stamp)) return null;
            int a = stamp.IndexOf("::", StringComparison.Ordinal);
            string n = a < 0 ? stamp : stamp.Substring(0, a);
            return n.Length == 0 ? null : n;
        }
    }
}
