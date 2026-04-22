// STING Tools — Model (architectural + MEP element creation) nodes.
// Wraps every ModelCommand so Dynamo graphs can drive the auto-modeling
// engine alongside Revit's built-in "create family instance" nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>
    /// Architectural + MEP element creation nodes backed by STING's
    /// ModelEngine (walls, floors, ceilings, roofs, doors, windows,
    /// columns, beams, MEP fixtures, building shell, ramps, canopies).
    ///
    /// Every node auto-tags its result via TagPipelineHelper so the
    /// ISO 19650 tag + containers are populated before control returns.
    /// </summary>
    public static class Model
    {
        // ── Architectural shell ─────────────────────────────────
        /// <summary>Create a straight wall between two picked points.</summary>
        [NodeCategory("STING Tools.Model.Shell")]
        public static bool CreateWall() => StingDispatcher.Dispatch("ModelCreateWall");

        /// <summary>Create a rectangular room enclosure (4 walls + floor + Room).</summary>
        [NodeCategory("STING Tools.Model.Shell")]
        public static bool CreateRoom() => StingDispatcher.Dispatch("ModelCreateRoom");

        /// <summary>Create a floor slab from a size preset or room boundary.</summary>
        [NodeCategory("STING Tools.Model.Shell")]
        public static bool CreateFloor() => StingDispatcher.Dispatch("ModelCreateFloor");

        /// <summary>Create a rectangular ceiling slab.</summary>
        [NodeCategory("STING Tools.Model.Shell")]
        public static bool CreateCeiling() => StingDispatcher.Dispatch("ModelCreateCeiling");

        /// <summary>Create a roof element.</summary>
        [NodeCategory("STING Tools.Model.Shell")]
        public static bool CreateRoof() => StingDispatcher.Dispatch("ModelCreateRoof");

        /// <summary>One-click building enclosure: 4 walls + floor + roof.</summary>
        [NodeCategory("STING Tools.Model.Shell")]
        public static bool BuildingShell() => StingDispatcher.Dispatch("ModelBuildingShell");

        /// <summary>Part M / BS 8300 compliant ramp (max gradient 1:12).</summary>
        [NodeCategory("STING Tools.Model.Shell")]
        public static bool CreateRamp() => StingDispatcher.Dispatch("ModelCreateRamp");

        /// <summary>External canopy / overhang.</summary>
        [NodeCategory("STING Tools.Model.Shell")]
        public static bool CreateCanopy() => StingDispatcher.Dispatch("ModelCreateCanopy");

        // ── Openings ────────────────────────────────────────────
        /// <summary>Place door families at picked wall insertion points.</summary>
        [NodeCategory("STING Tools.Model.Openings")]
        public static bool PlaceDoor() => StingDispatcher.Dispatch("ModelPlaceDoor");

        /// <summary>Place window families in walls.</summary>
        [NodeCategory("STING Tools.Model.Openings")]
        public static bool PlaceWindow() => StingDispatcher.Dispatch("ModelPlaceWindow");

        // ── Structural primitives ──────────────────────────────
        /// <summary>Place structural columns at picked points.</summary>
        [NodeCategory("STING Tools.Model.Structural")]
        public static bool PlaceColumn() => StingDispatcher.Dispatch("ModelPlaceColumn");

        /// <summary>Create a rectangular array of columns on a grid.</summary>
        [NodeCategory("STING Tools.Model.Structural")]
        public static bool ColumnGrid() => StingDispatcher.Dispatch("ModelColumnGrid");

        /// <summary>Create a structural beam between two points.</summary>
        [NodeCategory("STING Tools.Model.Structural")]
        public static bool CreateBeam() => StingDispatcher.Dispatch("ModelCreateBeam");

        // ── MEP primitives ─────────────────────────────────────
        /// <summary>Create an HVAC duct run along a picked path.</summary>
        [NodeCategory("STING Tools.Model.MEP")]
        public static bool CreateDuct() => StingDispatcher.Dispatch("ModelCreateDuct");

        /// <summary>Create a plumbing or mechanical pipe run.</summary>
        [NodeCategory("STING Tools.Model.MEP")]
        public static bool CreatePipe() => StingDispatcher.Dispatch("ModelCreatePipe");

        /// <summary>Place an MEP fixture (HVAC unit, panel, receptacle).</summary>
        [NodeCategory("STING Tools.Model.MEP")]
        public static bool PlaceFixture() => StingDispatcher.Dispatch("ModelPlaceFixture");

        // ── CAD ingestion ──────────────────────────────────────
        /// <summary>Auto-convert imported DWG to Revit elements.</summary>
        [NodeCategory("STING Tools.Model.CAD")]
        public static bool DWGToModel() => StingDispatcher.Dispatch("ModelDWGToModel");

        /// <summary>Preview extracted DWG geometry before conversion.</summary>
        [NodeCategory("STING Tools.Model.CAD")]
        public static bool DWGPreview() => StingDispatcher.Dispatch("ModelDWGPreview");
    }
}
