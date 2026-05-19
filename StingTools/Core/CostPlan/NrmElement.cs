// ══════════════════════════════════════════════════════════════════════════
//  NrmElement.cs — NRM1 elemental cost-plan structure.
//
//  NRM1 (RICS New Rules of Measurement 1) defines an elemental cost-plan
//  hierarchy used at RIBA Stages 1–3 to produce a cost estimate from
//  benchmark £/m² GIFA rates × building gross internal floor area.
//
//  Hierarchy (NRM1 §4):
//    Group 0 — Facilitating works
//    Group 1 — Substructure
//    Group 2 — Superstructure
//      2.1 Frame
//      2.2 Upper floors
//      2.3 Roof
//      2.4 Stairs and ramps
//      2.5 External walls
//      2.6 Windows and external doors
//      2.7 Internal walls and partitions
//      2.8 Internal doors
//    Group 3 — Internal finishes
//      3.1 Wall finishes / 3.2 Floor / 3.3 Ceiling
//    Group 4 — Fittings, furnishings and equipment
//    Group 5 — Services
//      5.1 Sanitary / 5.2 Services equipment / 5.3 Disposal /
//      5.4 Water / 5.5 Heat source / 5.6 Space heating + AC /
//      5.7 Ventilation / 5.8 Electrical / 5.9 Fuel / 5.10 Lift +
//      conveyor / 5.11 Fire and lightning / 5.12 Communications /
//      5.13 Special / 5.14 Builder's work / 5.15 Engineer's testing
//    Group 6 — Pre-fabricated buildings / units
//    Group 7 — Work to existing building
//    Group 8 — External works
//    Group 9 — Main contractor's preliminaries
//    Group 10 — Main contractor's OH+P
//    Group 11 — Project / design team fees
//    Group 12 — Other development / project costs
//    Group 13 — Risk allowances
//    Group 14 — Inflation
//
//  P4 of the Cost Management Implementation Plan.
// ══════════════════════════════════════════════════════════════════════════
using System.Collections.Generic;

namespace StingTools.Core.CostPlan
{
    /// <summary>One node in the NRM1 cost-plan element tree.</summary>
    public class NrmElement
    {
        /// <summary>"1", "2.1", "5.6" etc. — NRM1 hierarchical code.</summary>
        public string Code { get; set; } = "";

        /// <summary>Human label — "Substructure", "Frame", "Space heating + AC".</summary>
        public string Name { get; set; } = "";

        /// <summary>Direct parent code; empty for top-level groups.</summary>
        public string ParentCode { get; set; } = "";

        /// <summary>True if this is a measurable element (leaf), false for groups.</summary>
        public bool IsLeaf { get; set; } = false;

        /// <summary>Display order within the parent group.</summary>
        public int SortOrder { get; set; }
    }

    /// <summary>
    /// Static catalogue of the NRM1 element tree. The structure is
    /// fixed by the RICS standard; benchmarks per element vary by
    /// building type and live in <see cref="CostPlanRegistry"/>.
    /// </summary>
    public static class NrmElementCatalog
    {
        private static List<NrmElement> _elements;
        private static readonly object _lock = new object();

        public static IReadOnlyList<NrmElement> Elements
        {
            get
            {
                if (_elements != null) return _elements;
                lock (_lock)
                {
                    if (_elements != null) return _elements;
                    _elements = BuildCatalog();
                    return _elements;
                }
            }
        }

        /// <summary>Find an element by NRM1 code. Returns null if not found.</summary>
        public static NrmElement Find(string code)
        {
            foreach (var e in Elements)
                if (string.Equals(e.Code, code, System.StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        private static List<NrmElement> BuildCatalog()
        {
            // Authored from RICS New Rules of Measurement 1 (2nd ed., 2021).
            // Top-level groups only — sub-elements can be added per project
            // override; the engine handles arbitrary depth via ParentCode.
            return new List<NrmElement>
            {
                G("0",  "Facilitating works",                 1),
                G("1",  "Substructure",                       2),
                G("2",  "Superstructure",                     3),
                L("2.1","Frame",                       "2",   1),
                L("2.2","Upper floors",                "2",   2),
                L("2.3","Roof",                        "2",   3),
                L("2.4","Stairs and ramps",            "2",   4),
                L("2.5","External walls",              "2",   5),
                L("2.6","Windows and external doors",  "2",   6),
                L("2.7","Internal walls and partitions","2",  7),
                L("2.8","Internal doors",              "2",   8),
                G("3",  "Internal finishes",                  4),
                L("3.1","Wall finishes",               "3",   1),
                L("3.2","Floor finishes",              "3",   2),
                L("3.3","Ceiling finishes",            "3",   3),
                G("4",  "Fittings, furnishings and equipment", 5),
                G("5",  "Services",                           6),
                L("5.1","Sanitary installations",      "5",   1),
                L("5.2","Services equipment",          "5",   2),
                L("5.3","Disposal installations",      "5",   3),
                L("5.4","Water installations",         "5",   4),
                L("5.5","Heat source",                 "5",   5),
                L("5.6","Space heating and air conditioning","5", 6),
                L("5.7","Ventilation",                 "5",   7),
                L("5.8","Electrical installations",    "5",   8),
                L("5.9","Fuel installations",          "5",   9),
                L("5.10","Lift and conveyor installations","5",10),
                L("5.11","Fire and lightning protection","5", 11),
                L("5.12","Communications and security","5",   12),
                L("5.13","Special installations",      "5",   13),
                L("5.14","Builder's work in connection","5",  14),
                L("5.15","Testing and commissioning",  "5",   15),
                G("6",  "Pre-fabricated buildings and units", 7),
                G("7",  "Work to existing building",          8),
                G("8",  "External works",                     9),
                G("9",  "Main contractor's preliminaries",   10),
                G("10", "Main contractor's overheads and profit", 11),
                G("11", "Project and design team fees",      12),
                G("12", "Other development and project costs",13),
                G("13", "Risk allowances",                   14),
                G("14", "Inflation",                         15)
            };
        }

        private static NrmElement G(string code, string name, int sort) =>
            new NrmElement { Code = code, Name = name, IsLeaf = false, SortOrder = sort };

        private static NrmElement L(string code, string name, string parent, int sort) =>
            new NrmElement { Code = code, Name = name, ParentCode = parent, IsLeaf = true, SortOrder = sort };
    }
}
