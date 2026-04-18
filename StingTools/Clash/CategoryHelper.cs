// CategoryHelper.cs — H1.5. Resolves a Revit Category to its BuiltInCategory
// internal name ("OST_DuctCurves") for use in ClashMeshBuffer / matrix filters.
//
// Why:
// - Category.Name returns the user-visible localized display name ("Ducts",
//   "Walls"). These vary across Revit locales and don't match the matrix DSL
//   which uses the stable OST_* BuiltInCategory enum names.
// - BuiltInCategory enum values are stable across Revit versions and locales,
//   so a matrix cell written as "Category=OST_DuctCurves" works everywhere.
// - Non-standard (user-defined) categories have positive Id.IntegerValue and
//   are NOT in BuiltInCategory. For those we fall back to display name and
//   prefix with "CAT_" so they're distinguishable from BuiltIn entries.
using System;
using Autodesk.Revit.DB;

namespace StingTools.Core.Clash
{
    internal static class CategoryHelper
    {
        /// <summary>
        /// Returns "OST_*" for standard Revit categories; "CAT_&lt;displayName&gt;"
        /// for user-defined categories; empty string for null / unresolved.
        /// </summary>
        public static string GetBuiltInCategoryName(Category c)
        {
            if (c == null) return "";
            try
            {
                int id = c.Id.IntegerValue;
                if (Enum.IsDefined(typeof(BuiltInCategory), id))
                {
                    var bic = (BuiltInCategory)id;
                    return bic.ToString();   // "OST_DuctCurves"
                }
                // User-defined category — fall back to display name with a
                // CAT_ prefix so matrix authors can still target them.
                return "CAT_" + (c.Name ?? "");
            }
            catch
            {
                // Defensive: never throw from the extractor.
                return c.Name ?? "";
            }
        }
    }
}
