// BipCompat — built-in parameters that Revit renamed between versions.
//
// Revit 2026 renamed UNIFORMAT_CODE → ASSEMBLY_CODE, UNIFORMAT_DESCRIPTION →
// ASSEMBLY_DESCRIPTION and OMNICLASS_CODE → CLASSIFICATION_CODE (OMNICLASS_DESCRIPTION →
// CLASSIFICATION_DESCRIPTION). Naming either spelling in code compiles against one
// Revit and not the other (ROADMAP ELEC-29), so the member is resolved by NAME at run
// time from BipNames' candidate lists: the newer name first, then the older one.
// A version that has neither yields null and the caller skips the read.

using System;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    internal static class BipCompat
    {
        public static readonly BuiltInParameter? AssemblyCode = Resolve(BipNames.AssemblyCode);
        public static readonly BuiltInParameter? AssemblyDescription = Resolve(BipNames.AssemblyDescription);
        public static readonly BuiltInParameter? ClassificationCode = Resolve(BipNames.ClassificationCode);
        public static readonly BuiltInParameter? ClassificationDescription = Resolve(BipNames.ClassificationDescription);
        /// <summary>Null on Revit 2027, which has no such built-in: callers fall back to STING's U-value.</summary>
        public static readonly BuiltInParameter? HeatTransferCoefficient = Resolve(BipNames.HeatTransferCoefficient);

        /// <summary>The first candidate name this Revit's BuiltInParameter defines; null if none.</summary>
        public static BuiltInParameter? Resolve(string[] candidates)
        {
            foreach (string name in candidates ?? Array.Empty<string>())
                if (Enum.IsDefined(typeof(BuiltInParameter), name))
                    return (BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), name);
            return null;
        }

        /// <summary>The element's parameter for a resolved built-in, or null.</summary>
        public static Parameter Get(Element el, BuiltInParameter? bip)
            => el == null || bip == null ? null : el.get_Parameter(bip.Value);
    }
}
