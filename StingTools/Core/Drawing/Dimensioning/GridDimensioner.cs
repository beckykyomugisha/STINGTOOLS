// StingTools — Drawing Template Manager · Phase 175
//
// GridDimensioner replaces the hardcoded grid-dim block previously in
// AnnotationRunner.RunDimRules with a strategy-aware implementation:
//   * Linear / Chain → one Linear DimensionType chain per axis with
//     every grid as a segment reference. Revit auto-segments.
//   * Ordinate       → one Ordinate DimensionType chain per axis whose
//     witness lines all originate from the first grid (the datum).
//
// 3D / non-2D views are skipped — Revit's Dimension API rejects them.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using StingTools.Core.Drawing;
using StingTools.Core;

namespace StingTools.Core.Drawing.Dimensioning
{
    /// <summary>
    /// B1 CONVERGENCE — this class no longer dimensions grids.
    ///
    /// There were two grid-dimensioning implementations: this one and
    /// AnnotationRunner.DimGrids. Only DimGrids was ever reachable —
    /// GridDimensioner.Run had no call site anywhere — and both were
    /// broken in different ways (A-4 put every grid in one
    /// ReferenceArray; A-5 ran each chain PARALLEL to the grids it was
    /// measuring). Rather than fix both and route between them, DimGrids
    /// is the single surviving path: it splits grids into parallel sets,
    /// chains perpendicular to each, carries the C-4 idempotency guard,
    /// and now honours the pack's dimensionStrategy through
    /// DimensionStrategy.ResolveType — the one capability this class had
    /// that DimGrids lacked.
    ///
    /// Run/CreateChain/IsHorizontal/IsVertical are removed. IsDimensionable
    /// stays because MEPDimensioner and DrainageInvertDimensioner use it.
    /// </summary>
    public static class GridDimensioner
    {
        public static bool IsDimensionable(View v)
        {
            if (v == null) return false;
            if (v is View3D) return false;
            switch (v.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.AreaPlan:
                case ViewType.EngineeringPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.DraftingView:
                    return true;
                default:
                    return false;
            }
        }
    }
}
