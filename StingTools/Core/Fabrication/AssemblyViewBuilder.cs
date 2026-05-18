using StingTools.Core;
// StingTools v4 MVP — AssemblyViewBuilder.
//
// Creates the canonical view set for a v4 fabrication assembly,
// preferring AssemblyViewUtils.* over hand-rolled ViewSection /
// ViewSchedule reinvention.
//
// Views produced:
//   1. 3D orthographic         AssemblyViewUtils.Create3DOrthographic
//   2. Plan view               AssemblyViewUtils.CreatePartList
//      (name is historical — Revit's PartList is actually a 2D plan
//      view scoped to the assembly members)
//   3. Elevation (front)       AssemblyViewUtils.CreateDetailSection
//                               with AssemblyDetailViewOrientation.ElevationFront
//   4. Elevation (side/left)   AssemblyViewUtils.CreateDetailSection
//                               with AssemblyDetailViewOrientation.ElevationLeft
//   5. Elevation (top)         AssemblyViewUtils.CreateDetailSection
//                               with AssemblyDetailViewOrientation.HorizontalDetail
//   6. ISO 6412 axonometric    hand-rolled ViewSection.CreateSection at
//                               30° trimetric (no native API for ISO 6412)
//   7. BOM schedule            AssemblyViewUtils.CreateSingleCategorySchedule
//   8. Material takeoff        AssemblyViewUtils.CreateMaterialTakeoff
//
// Returns AssemblyViewSet record with every view id; ShopDrawingComposer
// places them on the title-block sheet.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication
{
    public class AssemblyViewSet
    {
        public ElementId AssemblyId     { get; set; }
        public ElementId View3D         { get; set; }
        public ElementId ViewPlan       { get; set; }
        public ElementId ViewIso6412    { get; set; }
        public ElementId Elevation0     { get; set; } // Front
        public ElementId Elevation90    { get; set; } // Left / side
        public ElementId ElevationTop   { get; set; } // Top (plan-like)
        public ElementId BomSchedule    { get; set; }
        public ElementId MaterialTakeoff{ get; set; }
        public List<string> Warnings    { get; } = new List<string>();
    }

    public static class AssemblyViewBuilder
    {
        public static AssemblyViewSet BuildViews(Document doc, ElementId assemblyId)
        {
            var set = new AssemblyViewSet { AssemblyId = assemblyId };
            if (doc == null || assemblyId == null || assemblyId == ElementId.InvalidElementId)
            {
                set.Warnings.Add("AssemblyViewBuilder: invalid assembly id");
                return set;
            }
            var ai = doc.GetElement(assemblyId) as AssemblyInstance;
            if (ai == null)
            {
                set.Warnings.Add("AssemblyViewBuilder: id is not an AssemblyInstance");
                return set;
            }

            bool allowsViews = false;
            try { allowsViews = ai.AllowsAssemblyViewCreation(); }
            catch (Exception ex) { set.Warnings.Add($"AllowsAssemblyViewCreation: {ex.Message}"); }
            if (!allowsViews)
            {
                set.Warnings.Add("Assembly reports AllowsAssemblyViewCreation == false; skipping view creation.");
                return set;
            }

            // 1. 3D orthographic — native API.
            try
            {
                var v3d = AssemblyViewUtils.Create3DOrthographic(doc, assemblyId);
                set.View3D = v3d?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { set.Warnings.Add($"3D ortho: {ex.Message}"); }

            // 2. Plan / part list — native API. Revit creates a 2D plan
            //    view scoped to the assembly, with the part list label
            //    pre-bound to the schedule created in step 7.
            try
            {
                var vpl = AssemblyViewUtils.CreatePartList(doc, assemblyId);
                set.ViewPlan = vpl?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { set.Warnings.Add($"Plan: {ex.Message}"); }

            // 3, 4, 5. Elevations — native API.
            set.Elevation0   = TryCreateDetailSection(doc, assemblyId,
                AssemblyDetailViewOrientation.ElevationFront, "Elevation front", set);
            set.Elevation90  = TryCreateDetailSection(doc, assemblyId,
                AssemblyDetailViewOrientation.ElevationLeft,  "Elevation left",  set);
            set.ElevationTop = TryCreateDetailSection(doc, assemblyId,
                AssemblyDetailViewOrientation.HorizontalDetail, "Top detail",    set);

            // 6. ISO 6412 axonometric — no native API.
            //    Emit a 30° trimetric Section via ViewSection.CreateSection
            //    with a rotated BoundingBoxXYZ. ISO 6412 isometrics
            //    are a text-file-plus-Isogen workflow in industry; this
            //    view is a best-effort axonometric for the shop package.
            try
            {
                set.ViewIso6412 = CreateIso6412Section(doc, ai);
            }
            catch (Exception ex) { set.Warnings.Add($"ISO 6412: {ex.Message}"); }

            // 7. BOM schedule — native API, scoped to assembly category.
            try
            {
                ElementId catId = ai.Category?.Id ?? new ElementId(BuiltInCategory.OST_GenericModel);
                var sch = AssemblyViewUtils.CreateSingleCategorySchedule(doc, assemblyId, catId);
                set.BomSchedule = sch?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { set.Warnings.Add($"BOM schedule: {ex.Message}"); }

            // 8. Material takeoff — native API. Provides a quantity /
            //    material breakdown that the shop uses for procurement.
            try
            {
                var mat = AssemblyViewUtils.CreateMaterialTakeoff(doc, assemblyId);
                set.MaterialTakeoff = mat?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { set.Warnings.Add($"Material takeoff: {ex.Message}"); }

            return set;
        }

        private static ElementId TryCreateDetailSection(
            Document doc, ElementId assemblyId,
            AssemblyDetailViewOrientation orientation, string label,
            AssemblyViewSet set)
        {
            try
            {
                var view = AssemblyViewUtils.CreateDetailSection(doc, assemblyId, orientation);
                return view?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex)
            {
                set.Warnings.Add($"{label}: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        private static ElementId CreateIso6412Section(Document doc, AssemblyInstance ai)
        {
            var bb = ai.get_BoundingBox(null);
            if (bb == null) throw new InvalidOperationException("Assembly has no bounding box");
            var centre = (bb.Min + bb.Max) * 0.5;

            // 30° trimetric: view direction is in the horizontal plane
            // bisecting X and Y, tilted up 30° so Z is projected.
            const double deg30 = Math.PI / 6.0;
            XYZ horiz = new XYZ(Math.Cos(deg30), -Math.Sin(deg30), 0).Normalize();
            // Tilt the view direction 30° upwards.
            XYZ basisZ = new XYZ(horiz.X, horiz.Y, -Math.Tan(deg30)).Normalize();
            XYZ basisY = XYZ.BasisZ;
            XYZ basisX = basisY.CrossProduct(basisZ).Normalize();

            var t = Transform.Identity;
            t.Origin = centre;
            t.BasisX = basisX;
            t.BasisY = basisY;
            t.BasisZ = basisZ;

            double dx = bb.Max.X - bb.Min.X;
            double dy = bb.Max.Y - bb.Min.Y;
            double dz = bb.Max.Z - bb.Min.Z;
            double half = 0.5 * Math.Sqrt(dx * dx + dy * dy + dz * dz);

            var sectionBb = new BoundingBoxXYZ
            {
                Transform = t,
                Min = new XYZ(-half, -half, -half),
                Max = new XYZ( half,  half,  half)
            };

            ElementId vftId = FindViewFamilyType(doc, ViewFamily.Section);
            if (vftId == null || vftId == ElementId.InvalidElementId)
                throw new InvalidOperationException("No Section ViewFamilyType found");

            var view = ViewSection.CreateSection(doc, vftId, sectionBb);
            return view?.Id ?? ElementId.InvalidElementId;
        }

        private static ElementId FindViewFamilyType(Document doc, ViewFamily family)
        {
            try
            {
                foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)))
                {
                    if (el is ViewFamilyType vft && vft.ViewFamily == family) return vft.Id;
                }
            }
            catch (Exception ex) { StingLog.Warn($"AssemblyViewBuilder: FindViewFamilyType: {ex.Message}"); }
            return ElementId.InvalidElementId;
        }
    }
}
