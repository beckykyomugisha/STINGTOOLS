// STING Tools — Phase 116 Standards Extension nodes (31 nodes).
using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>Standards Extensions — STD-01..10 + REG-01.</summary>
    public static class StdExt
    {
        [NodeCategory("STING Tools.Standards.Audit")]
        public static bool StageCompliance()   => StingDispatcher.Dispatch("StdExt_StageCompliance");
        [NodeCategory("STING Tools.Standards.Regional")]
        public static bool SetRegion()          => StingDispatcher.Dispatch("StdExt_SetRegion");
        [NodeCategory("STING Tools.Standards.Audit")]
        public static bool AccessibilityAudit() => StingDispatcher.Dispatch("StdExt_AccessibilityAudit");
        [NodeCategory("STING Tools.Standards.Audit")]
        public static bool ParkingAudit()       => StingDispatcher.Dispatch("StdExt_ParkingAudit");
        [NodeCategory("STING Tools.Standards.Structural")]
        public static bool LiveLoadAudit()      => StingDispatcher.Dispatch("StdExt_LiveLoadAudit");
        [NodeCategory("STING Tools.Standards.Structural")]
        public static bool LoadCombinations()   => StingDispatcher.Dispatch("StdExt_LoadCombinations");
        [NodeCategory("STING Tools.Standards.Energy")]
        public static bool EUIBenchmark()       => StingDispatcher.Dispatch("StdExt_EUIBenchmark");
        [NodeCategory("STING Tools.Standards.Energy")]
        public static bool WaterUse()           => StingDispatcher.Dispatch("StdExt_WaterUse");
        [NodeCategory("STING Tools.Standards.Audit")]
        public static bool SpaceEfficiency()    => StingDispatcher.Dispatch("StdExt_SpaceEff");
        [NodeCategory("STING Tools.Standards.FM")]
        public static bool LifecycleCost()      => StingDispatcher.Dispatch("StdExt_LifecycleCost");
    }

    /// <summary>20 bulk StandardsAPI wrappers.</summary>
    public static class StdBulk
    {
        [NodeCategory("STING Tools.Standards.HVAC")]
        public static bool Ventilation()         => StingDispatcher.Dispatch("StdBulk_Ventilation");
        [NodeCategory("STING Tools.Standards.Plumbing")]
        public static bool PlumbingPipe()        => StingDispatcher.Dispatch("StdBulk_PlumbingPipe");
        [NodeCategory("STING Tools.Standards.HVAC")]
        public static bool DuctEqualFriction()   => StingDispatcher.Dispatch("StdBulk_DuctEqualFrict");
        [NodeCategory("STING Tools.Standards.HVAC")]
        public static bool Psychrometric()       => StingDispatcher.Dispatch("StdBulk_Psychrometric");
        [NodeCategory("STING Tools.Standards.Electrical")]
        public static bool ArcFlash()            => StingDispatcher.Dispatch("StdBulk_ArcFlash");
        [NodeCategory("STING Tools.Standards.Electrical")]
        public static bool ConduitFill()         => StingDispatcher.Dispatch("StdBulk_ConduitFill");
        [NodeCategory("STING Tools.Standards.Structural")]
        public static bool SteelBeam()           => StingDispatcher.Dispatch("StdBulk_SteelBeam");
        [NodeCategory("STING Tools.Standards.Structural")]
        public static bool ConcreteBeam()        => StingDispatcher.Dispatch("StdBulk_ConcreteBeam");
        [NodeCategory("STING Tools.Standards.Structural")]
        public static bool Foundation()          => StingDispatcher.Dispatch("StdBulk_Foundation");
        [NodeCategory("STING Tools.Standards.Structural")]
        public static bool Seismic()             => StingDispatcher.Dispatch("StdBulk_Seismic");
        [NodeCategory("STING Tools.Standards.LifeSafety")]
        public static bool OccupantLoad()        => StingDispatcher.Dispatch("StdBulk_OccupantLoad");
        [NodeCategory("STING Tools.Standards.LifeSafety")]
        public static bool TravelDistance()      => StingDispatcher.Dispatch("StdBulk_TravelDistance");
        [NodeCategory("STING Tools.Standards.LifeSafety")]
        public static bool EgressWidth()         => StingDispatcher.Dispatch("StdBulk_EgressWidth");
        [NodeCategory("STING Tools.Standards.Audit")]
        public static bool SpaceUtilization()    => StingDispatcher.Dispatch("StdBulk_SpaceUtil");
        [NodeCategory("STING Tools.Standards.LifeSafety")]
        public static bool Hydrant()             => StingDispatcher.Dispatch("StdBulk_Hydrant");
        [NodeCategory("STING Tools.Standards.FM")]
        public static bool MaintenanceCost()     => StingDispatcher.Dispatch("StdBulk_MaintenanceCost");
        [NodeCategory("STING Tools.Standards.Accessibility")]
        public static bool AccessibleToilet()    => StingDispatcher.Dispatch("StdBulk_AccessibleToilet");
        [NodeCategory("STING Tools.Standards.Accessibility")]
        public static bool AccessibleFixtures()  => StingDispatcher.Dispatch("StdBulk_AccessibleFix");
        [NodeCategory("STING Tools.Standards.Energy")]
        public static bool EnergyAnalysis()      => StingDispatcher.Dispatch("StdBulk_EnergyAnalysis");
        [NodeCategory("STING Tools.Standards.LifeSafety")]
        public static bool SprinklerCriteria()   => StingDispatcher.Dispatch("StdBulk_SprinklerCriteria");
    }
}
