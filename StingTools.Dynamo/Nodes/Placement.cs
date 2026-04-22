// STING Tools — Placement category nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>v4 MVP fixture placement nodes.</summary>
    public static class Placement
    {
        /// <summary>Place fixtures per STING_PLACEMENT_RULES.json into scoped rooms.</summary>
        [NodeCategory("STING Tools.BIM.Placement")]
        public static bool PlaceFixtures() => StingDispatcher.Dispatch("Placement_PlaceFixtures");

        /// <summary>Lighting grid per BS EN 12464-1 for the active room.</summary>
        [NodeCategory("STING Tools.BIM.Placement")]
        public static bool LightingGrid() => StingDispatcher.Dispatch("Placement_LightingGrid");

        /// <summary>Extract project-override rules from placed fixtures.</summary>
        [NodeCategory("STING Tools.BIM.Placement")]
        public static bool LearnFromModel() => StingDispatcher.Dispatch("Placement_Learn");
    }
}
