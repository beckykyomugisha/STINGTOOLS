// STING Tools — Sheet Manager nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>Sheet Manager + compliance + batch print nodes.</summary>
    public static class Sheets
    {
        /// <summary>Auto-layout viewports on the active sheet.</summary>
        [NodeCategory("STING Tools.Sheets")]
        public static bool AutoLayout() => StingDispatcher.Dispatch("AutoLayout");

        /// <summary>Place unplaced views on new or existing sheets.</summary>
        [NodeCategory("STING Tools.Sheets")]
        public static bool PlaceUnplaced() => StingDispatcher.Dispatch("PlaceUnplaced");

        /// <summary>Clone a sheet with its viewports.</summary>
        [NodeCategory("STING Tools.Sheets")]
        public static bool CloneSheet() => StingDispatcher.Dispatch("CloneSheet");

        /// <summary>ISO 19650 sheet compliance audit.</summary>
        [NodeCategory("STING Tools.Sheets")]
        public static bool ComplianceCheck() => StingDispatcher.Dispatch("SheetComplianceCheck");

        /// <summary>Export sheet register CSV.</summary>
        [NodeCategory("STING Tools.Sheets")]
        public static bool ExportRegister() => StingDispatcher.Dispatch("ExportSheetRegister");

        /// <summary>Batch export sheets to PDF.</summary>
        [NodeCategory("STING Tools.Sheets")]
        public static bool BatchPrint() => StingDispatcher.Dispatch("BatchPrint");
    }
}
