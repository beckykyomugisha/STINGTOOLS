// BipNames — candidate BuiltInParameter member names for parameters Revit renamed
// between versions; newest spelling first. Revit-free (StingTools.Tags.Tests compiles
// this file); BipCompat resolves them against the running Revit.

namespace StingTools.Core
{
    public static class BipNames
    {
        public static readonly string[] AssemblyCode = { "ASSEMBLY_CODE", "UNIFORMAT_CODE" };
        public static readonly string[] AssemblyDescription = { "ASSEMBLY_DESCRIPTION", "UNIFORMAT_DESCRIPTION" };
        public static readonly string[] ClassificationCode = { "CLASSIFICATION_CODE", "OMNICLASS_CODE" };
        public static readonly string[] ClassificationDescription = { "CLASSIFICATION_DESCRIPTION", "OMNICLASS_DESCRIPTION" };
        /// <summary>"Heat Transfer Coefficient (U)" — present in 2025/2026, removed in 2027.</summary>
        public static readonly string[] HeatTransferCoefficient = { "ANALYTICAL_HEAT_TRANSFER_COEFFICIENT" };
    }
}
