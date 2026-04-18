// ClashGrouper.cs — collapse raw clashes into user-manageable groups.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace StingTools.Core.Clash
{
    public static class ClashGrouper
    {
        public static List<ClashGroupRecord> Group(List<ClashRecord> clashes)
        {
            var groups = new List<ClashGroupRecord>();
            var cellSize = new Vector3(2f, 2f, 3f);
            var byCell = new Dictionary<(long, long, long, string), List<ClashRecord>>();
            foreach (var c in clashes)
            {
                long cx = (long)(c.Centroid[0] / cellSize.X);
                long cy = (long)(c.Centroid[1] / cellSize.Y);
                long cz = (long)(c.Centroid[2] / cellSize.Z);
                var key = (cx, cy, cz, c.MatrixPairId);
                if (!byCell.TryGetValue(key, out var list))
                {
                    list = new List<ClashRecord>();
                    byCell[key] = list;
                }
                list.Add(c);
            }
            int gid = 1;
            foreach (var kv in byCell)
            {
                var groupId = $"GRP-{gid:D5}";
                foreach (var c in kv.Value) c.GroupId = groupId;
                groups.Add(new ClashGroupRecord
                {
                    Id = groupId, Kind = "spatial", Anchor = kv.Key.Item4,
                    Size = kv.Value.Count, Status = "Open"
                });
                gid++;
            }
            return groups;
        }
    }
}
