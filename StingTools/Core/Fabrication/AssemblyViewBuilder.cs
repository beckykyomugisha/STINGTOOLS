// StingTools v4 MVP — AssemblyViewBuilder.
//
// Creates the standard five-view set for a v4 fabrication assembly:
//   1. 3D orthographic    via AssemblyViewUtils.Create3DOrthographic
//   2. Plan / part list    via AssemblyViewUtils.CreatePartList
//   3. ISO 6412 axonometric (30deg trimetric) via ViewSection.CreateSection
//      with a transformed bounding box
//   4. Two elevations rotated 0deg and 90deg via ViewSection.CreateSection
//   5. BOM schedule         via AssemblyViewUtils.CreateSingleCategorySchedule
//
// Returns AssemblyViewSet record with the five view ids and a
// schedule id; ShopDrawingComposer (S5.6) places them on the sheet.

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core.Fabrication
{
    public class AssemblyViewSet
    {
        public ElementId AssemblyId   { get; set; }
        public ElementId View3D       { get; set; }
        public ElementId ViewPlan     { get; set; }
        public ElementId ViewIso6412  { get; set; }
        public ElementId Elevation0   { get; set; }
        public ElementId Elevation90  { get; set; }
        public ElementId BomSchedule  { get; set; }
        public List<string> Warnings  { get; } = new List<string>();
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

            // 1. 3D orthographic
            try
            {
                set.View3D = AssemblyViewUtils.Create3DOrthographic(doc, assemblyId)?.Id
                             ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { set.Warnings.Add($"3D ortho: {ex.Message}"); }

            // 2. Plan / part list
            try
            {
                set.ViewPlan = AssemblyViewUtils.CreatePartList(doc, assemblyId)?.Id
                               ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { set.Warnings.Add($"Plan: {ex.Message}"); }

            // 3. ISO 6412 axonometric — 30deg trimetric. Approximated by
            //    a section view with the section box rotated to align
            //    with the X+Y bisector and tilted to expose the assembly
            //    from above. TODO-VERIFY-API: section transform geometry.
            try
            {
                set.ViewIso6412 = CreateSectionAt(doc, ai, RotationDeg.Iso30);
            }
            catch (Exception ex) { set.Warnings.Add($"ISO 6412: {ex.Message}"); }

            // 4. Two cardinal elevations
            try { set.Elevation0  = CreateSectionAt(doc, ai, RotationDeg.Front); }
            catch (Exception ex) { set.Warnings.Add($"Elevation 0: {ex.Message}"); }

            try { set.Elevation90 = CreateSectionAt(doc, ai, RotationDeg.Side);  }
            catch (Exception ex) { set.Warnings.Add($"Elevation 90: {ex.Message}"); }

            // 5. BOM schedule
            try
            {
                ElementId catId = ai.Category?.Id ?? new ElementId(BuiltInCategory.OST_GenericModel);
                set.BomSchedule = AssemblyViewUtils.CreateSingleCategorySchedule(
                    doc, assemblyId, catId)?.Id ?? ElementId.InvalidElementId;
            }
            catch (Exception ex) { set.Warnings.Add($"BOM schedule: {ex.Message}"); }

            return set;
        }

        private enum RotationDeg { Front = 0, Side = 90, Iso30 = 30 }

        private static ElementId CreateSectionAt(Document doc, AssemblyInstance ai, RotationDeg rot)
        {
            // TODO-VERIFY-API:
            //   ViewSection.CreateSection(doc, viewFamilyTypeId, BoundingBoxXYZ)
            // The bounding box transform.Origin = assembly centre and
            // BasisX = horizontal in-plane direction, BasisY = vertical
            // (world Z), BasisZ = view-direction. Scale: 1/2 of largest
            // assembly dimension.

            var bb = ai.get_BoundingBox(null);
            if (bb == null) throw new InvalidOperationException("Assembly has no bounding box");
            var centre = (bb.Min + bb.Max) * 0.5;

            double angleRad = (double)rot * Math.PI / 180.0;
            // BasisZ rotated about world Z so view points horizontally
            XYZ basisZ = new XYZ(Math.Sin(angleRad), -Math.Cos(angleRad), 0).Normalize();
            XYZ basisY = XYZ.BasisZ;
            XYZ basisX = basisY.CrossProduct(basisZ).Normalize();

            // For the ISO 6412 axonometric, tilt 30deg upwards
            if (rot == RotationDeg.Iso30)
            {
                basisZ = new XYZ(basisZ.X, basisZ.Y, -Math.Tan(Math.PI / 6.0)).Normalize();
                basisX = basisY.CrossProduct(basisZ).Normalize();
            }

            var t = Transform.Identity;
            t.Origin = centre;
            t.BasisX = basisX;
            t.BasisY = basisY;
            t.BasisZ = basisZ;

            double dx = bb.Max.X - bb.Min.X;
            double dy = bb.Max.Y - bb.Min.Y;
            double dz = bb.Max.Z - bb.Min.Z;
            double half = Math.Max(Math.Max(dx, dy), dz);
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
