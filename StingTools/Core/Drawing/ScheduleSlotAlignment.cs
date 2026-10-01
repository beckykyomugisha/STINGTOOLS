// StingTools — Drawing Template Manager · a schedule's VISIBLE top-left on its slot
//
// ScheduleSheetInstance.Create(doc, sheet, schedule, point) takes an insertion
// point, and DTW-151 passed the slot's top-left there. The Drawing Self-Test in
// Revit 2025 then measured the placed schedule's bounding box on the sheet and
// found its top-left at (248, 400) mm for a slot whose top-left is (250, 400):
// the insertion point is not the visible corner — the instance's extent starts
// ~2 mm left of it. The slot is a promise about where the drawing's content sits,
// so the visible corner is what has to land on it.
//
// The fix is measure-and-correct, not a hard-coded 2 mm: place, regenerate, read
// get_BoundingBox(sheet), and move the instance by the difference between the
// slot corner and the measured corner. The offset is Revit's, may differ by
// schedule appearance (border, title), and this way is never assumed.
//
// Revit-free (plain doubles, feet in and out); unit-tested in StingTools.Tags.Tests.

using System;

namespace StingTools.Core.Drawing
{
    public static class ScheduleSlotAlignment
    {
        /// <summary>A measured corner within this of the target needs no move
        /// (0.1 mm in feet). Below it is rounding, not misplacement.</summary>
        public const double AlignToleranceFt = 0.1 / 304.8;

        /// <summary>A correction larger than this (50 mm) is not a border offset —
        /// the bounding box is describing something else (a split schedule, a
        /// stale extent). It is refused and reported rather than applied.</summary>
        public const double MaxCorrectionFt = 50.0 / 304.8;

        /// <summary>The outcome of comparing a placed schedule's extent with its slot.</summary>
        public readonly struct Correction
        {
            public Correction(double dx, double dy, bool needed, bool refused)
            { Dx = dx; Dy = dy; Needed = needed; Refused = refused; }

            /// <summary>Move to apply to the instance's Point, in feet.</summary>
            public double Dx { get; }
            public double Dy { get; }
            /// <summary>True when the corner is off by more than the tolerance and the move is sane.</summary>
            public bool Needed { get; }
            /// <summary>True when the offset is larger than <see cref="MaxCorrectionFt"/>:
            /// do not move, warn.</summary>
            public bool Refused { get; }
        }

        /// <summary>
        /// The move that puts the measured top-left (<paramref name="bbMinX"/>,
        /// <paramref name="bbMaxY"/>) on the slot's top-left (<paramref name="slotLeft"/>,
        /// <paramref name="slotTop"/>). All in feet, sheet coordinates.
        /// </summary>
        public static Correction Compute(double slotLeft, double slotTop, double bbMinX, double bbMaxY,
            double tolFt = AlignToleranceFt, double maxFt = MaxCorrectionFt)
        {
            if (double.IsNaN(bbMinX) || double.IsNaN(bbMaxY) || double.IsInfinity(bbMinX) || double.IsInfinity(bbMaxY))
                return new Correction(0, 0, false, true);
            double dx = slotLeft - bbMinX, dy = slotTop - bbMaxY;
            if (Math.Abs(dx) <= tolFt && Math.Abs(dy) <= tolFt) return new Correction(0, 0, false, false);
            if (Math.Abs(dx) > maxFt || Math.Abs(dy) > maxFt) return new Correction(dx, dy, false, true);
            // An axis already within tolerance is left exactly where it is.
            return new Correction(Math.Abs(dx) <= tolFt ? 0 : dx, Math.Abs(dy) <= tolFt ? 0 : dy, true, false);
        }

        /// <summary>True when a measured corner sits on the slot corner within tolerance.</summary>
        public static bool IsAligned(double slotLeft, double slotTop, double bbMinX, double bbMaxY,
            double tolFt = AlignToleranceFt)
            => Math.Abs(slotLeft - bbMinX) <= tolFt && Math.Abs(slotTop - bbMaxY) <= tolFt;
    }
}
