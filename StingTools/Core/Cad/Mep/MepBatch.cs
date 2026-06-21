// ============================================================================
// MepBatch.cs — Phase: MEP-from-DWG P6-4 (DRY).
//
// Shared scaffold the three MEP builders (fixtures / runs / risers) had each
// re-implemented — and drifted on (BuildRisers shipped with NO Escape-
// cancellation check). Centralising the escape cadence + the post-commit
// auto-tag removes that drift class. MepGeom unifies the 2D/3D
// closest-point-on-segment helpers that were duplicated across the fitting and
// fixture builders.
// ============================================================================
using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Model;   // ModelEngine.AutoTagCreatedElements

namespace StingTools.Core.Cad.Mep
{
    internal static class MepBatch
    {
        /// <summary>Escape-cancellation cadence shared by every per-item creation loop —
        /// checked every 50 items. Returns true (and records a warning) when the user cancels.</summary>
        public static bool ShouldCancel(int i, List<string> warnings)
        {
            if (i % 50 == 0 && EscapeChecker.IsEscapePressed())
            {
                warnings?.Add($"Cancelled by user after {i} items.");
                return true;
            }
            return false;
        }

        /// <summary>ISO 19650 auto-tag of just-created elements — the same path native
        /// Placement-Center output uses, so converted elements flow into tagging/BOQ/
        /// validation. No-op on an empty set; never throws.</summary>
        public static void AutoTag(Document doc, List<ElementId> createdIds, Action<int> setTagged)
        {
            if (createdIds == null || createdIds.Count == 0) return;
            try { setTagged(ModelEngine.AutoTagCreatedElements(doc, createdIds)); }
            catch (Exception ex) { StingLog.Warn($"MEP auto-tag: {ex.Message}"); }
        }
    }

    /// <summary>Shared geometry helpers (P6-4.2 — was duplicated 3D/2D in the fitting and
    /// fixture builders).</summary>
    internal static class MepGeom
    {
        /// <summary>Closest point on segment a→b to p, with the parameter t∈[0,1] and the
        /// distance. <paramref name="planar"/> ignores Z (XY-only), as the host-snap path needs.</summary>
        public static XYZ ClosestPointOnSegment(XYZ a, XYZ b, XYZ p, out double t, out double dist, bool planar = false)
        {
            if (planar) { a = Flat(a); b = Flat(b); p = Flat(p); }
            var ab = b - a;
            double len2 = ab.DotProduct(ab);
            t = len2 > 1e-12 ? (p - a).DotProduct(ab) / len2 : 0;
            t = Math.Max(0, Math.Min(1, t));
            var cp = a + t * ab;
            dist = cp.DistanceTo(p);
            return cp;
        }

        private static XYZ Flat(XYZ v) => new XYZ(v.X, v.Y, 0);
    }
}
