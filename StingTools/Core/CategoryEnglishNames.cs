// CategoryEnglishNames — the English Revit category name for a BuiltInCategory.
//
// TAGACC-6 (tagging accuracy review, 2026-09-29). Every tagging table (DiscMap,
// ProdMap, the SYS defaults, the skip list, KnownCategories) is keyed on the
// category's DISPLAY name, and Category.Name is localised: on a French Revit a
// door is "Portes", a duct "Gaines". None of those keys match, so every element
// read as NotTaggable and a non-English install tagged nothing, silently.
//
// The English name cannot be asked of a localised Revit, so it is derived from
// the BuiltInCategory enum name (OST_MechanicalEquipment → "Mechanical Equipment")
// with the exceptions Revit's naming does not follow written out. Revit-free so
// it is unit-tested; ParameterHelpers.GetCategoryName uses it only when the
// localised name is not a known key, so an English install is unaffected.

using System;
using System.Collections.Generic;
using System.Text;

namespace StingTools.Core
{
    public static class CategoryEnglishNames
    {
        // Enum names whose English display name is not their camel-case split.
        private static readonly Dictionary<string, string> Exceptions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["OST_DuctCurves"]              = "Ducts",
                ["OST_PipeCurves"]              = "Pipes",
                ["OST_FlexDuctCurves"]          = "Flex Ducts",
                ["OST_FlexPipeCurves"]          = "Flex Pipes",
                ["OST_DuctTerminal"]            = "Air Terminals",
                ["OST_DuctAccessory"]           = "Duct Accessories",
                ["OST_PipeAccessory"]           = "Pipe Accessories",
                ["OST_DuctFitting"]             = "Duct Fittings",
                ["OST_PipeFitting"]             = "Pipe Fittings",
                ["OST_CableTray"]               = "Cable Trays",
                ["OST_CableTrayFitting"]        = "Cable Tray Fittings",
                ["OST_Conduit"]                 = "Conduits",
                ["OST_ConduitFitting"]          = "Conduit Fittings",
                ["OST_DuctInsulations"]         = "Duct Insulations",
                ["OST_PipeInsulations"]         = "Pipe Insulations",
                ["OST_DuctLinings"]             = "Duct Linings",
                ["OST_StructColumns"]           = "Structural Columns",
                ["OST_StructuralFoundation"]    = "Structural Foundations",
                ["OST_StructuralStiffener"]     = "Structural Stiffeners",
                ["OST_StructuralTruss"]         = "Structural Trusses",
                ["OST_StructConnections"]       = "Structural Connections",
                ["OST_Rebar"]                   = "Structural Rebar",
                ["OST_SpecialityEquipment"]     = "Specialty Equipment",
                ["OST_GenericModel"]            = "Generic Models",
                ["OST_StairsRailing"]           = "Railings",
                ["OST_CurtainWallPanels"]       = "Curtain Panels",
                ["OST_CurtainWallMullions"]     = "Curtain Wall Mullions",
                ["OST_Wire"]                    = "Wires",
                ["OST_ElectricalCircuit"]       = "Electrical Circuits",
                ["OST_TemporaryStructure"]      = "Temporary Structures",
                ["OST_MEPSpaces"]               = "Spaces",
                ["OST_Mass"]                    = "Mass",
            };

        /// <summary>
        /// English display names to try for a BuiltInCategory enum name, best first:
        /// the written exception, then the camel-case split, then that split pluralised.
        /// Empty for a blank or non-"OST_" name. The caller keeps the first one its own
        /// tables know, so an over-eager candidate costs a failed lookup, never a wrong
        /// category.
        /// </summary>
        public static IReadOnlyList<string> Candidates(string builtInCategoryName)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(builtInCategoryName)) return result;
            string n = builtInCategoryName.Trim();
            if (!n.StartsWith("OST_", StringComparison.Ordinal) || n.Length <= 4) return result;

            if (Exceptions.TryGetValue(n, out string known)) result.Add(known);

            string split = SplitCamel(n.Substring(4));
            if (split.Length > 0)
            {
                if (!result.Contains(split)) result.Add(split);
                string plural = split.EndsWith("s", StringComparison.Ordinal) ? split
                              : split.EndsWith("y", StringComparison.Ordinal) ? split.Substring(0, split.Length - 1) + "ies"
                              : split + "s";
                if (!result.Contains(plural)) result.Add(plural);
            }
            return result;
        }

        /// <summary>"MechanicalEquipment" → "Mechanical Equipment"; "MEPSpaces" → "MEP Spaces".</summary>
        public static string SplitCamel(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_') { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); continue; }
                bool boundary = i > 0 && char.IsUpper(c)
                    && (char.IsLower(s[i - 1])
                        || (i + 1 < s.Length && char.IsLower(s[i + 1]) && char.IsUpper(s[i - 1])));
                if (boundary && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}
