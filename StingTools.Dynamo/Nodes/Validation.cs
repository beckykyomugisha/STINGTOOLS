// STING Tools — Validation category nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>Five v4 MVP validators + aggregate runner.</summary>
    public static class Validation
    {
        /// <summary>Run all five validators (Connectivity, Fill, Spec, Termination, Slope).</summary>
        [NodeCategory("STING Tools.BIM.Validation")]
        public static bool RunAll() => StingDispatcher.Dispatch("Validation_RunAll");
    }
}
