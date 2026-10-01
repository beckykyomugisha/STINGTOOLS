// StingTools — which HOST level a linked model's level stands for.
//
// MepLevelViewProducer.LevelsByDiscipline answers "on which levels is anything modelled"
// so a level with no ductwork gets no HVAC plan. It only looked in the host document.
// On a federated job the host holds architecture and links the M, E and P models, so
// every discipline reported "nothing modelled" and nothing was produced.
//
// Elements in a link sit on the LINK's levels. A plan is made on a HOST level, so each
// link level is mapped to a host level (in the host's frame, after the link transform).
// DTW-115: a link level close to a host level — an MEP model's SSL level 50-150 mm
// below the architect's FFL, or survey drift — is that level, not the storey below.
// "Close" is the nearest host level within a band of 300 mm or half the local storey
// height, whichever is smaller. Outside the band the host level at or below wins (the
// storey the element is on). A link level below every host level maps to the lowest
// host level: it is still in the building.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Mep
{
    public static class LinkLevelMapper
    {
        /// <summary>The largest snap band: 300 mm, in feet.</summary>
        public const double MaxSnapBandFt = 300.0 / 304.8;

        /// <summary>
        /// The id of the nearest host level when <paramref name="elevation"/> is within the snap
        /// band of it (300 mm or half the local storey, the smaller); otherwise the host level at
        /// or below <paramref name="elevation"/> (within <paramref name="tolerance"/>), else the
        /// lowest host level; null with no host levels. Elevations in feet.
        /// </summary>
        public static long? HostLevelFor(double elevation, IEnumerable<(long Id, double Elevation)> hostLevels,
            double tolerance = 0.01)
        {
            var levels = (hostLevels ?? Enumerable.Empty<(long, double)>()).OrderBy(l => l.Item2).ToList();
            if (levels.Count == 0) return null;

            // Nearest host level, and whether the link level is within its band.
            int nearest = 0;
            for (int i = 1; i < levels.Count; i++)
                if (Math.Abs(levels[i].Item2 - elevation) < Math.Abs(levels[nearest].Item2 - elevation)) nearest = i;
            double offset = elevation - levels[nearest].Item2;
            // The local storey is the gap to the neighbour on the link level's side; with no
            // neighbour there (top or bottom level) the other side's gap stands in.
            int side = offset < 0 ? nearest - 1 : nearest + 1;
            int other = offset < 0 ? nearest + 1 : nearest - 1;
            double storey = double.PositiveInfinity;
            if (side >= 0 && side < levels.Count) storey = Math.Abs(levels[nearest].Item2 - levels[side].Item2);
            else if (other >= 0 && other < levels.Count) storey = Math.Abs(levels[nearest].Item2 - levels[other].Item2);
            double band = Math.Min(MaxSnapBandFt, storey / 2.0);
            if (Math.Abs(offset) <= band) return levels[nearest].Item1;

            long? pick = null;
            foreach (var l in levels)
                if (l.Item2 <= elevation + tolerance) pick = l.Item1;
            return pick ?? levels[0].Item1;
        }
    }
}
