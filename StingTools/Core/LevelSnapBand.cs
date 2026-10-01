// StingTools — when two levels are the same storey.
//
// A structural / screed level (SSL) sits 50-150 mm under the finished floor (FFL) it
// belongs to, and survey drift moves levels a few millimetres between models. Such a
// level is the storey it sits against, not a storey of its own.
//
// One rule, used by LinkLevelMapper (which host level a linked level stands for,
// DTW-115) and IsoLevelCode (which levels share an ISO storey code, DTW-133): two
// levels are the same storey when they are within a band of 300 mm or half the local
// storey height, whichever is smaller. The half-storey cap keeps a low mezzanine or a
// tight plant floor from being swallowed by its neighbour.
//
// Unit-agnostic: the caller passes the storey height and the cap in the same unit.
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core
{
    public static class LevelSnapBand
    {
        /// <summary>The largest band: 300 mm.</summary>
        public const double MaxMm = 300.0;

        /// <summary>The largest band in feet (Revit internal units).</summary>
        public const double MaxFt = MaxMm / 304.8;

        /// <summary>The snap band for a level whose local storey is <paramref name="localStorey"/>
        /// tall: <c>min(maxBand, localStorey / 2)</c>. An unknown storey (infinity) gives the cap.</summary>
        public static double Band(double localStorey, double maxBand)
        {
            if (double.IsNaN(localStorey) || localStorey < 0) return maxBand;
            return Math.Min(maxBand, localStorey / 2.0);
        }

        /// <summary>True when <paramref name="offset"/> (the distance between two levels) is within the band.</summary>
        public static bool Within(double offset, double localStorey, double maxBand)
            => Math.Abs(offset) <= Band(localStorey, maxBand);
    }
}
