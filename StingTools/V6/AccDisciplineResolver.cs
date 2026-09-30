// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccDisciplineResolver.cs
//
// ACC clash data carries no Revit category - only the NAME of each clashing model - so
// the discipline that drives clash triage severity has to come from that name.
//
// The previous rule was a chain of substring tests, structural first:
// n.Contains("STR") made "KUT-ELEC-DISTRIBUTION.rvt" structural (diSTRibution), and
// "CONSTRUCTION" and "STREET" with it. Severity then treated an electrical board clash as
// a structural one.
//
// Now, in order, first answer wins:
//   1. the project's own map (acc_settings.json "disciplineMap": token → code), because
//      only the project knows its model-naming habits;
//   2. the ISO 19650 role field, when the name follows Project-Originator-Volume-Level-
//      Type-Role-Number (the naming STING itself produces and KUT's BEP mandates);
//   3. whole-word keywords.
// Tokens are compared WHOLE (split on - _ . space), never as substrings.
//
// Revit-free and log-free.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StingTools.V6
{
    public static class AccDisciplineResolver
    {
        /// <summary>ISO 19650-2 UK NA role codes → STING discipline.</summary>
        private static readonly Dictionary<string, string> IsoRoles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = "A", ["I"] = "A",            // architect, interior designer
            ["S"] = "S",                          // structural engineer
            ["M"] = "M", ["H"] = "M",            // mechanical, HVAC
            ["E"] = "E",                          // electrical
            ["P"] = "P", ["D"] = "P",            // public health, drainage
        };

        private static readonly Dictionary<string, string> Keywords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["STRUCT"] = "S", ["STRUCTURAL"] = "S", ["STRUCTURE"] = "S", ["STR"] = "S", ["STRU"] = "S",
            ["MECH"] = "M", ["MECHANICAL"] = "M", ["HVAC"] = "M", ["DUCT"] = "M", ["DUCTWORK"] = "M", ["AC"] = "M",
            ["PLUMB"] = "P", ["PLUMBING"] = "P", ["PIPE"] = "P", ["PIPING"] = "P", ["PH"] = "P", ["DRAIN"] = "P", ["DRAINAGE"] = "P",
            ["ELEC"] = "E", ["ELECTRICAL"] = "E", ["ELE"] = "E", ["CABLE"] = "E", ["TRAY"] = "E", ["POWER"] = "E", ["LIGHTING"] = "E",
            ["FIRE"] = "FP", ["SPRINKLER"] = "FP", ["SPRINKLERS"] = "FP", ["SPRINK"] = "FP", ["FP"] = "FP",
            ["ARCH"] = "A", ["ARC"] = "A", ["ARCHITECTURAL"] = "A", ["ARCHITECTURE"] = "A",
        };

        /// <summary>Discipline code for a model name, or "" when nothing identifies it.</summary>
        public static string Discipline(string documentName, IReadOnlyDictionary<string, string> projectMap = null)
        {
            string name = Path.GetFileNameWithoutExtension((documentName ?? string.Empty).Trim());
            if (name.Length == 0) return string.Empty;
            var tokens = name.Split(new[] { '-', '_', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (projectMap != null && projectMap.Count > 0)
                foreach (var t in tokens)
                    foreach (var kv in projectMap)
                        if (string.Equals(kv.Key, t, StringComparison.OrdinalIgnoreCase)) return kv.Value.Trim().ToUpperInvariant();

            var fields = name.Split('-');
            if (fields.Length >= 7 && IsoRoles.TryGetValue(fields[5].Trim(), out var role)) return role;

            foreach (var t in tokens)
                if (Keywords.TryGetValue(t, out var d)) return d;
            return string.Empty;
        }

        /// <summary>The Revit category the triage severity rule understands, for a discipline.</summary>
        public static string ToOst(string discipline)
        {
            switch ((discipline ?? string.Empty).ToUpperInvariant())
            {
                case "S": return "OST_StructuralFraming";
                case "M": return "OST_DuctCurves";
                case "P": return "OST_PipeCurves";
                case "E": return "OST_ElectricalEquipment";
                case "FP": return "OST_Sprinklers";
                default: return string.Empty;   // architectural / unknown: non-structural, non-services
            }
        }

        public static string Ost(string documentName, IReadOnlyDictionary<string, string> projectMap = null)
            => ToOst(Discipline(documentName, projectMap));
    }
}
