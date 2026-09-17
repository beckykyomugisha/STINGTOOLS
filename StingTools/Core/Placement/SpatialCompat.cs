using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using StingTools.Core;

namespace StingTools.Core.Placement
{
    /// <summary>
    /// Rooms and MEP Spaces behind one surface (LIGHTGRID-2).
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// Seven lighting commands collected <c>OST_Rooms</c> and were typed to
    /// <c>OfType&lt;Room&gt;()</c>. On an MEP model — where the lighting designer works in
    /// Spaces, not Rooms — every one of them found nothing, did nothing, and reported
    /// success. `LightingGridCommand` was the first fixed (LIGHTGRID-1) and proved the
    /// shape: the engines underneath already took a <see cref="SpatialElement"/>, and only
    /// the collectors were narrow.
    ///
    /// The three things that are NOT shared between Room and Space are the reason this is a
    /// helper rather than six copies of the same edit:
    ///
    ///   * <b>Number</b> — `Room.Number` and `Space.Number` are declared separately. There
    ///     is no `SpatialElement.Number`, so a cast is unavoidable and easy to forget.
    ///   * <b>Name</b> — `BuiltInParameter.ROOM_NAME` is a Room built-in and returns null on
    ///     a Space. Reading it without a fallback labels every Space with an element id.
    ///   * <b>Containment</b> — `FamilyInstance.Room` and `FamilyInstance.Space` are
    ///     different properties. Checking only the first finds no fixture in any Space.
    ///
    /// Each of those fails SILENTLY and plausibly, which is the failure mode this codebase
    /// produces. Centralising them means a command written next year gets all three.
    /// </summary>
    internal static class SpatialCompat
    {
        /// <summary>
        /// Every Room and MEP Space with a real area, de-duplicated.
        ///
        /// Rooms are collected first so an architectural model yields exactly the order it
        /// did before this helper existed.
        /// </summary>
        internal static List<SpatialElement> Collect(Document doc, double minAreaFt2 = 0.0)
        {
            var result = new List<SpatialElement>();
            if (doc == null) return result;

            var seen = new HashSet<ElementId>();
            foreach (BuiltInCategory bic in new[] { BuiltInCategory.OST_Rooms,
                                                    BuiltInCategory.OST_MEPSpaces })
            {
                try
                {
                    foreach (var se in new FilteredElementCollector(doc)
                                 .OfCategory(bic)
                                 .WhereElementIsNotElementType()
                                 .OfType<SpatialElement>())
                    {
                        if (se.Area > minAreaFt2 && seen.Add(se.Id)) result.Add(se);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"SpatialCompat.Collect({bic}): {ex.Message}");
                }
            }
            return result;
        }

        /// <summary>
        /// The Room OR Space this instance sits in.
        ///
        /// `ParameterHelpers.GetRoomAtElement` returns a Room and nothing else, so a caller
        /// that only has that helper cannot see a Space at all.
        /// </summary>
        internal static SpatialElement SpatialOf(FamilyInstance fi)
        {
            if (fi == null) return null;
            try
            {
                if (fi.Room != null) return fi.Room;
                if (fi.Space != null) return fi.Space;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SpatialCompat.SpatialOf: {ex.Message}");
            }
            return null;
        }

        /// <summary>Room.Number or Space.Number — neither is on SpatialElement.</summary>
        internal static string NumberOf(SpatialElement se)
        {
            try
            {
                if (se is Room r) return r.Number ?? string.Empty;
                if (se is Space sp) return sp.Number ?? string.Empty;
                return se?.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString()
                       ?? string.Empty;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SpatialCompat.NumberOf: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// A display name for either. ROOM_NAME is null on a Space, so this falls through to
        /// <c>SpatialElement.Name</c>, which both carry.
        /// </summary>
        internal static string NameOf(SpatialElement se)
        {
            if (se == null) return string.Empty;
            try
            {
                string n = se.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                if (!string.IsNullOrWhiteSpace(n)) return n;
                return se.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SpatialCompat.NameOf: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Geometric containment. <c>Room.IsPointInRoom</c> and <c>Space.IsPointInSpace</c>
        /// are separate methods with no shared base, so this is the fourth thing that
        /// silently answers "no" for every Space if it is written against Room alone.
        /// </summary>
        internal static bool IsPointInside(SpatialElement se, XYZ pt)
        {
            if (se == null || pt == null) return false;
            try
            {
                if (se is Room r) return r.IsPointInRoom(pt);
                if (se is Space sp) return sp.IsPointInSpace(pt);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SpatialCompat.IsPointInside: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Is this family instance inside that Room or Space?
        ///
        /// Checks BOTH <c>FamilyInstance.Room</c> and <c>FamilyInstance.Space</c>. Checking
        /// only the first is why a Space-based model reports zero fixtures in every space
        /// while the fixtures are plainly there.
        /// </summary>
        internal static bool Contains(FamilyInstance fi, SpatialElement se)
        {
            if (fi == null || se == null) return false;
            try
            {
                if (fi.Room != null && fi.Room.Id == se.Id) return true;
                if (fi.Space != null && fi.Space.Id == se.Id) return true;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SpatialCompat.Contains: {ex.Message}");
            }
            return false;
        }
    }
}
