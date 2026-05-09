// StingTools — SlabSoffitRouter.
//
// Mirror of InWallChaseRouter for floor slabs. Routes Pipe / Conduit
// segments parallel to the underside of a host floor slab at a
// configurable offset down from the slab soffit, respecting the
// slab's compound structure and the conduit's outer-diameter +
// insulation + clearance budget.
//
// Why: an electrical / plumbing run that needs to follow a slab
// soffit (typical for ground-floor "underfloor" services or for a
// suspended-ceiling void below a structural slab) currently falls
// through to the L/Z router which doesn't read slab thickness — it
// happily places a conduit IN the slab geometry. This router does
// what InWallChaseRouter does for walls: reads the slab's compound
// structure, computes the available service zone underneath, and
// rejects routes that don't fit.
//
// Geometric model:
//
//      ╔═════════════════════════════════╗   level Z
//      ║      structural concrete        ║
//      ╚═════════════════════════════════╝
//      ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░    finish layer (e.g. plaster)
//                  ↓ offset = finish thickness + clearance
//      ┄┄┄┄[ conduit centreline ]┄┄┄┄┄┄┄┄    soffit-routed conduit
//                  ↓
//      ────────────────────────────────────   ceiling soffit (underside of slab)
//                  ↓ optional service-zone gap
//      ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─    suspended ceiling (if any)
//
// available service zone = slab finish layers (interior side) +
// optional declared service zone below the soffit. If the available
// zone < required (conduit OD + insulation + clearance), the route
// is rejected with the same warn-and-skip semantics InWallChaseRouter
// uses.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Placement
{
    public sealed class SlabSoffitRouter
    {
        private const double FtToMm = 304.8;
        private const double MmToFt = 1.0 / 304.8;

        public sealed class SoffitRouteResult
        {
            public List<ElementId> CreatedSegments { get; } = new List<ElementId>();
            public List<string>    Warnings        { get; } = new List<string>();
            public int             RejectedSegments     { get; set; }
            public double          AvailableServiceMm   { get; set; }
            public double          RequiredServiceMm    { get; set; }
            public double          ResolvedOffsetMm     { get; set; }
        }

        private readonly Document _doc;

        /// <summary>
        /// Default service-zone gap (mm) between the soffit and the
        /// conduit centreline when the slab has no finish layer to
        /// claim. 50 mm matches typical suspended-ceiling void
        /// requirements before any cable tray / luminaire allowance.
        /// </summary>
        public double DefaultServiceZoneMm { get; set; } = 50.0;

        public SlabSoffitRouter(Document doc) { _doc = doc; }

        /// <summary>
        /// Route a single soffit-following segment from <paramref name="startPoint"/>
        /// to <paramref name="endPoint"/> below <paramref name="hostFloor"/>.
        /// Both endpoints' Z are recomputed to (slab top − slab thickness
        /// − offset) so the run stays at a uniform soffit-relative height.
        /// </summary>
        public SoffitRouteResult Route(
            Floor hostFloor, XYZ startPoint, XYZ endPoint,
            double conduitOdMm, double insulationMm,
            ElementId conduitTypeId)
        {
            var result = new SoffitRouteResult();
            if (_doc == null || hostFloor == null || startPoint == null || endPoint == null)
            {
                result.Warnings.Add("SlabSoffitRouter: null input.");
                return result;
            }

            try
            {
                // 1) Resolve slab thickness + service-zone budget.
                var (availableMm, requiredMm, slabThicknessMm) =
                    ResolveSoffitDepth(hostFloor, conduitOdMm, insulationMm);
                result.AvailableServiceMm = availableMm;
                result.RequiredServiceMm  = requiredMm;

                if (availableMm <= 0)
                {
                    result.Warnings.Add(
                        $"SlabSoffitRouter: floor '{hostFloor.FloorType?.Name}' has no compound " +
                        $"structure or no detectable finish layer below the structural core; " +
                        $"using default {DefaultServiceZoneMm:F0} mm service zone.");
                    availableMm = DefaultServiceZoneMm;
                }
                if (requiredMm > availableMm)
                {
                    result.Warnings.Add(
                        $"SlabSoffitRouter: required {requiredMm:F0} mm exceeds available " +
                        $"{availableMm:F0} mm in slab '{hostFloor.FloorType?.Name}'. " +
                        $"Rejected — increase service zone or use a thinner conduit.");
                    result.RejectedSegments++;
                    return result;
                }

                // 2) Resolve the soffit Z. Slab "top" is the host's level
                //    elevation; soffit Z = slab top − slab thickness. The
                //    conduit centreline sits offsetMm below the soffit.
                double offsetMm = (conduitOdMm * 0.5) + insulationMm + 5.0;     // 5 mm placing clearance
                result.ResolvedOffsetMm = offsetMm;
                double slabTopFt = hostFloor.LookupParameter("Elevation at Top")?.AsDouble()
                                ?? GetLevelZ(hostFloor.LevelId);
                double soffitFt  = slabTopFt - (slabThicknessMm * MmToFt);
                double zFt       = soffitFt - (offsetMm * MmToFt);

                XYZ s = new XYZ(startPoint.X, startPoint.Y, zFt);
                XYZ e = new XYZ(endPoint.X,   endPoint.Y,   zFt);

                // 3) Create the conduit. Manhattan L: x-axis run, then y-axis run,
                //    so the soffit segments stay axis-aligned (the slab grid is
                //    typically rectilinear, and orthogonal runs are easier to
                //    coordinate with cable trays + ceiling grid).
                XYZ corner = new XYZ(e.X, s.Y, zFt);
                if (conduitTypeId == null || conduitTypeId == ElementId.InvalidElementId)
                {
                    var ct = new FilteredElementCollector(_doc)
                        .OfClass(typeof(ConduitType))
                        .Cast<ConduitType>()
                        .FirstOrDefault();
                    if (ct == null)
                    {
                        result.Warnings.Add("No ConduitType available — cannot route soffit segment.");
                        return result;
                    }
                    conduitTypeId = ct.Id;
                }

                ElementId levelId = hostFloor.LevelId ?? ElementId.InvalidElementId;
                if (s.DistanceTo(corner) > 0.01)
                {
                    var c1 = Conduit.Create(_doc, conduitTypeId, s, corner, levelId);
                    if (c1 != null)
                    {
                        result.CreatedSegments.Add(c1.Id);
                        TryStampSoffit(c1, slabThicknessMm, offsetMm);
                    }
                }
                if (corner.DistanceTo(e) > 0.01)
                {
                    var c2 = Conduit.Create(_doc, conduitTypeId, corner, e, levelId);
                    if (c2 != null)
                    {
                        result.CreatedSegments.Add(c2.Id);
                        TryStampSoffit(c2, slabThicknessMm, offsetMm);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"SlabSoffitRouter.Route: {ex.Message}");
            }
            return result;
        }

        // ── helpers ─────────────────────────────────────────────────────

        private (double availableMm, double requiredMm, double slabThicknessMm)
            ResolveSoffitDepth(Floor floor, double conduitOdMm, double insulationMm)
        {
            double availableMm  = 0;
            double slabThickMm  = 0;
            try
            {
                var cs = floor.FloorType?.GetCompoundStructure();
                if (cs != null)
                {
                    var layers = cs.GetLayers();
                    if (layers != null)
                    {
                        int structuralIdx = -1;
                        try { structuralIdx = cs.StructuralMaterialIndex; } catch { }
                        // Walk EVERY layer to compute slab thickness; walk only
                        // INTERIOR (= below-soffit) finish layers to compute
                        // the available service zone. Floor compound layers
                        // are ordered TOP → BOTTOM in the API, so the bottom
                        // (soffit-side) finish layers come last.
                        for (int i = 0; i < layers.Count; i++)
                            slabThickMm += Math.Max(0.0, layers[i].Width * FtToMm);
                        for (int i = layers.Count - 1; i >= 0; i--)
                        {
                            var l = layers[i];
                            if (l == null) continue;
                            if (i == structuralIdx) break;
                            if (l.Function == MaterialFunctionAssignment.Structure) break;
                            availableMm += Math.Max(0.0, l.Width * FtToMm);
                        }
                    }
                }
                // Fallback when the floor has no compound structure: use
                // BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM if available,
                // else 200 mm typical RC slab.
                if (slabThickMm <= 0)
                {
                    var p = floor.LookupParameter("Thickness")
                         ?? floor.LookupParameter("Default Thickness");
                    if (p != null && p.StorageType == StorageType.Double)
                        slabThickMm = p.AsDouble() * FtToMm;
                    if (slabThickMm <= 0) slabThickMm = 200.0;
                }
            }
            catch (Exception ex) { StingLog.Warn($"SlabSoffitRouter.ResolveSoffitDepth: {ex.Message}"); }

            double requiredMm = conduitOdMm + 2 * insulationMm + 5.0; // 5 mm placing clearance
            return (availableMm, requiredMm, slabThickMm);
        }

        private double GetLevelZ(ElementId levelId)
        {
            try
            {
                var lvl = _doc.GetElement(levelId) as Level;
                return lvl?.Elevation ?? 0;
            }
            catch { return 0; }
        }

        private static void TryStampSoffit(Element el, double slabThicknessMm, double offsetMm)
        {
            try
            {
                ParameterHelpers.SetString(el, "ELC_CDT_INSTALL_METHOD_TXT",
                    "SOFFIT", overwrite: true);
                ParameterHelpers.SetString(el, "ELC_CDT_SOFFIT_OFFSET_MM",
                    offsetMm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                    overwrite: false);
            }
            catch (Exception ex) { StingLog.Warn($"TryStampSoffit: {ex.Message}"); }
        }
    }
}
