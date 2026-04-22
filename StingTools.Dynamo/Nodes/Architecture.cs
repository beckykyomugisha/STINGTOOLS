// STING Tools — Phase 111 Architecture & Shell nodes.
using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>
    /// Architecture & shell automation — BS 5395 / BS 6180 / BS EN 13830 /
    /// BS EN 13914 / Part B / Part K / Part L / Part M.
    /// </summary>
    public static class Architecture
    {
        /// <summary>Auto-stair per BS 5395 + Part K + Part M.</summary>
        [NodeCategory("STING Tools.Architecture.Shell")]
        public static bool AutoStair() => StingDispatcher.Dispatch("Arch_AutoStair");

        /// <summary>Auto-railing per BS 6180 + Part K.</summary>
        [NodeCategory("STING Tools.Architecture.Shell")]
        public static bool AutoRailing() => StingDispatcher.Dispatch("Arch_AutoRailing");

        /// <summary>Curtain wall grid per BS EN 13830.</summary>
        [NodeCategory("STING Tools.Architecture.Shell")]
        public static bool AutoCurtainWall() => StingDispatcher.Dispatch("Arch_AutoCurtainWall");

        /// <summary>Wall opening at picked point.</summary>
        [NodeCategory("STING Tools.Architecture.Shell")]
        public static bool AutoOpening() => StingDispatcher.Dispatch("Arch_AutoOpening");

        /// <summary>Plaster + paint takeoff per BS EN 13914.</summary>
        [NodeCategory("STING Tools.Architecture.Finishes")]
        public static bool AutoPlaster() => StingDispatcher.Dispatch("Arch_AutoPlaster");

        /// <summary>Cover fire + moisture + thermal audit.</summary>
        [NodeCategory("STING Tools.Architecture.Audit")]
        public static bool CoverAudit() => StingDispatcher.Dispatch("Arch_CoverAudit");
    }
}
