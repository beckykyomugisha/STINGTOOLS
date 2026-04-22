// STING Tools — BOQ / cost manager nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>BOQ Cost Manager + NRM2 + dual-currency nodes.</summary>
    public static class BOQ
    {
        /// <summary>Rebuild the BOQ document from the current model.</summary>
        [NodeCategory("STING Tools.BOQ")]
        public static bool Refresh() => StingDispatcher.Dispatch("BOQRefresh");

        /// <summary>Capture a snapshot alongside the project.</summary>
        [NodeCategory("STING Tools.BOQ")]
        public static bool SaveSnapshot() => StingDispatcher.Dispatch("BOQSaveSnapshot");

        /// <summary>Compare two snapshots and report the diff.</summary>
        [NodeCategory("STING Tools.BOQ")]
        public static bool CompareSnapshots() => StingDispatcher.Dispatch("BOQCompareSnapshots");

        /// <summary>Export the BOQ as NRM2-style Excel workbook.</summary>
        [NodeCategory("STING Tools.BOQ")]
        public static bool ExportXlsx() => StingDispatcher.Dispatch("BOQExport");

        /// <summary>Import rate overrides from an edited Excel workbook.</summary>
        [NodeCategory("STING Tools.BOQ")]
        public static bool ImportXlsx() => StingDispatcher.Dispatch("BOQImport");

        /// <summary>Reconcile provisional sums against modelled line items.</summary>
        [NodeCategory("STING Tools.BOQ")]
        public static bool ReconcilePS() => StingDispatcher.Dispatch("BOQReconcile");
    }
}
