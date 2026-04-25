// STING Tools — Routing category nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>v4 MVP auto-drop + layout nodes.</summary>
    public static class Routing
    {
        /// <summary>Multi-discipline auto-drop from selected fixtures.</summary>
        [NodeCategory("STING Tools.BIM.Routing")]
        public static bool AutoDrop() => StingDispatcher.Dispatch("Routing_AutoDrop");

        /// <summary>Manhattan layout preview for selected MEP runs.</summary>
        [NodeCategory("STING Tools.BIM.Routing")]
        public static bool GenerateLayout() => StingDispatcher.Dispatch("Routing_GenerateLayout");

        /// <summary>Run fill validation across conduit / tray / pipe / duct.</summary>
        [NodeCategory("STING Tools.BIM.Routing")]
        public static bool ValidateFills() => StingDispatcher.Dispatch("Routing_ValidateFills");
    }
}
