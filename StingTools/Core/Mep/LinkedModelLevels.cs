// StingTools — the host levels a linked model occupies (DTW-122).
//
// "Skip levels with nothing modelled" decided a non-MEP plan (architecture, structure,
// coordination) by the HOST's elements only. In a federated MEP host — the MEP is
// modelled here and the architecture is linked — every architectural, structural and
// coordination plan was then skipped on every level: the walls, slabs and columns are in
// the link, on the link's levels.
//
// The link's levels that hold at least one model element are mapped onto host levels the
// way MepLevelViewProducer maps linked MEP (DTW-49): each link level, through the link
// instance's transform, to the host level at or below it (LinkLevelMapper.HostLevelFor).
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System.Collections.Generic;

namespace StingTools.Core.Mep
{
    public static class LinkedModelLevels
    {
        /// <summary>
        /// The ids of the host levels standing for the given link levels, whose
        /// elevations are already in the host's frame. Empty with no host levels.
        /// </summary>
        public static HashSet<long> HostLevels(IEnumerable<double> linkLevelElevationsInHostFrame,
            IEnumerable<(long Id, double Elevation)> hostLevels)
        {
            var set = new HashSet<long>();
            var host = new List<(long Id, double Elevation)>(hostLevels ?? new (long, double)[0]);
            if (host.Count == 0 || linkLevelElevationsInHostFrame == null) return set;
            foreach (var z in linkLevelElevationsInHostFrame)
            {
                var id = LinkLevelMapper.HostLevelFor(z, host);
                if (id.HasValue) set.Add(id.Value);
            }
            return set;
        }

        /// <summary>
        /// A level "has something modelled" when the host holds a model element on it
        /// or a linked model holds one on a link level mapped to it.
        /// </summary>
        public static bool HasModel(bool hostHasModel, long hostLevelId, ISet<long> linkedHostLevels)
            => hostHasModel || (linkedHostLevels != null && linkedHostLevels.Contains(hostLevelId));
    }
}
