// StingTools — which HOST level a linked model's level stands for.
//
// MepLevelViewProducer.LevelsByDiscipline answers "on which levels is anything modelled"
// so a level with no ductwork gets no HVAC plan. It only looked in the host document.
// On a federated job the host holds architecture and links the M, E and P models, so
// every discipline reported "nothing modelled" and nothing was produced.
//
// Elements in a link sit on the LINK's levels. A plan is made on a HOST level, so each
// link level is mapped to the host level at or below its elevation (in the host's
// frame, after the link transform) — the storey the element is on. A link level below
// every host level maps to the lowest host level: it is still in the building.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Mep
{
    public static class LinkLevelMapper
    {
        /// <summary>
        /// The id of the host level at or below <paramref name="elevation"/> (within
        /// <paramref name="tolerance"/>), else the lowest host level; null with no host levels.
        /// </summary>
        public static long? HostLevelFor(double elevation, IEnumerable<(long Id, double Elevation)> hostLevels,
            double tolerance = 0.01)
        {
            var levels = (hostLevels ?? Enumerable.Empty<(long, double)>()).OrderBy(l => l.Item2).ToList();
            if (levels.Count == 0) return null;
            long? pick = null;
            foreach (var l in levels)
                if (l.Item2 <= elevation + tolerance) pick = l.Item1;
            return pick ?? levels[0].Item1;
        }
    }
}
