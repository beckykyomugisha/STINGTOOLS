// STING Tools — Phase 115 Fabrication Extension nodes (10).
using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>Fabrication extensions — ACC publish, weld, NC, ducts, pipes.</summary>
    public static class FabExt
    {
        [NodeCategory("STING Tools.Fabrication.Publish")]
        public static bool ACCPublishShop()    => StingDispatcher.Dispatch("FabExt_ACCPublish");
        [NodeCategory("STING Tools.Fabrication.CNC")]
        public static bool WeldPathExport()    => StingDispatcher.Dispatch("FabExt_WeldPath");
        [NodeCategory("STING Tools.Fabrication.CNC")]
        public static bool ExportNC()          => StingDispatcher.Dispatch("FabExt_ExportNC");
        [NodeCategory("STING Tools.Fabrication.Duct")]
        public static bool DuctSeamAudit()     => StingDispatcher.Dispatch("FabExt_DuctSeamAudit");
        [NodeCategory("STING Tools.Fabrication.Pipe")]
        public static bool PipeSupports()      => StingDispatcher.Dispatch("FabExt_PipeSupports");
        [NodeCategory("STING Tools.Fabrication.Support")]
        public static bool HangerTakedown()    => StingDispatcher.Dispatch("FabExt_HangerTakedown");
        [NodeCategory("STING Tools.Fabrication.Pipe")]
        public static bool FlangeRating()      => StingDispatcher.Dispatch("FabExt_FlangeRating");
        [NodeCategory("STING Tools.Fabrication.Spool")]
        public static bool SpoolWeight()       => StingDispatcher.Dispatch("FabExt_SpoolWeight");
        [NodeCategory("STING Tools.Fabrication.Spool")]
        public static bool TitleBlockFill()    => StingDispatcher.Dispatch("FabExt_TitleBlockFill");
        [NodeCategory("STING Tools.Fabrication.Symbols")]
        public static bool ISOSymbolsFull()    => StingDispatcher.Dispatch("FabExt_ISOSymbolsFull");
    }
}
