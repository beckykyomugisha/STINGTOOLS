// StingTools — which tag path a tag rule takes (DTW-83), and the one key
// "already tagged" is measured by (DTW-85).
//
// Rooms, MEP spaces and areas are SPATIAL elements. Revit tags them with a
// SpatialElementTag created by doc.Create.NewRoomTag / NewSpaceTag /
// NewAreaTag — never with IndependentTag.Create(new Reference(room)), which
// either throws (one warning per room, nothing placed) or produces a tag the
// room-tag machinery does not recognise. The annotation runner used to send
// all 40 shipped room / space / area rules down the IndependentTag path.
//
// This file is the Revit-free half: the routing decision, the view kinds each
// spatial tag can live in, and the host key shared by the tagged-element index.

using System;

namespace StingTools.Core.Drawing
{
    public enum SpatialTagKind { None, Room, Space, Area }

    public static class SpatialTagRouting
    {
        /// <summary>
        /// The spatial tag a host category takes, from its BuiltInCategory name
        /// ("OST_Rooms"). None = an ordinary IndependentTag host.
        /// </summary>
        public static SpatialTagKind KindOf(string builtInCategoryName)
        {
            switch ((builtInCategoryName ?? "").Trim().ToUpperInvariant())
            {
                case "OST_ROOMS":     return SpatialTagKind.Room;
                case "OST_MEPSPACES": return SpatialTagKind.Space;
                case "OST_AREAS":     return SpatialTagKind.Area;
                default:              return SpatialTagKind.None;
            }
        }

        /// <summary>
        /// Why a spatial tag of <paramref name="kind"/> cannot go in a view of
        /// <paramref name="viewType"/> (a Revit ViewType name), or null when it can.
        /// Room tags: plans and sections (NewRoomTag refuses anything else).
        /// Space tags: plans. Area tags: area plans only — an area exists nowhere else.
        /// </summary>
        public static string ViewRefusal(SpatialTagKind kind, string viewType)
        {
            var vt = (viewType ?? "").Trim();
            bool plan = vt == "FloorPlan" || vt == "CeilingPlan" || vt == "EngineeringPlan" || vt == "AreaPlan";
            switch (kind)
            {
                case SpatialTagKind.Room:
                    return plan || vt == "Section" ? null
                        : $"room tags go in plan or section views; this is a {Describe(vt)}";
                case SpatialTagKind.Space:
                    return plan && vt != "AreaPlan" ? null
                        : $"space tags go in floor / ceiling / engineering plans; this is a {Describe(vt)}";
                case SpatialTagKind.Area:
                    return vt == "AreaPlan" ? null
                        : $"area tags go in area plans only; this is a {Describe(vt)}";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Whether Revit can tag this kind when the element lives in a linked model.
        /// NewRoomTag takes a LinkElementId; NewSpaceTag and NewAreaTag take the host
        /// document's own element and cannot reach into a link.
        /// </summary>
        public static bool LinkedTaggable(SpatialTagKind kind) => kind == SpatialTagKind.Room;

        private static string Describe(string vt) => string.IsNullOrEmpty(vt) ? "view of unknown type" : vt + " view";
    }

    /// <summary>
    /// Key of a tagged host in the annotation runner's "already tagged" index.
    /// A host element and an element of a linked model can share an ElementId
    /// value, so the two are kept apart; a linked element is identified by the
    /// link INSTANCE as well, because two instances of one link are two things
    /// on the drawing.
    /// </summary>
    public static class TaggedHostKey
    {
        public static string Local(long elementId) => "h" + elementId;

        public static string Linked(long linkInstanceId, long linkedElementId)
            => "l" + linkInstanceId + ":" + linkedElementId;

        /// <summary>
        /// The key for a LinkElementId's three parts: linked when the link
        /// instance id is valid (positive), host otherwise.
        /// </summary>
        public static string From(long hostElementId, long linkInstanceId, long linkedElementId)
            => linkInstanceId > 0 && linkedElementId > 0
                ? Linked(linkInstanceId, linkedElementId)
                : Local(hostElementId);
    }
}
