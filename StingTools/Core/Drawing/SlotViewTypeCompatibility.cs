using System;
using System.Linq;

namespace StingTools.Core.Drawing
{
    /// <summary>
    /// Revit-free: does a view of a given Revit <c>ViewType</c> belong in a slot
    /// that declares a given <c>viewType</c> term?
    ///
    /// <para>Slot terms are STING vocabulary ("Plan", "3D", "RCP", "Schematic",
    /// "ISO", "Coordination" …), not Revit enum names ("FloorPlan", "ThreeD",
    /// "CeilingPlan", "DraftingView" …). DTW-63: the producer compared
    /// <c>View.ViewType.ToString()</c> with the slot term raw, so a floor plan in
    /// a "Plan" slot was reported as a type mismatch on most sheets. Both the
    /// producer and <see cref="SheetPlacementBridge"/> now ask this one
    /// predicate.</para>
    ///
    /// <para>Takes the Revit enum member NAME (<c>view.ViewType.ToString()</c>)
    /// so it can be exercised without the Revit API.</para>
    /// </summary>
    public static class SlotViewTypeCompatibility
    {
        /// <summary>
        /// The slot viewType terms <see cref="IsCompatible"/> discriminates on.
        /// Anything else reaches the permissive default arm and matches every
        /// view. DrawingTypeValidator (DT-137-SLOTVT) and DrawingSlotVocabularyTests
        /// read this list so there is one copy.
        /// </summary>
        public static readonly string[] KnownSlotViewTypes =
        {
            "Plan", "RCP", "Section", "Elevation", "Detail", "3D",
            "Schedule", "Legend", "ISO", "Schematic", "Drafting", "Coordination",
        };

        /// <summary>True when the term is one the compatibility switch discriminates on.</summary>
        public static bool IsKnown(string slotViewType)
            => !string.IsNullOrWhiteSpace(slotViewType)
            && KnownSlotViewTypes.Any(k => string.Equals(k, slotViewType.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// True when a view whose Revit <c>ViewType</c> member name is
        /// <paramref name="revitViewTypeName"/> fits a slot declaring
        /// <paramref name="slotViewType"/>. A missing view type or slot term, or
        /// an unknown slot term, is compatible (no false positives; DT-137-SLOTVT
        /// reports unknown terms).
        /// </summary>
        public static bool IsCompatible(string revitViewTypeName, string slotViewType)
        {
            if (string.IsNullOrWhiteSpace(revitViewTypeName) || string.IsNullOrWhiteSpace(slotViewType)) return true;
            bool Is(string name) => string.Equals(revitViewTypeName.Trim(), name, StringComparison.OrdinalIgnoreCase);
            return slotViewType.Trim().ToUpperInvariant() switch
            {
                "PLAN"      => Is("FloorPlan") || Is("AreaPlan") || Is("EngineeringPlan"),
                "RCP"       => Is("CeilingPlan"),
                "SECTION"   => Is("Section"),
                "ELEVATION" => Is("Elevation"),
                "DETAIL"    => Is("Detail"),
                "3D"        => Is("ThreeD"),
                "SCHEDULE"  => Is("Schedule"),
                "LEGEND"    => Is("Legend"),
                "ISO"       => Is("ThreeD"),
                "SCHEMATIC" => Is("DraftingView") || Is("Elevation"),
                // DRAFTING and COORDINATION were both used by shipped profiles
                // and once fell through to the permissive default.
                "DRAFTING"  => Is("DraftingView"),
                "COORDINATION" => Is("FloorPlan") || Is("EngineeringPlan") || Is("ThreeD"),
                _           => true,
            };
        }
    }
}
