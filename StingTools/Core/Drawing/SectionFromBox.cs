// StingTools — Drawing Template Manager · a section, or a 3D section box, from a scope box
//
// A Section rule produced for a scope box (STING:: box, or an area box) was cut by the
// producer's default: a fixed 10 m section through the project origin, whatever the box.
// Every box on the model produced the same drawing of somewhere else.
//
// The box already says where the section goes. Measured in its own frame
// (ScopeBoxRevit.TryMeasure: centre, width along AngleRad, depth across it, Z range),
// the section is cut through the box's centre along its LONG side — the longest view
// the box offers — half as wide as that side, looking across the short side as far as
// the box's face. A 3D view gets the box itself as its section box, turned with it.
//
// Units are whatever the caller passes (DrawingProducer passes feet). Revit-free:
// StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Drawing
{
    /// <summary>A section's frame in plan: where it cuts, which way, how wide and how deep.</summary>
    public sealed class SectionFrame
    {
        public double OriginX, OriginY;
        /// <summary>Unit vector along the cut (the drawing's horizontal).</summary>
        public double DirX, DirY;
        /// <summary>Half the cut's length.</summary>
        public double HalfWidth;
        /// <summary>How far the section looks, from the cut to the box's face.</summary>
        public double Depth;
    }

    public static class SectionFromBox
    {
        /// <summary>
        /// The section through the centre of a box measured as
        /// (<paramref name="width"/> along <paramref name="angleRad"/>, <paramref name="depth"/>
        /// across it), cut along its longer side. Null for a box with no extent.
        /// </summary>
        public static SectionFrame Frame(double centreX, double centreY, double width, double depth, double angleRad)
        {
            if (!(width > 0) || !(depth > 0)) return null;
            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            bool alongWidth = width >= depth;
            return new SectionFrame
            {
                OriginX = centreX,
                OriginY = centreY,
                DirX = alongWidth ? c : -s,
                DirY = alongWidth ? s : c,
                HalfWidth = (alongWidth ? width : depth) / 2.0,
                Depth = (alongWidth ? depth : width) / 2.0,
            };
        }

        /// <summary>
        /// The vertical band a box-driven section shows: from just below the context level
        /// to the next level up (or 4 m above it), clipped to the box's own height; the
        /// box's whole height when there is no level. Returned as (bottom, top), bottom &lt; top.
        /// </summary>
        public static (double Bottom, double Top) Band(double boxZMin, double boxZMax,
            double? levelElevation, double? nextLevelElevation, double belowLevel, double defaultStorey)
        {
            double lo = Math.Min(boxZMin, boxZMax), hi = Math.Max(boxZMin, boxZMax);
            if (!levelElevation.HasValue) return hi - lo > 1e-6 ? (lo, hi) : (lo, lo + defaultStorey);
            double bottom = levelElevation.Value - Math.Abs(belowLevel);
            double top = nextLevelElevation.HasValue && nextLevelElevation.Value > levelElevation.Value
                ? nextLevelElevation.Value
                : levelElevation.Value + Math.Abs(defaultStorey);
            if (hi - lo > 1e-6)
            {
                bottom = Math.Max(bottom, lo);
                top = Math.Min(top, hi);
            }
            if (top - bottom < 1e-6) top = bottom + Math.Abs(defaultStorey);
            return (bottom, top);
        }
    }
}
