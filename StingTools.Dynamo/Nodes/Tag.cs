// STING Tools — Tag category nodes.
//
// All nodes in "STING Tools.BIM.Tag". Delegates to the corresponding
// IExternalCommand via StingDispatcher.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>
    /// STING tagging workflow nodes.
    /// </summary>
    public static class Tag
    {
        /// <summary>Auto-tag every element in the active view.</summary>
        /// <returns>True when dispatch succeeded.</returns>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool AutoTag() => StingDispatcher.Dispatch("AutoTag");

        /// <summary>Tag only elements that have no STING tag yet.</summary>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool TagNewOnly() => StingDispatcher.Dispatch("TagNewOnly");

        /// <summary>Tag every taggable element across the whole project.</summary>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool BatchTag() => StingDispatcher.Dispatch("BatchTag");

        /// <summary>Tag currently selected elements only.</summary>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool TagSelected() => StingDispatcher.Dispatch("TagSelected");

        /// <summary>Force-overwrite tags on currently selected elements.</summary>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool ReTag() => StingDispatcher.Dispatch("ReTag");

        /// <summary>
        /// Validate every tag for ISO 19650 + cross-token consistency.
        /// </summary>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool ValidateTags() => StingDispatcher.Dispatch("ValidateTags");

        /// <summary>Auto-fix duplicate ASS_TAG_1 values.</summary>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool FixDuplicates() => StingDispatcher.Dispatch("FixDuplicates");

        /// <summary>Show per-discipline tag completeness dashboard.</summary>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool CompletenessDashboard() => StingDispatcher.Dispatch("CompletenessDashboard");

        /// <summary>Export comprehensive asset register CSV (40+ columns).</summary>
        [NodeCategory("STING Tools.BIM.Tag")]
        public static bool ExportRegister() => StingDispatcher.Dispatch("TagRegisterExport");
    }
}
