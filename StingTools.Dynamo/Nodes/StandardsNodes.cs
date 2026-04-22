// STING Tools — Standards & compliance calculation nodes (Phase 110).
// Surfaces the six Std_* commands wrapping the StingTools.Standards
// library. Nodes open the shared numeric prompt on the Revit API
// thread, so graph-level inputs currently drive the dialog defaults
// rather than the run values — future enhancement to accept Dynamo
// numeric inputs directly.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>
    /// Code-compliant engineering calculations — cable sizing, wind
    /// load, lighting, HVAC cooling, egress + travel distance,
    /// sprinkler design — backed by the 22,800-line
    /// StingTools.Standards calc library covering BS 7671, IEC 60364,
    /// NEC 310, ASCE 7, Eurocode EN 1991-1-4, BS 6399-2, CIBSE /
    /// EN 12464-1, ASHRAE 62.1, CIBSE Guide A, IBC, NFPA 101,
    /// BS 9999, NFPA 13, BS EN 12845.
    /// </summary>
    public static class Standards
    {
        /// <summary>BS 7671 / IEC 60364 / NEC 310 cable sizing.</summary>
        [NodeCategory("STING Tools.Standards.Electrical")]
        public static bool CableSize() => StingDispatcher.Dispatch("Std_CalcCableSize");

        /// <summary>ASCE 7 / Eurocode 1991-1-4 / BS 6399-2 wind load.</summary>
        [NodeCategory("STING Tools.Standards.Structural")]
        public static bool WindLoad() => StingDispatcher.Dispatch("Std_CalcWindLoad");

        /// <summary>CIBSE / EN 12464-1 / IES lux calculation.</summary>
        [NodeCategory("STING Tools.Standards.Lighting")]
        public static bool Lighting() => StingDispatcher.Dispatch("Std_CalcLighting");

        /// <summary>ASHRAE / CIBSE Guide A HVAC cooling load.</summary>
        [NodeCategory("STING Tools.Standards.HVAC")]
        public static bool CoolingLoad() => StingDispatcher.Dispatch("Std_CalcCoolingLoad");

        /// <summary>IBC / NFPA 101 / BS 9999 egress + travel distance.</summary>
        [NodeCategory("STING Tools.Standards.LifeSafety")]
        public static bool Egress() => StingDispatcher.Dispatch("Std_CalcEgress");

        /// <summary>NFPA 13 / BS EN 12845 sprinkler hydraulic design.</summary>
        [NodeCategory("STING Tools.Standards.LifeSafety")]
        public static bool DesignSprinkler() => StingDispatcher.Dispatch("Std_DesignSprinkler");
    }
}
