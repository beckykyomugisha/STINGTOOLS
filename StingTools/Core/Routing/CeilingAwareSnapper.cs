using StingTools.Core;
// StingTools — CeilingAwareSnapper.
//
// When a drop's intercept Z passes through a Ceiling element, the
// snapper offsets the intercept so the drop terminates at (or just
// above) the false ceiling soffit instead of inside the ceiling
// plenum or below the room. CIBSE Guide B4 §3.6 and standard
// architectural practice keep service drops above ceiling level so
// access panels stay usable.
//
// Geometry:
//
//      ─────── Slab soffit ───────
//
//      ░░░░░░░░░░░░░░░░░░░░░░░░░░░    Ceiling void (services zone)
//
//      ═══════ False ceiling ═══════ ← snap target (Z = ceiling top + buffer)
//
//                  ↓
//      ┌─[ light fixture / outlet ]
//      Room volume
//
// The snapper:
//   1. Finds every Ceiling whose XY bounding box contains the drop
//      origin (cheap — usually 1–3 candidates in a typical model).
//   2. Computes the ceiling's elevation Z (= reference plane + offset).
//   3. If the original intercept Z lies BELOW the ceiling (i.e. the
//      drop would pass through the ceiling), raises the intercept to
//      ceiling Z + buffer.
//   4. If the intercept lies ABOVE the ceiling (already in the void
//      or above the slab) — no change, return original.
//
// The snapper is purely a Z-coordinate refinement; XY is untouched.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Routing
{
    public static class CeilingAwareSnapper
    {
        /// <summary>
        /// Default buffer (mm) above the ceiling soffit. 50 mm matches
        /// typical service-zone clearance from CIBSE Guide B4 — enough
        /// for an access tile to pop without fouling the conduit.
        /// </summary>
        public const double DefaultBufferMm = 50.0;

        /// <summary>
        /// Compute a soffit-snapped target intercept. Returns the
        /// original <paramref name="intercept"/> unchanged when no
        /// ceiling sits between origin and intercept; otherwise returns
        /// a new XYZ at (intercept.X, intercept.Y, ceilingTopFt + buffer).
        /// Failure modes (no ceilings in model / lookup error) all fall
        /// through to the original — soffit snap is opportunistic, never
        /// blocking.
        /// </summary>
        public static XYZ Snap(Document doc, XYZ origin, XYZ intercept, double bufferMm = DefaultBufferMm)
        {
            if (doc == null || origin == null || intercept == null) return intercept;

            try
            {
                double bestCeilingZ = double.NegativeInfinity;
                bool found = false;

                var collector = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Ceilings)
                    .WhereElementIsNotElementType();
                foreach (var el in collector)
                {
                    var bb = el.get_BoundingBox(null);
                    if (bb == null) continue;
                    // XY containment.
                    if (origin.X < bb.Min.X || origin.X > bb.Max.X) continue;
                    if (origin.Y < bb.Min.Y || origin.Y > bb.Max.Y) continue;
                    // Ceiling Z must sit between origin (low) and intercept (high)
                    // for the intercept to "pass through" it. We use bb.Min.Z as
                    // a conservative ceiling-top approximation; real soffit
                    // would be slightly higher but the buffer absorbs the
                    // few-mm error.
                    double cz = bb.Min.Z;
                    if (cz < Math.Min(origin.Z, intercept.Z)) continue;
                    if (cz > Math.Max(origin.Z, intercept.Z)) continue;
                    if (cz > bestCeilingZ) { bestCeilingZ = cz; found = true; }
                }
                if (!found) return intercept;

                double bufferFt = bufferMm / 304.8;
                double snappedZ = bestCeilingZ + bufferFt;

                // Only snap if the new Z is closer to origin than the
                // original intercept — otherwise we'd be moving the
                // drop FURTHER from the host, breaking topology.
                if (Math.Abs(origin.Z - snappedZ) >= Math.Abs(origin.Z - intercept.Z))
                    return intercept;

                return new XYZ(intercept.X, intercept.Y, snappedZ);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"CeilingAwareSnapper.Snap: {ex.Message}");
                return intercept;
            }
        }
    }
}
