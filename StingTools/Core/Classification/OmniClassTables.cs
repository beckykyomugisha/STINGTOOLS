// ══════════════════════════════════════════════════════════════════════════
//  OmniClassTables.cs — Phase 199. OmniClass table metadata (host-free).
//
//  OmniClass has 15 tables; the BOQ-relevant ones are Table 21 (Elements),
//  Table 23 (Products) and Table 13 (Spaces by Function). This registry tells
//  the assigner + BOQ each table's human name, the corporate map file to load,
//  and — critically — whether the table classifies ELEMENTS or SPACES.
//
//  Element tables (21 / 23) resolve from the element's own category/family/type/
//  sys. Spatial tables (13) classify the element's HOST ROOM by function, so the
//  assigner feeds the room name into the resolver instead. Either way the result
//  is written to ASS_OMNICLASS_TXT and the code self-describes its table by its
//  numeric prefix ("21-…", "13-…", "23-…").
//
//  No Autodesk.Revit references — unit-testable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Classification
{
    public sealed class OmniClassTableInfo
    {
        public string Number;     // "21"
        public string Name;       // "Elements"
        public string MapFile;    // "STING_OMNICLASS_21_MAP.csv"

        /// <summary>What the assigner matches on for this table: "element" (the element's
        /// own category/family/type/sys — default), "room" (the host room name, for Spaces
        /// tables) or "material" (the element's material name, for the Materials table). A
        /// map may override this with a "# matchOn:" header directive.</summary>
        public string MatchMode = "element";

        /// <summary>true ⇒ classify the host room, not the element (Spaces tables 13/14).</summary>
        public bool IsSpatial => string.Equals(MatchMode, "room", StringComparison.OrdinalIgnoreCase);

        /// <summary>e.g. "Table 21 — Elements".</summary>
        public string Label => $"Table {Number} — {Name}";
    }

    public static class OmniClassTables
    {
        // The full OmniClass table set (there is NO table 24-28; the BOQ-relevant
        // ones are 21/23/41 — element/material axes — and 13/14 — spatial axes).
        // Only Spaces tables (13, 14) classify the host ROOM; every other table
        // classifies the element itself. Any number not listed here still resolves
        // (Resolve synthesises a generic non-spatial entry) so an overlay table works.
        private static readonly Dictionary<string, OmniClassTableInfo> Known =
            new Dictionary<string, OmniClassTableInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["11"] = new OmniClassTableInfo { Number = "11", Name = "Construction Entities by Function", MapFile = "STING_OMNICLASS_11_MAP.csv" },
            ["12"] = new OmniClassTableInfo { Number = "12", Name = "Construction Entities by Form",     MapFile = "STING_OMNICLASS_12_MAP.csv" },
            ["13"] = new OmniClassTableInfo { Number = "13", Name = "Spaces by Function",  MatchMode = "room",     MapFile = "STING_OMNICLASS_13_MAP.csv" },
            ["14"] = new OmniClassTableInfo { Number = "14", Name = "Spaces by Form",      MatchMode = "room",     MapFile = "STING_OMNICLASS_14_MAP.csv" },
            ["21"] = new OmniClassTableInfo { Number = "21", Name = "Elements",            MapFile = "STING_OMNICLASS_21_MAP.csv" },
            ["22"] = new OmniClassTableInfo { Number = "22", Name = "Work Results",        MapFile = "STING_OMNICLASS_22_MAP.csv" },
            ["23"] = new OmniClassTableInfo { Number = "23", Name = "Products",            MapFile = "STING_OMNICLASS_23_MAP.csv" },
            ["31"] = new OmniClassTableInfo { Number = "31", Name = "Phases",              MapFile = "STING_OMNICLASS_31_MAP.csv" },
            ["32"] = new OmniClassTableInfo { Number = "32", Name = "Services",            MapFile = "STING_OMNICLASS_32_MAP.csv" },
            ["33"] = new OmniClassTableInfo { Number = "33", Name = "Disciplines",         MapFile = "STING_OMNICLASS_33_MAP.csv" },
            ["34"] = new OmniClassTableInfo { Number = "34", Name = "Organizational Roles",MapFile = "STING_OMNICLASS_34_MAP.csv" },
            ["35"] = new OmniClassTableInfo { Number = "35", Name = "Tools",               MapFile = "STING_OMNICLASS_35_MAP.csv" },
            ["36"] = new OmniClassTableInfo { Number = "36", Name = "Information",         MapFile = "STING_OMNICLASS_36_MAP.csv" },
            ["41"] = new OmniClassTableInfo { Number = "41", Name = "Materials",           MatchMode = "material", MapFile = "STING_OMNICLASS_41_MAP.csv" },
            ["49"] = new OmniClassTableInfo { Number = "49", Name = "Properties",          MapFile = "STING_OMNICLASS_49_MAP.csv" },
        };

        /// <summary>Resolve a table number ("13"/"21"/…) to its metadata, defaulting
        /// to Table 21 (Elements) for an unknown number — and synthesising a generic
        /// (non-spatial) info for any other numeric table so an overlay table still
        /// works.</summary>
        public static OmniClassTableInfo Resolve(string tableNumber)
        {
            string n = (tableNumber ?? "").Trim();
            if (string.IsNullOrEmpty(n)) n = "21";
            if (Known.TryGetValue(n, out var info)) return info;
            return new OmniClassTableInfo
            {
                Number = n,
                Name = $"Table {n}",
                MapFile = $"STING_OMNICLASS_{n}_MAP.csv"
            };
        }

        /// <summary>The tables that ship a corporate STING_OMNICLASS_&lt;n&gt;_MAP.csv out of
        /// the box (so OmniClass_Assign / Audit work with no project overlay). Other tables
        /// resolve but need their map supplied via _BIM_COORD/omniclass_map.csv.</summary>
        public static readonly HashSet<string> MappedTableNumbers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "21", "23", "41", "13" };

        /// <summary>All known OmniClass tables, ordered by number, for a UI selector.</summary>
        public static IReadOnlyList<OmniClassTableInfo> All =>
            Known.Values.OrderBy(t => int.TryParse(t.Number, out int n) ? n : 999).ToList();

        /// <summary>True if the table ships a corporate map.</summary>
        public static bool ShipsMap(string number) => MappedTableNumbers.Contains((number ?? "").Trim());

        /// <summary>The table number a code belongs to, read from its numeric prefix
        /// ("21-04 30 00" → "21"). Empty when the code has no leading number.</summary>
        public static string TableOf(string omniClassCode)
        {
            if (string.IsNullOrWhiteSpace(omniClassCode)) return "";
            string s = omniClassCode.Trim();
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            return i > 0 ? s.Substring(0, i) : "";
        }
    }
}
