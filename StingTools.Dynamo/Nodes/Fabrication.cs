// STING Tools — Fabrication category nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>Phase 5 fabrication pipeline nodes.</summary>
    public static class Fab
    {
        /// <summary>Full fabrication package: group → assemble → views → sheets.</summary>
        [NodeCategory("STING Tools.Fabrication")]
        public static bool GeneratePackage() => StingDispatcher.Dispatch("Fabrication_GeneratePackage");

        /// <summary>Export pipe cut list CSV.</summary>
        [NodeCategory("STING Tools.Fabrication")]
        public static bool ExportCutList() => StingDispatcher.Dispatch("Fabrication_ExportCutList");

        /// <summary>Export shop drawing sheet index + filenames.</summary>
        [NodeCategory("STING Tools.Fabrication")]
        public static bool ExportIsometrics() => StingDispatcher.Dispatch("Fabrication_ExportIsometrics");

        /// <summary>Re-emit weld map CSV without rebuilding assemblies.</summary>
        [NodeCategory("STING Tools.Fabrication")]
        public static bool ExportWeldMap() => StingDispatcher.Dispatch("Fabrication_ExportWeldMap");
    }
}
