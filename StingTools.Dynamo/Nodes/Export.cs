// STING Tools — Export nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>Data exchange nodes: Excel / COBie / IFC / BCF.</summary>
    public static class Export
    {
        /// <summary>Export tagged elements to Excel.</summary>
        [NodeCategory("STING Tools.Export")]
        public static bool ToExcel() => StingDispatcher.Dispatch("ExportToExcel");

        /// <summary>Import Excel changes back onto the model.</summary>
        [NodeCategory("STING Tools.Export")]
        public static bool FromExcel() => StingDispatcher.Dispatch("ImportFromExcel");

        /// <summary>COBie V2.4 export (22 project type presets).</summary>
        [NodeCategory("STING Tools.Export")]
        public static bool COBie() => StingDispatcher.Dispatch("COBieExport");

        /// <summary>IFC export with property mapping.</summary>
        [NodeCategory("STING Tools.Export")]
        public static bool IFC() => StingDispatcher.Dispatch("IFCExport");

        /// <summary>BCF 2.1 export from project issues.</summary>
        [NodeCategory("STING Tools.Export")]
        public static bool BCF() => StingDispatcher.Dispatch("BCFExport");

        /// <summary>FM / O&M handover export.</summary>
        [NodeCategory("STING Tools.Export")]
        public static bool FmHandover() => StingDispatcher.Dispatch("COBieHandoverExport");
    }
}
