// STING Tools — Phase 113 MEP Design nodes (12 nodes).
using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>MEP design automation — cable, panel, breaker, conduit,
    /// grounding, ducts, pumps, transformers, generators, water, drainage,
    /// balance apply.</summary>
    public static class MepDesign
    {
        [NodeCategory("STING Tools.MEP.Electrical")]
        public static bool CableSizeApply()       => StingDispatcher.Dispatch("MepA_CableSizeApply");
        [NodeCategory("STING Tools.MEP.Electrical")]
        public static bool PanelSchedule()        => StingDispatcher.Dispatch("MepA_PanelSchedule");
        [NodeCategory("STING Tools.MEP.Electrical")]
        public static bool BreakerAutoSize()      => StingDispatcher.Dispatch("MepA_BreakerAutoSize");
        [NodeCategory("STING Tools.MEP.Electrical")]
        public static bool AutoSizeConduitAll()   => StingDispatcher.Dispatch("MepA_AutoSizeConduitAll");
        [NodeCategory("STING Tools.MEP.Electrical")]
        public static bool GroundingDesign()      => StingDispatcher.Dispatch("MepA_GroundingDesign");
        [NodeCategory("STING Tools.MEP.HVAC")]
        public static bool DuctStaticRegain()     => StingDispatcher.Dispatch("MepA_DuctStaticRegain");
        [NodeCategory("STING Tools.MEP.HVAC")]
        public static bool PumpSize()             => StingDispatcher.Dispatch("MepA_PumpSize");
        [NodeCategory("STING Tools.MEP.Electrical")]
        public static bool TransformerSize()      => StingDispatcher.Dispatch("MepA_TransformerSize");
        [NodeCategory("STING Tools.MEP.Electrical")]
        public static bool GeneratorSize()        => StingDispatcher.Dispatch("MepA_GeneratorSize");
        [NodeCategory("STING Tools.MEP.Plumbing")]
        public static bool WaterHeaterSize()      => StingDispatcher.Dispatch("MepA_WaterHeaterSize");
        [NodeCategory("STING Tools.MEP.Plumbing")]
        public static bool DrainageSize()         => StingDispatcher.Dispatch("MepA_DrainageSize");
        [NodeCategory("STING Tools.MEP.Intelligence")]
        public static bool BalanceApply()         => StingDispatcher.Dispatch("MepA_BalanceApply");
    }
}
