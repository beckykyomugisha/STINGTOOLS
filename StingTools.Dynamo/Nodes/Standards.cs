// STING Tools — Standards compliance nodes.

using Autodesk.DesignScript.Runtime;
using StingTools.Dynamo.Internal;

namespace StingTools.Dynamo
{
    /// <summary>ISO 19650 / CIBSE / BS / Uniclass audit nodes.</summary>
    public static class Std
    {
        /// <summary>ISO 19650 compliance audit.</summary>
        [NodeCategory("STING Tools.Standards")]
        public static bool ISO19650Audit() => StingDispatcher.Dispatch("ISO19650Deep");

        /// <summary>CIBSE velocity check for MEP systems.</summary>
        [NodeCategory("STING Tools.Standards")]
        public static bool CibseVelocity() => StingDispatcher.Dispatch("CibseVelocity");

        /// <summary>BS 7671 circuit protection audit.</summary>
        [NodeCategory("STING Tools.Standards")]
        public static bool BS7671() => StingDispatcher.Dispatch("BS7671");

        /// <summary>Uniclass 2015 classification validator.</summary>
        [NodeCategory("STING Tools.Standards")]
        public static bool UniclassValidate() => StingDispatcher.Dispatch("UniclassValidator");

        /// <summary>Approved Document Part L energy compliance.</summary>
        [NodeCategory("STING Tools.Standards")]
        public static bool PartL() => StingDispatcher.Dispatch("PartL");
    }
}
