// STING Tools — Phase 114 Placement + Routing Extension nodes.
using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>Placement extensions (7 nodes).</summary>
    public static class PlacementExt
    {
        [NodeCategory("STING Tools.Placement.LifeSafety")]
        public static bool SprinklerGrid()     => StingDispatcher.Dispatch("PlcExt_SprinklerGrid");
        [NodeCategory("STING Tools.Placement.Accessibility")]
        public static bool AccessibleWC()      => StingDispatcher.Dispatch("PlcExt_AccessibleWC");
        [NodeCategory("STING Tools.Placement.LifeSafety")]
        public static bool FireExtinguisher()  => StingDispatcher.Dispatch("PlcExt_FireExt");
        [NodeCategory("STING Tools.Placement.LifeSafety")]
        public static bool ExitSigns()         => StingDispatcher.Dispatch("PlcExt_ExitSigns");
        [NodeCategory("STING Tools.Placement.Lighting")]
        public static bool EmergencyLumAll()   => StingDispatcher.Dispatch("PlcExt_EmergencyLumAll");
        [NodeCategory("STING Tools.Placement.Security")]
        public static bool AccessControl()     => StingDispatcher.Dispatch("PlcExt_AccessControl");
        [NodeCategory("STING Tools.Placement.Security")]
        public static bool CCTVCoverage()      => StingDispatcher.Dispatch("PlcExt_CCTVCoverage");
    }

    /// <summary>Routing extensions (7 nodes).</summary>
    public static class RoutingExt
    {
        [NodeCategory("STING Tools.Routing.Layout")]
        public static bool Manhattan()         => StingDispatcher.Dispatch("RtExt_Manhattan");
        [NodeCategory("STING Tools.Routing.Layout")]
        public static bool ClashAvoid()        => StingDispatcher.Dispatch("RtExt_ClashAvoid");
        [NodeCategory("STING Tools.Routing.Electrical")]
        public static bool CableBundle()       => StingDispatcher.Dispatch("RtExt_CableBundle");
        [NodeCategory("STING Tools.Routing.Thermal")]
        public static bool PipeInsulation()    => StingDispatcher.Dispatch("RtExt_PipeInsulation");
        [NodeCategory("STING Tools.Routing.FireSafety")]
        public static bool AutoFireDamper()    => StingDispatcher.Dispatch("RtExt_AutoFireDamper");
        [NodeCategory("STING Tools.Routing.Thermal")]
        public static bool ExpansionLoop()     => StingDispatcher.Dispatch("RtExt_ExpansionLoop");
        [NodeCategory("STING Tools.Routing.Layout")]
        public static bool TrayRiser()         => StingDispatcher.Dispatch("RtExt_TrayRiser");
    }
}
