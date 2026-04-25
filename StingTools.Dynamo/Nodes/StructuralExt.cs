// STING Tools — Phase 112 Structural Extension nodes (10 nodes).
using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>
    /// Structural design-suite extensions — slab rebar, takedown,
    /// wind, seismic, piles, retaining, connections, composite,
    /// tolerances, creep. Backed by EC2/EC3/EC4/EC7 + BS EN 1090.
    /// </summary>
    public static class StructuralExt
    {
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool AutoSlabRebar()       => StingDispatcher.Dispatch("StrExt_AutoSlabRebar");
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool FullColumnTakedown()  => StingDispatcher.Dispatch("StrExt_FullColumnTakedown");
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool WindAutoApply()       => StingDispatcher.Dispatch("StrExt_WindAutoApply");
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool SeismicAutoApply()    => StingDispatcher.Dispatch("StrExt_SeismicAutoApply");
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool PileGroup()           => StingDispatcher.Dispatch("StrExt_PileGroup");
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool RetainingWall()       => StingDispatcher.Dispatch("StrExt_RetainingWall");
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool AutoConnection()      => StingDispatcher.Dispatch("StrExt_AutoConnection");
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool CompositeBeam()       => StingDispatcher.Dispatch("StrExt_CompositeBeam");
        [NodeCategory("STING Tools.Structural.Design")]
        public static bool ToleranceCheck()      => StingDispatcher.Dispatch("StrExt_ToleranceCheck");
        [NodeCategory("STING Tools.Structural.Analysis")]
        public static bool CreepDeflection()     => StingDispatcher.Dispatch("StrExt_CreepDeflection");
    }
}
